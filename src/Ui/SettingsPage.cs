using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;

namespace Aura
{
    sealed class SettingsPage : Canvas
    {
        readonly IShell shell;
        public Action ImportAgain;
        public Action Changed;      // a look setting changed: redraw everything
        public Action<string> ThemeChanged;
        static readonly string[] Swatches = { "#1688FF", "#7C5CFF", "#F72BD6", "#FF3B6B", "#FF6B35", "#FFB020", "#22C55E", "#14B8A6", "#06B6D4" };

        public SettingsPage(IShell shell) { this.shell = shell; }
        public void Reload() => Invalidate();

        float Card(Graphics g, float y, float h, string title, string sub, float subH = 40)
        {
            float pad = Theme.F(28);
            var r = new RectangleF(pad, y, Math.Min(Width - pad * 2, Theme.F(820)), h);
            Theme.Fill(g, Theme.Surface, r, Theme.F(14));
            Theme.Stroke(g, Theme.Alpha(Color.White, 14), r, Theme.F(14));
            Theme.Write(g, title, Theme.Font(17, FontStyle.Bold), Theme.Text, new RectangleF(r.X + Theme.F(22), r.Y + Theme.F(18), r.Width - Theme.F(44), Theme.F(26)));
            if (sub != null) Theme.Write(g, sub, Theme.Font(13), Theme.Sub, new RectangleF(r.X + Theme.F(22), r.Y + Theme.F(46), r.Width - Theme.F(44), Theme.F(subH)), Theme.Wrap);
            return r.X + Theme.F(22);
        }

        void Toggle(Graphics g, string id, float x, float y, float w, string label, bool on, Action flip)
        {
            var sw = new RectangleF(x, y + Theme.F(4), Theme.F(42), Theme.F(24));
            Theme.Fill(g, on ? Theme.Accent : Theme.Surface2, sw, sw.Height / 2);
            float d = Theme.F(18);
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, on ? sw.Right - d - Theme.F(3) : sw.X + Theme.F(3), sw.Y + Theme.F(3), d, d);
            Theme.Write(g, label, Theme.Font(14), Hover(id) ? Theme.Text : Theme.Sub, new RectangleF(sw.Right + Theme.F(14), y, w - Theme.F(60), Theme.F(32)));
            AddHit(id, new RectangleF(x, y, w, Theme.F(32)), flip);
        }

