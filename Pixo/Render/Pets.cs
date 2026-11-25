using System;
using System.Collections.Generic;
using System.Linq;

namespace Pixo.Render
{
    /// <summary>
    /// Fred (cachorro) e Dengo (gato) em pixel art — tradução do sistema de design Pixo.
    /// Cada bichinho é desenhado numa grade 36×36; a dupla numa grade 60×36
    /// (Fred em x−1, Dengo em x+23), sobre uma almofada. Na tela, é ampliada 6× (360×216)
    /// sem suavização.
    ///
    /// Este arquivo não usa WPF: só calcula os pixels (ARGB). Assim dá para testar fora do Windows.
    /// </summary>
    public static class Pets
    {
        public const int Grid = 36, DuoW = 60, DuoH = 36, Scale = 6;
        private const int N = Grid, G = 31;

        // ---------------- estados (quadros, durações e textos) ----------------

        public class StateInfo
        {
            public string Name, Phrase, Chip;
            public bool Loop;
            public int[] Durations;
        }

        public static readonly Dictionary<string, StateInfo> States = new Dictionary<string, StateInfo>
        {
            ["feliz"] = new StateInfo { Name = "Feliz / parado", Loop = true, Chip = "DE BOA", Phrase = "{0} e {1} de boa por aqui.", Durations = new[] { 375, 125, 125, 125, 125, 250, 125, 250, 125, 125, 250, 125 } },
            ["feliz_coracao"] = new StateInfo { Name = "Feliz + coração", Loop = false, Chip = "DE BOA", Phrase = "{0} e {1} de boa por aqui.", Durations = new[] { 125, 125, 125, 125, 125, 125, 125, 125, 125, 125, 125, 250 } },
            ["ocupado"] = new StateInfo { Name = "Ocupados (CPU > 60%)", Loop = true, Chip = "OCUPADOS", Phrase = "Os dois tão te ajudando a trabalhar!", Durations = new[] { 100, 100, 100, 100, 100, 100, 100, 100 } },
            ["quente"] = new StateInfo { Name = "Com calor (> 80°)", Loop = true, Chip = "COM CALOR", Phrase = "Tá quente! {0} tá com a língua de fora.", Durations = new[] { 125, 125, 125, 125, 125, 125, 125, 125 } },
            ["dormindo"] = new StateInfo { Name = "Dormindo", Loop = true, Chip = "DORMINDO", Phrase = "Shhh… soneca da dupla.", Durations = new[] { 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250 } },
            ["jogando"] = new StateInfo { Name = "Jogando", Loop = true, Chip = "JOGANDO", Phrase = "Partida em dupla!", Durations = new[] { 125, 125, 125, 125, 125, 125, 125, 125 } },
            ["musica"] = new StateInfo { Name = "Ouvindo música", Loop = true, Chip = "DANÇANDO", Phrase = "Dancinha da dupla!", Durations = new[] { 125, 125, 125, 125, 125, 125, 125, 125 } },
            ["susto"] = new StateInfo { Name = "Notificação (reação)", Loop = false, Chip = "SURPRESOS", Phrase = "Opa! Chegou notificação!", Durations = new[] { 125, 125, 125, 125, 125, 125, 125, 125, 125, 250 } },
            ["comemora"] = new StateInfo { Name = "Download concluído (reação)", Loop = false, Chip = "COMEMORANDO", Phrase = "Download pronto! Festa!", Durations = new[] { 125, 125, 125, 125, 125, 125, 125, 125, 125, 125, 125, 250 } },
        };

        // ---------------- pose (igual à do design) ----------------

