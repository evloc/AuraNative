using System;
using System.Drawing;
using System.Windows.Forms;

namespace Aura
{
    sealed class DarkColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Surface;
        public override Color ImageMarginGradientBegin => Theme.Surface;
        public override Color ImageMarginGradientMiddle => Theme.Surface;
        public override Color ImageMarginGradientEnd => Theme.Surface;
        public override Color MenuBorder => Theme.Hair;
        public override Color MenuItemBorder => Theme.Surface2;
        public override Color MenuItemSelected => Theme.Surface2;
        public override Color MenuItemSelectedGradientBegin => Theme.Surface2;
        public override Color MenuItemSelectedGradientEnd => Theme.Surface2;
        public override Color MenuItemPressedGradientBegin => Theme.Surface2;
        public override Color MenuItemPressedGradientEnd => Theme.Surface2;
        public override Color SeparatorDark => Theme.Hair;
        public override Color SeparatorLight => Theme.Hair;
        public override Color CheckBackground => Theme.Surface2;
        public override Color CheckSelectedBackground => Theme.Surface2;
        public override Color CheckPressedBackground => Theme.Surface2;
    }

    sealed class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Faint;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.Sub;
            base.OnRenderArrow(e);
        }
    }

    static class Menus
    {
        public static ContextMenuStrip New()
        {
            var m = new ContextMenuStrip { Renderer = new DarkRenderer(), ShowImageMargin = false, Font = Theme.Font(13.5f), BackColor = Theme.Surface, ForeColor = Theme.Text };
            m.Closed += (s, e) => { var mm = (ContextMenuStrip)s; mm.BeginInvoke(new Action(mm.Dispose)); };
            return m;
        }

        public static ToolStripMenuItem Item(string text, Action click, bool enabled = true)
        {
            var i = new ToolStripMenuItem(text) { Enabled = enabled, ForeColor = Theme.Text, Padding = new Padding(0, Theme.P(3), 0, Theme.P(3)) };
            if (click != null) i.Click += (s, e) => click();
            return i;
        }

        public static void Sub(ToolStripMenuItem parent)
        {
            if (parent.DropDown is ToolStripDropDownMenu dd)
            {
                dd.Renderer = new DarkRenderer(); dd.ShowImageMargin = false; dd.BackColor = Theme.Surface; dd.Font = Theme.Font(13.5f);
            }
        }
    }

    // A small dark window that asks for one line of text (playlist names).
    sealed class Ask : Form
    {
        readonly TextBox box;
        Ask(string title, string prompt, string value, string ok)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Surface; ForeColor = Theme.Text; Font = Theme.Font(14);
            // the question can be a few lines long
            int textH;
            using (var g = CreateGraphics()) textH = (int)Math.Ceiling(g.MeasureString(prompt ?? "", Font, Theme.P(376)).Height);
            textH = Math.Max(Theme.P(24), Math.Min(Theme.P(160), textH + Theme.P(4)));
            int extra = textH - Theme.P(24);
            ClientSize = new Size(Theme.P(420), Theme.P(168) + extra);
            var lbl = new Label { Text = prompt, AutoSize = false, Location = new Point(Theme.P(22), Theme.P(18)), Size = new Size(Theme.P(376), textH), ForeColor = Theme.Sub };
            box = new TextBox { Text = value ?? "", Location = new Point(Theme.P(22), Theme.P(50) + extra), Width = Theme.P(376), BackColor = Theme.Surface2, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Font(15) };
            var okB = new Button { Text = ok, DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat, BackColor = Theme.Accent, ForeColor = Color.White, Size = new Size(Theme.P(110), Theme.P(36)), Location = new Point(Theme.P(288), Theme.P(110) + extra) };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.Flat, BackColor = Theme.Surface2, ForeColor = Theme.Text, Size = new Size(Theme.P(100), Theme.P(36)), Location = new Point(Theme.P(176), Theme.P(110) + extra) };
            okB.FlatAppearance.BorderSize = 0; cancel.FlatAppearance.BorderSize = 0;
            Controls.AddRange(new Control[] { lbl, box, okB, cancel });
            AcceptButton = okB; CancelButton = cancel;
            Shown += (s, e) => { box.Focus(); box.SelectAll(); };
            HandleCreated += (s, e) => Win.DarkTitle(Handle);
        }

        // null when cancelled (or left empty, unless allowEmpty)
        public static string ForText(IWin32Window owner, string title, string prompt, string value, string ok = "OK", bool allowEmpty = false)
        {
            using (var a = new Ask(title, prompt, value, ok))
                return a.ShowDialog(owner) == DialogResult.OK && (allowEmpty || a.box.Text.Trim().Length > 0) ? a.box.Text.Trim() : null;
        }
    }
}
