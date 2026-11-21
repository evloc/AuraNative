using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Aura
{
    // Now Playing: the cover big, and the lyrics following the song.
    sealed class NowPlayingPage : Canvas
    {
        readonly IShell shell;
        readonly LyricsPane pane = new LyricsPane();
        readonly Timer tick = new Timer { Interval = 33 };
        Bitmap backdrop; long backdropFor = -1; Size backdropSize;
        RectangleF lyricsArea;
        Track shown;

        public NowPlayingPage(IShell shell)
        {
            this.shell = shell;
            Scrollable = false;
            tick.Tick += (s, e) =>
            {
                // smooth while lyrics move; slow and cheap otherwise
                bool moving = shell.Player.IsPlaying && pane.Lyrics != null && pane.Lyrics.Timed;
                tick.Interval = moving ? 33 : 250;
                Invalidate();
            };
            tick.Start();
            Reload();
        }

        public void Reload()
        {
            shown = shell.Player.Current;
            pane.Set(shown == null ? null : shell.Lyrics.Get(shown), shown?.Id ?? -1);
            Invalidate();
        }

        protected override void Dispose(bool disposing) { if (disposing) { tick.Dispose(); backdrop?.Dispose(); } base.Dispose(disposing); }

        // the cover, blurred, behind everything
        void DrawBackdrop(Graphics g, Track t)
        {
            var img = Art.Get(t);
            if (img == null) { using (var b = new LinearGradientBrush(new Rectangle(0, 0, Width, Height), Theme.Alpha(Theme.Accent, 70), Theme.Bg, 60f)) g.FillRectangle(b, 0, 0, Width, Height); return; }
            if (backdrop == null || backdropFor != t.Id || backdropSize != Size)
            {
                backdrop?.Dispose();
                backdrop = new Bitmap(Math.Max(1, Width), Math.Max(1, Height));
                using (var tiny = new Bitmap(10, 10))
                {
                    using (var tg = Graphics.FromImage(tiny)) { tg.InterpolationMode = InterpolationMode.HighQualityBilinear; tg.DrawImage(img, 0, 0, 10, 10); }
                    using (var bg = Graphics.FromImage(backdrop))
                    {
                        bg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        bg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        float s = Math.Max(Width, Height) * 1.25f;
                        using (var ia = new System.Drawing.Imaging.ImageAttributes()) { ia.SetWrapMode(WrapMode.TileFlipXY); bg.DrawImage(tiny, new Rectangle((int)((Width - s) / 2), (int)((Height - s) / 2), (int)s, (int)s), 0, 0, 10, 10, GraphicsUnit.Pixel, ia); }
                        using (var dark = new SolidBrush(Color.FromArgb(150, 6, 8, 12))) bg.FillRectangle(dark, 0, 0, Width, Height);
                        using (var fade = new LinearGradientBrush(new Rectangle(0, 0, Width, Height), Color.FromArgb(0, Theme.Bg), Color.FromArgb(200, Theme.Bg), 90f)) { fade.WrapMode = WrapMode.TileFlipXY; bg.FillRectangle(fade, 0, 0, Width, Height); }
                    }
                }
                backdropFor = t.Id; backdropSize = Size;
            }
            g.DrawImageUnscaled(backdrop, 0, 0);
        }

        protected override void Draw(Graphics g, RectangleF view)
        {
            var t = shell.Player.Current;
            if (t != shown) Reload();
            if (t == null)
            {
                Theme.Write(g, "Nothing playing", Theme.Font(30, FontStyle.Bold), Theme.Text, new RectangleF(Theme.F(40), Theme.F(40), Width, Theme.F(44)));
                Theme.Write(g, "Pick a song, or shuffle everything.", Theme.Font(15), Theme.Sub, new RectangleF(Theme.F(40), Theme.F(92), Width, Theme.F(26)));
                if (shell.Lib.Tracks.Count > 0) Pill(g, "shuf", Theme.F(40), Theme.F(136), "Shuffle everything", () => shell.Player.ShuffleAll(shell.Lib.Tracks.ConvertAll(x => x.Id)), true, "shuffle", 42);
                return;
            }
            DrawBackdrop(g, t);
            bool wide = Width >= Theme.F(860);
            float pad = Theme.F(44);
            float colW = wide ? Math.Min(Theme.F(400), Width * 0.36f) : Math.Min(Theme.F(170), Width * 0.3f);
            float art = colW;
            float infoH = Theme.F(170);
            float top = wide ? Math.Max(Theme.F(28), (Height - art - infoH) / 2) : Theme.F(28);
            var ar = new RectangleF(pad, top, art, art);
            for (int i = 4; i >= 1; i--) Theme.Fill(g, Theme.Alpha(Color.Black, 28), new RectangleF(ar.X - i * 3, ar.Y + i * 4, ar.Width + i * 6, ar.Height + i * 4), Theme.F(14) + i * 3);
            var img = Art.Get(t);
            if (img != null) Theme.Image(g, img, ar, Theme.F(12)); else Theme.NoArt(g, ar, Theme.F(12), t.Id);

            // title, artist, album, buttons
            float tx = wide ? pad : ar.Right + Theme.F(24), ty = wide ? ar.Bottom + Theme.F(22) : top + Theme.F(4), tw = wide ? colW : Width - tx - pad;
            Theme.Write(g, t.Name, Theme.Font(26, FontStyle.Bold), Theme.Text, new RectangleF(tx, ty, tw, Theme.F(36)));
            ty += Theme.F(38);
            var af = Theme.Font(15);
            float aw = Math.Min(tw, Theme.Measure(g, t.ArtistName, af) + Theme.F(6));
            var arr = new RectangleF(tx, ty, aw, Theme.F(24));
            using (var uf = new Font(af, Hover("artist") ? FontStyle.Underline : FontStyle.Regular)) Theme.Write(g, t.ArtistName, uf, Hover("artist") ? Theme.Text : Theme.Sub, arr);
            AddHit("artist", arr, () => shell.Go(new Route("artist", t.MainArtist.ToLowerInvariant())));
            ty += Theme.F(26);
            if (t.Album.Length > 0)
            {
                float alw = Math.Min(tw, Theme.Measure(g, t.Album, Theme.Font(13.5f)) + Theme.F(6));
                var alr = new RectangleF(tx, ty, alw, Theme.F(22));
                Theme.Write(g, t.Album, Theme.Font(13.5f), Hover("album") ? Theme.Text : Theme.Faint, alr);
                AddHit("album", alr, () => shell.Go(new Route("album", t.AlbumKey)));
            }
            ty += Theme.F(40);
            float bx = tx;
            IconButton(g, "fav", new RectangleF(bx, ty, Theme.F(38), Theme.F(38)), "heart", () => shell.Lib.SetFav(t, !t.Fav), t.Fav ? "Remove from Favorites" : "Add to Favorites", t.Fav, filled: t.Fav, circleBg: true, dot: false);
            bx += Theme.F(48);
            var l = pane.Lyrics;
            if (l != null) bx = Pill(g, "full", bx, ty + Theme.F(1), "Full screen", shell.FullScreenLyrics, false, "expand", 36, "Full-screen lyrics (F11)").Right + Theme.F(8);
            Pill(g, "find", bx, ty + Theme.F(1), l == null ? "Find lyrics" : "Other lyrics", () => shell.FindLyrics(t), false, "search", 36);

            // lyrics
            lyricsArea = wide ? new RectangleF(ar.Right + Theme.F(70), Theme.F(70), Width - ar.Right - Theme.F(70) - pad, Height - Theme.F(100))
                              : new RectangleF(pad, ty + Theme.F(70), Width - pad * 2, Height - ty - Theme.F(90));
            float hy = lyricsArea.Y - Theme.F(40);
            if (l == null)
            {
                Theme.Write(g, "No lyrics for this song yet", Theme.Font(22, FontStyle.Bold), Theme.Text, new RectangleF(lyricsArea.X, lyricsArea.Y + Theme.F(20), lyricsArea.Width, Theme.F(32)));
                string why = t.Artist.Trim().Length == 0 ? "Give it an artist tag and AURA can look for them online. Or press Find lyrics to search yourself."
                    : shell.Prefs.AutoLyrics ? "AURA looks online by itself for songs without lyrics. Unreleased songs often aren't online anywhere — press Find lyrics to search yourself."
                    : "Press Find lyrics to look online (LRCLIB and Genius).";
                Theme.Write(g, why, Theme.Font(14.5f), Theme.Sub, new RectangleF(lyricsArea.X, lyricsArea.Y + Theme.F(60), Math.Min(lyricsArea.Width, Theme.F(520)), Theme.F(80)), Theme.Wrap);
                Pill(g, "find2", lyricsArea.X, lyricsArea.Y + Theme.F(150), "Find lyrics", () => shell.FindLyrics(t), true, "search", 40);
                return;
            }
            string note = l.Source + (l.ByWord ? "  ·  word by word" : l.Timed ? "  ·  timed" : "  ·  not timed");
            Theme.Write(g, "LYRICS", Theme.Font(12, FontStyle.Regular, true), Theme.Sub, new RectangleF(lyricsArea.X, hy, Theme.F(80), Theme.F(22)));
            Theme.Write(g, note, Theme.Font(12.5f), Theme.Faint, new RectangleF(lyricsArea.X + Theme.F(76), hy, lyricsArea.Width - Theme.F(200), Theme.F(22)));
            if (l.FromAura)
            {
                var f = Theme.Font(12.5f);
                float w = Theme.Measure(g, "Remove these", f) + Theme.F(8);
                var rr = new RectangleF(lyricsArea.Right - w, hy, w, Theme.F(22));
                Theme.Write(g, "Remove these", f, Hover("rm") ? Theme.Text : Theme.Faint, rr, Theme.Right);
                AddHit("rm", rr, () => { shell.Lyrics.Remove(t); Reload(); }, "Wrong song? Remove the lyrics AURA found");
            }
            pane.Stage = false;
            pane.Draw(g, lyricsArea, shell.Player.Position, this, sec => shell.Player.Seek(sec));
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (lyricsArea.Contains(e.Location)) { pane.Wheel(e.Delta, lyricsArea.Height); Invalidate(); }
        }

        protected override void OnBackButton() => shell.Back();
        protected override void OnForwardButton() => shell.Forward();
    }
}
