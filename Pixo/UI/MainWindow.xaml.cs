using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Pixo.Core;
using Pixo.Render;

namespace Pixo.UI
{
    public class NavItem
    {
        public string Icon { get; set; }
        public string Title { get; set; }
        public string Page { get; set; }
    }

    public class ScreenRow : INotifyPropertyChanged
    {
        public string Id { get; set; }
        public string Name => ScreenInfo.Name(Id);
        public string Hint => ScreenInfo.Hint(Id);
        public string Icon => ScreenInfo.Icon(Id);
        private bool _enabled;
        public bool Enabled { get => _enabled; set { _enabled = value; Changed(); } }
        public event PropertyChangedEventHandler PropertyChanged;
        private void Changed([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public class VoiceRow
    {
        public string Phrase { get; set; }
        public string Action { get; set; }
    }

    public class MicOption
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class ActionOption
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
    {
        private readonly Config _cfg = App.Cfg;
        private readonly ObservableCollection<ScreenRow> _screens = new ObservableCollection<ScreenRow>();
        private readonly ObservableCollection<VoiceRow> _voice = new ObservableCollection<VoiceRow>();
        private readonly DispatcherTimer _timer;
        private bool _loading = true;

        // prévia do bichinho
        private string _petPreviewState = "feliz";
        private int _petFrame;
        private DateTime _petNext = DateTime.MinValue;

        public MainWindow(bool welcome = false)
        {
            InitializeComponent();
            DataContext = _cfg;

            NavLogo.Source = Images.Logo(64);
            AboutLogo.Source = Images.Logo(80);

            Nav.ItemsSource = new List<NavItem>
            {
                new NavItem { Icon = "home", Title = "Início", Page = "PageHome" },
                new NavItem { Icon = "screens", Title = "Telas", Page = "PageScreens" },
                new NavItem { Icon = "monitor", Title = "Tela física", Page = "PageDevice" },
                new NavItem { Icon = "pet", Title = "Bichinho", Page = "PagePet" },
                new NavItem { Icon = "mic", Title = "Voz", Page = "PageVoice" },
                new NavItem { Icon = "gamepad", Title = "Jogos", Page = "PageGames" },
                new NavItem { Icon = "download", Title = "Downloads", Page = "PageDownloads" },
                new NavItem { Icon = "image", Title = "Imagens", Page = "PageImages" },
                new NavItem { Icon = "sliders", Title = "Geral", Page = "PageGeneral" },
            };
            Nav.SelectedIndex = 0;

            // Telas
            foreach (var s in _cfg.Screens) _screens.Add(new ScreenRow { Id = s.Id, Enabled = s.Enabled });
            ScreenList.ItemsSource = _screens;

            // Tela física
            OrientationBox.SelectedIndex = _cfg.Flipped ? 1 : 0;
            FillPorts();

            // Bichinho
            PetStateBox.Items.Add("Ao vivo (humor de agora)");
            foreach (var kv in Bit.States) PetStateBox.Items.Add(kv.Value.Name);
            PetStateBox.SelectedIndex = 0;

            // Voz
            ActionColumn.ItemsSource = ScreenInfo.VoiceActions.Select(a => new ActionOption { Id = a.Id, Name = a.Name }).ToList();
            LoadVoiceRows(_cfg.VoiceCommands);
            VoiceGrid.ItemsSource = _voice;
            FillMics();
            App.Voice.Changed += () => Dispatcher.BeginInvoke(new Action(RefreshVoice));
            RefreshVoice();

            // Geral
            StartupSwitch.IsChecked = Startup.IsEnabled();

            RefreshPaths();
            WelcomeBar.IsOpen = welcome;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _timer.Tick += (s, e) => Tick();
            _timer.Start();

            _cfg.PropertyChanged += (s, e) => _cfg.Save();
            _loading = false;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // fechar a janela só esconde: o Pixo continua na bandeja
            _cfg.Save();
            if (!App.Exiting)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            _timer.Stop();
            base.OnClosing(e);
        }

        // ---------------- atualização periódica ----------------

        private void Tick()
        {
            if (!IsVisible) return;
            var eng = App.Engine;

            // status
            NavDot.Fill = (System.Windows.Media.Brush)FindResource(eng.Connected ? "PixoSuccess" : "PixoCritical");
            NavStatus.Text = eng.Connected ? "Tela conectada" : "Procurando a tela…";
            HomeConn.Text = eng.Connected ? "Tela conectada em " + eng.PortName : "Procurando a tela…";
            HomeScreen.Text = "Agora: " + eng.CurrentScreenName + (eng.RotationPaused ? " · rotação pausada" : "");
            PauseBtnIcon.Kind = eng.RotationPaused ? "play" : "pause";
            PauseBtnText.Text = eng.RotationPaused ? "Continuar rotação" : "Pausar rotação";

            if (!eng.Connected)
            {
                StatusBar.Title = "Não encontrei a tela";
                StatusBar.Message = "Confira o cabo USB e feche o UsbMonitor (o programa que veio com a tela). O Pixo tenta de novo a cada 3 segundos.";
                StatusBar.Severity = Wpf.Ui.Controls.InfoBarSeverity.Warning;
                StatusBar.IsOpen = true;
            }
            else if (Data.HardwareService.Current.Error == "Sem sensores" || Data.HardwareService.Current.CpuTempMissing)
            {
                bool pawn = Data.HardwareService.PawnIoInstalled();
                StatusBar.Title = "Sem a temperatura da CPU";
                StatusBar.Message = pawn
                    ? "O driver PawnIO está instalado, mas a CPU não respondeu. Reinicie o PC depois de instalar o driver e abra o Pixo como administrador."
                    : "Instale o driver PawnIO (pawnio.eu) e reinicie o Pixo. Ele é o que permite ler a temperatura da CPU e da placa-mãe.";
                StatusBar.Severity = Wpf.Ui.Controls.InfoBarSeverity.Warning;
                StatusBar.IsOpen = true;
            }
            else StatusBar.IsOpen = false;

            var prev = eng.Preview;
            if (prev != null && !ReferenceEquals(PreviewImage.Source, prev)) PreviewImage.Source = prev;

            // prévia animada do bichinho
            if (PagePet.Visibility == Visibility.Visible)
            {
                string state = PetStateBox.SelectedIndex <= 0 ? eng.PetState : Bit.States.Keys.ElementAt(PetStateBox.SelectedIndex - 1);
                if (state != _petPreviewState) { _petPreviewState = state; _petFrame = 0; _petNext = DateTime.MinValue; }
                if (DateTime.Now >= _petNext)
                {
                    var info = Bit.States[_petPreviewState];
                    PetPreview.Source = Images.BitFrame(_petPreviewState, _petFrame);
                    int dur = info.Durations[_petFrame];
                    _petFrame = (_petFrame + 1) % info.Durations.Length;
                    if (_petFrame == 0 && !info.Loop) dur += 900;
                    _petNext = DateTime.Now.AddMilliseconds(dur);
                }
            }
        }

        private void FillMics()
        {
            var was = _loading;
            _loading = true;
            MicBox.ItemsSource = Voice.VoiceListener.Microphones().Select(m => new MicOption { Id = m.Id, Name = m.Name }).ToList();
            MicBox.SelectedValue = _cfg.MicDevice;
            if (MicBox.SelectedIndex < 0) MicBox.SelectedIndex = 0;
            _loading = was;
        }

        private void OnMicDropDown(object sender, EventArgs e) => FillMics();

        private void OnMicChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || MicBox.SelectedValue is not int id || id == _cfg.MicDevice) return;
            _cfg.MicDevice = id;
            App.Voice.Restart();
        }

        private void RefreshVoice()
        {
            var v = App.Voice;
            MicLevel.Value = Math.Min(100, v.Level * 300); // fala normal fica entre 30% e 100%
            VoiceRaw.Text = "Entendendo: " + (string.IsNullOrEmpty(v.LastRaw) ? "—" : "\"" + v.LastRaw + "\"");
            VoiceStatus.Text = v.Status;
            VoiceHeard.Text = string.IsNullOrEmpty(v.LastHeard) ? "Nenhuma frase reconhecida ainda" : "Última frase: \"" + v.LastHeard + "\"";
            MicIcon.Brush = (System.Windows.Media.Brush)FindResource(v.Listening ? "PixoAccent" : "PixoText3");
            ModelPathText.Text = "Pasta do modelo: " + _cfg.VoiceModelPath;
        }

        private void RefreshPaths()
        {
            DownloadsPathText.Text = string.IsNullOrWhiteSpace(_cfg.DownloadsFolder)
                ? "Downloads do Windows (padrão)" : _cfg.DownloadsFolder;
            ImagesPathText.Text = _cfg.ImagesFolder;
            ModelPathText.Text = "Pasta do modelo: " + _cfg.VoiceModelPath;
        }

        // ---------------- navegação ----------------

        private void OnNavChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Nav.SelectedItem is not NavItem item) return;
            foreach (var name in new[] { "PageHome", "PageScreens", "PageDevice", "PagePet", "PageVoice", "PageGames", "PageDownloads", "PageImages", "PageGeneral" })
            {
                if (FindName(name) is FrameworkElement page)
                    page.Visibility = name == item.Page ? Visibility.Visible : Visibility.Collapsed;
            }
            PageScroll?.ScrollToTop();
            if (item.Page == "PageImages") LoadThumbs();
            if (item.Page == "PageDevice") FillPorts();
        }

