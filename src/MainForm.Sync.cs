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
                try
                {
                    int done = 0;
                    while (!autoSyncStop)
                    {
                        var m = Whisper.Models.FirstOrDefault(x => x.Name == Prefs.WhisperModel && x.Ready) ?? Whisper.Models.FirstOrDefault(x => x.Ready);
                        if (m == null) { Say("Waiting for a speech model — time one song by hand first (it downloads the model)."); Thread.Sleep(30000); continue; }
                        if (!Whisper.Load()) { Say("Not available: " + Whisper.LoadError + "."); return; }
                        if (TranscribeOpen || FullScreenOpen || (Player.IsPlaying && Environment.ProcessorCount < 4)) { Thread.Sleep(5000); continue; }
                        var tried = new HashSet<long>(Lib.Db.Rows("SELECT track_id FROM autosync_tried").Select(r => Conv.L(r[0])));
                        Track next = null; Lyrics lyr = null; int left = 0;
                        foreach (var t in Lib.Tracks.ToList())
                        {
                            if (tried.Contains(t.Id) || !t.HasLyrics && !Lyrics.HasFound(t.Id)) continue;
                            var l = Lyrics.Get(t);
                            if (l == null || l.Timed || string.IsNullOrWhiteSpace(l.Plain)) continue;
                            left++;
                            if (next == null) { next = t; lyr = l; }
                        }
                        if (next == null) { Say(done > 0 ? "Timed " + Theme.Count(done, "song") + " — every song with lyrics has timings now." : "Every song with lyrics has timings."); Thread.Sleep(60000); continue; }
                        Say("Timing " + next.Name + "  ·  " + Theme.Count(left, "song") + " to go");
                        bool ok = false;
                        try
                        {
                            var lines = lyr.Plain.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0 && !(x.StartsWith("[") && x.EndsWith("]"))).ToList();
                            var pcm = Hearing.Pcm(next, Prefs.Separate == "always", (s, k) => { }, () => autoSyncStop, out _);
                            var prompt = string.Join(" ", lines); if (prompt.Length > 220) prompt = prompt.Substring(0, 220);
                            // in the background: a few cores only, so the music and the screen stay smooth
                            var heard = Whisper.Listen(m.File, m.Dtw, pcm, "auto", prompt, 1, null, () => autoSyncStop, out _, Math.Max(1, Environment.ProcessorCount / 4));
                            var r = Sync.Align(lines, heard);
                            if (r.Confidence >= 0.5 && r.Lines.Count(x => x.T >= 0) > 0)
                            {
                                var timed = r.Lines.Where(x => x.T >= 0).ToList();
                                var lrc = Sync.ToLrc(timed); var words = Sync.ToWords(timed);
                                var tt = next;
                                ctx.Send(_ => Lyrics.Save(tt, lrc, "AURA auto-sync", true, words), null);
                                ok = true; done++;
                            }
                        }
                        catch (OperationCanceledException) { break; }
                        catch (Exception e) { App.Log("auto-sync " + next.Name + ": " + e.Message); }
                        Lib.Db.Exec("INSERT OR REPLACE INTO autosync_tried (track_id,ts,ok) VALUES (?,?,?)", next.Id, Library.Now, ok ? 1 : 0);
                        Thread.Sleep(2000);
                    }
                }
                finally { autoSync = null; if (autoSyncStop) Say(""); }
            }) { IsBackground = true, Priority = ThreadPriority.Lowest, Name = "auto-sync" };
            autoSync.Start();
        }
    }
}
