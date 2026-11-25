using System;
using System.Collections.Generic;

namespace Pixo.Device
{
    /// <summary>
    /// Decide quais retângulos da tela precisam ser reenviados, comparando o quadro novo com o anterior.
    ///
    /// Medido na tela de verdade (tools/PixoBench): o USB entrega ~166 KB/s, e cada escrita extra
    /// custa só ~0,05 ms. Ou seja, o que pesa são os PIXELS, não a quantidade de retângulos.
    /// Por isso os retângulos são bem justos: linha a linha, só os trechos que mudaram; um trecho
    /// só se junta a outro (ou ao retângulo da linha de cima) quando isso custa menos que abrir
    /// um retângulo novo.
    /// </summary>
    public static class DirtyRects
    {
        /// <summary>Custo de abrir um retângulo, em "bytes de pixel": 6 do comando + 2 escritas × ~0,03 ms (medido).</summary>
        public static int RectCost = 16;

        public readonly struct Rect
        {
            public readonly int X0, Y0, X1, Y1;
            public Rect(int x0, int y0, int x1, int y1) { X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; }
            public int Pixels => (X1 - X0 + 1) * (Y1 - Y0 + 1);
        }

        // retângulos ainda "abertos" (que podem crescer para a linha de baixo); só a thread da tela usa
        private struct Open { public int X0, Y0, X1, Y1; }
        private static readonly List<Open> _open = new List<Open>();
        private static readonly List<(int x0, int x1)> _spans = new List<(int, int)>();

        /// <summary>
        /// cur/prev: quadros no formato do painel (w×h). prev == null = tudo mudou.
        /// O resultado vai em "result" (reaproveitada, para não criar memória a cada quadro).
        /// </summary>
        public static void Find(ushort[] cur, ushort[] prev, int w, int h, List<Rect> result)
        {
            result.Clear();
            if (prev == null)
            {
                result.Add(new Rect(0, 0, w - 1, h - 1));
                return;
            }

            int gap = RectCost / 2; // juntar dois trechos custa 2 bytes por pixel do meio
            _open.Clear();
            for (int y = 0; y < h; y++)
            {
                // 1) trechos que mudaram nesta linha (juntando os que estão perto)
                _spans.Clear();
                int row = y * w, x0 = -1, x1 = -1;
                for (int x = 0; x < w; x++)
                {
                    if (cur[row + x] == prev[row + x]) continue;
                    if (x0 >= 0 && x - x1 - 1 <= gap) { x1 = x; continue; }
                    if (x0 >= 0) _spans.Add((x0, x1));
                    x0 = x1 = x;
                }
                if (x0 >= 0) _spans.Add((x0, x1));

                // 2) cada trecho cresce um retângulo de cima ou abre um novo (o que sair mais barato)
                foreach (var (s0, s1) in _spans)
                {
                    int best = -1;
                    long bestCost = RectCost + (s1 - s0 + 1) * 2L;
                    for (int i = 0; i < _open.Count; i++)
                    {
                        var o = _open[i];
                        if (o.Y1 < y - 1) continue;
                        int nx0 = Math.Min(o.X0, s0), nx1 = Math.Max(o.X1, s1);
                        long oldArea = (long)(o.X1 - o.X0 + 1) * (o.Y1 - o.Y0 + 1);
                        long newArea = (long)(nx1 - nx0 + 1) * (y - o.Y0 + 1);
                        long cost = (newArea - oldArea) * 2;
                        if (cost < bestCost) { bestCost = cost; best = i; }
                    }
                    if (best >= 0)
                    {
                        var o = _open[best];
                        o.X0 = Math.Min(o.X0, s0); o.X1 = Math.Max(o.X1, s1); o.Y1 = y;
                        _open[best] = o;
                    }
                    else _open.Add(new Open { X0 = s0, Y0 = y, X1 = s1, Y1 = y });
                }

                // 3) os que não cresceram nesta linha estão prontos
                for (int i = _open.Count - 1; i >= 0; i--)
                {
                    var o = _open[i];
                    if (o.Y1 >= y) continue;
                    result.Add(new Rect(o.X0, o.Y0, o.X1, o.Y1));
                    _open.RemoveAt(i);
                }
            }
            foreach (var o in _open) result.Add(new Rect(o.X0, o.Y0, o.X1, o.Y1));
        }
    }
}
