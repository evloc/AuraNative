using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;

namespace Aura
{
    // The full-screen lyrics scene, without the drawing: where everything goes, the cover's colors, the
    // blurred backdrop, the light streaks flying out of the cover and the glowing dots. The graphics-card
    // version (Wpf/Stage) and the older one (FullLyrics) both draw from this, so they look the same.
    sealed class Cosmos
    {
        // ---------- where things go ----------
        public sealed class Layout
        {
            public float W, H, Cx, Cy, S;               // the cover's center and size
            public RectangleF Cover, Ring, Date, Clock, Title, Artist, Album, Lyrics, Bar, UpNext;
            public float CardW, CardH, CardGap;
        }

        public static Layout Place(float W, float H, bool lyrics)
        {
            var L = new Layout { W = W, H = H };
            L.S = (float)Math.Round(Math.Min(H * 0.3f, W * (lyrics ? 0.19f : 0.22f)));
            L.Cx = W * (lyrics ? 0.37f : 0.5f);
            L.Cy = H * 0.42f;
            L.Cover = new RectangleF(L.Cx - L.S / 2, L.Cy - L.S / 2, L.S, L.S);
            float ring = L.S * 1.34f;
            L.Ring = new RectangleF(L.Cx - ring / 2, L.Cy - ring / 2, ring, ring);
            float tw = Math.Max(L.S * 2.2f, W * 0.26f);
            L.Clock = new RectangleF(L.Cx - tw / 2, L.Ring.Top - H * 0.115f, tw, H * 0.1f);
            L.Date = new RectangleF(L.Cx - tw / 2, L.Clock.Top - H * 0.03f, tw, H * 0.028f);
            L.Title = new RectangleF(L.Cx - tw / 2, L.Ring.Bottom + H * 0.005f, tw, H * 0.045f);
            L.Artist = new RectangleF(L.Cx - tw / 2, L.Title.Bottom, tw, H * 0.03f);
            L.Album = new RectangleF(L.Cx - tw / 2, L.Artist.Bottom, tw, H * 0.026f);
            L.Lyrics = new RectangleF(W * 0.565f, H * 0.09f, W * 0.385f, H * 0.76f);
            float bw = Math.Min(W * 0.5f, Math.Max(760, W * 0.38f)), bh = Math.Max(66, H * 0.064f);
            L.Bar = new RectangleF((W - bw) / 2, H - bh - H * 0.03f, bw, bh);
            L.CardW = Math.Max(190, W * 0.105f); L.CardH = Math.Max(52, H * 0.052f); L.CardGap = L.CardH * 0.24f;
            float left = lyrics ? W * 0.045f : W * 0.08f;
            L.UpNext = new RectangleF(left, L.Cy - (L.CardH * 5 + L.CardGap * 4) / 2 - H * 0.02f, L.CardW * 1.1f, L.CardH * 5 + L.CardGap * 4 + H * 0.03f);
            return L;
        }

        // ---------- the cover's colors ----------
        // three colors from the cover (the strongest hues), for the backdrop and the glowing dots
        public static Color[] Palette(Bitmap cover, Color accent)
        {
            var fallback = new[] { accent, Theme.Mix(accent, Color.FromArgb(255, 170, 70), 0.6f), Theme.Mix(accent, Color.FromArgb(140, 255, 120), 0.6f) };
            if (cover == null) return fallback;
            try
            {
                using (var tiny = new Bitmap(28, 28))
                {
                    using (var g = Graphics.FromImage(tiny)) { g.InterpolationMode = InterpolationMode.HighQualityBilinear; g.DrawImage(cover, 0, 0, 28, 28); }
                    var w = new double[24]; var sum = new double[24, 3];
                    for (int y = 0; y < 28; y++)
                        for (int x = 0; x < 28; x++)
                        {
                            var c = tiny.GetPixel(x, y);
                            float s = c.GetSaturation(), l = c.GetBrightness();
                            if (l < 0.1f || l > 0.96f) continue;
                            double k = s * s * (1 - Math.Abs(l - 0.55) * 1.3) + 0.002;
                            if (k <= 0) continue;
                            int h = (int)(c.GetHue() / 15) % 24;
                            w[h] += k; sum[h, 0] += c.R * k; sum[h, 1] += c.G * k; sum[h, 2] += c.B * k;
                        }
                    var picked = new List<Color>();
                    var used = new bool[24];
                    for (int n = 0; n < 3; n++)
                    {
                        int best = -1; double bw = 0.25;
                        for (int i = 0; i < 24; i++)
                        {
                            if (used[i] || used[(i + 1) % 24] || used[(i + 23) % 24]) continue;
                            if (w[i] > bw) { bw = w[i]; best = i; }
                        }
                        if (best < 0) break;
                        used[best] = true;
                        var col = Color.FromArgb((int)(sum[best, 0] / w[best]), (int)(sum[best, 1] / w[best]), (int)(sum[best, 2] / w[best]));
                        picked.Add(Theme.FromHsl(col.GetHue(), Math.Max(0.5f, Math.Min(0.95f, col.GetSaturation() * 1.2f)), 0.6f));
                    }
                    for (int i = picked.Count; i < 3; i++) picked.Add(fallback[i]);
                    return picked.ToArray();
                }
            }
            catch { return fallback; }
        }

