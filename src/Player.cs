using System;
using System.Collections.Generic;
using System.Linq;

namespace Aura
{
    // What's playing and what comes next. Everything here runs on the UI thread.
    sealed partial class Player : IDisposable
    {
        readonly Library lib;
        IAudio primary;
        IAudio audio;                  // the engine playing the current song
        readonly List<IAudio> spares = new List<IAudio>();     // engines to try when the main one can't play a song
        int tried;
        readonly System.Windows.Forms.Timer timer;
        readonly Random rnd = new Random();

        public List<long> Queue = new List<long>();     // songs in the order they were added
        public List<int> Order = new List<int>();       // play order: indexes into Queue (shuffled or not)
        public int Pos = -1;                             // index into Order
        public bool Shuffle;
        public int Repeat;                               // 0 off, 1 all, 2 one
        public Track Current { get; private set; }
        public PlayState State { get; private set; } = PlayState.Stopped;
        public bool IsPlaying => State == PlayState.Playing;
        public string EngineName => primary.Name + (primary is MfAudio || primary is WmpAudio ? " (with backups)" : "");

        public event Action TrackChanged, StateChanged, QueueChanged, Tick;
        public event Action<string> Problem;

        double listened, lastPos;
        bool counted;
        int volume = 80;
        int failsInRow;

        public Player(Library lib, IAudio audio)
        {
            this.lib = lib; primary = this.audio = audio;
            timer = new System.Windows.Forms.Timer { Interval = 100 };
            timer.Tick += (s, e) => Poll();
            timer.Start();
        }

        public int Volume
        {
            get => volume;
            set { volume = Math.Max(0, Math.Min(100, value)); ApplyVolume(); }   // the other engine gets it when it's used
        }

        public double Position => Current == null ? 0 : audio.Position;

        // the position for animation: moves steadily between the engine's (sometimes coarse) reports
        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        double anchorPos, anchorAt; long anchorFor = -1; bool anchorPlaying;
        public double SmoothPosition
        {
            get
            {
                if (Current == null) return 0;
                double raw = audio.Position, now = clock.Elapsed.TotalSeconds;
                bool playing = State == PlayState.Playing;
                double guess = anchorPos + (anchorPlaying ? now - anchorAt : 0);
                if (anchorFor != Current.Id || playing != anchorPlaying || Math.Abs(raw - guess) > 0.3) { anchorPos = raw; anchorAt = now; anchorFor = Current.Id; anchorPlaying = playing; return raw; }
                // drift back toward the engine gently, so there are no jumps
                anchorPos += (raw - guess) * 0.08;
                guess = anchorPos + (anchorPlaying ? now - anchorAt : 0);
                if (Looping && guess > LoopB) guess = LoopB;
                return Math.Max(0, Math.Min(guess, Math.Max(raw, Duration)));
            }
        }
        public double Duration { get { var d = Current == null ? 0 : audio.Duration; return d > 0 ? d : Current?.Duration ?? 0; } }

        public IEnumerable<long> UpNext()
        {
            for (int i = Pos + 1; i < Order.Count; i++) yield return Queue[Order[i]];
        }

        // ---------- starting things ----------
        public void PlayList(IList<long> ids, int start = 0)
        {
            if (ids == null || ids.Count == 0) return;
            Queue = ids.ToList();
            start = Math.Max(0, Math.Min(start, Queue.Count - 1));
            Order = Enumerable.Range(0, Queue.Count).ToList();
            if (Shuffle) { Order.Remove(start); ShuffleList(Order); Order.Insert(0, start); Pos = 0; }
            else Pos = start;
            QueueChanged?.Invoke();
            Start(true);
        }

        public void ShuffleAll(IList<long> ids)
        {
            if (ids == null || ids.Count == 0) return;
            Shuffle = true;
            PlayList(ids, rnd.Next(ids.Count));
        }

        public void PlayNext(IEnumerable<long> ids)
        {
            var list = ids.ToList();
            if (list.Count == 0) return;
            if (Current == null) { PlayList(list); return; }
            int at = Pos + 1;
            foreach (var id in list) { Queue.Add(id); Order.Insert(at++, Queue.Count - 1); }
            QueueChanged?.Invoke();
        }

