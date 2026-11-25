using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Pixo.Core;

namespace Pixo.Data
{
    public class AcInfo
    {
        public string Name;
        public double? Room;     // temperatura do quarto, medida pelo próprio ar
        public double? Target;   // temperatura escolhida no ar
        public bool On;
        public string Mode;      // "Frio", "Ventilar"...
        public DateTime Updated;
    }

    /// <summary>
    /// Ar-condicionado LG pela API oficial ThinQ Connect (a mesma da integração LG ThinQ do Home Assistant e do
    /// ControleTV). O Pixo só LÊ o estado (temperatura do quarto), a cada 5 minutos. O token pessoal da LG fica
    /// criptografado para este usuário do Windows (DPAPI) e nunca é gravado em texto puro nem em log.
    /// </summary>
    public static class AcService
    {
        // Chave pública que todo cliente ThinQ Connect envia (do SDK da própria LG); quem dá acesso é o token.
        private const string ApiKey = "v6GFvkweNo7DK7yD3ylIZ9w52aKBU0eJ7wLXkSR3";
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        private static readonly HashSet<string> Americas = new HashSet<string>
        {
            "US", "CA", "MX", "BR", "AR", "CL", "CO", "PE", "EC", "VE", "UY", "PY", "BO", "PA", "CR", "GT", "HN", "SV",
            "NI", "DO", "PR", "JM", "TT", "BS", "BB", "HT", "AW", "CW", "BZ", "GY", "SR",
        };
        private static readonly HashSet<string> AsiaPacific = new HashSet<string>
        {
            "AU", "BD", "CN", "HK", "ID", "IN", "JP", "KH", "KR", "LA", "LK", "MM", "MY", "NP", "NZ", "PH", "SG", "TH", "TW", "VN",
        };

        private static Timer _timer;
        private static Func<Config> _cfg;
        private static int _busy;
        private static DateTime _next = DateTime.MinValue;

        public static volatile AcInfo Current;
        public static volatile string Error;

        public static bool Configured(Config c) => c != null && c.AcTokenProtected.Length > 0 && c.AcDeviceId.Length > 0;

        public static void Start(Func<Config> cfg)
        {
            _cfg = cfg;
            _timer = new Timer(_ => Tick(), null, 4000, 30_000);
        }

        public static void Stop() => _timer?.Dispose();

        public static void RefreshSoon()
        {
            _next = DateTime.MinValue;
            ThreadPool.QueueUserWorkItem(_ => Tick());
        }

        private static void Tick()
        {
            var c = _cfg?.Invoke();
            if (!Configured(c)) { Current = null; Error = null; return; }
            if (DateTime.Now < _next) return;
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            try
            {
                Current = ReadNow(c).GetAwaiter().GetResult();
                Error = null;
                _next = DateTime.Now.AddMinutes(5);
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                _next = DateTime.Now.AddMinutes(5);
            }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }

        /// <summary>Lê o estado do ar agora (temperatura do quarto, ligado, modo).</summary>
        public static async Task<AcInfo> ReadNow(Config c)
        {
            var s = await Call(c, Unprotect(c.AcTokenProtected), $"devices/{Uri.EscapeDataString(c.AcDeviceId)}/state");
            var info = Parse(s);
            info.Name = string.IsNullOrWhiteSpace(c.AcName) ? "Ar-condicionado" : c.AcName;
            return info;
        }

        // ---------------- configuração ----------------

