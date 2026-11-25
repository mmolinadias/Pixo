using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
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
        private PetLife _life;

        // ---- estado público (lido pela interface) ----
        public bool Connected => _display != null;
        public string PortName => _display?.PortName;
        public string CurrentScreenId => _current?.Id;
        public string CurrentScreenName => _current == null ? "—" : ScreenInfo.Name(_current.Id);
        public bool RotationPaused => !_autoRotate || DateTime.Now < _pausedUntil;
        public volatile BitmapSource Preview;
        public string PetState => (_screens.TryGetValue("bichinho", out var s) ? (s as PetScreen)?.State : null) ?? "feliz";

        /// <summary>Quadros por segundo que chegaram na tela nos últimos ~2 s.</summary>
        public double DisplayFps { get; private set; }
        /// <summary>KB por segundo enviados pelo USB nos últimos ~2 s (o limite da tela é ~166 KB/s).</summary>
        public double DisplayKBps { get; private set; }
        private int _statFrames;
        private long _statBytes;
        private DateTime _statStart = DateTime.Now;

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
            var pet = new PetScreen(() => _cfg);
            _screens["bichinho"] = pet;
            _screens["clima"] = new WeatherScreen(() => _cfg);
            _screens["imagem"] = new ImageScreen(() => _cfg);
            _screens["relogio"] = new ClockScreen();
            _screens["gamer"] = new GamerScreen();
            _screens["boasvindas"] = new WelcomeScreen(() => _cfg);

            _life = new PetLife(_cfg, pet, () => ShowPetsForAWhile());

            NotificationService.NewArrived += () => (_screens["bichinho"] as PetScreen)?.React("susto");
            DownloadService.Finished += () => (_screens["bichinho"] as PetScreen)?.React("comemora");

            // prioridade acima do normal: com o PC carregado (jogos, renderização), o Windows
            // não pausa o envio para a tela no meio de um quadro
            _thread = new Thread(Run) { IsBackground = true, Name = "Pixo.Engine", Priority = ThreadPriority.AboveNormal };
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
                    _life?.Tick();
                    ApplyDisplaySettings();
                    if (DateTime.Now >= _nextRender) RenderAndSend();
                }
                catch (Exception)
                {
                    Disconnect();
                }
                // dorme até a hora do próximo quadro (no máximo 15 ms, para atender os comandos)
                double wait = (_nextRender - DateTime.Now).TotalMilliseconds;
                SleepPrecise(Math.Clamp(wait, 1, 15));
            }
            if (_timer != IntPtr.Zero) { CloseHandle(_timer); _timer = IntPtr.Zero; }
        }

        // ---- espera precisa ----
        // O Thread.Sleep do Windows anda de ~15,6 em ~15,6 ms: um quadro de 40 ms virava 47 ou 62 ms,
        // e a animação ficava irregular. O timer de alta resolução acorda na hora certa (Windows 10 1803+).
        private IntPtr _timer;
        private bool _timerTried;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWaitableTimerExW(IntPtr attributes, string name, uint flags, uint access);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetWaitableTimer(IntPtr timer, ref long dueTime, int period, IntPtr completion, IntPtr arg, bool resume);
        [DllImport("kernel32.dll")]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        private void SleepPrecise(double ms)
        {
            if (!_timerTried)
            {
                _timerTried = true;
                try { _timer = CreateWaitableTimerExW(IntPtr.Zero, null, 0x2 /* HIGH_RESOLUTION */, 0x1F0003 /* ALL_ACCESS */); }
                catch { _timer = IntPtr.Zero; }
            }
            if (_timer != IntPtr.Zero)
            {
                long due = -(long)(ms * 10_000); // negativo = relativo, em unidades de 100 ns
                if (SetWaitableTimer(_timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
                {
                    WaitForSingleObject(_timer, 1000);
                    return;
                }
            }
            Thread.Sleep((int)Math.Ceiling(ms));
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
            if (_display.SetColorTune(_cfg.ColorSaturation, _cfg.ColorContrast, _cfg.ColorGamma))
                _nextRender = DateTime.MinValue;
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
            // sem reenvio completo: o driver compara com o quadro anterior e só manda o que mudou
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
                if (a.StartsWith("evento:")) { HandleEvent(a.Substring(7)); continue; }
                if (a.StartsWith("msg:"))
                {
                    Pet?.Say("RECADO", a.Substring(4), 30);
                    ShowPetsForAWhile();
                    continue;
                }
                if (a == "carinho") { _life?.Carinho(); continue; }

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

        // ---------------- bichinhos: eventos de fora ----------------

        private PetScreen Pet => _screens.TryGetValue("bichinho", out var s) ? s as PetScreen : null;

        /// <summary>Mostra a tela dos bichinhos por um tempo (fora do Modo Gamer), pausando a rotação.</summary>
        private void ShowPetsForAWhile(double seconds = 30)
        {
            if (_inGame) return;
            Show("bichinho", true);
            _pausedUntil = DateTime.Now.AddSeconds(seconds);
            _nextRender = DateTime.MinValue;
            StatusChanged?.Invoke();
        }

        /// <summary>Eventos da Pixo API (ex.: o Claude Code).</summary>
        private void HandleEvent(string name)
        {
            var pet = Pet;
            if (pet == null) return;
            switch (name)
            {
                case "claude-espera":
                    pet.React("susto");
                    pet.Say("CLAUDE", "O Claude tá te chamando! Dá uma olhada lá.", 45);
                    ShowPetsForAWhile(45);
                    break;
                case "claude-pronto":
                    pet.React("comemora");
                    pet.Say("CLAUDE", "O Claude terminou! Bora ver o que ele fez?", 30);
                    ShowPetsForAWhile();
                    break;
                case "comemora":
                case "susto":
                    pet.React(name);
                    ShowPetsForAWhile(15);
                    break;
                case "carinho":
                    _life?.Carinho();
                    break;
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

            // desenha sempre na mesma imagem (não cria uma nova a cada quadro)
            _rtb ??= new RenderTargetBitmap(TuringDisplay.W, TuringDisplay.H, 96, 96, PixelFormats.Pbgra32);
            _rtb.Clear();
            _rtb.Render(BuildVisual(sc, ctx));
            // ritmo fixo: o próximo quadro sai um intervalo depois do anterior (e não depois do fim
            // do envio); se o envio atrasou mais que um intervalo, recomeça a contar de agora
            var now = DateTime.Now;
            _nextRender = _nextRender == DateTime.MinValue || now - _nextRender > sc.Interval
                ? now + sc.Interval
                : _nextRender + sc.Interval;
            _rtb.CopyPixels(_frameBuf, TuringDisplay.W * 4, 0);

            // a prévia da janela é uma cópia congelada, feita no máximo 1 vez por segundo
            if ((DateTime.Now - _lastPreview).TotalMilliseconds > 900)
            {
                var copy = BitmapSource.Create(TuringDisplay.W, TuringDisplay.H, 96, 96, PixelFormats.Pbgra32, null, _frameBuf, TuringDisplay.W * 4);
                copy.Freeze();
                Preview = copy;
                _lastPreview = DateTime.Now;
            }

            if (_display == null || _screenOff) return;
            int px = _display.SendFrame(_frameBuf, _cfg.Flipped);

            // estatística para a janela (aba Tela física)
            if (px > 0) { _statFrames++; _statBytes += px * 2L; }
            double sec = (DateTime.Now - _statStart).TotalSeconds;
            if (sec >= 2)
            {
                DisplayFps = _statFrames / sec;
                DisplayKBps = _statBytes / 1024.0 / sec;
                _statFrames = 0; _statBytes = 0; _statStart = DateTime.Now;
            }
        }

        // reaproveitados a cada quadro (evita pausas de limpeza de memória)
        private readonly byte[] _frameBuf = new byte[TuringDisplay.W * TuringDisplay.H * 4];
        private RenderTargetBitmap _rtb;

        private static DrawingVisual BuildVisual(Screen sc, FrameCtx ctx)
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
            return dv;
        }

        /// <summary>Desenha uma tela num bitmap 480×320 novo (para usos avulsos).</summary>
        public static BitmapSource Render(Screen sc, FrameCtx ctx)
        {
            var rtb = new RenderTargetBitmap(TuringDisplay.W, TuringDisplay.H, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(BuildVisual(sc, ctx));
            rtb.Freeze();
            return rtb;
        }
    }
}
