// BatchRename — 列表里的「行内改名」编辑器
// C# 5 语法（csc 4.0 编译，勿用插值字符串 / ?. / nameof / 自动属性初始化器）
//
// 职责边界：
//   * 本类只管一个无边框 TextBox 的「摆在哪、什么时候算提交、什么时候算放弃」；
//   * 具体把文字写到哪个 RenameItem 上，由外层 UiForm 订阅 Commit / Cancel 决定。
// 这样列表控件可以整块复用，编辑器也能单独测试。

using System;
using System.Drawing;
using System.Windows.Forms;

namespace DshBatchRename
{
    /// <summary>
    /// 跟随某一行的行内输入框。同一时刻只有一个实例，随用随开。
    /// </summary>
    internal class RowEditor
    {
        private readonly Form _owner;
        private readonly TextBox _box;

        // 只有「真的拿到过焦点」的输入框失焦才算改完：窗口刚激活时系统会把焦点
        // 抢回窗体里的第一个控件，那种失焦不能把手写内容当成确认。
        private bool _hadFocus;

        /// <summary>Enter 提交。参数 true 表示用户希望继续改下一行。</summary>
        public event Action<bool> Commit;

        /// <summary>Esc 或点空白处放弃这次修改。</summary>
        public event Action Cancel;

        public RowEditor(Form owner, Theme theme)
        {
            Index = -1;          // 默认不在编辑状态（int 的默认值是 0，会误判成「正在改第 1 行」）
            _owner = owner;

            _box = new TextBox();
            _box.BorderStyle = BorderStyle.None;
            _box.AutoSize = false;                 // 单行 TextBox 默认 AutoSize，会把宽度吃掉
            _box.Font = F.Get(9.2f, false);
            _box.ForeColor = theme.Text;
            _box.BackColor = theme.Input;
            _box.Visible = false;
            _box.KeyDown += OnBoxKeyDown;
            _box.GotFocus += delegate { _hadFocus = true; };
            _box.LostFocus += OnBoxLostFocus;

            owner.Controls.Add(_box);
        }

        /// <summary>正在编辑的行号；-1 表示没在编辑。</summary>
        public int Index { get; private set; }

        /// <summary>是否处于编辑状态（行号有效）。</summary>
        public bool Active { get { return Index >= 0; } }

        /// <summary>输入框当前是否可见（滚出行外时会暂时隐藏）。</summary>
        public bool Visible { get { return _box.Visible; } }

        /// <summary>输入框在窗体客户区里的位置，用来在它周围画强调圈。</summary>
        public Rectangle Bounds { get { return _box.Bounds; } }

        /// <summary>输入框内容（可能为 null，调用方自行兜底）。</summary>
        public string Text { get { return _box.Text; } }

        /// <summary>开始编辑某一行：把当前新名字填进去，光标全选，直接可以覆盖输入。</summary>
        public void Begin(int index, string text)
        {
            Index = index;
            _hadFocus = false;
            _box.Text = text ?? string.Empty;
            _box.Visible = false;   // 等 Place 摆好再显示，避免闪一下
        }

        /// <summary>
        /// 把输入框摆到指定行上。宽高由调用方给（它知道缩放和列宽），
        /// 这里只保证不超出窗体右边界。
        /// </summary>
        public void Place(int x, int y, int width, int rowHeight)
        {
            if (!Active) return;
            int h = _box.PreferredHeight;
            int w = Math.Max(40, width);
            int limit = _owner.ClientSize.Width - 16;
            if (x + w > limit) w = Math.Max(40, limit - x);
            _box.SetBounds(x, y + (rowHeight - h) / 2, w, h);
            _box.Visible = true;
        }

        /// <summary>行滚出可视区时先藏起来，但保留编辑状态。</summary>
        public void HideBox()
        {
            _box.Visible = false;
        }

        /// <summary>结束编辑（提交或放弃后调用）。</summary>
        public void Hide()
        {
            Index = -1;
            _hadFocus = false;
            _box.Visible = false;
        }

        /// <summary>把焦点交给输入框并全选。</summary>
        public void FocusAndSelect()
        {
            _box.BringToFront();
            _box.Focus();
            _box.SelectAll();
        }

        /// <summary>焦点离开本窗体的其它控件——输入框自己派生的类里做的判断。</summary>
        private void OnBoxLostFocus(object sender, EventArgs e)
        {
            if (!Active || !_hadFocus) return;
            _hadFocus = false;
            if (_owner.ContainsFocus && Commit != null) Commit(false);
        }

        /// <summary>Enter 提交并跳下一行；Esc / F2 放弃。</summary>
        private void OnBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                if (Commit != null) Commit(true);
            }
            else if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.F2)
            {
                e.SuppressKeyPress = true;
                if (Cancel != null) Cancel();
            }
        }
    }
}
