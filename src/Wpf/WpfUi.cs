#if WPF
using System;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Aura.Wpf
{
    // The door between the WinForms app and the WPF (graphics card) parts. If WPF can't start for any
    // reason, AURA falls back to its older drawing, so nothing breaks.
    static class WpfUi
    {
        static bool tried, ok;
        public static bool Available
        {
            get
            {
                if (tried) return ok;
                tried = true;
                if (!App.IsWindows || Environment.GetEnvironmentVariable("AURA_NO_WPF") != null) return ok = false;
                try
                {
                    var tier = RenderCapability.Tier >> 16;
                    App.Log("graphics card drawing (WPF): tier " + tier);
                    if (tier < 2) Why = "Windows says this graphics card can only partly help (tier " + tier + ")";
                    ok = true;
                }
                catch (Exception e) { App.Log("WPF isn't available: " + e.Message); ok = false; Why = e.Message; }
                return ok;
            }
        }

        public static string Why = "";
        public static void Failed(Exception e) { App.Log("WPF failed, using the older drawing: " + e); ok = false; Why = e.Message; }

        // full-screen lyrics; returns something that can be closed, or null if it couldn't open
        public static Action OpenStage(IShell shell, Form owner, Screen screen, Action closed)
        {
            try
            {
                var w = new Stage(shell, screen);
                new WindowInteropHelper(w).Owner = owner.Handle;
                ElementHost.EnableModelessKeyboardInterop(w);
                w.Closed += (s, e) => closed();
                w.Show();
                w.Activate();
                return w.Close;
            }
            catch (Exception e) { Failed(e); return null; }
        }

        public static bool StageOpen => Stage.Open > 0;

        // for the release build's self-test: lay out and draw lyrics off screen, and time it
        public static string SelfTest()
        {
            var lrc = "";
            for (int i = 0; i < 40; i++) lrc += "[00:" + (i * 1.5).ToString("00.00", System.Globalization.CultureInfo.InvariantCulture) + "]line number " + i + " of the test song, a little long so it wraps around\n";
            var l = Lyrics.From(lrc, null, null, "test");
            var v = new LyricsView { Stage = true, Width = 1600, Height = 800 };
            v.Set(l, 1);
            v.Measure(new System.Windows.Size(1600, 800)); v.Arrange(new System.Windows.Rect(0, 0, 1600, 800));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 120; i++) { v.Step(i / 20.0, 1 / 60.0); v.UpdateLayout(); }
            double stepMs = sw.Elapsed.TotalMilliseconds / 120;
            var rtb = new RenderTargetBitmap(1600, 800, 96, 96, PixelFormats.Pbgra32);
            sw.Restart();
            rtb.Render(v);
            double renderMs = sw.Elapsed.TotalMilliseconds;
            using (var sky = Space.Paint(new Size(640, 360), System.Drawing.Color.FromArgb(40, 160, 255))) { }
            return "ok (tier " + (RenderCapability.Tier >> 16) + ", " + stepMs.ToString("0.00") + " ms a frame to update, " + renderMs.ToString("0") + " ms to draw once in software)";
        }
    }

    // The lyrics in Now Playing, hosted inside the WinForms page. Its background is the matching piece
    // of the page's backdrop, so it blends in.
    sealed class LyricsHost : ElementHost
    {
        public readonly LyricsView View = new LyricsView();
        readonly System.Windows.Controls.Grid root = new System.Windows.Controls.Grid();
        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        double last;
        public Func<double> Position;
        public Func<bool> Moving;
        bool hooked;

        public LyricsHost()
        {
            root.Children.Add(View);
            Child = root;
            BackColorTransparent = false;
        }

        // the piece of the backdrop behind this area
        public void SetBackground(Bitmap backdrop, Rectangle area)
        {
            if (backdrop == null || area.Width < 2 || area.Height < 2) return;
            var r = Rectangle.Intersect(area, new Rectangle(0, 0, backdrop.Width, backdrop.Height));
            if (r.Width < 2 || r.Height < 2) return;
            using (var crop = backdrop.Clone(r, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
            {
                var bd = crop.LockBits(new Rectangle(0, 0, crop.Width, crop.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                var src = BitmapSource.Create(crop.Width, crop.Height, 96, 96, PixelFormats.Pbgra32, null, bd.Scan0, bd.Stride * crop.Height, bd.Stride);
                crop.UnlockBits(bd);
                src.Freeze();
                var b = new ImageBrush(src) { Stretch = Stretch.Fill };
                b.Freeze();
                root.Background = b;
            }
        }

        public void SetSolid(System.Drawing.Color c) => root.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(c.R, c.G, c.B));

        // animate only while something moves (the song plays): saves the processor otherwise
        public void Run(bool on)
        {
            if (on == hooked) return;
            hooked = on;
            if (on) { last = clock.Elapsed.TotalSeconds; CompositionTarget.Rendering += Frame; }
            else CompositionTarget.Rendering -= Frame;
        }

        public void StepOnce() => Frame(null, EventArgs.Empty);

        void Frame(object s, EventArgs e)
        {
            if (IsDisposed) { Run(false); return; }
            double now = clock.Elapsed.TotalSeconds;
            // 60 frames a second is plenty for lyrics beside the cover; on a 144 or 240 Hz screen the rest are skipped
            // (when nothing changes WPF draws nothing, so skipped frames cost the graphics card nothing)
            if (now - last < (App.Lite ? 1 / 31.0 : 1 / 62.0)) return;
            double dt = Math.Max(0.001, Math.Min(0.1, now - last));
            last = now;
            try { View.Step(Position?.Invoke() ?? 0, dt); }
            catch (Exception x) { App.Log("lyrics: " + x.Message); }
        }

        protected override void Dispose(bool disposing) { if (disposing) Run(false); base.Dispose(disposing); }
    }
}
#endif
