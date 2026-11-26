using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Aura
{
    // Ctrl+K: a small search box over whatever page you're on. Songs, artists, albums, and lines of lyrics
    // (picking one of those starts the song right at that line). Enter plays, arrows move, Esc closes.
    sealed class QuickSearch : Form
    {
        public static QuickSearch Open;
        readonly IShell shell;
        readonly TextBox box = new TextBox { BorderStyle = BorderStyle.None };
        readonly QuickView view;
        readonly Timer debounce = new Timer { Interval = 140 };

        public QuickSearch(IShell shell, Form owner)
        {
            this.shell = shell;
            Owner = owner;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            BackColor = Theme.Surface;
            Text = "AURA quick search";
            int w = Math.Min(Theme.P(640), owner.ClientSize.Width - Theme.P(60));
            Size = new Size(w, Theme.P(64));
            var oc = owner.PointToScreen(Point.Empty);
            Location = new Point(oc.X + (owner.ClientSize.Width - w) / 2, oc.Y + Math.Max(Theme.P(60), owner.ClientSize.Height / 7));
            box.BackColor = Theme.Surface; box.ForeColor = Theme.Text; box.Font = Theme.Font(17);
            view = new QuickView(shell, this) { Dock = DockStyle.Fill };
            Controls.Add(box);
            Controls.Add(view);
            box.HandleCreated += (s, e) => Win.Cue(box.Handle, "Search songs, artists, albums or a line of lyrics");
            box.TextChanged += (s, e) => { debounce.Stop(); debounce.Start(); };
            debounce.Tick += (s, e) => { debounce.Stop(); view.Find(box.Text); Fit(); };
            Layout += (s, e) => PlaceBox();
            Deactivate += (s, e) => { if (!closing) BeginInvoke((Action)Close); };
            FormClosed += (s, e) => { if (Open == this) Open = null; debounce.Dispose(); };
            Open = this;
        }

        bool closing;
        public void Done() { closing = true; Close(); }

        void PlaceBox()
        {
            int th = box.PreferredHeight;
            box.Bounds = new Rectangle(Theme.P(54), (Theme.P(64) - th) / 2, Width - Theme.P(74), th);
            view.Top = 0;
        }

        // grow to fit the results
        void Fit()
        {
            int want = Theme.P(64) + (int)Math.Ceiling(view.Needed);
            int max = Math.Max(Theme.P(200), (Owner?.ClientSize.Height ?? 800) * 3 / 4);
            Height = Math.Min(max, Math.Max(Theme.P(64), want));
            view.Invalidate();
        }

        protected override CreateParams CreateParams { get { var c = base.CreateParams; c.ClassStyle |= 0x20000; return c; } }   // a soft shadow
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (App.IsWindows) try { int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4); } catch { }
        }
        protected override void OnShown(EventArgs e) { base.OnShown(e); box.Focus(); view.Find(""); Fit(); }

        protected override bool ProcessCmdKey(ref Message msg, Keys key)
        {
            switch (key)
            {
                case Keys.Escape: Done(); return true;
                case Keys.Down: view.Step(1); return true;
                case Keys.Up: view.Step(-1); return true;
                case Keys.Enter: view.Pick(false); return true;
                case Keys.Shift | Keys.Enter: view.Pick(true); return true;
                case Keys.Control | Keys.K: Done(); return true;
            }
            return base.ProcessCmdKey(ref msg, key);
        }

        public string Query => box.Text;
        public void TestType(string text) => box.Text = text;
        public List<string> TestKinds => view.Kinds;
        public QuickView View => view;
    }

    sealed class QuickView : Canvas
    {
        readonly IShell shell;
        readonly QuickSearch form;
        sealed class Item { public string Kind, Title, Sub; public Track Cover; public Action Go, Queue; public bool Round; }
        readonly List<Item> items = new List<Item>();
        readonly List<string> heads = new List<string>();
        readonly List<int> headAt = new List<int>();
        int sel;
        public float Needed;
        public List<string> Kinds => items.Select(i => i.Kind + ":" + i.Title + "|" + i.Sub).ToList();
        public static readonly List<string> Recent = new List<string>();

        public QuickView(IShell shell, QuickSearch form) { this.shell = shell; this.form = form; Scrollable = true; BackColor = Theme.Surface; }

        public void Step(int d)
        {
            if (items.Count == 0) return;
            sel = (sel + d + items.Count) % items.Count;
            float y = YOf(sel);
            if (y - Offset < Theme.F(64)) ScrollTo(Math.Max(0, y - Theme.F(70)), false);
            else if (y + Theme.F(56) - Offset > Height) ScrollTo(y + Theme.F(62) - Height, false);
            Invalidate();
        }

        public void Pick(bool queue)
        {
            if (sel < 0 || sel >= items.Count) return;
            var q = form.Query.Trim();
            if (q.Length > 0) { Recent.Remove(q); Recent.Insert(0, q); if (Recent.Count > 6) Recent.RemoveAt(6); }
            var it = items[sel];
            form.Done();
            (queue && it.Queue != null ? it.Queue : it.Go)?.Invoke();
        }

        // play a song now, keeping the rest of the queue after it
        void PlayNow(Track t, double at = -1)
        {
            var P = shell.Player;
            if (P.Current == t) { if (at >= 0) P.Seek(at); else if (!P.IsPlaying) P.Toggle(); return; }
            if (P.Order.Count == 0) P.PlayList(new List<long> { t.Id });
            else { P.PlayNext(new[] { t.Id }); P.Next(); }
            if (at >= 0) P.Seek(at);
        }

        public void Find(string text)
        {
            items.Clear(); heads.Clear(); headAt.Clear(); sel = 0;
            var q = (text ?? "").Trim();
            var lib = shell.Lib;
            void Head(string h) { heads.Add(h); headAt.Add(items.Count); }
            if (q.Length == 0)
            {
                if (Recent.Count > 0)
                {
                    Head("RECENT SEARCHES");
                    foreach (var r in Recent) { var rr = r; items.Add(new Item { Kind = "recent", Title = r, Go = () => { } }); }
                }
                var played = lib.Tracks.Where(t => t.LastPlayed > 0).OrderByDescending(t => t.LastPlayed).Take(6).ToList();
                if (played.Count > 0)
                {
                    Head("PLAYED LATELY");
                    foreach (var t in played) { var tt = t; items.Add(new Item { Kind = "song", Title = t.Name, Sub = t.ArtistName, Cover = t, Go = () => PlayNow(tt), Queue = () => { shell.Player.AddToQueue(new[] { tt.Id }); shell.Toast("Added to Up next"); } }); }
                }
                Measure(); return;
            }
            string low = q.ToLowerInvariant();
            var sq = new SearchQuery(q, lib, shell.Sound);
            int Score(Track t)
            {
                var n = t.Name.ToLowerInvariant();
                return n == low ? 0 : n.StartsWith(low) ? 1 : n.Contains(low) ? 2 : t.ArtistName.ToLowerInvariant().Contains(low) ? 3 : 4;
            }
            var songs = lib.Tracks.Where(t => sq.Matches(t) && !sq.ByLyrics(t)).OrderBy(Score).ThenByDescending(t => t.Plays).Take(7).ToList();
            if (songs.Count > 0)
            {
                Head("SONGS");
                foreach (var t in songs) { var tt = t; items.Add(new Item { Kind = "song", Title = t.Name, Sub = t.ArtistName + (t.Album.Length > 0 ? "  ·  " + t.Album : ""), Cover = t, Go = () => PlayNow(tt), Queue = () => { shell.Player.AddToQueue(new[] { tt.Id }); shell.Toast("Added to Up next"); } }); }
            }
            var artists = lib.Artists().Where(a => a.Name.ToLowerInvariant().Contains(low)).OrderBy(a => a.Name.ToLowerInvariant().StartsWith(low) ? 0 : 1).ThenByDescending(a => a.Tracks.Count).Take(3).ToList();
            if (artists.Count > 0)
            {
                Head("ARTISTS");
                foreach (var a in artists) { var k = a.Key; var ids = a.Tracks.Select(x => x.Id).ToList(); items.Add(new Item { Kind = "artist", Title = a.Name, Sub = Theme.Count(a.Tracks.Count, "song"), Cover = a.Tracks.OrderByDescending(x => x.HasArt).ThenByDescending(x => x.Plays).FirstOrDefault(), Round = true, Go = () => shell.Go(new Route("artist", k)), Queue = () => shell.Player.ShuffleAll(ids) }); }
            }
            var albums = lib.Albums().Where(a => a.Name.ToLowerInvariant().Contains(low)).Take(3).ToList();
            if (albums.Count > 0)
            {
                Head("ALBUMS");
                foreach (var a in albums) { var k = a.Key; var ids = a.Tracks.Select(x => x.Id).ToList(); items.Add(new Item { Kind = "album", Title = a.Name, Sub = a.Artist + "  ·  " + Theme.Count(a.Tracks.Count, "song"), Cover = a.Cover, Go = () => shell.Go(new Route("album", k)), Queue = () => shell.Player.PlayList(ids) }); }
            }
            // lines of lyrics: start the song right where the line is sung
            if (q.Length >= 3)
            {
                var hits = lib.SearchLyrics(q);
                var found = new List<Item>();
                foreach (var id in hits)
                {
                    if (found.Count >= 5) break;
                    var t = lib.Get(id); if (t == null) continue;
                    var l = shell.Lyrics.Get(t); if (l == null) continue;
                    var (line, at) = LineWith(l, low);
                    if (line == null) continue;
                    var tt = t; double when = at;
                    found.Add(new Item { Kind = "line", Title = "“" + line + "”", Sub = t.Name + "  ·  " + t.ArtistName + (at >= 0 ? "  ·  " + Theme.Time(at) : ""), Cover = t, Go = () => PlayNow(tt, when > 1 ? when - 0.3 : -1) });
                }
                if (found.Count > 0) { Head("IN THE LYRICS"); items.AddRange(found); }
            }
            Measure();
        }

        static string Plain(string s) => new string((s ?? "").ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray());

        // the line holding the words, and when it's sung (-1 when the lyrics aren't timed)
        static (string, double) LineWith(Lyrics l, string low)
        {
            var want = Plain(low).Trim();
            var lines = l.Timed ? l.Lines.Select(x => (x.Text, x.T)).ToList() : (l.Plain ?? "").Split('\n').Select(x => (x.Trim(), -1.0)).ToList();
            foreach (var (text, t) in lines) if (Plain(text).Contains(want)) return (text.Trim(), t);
            // spread over two lines
            for (int i = 0; i + 1 < lines.Count; i++) if (Plain(lines[i].Item1 + " " + lines[i + 1].Item1).Contains(want)) return (lines[i].Item1.Trim() + " / " + lines[i + 1].Item1.Trim(), lines[i].Item2);
            return (null, -1);
        }

        float YOf(int i)
        {
            float y = Theme.F(64);
            for (int k = 0; k <= i && k < items.Count; k++)
            {
                if (headAt.Contains(k)) y += Theme.F(34);
                if (k < i) y += Theme.F(54);
            }
            return y;
        }

        void Measure()
        {
            Needed = items.Count == 0 ? (form.Query.Trim().Length > 0 ? Theme.F(60) : 0) : YOf(items.Count - 1) + Theme.F(54) + Theme.F(46) - Theme.F(64);
            ContentHeight = Theme.F(64) + Needed;
            Invalidate();
        }

        // the search line stays put while the results scroll (the box itself sits on top)
        protected override void DrawOver(Graphics g)
        {
            using (var b = new SolidBrush(Theme.Surface)) g.FillRectangle(b, 0, 0, Width, Theme.F(63));
            Icons.Draw(g, "search", new RectangleF(Theme.F(20), Theme.F(22), Theme.F(20), Theme.F(20)), Theme.Sub);
            if (items.Count > 0 || form.Query.Trim().Length > 0) Theme.Fill(g, Theme.Alpha(Theme.Ink, 18), new RectangleF(0, Theme.F(63), Width, Theme.F(1)), 0);
        }

        protected override void Draw(Graphics g, RectangleF view)
        {
            if (items.Count == 0)
            {
                if (form.Query.Trim().Length > 0) Theme.Write(g, "Nothing found for “" + form.Query.Trim() + "”", Theme.Font(14), Theme.Sub, new RectangleF(Theme.F(20), Theme.F(76), Width - Theme.F(40), Theme.F(30)));
                return;
            }
            float y = Theme.F(64);
            for (int i = 0; i < items.Count; i++)
            {
                int hi = headAt.IndexOf(i);
                if (hi >= 0) { Theme.Write(g, heads[hi], Theme.Font(11, FontStyle.Regular, true), Theme.Faint, new RectangleF(Theme.F(20), y + Theme.F(12), Width, Theme.F(20))); y += Theme.F(34); }
                var it = items[i];
                var r = new RectangleF(Theme.F(8), y, Width - Theme.F(16), Theme.F(52));
                int ii = i;
                bool on = i == sel || Hover("q:" + i);
                if (on) Theme.Fill(g, i == sel ? Theme.Alpha(Theme.Accent, 60) : Theme.Surface2, r, Theme.F(10));
                float x = r.X + Theme.F(12);
                if (it.Kind == "recent")
                {
                    Icons.Draw(g, "clock", new RectangleF(x + Theme.F(8), r.Y + Theme.F(16), Theme.F(20), Theme.F(20)), Theme.Sub);
                    Theme.Write(g, it.Title, Theme.Font(14), Theme.Text, new RectangleF(x + Theme.F(48), r.Y, r.Width - Theme.F(80), r.Height));
                    var q = it.Title;
                    AddHit("q:" + i, r, () => { if (FindForm() is QuickSearch qs) { var tb = qs.Controls.OfType<TextBox>().First(); tb.Text = q; tb.SelectionStart = q.Length; } });
                }
                else
                {
                    var cr = new RectangleF(x, r.Y + Theme.F(6), Theme.F(40), Theme.F(40));
                    var img = it.Cover != null ? Art.Get(it.Cover) : null;
                    if (img != null) Theme.Image(g, img, cr, it.Round ? cr.Width / 2 : Theme.F(6), it.Round);
                    else Theme.NoArt(g, cr, Theme.F(6), it.Cover?.Id ?? it.Title.GetHashCode(), it.Round, it.Kind == "artist" ? "artist" : "note");
                    float tx = cr.Right + Theme.F(14), tw = r.Right - tx - Theme.F(90);
                    Theme.Write(g, it.Title, Theme.Font(14.5f, it.Kind == "line" ? FontStyle.Italic : FontStyle.Bold), Theme.Text, new RectangleF(tx, r.Y + Theme.F(6), tw, Theme.F(22)));
                    Theme.Write(g, it.Sub, Theme.Font(12.5f), Theme.Sub, new RectangleF(tx, r.Y + Theme.F(27), tw, Theme.F(20)));
                    if (i == sel)
                    {
                        string hint = it.Kind == "song" ? "Enter to play" : it.Kind == "line" ? "Enter: play from here" : "Enter to open";
                        Theme.Write(g, hint, Theme.Font(11.5f), Theme.Faint, new RectangleF(r.Right - Theme.F(150), r.Y, Theme.F(138), r.Height), Theme.Right);
                    }
                    AddHit("q:" + i, r, () => { sel = ii; Pick(false); }, it.Kind == "song" ? "Play now (Shift+Enter adds it to Up next)" : null);
                }
                y += Theme.F(54);
            }
            Theme.Write(g, "↑ ↓ to move   ·   Enter to play or open   ·   Shift+Enter: add to Up next   ·   Esc to close", Theme.Font(11.5f), Theme.Faint, new RectangleF(Theme.F(20), y + Theme.F(8), Width - Theme.F(40), Theme.F(26)));
        }
    }
}