        private class Pose
        {
            public double Dy, Sx = 1, Sy = 1, Tilt, Look, LookY;
            public string Eyes = "open", Mouth = "smile", Arms = "down";
            public bool Hot, Phones;
            public int Btn;
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
                    p.Fx.Add(new object[] { "sweat", 132.0, 66.0 + f * 3 });
                    break;
                case "quente":
                    s = Math.Sin((double)f / n * Math.PI * 4);
                    p.Sy = 1 + 0.05 * s; p.Sx = 1 - 0.03 * s;
                    p.Hot = true; p.Eyes = "half"; p.Mouth = f % 2 == 1 ? "pant" : "pant2";
                    p.Fx.Add(new object[] { "heat", (double)f });
                    break;
                case "dormindo":
                    p.Sy = 0.96 + 0.04 * s; p.Sx = 1.04 - 0.02 * s;
                    p.Eyes = "closed"; p.Mouth = "sleep";
                    for (int i = 0; i < 3; i++)
                    {
                        int ph = (f + i * 4) % 12;
                        p.Fx.Add(new object[] { "z", 122 + ph * 2.2, 62 - ph * 4.0, 3.5 + ph * 0.55 });
                    }
                    break;
                case "jogando":
                    double b = Math.Abs(Math.Sin((double)f / n * Math.PI * 2));
                    p.Dy = -4 * b; p.Sy = 1 - 0.03 * b; p.Eyes = f == 5 ? "happy" : "open"; p.LookY = 2; p.Look = f < 4 ? -1 : 1;
                    p.Mouth = "grin"; p.Arms = "controller"; p.Phones = true; p.Btn = f % 2;
                    break;
                case "musica":
                    double w = Math.Sin((double)f / n * Math.PI * 2);
                    p.Tilt = 0.13 * w; p.Dy = -3 * Math.Abs(Math.Sin((double)f / n * Math.PI * 4)); p.Eyes = "happy"; p.Mouth = "smile";
                    p.Arms = w > 0 ? "swayR" : "swayL";
                    p.Fx.Add(new object[] { "note", 34.0 - (f % 4), 70.0 - f * 4, "#ff699c", false });
                    p.Fx.Add(new object[] { "note", 136.0 + (f % 3), 56.0 - ((f + 4) % 8) * 4, "#42cfef", true });
                    break;
                case "susto":
                    double[] dys = { 0, -10, -16, -12, -4, 0, 0, 0, 0, 0 }, sys = { 0.9, 1.06, 1.05, 1.02, 1, 0.88, 1.04, 0.98, 1, 1 };
                    p.Dy = dys[f]; p.Sy = sys[f]; p.Sx = 2 - sys[f];
                    p.Eyes = f >= 1 && f <= 6 ? "wide" : (f == 0 ? "blink" : "open"); p.Mouth = f >= 1 && f <= 6 ? "o" : "smile";
                    p.Arms = f >= 1 && f <= 4 ? "up" : "down";
                    if (f >= 1 && f <= 7) p.Fx.Add(new object[] { "bang", 140.0, 40.0 + (f < 3 ? 6 - f * 3 : 0) });
                    break;
                case "comemora":
                    double[] dyc = { 0, -6, -16, -20, -14, -2, 0, -10, -16, -10, -2, 0 }, syc = { 0.9, 1.05, 1.06, 1.04, 1.02, 1, 0.9, 1.05, 1.04, 1.02, 0.94, 1 };
                    p.Dy = dyc[f]; p.Sy = syc[f]; p.Sx = 2 - syc[f];
                    p.Eyes = "happy"; p.Mouth = "grin"; p.Arms = f == 0 || f == 6 || f == 11 ? "down" : "up";
                    p.Fx.Add(new object[] { "confetti", (double)f });
                    break;
            }
            return p;
        }

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        /// <summary>Mistura duas poses (o que é medida desliza; olhos, boca e braços trocam na metade).</summary>
        private static Pose Blend(Pose a, Pose b, double t, bool wrapped)
        {
            if (t <= 0.001) return a;
            var p = t < 0.5 ? a : b;
            var r = new Pose
            {
                Dy = Lerp(a.Dy, b.Dy, t), Sx = Lerp(a.Sx, b.Sx, t), Sy = Lerp(a.Sy, b.Sy, t),
                Tilt = Lerp(a.Tilt, b.Tilt, t), Look = Lerp(a.Look, b.Look, t), LookY = Lerp(a.LookY, b.LookY, t),
                Eyes = p.Eyes, Mouth = p.Mouth, Arms = p.Arms, Hot = p.Hot, Phones = p.Phones, Btn = p.Btn,
            };
            if (a.Fx.Count == b.Fx.Count && a.Fx.Select(x => x[0]).SequenceEqual(b.Fx.Select(x => x[0])))
            {
                for (int i = 0; i < a.Fx.Count; i++)
                {
                    var fa = a.Fx[i];
                    var fb = b.Fx[i];
                    var kind = (string)fa[0];
                    var item = (object[])fa.Clone();
                    for (int j = 1; j < fa.Length; j++)
                    {
                        if (fa[j] is double da && fb[j] is double db)
                        {
                            if ((kind == "heat" || kind == "confetti") && j == 1)
                                item[j] = da + t;                              // contador de quadro
                            else if (Math.Abs(db - da) > 25 || wrapped && Math.Abs(db - da) > 12)
                                item[j] = t < 0.5 ? da : db;                   // "pulo" do loop
                            else
                                item[j] = Lerp(da, db, t);
                        }
                    }
                    r.Fx.Add(item);
                }
            }
            else r.Fx = p.Fx;
            return r;
        }

