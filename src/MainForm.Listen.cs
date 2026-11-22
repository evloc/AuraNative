using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Aura
{
    // Ratings, eras, bookmarks, sleep timer, speed, between songs, leveling, radio, Discord, undo, and the keys.
    sealed partial class MainForm
    {
        Discord discord;
        bool undoing;

        void InitListen()
        {
            Player.Between = Prefs.Between;
            Player.Leveling = Prefs.Leveling;
            Player.Radio = Prefs.Radio;
            Player.Rate = Prefs.Rate;
            // leveling: loud songs are turned down to about the same loudness as the rest
            Player.GainFor = t =>
            {
                var s = Sound.Get(t);
                if (s == null || double.IsNaN(s.Loud)) return 1;
                return Math.Pow(10, (-16.0 - s.Loud) / 20);
            };
            Player.BpmOf = t => Sound.Get(t)?.Bpm ?? 0;
            Player.SleepChanged += () => Bar.Invalidate();
            Player.TrackChanged += () => discord?.Update(Player.Current, Player.IsPlaying, Player.Position);
            Player.StateChanged += () => discord?.Update(Player.Current, Player.IsPlaying, Player.Position);
            ApplyDiscord();
            // the lyrics index (for finding a song by a line of it) fills in slowly in the background
            Lib.StartLyricsIndex(t =>
            {
                var l = Lyrics.Get(t);
                return l == null ? "" : l.Timed ? string.Join("\n", l.Lines.Select(x => x.Text)) : l.Plain;
            });
        }

        public void ApplyDiscord()
        {
            discord?.Dispose(); discord = null;
            if (Prefs.Discord && Prefs.DiscordApp.Trim().Length > 0) { discord = new Discord(Prefs.DiscordApp.Trim()); discord.Update(Player.Current, Player.IsPlaying, Player.Position); }
        }

        // ---------- keys (not while typing) ----------
        bool ListenKey(Keys key)
        {
            var t = Player.Current;
            switch (key)
            {
                case Keys.B: if (t != null) { Lib.AddBookmark(t, Player.Position); Toast("Bookmarked " + Theme.Time(Player.Position) + " in " + t.Name); Page?.Invalidate(); } return true;
                case Keys.K: FullScreenLyrics(); return true;
                case Keys.M: ToggleMini(); return true;
                case Keys.N: if (Current?.Kind == "now") Back(); else Go(new Route("now")); return true;
                case Keys.Q: if (Current?.Kind == "queue") Back(); else Go(new Route("queue")); return true;
                case Keys.F: if (t != null) { Lib.SetFav(t, !t.Fav); Toast(t.Fav ? "Added to Favorites" : "Removed from Favorites"); } return true;
                case Keys.OemOpenBrackets: if (t != null) { Player.SetLoop(Player.Position, Player.LoopB > Player.Position ? Player.LoopB : Math.Min(Player.Duration, Player.Position + 8)); Toast("Loop starts at " + Theme.Time(Player.LoopA)); } return true;
                case Keys.OemCloseBrackets: if (t != null) { Player.SetLoop(Player.LoopA >= 0 ? Player.LoopA : 0, Player.Position); Toast("Loop ends at " + Theme.Time(Player.LoopB)); } return true;
                case Keys.OemPipe: case Keys.OemBackslash: Player.ClearLoop(); Toast("Loop off"); return true;
                case Keys.Shift | Keys.Right: Player.Seek(Player.Position + 10); return true;
                case Keys.Shift | Keys.Left: Player.Seek(Player.Position - 10); return true;
                case Keys.OemQuestion: Head.Search.Focus(); Head.Search.SelectAll(); return true;
                case Keys.D1: case Keys.D2: case Keys.D3: case Keys.D4: case Keys.D5: case Keys.D0:
                    if (t != null && (ModifierKeys & Keys.Alt) == 0) { int n = key == Keys.D0 ? 0 : key - Keys.D0; Lib.SetRating(new[] { t }, n); Toast(n == 0 ? "Rating cleared" : "Rated " + new string('★', n)); return true; }
                    return false;
            }
            return false;
        }

        // ---------- menus ----------
        ToolStripMenuItem RateMenu(IList<Track> tracks)
        {
            var m = Menus.Item("Rate", null);
            Menus.Sub(m);
            int cur = tracks.All(t => t.Rating == tracks[0].Rating) ? tracks[0].Rating : -1;
            for (int i = 5; i >= 1; i--)
            {
                int n = i;
                m.DropDownItems.Add(Menus.Item(new string('★', i) + new string('☆', 5 - i) + (cur == i ? "   ✓" : ""), () => Lib.SetRating(tracks, n)));
            }
            m.DropDownItems.Add(new ToolStripSeparator());
            m.DropDownItems.Add(Menus.Item("No rating", () => Lib.SetRating(tracks, 0), cur != 0));
            return m;
        }

        void AskEra(IList<Track> tracks)
        {
            var eras = Lib.Eras().Select(e => e.Name).ToList();
            string now = tracks.All(t => t.Era == tracks[0].Era) ? tracks[0].Era : "";
            var n = Ask.ForText(this, "Set era", "Era or session for " + (tracks.Count == 1 ? tracks[0].Name : Theme.Count(tracks.Count, "song")) + (eras.Count > 0 ? "\n(existing: " + string.Join(", ", eras.Take(6)) + (eras.Count > 6 ? "…" : "") + ")" : "") + "\nLeave empty to clear it.", now, "Set", true);
            if (n == null) return;
            // match an existing era's spelling
            var same = eras.FirstOrDefault(e => string.Equals(e, n.Trim(), StringComparison.CurrentCultureIgnoreCase));
            Lib.SetEra(tracks, same ?? n.Trim());
            Toast(n.Trim().Length == 0 ? "Era cleared" : "Set the era to " + (same ?? n.Trim()));
        }

        public void SleepMenu(Point screen)
        {
            var m = Menus.New();
            if (Player.Sleeping) m.Items.Add(Menus.Item("Stops " + (Player.SleepAfterSong ? "after this song" : "in " + Player.SleepText) + " — turn off", () => { Player.SetSleep(0); Toast("Sleep timer off"); }));
            foreach (var min in new[] { 15, 30, 45, 60, 90 }) { int mm = min; m.Items.Add(Menus.Item("In " + min + " minutes", () => { Player.SetSleep(mm); Toast("The music stops in " + mm + " minutes"); })); }
            m.Items.Add(Menus.Item("At the end of this song", () => { Player.SetSleep(-1); Toast("The music stops after this song"); }, Player.Current != null));
            m.Show(screen);
        }

        public void SpeedMenu(Point screen)
        {
            var m = Menus.New();
            foreach (var r in new[] { 0.75, 0.85, 1.0, 1.1, 1.25, 1.5 })
            {
                double rr = r;
                m.Items.Add(Menus.Item(r.ToString("0.##") + "×" + (Math.Abs(Player.Rate - r) < 0.01 ? "   ✓" : ""), () => { Player.Rate = rr; Prefs.Rate = rr; Prefs.Save(); Bar.Invalidate(); Page?.Invalidate(); }));
            }
            m.Show(screen);
        }

        // ---------- undo ----------
        public void UndoLast()
        {
            var step = Lib.UndoSteps().FirstOrDefault();
            if (step == null) { Toast("Nothing to undo"); return; }
            Undo(step);
        }

        public void Undo(Library.UndoStep step)
        {
            var jobs = new List<(Track, TagEdit)>();
            var eras = new Dictionary<string, List<Track>>();
            var rates = new Dictionary<int, List<Track>>();
            foreach (var r in step.Rows)
            {
                if (!long.TryParse(r[0], out var id) || !(Lib.Get(id) is Track t)) continue;
                int.TryParse(r[8], out var no);
                if (t.Title != r[2] || t.Artist != r[3] || t.Album != r[4] || t.AlbumArtist != r[5] || t.Year != r[6] || t.Genre != r[7] || t.TrackNo != no)
                    jobs.Add((t, new TagEdit { Title = r[2], Artist = r[3], Album = r[4], AlbumArtist = r[5], Year = r[6], Genre = r[7], Track = no > 0 ? no.ToString() : "" }));
                if (t.Era != r[9]) { if (!eras.ContainsKey(r[9])) eras[r[9]] = new List<Track>(); eras[r[9]].Add(t); }
                int.TryParse(r[10], out var stars);
                if (t.Rating != stars) { if (!rates.ContainsKey(stars)) rates[stars] = new List<Track>(); rates[stars].Add(t); }
            }
            Lib.DropUndo(step.Id);
            foreach (var kv in eras) Lib.SetEra(kv.Value, kv.Key, false);
            foreach (var kv in rates) Lib.SetRating(kv.Value, kv.Key);
            if (jobs.Count == 0) { Toast("Undid: " + step.Label); OnLibraryChanged(); return; }
            undoing = true;
            RunWrites(jobs, "Undid " + step.Label.ToLowerInvariant());
        }

        // ---------- lyrics by hand ----------
        public void EditLyrics(Track t)
        {
            using (var f = new LyricsEditForm(this, t)) f.ShowDialog(this);
        }

        public void SyncByHand(Track t)
        {
            var l = Lyrics.Get(t);
            if (l == null) { Toast("This song has no lyrics yet — add them first (Edit lyrics or Find lyrics)"); return; }
            using (var f = new SyncByHandForm(this, t, l)) f.ShowDialog(this);
        }

        public void GroupAsVersions(IList<Track> tracks)
        {
            var name = Ask.ForText(this, "Group as versions", "Song name for these " + tracks.Count + " versions", LyricsOnline.CleanTitle(tracks[0].Name), "Group");
            if (string.IsNullOrWhiteSpace(name)) return;
            Lib.GroupVersions(tracks, name.Trim());
            Toast("Grouped " + Theme.Count(tracks.Count, "version") + " as " + name.Trim());
            OnLibraryChanged();
        }
    }
}
