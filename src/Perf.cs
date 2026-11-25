using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Aura
{
    // How hard AURA is working, and which part of it: shown in Settings → Performance, and copied as a report.
    // Each of AURA's own threads says who it is when it starts (Here); everything else in the process
    // (Windows audio, the graphics card drawing, .NET itself) is counted together.
    static class Perf
    {
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] static extern uint GetGuiResources(IntPtr process, uint flags);

        static readonly Dictionary<int, string> names = new Dictionary<int, string>();
        public static void Here(string name)
        {
            if (!App.IsWindows) return;
            try { lock (names) names[(int)GetCurrentThreadId()] = name; } catch { }
        }

        public sealed class Sample
        {
            public double Cpu;                                  // % of the whole computer (like Task Manager)
            public List<(string part, double cpu)> Parts = new List<(string, double)>();
            public long Private, Working, Managed;
            public int Handles, Gdi, User, Threads;
            public DateTime At;
        }

        static Dictionary<int, TimeSpan> lastThreads = new Dictionary<int, TimeSpan>();
        static TimeSpan lastTotal; static DateTime lastAt;
        public static Sample Last;

        // call every second or two
        public static Sample Take()
        {
            var s = new Sample { At = DateTime.Now };
            try
            {
                var p = Process.GetCurrentProcess();
                p.Refresh();
                double wall = lastAt == default ? 0 : (s.At - lastAt).TotalSeconds;
                int cores = Environment.ProcessorCount;
                var total = p.TotalProcessorTime;
                if (wall > 0.2) s.Cpu = (total - lastTotal).TotalSeconds / wall / cores * 100;
                s.Private = p.PrivateMemorySize64; s.Working = p.WorkingSet64; s.Managed = GC.GetTotalMemory(false);
                s.Handles = p.HandleCount; s.Threads = p.Threads.Count;
                if (App.IsWindows) { try { s.Gdi = (int)GetGuiResources(p.Handle, 0); s.User = (int)GetGuiResources(p.Handle, 1); } catch { } }
                var now = new Dictionary<int, TimeSpan>();
                var byPart = new Dictionary<string, double>();
                foreach (ProcessThread th in p.Threads)
                {
                    TimeSpan t;
                    try { t = th.TotalProcessorTime; } catch { continue; }
                    now[th.Id] = t;
                    if (wall > 0.2 && lastThreads.TryGetValue(th.Id, out var before))
                    {
                        string part; lock (names) if (!names.TryGetValue(th.Id, out part)) part = "other (speech model, sound output, graphics card, .NET)";
                        byPart[part] = (byPart.TryGetValue(part, out var v) ? v : 0) + (t - before).TotalSeconds / wall / cores * 100;
                    }
                }
                lastThreads = now;
                s.Parts = byPart.Where(kv => kv.Value >= 0.05).OrderByDescending(kv => kv.Value).Select(kv => (kv.Key, kv.Value)).ToList();
                lastTotal = total; lastAt = s.At;
            }
            catch (Exception e) { App.Log("perf: " + e.Message); }
            Last = s;
            return s;
        }

        public static string Mb(long bytes) => (bytes / 1048576.0).ToString("N0") + " MB";

        public static string Report(MainForm f)
        {
            var s = Last ?? Take();
            var b = new StringBuilder();
            b.AppendLine("AURA Native " + App.Version + " — performance, " + DateTime.Now.ToString("g"));
            b.AppendLine("Processor: " + s.Cpu.ToString("0.0") + "% of the computer (" + Environment.ProcessorCount + " threads)");
            foreach (var (part, cpu) in s.Parts) b.AppendLine("  " + part + ": " + cpu.ToString("0.0") + "%");
            b.AppendLine("Memory: " + Mb(s.Private) + " private, " + Mb(s.Working) + " in use, .NET " + Mb(s.Managed));
            b.AppendLine("Handles " + s.Handles + ", GDI " + s.Gdi + ", USER " + s.User + ", threads " + s.Threads);
            try
            {
                b.AppendLine("Songs: " + f.Lib.Tracks.Count.ToString("N0") + ", heard " + f.Sound.Done.ToString("N0") + (f.Sound.Current.Length > 0 ? " (now: " + f.Sound.Current + ")" : "") + (f.Sound.Paused ? ", listening paused" : ""));
                b.AppendLine("Covers in memory: " + Art.InMemory + ", ready-made sizes: " + Theme.ScaledCount + " (" + Mb(Theme.ScaledBytes) + "), fonts " + Theme.FontCount);
                b.AppendLine("Page: " + (f.Page?.GetType().Name ?? "-") + ", playing: " + (f.Player.IsPlaying ? "yes" : "no") + ", window: " + f.WindowState);
#if WPF
                b.AppendLine("Drawing: " + (Aura.Wpf.WpfUi.Available ? "graphics card" : "older drawing") + (Aura.Wpf.WpfUi.Why.Length > 0 ? " (" + Aura.Wpf.WpfUi.Why + ")" : "") + (Aura.Wpf.WpfUi.StageOpen ? ", full screen " + Aura.Wpf.Stage.FrameMs.ToString("0.0") + " ms a frame" : ""));
#else
                b.AppendLine("Drawing: older drawing");
#endif
                b.AppendLine("Settings: listen " + f.Prefs.Listen + ", time all " + f.Prefs.AutoSyncAll + ", leveling " + f.Prefs.Leveling + ", discord " + f.Prefs.Discord + ", between " + f.Prefs.Between);
                b.AppendLine("Screen: " + System.Windows.Forms.Screen.FromControl(f).Bounds.Size + " at " + (Theme.S * 100).ToString("0") + "%");
            }
            catch (Exception e) { b.AppendLine("(" + e.Message + ")"); }
            return b.ToString();
        }
    }
}
