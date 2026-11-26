using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;

namespace Aura
{
    // Taking the beat away before listening: an MDX-Net vocal model (the kind UVR uses), run with onnxruntime.
    // The song is cut into ~6 second pieces, each is turned into a spectrogram, the model keeps the voice,
    // and the pieces are turned back into sound. Same math as the first AURA (app.py: mdx_separate).
    static class Separator
    {
        public const string ModelName = "UVR-MDX-NET-Voc_FT.onnx";
        public const long ModelSize = 66_762_490;
        const double Compensate = 1.021;
        static readonly string[] Urls =
        {
            Environment.GetEnvironmentVariable("AURA_VOCAL_URL") ?? "https://github.com/TRvlvr/model_repo/releases/download/all_public_uvr_models/UVR-MDX-NET-Voc_FT.onnx",
            "https://huggingface.co/seanghay/uvr_models/resolve/main/UVR-MDX-NET-Voc_FT.onnx",
        };

        public static string ModelFile => Path.Combine(Whisper.Dir, ModelName);
        public static bool ModelReady { get { try { return File.Exists(ModelFile) && new FileInfo(ModelFile).Length > 20_000_000; } catch { return false; } } }
        public static bool Ready => ModelReady && File.Exists(LibFile());
        public static bool Installed => File.Exists(LibFile());
        public static string SizeText => ModelReady ? (new FileInfo(ModelFile).Length / 1e6).ToString("0") + " MB" : "";
        public static string LoadError = "";

        public static void Download(Action<double> progress, Func<bool> cancelled)
        {
            Directory.CreateDirectory(Whisper.Dir);
            var part = ModelFile + ".part";
            Exception last = null;
            foreach (var url in Urls)
            {
                try
                {
                    var ua = Net.AppUA;
                    var req = (HttpWebRequest)WebRequest.Create(url);
                    req.UserAgent = ua; req.Timeout = 30000; req.ReadWriteTimeout = 60000;
                    long have = 0, total;
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    {
                        total = resp.ContentLength > 0 ? resp.ContentLength : ModelSize;
                        using (var src = resp.GetResponseStream())
                        using (var dst = new FileStream(part, FileMode.Create, FileAccess.Write))
                        {
                            var buf = new byte[1 << 16]; int n;
                            while ((n = src.Read(buf, 0, buf.Length)) > 0)
                            {
                                if (cancelled != null && cancelled()) throw new OperationCanceledException();
                                dst.Write(buf, 0, n); have += n;
                                progress?.Invoke(Math.Min(1, (double)have / total));
                            }
                        }
                    }
                    if (new FileInfo(part).Length < 20_000_000) throw new IOException("the download was cut short");
                    if (File.Exists(ModelFile)) File.Delete(ModelFile);
                    File.Move(part, ModelFile);
                    return;
                }
                catch (OperationCanceledException) { try { File.Delete(part); } catch { } throw; }
                catch (Exception e) { last = e; try { File.Delete(part); } catch { } }
            }
            throw new IOException("couldn't download the vocal model (" + last?.Message + ")");
        }

