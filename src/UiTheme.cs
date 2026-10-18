using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DshBatchRename
{
    // 浅色 / 深色主题：跟随系统「应用」模式，随时可重载。
    internal class Theme
    {
        public bool Dark;
        public Color Card;
        public Color Line;
        public Color LineSoft;
        public Color Text;
        public Color Sub;
        public Color Accent;
        public Color AccentOn;
        public Color AccentSoft;
        public Color Input;
        public Color InputLine;
        public Color ChipBg;
        public Color ChipText;
        public Color Danger;
        public Color RowAlt;
        public Color Hover;
        public Color Sel;
        public Color LinkText;

        private static Theme _current;

        public static Theme Current
        {
            get
            {
                if (_current == null) _current = Load();
                return _current;
            }
        }

        public static bool SystemDark()
        {
            try
            {
                object v = Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme", 1);
                if (v == null) return false;
                return Convert.ToInt32(v) == 0;
            }
            catch (Exception) { return false; }
        }

        public static Theme Load()
        {
            // 便于离线截图核对两种外观：DSHBR_THEME=dark / light
            try
            {
                string forced = Dev.Env("DSHBR_THEME");
                if (!string.IsNullOrEmpty(forced))
                {
                    forced = forced.Trim().ToLowerInvariant();
                    if (forced == "dark") return CreateDark();
                    if (forced == "light") return CreateLight();
                }
            }
            catch (Exception) { }
            return SystemDark() ? CreateDark() : CreateLight();
        }

        public static void Reload()
        {
            _current = Load();
        }

        public static Theme CreateLight()
        {
            Theme t = new Theme();
            t.Dark = false;
            t.Card = Color.FromArgb(0xFF, 0xFF, 0xFF);
            t.Line = Color.FromArgb(0xE8, 0xE8, 0xE8);
            t.LineSoft = Color.FromArgb(0xF0, 0xF0, 0xF0);
            t.Text = Color.FromArgb(0x1B, 0x1B, 0x1B);
            t.Sub = Color.FromArgb(0x6E, 0x6E, 0x6E);
            t.Accent = Color.FromArgb(0x00, 0x67, 0xC0);
            t.AccentOn = Color.White;
            t.AccentSoft = Color.FromArgb(0xEA, 0xF2, 0xFB);
            t.Input = Color.FromArgb(0xFA, 0xFA, 0xFA);
            t.InputLine = Color.FromArgb(0xDE, 0xDE, 0xDE);
            t.ChipBg = Color.FromArgb(0xF2, 0xF2, 0xF2);
            t.ChipText = Color.FromArgb(0x1B, 0x1B, 0x1B);
            t.Danger = Color.FromArgb(0xC4, 0x2B, 0x1C);
            t.RowAlt = Color.FromArgb(0xF6, 0xF7, 0xF9);
            t.Hover = Color.FromArgb(0xEE, 0xF4, 0xFB);
            t.Sel = Color.FromArgb(0xDC, 0xEB, 0xFA);
            t.LinkText = Color.FromArgb(0x00, 0x67, 0xC0);
            return t;
        }

        public static Theme CreateDark()
        {
            Theme t = new Theme();
            t.Dark = true;
            t.Card = Color.FromArgb(0x28, 0x28, 0x28);
            t.Line = Color.FromArgb(0x3B, 0x3B, 0x3B);
            t.LineSoft = Color.FromArgb(0x33, 0x33, 0x33);
            t.Text = Color.FromArgb(0xF2, 0xF2, 0xF2);
            t.Sub = Color.FromArgb(0x9E, 0x9E, 0x9E);
            t.Accent = Color.FromArgb(0x4C, 0xC2, 0xFF);
            t.AccentOn = Color.FromArgb(0x0B, 0x22, 0x30);
            t.AccentSoft = Color.FromArgb(0x25, 0x38, 0x45);
            t.Input = Color.FromArgb(0x33, 0x33, 0x33);
            t.InputLine = Color.FromArgb(0x48, 0x48, 0x48);
            t.ChipBg = Color.FromArgb(0x34, 0x34, 0x34);
            t.ChipText = Color.FromArgb(0xF2, 0xF2, 0xF2);
            t.Danger = Color.FromArgb(0xFF, 0x99, 0xA4);
            t.RowAlt = Color.FromArgb(0x2C, 0x2C, 0x2C);
            t.Hover = Color.FromArgb(0x33, 0x33, 0x33);
            t.Sel = Color.FromArgb(0x1E, 0x3A, 0x4C);
            t.LinkText = Color.FromArgb(0x4C, 0xC2, 0xFF);
            return t;
        }
    }

    // 字体缓存（避免每帧 new Font）
    internal static class F
    {
        private static readonly Dictionary<string, Font> Cache = new Dictionary<string, Font>();

        public static Font Get(float pt, bool bold)
        {
            return Get(pt, bold ? FontStyle.Bold : FontStyle.Regular);
        }

        public static Font Get(float pt, FontStyle style)
        {
            string key = pt.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "/" + (int)style;
            Font f;
            if (Cache.TryGetValue(key, out f) && f != null) return f;
            try { f = new Font("Microsoft YaHei UI", pt, style, GraphicsUnit.Point); }
            catch (Exception) { f = new Font(FontFamily.GenericSansSerif, pt, style, GraphicsUnit.Point); }
            Cache[key] = f;
            return f;
        }
    }

    // 绘图原语
    internal static class D
    {
        public const TextFormatFlags Left = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        public const TextFormatFlags LeftEll = Left | TextFormatFlags.EndEllipsis;
        public const TextFormatFlags Center = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        public const TextFormatFlags Right = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            if (radius <= 0.5f) { p.AddRectangle(r); return p; }
            float d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Fill(Graphics g, RectangleF r, float radius, Color c)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (GraphicsPath p = Round(r, radius))
            using (SolidBrush b = new SolidBrush(c))
                g.FillPath(b, p);
        }

        public static void Stroke(Graphics g, RectangleF r, float radius, Color c, float w)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (GraphicsPath p = Round(r, radius))
            using (Pen pen = new Pen(c, w))
                g.DrawPath(pen, p);
        }

        public static void HLine(Graphics g, float x1, float x2, float y, Color c)
        {
            using (Pen pen = new Pen(c, 1f))
            {
                pen.Alignment = PenAlignment.Inset;
                g.DrawLine(pen, x1, y, x2, y);
            }
        }

        public static void Txt(Graphics g, string s, Font f, Color c, Rectangle r, TextFormatFlags flags)
        {
            if (s == null) s = string.Empty;
            TextRenderer.DrawText(g, s, f, r, c, flags);
        }

        public static Size Measure(string s, Font f)
        {
            return TextRenderer.MeasureText(s == null ? string.Empty : s, f, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }

        public static Color Mix(Color a, Color b, double t)
        {
            if (t < 0) t = 0; if (t > 1) t = 1;
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }
    }

    // 无系统边框的圆角窗口：自绘标题区、可拖动、可从四边缩放
    internal class ShellForm : Form
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public ShellForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Current.Card;
            ShowInTaskbar = true;
            KeyPreview = true;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int round = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(Handle, 33, ref round, 4);
                int dark = Theme.Current.Dark ? 1 : 0;
                DwmSetWindowAttribute(Handle, 20, ref dark, 4);
            }
            catch (Exception) { }
        }

        // WM_NCHITTEST: 四边/四角可缩放，标题区可拖动
        private const int WM_NCHITTEST = 0x84;
        private const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14,
            HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17, HTCAPTION = 2, HTCLIENT = 1;
        private const int ResizeBorder = 6;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST && FormBorderStyle == FormBorderStyle.None)
            {
                Point p = PointToClient(new Point(m.LParam.ToInt32()));
                bool left = p.X <= ResizeBorder;
                bool right = p.X >= ClientSize.Width - ResizeBorder;
                bool top = p.Y <= ResizeBorder;
                bool bottom = p.Y >= ClientSize.Height - ResizeBorder;
                if (left && top) { m.Result = (IntPtr)HTTOPLEFT; return; }
                if (right && top) { m.Result = (IntPtr)HTTOPRIGHT; return; }
                if (left && bottom) { m.Result = (IntPtr)HTBOTTOMLEFT; return; }
                if (right && bottom) { m.Result = (IntPtr)HTBOTTOMRIGHT; return; }
                if (left) { m.Result = (IntPtr)HTLEFT; return; }
                if (right) { m.Result = (IntPtr)HTRIGHT; return; }
                if (top) { m.Result = (IntPtr)HTTOP; return; }
                if (bottom) { m.Result = (IntPtr)HTBOTTOM; return; }
                if (IsDragArea(p)) { m.Result = (IntPtr)HTCAPTION; return; }
                m.Result = (IntPtr)HTCLIENT;
                return;
            }
            base.WndProc(ref m);
        }

        protected virtual bool IsDragArea(Point p)
        {
            return p.Y < 56 && p.X < ClientSize.Width - 120;
        }
    }

    // 自绘下拉浮层（替代系统 ComboBox，保持一致的视觉）
    internal class Pop : Form
    {
        private readonly string[] _items;
        private readonly Theme _t;
        private int _hot = -1;
        private int _sel;
        private const int ItemH = 32;
        private const int PadY = 6;

        public event EventHandler Picked;

        public int SelectedIndex
        {
            get { return _sel; }
            set { _sel = value; }
        }

        public Pop(string[] items, int selected)
        {
            _items = items;
            _sel = selected;
            _t = Theme.Current;
            FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = _t.Card;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            ClientSize = new Size(200, _items.Length * ItemH + PadY * 2);
        }

        public void ShowAt(Rectangle anchorScreen)
        {
            Width = anchorScreen.Width;
            Height = _items.Length * ItemH + PadY * 2;
            int x = anchorScreen.X;
            int y = anchorScreen.Bottom + 4;
            Rectangle wa = Screen.FromPoint(new Point(x, y)).WorkingArea;
            if (y + Height > wa.Bottom) y = anchorScreen.Top - Height - 4;
            if (x + Width > wa.Right) x = wa.Right - Width;
            if (x < wa.Left) x = wa.Left;
            Location = new Point(x, y);
            Show();
            Activate();
        }

        protected override bool ShowWithoutActivation
        {
            get { return false; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF all = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            D.Fill(g, all, 8, _t.Card);
            D.Stroke(g, all, 8, _t.Line, 1f);
            for (int i = 0; i < _items.Length; i++)
            {
                RectangleF r = new RectangleF(4, PadY + i * ItemH, Width - 8, ItemH);
                if (i == _sel) D.Fill(g, r, 6, _t.Sel);
                else if (i == _hot) D.Fill(g, r, 6, _t.Hover);
                Color c = (i == _sel) ? _t.Accent : _t.Text;
                D.Txt(g, _items[i], F.Get(9.2f, i == _sel), c, Rectangle.Round(new RectangleF(12, r.Y, r.Width - 20, r.Height)), D.LeftEll);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = (e.Y - PadY) / ItemH;
            if (i < 0 || i >= _items.Length) i = -1;
            if (i != _hot) { _hot = i; Invalidate(); }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            int i = (e.Y - PadY) / ItemH;
            if (i >= 0 && i < _items.Length)
            {
                _sel = i;
                if (Picked != null) Picked(this, EventArgs.Empty);
            }
            Close();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            Close();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            if (keyData == Keys.Enter)
            {
                if (Picked != null) Picked(this, EventArgs.Empty);
                Close();
                return true;
            }
            if (keyData == Keys.Down || keyData == Keys.Up)
            {
                _sel += (keyData == Keys.Down) ? 1 : -1;
                if (_sel < 0) _sel = 0;
                if (_sel >= _items.Length) _sel = _items.Length - 1;
                _hot = _sel;
                Invalidate();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
