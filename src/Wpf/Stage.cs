#if WPF
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DColor = System.Drawing.Color;
using WinForms = System.Windows.Forms;

namespace Aura.Wpf
{
    // Full-screen lyrics in deep space, drawn by the graphics card (WPF). Same scene as before — nebula,
    // galaxy band, planet, drifting stars, shooting stars, a glow that breathes with the music, a warp
    // between songs — but every frame is composed on the graphics card, so it stays smooth.
    sealed class Stage : Window
    {
        public static int Open;
        readonly IShell shell;
        readonly Grid root = new Grid();
        readonly Image bgA = new Image { Stretch = Stretch.Fill }, bgB = new Image { Stretch = Stretch.Fill, Opacity = 0 };
        readonly ScaleTransform bgScale = new ScaleTransform(1.06, 1.06);
        readonly TranslateTransform bgMove = new TranslateTransform();
        readonly Ellipse glow = new Ellipse();
        readonly RadialGradientBrush glowBrush = new RadialGradientBrush();
        readonly Stars stars = new Stars();
        readonly LyricsView lyrics = new LyricsView { Stage = true };
        readonly Grid noLyrics = new Grid();
        readonly Image bigCover = new Image { Stretch = Stretch.UniformToFill };
        readonly Border bigCoverBox = new Border { CornerRadius = new CornerRadius(18), ClipToBounds = true };
        readonly Image cover = new Image { Stretch = Stretch.UniformToFill };
        readonly TextBlock title = new TextBlock(), artist = new TextBlock(), info = new TextBlock(), time = new TextBlock();
        readonly Rectangle barBack = new Rectangle(), barFill = new Rectangle();
        readonly Ellipse barHead = new Ellipse { Width = 26, Height = 26 };
        readonly StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 40) };
        readonly TranslateTransform buttonsMove = new TranslateTransform();
        readonly Border closeBtn;
        readonly TextBlock playGlyph = new TextBlock();
        readonly Rectangle flash = new Rectangle { IsHitTestVisible = false, Opacity = 0 };
        readonly SolidColorBrush flashBrush = new SolidColorBrush(Colors.White);
        readonly Rectangle intro = new Rectangle { Fill = Brushes.Black, IsHitTestVisible = false };
        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        double last, ui = 1, songFade = 1, energy, kick, warp, travel;
        DateTime lastMove = DateTime.Now;
        Track shown;
        int skyFor = -1; bool buildingSky; BitmapSource nextSky;
        Point lastMouse;

        public static double FrameMs;          // how long the last frame's work took (for the self-test)

        public Stage(IShell shell, WinForms.Screen screen)
        {
            this.shell = shell;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
            Background = Brushes.Black; Title = "AURA lyrics";
            WindowStartupLocation = WindowStartupLocation.Manual;
            // place it on the right screen first (in device pixels -> WPF units), then fill that screen
            double k = 96.0 / Math.Max(1, ScreenDpi());
            Left = screen.Bounds.Left * k; Top = screen.Bounds.Top * k; Width = screen.Bounds.Width * k; Height = screen.Bounds.Height * k;
            Loaded += (s, e) => WindowState = WindowState.Maximized;

            var bgGroup = new TransformGroup(); bgGroup.Children.Add(bgScale); bgGroup.Children.Add(bgMove);
            foreach (var b in new[] { bgA, bgB }) { b.RenderTransformOrigin = new Point(0.5, 0.5); b.RenderTransform = bgGroup; RenderOptions.SetBitmapScalingMode(b, BitmapScalingMode.LowQuality); root.Children.Add(b); }

            glowBrush.GradientStops.Add(new GradientStop(Colors.White, 0));
            glowBrush.GradientStops.Add(new GradientStop(Colors.White, 0.45));
            glowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
            glow.Fill = glowBrush; glow.IsHitTestVisible = false;
            glow.HorizontalAlignment = HorizontalAlignment.Center; glow.VerticalAlignment = VerticalAlignment.Top;
            root.Children.Add(glow);
            stars.IsHitTestVisible = false;
            root.Children.Add(stars);

            lyrics.Margin = new Thickness(0); lyrics.HorizontalAlignment = HorizontalAlignment.Stretch; lyrics.VerticalAlignment = VerticalAlignment.Stretch;
            lyrics.Seek = sec => { shell.Player.Seek(sec); Poke(); };
            var lyricsBox = new Grid();
            lyricsBox.Children.Add(lyrics);
            root.Children.Add(lyricsBox);
            lyricsBox.Loaded += (s, e) => LayoutLyrics(lyricsBox);
            SizeChanged += (s, e) => LayoutLyrics(lyricsBox);

            // no lyrics: the cover floats in the middle
            bigCoverBox.Child = bigCover;
            bigCoverBox.Effect = new DropShadowEffect { ShadowDepth = 0, BlurRadius = 90, Opacity = 0.9 };
            var nlText = new TextBlock { Text = "No lyrics for this song yet", Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), FontSize = 17, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, -44) };
            var nlStack = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 120) };
            nlStack.Children.Add(bigCoverBox); nlStack.Children.Add(nlText);
            noLyrics.Children.Add(nlStack);
            noLyrics.IsHitTestVisible = false;
            root.Children.Add(noLyrics);

            // the song, bottom left
            var family = new FontFamily(Theme.Family);
            var coverBox = new Border { Width = 86, Height = 86, CornerRadius = new CornerRadius(12), ClipToBounds = true, Child = cover, Effect = new DropShadowEffect { ShadowDepth = 0, BlurRadius = 50, Opacity = 0.9 } };
            title.FontSize = 23; title.FontWeight = FontWeights.Bold; title.Foreground = Brushes.White; title.FontFamily = family; title.TextTrimming = TextTrimming.CharacterEllipsis;
            artist.FontSize = 15; artist.Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)); artist.FontFamily = family; artist.TextTrimming = TextTrimming.CharacterEllipsis; artist.Margin = new Thickness(0, 4, 0, 0);
            info.FontSize = 12.5; info.Foreground = new SolidColorBrush(Color.FromArgb(115, 255, 255, 255)); info.FontFamily = family; info.Margin = new Thickness(0, 6, 0, 0);
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 0), MaxWidth = 700 };
            texts.Children.Add(title); texts.Children.Add(artist); texts.Children.Add(info);
            var songPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(44, 0, 0, 54), IsHitTestVisible = false };
            songPanel.Children.Add(coverBox); songPanel.Children.Add(texts);
            root.Children.Add(songPanel);

            // how far through the song
            barBack.Height = 4; barBack.VerticalAlignment = VerticalAlignment.Bottom; barBack.Fill = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255));
            barFill.Height = 4; barFill.VerticalAlignment = VerticalAlignment.Bottom; barFill.HorizontalAlignment = HorizontalAlignment.Left;
            barHead.VerticalAlignment = VerticalAlignment.Bottom; barHead.HorizontalAlignment = HorizontalAlignment.Left; barHead.Margin = new Thickness(0, 0, 0, -11); barHead.IsHitTestVisible = false;
            barBack.Cursor = Cursors.Hand;
            barBack.MouseLeftButtonUp += (s, e) => { double d = shell.Player.Duration; if (d > 0) shell.Player.Seek(e.GetPosition(barBack).X / Math.Max(1, barBack.ActualWidth) * d); Poke(); };
            var barHit = new Rectangle { Height = 14, VerticalAlignment = VerticalAlignment.Bottom, Fill = Brushes.Transparent, Cursor = Cursors.Hand };
            barHit.MouseLeftButtonUp += (s, e) => { double d = shell.Player.Duration; if (d > 0) shell.Player.Seek(e.GetPosition(barHit).X / Math.Max(1, barHit.ActualWidth) * d); Poke(); };
            root.Children.Add(barBack); root.Children.Add(barFill); root.Children.Add(barHead); root.Children.Add(barHit);

            // buttons (they slide away when the mouse rests)
            buttons.RenderTransform = buttonsMove;
            buttons.Children.Add(Button("", 46, () => shell.Player.Prev(), "Previous (P)"));
            var play = Button("", 60, () => shell.Player.Toggle(), "Play / pause (Space)", playGlyph, true);
            play.Margin = new Thickness(18, 0, 18, 0);
            buttons.Children.Add(play);
            buttons.Children.Add(Button("", 46, () => shell.Player.Next(), "Next (N)"));
            root.Children.Add(buttons);
            time.FontSize = 13; time.Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)); time.FontFamily = family;
            time.HorizontalAlignment = HorizontalAlignment.Right; time.VerticalAlignment = VerticalAlignment.Bottom; time.Margin = new Thickness(0, 0, 44, 54); time.IsHitTestVisible = false;
            root.Children.Add(time);
            closeBtn = Button("", 44, Close, "Close (Esc)");
            closeBtn.HorizontalAlignment = HorizontalAlignment.Right; closeBtn.VerticalAlignment = VerticalAlignment.Top; closeBtn.Margin = new Thickness(0, 30, 44, 0);
            root.Children.Add(closeBtn);

            flash.Fill = flashBrush;
            root.Children.Add(flash);
            root.Children.Add(intro);
            Content = root;

            KeyDown += OnKey;
            MouseMove += (s, e) => { var p = e.GetPosition(this); if ((p - lastMouse).Length > 2) { lastMouse = p; Poke(); } };
            MouseDoubleClick += (s, e) => { if (!(e.OriginalSource is TextBlock) || e.ChangedButton == MouseButton.Left) Close(); };
            Open++;
            Closed += (s, e) => { Open--; CompositionTarget.Rendering -= Frame; Mouse.OverrideCursor = null; };
            CompositionTarget.Rendering += Frame;
        }

        void LayoutLyrics(Grid box)
        {
            double w = ActualWidth > 0 ? ActualWidth : Width, h = ActualHeight > 0 ? ActualHeight : Height;
            lyrics.Width = w * 0.82; lyrics.Height = h * 0.72;
            lyrics.HorizontalAlignment = HorizontalAlignment.Center; lyrics.VerticalAlignment = VerticalAlignment.Top;
            lyrics.Margin = new Thickness(0, h * 0.06, 0, 0);
            double cs = Math.Round(Math.Min(w, h) * 0.34);
            bigCoverBox.Width = cs; bigCoverBox.Height = cs;
            glow.Width = w * 0.8; glow.Height = h * 0.6; glow.Margin = new Thickness(0, h * 0.42 - glow.Height / 2, 0, 0);
            barBack.Width = w;
        }

        static double ScreenDpi()
        {
            try { using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX; } catch { return 96; }
        }

        Border Button(string glyph, double size, Action click, string tip, TextBlock text = null, bool main = false)
        {
            text = text ?? new TextBlock();
            text.Text = glyph; text.FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"); text.FontSize = size * 0.36;
            text.Foreground = main ? Brushes.Black : Brushes.White; text.HorizontalAlignment = HorizontalAlignment.Center; text.VerticalAlignment = VerticalAlignment.Center;
            var b = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Child = text, Cursor = Cursors.Hand, ToolTip = tip };
            b.Background = main ? (Brush)new SolidColorBrush(Colors.White) : new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
            b.MouseEnter += (s, e) => { if (!main) b.Background = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)); };
            b.MouseLeave += (s, e) => { if (!main) b.Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)); };
            b.MouseLeftButtonUp += (s, e) => { click(); Poke(); e.Handled = true; };
            return b;
        }

        void OnKey(object s, KeyEventArgs e)
        {
            var P = shell.Player;
            switch (e.Key)
            {
                case Key.Escape: case Key.F11: Close(); break;
                case Key.Space: P.Toggle(); break;
                case Key.Left: P.Seek(P.Position - 5); break;
                case Key.Right: P.Seek(P.Position + 5); break;
                case Key.Up: P.Volume += 5; break;
                case Key.Down: P.Volume -= 5; break;
                case Key.N: P.Next(); break;
                case Key.P: P.Prev(); break;
                default: return;
            }
            Poke();
            e.Handled = true;
        }

        void Poke() { lastMove = DateTime.Now; Mouse.OverrideCursor = null; }

        static double Ease(double dt, double speed) => 1 - Math.Exp(-dt * speed);

        float Loudness(Track t, double pos)
        {
            var s = shell.Sound.Get(t);
            if (s == null || s.Env.Length == 0) return -1;
            int i = (int)(pos * SongSound.Fps);
            if (i < 0 || i >= s.Env.Length) return 0;
            int m = 0; for (int j = Math.Max(0, i - 2); j <= Math.Min(s.Env.Length - 1, i + 2); j++) m = Math.Max(m, s.Env[j]);
            return Math.Max(0, Math.Min(1, (m - 150) / 100f));
        }

        // a new sky for the song's colors, painted on a worker thread (the painting is the old, proven code)
        void Sky(DColor c)
        {
            int key = c.ToArgb();
            if (key == skyFor || buildingSky || ActualWidth < 200 || ActualHeight < 200) return;
            int w = (int)ActualWidth, h = (int)ActualHeight;
            double scale = ScreenDpi() / 96.0;
            w = (int)(w * scale); h = (int)(h * scale);
            skyFor = key; buildingSky = true;
            bool first = bgA.Source == null;
            var th = new System.Threading.Thread(() =>
            {
                BitmapSource src = null;
                try
                {
                    using (var bmp = Space.Paint(new System.Drawing.Size(w, h), c))
                    {
                        var bd = bmp.LockBits(new System.Drawing.Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                        src = BitmapSource.Create(w, h, 96 * scale, 96 * scale, PixelFormats.Pbgra32, null, bd.Scan0, bd.Stride * h, bd.Stride);
                        bmp.UnlockBits(bd);
                        src.Freeze();
                    }
                }
                catch (Exception e) { App.Log("space: " + e.Message); }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    buildingSky = false;
                    if (src == null) return;
                    if (first) bgA.Source = src;
                    else { nextSky = src; if (warp < 0.05) warp = 0.6; }
                }));
            }) { IsBackground = true, Priority = System.Threading.ThreadPriority.BelowNormal };
            th.Start();
        }

        void Frame(object sender, EventArgs e)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            double now = clock.Elapsed.TotalSeconds, dt = Math.Max(0.001, Math.Min(0.1, now - last));
            last = now;
            try { Step(now, dt); }
            catch (Exception x) { App.Log("full screen: " + x); }
            FrameMs = FrameMs * 0.9 + sw.Elapsed.TotalMilliseconds * 0.1;
        }

        void Step(double now, double dt)
        {
            var P = shell.Player;
            var t = P.Current;
            double pos = P.SmoothPosition;
            if (t != shown)
            {
                if (shown != null && t != null) warp = 1;
                shown = t; songFade = 0;
                lyrics.Set(t == null ? null : shell.Lyrics.Get(t), t?.Id ?? -1);
                if (t != null)
                {
                    shell.Sound.Want(t);
                    title.Text = t.Name; artist.Text = t.ArtistName + (t.Album.Length > 0 ? "  ·  " + t.Album : "");
                    var src = CoverOf(t);
                    cover.Source = src; bigCover.Source = src;
                }
            }
            if (t != null && cover.Source == null) { var src = CoverOf(t); if (src != null) { cover.Source = src; bigCover.Source = src; } }
            var accent = Theme.AccentTarget;
            Sky(accent);
            var ac = LyricsView.C(accent);
            var glowColor = LyricsView.C(Theme.Mix(accent, DColor.White, 0.35f));

            songFade += (1 - songFade) * Ease(dt, 3.5);
            intro.Opacity = Math.Max(0, intro.Opacity - dt * 1.6);
            if (intro.Opacity <= 0.01 && intro.Visibility == Visibility.Visible) intro.Visibility = Visibility.Collapsed;
            bool showUi = (DateTime.Now - lastMove).TotalSeconds < 2.5;
            ui += ((showUi ? 1 : 0) - ui) * Ease(dt, showUi ? 10 : 3);
            if (!showUi && IsActive && Mouse.OverrideCursor == null) Mouse.OverrideCursor = Cursors.None;
            else if (!IsActive && Mouse.OverrideCursor != null) Mouse.OverrideCursor = null;
            buttonsMove.Y = (1 - ui) * 140;
            closeBtn.Opacity = ui; closeBtn.IsHitTestVisible = ui > 0.3; time.Opacity = ui;

            // loudness -> energy and kicks
            float loud = t == null ? -1 : Loudness(t, pos);
            if (loud >= 0)
            {
                if (loud > energy + 0.12) kick = Math.Min(1, kick + (loud - energy) * 1.6);
                energy += (loud - energy) * Ease(dt, loud > energy ? 14 : 3);
            }
            else energy += (0.3 - energy) * Ease(dt, 1);
            if (!P.IsPlaying) energy += (0 - energy) * Ease(dt, 2);
            kick *= Math.Exp(-dt * 5);

            // the warp between songs
            warp = Math.Max(0, warp - dt / 1.6);
            double peak = warp > 0 ? Math.Sin(Math.PI * (1 - warp)) : 0;
            if (nextSky != null && warp < 0.55) { bgA.Source = nextSky; nextSky = null; }
            flash.Opacity = peak * peak * 0.55;
            if (flash.Opacity > 0.005) flashBrush.Color = LyricsView.C(Theme.Mix(accent, DColor.White, 0.6f));

            // the sky drifts very slowly, like a camera floating
            bgMove.X = Math.Sin(now * 0.031) * ActualWidth * 0.012;
            bgMove.Y = Math.Cos(now * 0.023) * ActualHeight * 0.01;

            // the glow
            glowBrush.GradientStops[0].Color = Color.FromArgb(255, ac.R, ac.G, ac.B);
            glowBrush.GradientStops[1].Color = Color.FromArgb(90, ac.R, ac.G, ac.B);
            glowBrush.GradientStops[2].Color = Color.FromArgb(0, ac.R, ac.G, ac.B);
            glow.Opacity = Math.Min(1, 0.12 + energy * 0.3 + kick * 0.22 + peak * 0.3);
            double gs = 1 + energy * 0.14;
            glow.RenderTransformOrigin = new Point(0.5, 0.5);
            glow.RenderTransform = new ScaleTransform(gs, gs);

            // stars
            double speed = (P.IsPlaying ? 1 : 0.25) * (1 + energy * 1.8 + kick * 1.5) + peak * 40;
            travel += dt * speed;
            stars.Update(travel, now, energy, kick, peak, dt, ac);

            // lyrics, or the cover when there aren't any
            bool has = t != null && lyrics.Lyrics != null;
            lyrics.Visibility = has ? Visibility.Visible : Visibility.Hidden;
            noLyrics.Visibility = t != null && !has ? Visibility.Visible : Visibility.Hidden;
            if (has)
            {
                lyrics.Glow = glowColor;
                lyrics.Duration = P.Duration;
                lyrics.Fade = songFade;
                lyrics.Step(pos, dt);
            }
            else if (t != null)
            {
                double p = 1 + energy * 0.025;
                bigCoverBox.RenderTransformOrigin = new Point(0.5, 0.5);
                bigCoverBox.RenderTransform = new ScaleTransform(p, p);
                ((DropShadowEffect)bigCoverBox.Effect).Color = ac;
                ((DropShadowEffect)bigCoverBox.Effect).Opacity = 0.6 + 0.4 * energy;
            }
            if (t != null)
            {
                var snd = shell.Sound.Get(t);
                info.Text = snd != null && snd.Bpm > 0 ? snd.Bpm.ToString("0") + " BPM" + (snd.Key.Length > 0 ? "  ·  " + snd.Key : "") : "";
                ((DropShadowEffect)((Border)cover.Parent).Effect).Color = ac;
                ((DropShadowEffect)((Border)cover.Parent).Effect).Opacity = 0.5 + energy * 0.45;
            }
            title.Opacity = artist.Opacity = info.Opacity = songFade;

            // progress
            double d = P.Duration, k = d > 0 ? Math.Max(0, Math.Min(1, pos / d)) : 0;
            double W = ActualWidth;
            barFill.Width = Math.Max(0, W * k);
            if (!(barFill.Fill is LinearGradientBrush lg) || lg.IsFrozen) barFill.Fill = lg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            if (lg.GradientStops.Count == 0) { lg.GradientStops.Add(new GradientStop()); lg.GradientStops.Add(new GradientStop(Colors.White, 1)); }
            lg.GradientStops[0].Color = Color.FromArgb(160, ac.R, ac.G, ac.B);
            lg.GradientStops[1].Color = LyricsView.C(Theme.Mix(accent, DColor.White, 0.5f));
            barHead.Margin = new Thickness(W * k - 13, 0, 0, -11);
            if (!(barHead.Fill is RadialGradientBrush rg) || rg.IsFrozen) barHead.Fill = rg = new RadialGradientBrush();
            if (rg.GradientStops.Count == 0) { rg.GradientStops.Add(new GradientStop(Color.FromArgb(220, 255, 255, 255), 0)); rg.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1)); }
            rg.GradientStops[1].Color = Color.FromArgb(0, ac.R, ac.G, ac.B);
            playGlyph.Text = P.IsPlaying ? "" : "";
            time.Text = Theme.Time(pos) + "  /  " + Theme.Time(d);
        }

        static readonly Dictionary<long, BitmapSource> covers = new Dictionary<long, BitmapSource>();
        static BitmapSource CoverOf(Track t)
        {
            if (covers.TryGetValue(t.Id, out var c)) return c;
            var img = Art.Get(t) as System.Drawing.Bitmap;
            if (img == null) return null;
            try
            {
                using (var copy = new System.Drawing.Bitmap(img.Width, img.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
                {
                    using (var g = System.Drawing.Graphics.FromImage(copy)) g.DrawImage(img, 0, 0, img.Width, img.Height);
                    var bd = copy.LockBits(new System.Drawing.Rectangle(0, 0, copy.Width, copy.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                    c = BitmapSource.Create(copy.Width, copy.Height, 96, 96, PixelFormats.Pbgra32, null, bd.Scan0, bd.Stride * copy.Height, bd.Stride);
                    copy.UnlockBits(bd);
                    c.Freeze();
                }
            }
            catch { return null; }
            if (covers.Count > 40) covers.Clear();
            covers[t.Id] = c;
            return c;
        }

        // ---------- the stars: three layers drifting at different speeds, twinkling, and shooting stars ----------
        sealed class Stars : FrameworkElement
        {
            sealed class Star { public double X, Y, Size, Phase, Speed, Depth; }
            sealed class Meteor { public double X, Y, Vx, Vy, Life, Age, Len; }
            readonly List<Star> stars = new List<Star>();
            readonly List<Meteor> meteors = new List<Meteor>();
            readonly Random rnd = new Random();
            double travel, time, energy, kick, peak, nextMeteor = 4;
            Color tint = Colors.White;
            readonly RadialGradientBrush dot;
            readonly Pen flare = new Pen(new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)), 1);

            public Stars()
            {
                var r = new Random(11);
                void Layer(int n, double depth, double min, double max)
                { for (int i = 0; i < n; i++) stars.Add(new Star { X = r.NextDouble(), Y = r.NextDouble(), Depth = depth, Size = min + r.NextDouble() * (max - min), Phase = r.NextDouble() * 6.28, Speed = 0.4 + r.NextDouble() }); }
                Layer(150, 0.25, 2.5, 5); Layer(70, 0.55, 4, 9); Layer(18, 1, 10, 22);
                dot = new RadialGradientBrush();
                dot.GradientStops.Add(new GradientStop(Colors.White, 0));
                dot.GradientStops.Add(new GradientStop(Color.FromArgb(200, 255, 255, 255), 0.18));
                dot.GradientStops.Add(new GradientStop(Color.FromArgb(40, 220, 230, 255), 0.45));
                dot.GradientStops.Add(new GradientStop(Color.FromArgb(0, 220, 230, 255), 1));
                dot.Freeze();
                flare.Freeze();
            }

            public void Update(double travel, double time, double energy, double kick, double peak, double dt, Color tint)
            {
                this.travel = travel; this.time = time; this.energy = energy; this.kick = kick; this.peak = peak; this.tint = tint;
                nextMeteor -= dt;
                double W = ActualWidth, H = ActualHeight;
                if (nextMeteor <= 0 && W > 0)
                {
                    nextMeteor = 5 + rnd.NextDouble() * 12 - energy * 4;
                    double ang = Math.PI * (0.12 + rnd.NextDouble() * 0.18), v = H * (0.9 + rnd.NextDouble() * 0.6);
                    meteors.Add(new Meteor { X = rnd.NextDouble() * W * 0.8, Y = rnd.NextDouble() * H * 0.35, Vx = Math.Cos(ang) * v, Vy = Math.Sin(ang) * v, Life = 0.7 + rnd.NextDouble() * 0.5, Len = H * 0.16 });
                }
                for (int i = meteors.Count - 1; i >= 0; i--) { var m = meteors[i]; m.Age += dt; m.X += m.Vx * dt; m.Y += m.Vy * dt; if (m.Age >= m.Life) meteors.RemoveAt(i); }
                InvalidateVisual();
            }

            protected override void OnRender(DrawingContext dc)
            {
                double W = ActualWidth, H = ActualHeight;
                if (W <= 0) return;
                bool streak = peak > 0.15;
                foreach (var s in stars)
                {
                    double x = ((s.X - travel * 0.012 * s.Depth * s.Speed) % 1 + 1) % 1 * W;
                    double y = ((s.Y + travel * 0.002 * s.Depth) % 1 + 1) % 1 * H;
                    double tw = 0.55 + 0.45 * Math.Sin(time * (1.1 + s.Speed) + s.Phase);
                    double a = Math.Min(1, (0.35 + 0.65 * tw) * (0.45 + s.Depth * 0.55) * (1 + kick * 0.5));
                    double sz = s.Size * (1 + (s.Depth >= 1 ? energy * 0.35 : 0));
                    if (streak)
                    {
                        double len = peak * W * 0.12 * s.Depth * s.Speed;
                        var p = new Pen(new SolidColorBrush(Color.FromArgb((byte)(Math.Min(1, a + 0.3) * 230), 235, 240, 255)), Math.Max(1, sz * 0.3)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                        dc.DrawLine(p, new Point(x, y), new Point(x + len, y - len * 0.17));
                        continue;
                    }
                    dc.PushOpacity(a);
                    dc.DrawEllipse(dot, null, new Point(x, y), sz / 2, sz / 2);
                    if (s.Depth >= 1) { dc.DrawLine(flare, new Point(x - sz * 0.6, y), new Point(x + sz * 0.6, y)); dc.DrawLine(flare, new Point(x, y - sz * 0.6), new Point(x, y + sz * 0.6)); }
                    dc.Pop();
                }
                foreach (var m in meteors)
                {
                    double k = m.Age / m.Life, fade = Math.Sin(Math.PI * k);
                    double vl = Math.Sqrt(m.Vx * m.Vx + m.Vy * m.Vy);
                    var tail = new Point(m.X - m.Vx / vl * m.Len, m.Y - m.Vy / vl * m.Len);
                    var br = new LinearGradientBrush(Color.FromArgb(0, 255, 255, 255), Color.FromArgb((byte)(220 * fade), 255, 255, 255), new Point(tail.X, tail.Y), new Point(m.X, m.Y)) { MappingMode = BrushMappingMode.Absolute };
                    dc.DrawLine(new Pen(br, 2.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, tail, new Point(m.X, m.Y));
                    dc.PushOpacity(fade);
                    dc.DrawEllipse(dot, null, new Point(m.X, m.Y), 7, 7);
                    dc.Pop();
                }
            }
        }
    }
}
#endif
