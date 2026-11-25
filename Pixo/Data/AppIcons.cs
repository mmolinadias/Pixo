using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pixo.Data
{
    /// <summary>
    /// Ícone oficial de um app, pego do próprio Windows (o mesmo do Menu Iniciar).
    /// Funciona com apps da Microsoft Store e com programas comuns (.exe).
    /// </summary>
    public static class AppIcons
    {
        private static readonly Dictionary<string, BitmapSource> Cache = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// id: o identificador do app que o Windows informa (AppUserModelId), por exemplo
        /// "Spotify.exe", "5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App" ou "com.squirrel.Discord.Discord".
        /// Devolve null se não achar.
        /// </summary>
        public static BitmapSource Get(string id, int size = 64)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            lock (Cache) if (Cache.TryGetValue(id, out var c)) return c;

            BitmapSource bmp = null;
            try
            {
                // 1) pelo Menu Iniciar (shell:AppsFolder) — apps da Store e a maioria dos programas
                bmp = FromShell(@"shell:AppsFolder\" + id, size);

                // 2) pelo .exe de um processo aberto (ex.: o Spotify informa só "Spotify.exe")
                if (bmp == null)
                {
                    var exe = FindExe(id);
                    if (exe != null) bmp = FromShell(exe, size);
                }
            }
            catch { bmp = null; }

            lock (Cache) Cache[id] = bmp;
            return bmp;
        }

        private static string FindExe(string id)
        {
            try
            {
                var name = id.Split('!')[0];
                name = Path.GetFileName(name);
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
                // AUMIDs como "com.squirrel.Discord.Discord": tenta a última parte
                var candidates = new[] { name, name.Split('.').Last() }.Distinct();
                foreach (var n in candidates)
                {
                    foreach (var p in Process.GetProcessesByName(n))
                    {
                        try
                        {
                            var path = p.MainModule?.FileName;
                            if (!string.IsNullOrEmpty(path)) return path;
                        }
                        catch { }
                        finally { p.Dispose(); }
                    }
                }
            }
            catch { }
            return null;
        }

        // ---------------- COM do Windows Shell ----------------

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int cx, cy; }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);
        [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr h, int size, ref DIBSECTION ds);

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount;
            public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DIBSECTION
        {
            public BITMAP dsBm; public BITMAPINFOHEADER dsBmih;
            public uint dsBitfields0, dsBitfields1, dsBitfields2; public IntPtr dshSection; public uint dsOffset;
        }

        private const int SIIGBF_ICONONLY = 0x4, SIIGBF_BIGGERSIZEOK = 0x1;

        private static BitmapSource FromShell(string path, int size)
        {
            IShellItemImageFactory factory;
            try { SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out factory); }
            catch { return null; }
            if (factory == null) return null;

            IntPtr hbm = IntPtr.Zero;
            try
            {
                if (factory.GetImage(new SIZE { cx = size, cy = size }, SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK, out hbm) != 0 || hbm == IntPtr.Zero)
                    return null;

                // copia os pixels com transparência (o jeito comum perde o alfa)
                var ds = new DIBSECTION();
                if (GetObject(hbm, Marshal.SizeOf<DIBSECTION>(), ref ds) == 0 || ds.dsBm.bmBits == IntPtr.Zero || ds.dsBm.bmBitsPixel != 32)
                    return null;
                int w = ds.dsBm.bmWidth, h = ds.dsBm.bmHeight, stride = ds.dsBm.bmWidthBytes;
                var pixels = new byte[stride * h];
                Marshal.Copy(ds.dsBm.bmBits, pixels, 0, pixels.Length);

                // DIB "de baixo para cima" (altura positiva): inverte as linhas
                if (ds.dsBmih.biHeight > 0)
                {
                    var flipped = new byte[pixels.Length];
                    for (int y = 0; y < h; y++)
                        Buffer.BlockCopy(pixels, y * stride, flipped, (h - 1 - y) * stride, stride);
                    pixels = flipped;
                }

                // ícones sem canal alfa (tudo 0): trata como opacos
                bool anyAlpha = false;
                for (int i = 3; i < pixels.Length; i += 4) if (pixels[i] != 0) { anyAlpha = true; break; }
                if (!anyAlpha) for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;

                var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
            finally
            {
                if (hbm != IntPtr.Zero) DeleteObject(hbm);
                try { Marshal.ReleaseComObject(factory); } catch { }
            }
        }
    }
}
