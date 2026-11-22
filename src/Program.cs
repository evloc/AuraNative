using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace Aura
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
#if TESTHOOKS
            if (args.Length > 0 && args[0] == "--tagdump") return TagDump.Run(args.Skip(1).ToArray());
            if (args.Length > 0 && args[0] == "--tagwrite") return TagDump.Write(args.Skip(1).ToArray());
            if (args.Length > 0 && args[0] == "--lyricsdump") return TagDump.Lyrics(args.Skip(1).ToArray());
#endif
            string data = Arg(args, "--data");
            bool test = args.Contains("--test-audio");
            App.Init(data);

            if (args.Contains("--selftest")) return SelfTest();

            // one AURA at a time: a second start just brings the first one forward
            Win.InitMessages();
            bool fresh;
            using (var single = new Mutex(true, "AURA-Native-" + Environment.UserName, out fresh))
            {
                if (!fresh && data == null)
                {
                    if (App.IsWindows && Win.ShowMessage != 0) Win.PostMessage(Win.Broadcast, Win.ShowMessage, IntPtr.Zero, IntPtr.Zero);
                    return 0;
                }
                return Run(args, test);
            }
        }

        static string Arg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        static int Run(string[] args, bool test)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) => Crash(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Crash(e.ExceptionObject as Exception);
            try { using (var g = Graphics.FromHwnd(IntPtr.Zero)) Theme.S = Math.Max(1f, g.DpiX / 96f); } catch { }

            var prefs = Settings.Load();
            if (!prefs.Imported && prefs.Folders.Count == 0)
                prefs.Folders = OldConfig.ReadFolders(Path.Combine(App.OldDir, "config.json")).Where(f => !string.IsNullOrWhiteSpace(f)).ToList();
            var music = Arg(args, "--music");
            if (music != null && !prefs.Folders.Contains(music)) prefs.Folders.Add(music);
            Theme.Apply(prefs.ThemeName);
            Theme.AccentBase = Theme.Accent = Theme.Parse(prefs.Accent, Theme.AccentBase);

            Library lib;
            try
            {
                lib = new Library(Path.Combine(App.DataDir, "library.db"));
                lib.Load();
            }
            catch (Exception e)
            {
                App.Log("library: " + e);
                MessageBox.Show("AURA couldn't open its library.\n\n" + e.Message + "\n\nAURA Native needs Windows 10 or 11.", "AURA", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }

            var player = new Player(lib, AudioEngine.Create(test));
            var form = new MainForm(lib, player, prefs);
            form.Load += (s, e) => { Art.Init(App.DataDir); form.Start(); };
#if TESTHOOKS
            var script = Arg(args, "--ui-test");
            if (script != null) UiTest.Attach(form, script, Arg(args, "--out"));
#endif
            Application.Run(form);
            player.Dispose();
            lib.Db.Dispose();
            return 0;
        }

        static bool crashed;
        static void Crash(Exception e)
        {
            App.Log("crash: " + e);
            if (crashed) return;
            crashed = true;
            try { MessageBox.Show("Something went wrong in AURA. It has been written to error.log in " + App.DataDir + ".\n\n" + e?.Message, "AURA", MessageBoxButtons.OK, MessageBoxIcon.Warning); } catch { }
            crashed = false;
        }

        // checks the parts AURA needs, for the release build; writes selftest.txt
        static int SelfTest()
        {
            var lines = new System.Collections.Generic.List<string> { "AURA Native self-test", "version      : " + App.Version };
            bool ok = true;
            try
            {
                var p = Path.Combine(App.DataDir, "selftest.db");
                using (var db = new Db(p)) { db.Exec("CREATE TABLE IF NOT EXISTS t (x)"); db.Exec("INSERT INTO t VALUES (1)"); }
                File.Delete(p); try { File.Delete(p + "-wal"); File.Delete(p + "-shm"); } catch { }
                lines.Add("database     : ok (SQLite " + Sqlite.Version() + ")");
            }
            catch (Exception e) { ok = false; lines.Add("database     : FAILED " + e.Message); }
            try
            {
                var a = AudioEngine.Create(false);
                lines.Add("audio        : " + a.Name);
                a.Dispose();
            }
            catch (Exception e) { lines.Add("audio        : not available (" + e.Message + ")"); }
            lines.Add("RESULT: " + (ok ? "OK" : "FAILED"));
            File.WriteAllLines(Path.Combine(App.DataDir, "selftest.txt"), lines);
            Console.WriteLine(string.Join(Environment.NewLine, lines));
            return ok ? 0 : 1;
        }
    }
}
