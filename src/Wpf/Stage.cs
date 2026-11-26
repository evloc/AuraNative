#if WPF
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DColor = System.Drawing.Color;
using WinForms = System.Windows.Forms;
using WCanvas = System.Windows.Controls.Canvas;

namespace Aura.Wpf
{
    // Full-screen lyrics, drawn by the graphics card (WPF): the cover in the middle with a ring that breathes
    // with the music, a clock above it, light streaks flying out of it, the sky made of the cover's colors,
    // what plays next on the left, the lyrics on the right and a glass player bar at the bottom.
    // The layout and the moving things come from Cosmos, shared with the older drawing (FullLyrics).
    sealed class Stage : Window
    {
        public static int Open;
        readonly IShell shell;
        readonly WCanvas root = new WCanvas { ClipToBounds = true };
        readonly Image skyA = new Image { Stretch = Stretch.Fill }, skyB = new Image { Stretch = Stretch.Fill, Opacity = 0 };
        readonly ScaleTransform skyScale = new ScaleTransform(1.08, 1.08);
        readonly TranslateTransform skyMove = new TranslateTransform();
        readonly Scene scene = new Scene();
        readonly Ellipse glow = new Ellipse { IsHitTestVisible = false };
        readonly RadialGradientBrush glowBrush = new RadialGradientBrush();
        readonly Ellipse ring = new Ellipse { IsHitTestVisible = false, StrokeThickness = 1.6 };
        readonly Ellipse ringHalo1 = new Ellipse { IsHitTestVisible = false, StrokeThickness = 8 }, ringHalo2 = new Ellipse { IsHitTestVisible = false, StrokeThickness = 20 };
        readonly SolidColorBrush ringBrush = new SolidColorBrush(), halo1Brush = new SolidColorBrush(), halo2Brush = new SolidColorBrush();
        readonly Rectangle coverShadow = new Rectangle { IsHitTestVisible = false };
        readonly SolidColorBrush accentBrush = new SolidColorBrush(), dimBrush = new SolidColorBrush(Color.FromArgb(190, 255, 255, 255));
        readonly LinearGradientBrush seekBrush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        double clipFor = -1;
        readonly Image cover = new Image { Stretch = Stretch.UniformToFill };
        readonly Border coverBox = new Border { ClipToBounds = true, IsHitTestVisible = false };
        readonly ScaleTransform coverScale = new ScaleTransform(1, 1);
        readonly TextBlock date = new TextBlock(), clockText = new TextBlock(), title = new TextBlock(), artist = new TextBlock(), album = new TextBlock(), hint = new TextBlock();
        readonly LyricsView lyrics = new LyricsView { Stage = true, LeftAlign = true };
        readonly StackPanel upNext = new StackPanel();
        readonly TextBlock upNextLabel = new TextBlock();
        string upNextKey = "";
        // the bar
        readonly Border bar = new Border();
        readonly TranslateTransform barMove = new TranslateTransform();
        readonly Image barCover = new Image { Stretch = Stretch.UniformToFill };
        readonly TextBlock barTitle = new TextBlock(), barArtist = new TextBlock(), timeNow = new TextBlock(), timeAll = new TextBlock();
        readonly TextBlock playGlyph = new TextBlock(), favGlyph = new TextBlock(), shuffleGlyph = new TextBlock(), repeatGlyph = new TextBlock(), volGlyph = new TextBlock();
        readonly Grid seek = new Grid { Height = 18, Background = Brushes.Transparent, Cursor = Cursors.Hand };
        readonly Rectangle seekFill = new Rectangle { Height = 3, RadiusX = 1.5, RadiusY = 1.5, HorizontalAlignment = HorizontalAlignment.Left };
        readonly Ellipse seekHead = new Ellipse { Width = 11, Height = 11, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left };
        readonly Grid vol = new Grid { Height = 18, Background = Brushes.Transparent, Cursor = Cursors.Hand };
        readonly Rectangle volFill = new Rectangle { Height = 3, RadiusX = 1.5, RadiusY = 1.5, HorizontalAlignment = HorizontalAlignment.Left };
        readonly Ellipse volHead = new Ellipse { Width = 11, Height = 11, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left };
        readonly Border closeBtn;
        readonly Rectangle flash = new Rectangle { IsHitTestVisible = false, Opacity = 0 };
        readonly SolidColorBrush flashBrush = new SolidColorBrush(Colors.White);
        readonly Rectangle intro = new Rectangle { Fill = Brushes.Black, IsHitTestVisible = false };
        readonly Cosmos cosmos = new Cosmos();
        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        double last, ui = 1, songFade = 1, side, skyFade = 1;
        DateTime lastMove = DateTime.Now;
        Track shown;
        StageSong song, loading;
        static readonly Color[] spacePal = Cosmos.SpacePal.Select(LyricsView.C).ToArray();       // deep space: the same colors for every song
        Color[] pal = spacePal;                 // the cover look: the cover's own colors
        bool CoverLook => shell.Prefs.StageLook != "space";
        int lookGen;
        readonly Border lookBtn;
        readonly TextBlock lookGlyph = new TextBlock();
        static BitmapSource spaceSky;
        Point lastMouse;
        bool dragSeek, dragVol;