        // ---------- onnxruntime, through its C API (the function table OrtGetApiBase hands out) ----------
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibraryExW(string path, IntPtr file, int flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr lib, string name);
        [DllImport("libdl.so.2")] static extern IntPtr dlopen(string path, int flags);
        [DllImport("libdl.so.2")] static extern IntPtr dlsym(IntPtr lib, string name);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr GetApiBaseFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr GetApiFn(uint version);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr ErrorMessageFn(IntPtr status);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void ReleaseFn(IntPtr p);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr CreateEnvFn(int level, byte[] id, out IntPtr env);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr CreateOptionsFn(out IntPtr opts);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr SetIntFn(IntPtr opts, int n);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr CreateSessionFromArrayFn(IntPtr env, byte[] data, UIntPtr len, IntPtr opts, out IntPtr session);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr GetAllocatorFn(out IntPtr alloc);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr GetNameFn(IntPtr session, UIntPtr index, IntPtr alloc, out IntPtr name);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr AllocatorFreeFn(IntPtr alloc, IntPtr p);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr GetTypeInfoFn(IntPtr session, UIntPtr index, out IntPtr info);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr CastFn(IntPtr typeInfo, out IntPtr tensorInfo);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr DimCountFn(IntPtr info, out UIntPtr n);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr DimsFn(IntPtr info, [Out] long[] dims, UIntPtr n);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr CpuInfoFn(int allocType, int memType, out IntPtr info);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr TensorFn(IntPtr info, IntPtr data, UIntPtr bytes, long[] shape, UIntPtr shapeLen, int type, out IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr RunFn(IntPtr session, IntPtr runOpts, IntPtr[] inNames, IntPtr[] inputs, UIntPtr nIn, IntPtr[] outNames, UIntPtr nOut, [In, Out] IntPtr[] outputs);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr DataFn(IntPtr value, out IntPtr data);

        // positions in the OrtApi table (onnxruntime_c_api.h, API version 1 onward — new functions are only ever added at the end)
        const int iGetErrorMessage = 2, iCreateEnv = 3, iCreateSessionFromArray = 8, iRun = 9, iCreateSessionOptions = 10,
            iSetGraphOptimization = 23, iSetIntraOpThreads = 24, iGetInputTypeInfo = 33, iGetInputName = 36, iGetOutputName = 37,
            iCreateTensorWithData = 49, iGetTensorData = 51, iCastTypeInfo = 55, iGetDimCount = 61, iGetDims = 62, iCreateCpuMemoryInfo = 69,
            iAllocatorFree = 76, iGetAllocator = 78, iReleaseEnv = 92, iReleaseStatus = 93, iReleaseMemoryInfo = 94, iReleaseSession = 95,
            iReleaseValue = 96, iReleaseTypeInfo = 98, iReleaseSessionOptions = 100;

        static IntPtr api, apiBase;
        static T Fn<T>(int i) => (T)(object)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(api, i * IntPtr.Size), typeof(T));
        static ErrorMessageFn errMsg; static ReleaseFn releaseStatus;

        static void Ok(IntPtr status, string what)
        {
            if (status == IntPtr.Zero) return;
            var msg = Marshal.PtrToStringAnsi(errMsg(status));
            releaseStatus(status);
            throw new InvalidOperationException(what + ": " + msg);
        }

        public static string LibFile()
        {
            var env = Environment.GetEnvironmentVariable("AURA_ORT_LIB");
            if (env != null) return env;
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "onnx", "onnxruntime.dll");
        }

        static bool Load()
        {
            if (api != IntPtr.Zero) return true;
            var file = LibFile();
            if (!File.Exists(file)) { LoadError = "the vocal part of AURA (onnx\\onnxruntime.dll) isn't installed"; return false; }
            // LOAD_WITH_ALTERED_SEARCH_PATH: the Visual C++ files it needs are in the same folder
            var lib = App.IsWindows ? LoadLibraryExW(file, IntPtr.Zero, 8) : dlopen(file, 2);
            if (lib == IntPtr.Zero) { LoadError = "Windows couldn't load onnxruntime.dll" + (App.IsWindows ? " (error " + Marshal.GetLastWin32Error() + ")" : ""); return false; }
            var p = App.IsWindows ? GetProcAddress(lib, "OrtGetApiBase") : dlsym(lib, "OrtGetApiBase");
            if (p == IntPtr.Zero) { LoadError = "onnxruntime.dll is broken"; return false; }
            var baseApi = ((GetApiBaseFn)Marshal.GetDelegateForFunctionPointer(p, typeof(GetApiBaseFn)))();
            apiBase = baseApi;
            var getApi = (GetApiFn)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(baseApi), typeof(GetApiFn));
            api = getApi(17);       // any onnxruntime from 1.17 on
            if (api == IntPtr.Zero) { LoadError = "this onnxruntime.dll is too old"; return false; }
            errMsg = Fn<ErrorMessageFn>(iGetErrorMessage); releaseStatus = Fn<ReleaseFn>(iReleaseStatus);
            return true;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr CreateStatusFn(int code, byte[] msg);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr VersionFn();

        // loads onnxruntime and checks its function table; with the model there, also runs it on a few seconds of sound
        public static string SelfTest()
        {
            try
            {
                if (!Load()) return LoadError;
                var st = Fn<CreateStatusFn>(0)(2, Whisper.Z("aura check"));
                var msg = Marshal.PtrToStringAnsi(errMsg(st)); releaseStatus(st);
                if (msg != "aura check") return "the onnxruntime function table doesn't match";
                var verFn = Marshal.ReadIntPtr(apiBase, IntPtr.Size);
                var ver = Marshal.PtrToStringAnsi(((VersionFn)Marshal.GetDelegateForFunctionPointer(verFn, typeof(VersionFn)))());
                Ok(Fn<CreateEnvFn>(iCreateEnv)(3, Whisper.Z("aura"), out var env), "onnxruntime");
                Fn<ReleaseFn>(iReleaseEnv)(env);
                string extra = "";
                if (ModelReady)
                {
                    using (var s = Open()) extra = ", model " + s.DimF + "x" + s.DimT;
                }
                return "ok (onnxruntime " + ver + extra + ")";
            }
            catch (Exception e) { return e.Message; }
        }

