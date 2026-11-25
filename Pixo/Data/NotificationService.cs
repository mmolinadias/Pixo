using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Pixo.Data
{
    public class NotificationItem
    {
        public uint Id;
        public string App, Title, Text;
        public DateTimeOffset When;
        public BitmapSource Logo;   // ícone oficial do app, vindo do Windows (pode ser null)
    }

    /// <summary>Notificações do Windows (as da Central de Notificações).</summary>
    public static class NotificationService
    {
        private static UserNotificationListener _listener;
        private static CancellationTokenSource _cts;
        private static readonly Dictionary<string, BitmapSource> Logos = new Dictionary<string, BitmapSource>();
        private static uint _maxSeenId;
        private static bool _first = true;

        public static volatile NotificationItem[] Items = Array.Empty<NotificationItem>();
        public static volatile string Error;

        /// <summary>Chamado quando chega uma notificação nova (o Bit leva um susto).</summary>
        public static event Action NewArrived;

        public static void Start()
        {
            _cts = new CancellationTokenSource();
            Task.Run(() => Loop(_cts.Token));
        }

        public static void Stop() => _cts?.Cancel();

        private static async Task Loop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (_listener == null)
                    {
                        var l = UserNotificationListener.Current;
                        var status = await l.RequestAccessAsync().AsTask();
                        if (status != UserNotificationListenerAccessStatus.Allowed)
                        {
                            Error = "Permita o acesso às notificações em Configurações do Windows › Privacidade › Notificações.";
                            await Task.Delay(30000, ct).ContinueWith(_ => { });
                            continue;
                        }
                        _listener = l;
                    }

                    var raw = await _listener.GetNotificationsAsync(NotificationKinds.Toast).AsTask();
                    var list = new List<NotificationItem>();
                    foreach (var n in raw)
                    {
                        var item = new NotificationItem { Id = n.Id, When = n.CreationTime, App = "App", Title = "", Text = "" };
                        try { item.App = n.AppInfo?.DisplayInfo?.DisplayName ?? "App"; } catch { }
                        try
                        {
                            var binding = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
                            var texts = binding?.GetTextElements()?.Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
                            if (texts != null && texts.Count > 0)
                            {
                                item.Title = texts[0];
                                item.Text = string.Join(" ", texts.Skip(1));
                            }
                        }
                        catch { }
                        item.Logo = await GetLogo(n, item.App);
                        list.Add(item);
                    }

                    var sorted = list.OrderByDescending(i => i.When).ToArray();
                    uint maxId = sorted.Length > 0 ? sorted.Max(i => i.Id) : _maxSeenId;
                    if (!_first && sorted.Any(i => i.Id > _maxSeenId)) NewArrived?.Invoke();
                    _maxSeenId = Math.Max(_maxSeenId, maxId);
                    _first = false;

                    Items = sorted;
                    Error = null;
                }
                catch (Exception)
                {
                    Error = "As notificações não estão disponíveis neste Windows.";
                    _listener = null;
                }
                await Task.Delay(4000, ct).ContinueWith(_ => { });
            }
        }

        private static async Task<BitmapSource> GetLogo(UserNotification n, string app)
        {
            lock (Logos) if (Logos.TryGetValue(app, out var cached)) return cached;
            BitmapSource bmp = null;

            // 1) ícone oficial pelo Windows Shell (funciona com programas comuns, como Claude e Discord)
            try
            {
                string aumid = null;
                try { aumid = n.AppInfo?.AppUserModelId; } catch { }
                bmp = AppIcons.Get(aumid);
            }
            catch { }
            if (bmp != null)
            {
                lock (Logos) Logos[app] = bmp;
                return bmp;
            }

            // 2) logo do pacote (apps da Microsoft Store)
            try
            {
                var reference = n.AppInfo?.DisplayInfo?.GetLogo(new Windows.Foundation.Size(64, 64));
                if (reference != null)
                {
                    using var stream = await reference.OpenReadAsync().AsTask();
                    using var ms = new MemoryStream();
                    await stream.AsStreamForRead().CopyToAsync(ms);
                    ms.Position = 0;
                    var img = new BitmapImage();
                    img.BeginInit();
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.StreamSource = ms;
                    img.EndInit();
                    img.Freeze();
                    bmp = img;
                }
            }
            catch { }
            lock (Logos) Logos[app] = bmp;
            return bmp;
        }
    }
}
