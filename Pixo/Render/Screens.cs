using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pixo.Core;
using Pixo.Data;

namespace Pixo.Render
{
    /// <summary>Informações que toda tela recebe ao ser desenhada.</summary>
    public class FrameCtx
    {
        public int PageIndex = -1, PageCount;
        public bool Paused;
        public Config Cfg;
    }

    /// <summary>Uma tela do display (480×320).</summary>
    public abstract class Screen
    {
        public abstract string Id { get; }
        public string Name => ScreenInfo.Name(Id);
        /// <summary>Tem algo para mostrar agora? Se não, a rotação pula.</summary>
        public virtual bool HasContent => true;
        /// <summary>Daqui a quanto tempo precisa redesenhar.</summary>
        public virtual TimeSpan Interval => TimeSpan.FromSeconds(1);
        public virtual void OnShow() { }
        public abstract void Draw(DrawingContext dc, FrameCtx ctx);

        protected D.HeaderInfo Head(FrameCtx ctx, string icon) => new D.HeaderInfo
        {
            Icon = icon, Title = Name, PageIndex = ctx.PageIndex, PageCount = ctx.PageCount, Paused = ctx.Paused
        };
    }

    // =====================================================================
    // a) TEMPERATURAS
    // =====================================================================
    public class TemperaturesScreen : Screen
    {
        public override string Id => "temperaturas";
        public override TimeSpan Interval => TimeSpan.FromSeconds(2);

        public override void Draw(DrawingContext dc, FrameCtx ctx)
        {
            D.Header(dc, Head(ctx, "thermo"));
            var hw = HardwareService.Current;
            if (hw.Error != null && hw.Cpu == null && hw.Gpu == null)
            {
                D.Empty(dc, "alert", Pal.Warn, "Sensores indisponíveis",
                    "Abra o Pixo no PC e confira a aba Início. Pode faltar permissão de administrador.", 78);
                return;
            }

            var items = new (string label, string icon, double? t)[]
            {
                ("CPU", "cpu", hw.Cpu?.Temp), ("GPU", "gpu", hw.Gpu?.Temp),
                ("Placa-mãe", "board", hw.Board?.Temp), ("SSD", "ssd", hw.Ssd?.Temp)
            };
            var pos = new[] { (12, 44), (244, 44), (12, 144), (244, 144) };
            for (int i = 0; i < 4; i++)
            {
                double x = pos[i].Item1, y = pos[i].Item2;
                var t = items[i].t;
                var col = Pal.Temp(t);
                D.Card(dc, x, y, 224, 92);
                D.Icon(dc, items[i].icon, x + 12, y + 12, 16, Pal.Ink2, 2);
                D.Text(dc, items[i].label, x + 34, y + 12, 120, 16, 13, 600, Pal.Ink2);
                D.Dot(dc, x + 204, y + 17, 8, col);
                D.Num(dc, t == null ? "--" : D.N0(t.Value), "°C", x + 12, y + 36, 110, 48, 44, 700, col, Align.Left, 18);
                D.Bar(dc, x + 124, y + 60, 88, 6, t ?? 0, col);
            }

            double cpu = hw.CpuLoad ?? 0, ram = hw.RamLoad ?? 0;
            double cx = 12, cy = 244;
            D.Card(dc, cx, cy, 456, 64);
            D.Text(dc, "Uso CPU", cx + 12, cy + 10, 60, 16, 13, 600, Pal.Ink2);
            D.Text(dc, D.N0(cpu) + "%", cx + 120, cy + 6, 96, 24, 20, 700, Pal.Ink, Align.Right);
            D.Bar(dc, cx + 12, cy + 40, 204, 8, cpu, Pal.Accent);
            D.Text(dc, "Uso RAM", cx + 240, cy + 10, 60, 16, 13, 600, Pal.Ink2);
            string ramText = hw.RamUsedGb != null ? D.N1(hw.RamUsedGb.Value) + " / " + D.N0(hw.RamTotalGb.Value) + " GB" : "";
            double pw = D.Measure(D.N0(ram) + "%", 20, 700);
            D.Text(dc, D.N0(ram) + "%", cx + 300, cy + 6, 144, 24, 20, 700, Pal.Ink, Align.Right);
            D.Text(dc, ramText, cx + 300, cy + 10, 144 - pw - 8, 16, 12, 600, Pal.Ink2, Align.Right);
            D.Bar(dc, cx + 240, cy + 40, 204, 8, ram, Pal.Blue);
        }
    }

