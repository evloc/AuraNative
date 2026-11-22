using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Aura
{
    // Media Foundation's media engine: the player built into Windows 10 and 11 (the one behind
    // Edge and the Media Player app). Plays MP3 (whatever size its cover), M4A/AAC, FLAC, WAV, WMA,
    // and Ogg/Opus/WebM where Windows' free web media extensions are installed.
    sealed class MfAudio : IAudio
    {
        // ---- Windows' definitions (mfmediaengine.h, mfobjects.h); the order of methods matters ----
        [ComImport, Guid("2CD2D921-C447-44A7-A13C-4ADABFC247E3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMFAttributes
        {
            void GetItem(); void GetItemType(); void CompareItem(); void Compare(); void GetUINT32(); void GetUINT64(); void GetDouble(); void GetGUID();
            void GetStringLength(); void GetString(); void GetAllocatedString(); void GetBlobSize(); void GetBlob(); void GetAllocatedBlob(); void GetUnknown();
            void SetItem(); void DeleteItem(); void DeleteAllItems();
            [PreserveSig] int SetUINT32([In] ref Guid key, int value);
            void SetUINT64(); void SetDouble(); void SetGUID(); void SetString(); void SetBlob();
            [PreserveSig] int SetUnknown([In] ref Guid key, [MarshalAs(UnmanagedType.IUnknown)] object value);
            void LockStore(); void UnlockStore(); void GetCount(); void GetItemByIndex(); void CopyAllItems();
        }

        [ComImport, Guid("FEE7C112-E776-42B5-9BBF-0048524E2BD5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMFMediaEngineNotify
        {
            [PreserveSig] int EventNotify(int ev, IntPtr param1, int param2);
        }

        [ComImport, Guid("4D645ACE-26AA-4688-9BE1-DF3516990B93"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMFMediaEngineClassFactory
        {
            [PreserveSig] int CreateInstance(int flags, IMFAttributes attributes, out IMFMediaEngine engine);
            void CreateTimeRange();
            void CreateError();
        }

        [ComImport, Guid("98A1B0BB-03EB-4935-AE7C-93C1FA0E1C93"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMFMediaEngine
        {
            [PreserveSig] int GetError(out IntPtr error);
            [PreserveSig] int SetErrorCode(int error);
            [PreserveSig] int SetSourceElements(IntPtr elements);
            [PreserveSig] int SetSource([MarshalAs(UnmanagedType.BStr)] string url);
            [PreserveSig] int GetCurrentSource(out IntPtr url);
            [PreserveSig] ushort GetNetworkState();
            [PreserveSig] int GetPreload();
            [PreserveSig] int SetPreload(int preload);
            [PreserveSig] int GetBuffered(out IntPtr buffered);
            [PreserveSig] int Load();
            [PreserveSig] int CanPlayType([MarshalAs(UnmanagedType.BStr)] string type, out int answer);
            [PreserveSig] ushort GetReadyState();
            [PreserveSig] int IsSeeking();
            [PreserveSig] double GetCurrentTime();
            [PreserveSig] int SetCurrentTime(double seconds);
            [PreserveSig] double GetStartTime();
            [PreserveSig] double GetDuration();
            [PreserveSig] int IsPaused();
            [PreserveSig] double GetDefaultPlaybackRate();
            [PreserveSig] int SetDefaultPlaybackRate(double rate);
            [PreserveSig] double GetPlaybackRate();
            [PreserveSig] int SetPlaybackRate(double rate);
            [PreserveSig] int GetPlayed(out IntPtr played);
            [PreserveSig] int GetSeekable(out IntPtr seekable);
            [PreserveSig] int IsEnded();
            [PreserveSig] int GetAutoPlay();
            [PreserveSig] int SetAutoPlay(int autoPlay);
            [PreserveSig] int GetLoop();
            [PreserveSig] int SetLoop(int loop);
            [PreserveSig] int Play();
            [PreserveSig] int Pause();
            [PreserveSig] int GetMuted();
            [PreserveSig] int SetMuted(int muted);
            [PreserveSig] double GetVolume();
            [PreserveSig] int SetVolume(double volume);
            [PreserveSig] int HasVideo();
            [PreserveSig] int HasAudio();
            [PreserveSig] int GetNativeVideoSize(out int x, out int y);
            [PreserveSig] int GetVideoAspectRatio(out int x, out int y);
            [PreserveSig] int Shutdown();
        }

        [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
        public sealed class Notify : IMFMediaEngineNotify
        {
            public volatile bool Error, Ended, Playing;
            public volatile int LastEvent;
            public int EventNotify(int ev, IntPtr p1, int p2)
            {
                LastEvent = ev;
                if (ev == 5) Error = true;           // MF_MEDIA_ENGINE_EVENT_ERROR
                else if (ev == 19) Ended = true;     // ENDED
                else if (ev == 13) Playing = true;   // PLAYING
                return 0;
            }
        }

        [DllImport("mfplat.dll")] static extern int MFStartup(int version, int flags);
        [DllImport("mfplat.dll")] static extern int MFCreateAttributes(out IMFAttributes attributes, int size);
        static readonly Guid FactoryClsid = new Guid("B44392DA-499B-446B-A4CB-005FEAD0E6D5");
        static Guid CallbackKey = new Guid("C60381B8-83A4-41F8-A3D0-DE05076849A9");     // MF_MEDIA_ENGINE_CALLBACK
        static Guid CategoryKey = new Guid("C8D4C51D-350E-41F2-BA46-FAEBBB0857F6");     // MF_MEDIA_ENGINE_AUDIO_CATEGORY
        static bool started;

        IMFMediaEngine engine;
        Notify notify;
        bool wantPlay, started_;
        double pending = -1;
        int vol = 80;
        readonly Stopwatch sincePlay = new Stopwatch();
        public string Name => "Media Foundation";

        public static MfAudio TryCreate()
        {
            try
            {
                var a = new MfAudio();
                a.Make();
                return a;
            }
            catch (Exception e) { App.Log("Media Foundation player not available: " + e.Message); return null; }
        }

        void Make()
        {
            if (!started) { int hr0 = MFStartup(0x20070, 0); if (hr0 < 0) throw new COMException("MFStartup failed", hr0); started = true; }
            var factory = (IMFMediaEngineClassFactory)Activator.CreateInstance(Type.GetTypeFromCLSID(FactoryClsid, true));
            Marshal.ThrowExceptionForHR(MFCreateAttributes(out var attrs, 2));
            notify = new Notify();
            Marshal.ThrowExceptionForHR(attrs.SetUnknown(ref CallbackKey, notify));
            attrs.SetUINT32(ref CategoryKey, 2);     // AudioCategory_Media... ignored where unknown
            int hr = factory.CreateInstance(0x1 /* audio only */, attrs, out engine);
            Marshal.ReleaseComObject(factory);
            Marshal.ReleaseComObject(attrs);
            Marshal.ThrowExceptionForHR(hr);
            engine.SetAutoPlay(0);
            engine.SetPreload(3);                    // load it all: seeking works straight away
            engine.SetVolume(Curve(vol));
        }

        static double Curve(int v) => Math.Pow(Math.Max(0, Math.Min(100, v)) / 100.0, 1.7);

        public bool Load(string path)
        {
            try
            {
                if (engine == null) Make();
                wantPlay = false; started_ = false; pending = -1;
                notify.Error = notify.Ended = notify.Playing = false;
                var url = new Uri(path).AbsoluteUri;
                int hr = engine.SetSource(url);
                if (hr < 0) { App.Log("Media Foundation couldn't open " + path + " (0x" + hr.ToString("X8") + ")"); return false; }
                engine.Load();
                return true;
            }
            catch (Exception e) { App.Log("Media Foundation couldn't open " + path + ": " + e.Message); return false; }
        }

        public void Play() { wantPlay = true; sincePlay.Restart(); try { engine?.Play(); } catch { } }
        public void Pause() { wantPlay = false; try { engine?.Pause(); } catch { } }
        public void Stop() { wantPlay = false; try { engine?.Pause(); engine?.SetCurrentTime(0); } catch { } }

        public double Position
        {
            get
            {
                if (pending >= 0) return pending;
                try { var t = engine?.GetCurrentTime() ?? 0; return double.IsNaN(t) ? 0 : t; } catch { return 0; }
            }
            set
            {
                // before the song's details are loaded a seek is kept for later
                try
                {
                    if (engine != null && engine.GetReadyState() >= 1) { engine.SetCurrentTime(Math.Max(0, value)); pending = -1; }
                    else pending = Math.Max(0, value);
                }
                catch { pending = Math.Max(0, value); }
            }
        }

        public double Duration
        {
            get { try { var d = engine?.GetDuration() ?? 0; return double.IsNaN(d) || double.IsInfinity(d) ? 0 : d; } catch { return 0; } }
        }

        public int Volume { set { vol = value; try { engine?.SetVolume(Curve(value)); } catch { } } }

        public PlayState Poll()
        {
            if (engine == null) return PlayState.Stopped;
            try
            {
                if (notify.Error || (engine.GetError(out var err) >= 0 && err != IntPtr.Zero && Release(err)))
                {
                    if (wantPlay || !started_) { App.Log("Media Foundation: couldn't play (event " + notify.LastEvent + ")"); return wantPlay ? PlayState.Failed : PlayState.Stopped; }
                }
                int ready = engine.GetReadyState();
                if (pending >= 0 && ready >= 1) { engine.SetCurrentTime(pending); pending = -1; }
                if (engine.IsEnded() != 0 || notify.Ended) return wantPlay ? PlayState.Ended : PlayState.Stopped;
                if (wantPlay)
                {
                    if (engine.IsPaused() == 0 && ready >= 2) { started_ = true; return PlayState.Playing; }
                    if (!started_ && sincePlay.Elapsed.TotalSeconds > 15) { App.Log("Media Foundation: never started (ready state " + ready + ")"); return PlayState.Failed; }
                    if (engine.IsPaused() != 0 && ready >= 2) { try { engine.Play(); } catch { } }   // asked to play before it was ready
                    return PlayState.Playing;
                }
                return Position > 0 ? PlayState.Paused : PlayState.Stopped;
            }
            catch (Exception e) { App.Log("Media Foundation: " + e.Message); return PlayState.Stopped; }
        }

        static bool Release(IntPtr p) { Marshal.Release(p); return true; }

        public void Unload()
        {
            // the engine holds the file open; shutting it down lets go (a new one is made for the next song)
            wantPlay = false; pending = -1;
            Kill();
        }

        void Kill()
        {
            if (engine == null) return;
            try { engine.Shutdown(); } catch { }
            try { Marshal.ReleaseComObject(engine); } catch { }
            engine = null;
        }

        public void Dispose() => Kill();
    }
}
