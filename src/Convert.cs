using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Aura
{
    // Turns a song into an MP3 with Windows' own MP3 encoder. Tags, cover and lyrics come along,
    // the original goes into an _originals folder next to it, and the song keeps its plays,
    // playlists and favorite (it's the same song in AURA, just a different file).
    static class Converter
    {
        public static readonly int[] Rates = { 128, 160, 192, 224, 256, 320 };

        public static bool CanConvert(Track t) => !string.Equals(Path.GetExtension(t.Path), ".mp3", StringComparison.OrdinalIgnoreCase) || AudioKind.Detect(t.Path) != ".mp3";

        interface IEncoder : IDisposable { void Write(short[] pcm, int n); void Finish(); }

        sealed class MfEncoder : IEncoder
        {
            readonly Mf.Mp3Writer w;
            public MfEncoder(string path, int ch, int rate, int kbps) { w = new Mf.Mp3Writer(path, ch, rate, kbps); }
            public void Write(short[] pcm, int n) => w.Write(pcm, n);
            public void Finish() => w.Finish();
            public void Dispose() => w.Dispose();
        }

#if TESTHOOKS
        // test build on Linux: ffmpeg's MP3 encoder stands in for Windows'
        sealed class FfEncoder : IEncoder
        {
            readonly Process p; readonly Stream s; byte[] buf = new byte[0];
            public FfEncoder(string path, int ch, int rate, int kbps)
            {
                var psi = new ProcessStartInfo("ffmpeg", "-v error -y -f s16le -ar " + rate + " -ac " + ch + " -i pipe:0 -c:a libmp3lame -b:a " + kbps + "k -f mp3 \"" + path + "\"")
                { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardError = true, CreateNoWindow = true };
                p = Process.Start(psi); s = p.StandardInput.BaseStream;
            }
            public void Write(short[] pcm, int n)
            {
                if (buf.Length < n * 2) buf = new byte[n * 2];
                Buffer.BlockCopy(pcm, 0, buf, 0, n * 2);
                s.Write(buf, 0, n * 2);
            }
            public void Finish() { s.Close(); p.WaitForExit(); if (p.ExitCode != 0) throw new IOException("encoder failed: " + p.StandardError.ReadToEnd()); }
            public void Dispose() { try { if (!p.HasExited) p.Kill(); } catch { } p.Dispose(); }
        }
#endif

        static IEncoder Encoder(string path, int ch, int rate, int kbps)
        {
#if TESTHOOKS
            if (!App.IsWindows) return new FfEncoder(path, ch, rate, kbps);
#endif
            return new MfEncoder(path, ch, rate, kbps);
        }

        // writes the MP3 next to the song and returns its path; the library isn't touched yet
        public static string Encode(Track t, int kbps, Action<double> progress, Func<bool> cancelled)
        {
            var dir = Path.GetDirectoryName(t.Path);
            var tmp = Path.Combine(dir, Path.GetFileNameWithoutExtension(t.Path) + ".aura-converting.mp3");
            if (FileX.Exists(tmp)) File.Delete(FileX.Long(tmp));
            IEncoder enc = null;
            int outCh = 0, outRate = 0, srcRate = 0;
            double step = 1, pos = 0; float[] prev = null;
            var pcm = new short[0];
            long frames = 0;
            double total = Math.Max(1, t.Duration);
            try
            {
                Decoder.Read(t, (b, n, ch, rate) =>
                {
                    if (enc == null)
                    {
                        srcRate = rate;
                        outCh = Math.Min(ch, 2);
                        outRate = rate == 32000 || rate == 44100 || rate == 48000 ? rate : rate < 38000 ? 32000 : rate % 11025 == 0 ? 44100 : 48000;
                        step = (double)rate / outRate;
                        prev = new float[outCh];
                        enc = Encoder(FileX.ForPlayer(dir) == dir ? tmp : Path.Combine(FileX.ForPlayer(dir), Path.GetFileName(tmp)), outCh, outRate, kbps);
                    }
                    int inFrames = n / ch;
                    int max = (int)(inFrames / step) + 4;
                    if (pcm.Length < max * outCh) pcm = new short[max * outCh];
                    int w = 0;
                    var cur = new float[outCh];
                    for (int i = 0; i < inFrames; i++)
                    {
                        // more than two channels: fold them into left and right
                        if (ch <= 2) for (int c = 0; c < outCh; c++) cur[c] = b[i * ch + c];
                        else { float l = 0, r = 0; for (int c = 0; c < ch; c++) { if (c % 2 == 0) l += b[i * ch + c]; else r += b[i * ch + c]; } cur[0] = l / ((ch + 1) / 2); cur[1] = r / (ch / 2); }
                        // straight-line resampling between this frame and the last (only when the rate must change)
                        while (pos <= 1)
                        {
                            for (int c = 0; c < outCh; c++)
                            {
                                float v = step == 1 ? cur[c] : prev[c] + (cur[c] - prev[c]) * (float)pos;
                                pcm[w++] = (short)Math.Max(-32768, Math.Min(32767, Math.Round(v * 32767)));
                            }
                            pos += step;
                            if (step == 1) break;
                        }
                        pos -= 1;
                        Array.Copy(cur, prev, outCh);
                    }
                    enc.Write(pcm, w);
                    frames += inFrames;
                    progress?.Invoke(Math.Min(0.99, frames / (double)srcRate / total));
                }, cancelled);
                if (cancelled != null && cancelled()) throw new OperationCanceledException();
                if (enc == null) throw new InvalidDataException("there's no sound in this file");
                enc.Finish();
            }
            catch
            {
                enc?.Dispose(); enc = null;
                try { if (FileX.Exists(tmp)) File.Delete(FileX.Long(tmp)); } catch { }
                throw;
            }
            finally { enc?.Dispose(); }
            if (!FileX.Exists(tmp) || new FileInfo(FileX.Long(tmp)).Length < 1000) throw new IOException("the MP3 came out empty");
            // tags, cover and lyrics
            var src = Tags.Read(t.Path, true);
            var e = new TagEdit
            {
                Title = src.Title, Artist = src.Artist, Album = src.Album, AlbumArtist = src.AlbumArtist,
                Year = src.Year, Genre = src.Genre, Track = src.Track, Picture = src.Picture,
            };
            var lyr = src.Synced != null && src.Synced.Count > 0 && !Lrc.IsTimed(src.LyricsText) ? Lrc.ToLrc(src.Synced) : src.LyricsText;
            if (lyr.Trim().Length > 0) e.Lyrics = lyr;
            if (src.WordsRaw.Trim().Length > 0) e.Words = src.WordsRaw;
            try { if (e.Any) TagWriter.Write(tmp, e); }
            catch
            {
                try { File.Delete(FileX.Long(tmp)); } catch { }
                throw;
            }
            var check = Tags.Read(tmp);
            if (t.Duration > 2 && Math.Abs(check.Duration - t.Duration) > 1.5)
            {
                try { File.Delete(FileX.Long(tmp)); } catch { }
                throw new IOException("the MP3 came out the wrong length (" + check.Duration.ToString("0.0") + " s instead of " + t.Duration.ToString("0.0") + " s)");
            }
            return tmp;
        }

        // swaps the new MP3 in: the original goes to _originals, the song now points at the MP3
        public static string Swap(Library lib, Track t, string tmp)
        {
            var dir = Path.GetDirectoryName(t.Path);
            var stem = Path.GetFileNameWithoutExtension(t.Path);
            var dest = Path.Combine(dir, stem + ".mp3");
            for (int n = 2; FileX.Exists(dest) && !string.Equals(dest, t.Path, StringComparison.OrdinalIgnoreCase); n++) dest = Path.Combine(dir, stem + " (" + n + ").mp3");
            var keepDir = Path.Combine(dir, "_originals");
            Directory.CreateDirectory(FileX.Long(keepDir));
            var orig = Path.Combine(keepDir, Path.GetFileName(t.Path));
            for (int n = 2; FileX.Exists(orig); n++) orig = Path.Combine(keepDir, stem + " (" + n + ")" + Path.GetExtension(t.Path));
            File.Move(FileX.Long(t.Path), FileX.Long(orig));
            try { File.Move(FileX.Long(tmp), FileX.Long(dest)); }
            catch { File.Move(FileX.Long(orig), FileX.Long(t.Path)); throw; }
            var side = LyricsStore.Sidecar(t.Path);
            if (side != null && !string.Equals(Path.ChangeExtension(dest, ".lrc"), side, StringComparison.OrdinalIgnoreCase))
                try { File.Move(FileX.Long(side), FileX.Long(Path.ChangeExtension(dest, ".lrc"))); } catch { }
            lib.Db.Exec("DELETE FROM tracks WHERE path=? AND id<>?", dest, t.Id);
            lib.Db.Exec("UPDATE tracks SET path=? WHERE id=?", dest, t.Id);
            t.Path = dest;
            lib.Refresh(t);
            App.Log("converted to MP3: " + dest + " (original kept in " + orig + ")");
            return orig;
        }
    }
}