        public void AddToQueue(IEnumerable<long> ids)
        {
            var list = ids.ToList();
            if (list.Count == 0) return;
            if (Current == null) { PlayList(list); return; }
            foreach (var id in list) { Queue.Add(id); Order.Add(Queue.Count - 1); }
            QueueChanged?.Invoke();
        }

        public void RemoveUpNext(int k)      // k-th song after the current one
        {
            int i = Pos + 1 + k;
            if (i <= Pos || i >= Order.Count) return;
            Order.RemoveAt(i);
            QueueChanged?.Invoke();
        }

        // move up-next songs (positions k, counted after the current one) so they start at position "to"
        public void MoveUpNext(List<int> ks, int to)
        {
            ks = ks.Where(k => Pos + 1 + k < Order.Count && k >= 0).Distinct().OrderBy(k => k).ToList();
            if (ks.Count == 0) return;
            var moving = ks.Select(k => Order[Pos + 1 + k]).ToList();
            int before = ks.Count(k => k < to);
            foreach (var k in ks.OrderByDescending(k => k)) Order.RemoveAt(Pos + 1 + k);
            int at = Math.Max(0, Math.Min(Order.Count - (Pos + 1), to - before));
            Order.InsertRange(Pos + 1 + at, moving);
            QueueChanged?.Invoke();
        }

        public void ClearUpNext()
        {
            if (Pos + 1 < Order.Count) Order.RemoveRange(Pos + 1, Order.Count - Pos - 1);
            QueueChanged?.Invoke();
        }

        public void JumpTo(int k)            // play the k-th song after the current one
        {
            int i = Pos + 1 + k;
            if (i < 0 || i >= Order.Count) return;
            Pos = i;
            Start(true);
        }

        void ShuffleList(List<int> l)
        {
            for (int i = l.Count - 1; i > 0; i--) { int j = rnd.Next(i + 1); var x = l[i]; l[i] = l[j]; l[j] = x; }
        }

        public void SetShuffle(bool on)
        {
            Shuffle = on;
            if (Order.Count > 0 && Pos >= 0)
            {
                int cur = Order[Pos];
                if (on) { var rest = Order.Where((x, i) => i != Pos).ToList(); ShuffleList(rest); Order = new List<int> { cur }; Order.AddRange(rest); Pos = 0; }
                else { Order = Enumerable.Range(0, Queue.Count).ToList(); Pos = cur; }
                QueueChanged?.Invoke();
            }
            StateChanged?.Invoke();
        }

        public void CycleRepeat() { Repeat = (Repeat + 1) % 3; StateChanged?.Invoke(); }

        // ---------- moving through the queue ----------
        void Start(bool play)
        {
            if (Pos < 0 || Pos >= Order.Count) { StopAll(); return; }
            var t = lib.Get(Queue[Order[Pos]]);
            string why = null;
            if (t == null) why = "That song isn't in your library any more";
            else if (FileX.Exists(t.Path) && AudioKind.Detect(t.Path) == null && new System.IO.FileInfo(FileX.Long(t.Path)).Length < 1024) { why = t.Name + " is empty or not fully downloaded"; App.Log("too small: " + t.Path); }
            else if (!FileX.Exists(t.Path) && !lib.Relocate(t)) { why = "Can't find " + t.Name + " — it may have been moved or renamed"; App.Log("not found: " + t.Path); }
            else if (!LoadWith(t, play)) why = "Windows couldn't play " + t.Name;
            if (why != null)
            {
                failsInRow++;
                Problem?.Invoke(why);
                if (failsInRow < Math.Min(Order.Count, 25)) { Advance(true); return; }
                failsInRow = 0; StopAll(); return;
            }
            if (Current != t) { LoopA = LoopB = -1; LoopChanged?.Invoke(); }
            CancelTwin();
            Current = t;
            listened = 0; lastPos = 0; counted = false;
            if (play) { audio.Play(); State = PlayState.Playing; }
            else State = PlayState.Paused;
            TrackChanged?.Invoke();
            StateChanged?.Invoke();
        }

