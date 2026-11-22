using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Aura
{
    // shared look for AURA's small windows
    class DarkForm : Form
    {
        public DarkForm(string title, int w, int h)
        {
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Surface; ForeColor = Theme.Text; Font = Theme.Font(13.5f);
            ClientSize = new Size(Theme.P(w), Theme.P(h));
            ShowInTaskbar = false; MinimizeBox = false;
            HandleCreated += (s, e) => Win.DarkTitle(Handle);
        }

        public static Button Btn(string text, bool primary)
        {
            var b = new Button { Text = text, FlatStyle = FlatStyle.Flat, BackColor = primary ? Theme.Accent : Theme.Surface2, ForeColor = primary ? Theme.OnAccent : Theme.Text, Font = Theme.Font(13.5f, FontStyle.Regular, true), Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        public static TextBox Box(string text = "") => new TextBox { Text = text ?? "", BackColor = Theme.Surface2, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Font(14.5f) };
        public static Label Lbl(string text, Color? c = null, float size = 13) => new Label { Text = text, AutoSize = false, ForeColor = c ?? Theme.Sub, Font = Theme.Font(size), TextAlign = ContentAlignment.MiddleLeft };
    }

    static class Editing
    {
        // "03 - Juice WRLD - Lucid Dreams (v2)" -> track 3, artist "Juice WRLD", title "Lucid Dreams (v2)"
        public static (string artist, string title, int track) FromName(string path)
        {
            var stem = Path.GetFileNameWithoutExtension(path).Replace('_', ' ').Trim();
            int track = 0;
            var m = Regex.Match(stem, @"^(\d{1,3})\s*(?:[.\-–)]\s*|\s+)(.+)$");
            if (m.Success && int.TryParse(m.Groups[1].Value, out track)) stem = m.Groups[2].Value.Trim();
            else track = 0;
            string artist = "", title = stem;
            var parts = Regex.Split(stem, @"\s+[-–—]\s+");
            if (parts.Length >= 2) { artist = parts[0].Trim(); title = string.Join(" - ", parts.Skip(1)).Trim(); }
            return (artist, Regex.Replace(title, @"\s+", " "), track);
        }

        // a cover, made a sensible size and kind (JPEG, at most 1200 pixels)
        public static byte[] CoverBytes(byte[] raw)
        {
            using (var ms = new MemoryStream(raw))
            using (var img = Image.FromStream(ms))
            {
                bool jpgOrPng = img.RawFormat.Guid == ImageFormat.Jpeg.Guid || img.RawFormat.Guid == ImageFormat.Png.Guid;
                if (jpgOrPng && img.Width <= 1400 && img.Height <= 1400 && raw.Length < 3 * 1024 * 1024) return raw;
                int s = Math.Min(1200, Math.Min(img.Width, img.Height));
                using (var sq = Art.Square(img, s))
                using (var o = new MemoryStream())
                {
                    var enc = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                    var ps = new EncoderParameters(1); ps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 90L);
                    sq.Save(o, enc, ps);
                    return o.ToArray();
                }
            }
        }

        static readonly char[] Bad = Path.GetInvalidFileNameChars().Concat(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' }).Distinct().ToArray();

        public static string NameFor(Track t, string pattern)
        {
            string tn = t.TrackNo > 0 ? t.TrackNo.ToString("00") : "";
            var s = pattern.Replace("{artist}", t.ArtistName).Replace("{title}", t.Name).Replace("{album}", t.AlbumName)
                           .Replace("{track}", tn).Replace("{year}", t.Year);
            s = new string(s.Select(c => Bad.Contains(c) ? ' ' : c).ToArray());
            s = Regex.Replace(s, @"\s+", " ").Trim(' ', '.', '-');
            if (s.Length > 150) s = s.Substring(0, 150).Trim();
            return s.Length == 0 ? null : s + Path.GetExtension(t.Path).ToLowerInvariant();
        }
    }

    // ---------------- Edit info ----------------
    sealed class EditInfoForm : DarkForm
    {
        readonly List<Track> tracks;
        readonly Dictionary<string, TextBox> boxes = new Dictionary<string, TextBox>();
        readonly Dictionary<string, string> start = new Dictionary<string, string>();
        readonly PictureBox cover;
        byte[] newCover; bool removeCover;
        public List<(Track, TagEdit)> Result;
        readonly Func<IList<Track>, byte[]> findCover;

        static readonly (string key, string label)[] Fields = { ("title", "Title"), ("artist", "Artist"), ("album", "Album"), ("albumartist", "Album artist"), ("year", "Year"), ("genre", "Genre"), ("track", "Track #") };

        static string Get(Track t, string k)
        {
            switch (k)
            {
                case "title": return t.Title; case "artist": return t.Artist; case "album": return t.Album; case "albumartist": return t.AlbumArtist;
                case "year": return t.Year; case "genre": return t.Genre; default: return t.TrackNo > 0 ? t.TrackNo.ToString() : "";
            }
        }

        public EditInfoForm(List<Track> tracks, Func<IList<Track>, byte[]> findCover)
            : base(tracks.Count == 1 ? "Edit info — " + tracks[0].Name : "Edit info — " + tracks.Count + " songs", 720, 520)
        {
            this.tracks = tracks; this.findCover = findCover;
            int pad = Theme.P(22), art = Theme.P(190), lx = pad * 2 + art, lw = Theme.P(110), bw = ClientSize.Width - lx - lw - pad;
            cover = new PictureBox { Bounds = new Rectangle(pad, pad, art, art), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Surface2 };
            var first = tracks.FirstOrDefault(t => t.HasArt);
            try { if (first != null) { var pic = Tags.Read(first.Path, true).Picture; if (pic != null) cover.Image = Image.FromStream(new MemoryStream(pic)); } } catch { }
            Controls.Add(cover);
            var change = Btn("Change cover", false); change.SetBounds(pad, pad + art + Theme.P(12), art, Theme.P(36));
            change.Click += (s, e) =>
            {
                var m = Menus.New();
                m.Items.Add(Menus.Item("Find one online…", () => { var b = findCover(tracks); if (b != null) SetCover(b); }));
                m.Items.Add(Menus.Item("Choose a picture file…", () =>
                {
                    using (var d = new OpenFileDialog { Filter = "Pictures|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp|All files|*.*", Title = "Choose a cover" })
                        if (d.ShowDialog(this) == DialogResult.OK)
                            try { SetCover(Editing.CoverBytes(File.ReadAllBytes(d.FileName))); } catch (Exception x) { MessageBox.Show(this, "That picture couldn't be read: " + x.Message, "AURA"); }
                }));
                m.Items.Add(Menus.Item("Remove the cover", () => { newCover = null; removeCover = true; cover.Image = null; }));
                m.Show(change, new Point(0, change.Height));
            };
            Controls.Add(change);
            int y = pad;
            foreach (var (key, label) in Fields)
            {
                var vals = tracks.Select(t => Get(t, key) ?? "").Distinct().ToList();
                string common = vals.Count == 1 ? vals[0] : "";
                var l = Lbl(label); l.SetBounds(lx, y, lw, Theme.P(34)); Controls.Add(l);
                var b = Box(common); b.SetBounds(lx + lw, y + Theme.P(2), bw, Theme.P(32)); Controls.Add(b);
                if (vals.Count > 1) b.HandleCreated += (s, e) => Win.Cue(b.Handle, "Different for each — leave as is");
                boxes[key] = b; start[key] = common;
                y += Theme.P(46);
            }
            var cant = tracks.Where(t => !TagWriter.CanWrite(t.Path)).ToList();
            var note = Lbl(cant.Count > 0 ? cant.Count + " of these can't be changed yet (" + string.Join(", ", cant.Select(t => Path.GetExtension(t.Path)).Distinct()) + " files)."
                                         : tracks.Count > 1 ? "Only the boxes you change are saved; the rest stay as they are for each song." : "Changes are saved into the music file.", Theme.Faint, 12.5f);
            note.SetBounds(lx, y + Theme.P(4), bw + lw, Theme.P(40));
            Controls.Add(note);
            var ok = Btn("Save", true); ok.SetBounds(ClientSize.Width - pad - Theme.P(120), ClientSize.Height - pad - Theme.P(38), Theme.P(120), Theme.P(38));
            var cancel = Btn("Cancel", false); cancel.SetBounds(ok.Left - Theme.P(110), ok.Top, Theme.P(100), Theme.P(38));
            ok.Click += (s, e) => { Build(); DialogResult = DialogResult.OK; Close(); };
            cancel.Click += (s, e) => Close();
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
        }

        void SetCover(byte[] b)
        {
            newCover = b; removeCover = false;
            try { cover.Image = Image.FromStream(new MemoryStream(b)); } catch { }
        }

        void Build()
        {
            Result = new List<(Track, TagEdit)>();
            foreach (var t in tracks)
            {
                if (!TagWriter.CanWrite(t.Path)) continue;
                var e = new TagEdit();
                foreach (var (key, _) in Fields)
                {
                    var v = boxes[key].Text.Trim();
                    if (v == start[key].Trim()) continue;                 // not changed
                    if (v.Length == 0 && tracks.Count > 1 && start[key].Length == 0) continue;
                    switch (key)
                    {
                        case "title": e.Title = v; break; case "artist": e.Artist = v; break; case "album": e.Album = v; break;
                        case "albumartist": e.AlbumArtist = v; break; case "year": e.Year = v; break; case "genre": e.Genre = v; break; case "track": e.Track = v; break;
                    }
                }
                if (newCover != null) e.Picture = newCover;
                if (removeCover) e.RemovePicture = true;
                if (e.Any) Result.Add((t, e));
            }
        }
    }

    // ---------------- Cover finder ----------------
    [DataContract] sealed class ItunesResp { [DataMember(Name = "results")] public List<ItunesItem> Results; }
    [DataContract] sealed class ItunesItem
    {
        [DataMember(Name = "collectionName")] public string Album;
        [DataMember(Name = "trackName")] public string Track;
        [DataMember(Name = "artistName")] public string Artist;
        [DataMember(Name = "artworkUrl100")] public string Art;
    }
    [DataContract] sealed class DeezerResp { [DataMember(Name = "data")] public List<DeezerItem> Data; }
    [DataContract] sealed class DeezerItem
    {
        [DataMember(Name = "title")] public string Title;
        [DataMember(Name = "cover_medium")] public string Small;
        [DataMember(Name = "cover_xl")] public string Big;
        [DataMember(Name = "artist")] public DeezerArtist Artist;
    }
    [DataContract] sealed class DeezerArtist { [DataMember(Name = "name")] public string Name; }

    sealed class CoverHit { public string Source, Title, Artist, Small, Big; public Image Thumb; }

    static class CoverSearch
    {
        public static string ItunesUrl = Environment.GetEnvironmentVariable("AURA_ITUNES_URL") ?? "https://itunes.apple.com";
        public static string DeezerUrl = Environment.GetEnvironmentVariable("AURA_DEEZER_URL") ?? "https://api.deezer.com";

        public static List<CoverHit> Search(string q, out bool offline)
        {
            var jobs = new[]
            {
                Task.Run(() => (Net.Json<ItunesResp>(Net.Get(ItunesUrl + "/search?media=music&entity=album&limit=12&term=" + Uri.EscapeDataString(q)))?.Results ?? new List<ItunesItem>())
                    .Where(r => !string.IsNullOrEmpty(r.Art)).Select(r => new CoverHit { Source = "Apple Music", Title = r.Album ?? r.Track ?? "", Artist = r.Artist ?? "", Small = r.Art, Big = r.Art.Replace("100x100bb", "1000x1000bb") }).ToList()),
                Task.Run(() => (Net.Json<ItunesResp>(Net.Get(ItunesUrl + "/search?media=music&entity=song&limit=8&term=" + Uri.EscapeDataString(q)))?.Results ?? new List<ItunesItem>())
                    .Where(r => !string.IsNullOrEmpty(r.Art)).Select(r => new CoverHit { Source = "Apple Music", Title = r.Album ?? r.Track ?? "", Artist = r.Artist ?? "", Small = r.Art, Big = r.Art.Replace("100x100bb", "1000x1000bb") }).ToList()),
                Task.Run(() => (Net.Json<DeezerResp>(Net.Get(DeezerUrl + "/search/album?limit=12&q=" + Uri.EscapeDataString(q)))?.Data ?? new List<DeezerItem>())
                    .Where(r => !string.IsNullOrEmpty(r.Big)).Select(r => new CoverHit { Source = "Deezer", Title = r.Title ?? "", Artist = r.Artist?.Name ?? "", Small = r.Small ?? r.Big, Big = r.Big }).ToList()),
            };
            try { Task.WaitAll(jobs, 10000); } catch { }
            var all = new List<CoverHit>(); int errors = 0;
            foreach (var j in jobs) { if (j.Status == TaskStatus.RanToCompletion) all.AddRange(j.Result); else errors++; }
            offline = errors == jobs.Length;
            // the same picture often comes back twice
            return all.GroupBy(h => h.Big).Select(g => g.First()).Take(30).ToList();
        }

        public static byte[] Download(string url)
        {
            var file = Path.GetTempFileName();
            try { Net.Download(url, file, null); return File.ReadAllBytes(file); }
            finally { try { File.Delete(file); } catch { } }
        }
    }

    sealed class CoverFinderForm : DarkForm
    {
        readonly TextBox query;
        readonly Label status;
        readonly FlowLayoutPanel grid;
        readonly Button use;
        readonly CheckBox wholeAlbum;
        List<CoverHit> hits = new List<CoverHit>();
        CoverHit picked;
        int searchNo;
        public byte[] Result;
        public bool WholeAlbum => wholeAlbum.Visible && wholeAlbum.Checked;

        public CoverFinderForm(string q, int albumSongs)
            : base("Find a cover", 880, 620)
        {
            int pad = Theme.P(18);
            query = Box(q);
            var go = Btn("Search", true);
            go.Click += (s, e) => Search();
            query.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Search(); } };
            status = Lbl("");
            grid = new FlowLayoutPanel { AutoScroll = true, BackColor = Theme.Bg, Padding = new Padding(Theme.P(8)) };
            use = Btn("Use this cover", true); use.Enabled = false;
            use.Click += (s, e) => Use();
            var file = Btn("Choose a file…", false);
            file.Click += (s, e) =>
            {
                using (var d = new OpenFileDialog { Filter = "Pictures|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp|All files|*.*", Title = "Choose a cover" })
                    if (d.ShowDialog(this) == DialogResult.OK)
                        try { Result = Editing.CoverBytes(File.ReadAllBytes(d.FileName)); DialogResult = DialogResult.OK; Close(); }
                        catch (Exception x) { status.Text = "That picture couldn't be read: " + x.Message; }
            };
            var cancel = Btn("Cancel", false); cancel.Click += (s, e) => Close();
            wholeAlbum = new CheckBox { Text = "Use it for all " + albumSongs + " songs on this album", Checked = true, Visible = albumSongs > 1, ForeColor = Theme.Text, Font = Theme.Font(13), AutoSize = false };
            Controls.AddRange(new Control[] { query, go, status, grid, use, file, cancel, wholeAlbum });
            CancelButton = cancel;
            void Place()
            {
                int w = ClientSize.Width, h = ClientSize.Height, bh = Theme.P(38);
                go.SetBounds(w - pad - Theme.P(110), pad, Theme.P(110), bh);
                query.SetBounds(pad, pad + (bh - query.PreferredHeight) / 2, w - pad * 3 - Theme.P(110), query.PreferredHeight);
                status.SetBounds(pad, pad + bh + Theme.P(4), w - pad * 2, Theme.P(28));
                int top = pad + bh + Theme.P(36), bottom = h - pad - bh - Theme.P(14);
                grid.SetBounds(pad, top, w - pad * 2, bottom - top);
                use.SetBounds(w - pad - Theme.P(160), h - pad - bh, Theme.P(160), bh);
                cancel.SetBounds(use.Left - Theme.P(110), h - pad - bh, Theme.P(100), bh);
                file.SetBounds(cancel.Left - Theme.P(150), h - pad - bh, Theme.P(140), bh);
                wholeAlbum.SetBounds(pad, h - pad - bh, Math.Max(10, file.Left - pad * 2), bh);
            }
            Resize += (s, e) => Place();
            Place();
            Shown += (s, e) => Search();
        }

        void Search()
        {
            int no = ++searchNo;
            string q = query.Text.Trim();
            if (q.Length == 0) return;
            status.Text = "Searching Apple Music and Deezer…";
            grid.Controls.Clear(); hits.Clear(); picked = null; use.Enabled = false;
            Task.Run(() => { var r = CoverSearch.Search(q, out bool off); return (r, off); }).ContinueWith(tk =>
            {
                if (IsDisposed) return;
                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed || no != searchNo) return;
                    if (tk.IsFaulted) { status.Text = "Something went wrong: " + tk.Exception?.GetBaseException().Message; return; }
                    var (r, off) = tk.Result;
                    hits = r;
                    status.Text = off ? "Couldn't reach Apple Music or Deezer — check your internet." : r.Count == 0 ? "Nothing found. Try the album or song name with the artist." : "Click a cover, then Use this cover.";
                    foreach (var h in r) AddTile(h, no);
                }));
            });
        }

        void AddTile(CoverHit h, int no)
        {
            int s = Theme.P(150);
            var tile = new Panel { Size = new Size(s + Theme.P(12), s + Theme.P(54)), Margin = new Padding(Theme.P(6)), BackColor = Theme.Bg, Cursor = Cursors.Hand, Tag = h };
            var pb = new PictureBox { Bounds = new Rectangle(Theme.P(6), Theme.P(6), s, s), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Surface2, Cursor = Cursors.Hand };
            var lb = new Label { Bounds = new Rectangle(Theme.P(6), s + Theme.P(8), s, Theme.P(42)), ForeColor = Theme.Sub, Font = Theme.Font(11.5f), Text = h.Title + "\n" + h.Artist + " · " + h.Source, AutoEllipsis = true, Cursor = Cursors.Hand };
            tile.Controls.Add(pb); tile.Controls.Add(lb);
            EventHandler click = (s2, e) =>
            {
                picked = h; use.Enabled = true;
                foreach (Control c in grid.Controls) c.BackColor = c.Tag == h ? Theme.Alpha(Theme.Accent, 120) : Theme.Bg;
            };
            tile.Click += click; pb.Click += click; lb.Click += click;
            pb.DoubleClick += (s2, e) => { click(s2, e); Use(); };
            grid.Controls.Add(tile);
            Task.Run(() => CoverSearch.Download(h.Small)).ContinueWith(tk =>
            {
                if (tk.IsFaulted || IsDisposed) return;
                try
                {
                    var img = Image.FromStream(new MemoryStream(tk.Result));
                    BeginInvoke(new Action(() => { if (!IsDisposed && no == searchNo) pb.Image = img; }));
                }
                catch { }
            });
        }

        void Use()
        {
            if (picked == null) return;
            status.Text = "Getting the full-size picture…";
            use.Enabled = false;
            var h = picked;
            Task.Run(() => Editing.CoverBytes(CoverSearch.Download(h.Big))).ContinueWith(tk =>
            {
                if (IsDisposed) return;
                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed) return;
                    if (tk.IsFaulted)
                    {
                        // the big one failed: the small one is better than nothing
                        try { Result = Editing.CoverBytes(CoverSearch.Download(h.Small)); }
                        catch { status.Text = "Couldn't download that cover: " + tk.Exception?.GetBaseException().Message; use.Enabled = true; return; }
                    }
                    else Result = tk.Result;
                    DialogResult = DialogResult.OK;
                    Close();
                }));
            });
        }

