using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Pixo.Core;

namespace Pixo.Data
{
    public class NowPlaying
    {
        public string Title, Artist, Album, App, AppKey;
        public bool Playing;
        public TimeSpan Position, Duration;
        public DateTimeOffset UpdatedAt;
        public BitmapSource Cover;      // capa
        public BitmapSource CoverTiny;  // capa reduzida (vira o fundo desfocado)
        public BitmapSource AppIcon;    // ícone oficial do app que está tocando (pode ser null)

        /// <summary>Posição estimada agora (o Windows só atualiza de vez em quando).</summary>
        public TimeSpan PositionNow
        {
            get
            {
                var p = Position;
                if (Playing) p += DateTimeOffset.Now - UpdatedAt;
                if (Duration > TimeSpan.Zero && p > Duration) p = Duration;
                return p < TimeSpan.Zero ? TimeSpan.Zero : p;
            }
        }
    }

    /// <summary>O que está tocando no PC, pela API de mídia do Windows (sem conta do Spotify).</summary>
    public static class MediaService
    {
        private static GlobalSystemMediaTransportControlsSessionManager _mgr;
        private static string _coverKey;
        private static BitmapSource _cover, _coverTiny;
        private static CancellationTokenSource _cts;

        public static volatile NowPlaying Current;
        public static Func<Config> Cfg;

        public static void Start(Func<Config> cfg)
        {
            Cfg = cfg;
            _cts = new CancellationTokenSource();
            Task.Run(() => Loop(_cts.Token));
        }

        public static void Stop() => _cts?.Cancel();

        private static async Task Loop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try { Current = await Read(); }
                catch { Current = null; }
                await Task.Delay(1000, ct).ContinueWith(_ => { });
            }
        }

        private static async Task<GlobalSystemMediaTransportControlsSession> Session()
        {
            _mgr ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask();
            var sessions = _mgr.GetSessions().ToList();
            if (Cfg?.Invoke()?.OnlySpotify == true)
                return sessions.FirstOrDefault(s => (s.SourceAppUserModelId ?? "").IndexOf("spotify", StringComparison.OrdinalIgnoreCase) >= 0);

            // prefere o que está tocando; depois a sessão "atual" do Windows
            var playing = sessions.FirstOrDefault(s =>
                s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
            return playing ?? _mgr.GetCurrentSession();
        }

        private static async Task<NowPlaying> Read()
        {
            var s = await Session();
            if (s == null) return null;
            var props = await s.TryGetMediaPropertiesAsync().AsTask();
            if (props == null || string.IsNullOrWhiteSpace(props.Title)) return null;
            var info = s.GetPlaybackInfo();
            var tl = s.GetTimelineProperties();

            var np = new NowPlaying
            {
                Title = props.Title,
                Artist = props.Artist ?? "",
                Album = props.AlbumTitle ?? "",
                AppKey = s.SourceAppUserModelId ?? "",
                App = FriendlyApp(s.SourceAppUserModelId),
                Playing = info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                Position = tl.Position,
                Duration = tl.EndTime - tl.StartTime,
                UpdatedAt = tl.LastUpdatedTime
            };

            string key = np.Title + "|" + np.Artist + "|" + np.Album;
            if (key != _coverKey)
            {
                _coverKey = key;
                _cover = null; _coverTiny = null;
                try
                {
                    if (props.Thumbnail != null)
                    {
                        using var stream = await props.Thumbnail.OpenReadAsync().AsTask();
                        using var ms = new MemoryStream();
                        await stream.AsStreamForRead().CopyToAsync(ms);
                        ms.Position = 0;
                        var img = new BitmapImage();
                        img.BeginInit();
                        img.CacheOption = BitmapCacheOption.OnLoad;
                        img.StreamSource = ms;
                        img.EndInit();
                        img.Freeze();
                        _cover = img;
                        var tiny = new TransformedBitmap(img, new ScaleTransform(12.0 / img.PixelWidth, 8.0 / img.PixelHeight));
                        tiny.Freeze();
                        _coverTiny = tiny;
                    }
                }
                catch { }
            }
            np.Cover = _cover;
            np.CoverTiny = _coverTiny;
            np.AppIcon = AppIcons.Get(np.AppKey);
            return np;
        }

        public static string FriendlyApp(string aumid)
        {
            if (string.IsNullOrEmpty(aumid)) return "Mídia";
            var a = aumid.ToLowerInvariant();
            if (a.Contains("spotify")) return "Spotify";
            if (a.Contains("chrome")) return "Chrome";
            if (a.Contains("msedge")) return "Edge";
            if (a.Contains("firefox")) return "Firefox";
            if (a.Contains("zunemusic")) return "Media Player";
            if (a.Contains("vlc")) return "VLC";
            if (a.Contains("deezer")) return "Deezer";
            if (a.Contains("youtube")) return "YouTube";
            if (a.Contains("applemusic") || a.Contains("itunes")) return "Apple Music";
            var name = aumid.Split('!').Last().Split('\\').Last();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
            name = name.Split('_')[0].Split('.').Last();
            return name.Length > 0 ? char.ToUpper(name[0]) + name[1..] : "Mídia";
        }

        /// <summary>acao: "pausar", "tocar" ou "pular".</summary>
        public static void Command(string acao)
        {
            Task.Run(async () =>
            {
                try
                {
                    var s = await Session();
                    if (s == null) return;
                    switch (acao)
                    {
                        case "pausar": await s.TryPauseAsync().AsTask(); break;
                        case "tocar": await s.TryPlayAsync().AsTask(); break;
                        case "pular": await s.TrySkipNextAsync().AsTask(); break;
                    }
                }
                catch { }
            });
        }
    }
}