        public static string Protect(string token) =>
            Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser));

        private static string Unprotect(string data)
        {
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(data), null, DataProtectionScope.CurrentUser)); }
            catch { throw new Exception("Não deu para abrir o token guardado. Conecte o ar de novo na aba Clima."); }
        }

        /// <summary>Lista os ar-condicionados da conta LG deste token: (id, nome, modelo).</summary>
        public static async Task<List<(string Id, string Name, string Model)>> ListAcs(Config c, string token)
        {
            var list = new List<(string, string, string)>();
            if (await Call(c, token, "devices") is not JsonArray arr) return list;
            foreach (var d in arr)
            {
                var info = d?["deviceInfo"];
                if (info?["deviceType"]?.ToString() != "DEVICE_AIR_CONDITIONER") continue;
                string name = info["alias"]?.ToString() is string a && a.Length > 0 ? a : "Ar-condicionado";
                list.Add((d["deviceId"]?.ToString(), name, info["modelName"]?.ToString() ?? ""));
            }
            return list;
        }

        /// <summary>Guarda o token (criptografado) e o ar escolhido.</summary>
        public static void Save(Config c, string token, string deviceId, string name)
        {
            c.AcTokenProtected = Protect(token);
            c.AcDeviceId = deviceId;
            c.AcName = name;
            c.Save();
            RefreshSoon();
        }

        public static void Forget(Config c)
        {
            c.AcTokenProtected = "";
            c.AcDeviceId = "";
            c.AcName = "";
            c.Save();
            Current = null;
            Error = null;
        }

        /// <summary>
        /// Usa o ar já configurado no ControleTV (arquivo dados\ar.json): o token dele é aberto com a chave deste
        /// usuário do Windows e guardado de novo, criptografado, nas configurações do Pixo.
        /// </summary>
        public static string ImportControleTv(Config c, string path)
        {
            var j = JsonNode.Parse(File.ReadAllText(path));
            string prot = j?["TokenProtected"]?.ToString() ?? "", dev = j?["DeviceId"]?.ToString() ?? "";
            if (prot.Length == 0 || dev.Length == 0) return "Esse arquivo não tem um ar-condicionado configurado.";
            string token;
            try { token = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(prot), null, DataProtectionScope.CurrentUser)); }
            catch { return "Não deu para abrir o token desse arquivo (ele foi criado em outro usuário do Windows?)."; }
            c.AcCountry = j?["Country"]?.ToString() ?? "BR";
            Save(c, token, dev, j?["Name"]?.ToString() ?? "Ar-condicionado");
            return null;
        }

        /// <summary>Onde o ControleTV costuma guardar o ar (o primeiro que existir), ou null.</summary>
        public static string FindControleTv()
        {
            var candidates = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ControleTV", "ar.json"),
            };
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
            {
                candidates.Add(Path.Combine(drive.RootDirectory.FullName, "android-adb", "ControleTV", "app", "dados", "ar.json"));
                candidates.Add(Path.Combine(drive.RootDirectory.FullName, "ControleTV", "dados", "ar.json"));
            }
            return candidates.FirstOrDefault(File.Exists);
        }

        // ---------------- API ----------------

        private static async Task<JsonNode> Call(Config c, string token, string path)
        {
            string country = (c.AcCountry ?? "BR").ToUpperInvariant();
            var region = Americas.Contains(country) ? "aic" : AsiaPacific.Contains(country) ? "kic" : "eic";
            using var req = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri($"https://api-{region}.lgthinq.com/"), path));
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
            req.Headers.TryAddWithoutValidation("x-country", country);
            req.Headers.TryAddWithoutValidation("x-message-id",
                Convert.ToBase64String(Guid.NewGuid().ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_'));
            req.Headers.TryAddWithoutValidation("x-client-id", c.AcClientId);
            req.Headers.TryAddWithoutValidation("x-api-key", ApiKey);
            req.Headers.TryAddWithoutValidation("x-service-phase", "OP");

            HttpResponseMessage res;
            try { res = await Http.SendAsync(req); }
            catch (TaskCanceledException) { throw new Exception("A LG não respondeu a tempo."); }
            catch (HttpRequestException) { throw new Exception("Sem internet (ou o servidor da LG fora do ar)."); }
            using (res)
            {
                var text = await res.Content.ReadAsStringAsync();
                JsonNode json = null;
                try { json = string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text); } catch { }
                if (res.IsSuccessStatusCode) return json?["response"];
                var code = json?["error"]?["code"]?.ToString() ?? ((int)res.StatusCode).ToString(CultureInfo.InvariantCulture);
                throw new Exception(code switch
                {
                    "1101" or "1102" or "1103" or "401" => "O token da LG não vale mais. Conecte o ar de novo na aba Clima.",
                    "1306" => "Muitos pedidos para a LG em pouco tempo. O Pixo tenta de novo depois.",
                    "1205" or "1212" or "1213" => "O ar não aparece mais na sua conta LG.",
                    "1222" or "2205" or "2301" => "O ar está sem conexão com a LG (sem Wi-Fi ou sem energia).",
                    _ => "A LG recusou o pedido (" + code + ").",
                });
            }
        }

        private static AcInfo Parse(JsonNode s)
        {
            var st = new AcInfo { Updated = DateTime.Now };
            if (s == null) return st;
            st.On = Find(s, "operation")?["airConOperationMode"]?.ToString()?.EndsWith("_ON", StringComparison.Ordinal) == true;
            st.Mode = Find(s, "airConJobMode")?["currentJobMode"]?.ToString() switch
            {
                "COOL" => "Frio", "HEAT" => "Quente", "AIR_DRY" => "Desumidificar", "FAN" => "Ventilar",
                "AUTO" => "Automático", "AIR_CLEAN" => "Purificar", "ENERGY_SAVING" => "Econômico", var m => m,
            };
            var units = Find(s, "temperatureInUnits");
            if (units is JsonArray arr && arr.FirstOrDefault(u => u?["unit"]?.ToString() == "C") is JsonNode cel)
            {
                st.Room = Num(cel["currentTemperature"]);
                st.Target = Num(cel["targetTemperature"]);
            }
            else if (units is JsonObject o)
            {
                st.Room = Num(o["currentTemperatureC"]) ?? Num(o["currentTemperature"]);
                st.Target = Num(o["targetTemperatureC"]) ?? Num(o["targetTemperature"]);
            }
            if (st.Room == null || st.Target == null)
            {
                var t = Find(s, "temperature");
                if (t != null && (t["unit"] == null || t["unit"].ToString() == "C"))
                {
                    st.Room ??= Num(t["currentTemperature"]);
                    st.Target ??= Num(t["targetTemperature"]);
                }
            }
            return st;
        }

        /// <summary>O primeiro objeto com esse nome em qualquer lugar da árvore.</summary>
        private static JsonNode Find(JsonNode node, string key)
        {
            switch (node)
            {
                case JsonObject o:
                    if (o.TryGetPropertyValue(key, out var v) && v != null) return v;
                    foreach (var kv in o)
                        if (Find(kv.Value, key) is JsonNode f) return f;
                    break;
                case JsonArray a:
                    foreach (var item in a)
                        if (Find(item, key) is JsonNode f) return f;
                    break;
            }
            return null;
        }

        private static double? Num(JsonNode n)
        {
            if (n is not JsonValue v) return null;
            if (v.TryGetValue<double>(out var d)) return d;
            if (v.TryGetValue<string>(out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            return null;
        }
    }
}
