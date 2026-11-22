using System;
using System.IO;
using System.Linq;

namespace Aura
{
    // File paths that older Windows APIs trip over: longer than 260 characters, or with a
    // folder or file name ending in a space or a dot. The \\?\ form reaches them anyway.
    static class FileX
    {
        public static bool Awkward(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (path.Length >= 240) return true;
            foreach (var part in path.Split('\\', '/'))
                if (part.Length > 0 && part != "." && part != ".." && (part.EndsWith(" ") || part.EndsWith("."))) return true;
            return false;
        }

        public static string Long(string path, bool always = false)
        {
            if (!App.IsWindows || string.IsNullOrEmpty(path) || path.StartsWith(@"\\?\")) return path;
            if (!always && !Awkward(path)) return path;
            if (path.StartsWith(@"\\")) return @"\\?\UNC\" + path.Substring(2);
            return @"\\?\" + path;
        }

        public static bool Exists(string path)
        {
            try { if (File.Exists(path)) return true; } catch { }
            try { return App.IsWindows && File.Exists(Long(path, true)); } catch { return false; }
        }

        public static FileStream OpenRead(string path, int buffer = 65536) =>
            new FileStream(Long(path), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, buffer);

        // what to hand to Windows' player engines: a short path when the real one would confuse them
        public static string ForPlayer(string path)
        {
            bool odd = Awkward(path) || path.IndexOfAny(new[] { '#', '%', ';' }) >= 0;
            if (!odd) return path;
            return Win.ShortPath(path) ?? path;
        }
    }
}

namespace Aura
{
    // What a music file really is, whatever its name says. Leaks are often M4A, Opus or WebM
    // files renamed to .mp3; Windows' player engines go by the name and then fail to open them.
    static class AudioKind
    {
        static readonly string[][] Same = { new[] { ".m4a", ".mp4", ".m4b" }, new[] { ".ogg", ".oga" }, new[] { ".aif", ".aiff" } };

        public static string Detect(string path)
        {
            byte[] b;
            try
            {
                using (var f = FileX.OpenRead(path))
                {
                    b = new byte[64];
                    int n = f.Read(b, 0, 64);
                    if (n < 12) return null;
                    // skip an ID3 tag in front, then look at what follows
                    if (b[0] == 'I' && b[1] == 'D' && b[2] == '3')
                    {
                        long size = 10 + (((b[6] & 0x7F) << 21) | ((b[7] & 0x7F) << 14) | ((b[8] & 0x7F) << 7) | (b[9] & 0x7F)) + ((b[5] & 0x10) != 0 ? 10 : 0);
                        if (size >= f.Length) return ".mp3";
                        f.Position = size;
                        b = new byte[64];
                        n = f.Read(b, 0, 64);
                        int z = 0; while (z < n && b[z] == 0) z++;      // padding after the tag
                        if (z > 0 && z < n) { var c = new byte[64]; Array.Copy(b, z, c, 0, n - z); b = c; }
                    }
                }
            }
            catch { return null; }
            string A(int o, int len) { var s = new char[len]; for (int i = 0; i < len; i++) s[i] = (char)b[o + i]; return new string(s); }
            if (A(4, 4) == "ftyp") return ".m4a";
            if (A(0, 4) == "OggS") return A(28, 8) == "OpusHead" ? ".opus" : ".ogg";
            if (A(0, 4) == "fLaC") return ".flac";
            if (A(0, 4) == "RIFF" && A(8, 4) == "WAVE") return ".wav";
            if (A(0, 4) == "FORM" && (A(8, 4) == "AIFF" || A(8, 4) == "AIFC")) return ".aiff";
            if (b[0] == 0x1A && b[1] == 0x45 && b[2] == 0xDF && b[3] == 0xA3) return ".webm";
            if (b[0] == 0x30 && b[1] == 0x26 && b[2] == 0xB2 && b[3] == 0x75) return ".wma";
            if (b[0] == 0xFF && (b[1] & 0xF0) == 0xF0 && (b[1] & 0x06) == 0) return ".aac";      // ADTS
            if (b[0] == 0xFF && (b[1] & 0xE0) == 0xE0) return ".mp3";
            return null;
        }

        public static bool Matches(string path, string kind)
        {
            if (kind == null) return true;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == kind) return true;
            return Same.Any(g => g.Contains(ext) && g.Contains(kind));
        }

        public static string Name(string kind) => kind == null ? "unknown" : kind.TrimStart('.').ToUpperInvariant();

        public static string Header(string path)
        {
            try
            {
                using (var f = FileX.OpenRead(path))
                {
                    var b = new byte[16]; int n = f.Read(b, 0, 16);
                    return BitConverter.ToString(b, 0, n);
                }
            }
            catch (Exception e) { return "(couldn't read: " + e.Message + ")"; }
        }

        // a copy with the right ending, for the player engines; kept in the temp folder
        public static string PlayableCopy(string path, string kind, long id)
        {
            var dir = Path.Combine(Path.GetTempPath(), "AURA Native", "play");
            Directory.CreateDirectory(dir);
            var fi = new FileInfo(FileX.Long(path));
            var dst = Path.Combine(dir, id + "_" + fi.Length + "_" + fi.LastWriteTimeUtc.Ticks.ToString("x") + kind);
            if (!File.Exists(dst))
            {
                File.Copy(FileX.Long(path), dst + ".part", true);
                File.Move(dst + ".part", dst);
                try
                {
                    // keep the last few only
                    foreach (var old in new DirectoryInfo(dir).GetFiles().OrderByDescending(x => x.LastWriteTimeUtc).Skip(8)) old.Delete();
                }
                catch { }
            }
            return dst;
        }
    }
}
