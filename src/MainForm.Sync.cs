using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Aura
{
    // Getting a song ready for Whisper (with the vocals taken out first, when asked), looking online for the
    // real words after writing them down, and timing the whole library in the background.
    static class Hearing
    {
        // 16 kHz mono for Whisper. separate: take the beat away first (falls back to the full song if that fails).
        public static float[] Pcm(Track t, bool separate, Action<string, double> say, Func<bool> stop, out string note)
        {
            note = "";
            if (separate)
            {
                if (!Separator.Installed) note = "the vocal part isn't installed, so it listened to the whole song";
                else
                {
                    try
                    {
                        say("Taking the vocals out…", 0);
                        var v = Separator.Vocals(t.Path, t.Id, t.Duration, 20 * 60, p =>
                        {
                            if (p < 0) say("Downloading the vocal model (65 MB, just this once)… " + ((-p - 1) * 100).ToString("0") + "%", -p - 1);
                            else say("Taking the vocals out… " + (p * 100).ToString("0") + "%", p);
                        }, stop);
                        note = "listened to the vocals on their own";
                        return v;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception e) { App.Log("vocals: " + e.Message); note = "couldn't take the vocals out (" + e.Message + "), so it listened to the whole song"; }
                }
            }
            say("Reading the song…", 0);
            return Decoder.Mono(t.Path, t.Id, 16000, 20 * 60, stop, out _);
        }

        // After transcribing: real lyrics online that match what was heard, timed to it. Searches by the song's
        // name and by a couple of the heard lines, so it also finds songs whose file is named differently.
        public static (Found found, Sync.Result result) MatchOnline(Track t, List<Whisper.Heard> heard, Sync.Result written, Func<bool> stop)
        {
            if (heard.Count < 8) return (null, null);
            var found = new List<Found>();
            try { found.AddRange(LyricsOnline.Search(t).Found); } catch { }
            // the clearest long lines, as searches
            foreach (var line in written.Lines.Select((l, i) => (l, i)).Where(x => !written.Guessed[x.i]).Select(x => x.l.Text)
                .Where(x => x.Split(' ').Length >= 5).OrderByDescending(x => x.Length).Take(2))
            {
                if (stop()) return (null, null);
                try { found.AddRange(LyricsOnline.Search(t, line).Found); } catch { }
            }
            Found best = null; Sync.Result bestR = null;
            int genius = 0;
            foreach (var f in found.GroupBy(f => f.Source + "|" + LyricsOnline.Norm(f.Title) + "|" + LyricsOnline.Norm(f.Artist)).Select(g => g.First()))
            {
                if (stop()) break;
                string text = f.Plain;
                if (string.IsNullOrWhiteSpace(text) && f.Source == "genius" && genius++ < 3) { try { text = LyricsOnline.GeniusLyrics(f.Url); f.Plain = text; } catch { } }
                if (string.IsNullOrWhiteSpace(text)) continue;
                var lines = text.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0 && !(x.StartsWith("[") && x.EndsWith("]"))).ToList();
                if (lines.Count < 4) continue;
                var r = Sync.Align(lines, heard);
                if (bestR == null || r.Confidence > bestR.Confidence) { best = f; bestR = r; }
            }
            return bestR != null && bestR.Confidence >= 0.45 ? (best, bestR) : (null, null);
        }
    }

    sealed partial class MainForm
    {
        public string DiscordStatus =>
            !Prefs.Discord ? "" : Prefs.DiscordApp.Trim().Length == 0 ? "Add the Application ID to turn it on" :
            discord == null ? "" : discord.Status == "on" ? "Connected to Discord" : discord.Status == "starting" ? "Connecting…" : discord.Status;

        public void ImportTracker(List<Track> only = null)
        {
            using (var f = new TrackerForm(this, only)) f.ShowDialog(this);
        }

        // ---------- timing every song in the background ----------
        Thread autoSync;
        volatile bool autoSyncStop;
        public string AutoSyncStatus = "";
        public volatile bool TranscribeOpen;       // the Lyrics in time window is busy: wait
#if WPF
        static bool FullScreenOpen => FullLyricsForm.Open > 0 || Aura.Wpf.WpfUi.StageOpen;
#else
        static bool FullScreenOpen => FullLyricsForm.Open > 0;
#endif

        static bool NeedsWords(Lyrics l) => l != null && !l.ByWord && LinesOf(l).Count > 0;
        static List<string> LinesOf(Lyrics l) =>
            (l.Timed ? l.Lines.Select(x => x.Text) : (l.Plain ?? "").Split('\n')).Select(x => x.Trim()).Where(x => x.Length > 0 && !(x.StartsWith("[") && x.EndsWith("]"))).ToList();

        public void AutoSyncChanged()
        {
            if (Prefs.AutoSyncAll && autoSync == null) StartAutoSync();
            else if (!Prefs.AutoSyncAll) { autoSyncStop = true; AutoSyncStatus = ""; }
        }

        void StartAutoSync()
        {
            autoSyncStop = false;
            var ctx = SynchronizationContext.Current;
            Lib.Db.Exec("CREATE TABLE IF NOT EXISTS autosync_tried (track_id INTEGER PRIMARY KEY, ts REAL, ok INTEGER)");
            void Say(string s) { AutoSyncStatus = s; ctx?.Post(_ => { if (Current?.Kind == "settings") Page?.Invalidate(); }, null); }
            autoSync = new Thread(() =>
            {
                Perf.Here("timing lyrics in the background");
                try
                {
                    int done = 0;
                    // the songs to do, worked out once (and again every half hour) — not by re-reading every song's
                    // lyrics before each one, which read thousands of files over and over
                    var todo = new Queue<long>(); DateTime listed = DateTime.MinValue;
                    while (!autoSyncStop)
                    {
                        // in the background it always uses the small "base" model: big models take gigabytes of memory
                        // and lots of processor, and lining up lyrics you already have doesn't need them
                        var m = Whisper.Models.First(x => x.Name == "base");
                        if (!m.Ready)
                        {
                            Say("Downloading the small speech model for timing in the background (148 MB, once)…");
                            try { Whisper.Download(m, p => Say("Downloading the small speech model (148 MB, once)… " + (p * 100).ToString("0") + "%"), () => autoSyncStop); }
                            catch (Exception e) { if (autoSyncStop) break; Say("Couldn't download the small speech model (" + e.Message + "). Trying again in a while."); Thread.Sleep(300000); continue; }
                        }
                        if (!Whisper.Load()) { Say("Not available: " + Whisper.LoadError + "."); return; }
                        if (TranscribeOpen || FullScreenOpen) { Thread.Sleep(5000); continue; }
                        if (todo.Count == 0 && (DateTime.Now - listed).TotalMinutes > 30)
                        {
                            listed = DateTime.Now;
                            var tried = new HashSet<long>(Lib.Db.Rows("SELECT track_id FROM autosync_tried").Select(r => Conv.L(r[0])));
                            var found = new HashSet<long>(Lib.Db.Rows("SELECT track_id FROM found_lyrics").Select(r => Conv.L(r[0])));
                            foreach (var t in Lib.Tracks.ToList())
                            {
                                if (autoSyncStop) break;
                                if (tried.Contains(t.Id) || !t.HasLyrics && !found.Contains(t.Id)) continue;
                                var l = Lyrics.Get(t);
                                // songs with no timings, and songs timed line by line only (they get word-by-word timings)
                                if (NeedsWords(l)) todo.Enqueue(t.Id);
                                Thread.Sleep(2);
                            }
                        }
                        if (todo.Count == 0) { Say(done > 0 ? "Timed " + Theme.Count(done, "song") + " — every song with lyrics has timings now." : "Every song with lyrics has timings."); Thread.Sleep(60000); continue; }
                        var next = Lib.Get(todo.Dequeue());
                        var lyr = next == null ? null : Lyrics.Get(next);
                        if (!NeedsWords(lyr)) continue;
                        Say("Timing " + next.Name + "  ·  " + Theme.Count(todo.Count + 1, "song") + " to go");
                        bool ok = false;
                        var took = System.Diagnostics.Stopwatch.StartNew();
                        try
                        {
                            var lines = LinesOf(lyr);
                            // never the vocal model here: that's another gigabyte or two, for every song
                            var pcm = Hearing.Pcm(next, false, (s2, k) => { }, () => autoSyncStop, out _);
                            var prompt = string.Join(" ", lines); if (prompt.Length > 220) prompt = prompt.Substring(0, 220);
                            // one core while music plays, two when it doesn't: the music and the screen come first
                            int threads = Player.IsPlaying ? 1 : 2;
                            var heard = Whisper.Listen(m.File, m.Dtw, pcm, "auto", prompt, 1, null, () => autoSyncStop || TranscribeOpen || FullScreenOpen, out _, threads);
                            var r = Sync.Align(lines, heard);
                            // lyrics that were already timed by line are only replaced by a sure match
                            if (r.Confidence >= (lyr.Timed ? 0.65 : 0.5) && r.Lines.Count(x => x.T >= 0) > 0)
                            {
                                var timed = r.Lines.Where(x => x.T >= 0).ToList();
                                var lrc = Sync.ToLrc(timed); var words = Sync.ToWords(timed);
                                var tt = next;
                                ctx.Send(_ => Lyrics.Save(tt, lrc, "AURA auto-sync", true, words), null);
                                ok = true; done++;
                            }
                        }
                        catch (OperationCanceledException) { if (autoSyncStop) break; todo.Enqueue(next.Id); Thread.Sleep(5000); continue; }   // full screen opened: do it later
                        catch (Exception e) { App.Log("auto-sync " + next.Name + ": " + e.Message); }
                        Lib.Db.Exec("INSERT OR REPLACE INTO autosync_tried (track_id,ts,ok) VALUES (?,?,?)", next.Id, Library.Now, ok ? 1 : 0);
                        // rest as long as the work took
                        for (long w = Math.Min(60000, Math.Max(2000, took.ElapsedMilliseconds)); w > 0 && !autoSyncStop; w -= 500) Thread.Sleep(500);
                    }
                }
                finally { autoSync = null; if (autoSyncStop) Say(""); }
            }) { IsBackground = true, Priority = ThreadPriority.Lowest, Name = "auto-sync" };
            autoSync.Start();
        }
    }
}
