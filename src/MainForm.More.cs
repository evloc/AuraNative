using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace Aura
{
    // Listening to songs, converting, duplicates, the mini player, and Windows' taskbar and media controls.
    sealed partial class MainForm
    {
        public SoundStore Sound { get; private set; }
        MiniForm mini;
        Taskbar taskbar;
        Smtc smtc;
        bool busy;          // converting / cleaning up

        void InitMore()
        {
            InitListen();
            if (Prefs.AutoSyncAll) StartAutoSync();
            if (Prefs.AutoYears && !UiTestMode) StartYears();
            Sound = new SoundStore(Lib) { Background = Prefs.Listen, NeedLoudness = Prefs.Leveling };
            Sound.Ready += id =>
            {
                if (Page is NowPlayingPage np) np.SoundReady(id);
                if (Page is SettingsPage sp) sp.Invalidate();
                if (Page is DuplicatesPage dp) dp.Invalidate();
            };
            Player.MapPosition = (a, b, pos) =>
            {
                var sa = Sound.Get(a); var sb = Sound.Get(b);
                return sa == null || sb == null ? -1 : sa.MapTo(sb, pos);
            };
            Player.TrackChanged += () =>
            {
                var t = Player.Current;
                if (t != null)
                {
                    Sound.Want(t);
                    foreach (var v in Lib.VersionsOf(t)) if (v != t) Sound.Want(v);       // ready for switching versions in step
                }
                mini?.Changed();
                UpdateMediaControls();
            };
            Player.StateChanged += () =>
            {
                mini?.Changed();
                taskbar?.SetPlaying(Player.IsPlaying);
                smtc?.SetState(Player.IsPlaying, Player.Current != null);
            };
            Player.LoopChanged += () => { Page?.Invalidate(); mini?.Changed(); };
            Art.Ready += id => { if (id == Player.Current?.Id) mini?.Changed(); };
        }

        void StartMore()
        {
            Sound.SetUiContext(SynchronizationContext.Current);
            Sound.Kick();
            if (App.IsWindows)
            {
                smtc = Smtc.TryCreate(Handle, b => BeginInvoke(new Action(() =>
                {
                    switch (b)
                    {
                        case Smtc.Play: if (!Player.IsPlaying) Player.Toggle(); break;
                        case Smtc.Pause: case Smtc.Stop: if (Player.IsPlaying) Player.Toggle(); break;
                        case Smtc.Next: Player.Next(); break;
                        case Smtc.Previous: Player.Prev(); break;
                    }
                })));
                if (smtc != null) { App.Log("Windows media controls: on"); UpdateMediaControls(); smtc.SetState(Player.IsPlaying, Player.Current != null); }
            }
        }

        void CloseMore()
        {
            Sound?.Stop();
            try { smtc?.Dispose(); } catch { }
            taskbar?.Dispose();
            if (mini != null) { mini.Close(); mini = null; }
        }

        // ---------- Windows media controls (volume-key pop-up, lock screen) ----------
        long mediaFor = -1;
        void UpdateMediaControls()
        {
            if (smtc == null) return;
            var t = Player.Current;
            if (t == null) { smtc.SetState(false, false); return; }
            if (mediaFor == t.Id) return;
            mediaFor = t.Id;
            var path = t.Path; var title = t.Name; var artist = t.ArtistName; var album = t.Album;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                byte[] cover = null;
                try { cover = Tags.Read(path, true).Picture; } catch { }
                if (cover != null && cover.Length > 3_000_000) cover = null;
                try { BeginInvoke(new Action(() => { if (Player.Current?.Path == path) smtc?.SetSong(title, artist, album, cover); })); } catch { }
            });
        }

        // ---------- taskbar buttons ----------
        bool TaskbarMessage(ref Message m)
        {
            if (Taskbar.CreatedMessage > 0 && m.Msg == Taskbar.CreatedMessage)
            {
                if (taskbar == null) taskbar = Taskbar.TryCreate(Handle);
                taskbar?.Created(Player.IsPlaying);
                return false;
            }
            if (m.Msg == 0x0111 && ((m.WParam.ToInt64() >> 16) & 0xFFFF) == 0x1800)     // WM_COMMAND from a thumbnail button
            {
                switch ((int)(m.WParam.ToInt64() & 0xFFFF))
                {
                    case Taskbar.Prev: Player.Prev(); return true;
                    case Taskbar.PlayPause: Player.Toggle(); return true;
                    case Taskbar.Next: Player.Next(); return true;
                }
            }
            return false;
        }

        // ---------- mini player ----------
        public void ToggleMini()
        {
            if (mini != null && !mini.IsDisposed)
            {
                mini.Close(); mini = null;
                if (WindowState == FormWindowState.Minimized) WindowState = Prefs.Maximized ? FormWindowState.Maximized : FormWindowState.Normal;
                Activate();
                Prefs.Save();
                return;
            }
            mini = new MiniForm(this) { Owner = null };
            mini.FormClosed += (s, e) => { if (s == mini) mini = null; Prefs.Save(); };
            mini.Show();
            Toast("Mini player is on — it stays on top. Close it with its × or Ctrl+M");
        }

        internal bool MiniOpen => mini != null && !mini.IsDisposed;

        // ---------- converting ----------
        public void ConvertToMp3(IList<Track> tracks)
        {
            var list = tracks.Where(Converter.CanConvert).ToList();
            if (list.Count == 0) { Toast("These are MP3s already"); return; }
            using (var f = new ConvertForm(list, Prefs.Mp3Kbps))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                Prefs.Mp3Kbps = f.Kbps; Prefs.Save();
                RunConvert(list, f.Kbps);
            }
        }

        internal void RunConvert(List<Track> list, int kbps, Action done = null)
        {
            if (busy || writing) { Toast("AURA is still busy with the last change — try again in a moment"); return; }
            busy = true;
            var ctx = SynchronizationContext.Current;
            var failed = new List<string>();
            int ok = 0;
            Toast(list.Count == 1 ? "Converting " + list[0].Name + " to MP3…" : "Converting " + Theme.Count(list.Count, "song") + " to MP3…");
            new Thread(() =>
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var t = list[i];
                    int k = i;
                    try
                    {
                        var tmp = Converter.Encode(t, kbps, p => ctx.Post(_ => taskbar?.Progress((k + p) / list.Count), null), null);
                        // the swap happens on the window's side, where the player can let go of the file first
                        Exception err = null;
                        using (var wait = new ManualResetEventSlim())
                        {
                            ctx.Post(_ =>
                            {
                                var held = Player.Release(new[] { t.Id });
                                try { Converter.Swap(Lib, t, tmp); Art.Forget(t.Id); Lyrics.Forget(t.Id); Sound.Forget(t.Id); }
                                catch (Exception x) { err = x; try { File.Delete(FileX.Long(tmp)); } catch { } }
                                Player.Restore(held);
                                wait.Set();
                            }, null);
                            wait.Wait();
                        }
                        if (err != null) throw err;
                        ok++;
                        if (list.Count > 1) ctx.Post(_ => Toast("Converted " + (k + 1) + " of " + list.Count), null);
                    }
                    catch (Exception x) { failed.Add(t.Name + ": " + x.Message); App.Log("convert " + t.Path + ": " + x); }
                }
                ctx.Post(_ =>
                {
                    busy = false;
                    taskbar?.Progress(-1);
                    OnLibraryChanged();
                    if (failed.Count == 0) Toast(ok == 1 ? "Converted to MP3 — the original is in the _originals folder" : "Converted " + Theme.Count(ok, "song") + " to MP3 — the originals are in _originals folders");
                    else
                    {
                        Toast(Theme.Count(failed.Count, "song") + " couldn't be converted — see the details");
                        if (!UiTestMode) MessageBox.Show(this, "These couldn't be converted (the originals weren't touched):\n\n" + string.Join("\n", failed.Take(15)), "AURA", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    done?.Invoke();
                }, null);
            }) { IsBackground = true, Name = "convert" }.Start();
        }

        internal static bool UiTestMode;
        internal bool Busy => busy;
        internal MiniForm Mini => mini;

        public void Transcribe(Track t)
        {
            if (t == null) return;
            using (var f = new TranscribeForm(this, t)) f.ShowDialog(this);
        }

        // ---------- duplicates ----------
        public void MergeDuplicates(IList<(DupGroup g, Track keep)> groups)
        {
            if (busy || writing) { Toast("AURA is still busy with the last change — try again in a moment"); return; }
            int moved = 0; var failed = new List<string>();
            var removing = groups.SelectMany(x => x.g.Tracks.Where(t => t.Id != x.keep.Id)).Select(t => t.Id).ToList();
            var current = Player.Current;
            var held = Player.Release(removing);
            foreach (var (g, keep) in groups)
            {
                try
                {
                    foreach (var t in g.Tracks) if (t.Id != keep.Id) Player.Replace(t.Id, keep.Id);
                    moved += Duplicates.Merge(Lib, Sound, Lyrics, g, keep);
                }
                catch (Exception e) { failed.Add(e.Message); }
            }
            Lib.Load();
            if (held != null)
            {
                // the song that was playing was moved away: carry on with the copy that's kept
                var keep = groups.FirstOrDefault(x => x.g.Tracks.Any(t => t.Id == held.Track.Id)).keep;
                if (keep != null && Lib.Get(keep.Id) != null) { Player.SwitchTo(Lib.Get(keep.Id)); if (!held.Playing && Player.IsPlaying) Player.Toggle(); }
                else Player.Restore(held);
            }
            OnLibraryChanged();
            Toast(failed.Count == 0 ? "Moved " + Theme.Count(moved, "extra copy", "extra copies") + " to _duplicates folders" : "Moved " + moved + "; " + failed.Count + " couldn't be: " + failed[0]);
        }
    }

    // choose how good the MP3s should be
    sealed class ConvertForm : DarkForm
    {
        readonly ComboBox rate;
        public int Kbps => Converter.Rates[Math.Max(0, rate.SelectedIndex)];

        public ConvertForm(List<Track> list, int kbps) : base("Convert to MP3", 470, 250)
        {
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
            var title = Lbl(list.Count == 1 ? "Convert " + list[0].Name + " to MP3" : "Convert " + Theme.Count(list.Count, "song") + " to MP3", Theme.Text, 15);
            title.SetBounds(Theme.P(22), Theme.P(16), Theme.P(426), Theme.P(30));
            var note = Lbl("Uses Windows' own MP3 encoder. The title, artist, cover and lyrics come along, and the song keeps its plays, playlists and favorite. The original file is moved into an \"_originals\" folder next to it, so nothing is lost.");
            note.SetBounds(Theme.P(22), Theme.P(50), Theme.P(426), Theme.P(80));
            note.TextAlign = ContentAlignment.TopLeft;
            var q = Lbl("Quality");
            q.SetBounds(Theme.P(22), Theme.P(140), Theme.P(90), Theme.P(30));
            rate = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Theme.Surface2, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat, Font = Theme.Font(13.5f) };
            foreach (var r in Converter.Rates) rate.Items.Add(r + " kbps" + (r == 320 ? " (best)" : r == 192 ? " (smaller files)" : r == 128 ? " (smallest)" : ""));
            rate.SelectedIndex = Math.Max(0, Array.IndexOf(Converter.Rates, kbps));
            rate.SetBounds(Theme.P(112), Theme.P(140), Theme.P(200), Theme.P(30));
            var ok = Btn("Convert", true); ok.DialogResult = DialogResult.OK; ok.SetBounds(Theme.P(338), Theme.P(196), Theme.P(110), Theme.P(36));
            var cancel = Btn("Cancel", false); cancel.DialogResult = DialogResult.Cancel; cancel.SetBounds(Theme.P(218), Theme.P(196), Theme.P(110), Theme.P(36));
            Controls.AddRange(new Control[] { title, note, q, rate, ok, cancel });
            AcceptButton = ok; CancelButton = cancel;
        }
    }
}
