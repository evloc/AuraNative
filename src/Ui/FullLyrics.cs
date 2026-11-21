using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Aura
{
    // Full-screen lyrics over a slow, starry space scene colored by the cover.
    sealed class FullLyricsForm : Form
    {
        public FullLyricsForm(IShell shell, Screen screen)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = screen.Bounds;
            BackColor = Color.Black;
            ShowInTaskbar = false;
            KeyPreview = true;
            Text = "AURA lyrics";
            var view = new StageView(shell, this) { Dock = DockStyle.Fill };
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
                    default: return;
                }
                e.Handled = true;
            };
            FormClosed += (s, e) => view.Dispose();
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
        readonly Timer tick = new Timer { Interval = 33 };
        readonly List<(float x, float y, float r, float phase, float speed)> stars = new List<(float, float, float, float, float)>();
        Bitmap nebula; int nebulaFor; Size nebulaSize;
        DateTime lastMove = DateTime.Now, lastFrame = DateTime.Now;
        Track shown;
        float drift;

        public StageView(IShell shell, Form owner)
        {
            this.shell = shell; this.owner = owner;
            Scrollable = false;
            BackColor = Color.Black;
            var rnd = new Random(7);
            for (int i = 0; i < 170; i++) stars.Add(((float)rnd.NextDouble(), (float)rnd.NextDouble(), 0.5f + (float)Math.Pow(rnd.NextDouble(), 3) * 2.2f, (float)(rnd.NextDouble() * Math.PI * 2), 0.3f + (float)rnd.NextDouble()));
            tick.Tick += (s, e) =>
            {
                tick.Interval = shell.Player.IsPlaying ? 33 : 90;
                Invalidate();
            };
            tick.Start();
        }

        protected override void Dispose(bool disposing) { if (disposing) { tick.Dispose(); nebula?.Dispose(); } base.Dispose(disposing); }

        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); lastMove = DateTime.Now; }
        protected override void OnMouseDoubleClick(MouseEventArgs e) { base.OnMouseDoubleClick(e); owner.Close(); }
        protected override void OnMouseWheel(MouseEventArgs e) { lastMove = DateTime.Now; pane.Wheel(e.Delta, Height * 0.7f); }

        Bitmap Nebula()
        {
            int key = Theme.Accent.ToArgb();
            if (nebula != null && nebulaFor == key && nebulaSize == Size) return nebula;
            nebula?.Dispose();
            int w = Math.Max(1, Width / 4), h = Math.Max(1, Height / 4);
            nebula = new Bitmap(w, h);
            using (var g = Graphics.FromImage(nebula))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var b = new LinearGradientBrush(new Rectangle(0, 0, w, h), Color.FromArgb(4, 5, 14), Color.FromArgb(10, 6, 22), 70f)) g.FillRectangle(b, 0, 0, w, h);
                var c1 = Theme.Accent;
                var c2 = Theme.FromHsl(c1.GetHue() + 50, 0.7f, 0.45f);
                var c3 = Theme.FromHsl(c1.GetHue() - 40, 0.6f, 0.35f);
                void Blob(float cx, float cy, float rx, float ry, Color c, int a)
                {
                    using (var p = new GraphicsPath())
                    {
                        p.AddEllipse(cx - rx, cy - ry, rx * 2, ry * 2);
                        using (var pb = new PathGradientBrush(p) { CenterColor = Color.FromArgb(a, c), SurroundColors = new[] { Color.FromArgb(0, c) } })
                            g.FillPath(pb, p);
                    }
                }
                Blob(w * 0.25f, h * 0.3f, w * 0.45f, h * 0.55f, c1, 120);
                Blob(w * 0.8f, h * 0.7f, w * 0.5f, h * 0.6f, c2, 95);
                Blob(w * 0.55f, h * 0.15f, w * 0.35f, h * 0.35f, c3, 70);
            }
            nebulaFor = key; nebulaSize = Size;
            return nebula;
        }

        protected override void Draw(Graphics g, RectangleF view)
        {
            var now = DateTime.Now;
            float dt = (float)Math.Min(0.1, (now - lastFrame).TotalSeconds);
            lastFrame = now;
            if (shell.Player.IsPlaying) drift += dt;
            var t = shell.Player.Current;
            if (t != shown) { shown = t; pane.Set(t == null ? null : shell.Lyrics.Get(t), t?.Id ?? -1); }

            // space
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.DrawImage(Nebula(), new Rectangle(0, 0, Width, Height));
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            float time = (float)(now - DateTime.Today).TotalSeconds;
            foreach (var s in stars)
            {
                float x = ((s.x - drift * 0.004f * s.speed) % 1 + 1) % 1 * Width;
                float y = s.y * Height;
                float tw = 0.55f + 0.45f * (float)Math.Sin(time * 1.3f * s.speed + s.phase);
                float r = Theme.F(s.r);
                using (var b = new SolidBrush(Color.FromArgb((int)(60 + 170 * tw), 235, 240, 255))) g.FillEllipse(b, x - r / 2, y - r / 2, r, r);
            }

            bool showUi = (now - lastMove).TotalSeconds < 3;
            Cursor = showUi ? Cursors.Default : Cursors.Default;

            if (t == null) { Theme.Write(g, "Nothing playing", Theme.Font(30, FontStyle.Bold), Theme.Text, new RectangleF(0, 0, Width, Height), Theme.Center); return; }

            // lyrics in the middle
            var area = new RectangleF(Width * 0.1f, Height * 0.07f, Width * 0.8f, Height * 0.72f);
            if (pane.Lyrics == null)
                Theme.Write(g, "No lyrics for this song yet", Theme.Font(30, FontStyle.Bold), Theme.Alpha(Color.White, 200), area, Theme.Center);
            else pane.Draw(g, area, shell.Player.Position, this, sec => shell.Player.Seek(sec), "st");

            // the song, bottom left
            float pad = Theme.F(40), art = Theme.F(84);
            var ar = new RectangleF(pad, Height - pad - art, art, art);
            var img = Art.Get(t);
            if (img != null) Theme.Image(g, img, ar, Theme.F(10)); else Theme.NoArt(g, ar, Theme.F(10), t.Id);
            Theme.Write(g, t.Name, Theme.Font(22, FontStyle.Bold), Color.White, new RectangleF(ar.Right + Theme.F(18), ar.Y + Theme.F(12), Width * 0.45f, Theme.F(32)));
            Theme.Write(g, t.ArtistName + (t.Album.Length > 0 ? "  ·  " + t.Album : ""), Theme.Font(15), Theme.Alpha(Color.White, 170), new RectangleF(ar.Right + Theme.F(18), ar.Y + Theme.F(46), Width * 0.45f, Theme.F(24)));

            // how far through the song
            double d = shell.Player.Duration, p = shell.Player.Position;
            float k = d > 0 ? (float)Math.Min(1, p / d) : 0;
            Theme.Fill(g, Theme.Alpha(Color.White, 30), new RectangleF(0, Height - Theme.F(3), Width, Theme.F(3)), 0);
            Theme.Fill(g, Theme.Alpha(Theme.Mix(Theme.Accent, Color.White, 0.3f), 220), new RectangleF(0, Height - Theme.F(3), Width * k, Theme.F(3)), 0);

            if (showUi)
            {
                float cx = Width / 2f, cy = Height - pad - Theme.F(30);
                PlayButton(g, "play", new RectangleF(cx - Theme.F(28), cy - Theme.F(28), Theme.F(56), Theme.F(56)), shell.Player.IsPlaying, shell.Player.Toggle, shell.Player.IsPlaying ? "Pause (Space)" : "Play (Space)");
                IconButton(g, "prev", new RectangleF(cx - Theme.F(90), cy - Theme.F(22), Theme.F(44), Theme.F(44)), "prev", shell.Player.Prev, "Previous", circleBg: true);
                IconButton(g, "next", new RectangleF(cx + Theme.F(46), cy - Theme.F(22), Theme.F(44), Theme.F(44)), "next", shell.Player.Next, "Next", circleBg: true);
                IconButton(g, "close", new RectangleF(Width - pad - Theme.F(44), pad - Theme.F(10), Theme.F(44), Theme.F(44)), "close", owner.Close, "Close (Esc)", circleBg: true);
            }
        }
    }
}
