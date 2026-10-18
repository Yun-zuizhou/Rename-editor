using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace DshBatchRename
{
    // 主窗口：自绘 Fluent 风格（跟随系统深浅色）
    internal class RenameForm : ShellForm
    {
        private const int ModeSequence = 0, ModeReplace = 1, ModeAffix = 2, ModeCase = 3, ModeFree = 4;

        private readonly List<RenameItem> _items;
        private readonly Rule _rule;
        private readonly Theme _t;


        private bool _loading;
        private Timer _debounce;

        private int _mode;
        private int _hot;
        private int _scroll;
        private bool _pressed;
        private int _pressedId;

        private TextBox _tpl, _find, _repl, _prefix, _suffix, _numStart, _numStep, _numPad;
        private TextBox _focusBox;

        private readonly HashSet<RenameItem> _sel = new HashSet<RenameItem>();
        private int _selAnchor = -1;

        // 命中区域
        private Rectangle _rcClose, _rcUndo, _rcCancel, _rcApply, _rcSort, _rcCase, _rcRegex, _rcCaseSens;
        private readonly Rectangle[] _rcChips = new Rectangle[5];
        private Rectangle _rcList, _rcListHeader;
        private Rectangle _rcTplWell, _rcFindWell, _rcReplWell, _rcPrefixWell, _rcSuffixWell;
        private Rectangle _rcPreset;
        private Rectangle[] _rcVars = new Rectangle[4];
        private int _rcVarHintRight;
        private Rectangle _rcStartWell, _rcStepWell, _rcPadWell;
        private int _rowH, _listTop, _headerH;

        // 行内改名：双击某一行就在「新名称」列上盖一个输入框
        private TextBox _edit;
        private int _editIndex = -1;

        private static readonly string[] ModeNames = new string[] { "按序号", "查找替换", "加前缀后缀", "大小写清理", "自由命名" };
        private static readonly string[] SortNames = new string[] { "名称（自然顺序）", "修改时间（旧→新）", "大小（小→大）", "保持选中顺序" };
        private static readonly string[] CaseNames = new string[] { "全部小写", "全部大写", "每词首字母大写", "清理非法字符", "空格转下划线" };

        // 模板变量芯片（点一下就插到光标处，不用记语法）
        private static readonly string[] VarTokens = new string[] { "{name}", "{n}", "{ext}", "{date}" };

        private static readonly string[] PresetNames = new string[] { "{name}_{n}", "{name}-{n}", "{n}_{name}", "{date}_{name}", "{name}_{date}_{n}", "{parent}_{name}" };

        // 命中 id
        private const int IdNone = 0, IdClose = 1, IdChip = 10, IdSort = 20, IdCase = 21,
            IdUndo = 30, IdCancel = 31, IdApply = 32, IdRegex = 33, IdCaseSens = 34, IdList = 40, IdRow = 200,
            IdVar = 50, IdPreset = 60;

        public RenameForm(string[] files, Rule rule)
        {
            _t = Theme.Current;

            _items = Engine.Load(files);
            _rule = rule == null ? Rule.Load() : rule;
            _mode = Dev.ShotMode();
            if (_mode < 0 && _rule.Mode == RenameMode.Affix) _mode = ModeAffix;   // 用户默认规则：加前缀后缀
            else if (_mode < 0 && _rule.Mode == RenameMode.Replace) _mode = ModeReplace;
            else if (_mode < 0 && _rule.Mode == RenameMode.Case) _mode = ModeCase;
            else if (_mode < 0) _mode = ModeSequence;

            Text = "批量重命名";
            Font = F.Get(9f, false);
            BackColor = _t.Card;
            ForeColor = _t.Text;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(U(900), U(580));
            MinimumSize = new Size(U(720), U(430));
            AllowDrop = true;
            KeyPreview = true;

            BuildBoxes();
            ApplyModeToRule();
            LoadRuleToBoxes();

            // 仅供离线截图核对：预填一个前缀，验证「输入框能显示文字」
            string shotPrefix = Dev.ShotPrefix();
            if (!string.IsNullOrEmpty(shotPrefix)) _prefix.Text = shotPrefix;

            string shotManual = Dev.ShotManual();
            if (!string.IsNullOrEmpty(shotManual) && _items.Count > 0)
            {
                _items[0].Manual = true;
                _items[0].ManualName = shotManual;
            }

            _debounce = new Timer();
            _debounce.Interval = 180;
            _debounce.Tick += delegate
            {
                _debounce.Stop();
                RefreshPreview();
            };

            DragEnter += delegate(object s, DragEventArgs e)
            {
                if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
                else e.Effect = DragDropEffects.None;
            };
            DragDrop += delegate(object s, DragEventArgs e)
            {
                if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
                string[] dropped = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (dropped != null) AddPaths(dropped);
            };

            MouseMove += OnMouseMove;
            MouseDown += OnMouseDown;
            MouseUp += OnMouseUp;
            MouseWheel += OnWheel;
            MouseDoubleClick += OnDoubleClick;
            MouseLeave += delegate { if (_hot != 0) { _hot = 0; Invalidate(); } };

            RefreshPreview();
        }


        // DPI：.NET 4.x 的 DeviceDpi 在部分环境下恒为 96，这里直接问系统
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        private float _scale = 1f;

        private void RefreshScale()
        {
            float s = 1f;
            try
            {
                if (Handle != IntPtr.Zero)
                {
                    uint dpi = GetDpiForWindow(Handle);
                    if (dpi >= 72 && dpi <= 480) s = dpi / 96f;
                }
                if (s <= 1.001f && DeviceDpi > 96) s = DeviceDpi / 96f;
            }
            catch (Exception) { }
            _scale = s;
        }

        private int U(float v)
        {
            return (int)Math.Round(v * _scale);
        }

        // ---------------------------------------------------------------- 子控件
        private TextBox MkBox(float pt)
        {
            TextBox b = new TextBox();
            b.BorderStyle = BorderStyle.None;
            b.Font = F.Get(pt, false);
            b.ForeColor = _t.Text;
            b.BackColor = _t.Input;
            b.GotFocus += delegate { _focusBox = b; Invalidate(); };
            b.LostFocus += delegate { if (_focusBox == b) _focusBox = null; Invalidate(); };
            b.TextChanged += delegate { OnUiChanged(); };
            b.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; OnApply(); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; OnCancel(); }
            };
            Controls.Add(b);
            b.Visible = false;
            return b;
        }

        private void BuildBoxes()
        {
            _tpl = MkBox(11.5f);
            _find = MkBox(9.6f);
            _repl = MkBox(9.6f);
            _prefix = MkBox(9.6f);
            _suffix = MkBox(9.6f);
            _numStart = MkBox(9.6f);
            _numStep = MkBox(9.6f);
            _numPad = MkBox(9.6f);
            foreach (TextBox b in new TextBox[] { _numStart, _numStep, _numPad })
            {
                b.KeyPress += delegate(object s, KeyPressEventArgs e)
                {
                    if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true;
                };
                b.TextAlign = HorizontalAlignment.Center;
            }
        }

        private void OnUiChanged()
        {
            if (_loading) return;
            if (_debounce != null) { _debounce.Stop(); _debounce.Start(); }
        }

        // ---------------------------------------------------------------- 布局
        private void LayoutAll()
        {
            int pad = U(24);
            int w = ClientSize.Width;
            _headerH = U(58);

            // 右上角关闭
            int bs = U(30);
            _rcClose = new Rectangle(w - pad - bs + U(6), U(14), bs, bs);

            // 规则分段
            int chipY = _headerH + U(14);
            int chipH = U(32);
            int[] cw = new int[] { U(74), U(88), U(104), U(96), U(88) };
            int cx = pad;
            for (int i = 0; i < cw.Length; i++)
            {
                _rcChips[i] = new Rectangle(cx, chipY, cw[i], chipH);
                cx += cw[i] + U(6);
            }

            // 第 1 行（主输入）
            int r1 = chipY + chipH + U(18);
            int r1h = U(40);
            int right = w - pad;

            // 排序框（所有模式共用）
            int sortW = U(198);
            _rcSort = new Rectangle(right - sortW, r1, sortW, r1h);

            _rcTplWell = _rcFindWell = _rcReplWell = _rcPrefixWell = _rcSuffixWell = Rectangle.Empty;
            int leftFieldW = _rcSort.X - U(64) - (pad + U(76));

            if (_mode == ModeSequence)
            {
                _rcTplWell = new Rectangle(pad + U(76), r1, Math.Max(U(240), leftFieldW), r1h);
            }
            else if (_mode == ModeReplace || _mode == ModeAffix)
            {
                int gap = U(80);
                int avail = right - (pad + U(76));
                int fw = (avail - gap) / 2;
                _rcFindWell = new Rectangle(pad + U(76), r1, fw, r1h);
                _rcReplWell = new Rectangle(_rcFindWell.Right + gap, r1, fw, r1h);
                _rcPrefixWell = _rcFindWell;
                _rcSuffixWell = _rcReplWell;
            }
            else if (_mode == ModeCase)
            {
                _rcCase = new Rectangle(pad + U(76), r1, U(200), r1h);
            }

            // 第 2 行（参数 / 开关）
            int r2 = r1 + r1h + U(14);
            int r2h = U(32);
            _rcStartWell = _rcStepWell = _rcPadWell = Rectangle.Empty;
            _rcRegex = _rcCaseSens = Rectangle.Empty;
            if (_mode == ModeSequence)
            {
                int nw = U(58);
                int x = pad + U(76);
                _rcStartWell = new Rectangle(x, r2, nw, r2h);
                x = _rcStartWell.Right + U(58);
                _rcStepWell = new Rectangle(x, r2, nw, r2h);
                x = _rcStepWell.Right + U(58);
                _rcPadWell = new Rectangle(x, r2, nw, r2h);

                // 右侧：模板预设 + 变量芯片（右对齐）
                int[] vw = new int[] { U(64), U(40), U(52), U(56) };
                int total = U(56);
                for (int i = 0; i < 4; i++) total += vw[i] + U(6);
                int vx = right - total;
                _rcPreset = new Rectangle(vx, r2, U(56), r2h);
                vx = _rcPreset.Right + U(6);
                for (int i = 0; i < 4; i++)
                {
                    _rcVars[i] = new Rectangle(vx, r2, vw[i], r2h);
                    vx += vw[i] + U(6);
                }
                _rcVarHintRight = _rcPreset.X - U(10);
            }
            else if (_mode == ModeReplace)
            {
                _rcCaseSens = new Rectangle(pad + U(76), r2, U(112), r2h);
                _rcRegex = new Rectangle(_rcCaseSens.Right + U(8), r2, U(106), r2h);
            }

            // 列表面板
            _rowH = U(34);
            // 自由命名模式没有规则字段，列表直接顶上去，别留一大片空白
            int headerY = _mode == ModeFree ? r1 + U(52) : r2 + r2h + U(30);
            _rcListHeader = new Rectangle(0, headerY, w, U(28));
            _listTop = _rcListHeader.Bottom;
            int bottomH = U(70);
            _rcList = new Rectangle(0, _listTop, w, Math.Max(U(60), ClientSize.Height - bottomH - _listTop));
            _headerH = _rcListHeader.Y;

            // 底部
            int by = ClientSize.Height - U(54);
            _rcUndo = new Rectangle(pad, by, U(120), U(36));
            _rcApply = new Rectangle(right - U(160), by, U(160), U(36));
            _rcCancel = new Rectangle(_rcApply.X - U(8) - U(92), by, U(92), U(36));

            PlaceBoxes();

            // 加前缀/后缀与查找替换共用同一块矩形。必须放在 PlaceBoxes() 之后，
            // 否则 PlaceOne 会把 Visible 重新设回 true，空输入框会盖住有字的那个。
            if (_mode != ModeReplace) { _find.Visible = false; _repl.Visible = false; }
            if (_mode != ModeAffix) { _prefix.Visible = false; _suffix.Visible = false; }

            // 布局变了，行内输入框跟着收起来，免得浮在错的位置
            if (_editIndex >= 0) { _editIndex = -1; if (_edit != null) _edit.Visible = false; }
        }

        private void PlaceBoxes(Rectangle tpl, Rectangle find, Rectangle repl, Rectangle prefix, Rectangle suffix,
            Rectangle ns, Rectangle nst, Rectangle npad)
        {
            PlaceOne(_tpl, tpl);
            PlaceOne(_find, find);
            PlaceOne(_repl, repl);
            PlaceOne(_prefix, prefix);
            PlaceOne(_suffix, suffix);
            PlaceOne(_numStart, ns);
            PlaceOne(_numStep, nst);
            PlaceOne(_numPad, npad);
        }

        private void PlaceBoxes()
        {
            PlaceBoxes(_rcTplWell, _rcFindWell, _rcReplWell, _rcPrefixWell, _rcSuffixWell,
                _rcStartWell, _rcStepWell, _rcPadWell);
        }

        private void PlaceOne(TextBox b, Rectangle well)
        {
            if (well.IsEmpty)
            {
                b.Visible = false;
                b.Bounds = Rectangle.Empty;
                return;
            }
            int inset = U(12);
            int h = b.PreferredHeight;
            b.SetBounds(well.X + inset, well.Y + (well.Height - h) / 2, Math.Max(U(20), well.Width - inset * 2), h);
            b.Visible = true;
        }

        // ---------------------------------------------------------------- 规则 <-> 界面
        private void ApplyModeToRule()
        {
            _rule.Mode = _mode == ModeSequence ? RenameMode.Sequence
                : _mode == ModeReplace ? RenameMode.Replace
                : _mode == ModeAffix ? RenameMode.Affix
                : _mode == ModeFree ? RenameMode.Free
                : RenameMode.Case;
        }

        private void SaveRuleFromUi()
        {
            _rule.Mode = (RenameMode)(_mode == ModeSequence ? 0 : _mode == ModeReplace ? 1 : _mode == ModeAffix ? 2 : _mode == ModeFree ? 4 : 3);
            _rule.Template = _tpl.Text;
            _rule.Find = _find.Text;
            _rule.ReplaceWith = _repl.Text;
            _rule.Prefix = _prefix.Text;
            _rule.Suffix = _suffix.Text;
            int v;
            if (int.TryParse(_numStart.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) _rule.Start = v;
            if (int.TryParse(_numStep.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) _rule.Step = v < 1 ? 1 : v;
            if (int.TryParse(_numPad.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) _rule.Pad = v;
        }

        private void LoadRuleToBoxes()
        {
            _loading = true;
            try
            {
                _tpl.Text = _rule.Template ?? string.Empty;
                _find.Text = _rule.Find ?? string.Empty;
                _repl.Text = _rule.ReplaceWith ?? string.Empty;
                _prefix.Text = _rule.Prefix ?? string.Empty;
                _suffix.Text = _rule.Suffix ?? string.Empty;
                _numStart.Text = _rule.Start.ToString(CultureInfo.InvariantCulture);
                _numStep.Text = _rule.Step.ToString(CultureInfo.InvariantCulture);
                _numPad.Text = _rule.Pad.ToString(CultureInfo.InvariantCulture);
            }
            finally { _loading = false; }
        }

        // ---------------------------------------------------------------- 预览
        private void RefreshPreview()
        {
            if (_loading) return;
            SaveRuleFromUi();
            Engine.Build(_items, _rule);
            int will = 0;
            foreach (RenameItem it in _items) if (it.Runnable) will++;
            Invalidate();
        }

        private int CountWill()
        {
            int n = 0;
            foreach (RenameItem it in _items) if (it.Runnable) n++;
            return n;
        }

        private int CountBlocked()
        {
            int n = 0;
            foreach (RenameItem it in _items)
                if (!it.Runnable && !it.Excluded && it.Status != "未变化") n++;
            return n;
        }

        private string HeaderSub()
        {
            int total = _items.Count;
            if (total == 0) return "把文件或文件夹拖进这个窗口";
            int will = CountWill();
            string s = total.ToString(CultureInfo.InvariantCulture) + " 个文件 · 将重命名 "
                + will.ToString(CultureInfo.InvariantCulture) + " 项";
            int blocked = CountBlocked();
            if (blocked > 0) s += " · " + blocked.ToString(CultureInfo.InvariantCulture) + " 项有问题";
            return s;
        }

        private RenameItem FirstItem()
        {
            foreach (RenameItem it in _items) if (it.Runnable) return it;
            return _items.Count > 0 ? _items[0] : null;
        }

        // ---------------------------------------------------------------- 绘制
        protected override void OnPaint(PaintEventArgs e)
        {
            LayoutAll();
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (SolidBrush bg = new SolidBrush(_t.Card)) g.FillRectangle(bg, ClientRectangle);

            DrawHeader(g);
            DrawChips(g);
            DrawRuleRow1(g);
            DrawRuleRow2(g);
            DrawExample(g);
            DrawList(g);
            DrawFooter(g);

            if (_focusBox != null)
            {
                Rectangle well = WellOfBox(_focusBox);
                if (!well.IsEmpty) D.Stroke(g, new RectangleF(well.X + 0.5f, well.Y + 0.5f, well.Width - 1, well.Height - 1), U(8), _t.Accent, 2f);
            }
        }

        private Rectangle WellOfBox(TextBox b)
        {
            if (b == _tpl) return _rcTplWell;
            if (b == _find) return _rcFindWell;
            if (b == _repl) return _rcReplWell;
            if (b == _prefix) return _rcPrefixWell;
            if (b == _suffix) return _rcSuffixWell;
            if (b == _numStart) return _rcStartWell;
            if (b == _numStep) return _rcStepWell;
            if (b == _numPad) return _rcPadWell;
            return Rectangle.Empty;
        }

        private void DrawHeader(Graphics g)
        {
            int pad = U(24);
            Size title = D.Measure("批量重命名", F.Get(13.5f, true));
            D.Txt(g, "批量重命名", F.Get(13.5f, true), _t.Text,
                new Rectangle(pad, U(12), title.Width + U(8), U(34)), D.Left);
            D.Txt(g, HeaderSub(), F.Get(9f, false), _t.Sub,
                new Rectangle(pad + title.Width + U(14), U(12), ClientSize.Width - pad * 2 - title.Width - U(60), U(34)), D.LeftEll);

            // 关闭按钮
            bool hot = _hot == IdClose;
            if (hot) D.Fill(g, _rcClose, U(6), _t.Dark ? Color.FromArgb(0x3A, 0x3A, 0x3A) : Color.FromArgb(0xF0, 0xF0, 0xF0));
            using (Pen p = new Pen(hot ? _t.Text : _t.Sub, 1.4f))
            {
                int m = U(10);
                g.DrawLine(p, _rcClose.X + m, _rcClose.Y + m, _rcClose.Right - m, _rcClose.Bottom - m);
                g.DrawLine(p, _rcClose.Right - m, _rcClose.Y + m, _rcClose.X + m, _rcClose.Bottom - m);
            }
        }

        private void DrawChips(Graphics g)
        {
            for (int i = 0; i < _rcChips.Length; i++)
            {
                bool on = _mode == i;
                bool hot = _hot == IdChip + i;
                Color bg = on ? _t.Accent : (hot ? D.Mix(_t.ChipBg, _t.Text, 0.06) : _t.ChipBg);
                D.Fill(g, _rcChips[i], U(7), bg);
                Color fg = on ? _t.AccentOn : _t.ChipText;
                D.Txt(g, ModeNames[i], F.Get(9.2f, false), fg, _rcChips[i], D.Center);
            }
        }

        private void DrawWell(Graphics g, Rectangle well, bool accent)
        {
            if (well.IsEmpty) return;
            D.Fill(g, well, U(8), _t.Input);
            D.Stroke(g, new RectangleF(well.X + 0.5f, well.Y + 0.5f, well.Width - 1, well.Height - 1),
                U(8), accent ? _t.Accent : _t.InputLine, accent ? 2f : 1f);
        }

        private void DrawPill(Graphics g, Rectangle r, string text, bool on, int id)
        {
            bool hot = _hot == id;
            Color bg = on ? _t.AccentSoft : (hot ? D.Mix(_t.ChipBg, _t.Text, 0.05) : _t.ChipBg);
            D.Fill(g, r, U(6), bg);
            D.Stroke(g, new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), U(6), on ? _t.Accent : _t.Line, 1f);
            D.Txt(g, text, F.Get(9f, on), on ? _t.Accent : _t.Text, r, D.Center);
        }

        private void Label(Graphics g, string text, int x, int y, int h)
        {
            D.Txt(g, text, F.Get(9f, false), _t.Sub, new Rectangle(x, y, U(74), h), D.Left);
        }

        // 紧贴输入框左侧、右对齐的标签（用于同一行里的第二、第三个字段）
        private void LabelBefore(Graphics g, string text, Rectangle well)
        {
            if (well.IsEmpty) return;
            int lw = U(56);
            D.Txt(g, text, F.Get(9f, false), _t.Sub, new Rectangle(well.X - U(12) - lw, well.Y, lw, well.Height), D.Right);
        }

        private void DrawRuleRow1(Graphics g)
        {
            int pad = U(24);
            if (_mode == ModeSequence)
            {
                Label(g, "名称模板", pad, _rcTplWell.Y, _rcTplWell.Height);
                DrawWell(g, _rcTplWell, _focusBox == _tpl);
                LabelBefore(g, "排序", _rcSort);
                DrawWell(g, _rcSort, false);
                D.Txt(g, SortNames[Clamp(_rule.SortKind, 0, SortNames.Length - 1)], F.Get(9.2f, false), _t.Text,
                    new Rectangle(_rcSort.X + U(12), _rcSort.Y, _rcSort.Width - U(34), _rcSort.Height), D.LeftEll);
                D.Txt(g, "▾", F.Get(8f, false), _t.Sub, new Rectangle(_rcSort.Right - U(24), _rcSort.Y, U(18), _rcSort.Height), D.Center);
            }
            else if (_mode == ModeReplace)
            {
                Label(g, "查找", pad, _rcFindWell.Y, _rcFindWell.Height);
                DrawWell(g, _rcFindWell, _focusBox == _find);
                LabelBefore(g, "替换为", _rcReplWell);
                DrawWell(g, _rcReplWell, _focusBox == _repl);
            }
            else if (_mode == ModeAffix)
            {
                Label(g, "前缀", pad, _rcPrefixWell.Y, _rcPrefixWell.Height);
                DrawWell(g, _rcPrefixWell, _focusBox == _prefix);
                LabelBefore(g, "后缀", _rcSuffixWell);
                DrawWell(g, _rcSuffixWell, _focusBox == _suffix);
            }
            else if (_mode == ModeCase)
            {
                Label(g, "处理方式", pad, _rcCase.Y, _rcCase.Height);
                DrawWell(g, _rcCase, false);
                D.Txt(g, CaseNames[Clamp(_rule.CaseKind, 0, CaseNames.Length - 1)], F.Get(9.2f, false), _t.Text,
                    new Rectangle(_rcCase.X + U(12), _rcCase.Y, _rcCase.Width - U(34), _rcCase.Height), D.LeftEll);
                D.Txt(g, "▾", F.Get(8f, false), _t.Sub, new Rectangle(_rcCase.Right - U(24), _rcCase.Y, U(18), _rcCase.Height), D.Center);
            }
            else if (_mode == ModeFree)
            {
                D.Txt(g, "双击下面的文件名（或按 F2）直接写名字；手写过的行不再受规则影响",
                    F.Get(9.2f, false), _t.Sub,
                    new Rectangle(pad, _rcSort.Y, Math.Max(U(200), _rcSort.X - pad - U(16)), _rcSort.Height), D.LeftEll);
            }
        }

        private void DrawRuleRow2(Graphics g)
        {
            int pad = U(24);
            if (_mode == ModeSequence)
            {
                Label(g, "起始序号", pad, _rcStartWell.Y, _rcStartWell.Height);
                DrawWell(g, _rcStartWell, _focusBox == _numStart);
                LabelBefore(g, "步长", _rcStepWell);
                DrawWell(g, _rcStepWell, _focusBox == _numStep);
                LabelBefore(g, "位数", _rcPadWell);
                DrawWell(g, _rcPadWell, _focusBox == _numPad);
                int hintW = Math.Max(0, _rcVarHintRight - (_rcPadWell.Right + U(12)));
                if (hintW > U(40))
                {
                    D.Txt(g, "位数 2 → 01", F.Get(8.8f, false), _t.Sub,
                        new Rectangle(_rcPadWell.Right + U(12), _rcPadWell.Y, hintW, _rcPadWell.Height), D.LeftEll);
                }

                // 变量芯片：点一下插到光标处
                DrawPill(g, _rcPreset, "模板 ▾", false, IdPreset);
                for (int i = 0; i < 4; i++) DrawPill(g, _rcVars[i], VarTokens[i], false, IdVar + i);
            }
            else if (_mode == ModeReplace)
            {
                DrawPill(g, _rcCaseSens, "区分大小写", _rule.CaseSensitive, IdCaseSens);
                DrawPill(g, _rcRegex, "正则表达式", _rule.UseRegex, IdRegex);
                D.Txt(g, "正则模式下替换内容可用 $1 分组", F.Get(8.8f, false), _t.Sub,
                    new Rectangle(_rcRegex.Right + U(14), _rcRegex.Y, ClientSize.Width - _rcRegex.Right - U(40), _rcRegex.Height), D.LeftEll);
            }
            else if (_mode == ModeAffix)
            {
                D.Txt(g, "后缀加在扩展名之前：照片.jpg + 前缀 2024_ → 2024_照片.jpg", F.Get(8.8f, false), _t.Sub,
                    new Rectangle(pad, _rcListHeader.Y - U(32), ClientSize.Width - pad * 2, U(24)), D.LeftEll);
            }
            else if (_mode == ModeCase)
            {
                D.Txt(g, "「清理非法字符」把 < > : \" / \\ | ? * 换成下划线，合并重复空格，去掉结尾的空格和点", F.Get(8.8f, false), _t.Sub,
                    new Rectangle(pad, _rcListHeader.Y - U(32), ClientSize.Width - pad * 2, U(24)), D.LeftEll);
            }

        }

        private void DrawExample(Graphics g)
        {
            if (_mode != ModeSequence) return;
            RenameItem it = FirstItem();
            if (it == null) return;
            int pad = U(24);
            string oldName = it.OldName ?? string.Empty;
            string newName = string.IsNullOrEmpty(it.NewName) ? oldName : it.NewName;
            int y = _rcListHeader.Y - U(30);
            int x = pad;
            D.Txt(g, "示例", F.Get(8.8f, false), _t.Sub, new Rectangle(x, y, U(34), U(24)), D.Left);
            x += U(40);
            D.Txt(g, oldName, F.Get(9f, false), _t.Sub, new Rectangle(x, y, U(300), U(24)), D.LeftEll);
            x += U(300);
            D.Txt(g, "→", F.Get(9f, false), _t.Sub, new Rectangle(x, y, U(24), U(24)), D.Center);
            x += U(26);
            int cp = CommonPrefix(oldName, newName);
            string head = newName.Substring(0, cp);
            string tail = newName.Substring(cp);
            D.Txt(g, head, F.Get(9f, false), _t.Text, new Rectangle(x, y, U(260), U(24)), D.LeftEll);
            x += D.Measure(head, F.Get(9f, false)).Width;
            D.Txt(g, tail, F.Get(9f, true), _t.Accent, new Rectangle(x, y, U(200), U(24)), D.LeftEll);
        }

        private static int CommonPrefix(string a, string b)
        {
            if (a == null) a = string.Empty;
            if (b == null) b = string.Empty;
            int n = Math.Min(a.Length, b.Length);
            int i = 0;
            while (i < n && a[i] == b[i]) i++;
            return i;
        }

        private void DrawList(Graphics g)
        {
            int pad = U(24);
            D.HLine(g, 0, ClientSize.Width, _rcListHeader.Y, _t.Line);
            int colOld = pad;
            int colNew = (int)(ClientSize.Width * 0.40);
            int colStatus = (int)(ClientSize.Width * 0.80);

            D.Txt(g, "原名称", F.Get(8.8f, false), _t.Sub, new Rectangle(colOld, _rcListHeader.Y, U(200), _rcListHeader.Height), D.Left);
            D.Txt(g, "新名称", F.Get(8.8f, false), _t.Sub, new Rectangle(colNew, _rcListHeader.Y, U(200), _rcListHeader.Height), D.Left);

            int visible = Math.Max(0, _rcList.Height / _rowH);
            int maxScroll = Math.Max(0, _items.Count - visible);
            if (_scroll > maxScroll) _scroll = maxScroll;
            if (_scroll < 0) _scroll = 0;

            g.SetClip(new Rectangle(_rcList.X, _rcList.Y, _rcList.Width, _rcList.Height));

            for (int i = _scroll; i < _items.Count && i < _scroll + visible + 1; i++)
            {
                RenameItem it = _items[i];
                int y = _rcList.Y + (i - _scroll) * _rowH;
                Rectangle row = new Rectangle(0, y, ClientSize.Width, _rowH);
                bool selected = _sel.Contains(it);
                if (selected) D.Fill(g, row, 0, _t.Sel);
                else if (i % 2 == 1) D.Fill(g, row, 0, _t.RowAlt);
                if (_hot == IdRow + i) D.Fill(g, row, 0, _t.Hover);

                bool dim = it.Excluded;
                Color oldColor = dim ? D.Mix(_t.Sub, _t.Card, 0.55) : _t.Sub;
                D.Txt(g, it.OldName, F.Get(9.2f, false), oldColor,
                    new Rectangle(colOld, y, colNew - colOld - U(28), _rowH), D.LeftEll);
                D.Txt(g, "→", F.Get(8.6f, false), _t.Sub, new Rectangle(colNew - U(26), y, U(20), _rowH), D.Center);

                string newName = string.IsNullOrEmpty(it.NewName) ? it.OldName : it.NewName;
                int cp = CommonPrefix(it.OldName ?? string.Empty, newName);
                string head = newName.Substring(0, cp);
                string tail = newName.Substring(cp);
                int maxNew = colStatus - colNew - U(10);
                Color headColor = dim ? D.Mix(_t.Sub, _t.Card, 0.55)
                    : (it.Status == "未变化" ? _t.Sub : _t.Text);
                int nx = colNew + U(2);
                D.Txt(g, head, F.Get(9.2f, false), headColor, new Rectangle(nx, y, maxNew, _rowH), D.LeftEll);
                // 用「整串宽 - 尾部宽」推 head 的绘制宽度：两次测量同字体同 flags，
                // 每次测量自带的那点额外内边距会互相抵消，两段文字才能严丝合缝。
                Size szFull = TextRenderer.MeasureText(newName, F.Get(9.2f, false), new Size(10000, 10000), D.Left);
                Size szTail = TextRenderer.MeasureText(tail, F.Get(9.2f, false), new Size(10000, 10000), D.Left);
                int hw = Math.Max(0, szFull.Width - szTail.Width);
                int room = maxNew - hw;
                if (room > U(6) && tail.Length > 0)
                {
                    Color tailColor = dim ? D.Mix(_t.Accent, _t.Card, 0.55) : _t.Accent;
                    D.Txt(g, tail, F.Get(9.2f, true), tailColor,
                        new Rectangle(nx + hw, y, room, _rowH), D.LeftEll);
                }

                string statusText = string.Empty;
                Color statusColor = _t.Sub;
                if (it.Excluded) { statusText = "已排除"; statusColor = _t.Sub; }
                else if (it.Status == "未变化") { statusText = "未变化"; statusColor = _t.Sub; }
                else if (!it.Runnable) { statusText = string.IsNullOrEmpty(it.Status) ? "无法重命名" : it.Status; statusColor = _t.Danger; }
                if (statusText.Length > 0)
                    D.Txt(g, statusText, F.Get(8.6f, false), statusColor,
                        new Rectangle(colStatus, y, ClientSize.Width - colStatus - U(16), _rowH), D.LeftEll);
                else if (it.Manual)
                    D.Txt(g, "手动", F.Get(8.6f, false), _t.Accent,
                        new Rectangle(colStatus, y, ClientSize.Width - colStatus - U(16), _rowH), D.LeftEll);
            }

            g.ResetClip();

            if (_items.Count == 0)
            {
                D.Txt(g, "选中文件后右键「批量重命名」，或把文件拖进这个窗口（可分批追加）", F.Get(9.4f, false), _t.Sub,
                    new Rectangle(0, _rcList.Y + U(40), ClientSize.Width, U(28)), D.Center);
            }

            // 细滚动条
            if (_items.Count > visible && visible > 0)
            {
                int trackX = ClientSize.Width - U(8);
                int trackY = _rcList.Y + U(4);
                int trackH = _rcList.Height - U(8);
                int thumbH = Math.Max(U(28), (int)(trackH * (double)visible / _items.Count));
                int thumbY = trackY + (int)((trackH - thumbH) * (double)_scroll / Math.Max(1, _items.Count - visible));
                D.Fill(g, new RectangleF(trackX - U(3), thumbY, U(6), thumbH), U(3), D.Mix(_t.Sub, _t.Card, 0.55));
            }
        }

        private void DrawFooter(Graphics g)
        {
            int pad = U(24);
            D.HLine(g, 0, ClientSize.Width, _rcUndo.Y - U(16), _t.Line);

            bool hotUndo = _hot == IdUndo;
            Color undoColor = hotUndo ? _t.Accent : _t.Sub;
            string undoText = "撤销上次重命名";
            int undoW = D.Measure(undoText, F.Get(9.2f, false)).Width + U(6);
            _rcUndo.Width = undoW;
            D.Txt(g, undoText, F.Get(9.2f, false), undoColor, _rcUndo, D.Left);

            string hint = "Enter 应用 · Esc 取消 · Ctrl+Z 撤销 · 双击行单独改名";
            int hintX = _rcUndo.Right + U(24);
            D.Txt(g, hint, F.Get(8.6f, false), _t.Sub,
                new Rectangle(hintX, _rcUndo.Y, Math.Max(U(60), _rcCancel.X - hintX - U(16)), _rcUndo.Height), D.LeftEll);

            int will = CountWill();
            // 取消
            bool hotCancel = _hot == IdCancel;
            D.Fill(g, _rcCancel, U(6), hotCancel ? D.Mix(_t.Card, _t.Text, 0.05) : _t.Card);
            D.Stroke(g, new RectangleF(_rcCancel.X + 0.5f, _rcCancel.Y + 0.5f, _rcCancel.Width - 1, _rcCancel.Height - 1), U(6), _t.Line, 1f);
            D.Txt(g, "取消", F.Get(9.4f, false), _t.Text, _rcCancel, D.Center);

            // 主按钮
            bool enabled = will > 0;
            bool hotApply = _hot == IdApply && enabled;
            Color applyBg = !enabled ? D.Mix(_t.Card, _t.Sub, 0.18)
                : (hotApply ? D.Mix(_t.Accent, _t.Dark ? Color.White : Color.Black, 0.10) : _t.Accent);
            D.Fill(g, _rcApply, U(6), applyBg);
            string label = will > 0 ? "重命名 " + will.ToString(CultureInfo.InvariantCulture) + " 项" : "没有可重命名的项";
            D.Txt(g, label, F.Get(9.4f, false), enabled ? _t.AccentOn : _t.Sub, _rcApply, D.Center);
        }

        // ---------------------------------------------------------------- 命中
        private int Hit(Point p)
        {
            if (_rcClose.Contains(p)) return IdClose;
            for (int i = 0; i < _rcChips.Length; i++) if (_rcChips[i].Contains(p)) return IdChip + i;
            if (_rcSort.Contains(p) && _mode == ModeSequence) return IdSort;
            if (_rcCase.Contains(p) && _mode == ModeCase) return IdCase;
            if (!_rcCaseSens.IsEmpty && _rcCaseSens.Contains(p)) return IdCaseSens;
            if (!_rcRegex.IsEmpty && _rcRegex.Contains(p)) return IdRegex;
            if (_mode == ModeSequence)
            {
                if (_rcPreset.Contains(p)) return IdPreset;
                for (int i = 0; i < 4; i++) if (_rcVars[i].Contains(p)) return IdVar + i;
            }
            if (_rcUndo.Contains(p)) return IdUndo;
            if (_rcCancel.Contains(p)) return IdCancel;
            if (_rcApply.Contains(p)) return IdApply;
            if (_rcList.Contains(p))
            {
                int idx = _scroll + (p.Y - _rcList.Y) / Math.Max(1, _rowH);
                if (idx >= 0 && idx < _items.Count) return IdRow + idx;
                return IdList;
            }
            return IdNone;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            int h = Hit(e.Location);
            if (h >= IdRow)
            {
                // 行热区不显示悬停底，避免和选中混淆；只保留手型
                if (_hot != h) { _hot = h; Cursor = Cursors.Default; Invalidate(); }
                return;
            }
            if (h != _hot)
            {
                _hot = h;
                Cursor = (h == IdNone) ? Cursors.Default : Cursors.Hand;
                Invalidate();
            }
        }

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            int h = Hit(e.Location);
            _pressed = true;
            _pressedId = h;
            if (h >= IdRow)
            {
                RenameItem it = _items[h - IdRow];
                bool ctrl = (Control.ModifierKeys & Keys.Control) == Keys.Control;
                bool shift = (Control.ModifierKeys & Keys.Shift) == Keys.Shift;
                int idx = h - IdRow;
                if (shift && _selAnchor >= 0)
                {
                    int a = Math.Min(_selAnchor, idx), b = Math.Max(_selAnchor, idx);
                    if (!ctrl) _sel.Clear();
                    for (int i = a; i <= b && i < _items.Count; i++) _sel.Add(_items[i]);
                }
                else if (ctrl)
                {
                    if (!_sel.Remove(it)) _sel.Add(it);
                    _selAnchor = idx;
                }
                else
                {
                    _sel.Clear();
                    _sel.Add(it);
                    _selAnchor = idx;
                }
                Invalidate();
            }
        }

        private void OnMouseUp(object sender, MouseEventArgs e)
        {
            if (!_pressed) return;
            _pressed = false;
            int h = Hit(e.Location);
            if (h != _pressedId) return;
            switch (h)
            {
                case IdClose: OnCancel(); break;
                case IdCancel: OnCancel(); break;
                case IdApply: OnApply(); break;
                case IdUndo: OnUndo(); break;
                case IdSort: OpenSort(); break;
                case IdCase: OpenCase(); break;
                case IdCaseSens: _rule.CaseSensitive = !_rule.CaseSensitive; RefreshPreview(); break;
                case IdRegex: _rule.UseRegex = !_rule.UseRegex; RefreshPreview(); break;
                case IdPreset: OpenPreset(); break;
                default:
                    if (h >= IdVar && h < IdVar + 4) InsertToken(VarTokens[h - IdVar]);
                    else if (h >= IdChip && h <= IdChip + 4)
                    {
                        _mode = h - IdChip;
                        ApplyModeToRule();
                        LoadRuleToBoxes();
                        RefreshPreview();
                        Invalidate();
                        if (_mode == ModeSequence) _tpl.Focus();
                        else if (_mode == ModeReplace) _find.Focus();
                        else if (_mode == ModeAffix) _prefix.Focus();
                    }
                    break;
            }
        }

        private void OnWheel(object sender, MouseEventArgs e)
        {
            int visible = Math.Max(1, _rcList.Height / Math.Max(1, _rowH));
            int maxScroll = Math.Max(0, _items.Count - visible);
            _scroll -= (e.Delta / 120) * 3;
            if (_scroll < 0) _scroll = 0;
            if (_scroll > maxScroll) _scroll = maxScroll;
            Invalidate();
        }

        // ---------------------------------------------------------------- 行内改名
        private TextBox EnsureEdit()
        {
            if (_edit != null) return _edit;
            TextBox b = new TextBox();
            b.BorderStyle = BorderStyle.None;
            b.Font = F.Get(9.2f, false);
            b.ForeColor = _t.Text;
            b.BackColor = _t.Input;
            b.Visible = false;
            b.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    int next = CommitEdit(true);
                    if (next >= 0) BeginRowEdit(next);
                }
                else if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.F2)
                {
                    e.SuppressKeyPress = true;
                    CancelEdit();
                }
            };
            b.LostFocus += delegate { if (_editIndex >= 0) CommitEdit(false); };
            Controls.Add(b);
            _edit = b;
            return b;
        }

        private void BeginRowEdit(int index)
        {
            if (index < 0 || index >= _items.Count) return;
            TextBox ed = EnsureEdit();
            int visible = Math.Max(1, _rcList.Height / Math.Max(1, _rowH));
            if (index < _scroll) _scroll = index;
            else if (index >= _scroll + visible) _scroll = Math.Max(0, index - visible + 1);

            _editIndex = index;
            RenameItem it = _items[index];
            ed.Text = string.IsNullOrEmpty(it.NewName) ? it.OldName : it.NewName;

            int y = _rcList.Y + (index - _scroll) * _rowH;
            int colNew = (int)(ClientSize.Width * 0.40);
            int colStatus = (int)(ClientSize.Width * 0.80);
            int h = ed.PreferredHeight;
            ed.SetBounds(colNew + U(4), y + (_rowH - h) / 2, Math.Max(U(80), colStatus - colNew - U(16)), h);
            ed.Visible = true;
            ed.BringToFront();
            ed.Focus();
            ed.SelectAll();
            Invalidate();
        }

        private int CommitEdit(bool moveNext)
        {
            if (_edit == null || _editIndex < 0) return -1;
            int idx = _editIndex;
            _editIndex = -1;
            _edit.Visible = false;
            RenameItem it = _items[idx];
            string txt = (_edit.Text == null ? string.Empty : _edit.Text).Trim();
            if (txt.Length == 0 || string.Equals(txt, it.OldName, StringComparison.Ordinal))
            {
                it.Manual = false;
                it.ManualName = null;
            }
            else
            {
                it.Manual = true;
                it.ManualName = txt;
            }
            _sel.Clear();
            _sel.Add(it);
            _selAnchor = idx;
            RefreshPreview();
            if (moveNext && idx + 1 < _items.Count) return idx + 1;
            return -1;
        }

        private void CancelEdit()
        {
            if (_edit == null) return;
            _editIndex = -1;
            _edit.Visible = false;
            Invalidate();
        }

        private void OnDoubleClick(object sender, MouseEventArgs e)
        {
            int h = Hit(e.Location);
            if (h >= IdRow) BeginRowEdit(h - IdRow);
        }

        private void OpenSort()
        {
            Pop p = new Pop(SortNames, Clamp(_rule.SortKind, 0, SortNames.Length - 1));
            p.Picked += delegate
            {
                _rule.SortKind = p.SelectedIndex;
                RefreshPreview();
            };
            p.ShowAt(RectangleToScreen(_rcSort));
        }

        // 把变量插到模板框的光标处（有选区就替换选区）
        private void InsertToken(string token)
        {
            if (token == null) return;
            string text = _tpl.Text ?? string.Empty;
            int s = _tpl.SelectionStart;
            int len = _tpl.SelectionLength;
            if (s < 0) s = text.Length;
            if (s > text.Length) s = text.Length;
            if (len < 0) len = 0;
            if (len > text.Length - s) len = text.Length - s;
            _tpl.Text = text.Substring(0, s) + token + text.Substring(s + len);
            _tpl.SelectionStart = s + token.Length;
            _tpl.SelectionLength = 0;
            _tpl.Focus();
            RefreshPreview();
        }

        private void OpenPreset()
        {
            Pop p = new Pop(PresetNames, 0);
            p.Picked += delegate
            {
                _tpl.Text = PresetNames[Clamp(p.SelectedIndex, 0, PresetNames.Length - 1)];
                _tpl.SelectionStart = _tpl.Text.Length;
                _tpl.SelectionLength = 0;
                _tpl.Focus();
                RefreshPreview();
            };
            p.ShowAt(RectangleToScreen(_rcPreset));
        }

        private void OpenCase()
        {
            Pop p = new Pop(CaseNames, Clamp(_rule.CaseKind, 0, CaseNames.Length - 1));
            p.Picked += delegate
            {
                _rule.CaseKind = p.SelectedIndex;
                RefreshPreview();
            };
            p.ShowAt(RectangleToScreen(_rcCase));
        }

        // ---------------------------------------------------------------- 动作
        private void AddPaths(string[] paths)
        {
            List<RenameItem> more = Engine.Load(paths);
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (RenameItem it in _items) seen.Add(it.FullPath);
            foreach (RenameItem it in more) if (seen.Add(it.FullPath)) _items.Add(it);
            RefreshPreview();
        }

        private void SetExcluded(bool excluded)
        {
            foreach (RenameItem it in _sel) it.Excluded = excluded;
            RefreshPreview();
        }

        private void OnApply()
        {
            if (_loading) return;
            SaveRuleFromUi();
            Engine.Build(_items, _rule);
            int will = CountWill();
            if (will == 0)
            {
                Toast t = Toast.Create("没有需要重命名的项目", null, null);
                t.Show();
                return;
            }
            string msg;
            int n = Engine.Apply(_items, out msg);
            if (n < 0)
            {
                MessageBox.Show(this, msg, "批量重命名失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                RefreshPreview();
                return;
            }
            try { _rule.Save(); } catch (Exception) { }
            int skipped = _items.Count - n;
            string text = "已重命名 " + n.ToString(CultureInfo.InvariantCulture) + " 项";
            if (skipped > 0) text += "，跳过 " + skipped.ToString(CultureInfo.InvariantCulture) + " 项";
            App.PendingToast = Toast.Create(text, "撤销", delegate
            {
                string m2;
                Engine.UndoLast(out m2);
                Toast t2 = Toast.Create(m2, null, null);
                t2.Show();
            });
            _loading = true;
            Close();
        }

        private void OnUndo()
        {
            string msg;
            int n = Engine.UndoLast(out msg);
            if (n <= 0)
            {
                Toast t = Toast.Create(msg, null, null);
                t.Show();
                return;
            }
            try { _rule.Save(); } catch (Exception) { }
            App.PendingToast = Toast.Create(msg, null, null);
            _loading = true;
            Close();
        }

        private void OnCancel()
        {
            _loading = true;
            Close();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { OnCancel(); return true; }
            if (keyData == Keys.Enter) { OnApply(); return true; }
            if (keyData == (Keys.Control | Keys.Z)) { OnUndo(); return true; }
            if (keyData == Keys.F2)
            {
                if (_selAnchor >= 0) BeginRowEdit(_selAnchor);
                else if (_items.Count > 0) BeginRowEdit(0);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void ApplySizeAndCenter()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int w = Math.Min(U(900), wa.Width - U(24));
            int h = Math.Min(U(580), wa.Height - U(24));
            // 项目少时按内容收窄：先按当前尺寸算一遍布局，取列表实际需要的高度
            if (_items != null && _items.Count > 0)
            {
                LayoutAll();
                int need = _rcList.Y + _items.Count * _rowH + U(10) + U(70);
                h = Math.Min(h, Math.Max(U(380), need));
            }
            MinimumSize = new Size(Math.Min(U(720), w), Math.Min(U(430), h));
            ClientSize = new Size(w, h);
            CenterToScreen();
            RefreshScale();
            Dev.Log("size scale=" + _scale.ToString("0.##", CultureInfo.InvariantCulture)
                + " client=" + ClientSize.Width.ToString(CultureInfo.InvariantCulture) + "x" + ClientSize.Height.ToString(CultureInfo.InvariantCulture));
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RefreshScale();
            // 句柄创建后 DeviceDpi 才是真实值，这里按真实 DPI 定尺寸并夹到工作区内
            ApplySizeAndCenter();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ApplySizeAndCenter();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplyModeToRule();
            RefreshPreview();

            if (_mode == ModeSequence) _tpl.Focus();
            else if (_mode == ModeReplace) _find.Focus();
            else if (_mode == ModeAffix) _prefix.Focus();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (!_loading)
            {
                try { SaveRuleFromUi(); _rule.Save(); }
                catch (Exception) { }
            }
        }

        protected override bool IsDragArea(Point p)
        {
            return p.Y < U(58) && !_rcClose.Contains(p);
        }

        private static int Clamp(int v, int lo, int hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }
    }
}
