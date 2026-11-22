using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace Aura
{
    // Auto-sync (time the lyrics a song has) and Transcribe (write them down by listening).
    sealed class TranscribeForm : DarkForm
    {
        readonly MainForm shell;
        readonly Track track;
        readonly RadioButton syncMode, writeMode;
        readonly ComboBox model, lang;
        readonly CheckBox toFile;
        readonly Button start, save, cancel;
        readonly Label status;
        readonly ProgressBar bar;
        readonly ListBox preview;
        readonly List<string> known;
        Sync.Result result;
        volatile bool stop;
        bool running;

        static readonly (string code, string name)[] Langs = { ("auto", "Find out by itself"), ("en", "English"), ("es", "Spanish"), ("fr", "French"), ("de", "German"), ("pt", "Portuguese"), ("it", "Italian"), ("ja", "Japanese"), ("ko", "Korean") };

        public TranscribeForm(MainForm shell, Track t) : base("Lyrics in time — " + t.Name, 640, 600)
        {
            this.shell = shell; track = t;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
            var lyr = shell.Lyrics.Get(t);
            known = KnownLines(lyr);
            int x = Theme.P(22), w = Theme.P(596), y = Theme.P(16);
            var head = Lbl(t.Name + "  ·  " + t.ArtistName, Theme.Text, 15); head.SetBounds(x, y, w, Theme.P(30)); y += Theme.P(38);
            syncMode = new RadioButton { Text = known.Count > 0 ? "Time the lyrics it has (" + Theme.Count(known.Count, "line") + ") — auto-sync" : "Time the lyrics it has — this song has none yet", Enabled = known.Count > 0, Checked = known.Count > 0, ForeColor = Theme.Text, Font = Theme.Font(13.5f) };
            syncMode.SetBounds(x, y, w, Theme.P(28)); y += Theme.P(30);
            writeMode = new RadioButton { Text = "Write the lyrics down by listening — transcribe", Checked = known.Count == 0, ForeColor = Theme.Text, Font = Theme.Font(13.5f) };
            writeMode.SetBounds(x, y, w, Theme.P(28)); y += Theme.P(34);
            var note = Lbl("AURA listens to the song with Whisper, on this PC (nothing is uploaded). It works best when the vocals are clear; heavy beats, ad-libs and effects make it guess more. Words it couldn't place are spread out between the ones it heard.", Theme.Sub, 12.5f);
            note.SetBounds(x, y, w, Theme.P(52)); note.TextAlign = ContentAlignment.TopLeft; y += Theme.P(58);

            var ml = Lbl("Model"); ml.SetBounds(x, y, Theme.P(90), Theme.P(30));
            model = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Theme.Surface2, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat, Font = Theme.Font(13) };
            foreach (var m in Whisper.Models) model.Items.Add(m.Label + " — " + m.Name + ", " + (m.Size / 1e6).ToString("0") + " MB" + (m.Ready ? " (downloaded)" : " (downloads once)"));
            model.SelectedIndex = Math.Max(0, Array.FindIndex(Whisper.Models, m => m.Name == shell.Prefs.WhisperModel));
            model.SetBounds(x + Theme.P(90), y, Theme.P(400), Theme.P(30)); y += Theme.P(38);
            var ll = Lbl("Language"); ll.SetBounds(x, y, Theme.P(90), Theme.P(30));
            lang = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Theme.Surface2, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat, Font = Theme.Font(13) };
            foreach (var (c, n) in Langs) lang.Items.Add(n);
            lang.SelectedIndex = 0;
            lang.SetBounds(x + Theme.P(90), y, Theme.P(220), Theme.P(30)); y += Theme.P(40);
            toFile = new CheckBox { Text = TagWriter.CanWrite(t.Path) ? "Also save the timed lyrics into the song file" : "Saved in AURA (this kind of file can't hold them)", Checked = TagWriter.CanWrite(t.Path), Enabled = TagWriter.CanWrite(t.Path), ForeColor = Theme.Text, Font = Theme.Font(13) };
            toFile.SetBounds(x, y, w, Theme.P(28)); y += Theme.P(36);

            bar = new ProgressBar { Minimum = 0, Maximum = 1000, Visible = false };
            bar.SetBounds(x, y, w, Theme.P(10)); y += Theme.P(16);
            status = Lbl("", Theme.Sub, 13); status.SetBounds(x, y, w, Theme.P(26)); y += Theme.P(30);
            preview = new ListBox { BackColor = Theme.Surface2, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Font = Theme.Font(13), IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = Theme.P(24) };
            preview.DrawItem += DrawLine;
            preview.SetBounds(x, y, w, Theme.P(600) - y - Theme.P(64));
            start = Btn("Start", true); start.SetBounds(Theme.P(508), Theme.P(548), Theme.P(110), Theme.P(36));
            save = Btn("Save", true); save.SetBounds(Theme.P(508), Theme.P(548), Theme.P(110), Theme.P(36)); save.Visible = false;
            cancel = Btn("Close", false); cancel.SetBounds(Theme.P(388), Theme.P(548), Theme.P(110), Theme.P(36));
            start.Click += (s, e) => Begin();
            save.Click += (s, e) => Save();
            cancel.Click += (s, e) => { if (running) { stop = true; status.Text = "Stopping…"; } else Close(); };
            Controls.AddRange(new Control[] { head, syncMode, writeMode, note, ml, model, ll, lang, toFile, bar, status, preview, start, save, cancel });
            FormClosing += (s, e) => { stop = true; };
        }

        // the lyric lines to time: section labels like [Chorus] are left out
        static List<string> KnownLines(Lyrics l)
        {
            if (l == null) return new List<string>();
            var lines = l.Timed ? l.Lines.Select(x => x.Text) : l.Plain.Split('\n');
            return lines.Select(x => x.Trim()).Where(x => x.Length > 0 && !Regex.IsMatch(x, @"^[\[\(][^\]\)]*[\]\)]$")).ToList();
        }

        void DrawLine(object s, DrawItemEventArgs e)
        {
            if (e.Index < 0 || result == null || e.Index >= result.Lines.Count) return;
            var l = result.Lines[e.Index];
            bool guess = result.Guessed[e.Index];
            using (var b = new SolidBrush(Theme.Surface2)) e.Graphics.FillRectangle(b, e.Bounds);
            Theme.Setup(e.Graphics);
            Theme.Write(e.Graphics, l.T >= 0 ? Theme.Time(l.T) + "." + ((int)(l.T * 10) % 10) : "—", Theme.Font(12), Theme.Faint, new RectangleF(e.Bounds.X + Theme.F(8), e.Bounds.Y, Theme.F(60), e.Bounds.Height));
            Theme.Write(e.Graphics, l.Text, Theme.Font(13), guess ? Theme.Faint : Theme.Text, new RectangleF(e.Bounds.X + Theme.F(74), e.Bounds.Y, e.Bounds.Width - Theme.F(80), e.Bounds.Height));
        }

        void Begin()
        {
            if (running) return;
            var m = Whisper.Models[Math.Max(0, model.SelectedIndex)];
            shell.Prefs.WhisperModel = m.Name; shell.Prefs.Save();
            if (!Whisper.Load())
            {
                status.Text = "Can't listen: " + Whisper.LoadError + ".";
                return;
            }
            bool align = syncMode.Checked && known.Count > 0;
            string code = Langs[Math.Max(0, lang.SelectedIndex)].code;
            running = true; stop = false; result = null; preview.Items.Clear();
            start.Enabled = false; model.Enabled = lang.Enabled = syncMode.Enabled = writeMode.Enabled = false;
            cancel.Text = "Stop"; bar.Visible = true; bar.Value = 0;
            var ctx = SynchronizationContext.Current;
            void Say(string text, double k) => ctx.Post(_ => { if (IsDisposed) return; status.Text = text; if (k >= 0) bar.Value = (int)(Math.Max(0, Math.Min(1, k)) * 1000); }, null);
            var path = track.Path; var id = track.Id; var title = LyricsOnline.CleanTitle(track.Name); var artist = track.MainArtist;
            var lines = known.ToList();
            new Thread(() =>
            {
                try
                {
                    if (!m.Ready)
                    {
                        Say("Downloading the " + m.Name + " model (" + (m.Size / 1e6).ToString("0") + " MB, just this once)…", 0);
                        Whisper.Download(m, p => Say("Downloading the " + m.Name + " model… " + (p * 100).ToString("0") + "%", p * 0.3), () => stop);
                    }
                    Say("Reading the song…", 0.3);
                    var pcm = Decoder.Mono(path, id, 16000, 20 * 60, () => stop, out _);
                    if (stop) throw new OperationCanceledException();
                    Say("Listening…", 0.35);
                    string prompt = align ? string.Join(" ", string.Join(" ", lines).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) : "Lyrics of the song " + title + (artist.Length > 0 && artist != "Unknown artist" ? " by " + artist : "") + ":";
                    if (prompt.Length > 220) prompt = prompt.Substring(0, 220);
                    var heard = Whisper.Listen(m.File, m.Dtw, pcm, code, prompt, align ? 1 : 5, p => Say("Listening… " + (p * 100).ToString("0") + "%", 0.35 + p * 0.6), () => stop, out var detected);
                    if (stop) throw new OperationCanceledException();
                    Sync.Result r;
                    if (align) r = Sync.Align(lines, heard);
                    else r = Sync.Lines(heard);
                    if (r.Lines.Count == 0) throw new InvalidOperationException("no singing was heard");
                    ctx.Post(_ => Done(r, align, heard.Count, detected), null);
                }
                catch (OperationCanceledException) { ctx.Post(_ => Failed("Stopped."), null); }
                catch (Exception e) { App.Log("lyrics in time: " + e); ctx.Post(_ => Failed("Didn't work: " + e.Message), null); }
            }) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "whisper" }.Start();
        }

        void Failed(string why)
        {
            if (IsDisposed) return;
            running = false; status.Text = why; bar.Visible = false;
            start.Enabled = true; model.Enabled = lang.Enabled = writeMode.Enabled = true; syncMode.Enabled = known.Count > 0;
            cancel.Text = "Close";
        }

        void Done(Sync.Result r, bool align, int heard, string lang)
        {
            if (IsDisposed) return;
            if (!r.Lines.Any(l => l.T >= 0))
            {
                Failed(align ? "None of the lyrics could be heard in the song (heard " + Theme.Count(heard, "word") + ") — nothing was changed. They may be for a different version, or the vocals are hard to make out."
                             : "No singing could be made out — nothing was changed.");
                return;
            }
            running = false; result = r; bar.Visible = false; cancel.Text = "Close";
            preview.Items.Clear();
            foreach (var l in r.Lines) preview.Items.Add(l.Text);
            int guessed = r.Guessed.Count(g => g);
            status.Text = align
                ? "Heard " + Theme.Count(heard, "word") + " — matched " + (r.Confidence * 100).ToString("0") + "% of the lyrics" + (guessed > 0 ? "; " + Theme.Count(guessed, "line") + " (dimmed) are best guesses" : "") + ". Check it while it plays, then Save."
                : "Wrote down " + Theme.Count(r.Lines.Count, "line") + (lang.Length > 0 ? " (" + lang + ")" : "") + ". Words may be misheard — you can fix them after saving with Find lyrics.";
            start.Visible = false; save.Visible = true;
            if (align && r.Confidence < 0.3) status.Text = "Only " + (r.Confidence * 100).ToString("0") + "% of the lyrics were heard — they may be for a different version. Save anyway if it looks right.";
        }

        void Save()
        {
            if (result == null) return;
            var lines = result.Lines.Where(l => l.T >= 0).ToList();
            if (lines.Count == 0) return;
            var lrc = Sync.ToLrc(lines);
            var words = Sync.ToWords(lines);
            bool align = syncMode.Checked && known.Count > 0;
            shell.Lyrics.Save(track, lrc, align ? "AURA auto-sync" : "AURA transcription", true, words);
            if (toFile.Checked && TagWriter.CanWrite(track.Path))
                shell.RunWrites(new List<(Track, TagEdit)> { (track, new TagEdit { Lyrics = lrc, Words = words }) }, "Timed lyrics saved");
            else shell.Toast("Timed lyrics saved in AURA");
            Close();
        }

#if TESTHOOKS
        internal void TestStart(bool transcribe) { if (transcribe) writeMode.Checked = true; Begin(); }
        internal bool TestRunning => running;
        internal Sync.Result TestResult => result;
        internal string TestStatus => status.Text;
        internal void TestSave() => Save();
#endif
    }
}
