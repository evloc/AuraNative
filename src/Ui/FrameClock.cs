using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Aura
{
    // Animation frames, driven by an ordinary window timer. That matters: Windows hands out timer ticks
    // and redraws only after clicks, keys and other work are done, so animating can never lock the
    // window up (an earlier version posted messages, which Windows puts ahead of the mouse).
    // The timer is made precise while animating, and slows down by itself if drawing takes long.
    sealed class FrameClock : IDisposable
    {
        [DllImport("winmm.dll")] static extern int timeBeginPeriod(int ms);
        [DllImport("winmm.dll")] static extern int timeEndPeriod(int ms);

        readonly Control target;
        readonly Func<bool> wanted;
        readonly Timer timer = new Timer { Interval = 16 };
        readonly Stopwatch sw = Stopwatch.StartNew();
        double lastStep;
        bool precise;
        public float Dt { get; private set; } = 1 / 60f;
        public double Now => sw.Elapsed.TotalSeconds;
        public event Action Frame;

        // wanted: whether to animate right now (checked on every tick; when not, ticks slow to 4 a second)
        public FrameClock(Control target, Func<bool> wanted)
        {
            this.target = target; this.wanted = wanted;
            timer.Tick += (s, e) => Tick();
            timer.Start();
        }

        void Tick()
        {
            if (target.IsDisposed) { Dispose(); return; }
            bool on;
            try { on = target.Visible && wanted(); } catch { on = false; }
            Precise(on);
            if (!on) { timer.Interval = 250; return; }
            // keep the window responsive: if drawing is slow, ask for fewer frames
            double cost = (target as Canvas)?.PaintMs ?? 0;
            timer.Interval = (int)Math.Max(15, Math.Min(66, cost * 1.6));
            Frame?.Invoke();
            target.Invalidate();
        }

        // call at the start of drawing a frame: seconds since the last one (capped)
        public float Step()
        {
            double now = sw.Elapsed.TotalSeconds;
            Dt = (float)Math.Max(0.001, Math.Min(0.1, now - lastStep));
            lastStep = now;
            return Dt;
        }

        void Precise(bool on)
        {
            if (on == precise || !App.IsWindows) return;
            try { if (on) timeBeginPeriod(1); else timeEndPeriod(1); precise = on; } catch { }
        }

        public void Nudge() { }

        public void Dispose()
        {
            timer.Stop(); timer.Dispose();
            Precise(false);
        }

        // frame-rate independent easing: how far to move toward a target this frame
        public static float Ease(float dt, float speed) => 1 - (float)Math.Exp(-dt * speed);
    }
}