        /// <summary>Pose numa posição "entre quadros" (3,4 = 40% do caminho da pose 3 à 4).</summary>
        private static Pose PoseAt(string state, double pos)
        {
            var info = States[state];
            int n = info.Durations.Length;
            pos = Math.Clamp(pos, 0, n - 1 + (info.Loop ? 0.999 : 0));
            int f = (int)Math.Floor(pos);
            double t = pos - f;
            int f2 = info.Loop ? (f + 1) % n : Math.Min(f + 1, n - 1);
            return f2 == f ? MakePose(state, f) : Blend(MakePose(state, f), MakePose(state, f2), t, f2 < f);
        }

        // ---------------- cores ----------------

        private static readonly Dictionary<string, uint> ColorCache = new Dictionary<string, uint>();
        private static uint C(string hex)
        {
            if (hex == null) return 0;
            lock (ColorCache)
            {
                if (!ColorCache.TryGetValue(hex, out var c))
                {
                    c = 0xFF000000u | Convert.ToUInt32(hex.TrimStart('#'), 16);
                    ColorCache[hex] = c;
                }
                return c;
            }
        }

        private static readonly uint EYE = C("#101418"), WHITE = C("#f7f7f7"), CHEEK = C("#ff8ea5");

        // ---------------- os dois bichinhos ----------------

        private class Kind
        {
            public string Name;
            public uint Body, Shade, Light, Line;
            public int W, H;
            public bool Fuzz, Ears, Tall, Tail, FredTail, Pupil, Mustache;
            public uint DogEars, EarTip, Nose, EyeC, Paw;
            public Func<int, int, uint, PaintCtx, uint> Paint;
        }

        private struct PaintCtx { public int Ey, Top, Bottom; public bool Edge; }

        private static readonly Kind Fred = new Kind
        {
            Name = "Fred", Body = C("#efe6d6"), Shade = C("#cbbda4"), Light = C("#ffffff"), Line = C("#4a4238"),
            W = 24, H = 18, Fuzz = true, DogEars = C("#8b929b"), EarTip = C("#5f6670"), FredTail = true, Nose = C("#101418"),
            Paint = (x, y, c, o) =>
            {
                double dx = x + 0.5 - 18;
                double ey = o.Ey;
                if (Sq(x + 0.5 - 14) / 7 + Sq(y + 0.5 - (ey + 1.5)) / 6 <= 1 || Sq(x + 0.5 - 22) / 7 + Sq(y + 0.5 - (ey + 1.5)) / 6 <= 1)
                    return C("#69707a");
                double mdx = dx / 4.6, mdy = (y + 0.5 - (ey + 5.5)) / 3;
                if (mdx * mdx + mdy * mdy <= 1) return y >= ey + 7 ? C("#d6a26a") : C("#ead6b8");
                if (y <= ey - 1) return Math.Abs(dx) < 1.5 && y >= o.Top + 2 ? C("#dcd7ce") : (o.Edge ? C("#878e96") : C("#a8aeb4"));
                return 0;
            }
        };

        private static readonly Kind Dengo = new Kind
        {
            Name = "Dengo", Body = C("#262b33"), Shade = C("#171b21"), Light = C("#3d4550"), Line = C("#6b7584"),
            W = 20, H = 17, Ears = true, Tall = true, Tail = true,
            EyeC = C("#f2c14e"), Pupil = true, Paw = C("#f2f4f7"), Nose = C("#101418"), Mustache = true,
            Paint = (x, y, c, o) =>
            {
                double dx = x + 0.5 - 18;
                uint W = C("#f2f4f7"), Ws = C("#c6ced8");
                if (y >= o.Top + 1 && y <= o.Ey + 4 && Math.Abs(dx) <= 0.9 + Math.Max(0, y - (o.Top + 4)) * 0.3) return W;
                double mdx = dx / 4.4, mdy = (y + 0.5 - (o.Ey + 6)) / 2.6;
                if (mdx * mdx + mdy * mdy <= 1 || (y >= o.Bottom - 3 && Math.Abs(dx) <= 5)) return o.Edge ? Ws : W;
                return 0;
            }
        };

        private static double Sq(double v) => v * v;

