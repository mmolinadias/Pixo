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
            try { Current = Detect(); }
            catch { Current = null; }
        }

        private static GameInfo Detect()
        {
            var (pid, exe) = Foreground();
            if (pid == 0 || string.IsNullOrEmpty(exe)) return null;
            var path = exe.ToLowerInvariant();
            var file = Path.GetFileName(path);
            if (NotGames.Any(n => file.Contains(n))) return null;

            var rtss = ReadRtss();
            rtss.TryGetValue(pid, out var r);
            bool isGame = GameFolders.Any(g => path.Contains(g))
                          || (_cfg?.Invoke()?.ExtraGamesList().Contains(file) ?? false)
                          || r.fps > 0;
            if (!isGame) return null;

            double? fps = r.fps > 0 ? r.fps : (double?)null;
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
                FrameMs = r.frameMs > 0 ? r.frameMs : (double?)null,
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

        private static Dictionary<int, (double fps, double frameMs)> ReadRtss()
        {
            var result = new Dictionary<int, (double, double)>();
            try
            {
                using var mmf = MemoryMappedFile.OpenExisting("RTSSSharedMemoryV2", MemoryMappedFileRights.Read);
                using var view = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                uint sig = view.ReadUInt32(0);
                if (sig != 0x52545353) return result; // 'RTSS'
                uint entrySize = view.ReadUInt32(8);
                uint arrOffset = view.ReadUInt32(12);
                uint arrSize = view.ReadUInt32(16);
                for (uint i = 0; i < arrSize; i++)
                {
                    long b = arrOffset + i * entrySize;
                    if (b + 288 > view.Capacity) break;
                    int pid = (int)view.ReadUInt32(b);
                    if (pid == 0) continue;
                    // depois do pid: nome (260 bytes), flags, time0, time1, frames, frameTime(µs)
                    uint t0 = view.ReadUInt32(b + 268);
                    uint t1 = view.ReadUInt32(b + 272);
                    uint frames = view.ReadUInt32(b + 276);
                    uint frameTime = view.ReadUInt32(b + 280);
                    if (t1 > t0) result[pid] = (1000.0 * frames / (t1 - t0), frameTime / 1000.0);
                }
            }
            catch { }
            return result;
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
