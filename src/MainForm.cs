using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Aura
{
    sealed partial class MainForm : Form, IShell
    {
        public Library Lib { get; }
        public Player Player { get; }
        public Settings Prefs { get; }
        public LyricsStore Lyrics { get; }
        FullLyricsForm full;

        internal readonly Sidebar Side;
        internal readonly PlayerBar Bar;
        internal readonly TopBar Head;
        readonly Panel host;
        internal Control Page;
        readonly ToastView toast;
        readonly List<Route> history = new List<Route>();
        int hist = -1;
        readonly Timer saveTimer, rescanTimer;
        readonly List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();
        bool rescanAfter;

        public MainForm(Library lib, Player player, Settings prefs)
        {
            Lib = lib; Player = player; Prefs = prefs;
            Lyrics = new LyricsStore(lib);
            Lyrics.Changed += id =>
            {
                if (Page is NowPlayingPage np && Player.Current?.Id == id) np.Reload();
                if (Page is SettingsPage sp) sp.Invalidate();
            };
            Lyrics.AutoProgress += () => { if (Page is SettingsPage sp) sp.Invalidate(); };
            Updater.Changed += () => { Head.Invalidate(); if (Page is SettingsPage sp) sp.Invalidate(); };
            Text = App.Name;
            BackColor = Theme.Side;
            ForeColor = Theme.Text;
            Font = Theme.Font(13.5f);
            MinimumSize = new Size(Theme.P(900), Theme.P(600));
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            try { if (App.IsWindows) Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            RestoreBounds_();

            Side = new Sidebar(this) { Dock = DockStyle.Left };
            Bar = new PlayerBar(this) { Dock = DockStyle.Bottom };
            Head = new TopBar(this) { Dock = DockStyle.Top };
            host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            var right = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            right.Controls.Add(host);
            right.Controls.Add(Head);
            Controls.Add(right);
            Controls.Add(Side);
            Controls.Add(Bar);
            toast = new ToastView { Visible = false };
            Controls.Add(toast);
            toast.BringToFront();

            Lib.Changed += OnLibraryChanged;
            Lib.ScanProgress += () => { Side.Invalidate(); Head.Invalidate(); if (Lib.Tracks.Count == 0) Page?.Invalidate(); };
            Player.TrackChanged += OnTrackChanged;
            Player.StateChanged += () => { Bar.Invalidate(); Page?.Invalidate(); };
            Player.Tick += () => Bar.Invalidate();
            Player.QueueChanged += () => { if (Page is TrackPage tp && tp.Route.Kind == "queue") tp.Reload(); };
            Player.Problem += Toast;
            Art.Ready += id =>
            {
                Page?.Invalidate(); Bar.Invalidate();
                if (id == Player.Current?.Id) FollowCover();
            };
            Theme.Changed += () => { Page?.Invalidate(); Bar.Invalidate(); Side.Invalidate(); Head.Invalidate(); };

            saveTimer = new Timer { Interval = 30000 };
            saveTimer.Tick += (s, e) => SaveState();
            saveTimer.Start();
            rescanTimer = new Timer { Interval = 4000 };
            rescanTimer.Tick += (s, e) => { rescanTimer.Stop(); Rescan(); };

            InitMore();
            Go(new Route("home"));
        }

        // ---------- window position ----------
        void RestoreBounds_()
        {
            var b = new Rectangle(Prefs.WinX, Prefs.WinY, Math.Max(Prefs.WinW, Theme.P(900)), Math.Max(Prefs.WinH, Theme.P(600)));
            bool visible = Prefs.WinX >= -10000 && Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(new Rectangle(b.X + 40, b.Y + 10, Math.Max(1, b.Width - 80), 40)));
            if (Prefs.WinX == -1 || !visible)
            {
                var wa = Screen.PrimaryScreen.WorkingArea;
                int w = Math.Min(Theme.P(1360), wa.Width - 40), h = Math.Min(Theme.P(860), wa.Height - 40);
                b = new Rectangle(wa.X + (wa.Width - w) / 2, wa.Y + (wa.Height - h) / 2, w, h);
            }
            Bounds = b;
            if (Prefs.Maximized) WindowState = FormWindowState.Maximized;
        }

        void SaveState()
        {
            try
            {
                Player.SaveTo(Prefs);
                if (WindowState == FormWindowState.Normal) { Prefs.WinX = Left; Prefs.WinY = Top_; Prefs.WinW = Width; Prefs.WinH = Height; }
                Prefs.Maximized = WindowState == FormWindowState.Maximized;
                Prefs.Save();
            }
            catch (Exception e) { App.Log("save: " + e.Message); }
        }
        int Top_ => base.Top;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Win.DarkTitle(Handle);
            if (App.IsWindows)
            {
                try
                {
                    // the keyboard's media keys, even when AURA isn't the window in front
                    Win.RegisterHotKey(Handle, 1, 0x4000, 0xB3);   // play/pause
                    Win.RegisterHotKey(Handle, 2, 0x4000, 0xB0);   // next
                    Win.RegisterHotKey(Handle, 3, 0x4000, 0xB1);   // previous
                    Win.RegisterHotKey(Handle, 4, 0x4000, 0xB2);   // stop
                }
                catch { }
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (TaskbarMessage(ref m)) return;
            if (m.Msg == 0x0312)        // WM_HOTKEY
            {
                switch (m.WParam.ToInt32())
                {
                    case 1: Player.Toggle(); break;
                    case 2: Player.Next(); break;
                    case 3: Player.Prev(); break;
                    case 4: if (Player.IsPlaying) Player.Toggle(); break;
                }
                return;
            }
            if (m.Msg == 0x0319)        // WM_APPCOMMAND (media keys while AURA is in front)
            {
                int cmd = (int)((m.LParam.ToInt64() >> 16) & 0x0FFF);
                bool done = true;
                switch (cmd)
                {
                    case 14: Player.Toggle(); break;          // play/pause
                    case 46: if (!Player.IsPlaying) Player.Toggle(); break;
                    case 47: if (Player.IsPlaying) Player.Toggle(); break;
                    case 11: Player.Next(); break;
                    case 12: Player.Prev(); break;
                    case 13: if (Player.IsPlaying) Player.Toggle(); break;
                    default: done = false; break;
                }
                if (done) { m.Result = new IntPtr(1); return; }
            }
            if (Win.ShowMessage != 0 && m.Msg == Win.ShowMessage)
            {
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Activate();
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveState();
            if (App.IsWindows) for (int i = 1; i <= 4; i++) try { Win.UnregisterHotKey(Handle, i); } catch { }
            CloseMore();
            foreach (var w in watchers) w.Dispose();
            base.OnFormClosing(e);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            PlaceToast();
        }

        // ---------- keys ----------
        protected override bool ProcessCmdKey(ref Message msg, Keys key)
        {
            bool typing = ActiveControl is TextBox || (ActiveControl is ContainerControl cc && cc.ActiveControl is TextBox) || Head.Search.Focused;
            switch (key)
            {
                case Keys.Control | Keys.F: Head.Search.Focus(); Head.Search.SelectAll(); return true;
                case Keys.Control | Keys.Right: Player.Next(); return true;
                case Keys.Control | Keys.Left: Player.Prev(); return true;
                case Keys.Control | Keys.Up: Player.Volume += 5; Bar.Invalidate(); return true;
                case Keys.Control | Keys.Down: Player.Volume -= 5; Bar.Invalidate(); return true;
                case Keys.F11: FullScreenLyrics(); return true;
                case Keys.Control | Keys.M: ToggleMini(); return true;
                case Keys.Control | Keys.L: if (Current?.Kind == "now") Back(); else Go(new Route("now")); return true;
                case Keys.Alt | Keys.Left: Back(); return true;
                case Keys.Alt | Keys.Right: Forward(); return true;
                case Keys.Space: if (!typing) { Player.Toggle(); return true; } break;
                case Keys.MediaPlayPause: Player.Toggle(); return true;
                case Keys.MediaNextTrack: Player.Next(); return true;
                case Keys.MediaPreviousTrack: Player.Prev(); return true;
                case Keys.Control | Keys.Z: if (!typing) { UndoLast(); return true; } break;
            }
            if (!typing && ListenKey(key)) return true;
            return base.ProcessCmdKey(ref msg, key);
        }

        // ---------- navigation ----------
        public Route Current => hist >= 0 && hist < history.Count ? history[hist] : null;
        public bool CanBack => hist > 0;
        public bool CanForward => hist < history.Count - 1;

        public void Go(Route r)
        {
            if (Current != null && Current.Same(r))
            {
                if (Page is Canvas c) c.ScrollTo(0);
                return;
            }
            if (Current != null && Page is Canvas pc) Current.Scroll = pc.Offset;
            if (hist < history.Count - 1) history.RemoveRange(hist + 1, history.Count - hist - 1);
            // a new search replaces the last one instead of piling up
            if (r.Kind == "search" && Current?.Kind == "search") history[hist] = r;
            else { history.Add(r); hist = history.Count - 1; }
            if (history.Count > 60) { history.RemoveAt(0); hist--; }
            ShowPage(r);
        }

        public void Back()
        {
            if (!CanBack) return;
            if (Page is Canvas pc) Current.Scroll = pc.Offset;
            hist--;
            ShowPage(Current);
        }

        public void Forward()
        {
            if (!CanForward) return;
            if (Page is Canvas pc) Current.Scroll = pc.Offset;
            hist++;
            ShowPage(Current);
        }

        void ShowPage(Route r)
        {
            Control page;
            switch (r.Kind)
            {
                case "home": page = new HomePage(this); break;
                case "now": page = new NowPlayingPage(this); break;
                case "missing" when r.Key.Length == 0: page = new MissingPage(this); break;
                case "versions": page = new VersionsPage(this); break;
                case "duplicates": page = new DuplicatesPage(this); break;
                case "stats": page = new StatsPage(this); break;
                case "artists": case "albums": case "eras": page = new GridPage(this, r); break;
                case "settings":
                    {
                        var sp = new SettingsPage(this);
                        sp.Changed = LookChanged;
                        sp.ThemeChanged = ApplyTheme;
                        sp.ImportAgain = () => ImportOld(true);
                        page = sp; break;
                    }
                default: page = new TrackPage(this, r); break;
            }
            page.Dock = DockStyle.Fill;
            var old = Page;
            host.SuspendLayout();
            host.Controls.Add(page);
            Page = page;
            if (old != null) { host.Controls.Remove(old); old.Dispose(); }
            host.ResumeLayout();
            if (r.Kind == "search") Head.Show(r.Key); else if (!Head.Search.Focused) Head.Show("");
            if (!Head.Search.Focused) page.Focus();
            if (r.Scroll > 0 && page is Canvas c) BeginInvoke(new Action(() => { page.Refresh(); c.ScrollTo(r.Scroll, false); }));
            Side.Invalidate(); Head.Invalidate(); Bar.Invalidate();
        }

        void ReloadPage()
        {
            switch (Page)
            {
                case TrackPage tp:
                    tp.Reload();
                    if (tp.Gone) { history.RemoveAt(hist); hist = Math.Max(0, hist - 1); if (history.Count == 0) { history.Add(new Route("home")); hist = 0; } ShowPage(Current); }
                    break;
                case GridPage gp: gp.Reload(); break;
                case HomePage hp: hp.Reload(); break;
                case SettingsPage sp: sp.Reload(); break;
                case MissingPage mp: mp.Reload(); break;
                case VersionsPage vp: vp.Reload(); break;
                case DuplicatesPage dp: dp.Reload(); break;
                case StatsPage stp: stp.Reload(); break;
                case NowPlayingPage np: np.Reload(); break;
            }
        }

        internal void OnLibraryChanged()
        {
            Player.Relink();
            Sound.LibraryChanged();
            ReloadPage();
            Side.Invalidate(); Bar.Invalidate(); Head.Invalidate();
            if (!Lib.Scan.Running) TellInFull();
        }

        // a song you only had snippets of is here in full: say so once
        void TellInFull()
        {
            try
            {
                var news = Lib.NowInFull().Where(x => !x.Told).ToList();
                if (news.Count == 0) return;
                foreach (var n in news) Lib.ToldInFull(n.Full);
                var a = news[0];
                Toast(news.Count == 1 ? "“" + LyricsOnline.CleanTitle(a.Full.Name) + "” is here in full — you had " + Theme.Count(a.Snippets.Count, "snippet") + " of it (see Versions)"
                                      : Theme.Count(news.Count, "song") + " you had snippets of are here in full (see Versions)");
            }
            catch (Exception e) { App.Log("in full: " + e.Message); }
        }

        // a new theme: recolor the window and draw the page again
        public void ApplyTheme(string name)
        {
            Theme.Apply(name);
            var p = Theme.Palettes.First(x => x.Name == Theme.ThemeName);
            Prefs.ThemeName = name == "Match Windows" ? name : p.Name; Prefs.Accent = p.Accent;
            BackColor = Theme.Side; Side.BackColor = Theme.Side; Bar.BackColor = Theme.Bar; Head.BackColor = Theme.Bg;
            host.BackColor = Theme.Bg; if (host.Parent != null) host.Parent.BackColor = Theme.Bg;
            Head.Search.BackColor = Theme.Surface; Head.Search.ForeColor = Theme.Text;
            Win.DarkTitle(Handle);
            LookChanged();
            if (Current != null) { if (Page is Canvas c) Current.Scroll = c.Offset; ShowPage(Current); }
        }

        void LookChanged()
        {
            Prefs.Save();
            Theme.AccentBase = Theme.Parse(Prefs.Accent, Color.FromArgb(22, 136, 255));
            FollowCover();
            Page?.Invalidate(); Side.Invalidate(); Bar.Invalidate();
        }

        // ---------- now playing ----------
#if WPF
        Action closeStage;
#endif
        public void FullScreenLyrics()
        {
#if WPF
            if (closeStage != null) { var c = closeStage; closeStage = null; c(); return; }
            if (Aura.Wpf.WpfUi.Available)
            {
                closeStage = Aura.Wpf.WpfUi.OpenStage(this, this, Screen.FromControl(this), () => { closeStage = null; Activate(); });
                if (closeStage != null) return;
            }
#endif
            if (full != null && !full.IsDisposed) { full.Close(); return; }
            full = new FullLyricsForm(this, Screen.FromControl(this));
            full.FormClosed += (s, e) => { full = null; Activate(); };
            full.Show(this);
        }

        public void FindLyrics(Track t)
        {
            if (t == null) return;
            using (var f = new LyricsFinder(this, t)) f.ShowDialog(this);
        }

        // ---------- editing songs ----------
        bool writing;

        // writes tags in the background; the player lets go of a song while it's changed
        internal bool Writing => writing;
        internal void RunWrites(List<(Track t, TagEdit e)> jobs, string what, bool undo = true)
        {
            if (!undo) undoing = true;
            if (jobs.Count == 0) return;
            if (writing) { Toast("AURA is still saving the last change — try again in a moment"); return; }
            writing = true;
            // written down first, so Undo can put the old tags back
            var textJobs = jobs.Where(j => j.e.Title != null || j.e.Artist != null || j.e.Album != null || j.e.AlbumArtist != null || j.e.Year != null || j.e.Genre != null || j.e.Track != null).Select(j => j.t).ToList();
            if (textJobs.Count > 0 && !undoing) try { Lib.SaveUndo(what, textJobs); } catch (Exception x) { App.Log("undo snapshot: " + x.Message); }
            undoing = false;
            var held = Player.Release(jobs.Select(j => j.t.Id).ToList());
            var failed = new List<string>();
            var done = new List<Track>();
            var ctx = System.Threading.SynchronizationContext.Current;
            if (jobs.Count > 3) Toast("Saving " + Theme.Count(jobs.Count, "song") + "…");
            new System.Threading.Thread(() =>
            {
                int i = 0;
                foreach (var (t, e) in jobs)
                {
                    i++;
                    try { TagWriter.Write(t.Path, e); done.Add(t); }
                    catch (Exception x) { failed.Add(t.Name + ": " + x.Message); App.Log("couldn't save tags for " + t.Path + ": " + x); }
                    if (jobs.Count > 3 && i % 10 == 0) { int k = i; ctx.Post(_ => Toast("Saving… " + k + " of " + jobs.Count), null); }
                }
                ctx.Post(_ =>
                {
                    foreach (var t in done) { try { Lib.Refresh(t); } catch (Exception x) { App.Log("refresh " + t.Path + ": " + x.Message); } Art.Forget(t.Id); Lyrics.Forget(t.Id); }
                    Player.Restore(held);
                    writing = false;
                    OnLibraryChanged();
                    if (failed.Count == 0) Toast(what + (done.Count == 1 ? " for " + done[0].Name : " for " + Theme.Count(done.Count, "song")));
                    else
                    {
                        Toast(Theme.Count(failed.Count, "song") + " couldn't be changed — see the details");
                        MessageBox.Show(this, "These couldn't be changed (nothing was damaged):\n\n" + string.Join("\n", failed.Take(15)) + (failed.Count > 15 ? "\n…" : ""), "AURA", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }, null);
            }) { IsBackground = true, Name = "tags" }.Start();
        }

        public void EditInfo(IList<Track> tracks)
        {
            if (tracks == null || tracks.Count == 0) return;
            using (var f = new EditInfoForm(tracks.ToList(), PickCover))
                if (f.ShowDialog(this) == DialogResult.OK)
                {
                    if (f.EraResult != null) Lib.SetEra(tracks, f.EraResult);
                    if (f.Result.Count > 0) RunWrites(f.Result, "Saved"); else if (f.EraResult != null) { Toast("Saved"); OnLibraryChanged(); }
                }
        }

        byte[] PickCover(IList<Track> tracks)
        {
            var t = tracks[0];
            string q = (t.MainArtist == "Unknown artist" ? "" : t.MainArtist + " ") + (t.Album.Length > 0 ? t.Album : LyricsOnline.CleanTitle(t.Name));
            using (var f = new CoverFinderForm(q.Trim(), 0))
                return f.ShowDialog(this) == DialogResult.OK ? f.Result : null;
        }

        public void FindCover(IList<Track> tracks)
        {
            if (tracks == null || tracks.Count == 0) return;
            var t = tracks[0];
            var album = tracks.Count == 1 && t.Album.Length > 0 ? Lib.Tracks.Where(x => x.AlbumKey == t.AlbumKey).ToList() : null;
            string q = (t.MainArtist == "Unknown artist" ? "" : t.MainArtist + " ") + (t.Album.Length > 0 ? t.Album : LyricsOnline.CleanTitle(t.Name));
            using (var f = new CoverFinderForm(q.Trim(), album?.Count ?? 0))
            {
                if (f.ShowDialog(this) != DialogResult.OK || f.Result == null) return;
                var targets = f.WholeAlbum && album != null ? album : tracks.ToList();
                RunWrites(targets.Where(x => TagWriter.CanWrite(x.Path)).Select(x => (x, new TagEdit { Picture = f.Result })).ToList(), "Cover saved");
            }
        }

        public void FillFromNames(IList<Track> tracks)
        {
            var jobs = FillJobs(tracks, Prefs.Folders);
            if (jobs.Count == 0) { Toast("Nothing to fill in — these songs already have their title, artist and number, or the file names don't say"); return; }
            var sample = string.Join("\n", jobs.Take(6).Select(j => Path.GetFileName(j.Item1.Path) + "  →  " + string.Join(", ", new[] { j.Item2.Artist == null ? null : "artist: " + j.Item2.Artist, j.Item2.Title == null ? null : "title: " + j.Item2.Title, j.Item2.Track == null ? null : "#" + j.Item2.Track, j.Item2.Album == null ? null : "album: " + j.Item2.Album, j.Item2.Year == null ? null : "year: " + j.Item2.Year }.Where(x => x != null))));
            if (MessageBox.Show(this, "Fill in the blanks for " + Theme.Count(jobs.Count, "song") + " from their file and folder names?\n\n" + sample + (jobs.Count > 6 ? "\n…" : "") + "\n\nOnly empty boxes are filled.", "Fill in from file names", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            RunWrites(jobs, "Filled in");
        }

        internal static List<(Track, TagEdit)> FillJobs(IList<Track> tracks, IList<string> roots = null)
        {
            var jobs = new List<(Track, TagEdit)>();
            string Norm(string p) => (p ?? "").TrimEnd('\\', '/').ToLowerInvariant();
            var rootSet = new HashSet<string>((roots ?? new List<string>()).Select(Norm));
            foreach (var t in tracks.Where(x => TagWriter.CanWrite(x.Path)))
            {
                var (artist, title, track) = Editing.FromName(t.Path);
                var e = new TagEdit();
                if (t.Title.Trim().Length == 0 && title.Length > 0) e.Title = title;
                if (t.Artist.Trim().Length == 0 && artist.Length > 0) e.Artist = artist;
                if (t.TrackNo == 0 && track > 0) e.Track = track.ToString();
                // the folders: Music/Artist/Album/song or Music/Artist/song (only below one of your music folders)
                var dir = Path.GetDirectoryName(t.Path) ?? "";
                var up = Path.GetDirectoryName(dir) ?? "";
                if (rootSet.Count > 0 && !rootSet.Contains(Norm(dir)))
                {
                    bool twoDeep = !rootSet.Contains(Norm(up)) && rootSet.Contains(Norm(Path.GetDirectoryName(up)));
                    string dirName = Path.GetFileName(dir), upName = Path.GetFileName(up);
                    string knownArtist = t.Artist.Trim().Length > 0 ? t.Artist : e.Artist ?? "";
                    if (twoDeep)
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(dirName, @"^(?:(\d{4})\s*[-–]\s*)?(.+?)(?:\s*[\(\[](\d{4})[\)\]])?$");
                        string album = m.Success ? m.Groups[2].Value.Trim() : dirName, year = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[3].Success ? m.Groups[3].Value : "";
                        if (t.Album.Trim().Length == 0 && album.Length > 0) e.Album = album;
                        if (t.Year.Trim().Length == 0 && year.Length > 0) e.Year = year;
                        if (knownArtist.Length == 0 && upName.Length > 0) e.Artist = upName;
                    }
                    else if (rootSet.Contains(Norm(up)) && knownArtist.Length == 0 && dirName.Length > 0) e.Artist = dirName;
                }
                if (e.Any) jobs.Add((t, e));
            }
            return jobs;
        }

        public void RenameFiles(IList<Track> tracks)
        {
            if (tracks == null || tracks.Count == 0) return;
            using (var f = new RenameForm(tracks.ToList(), Prefs.RenamePattern))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                Prefs.RenamePattern = f.Pattern; Prefs.Save();
                var held = Player.Release(tracks.Select(t => t.Id).ToList());
                int done = 0; var failed = new List<string>();
                foreach (var t in tracks)
                {
                    var name = Editing.NameFor(t, f.Pattern);
                    if (name == null || name == Path.GetFileName(t.Path)) continue;
                    var to = Path.Combine(Path.GetDirectoryName(t.Path) ?? "", name);
                    if (FileX.Exists(to) && !string.Equals(to, t.Path, StringComparison.OrdinalIgnoreCase)) { failed.Add(Path.GetFileName(t.Path) + ": " + name + " already exists"); continue; }
                    try { Lib.Rename(t, to); done++; }
                    catch (Exception x) { failed.Add(Path.GetFileName(t.Path) + ": " + x.Message); App.Log("rename " + t.Path + ": " + x); }
                }
                Player.Restore(held);
                OnLibraryChanged();
                Toast(failed.Count == 0 ? "Renamed " + Theme.Count(done, "file") : "Renamed " + done + ", " + failed.Count + " couldn't be");
                if (failed.Count > 0) MessageBox.Show(this, string.Join("\n", failed.Take(15)), "Some files weren't renamed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // renames "song.mp3" that is really an M4A to "song.m4a", so every player opens it
        internal void FixTypes(List<(Track t, string k)> wrong)
        {
            var held = Player.Release(wrong.Select(x => x.t.Id).ToList());
            int done = 0; var failed = new List<string>();
            foreach (var (t, k) in wrong)
            {
                var to = Path.ChangeExtension(t.Path, k == ".aiff" ? ".aiff" : k);
                if (FileX.Exists(to)) { failed.Add(Path.GetFileName(t.Path) + ": " + Path.GetFileName(to) + " already exists"); continue; }
                try { Lib.Rename(t, to); done++; }
                catch (Exception x) { failed.Add(Path.GetFileName(t.Path) + ": " + x.Message); }
            }
            Player.Restore(held);
            OnLibraryChanged();
            Toast(failed.Count == 0 ? "Fixed " + Theme.Count(done, "file") : "Fixed " + done + ", " + failed.Count + " couldn't be: " + failed[0]);
        }

        public void InstallUpdate()
        {
            if (!Updater.Available) return;
            if (MessageBox.Show(this, "Install AURA Native " + Updater.Latest + " now? AURA will close for a moment and open again by itself.", "Update AURA", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
            Updater.Install(file =>
            {
                try { SaveState(); Updater.RunInstaller(file); Close(); }
                catch (Exception e) { Toast("Couldn't start the update: " + e.Message); }
            }, err => Toast("The update didn't download: " + err));
        }

        void OnTrackChanged()
        {
            var t = Player.Current;
            Text = t == null ? App.Name : t.Name + " · " + t.ArtistName;
            FollowCover();
            Bar.Invalidate();
            Page?.Invalidate();
        }

        void FollowCover()
        {
            var t = Player.Current;
            if (!Prefs.CoverColors || t == null) { Theme.SetAccent(Theme.AccentBase); return; }
            var img = Art.Get(t);
            if (img != null) Theme.SetAccent(Art.Accent(img));
            else if (Art.Missing(t)) Theme.SetAccent(Theme.AccentBase);
        }

        // ---------- messages ----------
        public void Toast(string msg) { toast.Show(msg); PlaceToast(); toast.BringToFront(); }

        void PlaceToast()
        {
            if (toast == null || !toast.Visible) return;
            var size = toast.Want(Width);
            toast.Bounds = new Rectangle((ClientSize.Width - size.Width) / 2 + Side.Width / 2, ClientSize.Height - Bar.Height - size.Height - Theme.P(18), size.Width, size.Height);
        }

        // ---------- songs menu ----------
        public void TrackMenu(IList<Track> tracks, Point screen, TrackContext ctx)
        {
            if (tracks == null || tracks.Count == 0) return;
            var ids = tracks.Select(t => t.Id).ToList();
            var one = tracks.Count == 1 ? tracks[0] : null;
            var m = Menus.New();
            string what = one != null ? "" : " " + tracks.Count + " songs";
            m.Items.Add(Menus.Item("Play" + what, () => Player.PlayList(ids)));
            m.Items.Add(Menus.Item("Play next", () => { Player.PlayNext(ids); Toast(one != null ? "Playing next: " + one.Name : "Playing " + tracks.Count + " songs next"); }));
            m.Items.Add(Menus.Item("Add to up next", () => { Player.AddToQueue(ids); Toast(one != null ? "Added to up next: " + one.Name : "Added " + tracks.Count + " songs to up next"); }));
            m.Items.Add(new ToolStripSeparator());
            var add = Menus.Item("Add to playlist", null);
            Menus.Sub(add);
            add.DropDownItems.Add(Menus.Item("New playlist…", () => NewPlaylist(ids)));
            if (Lib.Playlists.Count > 0) add.DropDownItems.Add(new ToolStripSeparator());
            foreach (var p in Lib.Playlists)
            {
                var pl = p;
                add.DropDownItems.Add(Menus.Item(p.Name, () =>
                {
                    int n = Lib.AddToPlaylist(pl, ids);
                    Toast(n == 0 ? "Already in " + pl.Name : "Added " + (n == 1 ? (one?.Name ?? "1 song") : n + " songs") + " to " + pl.Name);
                }));
            }
            m.Items.Add(add);
            bool allFav = tracks.All(t => t.Fav);
            m.Items.Add(Menus.Item(allFav ? "Remove from Favorites" : "Add to Favorites", () => { foreach (var t in tracks) Lib.SetFav(t, !allFav); }));
            m.Items.Add(RateMenu(tracks));
            if (ctx?.Playlist != null && ctx.Removed != null)
                m.Items.Add(Menus.Item("Remove from this playlist", ctx.Removed));
            if (ctx != null && ctx.Queue && ctx.QueueIndexes != null)
            {
                var qi = ctx.QueueIndexes.OrderByDescending(k => k).ToList();
                m.Items.Add(Menus.Item("Remove from up next", () => { foreach (var k in qi) Player.RemoveUpNext(k); }));
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Menus.Item(one != null ? "Edit info…" : "Edit info for " + tracks.Count + " songs…", () => EditInfo(tracks)));
            m.Items.Add(Menus.Item("Find a cover…", () => FindCover(tracks)));
            var more = Menus.Item("More", null);
            Menus.Sub(more);
            more.DropDownItems.Add(Menus.Item("Fill in blanks from file names", () => FillFromNames(tracks)));
            more.DropDownItems.Add(Menus.Item("Set era…", () => AskEra(tracks)));
            more.DropDownItems.Add(Menus.Item("Fill in from a fan tracker…", () => ImportTracker(tracks.ToList())));
            if (tracks.Count > 1) more.DropDownItems.Add(Menus.Item("Group as versions…", () => GroupAsVersions(tracks)));
            more.DropDownItems.Add(Menus.Item("Rename files…", () => RenameFiles(tracks)));
            var wrong = tracks.Take(200).Select(t => (t, k: AudioKind.Detect(t.Path))).Where(x => x.k != null && !AudioKind.Matches(x.t.Path, x.k)).ToList();
            if (wrong.Count > 0)
                more.DropDownItems.Add(Menus.Item(wrong.Count == 1 ? "Fix file type (it's really " + AudioKind.Name(wrong[0].k) + ")" : "Fix file type for " + wrong.Count + " songs", () => FixTypes(wrong)));
            if (one != null) more.DropDownItems.Add(Menus.Item("Find lyrics…", () => FindLyrics(one)));
            var convertible = tracks.Where(Converter.CanConvert).ToList();
            if (convertible.Count > 0) more.DropDownItems.Add(Menus.Item(convertible.Count == 1 ? "Convert to MP3…" : "Convert " + convertible.Count + " songs to MP3…", () => ConvertToMp3(convertible)));
            if (one != null)
            {
                more.DropDownItems.Add(new ToolStripSeparator());
                more.DropDownItems.Add(Menus.Item("Write lyrics in time (auto-sync / transcribe)…", () => Transcribe(one)));
                more.DropDownItems.Add(Menus.Item("Time the lyrics by hand…", () => SyncByHand(one)));
                more.DropDownItems.Add(Menus.Item("Edit lyrics…", () => EditLyrics(one)));
            }
            m.Items.Add(more);
            if (one != null)
            {
                var versions = Lib.VersionsOf(one);
                if (versions.Count > 1) m.Items.Add(Menus.Item("Versions (" + versions.Count + ")", () => Go(new Route("version", Library.VersionKey(one)))));
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add(Menus.Item("Go to artist", () => Go(new Route("artist", one.MainArtist.ToLowerInvariant()))));
                if (one.Album.Length > 0) m.Items.Add(Menus.Item("Go to album", () => Go(new Route("album", one.AlbumKey))));
                m.Items.Add(Menus.Item("Show in folder", () =>
                {
                    try { Process.Start("explorer.exe", "/select,\"" + one.Path + "\""); } catch (Exception e) { Toast("Couldn't open the folder: " + e.Message); }
                }, FileX.Exists(one.Path)));
            }
            m.Show(screen);
        }

        public void NewPlaylist(IEnumerable<long> ids = null)
        {
            var list = ids?.ToList();
            string suggestion = "Playlist " + (Lib.Playlists.Count + 1);
            var name = Ask.ForText(this, "New playlist", list != null ? "Name for the new playlist (" + Theme.Count(list.Count, "song") + ")" : "Name for the new playlist", suggestion, "Create");
            if (name == null) return;
            var p = Lib.CreatePlaylist(name, list);
            if (list == null) Go(new Route("playlist", "", p.Id));
            else Toast("Made " + p.Name + " with " + Theme.Count(list.Count, "song"));
        }

        // ---------- folders and scanning ----------
        public void AddFolder()
        {
            using (var d = new FolderBrowserDialog { Description = "Choose the folder where your music is", ShowNewFolderButton = false })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                var f = d.SelectedPath;
                if (Prefs.Folders.Any(x => string.Equals(x.TrimEnd('\\'), f.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))) { Toast("That folder is already in your library"); return; }
                Prefs.Folders.Add(f);
                Prefs.Save();
                Rescan();
                Toast("Adding the songs in " + Path.GetFileName(f.TrimEnd('\\')) + "…");
            }
        }

        public void Rescan()
        {
            if (Lib.Scan.Running) { rescanAfter = true; return; }
            Lib.ScanAsync(Prefs.Folders, () =>
            {
                var s = Lib.Scan;
                if (s.Added > 0 && !firstScan) Toast(s.Added == 1 ? "Found 1 new song" : "Found " + s.Added.ToString("N0") + " new songs");
                if (firstScan) { firstScan = false; AfterFirstScan(); }
                if (rescanAfter) { rescanAfter = false; rescanTimer.Start(); }
                Lyrics.Nudge();
            });
            Watch();
            Side.Invalidate(); Head.Invalidate(); Page?.Invalidate();
        }
        bool firstScan = true;

        void Watch()
        {
            foreach (var w in watchers) w.Dispose();
            watchers.Clear();
            foreach (var f in Prefs.Folders)
            {
                if (!Directory.Exists(f)) continue;
                try
                {
                    var w = new FileSystemWatcher(f) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size };
                    FileSystemEventHandler h = (s, e) =>
                    {
                        if (Directory.Exists(e.FullPath) || Library.IsAudio(e.FullPath) || e.ChangeType == WatcherChangeTypes.Deleted)
                            BeginInvoke(new Action(() => { rescanTimer.Stop(); rescanTimer.Start(); }));
                    };
                    w.Created += h; w.Deleted += h; w.Changed += h;
                    w.Renamed += (s, e) => BeginInvoke(new Action(() => { rescanTimer.Stop(); rescanTimer.Start(); }));
                    w.EnableRaisingEvents = true;
                    watchers.Add(w);
                }
                catch (Exception e) { App.Log("watch " + f + ": " + e.Message); }
            }
        }

        // ---------- first start ----------
        public void Start()
        {
            Lib.SetUiContext(System.Threading.SynchronizationContext.Current);
            Player.RestoreFrom(Prefs);
            OnTrackChanged();
            if (Prefs.Folders.Count > 0) Rescan();
            else { firstScan = false; AfterFirstScan(); }
            Lyrics.StartAuto(() => Prefs.AutoLyrics);
            StartMore();
            if (Prefs.CheckUpdates && Environment.GetEnvironmentVariable("AURA_NO_UPDATE_CHECK") == null)
            {
                var once = new Timer { Interval = 6000 };
                once.Tick += (s, e) => { once.Stop(); once.Dispose(); Updater.Check(false); };
                once.Start();
            }
        }

        void AfterFirstScan()
        {
            if (!Prefs.Imported) ImportOld(false);
        }

        void ImportOld(bool again)
        {
            var oldDb = Path.Combine(App.OldDir, "library.db");
            Prefs.Imported = true;
            Prefs.Save();
            if (!File.Exists(oldDb)) { if (again) Toast("The first AURA's library wasn't found on this PC"); return; }
            if (again)
            {
                foreach (var f in OldConfig.ReadFolders(Path.Combine(App.OldDir, "config.json")))
                    if (!Prefs.Folders.Any(x => string.Equals(x, f, StringComparison.OrdinalIgnoreCase))) Prefs.Folders.Add(f);
            }
            try
            {
                var r = Lib.ImportOld(oldDb);
                OnLibraryChanged();
                if (r.playlists + r.favorites + r.plays > 0)
                    Toast("Brought over from AURA: " + Theme.Count(r.playlists, "playlist") + ", " + Theme.Count(r.favorites, "favorite") + ", play counts for " + Theme.Count(r.plays, "song"));
                else if (again) Toast("Nothing new to bring over");
            }
            catch (Exception e)
            {
                App.Log("import: " + e);
                Toast("Couldn't read the first AURA's library: " + e.Message);
            }
        }
    }

    // the little message that pops up above the player bar
    sealed class ToastView : Control
    {
        string msg = "";
        readonly Timer hide = new Timer { Interval = 2800 };
        public ToastView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Theme.Bg;
            hide.Tick += (s, e) => { hide.Stop(); Visible = false; };
        }
        public void Show(string m)
        {
            msg = m ?? "";
            Visible = true;
            hide.Stop(); hide.Start();
            Invalidate();
        }
        public Size Want(int max)
        {
            using (var g = CreateGraphics())
            {
                float w = Theme.Measure(g, msg, Theme.Font(13.5f, FontStyle.Regular, true)) + Theme.F(56);
                return new Size((int)Math.Min(max - Theme.F(300), w), Theme.P(42));
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Setup(g);
            g.Clear(Parent?.BackColor ?? Theme.Bg);
            var r = new RectangleF(0, 0, Width - 1, Height - 1);
            Theme.Fill(g, Color.FromArgb(240, 245, 250), r, r.Height / 2);
            Theme.Write(g, msg, Theme.Font(13.5f, FontStyle.Regular, true), Color.FromArgb(12, 16, 24), new RectangleF(Theme.F(22), 0, Width - Theme.F(44), Height));
        }
        protected override void OnClick(EventArgs e) { Visible = false; }
        protected override void Dispose(bool disposing) { if (disposing) hide.Dispose(); base.Dispose(disposing); }
    }
}
