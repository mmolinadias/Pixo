using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pixo.Core
{
    public class ScreenSetting
    {
        public string Id { get; set; }
        public bool Enabled { get; set; } = true;
    }

    public class VoiceCommand
    {
        public string Phrase { get; set; }
        public string Action { get; set; }
    }

    /// <summary>"Quando este app estiver em foco, mostrar esta tela."</summary>
    public class AppScreenRule
    {
        public string App { get; set; }      // nome do .exe, ex.: spotify.exe
        public string Screen { get; set; }   // id da tela, ex.: musica
    }

    /// <summary>Todas as configurações do Pixo. Salvas em %AppData%\Pixo\config.json.</summary>
    public class Config : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            return true;
        }

        // ---------------- Tela física ----------------
        private string _port = "Automático";
        public string Port { get => _port; set => Set(ref _port, value); }

        private double _brightness = 60;
        public double Brightness { get => _brightness; set => Set(ref _brightness, Math.Clamp(Math.Round(value), 0, 100)); }

        private bool _flipped;
        /// <summary>false = Paisagem, true = Paisagem invertida.</summary>
        public bool Flipped { get => _flipped; set => Set(ref _flipped, value); }

        // ---------------- Correção de cor (só na tela física) ----------------
        private double _colorSaturation = 140;
        /// <summary>100 = original. Acima disso, cores mais vivas.</summary>
        public double ColorSaturation { get => _colorSaturation; set => Set(ref _colorSaturation, Math.Clamp(Math.Round(value), 50, 200)); }

        private double _colorContrast = 104;
        /// <summary>100 = original. Acima disso, pretos mais pretos e brancos mais brancos.</summary>
        public double ColorContrast { get => _colorContrast; set => Set(ref _colorContrast, Math.Clamp(Math.Round(value), 50, 200)); }

        private double _colorGamma = 100;
        /// <summary>100 = original. Acima disso, tons médios mais escuros e encorpados.</summary>
        public double ColorGamma { get => _colorGamma; set => Set(ref _colorGamma, Math.Clamp(Math.Round(value), 50, 200)); }

        // ---------------- Rotação ----------------
        private double _secondsPerScreen = 15;
        public double SecondsPerScreen { get => _secondsPerScreen; set => Set(ref _secondsPerScreen, Math.Clamp(Math.Round(value), 5, 300)); }

        private double _pauseAfterVoice = 60;
        public double PauseAfterVoice { get => _pauseAfterVoice; set => Set(ref _pauseAfterVoice, Math.Clamp(Math.Round(value), 0, 600)); }

        private List<ScreenSetting> _screens = new List<ScreenSetting>();
        /// <summary>Ordem da rotação. A lista é sempre trocada inteira (nunca editada no lugar).</summary>
        public List<ScreenSetting> Screens { get => _screens; set => Set(ref _screens, value); }

        // ---------------- Música ----------------
        private bool _onlySpotify;
        public bool OnlySpotify { get => _onlySpotify; set => Set(ref _onlySpotify, value); }

        // ---------------- Bichinhos (Fred & Dengo) ----------------
        private string _petName1 = "Fred";
        /// <summary>Nome do cachorro.</summary>
        public string PetName1 { get => _petName1; set => Set(ref _petName1, string.IsNullOrWhiteSpace(value) ? "Fred" : value.Trim()); }

        private string _petName2 = "Dengo";
        /// <summary>Nome do gato.</summary>
        public string PetName2 { get => _petName2; set => Set(ref _petName2, string.IsNullOrWhiteSpace(value) ? "Dengo" : value.Trim()); }

        private double _petSleepMinutes = 5;
        public double PetSleepMinutes { get => _petSleepMinutes; set => Set(ref _petSleepMinutes, Math.Clamp(Math.Round(value), 1, 120)); }

        private double _petMood = 70;
        /// <summary>Humor da dupla (0–100): sobe com carinho, cai devagar quando eles ficam sem atenção.</summary>
        public double PetMood { get => _petMood; set => Set(ref _petMood, Math.Clamp(value, 0, 100)); }

        private DateTime _petMoodAt = DateTime.Now;
        /// <summary>Quando o humor foi atualizado pela última vez (para descontar o tempo com o Pixo fechado).</summary>
        public DateTime PetMoodAt { get => _petMoodAt; set => Set(ref _petMoodAt, value); }

        private double _petBreakMinutes = 90;
        /// <summary>Depois de quanto tempo seguido no PC a dupla lembra de fazer uma pausa (0 = nunca).</summary>
        public double PetBreakMinutes { get => _petBreakMinutes; set => Set(ref _petBreakMinutes, Math.Clamp(Math.Round(value / 15) * 15, 0, 240)); }

        // ---------------- Clima ----------------
        private string _weatherCity = "";
        /// <summary>Vazio = descobre a cidade pela internet. Senão, o nome da cidade (ex.: "Campinas").</summary>
        public string WeatherCity { get => _weatherCity; set => Set(ref _weatherCity, (value ?? "").Trim()); }

        // ---------------- Ar-condicionado LG (ThinQ Connect) ----------------
        private string _acTokenProtected = "";
        /// <summary>Token pessoal da LG, criptografado para este usuário do Windows (DPAPI). Nunca em texto puro.</summary>
        public string AcTokenProtected { get => _acTokenProtected; set => Set(ref _acTokenProtected, value ?? ""); }

        private string _acDeviceId = "";
        public string AcDeviceId { get => _acDeviceId; set => Set(ref _acDeviceId, value ?? ""); }

        private string _acName = "";
        public string AcName { get => _acName; set => Set(ref _acName, value ?? ""); }

        private string _acCountry = "BR";
        public string AcCountry { get => _acCountry; set => Set(ref _acCountry, string.IsNullOrWhiteSpace(value) ? "BR" : value.Trim().ToUpperInvariant()); }

        private string _acClientId = "";
        /// <summary>A LG pede um id fixo para cada programa cliente: um aleatório, criado uma vez.</summary>
        public string AcClientId { get => _acClientId; set => Set(ref _acClientId, value ?? ""); }

        // ---------------- Pixo API (avisos de outros programas, ex.: Claude Code) ----------------
        private bool _apiEnabled = true;
        public bool ApiEnabled { get => _apiEnabled; set => Set(ref _apiEnabled, value); }

        private int _apiPort = 7480;
        public int ApiPort { get => _apiPort; set => Set(ref _apiPort, Math.Clamp(value, 1024, 65535)); }

        /// <summary>Até qual versão os comandos de voz novos já foram acrescentados (para não voltar os que você apagou).</summary>
        public int VoiceDefaultsVersion { get; set; }

        // ---------------- Voz ----------------
        private bool _voiceEnabled = true;
        public bool VoiceEnabled { get => _voiceEnabled; set => Set(ref _voiceEnabled, value); }

        private string _voiceModelPath = Path.Combine(Folder, "modelo-voz");
        public string VoiceModelPath { get => _voiceModelPath; set => Set(ref _voiceModelPath, value); }

        private int _micDevice = -1;
        /// <summary>-1 = microfone padrão do Windows; 0, 1, 2... = um microfone específico.</summary>
        public int MicDevice { get => _micDevice; set => Set(ref _micDevice, value); }

        private List<VoiceCommand> _voiceCommands = new List<VoiceCommand>();
        public List<VoiceCommand> VoiceCommands { get => _voiceCommands; set => Set(ref _voiceCommands, value); }

        // ---------------- Tela por app em foco ----------------
        private bool _appScreensEnabled = true;
        public bool AppScreensEnabled { get => _appScreensEnabled; set => Set(ref _appScreensEnabled, value); }

        private List<AppScreenRule> _appScreens;
        /// <summary>Regras "app em foco → tela" (null = ainda não criadas: usa as padrão).</summary>
        public List<AppScreenRule> AppScreens { get => _appScreens; set => Set(ref _appScreens, value); }

        // ---------------- Jogos ----------------
        private bool _gameModeAuto = true;
        public bool GameModeAuto { get => _gameModeAuto; set => Set(ref _gameModeAuto, value); }

        private string _extraGames = "javaw.exe\nMinecraft.Windows.exe";
        /// <summary>Um .exe por linha.</summary>
        public string ExtraGames { get => _extraGames; set => Set(ref _extraGames, value ?? ""); }

        // ---------------- Downloads ----------------
        private string _downloadsFolder = "";
        public string DownloadsFolder { get => _downloadsFolder; set => Set(ref _downloadsFolder, value ?? ""); }

        private string _qbittorrentUrl = "http://localhost:8080";
        public string QBittorrentUrl { get => _qbittorrentUrl; set => Set(ref _qbittorrentUrl, value ?? ""); }

        // ---------------- Imagens ----------------
        private string _imagesFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Pixo");
        public string ImagesFolder { get => _imagesFolder; set => Set(ref _imagesFolder, value); }

        private bool _imageFill = true;
        public bool ImageFill { get => _imageFill; set => Set(ref _imageFill, value); }

        // ---------------- Arquivo ----------------
        [JsonIgnore]
        public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pixo");
        private static string FilePath => Path.Combine(Folder, "config.json");

        [JsonIgnore] public static bool IsFirstRun { get; private set; }

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions { WriteIndented = true };

        public static Config Load()
        {
            Config c = null;
            try
            {
                if (File.Exists(FilePath))
                    c = JsonSerializer.Deserialize<Config>(File.ReadAllText(FilePath), JsonOpts);
            }
            catch { /* arquivo corrompido: volta ao padrão */ }

            IsFirstRun = c == null;
            c ??= new Config();
            c.FillDefaults();
            return c;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts));
            }
            catch { }
        }

        private void FillDefaults()
        {
            // Telas: garante que todas existam, mantendo a ordem salva
            var known = ScreenInfo.RotationIds;
            var list = (Screens ?? new List<ScreenSetting>()).Where(s => s?.Id != null && known.Contains(s.Id)).ToList();
            foreach (var id in known)
                if (!list.Any(s => s.Id == id))
                    list.Add(new ScreenSetting { Id = id, Enabled = id != "relogio" });
            Screens = list;

            if (VoiceCommands == null || VoiceCommands.Count == 0)
            {
                VoiceCommands = DefaultVoiceCommands();
                VoiceDefaultsVersion = 3;
            }
            else if (VoiceDefaultsVersion < 2)
            {
                // versão 2: clima e carinho (acrescenta só o que ainda não existe)
                var list2 = VoiceCommands.ToList();
                foreach (var c in DefaultVoiceCommands().Where(c => c.Action == "clima" || c.Action == "carinho"))
                    if (!list2.Any(v => string.Equals(v.Phrase, c.Phrase, StringComparison.OrdinalIgnoreCase)))
                        list2.Add(c);
                VoiceCommands = list2;
                VoiceDefaultsVersion = 2;
            }
            if (VoiceDefaultsVersion < 3)
            {
                // versão 3: saúde do PC
                var list3 = VoiceCommands.ToList();
                foreach (var c in DefaultVoiceCommands().Where(c => c.Action == "saude"))
                    if (!list3.Any(v => string.Equals(v.Phrase, c.Phrase, StringComparison.OrdinalIgnoreCase)))
                        list3.Add(c);
                VoiceCommands = list3;
                VoiceDefaultsVersion = 3;
            }
            AppScreens ??= DefaultAppScreens();
            if (string.IsNullOrEmpty(AcClientId)) AcClientId = "pixo-" + Guid.NewGuid().ToString("N");
        }

        public static List<VoiceCommand> DefaultVoiceCommands() => new List<VoiceCommand>
        {
            new VoiceCommand { Phrase = "temperatura", Action = "temperaturas" },
            new VoiceCommand { Phrase = "temperaturas", Action = "temperaturas" },
            new VoiceCommand { Phrase = "música", Action = "musica" },
            new VoiceCommand { Phrase = "notificações", Action = "notificacoes" },
            new VoiceCommand { Phrase = "mensagens", Action = "notificacoes" },
            new VoiceCommand { Phrase = "downloads", Action = "downloads" },
            new VoiceCommand { Phrase = "baixando", Action = "downloads" },
            new VoiceCommand { Phrase = "bichinho", Action = "bichinho" },
            new VoiceCommand { Phrase = "bichinhos", Action = "bichinho" },
            new VoiceCommand { Phrase = "carinho", Action = "carinho" },
            new VoiceCommand { Phrase = "clima", Action = "clima" },
            new VoiceCommand { Phrase = "tempo", Action = "clima" },
            new VoiceCommand { Phrase = "saúde", Action = "saude" },
            new VoiceCommand { Phrase = "jogo", Action = "gamer" },
            new VoiceCommand { Phrase = "imagem", Action = "imagem" },
            new VoiceCommand { Phrase = "foto", Action = "imagem" },
            new VoiceCommand { Phrase = "hora", Action = "relogio" },
            new VoiceCommand { Phrase = "relógio", Action = "relogio" },
            new VoiceCommand { Phrase = "próxima", Action = "proxima" },
            new VoiceCommand { Phrase = "anterior", Action = "anterior" },
            new VoiceCommand { Phrase = "parar", Action = "pausar_rotacao" },
            new VoiceCommand { Phrase = "continuar", Action = "continuar" },
            new VoiceCommand { Phrase = "apagar", Action = "apagar" },
            new VoiceCommand { Phrase = "acender", Action = "acender" },
            new VoiceCommand { Phrase = "pausar", Action = "midia:pausar" },
            new VoiceCommand { Phrase = "tocar", Action = "midia:tocar" },
            new VoiceCommand { Phrase = "pular", Action = "midia:pular" },
        };

        public static List<AppScreenRule> DefaultAppScreens() => new List<AppScreenRule>
        {
            new AppScreenRule { App = "spotify.exe", Screen = "musica" },
            new AppScreenRule { App = "qbittorrent.exe", Screen = "downloads" },
            new AppScreenRule { App = "code.exe", Screen = "claude" },
            new AppScreenRule { App = "devenv.exe", Screen = "claude" },
            new AppScreenRule { App = "windowsterminal.exe", Screen = "claude" },
            new AppScreenRule { App = "claude.exe", Screen = "claude" },
            new AppScreenRule { App = "blender.exe", Screen = "temperaturas" },
            new AppScreenRule { App = "resolve.exe", Screen = "temperaturas" },
            new AppScreenRule { App = "handbrake.exe", Screen = "temperaturas" },
            new AppScreenRule { App = "obs64.exe", Screen = "temperaturas" },
        };

        public string[] ExtraGamesList() =>
            ExtraGames.Split(new[] { '\n', '\r', ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                      .Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0).ToArray();
    }

    /// <summary>Nomes e ícones das telas e das ações de voz (para a interface).</summary>
    public static class ScreenInfo
    {
        public static readonly string[] RotationIds =
            { "temperaturas", "musica", "notificacoes", "downloads", "bichinho", "clima", "claude", "saude", "imagem", "relogio" };

        public static string Name(string id) => id switch
        {
            "temperaturas" => "Temperaturas",
            "musica" => "Música",
            "notificacoes" => "Notificações",
            "downloads" => "Downloads",
            "bichinho" => "Bichinhos",
            "clima" => "Clima",
            "claude" => "Claude Code",
            "saude" => "Saúde do PC",
            "imagem" => "Imagem",
            "relogio" => "Relógio",
            "gamer" => "Modo Gamer",
            _ => id
        };

        public static string Hint(string id) => id switch
        {
            "temperaturas" => "CPU, GPU, placa-mãe e SSD",
            "musica" => "Só aparece quando algo está tocando",
            "notificacoes" => "Só aparece quando tem notificação",
            "downloads" => "Só aparece quando tem download",
            "bichinho" => "Fred e Dengo, que reagem ao seu PC",
            "clima" => "Tempo lá fora e, com o ar LG, no quarto",
            "claude" => "O que o Claude Code está fazendo (só aparece com uma sessão aberta)",
            "saude" => "Disco, reinício, atualização e temperatura (só aparece quando tem aviso)",
            "imagem" => "Suas fotos, uma por vez",
            "relogio" => "Hora e data em letras grandes",
            _ => ""
        };

        public static string Icon(string id) => id switch
        {
            "temperaturas" => "thermo",
            "musica" => "music",
            "notificacoes" => "bell",
            "downloads" => "download",
            "bichinho" => "pet",
            "clima" => "cloudSun",
            "claude" => "claude",
            "saude" => "pulse",
            "imagem" => "image",
            "relogio" => "clock",
            "gamer" => "gamepad",
            _ => "info"
        };

        /// <summary>Ações possíveis de um comando de voz: (id, nome exibido).</summary>
        public static readonly (string Id, string Name)[] VoiceActions =
        {
            ("temperaturas", "Mostrar Temperaturas"),
            ("musica", "Mostrar Música"),
            ("notificacoes", "Mostrar Notificações"),
            ("downloads", "Mostrar Downloads"),
            ("bichinho", "Mostrar Bichinhos"),
            ("clima", "Mostrar Clima"),
            ("claude", "Mostrar Claude Code"),
            ("saude", "Mostrar Saúde do PC"),
            ("carinho", "Fazer carinho nos bichinhos"),
            ("imagem", "Mostrar Imagem"),
            ("relogio", "Mostrar Relógio"),
            ("gamer", "Mostrar Modo Gamer"),
            ("proxima", "Próxima tela"),
            ("anterior", "Tela anterior"),
            ("pausar_rotacao", "Pausar rotação"),
            ("continuar", "Continuar rotação"),
            ("apagar", "Apagar a tela"),
            ("acender", "Acender a tela"),
            ("midia:pausar", "Pausar música"),
            ("midia:tocar", "Tocar música"),
            ("midia:pular", "Pular música"),
        };
    }
}
