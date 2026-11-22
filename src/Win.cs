using System;
using System.Runtime.InteropServices;

namespace Aura
{
    // Small bits of Windows: dark title bar, media keys, search box hint, bringing the window forward.
    static class Win
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wp, string lp);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int RegisterWindowMessage(string name);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wp, IntPtr lp);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        public static readonly IntPtr Broadcast = new IntPtr(0xffff);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern uint GetShortPathName(string longPath, System.Text.StringBuilder shortPath, uint size);

        // the old-style short path (C:\MUSIC~1\SONGNA~1.MP3): no long names, no odd characters
        public static string ShortPath(string path)
        {
            if (!App.IsWindows) return null;
            try
            {
                var sb = new System.Text.StringBuilder(1024);
                uint n = GetShortPathName(FileX.Long(path, true), sb, (uint)sb.Capacity);
                if (n == 0 || n >= sb.Capacity) return null;
                var s = sb.ToString();
                if (s.StartsWith(@"\\?\UNC\")) s = @"\\" + s.Substring(8);
                else if (s.StartsWith(@"\\?\")) s = s.Substring(4);
                return s;
            }
            catch { return null; }
        }

        public static void DarkTitle(IntPtr h)
        {
            if (!App.IsWindows) return;
            try
            {
                int on = Theme.IsLight ? 0 : 1;
                if (DwmSetWindowAttribute(h, 20, ref on, 4) != 0) DwmSetWindowAttribute(h, 19, ref on, 4);
                int dark = ColorToBgr(Theme.Side);
                DwmSetWindowAttribute(h, 35, ref dark, 4);     // caption color on Windows 11
            }
            catch { }
        }

        static int ColorToBgr(System.Drawing.Color c) => c.R | (c.G << 8) | (c.B << 16);

        public static void Cue(IntPtr textBox, string hint)
        {
            if (!App.IsWindows) return;
            try { SendMessage(textBox, 0x1501, new IntPtr(1), hint); } catch { }
        }

        public static int ShowMessage;
        public static void InitMessages()
        {
            if (!App.IsWindows) return;
            try { ShowMessage = RegisterWindowMessage("AURA_NATIVE_SHOW"); } catch { }
        }
    }
}
