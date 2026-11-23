using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Linq;

namespace Aura
{
    // Full-screen lyrics in deep space, colored by the cover and moving with the music.
    sealed class FullLyricsForm : Form
    {
        public static int Open;            // while it's up, pages behind it stop animating
        readonly StageView view;
        public FullLyricsForm(IShell shell, Screen screen)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = screen.Bounds;
            BackColor = Color.Black;
            ShowInTaskbar = false;
            KeyPreview = true;
            Text = "AURA lyrics";
            view = new StageView(shell, this) { Dock = DockStyle.Fill };
            Controls.Add(view);
            KeyDown += (s, e) =>
            {
                switch (e.KeyCode)
                {
                    case Keys.Escape: case Keys.F11: Close(); break;
                    case Keys.Space: shell.Player.Toggle(); break;
                    case Keys.Left: shell.Player.Seek(shell.Player.Position - 5); break;
                    case Keys.Right: shell.Player.Seek(shell.Player.Position + 5); break;
                    case Keys.Up: shell.Player.Volume += 5; break;
                    case Keys.Down: shell.Player.Volume -= 5; break;
                    case Keys.N: shell.Player.Next(); break;
                    case Keys.P: shell.Player.Prev(); break;
                    default: return;
                }
                view.Poke();
                e.Handled = true;
            };
            Open++;
            FormClosed += (s, e) => { Open--; view.Dispose(); };
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Space) { OnKeyDown(new KeyEventArgs(keyData)); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    sealed class StageView : Canvas
    {
        readonly IShell shell;
        readonly Form owner;
        readonly LyricsPane pane = new LyricsPane { Stage = true, LeftAlign = true };
        readonly Cosmos cosmos = new Cosmos();
        readonly FrameClock frames;
        DateTime lastMove = DateTime.Now;
        Track shown;
        float ui = 1, intro = 0, songFade = 1, side;     // eased 0..1: the buttons, the first fade from black, a new song, lyrics beside the cover
        bool cursorHidden;
        int frame;
        StageSong song, loading;
        Bitmap sky; Size skyFor;
        Faded[] dotSprites; int spritesFor;
        Faded glowSprite; int glowFor;

        public StageView(IShell shell, Form owner)
        {
            this.shell = shell; this.owner = owner;
            Scrollable = false;
            BackColor = Color.Black;
            frames = new FrameClock(this, () => Visible);
            frames.Frame += () => frame++;
        }

        public void Poke() { lastMove = DateTime.Now; ShowCursor(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                frames.Dispose(); pane.Dispose(); ShowCursor();
                sky?.Dispose(); song?.Dispose();
                if (dotSprites != null) foreach (var d in dotSprites) d.Dispose();
                glowSprite?.Dispose();
            }
            base.Dispose(disposing);
        }

        void ShowCursor() { if (cursorHidden) { Cursor.Show(); cursorHidden = false; } }

        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (e.Location != lastPoint) { lastPoint = e.Location; Poke(); } }
        Point lastPoint;
        protected override void OnMouseDoubleClick(MouseEventArgs e) { base.OnMouseDoubleClick(e); if (!HitAny(e.Location)) owner.Close(); }
        bool HitAny(Point p) => HoverId != null;
        protected override void OnMouseWheel(MouseEventArgs e) { Poke(); pane.Wheel(e.Delta, Height * 0.7f); }
        protected override void OnPaint(PaintEventArgs e) { using (Theme.Dark()) base.OnPaint(e); }

        float Loudness(Track t, double pos)
        {
            var s = shell.Sound.Get(t);
            if (s == null || s.Env.Length == 0) return -1;
            int i = (int)(pos * SongSound.Fps);
            if (i < 0 || i >= s.Env.Length) return 0;
            int m = 0; for (int j = Math.Max(0, i - 2); j <= Math.Min(s.Env.Length - 1, i + 2); j++) m = Math.Max(m, s.Env[j]);
            return Math.Max(0, Math.Min(1, (m - 150) / 100f));
        }

        internal static double DrawMs; internal static int Drawn;      // how long a frame takes to draw (for tests)
        readonly System.Diagnostics.Stopwatch drawWatch = new System.Diagnostics.Stopwatch();

        protected override void Draw(Graphics g, RectangleF view)
        {
            drawWatch.Restart();
            try { DrawStage(g); }
            finally
            {
                DrawMs = DrawMs * 0.9 + drawWatch.Elapsed.TotalMilliseconds * 0.1; Drawn++;
#if TESTHOOKS
                if (Drawn % 60 == 0 && Environment.GetEnvironmentVariable("AURA_TEST_TRACE") != null) Console.WriteLine("    frame " + Drawn + ": " + DrawMs.ToString("0.0") + " ms, now " + drawWatch.Elapsed.TotalMilliseconds.ToString("0.0"));
#endif
            }
        }

        // the song's big cover, colors and sky, made on a worker thread
        Track askedFor; DateTime askedAt;
        void Want(Track t)
        {
            if (t == null || loading?.T == t || song?.T == t) return;
            var small = Art.Get(t);
            if (askedFor != t) { askedFor = t; askedAt = DateTime.Now; }
            // not loaded yet: ask again next frame (but don't wait on it for long: the big one is read straight from the song)
            if (small == null && !Art.Missing(t) && (DateTime.Now - askedAt).TotalSeconds < 1.5) return;
            loading = StageSong.Make(t, small, (int)Math.Max(Height * 0.4f, 300), Math.Max(64, Width / 3), Math.Max(64, Height / 3), s =>
            {
                if (IsDisposed) { s.Dispose(); return; }
                if (loading == s) loading = null;
                if (shell.Player.Current != s.T) { s.Dispose(); return; }
                song?.Dispose(); song = s; sky?.Dispose(); sky = null;
            }, this);
        }

        void DrawStage(Graphics g)
        {
            float dt = frames.Step();
            var P = shell.Player;
            var t = P.Current;
            double pos = P.SmoothPosition;
            if (t != shown)
            {
                if (shown != null && t != null) cosmos.Warp();
                shown = t; songFade = 0;
                pane.Set(t == null ? null : shell.Lyrics.Get(t), t?.Id ?? -1);
                if (t != null) shell.Sound.Want(t);
            }
            Want(t);
            songFade += (1 - songFade) * FrameClock.Ease(dt, 3.5f);
            intro += (1 - intro) * FrameClock.Ease(dt, 2.5f);
            bool showUi = (DateTime.Now - lastMove).TotalSeconds < 3;
            ui += ((showUi ? 1 : 0) - ui) * FrameClock.Ease(dt, showUi ? 10 : 3);
            if (!showUi && !cursorHidden && App.IsWindows && Form.ActiveForm == owner) { Cursor.Hide(); cursorHidden = true; }
            cosmos.Step(dt, t == null ? -1 : Loudness(t, pos), P.IsPlaying);
            bool has = t != null && pane.Lyrics != null;
            side += ((has ? 1 : 0) - side) * FrameClock.Ease(dt, 4);
            var L0 = Cosmos.Place(Width, Height, false); var L1 = Cosmos.Place(Width, Height, true);
            var L = Mix(L0, L1, side);
            var pal = song?.Pal ?? new[] { Theme.Accent, Theme.Accent, Theme.Accent };

            // the sky
            if (song != null)
            {
                if (sky == null || skyFor != Size)
                {
                    sky?.Dispose();
                    sky = new Bitmap(Math.Max(1, Width), Math.Max(1, Height), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                    using (var sg = Graphics.FromImage(sky)) { sg.InterpolationMode = InterpolationMode.HighQualityBilinear; sg.PixelOffsetMode = PixelOffsetMode.Half; sg.DrawImage(song.Sky, new Rectangle(0, 0, Width, Height)); }
                    skyFor = Size;
                }
                g.DrawImageUnscaled(sky, 0, 0);
            }
            else g.Clear(Color.FromArgb(8, 9, 14));
            if (cosmos.Peak > 0.01f) using (var b = new SolidBrush(Color.FromArgb((int)(cosmos.Peak * 120), Theme.Mix(pal[0], Color.White, 0.5f)))) g.FillRectangle(b, 0, 0, Width, Height);

            // streaks flying out of the cover, and glowing dots
            g.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (var st in cosmos.Streaks)
            {
                if (!cosmos.Project(st, L.Cx, L.Cy, Width, Height, out var a, out var b2, out var al, out var w)) continue;
                using (var pen = new Pen(Color.FromArgb((int)(Math.Min(1, al) * 170), 238, 240, 255), w) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(pen, a, b2);
            }
            foreach (var d in cosmos.Dots)
            {
                cosmos.Place(d, L.Cx, L.Cy, Width, Height, out var p, out var r, out var al);
                if (al < 0.03f) continue;
                var c = Theme.Mix(pal[d.Color], Color.White, 0.25f);
                float halo = r * 2.4f;
                using (var b = new SolidBrush(Color.FromArgb((int)(al * 34), c))) g.FillEllipse(b, p.X - halo, p.Y - halo, halo * 2, halo * 2);
                using (var b = new SolidBrush(Color.FromArgb((int)(al * 235), c))) g.FillEllipse(b, p.X - r, p.Y - r, r * 2, r * 2);
            }

            if (t == null)
            {
                Theme.Write(g, "Nothing playing", Theme.Font(30, FontStyle.Bold), Theme.Alpha(Color.White, 220), new RectangleF(0, 0, Width, Height), Theme.Center);
                Intro(g); return;
            }
            int ta = (int)(255 * songFade);

            // the cover: a glow, a ring that breathes with the music, the picture
            var cover = song?.T == t ? song.Cover : null;
            float pulse = 1 + cosmos.Energy * 0.02f + cosmos.Kick * 0.015f;
            Glow(pal[0]);
            var gr = RectangleF.Inflate(L.Cover, L.S * 0.75f, L.S * 0.75f);
            var gs = glowSprite.At(Math.Min(1, 0.35f + cosmos.Energy * 0.4f + cosmos.Kick * 0.3f));
            if (gs != null) g.DrawImage(gs, gr);
            float rp = 1 + cosmos.Kick * 0.04f;
            var ring = RectangleF.Inflate(L.Ring, L.Ring.Width * (rp - 1) / 2, L.Ring.Height * (rp - 1) / 2);
            for (int i = 3; i >= 0; i--)
                using (var pen = new Pen(Color.FromArgb(i == 0 ? 190 : 34 - i * 6, Theme.Mix(pal[0], Color.White, 0.25f)), i == 0 ? Theme.F(1.6f) : Theme.F(2 + i * 4)))
                    g.DrawEllipse(pen, ring);
            var cr = RectangleF.Inflate(L.Cover, L.S * (pulse - 1) / 2, L.S * (pulse - 1) / 2);
            for (int i = 1; i <= 3; i++)
                using (var sh = new SolidBrush(Color.FromArgb(28, 0, 0, 0))) using (var sp = Theme.Round(RectangleF.Inflate(new RectangleF(cr.X, cr.Y + Theme.F(8), cr.Width, cr.Height), Theme.F(i * 4), Theme.F(i * 4)), L.S * 0.06f + Theme.F(i * 4))) g.FillPath(sh, sp);
            var img = cover ?? Art.Get(t);
            if (img != null) Theme.Image(g, img, cr, L.S * 0.06f); else Theme.NoArt(g, cr, L.S * 0.06f, t.Id);

            // the song
            Theme.Write(g, t.Name, Theme.Font(L.Title.Height * 0.62f / Theme.S, FontStyle.Bold), Color.FromArgb(ta, 255, 255, 255), L.Title, Theme.Center);
            Theme.Write(g, string.Join(" ", t.ArtistName.ToUpperInvariant().ToCharArray()), Theme.Font(L.Artist.Height * 0.46f / Theme.S, FontStyle.Regular, true), Color.FromArgb((int)(ta * 0.62f), 255, 255, 255), L.Artist, Theme.Center);
            var albumLine = t.Album.Length > 0 ? t.Album : "";
            if (t.Era.Length > 0 && t.Era != t.Album) albumLine += (albumLine.Length > 0 ? "  ·  " : "") + t.Era;
            Theme.Write(g, albumLine, Theme.Font(L.Album.Height * 0.5f / Theme.S), Color.FromArgb((int)(ta * 0.38f), 255, 255, 255), L.Album, Theme.Center);
            if (side < 0.5f && !has)
                Theme.Write(g, "No lyrics for this song yet", Theme.Font(13), Color.FromArgb((int)(ta * 0.3f * (1 - side)), 255, 255, 255), new RectangleF(L.Album.X, L.Album.Bottom + Theme.F(6), L.Album.Width, Theme.F(22)), Theme.Center);

            // up next
            DrawUpNext(g, L, ta);

            // the lyrics, beside the cover
            if (has)
            {
                pane.Glow = Theme.Mix(pal[0], Color.White, 0.35f);
                pane.Duration = P.Duration;
                pane.Fade = songFade * side;
                pane.Draw(g, L.Lyrics, pos, this, sec => { P.Seek(sec); Poke(); }, "st");
            }

            DrawBar(g, L, pos, pal);

            // close, top right
            if (ui > 0.02f)
            {
                var st = g.Save();
                g.TranslateTransform(0, -(1 - ui) * Theme.F(120));
                IconButton(g, "close", new RectangleF(Width - Theme.F(44) - Theme.F(44), Theme.F(30), Theme.F(44), Theme.F(44)), "close", owner.Close, "Close (Esc)", circleBg: true);
                g.Restore(st);
            }
            Intro(g);
        }

        static Cosmos.Layout Mix(Cosmos.Layout a, Cosmos.Layout b, float k)
        {
            if (k <= 0.001f) return a; if (k >= 0.999f) return b;
            RectangleF R(RectangleF x, RectangleF y) => new RectangleF(x.X + (y.X - x.X) * k, x.Y + (y.Y - x.Y) * k, x.Width + (y.Width - x.Width) * k, x.Height + (y.Height - x.Height) * k);
            return new Cosmos.Layout
            {
                W = a.W, H = a.H, Cx = a.Cx + (b.Cx - a.Cx) * k, Cy = a.Cy + (b.Cy - a.Cy) * k, S = a.S + (b.S - a.S) * k,
                Cover = R(a.Cover, b.Cover), Ring = R(a.Ring, b.Ring), Date = R(a.Date, b.Date), Clock = R(a.Clock, b.Clock), Title = R(a.Title, b.Title),
                Artist = R(a.Artist, b.Artist), Album = R(a.Album, b.Album), Lyrics = b.Lyrics, Bar = b.Bar, UpNext = R(a.UpNext, b.UpNext),
                CardW = b.CardW, CardH = b.CardH, CardGap = b.CardGap,
            };
        }

        static string lightFamily;
        static string LightFamily()
        {
            if (lightFamily != null) return lightFamily;
            foreach (var n in new[] { "Segoe UI Variable Display Light", "Segoe UI Light", "Bahnschrift Light", Theme.Family })
                try { using (var f = new FontFamily(n)) { lightFamily = n; break; } } catch { }
            return lightFamily ?? (lightFamily = Theme.Family);
        }

        // soft round dots in the cover's colors, made once per song
        void Sprites(Color[] pal)
        {
            int key = pal[0].ToArgb() ^ pal[1].ToArgb() * 7 ^ pal[2].ToArgb() * 13;
            if (dotSprites != null && spritesFor == key) return;
            if (dotSprites != null) foreach (var d in dotSprites) d.Dispose();
            dotSprites = pal.Select(c => new Faded(Sprite(Theme.Mix(c, Color.White, 0.2f), 48, true))).ToArray();
            spritesFor = key;
        }

        void Glow(Color c)
        {
            if (glowSprite != null && glowFor == c.ToArgb()) return;
            glowSprite?.Dispose();
            glowSprite = new Faded(Sprite(c, 160, false));
            glowFor = c.ToArgb();
        }

        static Bitmap Sprite(Color c, int size, bool core)
        {
            var b = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(b))
            using (var p = new GraphicsPath())
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                p.AddEllipse(0, 0, size, size);
                using (var pb = new PathGradientBrush(p) { CenterColor = Color.FromArgb(core ? 120 : 150, c), SurroundColors = new[] { Color.FromArgb(0, c) } })
                {
                    pb.SetSigmaBellShape(core ? 0.35f : 0.6f, 1f);
                    g.FillEllipse(pb, 0, 0, size, size);
                }
                if (core)
                {
                    float r = size * 0.11f;
                    using (var cb = new SolidBrush(Color.FromArgb(255, Theme.Mix(c, Color.White, 0.55f)))) g.FillEllipse(cb, size / 2f - r, size / 2f - r, r * 2, r * 2);
                }
            }
            return b;
        }

        // glass cards, tilted a little, with what plays next (click one to play it)
        void DrawUpNext(Graphics g, Cosmos.Layout L, int ta)
        {
            var P = shell.Player;
            var next = new List<Track>();
            for (int i = P.Pos; i < P.Order.Count && next.Count < 5; i++)
            {
                if (i < 0) continue;
                var tr = shell.Lib.Get(P.Queue[P.Order[i]]);
                if (tr != null) next.Add(tr);
            }
            if (next.Count == 0) return;
            float x = L.UpNext.X, y = L.UpNext.Y;
            Theme.Write(g, "U P   N E X T", Theme.Font(10.5f, FontStyle.Regular, true), Theme.Alpha(Color.White, 110), new RectangleF(x, y, L.CardW, Theme.F(20)));
            y += Theme.F(24);
            for (int i = 0; i < next.Count; i++)
            {
                var tr = next[i];
                bool now = i == 0;
                float w = L.CardW * (now ? 1.08f : 0.94f), h = L.CardH * (now ? 1.05f : 0.94f);
                var r = new RectangleF(x + (now ? Theme.F(10) : 0), y, w, h);
                var st = g.Save();
                // a little perspective: the cards lean back toward the middle
                using (var m = new Matrix()) { m.Translate(r.X, r.Y + h / 2); m.Shear(0, -0.05f); m.Translate(-r.X, -(r.Y + h / 2)); g.MultiplyTransform(m); }
                string id = "un:" + i;
                bool hot = Hover(id);
                using (var path = Theme.Round(r, Theme.F(10)))
                {
                    using (var b = new LinearGradientBrush(r, Color.FromArgb(hot ? 60 : now ? 46 : 30, 255, 255, 255), Color.FromArgb(hot ? 30 : 12, 255, 255, 255), 90f)) g.FillPath(b, path);
                    using (var pen = new Pen(now ? Color.FromArgb(150, Theme.Mix(Theme.Accent, Color.White, 0.3f)) : Color.FromArgb(55, 255, 255, 255), Theme.F(1))) g.DrawPath(pen, path);
                }
                float th = h - Theme.F(12);
                var tr2 = new RectangleF(r.X + Theme.F(6), r.Y + Theme.F(6), th, th);
                var img = Art.Get(tr);
                if (img != null) Theme.Image(g, img, tr2, Theme.F(6)); else Theme.NoArt(g, tr2, Theme.F(6), tr.Id);
                float tx = tr2.Right + Theme.F(10), tw = r.Right - tx - Theme.F(8);
                Theme.Write(g, tr.Name, Theme.Font(now ? 13.5f : 12.5f, FontStyle.Regular, true), Color.FromArgb(now ? 240 : 205, 255, 255, 255), new RectangleF(tx, r.Y + h * 0.16f, tw, h * 0.38f));
                Theme.Write(g, tr.ArtistName, Theme.Font(11), Color.FromArgb(120, 255, 255, 255), new RectangleF(tx, r.Y + h * 0.52f, tw, h * 0.32f));
                g.Restore(st);
                int k = i;
                if (!now) AddHit(id, r, () => { P.JumpTo(k); Poke(); }, "Play " + tr.Name);
                y += h + L.CardGap;
            }
        }

        // the glass player bar at the bottom (it slides away when the mouse rests)
        void DrawBar(Graphics g, Cosmos.Layout L, double pos, Color[] pal)
        {
            if (ui < 0.02f) return;
            var P = shell.Player;
            var t = P.Current;
            var st = g.Save();
            g.TranslateTransform(0, (1 - ui) * (L.Bar.Height + Theme.F(60)));
            var r = L.Bar;
            using (var path = Theme.Round(r, r.Height * 0.3f))
            {
                using (var b = new LinearGradientBrush(r, Color.FromArgb(46, 255, 255, 255), Color.FromArgb(20, 255, 255, 255), 90f)) g.FillPath(b, path);
                using (var dark = new SolidBrush(Color.FromArgb(70, 6, 8, 14))) g.FillPath(dark, path);
                using (var pen = new Pen(Color.FromArgb(60, 255, 255, 255), Theme.F(1))) g.DrawPath(pen, path);
            }
            float h = r.Height, pad = h * 0.18f;
            // the song
            var ar = new RectangleF(r.X + pad, r.Y + pad, h - pad * 2, h - pad * 2);
            var img = Art.Get(t);
            if (img != null) Theme.Image(g, img, ar, Theme.F(7)); else Theme.NoArt(g, ar, Theme.F(7), t.Id);
            float tx = ar.Right + Theme.F(12), leftW = r.Width * 0.24f;
            Theme.Write(g, t.Name, Theme.Font(13.5f, FontStyle.Regular, true), Color.White, new RectangleF(tx, r.Y + h * 0.2f, leftW - (tx - r.X) - Theme.F(36), h * 0.32f));
            Theme.Write(g, t.ArtistName, Theme.Font(11.5f), Theme.Alpha(Color.White, 140), new RectangleF(tx, r.Y + h * 0.5f, leftW - (tx - r.X) - Theme.F(36), h * 0.3f));
            float hb = Theme.F(32);
            IconButton(g, "fav", new RectangleF(r.X + leftW - hb, r.Y + (h - hb) / 2, hb, hb), "heart", () => { shell.Lib.SetFav(t, !t.Fav); Poke(); }, t.Fav ? "Remove from Favorites" : "Add to Favorites", t.Fav, filled: t.Fav, dot: false);
            // the buttons and how far along
            float cx = r.X + r.Width / 2, by = r.Y + h * 0.36f, bs = Theme.F(30);
            PlayButton(g, "play", new RectangleF(cx - h * 0.21f, by - h * 0.21f, h * 0.42f, h * 0.42f), P.IsPlaying, () => { P.Toggle(); Poke(); }, P.IsPlaying ? "Pause (Space)" : "Play (Space)");
            IconButton(g, "prev", new RectangleF(cx - h * 0.21f - Theme.F(10) - bs, by - bs / 2, bs, bs), "prev", () => { P.Prev(); Poke(); }, "Previous (P)");
            IconButton(g, "next", new RectangleF(cx + h * 0.21f + Theme.F(10), by - bs / 2, bs, bs), "next", () => { P.Next(); Poke(); }, "Next (N)");
            IconButton(g, "shuf", new RectangleF(cx - h * 0.21f - Theme.F(20) - bs * 2, by - bs / 2, bs, bs), "shuffle", () => { P.SetShuffle(!P.Shuffle); Poke(); }, "Shuffle", P.Shuffle);
            IconButton(g, "rep", new RectangleF(cx + h * 0.21f + Theme.F(20) + bs, by - bs / 2, bs, bs), P.Repeat == 2 ? "repeat1" : "repeat", () => { P.CycleRepeat(); Poke(); }, "Repeat", P.Repeat > 0);
            double d = P.Duration;
            float k = d > 0 ? (float)Math.Max(0, Math.Min(1, pos / d)) : 0;
            float pw = r.Width * 0.36f, px = cx - pw / 2, py = r.Y + h * 0.78f;
            Theme.Write(g, Theme.Time(pos), Theme.Font(10.5f), Theme.Alpha(Color.White, 150), new RectangleF(px - Theme.F(50), py - Theme.F(9), Theme.F(42), Theme.F(18)), Theme.Right);
            Theme.Write(g, Theme.Time(d), Theme.Font(10.5f), Theme.Alpha(Color.White, 150), new RectangleF(px + pw + Theme.F(8), py - Theme.F(9), Theme.F(42), Theme.F(18)));
            Theme.Fill(g, Theme.Alpha(Color.White, 40), new RectangleF(px, py - Theme.F(1.5f), pw, Theme.F(3)), Theme.F(1.5f));
            using (var b = new LinearGradientBrush(new RectangleF(px - 1, py, Math.Max(2, pw * k) + 2, Theme.F(3)), pal[0], Theme.Mix(pal[0], Color.White, 0.4f), 0f))
            using (var pth = Theme.Round(new RectangleF(px, py - Theme.F(1.5f), Math.Max(Theme.F(3), pw * k), Theme.F(3)), Theme.F(1.5f))) g.FillPath(b, pth);
            float hr = Theme.F(5.5f);
            Theme.Fill(g, Color.White, new RectangleF(px + pw * k - hr, py - hr, hr * 2, hr * 2), hr);
            var seek = new RectangleF(px, py - Theme.F(9), pw, Theme.F(18));
            var hit = AddHit("seek", seek, null, null);
            hit.Down = p => { if (d > 0) P.Seek(Math.Max(0, Math.Min(1, (p.X - seek.X) / seek.Width)) * d); Poke(); };
            hit.DragTo = p => { if (d > 0) P.Seek(Math.Max(0, Math.Min(1, (p.X - seek.X) / seek.Width)) * d); Poke(); };
            // speed and volume, right
            float rx = r.Right - pad;
            float vw = r.Width * 0.11f;
            var vr = new RectangleF(rx - vw, r.Y + h / 2 - Theme.F(9), vw, Theme.F(18));
            Theme.Fill(g, Theme.Alpha(Color.White, 40), new RectangleF(vr.X, vr.Y + Theme.F(7.5f), vr.Width, Theme.F(3)), Theme.F(1.5f));
            float vk = (float)(P.Volume / 100.0);
            Theme.Fill(g, Theme.Mix(pal[0], Color.White, 0.2f), new RectangleF(vr.X, vr.Y + Theme.F(7.5f), vr.Width * vk, Theme.F(3)), Theme.F(1.5f));
            Theme.Fill(g, Color.White, new RectangleF(vr.X + vr.Width * vk - hr, vr.Y + Theme.F(9) - hr, hr * 2, hr * 2), hr);
            var vh = AddHit("vol", vr, null, "Volume");
            vh.Down = p => { P.Volume = (int)Math.Round(Math.Max(0, Math.Min(1, (p.X - vr.X) / vr.Width)) * 100); Poke(); };
            vh.DragTo = p => { P.Volume = (int)Math.Round(Math.Max(0, Math.Min(1, (p.X - vr.X) / vr.Width)) * 100); Poke(); };
            IconButton(g, "mute", new RectangleF(vr.X - Theme.F(36), r.Y + (h - bs) / 2, bs, bs), P.Volume <= 0 ? "mute" : "volume", () => { P.Volume = P.Volume > 0 ? 0 : 70; Poke(); }, "Volume");
            g.Restore(st);
        }

        void Intro(Graphics g)
        {
            if (intro > 0.995f) return;
            using (var b = new SolidBrush(Color.FromArgb((int)(255 * (1 - intro)), 0, 0, 0))) g.FillRectangle(b, 0, 0, Width, Height);
        }
    }

