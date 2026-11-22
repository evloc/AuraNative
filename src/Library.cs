using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Aura
{
    sealed class Track
    {
        public long Id;
        public string Path = "", Title = "", Artist = "", Album = "", AlbumArtist = "", Year = "", Genre = "";
        public int TrackNo;
        public double Duration, Added, Mtime, LastPlayed;
        public long Size;
        public bool HasArt, Fav, HasLyrics;
        public int Rating;             // 0-5 stars
        public string Era = "";
        public int Plays;

        public string Name => Title.Length > 0 ? Title : System.IO.Path.GetFileNameWithoutExtension(Path);
        public string ArtistName => Artist.Length > 0 ? Artist : "Unknown artist";
        public string MainArtist
        {
            get
            {
                var a = AlbumArtist.Length > 0 ? AlbumArtist : Artist;
                if (a.Length == 0) return "Unknown artist";
                foreach (var sep in new[] { ", ", " & ", " feat. ", " ft. ", " x ", "/" })
                {
                    int i = a.IndexOf(sep, StringComparison.OrdinalIgnoreCase);
                    if (i > 0) a = a.Substring(0, i);
                }
                return a.Trim();
            }
        }
        public string AlbumName => Album.Length > 0 ? Album : "Singles";
        public string AlbumKey => (Album.Length > 0 ? Album : "\u0001singles") .ToLowerInvariant() + "\u0002" + MainArtist.ToLowerInvariant();
    }

    sealed class Playlist
    {
        public long Id;
        public string Name = "";
        public List<long> Items = new List<long>();
        public double Updated;
    }

    sealed class Album
    {
        public string Key, Name, Artist, Year = "";
        public List<Track> Tracks = new List<Track>();
        public Track Cover => Tracks.FirstOrDefault(t => t.HasArt) ?? Tracks.FirstOrDefault();
        public double Added => Tracks.Count == 0 ? 0 : Tracks.Max(t => t.Added);
    }

    sealed class Artist
    {
        public string Key, Name;
        public List<Track> Tracks = new List<Track>();
        public Track Cover => Tracks.OrderByDescending(t => t.Plays).FirstOrDefault(t => t.HasArt) ?? Tracks.FirstOrDefault();
    }

    sealed class ScanStatus
    {
        public bool Running;
        public int Seen, Read, Added, Removed;
        public string Current = "";
    }

    sealed partial class Library
    {
        public readonly Db Db;
        public List<Track> Tracks = new List<Track>();
        public Dictionary<long, Track> ById = new Dictionary<long, Track>();
        public List<Playlist> Playlists = new List<Playlist>();
        public readonly ScanStatus Scan = new ScanStatus();
        public event Action Changed;          // raised on the UI thread
        public event Action ScanProgress;
        SynchronizationContext ui;

        public static double Now => (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;

        const string Schema = @"
CREATE TABLE IF NOT EXISTS tracks (
  id INTEGER PRIMARY KEY, path TEXT UNIQUE NOT NULL, size INTEGER, mtime REAL,
  title TEXT DEFAULT '', artist TEXT DEFAULT '', album TEXT DEFAULT '', albumartist TEXT DEFAULT '',
  year TEXT DEFAULT '', genre TEXT DEFAULT '', track_no INTEGER DEFAULT 0, duration REAL DEFAULT 0,
  has_art INTEGER DEFAULT 0, added REAL, missing INTEGER DEFAULT 0);
CREATE TABLE IF NOT EXISTS favorites (track_id INTEGER PRIMARY KEY, added REAL);
CREATE TABLE IF NOT EXISTS playlists (id INTEGER PRIMARY KEY, name TEXT NOT NULL, created REAL, updated REAL);
CREATE TABLE IF NOT EXISTS playlist_items (playlist_id INTEGER NOT NULL, track_id INTEGER NOT NULL, pos INTEGER NOT NULL);
CREATE INDEX IF NOT EXISTS idx_pl_items ON playlist_items(playlist_id, pos);
CREATE TABLE IF NOT EXISTS plays (track_id INTEGER PRIMARY KEY, count INTEGER DEFAULT 0, last_played REAL);
CREATE TABLE IF NOT EXISTS history (id INTEGER PRIMARY KEY, track_id INTEGER, ts REAL);
CREATE TABLE IF NOT EXISTS kv (key TEXT PRIMARY KEY, value TEXT)";

        public Library(string dbPath)
        {
            Db = new Db(dbPath);
            Db.Script(Schema);
            // newer columns; songs are read again once to fill them in
            if (!Db.Rows("PRAGMA table_info(tracks)").Any(r => Conv.S(r[1]) == "has_lyrics"))
            {
                Db.Exec("ALTER TABLE tracks ADD COLUMN has_lyrics INTEGER DEFAULT 0");
                Db.Exec("UPDATE tracks SET mtime=0");
            }
            ui = SynchronizationContext.Current;
            PrepareMore();
        }

        public void SetUiContext(SynchronizationContext c) => ui = c;

        void OnUi(Action a)
        {
            if (ui == null || ui == SynchronizationContext.Current) a();
            else ui.Post(_ => a(), null);
        }

        public void RaiseChanged() => OnUi(() => Changed?.Invoke());

        // ---------- loading ----------
        public void Load()
        {
            var tracks = new List<Track>();
            var by = new Dictionary<long, Track>();
            foreach (var r in Db.Rows("SELECT id,path,size,mtime,title,artist,album,albumartist,year,genre,track_no,duration,has_art,added,has_lyrics FROM tracks WHERE missing=0"))
            {
                var t = new Track
                {
                    Id = Conv.L(r[0]), Path = Conv.S(r[1]), Size = Conv.L(r[2]), Mtime = Conv.D(r[3]),
                    Title = Conv.S(r[4]), Artist = Conv.S(r[5]), Album = Conv.S(r[6]), AlbumArtist = Conv.S(r[7]),
                    Year = Conv.S(r[8]), Genre = Conv.S(r[9]), TrackNo = (int)Conv.L(r[10]), Duration = Conv.D(r[11]),
                    HasArt = Conv.L(r[12]) != 0, Added = Conv.D(r[13]), HasLyrics = Conv.L(r[14]) != 0
                };
                tracks.Add(t); by[t.Id] = t;
            }
            foreach (var r in Db.Rows("SELECT track_id FROM favorites"))
                if (by.TryGetValue(Conv.L(r[0]), out var t)) t.Fav = true;
            LoadMore(by);
            foreach (var r in Db.Rows("SELECT track_id,count,last_played FROM plays"))
                if (by.TryGetValue(Conv.L(r[0]), out var t)) { t.Plays = (int)Conv.L(r[1]); t.LastPlayed = Conv.D(r[2]); }
            var pls = new List<Playlist>();
            var plById = new Dictionary<long, Playlist>();
            foreach (var r in Db.Rows("SELECT id,name,updated FROM playlists ORDER BY name COLLATE NOCASE"))
            {
                var p = new Playlist { Id = Conv.L(r[0]), Name = Conv.S(r[1]), Updated = Conv.D(r[2]) };
                pls.Add(p); plById[p.Id] = p;
            }
            foreach (var r in Db.Rows("SELECT playlist_id,track_id FROM playlist_items ORDER BY playlist_id,pos"))
                if (plById.TryGetValue(Conv.L(r[0]), out var p) && by.ContainsKey(Conv.L(r[1]))) p.Items.Add(Conv.L(r[1]));
            Tracks = tracks; ById = by; Playlists = pls;
        }

        public Track Get(long id) => ById.TryGetValue(id, out var t) ? t : null;

        public List<Album> Albums()
        {
            var d = new Dictionary<string, Album>();
            foreach (var t in Tracks)
            {
                if (!d.TryGetValue(t.AlbumKey, out var a))
                    d[t.AlbumKey] = a = new Album { Key = t.AlbumKey, Name = t.AlbumName, Artist = t.MainArtist };
                a.Tracks.Add(t);
                if (a.Year.Length == 0 && t.Year.Length > 0) a.Year = t.Year;
            }
            foreach (var a in d.Values)
                a.Tracks.Sort((x, y) => x.TrackNo != y.TrackNo ? (x.TrackNo == 0 ? 1 : y.TrackNo == 0 ? -1 : x.TrackNo.CompareTo(y.TrackNo)) : string.Compare(x.Name, y.Name, StringComparison.CurrentCultureIgnoreCase));
            return d.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public List<Artist> Artists()
        {
            var d = new Dictionary<string, Artist>();
            foreach (var t in Tracks)
            {
                var key = t.MainArtist.ToLowerInvariant();
                if (!d.TryGetValue(key, out var a)) d[key] = a = new Artist { Key = key, Name = t.MainArtist };
                a.Tracks.Add(t);
            }
            return d.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        // ---------- scanning ----------
        public static bool IsAudio(string path) => Tags.Extensions.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant());

        static IEnumerable<string> Walk(string root)
        {
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var dir = stack.Pop();
                string[] files = null, dirs = null;
                try { files = Directory.GetFiles(dir); } catch { }
                try { dirs = Directory.GetDirectories(dir); } catch { }
                if (files != null) foreach (var f in files) if (IsAudio(f)) yield return f;
                if (dirs != null)
                    foreach (var d in dirs)
                    {
                        var name = System.IO.Path.GetFileName(d);
                        if (name.StartsWith(".") || name.Equals("_originals", StringComparison.OrdinalIgnoreCase) || name.Equals("_duplicates", StringComparison.OrdinalIgnoreCase) || name.Equals("$RECYCLE.BIN", StringComparison.OrdinalIgnoreCase)) continue;
                        try { if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) continue; } catch { continue; }
                        stack.Push(d);
                    }
            }
        }

        static string Norm(string p) => p.Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();

        public void ScanAsync(IList<string> folders, Action done = null)
        {
            if (Scan.Running) return;
            Scan.Running = true; Scan.Seen = Scan.Read = Scan.Added = Scan.Removed = 0; Scan.Current = "";
            var list = folders.ToList();
            var th = new Thread(() =>
            {
                try { ScanNow(list); }
                catch (Exception e) { App.Log("scan failed: " + e); }
                finally
                {
                    Scan.Running = false;
                    OnUi(() => { Load(); Changed?.Invoke(); ScanProgress?.Invoke(); done?.Invoke(); });
                }
            }) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "scan" };
            th.Start();
        }

        public void ScanNow(IList<string> folders)
        {
            var known = new Dictionary<string, (long id, long size, double mtime, bool missing)>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in Db.Rows("SELECT id,path,size,mtime,missing FROM tracks"))
                known[Conv.S(r[1])] = (Conv.L(r[0]), Conv.L(r[2]), Conv.D(r[3]), Conv.L(r[4]) != 0);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var newPaths = new List<string>();
            var roots = new List<string>();
            var pending = new List<Action>();
            double lastUi = 0;
            void Flush()
            {
                if (pending.Count == 0) return;
                var batch = pending.ToList(); pending.Clear();
                Db.Tx(() => { foreach (var a in batch) a(); });
            }
            foreach (var folder in folders)
            {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) continue;   // unplugged drive: leave its songs alone
                roots.Add(Norm(folder) + "\\");
                foreach (var path in Walk(folder))
                {
                    if (!seen.Add(path)) continue;
                    Scan.Seen++;
                    FileInfo fi;
                    try { fi = new FileInfo(FileX.Long(path)); } catch { continue; }
                    long size = fi.Length;
                    double mtime = (fi.LastWriteTimeUtc - new DateTime(1970, 1, 1)).TotalSeconds;
                    if (known.TryGetValue(path, out var k) && !k.missing && k.size == size && Math.Abs(k.mtime - mtime) < 2) continue;
                    Scan.Current = System.IO.Path.GetFileName(path);
                    var t = Tags.Read(path);
                    Scan.Read++;
                    int trackNo = int.TryParse(t.Track, out int tn) ? tn : 0;
                    bool hasLyr = t.HasLyrics || LyricsStore.Sidecar(path) != null;
                    if (known.TryGetValue(path, out k))
                    {
                        var id = k.id;
                        pending.Add(() => Db.Exec("UPDATE tracks SET size=?,mtime=?,title=?,artist=?,album=?,albumartist=?,year=?,genre=?,track_no=?,duration=?,has_art=?,has_lyrics=?,missing=0 WHERE id=?",
                            size, mtime, t.Title, t.Artist, t.Album, t.AlbumArtist, t.Year, t.Genre, trackNo, t.Duration, t.HasPicture, hasLyr, id));
                        if (k.missing) Scan.Added++;
                    }
                    else
                    {
                        Scan.Added++;
                        newPaths.Add(path);
                        double added = Math.Min(Now, mtime > 0 ? mtime : Now);
                        pending.Add(() => Db.Exec("INSERT OR IGNORE INTO tracks (path,size,mtime,title,artist,album,albumartist,year,genre,track_no,duration,has_art,added,has_lyrics) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?)",
                            path, size, mtime, t.Title, t.Artist, t.Album, t.AlbumArtist, t.Year, t.Genre, trackNo, t.Duration, t.HasPicture, added, hasLyr));
                    }
                    if (pending.Count >= 200) Flush();
                    if (Now - lastUi > 0.25) { lastUi = Now; OnUi(() => ScanProgress?.Invoke()); }
                }
            }
            Flush();
            // a song converted to another format (song.wav -> song.mp3) is still the same song:
            // it keeps its plays, playlists, favorites and lyrics
            var fresh = newPaths.GroupBy(StemKey).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.First());
            if (fresh.Count > 0)
                Db.Tx(() =>
                {
                    foreach (var kv in known.ToList())
                    {
                        if (seen.Contains(kv.Key) || FileX.Exists(kv.Key)) continue;
                        if (!fresh.TryGetValue(StemKey(kv.Key), out var np)) continue;
                        if (MoveSong(kv.Value.id, np)) { fresh.Remove(StemKey(kv.Key)); seen.Add(kv.Key); Scan.Added--; }
                    }
                });
            // songs that are gone from folders that were scanned (or from folders no longer in the list)
            var wanted = folders.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => Norm(f) + "\\").ToList();
            Db.Tx(() =>
            {
                foreach (var kv in known)
                {
                    if (kv.Value.missing || seen.Contains(kv.Key)) continue;
                    var n = Norm(kv.Key);
                    bool underScanned = roots.Any(r => n.StartsWith(r));
                    bool underAny = wanted.Any(r => n.StartsWith(r));
                    if (underScanned || !underAny)
                    {
                        Db.Exec("UPDATE tracks SET missing=1 WHERE id=?", kv.Value.id);
                        Scan.Removed++;
                    }
                }
            });
        }

        static string StemKey(string p) => Norm(Path.Combine(Path.GetDirectoryName(p) ?? "", Path.GetFileNameWithoutExtension(p)));

        // points song "id" at the file a newer row has, and drops that newer row
        bool MoveSong(long id, string newPath)
        {
            var row = Db.Rows("SELECT id,size,mtime,title,artist,album,albumartist,year,genre,track_no,duration,has_art,has_lyrics FROM tracks WHERE path=?", newPath).FirstOrDefault();
            if (row == null) return false;
            long other = Conv.L(row[0]);
            if (other == id) return false;
            Db.Exec("DELETE FROM tracks WHERE id=?", other);
            Db.Exec("DELETE FROM playlist_items WHERE track_id=?", other);
            Db.Exec("DELETE FROM favorites WHERE track_id=?", other);
            Db.Exec("UPDATE tracks SET path=?,size=?,mtime=?,title=?,artist=?,album=?,albumartist=?,year=?,genre=?,track_no=?,duration=?,has_art=?,has_lyrics=?,missing=0 WHERE id=?",
                newPath, row[1], row[2], row[3], row[4], row[5], row[6], row[7], row[8], row[9], row[10], row[11], row[12], id);
            App.Log("song " + id + " now uses " + newPath);
            return true;
        }

        // when a song's file is gone: the same name with another ending in the same folder (a converted copy)
        public bool Relocate(Track t)
        {
            try
            {
                var dir = Path.GetDirectoryName(t.Path);
                var stem = Path.GetFileNameWithoutExtension(t.Path);
                if (dir == null || !Directory.Exists(FileX.Long(dir))) return false;
                var match = Tags.Extensions.Select(e => Path.Combine(dir, stem + e)).Where(p => !string.Equals(p, t.Path, StringComparison.OrdinalIgnoreCase)).FirstOrDefault(FileX.Exists);
                if (match == null) return false;
                bool known = Conv.L(Db.Scalar("SELECT COUNT(*) FROM tracks WHERE path=?", match)) > 0;
                if (known) { if (!MoveSong(t.Id, match)) return false; }
                else Db.Exec("UPDATE tracks SET path=? WHERE id=?", match, t.Id);
                var gone = Tracks.FirstOrDefault(x => x.Id != t.Id && string.Equals(x.Path, match, StringComparison.OrdinalIgnoreCase));
                if (gone != null) { Tracks.Remove(gone); ById.Remove(gone.Id); foreach (var p in Playlists) p.Items.RemoveAll(i => i == gone.Id); }
                t.Path = match;
                Refresh(t);
                return true;
            }
            catch (Exception e) { App.Log("relocate " + t.Path + ": " + e.Message); return false; }
        }

        // ---------- after AURA changes a file ----------
        public void Refresh(Track t)
        {
            var path = t.Path;
            var tg = Tags.Read(path);
            var fi = new FileInfo(FileX.Long(path));
            double mtime = (fi.LastWriteTimeUtc - new DateTime(1970, 1, 1)).TotalSeconds;
            t.Title = tg.Title; t.Artist = tg.Artist; t.Album = tg.Album; t.AlbumArtist = tg.AlbumArtist; t.Year = tg.Year; t.Genre = tg.Genre;
            t.TrackNo = int.TryParse(tg.Track, out int tn) ? tn : 0; t.Duration = tg.Duration > 0 ? tg.Duration : t.Duration;
            t.HasArt = tg.HasPicture; t.Size = fi.Length; t.Mtime = mtime;
            t.HasLyrics = tg.HasLyrics || LyricsStore.Sidecar(path) != null;
            Db.Exec("UPDATE tracks SET size=?,mtime=?,title=?,artist=?,album=?,albumartist=?,year=?,genre=?,track_no=?,duration=?,has_art=?,has_lyrics=?,missing=0 WHERE id=?",
                t.Size, t.Mtime, t.Title, t.Artist, t.Album, t.AlbumArtist, t.Year, t.Genre, t.TrackNo, t.Duration, t.HasArt, t.HasLyrics, t.Id);
        }

        // renames the file and keeps the same song (with its plays, playlists and favorites)
        public void Rename(Track t, string newPath)
        {
            File.Move(FileX.Long(t.Path), FileX.Long(newPath));
            foreach (var ext in new[] { ".lrc", ".LRC" })
            {
                var side = Path.ChangeExtension(t.Path, ext);
                if (FileX.Exists(side)) try { File.Move(FileX.Long(side), FileX.Long(Path.ChangeExtension(newPath, ".lrc"))); } catch { }
            }
            Db.Exec("DELETE FROM tracks WHERE path=? AND id<>?", newPath, t.Id);
            Db.Exec("UPDATE tracks SET path=? WHERE id=?", newPath, t.Id);
            t.Path = newPath;
        }

        // versions of the same song: same cleaned-up title and main artist
        public static string VersionKey(Track t)
        {
            if (ManualGroups.TryGetValue(t.Id, out var k)) return k;
            var title = LyricsOnline.Norm(LyricsOnline.CleanTitle(t.Title.Length > 0 ? t.Title : Path.GetFileNameWithoutExtension(t.Path)));
            return title.Length == 0 ? "" : title + "|" + LyricsOnline.Norm(t.MainArtist);
        }

        public List<List<Track>> VersionGroups()
        {
            return Tracks.GroupBy(VersionKey).Where(g => g.Key.Length > 0 && g.Count() > 1 && !NotVersionKeys.Contains(g.Key))
                .Select(g => g.OrderByDescending(t => t.Plays).ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList())
                .OrderBy(g => g[0].Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public List<Track> VersionsOf(Track t)
        {
            var k = VersionKey(t);
            return k.Length == 0 || NotVersionKeys.Contains(k) ? new List<Track> { t } : Tracks.Where(x => VersionKey(x) == k).OrderByDescending(x => x.Plays).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        // ---------- favorites, plays ----------
        public void SetFav(Track t, bool on)
        {
            t.Fav = on;
            if (on) Db.Exec("INSERT OR REPLACE INTO favorites (track_id,added) VALUES (?,?)", t.Id, Now);
            else Db.Exec("DELETE FROM favorites WHERE track_id=?", t.Id);
            RaiseChanged();
        }

        public void RecordPlay(Track t)
        {
            t.Plays++; t.LastPlayed = Now;
            // (no "upsert": older Windows 10 builds ship an SQLite without it)
            Db.Exec("INSERT OR IGNORE INTO plays (track_id,count,last_played) VALUES (?,0,0)", t.Id);
            Db.Exec("UPDATE plays SET count=count+1, last_played=? WHERE track_id=?", t.LastPlayed, t.Id);
            Db.Exec("INSERT INTO history (track_id,ts) VALUES (?,?)", t.Id, t.LastPlayed);
        }

        public List<Track> RecentlyPlayed(int n)
        {
            var list = new List<Track>(); var seen = new HashSet<long>();
            foreach (var r in Db.Rows("SELECT track_id FROM history ORDER BY id DESC LIMIT 400"))
            {
                var id = Conv.L(r[0]);
                if (seen.Add(id) && ById.TryGetValue(id, out var t)) { list.Add(t); if (list.Count >= n) break; }
            }
            if (list.Count < n)
                foreach (var t in Tracks.Where(x => x.LastPlayed > 0).OrderByDescending(x => x.LastPlayed))
                    if (seen.Add(t.Id)) { list.Add(t); if (list.Count >= n) break; }
            return list;
        }

        // ---------- playlists ----------
        public Playlist CreatePlaylist(string name, IEnumerable<long> ids = null)
        {
            Db.Exec("INSERT INTO playlists (name,created,updated) VALUES (?,?,?)", name, Now, Now);
            var p = new Playlist { Id = Db.LastId, Name = name, Updated = Now };
            Playlists.Add(p);
            Playlists.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            if (ids != null) AddToPlaylist(p, ids); else RaiseChanged();
            return p;
        }

        public void RenamePlaylist(Playlist p, string name)
        {
            p.Name = name;
            Db.Exec("UPDATE playlists SET name=?,updated=? WHERE id=?", name, Now, p.Id);
            Playlists.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            RaiseChanged();
        }

        public void DeletePlaylist(Playlist p)
        {
            Db.Tx(() => { Db.Exec("DELETE FROM playlist_items WHERE playlist_id=?", p.Id); Db.Exec("DELETE FROM playlists WHERE id=?", p.Id); });
            Playlists.Remove(p);
            RaiseChanged();
        }

        public int AddToPlaylist(Playlist p, IEnumerable<long> ids)
        {
            int added = 0;
            foreach (var id in ids) if (!p.Items.Contains(id) && ById.ContainsKey(id)) { p.Items.Add(id); added++; }
            SavePlaylist(p);
            return added;
        }

        public void SavePlaylist(Playlist p)
        {
            p.Updated = Now;
            Db.Tx(() =>
            {
                Db.Exec("DELETE FROM playlist_items WHERE playlist_id=?", p.Id);
                for (int i = 0; i < p.Items.Count; i++) Db.Exec("INSERT INTO playlist_items (playlist_id,track_id,pos) VALUES (?,?,?)", p.Id, p.Items[i], i);
                Db.Exec("UPDATE playlists SET updated=? WHERE id=?", p.Updated, p.Id);
            });
            RaiseChanged();
        }

        // ---------- bring things over from the first AURA ----------
        // Matches songs by file path; anything that doesn't match is skipped.
        public (int playlists, int favorites, int plays) ImportOld(string oldDb)
        {
            if (!File.Exists(oldDb)) return (0, 0, 0);
            Load();
            var byPath = Tracks.ToDictionary(t => Norm(t.Path), t => t);
            int np = 0, nf = 0, nplays = 0;
            using (var old = new Db(oldDb, readOnly: true))
            {
                var oldPath = new Dictionary<long, string>();
                foreach (var r in old.Rows("SELECT id,path FROM tracks")) oldPath[Conv.L(r[0])] = Norm(Conv.S(r[1]));
                Track Map(object oid) => oldPath.TryGetValue(Conv.L(oid), out var p) && byPath.TryGetValue(p, out var t) ? t : null;

                List<object[]> SafeRows(string sql) { try { return old.Rows(sql); } catch { return new List<object[]>(); } }

                Db.Tx(() =>
                {
                    foreach (var r in SafeRows("SELECT track_id,added FROM favorites"))
                    {
                        var t = Map(r[0]);
                        if (t == null) continue;
                        Db.Exec("INSERT OR IGNORE INTO favorites (track_id,added) VALUES (?,?)", t.Id, Conv.D(r[1]));
                        nf++;
                    }
                    foreach (var r in SafeRows("SELECT track_id,count,last_played FROM plays"))
                    {
                        var t = Map(r[0]);
                        if (t == null) continue;
                        Db.Exec("INSERT OR IGNORE INTO plays (track_id,count,last_played) VALUES (?,0,0)", t.Id);
                        Db.Exec("UPDATE plays SET count=max(count,?), last_played=max(last_played,?) WHERE track_id=?", Conv.L(r[1]), Conv.D(r[2]), t.Id);
                        nplays++;
                    }
                    var names = new HashSet<string>(Playlists.Select(p => p.Name), StringComparer.CurrentCultureIgnoreCase);
                    foreach (var r in SafeRows("SELECT id,name,created,updated FROM playlists ORDER BY id"))
                    {
                        string name = Conv.S(r[1]);
                        if (names.Contains(name)) continue;       // already brought over
                        Db.Exec("INSERT INTO playlists (name,created,updated) VALUES (?,?,?)", name, Conv.D(r[2]), Conv.D(r[3]));
                        long pid = Db.LastId; int pos = 0;
                        foreach (var it in SafeRows("SELECT track_id FROM playlist_items WHERE playlist_id=" + Conv.L(r[0]) + " ORDER BY pos"))
                        {
                            var t = Map(it[0]);
                            if (t != null) Db.Exec("INSERT INTO playlist_items (playlist_id,track_id,pos) VALUES (?,?,?)", pid, t.Id, pos++);
                        }
                        names.Add(name); np++;
                    }
                });
            }
            Load();
            return (np, nf, nplays);
        }
    }
}
