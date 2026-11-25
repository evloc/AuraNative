using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;

namespace Aura
{
    static class Theme
    {
        public static float S = 1f;                       // screen scale (1 at 100%, 1.5 at 150%)
        public static int P(float v) => (int)Math.Round(v * S);
        public static float F(float v) => v * S;

        public static Color Bg = Color.FromArgb(9, 11, 17);
        public static Color Side = Color.FromArgb(5, 7, 11);
        public static Color Bar = Color.FromArgb(11, 14, 21);
        public static Color Surface = Color.FromArgb(18, 23, 33);
        public static Color Surface2 = Color.FromArgb(27, 33, 46);
        public static Color Hair = Color.FromArgb(30, 37, 50);
        public static Color Text = Color.FromArgb(244, 246, 250);
        public static Color Sub = Color.FromArgb(163, 172, 186);
        public static Color Faint = Color.FromArgb(107, 117, 133);

        // ---------- themes ----------
        public sealed class Palette
        {
            public string Name, Accent;
            public Color Bg, Side, Bar, Surface, Surface2, Hair, Text, Sub, Faint;
            public bool IsLight;
        }

        static Color C(int r, int g, int b) => Color.FromArgb(r, g, b);
        public static readonly Palette[] Palettes =
        {
            new Palette { Name = "Midnight", Accent = "#1688FF", Bg = C(9, 11, 17), Side = C(5, 7, 11), Bar = C(11, 14, 21), Surface = C(18, 23, 33), Surface2 = C(27, 33, 46), Hair = C(30, 37, 50), Text = C(244, 246, 250), Sub = C(163, 172, 186), Faint = C(107, 117, 133) },
            new Palette { Name = "Neon", Accent = "#F72BD6", Bg = C(13, 8, 24), Side = C(8, 5, 16), Bar = C(16, 10, 30), Surface = C(27, 18, 46), Surface2 = C(40, 27, 66), Hair = C(46, 33, 72), Text = C(248, 244, 255), Sub = C(178, 164, 204), Faint = C(120, 106, 150) },
            new Palette { Name = "Sunset", Accent = "#FF6B35", Bg = C(21, 12, 13), Side = C(14, 8, 9), Bar = C(25, 14, 15), Surface = C(39, 23, 23), Surface2 = C(56, 33, 31), Hair = C(62, 39, 37), Text = C(255, 246, 242), Sub = C(205, 175, 166), Faint = C(150, 118, 110) },
            new Palette { Name = "Ocean", Accent = "#14B8A6", Bg = C(6, 16, 20), Side = C(4, 11, 14), Bar = C(8, 20, 25), Surface = C(14, 32, 38), Surface2 = C(21, 45, 53), Hair = C(26, 52, 60), Text = C(240, 250, 250), Sub = C(156, 186, 190), Faint = C(100, 130, 136) },
            new Palette { Name = "Light", Accent = "#1688FF", Bg = C(246, 247, 250), Side = C(236, 238, 243), Bar = C(250, 251, 253), Surface = C(255, 255, 255), Surface2 = C(232, 235, 241), Hair = C(222, 226, 233), Text = C(20, 24, 33), Sub = C(84, 93, 108), Faint = C(128, 137, 150), IsLight = true },
            new Palette { Name = "Mono", Accent = "#D4D4D8", Bg = C(12, 12, 13), Side = C(7, 7, 8), Bar = C(16, 16, 17), Surface = C(26, 26, 28), Surface2 = C(38, 38, 41), Hair = C(44, 44, 47), Text = C(245, 245, 245), Sub = C(170, 170, 175), Faint = C(115, 115, 120) },
        };
        public static string ThemeName = "Midnight";

