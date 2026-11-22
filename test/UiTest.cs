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
                Ok(F.Lyrics.Get(T("Hear Me Calling")) == null, "a song with no lyrics has none");
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
                Ok(F.Lyrics.Get(T("Hear Me Calling")) == null, "nothing found: nothing saved");
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
            Step("full screen", () => { F.FullScreenLyrics(); P.Seek(4.6); return true; });
            Step("full screen shot", () =>
            {
                if (!Pause(14)) return false;
                var full = Application.OpenForms.OfType<FullLyricsForm>().FirstOrDefault();
                Ok(full != null && full.Visible, "full-screen lyrics opens");
                if (full != null) ShotForm(full, "22_fullscreen");
                F.FullScreenLyrics();     // second press closes it
                return true;
            });
            Step("full screen closed", () =>
            {
                if (!Pause(4)) return false;
                Ok(!Application.OpenForms.OfType<FullLyricsForm>().Any(), "F11 again closes it");
                return true;
            });
            Step("song without lyrics", () => { P.PlayList(new[] { T("Hear Me Calling").Id }.ToList()); return true; });
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
                var r = LyricsOnline.Search(T("Hear Me Calling"));
                Ok(!r.Offline, "LRCLIB answering with no results while Genius is blocked isn't \"couldn't reach the sites\"");
                var keepL = LyricsOnline.LrclibUrl;
                LyricsOnline.LrclibUrl = "http://127.0.0.1:1";
                r = LyricsOnline.Search(T("Hear Me Calling"));
                Ok(r.Offline, "both sites failing still is");
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
