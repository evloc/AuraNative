using System;
using System.Collections.Generic;
using System.Linq;

namespace Aura
{
    // Speed, volume leveling, the sleep timer, what happens between songs (no gap / crossfade), and radio.
    sealed partial class Player
    {
        // ---------- speed ----------
        double rate = 1;
        public double Rate
        {
            get => rate;
            set { rate = Math.Max(0.5, Math.Min(2, value)); try { audio.Rate = rate; twin?.Set(rate); } catch { } StateChanged?.Invoke(); }
        }

        // ---------- volume: your volume x leveling x fades ----------
        public bool Leveling;
        public Func<Track, double> GainFor;          // set by the window: how much to turn this song down (0..1)
        double fadeOut = 1;                          // the sleep timer's fade

        double Gain(Track t)
        {
            if (!Leveling || t == null || GainFor == null) return 1;
            try { return Math.Max(0.2, Math.Min(1, GainFor(t))); } catch { return 1; }
        }

        void ApplyVolume(Track t = null, double k = 1)
        {
            t = t ?? Current;
            audio.Volume = (int)Math.Round(volume * Gain(t) * fadeOut * k);
        }

        // ---------- sleep timer ----------
        public DateTime SleepAt = DateTime.MinValue;
        public bool SleepAfterSong;
        public event Action SleepChanged;
        public bool Sleeping => SleepAt != DateTime.MinValue || SleepAfterSong;

        // minutes: 0 turns it off, -1 means "at the end of this song"
        public void SetSleep(int minutes)
        {
            SleepAfterSong = minutes < 0;
            SleepAt = minutes > 0 ? DateTime.Now.AddMinutes(minutes) : DateTime.MinValue;
            if (fadeOut < 1) { fadeOut = 1; ApplyVolume(); }
            SleepChanged?.Invoke();
        }

        public string SleepText => SleepAfterSong ? "after this song" : SleepAt == DateTime.MinValue ? "" : Math.Max(0, Math.Ceiling((SleepAt - DateTime.Now).TotalMinutes)) + " min";

        void SleepTick()
        {
            if (SleepAt == DateTime.MinValue || State != PlayState.Playing) return;
            double left = (SleepAt - DateTime.Now).TotalSeconds;
            if (left > 12) return;
            // the last 12 seconds fade out, then it stops
            fadeOut = Math.Max(0, left / 12);
            ApplyVolume();
            if (left <= 0)
            {
                audio.Pause(); State = PlayState.Paused;
                SleepAt = DateTime.MinValue; fadeOut = 1; ApplyVolume();
                StateChanged?.Invoke(); SleepChanged?.Invoke();
            }
        }

        // ---------- between songs ----------
        public int Between;              // 0: the normal small gap; -1: no gap; 2..12: crossfade this many seconds
        sealed class TwinState { public IAudio Engine; public Track Track; public int OrderPos; public bool Started; public double FadeFrom, FadeLen; public void Set(double r) { if (Engine != null) Engine.Rate = r; } }
        TwinState twin;
        IAudio twinSpare;
        readonly System.Diagnostics.Stopwatch fadeClock = System.Diagnostics.Stopwatch.StartNew();

        void PollMore()
        {
            SleepTick();
            if (twin != null && twin.Started && twin.FadeLen > 0)
            {
                double k = Math.Max(0, Math.Min(1, (fadeClock.Elapsed.TotalSeconds - twin.FadeFrom) / twin.FadeLen));
                // equal-power curves sound smoother than straight lines
                ApplyVolume(Current, Math.Cos(k * Math.PI / 2));
                twin.Engine.Volume = (int)Math.Round(volume * Gain(twin.Track) * fadeOut * Math.Sin(k * Math.PI / 2));
                if (k >= 1) { CountIfListened(true); TakeOverFromTwin(); }
                return;
            }
            if (Between == 0 || twin != null || State != PlayState.Playing || Looping || Repeat == 2 || SleepAfterSong || audio != primary) return;
            int next = Pos + 1 < Order.Count ? Pos + 1 : Repeat == 1 && !Shuffle && Order.Count > 1 ? 0 : -1;
            if (next < 0) return;
            double dur = Duration, pos = audio.Position, lead = Between > 0 ? Between : 2.5;
            if (dur < lead * 2 + 6 || pos < dur - lead) return;
            PrepareTwin(next, Between > 0 ? Math.Max(0.5, Math.Min(Between, dur - pos)) : 0);
        }

