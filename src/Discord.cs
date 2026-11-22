using System;
using System.IO;
using System.IO.Pipes;
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
        string pending;           // the latest activity to send
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        public string Status = "starting";

        public Discord(string appId)
        {
            this.appId = appId;
            worker = new Thread(Run) { IsBackground = true, Name = "discord" };
            worker.Start();
        }

        static string J(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ") + "\"";

        public void Update(Track t, bool playing, double pos)
        {
            string activity;
            if (t == null || !playing) activity = "null";
            else
            {
                long start = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - (long)pos;
                long end = t.Duration > 0 ? start + (long)t.Duration : 0;
                string title = t.Name.Length < 2 ? t.Name + "  " : t.Name, by = "by " + t.ArtistName;
                if (title.Length > 120) title = title.Substring(0, 120);
                activity = "{\"type\":2,\"details\":" + J(title) + ",\"state\":" + J(by.Length > 120 ? by.Substring(0, 120) : by)
                    + ",\"timestamps\":{\"start\":" + start + (end > 0 ? ",\"end\":" + end : "") + "}"
                    + ",\"assets\":{\"large_image\":\"aura\",\"large_text\":" + J(t.Album.Length > 0 ? t.Album : "AURA") + "}}";
            }
            lock (gate) pending = activity;
            wake.Set();
        }

        void Run()
        {
            while (!stop)
            {
                try
                {
                    if (pipe == null || !pipe.IsConnected) Connect();
                    string a;
                    lock (gate) { a = pending; pending = null; }
                    if (a != null)
                    {
                        var nonce = Guid.NewGuid().ToString();
                        Send(1, "{\"cmd\":\"SET_ACTIVITY\",\"args\":{\"pid\":" + System.Diagnostics.Process.GetCurrentProcess().Id + ",\"activity\":" + a + "},\"nonce\":\"" + nonce + "\"}");
                        Read();
                        Status = "on";
                    }
                    wake.WaitOne(15000);
                }
                catch (Exception e)
                {
                    Status = "Discord isn't open (" + e.Message + ")";
                    try { pipe?.Dispose(); } catch { }
                    pipe = null;
                    lock (gate) if (pending == null) pending = null;
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
