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

            // app de origem: ícone oficial do Windows; sem ele, o "slot" com a cor da marca + inicial
            double ax = Math.Max(300, 200 + chipW + 12);
            if (m.AppIcon != null)
            {
                dc.DrawImage(m.AppIcon, new Rect(ax, 190, 16, 16));
            }
            else
            {
                var appColor = Pal.ForName(m.App);
                dc.DrawEllipse(appColor, null, new Point(ax + 8, 198), 8, 8);
                D.Text(dc, m.App.Substring(0, 1).ToUpper(), ax, 190, 16, 16, 11, 800, Pal.Bg, Align.Center);
            }
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
            string sub = fps == null ? "Jogue em tela cheia para ver o FPS"
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
    // g2) CLIMA (Open-Meteo) + quarto (ar-condicionado LG)
    // =====================================================================
    public class WeatherScreen : Screen
    {
        public override string Id => "clima";
        public override TimeSpan Interval => TimeSpan.FromSeconds(5);
        private readonly Func<Config> _cfg;

        public WeatherScreen(Func<Config> cfg) { _cfg = cfg; }

        public static Brush IconColor(string icon) => icon switch
        {
            "sun" or "cloudSun" => Pal.Warn,
            "moon" or "cloudMoon" => Pal.Accent,
            "rain" or "drizzle" => Pal.Blue,
            "storm" => Pal.Gamer,
            "snow" => Pal.Ink,
            "fog" => Pal.Ink3,
            _ => Pal.Ink2,
        };

        /// <summary>Cor de conforto de uma temperatura ambiente.</summary>
        public static Brush Comfort(double? t) => t == null ? Pal.Ink3 : t < 18 ? Pal.Blue : t < 26 ? Pal.Ok : t < 30 ? Pal.Warn : Pal.Hot;

        private static string Deg(double? t) => t == null ? "--" : D.N0(t.Value) + "°";

        private static string DayName(DateTime d)
        {
            if (d.Date == DateTime.Today) return "Hoje";
            if (d.Date == DateTime.Today.AddDays(1)) return "Amanhã";
            var s = d.ToString("ddd", D.PtBr).TrimEnd('.');
            return s.Length > 0 ? char.ToUpper(s[0], D.PtBr) + s.Substring(1) : s;
        }

        public override void Draw(DrawingContext dc, FrameCtx ctx)
        {
            var w = WeatherService.Current;
            var ac = AcService.Configured(_cfg()) ? AcService.Current : null;
            var head = Head(ctx, "cloudSun");
            if (!string.IsNullOrEmpty(w?.Place)) head.Title = "Clima · " + w.Place.Split(',')[0];
            D.Header(dc, head);

            if (w == null)
            {
                var err = WeatherService.Error;
                D.Empty(dc, err == null ? "cloud" : "alert", err == null ? Pal.Ink3 : Pal.Warn,
                    err == null ? "Buscando o clima…" : "Sem dados do clima",
                    err ?? "Leva só alguns segundos.", 78);
                return;
            }

            // ---- agora, lá fora ----
            D.Card(dc, 12, 48, 228, 146);
            string icon = WeatherService.Icon(w.Code, w.IsDay);
            D.Icon(dc, icon, 22, 60, 60, IconColor(icon), 2.2);
            D.Num(dc, w.Temp == null ? "--" : D.N0(w.Temp.Value), "°", 92, 58, 140, 64, 60, 700, Pal.Ink, Align.Left, 28);
            D.Text(dc, WeatherService.Describe(w.Code), 22, 130, 206, 20, 15, 700, Pal.Ink);
            var bits = new System.Collections.Generic.List<string>();
            if (w.Feels != null) bits.Add("Sensação " + Deg(w.Feels));
            if (w.Humidity != null) bits.Add("Umidade " + D.N0(w.Humidity.Value) + "%");
            D.Text(dc, string.Join(" · ", bits), 22, 154, 206, 16, 12, 600, Pal.Ink2);
            if (w.Wind != null) D.Text(dc, "Vento " + D.N0(w.Wind.Value) + " km/h", 22, 172, 206, 16, 12, 600, Pal.Ink2);

            // ---- no quarto (ar LG) ou, sem ar, chuva e vento ----
            D.Card(dc, 252, 48, 216, 146);
            if (ac != null)
            {
                D.Icon(dc, "ac", 264, 60, 18, Pal.Accent, 2);
                D.Text(dc, "NO QUARTO", 288, 60, 168, 18, 13, 700, Pal.Ink2);
                var col = Comfort(ac.Room);
                D.Num(dc, ac.Room == null ? "--" : (ac.Room.Value % 1 == 0 ? D.N0(ac.Room.Value) : D.N1(ac.Room.Value)), "°C",
                    264, 84, 192, 56, 48, 700, col, Align.Left, 20);
                string status = ac.On
                    ? "Ar ligado" + (ac.Target != null ? " · " + Deg(ac.Target) : "") + (string.IsNullOrEmpty(ac.Mode) ? "" : " · " + ac.Mode)
                    : "Ar desligado";
                D.Dot(dc, 264, 150, 8, ac.On ? Pal.Ok : Pal.Ink3);
                D.Text(dc, status, 278, 145, 182, 18, 12, 600, Pal.Ink2);
                if (ac.Room != null && w.Temp != null)
                {
                    // compara os números como aparecem na tela (arredondados)
                    double diff = Math.Round(ac.Room.Value, MidpointRounding.AwayFromZero) - Math.Round(w.Temp.Value, MidpointRounding.AwayFromZero);
                    string cmp = diff == 0 ? "Igual a lá fora" : D.N0(Math.Abs(diff)) + "° " + (diff < 0 ? "mais fresco" : "mais quente") + " que lá fora";
                    D.Text(dc, cmp, 264, 168, 196, 16, 12, 600, Pal.Ink3);
                }
            }
            else
            {
                var today = w.Days.Length > 0 ? w.Days[0] : null;
                D.Text(dc, "HOJE", 264, 60, 192, 18, 13, 700, Pal.Ink2);
                D.Icon(dc, "drop", 264, 88, 20, Pal.Blue, 2);
                D.Text(dc, "Chuva", 292, 88, 90, 20, 14, 600, Pal.Ink2);
                D.Text(dc, today?.RainChance == null ? "--" : D.N0(today.RainChance.Value) + "%", 380, 86, 76, 24, 20, 700, Pal.Ink, Align.Right);
                D.Icon(dc, "thermo", 264, 120, 20, Pal.Hot, 2);
                D.Text(dc, "Máx / mín", 292, 120, 90, 20, 14, 600, Pal.Ink2);
                D.Text(dc, Deg(today?.Max) + " / " + Deg(today?.Min), 372, 118, 84, 24, 18, 700, Pal.Ink, Align.Right);
                D.Text(dc, "Ligue o ar LG na aba Clima do Pixo para ver o quarto aqui.", 264, 152, 196, 32, 11, 600, Pal.Ink3, Align.Left, 2);
            }

            // ---- próximos dias ----
            for (int i = 0; i < 3; i++)
            {
                double x = 12 + i * 116, y = 202;
                D.Card(dc, x, y, 108, 106);
                var d = i < w.Days.Length ? w.Days[i] : null;
                if (d == null) continue;
                D.Text(dc, DayName(d.Date), x, y + 8, 108, 18, 13, 700, i == 0 ? Pal.Accent : Pal.Ink2, Align.Center);
                string di = WeatherService.Icon(d.Code);
                D.Icon(dc, di, x + 38, y + 30, 32, IconColor(di), 2);
                D.Text(dc, Deg(d.Max), x + 6, y + 68, 48, 22, 18, 700, Pal.Ink, Align.Right);
                D.Text(dc, Deg(d.Min), x + 58, y + 70, 44, 20, 15, 600, Pal.Ink3, Align.Left);
                if (d.RainChance is double rc && rc >= 20)
                    D.Text(dc, D.N0(rc) + "% chuva", x, y + 88, 108, 14, 11, 600, Pal.Blue, Align.Center);
            }

            // ---- UV, qualidade do ar, sol ----
            double ix = 360, iy = 202;
            D.Card(dc, ix, iy, 108, 106);
            double uv = w.UvNow ?? w.UvMax ?? -1;
            D.Icon(dc, "sun", ix + 10, iy + 10, 14, Pal.Warn, 2);
            D.Text(dc, uv < 0 ? "UV --" : "UV " + D.N0(uv) + " " + WeatherService.UvLevel(uv), ix + 28, iy + 8, 76, 18, 12, 700, Pal.Ink);
            D.Icon(dc, "leaf", ix + 10, iy + 34, 14, Pal.Ok, 2);
            D.Text(dc, w.Aqi == null ? "Ar --" : "Ar " + WeatherService.AqiLevel(w.Aqi.Value), ix + 28, iy + 32, 76, 18, 12, 700, Pal.Ink);
            D.Icon(dc, "sunrise", ix + 10, iy + 58, 14, Pal.Orange, 2);
            D.Text(dc, w.Sunrise?.ToString("HH:mm") ?? "--", ix + 28, iy + 56, 76, 18, 12, 700, Pal.Ink);
            D.Icon(dc, "sunset", ix + 10, iy + 82, 14, Pal.Pink, 2);
            D.Text(dc, w.Sunset?.ToString("HH:mm") ?? "--", ix + 28, iy + 80, 76, 18, 12, 700, Pal.Ink);
        }
    }

    // =====================================================================
    // g3) CLAUDE CODE (painel ao vivo, pelos hooks)
    // =====================================================================
    public class ClaudeScreen : Screen
    {
        public override string Id => "claude";
        public override bool HasContent => ClaudeService.HasActive;
        public override TimeSpan Interval => TimeSpan.FromSeconds(1);

        private static string Ago(DateTime t)
        {
            var d = DateTime.Now - t;
            return d.TotalSeconds < 60 ? $"há {Math.Max(0, (int)d.TotalSeconds)} s"
                 : d.TotalMinutes < 60 ? $"há {(int)d.TotalMinutes} min"
                 : $"há {(int)d.TotalHours} h";
        }

        public override void Draw(DrawingContext dc, FrameCtx ctx)
        {
            var (s, others) = ClaudeService.Snapshot();
            var head = Head(ctx, "claude");
            head.IconColor = Pal.Orange;
            D.Header(dc, head);
            if (s == null)
            {
                D.Empty(dc, "claude", Pal.Ink3, "Nenhuma sessão do Claude Code",
                    "Ligue o Claude Code na aba Geral do Pixo e comece uma conversa.", 78);
                return;
            }

            var (chip, col) = s.Status switch
            {
                ClaudeStatus.Working => ("TRABALHANDO", Pal.Accent),
                ClaudeStatus.Waiting => ("TE CHAMANDO", Pal.Warn),
                ClaudeStatus.Done => ("PRONTO", Pal.Ok),
                _ => ("PARADO", Pal.Ink3),
            };
            double cw = D.Chip(dc, chip, 150, 10, 20, Pal.S2, col, 12, 700, 8);
            if (others > 0) D.Chip(dc, "+" + others, 150 + cw + 6, 10, 20, Pal.S2, Pal.Ink2, 12, 700, 8);

            // ---- projeto, tempo e o pedido ----
            D.Card(dc, 12, 48, 456, 84);
            D.Icon(dc, "folder", 24, 58, 16, Pal.Ink3, 2);
            D.Text(dc, s.Project ?? "—", 46, 57, 280, 18, 13, 700, Pal.Ink2);
            D.Icon(dc, "clock", 368, 58, 16, Pal.Ink3, 2);
            D.Text(dc, D.Clock(s.Elapsed), 388, 55, 68, 22, 17, 700, s.Status == ClaudeStatus.Working ? Pal.Ink : Pal.Ink2, Align.Right);
            D.Text(dc, string.IsNullOrEmpty(s.Prompt) ? "(sem pedido ainda)" : "“" + s.Prompt + "”", 24, 82, 432, 44, 15, 600, Pal.Ink, Align.Left, 2);

            // ---- agora ----
            bool waiting = s.Status == ClaudeStatus.Waiting;
            D.Card(dc, 12, 140, 456, 84, Pal.S1, waiting ? new Pen(Pal.Warn, 2) : null);
            var act = s.Current ?? new ClaudeAction { Icon = "claude", Text = "—", At = s.LastEvent };
            string icon = waiting ? "bell" : act.Icon;
            Brush ic = waiting ? Pal.Warn : s.Status == ClaudeStatus.Done ? Pal.Ok : s.Status == ClaudeStatus.Working ? Pal.Accent : Pal.Ink3;
            D.Icon(dc, icon, 26, 160, 30, ic, 2.2);
            D.Text(dc, waiting ? s.WaitingFor : act.Text, 70, 152, 386, 30, 20, 700, Pal.Ink);
            string sub = waiting ? "O Claude precisa de você"
                : s.Status == ClaudeStatus.Done ? "Terminou em " + D.Clock(s.Elapsed) + " · " + Ago(s.TurnEnd)
                : s.Status == ClaudeStatus.Working ? Ago(act.At)
                : "Sem novidade " + Ago(s.LastEvent);
            D.Text(dc, sub, 70, 186, 386, 18, 13, 600, waiting ? Pal.Warn : Pal.Ink2);

            // ---- números do pedido ----
            D.Icon(dc, "sliders", 16, 236, 16, Pal.Ink3, 2);
            D.Text(dc, s.Tools == 1 ? "1 ação" : s.Tools + " ações", 38, 234, 120, 20, 14, 700, Pal.Ink);
            D.Icon(dc, "edit", 160, 236, 16, Pal.Ink3, 2);
            D.Text(dc, s.Files.Count == 1 ? "1 arquivo mexido" : s.Files.Count + " arquivos mexidos", 182, 234, 170, 20, 14, 700, Pal.Ink);

            // ---- o que fez antes (a última) ----
            var before = s.Recent.LastOrDefault(a => !ReferenceEquals(a, s.Current));
            if (before != null)
            {
                D.Icon(dc, before.Icon, 16, 260, 14, Pal.Ink3, 2);
                D.Text(dc, before.Text, 38, 258, 340, 18, 13, 600, Pal.Ink3);
                D.Text(dc, Ago(before.At), 380, 258, 88, 18, 12, 600, Pal.Ink3, Align.Right);
            }

            // ---- placar do dia ----
            var day = ClaudeService.Today();
            D.Card(dc, 12, 280, 456, 36);
            D.Text(dc, "HOJE", 22, 280, 40, 36, 11, 700, Pal.Orange);
            var cells = new (string num, string label, double x, double w)[]
            {
                (D.N0(day.Prompts), day.Prompts == 1 ? "pedido" : "pedidos", 64, 66),
                (Duration(day.WorkSeconds), "trabalhando", 130, 88),
                (D.N0(day.Tools), day.Tools == 1 ? "ação" : "ações", 218, 62),
                (D.N0(day.Files.Count), day.Files.Count == 1 ? "arquivo" : "arquivos", 280, 72),
                (Tokens(day.OutputTokens), "tokens escritos", 352, 108),
            };
            foreach (var c in cells)
            {
                D.Text(dc, c.num, c.x, 282, c.w, 18, 14, 700, Pal.Ink);
                D.Text(dc, c.label, c.x, 299, c.w, 14, 10, 600, Pal.Ink3);
            }
        }

        /// <summary>"35 min", "1h12".</summary>
        public static string Duration(double seconds)
        {
            var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h{t.Minutes:00}" : $"{(int)t.TotalMinutes} min";
        }

        /// <summary>"850", "23 mil", "1,2 mi".</summary>
        public static string Tokens(long n) =>
            n >= 1_000_000 ? (n / 1_000_000.0).ToString("0.#", D.PtBr) + " mi"
            : n >= 1_000 ? D.N0(n / 1000.0) + " mil"
            : D.N0(n);
    }

    // =====================================================================
    // g4) SAÚDE DO PC
    // =====================================================================
    public class HealthScreen : Screen
    {
        public override string Id => "saude";
        public override bool HasContent => HealthService.Warnings > 0;
        public override TimeSpan Interval => TimeSpan.FromSeconds(5);

        public override void Draw(DrawingContext dc, FrameCtx ctx)
        {
            D.Header(dc, Head(ctx, "pulse"));
            var items = HealthService.Items;
            int warn = items.Count(i => i.Level != HealthLevel.Ok);
            D.Chip(dc, warn == 0 ? "TUDO CERTO" : warn == 1 ? "1 AVISO" : warn + " AVISOS", 150, 10, 20, Pal.S2, warn == 0 ? Pal.Ok : Pal.Warn, 12, 700, 8);
            if (items.Length == 0)
            {
                D.Empty(dc, "pulse", Pal.Ink3, "Conferindo o PC…", "Leva só alguns segundos.", 78);
                return;
            }

            // avisos primeiro
            var list = items.OrderByDescending(i => i.Level).Take(5).ToList();
            for (int i = 0; i < list.Count; i++)
            {
                var it = list[i];
                double y = 48 + i * 53;
                var col = it.Level == HealthLevel.Bad ? Pal.Hot : it.Level == HealthLevel.Warn ? Pal.Warn : Pal.Ok;
                D.Card(dc, 12, y, 456, 47, Pal.S1, it.Level == HealthLevel.Ok ? null : new Pen(col, 1.5));
                D.Icon(dc, it.Icon, 24, y + 13, 20, it.Level == HealthLevel.Ok ? Pal.Ink2 : col, 2);
                D.Text(dc, it.Title, 56, y + 6, 380, 18, 14, 700, Pal.Ink);
                D.Text(dc, it.Detail, 56, y + 25, 390, 16, 12, 600, it.Level == HealthLevel.Ok ? Pal.Ink3 : Pal.Ink2);
                D.Dot(dc, 448, y + 19, 9, col);
            }
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

        // recado temporário (pausa, Claude Code, Pixo API...): troca a frase e, se tiver, o chip
        private string _sayChip, _sayText;
        private DateTime _sayUntil;

        public PetScreen(Func<Config> cfg) { _cfg = cfg; }

        public string State => _state;

        /// <summary>Reação curta (susto, comemora, feliz_coracao). Vale por 30 s se o bichinho não estiver na tela.</summary>
        public void React(string reaction)
        {
            _reaction = reaction;
            _reactionUntil = DateTime.Now.AddSeconds(30);
        }

        /// <summary>Mostra um recado no lugar da frase por alguns segundos (chip null = o do estado).</summary>
        public void Say(string chip, string text, double seconds = 30)
        {
            _sayChip = chip;
            _sayText = text;
            _sayUntil = DateTime.Now.AddSeconds(seconds);
        }

        public static bool IsNight(DateTime t) => t.Hour >= 23 || t.Hour < 6;

        // ---------------- cena: clima, datas e visitas ----------------

        private readonly PetScene _scene = new PetScene();
        private readonly DateTime _t0 = DateTime.Now;

        // visita pedida (vale 30 s; só começa quando a tela dos bichinhos aparece, como as reações)
        private string _visitPending, _visit;
        private DateTime _visitPendingUntil, _visitStart;
        private bool _visitReacted;
        private bool _ghost;

        // teste: uma cena escolhida na janela toca na tela por um tempo
        private string _sceneForced;
        private DateTime _sceneForcedUntil;

        /// <summary>Uma visita: "passarinho" (traz a notificação) ou "entrega" (download pronto). Ao pousar, a dupla reage.</summary>
        public void Visit(string who)
        {
            _visitPending = who;
            _visitPendingUntil = DateTime.Now.AddSeconds(30);
        }

        /// <summary>Visitas que podem ser testadas na janela.</summary>
        public static readonly (string Id, string Name)[] SceneChoices =
        {
            ("chuva", "Chuva lá fora"), ("neve", "Neve lá fora"), ("sol", "Calor lá fora"), ("frio", "Frio (ou ar gelado)"),
            ("natal", "Natal"), ("anonovo", "Ano Novo"), ("junina", "Festa junina"), ("halloween", "Dia das Bruxas"), ("aniversario", "Aniversário da dupla"),
            ("passarinho", "Visita: passarinho (notificação)"), ("entrega", "Visita: entrega (download)"), ("fantasma", "Visita: fantasma (PC a 90°)"),
        };

        /// <summary>Mostra uma cena por alguns segundos (as visitas começam já).</summary>
        public void PreviewScene(string kind, double seconds = 30)
        {
            if (kind == "passarinho" || kind == "entrega") { Visit(kind); return; }
            _sceneForced = kind;
            _sceneForcedUntil = DateTime.Now.AddSeconds(seconds);
        }

        /// <summary>Clima lá fora (e no quarto, pelo ar LG) do jeito que a dupla se veste.</summary>
        public static string WeatherKind(WeatherInfo w, AcInfo ac)
        {
            if (w != null && w.Temp != null)
            {
                int c = w.Code;
                if (c is >= 71 and <= 77 or 85 or 86) return "neve";
                if (c is >= 51 and <= 67 or >= 80 and <= 82 or >= 95) return "chuva";
                if (w.Temp >= 30 && w.IsDay) return "sol";
            }
            bool acCold = ac != null && ac.On && ac.Mode == "Frio" && (ac.Target ?? 99) <= 21;
            if (acCold || w?.Temp <= 14) return "frio";
            return null;
        }

        /// <summary>Data especial de hoje (ou null).</summary>
        public static string PartyKind(DateTime d, DateTime? birthday)
        {
            if (birthday is DateTime b && d.Month == b.Month && d.Day == b.Day && d.Year > b.Year) return "aniversario";
            if ((d.Month == 12 && d.Day == 31) || (d.Month == 1 && d.Day == 1)) return "anonovo";
            if (d.Month == 12 && d.Day <= 26) return "natal";
            if (d.Month == 6) return "junina";
            if (d.Month == 10 && d.Day >= 29) return "halloween";
            return null;
        }

        /// <summary>Cena para a prévia da janela: a de agora (kind = null) ou uma escolhida (as visitas repetem).</summary>
        public static PetScene SceneFor(string kind, Config cfg, double time)
        {
            var sc = new PetScene { Time = time };
            if (kind == null)
            {
                sc.Weather = WeatherKind(WeatherService.Current, AcService.Configured(cfg) ? AcService.Current : null);
                sc.Party = PartyKind(DateTime.Now, cfg.PetBirthday);
            }
            else if (kind is "chuva" or "neve" or "sol" or "frio") sc.Weather = kind;
            else if (kind is "passarinho" or "entrega")
            {
                sc.Visitor = kind;
                sc.VisitT = time % (Pets.VisitLength(kind) + 1.5);
            }
            else if (kind == "fantasma") sc.Visitor = kind;
            else sc.Party = kind;
            return sc;
        }

        /// <summary>Atualiza a cena do quadro: clima, data, visita (e dispara a reação quando ela pousa).</summary>
        private PetScene UpdateScene(Config cfg, DateTime now)
        {
            var sc = _scene;
            sc.Time = (now - _t0).TotalSeconds;
            sc.Weather = WeatherKind(WeatherService.Current, AcService.Configured(cfg) ? AcService.Current : null);
            sc.Party = PartyKind(now, cfg.PetBirthday);

            // fantasma: aparece com a CPU ou a GPU acima de 90° e só vai embora abaixo de 85°
            var hw = HardwareService.Current;
            double temp = new[] { hw.Cpu?.Temp, hw.Gpu?.Temp }.Where(t => t != null).Select(t => t.Value).DefaultIfEmpty(0).Max();
            if (temp > 90) _ghost = true; else if (temp < 85) _ghost = false;

            if (_visitPending != null)
            {
                if (now < _visitPendingUntil) { _visit = _visitPending; _visitStart = now; _visitReacted = false; }
                _visitPending = null;
            }
            sc.Visitor = null; sc.VisitT = 0;
            if (_visit != null)
            {
                double t = (now - _visitStart).TotalSeconds;
                if (t >= Pets.VisitLength(_visit)) _visit = null;
                else
                {
                    sc.Visitor = _visit; sc.VisitT = t;
                    if (!_visitReacted && t >= Pets.VisitLandAt(_visit))
                    {
                        _visitReacted = true;
                        React(_visit == "entrega" ? "comemora" : "susto");
                    }
                }
            }

            bool forcing = _sceneForced != null && now < _sceneForcedUntil;
            if (!forcing) _sceneForced = null;
            if (forcing)
            {
                var f = SceneFor(_sceneForced, cfg, sc.Time);
                if (f.Weather != null) sc.Weather = f.Weather;
                if (f.Party != null) sc.Party = f.Party;
                if (f.Visitor == "fantasma") sc.Visitor = "fantasma";
            }
            else if (sc.Visitor == null && _ghost) sc.Visitor = "fantasma";
            return sc;
        }

        // teste: uma animação escolhida na janela (ou pela API) toca na tela por um tempo, ignorando o PC
        private string _forced;
        private DateTime _forcedUntil;

        /// <summary>Toca esta animação na tela por alguns segundos (as reações repetem até acabar o tempo).</summary>
        public void Preview(string state, double seconds = 30)
        {
            if (!Pets.States.ContainsKey(state)) return;
            _forced = state;
            _forcedUntil = DateTime.Now.AddSeconds(seconds);
            _nextFrame = DateTime.MinValue; // troca já
        }

        /// <summary>Volta para o "ao vivo" (o que o PC estiver fazendo).</summary>
        public void StopPreview() { _forced = null; _nextFrame = DateTime.MinValue; }

        // redesenha ~25 vezes por segundo; a dupla é desenhada "entre poses" (interpolada),
        // então a animação fica fluida. Só o pedaço que mudou vai para a tela.
        public override TimeSpan Interval => TimeSpan.FromMilliseconds(40);

        private DateTime _frameStart = DateTime.Now;

        // troca de estado suave: de onde o corpo vinha e quando trocou
        private string _from;
        private double _fromPos;
        private DateTime _changedAt = DateTime.MinValue;
        private const double GlideMs = 280;

        public override void OnShow() { _nextFrame = DateTime.MinValue; _from = null; }

        /// <summary>Posição atual na animação, com fração (ex.: 3,4 = 40% entre a pose 3 e a 4).</summary>
        private double Position()
        {
            var info = Pets.States[_state];
            bool lastOfOneShot = !info.Loop && _frame >= info.Durations.Length - 1;
            if (_oneShotDone || lastOfOneShot) return _frame;
            double total = (_nextFrame - _frameStart).TotalMilliseconds;
            if (total <= 0) return _frame;
            double t = (DateTime.Now - _frameStart).TotalMilliseconds / total;
            return _frame + Math.Clamp(t, 0, 0.999);
        }

        /// <summary>Estado de fundo, conforme o que acontece no PC.</summary>
        private string BaseState()
        {
            var hw = HardwareService.Current;
            var cfg = _cfg();
            double temp = new[] { hw.Cpu?.Temp, hw.Gpu?.Temp }.Where(t => t != null).Select(t => t.Value).DefaultIfEmpty(0).Max();
            if (GameService.Current != null) return "jogando";
            if (ClaudeService.LongestWait().wait != null) return "esperando"; // o Claude pediu algo e está esperando você
            if (temp > 80) return "quente";
            if (MediaService.Current?.Playing == true) return "musica";
            if (ClaudeService.AnyWorking) return "ocupado"; // ajudando o Claude Code
            if (GameService.IdleSeconds() > cfg.PetSleepMinutes * 60) return "dormindo";
            if (IsNight(DateTime.Now)) return "dormindo"; // depois das 23h a dupla vai dormir
            if ((hw.CpuLoad ?? 0) > 60) return "ocupado";
            return "feliz";
        }

        private void Advance()
        {
            var now = DateTime.Now;
            if (now < _nextFrame) return;

            var info = Pets.States[_state];
            bool atEnd = _frame >= info.Durations.Length - 1;

            // um estado sem loop termina e volta para o de fundo
            if (!info.Loop && atEnd && !_oneShotDone)
            {
                _oneShotDone = true;
                _frameStart = now;
                _nextFrame = now.AddMilliseconds(900);
                return;
            }

            bool forcing = _forced != null && now < _forcedUntil;
            if (!forcing) _forced = null;
            string target = forcing ? _forced : BaseState();
            if (forcing) { }
            else if (_reaction != null && now < _reactionUntil) { target = _reaction; _reaction = null; }
            else if (_reaction != null) _reaction = null;
            else if (target == "feliz" && now > _nextHeart && (_state == "feliz" || !Pets.States[_state].Loop))
            {
                // de bom humor, coraçõezinhos mais vezes; carentes, nenhum
                double mood = _cfg().PetMood;
                _nextHeart = now.AddSeconds(mood >= 70 ? 15 : 25);
                if (mood >= 30) target = "feliz_coracao";
            }

            bool reactionPlaying = !info.Loop && !_oneShotDone;
            if (forcing && target == _state && !info.Loop && _oneShotDone)
            {
                _frame = 0; _oneShotDone = false; // no teste, a reação repete
            }
            else if (!forcing && reactionPlaying && target != "susto" && target != "comemora")
            {
                _frame++; // termina a reação antes de trocar
            }
            else if (target != _state && (forcing || info.Loop || _oneShotDone || target == "susto" || target == "comemora"))
            {
                _from = _state; _fromPos = Position(); _changedAt = now; // o corpo desliza da pose antiga
                _state = target; _frame = 0; _oneShotDone = false;
            }
            else
            {
                _frame = info.Loop ? (_frame + 1) % info.Durations.Length : Math.Min(_frame + 1, info.Durations.Length - 1);
            }
            _frameStart = now;
            _nextFrame = now.AddMilliseconds(Pets.States[_state].Durations[_frame]);
        }

        public override void Draw(DrawingContext dc, FrameCtx ctx)
        {
            Advance();
            var cfg = _cfg();
            var hw = HardwareService.Current;
            var info = Pets.States[_state];
            var now = DateTime.Now;
            var scene = UpdateScene(cfg, now);

            bool saying = _sayText != null && DateTime.Now < _sayUntil;
            bool needy = _state == "feliz" && cfg.PetMood < 30;

            // cabeçalho + chip do humor
            D.Header(dc, Head(ctx, "pet"));
            string chip = saying && _sayChip != null ? _sayChip : needy ? "CARENTES" : info.Chip;
            D.Chip(dc, chip, 132, 10, 20, Pal.S2, saying && _sayChip != null ? Pal.Warn : Pal.Accent, 12, 700, 8);

            // humor da dupla: 5 coraçõezinhos no canto
            int full = (int)Math.Round(cfg.PetMood / 20);
            for (int i = 0; i < 5; i++) PixelHeart(dc, 392 + i * 16, 48, i < full ? Pal.Pink : Pal.Line);

            // a dupla na almofada (360×216, pixel art 6×)
            double mix = _from == null ? 1 : (DateTime.Now - _changedAt).TotalMilliseconds / GlideMs;
            if (mix >= 1) _from = null;
            D.PetsDuo(dc, _state, Position(), 60, 40, true, _from, _fromPos, Math.Min(1, mix), scene);

            // o Claude espera você há mais de 2 minutos: faixas piscando em cima e embaixo
            // (só faixas inteiras: linhas seguidas saem baratas no USB)
            var (wait, _) = ClaudeService.LongestWait();
            if (wait >= TimeSpan.FromMinutes(2) && (int)Math.Floor((now - _t0).TotalSeconds) % 2 == 0)
            {
                dc.DrawRectangle(Pal.Warn, null, new Rect(0, 0, 480, 4));
                dc.DrawRectangle(Pal.Warn, null, new Rect(0, 316, 480, 4));
            }

            // nomes embaixo de cada um
            D.Text(dc, cfg.PetName1, 112, 258, 100, 16, 13, 700, Pal.Ink, Align.Center);
            D.Text(dc, cfg.PetName2, 256, 258, 100, 16, 13, 700, Pal.Ink, Align.Center);

            // frase da dupla (ou o recado do momento)
            string phrase = saying ? _sayText
                : scene.Visitor == "fantasma" ? "Tá tão quente que apareceu até um fantasma!"
                : needy ? string.Format("{0} e {1} tão com saudade de um carinho…", cfg.PetName1, cfg.PetName2)
                : _state == "dormindo" && IsNight(now) && GameService.IdleSeconds() < cfg.PetSleepMinutes * 60
                    ? "Já passou das 23h… a dupla foi dormir. E você?"
                : _state == "ocupado" && ClaudeService.AnyWorking
                    ? string.Format("{0} e {1} tão ajudando o Claude a programar!", cfg.PetName1, cfg.PetName2)
                : _state.StartsWith("feliz") && SceneLine(scene, cfg, now) is string line ? line
                : string.Format(info.Phrase, cfg.PetName1, cfg.PetName2);
            D.Text(dc, phrase, 12, 278, 228, 32, 13, 600, saying ? Pal.Ink : Pal.Ink2, Align.Left, 2);

            // energia (CPU) e calor, em bloquinhos retrô
            double cpu = hw.CpuLoad ?? 0;
            double temp = hw.Cpu?.Temp ?? 0;
            D.Text(dc, "Energia", 256, 277, 56, 16, 12, 600, Pal.Ink2);
            D.SegBar(dc, 316, 280, 152, 10, 10, cpu, Pal.Accent);
            D.Text(dc, "Calor", 256, 294, 56, 16, 12, 600, Pal.Ink2);
            D.SegBar(dc, 316, 297, 152, 10, 10, temp, Pal.Temp(hw.Cpu?.Temp));
        }

        /// <summary>Frase da data especial ou do clima (quando a dupla está só de boa).</summary>
        public static string SceneLine(PetScene sc, Config cfg, DateTime now)
        {
            string a = cfg.PetName1, b = cfg.PetName2;
            switch (sc.Party)
            {
                case "natal": return now.Day >= 24 && now.Day <= 25 ? "Feliz Natal! A dupla tá de gorrinho." : $"Clima de Natal! {a} e {b} de gorrinho.";
                case "anonovo": return now.Month == 12 ? "Hoje tem fogos! Feliz Ano Novo!" : "Feliz Ano Novo! Que venha um ano de boa.";
                case "junina": return "Arraiá da dupla! Chapéu de palha e bandeirinha.";
                case "halloween": return "Dia das Bruxas! Cuidado com a abóbora.";
                case "aniversario":
                {
                    int years = now.Year - (cfg.PetBirthday ?? now).Year;
                    return $"Hoje a dupla faz {years} {(years == 1 ? "ano" : "anos")}! Parabéns, {a} e {b}!";
                }
            }
            return sc.Weather switch
            {
                "chuva" => $"Chovendo lá fora… o {b} trouxe o guarda-chuva.",
                "neve" => "Nevando lá fora?! Cachecol na dupla.",
                "sol" => "Calorão lá fora! Óculos escuros na dupla.",
                "frio" => "Friozinho! Cachecol na dupla.",
                _ => null,
            };
        }

        // coração em pixel art (7×6 pixels de 2 px)
        private static readonly string[] HeartMap = { ".xx.xx.", "xxxxxxx", "xxxxxxx", ".xxxxx.", "..xxx..", "...x..." };

        private static void PixelHeart(DrawingContext dc, double x, double y, Brush color)
        {
            for (int j = 0; j < HeartMap.Length; j++)
                for (int i = 0; i < HeartMap[j].Length; i++)
                    if (HeartMap[j][i] == 'x') dc.DrawRectangle(color, null, new Rect(x + i * 2, y + j * 2, 2, 2));
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
        public override TimeSpan Interval => TimeSpan.FromMilliseconds(40);
        public override void OnShow() { _frame = 0; }

        public override void Draw(DrawingContext dc, FrameCtx ctx)
        {
            var cfg = _cfg();
            // ~25 quadros/s; o coração sobe ao longo dos 12 quadros do estado
            double pos = Math.Min(_frame * 0.32, 11);
            D.PetsDuo(dc, "feliz_coracao", pos, 60, 16);
            _frame++;
            D.Text(dc, "Oi! Somos o " + cfg.PetName1 + " e o " + cfg.PetName2 + ".", 0, 238, 480, 28, 24, 700, Pal.Ink, Align.Center);
            D.Text(dc, "Tudo conectado. Vamos nessa!", 0, 272, 480, 20, 15, 600, Pal.Ink2, Align.Center);
        }
    }
}
