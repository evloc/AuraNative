using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Aura
{
    // Everything worth knowing about a song for the cards under Now Playing: who's on it, who made it,
    // the album or era it belongs to, and how you've listened to it. Read from the file's tags, its name
    // ("(feat. X)", "(prod. Y)") and the library. Made on a worker thread and kept for a while.
    sealed class SongAbout
    {
        public sealed class Person { public string Name, Role; public Track Pic; public string Key; }
        public long Id;
        public List<Person> Artists = new List<Person>();
        public List<KeyValuePair<string, string>> Credits = new List<KeyValuePair<string, string>>();
        public string Comment = "";

        static readonly Dictionary<long, SongAbout> cache = new Dictionary<long, SongAbout>();
        static readonly HashSet<long> making = new HashSet<long>();

        // null while it's being made; ready() runs on a worker thread when it is
        public static SongAbout Get(Track t, Library lib, Action ready)
        {
            lock (cache)
            {
                if (cache.TryGetValue(t.Id, out var a)) return a;
                if (!making.Add(t.Id)) return null;
            }
            new System.Threading.Thread(() =>
            {
                SongAbout made;
                try { made = Make(t, lib); }
                catch (Exception e) { App.Log("about " + t.Name + ": " + e.Message); made = new SongAbout { Id = t.Id }; }
                lock (cache) { if (cache.Count > 200) cache.Clear(); cache[t.Id] = made; making.Remove(t.Id); }
                ready?.Invoke();
            }) { IsBackground = true, Priority = System.Threading.ThreadPriority.BelowNormal, Name = "about the song" }.Start();
            return null;
        }

        public static void Forget(long id) { lock (cache) cache.Remove(id); }

        static readonly Regex Feat = new Regex(@"[\(\[]\s*(?:feat\.?|ft\.?|featuring|with)\s+([^\)\]]+)[\)\]]", RegexOptions.IgnoreCase);
        static readonly Regex FeatLoose = new Regex(@"\s(?:feat\.?|ft\.?|featuring)\s+(.+)$", RegexOptions.IgnoreCase);
        static readonly Regex Prod = new Regex(@"[\(\[]\s*(?:prod(?:uced)?\.?(?:\s+by)?|p\s*/\s*b)\s*:?\s*([^\)\]]+)[\)\]]", RegexOptions.IgnoreCase);
        static readonly Regex ProdLoose = new Regex(@"\b(?:prod(?:uced)?\.?\s+by|prod\.)\s*:?\s*([^\n\r\(\)\[\]|;]+)", RegexOptions.IgnoreCase);

        public static List<string> SplitNames(string s)
        {
            var parts = Regex.Split(s ?? "", @"\s*(?:,|&|\+|/|;|\bx\b|\band\b|\bfeat\.?|\bft\.?)\s*", RegexOptions.IgnoreCase);
            return parts.Select(p => p.Trim().Trim('.', '-', ' ')).Where(p => p.Length > 1 && p.Length < 60).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        static SongAbout Make(Track t, Library lib)
        {
            var a = new SongAbout { Id = t.Id };
            TagInfo tags = null;
            try { tags = Tags.Read(t.Path); } catch { }
            string name = t.Name, file = Path.GetFileNameWithoutExtension(t.Path);

            // who's on it: the main artist, then everyone featured (from the artist tag and the song's name)
            var main = t.MainArtist;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Person(string n, string role)
            {
                n = n.Trim();
                if (n.Length < 2 || n.Equals("Unknown artist", StringComparison.OrdinalIgnoreCase) || !seen.Add(n)) return;
                var key = n.ToLowerInvariant();
                var pic = lib.Tracks.Where(x => x.MainArtist.Equals(n, StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => x.HasArt).ThenByDescending(x => x.Plays).FirstOrDefault();
                a.Artists.Add(new Person { Name = n, Role = role, Pic = pic, Key = pic != null ? key : null });
            }
            if (t.Artist.Length > 0 || t.AlbumArtist.Length > 0) Person(main, "Main artist");
            foreach (var n in SplitNames(t.Artist)) Person(n, "Featured artist");
            foreach (var src in new[] { name, file })
            {
                foreach (Match m in Feat.Matches(src)) foreach (var n in SplitNames(m.Groups[1].Value)) Person(n, "Featured artist");
                var fl = FeatLoose.Match(Regex.Replace(src, @"[\(\[][^\)\]]*[\)\]]", ""));
                if (fl.Success) foreach (var n in SplitNames(fl.Groups[1].Value)) Person(n, "Featured artist");
            }

            // who made it
            void Credit(string role, string v) { v = (v ?? "").Trim().Trim('.', ' '); if (v.Length > 0 && !a.Credits.Any(c => c.Key == role && c.Value.Equals(v, StringComparison.OrdinalIgnoreCase))) a.Credits.Add(new KeyValuePair<string, string>(role, v)); }
            var producers = new List<string>();
            foreach (var src in new[] { name, file })
                foreach (Match m in Prod.Matches(src)) producers.AddRange(SplitNames(m.Groups[1].Value));
            if (tags != null)
            {
                foreach (var c in tags.Credits) if (c.Key == "Produced by") producers.AddRange(SplitNames(c.Value));
                a.Comment = tags.Comment ?? "";
                var pm = ProdLoose.Match(a.Comment);
                if (pm.Success) producers.AddRange(SplitNames(pm.Groups[1].Value));
            }
            var info = lib.InfoOf(t);
            if (info.Length > 0) { var pm = ProdLoose.Match(info); if (pm.Success) producers.AddRange(SplitNames(pm.Groups[1].Value)); }
            var prod = producers.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (prod.Count > 0) Credit("Produced by", string.Join(", ", prod));
            if (tags != null) foreach (var c in tags.Credits) if (c.Key != "Produced by") Credit(c.Key, c.Value);
            if (tags != null && tags.FullDate.Length > 4) Credit("Date", Pretty(tags.FullDate));
            else if (t.Year.Length > 0) Credit("Year", t.Year);
            if (t.Genre.Length > 0) Credit("Genre", t.Genre);
            if (info.Length > 0) Credit("Notes", info);
            // a comment that isn't just an encoder's name or a web address
            if (a.Comment.Length > 2 && a.Comment.Length < 240 && !Regex.IsMatch(a.Comment, @"^(https?://|www\.|lame|itunes|encoded|0\d+ )", RegexOptions.IgnoreCase) && !Regex.IsMatch(a.Comment, @"^[0-9A-F ]{20,}$"))
                Credit("Comment", a.Comment);
            var ext = Path.GetExtension(t.Path).TrimStart('.').ToUpperInvariant();
            int kbps = Duplicates.Kbps(t);
            Credit("File", ext + (kbps > 0 && !Duplicates.Lossless(t) ? " · " + kbps + " kbps" : Duplicates.Lossless(t) ? " · lossless" : "") + " · " + (t.Size / 1048576.0).ToString("0.0") + " MB");
            return a;
        }

        // "2016-01-19" → "January 19, 2016"
        static string Pretty(string d)
        {
            if (DateTime.TryParseExact(d.Length >= 10 ? d.Substring(0, 10) : d, new[] { "yyyy-MM-dd", "yyyy-MM", "yyyy/MM/dd" }, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dt))
                return d.Length >= 10 ? dt.ToString("MMMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture) : dt.ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
            return d;
        }
    }
}
