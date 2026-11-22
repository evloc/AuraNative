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
            if (args.Length > 0 && args[0] == "--sniff") return TagDump.Sniff(args.Skip(1).ToArray());
            if (args.Length > 0 && args[0] == "--tagwrite") return TagDump.Write(args.Skip(1).ToArray());
            if (args.Length > 0 && args[0] == "--analyze") return TagDump.Analyze(args.Skip(1).ToArray());
            if (args.Length > 0 && args[0] == "--match") return TagDump.Match(args.Skip(1).ToArray());
            if (args.Length > 0 && args[0] == "--lyricsdump") return TagDump.Lyrics(args.Skip(1).ToArray());
#endif
            string data = Arg(args, "--data");
            bool test = args.Contains("--test-audio");
            App.Init(data);

            if (args.Contains("--selftest")) return SelfTest();
            if (args.Contains("--vocals")) return VocalsCli(Arg(args, "--vocals"), Arg(args, "--out"));

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

        static void WriteTone(string path, double secs)
        {
            int rate = 44100, n = (int)(rate * secs);
            using (var f = new BinaryWriter(File.Create(path)))
            {
                f.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); f.Write(36 + n * 4); f.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                f.Write(16); f.Write((short)1); f.Write((short)2); f.Write(rate); f.Write(rate * 4); f.Write((short)4); f.Write((short)16);
                f.Write(System.Text.Encoding.ASCII.GetBytes("data")); f.Write(n * 4);
                for (int i = 0; i < n; i++) { short v = (short)(8000 * Math.Sin(2 * Math.PI * 440 * i / rate) * (i % 11025 < 2000 ? 1 : 0.2)); f.Write(v); f.Write(v); }
            }
        }

        // AURA Native.exe --vocals song.mp3 --out vocals.wav   (the vocals on their own, 16 kHz mono)
        static int VocalsCli(string input, string output)
        {
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var y = Separator.Vocals(Path.GetFullPath(input), 0, Tags.Read(input).Duration, 20 * 60, p => { }, null);
                using (var f = new BinaryWriter(File.Create(output ?? "vocals.wav")))
                {
                    f.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); f.Write(36 + y.Length * 2); f.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                    f.Write(16); f.Write((short)1); f.Write((short)1); f.Write(16000); f.Write(32000); f.Write((short)2); f.Write((short)16);
                    f.Write(System.Text.Encoding.ASCII.GetBytes("data")); f.Write(y.Length * 2);
                    foreach (var v in y) f.Write((short)Math.Max(-32767, Math.Min(32767, v * 32767)));
                }
                Console.WriteLine("vocals: " + (y.Length / 16000.0).ToString("0.0") + " s in " + sw.Elapsed.TotalSeconds.ToString("0.0") + " s (" + Separator.LastStats + ")");
                return 0;
            }
            catch (Exception e) { Console.WriteLine("vocals: FAILED " + e.Message); return 1; }
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
            // reading and writing sound through Media Foundation (Windows Server machines may not have it)
            try
            {
                var dir = Path.Combine(Path.GetTempPath(), "aura-selftest");
                Directory.CreateDirectory(dir);
                var wav = Path.Combine(dir, "tone.wav");
                WriteTone(wav, 3);
                var mono = Decoder.Mono(wav, 0, 11025, 60, null, out double len);
                lines.Add("read sound   : ok (" + len.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " s, " + mono.Length + " samples)");
                var mp3 = Path.Combine(dir, "tone.mp3");
                if (File.Exists(mp3)) File.Delete(mp3);
                using (var w = new Mf.Mp3Writer(mp3, 2, 44100, 192))
                {
                    var pcm = new short[44100 * 2 * 3];
                    for (int i = 0; i < pcm.Length / 2; i++) pcm[i * 2] = pcm[i * 2 + 1] = (short)(8000 * Math.Sin(2 * Math.PI * 440 * i / 44100.0));
                    w.Write(pcm, pcm.Length); w.Finish();
                }
                var back = Tags.Read(mp3);
                lines.Add("write MP3    : ok (" + new FileInfo(mp3).Length + " bytes, " + back.Duration.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " s)");
                var s2 = SongSound.Analyze(mp3, 0, null);
                lines.Add("listen       : ok (" + s2.Print.Length + " steps)");
            }
            catch (Exception e) { lines.Add("sound        : not available here (" + e.Message + ")"); }
#if WPF
            try { lines.Add("graphics card: " + Aura.Wpf.WpfUi.SelfTest()); }
            catch (Exception e) { ok = false; lines.Add("graphics card: FAILED " + e); }
#endif
            // speech recognition: must load when it's installed
            if (Whisper.Load()) lines.Add("speech       : ok");
            else if (Whisper.LoadError.Contains("isn't installed")) lines.Add("speech       : not installed");
            else { ok = false; lines.Add("speech       : FAILED " + Whisper.LoadError); }
            // taking the vocals out: onnxruntime must load when it's installed
            if (!Separator.Installed) lines.Add("vocals       : not installed");
            else
            {
                var r = Separator.SelfTest();
                if (r.StartsWith("ok")) lines.Add("vocals       : " + r); else { ok = false; lines.Add("vocals       : FAILED " + r); }
            }
            lines.Add("RESULT: " + (ok ? "OK" : "FAILED"));
            File.WriteAllLines(Path.Combine(App.DataDir, "selftest.txt"), lines);
            Console.WriteLine(string.Join(Environment.NewLine, lines));
            return ok ? 0 : 1;
        }
    }
}
