using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Pixo.Core;

namespace Pixo.Data
{
    public class GameInfo
    {
        public string Name;
        public int Pid;
        public double? Fps;
        public double? FrameMs;
        public double? MinFps;   // menor FPS dos últimos ~60 s
    }

    /// <summary>
    /// Detecta jogo em primeiro plano, mede FPS pelo RivaTuner (RTSS) e
    /// informa há quanto tempo o PC está sem uso (teclado/mouse).
    /// </summary>
    public static class GameService
    {
        private static Timer _timer;
        private static Func<Config> _cfg;
        private static readonly Queue<double> FpsHist = new Queue<double>();
        private static int _histPid;

        public static volatile GameInfo Current;

        /// <summary>Caminho do .exe do app em primeiro plano agora (null se não deu para ler).</summary>
        public static volatile string ForegroundExe;

        private static readonly string[] GameFolders =
        {
            @"steamapps\common", @"epic games", @"riot games", @"gog galaxy\games", @"xboxgames",
            @"ea games", @"ubisoft game launcher\games", @"battle.net", @"rockstar games", @"\games\"
        };
        private static readonly string[] NotGames =
        {
            "launcher", "steamwebhelper", "crashhandler", "easyanticheat", "battleye", "riotclient",
            "epicgameslauncher", "explorer.exe", "chrome.exe", "msedge.exe", "firefox.exe", "discord.exe", "pixo.exe"
        };

        public static void Start(Func<Config> cfg)
        {
            _cfg = cfg;
            _timer = new Timer(_ => Tick(), null, 1000, 1000);
        }

        public static void Stop() => _timer?.Dispose();

        private static void Tick()
        {
            try
            {
                var fg = Foreground();
                ForegroundExe = fg.exe;
                Current = Detect(fg);
            }
            catch { Current = null; }
        }

        private static GameInfo Detect((int pid, string exe) fg)
        {
            var (pid, exe) = fg;
            if (pid == 0 || string.IsNullOrEmpty(exe)) return null;
            var path = exe.ToLowerInvariant();
            var file = Path.GetFileName(path);
            if (NotGames.Any(n => file.Contains(n))) return null;

            var rtss = ReadRtss();
            var r = rtss.FirstOrDefault(e => e.Pid == pid);
            // o jogo pode desenhar por outro processo (launcher, processo filho):
            // nesse caso usa a entrada do RivaTuner que está ativa agora
            if (r.Fps <= 0)
                r = rtss.Where(e => e.Fps > 0 && e.Active).OrderByDescending(e => e.Fps).FirstOrDefault();

            // reserva: sensor de FPS da própria placa de vídeo (AMD informa "Fullscreen FPS")
            double? gpuFps = HardwareService.Current?.Gpu?.Fps;

            bool isGame = GameFolders.Any(g => path.Contains(g))
                          || (_cfg?.Invoke()?.ExtraGamesList().Contains(file) ?? false)
                          || r.Fps > 0
                          || gpuFps > 0;
            WriteDiag(pid, exe, isGame, rtss, gpuFps);
            if (!isGame) return null;

            // principal: sensor da placa de vídeo; reserva: RivaTuner
            double? fps = gpuFps > 0 ? gpuFps : r.Fps > 0 ? r.Fps : (double?)null;
            bool fromGpu = gpuFps > 0;
            if (pid != _histPid) { FpsHist.Clear(); _histPid = pid; }
            if (fps != null)
            {
                FpsHist.Enqueue(fps.Value);
                while (FpsHist.Count > 60) FpsHist.Dequeue();
            }
            return new GameInfo
            {
                Name = PrettyName(exe),
                Pid = pid,
                Fps = fps,
                FrameMs = !fromGpu && r.FrameMs > 0 ? r.FrameMs : fps > 0 ? 1000.0 / fps : (double?)null,
                MinFps = FpsHist.Count > 3 ? FpsHist.Min() : (double?)null
            };
        }

        private static string PrettyName(string exe)
        {
            var parts = exe.Replace('/', '\\').Split('\\');
            foreach (var marker in new[] { "common", "Epic Games", "Riot Games", "Games", "XboxGames" })
            {
                int i = Array.FindIndex(parts, p => p.Equals(marker, StringComparison.OrdinalIgnoreCase));
                if (i >= 0 && i + 1 < parts.Length - 1) return parts[i + 1];
            }
            return Path.GetFileNameWithoutExtension(exe);
        }

        // ---------------- RivaTuner Statistics Server ----------------

        public struct RtssEntry
        {
            public int Pid;
            public string Name;
            public double Fps, FrameMs;
            public bool Active;   // atualizado nos últimos segundos
        }

