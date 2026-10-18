// BatchRename — 重命名引擎（预览、冲突校验、两阶段改名、撤销）
// 说明见 README.md；C# 5 语法（csc 4.0 编译，勿用插值字符串 / ?. / nameof / 自动属性初始化器）

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DshBatchRename
{
    internal static class Engine
    {
        public static readonly char[] IllegalChars = new char[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };

        private static readonly Regex TokenRx = new Regex("\\{(name|ext|parent|date|time|n|i)\\}", RegexOptions.IgnoreCase);

        public static List<RenameItem> Load(IEnumerable<string> paths)
        {
            List<RenameItem> list = new List<RenameItem>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (paths == null) return list;
            foreach (string p in paths)
            {
                if (string.IsNullOrEmpty(p)) continue;
                string full;
                try { full = Path.GetFullPath(p); }
                catch (Exception) { continue; }
                if (!seen.Add(full)) continue;
                bool isDir = false;
                bool isFile = false;
                try { isDir = Directory.Exists(full); isFile = File.Exists(full); }
                catch (Exception) { continue; }
                if (!isDir && !isFile) continue;

                string dir;
                try { dir = Path.GetDirectoryName(full); }
                catch (Exception) { continue; }
                if (string.IsNullOrEmpty(dir)) continue;
                string name = Path.GetFileName(full);
                if (string.IsNullOrEmpty(name)) continue;

                RenameItem it = new RenameItem();
                it.FullPath = full;
                it.Dir = dir;
                it.IsDir = isDir;
                it.OldName = name;
                if (isDir)
                {
                    it.OldBase = name;
                    it.Ext = string.Empty;
                }
                else
                {
                    it.OldBase = Path.GetFileNameWithoutExtension(name);
                    it.Ext = Path.GetExtension(name);
                }
                try
                {
                    if (isDir)
                    {
                        DirectoryInfo di = new DirectoryInfo(full);
                        it.MTime = di.LastWriteTime;
                        it.Size = 0;
                    }
                    else
                    {
                        FileInfo fi = new FileInfo(full);
                        it.MTime = fi.LastWriteTime;
                        it.Size = fi.Length;
                    }
                }
                catch (Exception) { it.MTime = DateTime.MinValue; it.Size = 0; }
                list.Add(it);
            }
            return list;
        }

        public static void Build(List<RenameItem> items, Rule rule)
        {
            SortItems(items, rule.SortKind);
            int n = rule.Start;
            for (int i = 0; i < items.Count; i++)
            {
                RenameItem it = items[i];
                it.Runnable = false;
                it.Status = string.Empty;
                if (it.Excluded)
                {
                    it.NewName = it.OldName;
                    it.Status = "已排除";
                    continue;
                }
                string newBase = ComputeBase(it, rule, n);
                it.NewName = BuildName(it, rule, newBase);
                n += rule.Step;
            }
            Validate(items);
        }

        private static void SortItems(List<RenameItem> items, int kind)
        {
            if (kind == 3) return;
            if (kind == 1)
            {
                items.Sort(delegate (RenameItem a, RenameItem b) { return a.MTime.CompareTo(b.MTime); });
                return;
            }
            if (kind == 2)
            {
                items.Sort(delegate (RenameItem a, RenameItem b) { return a.Size.CompareTo(b.Size); });
                return;
            }
            items.Sort(delegate (RenameItem a, RenameItem b)
            {
                int c = NaturalCompare(a.OldName, b.OldName);
                if (c != 0) return c;
                return string.Compare(a.FullPath, b.FullPath, StringComparison.OrdinalIgnoreCase);
            });
        }

        public static int NaturalCompare(string a, string b)
        {
            if (a == null) a = string.Empty;
            if (b == null) b = string.Empty;
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                char ca = a[i];
                char cb = b[j];
                bool da = char.IsDigit(ca);
                bool db = char.IsDigit(cb);
                if (da && db)
                {
                    int si = i, sj = j;
                    while (i < a.Length && char.IsDigit(a[i])) i++;
                    while (j < b.Length && char.IsDigit(b[j])) j++;
                    string na = a.Substring(si, i - si).TrimStart('0');
                    string nb = b.Substring(sj, j - sj).TrimStart('0');
                    if (na.Length != nb.Length) return na.Length - nb.Length;
                    int c = string.CompareOrdinal(na, nb);
                    if (c != 0) return c;
                }
                else
                {
                    int c = char.ToUpperInvariant(ca).CompareTo(char.ToUpperInvariant(cb));
                    if (c != 0) return c;
                    i++;
                    j++;
                }
            }
            return (a.Length - i) - (b.Length - j);
        }

        private static string ComputeBase(RenameItem it, Rule rule, int n)
        {
            switch (rule.Mode)
            {
                case RenameMode.Replace:
                    {
                        string find = rule.Find == null ? string.Empty : rule.Find;
                        if (find.Length == 0) return it.OldBase;
                        string pattern = rule.UseRegex ? find : Regex.Escape(find);
                        string repl = rule.ReplaceWith == null ? string.Empty : rule.ReplaceWith;
                        if (!rule.UseRegex) repl = repl.Replace("$", "$$");
                        RegexOptions opts = rule.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
                        try { return Regex.Replace(it.OldBase, pattern, repl, opts); }
                        catch (Exception) { return it.OldBase; }
                    }
                case RenameMode.Affix:
                    return (rule.Prefix == null ? string.Empty : rule.Prefix) + it.OldBase + (rule.Suffix == null ? string.Empty : rule.Suffix);
                case RenameMode.Case:
                    return ApplyCase(it.OldBase, rule.CaseKind);
                default:
                    return ExpandTemplate(rule.Template, it, n, rule.Pad);
            }
        }

        private static string BuildName(RenameItem it, Rule rule, string newBase)
        {
            if (rule.Mode == RenameMode.Sequence)
            {
                bool hasExtToken = false;
                try { hasExtToken = Regex.IsMatch(rule.Template == null ? string.Empty : rule.Template, "\\{ext\\}", RegexOptions.IgnoreCase); }
                catch (Exception) { }
                if (hasExtToken) return newBase;
            }
            return newBase + it.Ext;
        }

        public static string ExpandTemplate(string tpl, RenameItem it, int n, int pad)
        {
            if (tpl == null) tpl = string.Empty;
            if (pad < 0) pad = 0;
            string num = n.ToString(CultureInfo.InvariantCulture);
            if (pad > num.Length) num = new string('0', pad - num.Length) + num;
            string parentName = string.Empty;
            try
            {
                string d = it.Dir == null ? string.Empty : it.Dir.TrimEnd('\\');
                parentName = Path.GetFileName(d);
            }
            catch (Exception) { }
            string mtimeDate = it.MTime == DateTime.MinValue ? "00000000" : it.MTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            string mtimeTime = it.MTime == DateTime.MinValue ? "000000" : it.MTime.ToString("HHmmss", CultureInfo.InvariantCulture);
            return TokenRx.Replace(tpl, delegate (Match m)
            {
                string k = m.Groups[1].Value.ToLowerInvariant();
                switch (k)
                {
                    case "name": return it.OldBase;
                    case "ext": return it.Ext.TrimStart('.');
                    case "parent": return parentName;
                    case "date": return mtimeDate;
                    case "time": return mtimeTime;
                    default: return num;
                }
            });
        }

        public static string ApplyCase(string s, int kind)
        {
            if (s == null) return string.Empty;
            switch (kind)
            {
                case 0: return s.ToLowerInvariant();
                case 1: return s.ToUpperInvariant();
                case 2: return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());
                case 3: return CleanName(s);
                case 4: return Regex.Replace(s, "\\s+", "_");
                default: return s;
            }
        }

        public static string CleanName(string s)
        {
            if (s == null) return string.Empty;
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c < 32) continue;
                sb.Append(Array.IndexOf(IllegalChars, c) >= 0 ? '_' : c);
            }
            string t = Regex.Replace(sb.ToString(), "[ ]{2,}", " ");
            t = t.Trim();
            while (t.Length > 0 && t[t.Length - 1] == '.') t = t.Substring(0, t.Length - 1).TrimEnd(' ');
            return t;
        }

        private static void Validate(List<RenameItem> items)
        {
            foreach (RenameItem it in items)
            {
                if (it.Excluded) continue;
                if (it.NewName == null) it.NewName = it.OldName;
                if (string.Equals(it.NewName, it.OldName, StringComparison.Ordinal))
                {
                    it.Status = "未变化";
                    it.Runnable = false;
                    continue;
                }
                string err = CheckFileName(it.NewName);
                if (err != null)
                {
                    it.Status = err;
                    it.Runnable = false;
                    continue;
                }
                it.Runnable = true;
                it.Status = string.Empty;
            }

            Dictionary<string, List<RenameItem>> map = new Dictionary<string, List<RenameItem>>(StringComparer.OrdinalIgnoreCase);
            foreach (RenameItem it in items)
            {
                if (!it.Runnable) continue;
                string key = it.Dir + "|" + it.NewName;
                List<RenameItem> bucket;
                if (!map.TryGetValue(key, out bucket))
                {
                    bucket = new List<RenameItem>();
                    map[key] = bucket;
                }
                bucket.Add(it);
            }
            foreach (KeyValuePair<string, List<RenameItem>> kv in map)
            {
                if (kv.Value.Count > 1)
                {
                    foreach (RenameItem it in kv.Value)
                    {
                        it.Runnable = false;
                        it.Status = "重名冲突";
                    }
                }
            }

            HashSet<string> sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (RenameItem it in items) if (it.Runnable) sources.Add(it.FullPath);
            foreach (RenameItem it in items)
            {
                if (!it.Runnable) continue;
                string target = it.NewFullPath;
                if (sources.Contains(target)) continue;
                bool exists = false;
                try { exists = File.Exists(target) || Directory.Exists(target); }
                catch (Exception) { }
                if (exists)
                {
                    it.Runnable = false;
                    it.Status = "目标已存在";
                }
            }
        }

        public static string CheckFileName(string name)
        {
            if (name == null || name.Trim().Length == 0) return "名称为空";
            if (name.Length > 200) return "名称过长";
            if (name.IndexOfAny(IllegalChars) >= 0) return "含非法字符";
            if (name[name.Length - 1] == ' ' || name[name.Length - 1] == '.') return "结尾含空格或点";
            for (int i = 0; i < name.Length; i++) if (name[i] < 32) return "含控制字符";
            string stem = name;
            int dot = stem.IndexOf('.');
            if (dot > 0) stem = stem.Substring(0, dot);
            stem = stem.Trim().ToUpperInvariant();
            if (stem.Length > 0)
            {
                if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL") return "系统保留名";
                if (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] >= '1' && stem[3] <= '9') return "系统保留名";
            }
            return null;
        }

        private class Staged
        {
            public string Orig;
            public string Temp;
            public string Final;
            public bool IsDir;
        }

        public static int Apply(List<RenameItem> items, out string message)
        {
            message = string.Empty;
            List<RenameItem> todo = new List<RenameItem>();
            foreach (RenameItem it in items) if (it.Runnable) todo.Add(it);
            if (todo.Count == 0)
            {
                message = "没有可重命名的项目";
                return 0;
            }

            List<Staged> staged = new List<Staged>();
            try
            {
                foreach (RenameItem it in todo)
                {
                    string temp = Path.Combine(it.Dir, "~dshbr" + Guid.NewGuid().ToString("N").Substring(0, 10) + ".tmp");
                    MovePath(it.FullPath, temp, it.IsDir);
                    Staged s = new Staged();
                    s.Orig = it.FullPath;
                    s.Temp = temp;
                    s.Final = it.NewFullPath;
                    s.IsDir = it.IsDir;
                    staged.Add(s);
                }
                foreach (Staged s in staged)
                {
                    MovePath(s.Temp, s.Final, s.IsDir);
                }
            }
            catch (Exception ex)
            {
                Rollback(staged);
                message = "重命名失败：" + ex.Message;
                return -1;
            }
            WriteUndoLog(staged);
            return staged.Count;
        }

        private static void Rollback(List<Staged> staged)
        {
            for (int i = staged.Count - 1; i >= 0; i--)
            {
                Staged s = staged[i];
                try
                {
                    if (Exists(s.Final, s.IsDir))
                    {
                        if (!Exists(s.Orig, s.IsDir)) MovePath(s.Final, s.Orig, s.IsDir);
                    }
                    else if (Exists(s.Temp, s.IsDir))
                    {
                        if (!Exists(s.Orig, s.IsDir)) MovePath(s.Temp, s.Orig, s.IsDir);
                    }
                }
                catch (Exception) { }
            }
        }

        private static bool Exists(string p, bool isDir)
        {
            if (string.IsNullOrEmpty(p)) return false;
            try { return isDir ? Directory.Exists(p) : File.Exists(p); }
            catch (Exception) { return false; }
        }

        public static void MovePath(string from, string to, bool isDir)
        {
            if (isDir) Directory.Move(from, to);
            else File.Move(from, to);
        }

        private static void WriteUndoLog(List<Staged> staged)
        {
            try
            {
                AppInfo.EnsureDirs();
                StringBuilder sb = new StringBuilder();
                foreach (Staged s in staged) sb.AppendLine(s.Orig + "\t" + s.Final);
                string name = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + ".tsv";
                File.WriteAllText(Path.Combine(AppInfo.UndoDir, name), sb.ToString(), new UTF8Encoding(false));
                PruneUndo(20);
            }
            catch (Exception) { }
        }

        private static void PruneUndo(int keep)
        {
            try
            {
                string[] files = Directory.GetFiles(AppInfo.UndoDir, "*.tsv");
                if (files.Length <= keep) return;
                Array.Sort(files, StringComparer.Ordinal);
                for (int i = 0; i < files.Length - keep; i++)
                {
                    try { File.Delete(files[i]); }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
        }

        private class UndoRec
        {
            public string Orig;
            public string Final;
            public string Temp;
            public bool IsDir;
        }

        public static int UndoLast(out string message)
        {
            message = string.Empty;
            List<UndoRec> recs = new List<UndoRec>();
            string latest = null;
            try
            {
                AppInfo.EnsureDirs();
                string[] files = Directory.GetFiles(AppInfo.UndoDir, "*.tsv");
                if (files.Length == 0)
                {
                    message = "没有可撤销的记录";
                    return 0;
                }
                Array.Sort(files, StringComparer.Ordinal);
                latest = files[files.Length - 1];
                foreach (string line in File.ReadAllLines(latest, Encoding.UTF8))
                {
                    if (string.IsNullOrEmpty(line)) continue;
                    int tab = line.IndexOf('\t');
                    if (tab <= 0) continue;
                    UndoRec r = new UndoRec();
                    r.Orig = line.Substring(0, tab);
                    r.Final = line.Substring(tab + 1);
                    recs.Add(r);
                }
                if (recs.Count == 0)
                {
                    message = "撤销记录为空";
                    return 0;
                }
            }
            catch (Exception ex)
            {
                message = "撤销失败：" + ex.Message;
                return -1;
            }

            // 两阶段还原：先把现有文件挪到临时名，再挪回原名，避免 A→B、B→C 这类链条互相占用。
            int done = 0, skipped = 0;
            try
            {
                foreach (UndoRec r in recs)
                {
                    r.IsDir = false;
                    try { r.IsDir = Directory.Exists(r.Final); }
                    catch (Exception) { }
                    if (!Exists(r.Final, r.IsDir))
                    {
                        r.Temp = null;
                        continue;
                    }
                    string dir = Path.GetDirectoryName(r.Final);
                    if (string.IsNullOrEmpty(dir)) dir = Path.GetDirectoryName(r.Orig);
                    r.Temp = Path.Combine(dir, "~dshbr-undo" + Guid.NewGuid().ToString("N").Substring(0, 10) + ".tmp");
                    MovePath(r.Final, r.Temp, r.IsDir);
                }
                foreach (UndoRec r in recs)
                {
                    if (r.Temp == null)
                    {
                        skipped++;
                        continue;
                    }
                    if (Exists(r.Orig, r.IsDir))
                    {
                        try { MovePath(r.Temp, r.Final, r.IsDir); }
                        catch (Exception) { }
                        skipped++;
                    }
                    else
                    {
                        MovePath(r.Temp, r.Orig, r.IsDir);
                        done++;
                    }
                }
            }
            catch (Exception ex)
            {
                for (int i = recs.Count - 1; i >= 0; i--)
                {
                    UndoRec r = recs[i];
                    if (r.Temp == null) continue;
                    try
                    {
                        if (Exists(r.Temp, r.IsDir) && !Exists(r.Final, r.IsDir)) MovePath(r.Temp, r.Final, r.IsDir);
                    }
                    catch (Exception) { }
                }
                message = "撤销失败：" + ex.Message;
                return -1;
            }

            if (skipped == 0)
            {
                try { File.Delete(latest); }
                catch (Exception) { }
            }
            message = "已撤销 " + done.ToString(CultureInfo.InvariantCulture) + " 项";
            if (skipped > 0) message += "，跳过 " + skipped.ToString(CultureInfo.InvariantCulture) + " 项";
            return done;
        }
    }
}