        protected override void Draw(Graphics g, RectangleF view)
        {
            float pad = Theme.F(28), y = Theme.F(8);
            Theme.Write(g, "Settings", Theme.Font(34, FontStyle.Bold), Theme.Text, new RectangleF(pad, y + Theme.F(24), Width - pad * 2, Theme.F(48)));
            y += Theme.F(100);
            float cw = Math.Min(Width - pad * 2, Theme.F(820));

            // ---- music folders
            var folders = shell.Prefs.Folders;
            float h = Theme.F(150) + folders.Count * Theme.F(44);
            float x = Card(g, y, h, "Music folders", "AURA plays your songs from these folders and notices new ones by itself. Nothing is moved or copied.");
            float fy = y + Theme.F(92);
            foreach (var f in folders.ToList())
            {
                var r = new RectangleF(x, fy, cw - Theme.F(44), Theme.F(38));
                Theme.Fill(g, Theme.Surface2, r, Theme.F(9));
                float s = Theme.F(18);
                Icons.Draw(g, "folder", new RectangleF(r.X + Theme.F(12), r.Y + (r.Height - s) / 2, s, s), Directory.Exists(f) ? Theme.Sub : Color.FromArgb(255, 120, 120));
                Theme.Write(g, f + (Directory.Exists(f) ? "" : "   (not connected)"), Theme.Font(13.5f), Theme.Text, new RectangleF(r.X + Theme.F(42), r.Y, r.Width - Theme.F(90), r.Height));
                var ff = f;
                IconButton(g, "rm:" + f, new RectangleF(r.Right - Theme.F(36), r.Y + Theme.F(4), Theme.F(30), Theme.F(30)), "close", () =>
                {
                    shell.Prefs.Folders.Remove(ff); shell.Prefs.Save(); shell.Rescan(); Invalidate();
                }, "Stop using this folder");
                fy += Theme.F(44);
            }
            float bx = Pill(g, "add", x, fy + Theme.F(4), "Add folder", shell.AddFolder, true, "folder").Right + Theme.F(10);
            Pill(g, "rescan", bx, fy + Theme.F(4), shell.Lib.Scan.Running ? "Scanning…" : "Scan again", shell.Rescan, false, "refresh");
            y += h + Theme.F(20);

            // ---- look
            h = Theme.F(330);
            x = Card(g, y, h, "Look", null);
            float ly = y + Theme.F(58);
            // themes as little previews
            float tx = x, tsz = Math.Min(Theme.F(132), (cw - Theme.F(44) - Theme.F(12) * 4) / 5);
            foreach (var p in Theme.Palettes)
            {
                var r = new RectangleF(tx, ly, tsz, Theme.F(76));
                bool on = Theme.ThemeName == p.Name, hot = Hover("th:" + p.Name);
                Theme.Fill(g, p.Bg, r, Theme.F(10));
                Theme.Fill(g, p.Side, new RectangleF(r.X, r.Y, r.Width * 0.28f, r.Height), Theme.F(10));
                Theme.Fill(g, p.Surface2, new RectangleF(r.X + r.Width * 0.36f, r.Y + Theme.F(14), r.Width * 0.5f, Theme.F(8)), Theme.F(4));
                Theme.Fill(g, p.Surface, new RectangleF(r.X + r.Width * 0.36f, r.Y + Theme.F(28), r.Width * 0.36f, Theme.F(8)), Theme.F(4));
                using (var b = new SolidBrush(Theme.Parse(p.Accent, Theme.AccentBase))) g.FillEllipse(b, r.X + r.Width * 0.36f, r.Y + Theme.F(46), Theme.F(18), Theme.F(18));
                Theme.Stroke(g, on ? Theme.Text : hot ? Theme.Sub : Theme.Alpha(Color.White, 30), r, Theme.F(10), on ? Theme.F(2) : 1);
                Theme.Write(g, p.Name, Theme.Font(13, FontStyle.Regular, on), on ? Theme.Text : Theme.Sub, new RectangleF(r.X, r.Bottom + Theme.F(4), r.Width, Theme.F(22)), Theme.Center);
                var name = p.Name;
                AddHit("th:" + p.Name, new RectangleF(r.X, r.Y, r.Width, r.Height + Theme.F(26)), () => ThemeChanged?.Invoke(name));
                tx += tsz + Theme.F(12);
            }
            ly += Theme.F(118);
            Toggle(g, "cc", x, ly, cw - Theme.F(44), "Colors follow the song — AURA takes its color from the cover that's playing", shell.Prefs.CoverColors, () => { shell.Prefs.CoverColors = !shell.Prefs.CoverColors; Changed?.Invoke(); });
            ly += Theme.F(42);
            Toggle(g, "cmp", x, ly, cw - Theme.F(44), "Compact song list — smaller rows, more songs on screen", shell.Prefs.Compact, () => { shell.Prefs.Compact = !shell.Prefs.Compact; Changed?.Invoke(); });
            ly += Theme.F(50);
            Theme.Write(g, "Accent color", Theme.Font(14), Theme.Sub, new RectangleF(x, ly, Theme.F(140), Theme.F(30)));
            float sx = x + Theme.F(140);
            foreach (var hex in Swatches)
            {
                var c = Theme.Parse(hex, Theme.AccentBase);
                var r = new RectangleF(sx, ly + Theme.F(2), Theme.F(26), Theme.F(26));
                bool on = string.Equals(shell.Prefs.Accent, hex, StringComparison.OrdinalIgnoreCase);
                if (on || Hover("sw:" + hex)) using (var pen = new Pen(Theme.Text, Theme.F(2))) g.DrawEllipse(pen, RectangleF.Inflate(r, Theme.F(3), Theme.F(3)));
                using (var b = new SolidBrush(c)) g.FillEllipse(b, r);
                var hh = hex;
                AddHit("sw:" + hex, r, () => { shell.Prefs.Accent = hh; Changed?.Invoke(); }, shell.Prefs.CoverColors ? "Used when a song has no cover" : null);
                sx += Theme.F(36);
            }
            Theme.Write(g, shell.Prefs.CoverColors ? "Used when a song has no cover." : "", Theme.Font(12.5f), Theme.Faint, new RectangleF(x, ly + Theme.F(34), cw, Theme.F(22)));
            y += h + Theme.F(20);

            // ---- lyrics
            h = Theme.F(226);
            x = Card(g, y, h, "Lyrics", "Lyrics already in your songs, or in a .lrc file next to them, show up by themselves. For songs without any, AURA looks on LRCLIB and Genius and only keeps a match when the title and artist both agree. Found lyrics are saved in AURA — your music files aren't changed.", 60);
            float lyy = y + Theme.F(128);
            Toggle(g, "auto", x, lyy, cw - Theme.F(44), "Look for lyrics by itself", shell.Prefs.AutoLyrics, () => { shell.Prefs.AutoLyrics = !shell.Prefs.AutoLyrics; shell.Prefs.Save(); shell.Lyrics.Nudge(); Invalidate(); });
            var au = shell.Lyrics.Auto;
            int without = shell.Lyrics.Without().Count;
            string ls = au.Offline ? "Couldn't reach the lyric sites — AURA will try again later."
                : au.Running ? "Looking… " + au.Current + (au.Found > 0 ? "  ·  found " + au.Found + " so far" : "")
                : without == 0 ? "Every song has lyrics."
                : Theme.Count(without, "song") + " without lyrics" + (au.Found > 0 ? "  ·  found " + au.Found + " this time" : "");
            Theme.Write(g, ls, Theme.Font(13), Theme.Faint, new RectangleF(x, lyy + Theme.F(38), cw - Theme.F(260), Theme.F(24)));
            if (without > 0) Pill(g, "again", x + cw - Theme.F(44) - Theme.F(220), lyy + Theme.F(32), "Look again for all", () => { shell.Lyrics.TryAllAgain(); shell.Toast("Looking again for songs without lyrics"); Invalidate(); }, false, "refresh", 34);
            y += h + Theme.F(20);

            // ---- listening
            h = Theme.F(196);
            x = Card(g, y, h, "Listening", "AURA listens to each song once, slowly in the background, to draw its waveform, find its tempo (BPM) and key, line up versions when you switch between them, and spot duplicates that have different names. It runs at the lowest priority and rests between songs.", 60);
            float sy = y + Theme.F(128);
            Toggle(g, "listen", x, sy, cw - Theme.F(44), "Listen to songs in the background", shell.Prefs.Listen, () =>
            {
                shell.Prefs.Listen = !shell.Prefs.Listen; shell.Prefs.Save();
                shell.Sound.Background = shell.Prefs.Listen; shell.Sound.Kick(); Invalidate();
            });
            int heard = shell.Sound.Done, all = shell.Lib.Tracks.Count;
            string cur = shell.Sound.Current;
            Theme.Write(g, heard >= all ? "Listened to every song." : "Listened to " + heard.ToString("N0") + " of " + all.ToString("N0") + " songs" + (cur.Length > 0 ? "  ·  now: " + cur : shell.Prefs.Listen ? "" : "  ·  paused (the song that's playing is still listened to)"),
                Theme.Font(13), Theme.Faint, new RectangleF(x, sy + Theme.F(38), cw - Theme.F(60), Theme.F(24)));
            y += h + Theme.F(20);

            // ---- speech (auto-sync and transcribe)
            h = Theme.F(176);
            x = Card(g, y, h, "Lyrics in time", "Right-click a song → More → \"Write lyrics in time\" to time its lyrics to the song (auto-sync), or to write them down by listening (transcribe). It uses Whisper, on this PC. Each model downloads once.", 60);
            float wy = y + Theme.F(124), wx = x;
            bool any = false;
            foreach (var m in Whisper.Models)
            {
                if (!m.Ready) continue;
                any = true;
                var mm = m;
                long size = 0; try { size = new System.IO.FileInfo(m.File).Length; } catch { }
                wx = Pill(g, "wm:" + m.Name, wx, wy, m.Name + " · " + (size / 1e6).ToString("0") + " MB", () =>
                {
                    if (System.Windows.Forms.MessageBox.Show(this, "Delete the " + mm.Name + " model? It can be downloaded again any time.", "AURA", System.Windows.Forms.MessageBoxButtons.OKCancel) != System.Windows.Forms.DialogResult.OK) return;
                    try { System.IO.File.Delete(mm.File); } catch (Exception e) { shell.Toast("Couldn't delete it: " + e.Message); }
                    Invalidate();
                }, false, "trash", 32, "Delete this model").Right + Theme.F(8);
            }
            if (!any) Theme.Write(g, Whisper.Load() ? "No models downloaded yet." : "Not available: " + Whisper.LoadError + ".", Theme.Font(13), Theme.Faint, new RectangleF(x, wy, cw - Theme.F(60), Theme.F(30)));
            y += h + Theme.F(20);

            // ---- updates
            h = Theme.F(150);
            x = Card(g, y, h, "Updates", "AURA Native checks GitHub for a new version when it opens, and puts an Update button at the top when there is one.");
            float uy = y + Theme.F(96);
            float ub = Pill(g, "chk", x, uy, Updater.Checking ? "Checking…" : "Check now", () => Updater.Check(true), false, "refresh", 34).Right + Theme.F(10);
            if (Updater.Available && !Updater.Downloading) ub = Pill(g, "upd", ub, uy, "Update to " + Updater.Latest, () => (FindForm() as MainForm)?.InstallUpdate(), true, "download", 34).Right + Theme.F(10);
            Theme.Write(g, Updater.Status, Theme.Font(13), Theme.Faint, new RectangleF(ub + Theme.F(6), uy, cw - (ub - x) - Theme.F(50), Theme.F(34)));
            y += h + Theme.F(20);

            // ---- from the first AURA
            h = Theme.F(150);
            x = Card(g, y, h, "From the first AURA", "Your folders, playlists, favorites and play counts were brought over the first time this app opened. Do it again to pick up anything new — nothing is doubled up.");
            Pill(g, "imp", x, y + Theme.F(98), "Bring them over again", () => ImportAgain?.Invoke(), false, "refresh");
            y += h + Theme.F(20);

            // ---- about
            h = Theme.F(200);
            x = Card(g, y, h, "About", null);
            float ay = y + Theme.F(56);
            void Line(string k, string v) { Theme.Write(g, k, Theme.Font(13.5f), Theme.Faint, new RectangleF(x, ay, Theme.F(150), Theme.F(26))); Theme.Write(g, v, Theme.Font(13.5f), Theme.Text, new RectangleF(x + Theme.F(150), ay, cw - Theme.F(200), Theme.F(26))); ay += Theme.F(28); }
            Line("Version", "AURA Native " + App.Version + "  (early)");
            Line("Plays with", shell.Player.EngineName);
            Line("Library", Theme.Count(shell.Lib.Tracks.Count, "song") + " · " + Theme.Count(shell.Lib.Playlists.Count, "playlist"));
            Line("Saved in", App.DataDir);
            Pill(g, "open", x, ay + Theme.F(6), "Open that folder", () => { try { Process.Start("explorer.exe", "\"" + App.DataDir + "\""); } catch { } }, false, "folder", 34);
            y += h + Theme.F(40);
            ContentHeight = y;
        }

        protected override void OnBackButton() => shell.Back();
        protected override void OnForwardButton() => shell.Forward();
    }
}