    // =====================================================================
    // b) MÚSICA
    // =====================================================================
    public class MusicScreen : Screen
    {
        public override string Id => "musica";
        public override bool HasContent => MediaService.Current != null;

        public override void Draw(DrawingContext dc, FrameCtx ctx)
        {
            var m = MediaService.Current;
            if (m == null)
            {
                D.Header(dc, Head(ctx, "music"));
                D.Empty(dc, "music", Pal.Ink3, "Nada tocando", "Dê play em qualquer app e a música aparece aqui.");
                return;
            }

            // fundo: capa desfocada + véu escuro
            if (m.CoverTiny != null)
            {
                dc.DrawImage(m.CoverTiny, new Rect(-40, -40, 560, 400));
                dc.DrawRectangle(Pal.B("#080c10", 0.70), null, new Rect(0, 0, 480, 320));
            }
            D.Header(dc, Head(ctx, "music"));

            // capa
            var coverRect = new Rect(16, 52, 168, 168);
            dc.PushClip(new RectangleGeometry(coverRect, 12, 12));
            if (m.Cover != null) dc.DrawImage(m.Cover, coverRect);
            else
            {
                dc.DrawRectangle(Pal.S2, null, coverRect);
                D.Icon(dc, "music", 16 + 60, 52 + 60, 48, Pal.Ink3, 2);
            }
            dc.Pop();

            D.Text(dc, m.Title, 200, 56, 264, 56, 24, 700, Pal.Ink, Align.Left, 2);
            D.Text(dc, m.Artist, 200, 118, 264, 20, 16, 600, Pal.Ink);
            D.Text(dc, m.Album, 200, 142, 264, 16, 13, 600, Pal.Ink2);

            // chip Tocando / Pausado
            double chipW = D.Chip(dc, m.Playing ? "Tocando" : "Pausado", 200, 184, 28, Pal.Ink, Pal.Bg, 13, 700, 12, m.Playing ? "play" : "pause");

            // app de origem (slot com a cor da marca + inicial)
            double ax = Math.Max(300, 200 + chipW + 12);
            var appColor = Pal.ForName(m.App);
            dc.DrawEllipse(appColor, null, new Point(ax + 8, 198), 8, 8);
            D.Text(dc, m.App.Substring(0, 1).ToUpper(), ax, 190, 16, 16, 11, 800, Pal.Bg, Align.Center);
            D.Text(dc, m.App, ax + 22, 190, 464 - ax - 22, 16, 12, 600, Pal.Ink2);

            // progresso
            var pos = m.PositionNow;
            double pct = m.Duration.TotalSeconds > 0 ? 100 * pos.TotalSeconds / m.Duration.TotalSeconds : 0;
            D.Bar(dc, 16, 244, 448, 6, pct, Pal.Ink, Pal.B("#f7f7f7", 0.22));
            D.Text(dc, D.Clock(pos), 16, 258, 80, 16, 13, 600, Pal.Ink2);
            D.Text(dc, m.Duration.TotalSeconds > 0 ? D.Clock(m.Duration) : "--:--", 384, 258, 80, 16, 13, 600, Pal.Ink2, Align.Right);
        }
    }

    // =====================================================================
    // c) NOTIFICAÇÕES
    // =====================================================================
    public class NotificationsScreen : Screen
    {
        public override string Id => "notificacoes";
        public override TimeSpan Interval => TimeSpan.FromSeconds(3);
        public override bool HasContent => NotificationService.Items.Length > 0;

