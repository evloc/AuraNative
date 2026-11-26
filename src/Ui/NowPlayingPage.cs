using System;
using System.Collections.Generic;
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
        RectangleF lyricsArea, waveStrip;
        Track shown;

        public NowPlayingPage(IShell shell)
        {
            this.shell = shell;
            Scrollable = false;
            tick.Tick += (s, e) =>
            {
                minimized = FindForm()?.WindowState == FormWindowState.Minimized;
                // with the graphics-card lyrics, the page itself only needs the waveform moving now and then
                tick.Interval = UsingHost && shell.Player.IsPlaying ? 120 : 250;
                // with the graphics-card lyrics only the waveform strip moves: repaint just that, not the whole page
                if (UsingHost && shell.Player.IsPlaying && waveStrip.Height > 0) Invalidate(Rectangle.Round(waveStrip));
                else if (!shell.Player.IsPlaying || UsingHost) Invalidate();
            };
            tick.Start();
            frames = new FrameClock(this, () => Visible && shell.Player.IsPlaying && FullLyricsForm.Open == 0 && !Minimized && !UsingHost);
#if WPF
            if (Aura.Wpf.WpfUi.Available)
            {
                try
                {
                    // one lyrics view for the whole app, moved into each Now Playing page: making a new one for every visit
                    // leaks memory (a WPF window inside a WinForms one is never quite freed)
                    if (sharedHost == null || sharedHost.IsDisposed) sharedHost = new Aura.Wpf.LyricsHost();
                    host = sharedHost;
                    host.Visible = false;
                    host.Position = () => shell.Player.SmoothPosition;
                    host.View.Seek = sec => shell.Player.Seek(sec);
                    Controls.Add(host);
                }
                catch (Exception e) { Aura.Wpf.WpfUi.Failed(e); host = null; }
            }
#endif
            Reload();
        }

        bool minimized;
        bool Minimized => minimized;
#if WPF
        Aura.Wpf.LyricsHost host;
        Rectangle hostArea; Bitmap hostBg;
        static Aura.Wpf.LyricsHost sharedHost;
        bool UsingHost => host != null && host.Visible;
        // the lyrics drawn by the graphics card, placed over the lyrics area
        bool HostLyrics(Lyrics l, Track t)
        {
            if (host == null) return false;
            try
            {
                var area = Rectangle.Round(lyricsArea);
                host.View.Stage = false;
                host.View.Glow = Aura.Wpf.LyricsView.C(Theme.Mix(Theme.AccentTarget, Color.White, 0.4f));
                host.View.Set(l, t.Id);
                if (area != hostArea || !ReferenceEquals(hostBg, backdrop))
                {
                    hostArea = area; hostBg = backdrop;
                    host.SetBounds(area.X, area.Y, area.Width, area.Height);
                    if (backdrop != null) host.SetBackground(backdrop, area); else host.SetSolid(Theme.Bg);
                }
                if (!host.Visible) { host.Visible = true; host.StepOnce(); }
                bool run = shell.Player.IsPlaying && !Minimized && FullLyricsForm.Open == 0 && !Aura.Wpf.WpfUi.StageOpen;
                host.Run(run);
                if (!run) host.StepOnce();
                return true;
            }
            catch (Exception e) { Aura.Wpf.WpfUi.Failed(e); try { host.Visible = false; } catch { } host = null; return false; }
        }
        void HideHost() { if (host != null && host.Visible) { host.Run(false); host.Visible = false; } }
#else
        bool UsingHost => false;
        bool HostLyrics(Lyrics l, Track t) => false;
        void HideHost() { }
