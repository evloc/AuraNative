using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Aura
{
    sealed class DupGroup
    {
        public string Kind;                // "exact", "name", "sound"
        public List<Track> Tracks = new List<Track>();
        public Track Keep;
        public bool Checked;               // confirmed by listening (or by being byte-for-byte the same)
        public string Key => string.Join(",", Tracks.Select(t => t.Id).OrderBy(x => x));
        public string Title => Kind == "exact" ? "Exact copies" : Kind == "sound" ? "Sounds the same" : "Same song, different file";
    }

    // Finds the same song saved more than once. Different versions (v1, v2, snippet, remix, ...) never count.
    static class Duplicates
    {
        static readonly Regex Marker = new Regex(@"\b(v\s?\d+|ver(?:sion)?\s?\d+|snippet|demo|og|leak(?:ed)?|session|alt(?:ernate)?|instrumental|inst|acapella|a\s?cappella|remix|rmx|sped\s?up|slowed|reverb|nightcore|live|edit|radio edit|extended|clean|explicit|dirty|mix\s?\d+|take\s?\d+|ref(?:erence)?|mastered|unmastered|rough|vip|intro|outro|interlude|pt\s?\d+|part\s?\d+|\d{1,2}x)\b", RegexOptions.IgnoreCase);

        // the version words in a song's title and file name: two songs with different ones are different versions
        public static string Markers(Track t)
        {
            var text = (t.Title + " " + Path.GetFileNameWithoutExtension(t.Path)).ToLowerInvariant().Replace('_', ' ');
            var set = new SortedSet<string>();
            foreach (Match m in Marker.Matches(text))
            {
                var w = Regex.Replace(m.Value.ToLowerInvariant(), @"\s+", "");
                w = Regex.Replace(w, @"^ver(sion)?(\d)", "v$2");
                if (w == "rmx") w = "remix"; else if (w == "inst") w = "instrumental"; else if (w == "leaked") w = "leak";
                else if (w.StartsWith("part")) w = "pt" + w.Substring(4);
                set.Add(w);
            }
            return string.Join("+", set);
        }

        public static bool Lossless(Track t) => new[] { ".flac", ".wav", ".aif", ".aiff" }.Contains(Path.GetExtension(t.Path).ToLowerInvariant());

        // kilobits a second, roughly
        public static int Kbps(Track t) => t.Duration > 1 ? (int)(t.Size * 8 / t.Duration / 1000) : 0;

        // which copy to keep: the best sound, then the most complete tags, then the most played
        public static double Score(Track t)
        {
            double s = Lossless(t) ? 2000 : Math.Min(Kbps(t), 400) * 3;
            if (t.HasArt) s += 40;
            if (t.HasLyrics) s += 40;
            if (t.Artist.Length > 0) s += 15;
            if (t.Album.Length > 0) s += 10;
            if (t.Year.Length > 0) s += 5;
            s += Math.Min(t.Plays, 50);
            if (t.Path.IndexOf("_duplicates", StringComparison.OrdinalIgnoreCase) >= 0) s -= 5000;
            return s;
        }

        public static void Prepare(Library lib)
        {
            lib.Db.Script(@"CREATE TABLE IF NOT EXISTS file_hash (track_id INTEGER PRIMARY KEY, size INTEGER, mtime REAL, sha1 TEXT);
CREATE TABLE IF NOT EXISTS dup_ignore (a INTEGER, b INTEGER, PRIMARY KEY (a, b))");
        }

        static string Hash(Library lib, Track t)
        {
            var r = lib.Db.Rows("SELECT size,mtime,sha1 FROM file_hash WHERE track_id=?", t.Id).FirstOrDefault();
            if (r != null && Conv.L(r[0]) == t.Size && Math.Abs(Conv.D(r[1]) - t.Mtime) < 2) return Conv.S(r[2]);
            string h;
            using (var f = FileX.OpenRead(t.Path, 1 << 20))
            using (var sha = SHA1.Create()) h = BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "");
            lib.Db.Exec("INSERT OR REPLACE INTO file_hash (track_id,size,mtime,sha1) VALUES (?,?,?,?)", t.Id, t.Size, t.Mtime, h);
            return h;
        }

        public static List<DupGroup> Find(Library lib, SoundStore sound, Action<string> progress = null)
        {
            Prepare(lib);
            var tracks = lib.Tracks.Where(t => t.Path.IndexOf("\\_duplicates\\", StringComparison.OrdinalIgnoreCase) < 0 && t.Path.IndexOf("/_duplicates/", StringComparison.Ordinal) < 0).ToList();
            var ignored = new HashSet<(long, long)>();
            foreach (var r in lib.Db.Rows("SELECT a,b FROM dup_ignore")) ignored.Add((Conv.L(r[0]), Conv.L(r[1])));
            bool Ignored(Track a, Track b) => ignored.Contains((Math.Min(a.Id, b.Id), Math.Max(a.Id, b.Id)));

            var parent = new Dictionary<long, long>();
            var kind = new Dictionary<(long, long), string>();
            long Root(long x)
            {
                if (!parent.ContainsKey(x)) return x;
                while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
                return x;
            }
            void Link(Track a, Track b, string k)
            {
                if (a.Id == b.Id || Ignored(a, b)) return;
                if (!parent.ContainsKey(a.Id)) parent[a.Id] = a.Id;
                if (!parent.ContainsKey(b.Id)) parent[b.Id] = b.Id;
                var key = (Math.Min(a.Id, b.Id), Math.Max(a.Id, b.Id));
                if (!kind.ContainsKey(key) || Rank(k) > Rank(kind[key])) kind[key] = k;
                long ra = Root(a.Id), rb = Root(b.Id);
                if (ra != rb) parent[ra] = rb;
            }
            var prints = new Dictionary<long, SongSound>();
            SongSound S(Track t) { if (!prints.TryGetValue(t.Id, out var s)) prints[t.Id] = s = sound?.Get(t); return s; }

            // 1. byte-for-byte copies
            progress?.Invoke("Comparing files…");
            foreach (var g in tracks.Where(t => t.Size > 0).GroupBy(t => t.Size).Where(g => g.Count() > 1))
            {
                var byHash = new Dictionary<string, Track>();
                foreach (var t in g)
                {
                    string h;
                    try { h = Hash(lib, t); } catch { continue; }
                    if (byHash.TryGetValue(h, out var other)) Link(other, t, "exact"); else byHash[h] = t;
                }
            }

            // 2. same title, artist and version words, about the same length
            progress?.Invoke("Comparing names…");
            foreach (var g in tracks.GroupBy(t => Library.VersionKey(t) + "#" + Markers(t)).Where(g => g.Count() > 1 && !g.Key.StartsWith("#")))
            {
                var list = g.OrderBy(t => t.Duration).ToList();
                for (int i = 0; i < list.Count; i++)
                    for (int j = i + 1; j < list.Count && list[j].Duration - list[i].Duration <= 2.5; j++)
                    {
                        if (list[i].Duration <= 0 || list[j].Duration <= 0) continue;
                        var a = S(list[i]); var b = S(list[j]);
                        if (a != null && b != null && SongSound.Match(a, b, 6).ber > 0.3) continue;       // they sound different: not the same take
                        Link(list[i], list[j], "name");
                    }
            }

            // 3. sounds the same, whatever the names say
            progress?.Invoke("Comparing how songs sound…");
            var heard = tracks.Where(t => sound != null && sound.Has(t.Id)).Select(t => (t, s: S(t))).Where(x => x.s != null && x.s.Print.Length > 400).OrderBy(x => x.s.Length).ToList();
            // quick look first: loudness second by second (lined up within ±5 s) must be nearly the same
            var coarse = heard.ToDictionary(x => x.t.Id, x => SongSound.Coarse(x.s.Env, (int)SongSound.Fps));
            var loud = coarse.ToDictionary(kv => kv.Key, kv => kv.Value.Length == 0 ? 0 : kv.Value.Average());
            double Mad(double[] a, double[] b, int o)
            {
                int from = Math.Max(0, -o), to = Math.Min(a.Length, b.Length - o);
                if (to - from < 10) return 999;
                double sum = 0; for (int i = from; i < to; i++) sum += Math.Abs(a[i] - b[i + o]);
                return sum / (to - from);
            }
            for (int i = 0; i < heard.Count; i++)
                for (int j = i + 1; j < heard.Count && heard[j].s.Length - heard[i].s.Length <= 3; j++)
                {
                    var (ta, sa) = heard[i]; var (tb, sb) = heard[j];
                    if (parent.ContainsKey(ta.Id) && parent.ContainsKey(tb.Id) && Root(ta.Id) == Root(tb.Id)) continue;
                    if (Math.Abs(loud[ta.Id] - loud[tb.Id]) > 15) continue;
                    if (Markers(ta) != Markers(tb)) continue;
                    var ca = coarse[ta.Id]; var cb = coarse[tb.Id];
                    double best = 999; for (int o = -5; o <= 5; o++) best = Math.Min(best, Mad(ca, cb, o));
                    if (best > 10) continue;
                    if (SongSound.Match(sa, sb, 6).ber < 0.15) Link(ta, tb, "sound");
                }

            // gather the groups
            var groups = new Dictionary<long, DupGroup>();
            foreach (var t in tracks)
            {
                if (!parent.ContainsKey(t.Id)) continue;
                var r = Root(t.Id);
                if (!groups.TryGetValue(r, out var g)) groups[r] = g = new DupGroup();
                g.Tracks.Add(t);
            }
            var outp = new List<DupGroup>();
            foreach (var g in groups.Values.Where(g => g.Tracks.Count > 1))
            {
                var kinds = new List<string>();
                for (int i = 0; i < g.Tracks.Count; i++)
                    for (int j = i + 1; j < g.Tracks.Count; j++)
                        if (kind.TryGetValue((Math.Min(g.Tracks[i].Id, g.Tracks[j].Id), Math.Max(g.Tracks[i].Id, g.Tracks[j].Id)), out var k)) kinds.Add(k);
                g.Kind = kinds.All(k => k == "exact") ? "exact" : kinds.Any(k => k == "sound") ? "sound" : kinds.Any(k => k == "name") ? "name" : "exact";
                // checked: every copy is the same by bytes or by listening
                g.Checked = g.Kind != "name" || g.Tracks.All(t => S(t) != null);
                g.Tracks = g.Tracks.OrderByDescending(Score).ThenBy(t => t.Added).ToList();
                g.Keep = g.Tracks[0];
                outp.Add(g);
            }
            return outp.OrderBy(g => g.Keep.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        static int Rank(string k) => k == "exact" ? 3 : k == "sound" ? 2 : 1;

        public static void Ignore(Library lib, DupGroup g)
        {
            Prepare(lib);
            lib.Db.Tx(() =>
            {
                foreach (var a in g.Tracks) foreach (var b in g.Tracks)
                        if (a.Id < b.Id) lib.Db.Exec("INSERT OR IGNORE INTO dup_ignore (a,b) VALUES (?,?)", a.Id, b.Id);
            });
        }

        // keeps one copy; the others go into a _duplicates folder next to them, and everything
        // they had (plays, playlists, favorite, found lyrics) moves to the one that's kept
        public static int Merge(Library lib, SoundStore sound, LyricsStore lyrics, DupGroup g, Track keep)
        {
            int moved = 0;
            foreach (var t in g.Tracks.Where(x => x.Id != keep.Id).ToList())
            {
                string dest;
                try
                {
                    var dir = Path.Combine(Path.GetDirectoryName(t.Path), "_duplicates");
                    Directory.CreateDirectory(FileX.Long(dir));
                    dest = Path.Combine(dir, Path.GetFileName(t.Path));
                    for (int n = 2; FileX.Exists(dest); n++) dest = Path.Combine(dir, Path.GetFileNameWithoutExtension(t.Path) + " (" + n + ")" + Path.GetExtension(t.Path));
                    File.Move(FileX.Long(t.Path), FileX.Long(dest));
                    var side = LyricsStore.Sidecar(t.Path);
                    if (side != null) try { File.Move(FileX.Long(side), FileX.Long(Path.ChangeExtension(dest, ".lrc"))); } catch { }
                }
                catch (Exception e) { App.Log("couldn't move duplicate " + t.Path + ": " + e.Message); throw new IOException("Couldn't move " + Path.GetFileName(t.Path) + ": " + e.Message); }
                MergeInto(lib, t, keep);
                sound?.Forget(t.Id);
                lyrics?.Forget(keep.Id);
                App.Log("duplicate " + t.Path + " moved to " + dest + "; kept " + keep.Path);
                moved++;
            }
            lib.Load();
            return moved;
        }

        static void MergeInto(Library lib, Track from, Track into)
        {
            var db = lib.Db;
            db.Tx(() =>
            {
                // plays add up
                var p = db.Rows("SELECT count,last_played FROM plays WHERE track_id=?", from.Id).FirstOrDefault();
                if (p != null)
                {
                    db.Exec("INSERT OR IGNORE INTO plays (track_id,count,last_played) VALUES (?,0,0)", into.Id);
                    db.Exec("UPDATE plays SET count=count+?, last_played=max(last_played,?) WHERE track_id=?", Conv.L(p[0]), Conv.D(p[1]), into.Id);
                    db.Exec("DELETE FROM plays WHERE track_id=?", from.Id);
                }
                db.Exec("UPDATE history SET track_id=? WHERE track_id=?", into.Id, from.Id);
                // a favorite stays a favorite
                if (Conv.L(db.Scalar("SELECT COUNT(*) FROM favorites WHERE track_id=?", from.Id)) > 0)
                {
                    db.Exec("INSERT OR IGNORE INTO favorites (track_id,added) SELECT ?,added FROM favorites WHERE track_id=?", into.Id, from.Id);
                    db.Exec("DELETE FROM favorites WHERE track_id=?", from.Id);
                }
                // playlists point at the kept copy, once, at the earlier of the two places
                foreach (var r in db.Rows("SELECT DISTINCT playlist_id FROM playlist_items WHERE track_id=?", from.Id))
                {
                    long pl = Conv.L(r[0]);
                    long first = Conv.L(db.Scalar("SELECT MIN(pos) FROM playlist_items WHERE playlist_id=? AND track_id=?", pl, from.Id));
                    var mine = db.Scalar("SELECT MIN(pos) FROM playlist_items WHERE playlist_id=? AND track_id=?", pl, into.Id);
                    if (mine != null && Conv.L(mine) <= first) { db.Exec("DELETE FROM playlist_items WHERE playlist_id=? AND track_id=?", pl, from.Id); continue; }
                    db.Exec("DELETE FROM playlist_items WHERE playlist_id=? AND track_id=?", pl, into.Id);
                    db.Exec("DELETE FROM playlist_items WHERE playlist_id=? AND track_id=? AND pos<>?", pl, from.Id, first);
                    db.Exec("UPDATE playlist_items SET track_id=? WHERE playlist_id=? AND track_id=?", into.Id, pl, from.Id);
                }
                // lyrics AURA found
                if (Conv.L(db.Scalar("SELECT COUNT(*) FROM found_lyrics WHERE track_id=?", into.Id)) == 0)
                    db.Exec("UPDATE found_lyrics SET track_id=? WHERE track_id=?", into.Id, from.Id);
                db.Exec("DELETE FROM found_lyrics WHERE track_id=?", from.Id);
                db.Exec("DELETE FROM lyrics_tried WHERE track_id=?", from.Id);
                db.Exec("DELETE FROM file_hash WHERE track_id=?", from.Id);
                db.Exec("DELETE FROM tracks WHERE id=?", from.Id);
            });
        }
    }
}
