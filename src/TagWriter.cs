using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Aura
{
    // What to change in a song's tags. null = leave as is; "" = clear it.
    sealed class TagEdit
    {
        public string Title, Artist, Album, AlbumArtist, Year, Genre, Track;
        public byte[] Picture;          // new front cover (JPEG or PNG)
        public bool RemovePicture;
        public bool Any => Title != null || Artist != null || Album != null || AlbumArtist != null || Year != null || Genre != null || Track != null || Picture != null || RemovePicture;
    }

    // Writes tags into MP3, FLAC, Ogg Vorbis/Opus, M4A, WAV and AIFF files.
    // Always writes a new copy next to the song, reads it back to check it, and only then
    // swaps it in — so a failed write never damages the original.
    static class TagWriter
    {
        public static readonly string[] Writable = { ".mp3", ".flac", ".ogg", ".oga", ".opus", ".m4a", ".mp4", ".wav", ".aif", ".aiff" };
        public static bool CanWrite(string path) => Writable.Contains(Path.GetExtension(path).ToLowerInvariant());

        public static void Write(string path, TagEdit e)
        {
            if (!e.Any) return;
            if (!CanWrite(path)) throw new NotSupportedException("AURA can't change the tags of " + Path.GetExtension(path) + " files yet");
            var before = Tags.Read(path);
            byte[] data = File.ReadAllBytes(FileX.Long(path));
            byte[] outp;
            string head = data.Length >= 12 ? Encoding.ASCII.GetString(data, 0, 4) : "";
            string kind = data.Length >= 12 ? Encoding.ASCII.GetString(data, 8, 4) : "";
            if (head == "fLaC" || (head.StartsWith("ID3") && Path.GetExtension(path).ToLowerInvariant() == ".flac")) outp = Flac(data, e);
            else if (head == "OggS") outp = Ogg(data, e);
            else if (head == "RIFF" && kind == "WAVE") outp = Riff(data, e, false);
            else if (head == "FORM" && (kind == "AIFF" || kind == "AIFC")) outp = Riff(data, e, true);
            else if (data.Length >= 8 && Encoding.ASCII.GetString(data, 4, 4) == "ftyp") outp = Mp4(data, e);
            else outp = Mp3(data, e);

            var tmp = path + ".auratmp";
            File.WriteAllBytes(FileX.Long(tmp), outp);
            try
            {
                var after = Tags.Read(tmp);
                string Want(string v, string old) => v ?? old;
                void Check(string what, string want, string got)
                {
                    if (!string.Equals((want ?? "").Trim(), (got ?? "").Trim(), StringComparison.Ordinal))
                        throw new IOException("the " + what + " didn't save correctly (" + got + ")");
                }
                Check("title", Want(e.Title, before.Title), after.Title);
                Check("artist", Want(e.Artist, before.Artist), after.Artist);
                Check("album", Want(e.Album, before.Album), after.Album);
                if (e.Track != null) Check("track number", Clean(e.Track), after.Track);
                if (before.Duration > 1 && Math.Abs(after.Duration - before.Duration) > 0.6)
                    throw new IOException("the song's length changed (" + before.Duration.ToString("0.0") + " → " + after.Duration.ToString("0.0") + " s)");
                if (e.Picture != null && !after.HasPicture) throw new IOException("the cover didn't save");
                if (e.RemovePicture && e.Picture == null && after.HasPicture) throw new IOException("the cover is still there");
                if (before.HasLyrics && !after.HasLyrics) throw new IOException("the lyrics went missing");
                Replace(tmp, path);
            }
            catch
            {
                try { File.Delete(FileX.Long(tmp)); } catch { }
                throw;
            }
        }

        static void Replace(string tmp, string path)
        {
            var lp = FileX.Long(path); var lt = FileX.Long(tmp);
            try { File.Replace(lt, lp, null, true); return; } catch (PlatformNotSupportedException) { } catch (IOException) when (!App.IsWindows) { }
            File.Copy(lt, lp, true);
            File.Delete(lt);
        }

        static string Clean(string track)
        {
            track = (track ?? "").Trim();
            int slash = track.IndexOf('/');
            if (slash >= 0) track = track.Substring(0, slash);
            return int.TryParse(track, out int n) && n > 0 ? n.ToString() : "";
        }

        static string Mime(byte[] pic) => pic.Length > 4 && pic[0] == 0x89 && pic[1] == 'P' ? "image/png" : "image/jpeg";

        // ---------- byte helpers ----------
        static int BE32(byte[] b, int o) => (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];
        static uint UBE32(byte[] b, int o) => (uint)BE32(b, o);
        static int LE32(byte[] b, int o) => b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24);
        static int Sync32(byte[] b, int o) => ((b[o] & 0x7F) << 21) | ((b[o + 1] & 0x7F) << 14) | ((b[o + 2] & 0x7F) << 7) | (b[o + 3] & 0x7F);
        static void PutBE32(List<byte> l, long v) { l.Add((byte)(v >> 24)); l.Add((byte)(v >> 16)); l.Add((byte)(v >> 8)); l.Add((byte)v); }
        static void PutLE32(List<byte> l, long v) { l.Add((byte)v); l.Add((byte)(v >> 8)); l.Add((byte)(v >> 16)); l.Add((byte)(v >> 24)); }
        static void SetBE32(byte[] b, int o, long v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
        static void SetLE32(byte[] b, int o, long v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24); }
        static byte[] Sync(int v) => new[] { (byte)((v >> 21) & 0x7F), (byte)((v >> 14) & 0x7F), (byte)((v >> 7) & 0x7F), (byte)(v & 0x7F) };
        static byte[] Slice(byte[] b, int o, int n) { var r = new byte[n]; Array.Copy(b, o, r, 0, n); return r; }
        static string Ascii(byte[] b, int o, int n) => o + n <= b.Length ? Encoding.ASCII.GetString(b, o, n) : "";

        // ================= ID3v2 (MP3, and inside WAV/AIFF) =================
        sealed class Frame { public string Id; public int Flags; public byte[] Data; }

        static List<Frame> ParseId3(byte[] tag, out int version)
        {
            var frames = new List<Frame>();
            version = tag[3];
            int flags = tag[5], size = Sync32(tag, 6);
            var body = Slice(tag, 10, Math.Min(size, tag.Length - 10));
            if (version < 4 && (flags & 0x80) != 0)
            {
                var l = new List<byte>(body.Length);
                for (int i = 0; i < body.Length; i++) { l.Add(body[i]); if (body[i] == 0xFF && i + 1 < body.Length && body[i + 1] == 0) i++; }
                body = l.ToArray();
            }
            int p = 0;
            if ((flags & 0x40) != 0 && version >= 3 && body.Length >= 4) p = version == 3 ? 4 + BE32(body, 0) : Sync32(body, 0);
            while (p + (version == 2 ? 6 : 10) <= body.Length)
            {
                if (version == 2)
                {
                    string id = Ascii(body, p, 3);
                    int n = (body[p + 3] << 16) | (body[p + 4] << 8) | body[p + 5];
                    if (body[p] == 0 || n <= 0 || p + 6 + n > body.Length) break;
                    var map = new Dictionary<string, string> { { "TT2", "TIT2" }, { "TP1", "TPE1" }, { "TAL", "TALB" }, { "TP2", "TPE2" }, { "TYE", "TYER" }, { "TCO", "TCON" }, { "TRK", "TRCK" }, { "ULT", "USLT" }, { "COM", "COMM" }, { "TXX", "TXXX" } };
                    var d = Slice(body, p + 6, n);
                    if (map.TryGetValue(id, out var nid)) frames.Add(new Frame { Id = nid, Data = d });
                    else if (id == "PIC" && d.Length > 6)
                    {
                        // PIC has a 3-letter image type where APIC has a mime type
                        string mime = Ascii(d, 1, 3).ToUpperInvariant() == "PNG" ? "image/png" : "image/jpeg";
                        var l = new List<byte> { d[0] }; l.AddRange(Encoding.ASCII.GetBytes(mime)); l.Add(0); l.AddRange(d.Skip(4));
                        frames.Add(new Frame { Id = "APIC", Data = l.ToArray() });
                    }
                    p += 6 + n;
                }
                else
                {
                    string id = Ascii(body, p, 4);
                    int n = version == 4 ? Sync32(body, p + 4) : BE32(body, p + 4);
                    if (version == 4 && p + 10 + n < body.Length && !LooksLikeFrame(body, p + 10 + n))
                    {
                        int plain = BE32(body, p + 4);
                        if (p + 10 + plain <= body.Length && (p + 10 + plain == body.Length || LooksLikeFrame(body, p + 10 + plain))) n = plain;
                    }
                    if (body[p] == 0 || n <= 0 || p + 10 + n > body.Length) break;
                    frames.Add(new Frame { Id = id, Flags = (body[p + 8] << 8) | body[p + 9], Data = Slice(body, p + 10, n) });
                    p += 10 + n;
                }
            }
            if (version == 2) version = 3;
            return frames;
        }

        static bool LooksLikeFrame(byte[] b, int p)
        {
            if (p + 4 > b.Length) return false;
            if (b[p] == 0) return true;
            for (int i = 0; i < 4; i++) { char c = (char)b[p + i]; if (!((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))) return false; }
            return true;
        }

        static Frame Text(string id, string value, int version)
        {
            var l = new List<byte>();
            if (version == 4) { l.Add(3); l.AddRange(Encoding.UTF8.GetBytes(value)); }
            else { l.Add(1); l.Add(0xFF); l.Add(0xFE); l.AddRange(Encoding.Unicode.GetBytes(value)); }
            return new Frame { Id = id, Data = l.ToArray() };
        }

        static byte[] BuildId3(byte[] oldTag, TagEdit e, int padding = 1024)
        {
            int version = 3;
            var frames = oldTag != null && oldTag.Length >= 10 ? ParseId3(oldTag, out version) : new List<Frame>();
            if (version != 3 && version != 4) version = 3;
            void SetText(string id, string v, params string[] also)
            {
                if (v == null) return;
                frames.RemoveAll(f => f.Id == id || also.Contains(f.Id));
                if (v.Trim().Length > 0) frames.Add(Text(id, v.Trim(), version));
            }
            SetText("TIT2", e.Title);
            SetText("TPE1", e.Artist);
            SetText("TALB", e.Album);
            SetText("TPE2", e.AlbumArtist);
            SetText(version == 4 ? "TDRC" : "TYER", e.Year, "TYER", "TDRC", "TDAT", "TIME");
            SetText("TCON", e.Genre);
            if (e.Track != null) SetText("TRCK", Clean(e.Track));
            if (e.Picture != null || e.RemovePicture)
            {
                frames.RemoveAll(f => f.Id == "APIC");
                if (e.Picture != null)
                {
                    var l = new List<byte> { 0 };
                    l.AddRange(Encoding.ASCII.GetBytes(Mime(e.Picture))); l.Add(0);
                    l.Add(3);      // front cover
                    l.Add(0);      // no description
                    l.AddRange(e.Picture);
                    frames.Add(new Frame { Id = "APIC", Data = l.ToArray() });
                }
            }
            var body = new List<byte>();
            foreach (var f in frames)
            {
                body.AddRange(Encoding.ASCII.GetBytes(f.Id));
                if (version == 4) body.AddRange(Sync(f.Data.Length)); else PutBE32(body, f.Data.Length);
                body.Add((byte)(f.Flags >> 8)); body.Add((byte)f.Flags);
                body.AddRange(f.Data);
            }
            body.AddRange(new byte[padding]);
            var tag = new List<byte> { (byte)'I', (byte)'D', (byte)'3', (byte)version, 0, 0 };
            tag.AddRange(Sync(body.Count));
            tag.AddRange(body);
            return tag.ToArray();
        }

        static int Id3Size(byte[] d, int at)
        {
            if (at + 10 > d.Length || Ascii(d, at, 3) != "ID3") return 0;
            return 10 + Sync32(d, at + 6) + ((d[at + 5] & 0x10) != 0 ? 10 : 0);
        }

        static byte[] Mp3(byte[] d, TagEdit e)
        {
            int start = Id3Size(d, 0);
            byte[] old = start > 0 ? Slice(d, 0, Math.Min(start, d.Length)) : null;
            var tag = BuildId3(old, e);
            int end = d.Length;
            byte[] v1 = null;
            if (d.Length >= 128 && Ascii(d, d.Length - 128, 3) == "TAG")
            {
                end = d.Length - 128;
                v1 = Slice(d, end, 128);
                // keep the old one-line tag in step too
                void Put(int o, string v) { if (v == null) return; var bytes = Encoding.GetEncoding(28591).GetBytes(v); for (int i = 0; i < 30; i++) v1[o + i] = i < bytes.Length ? bytes[i] : (byte)0; }
                Put(3, e.Title); Put(33, e.Artist); Put(63, e.Album);
                if (e.Year != null) { var y = Encoding.ASCII.GetBytes((e.Year + "    ").Substring(0, 4)); Array.Copy(y, 0, v1, 93, 4); }
            }
            var outp = new byte[tag.Length + (end - start) + (v1?.Length ?? 0)];
            Array.Copy(tag, outp, tag.Length);
            Array.Copy(d, start, outp, tag.Length, end - start);
            if (v1 != null) Array.Copy(v1, 0, outp, tag.Length + end - start, 128);
            return outp;
        }

        // WAV (little-endian RIFF) and AIFF (big-endian FORM): the tag lives in an "id3 " chunk
        static byte[] Riff(byte[] d, TagEdit e, bool aiff)
        {
            int Size(int o) => aiff ? BE32(d, o) : LE32(d, o);
            var chunks = new List<(string id, int start, int len)>();
            int p = 12;
            while (p + 8 <= d.Length)
            {
                string id = Ascii(d, p, 4);
                long len = (uint)Size(p + 4);
                if (len > d.Length - p - 8) len = d.Length - p - 8;     // a broken size: take the rest
                chunks.Add((id, p, (int)len));
                p += 8 + (int)len + (int)(len & 1);
            }
            byte[] oldTag = null;
            foreach (var c in chunks) if (c.id == "id3 " || c.id == "ID3 ") { oldTag = Slice(d, c.start + 8, c.len); break; }
            var tag = BuildId3(oldTag, e, 256);
            var outp = new List<byte>(d.Length + tag.Length);
            outp.AddRange(Slice(d, 0, 12));
            foreach (var c in chunks)
            {
                if (c.id == "id3 " || c.id == "ID3 ") continue;
                if (!aiff && c.id == "LIST" && c.len >= 4 && Ascii(d, c.start + 8, 4) == "INFO")
                {
                    // the old WAV text fields: drop the ones being changed, so they can't show through
                    var drop = new HashSet<string>();
                    if (e.Title != null) drop.Add("INAM");
                    if (e.Artist != null) drop.Add("IART");
                    if (e.Album != null) drop.Add("IPRD");
                    if (e.Year != null) drop.Add("ICRD");
                    if (e.Genre != null) drop.Add("IGNR");
                    if (e.Track != null) { drop.Add("ITRK"); drop.Add("IPRT"); }
                    var info = new List<byte>(Encoding.ASCII.GetBytes("INFO"));
                    int q = c.start + 12, endq = c.start + 8 + c.len;
                    while (q + 8 <= endq)
                    {
                        string k = Ascii(d, q, 4); int n = LE32(d, q + 4);
                        if (n < 0 || q + 8 + n > endq) break;
                        if (!drop.Contains(k)) { info.AddRange(Slice(d, q, 8 + n)); if ((n & 1) != 0) info.Add(0); }
                        q += 8 + n + (n & 1);
                    }
                    if (info.Count > 4)
                    {
                        outp.AddRange(Encoding.ASCII.GetBytes("LIST")); PutLE32(outp, info.Count); outp.AddRange(info);
                        if ((info.Count & 1) != 0) outp.Add(0);
                    }
                    continue;
                }
                int total = 8 + c.len + (c.len & 1);
                outp.AddRange(Slice(d, c.start, Math.Min(total, d.Length - c.start)));
                if (c.start + total > d.Length) outp.Add(0);
            }
            outp.AddRange(Encoding.ASCII.GetBytes(aiff ? "ID3 " : "id3 "));
            if (aiff) PutBE32(outp, tag.Length); else PutLE32(outp, tag.Length);
            outp.AddRange(tag);
            if ((tag.Length & 1) != 0) outp.Add(0);
            var arr = outp.ToArray();
            if (aiff) SetBE32(arr, 4, arr.Length - 8); else SetLE32(arr, 4, arr.Length - 8);
            return arr;
        }

        // ================= Vorbis comments (FLAC, Ogg) =================
        static (string vendor, List<(string key, string value)> items) ParseComments(byte[] b, int o, int end)
        {
            var items = new List<(string, string)>();
            if (o + 4 > end) return ("AURA", items);
            int vl = LE32(b, o); o += 4;
            string vendor = vl >= 0 && o + vl <= end ? Encoding.UTF8.GetString(b, o, vl) : "AURA";
            o += Math.Max(0, vl);
            if (o + 4 > end) return (vendor, items);
            int n = LE32(b, o); o += 4;
            for (int i = 0; i < n && o + 4 <= end; i++)
            {
                int l = LE32(b, o); o += 4;
                if (l < 0 || o + l > end) break;
                var s = Encoding.UTF8.GetString(b, o, l);
                int eq = s.IndexOf('=');
                if (eq > 0) items.Add((s.Substring(0, eq), s.Substring(eq + 1)));
                o += l;
            }
            return (vendor, items);
        }

        static byte[] FlacPictureBlock(byte[] pic)
        {
            var l = new List<byte>();
            PutBE32(l, 3);
            var mime = Encoding.ASCII.GetBytes(Mime(pic));
            PutBE32(l, mime.Length); l.AddRange(mime);
            PutBE32(l, 0);                                   // no description
            PutBE32(l, 0); PutBE32(l, 0); PutBE32(l, 0); PutBE32(l, 0);   // size/depth unknown (allowed)
            PutBE32(l, pic.Length); l.AddRange(pic);
            return l.ToArray();
        }

        static List<(string key, string value)> EditComments(List<(string key, string value)> items, TagEdit e, bool pictureInComments)
        {
            var list = items.ToList();
            void Set(string v, string key, params string[] also)
            {
                if (v == null) return;
                list.RemoveAll(x => string.Equals(x.key, key, StringComparison.OrdinalIgnoreCase) || also.Any(a => string.Equals(x.key, a, StringComparison.OrdinalIgnoreCase)));
                if (v.Trim().Length > 0) list.Add((key, v.Trim()));
            }
            Set(e.Title, "TITLE");
            Set(e.Artist, "ARTIST");
            Set(e.Album, "ALBUM");
            Set(e.AlbumArtist, "ALBUMARTIST", "ALBUM ARTIST");
            Set(e.Year, "DATE", "YEAR");
            Set(e.Genre, "GENRE");
            if (e.Track != null) Set(Clean(e.Track), "TRACKNUMBER");
            if (pictureInComments && (e.Picture != null || e.RemovePicture))
            {
                list.RemoveAll(x => string.Equals(x.key, "METADATA_BLOCK_PICTURE", StringComparison.OrdinalIgnoreCase) || string.Equals(x.key, "COVERART", StringComparison.OrdinalIgnoreCase));
                if (e.Picture != null) list.Add(("METADATA_BLOCK_PICTURE", Convert.ToBase64String(FlacPictureBlock(e.Picture))));
            }
            return list;
        }

        static byte[] BuildComments(string vendor, List<(string key, string value)> items)
        {
            var l = new List<byte>();
            var v = Encoding.UTF8.GetBytes(vendor ?? "");
            PutLE32(l, v.Length); l.AddRange(v);
            PutLE32(l, items.Count);
            foreach (var (k, val) in items) { var b = Encoding.UTF8.GetBytes(k + "=" + val); PutLE32(l, b.Length); l.AddRange(b); }
            return l.ToArray();
        }

        // ================= FLAC =================
        static byte[] Flac(byte[] d, TagEdit e)
        {
            int start = Id3Size(d, 0);           // a stray ID3 tag in front is dropped
            if (Ascii(d, start, 4) != "fLaC") throw new IOException("not a FLAC file");
            int p = start + 4;
            var blocks = new List<(int type, byte[] data)>();
            while (p + 4 <= d.Length)
            {
                bool last = (d[p] & 0x80) != 0;
                int type = d[p] & 0x7F, len = (d[p + 1] << 16) | (d[p + 2] << 8) | d[p + 3];
                if (p + 4 + len > d.Length) throw new IOException("the FLAC file looks damaged");
                blocks.Add((type, Slice(d, p + 4, len)));
                p += 4 + len;
                if (last) break;
            }
            if (blocks.Count == 0 || blocks[0].type != 0) throw new IOException("the FLAC file looks damaged");
            int audio = p;
            var vc = blocks.FirstOrDefault(b => b.type == 4);
            var (vendor, items) = vc.data != null ? ParseComments(vc.data, 0, vc.data.Length) : ("reference libFLAC", new List<(string, string)>());
            items = EditComments(items, e, false);
            var keep = new List<(int type, byte[] data)> { blocks[0] };
            keep.AddRange(blocks.Skip(1).Where(b => b.type != 1 && b.type != 4 && !(b.type == 6 && (e.Picture != null || e.RemovePicture))));
            keep.Add((4, BuildComments(vendor, items)));
            if (e.Picture != null) keep.Add((6, FlacPictureBlock(e.Picture)));
            keep.Add((1, new byte[4096]));      // room to grow next time
            var outp = new List<byte>(d.Length + 8192);
            outp.AddRange(Encoding.ASCII.GetBytes("fLaC"));
            for (int i = 0; i < keep.Count; i++)
            {
                var (type, data) = keep[i];
                if (data.Length >= 1 << 24) throw new IOException("the cover is too big for a FLAC file");
                outp.Add((byte)((i == keep.Count - 1 ? 0x80 : 0) | type));
                outp.Add((byte)(data.Length >> 16)); outp.Add((byte)(data.Length >> 8)); outp.Add((byte)data.Length);
                outp.AddRange(data);
            }
            var arr = new byte[outp.Count + d.Length - audio];
            outp.CopyTo(arr);
            Array.Copy(d, audio, arr, outp.Count, d.Length - audio);
            return arr;
        }

        // ================= Ogg (Vorbis, Opus) =================
        static uint[] crcTable;
        static uint OggCrc(byte[] b, int o, int n)
        {
            if (crcTable == null)
            {
                var t = new uint[256];
                for (uint i = 0; i < 256; i++)
                {
                    uint r = i << 24;
                    for (int k = 0; k < 8; k++) r = (r & 0x80000000) != 0 ? (r << 1) ^ 0x04C11DB7 : r << 1;
                    t[i] = r;
                }
                crcTable = t;
            }
            uint crc = 0;
            for (int i = o; i < o + n; i++) crc = (crc << 8) ^ crcTable[((crc >> 24) ^ b[i]) & 0xFF];
            return crc;
        }

        static void FinishPage(byte[] page)
        {
            page[22] = page[23] = page[24] = page[25] = 0;
            uint c = OggCrc(page, 0, page.Length);
            page[22] = (byte)c; page[23] = (byte)(c >> 8); page[24] = (byte)(c >> 16); page[25] = (byte)(c >> 24);
        }

        static byte[] Ogg(byte[] d, TagEdit e)
        {
            // read pages until the header packets are complete
            int p = 0, serial = 0, headerPackets = 0;
            var packets = new List<byte[]>();
            var cur = new MemoryStream();
            bool opus = false;
            int pagesUsed = 0;
            while (p + 27 <= d.Length)
            {
                if (Ascii(d, p, 4) != "OggS") throw new IOException("the Ogg file looks damaged");
                int nseg = d[p + 26];
                int s = LE32(d, p + 14);
                if (pagesUsed == 0) serial = s;
                else if (s != serial) throw new NotSupportedException("this Ogg file has more than one stream");
                int dataStart = p + 27 + nseg, o = dataStart;
                for (int i = 0; i < nseg; i++)
                {
                    int len = d[p + 27 + i];
                    cur.Write(d, o, len); o += len;
                    if (len < 255)
                    {
                        packets.Add(cur.ToArray()); cur = new MemoryStream();
                        if (packets.Count == 1)
                        {
                            opus = packets[0].Length >= 8 && Ascii(packets[0], 0, 8) == "OpusHead";
                            headerPackets = opus ? 2 : 3;
                        }
                    }
                }
                p = o; pagesUsed++;
                if (headerPackets > 0 && packets.Count >= headerPackets)
                {
                    if (packets.Count > headerPackets || cur.Length > 0) throw new NotSupportedException("this Ogg file's layout isn't supported");
                    break;
                }
            }
            if (headerPackets == 0 || packets.Count < headerPackets) throw new IOException("the Ogg file looks damaged");
            int audioStart = p;
            // the new comment packet
            var c = packets[1];
            int skip = opus ? 8 : 7;
            if (opus ? Ascii(c, 0, 8) != "OpusTags" : !(c[0] == 3 && Ascii(c, 1, 6) == "vorbis")) throw new IOException("the Ogg file's tags look damaged");
            var (vendor, items) = ParseComments(c, skip, c.Length);
            items = EditComments(items, e, true);
            var nc = new List<byte>();
            nc.AddRange(opus ? Encoding.ASCII.GetBytes("OpusTags") : new byte[] { 3, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s' });
            nc.AddRange(BuildComments(vendor, items));
            if (!opus) nc.Add(1);          // Vorbis framing bit
            packets[1] = nc.ToArray();

            // lay the header packets out into pages again: the first packet alone, the rest after it
            var pages = new List<byte[]>();
            int seq = 0;
            void Paginate(List<byte[]> pk, bool first)
            {
                var segs = new List<(byte[] pk, int off, int len, bool endsPacket)>();
                foreach (var x in pk)
                {
                    int off = 0;
                    while (x.Length - off >= 255) { segs.Add((x, off, 255, false)); off += 255; }
                    segs.Add((x, off, x.Length - off, true));
                }
                bool continued = false;
                int i = 0;
                while (i < segs.Count)
                {
                    int n = Math.Min(255, segs.Count - i);
                    var body = new List<byte>();
                    var lace = new byte[n];
                    for (int k = 0; k < n; k++) { var sg = segs[i + k]; lace[k] = (byte)sg.len; body.AddRange(new ArraySegment<byte>(sg.pk, sg.off, sg.len)); }
                    var page = new byte[27 + n + body.Count];
                    Encoding.ASCII.GetBytes("OggS").CopyTo(page, 0);
                    page[4] = 0;
                    page[5] = (byte)((continued ? 1 : 0) | (first && seq == 0 ? 2 : 0));
                    // granule 0 for header pages
                    SetLE32(page, 14, serial);
                    SetLE32(page, 18, seq++);
                    page[26] = (byte)n;
                    lace.CopyTo(page, 27);
                    body.CopyTo(page, 27 + n);
                    FinishPage(page);
                    pages.Add(page);
                    continued = !segs[i + n - 1].endsPacket;
                    i += n;
                }
            }
            Paginate(new List<byte[]> { packets[0] }, true);
            Paginate(packets.Skip(1).ToList(), false);
            int delta = seq - pagesUsed;
            var outp = new MemoryStream(d.Length + 4096);
            foreach (var pg in pages) outp.Write(pg, 0, pg.Length);
            if (delta == 0) outp.Write(d, audioStart, d.Length - audioStart);
            else
            {
                // the rest of the pages keep their content; only their numbers (and so their checksums) change
                int q = audioStart;
                while (q + 27 <= d.Length)
                {
                    if (Ascii(d, q, 4) != "OggS") { outp.Write(d, q, d.Length - q); break; }
                    int nseg = d[q + 26], len = 27 + nseg;
                    for (int i = 0; i < nseg; i++) len += d[q + 27 + i];
                    if (q + len > d.Length) { outp.Write(d, q, d.Length - q); break; }
                    var page = Slice(d, q, len);
                    SetLE32(page, 18, LE32(page, 18) + delta);
                    FinishPage(page);
                    outp.Write(page, 0, page.Length);
                    q += len;
                }
            }
            return outp.ToArray();
        }

        // ================= MP4 / M4A =================
        sealed class Atom
        {
            public string Type; public byte[] Raw;       // Raw: whole atom when not opened up
            public List<Atom> Kids; public byte[] Head;   // Head: bytes after the 8-byte header and before the children (e.g. meta's version/flags)
            public int Size => Kids == null ? Raw.Length : 8 + (Head?.Length ?? 0) + Kids.Sum(k => k.Size);
            public void WriteTo(List<byte> l)
            {
                if (Kids == null) { l.AddRange(Raw); return; }
                PutBE32(l, Size); l.AddRange(Encoding.GetEncoding(28591).GetBytes(Type));
                if (Head != null) l.AddRange(Head);
                foreach (var k in Kids) k.WriteTo(l);
            }
        }

        static readonly HashSet<string> Containers = new HashSet<string> { "moov", "udta", "meta", "ilst", "trak", "mdia", "minf", "stbl" };

        static List<Atom> ParseAtoms(byte[] d, int start, int end, bool open)
        {
            var list = new List<Atom>();
            int p = start;
            while (p + 8 <= end)
            {
                long size = UBE32(d, p);
                string type = Encoding.GetEncoding(28591).GetString(d, p + 4, 4);
                if (size == 1) throw new NotSupportedException("this M4A file is too big for AURA to tag");
                if (size == 0) size = end - p;
                if (size < 8 || p + size > end) throw new IOException("the M4A file looks damaged");
                var a = new Atom { Type = type };
                if (open && Containers.Contains(type))
                {
                    int bodyStart = p + 8;
                    if (type == "meta")
                    {
                        // usually 4 bytes of version/flags first; QuickTime-style files go straight to the children
                        bool full = !(Ascii(d, p + 12, 4) == "hdlr");
                        if (full) { a.Head = Slice(d, p + 8, 4); bodyStart += 4; }
                    }
                    a.Kids = ParseAtoms(d, bodyStart, (int)(p + size), type != "ilst");
                }
                else a.Raw = Slice(d, p, (int)size);
                list.Add(a);
                p += (int)size;
            }
            return list;
        }

        static Atom DataItem(string type, int kind, byte[] payload)
        {
            var l = new List<byte>();
            PutBE32(l, 8 + 16 + payload.Length); l.AddRange(Encoding.GetEncoding(28591).GetBytes(type));
            PutBE32(l, 16 + payload.Length); l.AddRange(Encoding.ASCII.GetBytes("data"));
            PutBE32(l, kind); PutBE32(l, 0);
            l.AddRange(payload);
            return new Atom { Type = type, Raw = l.ToArray() };
        }

        static byte[] Mp4(byte[] d, TagEdit e)
        {
            var top = ParseAtoms(d, 0, d.Length, true);
            if (top.Any(a => a.Type == "moof")) throw new NotSupportedException("streaming-style M4A files can't be tagged yet");
            var moov = top.FirstOrDefault(a => a.Type == "moov") ?? throw new IOException("the M4A file looks damaged");
            int oldMoov = moov.Size;
            var udta = moov.Kids.FirstOrDefault(a => a.Type == "udta");
            if (udta == null) { udta = new Atom { Type = "udta", Kids = new List<Atom>() }; moov.Kids.Add(udta); }
            var meta = udta.Kids.FirstOrDefault(a => a.Type == "meta");
            if (meta == null)
            {
                var hdlr = new List<byte>();
                PutBE32(hdlr, 33); hdlr.AddRange(Encoding.ASCII.GetBytes("hdlr"));
                hdlr.AddRange(new byte[8]); hdlr.AddRange(Encoding.ASCII.GetBytes("mdirappl")); hdlr.AddRange(new byte[9]);
                meta = new Atom { Type = "meta", Head = new byte[4], Kids = new List<Atom> { new Atom { Type = "hdlr", Raw = hdlr.ToArray() } } };
                udta.Kids.Add(meta);
            }
            var ilst = meta.Kids.FirstOrDefault(a => a.Type == "ilst");
            if (ilst == null) { ilst = new Atom { Type = "ilst", Kids = new List<Atom>() }; meta.Kids.Add(ilst); }
            void SetText(string type, string v)
            {
                if (v == null) return;
                ilst.Kids.RemoveAll(a => a.Type == type);
                if (v.Trim().Length > 0) ilst.Kids.Add(DataItem(type, 1, Encoding.UTF8.GetBytes(v.Trim())));
            }
            SetText("©nam", e.Title);
            SetText("©ART", e.Artist);
            SetText("©alb", e.Album);
            SetText("aART", e.AlbumArtist);
            SetText("©day", e.Year);
            if (e.Genre != null) { ilst.Kids.RemoveAll(a => a.Type == "gnre"); SetText("©gen", e.Genre); }
            if (e.Track != null)
            {
                ilst.Kids.RemoveAll(a => a.Type == "trkn");
                var tn = Clean(e.Track);
                if (tn.Length > 0) { int n = int.Parse(tn); ilst.Kids.Add(DataItem("trkn", 0, new byte[] { 0, 0, (byte)(n >> 8), (byte)n, 0, 0, 0, 0 })); }
            }
            if (e.Picture != null || e.RemovePicture)
            {
                ilst.Kids.RemoveAll(a => a.Type == "covr");
                if (e.Picture != null) ilst.Kids.Add(DataItem("covr", Mime(e.Picture) == "image/png" ? 14 : 13, e.Picture));
            }
            int delta = moov.Size - oldMoov;
            // if the song data comes after the tags, moving it means its position tables must move too
            int moovAt = 0; foreach (var a in top) { if (a == moov) break; moovAt += a.Size; }
            if (delta != 0 && top.SkipWhile(a => a != moov).Skip(1).Any(a => a.Type == "mdat"))
                foreach (var trak in moov.Kids.Where(a => a.Type == "trak")) ShiftOffsets(trak, delta, moovAt);
            var outp = new List<byte>(d.Length + delta + 16);
            foreach (var a in top) a.WriteTo(outp);
            return outp.ToArray();
        }

        static void ShiftOffsets(Atom a, int delta, int after)
        {
            if (a.Kids != null) { foreach (var k in a.Kids) ShiftOffsets(k, delta, after); return; }
            if (a.Type == "stco")
            {
                int n = BE32(a.Raw, 12);
                for (int i = 0; i < n; i++) { long v = UBE32(a.Raw, 16 + i * 4); if (v > after) SetBE32(a.Raw, 16 + i * 4, v + delta); }
            }
            else if (a.Type == "co64")
            {
                int n = BE32(a.Raw, 12);
                for (int i = 0; i < n; i++)
                {
                    int o = 16 + i * 8;
                    long v = ((long)UBE32(a.Raw, o) << 32) | UBE32(a.Raw, o + 4);
                    if (v > after) { v += delta; SetBE32(a.Raw, o, v >> 32); SetBE32(a.Raw, o + 4, v & 0xFFFFFFFF); }
                }
            }
        }
    }
}
