using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Aura
{
    // Albums or Artists as a grid of covers.
    sealed class GridPage : Canvas
    {
        readonly IShell shell;
        public readonly Route Route;
        List<Album> albums; List<Artist> artists; List<Library.EraGroup> eras;
        bool isArtists => Route.Kind == "artists";
        bool isEras => Route.Kind == "eras";

        public GridPage(IShell shell, Route r) { this.shell = shell; Route = r; Reload(); }

        public void Reload()
        {
            if (isEras) eras = shell.Lib.Eras();
            else if (isArtists) artists = shell.Lib.Artists().OrderByDescending(a => a.Tracks.Count > 2).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            else albums = shell.Lib.Albums().OrderBy(a => a.Name == "Singles" ? 1 : 0).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            Invalidate();
        }

        protected override void Draw(Graphics g, RectangleF view)
        {
            float pad = Theme.F(28), y = Theme.F(8);
            int n = isEras ? eras.Count : isArtists ? artists.Count : albums.Count;
            Theme.Write(g, isEras ? "Eras" : isArtists ? "Artists" : "Albums", Theme.Font(34, FontStyle.Bold), Theme.Text, new RectangleF(pad, y + Theme.F(24), Width - pad * 2, Theme.F(48)));
            Theme.Write(g, Theme.Count(n, isEras ? "era" : isArtists ? "artist" : "album"), Theme.Font(13.5f), Theme.Sub, new RectangleF(pad, y + Theme.F(72), Width, Theme.F(22)));
            y += Theme.F(118);
            if (n == 0)
            {
                Theme.Write(g, isEras ? "No eras yet. Select songs, right-click → More → Set era… (like \"Goodbye & Good Riddance\" or \"2018 sessions\")." : "Nothing here yet.", Theme.Font(15), Theme.Sub, new RectangleF(pad, y, Width - pad * 2, Theme.F(50)), Theme.Wrap);
                ContentHeight = y + Theme.F(80);
                return;
            }
            float gap = Theme.F(22), min = Theme.F(170);
            int cols = Math.Max(2, (int)((Width - pad * 2 + gap) / (min + gap)));
            float tile = (Width - pad * 2 - gap * (cols - 1)) / cols;
            float th = tile + Theme.F(56);
            int firstRow = Math.Max(0, (int)((view.Top - y) / (th + gap)) - 1);
            int lastRow = (int)((view.Bottom - y) / (th + gap)) + 1;
            for (int row = firstRow; row <= lastRow; row++)
                for (int c = 0; c < cols; c++)
                {
                    int i = row * cols + c;
                    if (i >= n) break;
                    float x = pad + c * (tile + gap), ty = y + row * (th + gap);
                    if (isEras) { var e = eras[i]; Tile(g, "e:" + e.Name, x, ty, tile, e.Cover, false, e.Name, (e.Year.Length > 0 ? e.Year + " · " : "") + Theme.Count(e.Tracks.Count, "song"), () => shell.Go(new Route("era", e.Name)), () => shell.Player.PlayList(e.Tracks.Select(t => t.Id).ToList()), "era"); }
                    else if (isArtists) DrawArtist(g, artists[i], x, ty, tile);
                    else DrawAlbum(g, albums[i], x, ty, tile);
                }
            int rowsN = (n + cols - 1) / cols;
            ContentHeight = y + rowsN * (th + gap) + Theme.F(40);
        }

        void Tile(Graphics g, string id, float x, float y, float tile, Track cover, bool circle, string title, string sub, Action open, Action play, string icon)
        {
            bool hot = HoverStarts(id);
            var box = new RectangleF(x - Theme.F(10), y - Theme.F(10), tile + Theme.F(20), tile + Theme.F(76));
            if (hot) Theme.Fill(g, Theme.Surface, box, Theme.F(12));
            var r = new RectangleF(x, y, tile, tile);
            var img = Art.Get(cover);
            if (img != null) Theme.Image(g, img, r, Theme.F(8), circle);
            else Theme.NoArt(g, r, Theme.F(8), cover?.Id ?? title.GetHashCode(), circle, icon);
            Theme.Write(g, title, Theme.Font(14, FontStyle.Regular, true), Theme.Text, new RectangleF(x, r.Bottom + Theme.F(10), tile, Theme.F(21)));
            Theme.Write(g, sub, Theme.Font(12.5f), Theme.Sub, new RectangleF(x, r.Bottom + Theme.F(32), tile, Theme.F(19)));
            AddHit(id, box, open);
            if (hot)
            {
                float s = Theme.F(46);
                PlayButton(g, id + ":play", new RectangleF(r.Right - s - Theme.F(10), r.Bottom - s - Theme.F(10), s, s), false, play);
            }
        }

        void DrawAlbum(Graphics g, Album a, float x, float y, float tile)
        {
            string sub = a.Name == "Singles" ? a.Artist + " · " + Theme.Count(a.Tracks.Count, "song") : (a.Year.Length > 0 ? a.Year + " · " : "") + a.Artist;
            Tile(g, "a:" + a.Key, x, y, tile, a.Cover, false, a.Name, sub, () => shell.Go(new Route("album", a.Key)),
                () => shell.Player.PlayList(a.Tracks.Select(t => t.Id).ToList()), "album");
        }

        void DrawArtist(Graphics g, Artist a, float x, float y, float tile)
        {
            Tile(g, "r:" + a.Key, x, y, tile, a.Cover, true, a.Name, Theme.Count(a.Tracks.Count, "song"), () => shell.Go(new Route("artist", a.Key)),
                () => shell.Player.ShuffleAll(a.Tracks.Select(t => t.Id).ToList()), "artist");
        }

        protected override void OnBackButton() => shell.Back();
        protected override void OnForwardButton() => shell.Forward();
    }
}
