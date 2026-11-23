using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;

namespace Aura
{
    // Draws lyrics that follow the song: the current line lights up (and grows a little), word by word
    // when the song has word timings. Used by Now Playing (Stage = false) and full-screen lyrics (Stage = true).
    //
    // For smoothness each line is drawn into a picture once, and every frame only copies those pictures
    // around (with fading, growing and a glow) — drawing big text over and over is what made it choppy.
    sealed class LyricsPane : IDisposable
    {
        public bool Stage;
        public bool LeftAlign;            // full screen, beside the cover: lines start at the left
        public double Duration;            // for plain lyrics on the stage: they scroll along with the song
        public Color Glow = Color.White;   // the current line's glow (stage)
        Lyrics lyr; long forId = -1;
        float scroll = float.NaN, manual; DateTime manualUntil = DateTime.MinValue;
        float layoutW = -1; bool layoutStage, layoutLeft;
        List<Line> lay = new List<Line>();
        float total, rowH;
        readonly Stopwatch clock = Stopwatch.StartNew();
        double last;
        int lastCur = -2;
        public float Fade = 1;             // the whole thing (for song changes)

        sealed class Word { public string Text; public float X, W; public int Timed = -1; }
        sealed class Row { public List<Word> Words = new List<Word>(); public float Y, Width, Left; }
        sealed class Line
        {
            public List<Row> Rows = new List<Row>(); public float Top, Height, Width; public double T = -1; public LyricLine Src;
            public Faded Sprite, GlowSprite; public float Lit;      // Lit: 0..1, eased toward 1 while it's the current line
        }

        public Lyrics Lyrics => lyr;

        public void Set(Lyrics l, long id)
        {
            if (id == forId && ReferenceEquals(l, lyr)) return;
            Free();
            lyr = l; forId = id; scroll = float.NaN; manualUntil = DateTime.MinValue; manual = 0; layoutW = -1; lastCur = -2;
        }

        void Free()
        {
            foreach (var L in lay) { L.Sprite?.Dispose(); L.GlowSprite?.Dispose(); }
            lay = new List<Line>();
        }

        public void Dispose() => Free();

        Font F => Stage ? Theme.Font(LeftAlign ? 40 : 46, FontStyle.Bold) : Theme.Font(27, FontStyle.Bold);
        bool Timed => lyr != null && lyr.Timed;
        float Pad => Theme.F(Stage ? 26 : 10);

        void Layout(Graphics g, float width)
        {
            Free();
            var f = F;
            rowH = f.GetHeight(g) * 1.14f;
            float gap = Theme.F(Stage ? 26 : 16), space = Theme.Measure(g, "a a", f) - Theme.Measure(g, "aa", f);
            float y = 0;
            var src = Timed ? lyr.Lines : (lyr?.Plain ?? "").Split('\n').Select(x => new LyricLine { T = -1, Text = x.Trim() }).ToList();
            // a little room so a growing line doesn't spill over
            float wrapAt = width * (Stage ? 0.94f : 0.96f);
            foreach (var ln in src)
            {
                var L = new Line { Top = y, T = Timed ? ln.T : -1, Src = ln };
                List<(string w, int timed)> words;
                if (ln.Words != null) words = ln.Words.Select((x, i) => (x.w, i)).ToList();
                else words = ln.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(x => (x, -1)).ToList();
                if (words.Count == 0)
                {
                    // an instrumental gap in timed lyrics shows a note; a blank line in plain lyrics is a small space
                    if (Timed) { var r = new Row(); r.Words.Add(new Word { Text = "♪", X = 0, W = Theme.Measure(g, "♪", f) }); r.Width = r.Words[0].W; L.Rows.Add(r); L.Height = rowH; }
                    else L.Height = rowH * 0.45f;
                }
                else
                {
                    var row = new Row(); float x = 0;
                    foreach (var (w, ti) in words)
                    {
                        float ww = Theme.Measure(g, w, f);
                        if (x > 0 && x + ww > wrapAt) { row.Width = x - space; L.Rows.Add(row); row = new Row { Y = L.Rows.Count * rowH }; x = 0; }
                        row.Words.Add(new Word { Text = w, X = x, W = ww, Timed = ti });
                        x += ww + space;
                    }
                    row.Width = x - space; L.Rows.Add(row);
                    L.Height = L.Rows.Count * rowH;
                }
                L.Width = L.Rows.Count == 0 ? 0 : L.Rows.Max(r => r.Width);
                foreach (var r in L.Rows) r.Left = Stage && !LeftAlign ? (L.Width - r.Width) / 2 : 0;
                lay.Add(L);
                y += L.Height + gap;
            }
            total = y;
            layoutW = width; layoutStage = Stage; layoutLeft = LeftAlign;
        }

