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
