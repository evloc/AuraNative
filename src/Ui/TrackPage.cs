using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Aura
{
    // A page with a header and a list of songs: Songs, search results, an album, an artist,
    // a playlist, Favorites, Up next.
    sealed class TrackPage : Canvas
    {
        readonly IShell shell;
        public readonly Route Route;
        string kind => Route.Kind;
        List<Track> rows = new List<Track>();
        readonly HashSet<int> sel = new HashSet<int>();
        int anchor = -1, focus = -1;
        float listTop, rowH, colH;
        string sortKey = "added"; bool sortDesc = true;
        Album album; Artist artist; Playlist playlist;
        List<Album> artistAlbums = new List<Album>();
        List<Artist> foundArtists = new List<Artist>(); List<Album> foundAlbums = new List<Album>();
        Color headerColor = Color.Empty; long headerColorFor = -1;
        // dragging rows to reorder (playlists, up next)
        int dragFrom = -1, dropAt = -1; PointF dragStart; bool dragging;

        bool CanReorder => (kind == "playlist" || kind == "queue") && rows.Count > 1;
        bool Sortable => kind == "songs" || kind == "favorites" || kind == "search" || kind == "missing";

        public TrackPage(IShell shell, Route r)
        {
            this.shell = shell; Route = r;
            if (kind == "songs") { sortKey = shell.Prefs.SongsSort; sortDesc = shell.Prefs.SongsDesc; }
            else if (kind == "favorites") { sortKey = "added"; sortDesc = true; }
            else if (kind == "search" || kind == "missing") { sortKey = "title"; sortDesc = false; }
            Reload();
        }

        public bool Gone => (kind == "playlist" && playlist == null) || ((kind == "album" || kind == "artist") && rows.Count == 0 && shell.Lib.Tracks.Count > 0);

        public void Reload()
        {
            var lib = shell.Lib;
            int oldCount = rows.Count;
            IEnumerable<Track> src;
            album = null; artist = null; playlist = null;
            switch (kind)
            {
                case "album":
                    album = lib.Albums().FirstOrDefault(a => a.Key == Route.Key);
                    src = album?.Tracks ?? new List<Track>();
                    break;
                case "artist":
                    artist = lib.Artists().FirstOrDefault(a => a.Key == Route.Key);
                    src = (artist?.Tracks ?? new List<Track>()).OrderBy(t => t.Album.Length == 0 ? "￿" : t.Album, StringComparer.CurrentCultureIgnoreCase).ThenBy(t => t.TrackNo == 0 ? 9999 : t.TrackNo).ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase);
                    artistAlbums = artist == null ? new List<Album>() : lib.Albums().Where(a => a.Name != "Singles" && a.Tracks.Any(t => t.MainArtist.ToLowerInvariant() == artist.Key)).OrderByDescending(a => a.Year).ToList();
                    break;
                case "playlist":
                    playlist = lib.Playlists.FirstOrDefault(p => p.Id == Route.Id);
                    src = playlist == null ? new List<Track>() : playlist.Items.Select(lib.Get).Where(t => t != null);
                    break;
                case "favorites": src = lib.Tracks.Where(t => t.Fav); break;
                case "missing": src = MissingPage.Which(shell, Route.Key); break;
                case "version": src = lib.Tracks.Where(t => Library.VersionKey(t) == Route.Key).OrderByDescending(t => t.Plays).ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase); break;
                case "queue": src = shell.Player.UpNext().Select(lib.Get).Where(t => t != null); break;
                case "search":
                    {
                        var words = Route.Key.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        bool Match(string hay) { hay = hay.ToLowerInvariant(); return words.All(w => hay.Contains(w)); }
                        src = lib.Tracks.Where(t => Match(t.Name + " " + t.Artist + " " + t.Album + " " + t.AlbumArtist + " " + t.Genre + " " + t.Year + " " + System.IO.Path.GetFileNameWithoutExtension(t.Path)));
                        foundArtists = lib.Artists().Where(a => Match(a.Name)).OrderByDescending(a => a.Tracks.Sum(t => t.Plays)).Take(8).ToList();
                        foundAlbums = lib.Albums().Where(a => a.Name != "Singles" && Match(a.Name + " " + a.Artist)).Take(8).ToList();
                        break;
                    }
                default: src = lib.Tracks; break;
            }
            rows = Sortable ? Sort(src).ToList() : src.ToList();
            if (rows.Count != oldCount) { sel.Clear(); anchor = focus = -1; }
            Invalidate();
        }

        IEnumerable<Track> Sort(IEnumerable<Track> src)
        {
            var c = StringComparer.CurrentCultureIgnoreCase;
            IOrderedEnumerable<Track> o;
            switch (sortKey)
            {
                case "title": o = sortDesc ? src.OrderByDescending(t => t.Name, c) : src.OrderBy(t => t.Name, c); break;
                case "artist": o = sortDesc ? src.OrderByDescending(t => t.ArtistName, c) : src.OrderBy(t => t.ArtistName, c); o = o.ThenBy(t => t.Album, c).ThenBy(t => t.TrackNo); break;
                case "album": o = sortDesc ? src.OrderByDescending(t => t.Album, c) : src.OrderBy(t => t.Album.Length == 0 ? "￿" : t.Album, c); o = o.ThenBy(t => t.TrackNo).ThenBy(t => t.Name, c); break;
                case "time": o = sortDesc ? src.OrderByDescending(t => t.Duration) : src.OrderBy(t => t.Duration); break;
                case "plays": o = src.OrderByDescending(t => t.Plays).ThenByDescending(t => t.LastPlayed); break;
                default: o = sortDesc ? src.OrderByDescending(t => t.Added) : src.OrderBy(t => t.Added); o = o.ThenBy(t => t.Name, c); break;
            }
            return o;
        }

        void SetSort(string key)
        {
            if (sortKey == key) sortDesc = !sortDesc;
            else { sortKey = key; sortDesc = key == "added" || key == "time"; }
            if (kind == "songs") { shell.Prefs.SongsSort = sortKey; shell.Prefs.SongsDesc = sortDesc; }
            Reload();
        }

        public IList<long> Ids => rows.Select(t => t.Id).ToList();

        void PlayFrom(int i)
        {
            if (i < 0 || i >= rows.Count) return;
            if (kind == "queue") { shell.Player.JumpTo(i); return; }
            if (rows[i] == shell.Player.Current && rows[i] != null && sel.Count <= 1 && shell.Player.Queue.Count == rows.Count) { shell.Player.Toggle(); return; }
            shell.Player.PlayList(Ids, i);
        }

        // ---------- layout ----------
        struct Cols { public float X, Num, Art, Title, Album, Added, Fav, Time, Right; }

        Cols ColsFor()
        {
            var c = new Cols();
            float pad = Theme.F(28);
            c.X = pad; c.Right = Width - pad;
            c.Num = Theme.F(44);
            c.Art = kind == "album" ? 0 : Theme.F(52);
            c.Time = Theme.F(64); c.Fav = Theme.F(40);
            float rest = c.Right - c.X - c.Num - c.Art - c.Time - c.Fav;
            bool showAlbum = kind != "album" && rest > Theme.F(460);
            bool showAdded = (kind == "songs" || kind == "favorites" || kind == "playlist") && rest > Theme.F(720);
            c.Added = showAdded ? Theme.F(130) : 0;
            c.Album = showAlbum ? (rest - c.Added) * 0.36f : 0;
            c.Title = rest - c.Album - c.Added;
            return c;
        }

        // ---------- drawing ----------
        protected override void Draw(Graphics g, RectangleF view)
        {
            rowH = Theme.F(shell.Prefs.Compact ? 46 : 58);
            colH = Theme.F(38);
            float y = DrawHeader(g);
            listTop = y;
            if (rows.Count == 0 && kind != "queue") { DrawEmpty(g, y); return; }
            DrawColumns(g, y, false);
            y += colH;
            var cols = ColsFor();
            int first = Math.Max(0, (int)((view.Top - y) / rowH) - 1);
            int last = Math.Min(rows.Count - 1, (int)((view.Bottom - y) / rowH) + 1);
            for (int i = first; i <= last; i++) DrawRow(g, i, y + i * rowH, cols);
            if (dragging && dropAt >= 0)
            {
                float ly = y + dropAt * rowH;
                Theme.Fill(g, Theme.Accent, new RectangleF(cols.X, ly - Theme.F(1.5f), cols.Right - cols.X, Theme.F(3)), Theme.F(1.5f));
            }
            float end = y + rows.Count * rowH;
            if (kind == "queue" && rows.Count == 0)
                Theme.Write(g, "Nothing up next. Right-click any song → Play next or Add to up next.", Theme.Font(14), Theme.Faint, new RectangleF(cols.X, end + Theme.F(10), Width - cols.X * 2, Theme.F(40)));
            ContentHeight = end + Theme.F(60);
        }

        protected override void DrawOver(Graphics g)
        {
            // keep the column titles on screen while scrolling the list
            if (rows.Count > 0 && Offset > listTop)
            {
                using (var b = new SolidBrush(Theme.Bg)) g.FillRectangle(b, 0, 0, Width - Theme.F(12), colH);
                var st = g.Save();
                g.TranslateTransform(0, -listTop);
                DrawColumns(g, listTop, true);
                g.Restore(st);
                using (var pen = new Pen(Theme.Hair)) g.DrawLine(pen, Theme.F(28), colH, Width - Theme.F(28), colH);
            }
        }

        void DrawColumns(Graphics g, float y, bool fixedPos)
        {
            var c = ColsFor();
            var f = Theme.Font(12, FontStyle.Regular, true);
            var col = Theme.Faint;
            float x = c.X;
            Theme.Write(g, "#", f, col, new RectangleF(x, y, c.Num - Theme.F(12), colH), Theme.Center);
            x += c.Num;
            void Head(string id, string text, float w, string key, StringFormat fmt = null)
            {
                if (w <= 0) return;
                var r = new RectangleF(x, y, w, colH);
                bool on = Sortable && sortKey == key;
                bool hot = Sortable && Hover("col:" + id);
                string label = text + (on ? (sortDesc ? "  ↓" : "  ↑") : "");
                Theme.Write(g, label, f, on ? Theme.AccentText : hot ? Theme.Text : col, new RectangleF(r.X, r.Y, r.Width - Theme.F(8), r.Height), fmt);
                if (Sortable)
                {
                    var hh = AddHit("col:" + id, new RectangleF(r.X, r.Y + (fixedPos ? -listTop : 0), r.Width, r.Height), () => SetSort(key), "Sort by " + text.ToLowerInvariant());
                    if (fixedPos) { hh.Fixed = true; hh.R = new RectangleF(r.X, 0, r.Width, colH); }
                }
                x += w;
            }
            if (shell.Prefs.Compact)
            {
                Head("title", "TITLE", c.Art + c.Title * 0.55f, "title");
                if (Sortable) Head("artist", "ARTIST", c.Title * 0.45f, "artist"); else x += c.Title * 0.45f;
            }
            else
            {
                // two-line rows: "TITLE" and "ARTIST" side by side over the same column
                float start = x, tw = Theme.Measure(g, "TITLE" + (Sortable && sortKey == "title" ? "  ↓" : ""), f) + Theme.F(22);
                Head("title", "TITLE", Sortable ? tw : c.Art + c.Title, "title");
                if (Sortable) { Head("artist", "ARTIST", Theme.Measure(g, "ARTIST  ↓", f) + Theme.F(20), "artist"); }
                x = start + c.Art + c.Title;
            }
            Head("album", "ALBUM", c.Album, "album");
            Head("added", "ADDED", c.Added, "added");
            x += c.Fav;
            if (Sortable) Head("time", "TIME", c.Time, "time", Theme.Right);
            else { var r = new RectangleF(x, y, c.Time - Theme.F(8), colH); Theme.Write(g, "TIME", f, col, r, Theme.Right); }
            if (!fixedPos) using (var pen = new Pen(Theme.Hair)) g.DrawLine(pen, c.X, y + colH - 1, c.Right, y + colH - 1);
        }

        void DrawRow(Graphics g, int i, float y, Cols c)
        {
            var t = rows[i];
            string id = "row:" + i;
            bool selected = sel.Contains(i), hot = HoverStarts("row:" + i) || Hover("rp:" + i) || Hover("rf:" + i);
            bool current = shell.Player.Current == t && (kind != "queue");
            var r = new RectangleF(c.X - Theme.F(10), y + Theme.F(2), c.Right - c.X + Theme.F(20), rowH - Theme.F(4));
            if (selected) Theme.Fill(g, Theme.Alpha(Theme.Accent, 52), r, Theme.F(8));
            else if (hot) Theme.Fill(g, Theme.Surface, r, Theme.F(8));
            var h = AddHit(id, r);
            int ii = i;
            h.Down = e => RowDown(ii, e);
            h.Double = () => PlayFrom(ii);
            h.Right = pt => RowMenu(ii, pt);
            if (CanReorder) { h.DragTo = p => RowDrag(ii, p); h.DragEnd = RowDrop; }

            float x = c.X;
            // number / now playing / play on hover
            var nr = new RectangleF(x, y, c.Num - Theme.F(12), rowH);
            if (hot && !(current && shell.Player.IsPlaying))
            {
                float s = Theme.F(16);
                Icons.Draw(g, "play", new RectangleF(nr.X + (nr.Width - s) / 2, y + (rowH - s) / 2, s, s), Theme.Text, true);
                AddHit("rp:" + i, new RectangleF(nr.X, y + Theme.F(8), nr.Width, rowH - Theme.F(16)), () => PlayFrom(ii), "Play");
            }
            else if (current)
            {
                float s = Theme.F(16);
                Icons.Draw(g, shell.Player.IsPlaying ? "bars" : "play", new RectangleF(nr.X + (nr.Width - s) / 2, y + (rowH - s) / 2, s, s), Theme.AccentText, true);
            }
            else
            {
                string num = kind == "album" && t.TrackNo > 0 ? t.TrackNo.ToString() : (i + 1).ToString();
                Theme.Write(g, num, Theme.Font(13), Theme.Faint, nr, Theme.Center);
            }
            x += c.Num;
            // cover
            if (c.Art > 0)
            {
                float s = rowH - Theme.F(shell.Prefs.Compact ? 12 : 16);
                var ar = new RectangleF(x, y + (rowH - s) / 2, s, s);
                var img = Art.Get(t);
                if (img != null) Theme.Image(g, img, ar, Theme.F(5)); else Theme.NoArt(g, ar, Theme.F(5), t.Id);
                x += c.Art;
            }
            // title + artist
            var titleColor = current ? Theme.AccentText : Theme.Text;
            if (shell.Prefs.Compact)
            {
                Theme.Write(g, t.Name, Theme.Font(14, FontStyle.Regular, true), titleColor, new RectangleF(x, y, c.Title * 0.55f - Theme.F(10), rowH));
                Theme.Write(g, t.ArtistName, Theme.Font(13), Theme.Sub, new RectangleF(x + c.Title * 0.55f, y, c.Title * 0.45f - Theme.F(10), rowH));
            }
            else
            {
                Theme.Write(g, t.Name, Theme.Font(14.5f, FontStyle.Regular, true), titleColor, new RectangleF(x, y + rowH / 2 - Theme.F(21), c.Title - Theme.F(12), Theme.F(21)));
                Theme.Write(g, t.ArtistName, Theme.Font(12.5f), Theme.Sub, new RectangleF(x, y + rowH / 2 + Theme.F(1), c.Title - Theme.F(12), Theme.F(19)));
            }
            x += c.Title;
            if (c.Album > 0) { Theme.Write(g, t.Album, Theme.Font(13), Theme.Sub, new RectangleF(x, y, c.Album - Theme.F(14), rowH)); x += c.Album; }
            if (c.Added > 0) { Theme.Write(g, Theme.Ago(t.Added), Theme.Font(13), Theme.Sub, new RectangleF(x, y, c.Added - Theme.F(10), rowH)); x += c.Added; }
            // heart: always shown when liked, otherwise on hover
            if (t.Fav || hot)
            {
                var fr = new RectangleF(x + (c.Fav - Theme.F(30)) / 2, y + (rowH - Theme.F(30)) / 2, Theme.F(30), Theme.F(30));
                var tt = t;
                IconButton(g, "rf:" + i, fr, "heart", () => shell.Lib.SetFav(tt, !tt.Fav), t.Fav ? "Remove from Favorites" : "Add to Favorites", t.Fav, filled: t.Fav, dot: false);
            }
            x += c.Fav;
            Theme.Write(g, t.Duration > 0 ? Theme.Time(t.Duration) : "", Theme.Font(13), Theme.Sub, new RectangleF(x, y, c.Time - Theme.F(8), rowH), Theme.Right);
        }

        float DrawHeader(Graphics g)
        {
            float pad = Theme.F(28), y = Theme.F(8);
            Track coverTrack = album?.Cover ?? artist?.Cover;
            bool big = kind == "album" || kind == "artist" || kind == "playlist" || kind == "favorites";
            string eyebrow = "", title = "", meta = "";
            double total = rows.Sum(t => t.Duration);
            switch (kind)
            {
                case "songs": title = "Songs"; meta = Theme.Count(rows.Count, "song") + (total > 0 ? ", " + Theme.Long(total) : ""); break;
                case "search": title = "“" + Route.Key + "”"; eyebrow = "SEARCH"; meta = Theme.Count(rows.Count, "song"); break;
                case "favorites": eyebrow = "PLAYLIST"; title = "Favorites"; meta = Theme.Count(rows.Count, "song") + (total > 0 ? ", " + Theme.Long(total) : ""); break;
                case "queue": title = "Up next"; meta = rows.Count == 0 ? "" : Theme.Count(rows.Count, "song") + ", " + Theme.Long(total); break;
                case "album":
                    eyebrow = "ALBUM"; title = album?.Name ?? "Album";
                    meta = string.Join("  ·  ", new[] { album?.Artist, album?.Year, Theme.Count(rows.Count, "song") + (total > 0 ? ", " + Theme.Long(total) : "") }.Where(s => !string.IsNullOrEmpty(s)));
                    break;
                case "artist":
                    eyebrow = "ARTIST"; title = artist?.Name ?? "Artist";
                    meta = Theme.Count(rows.Count, "song") + (artistAlbums.Count > 0 ? "  ·  " + Theme.Count(artistAlbums.Count, "album") : "") + "  ·  " + Theme.Count(rows.Sum(t => t.Plays), "play");
                    break;
                case "playlist": eyebrow = "PLAYLIST"; title = playlist?.Name ?? "Playlist"; meta = Theme.Count(rows.Count, "song") + (total > 0 ? ", " + Theme.Long(total) : ""); break;
                case "missing":
                    eyebrow = "MISSING INFO";
                    title = MissingPage.Kinds.FirstOrDefault(k => k.key == Route.Key).title ?? "Missing";
                    meta = Theme.Count(rows.Count, "song") + "  ·  select songs (Ctrl+A for all) and right-click to fix them";
                    break;
                case "version":
                    eyebrow = "VERSIONS";
                    title = rows.Count > 0 ? LyricsOnline.CleanTitle(rows[0].Name) : "Versions";
                    meta = (rows.Count > 0 ? rows[0].MainArtist + "  ·  " : "") + Theme.Count(rows.Count, "version");
                    break;
            }

            float headH = big ? Theme.F(260) : Theme.F(118);
            // a wash of the header art's color behind the top of the page
            if (big)
            {
                var c = HeaderColor(coverTrack);
                using (var b = new LinearGradientBrush(new RectangleF(0, -Offset - 1, Width, headH + Theme.F(120) + Offset + 2), Theme.Alpha(c, 90), Theme.Alpha(c, 0), 90f))
                    { b.WrapMode = WrapMode.TileFlipXY; g.FillRectangle(b, 0, -Offset, Width, headH + Theme.F(120) + Offset); }
            }
            float tx = pad;
            if (big)
            {
                float s = Theme.F(200);
                var ar = new RectangleF(pad, y + Theme.F(24), s, s);
                DrawHeaderArt(g, ar, coverTrack);
                tx = ar.Right + Theme.F(28);
                float bottom = ar.Bottom;
                var tf = TitleFont(g, title, Width - tx - pad);
                float th = tf.GetHeight(g) * 1.05f;
                Theme.Write(g, eyebrow, Theme.Font(12, FontStyle.Regular, true), Theme.Text, new RectangleF(tx, bottom - th - Theme.F(62), Width - tx - pad, Theme.F(20)));
                Theme.Write(g, title, tf, Theme.Text, new RectangleF(tx, bottom - th - Theme.F(36), Width - tx - pad, th));
                Theme.Write(g, meta, Theme.Font(13.5f), Theme.Sub, new RectangleF(tx, bottom - Theme.F(30), Width - tx - pad, Theme.F(24)));
                y = bottom + Theme.F(26);
            }
            else
            {
                if (eyebrow.Length > 0) Theme.Write(g, eyebrow, Theme.Font(12, FontStyle.Regular, true), Theme.Sub, new RectangleF(pad, y + Theme.F(8), Width - pad * 2, Theme.F(20)));
                Theme.Write(g, title, Theme.Font(34, FontStyle.Bold), Theme.Text, new RectangleF(pad, y + Theme.F(24), Width - pad * 2, Theme.F(48)));
                Theme.Write(g, meta, Theme.Font(13.5f), Theme.Sub, new RectangleF(pad, y + Theme.F(72), Width - pad * 2, Theme.F(22)));
                y += Theme.F(108);
            }

            // actions
            if (rows.Count > 0 || kind == "queue")
            {
                float bx = pad;
                if (rows.Count > 0)
                {
                    bool playingThis = shell.Player.IsPlaying && shell.Player.Current != null && rows.Contains(shell.Player.Current) && kind != "queue";
                    PlayButton(g, "hplay", new RectangleF(bx, y, Theme.F(54), Theme.F(54)), playingThis, () =>
                    {
                        if (playingThis) shell.Player.Toggle();
                        else if (kind == "queue") shell.Player.JumpTo(0);
                        else shell.Player.PlayList(Ids, 0);
                    }, playingThis ? "Pause" : "Play");
                    bx += Theme.F(70);
                    if (kind != "queue")
                    {
                        IconButton(g, "hshuf", new RectangleF(bx, y + Theme.F(7), Theme.F(40), Theme.F(40)), "shuffle", () => shell.Player.ShuffleAll(Ids), "Shuffle play");
                        bx += Theme.F(52);
                    }
                }
                float py = y + Theme.F(9);
                if (kind == "playlist" && playlist != null)
                {
                    var p = playlist;
                    bx = Pill(g, "ren", bx, py, "Rename", () => { var n = Ask.ForText(FindForm(), "Rename playlist", "New name", p.Name, "Rename"); if (n != null) shell.Lib.RenamePlaylist(p, n); }, icon: "edit").Right + Theme.F(10);
                    bx = Pill(g, "del", bx, py, "Delete", () =>
                    {
                        if (MessageBox.Show(FindForm(), "Delete the playlist \"" + p.Name + "\"? The songs stay in your library.", "Delete playlist", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                        { shell.Lib.DeletePlaylist(p); shell.Go(new Route("home")); }
                    }, icon: "trash").Right + Theme.F(10);
                }
                if (kind == "queue" && rows.Count > 0) bx = Pill(g, "clr", bx, py, "Clear up next", () => shell.Player.ClearUpNext(), icon: "close").Right + Theme.F(10);
                if (kind == "songs") bx = Pill(g, "most", bx, py, sortKey == "plays" ? "Most played  ✓" : "Most played", () => { sortKey = sortKey == "plays" ? "added" : "plays"; sortDesc = true; shell.Prefs.SongsSort = sortKey; Reload(); }).Right + Theme.F(10);
                if (kind == "missing" && rows.Count > 0)
                {
                    var all = rows.ToList();
                    if (Route.Key == "noart") bx = Pill(g, "fc", bx, py, "Find a cover for the first one", () => shell.FindCover(new List<Track> { all[0] }), icon: "search").Right + Theme.F(10);
                    else if (Route.Key == "nolyrics") bx = Pill(g, "la", bx, py, "Look again for all", () => { shell.Lyrics.TryAllAgain(); shell.Toast("Looking again for songs without lyrics"); }, icon: "refresh").Right + Theme.F(10);
                    else
                    {
                        bx = Pill(g, "ea", bx, py, "Edit all " + rows.Count, () => shell.EditInfo(all), icon: "edit").Right + Theme.F(10);
                        if (Route.Key == "notitle" || Route.Key == "noartist") bx = Pill(g, "ff", bx, py, "Fill in from file names", () => shell.FillFromNames(all)).Right + Theme.F(10);
                    }
                }
                if (kind == "album" && rows.Count > 0)
                {
                    var all = rows.ToList();
                    bx = Pill(g, "ed", bx, py, "Edit", () => shell.EditInfo(all), icon: "edit").Right + Theme.F(10);
                    bx = Pill(g, "cv", bx, py, "Cover", () => shell.FindCover(new List<Track> { all[0] }), icon: "album").Right + Theme.F(10);
                }
                if (kind == "artist" || kind == "album")
                {
                    var ids = Ids;
                    bx = Pill(g, "pnext", bx, py, "Play next", () => { shell.Player.PlayNext(ids); shell.Toast("Playing next: " + title); }).Right + Theme.F(10);
                    bx = Pill(g, "qadd", bx, py, "Add to up next", () => { shell.Player.AddToQueue(ids); shell.Toast("Added " + Theme.Count(ids.Count, "song") + " to up next"); }).Right + Theme.F(10);
                }
                y += Theme.F(72);
            }

            // queue: what's playing now, above the list
            if (kind == "queue" && shell.Player.Current != null)
            {
                Theme.Write(g, "NOW PLAYING", Theme.Font(12, FontStyle.Regular, true), Theme.Faint, new RectangleF(pad, y, Width, Theme.F(22)));
                y += Theme.F(28);
                var t = shell.Player.Current;
                float s = Theme.F(52);
                var ar = new RectangleF(pad, y, s, s);
                var img = Art.Get(t);
                if (img != null) Theme.Image(g, img, ar, Theme.F(6)); else Theme.NoArt(g, ar, Theme.F(6), t.Id);
                Theme.Write(g, t.Name, Theme.Font(15, FontStyle.Regular, true), Theme.AccentText, new RectangleF(ar.Right + Theme.F(14), y + Theme.F(4), Width - ar.Right - pad * 2, Theme.F(22)));
                Theme.Write(g, t.ArtistName, Theme.Font(13), Theme.Sub, new RectangleF(ar.Right + Theme.F(14), y + Theme.F(27), Width - ar.Right - pad * 2, Theme.F(20)));
                y += s + Theme.F(26);
                Theme.Write(g, "UP NEXT", Theme.Font(12, FontStyle.Regular, true), Theme.Faint, new RectangleF(pad, y, Width, Theme.F(22)));
                y += Theme.F(24);
            }

            // search: matching artists and albums as chips
            if (kind == "search" && (foundArtists.Count > 0 || foundAlbums.Count > 0))
            {
                float x = pad;
                float rowY = y;
                foreach (var a in foundArtists)
                {
                    var aa = a;
                    var r = Pill(g, "fa:" + a.Key, x, rowY, a.Name, () => shell.Go(new Route("artist", aa.Key)), icon: "artist", h: 34);
                    x = r.Right + Theme.F(8);
                    if (x > Width - Theme.F(200)) break;
                }
                foreach (var al in foundAlbums)
                {
                    if (x > Width - Theme.F(200)) break;
                    var aa = al;
                    var r = Pill(g, "fl:" + al.Key, x, rowY, al.Name, () => shell.Go(new Route("album", aa.Key)), icon: "album", h: 34);
                    x = r.Right + Theme.F(8);
                }
                y += Theme.F(52);
            }

            // artist: their albums as a row of tiles
            if (kind == "artist" && artistAlbums.Count > 0)
            {
                Theme.Write(g, "Albums", Theme.Font(19, FontStyle.Bold), Theme.Text, new RectangleF(pad, y, Width, Theme.F(28)));
                y += Theme.F(40);
                float tile = Theme.F(150), gap = Theme.F(20);
                int n = Math.Max(1, (int)((Width - pad * 2 + gap) / (tile + gap)));
                float x = pad;
                foreach (var al in artistAlbums.Take(n))
                {
                    var aa = al;
                    string id = "al:" + al.Key;
                    var tr = new RectangleF(x, y, tile, tile);
                    bool hot = Hover(id);
                    if (hot) Theme.Fill(g, Theme.Surface, RectangleF.Inflate(new RectangleF(x, y, tile, tile + Theme.F(48)), Theme.F(8), Theme.F(8)), Theme.F(12));
                    var img = Art.Get(al.Cover);
                    if (img != null) Theme.Image(g, img, tr, Theme.F(8)); else Theme.NoArt(g, tr, Theme.F(8), al.Cover?.Id ?? 1, false, "album");
                    Theme.Write(g, al.Name, Theme.Font(13.5f, FontStyle.Regular, true), Theme.Text, new RectangleF(x, tr.Bottom + Theme.F(8), tile, Theme.F(20)));
                    Theme.Write(g, (al.Year.Length > 0 ? al.Year + " · " : "") + Theme.Count(al.Tracks.Count, "song"), Theme.Font(12), Theme.Sub, new RectangleF(x, tr.Bottom + Theme.F(28), tile, Theme.F(18)));
                    AddHit(id, new RectangleF(x, y, tile, tile + Theme.F(48)), () => shell.Go(new Route("album", aa.Key)));
                    x += tile + gap;
                }
                y += tile + Theme.F(70);
                Theme.Write(g, "Songs", Theme.Font(19, FontStyle.Bold), Theme.Text, new RectangleF(pad, y, Width, Theme.F(28)));
                y += Theme.F(40);
            }
            return y;
        }

        Font TitleFont(Graphics g, string title, float w)
        {
            foreach (var size in new[] { 46f, 38f, 32f, 26f })
            {
                var f = Theme.Font(size, FontStyle.Bold);
                if (Theme.Measure(g, title, f) <= w) return f;
            }
            return Theme.Font(24, FontStyle.Bold);
        }

        Color HeaderColor(Track cover)
        {
            if (kind == "favorites") return Color.FromArgb(120, 70, 255);
            if (cover != null && headerColorFor != cover.Id)
            {
                var img = Art.Get(cover);
                if (img != null) { headerColor = Art.Accent(img); headerColorFor = cover.Id; }
            }
            return headerColor.IsEmpty ? Theme.Accent : headerColor;
        }

        void DrawHeaderArt(Graphics g, RectangleF r, Track cover)
        {
            // soft shadow
            bool circle = kind == "artist";
            for (int i = 3; i >= 1; i--)
            {
                var sr = new RectangleF(r.X - i * 2, r.Y + i * 3, r.Width + i * 4, r.Height + i * 3);
                if (circle) using (var b = new SolidBrush(Theme.Alpha(Color.Black, 26))) g.FillEllipse(b, sr);
                else Theme.Fill(g, Theme.Alpha(Color.Black, 26), sr, Theme.F(12) + i * 2);
            }
            if (kind == "favorites")
            {
                using (var p = Theme.Round(r, Theme.F(10)))
                using (var b = new LinearGradientBrush(r, Color.FromArgb(120, 70, 255), Color.FromArgb(40, 160, 255), 45f)) g.FillPath(b, p);
                float s = r.Width * 0.42f;
                Icons.Draw(g, "heart", new RectangleF(r.X + (r.Width - s) / 2, r.Y + (r.Height - s) / 2, s, s), Color.White, true);
                return;
            }
            if (kind == "playlist")
            {
                var covers = rows.Where(t => t.HasArt).Select(t => t).GroupBy(t => t.AlbumKey).Select(gp => gp.First()).Take(4).ToList();
                if (covers.Count >= 4)
                {
                    float h = r.Width / 2;
                    var st = g.Save();
                    using (var p = Theme.Round(r, Theme.F(10))) g.SetClip(p, CombineMode.Intersect);
                    for (int i = 0; i < 4; i++)
                    {
                        var cr = new RectangleF(r.X + (i % 2) * h, r.Y + (i / 2) * h, h, h);
                        var im = Art.Get(covers[i]);
                        if (im != null) g.DrawImage(im, cr); else Theme.NoArt(g, cr, 0, covers[i].Id);
                    }
                    g.Restore(st);
                    return;
                }
                cover = covers.FirstOrDefault();
                if (cover == null) { Theme.NoArt(g, r, Theme.F(10), playlist?.Id ?? 5, false, "playlist"); return; }
            }
            var img = Art.Get(cover);
            if (img != null) Theme.Image(g, img, r, Theme.F(10), circle);
            else Theme.NoArt(g, r, Theme.F(10), cover?.Id ?? 2, circle, circle ? "artist" : "album");
        }

        void DrawEmpty(Graphics g, float y)
        {
            float pad = Theme.F(28);
            string msg;
            switch (kind)
            {
                case "favorites": msg = "Songs you like show up here. Press the heart next to any song."; break;
                case "playlist": msg = "This playlist is empty. Right-click songs anywhere → Add to playlist."; break;
                case "search": msg = "Nothing in your library matches that."; break;
                case "songs": msg = shell.Lib.Scan.Running ? "Looking through your music folders…" : "No songs yet. Add the folder where your music is."; break;
                default: msg = "Nothing here."; break;
            }
            Theme.Write(g, msg, Theme.Font(15), Theme.Sub, new RectangleF(pad, y + Theme.F(10), Width - pad * 2, Theme.F(30)));
            if (kind == "songs" && !shell.Lib.Scan.Running) Pill(g, "addf", pad, y + Theme.F(56), "Add music folder", shell.AddFolder, true, "folder", 40);
            ContentHeight = y + Theme.F(140);
        }

        // ---------- mouse and keys ----------
        void RowDown(int i, MouseEventArgs e)
        {
            dragFrom = -1; dragging = false;
            if (e.Button == MouseButtons.Right) { if (!sel.Contains(i)) { sel.Clear(); sel.Add(i); anchor = focus = i; Invalidate(); } return; }
            bool ctrl = (ModifierKeys & Keys.Control) != 0, shift = (ModifierKeys & Keys.Shift) != 0;
            if (shift && anchor >= 0)
            {
                if (!ctrl) sel.Clear();
                for (int k = Math.Min(anchor, i); k <= Math.Max(anchor, i); k++) sel.Add(k);
            }
            else if (ctrl) { if (!sel.Remove(i)) sel.Add(i); anchor = i; }
            else
            {
                if (!sel.Contains(i)) sel.Clear();
                sel.Add(i); anchor = i;
                if (CanReorder) { dragFrom = i; dragStart = ToContent(e.Location); }
            }
            focus = i;
            Invalidate();
        }

        void RowDrag(int i, PointF p)
        {
            if (dragFrom < 0) return;
            if (!dragging && Math.Abs(p.Y - dragStart.Y) < Theme.F(6)) return;
            dragging = true;
            float top = listTop + colH;
            dropAt = Math.Max(0, Math.Min(rows.Count, (int)Math.Round((p.Y - top) / rowH)));
            // scroll when dragging near the edges
            float vy = p.Y - Offset;
            if (vy < Theme.F(40)) ScrollTo(Offset - Theme.F(30));
            else if (vy > Height - Theme.F(40)) ScrollTo(Offset + Theme.F(30));
            Invalidate();
        }

        void RowDrop()
        {
            if (!dragging || dropAt < 0) { dragging = false; dragFrom = -1; return; }
            var moving = sel.Contains(dragFrom) ? sel.OrderBy(x => x).ToList() : new List<int> { dragFrom };
            int to = dropAt;
            dragging = false; dragFrom = -1; dropAt = -1;
            if (kind == "queue") shell.Player.MoveUpNext(moving, to);
            else if (playlist != null)
            {
                var items = playlist.Items;
                var ids = moving.Select(k => rows[k].Id).ToList();
                int before = moving.Count(k => k < to);
                foreach (var k in moving.OrderByDescending(k => k)) items.RemoveAt(k);
                items.InsertRange(Math.Max(0, Math.Min(items.Count, to - before)), ids);
                shell.Lib.SavePlaylist(playlist);
            }
            sel.Clear();
            int start = to - moving.Count(k => k < to);
            for (int k = 0; k < moving.Count; k++) sel.Add(start + k);
            Reload();
        }

        void RowMenu(int i, Point pt)
        {
            if (!sel.Contains(i)) { sel.Clear(); sel.Add(i); anchor = focus = i; }
            var idx = sel.OrderBy(x => x).ToList();
            var tracks = idx.Select(k => rows[k]).ToList();
            var ctx = new TrackContext { Playlist = playlist, Queue = kind == "queue", QueueIndexes = kind == "queue" ? idx : null };
            if (playlist != null)
            {
                var p = playlist;
                ctx.Removed = () =>
                {
                    foreach (var k in idx.OrderByDescending(k => k)) if (k < p.Items.Count) p.Items.RemoveAt(k);
                    shell.Lib.SavePlaylist(p); sel.Clear(); Reload();
                };
            }
            Invalidate();
            shell.TrackMenu(tracks, pt, ctx);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (rows.Count > 0)
            {
                int to = -1;
                switch (e.KeyCode)
                {
                    case Keys.Down: to = Math.Min(rows.Count - 1, focus + 1); break;
                    case Keys.Up: to = Math.Max(0, focus < 0 ? 0 : focus - 1); break;
                    case Keys.Enter: if (focus >= 0) PlayFrom(focus); e.Handled = true; break;
                    case Keys.A: if (e.Control) { for (int k = 0; k < rows.Count; k++) sel.Add(k); Invalidate(); e.Handled = true; } break;
                    case Keys.Delete:
                        if (sel.Count > 0 && (kind == "playlist" || kind == "queue"))
                        {
                            var idx = sel.OrderByDescending(x => x).ToList();
                            if (kind == "queue") { foreach (var k in idx) shell.Player.RemoveUpNext(k); }
                            else if (playlist != null) { foreach (var k in idx) playlist.Items.RemoveAt(k); shell.Lib.SavePlaylist(playlist); }
                            sel.Clear(); Reload(); e.Handled = true;
                        }
                        break;
                    case Keys.Apps:
                        if (focus >= 0) RowMenu(focus, PointToScreen(new Point(Theme.P(120), (int)(listTop + colH + focus * rowH - Offset + rowH / 2))));
                        e.Handled = true; break;
                }
                if (to >= 0)
                {
                    if (e.Shift && anchor >= 0) { sel.Clear(); for (int k = Math.Min(anchor, to); k <= Math.Max(anchor, to); k++) sel.Add(k); }
                    else { sel.Clear(); sel.Add(to); anchor = to; }
                    focus = to;
                    float top = listTop + colH + to * rowH;
                    EnsureVisible(top, top + rowH, colH);
                    Invalidate();
                    e.Handled = true;
                }
            }
            base.OnKeyDown(e);
        }

        protected override void OnEmptyRightClick(Point screen) { }
#if TESTHOOKS
        internal int RowCount => rows.Count;
        internal string RowName(int i) => rows[i].Name;
        internal void TestDrag(int from, int to) { sel.Clear(); sel.Add(from); dragFrom = from; dragging = true; dropAt = to; RowDrop(); }
        internal void TestSelect(params int[] idx) { sel.Clear(); foreach (var i in idx) sel.Add(i); }
#endif
        protected override void OnBackButton() => shell.Back();
        protected override void OnForwardButton() => shell.Forward();
    }
}
