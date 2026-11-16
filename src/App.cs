using System;
using System.IO;

namespace Aura
{
    static class App
    {
        public const string Name = "AURA";
        public static string DataDir;
        public static string OldDir;          // the first AURA's data folder (%APPDATA%\Unreleased)
        public static bool IsWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;
        public static string Version
        {
            get
            {
                var v = typeof(App).Assembly.GetName().Version;
                return v.Major + "." + v.Minor + "." + v.Build;
            }
        }

        public static void Init(string dataOverride)
        {
            var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            DataDir = dataOverride ?? Path.Combine(appdata, "AURA Native");
            OldDir = Environment.GetEnvironmentVariable("AURA_OLD_DIR") ?? Path.Combine(appdata, "Unreleased");
            Directory.CreateDirectory(DataDir);
            Settings.Dir = DataDir;
        }

        static readonly object logLock = new object();
        public static void Log(string msg)
        {
            try
            {
                lock (logLock)
                {
                    var p = Path.Combine(DataDir ?? Path.GetTempPath(), "error.log");
                    if (File.Exists(p) && new FileInfo(p).Length > 512 * 1024) File.Delete(p);
                    File.AppendAllText(p, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
