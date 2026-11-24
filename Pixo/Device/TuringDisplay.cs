using System;
using System.IO.Ports;
using System.Linq;
using Microsoft.Win32;

namespace Pixo.Device
{
    /// <summary>
    /// Driver da Turing Smart Screen 3.5" (protocolo "revisão A").
    /// - Porta serial USB, 115200 baud, controle de fluxo RTS/CTS.
    /// - Comando = 6 bytes: x, y, x_fim, y_fim empacotados em 5 bytes + código.
    /// - Desenho: DISPLAY_BITMAP com o retângulo + pixels RGB565 little-endian.
    /// A tela fica sempre em "retrato" no hardware (320×480); o app desenha em
    /// paisagem (480×320) e gira a imagem aqui, por software.
    /// </summary>
    public sealed class TuringDisplay : IDisposable
    {
        public const int PanelW = 320, PanelH = 480;   // nativo do painel
        public const int W = 480, H = 320;             // como o app desenha (paisagem)

        private const byte CmdClear = 102, CmdScreenOff = 108, CmdScreenOn = 109,
                           CmdBrightness = 110, CmdOrientation = 121, CmdBitmap = 197;

        private readonly SerialPort _port;
        private ushort[] _last;          // último quadro enviado (no formato do painel)
        public string PortName { get; }

        private TuringDisplay(string portName)
        {
            PortName = portName;
            _port = new SerialPort(portName, 115200)
            {
                Handshake = Handshake.RequestToSend,
                ReadTimeout = 1000,
                WriteTimeout = 5000,
                WriteBufferSize = 1 << 20
            };
            _port.Open();
        }

