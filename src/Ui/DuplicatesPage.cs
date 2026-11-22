using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;

namespace Aura
{
    // Duplicates: the same song saved more than once
    sealed class DuplicatesPage : Canvas
    {
        readonly IShell shell;
        List<DupGroup> groups;
        readonly Dictionary<string, long> keepChoice = new Dictionary<string, long>();
        bool looking;
        string status = "";
        int heardAtLook = -1;

        public DuplicatesPage(IShell shell) { this.shell = shell; Look(); }

        public void Reload() { if (!looking) Look(); }

        void Look()
        {
            looking = true; status = "Looking for duplicates…";
            var ctx = SynchronizationContext.Current;
            heardAtLook = shell.Sound.Done;
            new Thread(() =>
            {
                List<DupGroup> found;
                try { found = Duplicates.Find(shell.Lib, shell.Sound, s => ctx.Post(_ => { status = s; Invalidate(); }, null)); }
                catch (Exception e) { App.Log("duplicates: " + e); found = new List<DupGroup>(); ctx.Post(_ => shell.Toast("Couldn't look for duplicates: " + e.Message), null); }
                ctx.Post(_ => { if (IsDisposed) return; groups = found; looking = false; Invalidate(); }, null);
            }) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "duplicates" }.Start();
            Invalidate();
        }

        Track KeepOf(DupGroup g) => keepChoice.TryGetValue(g.Key, out var id) ? g.Tracks.FirstOrDefault(t => t.Id == id) ?? g.Keep : g.Keep;

        static string Folder(string p)
        {
            var d = Path.GetDirectoryName(p) ?? "";
            var parts = d.Split('\\', '/').Where(x => x.Length > 0).ToArray();
            return parts.Length <= 2 ? d : "…\\" + string.Join("\\", parts.Skip(parts.Length - 2));
        }

