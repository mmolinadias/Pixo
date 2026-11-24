using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pixo.Render;

namespace Pixo.UI
{
    /// <summary>Ícone de traço do sistema de design Pixo (os mesmos 24×24 do display).</summary>
    public class PixoIcon : FrameworkElement
    {
        public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
            nameof(Kind), typeof(string), typeof(PixoIcon), new FrameworkPropertyMetadata("info", FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly Brush DefaultBrush = MakeDefaultBrush();
        private static Brush MakeDefaultBrush()
        {
            var b = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6));
            b.Freeze(); // valor padrão de propriedade precisa ser congelado
            return b;
        }

        public static readonly DependencyProperty BrushProperty = DependencyProperty.Register(
            nameof(Brush), typeof(Brush), typeof(PixoIcon), new FrameworkPropertyMetadata(DefaultBrush, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty StrokeWidthProperty = DependencyProperty.Register(
            nameof(StrokeWidth), typeof(double), typeof(PixoIcon), new FrameworkPropertyMetadata(1.8, FrameworkPropertyMetadataOptions.AffectsRender));

        public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
        public Brush Brush { get => (Brush)GetValue(BrushProperty); set => SetValue(BrushProperty, value); }
        public double StrokeWidth { get => (double)GetValue(StrokeWidthProperty); set => SetValue(StrokeWidthProperty, value); }

        protected override Size MeasureOverride(Size availableSize)
        {
            double w = double.IsNaN(Width) ? 20 : Width, h = double.IsNaN(Height) ? 20 : Height;
            return new Size(w, h);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double size = Math.Min(ActualWidth, ActualHeight);
            if (size <= 0) return;
            D.Icon(dc, Kind, (ActualWidth - size) / 2, (ActualHeight - size) / 2, size, Brush, StrokeWidth);
        }
    }

    public static class Images
    {
        /// <summary>Logo do Pixo como imagem (para a interface e o ícone da bandeja).</summary>
        public static BitmapSource Logo(int px, bool off = false)
        {
            var dv = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
            using (var dc = dv.RenderOpen()) Bit.DrawLogo(dc, px, off);
            var rtb = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        /// <summary>Um quadro do Bit (180×180) para a prévia na janela.</summary>
        public static BitmapSource BitFrame(string state, int frame)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Pal.Bg, null, new Rect(0, 0, Bit.Size, Bit.Size));
                Bit.Draw(dc, state, frame);
            }
            var rtb = new RenderTargetBitmap(Bit.Size, Bit.Size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }
    }
}