        private static string _rtssStatus = "";

        private static List<RtssEntry> ReadRtss()
        {
            var result = new List<RtssEntry>();
            try
            {
                using var mmf = MemoryMappedFile.OpenExisting("RTSSSharedMemoryV2", MemoryMappedFileRights.Read);
                using var view = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                uint sig = view.ReadUInt32(0);
                uint ver = view.ReadUInt32(4);
                uint entrySize = view.ReadUInt32(8);
                uint arrOffset = view.ReadUInt32(12);
                uint arrSize = view.ReadUInt32(16);
                _rtssStatus = $"memória do RivaTuner aberta: assinatura 0x{sig:X8}, versão 0x{ver:X8}, {arrSize} entradas de {entrySize} bytes";
                if (sig != 0x52545353) return result; // 'RTSS' (diferente disso = RTSS fechando ou incompatível)
                uint now = unchecked((uint)Environment.TickCount);
                for (uint i = 0; i < arrSize; i++)
                {
                    long b = arrOffset + (long)i * entrySize;
                    if (b + 284 > view.Capacity) break;
                    int pid = (int)view.ReadUInt32(b);
                    if (pid == 0) continue;
                    // depois do pid: nome (260 bytes), flags, time0, time1, frames, frameTime(µs)
                    var nameBytes = new byte[260];
                    view.ReadArray(b + 4, nameBytes, 0, 260);
                    int z = Array.IndexOf(nameBytes, (byte)0);
                    string name = Encoding.Default.GetString(nameBytes, 0, z < 0 ? 260 : z);
                    uint t0 = view.ReadUInt32(b + 268);
                    uint t1 = view.ReadUInt32(b + 272);
                    uint frames = view.ReadUInt32(b + 276);
                    uint frameTime = view.ReadUInt32(b + 280);
                    double fps = t1 > t0 ? 1000.0 * frames / (t1 - t0) : 0;
                    // a contagem de quadros também dá o FPS pelo tempo de quadro, se o intervalo vier zerado
                    if (fps <= 0 && frameTime > 0) fps = 1_000_000.0 / frameTime;
                    result.Add(new RtssEntry
                    {
                        Pid = pid, Name = name, Fps = fps, FrameMs = frameTime / 1000.0,
                        Active = unchecked(now - t1) < 5000
                    });
                }
            }
            catch (FileNotFoundException) { _rtssStatus = "RivaTuner não encontrado (memória compartilhada não existe: ele está aberto?)"; }
            catch (Exception ex) { _rtssStatus = "erro ao ler o RivaTuner: " + ex.GetType().Name + " - " + ex.Message; }
            return result;
        }

        // ---------------- diagnóstico (jogo.txt ao lado do Pixo.exe) ----------------

        private static DateTime _lastDiag = DateTime.MinValue;

        private static void WriteDiag(int pid, string exe, bool isGame, List<RtssEntry> rtss, double? gpuFps)
        {
            if ((DateTime.Now - _lastDiag).TotalSeconds < 3) return;
            _lastDiag = DateTime.Now;
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Pixo - diagnóstico de jogo/FPS em " + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"));
                sb.AppendLine("Janela em primeiro plano: pid " + pid + " | " + exe);
                sb.AppendLine("Considerado jogo: " + (isGame ? "sim" : "não"));
                sb.AppendLine("FPS da placa de vídeo (sensor): " + (gpuFps?.ToString("0.0") ?? "(nenhum)"));
                sb.AppendLine(_rtssStatus);
                foreach (var e in rtss)
                    sb.AppendLine($"  RTSS pid {e.Pid} | {e.Name} | {e.Fps:0.0} fps | {e.FrameMs:0.00} ms | ativo: {(e.Active ? "sim" : "não")}");
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "jogo.txt"), sb.ToString());
            }
            catch { }
        }

        // ---------------- Win32 ----------------

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder name, ref int size);

        private static (int pid, string exe) Foreground()
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return (0, null);
            GetWindowThreadProcessId(hwnd, out var pid);
            var h = OpenProcess(0x1000 /* QUERY_LIMITED_INFORMATION */, false, pid);
            if (h == IntPtr.Zero) return ((int)pid, null);
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref size) ? ((int)pid, sb.ToString()) : ((int)pid, null);
            }
            finally { CloseHandle(h); }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

        /// <summary>Segundos sem mexer no teclado/mouse.</summary>
        public static double IdleSeconds()
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (!GetLastInputInfo(ref info)) return 0;
            return unchecked((uint)Environment.TickCount - info.dwTime) / 1000.0;
        }
    }
}
