using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Aura
{
    // Ratings, eras, bookmarks, listening history, undo, and searching inside lyrics.
    sealed partial class Library
    {
        void PrepareMore()
        {
            Db.Script(@"CREATE TABLE IF NOT EXISTS ratings (track_id INTEGER PRIMARY KEY, stars INTEGER);
CREATE TABLE IF NOT EXISTS eras (track_id INTEGER PRIMARY KEY, era TEXT);
CREATE TABLE IF NOT EXISTS bookmarks (id INTEGER PRIMARY KEY, track_id INTEGER, at REAL, label TEXT, added REAL);
CREATE TABLE IF NOT EXISTS lyrics_index (track_id INTEGER PRIMARY KEY, mtime REAL, text TEXT);
CREATE TABLE IF NOT EXISTS undo (id INTEGER PRIMARY KEY, label TEXT, ts REAL, data TEXT);
CREATE TABLE IF NOT EXISTS not_versions (key TEXT PRIMARY KEY);
CREATE TABLE IF NOT EXISTS song_info (track_id INTEGER PRIMARY KEY, info TEXT);
CREATE TABLE IF NOT EXISTS snippet_alerts (full_id INTEGER PRIMARY KEY, ts REAL, dismissed INTEGER DEFAULT 0);
CREATE TABLE IF NOT EXISTS version_groups (track_id INTEGER PRIMARY KEY, song TEXT, label TEXT)");
        }

        void LoadMore(Dictionary<long, Track> by)
        {
            foreach (var r in Db.Rows("SELECT track_id,stars FROM ratings"))
                if (by.TryGetValue(Conv.L(r[0]), out var t)) t.Rating = (int)Conv.L(r[1]);
            var mg = new Dictionary<long, string>();
            foreach (var r in Db.Rows("SELECT track_id,song FROM version_groups")) mg[Conv.L(r[0])] = Conv.S(r[1]);
            ManualGroups = mg;
            NotVersionKeys = new HashSet<string>(Db.Rows("SELECT key FROM not_versions").Select(r => Conv.S(r[0])));
            foreach (var r in Db.Rows("SELECT track_id,era FROM eras"))
                if (by.TryGetValue(Conv.L(r[0]), out var t)) t.Era = Conv.S(r[1]);
        }

        // ---------- ratings ----------
        public void SetRating(IEnumerable<Track> tracks, int stars)
        {
            stars = Math.Max(0, Math.Min(5, stars));
            Db.Tx(() =>
            {
                foreach (var t in tracks)
                {
                    t.Rating = stars;
                    if (stars == 0) Db.Exec("DELETE FROM ratings WHERE track_id=?", t.Id);
                    else Db.Exec("INSERT OR REPLACE INTO ratings (track_id,stars) VALUES (?,?)", t.Id, stars);
                }
            });
            RaiseChanged();
        }

        // ---------- eras ----------
        public void SetEra(IEnumerable<Track> tracks, string era, bool undo = true)
        {
            era = (era ?? "").Trim();
            var list = tracks.ToList();
            if (undo) SaveUndo("Set era", list);
            Db.Tx(() =>
            {
                foreach (var t in list)
                {
                    t.Era = era;
                    if (era.Length == 0) Db.Exec("DELETE FROM eras WHERE track_id=?", t.Id);
                    else Db.Exec("INSERT OR REPLACE INTO eras (track_id,era) VALUES (?,?)", t.Id, era);
                }
            });
            RaiseChanged();
        }

        public sealed class EraGroup { public string Name; public List<Track> Tracks = new List<Track>(); public string Year => Tracks.Select(t => t.Year).Where(y => y.Length >= 4).OrderBy(y => y).FirstOrDefault() ?? ""; public Track Cover => Tracks.OrderByDescending(t => t.Plays).FirstOrDefault(t => t.HasArt) ?? Tracks.FirstOrDefault(); }

        public List<EraGroup> Eras() => Tracks.Where(t => t.Era.Length > 0).GroupBy(t => t.Era, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new EraGroup { Name = g.First().Era, Tracks = g.OrderBy(t => t.Album).ThenBy(t => t.TrackNo).ThenBy(t => t.Name).ToList() })
            .OrderBy(e => e.Year.Length == 0 ? "9999" : e.Year).ThenBy(e => e.Name).ToList();

        public void RenameEra(string from, string to)
        {
            SetEra(Tracks.Where(t => string.Equals(t.Era, from, StringComparison.CurrentCultureIgnoreCase)), to);
        }

        // ---------- notes about a song (producer, recorded, leaked — from a fan tracker) ----------
        readonly Dictionary<long, string> info = new Dictionary<long, string>();
        bool infoLoaded;
        public string InfoOf(Track t)
        {
            lock (info)
            {
                if (!infoLoaded) { foreach (var r in Db.Rows("SELECT track_id,info FROM song_info")) info[Conv.L(r[0])] = Conv.S(r[1]); infoLoaded = true; }
                return info.TryGetValue(t.Id, out var s) ? s : "";
            }
        }
        public void SetInfo(Track t, string text)
        {
            InfoOf(t);
            lock (info)
            {
                if (string.IsNullOrWhiteSpace(text)) { Db.Exec("DELETE FROM song_info WHERE track_id=?", t.Id); info.Remove(t.Id); }
                else { Db.Exec("INSERT OR REPLACE INTO song_info (track_id,info) VALUES (?,?)", t.Id, text.Trim()); info[t.Id] = text.Trim(); }
            }
        }

        // ---------- versions grouped by hand ----------
        public static Dictionary<long, string> ManualGroups = new Dictionary<long, string>();

        public void GroupVersions(IEnumerable<Track> tracks, string song)
        {
            var list = tracks.ToList();
            if (list.Count == 0) return;
            string key = LyricsOnline.Norm(song) + "|" + LyricsOnline.Norm(list[0].MainArtist);
            Db.Tx(() => { foreach (var t in list) Db.Exec("INSERT OR REPLACE INTO version_groups (track_id,song,label) VALUES (?,?,'')", t.Id, key); });
            foreach (var t in list) ManualGroups[t.Id] = key;
            RaiseChanged();
        }

        public void Ungroup(Track t) { Db.Exec("DELETE FROM version_groups WHERE track_id=?", t.Id); ManualGroups.Remove(t.Id); RaiseChanged(); }

        public static HashSet<string> NotVersionKeys = new HashSet<string>();
        public bool IsNotVersions(string key) => NotVersionKeys.Contains(key);
        public void MarkNotVersions(string key)
        {
            Db.Exec("INSERT OR IGNORE INTO not_versions (key) VALUES (?)", key);
            NotVersionKeys.Add(key);
            // songs grouped by hand under this key go back to on their own too
            foreach (var id in ManualGroups.Where(kv => kv.Value == key).Select(kv => kv.Key).ToList()) { Db.Exec("DELETE FROM version_groups WHERE track_id=?", id); ManualGroups.Remove(id); }
            RaiseChanged();
        }

        static string TitleKey(Track t) => LyricsOnline.Norm(LyricsOnline.CleanTitle(t.Title.Length > 0 ? t.Title : Path.GetFileNameWithoutExtension(t.Path)));

        // Maybe versions: the same song title under artist names that don't quite match ("Juice WRLD" / "Juice Wrld ft. X" /
        // no artist at all). Shown on the Versions page to group with one click, or to dismiss.
        public List<List<Track>> SuggestedVersions()
        {
            var res = new List<List<Track>>();
            foreach (var g in Tracks.GroupBy(TitleKey))
            {
                if (g.Key.Length < 3 || NotVersionKeys.Contains("s:" + g.Key)) continue;
                var keys = g.Select(VersionKey).Distinct().ToList();
                if (keys.Count < 2) continue;
                // the artists must look alike: same first word, or one of them unknown
                string First(Track t) { var a = LyricsOnline.Norm(t.MainArtist); return a.Split(' ')[0]; }
                var firsts = g.Select(First).Where(a => a.Length > 0 && a != "unknown").Distinct().ToList();
                if (firsts.Count > 1) continue;
                res.Add(g.OrderByDescending(t => t.Plays).ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
            }
            return res.OrderBy(g => g[0].Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        public void DismissSuggestion(List<Track> g) { if (g.Count > 0) MarkNotVersions("s:" + TitleKey(g[0])); }

        // ---------- snippets that turn up in full ----------
        public static bool IsSnippet(Track t) => (t.Title + " " + Path.GetFileNameWithoutExtension(t.Path)).IndexOf("snippet", StringComparison.OrdinalIgnoreCase) >= 0;

        public sealed class InFull { public Track Full; public List<Track> Snippets; public bool Told; }

        // full songs that you also have snippets of (not dismissed yet)
        public List<InFull> NowInFull()
        {
            var rows = Db.Rows("SELECT full_id, dismissed FROM snippet_alerts").ToDictionary(r => Conv.L(r[0]), r => Conv.L(r[1]) != 0);
            var res = new List<InFull>();
            foreach (var g in Tracks.GroupBy(TitleKey))
            {
                if (g.Key.Length < 2) continue;
                var snips = g.Where(IsSnippet).ToList();
                if (snips.Count == 0) continue;
                foreach (var f in g.Where(t => !IsSnippet(t)))
                {
                    if (rows.TryGetValue(f.Id, out var dismissed) && dismissed) continue;
                    string First(Track t) => LyricsOnline.Norm(t.MainArtist).Split(' ')[0];
                    var same = snips.Where(sn => First(sn).Length == 0 || First(f).Length == 0 || First(sn) == First(f) || First(sn) == "unknown" || First(f) == "unknown").ToList();
                    if (same.Count > 0) res.Add(new InFull { Full = f, Snippets = same, Told = rows.ContainsKey(f.Id) });
                }
            }
            return res;
        }
        public void ToldInFull(Track full) => Db.Exec("INSERT OR IGNORE INTO snippet_alerts (full_id,ts,dismissed) VALUES (?,?,0)", full.Id, Now);
        public void DismissInFull(Track full) => Db.Exec("INSERT OR REPLACE INTO snippet_alerts (full_id,ts,dismissed) VALUES (?,?,1)", full.Id, Now);

        // the version part of a title: "Redline (v2) [CDQ]" -> "v2 CDQ"
        public static string VersionLabel(Track t)
        {
            var name = t.Title.Length > 0 ? t.Title : Path.GetFileNameWithoutExtension(t.Path);
            var bare = LyricsOnline.CleanTitle(name);
            string rest = name;
            int at = name.IndexOf(bare, StringComparison.CurrentCultureIgnoreCase);
            if (bare.Length > 0 && at >= 0) rest = name.Remove(at, bare.Length);
            rest = System.Text.RegularExpressions.Regex.Replace(rest, @"[\(\)\[\]\{\}]|^\s*[-–—_.]+|[-–—_.]+\s*$", " ");
            return System.Text.RegularExpressions.Regex.Replace(rest, @"\s+", " ").Trim(' ', '-', '–', '—', '_', '.');
        }

        // distinct, readable titles for one song's versions: "Song (v2)", "Song (Snippet)"; several unlabelled takes
        // become Version 1, Version 2… oldest first (like the first AURA)
        public static Dictionary<long, string> VersionTitles(string song, List<Track> tracks)
        {
            var ordered = tracks.OrderBy(t => t.Mtime).ThenBy(t => t.Path, StringComparer.OrdinalIgnoreCase).ToList();
            int unlabeled = ordered.Count(t => VersionLabel(t).Length == 0), n = 0;
            var used = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
            var outp = new Dictionary<long, string>();
            foreach (var t in ordered)
            {
                var label = VersionLabel(t);
                if (label.Length == 0 && unlabeled > 1) label = "Version " + (++n);
                var title = label.Length > 0 ? song + " (" + label + ")" : song;
                if (used.TryGetValue(title, out var c)) { used[title] = c + 1; title = song + " (" + (label.Length > 0 ? label : "Version") + " " + (c + 1) + ")"; }
                else used[title] = 1;
                outp[t.Id] = title;
            }
            return outp;
        }

        // ---------- bookmarks ----------
        public sealed class Bookmark { public long Id, TrackId; public double At; public string Label = ""; }

        readonly Dictionary<long, List<Bookmark>> marks = new Dictionary<long, List<Bookmark>>();
        public List<Bookmark> BookmarksOf(long trackId)
        {
            if (marks.TryGetValue(trackId, out var l)) return l;
            l = Db.Rows("SELECT id,track_id,at,label FROM bookmarks WHERE track_id=? ORDER BY at", trackId)
                  .Select(r => new Bookmark { Id = Conv.L(r[0]), TrackId = Conv.L(r[1]), At = Conv.D(r[2]), Label = Conv.S(r[3]) }).ToList();
            if (marks.Count > 200) marks.Clear();
            marks[trackId] = l;
            return l;
        }

        public void AddBookmark(Track t, double at, string label = "")
        {
            // one bookmark per spot (within a second)
            if (BookmarksOf(t.Id).Any(b => Math.Abs(b.At - at) < 1)) return;
            Db.Exec("INSERT INTO bookmarks (track_id,at,label,added) VALUES (?,?,?,?)", t.Id, at, label ?? "", Now);
            marks.Remove(t.Id);
        }

        public void RemoveBookmark(long id) { Db.Exec("DELETE FROM bookmarks WHERE id=?", id); marks.Clear(); }

        // ---------- history ----------
        public List<(Track t, double ts)> History(int n)
        {
            var list = new List<(Track, double)>();
            foreach (var r in Db.Rows("SELECT track_id,ts FROM history ORDER BY id DESC LIMIT ?", n))
                if (ById.TryGetValue(Conv.L(r[0]), out var t)) list.Add((t, Conv.D(r[1])));
            return list;
        }

        public void ClearHistory() { Db.Exec("DELETE FROM history"); RaiseChanged(); }

        // ---------- undo: tag changes are written down before they happen ----------
        // a snapshot of the tags (and era/rating) of each song
        public void SaveUndo(string label, IEnumerable<Track> tracks)
        {
            var sb = new StringBuilder();
            foreach (var t in tracks)
                sb.Append(string.Join("\u001f", new[] { t.Id.ToString(), t.Path, t.Title, t.Artist, t.Album, t.AlbumArtist, t.Year, t.Genre, t.TrackNo.ToString(), t.Era, t.Rating.ToString() }.Select(x => (x ?? "").Replace("\u001f", " ").Replace("\u001e", " ")))).Append('\u001e');
            Db.Exec("INSERT INTO undo (label,ts,data) VALUES (?,?,?)", label, Now, sb.ToString());
            Db.Exec("DELETE FROM undo WHERE id NOT IN (SELECT id FROM undo ORDER BY id DESC LIMIT 20)");
        }

        public sealed class UndoStep { public long Id; public string Label; public double Ts; public List<string[]> Rows = new List<string[]>(); }

        public List<UndoStep> UndoSteps() =>
            Db.Rows("SELECT id,label,ts,data FROM undo ORDER BY id DESC").Select(r => new UndoStep
            {
                Id = Conv.L(r[0]), Label = Conv.S(r[1]), Ts = Conv.D(r[2]),
                Rows = Conv.S(r[3]).Split(new[] { '\u001e' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Split('\u001f')).Where(x => x.Length >= 11).ToList()
            }).ToList();

        public void DropUndo(long id) => Db.Exec("DELETE FROM undo WHERE id=?", id);

        // ---------- searching inside lyrics ----------
        static readonly Regex Stamp = new Regex(@"\[[0-9:.]+\]|<[0-9:.]+>");
        public static string NormLyrics(string s) => Regex.Replace(Stamp.Replace(s ?? "", " ").ToLowerInvariant().Replace("’", "'").Replace("'", ""), @"[^\p{L}\p{N}]+", " ").Trim();

        public void IndexLyrics(Track t, string text) => Db.Exec("INSERT OR REPLACE INTO lyrics_index (track_id,mtime,text) VALUES (?,?,?)", t.Id, t.Mtime, NormLyrics(text));

        // songs whose lyrics have all these words, in this order when it's a phrase
        public HashSet<long> SearchLyrics(string query)
        {
            var q = NormLyrics(query);
            var hits = new HashSet<long>();
            if (q.Length < 3) return hits;
            foreach (var r in Db.Rows("SELECT track_id FROM lyrics_index WHERE text LIKE ?", "%" + q + "%")) hits.Add(Conv.L(r[0]));
            return hits;
        }

        // fills the lyrics index in the background (reads the lyrics of songs that have them)
        public void StartLyricsIndex(Func<Track, string> text)
        {
            var th = new Thread(() =>
            {
                Perf.Here("lyrics search index");
                try
                {
                    var have = new Dictionary<long, double>();
                    foreach (var r in Db.Rows("SELECT track_id,mtime FROM lyrics_index")) have[Conv.L(r[0])] = Conv.D(r[1]);
                    foreach (var t in Tracks.ToList())
                    {
                        if (have.TryGetValue(t.Id, out var m) && Math.Abs(m - t.Mtime) < 2) continue;
                        string s = null;
                        try { s = text(t); } catch { }
                        IndexLyrics(t, s ?? "");
                        Thread.Sleep(5);
                    }
                }
                catch (Exception e) { App.Log("lyrics index: " + e.Message); }
            }) { IsBackground = true, Priority = ThreadPriority.Lowest, Name = "lyrics index" };
            th.Start();
        }
    }
}