        /// <summary>Arredonda como o JavaScript (0,5 sobe), para bater pixel a pixel com o design.</summary>
        private static int Rnd(double v) => (int)Math.Floor(v + 0.5);

        /// <summary>
        /// Desenha um bichinho numa grade 36×36. Devolve o personagem (com contorno), os efeitos
        /// e a largura da sombra.
        /// </summary>
        private static (uint[] chr, uint[] fx, int shadow) DrawPet(Kind K, Pose p, int f, double lean, bool mirrorTilt, char only)
        {
            p.Tilt += lean;
            if (mirrorTilt) p.Tilt = -p.Tilt;
            // efeitos de cada um: o Fred não tem "Zzz" nem a nota dupla; o Dengo não tem a nota simples
            var fxList = only == 'a'
                ? p.Fx.Where(e => !((string)e[0] == "note" && (bool)e[4]) && (string)e[0] != "z").ToList()
                : p.Fx.Where(e => !((string)e[0] == "note" && !(bool)e[4])).ToList();

            uint body = K.Body, shade = K.Shade, light = K.Light, line = K.Line; // a dupla mantém as cores no calor
            var chr = new uint[N * N];
            var fx = new uint[N * N];
            int dy = Rnd(p.Dy / 5);
            int H = K.H + Rnd((p.Sy - 1) * 30), W = K.W + 2 * Rnd((p.Sx - 1) * 15);
            int bottom = 28 + dy, top = bottom - H + 1;
            double L = 18 - W / 2.0, R = 18 + W / 2.0 - 1;
            double tilt = p.Tilt;

            int Sh(double y) => Rnd(tilt * 0.7 * (G - y));
            void Set(uint[] buf, double x, double y, uint c)
            {
                int xi = Rnd(x), yi = Rnd(y);
                if (xi >= 0 && xi < N && yi >= 0 && yi < N) buf[yi * N + xi] = c;
            }
            void Cs(double x, double y, uint c) => Set(chr, x + Sh(y), y, c);
            uint Get(int x, int y) => x < 0 || y < 0 || x >= N || y >= N ? 0 : chr[y * N + x];
            void Stamp(uint[] buf, string[] map, double x, double y, Func<char, uint> cmap, bool shear)
            {
                for (int j = 0; j < map.Length; j++)
                    for (int i = 0; i < map[j].Length; i++)
                    {
                        uint c = cmap(map[j][i]);
                        if (c == 0) continue;
                        if (shear) Cs(x + i, y + j, c); else Set(buf, x + i, y + j, c);
                    }
            }
            Func<char, uint> M(params (char k, uint c)[] pairs) => ch =>
            {
                foreach (var (k, c) in pairs) if (k == ch) return c;
                return 0;
            };

            // máscara do corpo
            double b2 = H / 2.0, cyc = bottom + 1 - b2;
            bool Inside(int x, int y)
            {
                if (y < top || y > bottom) return false;
                double u = (x + 0.5 - 18) / (W / 2.0), v = (y + 0.5 - cyc) / b2;
                double n = v > 0 ? 3.6 : 2.4;
                return Math.Pow(Math.Abs(u), n) + Math.Pow(Math.Abs(v), n) <= 1;
            }

            // braços (atrás)
            int ey = top + Rnd(H * 0.42);
            uint armC = shade;
            var arms = p.Arms switch
            {
                "down" => new[] { (L - 1, ey + 3.0, new[] { "ss", "s." }), (R, ey + 3.0, new[] { "ss", ".s" }) },
                "tapA" => new[] { (L - 1, ey + 4.0, new[] { "ss", "s." }), (R, ey + 3.0, new[] { "ss", ".s" }) },
                "tapB" => new[] { (L - 1, ey + 3.0, new[] { "ss", "s." }), (R, ey + 4.0, new[] { "ss", ".s" }) },
                "up" => new[] { (L - 2, top + 1.0, new[] { "s.", "ss", ".s" }), (R + 1, top + 1.0, new[] { ".s", "ss", "s." }) },
                "swayL" => new[] { (L - 2, ey - 1.0, new[] { "s.", "ss" }), (R, ey + 3.0, new[] { "ss", ".s" }) },
                "swayR" => new[] { (L - 1, ey + 3.0, new[] { "ss", "s." }), (R + 1, ey - 1.0, new[] { ".s", "ss" }) },
                _ => null
            };
            if (arms != null) foreach (var a in arms) Stamp(chr, a.Item3, a.Item1, a.Item2, M(('s', armC)), true);

            // pés
            uint pc = K.Paw != 0 && !p.Hot ? K.Paw : shade;
            Stamp(chr, new[] { "ssss", "ssss" }, 12, bottom + 1, M(('s', pc)), false);
            Stamp(chr, new[] { "ssss", "ssss" }, 20, bottom + 1, M(('s', pc)), false);

            // rabo do gato
            if (K.Tail)
            {
                bool sw = f % 4 >= 2;
                var map = new[] { "....t", sw ? "...st" : "...s.", "..s.", ".s..", "s..." };
                Stamp(chr, map, R + 1, bottom - 6, M(('s', shade), ('t', shade)), false);
            }
            // rabo do cachorro
            if (K.FredTail)
            {
                int wg = f % 2;
                Stamp(chr, new[] { ".bb.", "bbbb", "bb.b", "...b" }, R - 2 + wg, bottom - 10, M(('b', body)), false);
            }
            // orelhas do gato
            if (K.Ears && K.Tall)
            {
                var em = M(('b', body), ('p', C("#c9707e")));
                Stamp(chr, new[] { "b...", "b...", "bb..", "bpb.", "bppb" }, L + 2, top - 4, em, true);
                Stamp(chr, new[] { "...b", "...b", "..bb", ".bpb", "bppb" }, R - 5, top - 4, em, true);
            }

            // corpo
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    if (!Inside(x, y)) continue;
                    uint c = body;
                    bool edge = !Inside(x, y + 2) || (!Inside(x + 1, y) && y > cyc);
                    if (edge) c = shade;
                    if (K.Paint != null)
                    {
                        uint pc2 = K.Paint(x, y, c, new PaintCtx { Ey = ey, Top = top, Bottom = bottom, Edge = edge });
                        if (pc2 != 0) c = pc2;
                    }
                    Cs(x, y, c);
                }
            // brilho
            foreach (var (qx, qy) in new[] { (3, 2), (4, 2), (3, 3) })
            {
                int x = Rnd(L) + qx, y = top + qy;
                if (Inside(x, y) && Inside(x - 1, y)) Cs(x, y, light);
            }
            if (K.Ears) foreach (var (qx, qy) in new[] { (15, 1), (17, 1), (18, 1), (20, 1) }) Cs(qx, top + qy, shade);

