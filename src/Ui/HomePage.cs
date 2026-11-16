using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace Aura
{
    sealed class HomePage : Canvas
    {
        readonly IShell shell;
        public HomePage(IShell shell) { this.shell = shell; }
        public void Reload() => Invalidate();

        protected override void Draw(Graphics g, RectangleF view)
        {
            var lib = shell.Lib;
            float pad = Theme.F(28), y = Theme.F(8);
            // a glow of the current song's color at the top
            using (var b = new LinearGradientBrush(new RectangleF(0, -1, Width, Theme.F(340)), Theme.Alpha(Theme.Accent, 60), Theme.Alpha(Theme.Accent, 0), 90f))
                { b.WrapMode = WrapMode.TileFlipXY; g.FillRectangle(b, 0, 0, Width, Theme.F(340)); }
            int hr = DateTime.Now.Hour;
            string hello = hr < 5 ? "Up late" : hr < 12 ? "Good morning" : hr < 18 ? "Good afternoon" : "Good evening";
            Theme.Write(g, hello, Theme.Font(34, FontStyle.Bold), Theme.Text, new RectangleF(pad, y + Theme.F(22), Width - pad * 2, Theme.F(48)));
            double total = lib.Tracks.Sum(t => t.Duration);
            string meta = lib.Tracks.Count == 0 ? (lib.Scan.Running ? "Looking through your music…" : "Your library is empty") :
                Theme.Count(lib.Tracks.Count, "song") + "  ·  " + Theme.Long(total);
            Theme.Write(g, meta, Theme.Font(13.5f), Theme.Sub, new RectangleF(pad, y + Theme.F(70), Width - pad * 2, Theme.F(22)));
            y += Theme.F(110);

            if (lib.Tracks.Count == 0)
            {
                if (!lib.Scan.Running)
                {
                    Theme.Write(g, "Show AURA where your music is. It reads the songs where they are — nothing is moved or copied.", Theme.Font(15), Theme.Sub, new RectangleF(pad, y, Width - pad * 2, Theme.F(50)), Theme.Wrap);
                    Pill(g, "addf", pad, y + Theme.F(56), "Add music folder", shell.AddFolder, true, "folder", 42);
                }
                ContentHeight = y + Theme.F(160);
                return;
            }

            float bx = Pill(g, "shufall", pad, y, "Shuffle everything", () => shell.Player.ShuffleAll(lib.Tracks.Select(t => t.Id).ToList()), true, "shuffle", 42).Right + Theme.F(12);
            var recent = lib.Tracks.OrderByDescending(t => t.Added).Take(200).Select(t => t.Id).ToList();
            Pill(g, "playnew", bx, y, "Play what's new", () => shell.Player.PlayList(recent), false, "play", 42);
            y += Theme.F(70);

            // quick picks: favorites, playlists, recent albums, as wide cards
            var quick = new List<(string id, string name, Track cover, Action open, Action play, string icon)>();
            var favs = lib.Tracks.Where(t => t.Fav).ToList();
            if (favs.Count > 0) quick.Add(("q:fav", "Favorites", null, () => shell.Go(new Route("favorites")), () => shell.Player.PlayList(favs.OrderByDescending(t => t.Added).Select(t => t.Id).ToList()), "heart"));
            foreach (var p in lib.Playlists.OrderByDescending(p => p.Updated).Take(3))
            {
                var pp = p;
                quick.Add(("q:pl" + p.Id, p.Name, p.Items.Select(lib.Get).FirstOrDefault(t => t != null && t.HasArt), () => shell.Go(new Route("playlist", "", pp.Id)), () => shell.Player.PlayList(pp.Items), "playlist"));
            }
            var albums = lib.Albums().Where(a => a.Name != "Singles").ToList();
            foreach (var a in albums.OrderByDescending(a => a.Tracks.Max(t => t.LastPlayed)).Take(6 - Math.Min(6, quick.Count)))
            {
                var aa = a;
                quick.Add(("q:al" + a.Key, a.Name, a.Cover, () => shell.Go(new Route("album", aa.Key)), () => shell.Player.PlayList(aa.Tracks.Select(t => t.Id).ToList()), "album"));
            }
            if (quick.Count > 0)
            {
                int cols = Width > Theme.F(1100) ? 3 : 2;
                float gap = Theme.F(14), cw = (Width - pad * 2 - gap * (cols - 1)) / cols, ch = Theme.F(64);
                for (int i = 0; i < quick.Count && i < 6; i++)
                {
                    var q = quick[i];
                    var r = new RectangleF(pad + (i % cols) * (cw + gap), y + (i / cols) * (ch + gap), cw, ch);
                    bool hot = HoverStarts(q.id);
                    Theme.Fill(g, hot ? Theme.Surface2 : Theme.Alpha(Theme.Surface, 230), r, Theme.F(8));
                    var ar = new RectangleF(r.X, r.Y, ch, ch);
                    var st = g.Save();
                    using (var clip = Theme.Round(r, Theme.F(8))) g.SetClip(clip, CombineMode.Intersect);
                    var img = Art.Get(q.cover);
                    if (q.icon == "heart")
                    {
                        using (var b = new LinearGradientBrush(ar, Color.FromArgb(120, 70, 255), Color.FromArgb(40, 160, 255), 45f)) g.FillRectangle(b, ar);
                        float s = ch * 0.42f; Icons.Draw(g, "heart", new RectangleF(ar.X + (ch - s) / 2, ar.Y + (ch - s) / 2, s, s), Color.White, true);
                    }
                    else if (img != null) g.DrawImage(img, ar);
                    else Theme.NoArt(g, ar, 0, q.id.GetHashCode(), false, q.icon);
                    g.Restore(st);
                    Theme.Write(g, q.name, Theme.Font(14, FontStyle.Regular, true), Theme.Text, new RectangleF(ar.Right + Theme.F(14), r.Y, r.Width - ch - Theme.F(70), r.Height));
                    AddHit(q.id, r, q.open);
                    if (hot) PlayButton(g, q.id + ":play", new RectangleF(r.Right - Theme.F(48), r.Y + (ch - Theme.F(38)) / 2, Theme.F(38), Theme.F(38)), false, q.play);
                }
                y += ((Math.Min(6, quick.Count) + cols - 1) / cols) * (ch + gap) + Theme.F(22);
            }

            // shelves
            var played = lib.RecentlyPlayed(12);
            if (played.Count > 0) y = SongShelf(g, y, "Jump back in", played, null);
            var newAlbums = albums.OrderByDescending(a => a.Added).Take(12).ToList();
            if (newAlbums.Count > 0) y = AlbumShelf(g, y, "Recently added", newAlbums, () => shell.Go(new Route("albums")));
            var top = lib.Tracks.Where(t => t.Plays > 0).OrderByDescending(t => t.Plays).ThenByDescending(t => t.LastPlayed).Take(12).ToList();
            if (top.Count >= 3) y = SongShelf(g, y, "On repeat", top, () => { shell.Prefs.SongsSort = "plays"; shell.Go(new Route("songs")); });
            var newSongs = lib.Tracks.OrderByDescending(t => t.Added).Take(12).ToList();
            y = SongShelf(g, y, "New songs", newSongs, () => { shell.Prefs.SongsSort = "added"; shell.Prefs.SongsDesc = true; shell.Go(new Route("songs")); });
            var artists = lib.Artists().Where(a => a.Name != "Unknown artist").OrderByDescending(a => a.Tracks.Sum(t => t.Plays)).ThenByDescending(a => a.Tracks.Count).Take(12).ToList();
            if (artists.Count > 0) y = ArtistShelf(g, y, "Your artists", artists);
            ContentHeight = y + Theme.F(30);
        }

        int PerRow(out float tile)
        {
            float pad = Theme.F(28), gap = Theme.F(22), min = Theme.F(160);
            int n = Math.Max(2, (int)((Width - pad * 2 + gap) / (min + gap)));
            tile = (Width - pad * 2 - gap * (n - 1)) / n;
            return n;
        }

        float ShelfTitle(Graphics g, float y, string title, Action seeAll, string id)
        {
            float pad = Theme.F(28);
            Theme.Write(g, title, Theme.Font(21, FontStyle.Bold), Theme.Text, new RectangleF(pad, y, Width - pad * 2, Theme.F(30)));
            if (seeAll != null)
            {
                var f = Theme.Font(12.5f, FontStyle.Regular, true);
                float w = Theme.Measure(g, "Show all", f) + Theme.F(8);
                var r = new RectangleF(Width - pad - w, y + Theme.F(4), w, Theme.F(24));
                Theme.Write(g, "Show all", f, Hover(id) ? Theme.Text : Theme.Sub, r, Theme.Right);
                AddHit(id, r, seeAll);
            }
            return y + Theme.F(44);
        }

        float SongShelf(Graphics g, float y, string title, List<Track> songs, Action seeAll)
        {
            y = ShelfTitle(g, y, title, seeAll, "all:" + title);
            int n = PerRow(out float tile);
            float pad = Theme.F(28), gap = Theme.F(22);
            var ids = songs.Select(t => t.Id).ToList();
            for (int i = 0; i < Math.Min(n, songs.Count); i++)
            {
                var t = songs[i]; int k = i;
                string id = "s:" + title + ":" + i;
                float x = pad + i * (tile + gap);
                bool hot = HoverStarts(id);
                var box = new RectangleF(x - Theme.F(10), y - Theme.F(10), tile + Theme.F(20), tile + Theme.F(72));
                if (hot) Theme.Fill(g, Theme.Surface, box, Theme.F(12));
                var r = new RectangleF(x, y, tile, tile);
                var img = Art.Get(t);
                if (img != null) Theme.Image(g, img, r, Theme.F(8)); else Theme.NoArt(g, r, Theme.F(8), t.Id);
                bool cur = shell.Player.Current == t;
                Theme.Write(g, t.Name, Theme.Font(14, FontStyle.Regular, true), cur ? Theme.AccentText : Theme.Text, new RectangleF(x, r.Bottom + Theme.F(10), tile, Theme.F(21)));
                Theme.Write(g, t.ArtistName, Theme.Font(12.5f), Theme.Sub, new RectangleF(x, r.Bottom + Theme.F(32), tile, Theme.F(19)));
                var h = AddHit(id, box, () => shell.Player.PlayList(ids, k));
                h.Right = pt => shell.TrackMenu(new List<Track> { t }, pt, new TrackContext());
                if (hot) PlayButton(g, id + ":play", new RectangleF(r.Right - Theme.F(56), r.Bottom - Theme.F(56), Theme.F(46), Theme.F(46)), cur && shell.Player.IsPlaying,
                    () => { if (cur) shell.Player.Toggle(); else shell.Player.PlayList(ids, k); });
            }
            return y + tile + Theme.F(96);
        }

        float AlbumShelf(Graphics g, float y, string title, List<Album> list, Action seeAll)
        {
            y = ShelfTitle(g, y, title, seeAll, "all:" + title);
            int n = PerRow(out float tile);
            float pad = Theme.F(28), gap = Theme.F(22);
            for (int i = 0; i < Math.Min(n, list.Count); i++)
            {
                var a = list[i];
                string id = "a:" + title + ":" + i;
                float x = pad + i * (tile + gap);
                bool hot = HoverStarts(id);
                var box = new RectangleF(x - Theme.F(10), y - Theme.F(10), tile + Theme.F(20), tile + Theme.F(72));
                if (hot) Theme.Fill(g, Theme.Surface, box, Theme.F(12));
                var r = new RectangleF(x, y, tile, tile);
                var img = Art.Get(a.Cover);
                if (img != null) Theme.Image(g, img, r, Theme.F(8)); else Theme.NoArt(g, r, Theme.F(8), a.Cover?.Id ?? 1, false, "album");
                Theme.Write(g, a.Name, Theme.Font(14, FontStyle.Regular, true), Theme.Text, new RectangleF(x, r.Bottom + Theme.F(10), tile, Theme.F(21)));
                Theme.Write(g, a.Artist, Theme.Font(12.5f), Theme.Sub, new RectangleF(x, r.Bottom + Theme.F(32), tile, Theme.F(19)));
                AddHit(id, box, () => shell.Go(new Route("album", a.Key)));
                if (hot) PlayButton(g, id + ":play", new RectangleF(r.Right - Theme.F(56), r.Bottom - Theme.F(56), Theme.F(46), Theme.F(46)), false,
                    () => shell.Player.PlayList(a.Tracks.Select(t => t.Id).ToList()));
            }
            return y + tile + Theme.F(96);
        }

        float ArtistShelf(Graphics g, float y, string title, List<Artist> list)
        {
            y = ShelfTitle(g, y, title, () => shell.Go(new Route("artists")), "all:" + title);
            int n = PerRow(out float tile);
            float pad = Theme.F(28), gap = Theme.F(22);
            for (int i = 0; i < Math.Min(n, list.Count); i++)
            {
                var a = list[i];
                string id = "r:" + i;
                float x = pad + i * (tile + gap);
                bool hot = HoverStarts(id);
                var box = new RectangleF(x - Theme.F(10), y - Theme.F(10), tile + Theme.F(20), tile + Theme.F(72));
                if (hot) Theme.Fill(g, Theme.Surface, box, Theme.F(12));
                var r = new RectangleF(x, y, tile, tile);
                var img = Art.Get(a.Cover);
                if (img != null) Theme.Image(g, img, r, 0, true); else Theme.NoArt(g, r, 0, a.Name.GetHashCode(), true, "artist");
                Theme.Write(g, a.Name, Theme.Font(14, FontStyle.Regular, true), Theme.Text, new RectangleF(x, r.Bottom + Theme.F(10), tile, Theme.F(21)), Theme.Center);
                Theme.Write(g, Theme.Count(a.Tracks.Count, "song"), Theme.Font(12.5f), Theme.Sub, new RectangleF(x, r.Bottom + Theme.F(32), tile, Theme.F(19)), Theme.Center);
                AddHit(id, box, () => shell.Go(new Route("artist", a.Key)));
            }
            return y + tile + Theme.F(96);
        }

        protected override void OnBackButton() => shell.Back();
        protected override void OnForwardButton() => shell.Forward();
    }
}