        // the line drawn once, white on transparent
        Faded SpriteOf(Line L)
        {
            if (L.Sprite != null || L.Rows.Count == 0) return L.Sprite;
            float pad = Pad;
            int w = Math.Max(1, (int)Math.Ceiling(L.Width + pad * 2)), h = Math.Max(1, (int)Math.Ceiling(L.Height + pad * 2));
            var b = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(b))
            using (var br = new SolidBrush(Color.White))
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                foreach (var r in L.Rows)
                    foreach (var wd in r.Words)
                        g.DrawString(wd.Text, F, br, pad + r.Left + wd.X, pad + r.Y, StringFormat.GenericTypographic);
            }
            L.Sprite = new Faded(b);
            return L.Sprite;
        }

        // a soft blur of the line, for the glow behind the current one: the line with room around it,
        // shrunk a lot and grown back (that's the blur). GlowMargin is that room, in the line's own pixels.
        float GlowMargin => rowH * 0.5f;
        Faded GlowOf(Line L)
        {
            if (L.GlowSprite != null) return L.GlowSprite;
            var s = SpriteOf(L)?.Source;
            if (s == null) return null;
            int m = (int)GlowMargin;
            int pw = s.Width + m * 2, ph = s.Height + m * 2;
            using (var tiny = new Bitmap(Math.Max(1, pw / 12), Math.Max(1, ph / 12), PixelFormat.Format32bppPArgb))
            {
                using (var g = Graphics.FromImage(tiny))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(s, new RectangleF(m * tiny.Width / (float)pw, m * tiny.Height / (float)ph, s.Width * tiny.Width / (float)pw, s.Height * tiny.Height / (float)ph));
                }
                var b = new Bitmap(Math.Max(1, pw / 3), Math.Max(1, ph / 3), PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(b))
                using (var ia = new ImageAttributes())
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    ia.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(tiny, new Rectangle(0, 0, b.Width, b.Height), 0, 0, tiny.Width, tiny.Height, GraphicsUnit.Pixel, ia);
                }
                L.GlowSprite = new Faded(b, Glow);
            }
            return L.GlowSprite;
        }

        public int Current(double pos)
        {
            if (!Timed || lay == null) return -1;
            int cur = -1;
            for (int i = 0; i < lay.Count; i++) { if (lay[i].T <= pos + 0.12) cur = i; else break; }
            return cur;
        }

        public void Wheel(int delta, float areaH)
        {
            if (lay == null) return;
            float from = DateTime.Now < manualUntil ? manual : (float.IsNaN(scroll) ? 0 : scroll);
            manual = Math.Max(-areaH * 0.4f, Math.Min(Math.Max(0, total - areaH * 0.5f), from - delta / 120f * Theme.F(110)));
            manualUntil = Timed ? DateTime.Now.AddSeconds(4) : DateTime.MaxValue;
        }

        static void Blit(Graphics g, Faded f, float alpha, RectangleF dest, RectangleF src)
        {
            var b = f?.At(alpha);
            if (b == null || dest.Width < 0.5f || dest.Height < 0.5f || src.Width < 0.5f || src.Height < 0.5f) return;
            g.DrawImage(b, dest, src, GraphicsUnit.Pixel);
        }

        // pos = where the song is; seek = called with a time when a line is clicked
        public void Draw(Graphics g, RectangleF area, double pos, Canvas host, Action<double> seek, string idPrefix = "ly")
        {
            if (lyr == null) return;
            double now = clock.Elapsed.TotalSeconds;
            float dt = (float)Math.Max(0.001, Math.Min(0.1, now - last));
            last = now;
            if (layoutW < 0 || Math.Abs(layoutW - area.Width) > 0.5f || layoutStage != Stage || layoutLeft != LeftAlign) Layout(g, area.Width);
            int cur = Current(pos);
            bool jumped = lastCur >= 0 && cur >= 0 && Math.Abs(cur - lastCur) > 3;       // a seek: get there quickly
            lastCur = cur;
            float anchor = area.Height * (Stage ? 0.40f : 0.30f);
            float target;
            if (DateTime.Now < manualUntil) target = manual;
            else if (Timed) target = cur >= 0 ? lay[cur].Top + lay[cur].Height / 2 - anchor : -anchor + (lay.Count > 0 ? lay[0].Height / 2 : 0);
            else if (Stage && Duration > 1) target = (float)Math.Max(0, Math.Min(1, (pos - 8) / Math.Max(1, Duration - 30))) * Math.Max(0, total - area.Height * 0.55f) - area.Height * 0.08f;
            else target = 0;
            if (float.IsNaN(scroll) || jumped) scroll = target;
            else scroll += (target - scroll) * FrameClock.Ease(dt, Stage ? 5.5f : 7f);

            var st = g.Save();
            g.SetClip(area);
            g.InterpolationMode = InterpolationMode.Bilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            float pad = Pad;
            float edge = Theme.F(Stage ? 120 : 50);      // lines fade out toward the top and bottom
            for (int i = 0; i < lay.Count; i++)
            {
                var L = lay[i];
                float want = Timed ? (i == cur ? 1 : 0) : 0;
                L.Lit += (want - L.Lit) * FrameClock.Ease(dt, want > L.Lit ? 12 : 6);
                float top = area.Y + L.Top - scroll;
                if (top > area.Bottom + rowH || top + L.Height < area.Y - rowH) { continue; }
                if (L.Rows.Count == 0) continue;
                // how bright: the current line is white, sung lines a bit dimmer, lines to come dimmer still
                float baseA;
                if (!Timed) baseA = 0.88f;
                else baseA = cur < 0 || i > cur ? 0.36f : 0.52f;
                if (Stage && Timed && cur >= 0) baseA *= (float)Math.Max(0.22, 1 - Math.Abs(i - cur) * 0.14);
                float a = baseA + (1 - baseA) * L.Lit;
                float mid = top + L.Height / 2;
                float fadeEdge = Math.Min(1, Math.Min((mid - area.Y) / edge, (area.Bottom - mid) / edge));
                a *= Math.Max(0, fadeEdge) * Fade;
                bool hot = host != null && Timed && host.HoverIdIs(idPrefix + ":" + i);
                if (hot && i != cur) a = Math.Min(1, a + 0.3f);
                var lineRect = new RectangleF(area.X - Theme.F(10), top - Theme.F(4), area.Width + Theme.F(20), L.Height + Theme.F(8));
                if (hot) Theme.Fill(g, Theme.Alpha(Theme.Ink, 14), lineRect, Theme.F(10));
                if (a > 0.01f)
                {
                    var sp = SpriteOf(L);
                    float scale = 1 + L.Lit * (Stage ? 0.06f : 0.035f);
                    float x0 = Stage && !LeftAlign ? area.X + (area.Width - L.Width) / 2 : area.X;
                    // grow around the line's middle (stage) or its left edge (Now Playing)
                    float ox = Stage && !LeftAlign ? x0 + L.Width / 2 : x0, oy = top + L.Height / 2;
                    RectangleF Dest(float sx, float sy, float sw, float sh) =>
                        new RectangleF(ox + (x0 - pad + sx - ox) * scale, oy + (top - pad + sy - oy) * scale, sw * scale, sh * scale);
                    var whole = new RectangleF(0, 0, sp.Width, sp.Height);
                    if (L.Lit > 0.02f)
                    {
                        var gl = GlowOf(L);
                        float gm = (int)GlowMargin;
                        var d = Dest(-gm, -gm, sp.Width + gm * 2, sp.Height + gm * 2);
                        // twice, a bit bigger the second time: a brighter core and a wide soft halo
                        float gs = Stage ? 1 : 0.5f;
                        Blit(g, gl, 0.95f * gs * L.Lit * Fade, d, new RectangleF(0, 0, gl.Width, gl.Height));
                        var d2 = RectangleF.Inflate(d, d.Width * 0.04f, d.Height * 0.18f);
                        if (Stage) Blit(g, gl, 0.5f * L.Lit * Fade, d2, new RectangleF(0, 0, gl.Width, gl.Height));
                    }
                    bool byWord = Timed && i == cur && L.Src.Words != null;
                    if (!byWord) Blit(g, sp, a, Dest(0, 0, sp.Width, sp.Height), whole);
                    else
                    {
                        // word by word: dim, then the sung part filled in, row by row
                        double next = i + 1 < lay.Count ? lay[i + 1].T : -1;
                        Blit(g, sp, Math.Min(a, 0.42f * Fade + 0.1f), Dest(0, 0, sp.Width, sp.Height), whole);
                        foreach (var r in L.Rows)
                        {
                            float fill = 0;
                            foreach (var w in r.Words)
                            {
                                if (w.Timed < 0) continue;
                                double k = WordProgress(L.Src, w.Timed, pos, next);
                                if (k <= 0) break;
                                fill = w.X + w.W * (float)k + (k >= 1 ? Theme.F(14) : 0);
                                if (k < 1) break;
                            }
                            if (fill <= 0) continue;
                            bool firstRow = r == L.Rows[0], lastRow = r == L.Rows[L.Rows.Count - 1];
                            float sx = 0, sw = Math.Min(sp.Width, pad + r.Left + fill);
                            float sy = firstRow ? 0 : pad + r.Y;
                            float sh = Math.Min(sp.Height, pad + r.Y + rowH + (lastRow ? pad : 0)) - sy;
                            Blit(g, sp, Fade, Dest(sx, sy, sw, sh), new RectangleF(sx, sy, sw, sh));
                        }
                    }
                }
                if (Timed && host != null && seek != null && lineRect.Bottom > area.Y && lineRect.Top < area.Bottom)
                {
                    double t = L.T;
                    host.AddHit(idPrefix + ":" + i, RectangleF.Intersect(lineRect, area), () => seek(t), "Play from here");
                }
            }
            g.Restore(st);
        }

        // whether anything is still moving (so the page knows to keep drawing frames)
        public bool Moving => lay.Any(l => l.Lit > 0.01f && l.Lit < 0.99f) || (!float.IsNaN(scroll) && lay.Count > 0);

        static double WordProgress(LyricLine line, int i, double pos, double nextLine)
        {
            var ws = line.Words;
            double start = ws[i].t;
            double end = i + 1 < ws.Count ? ws[i + 1].t : line.End > 0 ? line.End : nextLine > 0 ? Math.Min(nextLine, start + 1.2) : start + 0.8;
            if (end <= start) end = start + 0.25;
            end = Math.Min(end, start + 2.5);          // a long pause after a word isn't part of it
            return Math.Max(0, Math.Min(1, (pos - start) / (end - start)));
        }
    }
}
