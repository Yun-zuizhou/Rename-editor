// BatchRename — 开发期开关
// 说明见 README.md；C# 5 语法（csc 4.0 编译，勿用插值字符串 / ?. / nameof / 自动属性初始化器）

using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace DshBatchRename
{
    /// <summary>
    /// 只给开发和离线截图用的环境变量开关，正常运行完全不读它们。
    ///   DSHBR_THEME=light|dark   强制配色（不设则跟随系统）
    ///   DSHBR_SHOT_MODE=0..3     强制落到某个规则页签（0 按序号 1 查找替换 2 加前缀后缀 3 大小写清理）
    ///   DSHBR_SHOT_AFFIX=前缀    预填前缀，用来核对输入框能正常显示文字
    ///   DSHBR_SHOT_MANUAL=名字    把第一行标成手写名，用来核对「手动」标记
    ///   DSHBR_SHOT_EDIT=1         截图时直接进第一行的改名状态
    ///   DSHBR_DEBUG=1            把窗口尺寸/缩放写进 %TEMP%\dshbr-ui.log
    /// </summary>
    internal static class Dev
    {
        // 注意：空字符串和「没设置」等价（PowerShell 里 $env:X = $null 会留下一个空变量）
        public static string Env(string name)
        {
            string v;
            try { v = Environment.GetEnvironmentVariable(name); }
            catch (Exception) { return null; }
            return string.IsNullOrEmpty(v) ? null : v;
        }

        /// <summary>强制规则页签；没设或越界返回 -1。</summary>
        public static int ShotMode()
        {
            string v = Env("DSHBR_SHOT_MODE");
            if (string.IsNullOrEmpty(v)) return -1;
            int n;
            if (!int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return -1;
            return (n >= 0 && n <= 3) ? n : -1;
        }

        public static string ShotPrefix()
        {
            return Env("DSHBR_SHOT_AFFIX");
        }

        /// <summary>把第一行标成手写名，用来截图核对「手动」标记。</summary>
        public static string ShotManual()
        {
            return Env("DSHBR_SHOT_MANUAL");
        }

        /// <summary>截图时直接进第一行的改名状态，用来核对行内输入框。</summary>
        public static bool ShotEdit()
        {
            return Env("DSHBR_SHOT_EDIT") != null;
        }

        public static void Log(string msg)
        {
            if (Env("DSHBR_DEBUG") == null) return;
            try
            {
                File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + msg + "\r\n",
                    Encoding.UTF8);
            }
            catch (Exception) { }
        }
    }
}
