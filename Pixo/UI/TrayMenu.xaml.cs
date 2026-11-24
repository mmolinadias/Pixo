using System;
using System.Windows;
using System.Windows.Media;
using WinForms = System.Windows.Forms;

namespace Pixo.UI
{
    /// <summary>Menu rápido da bandeja (clique direito no ícone).</summary>
    public partial class TrayMenu : Window
    {
        public event Action OpenSettingsRequested;
        public event Action ExitRequested;

        public TrayMenu()
        {
            InitializeComponent();
            DataContext = App.Cfg;
            App.Engine.StatusChanged += () => Dispatcher.BeginInvoke(new Action(Refresh));
        }

        public void ShowNearCursor()
        {
            Refresh();
            Show();
            UpdateLayout();
            // posiciona acima do cursor, dentro da área de trabalho
            var dpi = VisualTreeHelper.GetDpi(this);
            var p = WinForms.Cursor.Position;
            double x = p.X / dpi.DpiScaleX, y = p.Y / dpi.DpiScaleY;
            var area = SystemParameters.WorkArea;
            Left = Math.Max(area.Left, Math.Min(x - ActualWidth + 12, area.Right - ActualWidth));
            Top = Math.Max(area.Top, Math.Min(y - ActualHeight + 12, area.Bottom - ActualHeight));
            Activate();
        }

        private void Refresh()
        {
            var e = App.Engine;
            LogoImage.Source = Images.Logo(72, !e.Connected);
            StatusDot.Fill = (Brush)FindResource(e.Connected ? "PixoSuccess" : "PixoCritical");
            StatusText.Text = e.Connected ? "Tela conectada · " + e.PortName : "Tela desconectada";
            ScreenText.Text = "Agora na tela: " + e.CurrentScreenName + (e.RotationPaused ? " (rotação pausada)" : "");
            PauseIcon.Kind = e.RotationPaused ? "play" : "pause";
            PauseButton.ToolTip = e.RotationPaused ? "Continuar a rotação" : "Pausar a rotação";
        }

        private void OnDeactivated(object sender, EventArgs e) => Hide();
        private void OnPrev(object sender, RoutedEventArgs e) => App.Engine.Post("anterior");
        private void OnNext(object sender, RoutedEventArgs e) => App.Engine.Post("proxima");
        private void OnPause(object sender, RoutedEventArgs e) => App.Engine.Post("alternar_pausa");

        private void OnSettings(object sender, RoutedEventArgs e)
        {
            Hide();
            OpenSettingsRequested?.Invoke();
        }

        private void OnExit(object sender, RoutedEventArgs e)
        {
            Hide();
            ExitRequested?.Invoke();
        }
    }
}
