using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace Aura
{
    // A picture with ready-made see-through copies (12 steps, made only when first needed).
    // Drawing a ready copy is plain copying; fading a picture while drawing it is several times slower in Windows' GDI+.
    sealed class Faded : IDisposable
    {
        public const int Steps = 12;
        public readonly Bitmap Source;
        readonly Bitmap[] levels = new Bitmap[Steps + 1];
        readonly Color? tint;
        readonly bool own;

        public Faded(Bitmap source, Color? tint = null, bool own = true) { Source = source; this.tint = tint; this.own = own; }

        public int Width => Source.Width;
        public int Height => Source.Height;

        public Bitmap At(float a)
        {
            int q = Math.Max(0, Math.Min(Steps, (int)Math.Round(a * Steps)));
            if (q == 0) return null;
            if (q == Steps && tint == null) return Source;
            if (levels[q] != null) return levels[q];
            var b = new Bitmap(Source.Width, Source.Height, PixelFormat.Format32bppPArgb);
            using (var g = System.Drawing.Graphics.FromImage(b))
            using (var ia = new ImageAttributes())
            {
                var c = tint ?? Color.White;
                float k = q / (float)Steps;
                ia.SetColorMatrix(tint == null ? new ColorMatrix { Matrix33 = k } : new ColorMatrix(new[]
                {
                    new float[] { c.R / 255f, 0, 0, 0, 0 }, new float[] { 0, c.G / 255f, 0, 0, 0 }, new float[] { 0, 0, c.B / 255f, 0, 0 },
                    new float[] { 0, 0, 0, k, 0 }, new float[] { 0, 0, 0, 0, 1 }
                }));
                g.DrawImage(Source, new Rectangle(0, 0, b.Width, b.Height), 0, 0, b.Width, b.Height, GraphicsUnit.Pixel, ia);
            }
            levels[q] = b;
            return b;
        }

        public void Dispose()
        {
            for (int i = 0; i < levels.Length; i++) { if (levels[i] != null && levels[i] != Source) levels[i].Dispose(); levels[i] = null; }
            if (own) Source.Dispose();
        }
    }
}
