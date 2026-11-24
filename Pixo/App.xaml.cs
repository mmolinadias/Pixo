using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using Pixo.Core;
using Pixo.Data;
using Pixo.UI;
using Pixo.Voice;

namespace Pixo
{
    public partial class App : Application
    {
        private static Mutex _single;

        public static Config Cfg { get; private set; }
        public static Engine Engine { get; private set; }
        public static VoiceListener Voice { get; private set; }

        private TrayIcon _tray;
        private MainWindow _main;
        public static bool Exiting { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            // só uma cópia do Pixo por vez
            _single = new Mutex(true, "Pixo.SingleInstance", out bool first);
            if (!first)
            {
                Shutdown();
                return;
            }

            base.OnStartup(e);

            try
            {
                Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(
                    (Color)ColorConverter.ConvertFromString("#42cfef"), Wpf.Ui.Appearance.ApplicationTheme.Dark);
            }
            catch { }

            Cfg = Config.Load();

            HardwareService.Start();
            NetworkService.Start();
            MediaService.Start(() => Cfg);
            NotificationService.Start();
            DownloadService.Start(() => Cfg);
            GameService.Start(() => Cfg);

            Engine = new Engine(Cfg);
            Engine.Start();

            Voice = new VoiceListener(Cfg, action => Engine.Post(action, fromVoice: true));
            // se a última carga do modelo de voz derrubou o app, abre com a voz desligada
            if (!Voice.RecoverFromCrash()) Voice.Restart();

            _tray = new TrayIcon();
            _tray.OpenSettingsRequested += () => ShowMain();
            _tray.ExitRequested += () => ExitApp();

            bool minimized = e.Args.Any(a => a.Equals("--minimizado", StringComparison.OrdinalIgnoreCase));
            if (!minimized || Config.IsFirstRun) ShowMain(Config.IsFirstRun);
        }

        public void ShowMain(bool welcome = false)
        {
            if (_main == null)
            {
                _main = new MainWindow(welcome);
            }
            _main.Show();
            if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
            _main.Activate();
        }

        public void ExitApp()
        {
            Exiting = true;
            try { Cfg?.Save(); } catch { }
            try { Voice?.Dispose(); } catch { }
            try { Engine?.Stop(); } catch { }
            try { HardwareService.Stop(); } catch { }
            try { NetworkService.Stop(); } catch { }
            try { MediaService.Stop(); } catch { }
            try { NotificationService.Stop(); } catch { }
            try { DownloadService.Stop(); } catch { }
            try { GameService.Stop(); } catch { }
            _tray?.Dispose();
            _main?.Close();
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { Cfg?.Save(); } catch { }
            base.OnExit(e);
        }
    }
}
