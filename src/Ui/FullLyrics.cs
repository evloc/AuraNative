using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

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
        readonly LyricsPane pane = new LyricsPane { Stage = true };
        readonly Space space = new Space();
        readonly FrameClock frames;
        DateTime lastMove = DateTime.Now;
        Track shown;
        float ui = 1, intro = 0, songFade = 1;     // eased 0..1: the buttons, the first fade from black, a new song coming in
        bool cursorHidden;
        int frame;

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
            if (disposing) { frames.Dispose(); space.Dispose(); pane.Dispose(); ShowCursor(); }
            base.Dispose(disposing);
        }

        void ShowCursor() { if (cursorHidden) { Cursor.Show(); cursorHidden = false; } }

        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (e.Location != lastPoint) { lastPoint = e.Location; Poke(); } }
        Point lastPoint;
        protected override void OnMouseDoubleClick(MouseEventArgs e) { base.OnMouseDoubleClick(e); owner.Close(); }
        protected override void OnMouseWheel(MouseEventArgs e) { Poke(); pane.Wheel(e.Delta, Height * 0.7f); }

        // how loud the song is right now, from what AURA learned by listening to it (-1 when it hasn't yet)
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

        void DrawStage(Graphics g)
        {
            float dt = frames.Step();
            var P = shell.Player;
            var t = P.Current;
            double pos = P.SmoothPosition;
            if (t != shown)
            {
                if (shown != null && t != null) space.Warp();
                shown = t; songFade = 0;
                pane.Set(t == null ? null : shell.Lyrics.Get(t), t?.Id ?? -1);
                if (t != null) shell.Sound.Want(t);
            }
            songFade += (1 - songFade) * FrameClock.Ease(dt, 3.5f);
            intro += (1 - intro) * FrameClock.Ease(dt, 2.5f);
            bool showUi = (DateTime.Now - lastMove).TotalSeconds < 2.5;
            ui += ((showUi ? 1 : 0) - ui) * FrameClock.Ease(dt, showUi ? 10 : 3);
            if (!showUi && !cursorHidden && App.IsWindows && Form.ActiveForm == owner) { Cursor.Hide(); cursorHidden = true; }

            // space
            space.Prepare(Size, Theme.AccentTarget);
            space.Draw(g, new Rectangle(0, 0, Width, Height), dt, frames.Now, t == null ? -1 : Loudness(t, pos), P.IsPlaying);

            if (t == null)
            {
                Theme.Write(g, "Nothing playing", Theme.Font(30, FontStyle.Bold), Theme.Alpha(Color.White, 220), new RectangleF(0, 0, Width, Height), Theme.Center);
                Intro(g); return;
            }

            float pad = Theme.F(44);
            var area = new RectangleF(Width * 0.09f, Height * 0.06f, Width * 0.82f, Height * 0.72f);
            if (pane.Lyrics == null) DrawCoverCenter(g, t);
            else
            {
                pane.Glow = Theme.Mix(Theme.Accent, Color.White, 0.35f);
                pane.Duration = P.Duration;
                pane.Fade = songFade;
                pane.Draw(g, area, pos, this, sec => { P.Seek(sec); Poke(); }, "st");
            }

            // the song, bottom left, with a soft glow behind the cover
            float art = Theme.F(86);
            var ar = new RectangleF(pad, Height - pad - art - Theme.F(10), art, art);
            space.GlowAt(g, RectangleF.Inflate(ar, art * 0.7f, art * 0.7f), 0.45f + space.Energy * 0.4f);
            var img = Art.Get(t);
            if (img != null) Theme.Image(g, img, ar, Theme.F(12)); else Theme.NoArt(g, ar, Theme.F(12), t.Id);
            int ta = (int)(255 * songFade);
            Theme.Write(g, t.Name, Theme.Font(23, FontStyle.Bold), Color.FromArgb(ta, 255, 255, 255), new RectangleF(ar.Right + Theme.F(20), ar.Y + Theme.F(10), Width * 0.42f, Theme.F(34)));
            Theme.Write(g, t.ArtistName + (t.Album.Length > 0 ? "  ·  " + t.Album : ""), Theme.Font(15), Color.FromArgb((int)(ta * 0.7f), 255, 255, 255), new RectangleF(ar.Right + Theme.F(20), ar.Y + Theme.F(46), Width * 0.42f, Theme.F(24)));
            var snd = shell.Sound.Get(t);
            if (snd != null && snd.Bpm > 0)
                Theme.Write(g, snd.Bpm.ToString("0") + " BPM" + (snd.Key.Length > 0 ? "  ·  " + snd.Key : ""), Theme.Font(12.5f), Color.FromArgb((int)(ta * 0.45f), 255, 255, 255), new RectangleF(ar.Right + Theme.F(20), ar.Y + Theme.F(72), Width * 0.42f, Theme.F(20)));

            // how far through the song: a thin line with a glowing head
            double d = P.Duration;
            float k = d > 0 ? (float)Math.Max(0, Math.Min(1, pos / d)) : 0;
            float by = Height - Theme.F(4);
            Theme.Fill(g, Theme.Alpha(Color.White, 26), new RectangleF(0, by, Width, Theme.F(4)), 0);
            using (var b = new LinearGradientBrush(new RectangleF(0, by, Math.Max(2, Width * k), Theme.F(4)), Theme.Alpha(Theme.Accent, 160), Theme.Mix(Theme.Accent, Color.White, 0.5f), 0f))
                g.FillRectangle(b, 0, by, Width * k, Theme.F(4));
            using (var p = new GraphicsPath())
            {
                float hx = Width * k, hr = Theme.F(16);
                p.AddEllipse(hx - hr, by + Theme.F(2) - hr, hr * 2, hr * 2);
                using (var pb = new PathGradientBrush(p) { CenterColor = Theme.Alpha(Color.White, 200), SurroundColors = new[] { Color.FromArgb(0, Theme.Accent) } })
                    g.FillPath(pb, p);
            }

            // buttons fade in when the mouse moves
            if (ui > 0.02f)
            {
                int ua = (int)(255 * ui);
                float cx = Width / 2f, cy = Height - pad - Theme.F(34);
                Theme.Write(g, Theme.Time(pos) + "  /  " + Theme.Time(d), Theme.Font(13), Color.FromArgb((int)(ua * 0.7f), 255, 255, 255), new RectangleF(Width - pad - Theme.F(300), Height - pad - Theme.F(30), Theme.F(300), Theme.F(24)), Theme.Right);
                // they slide away off the edges when the mouse rests
                var st = g.Save();
                g.TranslateTransform(0, (1 - ui) * Theme.F(140));
                PlayButton(g, "play", new RectangleF(cx - Theme.F(30), cy - Theme.F(30), Theme.F(60), Theme.F(60)), P.IsPlaying, () => { P.Toggle(); Poke(); }, P.IsPlaying ? "Pause (Space)" : "Play (Space)");
                IconButton(g, "prev", new RectangleF(cx - Theme.F(96), cy - Theme.F(23), Theme.F(46), Theme.F(46)), "prev", () => { P.Prev(); Poke(); }, "Previous (P)", circleBg: true);
                IconButton(g, "next", new RectangleF(cx + Theme.F(50), cy - Theme.F(23), Theme.F(46), Theme.F(46)), "next", () => { P.Next(); Poke(); }, "Next (N)", circleBg: true);
                g.Restore(st);
                st = g.Save();
                g.TranslateTransform(0, -(1 - ui) * Theme.F(120));
                IconButton(g, "close", new RectangleF(Width - pad - Theme.F(44), pad - Theme.F(14), Theme.F(44), Theme.F(44)), "close", owner.Close, "Close (Esc)", circleBg: true);
                g.Restore(st);
            }
            Intro(g);
        }

        // no lyrics: the cover floats in the middle, pulsing gently with the music
        void DrawCoverCenter(Graphics g, Track t)
        {
            float s = (float)Math.Round(Math.Min(Width, Height) * 0.34f), pulse = 1 + space.Energy * 0.025f;
            var r = new RectangleF((float)Math.Round((Width - s) / 2), (float)Math.Round(Height * 0.4f - s / 2), s, s);
            space.GlowAt(g, RectangleF.Inflate(r, s * 0.6f, s * 0.6f), 0.5f + 0.45f * space.Energy);
            var img = Art.Get(t);
            // the cover is drawn at one size (so it's prepared once) and pulses by scaling
            var st = g.Save();
            g.TranslateTransform(r.X + s / 2, r.Y + s / 2); g.ScaleTransform(pulse, pulse); g.TranslateTransform(-(r.X + s / 2), -(r.Y + s / 2));
            if (img != null) Theme.Image(g, img, r, s * 0.05f); else Theme.NoArt(g, r, s * 0.05f, t.Id);
            g.Restore(st);
            Theme.Write(g, "No lyrics for this song yet", Theme.Font(17), Theme.Alpha(Color.White, (int)(150 * songFade)), new RectangleF(0, r.Bottom + Theme.F(28), Width, Theme.F(30)), Theme.Center);
        }

        void Intro(Graphics g)
        {
            if (intro > 0.995f) return;
            using (var b = new SolidBrush(Color.FromArgb((int)(255 * (1 - intro)), 0, 0, 0))) g.FillRectangle(b, 0, 0, Width, Height);
        }
    }
}
