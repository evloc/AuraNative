using System;
using System.IO;
using System.Net;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Aura
{
    static class Net
    {
        public const string BrowserUA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36";
        public static string AppUA => "AURA-Native/" + App.Version + " (https://github.com/evloc/AuraNative)";

        static Net()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
        }

        public static string Get(string url, int timeoutMs = 9000, string ua = null, string accept = "application/json")
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = ua ?? AppUA;
            req.Accept = accept;
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var s = resp.GetResponseStream())
            using (var r = new StreamReader(s, Encoding.UTF8))
                return r.ReadToEnd();
        }

        public static T Json<T>(string text) where T : class
        {
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(text ?? "")))
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(ms);
        }

        // downloads to a file, reporting 0..100
        public static void Download(string url, string file, Action<int> progress, Func<bool> cancelled = null)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = AppUA;
            req.Timeout = 30000; req.ReadWriteTimeout = 30000;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var s = resp.GetResponseStream())
            using (var f = File.Create(file))
            {
                long total = resp.ContentLength, got = 0;
                var buf = new byte[81920];
                int n, last = -1;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                {
                    if (cancelled != null && cancelled()) throw new OperationCanceledException();
                    f.Write(buf, 0, n);
                    got += n;
                    int pct = total > 0 ? (int)(got * 100 / total) : -1;
                    if (pct != last) { last = pct; progress?.Invoke(pct); }
                }
            }
        }
    }
}
