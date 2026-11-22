using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;

namespace Aura
{
    // The full-screen lyrics backdrop: a deep-space scene in the cover's colors — nebula clouds, a band
    // of the galaxy, a planet's edge with a glowing atmosphere, three layers of drifting stars, shooting
    // stars, and a glow that breathes with how loud the song is right now.
    //
    // Everything heavy is painted once into pictures; each frame only moves and fades them.
    sealed class Space : IDisposable
    {
        Bitmap bg, nextBg;                    // the still part; a new one waiting for the warp to swap it in
        float warp;                           // 0..1: jumping to a new song (stars streak, a flash, a new sky)
        float flash;
        Size size; int forColor = -1; bool building;
        Color accent = Color.FromArgb(90, 120, 255);
        readonly Faded[] starSprites = new Faded[5];
        Faded glow, head;
        readonly List<Star> stars = new List<Star>();
        readonly List<Meteor> meteors = new List<Meteor>();
        readonly Random rnd = new Random();
        double nextMeteor = 4;
        float travel;                         // how far the stars have drifted
        public float Energy;                  // 0..1, eased loudness
        float kick;                           // a quick flash when the song hits harder
        SynchronizationContext ui;

        sealed class Star { public float X, Y, Size, Phase, Speed, Depth; public int Sprite; }
        sealed class Meteor { public float X, Y, Vx, Vy, Life, Age, Len; }

        public Space()
        {
            ui = SynchronizationContext.Current;
            var r = new Random(11);
            // far, middle and near layers: the nearer, the bigger, brighter and faster
            for (int i = 0; i < 150; i++) stars.Add(new Star { X = (float)r.NextDouble(), Y = (float)r.NextDouble(), Depth = 0.25f, Size = 0.6f + (float)r.NextDouble() * 0.6f, Phase = (float)(r.NextDouble() * 6.28), Speed = 0.5f + (float)r.NextDouble(), Sprite = r.Next(0, 2) });
            for (int i = 0; i < 70; i++) stars.Add(new Star { X = (float)r.NextDouble(), Y = (float)r.NextDouble(), Depth = 0.55f, Size = 0.8f + (float)r.NextDouble() * 0.7f, Phase = (float)(r.NextDouble() * 6.28), Speed = 0.4f + (float)r.NextDouble(), Sprite = r.Next(1, 4) });
            for (int i = 0; i < 18; i++) stars.Add(new Star { X = (float)r.NextDouble(), Y = (float)r.NextDouble(), Depth = 1f, Size = 0.9f + (float)r.NextDouble() * 0.8f, Phase = (float)(r.NextDouble() * 6.28), Speed = 0.3f + (float)r.NextDouble() * 0.6f, Sprite = r.Next(3, 5) });
        }

        public void Dispose()
        {
            bg?.Dispose(); nextBg?.Dispose(); glow?.Dispose(); head?.Dispose();
            foreach (var s in starSprites) s?.Dispose();
        }

        // ---------- the still backdrop, painted on a worker thread ----------
        public void Prepare(Size s, Color c)
        {
            if (s.Width < 2 || s.Height < 2) return;
            int key = c.ToArgb();
            if ((s == size && key == forColor) || building) return;
            bool first = bg == null || s != size;
            building = true; forColor = key; size = s; accent = c;
            MakeSprites(c);
            var w = new Thread(() =>
            {
                Bitmap made = null;
                try { made = Paint(s, c); } catch (Exception e) { App.Log("space: " + e.Message); }
                void Swap()
                {
                    building = false;
                    if (made == null) return;
                    if (first || bg == null) { bg?.Dispose(); bg = made; }
                    else { nextBg?.Dispose(); nextBg = made; if (warp < 0.05f) warp = 0.6f; }      // swapped in at the warp's brightest moment
                }
                if (ui != null) ui.Post(_ => Swap(), null); else Swap();
            }) { IsBackground = true, Name = "space", Priority = ThreadPriority.BelowNormal };
            w.Start();
            if (first && bg == null) { /* black until it's ready (a fraction of a second) */ }
        }

