using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;

namespace Aura
{
    // "Listening to … by …" on your Discord profile, through the Discord app on this PC (its local pipe).
    // Needs an Application ID from discord.com/developers (Settings explains it).
    sealed class Discord : IDisposable
    {
        readonly string appId;
        NamedPipeClientStream pipe;
        readonly object gate = new object();
        Thread worker;
        volatile bool stop;
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        public string Status = "starting";

        public Discord(string appId)
        {
            this.appId = appId;
            worker = new Thread(Run) { IsBackground = true, Name = "discord" };
            worker.Start();
        }

        static string J(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ") + "\"";

        // what's playing now (the worker turns it into a status)
        sealed class Now { public Track T; public bool Playing; public long StartMs; }
        Now pending;
        bool fresh;

        public void Update(Track t, bool playing, double pos)
        {
            var n = new Now { T = t, Playing = playing && t != null, StartMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - (long)(pos * 1000) };
            lock (gate)
            {
                // nothing Discord would show differently: don't bother it (each update makes Discord redraw)
                var p = pending;
                if (p != null && p.T == n.T && p.Playing == n.Playing && (!n.Playing || Math.Abs(p.StartMs - n.StartMs) < 2000)) return;
                pending = n; fresh = true;
            }
            wake.Set();
        }

        // the song's cover as a web link (Discord can only show pictures from the web): looked up on Apple Music once per song
        static readonly System.Collections.Generic.Dictionary<string, string> covers = new System.Collections.Generic.Dictionary<string, string>();
        static string CoverKey(Track t) => LyricsOnline.Norm(t.MainArtist) + "|" + LyricsOnline.Norm(LyricsOnline.CleanTitle(t.Name));
        // Only a picture that really is this song's: the title must match closely (no "same artist, some album"
        // guesses), and when the song has its own cover, Apple's must look like it. Otherwise: the AURA logo.
        static string CoverFor(Track t, bool lookUp)
        {
            var key = CoverKey(t);
            lock (covers) if (covers.TryGetValue(key, out var u)) return u;
            if (!lookUp) return null;
            string url = "";
            {
                try
                {
                    string title = LyricsOnline.CleanTitle(t.Name), artist = t.MainArtist;
                    ulong? own = null;
                    using (var mine = Art.LoadBig(t, 512))
                        if (mine != null)
                        {
                            own = Hash(mine);
                            // your own cover, put online so Discord can show it (only when that's turned on in Settings)
                            if (UploadOwn) { var mineUrl = Uploaded(mine, own.Value); if (mineUrl != null) { lock (covers) covers[key] = mineUrl; return mineUrl; } }
                        }
                    if (t.MainArtist.Length == 0 || t.MainArtist == "Unknown artist") { lock (covers) covers[key] = ""; return ""; }
                    var res = Net.Json<ItunesResp>(Net.Get(CoverSearch.ItunesUrl + "/search?media=music&entity=song&limit=10&term=" + Uri.EscapeDataString(artist + " " + title)))?.Results;
                    foreach (var r in res ?? new System.Collections.Generic.List<ItunesItem>())
                    {
                        if (string.IsNullOrEmpty(r.Art) || !LyricsOnline.ArtistOk(artist, r.Artist ?? "")) continue;
                        if (LyricsOnline.Sim(LyricsOnline.CleanTitle(r.Track ?? ""), title) < 0.92) continue;
                        if (own != null)
                        {
                            // the song has its own picture (a leak, a single, a custom cover): Apple's must be the same picture
                            byte[] data; try { data = Net.Bytes(r.Art); } catch { continue; }
                            using (var ms = new System.IO.MemoryStream(data)) using (var im = new System.Drawing.Bitmap(ms))
                                if (Distance(Hash(im), own.Value) > 12) continue;
                        }
                        url = r.Art.Replace("100x100bb", "512x512bb");
                        break;
                    }
                }
                catch (Exception e) { App.Log("discord cover: " + e.Message); return null; }   // try again next time
            }
            lock (covers) covers[key] = url;
            return url;
        }

        // ---------- your own covers on Discord ----------
        public static void ClearCovers() { lock (covers) covers.Clear(); }
        public static bool UploadOwn;                     // Settings → Discord status → Show my own covers
        public static Db Db;                              // remembers what's already online, so each picture goes up once
        public static string UploadUrl = Environment.GetEnvironmentVariable("AURA_COVER_UPLOAD_URL") ?? "https://catbox.moe/user/api.php";

        static string Uploaded(System.Drawing.Bitmap cover, ulong hash)
        {
            string key = hash.ToString("x16");
            try
            {
                Db?.Exec("CREATE TABLE IF NOT EXISTS discord_covers (hash TEXT PRIMARY KEY, url TEXT, ts REAL)");
                var have = Db == null ? null : Conv.S(Db.Scalar("SELECT url FROM discord_covers WHERE hash=?", key));
                if (!string.IsNullOrEmpty(have)) return have;
                byte[] jpg;
                using (var ms = new System.IO.MemoryStream())
                {
                    var enc = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders().First(c => c.MimeType == "image/jpeg");
                    using (var ps = new System.Drawing.Imaging.EncoderParameters(1))
                    {
                        ps.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 88L);
                        cover.Save(ms, enc, ps);
                    }
                    jpg = ms.ToArray();
                }
                var url = Upload(jpg);
                if (url == null) return null;
                Db?.Exec("INSERT OR REPLACE INTO discord_covers (hash,url,ts) VALUES (?,?,?)", key, url, Library.Now);
                return url;
            }
            catch (Exception e) { App.Log("discord cover upload: " + e.Message); return null; }
        }

