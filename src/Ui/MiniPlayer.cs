using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Aura
{
    // A small window that stays on top of everything: cover, song, buttons, and where you are in it.
    sealed class MiniForm : Form
    {
        readonly MiniView view;
        readonly IShell shell;

        public MiniForm(IShell shell)
        {
            this.shell = shell;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Bar;
            Text = "AURA mini player";
            Size = new Size(Theme.P(380), Theme.P(108));
            var wa = Screen.PrimaryScreen.WorkingArea;
            var p = shell.Prefs;
            var at = new Point(p.MiniX, p.MiniY);
            if (p.MiniX < 0 || !Array.Exists(Screen.AllScreens, s => s.WorkingArea.Contains(new Point(at.X + 20, at.Y + 20))))
                at = new Point(wa.Right - Width - Theme.P(24), wa.Bottom - Height - Theme.P(24));
            Location = at;
            view = new MiniView(shell, this) { Dock = DockStyle.Fill };
            Controls.Add(view);
        }

        protected override CreateParams CreateParams
        {
            get { var c = base.CreateParams; c.ClassStyle |= 0x20000; return c; }      // a soft shadow
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (App.IsWindows)
            {
                try { int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4); } catch { }    // rounded corners on Windows 11
            }
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

        public void StartMove()
        {
            if (!App.IsWindows) return;
            ReleaseCapture();
            SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero);       // like dragging a title bar
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            if (WindowState == FormWindowState.Normal && Visible) { shell.Prefs.MiniX = Left; shell.Prefs.MiniY = Top; }
        }

        public void Changed() => view.Invalidate();
    }

    sealed class MiniView : Canvas
    {
        readonly IShell shell;
        readonly MiniForm form;
        readonly Timer tick = new Timer { Interval = 500 };
        float seekDrag = -1;

        public MiniView(IShell shell, MiniForm form)
        {
            this.shell = shell; this.form = form;
            Scrollable = false;
            BackColor = Theme.Bar;
            tick.Tick += (s, e) => { if (shell.Player.IsPlaying) Invalidate(); };
            tick.Start();
        }

        protected override void Dispose(bool disposing) { if (disposing) tick.Dispose(); base.Dispose(disposing); }

        protected override void Draw(Graphics g, RectangleF v)
        {
            var P = shell.Player;
            var t = P.Current;
            using (var b = new LinearGradientBrush(new RectangleF(0, 0, Width, Height), Theme.Alpha(Theme.Accent, 60), Theme.Alpha(Theme.Accent, 8), 0f))
                g.FillRectangle(b, 0, 0, Width, Height);
            using (var pen = new Pen(Theme.Hair)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            float pad = Theme.F(12), art = Height - pad * 2 - Theme.F(6);
            var ar = new RectangleF(pad, pad, art, art);
            var img = Art.Get(t);
            if (img != null) Theme.Image(g, img, ar, Theme.F(8)); else Theme.NoArt(g, ar, Theme.F(8), t?.Id ?? 3);
            AddHit("art", ar, () => { form.Owner?.Activate(); }, "Back to AURA");
            float tx = ar.Right + Theme.F(12), tw = Width - tx - Theme.F(40);
            Theme.Write(g, t?.Name ?? "Nothing playing", Theme.Font(13.5f, FontStyle.Regular, true), Theme.Text, new RectangleF(tx, pad - Theme.F(2), tw, Theme.F(22)));
            Theme.Write(g, t?.ArtistName ?? "", Theme.Font(12), Theme.Sub, new RectangleF(tx, pad + Theme.F(19), tw, Theme.F(18)));
            // buttons
            float by = pad + Theme.F(42), bs = Theme.F(30);
            IconButton(g, "prev", new RectangleF(tx - Theme.F(4), by, bs, bs), "prev", P.Prev, "Previous");
            PlayButton(g, "play", new RectangleF(tx + bs + Theme.F(2), by - Theme.F(2), bs + Theme.F(4), bs + Theme.F(4)), P.IsPlaying, P.Toggle, P.IsPlaying ? "Pause" : "Play");
            IconButton(g, "next", new RectangleF(tx + bs * 2 + Theme.F(12), by, bs, bs), "next", P.Next, "Next");
            if (t != null) IconButton(g, "fav", new RectangleF(tx + bs * 3 + Theme.F(22), by, bs, bs), "heart", () => shell.Lib.SetFav(t, !t.Fav), t.Fav ? "Remove from Favorites" : "Add to Favorites", t.Fav, filled: t.Fav, dot: false);
            Theme.Write(g, Theme.Time(seekDrag >= 0 ? seekDrag * P.Duration : P.Position) + " / " + Theme.Time(P.Duration), Theme.Font(11), Theme.Faint,
                new RectangleF(tx, by, Width - tx - Theme.F(14), bs), Theme.Right);
            // close and back-to-AURA
            IconButton(g, "close", new RectangleF(Width - Theme.F(34), Theme.F(6), Theme.F(26), Theme.F(26)), "close", shell.ToggleMini, "Close the mini player");
            // where you are in the song
            double dur = P.Duration;
            float k = seekDrag >= 0 ? seekDrag : dur > 0 ? (float)(P.SmoothPosition / dur) : 0;
            var sr = new RectangleF(0, Height - Theme.F(5), Width, Theme.F(5));
            Theme.Fill(g, Theme.Alpha(Theme.Text, 30), sr, 0);
            Theme.Fill(g, Theme.Accent, new RectangleF(0, sr.Y, Width * Math.Max(0, Math.Min(1, k)), sr.Height), 0);
            if (P.Looping && dur > 0)
                Theme.Fill(g, Theme.Alpha(Theme.Ink, 70), new RectangleF((float)(P.LoopA / dur) * Width, sr.Y, (float)((P.LoopB - P.LoopA) / dur) * Width, sr.Height), 0);
            var hit = AddHit("seek", new RectangleF(0, Height - Theme.F(12), Width, Theme.F(12)), null, null);
            hit.Cursor = Cursors.Hand;
            hit.DragTo = p => { if (t != null) { seekDrag = Math.Max(0, Math.Min(1, p.X / Width)); Invalidate(); } };
            hit.DragEnd = () => { if (seekDrag >= 0 && t != null) P.Seek(seekDrag * P.Duration); seekDrag = -1; };
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && HitAt(e.Location) == null) { form.StartMove(); return; }
            base.OnMouseDown(e);
        }
    }
}
