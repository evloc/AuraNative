using System;
using System.Collections.Generic;
using System.Linq;

namespace Aura
{
    // What's playing and what comes next. Everything here runs on the UI thread.
    sealed class Player : IDisposable
    {
        readonly Library lib;
        readonly IAudio primary;
        IAudio fallback, audio;        // audio: the engine playing the current song
        bool triedFallback;
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
        public string EngineName => primary.Name;

        public event Action TrackChanged, StateChanged, QueueChanged, Tick;
        public event Action<string> Problem;

        double listened, lastPos;
        bool counted;
        int volume = 80;
        int failsInRow;

        public Player(Library lib, IAudio audio)
        {
            this.lib = lib; primary = this.audio = audio;
            timer = new System.Windows.Forms.Timer { Interval = 200 };
            timer.Tick += (s, e) => Poll();
            timer.Start();
        }

        public int Volume
        {
            get => volume;
            set { volume = Math.Max(0, Math.Min(100, value)); audio.Volume = volume; }   // the other engine gets it when it's used
        }

        public double Position => Current == null ? 0 : audio.Position;
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
            else if (!FileX.Exists(t.Path)) { why = "Can't find " + t.Name + " — it may have been moved or renamed"; App.Log("not found: " + t.Path); }
            else if (!LoadWith(t, play)) why = "Windows couldn't play " + t.Name;
            if (why != null)
            {
                failsInRow++;
                Problem?.Invoke(why);
                if (failsInRow < Math.Min(Order.Count, 25)) { Advance(true); return; }
                failsInRow = 0; StopAll(); return;
            }
            Current = t;
            listened = 0; lastPos = 0; counted = false;
            if (play) { audio.Play(); State = PlayState.Playing; }
            else State = PlayState.Paused;
            TrackChanged?.Invoke();
            StateChanged?.Invoke();
        }

        // opens a song in the main engine, or the second one if the main one can't
        bool LoadWith(Track t, bool play)
        {
            if (audio != primary) { try { audio.Stop(); } catch { } audio = primary; }
            triedFallback = false;
            if (primary.Load(t.Path)) { audio.Volume = volume; return true; }
            return SwitchToFallback(t, false);
        }

        bool SwitchToFallback(Track t, bool play)
        {
            if (triedFallback) return false;
            triedFallback = true;
            if (fallback == null) fallback = AudioEngine.Fallback(primary);
            if (fallback == null) return false;
            try { primary.Stop(); } catch { }
            App.Log("trying " + fallback.Name + " for " + t.Path);
            if (!fallback.Load(t.Path)) return false;
            audio = fallback;
            audio.Volume = volume;
            if (play) audio.Play();
            return true;
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
            // end of the queue: stop at the start of the last song
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
            if (State == PlayState.Playing) { audio.Pause(); State = PlayState.Paused; }
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
            audio.Position = Math.Max(0, Math.Min(sec, Math.Max(0, Duration - 0.25)));
            lastPos = audio.Position;
            Tick?.Invoke();
        }

        void Poll()
        {
            if (Current == null) return;
            var s = audio.Poll();
            if (s == PlayState.Failed)
            {
                var t = Current;
                string err = (audio as WmpAudio)?.LastError ?? "";
                App.Log(audio.Name + " couldn't play " + t.Path + (err.Length > 0 ? ": " + err : ""));
                if (SwitchToFallback(t, true)) return;
                Problem?.Invoke("Windows couldn't play " + t.Name + (err.Length > 0 ? " (" + err + ")" : ""));
                failsInRow++;
                if (failsInRow < Math.Min(Order.Count, 25)) Advance(true); else { failsInRow = 0; StopAll(); }
                return;
            }
            if (s == PlayState.Ended)
            {
                CountIfListened(true);
                failsInRow = 0;
                Advance(true);
                return;
            }
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
        public void SwitchTo(Track t)
        {
            if (Current == null || t == null) { if (t != null) PlayList(new List<long> { t.Id }); return; }
            double pos = audio.Position;
            bool playing = State == PlayState.Playing || State == PlayState.Paused;
            Queue[Order[Pos]] = t.Id;
            Start(true);
            if (Current == t && pos > 1 && pos < t.Duration - 2) audio.Position = pos;
            QueueChanged?.Invoke();
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
            if (h == null || Current != h.Track) return;
            var t = lib.Get(h.Track.Id) ?? h.Track;
            Current = t;
            if (!LoadWith(t, h.Playing)) { Problem?.Invoke("Couldn't open " + t.Name + " again"); return; }
            audio.Position = h.Pos;
            if (h.Playing) { audio.Play(); State = PlayState.Playing; }
            lastPos = h.Pos;
            StateChanged?.Invoke();
            TrackChanged?.Invoke();
        }

        public void Dispose() { timer.Stop(); primary.Dispose(); fallback?.Dispose(); }
    }
}
