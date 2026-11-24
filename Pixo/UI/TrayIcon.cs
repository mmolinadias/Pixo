using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using WinForms = System.Windows.Forms;

namespace Pixo.UI
{
    /// <summary>Ícone do Pixo na bandeja do Windows (perto do relógio).</summary>
    public sealed class TrayIcon : IDisposable
    {
        private readonly WinForms.NotifyIcon _icon;
        private readonly System.Drawing.Icon _on, _off;
        private TrayMenu _menu;
        private bool _lastConnected;

        public event Action OpenSettingsRequested;
        public event Action ExitRequested;

        public TrayIcon()
        {
            _on = MakeIcon(false);
            _off = MakeIcon(true);
            _icon = new WinForms.NotifyIcon
            {
                Icon = _off,
                Text = "Pixo",
                Visible = true
            };
            // clique direito: menu rápido; clique esquerdo: abre as configurações
            _icon.MouseClick += (s, e) =>
            {
                if (e.Button == WinForms.MouseButtons.Right) ShowMenu();
                else if (e.Button == WinForms.MouseButtons.Left) OpenSettingsRequested?.Invoke();
            };

            App.Engine.StatusChanged += () => Application.Current?.Dispatcher.BeginInvoke(new Action(UpdateIcon));
            UpdateIcon();
        }

        private void UpdateIcon()
        {
            bool c = App.Engine.Connected;
            if (c != _lastConnected || _icon.Icon == null)
            {
                _icon.Icon = c ? _on : _off;
                _lastConnected = c;
            }
            var text = c ? "Pixo · " + App.Engine.CurrentScreenName : "Pixo · tela desconectada";
            _icon.Text = text.Length > 63 ? text.Substring(0, 63) : text;
        }

        private void ShowMenu()
        {
            if (_menu == null)
            {
                _menu = new TrayMenu();
                _menu.OpenSettingsRequested += () => OpenSettingsRequested?.Invoke();
                _menu.ExitRequested += () => ExitRequested?.Invoke();
            }
            _menu.ShowNearCursor();
        }

        private static System.Drawing.Icon MakeIcon(bool off)
        {
            var img = Images.Logo(32, off);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(img));
            using var ms = new MemoryStream();
            enc.Save(ms);
            ms.Position = 0;
            using var bmp = new System.Drawing.Bitmap(ms);
            return System.Drawing.Icon.FromHandle(bmp.GetHicon());
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
            _menu?.Close();
        }
    }
}
