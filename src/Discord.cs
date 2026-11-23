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
            lock (gate) { pending = new Now { T = t, Playing = playing && t != null, StartMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - (long)(pos * 1000) }; fresh = true; }
            wake.Set();
        }

        // the song's cover as a web link (Discord can only show pictures from the web): looked up on Apple Music once per song
        static readonly System.Collections.Generic.Dictionary<string, string> covers = new System.Collections.Generic.Dictionary<string, string>();
        static string CoverKey(Track t) => LyricsOnline.Norm(t.MainArtist) + "|" + LyricsOnline.Norm(LyricsOnline.CleanTitle(t.Name));
        static string CoverFor(Track t, bool lookUp)
        {
            var key = CoverKey(t);
            lock (covers) if (covers.TryGetValue(key, out var u)) return u;
            if (!lookUp || t.MainArtist.Length == 0 || t.MainArtist == "Unknown artist") return null;
            string url = "";
            try
            {
                string title = LyricsOnline.CleanTitle(t.Name), artist = t.MainArtist;
                foreach (var (entity, term) in new[] { ("song", artist + " " + title), ("album", artist + " " + (t.Album.Length > 0 ? t.Album : title)) })
                {
                    var res = Net.Json<ItunesResp>(Net.Get(CoverSearch.ItunesUrl + "/search?media=music&entity=" + entity + "&limit=10&term=" + Uri.EscapeDataString(term)))?.Results;
                    var hit = res?.FirstOrDefault(r => !string.IsNullOrEmpty(r.Art) && LyricsOnline.ArtistOk(artist, r.Artist ?? "")
                        && (entity == "album" || LyricsOnline.Sim(r.Track ?? "", title) >= 0.8));
                    if (hit != null) { url = hit.Art.Replace("100x100bb", "512x512bb"); break; }
                }
            }
            catch (Exception e) { App.Log("discord cover: " + e.Message); return null; }   // try again next time
            lock (covers) covers[key] = url;
            return url;
        }

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
