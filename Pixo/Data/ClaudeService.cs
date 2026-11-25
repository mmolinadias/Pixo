using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

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
        public int Tools;                     // ações neste pedido
        public readonly HashSet<string> Files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly List<ClaudeAction> Recent = new List<ClaudeAction>();

        public TimeSpan Elapsed => (Status == ClaudeStatus.Working || Status == ClaudeStatus.Waiting ? DateTime.Now : TurnEnd) - TurnStart;
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
                if (evt == "SessionEnd") { Sessions.Remove(id); return null; }
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
                        if (s.Status != ClaudeStatus.Working) s.Status = ClaudeStatus.Idle;
                        return null;

                    case "UserPromptSubmit":
                        s.Status = ClaudeStatus.Working;
                        s.Prompt = FirstLine(e["prompt"]?.ToString());
                        s.TurnStart = now;
                        s.Tools = 0;
                        s.Files.Clear();
                        s.Recent.Clear();
                        s.WaitingFor = null;
                        s.Current = new ClaudeAction { Icon = "claude", Text = "Pensando…", At = now };
                        return null;

                    case "PreToolUse":
                    {
                        bool wasIdle = s.Status != ClaudeStatus.Working;
                        s.Status = ClaudeStatus.Working;
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
                        s.Status = ClaudeStatus.Waiting;
                        s.WaitingFor = Waiting(msg);
                        return "evento:claude-espera";
                    }

                    case "Stop":
                    {
                        bool long_ = s.Status == ClaudeStatus.Working && (now - s.TurnStart).TotalSeconds >= 30;
                        s.Status = ClaudeStatus.Done;
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
                Sessions.Remove(id);
        }

        private static string FirstLine(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            s = s.Trim();
            int nl = s.IndexOfAny(new[] { '\r', '\n' });
            return nl > 0 ? s.Substring(0, nl).Trim() : s;
        }

        private static string Waiting(string msg)
        {
            var m = msg.ToLowerInvariant();
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
                    if (name != null) s.Files.Add(file);
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