        static Bitmap Paint(Size s, Color c)
        {
            int W = s.Width, H = s.Height;
            var b = new Bitmap(W, H, PixelFormat.Format32bppPArgb);
            float hue = c.GetHue();
            var c1 = Theme.FromHsl(hue, 0.75f, 0.55f);
            var c2 = Theme.FromHsl(hue + 55, 0.7f, 0.5f);
            var c3 = Theme.FromHsl(hue - 45, 0.65f, 0.42f);
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                using (var br = new LinearGradientBrush(new Rectangle(0, 0, W, H), Color.FromArgb(3, 4, 12), Theme.Mix(Color.FromArgb(8, 5, 20), c3, 0.12f), 75f)) g.FillRectangle(br, 0, 0, W, H);

                // nebula and the galaxy band: soft noise, colored, painted small and scaled up (that's what makes it soft)
                int nw = Math.Max(16, W / 6), nh = Math.Max(9, H / 6);
                using (var neb = new Bitmap(nw, nh, PixelFormat.Format32bppArgb))
                {
                    var px = new int[nw * nh];
                    var noise = new Noise(hue.GetHashCode() ^ 0x5bd1e995);
                    // the galaxy band runs corner to corner
                    double bx = 0.95, by = -0.55, bl = Math.Sqrt(bx * bx + by * by); bx /= bl; by /= bl;
                    for (int y = 0; y < nh; y++)
                        for (int x = 0; x < nw; x++)
                        {
                            double u = (double)x / nh, v = (double)y / nh;     // square units
                            // warp the space a little for wispy shapes
                            double wx = u + 0.35 * noise.Fbm(u * 1.6 + 7.1, v * 1.6 + 2.3, 3), wy = v + 0.35 * noise.Fbm(u * 1.6 + 1.7, v * 1.6 + 9.2, 3);
                            double n1 = noise.Fbm(wx * 2.2, wy * 2.2, 5), n2 = noise.Fbm(wx * 3.1 + 5, wy * 3.1 + 3, 4), n3 = noise.Fbm(wx * 1.3 + 11, wy * 1.3 + 7, 4);
                            double d1 = Math.Pow(Math.Max(0, n1 - 0.33) / 0.67, 1.5), d2 = Math.Pow(Math.Max(0, n2 - 0.42) / 0.58, 1.6), d3 = Math.Pow(Math.Max(0, n3 - 0.38) / 0.62, 1.4);
                            // distance from the band's center line
                            double cx = (double)x / nw - 0.5, cy = (double)y / nh - 0.5;
                            double dist = Math.Abs(cx * by - cy * bx);
                            double band = Math.Exp(-dist * dist / 0.018) * (0.35 + 0.65 * noise.Fbm(u * 6 + 3, v * 6 + 1, 4));
                            double r = c1.R * d1 * 0.9 + c2.R * d2 * 0.7 + c3.R * d3 * 0.6 + 210 * band * 0.5;
                            double gg = c1.G * d1 * 0.9 + c2.G * d2 * 0.7 + c3.G * d3 * 0.6 + 215 * band * 0.5;
                            double bb = c1.B * d1 * 0.9 + c2.B * d2 * 0.7 + c3.B * d3 * 0.6 + 235 * band * 0.5;
                            double a = Math.Min(1, d1 * 0.95 + d2 * 0.7 + d3 * 0.55 + band * 0.5);
                            // keep the middle (where the words are) calmer
                            double calm = 1 - 0.45 * Math.Exp(-(cx * cx * 2.2 + cy * cy * 3.5) / 0.08);
                            a *= calm;
                            double m = Math.Max(r, Math.Max(gg, bb)); if (m > 255) { r *= 255 / m; gg *= 255 / m; bb *= 255 / m; }
                            int A = (int)(a * 235);
                            px[y * nw + x] = (A << 24) | ((int)r << 16) | ((int)gg << 8) | (int)bb;
                        }
                    var bd = neb.LockBits(new Rectangle(0, 0, nw, nh), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                    Marshal.Copy(px, 0, bd.Scan0, px.Length);
                    neb.UnlockBits(bd);
                    using (var ia = new ImageAttributes()) { ia.SetWrapMode(WrapMode.TileFlipXY); g.DrawImage(neb, new Rectangle(0, 0, W, H), 0, 0, nw, nh, GraphicsUnit.Pixel, ia); }
                }

                // a dust of faint stars, thicker along the band
                var r2 = new Random(hue.GetHashCode());
                int count = W * H / 900;
                for (int i = 0; i < count; i++)
                {
                    float x = (float)r2.NextDouble() * W, y = (float)r2.NextDouble() * H;
                    int a = 30 + (int)(Math.Pow(r2.NextDouble(), 3) * 200);
                    var tint = r2.NextDouble() < 0.15 ? Theme.Mix(Color.White, c2, 0.4f) : r2.NextDouble() < 0.1 ? Color.FromArgb(255, 225, 190) : Color.FromArgb(225, 232, 255);
                    float sz = r2.NextDouble() < 0.06 ? 1.8f : 1f;
                    using (var sb = new SolidBrush(Color.FromArgb(a, tint))) g.FillRectangle(sb, x, y, sz, sz);
                }

                // a planet's edge rising from the bottom right, lit from the upper left
                float pr = H * 0.62f, pcx = W * 0.86f, pcy = H + pr * 0.58f;
                var pRect = new RectangleF(pcx - pr, pcy - pr, pr * 2, pr * 2);
                // atmosphere: a smooth glow hugging the edge
                using (var halo = new GraphicsPath())
                {
                    float hr = pr * 1.13f;
                    halo.AddEllipse(pcx - hr, pcy - hr, hr * 2, hr * 2);
                    using (var hb = new PathGradientBrush(halo) { CenterColor = Color.FromArgb(150, Theme.Mix(c1, Color.White, 0.3f)), SurroundColors = new[] { Color.FromArgb(0, c1) } })
                    {
                        float edge = 1 - 1 / 1.13f;
                        hb.Blend = new Blend { Positions = new[] { 0f, edge * 0.5f, edge, 1f }, Factors = new[] { 0f, 0.25f, 1f, 1f } };
                        g.FillPath(hb, halo);
                    }
                }
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(pRect);
                    using (var pb = new PathGradientBrush(path)
                    {
                        CenterPoint = new PointF(pcx + pr * 0.35f, pcy + pr * 0.35f),
                        CenterColor = Color.FromArgb(255, 2, 2, 6),
                        SurroundColors = new[] { Theme.Mix(Color.FromArgb(16, 14, 30), c3, 0.35f) },
                        FocusScales = new PointF(0.75f, 0.75f)
                    }) g.FillPath(pb, path);
                    // the lit crescent
                    var st = g.Save();
                    g.SetClip(path);
                    using (var lit = new LinearGradientBrush(pRect, Color.FromArgb(120, Theme.Mix(c1, Color.White, 0.45f)), Color.FromArgb(0, c1), 45f))
                    {
                        var blend = new Blend { Positions = new[] { 0f, 0.12f, 0.35f, 1f }, Factors = new[] { 0f, 0.55f, 1f, 1f } };
                        lit.Blend = blend;
                        g.FillEllipse(lit, pRect);
                    }
                    g.Restore(st);
                    using (var rim = new Pen(Color.FromArgb(150, Theme.Mix(c1, Color.White, 0.6f)), H * 0.0025f)) g.DrawArc(rim, pRect, 190, 80);
                }

                // darker at the edges
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(-W * 0.25f, -H * 0.3f, W * 1.5f, H * 1.6f);
                    using (var pb = new PathGradientBrush(path) { CenterColor = Color.FromArgb(0, 0, 0, 0), SurroundColors = new[] { Color.FromArgb(200, 0, 0, 4) }, FocusScales = new PointF(0.55f, 0.5f) })
                        g.FillRectangle(pb, 0, 0, W, H);
                }
            }
            return b;
        }

        void MakeSprites(Color c)
        {
            for (int i = 0; i < starSprites.Length; i++) { starSprites[i]?.Dispose(); starSprites[i] = null; }
            int[] sizes = { 4, 6, 9, 15, 24 };
            for (int i = 0; i < sizes.Length; i++)
            {
                int n = (int)Math.Ceiling(Theme.F(sizes[i]));
                var b = new Bitmap(n, n, PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(b))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var p = new GraphicsPath())
                    {
                        p.AddEllipse(0, 0, n, n);
                        using (var pb = new PathGradientBrush(p) { CenterColor = Color.White, SurroundColors = new[] { Color.FromArgb(0, Theme.Mix(Color.White, c, 0.3f)) } })
                        {
                            pb.Blend = new Blend { Positions = new[] { 0f, 0.6f, 0.85f, 1f }, Factors = new[] { 0f, 0.08f, 0.5f, 1f } };
                            g.FillEllipse(pb, 0, 0, n, n);
                        }
                    }
                    if (i >= 3)     // the big ones get a little cross of light
                        using (var pen = new Pen(Color.FromArgb(110, 255, 255, 255), 1))
                        { g.DrawLine(pen, n / 2f, 0, n / 2f, n); g.DrawLine(pen, 0, n / 2f, n, n / 2f); }
                }
                starSprites[i] = new Faded(b);
            }
            glow?.Dispose();
            var gb = new Bitmap(128, 128, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(gb))
            using (var p = new GraphicsPath())
            {
                p.AddEllipse(0, 0, 128, 128);
                using (var pb = new PathGradientBrush(p) { CenterColor = Color.FromArgb(255, Theme.Mix(c, Color.White, 0.25f)), SurroundColors = new[] { Color.FromArgb(0, c) } })
                {
                    pb.Blend = new Blend { Positions = new[] { 0f, 0.5f, 1f }, Factors = new[] { 0f, 0.35f, 1f } };
                    g.FillEllipse(pb, 0, 0, 128, 128);
                }
            }
            glow = new Faded(gb);
            head?.Dispose();
            head = new Faded(new Bitmap(starSprites[3].Source));
        }

        // a soft round glow (for behind covers and the like)
        public void GlowAt(Graphics g, RectangleF r, float alpha)
        {
            var im = g.InterpolationMode; g.InterpolationMode = InterpolationMode.Bilinear;
            Put(g, glow, Math.Min(1, alpha), r.X, r.Y, r.Width, r.Height);
            g.InterpolationMode = im;
        }

        // a new song: jump through space to its sky
        public void Warp() { warp = 1; }

        static void Put(Graphics g, Faded f, float alpha, float x, float y, float w, float h)
        {
            var b = f?.At(alpha);
            if (b != null && w >= 1 && h >= 1) g.DrawImage(b, x, y, w, h);
        }

        // ---------- every frame ----------
        // loud: the song's loudness right now (0..1, -1 when unknown); playing: whether music is playing
        public void Draw(Graphics g, Rectangle r, float dt, double time, float loud, bool playing)
        {
            var cq = g.CompositingQuality; var im = g.InterpolationMode; var pm = g.PixelOffsetMode; var sm = g.SmoothingMode;
            g.CompositingQuality = CompositingQuality.HighSpeed;
            g.InterpolationMode = InterpolationMode.Bilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            // the warp: up fast, down slowly; the new sky comes in at the bright peak
            warp = Math.Max(0, warp - dt / 1.6f);
            float peak = warp > 0 ? (float)Math.Sin(Math.PI * (1 - warp)) : 0;      // 0 -> 1 -> 0 over the warp
            if (nextBg != null && (warp < 0.55f || warp <= 0))
            {
                bg?.Dispose(); bg = nextBg; nextBg = null;
            }
            if (bg != null) g.DrawImageUnscaled(bg, r.X, r.Y);
            else using (var b = new SolidBrush(Color.FromArgb(3, 4, 12))) g.FillRectangle(b, r);

            // the song's loudness: quick to rise, slow to fall; a jump up is a "kick"
            if (loud >= 0)
            {
                if (loud > Energy + 0.12f) kick = Math.Min(1, kick + (loud - Energy) * 1.6f);
                Energy += (loud - Energy) * FrameClock.Ease(dt, loud > Energy ? 14 : 3);
            }
            else Energy += (0.3f - Energy) * FrameClock.Ease(dt, 1);
            if (!playing) Energy += (0 - Energy) * FrameClock.Ease(dt, 2);
            kick *= (float)Math.Exp(-dt * 5);

            // a glow that breathes with the music, behind the words
            {
                float a = 0.10f + Energy * 0.28f + kick * 0.22f + peak * 0.3f;
                float gw = r.Width * (0.75f + Energy * 0.12f), gh = r.Height * (0.55f + Energy * 0.1f);
                Put(g, glow, Math.Min(1, a), r.X + (r.Width - gw) / 2, r.Y + r.Height * 0.42f - gh / 2, gw, gh);
            }

            // stars drifting past, faster when the music is loud; streaks during a warp
            float speed = (playing ? 1f : 0.25f) * (1 + Energy * 1.8f + kick * 1.5f) + peak * 40;
            travel += dt * speed;
            bool streak = peak > 0.15f;
            Pen pen = streak ? new Pen(Color.White, 1) { StartCap = LineCap.Round, EndCap = LineCap.Round } : null;
            if (streak) g.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (var s in stars)
            {
                float x = ((s.X - travel * 0.012f * s.Depth * s.Speed) % 1 + 1) % 1 * r.Width + r.X;
                float y = ((s.Y + travel * 0.002f * s.Depth) % 1 + 1) % 1 * r.Height + r.Y;
                float tw = 0.55f + 0.45f * (float)Math.Sin(time * (1.1 + s.Speed) + s.Phase);
                float alpha = Math.Min(1, (0.35f + 0.65f * tw) * (0.45f + s.Depth * 0.55f) * (1 + kick * 0.5f));
                var sp = starSprites[s.Sprite];
                if (sp == null) continue;
                float sz = sp.Width * s.Size * (1 + (s.Depth >= 1 ? Energy * 0.35f : 0));
                if (streak)
                {
                    float len = peak * r.Width * 0.12f * s.Depth * s.Speed;
                    pen.Color = Color.FromArgb((int)(Math.Min(1, alpha + 0.3f) * 230), 235, 240, 255);
                    pen.Width = Math.Max(1, sz * 0.35f);
                    g.DrawLine(pen, x, y, x + len, y - len * 0.17f);
                }
                else Put(g, sp, alpha, x - sz / 2, y - sz / 2, sz, sz);
            }
            pen?.Dispose();
            g.SmoothingMode = sm;

            // now and then a shooting star
            nextMeteor -= dt;
            if (nextMeteor <= 0)
            {
                nextMeteor = 5 + rnd.NextDouble() * 12 - Energy * 4;
                float ang = (float)(Math.PI * (0.12 + rnd.NextDouble() * 0.18));
                float v = r.Height * (0.9f + (float)rnd.NextDouble() * 0.6f);
                meteors.Add(new Meteor { X = r.X + (float)rnd.NextDouble() * r.Width * 0.8f, Y = r.Y + (float)rnd.NextDouble() * r.Height * 0.35f, Vx = (float)Math.Cos(ang) * v, Vy = (float)Math.Sin(ang) * v, Life = 0.7f + (float)rnd.NextDouble() * 0.5f, Len = r.Height * 0.16f });
            }
            for (int i = meteors.Count - 1; i >= 0; i--)
            {
                var m = meteors[i];
                m.Age += dt; m.X += m.Vx * dt; m.Y += m.Vy * dt;
                if (m.Age >= m.Life) { meteors.RemoveAt(i); continue; }
                float k = m.Age / m.Life, fade = (float)Math.Sin(Math.PI * k);
                float vl = (float)Math.Sqrt(m.Vx * m.Vx + m.Vy * m.Vy);
                float tx = m.X - m.Vx / vl * m.Len, ty = m.Y - m.Vy / vl * m.Len;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var br = new LinearGradientBrush(new PointF(tx - 1, ty - 1), new PointF(m.X + 1, m.Y + 1), Color.FromArgb(0, 255, 255, 255), Color.FromArgb((int)(220 * fade), 255, 255, 255)))
                using (var mp = new Pen(br, Theme.F(2.2f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(mp, tx, ty, m.X, m.Y);
                g.SmoothingMode = sm;
                float hs = head.Width * 0.9f;
                Put(g, head, fade, m.X - hs / 2, m.Y - hs / 2, hs, hs);
            }

            // the warp's flash
            flash = peak * peak * 0.55f;
            if (flash > 0.01f)
                using (var b = new SolidBrush(Color.FromArgb((int)(flash * 255), Theme.Mix(accent, Color.White, 0.6f)))) g.FillRectangle(b, r);

            g.CompositingQuality = cq; g.InterpolationMode = im; g.PixelOffsetMode = pm; g.SmoothingMode = sm;
        }

        // smooth noise (value noise, several layers), for the clouds
        sealed class Noise
        {
            readonly int[] perm = new int[512];
            readonly double[] val = new double[256];
            public Noise(int seed)
            {
                var r = new Random(seed);
                var p = new int[256];
                for (int i = 0; i < 256; i++) { p[i] = i; val[i] = r.NextDouble(); }
                for (int i = 255; i > 0; i--) { int j = r.Next(i + 1); var t = p[i]; p[i] = p[j]; p[j] = t; }
                for (int i = 0; i < 512; i++) perm[i] = p[i & 255];
            }
            double V(int x, int y) => val[perm[(perm[x & 255] + y) & 511]];
            static double S(double t) => t * t * (3 - 2 * t);
            public double At(double x, double y)
            {
                int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
                double fx = S(x - xi), fy = S(y - yi);
                double a = V(xi, yi), b = V(xi + 1, yi), c = V(xi, yi + 1), d = V(xi + 1, yi + 1);
                return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
            }
            public double Fbm(double x, double y, int oct)
            {
                double sum = 0, amp = 0.5, norm = 0, f = 1;
                for (int i = 0; i < oct; i++) { sum += At(x * f, y * f) * amp; norm += amp; amp *= 0.5; f *= 2.03; }
                return sum / norm;
            }
        }
    }
}
