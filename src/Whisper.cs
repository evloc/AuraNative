using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Aura
{
    // Speech recognition with whisper.cpp (through aura_whisper.dll, which ships next to AURA).
    // Used to time lyrics to the song (auto-sync) or to write them down from scratch (transcribe).
    static class Whisper
    {
        // ---------- the models ----------
        public sealed class Model
        {
            public string Name, Label; public long Size; public int Dtw;
            public string File => Path.Combine(Dir, "ggml-" + Name + ".bin");
            public bool Ready => System.IO.File.Exists(File);
        }
        public static readonly Model[] Models =
        {
            new Model { Name = "base", Label = "Fast", Size = 147_951_465, Dtw = 6 },
            new Model { Name = "small", Label = "Better", Size = 487_601_967, Dtw = 8 },
            new Model { Name = "medium", Label = "Best (slow)", Size = 1_533_763_059, Dtw = 10 },
            new Model { Name = "large-v3-turbo", Label = "Best, faster than medium", Size = 1_624_555_275, Dtw = 14 },
        };
        public static string Dir => Path.Combine(App.DataDir, "models");
        public static string BaseUrl = Environment.GetEnvironmentVariable("AURA_MODEL_URL") ?? "https://huggingface.co/ggerganov/whisper.cpp/resolve/main";
        public static Model Find(string name) => Models.FirstOrDefault(m => m.Name == name) ?? Models[0];

        // downloads a model; progress 0..1
        public static void Download(Model m, Action<double> progress, Func<bool> cancelled)
        {
            Directory.CreateDirectory(Dir);
            var part = m.File + ".part";
            var ua = Net.AppUA;        // (also switches on TLS 1.2)
            var req = (HttpWebRequest)WebRequest.Create(BaseUrl.TrimEnd('/') + "/ggml-" + m.Name + ".bin");
            req.UserAgent = ua;
            req.Timeout = 30000; req.ReadWriteTimeout = 60000;
            long have = System.IO.File.Exists(part) ? new FileInfo(part).Length : 0, total = 0;
            if (have > 0) req.AddRange(have);
            using (var resp = (HttpWebResponse)req.GetResponse())
            {
                bool resumed = resp.StatusCode == HttpStatusCode.PartialContent;
                total = resp.ContentLength > 0 ? (resumed ? have : 0) + resp.ContentLength : 0;
                if (!resumed) have = 0;
                using (var src = resp.GetResponseStream())
                using (var dst = new FileStream(part, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write))
                {
                    var buf = new byte[1 << 16];
                    int n;
                    while ((n = src.Read(buf, 0, buf.Length)) > 0)
                    {
                        if (cancelled != null && cancelled()) throw new OperationCanceledException();
                        dst.Write(buf, 0, n); have += n;
                        progress?.Invoke(total > 0 ? (double)have / total : 0);
                    }
                }
            }
            if (total > 0 && new FileInfo(part).Length < total) throw new IOException("the download stopped early — try again");
            if (System.IO.File.Exists(m.File)) System.IO.File.Delete(m.File);
            System.IO.File.Move(part, m.File);
        }

        // ---------- the engine ----------
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr OpenFn(byte[] path, int dtw);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void CloseFn(IntPtr h);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void ProgressFn(int percent);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int StopFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int RunFn(IntPtr h, float[] samples, int n, byte[] lang, byte[] prompt, int beam, int threads, ProgressFn progress, StopFn stop);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr TextFn(IntPtr h);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr VersionFn();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibraryW(string path);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr lib, string name);
        [DllImport("kernel32.dll")] static extern bool IsProcessorFeaturePresent(int feature);
        [DllImport("libdl.so.2")] static extern IntPtr dlopen(string path, int flags);
        [DllImport("libdl.so.2")] static extern IntPtr dlsym(IntPtr lib, string name);

        static IntPtr lib;
        static OpenFn open; static CloseFn close; static RunFn run; static TextFn result, language; static VersionFn version;
        public static string LoadError = "";

        static string LibFile()
        {
            var env = Environment.GetEnvironmentVariable("AURA_WHISPER_LIB");
            if (env != null) return env;
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            bool avx2 = false;
            try { avx2 = IsProcessorFeaturePresent(40); } catch { }       // PF_AVX2_INSTRUCTIONS_AVAILABLE
            var fast = Path.Combine(dir, "aura_whisper.dll");
            var basic = Path.Combine(dir, "aura_whisper_basic.dll");
            return avx2 && System.IO.File.Exists(fast) ? fast : System.IO.File.Exists(basic) ? basic : fast;
        }

        public static bool Load()
        {
            if (lib != IntPtr.Zero) return true;
            var file = LibFile();
            try
            {
                if (!System.IO.File.Exists(file)) { LoadError = "the speech part of AURA (" + Path.GetFileName(file) + ") isn't installed next to AURA"; return false; }
                lib = App.IsWindows ? LoadLibraryW(file) : dlopen(file, 2);
                if (lib == IntPtr.Zero) { LoadError = "Windows couldn't load " + Path.GetFileName(file) + (App.IsWindows ? " (error " + Marshal.GetLastWin32Error() + ")" : ""); return false; }
                T Fn<T>(string name)
                {
                    var p = App.IsWindows ? GetProcAddress(lib, name) : dlsym(lib, name);
                    if (p == IntPtr.Zero) throw new EntryPointNotFoundException(name);
                    return (T)(object)Marshal.GetDelegateForFunctionPointer(p, typeof(T));
                }
                open = Fn<OpenFn>("aw_open"); close = Fn<CloseFn>("aw_close"); run = Fn<RunFn>("aw_run");
                result = Fn<TextFn>("aw_result"); language = Fn<TextFn>("aw_language"); version = Fn<VersionFn>("aw_version");
                App.Log("speech: " + Marshal.PtrToStringAnsi(version()) + " from " + file);
                return true;
            }
            catch (Exception e) { LoadError = e.Message; lib = IntPtr.Zero; return false; }
        }

        public static byte[] Z(string s) => Encoding.UTF8.GetBytes((s ?? "") + "\0");

        public sealed class Heard { public string Word; public double Start, End; public double P; }

        // what was sung, word by word
        public static List<Heard> Listen(string modelFile, int dtw, float[] pcm16k, string lang, string prompt, int beam, Action<double> progress, Func<bool> cancelled, out string detected, int maxThreads = 8)
        {
            CrashGuard.Doing("speech model", Path.GetFileName(modelFile) + ", " + (pcm16k.Length / 16000) + " s of sound, beam " + beam);
            try { return ListenNow(modelFile, dtw, pcm16k, lang, prompt, beam, progress, cancelled, out detected, maxThreads); }
            finally { CrashGuard.Doing("speech model", null); }
        }

        static List<Heard> ListenNow(string modelFile, int dtw, float[] pcm16k, string lang, string prompt, int beam, Action<double> progress, Func<bool> cancelled, out string detected, int maxThreads)
        {
            detected = "";
            if (!Load()) throw new InvalidOperationException(LoadError);
            if (Environment.GetEnvironmentVariable("AURA_WHISPER_NODTW") != null) dtw = 0;     // test models don't match the real presets
            var h = open(Z(modelFile), dtw);
            if (h == IntPtr.Zero) throw new IOException("the speech model couldn't be opened (it may be damaged — delete it in Settings and download it again)");
            try
            {
                ProgressFn pf = p => progress?.Invoke(p / 100.0);
                StopFn sf = () => cancelled != null && cancelled() ? 1 : 0;
                int threads = Math.Max(1, Math.Min(maxThreads, Environment.ProcessorCount - 1));
                int rc = run(h, pcm16k, pcm16k.Length, Z(string.IsNullOrEmpty(lang) ? "auto" : lang), Z(prompt ?? ""), beam, threads, pf, sf);
                GC.KeepAlive(pf); GC.KeepAlive(sf);
                if (cancelled != null && cancelled()) throw new OperationCanceledException();
                if (rc != 0) throw new IOException("speech recognition failed (code " + rc + ")");
                detected = Utf8(language(h));
                return Words(Utf8(result(h)));
            }
            finally { close(h); }
        }

        static string Utf8(IntPtr p)
        {
            if (p == IntPtr.Zero) return "";
            int n = 0; while (Marshal.ReadByte(p, n) != 0) n++;
            var b = new byte[n]; Marshal.Copy(p, b, 0, n);
            return Encoding.UTF8.GetString(b);
        }

        // tokens -> words (a token starting with a space starts a new word)
        public static List<Heard> Words(string raw)
        {
            var words = new List<Heard>();
            Heard cur = null; int seg = -1;
            var tokBytes = new List<byte>();
            foreach (var line in raw.Split('\n'))
            {
                var f = line.Split('\t');
                if (f.Length < 6) continue;
                int s = int.Parse(f[0], CultureInfo.InvariantCulture);
                double t0 = D(f[1]), t1 = D(f[2]), dtw = D(f[3]), p = D(f[4]);
                string text = string.Join("\t", f.Skip(5));
                if (text.StartsWith("[") && text.EndsWith("]")) continue;          // [Music], [BLANK_AUDIO]
                bool starts = cur == null || s != seg || text.StartsWith(" ");
                double at = dtw >= 0 ? dtw : t0;
                if (starts)
                {
                    if (cur != null && cur.Word.Trim().Length > 0) words.Add(cur);
                    cur = new Heard { Word = text.Trim(), Start = at, End = Math.Max(t1, at), P = p };
                }
                else { cur.Word += text; cur.End = Math.Max(cur.End, t1); cur.P = Math.Min(cur.P, p); }
                seg = s;
            }
            if (cur != null && cur.Word.Trim().Length > 0) words.Add(cur);
            // a word made only of punctuation belongs to the one before
            var outp = new List<Heard>();
            foreach (var w in words)
            {
                if (outp.Count > 0 && !w.Word.Any(char.IsLetterOrDigit)) { outp[outp.Count - 1].Word += w.Word; continue; }
                outp.Add(w);
            }
            return outp;
        }

        static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);
    }

    // Laying known lyrics over what was heard, and turning heard words into lines.
    static class Sync
    {
        public sealed class Result
        {
            public List<LyricLine> Lines = new List<LyricLine>();
            public List<bool> Guessed = new List<bool>();
            public double Confidence;
            public string Note = "";
        }

        static List<string> Tok(string text)
        {
            var t = (text ?? "").Normalize(NormalizationForm.FormKD).ToLowerInvariant().Replace("’", "'").Replace("'", "");
            var sb = new StringBuilder();
            foreach (var c in t) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            return Regex.Matches(sb.ToString(), "[a-z0-9]+").Cast<Match>().Select(m => m.Value).ToList();
        }

        static double Ratio(string a, string b)
        {
            // like Python's SequenceMatcher ratio, close enough for short words: 2*LCS / total
            int n = a.Length, m = b.Length;
            var d = new int[n + 1, m + 1];
            for (int i = 1; i <= n; i++) for (int j = 1; j <= m; j++) d[i, j] = a[i - 1] == b[j - 1] ? d[i - 1, j - 1] + 1 : Math.Max(d[i - 1, j], d[i, j - 1]);
            return 2.0 * d[n, m] / Math.Max(1, n + m);
        }

        // lines: the lyrics. words: what was heard. (ported from the first AURA)
        public static Result Align(List<string> lines, List<Whisper.Heard> words)
        {
            var res = new Result();
            var disp = lines.Select(l => l.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)).ToList();
            var L = new List<(string tk, int line, int k, int wi)>();
            for (int i = 0; i < disp.Count; i++)
            {
                int k = 0;
                for (int wi = 0; wi < disp[i].Length; wi++) foreach (var tk in Tok(disp[i][wi])) L.Add((tk, i, k++, wi));
            }
            var W = new List<(string tk, double st, double en)>();
            foreach (var w in words) foreach (var tk in Tok(w.Word)) W.Add((tk, w.Start, w.End));
            int n = L.Count, m = W.Count;
            if (lines.Count == 0) return res;
            if (n == 0 || m == 0)
            {
                foreach (var l in lines) { res.Lines.Add(new LyricLine { T = -1, Text = l }); res.Guessed.Add(true); }
                return res;
            }
            var cache = new Dictionary<(string, string), double>();
            double Score(string a, string b)
            {
                if (a == b) return 2;
                if (!cache.TryGetValue((a, b), out var v)) cache[(a, b)] = v = a.Length > 2 && b.Length > 2 && Ratio(a, b) >= 0.75 ? 1 : -1;
                return v;
            }
            const double GAP = -0.6;
            var prev = new double[m + 1];
            var back = new byte[n][];
            for (int i = 1; i <= n; i++)
            {
                var cur = new double[m + 1];
                cur[0] = prev[0] + GAP;
                var bi = new byte[m + 1]; bi[0] = 1;
                string a = L[i - 1].tk; bool last = i == n;
                for (int j = 1; j <= m; j++)
                {
                    double d = prev[j - 1] + Score(a, W[j - 1].tk), u = prev[j] + GAP, lf = cur[j - 1] + (last ? 0 : GAP);
                    if (d >= u && d >= lf) { cur[j] = d; bi[j] = 0; }
                    else if (u >= lf) { cur[j] = u; bi[j] = 1; }
                    else { cur[j] = lf; bi[j] = 2; }
                }
                back[i - 1] = bi; prev = cur;
            }
            int ii = n, jj = 0; double best = double.MinValue;
            for (int j = 0; j <= m; j++) if (prev[j] > best) { best = prev[j]; jj = j; }
            var match = new Dictionary<int, int>();
            while (ii > 0 && jj > 0)
            {
                var mv = back[ii - 1][jj];
                if (mv == 0) { if (Score(L[ii - 1].tk, W[jj - 1].tk) > 0) match[ii - 1] = jj - 1; ii--; jj--; }
                else if (mv == 1) ii--;
                else jj--;
            }
            int nl = lines.Count;
            var perLine = new double?[nl]; var counts = new int[nl]; var matchedIn = new int[nl];
            var wtime = Enumerable.Range(0, nl).Select(_ => new Dictionary<int, double>()).ToList();
            var lastEnd = new double?[nl];
            for (int li = 0; li < L.Count; li++)
            {
                var (tk, line, k, wi) = L[li];
                counts[line]++;
                if (match.TryGetValue(li, out var wj))
                {
                    double st = W[wj].st, en = W[wj].en;
                    matchedIn[line]++;
                    if (perLine[line] == null) perLine[line] = Math.Max(0, st - k * 0.28);
                    if (!wtime[line].ContainsKey(wi)) wtime[line][wi] = st;
                    lastEnd[line] = en;
                }
            }
            var times = perLine.ToArray();
            var anchors = Enumerable.Range(0, nl).Where(i => times[i] != null).ToList();
            var guessed = Enumerable.Range(0, nl).Select(i => times[i] == null || (counts[i] > 0 && (double)matchedIn[i] / counts[i] < 0.3)).ToList();
            if (anchors.Count == 0)
            {
                foreach (var l in lines) { res.Lines.Add(new LyricLine { T = -1, Text = l }); res.Guessed.Add(true); }
                return res;
            }
            int first = anchors[0], lastA = anchors[anchors.Count - 1];
            for (int i = first - 1; i >= 0; i--) times[i] = Math.Max(0, times[i + 1].Value - 2.5);
            for (int i = lastA + 1; i < nl; i++) times[i] = times[i - 1].Value + 2.5;
            for (int x = 0; x + 1 < anchors.Count; x++)
            {
                int a = anchors[x], b = anchors[x + 1];
                if (b - a <= 1) continue;
                double span = Math.Max(0, times[b].Value - times[a].Value);
                var w = Enumerable.Range(a, b - a).Select(k => (double)Math.Max(1, counts[k])).ToList();
                double tot = w.Sum(), acc = 0;
                for (int k = a + 1; k < b; k++) { acc += w[k - 1 - a]; times[k] = times[a].Value + span * acc / tot; }
            }
            for (int i = 1; i < nl; i++) if (times[i] < times[i - 1] + 0.2) times[i] = times[i - 1] + 0.2;
            for (int i = 0; i < nl; i++)
            {
                double? nxt = i + 1 < nl ? times[i + 1] : null;
                double? end = lastEnd[i] != null && lastEnd[i] > times[i] ? lastEnd[i] : null;
                var (ws, e2) = Spread(disp[i], wtime[i], times[i].Value, nxt, end);
                res.Lines.Add(new LyricLine { T = Math.Round(times[i].Value, 2), Text = string.Join(" ", disp[i]), Words = ws, End = e2 });
                res.Guessed.Add(guessed[i]);
            }
            res.Confidence = Math.Round((double)match.Count / n, 3);
            return res;
        }

        // times for every word of a line: the heard ones where they were heard, the rest in between by length
        static (List<(double t, string w)>, double) Spread(string[] dws, Dictionary<int, double> known, double start, double? nxt, double? end)
        {
            if (dws.Length == 0) return (null, -1);
            double? limit = nxt != null ? nxt - 0.05 : null;
            double e = end ?? Math.Max((known.Count > 0 ? known.Values.Max() : start) + 0.4, start + 0.32 * dws.Length);
            if (limit != null) e = Math.Min(e, limit.Value);
            e = Math.Max(e, start + 0.1);
            var pts = new SortedDictionary<int, double> { [0] = start };
            foreach (var kv in known) if (kv.Key > 0 && kv.Key < dws.Length && kv.Value > start && kv.Value < e) pts[kv.Key] = kv.Value;
            pts[dws.Length] = e;
            var clean = new List<int>(); double lastT = -1;
            foreach (var k in pts.Keys) if (pts[k] > lastT || k == 0) { clean.Add(k); lastT = pts[k]; }
            if (clean[clean.Count - 1] != dws.Length) { clean.Add(dws.Length); pts[dws.Length] = Math.Max(e, lastT + 0.1); }
            var ts = new double[dws.Length];
            for (int x = 0; x + 1 < clean.Count; x++)
            {
                int a = clean[x], b = clean[x + 1];
                var w = Enumerable.Range(a, b - a).Select(k => (double)Math.Max(1, dws[k].Length)).ToList();
                double tot = w.Sum(), acc = 0;
                for (int k = a; k < b; k++) { ts[k] = pts[a] + (pts[b] - pts[a]) * acc / tot; acc += w[k - a]; }
            }
            return (ts.Select((t, i) => (Math.Round(t, 2), dws[i])).ToList(), Math.Round(pts[dws.Length], 2));
        }

        // heard words -> lyric lines, by pauses, punctuation and length (for transcribing)
        public static Result Lines(List<Whisper.Heard> words, int maxWords = 10, double gap = 0.6)
        {
            var res = new Result();
            var cur = new List<Whisper.Heard>();
            void Flush()
            {
                if (cur.Count == 0) return;
                var parts = cur.Select(w => w.Word.Trim()).Where(w => w.Length > 0).ToList();
                if (parts.Count > 0)
                {
                    parts[0] = char.ToUpper(parts[0][0]) + parts[0].Substring(1);
                    var line = new LyricLine { T = Math.Round(cur[0].Start, 2), Text = string.Join(" ", parts), End = Math.Round(cur[cur.Count - 1].End, 2) };
                    line.Words = cur.Where(w => w.Word.Trim().Length > 0).Select((w, i) => (Math.Round(w.Start, 2), parts[i])).ToList();
                    res.Lines.Add(line); res.Guessed.Add(false);
                }
                cur.Clear();
            }
            double? lastEnd = null;
            foreach (var w in words)
            {
                var t = w.Word.Trim();
                if (t.Length == 0) continue;
                double pause = lastEnd != null ? w.Start - lastEnd.Value : 0;
                if (cur.Count > 0 && (pause > gap || cur.Count >= maxWords || (cur.Count >= 5 && pause > 0.3) || (cur.Count >= 6 && cur[cur.Count - 1].Word.TrimEnd().EndsWith(","))))
                    Flush();
                cur.Add(w);
                lastEnd = w.End;
                if (".?!".IndexOf(t[t.Length - 1]) >= 0 && cur.Count >= 3) Flush();
            }
            Flush();
            // keep the line times going forward
            for (int i = 1; i < res.Lines.Count; i++) if (res.Lines[i].T < res.Lines[i - 1].T + 0.2) res.Lines[i].T = res.Lines[i - 1].T + 0.2;
            res.Confidence = words.Count == 0 ? 0 : Math.Round(words.Average(w => w.P), 3);
            return res;
        }

        // LRC text, and word timings as enhanced LRC (what AURA stores as AURA_WORDS)
        public static string ToLrc(List<LyricLine> lines) => Lrc.ToLrc(lines.Select(l => (l.T, l.Text)));

        public static string ToWords(List<LyricLine> lines)
        {
            var sb = new StringBuilder();
            foreach (var l in lines)
            {
                sb.Append('[').Append(Ts(l.T)).Append(']');
                if (l.Words != null)
                {
                    foreach (var (t, w) in l.Words) sb.Append('<').Append(Ts(t)).Append('>').Append(w).Append(' ');
                    if (l.End > 0) sb.Append('<').Append(Ts(l.End)).Append('>');
                }
                sb.Append('\n');
            }
            return sb.ToString().TrimEnd();
        }

        static string Ts(double t)
        {
            t = Math.Max(0, t);
            int m = (int)(t / 60);
            return m.ToString("00") + ":" + (t - m * 60).ToString("00.00", CultureInfo.InvariantCulture);
        }
    }
}
