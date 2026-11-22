using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace Aura
{
    // Reads a song as raw sound. On Windows that's Media Foundation; the test build can use ffmpeg instead.
    static class Decoder
    {
        // block(samples, count, channels, rate): interleaved floats
        public static void Read(Track t, Action<float[], int, int, int> block, Func<bool> cancelled) => Read(t.Path, t.Id, block, cancelled);

        public static void Read(string path, long id, Action<float[], int, int, int> block, Func<bool> cancelled)
        {
            if (!FileX.Exists(path)) throw new FileNotFoundException("the file isn't there", path);
            var p = path;
            var kind = AudioKind.Detect(path);
            if (!AudioKind.Matches(path, kind)) p = AudioKind.PlayableCopy(path, kind, id);
#if TESTHOOKS
            if (!App.IsWindows || Environment.GetEnvironmentVariable("AURA_DECODER") == "ffmpeg") { Ffmpeg(p, block, cancelled); return; }
#endif
            Mf.Read(FileX.ForPlayer(p), block, cancelled);
        }

#if TESTHOOKS
        static void Ffmpeg(string path, Action<float[], int, int, int> block, Func<bool> cancelled)
        {
            var psi = new ProcessStartInfo("ffmpeg", "-v error -nostdin -i \"" + path + "\" -vn -f f32le -ac 2 -ar 44100 pipe:1")
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            using (var pr = Process.Start(psi))
            {
                var err = pr.StandardError.ReadToEndAsync();
                var s = pr.StandardOutput.BaseStream;
                var bytes = new byte[65536]; var f = new float[16384];
                int have = 0;
                while (cancelled == null || !cancelled())
                {
                    int n = s.Read(bytes, have, bytes.Length - have);
                    if (n <= 0) break;
                    have += n;
                    int whole = have / 8 * 8;
                    Buffer.BlockCopy(bytes, 0, f, 0, whole);
                    block(f, whole / 4, 2, 44100);
                    Array.Copy(bytes, whole, bytes, 0, have - whole); have -= whole;
                }
                try { if (!pr.HasExited) pr.Kill(); } catch { }
                pr.WaitForExit();
                if (pr.ExitCode != 0 && pr.ExitCode != 137 && !(cancelled?.Invoke() ?? false)) throw new IOException("couldn't decode: " + err.Result.Trim());
            }
        }
#endif

        // the whole song, mixed to one channel and brought down to "rate" samples a second
        public static float[] Mono(string path, long id, int rate, double maxSeconds, Func<bool> cancelled, out double length)
        {
            var outp = new List<float>(rate * 240);
            double pos = 0, step = 0; float acc = 0; int cnt = 0;
            float lp1 = 0, lp2 = 0; double alpha = 0;
            long frames = 0; int srcRate = 0;
            Read(path, id, (b, n, ch, r) =>
            {
                if (srcRate == 0)
                {
                    srcRate = r; step = (double)r / rate;
                    alpha = 1 - Math.Exp(-2 * Math.PI * (rate * 0.45) / r);       // a gentle low-pass before thinning out
                }
                if (outp.Count >= rate * maxSeconds) return;
                for (int i = 0; i + ch <= n; i += ch)
                {
                    float m = 0; for (int c = 0; c < ch; c++) m += b[i + c]; m /= ch;
                    lp1 += (float)alpha * (m - lp1); lp2 += (float)alpha * (lp1 - lp2);
                    acc += lp2; cnt++; frames++;
                    pos += 1;
                    if (pos >= step) { pos -= step; outp.Add(acc / cnt); acc = 0; cnt = 0; }
                }
            }, () => (cancelled?.Invoke() ?? false) || outp.Count >= rate * maxSeconds);
            length = srcRate > 0 ? (double)frames / srcRate : 0;
            return outp.ToArray();
        }
    }

    static class Dsp
    {
        // in-place radix-2 FFT
        public static void Fft(double[] re, double[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) { var t = re[i]; re[i] = re[j]; re[j] = t; t = im[i]; im[i] = im[j]; im[j] = t; }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2 * Math.PI / len, wr = Math.Cos(ang), wi = Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    double cr = 1, ci = 0;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int a = i + k, b = a + len / 2;
                        double xr = re[b] * cr - im[b] * ci, xi = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - xr; im[b] = im[a] - xi; re[a] += xr; im[a] += xi;
                        double nr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = nr;
                    }
                }
            }
        }

        public static double[] Hann(int n) { var w = new double[n]; for (int i = 0; i < n; i++) w[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / n); return w; }

        public static int Bits(uint v)
        {
            v = v - ((v >> 1) & 0x55555555u);
            v = (v & 0x33333333u) + ((v >> 2) & 0x33333333u);
            return (int)((((v + (v >> 4)) & 0x0F0F0F0Fu) * 0x01010101u) >> 24);
        }
    }

    // What AURA learns by listening to a song once.
    sealed class SongSound
    {
        public const int Version = 2;      // 2: also how loud the song is (for leveling)
        public const int Rate = 11025, Hop = 256;          // ~43 measurements a second
        public static double Fps => (double)Rate / Hop;
        public double Length;
        public double Bpm;                // 0 when there's no steady beat
        public double Loud = double.NaN;  // average loudness of the sung/played parts, dB below full scale (NaN when unknown)
        public string Key = "";           // "A minor"
        public byte[] Peaks = new byte[0];    // loudest point in each step, 0-255
        public byte[] Env = new byte[0];      // loudness in each step, 0-255 (for lining versions up)
        public uint[] Print = new uint[0];    // sound fingerprint, one 32-bit value per step

        public string Camelot => KeyInfo.Camelot(Key);

        public static SongSound Analyze(string path, long id, Func<bool> cancelled)
        {
            var x = Decoder.Mono(path, id, Rate, 20 * 60, cancelled, out double length);
            if (cancelled != null && cancelled()) return null;
            if (x.Length < Rate) throw new InvalidDataException("the song is too short or silent");
            return FromSamples(x, length > 0 ? length : (double)x.Length / Rate);
        }

        public static SongSound FromSamples(float[] x, double length)
        {
            var s = new SongSound { Length = length };
            int steps = x.Length / Hop;
            // peaks and loudness
            var peaks = new double[steps]; var env = new double[steps];
            for (int i = 0; i < steps; i++)
            {
                double mx = 0, sum = 0;
                for (int j = i * Hop, e = j + Hop; j < e; j++) { double v = Math.Abs(x[j]); if (v > mx) mx = v; sum += x[j] * x[j]; }
                peaks[i] = mx; env[i] = Math.Sqrt(sum / Hop);
            }
            // loudness: the average power of the parts that aren't near-silent
            {
                var pw = env.Where(v => v > 0.003).Select(v => v * v).OrderBy(v => v).ToList();
                if (pw.Count > 0) { var loudest = pw.Skip(pw.Count / 5).ToList(); s.Loud = 10 * Math.Log10(Math.Max(1e-10, loudest.Average())); }
            }
            double top = Math.Max(1e-6, peaks.Max());
            s.Peaks = peaks.Select(v => (byte)Math.Min(255, Math.Round(255 * Math.Sqrt(v / top)))).ToArray();
            s.Env = env.Select(v => (byte)Math.Max(0, Math.Min(255, Math.Round(255 + 255 * Math.Log10(Math.Max(v, 1e-5) / top) / 4.0)))).ToArray();

            // spectrum, every step
            const int N = 2048;
            var win = Dsp.Hann(N);
            var re = new double[N]; var im = new double[N];
            const int bands = 33;
            var edges = new int[bands + 1];
            for (int b = 0; b <= bands; b++) edges[b] = (int)Math.Round(300 * Math.Pow(2000.0 / 300, (double)b / bands) * N / Rate);
            var prevBand = new double[bands]; var band = new double[bands];
            int kMax = 4000 * N / Rate;
            var prevLog = new double[kMax]; var flux = new double[steps];
            var print = new uint[Math.Max(0, steps)];
            for (int i = 0; i < steps; i++)
            {
                int at = i * Hop - N / 2;
                for (int j = 0; j < N; j++) { int q = at + j; re[j] = q >= 0 && q < x.Length ? x[q] * win[j] : 0; im[j] = 0; }
                Dsp.Fft(re, im);
                double f = 0;
                for (int k = 1; k < kMax; k++)
                {
                    double m = Math.Log(1 + 1000 * Math.Sqrt(re[k] * re[k] + im[k] * im[k]));
                    if (m > prevLog[k]) f += m - prevLog[k];
                    prevLog[k] = m;
                }
                flux[i] = f;
                for (int b = 0; b < bands; b++)
                {
                    double e = 0;
                    for (int k = edges[b]; k < edges[b + 1]; k++) e += re[k] * re[k] + im[k] * im[k];
                    band[b] = e;
                }
                uint v = 0;
                for (int b = 0; b < 32; b++)
                    if ((band[b] - band[b + 1]) - (prevBand[b] - prevBand[b + 1]) > 0) v |= 1u << b;
                print[i] = i == 0 ? 0 : v;
                Array.Copy(band, prevBand, bands);
            }
            s.Print = print;
            s.Bpm = Tempo(flux);
            s.Key = KeyOf(x);
            return s;
        }

        // beats per minute from how often new sounds start
        static double Tempo(double[] flux)
        {
            int n = flux.Length;
            if (n < Fps * 8) return 0;
            // take away the slow trend, keep the jumps
            var o = new double[n];
            int w = (int)Fps;
            double run = 0;
            for (int i = 0; i < n; i++)
            {
                run += flux[i]; if (i >= w) run -= flux[i - w];
                double mean = run / Math.Min(i + 1, w);
                o[i] = Math.Max(0, flux[i] - mean);
            }
            int minLag = (int)(60 * Fps / 220), maxLag = (int)Math.Ceiling(60 * Fps / 50) * 2 + 2;
            var ac = new double[maxLag + 1];
            for (int L = minLag; L <= maxLag; L++) { double sum = 0; for (int i = L; i < n; i++) sum += o[i] * o[i - L]; ac[L] = sum / (n - L); }
            double zero = 0; for (int i = 0; i < n; i++) zero += o[i] * o[i]; zero /= n;
            if (zero <= 0) return 0;
            double best = 0; double bestLag = 0;
            for (int L = minLag; L * 2 <= maxLag && L <= 60 * Fps / 50; L++)
            {
                double bpm = 60 * Fps / L;
                double prior = Math.Exp(-0.5 * Math.Pow(Math.Log(bpm / 120, 2) / 0.9, 2));
                double sc = (ac[L] + 0.5 * ac[2 * L] + (3 * L <= maxLag ? 0.25 * ac[3 * L] : 0)) * prior;
                if (sc > best) { best = sc; bestLag = L; }
            }
            if (bestLag == 0 || ac[(int)bestLag] / zero < 0.04) return 0;
            // finer than one step: the lag whose multiples all land on strong beats
            var longAc = new double[Math.Min(n - 1, (int)(bestLag * 8) + 3)];
            for (int L = 0; L < longAc.Length; L++) { double sum = 0; for (int i = L; i < n; i++) sum += o[i] * o[i - L]; longAc[L] = sum / (n - L); }
            double LongAt(double L) { int i = (int)L; double f = L - i; return i + 1 < longAc.Length ? longAc[i] * (1 - f) + longAc[i + 1] * f : 0; }
            double lag = bestLag, bestS = double.MinValue;
            for (double L = bestLag - 1; L <= bestLag + 1; L += 0.01)
            {
                double sc = 0; int m = 0;
                for (int k = 1; k <= 8 && k * L + 1 < longAc.Length; k++) { sc += LongAt(k * L); m++; }
                if (m > 0 && sc / m > bestS) { bestS = sc / m; lag = L; }
            }
            double tempo = 60 * Fps / lag;
            while (tempo < 70) tempo *= 2;
            while (tempo >= 180) tempo /= 2;
            return Math.Round(tempo, 1);
        }

        // musical key from which notes are heard most
        static readonly double[] Major = { 6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88 };
        static readonly double[] Minor = { 6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17 };
        public static readonly string[] Notes = { "C", "C♯", "D", "E♭", "E", "F", "F♯", "G", "A♭", "A", "B♭", "B" };

        static string KeyOf(float[] x)
        {
            const int N = 8192, hop = 2048;
            var win = Dsp.Hann(N);
            var re = new double[N]; var im = new double[N];
            var chroma = new double[12];
            var pcOf = new int[N / 2]; var wOf = new double[N / 2];
            for (int k = 0; k < N / 2; k++)
            {
                double f = (double)k * Rate / N;
                if (f < 60 || f > 2100) { pcOf[k] = -1; continue; }
                double midi = 69 + 12 * Math.Log(f / 440, 2);
                int r = (int)Math.Round(midi);
                pcOf[k] = ((r % 12) + 12) % 12;
                wOf[k] = Math.Max(0, 1 - 2 * Math.Abs(midi - r));        // bins between notes count less
            }
            var frame = new double[12];
            for (int at = 0; at + N <= x.Length; at += hop)
            {
                for (int j = 0; j < N; j++) { re[j] = x[at + j] * win[j]; im[j] = 0; }
                Dsp.Fft(re, im);
                Array.Clear(frame, 0, 12);
                for (int k = 1; k < N / 2; k++)
                    if (pcOf[k] >= 0) frame[pcOf[k]] += wOf[k] * Math.Sqrt(Math.Sqrt(re[k] * re[k] + im[k] * im[k]));
                double sum = frame.Sum();
                if (sum <= 1e-9) continue;
                for (int c = 0; c < 12; c++) chroma[c] += frame[c] / sum;
            }
            if (chroma.Sum() <= 0) return "";
            double bestR = -2; string best = "";
            for (int root = 0; root < 12; root++)
                foreach (var (prof, name) in new[] { (Major, "major"), (Minor, "minor") })
                {
                    double r = Corr(chroma, prof, root);
                    if (r > bestR) { bestR = r; best = Notes[root] + " " + name; }
                }
            return bestR > 0.3 ? best : "";
        }

        static double Corr(double[] c, double[] prof, int root)
        {
            double mc = c.Average(), mp = prof.Average(), sxy = 0, sxx = 0, syy = 0;
            for (int i = 0; i < 12; i++)
            {
                double a = c[(i + root) % 12] - mc, b = prof[i] - mp;
                sxy += a * b; sxx += a * a; syy += b * b;
            }
            return sxy / Math.Sqrt(sxx * syy + 1e-12);
        }

        // ---------- comparing ----------
        // share of differing fingerprint bits when b is shifted by "offset" steps (0 = identical, ~0.5 = unrelated)
        public static double Ber(uint[] a, uint[] b, int offset, int stride = 1)
        {
            long bits = 0, n = 0;
            int from = Math.Max(1, -offset), to = Math.Min(a.Length, b.Length - offset);
            for (int i = from; i < to; i += stride) { bits += Dsp.Bits(a[i] ^ b[i + offset]); n++; }
            return n < 200 ? 1 : bits / (32.0 * n);
        }

        // best match between two fingerprints within ±maxShift seconds: (difference, offset in steps)
        public static (double ber, int offset) Match(SongSound a, SongSound b, double maxShift)
        {
            int ms = (int)(maxShift * Fps);
            double best = 1; int bestOff = 0;
            for (int o = -ms; o <= ms; o++)
            {
                double v = Ber(a.Print, b.Print, o, 7);
                if (v < best) { best = v; bestOff = o; }
            }
            int c = bestOff; best = 2;
            for (int o = c - 3; o <= c + 3; o++)
            {
                double v = Ber(a.Print, b.Print, o, 1);
                if (v < best) { best = v; bestOff = o; }
            }
            return (best, bestOff);
        }

        // how far b is shifted against a, from their loudness over time (coarse, 4 per second)
        static int EnvLag(byte[] a, byte[] b, int maxShift)
        {
            int f = (int)(Fps / 4);
            var ca = Coarse(a, f); var cb = Coarse(b, f);
            int ms = Math.Max(1, maxShift / f);
            double best = double.MinValue; int bl = 0;
            for (int o = -ms; o <= ms; o++)
            {
                double c = NCorr(ca, cb, o);
                if (c > best) { best = c; bl = o; }
            }
            return bl * f;
        }

        public static double[] Coarse(byte[] e, int f)
        {
            var c = new double[e.Length / f];
            for (int i = 0; i < c.Length; i++) { double s = 0; for (int j = 0; j < f; j++) s += e[i * f + j]; c[i] = s / f; }
            return c;
        }

        public static double NCorr(double[] a, double[] b, int offset, int from = 0, int to = int.MaxValue)
        {
            int s = Math.Max(from, Math.Max(0, -offset)), e = Math.Min(Math.Min(to, a.Length), b.Length - offset);
            int n = e - s;
            if (n < 8) return -1;
            double ma = 0, mb = 0;
            for (int i = s; i < e; i++) { ma += a[i]; mb += b[i + offset]; }
            ma /= n; mb /= n;
            double sxy = 0, sxx = 0, syy = 0;
            for (int i = s; i < e; i++) { double x = a[i] - ma, y = b[i + offset] - mb; sxy += x * y; sxx += x * x; syy += y * y; }
            return sxy / Math.Sqrt(sxx * syy + 1e-9);
        }

        // where "seconds" in this song is in another version of it, lined up by sound; -1 if they don't line up
        public double MapTo(SongSound other, double seconds)
        {
            int f = 4;              // ~10 a second
            var a = Coarse(Env, f); var b = Coarse(other.Env, f);
            double rate = Fps / f;
            int at = (int)(seconds * rate);
            int from = Math.Max(0, at - (int)(20 * rate)), to = Math.Min(a.Length, at + (int)(6 * rate));
            if (to - from < 10 * rate) { from = Math.Max(0, to - (int)(26 * rate)); }
            if (to - from < 8 * rate) return -1;
            // a window of this song, searched across the whole other one: the best few spots by loudness...
            var cands = new List<(double c, int o)>();
            for (int o = -from; o + from < b.Length; o++)
            {
                double c = NCorr(a, b, o, from, to);
                if (c > 0.5) cands.Add((c * (1 - 0.0005 * Math.Abs(o)), o));      // when equally good, prefer the closer spot
            }
            if (cands.Count == 0) return -1;
            // ...peaks only, then the one whose fingerprint fits best decides
            var peaks = cands.Where(x => !cands.Any(y => Math.Abs(y.o - x.o) <= 2 && y.c > x.c)).OrderByDescending(x => x.c).Take(16).ToList();
            int pf = Math.Max(0, from * f), pt = Math.Min(Print.Length, to * f);
            double bestBer = 1; int bestOff = peaks[0].o * f;
            foreach (var (c, o) in peaks)
                for (int d = -f; d <= f; d++)
                {
                    int off = o * f + d;
                    long bits = 0, n = 0;
                    for (int i = Math.Max(pf, Math.Max(1, -off)); i < pt && i + off < other.Print.Length; i += 2) { bits += Dsp.Bits(Print[i] ^ other.Print[i + off]); n++; }
                    if (n < 50) continue;
                    double ber = bits / (32.0 * n) + 0.001 * Math.Abs(off) / Fps;     // a little in favor of the closer spot
                    if (ber < bestBer) { bestBer = ber; bestOff = off; }
                }
            if (peaks[0].c < 0.6 && bestBer > 0.4) return -1;
            double t = seconds + bestOff / Fps;
            return t < 0 || t > other.Length ? -1 : t;
        }
    }

    static class KeyInfo
    {
        static readonly string[] MajorCam = { "8B", "3B", "10B", "5B", "12B", "7B", "2B", "9B", "4B", "11B", "6B", "1B" };
        static readonly string[] MinorCam = { "5A", "12A", "7A", "2A", "9A", "4A", "11A", "6A", "1A", "8A", "3A", "10A" };
        public static string Camelot(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            var parts = key.Split(' ');
            int i = Array.IndexOf(SongSound.Notes, parts[0]);
            if (i < 0 || parts.Length < 2) return "";
            return parts[1] == "major" ? MajorCam[i] : MinorCam[i];
        }
        public static string Short(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            var parts = key.Split(' ');
            return parts.Length < 2 ? key : parts[0] + (parts[1] == "minor" ? "m" : "");
        }
    }

    // Keeps what was learned, and listens to songs in the background (one at a time, gently).
    sealed class SoundStore
    {
        readonly Library lib;
        readonly Dictionary<long, SongSound> cache = new Dictionary<long, SongSound>();
        readonly HashSet<long> done = new HashSet<long>();
        readonly object gate = new object();
        readonly LinkedList<long> urgent = new LinkedList<long>();
        Thread worker;
        volatile bool stop;
        public bool Background = true;
        public event Action<long> Ready;      // on the UI thread
        public int Total => lib.Tracks.Count;
        public int Done { get { lock (gate) return lib.Tracks.Count(t => done.Contains(t.Id)); } }
        public string Current = "";
        SynchronizationContext ui;

        public SoundStore(Library lib)
        {
            this.lib = lib;
            lib.Db.Script("CREATE TABLE IF NOT EXISTS sound (track_id INTEGER PRIMARY KEY, size INTEGER, mtime REAL, version INTEGER, length REAL, bpm REAL, key TEXT, peaks BLOB, env BLOB, print BLOB, err TEXT, ts REAL)");
            if (!lib.Db.Rows("PRAGMA table_info(sound)").Any(r => Conv.S(r[1]) == "loud")) lib.Db.Exec("ALTER TABLE sound ADD COLUMN loud REAL");
            // what was learned by an older version still shows (waveform, tempo) until the song is heard again
            foreach (var r in lib.Db.Rows("SELECT track_id,size,mtime FROM sound WHERE version<>? AND err=''", SongSound.Version)) older[Conv.L(r[0])] = (Conv.L(r[1]), Conv.D(r[2]));
            ui = SynchronizationContext.Current;
            Reload();
        }

        public void SetUiContext(SynchronizationContext c) => ui = c;

        void Reload()
        {
            var by = lib.ById;
            var d = new HashSet<long>();
            foreach (var r in lib.Db.Rows("SELECT track_id,size,mtime,version FROM sound"))
            {
                if (!by.TryGetValue(Conv.L(r[0]), out var t)) continue;
                if (Conv.L(r[1]) == t.Size && Math.Abs(Conv.D(r[2]) - t.Mtime) < 2 && Conv.L(r[3]) == SongSound.Version) d.Add(t.Id);
            }
            lock (gate) { done.Clear(); done.UnionWith(d); }
        }

        public void LibraryChanged() { Reload(); Kick(); }

        public bool Has(long id) { lock (gate) return done.Contains(id); }
        readonly Dictionary<long, (long size, double mtime)> older = new Dictionary<long, (long, double)>();

        public SongSound Get(Track t)
        {
            if (t == null) return null;
            lock (gate)
            {
                if (cache.TryGetValue(t.Id, out var c)) return c;
                if (!done.Contains(t.Id) && !(older.TryGetValue(t.Id, out var o) && o.size == t.Size && Math.Abs(o.mtime - t.Mtime) < 2)) return null;
            }
            var r = lib.Db.Rows("SELECT length,bpm,key,peaks,env,print,err,loud FROM sound WHERE track_id=?", t.Id).FirstOrDefault();
            if (r == null || Conv.S(r[6]).Length > 0) return null;
            var s = new SongSound
            {
                Length = Conv.D(r[0]), Bpm = Conv.D(r[1]), Key = Conv.S(r[2]),
                Peaks = r[3] as byte[] ?? new byte[0], Env = r[4] as byte[] ?? new byte[0],
                Loud = r[7] == null || r[7] is DBNull ? double.NaN : Conv.D(r[7]),
            };
            var pb = r[5] as byte[] ?? new byte[0];
            s.Print = new uint[pb.Length / 4];
            Buffer.BlockCopy(pb, 0, s.Print, 0, s.Print.Length * 4);
            lock (gate) { if (cache.Count > 60) cache.Clear(); cache[t.Id] = s; }
            return s;
        }

        public string Error(Track t) => t == null ? "" : Conv.S(lib.Db.Scalar("SELECT err FROM sound WHERE track_id=?", t.Id));

        // listen to this one next
        public void Want(Track t)
        {
            if (t == null || Has(t.Id)) return;
            lock (gate) { urgent.Remove(t.Id); urgent.AddFirst(t.Id); }
            Kick();
        }

        public void Forget(long id) { lock (gate) { cache.Remove(id); done.Remove(id); } lib.Db.Exec("DELETE FROM sound WHERE track_id=?", id); }

        public void Kick()
        {
            lock (gate)
            {
                if (worker != null && worker.IsAlive) { Monitor.PulseAll(gate); return; }
                stop = false;
                worker = new Thread(Run) { IsBackground = true, Priority = ThreadPriority.Lowest, Name = "listen" };
                worker.Start();
            }
        }

        public void Stop() { stop = true; lock (gate) Monitor.PulseAll(gate); }

        Track Next()
        {
            lock (gate)
            {
                while (urgent.Count > 0)
                {
                    var id = urgent.First.Value; urgent.RemoveFirst();
                    if (!done.Contains(id) && lib.ById.TryGetValue(id, out var u)) return u;
                }
                if (!Background) return null;
                var tried = failedRecently;
                return lib.Tracks.Where(t => !done.Contains(t.Id) && !tried.Contains(t.Id)).OrderByDescending(t => t.Plays).ThenByDescending(t => t.Added).FirstOrDefault();
            }
        }

        readonly HashSet<long> failedRecently = new HashSet<long>();

        void Run()
        {
            while (!stop)
            {
                Track t;
                try { t = Next(); } catch { t = null; }
                if (t == null)
                {
                    lock (gate) { Monitor.Wait(gate, 30000); }
                    continue;
                }
                Current = t.Name;
                var sw = Stopwatch.StartNew();
                try
                {
                    var s = SongSound.Analyze(t.Path, t.Id, () => stop);
                    if (s == null) break;
                    Save(t, s, null);
                }
                catch (Exception e)
                {
                    App.Log("listening to " + t.Path + ": " + e.Message);
                    Save(t, null, e.Message);
                    lock (gate) failedRecently.Add(t.Id);
                }
                Current = "";
                var id = t.Id;
                if (ui != null) ui.Post(_ => Ready?.Invoke(id), null);
                // stay gentle: rest about as long as the work took
                int rest = (int)Math.Min(3000, Math.Max(150, sw.ElapsedMilliseconds));
                lock (gate) { if (urgent.Count == 0 && !stop) Monitor.Wait(gate, rest); }
            }
        }

        void Save(Track t, SongSound s, string err)
        {
            byte[] pb = null;
            if (s != null) { pb = new byte[s.Print.Length * 4]; Buffer.BlockCopy(s.Print, 0, pb, 0, pb.Length); }
            lib.Db.Exec("INSERT OR REPLACE INTO sound (track_id,size,mtime,version,length,bpm,key,peaks,env,print,err,ts,loud) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?)",
                t.Id, t.Size, t.Mtime, SongSound.Version, s?.Length ?? 0, s?.Bpm ?? 0, s?.Key ?? "", s?.Peaks, s?.Env, pb, err ?? "", Library.Now, s == null || double.IsNaN(s.Loud) ? (object)null : s.Loud);
            lock (gate) { done.Add(t.Id); if (s != null) { cache[t.Id] = s; } else cache.Remove(t.Id); }
        }

        // what's known for each song, for lists (bpm, key)
        public Dictionary<long, (double bpm, string key)> Summary()
        {
            var d = new Dictionary<long, (double, string)>();
            foreach (var r in lib.Db.Rows("SELECT track_id,bpm,key FROM sound WHERE err=''")) d[Conv.L(r[0])] = (Conv.D(r[1]), Conv.S(r[2]));
            return d;
        }
    }
}
