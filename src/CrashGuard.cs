using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Aura
{
    // When AURA closes without warning, find out why.
    //  • A small note (running.txt) says what AURA is busy with right now: the song and which player
    //    engine plays it, and any background work (timing lyrics, listening to a song, taking out vocals).
    //    A normal exit removes it, so if it's still there at the next start, AURA didn't close normally,
    //    and the note says what was going on at that moment.
    //  • A crash deep inside Windows or a helper (the speech model, a sound decoder) skips .NET's own
    //    crash handling; Windows' last-chance hook still writes the error code and the file it was in.
    static class CrashGuard
    {
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int FilterFn(IntPtr pointers);
        [DllImport("kernel32.dll")] static extern IntPtr SetUnhandledExceptionFilter(FilterFn filter);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetModuleHandleEx(uint flags, IntPtr address, out IntPtr module);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern uint GetModuleFileName(IntPtr module, StringBuilder name, int size);

        static FilterFn keep;          // kept alive: Windows holds on to it
        static IntPtr previous;
        static string file;
        static readonly object gate = new object();
        static readonly SortedDictionary<string, string> doing = new SortedDictionary<string, string>();

        public static string LastTime { get; private set; }     // what AURA was doing when it last closed without warning

        public static void Start()
        {
            try
            {
                file = Path.Combine(App.DataDir, "running.txt");
                if (File.Exists(file))
                {
                    var was = File.ReadAllText(file).Trim();
                    LastTime = was.Length > 0 ? was : "(nothing in particular)";
                    App.Log("AURA closed without warning last time. It was: " + LastTime.Replace(Environment.NewLine, "; "));
                }
                Write();
            }
            catch { }
            if (!App.IsWindows) return;
            try
            {
                keep = OnCrash;
                previous = SetUnhandledExceptionFilter(keep);
            }
            catch (Exception e) { App.Log("crash guard: " + e.Message); }
        }

        // what = a short name for the kind of work; value = what it's working on (null when it's done)
        public static void Doing(string what, string value)
        {
            lock (gate)
            {
                if (value == null) { if (!doing.Remove(what)) return; }
                else { if (doing.TryGetValue(what, out var v) && v == value) return; doing[what] = value; }
                Write();
            }
        }

        public static string Now { get { lock (gate) return string.Join("; ", doing.Select(kv => kv.Key + ": " + kv.Value)); } }

        static void Write()
        {
            if (file == null) return;
            try
            {
                var b = new StringBuilder();
                b.AppendLine("AURA " + App.Version + ", started " + started.ToString("yyyy-MM-dd HH:mm:ss") + ", noted " + DateTime.Now.ToString("HH:mm:ss"));
                foreach (var kv in doing) b.AppendLine(kv.Key + ": " + kv.Value);
                File.WriteAllText(file, b.ToString());
            }
            catch { }
        }
        static readonly DateTime started = DateTime.Now;

        // a normal exit
        public static void Stop()
        {
            try { if (file != null) File.Delete(file); } catch { }
            file = null;
        }

        static bool inCrash;
        static int OnCrash(IntPtr pointers)
        {
            if (!inCrash)
            {
                inCrash = true;
                try
                {
                    IntPtr rec = Marshal.ReadIntPtr(pointers);
                    uint code = (uint)Marshal.ReadInt32(rec);
                    IntPtr at = Marshal.ReadIntPtr(rec, 8 + IntPtr.Size);
                    string where = "?";
                    if (GetModuleHandleEx(0x4 | 0x2, at, out var mod))      // FROM_ADDRESS | UNCHANGED_REFCOUNT
                    {
                        var sb = new StringBuilder(520);
                        GetModuleFileName(mod, sb, sb.Capacity);
                        where = Path.GetFileName(sb.ToString()) + "+0x" + ((long)at - (long)mod).ToString("X");
                    }
                    App.Log("crash (inside Windows or a helper): code 0x" + code.ToString("X8") + " " + CodeName(code) + " in " + where + ". AURA was: " + Now);
                }
                catch { }
            }
            if (previous != IntPtr.Zero)
                try { return Marshal.GetDelegateForFunctionPointer<FilterFn>(previous)(pointers); } catch { }
            return 0;      // let Windows close it as usual
        }

        static string CodeName(uint code)
        {
            switch (code)
            {
                case 0xC0000005: return "(reading or writing memory it shouldn't)";
                case 0xC00000FD: return "(stack overflow)";
                case 0xC0000409: return "(a helper stopped itself)";
                case 0xC000001D: return "(an instruction this processor doesn't have)";
                case 0xE0434352: return "(.NET error)";
                case 0xC0000374: return "(memory damaged)";
                default: return "";
            }
        }
    }
}
