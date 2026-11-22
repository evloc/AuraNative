using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Aura
{
    enum PlayState { Stopped, Playing, Paused, Ended, Failed }

    interface IAudio : IDisposable
    {
        string Name { get; }
        bool Load(string path);
        void Play();
        void Pause();
        void Stop();
        double Position { get; set; }
        double Duration { get; }
        int Volume { set; }
        PlayState Poll();
        void Unload();          // let go of the file (so it can be changed or renamed)
    }

    static class AudioEngine
    {
        public static IAudio Create(bool test)
        {
            if (test || !App.IsWindows) return new TestAudio();
            return (IAudio)WmpAudio.TryCreate() ?? new MciAudio();
        }

        // a second engine to try when the first can't play a song
        public static IAudio Fallback(IAudio first)
        {
            if (first is TestAudio) return Environment.GetEnvironmentVariable("AURA_TEST_FALLBACK") != null ? new TestAudio { Picky = false } : null;
            if (first is WmpAudio) return new MciAudio();
            return WmpAudio.TryCreate();
        }
    }

    // Windows' own player engine (the one behind Windows Media Player / Media Foundation):
    // plays MP3, M4A/AAC, FLAC, WAV, WMA, and Ogg/Opus where Windows' web media codecs are installed.
    sealed class WmpAudio : IAudio
    {
        dynamic w;
        bool wantPlay, sawPlaying;
        readonly Stopwatch sincePlay = new Stopwatch();
        public string Name => "Windows Media";

        public static WmpAudio TryCreate()
        {
            try
            {
                var t = Type.GetTypeFromProgID("WMPlayer.OCX.7") ?? Type.GetTypeFromProgID("WMPlayer.OCX");
                if (t == null) return null;
                var a = new WmpAudio { w = Activator.CreateInstance(t) };
                a.w.uiMode = "invisible";
                a.w.settings.autoStart = false;
                a.w.settings.enableErrorDialogs = false;
                return a;
            }
            catch (Exception e) { App.Log("Windows Media engine not available: " + e.Message); return null; }
        }

        double pending = -1;     // a position asked for before the song has started (WMP ignores it until then)

        public bool Load(string path)
        {
            wantPlay = false; sawPlaying = false; pending = -1;
            var p = FileX.ForPlayer(path);
            try { w.URL = p; return true; } catch (Exception e) { App.Log("Windows Media couldn't open " + path + (p != path ? " (as " + p + ")" : "") + ": " + e.Message); return false; }
        }
        public void Play() { wantPlay = true; sincePlay.Restart(); try { w.controls.play(); } catch { } }
        public void Pause() { wantPlay = false; try { w.controls.pause(); } catch { } }
        public void Stop() { wantPlay = false; pending = -1; try { w.controls.stop(); } catch { } }
        public double Position
        {
            get { if (pending >= 0) return pending; try { return (double)w.controls.currentPosition; } catch { return 0; } }
            set
            {
                if (!sawPlaying) { pending = Math.Max(0, value); return; }
                try { w.controls.currentPosition = Math.Max(0, value); } catch { }
            }
        }
        public double Duration { get { try { return w.currentMedia == null ? 0 : (double)w.currentMedia.duration; } catch { return 0; } } }
        public int Volume { set { try { w.settings.volume = value; } catch { } } }

        public PlayState Poll()
        {
            int s;
            try { s = (int)w.playState; } catch { return PlayState.Stopped; }
            switch (s)
            {
                case 3:
                    if (!sawPlaying) { sawPlaying = true; if (pending > 0) { try { w.controls.currentPosition = pending; } catch { } } pending = -1; }
                    return PlayState.Playing;
                case 2: return PlayState.Paused;
                case 8: return wantPlay ? PlayState.Ended : PlayState.Stopped;
                case 1:
                case 10:
                case 0:
                    // WMP drops to "stopped/ready" once a song finishes
                    if (wantPlay && sawPlaying) return PlayState.Ended;
                    if (wantPlay && sincePlay.Elapsed.TotalSeconds > 5) return PlayState.Failed;     // it never started
                    return wantPlay ? PlayState.Playing : PlayState.Stopped;
                default:
                    if (wantPlay && !sawPlaying && sincePlay.Elapsed.TotalSeconds > 12) return PlayState.Failed;
                    return wantPlay ? PlayState.Playing : PlayState.Paused;
            }
        }

        public void Unload() { wantPlay = false; pending = -1; try { w.controls.stop(); w.close(); } catch { } }

        public string LastError
        {
            get
            {
                try { if (w.error.errorCount > 0) return (string)w.error.get_Item(0).errorDescription; } catch { }
                return "";
            }
        }

        public void Dispose() { try { w.close(); Marshal.FinalReleaseComObject(w); } catch { } w = null; }
    }

    // Fallback for Windows editions without the media engine (Windows "N"): plays MP3 and WAV.
    sealed class MciAudio : IAudio
    {
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        static extern int mciSendString(string cmd, StringBuilder ret, int len, IntPtr cb);
        bool open, wantPlay;
        public string Name => "Windows MCI";

        string Send(string cmd)
        {
            var sb = new StringBuilder(256);
            try { return mciSendString(cmd, sb, sb.Capacity, IntPtr.Zero) == 0 ? sb.ToString() : null; } catch { return null; }
        }

        public bool Load(string path)
        {
            if (open) Send("close aura");
            var p = Win.ShortPath(path) ?? path;     // MCI is happiest with short, plain paths
            open = Send("open \"" + p + "\" type mpegvideo alias aura") != null;
            if (!open) App.Log("MCI couldn't open " + path);
            if (open) Send("set aura time format milliseconds");
            wantPlay = false;
            return open;
        }
        public void Play() { wantPlay = true; Send("play aura"); }
        public void Pause() { wantPlay = false; Send("pause aura"); }
        public void Stop() { wantPlay = false; Send("stop aura"); }
        public double Position
        {
            get { return double.TryParse(Send("status aura position"), out var ms) ? ms / 1000 : 0; }
            set { Send("seek aura to " + (long)(value * 1000)); if (wantPlay) Send("play aura"); }
        }
        public double Duration => double.TryParse(Send("status aura length"), out var ms) ? ms / 1000 : 0;
        public int Volume { set { Send("setaudio aura volume to " + value * 10); } }
        public PlayState Poll()
        {
            if (!open) return wantPlay ? PlayState.Ended : PlayState.Stopped;
            var m = Send("status aura mode") ?? "";
            if (m == "playing") return PlayState.Playing;
            if (m == "paused") return PlayState.Paused;
            if (wantPlay && Position >= Duration - 0.5) return PlayState.Ended;
            if (wantPlay && m == "stopped" && Position < 0.5) return PlayState.Failed;
            return PlayState.Stopped;
        }
        public void Unload() { wantPlay = false; if (open) Send("close aura"); open = false; }
        public void Dispose() { if (open) Send("close aura"); open = false; }
    }

    // Used when testing on a machine without Windows: pretends to play, in real time.
    sealed class TestAudio : IAudio
    {
        readonly Stopwatch sw = new Stopwatch();
        double baseAt, dur;
        bool wantPlay;
        public static double Speed = 1;
        public bool Picky = true;       // test: the main engine "can't play" files with "broken" in the name
        bool broken;
        readonly Stopwatch since = new Stopwatch();
        public string Name => Picky ? "test" : "test backup";
        public bool Load(string path) { dur = Tags.Read(path).Duration; if (dur <= 0) dur = 30; baseAt = 0; sw.Reset(); wantPlay = false; broken = Picky && path.Contains("broken"); return System.IO.File.Exists(path); }
        public void Play() { wantPlay = true; since.Restart(); if (!broken) sw.Start(); }
        public void Pause() { wantPlay = false; baseAt = Position; sw.Reset(); }
        public void Stop() { wantPlay = false; baseAt = 0; sw.Reset(); }
        public double Position { get => Math.Min(dur, baseAt + sw.Elapsed.TotalSeconds * Speed); set { baseAt = Math.Max(0, Math.Min(dur, value)); if (sw.IsRunning) sw.Restart(); else sw.Reset(); } }
        public double Duration => dur;
        public int Volume { set { } }
        public PlayState Poll() => broken && wantPlay ? (since.Elapsed.TotalSeconds > 1 ? PlayState.Failed : PlayState.Playing) : !wantPlay ? (baseAt > 0 ? PlayState.Paused : PlayState.Stopped) : Position >= dur ? PlayState.Ended : PlayState.Playing;
        public void Unload() { Stop(); }
        public void Dispose() { }
    }
}
