using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pixo.Core
{
    /// <summary>
    /// Liga o Claude Code ao Pixo com "hooks" (comandos que o Claude Code roda sozinho em certos momentos),
    /// no arquivo de configurações do usuário (%USERPROFILE%\.claude\settings.json). Cada evento (início de
    /// sessão, pedido, cada ferramenta, aviso, fim) é repassado como está para a Pixo API (/claude), que monta
    /// o painel do Claude Code. Só mexe nas entradas do Pixo; o resto do arquivo fica como estava (e uma cópia
    /// é guardada antes).
    /// </summary>
    public static class ClaudeHooks
    {
        public static string SettingsPath => PathOverride ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

        /// <summary>Só para testes (PixoShot): usa outro arquivo no lugar do settings.json de verdade.</summary>
        public static string PathOverride;

        /// <summary>Eventos repassados e se precisam de "matcher" (os de ferramenta).</summary>
        private static readonly (string hook, bool tools)[] Events =
        {
            ("SessionStart", false), ("UserPromptSubmit", false), ("PreToolUse", true), ("PostToolUse", true),
            ("Notification", false), ("Stop", false), ("SessionEnd", false),
        };

        private static string Command(int port) =>
            $"curl -s -m 2 -X POST http://127.0.0.1:{port}/claude -H \"Content-Type: application/json\" --data-binary @-";

        private static JsonObject Entry(int port, bool tools)
        {
            var e = new JsonObject();
            if (tools) e["matcher"] = "*";
            e["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = Command(port) });
            return e;
        }

        /// <summary>O trecho de configuração, para quem prefere colar à mão.</summary>
        public static string Snippet(int port)
        {
            var hooks = new JsonObject();
            foreach (var (hook, tools) in Events) hooks[hook] = new JsonArray(Entry(port, tools));
            return new JsonObject { ["hooks"] = hooks }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }

        private static string Read()
        {
            try { return File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath) : ""; }
            catch { return ""; }
        }

        /// <summary>Os hooks atuais do Pixo (painel completo) estão ligados nesta porta?</summary>
        public static bool IsInstalled(int port)
        {
            var root = Parse(Read());
            if (root?["hooks"] is not JsonObject hooks) return false;
            string cmd = $"127.0.0.1:{port}/claude ";
            return Events.All(ev => hooks[ev.hook] is JsonArray list &&
                list.Any(x => (x?["hooks"] as JsonArray)?.Any(h => h?["command"]?.ToString()?.Contains(cmd) == true) == true));
        }

        /// <summary>Tem a versão antiga (só "te chamou" e "terminou")? Aí vale trocar pela completa.</summary>
        public static bool HasOldVersion() => Read().Contains("/evento/claude-");

        /// <summary>Liga (ou atualiza) os hooks do Pixo. Devolve null se deu certo, ou o problema.</summary>
        public static string Install(int port) => Edit(root =>
        {
            var hooks = root["hooks"] as JsonObject ?? new JsonObject();
            root["hooks"] = hooks;
            foreach (var key in hooks.Select(kv => kv.Key).ToList())
                if (hooks[key] is JsonArray l) RemoveOurs(l);
            foreach (var (hook, tools) in Events)
            {
                var list = hooks[hook] as JsonArray ?? new JsonArray();
                hooks[hook] = list;
                list.Add(Entry(port, tools));
            }
            foreach (var key in hooks.Select(kv => kv.Key).ToList())
                if (hooks[key] is JsonArray l && l.Count == 0) hooks.Remove(key);
        });

        /// <summary>Tira só os hooks do Pixo.</summary>
        public static string Uninstall() => Edit(root =>
        {
            if (root["hooks"] is not JsonObject hooks) return;
            foreach (var key in hooks.Select(kv => kv.Key).ToList())
            {
                if (hooks[key] is not JsonArray list) continue;
                RemoveOurs(list);
                if (list.Count == 0) hooks.Remove(key);
            }
            if (hooks.Count == 0) root.Remove("hooks");
        });

        private static bool IsOurs(string cmd) =>
            cmd != null && cmd.Contains("127.0.0.1:") && (cmd.Contains("/evento/claude-") || cmd.Contains("/claude "));

        /// <summary>Remove as entradas cujos comandos são todos do Pixo (os outros hooks ficam).</summary>
        private static void RemoveOurs(JsonArray list)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var inner = list[i]?["hooks"] as JsonArray;
                if (inner == null || inner.Count == 0) continue;
                if (inner.All(h => IsOurs(h?["command"]?.ToString()))) list.RemoveAt(i);
            }
        }

        private static JsonObject Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            try { return JsonNode.Parse(text) as JsonObject; } catch { return null; }
        }

        private static string Edit(Action<JsonObject> change)
        {
            try
            {
                var path = SettingsPath;
                JsonObject root;
                if (File.Exists(path))
                {
                    var text = File.ReadAllText(path);
                    root = string.IsNullOrWhiteSpace(text) ? new JsonObject() : Parse(text);
                    if (root == null) return "O arquivo de configurações do Claude Code tem um erro de formato; não mexi nele.";
                    File.Copy(path, path + ".pixo-backup", true);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    root = new JsonObject();
                }
                change(root);
                File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                }));
                return null;
            }
            catch (Exception ex) { return "Não deu para salvar: " + ex.Message; }
        }
    }
}