        // The sky: the cover, blurred into soft clouds of its colors, darkened, with dark edges.
        // Painted small (it's all soft) and stretched by whoever draws it.
        public static Bitmap Backdrop(Bitmap cover, Color[] pal, int w, int h)
        {
            var b = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.Clear(Color.FromArgb(8, 9, 14));
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                if (cover != null)
                {
                    // shrink to a few pixels, then blow it up: a very wide blur for free
                    using (var tiny = new Bitmap(4, 4, PixelFormat.Format32bppPArgb))
                    {
                        using (var tg = Graphics.FromImage(tiny)) { tg.InterpolationMode = InterpolationMode.HighQualityBilinear; tg.DrawImage(cover, 0, 0, 4, 4); }
                        using (var ia = new ImageAttributes())
                        {
                            ia.SetWrapMode(WrapMode.TileFlipXY);
                            ia.SetColorMatrix(new ColorMatrix(new[]
                            {
                                new float[] { 0.62f, 0, 0, 0, 0 }, new float[] { 0, 0.62f, 0, 0, 0 }, new float[] { 0, 0, 0.62f, 0, 0 },
                                new float[] { 0, 0, 0, 1, 0 }, new float[] { 0.02f, 0.02f, 0.03f, 0, 1 },
                            }));
                            float over = Math.Max(w, h) * 0.3f;
                            g.DrawImage(tiny, new[] { new PointF(-over, -over), new PointF(w + over, -over), new PointF(-over, h + over) }, new RectangleF(0, 0, 4, 4), GraphicsUnit.Pixel, ia);
                        }
                    }
                }
                // soft clouds of the cover's colors
                var rnd = new Random(pal[0].ToArgb());
                for (int i = 0; i < 5; i++)
                {
                    var c = pal[i % pal.Length];
                    float r = Math.Max(w, h) * (0.25f + (float)rnd.NextDouble() * 0.2f);
                    float x = w * (0.1f + (float)rnd.NextDouble() * 0.8f), y = h * (0.15f + (float)rnd.NextDouble() * 0.7f);
                    Blob(g, x, y, r, Color.FromArgb(70, c));
                }
                // darker toward the edges (and a little overall, so the words stay readable)
                using (var p = new GraphicsPath())
                {
                    p.AddEllipse(-w * 0.25f, -h * 0.3f, w * 1.5f, h * 1.6f);
                    using (var pb = new PathGradientBrush(p) { CenterColor = Color.FromArgb(0, 0, 0, 0), SurroundColors = new[] { Color.FromArgb(235, 0, 0, 0) }, CenterPoint = new PointF(w * 0.5f, h * 0.45f) })
                    {
                        // position 0 is the edge, 1 the middle; the factor is how much of the (clear) middle color
                        pb.Blend = new Blend { Factors = new[] { 0f, 0.55f, 1f, 1f }, Positions = new[] { 0f, 0.28f, 0.6f, 1f } };
                        g.FillRectangle(pb, 0, 0, w, h);
                    }
                    using (var dim = new SolidBrush(Color.FromArgb(40, 0, 0, 0))) g.FillRectangle(dim, 0, 0, w, h);
                }
            }
            return b;
        }

        static void Blob(Graphics g, float x, float y, float r, Color c)
        {
            using (var p = new GraphicsPath())
            {
                p.AddEllipse(x - r, y - r, r * 2, r * 2);
                using (var pb = new PathGradientBrush(p) { CenterColor = c, SurroundColors = new[] { Color.FromArgb(0, c) } })
                {
                    pb.SetSigmaBellShape(0.5f, 1f);
                    g.FillEllipse(pb, x - r, y - r, r * 2, r * 2);
                }
            }
        }

