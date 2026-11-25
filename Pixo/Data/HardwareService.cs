using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using LibreHardwareMonitor.Hardware;

namespace Pixo.Data
{
    public class HwComponent
    {
        public string Kind;      // "CPU", "GPU", "Placa-mãe", "SSD"
        public string Name;
        public double? Temp;
        public double? Load;
        public double? VramUsedGb;
        public double? VramTotalGb;
        public double? Fps;      // FPS informado pelo driver da placa de vídeo (AMD: "Fullscreen FPS")
    }

    public class HwSnapshot
    {
        public HwComponent Cpu, Gpu, Board, Ssd;
        public double? CpuLoad;
        public double? RamLoad, RamUsedGb, RamTotalGb;
        public string Error;
        public bool CpuTempMissing;
        public static readonly HwSnapshot Empty = new HwSnapshot { Error = "Lendo sensores…" };
    }

    /// <summary>
    /// Lê temperaturas e uso pelo LibreHardwareMonitorLib (dentro do próprio app).
    /// Precisa de administrador. Atualiza a cada 2 s em segundo plano.
    /// </summary>
    public static class HardwareService
    {
        private static Computer _computer;
        private static Timer _timer;
        private static int _busy;

        public static volatile HwSnapshot Current = HwSnapshot.Empty;

        private static readonly string[] CpuPreferred = { "CPU Package", "Core (Tctl/Tdie)", "Tctl/Tdie", "Core Average", "Core Max", "CPU Total" };
        private static readonly string[] GpuPreferred = { "GPU Core", "GPU Hot Spot" };
        private static readonly string[] SsdPreferred = { "Composite Temperature", "Temperature", "Temperature 1" };
        private static readonly string[] BoardPreferred = { "System", "Motherboard", "Mainboard", "SYSTIN", "MB" };

        public static void Start()
        {
            _timer = new Timer(_ => Update(), null, 0, 1000);
        }

        public static void Stop()
        {
            _timer?.Dispose();
            try { _computer?.Close(); } catch { }
        }

        private static void Update()
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            try
            {
                if (_computer == null)
                {
                    _computer = new Computer
                    {
                        IsCpuEnabled = true,
                        IsGpuEnabled = true,
                        IsMotherboardEnabled = true,
                        IsStorageEnabled = true,
                        IsMemoryEnabled = true
                    };
                    _computer.Open();
                }

                var snap = new HwSnapshot();
                HwComponent intelGpu = null;

                foreach (var hw in _computer.Hardware)
                {
                    hw.Update();
                    foreach (var sub in hw.SubHardware) sub.Update();
                    var sensors = hw.Sensors.Concat(hw.SubHardware.SelectMany(s => s.Sensors)).ToList();

                    switch (hw.HardwareType)
                    {
                        case HardwareType.Cpu:
                            if (snap.Cpu != null) break;
                            snap.Cpu = new HwComponent
                            {
                                Kind = "CPU", Name = hw.Name,
                                Temp = Pick(sensors, SensorType.Temperature, CpuPreferred),
                                Load = Value(sensors, SensorType.Load, "CPU Total")
                            };
                            snap.CpuLoad = snap.Cpu.Load;
                            break;

                        case HardwareType.GpuNvidia:
                        case HardwareType.GpuAmd:
                        case HardwareType.GpuIntel:
                            var used = Value(sensors, SensorType.SmallData, "GPU Memory Used");
                            var total = Value(sensors, SensorType.SmallData, "GPU Memory Total");
                            var gpu = new HwComponent
                            {
                                Kind = "GPU", Name = hw.Name,
                                Temp = Pick(sensors, SensorType.Temperature, GpuPreferred),
                                Load = Value(sensors, SensorType.Load, "GPU Core"),
                                VramUsedGb = used / 1024,
                                VramTotalGb = total / 1024,
                                Fps = Value(sensors, SensorType.Factor, "Fullscreen FPS")
                            };
                            // prefere a placa de vídeo dedicada à integrada
                            if (hw.HardwareType == HardwareType.GpuIntel) intelGpu ??= gpu;
                            else snap.Gpu ??= gpu;
                            break;

                        case HardwareType.Motherboard:
                            if (snap.Board != null) break;
                            snap.Board = new HwComponent
                            {
                                Kind = "Placa-mãe", Name = hw.Name,
                                Temp = PickContains(sensors, SensorType.Temperature, BoardPreferred)
                            };
                            break;

                        case HardwareType.Storage:
                            var t = Pick(sensors, SensorType.Temperature, SsdPreferred);
                            if (t != null && snap.Ssd == null)
                                snap.Ssd = new HwComponent { Kind = "SSD", Name = hw.Name, Temp = t };
                            break;

                    }
                }
                snap.Gpu ??= intelGpu;

                // RAM física direto do Windows (o LHM também lista a memória virtual, que soma o arquivo de paginação)
                var ms = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
                if (GlobalMemoryStatusEx(ref ms) && ms.ullTotalPhys > 0)
                {
                    double gb = 1024.0 * 1024 * 1024;
                    snap.RamTotalGb = ms.ullTotalPhys / gb;
                    snap.RamUsedGb = (ms.ullTotalPhys - ms.ullAvailPhys) / gb;
                    snap.RamLoad = 100.0 * (ms.ullTotalPhys - ms.ullAvailPhys) / ms.ullTotalPhys;
                }

                // a CPU existe mas nenhum sensor de temperatura dela foi lido: falta o driver
                snap.CpuTempMissing = snap.Cpu != null && snap.Cpu.Temp == null;

                bool anyTemp = new[] { snap.Cpu, snap.Gpu, snap.Board, snap.Ssd }.Any(c => c?.Temp != null);
                snap.Error = anyTemp ? null : "Sem sensores";
                Current = snap;

                WriteReport();
            }
            catch (Exception)
            {
                Current = new HwSnapshot { Error = "Sem sensores" };
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        }

