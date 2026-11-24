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

        // ---------------- Bichinho ----------------
        private string _petName = "Bit";
        public string PetName { get => _petName; set => Set(ref _petName, string.IsNullOrWhiteSpace(value) ? "Bit" : value.Trim()); }

        private double _petSleepMinutes = 5;
        public double PetSleepMinutes { get => _petSleepMinutes; set => Set(ref _petSleepMinutes, Math.Clamp(Math.Round(value), 1, 120)); }

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
                VoiceCommands = DefaultVoiceCommands();
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

        public string[] ExtraGamesList() =>
            ExtraGames.Split(new[] { '\n', '\r', ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                      .Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0).ToArray();
    }

    /// <summary>Nomes e ícones das telas e das ações de voz (para a interface).</summary>
    public static class ScreenInfo
    {
        public static readonly string[] RotationIds =
            { "temperaturas", "musica", "notificacoes", "downloads", "bichinho", "imagem", "relogio" };

        public static string Name(string id) => id switch
        {
            "temperaturas" => "Temperaturas",
            "musica" => "Música",
            "notificacoes" => "Notificações",
            "downloads" => "Downloads",
            "bichinho" => "Bichinho",
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
            "bichinho" => "O Bit, que reage ao seu PC",
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
            ("bichinho", "Mostrar Bichinho"),
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