        // ---------- deep space: the same for every song ----------
        // violet, pink and blue splashes of nebula on black
        public static readonly Color[] SpacePal = { Color.FromArgb(150, 100, 255), Color.FromArgb(255, 90, 175), Color.FromArgb(70, 170, 255) };

        public static Bitmap SpaceSky(int w, int h)
        {
            var b = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.Clear(Color.FromArgb(3, 3, 8));
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var rnd = new Random(1977);
                float m = Math.Max(w, h);
                // big soft clouds, then smaller brighter knots inside them
                (float x, float y, float r, int c, int a)[] clouds =
                {
                    (0.12f, 0.2f, 0.3f, 0, 30), (0.88f, 0.16f, 0.26f, 1, 26), (0.82f, 0.88f, 0.32f, 2, 28),
                    (0.06f, 0.92f, 0.22f, 1, 18), (0.42f, 0.04f, 0.16f, 2, 16),
                };
                foreach (var cl in clouds)
                {
                    Blob(g, w * cl.x, h * cl.y, m * cl.r, Color.FromArgb(cl.a, SpacePal[cl.c]));
                    for (int i = 0; i < 7; i++)
                    {
                        float ang = (float)(rnd.NextDouble() * Math.PI * 2), d = (float)rnd.NextDouble() * m * cl.r * 0.55f;
                        Blob(g, w * cl.x + (float)Math.Cos(ang) * d, h * cl.y + (float)Math.Sin(ang) * d, m * cl.r * (0.18f + (float)rnd.NextDouble() * 0.25f),
                            Color.FromArgb((int)(cl.a * 0.9f), Theme.Mix(SpacePal[cl.c], SpacePal[(cl.c + 1) % 3], (float)rnd.NextDouble() * 0.4f)));
                    }
                }
                // a faint dust of tiny stars that never moves (the moving ones are drawn on top)
                for (int i = 0; i < w * h / 2600; i++)
                {
                    float x = (float)rnd.NextDouble() * w, y = (float)rnd.NextDouble() * h, r = 0.4f + (float)rnd.NextDouble() * 0.7f;
                    using (var br = new SolidBrush(Color.FromArgb(40 + rnd.Next(90), 220, 225, 255))) g.FillEllipse(br, x - r, y - r, r * 2, r * 2);
                }
                // darker toward the edges
                using (var p = new GraphicsPath())
                {
                    p.AddEllipse(-w * 0.25f, -h * 0.3f, w * 1.5f, h * 1.6f);
                    using (var pb = new PathGradientBrush(p) { CenterColor = Color.FromArgb(0, 0, 0, 0), SurroundColors = new[] { Color.FromArgb(220, 0, 0, 0) }, CenterPoint = new PointF(w * 0.5f, h * 0.45f) })
                    {
                        pb.Blend = new Blend { Factors = new[] { 0f, 0.6f, 1f, 1f }, Positions = new[] { 0f, 0.25f, 0.55f, 1f } };
                        g.FillRectangle(pb, 0, 0, w, h);
                    }
                }
            }
            return b;
        }

        // ---------- moving things ----------
        public float Energy, Kick, Peak;                   // eased loudness, a beat's punch, the warp between songs (0..1)
        double warp, nextShot = 2;
        public double Time;

        public sealed class Star { public float X, Y, Size, Depth, Phase, Speed; public int Tint; }
        public sealed class Shot { public float X, Y, Vx, Vy, Life, Age, Len; }
        public readonly List<Star> Stars = new List<Star>();
        public readonly List<Shot> Shots = new List<Shot>();
        readonly Random rnd = new Random(7);
        float W = 1920, H = 1080;

        public Cosmos()
        {
            void Layer(int n, float depth, float min, float max)
            {
                for (int i = 0; i < n; i++)
                    Stars.Add(new Star
                    {
                        X = (float)rnd.NextDouble(), Y = (float)rnd.NextDouble(), Depth = depth, Size = min + (float)rnd.NextDouble() * (max - min),
                        Phase = (float)(rnd.NextDouble() * 6.28), Speed = 0.5f + (float)rnd.NextDouble(), Tint = rnd.Next(10) < 7 ? -1 : rnd.Next(3),
                    });
            }
            Layer(170, 0.3f, 0.6f, 1.2f); Layer(80, 0.6f, 1.1f, 1.9f); Layer(22, 1f, 1.8f, 3f);
        }

        public void Warp() => warp = 1;

        // loud: 0..1 from the song's loudness, or -1 when AURA hasn't heard the song yet
        public void Step(float dt, float loud, bool playing, float w, float h)
        {
            W = w; H = h;
            Time += dt;
            if (loud >= 0)
            {
                if (loud > Energy + 0.12f) Kick = Math.Min(1, Kick + (loud - Energy) * 1.6f);
                Energy += (loud - Energy) * Ease(dt, loud > Energy ? 14 : 3);
            }
            else Energy += (0.3f - Energy) * Ease(dt, 1);
            if (!playing) Energy += (0 - Energy) * Ease(dt, 2);
            Kick *= (float)Math.Exp(-dt * 5);
            warp = Math.Max(0, warp - dt / 1.4);
            Peak = warp > 0 ? (float)Math.Sin(Math.PI * (1 - warp)) : 0;
            // stars drift slowly sideways (faster on the big parts of the song)
            float drift = dt * (playing ? 0.004f : 0.0015f) * (1 + Energy * 1.5f + Kick * 2) + dt * Peak * 0.05f;
            foreach (var s in Stars) { s.X -= drift * s.Depth * s.Speed; if (s.X < -0.01f) { s.X += 1.02f; s.Y = (float)rnd.NextDouble(); } }
            // the odd shooting star
            nextShot -= dt * (1 + Energy);
            if (nextShot <= 0)
            {
                nextShot = 3 + rnd.NextDouble() * 7;
                double ang = Math.PI * (0.1 + rnd.NextDouble() * 0.2) * (rnd.Next(2) == 0 ? 1 : -1) + (rnd.Next(2) == 0 ? 0 : Math.PI);
                if (Math.Sin(ang) < 0) ang = -ang;      // always heading down
                float v = H * (0.9f + (float)rnd.NextDouble() * 0.7f);
                Shots.Add(new Shot { X = (float)rnd.NextDouble() * W, Y = (float)rnd.NextDouble() * H * 0.4f, Vx = (float)Math.Cos(ang) * v, Vy = (float)Math.Sin(ang) * v, Life = 0.6f + (float)rnd.NextDouble() * 0.6f, Len = H * (0.12f + (float)rnd.NextDouble() * 0.1f) });
            }
            for (int i = Shots.Count - 1; i >= 0; i--) { var m = Shots[i]; m.Age += dt; m.X += m.Vx * dt; m.Y += m.Vy * dt; if (m.Age >= m.Life) Shots.RemoveAt(i); }
        }

        static float Ease(float dt, float speed) => 1 - (float)Math.Exp(-dt * speed);

        // a star on screen: where, how big, how bright; during the warp between songs it's stretched away from (cx, cy) into a line
        public void Place(Star s, float cx, float cy, out PointF p, out PointF tail, out float r, out float alpha)
        {
            p = new PointF(s.X * W, s.Y * H);
            float tw = 0.55f + 0.45f * (float)Math.Sin(Time * (1.1 + s.Speed) + s.Phase);
            alpha = Math.Min(1, (0.3f + 0.7f * tw) * (0.45f + s.Depth * 0.55f) * (1 + Kick * 0.4f));
            r = s.Size * Math.Max(1, Math.Min(W, H) / 1080f) * (s.Depth >= 1 ? 1 + Energy * 0.3f : 1);
            float k = Peak * 0.35f * s.Depth;
            tail = new PointF(p.X + (p.X - cx) * k, p.Y + (p.Y - cy) * k);
        }

        // a shooting star: its head, its tail and how bright
        public void Place(Shot m, out PointF head, out PointF tail, out float alpha)
        {
            float k = m.Age / m.Life, vl = (float)Math.Sqrt(m.Vx * m.Vx + m.Vy * m.Vy);
            alpha = (float)Math.Sin(Math.PI * k);
            head = new PointF(m.X, m.Y);
            tail = new PointF(m.X - m.Vx / vl * m.Len, m.Y - m.Vy / vl * m.Len);
        }

        // 2:14 and MON, NOV 23 (in the PC's own way of writing time)
        public static string ClockText(DateTime now)
        {
            var f = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat;
            bool h12 = f.ShortTimePattern.Contains("h") || f.ShortTimePattern.Contains("t");
            return h12 ? now.ToString("h:mm") : now.ToString("HH:mm");
        }
        public static string DateText(DateTime now) => string.Join(" ", now.ToString("ddd, MMM d").ToUpperInvariant().ToCharArray());
    }
}
