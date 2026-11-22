using System;
using System.Drawing;
using System.Windows.Forms;

namespace Aura
{
    sealed class TopBar : Canvas
    {
        readonly IShell shell;
        public readonly TextBox Search;
        readonly Timer debounce = new Timer { Interval = 220 };
        bool quiet;

        public TopBar(IShell shell)
        {
            this.shell = shell;
            Scrollable = false;
            BackColor = Theme.Bg;
            Height = Theme.P(64);
            Search = new TextBox
            {
                BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.Text, Font = Theme.Font(14),
            };
            Controls.Add(Search);
            Search.HandleCreated += (s, e) => Win.Cue(Search.Handle, "Search songs, artists, albums");
            Search.TextChanged += (s, e) => { if (!quiet) { debounce.Stop(); debounce.Start(); } Invalidate(); };
            Search.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) { Search.Text = ""; e.Handled = e.SuppressKeyPress = true; Parent?.Focus(); }
                if (e.KeyCode == Keys.Enter) { debounce.Stop(); RunSearch(); e.Handled = e.SuppressKeyPress = true; }
            };
            Search.GotFocus += (s, e) => Invalidate();
            Search.LostFocus += (s, e) => Invalidate();
            debounce.Tick += (s, e) => { debounce.Stop(); RunSearch(); };
        }

        void RunSearch()
        {
            var q = Search.Text.Trim();
            if (q.Length == 0) { if (shell.Current?.Kind == "search") shell.Back(); return; }
            shell.Go(new Route("search", q));
        }

        // when going back to a search, put its text back without searching again
        public void Show(string text)
        {
            if (Search.Text == text) return;
            quiet = true; Search.Text = text; Search.SelectionStart = text.Length; quiet = false;
        }

        RectangleF box;

        protected override void Draw(Graphics g, RectangleF view)
        {
            float y = (Height - Theme.F(36)) / 2;
            IconButton(g, "back", new RectangleF(Theme.F(20), y, Theme.F(36), Theme.F(36)), "back", shell.Back, "Back", circleBg: true);
            IconButton(g, "fwd", new RectangleF(Theme.F(62), y, Theme.F(36), Theme.F(36)), "forward", shell.Forward, "Forward", circleBg: true);
            if (!shell.CanBack) Theme.Fill(g, Theme.Alpha(Theme.Bg, 150), new RectangleF(Theme.F(20), y, Theme.F(36), Theme.F(36)), Theme.F(18));
            if (!shell.CanForward) Theme.Fill(g, Theme.Alpha(Theme.Bg, 150), new RectangleF(Theme.F(62), y, Theme.F(36), Theme.F(36)), Theme.F(18));

            float w = Math.Min(Theme.F(420), Width - Theme.F(140) - Theme.F(150));
            box = new RectangleF(Theme.F(114), y - Theme.F(2), Math.Max(Theme.F(160), w), Theme.F(40));
            bool focus = Search.Focused;
            Theme.Fill(g, Theme.Surface, box, box.Height / 2);
            Theme.Stroke(g, focus ? Theme.Alpha(Theme.Accent, 200) : Theme.Alpha(Theme.Ink, Hover("box") ? 50 : 24), box, box.Height / 2, focus ? 1.6f : 1);
            float s = Theme.F(18);
            Icons.Draw(g, "search", new RectangleF(box.X + Theme.F(14), box.Y + (box.Height - s) / 2, s, s), focus ? Theme.Text : Theme.Sub);
            int th = Search.PreferredHeight;
            var tb = new Rectangle((int)(box.X + Theme.F(42)), (int)(box.Y + (box.Height - th) / 2), (int)(box.Width - Theme.F(42) - Theme.F(38)), th);
            if (Search.Bounds != tb) Search.Bounds = tb;
            if (Search.Text.Length == 0 && !focus && !App.IsWindows)
                Theme.Write(g, "Search songs, artists, albums", Theme.Font(14), Theme.Faint, new RectangleF(tb.X, box.Y, tb.Width, box.Height));
            AddHit("box", box, () => Search.Focus()).Cursor = Cursors.IBeam;
            if (Search.Text.Length > 0)
                IconButton(g, "clear", new RectangleF(box.Right - Theme.F(34), box.Y + Theme.F(6), Theme.F(28), Theme.F(28)), "close", () => { Search.Text = ""; Search.Focus(); }, "Clear");

            // an update, or scanning, on the right
            var scan = shell.Lib.Scan;
            if (Updater.Available || Updater.Downloading)
            {
                string label = Updater.Downloading ? (Updater.Percent >= 0 ? "Updating… " + Updater.Percent + "%" : "Updating…") : "Update to " + Updater.Latest;
                var f = Theme.Font(13.5f, FontStyle.Regular, true);
                float uw = Theme.Measure(g, label, f) + Theme.F(36) + Theme.F(26);
                Pill(g, "update", Width - Theme.F(28) - uw, y, label, Updater.Downloading ? (Action)null : () => (FindForm() as MainForm)?.InstallUpdate(), true, "download", 36,
                    Updater.Downloading ? null : "A new version of AURA Native is ready");
            }
            else if (scan.Running)
            {
                string msg = "Scanning your music… " + scan.Seen.ToString("N0") + " files";
                Theme.Write(g, msg, Theme.Font(12.5f), Theme.Sub, new RectangleF(box.Right + Theme.F(16), 0, Width - box.Right - Theme.F(36), Height), Theme.Right);
            }
        }
    }
}