        public static double FrameMs;          // how long the last frame's work took (for the self-test)
        static FontFamily family, light;
        static FontFamily Family => family ?? (family = new FontFamily(Theme.Family));
        static FontFamily Light => light ?? (light = new FontFamily("Segoe UI Variable Display Light, Segoe UI Light, " + Theme.Family));
        static readonly FontFamily Glyphs = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        public Stage(IShell shell, WinForms.Screen screen)
        {
            this.shell = shell;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
            Background = Brushes.Black; Title = "AURA lyrics";
            WindowStartupLocation = WindowStartupLocation.Manual;
            double k = 96.0 / Math.Max(1, ScreenDpi());
            Left = screen.Bounds.Left * k; Top = screen.Bounds.Top * k; Width = screen.Bounds.Width * k; Height = screen.Bounds.Height * k;
            Loaded += (s, e) => WindowState = WindowState.Maximized;

            // the sky: two layers so a new song's sky can fade in over the old one
            var tg = new TransformGroup(); tg.Children.Add(skyScale); tg.Children.Add(skyMove);
            foreach (var im in new[] { skyA, skyB }) { im.RenderTransformOrigin = new Point(0.5, 0.5); im.RenderTransform = tg; RenderOptions.SetBitmapScalingMode(im, BitmapScalingMode.HighQuality); root.Children.Add(im); }
            if (spaceSky == null) using (var b = Cosmos.SpaceSky(1280, 720)) spaceSky = ToSource(b);
            skyA.Source = spaceSky;

            scene.IsHitTestVisible = false;
            root.Children.Add(scene);

            glowBrush.GradientStops.Add(new GradientStop(Colors.White, 0));
            glowBrush.GradientStops.Add(new GradientStop(Colors.White, 0.4));
            glowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
            glow.Fill = glowBrush;
            root.Children.Add(glow);
            ring.Stroke = ringBrush; ringHalo1.Stroke = halo1Brush; ringHalo2.Stroke = halo2Brush;
            root.Children.Add(ringHalo2); root.Children.Add(ringHalo1); root.Children.Add(ring);
            seekBrush.GradientStops.Add(new GradientStop(Colors.White, 0)); seekBrush.GradientStops.Add(new GradientStop(Colors.White, 1));
            // a soft shadow under the cover (a drawn gradient: cheaper than an effect that would be redone every frame)
            var shadow = new RadialGradientBrush(Color.FromArgb(150, 0, 0, 0), Color.FromArgb(0, 0, 0, 0)) { GradientOrigin = new Point(0.5, 0.45) };
            shadow.Freeze();
            coverShadow.Fill = shadow;
            root.Children.Add(coverShadow);

            coverBox.Child = cover;
            coverBox.RenderTransformOrigin = new Point(0.5, 0.5); coverBox.RenderTransform = coverScale;
            RenderOptions.SetBitmapScalingMode(cover, BitmapScalingMode.HighQuality);
            root.Children.Add(coverBox);

            Text(date, 13, FontWeights.SemiBold, 150); Text(title, 26, FontWeights.Bold, 255); Text(artist, 14, FontWeights.SemiBold, 160); Text(album, 13, FontWeights.Normal, 100); Text(hint, 13, FontWeights.Normal, 80);
            clockText.FontFamily = Light; clockText.TextAlignment = TextAlignment.Center; clockText.IsHitTestVisible = false;
            clockText.Foreground = new LinearGradientBrush(Color.FromArgb(240, 255, 255, 255), Color.FromArgb(105, 255, 255, 255), 90);
            hint.Text = "No lyrics for this song yet";
            foreach (var tb in new[] { date, clockText, title, artist, album, hint }) root.Children.Add(tb);

            lyrics.Seek = sec => { shell.Player.Seek(sec); Poke(); };
            root.Children.Add(lyrics);

            Text(upNextLabel, 10.5, FontWeights.SemiBold, 110); upNextLabel.TextAlignment = TextAlignment.Left; upNextLabel.Text = "U P   N E X T";
            upNext.Children.Add(upNextLabel);
            root.Children.Add(upNext);

            BuildBar();
            root.Children.Add(bar);

            closeBtn = Round("", 44, Close, "Close (Esc)");
            root.Children.Add(closeBtn);
            lookBtn = Round("\uE790", 44, ToggleLook, "Switch the look: the cover's colors, or space (V)", lookGlyph);
            root.Children.Add(lookBtn);
            flash.Fill = flashBrush;
            root.Children.Add(flash);
            root.Children.Add(intro);
            Content = root;

            KeyDown += OnKey;
            MouseMove += (s, e) => { var p = e.GetPosition(this); if ((p - lastMouse).Length > 2) { lastMouse = p; Poke(); } };
            MouseDoubleClick += (s, e) => { if (e.OriginalSource == root || e.OriginalSource == scene || e.OriginalSource is Image im && (im == skyA || im == skyB)) Close(); };
            MouseLeftButtonUp += (s, e) => { if (dragSeek || dragVol) { dragSeek = dragVol = false; ReleaseMouseCapture(); } };
            Open++; CrashGuard.Doing("full screen", "open");
            Closed += (s, e) => { Open--; if (Open == 0) CrashGuard.Doing("full screen", null); CompositionTarget.Rendering -= Frame; Mouse.OverrideCursor = null; song?.Dispose(); };
            CompositionTarget.Rendering += Frame;
        }