#endif

        public void Reload()
        {
            shown = shell.Player.Current;
            pane.Set(shown == null ? null : shell.Lyrics.Get(shown), shown?.Id ?? -1);
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
#if WPF
                // hand the shared lyrics view back instead of letting it be thrown away with the page
                if (host != null) { try { host.Run(false); host.Visible = false; Controls.Remove(host); } catch { } host = null; }
#endif
                tick.Dispose(); frames.Dispose(); pane.Dispose(); backdrop?.Dispose();
            }
            base.Dispose(disposing);
        }

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

        protected override void OnPaint(System.Windows.Forms.PaintEventArgs e)
        {
            using (Theme.Dark()) base.OnPaint(e);
        }

        protected override void Draw(Graphics g, RectangleF view)
        {
            var t = shell.Player.Current;
            if (t != shown) Reload();
            if (t == null)
            {
                HideHost();
                Theme.Write(g, "Nothing playing", Theme.Font(30, FontStyle.Bold), Theme.Text, new RectangleF(Theme.F(40), Theme.F(40), Width, Theme.F(44)));
                Theme.Write(g, "Pick a song, or shuffle everything.", Theme.Font(15), Theme.Sub, new RectangleF(Theme.F(40), Theme.F(92), Width, Theme.F(26)));
                if (shell.Lib.Tracks.Count > 0) Pill(g, "shuf", Theme.F(40), Theme.F(136), "Shuffle everything", () => shell.Player.ShuffleAll(shell.Lib.Tracks.ConvertAll(x => x.Id)), true, "shuffle", 42);
                return;
            }
            DrawBackdrop(g, t);
            float strip = Theme.F(104);
            waveStrip = new RectangleF(0, Height - strip - Theme.F(30), Width, strip + Theme.F(30));
            DrawWave(g, t, new RectangleF(Theme.F(44), Height - strip + Theme.F(14), Width - Theme.F(88), strip - Theme.F(30)));
            float H = Height - strip;
            bool wide = Width >= Theme.F(860);
            float pad = Theme.F(44);
            float colW = wide ? Math.Min(Theme.F(400), Width * 0.36f) : Math.Min(Theme.F(170), Width * 0.3f);
            bool hasVersions = shell.Lib.VersionsOf(t).Count > 1;
            float infoH = Theme.F(196) + (hasVersions ? Theme.F(84) : 0);
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
            float aw = Math.Min(tw, Theme.Measure(g, t.ArtistName, af) * 1.12f + Theme.F(12));
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
            // rating: click a star (click the same one again to clear)
            ty += Theme.F(26);
            for (int i = 1; i <= 5; i++)
            {
                int n = i;
                var sr = new RectangleF(tx + (i - 1) * Theme.F(22), ty, Theme.F(20), Theme.F(20));
                bool lit = (hoverStar > 0 ? hoverStar : t.Rating) >= i;
                Icons.Draw(g, "star", sr, lit ? Theme.AccentText : Theme.Faint, lit);
                var hs = AddHit("star:" + i, RectangleF.Inflate(sr, 1, 2), () => shell.Lib.SetRating(new[] { t }, t.Rating == n ? 0 : n), n + (n == 1 ? " star" : " stars") + " (keys 1–5)");
            }
            hoverStar = 0;
            for (int i = 1; i <= 5; i++) if (Hover("star:" + i)) hoverStar = i;
            // the era, and notes from a fan tracker (producer, dates)
            var extra = string.Join("  ·  ", new[] { t.Era, shell.Lib.InfoOf(t) }.Where(x => !string.IsNullOrEmpty(x)));
            if (extra.Length > 0)
            {
                var er = new RectangleF(tx + Theme.F(124), ty, tw - Theme.F(124), Theme.F(22));
                Theme.Write(g, extra, Theme.Font(12.5f), Theme.Faint, er);
                var era0 = t.Era;
                AddHit("eraline", er, era0.Length > 0 ? () => shell.Go(new Route("era", era0)) : (Action)null, extra.Length > 60 ? extra : null);
            }
            ty += Theme.F(40);
            float bx = tx;
            IconButton(g, "fav", new RectangleF(bx, ty, Theme.F(38), Theme.F(38)), "heart", () => shell.Lib.SetFav(t, !t.Fav), t.Fav ? "Remove from Favorites" : "Add to Favorites", t.Fav, filled: t.Fav, circleBg: true, dot: false);
            bx += Theme.F(48);
            var l = pane.Lyrics;
            if (l != null) bx = Pill(g, "full", bx, ty + Theme.F(1), "Full screen", shell.FullScreenLyrics, false, "expand", 36, "Full-screen lyrics (F11)").Right + Theme.F(8);
            bx = Pill(g, "find", bx, ty + Theme.F(1), l == null ? "Find lyrics" : "Other lyrics", () => shell.FindLyrics(t), false, "search", 36).Right + Theme.F(8);
            if (l == null || !l.ByWord)
            {
                IconButton(g, "sync", new RectangleF(bx, ty, Theme.F(38), Theme.F(38)), "wave", () => shell.Transcribe(t), l == null ? "Write the lyrics down by listening" : "Time these lyrics to the song (auto-sync)", circleBg: true);
                bx += Theme.F(46);
            }
            var P2 = shell.Player;
            string sp = P2.Rate.ToString("0.##") + "×";
            Pill(g, "speed", bx, ty + Theme.F(1), sp, () => (shell as MainForm)?.SpeedMenu(PointToScreen(Point.Round(new PointF(bx, ty + Theme.F(40) - Offset)))), Math.Abs(P2.Rate - 1) > 0.01, null, 36, "Playback speed");

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
            // two tabs above the right side: the lyrics, or everything about the song
            {
                var tf = Theme.Font(12, FontStyle.Regular, true);
                float lw = Theme.Measure(g, "LYRICS", tf) + Theme.F(4), aw2 = Theme.Measure(g, "ABOUT", tf) + Theme.F(4);
                RectangleF lr = new RectangleF(lyricsArea.X, hy, lw, Theme.F(22)), abr = new RectangleF(lyricsArea.X + lw + Theme.F(18), hy, aw2, Theme.F(22));
                Theme.Write(g, "LYRICS", tf, !ShowAbout ? Theme.Text : Hover("tab:l") ? Theme.Sub : Theme.Faint, lr);
                Theme.Write(g, "ABOUT", tf, ShowAbout ? Theme.Text : Hover("tab:a") ? Theme.Sub : Theme.Faint, abr);
                var ul = ShowAbout ? abr : lr;
                Theme.Fill(g, Theme.AccentText, new RectangleF(ul.X, ul.Bottom + Theme.F(1), ul.Width - Theme.F(4), Theme.F(2)), Theme.F(1));
                AddHit("tab:l", RectangleF.Inflate(lr, 4, 4), () => { ShowAbout = false; Invalidate(); }, "The lyrics");
                AddHit("tab:a", RectangleF.Inflate(abr, 4, 4), () => { ShowAbout = true; aboutScroll = 0; Invalidate(); }, "Who's on it, who made it, the album and your plays");
                hy += 0;
            }
            if (ShowAbout) { HideHost(); DrawAbout(g, t, new RectangleF(lyricsArea.X, lyricsArea.Y - Theme.F(6), lyricsArea.Width, H - lyricsArea.Y - Theme.F(4))); return; }
            float tabsW = Theme.F(150);
            if (l == null)
            {
                HideHost();
                Theme.Write(g, "No lyrics for this song yet", Theme.Font(22, FontStyle.Bold), Theme.Text, new RectangleF(lyricsArea.X, lyricsArea.Y + Theme.F(20), lyricsArea.Width, Theme.F(32)));
                string why = t.Artist.Trim().Length == 0 ? "Give it an artist tag and AURA can look for them online. Or press Find lyrics to search yourself."
                    : shell.Prefs.AutoLyrics ? "AURA looks online by itself for songs without lyrics. Unreleased songs often aren't online anywhere — press Find lyrics to search yourself."
                    : "Press Find lyrics to look online (LRCLIB and Genius).";
                Theme.Write(g, why, Theme.Font(14.5f), Theme.Sub, new RectangleF(lyricsArea.X, lyricsArea.Y + Theme.F(60), Math.Min(lyricsArea.Width, Theme.F(520)), Theme.F(80)), Theme.Wrap);
                Pill(g, "find2", lyricsArea.X, lyricsArea.Y + Theme.F(150), "Find lyrics", () => shell.FindLyrics(t), true, "search", 40);
                return;
            }
            string note = l.Source + (l.ByWord ? "  ·  word by word" : l.Timed ? "  ·  timed" : "  ·  not timed");
            Theme.Write(g, note, Theme.Font(12.5f), Theme.Faint, new RectangleF(lyricsArea.X + tabsW, hy, lyricsArea.Width - tabsW - Theme.F(130), Theme.F(22)));
            if (l.FromAura)
            {
                var f = Theme.Font(12.5f);
                float w = Theme.Measure(g, "Remove these", f) + Theme.F(8);
                var rr = new RectangleF(lyricsArea.Right - w, hy, w, Theme.F(22));
                Theme.Write(g, "Remove these", f, Hover("rm") ? Theme.Text : Theme.Faint, rr, Theme.Right);
                AddHit("rm", rr, () => { shell.Lyrics.Remove(t); Reload(); }, "Wrong song? Remove the lyrics AURA found");
            }
            if (HostLyrics(l, t)) return;
            pane.Stage = false;
            pane.Glow = Theme.Mix(Theme.AccentTarget, Color.White, 0.4f);
            pane.Draw(g, lyricsArea, shell.Player.SmoothPosition, this, sec => shell.Player.Seek(sec));
        }

        public void SoundReady(long id) { if (id == shell.Player.Current?.Id) Invalidate(); }

        // ---------- the song's waveform: click to jump, drag to loop a part ----------
        float dragFrom = -1, dragTo = -1;
        int hoverStar;
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
            bx -= Theme.F(36);
            bx -= PillWidth(g, "Bookmark", "bookmark");
            Pill(g, "addbm", bx, ly - Theme.F(3), "Bookmark", () => { shell.Lib.AddBookmark(t, P.Position); shell.Toast("Bookmarked " + Theme.Time(P.Position)); Invalidate(); }, false, "bookmark", 26, "Mark this spot (B)");
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
                Theme.Fill(g, Theme.Alpha(Theme.Ink, 40), new RectangleF(a, wr.Y - Theme.F(2), b - a, wr.Height + Theme.F(4)), Theme.F(4));
            }
            float bar = Theme.F(3), gap = Theme.F(2);
            int n = Math.Max(10, (int)(wr.Width / (bar + gap)));
            var peaks = snd?.Peaks;
            float mid = wr.Y + wr.Height / 2;
            var smooth = g.SmoothingMode; g.SmoothingMode = SmoothingMode.None;     // plain bars draw much faster
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
            g.SmoothingMode = smooth;
            // where you are
            using (var pen = new Pen(Theme.Text, Theme.F(1.5f))) g.DrawLine(pen, wr.X + k * wr.Width, wr.Y - Theme.F(3), wr.X + k * wr.Width, wr.Bottom + Theme.F(3));
            // bookmarks: little flags above the wave (click to jump, right-click to remove)
            if (dur > 0)
                foreach (var bm in shell.Lib.BookmarksOf(t.Id))
                {
                    float mx = wr.X + (float)(bm.At / dur) * wr.Width;
                    var mr = new RectangleF(mx - Theme.F(7), wr.Y - Theme.F(20), Theme.F(14), Theme.F(16));
                    Icons.Draw(g, "bookmark", mr, Hover("bm:" + bm.Id) ? Theme.Text : Theme.AccentText, true);
                    var bb = bm;
                    var bh = AddHit("bm:" + bm.Id, RectangleF.Inflate(mr, 3, 3), () => P.Seek(bb.At), "Bookmark at " + Theme.Time(bm.At) + " — click to jump, right-click to remove");
                    bh.Right = pt => { shell.Lib.RemoveBookmark(bb.Id); Invalidate(); };
                }
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
            if (ShowAbout && aboutArea.Contains(e.Location)) { aboutScroll = Math.Max(0, Math.Min(aboutMax, aboutScroll - e.Delta / 120f * Theme.F(60))); Invalidate(); return; }
            if (lyricsArea.Contains(e.Location)) { pane.Wheel(e.Delta, lyricsArea.Height); Invalidate(); }
        }

        // ---------- About: who's on it, who made it, the album or era, and your plays ----------
        public static bool ShowAbout;
        float aboutScroll, aboutMax; RectangleF aboutArea;

        void Card(Graphics g, RectangleF r, string title)
        {
            using (var b = new LinearGradientBrush(r, Theme.Alpha(Theme.Ink, 26), Theme.Alpha(Theme.Ink, 10), 90f))
            using (var path = Theme.Round(r, Theme.F(16))) g.FillPath(b, path);
            Theme.Stroke(g, Theme.Alpha(Theme.Ink, 34), r, Theme.F(16));
            if (title != null) Theme.Write(g, title, Theme.Font(15, FontStyle.Bold), Theme.Text, new RectangleF(r.X + Theme.F(20), r.Y + Theme.F(16), r.Width - Theme.F(40), Theme.F(24)));
        }

        void DrawAbout(Graphics g, Track t, RectangleF area)
        {
            aboutArea = area;
            var info = SongAbout.Get(t, shell.Lib, () => { try { BeginInvoke((Action)Invalidate); } catch { } });
            var clip = g.Clip; g.SetClip(area);
            try
            {
                float gap = Theme.F(14);
                bool two = area.Width >= Theme.F(640);
                float colW = two ? (area.Width - gap) / 2 : area.Width;
                float[] y = { area.Y - aboutScroll, area.Y - aboutScroll };
                float X(int c) => area.X + c * (colW + gap);
                int Col() => two && y[1] < y[0] ? 1 : 0;
                float pad = Theme.F(20);

                // artists
                if (info != null && info.Artists.Count > 0)
                {
                    int c = Col(); float h = Theme.F(56) + info.Artists.Count * Theme.F(58);
                    var r = new RectangleF(X(c), y[c], colW, h); Card(g, r, info.Artists.Count == 1 ? "Artist" : "Artists");
                    float ay = r.Y + Theme.F(54);
                    foreach (var p in info.Artists)
                    {
                        var av = new RectangleF(r.X + pad, ay, Theme.F(44), Theme.F(44));
                        var img = p.Pic != null ? Art.Get(p.Pic) : null;
                        if (img != null) Theme.Image(g, img, av, av.Width / 2, true);
                        else
                        {
                            var hue = Theme.FromHsl(Math.Abs(p.Name.GetHashCode() % 360), 0.6f, 0.55f);
                            using (var b = new LinearGradientBrush(av, hue, Theme.Mix(hue, Color.Black, 0.3f), 45f)) g.FillEllipse(b, av);
                            Theme.Write(g, p.Name.Substring(0, 1).ToUpperInvariant(), Theme.Font(17, FontStyle.Bold), Color.White, av, Theme.Center);
                        }
                        string id = "art:" + p.Name;
                        Theme.Write(g, p.Name, Theme.Font(14.5f, FontStyle.Bold), Hover(id) ? Theme.AccentText : Theme.Text, new RectangleF(av.Right + Theme.F(14), ay + Theme.F(2), r.Width - Theme.F(110), Theme.F(22)));
                        Theme.Write(g, p.Role, Theme.Font(12.5f), Theme.Faint, new RectangleF(av.Right + Theme.F(14), ay + Theme.F(23), r.Width - Theme.F(110), Theme.F(20)));
                        if (p.Key != null) { var k = p.Key; AddHit(id, new RectangleF(r.X + Theme.F(8), ay - Theme.F(4), r.Width - Theme.F(16), Theme.F(52)), () => shell.Go(new Route("artist", k)), "Open " + p.Name); }
                        ay += Theme.F(58);
                    }
                    y[c] += h + gap;
                }

                // album and era
                Action<string, string, string, Track, Action> group = (kind, name, sub, cover, open) =>
                {
                    int c = Col(); float h = Theme.F(112);
                    var r = new RectangleF(X(c), y[c], colW, h); Card(g, r, null);
                    var cr = new RectangleF(r.X + Theme.F(16), r.Y + Theme.F(16), Theme.F(80), Theme.F(80));
                    var img = cover != null ? Art.Get(cover) : null;
                    if (img != null) Theme.Image(g, img, cr, Theme.F(10)); else Theme.NoArt(g, cr, Theme.F(10), cover?.Id ?? 0);
                    float tx = cr.Right + Theme.F(16), tw = r.Right - tx - Theme.F(16);
                    string id = "grp:" + kind;
                    Theme.Write(g, kind.ToUpperInvariant(), Theme.Font(11, FontStyle.Regular, true), Theme.Faint, new RectangleF(tx, cr.Y + Theme.F(4), tw, Theme.F(18)));
                    Theme.Write(g, name.ToUpperInvariant(), Theme.Font(16, FontStyle.Bold), Hover(id) ? Theme.AccentText : Theme.Text, new RectangleF(tx, cr.Y + Theme.F(24), tw, Theme.F(26)));
                    Theme.Write(g, sub, Theme.Font(12.5f), Theme.Sub, new RectangleF(tx, cr.Y + Theme.F(52), tw, Theme.F(20)));
                    AddHit(id, r, open, "Open " + name);
                    y[c] += h + gap;
                };
                if (t.Album.Length > 0)
                {
                    var al = shell.Lib.Tracks.Where(x => x.AlbumKey == t.AlbumKey).ToList();
                    int plays = al.Sum(x => x.Plays);
                    var years = al.Select(x => x.Year).Where(x => x.Length >= 4).Select(x => x.Substring(0, 4)).Distinct().OrderBy(x => x).ToList();
                    string when = years.Count == 0 ? "" : years.Count == 1 ? years[0] : years[0] + " – " + years[years.Count - 1];
                    group("Album", t.Album, string.Join("  ·  ", new[] { when, Theme.Count(al.Count, "song"), Theme.Count(plays, "play") }.Where(x => x.Length > 0)),
                        al.OrderByDescending(x => x.HasArt).ThenByDescending(x => x.Plays).FirstOrDefault() ?? t, () => shell.Go(new Route("album", t.AlbumKey)));
                }
                if (t.Era.Length > 0 && !t.Era.Equals(t.Album, StringComparison.OrdinalIgnoreCase))
                {
                    var er = shell.Lib.Tracks.Where(x => x.Era.Equals(t.Era, StringComparison.CurrentCultureIgnoreCase)).ToList();
                    var era = t.Era;
                    group("Era", era, Theme.Count(er.Count, "song") + "  ·  " + Theme.Count(er.Sum(x => x.Plays), "play"),
                        er.OrderByDescending(x => x.Plays).FirstOrDefault(x => x.HasArt) ?? t, () => shell.Go(new Route("era", era)));
                }

                // credits
                {
                    int c = Col();
                    var lines = info?.Credits ?? new List<KeyValuePair<string, string>>();
                    float w = colW - pad * 2;
                    var vf = Theme.Font(14, FontStyle.Bold);
                    var heights = lines.Select(kv => Math.Min(Theme.F(66), Math.Max(Theme.F(22), g.MeasureString(kv.Value, vf, (int)w).Height + Theme.F(2)))).ToList();
                    float h = Theme.F(56) + (info == null ? Theme.F(30) : heights.Sum() + lines.Count * Theme.F(26f)) + Theme.F(4);
                    var r = new RectangleF(X(c), y[c], colW, h); Card(g, r, "Credits");
                    float cy = r.Y + Theme.F(52);
                    if (info == null) Theme.Write(g, "Reading the song…", Theme.Font(13), Theme.Faint, new RectangleF(r.X + pad, cy, w, Theme.F(22)));
                    for (int i = 0; i < lines.Count; i++)
                    {
                        Theme.Write(g, lines[i].Key, Theme.Font(12.5f), Theme.Faint, new RectangleF(r.X + pad, cy, w, Theme.F(20)));
                        cy += Theme.F(22);
                        Theme.Write(g, lines[i].Value, vf, Theme.Text, new RectangleF(r.X + pad, cy, w, heights[i]), Theme.Wrap);
                        cy += heights[i] + Theme.F(4);
                    }
                    y[c] += h + gap;
                }

                // you and this song
                {
                    int c = Col();
                    var rows = new List<(string k, string v)>
                    {
                        ("Plays", t.Plays == 0 ? "Not played yet" : Theme.Count(t.Plays, "play")),
                        ("Last played", t.LastPlayed > 0 ? Theme.Ago(t.LastPlayed) : "Never"),
                        ("Added", Theme.Ago(t.Added)),
                    };
                    if (t.Rating > 0) rows.Add(("Rating", new string('★', t.Rating) + new string('☆', 5 - t.Rating)));
                    int bms = shell.Lib.BookmarksOf(t.Id).Count; if (bms > 0) rows.Add(("Bookmarks", bms.ToString()));
                    var snd = shell.Sound.Get(t);
                    if (snd != null && snd.Bpm > 0) rows.Add(("Tempo", snd.Bpm.ToString("0") + " BPM" + (snd.Key.Length > 0 ? "  ·  " + snd.Key : "")));
                    float h = Theme.F(56) + rows.Count * Theme.F(30);
                    var r = new RectangleF(X(c), y[c], colW, h); Card(g, r, "You and this song");
                    float ry = r.Y + Theme.F(52);
                    foreach (var (k, v) in rows)
                    {
                        Theme.Write(g, k, Theme.Font(13), Theme.Sub, new RectangleF(r.X + pad, ry, colW / 2, Theme.F(24)));
                        Theme.Write(g, v, Theme.Font(13.5f, FontStyle.Bold), Theme.Text, new RectangleF(r.X + colW / 2 - pad, ry, colW / 2, Theme.F(24)), Theme.Right);
                        ry += Theme.F(30);
                    }
                    y[c] += h + gap;
                }
                aboutMax = Math.Max(0, Math.Max(y[0], y[1]) + aboutScroll - area.Bottom);
                if (aboutScroll > aboutMax) aboutScroll = aboutMax;
            }
            finally { g.Clip = clip; }
        }

        protected override void OnBackButton() => shell.Back();
        protected override void OnForwardButton() => shell.Forward();
    }
}
