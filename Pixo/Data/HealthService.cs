using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.Json;
using System.Threading;
using Microsoft.Win32;

namespace Pixo.Data
{
    public enum HealthLevel { Ok, Warn, Bad }

    public class HealthItem
    {
        public string Id, Icon, Title, Detail;
        public HealthLevel Level;
    }

    /// <summary>
    /// Saúde do PC: espaço e estado dos discos, há quanto tempo o PC está ligado, atualização do Windows esperando
    /// reiniciar e a temperatura da CPU comparada com a de costume (média por dia, guardada em saude.json).
    /// Verifica a cada 10 minutos; a temperatura é anotada a cada minuto.
    /// </summary>
    public static class HealthService
    {
        private static Timer _check, _sample;
        private static int _busy;
        private static readonly object Lock = new object();
        private static Dictionary<string, double[]> _days;        // "2026-11-25" → [soma, quantidade]
        private static readonly Dictionary<string, DateTime> Notified = new Dictionary<string, DateTime>();

        public static volatile HealthItem[] Items = Array.Empty<HealthItem>();

        /// <summary>Um aviso novo (no máximo um por item por dia): o texto para a dupla falar.</summary>
        public static event Action<string> NewWarning;

        public static int Warnings => Items.Count(i => i.Level != HealthLevel.Ok);

        private static string FilePath => Path.Combine(Core.Config.Folder, "saude.json");

        public static void Start()
        {
            LoadDays();
            _sample = new Timer(_ => Sample(), null, 30_000, 60_000);
            _check = new Timer(_ => Check(), null, 20_000, 600_000);
        }

        public static void Stop()
        {
            _sample?.Dispose();
            _check?.Dispose();
            SaveDays();
        }

        public static void CheckNow() => ThreadPool.QueueUserWorkItem(_ => Check());

        private static void Check()
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            try
            {
                var list = new List<HealthItem>();
                Safe(list, DiskSpace);
                Safe(list, DiskHealth);
                Safe(list, Uptime);
                Safe(list, WindowsUpdate);
                Safe(list, CpuTrend);
                Items = list.ToArray();

                foreach (var i in list.Where(i => i.Level != HealthLevel.Ok))
                {
                    if (Notified.TryGetValue(i.Id, out var when) && when.Date == DateTime.Today) continue;
                    Notified[i.Id] = DateTime.Now;
                    NewWarning?.Invoke(i.Title + ": " + i.Detail);
                }
            }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }

        private static void Safe(List<HealthItem> list, Func<HealthItem> f)
        {
            try { var i = f(); if (i != null) list.Add(i); } catch { }
        }

        // ---------------- verificações ----------------

