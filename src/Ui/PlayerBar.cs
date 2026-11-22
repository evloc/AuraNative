using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Aura
{
    sealed class PlayerBar : Canvas
    {
        readonly IShell shell;
        Player P => shell.Player;
        float seekDrag = -1;       // 0..1 while dragging the seek bar
        int muteFrom = -1;

        public PlayerBar(IShell shell)
        {
            this.shell = shell;
            Scrollable = false;
            BackColor = Theme.Bar;
            Height = Theme.P(92);
        }

        protected override void Draw(Graphics g, RectangleF view)
        {
            // a faint wash of the song's color along the top
            using (var b = new LinearGradientBrush(new RectangleF(0, 0, Width, Height), Theme.Alpha(Theme.Accent, 26), Theme.Alpha(Theme.Accent, 0), 90f))
                { b.WrapMode = WrapMode.TileFlipXY; g.FillRectangle(b, 0, 0, Width, Height); }
            using (var pen = new Pen(Theme.Hair)) g.DrawLine(pen, 0, 0, Width, 0);

            var t = P.Current;
            float h = Height;
            // ---- left: cover, title, artist, heart
            float art = Theme.F(58), x = Theme.F(16), ay = (h - art) / 2;
            var ar = new RectangleF(x, ay, art, art);
            var img = Art.Get(t);
            if (img != null) Theme.Image(g, img, ar, Theme.F(8));
            else Theme.NoArt(g, ar, Theme.F(8), t?.Id ?? 3);
            if (t != null) AddHit("art", ar, () => { if (shell.Current?.Kind == "now") shell.Back(); else shell.Go(new Route("now")); }, "Now playing and lyrics (Ctrl+L)");
            float leftW = Math.Min(Theme.F(330), Width * 0.3f);
            float tx = x + art + Theme.F(14), tw = leftW - (tx - x) - Theme.F(40);
            if (t != null)
            {
                Theme.Write(g, t.Name, Theme.Font(14.5f, FontStyle.Regular, true), Theme.Text, new RectangleF(tx, h / 2 - Theme.F(22), tw, Theme.F(22)));
                bool ah = Hover("artist");
                var arr = new RectangleF(tx, h / 2 + Theme.F(1), Math.Min(tw, Theme.Measure(g, t.ArtistName, Theme.Font(12.5f)) + Theme.F(6)), Theme.F(20));
                using (var f = new Font(Theme.Font(12.5f), ah ? FontStyle.Underline : FontStyle.Regular))
                    Theme.Write(g, t.ArtistName, f, ah ? Theme.Text : Theme.Sub, arr);
                AddHit("artist", arr, () => shell.Go(new Route("artist", t.MainArtist.ToLowerInvariant())));
                IconButton(g, "fav", new RectangleF(x + leftW - Theme.F(34), h / 2 - Theme.F(16), Theme.F(32), Theme.F(32)), "heart",
                    () => shell.Lib.SetFav(t, !t.Fav), t.Fav ? "Remove from Favorites" : "Add to Favorites", t.Fav, filled: t.Fav, dot: false);
            }
            else Theme.Write(g, shell.Lib.Tracks.Count == 0 ? "Add your music to start" : "Pick something to play", Theme.Font(13.5f), Theme.Sub, new RectangleF(tx, 0, tw + Theme.F(30), h));

            // ---- right: up next, volume
            float vw = Theme.F(110), rx = Width - Theme.F(20) - vw;
            var vr = new RectangleF(rx, h / 2 - Theme.F(10), vw, Theme.F(20));
            Slider(g, "vol", vr, P.Volume / 100f, k => { P.Volume = (int)Math.Round(k * 100); muteFrom = -1; Invalidate(); }, null);
            var volIcon = P.Volume == 0 ? "mute" : P.Volume < 50 ? "volume1" : "volume";
            IconButton(g, "mute", new RectangleF(rx - Theme.F(40), h / 2 - Theme.F(16), Theme.F(32), Theme.F(32)), volIcon, ToggleMute, P.Volume == 0 ? "Unmute" : "Mute");
            IconButton(g, "mini", new RectangleF(rx - Theme.F(160), h / 2 - Theme.F(16), Theme.F(32), Theme.F(32)), "mini", shell.ToggleMini, "Mini player — stays on top (Ctrl+M)");
            IconButton(g, "lyrics", new RectangleF(rx - Theme.F(120), h / 2 - Theme.F(16), Theme.F(32), Theme.F(32)), "mic",
                () => { if (shell.Current?.Kind == "now") shell.Back(); else shell.Go(new Route("now")); }, "Lyrics (Ctrl+L)", shell.Current?.Kind == "now");
            IconButton(g, "queue", new RectangleF(rx - Theme.F(80), h / 2 - Theme.F(16), Theme.F(32), Theme.F(32)), "queue",
                () => { if (shell.Current?.Kind == "queue") shell.Back(); else shell.Go(new Route("queue")); }, "Up next", shell.Current?.Kind == "queue");

            // ---- middle: buttons and seek bar
            float cx = Width / 2f;
            float midW = Math.Min(Theme.F(620), Width - leftW * 2 - Theme.F(40));
            midW = Math.Max(Theme.F(260), Math.Min(midW, (rx - Theme.F(170)) - (x + leftW) - Theme.F(24)));
            cx = Math.Max(cx, x + leftW + Theme.F(12) + midW / 2);
            float by = Theme.F(12), bs = Theme.F(34);
            PlayButton(g, "play", new RectangleF(cx - Theme.F(20), by - Theme.F(2), Theme.F(40), Theme.F(40)), P.IsPlaying, P.Toggle, P.IsPlaying ? "Pause (Space)" : "Play (Space)");
            IconButton(g, "prev", new RectangleF(cx - Theme.F(20) - Theme.F(12) - bs, by + Theme.F(1), bs, bs), "prev", P.Prev, "Previous");
            IconButton(g, "next", new RectangleF(cx + Theme.F(20) + Theme.F(12), by + Theme.F(1), bs, bs), "next", P.Next, "Next");
            IconButton(g, "shuffle", new RectangleF(cx - Theme.F(20) - Theme.F(24) - bs * 2, by + Theme.F(1), bs, bs), "shuffle", () => P.SetShuffle(!P.Shuffle), P.Shuffle ? "Shuffle is on" : "Shuffle", P.Shuffle);
            IconButton(g, "repeat", new RectangleF(cx + Theme.F(20) + Theme.F(24) + bs, by + Theme.F(1), bs, bs), P.Repeat == 2 ? "repeat1" : "repeat", P.CycleRepeat,
                P.Repeat == 0 ? "Repeat" : P.Repeat == 1 ? "Repeating everything" : "Repeating this song", P.Repeat > 0);

            double dur = P.Duration, pos = P.Position;
            float k = seekDrag >= 0 ? seekDrag : dur > 0 ? (float)(pos / dur) : 0;
            float tlw = Theme.F(46);
            var sr = new RectangleF(cx - midW / 2 + tlw, Theme.F(58), midW - tlw * 2, Theme.F(16));
            Theme.Write(g, Theme.Time(seekDrag >= 0 ? seekDrag * dur : pos), Theme.Font(11.5f), Theme.Faint, new RectangleF(sr.X - tlw, sr.Y - Theme.F(2), tlw - Theme.F(8), sr.Height + Theme.F(4)), Theme.Right);
            Theme.Write(g, Theme.Time(dur), Theme.Font(11.5f), Theme.Faint, new RectangleF(sr.Right + Theme.F(8), sr.Y - Theme.F(2), tlw, sr.Height + Theme.F(4)));
            Slider(g, "seek", sr, k, v => { if (P.Current != null) { seekDrag = v; Invalidate(); } }, () =>
            {
                if (seekDrag >= 0 && P.Current != null) P.Seek(seekDrag * P.Duration);
                seekDrag = -1;
            }, t == null);
        }

        void ToggleMute()
        {
            if (P.Volume > 0) { muteFrom = P.Volume; P.Volume = 0; }
            else P.Volume = muteFrom > 0 ? muteFrom : 60;
            Invalidate();
        }

        void Slider(Graphics g, string id, RectangleF r, float k, Action<float> set, Action end, bool disabled = false)
        {
            k = Math.Max(0, Math.Min(1, k));
            bool hot = !disabled && (Hover(id) || (id == "seek" && seekDrag >= 0));
            float th = Theme.F(hot ? 5 : 4);
            var track = new RectangleF(r.X, r.Y + (r.Height - th) / 2, r.Width, th);
            Theme.Fill(g, Theme.Alpha(Color.White, 34), track, th / 2);
            var fill = new RectangleF(track.X, track.Y, track.Width * k, th);
            if (fill.Width > 0.5f) Theme.Fill(g, hot ? Theme.Accent : Theme.Mix(Theme.Accent, Color.White, 0.15f), fill, th / 2);
            if (hot)
            {
                float d = Theme.F(13);
                using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, track.X + track.Width * k - d / 2, track.Y + th / 2 - d / 2, d, d);
            }
            if (disabled) return;
            var h = AddHit(id, new RectangleF(r.X - Theme.F(6), r.Y - Theme.F(6), r.Width + Theme.F(12), r.Height + Theme.F(12)));
            h.Cursor = Cursors.Hand;
            h.DragTo = p => set(Math.Max(0, Math.Min(1, (p.X - r.X) / r.Width)));
            h.DragEnd = end;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            // the wheel over the bar changes the volume
            P.Volume += e.Delta > 0 ? 5 : -5;
            Invalidate();
        }
    }
}