            // orelhas do cachorro
            if (K.DogEars != 0)
            {
                uint ec = K.DogEars;
                int flap = p.Dy < -6 ? -1 : 0;
                uint et = K.EarTip != 0 ? K.EarTip : ec;
                var dm = M(('e', ec), ('t', et));
                Stamp(chr, new[] { ".ee", "eee", "eee", "eee", "eee", "ett", ".tt" }, Rnd(L) - 2, top + 1 + flap, dm, true);
                Stamp(chr, new[] { "ee.", "eee", "eee", "eee", "eee", "tte", "tt." }, Rnd(R), top + 1 + flap, dm, true);
            }

            // olhos
            uint faceE = K.EyeC != 0 && !p.Hot ? K.EyeC : EYE;
            uint faceC = CHEEK, faceP = 0;
            Func<char, uint> Face() => ch => ch switch { 'e' => faceE, 'w' => WHITE, 'c' => faceC, 'o' => line, 'p' => faceP, _ => 0u };
            Func<char, uint> Mouth = ch => ch switch { 'e' => EYE, 'w' => WHITE, 'c' => CHEEK, 'o' => line, _ => 0u };

            int lx = Rnd(13 + p.Look / 4), rx = Rnd(21 + p.Look / 4), eY = ey + Rnd(p.LookY / 2);
            (string[] map, int dx, int dy2) E = p.Eyes switch
            {
                "blink" => (new[] { "..", "..", "ee" }, 0, 0),
                "happy" => (new[] { ".e.", "e.e" }, -1, 0),
                "closed" => (new[] { "e.e", ".e." }, -1, 0),
                "wide" => (new[] { "www", "wew", "www" }, -1, 0),
                "focus" => (new[] { "we", "ee" }, 0, 1),
                "half" => (new[] { "oo", "ee", "ee" }, 0, 0),
                _ => (new[] { "we", "ee", "ee" }, 0, 0), // open
            };
            if (K.Pupil && p.Eyes == "open")
            {
                faceP = C("#101418");
                Stamp(chr, new[] { "wp", "ep", "ep" }, lx, eY, Face(), true);
                Stamp(chr, new[] { "pw", "pe", "pe" }, rx, eY, Face(), true);
            }
            else
            {
                Stamp(chr, E.map, lx + E.dx, eY + E.dy2, Face(), true);
                Stamp(chr, E.map, rx, eY + E.dy2, Face(), true);
            }
            if (p.Eyes == "focus")
            {
                Stamp(chr, new[] { "ee.", "..e" }, 12, eY - 2, Face(), true);
                Stamp(chr, new[] { ".ee", "e.." }, 21, eY - 2, Face(), true);
            }

