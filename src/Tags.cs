using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Aura
{
    sealed class TagInfo
    {
        public string Title = "", Artist = "", Album = "", AlbumArtist = "", Year = "", Genre = "", Track = "";
        public double Duration;
        public byte[] Picture;          // only filled when asked for
        public bool HasPicture;
        // lyrics, as the first AURA (and most taggers) store them
        public string LyricsText = "";                         // plain lyrics, or LRC text
        public List<(double t, string text)> Synced;           // ID3 SYLT (timed lines)
        public string WordsRaw = "";                           // AURA_WORDS: word timings as enhanced LRC
        public bool HasLyrics => LyricsText.Trim().Length > 0 || (Synced != null && Synced.Count > 0);
        // who made it (composer, producer, label, studio, the full date…): role → names, in the order found
        public readonly List<KeyValuePair<string, string>> Credits = new List<KeyValuePair<string, string>>();
        public string Comment = "", FullDate = "";
        public void Credit(string role, string value)
        {
            value = (value ?? "").Replace("\0", "").Trim();
            if (value.Length == 0 || value.Length > 300) return;
            foreach (var kv in Credits) if (kv.Key == role && string.Equals(kv.Value, value, StringComparison.OrdinalIgnoreCase)) return;
            Credits.Add(new KeyValuePair<string, string>(role, value));
        }
        // a tag name (TXXX description, Vorbis field, …) → the role it names, or null
        public static string RoleOf(string key)
        {
            switch ((key ?? "").Trim().ToUpperInvariant().Replace("_", " "))
            {
                case "PRODUCER": case "PRODUCERS": case "PRODUCED BY": return "Produced by";
                case "COMPOSER": case "WRITER": case "SONGWRITER": case "WRITTEN BY": case "LYRICIST": return "Written by";
                case "ENGINEER": case "RECORDING ENGINEER": case "ENGINEERED BY": return "Engineered by";
                case "MIXER": case "MIXED BY": case "MIX ENGINEER": case "MIXING ENGINEER": return "Mixed by";
                case "MASTERED BY": case "MASTERING ENGINEER": return "Mastered by";
                case "LABEL": case "ORGANIZATION": case "PUBLISHER": case "RECORD LABEL": return "Label";
                case "RECORDING LOCATION": case "RECORDED AT": case "STUDIO": case "LOCATION": return "Recorded at";
                case "RECORDING DATE": case "RECORDED": case "RECORDINGDATE": return "Recorded";
                case "RELEASE DATE": case "RELEASEDATE": case "ORIGINALDATE": return "Released";
                case "LEAK DATE": case "LEAKED": case "LEAKDATE": return "Leaked";
                default: return null;
            }
        }
    }

    // Reads tags, cover and length straight from the file: MP3 (ID3v2/ID3v1), FLAC, Ogg Vorbis/Opus,
    // M4A/MP4, WAV and AIFF. Never throws: a file it can't read just has no tags.
    static class Tags
    {
        public static readonly string[] Extensions = { ".mp3", ".m4a", ".mp4", ".aac", ".flac", ".ogg", ".oga", ".opus", ".wav", ".aif", ".aiff", ".wma" };

        public static TagInfo Read(string path, bool wantPicture = false)
        {
            var t = new TagInfo();
            try
            {
                using (var f = FileX.OpenRead(path))
                {
                    var head = new byte[12];
                    int n = f.Read(head, 0, 12);
                    f.Position = 0;
                    string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                    if (n >= 4 && Ascii(head, 0, 4) == "fLaC") Flac(f, t, wantPicture);
                    else if (n >= 4 && Ascii(head, 0, 4) == "OggS") Ogg(f, t, wantPicture);
                    else if (n >= 12 && Ascii(head, 0, 4) == "RIFF" && Ascii(head, 8, 4) == "WAVE") Wav(f, t, wantPicture);
                    else if (n >= 12 && Ascii(head, 0, 4) == "FORM" && (Ascii(head, 8, 4) == "AIFF" || Ascii(head, 8, 4) == "AIFC")) Aiff(f, t, wantPicture);
                    else if (n >= 8 && Ascii(head, 4, 4) == "ftyp") Mp4(f, t, wantPicture);
                    else if (n >= 3 && Ascii(head, 0, 3) == "ID3" && ext == ".flac" && FlacAfterId3(f)) Flac(f, t, wantPicture);
                    else if (ext == ".mp3" || ext == ".aac" || (n >= 3 && Ascii(head, 0, 3) == "ID3") || (n >= 2 && head[0] == 0xFF && (head[1] & 0xE0) == 0xE0)) Mp3(f, t, wantPicture);
                }
            }
            catch { }
            t.Track = CleanTrack(t.Track);
            if (t.Year.Length > 4 && char.IsDigit(t.Year[0])) t.Year = t.Year.Substring(0, 4);
            t.Genre = CleanGenre(t.Genre);
            return t;
        }

        // ---------- helpers ----------
        static string Ascii(byte[] b, int o, int n) => o + n <= b.Length ? Encoding.ASCII.GetString(b, o, n) : "";
        static string Latin1(byte[] b, int o, int n) => Encoding.GetEncoding(28591).GetString(b, o, n);
        static int BE32(byte[] b, int o) => (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];
        static uint UBE32(byte[] b, int o) => (uint)BE32(b, o);
        static int BE24(byte[] b, int o) => (b[o] << 16) | (b[o + 1] << 8) | b[o + 2];
        static int BE16(byte[] b, int o) => (b[o] << 8) | b[o + 1];
        static int LE32(byte[] b, int o) => b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24);
        static int LE16(byte[] b, int o) => b[o] | (b[o + 1] << 8);
        static int Sync32(byte[] b, int o) => ((b[o] & 0x7F) << 21) | ((b[o + 1] & 0x7F) << 14) | ((b[o + 2] & 0x7F) << 7) | (b[o + 3] & 0x7F);

        static byte[] ReadN(Stream f, long n)
        {
            if (n < 0 || n > 64L * 1024 * 1024) throw new IOException("block too big");
            var b = new byte[n];
            int got = 0;
            while (got < n) { int r = f.Read(b, got, (int)n - got); if (r <= 0) break; got += r; }
            if (got < n) Array.Resize(ref b, got);
            return b;
        }

        static void Set(ref string field, string v)
        {
            v = (v ?? "").Replace("\0", "").Trim();
            if (field.Length == 0 && v.Length > 0) field = v;
        }
        static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant();
        static void Add(ref string field, string v)
        {
            v = (v ?? "").Replace("\0", "").Trim();
            if (v.Length == 0) return;
            field = field.Length == 0 ? v : field + ", " + v;
        }

        static string CleanTrack(string s)
        {
            s = (s ?? "").Trim();
            int slash = s.IndexOf('/');
            if (slash >= 0) s = s.Substring(0, slash).Trim();
            return int.TryParse(s, out int k) && k > 0 ? k.ToString() : "";
        }

        static readonly string[] Genres = { "Blues","Classic Rock","Country","Dance","Disco","Funk","Grunge","Hip-Hop","Jazz","Metal","New Age","Oldies","Other","Pop","R&B","Rap","Reggae","Rock","Techno","Industrial","Alternative","Ska","Death Metal","Pranks","Soundtrack","Euro-Techno","Ambient","Trip-Hop","Vocal","Jazz+Funk","Fusion","Trance","Classical","Instrumental","Acid","House","Game","Sound Clip","Gospel","Noise","AlternRock","Bass","Soul","Punk","Space","Meditative","Instrumental Pop","Instrumental Rock","Ethnic","Gothic","Darkwave","Techno-Industrial","Electronic","Pop-Folk","Eurodance","Dream","Southern Rock","Comedy","Cult","Gangsta","Top 40","Christian Rap","Pop/Funk","Jungle","Native American","Cabaret","New Wave","Psychadelic","Rave","Showtunes","Trailer","Lo-Fi","Tribal","Acid Punk","Acid Jazz","Polka","Retro","Musical","Rock & Roll","Hard Rock" };
        static string CleanGenre(string g)
        {
            g = (g ?? "").Trim();
            var m = System.Text.RegularExpressions.Regex.Match(g, @"^\((\d+)\)(.*)$");
            if (m.Success) g = m.Groups[2].Value.Trim().Length > 0 ? m.Groups[2].Value.Trim() : m.Groups[1].Value;
            if (int.TryParse(g, out int k) && k >= 0 && k < Genres.Length) g = Genres[k];
            return g;
        }

        // a FLAC with an ID3 tag stuck in front (rare), rather than an MP3 named .flac
        static bool FlacAfterId3(Stream f)
        {
            SkipId3(f);
            var m = ReadN(f, 4);
            bool flac = m.Length == 4 && Ascii(m, 0, 4) == "fLaC";
            f.Position = flac ? f.Position - 4 : 0;
            return flac;
        }

        // the description of an ID3 TXXX frame
        public static string Id3Desc(byte[] fb)
        {
            int enc = fb[0], zl = (enc == 1 || enc == 2) ? 2 : 1;
            int q = SkipZ(fb, 1, fb.Length, enc);
            return Decode(fb, 1, Math.Max(0, q - 1 - zl), enc).Trim('\0', ' ');
        }

        // ---------- ID3v2 ----------
        static long SkipId3(Stream f)
        {
            var h = ReadN(f, 10);
            if (h.Length < 10 || Ascii(h, 0, 3) != "ID3") { f.Position = 0; return 0; }
            long size = 10 + Sync32(h, 6) + ((h[5] & 0x10) != 0 ? 10 : 0);
            f.Position = size;
            return size;
        }

        static byte[] Unsync(byte[] b, int o, int n)
        {
            var outp = new List<byte>(n);
            for (int i = o; i < o + n; i++)
            {
                outp.Add(b[i]);
                if (b[i] == 0xFF && i + 1 < o + n && b[i + 1] == 0x00) i++;
            }
            return outp.ToArray();
        }

        static string Id3Text(byte[] b, int o, int n)
        {
            if (n <= 0) return "";
            int enc = b[o];
            string s = Decode(b, o + 1, n - 1, enc);
            // several values are separated by NULs (ID3v2.4)
            var parts = s.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
            var keep = new List<string>();
            foreach (var p in parts) if (p.Trim().Length > 0) keep.Add(p.Trim());
            return string.Join(", ", keep);
        }

        static string Decode(byte[] b, int o, int n, int enc)
        {
            if (n <= 0) return "";
            switch (enc)
            {
                case 1:
                    if (n >= 2 && b[o] == 0xFE && b[o + 1] == 0xFF) return Encoding.BigEndianUnicode.GetString(b, o + 2, (n - 2) & ~1);
                    if (n >= 2 && b[o] == 0xFF && b[o + 1] == 0xFE) return Encoding.Unicode.GetString(b, o + 2, (n - 2) & ~1);
                    return Encoding.Unicode.GetString(b, o, n & ~1);
                case 2: return Encoding.BigEndianUnicode.GetString(b, o, n & ~1);
                case 3: return Encoding.UTF8.GetString(b, o, n);
                default: return Latin1(b, o, n);
            }
        }

        // position just past a NUL terminator in the given encoding
        static int SkipZ(byte[] b, int o, int end, int enc)
        {
            if (enc == 1 || enc == 2)
            {
                for (int i = o; i + 1 < end; i += 2) if (b[i] == 0 && b[i + 1] == 0) return i + 2;
                return end;
            }
            for (int i = o; i < end; i++) if (b[i] == 0) return i + 1;
            return end;
        }

        // Reads an ID3v2 tag at the stream's position. Returns the tag's size (0 if none).
        static long Id3v2(Stream f, TagInfo t, bool wantPic)
        {
            long start = f.Position;
            var h = ReadN(f, 10);
            if (h.Length < 10 || Ascii(h, 0, 3) != "ID3") { f.Position = start; return 0; }
            int ver = h[3], flags = h[5];
            int size = Sync32(h, 6);
            long total = 10 + size + ((flags & 0x10) != 0 ? 10 : 0);
            var body = ReadN(f, size);
            f.Position = start + total;
            if (ver < 2 || ver > 4) return total;
            if (ver < 4 && (flags & 0x80) != 0) body = Unsync(body, 0, body.Length);
            int p = 0;
            if ((flags & 0x40) != 0 && ver >= 3 && body.Length >= 4)
                p = ver == 3 ? 4 + BE32(body, 0) : Sync32(body, 0);
            byte[] bestPic = null; int bestType = 99;
            while (p + (ver == 2 ? 6 : 10) <= body.Length)
            {
                string id; int fsize, fflags = 0, hdr;
                if (ver == 2) { id = Ascii(body, p, 3); fsize = BE24(body, p + 3); hdr = 6; }
                else
                {
                    id = Ascii(body, p, 4);
                    fsize = ver == 4 ? Sync32(body, p + 4) : BE32(body, p + 4);
                    fflags = BE16(body, p + 8); hdr = 10;
                    // some v2.4 writers use plain sizes: fall back if the synchsafe size doesn't land on a frame
                    if (ver == 4 && p + hdr + fsize < body.Length && !LooksLikeFrame(body, p + hdr + fsize))
                    {
                        int plain = BE32(body, p + 4);
                        if (p + hdr + plain <= body.Length && (p + hdr + plain == body.Length || LooksLikeFrame(body, p + hdr + plain))) fsize = plain;
                    }
                }
                if (id[0] == '\0' || fsize <= 0 || p + hdr + fsize > body.Length) break;
                int o = p + hdr, n = fsize;
                byte[] fb = body;
                bool skip = false;
                if (ver == 4)
                {
                    if ((fflags & 0x000C) != 0) skip = true;                         // compressed / encrypted
                    if ((fflags & 0x0001) != 0) { o += 4; n -= 4; }                 // data length indicator
                    if ((fflags & 0x0002) != 0 && n > 0) { fb = Unsync(body, o, n); o = 0; n = fb.Length; }
                }
                else if (ver == 3)
                {
                    if ((fflags & 0x00C0) != 0) skip = true;
                    if ((fflags & 0x0020) != 0) { o += 1; n -= 1; }
                }
                if (!skip && n > 0)
                {
                    switch (id)
                    {
                        case "TIT2": case "TT2": Set(ref t.Title, Id3Text(fb, o, n)); break;
                        case "TPE1": case "TP1": Set(ref t.Artist, Id3Text(fb, o, n)); break;
                        case "TALB": case "TAL": Set(ref t.Album, Id3Text(fb, o, n)); break;
                        case "TPE2": case "TP2": Set(ref t.AlbumArtist, Id3Text(fb, o, n)); break;
                        case "TDRC": case "TYER": case "TYE": case "TDOR": { var d = Id3Text(fb, o, n); Set(ref t.Year, d); if (d.Trim().Length > 4 && t.FullDate.Length == 0) t.FullDate = d.Trim(); break; }
                        case "TCOM": case "TCM": case "TEXT": case "TXT": t.Credit("Written by", Id3Text(fb, o, n)); break;
                        case "TPUB": case "TPB": t.Credit("Label", Id3Text(fb, o, n)); break;
                        case "TDRL": t.Credit("Released", Id3Text(fb, o, n)); break;
                        case "TIPL": case "IPLS": case "IPL": case "TMCL":
                            {
                                // pairs: role, name, role, name…
                                var parts = Decode(fb, o + 1, n - 1, fb[o]).Split('\0').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                                for (int pi = 0; pi + 1 < parts.Count; pi += 2) t.Credit(TagInfo.RoleOf(parts[pi]) ?? Cap(parts[pi]), parts[pi + 1]);
                                break;
                            }
                        case "COMM": case "COM":
                            {
                                if (n < 5 || t.Comment.Length > 0) break;
                                int enc = fb[o], q = SkipZ(fb, o + 4, o + n, enc);
                                if (q < o + n) t.Comment = Decode(fb, q, o + n - q, enc).Replace("\0", "").Trim();
                                break;
                            }
                        case "TCON": case "TCO": Set(ref t.Genre, Id3Text(fb, o, n)); break;
                        case "TRCK": case "TRK": Set(ref t.Track, Id3Text(fb, o, n)); break;
                        case "TLEN":
                            if (t.Duration <= 0 && double.TryParse(Id3Text(fb, o, n), out double ms) && ms > 0) t.Duration = ms / 1000.0;
                            break;
                        case "USLT":
                        case "ULT":
                            {
                                if (n < 5) break;
                                int enc = fb[o], q = SkipZ(fb, o + 4, o + n, enc);     // language, then a description
                                if (q < o + n && t.LyricsText.Length == 0) t.LyricsText = Decode(fb, q, o + n - q, enc).Replace("\0", "").Trim();
                                break;
                            }
                        case "SYLT":
                            {
                                if (n < 7 || t.Synced != null) break;
                                int enc = fb[o], fmt = fb[o + 4], end = o + n;
                                int q = SkipZ(fb, o + 6, end, enc);                    // description
                                var lines = new List<(double, string)>();
                                int zl = (enc == 1 || enc == 2) ? 2 : 1;
                                while (q < end)
                                {
                                    int z = SkipZ(fb, q, end, enc);
                                    if (z + 4 > end) break;
                                    int tl = z - q - zl;
                                    string text = tl > 0 ? Decode(fb, q, tl, enc) : "";
                                    long stamp = UBE32(fb, z);
                                    if (fmt == 2) lines.Add((stamp / 1000.0, text.Replace("\r", "").Trim('\n', ' ', '\0')));
                                    q = z + 4;
                                }
                                if (lines.Count > 0) { lines.Sort((a, b2) => a.Item1.CompareTo(b2.Item1)); t.Synced = lines; }
                                break;
                            }
                        case "TXXX":
                        case "TXX":
                            {
                                if (n < 2) break;
                                int enc = fb[o], zl = (enc == 1 || enc == 2) ? 2 : 1;
                                int q = SkipZ(fb, o + 1, o + n, enc);
                                string desc = Decode(fb, o + 1, Math.Max(0, q - (o + 1) - zl), enc).Trim('\0', ' ');
                                string val = q < o + n ? Decode(fb, q, o + n - q, enc).Trim('\0', ' ') : "";
                                if (desc.Equals("AURA_WORDS", StringComparison.OrdinalIgnoreCase)) t.WordsRaw = val;
                                else if (TagInfo.RoleOf(desc) is string role) t.Credit(role, val);
                                else if ((desc.Equals("LYRICS", StringComparison.OrdinalIgnoreCase) || desc.Equals("UNSYNCEDLYRICS", StringComparison.OrdinalIgnoreCase)) && t.LyricsText.Length == 0) t.LyricsText = val;
                                break;
                            }
                        case "APIC":
                        case "PIC":
                            {
                                int enc = fb[o], q = o + 1, end = o + n;
                                if (id == "APIC") q = SkipZ(fb, q, end, 0); else q += 3;      // mime / image format
                                if (q >= end) break;
                                int type = fb[q++];
                                q = SkipZ(fb, q, end, enc);                                      // description
                                if (q >= end) break;
                                t.HasPicture = true;
                                int rank = type == 3 ? 0 : type == 0 ? 1 : 2;
                                if (wantPic && rank < bestType) { bestType = rank; bestPic = new byte[end - q]; Array.Copy(fb, q, bestPic, 0, end - q); }
                                break;
                            }
                    }
                }
                p += hdr + fsize;
            }
            if (bestPic != null) t.Picture = bestPic;
            return total;
        }

        static bool LooksLikeFrame(byte[] b, int p)
        {
            if (p + 4 > b.Length) return false;
            if (b[p] == 0) return true;   // padding
            for (int i = 0; i < 4; i++) { char c = (char)b[p + i]; if (!((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))) return false; }
            return true;
        }

        static void Id3v1(Stream f, TagInfo t)
        {
            if (f.Length < 128) return;
            f.Position = f.Length - 128;
            var b = ReadN(f, 128);
            if (Ascii(b, 0, 3) != "TAG") return;
            Set(ref t.Title, Latin1(b, 3, 30));
            Set(ref t.Artist, Latin1(b, 33, 30));
            Set(ref t.Album, Latin1(b, 63, 30));
            Set(ref t.Year, Latin1(b, 93, 4));
            if (b[125] == 0 && b[126] != 0) Set(ref t.Track, b[126].ToString());
            if (b[127] < Genres.Length) Set(ref t.Genre, Genres[b[127]]);
        }

        // ---------- MP3 ----------
        static readonly int[,] BitratesV1 = { { 0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448 }, { 0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384 }, { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320 } };
        static readonly int[,] BitratesV2 = { { 0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256 }, { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160 }, { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160 } };
        static readonly int[,] Rates = { { 11025, 12000, 8000 }, { 0, 0, 0 }, { 22050, 24000, 16000 }, { 44100, 48000, 32000 } };

        struct MpegFrame { public int Version, Layer, Bitrate, Rate, Length, Samples; public bool Mono; }

        static bool ParseFrame(byte[] b, int p, out MpegFrame fr)
        {
            fr = default;
            if (p + 4 > b.Length || b[p] != 0xFF || (b[p + 1] & 0xE0) != 0xE0) return false;
            int ver = (b[p + 1] >> 3) & 3, layer = (b[p + 1] >> 1) & 3;
            int bri = (b[p + 2] >> 4) & 15, sri = (b[p + 2] >> 2) & 3, pad = (b[p + 2] >> 1) & 1;
            if (ver == 1 || layer == 0 || bri == 0 || bri == 15 || sri == 3) return false;
            int L = 4 - layer;   // 1, 2 or 3
            int br = (ver == 3 ? BitratesV1[L - 1, bri] : BitratesV2[L - 1, bri]) * 1000;
            int sr = Rates[ver, sri];
            if (br <= 0 || sr <= 0) return false;
            int spf = L == 1 ? 384 : (L == 3 && ver != 3) ? 576 : 1152;
            int len = L == 1 ? (12 * br / sr + pad) * 4 : (spf / 8) * br / sr + pad;
            fr = new MpegFrame { Version = ver, Layer = L, Bitrate = br, Rate = sr, Length = len, Samples = spf, Mono = ((b[p + 3] >> 6) & 3) == 3 };
            return len > 0;
        }

        static void Mp3(Stream f, TagInfo t, bool wantPic)
        {
            long audioStart = Id3v2(f, t, wantPic);
            // some files carry a second (or padded) tag; skip zero padding
            f.Position = audioStart;
            var buf = ReadN(f, Math.Min(256 * 1024, Math.Max(0, f.Length - audioStart)));
            int p = 0;
            while (p < buf.Length && buf[p] == 0) p++;
            if (p + 3 < buf.Length && Ascii(buf, p, 3) == "ID3")
            {
                f.Position = audioStart + p;
                long more = Id3v2(f, t, wantPic);
                audioStart += p + more;
                f.Position = audioStart;
                buf = ReadN(f, Math.Min(256 * 1024, Math.Max(0, f.Length - audioStart)));
                p = 0;
            }
            bool hasV1 = false;
            if (f.Length >= 128)
            {
                var save = f.Position;
                f.Position = f.Length - 128;
                var v1 = ReadN(f, 3);
                hasV1 = Ascii(v1, 0, 3) == "TAG";
                if (t.Title.Length == 0 || t.Artist.Length == 0) Id3v1(f, t);
                f.Position = save;
            }
            for (; p + 4 <= buf.Length; p++)
            {
                if (!ParseFrame(buf, p, out var fr)) continue;
                // make sure the next frame lines up, so a stray 0xFF in the data isn't taken for audio
                int q = p + fr.Length;
                if (q + 4 <= buf.Length && !ParseFrame(buf, q, out _)) continue;
                double dur = 0;
                int side = fr.Version == 3 ? (fr.Mono ? 17 : 32) : (fr.Mono ? 9 : 17);
                int x = p + 4 + side;
                if (x + 12 <= buf.Length && (Ascii(buf, x, 4) == "Xing" || Ascii(buf, x, 4) == "Info"))
                {
                    int flags = BE32(buf, x + 4);
                    if ((flags & 1) != 0) { long frames = UBE32(buf, x + 8); if (frames > 0) dur = frames * (double)fr.Samples / fr.Rate; }
                }
                int v = p + 4 + 32;
                if (dur <= 0 && v + 18 <= buf.Length && Ascii(buf, v, 4) == "VBRI")
                {
                    long frames = UBE32(buf, v + 14);
                    if (frames > 0) dur = frames * (double)fr.Samples / fr.Rate;
                }
                if (dur <= 0)
                {
                    long bytes = f.Length - (audioStart + p) - (hasV1 ? 128 : 0);
                    dur = bytes * 8.0 / fr.Bitrate;
                }
                if (t.Duration <= 0 || Math.Abs(t.Duration - dur) > 5) t.Duration = dur;   // trust the audio over a TLEN tag
                break;
            }
        }

        // ---------- Vorbis comments (FLAC, Ogg) ----------
        static void VorbisComments(byte[] b, int o, int end, TagInfo t, bool wantPic)
        {
            if (o + 4 > end) return;
            int vlen = LE32(b, o); o += 4 + vlen;
            if (vlen < 0 || o + 4 > end) return;
            int count = LE32(b, o); o += 4;
            for (int i = 0; i < count && o + 4 <= end; i++)
            {
                int len = LE32(b, o); o += 4;
                if (len < 0 || o + len > end) break;
                int eq = Array.IndexOf(b, (byte)'=', o, len);
                if (eq > o)
                {
                    string key = Encoding.ASCII.GetString(b, o, eq - o).ToUpperInvariant();
                    int vo = eq + 1, vn = o + len - vo;
                    if (key == "METADATA_BLOCK_PICTURE" || key == "COVERART")
                    {
                        t.HasPicture = true;
                        if (wantPic && t.Picture == null)
                        {
                            try
                            {
                                var raw = Convert.FromBase64String(Encoding.ASCII.GetString(b, vo, vn));
                                if (key == "COVERART") t.Picture = raw;
                                else FlacPicture(raw, 0, raw.Length, t, true);
                            }
                            catch { }
                        }
                    }
                    else
                    {
                        string val = Encoding.UTF8.GetString(b, vo, vn);
                        switch (key)
                        {
                            case "TITLE": Set(ref t.Title, val); break;
                            case "ARTIST": Add(ref t.Artist, val); break;
                            case "ALBUM": Set(ref t.Album, val); break;
                            case "ALBUMARTIST": case "ALBUM ARTIST": Set(ref t.AlbumArtist, val); break;
                            case "DATE": case "YEAR": Set(ref t.Year, val); if (val.Trim().Length > 4 && t.FullDate.Length == 0) t.FullDate = val.Trim(); break;
                            case "GENRE": Set(ref t.Genre, val); break;
                            case "TRACKNUMBER": Set(ref t.Track, val); break;
                            case "LYRICS": case "UNSYNCEDLYRICS": case "SYNCEDLYRICS": case "UNSYNCED LYRICS":
                                if (t.LyricsText.Length == 0 || (key == "SYNCEDLYRICS" && !Lrc.IsTimed(t.LyricsText))) t.LyricsText = val.Trim();
                                break;
                            case "AURA_WORDS": t.WordsRaw = val.Trim(); break;
                            case "COMMENT": case "DESCRIPTION": if (t.Comment.Length == 0) t.Comment = val.Trim(); break;
                            default: if (TagInfo.RoleOf(key) is string role) t.Credit(role, val); break;
                        }
                    }
                }
                o += len;
            }
        }

        static void FlacPicture(byte[] b, int o, int end, TagInfo t, bool wantPic)
        {
            t.HasPicture = true;
            if (!wantPic || t.Picture != null || o + 8 > end) return;
            int type = BE32(b, o); o += 4;
            int ml = BE32(b, o); o += 4 + ml;
            if (ml < 0 || o + 4 > end) return;
            int dl = BE32(b, o); o += 4 + dl;
            if (dl < 0 || o + 20 > end) return;
            o += 16;
            int len = BE32(b, o); o += 4;
            if (len <= 0 || o + len > end) return;
            var pic = new byte[len];
            Array.Copy(b, o, pic, 0, len);
            if (t.Picture == null || type == 3) t.Picture = pic;
        }

        // ---------- FLAC ----------
        static void Flac(Stream f, TagInfo t, bool wantPic)
        {
            var magic = ReadN(f, 4);
            if (Ascii(magic, 0, 4) != "fLaC") return;
            while (true)
            {
                var h = ReadN(f, 4);
                if (h.Length < 4) return;
                bool last = (h[0] & 0x80) != 0;
                int type = h[0] & 0x7F, len = BE24(h, 1);
                if (type == 0 && len >= 18)
                {
                    var b = ReadN(f, len);
                    int rate = (b[10] << 12) | (b[11] << 4) | (b[12] >> 4);
                    long total = ((long)(b[13] & 0x0F) << 32) | ((long)b[14] << 24) | ((long)b[15] << 16) | ((long)b[16] << 8) | b[17];
                    if (rate > 0 && total > 0) t.Duration = total / (double)rate;
                }
                else if (type == 4)
                {
                    var b = ReadN(f, len);
                    VorbisComments(b, 0, b.Length, t, wantPic);
                }
                else if (type == 6)
                {
                    t.HasPicture = true;
                    if (wantPic && t.Picture == null) { var b = ReadN(f, len); FlacPicture(b, 0, b.Length, t, true); }
                    else f.Position += len;
                }
                else f.Position += len;
                if (last || type == 127) return;
            }
        }

        // ---------- Ogg (Vorbis, Opus) ----------
        static void Ogg(Stream f, TagInfo t, bool wantPic)
        {
            var packets = new List<byte[]>();
            var cur = new MemoryStream();
            int serial = 0; bool first = true;
            long readLimit = 32L * 1024 * 1024;
            while (packets.Count < 2 && f.Position < f.Length && f.Position < readLimit)
            {
                var h = ReadN(f, 27);
                if (h.Length < 27 || Ascii(h, 0, 4) != "OggS") break;
                int s = LE32(h, 14), nseg = h[26];
                var seg = ReadN(f, nseg);
                int total = 0; foreach (var x in seg) total += x;
                var data = ReadN(f, total);
                if (first) { serial = s; first = false; }
                if (s != serial) continue;
                int o = 0;
                foreach (var x in seg)
                {
                    cur.Write(data, o, Math.Min(x, data.Length - o));
                    o += x;
                    if (x < 255) { packets.Add(cur.ToArray()); cur = new MemoryStream(); if (packets.Count >= 2) break; }
                }
            }
            if (packets.Count == 0) return;
            var id = packets[0];
            int rate = 0, preskip = 0;
            bool opus = id.Length >= 19 && Ascii(id, 0, 8) == "OpusHead";
            if (opus) { rate = 48000; preskip = LE16(id, 10); }
            else if (id.Length >= 16 && id[0] == 1 && Ascii(id, 1, 6) == "vorbis") rate = LE32(id, 12);
            if (packets.Count > 1)
            {
                var c = packets[1];
                if (opus && c.Length >= 8 && Ascii(c, 0, 8) == "OpusTags") VorbisComments(c, 8, c.Length, t, wantPic);
                else if (c.Length >= 7 && c[0] == 3 && Ascii(c, 1, 6) == "vorbis") VorbisComments(c, 7, c.Length, t, wantPic);
            }
            // length: the last page's granule position
            if (rate > 0)
            {
                long back = Math.Min(f.Length, 128 * 1024);
                f.Position = f.Length - back;
                var tail = ReadN(f, back);
                for (int i = tail.Length - 27; i >= 0; i--)
                {
                    if (tail[i] == 'O' && tail[i + 1] == 'g' && tail[i + 2] == 'g' && tail[i + 3] == 'S' && LE32(tail, i + 14) == serial)
                    {
                        long gran = (long)((ulong)(uint)LE32(tail, i + 6) | ((ulong)(uint)LE32(tail, i + 10) << 32));
                        if (gran > 0) t.Duration = Math.Max(0, gran - preskip) / (double)rate;
                        break;
                    }
                }
            }
        }

        // ---------- MP4 / M4A ----------
        static void Mp4(Stream f, TagInfo t, bool wantPic)
        {
            Atoms(f, 0, f.Length, t, wantPic, 0, "");
        }

        static void Atoms(Stream f, long start, long end, TagInfo t, bool wantPic, int depth, string parent)
        {
            long p = start;
            while (p + 8 <= end && depth < 8)
            {
                f.Position = p;
                var h = ReadN(f, 8);
                if (h.Length < 8) return;
                long size = UBE32(h, 0);
                string type = Latin1(h, 4, 4);
                int hdr = 8;
                if (size == 1) { var x = ReadN(f, 8); size = ((long)UBE32(x, 0) << 32) | UBE32(x, 4); hdr = 16; }
                else if (size == 0) size = end - p;
                if (size < hdr || p + size > end) return;
                long body = p + hdr, bodyEnd = p + size;
                switch (type)
                {
                    case "moov": case "udta": case "ilst": case "trak": case "mdia":
                        Atoms(f, body, bodyEnd, t, wantPic, depth + 1, type);
                        break;
                    case "meta":
                        {
                            // usually a "full box" (4 bytes of version/flags first); QuickTime files sometimes aren't
                            f.Position = body;
                            var peek = ReadN(f, 8);
                            long inner = peek.Length == 8 && Latin1(peek, 4, 4) == "hdlr" ? body : body + 4;
                            Atoms(f, inner, bodyEnd, t, wantPic, depth + 1, type);
                            break;
                        }
                    case "mvhd":
                        {
                            var b = ReadN(f, (int)Math.Min(32, size - hdr));
                            if (b.Length >= 20)
                            {
                                if (b[0] == 1 && b.Length >= 32)
                                {
                                    long ts = UBE32(b, 20); long dur = ((long)UBE32(b, 24) << 32) | UBE32(b, 28);
                                    if (ts > 0 && dur > 0) t.Duration = dur / (double)ts;
                                }
                                else
                                {
                                    long ts = UBE32(b, 12), dur = UBE32(b, 16);
                                    if (ts > 0 && dur > 0 && dur != 0xFFFFFFFF) t.Duration = dur / (double)ts;
                                }
                            }
                            break;
                        }
                    default:
                        if (parent == "ilst") IlstItem(f, type, body, bodyEnd, t, wantPic);
                        break;
                }
                p += size;
            }
        }

        static void IlstItem(Stream f, string type, long start, long end, TagInfo t, bool wantPic)
        {
            long p = start;
            if (type == "----")
            {
                // freeform item: mean ("com.apple.iTunes"), name ("AURA_WORDS"), data
                string name = "";
                while (p + 12 <= end)
                {
                    f.Position = p;
                    var h = ReadN(f, 8);
                    long size = UBE32(h, 0);
                    if (size < 12 || p + size > end || size > 4 * 1024 * 1024) return;
                    string kind = Latin1(h, 4, 4);
                    if (kind == "name") { var b = ReadN(f, size - 8); if (b.Length > 4) name = Encoding.UTF8.GetString(b, 4, b.Length - 4); }
                    else if (kind == "data" && size >= 16 && name == "AURA_WORDS") { ReadN(f, 8); t.WordsRaw = Encoding.UTF8.GetString(ReadN(f, size - 16)).Trim(); }
                    p += size;
                }
                return;
            }
            while (p + 16 <= end)
            {
                f.Position = p;
                var h = ReadN(f, 16);
                long size = UBE32(h, 0);
                if (size < 16 || p + size > end) return;
                if (Latin1(h, 4, 4) == "data")
                {
                    int kind = BE32(h, 8) & 0xFFFFFF;
                    long n = size - 16;
                    if (type == "covr")
                    {
                        t.HasPicture = true;
                        if (wantPic && t.Picture == null) t.Picture = ReadN(f, n);
                    }
                    else if (n > 0 && n < 1024 * 1024)
                    {
                        var b = ReadN(f, n);
                        if (type == "trkn" && b.Length >= 4) Set(ref t.Track, BE16(b, 2).ToString());
                        else if (type == "gnre" && b.Length >= 2) { int g = BE16(b, 0) - 1; if (g >= 0 && g < Genres.Length) Set(ref t.Genre, Genres[g]); }
                        else if (kind == 1)
                        {
                            string s = Encoding.UTF8.GetString(b);
                            switch (type)
                            {
                                case "©nam": Set(ref t.Title, s); break;
                                case "©ART": Set(ref t.Artist, s); break;
                                case "©alb": Set(ref t.Album, s); break;
                                case "aART": Set(ref t.AlbumArtist, s); break;
                                case "©day": Set(ref t.Year, s); break;
                                case "©gen": Set(ref t.Genre, s); break;
                                case "©lyr": if (t.LyricsText.Length == 0) t.LyricsText = s.Trim(); break;
                                case "©wrt": t.Credit("Written by", s); break;
                                case "©cmt": case "desc": if (t.Comment.Length == 0) t.Comment = s.Trim(); break;
                            }
                        }
                    }
                    return;
                }
                p += size;
            }
        }

        // ---------- WAV / AIFF ----------
        static void Wav(Stream f, TagInfo t, bool wantPic)
        {
            f.Position = 12;
            int byteRate = 0; long dataSize = 0;
            var info = new TagInfo();
            while (f.Position + 8 <= f.Length)
            {
                var h = ReadN(f, 8);
                if (h.Length < 8) break;
                string id = Ascii(h, 0, 4);
                long len = (uint)LE32(h, 4);
                long next = f.Position + len + (len & 1);
                if (id == "fmt " && len >= 16) { var b = ReadN(f, 16); byteRate = LE32(b, 8); }
                else if (id == "data") { dataSize = len == 0xFFFFFFFF || f.Position + len > f.Length ? f.Length - f.Position : len; }
                else if (id == "id3 " || id == "ID3 ") Id3v2(f, t, wantPic);
                else if (id == "LIST" && len >= 4 && len < 1024 * 1024)
                {
                    var b = ReadN(f, len);
                    if (Ascii(b, 0, 4) == "INFO")
                    {
                        int o = 4;
                        while (o + 8 <= b.Length)
                        {
                            string k = Ascii(b, o, 4); int n = LE32(b, o + 4);
                            if (n < 0 || o + 8 + n > b.Length) break;
                            string v = Encoding.UTF8.GetString(b, o + 8, n);
                            switch (k)
                            {
                                case "INAM": Set(ref info.Title, v); break;
                                case "IART": Set(ref info.Artist, v); break;
                                case "IPRD": Set(ref info.Album, v); break;
                                case "ICRD": Set(ref info.Year, v); break;
                                case "IGNR": Set(ref info.Genre, v); break;
                                case "ITRK": case "IPRT": Set(ref info.Track, v); break;
                            }
                            o += 8 + n + (n & 1);
                        }
                    }
                }
                if (next <= f.Position - 8) break;
                f.Position = next;
            }
            if (byteRate > 0 && dataSize > 0) t.Duration = dataSize / (double)byteRate;
            Set(ref t.Title, info.Title); Set(ref t.Artist, info.Artist); Set(ref t.Album, info.Album);
            Set(ref t.Year, info.Year); Set(ref t.Genre, info.Genre); Set(ref t.Track, info.Track);
        }

        static double Ext80(byte[] b, int o)
        {
            int exp = ((b[o] & 0x7F) << 8) | b[o + 1];
            ulong mant = 0;
            for (int i = 0; i < 8; i++) mant = (mant << 8) | b[o + 2 + i];
            if (exp == 0 && mant == 0) return 0;
            double v = mant * Math.Pow(2, exp - 16383 - 63);
            return (b[o] & 0x80) != 0 ? -v : v;
        }

        static void Aiff(Stream f, TagInfo t, bool wantPic)
        {
            f.Position = 12;
            string name = "", auth = "";
            while (f.Position + 8 <= f.Length)
            {
                var h = ReadN(f, 8);
                if (h.Length < 8) break;
                string id = Ascii(h, 0, 4);
                long len = UBE32(h, 4);
                long next = f.Position + len + (len & 1);
                if (id == "COMM" && len >= 18)
                {
                    var b = ReadN(f, 18);
                    long frames = UBE32(b, 2);
                    double rate = Ext80(b, 8);
                    if (rate > 0) t.Duration = frames / rate;
                }
                else if (id == "ID3 " || id == "id3 ") Id3v2(f, t, wantPic);
                else if (id == "NAME" && len < 4096) name = Encoding.UTF8.GetString(ReadN(f, len));
                else if (id == "AUTH" && len < 4096) auth = Encoding.UTF8.GetString(ReadN(f, len));
                if (next <= f.Position - 8) break;
                f.Position = next;
            }
            Set(ref t.Title, name);       // the ID3 tag wins over these older fields
            Set(ref t.Artist, auth);
        }
    }
}
