using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pixo.Data;
using Pixo.Device;
using Pixo.Render;

namespace Pixo.Core
{
    /// <summary>
    /// O "motor" do Pixo: conecta na tela, faz a rotação das telas, entra no Modo Gamer,
    /// atende os comandos (voz, bandeja, janela) e envia os quadros. Roda numa thread própria.
    /// </summary>
    public sealed class Engine
    {
        public static Engine Instance { get; private set; }

        private readonly Config _cfg;
        private readonly ConcurrentQueue<string> _commands = new ConcurrentQueue<string>();
        private readonly Dictionary<string, Screen> _screens = new Dictionary<string, Screen>();
        private Thread _thread;
        private volatile bool _stop;

        private TuringDisplay _display;
        private DateTime _nextConnectTry = DateTime.MinValue;
        private Screen _current;
        private DateTime _shownAt, _nextRender = DateTime.MinValue, _pausedUntil = DateTime.MinValue;
        private DateTime _welcomeUntil = DateTime.MinValue;
        private bool _autoRotate = true;
        private bool _inGame;
        private string _beforeGame;
        private double _appliedBrightness = -1;
        private bool _appliedFlip;
        private bool _screenOff;
        private DateTime _lastPreview = DateTime.MinValue;

        // ---- estado público (lido pela interface) ----
        public bool Connected => _display != null;
        public string PortName => _display?.PortName;
        public string CurrentScreenId => _current?.Id;
        public string CurrentScreenName => _current == null ? "—" : ScreenInfo.Name(_current.Id);
        public bool RotationPaused => !_autoRotate || DateTime.Now < _pausedUntil;
        public volatile BitmapSource Preview;
        public string PetState => (_screens.TryGetValue("bichinho", out var s) ? (s as PetScreen)?.State : null) ?? "feliz";

        public event Action StatusChanged;

        public Engine(Config cfg)
        {
            _cfg = cfg;
            Instance = this;
        }