        public override void Draw(DrawingContext dc, FrameCtx ctx)
        {
            D.Header(dc, Head(ctx, "bell"));
            var list = NotificationService.Items;
            if (list.Length == 0)
            {
                if (NotificationService.Error != null)
                {
                    D.Empty(dc, "alert", Pal.Warn, "Sem acesso às notificações", NotificationService.Error, 78);
                    return;
                }
                dc.DrawEllipse(Pal.Ok, null, new Point(240, 124), 32, 32);
                D.Icon(dc, "check", 222, 106, 36, Pal.Bg, 3);
                D.Text(dc, "Tudo em dia", 40, 168, 400, 24, 20, 700, Pal.Ink, Align.Center);
                D.Text(dc, "Nenhuma notificação nova.", 60, 198, 360, 16, 13, 600, Pal.Ink2, Align.Center);
                return;
            }

            D.Card(dc, 12, 44, 456, 264);
            double ox = 12, oy = 44;
            int count = Math.Min(4, list.Length);
            for (int i = 0; i < count; i++)
            {
                var n = list[i];
                double y = oy + i * 66;
                var color = Pal.ForName(n.App);
                bool nova = (DateTimeOffset.Now - n.When).TotalMinutes < 10;
                if (nova) D.Dot(dc, ox + 4, y + 30, 6, Pal.Accent);

                // ícone oficial do app (do Windows) ou a inicial colorida
                var avatar = new Rect(ox + 14, y + 15, 36, 36);
                if (n.Logo != null)
                {
                    dc.DrawRoundedRectangle(Pal.S2, null, avatar, 18, 18);
                    dc.PushClip(new RectangleGeometry(avatar, 18, 18));
                    dc.DrawImage(n.Logo, new Rect(avatar.X + 4, avatar.Y + 4, 28, 28));
                    dc.Pop();
                }
                else
                {
                    dc.DrawRoundedRectangle(color, null, avatar, 18, 18);
                    D.Text(dc, (n.App ?? "?").Substring(0, 1).ToUpper(), avatar.X, avatar.Y, 36, 36, 16, 800, Pal.Bg, Align.Center);
                }

                D.Text(dc, n.App, ox + 60, y + 8, 280, 16, 12, 600, color);
                D.Text(dc, D.Ago(n.When), ox + 344, y + 8, 100, 16, 12, 600, Pal.Ink2, Align.Right);
                D.Text(dc, string.IsNullOrEmpty(n.Title) ? n.App : n.Title, ox + 60, y + 24, 384, 20, 15, 600, Pal.Ink);
                D.Text(dc, n.Text, ox + 60, y + 45, 384, 16, 13, 600, Pal.Ink2);
                if (i < count - 1) dc.DrawRectangle(Pal.Line, null, new Rect(ox + 60, y + 65, 384, 1));
            }
        }
    }

    // =====================================================================
    // d) DOWNLOADS
    // =====================================================================
    public class DownloadsScreen : Screen
    {
        public override string Id => "downloads";
        public override bool HasContent => DownloadService.Items.Length > 0;
        private double _phase;

        public override void Draw(DrawingContext dc, FrameCtx ctx)
        {
            D.Header(dc, Head(ctx, "download"));
            _phase += 4;

            // cartão da rede
            double nx = 12, ny = 44;
            D.Card(dc, nx, ny, 456, 68);
            D.Text(dc, "REDE", nx + 14, ny + 10, 80, 16, 12, 600, Pal.Ink2);
            D.Icon(dc, "arrowDown", nx + 12, ny + 34, 22, Pal.Accent, 2.6);
            var (dn, du) = D.Speed(NetworkService.DownBps);
            D.Num(dc, dn, du, nx + 38, ny + 26, 160, 40, 36, 700, Pal.Ink, Align.Left, 15, Pal.Ink2);
            D.Icon(dc, "arrowUp", nx + 196 + 8, ny + 40, 14, Pal.Ink2, 2.6);
            D.Text(dc, D.SpeedText(NetworkService.UpBps), nx + 212 + 8, ny + 39, 80, 16, 13, 600, Pal.Ink2);
            DrawSpark(dc, nx + 298, ny + 10, 146, 48, NetworkService.Spark);

            var list = DownloadService.Items;
            if (list.Length == 0)
            {
                D.Card(dc, 12, 120, 456, 188);
                D.Empty(dc, "check", Pal.Ink3, "Nenhum download agora", "Steam, navegador e qBittorrent aparecem aqui.", 120 + 28);
                return;
            }

            for (int i = 0; i < Math.Min(3, list.Length); i++)
            {
                var it = list[i];
                double x = 12, y = 120 + i * 64;
                var (icon, color) = it.Source switch
                {
                    "Steam" => ("gamepad", Pal.Blue),
                    "qBittorrent" => ("magnet", Pal.Orange),
                    _ => ("globe", Pal.Accent)
                };
                D.Card(dc, x, y, 456, 60);
                dc.DrawRoundedRectangle(Pal.S2, null, new Rect(x + 12, y + 14, 32, 32), 8, 8);
                D.Icon(dc, icon, x + 19, y + 21, 18, color, 2.2);
                D.Text(dc, it.Name, x + 54, y + 8, 284, 18, 14, 700, Pal.Ink);
                D.Text(dc, D.SpeedText(it.Speed), x + 344, y + 9, 100, 16, 13, 600, Pal.Ink, Align.Right);

                string info, eta = "";
                if (it.Pct == null)
                {
                    D.StripedBar(dc, x + 54, y + 30, 390, 6, color, _phase);
                    info = it.Source + " · " + D.Size(it.Done) + " baixados";
                }
                else
                {
                    D.Bar(dc, x + 54, y + 30, 390, 6, it.Pct, color);
                    info = it.Source + " · " + D.N0(it.Pct.Value) + "% · " + D.Size(it.Done) + " de " + D.Size(it.Total.Value);
                    if (it.Speed > 0)
                    {
                        double rest = (it.Total.Value - it.Done) / it.Speed.Value;
                        eta = rest < 60 ? "falta menos de 1 min" : rest < 3600 ? "faltam " + (int)Math.Ceiling(rest / 60) + " min" : "faltam " + D.N1(rest / 3600) + " h";
                    }
                }
                D.Text(dc, info, x + 54, y + 40, 280, 16, 12, 600, Pal.Ink2);
                D.Text(dc, eta, x + 334, y + 40, 110, 16, 12, 600, Pal.Ink2, Align.Right);
            }
        }

