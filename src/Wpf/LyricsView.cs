#if WPF
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using DColor = System.Drawing.Color;

namespace Aura.Wpf
{
    // The lyrics, drawn by the graphics card (WPF) instead of by the processor: text, glow, fading,
    // growing and scrolling are all handled there, so it stays smooth even full screen.
    // Each frame, Step() only moves and fades things that already exist.
    sealed class LyricsView : System.Windows.Controls.Canvas
    {
        public bool Stage;
        public bool LeftAlign;            // full screen, beside the cover: lines start at the left
        public double Duration;
        public Action<double> Seek;
        public Color Glow = Colors.White;
        public double Fade = 1;

        Lyrics lyr; long forId = -1;
        double builtWidth = -1; bool builtStage, builtLeft;
        readonly System.Windows.Controls.Canvas content = new System.Windows.Controls.Canvas();
        readonly TranslateTransform scrollT = new TranslateTransform();
        readonly List<Line> lines = new List<Line>();
        double total, rowH, scroll = double.NaN, manual; DateTime manualUntil = DateTime.MinValue;
        int lastCur = -2;

        sealed class Row { public TextBlock Tb; public double Y, Left, Width; public List<(double x, double w, int timed)> Words = new List<(double, double, int)>(); public LinearGradientBrush Fill; }
        sealed class Line
        {
            public System.Windows.Controls.Canvas Box; public ScaleTransform Scale; public DropShadowEffect GlowFx;
            public List<Row> Rows = new List<Row>(); public double Top, Height, Width, T = -1, Lit; public LyricLine Src; public bool Hot; public bool Filling;
        }

        static readonly Brush White = Frozen(new SolidColorBrush(Colors.White));
        static Brush Frozen(Brush b) { b.Freeze(); return b; }

        public LyricsView()
        {
            ClipToBounds = true;
            content.RenderTransform = scrollT;
            Children.Add(content);
            Background = Brushes.Transparent;          // so the wheel works anywhere in it
            SizeChanged += (s, e) => { SetMask(); };
            MouseWheel += (s, e) =>
            {
                double from = DateTime.Now < manualUntil ? manual : (double.IsNaN(scroll) ? 0 : scroll);
                manual = Math.Max(-ActualHeight * 0.4, Math.Min(Math.Max(0, total - ActualHeight * 0.5), from - e.Delta / 120.0 * 110));
                manualUntil = lyr != null && lyr.Timed ? DateTime.Now.AddSeconds(4) : DateTime.MaxValue;
                e.Handled = true;
            };
        }