    // a song's big cover, its colors and its sky (blurred cover), prepared on a worker thread
    sealed class StageSong : IDisposable
    {
        public Track T; public Bitmap Cover; public Color[] Pal; public Bitmap Sky;
        public void Dispose() { Cover?.Dispose(); Sky?.Dispose(); }

        // small: the cover AURA already has (copied here on the UI thread); done runs on the UI thread
        public static StageSong Make(Track t, Bitmap small, int coverPx, int skyW, int skyH, Action<StageSong> done, Control ui)
            => Make(t, small, coverPx, skyW, skyH, done, a => { if (!ui.IsDisposed) ui.BeginInvoke(a); });

        public static StageSong Make(Track t, Bitmap small, int coverPx, int skyW, int skyH, Action<StageSong> done, Action<Action> post)
        {
            var s = new StageSong { T = t };
            Bitmap copy = null;
            try { if (small != null) copy = new Bitmap(small); } catch { }
            var accent = Theme.AccentTarget;
            new System.Threading.Thread(() =>
            {
                try
                {
                    s.Cover = Art.LoadBig(t, coverPx) ?? copy;
                    if (s.Cover != copy) copy?.Dispose();
                    s.Pal = Cosmos.Palette(s.Cover, accent);
                    s.Sky = Cosmos.Backdrop(s.Cover, s.Pal, skyW, skyH);
                }
                catch (Exception e) { App.Log("full screen song: " + e.Message); s.Pal = s.Pal ?? Cosmos.Palette(null, accent); s.Sky = s.Sky ?? Cosmos.Backdrop(null, s.Pal, 64, 36); }
                try { post(() => done(s)); } catch { }
            }) { IsBackground = true, Priority = System.Threading.ThreadPriority.BelowNormal, Name = "stage song" }.Start();
            return s;
        }
    }
}