        // ---------------- Início ----------------

        private void OnPrev(object sender, RoutedEventArgs e) => App.Engine.Post("anterior");
        private void OnNext(object sender, RoutedEventArgs e) => App.Engine.Post("proxima");
        private void OnPause(object sender, RoutedEventArgs e) => App.Engine.Post("alternar_pausa");
        private void OnReconnect(object sender, RoutedEventArgs e) => App.Engine.Post("reconectar");

        // ---------------- Telas ----------------

        private void SaveScreens()
        {
            if (_loading) return;
            _cfg.Screens = _screens.Select(r => new ScreenSetting { Id = r.Id, Enabled = r.Enabled }).ToList();
            _cfg.Save();
        }

        private void OnScreenToggled(object sender, RoutedEventArgs e) => Dispatcher.BeginInvoke(new Action(SaveScreens));

        private void MoveScreen(object sender, int dir)
        {
            if ((sender as FrameworkElement)?.Tag is not ScreenRow row) return;
            int i = _screens.IndexOf(row), j = i + dir;
            if (i < 0 || j < 0 || j >= _screens.Count) return;
            _screens.Move(i, j);
            SaveScreens();
        }

        private void OnScreenUp(object sender, RoutedEventArgs e) => MoveScreen(sender, -1);
        private void OnScreenDown(object sender, RoutedEventArgs e) => MoveScreen(sender, 1);

