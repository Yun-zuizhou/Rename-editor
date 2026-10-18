// BatchRename — 数据模型（重命名模式、应用目录、规则、单个待改项）
// 说明见 README.md；C# 5 语法（csc 4.0 编译，勿用插值字符串 / ?. / nameof / 自动属性初始化器）

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DshBatchRename
{
    internal enum RenameMode
    {
        Sequence = 0,
        Replace = 1,
        Affix = 2,
        Case = 3
    }

    internal static class AppInfo
    {
        private static string _home;
        private static string _undoDir;
        private static string _settingsPath;

        public static string Home { get { return _home; } }
        public static string UndoDir { get { return _undoDir; } }
        public static string SettingsPath { get { return _settingsPath; } }

        static AppInfo()
        {
            ResolveDefault();
        }

        private static void ResolveDefault()
        {
            List<string> candidates = new List<string>();
            try
            {
                string lad = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(lad)) candidates.Add(Path.Combine(lad, "DSHBatchRename"));
            }
            catch (Exception) { }
            candidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "userdata"));

            foreach (string c in candidates)
            {
                try
                {
                    Directory.CreateDirectory(c);
                    Directory.CreateDirectory(Path.Combine(c, "undo"));
                    Bind(c);
                    return;
                }
                catch (Exception) { }
            }
            Bind(candidates[candidates.Count - 1]);
        }

        private static void Bind(string home)
        {
            _home = home;
            _undoDir = Path.Combine(home, "undo");
            _settingsPath = Path.Combine(home, "settings.txt");
        }

        public static void UseForTesting(string home)
        {
            try { Directory.CreateDirectory(home); Directory.CreateDirectory(Path.Combine(home, "undo")); } catch (Exception) { }
            Bind(home);
        }

        public static void EnsureDirs()
        {
            try { Directory.CreateDirectory(_home); Directory.CreateDirectory(_undoDir); }
            catch (Exception) { }
        }
    }

    internal class Rule
    {
        public RenameMode Mode;
        public string Template;
        public int Start;
        public int Step;
        public int Pad;
        public string Find;
        public string ReplaceWith;
        public bool UseRegex;
        public bool CaseSensitive;
        public string Prefix;
        public string Suffix;
        public int CaseKind;
        public int SortKind;

        public Rule()
        {
            Mode = RenameMode.Affix;          // 用户最常用的规则：加前缀/后缀
            Template = "{name}_{n}";
            Start = 1;
            Step = 1;
            Pad = 2;
            Find = string.Empty;
            ReplaceWith = string.Empty;
            UseRegex = false;
            CaseSensitive = true;
            Prefix = string.Empty;
            Suffix = string.Empty;
            CaseKind = 0;
            SortKind = 0;
        }

        public void Save()
        {
            try
            {
                AppInfo.EnsureDirs();
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("version=1");
                sb.AppendLine("mode=" + ((int)Mode).ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("template=" + Uri.EscapeDataString(Template == null ? string.Empty : Template));
                sb.AppendLine("start=" + Start.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("step=" + Step.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("pad=" + Pad.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("find=" + Uri.EscapeDataString(Find == null ? string.Empty : Find));
                sb.AppendLine("replace=" + Uri.EscapeDataString(ReplaceWith == null ? string.Empty : ReplaceWith));
                sb.AppendLine("regex=" + (UseRegex ? "1" : "0"));
                sb.AppendLine("casesensitive=" + (CaseSensitive ? "1" : "0"));
                sb.AppendLine("prefix=" + Uri.EscapeDataString(Prefix == null ? string.Empty : Prefix));
                sb.AppendLine("suffix=" + Uri.EscapeDataString(Suffix == null ? string.Empty : Suffix));
                sb.AppendLine("casekind=" + CaseKind.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("sort=" + SortKind.ToString(CultureInfo.InvariantCulture));
                File.WriteAllText(AppInfo.SettingsPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        public static Rule Load()
        {
            Rule r = new Rule();
            try
            {
                if (!File.Exists(AppInfo.SettingsPath)) return r;
                foreach (string raw in File.ReadAllLines(AppInfo.SettingsPath, Encoding.UTF8))
                {
                    int idx = raw.IndexOf('=');
                    if (idx <= 0) continue;
                    string k = raw.Substring(0, idx).Trim().ToLowerInvariant();
                    string v = raw.Substring(idx + 1);
                    switch (k)
                    {
                        case "mode": r.Mode = (RenameMode)ParseInt(v, (int)r.Mode); break;
                        case "template": r.Template = Uri.UnescapeDataString(v); break;
                        case "start": r.Start = ParseInt(v, r.Start); break;
                        case "step": r.Step = ParseInt(v, r.Step); break;
                        case "pad": r.Pad = ParseInt(v, r.Pad); break;
                        case "find": r.Find = Uri.UnescapeDataString(v); break;
                        case "replace": r.ReplaceWith = Uri.UnescapeDataString(v); break;
                        case "regex": r.UseRegex = v.Trim() == "1"; break;
                        case "casesensitive": r.CaseSensitive = v.Trim() == "1"; break;
                        case "prefix": r.Prefix = Uri.UnescapeDataString(v); break;
                        case "suffix": r.Suffix = Uri.UnescapeDataString(v); break;
                        case "casekind": r.CaseKind = ParseInt(v, r.CaseKind); break;
                        case "sort": r.SortKind = ParseInt(v, r.SortKind); break;
                    }
                }
            }
            catch (Exception) { }
            return r;
        }

        private static int ParseInt(string s, int fallback)
        {
            int v;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
            return fallback;
        }
    }

    internal class RenameItem
    {
        public string FullPath;
        public string Dir;
        public string OldName;
        public string OldBase;
        public string Ext;
        public string NewName;
        public string Status = string.Empty;
        public bool Excluded;
        public bool Runnable;
        public bool IsDir;
        public bool Manual;          // 这一条被手动改过名，规则不再覆盖它
        public string ManualName;    // 手动指定的完整新文件名（含扩展名）
        public DateTime MTime;
        public long Size;

        public string NewFullPath
        {
            get { return Path.Combine(Dir, NewName == null ? OldName : NewName); }
        }
    }
}
