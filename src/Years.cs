using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Aura
{
    // Filling in a song's year by looking it up: MusicBrainz (the first time the song came out), Apple Music, and
    // Genius (which also knows leaks and unreleased songs). Only when the title and artist both match.
    static class YearLookup
    {
        static string MbUA => "AURA-Native/" + App.Version + " ( https://github.com/evloc/AuraNative )";
        static bool Same(string a, string b) => LyricsOnline.Sim(LyricsOnline.Norm(LyricsOnline.CleanTitle(a ?? "")), LyricsOnline.Norm(LyricsOnline.CleanTitle(b ?? ""))) >= 0.9;
        static int YearOf(string date) => date != null && date.Length >= 4 && int.TryParse(date.Substring(0, 4), out var y) && y > 1900 && y <= DateTime.Now.Year + 1 ? y : 0;

        // the earliest year any of them knows (a song re-released later shows up again with a later date); 0 when none
        public static (int year, string source) Find(Track t)
        {
            string title = LyricsOnline.CleanTitle(t.Name), artist = t.MainArtist;
            if (title.Length == 0 || artist.Length == 0 || artist == "Unknown artist") return (0, "");
            var found = new List<(int, string)>();
            try
            {
                var q = "recording:\"" + title.Replace("\"", "") + "\" AND artist:\"" + artist.Replace("\"", "") + "\"";
                var mb = Net.Json<MbRecordings>(Net.Get(CoverSearch.MusicBrainzUrl + "/ws/2/recording?fmt=json&limit=10&query=" + Uri.EscapeDataString(q), 9000, MbUA))?.Recordings;
                foreach (var r in mb ?? new List<MbRecording>())
                    if (r.Score >= 85 && Same(r.Title, title) && LyricsOnline.ArtistOk(artist, string.Join(" ", (r.Credit ?? new List<MbCredit>()).Select(c => c.Name))) && YearOf(r.First) > 0)
                        found.Add((YearOf(r.First), "MusicBrainz"));
            }
            catch (Exception e) { App.Log("year (MusicBrainz) " + t.Name + ": " + e.Message); }
            try
            {
                var it = Net.Json<ItunesResp>(Net.Get(CoverSearch.ItunesUrl + "/search?media=music&entity=song&limit=10&term=" + Uri.EscapeDataString(artist + " " + title)))?.Results;
                foreach (var r in it ?? new List<ItunesItem>())
                    if (Same(r.Track, title) && LyricsOnline.ArtistOk(artist, r.Artist ?? "") && YearOf(r.Released) > 0) found.Add((YearOf(r.Released), "Apple Music"));
            }
            catch (Exception e) { App.Log("year (Apple) " + t.Name + ": " + e.Message); }
            try
            {
                foreach (var (type, r) in LyricsOnline.GeniusSearch(artist + " " + title))
                    if (type == "song" && Same(r.Title, title) && LyricsOnline.ArtistOk(artist, r.Primary?.Name ?? r.ArtistNames ?? "") && (r.Released?.Year ?? 0) > 1900)
                        found.Add((r.Released.Year.Value, "Genius"));
            }
            catch (Exception e) { App.Log("year (Genius) " + t.Name + ": " + e.Message); }
            return found.Count == 0 ? (0, "") : found.OrderBy(f => f.Item1).First();
        }
    }

    sealed partial class MainForm
    {
        Thread yearThread;
        volatile bool yearStop;
        public string YearStatus = "";

        public void YearsChanged()
        {
            if (Prefs.AutoYears && yearThread == null) StartYears();
            else if (!Prefs.AutoYears) { yearStop = true; YearStatus = ""; }
        }

        void StartYears()
        {
            yearStop = false;
            var ctx = SynchronizationContext.Current;
            Lib.Db.Exec("CREATE TABLE IF NOT EXISTS year_tried (track_id INTEGER PRIMARY KEY, ts REAL, year INTEGER)");
            void Say(string s) { YearStatus = s; ctx?.Post(_ => { if (Current?.Kind == "settings") Page?.Invalidate(); }, null); }
            yearThread = new Thread(() =>
            {
                Perf.Here("looking up years");
                try
                {
                    Thread.Sleep(15000);            // let AURA finish starting first
                    int filled = 0;
                    var batch = new List<(Track, TagEdit)>();
                    void Flush()
                    {
                        if (batch.Count == 0) return;
                        var jobs = batch.ToList(); batch.Clear();
                        // write when nothing else is being written (all of them as one step Ctrl+Z can undo)
                        while (!yearStop) { bool busy = true; ctx.Send(_ => busy = Writing, null); if (!busy) break; Thread.Sleep(2000); }
                        ctx.Send(_ => RunWrites(jobs, "Filled in " + Theme.Count(jobs.Count, "year") + " from the internet"), null);
                    }
                    while (!yearStop)
                    {
                        var tried = new HashSet<long>(Lib.Db.Rows("SELECT track_id FROM year_tried").Select(r => Conv.L(r[0])));
                        var todo = Lib.Tracks.Where(t => t.Year.Trim().Length == 0 && !tried.Contains(t.Id) && TagWriter.CanWrite(t.Path)).ToList();
                        if (todo.Count == 0) { Flush(); Say(filled > 0 ? "Filled in " + Theme.Count(filled, "year") + " — that's every year it could find." : "Every song it can look up has a year."); Thread.Sleep(600000); continue; }
                        foreach (var t in todo)
                        {
                            if (yearStop) break;
                            Say("Looking up " + t.Name + "  ·  " + Theme.Count(todo.Count, "song") + " without a year" + (filled > 0 ? "  ·  filled " + filled : ""));
                            var (y, src) = YearLookup.Find(t);
                            Lib.Db.Exec("INSERT OR REPLACE INTO year_tried (track_id,ts,year) VALUES (?,?,?)", t.Id, Library.Now, y);
                            if (y > 0 && t.Year.Trim().Length == 0) { batch.Add((t, new TagEdit { Year = y.ToString() })); filled++; App.Log("year: " + t.Name + " = " + y + " (" + src + ")"); }
                            if (batch.Count >= 10) Flush();
                            // MusicBrainz allows about one question a second
                            for (int w = 0; w < 4 && !yearStop; w++) Thread.Sleep(400);
                        }
                        Flush();
                    }
                }
                catch (Exception e) { App.Log("years: " + e.Message); }
                finally { yearThread = null; }
            }) { IsBackground = true, Priority = ThreadPriority.Lowest, Name = "years" };
            yearThread.Start();
        }
    }
}