        sealed class Session : IDisposable
        {
            public IntPtr Env, Opts, Handle, Mem;
            public string InName, OutName;
            public int DimF, DimT;
            public void Dispose()
            {
                if (Handle != IntPtr.Zero) Fn<ReleaseFn>(iReleaseSession)(Handle);
                if (Opts != IntPtr.Zero) Fn<ReleaseFn>(iReleaseSessionOptions)(Opts);
                if (Mem != IntPtr.Zero) Fn<ReleaseFn>(iReleaseMemoryInfo)(Mem);
                if (Env != IntPtr.Zero) Fn<ReleaseFn>(iReleaseEnv)(Env);
                Handle = Opts = Mem = Env = IntPtr.Zero;
            }
        }

        static Session Open()
        {
            var s = new Session();
            try
            {
                Ok(Fn<CreateEnvFn>(iCreateEnv)(3, Whisper.Z("aura"), out s.Env), "onnxruntime");
                Ok(Fn<CreateOptionsFn>(iCreateSessionOptions)(out s.Opts), "options");
                int cores = Environment.ProcessorCount;
                Ok(Fn<SetIntFn>(iSetIntraOpThreads)(s.Opts, Math.Max(2, Math.Min(12, cores > 8 ? cores / 2 : cores))), "threads");
                Ok(Fn<SetIntFn>(iSetGraphOptimization)(s.Opts, 99), "optimization");
                var bytes = File.ReadAllBytes(ModelFile);
                Ok(Fn<CreateSessionFromArrayFn>(iCreateSessionFromArray)(s.Env, bytes, (UIntPtr)bytes.Length, s.Opts, out s.Handle), "the vocal model");
                Ok(Fn<GetAllocatorFn>(iGetAllocator)(out var alloc), "allocator");
                string Name(int fn)
                {
                    Ok(Fn<GetNameFn>(fn)(s.Handle, UIntPtr.Zero, alloc, out var n), "name");
                    var r = Marshal.PtrToStringAnsi(n);
                    Fn<AllocatorFreeFn>(iAllocatorFree)(alloc, n);
                    return r;
                }
                s.InName = Name(iGetInputName); s.OutName = Name(iGetOutputName);
                Ok(Fn<GetTypeInfoFn>(iGetInputTypeInfo)(s.Handle, UIntPtr.Zero, out var ti), "input");
                try
                {
                    Ok(Fn<CastFn>(iCastTypeInfo)(ti, out var tinfo), "input");          // owned by ti
                    Ok(Fn<DimCountFn>(iGetDimCount)(tinfo, out var nd), "input");
                    var dims = new long[(int)nd];
                    Ok(Fn<DimsFn>(iGetDims)(tinfo, dims, nd), "input");
                    if (dims.Length != 4 || dims[1] != 4) throw new InvalidOperationException("this isn't an MDX-Net model");
                    s.DimF = (int)dims[2]; s.DimT = (int)dims[3];
                }
                finally { Fn<ReleaseFn>(iReleaseTypeInfo)(ti); }
                Ok(Fn<CpuInfoFn>(iCreateCpuMemoryInfo)(1, 0, out s.Mem), "memory");
                return s;
            }
            catch { s.Dispose(); throw; }
        }

        static void Infer(Session s, float[] input, float[] output)
        {
            var h = GCHandle.Alloc(input, GCHandleType.Pinned);
            IntPtr inVal = IntPtr.Zero; var outs = new IntPtr[1];
            IntPtr inName = Marshal.StringToHGlobalAnsi(s.InName), outName = Marshal.StringToHGlobalAnsi(s.OutName);
            try
            {
                var shape = new long[] { 1, 4, s.DimF, s.DimT };
                Ok(Fn<TensorFn>(iCreateTensorWithData)(s.Mem, h.AddrOfPinnedObject(), (UIntPtr)(input.Length * 4), shape, (UIntPtr)4, 1, out inVal), "input");
                Ok(Fn<RunFn>(iRun)(s.Handle, IntPtr.Zero, new[] { inName }, new[] { inVal }, (UIntPtr)1, new[] { outName }, (UIntPtr)1, outs), "the vocal model");
                Ok(Fn<DataFn>(iGetTensorData)(outs[0], out var data), "output");
                Marshal.Copy(data, output, 0, output.Length);
            }
            finally
            {
                if (outs[0] != IntPtr.Zero) Fn<ReleaseFn>(iReleaseValue)(outs[0]);
                if (inVal != IntPtr.Zero) Fn<ReleaseFn>(iReleaseValue)(inVal);
                Marshal.FreeHGlobal(inName); Marshal.FreeHGlobal(outName);
                h.Free();
            }
        }

