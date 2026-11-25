using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Pixo.Core;

namespace Pixo.Data
{
    public class WeatherDay
    {
        public DateTime Date;
        public int Code;
        public double? Max, Min, RainChance;
    }

    public class WeatherInfo
    {
        public string Place;               // "Campinas, São Paulo"
        public double? Temp, Feels, Humidity, Wind;
        public int Code;
        public bool IsDay = true;
        public WeatherDay[] Days = Array.Empty<WeatherDay>();
        public double? UvNow, UvMax;
        public int? Aqi;                   // índice de qualidade do ar (escala dos EUA, 0–500)
        public DateTime? Sunrise, Sunset;
        public DateTime Updated;
    }

    /// <summary>
    /// Clima pelo Open-Meteo (grátis, sem chave). A cidade vem da internet (localização aproximada pelo IP,
    /// ipwho.is) ou do nome digitado na aba Clima (geocoding do Open-Meteo). Atualiza a cada 30 minutos.
    /// </summary>
    public static class WeatherService
    {
        private static Timer _timer;
        private static Func<Config> _cfg;
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        private static int _busy;
        private static DateTime _next = DateTime.MinValue;
        private static string _placeFor;                 // para qual "cidade escolhida" o lugar abaixo vale
        private static (double lat, double lon, string name)? _place;

        public static volatile WeatherInfo Current;
        /// <summary>Último problema (sem internet, cidade não encontrada...); null quando deu certo.</summary>
        public static volatile string Error;

        static WeatherService()
        {
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("Pixo/1.0");
        }

        public static void Start(Func<Config> cfg)
        {
            _cfg = cfg;
            _timer = new Timer(_ => Tick(), null, 3000, 60_000);
        }

        public static void Stop() => _timer?.Dispose();

        /// <summary>Busca de novo agora (ex.: a cidade mudou).</summary>
        public static void RefreshSoon()
        {
            _next = DateTime.MinValue;
            ThreadPool.QueueUserWorkItem(_ => Tick());
        }

        private static void Tick()
        {
            var city = _cfg?.Invoke()?.WeatherCity ?? "";
            if (city != _placeFor) _next = DateTime.MinValue;
            if (DateTime.Now < _next) return;
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            try
            {
                Refresh(city).GetAwaiter().GetResult();
                Error = null;
                _next = DateTime.Now.AddMinutes(30);
            }
            catch (Exception ex)
            {
                Error = ex is HttpRequestException || ex is TaskCanceledException
                    ? "Sem internet (ou o serviço do clima fora do ar)."
                    : ex.Message;
                _next = DateTime.Now.AddMinutes(Current == null ? 2 : 10);
            }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }

        /// <summary>Busca o clima agora (usado pelo timer e pelos testes).</summary>
        public static async Task Refresh(string city)
        {
            if (_place == null || _placeFor != city)
            {
                _place = string.IsNullOrWhiteSpace(city) ? await PlaceFromIp() : await PlaceFromName(city);
                _placeFor = city;
            }
            var (lat, lon, name) = _place.Value;
            string ll = $"latitude={lat.ToString(CultureInfo.InvariantCulture)}&longitude={lon.ToString(CultureInfo.InvariantCulture)}";

            var f = await Get("https://api.open-meteo.com/v1/forecast?" + ll +
                "&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,is_day,wind_speed_10m" +
                "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max,uv_index_max,sunrise,sunset" +
                "&timezone=auto&forecast_days=4");

            var w = new WeatherInfo { Place = name, Updated = DateTime.Now };
            var cur = f?["current"];
            w.Temp = Num(cur?["temperature_2m"]);
            w.Feels = Num(cur?["apparent_temperature"]);
            w.Humidity = Num(cur?["relative_humidity_2m"]);
            w.Wind = Num(cur?["wind_speed_10m"]);
            w.Code = (int)(Num(cur?["weather_code"]) ?? 0);
            w.IsDay = (Num(cur?["is_day"]) ?? 1) == 1;

            var daily = f?["daily"];
            var times = daily?["time"] as JsonArray;
            if (times != null)
            {
                int n = Math.Min(times.Count, 4);
                w.Days = new WeatherDay[n];
                for (int i = 0; i < n; i++)
                    w.Days[i] = new WeatherDay
                    {
                        Date = DateTime.Parse(times[i]?.ToString() ?? "", CultureInfo.InvariantCulture),
                        Code = (int)(Num(daily["weather_code"]?[i]) ?? 0),
                        Max = Num(daily["temperature_2m_max"]?[i]),
                        Min = Num(daily["temperature_2m_min"]?[i]),
                        RainChance = Num(daily["precipitation_probability_max"]?[i]),
                    };
                w.UvMax = Num(daily["uv_index_max"]?[0]);
                w.Sunrise = Time(daily["sunrise"]?[0]);
                w.Sunset = Time(daily["sunset"]?[0]);
            }

            // qualidade do ar e UV de agora (outro endereço do Open-Meteo; se falhar, o resto continua valendo)
            try
            {
                var a = await Get("https://air-quality-api.open-meteo.com/v1/air-quality?" + ll + "&current=us_aqi,uv_index&timezone=auto");
                w.Aqi = Num(a?["current"]?["us_aqi"]) is double q ? (int)Math.Round(q) : null;
                w.UvNow = Num(a?["current"]?["uv_index"]);
            }
            catch { }

            Current = w;
        }

