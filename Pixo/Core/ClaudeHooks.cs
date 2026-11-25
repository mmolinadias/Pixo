using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pixo.Core
{
    /// <summary>
    /// Liga o Claude Code ao Pixo com "hooks" (comandos que o Claude Code roda sozinho em certos momentos),
    /// no arquivo de configurações do usuário (%USERPROFILE%\.claude\settings.json):
    ///  - Notification (o Claude precisa de você) → /evento/claude-espera
    ///  - Stop (o Claude terminou de responder)   → /evento/claude-pronto
    /// Só mexe nessas duas entradas; o resto do arquivo fica como estava (e uma cópia é guardada antes).
    /// </summary>
    public static class ClaudeHooks
    {
        public static string SettingsPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

        private static readonly (string hook, string evt)[] Map = { ("Notification", "claude-espera"), ("Stop", "claude-pronto") };

        private static string Command(int port, string evt) => $"curl -s -m 2 -X POST http://127.0.0.1:{port}/evento/{evt}";

        /// <summary>O trecho de configuração, para quem prefere colar à mão.</summary>
        public static string Snippet(int port)
        {
            var hooks = new JsonObject();
            foreach (var (hook, evt) in Map)
                hooks[hook] = new JsonArray(new JsonObject
                {
                    ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = Command(port, evt) })
                });
            return new JsonObject { ["hooks"] = hooks }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }

        public static bool IsInstalled(int port)
        {
            try
            {
                if (!File.Exists(SettingsPath)) return false;
                var text = File.ReadAllText(SettingsPath);
                return Map.All(m => text.Contains($"127.0.0.1:{port}/evento/{m.evt}"));
            }
            catch { return false; }
        }

        /// <summary>Acrescenta os hooks do Pixo. Devolve null se deu certo, ou o problema.</summary>
        public static string Install(int port) => Edit(root =>
        {
            var hooks = root["hooks"] as JsonObject ?? new JsonObject();
            root["hooks"] = hooks;
            foreach (var (hook, evt) in Map)
            {
                var list = hooks[hook] as JsonArray ?? new JsonArray();
                hooks[hook] = list;
                RemoveOurs(list);
                list.Add(new JsonObject
                {
                    ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = Command(port, evt) })
                });
            }
        });

        /// <summary>Tira só os hooks do Pixo.</summary>
        public static string Uninstall() => Edit(root =>
        {
            if (root["hooks"] is not JsonObject hooks) return;
            foreach (var (hook, _) in Map)
            {
                if (hooks[hook] is not JsonArray list) continue;
                RemoveOurs(list);
                if (list.Count == 0) hooks.Remove(hook);
            }
            if (hooks.Count == 0) root.Remove("hooks");
        });

        /// <summary>Remove as entradas cujos comandos são todos do Pixo (os outros hooks ficam).</summary>
        private static void RemoveOurs(JsonArray list)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var inner = list[i]?["hooks"] as JsonArray;
                if (inner == null || inner.Count == 0) continue;
                if (inner.All(h => h?["command"]?.ToString()?.Contains("/evento/claude-") == true &&
                                   h["command"].ToString().Contains("127.0.0.1:")))
                    list.RemoveAt(i);
            }
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
                    try { root = string.IsNullOrWhiteSpace(text) ? new JsonObject() : JsonNode.Parse(text) as JsonObject; }
                    catch { return "O arquivo de configurações do Claude Code tem um erro de formato; não mexi nele."; }
                    if (root == null) return "O arquivo de configurações do Claude Code não está no formato esperado; não mexi nele.";
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