        /// <summary>Espaço livre: aviso abaixo de 10% (ou 20 GB), alerta abaixo de 5% (ou 5 GB).</summary>
        private static HealthItem DiskSpace()
        {
            var drives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady && d.TotalSize > 8L << 30).ToList();
            if (drives.Count == 0) return null;
            HealthLevel Level(DriveInfo d)
            {
                double free = d.AvailableFreeSpace, pct = free / d.TotalSize;
                return pct < 0.05 || free < 5.0 * (1L << 30) ? HealthLevel.Bad : pct < 0.10 || free < 20.0 * (1L << 30) ? HealthLevel.Warn : HealthLevel.Ok;
            }
            var worst = drives.OrderByDescending(Level).ThenBy(d => (double)d.AvailableFreeSpace / d.TotalSize).First();
            var lvl = Level(worst);
            string Free(DriveInfo d) => $"{d.Name.TrimEnd('\\')} {Size(d.AvailableFreeSpace)} livres";
            return new HealthItem
            {
                Id = "disco", Icon = "disk", Level = lvl,
                Title = lvl == HealthLevel.Ok ? "Espaço em disco" : $"Disco {worst.Name.TrimEnd('\\')} quase cheio",
                Detail = lvl == HealthLevel.Ok
                    ? string.Join(" · ", drives.Take(3).Select(Free))
                    : $"Só {Size(worst.AvailableFreeSpace)} livres ({(double)worst.AvailableFreeSpace / worst.TotalSize:0%}) de {Size(worst.TotalSize)}",
            };
        }

        /// <summary>Estado dos discos pelo próprio Windows (MSFT_PhysicalDisk: saudável / aviso / com defeito).</summary>
        private static HealthItem DiskHealth()
        {
            using var s = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage", "SELECT FriendlyName, HealthStatus FROM MSFT_PhysicalDisk");
            var disks = s.Get().Cast<ManagementObject>()
                .Select(o => (name: o["FriendlyName"]?.ToString() ?? "Disco", health: Convert.ToInt32(o["HealthStatus"] ?? 5)))
                .ToList();
            if (disks.Count == 0) return null;
            var bad = disks.Where(d => d.health == 2).ToList();
            var warn = disks.Where(d => d.health == 1).ToList();
            if (bad.Count > 0)
                return new HealthItem { Id = "smart", Icon = "alert", Level = HealthLevel.Bad, Title = "Disco com defeito", Detail = $"{bad[0].name}: faça backup logo" };
            if (warn.Count > 0)
                return new HealthItem { Id = "smart", Icon = "alert", Level = HealthLevel.Warn, Title = "Disco com aviso", Detail = $"{warn[0].name}: o Windows viu sinais de desgaste" };
            return new HealthItem
            {
                Id = "smart", Icon = "check", Level = HealthLevel.Ok, Title = "Estado dos discos",
                Detail = disks.Count == 1 ? "1 disco saudável" : $"{disks.Count} discos saudáveis",
            };
        }

        /// <summary>Ligado há muitos dias: aviso a partir de 7.</summary>
        private static HealthItem Uptime()
        {
            var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
            string text = up.TotalDays >= 1 ? $"Ligado há {(int)up.TotalDays} dia{((int)up.TotalDays == 1 ? "" : "s")}"
                        : up.TotalHours >= 1 ? $"Ligado há {(int)up.TotalHours} h" : $"Ligado há {(int)up.TotalMinutes} min";
            bool old = up.TotalDays >= 7;
            return new HealthItem
            {
                Id = "reinicio", Icon = "restart", Level = old ? HealthLevel.Warn : HealthLevel.Ok,
                Title = old ? "Hora de reiniciar" : "Reinício",
                Detail = old ? text + " sem reiniciar. Reiniciar deixa o Windows mais leve." : text,
            };
        }

        /// <summary>Atualização do Windows instalada e esperando reiniciar.</summary>
        private static HealthItem WindowsUpdate()
        {
            bool pending;
            using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired"))
                pending = k != null;
            return new HealthItem
            {
                Id = "update", Icon = "refresh", Level = pending ? HealthLevel.Warn : HealthLevel.Ok,
                Title = "Atualização do Windows",
                Detail = pending ? "Instalada, esperando você reiniciar" : "Nada esperando reiniciar",
            };
        }

        /// <summary>CPU mais quente que de costume: média dos últimos 3 dias contra a dos 4 a 30 dias antes.</summary>
        private static HealthItem CpuTrend()
        {
            Dictionary<string, double[]> days;
            lock (Lock) days = _days.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
            double? Avg(IEnumerable<KeyValuePair<string, double[]>> d)
            {
                var l = d.Where(kv => kv.Value[1] >= 30).ToList(); // dia com pelo menos 30 minutos de leitura
                return l.Count == 0 ? null : l.Sum(kv => kv.Value[0]) / l.Sum(kv => kv.Value[1]);
            }
            var today = DateTime.Today;
            var recent = days.Where(kv => Age(kv.Key, today) is int a && a <= 2).ToList();
            var before = days.Where(kv => Age(kv.Key, today) is int a && a >= 3 && a <= 30).ToList();
            double? r = Avg(recent), b = Avg(before);
            if (r == null) return new HealthItem { Id = "cpu", Icon = "thermo", Level = HealthLevel.Ok, Title = "Temperatura da CPU", Detail = "Anotando a média do dia…" };
            if (b == null || before.Count(kv => kv.Value[1] >= 30) < 3)
                return new HealthItem { Id = "cpu", Icon = "thermo", Level = HealthLevel.Ok, Title = "Temperatura da CPU", Detail = $"Média recente {r:0}° (comparando depois de alguns dias)" };
            double diff = r.Value - b.Value;
            bool hot = diff >= 7;
            return new HealthItem
            {
                Id = "cpu", Icon = "thermo", Level = hot ? HealthLevel.Warn : HealthLevel.Ok,
                Title = hot ? "CPU mais quente que de costume" : "Temperatura da CPU",
                Detail = hot ? $"{r:0}° de média, {diff:0}° acima do normal. Poeira no cooler?" : $"Média {r:0}°, normal para este PC",
            };
        }

        private static int? Age(string day, DateTime today) =>
            DateTime.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? (int)(today - d).TotalDays : null;

        // ---------------- média de temperatura por dia ----------------

        private static int _samples;

        private static void Sample()
        {
            var t = HardwareService.Current?.Cpu?.Temp;
            if (t == null || t <= 0) return;
            string key = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            lock (Lock)
            {
                if (!_days.TryGetValue(key, out var v)) _days[key] = v = new double[2];
                v[0] += t.Value;
                v[1] += 1;
                foreach (var old in _days.Keys.Where(k => Age(k, DateTime.Today) is int a && a > 31).ToList()) _days.Remove(old);
            }
            if (++_samples % 15 == 0) SaveDays(); // grava a cada 15 minutos
        }

        private static void LoadDays()
        {
            lock (Lock)
            {
                try { _days = File.Exists(FilePath) ? JsonSerializer.Deserialize<Dictionary<string, double[]>>(File.ReadAllText(FilePath)) : null; }
                catch { _days = null; }
                _days ??= new Dictionary<string, double[]>();
            }
        }

        private static void SaveDays()
        {
            try
            {
                string json;
                lock (Lock) json = JsonSerializer.Serialize(_days);
                Directory.CreateDirectory(Core.Config.Folder);
                File.WriteAllText(FilePath, json);
            }
            catch { }
        }

        private static string Size(double bytes) =>
            bytes >= 1L << 40 ? (bytes / (1L << 40)).ToString("0.0", CultureInfo.GetCultureInfo("pt-BR")) + " TB"
                              : (bytes / (1L << 30)).ToString("0", CultureInfo.GetCultureInfo("pt-BR")) + " GB";

        /// <summary>Só para testes: troca as médias por dia.</summary>
        public static void SetDaysForTest(Dictionary<string, double[]> days) { lock (Lock) _days = days; }
        public static void CheckForTest() => Check();
    }
}