        // opens a song in the main engine, or the second one if the main one can't
        string playPath;          // what the engines are given: the song, or a copy named for what it really is

        bool LoadWith(Track t, bool play)
        {
            if (audio != primary) { try { audio.Unload(); } catch { } audio = primary; }
            tried = 0;
            playPath = t.Path;
            var kind = AudioKind.Detect(t.Path);
            if (!AudioKind.Matches(t.Path, kind))
            {
                // e.g. an M4A named .mp3: Windows would try to open it as an MP3 and fail
                try
                {
                    playPath = AudioKind.PlayableCopy(t.Path, kind, t.Id);
                    App.Log(t.Path + " is really " + AudioKind.Name(kind) + " — playing it as that");
                }
                catch (Exception e) { App.Log("couldn't make a playable copy of " + t.Path + ": " + e.Message); playPath = t.Path; }
            }
            if (primary.Load(playPath)) { ApplyVolume(t); audio.Rate = Rate; return true; }
            return SwitchToFallback(t, false);
        }

        bool SwitchToFallback(Track t, bool play)
        {
            while (true)
            {
                IAudio next;
                if (tried < spares.Count) next = spares[tried];
                else
                {
                    next = AudioEngine.Fallback(spares.Count == 0 ? primary : spares[spares.Count - 1]);
                    if (next == null) return false;
                    spares.Add(next);
                }
                tried++;
                try { audio.Unload(); } catch { }
                App.Log("trying " + next.Name + " for " + t.Path);
                if (!next.Load(playPath ?? t.Path)) continue;
                audio = next;
                ApplyVolume(t); audio.Rate = Rate;
                if (play) audio.Play();
                return true;
            }
        }

        void StopAll()
        {
            audio.Stop();
            State = PlayState.Stopped;
            StateChanged?.Invoke();
        }

        void Advance(bool auto)
        {
            if (auto && Repeat == 2 && Current != null && failsInRow == 0) { audio.Position = 0; audio.Play(); listened = 0; counted = false; return; }
            if (Pos + 1 < Order.Count) { Pos++; Start(true); return; }
            if (Repeat >= 1 && Order.Count > 0)
            {
                if (Shuffle) ShuffleList(Order);
                Pos = 0; QueueChanged?.Invoke(); Start(true); return;
            }
            // end of the queue: radio keeps going with songs like these; otherwise stop
            if (auto && Radio && AddRadioSongs()) { Pos++; Start(true); return; }
            audio.Stop();
            State = PlayState.Stopped;
            StateChanged?.Invoke();
        }

        public void Next() { failsInRow = 0; if (Order.Count == 0) return; if (Pos + 1 >= Order.Count && Repeat == 0) { Pos = Order.Count; StopAll(); Pos = Order.Count - 1; return; } Advance(false); }

        public void Prev()
        {
            failsInRow = 0;
            if (Current == null) return;
            if (audio.Position > 3 || Pos <= 0) { audio.Position = 0; if (State != PlayState.Playing) { audio.Play(); State = PlayState.Playing; StateChanged?.Invoke(); } Tick?.Invoke(); return; }
            Pos--;
            Start(true);
        }

        public void Toggle()
        {
            if (Current == null)
            {
                if (Order.Count > 0) { Pos = Math.Max(0, Math.Min(Pos, Order.Count - 1)); Start(true); }
                else if (lib.Tracks.Count > 0) PlayList(lib.Tracks.OrderByDescending(t => t.Added).Select(t => t.Id).ToList());
                return;
            }
            if (State == PlayState.Playing) { CancelTwin(); audio.Pause(); State = PlayState.Paused; }
            else
            {
                if (State == PlayState.Stopped && audio.Duration > 0 && audio.Position >= audio.Duration - 0.5) audio.Position = 0;
                audio.Play(); State = PlayState.Playing;
            }
            StateChanged?.Invoke();
        }