        private static void DrawSpark(DrawingContext dc, double x, double y, double w, double h, double[] data)
        {
            if (data == null || data.Length < 2) return;
            double max = Math.Max(1, data.Max());
            var pts = data.Select((v, i) => new Point(x + i / (double)(data.Length - 1) * w, y + (46 - v / max * 40) * h / 48)).ToList();
            var area = new StreamGeometry();
            using (var c = area.Open())
            {
                c.BeginFigure(pts[0], true, true);
                foreach (var p in pts.Skip(1)) c.LineTo(p, false, true);
                c.LineTo(new Point(x + w, y + h), false, false);
                c.LineTo(new Point(x, y + h), false, false);
            }
            dc.DrawGeometry(Pal.B("#103442"), null, area);
            var line = new StreamGeometry();
            using (var c = line.Open())
            {
                c.BeginFigure(pts[0], false, false);
                foreach (var p in pts.Skip(1)) c.LineTo(p, true, true);
            }
            dc.DrawGeometry(null, new Pen(Pal.Accent, 2) { LineJoin = PenLineJoin.Round }, line);
        }
    }

    // =====================================================================
    // e) MODO GAMER
    // =====================================================================
    public class GamerScreen : Screen
    {
        public override string Id => "gamer";

        public override void Draw(DrawingContext dc, FrameCtx ctx)
        {
            var game = GameService.Current;
            var hw = HardwareService.Current;
            D.Header(dc, new D.HeaderInfo
            {
                Icon = "gamepad", Title = game?.Name ?? "Nenhum jogo aberto", IconColor = Pal.Gamer,
                Underline = Pal.Gamer, GameTitle = true
            });
            D.Chip(dc, "MODO GAMER", 290, 10, 20, Pal.Gamer, Pal.Bg, 12, 700, 8);

            var gcardBorder = new Pen(Pal.GamerLine, 1);
            double? fps = game?.Fps;
            var fc = Pal.Fps(fps);

            // painel do FPS
            double px = 12, py = 48;
            D.Card(dc, px, py, 216, 260, Pal.GamerSurface, gcardBorder);
            D.Text(dc, "FPS", px + 16, py + 14, 60, 16, 13, 700, Pal.Gamer);
            D.Text(dc, fps == null ? "--" : D.N0(fps.Value), px, py + 52, 216, 96, 96, 800, fc, Align.Center);
            string sub = fps == null ? "Abra o RivaTuner para ver o FPS"
                : "mín. " + (game.MinFps != null ? D.N0(game.MinFps.Value) : "--") + " · " + (game.FrameMs != null ? D.N1(game.FrameMs.Value) : "--") + " ms";
            D.Text(dc, sub, px, py + 156, 216, 16, 13, 600, Pal.Ink2, Align.Center);

            // régua 0–165 com as faixas
            double rx = px + 16, ry = py + 214, rw = 184;
            dc.PushClip(new RectangleGeometry(new Rect(rx, ry, rw, 8), 4, 4));
            dc.DrawRectangle(Pal.Hot, null, new Rect(rx, ry, rw * 0.182, 8));
            dc.DrawRectangle(Pal.Warn, null, new Rect(rx + rw * 0.182, ry, rw * 0.182, 8));
            dc.DrawRectangle(Pal.Ok, null, new Rect(rx + rw * 0.364, ry, rw * 0.636, 8));
            dc.Pop();
            if (fps != null)
            {
                double mk = Math.Min(fps.Value, 165) / 165 * rw;
                dc.DrawRoundedRectangle(Pal.Ink, null, new Rect(rx + mk - 2, ry - 6, 4, 20), 2, 2);
            }
            D.Text(dc, "30", rx + 33.5 - 8, ry + 16, 20, 16, 12, 600, Pal.Ink2);
            D.Text(dc, "60", rx + 67 - 8, ry + 16, 20, 16, 12, 600, Pal.Ink2);
            D.Text(dc, "165", rx + 144, ry + 16, 40, 16, 12, 600, Pal.Ink2, Align.Right);

            // GPU e CPU
            HwCard(dc, 48, "GPU", "gpu", hw.Gpu?.Temp, hw.Gpu?.Load, gcardBorder);
            HwCard(dc, 140, "CPU", "cpu", hw.Cpu?.Temp, hw.CpuLoad, gcardBorder);

            // VRAM e RAM
            double vx = 236, vy = 232;
            D.Card(dc, vx, vy, 232, 76, Pal.GamerSurface, gcardBorder);
            D.Text(dc, "VRAM", vx + 12, vy + 10, 60, 16, 12, 600, Pal.Ink2);
            var g = hw.Gpu;
            string vram = g?.VramUsedGb != null ? D.N1(g.VramUsedGb.Value) + (g.VramTotalGb != null ? " / " + D.N0(g.VramTotalGb.Value) : "") + " GB" : "--";
            double? vp = g?.VramUsedGb != null && g.VramTotalGb > 0 ? 100 * g.VramUsedGb / g.VramTotalGb : null;
            D.Text(dc, vram, vx + 100, vy + 10, 120, 16, 12, 600, Pal.Ink, Align.Right);
            D.Bar(dc, vx + 12, vy + 28, 208, 6, vp, Pal.Gamer);
            D.Text(dc, "RAM", vx + 12, vy + 42, 60, 16, 12, 600, Pal.Ink2);
            string ram = hw.RamUsedGb != null ? D.N1(hw.RamUsedGb.Value) + " / " + D.N0(hw.RamTotalGb.Value) + " GB" : "--";
            D.Text(dc, ram, vx + 100, vy + 42, 120, 16, 12, 600, Pal.Ink, Align.Right);
            D.Bar(dc, vx + 12, vy + 60, 208, 6, hw.RamLoad, Pal.Blue);
        }

