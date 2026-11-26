using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Pixo.Data
{
    public enum ClaudeStatus { Idle, Working, Waiting, Done }

    public class ClaudeAction
    {
        public string Icon, Text;
        public DateTime At;
    }

    /// <summary>Uma sessão do Claude Code, montada a partir dos hooks.</summary>
    public class ClaudeSession
    {
        public string Id, Project, Folder;
        public ClaudeStatus Status = ClaudeStatus.Idle;
        public string Prompt;                 // o pedido atual (primeira linha)
        public ClaudeAction Current;          // o que está fazendo agora
        public string WaitingFor;             // por que está esperando você
        public DateTime TurnStart, TurnEnd, LastEvent;
        public DateTime WaitingSince;         // desde quando está esperando você
        public bool Blocking;                 // esperando de verdade (permissão, pergunta), não só "parado"
        public DateTime WorkFrom;             // começo da fase "trabalhando" atual (para o placar do dia)
        public int Tools;                     // ações neste pedido
        public readonly HashSet<string> Files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly List<ClaudeAction> Recent = new List<ClaudeAction>();

        public TimeSpan Elapsed => (Status == ClaudeStatus.Working || Status == ClaudeStatus.Waiting ? DateTime.Now : TurnEnd) - TurnStart;
    }

    /// <summary>Placar do dia do Claude Code (zera à meia-noite; salvo em %AppData%\Pixo\claude-dia.json).</summary>
    public class ClaudeDay
    {
        public string Date { get; set; } = "";
        public int Prompts { get; set; }
        public int Tools { get; set; }
        public double WorkSeconds { get; set; }
        public long OutputTokens { get; set; }
        public List<string> Sessions { get; set; } = new List<string>();
        public List<string> Files { get; set; } = new List<string>();
        /// <summary>Mensagens já contadas (o transcript repete a mesma mensagem em várias linhas).</summary>
        public List<string> Messages { get; set; } = new List<string>();
        /// <summary>Até onde cada transcript já foi lido (em bytes).</summary>
        public Dictionary<string, long> Offsets { get; set; } = new Dictionary<string, long>();

        public ClaudeDay Copy() => new ClaudeDay
        {
            Date = Date, Prompts = Prompts, Tools = Tools, WorkSeconds = WorkSeconds, OutputTokens = OutputTokens,
            Sessions = Sessions.ToList(), Files = Files.ToList(),
        };
    }

    /// <summary>
    /// Painel do Claude Code: os hooks do Claude Code mandam cada evento (JSON) para a Pixo API (/claude) e aqui
    /// vira o estado de cada sessão: o pedido, o que ele está fazendo, há quanto tempo, quantas ações, se está
    /// esperando você. Tudo fica só neste PC.
    /// </summary>
    public static class ClaudeService
    {
        private static readonly object Lock = new object();
        private static readonly Dictionary<string, ClaudeSession> Sessions = new Dictionary<string, ClaudeSession>();

        /// <summary>Sessão mais recente que ainda vale mostrar (ou null) e quantas outras estão ativas.</summary>
        public static (ClaudeSession main, int others) Snapshot()
        {
            lock (Lock)
            {
                Cleanup();
                var list = Sessions.Values.OrderByDescending(s => s.Status == ClaudeStatus.Waiting).ThenByDescending(s => s.LastEvent).ToList();
                if (list.Count == 0) return (null, 0);
                var m = list[0];
                var copy = new ClaudeSession
                {
                    Id = m.Id, Project = m.Project, Folder = m.Folder, Status = m.Status, Prompt = m.Prompt, Current = m.Current,
                    WaitingFor = m.WaitingFor, TurnStart = m.TurnStart, TurnEnd = m.TurnEnd, LastEvent = m.LastEvent, Tools = m.Tools,
                };
                foreach (var f in m.Files) copy.Files.Add(f);
                copy.Recent.AddRange(m.Recent);
                return (copy, list.Count - 1);
            }
        }

        /// <summary>
        /// A espera mais longa de um Claude que pediu algo (permissão, pergunta) e ainda não teve resposta:
        /// quanto tempo (null = ninguém esperando) e desde quando (identifica a espera).
        /// </summary>
        public static (TimeSpan? wait, DateTime since) LongestWait()
        {
            lock (Lock)
            {
                Cleanup();
                var w = Sessions.Values.Where(s => s.Status == ClaudeStatus.Waiting && s.Blocking).OrderBy(s => s.WaitingSince).FirstOrDefault();
                return w == null ? (null, DateTime.MinValue) : (DateTime.Now - w.WaitingSince, w.WaitingSince);
            }
        }

        /// <summary>Algum Claude trabalhando agora (os bichinhos ficam "ocupados" junto).</summary>
        public static bool AnyWorking
        {
            get { lock (Lock) { Cleanup(); return Sessions.Values.Any(s => s.Status == ClaudeStatus.Working); } }
        }

        public static bool HasActive
        {
            get { lock (Lock) { Cleanup(); return Sessions.Count > 0; } }
        }

        /// <summary>
        /// Recebe um evento de hook. Devolve um comando para o Engine ("evento:claude-espera", "evento:claude-pronto")
        /// quando a tela deve reagir, ou null.
        /// </summary>
        public static string Handle(JsonNode e)
        {
            if (e == null) return null;
            string id = e["session_id"]?.ToString() ?? "sem-id";
            string evt = e["hook_event_name"]?.ToString() ?? "";
            var now = DateTime.Now;
            lock (Lock)
            {
                if (evt == "SessionEnd")
                {
                    if (Sessions.TryGetValue(id, out var ended)) { SetStatus(ended, ClaudeStatus.Idle, now); SaveDay(); }
                    Sessions.Remove(id);
                    return null;
                }
                if (!Sessions.TryGetValue(id, out var s))
                {
                    s = new ClaudeSession { Id = id, TurnStart = now, TurnEnd = now };
                    Sessions[id] = s;
                }
                var cwd = e["cwd"]?.ToString();
                if (!string.IsNullOrEmpty(cwd))
                {
                    s.Folder = cwd;
                    s.Project = Path.GetFileName(cwd.TrimEnd('\\', '/'));
                    if (string.IsNullOrEmpty(s.Project)) s.Project = cwd;
                }
                s.LastEvent = now;

                switch (evt)
                {
                    case "SessionStart":
                        if (s.Status != ClaudeStatus.Working) SetStatus(s, ClaudeStatus.Idle, now);
                        return null;

                    case "UserPromptSubmit":
                    {
                        SetStatus(s, ClaudeStatus.Working, now);
                        var day = Day;
                        day.Prompts++;
                        if (!day.Sessions.Contains(id)) day.Sessions.Add(id);
                        SaveDay();
                        s.Prompt = FirstLine(e["prompt"]?.ToString());
                        s.TurnStart = now;
                        s.Tools = 0;
                        s.Files.Clear();
                        s.Recent.Clear();
                        s.WaitingFor = null;
                        s.Current = new ClaudeAction { Icon = "claude", Text = "Pensando…", At = now };
                        return null;
                    }

                    case "PreToolUse":
                    {
                        bool wasIdle = s.Status != ClaudeStatus.Working;
                        SetStatus(s, ClaudeStatus.Working, now);
                        Day.Tools++;
                        s.WaitingFor = null;
                        if (wasIdle && (now - s.TurnEnd).TotalMinutes > 1) { s.TurnStart = now; s.Tools = 0; s.Files.Clear(); }
                        var a = Describe(e["tool_name"]?.ToString(), e["tool_input"], s);
                        a.At = now;
                        s.Current = a;
                        s.Tools++;
                        s.Recent.Add(a);
                        if (s.Recent.Count > 6) s.Recent.RemoveAt(0);
                        return null;
                    }

                    case "PostToolUse":
                        if (s.Status == ClaudeStatus.Working)
                            s.Current = new ClaudeAction { Icon = "claude", Text = "Pensando…", At = now };
                        return null;

                    case "Notification":
                    {
                        string msg = e["message"]?.ToString() ?? "";
                        SetStatus(s, ClaudeStatus.Waiting, now);
                        s.WaitingFor = Waiting(msg, out bool idle);
                        s.Blocking = !idle;
                        return "evento:claude-espera";
                    }

                    case "Stop":
                    {
                        bool long_ = s.Status == ClaudeStatus.Working && (now - s.TurnStart).TotalSeconds >= 30;
                        SetStatus(s, ClaudeStatus.Done, now);
                        SaveDay();
                        ReadTranscriptLater(e["transcript_path"]?.ToString());
                        s.TurnEnd = now;
                        s.WaitingFor = null;
                        s.Current = new ClaudeAction { Icon = "check", Text = "Pronto", At = now };
                        // só chama a atenção se o pedido demorou (respostas rápidas você já está vendo)
                        return long_ ? "evento:claude-pronto" : null;
                    }
                }
                return null;
            }
        }

        /// <summary>Sessões sem notícia há muito tempo saem do painel.</summary>
        private static void Cleanup()
        {
            var now = DateTime.Now;
            foreach (var id in Sessions.Where(kv =>
                         kv.Value.Status == ClaudeStatus.Working ? (now - kv.Value.LastEvent).TotalMinutes > 30
                                                                 : (now - kv.Value.LastEvent).TotalHours > 2)
                                       .Select(kv => kv.Key).ToList())
            {
                // trabalhando sem notícia há 30 min: conta o tempo só até o último evento
                var s = Sessions[id];
                if (s.Status == ClaudeStatus.Working) SetStatus(s, ClaudeStatus.Idle, s.LastEvent);
                Sessions.Remove(id);
            }
        }

        // ---------------- placar do dia ----------------

        private static ClaudeDay _day;
        private static string _dayFile;

        /// <summary>Outro arquivo para o placar (só para os testes não mexerem no de verdade).</summary>
        public static string DayFileOverride
        {
            get => _dayFile;
            set { lock (Lock) { _dayFile = value; _day = null; } }
        }

        private static string DayPath => _dayFile ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pixo", "claude-dia.json");
        private static string TodayKey => DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>O placar de hoje (chame com o Lock); vira o dia sozinho.</summary>
        private static ClaudeDay Day
        {
            get
            {
                if (_day == null)
                {
                    try { if (File.Exists(DayPath)) _day = JsonSerializer.Deserialize<ClaudeDay>(File.ReadAllText(DayPath)); }
                    catch { }
                    _day ??= new ClaudeDay { Date = TodayKey };
                }
                if (_day.Date != TodayKey)
                {
                    // dia novo: zera, mas lembra até onde cada transcript já foi lido
                    _day = new ClaudeDay { Date = TodayKey, Offsets = _day.Offsets };
                    foreach (var s in Sessions.Values.Where(x => x.Status == ClaudeStatus.Working)) s.WorkFrom = DateTime.Today;
                }
                return _day;
            }
        }

        private static void SaveDay()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(DayPath));
                File.WriteAllText(DayPath, JsonSerializer.Serialize(Day));
            }
            catch { }
        }

        /// <summary>Troca o estado da sessão, somando ao placar o tempo que ela passou trabalhando.</summary>
        private static void SetStatus(ClaudeSession s, ClaudeStatus st, DateTime now)
        {
            if (s.Status == ClaudeStatus.Working && st != ClaudeStatus.Working)
            {
                var from = s.WorkFrom < DateTime.Today ? DateTime.Today : s.WorkFrom;
                if (now > from) Day.WorkSeconds += (now - from).TotalSeconds;
            }
            if (st == ClaudeStatus.Working && s.Status != ClaudeStatus.Working) s.WorkFrom = now;
            if (st == ClaudeStatus.Waiting && s.Status != ClaudeStatus.Waiting) s.WaitingSince = now;
            s.Status = st;
        }

        /// <summary>Placar de hoje (cópia), contando também quem está trabalhando agora.</summary>
        public static ClaudeDay Today()
        {
            lock (Lock)
            {
                var d = Day.Copy();
                var now = DateTime.Now;
                foreach (var s in Sessions.Values.Where(x => x.Status == ClaudeStatus.Working))
                    d.WorkSeconds += Math.Max(0, (now - (s.WorkFrom < DateTime.Today ? DateTime.Today : s.WorkFrom)).TotalSeconds);
                return d;
            }
        }

        /// <summary>Lê o transcript em segundo plano (pode ser grande); só as linhas novas desde a última vez.</summary>
        private static void ReadTranscriptLater(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            Task.Run(() => ReadTranscript(path));
        }

        /// <summary>
        /// Soma os tokens escritos pelo Claude hoje (output_tokens das respostas), lendo o transcript de onde parou
        /// e sem contar duas vezes a mesma mensagem (ela aparece em várias linhas, uma por bloco).
        /// </summary>
        public static void ReadTranscript(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                long start;
                lock (Lock) start = Day.Offsets.TryGetValue(path, out var o) ? o : 0;
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (start > fs.Length) start = 0; // arquivo novo com o mesmo nome
                fs.Seek(start, SeekOrigin.Begin);
                var buf = new byte[fs.Length - start];
                int got = 0;
                while (got < buf.Length) { int n = fs.Read(buf, got, buf.Length - got); if (n <= 0) break; got += n; }
                if (got == 0) return;
                int end = Array.LastIndexOf(buf, (byte)'\n', got - 1);
                if (end < 0) return; // nenhuma linha completa ainda

                var found = new List<(string id, long tokens)>();
                foreach (var line in System.Text.Encoding.UTF8.GetString(buf, 0, end + 1).Split('\n'))
                {
                    if (line.Length < 10 || !line.Contains("\"output_tokens\"")) continue;
                    try
                    {
                        var j = JsonNode.Parse(line);
                        if (j?["type"]?.ToString() != "assistant") continue;
                        var ts = j["timestamp"]?.ToString();
                        if (ts == null || !DateTimeOffset.TryParse(ts, System.Globalization.CultureInfo.InvariantCulture,
                                System.Globalization.DateTimeStyles.AssumeUniversal, out var when)) continue;
                        if (when.LocalDateTime.Date != DateTime.Today) continue;
                        var msg = j["message"];
                        string mid = msg?["id"]?.ToString() ?? j["uuid"]?.ToString();
                        var t = msg?["usage"]?["output_tokens"];
                        if (mid == null || t == null) continue;
                        found.Add((mid, t.GetValue<long>()));
                    }
                    catch { }
                }
                lock (Lock)
                {
                    var day = Day;
                    var seen = new HashSet<string>(day.Messages);
                    // a mesma mensagem pode vir em várias linhas: conta a última (com o total final)
                    foreach (var g in found.GroupBy(f => f.id))
                    {
                        if (seen.Contains(g.Key)) continue;
                        day.OutputTokens += g.Last().tokens;
                        day.Messages.Add(g.Key);
                    }
                    day.Offsets[path] = start + end + 1;
                    SaveDay();
                }
            }
            catch { }
        }

        private static string FirstLine(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            s = s.Trim();
            int nl = s.IndexOfAny(new[] { '\r', '\n' });
            return nl > 0 ? s.Substring(0, nl).Trim() : s;
        }

        private static string Waiting(string msg, out bool idle)
        {
            var m = msg.ToLowerInvariant();
            // "waiting for your input" chega 1 min depois de ele terminar: é só ele parado, não um pedido
            idle = m.Contains("waiting for your input") || m.Contains("idle");
            if (m.Contains("permission"))
            {
                int i = msg.LastIndexOf(" use ", StringComparison.OrdinalIgnoreCase);
                return i >= 0 ? "Pedindo permissão para usar " + msg.Substring(i + 5).Trim().TrimEnd('.') : "Pedindo permissão";
            }
            if (m.Contains("waiting for your input") || m.Contains("idle")) return "Esperando a sua resposta";
            return string.IsNullOrWhiteSpace(msg) ? "Esperando você" : msg;
        }

        /// <summary>Texto curto e ícone para cada ferramenta.</summary>
        private static ClaudeAction Describe(string tool, JsonNode input, ClaudeSession s)
        {
            tool ??= "";
            string file = input?["file_path"]?.ToString() ?? input?["notebook_path"]?.ToString() ?? input?["path"]?.ToString();
            string name = string.IsNullOrEmpty(file) ? null : Path.GetFileName(file);
            switch (tool)
            {
                case "Edit":
                case "MultiEdit":
                case "Write":
                case "NotebookEdit":
                    if (name != null)
                    {
                        s.Files.Add(file);
                        if (!Day.Files.Contains(file, StringComparer.OrdinalIgnoreCase)) Day.Files.Add(file);
                    }
                    return new ClaudeAction { Icon = "edit", Text = (tool == "Write" ? "Escrevendo " : "Editando ") + (name ?? "arquivo") };
                case "Read":
                    return new ClaudeAction { Icon = "doc", Text = "Lendo " + (name ?? "arquivo") };
                case "Bash":
                case "PowerShell":
                {
                    string desc = input?["description"]?.ToString();
                    string cmd = input?["command"]?.ToString() ?? "";
                    return new ClaudeAction { Icon = "terminal", Text = !string.IsNullOrWhiteSpace(desc) ? desc : "Rodando " + FirstLine(cmd) };
                }
                case "Grep":
                case "Glob":
                    return new ClaudeAction { Icon = "search", Text = "Procurando " + (input?["pattern"]?.ToString() ?? "no código") };
                case "WebSearch":
                    return new ClaudeAction { Icon = "globe", Text = "Pesquisando: " + (input?["query"]?.ToString() ?? "na web") };
                case "WebFetch":
                    return new ClaudeAction { Icon = "globe", Text = "Lendo uma página da web" };
                case "Task":
                case "Agent":
                    return new ClaudeAction { Icon = "screens", Text = "Chamando um ajudante: " + (input?["description"]?.ToString() ?? "tarefa") };
                case "TodoWrite":
                    return new ClaudeAction { Icon = "check", Text = "Organizando as tarefas" };
            }
            if (tool.StartsWith("mcp__"))
            {
                var parts = tool.Split("__");
                string t = parts.Length > 2 ? parts[2] : tool;
                if (t.StartsWith("update_plan")) return new ClaudeAction { Icon = "check", Text = "Atualizando o plano" };
                return new ClaudeAction { Icon = "sliders", Text = "Usando " + t.Replace('_', ' ') };
            }
            return new ClaudeAction { Icon = "sliders", Text = "Usando " + tool };
        }
    }
}