        void PrepareTwin(int orderPos, double fade)
        {
            var t = lib.Get(Queue[Order[orderPos]]);
            if (t == null || !FileX.Exists(t.Path)) return;
            var e = twinSpare ?? AudioEngine.Twin(primary);
            twinSpare = null;
            if (e == null) { App.Log("no second player: playing songs one after another as usual"); Between = 0; return; }
            string path = t.Path;
            var kind = AudioKind.Detect(t.Path);
            if (!AudioKind.Matches(t.Path, kind)) try { path = AudioKind.PlayableCopy(t.Path, kind, t.Id); } catch { }
            if (!e.Load(path)) { twinSpare = e; twin = new TwinState { Engine = null, Track = t, OrderPos = -1 }; return; }     // that one plays the normal way
            e.Rate = rate;
            twin = new TwinState { Engine = e, Track = t, OrderPos = orderPos, FadeLen = fade };
            if (fade > 0)
            {
                e.Volume = 0;
                e.Play();
                twin.Started = true; twin.FadeFrom = fadeClock.Elapsed.TotalSeconds;
            }
        }

        // the song that was waiting becomes the current one
        bool TakeOverFromTwin()
        {
            if (twin == null || twin.Engine == null) { twin = null; return false; }
            var tw = twin; twin = null;
            if (!tw.Started) { ApplyVolume(tw.Track); tw.Engine.Volume = (int)Math.Round(volume * Gain(tw.Track) * fadeOut); tw.Engine.Play(); }
            var old = audio;
            try { old.Stop(); old.Unload(); } catch { }
            twinSpare = old;
            primary = audio = tw.Engine;
            Pos = tw.OrderPos;
            if (Current != tw.Track) { LoopA = LoopB = -1; LoopChanged?.Invoke(); }
            Current = tw.Track;
            playPath = tw.Track.Path;
            listened = 0; lastPos = audio.Position; counted = false; failsInRow = 0;
            CrashGuard.Doing("playing", tw.Track.Name + " (" + audio.Name + ")");
            State = PlayState.Playing;
            ApplyVolume();
            TrackChanged?.Invoke(); StateChanged?.Invoke(); QueueChanged?.Invoke();
            return true;
        }

        void CancelTwin()
        {
            if (twin == null) return;
            var tw = twin; twin = null;
            if (tw.Engine != null) { try { tw.Engine.Stop(); tw.Engine.Unload(); } catch { } twinSpare = tw.Engine; }
            if (Current != null) ApplyVolume();
        }

        // ---------- radio: when the queue runs out, more songs like these ----------
        public bool Radio;
        public Func<Track, double> BpmOf;
        readonly Random radioRnd = new Random();

        bool AddRadioSongs()
        {
            var seeds = Order.Skip(Math.Max(0, Order.Count - 5)).Select(i => lib.Get(Queue[i])).Where(t => t != null).ToList();
            if (seeds.Count == 0 || lib.Tracks.Count < 2) return false;
            var recent = new HashSet<long>(Queue.Skip(Math.Max(0, Queue.Count - 60)));
            foreach (var (t, _) in lib.History(80)) recent.Add(t.Id);
            var artists = new HashSet<string>(seeds.Select(t => t.MainArtist.ToLowerInvariant()));
            var eras = new HashSet<string>(seeds.Where(t => t.Era.Length > 0).Select(t => t.Era.ToLowerInvariant()));
            var bpms = seeds.Select(t => BpmOf?.Invoke(t) ?? 0).Where(b => b > 0).ToList();
            double Score(Track t)
            {
                double s = 0.5;
                if (artists.Contains(t.MainArtist.ToLowerInvariant())) s += 3;
                if (t.Era.Length > 0 && eras.Contains(t.Era.ToLowerInvariant())) s += 2;
                if (t.Fav) s += 1;
                if (t.Rating >= 4) s += 1;
                if (bpms.Count > 0) { double b = BpmOf?.Invoke(t) ?? 0; if (b > 0 && bpms.Any(x => Math.Abs(x - b) / x < 0.08)) s += 1; }
                return s;
            }
            var pool = lib.Tracks.Where(t => !recent.Contains(t.Id)).Select(t => (t, w: Score(t) * (0.5 + radioRnd.NextDouble()))).OrderByDescending(x => x.w).Take(10).Select(x => x.t.Id).ToList();
            if (pool.Count == 0) pool = lib.Tracks.OrderBy(_ => radioRnd.Next()).Take(10).Select(t => t.Id).ToList();
            foreach (var id in pool) { Queue.Add(id); Order.Add(Queue.Count - 1); }
            QueueChanged?.Invoke();
            return true;
        }
    }
}
