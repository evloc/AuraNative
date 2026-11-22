using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace Aura
{
    // Import a fan tracker: open the sheet, see what would change, tick what to keep, apply.
    sealed class TrackerForm : DarkForm
    {
        readonly MainForm shell;
        readonly List<Track> only;
        readonly TextBox link;
        readonly Label status;
        readonly CheckBox cTitle, cEra, cYear, cInfo, cOver;
        readonly ListView list;
        readonly Button apply;
        Tracker.Sheet sheet;
        List<Tracker.Item> items;
        List<Tracker.Proposal> props = new List<Tracker.Proposal>();

        public TrackerForm(MainForm shell, List<Track> only) : base("Import a fan tracker", 860, 640)
        {
            this.shell = shell; this.only = only;
            int x = Theme.P(20), w = Theme.P(820), y = Theme.P(14);
            var help = Lbl("Fan trackers are the spreadsheets that list an artist's songs with their era, producer and dates. AURA matches them to your songs by title"
                + (only != null && only.Count > 0 ? " (just the " + Theme.Count(only.Count, "song") + " you selected)" : "") + " and shows what it would change before anything is saved.", Theme.Sub, 12.5f);
            help.SetBounds(x, y, w, Theme.P(40)); help.TextAlign = ContentAlignment.TopLeft; y += Theme.P(46);
            var pick = Btn("Choose a .csv or .xlsx…", false); pick.SetBounds(x, y, Theme.P(220), Theme.P(34));
            link = Box(""); link.SetBounds(x + Theme.P(234), y + Theme.P(2), Theme.P(470), Theme.P(30));
            link.HandleCreated += (s, e) => Win.Cue(link.Handle, "…or paste a Google Sheets link");
            var open = Btn("Open link", false); open.SetBounds(x + Theme.P(714), y, Theme.P(106), Theme.P(34));
            y += Theme.P(44);
            status = Lbl("", Theme.Faint, 12.5f); status.SetBounds(x, y, w, Theme.P(24)); y += Theme.P(28);
            CheckBox Chk(string text, bool on, int cx, int cw) { var c = new CheckBox { Text = text, Checked = on, ForeColor = Theme.Text, Font = Theme.Font(13) }; c.SetBounds(cx, y, cw, Theme.P(26)); c.CheckedChanged += (s, e) => Rematch(); return c; }
            cTitle = Chk("Titles", true, x, Theme.P(90));
            cEra = Chk("Eras", true, x + Theme.P(96), Theme.P(80));
            cYear = Chk("Years", true, x + Theme.P(182), Theme.P(86));
            cInfo = Chk("Notes (producer, dates)", true, x + Theme.P(274), Theme.P(210));
            cOver = Chk("Replace what songs already have", false, x + Theme.P(500), Theme.P(300));
            y += Theme.P(34);
            list = new ListView { View = View.Details, CheckBoxes = true, FullRowSelect = true, BackColor = Theme.Surface2, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Font = Theme.Font(12.5f), HeaderStyle = ColumnHeaderStyle.Nonclickable };
            list.Columns.Add("Your song", Theme.P(250)); list.Columns.Add("In the tracker", Theme.P(220)); list.Columns.Add("What changes", Theme.P(330));
            list.SetBounds(x, y, w, Theme.P(640) - y - Theme.P(64));
            apply = Btn("Apply", true); apply.SetBounds(Theme.P(730), Theme.P(592), Theme.P(110), Theme.P(36)); apply.Enabled = false;
            var close = Btn("Close", false); close.SetBounds(Theme.P(610), Theme.P(592), Theme.P(110), Theme.P(36)); close.DialogResult = DialogResult.Cancel;
            Controls.AddRange(new Control[] { help, pick, link, open, status, cTitle, cEra, cYear, cInfo, cOver, list, apply, close });
            CancelButton = close;
            pick.Click += (s, e) =>
            {
                using (var d = new OpenFileDialog { Filter = "Spreadsheets|*.xlsx;*.xlsm;*.csv|All files|*.*", Title = "Choose a fan tracker" })
                    if (d.ShowDialog(this) == DialogResult.OK) OpenSheet(() => Tracker.Load(File.ReadAllBytes(d.FileName), d.FileName), Path.GetFileName(d.FileName));
            };
            open.Click += (s, e) => { var u = link.Text.Trim(); if (u.Length > 0) OpenSheet(() => Tracker.Download(u), "the sheet"); };
            apply.Click += (s, e) => Apply();
        }

        void OpenSheet(Func<Tracker.Sheet> get, string what)
        {
            status.Text = "Opening " + what + "…"; apply.Enabled = false;
            var ctx = SynchronizationContext.Current;
            new Thread(() =>
            {
                try
                {
                    var sh = get();
                    ctx.Post(_ => Loaded(sh), null);
                }
                catch (Exception e) { ctx.Post(_ => { if (!IsDisposed) status.Text = "Couldn't read it: " + e.Message; }, null); }
            }) { IsBackground = true }.Start();
        }

        void Loaded(Tracker.Sheet sh)
        {
            if (IsDisposed) return;
            if (sh.Headers.Count == 0 || sh.Rows.Count == 0) { status.Text = "No rows found in that spreadsheet."; return; }
            var map = Tracker.GuessMapping(sh.Headers);
            if (!map.ContainsKey("name")) { status.Text = "Couldn't find a column with the song names (looked for Name, Title, Song or Track)."; return; }
            if (!map.ContainsKey("era") && sh.Headers.Contains("Sheet")) map["era"] = sh.Headers.IndexOf("Sheet");
            sheet = sh; items = Tracker.Items(sh, map);
            Rematch();
        }

        void Rematch()
        {
            if (items == null) return;
            props = Tracker.Match(items, only != null && only.Count > 0 ? only : shell.Lib.Tracks, shell.Lib, cTitle.Checked, cEra.Checked, cYear.Checked, cInfo.Checked, cOver.Checked, out int unmatched);
            list.BeginUpdate(); list.Items.Clear();
            foreach (var p in props)
            {
                var ch = new List<string>();
                if (p.Title != null) ch.Add("title → " + p.Title);
                if (p.Era != null) ch.Add("era → " + p.Era);
                if (p.Year != null) ch.Add("year → " + p.Year);
                if (p.Info != null) ch.Add("notes");
                var it = new ListViewItem(new[] { p.Track.Name, p.Item.Raw + (p.Score < 1 ? "  (" + (p.Score * 100).ToString("0") + "% alike)" : ""), string.Join(", ", ch) }) { Checked = p.Score >= 0.9, Tag = p };
                if (p.Score < 0.9) it.ForeColor = Theme.Sub;
                list.Items.Add(it);
            }
            list.EndUpdate();
            status.Text = Theme.Count(items.Count, "song") + " in the tracker  ·  " + Theme.Count(props.Count, "of your songs", "of your songs") + " would change" + (unmatched > 0 ? "  ·  " + unmatched + " not in it" : "") + (props.Any(p => p.Score < 0.9) ? "  ·  close-but-not-exact matches are unticked" : "");
            apply.Enabled = props.Count > 0;
        }

        void Apply()
        {
            var chosen = list.Items.Cast<ListViewItem>().Where(i => i.Checked).Select(i => (Tracker.Proposal)i.Tag).ToList();
            if (chosen.Count == 0) return;
            shell.Lib.SaveUndo("Tracker import", chosen.Select(p => p.Track));
            foreach (var g in chosen.Where(p => p.Era != null).GroupBy(p => p.Era)) shell.Lib.SetEra(g.Select(p => p.Track).ToList(), g.Key, false);
            foreach (var p in chosen.Where(p => p.Info != null)) shell.Lib.SetInfo(p.Track, p.Info);
            var jobs = chosen.Where(p => (p.Title != null || p.Year != null) && TagWriter.CanWrite(p.Track.Path))
                .Select(p => (p.Track, new TagEdit { Title = p.Title, Year = p.Year })).ToList();
            if (jobs.Count > 0) shell.RunWrites(jobs, "Tracker import: " + Theme.Count(chosen.Count, "song"), false);
            else { shell.Toast("Tracker import: " + Theme.Count(chosen.Count, "song")); shell.OnLibraryChanged(); }
            DialogResult = DialogResult.OK;
        }

#if TESTHOOKS
        internal void TestLoad(Tracker.Sheet sh) => Loaded(sh);
        internal List<Tracker.Proposal> TestProps => props;
        internal void TestApply() => Apply();
#endif
    }
}
