using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Aura
{
    // Ctrl+K quick search, performance mode, and "what's new" after an update.
    sealed partial class MainForm
    {
        public void QuickFind()
        {
            if (QuickSearch.Open != null && !QuickSearch.Open.IsDisposed) { QuickSearch.Open.Done(); return; }
            new QuickSearch(this, this).Show(this);
        }

        // ---------- performance mode ----------
        public void TogglePerformance()
        {
            Prefs.Lite = !Prefs.Lite;
            ApplyPerformance();
            Prefs.Save();
            Toast(Prefs.Lite ? "Performance mode on — no effects, fewer frames, and AURA's background work waits (P to turn off)"
                             : "Performance mode off");
        }

        public void ApplyPerformance()
        {
            App.Lite = Prefs.Lite;
            Sound.Kick();
            Bar?.Invalidate(); Page?.Invalidate();
        }

        // ---------- what's new ----------
        public static readonly (string version, string[] changes)[] Notes =
        {
            ("0.5.0", new[]
            {
                "About tab on Now Playing: the artists on the song (featured ones too), credits like producer, writer, label and date from the song's tags and name, its album and era, and how you've listened to it",
                "Quick search from anywhere: Ctrl+K. Songs, artists, albums and lines of lyrics — pick a line and the song starts right where it's sung",
                "Performance mode: P or Ctrl+P (or the lightning button) turns off the effects, draws fewer frames and pauses AURA's background work",
                "A new look for full screen: the sky is made from the cover, the cover sits big on the left with the song's name under it, and the lyrics are calmer — the line being sung is white with a glowing dot, the rest fade. Press V in full screen (or the button at the top) to switch back to space",
                "This window: what changed, the first time a new version opens (Settings → What's new to see it again)",
            }),
            ("0.4.13", new[]
            {
                "The speech model (timing lyrics) runs on its own now, so if it crashes on a song, only it closes — the music keeps playing and that song is skipped",
            }),
            ("0.4.12", new[]
            {
                "Songs with ’ or accents in their names play in the main player",
                "The Windows media pop-up (volume keys, lock screen) works again",
                "If AURA ever closes without warning, the next start says so, and Copy report shows what it was doing",
            }),
        };

        void MaybeWhatsNew()
        {
            string seen = Prefs.SeenVersion ?? "";
            if (seen == App.Version) return;
            bool existing = Lib.Tracks.Count > 0 || Prefs.Folders.Count > 0;
            Prefs.SeenVersion = App.Version; Prefs.Save();
            if (!existing || Environment.GetEnvironmentVariable("AURA_NO_WHATS_NEW") != null || Environment.GetCommandLineArgs().Contains("--ui-test")) return;
            var later = new Timer { Interval = 1500 };
            later.Tick += (s, e) => { later.Stop(); later.Dispose(); WhatsNew(seen); };
            later.Start();
        }

        // since: the version they had before (notes newer than it are shown); "" shows the newest
        public void WhatsNew(string since = "")
        {
            var show = Notes.Where(n => since.Length > 0 ? Updater.Newer(n.version, since) : n.version == Notes[0].version).ToList();
            if (show.Count == 0) show = Notes.Take(1).ToList();
            using (var f = new WhatsNewForm(show)) f.ShowDialog(this);
        }
    }

    sealed class WhatsNewForm : Form
    {
        public WhatsNewForm(List<(string version, string[] changes)> notes)
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Surface; Text = "What's new in AURA";
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Enter) Close(); };
            var view = new WhatsNewView(notes, Close) { Dock = DockStyle.Fill };
            Controls.Add(view);
            Size = new Size(Theme.P(560), Theme.P(200));
            using (var g = CreateGraphics()) Height = (int)Math.Min(Screen.FromPoint(Cursor.Position).WorkingArea.Height * 0.85f, view.Measure(g, Theme.P(560)));
        }
        protected override CreateParams CreateParams { get { var c = base.CreateParams; c.ClassStyle |= 0x20000; return c; } }
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (App.IsWindows) try { int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4); } catch { }
        }
    }

    sealed class WhatsNewView : Canvas
    {
        readonly List<(string version, string[] changes)> notes;
        readonly Action close;
        public WhatsNewView(List<(string version, string[] changes)> notes, Action close) { this.notes = notes; this.close = close; Scrollable = true; BackColor = Theme.Surface; }

        public float Measure(Graphics g, float width)
        {
            float y = Theme.F(110), w = width - Theme.F(96);
            foreach (var n in notes)
            {
                y += Theme.F(40);
                foreach (var c in n.changes) y += g.MeasureString(c, Theme.Font(14), (int)w).Height + Theme.F(12);
                y += Theme.F(10);
            }
            return y + Theme.F(76);
        }

        protected override void Draw(Graphics g, RectangleF view)
        {
            float pad = Theme.F(36);
            // a soft glow of the accent color at the top
            using (var b = new System.Drawing.Drawing2D.LinearGradientBrush(new RectangleF(0, 0, Width, Theme.F(120)), Theme.Alpha(Theme.Accent, 70), Theme.Alpha(Theme.Accent, 0), 90f))
                g.FillRectangle(b, 0, 0, Width, Theme.F(120));
            Theme.Write(g, "WHAT'S NEW", Theme.Font(12, FontStyle.Regular, true), Theme.AccentText, new RectangleF(pad, Theme.F(30), Width, Theme.F(20)));
            Theme.Write(g, "AURA " + notes[0].version, Theme.Font(28, FontStyle.Bold), Theme.Text, new RectangleF(pad, Theme.F(50), Width - pad * 2, Theme.F(42)));
            float y = Theme.F(110), w = Width - pad * 2 - Theme.F(24);
            for (int k = 0; k < notes.Count; k++)
            {
                var n = notes[k];
                if (notes.Count > 1 || k > 0) Theme.Write(g, n.version, Theme.Font(13, FontStyle.Bold), Theme.Sub, new RectangleF(pad, y + Theme.F(8), Width, Theme.F(22)));
                y += Theme.F(40);
                foreach (var c in n.changes)
                {
                    float h = g.MeasureString(c, Theme.Font(14), (int)w).Height;
                    using (var dot = new SolidBrush(Theme.AccentText)) g.FillEllipse(dot, pad + Theme.F(2), y + Theme.F(8), Theme.F(6), Theme.F(6));
                    Theme.Write(g, c, Theme.Font(14), Theme.Text, new RectangleF(pad + Theme.F(24), y, w, h + Theme.F(4)), Theme.Wrap);
                    y += h + Theme.F(12);
                }
                y += Theme.F(10);
            }
            ContentHeight = y + Theme.F(76);
            Pill(g, "ok", Width - pad - PillWidth(g, "Got it"), y + Theme.F(12), "Got it", close, true);
        }
    }
}
