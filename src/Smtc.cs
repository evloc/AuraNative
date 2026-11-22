using System;
using System.Runtime.InteropServices;

namespace Aura
{
    // Windows' media controls: the box that pops up with the volume keys, and the lock screen.
    // (System Media Transport Controls, reached from a normal window through its "interop" door.)
    sealed class Smtc : IDisposable
    {
        [DllImport("combase.dll", PreserveSig = true)] static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string s, int len, out IntPtr h);
        [DllImport("combase.dll", PreserveSig = true)] static extern int WindowsDeleteString(IntPtr h);
        [DllImport("combase.dll", PreserveSig = true)] static extern int RoGetActivationFactory(IntPtr classId, [In] ref Guid iid, out IntPtr factory);
        [DllImport("shlwapi.dll", PreserveSig = true)] static extern IntPtr SHCreateMemStream(byte[] data, int len);
        [DllImport("shcore.dll", PreserveSig = true)] static extern int CreateRandomAccessStreamOverStream(IntPtr stream, int options, [In] ref Guid iid, out IntPtr ras);

        [ComImport, Guid("ddb0472d-c911-4a1f-86d9-dc3d71a95f5a"), InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]
        interface IInterop { [PreserveSig] int GetForWindow(IntPtr hwnd, [In] ref Guid iid, out IntPtr controls); }

        [ComImport, Guid("99fa3ff4-1742-42a6-902e-087d41f965ec"), InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]
        interface IControls
        {
            [PreserveSig] int get_PlaybackStatus(out int v); [PreserveSig] int put_PlaybackStatus(int v);
            [PreserveSig] int get_DisplayUpdater(out IntPtr v);
            [PreserveSig] int get_SoundLevel(out int v);
            [PreserveSig] int get_IsEnabled(out byte v); [PreserveSig] int put_IsEnabled(byte v);
            [PreserveSig] int get_IsPlayEnabled(out byte v); [PreserveSig] int put_IsPlayEnabled(byte v);
            [PreserveSig] int get_IsStopEnabled(out byte v); [PreserveSig] int put_IsStopEnabled(byte v);
            [PreserveSig] int get_IsPauseEnabled(out byte v); [PreserveSig] int put_IsPauseEnabled(byte v);
            [PreserveSig] int get_IsRecordEnabled(out byte v); [PreserveSig] int put_IsRecordEnabled(byte v);
            [PreserveSig] int get_IsFastForwardEnabled(out byte v); [PreserveSig] int put_IsFastForwardEnabled(byte v);
            [PreserveSig] int get_IsRewindEnabled(out byte v); [PreserveSig] int put_IsRewindEnabled(byte v);
            [PreserveSig] int get_IsPreviousEnabled(out byte v); [PreserveSig] int put_IsPreviousEnabled(byte v);
            [PreserveSig] int get_IsNextEnabled(out byte v); [PreserveSig] int put_IsNextEnabled(byte v);
            [PreserveSig] int get_IsChannelUpEnabled(out byte v); [PreserveSig] int put_IsChannelUpEnabled(byte v);
            [PreserveSig] int get_IsChannelDownEnabled(out byte v); [PreserveSig] int put_IsChannelDownEnabled(byte v);
            [PreserveSig] int add_ButtonPressed(IntPtr handler, out long token);
            [PreserveSig] int remove_ButtonPressed(long token);
        }

        [ComImport, Guid("8abbc53e-fa55-4ecf-ad8e-c984e5dd1550"), InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]
        interface IDisplay
        {
            [PreserveSig] int get_Type(out int v); [PreserveSig] int put_Type(int v);
            [PreserveSig] int get_AppMediaId(out IntPtr v); [PreserveSig] int put_AppMediaId(IntPtr v);
            [PreserveSig] int get_Thumbnail(out IntPtr v); [PreserveSig] int put_Thumbnail(IntPtr v);
            [PreserveSig] int get_MusicProperties(out IntPtr v);
            [PreserveSig] int get_VideoProperties(out IntPtr v);
            [PreserveSig] int get_ImageProperties(out IntPtr v);
            [PreserveSig] int CopyFromFileAsync(int type, IntPtr file, out IntPtr op);
            [PreserveSig] int ClearAll();
            [PreserveSig] int Update();
        }

        [ComImport, Guid("6bbf0c59-d0a0-4d26-92a0-f978e1d18e7b"), InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]
        interface IMusic
        {
            [PreserveSig] int get_Title(out IntPtr v); [PreserveSig] int put_Title(IntPtr v);
            [PreserveSig] int get_AlbumArtist(out IntPtr v); [PreserveSig] int put_AlbumArtist(IntPtr v);
            [PreserveSig] int get_Artist(out IntPtr v); [PreserveSig] int put_Artist(IntPtr v);
        }

        [ComImport, Guid("b7f47116-a56f-4dc8-9e11-92031f4a87c2"), InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]
        interface IPressed { [PreserveSig] int get_Button(out int v); }

        [ComImport, Guid("857309dc-3fbf-4e7d-986f-ef3b1a07a964"), InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]
        interface IStreamRefStatics
        {
            [PreserveSig] int CreateFromFile(IntPtr file, out IntPtr r);
            [PreserveSig] int CreateFromUri(IntPtr uri, out IntPtr r);
            [PreserveSig] int CreateFromStream(IntPtr stream, out IntPtr r);
        }

