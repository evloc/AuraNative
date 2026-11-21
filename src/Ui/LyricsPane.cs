using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Aura
{
    // Draws lyrics that follow the song: the current line lights up, word by word when the song has
    // word timings. Used by Now Playing (Stage = false) and full-screen lyrics (Stage = true).
    sealed class LyricsPane
    {
        public bool Stage;
        Lyrics lyr; long forId = -1;
        float scroll = float.NaN, manual; DateTime manualUntil = DateTime.MinValue;
        float layoutW = -1; bool layoutStage;
        List<Line> lay;
        float total;

        sealed class Word { public string Text; public float X, W; public int Timed = -1; }
        sealed class Row { public List<Word> Words = new List<Word>(); public float Y, Width; }
        sealed class Line { public List<Row> Rows = new List<Row>(); public float Top, Height; public double T = -1; public LyricLine Src; }

        public Lyrics Lyrics => lyr;

        public void Set(Lyrics l, long id)
        {
            if (id == forId && ReferenceEquals(l, lyr)) return;
            lyr = l; forId = id; lay = null; scroll = float.NaN; manualUntil = DateTime.MinValue; manual = 0;
        }

        Font F => Stage ? Theme.Font(44, FontStyle.Bold) : Theme.Font(27, FontStyle.Bold);
        bool Timed => lyr != null && lyr.Timed;

        void Layout(Graphics g, float width)
        {
            lay = new List<Line>();
            var f = F;
            float rowH = f.GetHeight(g) * 1.12f, gap = Theme.F(Stage ? 22 : 16), space = Theme.Measure(g, "a a", f) - Theme.Measure(g, "aa", f);
            float y = 0;
            var src = Timed ? lyr.Lines : (lyr?.Plain ?? "").Split('\n').Select(x => new LyricLine { T = -1, Text = x.Trim() }).ToList();
            foreach (var ln in src)
            {
                var L = new Line { Top = y, T = Timed ? ln.T : -1, Src = ln };
                List<(string w, int timed)> words;
                if (ln.Words != null) words = ln.Words.Select((x, i) => (x.w, i)).ToList();
                else words = ln.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(x => (x, -1)).ToList();
                if (words.Count == 0)
                {
                    // an instrumental gap in timed lyrics shows a note; a blank line in plain lyrics is a small space
                    if (Timed) { var r = new Row { Y = 0 }; r.Words.Add(new Word { Text = "♪", X = 0, W = Theme.Measure(g, "♪", f) }); r.Width = r.Words[0].W; L.Rows.Add(r); L.Height = rowH; }
                    else L.Height = rowH * 0.45f;
                }
                else
                {
                    var row = new Row { Y = 0 }; float x = 0;
                    foreach (var (w, ti) in words)
                    {
                        float ww = Theme.Measure(g, w, f);
                        if (x > 0 && x + ww > width) { row.Width = x - space; L.Rows.Add(row); row = new Row { Y = L.Rows.Count * rowH }; x = 0; }
                        row.Words.Add(new Word { Text = w, X = x, W = ww, Timed = ti });
                        x += ww + space;
                    }
                    row.Width = x - space; L.Rows.Add(row);
                    L.Height = L.Rows.Count * rowH;
                }
                lay.Add(L);
                y += L.Height + gap;
            }
            total = y;
            layoutW = width; layoutStage = Stage;
        }

        public int Current(double pos)
        {
            if (!Timed || lay == null) return -1;
            int cur = -1;
            for (int i = 0; i < lay.Count; i++) { if (lay[i].T <= pos + 0.12) cur = i; else break; }
            return cur;
        }

        public void Wheel(int delta, float areaH)
        {
            if (lay == null) return;
            float from = DateTime.Now < manualUntil ? manual : (float.IsNaN(scroll) ? 0 : scroll);
            manual = Math.Max(-areaH * 0.4f, Math.Min(Math.Max(0, total - areaH * 0.5f), from - delta / 120f * Theme.F(110)));
            manualUntil = Timed ? DateTime.Now.AddSeconds(4) : DateTime.MaxValue;
        }

        // pos = where the song is; seek = called with a time when a line is clicked
        public void Draw(Graphics g, RectangleF area, double pos, Canvas host, Action<double> seek, string idPrefix = "ly")
        {
            if (lyr == null) return;
            if (lay == null || Math.Abs(layoutW - area.Width) > 0.5f || layoutStage != Stage) Layout(g, area.Width);
            int cur = Current(pos);
            float anchor = area.Height * (Stage ? 0.42f : 0.32f);
            float target;
            if (DateTime.Now < manualUntil) target = manual;
            else if (Timed) target = cur >= 0 ? lay[cur].Top + lay[cur].Height / 2 - anchor : -anchor + (lay.Count > 0 ? lay[0].Height / 2 : 0);
            else target = 0;
            if (float.IsNaN(scroll)) scroll = target;
            else scroll += (target - scroll) * 0.16f;
            if (Math.Abs(target - scroll) < 0.3f) scroll = target;

            var st = g.Save();
            g.SetClip(area);
            var f = F;
            for (int i = 0; i < lay.Count; i++)
            {
                var L = lay[i];
                float top = area.Y + L.Top - scroll;
                if (top > area.Bottom || top + L.Height < area.Y) continue;
                // how bright: the current line is white, sung lines a bit dimmer, lines to come dimmer still
                int alpha;
                if (!Timed) alpha = 225;
                else if (i == cur) alpha = 255;
                else alpha = i < cur ? 135 : 85;
                if (Stage && Timed && cur >= 0) alpha = (int)(alpha * Math.Max(0.25, 1 - Math.Abs(i - cur) * 0.16));
                bool hot = host != null && Timed && host.HoverIdIs(idPrefix + ":" + i);
                if (hot && i != cur) alpha = Math.Min(255, alpha + 70);
                var lineRect = new RectangleF(area.X - Theme.F(10), top - Theme.F(4), area.Width + Theme.F(20), L.Height + Theme.F(8));
                if (hot) Theme.Fill(g, Theme.Alpha(Color.White, 14), lineRect, Theme.F(10));
                foreach (var row in L.Rows)
                {
                    float rx = Stage ? area.X + (area.Width - row.Width) / 2 : area.X;
                    float ry = top + row.Y;
                    foreach (var w in row.Words)
                    {
                        var wr = new RectangleF(rx + w.X, ry, w.W + Theme.F(12), f.GetHeight(g) * 1.12f);
                        if (Stage) Theme.Write(g, w.Text, f, Theme.Alpha(Color.Black, alpha / 3), new RectangleF(wr.X + Theme.F(2), wr.Y + Theme.F(3), wr.Width, wr.Height), Theme.TopLeft);
                        if (i == cur && L.Src.Words != null && w.Timed >= 0)
                        {
                            // word by word: dim, then the sung part filled in
                            double k = WordProgress(L.Src, w.Timed, pos, i + 1 < lay.Count ? lay[i + 1].T : -1);
                            Theme.Write(g, w.Text, f, Theme.Alpha(Color.White, 95), wr, Theme.TopLeft);
                            if (k > 0)
                            {
                                var s2 = g.Save();
                                g.SetClip(new RectangleF(wr.X, wr.Y, (float)(w.W * k) + (k >= 1 ? Theme.F(12) : 0), wr.Height), System.Drawing.Drawing2D.CombineMode.Intersect);
                                Theme.Write(g, w.Text, f, Color.White, wr, Theme.TopLeft);
                                g.Restore(s2);
                            }
                        }
                        else Theme.Write(g, w.Text, f, Theme.Alpha(Color.White, alpha), wr, Theme.TopLeft);
                    }
                }
                if (Timed && host != null && seek != null && lineRect.Bottom > area.Y && lineRect.Top < area.Bottom)
                {
                    double t = L.T;
                    host.AddHit(idPrefix + ":" + i, RectangleF.Intersect(lineRect, area), () => seek(t), "Play from here");
                }
            }
            g.Restore(st);
        }

        static double WordProgress(LyricLine line, int i, double pos, double nextLine)
        {
            var ws = line.Words;
            double start = ws[i].t;
            double end = i + 1 < ws.Count ? ws[i + 1].t : line.End > 0 ? line.End : nextLine > 0 ? Math.Min(nextLine, start + 1.2) : start + 0.8;
            if (end <= start) end = start + 0.25;
            end = Math.Min(end, start + 2.5);          // a long pause after a word isn't part of it
            return Math.Max(0, Math.Min(1, (pos - start) / (end - start)));
        }
    }
}