        // ---------------- Tela física ----------------

        private void OnOrientationChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            _cfg.Flipped = OrientationBox.SelectedIndex == 1;
        }

        private void FillPorts()
        {
            var text = _cfg.Port;
            PortBox.Items.Clear();
            PortBox.Items.Add("Automático");
            foreach (var p in SerialPort.GetPortNames().OrderBy(p => p)) PortBox.Items.Add(p);
            PortBox.Text = text;
        }

        private void OnPortDropDown(object sender, EventArgs e) => FillPorts();

        // ---------------- Bichinho ----------------

        private void OnPetStateChanged(object sender, SelectionChangedEventArgs e)
        {
            PetPreviewHint.Text = PetStateBox.SelectedIndex <= 0 ? "Mostrando o humor de agora" : "Mostrando um humor específico";
            _petNext = DateTime.MinValue;
        }

        // ---------------- Voz ----------------

        private void LoadVoiceRows(IEnumerable<VoiceCommand> cmds)
        {
            _voice.Clear();
            foreach (var c in cmds) _voice.Add(new VoiceRow { Phrase = c.Phrase, Action = c.Action });
        }

        private void SaveVoice()
        {
            _cfg.VoiceCommands = _voice.Where(v => !string.IsNullOrWhiteSpace(v.Phrase) && !string.IsNullOrWhiteSpace(v.Action))
                                       .Select(v => new VoiceCommand { Phrase = v.Phrase.Trim(), Action = v.Action }).ToList();
            _cfg.Save();
        }

        private void OnVoiceToggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            Dispatcher.BeginInvoke(new Action(() => App.Voice.Restart()));
        }

        private void OnVoiceEdited(object sender, DataGridCellEditEndingEventArgs e) =>
            Dispatcher.BeginInvoke(new Action(SaveVoice), DispatcherPriority.Background);

        private void OnVoiceAdd(object sender, RoutedEventArgs e)
        {
            var row = new VoiceRow { Phrase = "nova frase", Action = "proxima" };
            _voice.Add(row);
            VoiceGrid.SelectedItem = row;
            VoiceGrid.ScrollIntoView(row);
        }

        private void OnVoiceRemove(object sender, RoutedEventArgs e)
        {
            if (VoiceGrid.SelectedItem is VoiceRow row) _voice.Remove(row);
            SaveVoice();
        }

        private void OnVoiceReset(object sender, RoutedEventArgs e)
        {
            LoadVoiceRows(Config.DefaultVoiceCommands());
            SaveVoice();
        }

        private void OnVoiceApply(object sender, RoutedEventArgs e)
        {
            VoiceGrid.CommitEdit(DataGridEditingUnit.Row, true);
            SaveVoice();
            App.Voice.Restart();
        }

        private void OnModelSite(object sender, RoutedEventArgs e) => OpenUrl("https://alphacephei.com/vosk/models");

        private void OnPickModel(object sender, RoutedEventArgs e)
        {
            var path = PickFolder("Escolha a pasta do modelo de voz (vosk-model-small-pt…)", _cfg.VoiceModelPath);
            if (path == null) return;
            _cfg.VoiceModelPath = path;
            RefreshPaths();
            App.Voice.Restart();
        }

        // ---------------- Downloads / Imagens ----------------

        private void OnPickDownloads(object sender, RoutedEventArgs e)
        {
            var path = PickFolder("Escolha a pasta de downloads do navegador", _cfg.DownloadsFolder);
            if (path == null) return;
            _cfg.DownloadsFolder = path;
            RefreshPaths();
        }

        private void OnPickImages(object sender, RoutedEventArgs e)
        {
            var path = PickFolder("Escolha a pasta das imagens", _cfg.ImagesFolder);
            if (path == null) return;
            _cfg.ImagesFolder = path;
            RefreshPaths();
            LoadThumbs();
        }

        private void OnOpenImages(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(_cfg.ImagesFolder);
                OpenUrl(_cfg.ImagesFolder);
            }
            catch { }
        }

        private void LoadThumbs()
        {
            var list = new List<BitmapSource>();
            try
            {
                if (Directory.Exists(_cfg.ImagesFolder))
                {
                    var exts = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".gif" };
                    foreach (var f in Directory.GetFiles(_cfg.ImagesFolder).Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant())).OrderBy(f => f).Take(24))
                    {
                        try
                        {
                            var bi = new BitmapImage();
                            bi.BeginInit();
                            bi.CacheOption = BitmapCacheOption.OnLoad;
                            bi.UriSource = new Uri(f);
                            bi.DecodePixelWidth = 240;
                            bi.EndInit();
                            bi.Freeze();
                            list.Add(bi);
                        }
                        catch { }
                    }
                }
            }
            catch { }
            ImageThumbs.ItemsSource = list;
            ImagesEmpty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // ---------------- Geral ----------------

        private void OnStartupToggled(object sender, RoutedEventArgs e)
        {
            bool want = StartupSwitch.IsChecked == true;
            bool ok = want ? Startup.Enable() : Startup.Disable();
            if (!ok) StartupSwitch.IsChecked = Startup.IsEnabled();
        }

        private void OnOpenConfigFolder(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(Config.Folder);
            OpenUrl(Config.Folder);
        }

        // ---------------- utilitários ----------------

        private static string PickFolder(string title, string initial)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = title };
            if (!string.IsNullOrWhiteSpace(initial) && Directory.Exists(initial)) dlg.InitialDirectory = initial;
            return dlg.ShowDialog() == true ? dlg.FolderName : null;
        }

        private static void OpenUrl(string target)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
        }
    }
}