#if TESTHOOKS
        internal int TestCount => hits.Count;
        internal void TestPick(int i) { picked = hits[i]; Use(); }
#endif
    }

    // ---------------- Rename files ----------------
    sealed class RenameForm : DarkForm
    {
        readonly List<Track> tracks;
        readonly TextBox pattern, preview;
        public string Pattern => pattern.Text;

        public RenameForm(List<Track> tracks, string last)
            : base("Rename files — " + Theme.Count(tracks.Count, "song"), 760, 520)
        {
            this.tracks = tracks;
            int pad = Theme.P(20);
            var l = Lbl("Name pattern — {artist} {title} {album} {track} {year}"); l.SetBounds(pad, pad, Theme.P(700), Theme.P(26));
            pattern = Box(string.IsNullOrWhiteSpace(last) ? "{artist} - {title}" : last); pattern.SetBounds(pad, pad + Theme.P(30), ClientSize.Width - pad * 2, Theme.P(32));
            preview = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = false, BackColor = Theme.Bg, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Font = Theme.Font(13) };
            preview.SetBounds(pad, pad + Theme.P(78), ClientSize.Width - pad * 2, ClientSize.Height - pad * 2 - Theme.P(78) - Theme.P(52));
            var ok = Btn("Rename", true); ok.SetBounds(ClientSize.Width - pad - Theme.P(120), ClientSize.Height - pad - Theme.P(38), Theme.P(120), Theme.P(38));
            var cancel = Btn("Cancel", false); cancel.SetBounds(ok.Left - Theme.P(110), ok.Top, Theme.P(100), Theme.P(38));
            ok.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            cancel.Click += (s, e) => Close();
            var note = Lbl("Files stay in their folders. Plays, playlists and favorites stay with each song.", Theme.Faint, 12.5f);
            note.SetBounds(pad, ok.Top, cancel.Left - pad * 2, Theme.P(38));
            Controls.AddRange(new Control[] { l, pattern, preview, ok, cancel, note });
            AcceptButton = ok; CancelButton = cancel;
            pattern.TextChanged += (s, e) => Preview();
            Preview();
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
        }

        void Preview()
        {
            var lines = new List<string>();
            foreach (var t in tracks.Take(300))
            {
                var n = Editing.NameFor(t, pattern.Text);
                var old = Path.GetFileName(t.Path);
                lines.Add(n == null ? old + "   (skipped: the pattern gives an empty name)" : n == old ? old + "   (no change)" : old + "   →   " + n);
            }
            if (tracks.Count > 300) lines.Add("… and " + (tracks.Count - 300) + " more");
            preview.Text = string.Join(Environment.NewLine, lines);
        }
    }
}
