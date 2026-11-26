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

            // menos pausas longas de limpeza de memória (deixa a animação da tela mais lisa)
            System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;

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
            WeatherService.Start(() => Cfg);
            AcService.Start(() => Cfg);
            HealthService.Start();

            Engine = new Engine(Cfg);
            Engine.Start();
            PixoApi.Start(() => Cfg, action => Engine.Post(action));
            Updater.Start();

            // painel do Claude Code: quem tinha ligado a versão antiga (só "te chamou"/"terminou") ganha a completa
            try { if (ClaudeHooks.HasOldVersion() && !ClaudeHooks.IsInstalled(Cfg.ApiPort)) ClaudeHooks.Install(Cfg.ApiPort); } catch { }

            // "Iniciar com o Windows": corrige o caminho (ex.: veio do C:\Pixo para o instalado) e, na primeira
            // vez num PC instalado pelo Setup, já deixa ligado (dá para desligar na aba Geral)
            if (Program.IsAdmin())
            {
                if (Config.IsFirstRun && Updater.Installed && !Core.Startup.IsEnabled()) Core.Startup.Enable();
                else Core.Startup.FixPath();
            }

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
            try { WeatherService.Stop(); } catch { }
            try { AcService.Stop(); } catch { }
            try { HealthService.Stop(); } catch { }
            try { PixoApi.Stop(); } catch { }
            try { Updater.Stop(); Updater.ApplyOnExit(); } catch { }
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
