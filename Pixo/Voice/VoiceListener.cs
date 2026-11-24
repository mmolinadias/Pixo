using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using NAudio.Wave;
using Pixo.Core;
using Vosk;

namespace Pixo.Voice
{
    /// <summary>
    /// Comandos de voz offline em português (Vosk + microfone pelo NAudio).
    /// O reconhecimento fica restrito às frases cadastradas, o que deixa tudo mais preciso.
    /// </summary>
    public sealed class VoiceListener : IDisposable
    {
        private readonly Config _cfg;
        private readonly Action<string> _onAction;
        private Model _model;
        private string _modelPath;
        private VoskRecognizer _rec;
        private WaveInEvent _mic;
        private Dictionary<string, string> _map = new Dictionary<string, string>();
        private readonly object _lock = new object();

        public string Status { get; private set; } = "Desligado";
        public bool Listening { get; private set; }
        public string LastHeard { get; private set; } = "";
        /// <summary>O que o reconhecedor está entendendo agora (mesmo que não seja um comando).</summary>
        public string LastRaw { get; private set; } = "";
        /// <summary>Volume do microfone agora, de 0 a 1.</summary>
        public double Level { get; private set; }
        public event Action Changed;

        /// <summary>Microfones do Windows: (número, nome). -1 = padrão.</summary>
        public static List<(int Id, string Name)> Microphones()
        {
            var list = new List<(int, string)> { (-1, "Padrão do Windows") };
            try
            {
                for (int i = 0; i < WaveInEvent.DeviceCount; i++)
                    list.Add((i, WaveInEvent.GetCapabilities(i).ProductName));
            }
            catch { }
            return list;
        }

