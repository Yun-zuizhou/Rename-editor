// BatchRename — 程序入口（命令行、GUI 启动、提示条、离线截图）
// 说明见 README.md；C# 5 语法（csc 4.0 编译，勿用插值字符串 / ?. / nameof / 自动属性初始化器）

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace DshBatchRename
{
    internal static class App
    {
        public static Toast PendingToast;
    }

    internal class Toast : Form
    {
        private Timer _timer;
        private Label _lbl;

        public Toast(string text, string actionText, Action action)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(30, 30, 30);
            ClientSize = new Size(392, 88);
            Font = UiFont(9.5f);

            _lbl = new Label();
            _lbl.AutoSize = false;
            _lbl.Bounds = new Rectangle(16, 14, 360, 26);
            _lbl.Text = text;
            _lbl.ForeColor = Color.White;
            _lbl.BackColor = Color.Transparent;
            _lbl.AutoEllipsis = true;
            _lbl.Font = new Font(Font.FontFamily, 10.5f, FontStyle.Bold);
            Controls.Add(_lbl);

            int ms = 4200;
            if (!string.IsNullOrEmpty(actionText))
            {
                ms = 9000;
                Button btn = new Button();
                btn.Text = actionText;
                btn.Bounds = new Rectangle(16, 48, 92, 28);
                btn.FlatStyle = FlatStyle.Flat;
                btn.ForeColor = Color.White;
                btn.BackColor = Color.FromArgb(58, 58, 58);
                btn.FlatAppearance.BorderColor = Color.FromArgb(96, 96, 96);
                btn.Click += delegate
                {
                    try
                    {
                        if (action != null) action();
                    }
                    catch (Exception) { }
                    Close();
                };
                Controls.Add(btn);
            }

            _timer = new Timer();
            _timer.Interval = ms;
            _timer.Tick += delegate
            {
                _timer.Stop();
                Close();
            };
        }

        public static Font UiFont(float size)
        {
            try { return new Font("Microsoft YaHei UI", size); }
            catch (Exception) { }
            try { return new Font("Microsoft Sans Serif", size); }
            catch (Exception) { return SystemFonts.DefaultFont; }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000;
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _timer.Start();
        }

        public static Toast Create(string text, string actionText, Action action)
        {
            Toast t = new Toast(text, actionText, action);
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int x = wa.Right - t.Width - 24;
            int y = wa.Bottom - t.Height - 24;
            if (x < wa.Left) x = wa.Left;
            if (y < wa.Top) y = wa.Top;
            t.Location = new Point(x, y);
            return t;
        }
    }

    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        public static int Main(string[] args)
        {
            try { SetProcessDPIAware(); }
            catch (Exception) { }

            args = ExpandListArgs(args);

            if (args.Length >= 1 && Eq(args[0], "--selftest"))
                return SelfTest.Run(args.Length >= 2 ? args[1] : null);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Length >= 1 && Eq(args[0], "--undo"))
            {
                string msg;
                Engine.UndoLast(out msg);
                Application.Run(Toast.Create(msg, null, null));
                return 0;
            }

            if (args.Length >= 1 && Eq(args[0], "--quick"))
            {
                List<string> rest = new List<string>();
                for (int i = 1; i < args.Length; i++) rest.Add(args[i]);
                Rule r = Rule.Load();
                bool hasRule = File.Exists(AppInfo.SettingsPath);
                List<RenameItem> items = Engine.Load(rest);
                if (!hasRule || items.Count == 0) return RunGui(rest, r);

                Engine.Build(items, r);
                int will = 0;
                foreach (RenameItem it in items) if (it.Runnable) will++;
                if (will == 0)
                {
                    Application.Run(Toast.Create("没有可以重命名的项目（规则未产生变化，或存在冲突）", null, null));
                    return 0;
                }
                string msg;
                int n = Engine.Apply(items, out msg);
                if (n < 0)
                {
                    Application.Run(Toast.Create("重命名失败：" + msg, null, null));
                    return 2;
                }
                int skipped = items.Count - n;
                string text = "已重命名 " + n.ToString(CultureInfo.InvariantCulture) + " 项";
                if (skipped > 0) text += "，跳过 " + skipped.ToString(CultureInfo.InvariantCulture) + " 项";
                Application.Run(Toast.Create(text, "撤销", delegate
                {
                    string m2;
                    Engine.UndoLast(out m2);
                }));
                return 0;
            }

            if (args.Length >= 2 && Eq(args[0], "--shot"))
            {
                List<string> shotFiles = new List<string>();
                for (int i = 2; i < args.Length; i++) shotFiles.Add(args[i]);
                return Shot(args[1], shotFiles);
            }

            List<string> files = new List<string>();
            bool folderMode = false;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == null) continue;
                if (Eq(a, "--folder"))
                {
                    folderMode = true;
                    continue;
                }
                if (a.StartsWith("--", StringComparison.Ordinal)) continue;
                files.Add(a);
            }
            if (folderMode) files = ExpandFolders(files);
            return RunGui(files, Rule.Load());
        }

        // --list <file>：右键扩展把选中路径写进 UTF-8 清单文件，避免命令行超长与转义问题
        private static string[] ExpandListArgs(string[] args)
        {
            if (args == null || args.Length == 0) return args == null ? new string[0] : args;
            List<string> outArgs = new List<string>();
            bool consumed = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--list" && i + 1 < args.Length)
                {
                    string listPath = args[i + 1];
                    i++;
                    try
                    {
                        if (File.Exists(listPath))
                        {
                            foreach (string line in File.ReadAllLines(listPath))
                            {
                                string t = line.Trim();
                                if (t.Length > 0) outArgs.Add(t);
                            }
                            consumed = true;
                            try { File.Delete(listPath); }
                            catch (Exception) { }
                        }
                    }
                    catch (Exception) { }
                    continue;
                }
                outArgs.Add(args[i]);
            }
            return consumed ? outArgs.ToArray() : args;
        }

        private static int RunGui(List<string> files, Rule rule)        {
            RenameForm f = new RenameForm(files.ToArray(), rule);
            Application.Run(f);
            if (App.PendingToast != null)
            {
                Application.Run(App.PendingToast);
                App.PendingToast = null;
            }
            return 0;
        }

        // 背景右键菜单传进来的是文件夹本身，这里展开成该文件夹下的文件（不递归）。
        private static List<string> ExpandFolders(List<string> paths)
        {
            List<string> result = new List<string>();
            foreach (string p in paths)
            {
                bool isDir = false;
                try { isDir = Directory.Exists(p); }
                catch (Exception) { }
                if (!isDir)
                {
                    result.Add(p);
                    continue;
                }
                try
                {
                    string[] entries = Directory.GetFileSystemEntries(p);
                    Array.Sort(entries, StringComparer.OrdinalIgnoreCase);
                    result.AddRange(entries);
                }
                catch (Exception) { }
            }
            return result;
        }

        // 把主窗口直接画到位图，用于离线检查布局与中文渲染（无需依赖桌面截屏）。
        private static int Shot(string outPath, List<string> files)
        {
            ShotLog(outPath, "enter, files=" + files.Count.ToString(CultureInfo.InvariantCulture));
            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
            {
                ShotLog(outPath, "thread-ex: " + e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                ShotLog(outPath, "domain-ex: " + e.ExceptionObject);
            };

            RenameForm f;
            try
            {
                f = new RenameForm(files.ToArray(), Rule.Load());
                ShotLog(outPath, "ctor ok " + f.ClientSize.Width.ToString(CultureInfo.InvariantCulture) + "x" + f.ClientSize.Height.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                ShotLog(outPath, "ctor threw: " + ex);
                return 3;
            }

            Timer t = new Timer();
            t.Interval = 900;
            t.Tick += delegate(object s, EventArgs e)
            {
                t.Stop();
                ShotLog(outPath, "tick");
                try
                {
                    using (Bitmap bmp = new Bitmap(f.ClientSize.Width, f.ClientSize.Height))
                    {
                        f.DrawToBitmap(bmp, new Rectangle(0, 0, f.ClientSize.Width, f.ClientSize.Height));
                        ShotLog(outPath, "drawn");
                        bmp.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
                        ShotLog(outPath, "saved");
                    }
                }
                catch (Exception ex)
                {
                    ShotLog(outPath, "capture threw: " + ex);
                }
                try { f.Close(); }
                catch (Exception) { }
            };

            try
            {
                t.Start();
                Application.Run(f);
                ShotLog(outPath, "loop done");
                return 0;
            }
            catch (Exception ex)
            {
                ShotLog(outPath, "run threw: " + ex);
                return 3;
            }
        }

        private static void ShotLog(string outPath, string msg)
        {
            try { File.AppendAllText(outPath + ".log.txt", msg + "\r\n", Encoding.UTF8); }
            catch (Exception) { }
        }

        private static bool Eq(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }
}