            // bochechas
            if (p.Hot) faceC = C("#ff595a");
            Stamp(chr, new[] { "cc" }, 10, ey + 3, Face(), true);
            Stamp(chr, new[] { "cc" }, 24, ey + 3, Face(), true);

            // boca
            int my = ey + 4 + Rnd(p.LookY / 2), mx = 16 + Rnd(p.Look / 4);
            if (K.Nose != 0) Stamp(chr, new[] { ".nn." }, mx, my - 1, M(('n', K.Nose)), true);
            if (K.Ears && p.Mouth == "smile")
            {
                var sm = new[] { ".cc.", "e..e", ".ee." };
                Stamp(chr, K.Nose != 0 ? sm.Skip(1).ToArray() : sm, mx, my - (K.Nose != 0 ? 0 : 1), Mouth, true);
            }
            else
            {
                string[] mm = p.Mouth switch
                {
                    "flat" => new[] { "....", ".ee." },
                    "o" => new[] { ".ee.", ".ee." },
                    "sleep" => new[] { "....", ".ee." },
                    "grin" => new[] { "eeee", "ecce", ".ee." },
                    "pant" => new[] { ".ee.", ".ee.", ".cc." },
                    "pant2" => new[] { ".ee.", ".cc." },
                    _ => new[] { "e..e", ".ee." }, // smile
                };
                Stamp(chr, mm, mx, my, Mouth, true);
            }
            if (K.Mustache) Stamp(chr, new[] { "kk", "k." }, mx - 1, my - 1, M(('k', C("#101418"))), true);

            // controle e fones (jogando)
            if (p.Arms == "controller")
            {
                int gy = bottom - 4;
                Stamp(chr, new[] { ".kkkkkkkkkk.", "kkzkkkkkkxkk", "kzzzkkkkykkk", "kkzkkkkkkkkk", ".kk......kk." }, 12, gy,
                    M(('k', C("#293442")), ('z', C("#9ca6b5")), ('x', p.Btn == 1 ? C("#42cfef") : C("#212a31")), ('y', p.Btn == 1 ? C("#212a31") : C("#ff699c"))), true);
                Stamp(chr, new[] { "ss", "ss" }, 10, gy + 1, M(('s', armC)), true);
                Stamp(chr, new[] { "ss", "ss" }, 24, gy + 1, M(('s', armC)), true);
            }
            if (p.Phones)
            {
                uint hb = C("#293442");
                for (int xh = 12; xh <= 23; xh++) Cs(xh, top - 2, hb);
                Cs(11, top - 1, hb); Cs(24, top - 1, hb); Cs(10, top, hb); Cs(25, top, hb);
                var vm = M(('v', C("#a569ff")), ('k', C("#4a3484")));
                Stamp(chr, new[] { "vvv", "vkv", "vkv", "vvv" }, Rnd(L) - 2, ey - 2, vm, true);
                Stamp(chr, new[] { "vvv", "vkv", "vkv", "vvv" }, Rnd(R), ey - 2, vm, true);
            }

            // pelo (franja na borda de cima do cachorro)
            if (K.Fuzz)
            {
                for (int yf = top; yf < cyc + 2; yf++)
                    for (int xf = 0; xf < N; xf++)
                    {
                        if (yf < 0 || yf >= N) continue;
                        if (chr[yf * N + xf] != 0 || !(Get(xf + 1, yf) != 0 || Get(xf - 1, yf) != 0 || Get(xf, yf + 1) != 0)) continue;
                        if ((xf * 7 + yf * 3) % 5 == 0)
                        {
                            uint v = Get(xf, yf + 1);
                            if (v == 0) v = Get(xf + 1, yf);
                            if (v == 0) v = Get(xf - 1, yf);
                            chr[yf * N + xf] = v;
                        }
                    }
            }

            // contorno de 1 pixel
            var outBuf = (uint[])chr.Clone();
            for (int y2 = 0; y2 < N; y2++)
                for (int x2 = 0; x2 < N; x2++)
                {
                    if (chr[y2 * N + x2] != 0) continue;
                    if (Get(x2 - 1, y2) != 0 || Get(x2 + 1, y2) != 0 || Get(x2, y2 - 1) != 0 || Get(x2, y2 + 1) != 0)
                        outBuf[y2 * N + x2] = line;
                }