        public void Seek(double sec)
        {
            if (Current == null) return;
            CancelTwin();
            audio.Position = Math.Max(0, Math.Min(sec, Math.Max(0, Duration - 0.25)));
            lastPos = audio.Position;
            anchorFor = -1;
            Tick?.Invoke();
        }

        void Poll()
        {
            if (Current == null) return;
            PollMore();
            if (Current == null) return;
            var s = audio.Poll();
            if (s == PlayState.Failed)
            {
                var t = Current;
                string err = (audio as WmpAudio)?.LastError ?? "";
                var kind = AudioKind.Detect(t.Path);
                App.Log(audio.Name + " couldn't play " + t.Path + (err.Length > 0 ? ": " + err : "") + "  [inside it's " + AudioKind.Name(kind) + "; starts " + AudioKind.Header(t.Path) + "]");
                if (SwitchToFallback(t, true)) return;
                string what = kind == ".webm" ? " — it's a WebM file inside, which Windows can't play without its free \"Web Media Extensions\" from the Microsoft Store"
                            : kind == null ? " — the file doesn't look like music inside (it may be damaged or not fully downloaded)" : "";
                Problem?.Invoke("Windows couldn't play " + t.Name + what + (err.Length > 0 && what.Length == 0 ? " (" + err + ")" : ""));
                failsInRow++;
                if (failsInRow < Math.Min(Order.Count, 25)) Advance(true); else { failsInRow = 0; StopAll(); }
                return;
            }
            if (s == PlayState.Ended)
            {
                CountIfListened(true);
                failsInRow = 0;
                if (SleepAfterSong) { SleepAfterSong = false; SleepAt = DateTime.MinValue; StopAll(); SleepChanged?.Invoke(); return; }
                if (TakeOverFromTwin()) return;          // the next song is already playing (no gap / crossfade)
                Advance(true);
                return;
            }
            if (State == PlayState.Playing && Looping && audio.Position >= LoopB) { audio.Position = LoopA; lastPos = LoopA; }
            if (State == PlayState.Playing)
            {
                double p = audio.Position;
                double d = p - lastPos;
                if (d > 0 && d < 1.5) listened += d;    // a seek isn't listening
                lastPos = p;
                CountIfListened(false);
                failsInRow = 0;
            }
            Tick?.Invoke();
        }

        void CountIfListened(bool finished)
        {
            if (counted || Current == null) return;
            double need = Math.Min(30, Math.Max(5, Duration * 0.5));
            if (listened >= need || (finished && listened >= Math.Min(need, Duration * 0.8)))
            {
                counted = true;
                try { lib.RecordPlay(Current); } catch (Exception e) { App.Log("play count: " + e.Message); }
            }
        }

        // ---------- remembering where you were ----------
        public void SaveTo(Settings s)
        {
            s.Volume = volume; s.Shuffle = Shuffle; s.Repeat = Repeat;
            s.Queue = Queue.Count <= 5000 ? Queue.ToList() : new List<long>();
            s.Order = Queue.Count <= 5000 ? Order.ToList() : new List<int>();
            s.QueuePos = Pos;
            s.LastTrack = Current?.Id ?? 0;
            s.LastPosition = Current == null ? 0 : audio.Position;
        }

        public void RestoreFrom(Settings s)
        {
            Volume = s.Volume; Shuffle = s.Shuffle; Repeat = s.Repeat;
            var q = s.Queue ?? new List<long>();
            var o = s.Order ?? new List<int>();
            if (q.Count > 0 && o.Count > 0 && o.All(i => i >= 0 && i < q.Count))
            {
                // drop songs that are gone, keeping the order
                var keep = new Dictionary<int, int>();
                var nq = new List<long>();
                for (int i = 0; i < q.Count; i++) if (lib.Get(q[i]) != null) { keep[i] = nq.Count; nq.Add(q[i]); }
                var no = new List<int>(); int npos = -1;
                for (int i = 0; i < o.Count; i++)
                {
                    if (!keep.TryGetValue(o[i], out var ni)) continue;
                    if (i == s.QueuePos) npos = no.Count;
                    no.Add(ni);
                }
                Queue = nq; Order = no; Pos = npos >= 0 ? npos : Math.Min(Math.Max(0, s.QueuePos), no.Count - 1);
            }
            if (Pos >= 0 && Pos < Order.Count && lib.Get(Queue[Order[Pos]]) is Track t && t.Id == s.LastTrack && FileX.Exists(t.Path))
            {
                Start(false);
                if (s.LastPosition > 1 && s.LastPosition < t.Duration - 2) audio.Position = s.LastPosition;
            }
            StateChanged?.Invoke();
            QueueChanged?.Invoke();
        }

