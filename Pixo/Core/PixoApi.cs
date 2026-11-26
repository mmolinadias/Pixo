using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

namespace Pixo.Core
{
    /// <summary>
    /// Pixo API: um endereço só deste PC (http://127.0.0.1:7480) para outros programas mandarem avisos à tela.
    /// Exemplos (curl já vem no Windows):
    ///   curl -X POST http://127.0.0.1:7480/evento/claude-espera     → a dupla avisa que o Claude Code te chamou
    ///   curl -X POST http://127.0.0.1:7480/evento/claude-pronto     → a dupla comemora: o Claude terminou
    ///   curl -X POST http://127.0.0.1:7480/mensagem -d "Pizza chegou!"
    ///   curl http://127.0.0.1:7480/tela/clima                        → mostra uma tela
    ///   curl http://127.0.0.1:7480/bichinhos/quente                → testa uma animação na tela (30 s)
    ///   curl http://127.0.0.1:7480/status
    ///   (hooks do Claude Code)  curl -X POST http://127.0.0.1:7480/claude --data-binary @-   → painel do Claude Code
    /// Só aceita conexões do próprio PC, e recusa pedidos feitos por páginas da web (cabeçalho Origin).
    /// </summary>
    public static class PixoApi
    {
        public static readonly string[] Events = { "claude-espera", "claude-pronto", "comemora", "susto", "carinho" };

        private static TcpListener _listener;
        private static Func<Config> _cfg;
        private static Action<string> _post;

        /// <summary>Porta em que está ouvindo (0 = desligada) e o último problema.</summary>
        public static volatile int Port;
        public static volatile string Error;

        public static void Start(Func<Config> cfg, Action<string> post)
        {
            _cfg = cfg;
            _post = post;
            Restart();
        }

        public static void Restart()
        {
            Stop();
            var c = _cfg();
            if (!c.ApiEnabled) return;
            try
            {
                var l = new TcpListener(IPAddress.Loopback, c.ApiPort);
                l.Start();
                _listener = l;
                Port = c.ApiPort;
                Error = null;
                new Thread(() => Accept(l)) { IsBackground = true, Name = "Pixo.Api" }.Start();
            }
            catch (SocketException)
            {
                Error = $"A porta {c.ApiPort} já está em uso por outro programa. Escolha outra.";
            }
        }

        public static void Stop()
        {
            Port = 0;
            try { _listener?.Stop(); } catch { }
            _listener = null;
        }

        private static void Accept(TcpListener l)
        {
            while (true)
            {
                TcpClient client;
                try { client = l.AcceptTcpClient(); }
                catch { return; } // parou
                ThreadPool.QueueUserWorkItem(_ => Handle(client));
            }
        }

