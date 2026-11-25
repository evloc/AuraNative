using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;

namespace Aura
{
    // Cover art: read once from the song (or a cover.jpg next to it), saved small in the art folder,
    // kept in memory for what's on screen. Loading happens on a background thread, newest request first.
    static class Art
    {
        public const int Size = 320;
        static string dir;
        static readonly Dictionary<long, Bitmap> mem = new Dictionary<long, Bitmap>();
        static readonly LinkedList<long> lru = new LinkedList<long>();
        static readonly Dictionary<long, LinkedListNode<long>> lruNode = new Dictionary<long, LinkedListNode<long>>();
        const int Keep = 300;
        public static int InMemory => mem.Count;
        static readonly HashSet<long> none = new HashSet<long>();
        static readonly List<(long id, string path, double mtime, bool hasArt)> want = new List<(long, string, double, bool)>();
        static readonly HashSet<long> queued = new HashSet<long>();
        static readonly object gate = new object();
        static readonly AutoResetEvent wake = new AutoResetEvent(false);
        static SynchronizationContext ui;
        public static event Action<long> Ready;      // on the UI thread

        public static void Init(string dataDir)
        {
            dir = Path.Combine(dataDir, "art");
            Directory.CreateDirectory(dir);
            ui = SynchronizationContext.Current;
            var th = new Thread(Worker) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "art" };
            th.Start();
        }

        static string FileFor(long id, double mtime) => Path.Combine(dir, id + "_" + ((long)mtime).ToString("x") + ".jpg");

        // Returns the cover if it's ready; otherwise asks for it and returns null (Ready fires later).
        public static Bitmap Get(Track t)
        {
            if (t == null) return null;
            if (mem.TryGetValue(t.Id, out var b)) { Touch(t.Id); return b; }
            if (none.Contains(t.Id)) return null;
            lock (gate)
            {
                if (queued.Add(t.Id)) want.Add((t.Id, t.Path, t.Mtime, t.HasArt));
                else
                {   // move to the front of the line
                    int i = want.FindIndex(x => x.id == t.Id);
                    if (i >= 0 && i != want.Count - 1) { var x = want[i]; want.RemoveAt(i); want.Add(x); }
                }
            }
            wake.Set();
            return null;
        }

        public static bool Missing(Track t) => t != null && none.Contains(t.Id);

        public static void Forget(long id)
        {
            if (mem.TryGetValue(id, out var b)) { mem.Remove(id); Unlink(id); Theme.ForgetScaled(b); b.Dispose(); }
            none.Remove(id);
        }

        // (a covers list is touched for every cover drawn, every frame: this has to be instant)
        static void Touch(long id)
        {
            if (lruNode.TryGetValue(id, out var n)) { if (n != lru.First) { lru.Remove(n); lru.AddFirst(n); } }
            else lruNode[id] = lru.AddFirst(id);
        }
        static void Unlink(long id) { if (lruNode.TryGetValue(id, out var n)) { lru.Remove(n); lruNode.Remove(id); } }

        static void Store(long id, Bitmap b)
        {
            lock (gate) queued.Remove(id);
            if (b == null) none.Add(id);
            else
            {
                if (mem.TryGetValue(id, out var old)) { Theme.ForgetScaled(old); old.Dispose(); }
                mem[id] = b; Touch(id);
                while (lru.Count > Keep) { var last = lru.Last.Value; Unlink(last); if (mem.TryGetValue(last, out var x)) { mem.Remove(last); Theme.ForgetScaled(x); x.Dispose(); } }
            }
            Ready?.Invoke(id);
        }

        static void Worker()
        {
            Perf.Here("covers");
            while (true)
            {
                (long id, string path, double mtime, bool hasArt) job;
                lock (gate)
                {
                    if (want.Count == 0) job = (0, null, 0, false);
                    else { job = want[want.Count - 1]; want.RemoveAt(want.Count - 1); }
                    if (want.Count > 400) { foreach (var x in want.Take(want.Count - 400)) queued.Remove(x.id); want.RemoveRange(0, want.Count - 400); }
                }
                if (job.path == null) { wake.WaitOne(2000); continue; }
                Bitmap result = null;
                try { result = Load(job.id, job.path, job.mtime, job.hasArt); }
                catch (Exception e) { App.Log("cover " + job.path + ": " + e.Message); }
                var r = result; var id = job.id;
                if (ui != null) ui.Post(_ => Store(id, r), null); else Store(id, r);
            }
        }