        // catbox.moe: free picture hosting, no account; answers with the picture's link
        static string Upload(byte[] jpg)
        {
            string boundary = "----aura" + Guid.NewGuid().ToString("N");
            var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(UploadUrl);
            req.Method = "POST"; req.UserAgent = Net.AppUA; req.Timeout = 30000; req.ReadWriteTimeout = 30000;
            req.ContentType = "multipart/form-data; boundary=" + boundary;
            using (var body = new System.IO.MemoryStream())
            {
                void W(string x) { var b = Encoding.UTF8.GetBytes(x); body.Write(b, 0, b.Length); }
                W("--" + boundary + "\r\nContent-Disposition: form-data; name=\"reqtype\"\r\n\r\nfileupload\r\n");
                W("--" + boundary + "\r\nContent-Disposition: form-data; name=\"fileToUpload\"; filename=\"cover.jpg\"\r\nContent-Type: image/jpeg\r\n\r\n");
                body.Write(jpg, 0, jpg.Length);
                W("\r\n--" + boundary + "--\r\n");
                req.ContentLength = body.Length;
                using (var rs = req.GetRequestStream()) body.WriteTo(rs);
            }
            using (var resp = (System.Net.HttpWebResponse)req.GetResponse())
            using (var r = new System.IO.StreamReader(resp.GetResponseStream()))
            {
                var url = r.ReadToEnd().Trim();
                return url.StartsWith("https://") ? url : null;
            }
        }

        // a tiny fingerprint of a picture (8×8, lighter or darker than average): close pictures give close fingerprints
        internal static ulong Hash(System.Drawing.Image img)
        {
            using (var b = new System.Drawing.Bitmap(8, 8))
            {
                using (var g = System.Drawing.Graphics.FromImage(b)) { g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear; g.DrawImage(img, 0, 0, 8, 8); }
                var v = new double[64]; double avg = 0;
                for (int i = 0; i < 64; i++) { var c = b.GetPixel(i % 8, i / 8); v[i] = c.R * 0.3 + c.G * 0.59 + c.B * 0.11; avg += v[i] / 64; }
                ulong h = 0;
                for (int i = 0; i < 64; i++) if (v[i] > avg) h |= 1UL << i;
                return h;
            }
        }
        internal static int Distance(ulong a, ulong b) { ulong x = a ^ b; int n = 0; while (x != 0) { x &= x - 1; n++; } return n; }

        static string Activity(Now n, string cover, bool withType)
        {
            if (n == null || !n.Playing) return "null";
            var t = n.T;
            long end = t.Duration > 0 ? n.StartMs + (long)(t.Duration * 1000) : 0;
            string title = t.Name.Length < 2 ? t.Name + "  " : t.Name, by = "by " + t.ArtistName;
            if (title.Length > 120) title = title.Substring(0, 120);
            string art = !string.IsNullOrEmpty(cover)
                ? "\"large_image\":" + J(cover) + ",\"large_text\":" + J(t.Album.Length > 0 ? t.Album : t.Name) + ",\"small_image\":\"aura\",\"small_text\":\"AURA\""
                : "\"large_image\":\"aura\",\"large_text\":" + J(t.Album.Length > 0 ? t.Album : "AURA");
            // type 2 = "Listening to"; status_display_type 2 = show the song's name (not "AURA") in the member list
            return "{" + (withType ? "\"type\":2,\"status_display_type\":2," : "") + "\"details\":" + J(title) + ",\"state\":" + J(by.Length > 120 ? by.Substring(0, 120) : by)
                + ",\"timestamps\":{\"start\":" + n.StartMs + (end > 0 ? ",\"end\":" + end : "") + "}"
                + ",\"assets\":{" + art + "},\"instance\":false}";
        }