        /// <summary>Procura a tela e conecta. Devolve null se não achar.</summary>
        public static TuringDisplay TryOpen(string configuredPort)
        {
            string port = null;
            var existing = SerialPort.GetPortNames();
            if (!string.IsNullOrWhiteSpace(configuredPort) && configuredPort.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
                port = configuredPort.Trim().ToUpperInvariant();
            else
                port = FindPort(existing);

            if (port == null || !existing.Contains(port, StringComparer.OrdinalIgnoreCase)) return null;

            var d = new TuringDisplay(port);
            d.SendOrientation();
            return d;
        }

        /// <summary>A tela usa o chip USB da WCH (VID 1A86, PID 5722). Procuramos a porta COM dela no registro.</summary>
        private static string FindPort(string[] existing)
        {
            try
            {
                using var usb = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
                if (usb == null) return null;
                foreach (var dev in usb.GetSubKeyNames().Where(n => n.StartsWith("VID_1A86&PID_5722", StringComparison.OrdinalIgnoreCase)))
                {
                    using var devKey = usb.OpenSubKey(dev);
                    if (devKey == null) continue;
                    foreach (var inst in devKey.GetSubKeyNames())
                    {
                        using var p = devKey.OpenSubKey(inst + @"\Device Parameters");
                        if (p?.GetValue("PortName") is string name && existing.Contains(name, StringComparer.OrdinalIgnoreCase))
                            return name;
                    }
                }
            }
            catch { }
            return null;
        }

        // ---------------- comandos ----------------

        private void Cmd(byte cmd, int x = 0, int y = 0, int ex = 0, int ey = 0)
        {
            var b = new byte[]
            {
                (byte)(x >> 2),
                (byte)(((x & 3) << 6) + (y >> 4)),
                (byte)(((y & 15) << 4) + (ex >> 6)),
                (byte)(((ex & 63) << 2) + (ey >> 8)),
                (byte)(ey & 255),
                cmd
            };
            _port.Write(b, 0, b.Length);
        }

        private void SendOrientation()
        {
            var b = new byte[16];
            b[5] = CmdOrientation;
            b[6] = 100;                // 0 (retrato) + 100
            b[7] = PanelW >> 8; b[8] = PanelW & 255;
            b[9] = PanelH >> 8; b[10] = PanelH & 255;
            _port.Write(b, 0, b.Length);
        }

        public void Clear() { Cmd(CmdClear); _last = null; }
        public void ScreenOn() => Cmd(CmdScreenOn);
        public void ScreenOff() => Cmd(CmdScreenOff);

        /// <summary>level 0–100 (no hardware: 0 = mais claro, 255 = mais escuro).</summary>
        public void SetBrightness(double level)
        {
            int v = (int)Math.Round(255 - Math.Clamp(level, 0, 100) * 255 / 100);
            Cmd(CmdBrightness, x: v);
        }

        /// <summary>Força o próximo quadro a ser enviado inteiro.</summary>
        public void Invalidate() => _last = null;

        // ---------------- correção de cor ----------------

        private readonly byte[] _lut = new byte[256];
        private double _lutContrast = -1, _lutGamma = -1;
        private int _satFixed = 256; // saturação em ponto fixo (256 = 1,0)

        /// <summary>
        /// Ajusta as cores para compensar o painel (que mostra cores lavadas).
        /// saturation/contrast/gamma em porcentagem (100 = sem mudança).
        /// </summary>
        /// <returns>true se algo mudou (o próximo quadro vai inteiro).</returns>
        public bool SetColorTune(double saturation, double contrast, double gamma)
        {
            int satFixed = (int)Math.Round(saturation / 100.0 * 256);
            if (satFixed == _satFixed && contrast == _lutContrast && gamma == _lutGamma) return false;
            _satFixed = satFixed;
            _lutContrast = contrast; _lutGamma = gamma;
            double c = contrast / 100.0, g = gamma / 100.0;
            for (int i = 0; i < 256; i++)
            {
                double v = i / 255.0;
                v = Math.Pow(v, g);                 // gama: >1 encorpa os tons médios
                v = (v - 0.5) * c + 0.5;            // contraste em torno do cinza médio
                _lut[i] = (byte)Math.Clamp((int)Math.Round(v * 255), 0, 255);
            }
            _last = null; // reenvia tudo com as cores novas
            return true;
        }

        private bool TuneActive => _satFixed != 256 || _lutContrast != 100 || _lutGamma != 100;

        /// <summary>
        /// Envia um quadro 480×320 (BGRA, 4 bytes por pixel). Só o retângulo que mudou
        /// desde o último quadro é enviado. Devolve quantos pixels foram enviados.
        /// </summary>
        public int SendFrame(byte[] bgra, bool flipped)
        {
            if (_lutContrast < 0) SetColorTune(100, 100, 100);
            bool tune = TuneActive;
            int sat = _satFixed;
            // 1) gira para o formato do painel (320×480) e converte para RGB565
            var px = new ushort[PanelW * PanelH];
            for (int py = 0; py < PanelH; py++)
            {
                for (int pxl = 0; pxl < PanelW; pxl++)
                {
                    int sx, sy;
                    if (!flipped) { sx = py; sy = H - 1 - pxl; }          // gira 90° num sentido
                    else { sx = W - 1 - py; sy = pxl; }                    // e no outro
                    int i = (sy * W + sx) * 4;
                    int b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
                    if (tune)
                    {
                        // saturação: afasta cada canal do cinza (luminância) do pixel
                        if (sat != 256)
                        {
                            int y = (r * 77 + g * 150 + b * 29) >> 8;
                            r = Math.Clamp(y + (((r - y) * sat) >> 8), 0, 255);
                            g = Math.Clamp(y + (((g - y) * sat) >> 8), 0, 255);
                            b = Math.Clamp(y + (((b - y) * sat) >> 8), 0, 255);
                        }
                        r = _lut[r]; g = _lut[g]; b = _lut[b];
                    }
                    px[py * PanelW + pxl] = (ushort)(((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3));
                }
            }

            // 2) acha o retângulo que mudou
            int x0 = 0, y0 = 0, x1 = PanelW - 1, y1 = PanelH - 1;
            if (_last != null)
            {
                x0 = PanelW; y0 = PanelH; x1 = -1; y1 = -1;
                for (int y = 0; y < PanelH; y++)
                {
                    int row = y * PanelW;
                    for (int x = 0; x < PanelW; x++)
                    {
                        if (px[row + x] != _last[row + x])
                        {
                            if (x < x0) x0 = x;
                            if (x > x1) x1 = x;
                            if (y < y0) y0 = y;
                            y1 = y;
                        }
                    }
                }
                if (x1 < 0) return 0; // nada mudou
            }

            // 3) envia
            int w = x1 - x0 + 1, h = y1 - y0 + 1;
            var data = new byte[w * h * 2];
            int k = 0;
            for (int y = y0; y <= y1; y++)
            {
                int row = y * PanelW;
                for (int x = x0; x <= x1; x++)
                {
                    ushort v = px[row + x];
                    data[k++] = (byte)(v & 0xFF);
                    data[k++] = (byte)(v >> 8);
                }
            }
            Cmd(CmdBitmap, x0, y0, x1, y1);
            int chunk = w * 8 * 2 * 8;
            for (int i = 0; i < data.Length; i += chunk)
                _port.Write(data, i, Math.Min(chunk, data.Length - i));

            _last = px;
            return w * h;
        }

        public void Dispose()
        {
            try { _port?.Close(); } catch { }
            try { _port?.Dispose(); } catch { }
        }
    }
}