        // TypedEventHandler<SystemMediaTransportControls, SystemMediaTransportControlsButtonPressedEventArgs>
        [ComVisible(true), Guid("0557e996-7b23-5bae-aa81-ea0d671143a4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IPressedHandler { void Invoke(IntPtr sender, IntPtr args); }

        [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
        public sealed class Handler : IPressedHandler
        {
            public Action<int> OnButton;
            public void Invoke(IntPtr sender, IntPtr args)
            {
                try
                {
                    var a = (IPressed)Marshal.GetObjectForIUnknown(args);
                    if (a.get_Button(out int b) >= 0) OnButton?.Invoke(b);
                    Marshal.ReleaseComObject(a);
                }
                catch (Exception e) { App.Log("media controls: " + e.Message); }
            }
        }

        IControls controls;
        IDisplay display;
        IMusic music;
        IntPtr handlerPtr; long token; Handler handler;
        public bool Working => controls != null;

        public const int Play = 0, Pause = 1, Stop = 2, Next = 6, Previous = 7;

        static IntPtr H(string s) { WindowsCreateString(s ?? "", (s ?? "").Length, out var h); return h; }

        static T Factory<T>(string cls, Guid iid)
        {
            var h = H(cls);
            try
            {
                int hr = RoGetActivationFactory(h, ref iid, out var f);
                if (hr < 0) throw new COMException("no " + cls, hr);
                try { return (T)Marshal.GetObjectForIUnknown(f); } finally { Marshal.Release(f); }
            }
            finally { WindowsDeleteString(h); }
        }

        public static Smtc TryCreate(IntPtr hwnd, Action<int> onButton)
        {
            if (!App.IsWindows || Environment.OSVersion.Version.Major < 10) return null;
            try { return new Smtc(hwnd, onButton); }
            catch (Exception e) { App.Log("Windows media controls aren't available: " + e.Message); return null; }
        }

        Smtc(IntPtr hwnd, Action<int> onButton)
        {
            var interop = Factory<IInterop>("Windows.Media.SystemMediaTransportControls", typeof(IInterop).GUID);
            var iid = typeof(IControls).GUID;
            int hr = interop.GetForWindow(hwnd, ref iid, out var p);
            Marshal.ReleaseComObject(interop);
            if (hr < 0) throw new COMException("GetForWindow", hr);
            controls = (IControls)Marshal.GetObjectForIUnknown(p); Marshal.Release(p);
            controls.put_IsEnabled(1);
            controls.put_IsPlayEnabled(1); controls.put_IsPauseEnabled(1);
            controls.put_IsNextEnabled(1); controls.put_IsPreviousEnabled(1); controls.put_IsStopEnabled(1);
            controls.put_PlaybackStatus(2);
            if (controls.get_DisplayUpdater(out var d) >= 0) { display = (IDisplay)Marshal.GetObjectForIUnknown(d); Marshal.Release(d); }
            if (display != null && display.put_Type(1) >= 0 && display.get_MusicProperties(out var m) >= 0) { music = (IMusic)Marshal.GetObjectForIUnknown(m); Marshal.Release(m); }
            handler = new Handler { OnButton = onButton };
            handlerPtr = Marshal.GetComInterfaceForObject(handler, typeof(IPressedHandler));
            hr = controls.add_ButtonPressed(handlerPtr, out token);
            if (hr < 0) App.Log("media controls: buttons not connected (0x" + hr.ToString("X8") + ")");
        }

        public void SetState(bool playing, bool any)
        {
            try { controls?.put_PlaybackStatus(!any ? 2 : playing ? 3 : 4); } catch (Exception e) { App.Log("media controls: " + e.Message); }
        }

        public void SetSong(string title, string artist, string album, byte[] cover)
        {
            if (display == null) return;
            try
            {
                display.put_Type(1);
                if (music != null)
                {
                    var t = H(title); var a = H(artist); var aa = H(album);
                    music.put_Title(t); music.put_Artist(a); music.put_AlbumArtist(aa);
                    WindowsDeleteString(t); WindowsDeleteString(a); WindowsDeleteString(aa);
                }
                IntPtr thumb = IntPtr.Zero;
                if (cover != null && cover.Length > 0)
                {
                    var stream = SHCreateMemStream(cover, cover.Length);
                    if (stream != IntPtr.Zero)
                    {
                        var iid = new Guid("905a0fe1-bc53-11df-8c49-001e4fc686da");
                        if (CreateRandomAccessStreamOverStream(stream, 0, ref iid, out var ras) >= 0)
                        {
                            var statics = Factory<IStreamRefStatics>("Windows.Storage.Streams.RandomAccessStreamReference", typeof(IStreamRefStatics).GUID);
                            statics.CreateFromStream(ras, out thumb);
                            Marshal.ReleaseComObject(statics);
                            Marshal.Release(ras);
                        }
                        Marshal.Release(stream);
                    }
                }
                display.put_Thumbnail(thumb);
                if (thumb != IntPtr.Zero) Marshal.Release(thumb);
                display.Update();
            }
            catch (Exception e) { App.Log("media controls: " + e.Message); }
        }

        public void Dispose()
        {
            try
            {
                if (controls != null)
                {
                    if (token != 0) controls.remove_ButtonPressed(token);
                    controls.put_PlaybackStatus(0);
                    display?.ClearAll();
                }
            }
            catch { }
            if (handlerPtr != IntPtr.Zero) { Marshal.Release(handlerPtr); handlerPtr = IntPtr.Zero; }
            foreach (var o in new object[] { music, display, controls }) if (o != null) try { Marshal.ReleaseComObject(o); } catch { }
            music = null; display = null; controls = null;
        }
    }
}