        // ---------- the whole thing ----------
        // the singing on its own: mono at 16 kHz (for Whisper). Throws when it can't be done well.
        public static float[] Vocals(string path, long id, double length, double maxSeconds, Action<double> progress, Func<bool> cancelled)
        {
            CrashGuard.Doing("taking out vocals", Path.GetFileName(path));
            try { return VocalsNow(path, id, length, maxSeconds, progress, cancelled); }
            finally { CrashGuard.Doing("taking out vocals", null); }
        }

        static float[] VocalsNow(string path, long id, double length, double maxSeconds, Action<double> progress, Func<bool> cancelled)
        {
            if (!Load()) throw new InvalidOperationException(LoadError);
            if (!ModelReady) Download(p => progress?.Invoke(-1 - p), cancelled);
            using (var s = Open())
            {
                int dimF = s.DimF, dimT = s.DimT;
                int nfft = dimF == 3072 ? 7680 : dimF == 2048 ? 6144 : dimF == 2560 ? 5120 : dimF * 5 / 2;
                const int hop = 1024;
                int chunk = hop * (dimT - 1), trim = nfft / 2, gen = chunk - 2 * trim;
                var stft = new Stft(nfft, hop);
                var input = new float[4 * dimF * dimT];
                var output = new float[4 * dimF * dimT];
                var winL = new float[chunk]; var winR = new float[chunk];

                // the song, in a sliding window: [trim zeros][song...]; each step uses `chunk` samples and moves on by `gen`
                var L = new List<float>(chunk * 2); var R = new List<float>(chunk * 2);
                for (int i = 0; i < trim; i++) { L.Add(0); R.Add(0); }
                long total = 0, made = 0, limit = (long)(44100 * maxSeconds);
                double em = 0, ev = 0, er = 0, emv = 0;
                var outp = new List<float>(16000 * 240);
                var down = new Down16k();
                if (length <= 1) length = 240;

                void Step()
                {
                    L.CopyTo(0, winL, 0, chunk); R.CopyTo(0, winR, 0, chunk);
                    stft.Forward(winL, winR, input, dimF, dimT);
                    Infer(s, input, output);
                    stft.Inverse(output, dimF, dimT, out var vl, out var vr);     // `chunk` samples each
                    // the middle part (gen samples) lines up with the song at `made`
                    for (int i = 0; i < gen && made < total; i++, made++)
                    {
                        float v = (float)((vl[trim + i] + vr[trim + i]) * 0.5 * Compensate);
                        float m = (L[trim + i] + R[trim + i]) * 0.5f;
                        em += m * m; ev += v * v; er += (m - v) * (m - v); emv += m * v;
                        down.Push(v, outp);
                    }
                    L.RemoveRange(0, gen); R.RemoveRange(0, gen);
                    progress?.Invoke(Math.Min(0.99, made / 44100.0 / Math.Min(length, maxSeconds)));
                    if (cancelled != null && cancelled()) throw new OperationCanceledException();
                }

                var rs = new Resample44k();
                Decoder.Read(path, id, (b, n, ch, rate) =>
                {
                    if (total >= limit) return;
                    rs.Push(b, n, ch, rate, (l, r) => { if (total < limit) { L.Add(l); R.Add(r); total++; } });
                    while (L.Count >= chunk) Step();
                }, () => (cancelled?.Invoke() ?? false) || total >= limit);
                if (cancelled != null && cancelled()) throw new OperationCanceledException();
                if (total < 44100) throw new InvalidOperationException("couldn't read the song");
                // the end: zeros so the last piece is whole
                while (made < total) { while (L.Count < chunk) { L.Add(0); R.Add(0); } Step(); }

                // a separated voice is a *part* of the song: if what came back isn't, don't use it
                double n0 = Math.Max(1, made);
                em /= n0; ev /= n0; er /= n0; emv /= n0;
                double corr = emv / (Math.Sqrt(em * ev) + 1e-12);
                if (double.IsNaN(ev) || em <= 0 || ev < em * 0.004 || er >= em * 1.02 || corr <= 0.25)
                    throw new InvalidOperationException("the separated vocals didn't pass the check");
                var y = outp.ToArray();
                float peak = 0; foreach (var v in y) peak = Math.Max(peak, Math.Abs(v));
                float k = Math.Min(4f, 0.9f / Math.Max(peak, 1e-6f));
                for (int i = 0; i < y.Length; i++) y[i] *= k;
                LastStats = "corr " + corr.ToString("0.00") + ", energy " + (ev / em).ToString("0.00");
                return y;
            }
        }
        public static string LastStats = "";

