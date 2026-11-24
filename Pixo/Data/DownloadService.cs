using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;
using Pixo.Core;

namespace Pixo.Data
{
    public class DownloadItem
    {
        public string Key;
        public string Source;     // "Steam", "Navegador", "qBittorrent"
        public string Name;
        public long Done;
        public long? Total;
        public double? Speed;     // bytes/s
        public double? Pct => Total > 0 ? 100.0 * Done / Total.Value : (double?)null;
    }

    /// <summary>Downloads em andamento: Steam, navegador (.crdownload/.part) e qBittorrent.</summary>
    public static class DownloadService
    {
        private static Timer _timer;
        private static int _busy;
        private static Func<Config> _cfg;
        private static readonly Dictionary<string, (DateTime t, long bytes)> Hist = new Dictionary<string, (DateTime, long)>();
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(1.5) };
        private static HashSet<string> _prevKeys = new HashSet<string>();

        public static volatile DownloadItem[] Items = Array.Empty<DownloadItem>();

        /// <summary>Um download sumiu da lista (terminou): o Bit comemora.</summary>
        public static event Action Finished;

        public static void Start(Func<Config> cfg)
        {
            _cfg = cfg;
            _timer = new Timer(_ => Update(), null, 500, 2000);
        }

        public static void Stop() => _timer?.Dispose();

        private static void Update()
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            try
            {
                var items = new List<DownloadItem>();
                try { items.AddRange(Steam()); } catch { }
                try { items.AddRange(QBittorrent()); } catch { }
                try { items.AddRange(Browser()); } catch { }

                var keys = new HashSet<string>(items.Select(i => i.Key));
                if (_prevKeys.Count > 0 && _prevKeys.Any(k => !keys.Contains(k)))
                    Finished?.Invoke();
                _prevKeys = keys;
                Items = items.ToArray();
            }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }

        private static double? Speed(string key, long bytes)
        {
            var now = DateTime.UtcNow;
            if (!Hist.TryGetValue(key, out var prev)) { Hist[key] = (now, bytes); return null; }
            double dt = (now - prev.t).TotalSeconds;
            if (dt < 1) return null;
            if (dt >= 3) Hist[key] = (now, bytes);
            return Math.Max(0, (bytes - prev.bytes) / dt);
        }

        // ---------------- Steam ----------------

        private static IEnumerable<string> SteamLibraries()
        {
            string basePath;
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                basePath = k?.GetValue("SteamPath") as string;
            if (string.IsNullOrEmpty(basePath)) yield break;

            var libs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.GetFullPath(basePath) };
            var vdf = Path.Combine(basePath, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                    libs.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
            foreach (var l in libs) yield return Path.Combine(l, "steamapps");
        }

        private static IEnumerable<DownloadItem> Steam()
        {
            foreach (var dir in SteamLibraries())
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var file in Directory.GetFiles(dir, "appmanifest_*.acf"))
                {
                    string txt;
                    try { txt = File.ReadAllText(file); } catch { continue; }
                    string Field(string n)
                    {
                        var m = Regex.Match(txt, "\"" + n + "\"\\s+\"([^\"]*)\"");
                        return m.Success ? m.Groups[1].Value : "";
                    }
                    long.TryParse(Field("BytesToDownload"), out var total);
                    long.TryParse(Field("BytesDownloaded"), out var done);
                    if (total > 0 && done < total)
                    {
                        var key = "steam:" + Path.GetFileName(file);
                        yield return new DownloadItem
                        {
                            Key = key, Source = "Steam", Name = Field("name"),
                            Done = done, Total = total, Speed = Speed(key, done)
                        };
                    }
                }
            }
        }

        // ---------------- Navegador ----------------

        private static readonly string[] PartialExt = { ".crdownload", ".part", ".opdownload", ".partial", ".download" };

        private static IEnumerable<DownloadItem> Browser()
        {
            var folder = _cfg?.Invoke()?.DownloadsFolder;
            if (string.IsNullOrWhiteSpace(folder))
                folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (!Directory.Exists(folder)) yield break;

            foreach (var f in Directory.GetFiles(folder))
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                if (!PartialExt.Contains(ext)) continue;
                var info = new FileInfo(f);
                if ((DateTime.Now - info.LastWriteTime).TotalSeconds > 120) continue; // abandonado
                var name = Path.GetFileNameWithoutExtension(f);
                if (name.StartsWith("Não confirmado", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("Unconfirmed", StringComparison.OrdinalIgnoreCase))
                    name = "Download do navegador";
                var key = "nav:" + f;
                yield return new DownloadItem
                {
                    Key = key, Source = "Navegador", Name = name,
                    Done = info.Length, Total = null, Speed = Speed(key, info.Length)
                };
            }
        }

        // ---------------- qBittorrent ----------------

        private static IEnumerable<DownloadItem> QBittorrent()
        {
            var url = _cfg?.Invoke()?.QBittorrentUrl;
            if (string.IsNullOrWhiteSpace(url)) return Array.Empty<DownloadItem>();
            string json;
            try { json = Http.GetStringAsync(url.TrimEnd('/') + "/api/v2/torrents/info?filter=downloading").GetAwaiter().GetResult(); }
            catch { return Array.Empty<DownloadItem>(); }

            var list = new List<DownloadItem>();
            using var doc = JsonDocument.Parse(json);
            foreach (var t in doc.RootElement.EnumerateArray())
            {
                long size = t.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                long done = t.TryGetProperty("downloaded", out var d) ? d.GetInt64() : 0;
                double sp = t.TryGetProperty("dlspeed", out var v) ? v.GetDouble() : 0;
                string name = t.TryGetProperty("name", out var n) ? n.GetString() : "Torrent";
                string hash = t.TryGetProperty("hash", out var h) ? h.GetString() : name;
                list.Add(new DownloadItem
                {
                    Key = "qb:" + hash, Source = "qBittorrent", Name = name,
                    Done = Math.Min(done, size > 0 ? size : done), Total = size > 0 ? size : (long?)null, Speed = sp
                });
            }
            return list;
        }
    }
}
