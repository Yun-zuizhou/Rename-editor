// 批量重命名 —— 单文件安装器
// 把主程序、菜单扩展 DLL、包清单、包图标全部嵌进这一个 exe；
// 运行后无需联网、无需管理员权限，双击即可装好 Win11 新式右键菜单。
//
//   RenameEditorSetup.exe              安装
//   RenameEditorSetup.exe --uninstall  卸载
//   RenameEditorSetup.exe --silent     静默（无界面，供脚本/CI 用）

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace RenameEditorSetup
{
    internal class Result
    {
        public bool Ok;
        public string Message;
    }

    internal class SetupForm : Form
    {
        private readonly Label _step;
        private readonly ProgressBar _bar;

        public SetupForm(bool uninstall)
        {
            Text = uninstall ? "卸载 批量重命名" : "安装 批量重命名";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(430, 132);
            Font = new Font("Microsoft YaHei UI", 9f);
            BackColor = Color.White;

            Label title = new Label();
            title.Text = uninstall ? "正在卸载「批量重命名」…" : "正在安装「批量重命名」…";
            title.Font = new Font(Font.FontFamily, 11.5f, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(27, 27, 27);
            title.SetBounds(20, 18, 390, 26);
            Controls.Add(title);

            _step = new Label();
            _step.Text = "准备中…";
            _step.ForeColor = Color.FromArgb(96, 96, 96);
            _step.SetBounds(20, 50, 390, 22);
            Controls.Add(_step);

            _bar = new ProgressBar();
            _bar.Style = ProgressBarStyle.Marquee;
            _bar.MarqueeAnimationSpeed = 26;
            _bar.SetBounds(20, 80, 390, 10);
            Controls.Add(_bar);
        }

        public void SetStep(string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke((MethodInvoker)delegate { SetStep(text); });
                return;
            }
            _step.Text = text;
        }
    }

    internal static class Program
    {
        private const string PackageName = "DSHBatchRename.Shell";
        private const string ExeName = "BatchRename.exe";

        private static string _dirOverride;

        private static string InstallDir
        {
            get
            {
                if (!string.IsNullOrEmpty(_dirOverride)) return _dirOverride;
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Programs\DSHBatchRename");
            }
        }

        // 资源名 → 安装位置（相对安装目录）
        private static readonly string[][] Payload = new string[][]
        {
            new string[] { "BatchRename.exe", "BatchRename.exe" },
            new string[] { "DSHBRShell.dll", @"Shell\DSHBRShell.dll" },
            new string[] { "AppxManifest.xml", "AppxManifest.xml" },
            new string[] { "StoreLogo.png", @"Assets\StoreLogo.png" },
            new string[] { "Square150x150Logo.png", @"Assets\Square150x150Logo.png" },
            new string[] { "Square44x44Logo.png", @"Assets\Square44x44Logo.png" }
        };

        [STAThread]
        private static int Main(string[] args)
        {
            bool uninstall = HasFlag(args, "--uninstall");
            bool silent = HasFlag(args, "--silent");
            _dirOverride = GetOption(args, "--dir");

            if (silent)
            {
                Result r = DoWork(uninstall, delegate(string s) { });
                Console.WriteLine(r.Message);
                return r.Ok ? 0 : 1;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            SetupForm form = new SetupForm(uninstall);
            Result result = null;

            form.Shown += delegate
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    result = DoWork(uninstall, form.SetStep);
                    try { form.BeginInvoke((MethodInvoker)delegate { form.Close(); }); }
                    catch (Exception) { }
                });
            };

            Application.Run(form);

            Result shown = result ?? new Result();
            if (shown.Message == null) shown.Message = "操作已取消。";
            MessageBox.Show(shown.Message,
                uninstall ? "卸载 批量重命名" : "安装 批量重命名",
                MessageBoxButtons.OK,
                shown.Ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
            return shown.Ok ? 0 : 1;
        }

        private static bool HasFlag(string[] args, string flag)
        {
            if (args == null) return false;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static string GetOption(string[] args, string name)
        {
            if (args == null) return null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            }
            return null;
        }

        private static Result DoWork(bool uninstall, Action<string> step)
        {
            Result r = new Result();
            try
            {
                if (uninstall) { Uninstall(step, r); return r; }
                Install(step, r);
            }
            catch (Exception ex)
            {
                r.Ok = false;
                r.Message = "失败：" + ex.Message;
            }
            return r;
        }

        private static void Install(Action<string> step, Result r)
        {
            step("正在关闭已运行的窗口…");
            KillRunning();

            step("正在释放程序文件…");
            ExtractPayload();

            step("正在注册右键菜单（Win11 新式菜单）…");
            string manifest = Path.Combine(InstallDir, "AppxManifest.xml");
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("$ErrorActionPreference = 'Stop'");
            sb.AppendLine("$name = '" + PackageName + "'");
            sb.AppendLine("$dev = Get-ItemProperty 'HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\AppModelUnlock' -ErrorAction SilentlyContinue");
            sb.AppendLine("if (-not $dev -or (($dev.AllowDevelopmentWithoutDevLicense -ne 1) -and ($dev.AllowAllTrustedApps -ne 1))) {");
            sb.AppendLine("  throw '未开启「开发者模式」或「旁加载应用」。请到 设置 → 系统 → 开发者选项 打开后重试。'");
            sb.AppendLine("}");
            sb.AppendLine("$old = Get-AppxPackage -Name $name -ErrorAction SilentlyContinue");
            sb.AppendLine("if ($old) { Remove-AppxPackage -Package $old.PackageFullName -ErrorAction Stop }");
            sb.AppendLine("Add-AppxPackage -Register '" + manifest.Replace("'", "''") + "' -ErrorAction Stop");
            sb.AppendLine("$p = Get-AppxPackage -Name $name -ErrorAction SilentlyContinue");
            sb.AppendLine("if (-not $p) { throw '注册后查不到包标识。' }");
            sb.AppendLine("Write-Output ('已注册: ' + $p.PackageFullName)");

            string output;
            int code = RunPowerShell(sb.ToString(), out output);
            if (code != 0)
            {
                // 新式菜单注册失败（例如没开开发者模式）：退回到经典菜单项，至少能用
                step("新式菜单未注册成功，正在改为安装经典菜单项…");
                string classic = @"
$ErrorActionPreference = 'Stop'
$exe = '@EXE@'
$cu = [Microsoft.Win32.Registry]::CurrentUser
$verbs = @(
    @{ Key = '*\shell\DshBatchRename'; Label = '批量重命名(&R)'; Cmd = '""' + $exe + '"" ""%1""'; Multi = $true },
    @{ Key = '*\shell\DshBatchRenameQuick'; Label = '快速重命名（沿用上次规则）(&Q)'; Cmd = '""' + $exe + '"" --quick ""%1""'; Multi = $true },
    @{ Key = 'Directory\shell\DshBatchRename'; Label = '批量重命名(&R)'; Cmd = '""' + $exe + '"" ""%1""'; Multi = $true },
    @{ Key = 'Directory\Background\shell\DshBatchRename'; Label = '批量重命名（本文件夹）(&R)'; Cmd = '""' + $exe + '"" --folder ""%V""'; Multi = $false },
    @{ Key = 'Directory\Background\shell\DshBatchRenameUndo'; Label = '撤销上次批量重命名(&U)'; Cmd = '""' + $exe + '"" --undo'; Multi = $false }
)
foreach ($v in $verbs) {
    $full = 'Software\Classes\' + $v.Key
    $k = $cu.CreateSubKey($full)
    if ($null -eq $k) { throw ('无法创建 HKCU\' + $full) }
    $k.SetValue('', $v.Label)
    $k.SetValue('Icon', $exe + ',0')
    if ($v.Multi) { $k.SetValue('MultiSelectModel', 'Player') }
    $ck = $k.CreateSubKey('command')
    $ck.SetValue('', $v.Cmd)
    $ck.Close()
    $k.Close()
}
Write-Output '已写入经典菜单项'
";
                classic = classic.Replace("@EXE@", Path.Combine(InstallDir, ExeName).Replace("'", "''"));

                string out2;
                int code2 = RunPowerShell(classic, out out2);
                if (code2 == 0)
                {
                    r.Ok = true;
                    r.Message = "已安装（经典菜单）。\r\n\r\n" +
                                "Win11 新式一级菜单没能注册成功，已自动改用经典菜单项：\r\n" +
                                "在资源管理器里选中文件 → 右键 → 「显示更多选项」（或按 Shift+F10）→「批量重命名」。\r\n\r\n" +
                                "新式菜单失败原因：\r\n" + Trim(output) +
                                "\r\n\r\n提示：到「设置 → 系统 → 开发者选项」打开开发者模式后，再次运行本安装程序即可启用一级菜单。" +
                                "\r\n\r\n安装位置：" + InstallDir;
                    return;
                }

                r.Ok = false;
                r.Message = "安装失败。\r\n\r\n新式菜单：" + Trim(output) + "\r\n\r\n经典菜单：" + Trim(out2) +
                            "\r\n\r\n程序本身已解压到：\r\n" + InstallDir +
                            "\r\n可以双击该目录下的 " + ExeName + " 直接使用（只是没有右键菜单）。";
                return;
            }

            r.Ok = true;
            r.Message = "安装完成。\r\n\r\n" +
                        "现在在资源管理器里选中文件后点右键，就能看到「批量重命名」。\r\n" +
                        "若菜单里暂时没有，等几秒或重启一次资源管理器。\r\n\r\n" +
                        "安装位置：" + InstallDir +
                        "\r\n\r\n卸载：再次运行本安装程序并加参数 --uninstall";
        }

        private static void Uninstall(Action<string> step, Result r)
        {
            step("正在关闭已运行的窗口…");
            KillRunning();

            step("正在移除右键菜单…");
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("$ErrorActionPreference = 'SilentlyContinue'");
            sb.AppendLine("$p = Get-AppxPackage -Name '" + PackageName + "'");
            sb.AppendLine("if ($p) { Remove-AppxPackage -Package $p.PackageFullName }");
            sb.AppendLine("Remove-Item 'HKCU:\\Software\\Classes\\*\\shell\\DshBatchRename' -Recurse -Force");
            sb.AppendLine("Remove-Item 'HKCU:\\Software\\Classes\\*\\shell\\DshBatchRenameQuick' -Recurse -Force");
            sb.AppendLine("Remove-Item 'HKCU:\\Software\\Classes\\Directory\\shell\\DshBatchRename' -Recurse -Force");
            sb.AppendLine("Remove-Item 'HKCU:\\Software\\Classes\\Directory\\shell\\DshBatchRenameQuick' -Recurse -Force");
            sb.AppendLine("Remove-Item 'HKCU:\\Software\\Classes\\Directory\\Background\\shell\\DshBatchRename' -Recurse -Force");
            sb.AppendLine("Remove-Item 'HKCU:\\Software\\Classes\\Directory\\Background\\shell\\DshBatchRenameUndo' -Recurse -Force");
            sb.AppendLine("Write-Output '已移除菜单'");

            string output;
            RunPowerShell(sb.ToString(), out output);

            step("正在删除程序文件…");
            try
            {
                if (Directory.Exists(InstallDir)) Directory.Delete(InstallDir, true);
            }
            catch (Exception)
            {
                r.Ok = false;
                r.Message = "菜单已移除，但删不掉安装目录（可能有文件正在使用）：\r\n" + InstallDir;
                return;
            }

            // 用户数据（撤销记录、上次规则）保留在 DSHBatchRenameData，不删
            r.Ok = true;
            r.Message = "已卸载。\r\n\r\n右键菜单已移除，程序文件已删除。\r\n" +
                        "撤销记录仍保留在 %LOCALAPPDATA%\\DSHBatchRenameData，需要时手动删除即可。";
        }

        private static void KillRunning()
        {
            Process[] list = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName));
            for (int i = 0; i < list.Length; i++)
            {
                try
                {
                    list[i].Kill();
                    list[i].WaitForExit(3000);
                }
                catch (Exception) { }
            }
        }

        private static void ExtractPayload()
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            if (!Directory.Exists(InstallDir)) Directory.CreateDirectory(InstallDir);

            for (int i = 0; i < Payload.Length; i++)
            {
                string resName = Payload[i][0];
                string rel = Payload[i][1];
                string target = Path.Combine(InstallDir, rel);
                string dir = Path.GetDirectoryName(target);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                using (Stream src = asm.GetManifestResourceStream(resName))
                {
                    if (src == null) throw new InvalidOperationException("安装包损坏，缺少内部资源：" + resName);
                    using (FileStream dst = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        src.CopyTo(dst);
                    }
                }
            }
        }

        private static int RunPowerShell(string script, out string output)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "powershell.exe";
            psi.Arguments = "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" +
                            script.Replace("\"", "\\\"") + "\"";
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;

            using (Process p = Process.Start(psi))
            {
                string outText = p.StandardOutput.ReadToEnd();
                string errText = p.StandardError.ReadToEnd();
                p.WaitForExit();
                output = (outText + "\r\n" + errText).Trim();
                return p.ExitCode;
            }
        }

        private static string Trim(string s)
        {
            if (s == null) return "";
            s = s.Trim();
            if (s.Length > 1200) s = s.Substring(0, 1200) + "…";
            return s;
        }
    }
}
