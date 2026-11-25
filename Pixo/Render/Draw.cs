using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pixo.Render
{
    /// <summary>Paleta do display (valores exatos em RGB565, do sistema de design Pixo).</summary>
    public static class Pal
    {
        public static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

        private static readonly Dictionary<string, SolidColorBrush> Cache = new Dictionary<string, SolidColorBrush>();
        public static SolidColorBrush B(string hex, double opacity = 1)
        {
            var key = hex + "|" + opacity.ToString(CultureInfo.InvariantCulture);
            lock (Cache)
            {
                if (!Cache.TryGetValue(key, out var b))
                {
                    b = new SolidColorBrush(C(hex)) { Opacity = opacity };
                    b.Freeze();
                    Cache[key] = b;
                }
                return b;
            }
        }

        public static SolidColorBrush Bg => B("#080c10");
        public static SolidColorBrush S1 => B("#181c21");
        public static SolidColorBrush S2 => B("#212a31");
        public static SolidColorBrush Line => B("#293442");
        public static SolidColorBrush Ink => B("#f7f7f7");
        public static SolidColorBrush Ink2 => B("#9ca6b5");
        public static SolidColorBrush Ink3 => B("#6b7584");
        public static SolidColorBrush Accent => B("#42cfef");
        public static SolidColorBrush Ok => B("#39db84");
        public static SolidColorBrush Warn => B("#ffc739");
        public static SolidColorBrush Hot => B("#ff595a");
        public static SolidColorBrush Blue => B("#528eff");
        public static SolidColorBrush Orange => B("#ffa639");
        public static SolidColorBrush Pink => B("#ff699c");
        public static SolidColorBrush Gamer => B("#a569ff");
        public static SolidColorBrush GamerLine => B("#4a3484");
        public static SolidColorBrush GamerSurface => B("#211831");

        public static SolidColorBrush Temp(double? t) => t == null ? Ink3 : t < 60 ? Ok : t <= 80 ? Warn : Hot;
        public static SolidColorBrush Fps(double? f) => f == null ? Ink3 : f >= 60 ? Ok : f >= 30 ? Warn : Hot;

        /// <summary>Cor estável para um nome (ex.: app de notificação sem ícone).</summary>
        public static SolidColorBrush ForName(string name)
        {
            var known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["WhatsApp"] = "#25d366", ["Discord"] = "#5865f2", ["Outlook"] = "#1a86d9", ["Steam"] = "#66c0f4",
                ["Spotify"] = "#1db954", ["Telegram"] = "#2aa3e0", ["Teams"] = "#6264a7", ["Gmail"] = "#ea4335",
                ["Chrome"] = "#4c8bf5", ["Edge"] = "#33a5e0", ["Firefox"] = "#ff7139", ["YouTube"] = "#ff3d3d"
            };
            foreach (var kv in known)
                if ((name ?? "").IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0) return B(kv.Value);
            var pool = new[] { "#42cfef", "#39db84", "#ffc739", "#a569ff", "#ffa639", "#ff699c", "#528eff" };
            int h = 0;
            foreach (var ch in name ?? "") h = (h * 31 + ch) & 0x7fffffff;
            return B(pool[h % pool.Length]);
        }
    }

    public enum Align { Left, Center, Right }

    /// <summary>Kit de desenho das telas: texto, cartões, barras, ícones, cabeçalho.</summary>
    public static class D
    {
        public static readonly CultureInfo PtBr = new CultureInfo("pt-BR");
        private static readonly FontFamily Family = new FontFamily("Segoe UI Variable Display, Segoe UI");
        private static readonly Dictionary<int, Typeface> Faces = new Dictionary<int, Typeface>();

        public static Typeface Face(int weight)
        {
            lock (Faces)
            {
                if (!Faces.TryGetValue(weight, out var f))
                {
                    f = new Typeface(Family, FontStyles.Normal, FontWeight.FromOpenTypeWeight(weight), FontStretches.Normal);
                    Faces[weight] = f;
                }
                return f;
            }
        }

        public static FormattedText Fmt(string s, double size, int weight, Brush brush)
        {
            return new FormattedText(s ?? "", PtBr, FlowDirection.LeftToRight, Face(weight), size, brush, 1.0);
        }

        /// <summary>Texto numa caixa (x, y, w, h) como no design: alinhado e cortado com "…".</summary>
        public static double Text(DrawingContext dc, string s, double x, double y, double w, double h,
            double size, int weight, Brush brush, Align align = Align.Left, int maxLines = 1)
        {
            var ft = Fmt(s, size, weight, brush);
            ft.MaxTextWidth = Math.Max(1, w);
            ft.MaxLineCount = Math.Max(1, maxLines);
            ft.Trimming = TextTrimming.CharacterEllipsis;
            ft.TextAlignment = align == Align.Center ? TextAlignment.Center : align == Align.Right ? TextAlignment.Right : TextAlignment.Left;
            double lineH = maxLines > 1 ? h / maxLines : h;
            if (maxLines > 1) ft.LineHeight = lineH;
            // centraliza a linha dentro da altura da caixa (como o line-height do CSS)
            double top = maxLines > 1 ? y : y + (h - ft.Height) / 2;
            dc.DrawText(ft, new Point(x, top));
            return ft.WidthIncludingTrailingWhitespace;
        }

        /// <summary>Número grande + unidade menor ("54°C", "48,2 MB/s").</summary>
        public static void Num(DrawingContext dc, string num, string unit, double x, double y, double w, double h,
            double size, int weight, Brush brush, Align align = Align.Left, double unitSize = 0, Brush unitBrush = null)
        {
            unitSize = unitSize > 0 ? unitSize : Math.Round(size * 0.42);
            var a = Fmt(num, size, weight, brush);
            var b = Fmt(unit ?? "", unitSize, 700, unitBrush ?? brush);
            double gap = string.IsNullOrEmpty(unit) ? 0 : 2;
            double total = a.WidthIncludingTrailingWhitespace + gap + b.WidthIncludingTrailingWhitespace;
            double x0 = align == Align.Center ? x + (w - total) / 2 : align == Align.Right ? x + w - total : x;
            double top = y + (h - a.Height) / 2;
            dc.DrawText(a, new Point(x0, top));
            if (!string.IsNullOrEmpty(unit))
            {
                // alinha a base da unidade com a base do número
                double baseA = top + a.Baseline;
                dc.DrawText(b, new Point(x0 + a.WidthIncludingTrailingWhitespace + gap, baseA - b.Baseline));
            }
        }

        public static double Measure(string s, double size, int weight) => Fmt(s, size, weight, Brushes.White).WidthIncludingTrailingWhitespace;

        public static void Card(DrawingContext dc, double x, double y, double w, double h, Brush fill = null, Pen border = null, double radius = 12)
        {
            dc.DrawRoundedRectangle(fill ?? Pal.S1, border, new Rect(x, y, w, h), radius, radius);
        }

        public static void Bar(DrawingContext dc, double x, double y, double w, double h, double? pct, Brush color, Brush track = null)
        {
            double r = h / 2;
            dc.DrawRoundedRectangle(track ?? Pal.Line, null, new Rect(x, y, w, h), r, r);
            if (pct == null || pct <= 0) return;
            double fw = Math.Max(h, w * Math.Min(100, pct.Value) / 100);
            dc.DrawRoundedRectangle(color, null, new Rect(x, y, fw, h), r, r);
        }

        /// <summary>Barra listrada para download sem tamanho total.</summary>
        public static void StripedBar(DrawingContext dc, double x, double y, double w, double h, Brush color, double phase)
        {
            var rect = new Rect(x, y, w, h);
            dc.DrawRoundedRectangle(Pal.Line, null, rect, h / 2, h / 2);
            dc.PushClip(new RectangleGeometry(rect, h / 2, h / 2));
            for (double sx = x - 12 + (phase % 12); sx < x + w; sx += 12)
            {
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(new Point(sx, y + h), true, true);
                    c.LineTo(new Point(sx + 6, y + h), true, false);
                    c.LineTo(new Point(sx + 6 + h, y), true, false);
                    c.LineTo(new Point(sx + h, y), true, false);
                }
                g.Freeze();
                dc.DrawGeometry(color, null, g);
            }
            dc.Pop();
        }

        /// <summary>Barra em "bloquinhos" retrô (usada na tela dos bichinhos).</summary>
        public static void SegBar(DrawingContext dc, double x, double y, double w, double h, int n, double pct, Brush color)
        {
            double bw = (w - (n - 1) * 2) / n;
            int on = (int)Math.Floor(Math.Clamp(pct, 0, 100) / 100 * n + 0.5);
            for (int i = 0; i < n; i++)
                dc.DrawRoundedRectangle(i < on ? color : Pal.Line, null,
                    new Rect(Math.Floor(x + i * (bw + 2) + 0.5), y, Math.Floor(bw + 0.5), h), 2, 2);
        }

        // imagem da dupla reaproveitada a cada quadro (uma por thread: a da tela e a da janela)
        [ThreadStatic] private static WriteableBitmap _petsBmp;
        [ThreadStatic] private static uint[] _petsBuf;

        /// <summary>
        /// Fred & Dengo (pixel art 6×, 360×216) com o canto em (x, y). "from"/"mix": vindo de outro
        /// estado (mix 0 → 1), para a troca não dar tranco.
        /// </summary>
        public static void PetsDuo(DrawingContext dc, string state, double pos, double x, double y, bool cushion = true,
                                   string from = null, double fromPos = 0, double mix = 1)
        {
            // 1:1, mas sem suavização por garantia (pixel art tem que ficar nítida)
            var group = new DrawingGroup();
            RenderOptions.SetBitmapScalingMode(group, BitmapScalingMode.NearestNeighbor);
            group.Children.Add(new ImageDrawing(PetsBitmap(state, pos, cushion, from, fromPos, mix), new Rect(x, y, Pets.ScreenW, Pets.ScreenH)));
            dc.DrawDrawing(group);
        }

        /// <summary>A dupla numa imagem 360×216 que é reaproveitada (não guarde a referência entre quadros).</summary>
        public static WriteableBitmap PetsBitmap(string state, double pos, bool cushion = true, string from = null, double fromPos = 0, double mix = 1)
        {
            _petsBuf ??= new uint[Pets.ScreenW * Pets.ScreenH];
            _petsBmp ??= new WriteableBitmap(Pets.ScreenW, Pets.ScreenH, 96, 96, PixelFormats.Bgra32, null);
            Pets.RenderDuoScreen(state, pos, from, fromPos, mix, _petsBuf, cushion);
            _petsBmp.WritePixels(new Int32Rect(0, 0, Pets.ScreenW, Pets.ScreenH), _petsBuf, Pets.ScreenW * 4, 0);
            return _petsBmp;
        }

        public static void Dot(DrawingContext dc, double x, double y, double size, Brush color)
        {
            dc.DrawEllipse(color, null, new Point(x + size / 2, y + size / 2), size / 2, size / 2);
        }

        /// <summary>Chip em forma de pílula com texto (altura h). Devolve a largura.</summary>
        public static double Chip(DrawingContext dc, string text, double x, double y, double h, Brush bg, Brush fg,
            double size = 12, int weight = 700, double pad = 8, string icon = null)
        {
            double tw = Measure(text, size, weight);
            double iw = icon != null ? 14 + 6 : 0;
            double w = pad * 2 + iw + tw;
            dc.DrawRoundedRectangle(bg, null, new Rect(x, y, w, h), h / 2, h / 2);
            if (icon != null) Icon(dc, icon, x + pad, y + (h - 14) / 2, 14, fg, 2.6);
            Text(dc, text, x + pad + iw, y, tw + 2, h, size, weight, fg);
            return w;
        }

        // ---------------- ícones (traço, grade 24×24) ----------------

        public static readonly Dictionary<string, string> IconPaths = new Dictionary<string, string>
        {
            ["thermo"] = "M10 13.5V5a2 2 0 0 1 4 0v8.5a4 4 0 1 1-4 0Z M12 10v6",
            ["music"] = "M9 18V6l10-2v12 M9 18a3 3 0 1 1-6 0a3 3 0 1 1 6 0Z M19 16a3 3 0 1 1-6 0a3 3 0 1 1 6 0Z",
            ["bell"] = "M6 16v-5a6 6 0 0 1 12 0v5l2 2H4Z M10 21h4",
            ["download"] = "M12 4v11 M7 10l5 5 5-5 M5 20h14",
            ["upload"] = "M12 20V9 M7 14l5-5 5 5 M5 4h14",
            ["arrowDown"] = "M12 4v16 M6 14l6 6 6-6",
            ["arrowUp"] = "M12 20V4 M6 10l6-6 6 6",
            ["gamepad"] = "M7 8h10a4 4 0 0 1 4 4v3a3 3 0 0 1-5.4 1.8L14 15h-4l-1.6 1.8A3 3 0 0 1 3 15v-3a4 4 0 0 1 4-4Z M8 10.5v3 M6.5 12h3 M15.5 11h.01 M17.5 13h.01",
            ["image"] = "M4 5h16v14H4Z M4 16l5-5 4 4 2-2 5 5 M15.5 9h.01",
            ["clock"] = "M12 3a9 9 0 1 1 0 18a9 9 0 1 1 0-18Z M12 7v5l3 2",
            ["pet"] = "M5 13a7 6.5 0 0 1 14 0v2a4 4 0 0 1-4 4H9a4 4 0 0 1-4-4Z M12 6.5V3.5 M9.5 13h.01 M14.5 13h.01",
            ["cpu"] = "M7 7h10v10H7Z M10 3v4 M14 3v4 M10 17v4 M14 17v4 M3 10h4 M3 14h4 M17 10h4 M17 14h4",
            ["gpu"] = "M3 6h18v11H3Z M8 11.5a2.5 2.5 0 1 0 5 0a2.5 2.5 0 1 0-5 0 M16 9.5h2 M16 13.5h2 M6 17v3",
            ["board"] = "M4 4h16v16H4Z M8 8h5v5H8Z M16 8h.01 M16 12h.01 M8 16.5h8",
            ["ssd"] = "M4 6h16v12H4Z M8 10h6 M8 14h3 M16.5 14h.01",
            ["ram"] = "M3 8h18v8H3Z M7 11v2 M11 11v2 M15 11v2 M6 16v3 M18 16v3",
            ["play"] = "M8 5.5v13l10.5-6.5Z",
            ["pause"] = "M8.5 5.5v13 M15.5 5.5v13",
            ["next"] = "M6 6l8 6-8 6Z M18 6v12",
            ["prev"] = "M18 6l-8 6 8 6Z M6 6v12",
            ["check"] = "M5 12.5l4.5 4.5L19 7.5",
            ["alert"] = "M12 4l9 16H3Z M12 10v4 M12 17h.01",
            ["info"] = "M12 3a9 9 0 1 1 0 18a9 9 0 1 1 0-18Z M12 11v5 M12 8h.01",
            ["folder"] = "M3 6h6l2 2h10v11H3Z",
            ["mic"] = "M9 5.5a3 3 0 0 1 6 0V11a3 3 0 0 1-6 0Z M5.5 11a6.5 6.5 0 0 0 13 0 M12 17.5V21",
            ["sun"] = "M12 8.5a3.5 3.5 0 1 1 0 7a3.5 3.5 0 1 1 0-7Z M12 2.5v2 M12 19.5v2 M2.5 12h2 M19.5 12h2 M5.3 5.3l1.4 1.4 M17.3 17.3l1.4 1.4 M5.3 18.7l1.4-1.4 M17.3 6.7l1.4-1.4",
            ["sliders"] = "M4 7h10 M18 7h2 M4 17h4 M12 17h8 M16 5v4 M10 15v4",
            ["home"] = "M4 11l8-7 8 7v9h-5v-6H9v6H4Z",
            ["screens"] = "M3 5h13v9H3Z M8 18h3 M19 8h2v10H9v-1",
            ["monitor"] = "M3 6h18v11H3Z M8 21h8 M12 17v4",
            ["wave"] = "M4 10v4 M8 7v10 M12 4v16 M16 7v10 M20 10v4",
            ["power"] = "M12 3v8 M6.3 6.3a8 8 0 1 0 11.4 0",
            ["usb"] = "M9 3h6v5H9Z M7 8h10v5a5 5 0 0 1-10 0Z M12 18v3",
            ["chevR"] = "M9 6l6 6-6 6",
            ["chevL"] = "M15 6l-6 6 6 6",
            ["chevD"] = "M6 9l6 6 6-6",
            ["plus"] = "M12 5v14 M5 12h14",
            ["close"] = "M6 6l12 12 M18 6L6 18",
            ["minus"] = "M5 12h14",
            ["trash"] = "M4 7h16 M9 7V4h6v3 M6 7l1 13h10l1-13",
            ["heart"] = "M12 20s-7-4.4-7-10a4 4 0 0 1 7-2.6A4 4 0 0 1 19 10c0 5.6-7 10-7 10Z",
            ["globe"] = "M12 3a9 9 0 1 1 0 18a9 9 0 1 1 0-18Z M3 12h18 M12 3a14 14 0 0 1 0 18 M12 3a14 14 0 0 0 0 18",
            ["magnet"] = "M6 4h4v8a2 2 0 0 0 4 0V4h4v8a6 6 0 0 1-12 0Z M6 8h4 M14 8h4",
            ["refresh"] = "M20 11a8 8 0 1 0-2.3 5.7 M20 4v7h-7",
            ["flip"] = "M4 12a8 8 0 0 1 13.7-5.7L20 8.5 M20 4v4.5h-4.5 M20 12a8 8 0 0 1-13.7 5.7L4 15.5 M4 20v-4.5h4.5",
            ["grip"] = "M9 6h.01 M15 6h.01 M9 12h.01 M15 12h.01 M9 18h.01 M15 18h.01",
            ["lang"] = "M4 5h9 M8.5 3v2 M6 5a8 8 0 0 0 6 8 M11 5a8 8 0 0 1-6 8 M13 21l4-9 4 9 M14.5 18h5",
        };

        private static readonly Dictionary<string, Geometry> GeoCache = new Dictionary<string, Geometry>();

        public static Geometry IconGeometry(string name)
        {
            lock (GeoCache)
            {
                if (!GeoCache.TryGetValue(name, out var g))
                {
                    g = Geometry.Parse(IconPaths.TryGetValue(name, out var d) ? d : IconPaths["info"]);
                    g.Freeze();
                    GeoCache[name] = g;
                }
                return g;
            }
        }

        public static void Icon(DrawingContext dc, string name, double x, double y, double size, Brush color, double stroke = 2)
        {
            double s = size / 24.0;
            dc.PushTransform(new TranslateTransform(x, y));
            dc.PushTransform(new ScaleTransform(s, s));
            var pen = new Pen(color, stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            dc.DrawGeometry(name == "play" ? color : null, pen, IconGeometry(name));
            dc.Pop();
            dc.Pop();
        }

        // ---------------- cabeçalho e estado vazio ----------------

        public class HeaderInfo
        {
            public string Icon;
            public string Title;
            public Brush IconColor;
            public Brush Underline;
            public bool GameTitle;      // título em caixa normal (nome do jogo)
            public int PageIndex = -1, PageCount;
            public bool Paused;
            public bool ShowTime = true;
        }

        public static void Header(DrawingContext dc, HeaderInfo o)
        {
            var iconColor = o.IconColor ?? Pal.Accent;
            if (o.Icon != null) Icon(dc, o.Icon, 12, 11, 18, iconColor, 2.2);
            if (o.GameTitle)
                Text(dc, o.Title, 38, 11, 244, 18, 15, 700, Pal.Ink);
            else
                Text(dc, (o.Title ?? "").ToUpper(PtBr), 38, 11, 240, 18, 14, 700, Pal.Ink);

            if (o.PageCount > 1 && o.PageIndex >= 0)
            {
                int n = o.PageCount;
                double w = n * 6 + (n - 1) * 4, x0 = 360 - w - (o.Paused ? 22 : 0);
                for (int i = 0; i < n; i++)
                    Dot(dc, x0 + i * 10, 18, 6, i == o.PageIndex ? iconColor : Pal.Line);
            }
            if (o.Paused) Icon(dc, "pause", 362, 12, 16, Pal.Ink2, 2.4);
            if (o.ShowTime) Text(dc, DateTime.Now.ToString("HH:mm"), 388, 9, 80, 22, 16, 600, Pal.Ink, Align.Right);
            if (o.Underline != null) dc.DrawRectangle(o.Underline, null, new Rect(12, 39, 456, 2));
        }

        public static void Empty(DrawingContext dc, string icon, Brush color, string title, string sub, double y = 96, double dx = 0)
        {
            Icon(dc, icon, 216 + dx, y, 48, color, 2);
            Text(dc, title, 40 + dx, y + 60, 400, 24, 20, 700, Pal.Ink, Align.Center);
            Text(dc, sub, 60 + dx, y + 90, 360, 36, 13, 600, Pal.Ink2, Align.Center, 2);
        }

        // ---------------- formatação (pt-BR) ----------------

        public static string N1(double v) => v.ToString("0.0", PtBr);
        public static string N0(double v) => v.ToString("0", PtBr);

        /// <summary>(número, unidade) de uma velocidade em bytes/s.</summary>
        public static (string num, string unit) Speed(double? bps)
        {
            if (bps == null) return ("--", "");
            double v = bps.Value;
            if (v >= 1024 * 1024 * 1024) return (N1(v / 1024 / 1024 / 1024), "GB/s");
            if (v >= 1024 * 1024) return (N1(v / 1024 / 1024), "MB/s");
            if (v >= 1024) return (N0(v / 1024), "KB/s");
            return (N0(v), "B/s");
        }

        public static string SpeedText(double? bps)
        {
            var (n, u) = Speed(bps);
            return u == "" ? n : n + " " + u;
        }

        public static string Size(double bytes)
        {
            if (bytes >= 1024d * 1024 * 1024) return N1(bytes / 1024 / 1024 / 1024) + " GB";
            if (bytes >= 1024d * 1024) return N1(bytes / 1024 / 1024) + " MB";
            return N0(bytes / 1024) + " KB";
        }

        public static string Ago(DateTimeOffset when)
        {
            var s = (DateTimeOffset.Now - when).TotalSeconds;
            if (s < 60) return "agora";
            if (s < 3600) return "há " + (int)(s / 60) + " min";
            if (s < 86400) return "há " + (int)(s / 3600) + " h";
            if (s < 172800) return "ontem";
            return "há " + (int)(s / 86400) + " dias";
        }

        public static string Clock(TimeSpan t) => (int)t.TotalMinutes + ":" + t.Seconds.ToString("00");
    }
}