        protected override void Draw(Graphics g, RectangleF view)
        {
            float pad = Theme.F(28), y = Theme.F(8), cw = Width - pad * 2;
            Theme.Write(g, "Duplicates", Theme.Font(34, FontStyle.Bold), Theme.Text, new RectangleF(pad, y + Theme.F(24), cw, Theme.F(48)));
            Theme.Write(g, "The same song saved more than once. AURA keeps the best copy (best sound, then the most complete tags) and moves the others into a \"_duplicates\" folder next to them — nothing is deleted, and their plays, playlists and favorites move to the copy you keep. Different versions (v1, v2, snippets, remixes, sped up…) never count as duplicates.",
                Theme.Font(13.5f), Theme.Sub, new RectangleF(pad, y + Theme.F(72), Math.Min(cw, Theme.F(900)), Theme.F(64)), Theme.Wrap);
            y += Theme.F(146);
            int heard = shell.Sound.Done, total = shell.Lib.Tracks.Count;
            string listen = heard >= total ? "AURA has listened to all your songs, so it can also find copies with different names."
                : shell.Prefs.Listen ? "AURA has listened to " + heard.ToString("N0") + " of " + total.ToString("N0") + " songs so far (it keeps going in the background). Until it has heard a song, that song is only matched by name and length."
                : "Listening in the background is off (Settings), so songs are only matched by file and by name and length.";
            Theme.Write(g, listen, Theme.Font(12.5f), Theme.Faint, new RectangleF(pad, y, Math.Min(cw, Theme.F(900)), Theme.F(40)), Theme.Wrap);
            y += Theme.F(48);

            if (looking || groups == null)
            {
                Theme.Write(g, status, Theme.Font(15), Theme.Sub, new RectangleF(pad, y, cw, Theme.F(30)));
                ContentHeight = y + Theme.F(60);
                return;
            }
            float bx = pad;
            var ready = groups.Where(x => x.Checked).ToList();
            var all = groups.ToList();
            // every group at once (the copies only move to _duplicates folders, so nothing is lost)
            if (all.Count > 0)
                bx = Pill(g, "all", bx, y, all.Count == 1 ? "Clean it up" : "Clean up all " + all.Count.ToString("N0"), () => CleanUp(all), true, "check", 38, "Keep the best copy of each and move the others to _duplicates").Right + Theme.F(10);
            if (ready.Count > 0 && ready.Count < all.Count)
                bx = Pill(g, "ready", bx, y, "Only the " + ready.Count.ToString("N0") + " confirmed by listening", () => CleanUp(ready), false, null, 38, "Leave the ones AURA hasn't heard yet").Right + Theme.F(10);
            Pill(g, "again", bx, y, heard > heardAtLook ? "Look again (AURA has heard more songs)" : "Look again", Look, false, "refresh", 38);
            y += Theme.F(58);
            if (groups.Count == 0)
            {
                Theme.Write(g, "No duplicates found.", Theme.Font(20, FontStyle.Bold), Theme.Text, new RectangleF(pad, y, cw, Theme.F(32)));
                ContentHeight = y + Theme.F(60);
                return;
            }
            Theme.Write(g, Theme.Count(groups.Count, "song") + " saved more than once", Theme.Font(19, FontStyle.Bold), Theme.Text, new RectangleF(pad, y, cw, Theme.F(28)));
            y += Theme.F(42);

            float rowH = Theme.F(50);
            for (int gi = 0; gi < groups.Count; gi++)
            {
                var grp = groups[gi];
                float h = Theme.F(64) + grp.Tracks.Count * rowH + Theme.F(58);
                var card = new RectangleF(pad, y, cw, h);
                if (card.Bottom < view.Top || card.Top > view.Bottom) { y += h + Theme.F(16); continue; }
                Theme.Fill(g, Theme.Surface, card, Theme.F(14));
                var keep = KeepOf(grp);
                // header
                var chipF = Theme.Font(11.5f, FontStyle.Regular, true);
                string chip = grp.Title.ToUpperInvariant() + (grp.Checked ? "" : "  ·  NOT HEARD YET");
                float chw = Theme.Measure(g, chip, chipF) + Theme.F(20);
                var chr = new RectangleF(card.X + Theme.F(20), card.Y + Theme.F(18), chw, Theme.F(24));
                Theme.Fill(g, grp.Kind == "exact" ? Theme.Alpha(Theme.Accent, 90) : Theme.Surface2, chr, chr.Height / 2);
                Theme.Write(g, chip, chipF, Theme.Text, chr, Theme.Center);
                Theme.Write(g, keep.Name + "  ·  " + keep.ArtistName, Theme.Font(15, FontStyle.Regular, true), Theme.Text, new RectangleF(chr.Right + Theme.F(14), card.Y + Theme.F(16), card.Width - chw - Theme.F(60), Theme.F(28)));
                float ry = card.Y + Theme.F(58);
                foreach (var t in grp.Tracks)
                {
                    string id = "d:" + gi + ":" + t.Id;
                    var rr = new RectangleF(card.X + Theme.F(10), ry, card.Width - Theme.F(20), rowH - Theme.F(4));
                    bool isKeep = t == keep;
                    if (isKeep) Theme.Fill(g, Theme.Alpha(Theme.Accent, 40), rr, Theme.F(10));
                    else if (Hover(id)) Theme.Fill(g, Theme.Surface2, rr, Theme.F(10));
                    // keep mark
                    var mr = new RectangleF(rr.X + Theme.F(12), rr.Y + (rr.Height - Theme.F(20)) / 2, Theme.F(20), Theme.F(20));
                    using (var pen = new Pen(isKeep ? Theme.Accent : Theme.Faint, Theme.F(2))) g.DrawEllipse(pen, mr);
                    if (isKeep) using (var b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, RectangleF.Inflate(mr, -Theme.F(5), -Theme.F(5)));
                    float tx = mr.Right + Theme.F(14);
                    float infoW = Theme.F(330);
                    Theme.Write(g, Path.GetFileName(t.Path), Theme.Font(13.5f, FontStyle.Regular, isKeep), Theme.Text, new RectangleF(tx, rr.Y + Theme.F(4), rr.Right - tx - infoW - Theme.F(60), Theme.F(22)));
                    Theme.Write(g, (isKeep ? "Keep  ·  " : "Moves to _duplicates  ·  ") + Folder(t.Path), Theme.Font(12), isKeep ? Theme.AccentText : Theme.Faint, new RectangleF(tx, rr.Y + Theme.F(24), rr.Right - tx - infoW - Theme.F(60), Theme.F(18)));
                    string kind = (Path.GetExtension(t.Path).TrimStart('.').ToUpperInvariant()) + (Duplicates.Lossless(t) ? " (lossless)" : Duplicates.Kbps(t) > 0 ? "  " + Duplicates.Kbps(t) + " kbps" : "");
                    string extra = Theme.Time(t.Duration) + "  ·  " + (t.Size / 1048576.0).ToString("0.0") + " MB" + (t.Plays > 0 ? "  ·  " + Theme.Count(t.Plays, "play") : "") + (t.Fav ? "  ·  ♥" : "");
                    var ir = new RectangleF(rr.Right - infoW - Theme.F(50), rr.Y, infoW, rr.Height);
                    Theme.Write(g, kind, Theme.Font(12.5f), Theme.Sub, new RectangleF(ir.X, rr.Y + Theme.F(4), ir.Width, Theme.F(22)), Theme.Right);
                    Theme.Write(g, extra, Theme.Font(12), Theme.Faint, new RectangleF(ir.X, rr.Y + Theme.F(24), ir.Width, Theme.F(18)), Theme.Right);
                    var tt = t; var key = grp.Key;
                    var ids = grp.Tracks.Select(x => x.Id).ToList();
                    IconButton(g, id + ":play", new RectangleF(rr.Right - Theme.F(40), rr.Y + (rr.Height - Theme.F(32)) / 2, Theme.F(32), Theme.F(32)), "play", () => shell.Player.PlayList(ids, ids.IndexOf(tt.Id)), "Play this copy");
                    AddHit(id, new RectangleF(rr.X, rr.Y, rr.Width - Theme.F(46), rr.Height), () => { keepChoice[key] = tt.Id; Invalidate(); }, isKeep ? "This copy is kept" : "Keep this copy instead");
                    ry += rowH;
                }
                float by = card.Bottom - Theme.F(50);
                var gg = grp;
                var r1 = Pill(g, "c:" + gi, card.X + Theme.F(20), by, "Clean up", () => CleanUp(new List<DupGroup> { gg }), true, null, 34, "Keep the marked copy and move the others to _duplicates");
                Pill(g, "n:" + gi, r1.Right + Theme.F(10), by, "Not duplicates", () => { Duplicates.Ignore(shell.Lib, gg); groups.Remove(gg); Invalidate(); }, false, null, 34, "They're different — don't show them here again");
                y += h + Theme.F(16);
            }
            ContentHeight = y + Theme.F(40);
        }

        void CleanUp(List<DupGroup> which)
        {
            var jobs = which.Select(x => (x, KeepOf(x))).ToList();
            int n = jobs.Sum(j => j.x.Tracks.Count - 1);
            int unheard = which.Count(x => !x.Checked);
            if (!MainForm.UiTestMode && System.Windows.Forms.MessageBox.Show(this, "Move " + Theme.Count(n, "extra copy", "extra copies") + " from " + Theme.Count(which.Count, "song") + " into _duplicates folders?\n\nThe copies you keep get their plays, playlists and favorites. Nothing is deleted — you can delete the _duplicates folders yourself later."
                + (unheard > 0 ? "\n\n" + unheard + " of these were matched by name and length only (AURA hasn't listened to them yet), so glance over them first if you're not sure." : ""),
                "Clean up duplicates", System.Windows.Forms.MessageBoxButtons.OKCancel, System.Windows.Forms.MessageBoxIcon.Question) != System.Windows.Forms.DialogResult.OK) return;
            shell.MergeDuplicates(jobs);
            foreach (var (x, _) in jobs) groups.Remove(x);
            Invalidate();
        }

        protected override void OnBackButton() => shell.Back();
        protected override void OnForwardButton() => shell.Forward();
    }
}