        // ---------------- diagnóstico ----------------

        private static DateTime _lastReport = DateTime.MinValue;

        /// <summary>
        /// Salva a lista de todos os sensores encontrados em "sensores.txt", ao lado do Pixo.exe,
        /// a cada 30 s. Serve para descobrir por que algum sensor não aparece.
        /// </summary>
        private static void WriteReport()
        {
            if ((DateTime.Now - _lastReport).TotalSeconds < 30) return;
            _lastReport = DateTime.Now;
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Pixo - sensores encontrados em " + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"));
                sb.AppendLine("Administrador: " + new System.Security.Principal.WindowsPrincipal(
                    System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator));
                sb.AppendLine("PawnIO instalado: " + PawnIoInstalled());
                sb.AppendLine();
                foreach (var hw in _computer.Hardware)
                {
                    sb.AppendLine("[" + hw.HardwareType + "] " + hw.Name);
                    foreach (var s in hw.Sensors)
                        sb.AppendLine("    " + s.SensorType + " | " + s.Name + " = " + (s.Value?.ToString("0.0") ?? "(vazio)"));
                    foreach (var sub in hw.SubHardware)
                    {
                        sb.AppendLine("  [" + sub.HardwareType + "] " + sub.Name);
                        foreach (var s in sub.Sensors)
                            sb.AppendLine("      " + s.SensorType + " | " + s.Name + " = " + (s.Value?.ToString("0.0") ?? "(vazio)"));
                    }
                }
                System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "sensores.txt"), sb.ToString());
            }
            catch { }
        }

        public static bool PawnIoInstalled()
        {
            try
            {
                using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
                return k != null;
            }
            catch { return false; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

        private static double? Value(List<ISensor> sensors, SensorType type, string name)
        {
            var s = sensors.FirstOrDefault(x => x.SensorType == type && x.Name == name);
            return s?.Value;
        }

        private static double? Pick(List<ISensor> sensors, SensorType type, string[] preferred)
        {
            foreach (var p in preferred)
            {
                var v = Value(sensors, type, p);
                if (v != null && v > 0) return v;
            }
            var any = sensors.Where(x => x.SensorType == type && x.Value > 0 && x.Value < 150).Select(x => (double?)x.Value);
            return any.Any() ? any.Max() : null;
        }

        private static double? PickContains(List<ISensor> sensors, SensorType type, string[] preferred)
        {
            var temps = sensors.Where(x => x.SensorType == type && x.Value > 5 && x.Value < 120).ToList();
            foreach (var p in preferred)
            {
                var s = temps.FirstOrDefault(x => x.Name.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0);
                if (s != null) return s.Value;
            }
            return temps.FirstOrDefault()?.Value;
        }
    }
}