        static void Text(TextBlock t, double size, FontWeight w, byte alpha)
        {
            t.FontFamily = Family; t.FontSize = size; t.FontWeight = w; t.Foreground = new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255));
            t.TextAlignment = TextAlignment.Center; t.TextTrimming = TextTrimming.CharacterEllipsis; t.IsHitTestVisible = false;
        }

        static double ScreenDpi()
        {
            try { using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX; } catch { return 96; }
        }

        // ---------- buttons ----------
        Border Round(string glyph, double size, Action click, string tip, TextBlock text = null, bool main = false, bool bare = false)
        {
            text = text ?? new TextBlock();
            text.Text = glyph; text.FontFamily = Glyphs; text.FontSize = size * (main ? 0.4 : 0.42);
            text.Foreground = main ? Brushes.Black : Brushes.White; text.HorizontalAlignment = HorizontalAlignment.Center; text.VerticalAlignment = VerticalAlignment.Center;
            var idle = main ? (Brush)Brushes.White : bare ? Brushes.Transparent : new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
            var b = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Child = text, Cursor = Cursors.Hand, ToolTip = tip, Background = idle };
            b.MouseEnter += (s, e) => { if (!main) b.Background = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)); };
            b.MouseLeave += (s, e) => { if (!main) b.Background = idle; };
            b.MouseLeftButtonUp += (s, e) => { click(); Poke(); e.Handled = true; };
            return b;
        }

        void BuildBar()
        {
            var g = new Grid { Margin = new Thickness(10, 8, 18, 8) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var P = shell.Player;

            // left: the song
            var left = new DockPanel { LastChildFill = true, VerticalAlignment = VerticalAlignment.Center };
            var thumb = new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = barCover, Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)) };
            DockPanel.SetDock(thumb, Dock.Left); left.Children.Add(thumb);
            var fav = Round("", 34, () => { var t = shell.Player.Current; if (t != null) shell.Lib.SetFav(t, !t.Fav); }, "Favorite", favGlyph, bare: true);
            DockPanel.SetDock(fav, Dock.Right); left.Children.Add(fav);
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 6, 0) };
            Text(barTitle, 14, FontWeights.SemiBold, 255); Text(barArtist, 12, FontWeights.Normal, 150);
            barTitle.TextAlignment = barArtist.TextAlignment = TextAlignment.Left;
            names.Children.Add(barTitle); names.Children.Add(barArtist);
            left.Children.Add(names);
            Grid.SetColumn(left, 0); g.Children.Add(left);

            // middle: buttons and how far along
            var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            row.Children.Add(Round("", 34, () => P.SetShuffle(!P.Shuffle), "Shuffle", shuffleGlyph, bare: true));
            row.Children.Add(Round("", 34, () => P.Prev(), "Previous (P)", bare: true));
            var play = Round("", 40, () => P.Toggle(), "Play / pause (Space)", playGlyph, true);
            play.Margin = new Thickness(10, 0, 10, 0);
            row.Children.Add(play);
            row.Children.Add(Round("", 34, () => P.Next(), "Next (N)", bare: true));
            row.Children.Add(Round("", 34, () => P.CycleRepeat(), "Repeat", repeatGlyph, bare: true));
            mid.Children.Add(row);
            var prog = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
            Text(timeNow, 11, FontWeights.Normal, 150); Text(timeAll, 11, FontWeights.Normal, 150);
            timeNow.Width = timeAll.Width = 44; timeNow.TextAlignment = TextAlignment.Right; timeAll.TextAlignment = TextAlignment.Left;
            timeNow.Margin = new Thickness(0, 0, 8, 0); timeAll.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(timeNow, Dock.Left); DockPanel.SetDock(timeAll, Dock.Right);
            prog.Children.Add(timeNow); prog.Children.Add(timeAll);
            Slider(seek, seekFill, seekHead);
            prog.Children.Add(seek);
            seek.MouseLeftButtonDown += (s, e) => { dragSeek = true; CaptureMouse(); SeekTo(e.GetPosition(seek).X); e.Handled = true; };
            MouseMove += (s, e) => { if (dragSeek) SeekTo(e.GetPosition(seek).X); if (dragVol) VolTo(e.GetPosition(vol).X); };
            mid.Children.Add(prog);
            Grid.SetColumn(mid, 1); g.Children.Add(mid);

            // right: volume
            var right = new DockPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Width = 170 };
            var mute = Round("", 34, () => P.Volume = P.Volume > 0 ? 0 : 70, "Volume", volGlyph, bare: true);
            DockPanel.SetDock(mute, Dock.Left); right.Children.Add(mute);
            Slider(vol, volFill, volHead);
            vol.Margin = new Thickness(6, 0, 0, 0);
            vol.MouseLeftButtonDown += (s, e) => { dragVol = true; CaptureMouse(); VolTo(e.GetPosition(vol).X); e.Handled = true; };
            right.Children.Add(vol);
            Grid.SetColumn(right, 2); g.Children.Add(right);

            bar.Child = g;
            bar.CornerRadius = new CornerRadius(20);
            bar.BorderThickness = new Thickness(1);
            bar.BorderBrush = new LinearGradientBrush(Color.FromArgb(90, 255, 255, 255), Color.FromArgb(25, 255, 255, 255), 90);
            bar.Background = new LinearGradientBrush(Color.FromArgb(70, 40, 42, 52), Color.FromArgb(110, 12, 13, 20), 90);
            bar.Effect = new DropShadowEffect { ShadowDepth = 8, BlurRadius = 40, Opacity = 0.5, Color = Colors.Black };
            bar.RenderTransform = barMove;
        }

        static void Slider(Grid host, Rectangle fill, Ellipse head)
        {
            host.Children.Add(new Rectangle { Height = 3, RadiusX = 1.5, RadiusY = 1.5, Fill = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center });
            fill.VerticalAlignment = VerticalAlignment.Center; head.VerticalAlignment = VerticalAlignment.Center;
            host.Children.Add(fill); host.Children.Add(head);
        }

        void SeekTo(double x) { double d = shell.Player.Duration; if (d > 0) shell.Player.Seek(Math.Max(0, Math.Min(1, x / Math.Max(1, seek.ActualWidth))) * d); Poke(); }
        void VolTo(double x) { shell.Player.Volume = (int)Math.Round(Math.Max(0, Math.Min(1, x / Math.Max(1, vol.ActualWidth))) * 100); Poke(); }

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
                case Key.V: ToggleLook(); break;
                default: return;
            }
            Poke();
            e.Handled = true;
        }

        void Poke() { lastMove = DateTime.Now; Mouse.OverrideCursor = null; }

        // the cover look (the sky and colors from the cover, calm lyrics) or deep space
        void ToggleLook()
        {
            shell.Prefs.StageLook = CoverLook ? "space" : "cover";
            try { shell.Prefs.Save(); } catch { }
            lookGen++;
            song?.Dispose(); song = null; loading = null; askedFor = null;
            pal = spacePal;
            if (!CoverLook) { skyB.Source = spaceSky; skyB.Opacity = 0; skyFade = 0; }
            shown = null;
        }

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

        public static BitmapSource ToSource(System.Drawing.Bitmap img)
        {
            if (img == null) return null;
            try
            {
                using (var copy = new System.Drawing.Bitmap(img.Width, img.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
                {
                    using (var g = System.Drawing.Graphics.FromImage(copy)) g.DrawImage(img, 0, 0, img.Width, img.Height);
                    var bd = copy.LockBits(new System.Drawing.Rectangle(0, 0, copy.Width, copy.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                    var c = BitmapSource.Create(copy.Width, copy.Height, 96, 96, PixelFormats.Pbgra32, null, bd.Scan0, bd.Stride * copy.Height, bd.Stride);
                    copy.UnlockBits(bd);
                    c.Freeze();
                    return c;
                }
            }
            catch (Exception e) { App.Log("full screen picture: " + e.Message); return null; }
        }

        static readonly Dictionary<long, BitmapSource> thumbs = new Dictionary<long, BitmapSource>();
        static BitmapSource Thumb(Track t)
        {
            if (thumbs.TryGetValue(t.Id, out var c)) return c;
            var b = Art.Get(t);
            if (b == null) return null;
            c = ToSource(b);
            if (c == null) return null;
            if (thumbs.Count > 60) thumbs.Clear();
            thumbs[t.Id] = c;
            return c;
        }

        // the song's big cover, colors and sky (made on a worker thread)
        Track askedFor; DateTime askedAt;
        void Want(Track t)
        {
            if (t == null || loading?.T == t || song?.T == t) return;
            var small = Art.Get(t);
            if (askedFor != t) { askedFor = t; askedAt = DateTime.Now; }
            // not loaded yet: ask again next frame (but don't wait on it for long: the big one is read straight from the song)
            if (small == null && !Art.Missing(t) && (DateTime.Now - askedAt).TotalSeconds < 1.5) return;
            double dpi = ScreenDpi() / 96.0;
            int coverPx = (int)Math.Max(320, Math.Min(1200, ActualHeight * 0.3 * dpi));
            int gen = lookGen;
            loading = StageSong.Make(t, small, coverPx, 480, 270, s =>
            {
                if (loading == s) loading = null;
                if (!IsLoaded || shell.Player.Current != s.T || gen != lookGen) { s.Dispose(); return; }
                var old = song; song = s; old?.Dispose();
                cover.Source = ToSource(s.Cover) ?? Thumb(t);
                if (CoverLook && s.Sky != null)
                {
                    skyB.Source = ToSource(s.Sky); skyB.Opacity = 0; skyFade = 0;
                    pal = s.Pal.Select(LyricsView.C).ToArray();
                }
            }, a => Dispatcher.BeginInvoke(a), CoverLook);
        }

        void Frame(object sender, EventArgs e)
        {
            double now = clock.Elapsed.TotalSeconds;
            if (now - last < (App.Lite ? 1 / 31.0 : 1 / 121.0)) return;          // up to 120 frames a second (30 in performance mode); more only costs power
            var sw = System.Diagnostics.Stopwatch.StartNew();
            double dt = Math.Max(0.001, Math.Min(0.1, now - last));
            last = now;
            try { Step(now, dt); }
            catch (Exception x) { App.Log("full screen: " + x); }
            FrameMs = FrameMs * 0.9 + sw.Elapsed.TotalMilliseconds * 0.1;
        }

        static void Put(UIElement el, RectangleF r) { WCanvas.SetLeft(el, r.X); WCanvas.SetTop(el, r.Y); if (el is FrameworkElement f) { f.Width = Math.Max(0, r.Width); f.Height = Math.Max(0, r.Height); } }
        struct RectangleF { public double X, Y, Width, Height; public RectangleF(double x, double y, double w, double h) { X = x; Y = y; Width = w; Height = h; } }
        static RectangleF R(System.Drawing.RectangleF a, System.Drawing.RectangleF b, double k) => new RectangleF(a.X + (b.X - a.X) * k, a.Y + (b.Y - a.Y) * k, a.Width + (b.Width - a.Width) * k, a.Height + (b.Height - a.Height) * k);

        void Step(double now, double dt)
        {
            var P = shell.Player;
            var t = P.Current;
            double pos = P.SmoothPosition, W = ActualWidth, H = ActualHeight;
            if (W < 50 || H < 50) return;
            if (t != shown)
            {
                if (shown != null && t != null) cosmos.Warp();
                shown = t; songFade = 0;
                lyrics.Set(t == null ? null : shell.Lyrics.Get(t), t?.Id ?? -1);
                if (t != null)
                {
                    shell.Sound.Want(t);
                    title.Text = t.Name;
                    var al = t.Album; if (t.Era.Length > 0 && t.Era != t.Album) al += (al.Length > 0 ? "  ·  " : "") + t.Era;
                    if (CoverLook)
                    {
                        artist.Text = t.ArtistName;
                        album.Text = string.Join(" ", al.ToUpperInvariant().ToCharArray());
                        title.FontWeight = FontWeights.ExtraBold;
                    }
                    else
                    {
                        artist.Text = string.Join(" ", t.ArtistName.ToUpperInvariant().ToCharArray());
                        album.Text = al;
                        title.FontWeight = FontWeights.Bold;
                    }
                    barTitle.Text = t.Name; barArtist.Text = t.ArtistName;
                    if (song?.T != t) cover.Source = Thumb(t);       // the small one until the big one is ready
                }
            }
            Want(t);
            if (t != null && cover.Source == null) cover.Source = Thumb(t);
            if (t != null && barCover.Source == null || barCover.Tag != (object)t) { var th = t == null ? null : Thumb(t); if (th != null || t == null) { barCover.Source = th; barCover.Tag = t; } }

            cosmos.Step((float)dt, t == null ? -1 : Loudness(t, pos), P.IsPlaying, (float)W, (float)H);
            songFade += (1 - songFade) * Ease(dt, 3.5);
            intro.Opacity = Math.Max(0, intro.Opacity - dt * 1.6);
            if (intro.Opacity <= 0.01 && intro.Visibility == Visibility.Visible) intro.Visibility = Visibility.Collapsed;
            bool showUi = (DateTime.Now - lastMove).TotalSeconds < 3 || dragSeek || dragVol;
            ui += ((showUi ? 1 : 0) - ui) * Ease(dt, showUi ? 10 : 3);
            if (!showUi && IsActive && Mouse.OverrideCursor == null) Mouse.OverrideCursor = Cursors.None;
            else if (!IsActive && Mouse.OverrideCursor != null) Mouse.OverrideCursor = null;

            bool has = t != null && lyrics.Lyrics != null;
            side += ((has ? 1 : 0) - side) * Ease(dt, 4);
            if (Math.Abs(side - (has ? 1 : 0)) < 0.002) side = has ? 1 : 0;      // settle exactly, so nothing keeps moving by tiny amounts
            bool look = CoverLook, lite = App.Lite;
            var L0 = look ? Cosmos.CoverPlace((float)W, (float)H, false) : Cosmos.Place((float)W, (float)H, false);
            var L1 = look ? Cosmos.CoverPlace((float)W, (float)H, true) : Cosmos.Place((float)W, (float)H, true);
            double cx = L0.Cx + (L1.Cx - L0.Cx) * side, cy = L0.Cy + (L1.Cy - L0.Cy) * side, S = L0.S + (L1.S - L0.S) * side;
            var c0 = pal[0];

            // the sky drifts like a floating camera; a new one fades in
            Put(skyA, new RectangleF(0, 0, W, H)); Put(skyB, new RectangleF(0, 0, W, H));
            if (lite) { skyMove.X = skyMove.Y = 0; } else { skyMove.X = Math.Sin(now * 0.031) * W * 0.012; skyMove.Y = Math.Cos(now * 0.023) * H * 0.01; }
            if (skyFade < 1)
            {
                skyFade = Math.Min(1, skyFade + dt / 1.2);
                skyB.Opacity = skyFade;
                if (skyFade >= 1) { skyA.Source = skyB.Source; skyA.Opacity = 1; skyB.Opacity = 0; skyB.Source = null; }
            }
            flash.Width = W; flash.Height = H; intro.Width = W; intro.Height = H;
            flash.Opacity = look || lite ? 0 : cosmos.Peak * cosmos.Peak * 0.35;
            if (flash.Opacity > 0.005) flashBrush.Color = LyricsView.C(Theme.Mix(DColor.FromArgb(c0.R, c0.G, c0.B), DColor.White, 0.6f));

            // streaks and dots
            var sceneVis = look || lite ? Visibility.Collapsed : Visibility.Visible;
            if (scene.Visibility != sceneVis) scene.Visibility = sceneVis;
            if (sceneVis == Visibility.Visible) { Put(scene, new RectangleF(0, 0, W, H)); scene.Update(cosmos, cx, cy, pal); }

            if (t == null) { foreach (var el in new UIElement[] { glow, ring, ringHalo1, ringHalo2, coverShadow, coverBox, date, clockText, title, artist, album, hint, lyrics, upNext, bar }) el.Visibility = Visibility.Hidden; return; }
            foreach (var el in new UIElement[] { glow, ring, ringHalo1, ringHalo2, coverShadow, coverBox, title, artist, album, upNext, bar }) el.Visibility = Visibility.Visible;
            date.Visibility = clockText.Visibility = Visibility.Collapsed;
            if (look || lite) { ring.Visibility = ringHalo1.Visibility = ringHalo2.Visibility = Visibility.Hidden; }
            if (look) upNext.Visibility = Visibility.Hidden;

            // glow, ring and cover
            double gs = S * 2.6 * (1 + cosmos.Energy * 0.12);
            Put(glow, new RectangleF(cx - gs / 2, cy - gs / 2, gs, gs));
            glowBrush.GradientStops[0].Color = Color.FromArgb(255, c0.R, c0.G, c0.B);
            glowBrush.GradientStops[1].Color = Color.FromArgb(80, c0.R, c0.G, c0.B);
            glowBrush.GradientStops[2].Color = Color.FromArgb(0, c0.R, c0.G, c0.B);
            glow.Opacity = Math.Min(1, 0.22 + cosmos.Energy * 0.3 + cosmos.Kick * 0.25 + cosmos.Peak * 0.3);
            if (look || lite)
            {
                // a steady soft glow of the cover's color behind it
                gs = S * (look ? 1.9 : 2.4);
                Put(glow, new RectangleF(cx - gs / 2, cy - gs / 2, gs, gs));
                glow.Opacity = look ? 0.32 * songFade : 0.25;
            }
            double rs = S * 1.34 * (1 + cosmos.Kick * 0.04);
            Put(ring, new RectangleF(cx - rs / 2, cy - rs / 2, rs, rs));
            Put(ringHalo1, new RectangleF(cx - rs / 2 - 3, cy - rs / 2 - 3, rs + 6, rs + 6));
            Put(ringHalo2, new RectangleF(cx - rs / 2 - 9, cy - rs / 2 - 9, rs + 18, rs + 18));
            var rc = LyricsView.C(Theme.Mix(DColor.FromArgb(c0.R, c0.G, c0.B), DColor.White, 0.25f));
            double ringA = Math.Min(1, cosmos.Kick + cosmos.Energy * 0.4);
            ringBrush.Color = Color.FromArgb((byte)(150 + 80 * ringA), rc.R, rc.G, rc.B);
            halo1Brush.Color = Color.FromArgb((byte)(30 + 40 * ringA), rc.R, rc.G, rc.B);
            halo2Brush.Color = Color.FromArgb((byte)(10 + 22 * ringA), rc.R, rc.G, rc.B);
            accentBrush.Color = rc;
            Put(coverShadow, new RectangleF(cx - S * 0.75, cy - S * 0.68, S * 1.5, S * 1.6));
            Put(coverBox, new RectangleF(cx - S / 2, cy - S / 2, S, S));
            if (Math.Abs(clipFor - S) > 0.5)
            {
                clipFor = S;
                var clip = new RectangleGeometry(new Rect(0, 0, S, S), S * 0.06, S * 0.06); clip.Freeze();
                coverBox.Clip = clip;
            }
            if (cover.Source == null) coverBox.Background = new SolidColorBrush(Color.FromArgb(60, c0.R, c0.G, c0.B));
            else if (coverBox.Background != null) coverBox.Background = null;
            double pulse = 1 + cosmos.Energy * 0.02 + cosmos.Kick * 0.015;
            if (lite) pulse = 1;
            else if (look) pulse = (0.94 + 0.06 * songFade) * (1 + (P.IsPlaying ? 0.012 * Math.Sin(now * 2 * Math.PI / 5) : 0));     // pops in, then breathes slowly
            coverScale.ScaleX = coverScale.ScaleY = pulse;

            // the song's name
            var tr = R(L0.Title, L1.Title, side); Put(title, tr); title.FontSize = tr.Height * (look ? 0.64 : 0.6);
            var ar = R(L0.Artist, L1.Artist, side); Put(artist, ar); artist.FontSize = Math.Max(11, ar.Height * (look ? 0.56 : 0.46));
            var alr = R(L0.Album, L1.Album, side); Put(album, alr); album.FontSize = Math.Max(10.5, alr.Height * (look ? 0.46 : 0.5));
            var align = look && side > 0.5 ? TextAlignment.Left : TextAlignment.Center;
            if (title.TextAlignment != align) title.TextAlignment = artist.TextAlignment = album.TextAlignment = align;
            title.Opacity = artist.Opacity = album.Opacity = songFade;
            hint.Visibility = has || side > 0.5 ? Visibility.Hidden : Visibility.Visible;
            Put(hint, new RectangleF(alr.X, alr.Y + alr.Height + 6, alr.Width, 22)); hint.Opacity = songFade * (1 - side);

            // lyrics, on the right
            lyrics.Visibility = has ? Visibility.Visible : Visibility.Hidden;
            if (has)
            {
                Put(lyrics, R(L1.Lyrics, L1.Lyrics, 0));
                lyrics.Calm = look;
                lyrics.Glow = LyricsView.C(Theme.Mix(DColor.FromArgb(c0.R, c0.G, c0.B), DColor.White, look ? 0.3f : 0.5f));
                lyrics.Duration = P.Duration;
                lyrics.Fade = songFade * side;
                lyrics.Step(pos, dt);
            }

            // up next
            var un = R(L0.UpNext, L1.UpNext, side);
            WCanvas.SetLeft(upNext, un.X); WCanvas.SetTop(upNext, un.Y);
            if (!look) UpdateUpNext(L1.CardW, L1.CardH, L1.CardGap);

            // the bar (slides down when the mouse rests), and close
            var br = L1.Bar;
            Put(bar, new RectangleF(br.X, br.Y, br.Width, br.Height));
            barMove.Y = (1 - ui) * (br.Height + 80);
            bar.Opacity = Math.Min(1, ui * 1.4);
            bar.IsHitTestVisible = ui > 0.3;
            WCanvas.SetLeft(closeBtn, W - 88); WCanvas.SetTop(closeBtn, 30);
            closeBtn.Opacity = ui; closeBtn.IsHitTestVisible = ui > 0.3;
            WCanvas.SetLeft(lookBtn, W - 144); WCanvas.SetTop(lookBtn, 30);
            lookBtn.Opacity = ui; lookBtn.IsHitTestVisible = ui > 0.3;
            Brush lb = look ? accentBrush : Brushes.White; if (lookGlyph.Foreground != lb) lookGlyph.Foreground = lb;
            playGlyph.Text = P.IsPlaying ? "" : "";
            favGlyph.Text = t.Fav ? "" : "";
            Brush want = t.Fav ? accentBrush : Brushes.White; if (favGlyph.Foreground != want) favGlyph.Foreground = want;
            want = P.Shuffle ? accentBrush : dimBrush; if (shuffleGlyph.Foreground != want) shuffleGlyph.Foreground = want;
            repeatGlyph.Text = P.Repeat == 2 ? "" : "";
            want = P.Repeat > 0 ? accentBrush : dimBrush; if (repeatGlyph.Foreground != want) repeatGlyph.Foreground = want;
            volGlyph.Text = P.Volume <= 0 ? "" : "";
            double d = P.Duration, k = d > 0 ? Math.Max(0, Math.Min(1, pos / d)) : 0;
            timeNow.Text = Theme.Time(pos); timeAll.Text = Theme.Time(d);
            double sw = seek.ActualWidth;
            seekFill.Width = Math.Max(0, sw * k); seekHead.Margin = new Thickness(sw * k - 5.5, 0, 0, 0);
            seekBrush.GradientStops[0].Color = Color.FromArgb(255, c0.R, c0.G, c0.B); seekBrush.GradientStops[1].Color = rc;
            if (seekFill.Fill != seekBrush) seekFill.Fill = seekBrush;
            double vw = vol.ActualWidth, vk = P.Volume / 100.0;
            volFill.Width = Math.Max(0, vw * vk); volHead.Margin = new Thickness(vw * vk - 5.5, 0, 0, 0);
            if (volFill.Fill != accentBrush) volFill.Fill = accentBrush;
        }

        // glass cards with what plays next, leaning back a little; click one to play it
        void UpdateUpNext(double cw, double ch, double gap)
        {
            var P = shell.Player;
            var ids = new List<long>();
            for (int i = Math.Max(0, P.Pos); i < P.Order.Count && ids.Count < 5; i++) ids.Add(P.Queue[P.Order[i]]);
            int first = Math.Max(0, P.Pos);
            string key = string.Join(",", ids) + "|" + cw.ToString("0") + "|" + (P.Current?.Fav ?? false);
            if (key == upNextKey) return;
            upNextKey = key;
            while (upNext.Children.Count > 1) upNext.Children.RemoveAt(1);
            upNextLabel.Margin = new Thickness(0, 0, 0, 8);
            upNextLabel.Visibility = ids.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            for (int i = 0; i < ids.Count; i++)
            {
                var tr = shell.Lib.Get(ids[i]);
                if (tr == null) continue;
                bool now = i == 0;
                double w = cw * (now ? 1.08 : 0.94), h = ch * (now ? 1.05 : 0.94);
                var thumb = new Border { Width = h - 12, Height = h - 12, CornerRadius = new CornerRadius(6), ClipToBounds = true, Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), Child = new Image { Source = Thumb(tr), Stretch = Stretch.UniformToFill } };
                var t1 = new TextBlock { Text = tr.Name, FontFamily = Family, FontSize = now ? 13.5 : 12.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromArgb((byte)(now ? 240 : 205), 255, 255, 255)), TextTrimming = TextTrimming.CharacterEllipsis };
                var t2 = new TextBlock { Text = tr.ArtistName, FontFamily = Family, FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), TextTrimming = TextTrimming.CharacterEllipsis };
                var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) };
                texts.Children.Add(t1); texts.Children.Add(t2);
                var row = new DockPanel(); DockPanel.SetDock(thumb, Dock.Left); row.Children.Add(thumb); row.Children.Add(texts);
                var ac = pal[0];
                var card = new Border
                {
                    Width = w, Height = h, CornerRadius = new CornerRadius(10), Padding = new Thickness(6), Child = row,
                    Margin = new Thickness(now ? 10 : 0, 0, 0, gap),
                    Background = new LinearGradientBrush(Color.FromArgb((byte)(now ? 46 : 30), 255, 255, 255), Color.FromArgb(12, 255, 255, 255), 90),
                    BorderThickness = new Thickness(1),
                    BorderBrush = now ? (Brush)new SolidColorBrush(Color.FromArgb(150, ac.R, ac.G, ac.B)) : new SolidColorBrush(Color.FromArgb(55, 255, 255, 255)),
                    RenderTransform = new SkewTransform(0, -3), RenderTransformOrigin = new Point(0, 0.5),
                    Effect = new DropShadowEffect { ShadowDepth = 6, BlurRadius = 24, Opacity = 0.35, Color = Colors.Black },
                };
                if (!now)
                {
                    int k = first + i - P.Pos - 1;      // JumpTo counts from the song after the one playing
                    card.Cursor = Cursors.Hand; card.ToolTip = "Play " + tr.Name;
                    var idle = card.Background;
                    card.MouseEnter += (s, e) => card.Background = new LinearGradientBrush(Color.FromArgb(64, 255, 255, 255), Color.FromArgb(30, 255, 255, 255), 90);
                    card.MouseLeave += (s, e) => card.Background = idle;
                    card.MouseLeftButtonUp += (s, e) => { P.JumpTo(k); Poke(); e.Handled = true; };
                }
                upNext.Children.Add(card);
            }
        }

        // ---------- light streaks and glowing dots, drawn each frame ----------
        sealed class Scene : FrameworkElement
        {
            Cosmos c; double cx, cy;
            // everything it draws with is made once and frozen (new brushes every frame made it heavy)
            static readonly SolidColorBrush[,] dots = new SolidColorBrush[4, 17];
            static readonly Pen[,] lines = new Pen[4, 17];
            static Pen shotPen;
            static Color Tint(int t) => t < 0 ? Color.FromRgb(235, 238, 255) : LyricsView.C(Theme.Mix(Cosmos.SpacePal[t], System.Drawing.Color.White, 0.45f));
            static SolidColorBrush Dot(int tint, int a, double k = 1)
            {
                int i = tint + 1;
                if (k != 1) { var b0 = new SolidColorBrush(Color.FromArgb((byte)(a / 16.0 * 255 * k), Tint(tint).R, Tint(tint).G, Tint(tint).B)); b0.Freeze(); return b0; }
                var b = dots[i, a];
                if (b == null) { var c = Tint(tint); b = new SolidColorBrush(Color.FromArgb((byte)(a / 16.0 * 255), c.R, c.G, c.B)); b.Freeze(); dots[i, a] = b; }
                return b;
            }
            static readonly SolidColorBrush[,] halos = new SolidColorBrush[4, 17], halos2 = new SolidColorBrush[4, 17];
            static SolidColorBrush Halo(SolidColorBrush[,] arr, int tint, int a, double k)
            {
                var b = arr[tint + 1, a];
                if (b == null) { var c = Tint(tint); b = new SolidColorBrush(Color.FromArgb((byte)(a / 16.0 * 255 * k), c.R, c.G, c.B)); b.Freeze(); arr[tint + 1, a] = b; }
                return b;
            }
            static Pen Line(int tint, int a)
            {
                var p = lines[tint + 1, a];
                if (p == null) { p = new Pen(Halo(new SolidColorBrush[4, 17], tint, a, 0.35), 1); p.Freeze(); lines[tint + 1, a] = p; }
                return p;
            }

            public void Update(Cosmos c, double cx, double cy, Color[] pal) { this.c = c; this.cx = cx; this.cy = cy; InvalidateVisual(); }

            protected override void OnRender(DrawingContext dc)
            {
                if (c == null) return;
                bool warp = c.Peak > 0.05f;
                foreach (var s in c.Stars)
                {
                    c.Place(s, (float)cx, (float)cy, out var p, out var tail, out var r, out var al);
                    int a = Math.Max(1, Math.Min(16, (int)Math.Round(al * 16)));
                    if (warp)
                    {
                        var pen = new Pen(Dot(s.Tint, Math.Min(16, a + 3)), Math.Max(1, r)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                        pen.Freeze();
                        dc.DrawLine(pen, new Point(p.X, p.Y), new Point(tail.X, tail.Y));
                        continue;
                    }
                    var pt = new Point(p.X, p.Y);
                    if (s.Depth >= 1)
                    {
                        // the bright ones get a soft glow and a little sparkle
                        dc.DrawEllipse(Halo(halos2, s.Tint, a, 0.055), null, pt, r * 3.2, r * 3.2);
                        dc.DrawEllipse(Halo(halos, s.Tint, a, 0.12), null, pt, r * 2.1, r * 2.1);
                        var lp = Line(s.Tint, a);
                        dc.DrawLine(lp, new Point(p.X - r * 3, p.Y), new Point(p.X + r * 3, p.Y));
                        dc.DrawLine(lp, new Point(p.X, p.Y - r * 3), new Point(p.X, p.Y + r * 3));
                    }
                    dc.DrawEllipse(Dot(s.Tint, a), null, pt, r, r);
                }
                foreach (var m in c.Shots)
                {
                    c.Place(m, out var head, out var tail, out var al);
                    if (al < 0.02f) continue;
                    var br = new LinearGradientBrush(Color.FromArgb(0, 255, 255, 255), Color.FromArgb((byte)(al * 230), 255, 255, 255), new Point(tail.X, tail.Y), new Point(head.X, head.Y)) { MappingMode = BrushMappingMode.Absolute };
                    br.Freeze();
                    var pen = new Pen(br, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }; pen.Freeze();
                    dc.DrawLine(pen, new Point(tail.X, tail.Y), new Point(head.X, head.Y));
                    dc.DrawEllipse(Dot(-1, Math.Max(1, (int)(al * 16))), null, new Point(head.X, head.Y), 2.6, 2.6);
                }
            }
        }
    }
}
#endif