        // registro do que foi ouvido (voz.txt ao lado do Pixo.exe), para diagnóstico
        private readonly Queue<string> _log = new Queue<string>();
        private void Log(string line)
        {
            try
            {
                lock (_log)
                {
                    _log.Enqueue(DateTime.Now.ToString("HH:mm:ss") + "  " + line);
                    while (_log.Count > 80) _log.Dequeue();
                    File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "voz.txt"), _log);
                }
            }
            catch { }
        }

        public VoiceListener(Config cfg, Action<string> onAction)
        {
            _cfg = cfg;
            _onAction = onAction;
        }

        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var d = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var c in d)
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            return sb.ToString().Trim();
        }

        /// <summary>
        /// Arquivo-sentinela: criado antes de carregar o modelo e apagado depois.
        /// Se o app travar no meio (erro nativo do Vosk), na próxima abertura a voz
        /// vem desligada em vez de travar de novo.
        /// </summary>
        private static string CrashMarker => Path.Combine(Config.Folder, "voz-carregando.tmp");

        /// <summary>Chamar ao abrir o app: se a última carga do modelo travou, desliga a voz.</summary>
        public bool RecoverFromCrash()
        {
            if (!File.Exists(CrashMarker)) return false;
            try { File.Delete(CrashMarker); } catch { }
            _cfg.VoiceEnabled = false;
            _cfg.Save();
            SetStatus("A voz travou ao carregar o modelo e foi desligada. Escolha outro modelo (recomendado: vosk-model-small-pt).", false);
            return true;
        }

        /// <summary>
        /// A lista de comandos (gramática) só é segura em modelos pequenos com grafo dinâmico
        /// (graph/Gr.fst + HCLr.fst) e sem reavaliação (rescore/rnnlm). Nos outros, o Vosk pode
        /// derrubar o app ao receber a lista; então ouvimos a fala livre e procuramos os comandos nela.
        /// </summary>
        /// <summary>Procura um arquivo do modelo no formato novo (subpasta) ou antigo (tudo na raiz).</summary>
        private static bool Has(string path, string sub, string file) =>
            File.Exists(Path.Combine(path, sub, file)) || File.Exists(Path.Combine(path, file));

        /// <summary>É uma pasta de modelo do Vosk (formato novo com am/ ou antigo com final.mdl na raiz)?</summary>
        private static bool IsModelFolder(string path) =>
            Directory.Exists(path) && Has(path, "am", "final.mdl");

        private static bool SupportsGrammar(string path) =>
            Has(path, "graph", "Gr.fst")
            && Has(path, "graph", "HCLr.fst")
            && !Has(path, "graph", "HCLG.fst")
            && !Directory.Exists(Path.Combine(path, "rescore"))
            && !Directory.Exists(Path.Combine(path, "rnnlm"));

        /// <summary>
        /// A biblioteca do Vosk para Windows não lê arquivos com mais de 2 GB e derruba o app.
        /// Devolve o nome do arquivo grande demais, ou null.
        /// </summary>
        private static string TooLargeFile(string path)
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    if (new FileInfo(f).Length >= 2L * 1024 * 1024 * 1024)
                        return Path.GetRelativePath(path, f);
            }
            catch { }
            return null;
        }

        private static bool IsBigModel(string path) =>
            Directory.Exists(Path.Combine(path, "rnnlm")) || Directory.Exists(Path.Combine(path, "rescore")) || !SupportsGrammar(path);

        /// <summary>Liga (ou religa, depois de mudar comandos/modelo) conforme a configuração.
        /// Carrega o modelo em segundo plano para a janela não congelar.</summary>
        public void Restart()
        {
            Stop();
            if (!_cfg.VoiceEnabled) { SetStatus("Desligado", false); return; }

            var path = _cfg.VoiceModelPath;
            if (!IsModelFolder(path))
            {
                SetStatus("Modelo de voz não encontrado nesta pasta", false);
                return;
            }
            if (path.Any(ch => ch > 127))
            {
                SetStatus("O caminho do modelo tem acentos ou símbolos. Mova a pasta para um caminho simples, ex.: C:\\Pixo\\modelo-voz", false);
                return;
            }

            var huge = TooLargeFile(path);
            if (huge != null)
            {
                var folder = huge.Split(Path.DirectorySeparatorChar)[0];
                SetStatus("O arquivo \"" + huge + "\" passa de 2 GB e o Vosk não consegue ler no Windows. " +
                          "Renomeie a pasta \"" + folder + "\" do modelo para \"_" + folder + "\" e ligue a voz de novo.", false);
                return;
            }

            bool big = IsBigModel(path);
            SetStatus(big ? "Carregando modelo grande… (pode levar 1 minuto)" : "Carregando modelo…", false);
            System.Threading.Tasks.Task.Run(() => Load(path, big));
        }

        private void Load(string path, bool big)
        {
            try
            {
                Vosk.Vosk.SetLogLevel(-1);
                // o sentinela cobre o carregamento do modelo E a criação do reconhecedor
                Directory.CreateDirectory(Config.Folder);
                File.WriteAllText(CrashMarker, path);
                if (_model == null || _modelPath != path)
                {
                    _model?.Dispose();
                    _model = null;
                    _model = new Model(path);
                    _modelPath = path;
                }

                var commands = _cfg.VoiceCommands.Where(c => !string.IsNullOrWhiteSpace(c.Phrase) && !string.IsNullOrWhiteSpace(c.Action)).ToList();
                _map = new Dictionary<string, string>();
                foreach (var c in commands) _map[Normalize(c.Phrase)] = c.Action;

                lock (_lock)
                {
                    if (!SupportsGrammar(path))
                    {
                        // modelo grande: reconhece a fala livre e o Pixo procura os comandos nela
                        _rec = new VoskRecognizer(_model, 16000f);
                    }
                    else
                    {
                        var grammar = commands.Select(c => c.Phrase.Trim().ToLowerInvariant()).Distinct().ToList();
                        grammar.Add("[unk]");
                        // mantém os acentos como texto (sem \u00e1), que é o que o Vosk espera
                        var json = JsonSerializer.Serialize(grammar, new JsonSerializerOptions
                        {
                            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                        });
                        _rec = new VoskRecognizer(_model, 16000f, json);
                    }
                }
                try { File.Delete(CrashMarker); } catch { }

                if (WaveInEvent.DeviceCount == 0)
                {
                    SetStatus("Nenhum microfone encontrado", false);
                    ScheduleRetry();
                    return;
                }
                StartMic();
            }
            catch (Exception ex)
            {
                SetStatus("Erro na voz: " + ex.Message, false);
            }
        }

        // ---------------- microfone (com recuperação automática) ----------------

        private DateTime _lastAudio = DateTime.MinValue;
        private DateTime _lastFired = DateTime.MinValue;
        private string _lastFiredKey;
        private System.Threading.Timer _watchdog;
        private volatile bool _stopping;

        private void StartMic()
        {
            _stopping = false;
            // -1 = microfone padrão do Windows (acompanha quando você troca de microfone)
            int dev = _cfg.MicDevice;
            if (dev >= WaveInEvent.DeviceCount) dev = -1;
            _mic = new WaveInEvent { WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 100, DeviceNumber = dev };
            _mic.DataAvailable += OnAudio;
            _mic.RecordingStopped += OnMicStopped;
            _mic.StartRecording();
            _lastAudio = DateTime.Now;
            string micName = dev < 0 ? "padrão do Windows" : WaveInEvent.GetCapabilities(dev).ProductName;
            Log("Ouvindo pelo microfone: " + micName + " | modelo: " + _cfg.VoiceModelPath + " | lista de comandos: " + (SupportsGrammar(_cfg.VoiceModelPath) ? "sim" : "não"));
            SetStatus("Ouvindo", true);

            // vigia: se o microfone parar de mandar som por 4 s, religa
            _watchdog?.Dispose();
            _watchdog = new System.Threading.Timer(_ =>
            {
                if (!_stopping && Listening && (DateTime.Now - _lastAudio).TotalSeconds > 4) RestartMic("o microfone parou de responder");
            }, null, 4000, 2000);
        }

        private void OnMicStopped(object sender, StoppedEventArgs e)
        {
            if (_stopping) return;
            RestartMic(e.Exception != null ? "o microfone foi desconectado" : "a gravação parou");
        }

        private int _restarting;

        /// <summary>Religa só o microfone (o modelo continua carregado).</summary>
        private void RestartMic(string why)
        {
            if (System.Threading.Interlocked.Exchange(ref _restarting, 1) == 1) return;
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    SetStatus("Religando o microfone (" + why + ")…", false);
                    StopMicOnly();
                    await System.Threading.Tasks.Task.Delay(1500);
                    if (!_cfg.VoiceEnabled || _rec == null) return;
                    if (WaveInEvent.DeviceCount == 0) { SetStatus("Nenhum microfone encontrado", false); ScheduleRetry(); return; }
                    lock (_lock) _rec?.Reset();
                    StartMic();
                }
                catch (Exception ex)
                {
                    SetStatus("Erro no microfone: " + ex.Message, false);
                    ScheduleRetry();
                }
                finally { System.Threading.Interlocked.Exchange(ref _restarting, 0); }
            });
        }

        private void ScheduleRetry()
        {
            _watchdog?.Dispose();
            _watchdog = new System.Threading.Timer(_ =>
            {
                if (!_stopping && !Listening && _cfg.VoiceEnabled && _rec != null && WaveInEvent.DeviceCount > 0)
                    RestartMic("tentando de novo");
            }, null, 5000, 5000);
        }

        private void StopMicOnly()
        {
            _stopping = true;
            try
            {
                if (_mic != null)
                {
                    _mic.DataAvailable -= OnAudio;
                    _mic.RecordingStopped -= OnMicStopped;
                    _mic.StopRecording();
                    _mic.Dispose();
                }
            }
            catch { }
            _mic = null;
            Listening = false;
        }

        private DateTime _lastLevelEvent = DateTime.MinValue;
        private double _peak;
        private string _lastPartialLogged = "";

        private void OnAudio(object sender, WaveInEventArgs e)
        {
            _lastAudio = DateTime.Now;

            // volume (pico do bloco), para o medidor da janela
            int max = 0;
            for (int i = 0; i + 1 < e.BytesRecorded; i += 2)
            {
                int v = Math.Abs((short)(e.Buffer[i] | (e.Buffer[i + 1] << 8)));
                if (v > max) max = v;
            }
            _peak = Math.Max(_peak * 0.7, max / 32768.0);
            Level = _peak;
            if ((DateTime.Now - _lastLevelEvent).TotalMilliseconds > 150)
            {
                _lastLevelEvent = DateTime.Now;
                Changed?.Invoke();
            }

            string text = null, partial = null;
            try
            {
                lock (_lock)
                {
                    if (_rec == null) return;
                    if (_rec.AcceptWaveform(e.Buffer, e.BytesRecorded))
                    {
                        using var doc = JsonDocument.Parse(_rec.Result());
                        text = doc.RootElement.TryGetProperty("text", out var t) ? t.GetString() : null;
                    }
                    else
                    {
                        // resultado parcial: reage sem esperar a frase "fechar"
                        // (com barulho de fundo constante, o Vosk pode demorar muito para fechar)
                        using var doc = JsonDocument.Parse(_rec.PartialResult());
                        partial = doc.RootElement.TryGetProperty("partial", out var p) ? p.GetString() : null;
                    }
                }
            }
            catch { return; }

            if (text != null && text.Trim().Length > 0)
            {
                LastRaw = text;
                Log("ouvi: \"" + text + "\" (volume máx " + Math.Round(_peak * 100) + "%)");
                Changed?.Invoke();
            }
            else if (!string.IsNullOrWhiteSpace(partial) && partial != _lastPartialLogged)
            {
                _lastPartialLogged = partial;
                LastRaw = partial + "…";
                Changed?.Invoke();
            }

            if (!string.IsNullOrWhiteSpace(text) && text != "[unk]") { TryFire(text, false); return; }
            if (!string.IsNullOrWhiteSpace(partial)) TryFire(partial, true);
        }

        private void TryFire(string heard, bool fromPartial)
        {
            var norm = Normalize(heard);
            // a frase mais longa primeiro ("desligar tela" antes de "tela")
            foreach (var key in _map.Keys.OrderByDescending(k => k.Length))
            {
                if (!(" " + norm + " ").Contains(" " + key + " ")) continue;

                // evita disparar duas vezes (parcial + resultado final da mesma frase)
                if (key == _lastFiredKey && (DateTime.Now - _lastFired).TotalSeconds < 2.5) return;
                _lastFired = DateTime.Now;
                _lastFiredKey = key;
                if (fromPartial) lock (_lock) _rec?.Reset(); // começa a próxima frase do zero

                LastHeard = heard;
                Log("COMANDO: \"" + key + "\" -> " + _map[key]);
                Changed?.Invoke();
                _onAction(_map[key]);
                return;
            }
        }

        private void SetStatus(string s, bool listening)
        {
            if (s != Status) Log("status: " + s);
            Status = s;
            Listening = listening;
            Changed?.Invoke();
        }

        public void Stop()
        {
            _watchdog?.Dispose();
            _watchdog = null;
            StopMicOnly();
            lock (_lock)
            {
                _rec?.Dispose();
                _rec = null;
            }
            Listening = false;
        }

        public void Dispose()
        {
            Stop();
            _model?.Dispose();
            _model = null;
        }
    }
}