        bool typeRefused;
#if TESTHOOKS
        internal static string TestActivity(Track t, string cover) => Activity(new Now { T = t, Playing = true, StartMs = 1700000000000 }, cover, true);
        internal static string TestCover(Track t) => CoverFor(t, true);
        internal static void TestClear() => ClearCovers();
#endif

        void Set(Now n, string cover)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var nonce = Guid.NewGuid().ToString();
                Send(1, "{\"cmd\":\"SET_ACTIVITY\",\"args\":{\"pid\":" + System.Diagnostics.Process.GetCurrentProcess().Id + ",\"activity\":" + Activity(n, cover, !typeRefused) + "},\"nonce\":\"" + nonce + "\"}");
                var reply = Read();
                // older Discord apps refuse "Listening to": send it as a plain status instead
                if (reply.Contains("\"evt\":\"ERROR\""))
                {
                    App.Log("discord: " + reply);
                    if (!typeRefused) { typeRefused = true; continue; }
                    var m = System.Text.RegularExpressions.Regex.Match(reply, "\"message\":\"([^\"]*)\"");
                    Status = "Discord said no: " + (m.Success ? m.Groups[1].Value : "it refused the status");
                    return;
                }
                Status = "on";
                return;
            }
        }

        void Run()
        {
            Perf.Here("discord");
            Now last = null;
            while (!stop)
            {
                try
                {
                    if (pipe == null || !pipe.IsConnected) { Connect(); lock (gate) { if (pending == null) pending = last; fresh = true; } }
                    Now n = null;
                    lock (gate) { if (fresh) { n = pending; fresh = false; } }
                    if (n != null)
                    {
                        last = n;
                        string cover = n.Playing ? CoverFor(n.T, false) : null;
                        Set(n, cover);
                        if (n.Playing && cover == null)
                        {
                            cover = CoverFor(n.T, true);
                            bool same; lock (gate) same = !fresh;
                            if (!string.IsNullOrEmpty(cover) && same) Set(n, cover);
                        }
                    }
                    wake.WaitOne(15000);
                }
                catch (Exception e)
                {
                    Status = "Discord isn't open (" + e.Message + ")";
                    try { pipe?.Dispose(); } catch { }
                    pipe = null;
                    wake.WaitOne(20000);
                }
            }
        }

        void Connect()
        {
            for (int i = 0; i < 10; i++)
            {
                var p = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut, PipeOptions.None);
                try { p.Connect(300); pipe = p; break; } catch { p.Dispose(); }
            }
            if (pipe == null) throw new IOException("no Discord app running");
            Send(0, "{\"v\":1,\"client_id\":" + J(appId) + "}");
            var hello = Read();
            if (hello.Contains("\"code\":4000") || hello.Contains("Invalid Client ID")) throw new IOException("the Application ID isn't right");
        }

        void Send(int op, string json)
        {
            var body = Encoding.UTF8.GetBytes(json);
            var head = new byte[8];
            BitConverter.GetBytes(op).CopyTo(head, 0);
            BitConverter.GetBytes(body.Length).CopyTo(head, 4);
            pipe.Write(head, 0, 8); pipe.Write(body, 0, body.Length); pipe.Flush();
        }

        string Read()
        {
            var head = new byte[8];
            int got = 0;
            while (got < 8) { int n = pipe.Read(head, got, 8 - got); if (n <= 0) throw new IOException("Discord closed the connection"); got += n; }
            int len = BitConverter.ToInt32(head, 4);
            if (len < 0 || len > 1 << 20) throw new IOException("odd reply from Discord");
            var body = new byte[len];
            got = 0;
            while (got < len) { int n = pipe.Read(body, got, len - got); if (n <= 0) throw new IOException("Discord closed the connection"); got += n; }
            return Encoding.UTF8.GetString(body);
        }

        public void Dispose()
        {
            stop = true; wake.Set();
            try { if (pipe != null && pipe.IsConnected) { lock (gate) { } Send(1, "{\"cmd\":\"SET_ACTIVITY\",\"args\":{\"pid\":" + System.Diagnostics.Process.GetCurrentProcess().Id + ",\"activity\":null},\"nonce\":\"bye\"}"); } } catch { }
            try { pipe?.Dispose(); } catch { }
        }
    }
}