        private static void Handle(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 3000;
                    client.SendTimeout = 3000;
                    var stream = client.GetStream();
                    var (method, target, headers, body) = Read(stream);
                    if (method == null) return;
                    var (status, json) = headers.ContainsKey("origin")
                        ? (403, Obj(false, "Pedidos de páginas da web não são aceitos."))
                        : Route(method, target, body);
                    Write(stream, status, json);
                }
                catch { /* conexão quebrada: ignora */ }
            }
        }

        private static (int, string) Route(string method, string target, string body)
        {
            string path = target, query = "";
            int q = target.IndexOf('?');
            if (q >= 0) { path = target.Substring(0, q); query = target.Substring(q + 1); }
            var parts = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Unescape).ToArray();

            if (parts.Length == 0 || parts[0] == "status")
            {
                var e = Engine.Instance;
                var o = new JsonObject
                {
                    ["ok"] = true,
                    ["tela"] = e?.CurrentScreenId,
                    ["conectada"] = e?.Connected ?? false,
                    ["bichinhos"] = e?.PetState,
                    ["eventos"] = new JsonArray(Events.Select(x => (JsonNode)x).ToArray()),
                };
                return (200, o.ToJsonString());
            }

            switch (parts[0])
            {
                case "evento" when parts.Length == 2:
                    if (!Events.Contains(parts[1])) return (404, Obj(false, "Evento desconhecido. Use: " + string.Join(", ", Events)));
                    _post("evento:" + parts[1]);
                    return (200, Obj(true));

                case "mensagem":
                    string text = Param(query, "texto") ?? TextFromBody(body);
                    if (string.IsNullOrWhiteSpace(text)) return (400, Obj(false, "Mande o texto: /mensagem?texto=... ou no corpo do pedido."));
                    text = text.Trim();
                    if (text.Length > 120) text = text.Substring(0, 120);
                    _post("msg:" + text);
                    return (200, Obj(true));

                case "claude":
                {
                    // eventos dos hooks do Claude Code (o JSON que o Claude Code manda no stdin, repassado pelo curl)
                    JsonNode evt;
                    try { evt = JsonNode.Parse(body ?? ""); }
                    catch { return (400, Obj(false, "Mande o JSON do hook no corpo do pedido.")); }
                    var action = Data.ClaudeService.Handle(evt);
                    if (action != null) _post(action);
                    _post("claude:atualizou");
                    return (200, Obj(true));
                }

                case "bichinhos":
                    // teste de animação na tela: /bichinhos/quente (30 s) ou /bichinhos/ao-vivo
                    if (parts.Length == 2 && (parts[1] == "ao-vivo" || Render.Pets.States.ContainsKey(parts[1])))
                    {
                        _post("bichinhos:" + parts[1]);
                        return (200, Obj(true));
                    }
                    return (404, Obj(false, "Animações: " + string.Join(", ", Render.Pets.States.Keys) + " (ou ao-vivo)"));

                case "tela" when parts.Length == 2:
                    _post(parts[1]);
                    return (200, Obj(true));
            }
            return (404, Obj(false, "Caminho desconhecido. Use /status, /evento/<nome>, /mensagem ou /tela/<id>."));
        }

        private static string TextFromBody(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            body = body.Trim();
            if (body.StartsWith("{"))
            {
                try { return JsonNode.Parse(body)?["texto"]?.ToString() ?? JsonNode.Parse(body)?["text"]?.ToString(); }
                catch { return null; }
            }
            if (body.StartsWith("texto=")) return Unescape(body.Substring(6));
            return body;
        }

        private static string Param(string query, string name)
        {
            foreach (var kv in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int i = kv.IndexOf('=');
                if (i > 0 && kv.Substring(0, i) == name) return Unescape(kv.Substring(i + 1));
            }
            return null;
        }

        private static string Unescape(string s) => Uri.UnescapeDataString(s.Replace('+', ' '));

        private static string Obj(bool ok, string message = null)
        {
            var o = new JsonObject { ["ok"] = ok };
            if (message != null) o["mensagem"] = message;
            return o.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        }

        // ---------------- HTTP mínimo ----------------

        private static (string method, string target, System.Collections.Generic.Dictionary<string, string> headers, string body) Read(Stream s)
        {
            var headers = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var buf = new byte[8192];
            int len = 0, end = -1;
            while (len < buf.Length)
            {
                int n = s.Read(buf, len, buf.Length - len);
                if (n <= 0) break;
                len += n;
                end = IndexOf(buf, len, "\r\n\r\n");
                if (end >= 0) break;
            }
            if (end < 0) return (null, null, headers, null);

            var lines = Encoding.ASCII.GetString(buf, 0, end).Split("\r\n");
            var first = lines[0].Split(' ');
            if (first.Length < 2) return (null, null, headers, null);
            foreach (var line in lines.Skip(1))
            {
                int c = line.IndexOf(':');
                if (c > 0) headers[line.Substring(0, c).Trim()] = line.Substring(c + 1).Trim();
            }

            int want = headers.TryGetValue("content-length", out var cl) && int.TryParse(cl, out var v) ? Math.Clamp(v, 0, 4 << 20) : 0; // até 4 MB (um Write grande manda o arquivo inteiro)
            var body = new MemoryStream();
            body.Write(buf, end + 4, Math.Min(len - end - 4, want));
            while (body.Length < want)
            {
                int n = s.Read(buf, 0, (int)Math.Min(buf.Length, want - body.Length));
                if (n <= 0) break;
                body.Write(buf, 0, n);
            }
            return (first[0].ToUpperInvariant(), first[1], headers, Encoding.UTF8.GetString(body.ToArray()));
        }

        private static int IndexOf(byte[] b, int len, string pattern)
        {
            for (int i = 0; i + pattern.Length <= len; i++)
            {
                int k = 0;
                while (k < pattern.Length && b[i + k] == pattern[k]) k++;
                if (k == pattern.Length) return i;
            }
            return -1;
        }

        private static void Write(Stream s, int status, string json)
        {
            var body = Encoding.UTF8.GetBytes(json + "\n");
            string reason = status switch { 200 => "OK", 400 => "Bad Request", 403 => "Forbidden", _ => "Not Found" };
            var head = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {status} {reason}\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
            s.Write(head, 0, head.Length);
            s.Write(body, 0, body.Length);
        }
    }
}
