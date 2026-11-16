using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Aura
{
    sealed class Sidebar : Canvas
    {
        readonly IShell shell;
        public Sidebar(IShell shell)
        {
            this.shell = shell;
            BackColor = Theme.Side;
            Width = Theme.P(240);
        }

        bool On(string kind, long id = 0) => shell.Current != null && shell.Current.Kind == kind && (id == 0 || shell.Current.Id == id);

        void NavItem(Graphics g, ref float y, string id, string icon, string text, Route to, bool on, string right = null)
        {
            var r = new RectangleF(Theme.F(12), y, Width - Theme.F(24), Theme.F(40));
            bool hot = Hover(id);
            if (on || hot) Theme.Fill(g, on ? Theme.Surface2 : Theme.Surface, r, Theme.F(10));
            if (on) Theme.Fill(g, Theme.Accent, new RectangleF(r.X, r.Y + Theme.F(10), Theme.F(3), r.Height - Theme.F(20)), Theme.F(1.5f));
            float s = Theme.F(20);
            Icons.Draw(g, icon, new RectangleF(r.X + Theme.F(14), r.Y + (r.Height - s) / 2, s, s), on ? Theme.AccentText : hot ? Theme.Text : Theme.Sub, on);
            Theme.Write(g, text, Theme.Font(14, FontStyle.Regular, on), on || hot ? Theme.Text : Theme.Sub, new RectangleF(r.X + Theme.F(46), r.Y, r.Width - Theme.F(56), r.Height));
            if (right != null) Theme.Write(g, right, Theme.Font(12), Theme.Faint, new RectangleF(r.X, r.Y, r.Width - Theme.F(12), r.Height), Theme.Right);
            AddHit(id, r, () => shell.Go(to));
            y += Theme.F(42);
        }

        protected override void Draw(Graphics g, RectangleF view)
        {
            float y = Theme.F(20);
            // logo
            float ls = Theme.F(28);
            Icons.Draw(g, "logo", new RectangleF(Theme.F(24), y, ls, ls), Theme.AccentText);
            Theme.Write(g, "AURA", Theme.Font(20, FontStyle.Bold), Theme.Text, new RectangleF(Theme.F(60), y - Theme.F(2), Theme.F(150), ls + Theme.F(4)));
            y += Theme.F(52);

            NavItem(g, ref y, "home", "home", "Home", new Route("home"), On("home"));
            NavItem(g, ref y, "songs", "songs", "Songs", new Route("songs"), On("songs") || On("search"), shell.Lib.Tracks.Count > 0 ? shell.Lib.Tracks.Count.ToString("N0") : null);
            NavItem(g, ref y, "artists", "artist", "Artists", new Route("artists"), On("artists") || On("artist"));
            NavItem(g, ref y, "albums", "album", "Albums", new Route("albums"), On("albums") || On("album"));
            NavItem(g, ref y, "favorites", "heart", "Favorites", new Route("favorites"), On("favorites"));
            NavItem(g, ref y, "queue", "queue", "Up next", new Route("queue"), On("queue"));

            y += Theme.F(14);
            using (var pen = new Pen(Theme.Hair)) g.DrawLine(pen, Theme.F(24), y, Width - Theme.F(24), y);
            y += Theme.F(16);
            Theme.Write(g, "PLAYLISTS", Theme.Font(11.5f, FontStyle.Regular, true), Theme.Faint, new RectangleF(Theme.F(26), y, Theme.F(150), Theme.F(26)));
            IconButton(g, "newpl", new RectangleF(Width - Theme.F(52), y - Theme.F(2), Theme.F(30), Theme.F(30)), "plus", () => shell.NewPlaylist(), "New playlist");
            y += Theme.F(34);
            if (shell.Lib.Playlists.Count == 0)
            {
                var r = new RectangleF(Theme.F(26), y, Width - Theme.F(52), Theme.F(40));
                Theme.Write(g, "Make one with + or right-click any song", Theme.Font(12.5f), Theme.Faint, r, Theme.Wrap);
                y += Theme.F(44);
            }
            foreach (var p in shell.Lib.Playlists)
            {
                string id = "pl:" + p.Id;
                var r = new RectangleF(Theme.F(12), y, Width - Theme.F(24), Theme.F(36));
                bool on = On("playlist", p.Id), hot = Hover(id);
                if (on || hot) Theme.Fill(g, on ? Theme.Surface2 : Theme.Surface, r, Theme.F(9));
                Theme.Write(g, p.Name, Theme.Font(13.5f, FontStyle.Regular, on), on || hot ? Theme.Text : Theme.Sub, new RectangleF(r.X + Theme.F(14), r.Y, r.Width - Theme.F(54), r.Height));
                Theme.Write(g, p.Items.Count.ToString(), Theme.Font(12), Theme.Faint, new RectangleF(r.X, r.Y, r.Width - Theme.F(12), r.Height), Theme.Right);
                var pl = p;
                var h = AddHit(id, r, () => shell.Go(new Route("playlist", "", pl.Id)));
                h.Right = pt => PlaylistMenu(pl, pt);
                y += Theme.F(38);
            }
            y += Theme.F(70);
            ContentHeight = y;
        }

        protected override void DrawOver(Graphics g)
        {
            // settings and scan status stay at the bottom
            float h = Theme.F(62);
            var area = new RectangleF(0, Height - h, Width, h);
            using (var b = new SolidBrush(Theme.Side)) g.FillRectangle(b, area);
            using (var pen = new Pen(Theme.Hair)) g.DrawLine(pen, Theme.F(24), area.Y, Width - Theme.F(24), area.Y);
            var r = new RectangleF(Theme.F(12), area.Y + Theme.F(11), Width - Theme.F(24), Theme.F(40));
            bool on = On("settings"), hot = Hover("settings");
            if (on || hot) Theme.Fill(g, on ? Theme.Surface2 : Theme.Surface, r, Theme.F(10));
            float s = Theme.F(20);
            Icons.Draw(g, "settings", new RectangleF(r.X + Theme.F(14), r.Y + (r.Height - s) / 2, s, s), on ? Theme.AccentText : Theme.Sub);
            var scan = shell.Lib.Scan;
            string label = scan.Running ? "Scanning… " + scan.Seen.ToString("N0") : "Settings";
            Theme.Write(g, label, Theme.Font(14, FontStyle.Regular, on), scan.Running ? Theme.AccentText : on || hot ? Theme.Text : Theme.Sub, new RectangleF(r.X + Theme.F(46), r.Y, r.Width - Theme.F(56), r.Height));
            AddHit("settings", r, () => shell.Go(new Route("settings")), scan.Running ? "Looking through your music folders" : null, true);
        }

        void PlaylistMenu(Playlist p, Point pt)
        {
            var m = Menus.New();
            m.Items.Add(Menus.Item("Play", () => shell.Player.PlayList(p.Items)));
            m.Items.Add(Menus.Item("Shuffle", () => shell.Player.ShuffleAll(p.Items)));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Menus.Item("Rename…", () =>
            {
                var n = Ask.ForText(FindForm(), "Rename playlist", "New name", p.Name, "Rename");
                if (n != null) shell.Lib.RenamePlaylist(p, n);
            }));
            m.Items.Add(Menus.Item("Delete…", () =>
            {
                if (MessageBox.Show(FindForm(), "Delete the playlist \"" + p.Name + "\"? The songs stay in your library.", "Delete playlist", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    shell.Lib.DeletePlaylist(p);
                    if (shell.Current?.Kind == "playlist" && shell.Current.Id == p.Id) shell.Go(new Route("home"));
                }
            }));
            m.Show(pt);
        }
    }
}