        private static void HwCard(DrawingContext dc, double y, string name, string icon, double? t, double? use, Pen border)
        {
            double x = 236;
            var col = Pal.Temp(t);
            D.Card(dc, x, y, 232, 84, Pal.GamerSurface, border);
            D.Icon(dc, icon, x + 12, y + 12, 16, Pal.Ink2, 2);
            D.Text(dc, name, x + 34, y + 12, 60, 16, 13, 600, Pal.Ink2);
            D.Num(dc, t == null ? "--" : D.N0(t.Value), "°C", x + 110, y + 6, 110, 32, 28, 700, col, Align.Right, 16);
            D.Text(dc, "Uso", x + 12, y + 50, 60, 16, 12, 600, Pal.Ink2);
            D.Text(dc, use == null ? "--" : D.N0(use.Value) + "%", x + 160, y + 50, 60, 16, 13, 600, Pal.Ink, Align.Right);
            D.Bar(dc, x + 12, y + 68, 208, 6, use, Pal.Gamer);
        }
    }

    // =====================================================================
    // f) IMAGEM
    // =====================================================================
    public class ImageScreen : Screen
    {
        public override string Id => "imagem";
        public override TimeSpan Interval => TimeSpan.FromSeconds(5);
        private static readonly string[] Exts = { ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".gif" };
        private int _index = -1;
        private string _path;
        private BitmapSource _img;
        private readonly Func<Config> _cfg;

        public ImageScreen(Func<Config> cfg) { _cfg = cfg; }

