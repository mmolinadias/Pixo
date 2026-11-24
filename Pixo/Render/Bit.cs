using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Pixo.Render
{
    /// <summary>
    /// O Bit, desenhado num quadro de 180×180 (fundo transparente).
    /// Tradução direta do desenho e das animações do sistema de design Pixo.
    /// Cada estado é uma lista de quadros, e cada quadro tem a sua duração (ms).
    /// </summary>
    public static class Bit
    {
        public const int Size = 180;

        public class StateInfo
        {
            public string Name, Phrase, Chip;
            public bool Loop;
            public int[] Durations;
        }

        public static readonly Dictionary<string, StateInfo> States = new Dictionary<string, StateInfo>
        {
            ["feliz"] = new StateInfo { Name = "Feliz / parado", Loop = true, Chip = "FELIZ", Phrase = "Tudo tranquilo por aqui.", Durations = new[] { 375, 125, 125, 125, 125, 250, 125, 250, 125, 125, 250, 125 } },
            ["feliz_coracao"] = new StateInfo { Name = "Feliz + coração", Loop = false, Chip = "FELIZ", Phrase = "Tudo tranquilo por aqui.", Durations = new[] { 125, 125, 125, 125, 125, 125, 125, 125, 125, 125, 125, 250 } },
            ["ocupado"] = new StateInfo { Name = "Ocupado (CPU > 60%)", Loop = true, Chip = "OCUPADO", Phrase = "Trabalhando pesado! Segura aí.", Durations = new[] { 100, 100, 100, 100, 100, 100, 100, 100 } },
            ["quente"] = new StateInfo { Name = "Quente (> 80°)", Loop = true, Chip = "COM CALOR", Phrase = "Ufa, que calor… liga um ventinho?", Durations = new[] { 125, 125, 125, 125, 125, 125, 125, 125 } },
            ["dormindo"] = new StateInfo { Name = "Dormindo", Loop = true, Chip = "DORMINDO", Phrase = "Zzz… me acorda quando voltar.", Durations = new[] { 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250 } },
            ["jogando"] = new StateInfo { Name = "Jogando", Loop = true, Chip = "JOGANDO", Phrase = "Bora! Essa partida é nossa.", Durations = new[] { 125, 125, 125, 125, 125, 125, 125, 125 } },
            ["musica"] = new StateInfo { Name = "Ouvindo música", Loop = true, Chip = "CURTINDO", Phrase = "Essa música é boa demais!", Durations = new[] { 125, 125, 125, 125, 125, 125, 125, 125 } },
            ["susto"] = new StateInfo { Name = "Notificação (reação)", Loop = false, Chip = "SURPRESO", Phrase = "Opa! Chegou notificação!", Durations = new[] { 125, 125, 125, 125, 125, 125, 125, 125, 125, 250 } },
            ["comemora"] = new StateInfo { Name = "Download concluído (reação)", Loop = false, Chip = "COMEMORANDO", Phrase = "Download concluído! Uhuu!", Durations = new[] { 125, 125, 125, 125, 125, 125, 125, 125, 125, 125, 125, 250 } },
        };

        // cores do Bit
        private static readonly Brush Body = Pal.B("#6be7c6"), Shade = Pal.B("#39c3a5"), Light = Pal.B("#b5f7e7"),
            Eye = Pal.B("#101418"), Cheek = Pal.B("#ff8ea5"), White = Pal.B("#f7f7f7"),
            HBody = Pal.B("#ff796b"), HShade = Pal.B("#de5142"), HLight = Pal.B("#ffb2a5"), Sweat = Pal.B("#7bc7ff"),
            ShadowBrush = Pal.B("#000000", 0.38);

        private class Pose
        {
            public double Dy, Sx = 1, Sy = 1, Tilt, Look, LookY;
            public string Eyes = "open", Mouth = "smile", Arms = "down";
            public bool Hot, Phones;
            public int Btn;
            public Brush Ant = Pal.B("#42cfef");
            public List<object[]> Fx = new List<object[]>();
        }

        private static Pose MakePose(string state, int f)
        {
            int n = States[state].Durations.Length;
            double s = Math.Sin((double)f / n * Math.PI * 2);
            var p = new Pose();
            switch (state)
            {
                case "feliz":
                case "feliz_coracao":
                    p.Sy = 1 + 0.03 * s; p.Sx = 1 - 0.02 * s;
                    if (state == "feliz")
                    {
                        if (f == 4) p.Eyes = "blink";
                        if (f == 6 || f == 7) p.Look = -4;
                        if (f == 9 || f == 10) p.Look = 4;
                    }
                    else if (f >= 2 && f <= 9)
                    {
                        p.Eyes = "happy";
                        p.Fx.Add(new object[] { "heart", 128 + (f - 2) * 1.5, 52 - (f - 2) * 5.0, Math.Min(1.4, 0.6 + (f - 2) * 0.25) * (f > 8 ? 0.7 : 1) });
                    }
                    break;

                case "ocupado":
                    s = Math.Sin((double)f / n * Math.PI * 4);
                    p.Sy = 1 + 0.04 * s; p.Sx = 1 - 0.025 * s; p.Dy = -2 * Math.Abs(s);
                    p.Eyes = "focus"; p.Mouth = "flat"; p.Arms = f % 2 == 1 ? "tapA" : "tapB";
                    p.Ant = f % 2 == 1 ? Pal.B("#ffc739") : Pal.B("#6b7584");
                    p.Fx.Add(new object[] { "sweat", 132.0, 66.0 + f * 3 });
                    break;

                case "quente":
                    s = Math.Sin((double)f / n * Math.PI * 4);
                    p.Sy = 1 + 0.05 * s; p.Sx = 1 - 0.03 * s;
                    p.Hot = true; p.Eyes = "half"; p.Mouth = f % 2 == 1 ? "pant" : "pant2"; p.Ant = Pal.B("#ff595a");
                    p.Fx.Add(new object[] { "heat", (double)f });
                    break;

                case "dormindo":
                    p.Sy = 0.96 + 0.04 * s; p.Sx = 1.04 - 0.02 * s;
                    p.Eyes = "closed"; p.Mouth = "sleep"; p.Ant = Pal.B("#6b7584");
                    for (int i = 0; i < 3; i++)
                    {
                        int ph = (f + i * 4) % 12;
                        p.Fx.Add(new object[] { "z", 122 + ph * 2.2, 62 - ph * 4.0, 3.5 + ph * 0.55 });
                    }
                    break;

                case "jogando":
                    double b = Math.Abs(Math.Sin((double)f / n * Math.PI * 2));
                    p.Dy = -4 * b; p.Sy = 1 - 0.03 * b; p.Eyes = f == 5 ? "happy" : "open"; p.LookY = 2; p.Look = f < 4 ? -1 : 1;
                    p.Mouth = "grin"; p.Arms = "controller"; p.Ant = Pal.B("#a569ff"); p.Phones = true; p.Btn = f % 2;
                    break;

                case "musica":
                    double w = Math.Sin((double)f / n * Math.PI * 2);
                    p.Tilt = 0.13 * w; p.Dy = -3 * Math.Abs(Math.Sin((double)f / n * Math.PI * 4)); p.Eyes = "happy"; p.Mouth = "smile";
                    p.Arms = w > 0 ? "swayR" : "swayL"; p.Ant = Pal.B("#ff699c");
                    p.Fx.Add(new object[] { "note", 34.0 - (f % 4), 70.0 - f * 4, Pal.B("#ff699c"), false });
                    p.Fx.Add(new object[] { "note", 136.0 + (f % 3), 56.0 - ((f + 4) % 8) * 4, Pal.B("#42cfef"), true });
                    break;

                case "susto":
                    double[] dys = { 0, -10, -16, -12, -4, 0, 0, 0, 0, 0 }, sys = { 0.9, 1.06, 1.05, 1.02, 1, 0.88, 1.04, 0.98, 1, 1 };
                    p.Dy = dys[f]; p.Sy = sys[f]; p.Sx = 2 - sys[f];
                    p.Eyes = f >= 1 && f <= 6 ? "wide" : (f == 0 ? "blink" : "open"); p.Mouth = f >= 1 && f <= 6 ? "o" : "smile";
                    p.Arms = f >= 1 && f <= 4 ? "up" : "down"; p.Ant = Pal.B("#ffc739");
                    if (f >= 1 && f <= 7) p.Fx.Add(new object[] { "bang", 140.0, 40.0 + (f < 3 ? 6 - f * 3 : 0) });
                    break;

                case "comemora":
                    double[] dyc = { 0, -6, -16, -20, -14, -2, 0, -10, -16, -10, -2, 0 }, syc = { 0.9, 1.05, 1.06, 1.04, 1.02, 1, 0.9, 1.05, 1.04, 1.02, 0.94, 1 };
                    p.Dy = dyc[f]; p.Sy = syc[f]; p.Sx = 2 - syc[f];
                    p.Eyes = "happy"; p.Mouth = "grin"; p.Arms = f == 0 || f == 6 || f == 11 ? "down" : "up"; p.Ant = Pal.B("#39db84");
                    p.Fx.Add(new object[] { "confetti", (double)f });
                    break;
            }
            return p;
        }

        // ---------------- primitivas ----------------

        private static void Ell(DrawingContext dc, double x, double y, double rx, double ry, double rot, Brush fill)
        {
            if (rot != 0) dc.PushTransform(new RotateTransform(rot * 180 / Math.PI, x, y));
            dc.DrawEllipse(fill, null, new Point(x, y), rx, ry);
            if (rot != 0) dc.Pop();
        }

        private static Pen RoundPen(Brush b, double w) =>
            new Pen(b, w) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };

        /// <summary>Linha reta (4 números) ou curva quadrática (6 números).</summary>
        private static void Line(DrawingContext dc, double[] p, Brush color, double w)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(p[0], p[1]), false, false);
                if (p.Length == 4) c.LineTo(new Point(p[2], p[3]), true, true);
                else c.QuadraticBezierTo(new Point(p[2], p[3]), new Point(p[4], p[5]), true, true);
            }
            dc.DrawGeometry(null, RoundPen(color, w), g);
        }

        /// <summary>Superelipse: topo mais redondo, base mais reta.</summary>
        private static Geometry Sq(double cx, double cy, double a, double b)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                for (int i = 0; i <= 72; i++)
                {
                    double t = i / 72.0 * Math.PI * 2, co = Math.Cos(t), si = Math.Sin(t);
                    double n = si > 0 ? 3.6 : 2.4;
                    double x = cx + a * Math.Sign(co) * Math.Pow(Math.Abs(co), 2 / n);
                    double y = cy + b * Math.Sign(si) * Math.Pow(Math.Abs(si), 2 / n);
                    if (i == 0) c.BeginFigure(new Point(x, y), true, true);
                    else c.LineTo(new Point(x, y), true, true);
                }
            }
            return g;
        }

        private static void Heart(DrawingContext dc, double x, double y, double s, Brush color)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(0, 6), true, true);
                c.BezierTo(new Point(-9, 0), new Point(-7, -8), new Point(0, -4), true, true);
                c.BezierTo(new Point(7, -8), new Point(9, 0), new Point(0, 6), true, true);
            }
            dc.PushTransform(new TranslateTransform(x, y));
            dc.PushTransform(new ScaleTransform(s, s));
            dc.DrawGeometry(color, null, g);
            dc.Pop(); dc.Pop();
        }

        private static void Note(DrawingContext dc, double x, double y, Brush color, bool dbl)
        {
            Ell(dc, x, y, 4.5, 3.5, -0.4, color);
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(x + 4, y), false, false);
                c.LineTo(new Point(x + 4, y - 15), true, true);
                if (dbl)
                {
                    c.BeginFigure(new Point(x + 15, y - 3), false, false);
                    c.LineTo(new Point(x + 15, y - 18), true, true);
                    c.BeginFigure(new Point(x + 4, y - 15), false, false);
                    c.LineTo(new Point(x + 15, y - 18), true, true);
                }
                else
                {
                    c.BeginFigure(new Point(x + 4, y - 15), false, false);
                    c.QuadraticBezierTo(new Point(x + 10, y - 12), new Point(x + 9, y - 6), true, true);
                }
            }
            if (dbl) Ell(dc, x + 11, y - 3, 4.5, 3.5, -0.4, color);
            dc.DrawGeometry(null, RoundPen(color, 2.5), g);
        }

        private static void Zed(DrawingContext dc, double x, double y, double s, Brush color)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(x - s, y - s), false, false);
                c.LineTo(new Point(x + s, y - s), true, true);
                c.LineTo(new Point(x - s, y + s), true, true);
                c.LineTo(new Point(x + s, y + s), true, true);
            }
            dc.DrawGeometry(null, RoundPen(color, Math.Max(2, s * 0.45)), g);
        }

        /// <summary>Arco de elipse entre dois ângulos (em radianos, sentido horário como no canvas).</summary>
        private static Geometry EllipseArc(double cx, double cy, double rx, double ry, double a0, double a1, bool close)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                var p0 = new Point(cx + rx * Math.Cos(a0), cy + ry * Math.Sin(a0));
                var p1 = new Point(cx + rx * Math.Cos(a1), cy + ry * Math.Sin(a1));
                c.BeginFigure(p0, close, close);
                c.ArcTo(p1, new Size(rx, ry), 0, (a1 - a0) > Math.PI, SweepDirection.Clockwise, true, true);
            }
            return g;
        }

        // ---------------- desenho ----------------

        /// <summary>Desenha o Bit no estado e quadro indicados, com origem em (0,0) e 180×180.</summary>
        public static void Draw(DrawingContext dc, string state, int f)
        {
            if (!States.ContainsKey(state)) state = "feliz";
            f = Math.Clamp(f, 0, States[state].Durations.Length - 1);
            var p = MakePose(state, f);

            double G = 152, A = 52 * p.Sx, Bh = 46 * p.Sy;
            double cx = 90, cy = G - Bh + p.Dy;
            var body = p.Hot ? HBody : Body;
            var shade = p.Hot ? HShade : Shade;
            var light = p.Hot ? HLight : Light;

            // sombra no chão (encolhe no pulo)
            double k = Math.Max(0.45, 1 + p.Dy / 40);
            Ell(dc, 90, G + 4, 42 * k, 6 * k, 0, ShadowBrush);

            dc.PushTransform(new RotateTransform(p.Tilt * 180 / Math.PI, 90, G + p.Dy));

            // pés
            Ell(dc, cx - 21, G + p.Dy - 3, 12, 6.5, 0, shade);
            Ell(dc, cx + 21, G + p.Dy - 3, 12, 6.5, 0, shade);

            // braços atrás do corpo
            double[][] arms = p.Arms switch
            {
                "down" => new[] { new[] { -A + 2, 12, 0.45 }, new[] { A - 2, 12, -0.45 } },
                "tapA" => new[] { new[] { -A + 4, 18, 0.9 }, new[] { A - 4, 14, -0.6 } },
                "tapB" => new[] { new[] { -A + 4, 14, 0.6 }, new[] { A - 4, 18, -0.9 } },
                "up" => new[] { new[] { -A - 6, -Bh * 0.35, -0.5 }, new[] { A + 6, -Bh * 0.35, 0.5 } },
                "swayL" => new[] { new[] { -A - 2, -4, -0.3 }, new[] { A - 2, 14, -0.5 } },
                "swayR" => new[] { new[] { -A + 2, 14, 0.5 }, new[] { A + 2, -4, 0.3 } },
                _ => null
            };
            if (arms != null)
                foreach (var a in arms) Ell(dc, cx + a[0], cy + a[1], 8, 12, a[2], shade);

            // antena
            Line(dc, new[] { cx, cy - Bh + 4, cx + 2, cy - Bh - 6, cx + 6, cy - Bh - 13 }, shade, 3.5);
            Ell(dc, cx + 6, cy - Bh - 15, 5.5, 5.5, 0, p.Ant);

            // corpo: sombra + corpo + brilho
            dc.DrawGeometry(shade, null, Sq(cx, cy, A, Bh));
            dc.DrawGeometry(body, null, Sq(cx, cy - 4, A - 3, Bh - 3));
            Ell(dc, cx - A * 0.48, cy - Bh * 0.56, 10, 5.5, -0.55, light);
            Ell(dc, cx - A * 0.22, cy - Bh * 0.74, 3, 2.4, 0, light);

            // fones (jogando): arco por cima
            if (p.Phones)
                dc.DrawGeometry(null, RoundPen(Pal.B("#293442"), 7), EllipseArc(cx, cy - 2, A + 1, Bh + 5, Math.PI * 1.08, Math.PI * 1.92, false));

            double ex = 17, ey = cy - 6 + p.LookY, L = p.Look;
            // bochechas
            var cheek = p.Hot ? Pal.B("#ff4f4f") : Cheek;
            Ell(dc, cx - 31 + L * 0.3, ey + 11, 7, 4.2, 0, cheek);
            Ell(dc, cx + 31 + L * 0.3, ey + 11, 7, 4.2, 0, cheek);

            // olhos
            foreach (int sd in new[] { -1, 1 })
            {
                double x = cx + sd * ex + L, y = ey;
                switch (p.Eyes)
                {
                    case "open": Ell(dc, x, y, 6.5, 8.5, 0, Eye); Ell(dc, x - 2 + L * 0.2, y - 3, 2.4, 2.4, 0, White); break;
                    case "blink": Line(dc, new[] { x - 6, y + 1, x, y + 3, x + 6, y + 1 }, Eye, 3.5); break;
                    case "happy": Line(dc, new[] { x - 6, y + 3, x, y - 6, x + 6, y + 3 }, Eye, 3.5); break;
                    case "closed": Line(dc, new[] { x - 6, y + 1, x, y + 5, x + 6, y + 1 }, Eye, 3); break;
                    case "wide":
                        Ell(dc, x, y, 8.5, 10, 0, White); Ell(dc, x, y + 1, 4.5, 5, 0, Eye); Ell(dc, x - 1.5, y - 1.5, 1.6, 1.6, 0, White); break;
                    case "focus":
                        Ell(dc, x, y + 1, 6, 5.5, 0, Eye); Ell(dc, x - 2, y - 1, 1.8, 1.8, 0, White);
                        Line(dc, new[] { x - sd * 7, y - 11, x + sd * 5, y - 8 }, Eye, 3); break;
                    case "half":
                        dc.DrawGeometry(Eye, null, EllipseArc(x, y, 6.5, 7.5, 0, Math.PI, true));
                        Line(dc, new[] { x - 7, y - 1, x + 7, y - 1 }, Eye, 3); break;
                }
            }

            // boca
            double mx = cx + L * 0.4, my = ey + 14;
            switch (p.Mouth)
            {
                case "smile": Line(dc, new[] { mx - 5, my, mx, my + 5, mx + 5, my }, Eye, 3); break;
                case "flat": Line(dc, new[] { mx - 4, my + 2, mx + 4, my + 1 }, Eye, 3); break;
                case "o": Ell(dc, mx, my + 2, 3.8, 4.8, 0, Eye); break;
                case "sleep": Ell(dc, mx, my + 2, 2.2, 2.6, 0, Eye); break;
                case "grin":
                    var g = new StreamGeometry();
                    using (var c = g.Open())
                    {
                        c.BeginFigure(new Point(mx - 8, my - 1), true, true);
                        c.QuadraticBezierTo(new Point(mx, my + 13), new Point(mx + 8, my - 1), true, true);
                    }
                    dc.DrawGeometry(Eye, null, g);
                    Ell(dc, mx, my + 5, 4, 2.2, 0, Cheek);
                    break;
                case "pant":
                case "pant2":
                    double o = p.Mouth == "pant" ? 6 : 4.5;
                    Ell(dc, mx, my + 2, 6, o, 0, Eye);
                    Ell(dc, mx + 1, my + 3 + o * 0.6, 4, 3.6, 0, Pal.B("#ff8ea5"));
                    break;
            }

            // controle (jogando) + mãos
            if (p.Arms == "controller")
            {
                double gy = cy + Bh * 0.52;
                dc.DrawRoundedRectangle(Pal.B("#293442"), null, new Rect(cx - 29, gy - 11, 58, 22), 11, 11);
                dc.DrawRectangle(Pal.B("#9ca6b5"), null, new Rect(cx - 20, gy - 1.5, 9, 3));
                dc.DrawRectangle(Pal.B("#9ca6b5"), null, new Rect(cx - 17, gy - 4.5, 3, 9));
                Ell(dc, cx + 14, gy - 3, 2.8, 2.8, 0, p.Btn == 1 ? Pal.B("#42cfef") : Pal.B("#212a31"));
                Ell(dc, cx + 20, gy + 2, 2.8, 2.8, 0, p.Btn == 1 ? Pal.B("#212a31") : Pal.B("#ff699c"));
                Ell(dc, cx - 30, gy + 1, 7.5, 8.5, 0.3, shade);
                Ell(dc, cx + 30, gy + 1, 7.5, 8.5, -0.3, shade);
            }
            // fones: conchas
            if (p.Phones)
            {
                foreach (int sd in new[] { -1, 1 })
                {
                    dc.DrawRoundedRectangle(Pal.B("#a569ff"), null, new Rect(cx + sd * (A + 1) - 7, ey - 12, 14, 24), 7, 7);
                    dc.DrawRoundedRectangle(Pal.B("#4a3484"), null, new Rect(cx + sd * (A + 1) - 3, ey - 7, 6, 14), 3, 3);
                }
            }
            dc.Pop(); // rotação

            // efeitos (fora da rotação)
            foreach (var fx in p.Fx)
            {
                switch ((string)fx[0])
                {
                    case "heart": Heart(dc, (double)fx[1], (double)fx[2], (double)fx[3], Pal.B("#ff699c")); break;
                    case "sweat":
                        {
                            double x = (double)fx[1], y = (double)fx[2];
                            var g = new StreamGeometry();
                            using (var c = g.Open())
                            {
                                c.BeginFigure(new Point(x, y - 10), true, true);
                                c.QuadraticBezierTo(new Point(x + 8, y + 1), new Point(x, y + 5), true, true);
                                c.QuadraticBezierTo(new Point(x - 8, y + 1), new Point(x, y - 10), true, true);
                            }
                            dc.DrawGeometry(Sweat, null, g);
                            break;
                        }
                    case "heat":
                        {
                            double ff = (double)fx[1];
                            double[] hxs = { 44, 122, 140 };
                            for (int i = 0; i < 3; i++)
                            {
                                double hx = hxs[i], ph = (ff + i * 3) % 8;
                                var g = new StreamGeometry();
                                using (var c = g.Open())
                                {
                                    for (int j = 0; j <= 16; j++)
                                    {
                                        double yy = 46 - j * 2.2, xx = hx + Math.Sin((j + ph * 2) * 0.8) * 3.5;
                                        if (j == 0) c.BeginFigure(new Point(xx, yy), false, false);
                                        else c.LineTo(new Point(xx, yy), true, true);
                                    }
                                }
                                dc.DrawGeometry(null, RoundPen(Pal.B("#ffa639"), 3), g);
                            }
                            break;
                        }
                    case "z": Zed(dc, (double)fx[1], (double)fx[2], (double)fx[3], Pal.B("#9ca6b5")); break;
                    case "note": Note(dc, (double)fx[1], (double)fx[2], (Brush)fx[3], (bool)fx[4]); break;
                    case "bang":
                        {
                            double x = (double)fx[1], y = (double)fx[2];
                            dc.DrawRoundedRectangle(Pal.B("#ffc739"), null, new Rect(x - 3.5, y - 14, 7, 18), 3.5, 3.5);
                            Ell(dc, x, y + 10, 4, 4, 0, Pal.B("#ffc739"));
                            break;
                        }
                    case "confetti":
                        {
                            double ff = (double)fx[1];
                            string[] cols = { "#42cfef", "#ffc739", "#ff699c", "#a569ff", "#39db84" };
                            for (int c2 = 0; c2 < 10; c2++)
                            {
                                double ang = c2 / 10.0 * Math.PI * 2 + 0.3, r = 74 + ff * 3;
                                double px = 90 + Math.Cos(ang) * r * 0.95, py = 96 + Math.Sin(ang) * r * 0.7 + ff * 1.6;
                                if (px < 6 || px > 174 || py < 6 || py > 170) continue;
                                dc.PushTransform(new TranslateTransform(px, py));
                                dc.PushTransform(new RotateTransform((ang + ff * 0.6) * 180 / Math.PI));
                                dc.DrawRectangle(Pal.B(cols[c2 % 5]), null, new Rect(-4, -2, 8, 4));
                                dc.Pop(); dc.Pop();
                            }
                            break;
                        }
                }
            }
        }

        /// <summary>Marca do Pixo (o Bit simplificado), em 64×64. off = desconectado (cinza, olhos fechados).</summary>
        public static void DrawLogo(DrawingContext dc, double size, bool off)
        {
            double s = size / 64.0;
            dc.PushTransform(new ScaleTransform(s, s));
            var body = Pal.B(off ? "#9ca6b5" : "#6be7c6");
            var shade = Pal.B(off ? "#6b7584" : "#39c3a5");
            var ant = Pal.B(off ? "#6b7584" : "#42cfef");
            dc.DrawGeometry(null, RoundPen(shade, 3.5), Geometry.Parse("M32 13.5V7.5"));
            dc.DrawEllipse(ant, null, new Point(32, 7), 5, 5);
            dc.DrawGeometry(shade, null, Geometry.Parse("M6 36C6 21 18 13 32 13s26 8 26 23c0 14-8 20-26 20S6 50 6 36Z"));
            dc.DrawGeometry(body, null, Geometry.Parse("M8.5 34.5C8.5 21.5 19 15 32 15s23.5 6.5 23.5 19.5C55.5 46 48 51 32 51S8.5 46 8.5 34.5Z"));
            var eye = Pal.B("#101418");
            if (off)
                dc.DrawGeometry(null, RoundPen(eye, 4), Geometry.Parse("M19 33h8M37 33h8"));
            else
            {
                dc.DrawEllipse(eye, null, new Point(23, 33), 4.5, 6);
                dc.DrawEllipse(eye, null, new Point(41, 33), 4.5, 6);
            }
            dc.Pop();
        }
    }
}
