using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Aura
{
    // Previous / play-pause / next buttons under AURA's taskbar thumbnail, and a progress bar on its
    // taskbar button while it converts songs.
    sealed class Taskbar
    {
        [ComImport, Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ITaskbarList3
        {
            [PreserveSig] int HrInit();
            [PreserveSig] int AddTab(IntPtr hwnd);
            [PreserveSig] int DeleteTab(IntPtr hwnd);
            [PreserveSig] int ActivateTab(IntPtr hwnd);
            [PreserveSig] int SetActiveAlt(IntPtr hwnd);
            [PreserveSig] int MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool full);
            [PreserveSig] int SetProgressValue(IntPtr hwnd, ulong done, ulong total);
            [PreserveSig] int SetProgressState(IntPtr hwnd, int flags);
            [PreserveSig] int RegisterTab(IntPtr tab, IntPtr main);
            [PreserveSig] int UnregisterTab(IntPtr tab);
            [PreserveSig] int SetTabOrder(IntPtr tab, IntPtr before);
            [PreserveSig] int SetTabActive(IntPtr tab, IntPtr main, int reserved);
            [PreserveSig] int ThumbBarAddButtons(IntPtr hwnd, uint count, [MarshalAs(UnmanagedType.LPArray)] ThumbButton[] buttons);
            [PreserveSig] int ThumbBarUpdateButtons(IntPtr hwnd, uint count, [MarshalAs(UnmanagedType.LPArray)] ThumbButton[] buttons);
        }

        [ComImport, Guid("56fdf344-fd6d-11d0-958a-006097c9a090")] class TaskbarListClass { }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct ThumbButton
        {
            public int Mask; public uint Id; public uint Bitmap; public IntPtr Icon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Tip;
            public int Flags;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int RegisterWindowMessage(string s);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        public static readonly int CreatedMessage = App.IsWindows ? RegisterWindowMessage("TaskbarButtonCreated") : -1;
        public const int Prev = 1, PlayPause = 2, Next = 3;

        ITaskbarList3 list;
        IntPtr hwnd;
        bool added;
        readonly IntPtr[] icons = new IntPtr[4];      // prev, play, pause, next

        public static Taskbar TryCreate(IntPtr hwnd)
        {
            if (!App.IsWindows) return null;
            try
            {
                var t = new Taskbar { hwnd = hwnd, list = (ITaskbarList3)new TaskbarListClass() };
                t.list.HrInit();
                string[] names = { "prev", "play", "pause", "next" };
                for (int i = 0; i < 4; i++) t.icons[i] = MakeIcon(names[i]);
                return t;
            }
            catch (Exception e) { App.Log("taskbar buttons aren't available: " + e.Message); return null; }
        }

        static IntPtr MakeIcon(string name)
        {
            int s = Math.Max(16, (int)Math.Round(16 * Theme.S));
            using (var b = new Bitmap(s, s))
            {
                using (var g = Graphics.FromImage(b))
                {
                    Theme.Setup(g);
                    g.Clear(Color.Transparent);
                    Icons.Draw(g, name, new RectangleF(0, 0, s, s), Color.White);
                }
                return b.GetHicon();
            }
        }

        ThumbButton[] Buttons(bool playing) => new[]
        {
            new ThumbButton { Mask = 2 | 4 | 8, Id = Prev, Icon = icons[0], Tip = "Previous" },
            new ThumbButton { Mask = 2 | 4 | 8, Id = PlayPause, Icon = playing ? icons[2] : icons[1], Tip = playing ? "Pause" : "Play" },
            new ThumbButton { Mask = 2 | 4 | 8, Id = Next, Icon = icons[3], Tip = "Next" },
        };

        // once Windows says the taskbar button exists
        public void Created(bool playing)
        {
            try
            {
                int hr = added ? list.ThumbBarUpdateButtons(hwnd, 3, Buttons(playing)) : list.ThumbBarAddButtons(hwnd, 3, Buttons(playing));
                if (hr >= 0) added = true; else App.Log("taskbar buttons: 0x" + hr.ToString("X8"));
            }
            catch (Exception e) { App.Log("taskbar buttons: " + e.Message); }
        }

        public void SetPlaying(bool playing)
        {
            if (!added) return;
            try { list.ThumbBarUpdateButtons(hwnd, 3, Buttons(playing)); } catch { }
        }

        // 0..1, or below 0 to hide it
        public void Progress(double k)
        {
            try
            {
                if (k < 0) { list.SetProgressState(hwnd, 0); return; }
                list.SetProgressState(hwnd, 2);
                list.SetProgressValue(hwnd, (ulong)(Math.Max(0, Math.Min(1, k)) * 1000), 1000);
            }
            catch { }
        }

        public void Dispose() { foreach (var i in icons) if (i != IntPtr.Zero) DestroyIcon(i); }
    }
}