        // any rate, any channels -> stereo 44.1 kHz (straight-line in-between)
        sealed class Resample44k
        {
            double pos; float pl, pr; bool started;
            public void Push(float[] b, int n, int ch, int rate, Action<float, float> put)
            {
                double step = rate / 44100.0;
                for (int i = 0; i + ch <= n; i += ch)
                {
                    float l = b[i], r = ch > 1 ? b[i + 1] : b[i];
                    if (rate == 44100) { put(l, r); continue; }
                    if (!started) { pl = l; pr = r; started = true; pos = 0; }
                    // output samples that fall between the previous frame and this one
                    while (pos <= 1) { put(pl + (float)pos * (l - pl), pr + (float)pos * (r - pr)); pos += step; }
                    pos -= 1; pl = l; pr = r;
                }
            }
        }

        // 44.1 kHz -> 16 kHz with a gentle low-pass first
        sealed class Down16k
        {
            double pos; float lp1, lp2, acc; int cnt;
            readonly double step = 44100.0 / 16000, alpha = 1 - Math.Exp(-2 * Math.PI * (16000 * 0.45) / 44100);
            public void Push(float v, List<float> outp)
            {
                lp1 += (float)alpha * (v - lp1); lp2 += (float)alpha * (lp1 - lp2);
                acc += lp2; cnt++; pos += 1;
                if (pos >= step) { pos -= step; outp.Add(acc / cnt); acc = 0; cnt = 0; }
            }
        }

        // torch.stft(center=True, hann window) and its inverse, for two channels at once
        sealed class Stft
        {
            readonly int nfft, hop, bins;
            readonly double[] win;
            readonly Fft fft;
            readonly double[] re, im;
            public Stft(int nfft, int hop)
            {
                this.nfft = nfft; this.hop = hop; bins = nfft / 2 + 1;
                win = new double[nfft];
                for (int i = 0; i < nfft; i++) win[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / nfft);
                fft = new Fft(nfft);
                re = new double[nfft]; im = new double[nfft];
            }

            static int Reflect(int j, int n) { while (j < 0 || j >= n) { if (j < 0) j = -j; if (j >= n) j = 2 * n - 2 - j; } return j; }

            // spec: [4][dimF][dimT] = L.re, L.im, R.re, R.im; the lowest 3 bins are left at zero (like the first AURA)
            public void Forward(float[] l, float[] r, float[] spec, int dimF, int dimT)
            {
                int n = l.Length, half = nfft / 2;
                for (int t = 0; t < dimT; t++)
                {
                    int start = t * hop - half;
                    for (int i = 0; i < nfft; i++) { int j = Reflect(start + i, n); re[i] = l[j] * win[i]; im[i] = r[j] * win[i]; }
                    fft.Run(re, im, false);
                    // two real signals in one transform: L = (X[k] + conj X[n-k]) / 2, R = (X[k] - conj X[n-k]) / 2i
                    for (int k = 0; k < dimF; k++)
                    {
                        int nk = k == 0 ? 0 : nfft - k;
                        double xr = re[k], xi = im[k], yr = re[nk], yi = -im[nk];
                        double lr = (xr + yr) * 0.5, li = (xi + yi) * 0.5;
                        double rr = (xi - yi) * 0.5, ri = -(xr - yr) * 0.5;
                        int o = k * dimT + t;
                        if (k < 3) { lr = li = rr = ri = 0; }
                        spec[o] = (float)lr; spec[dimF * dimT + o] = (float)li;
                        spec[2 * dimF * dimT + o] = (float)rr; spec[3 * dimF * dimT + o] = (float)ri;
                    }
                }
            }