        // play another version of the current song from the same point
        // the same moment in another version, lined up by how they sound (set by the window); -1 when unknown
        public Func<Track, Track, double, double> MapPosition;
        public bool LastSwitchLinedUp { get; private set; }

        public void SwitchTo(Track t)
        {
            if (Current == null || t == null) { if (t != null) PlayList(new List<long> { t.Id }); return; }
            var from = Current;
            double pos = audio.Position;
            double mapped = -1;
            try { mapped = MapPosition?.Invoke(from, t, pos) ?? -1; } catch (Exception e) { App.Log("lining up versions: " + e.Message); }
            LastSwitchLinedUp = mapped >= 0;
            if (mapped >= 0) pos = mapped;
            Queue[Order[Pos]] = t.Id;
            Start(true);
            if (Current == t && pos > 1 && pos < t.Duration - 2) { audio.Position = pos; lastPos = pos; }
            QueueChanged?.Invoke();
        }

        // after the library is read again: the song that's playing is the new copy of itself
        public void Relink() { if (Current != null && lib.Get(Current.Id) is Track t && t != Current) Current = t; }

        // ---------- A-B loop ----------
        public double LoopA { get; private set; } = -1;
        public double LoopB { get; private set; } = -1;
        public bool Looping => LoopA >= 0 && LoopB > LoopA;
        public event Action LoopChanged;
        public void SetLoop(double a, double b)
        {
            if (a >= 0 && b >= 0 && b < a) { var x = a; a = b; b = x; }
            if (a >= 0 && b >= 0 && b - a < 0.5) b = a + 0.5;
            LoopA = a; LoopB = b;
            LoopChanged?.Invoke();
            if (Looping && (Position < LoopA || Position > LoopB)) Seek(LoopA);
        }
        public void ClearLoop() { if (LoopA < 0 && LoopB < 0) return; LoopA = LoopB = -1; LoopChanged?.Invoke(); }

        // a song that was merged into another: the queue points at the one that's kept
        public void Replace(long from, long to)
        {
            bool any = false;
            for (int i = 0; i < Queue.Count; i++) if (Queue[i] == from) { Queue[i] = to; any = true; }
            if (any) QueueChanged?.Invoke();
        }

        // while a song's file is being changed, the player lets go of it, then picks up where it was
        public sealed class Held { public Track Track; public double Pos; public bool Playing; }

        public Held Release(IEnumerable<long> ids)
        {
            var t = Current;
            if (t == null || !ids.Contains(t.Id)) return null;
            var h = new Held { Track = t, Pos = audio.Position, Playing = State == PlayState.Playing };
            try { audio.Unload(); } catch { }
            State = PlayState.Paused;
            StateChanged?.Invoke();
            return h;
        }

        public void Restore(Held h)
        {
            if (h == null || Current == null || Current.Id != h.Track.Id) return;
            var t = lib.Get(h.Track.Id) ?? h.Track;
            Current = t;
            if (!LoadWith(t, h.Playing)) { Problem?.Invoke("Couldn't open " + t.Name + " again"); return; }
            audio.Position = h.Pos;
            if (h.Playing) { audio.Play(); State = PlayState.Playing; }
            lastPos = h.Pos;
            StateChanged?.Invoke();
            TrackChanged?.Invoke();
        }

        public void Dispose() { timer.Stop(); primary.Dispose(); foreach (var s in spares) s.Dispose(); }
    }
}
