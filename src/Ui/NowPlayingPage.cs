using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Aura
{
    // Now Playing: the cover big, and the lyrics following the song.
    sealed class NowPlayingPage : Canvas
    {
        readonly IShell shell;
        readonly LyricsPane pane = new LyricsPane();
        readonly Timer tick = new Timer { Interval = 250 };      // while paused: now and then is enough
        readonly FrameClock frames;                               // while playing: every screen refresh
        Bitmap backdrop; long backdropFor = -1; Size backdropSize;
        RectangleF lyricsArea;
        Track shown;

        public NowPlayingPage(IShell shell)
        {
            this.shell = shell;
            Scrollable = false;
            tick.Tick += (s, e) => { minimized = FindForm()?.WindowState == FormWindowState.Minimized; if (!shell.Player.IsPlaying) Invalidate(); };
            tick.Start();
            frames = new FrameClock(this, () => Visible && shell.Player.IsPlaying && FullLyricsForm.Open == 0 && !Minimized);
            Reload();
        }

        bool minimized;
        bool Minimized => minimized;

        public void Reload()
        {
            shown = shell.Player.Current;
            pane.Set(shown == null ? null : shell.Lyrics.Get(shown), shown?.Id ?? -1);
            Invalidate();
        }

        protected override void Dispose(bool disposing) { if (disposing) { tick.Dispose(); frames.Dispose(); pane.Dispose(); backdrop?.Dispose(); } base.Dispose(disposing); }

        // the cover, blurred, behind everything
        void DrawBackdrop(Graphics g, Track t)
        {
            var img = Art.Get(t);
            if (img == null) { using (var b = new LinearGradientBrush(new Rectangle(0, 0, Width, Height), Theme.Alpha(Theme.Accent, 70), Theme.Bg, 60f)) g.FillRectangle(b, 0, 0, Width, Height); return; }
            if (backdrop == null || backdropFor != t.Id || backdropSize != Size)
            {
                backdrop?.Dispose();
                backdrop = new Bitmap(Math.Max(1, Width), Math.Max(1, Height), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
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
            float strip = Theme.F(104);
            DrawWave(g, t, new RectangleF(Theme.F(44), Height - strip + Theme.F(14), Width - Theme.F(88), strip - Theme.F(30)));
            float H = Height - strip;
            bool wide = Width >= Theme.F(860);
            float pad = Theme.F(44);
            float colW = wide ? Math.Min(Theme.F(400), Width * 0.36f) : Math.Min(Theme.F(170), Width * 0.3f);
            bool hasVersions = shell.Lib.VersionsOf(t).Count > 1;
            float infoH = Theme.F(170) + (hasVersions ? Theme.F(84) : 0);
            float art = wide ? Math.Max(Theme.F(160), Math.Min(colW, H - infoH - Theme.F(56))) : colW;
            float top = wide ? Math.Max(Theme.F(28), (H - art - infoH) / 2) : Theme.F(28);
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
            bx = Pill(g, "find", bx, ty + Theme.F(1), l == null ? "Find lyrics" : "Other lyrics", () => shell.FindLyrics(t), false, "search", 36).Right + Theme.F(8);
            if (l == null || !l.ByWord)
                IconButton(g, "sync", new RectangleF(bx, ty, Theme.F(38), Theme.F(38)), "wave", () => shell.Transcribe(t), l == null ? "Write the lyrics down by listening" : "Time these lyrics to the song (auto-sync)", circleBg: true);

            // other versions of this song: switch without losing your place
            var versions = shell.Lib.VersionsOf(t);
            if (hasVersions && wide)
            {
                float vy = ty + Theme.F(54), vx = tx;
                Theme.Write(g, "VERSIONS", Theme.Font(11.5f, FontStyle.Regular, true), Theme.Faint, new RectangleF(vx, vy, tw, Theme.F(20)));
                vy += Theme.F(24);
                foreach (var v in versions.Take(8))
                {
                    var f = Theme.Font(12.5f);
                    float w = Math.Min(tw, Theme.Measure(g, v.Name, f) + Theme.F(26));
                    if (vx + w > tx + tw) { vx = tx; vy += Theme.F(36); }
                    if (vy > H - Theme.F(40)) break;
                    var cr = new RectangleF(vx, vy, w, Theme.F(30));
                    bool cur = v.Id == t.Id, hot = Hover("ver:" + v.Id);
                    Theme.Fill(g, cur ? Theme.Alpha(Theme.Accent, 110) : hot ? Theme.Surface2 : Theme.Alpha(Theme.Surface, 200), cr, cr.Height / 2);
                    Theme.Write(g, v.Name, f, cur ? Theme.Text : Theme.Sub, new RectangleF(cr.X + Theme.F(13), cr.Y, cr.Width - Theme.F(20), cr.Height));
                    var vv = v;
                    if (!cur) AddHit("ver:" + v.Id, cr, () =>
                    {
                        shell.Player.SwitchTo(vv);
                        if (!shell.Player.LastSwitchLinedUp && shell.Sound.Get(vv) == null) shell.Toast("Switched at the same time — AURA hasn't listened to this version yet, so it couldn't line them up");
                    }, shell.Sound.Get(vv) != null && shell.Sound.Get(t) != null ? "Switch to this version at the same part of the song" : "Switch to this version at the same time");
                    vx += w + Theme.F(8);
                }
            }

            // lyrics
            lyricsArea = wide ? new RectangleF(pad + colW + Theme.F(70), Theme.F(70), Width - pad - colW - Theme.F(70) - pad, H - Theme.F(80))
                              : new RectangleF(pad, ty + Theme.F(70), Width - pad * 2, H - ty - Theme.F(80));
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
            pane.Glow = Theme.Mix(Theme.AccentTarget, Color.White, 0.4f);
            pane.Draw(g, lyricsArea, shell.Player.SmoothPosition, this, sec => shell.Player.Seek(sec));
        }

        public void SoundReady(long id) { if (id == shell.Player.Current?.Id) Invalidate(); }

        // ---------- the song's waveform: click to jump, drag to loop a part ----------
        float dragFrom = -1, dragTo = -1;
        void DrawWave(Graphics g, Track t, RectangleF r)
        {
            var P = shell.Player;
            var snd = shell.Sound.Get(t);
            double dur = P.Duration > 0 ? P.Duration : t.Duration;
            // a line above: tempo, key, loop buttons
            float ly = r.Y;
            string info = snd == null ? (shell.Sound.Has(t.Id) ? "AURA couldn't read this song's sound" : "AURA is listening to this song…")
                : (snd.Bpm > 0 ? snd.Bpm.ToString("0") + " BPM" : "No steady beat") + (snd.Key.Length > 0 ? "  ·  " + snd.Key + " (" + snd.Camelot + ")" : "");
            Theme.Write(g, info, Theme.Font(12.5f), Theme.Sub, new RectangleF(r.X, ly, r.Width * 0.5f, Theme.F(22)));
            float bx = r.Right;
            string loopTip = P.Looping ? "Looping " + Theme.Time(P.LoopA) + " – " + Theme.Time(P.LoopB) : "Drag across the waveform to loop a part";
            if (P.Looping)
            {
                bx -= PillWidth(g, "Stop looping", "close"); Pill(g, "loopoff", bx, ly - Theme.F(3), "Stop looping", P.ClearLoop, true, "close", 26, loopTip);
                bx -= Theme.F(8);
            }
            bx -= PillWidth(g, "B");
            Pill(g, "loopb", bx, ly - Theme.F(3), "B", () => P.SetLoop(P.LoopA >= 0 ? P.LoopA : 0, P.Position), P.LoopB >= 0, null, 26, "Loop ends here (" + Theme.Time(P.Position) + ")");
            bx -= Theme.F(6);
            bx -= PillWidth(g, "A");
            Pill(g, "loopa", bx, ly - Theme.F(3), "A", () => P.SetLoop(P.Position, P.LoopB > P.Position ? P.LoopB : Math.Min(dur, P.Position + 8)), P.LoopA >= 0, null, 26, "Loop starts here (" + Theme.Time(P.Position) + ")");
            Icons.Draw(g, "loop", new RectangleF(bx - Theme.F(26), ly, Theme.F(18), Theme.F(18)), P.Looping ? Theme.AccentText : Theme.Faint);
            // the wave
            var wr = new RectangleF(r.X, ly + Theme.F(28), r.Width, r.Height - Theme.F(28));
            double pos = P.SmoothPosition;
            float k = dur > 0 ? (float)(pos / dur) : 0;
            if (P.Looping && dur > 0)
            {
                float a = wr.X + (float)(P.LoopA / dur) * wr.Width, b = wr.X + (float)(P.LoopB / dur) * wr.Width;
                Theme.Fill(g, Theme.Alpha(Theme.Accent, 50), new RectangleF(a, wr.Y - Theme.F(2), b - a, wr.Height + Theme.F(4)), Theme.F(4));
            }
            if (dragFrom >= 0 && dragTo >= 0 && Math.Abs(dragTo - dragFrom) > 0.004f)
            {
                float a = wr.X + Math.Min(dragFrom, dragTo) * wr.Width, b = wr.X + Math.Max(dragFrom, dragTo) * wr.Width;
                Theme.Fill(g, Theme.Alpha(Color.White, 40), new RectangleF(a, wr.Y - Theme.F(2), b - a, wr.Height + Theme.F(4)), Theme.F(4));
            }
            float bar = Theme.F(3), gap = Theme.F(2);
            int n = Math.Max(10, (int)(wr.Width / (bar + gap)));
            var peaks = snd?.Peaks;
            float mid = wr.Y + wr.Height / 2;
            using (var played = new SolidBrush(Theme.Mix(Theme.Accent, Color.White, 0.15f)))
            using (var rest = new SolidBrush(Theme.Alpha(Theme.Text, 70)))
                for (int i = 0; i < n; i++)
                {
                    float v;
                    if (peaks != null && peaks.Length > 0)
                    {
                        // the loudest point in this bar's part of the song (peaks cover the length that was heard)
                        double coverage = snd.Length > 0 && dur > 0 ? Math.Min(1, (peaks.Length / SongSound.Fps) / dur) : 1;
                        int from = (int)((double)i / n / coverage * peaks.Length), to = (int)((double)(i + 1) / n / coverage * peaks.Length);
                        int mx = 0; for (int j = from; j < Math.Max(from + 1, to) && j < peaks.Length; j++) if (peaks[j] > mx) mx = peaks[j];
                        v = mx / 255f;
                    }
                    else v = 0.12f;
                    float hgt = Math.Max(Theme.F(2), v * wr.Height);
                    float x = wr.X + i * (bar + gap);
                    g.FillRectangle((float)i / n < k ? played : rest, x, mid - hgt / 2, bar, hgt);
                }
            // where you are
            using (var pen = new Pen(Theme.Text, Theme.F(1.5f))) g.DrawLine(pen, wr.X + k * wr.Width, wr.Y - Theme.F(3), wr.X + k * wr.Width, wr.Bottom + Theme.F(3));
            var hit = AddHit("wave", wr, null, loopTip);
            hit.Cursor = Cursors.Hand;
            hit.DragTo = p => { float f = Math.Max(0, Math.Min(1, (p.X - wr.X) / wr.Width)); if (dragFrom < 0) dragFrom = f; dragTo = f; Invalidate(); };
            hit.DragEnd = () =>
            {
                if (dragFrom >= 0 && dur > 0)
                {
                    if (Math.Abs(dragTo - dragFrom) * wr.Width < Theme.F(6)) P.Seek(dragTo * dur);
                    else P.SetLoop(Math.Min(dragFrom, dragTo) * dur, Math.Max(dragFrom, dragTo) * dur);
                }
                dragFrom = dragTo = -1;
                Invalidate();
            };
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (lyricsArea.Contains(e.Location)) { pane.Wheel(e.Delta, lyricsArea.Height); Invalidate(); }
        }

        protected override void OnBackButton() => shell.Back();
        protected override void OnForwardButton() => shell.Forward();
    }
}
