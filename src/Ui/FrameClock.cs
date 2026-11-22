using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Aura
{
    // Smooth animation: one frame each time the screen refreshes (Windows tells us through DwmFlush),
    // instead of a timer that drifts and stutters. Frames are skipped, never queued up, when drawing is slow.
    sealed class FrameClock : IDisposable
    {
        [DllImport("dwmapi.dll")] static extern int DwmFlush();

        readonly Control target;
        readonly Func<bool> wanted;
        readonly Stopwatch sw = Stopwatch.StartNew();
        Thread thread;
        volatile bool stop, pending;
        volatile float lastCost;          // how long the last frame took, ms
        double last;
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        public float Dt { get; private set; } = 1 / 60f;     // seconds since the last frame (capped)
        public double Now => sw.Elapsed.TotalSeconds;
        public event Action Frame;

        // wanted: whether to animate right now (when not, it checks again 4 times a second)
        public FrameClock(Control target, Func<bool> wanted)
        {
            this.target = target; this.wanted = wanted;
            thread = new Thread(Run) { IsBackground = true, Name = "frames", Priority = ThreadPriority.AboveNormal };
            thread.Start();
        }

        void Run()
        {
            bool dwm = App.IsWindows;
            while (!stop)
            {
                bool on;
                try { on = target.IsHandleCreated && !target.IsDisposed && wanted(); } catch { on = false; }
                if (!on) { wake.WaitOne(250); continue; }
                if (dwm) { try { if (DwmFlush() < 0) Thread.Sleep(15); } catch { dwm = false; } }
                else Thread.Sleep(16);
                if (pending || stop) continue;
                // a slow frame leaves the window some room for everything else (clicks, timers) before the next
                if (lastCost > 10) Thread.Sleep((int)Math.Min(40, lastCost / 3));
                pending = true;
                try { target.BeginInvoke(new Action(Tick)); } catch { pending = false; Thread.Sleep(50); }
            }
        }

        void Tick()
        {
            pending = false;
            if (target.IsDisposed) return;
            double now = sw.Elapsed.TotalSeconds;
            Dt = (float)Math.Max(0.001, Math.Min(0.1, now - last));
            last = now;
            Frame?.Invoke();
            target.Invalidate();
            target.Update();          // draw now, so the frame lands on this refresh
            lastCost = (float)((sw.Elapsed.TotalSeconds - now) * 1000);
        }

        public void Nudge() => wake.Set();

        public void Dispose() { stop = true; wake.Set(); }

        // frame-rate independent easing: how far to move toward a target this frame
        public static float Ease(float dt, float speed) => 1 - (float)Math.Exp(-dt * speed);
    }
}