        public override void OnShow()
        {
            _img = null; _path = null;
            try
            {
                var folder = _cfg().ImagesFolder;
                Directory.CreateDirectory(folder);
                var files = Directory.GetFiles(folder).Where(f => Exts.Contains(Path.GetExtension(f).ToLowerInvariant())).OrderBy(f => f).ToArray();
                if (files.Length == 0) return;
                _index = (_index + 1) % files.Length;
                _path = files[_index];
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.UriSource = new Uri(_path);
                bi.DecodePixelWidth = 960;
                bi.EndInit();
                bi.Freeze();
                _img = bi;
            }
            catch { _img = null; }
        }

        public override void Draw(DrawingContext dc, FrameCtx ctx)
        {
            if (_img == null)
            {
                D.Header(dc, Head(ctx, "image"));
                dc.DrawRoundedRectangle(null, new Pen(Pal.Line, 2) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) },
                    new Rect(40, 56, 400, 240), 12, 12);
                D.Empty(dc, "image", Pal.Ink3, "Coloque imagens na pasta", "Imagens › Pixo, ou escolha outra pasta no app, aba Imagens.", 84);
                return;
            }

            double iw = _img.PixelWidth, ih = _img.PixelHeight;
            Rect r;
            if (_cfg().ImageFill)
            {
                double s = Math.Max(480 / iw, 320 / ih);
                r = new Rect((480 - iw * s) / 2, (320 - ih * s) / 2, iw * s, ih * s);
            }
            else
            {
                double s = Math.Min(480 / iw, 320 / ih);
                r = new Rect((480 - iw * s) / 2, (320 - ih * s) / 2, iw * s, ih * s);
            }
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, 480, 320)));
            dc.DrawImage(_img, r);
            dc.Pop();

            // hora numa pílula
            dc.DrawRoundedRectangle(Pal.B("#080c10", 0.72), null, new Rect(400, 280, 68, 28), 14, 14);
            D.Text(dc, DateTime.Now.ToString("HH:mm"), 400, 280, 68, 28, 13, 600, Pal.Ink, Align.Center);
        }
    }

    // =====================================================================
    // g) RELÓGIO
    // =====================================================================
    public class ClockScreen : Screen
    {
        public override string Id => "relogio";

        public override void Draw(DrawingContext dc, FrameCtx ctx)
        {
            var h = Head(ctx, "clock");
            h.ShowTime = false;
            D.Header(dc, h);
            var now = DateTime.Now;
            D.Text(dc, now.ToString("HH:mm"), 0, 74, 480, 128, 128, 700, Pal.Ink, Align.Center);
            int sec = now.Second;
            for (int i = 0; i < 60; i++)
            {
                bool major = i % 5 == 0;
                dc.DrawRoundedRectangle(i <= sec ? Pal.Accent : Pal.Line, null,
                    new Rect(61 + i * 6, major ? 229 : 232, 3, major ? 14 : 8), 1.5, 1.5);
            }
            string date = now.ToString("dddd, d 'de' MMMM", D.PtBr).ToLower(D.PtBr);
            D.Text(dc, date, 0, 262, 480, 24, 20, 700, Pal.Ink2, Align.Center);
        }
    }

    // =====================================================================
    // h) BICHINHO
    // =====================================================================
    public class PetScreen : Screen
    {
        public override string Id => "bichinho";
        private readonly Func<Config> _cfg;
        private string _state = "feliz";
        private int _frame;
        private DateTime _nextFrame = DateTime.MinValue;
        private string _reaction;
        private DateTime _reactionUntil;
        private DateTime _nextHeart = DateTime.Now.AddSeconds(20);
        private bool _oneShotDone;

        public PetScreen(Func<Config> cfg) { _cfg = cfg; }

        public string State => _state;

        /// <summary>Reação curta (susto/comemora). Vale por 30 s se o bichinho não estiver na tela.</summary>
        public void React(string reaction)
        {
            _reaction = reaction;
            _reactionUntil = DateTime.Now.AddSeconds(30);
        }

        public override TimeSpan Interval
        {
            get
            {
                var d = _nextFrame - DateTime.Now;
                return d < TimeSpan.FromMilliseconds(20) ? TimeSpan.FromMilliseconds(20) : d;
            }
        }

        public override void OnShow() { _nextFrame = DateTime.MinValue; }

        /// <summary>Estado de fundo, conforme o que acontece no PC.</summary>
        private string BaseState()
        {
            var hw = HardwareService.Current;
            var cfg = _cfg();
            double temp = new[] { hw.Cpu?.Temp, hw.Gpu?.Temp }.Where(t => t != null).Select(t => t.Value).DefaultIfEmpty(0).Max();
            if (GameService.Current != null) return "jogando";
            if (temp > 80) return "quente";
            if (MediaService.Current?.Playing == true) return "musica";
            if (GameService.IdleSeconds() > cfg.PetSleepMinutes * 60) return "dormindo";
            if ((hw.CpuLoad ?? 0) > 60) return "ocupado";
            return "feliz";
        }

        private void Advance()
        {
            var now = DateTime.Now;
            if (now < _nextFrame) return;

            var info = Bit.States[_state];
            bool atEnd = _frame >= info.Durations.Length - 1;

            // um estado sem loop termina e volta para o de fundo
            if (!info.Loop && atEnd && !_oneShotDone)
            {
                _oneShotDone = true;
                _nextFrame = now.AddMilliseconds(900);
                return;
            }

            string target = BaseState();
            if (_reaction != null && now < _reactionUntil) { target = _reaction; _reaction = null; }
            else if (_reaction != null) _reaction = null;
            else if (target == "feliz" && now > _nextHeart && (_state == "feliz" || !Bit.States[_state].Loop))
            {
                target = "feliz_coracao";
                _nextHeart = now.AddSeconds(25);
            }

            bool reactionPlaying = !info.Loop && !_oneShotDone;
            if (reactionPlaying && target != "susto" && target != "comemora")
            {
                _frame++; // termina a reação antes de trocar
            }
            else if (target != _state && (info.Loop || _oneShotDone || target == "susto" || target == "comemora"))
            {
                _state = target; _frame = 0; _oneShotDone = false;
            }
            else
            {
                _frame = info.Loop ? (_frame + 1) % info.Durations.Length : Math.Min(_frame + 1, info.Durations.Length - 1);
            }
            _nextFrame = now.AddMilliseconds(Bit.States[_state].Durations[_frame]);
        }

        public override void Draw(DrawingContext dc, FrameCtx ctx)
        {
            Advance();
            var cfg = _cfg();
            var hw = HardwareService.Current;
            var info = Bit.States[_state];
            D.Header(dc, Head(ctx, "pet"));

            dc.PushTransform(new TranslateTransform(24, 72));
            Bit.Draw(dc, _state, _frame);
            dc.Pop();

            D.Text(dc, cfg.PetName, 220, 60, 248, 38, 32, 800, Pal.Ink);
            D.Chip(dc, info.Chip, 220, 104, 20, Pal.S2, Pal.Accent, 12, 700, 8);
            D.Text(dc, info.Phrase, 220, 132, 248, 40, 16, 600, Pal.Ink2, Align.Left, 2);

            double cpu = hw.CpuLoad ?? 0;
            double temp = hw.Cpu?.Temp ?? 0;
            D.Text(dc, "Energia", 220, 196, 120, 16, 13, 600, Pal.Ink);
            D.Text(dc, D.N0(cpu) + "% CPU", 368, 196, 100, 16, 13, 600, Pal.Ink2, Align.Right);
            D.Bar(dc, 220, 218, 248, 10, cpu, Pal.Accent);
            D.Text(dc, "Calor", 220, 242, 120, 16, 13, 600, Pal.Ink);
            D.Text(dc, D.N0(temp) + "°C", 368, 242, 100, 16, 13, 600, Pal.Ink2, Align.Right);
            D.Bar(dc, 220, 264, 248, 10, temp, Pal.Temp(hw.Cpu?.Temp));
        }
    }

    // =====================================================================
    // Boas-vindas (ao conectar)
    // =====================================================================
    public class WelcomeScreen : Screen
    {
        public override string Id => "boasvindas";
        private readonly Func<Config> _cfg;
        private int _frame;
        public WelcomeScreen(Func<Config> cfg) { _cfg = cfg; }
        public override TimeSpan Interval => TimeSpan.FromMilliseconds(125);
        public override void OnShow() { _frame = 0; }

        public override void Draw(DrawingContext dc, FrameCtx ctx)
        {
            dc.PushTransform(new TranslateTransform(150, 28));
            Bit.Draw(dc, "feliz_coracao", Math.Min(_frame, 11));
            dc.Pop();
            _frame++;
            D.Text(dc, "Oi! Eu sou o " + _cfg().PetName + ".", 0, 214, 480, 38, 32, 800, Pal.Ink, Align.Center);
            D.Text(dc, "Tudo conectado. Vamos nessa!", 0, 258, 480, 20, 16, 600, Pal.Ink2, Align.Center);
        }
    }
}
