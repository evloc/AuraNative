using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Aura
{
    // Missing info: songs without a title, artist, album, cover, year or lyrics
    sealed class MissingPage : Canvas
    {
        readonly IShell shell;
        public MissingPage(IShell shell) { this.shell = shell; }
        public void Reload() => Invalidate();

        public static List<Track> Which(IShell shell, string key)
        {
            var all = shell.Lib.Tracks;
            switch (key)
            {
                case "notitle": return all.Where(t => t.Title.Trim().Length == 0).ToList();
                case "noartist": return all.Where(t => t.Artist.Trim().Length == 0).ToList();
                case "noalbum": return all.Where(t => t.Album.Trim().Length == 0).ToList();
                case "noart": return all.Where(t => !t.HasArt).ToList();
                case "noyear": return all.Where(t => t.Year.Trim().Length == 0).ToList();
                case "nolyrics": return shell.Lyrics.Without();
                default: return new List<Track>();
            }
        }

        public static readonly (string key, string title, string icon, string tip)[] Kinds =
        {
            ("noartist", "No artist", "artist", "Artist tags let AURA group songs and find lyrics and covers"),
            ("notitle", "No title", "songs", "These show their file name instead"),
            ("noart", "No cover", "album", "Find covers online, or choose a picture"),
            ("noalbum", "No album", "album", "These show up under Singles"),
            ("noyear", "No year", "clock", ""),
            ("nolyrics", "No lyrics", "mic", "AURA looks for these online by itself"),
        };

        protected override void Draw(Graphics g, RectangleF view)
        {
            float pad = Theme.F(28), y = Theme.F(8);
            Theme.Write(g, "Missing info", Theme.Font(34, FontStyle.Bold), Theme.Text, new RectangleF(pad, y + Theme.F(24), Width - pad * 2, Theme.F(48)));
            Theme.Write(g, "Songs that are missing something. Open a group, select songs, and right-click to fix them.", Theme.Font(13.5f), Theme.Sub, new RectangleF(pad, y + Theme.F(72), Width - pad * 2, Theme.F(22)));
            float px = pad;
            px = Pill(g, "fillall", px, y + Theme.F(104), "Fill in blanks from file & folder names", () => shell.FillFromNames(shell.Lib.Tracks.ToList()), false, "edit", 34).Right + Theme.F(10);
            Pill(g, "tracker", px, y + Theme.F(104), "Import a fan tracker…", () => (FindForm() as MainForm)?.ImportTracker(), false, "era", 34);
            y += Theme.F(160);
            int cols = Width > Theme.F(1000) ? 3 : 2;
            float gap = Theme.F(16), cw = (Width - pad * 2 - gap * (cols - 1)) / cols, ch = Theme.F(112);
            int i = 0;
            foreach (var (key, title, icon, tip) in Kinds)
            {
                int n = Which(shell, key).Count;
                var r = new RectangleF(pad + (i % cols) * (cw + gap), y + (i / cols) * (ch + gap), cw, ch);
                bool hot = Hover("m:" + key);
                Theme.Fill(g, hot && n > 0 ? Theme.Surface2 : Theme.Surface, r, Theme.F(14));
                float s = Theme.F(26);
                Icons.Draw(g, icon, new RectangleF(r.X + Theme.F(22), r.Y + Theme.F(22), s, s), n > 0 ? Theme.AccentText : Theme.Faint);
                Theme.Write(g, title, Theme.Font(16, FontStyle.Bold), Theme.Text, new RectangleF(r.X + Theme.F(62), r.Y + Theme.F(20), r.Width - Theme.F(160), Theme.F(28)));
                Theme.Write(g, n == 0 ? "All done" : tip, Theme.Font(12.5f), Theme.Faint, new RectangleF(r.X + Theme.F(62), r.Y + Theme.F(50), r.Width - Theme.F(84), Theme.F(40)), Theme.Wrap);
                Theme.Write(g, n.ToString("N0"), Theme.Font(30, FontStyle.Bold), n > 0 ? Theme.Text : Theme.Faint, new RectangleF(r.X, r.Y + Theme.F(14), r.Width - Theme.F(22), Theme.F(40)), Theme.Right);
                var k = key;
                AddHit("m:" + key, r, n > 0 ? () => shell.Go(new Route("missing", k)) : (Action)null);
                i++;
            }
            ContentHeight = y + ((Kinds.Length + cols - 1) / cols) * (ch + gap) + Theme.F(40);
        }

        protected override void OnBackButton() => shell.Back();
        protected override void OnForwardButton() => shell.Forward();
    }

    // Versions: songs that come in more than one version (v1, v2, snippet, CDQ, ...)
    sealed class VersionsPage : Canvas
    {
        readonly IShell shell;
        List<List<Track>> groups, suggested;
        List<Library.InFull> inFull;
        public VersionsPage(IShell shell) { this.shell = shell; Reload(); }
        public void Reload()
        {
            groups = shell.Lib.VersionGroups(); inFull = shell.Lib.NowInFull();
            // a snippet that's here in full is shown once, under Now in full
            suggested = shell.Lib.SuggestedVersions().Where(sg => !inFull.Any(a => sg.Contains(a.Full))).ToList();
            Invalidate();
        }

        public static void WriteTitles(MainForm f, List<Track> grp)
        {
            var song = LyricsOnline.CleanTitle(grp[0].Name);
            var titles = Library.VersionTitles(song, grp);
            var jobs = grp.Where(t => titles[t.Id] != t.Title).Select(t => (t, new TagEdit { Title = titles[t.Id] })).ToList();
            if (jobs.Count == 0) { f.Toast("The titles are already like that"); return; }
            var list = string.Join("\n", jobs.Take(12).Select(j => "  " + j.t.Name + "  →  " + j.Item2.Title)) + (jobs.Count > 12 ? "\n  …" : "");
            if (System.Windows.Forms.MessageBox.Show(f, "Give these versions clear titles?\n\n" + list + "\n\nThis changes the title tag in the files (Ctrl+Z puts it back).", "AURA", System.Windows.Forms.MessageBoxButtons.OKCancel) != System.Windows.Forms.DialogResult.OK) return;
            f.RunWrites(jobs, "Wrote version titles");
        }

        void Menu(List<Track> grp, Point at)
        {
            var f = FindForm() as MainForm; if (f == null) return;
            var m = Menus.New();
            var key = Library.VersionKey(grp[0]);
            m.Items.Add(Menus.Item("Play all versions", () => shell.Player.PlayList(grp.Select(x => x.Id).ToList())));
            m.Items.Add(Menus.Item("Write clear titles — " + LyricsOnline.CleanTitle(grp[0].Name) + " (v2), (Snippet)…", () => WriteTitles(f, grp)));
            m.Items.Add(Menus.Item("Rename the song…", () => f.GroupAsVersions(grp)));
            m.Items.Add(Menus.Item("These aren't versions of one song — split them up", () => { shell.Lib.MarkNotVersions(key); Reload(); f.Toast("Split up — they won't be grouped again"); }));
            m.Show(at);
        }

        protected override void Draw(Graphics g, RectangleF view)
        {
            float pad = Theme.F(28), y = Theme.F(8);
            Theme.Write(g, "Versions", Theme.Font(34, FontStyle.Bold), Theme.Text, new RectangleF(pad, y + Theme.F(24), Width - pad * 2, Theme.F(48)));
            Theme.Write(g, groups.Count == 0 ? "No song has more than one version yet. Versions are matched by title and artist, ignoring things like (v2), [CDQ] or (snippet)."
                : Theme.Count(groups.Count, "song") + " with more than one version — matched by title and artist, ignoring (v2), [CDQ], (snippet) and the like.",
                Theme.Font(13.5f), Theme.Sub, new RectangleF(pad, y + Theme.F(72), Width - pad * 2, Theme.F(40)), Theme.Wrap);
            y += Theme.F(124);
            // ---- snippets that are here in full now
            if (inFull.Count > 0)
            {
                Theme.Write(g, "NOW IN FULL  ·  you had snippets of these", Theme.Font(12, FontStyle.Bold), Theme.Faint, new RectangleF(pad, y, Width - pad * 2, Theme.F(22)));
                y += Theme.F(30);
                foreach (var a in inFull.Take(8))
                {
                    var r = new RectangleF(pad - Theme.F(10), y, Width - pad * 2 + Theme.F(20), Theme.F(58));
                    Theme.Fill(g, Theme.Alpha(Theme.Accent, 40), r, Theme.F(10));
                    Theme.Write(g, a.Full.Name + " — " + a.Full.ArtistName, Theme.Font(14.5f, FontStyle.Regular, true), Theme.Text, new RectangleF(r.X + Theme.F(16), r.Y + Theme.F(8), r.Width - Theme.F(420), Theme.F(22)));
                    Theme.Write(g, "Snippets: " + string.Join(", ", a.Snippets.Select(x => x.Name).Take(3)) + (a.Snippets.Count > 3 ? "…" : ""), Theme.Font(12.5f), Theme.Sub, new RectangleF(r.X + Theme.F(16), r.Y + Theme.F(31), r.Width - Theme.F(420), Theme.F(20)));
                    var aa = a;
                    float bx = r.Right - Theme.F(16);
                    bx -= PillWidth(g, "Dismiss");
                    Pill(g, "ifno:" + a.Full.Id, bx, r.Y + Theme.F(12), "Dismiss", () => { shell.Lib.DismissInFull(aa.Full); Reload(); }, false, null, 32);
                    bx -= Theme.F(8) + PillWidth(g, "Group as versions");
                    Pill(g, "ifgrp:" + a.Full.Id, bx, r.Y + Theme.F(12), "Group as versions", () => { shell.Lib.GroupVersions(new[] { aa.Full }.Concat(aa.Snippets), LyricsOnline.CleanTitle(aa.Full.Name)); shell.Lib.DismissInFull(aa.Full); Reload(); }, false, null, 32);
                    bx -= Theme.F(8) + PillWidth(g, "Play", "play");
                    Pill(g, "ifplay:" + a.Full.Id, bx, r.Y + Theme.F(12), "Play", () => shell.Player.PlayList(new List<long> { aa.Full.Id }), true, "play", 32);
                    y += Theme.F(66);
                }
                y += Theme.F(20);
            }
            // ---- maybe versions
            if (suggested.Count > 0)
            {
                Theme.Write(g, "MAYBE VERSIONS  ·  same title, artist written differently", Theme.Font(12, FontStyle.Bold), Theme.Faint, new RectangleF(pad, y, Width - pad * 2, Theme.F(22)));
                y += Theme.F(30);
                foreach (var sg in suggested.Take(8))
                {
                    var r = new RectangleF(pad - Theme.F(10), y, Width - pad * 2 + Theme.F(20), Theme.F(58));
                    Theme.Fill(g, Theme.Surface, r, Theme.F(10));
                    Theme.Write(g, LyricsOnline.CleanTitle(sg[0].Name), Theme.Font(14.5f, FontStyle.Regular, true), Theme.Text, new RectangleF(r.X + Theme.F(16), r.Y + Theme.F(8), r.Width * 0.5f, Theme.F(22)));
                    Theme.Write(g, string.Join("  ·  ", sg.Select(t => t.Name + " — " + t.ArtistName).Distinct().Take(4)), Theme.Font(12.5f), Theme.Sub, new RectangleF(r.X + Theme.F(16), r.Y + Theme.F(31), r.Width - Theme.F(300), Theme.F(20)));
                    var gg = sg;
                    float bx = r.Right - Theme.F(16);
                    bx -= PillWidth(g, "Not versions");
                    Pill(g, "sgno:" + sg[0].Id, bx, r.Y + Theme.F(12), "Not versions", () => { shell.Lib.DismissSuggestion(gg); Reload(); }, false, null, 32);
                    bx -= Theme.F(8) + PillWidth(g, "Group them");
                    Pill(g, "sgok:" + sg[0].Id, bx, r.Y + Theme.F(12), "Group them", () => { shell.Lib.GroupVersions(gg, LyricsOnline.CleanTitle(gg[0].Name)); Reload(); shell.Toast("Grouped as versions"); }, true, null, 32);
                    y += Theme.F(66);
                }
                y += Theme.F(20);
                Theme.Write(g, "VERSIONS  ·  right-click one to write clear titles or split it up", Theme.Font(12, FontStyle.Bold), Theme.Faint, new RectangleF(pad, y, Width - pad * 2, Theme.F(22)));
                y += Theme.F(30);
            }
            float rowH = Theme.F(76);
            int first = Math.Max(0, (int)((view.Top - y) / rowH) - 1), last = Math.Min(groups.Count - 1, (int)((view.Bottom - y) / rowH) + 1);
            for (int i = first; i <= last; i++)
            {
                var grp = groups[i];
                var t = grp[0];
                var r = new RectangleF(pad - Theme.F(10), y + i * rowH, Width - pad * 2 + Theme.F(20), rowH - Theme.F(6));
                string id = "v:" + i;
                if (HoverStarts(id)) Theme.Fill(g, Theme.Surface, r, Theme.F(10));
                float s = rowH - Theme.F(22);
                var ar = new RectangleF(r.X + Theme.F(10), r.Y + (r.Height - s) / 2, s, s);
                var cov = grp.FirstOrDefault(x => x.HasArt) ?? t;
                var img = Art.Get(cov);
                if (img != null) Theme.Image(g, img, ar, Theme.F(6)); else Theme.NoArt(g, ar, Theme.F(6), t.Id);
                float tx = ar.Right + Theme.F(16);
                Theme.Write(g, LyricsOnline.CleanTitle(t.Name), Theme.Font(15, FontStyle.Regular, true), Theme.Text, new RectangleF(tx, r.Y + Theme.F(10), Width * 0.3f, Theme.F(22)));
                Theme.Write(g, t.MainArtist + "  ·  " + Theme.Count(grp.Count, "version"), Theme.Font(12.5f), Theme.Sub, new RectangleF(tx, r.Y + Theme.F(34), Width * 0.3f, Theme.F(20)));
                // the versions as chips
                float cx = tx + Width * 0.3f + Theme.F(20);
                foreach (var v in grp)
                {
                    var label = v.Name;
                    var f = Theme.Font(12.5f);
                    float w = Math.Min(Theme.F(220), Theme.Measure(g, label, f) + Theme.F(24));
                    if (cx + w > r.Right - Theme.F(10)) break;
                    var cr = new RectangleF(cx, r.Y + (r.Height - Theme.F(28)) / 2, w, Theme.F(28));
                    bool cur = shell.Player.Current?.Id == v.Id, hot = Hover(id + ":" + v.Id);
                    Theme.Fill(g, cur ? Theme.Alpha(Theme.Accent, 90) : hot ? Theme.Surface2 : Theme.Alpha(Theme.Surface2, 160), cr, cr.Height / 2);
                    Theme.Write(g, label, f, cur ? Theme.Text : Theme.Sub, new RectangleF(cr.X + Theme.F(12), cr.Y, cr.Width - Theme.F(20), cr.Height));
                    var vv = v; var ids = grp.Select(x => x.Id).ToList();
                    AddHit(id + ":" + v.Id, cr, () => shell.Player.PlayList(ids, ids.IndexOf(vv.Id)), "Play this version");
                    cx += w + Theme.F(8);
                }
                var key = Library.VersionKey(t);
                var gr = grp;
                AddHit(id, r, () => shell.Go(new Route("version", key))).Right = p => Menu(gr, p);
            }
            ContentHeight = y + groups.Count * rowH + Theme.F(40);
        }

        protected override void OnBackButton() => shell.Back();
        protected override void OnForwardButton() => shell.Forward();
    }

    // Stats: what you've been listening to
    sealed class StatsPage : Canvas
    {
        readonly IShell shell;
        int days = 30;       // 7, 30, 365, 0 = all time
        public StatsPage(IShell shell) { this.shell = shell; }
        public void Reload() => Invalidate();

        protected override void Draw(Graphics g, RectangleF view)
        {
            var lib = shell.Lib;
            float pad = Theme.F(28), y = Theme.F(8);
            Theme.Write(g, "Stats", Theme.Font(34, FontStyle.Bold), Theme.Text, new RectangleF(pad, y + Theme.F(24), Width - pad * 2, Theme.F(48)));
            y += Theme.F(84);
            float px = pad;
            foreach (var (d, label) in new[] { (7, "Week"), (30, "Month"), (365, "Year"), (0, "All time") })
            {
                var dd = d;
                var r = Pill(g, "p:" + d, px, y, label, () => { days = dd; Invalidate(); }, days == d, null, 34);
                px = r.Right + Theme.F(8);
            }
            y += Theme.F(56);

            double now = Library.Now, from = days == 0 ? 0 : now - days * 86400.0;
            // plays in the period: from the play history; "All time" also counts plays brought over from the first AURA
            var hist = lib.Db.Rows("SELECT track_id, ts FROM history WHERE ts >= ?", from).Select(r => (id: Conv.L(r[0]), ts: Conv.D(r[1]))).ToList();
            Dictionary<long, int> counts;
            if (days == 0) counts = lib.Tracks.Where(t => t.Plays > 0).ToDictionary(t => t.Id, t => t.Plays);
            else counts = hist.GroupBy(h => h.id).ToDictionary(x => x.Key, x => x.Count());
            int plays = counts.Values.Sum();
            double secs = counts.Sum(kv => (lib.Get(kv.Key)?.Duration ?? 0) * kv.Value);
            var topSongs = counts.Where(kv => lib.Get(kv.Key) != null).OrderByDescending(kv => kv.Value).Take(10).Select(kv => (t: lib.Get(kv.Key), n: kv.Value)).ToList();
            var topArtists = counts.Where(kv => lib.Get(kv.Key) != null).GroupBy(kv => lib.Get(kv.Key).MainArtist).Select(x => (name: x.Key, n: x.Sum(kv => kv.Value), cover: lib.Get(x.First().Key))).OrderByDescending(x => x.n).Take(6).ToList();

            // cards
            int cols = 4; float gap = Theme.F(14), cw = (Width - pad * 2 - gap * (cols - 1)) / cols, ch = Theme.F(96);
            var cards = new[] { ("Listening time", secs < 60 ? "—" : Theme.Long(secs)), ("Plays", plays.ToString("N0")), ("Songs", counts.Count.ToString("N0")), ("Top artist", topArtists.Count > 0 ? topArtists[0].name : "—") };
            for (int i = 0; i < cards.Length; i++)
            {
                var r = new RectangleF(pad + i * (cw + gap), y, cw, ch);
                Theme.Fill(g, Theme.Surface, r, Theme.F(14));
                Theme.Write(g, cards[i].Item1.ToUpperInvariant(), Theme.Font(11.5f, FontStyle.Regular, true), Theme.Faint, new RectangleF(r.X + Theme.F(18), r.Y + Theme.F(16), r.Width - Theme.F(30), Theme.F(20)));
                Theme.Write(g, cards[i].Item2, Theme.Font(24, FontStyle.Bold), Theme.Text, new RectangleF(r.X + Theme.F(18), r.Y + Theme.F(42), r.Width - Theme.F(30), Theme.F(36)));
            }
            y += ch + Theme.F(30);

            if (plays == 0)
            {
                Theme.Write(g, days == 0 ? "No plays yet. A song counts once you've listened to half of it (or 30 seconds)." : "Nothing played in this time yet.", Theme.Font(15), Theme.Sub, new RectangleF(pad, y, Width - pad * 2, Theme.F(30)));
                ContentHeight = y + Theme.F(80);
                return;
            }

            // plays per day (or per month for a year / all time)
            if (days != 0 || hist.Count > 0)
            {
                Theme.Write(g, days == 365 || days == 0 ? "Plays by month" : "Plays by day", Theme.Font(19, FontStyle.Bold), Theme.Text, new RectangleF(pad, y, Width, Theme.F(28)));
                y += Theme.F(40);
                bool monthly = days == 365 || days == 0;
                var buckets = new List<(string label, int n)>();
                var today = DateTime.Today;
                if (!monthly)
                    for (int i = days - 1; i >= 0; i--)
                    {
                        var d = today.AddDays(-i);
                        double a = (d.ToUniversalTime() - new DateTime(1970, 1, 1)).TotalSeconds, b = a + 86400;
                        buckets.Add((days == 7 ? d.ToString("ddd") : d.Day.ToString(), hist.Count(h => h.ts >= a && h.ts < b)));
                    }
                else
                {
                    int months = days == 365 ? 12 : Math.Max(1, Math.Min(36, (int)Math.Ceiling((now - (hist.Count > 0 ? hist.Min(h => h.ts) : now)) / (30 * 86400.0)) + 1));
                    for (int i = months - 1; i >= 0; i--)
                    {
                        var m0 = new DateTime(today.Year, today.Month, 1).AddMonths(-i);
                        double a = (m0.ToUniversalTime() - new DateTime(1970, 1, 1)).TotalSeconds, b = (m0.AddMonths(1).ToUniversalTime() - new DateTime(1970, 1, 1)).TotalSeconds;
                        buckets.Add((m0.ToString("MMM"), hist.Count(h => h.ts >= a && h.ts < b)));
                    }
                }
                float chartH = Theme.F(150), cwid = Width - pad * 2;
                int max = Math.Max(1, buckets.Max(bk => bk.n));
                float bw = cwid / buckets.Count;
                for (int i = 0; i < buckets.Count; i++)
                {
                    float h = buckets[i].n == 0 ? Theme.F(3) : Math.Max(Theme.F(4), chartH * buckets[i].n / max);
                    var br = new RectangleF(pad + i * bw + bw * 0.15f, y + chartH - h, bw * 0.7f, h);
                    Theme.Fill(g, buckets[i].n == 0 ? Theme.Surface2 : Hover("b:" + i) ? Theme.Mix(Theme.Accent, Color.White, 0.2f) : Theme.Accent, br, Math.Min(Theme.F(4), br.Width / 2));
                    AddHit("b:" + i, new RectangleF(pad + i * bw, y, bw, chartH), null, buckets[i].label + ": " + Theme.Count(buckets[i].n, "play"));
                    bool showLabel = buckets.Count <= 12 || i % 5 == 0 || i == buckets.Count - 1;
                    if (showLabel) Theme.Write(g, buckets[i].label, Theme.Font(11), Theme.Faint, new RectangleF(pad + i * bw - Theme.F(10), y + chartH + Theme.F(4), bw + Theme.F(20), Theme.F(18)), Theme.Center);
                }
                y += chartH + Theme.F(46);
            }

            // top songs and top artists, side by side when there's room
            bool side = Width > Theme.F(1000);
            float lw = side ? (Width - pad * 3) * 0.58f : Width - pad * 2;
            float ly = y;
            Theme.Write(g, "Top songs", Theme.Font(19, FontStyle.Bold), Theme.Text, new RectangleF(pad, ly, lw, Theme.F(28)));
            ly += Theme.F(40);
            var ids = topSongs.Select(x => x.t.Id).ToList();
            for (int i = 0; i < topSongs.Count; i++)
            {
                var (t, n) = topSongs[i];
                var r = new RectangleF(pad - Theme.F(8), ly, lw + Theme.F(16), Theme.F(52));
                string id = "s:" + i;
                if (Hover(id)) Theme.Fill(g, Theme.Surface, r, Theme.F(8));
                Theme.Write(g, (i + 1).ToString(), Theme.Font(14, FontStyle.Bold), Theme.Faint, new RectangleF(pad, ly, Theme.F(28), Theme.F(52)), Theme.Center);
                var ar = new RectangleF(pad + Theme.F(36), ly + Theme.F(6), Theme.F(40), Theme.F(40));
                var img = Art.Get(t);
                if (img != null) Theme.Image(g, img, ar, Theme.F(5)); else Theme.NoArt(g, ar, Theme.F(5), t.Id);
                Theme.Write(g, t.Name, Theme.Font(14, FontStyle.Regular, true), Theme.Text, new RectangleF(ar.Right + Theme.F(12), ly + Theme.F(6), lw - Theme.F(200), Theme.F(20)));
                Theme.Write(g, t.ArtistName, Theme.Font(12.5f), Theme.Sub, new RectangleF(ar.Right + Theme.F(12), ly + Theme.F(27), lw - Theme.F(200), Theme.F(18)));
                Theme.Write(g, Theme.Count(n, "play"), Theme.Font(13), Theme.Sub, new RectangleF(pad, ly, lw, Theme.F(52)), Theme.Right);
                int k = i;
                AddHit(id, r, () => shell.Player.PlayList(ids, k), "Play");
                ly += Theme.F(54);
            }
            float ay = side ? y : ly + Theme.F(24), ax = side ? pad * 2 + lw : pad, aw = side ? Width - pad * 3 - lw : Width - pad * 2;
            Theme.Write(g, "Top artists", Theme.Font(19, FontStyle.Bold), Theme.Text, new RectangleF(ax, ay, aw, Theme.F(28)));
            ay += Theme.F(40);
            foreach (var (name, n, cover) in topArtists)
            {
                var r = new RectangleF(ax - Theme.F(8), ay, aw + Theme.F(16), Theme.F(60));
                string id = "a:" + name;
                if (Hover(id)) Theme.Fill(g, Theme.Surface, r, Theme.F(8));
                var ar = new RectangleF(ax, ay + Theme.F(6), Theme.F(48), Theme.F(48));
                var img = Art.Get(cover);
                if (img != null) Theme.Image(g, img, ar, 0, true); else Theme.NoArt(g, ar, 0, name.GetHashCode(), true, "artist");
                Theme.Write(g, name, Theme.Font(14.5f, FontStyle.Regular, true), Theme.Text, new RectangleF(ar.Right + Theme.F(14), ay, aw - Theme.F(150), Theme.F(60)));
                Theme.Write(g, Theme.Count(n, "play"), Theme.Font(13), Theme.Sub, new RectangleF(ax, ay, aw, Theme.F(60)), Theme.Right);
                var key = name.ToLowerInvariant();
                AddHit(id, r, () => shell.Go(new Route("artist", key)));
                ay += Theme.F(62);
            }
            ContentHeight = Math.Max(ly, ay) + Theme.F(40);
        }

        protected override void OnBackButton() => shell.Back();
        protected override void OnForwardButton() => shell.Forward();
    }
}
