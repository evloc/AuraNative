using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Aura
{
    // Search LRCLIB and Genius for a song's lyrics and pick the right ones.
    sealed class LyricsFinder : Form
    {
        readonly IShell shell;
        readonly Track track;
        readonly TextBox query, preview;
        readonly ListBox list;
        readonly Label status;
        readonly Button use, search;
        List<Found> results = new List<Found>();
        int searchNo;

        public LyricsFinder(IShell shell, Track t)
        {
            this.shell = shell; track = t;
            Text = "Find lyrics — " + t.Name;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Surface; ForeColor = Theme.Text; Font = Theme.Font(13.5f);
            ClientSize = new Size(Theme.P(900), Theme.P(600));
            MinimumSize = new Size(Theme.P(640), Theme.P(420));
            ShowInTaskbar = false;
            HandleCreated += (s, e) => Win.DarkTitle(Handle);

            int pad = Theme.P(18);
            query = new TextBox { BackColor = Theme.Surface2, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Font(15) };
            query.Text = (LyricsOnline.MainArtist(t.Artist) + " " + LyricsOnline.CleanTitle(t.Title.Length > 0 ? t.Title : System.IO.Path.GetFileNameWithoutExtension(t.Path))).Trim();
            search = Btn("Search", true);
            search.Click += (s, e) => Search(query.Text);
            query.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Search(query.Text); } };
            status = new Label { ForeColor = Theme.Sub, AutoSize = false, Font = Theme.Font(13), TextAlign = ContentAlignment.MiddleLeft };
            list = new ListBox
            {
                BackColor = Theme.Bg, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = Theme.P(58), IntegralHeight = false
            };
            list.DrawItem += DrawItem;
            list.SelectedIndexChanged += (s, e) => ShowPreview();
            list.DoubleClick += (s, e) => Use();
            preview = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Theme.Bg, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Font = Theme.Font(14), WordWrap = true };
            use = Btn("Use these lyrics", true);
            use.Enabled = false;
            use.Click += (s, e) => Use();
            var cancel = Btn("Cancel", false);
            cancel.Click += (s, e) => Close();
            CancelButton = cancel;
            var note = new Label { Text = "Saved in AURA — your music file isn't changed.", ForeColor = Theme.Faint, AutoSize = false, Font = Theme.Font(12.5f), TextAlign = ContentAlignment.MiddleLeft };
            Controls.AddRange(new Control[] { query, search, status, list, preview, use, cancel, note });

            void Place()
            {
                int w = ClientSize.Width, h = ClientSize.Height, bh = Theme.P(38);
                search.SetBounds(w - pad - Theme.P(110), pad, Theme.P(110), bh);
                query.SetBounds(pad, pad + (bh - query.PreferredHeight) / 2, w - pad * 3 - Theme.P(110), query.PreferredHeight);
                status.SetBounds(pad, pad + bh + Theme.P(6), w - pad * 2, Theme.P(26));
                int top = pad + bh + Theme.P(38), bottom = h - pad - bh - Theme.P(12);
                int lw = (int)((w - pad * 3) * 0.46);
                list.SetBounds(pad, top, lw, bottom - top);
                preview.SetBounds(pad * 2 + lw, top, w - pad * 3 - lw, bottom - top);
                use.SetBounds(w - pad - Theme.P(170), h - pad - bh, Theme.P(170), bh);
                cancel.SetBounds(w - pad - Theme.P(170) - Theme.P(10) - Theme.P(100), h - pad - bh, Theme.P(100), bh);
                note.SetBounds(pad, h - pad - bh, w - pad * 3 - Theme.P(290), bh);
            }
            Resize += (s, e) => Place();
            Place();
            Shown += (s, e) => Search(query.Text);
        }

        Button Btn(string text, bool primary)
        {
            var b = new Button { Text = text, FlatStyle = FlatStyle.Flat, BackColor = primary ? Theme.Accent : Theme.Surface2, ForeColor = primary ? Color.White : Theme.Text, Font = Theme.Font(13.5f, FontStyle.Regular, true), Cursor = Cursors.Hand };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        void Search(string q)
        {
            int no = ++searchNo;
            status.Text = "Searching LRCLIB and Genius…";
            list.Items.Clear(); results.Clear(); preview.Text = ""; use.Enabled = false;
            Task.Run(() => LyricsOnline.Search(track, q)).ContinueWith(tk =>
            {
                if (IsDisposed || no != searchNo) return;
                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed || no != searchNo) return;
                    if (tk.IsFaulted) { status.Text = "Something went wrong: " + tk.Exception?.GetBaseException().Message; return; }
                    var r = tk.Result;
                    results = r.Found;
                    if (r.Offline) status.Text = "Couldn't reach the lyric sites — check your internet and try again.";
                    else if (results.Count == 0) status.Text = "Nothing found. Try fewer words, or just the song name.";
                    else status.Text = Theme.Count(results.Count, "result") + ". The best match is first.";
                    foreach (var f in results) list.Items.Add(f);
                    if (results.Count > 0) list.SelectedIndex = 0;
                }));
            });
        }

        string LengthNote(Found f)
        {
            if (f.Duration <= 0 || track.Duration <= 0) return "";
            double diff = Math.Abs(f.Duration - track.Duration);
            return diff <= 2.5 ? " · same length" : " · " + Theme.Time(f.Duration) + " (yours " + Theme.Time(track.Duration) + ")";
        }

        void DrawItem(object s, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= results.Count) return;
            var g = e.Graphics;
            Theme.Setup(g);
            var f = results[e.Index];
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(Theme.Bg)) g.FillRectangle(b, e.Bounds);
            var r = new RectangleF(e.Bounds.X + Theme.F(4), e.Bounds.Y + Theme.F(3), e.Bounds.Width - Theme.F(8), e.Bounds.Height - Theme.F(6));
            if (sel) Theme.Fill(g, Theme.Alpha(Theme.Accent, 70), r, Theme.F(8));
            Theme.Write(g, f.Title + " — " + f.Artist, Theme.Font(14, FontStyle.Regular, true), Theme.Text, new RectangleF(r.X + Theme.F(12), r.Y + Theme.F(6), r.Width - Theme.F(24), Theme.F(22)));
            string kind = f.Source == "lrclib" ? (f.IsTimed ? "timed" : "words only") : "words only";
            bool lengthOff = f.IsTimed && f.Duration > 0 && track.Duration > 0 && Math.Abs(f.Duration - track.Duration) > 2.5;
            Theme.Write(g, f.SourceName + " · " + kind + LengthNote(f) + (lengthOff ? " — timings may be off" : ""), Theme.Font(12.5f), lengthOff ? Color.FromArgb(255, 190, 110) : Theme.Sub,
                new RectangleF(r.X + Theme.F(12), r.Y + Theme.F(28), r.Width - Theme.F(24), Theme.F(20)));
        }

        void ShowPreview()
        {
            int i = list.SelectedIndex;
            use.Enabled = false;
            if (i < 0 || i >= results.Count) { preview.Text = ""; return; }
            var f = results[i];
            if (f.Plain != null) { preview.Text = f.Plain.Replace("\n", Environment.NewLine); use.Enabled = f.Plain.Trim().Length > 0; return; }
            preview.Text = "Getting the words from Genius…";
            int no = searchNo;
            Task.Run(() => LyricsOnline.GeniusLyrics(f.Url)).ContinueWith(tk =>
            {
                if (IsDisposed) return;
                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed || no != searchNo) return;
                    if (tk.IsFaulted) { if (list.SelectedIndex == i) preview.Text = "Couldn't get the words: " + tk.Exception?.GetBaseException().Message; return; }
                    f.Plain = tk.Result;
                    if (list.SelectedIndex == i) ShowPreview();
                }));
            });
        }

#if TESTHOOKS
        internal int TestCount => results.Count;
        internal string TestStatus => status.Text;
        internal string TestPreview => preview.Text;
        internal void TestPick(int i) { list.SelectedIndex = i; }
        internal void TestUse() => Use();
#endif
        void Use()
        {
            int i = list.SelectedIndex;
            if (i < 0 || i >= results.Count || string.IsNullOrWhiteSpace(results[i].Plain)) return;
            var f = results[i];
            shell.Lyrics.Save(track, f.IsTimed ? f.Synced : f.Plain, f.SourceName, true);
            shell.Toast("Lyrics saved for " + track.Name);
            Close();
        }
    }
}