        // lines fade out toward the top and bottom edges
        void SetMask()
        {
            double h = Math.Max(1, ActualHeight), e = Math.Min(0.3, (Stage ? 120 : 50) / h);
            var m = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            m.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0));
            m.GradientStops.Add(new GradientStop(Color.FromArgb(255, 0, 0, 0), e));
            m.GradientStops.Add(new GradientStop(Color.FromArgb(255, 0, 0, 0), 1 - e));
            m.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1));
            m.Freeze();
            OpacityMask = m;
        }

        public Lyrics Lyrics => lyr;

        public void Set(Lyrics l, long id)
        {
            if (id == forId && ReferenceEquals(l, lyr)) return;
            lyr = l; forId = id; builtWidth = -1; scroll = double.NaN; manualUntil = DateTime.MinValue; lastCur = -2;
            content.Children.Clear(); lines.Clear();
        }

        bool Timed => lyr != null && lyr.Timed;
        double FontPx => Stage ? (LeftAlign ? 40 : 46) : 27;

        static FontFamily family;
        static FontFamily Family => family ?? (family = new FontFamily(Theme.Family));

        void Build(double width)
        {
            content.Children.Clear(); lines.Clear();
            builtWidth = width; builtStage = Stage; builtLeft = LeftAlign;
            if (lyr == null || width < 20) return;
            var tf = new Typeface(Family, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            double dip = 1;
            try { dip = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch { }
            double Measure(string s) => s.Length == 0 ? 0 : new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, tf, FontPx, White, dip).WidthIncludingTrailingWhitespace;
            rowH = Math.Ceiling(FontPx * 1.32);
            double gap = Stage ? 26 : 16, space = Measure("a a") - Measure("aa");
            double wrapAt = width * (Stage ? 0.92 : 0.94);
            var src = Timed ? lyr.Lines : (lyr.Plain ?? "").Split('\n').Select(x => new LyricLine { T = -1, Text = x.Trim() }).ToList();
            double y = 0;
            for (int li = 0; li < src.Count; li++)
            {
                var ln = src[li];
                var L = new Line { Top = y, T = Timed ? ln.T : -1, Src = ln };
                List<(string w, int timed)> words = ln.Words != null ? ln.Words.Select((x, i) => (x.w, i)).ToList()
                    : ln.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(x => (x, -1)).ToList();
                if (words.Count == 0)
                {
                    if (Timed) { var r = new Row(); r.Words.Add((0, Measure("♪"), -1)); r.Width = r.Words[0].w; L.Rows.Add(r); AddText(r, "♪", tf); L.Height = rowH; }
                    else L.Height = rowH * 0.45;
                }
                else
                {
                    var row = new Row(); var text = new List<string>(); double x = 0;
                    foreach (var (w, ti) in words)
                    {
                        double ww = Measure(w);
                        if (x > 0 && x + ww > wrapAt)
                        {
                            row.Width = x - space; AddText(row, string.Join(" ", text), tf); L.Rows.Add(row);
                            row = new Row { Y = L.Rows.Count * rowH }; text.Clear(); x = 0;
                        }
                        // where the word starts, measured with everything before it (so spacing matches exactly)
                        double at = text.Count == 0 ? 0 : Measure(string.Join(" ", text) + " ");
                        row.Words.Add((at, ww, ti));
                        text.Add(w); x = at + ww + space;
                    }
                    row.Width = Measure(string.Join(" ", text)); AddText(row, string.Join(" ", text), tf); L.Rows.Add(row);
                    L.Height = L.Rows.Count * rowH;
                }
                L.Width = L.Rows.Count == 0 ? 0 : L.Rows.Max(r => r.Width);
                L.Box = new System.Windows.Controls.Canvas { Width = Math.Max(1, L.Width), Height = Math.Max(1, L.Height), Background = Brushes.Transparent };
                L.Scale = new ScaleTransform(1, 1, Stage && !LeftAlign ? L.Width / 2 : 0, L.Height / 2);
                L.Box.RenderTransform = L.Scale;
                System.Windows.Controls.Canvas.SetLeft(L.Box, Stage && !LeftAlign ? (width - L.Width) / 2 : 0);
                System.Windows.Controls.Canvas.SetTop(L.Box, L.Top);
                foreach (var r in L.Rows)
                {
                    r.Left = Stage && !LeftAlign ? (L.Width - r.Width) / 2 : 0;
                    System.Windows.Controls.Canvas.SetLeft(r.Tb, r.Left); System.Windows.Controls.Canvas.SetTop(r.Tb, r.Y);
                    L.Box.Children.Add(r.Tb);
                }
                if (Timed)
                {
                    var LL = L;
                    L.Box.Cursor = Cursors.Hand;
                    L.Box.MouseEnter += (s, e) => LL.Hot = true;
                    L.Box.MouseLeave += (s, e) => LL.Hot = false;
                    L.Box.MouseLeftButtonUp += (s, e) => { if (LL.T >= 0) Seek?.Invoke(LL.T); e.Handled = true; };
                }
                content.Children.Add(L.Box);
                lines.Add(L);
                y += L.Height + gap;
            }
            total = y;
        }

        void AddText(Row r, string text, Typeface tf)
        {
            r.Tb = new TextBlock
            {
                Text = text, FontFamily = tf.FontFamily, FontWeight = FontWeights.Bold, FontSize = FontPx, Foreground = White,
                TextWrapping = TextWrapping.NoWrap, IsHitTestVisible = false,
            };
            TextOptions.SetTextFormattingMode(r.Tb, TextFormattingMode.Ideal);      // smooth sub-pixel movement while it scrolls
            TextOptions.SetTextRenderingMode(r.Tb, TextRenderingMode.Grayscale);
        }

        int Current(double pos)
        {
            if (!Timed) return -1;
            int cur = -1;
            for (int i = 0; i < lines.Count; i++) { if (lines[i].T <= pos + 0.12) cur = i; else break; }
            return cur;
        }

        static double Ease(double dt, double speed) => 1 - Math.Exp(-dt * speed);

        // one frame: pos = where the song is, dt = seconds since the last frame
        public void Step(double pos, double dt)
        {
            if (lyr == null) return;
            double w = ActualWidth, h = ActualHeight;
            if (w < 20 || h < 20) return;
            if (builtWidth < 0 || Math.Abs(builtWidth - w) > 0.5 || builtStage != Stage || builtLeft != LeftAlign) { Build(w); SetMask(); }
            int cur = Current(pos);
            bool jumped = lastCur >= 0 && cur >= 0 && Math.Abs(cur - lastCur) > 3;
            lastCur = cur;
            double anchor = h * (Stage ? 0.40 : 0.30), target;
            if (DateTime.Now < manualUntil) target = manual;
            else if (Timed) target = cur >= 0 ? lines[cur].Top + lines[cur].Height / 2 - anchor : -anchor + (lines.Count > 0 ? lines[0].Height / 2 : 0);
            else if (Stage && Duration > 1) target = Math.Max(0, Math.Min(1, (pos - 8) / Math.Max(1, Duration - 30))) * Math.Max(0, total - h * 0.55) - h * 0.08;
            else target = 0;
            if (double.IsNaN(scroll) || jumped) scroll = target;
            else scroll += (target - scroll) * Ease(dt, Stage ? 5.5 : 7);
            scrollT.Y = -scroll;

            for (int i = 0; i < lines.Count; i++)
            {
                var L = lines[i];
                double want = Timed && i == cur ? 1 : 0;
                L.Lit += (want - L.Lit) * Ease(dt, want > L.Lit ? 12 : 6);
                double top = L.Top - scroll;
                bool onScreen = top < h + rowH && top + L.Height > -rowH;
                if (!onScreen) { if (L.Box.Visibility != Visibility.Hidden) L.Box.Visibility = Visibility.Hidden; continue; }
                if (L.Box.Visibility != Visibility.Visible) L.Box.Visibility = Visibility.Visible;
                double baseA = !Timed ? 0.88 : (cur < 0 || i > cur ? 0.36 : 0.52);
                if (Stage && Timed && cur >= 0) baseA *= Math.Max(0.22, 1 - Math.Abs(i - cur) * 0.14);
                double a = baseA + (1 - baseA) * L.Lit;
                if (L.Hot && i != cur) a = Math.Min(1, a + 0.3);
                L.Box.Opacity = a * Fade;
                double s = 1 + L.Lit * (Stage ? 0.06 : 0.035);
                L.Scale.ScaleX = s; L.Scale.ScaleY = s;
                // the glow, only while it's (becoming or stopping being) the current line
                if (L.Lit > 0.02)
                {
                    if (L.GlowFx == null) { L.GlowFx = new DropShadowEffect { ShadowDepth = 0, BlurRadius = Stage ? 38 : 22, Color = Glow }; L.Box.Effect = L.GlowFx; }
                    L.GlowFx.Opacity = Math.Min(1, (Stage ? 1.0 : 0.7) * L.Lit * Fade);
                }
                else if (L.GlowFx != null) { L.Box.Effect = null; L.GlowFx = null; }
                // word by word: the sung part bright, the rest dim
                bool byWord = Timed && i == cur && L.Src.Words != null;
                if (byWord)
                {
                    double next = i + 1 < lines.Count ? lines[i + 1].T : -1;
                    foreach (var r in L.Rows)
                    {
                        double fill = 0;
                        foreach (var wd in r.Words)
                        {
                            if (wd.timed < 0) continue;
                            double k = WordProgress(L.Src, wd.timed, pos, next);
                            if (k <= 0) break;
                            fill = wd.x + wd.w * k;
                            if (k < 1) break;
                            fill += 14;
                        }
                        if (r.Fill == null)
                        {
                            r.Fill = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, 0), EndPoint = new Point(Math.Max(1, r.Width), 0) };
                            r.Fill.GradientStops.Add(new GradientStop(Colors.White, 0));
                            r.Fill.GradientStops.Add(new GradientStop(Colors.White, 0));
                            r.Fill.GradientStops.Add(new GradientStop(Color.FromArgb(105, 255, 255, 255), 0));
                            r.Fill.GradientStops.Add(new GradientStop(Color.FromArgb(105, 255, 255, 255), 1));
                            r.Tb.Foreground = r.Fill;
                        }
                        double kk = Math.Max(0, Math.Min(1, fill / Math.Max(1, r.Width)));
                        double soft = Math.Min(0.03, 10 / Math.Max(1, r.Width));
                        r.Fill.GradientStops[1].Offset = Math.Max(0, kk - soft);
                        r.Fill.GradientStops[2].Offset = Math.Min(1, kk + soft * 0.2);
                    }
                    L.Filling = true;
                }
                else if (L.Filling)
                {
                    foreach (var r in L.Rows) { r.Tb.Foreground = White; r.Fill = null; }
                    L.Filling = false;
                }
            }
        }

        static double WordProgress(LyricLine line, int i, double pos, double nextLine)
        {
            var ws = line.Words;
            double start = ws[i].t;
            double end = i + 1 < ws.Count ? ws[i + 1].t : line.End > 0 ? line.End : nextLine > 0 ? Math.Min(nextLine, start + 1.2) : start + 0.8;
            if (end <= start) end = start + 0.25;
            end = Math.Min(end, start + 2.5);
            return Math.Max(0, Math.Min(1, (pos - start) / (end - start)));
        }

        public static Color C(DColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);
    }
}
#endif
