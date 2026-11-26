using System;
using System.Collections.Generic;
using System.Linq;

namespace Pixo.Render
{
    /// <summary>
    /// Fred (cachorro) e Dengo (gato) em pixel art — tradução do sistema de design Pixo.
    /// Cada bichinho é desenhado numa grade 36×36 (a dupla ocupa 60×36: Fred em x−1, Dengo em x+23),
    /// sobre uma almofada, e aparece ampliado 6× (360×216). A arte fica sempre em blocos de 6 px;
    /// o pulo, o esticar, a inclinação e os efeitos andam pixel a pixel (RenderDuoScreen).
    ///
    /// Este arquivo não usa WPF: só calcula os pixels (ARGB). Assim dá para testar fora do Windows.
    /// </summary>
    public static class Pets
    {
        public const int Grid = 36, DuoW = 60, DuoH = 36;

        /// <summary>Ampliação da arte na tela (6 = 360×216). Variável só para o laboratório comparar tamanhos.</summary>
        public static int Scale = 6;

        /// <summary>
        /// Reações (susto e comemoração) mais contidas que no design: o pulo do corpo inteiro mudava quase todos
        /// os pixels de uma vez, e a tela (que só recebe ~166 KB/s) levava ~0,3 s para pintar cada quadro: dava
        /// para ver uma "linha" descendo. Testado na tela física, subindo de pouco em pouco: sem alargar de lado,
        /// esticando 1/4 e com o pulo a 0,24 do design ainda não aparece linha (a 0,3 já aparece). (1 = como no design.)
        /// </summary>
        public static double ReactionJump = 0.24, ReactionSquashX = 0, ReactionSquashY = 0.25;
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