        // "Match Windows": Light when Windows apps are set to light, Midnight otherwise
        public static bool WindowsIsLight()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return k?.GetValue("AppsUseLightTheme") is int v && v == 1;
            }
            catch { return false; }
        }
        public static bool IsLight;
        // lines and hover tints: white on dark themes, black on the light one
        public static Color Ink => IsLight ? Color.Black : Color.White;

        // drawing something that always looks dark (Now Playing over its cover backdrop), even in the light theme
        sealed class Restore : IDisposable
        {
            readonly Color[] c; readonly bool light;
            public Restore() { c = new[] { Bg, Side, Bar, Surface, Surface2, Hair, Text, Sub, Faint }; light = IsLight; }
            public void Dispose() { Bg = c[0]; Side = c[1]; Bar = c[2]; Surface = c[3]; Surface2 = c[4]; Hair = c[5]; Text = c[6]; Sub = c[7]; Faint = c[8]; IsLight = light; }
        }
        public static IDisposable Dark()
        {
            if (!IsLight) return null;
            var r = new Restore();
            var p = Palettes[0];
            Bg = p.Bg; Side = p.Side; Bar = p.Bar; Surface = p.Surface; Surface2 = p.Surface2; Hair = p.Hair; Text = p.Text; Sub = p.Sub; Faint = p.Faint; IsLight = false;
            return r;
        }

        public static void Apply(string name)
        {
            if (name == "Match Windows") name = WindowsIsLight() ? "Light" : "Midnight";
            var p = Palettes.FirstOrDefault(x => x.Name == name) ?? Palettes[0];
            IsLight = p.IsLight;
            ThemeName = p.Name;
            Bg = p.Bg; Side = p.Side; Bar = p.Bar; Surface = p.Surface; Surface2 = p.Surface2; Hair = p.Hair; Text = p.Text; Sub = p.Sub; Faint = p.Faint;
        }

        public static Color AccentBase = Color.FromArgb(22, 136, 255);
        public static Color Accent = AccentBase;
        static Color from, to;
        static int step = -1;
        const int Steps = 14;
        static System.Windows.Forms.Timer anim;
        public static event Action Changed;

        // fade to a new accent color over about half a second
        public static void SetAccent(Color c)
        {
            if (c.IsEmpty) c = AccentBase;
            if (c.ToArgb() == Accent.ToArgb() && step < 0) return;
            from = Accent; to = c; step = 0;
            if (anim == null)
            {
                anim = new System.Windows.Forms.Timer { Interval = 33 };
                anim.Tick += (s, e) =>
                {
                    if (step < 0) { anim.Stop(); return; }
                    step++;
                    float k = Math.Min(1f, step / (float)Steps);
                    k = 1 - (1 - k) * (1 - k);
                    Accent = Mix(from, to, k);
                    if (step >= Steps) { Accent = to; step = -1; anim.Stop(); }
                    Changed?.Invoke();
                };
            }
            anim.Start();
        }

        // where the accent is heading (it glides there over a moment)
        public static Color AccentTarget => step >= 0 ? to : Accent;

        public static Color Mix(Color a, Color b, float k) =>
            Color.FromArgb((int)(a.A + (b.A - a.A) * k), (int)(a.R + (b.R - a.R) * k), (int)(a.G + (b.G - a.G) * k), (int)(a.B + (b.B - a.B) * k));
        public static Color Alpha(Color c, int a) => Color.FromArgb(Math.Max(0, Math.Min(255, a)), c.R, c.G, c.B);
        public static Color AccentText => Mix(Accent, Color.White, 0.25f);
        // text and icons on top of the accent color: dark when the accent is very light
        public static Color OnAccent => Accent.GetBrightness() > 0.72f ? Color.FromArgb(12, 14, 18) : Color.White;

        public static Color FromHsl(float h, float s, float l)
        {
            h = ((h % 360) + 360) % 360 / 360f;
            float q = l < 0.5f ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
            float Hue(float t)
            {
                if (t < 0) t += 1; if (t > 1) t -= 1;
                if (t < 1f / 6) return p + (q - p) * 6 * t;
                if (t < 0.5f) return q;
                if (t < 2f / 3) return p + (q - p) * (2f / 3 - t) * 6;
                return p;
            }
            return Color.FromArgb((int)(Hue(h + 1f / 3) * 255), (int)(Hue(h) * 255), (int)(Hue(h - 1f / 3) * 255));
        }

        public static Color Parse(string hex, Color fallback)
        {
            try { return ColorTranslator.FromHtml(hex); } catch { return fallback; }
        }

        // ---------- fonts ----------
        static string family;
        static readonly Dictionary<(float, FontStyle, bool), Font> fonts = new Dictionary<(float, FontStyle, bool), Font>();

        public static string Family
        {
            get
            {
                if (family != null) return family;
                using (var ifc = new InstalledFontCollection())
                {
                    var names = new HashSet<string>(ifc.Families.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
                    family = new[] { "Segoe UI Variable Text", "Segoe UI", "Inter", "DejaVu Sans" }.FirstOrDefault(names.Contains) ?? FontFamily.GenericSansSerif.Name;
                }
                return family;
            }
        }

        // size in pixels at 100%; semi = semibold
        public static int FontCount => fonts.Count;
        public static Font Font(float px, FontStyle style = FontStyle.Regular, bool semi = false)
        {
            px = (float)Math.Round(px * 2) / 2;        // sizes worked out from the screen land on half steps, so the font list can't keep growing
            var key = (px, style, semi);
            if (fonts.TryGetValue(key, out var f)) return f;
            string fam = Family;
            if (semi)
            {
                var semiName = fam == "Segoe UI Variable Text" ? "Segoe UI Variable Text Semibold" : fam == "Segoe UI" ? "Segoe UI Semibold" : null;
                try { if (semiName != null) { using (var ff = new FontFamily(semiName)) fam = semiName; } else style |= FontStyle.Bold; }
                catch { style |= FontStyle.Bold; }
            }
            f = new Font(fam, px * S, style, GraphicsUnit.Pixel);
            fonts[key] = f;
            return f;
        }

        public static readonly StringFormat Left = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter, LineAlignment = StringAlignment.Center };
        public static readonly StringFormat Right = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter, LineAlignment = StringAlignment.Center, Alignment = StringAlignment.Far };
        public static readonly StringFormat Center = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter, LineAlignment = StringAlignment.Center, Alignment = StringAlignment.Center };
        public static readonly StringFormat Wrap = new StringFormat { Trimming = StringTrimming.EllipsisWord };
        public static readonly StringFormat TopLeft = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip | StringFormatFlags.MeasureTrailingSpaces };

        public static void Setup(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }

        public static void Write(Graphics g, string s, Font f, Color c, RectangleF r, StringFormat fmt = null)
        {
            if (string.IsNullOrEmpty(s) || r.Width <= 1) return;
            using (var b = new SolidBrush(c)) g.DrawString(s, f, b, r, fmt ?? Left);
        }

        public static float Measure(Graphics g, string s, Font f) => string.IsNullOrEmpty(s) ? 0 : g.MeasureString(s, f, 10000, StringFormat.GenericTypographic).Width;

        // ---------- shapes ----------
        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            rad = Math.Min(rad, Math.Min(r.Width, r.Height) / 2);
            if (rad <= 0.5f) { p.AddRectangle(r); return p; }
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Fill(Graphics g, Color c, RectangleF r, float rad)
        {
            using (var b = new SolidBrush(c)) using (var p = Round(r, rad)) g.FillPath(b, p);
        }

        public static void Stroke(Graphics g, Color c, RectangleF r, float rad, float w = 1)
        {
            using (var pen = new Pen(c, w)) using (var p = Round(r, rad)) g.DrawPath(pen, p);
        }

        // covers are scaled and rounded once per size, then just copied to the screen (much smoother scrolling)
        // Kept within a budget (oldest dropped first) and freed right away when a cover is let go: these are
        // native pictures the garbage collector doesn't see, so they must never wait for it.
        static readonly Dictionary<Image, Dictionary<(int, int, int, bool), Bitmap>> scaled = new Dictionary<Image, Dictionary<(int, int, int, bool), Bitmap>>();
        static readonly LinkedList<Image> scaledOrder = new LinkedList<Image>();
        static readonly Dictionary<Image, LinkedListNode<Image>> scaledNode = new Dictionary<Image, LinkedListNode<Image>>();
        static long scaledPixels;
        const long ScaledBudget = 40_000_000;          // about 160 MB of ready-made covers
        public static long ScaledBytes => scaledPixels * 4;
        public static int ScaledCount { get { lock (scaled) return scaled.Values.Sum(d => d.Count); } }

        public static void ForgetScaled(Image img)
        {
            lock (scaled)
            {
                if (!scaled.TryGetValue(img, out var d)) return;
                foreach (var x in d.Values) { scaledPixels -= (long)x.Width * x.Height; x.Dispose(); }
                scaled.Remove(img);
                if (scaledNode.TryGetValue(img, out var n)) { scaledOrder.Remove(n); scaledNode.Remove(img); }
            }
        }

        static Dictionary<(int, int, int, bool), Bitmap> ScaledFor(Image img)
        {
            if (!scaled.TryGetValue(img, out var d)) { d = new Dictionary<(int, int, int, bool), Bitmap>(); scaled[img] = d; }
            if (scaledNode.TryGetValue(img, out var n)) { scaledOrder.Remove(n); scaledOrder.AddFirst(n); }
            else scaledNode[img] = scaledOrder.AddFirst(img);
            return d;
        }

        static void TrimScaled(Image keep)
        {
            while (scaledPixels > ScaledBudget && scaledOrder.Count > 1)
            {
                var last = scaledOrder.Last.Value;
                if (last == keep) break;
                scaledOrder.RemoveLast(); scaledNode.Remove(last);
                if (scaled.TryGetValue(last, out var d)) { foreach (var x in d.Values) { scaledPixels -= (long)x.Width * x.Height; x.Dispose(); } scaled.Remove(last); }
            }
        }

        public static void Image(Graphics g, Image img, RectangleF r, float rad, bool circle = false)
        {
            if (img == null || r.Width < 1 || r.Height < 1) return;
            int w = (int)Math.Round(r.Width), h = (int)Math.Round(r.Height), rr = (int)Math.Round(rad);
            Bitmap b = null;
            if (w <= 1600 && h <= 1600)
            {
                lock (scaled)
                {
                    var d = ScaledFor(img);
                    var key = (w, h, rr, circle);
                    if (!d.TryGetValue(key, out b))
                    {
                        if (d.Count >= 6) { foreach (var x in d.Values) { scaledPixels -= (long)x.Width * x.Height; x.Dispose(); } d.Clear(); }
                        b = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                        using (var bg = Graphics.FromImage(b))
                        {
                            bg.SmoothingMode = SmoothingMode.AntiAlias;
                            bg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            bg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            using (var src = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
                            {
                                using (var sg = Graphics.FromImage(src))
                                {
                                    sg.InterpolationMode = InterpolationMode.HighQualityBicubic; sg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                                    using (var ia = new System.Drawing.Imaging.ImageAttributes()) { ia.SetWrapMode(WrapMode.TileFlipXY); sg.DrawImage(img, new Rectangle(0, 0, w, h), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, ia); }
                                }
                                using (var tb = new TextureBrush(src))
                                using (var p = circle ? Ellipse(new RectangleF(0, 0, w, h)) : Round(new RectangleF(0, 0, w, h), rr))
                                    bg.FillPath(tb, p);
                            }
                        }
                        d[key] = b;
                        scaledPixels += (long)w * h;
                        TrimScaled(img);
                    }
                }
            }
            if (b != null)
            {
                var im = g.InterpolationMode; var pm = g.PixelOffsetMode;
                g.InterpolationMode = InterpolationMode.Bilinear; g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(b, new RectangleF(r.X, r.Y, w, h));
                g.InterpolationMode = im; g.PixelOffsetMode = pm;
                return;
            }
            var state = g.Save();
            using (var p = circle ? Ellipse(r) : Round(r, rad))
            {
                g.SetClip(p, CombineMode.Intersect);
                g.DrawImage(img, r);
            }
            g.Restore(state);
        }

        static GraphicsPath Ellipse(RectangleF r) { var p = new GraphicsPath(); p.AddEllipse(r); return p; }

        // the placeholder for a song with no cover: a soft gradient with a note on it
        public static void NoArt(Graphics g, RectangleF r, float rad, long seed, bool circle = false, string icon = "note")
        {
            float h = (seed * 47) % 360;
            using (var p = circle ? Ellipse(r) : Round(r, rad))
            using (var b = new LinearGradientBrush(new RectangleF(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), FromHsl(h, 0.35f, 0.22f), FromHsl(h + 40, 0.35f, 0.13f), 45f))
                g.FillPath(b, p);
            float s = r.Width * 0.38f;
            Icons.Draw(g, icon, new RectangleF(r.X + (r.Width - s) / 2, r.Y + (r.Height - s) / 2, s, s), Alpha(Ink, 110));
        }

        public static string Time(double sec)
        {
            if (double.IsNaN(sec) || sec < 0) sec = 0;
            var t = TimeSpan.FromSeconds(Math.Floor(sec));
            return t.TotalHours >= 1 ? string.Format("{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds) : string.Format("{0}:{1:00}", t.Minutes, t.Seconds);
        }

        public static string Long(double sec)
        {
            var t = TimeSpan.FromSeconds(sec);
            if (t.TotalHours >= 1) return string.Format("{0} hr {1} min", (int)t.TotalHours, t.Minutes);
            return string.Format("{0} min", Math.Max(1, (int)Math.Round(t.TotalMinutes)));
        }

        public static string Count(int n, string one, string many = null) => n.ToString("N0") + " " + (n == 1 ? one : many ?? one + "s");

        public static string Ago(double unix)
        {
            if (unix <= 0) return "";
            var d = new DateTime(1970, 1, 1).AddSeconds(unix).ToLocalTime();
            var span = DateTime.Now - d;
            if (span.TotalDays < 1) return "Today";
            if (span.TotalDays < 2) return "Yesterday";
            if (span.TotalDays < 7) return (int)span.TotalDays + " days ago";
            return d.ToString(d.Year == DateTime.Now.Year ? "MMM d" : "MMM d, yyyy");
        }
    }

    // Icons drawn as shapes, so they're sharp at any size and need no image files.
    static class Icons
    {
        public static void Draw(Graphics g, string name, RectangleF r, Color c, bool filled = false)
        {
            var state = g.Save();
            g.TranslateTransform(r.X, r.Y);
            g.ScaleTransform(r.Width / 24f, r.Height / 24f);
            float w = 1.9f;
            using (var pen = new Pen(c, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var br = new SolidBrush(c))
            {
                switch (name)
                {
                    case "play":
                        using (var p = Poly(new[] { 7.5f, 4.5f, 19.5f, 12f, 7.5f, 19.5f })) { g.FillPath(br, p); pen.Width = 1.5f; g.DrawPath(pen, p); }
                        break;
                    case "pause":
                        using (var p = Theme.Round(new RectangleF(6, 4.5f, 4.2f, 15), 1.2f)) g.FillPath(br, p);
                        using (var p = Theme.Round(new RectangleF(13.8f, 4.5f, 4.2f, 15), 1.2f)) g.FillPath(br, p);
                        break;
                    case "next":
                        using (var p = Poly(new[] { 5.5f, 5.5f, 15f, 12f, 5.5f, 18.5f })) { g.FillPath(br, p); pen.Width = 1.4f; g.DrawPath(pen, p); }
                        using (var p = Theme.Round(new RectangleF(16.2f, 5f, 2.6f, 14), 1.2f)) g.FillPath(br, p);
                        break;
                    case "prev":
                        using (var p = Poly(new[] { 18.5f, 5.5f, 9f, 12f, 18.5f, 18.5f })) { g.FillPath(br, p); pen.Width = 1.4f; g.DrawPath(pen, p); }
                        using (var p = Theme.Round(new RectangleF(5.2f, 5f, 2.6f, 14), 1.2f)) g.FillPath(br, p);
                        break;
                    case "shuffle":
                        g.DrawBezier(pen, 3, 7, 9, 7, 12, 17, 19, 17);
                        g.DrawBezier(pen, 3, 17, 9, 17, 12, 7, 19, 7);
                        g.DrawLines(pen, new[] { new PointF(16.5f, 4.5f), new PointF(19.5f, 7), new PointF(16.5f, 9.5f) });
                        g.DrawLines(pen, new[] { new PointF(16.5f, 14.5f), new PointF(19.5f, 17), new PointF(16.5f, 19.5f) });
                        break;
                    case "repeat":
                    case "repeat1":
                        g.DrawLines(pen, new[] { new PointF(17, 4), new PointF(20, 7), new PointF(17, 10) });
                        using (var p = new GraphicsPath())
                        {
                            p.AddLine(20, 7, 8, 7); p.AddArc(4, 7, 8, 8, 270, -90); g.DrawPath(pen, p);
                        }
                        g.DrawLines(pen, new[] { new PointF(7, 20), new PointF(4, 17), new PointF(7, 14) });
                        using (var p = new GraphicsPath())
                        {
                            p.AddLine(4, 17, 16, 17); p.AddArc(12, 9, 8, 8, 90, -90); g.DrawPath(pen, p);
                        }
                        if (name == "repeat1")
                        {
                            using (var f = new Font(FontFamily.GenericSansSerif, 7.5f, FontStyle.Bold, GraphicsUnit.Pixel))
                            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                                g.DrawString("1", f, br, new RectangleF(7, 7.5f, 10, 10), sf);
                        }
                        break;
                    case "heart":
                        using (var p = Heart()) { if (filled) g.FillPath(br, p); g.DrawPath(pen, p); }
                        break;
                    case "volume":
                    case "volume1":
                    case "mute":
                        using (var p = Poly(new[] { 3.5f, 9.5f, 7.5f, 9.5f, 12.5f, 5f, 12.5f, 19f, 7.5f, 14.5f, 3.5f, 14.5f })) { g.FillPath(br, p); pen.Width = 1.4f; g.DrawPath(pen, p); pen.Width = w; }
                        if (name == "mute") { g.DrawLine(pen, 16, 9.5f, 21, 14.5f); g.DrawLine(pen, 21, 9.5f, 16, 14.5f); }
                        else
                        {
                            g.DrawArc(pen, 11.5f, 8.5f, 6, 7, -55, 110);
                            if (name == "volume") g.DrawArc(pen, 11, 5, 10.5f, 14, -55, 110);
                        }
                        break;
                    case "search":
                        g.DrawEllipse(pen, 4, 4, 12.5f, 12.5f);
                        g.DrawLine(pen, 14.5f, 14.5f, 20, 20);
                        break;
                    case "home":
                        g.DrawLines(pen, new[] { new PointF(3.5f, 11.5f), new PointF(12, 4), new PointF(20.5f, 11.5f) });
                        g.DrawLines(pen, new[] { new PointF(6, 9.8f), new PointF(6, 20), new PointF(18, 20), new PointF(18, 9.8f) });
                        if (filled) using (var p = Poly(new[] { 6.5f, 10f, 12, 5.2f, 17.5f, 10f, 17.5f, 19.5f, 6.5f, 19.5f })) g.FillPath(br, p);
                        break;
                    case "note":
                        g.FillEllipse(br, 4.5f, 14.5f, 6, 5);
                        g.FillEllipse(br, 13.5f, 12.5f, 6, 5);
                        g.DrawLine(pen, 10, 17, 10, 6);
                        g.DrawLine(pen, 19, 15, 19, 4);
                        pen.Width = 2.6f; g.DrawLine(pen, 10, 6, 19, 4); g.DrawLine(pen, 10, 8.6f, 19, 6.6f);
                        break;
                    case "songs":
                        g.DrawLine(pen, 3.5f, 6, 13, 6); g.DrawLine(pen, 3.5f, 11, 13, 11); g.DrawLine(pen, 3.5f, 16, 9, 16);
                        g.FillEllipse(br, 12, 15.5f, 5.5f, 4.5f);
                        g.DrawLine(pen, 17, 17.5f, 17, 6); g.DrawLine(pen, 17, 6, 20.5f, 7.5f);
                        break;
                    case "artist":
                        g.DrawEllipse(pen, 8, 3.5f, 8, 8);
                        g.DrawArc(pen, 4.5f, 13.5f, 15, 13, 200, 140);
                        break;
                    case "album":
                        g.DrawEllipse(pen, 3.5f, 3.5f, 17, 17);
                        g.DrawEllipse(pen, 9.5f, 9.5f, 5, 5);
                        break;
                    case "playlist":
                        g.DrawLine(pen, 3.5f, 6, 15, 6); g.DrawLine(pen, 3.5f, 11, 15, 11); g.DrawLine(pen, 3.5f, 16, 10, 16);
                        using (var p = Poly(new[] { 14f, 13.5f, 20.5f, 17f, 14f, 20.5f })) g.FillPath(br, p);
                        break;
                    case "queue":
                        g.DrawLine(pen, 3.5f, 6, 20.5f, 6); g.DrawLine(pen, 9, 11.5f, 20.5f, 11.5f); g.DrawLine(pen, 9, 17, 20.5f, 17);
                        using (var p = Poly(new[] { 3.5f, 9.5f, 7f, 12.2f, 3.5f, 15f })) g.FillPath(br, p);
                        break;
                    case "settings":
                        for (int i = 0; i < 8; i++)
                        {
                            double a = i * Math.PI / 4;
                            g.DrawLine(pen, 12 + (float)Math.Cos(a) * 7.2f, 12 + (float)Math.Sin(a) * 7.2f, 12 + (float)Math.Cos(a) * 9.6f, 12 + (float)Math.Sin(a) * 9.6f);
                        }
                        g.DrawEllipse(pen, 5.2f, 5.2f, 13.6f, 13.6f);
                        g.DrawEllipse(pen, 9.3f, 9.3f, 5.4f, 5.4f);
                        break;
                    case "plus":
                        g.DrawLine(pen, 12, 5, 12, 19); g.DrawLine(pen, 5, 12, 19, 12);
                        break;
                    case "close":
                        g.DrawLine(pen, 6, 6, 18, 18); g.DrawLine(pen, 18, 6, 6, 18);
                        break;
                    case "more":
                        g.FillEllipse(br, 4, 10.5f, 3.2f, 3.2f); g.FillEllipse(br, 10.4f, 10.5f, 3.2f, 3.2f); g.FillEllipse(br, 16.8f, 10.5f, 3.2f, 3.2f);
                        break;
                    case "back":
                        g.DrawLines(pen, new[] { new PointF(14.5f, 5.5f), new PointF(8, 12), new PointF(14.5f, 18.5f) });
                        break;
                    case "forward":
                        g.DrawLines(pen, new[] { new PointF(9.5f, 5.5f), new PointF(16, 12), new PointF(9.5f, 18.5f) });
                        break;
                    case "folder":
                        g.DrawLines(pen, new[] { new PointF(3.5f, 18.5f), new PointF(3.5f, 5.5f), new PointF(9.5f, 5.5f), new PointF(11.5f, 8), new PointF(20.5f, 8), new PointF(20.5f, 18.5f), new PointF(3.5f, 18.5f) });
                        break;
                    case "refresh":
                        g.DrawArc(pen, 4.5f, 4.5f, 15, 15, -60, 300);
                        g.DrawLines(pen, new[] { new PointF(16.5f, 3.5f), new PointF(19.6f, 5.7f), new PointF(17.4f, 8.8f) });
                        break;
                    case "clock":
                        g.DrawEllipse(pen, 3.5f, 3.5f, 17, 17);
                        g.DrawLines(pen, new[] { new PointF(12, 7.5f), new PointF(12, 12), new PointF(15, 14) });
                        break;
                    case "check":
                        g.DrawLines(pen, new[] { new PointF(5, 12.5f), new PointF(10, 17.5f), new PointF(19, 7) });
                        break;
                    case "bars":     // the "now playing" mark
                        using (var p = Theme.Round(new RectangleF(4, 9, 3.4f, 11), 1)) g.FillPath(br, p);
                        using (var p = Theme.Round(new RectangleF(10.3f, 4, 3.4f, 16), 1)) g.FillPath(br, p);
                        using (var p = Theme.Round(new RectangleF(16.6f, 11, 3.4f, 9), 1)) g.FillPath(br, p);
                        break;
                    case "logo":
                        pen.Width = 2.2f;
                        g.DrawEllipse(pen, 3, 3, 18, 18);
                        g.DrawEllipse(pen, 7.5f, 7.5f, 9, 9);
                        g.FillEllipse(br, 10.3f, 10.3f, 3.4f, 3.4f);
                        break;
                    case "mic":
                        using (var p = Theme.Round(new RectangleF(8.5f, 3, 7, 11.5f), 3.5f)) { if (filled) g.FillPath(br, p); g.DrawPath(pen, p); }
                        g.DrawArc(pen, 5, 6.5f, 14, 11, 0, 180);
                        g.DrawLine(pen, 12, 17.5f, 12, 21); g.DrawLine(pen, 8.5f, 21, 15.5f, 21);
                        break;
                    case "expand":
                        g.DrawLines(pen, new[] { new PointF(4, 9), new PointF(4, 4), new PointF(9, 4) });
                        g.DrawLines(pen, new[] { new PointF(15, 4), new PointF(20, 4), new PointF(20, 9) });
                        g.DrawLines(pen, new[] { new PointF(20, 15), new PointF(20, 20), new PointF(15, 20) });
                        g.DrawLines(pen, new[] { new PointF(9, 20), new PointF(4, 20), new PointF(4, 15) });
                        break;
                    case "download":
                        g.DrawLine(pen, 12, 4, 12, 15); g.DrawLines(pen, new[] { new PointF(7.5f, 10.5f), new PointF(12, 15), new PointF(16.5f, 10.5f) });
                        g.DrawLine(pen, 5, 19.5f, 19, 19.5f);
                        break;
                    case "trash":
                        g.DrawLine(pen, 4, 6.5f, 20, 6.5f);
                        g.DrawLines(pen, new[] { new PointF(9, 6.5f), new PointF(9.5f, 3.8f), new PointF(14.5f, 3.8f), new PointF(15, 6.5f) });
                        g.DrawLines(pen, new[] { new PointF(6, 6.5f), new PointF(7, 20), new PointF(17, 20), new PointF(18, 6.5f) });
                        break;
                    case "edit":
                        g.DrawLines(pen, new[] { new PointF(4.5f, 19.5f), new PointF(5.2f, 15.5f), new PointF(15.8f, 4.9f), new PointF(19.1f, 8.2f), new PointF(8.5f, 18.8f), new PointF(4.5f, 19.5f) });
                        break;
                    case "star":
                        {
                            var pts = new List<PointF>();
                            for (int i = 0; i < 10; i++) { double a = -Math.PI / 2 + i * Math.PI / 5; float rr = i % 2 == 0 ? 9f : 4f; pts.Add(new PointF(12 + rr * (float)Math.Cos(a), 12.6f + rr * (float)Math.Sin(a))); }
                            using (var p = new GraphicsPath()) { p.AddPolygon(pts.ToArray()); if (filled) g.FillPath(br, p); pen.Width = 1.6f; g.DrawPath(pen, p); }
                        }
                        break;
                    case "moon":
                        using (var p = new GraphicsPath())
                        {
                            p.AddArc(4, 4, 16, 16, 300, 280);
                            p.AddArc(9.5f, 1.5f, 13, 13, 135, -230);
                            p.CloseFigure();
                            if (filled) g.FillPath(br, p); g.DrawPath(pen, p);
                        }
                        break;
                    case "bookmark":
                        using (var p = Poly(new[] { 6.5f, 3.5f, 17.5f, 3.5f, 17.5f, 20.5f, 12f, 16.5f, 6.5f, 20.5f })) { if (filled) g.FillPath(br, p); g.DrawPath(pen, p); }
                        break;
                    case "spark":      // a smart playlist
                        g.DrawLine(pen, 12, 3.5f, 12, 8.5f); g.DrawLine(pen, 12, 15.5f, 12, 20.5f);
                        g.DrawLine(pen, 3.5f, 12, 8.5f, 12); g.DrawLine(pen, 15.5f, 12, 20.5f, 12);
                        g.DrawLine(pen, 6.5f, 6.5f, 9, 9); g.DrawLine(pen, 15, 15, 17.5f, 17.5f);
                        g.DrawLine(pen, 17.5f, 6.5f, 15, 9); g.DrawLine(pen, 9, 15, 6.5f, 17.5f);
                        break;
                    case "radio":
                        g.DrawEllipse(pen, 10, 10, 4, 4);
                        g.DrawArc(pen, 6, 6, 12, 12, 200, 140); g.DrawArc(pen, 6, 6, 12, 12, 20, 140);
                        g.DrawArc(pen, 2.5f, 2.5f, 19, 19, 205, 130); g.DrawArc(pen, 2.5f, 2.5f, 19, 19, 25, 130);
                        break;
                    case "era":        // a stack of records
                        g.DrawEllipse(pen, 3.5f, 6.5f, 13, 13); g.DrawEllipse(pen, 8.5f, 11.5f, 3, 3);
                        g.DrawArc(pen, 7.5f, 4.5f, 13, 13, 250, 200);
                        break;
                    case "copies":     // two pages, one over the other
                        using (var p = Theme.Round(new RectangleF(8, 3.5f, 12, 13.5f), 2)) g.DrawPath(pen, p);
                        using (var p = new GraphicsPath()) { p.AddLines(new[] { new PointF(5, 7.5f), new PointF(4, 7.5f), new PointF(4, 20.5f), new PointF(15.5f, 20.5f), new PointF(15.5f, 19.5f) }); g.DrawPath(pen, p); }
                        break;
                    case "loop":
                        g.DrawArc(pen, 3.5f, 6, 9, 12, 90, 180);
                        g.DrawArc(pen, 11.5f, 6, 9, 12, 270, 180);
                        g.DrawLine(pen, 8, 6, 16, 6); g.DrawLine(pen, 8, 18, 16, 18);
                        g.DrawLines(pen, new[] { new PointF(13.5f, 3.5f), new PointF(16, 6), new PointF(13.5f, 8.5f) });
                        break;
                    case "mini":       // a small window in a big one
                        using (var p = Theme.Round(new RectangleF(3, 5, 18, 14), 2)) g.DrawPath(pen, p);
                        using (var p = Theme.Round(new RectangleF(11.5f, 11.5f, 7, 5), 1)) g.FillPath(br, p);
                        break;
                    case "wave":
                        foreach (var (x, h) in new[] { (4f, 4f), (7.5f, 10f), (11f, 16f), (14.5f, 8f), (18f, 12f) }) g.DrawLine(pen, x + 1, 12 - h / 2, x + 1, 12 + h / 2);
                        break;
                    case "convert":
                        g.DrawLines(pen, new[] { new PointF(4, 9), new PointF(18, 9), new PointF(14.5f, 5.5f) });
                        g.DrawLines(pen, new[] { new PointF(20, 15), new PointF(6, 15), new PointF(9.5f, 18.5f) });
                        break;
                }
            }
            g.Restore(state);
        }

        static GraphicsPath Poly(float[] xy)
        {
            var pts = new PointF[xy.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(xy[i * 2], xy[i * 2 + 1]);
            var p = new GraphicsPath(); p.AddPolygon(pts); return p;
        }

        static GraphicsPath Heart()
        {
            var p = new GraphicsPath();
            p.AddBezier(12, 20, 5.5f, 15.5f, 2.8f, 12, 3.4f, 8.3f);
            p.AddBezier(3.4f, 8.3f, 4.2f, 4, 9.6f, 3.2f, 12, 7.2f);
            p.AddBezier(12, 7.2f, 14.4f, 3.2f, 19.8f, 4, 20.6f, 8.3f);
            p.AddBezier(20.6f, 8.3f, 21.2f, 12, 18.5f, 15.5f, 12, 20);
            p.CloseFigure();
            return p;
        }
    }
}
