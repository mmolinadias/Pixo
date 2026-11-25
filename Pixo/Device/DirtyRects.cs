using System;
using System.Collections.Generic;

namespace Pixo.Device
{
    /// <summary>
    /// Decide quais retângulos da tela precisam ser reenviados, comparando o quadro novo com o anterior.
    ///
    /// A tela é dividida em faixas de 16 linhas. Em cada faixa, só os trechos de colunas que mudaram
    /// viram um retângulo (trechos muito próximos são juntados, para não mandar comandos demais).
    /// Assim, quando os bichinhos se mexem, só as bordas e os detalhes que mudaram vão pelo USB,
    /// e não a área inteira deles.
    /// </summary>
    public static class DirtyRects
    {
        public static int Band = 16;     // altura da faixa (linhas do painel)
        public static int MergeGap = 24; // trechos a menos de 24 px um do outro viram um só

        public readonly struct Rect
        {
            public readonly int X0, Y0, X1, Y1;
            public Rect(int x0, int y0, int x1, int y1) { X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; }
            public int Pixels => (X1 - X0 + 1) * (Y1 - Y0 + 1);
        }

        /// <summary>
        /// cur/prev: quadros no formato do painel (w×h). prev == null = tudo mudou.
        /// colBuf: buffer de apoio com tamanho w (evita criar memória a cada quadro).
        /// </summary>
        public static void Find(ushort[] cur, ushort[] prev, int w, int h, bool[] colBuf, List<Rect> result)
        {
            result.Clear();
            if (prev == null)
            {
                result.Add(new Rect(0, 0, w - 1, h - 1));
                return;
            }

            for (int by = 0; by < h; by += Band)
            {
                int bEnd = Math.Min(h, by + Band);

                // 1) quais colunas mudaram nesta faixa
                bool any = false;
                Array.Clear(colBuf, 0, w);
                for (int y = by; y < bEnd; y++)
                {
                    int row = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        if (cur[row + x] != prev[row + x]) { colBuf[x] = true; any = true; }
                    }
                }
                if (!any) continue;

                // 2) trechos de colunas que mudaram (juntando os próximos)
                int x0 = -1, x1 = -1;
                for (int x = 0; x <= w; x++)
                {
                    bool changed = x < w && colBuf[x];
                    if (changed)
                    {
                        if (x0 < 0) x0 = x;
                        else if (x - x1 > MergeGap) { Emit(cur, prev, w, by, bEnd, x0, x1, result); x0 = x; }
                        x1 = x;
                    }
                    else if (x == w && x0 >= 0)
                    {
                        Emit(cur, prev, w, by, bEnd, x0, x1, result);
                    }
                }
            }
        }

        /// <summary>Recorta as linhas de cima e de baixo que não mudaram dentro do trecho.</summary>
        private static void Emit(ushort[] cur, ushort[] prev, int w, int by, int bEnd, int x0, int x1, List<Rect> result)
        {
            int y0 = -1, y1 = -1;
            for (int y = by; y < bEnd; y++)
            {
                int row = y * w;
                for (int x = x0; x <= x1; x++)
                    if (cur[row + x] != prev[row + x]) { if (y0 < 0) y0 = y; y1 = y; break; }
            }
            if (y0 >= 0) result.Add(new Rect(x0, y0, x1, y1));
        }
    }
}
