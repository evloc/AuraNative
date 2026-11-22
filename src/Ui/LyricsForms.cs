using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Aura
{
    static class LyricsSave
    {
        // saves lyrics in AURA, and into the song file too when asked (and the file can hold them)
        public static void Save(MainForm shell, Track t, string text, string words, string source, bool toFile)
        {
            shell.Lyrics.Save(t, text, source, true, words ?? "");
            shell.Lib.IndexLyrics(t, text);
            if (toFile && TagWriter.CanWrite(t.Path))
                shell.RunWrites(new List<(Track, TagEdit)> { (t, new TagEdit { Lyrics = text, Words = words ?? "" }) }, "Lyrics saved");
            else { shell.Toast("Lyrics saved in AURA"); shell.Lyrics.Forget(t.Id); }
        }
    }

    // Typing or fixing lyrics by hand.
    sealed class LyricsEditForm : DarkForm
    {
        readonly TextBox box;
        readonly CheckBox toFile;

        public LyricsEditForm(MainForm shell, Track t) : base("Lyrics — " + t.Name, 620, 640)
        {
            var l = shell.Lyrics.Get(t);
            string start = l == null ? "" : l.Timed ? Lrc.ToLrc(l.Lines.Select(x => (x.T, x.Text))) : l.Plain;
            int x = Theme.P(20), w = Theme.P(580);
            var head = Lbl("Type or paste the lyrics, one line per line. Timings like [01:23.45] at the start of a line are kept — or leave them out and time them later.", Theme.Sub, 12.5f);
            head.SetBounds(x, Theme.P(12), w, Theme.P(44)); head.TextAlign = ContentAlignment.TopLeft;
            box = new TextBox { Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, BackColor = Theme.Surface2, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Font(14), WordWrap = false, Text = (start ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n") };
            box.SetBounds(x, Theme.P(60), w, Theme.P(470));
            toFile = new CheckBox { Text = TagWriter.CanWrite(t.Path) ? "Also save them into the song file" : "Saved in AURA (this kind of file can't hold lyrics)", Checked = TagWriter.CanWrite(t.Path), Enabled = TagWriter.CanWrite(t.Path), ForeColor = Theme.Text, Font = Theme.Font(13) };
            toFile.SetBounds(x, Theme.P(540), w, Theme.P(26));
            var save = Btn("Save", true); save.SetBounds(Theme.P(490), Theme.P(580), Theme.P(110), Theme.P(36));
            var cancel = Btn("Cancel", false); cancel.SetBounds(Theme.P(370), Theme.P(580), Theme.P(110), Theme.P(36)); cancel.DialogResult = DialogResult.Cancel;
            save.Click += (s, e) =>
            {
                var text = box.Text.Replace("\r\n", "\n").Trim();
                if (text.Length == 0) { MessageBox.Show(this, "There are no lyrics to save.", "AURA"); return; }
                // the word timings only still fit if the words didn't change
                string words = l != null && l.ByWord && text == (start ?? "").Trim() ? Sync.ToWords(l.Lines) : "";
                LyricsSave.Save(shell, t, text, words, "Edited in AURA", toFile.Checked);
                DialogResult = DialogResult.OK;
            };
            Controls.AddRange(new Control[] { head, box, toFile, save, cancel });
            CancelButton = cancel;
        }
    }

    // Timing lyrics by hand: play the song and press Space as each line starts.
    sealed class SyncByHandForm : DarkForm
    {
        readonly MainForm shell;
        readonly Track track;
        readonly List<string> lines;
        readonly double[] times;
        int next;                       // the line the next Space times
        readonly ListBox list;
        readonly Label status;
        readonly CheckBox toFile;
        readonly Timer tick = new Timer { Interval = 60 };

        public SyncByHandForm(MainForm shell, Track t, Lyrics l) : base("Time the lyrics — " + t.Name, 640, 680)
        {
            this.shell = shell; track = t;
            lines = (l.Timed ? l.Lines.Select(x => x.Text) : l.Plain.Split('\n')).Select(x => x.Trim()).Where(x => x.Length > 0 && !Regex.IsMatch(x, @"^[\[\(][^\]\)]*[\]\)]$")).ToList();
            times = Enumerable.Repeat(-1.0, lines.Count).ToArray();
            int x = Theme.P(20), w = Theme.P(600);
            var help = Lbl("Press Space right as each highlighted line starts.  Backspace: undo the last one.  − / +: move the selected line by 0.1 s.  Click a line to time again from there.", Theme.Sub, 12.5f);
            help.SetBounds(x, Theme.P(10), w, Theme.P(44)); help.TextAlign = ContentAlignment.TopLeft;
            list = new ListBox { BackColor = Theme.Surface2, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Font = Theme.Font(14), IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = Theme.P(28) };
            foreach (var s in lines) list.Items.Add(s);
            list.DrawItem += DrawLine;
            list.SetBounds(x, Theme.P(60), w, Theme.P(470));
            list.MouseClick += (s, e) =>
            {
                int i = list.IndexFromPoint(e.Location);
                if (i < 0) return;
                next = i;
                for (int k = i; k < times.Length; k++) times[k] = -1;
                double from = i > 0 && times[i - 1] >= 0 ? times[i - 1] - 1 : 0;
                if (shell.Player.Current?.Id == track.Id) shell.Player.Seek(Math.Max(0, from));
                list.Invalidate();
            };
            status = Lbl("", Theme.Sub, 13); status.SetBounds(x, Theme.P(536), w, Theme.P(24));
            toFile = new CheckBox { Text = TagWriter.CanWrite(t.Path) ? "Also save into the song file" : "Saved in AURA", Checked = TagWriter.CanWrite(t.Path), Enabled = TagWriter.CanWrite(t.Path), ForeColor = Theme.Text, Font = Theme.Font(13) };
            toFile.SetBounds(x, Theme.P(566), w, Theme.P(26));
            var restart = Btn("Start over", false); restart.SetBounds(x, Theme.P(620), Theme.P(130), Theme.P(36));
            restart.Click += (s, e) => { for (int k = 0; k < times.Length; k++) times[k] = -1; next = 0; shell.Player.PlayList(new List<long> { track.Id }); shell.Player.Seek(0); list.Invalidate(); list.Focus(); };
            var save = Btn("Save", true); save.SetBounds(Theme.P(510), Theme.P(620), Theme.P(110), Theme.P(36));
            save.Click += (s, e) => Save();
            var cancel = Btn("Cancel", false); cancel.SetBounds(Theme.P(390), Theme.P(620), Theme.P(110), Theme.P(36)); cancel.DialogResult = DialogResult.Cancel;
            Controls.AddRange(new Control[] { help, list, status, toFile, restart, save, cancel });
            KeyPreview = true;
            KeyDown += OnKey;
            tick.Tick += (s, e) => { status.Text = Theme.Time(shell.Player.Position) + "   ·   timed " + times.Count(v => v >= 0) + " of " + lines.Count + " lines"; };
            tick.Start();
            Shown += (s, e) =>
            {
                if (shell.Player.Current?.Id != track.Id) shell.Player.PlayList(new List<long> { track.Id });
                shell.Player.Seek(0);
                if (!shell.Player.IsPlaying) shell.Player.Toggle();
                list.Focus();
            };
            FormClosed += (s, e) => tick.Dispose();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys key)
        {
            if (key == Keys.Space) { Tap(); return true; }
            return base.ProcessCmdKey(ref msg, key);
        }

        void Tap()
        {
            if (next >= lines.Count) return;
            times[next] = Math.Round(shell.Player.Position, 2);
            next++;
            if (next < lines.Count) list.TopIndex = Math.Max(0, next - 6);
            list.Invalidate();
        }

        void OnKey(object s, KeyEventArgs e)
        {
            int sel = list.SelectedIndex >= 0 ? list.SelectedIndex : next - 1;
            switch (e.KeyCode)
            {
                case Keys.Back: if (next > 0) { next--; times[next] = -1; list.Invalidate(); } e.Handled = true; break;
                case Keys.Subtract: case Keys.OemMinus: if (sel >= 0 && times[sel] >= 0) { times[sel] = Math.Max(0, times[sel] - 0.1); list.Invalidate(); } e.Handled = true; break;
                case Keys.Add: case Keys.Oemplus: if (sel >= 0 && times[sel] >= 0) { times[sel] += 0.1; list.Invalidate(); } e.Handled = true; break;
            }
        }

        void DrawLine(object s, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool isNext = e.Index == next, sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(isNext ? Theme.Alpha(Theme.Accent, 70) : sel ? Theme.Surface : Theme.Surface2)) e.Graphics.FillRectangle(b, e.Bounds);
            Theme.Setup(e.Graphics);
            double t = times[e.Index];
            Theme.Write(e.Graphics, t >= 0 ? Theme.Time(t) + "." + ((int)(t * 10) % 10) : "", Theme.Font(12), Theme.Faint, new RectangleF(e.Bounds.X + Theme.F(8), e.Bounds.Y, Theme.F(60), e.Bounds.Height));
            Theme.Write(e.Graphics, lines[e.Index], Theme.Font(14, FontStyle.Regular, isNext), t >= 0 || isNext ? Theme.Text : Theme.Faint, new RectangleF(e.Bounds.X + Theme.F(74), e.Bounds.Y, e.Bounds.Width - Theme.F(80), e.Bounds.Height));
        }

        void Save()
        {
            var timed = lines.Select((l, i) => (t: times[i], l)).Where(x => x.t >= 0).ToList();
            if (timed.Count == 0) { MessageBox.Show(this, "No lines are timed yet — press Space as each line starts.", "AURA"); return; }
            if (timed.Count < lines.Count && MessageBox.Show(this, (lines.Count - timed.Count) + " lines aren't timed yet and will be left out. Save anyway?", "AURA", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            for (int i = 1; i < timed.Count; i++) if (timed[i].t < timed[i - 1].t) { var x = timed[i]; x.t = timed[i - 1].t + 0.1; timed[i] = x; }
            LyricsSave.Save(shell, track, Lrc.ToLrc(timed), "", "Timed by hand", toFile.Checked);
            DialogResult = DialogResult.OK;
        }
    }
}
