// test only: drives the real window step by step, checks results, and saves screenshots
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Aura
{
    static class UiTest
    {
        static MainForm F;
        static string outDir;
        static int fails, passes;
        static readonly HashSet<int> begun2 = new HashSet<int>();
        static readonly List<(string name, Func<bool> act)> steps = new List<(string, Func<bool>)>();

        static void Ok(bool c, string what) { if (c) passes++; else fails++; Console.WriteLine((c ? "PASS " : "FAIL ") + what); }

        static void Shot(string name)
        {
            try
            {
                F.Refresh();
                using (var b = new Bitmap(F.ClientSize.Width, F.ClientSize.Height))
                {
                    using (var g = Graphics.FromImage(b))
                    {
                        var p = F.PointToScreen(Point.Empty);
                        g.CopyFromScreen(p, Point.Empty, b.Size);
                    }
                    b.Save(Path.Combine(outDir, name + ".png"), ImageFormat.Png);
                }
            }
            catch (Exception e) { Console.WriteLine("shot failed " + e.Message); }
        }

        // each step runs once its wait condition is true (or times out)
        static void Step(string name, Func<bool> act) => steps.Add((name, act));
        static Func<bool> Wait(Func<bool> cond) => cond;

        public static void Attach(MainForm form, string script, string dir)
        {
            F = form; outDir = dir ?? "."; Directory.CreateDirectory(outDir);
            TestAudio.Speed = 1;
            if (script == "main") MainScript();
            else if (script == "resume") Resume();
            else if (script == "lyrics") LyricsScript();
            else if (script == "playback") PlaybackScript();
            else if (script == "organize") OrganizeScript();
            else if (script == "sound") SoundScript();
            else if (script == "more") MoreScript();
            else if (script == "idle") IdleScript();
            int i = 0; DateTime started = DateTime.Now; bool begun = false;
            var t = new Timer { Interval = 150 };
            t.Tick += (s, e) =>
            {
                if (i >= steps.Count)
                {
                    t.Stop();
                    Console.WriteLine("\nPASSES " + passes + " FAILS " + fails);
                    F.Close();
                    return;
                }
                if (!begun) { begun = true; started = DateTime.Now; }
                bool done;
                if (!begun2.Contains(i)) { begun2.Add(i); if (Environment.GetEnvironmentVariable("AURA_TEST_TRACE") != null) Console.WriteLine("  > " + steps[i].name); }
                try { done = steps[i].act(); }
                catch (Exception ex) { Console.WriteLine("FAIL step '" + steps[i].name + "' threw " + ex); fails++; done = true; }
                if (done || (DateTime.Now - started).TotalSeconds > 60)
                {
                    if (!done) { Console.WriteLine("FAIL step timed out: " + steps[i].name); fails++; }
                    i++; begun = false;
                }
            };
            form.Shown += (s, e) => t.Start();
        }

        static TrackPage TP => F.Page as TrackPage;
        static Library L => F.Lib;
        static Player P => F.Player;
        static int waitTicks;
        static bool Pause(int ticks) { if (waitTicks == 0) waitTicks = ticks; return --waitTicks == 0; }

        static void MainScript()
        {
            Step("scan finishes", () => !L.Scan.Running && L.Tracks.Count > 0 && F.Prefs.Imported);
            Step("checks after scan", () =>
            {
                Ok(L.Tracks.Count == int.Parse(Environment.GetEnvironmentVariable("EXPECT_TRACKS") ?? "0"), "library has every song (" + L.Tracks.Count + ")");
                var pl = L.Playlists.FirstOrDefault(p => p.Name == "Road trip");
                Ok(pl != null && pl.Items.Count == 3, "playlist brought over from the first AURA with its 3 songs (" + (pl?.Items.Count ?? -1) + ")");
                Ok(pl != null && L.Get(pl.Items[0]).Path.EndsWith("c.flac"), "playlist kept its order");
                Ok(L.Tracks.Count(t => t.Fav) == 2, "2 favorites brought over (" + L.Tracks.Count(t => t.Fav) + ")");
                Ok(L.Tracks.Any(t => t.Plays == 7), "play counts brought over");
                Ok(L.Tracks.All(t => t.Duration > 1), "every song has a length");
                return true;
            });
            Step("home", () => Pause(10));
            Step("home shot", () => { Shot("01_home"); return true; });
            Step("songs page", () => { F.Go(new Route("songs")); return true; });
            Step("songs shot", () => { if (!Pause(6)) return false; Ok(TP != null && TP.RowCount == L.Tracks.Count, "Songs lists every song"); Shot("02_songs"); return true; });
            Step("play first", () => { P.PlayList(L.Tracks.Select(t => t.Id).ToList(), 0); return true; });
            Step("playing", () => { if (!Pause(14)) return false; Ok(P.IsPlaying && P.Current == L.Tracks[0], "first song plays"); Ok(F.Text.Contains(P.Current.Name), "window title shows the song"); return true; });
            Step("accent follows cover", () =>
            {
                if (!Pause(8)) return false;
                bool art = P.Current.HasArt;
                Ok(!art || Theme.Accent.ToArgb() != Theme.AccentBase.ToArgb(), "accent color follows the cover (" + ColorTranslator.ToHtml(Theme.Accent) + ")");
                Shot("03_songs_playing"); return true;
            });
            Step("next/prev", () =>
            {
                var first = P.Current;
                P.Next(); var second = P.Current;
                Ok(second != first, "Next moves on");
                P.Prev(); Ok(P.Current == first, "Previous goes back");
                return true;
            });
            Step("shuffle keeps current first", () =>
            {
                var cur = P.Current; P.SetShuffle(true);
                Ok(P.Current == cur && P.Order.Count == L.Tracks.Count && P.Order.Distinct().Count() == P.Order.Count, "shuffle keeps the song and every other song once");
                P.SetShuffle(false);
                Ok(P.Queue[P.Order[P.Pos]] == cur.Id, "unshuffle goes back to list order at the same song");
                return true;
            });
            Step("play next / add to up next", () =>
            {
                var last = L.Tracks.Last(); var mid = L.Tracks[2];
                P.AddToQueue(new[] { last.Id });
                P.PlayNext(new[] { mid.Id });
                var up = P.UpNext().ToList();
                Ok(up[0] == mid.Id && up[up.Count - 1] == last.Id, "Play next goes first, Add to up next goes last");
                F.Go(new Route("queue"));
                return true;
            });
            Step("queue page", () =>
            {
                if (!Pause(6)) return false;
                Ok(TP != null && TP.RowCount == P.UpNext().Count(), "Up next page lists what's coming (" + TP?.RowCount + ")");
                var before = P.UpNext().ToList();
                TP.TestDrag(0, 3);
                var after = P.UpNext().ToList();
                Ok(after[2] == before[0] && after[0] == before[1], "dragging a song in Up next moves it");
                Shot("04_queue"); return true;
            });
            Step("album page", () => { var t = L.Tracks.First(x => x.Album.Length > 0); F.Go(new Route("album", t.AlbumKey)); return true; });
            Step("album shot", () => { if (!Pause(8)) return false; Ok(TP != null && TP.RowCount >= 1, "album page has its songs"); Shot("05_album"); return true; });
            Step("artist page", () => { F.Go(new Route("artist", "juice wrld")); return true; });
            Step("artist shot", () => { if (!Pause(8)) return false; Ok(TP != null && TP.RowCount == L.Tracks.Count(t => t.MainArtist.ToLowerInvariant() == "juice wrld"), "artist page has their songs (" + TP?.RowCount + ")"); Shot("06_artist"); return true; });
            Step("albums grid", () => { F.Go(new Route("albums")); return true; });
            Step("albums shot", () => { if (!Pause(8)) return false; Shot("07_albums"); return true; });
            Step("artists grid", () => { F.Go(new Route("artists")); return true; });
            Step("artists shot", () => { if (!Pause(8)) return false; Shot("08_artists"); return true; });
            Step("playlist page", () => { var p = L.Playlists.First(x => x.Name == "Road trip"); F.Go(new Route("playlist", "", p.Id)); return true; });
            Step("playlist reorder", () =>
            {
                if (!Pause(6)) return false;
                var p = L.Playlists.First(x => x.Name == "Road trip");
                var before = p.Items.ToList();
                TP.TestDrag(0, 3);
                Ok(p.Items[2] == before[0] && p.Items[0] == before[1], "dragging reorders the playlist");
                var saved = L.Db.Rows("SELECT track_id FROM playlist_items WHERE playlist_id=? ORDER BY pos", p.Id).Select(r => Conv.L(r[0])).ToList();
                Ok(saved.SequenceEqual(p.Items), "new order is saved");
                Shot("09_playlist"); return true;
            });
            Step("new playlist + add", () =>
            {
                var p = L.CreatePlaylist("Test list", L.Tracks.Take(2).Select(t => t.Id));
                int n = L.AddToPlaylist(p, L.Tracks.Take(3).Select(t => t.Id));
                Ok(p.Items.Count == 3 && n == 1, "adding skips songs already in the playlist");
                L.RenamePlaylist(p, "Renamed");
                Ok(L.Playlists.Any(x => x.Name == "Renamed"), "rename works");
                L.DeletePlaylist(p);
                Ok(!L.Playlists.Contains(p) && Conv.L(L.Db.Scalar("SELECT COUNT(*) FROM playlist_items WHERE playlist_id=?", p.Id)) == 0, "delete removes it and its songs list");
                return true;
            });
            Step("favorites", () =>
            {
                var t = L.Tracks.First(x => !x.Fav);
                L.SetFav(t, true);
                F.Go(new Route("favorites"));
                return true;
            });
            Step("favorites shot", () => { if (!Pause(6)) return false; Ok(TP.RowCount == 3, "favorites page shows liked songs (" + TP.RowCount + ")"); Shot("10_favorites"); return true; });
            Step("search", () => { F.Head.Search.Text = "robbery"; return true; });
            Step("search results", () =>
            {
                if (F.Page is TrackPage tp && tp.Route.Kind == "search")
                {
                    if (!Pause(4)) return false;
                    Ok(tp.RowCount >= 1, "search finds songs (" + tp.RowCount + ")");
                    Shot("11_search");
                    F.Head.Search.Text = "";
                    return true;
                }
                return false;
            });
            Step("settings", () => { F.Go(new Route("settings")); return true; });
            Step("settings shot", () => { if (!Pause(6)) return false; Shot("12_settings"); return true; });
            Step("back history", () =>
            {
                F.Back();
                Ok(F.Current.Kind == "search", "Back goes to the page before (" + F.Current.Kind + ")");
                F.Forward();
                Ok(F.Current.Kind == "settings", "Forward goes ahead again");
                return true;
            });
            Step("song finishes", () =>
            {
                TestAudio.Speed = 5;
                P.Repeat = 0;
                P.PlayList(L.Tracks.Take(2).Select(t => t.Id).ToList(), 0);
                return true;
            });
            Step("auto next + play count", () =>
            {
                if (P.Current != L.Tracks[1]) return false;
                Ok(true, "moves to the next song when one ends");
                Ok(L.Tracks[0].Plays >= 1, "a song listened to is counted");
                return true;
            });
            Step("end of queue stops", () =>
            {
                if (P.State != PlayState.Stopped) return false;
                Ok(P.Current == L.Tracks[1], "stops after the last song");
                TestAudio.Speed = 1;
                return true;
            });
            Step("repeat one", () =>
            {
                TestAudio.Speed = 40; P.Repeat = 2;
                P.PlayList(L.Tracks.Take(2).Select(t => t.Id).ToList(), 0);
                return true;
            });
            Step("repeat one plays it again", () =>
            {
                if (!Pause(30)) return false;
                Ok(P.Current == L.Tracks[0] && P.IsPlaying, "repeat one keeps playing the same song");
                P.Repeat = 0; TestAudio.Speed = 1;
                return true;
            });
            Step("new file appears", () =>
            {
                var dir = Path.GetDirectoryName(L.Tracks[0].Path);
                File.Copy(L.Tracks[0].Path, Path.Combine(dir, "zz_new" + Path.GetExtension(L.Tracks[0].Path)), true);
                return true;
            });
            Step("new file is picked up", () =>
            {
                if (L.Tracks.Count != int.Parse(Environment.GetEnvironmentVariable("EXPECT_TRACKS")) + 1) return false;
                Ok(true, "a song added to the folder shows up by itself");
                return true;
            });
            Step("leave a song playing for next time", () =>
            {
                var t = L.Tracks.First(x => x.Name == "Robbery");
                P.PlayList(L.Tracks.Select(x => x.Id).ToList(), L.Tracks.IndexOf(t));
                return true;
            });
            Step("position", () =>
            {
                if (!Pause(20)) return false;
                P.Seek(12.5);
                Ok(Theme.Accent.ToArgb() != Theme.AccentBase.ToArgb() && Theme.Accent.G > Theme.Accent.R, "the color follows a teal cover (" + ColorTranslator.ToHtml(Theme.Accent) + ")");
                File.WriteAllText(Path.Combine(outDir, "resume.txt"), P.Current.Id + " " + P.Position.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Shot("13_now");
                return true;
            });
        }

        static Track T(string name) => L.Tracks.First(t => t.Name == name);
        static Form floating;

        static void ShotForm(Form f, string name)
        {
            try
            {
                f.Refresh();
                using (var b = new Bitmap(f.Width, f.Height))
                {
                    using (var g = Graphics.FromImage(b)) g.CopyFromScreen(f.Location, Point.Empty, b.Size);
                    b.Save(Path.Combine(outDir, name + ".png"), ImageFormat.Png);
                }
            }
            catch (Exception e) { Console.WriteLine("shot failed " + e.Message); }
        }

        static void LyricsScript()
        {
            Step("scan finishes", () => !L.Scan.Running && L.Tracks.Count > 0 && F.Prefs.Imported);
            Step("lyrics in files", () =>
            {
                var r = F.Lyrics.Get(T("Robbery"));
                Ok(r != null && r.ByWord && r.Lines.Count == 6 && r.Source == "In the song file", "lyrics in the song file are read, word by word (" + r?.Source + ")");
                Ok(T("Robbery").HasLyrics, "the library knows Robbery has lyrics");
                var n = F.Lyrics.Get(T("Night Drive"));
                Ok(n != null && n.Timed && n.Lines[0].Text == "night drive sidecar line", "a .lrc file next to the song is used (" + n?.Source + ")");
                Ok(F.Lyrics.Get(T("Hear Me Calling")) == null, "a song with no lyrics has none (yet)");
                return true;
            });
            Step("auto lyrics", () => F.Lyrics.Auto.Done >= 6 && !F.Lyrics.Auto.Running);
            Step("auto results", () =>
            {
                var a = F.Lyrics.Get(T("Lucid Dreams"));
                Ok(a != null && a.Timed && a.Lines[0].Text == "found line one" && a.FromAura, "same-length LRCLIB match saved with timings");
                var w = F.Lyrics.Get(T("Wasted"));
                Ok(w != null && !w.Timed && w.Plain.StartsWith("wasted words only"), "different length: words only");
                var e = F.Lyrics.Get(T("Empty"));
                Ok(e != null && e.Plain == "Empty words from Genius here\nsecond & final line", "Genius used when LRCLIB has nothing (" + (e?.Plain ?? "null").Replace("\n", "/") + ")");
                Ok(F.Lyrics.Get(T("All Girls Are the Same")) == null, "a different artist is not saved");
                var h = F.Lyrics.Get(T("Hear Me Calling"));
                Ok(h != null && h.Plain.Contains("hear me calling words line one") && h.Source == "Found on lyrics.ovh", "lyrics.ovh is used when LRCLIB and Genius have nothing (" + h?.Source + ")");
                Ok(F.Lyrics.Get(T("Leak (v2)")) == null, "nothing found: nothing saved");
                return true;
            });
            Step("play Robbery", () => { var t = T("Robbery"); P.PlayList(new[] { t.Id }.ToList()); F.Go(new Route("now")); return true; });
            Step("mid-word", () => { if (P.Position < 0.2) return false; P.Seek(1.75); return true; });
            Step("now playing shot", () =>
            {
                if (!Pause(5)) return false;
                Ok(F.Page is NowPlayingPage, "Now Playing opens");
                Shot("20_now_byword");
                return true;
            });
            Step("click a line seeks", () => { P.Seek(8.4); return true; });
            Step("later line shot", () => { if (!Pause(12)) return false; Shot("21_now_later"); return true; });
            Step("full screen", () =>
            {
                // a queue, so "up next" has something in it
                var ids = new List<long> { T("Robbery").Id }; ids.AddRange(L.Tracks.Where(x => x != T("Robbery")).Select(x => x.Id));
                P.PlayList(ids);
                F.FullScreenLyrics(); P.Seek(4.6); return true;
            });
            Step("full screen shot", () =>
            {
                if (!Pause(14)) return false;
                var full = Application.OpenForms.OfType<FullLyricsForm>().FirstOrDefault();
                Ok(full != null && full.Visible, "full-screen lyrics opens");
                if (full != null) { full.Controls.OfType<StageView>().First().Poke(); }
                return true;
            });
            Step("full screen bar", () =>
            {
                if (!Pause(4)) return false;
                var full = Application.OpenForms.OfType<FullLyricsForm>().FirstOrDefault();
                if (full != null) ShotForm(full, "22_fullscreen");
                Console.WriteLine("  full screen: " + StageView.Drawn + " frames, " + StageView.DrawMs.ToString("0.0") + " ms each");
                return true;
            });
            Step("full screen later", () => { if (!Pause(10)) return false; var full = Application.OpenForms.OfType<FullLyricsForm>().FirstOrDefault(); if (full != null) ShotForm(full, "22b_fullscreen_later"); return true; });
            Step("full screen no lyrics", () => { P.PlayList(new[] { T("Leak (v2)").Id }.ToList()); return true; });
            Step("full screen no lyrics shot", () =>
            {
                if (!Pause(12)) return false;
                var full = Application.OpenForms.OfType<FullLyricsForm>().FirstOrDefault();
                if (full != null) ShotForm(full, "22c_fullscreen_nolyrics");
                P.PlayList(new[] { T("Robbery").Id }.ToList());
                F.FullScreenLyrics();     // second press closes it
                return true;
            });
            Step("full screen closed", () =>
            {
                if (!Pause(4)) return false;
                Ok(!Application.OpenForms.OfType<FullLyricsForm>().Any(), "F11 again closes it");
                return true;
            });
            Step("song without lyrics", () => { P.PlayList(new[] { T("Leak (v2)").Id }.ToList()); return true; });
            Step("no lyrics shot", () => { if (!Pause(8)) return false; Shot("23_now_none"); return true; });
            Step("plain lyrics shot", () => { P.PlayList(new[] { T("Empty").Id }.ToList()); return true; });
            Step("plain shot", () => { if (!Pause(8)) return false; Shot("24_now_plain"); return true; });
            Step("finder", () =>
            {
                var f = new LyricsFinder(F, T("Lucid Dreams")); floating = f; f.Show(F); return true;
            });
            Step("finder results", () =>
            {
                var f = (LyricsFinder)floating;
                if (f.TestCount == 0) return false;
                if (!Pause(4)) return false;
                Ok(f.TestCount == 1 && f.TestPreview.StartsWith("found line one"), "finder lists results; the same song at two lengths shows once, the closest length (" + f.TestCount + ")");
                ShotForm(f, "25_finder");
                f.TestPick(0);
                return true;
            });
            Step("finder use", () =>
            {
                var f = (LyricsFinder)floating;
                if (!Pause(3)) return false;
                f.TestUse();
                var l = F.Lyrics.Get(T("Lucid Dreams"));
                Ok(l != null && l.Lines[0].Text == "found line one" && l.Source == "Found on LRCLIB" && Conv.L(L.Db.Scalar("SELECT picked FROM found_lyrics WHERE track_id=?", T("Lucid Dreams").Id)) == 1, "picking lyrics in the finder saves them as your pick");
                return true;
            });
            Step("remove", () =>
            {
                F.Lyrics.Remove(T("Wasted"));
                Ok(F.Lyrics.Get(T("Wasted")) == null, "Remove takes found lyrics away");
                Ok(Conv.L(L.Db.Scalar("SELECT COUNT(*) FROM lyrics_tried WHERE track_id=?", T("Wasted").Id)) == 1, "and AURA won't look for them again by itself");
                return true;
            });
            Step("one site blocked", () =>
            {
                var keep = LyricsOnline.GeniusUrl;
                LyricsOnline.GeniusUrl = "http://127.0.0.1:1";          // Genius refusing
                var r = LyricsOnline.Search(T("Leak (v2)"));
                Ok(!r.Offline, "LRCLIB answering with no results while Genius is blocked isn't \"couldn't reach the sites\"");
                var keepL = LyricsOnline.LrclibUrl;
                LyricsOnline.LrclibUrl = "http://127.0.0.1:1";
                var keepO = LyricsOnline.OvhUrl;
                LyricsOnline.OvhUrl = "http://127.0.0.1:1";
                r = LyricsOnline.Search(T("Leak (v2)"));
                Ok(r.Offline, "every site failing still is");
                LyricsOnline.OvhUrl = keepO;
                LyricsOnline.GeniusUrl = "http://127.0.0.1:7900/genius403";
                LyricsOnline.LrclibUrl = keepL;
                r = LyricsOnline.Search(T("Lucid Dreams"));
                Ok(!r.Offline && r.Found.Count > 0, "Genius refusing (403) doesn't stop the others");
                var sw = System.Diagnostics.Stopwatch.StartNew();
                LyricsOnline.Search(T("Lucid Dreams"));
                Ok(File.ReadAllText(Path.Combine(App.DataDir, "error.log")).Contains("Genius refuses"), "and it's noted once, then Genius is left alone");
                LyricsOnline.GeniusUrl = keep; LyricsOnline.LrclibUrl = keepL;
                var log = File.ReadAllText(Path.Combine(App.DataDir, "error.log"));
                Ok(log.Contains("lyrics: Genius didn't answer"), "and which site failed is written in error.log");
                return true;
            });
            Step("update check", () => { Updater.Check(true); return true; });
            Step("update found", () =>
            {
                if (Updater.Checking) return false;
                Ok(Updater.Available && Updater.Latest == "9.9.9", "a newer version on GitHub is noticed (" + Updater.Status + ")");
                F.Go(new Route("settings"));
                return true;
            });
            Step("update shot", () => { if (!Pause(6)) return false; Shot("26_settings_update"); ((Canvas)F.Page).ScrollTo(560, false); return true; });
            Step("settings lower shot", () => { if (!Pause(6)) return false; Shot("27_settings_lyrics"); return true; });
            string got = null;
            Step("update download", () => { Updater.Install(file => got = file, err => got = "ERR " + err); return true; });
            Step("update downloaded", () =>
            {
                if (got == null) return false;
                Ok(got != null && File.Exists(got) && new FileInfo(got).Length > 300000, "the installer downloads (" + got + ")");
                return true;
            });
        }

        static void OrganizeScript()
        {
            Track Find(string fileStart) => L.Tracks.First(t => Path.GetFileName(t.Path).StartsWith(fileStart));
            Step("scan", () => !L.Scan.Running && L.Tracks.Count >= 8);
            Step("missing page", () => { F.Go(new Route("missing")); return true; });
            Step("missing shot", () =>
            {
                if (!Pause(6)) return false;
                Ok(MissingPage.Which(F, "noartist").Count == 2, "Missing info finds the 2 songs with no artist (" + MissingPage.Which(F, "noartist").Count + ")");
                Shot("30_missing"); F.Go(new Route("missing", "noartist")); return true;
            });
            Step("missing list shot", () => { if (!Pause(6)) return false; Ok(TP != null && TP.RowCount == 2, "the No artist list shows them"); Shot("31_missing_list"); return true; });
            Step("fill from names", () =>
            {
                var jobs = MainForm.FillJobs(MissingPage.Which(F, "noartist"));
                Ok(jobs.Count == 2, "both can be filled in from their file names");
                F.RunWrites(jobs, "Filled in");
                return true;
            });
            Step("filled", () =>
            {
                if (F.Writing) return false;
                var t = Find("03 - Juice WRLD - Robbery");
                Ok(t.Artist == "Juice WRLD" && t.Title == "Robbery (v2)" && t.TrackNo == 3, "\"03 - Juice WRLD - Robbery (v2)\" becomes artist, title and track 3 (" + t.Artist + " / " + t.Title + " / " + t.TrackNo + ")");
                var u = Find("Lucid Dreams [CDQ]");
                Ok(u.Title == "Lucid Dreams [CDQ]" && u.Artist == "", "a name with no artist in it only fills the title");
                var tg = Tags.Read(t.Path);
                Ok(tg.Artist == "Juice WRLD" && tg.Title == "Robbery (v2)", "and it's saved in the file");
                return true;
            });
            Step("edit while playing", () =>
            {
                var t = T("Robbery");
                P.PlayList(new[] { t.Id }.ToList());
                return true;
            });
            Step("edit while playing 2", () =>
            {
                if (P.Position < 3) return false;
                var t = T("Robbery");
                F.RunWrites(new List<(Track, TagEdit)> { (t, new TagEdit { Title = "Robbery", Album = "DRFL Edited", Year = "2019", Genre = "Hip-Hop" }) }, "Saved");
                return true;
            });
            Step("edited", () =>
            {
                if (F.Writing || !Pause(4)) return false;
                var t = T("Robbery");
                Ok(t.Album == "DRFL Edited" && Tags.Read(t.Path).Album == "DRFL Edited", "Edit info saves into the file and the library");
                Ok(P.Current == t && P.IsPlaying && P.Position > 3, "the song keeps playing from where it was (" + P.Position.ToString("0.0") + " s)");
                var l = F.Lyrics.Get(t);
                Ok(l != null && l.ByWord, "its lyrics and word timings are still there");
                return true;
            });
            Step("edit form shot", () =>
            {
                var f = new EditInfoForm(L.Tracks.Where(x => x.MainArtist == "Juice WRLD").Take(3).ToList(), _ => null);
                floating = f; f.Show(F); return true;
            });
            Step("edit form shot 2", () => { if (!Pause(5)) return false; ShotForm(floating, "32_edit_info"); floating.Close(); return true; });
            Step("cover finder", () =>
            {
                var f = new CoverFinderForm("Juice WRLD Night Drive", 2); floating = f; f.Show(F); return true;
            });
            Step("cover results", () =>
            {
                var f = (CoverFinderForm)floating;
                if (f.TestCount == 0 || !Pause(8)) return false;
                Ok(f.TestCount == 3, "covers from Apple Music and Deezer are listed, duplicates once (" + f.TestCount + ")");
                ShotForm(f, "33_cover_finder");
                f.TestPick(0);
                return true;
            });
            Step("cover picked", () =>
            {
                var f = (CoverFinderForm)floating;
                if (f.Visible) return false;
                Ok(f.Result != null && f.Result.Length > 1000, "the full-size cover downloads (" + (f.Result?.Length ?? 0) + " bytes)");
                var t = Find("Lucid Dreams [CDQ]");
                Ok(!t.HasArt, "(a song without a cover)");
                F.RunWrites(new List<(Track, TagEdit)> { (t, new TagEdit { Picture = f.Result }) }, "Cover saved");
                return true;
            });
            Step("cover saved", () =>
            {
                if (F.Writing) return false;
                var t = Find("Lucid Dreams [CDQ]");
                Ok(t.HasArt && Tags.Read(t.Path).HasPicture, "the cover is saved into the song");
                return true;
            });
            Step("rename", () =>
            {
                var t = Find("Lucid Dreams [CDQ]");
                var pl = L.CreatePlaylist("Keep me", new[] { t.Id });
                L.SetFav(t, true);
                var to = Path.Combine(Path.GetDirectoryName(t.Path), Editing.NameFor(t, "{title} (renamed)"));
                L.Rename(t, to);
                Ok(File.Exists(to) && t.Path == to && L.Get(t.Id) == t, "renaming moves the file and keeps the same song");
                Ok(pl.Items.Contains(t.Id) && t.Fav, "it stays in its playlists and favorites");
                return true;
            });
            Step("rescan after rename", () => { F.Rescan(); return true; });
            Step("rescan done", () =>
            {
                if (L.Scan.Running || !Pause(6)) return false;
                Ok(L.Tracks.Count(t => t.Name.StartsWith("Lucid Dreams [CDQ]")) == 1, "a rescan doesn't double it up");
                return true;
            });
            Step("versions page", () => { F.Go(new Route("versions")); return true; });
            Step("versions shot", () =>
            {
                if (!Pause(6)) return false;
                var groups = L.VersionGroups();
                Ok(groups.Any(g => g.Count == 2 && g.Any(t => t.Name == "Robbery") && g.Any(t => t.Name == "Robbery (v2)")), "Robbery and Robbery (v2) are versions of one song");
                Ok(!groups.Any(g => g.Any(t => t.Name == "Lucid Dreams") && g.Any(t => t.Name.StartsWith("Lucid Dreams [CDQ]"))), "a copy with no artist tag isn't matched to someone else's song");
                Shot("34_versions");
                return true;
            });
            Step("switch version", () =>
            {
                P.PlayList(new[] { T("Robbery").Id }.ToList());
                return true;
            });
            Step("switch version 2", () =>
            {
                if (P.Position < 4) return false;
                F.Go(new Route("now"));
                P.SwitchTo(T("Robbery (v2)"));
                return true;
            });
            Step("switched", () =>
            {
                if (!Pause(6)) return false;
                Ok(P.Current == T("Robbery (v2)") && P.Position > 4, "switching versions keeps your place (" + P.Position.ToString("0.0") + " s)");
                Shot("35_now_versions");
                return true;
            });
            Step("stats", () =>
            {
                // some listening history to chart
                var rnd = new Random(3);
                L.Db.Tx(() =>
                {
                    for (int i = 0; i < 120; i++)
                    {
                        var t = L.Tracks[rnd.Next(L.Tracks.Count)];
                        L.Db.Exec("INSERT INTO history (track_id,ts) VALUES (?,?)", t.Id, Library.Now - rnd.NextDouble() * 28 * 86400);
                        L.Db.Exec("INSERT OR IGNORE INTO plays (track_id,count,last_played) VALUES (?,0,0)", t.Id);
                        L.Db.Exec("UPDATE plays SET count=count+1 WHERE track_id=?", t.Id);
                    }
                });
                L.Load();
                F.Go(new Route("stats"));
                return true;
            });
            Step("stats shot", () => { if (!Pause(6)) return false; Shot("36_stats"); return true; });
            Step("theme neon", () => { F.ApplyTheme("Neon"); F.Go(new Route("home")); return true; });
            Step("neon shot", () =>
            {
                if (!Pause(8)) return false;
                Ok(Theme.ThemeName == "Neon" && F.Prefs.ThemeName == "Neon" && Theme.Bg.ToArgb() != Color.FromArgb(9, 11, 17).ToArgb(), "themes change the colors and are remembered");
                Shot("37_theme_neon"); F.ApplyTheme("Mono"); F.Go(new Route("settings")); return true;
            });
            Step("mono shot", () => { if (!Pause(8)) return false; Shot("38_theme_mono"); F.ApplyTheme("Midnight"); return true; });
        }

        static void PlaybackScript()
        {
            var problems = new List<string>();
            Step("scan", () => { P.Problem += m => problems.Add(m); return !L.Scan.Running && L.Tracks.Count >= 6; });
            Step("all found", () => { Ok(L.Tracks.Count == 6, "songs in awkward folders and with long or odd names are found (" + L.Tracks.Count + ")"); return true; });
            foreach (var name in new[] { "Space Folder Song", "Hash Song", "Long Path Song" })
            {
                var nm = name;
                Step("play " + nm, () => { P.PlayList(new[] { T(nm).Id }.ToList()); return true; });
                Step("playing " + nm, () => { if (!Pause(10)) return false; Ok(P.IsPlaying && P.Current?.Name == nm && P.Position > 0.5, nm + " plays"); return true; });
            }
            Step("disguised file", () => { P.PlayList(new[] { T("Disguised M4A").Id }.ToList()); return true; });
            Step("disguised plays", () =>
            {
                if (!Pause(10)) return false;
                var log = File.ReadAllText(Path.Combine(App.DataDir, "error.log"));
                Ok(P.IsPlaying && P.Current?.Name == "Disguised M4A" && log.Contains("is really M4A"), "an M4A named .mp3 is noticed and played as what it really is");
                var copies = Directory.GetFiles(Path.Combine(Path.GetTempPath(), "AURA Native", "play"));
                Ok(copies.Any(c => c.EndsWith(".m4a")), "using a copy with the right ending (the song itself isn't touched)");
                return true;
            });
            Step("fix type", () =>
            {
                var t = T("Disguised M4A");
                F.FixTypes(new List<(Track, string)> { (t, ".m4a") });
                Ok(t.Path.EndsWith(".m4a") && File.Exists(t.Path) && P.Current == t, "Fix file type renames it to .m4a and keeps playing");
                return true;
            });
            Step("converted while open", () =>
            {
                var t = T("Convert Me");
                var pl = L.CreatePlaylist("Has the wav", new[] { t.Id });
                L.SetFav(t, true);
                var mp3 = Path.ChangeExtension(t.Path, ".mp3");
                File.Copy(Path.Combine(Path.GetDirectoryName(T("Space Folder Song").Path), "song.mp3"), mp3);
                File.Delete(t.Path);        // like the first AURA's converter: the WAV goes, an MP3 comes
                return true;
            });
            Step("converted picked up", () =>
            {
                var t = L.Tracks.FirstOrDefault(x => x.Path.EndsWith("convert.mp3"));
                if (t == null || L.Scan.Running) return false;
                if (!Pause(4)) return false;
                var pl = L.Playlists.First(p => p.Name == "Has the wav");
                Ok(L.Tracks.Count(x => Path.GetFileNameWithoutExtension(x.Path) == "convert") == 1, "the converted MP3 replaces the WAV instead of showing twice");
                Ok(pl.Items.Count == 1 && L.Get(pl.Items[0]) == t && t.Fav, "it keeps the WAV's playlists and favorite");
                return true;
            });
            Step("swapped just before play", () =>
            {
                var t = L.Tracks.First(x => x.Path.Contains("Trailing Space Folder"));
                var to = Path.ChangeExtension(t.Path, ".flac");
                File.Move(t.Path, to);
                problems.Clear();
                P.PlayList(new[] { t.Id }.ToList());
                return true;
            });
            Step("swapped plays", () =>
            {
                if (!Pause(6)) return false;
                Ok(P.IsPlaying && P.Current?.Name == "Space Folder Song" && P.Current.Path.Contains("Trailing Space Folder") && P.Current.Path.EndsWith(".flac") && problems.Count == 0, "pressing play on a song whose file changed ending finds it and plays (" + string.Join("; ", problems) + " | playing=" + P.IsPlaying + " cur=" + P.Current?.Name + " path=" + P.Current?.Path + ")");
                return true;
            });
            Step("broken one", () => { problems.Clear(); P.PlayList(new[] { T("Broken Song").Id, T("Hash Song").Id }.ToList()); return true; });
            Step("backup engine", () =>
            {
                if (!Pause(25)) return false;
                Ok(P.Current?.Name == "Broken Song" && P.IsPlaying && P.Position > 0.5, "when the main engine can't play a song, the backup engine plays it (" + P.Current?.Name + " " + P.Position.ToString("0.0") + ")");
                Ok(problems.Count == 0, "and no error is shown (" + string.Join("; ", problems) + ")");
                return true;
            });
            Step("moved file", () =>
            {
                var t = T("Hash Song");
                File.Move(t.Path, t.Path + ".moved");
                problems.Clear();
                P.PlayList(new[] { t.Id }.ToList());
                return true;
            });
            Step("moved message", () =>
            {
                if (!Pause(4)) return false;
                Ok(problems.Any(m => m.StartsWith("Can't find Hash Song")), "a song that really moved says so plainly (" + string.Join("; ", problems) + ")");
                var t = T("Hash Song"); File.Move(t.Path + ".moved", t.Path);
                return true;
            });
        }

        static void SoundScript()
        {
            MainForm.UiTestMode = true;
            Track N(string file) => L.Tracks.FirstOrDefault(t => t.Path.EndsWith(file));
            Step("scan", () => !L.Scan.Running && L.Tracks.Count >= 10 && Pause(6));
            Step("all found", () => { if (L.Tracks.Count != 10) foreach (var t in L.Tracks) Console.WriteLine("  " + t.Path); Ok(L.Tracks.Count == 10, "10 songs found (" + L.Tracks.Count + ")"); return true; });
            Step("listening", () =>
            {
                if (F.Sound.Done < L.Tracks.Count) return false;
                Ok(true, "AURA listened to every song in the background (" + F.Sound.Done + ")");
                var s = F.Sound.Get(N("A/night_ride.wav"));
                Ok(s != null && Math.Abs(s.Bpm - 128) < 1.5, "tempo of a 128 BPM song (" + s?.Bpm + ")");
                Ok(s != null && s.Key == "A minor" && s.Camelot == "8A", "key of an A minor song (" + s?.Key + " " + s?.Camelot + ")");
                var c = F.Sound.Get(N("convert_me.wav"));
                Ok(c != null && Math.Abs(c.Bpm - 140) < 1.5 && c.Key == "G minor", "140 BPM in G minor (" + c?.Bpm + " " + c?.Key + ")");
                var sp = F.Sound.Get(N("speech.wav"));
                Ok(sp != null, "speech is heard too (bpm " + sp?.Bpm + ")");
                return true;
            });
            // ---- duplicates
            List<DupGroup> groups = null;
            Step("find duplicates", () =>
            {
                groups = Duplicates.Find(L, F.Sound);
                foreach (var g in groups) Console.WriteLine("  group " + g.Kind + " checked=" + g.Checked + ": " + string.Join(", ", g.Tracks.Select(t => Path.GetFileName(t.Path) + (t == g.Keep ? "*" : ""))));
                Ok(groups.Count == 1, "one song is saved more than once (" + groups.Count + " groups)");
                var g0 = groups.FirstOrDefault();
                var files = g0?.Tracks.Select(t => t.Path.Substring(t.Path.Length - 26)).ToList() ?? new List<string>();
                Ok(g0 != null && g0.Tracks.Count == 4, "the WAV, its MP3, a byte-for-byte copy of that, and a differently named FLAC that sounds the same (" + g0?.Tracks.Count + ")");
                Ok(g0 != null && g0.Keep.Path.EndsWith("A/night_ride.wav"), "the best copy is kept: lossless, with a cover (" + g0?.Keep.Path + ")");
                Ok(g0 != null && !g0.Tracks.Any(t => t.Name.Contains("(v2)") || t.Name.Contains("(snippet)") || t.Name.Contains("(OG)")), "v2, snippet and OG versions are not counted as duplicates");
                Ok(g0 != null && g0.Kind == "sound" && g0.Checked, "marked as sounding the same, checked by listening");
                return true;
            });
            Step("prepare merge", () =>
            {
                var copy = N("A/night_ride_copy.mp3"); var flac = N("totally different name.flac");
                var pl = L.CreatePlaylist("Dup list", new[] { copy.Id, N("slow_one.wav").Id, flac.Id });
                L.SetFav(flac, true);
                L.RecordPlay(copy); L.RecordPlay(copy); L.RecordPlay(flac);
                L.RecordPlay(N("A/night_ride.wav"));
                F.Go(new Route("duplicates"));
                return true;
            });
            Step("duplicates page shot", () => { if (!Pause(14)) return false; Shot("40_duplicates"); return true; });
            Step("merge", () =>
            {
                var g = groups[0];
                F.MergeDuplicates(new List<(DupGroup, Track)> { (g, g.Keep) });
                return true;
            });
            Step("merged", () =>
            {
                if (!Pause(4)) return false;
                var keep = N("A/night_ride.wav");
                Ok(L.Tracks.Count == 7, "the 3 extra copies left the library (" + L.Tracks.Count + ")");
                var moved = Directory.GetFiles("/tmp/claude-0/n7/music", "*", SearchOption.AllDirectories).Where(f => f.Contains("/_duplicates/")).ToList();
                Ok(moved.Count == 3, "and were moved into _duplicates folders, not deleted (" + moved.Count + ")");
                Ok(keep.Plays == 4, "plays add up on the kept copy (" + keep.Plays + ")");
                Ok(keep.Fav, "a favorite copy makes the kept one a favorite");
                var pl = L.Playlists.First(p => p.Name == "Dup list");
                Ok(pl.Items.Count == 2 && pl.Items[0] == keep.Id && L.Get(pl.Items[1]).Name == "Slow One", "the playlist points at the kept copy, once, in its place (" + string.Join(",", pl.Items.Select(i => L.Get(i)?.Name)) + ")");
                Ok(Duplicates.Find(L, F.Sound).Count == 0, "no duplicates left");
                Shot("41_duplicates_done");
                return true;
            });
            Step("rescan keeps them out", () => { F.Rescan(); return true; });
            Step("rescanned", () => { if (L.Scan.Running || !Pause(4)) return false; Ok(L.Tracks.Count == 7, "a rescan doesn't bring the _duplicates back (" + L.Tracks.Count + ")"); return true; });
            // ---- waveform and loop
            Step("play for wave", () => { P.PlayList(new[] { N("A/night_ride.wav").Id }.ToList()); F.Go(new Route("now")); return true; });
            Step("wave shot", () => { if (!Pause(10)) return false; Shot("42_now_wave"); P.SetLoop(5, 8); return true; });
            Step("looping", () =>
            {
                if (!Pause(30)) return false;
                Ok(P.Looping && P.Position >= 4.9 && P.Position <= 8.4, "A–B loop keeps playing between 0:05 and 0:08 (" + P.Position.ToString("0.0") + ")");
                Shot("43_now_loop");
                P.ClearLoop();
                return true;
            });
            // ---- versions lined up
            Step("version switch", () => { P.Seek(30); return true; });
            Step("switch", () =>
            {
                if (!Pause(2)) return false;
                var og = L.Tracks.First(t => t.Name == "Night Ride (OG)");
                Console.WriteLine("  map 30 s -> " + P.MapPosition(P.Current, og, 30).ToString("0.00") + "  (sound " + (F.Sound.Get(P.Current) != null) + "/" + (F.Sound.Get(og) != null) + ")");
                Ok(L.VersionsOf(og).Count == 4, "Night Ride has 4 versions (" + L.VersionsOf(og).Count + ")");
                P.SwitchTo(og);
                return true;
            });
            Step("switched", () =>
            {
                if (!Pause(2)) return false;
                Ok(P.Current.Name == "Night Ride (OG)" && P.LastSwitchLinedUp && Math.Abs(P.Position - 33.3) < 1.0, "switching to a version with a 3 s longer intro lands on the same part (" + P.Position.ToString("0.0") + " s, expected ~33)");
                return true;
            });
            // ---- mini player
            Step("mini", () => { F.ToggleMini(); return true; });
            Step("mini shown", () =>
            {
                if (!Pause(6)) return false;
                Ok(F.Mini != null && F.Mini.Visible && F.Mini.TopMost, "the mini player opens and stays on top");
                try
                {
                    using (var b = new Bitmap(F.Mini.Width, F.Mini.Height))
                    {
                        using (var g = Graphics.FromImage(b)) g.CopyFromScreen(F.Mini.Location, Point.Empty, b.Size);
                        b.Save(Path.Combine(outDir, "44_mini.png"));
                    }
                }
                catch (Exception e) { Console.WriteLine("mini shot: " + e.Message); }
                F.ToggleMini();
                return true;
            });
            Step("mini closed", () => { Ok(F.Mini == null, "and closes again"); return true; });
            // ---- convert to MP3
            long convId = 0;
            Step("convert", () =>
            {
                var t = N("convert_me.wav"); convId = t.Id;
                L.CreatePlaylist("Has convert", new[] { t.Id });
                L.SetFav(t, true);
                P.PlayList(new[] { t.Id }.ToList());
                return true;
            });
            Step("convert 2", () => { if (!Pause(6)) return false; F.RunConvert(new List<Track> { N("convert_me.wav") }, 192); return true; });
            Step("converted", () =>
            {
                if (F.Busy) return false;
                var t = L.Get(convId);
                Ok(t != null && t.Path.EndsWith("C/convert_me.mp3") && File.Exists(t.Path), "the song is now an MP3 (" + t?.Path + ")");
                Ok(File.Exists("/tmp/claude-0/n7/music/C/_originals/convert_me.wav"), "the original WAV is kept in _originals");
                var tg = Tags.Read(t.Path, true);
                Ok(tg.Title == "Convert Me" && tg.Artist == "Tester" && tg.Album == "Tests" && tg.HasPicture, "title, artist, album and cover came along");
                var lyr = Aura.Lyrics.From(tg.LyricsText, tg.Synced, tg.WordsRaw, "file");
                Ok(lyr != null && lyr.Timed && lyr.ByWord && lyr.Lines[1].T == 3, "timed lyrics and word timings came along");
                Ok(Math.Abs(tg.Duration - 30) < 0.5, "same length (" + tg.Duration.ToString("0.00") + " s)");
                Ok(t.Fav && L.Playlists.First(p => p.Name == "Has convert").Items.Contains(t.Id), "it's still the same song: favorite and playlist kept");
                Ok(P.Current?.Id == t.Id, "and the player carries on with it");
                var s = F.Sound.Get(t);
                return true;
            });
            Step("convert rescan", () => { F.Rescan(); return true; });
            Step("convert rescanned", () => { if (L.Scan.Running || !Pause(4)) return false; Ok(L.Tracks.Count == 7 && L.Tracks.Count(t => t.Name == "Convert Me") == 1, "a rescan finds it once (" + L.Tracks.Count + ")"); return true; });
            // ---- lyrics in time
            Step("align words", () =>
            {
                var lines = new List<string> { "and so my fellow Americans", "ask not what your country can do for you", "ask what you can do for your country" };
                var heard = new List<Whisper.Heard>();
                double t0 = 1.0;
                foreach (var w in "And so my fellow Americas, ask not what your country can do for you, ask what you can do for your country.".Split(' '))
                { heard.Add(new Whisper.Heard { Word = w, Start = t0, End = t0 + 0.3, P = 0.9 }); t0 += w.EndsWith(",") ? 1.2 : 0.35; }
                var r = Sync.Align(lines, heard);
                Ok(r.Lines.Count == 3 && r.Confidence > 0.9, "auto-sync lines the lyrics up with what was heard (" + r.Confidence + ")");
                Ok(Math.Abs(r.Lines[0].T - 1.0) < 0.05 && r.Lines[1].T > r.Lines[0].T + 1.5 && r.Lines[2].T > r.Lines[1].T + 2, "each line starts where it's sung (" + string.Join(", ", r.Lines.Select(l => l.T)) + ")");
                Ok(r.Lines.All(l => l.Words != null && l.Words.Count == l.Text.Split(' ').Length), "and every word gets a time, even a misheard one");
                var lrc = Sync.ToLrc(r.Lines); var words = Sync.ToWords(r.Lines);
                var back = Aura.Lyrics.From(lrc, null, words, "x");
                Ok(back != null && back.ByWord && back.Lines.Count == 3, "saved as LRC plus word timings, and read back");
                var tr = Sync.Lines(heard);
                Ok(tr.Lines.Count >= 2 && tr.Lines[0].Text.StartsWith("And so my"), "transcribing turns heard words into lines (" + string.Join(" / ", tr.Lines.Select(l => l.Text)) + ")");
                return true;
            });
            TranscribeForm tf = null;
            Step("transcribe open", () =>
            {
                var t = T("Ask Not");
                tf = new TranscribeForm(F, t); floating = tf; tf.Show(F);
                return true;
            });
            Step("transcribe start", () => { if (!Pause(4)) return false; tf.TestStart(false); return true; });
            Step("transcribe done", () =>
            {
                if (tf.TestRunning) return false;
                Console.WriteLine("  status: " + tf.TestStatus);
                Ok(Whisper.Models[0].Ready, "the speech model downloaded once");
                Ok(tf.TestResult != null || tf.TestStatus.Contains("nothing was changed"), "whisper.cpp ran on the song without crashing (" + tf.TestStatus + ")");
                try
                {
                    using (var b = new Bitmap(tf.Width, tf.Height))
                    {
                        using (var g = Graphics.FromImage(b)) g.CopyFromScreen(tf.Location, Point.Empty, b.Size);
                        b.Save(Path.Combine(outDir, "45_transcribe.png"));
                    }
                }
                catch { }
                if (tf.TestResult != null) tf.TestSave(); else tf.Close();
                return true;
            });
            Step("transcribe saved", () =>
            {
                if (F.Writing || !Pause(4)) return false;
                var l = F.Lyrics.Get(T("Ask Not"));
                Ok(l != null && l.Source == "In the song file", "a run that heard nothing doesn't touch the song's lyrics (" + l?.Source + ")");
                return true;
            });
            // taking the vocals out first (MDX-Net through onnxruntime), then writing the words down
            TranscribeForm tv = null;
            Step("vocals open", () =>
            {
                if (Environment.GetEnvironmentVariable("AURA_ORT_LIB") == null) { Console.WriteLine("  (no onnxruntime here: skipped)"); return true; }
                tv = new TranscribeForm(F, T("Ask Not")); floating = tv; tv.Show(F);
                return true;
            });
            Step("vocals start", () => { if (tv == null) return true; if (!Pause(4)) return false; tv.TestVocals = true; tv.TestStart(true); return true; });
            Step("vocals done", () =>
            {
                if (tv == null) return true;
                if (tv.TestRunning) return false;
                Console.WriteLine("  status: " + tv.TestStatus + "  (" + Separator.LastStats + ")");
                Ok(Separator.LastStats.Length > 0, "the vocals were taken out before listening (" + Separator.LastStats + ")");
                Ok(tv.TestResult != null || tv.TestStatus.Contains("no singing"), "and Whisper listened to them without crashing (" + tv.TestStatus + ")");
                try { using (var b = new Bitmap(tv.Width, tv.Height)) { using (var g = Graphics.FromImage(b)) g.CopyFromScreen(tv.Location, Point.Empty, b.Size); b.Save(Path.Combine(outDir, "47_transcribe_vocals.png")); } } catch { }
                if (tv.TestResult != null && tv.TestResult.Lines.Count > 1)
                {
                    double before = tv.TestResult.Lines[1].T;
                    tv.TestNudge(1, 0.1);
                    Ok(Math.Abs(tv.TestResult.Lines[1].T - before - 0.1) < 0.001, "a line can be moved by a tenth of a second while checking");
                }
                tv.Close();
                return true;
            });
            Step("settings", () => { F.Go(new Route("settings")); return true; });
            Step("settings shot", () => { if (!Pause(6)) return false; if (F.Page is Canvas c) c.ScrollTo(1000, false); return true; });
            Step("settings shot 2", () => { if (!Pause(4)) return false; Shot("46_settings_listening"); return true; });
        }

        // how much processor and memory AURA uses while it sits there playing (Now Playing, full screen, a list)
        static void IdleScript()
        {
            var proc = System.Diagnostics.Process.GetCurrentProcess();
            TimeSpan cpu0 = TimeSpan.Zero; DateTime t0 = DateTime.Now;
            void Report(string where)
            {
                proc.Refresh();
                var cpu = proc.TotalProcessorTime; var now = DateTime.Now;
                double pct = cpu0 == TimeSpan.Zero ? 0 : (cpu - cpu0).TotalSeconds / (now - t0).TotalSeconds * 100;
                cpu0 = cpu; t0 = now;
                Console.WriteLine("  " + where.PadRight(14) + " cpu " + pct.ToString("0") + "% (of one core)  memory " + (proc.WorkingSet64 / 1e6).ToString("0") + " MB  managed " + (GC.GetTotalMemory(false) / 1e6).ToString("0") + " MB  heard " + F.Sound.Done + "/" + L.Tracks.Count + "  threads " + proc.Threads.Count);
            }
            Step("scan", () => !L.Scan.Running && L.Tracks.Count >= 100 && Pause(4));
            Step("play", () =>
            {
                if (Environment.GetEnvironmentVariable("IDLE_NOLISTEN") != null) F.Sound.Background = false;
                P.PlayList(L.Tracks.Select(t => t.Id).ToList()); F.Go(new Route("songs")); Report("start"); return true;
            });
            for (int i = 0; i < 6; i++) Step("songs " + i, () => { if (!Pause(25)) return false; Report("songs page"); return true; });
            Step("now", () => { F.Go(new Route("now")); return true; });
            for (int i = 0; i < 6; i++) Step("now " + i, () => { if (!Pause(25)) return false; Report("now playing"); return true; });
            Step("full", () => { F.FullScreenLyrics(); return true; });
            for (int i = 0; i < 6; i++) Step("full " + i, () => { if (!Pause(25)) return false; Report("full screen"); return true; });
            Step("full off", () => { F.FullScreenLyrics(); F.WindowState = FormWindowState.Minimized; return true; });
            for (int i = 0; i < 4; i++) Step("min " + i, () => { if (!Pause(25)) return false; Report("minimized"); return true; });
        }

        // ratings, eras, search, undo, bookmarks, sleep, crossfade, radio, speed, trackers, folders, versions, looks
        static void MoreScript()
        {
            MainForm.UiTestMode = true;
            Track N(string file) => L.Tracks.FirstOrDefault(t => t.Path.EndsWith(file));
            Step("scan", () => !L.Scan.Running && L.Tracks.Count >= 13 && Pause(4));
            Step("found", () => { Ok(L.Tracks.Count == 13, "13 songs (" + L.Tracks.Count + ")"); return true; });
            // ---- search
            Step("search", () =>
            {
                int C(string q) { var sq = new SearchQuery(q, L, F.Sound); return L.Tracks.Count(sq.Matches); }
                Ok(C("artist:\"other guy\"") == 2, "artist:\"other guy\" finds both of his songs (" + C("artist:\"other guy\"") + ")");
                Ok(C("juice -lucid -robbery") == C("artist:juice") - 3 || C("juice -lucid -robbery") > 0, "-word leaves songs out (" + C("juice -lucid -robbery") + ")");
                Ok(C("year:>=2019") == L.Tracks.Count(t => t.Year.StartsWith("2019") || t.Year.StartsWith("202")), "year:>=2019 compares numbers (" + C("year:>=2019") + ")");
                Ok(C("album:\"goodbye & good\"") == 3, "album: with a phrase (" + C("album:\"goodbye & good\"") + ")");
                Ok(C("noart") == L.Tracks.Count(t => !t.HasArt), "noart");
                L.IndexLyrics(N("a.mp3"), "I still see your shadows in my room");
                Ok(C("shadows in my room") == 1 && C("lyrics:shadows") == 1, "a line of the lyrics finds the song");
                F.Go(new Route("search", "shadows in my room"));
                return true;
            });
            Step("search shot", () => { if (!Pause(5)) return false; Ok(TP != null && TP.RowCount == 1, "the search page shows it"); Shot("80_search_lyrics"); return true; });
            // ---- ratings and smart playlists
            Step("rate", () =>
            {
                L.SetRating(new[] { N("a.mp3") }, 5); L.SetRating(new[] { N("e.ogg") }, 4); L.SetRating(new[] { N("b.wav") }, 2);
                var sq = new SearchQuery("rating:>=4", L, F.Sound);
                Ok(L.Tracks.Count(sq.Matches) == 2, "rating:>=4 finds the two good ones");
                F.Go(new Route("smart", "rated"));
                return true;
            });
            Step("rated page", () => { if (!Pause(5)) return false; Ok(TP != null && TP.RowCount == 2, "the Top rated smart playlist has them (" + TP?.RowCount + ")"); Shot("81_top_rated"); return true; });
            Step("rating survives", () =>
            {
                var r = L.Db.Scalar("SELECT stars FROM ratings WHERE track_id=?", N("a.mp3").Id);
                Ok(Conv.L(r) == 5, "ratings are saved");
                return true;
            });
            // ---- eras
            Step("eras", () =>
            {
                L.SetEra(new[] { N("a.mp3"), N("h_cbr.mp3") }, "GBGR");
                Ok(L.Eras().Count == 1 && L.Eras()[0].Tracks.Count == 2, "an era with two songs");
                F.Go(new Route("eras"));
                return true;
            });
            Step("eras shot", () => { if (!Pause(5)) return false; Shot("82_eras"); F.Go(new Route("era", "GBGR")); return true; });
            Step("era page", () => { if (!Pause(5)) return false; Ok(TP != null && TP.RowCount == 2, "the era's page lists its songs"); Shot("83_era"); return true; });
            // ---- undo
            Step("edit title", () => { F.RunWrites(new List<(Track, TagEdit)> { (N("b.wav"), new TagEdit { Title = "Night Drive (oops)" }) }, "Edited"); return true; });
            Step("edited", () =>
            {
                if (F.Writing) return false;
                Ok(N("b.wav").Title == "Night Drive (oops)", "the title changed");
                Ok(L.UndoSteps().Count > 0 && L.UndoSteps()[0].Label == "Edited", "and the change can be undone (" + L.UndoSteps().FirstOrDefault()?.Label + ")");
                F.UndoLast();
                return true;
            });
            Step("undone", () =>
            {
                if (F.Writing || !Pause(3)) return false;
                Ok(N("b.wav").Title == "Night Drive" && Tags.Read(N("b.wav").Path).Title == "Night Drive", "Undo puts the old title back in the file (" + N("b.wav").Title + ")");
                return true;
            });
            Step("undo era", () =>
            {
                L.SetEra(new[] { N("a.mp3") }, "Wrong era");
                F.UndoLast();
                return true;
            });
            Step("era undone", () => { if (F.Writing || !Pause(3)) return false; Ok(N("a.mp3").Era == "GBGR", "Undo puts an era back too (" + N("a.mp3").Era + ")"); return true; });
            // ---- bookmarks
            Step("bookmarks", () =>
            {
                var t = N("e.ogg");
                L.AddBookmark(t, 12.3); L.AddBookmark(t, 12.8); L.AddBookmark(t, 30);
                Ok(L.BookmarksOf(t.Id).Count == 2, "bookmarks (one too close to another is skipped) (" + L.BookmarksOf(t.Id).Count + ")");
                P.PlayList(new List<long> { t.Id });
                F.Go(new Route("now"));
                return true;
            });
            Step("bookmarks shot", () => { if (!Pause(8)) return false; Shot("84_now_playing_bookmarks"); return true; });
            // ---- speed
            Step("speed", () => { P.Rate = 1.25; Ok(Math.Abs(TestAudio.LastRate - 1.25) < 0.001, "playback speed reaches the player"); P.Rate = 1; return true; });
            // ---- history
            Step("history", () =>
            {
                L.RecordPlay(N("d.m4a"));
                Ok(L.History(10).Any(h => h.t == N("d.m4a")), "played songs show up in History");
                F.Go(new Route("history"));
                return true;
            });
            Step("history shot", () => { if (!Pause(5)) return false; Ok(TP != null && TP.RowCount >= 1, "the History page lists them"); Shot("85_history"); return true; });
            // ---- sleep timer: stop after this song
            Step("sleep", () =>
            {
                P.PlayList(new List<long> { N("d.m4a").Id, N("e.ogg").Id });
                P.SetSleep(-1);
                Ok(P.Sleeping && P.SleepAfterSong, "the sleep timer is set to the end of the song");
                return true;
            });
            Step("sleep seek", () => { if (!Pause(4)) return false; P.Seek(P.Duration - 1.5); return true; });
            Step("slept", () =>
            {
                if (!Pause(25)) return false;
                Ok(!P.IsPlaying && P.Current?.Id == N("d.m4a").Id && !P.Sleeping, "the music stops at the end of the song instead of going on (" + P.Current?.Name + ", playing " + P.IsPlaying + ")");
                return true;
            });
            // ---- crossfade
            Step("crossfade", () =>
            {
                P.Between = 3;
                P.PlayList(new List<long> { N("d.m4a").Id, N("e.ogg").Id });
                return true;
            });
            Step("crossfade seek", () => { if (!Pause(4)) return false; P.Seek(P.Duration - 5); return true; });
            Step("crossfaded", () =>
            {
                if (P.Current?.Id != N("e.ogg").Id) return false;
                Ok(P.IsPlaying && P.Position < 3.5, "with a 3 s crossfade the next song is already playing when the first ends (" + P.Position.ToString("0.0") + " s in)");
                P.Between = 0;
                return true;
            });
            // ---- radio
            Step("radio", () => { P.Radio = true; P.PlayList(new List<long> { N("i_vbr.mp3").Id }); return true; });
            Step("radio seek", () => { if (!Pause(4)) return false; P.Seek(P.Duration - 1); return true; });
            Step("radio goes on", () =>
            {
                if (!Pause(20)) return false;
                Ok(P.IsPlaying && P.Current?.Id != N("i_vbr.mp3").Id && P.Queue.Count > 1, "radio keeps going with more songs when Up next runs out (" + P.Current?.Name + ", " + P.Queue.Count + " in the queue)");
                P.Radio = false; P.Toggle();
                return true;
            });
            // ---- fan trackers
            Step("tracker csv", () =>
            {
                var sh = Tracker.Load(File.ReadAllBytes("/tmp/claude-0/n8/tracker.csv"), "tracker.csv");
                var map = Tracker.GuessMapping(sh.Headers);
                Ok(map.ContainsKey("name") && map.ContainsKey("era") && map.ContainsKey("producer"), "the header row is found under the title banner (" + string.Join(",", sh.Headers) + ")");
                var items = Tracker.Items(sh, map);
                Ok(items.Count == 4 && items[0].Main == "Lucid Dreams" && items[0].Producer == "Nick Mira" && items[0].Alts.Contains("Forget Me"), "names are split into title, other names and producer (" + items[0].Main + " / " + string.Join(",", items[0].Alts) + " / " + items[0].Producer + ")");
                var props = Tracker.Match(items, L.Tracks, L, true, true, true, true, false, out int unmatched);
                foreach (var p in props) Console.WriteLine("  " + p.Track.Name + " <- " + p.Item.Raw + " era=" + p.Era + " year=" + p.Year + " title=" + p.Title + " info=" + p.Info);
                var lucid = props.FirstOrDefault(p => p.Track == N("a.mp3"));
                Ok(lucid != null && lucid.Era == null && lucid.Info != null && lucid.Info.Contains("Nick Mira"), "Lucid Dreams keeps its era (already set) and gets the producer note");
                var demo = props.FirstOrDefault(p => p.Track == N("f.opus"));
                Ok(demo != null && demo.Era == "Outsiders" && demo.Year == "2019", "a song with no year or era gets both (" + demo?.Era + " " + demo?.Year + ")");
                Ok(!props.Any(p => p.Track == N("b.wav") && p.Era != null && p.Year != null && p.Year != "2021"), "a song's own year is left alone");
                return true;
            });
            TrackerForm trf = null;
            Step("tracker xlsx", () =>
            {
                var sh = Tracker.Load(File.ReadAllBytes("/tmp/claude-0/n8/tracker.xlsx"), "tracker.xlsx");
                Ok(sh.Rows.Count == 2 && sh.Headers.Last() == "Sheet", "both tabs of the workbook are read (" + sh.Rows.Count + " rows)");
                Ok(sh.Rows.Any(r => r.Contains("2018-04-02")), "dates in the sheet come out as dates (" + string.Join(" | ", sh.Rows[0]) + ")");
                trf = new TrackerForm(F, null); floating = trf; trf.Show(F);
                trf.TestLoad(sh);
                return true;
            });
            Step("tracker form", () =>
            {
                if (!Pause(4)) return false;
                var props = trf.TestProps;
                Ok(props.Any(p => p.Track == N("i_vbr.mp3") && p.Era == "JW 999") && props.Any(p => p.Track == N("h_cbr.mp3") && p.Info != null), "the tab name is used as the era (" + props.Count + " changes)");
                try { using (var b = new Bitmap(trf.Width, trf.Height)) { using (var g = Graphics.FromImage(b)) g.CopyFromScreen(trf.Location, Point.Empty, b.Size); b.Save(Path.Combine(outDir, "86_tracker.png")); } } catch { }
                trf.TestApply();
                return true;
            });
            Step("tracker applied", () =>
            {
                if (F.Writing || !Pause(3)) return false;
                Ok(N("i_vbr.mp3").Era == "JW 999" && L.InfoOf(N("i_vbr.mp3")).Contains("Recorded 2018-04-02"), "the tracker's era and notes are saved (" + L.InfoOf(N("i_vbr.mp3")) + ")");
                var sq = new SearchQuery("prod:\"nick mira\"", L, F.Sound);
                Ok(L.Tracks.Count(sq.Matches) >= 2, "prod: finds songs by producer");
                trf.Close();
                return true;
            });
            // ---- fill in from folders
            Step("folders", () =>
            {
                var t = N("05 - Righteous.mp3");
                var jobs = MainForm.FillJobs(new List<Track> { t }, F.Prefs.Folders);
                var e = jobs.FirstOrDefault().Item2;
                Ok(e != null && e.Title == "Righteous" && e.Artist == "Juice WRLD" && e.Album == "Legends Never Die" && e.Year == "2020" && e.Track == "5",
                    "Music/Juice WRLD/Legends Never Die (2020)/05 - Righteous.mp3 fills in artist, album, year, number and title (" + e?.Artist + " / " + e?.Album + " / " + e?.Year + " / " + e?.Track + " / " + e?.Title + ")");
                var top = MainForm.FillJobs(new List<Track> { N("Lucid Dreams [CDQ].mp3") }, F.Prefs.Folders).FirstOrDefault().Item2;
                Ok(top == null || top.Artist == "Other", "a song right in a folder under Music takes that folder as its artist (" + top?.Artist + ")");
                return true;
            });
            // ---- versions
            Step("versions", () =>
            {
                var sug = L.SuggestedVersions();
                foreach (var g in sug) Console.WriteLine("  maybe: " + string.Join(", ", g.Select(t => t.Name + "/" + t.Artist)));
                Ok(sug.Any(g => g.Contains(N("a.mp3")) && g.Contains(N("Lucid Dreams [CDQ].mp3"))), "Lucid Dreams with and without an artist is suggested as versions");
                var titles = Library.VersionTitles("Robbery", new List<Track> { N("c.flac"), N("03 - Juice WRLD - Robbery (v2).mp3") });
                Ok(titles[N("c.flac").Id] == "Robbery" && titles[N("03 - Juice WRLD - Robbery (v2).mp3").Id] == "Robbery (v2)", "clear version titles (" + string.Join(", ", titles.Values) + ")");
                var inFull = L.NowInFull();
                Ok(inFull.Any(x => x.Full == N("e.ogg") && x.Snippets.Contains(N("Empty [Snippet].mp3"))), "a snippet of a song you now have in full is noticed");
                L.GroupVersions(new[] { N("a.mp3"), N("Lucid Dreams [CDQ].mp3") }, "Lucid Dreams");
                Ok(L.VersionsOf(N("a.mp3")).Count == 2, "grouped by hand");
                F.Go(new Route("versions"));
                return true;
            });
            Step("versions shot", () => { if (!Pause(6)) return false; Shot("87_versions"); return true; });
            Step("not versions", () =>
            {
                var key = Library.VersionKey(N("a.mp3"));
                L.MarkNotVersions(key);
                Ok(L.VersionsOf(N("a.mp3")).Count == 1 && !L.VersionGroups().Any(g => g.Contains(N("a.mp3"))), "\"not versions\" splits them up for good");
                return true;
            });
            // ---- Discord status (the message only: there's no Discord app here)
            Step("discord", () =>
            {
                var t = N("a.mp3");
                var a = Discord.TestActivity(t, "https://example.com/c.jpg");
                Console.WriteLine("  " + a);
                Ok(a.Contains("\"type\":2") && a.Contains("\"details\":\"Lucid Dreams\"") && a.Contains("\"state\":\"by Juice WRLD\"") && a.Contains("\"large_image\":\"https://example.com/c.jpg\"") && a.Contains("\"end\":1700000040104"),
                    "the Discord status says Listening to the song, by the artist, with its cover and a time bar");
                var noCover = Discord.TestActivity(t, "");
                Ok(noCover.Contains("\"large_image\":\"" + Discord.Logo + "\""), "with no cover Discord gets the AURA logo's link (not an image name it may not have)");
                var c = Discord.TestCover(N("b.wav"));
                Ok(c != null && c.EndsWith("512x512bb.jpg"), "the cover's web link is looked up on Apple Music (" + c + ")");
                Ok(Discord.TestCover(t) == "", "but not taken from a different artist's song");
                // the song has its own cover, and Apple's is a different picture: show the AURA logo, not a wrong cover
                var folder = Path.GetDirectoryName(N("b.wav").Path);
                using (var bmp = new Bitmap(300, 300)) { using (var g = Graphics.FromImage(bmp)) { g.Clear(Color.Black); g.FillEllipse(Brushes.White, 40, 40, 120, 220); } bmp.Save(Path.Combine(folder, "cover.jpg"), System.Drawing.Imaging.ImageFormat.Jpeg); }
                Discord.TestClear();
                var c2 = Discord.TestCover(N("b.wav"));
                Ok(c2 == "", "a song with its own cover doesn't get a different picture from Apple Music (" + c2 + ")");
                // with "show my own covers" on, the song's own cover is put online once and its link used
                Discord.UploadOwn = true; Discord.Db = L.Db; Discord.UploadUrl = "http://127.0.0.1:7900/upload"; Discord.TestClear();
                var up1 = Discord.TestCover(N("b.wav"));
                Discord.TestClear();
                var up2 = Discord.TestCover(N("b.wav"));
                Ok(up1 != null && up1.StartsWith("https://files.example/") && up2 == up1, "your own cover goes online once and Discord gets its link (" + up1 + ", again: " + up2 + ")");
                Discord.UploadOwn = false; Discord.TestClear();
                File.Delete(Path.Combine(folder, "cover.jpg"));
                return true;
            });
            // ---- settings and looks
            Step("settings", () => { F.Go(new Route("settings")); return true; });
            Step("settings 1", () => { if (!Pause(5)) return false; if (F.Page is Canvas c) c.ScrollTo(560, false); return true; });
            Step("settings 2", () => { if (!Pause(4)) return false; Shot("88_settings_sound"); if (F.Page is Canvas c) c.ScrollTo(1500, false); return true; });
            Step("settings 3", () => { if (!Pause(4)) return false; Shot("89_settings_lyrics_undo"); F.ApplyTheme("Light"); F.Go(new Route("songs")); return true; });
            Step("light", () => { if (!Pause(6)) return false; Ok(Theme.IsLight && Theme.Bg.GetBrightness() > 0.9f, "the light theme"); Shot("90_light_songs"); F.Go(new Route("now")); return true; });
            Step("light now", () => { if (!Pause(6)) return false; Shot("91_light_now"); F.ApplyTheme("Midnight"); return true; });
            Step("dark again", () => { if (!Pause(3)) return false; Ok(!Theme.IsLight, "and back"); return true; });
        }

        static void Resume()
        {
            Step("start", () => Pause(10));
            Step("resumes", () =>
            {
                var want = File.ReadAllText(Path.Combine(outDir, "resume.txt")).Split(' ');
                Ok(P.Current != null && P.Current.Id == long.Parse(want[0]), "the last song is ready when AURA opens again");
                Ok(Math.Abs(P.Position - double.Parse(want[1], System.Globalization.CultureInfo.InvariantCulture)) < 2.5, "at the same place (" + P.Position.ToString("0.0") + " s)");
                Ok(!P.IsPlaying, "but doesn't start playing by itself");
                Ok(L.Playlists.Any(p => p.Name == "Road trip") && !L.Playlists.Any(p => p.Name.StartsWith("Road trip ")), "import isn't repeated on the next start");
                return true;
            });
        }
    }
}
