// BatchRename — 内置自测（--selftest，24 项）
// 说明见 README.md；C# 5 语法（csc 4.0 编译，勿用插值字符串 / ?. / nameof / 自动属性初始化器）

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace DshBatchRename
{
    internal static class SelfTest
    {
        private static Rule SeqRule()
        {
            Rule r = new Rule();
            r.Mode = RenameMode.Sequence;
            return r;
        }

        private static StringBuilder _log;
        private static int _fail;

        private static void Check(string name, bool ok, string detail)
        {
            string line = (ok ? "PASS " : "FAIL ") + name;
            if (!ok && !string.IsNullOrEmpty(detail)) line += " :: " + detail;
            _log.AppendLine(line);
            if (!ok) _fail++;
        }

        public static int Run(string outFile)
        {
            _log = new StringBuilder();
            _fail = 0;
            string root = Path.Combine(Path.GetTempPath(), "dshbr-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                Directory.CreateDirectory(root);
                AppInfo.UseForTesting(Path.Combine(root, "_data"));
                TestNames();
                TestSequence(root);
                TestNaturalSort(root);
                TestReplacePlain(root);
                TestReplaceRegex(root);
                TestReplaceIgnoreCase(root);
                TestAffix(root);
                TestCaseModes(root);
                TestClean(root);
                TestConflictExisting(root);
                TestRotationAndUndo(root);
                TestDuplicate(root);
                TestInvalid(root);
                TestDirectory(root);
                TestManualName(root);
                TestFreeMode(root);
                TestMixedManual(root);
            }
            catch (Exception ex)
            {
                _log.AppendLine("FAIL fatal :: " + ex.ToString().Replace("\r", " ").Replace("\n", " "));
                _fail++;
            }
            finally
            {
                try { Directory.Delete(root, true); }
                catch (Exception) { }
            }
            _log.AppendLine(_fail == 0 ? "RESULT: ALL PASS" : "RESULT: FAILED " + _fail.ToString(CultureInfo.InvariantCulture));
            string text = _log.ToString();
            if (!string.IsNullOrEmpty(outFile))
            {
                try { File.WriteAllText(outFile, text, new UTF8Encoding(true)); }
                catch (Exception) { }
            }
            return _fail == 0 ? 0 : 1;
        }

        private static string Dir(string root, string name)
        {
            string d = Path.Combine(root, name);
            Directory.CreateDirectory(d);
            return d;
        }

        private static void Touch(string dir, params string[] names)
        {
            foreach (string n in names) File.WriteAllText(Path.Combine(dir, n), "x");
        }

        private static string[] Entries(string dir)
        {
            string[] all = Directory.GetFileSystemEntries(dir);
            List<string> names = new List<string>();
            foreach (string p in all) names.Add(Path.GetFileName(p));
            names.Sort(StringComparer.Ordinal);
            return names.ToArray();
        }

        private static string Join(string[] a)
        {
            return string.Join(" | ", a);
        }

        private static void Expect(string testName, string dir, params string[] expected)
        {
            List<string> e = new List<string>(expected);
            e.Sort(StringComparer.Ordinal);
            string[] actual = Entries(dir);
            bool ok = actual.Length == e.Count;
            if (ok)
            {
                for (int i = 0; i < actual.Length; i++)
                {
                    if (!string.Equals(actual[i], e[i], StringComparison.Ordinal))
                    {
                        ok = false;
                        break;
                    }
                }
            }
            Check(testName, ok, "期望[" + Join(e.ToArray()) + "] 实际[" + Join(actual) + "]");
        }

        private static List<RenameItem> BuildRule(string dir, Rule r)
        {
            List<RenameItem> items = Engine.Load(Directory.GetFileSystemEntries(dir));
            Engine.Build(items, r);
            return items;
        }

        private static int ApplyRule(string dir, Rule r)
        {
            List<RenameItem> items = BuildRule(dir, r);
            string msg;
            return Engine.Apply(items, out msg);
        }

        private static void TestNames()
        {
            bool ok = Engine.CheckFileName("a b.txt") == null
                && Engine.CheckFileName("a<b") != null
                && Engine.CheckFileName("CON.txt") != null
                && Engine.CheckFileName("x.txt.") != null
                && Engine.CheckFileName("  ") != null
                && Engine.CheckFileName("普通 名称.txt") == null;
            Check("文件名合法性校验", ok, string.Empty);
        }

        private static void TestSequence(string root)
        {
            string d = Dir(root, "t1");
            Touch(d, "a.txt", "b.txt", "c.txt");
            Rule r = SeqRule();
            int n = ApplyRule(d, r);
            Check("序号重命名应用数", n == 3, "n=" + n.ToString(CultureInfo.InvariantCulture));
            Expect("序号重命名 {name}_{n}", d, "a_01.txt", "b_02.txt", "c_03.txt");
        }

        private static void TestNaturalSort(string root)
        {
            string d = Dir(root, "t2");
            Touch(d, "2.txt", "10.txt", "1.txt");
            Rule r = SeqRule();
            r.Template = "{name}-{n}";
            ApplyRule(d, r);
            Expect("自然排序 1<2<10", d, "1-01.txt", "2-02.txt", "10-03.txt");
        }

        private static void TestReplacePlain(string root)
        {
            string d = Dir(root, "t3");
            Touch(d, "IMG_001.jpg", "IMG_002.jpg");
            Rule r = SeqRule();
            r.Mode = RenameMode.Replace;
            r.Find = "IMG_";
            r.ReplaceWith = "照片_";
            ApplyRule(d, r);
            Expect("纯文本查找替换", d, "照片_001.jpg", "照片_002.jpg");
        }

        private static void TestReplaceRegex(string root)
        {
            string d = Dir(root, "t4");
            Touch(d, "abc123.txt");
            Rule r = SeqRule();
            r.Mode = RenameMode.Replace;
            r.UseRegex = true;
            r.Find = "([a-z]+)([0-9]+)";
            r.ReplaceWith = "$2-$1";
            ApplyRule(d, r);
            Expect("正则替换与 $1 分组", d, "123-abc.txt");
        }

        private static void TestReplaceIgnoreCase(string root)
        {
            string d = Dir(root, "t5");
            Touch(d, "Photo.PNG");
            Rule r = SeqRule();
            r.Mode = RenameMode.Replace;
            r.Find = "photo";
            r.ReplaceWith = "Image";
            r.CaseSensitive = false;
            ApplyRule(d, r);
            Expect("不区分大小写替换并保留扩展名", d, "Image.PNG");
        }

        private static void TestAffix(string root)
        {
            string d = Dir(root, "t6");
            Touch(d, "x.txt");
            Rule r = SeqRule();
            r.Mode = RenameMode.Affix;
            r.Prefix = "2024_";
            r.Suffix = "_ok";
            ApplyRule(d, r);
            Expect("前后缀（后缀在扩展名前）", d, "2024_x_ok.txt");
        }

        private static void TestCaseModes(string root)
        {
            string d = Dir(root, "t7");
            Touch(d, "MiXeD.TXT");
            Rule r = SeqRule();
            r.Mode = RenameMode.Case;
            r.CaseKind = 0;
            ApplyRule(d, r);
            Expect("全部小写", d, "mixed.TXT");

            Rule r2 = SeqRule();
            r2.Mode = RenameMode.Case;
            r2.CaseKind = 1;
            ApplyRule(d, r2);
            Expect("全部大写", d, "MIXED.TXT");
        }

        private static void TestClean(string root)
        {
            string d = Dir(root, "t8");
            Touch(d, "  a  b.txt");
            Rule r = SeqRule();
            r.Mode = RenameMode.Case;
            r.CaseKind = 3;
            ApplyRule(d, r);
            Expect("清理：去首尾空格与重复空格", d, "a b.txt");

            string d2 = Dir(root, "t8b");
            Touch(d2, "a b.txt");
            Rule r2 = SeqRule();
            r2.Mode = RenameMode.Case;
            r2.CaseKind = 4;
            ApplyRule(d2, r2);
            Expect("空格转下划线", d2, "a_b.txt");
        }

        private static void TestConflictExisting(string root)
        {
            string d = Dir(root, "t9");
            Touch(d, "a.txt", "c.txt");
            Rule r = SeqRule();
            r.Mode = RenameMode.Replace;
            r.Find = "a";
            r.ReplaceWith = "c";
            List<RenameItem> items = BuildRule(d, r);
            int blocked = 0;
            foreach (RenameItem it in items) if (!it.Runnable && it.Status == "目标已存在") blocked++;
            Check("目标已存在时阻止", blocked == 1, "blocked=" + blocked.ToString(CultureInfo.InvariantCulture));
            string msg;
            int n = Engine.Apply(items, out msg);
            Check("冲突时不执行任何重命名", n == 0, "n=" + n.ToString(CultureInfo.InvariantCulture));
            Expect("冲突后文件保持不变", d, "a.txt", "c.txt");
        }

        private static void TestRotationAndUndo(string root)
        {
            string d = Dir(root, "t10");
            Touch(d, "1.txt", "2.txt", "3.txt");
            try
            {
                foreach (string f in Directory.GetFiles(AppInfo.UndoDir, "*.tsv")) File.Delete(f);
            }
            catch (Exception) { }
            Rule r = SeqRule();
            r.Template = "{n}";
            r.Pad = 0;
            r.Start = 2;
            int n = ApplyRule(d, r);
            Check("链式改名应用数", n == 3, "n=" + n.ToString(CultureInfo.InvariantCulture));
            Expect("1→2 / 2→3 / 3→4 链式改名", d, "2.txt", "3.txt", "4.txt");

            string msg;
            int u = Engine.UndoLast(out msg);
            Check("撤销返回数", u == 3, "u=" + u.ToString(CultureInfo.InvariantCulture) + " msg=" + msg);
            Expect("撤销后恢复原名", d, "1.txt", "2.txt", "3.txt");
        }

        private static void TestDuplicate(string root)
        {
            string d = Dir(root, "t11");
            Touch(d, "a.txt", "b.txt");
            Rule r = SeqRule();
            r.Template = "same";
            r.Pad = 0;
            List<RenameItem> items = BuildRule(d, r);
            int dup = 0;
            foreach (RenameItem it in items) if (it.Status == "重名冲突") dup++;
            Check("同批次重名冲突", dup == 2, "dup=" + dup.ToString(CultureInfo.InvariantCulture));
            string msg;
            int n = Engine.Apply(items, out msg);
            Check("重名冲突时不执行", n == 0, "n=" + n.ToString(CultureInfo.InvariantCulture));
        }

        private static void TestInvalid(string root)
        {
            string d = Dir(root, "t12");
            Touch(d, "x.txt");
            Rule r = SeqRule();
            r.Mode = RenameMode.Replace;
            r.Find = "x";
            r.ReplaceWith = "a:b";
            List<RenameItem> items = BuildRule(d, r);
            string st = items.Count == 1 ? items[0].Status : "count=" + items.Count.ToString(CultureInfo.InvariantCulture);
            Check("非法字符被识别", items.Count == 1 && items[0].Status == "含非法字符", st);
            Expect("非法名称不落盘", d, "x.txt");
        }

        // 手动改名：整名照用，且不占用序号
        private static void TestManualName(string root)
        {
            string d = Dir(root, "t14");
            Touch(d, "a.txt", "b.txt", "c.txt");
            Rule r = SeqRule();
            r.Template = "{name}_{n}";
            List<RenameItem> items = Engine.Load(Directory.GetFileSystemEntries(d));
            foreach (RenameItem it in items) if (it.OldName == "b.txt") { it.Manual = true; it.ManualName = "特殊 名字.txt"; }
            Engine.Build(items, r);
            string msg;
            int n = Engine.Apply(items, out msg);
            Check("手动改名应用数", n == 3, "n=" + n.ToString(CultureInfo.InvariantCulture) + " msg=" + msg);
            Expect("手动改名与序号并存", d, "a_01.txt", "特殊 名字.txt", "c_02.txt");
        }

        // 自由命名模式：没手改的行保持原名
        private static void TestFreeMode(string root)
        {
            string d = Dir(root, "t15");
            Touch(d, "x.txt", "y.txt");
            Rule r = SeqRule();
            r.Mode = RenameMode.Free;
            List<RenameItem> items = Engine.Load(Directory.GetFileSystemEntries(d));
            Engine.Build(items, r);
            int will = 0;
            foreach (RenameItem it in items) if (it.Runnable) will++;
            Check("自由命名模式默认不产生改动", will == 0, "will=" + will.ToString(CultureInfo.InvariantCulture));
            foreach (RenameItem it in items) if (it.OldName == "y.txt") { it.Manual = true; it.ManualName = "只改这一条.txt"; }
            Engine.Build(items, r);
            string msg;
            int n = Engine.Apply(items, out msg);
            bool ok = n == 1 && File.Exists(Path.Combine(d, "只改这一条.txt")) && File.Exists(Path.Combine(d, "x.txt"));
            Check("自由命名只改手写的那条", ok, "n=" + n.ToString(CultureInfo.InvariantCulture) + " msg=" + msg);
        }

        // 从别的模式切回自由命名后，手写过的行仍然保留
        private static void TestMixedManual(string root)
        {
            string d = Dir(root, "t16");
            Touch(d, "m.txt", "n.txt");
            Rule seq = SeqRule();
            seq.Template = "{name}_{n}";
            List<RenameItem> items = Engine.Load(Directory.GetFileSystemEntries(d));
            foreach (RenameItem it in items) if (it.OldName == "n.txt") { it.Manual = true; it.ManualName = "n2.txt"; }
            Engine.Build(items, seq);
            string got = string.Empty;
            foreach (RenameItem it in items) got += it.OldName + "→" + it.NewName + " ";
            Check("切模式后手写名仍保留", got.Contains("n.txt→n2.txt") && got.Contains("m.txt→m_01.txt"), got);
        }

        private static void TestDirectory(string root)
        {
            string d = Dir(root, "t13");
            string sub = Path.Combine(d, "sub1");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(sub, "in.txt"), "x");
            Rule r = SeqRule();
            r.Mode = RenameMode.Affix;
            r.Prefix = "new_";
            List<RenameItem> items = Engine.Load(new string[] { sub });
            Engine.Build(items, r);
            string msg;
            int n = Engine.Apply(items, out msg);
            bool ok = n == 1
                && Directory.Exists(Path.Combine(d, "new_sub1"))
                && File.Exists(Path.Combine(d, "new_sub1", "in.txt"));
            Check("文件夹重命名", ok, "n=" + n.ToString(CultureInfo.InvariantCulture) + " msg=" + msg);
            Expect("文件夹改名后父目录内容", d, "new_sub1");
        }
    }
}
