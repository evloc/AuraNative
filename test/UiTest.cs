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
            Step("update check", () => { Updater.Check(true); return true; });
            Step("update found", () =>
            {
                if (Updater.Checking) return false;
                Ok(Updater.Available && Updater.Latest == "0.2.0", "a newer version on GitHub is noticed (" + Updater.Status + ")");
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