        public void Start()
        {
            _screens["temperaturas"] = new TemperaturesScreen();
            _screens["musica"] = new MusicScreen();
            _screens["notificacoes"] = new NotificationsScreen();
            _screens["downloads"] = new DownloadsScreen();
            _screens["bichinho"] = new PetScreen(() => _cfg);
            _screens["imagem"] = new ImageScreen(() => _cfg);
            _screens["relogio"] = new ClockScreen();
            _screens["gamer"] = new GamerScreen();
            _screens["boasvindas"] = new WelcomeScreen(() => _cfg);

            NotificationService.NewArrived += () => (_screens["bichinho"] as PetScreen)?.React("susto");
            DownloadService.Finished += () => (_screens["bichinho"] as PetScreen)?.React("comemora");

            _thread = new Thread(Run) { IsBackground = true, Name = "Pixo.Engine" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        public void Stop()
        {
            _stop = true;
            _thread?.Join(2000);
            try { _display?.Dispose(); } catch { }
            _display = null;
        }

        /// <summary>Pede uma ação: id de tela, "proxima", "anterior", "pausar_rotacao", "continuar", "alternar_pausa",
        /// "apagar", "acender", "midia:pausar|tocar|pular", "reconectar".</summary>
        public void Post(string action, bool fromVoice = false)
        {
            if (!string.IsNullOrWhiteSpace(action)) _commands.Enqueue((fromVoice ? "voz:" : "") + action);
        }

        // ---------------- loop ----------------

        private void Run()
        {
            CultureInfo.CurrentCulture = D.PtBr;
            CultureInfo.CurrentUICulture = D.PtBr;
            Show(FirstRotationScreen() ?? "temperaturas", resetTimer: true);

            while (!_stop)
            {
                try
                {
                    EnsureConnected();
                    HandleCommands();
                    HandleGameMode();
                    HandleRotation();
                    ApplyDisplaySettings();
                    if (DateTime.Now >= _nextRender) RenderAndSend();
                }
                catch (Exception)
                {
                    Disconnect();
                }
                Thread.Sleep(15);
            }
        }

        private void EnsureConnected()
        {
            if (_display != null || DateTime.Now < _nextConnectTry) return;
            _nextConnectTry = DateTime.Now.AddSeconds(3);
            try
            {
                _display = TuringDisplay.TryOpen(_cfg.Port);
            }
            catch { _display = null; }
            if (_display != null)
            {
                _appliedBrightness = -1;
                _appliedFlip = _cfg.Flipped;
                _screenOff = false;
                _display.ScreenOn();
                _welcomeUntil = DateTime.Now.AddSeconds(2.5);
                _screens["boasvindas"].OnShow();
                _nextRender = DateTime.MinValue;
                StatusChanged?.Invoke();
            }
        }

        private void Disconnect()
        {
            try { _display?.Dispose(); } catch { }
            if (_display != null) { _display = null; StatusChanged?.Invoke(); }
            _nextConnectTry = DateTime.Now.AddSeconds(3);
        }

        private void ApplyDisplaySettings()
        {
            if (_display == null) return;
            if (Math.Abs(_appliedBrightness - _cfg.Brightness) > 0.5)
            {
                _display.SetBrightness(_screenOff ? 0 : _cfg.Brightness);
                _appliedBrightness = _cfg.Brightness;
            }
            if (_appliedFlip != _cfg.Flipped)
            {
                _appliedFlip = _cfg.Flipped;
                _display.Invalidate();
                _nextRender = DateTime.MinValue;
            }
        }

        // ---------------- rotação ----------------

        private List<string> RotationIds() =>
            _cfg.Screens.Where(s => s.Enabled).Select(s => s.Id).Where(id => _screens.ContainsKey(id)).ToList();

        private string FirstRotationScreen() => RotationIds().FirstOrDefault();

        private void Show(string id, bool resetTimer)
        {
            if (!_screens.TryGetValue(id, out var sc)) return;
            if (_current != sc) sc.OnShow();
            _current = sc;
            if (resetTimer) _shownAt = DateTime.Now;
            _display?.Invalidate();
            _nextRender = DateTime.MinValue;
            StatusChanged?.Invoke();
        }

        private void Step(int dir)
        {
            var ids = RotationIds();
            if (ids.Count == 0) return;
            int idx = _current == null ? -1 : ids.IndexOf(_current.Id);
            for (int i = 1; i <= ids.Count; i++)
            {
                int j = ((idx + dir * i) % ids.Count + ids.Count) % ids.Count;
                if (idx < 0 && dir < 0) j = (ids.Count - i % ids.Count) % ids.Count;
                var sc = _screens[ids[j]];
                if (sc.HasContent) { Show(sc.Id, true); return; }
            }
            _shownAt = DateTime.Now; // nada disponível: fica onde está
        }

        private void HandleRotation()
        {
            if (_inGame || !_autoRotate || DateTime.Now < _pausedUntil) return;
            if ((DateTime.Now - _shownAt).TotalSeconds >= _cfg.SecondsPerScreen) Step(1);
            // a tela atual ficou sem conteúdo (ex.: a música parou): passa para a próxima
            else if (_current != null && !_current.HasContent && (DateTime.Now - _shownAt).TotalSeconds > 3) Step(1);
        }

        private void HandleGameMode()
        {
            bool playing = _cfg.GameModeAuto && GameService.Current != null;
            if (playing && !_inGame)
            {
                _inGame = true;
                _beforeGame = _current?.Id;
                Show("gamer", true);
            }
            else if (!playing && _inGame)
            {
                _inGame = false;
                if (_current?.Id == "gamer") Show(_beforeGame ?? FirstRotationScreen() ?? "temperaturas", true);
            }
        }

        private void HandleCommands()
        {
            while (_commands.TryDequeue(out var raw))
            {
                bool voice = raw.StartsWith("voz:");
                var a = voice ? raw.Substring(4) : raw;

                if (a.StartsWith("midia:")) { MediaService.Command(a.Substring(6)); continue; }

                switch (a)
                {
                    case "proxima": Step(1); break;
                    case "anterior": Step(-1); break;
                    case "pausar_rotacao": _autoRotate = false; break;
                    case "continuar": _autoRotate = true; _pausedUntil = DateTime.MinValue; break;
                    case "alternar_pausa":
                        if (RotationPaused) { _autoRotate = true; _pausedUntil = DateTime.MinValue; }
                        else _autoRotate = false;
                        break;
                    case "apagar":
                        _screenOff = true;
                        try { _display?.ScreenOff(); } catch { }
                        break;
                    case "acender":
                        _screenOff = false;
                        try { _display?.ScreenOn(); } catch { }
                        _appliedBrightness = -1;
                        break;
                    case "reconectar":
                        Disconnect();
                        _nextConnectTry = DateTime.MinValue;
                        break;
                    default:
                        if (_screens.ContainsKey(a) && a != "boasvindas")
                        {
                            Show(a, true);
                            if (voice && _cfg.PauseAfterVoice > 0)
                                _pausedUntil = DateTime.Now.AddSeconds(_cfg.PauseAfterVoice);
                        }
                        break;
                }
                // o símbolo de pausa no cabeçalho muda
                _nextRender = DateTime.MinValue;
                StatusChanged?.Invoke();
            }
        }

        // ---------------- desenho ----------------

        private void RenderAndSend()
        {
            var sc = DateTime.Now < _welcomeUntil && _display != null ? _screens["boasvindas"] : _current;
            if (sc == null) return;

            var ids = RotationIds();
            var ctx = new FrameCtx
            {
                Cfg = _cfg,
                PageIndex = ids.IndexOf(sc.Id),
                PageCount = ids.Count,
                Paused = RotationPaused && !_inGame
            };

            var frame = Render(sc, ctx);
            _nextRender = DateTime.Now + sc.Interval;

            if ((DateTime.Now - _lastPreview).TotalMilliseconds > 900)
            {
                Preview = frame;
                _lastPreview = DateTime.Now;
            }

            if (_display == null || _screenOff) return;
            var px = new byte[TuringDisplay.W * TuringDisplay.H * 4];
            frame.CopyPixels(px, TuringDisplay.W * 4, 0);
            _display.SendFrame(px, _cfg.Flipped);
        }

        /// <summary>Desenha uma tela num bitmap 480×320 (também usado pela prévia da janela).</summary>
        public static BitmapSource Render(Screen sc, FrameCtx ctx)
        {
            var dv = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
            TextOptions.SetTextFormattingMode(dv, TextFormattingMode.Ideal);
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Pal.Bg, null, new Rect(0, 0, TuringDisplay.W, TuringDisplay.H));
                dc.PushClip(new RectangleGeometry(new Rect(0, 0, TuringDisplay.W, TuringDisplay.H)));
                sc.Draw(dc, ctx);
                dc.Pop();
            }
            var rtb = new RenderTargetBitmap(TuringDisplay.W, TuringDisplay.H, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }
    }
}