        /// <summary>Cidade aproximada pelo IP (ipwho.is, sem chave).</summary>
        private static async Task<(double, double, string)> PlaceFromIp()
        {
            var j = await Get("https://ipwho.is/");
            if (j?["success"]?.GetValue<bool>() != true || Num(j["latitude"]) == null)
                throw new Exception("Não deu para descobrir a cidade pela internet. Escolha uma na aba Clima.");
            string name = Join(j["city"]?.ToString(), j["region"]?.ToString());
            return (Num(j["latitude"]).Value, Num(j["longitude"]).Value, name);
        }

        /// <summary>Procura uma cidade pelo nome (geocoding do Open-Meteo).</summary>
        public static async Task<(double lat, double lon, string name)> PlaceFromName(string city)
        {
            var j = await Get("https://geocoding-api.open-meteo.com/v1/search?count=1&language=pt&format=json&name=" + Uri.EscapeDataString(city));
            var r = (j?["results"] as JsonArray)?[0];
            if (r == null || Num(r["latitude"]) == null) throw new Exception($"Não achei a cidade \"{city}\".");
            return (Num(r["latitude"]).Value, Num(r["longitude"]).Value, Join(r["name"]?.ToString(), r["admin1"]?.ToString()));
        }

        private static string Join(string a, string b) =>
            string.IsNullOrWhiteSpace(b) || b == a ? a ?? "" : $"{a}, {b}";

        private static async Task<JsonNode> Get(string url)
        {
            using var res = await Http.GetAsync(url);
            res.EnsureSuccessStatusCode();
            return JsonNode.Parse(await res.Content.ReadAsStringAsync());
        }

        private static double? Num(JsonNode n)
        {
            if (n is not JsonValue v) return null;
            if (v.TryGetValue<double>(out var d)) return d;
            if (v.TryGetValue<string>(out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            return null;
        }

        private static DateTime? Time(JsonNode n) =>
            n != null && DateTime.TryParse(n.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;

        // ---------------- textos e ícones ----------------

        /// <summary>Descrição em português do código de tempo (WMO) do Open-Meteo.</summary>
        public static string Describe(int code) => code switch
        {
            0 => "Céu limpo",
            1 => "Quase limpo",
            2 => "Parcialmente nublado",
            3 => "Nublado",
            45 or 48 => "Neblina",
            51 or 53 or 55 => "Garoa",
            56 or 57 => "Garoa gelada",
            61 => "Chuva fraca",
            63 => "Chuva",
            65 => "Chuva forte",
            66 or 67 => "Chuva gelada",
            71 or 73 or 75 or 77 or 85 or 86 => "Neve",
            80 => "Pancadas fracas",
            81 => "Pancadas de chuva",
            82 => "Pancadas fortes",
            95 => "Trovoadas",
            96 or 99 => "Trovoadas e granizo",
            _ => "—",
        };

        /// <summary>Nome do ícone (Draw.IconPaths) para o código de tempo.</summary>
        public static string Icon(int code, bool day = true) => code switch
        {
            0 => day ? "sun" : "moon",
            1 or 2 => day ? "cloudSun" : "cloudMoon",
            3 => "cloud",
            45 or 48 => "fog",
            51 or 53 or 55 or 56 or 57 => "drizzle",
            61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => "rain",
            71 or 73 or 75 or 77 or 85 or 86 => "snow",
            95 or 96 or 99 => "storm",
            _ => "cloud",
        };

        public static string UvLevel(double uv) => uv < 3 ? "baixo" : uv < 6 ? "moderado" : uv < 8 ? "alto" : uv < 11 ? "muito alto" : "extremo";

        /// <summary>Como está o ar (índice dos EUA). "atenção" = ruim para quem tem asma, crianças e idosos.</summary>
        public static string AqiLevel(int aqi) => aqi <= 50 ? "bom" : aqi <= 100 ? "moderado" : aqi <= 150 ? "atenção" : aqi <= 200 ? "ruim" : aqi <= 300 ? "muito ruim" : "perigoso";
    }
}