        /// <summary>
        /// Pose num instante da animação. "fp" pode ter fração (3,4 = 40% do caminho da pose 3 à 4).
        /// Tudo que é movimento (pulo, respiração, inclinação, efeitos) é calculado no instante exato;
        /// olhos, boca e braços trocam a cada quadro, como no design.
        /// </summary>
        private static Pose MakePose(string state, double fp)
        {
            var info = States[state];
            int n = info.Durations.Length;
            fp = Math.Clamp(fp, 0, info.Loop ? n - 1e-6 : n - 1);
            int f = (int)Math.Floor(fp);
            double s = Math.Sin(fp / n * Math.PI * 2);
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
                        double h = fp - 2; // sobe sem parar; o tamanho troca por quadro
                        p.Fx.Add(new object[] { "heart", 128 + h * 1.5, 52 - h * 5.0, Math.Min(1.4, 0.6 + (f - 2) * 0.25) * (f > 8 ? 0.7 : 1) });
                    }
                    break;
                case "ocupado":
                    s = Math.Sin(fp / n * Math.PI * 4);
                    p.Sy = 1 + 0.04 * s; p.Sx = 1 - 0.025 * s; p.Dy = -2 * Math.Abs(s);
                    p.Eyes = "focus"; p.Mouth = "flat"; p.Arms = f % 2 == 1 ? "tapA" : "tapB";
                    p.Fx.Add(new object[] { "sweat", 132.0, 66.0 + fp * 3 });
                    break;
                case "quente":
                    s = Math.Sin(fp / n * Math.PI * 4);
                    p.Sy = 1 + 0.05 * s; p.Sx = 1 - 0.03 * s;
                    p.Hot = true; p.Eyes = "half"; p.Mouth = f % 2 == 1 ? "pant" : "pant2";
                    p.Fx.Add(new object[] { "heat", (double)f });
                    break;
                case "dormindo":
                    p.Sy = 0.96 + 0.04 * s; p.Sx = 1.04 - 0.02 * s;
                    p.Eyes = "closed"; p.Mouth = "sleep";
                    for (int i = 0; i < 3; i++)
                    {
                        double ph = (fp + i * 4) % 12; // cada "z" sobe devagar e cresce por etapas
                        p.Fx.Add(new object[] { "z", 122 + ph * 2.2, 62 - ph * 4.0, 3.5 + Math.Floor(ph) * 0.55 });
                    }
                    break;
                case "jogando":
                    double b = Math.Abs(Math.Sin(fp / n * Math.PI * 2));
                    p.Dy = -4 * b; p.Sy = 1 - 0.03 * b; p.Eyes = f == 5 ? "happy" : "open"; p.LookY = 2; p.Look = f < 4 ? -1 : 1;
                    p.Mouth = "grin"; p.Arms = "controller"; p.Phones = true; p.Btn = f % 2;
                    break;
                case "musica":
                    double w = Math.Sin(fp / n * Math.PI * 2);
                    p.Tilt = 0.13 * w; p.Dy = -3 * Math.Abs(Math.Sin(fp / n * Math.PI * 4)); p.Eyes = "happy"; p.Mouth = "smile";
                    p.Arms = Math.Sin((double)f / n * Math.PI * 2) > 0 ? "swayR" : "swayL";
                    p.Fx.Add(new object[] { "note", 34.0 - (f % 4), 70.0 - fp * 4, "#ff699c", false });
                    p.Fx.Add(new object[] { "note", 136.0 + (f % 3), 56.0 - ((fp + 4) % 8) * 4, "#42cfef", true });
                    break;
                case "susto":
                    double[] dys = { 0, -10, -16, -12, -4, 0, 0, 0, 0, 0 }, sys = { 0.9, 1.06, 1.05, 1.02, 1, 0.88, 1.04, 0.98, 1, 1 };
                    p.Dy = Curve(dys, fp) * ReactionJump;
                    p.Sy = 1 + (Curve(sys, fp) - 1) * ReactionSquashY; p.Sx = 1 + (1 - Curve(sys, fp)) * ReactionSquashX;
                    p.Eyes = f >= 1 && f <= 6 ? "wide" : (f == 0 ? "blink" : "open"); p.Mouth = f >= 1 && f <= 6 ? "o" : "smile";
                    p.Arms = f >= 1 && f <= 4 ? "up" : "down";
                    if (f >= 1 && f <= 7) p.Fx.Add(new object[] { "bang", 140.0, 40.0 + Math.Max(0, 6 - fp * 3) });
                    break;
                case "comemora":
                    double[] dyc = { 0, -6, -16, -20, -14, -2, 0, -10, -16, -10, -2, 0 }, syc = { 0.9, 1.05, 1.06, 1.04, 1.02, 1, 0.9, 1.05, 1.04, 1.02, 0.94, 1 };
                    p.Dy = Curve(dyc, fp) * ReactionJump;
                    p.Sy = 1 + (Curve(syc, fp) - 1) * ReactionSquashY; p.Sx = 1 + (1 - Curve(syc, fp)) * ReactionSquashX;
                    p.Eyes = "happy"; p.Mouth = "grin"; p.Arms = f == 0 || f == 6 || f == 11 ? "down" : "up";
                    p.Fx.Add(new object[] { "confetti", fp });
                    break;
            }
            return p;
        }

        /// <summary>Curva suave (Catmull-Rom) passando pelos valores da tabela: pulos com arco natural.</summary>
        private static double Curve(double[] v, double t)
        {
            int last = v.Length - 1;
            int i = Math.Clamp((int)Math.Floor(t), 0, last);
            if (i >= last) return v[last];
            double u = t - i;
            double p0 = v[Math.Max(0, i - 1)], p1 = v[i], p2 = v[i + 1], p3 = v[Math.Min(last, i + 2)];
            return 0.5 * (2 * p1 + (p2 - p0) * u + (2 * p0 - 5 * p1 + 4 * p2 - p3) * u * u + (3 * p1 - p0 - 3 * p2 + p3) * u * u * u);
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

        /// <summary>Um efeito (coração, nota, "z"...) com posição fracionária na grade do bichinho.</summary>
        private struct FxStamp { public string[] Map; public double X, Y; public Func<char, uint> Cm; }
        private static readonly string[] Dot = { "x" };

        /// <summary>
        /// Desenha um bichinho numa grade 36×36, "em pé": sem pulo, sem esticar e sem inclinar
        /// (isso é aplicado depois, pixel a pixel, em DrawPetScreen). Devolve o personagem
        /// (com contorno) e a lista de efeitos.
        /// </summary>
        private static (uint[] chr, List<FxStamp> fx) DrawPet(Kind K, Pose p, int f, char only)
        {
            // efeitos de cada um: o Fred não tem "Zzz" nem a nota dupla; o Dengo não tem a nota simples
            var fxList = only == 'a'
                ? p.Fx.Where(e => !((string)e[0] == "note" && (bool)e[4]) && (string)e[0] != "z").ToList()
                : p.Fx.Where(e => !((string)e[0] == "note" && !(bool)e[4])).ToList();

            uint body = K.Body, shade = K.Shade, light = K.Light, line = K.Line; // a dupla mantém as cores no calor
            var chr = new uint[N * N];
            var fx = new List<FxStamp>();
            int H = K.H, W = K.W;
            int bottom = 28, top = bottom - H + 1;
            double L = 18 - W / 2.0, R = 18 + W / 2.0 - 1;

            void Set(uint[] buf, double x, double y, uint c)
            {
                int xi = Rnd(x), yi = Rnd(y);
                if (xi >= 0 && xi < N && yi >= 0 && yi < N) buf[yi * N + xi] = c;
            }
            void Cs(double x, double y, uint c) => Set(chr, x, y, c);
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

            // efeitos: guardados com posição fracionária; DrawPetScreen desenha cada um em
            // blocos de 6 px que andam de 1 em 1 pixel
            double X(double v) => v / 5;
            void Fx(string[] map, double x, double y, Func<char, uint> cm) => fx.Add(new FxStamp { Map = map, X = x, Y = y, Cm = cm });
            foreach (var e in fxList)
            {
                switch ((string)e[0])
                {
                    case "heart":
                        Fx((double)e[3] > 1.1 ? new[] { "pp.pp", "ppppp", "ppppp", ".ppp.", "..p.." } : new[] { "p.p", "ppp", ".p." },
                            X((double)e[1]) - 2, X((double)e[2]) - 2, M(('p', C("#ff699c"))));
                        break;
                    case "sweat":
                        Fx(new[] { ".d.", "ddd", "dwd", ".d." }, X((double)e[1]), X((double)e[2]) - 2, M(('d', C("#7bc7ff")), ('w', C("#d6efff"))));
                        break;
                    case "heat":
                        {
                            int ef = (int)Math.Floor((double)e[1]);
                            int[] hxs = { 9, 24, 28 }, off = { 0, 1, 1, 0, -1, -1 };
                            var hc = M(('x', C("#ffa639")));
                            for (int i = 0; i < 3; i++)
                                for (int j = 0; j < 7; j++)
                                    Fx(Dot, hxs[i] + off[(j + ef + i * 2) % 6], 9 - j + (i == 0 ? 1 : 0), hc);
                            break;
                        }
                    case "z":
                        {
                            double size = (double)e[3];
                            var zs = size < 5 ? new[] { "zzz", ".z.", "zzz" }
                                : size < 7.5 ? new[] { "zzzz", "..z.", ".z..", "zzzz" }
                                : new[] { "zzzzz", "...z.", "..z..", ".z...", "zzzzz" };
                            Fx(zs, X((double)e[1]), X((double)e[2]) - 2, M(('z', C("#9ca6b5"))));
                            break;
                        }
                    case "note":
                        Fx((bool)e[4] ? new[] { "..nnn", "..n.n", "..n.n", "nnnnn", "nn.nn" } : new[] { "..nn", "..n.", "..n.", "nnn.", "nn.." },
                            X((double)e[1]), X((double)e[2]) - 4, M(('n', C((string)e[3]))));
                        break;
                    case "bang":
                        Fx(new[] { "yy", "yy", "yy", "yy", "..", "yy" }, X((double)e[1]) - 1, X((double)e[2]) - 3, M(('y', C("#ffc739"))));
                        break;
                    case "confetti":
                        {
                            double ev = (double)e[1];
                            int ef = (int)Math.Floor(ev);
                            string[] cols = { "#42cfef", "#ffc739", "#ff699c", "#a569ff", "#39db84" };
                            for (int c2 = 0; c2 < 10; c2++)
                            {
                                double ang = c2 / 10.0 * Math.PI * 2 + 0.3, r = 74 + ev * 3;
                                double gx = X(90 + Math.Cos(ang) * r * 0.95), gy = X(96 + Math.Sin(ang) * r * 0.7 + ev * 1.6);
                                int px = Rnd(gx), py = Rnd(gy);
                                if (px < 1 || px > 34 || py < 1 || py > 33 || Get(px, py) != 0) continue;
                                var cm = M(('x', C(cols[c2 % 5])));
                                Fx(Dot, gx, gy, cm);
                                Fx(Dot, gx + ((c2 + ef) % 2), gy + ((c2 + ef + 1) % 2), cm);
                            }
                            break;
                        }
                }
            }

            return (outBuf, fx);
        }

        // ---------------- a dupla ----------------

        private static readonly Dictionary<string, int> Phase = new Dictionary<string, int>
        {
            ["feliz"] = 6, ["ocupado"] = 3, ["quente"] = 2, ["dormindo"] = 6, ["jogando"] = 4, ["musica"] = 0, ["susto"] = 1, ["comemora"] = 3
        };

        /// <summary>Tamanho da dupla na tela: 360×216 (a grade 60×36 ampliada 6×).</summary>
        public static int ScreenW => DuoW * Scale;
        public static int ScreenH => DuoH * Scale;

        /// <summary>
        /// De quantos em quantos pixels da tela o movimento anda em cada animação (1 = mais liso;
        /// 6 = igual à grade do design). Cada pixel andado custa banda no USB, que na tela real é de
        /// só ~166 KB/s; estes valores foram escolhidos medindo no PixoSim para cada animação caber
        /// no tempo de um quadro (ou chegar o mais perto possível).
        /// </summary>
        private static readonly Dictionary<string, int> Steps = new Dictionary<string, int>
        {
            // movimentos lentos: passo pequeno (liso e barato)
            ["feliz"] = 1, ["feliz_coracao"] = 1, ["susto"] = 1, ["comemora"] = 1, ["dormindo"] = 2, ["jogando"] = 3,
            // movimentos rápidos andam 5–7 px por quadro de qualquer jeito; em blocos inteiros custam
            // metade e sobra banda para mais quadros por segundo
            ["ocupado"] = 6, ["quente"] = 6, ["musica"] = 6,
        };

        /// <summary>Força um passo para todas as animações (só para medir no PixoSim; null = o de cada uma).</summary>
        public static int? StepOverride;

        private static int StepFor(string state) => StepOverride ?? (Steps.TryGetValue(state, out var v) ? v : 1);

        /// <summary>
        /// Como o design arredonda o pulo para blocos inteiros, balanços de menos de meio bloco
        /// nunca aparecem lá. Aqui também não; acima disso o movimento cresce suave até bater
        /// com o design a partir de 1 bloco.
        /// </summary>
        private static double Soft(double g)
        {
            double a = Math.Abs(g);
            return a <= 0.4 ? 0 : Math.Sign(g) * (a >= 1 ? a : (a - 0.4) / 0.6);
        }

        /// <summary>
        /// Desenha a dupla direto na resolução da tela (360×216, ARGB; 0 = transparente) em "dst".
        /// A arte continua em blocos de 6 px, mas pulo, respiração, inclinação, sombra e efeitos
        /// andam de 1 em 1 pixel: o movimento fica fluido e cada quadro muda pouca coisa no USB.
        /// "pos" pode ter fração (3,4 = 40% do caminho da pose 3 à 4).
        /// </summary>
        public static void RenderDuoScreen(string state, double pos, uint[] dst, bool cushion = true)
            => RenderDuoScreen(state, pos, null, 0, 1, dst, cushion);

        /// <summary>
        /// Igual, mas vindo de outro estado ("from", na posição fromPos): mix = 0 é o movimento do
        /// estado antigo, 1 é o do novo. Assim a troca de estado não dá tranco.
        /// </summary>
        public static void RenderDuoScreen(string state, double pos, string from, double fromPos, double mix, uint[] dst, bool cushion = true)
        {
            if (!States.ContainsKey(state)) state = "feliz";
            if (from != null && !States.ContainsKey(from)) from = null;
            Array.Clear(dst, 0, ScreenW * ScreenH);

            if (cushion)
            {
                var rows = new (int y, int x0, int x1, string c)[]
                {
                    (31, 6, 53, "#3b4354"), (32, 4, 55, "#2b3140"), (33, 3, 56, "#2b3140"), (34, 3, 56, "#232834"), (35, 5, 54, "#1b1f28")
                };
                foreach (var r in rows) FillPx(dst, r.x0 * Scale, r.y * Scale, (r.x1 - r.x0 + 1) * Scale, Scale, C(r.c));
                foreach (var x in new[] { 10, 17, 24, 31, 38, 45, 52 }) FillPx(dst, x * Scale, 31 * Scale, Scale, Scale, C("#4a5468"));
            }

            var (poseA, fA, poseB, fB) = Poses(state, pos);
            if (from != null && mix < 1)
            {
                // troca de estado: o corpo desliza da pose antiga para a nova
                // (olhos, boca e efeitos já são os do estado novo)
                var (oldA, _, oldB, _) = Poses(from, fromPos);
                double t = Math.Clamp(mix, 0, 1);
                t = t * t * (3 - 2 * t); // começa e termina devagar
                Glide(poseA, oldA, t);
                Glide(poseB, oldB, t);
            }
            double[] lean = state == "dormindo" ? new[] { 0.06, -0.06 } : new[] { 0.0, 0.0 };
            int step = StepFor(state);
            DrawPetScreen(dst, Fred, poseA, fA, lean[0], false, 'a', -1, step);
            DrawPetScreen(dst, Dengo, poseB, fB, lean[1], state == "musica", 'b', 23, step);
        }

        /// <summary>Poses do Fred e do Dengo (o Dengo anda um pouco defasado, como no design).</summary>
        private static (Pose a, int fa, Pose b, int fb) Poses(string state, double pos)
        {
            var info = States[state];
            int n = info.Durations.Length;
            int ph = Phase.TryGetValue(state, out var v) ? v : 0;
            double posB = info.Loop ? ((pos + ph) % n + n) % n : Math.Max(0, pos - ph);
            return (MakePose(state, pos), (int)Math.Floor(pos), MakePose(state, posB), (int)Math.Floor(posB));
        }

        /// <summary>Leva as medidas do corpo de "p" na direção de "old" (t = 0: igual a old; t = 1: igual a p).</summary>
        private static void Glide(Pose p, Pose old, double t)
        {
            p.Dy = old.Dy + (p.Dy - old.Dy) * t;
            p.Sy = old.Sy + (p.Sy - old.Sy) * t;
            p.Sx = old.Sx + (p.Sx - old.Sx) * t;
            p.Tilt = old.Tilt + (p.Tilt - old.Tilt) * t;
        }

        /// <summary>
        /// Um bichinho na tela: sombra, corpo (com pulo, esticar/achatar e inclinação aplicados
        /// pixel a pixel) e efeitos. offsetX em blocos da grade da dupla.
        /// </summary>
        private static void DrawPetScreen(uint[] dst, Kind kind, Pose p, int f, double lean, bool mirror, char only, int offsetX, int step)
        {
            int S = Scale;
            int Q(double px) => Rnd(px / step) * step; // arredonda para o passo do movimento
            double tilt = p.Tilt + lean;
            if (mirror) tilt = -tilt;
            var (chr, fx) = DrawPet(kind, p, f, only);
            int ox = offsetX * S;

            // sombra no chão (preto com 38% de opacidade); encolhe quando eles pulam
            double sw = 9 * Math.Max(0.5, 1 + p.Dy / 40);
            Shade(dst, ox + Q((18 - sw) * S), ox + Q((18 + sw) * S), (G + 1) * S, S);
            Shade(dst, ox + Q((20 - sw) * S), ox + Q((16 + sw) * S), (G + 2) * S, S);

            // corpo: cada pixel da tela procura de qual bloco da arte ele vem
            // (desfaz o pulo, a inclinação e o esticar/achatar, nessa ordem)
            int midX = 18 * S, shearY = G * S;
            // O design estica a altura em (Sy − 1)·30 blocos. Aqui o esticar acontece só na testa:
            // o alto da cabeça e as orelhas sobem/descem como um bloco e o rosto fica parado
            // (menos bordas mudam por quadro = menos USB; e os olhos não deformam).
            double dH = (p.Sy - 1) * 30, dW = (p.Sx - 1) * 30;
            int eyG = 29 - kind.H + Rnd(kind.H * 0.42);
            int sliceY = (eyG - 1) * S;             // linha da testa, logo acima dos olhos
            int dTop = Q(dH * S);
            // a largura muda pouco nas animações calmas (menos de 1 bloco nem aparece no design);
            // nos pulos o corpo alarga/estreita inteiro
            dW = Math.Sign(dW) * Math.Max(0, Math.Abs(dW) - 1);
            double sx = 1 + Q(dW * S / 2) / (double)(kind.W * S / 2);
            int dyPx = Q(Soft(p.Dy / 5) * S);
            for (int ys = 0; ys < ScreenH; ys++)
            {
                double ty = ys - dyPx;
                double gyf = (ty < sliceY ? Math.Min(ty + dTop, sliceY) : ty) / S;
                if (gyf < 0 || gyf >= N) continue;
                int row = (int)gyf * N;
                // a inclinação anda por linha da arte (não por linha da tela), para não sobrar lascas soltas
                int shift = Q(tilt * 0.7 * (shearY - ((int)gyf + 0.5) * S));
                int drow = ys * ScreenW;
                for (int xs = 0; xs < N * S; xs++)
                {
                    int x = ox + xs;
                    if (x < 0 || x >= ScreenW) continue;
                    double gxf = (midX + (xs - shift - midX) / sx) / S;
                    if (gxf < 0 || gxf >= N) continue;
                    uint c = chr[row + (int)gxf];
                    if (c != 0) dst[drow + x] = c;
                }
            }

            // efeitos por cima
            foreach (var e in fx)
            {
                int bx = ox + Q(e.X * S), by = Q(e.Y * S);
                for (int j = 0; j < e.Map.Length; j++)
                    for (int i = 0; i < e.Map[j].Length; i++)
                    {
                        uint c = e.Cm(e.Map[j][i]);
                        if (c != 0) FillPx(dst, bx + i * S, by + j * S, S, S, c);
                    }
            }
        }

        private static void FillPx(uint[] dst, int x, int y, int w, int h, uint c)
        {
            int x0 = Math.Max(0, x), x1 = Math.Min(ScreenW, x + w), y0 = Math.Max(0, y), y1 = Math.Min(ScreenH, y + h);
            for (int yy = y0; yy < y1; yy++)
                for (int xx = x0; xx < x1; xx++) dst[yy * ScreenW + xx] = c;
        }

        /// <summary>Preto com 38% de opacidade por cima do que já está desenhado.</summary>
        private static void Shade(uint[] dst, int x, int x1, int y, int h)
        {
            const double a = 0.38;
            for (int yy = Math.Max(0, y); yy < Math.Min(ScreenH, y + h); yy++)
                for (int xx = Math.Max(0, x); xx < Math.Min(ScreenW, x1); xx++)
                {
                    uint d = dst[yy * ScreenW + xx];
                    double da = (d >> 24) / 255.0;
                    double oa = a + da * (1 - a);                 // opacidade final
                    double k = da * (1 - a) / oa;                 // quanto da cor de baixo sobra
                    uint r = (uint)Math.Round(((d >> 16) & 255) * k), g = (uint)Math.Round(((d >> 8) & 255) * k), b = (uint)Math.Round((d & 255) * k);
                    dst[yy * ScreenW + xx] = ((uint)Math.Round(oa * 255) << 24) | (r << 16) | (g << 8) | b;
                }
        }
    }
}
