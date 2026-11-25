using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Aura
{
    sealed class LyricLine
    {
        public double T;
        public string Text = "";
        public List<(double t, string w)> Words;   // word timings, when the song has them
        public double End = -1;                     // where the last word ends (-1: unknown)
    }

    sealed class Lyrics
    {
        public string Plain = "";
        public List<LyricLine> Lines;               // timed lines, or null for plain lyrics
        public string Source = "";                  // "In the song file", "From LRCLIB", ...
        public bool FromAura;                       // found online by AURA (can be removed)
        public bool Timed => Lines != null && Lines.Count > 0;
        public bool ByWord => Timed && Lines.Any(l => l.Words != null);

        public static Lyrics From(string text, List<(double t, string text)> synced, string wordsRaw, string source)
        {
            text = (text ?? "").Replace("\r\n", "\n").Trim();
            var l = new Lyrics { Source = source };
            List<(double t, string line)> timed = synced != null && synced.Count > 0 ? synced : Lrc.Parse(text);
            if (timed != null && timed.Count > 0)
            {
                l.Lines = timed.Select(x => new LyricLine { T = x.t, Text = x.line }).ToList();
                l.Plain = synced != null && text.Length > 0 && !Lrc.IsTimed(text) ? text : string.Join("\n", l.Lines.Select(x => x.Text));
                Lrc.AttachWords(l.Lines, wordsRaw);
            }
            else l.Plain = text;
            return l.Plain.Trim().Length == 0 && !l.Timed ? null : l;
        }
    }

    static class Lrc
    {
        static readonly Regex Stamp = new Regex(@"\[(\d{1,3}):(\d{1,2}(?:[.:]\d{1,3})?)\]", RegexOptions.Compiled);
        static readonly Regex Meta = new Regex(@"^\s*\[(ar|ti|al|au|by|length|offset|re|ve|#)\s*:[^\]]*\]\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex ELine = new Regex(@"^\[(\d+):(\d+(?:\.\d+)?)\](.*)$", RegexOptions.Compiled);
        static readonly Regex EWord = new Regex(@"<(\d+):(\d+(?:\.\d+)?)>([^<]*)", RegexOptions.Compiled);
        static double Num(string s) => double.Parse(s.Replace(':', '.'), CultureInfo.InvariantCulture);

        public static bool IsTimed(string text) => !string.IsNullOrEmpty(text) && Stamp.IsMatch(text);

        public static List<(double t, string line)> Parse(string text)
        {
            if (!IsTimed(text)) return null;
            double offset = 0;
            var m = Regex.Match(text, @"\[offset\s*:\s*([+-]?\d+)\s*\]", RegexOptions.IgnoreCase);
            if (m.Success) offset = int.Parse(m.Groups[1].Value) / 1000.0;
            var outp = new List<(double, string)>();
            foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                var ms = Stamp.Matches(raw);
                if (ms.Count == 0) continue;
                string line = Stamp.Replace(raw, "").Trim();
                // enhanced LRC: drop word stamps from the shown text
                if (line.Contains("<")) line = Regex.Replace(line, @"<\d+:\d+(?:\.\d+)?>", "").Replace("  ", " ").Trim();
                foreach (Match st in ms)
                    outp.Add((Math.Max(0, Math.Round(int.Parse(st.Groups[1].Value) * 60 + Num(st.Groups[2].Value) - offset, 3)), line));
            }
            outp.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            return outp.Count > 0 ? outp : null;
        }

        public static string Plain(string text)
        {
            var keep = new List<string>();
            foreach (var raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                if (Meta.IsMatch(raw)) continue;
                keep.Add(Stamp.Replace(raw, "").TrimEnd());
            }
            return string.Join("\n", keep).Trim();
        }

        public static string ToLrc(IEnumerable<(double t, string line)> lines) =>
            string.Join("\n", lines.Select(x => "[" + Ts(x.t) + "]" + x.line));

        static string Ts(double t)
        {
            t = Math.Max(0, t);
            int m = (int)(t / 60);
            return m.ToString("00") + ":" + (t - m * 60).ToString("00.00", CultureInfo.InvariantCulture);
        }

        static string Squash(string s) => Regex.Replace(s ?? "", @"\s+", " ").Trim();

        // word timings (AURA_WORDS) are used only for lines they still fit
        public static void AttachWords(List<LyricLine> lines, string raw)
        {
            if (string.IsNullOrWhiteSpace(raw) || lines == null) return;
            var el = new List<(double lt, List<(double, string)> ws, double end)>();
            foreach (var r in raw.Replace("\r\n", "\n").Split('\n'))
            {
                var m = ELine.Match(r.Trim());
                if (!m.Success) continue;
                double lt = int.Parse(m.Groups[1].Value) * 60 + Num(m.Groups[2].Value);
                var ws = new List<(double, string)>(); double end = -1;
                foreach (Match w in EWord.Matches(m.Groups[3].Value))
                {
                    double wt = int.Parse(w.Groups[1].Value) * 60 + Num(w.Groups[2].Value);
                    var word = w.Groups[3].Value.Trim();
                    if (word.Length > 0) ws.Add((Math.Round(wt, 3), word)); else end = Math.Round(wt, 3);
                }
                el.Add((Math.Round(lt, 3), ws, end));
            }
            if (el.Count != lines.Count) return;
            for (int i = 0; i < lines.Count; i++)
            {
                var (lt, ws, end) = el[i];
                if (ws.Count > 0 && Math.Abs(lt - lines[i].T) < 0.06 && Squash(string.Join(" ", ws.Select(x => x.Item2))) == Squash(lines[i].Text))
                {
                    lines[i].Words = ws;
                    lines[i].End = end;
                }
            }
        }
    }

    // finding lyrics online: LRCLIB (free, often timed) and Genius (words only)
    sealed class Found
    {
        public string Source = "", Title = "", Artist = "", Album = "", Url = "";
        public double Duration;
        public string Synced;      // LRC text when timed
        public string Plain;       // words (Genius: filled in on demand)
        public double Score;
        public bool IsTimed => !string.IsNullOrEmpty(Synced);
        public string SourceName => Source == "lrclib" ? "LRCLIB" : Source == "ovh" ? "lyrics.ovh" : "Genius";
    }

    [DataContract] sealed class LrclibItem
    {
        [DataMember(Name = "trackName")] public string TrackName;
        [DataMember(Name = "artistName")] public string ArtistName;
        [DataMember(Name = "albumName")] public string AlbumName;
        [DataMember(Name = "duration")] public double? Duration;
        [DataMember(Name = "plainLyrics")] public string PlainLyrics;
        [DataMember(Name = "syncedLyrics")] public string SyncedLyrics;
    }
    [DataContract] sealed class GeniusResp { [DataMember(Name = "response")] public GeniusInner Response; }
    [DataContract] sealed class GeniusInner { [DataMember(Name = "sections")] public List<GeniusSection> Sections; }
    [DataContract] sealed class GeniusSection { [DataMember(Name = "type")] public string Type; [DataMember(Name = "hits")] public List<GeniusHit> Hits; }
    [DataContract] sealed class GeniusHit { [DataMember(Name = "type")] public string Type; [DataMember(Name = "result")] public GeniusResult Result; }
    [DataContract] sealed class GeniusResult
    {
        [DataMember(Name = "title")] public string Title;
        [DataMember(Name = "url")] public string Url;
        [DataMember(Name = "artist_names")] public string ArtistNames;
        [DataMember(Name = "primary_artist")] public GeniusArtist Primary;
    }
    [DataContract] sealed class GeniusArtist { [DataMember(Name = "name")] public string Name; }

    static class LyricsOnline
    {
        public static string LrclibUrl = Environment.GetEnvironmentVariable("AURA_LRCLIB_URL") ?? "https://lrclib.net";
        public static string GeniusUrl = Environment.GetEnvironmentVariable("AURA_GENIUS_URL") ?? "https://genius.com";
        public static string OvhUrl = Environment.GetEnvironmentVariable("AURA_OVH_URL") ?? "https://api.lyrics.ovh";

        [DataContract] sealed class OvhResp { [DataMember(Name = "lyrics")] public string Lyrics; }

        // lyrics.ovh: words only, by exact artist and title
        static List<Found> Ovh(string artist, string title)
        {
            var outp = new List<Found>();
            if (artist.Length == 0 || title.Length == 0) return outp;
            try
            {
                var r = Net.Json<OvhResp>(Net.Get(OvhUrl + "/v1/" + Uri.EscapeDataString(artist) + "/" + Uri.EscapeDataString(title), 8000));
                var text = Regex.Replace((r?.Lyrics ?? "").Replace("\r\n", "\n").Replace("\r", "\n"), @"\n{3,}", "\n\n").Trim();
                if (text.Length >= 40) outp.Add(new Found { Source = "ovh", Title = title, Artist = artist, Plain = text });
            }
            catch (WebException e) when ((e.Response as HttpWebResponse)?.StatusCode == HttpStatusCode.NotFound) { }    // simply not there
            return outp;
        }

        static readonly Regex LeakWords = new Regex(@"\b(snippet|cdq|hq|lq|og|leak(?:ed)?|unreleased|unrel|unr|full|remaster(?:ed)?|v\d+|version\s*\d+|session|sessions|demo|rough|reference|ref|throwaway|alt|alternate)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // "Bad Energy (prod. Nick Mira) [v2] {CDQ}" -> "Bad Energy"
        public static string CleanTitle(string title)
        {
            var t = title ?? "";
            t = Regex.Replace(t, @"[\(\[\{][^\)\]\}]*(?:prod|feat|ft\.|with|w/|snippet|cdq|leak|v\d|og|session|unrel|demo|version|instrumental)[^\)\]\}]*[\)\]\}]", " ", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\b(?:feat|ft|featuring|prod(?:uced)?(?:\s+by)?)\.?\s.*$", " ", RegexOptions.IgnoreCase);
            t = LeakWords.Replace(t, " ");
            t = Regex.Replace(t, @"[\(\[\{]\s*[\)\]\}]", " ");
            t = t.Replace('_', ' ');
            return Regex.Replace(t, @"\s+", " ").Trim(' ', '-', '_', '.');
        }

        public static string MainArtist(string artist)
        {
            var a = (artist ?? "").Split(',')[0];
            int amp = a.IndexOf(" & ", StringComparison.Ordinal); if (amp > 0) a = a.Substring(0, amp);
            return Regex.Split(a, @"\s+(?:feat|ft)\.?\s", RegexOptions.IgnoreCase)[0].Trim();
        }

        public static string Norm(string x)
        {
            x = (x ?? "").Normalize(NormalizationForm.FormKD).ToLowerInvariant();
            var sb = new StringBuilder();
            foreach (var c in x) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            x = Regex.Replace(sb.ToString(), @"[\(\[].*?[\)\]]", " ");
            x = Regex.Replace(x, @"\b(feat|ft|featuring)\b.*$", " ");
            return Regex.Replace(x, @"[^a-z0-9]+", " ").Trim();
        }

        public static double Sim(string a, string b)
        {
            a = Norm(a); b = Norm(b);
            if (a.Length == 0 || b.Length == 0) return 0;
            if (a == b) return 1;
            var d = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) d[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                int prev = d[0]; d[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int tmp = d[j];
                    d[j] = Math.Min(Math.Min(d[j] + 1, d[j - 1] + 1), prev + (a[i - 1] == b[j - 1] ? 0 : 1));
                    prev = tmp;
                }
            }
            return 1 - d[b.Length] / (double)Math.Max(a.Length, b.Length);
        }

        public static bool ArtistOk(string mine, string theirs)
        {
            var a = Norm(mine); var b = Norm(theirs);
            if (a.Length == 0 || b.Length == 0) return false;
            if (a == b || Sim(mine, theirs) >= 0.8) return true;
            return (" " + b + " ").Contains(" " + a + " ");     // "Juice WRLD" inside "Juice WRLD & Young Thug"
        }

        static List<Found> Lrclib(string q)
        {
            var json = Net.Get(LrclibUrl + "/api/search?q=" + Uri.EscapeDataString(q), 8000);
            var items = Net.Json<List<LrclibItem>>(json) ?? new List<LrclibItem>();
            var outp = new List<Found>();
            foreach (var r in items.Take(15))
            {
                if (string.IsNullOrWhiteSpace(r.PlainLyrics) && string.IsNullOrWhiteSpace(r.SyncedLyrics)) continue;
                outp.Add(new Found
                {
                    Source = "lrclib", Title = r.TrackName ?? "", Artist = r.ArtistName ?? "", Album = r.AlbumName ?? "",
                    Duration = r.Duration ?? 0,
                    Synced = Lrc.IsTimed(r.SyncedLyrics) ? r.SyncedLyrics : null,
                    Plain = (string.IsNullOrWhiteSpace(r.PlainLyrics) ? Lrc.Plain(r.SyncedLyrics) : r.PlainLyrics).Trim()
                });
            }
            return outp;
        }

        // Genius sometimes refuses programs like AURA (403); after that it isn't asked again until AURA restarts
        static volatile bool geniusRefused;

        static List<Found> Genius(string q)
        {
            if (geniusRefused) return new List<Found>();
            string json;
            try { json = Net.Get(GeniusUrl + "/api/search/multi?per_page=5&q=" + Uri.EscapeDataString(q), 8000, Net.BrowserUA, "application/json, text/plain, */*", GeniusUrl + "/"); }
            catch (WebException e) when ((e.Response as HttpWebResponse)?.StatusCode == HttpStatusCode.Forbidden)
            {
                geniusRefused = true;
                App.Log("lyrics: Genius refuses AURA's searches (403) — using LRCLIB and lyrics.ovh");
                return new List<Found>();
            }
            var resp = Net.Json<GeniusResp>(json);
            var outp = new List<Found>(); var seen = new HashSet<string>();
            foreach (var sec in resp?.Response?.Sections ?? new List<GeniusSection>())
                foreach (var hit in sec.Hits ?? new List<GeniusHit>())
                {
                    if (hit.Type != "song" && sec.Type != "song") continue;
                    var r = hit.Result;
                    if (r == null || string.IsNullOrEmpty(r.Url) || !seen.Add(r.Url)) continue;
                    outp.Add(new Found { Source = "genius", Title = r.Title ?? "", Artist = r.Primary?.Name ?? r.ArtistNames ?? "", Url = r.Url });
                }
            return outp;
        }

        // the words from a Genius song page
        public static string GeniusLyrics(string url)
        {
            var host = new Uri(url).Host;
            bool ours = GeniusUrl != "https://genius.com" && url.StartsWith(GeniusUrl, StringComparison.OrdinalIgnoreCase);
            if (!(host == "genius.com" || host.EndsWith(".genius.com")) && !ours) throw new ArgumentException("not a Genius link");
            var html = Net.Get(url, 10000, Net.BrowserUA, "text/html,application/xhtml+xml", GeniusUrl + "/");
            var sb = new StringBuilder();
            int depth = 0, skip = 0;
            var tags = new HashSet<string> { "div", "span", "a", "i", "b", "em", "strong", "p" };
            foreach (Match m in Regex.Matches(html, @"<(/?)([a-zA-Z][a-zA-Z0-9]*)([^>]*?)(/?)>|([^<]+)", RegexOptions.Singleline))
            {
                // line breaks in the page's code aren't line breaks in the song (only <br> is)
                if (m.Groups[5].Success) { if (depth > 0 && skip == 0) sb.Append(WebUtility.HtmlDecode(Regex.Replace(m.Groups[5].Value, @"\s*\n\s*", " "))); continue; }
                bool close = m.Groups[1].Value == "/", selfClose = m.Groups[4].Value == "/";
                string tag = m.Groups[2].Value.ToLowerInvariant(), attrs = m.Groups[3].Value;
                if (tag == "br") { if (depth > 0) sb.Append('\n'); continue; }
                if (!close)
                {
                    if (depth > 0)
                    {
                        if (tags.Contains(tag) && !selfClose)
                        {
                            depth++;
                            if (skip > 0 || attrs.Contains("data-exclude-from-selection=\"true\"")) skip++;
                        }
                    }
                    else if (tag == "div" && attrs.Contains("data-lyrics-container=\"true\""))
                    {
                        depth = 1;
                        if (sb.Length > 0) sb.Append('\n');
                    }
                }
                else if (depth > 0 && tags.Contains(tag))
                {
                    if (skip > 0) skip--;
                    depth--;
                }
            }
            var lines = new List<string>();
            foreach (var raw in sb.ToString().Split('\n'))
            {
                var ln = raw.Trim();
                if (Regex.IsMatch(ln, @"^\[[^\]]*\]$")) { if (lines.Count > 0 && lines[lines.Count - 1] != "") lines.Add(""); continue; }   // [Verse 1] isn't sung
                lines.Add(ln);
            }
            return Regex.Replace(string.Join("\n", lines), @"\n{3,}", "\n\n").Trim();
        }

        static readonly HashSet<string> logged = new HashSet<string>();
        static void LogOnce(string site, string why)
        {
            lock (logged) { if (!logged.Add(site + why)) return; }
            App.Log("lyrics: " + site + " didn't answer: " + why);
        }

        public sealed class Result { public List<Found> Found = new List<Found>(); public bool Offline; public string Query = ""; }

        // searches both sites at once (about 9 seconds at most)
        public static Result Search(Track t, string query = null)
        {
            string title = CleanTitle(t.Title.Length > 0 ? t.Title : Path.GetFileNameWithoutExtension(t.Path));
            string artist = MainArtist(t.Artist);
            var queries = new List<string>();
            if (!string.IsNullOrWhiteSpace(query)) queries.Add(query.Trim());
            else
            {
                var q1 = (artist + " " + title).Trim();
                if (q1.Length > 0) queries.Add(q1);
                if (title.Length > 0 && title != q1) queries.Add(title);
            }
            var res = new Result { Query = queries.FirstOrDefault() ?? "" };
            var tasks = new List<(string site, Task<List<Found>> task)>();
            foreach (var q in queries)
            {
                var qq = q;
                tasks.Add(("LRCLIB", Task.Run(() => Lrclib(qq))));
                tasks.Add(("Genius", Task.Run(() => Genius(qq))));
            }
            if (string.IsNullOrWhiteSpace(query) && artist.Length > 0 && title.Length > 0) tasks.Add(("lyrics.ovh", Task.Run(() => Ovh(artist, title))));
            try { Task.WaitAll(tasks.Select(x => (Task)x.task).ToArray(), 9000); } catch { }
            int errors = 0;
            var all = new List<Found>();
            foreach (var (site, tk) in tasks)
            {
                if (tk.Status == TaskStatus.RanToCompletion) all.AddRange(tk.Result);
                else { errors++; LogOnce(site, tk.IsFaulted ? tk.Exception?.GetBaseException().Message : "took too long"); }
            }
            double dur = t.Duration;
            // the same song is often listed several times at different lengths: keep the one closest to this file
            var best = new Dictionary<string, Found>();
            foreach (var r in all)
            {
                var key = r.Source + "\u0001" + Norm(r.Title) + "\u0001" + Norm(r.Artist);
                if (!best.TryGetValue(key, out var old)) best[key] = r;
                else if (dur > 0 && r.Duration > 0 && r.IsTimed && (!old.IsTimed || old.Duration <= 0 || Math.Abs(r.Duration - dur) < Math.Abs(old.Duration - dur))) best[key] = r;
            }
            foreach (var r in best.Values)
            {
                double sc = Sim(r.Title, title) * 2 + (artist.Length > 0 ? Sim(r.Artist, artist) : 0.5);
                if (r.IsTimed) sc += 0.3;
                if (dur > 0 && r.Duration > 0) sc -= Math.Min(1.0, Math.Abs(r.Duration - dur) / 30.0);
                r.Score = sc;
            }
            res.Found = best.Values.OrderByDescending(r => r.Score).Take(10).ToList();
            // "offline" only when every site failed — no results for an unreleased song isn't being offline
            res.Offline = errors == tasks.Count;
            return res;
        }

        // What AURA saves by itself for a song without lyrics: only a confident title + artist match.
        public static (string text, string source, bool offline) Pick(Track t)
        {
            string title = CleanTitle(t.Title.Length > 0 ? t.Title : Path.GetFileNameWithoutExtension(t.Path));
            string artist = MainArtist(t.Artist);
            if (title.Length == 0 || artist.Length == 0) return (null, null, false);
            var res = Search(t);
            if (res.Offline) return (null, null, true);
            double dur = t.Duration;
            var good = res.Found.Where(r => Sim(r.Title, title) >= 0.9 && ArtistOk(artist, r.Artist)).ToList();
            var lr = good.Where(r => r.Source == "lrclib" && !string.IsNullOrWhiteSpace(r.Plain)).ToList();
            // timings only fit when the recording is the same length; a leak or snippet still gets the words
            var fits = lr.Where(r => r.IsTimed && dur > 0 && r.Duration > 0 && Math.Abs(r.Duration - dur) <= 2.5).OrderBy(r => Math.Abs(r.Duration - dur)).FirstOrDefault();
            if (fits != null) return (fits.Synced, "LRCLIB", false);
            if (lr.Count > 0) return (lr[0].Plain, "LRCLIB", false);
            var ovh = res.Found.FirstOrDefault(r => r.Source == "ovh" && !string.IsNullOrWhiteSpace(r.Plain));
            if (ovh != null) return (ovh.Plain, "lyrics.ovh", false);
            foreach (var r in good.Where(r => r.Source == "genius").Take(2))
            {
                try { var text = GeniusLyrics(r.Url); if (text.Length >= 40) return (text, "Genius", false); } catch { }
            }
            return (null, null, false);
        }
    }

    // Where a song's lyrics come from, and the lyrics AURA found by itself.
    sealed class LyricsStore
    {
        readonly Library lib;
        readonly Dictionary<long, Lyrics> cache = new Dictionary<long, Lyrics>();
        public event Action<long> Changed;       // on the UI thread

        public LyricsStore(Library lib)
        {
            this.lib = lib;
            lib.Db.Script(@"CREATE TABLE IF NOT EXISTS found_lyrics (track_id INTEGER PRIMARY KEY, text TEXT, source TEXT, picked INTEGER DEFAULT 0, added REAL);
CREATE TABLE IF NOT EXISTS lyrics_tried (track_id INTEGER PRIMARY KEY, ts REAL)");
            if (!lib.Db.Rows("PRAGMA table_info(found_lyrics)").Any(r => Conv.S(r[1]) == "words"))
                lib.Db.Exec("ALTER TABLE found_lyrics ADD COLUMN words TEXT DEFAULT ''");
        }

        public static string Sidecar(string path)
        {
            foreach (var ext in new[] { ".lrc", ".LRC" })
            {
                var p = Path.ChangeExtension(path, ext);
                if (FileX.Exists(p)) return p;
            }
            return null;
        }

        public bool HasFound(long id) => Conv.L(lib.Db.Scalar("SELECT COUNT(*) FROM found_lyrics WHERE track_id=?", id)) > 0;

        public Lyrics Get(Track t)
        {
            if (t == null) return null;
            lock (cache) if (cache.TryGetValue(t.Id, out var c)) return c;
            var l = Load(t);
            lock (cache) { if (cache.Count > 40) cache.Clear(); cache[t.Id] = l; }
            return l;
        }

        public void Forget(long id) { lock (cache) cache.Remove(id); }

        Lyrics Load(Track t)
        {
            var row = lib.Db.Rows("SELECT text, source, picked, words FROM found_lyrics WHERE track_id=?", t.Id).FirstOrDefault();
            Lyrics Found() => row == null ? null : Mark(Lyrics.From(Conv.S(row[0]), null, Conv.S(row[3]), Conv.S(row[1]).StartsWith("AURA") ? Conv.S(row[1]) : "Found on " + Conv.S(row[1])), true);
            if (row != null && Conv.L(row[2]) != 0) { var f = Found(); if (f != null) return f; }     // you picked these
            try
            {
                var tg = Tags.Read(t.Path);
                if (tg.HasLyrics)
                {
                    var l = Lyrics.From(tg.LyricsText, tg.Synced, tg.WordsRaw, "In the song file");
                    if (l != null) return l;
                }
            }
            catch { }
            var side = Sidecar(t.Path);
            if (side != null)
            {
                try { var l = Lyrics.From(File.ReadAllText(FileX.Long(side)), null, null, "From " + Path.GetFileName(side)); if (l != null) return l; } catch { }
            }
            return Found();
        }

        static Lyrics Mark(Lyrics l, bool fromAura) { if (l != null) l.FromAura = fromAura; return l; }

        public void Save(Track t, string text, string source, bool picked, string words = "")
        {
            lib.Db.Exec("INSERT OR REPLACE INTO found_lyrics (track_id,text,source,picked,added,words) VALUES (?,?,?,?,?,?)", t.Id, text, source, picked, Library.Now, words ?? "");
            Forget(t.Id);
            Changed?.Invoke(t.Id);
        }

        public void Remove(Track t)
        {
            lib.Db.Exec("DELETE FROM found_lyrics WHERE track_id=?", t.Id);
            lib.Db.Exec("INSERT OR REPLACE INTO lyrics_tried (track_id,ts) VALUES (?,?)", t.Id, Library.Now);   // don't look again by itself
            Forget(t.Id);
            Changed?.Invoke(t.Id);
        }

        // ---------- looking by itself ----------
        public sealed class AutoStatus { public bool Running; public int Found, Done; public string Current = ""; public bool Offline; }
        public readonly AutoStatus Auto = new AutoStatus();
        Func<bool> enabled;
        SynchronizationContext ui;
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        public event Action AutoProgress;     // on the UI thread

        public void StartAuto(Func<bool> isOn)
        {
            enabled = isOn;
            ui = SynchronizationContext.Current;
            new Thread(AutoLoop) { IsBackground = true, Priority = ThreadPriority.Lowest, Name = "lyrics" }.Start();
        }

        public void Nudge() => wake.Set();

        public void TryAllAgain()
        {
            lib.Db.Exec("DELETE FROM lyrics_tried");
            Auto.Offline = false;
            wake.Set();
        }

        public List<Track> Without()
        {
            var have = new HashSet<long>(lib.Db.Rows("SELECT track_id FROM found_lyrics").Select(r => Conv.L(r[0])));
            return lib.Tracks.Where(t => !t.HasLyrics && !have.Contains(t.Id)).ToList();
        }

        List<Track> Waiting()
        {
            var tried = new HashSet<long>(lib.Db.Rows("SELECT track_id FROM lyrics_tried").Select(r => Conv.L(r[0])));
            return Without().Where(t => !tried.Contains(t.Id) && t.Artist.Trim().Length > 0).OrderByDescending(t => t.Plays).ThenByDescending(t => t.Added).ToList();
        }

        public int WaitingCount => Waiting().Count;

        void Post(Action a) { if (ui != null) ui.Post(_ => a(), null); else a(); }

        void AutoLoop()
        {
            Perf.Here("finding lyrics online");
            Thread.Sleep(8000);           // let AURA finish starting first
            int fails = 0;
            while (true)
            {
                try
                {
                    if (!enabled() || lib.Scan.Running) { wake.WaitOne(20000); continue; }
                    var next = Waiting().FirstOrDefault();
                    if (next == null) { Auto.Running = false; Post(() => AutoProgress?.Invoke()); wake.WaitOne(60000); continue; }
                    Auto.Running = true; Auto.Current = next.Name;
                    Post(() => AutoProgress?.Invoke());
                    var (text, source, offline) = LyricsOnline.Pick(next);
                    if (offline)
                    {
                        if (++fails >= 3) { Auto.Offline = true; Auto.Running = false; Post(() => AutoProgress?.Invoke()); wake.WaitOne(10 * 60 * 1000); fails = 0; Auto.Offline = false; }
                        continue;
                    }
                    fails = 0;
                    if (text != null && !HasFound(next.Id))
                    {
                        lib.Db.Exec("INSERT OR IGNORE INTO found_lyrics (track_id,text,source,picked,added) VALUES (?,?,?,0,?)", next.Id, text, source, Library.Now);
                        Forget(next.Id);
                        Auto.Found++;
                        var id = next.Id;
                        Post(() => Changed?.Invoke(id));
                    }
                    lib.Db.Exec("INSERT OR REPLACE INTO lyrics_tried (track_id,ts) VALUES (?,?)", next.Id, Library.Now);
                    Auto.Done++;
                    Post(() => AutoProgress?.Invoke());
                    Thread.Sleep(1500);     // go easy on the lyric sites
                }
                catch (Exception e) { App.Log("auto lyrics: " + e.Message); Thread.Sleep(5000); }
            }
        }
    }
}
