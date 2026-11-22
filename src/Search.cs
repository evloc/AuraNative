using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Aura
{
    // Search: plain words, "exact phrases", -excluded words, field:value, numbers (bpm:>140, rating:>=4),
    // and flags (noart, nolyrics, unrated, fav, versioned, notmp3). Plain words also find songs by their lyrics.
    sealed class SearchQuery
    {
        readonly List<Func<Track, bool>> must = new List<Func<Track, bool>>();
        readonly List<string> plain = new List<string>();
        public bool OnlyPlain => must.Count == 0;
        public string PlainText => string.Join(" ", plain);
        public HashSet<long> LyricHits = new HashSet<long>();

        static string Hay(Track t) => (t.Name + " " + t.Artist + " " + t.Album + " " + t.AlbumArtist + " " + t.Genre + " " + t.Year + " " + t.Era + " " + Path.GetFileNameWithoutExtension(t.Path)).ToLowerInvariant();

        public SearchQuery(string text, Library lib, SoundStore sound)
        {
            var tokens = Regex.Matches(text ?? "", @"-?\w+:(?:""[^""]*""|\S+)|-?""[^""]*""|\S+").Cast<Match>().Select(m => m.Value).ToList();
            foreach (var raw in tokens)
            {
                bool neg = raw.StartsWith("-") && raw.Length > 1;
                var tok = neg ? raw.Substring(1) : raw;
                Func<Track, bool> f = null;
                var fm = Regex.Match(tok, @"^(\w+):(.*)$");
                if (fm.Success)
                {
                    string key = fm.Groups[1].Value.ToLowerInvariant(), val = fm.Groups[2].Value.Trim('"').ToLowerInvariant();
                    var num = Regex.Match(val, @"^(>=|<=|>|<|=)?\s*(\d+(?:\.\d+)?)$");
                    Func<double, bool> cmp = null;
                    if (num.Success)
                    {
                        double n = double.Parse(num.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                        switch (num.Groups[1].Value) { case ">": cmp = x => x > n; break; case "<": cmp = x => x < n; break; case ">=": cmp = x => x >= n; break; case "<=": cmp = x => x <= n; break; default: cmp = x => Math.Abs(x - n) < 0.5; break; }
                    }
                    switch (key)
                    {
                        case "artist": f = t => (t.Artist + " " + t.AlbumArtist).ToLowerInvariant().Contains(val); break;
                        case "title": f = t => t.Name.ToLowerInvariant().Contains(val); break;
                        case "album": f = t => t.Album.ToLowerInvariant().Contains(val); break;
                        case "genre": f = t => t.Genre.ToLowerInvariant().Contains(val); break;
                        case "era": f = t => t.Era.ToLowerInvariant().Contains(val); break;
                        case "folder": f = t => (Path.GetDirectoryName(t.Path) ?? "").ToLowerInvariant().Contains(val); break;
                        case "year": f = cmp != null ? (Func<Track, bool>)(t => double.TryParse(t.Year.Length >= 4 ? t.Year.Substring(0, 4) : t.Year, out var y) && cmp(y)) : t => t.Year.Contains(val); break;
                        case "rating": if (cmp != null) f = t => cmp(t.Rating); break;
                        case "plays": if (cmp != null) f = t => cmp(t.Plays); break;
                        case "bpm": if (cmp != null) f = t => { var b = sound?.Get(t)?.Bpm ?? 0; return b > 0 && cmp(b); }; break;
                        case "key": f = t => { var k = sound?.Get(t)?.Key ?? ""; return k.ToLowerInvariant().Contains(val) || KeyInfo.Camelot(k).ToLowerInvariant() == val; }; break;
                        case "kbps": case "bitrate": if (cmp != null) f = t => cmp(Duplicates.Kbps(t)); break;
                        case "prod": case "producer": case "info": case "notes": f = t => lib.InfoOf(t).ToLowerInvariant().Contains(val); break;
                        case "song": f = t => Library.VersionKey(t).StartsWith(LyricsOnline.Norm(val)); break;
                        case "lyrics": { var hits = lib.SearchLyrics(val); f = t => hits.Contains(t.Id); break; }
                        case "type": case "ext": f = t => Path.GetExtension(t.Path).TrimStart('.').ToLowerInvariant() == val.TrimStart('.'); break;
                    }
                }
                else
                {
                    switch (tok.ToLowerInvariant())
                    {
                        case "noart": f = t => !t.HasArt; break;
                        case "nolyrics": f = t => !t.HasLyrics; break;
                        case "unrated": f = t => t.Rating == 0; break;
                        case "rated": f = t => t.Rating > 0; break;
                        case "fav": case "favorite": case "favorites": f = t => t.Fav; break;
                        case "versioned": f = t => lib.VersionsOf(t).Count > 1; break;
                        case "unversioned": f = t => lib.VersionsOf(t).Count <= 1; break;
                        case "notmp3": f = t => !string.Equals(Path.GetExtension(t.Path), ".mp3", StringComparison.OrdinalIgnoreCase); break;
                        case "noera": f = t => t.Era.Length == 0; break;
                        case "unplayed": case "neverplayed": f = t => t.Plays == 0; break;
                    }
                }
                if (f == null)
                {
                    var w = tok.Trim('"').ToLowerInvariant();
                    if (w.Length == 0) continue;
                    if (!neg && !(fm.Success)) { plain.Add(w); continue; }
                    f = t => Hay(t).Contains(w);
                }
                if (neg) { var g = f; must.Add(t => !g(t)); } else must.Add(f);
            }
            if (plain.Count > 0) LyricHits = lib.SearchLyrics(string.Join(" ", plain));
        }

        public bool Matches(Track t)
        {
            foreach (var f in must) if (!f(t)) return false;
            if (plain.Count == 0) return true;
            var hay = Hay(t);
            return plain.All(w => hay.Contains(w)) || LyricHits.Contains(t.Id);
        }

        public bool ByLyrics(Track t)
        {
            if (plain.Count == 0 || !LyricHits.Contains(t.Id)) return false;
            var hay = Hay(t);
            return !plain.All(w => hay.Contains(w));
        }
    }
}
