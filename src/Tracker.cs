using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Aura
{
    // Fan trackers: the community spreadsheets that list every song of an artist with its era, producer,
    // recording and leak dates. AURA reads one (a .csv, an .xlsx, or a Google Sheets link) and fills in
    // titles, eras and years for the songs it recognises. Like the first AURA (app.py: match_tracker).
    static class Tracker
    {
        public sealed class Sheet { public List<string> Headers = new List<string>(); public List<List<string>> Rows = new List<List<string>>(); public string Name = ""; }

        public static readonly (string field, string[] keys)[] Fields =
        {
            ("name", new[] { "name", "title", "song", "track" }),
            ("era", new[] { "era", "album", "project", "tape", "section", "sheet" }),
            ("producer", new[] { "prod", "producer", "producers", "beat" }),
            ("recorded", new[] { "recorded", "record date", "recording date", "file date", "session date", "date recorded", "made" }),
            ("leaked", new[] { "leak", "leaked", "leak date", "date leaked", "surfaced", "release date", "released" }),
            ("notes", new[] { "notes", "note", "description", "info", "details" }),
            ("quality", new[] { "quality" }),
            ("type", new[] { "type", "status", "availability", "available" }),
            ("features", new[] { "feat", "features", "featuring" }),
        };

        // ---------- reading ----------
        public static Sheet Load(byte[] data, string name)
        {
            var low = (name ?? "").ToLowerInvariant();
            if (low.EndsWith(".xlsx") || low.EndsWith(".xlsm") || (data.Length > 2 && data[0] == 'P' && data[1] == 'K')) return Xlsx(data, name);
            return Csv(Encoding.UTF8.GetString(data).TrimStart('﻿'), name);
        }

        public static Sheet Download(string url)
        {
            var m = Regex.Match(url ?? "", @"docs\.google\.com/spreadsheets/d/([A-Za-z0-9_-]+)");
            string name;
            if (m.Success) { url = "https://docs.google.com/spreadsheets/d/" + m.Groups[1].Value + "/export?format=xlsx"; name = "sheet.xlsx"; }
            else if (!(url ?? "").StartsWith("https://")) throw new InvalidOperationException("Paste a Google Sheets link, or pick a .csv or .xlsx file");
            else name = url.Substring(url.LastIndexOf('/') + 1);
            var ua = Net.AppUA;
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36";
            req.Timeout = 40000; req.AllowAutoRedirect = true;
            try
            {
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var s = resp.GetResponseStream())
                using (var ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    if ((resp.ContentType ?? "").Contains("text/html")) throw new InvalidOperationException("That link opens a web page, not a spreadsheet. In Google Sheets: Share → General access → Anyone with the link.");
                    return Load(ms.ToArray(), name);
                }
            }
            catch (WebException e) when (e.Response is HttpWebResponse r)
            {
                throw new InvalidOperationException("Couldn't open that sheet (" + (int)r.StatusCode + "). In Google Sheets: Share → General access → Anyone with the link.");
            }
        }

        static Sheet Csv(string text, string name)
        {
            var rows = new List<List<string>>();
            var row = new List<string>(); var cell = new StringBuilder();
            bool q = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (q)
                {
                    if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; } else q = false; }
                    else cell.Append(c);
                }
                else if (c == '"') q = true;
                else if (c == ',') { row.Add(cell.ToString().Trim()); cell.Clear(); }
                else if (c == '\n' || c == '\r')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    row.Add(cell.ToString().Trim()); cell.Clear(); rows.Add(row); row = new List<string>();
                }
                else cell.Append(c);
            }
            if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString().Trim()); rows.Add(row); }
            return Build(new List<(string, List<List<string>>)> { (name, rows) }, false);
        }

        static Sheet Xlsx(byte[] data, string name)
        {
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace pkg = "http://schemas.openxmlformats.org/package/2006/relationships";
            using (var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read))
            {
                XDocument Open(string path) { var e = zip.GetEntry(path); if (e == null) return null; using (var s = e.Open()) return XDocument.Load(s); }
                var shared = new List<string>();
                var sst = Open("xl/sharedStrings.xml");
                if (sst != null) foreach (var si in sst.Root.Elements(ns + "si")) shared.Add(string.Concat(si.Descendants(ns + "t").Select(t => t.Value)));
                // which number styles are dates
                var dateStyles = new HashSet<int>();
                var styles = Open("xl/styles.xml");
                if (styles != null)
                {
                    var custom = new HashSet<int>();
                    foreach (var nf in styles.Root.Element(ns + "numFmts")?.Elements(ns + "numFmt") ?? Enumerable.Empty<XElement>())
                    {
                        var code = ((string)nf.Attribute("formatCode") ?? "").ToLowerInvariant();
                        code = Regex.Replace(code, "\"[^\"]*\"|\\[[^\\]]*\\]", "");
                        if (Regex.IsMatch(code, "[dmy]")) custom.Add((int)nf.Attribute("numFmtId"));
                    }
                    int k = 0;
                    foreach (var xf in styles.Root.Element(ns + "cellXfs")?.Elements(ns + "xf") ?? Enumerable.Empty<XElement>())
                    {
                        int id = (int?)xf.Attribute("numFmtId") ?? 0;
                        if ((id >= 14 && id <= 22) || (id >= 45 && id <= 47) || custom.Contains(id)) dateStyles.Add(k);
                        k++;
                    }
                }
                var wb = Open("xl/workbook.xml");
                var rels = Open("xl/_rels/workbook.xml.rels");
                var targets = rels?.Root.Elements(pkg + "Relationship").ToDictionary(r => (string)r.Attribute("Id"), r => (string)r.Attribute("Target")) ?? new Dictionary<string, string>();
                var tabs = new List<(string, List<List<string>>)>();
                foreach (var sh in wb.Root.Element(ns + "sheets").Elements(ns + "sheet"))
                {
                    var rid = (string)sh.Attribute(rel + "id");
                    if (rid == null || !targets.TryGetValue(rid, out var target)) continue;
                    target = target.TrimStart('/');
                    var path = target.StartsWith("xl/") ? target : "xl/" + target;
                    var doc = Open(path);
                    if (doc == null) continue;
                    var rows = new List<List<string>>();
                    foreach (var r in doc.Root.Element(ns + "sheetData")?.Elements(ns + "row") ?? Enumerable.Empty<XElement>())
                    {
                        var cells = new List<string>();
                        foreach (var c in r.Elements(ns + "c"))
                        {
                            int col = ColumnOf((string)c.Attribute("r"), cells.Count);
                            while (cells.Count < col) cells.Add("");
                            string t = (string)c.Attribute("t"), v = (string)c.Element(ns + "v");
                            int s = (int?)c.Attribute("s") ?? 0;
                            string val;
                            if (t == "s" && int.TryParse(v, out var si) && si < shared.Count) val = shared[si];
                            else if (t == "inlineStr") val = string.Concat(c.Descendants(ns + "t").Select(x => x.Value));
                            else if (t == "b") val = v == "1" ? "TRUE" : "FALSE";
                            else if (v != null && dateStyles.Contains(s) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var oa) && oa > 1 && oa < 80000)
                                val = DateTime.FromOADate(oa).ToString("yyyy-MM-dd");
                            else if (v != null && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var num) && Math.Abs(num - Math.Round(num)) < 1e-9) val = ((long)Math.Round(num)).ToString();
                            else val = v ?? "";
                            cells.Add(val.Trim());
                        }
                        rows.Add(cells);
                    }
                    tabs.Add(((string)sh.Attribute("name") ?? "Sheet", rows));
                }
                return Build(tabs, true);
            }
        }

        static int ColumnOf(string cellRef, int fallback)
        {
            if (string.IsNullOrEmpty(cellRef)) return fallback;
            int n = 0;
            foreach (var ch in cellRef) { if (ch < 'A' || ch > 'Z') break; n = n * 26 + (ch - 'A' + 1); }
            return n > 0 ? n - 1 : fallback;
        }

        // every tab combined, with the tab's name in a "Sheet" column (trackers often keep one era per tab)
        static Sheet Build(List<(string name, List<List<string>> rows)> tabs, bool sheetColumn)
        {
            var res = new Sheet();
            foreach (var (tab, raw) in tabs)
            {
                var rows = raw.Where(r => r.Any(c => c.Length > 0)).ToList();
                if (rows.Count == 0) continue;
                int hi = HeaderRow(rows);
                var h = rows[hi].Select((x, i) => x.Length > 0 ? x : "Column " + (i + 1)).ToList();
                if (res.Headers.Count == 0) { res.Headers = h.ToList(); if (sheetColumn) res.Headers.Add("Sheet"); }
                int width = res.Headers.Count - (sheetColumn ? 1 : 0);
                foreach (var r0 in rows.Skip(hi + 1))
                {
                    var r = r0.Concat(Enumerable.Repeat("", Math.Max(0, h.Count - r0.Count))).Take(h.Count).ToList();
                    // line columns up by header name when tabs differ
                    if (!h.SequenceEqual(res.Headers.Take(width)))
                    {
                        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        for (int i = 0; i < h.Count; i++) if (!map.ContainsKey(h[i])) map[h[i]] = r[i];
                        r = res.Headers.Take(width).Select(x => map.TryGetValue(x, out var v) ? v : "").ToList();
                    }
                    else r = r.Concat(Enumerable.Repeat("", Math.Max(0, width - r.Count))).Take(width).ToList();
                    if (sheetColumn) r.Add(tab);
                    res.Rows.Add(r);
                }
                if (res.Name.Length == 0) res.Name = tab;
            }
            return res;
        }

        // trackers often have a title banner above the real header row
        static int HeaderRow(List<List<string>> rows)
        {
            int best = 0, bestS = -1;
            for (int i = 0; i < Math.Min(15, rows.Count); i++)
            {
                int s = rows[i].Count(c => c.Length > 0 && Fields.Any(f => f.keys.Any(k => c.ToLowerInvariant().Contains(k))));
                if (s > bestS) { best = i; bestS = s; }
            }
            return best;
        }

        public static Dictionary<string, int> GuessMapping(List<string> headers)
        {
            var m = new Dictionary<string, int>();
            foreach (var (field, keys) in Fields)
                for (int i = 0; i < headers.Count; i++)
                {
                    var hl = headers[i].ToLowerInvariant();
                    if (m.ContainsValue(i)) continue;
                    if (keys.Any(k => hl == k || hl.StartsWith(k) || hl.Contains(" " + k))) { m[field] = i; break; }
                }
            return m;
        }

        // ---------- matching ----------
        static readonly Regex Prod = new Regex(@"[\(\[\{]?\s*prod(?:uced)?\.?\s*(?:by)?\s*[:\-]?\s*([^\)\]\}\[\(]+)[\)\]\}]?", RegexOptions.IgnoreCase);
        static readonly Regex Feat = new Regex(@"[\(\[\{]?\s*(?:feat|ft|featuring|w/|with)\.?\s+([^\)\]\}\[\(]+)[\)\]\}]?", RegexOptions.IgnoreCase);

        public sealed class Item { public string Main = "", Producer = "", Features = "", Era = "", Recorded = "", Leaked = "", Notes = "", Quality = "", Type = "", Raw = ""; public List<string> Alts = new List<string>(); }

        // "Bad Energy (Wishing Well) [prod. Nick Mira]" -> main title, other names, producer, features
        public static (string main, List<string> alts, string prod, string feats) Names(string name)
        {
            var lines = (name ?? "").Replace("\r", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            if (lines.Count == 0) return ("", new List<string>(), "", "");
            var first = lines[0];
            string prod = "", feats = "";
            var m = Prod.Match(first);
            if (m.Success) { prod = m.Groups[1].Value.Trim(' ', '-', '–', '—', '_', '.', '&', ','); first = (first.Substring(0, m.Index) + " " + first.Substring(m.Index + m.Length)).Trim(); }
            m = Feat.Match(first);
            if (m.Success) { feats = m.Groups[1].Value.Trim(' ', '-', '–', '—', '_', '.', '&', ','); first = (first.Substring(0, m.Index) + " " + first.Substring(m.Index + m.Length)).Trim(); }
            var alts = new List<string>();
            foreach (Match b in Regex.Matches(first, @"[\(\[]([^\)\]]+)[\)\]]"))
            {
                var inner = b.Groups[1].Value;
                if (!Regex.IsMatch(inner, @"\b(v\d|ver|version|snippet|og|cdq|hq|lq|leak|session|remix|edit|demo|ref)\b", RegexOptions.IgnoreCase))
                    alts.AddRange(Regex.Split(inner, @"[,/]| & ").Select(a => a.Trim()).Where(a => a.Length > 0));
            }
            var main = Regex.Replace(first, @"\s*[\(\[][^\)\]]*[\)\]]\s*", " ");
            var parts = Regex.Split(main, @"\s+/\s+").Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
            main = parts.Count > 0 ? parts[0] : main.Trim();
            alts.InsertRange(0, parts.Skip(1));
            foreach (var l0 in lines.Skip(1))
            {
                var l = Regex.Replace(l0, @"^[\(\[]|[\)\]]$", "").Trim();
                if (l.Length > 0 && l.Length < 60) alts.AddRange(Regex.Split(l, "[,/]").Select(a => a.Trim()).Where(a => a.Length > 0));
            }
            return (Regex.Replace(main, @"\s+", " ").Trim(' ', '-'), alts, prod, feats);
        }

        public static string YearOf(string s) { var m = Regex.Match(s ?? "", @"\b(19[6-9]\d|20[0-4]\d)\b"); return m.Success ? m.Groups[1].Value : ""; }

        public static List<Item> Items(Sheet sh, Dictionary<string, int> map)
        {
            string Get(List<string> r, string f) => map.TryGetValue(f, out var i) && i >= 0 && i < r.Count ? r[i] : "";
            var items = new List<Item>();
            string curEra = "";
            bool hasEra = map.ContainsKey("era");
            int nameCol = map.TryGetValue("name", out var nc) ? nc : -1;
            foreach (var r in sh.Rows)
            {
                var name = Get(r, "name");
                bool others = r.Where((c, i) => c.Length > 0 && i != nameCol && sh.Headers[i] != "Sheet").Any();
                if (name.Length > 0 && !others && !hasEra) { curEra = Regex.Replace(name, @"\s+", " ").Trim(); continue; }      // a section heading row
                if (name.Length == 0) continue;
                var (main, alts, prod, feats) = Names(name);
                if (main.Length == 0) continue;
                var era = Get(r, "era"); if (era.Length == 0) era = curEra;
                items.Add(new Item
                {
                    Main = main, Alts = alts, Producer = Get(r, "producer").Length > 0 ? Get(r, "producer") : prod,
                    Features = Get(r, "features").Length > 0 ? Get(r, "features") : feats, Era = era,
                    Recorded = Get(r, "recorded"), Leaked = Get(r, "leaked"), Notes = Get(r, "notes"), Quality = Get(r, "quality"), Type = Get(r, "type"),
                    Raw = name.Split('\n')[0],
                });
            }
            return items;
        }

        public sealed class Proposal
        {
            public Track Track; public Item Item; public double Score;
            public string Title, Era, Year, Info;       // null: no change
        }

        public static string InfoOf(Item it)
        {
            var bits = new List<string>();
            if (it.Producer.Length > 0) bits.Add("Prod. " + it.Producer);
            if (it.Recorded.Length > 0) bits.Add("Recorded " + it.Recorded);
            if (it.Leaked.Length > 0) bits.Add("Leaked " + it.Leaked);
            if (it.Alts.Count > 0) bits.Add("Also known as " + string.Join(", ", it.Alts.Take(4)));
            if (it.Quality.Length > 0) bits.Add(it.Quality);
            if (it.Notes.Length > 0) bits.Add(it.Notes);
            return string.Join("  ·  ", bits);
        }

        static string Words(string s) => LyricsOnline.Norm(s);

        public static List<Proposal> Match(List<Item> items, IEnumerable<Track> tracks, Library lib, bool title, bool era, bool year, bool info, bool overwrite, out int unmatched)
        {
            var index = new Dictionary<string, List<int>>();
            for (int k = 0; k < items.Count; k++)
                foreach (var nm in new[] { items[k].Main }.Concat(items[k].Alts))
                    foreach (var nn in new[] { Words(nm), Words(LyricsOnline.CleanTitle(nm)) }.Distinct())
                    {
                        if (nn.Length == 0) continue;
                        if (!index.TryGetValue(nn, out var l)) index[nn] = l = new List<int>();
                        if (!l.Contains(k)) l.Add(k);
                    }
            var names = index.Keys.ToList();
            var outp = new List<Proposal>();
            unmatched = 0;
            foreach (var t in tracks)
            {
                var bare = Words(LyricsOnline.CleanTitle(t.Title.Length > 0 ? t.Title : Path.GetFileNameWithoutExtension(t.Path)));
                if (bare.Length == 0) continue;
                double score = 1;
                if (!index.TryGetValue(bare, out var cands))
                {
                    string best = null; double bs = 0;
                    foreach (var n in names) { if (Math.Abs(n.Length - bare.Length) > Math.Max(3, bare.Length / 5)) continue; var s = LyricsOnline.Sim(n, bare); if (s > bs) { bs = s; best = n; } }
                    if (best != null && bs >= 0.86) { cands = index[best]; score = Math.Round(bs, 3); }
                }
                if (cands == null) { unmatched++; continue; }
                // several rows with the same name (versions): prefer the one whose era matches
                var it = items[cands[0]];
                if (cands.Count > 1 && t.Era.Length > 0)
                    foreach (var k in cands) if (Words(items[k].Era) == Words(t.Era)) { it = items[k]; break; }
                var p = new Proposal { Track = t, Item = it, Score = score };
                string Put(string have, string v) { v = (v ?? "").Trim(); return v.Length > 0 && (overwrite || (have ?? "").Trim().Length == 0) && v != (have ?? "") ? v : null; }
                if (title)
                {
                    // keep the version part of the title the song has ("(v2)", "[Snippet]")
                    var label = Library.VersionLabel(t);
                    var nt = it.Main + (it.Features.Length > 0 ? " (feat. " + it.Features + ")" : "") + (label.Length > 0 ? " (" + label + ")" : "");
                    p.Title = Put(t.Title, nt);
                }
                if (era) p.Era = Put(t.Era, it.Era);
                if (year) { var y = YearOf(it.Recorded); if (y.Length == 0) y = YearOf(it.Leaked); p.Year = Put(t.Year, y); }
                if (info) { var inf = InfoOf(it); var have = lib.InfoOf(t); p.Info = inf.Length > 0 && inf != have ? inf : null; }
                if (p.Title != null || p.Era != null || p.Year != null || p.Info != null) outp.Add(p);
            }
            return outp.OrderBy(p => p.Score).ThenBy(p => p.Track.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }
}