            // efeitos
            int X(double v) => Rnd(v / 5);
            foreach (var e in fxList)
            {
                switch ((string)e[0])
                {
                    case "heart":
                        Stamp(fx, (double)e[3] > 1.1 ? new[] { "pp.pp", "ppppp", "ppppp", ".ppp.", "..p.." } : new[] { "p.p", "ppp", ".p." },
                            X((double)e[1]) - 2, X((double)e[2]) - 2, M(('p', C("#ff699c"))), false);
                        break;
                    case "sweat":
                        Stamp(fx, new[] { ".d.", "ddd", "dwd", ".d." }, X((double)e[1]), X((double)e[2]) - 2, M(('d', C("#7bc7ff")), ('w', C("#d6efff"))), false);
                        break;
                    case "heat":
                        {
                            int ef = (int)Math.Floor((double)e[1]);
                            int[] hxs = { 9, 24, 28 }, off = { 0, 1, 1, 0, -1, -1 };
                            for (int i = 0; i < 3; i++)
                                for (int j = 0; j < 7; j++)
                                    Set(fx, hxs[i] + off[(j + ef + i * 2) % 6], 9 - j + (i == 0 ? 1 : 0), C("#ffa639"));
                            break;
                        }
                    case "z":
                        {
                            double size = (double)e[3];
                            var zs = size < 5 ? new[] { "zzz", ".z.", "zzz" }
                                : size < 7.5 ? new[] { "zzzz", "..z.", ".z..", "zzzz" }
                                : new[] { "zzzzz", "...z.", "..z..", ".z...", "zzzzz" };
                            Stamp(fx, zs, X((double)e[1]), X((double)e[2]) - 2, M(('z', C("#9ca6b5"))), false);
                            break;
                        }
                    case "note":
                        Stamp(fx, (bool)e[4] ? new[] { "..nnn", "..n.n", "..n.n", "nnnnn", "nn.nn" } : new[] { "..nn", "..n.", "..n.", "nnn.", "nn.." },
                            X((double)e[1]), X((double)e[2]) - 4, M(('n', C((string)e[3]))), false);
                        break;
                    case "bang":
                        Stamp(fx, new[] { "yy", "yy", "yy", "yy", "..", "yy" }, X((double)e[1]) - 1, X((double)e[2]) - 3, M(('y', C("#ffc739"))), false);
                        break;
                    case "confetti":
                        {
                            double ev = (double)e[1];
                            int ef = (int)Math.Floor(ev);
                            string[] cols = { "#42cfef", "#ffc739", "#ff699c", "#a569ff", "#39db84" };
                            for (int c2 = 0; c2 < 10; c2++)
                            {
                                double ang = c2 / 10.0 * Math.PI * 2 + 0.3, r = 74 + ev * 3;
                                int px = X(90 + Math.Cos(ang) * r * 0.95), py = X(96 + Math.Sin(ang) * r * 0.7 + ev * 1.6);
                                if (px < 1 || px > 34 || py < 1 || py > 33 || Get(px, py) != 0) continue;
                                Set(fx, px, py, C(cols[c2 % 5]));
                                Set(fx, px + ((c2 + ef) % 2), py + ((c2 + ef + 1) % 2), C(cols[c2 % 5]));
                            }
                            break;
                        }
                }
            }

            // sombra no chão (encolhe no pulo)
            double k = Math.Max(0.5, 1 + p.Dy / 40);
            int sw2 = Rnd(9 * k);