            public void Inverse(float[] spec, int dimF, int dimT, out double[] outL, out double[] outR)
            {
                int total = nfft + hop * (dimT - 1), half = nfft / 2;
                var yl = new double[total]; var yr = new double[total]; var ws = new double[total];
                int plane = dimF * dimT;
                for (int t = 0; t < dimT; t++)
                {
                    Array.Clear(re, 0, nfft); Array.Clear(im, 0, nfft);
                    // Z = L + iR, with each spectrum extended to be Hermitian (bins past dimF are zero)
                    for (int k = 0; k < dimF; k++)
                    {
                        int o = k * dimT + t;
                        double lr = spec[o], li = spec[plane + o], rr = spec[2 * plane + o], ri = spec[3 * plane + o];
                        if (k == 0) { li = 0; ri = 0; }
                        re[k] += lr - ri; im[k] += li + rr;
                        if (k > 0) { int nk = nfft - k; re[nk] += lr + ri; im[nk] += -li + rr; }
                    }
                    fft.Run(re, im, true);
                    int at = t * hop;
                    for (int i = 0; i < nfft; i++)
                    {
                        double w = win[i];
                        yl[at + i] += re[i] / nfft * w; yr[at + i] += im[i] / nfft * w; ws[at + i] += w * w;
                    }
                }
                int len = total - nfft;
                outL = new double[len]; outR = new double[len];
                for (int i = 0; i < len; i++)
                {
                    double d = Math.Max(ws[i + half], 1e-8);
                    outL[i] = yl[i + half] / d; outR[i] = yr[i + half] / d;
                }
            }
        }
    }

    // a mixed-radix FFT (any size made of 2s, 3s, 4s and 5s — 7680, 6144 and 5120 all are)
    sealed class Fft
    {
        readonly int n;
        readonly List<int> f = new List<int>();     // p, m pairs
        readonly double[] twr, twi;
        double[] ar, ai;
        readonly double[] sr = new double[16], si = new double[16];

        public Fft(int n)
        {
            this.n = n;
            twr = new double[n]; twi = new double[n];
            for (int i = 0; i < n; i++) { twr[i] = Math.Cos(-2 * Math.PI * i / n); twi[i] = Math.Sin(-2 * Math.PI * i / n); }
            int rest = n, p = 4;
            while (rest > 1)
            {
                while (rest % p != 0) { p = p == 4 ? 2 : p == 2 ? 3 : p + 2; if (p * p > rest) p = rest; }
                rest /= p; f.Add(p); f.Add(rest);
            }
            ar = new double[n]; ai = new double[n];
        }

        // in place; inverse = without the 1/n
        public void Run(double[] re, double[] im, bool inverse)
        {
            if (inverse) for (int i = 0; i < n; i++) im[i] = -im[i];
            Work(ar, ai, 0, re, im, 0, 1, 0);
            Array.Copy(ar, re, n);
            if (inverse) for (int i = 0; i < n; i++) im[i] = -ai[i]; else Array.Copy(ai, im, n);
        }

        void Work(double[] or, double[] oi, int o, double[] inr, double[] ini, int fi, int fstride, int k)
        {
            int p = f[2 * k], m = f[2 * k + 1], end = o + p * m, beg = o;
            if (m == 1) { for (; o < end; o++, fi += fstride) { or[o] = inr[fi]; oi[o] = ini[fi]; } }
            else for (; o < end; o += m, fi += fstride) Work(or, oi, o, inr, ini, fi, fstride * p, k + 1);
            o = beg;
            if (p == 2)
            {
                for (int u = 0; u < m; u++)
                {
                    int a = o + u, b = a + m, tw = u * fstride;
                    double tr = or[b] * twr[tw] - oi[b] * twi[tw], ti = or[b] * twi[tw] + oi[b] * twr[tw];
                    or[b] = or[a] - tr; oi[b] = oi[a] - ti; or[a] += tr; oi[a] += ti;
                }
                return;
            }
            for (int u = 0; u < m; u++)
            {
                for (int q = 0, kk = o + u; q < p; q++, kk += m) { sr[q] = or[kk]; si[q] = oi[kk]; }
                for (int q1 = 0, kk = u; q1 < p; q1++, kk += m)
                {
                    double xr = sr[0], xi = si[0];
                    int tw = 0;
                    for (int q = 1; q < p; q++)
                    {
                        tw += fstride * kk; if (tw >= n) tw -= n;
                        xr += sr[q] * twr[tw] - si[q] * twi[tw];
                        xi += sr[q] * twi[tw] + si[q] * twr[tw];
                    }
                    or[o + kk] = xr; oi[o + kk] = xi;
                }
            }
        }
    }
}
