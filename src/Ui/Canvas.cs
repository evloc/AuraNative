using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Aura
{
    // Something on screen you can point at: a button, a row, a tile.
    sealed class Hit
    {
        public RectangleF R;
        public string Id;
        public bool Fixed;                       // drawn over the scrolling content (not scrolled)
        public Action Click, Double;
        public Action<MouseEventArgs> Down;
        public Action<Point> Right;              // screen point, for a menu
        public Action<PointF> DragTo;            // sliders / reordering: called while the mouse is held
        public Action DragEnd;
        public string Tip;
        public Cursor Cursor;
    }

    // A custom-drawn, smoothly scrolling surface. Pages and bars build on this.
    abstract class Canvas : Control
    {
        protected readonly List<Hit> hits = new List<Hit>();
        public float Offset { get; private set; }
        float target;
        protected float ContentHeight;
        protected string HoverId;
        protected Point MouseAt = new Point(-1, -1);
        Hit pressed;
        bool draggingThumb; float thumbGrab;
        readonly Timer smooth;
        readonly ToolTip tip = new ToolTip { InitialDelay = 500, ReshowDelay = 200, AutoPopDelay = 6000 };
        string tipShown;
        protected bool Scrollable = true;

        protected Canvas()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, true);
            DoubleBuffered = true;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            smooth = new Timer { Interval = 15 };
            smooth.Tick += (s, e) =>
            {
                float d = target - Offset;
                if (Math.Abs(d) < 0.6f) { Offset = target; smooth.Stop(); }
                else Offset += d * 0.32f;
                OnScrolled();
                Invalidate();
            };
        }

        protected override void Dispose(bool disposing) { if (disposing) { smooth.Dispose(); tip.Dispose(); } base.Dispose(disposing); }

        // content coordinates: y grows down from the top of the page
        protected abstract void Draw(Graphics g, RectangleF view);
        // drawn on top, in window coordinates (sticky headers and the like)
        protected virtual void DrawOver(Graphics g) { }
        protected virtual void OnScrolled() { }

        public float MaxOffset => Math.Max(0, ContentHeight - Height);

        public void ScrollTo(float y, bool animate = true)
        {
            target = Math.Max(0, Math.Min(MaxOffset, y));
            if (animate) smooth.Start(); else { Offset = target; smooth.Stop(); OnScrolled(); Invalidate(); }
        }

        public void EnsureVisible(float top, float bottom, float topMargin = 0)
        {
            if (top - topMargin < target) ScrollTo(top - topMargin);
            else if (bottom > target + Height) ScrollTo(bottom - Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            Theme.Setup(g);
            hits.Clear();
            if (Offset > MaxOffset && MaxOffset >= 0) { Offset = target = MaxOffset; }
            var st = g.Save();
            g.TranslateTransform(0, -Offset);
            try { Draw(g, new RectangleF(0, Offset, Width, Height)); }
            catch (Exception ex) { App.Log("draw " + GetType().Name + ": " + ex); }
            g.Restore(st);
            try { DrawOver(g); } catch (Exception ex) { App.Log("draw over " + GetType().Name + ": " + ex); }
            if (Scrollable && ContentHeight > Height + 1) DrawScrollbar(g);
        }

        RectangleF ThumbRect()
        {
            float track = Height - Theme.F(8);
            float h = Math.Max(Theme.F(36), track * Height / ContentHeight);
            float y = Theme.F(4) + (track - h) * (MaxOffset <= 0 ? 0 : Offset / MaxOffset);
            return new RectangleF(Width - Theme.F(10), y, Theme.F(6), h);
        }

        void DrawScrollbar(Graphics g)
        {
            var r = ThumbRect();
            bool hot = draggingThumb || (MouseAt.X >= Width - Theme.P(14) && MouseAt.X <= Width);
            Theme.Fill(g, Theme.Alpha(Color.White, hot ? 90 : 38), r, r.Width / 2);
        }

        // ---------- hits ----------
        protected internal Hit AddHit(string id, RectangleF r, Action click = null, string tip = null, bool fixedPos = false)
        {
            var h = new Hit { Id = id, R = r, Click = click, Tip = tip, Fixed = fixedPos, Cursor = click != null ? Cursors.Hand : null };
            hits.Add(h);
            return h;
        }

        protected bool Hover(string id) => HoverId == id;
        public bool HoverIdIs(string id) => HoverId == id;
        protected bool HoverStarts(string prefix) => HoverId != null && HoverId.StartsWith(prefix);

        protected Hit HitAt(Point p)
        {
            for (int i = hits.Count - 1; i >= 0; i--)
            {
                var h = hits[i];
                if (h.Fixed ? h.R.Contains(p) : h.R.Contains(p.X, p.Y + Offset)) return h;
            }
            return null;
        }

        protected PointF ToContent(Point p) => new PointF(p.X, p.Y + Offset);

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool nearBar = (MouseAt.X >= Width - Theme.P(14)) != (e.X >= Width - Theme.P(14));
            MouseAt = e.Location;
            if (draggingThumb)
            {
                var r = ThumbRect();
                float track = Height - Theme.F(8) - r.Height;
                if (track > 0) ScrollTo((e.Y - thumbGrab - Theme.F(4)) / track * MaxOffset, false);
                return;
            }
            if (pressed?.DragTo != null && e.Button == MouseButtons.Left) { pressed.DragTo(pressed.Fixed ? (PointF)e.Location : ToContent(e.Location)); return; }
            var h = HitAt(e.Location);
            string id = h?.Id;
            if (id != HoverId) { HoverId = id; Invalidate(); }
            else if (nearBar) Invalidate();
            Cursor = h?.Cursor ?? Cursors.Default;
            var t = h?.Tip;
            if (t != tipShown) { tipShown = t; tip.SetToolTip(this, t); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            MouseAt = new Point(-1, -1);
            if (HoverId != null) { HoverId = null; }
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!(this is IStayUnfocused)) Focus();
            if (e.Button == MouseButtons.Left && Scrollable && ContentHeight > Height && e.X >= Width - Theme.P(14))
            {
                var r = ThumbRect();
                if (e.Y >= r.Top && e.Y <= r.Bottom) thumbGrab = e.Y - r.Top;
                else { thumbGrab = r.Height / 2; }
                draggingThumb = true; Capture = true;
                OnMouseMove(e);
                return;
            }
            var h = HitAt(e.Location);
            if (e.Button == MouseButtons.Left)
            {
                pressed = h;
                h?.Down?.Invoke(e);
                if (h?.DragTo != null) { Capture = true; h.DragTo(h.Fixed ? (PointF)e.Location : ToContent(e.Location)); }
            }
            else if (e.Button == MouseButtons.Right)
            {
                h?.Down?.Invoke(e);
                if (h?.Right != null) h.Right(PointToScreen(e.Location));
                else OnEmptyRightClick(PointToScreen(e.Location));
            }
            else if (e.Button == MouseButtons.XButton1) OnBackButton();
            else if (e.Button == MouseButtons.XButton2) OnForwardButton();
        }

        protected virtual void OnEmptyRightClick(Point screen) { }
        protected virtual void OnBackButton() { }
        protected virtual void OnForwardButton() { }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (draggingThumb) { draggingThumb = false; Capture = false; Invalidate(); return; }
            var p = pressed; pressed = null;
            if (p == null || e.Button != MouseButtons.Left) return;
            Capture = false;
            if (p.DragTo != null) { p.DragEnd?.Invoke(); Invalidate(); return; }
            var h = HitAt(e.Location);
            if (h != null && h.Id == p.Id) h.Click?.Invoke();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left) HitAt(e.Location)?.Double?.Invoke();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!Scrollable) return;
            ScrollTo(target - e.Delta / 120f * Theme.F(130));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (target > MaxOffset) ScrollTo(MaxOffset, false);
        }

        protected override bool IsInputKey(Keys k)
        {
            switch (k & Keys.KeyCode)
            {
                case Keys.Up: case Keys.Down: case Keys.Left: case Keys.Right: case Keys.PageUp: case Keys.PageDown: case Keys.Home: case Keys.End: return true;
            }
            return base.IsInputKey(k);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Handled || !Scrollable) return;
            switch (e.KeyCode)
            {
                case Keys.PageDown: ScrollTo(target + Height * 0.85f); e.Handled = true; break;
                case Keys.PageUp: ScrollTo(target - Height * 0.85f); e.Handled = true; break;
                case Keys.Home: ScrollTo(0); e.Handled = true; break;
                case Keys.End: ScrollTo(MaxOffset); e.Handled = true; break;
            }
        }

        // ---------- common pieces ----------
        // a rounded button with text (and an optional icon); primary = filled with the accent color
        protected float PillWidth(Graphics g, string text, string icon = null)
        {
            var f = Theme.Font(13.5f, FontStyle.Regular, true);
            float pad = Theme.F(18), ic = icon != null ? Theme.F(18) + Theme.F(8) : 0;
            return Theme.Measure(g, text, f) + pad * 2 + ic + Theme.F(4);
        }

        protected RectangleF Pill(Graphics g, string id, float x, float y, string text, Action click, bool primary = false, string icon = null, float h = 36, string tip = null, bool fixedPos = false)
        {
            var f = Theme.Font(13.5f, FontStyle.Regular, true);
            float pad = Theme.F(18), ic = icon != null ? Theme.F(18) + Theme.F(8) : 0;
            float w = Theme.Measure(g, text, f) + pad * 2 + ic + Theme.F(4);
            if (string.IsNullOrEmpty(text)) w = Theme.F(h);
            var r = new RectangleF(x, y, w, Theme.F(h));
            bool hot = Hover(id);
            var bg = primary ? (hot ? Theme.Mix(Theme.Accent, Color.White, 0.12f) : Theme.Accent) : (hot ? Theme.Surface2 : Theme.Surface);
            Theme.Fill(g, bg, r, r.Height / 2);
            if (!primary) Theme.Stroke(g, Theme.Alpha(Color.White, hot ? 40 : 22), r, r.Height / 2);
            var fg = primary ? Theme.OnAccent : Theme.Text;
            if (icon != null)
            {
                float s = Theme.F(18);
                float ix = string.IsNullOrEmpty(text) ? r.X + (r.Width - s) / 2 : r.X + pad;
                Icons.Draw(g, icon, new RectangleF(ix, r.Y + (r.Height - s) / 2, s, s), fg, true);
            }
            if (!string.IsNullOrEmpty(text)) Theme.Write(g, text, f, fg, new RectangleF(r.X + pad + ic, r.Y, r.Width - pad * 2 - ic + Theme.F(4), r.Height));
            AddHit(id, r, click, tip, fixedPos);
            return r;
        }

        // a round icon button
        protected RectangleF IconButton(Graphics g, string id, RectangleF r, string icon, Action click, string tip = null, bool active = false, bool fixedPos = false, bool filled = false, bool circleBg = false, bool dot = true)
        {
            bool hot = Hover(id);
            if (circleBg || hot) Theme.Fill(g, hot ? Theme.Surface2 : Theme.Surface, r, r.Width / 2);
            float s = Math.Min(r.Width, r.Height) * 0.56f;
            var c = active ? Theme.AccentText : hot ? Theme.Text : Theme.Sub;
            Icons.Draw(g, icon, new RectangleF(r.X + (r.Width - s) / 2, r.Y + (r.Height - s) / 2, s, s), c, filled || active);
            if (active && dot)
            {
                float d = Theme.F(4);
                Theme.Fill(g, Theme.AccentText, new RectangleF(r.X + (r.Width - d) / 2, r.Bottom - d * 0.6f, d, d), d / 2);
            }
            AddHit(id, r, click, tip, fixedPos);
            return r;
        }

        // the big round accent play button
        protected RectangleF PlayButton(Graphics g, string id, RectangleF r, bool playing, Action click, string tip = "Play")
        {
            bool hot = Hover(id);
            var rr = hot ? RectangleF.Inflate(r, Theme.F(1.5f), Theme.F(1.5f)) : r;
            using (var p = new System.Drawing.Drawing2D.GraphicsPath())
            {
                p.AddEllipse(rr);
                using (var b = new SolidBrush(hot ? Theme.Mix(Theme.Accent, Color.White, 0.12f) : Theme.Accent)) g.FillPath(b, p);
            }
            float s = r.Width * 0.46f;
            Icons.Draw(g, playing ? "pause" : "play", new RectangleF(r.X + (r.Width - s) / 2 + (playing ? 0 : s * 0.06f), r.Y + (r.Height - s) / 2, s, s), Theme.OnAccent, true);
            AddHit(id, r, click, tip);
            return r;
        }
    }

    interface IStayUnfocused { }
}