            // personagem por cima dos efeitos? no design, efeitos vêm depois (por cima)
            return (outBuf, fx, sw2);
        }

        // ---------------- a dupla ----------------

        private static readonly Dictionary<string, int> Phase = new Dictionary<string, int>
        {
            ["feliz"] = 6, ["ocupado"] = 3, ["quente"] = 2, ["dormindo"] = 6, ["jogando"] = 4, ["musica"] = 0, ["susto"] = 1, ["comemora"] = 3
        };

        /// <summary>
        /// Desenha a dupla (60×36, ARGB com transparência) na posição de animação "pos"
        /// (pode ter fração, para a animação ficar fluida).
        /// </summary>
        public static uint[] RenderDuo(string state, double pos, bool cushion = true)
        {
            if (!States.ContainsKey(state)) state = "feliz";
            var info = States[state];
            int n = info.Durations.Length;

            // imagem em cor "pré-multiplicada" para misturar a sombra semitransparente
            var buf = new double[DuoW * DuoH * 4];

            if (cushion)
            {
                var rows = new (int y, int x0, int x1, string c)[]
                {
                    (31, 6, 53, "#3b4354"), (32, 4, 55, "#2b3140"), (33, 3, 56, "#2b3140"), (34, 3, 56, "#232834"), (35, 5, 54, "#1b1f28")
                };
                foreach (var r in rows) FillOpaque(buf, r.x0, r.y, r.x1 - r.x0 + 1, 1, C(r.c));
                foreach (var x in new[] { 10, 17, 24, 31, 38, 45, 52 }) FillOpaque(buf, x, 31, 1, 1, C("#4a5468"));
            }

            int ph = Phase.TryGetValue(state, out var v) ? v : 0;
            double posA = pos;
            double posB = info.Loop ? (pos + ph) % n : Math.Max(0, pos - ph);
            double[] lean = state == "dormindo" ? new[] { 0.06, -0.06 } : new[] { 0.0, 0.0 };

            DrawInto(buf, Fred, PoseAt(state, posA), (int)Math.Floor(posA), lean[0], false, 'a', -1);
            DrawInto(buf, Dengo, PoseAt(state, posB), (int)Math.Floor(posB), lean[1], state == "musica", 'b', 23);

            // de volta para ARGB "normal"
            var outArgb = new uint[DuoW * DuoH];
            for (int i = 0; i < outArgb.Length; i++)
            {
                double a = buf[i * 4 + 3];
                if (a <= 0) continue;
                uint r = (uint)Math.Round(buf[i * 4] / a), g = (uint)Math.Round(buf[i * 4 + 1] / a), b = (uint)Math.Round(buf[i * 4 + 2] / a);
                uint A = (uint)Math.Round(a * 255);
                outArgb[i] = (A << 24) | (Math.Min(r, 255u) << 16) | (Math.Min(g, 255u) << 8) | Math.Min(b, 255u);
            }
            return outArgb;
        }

        private static void DrawInto(double[] duo, Kind kind, Pose p, int f, double lean, bool mirror, char only, int offsetX)
        {
            var (chr, fx, sw2) = DrawPet(kind, p, f, lean, mirror, only);

            // camada do bichinho (36×36), como o canvas temporário do design
            var tmp = new double[N * N * 4];
            // sombra: preto com 38% de opacidade
            for (int x = 18 - sw2; x < 18 + sw2; x++) BlendInto(tmp, N, x, G + 1, 0, 0, 0, 0.38);
            for (int x = 18 - sw2 + 2; x < 18 - sw2 + 2 + (sw2 - 2) * 2; x++) BlendInto(tmp, N, x, G + 2, 0, 0, 0, 0.38);
            // personagem, depois efeitos (opacos)
            foreach (var layer in new[] { chr, fx })
                for (int i = 0; i < N * N; i++)
                    if (layer[i] != 0)
                    {
                        uint c = layer[i];
                        BlendInto(tmp, N, i % N, i / N, (c >> 16) & 255, (c >> 8) & 255, c & 255, 1);
                    }

            // cola na dupla, deslocado
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int k = (y * N + x) * 4;
                    double a = tmp[k + 3];
                    if (a <= 0) continue;
                    int dx = x + offsetX;
                    if (dx < 0 || dx >= DuoW || y >= DuoH) continue;
                    BlendInto(duo, DuoW, dx, y, tmp[k] / a, tmp[k + 1] / a, tmp[k + 2] / a, a);
                }
        }

        private static void FillOpaque(double[] buf, int x, int y, int w, int h, uint c)
        {
            for (int j = y; j < y + h; j++)
                for (int i = x; i < x + w; i++)
                    BlendInto(buf, DuoW, i, j, (c >> 16) & 255, (c >> 8) & 255, c & 255, 1);
        }

        /// <summary>"Por cima" (source over) em cor pré-multiplicada.</summary>
        private static void BlendInto(double[] buf, int w, int x, int y, double r, double g, double b, double a)
        {
            int h = buf.Length / 4 / w;
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            int k = (y * w + x) * 4;
            double ia = 1 - a;
            buf[k] = r * a + buf[k] * ia;
            buf[k + 1] = g * a + buf[k + 1] * ia;
            buf[k + 2] = b * a + buf[k + 2] * ia;
            buf[k + 3] = a + buf[k + 3] * ia;
        }
    }
}