        static readonly string[] FolderNames = { "cover.jpg", "cover.png", "folder.jpg", "folder.png", "front.jpg", "front.png", "album.jpg", "album.png" };

        // the cover at a bigger size (full-screen lyrics), read straight from the song. Slow: call it off the UI thread.
        public static Bitmap LoadBig(Track t, int size)
        {
            try
            {
                byte[] data = null;
                if (t.HasArt) data = Tags.Read(t.Path, true).Picture;
                if (data == null)
                {
                    var folder = Path.GetDirectoryName(t.Path);
                    foreach (var n in FolderNames)
                    {
                        var f = Path.Combine(folder ?? "", n);
                        if (FileX.Exists(f)) { data = File.ReadAllBytes(FileX.Long(f)); break; }
                    }
                }
                if (data == null || data.Length == 0) return null;
                using (var ms = new MemoryStream(data))
                using (var img = Image.FromStream(ms))
                    return Square(img, Math.Min(size, Math.Max(img.Width, img.Height)));
            }
            catch (Exception e) { App.Log("big cover " + t.Path + ": " + e.Message); return null; }
        }

        static Bitmap Load(long id, string path, double mtime, bool hasArt)
        {
            var file = FileFor(id, mtime);
            if (File.Exists(file))
                using (var fs = File.OpenRead(file)) using (var img = Image.FromStream(fs)) return new Bitmap(img);
            byte[] data = null;
            if (hasArt) data = Tags.Read(path, true).Picture;
            if (data == null)
            {
                var folder = Path.GetDirectoryName(path);
                foreach (var n in FolderNames)
                {
                    var f = Path.Combine(folder ?? "", n);
                    if (FileX.Exists(f)) { data = File.ReadAllBytes(FileX.Long(f)); break; }
                }
            }
            if (data == null || data.Length == 0) return null;
            Bitmap small;
            using (var ms = new MemoryStream(data))
            using (var img = Image.FromStream(ms))
                small = Square(img, Size);
            try
            {
                foreach (var old in Directory.GetFiles(dir, id + "_*.jpg")) File.Delete(old);
                var enc = ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);
                var ps = new EncoderParameters(1); ps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 88L);
                if (enc != null) small.Save(file, enc, ps); else small.Save(file, ImageFormat.Jpeg);
            }
            catch { }
            return small;
        }

        public static Bitmap Square(Image img, int size)
        {
            var b = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                int s = Math.Min(img.Width, img.Height);
                var src = new Rectangle((img.Width - s) / 2, (img.Height - s) / 2, s, s);
                using (var ia = new ImageAttributes()) { ia.SetWrapMode(WrapMode.TileFlipXY); g.DrawImage(img, new Rectangle(0, 0, size, size), src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel, ia); }
            }
            return b;
        }

        // The cover's main color, made bright enough to read on a dark background.
        public static Color Accent(Bitmap b)
        {
            if (b == null) return Color.Empty;
            using (var tiny = new Bitmap(24, 24))
            {
                using (var g = Graphics.FromImage(tiny)) { g.InterpolationMode = InterpolationMode.HighQualityBilinear; g.DrawImage(b, 0, 0, 24, 24); }
                var buckets = new double[36]; var sums = new double[36, 3];
                for (int y = 0; y < 24; y++)
                    for (int x = 0; x < 24; x++)
                    {
                        var c = tiny.GetPixel(x, y);
                        float s = c.GetSaturation(), l = c.GetBrightness();
                        if (l < 0.12f || l > 0.95f) continue;
                        double w = s * s * (1 - Math.Abs(l - 0.55) * 1.2);
                        if (w <= 0) continue;
                        int h = (int)(c.GetHue() / 10) % 36;
                        buckets[h] += w; sums[h, 0] += c.R * w; sums[h, 1] += c.G * w; sums[h, 2] += c.B * w;
                    }
                int best = -1; double bw = 0;
                for (int i = 0; i < 36; i++) { double w = buckets[i] + 0.5 * (buckets[(i + 35) % 36] + buckets[(i + 1) % 36]); if (w > bw) { bw = w; best = i; } }
                if (best < 0 || buckets[best] < 0.6) return Color.Empty;     // a grey/black/white cover: keep the theme color
                var col = Color.FromArgb((int)(sums[best, 0] / buckets[best]), (int)(sums[best, 1] / buckets[best]), (int)(sums[best, 2] / buckets[best]));
                return Theme.FromHsl(col.GetHue(), Math.Max(0.55f, Math.Min(0.9f, col.GetSaturation())), 0.6f);
            }
        }
    }
}
