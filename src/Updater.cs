using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;

namespace Aura
{
    [DataContract] sealed class GhRelease
    {
        [DataMember(Name = "tag_name")] public string Tag;
        [DataMember(Name = "html_url")] public string Page;
        [DataMember(Name = "assets")] public List<GhAsset> Assets;
    }
    [DataContract] sealed class GhAsset
    {
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "browser_download_url")] public string Url;
        [DataMember(Name = "size")] public long Size;
    }

    // Checks GitHub for a newer AURA Native, downloads its installer and runs it quietly.
    // The installer closes AURA, updates it and opens it again.
    static class Updater
    {
        public static string ReleasesUrl = Environment.GetEnvironmentVariable("AURA_UPDATE_URL") ?? "https://api.github.com/repos/evloc/AuraNative/releases/latest";
        public const string AssetName = "AURA-Native-Setup.exe";

        public static string Latest = "";        // newest version on GitHub ("" until checked)
        public static string Url = "", Page = "";
        public static string Status = "";        // shown in Settings
        public static bool Checking, Downloading;
        public static int Percent = -1;
        public static bool Available => Latest.Length > 0 && Newer(Latest, App.Version) && Url.Length > 0;
        public static event Action Changed;      // on the UI thread
        static SynchronizationContext ui;

        public static bool Newer(string a, string b)
        {
            Version Parse(string s) { s = (s ?? "").Trim().TrimStart('v', 'V'); return Version.TryParse(s, out var v) ? v : new Version(0, 0); }
            return Parse(a) > Parse(b);
        }

        static void Raise() { if (ui != null) ui.Post(_ => Changed?.Invoke(), null); else Changed?.Invoke(); }

        public static void Check(bool manual)
        {
            if (Checking || Downloading) return;
            ui = ui ?? SynchronizationContext.Current;
            Checking = true; Status = "Checking…"; Raise();
            new Thread(() =>
            {
                try
                {
                    var rel = Net.Json<GhRelease>(Net.Get(ReleasesUrl, 10000));
                    var asset = rel?.Assets?.FirstOrDefault(a => string.Equals(a.Name, AssetName, StringComparison.OrdinalIgnoreCase));
                    Latest = (rel?.Tag ?? "").TrimStart('v', 'V');
                    Url = asset?.Url ?? ""; Page = rel?.Page ?? "";
                    Status = Available ? "Version " + Latest + " is ready to install" : Latest.Length > 0 && asset == null && Newer(Latest, App.Version) ? "Version " + Latest + " is still being built — try again in a few minutes" : "You have the newest version";
                }
                catch (Exception e)
                {
                    Status = manual ? "Couldn't reach GitHub: " + e.Message : "";
                }
                finally { Checking = false; Raise(); }
            }) { IsBackground = true, Name = "update check" }.Start();
        }

        // download, then hand over to the installer; onReady runs on the UI thread and should close AURA
        public static void Install(Action<string> onReady, Action<string> onFail)
        {
            if (!Available || Downloading) return;
            ui = ui ?? SynchronizationContext.Current;
            Downloading = true; Percent = 0; Status = "Downloading " + Latest + "…"; Raise();
            new Thread(() =>
            {
                try
                {
                    var file = Path.Combine(Path.GetTempPath(), "AURA-Native-Setup-" + Latest + ".exe");
                    Net.Download(Url, file, p => { if (p >= 0 && p != Percent) { Percent = p; Status = "Downloading " + Latest + "… " + p + "%"; Raise(); } });
                    if (new FileInfo(file).Length < 100 * 1024) throw new IOException("the download looks broken");
                    Status = "Installing…"; Raise();
                    ui.Post(_ => onReady(file), null);
                }
                catch (Exception e)
                {
                    Downloading = false; Status = "Update failed: " + e.Message; Raise();
                    ui.Post(_ => onFail(e.Message), null);
                }
            }) { IsBackground = true, Name = "update" }.Start();
        }

        public static void RunInstaller(string file)
        {
            // the installer waits for AURA to close, updates it and opens it again (see installer.iss)
            Process.Start(new ProcessStartInfo(file, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS") { UseShellExecute = true });
        }
    }
}
