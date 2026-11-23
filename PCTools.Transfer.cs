// 导入导出：.pct 数据包读写、SQL 导入导出与页面
// PCTools - Windows 10/11 System Manager
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;

namespace PCTools
{
    // .pct 中的一行：备份/建议使用 Key+Name+Value/Label，分析额外带 seq/正文/用量
    internal sealed class PctRow
    {
        internal string Key;
        internal string Name;
        internal string Value;
        internal string Label;
        internal string Updated;
        internal int Seq;
        internal string Model;
        internal string Intro;
        internal string Advice;
        internal int InTokens;
        internal int OutTokens;
    }

    // 一个数据集：某个项目下的某一种数据（备份基线 / AI 建议 / AI 分析）。
    // v1 文件只有一集，v2 文件可含多集，由各自的 project/type 标识
    internal sealed class PctSet
    {
        internal int Project;
        internal int Type;
        internal List<PctRow> Rows = new List<PctRow>();
        internal int Skipped;
        // 解析阶段被跳过的条目与原因（最多 20 条），供导入结果弹窗展示
        internal List<string> SkipNotes = new List<string>();
        internal int Count { get { return Rows.Count; } }

        internal void Note(string text)
        {
            if (SkipNotes.Count >= 20 || string.IsNullOrEmpty(text)) return;
            if (!SkipNotes.Contains(text)) SkipNotes.Add(text);
        }
    }

    // 导出目标：项目 × 类型的一个组合
    internal sealed class DatasetRef
    {
        internal int Project;
        internal int Type;
        internal DatasetRef(int project, int type) { Project = project; Type = type; }
    }

    // 多数据集导入时每个数据集的结果
    internal sealed class SetImportResult
    {
        internal PctSet Set;
        internal bool Ok;
        internal int Written;
        internal int Skipped;
        internal string Error;
        internal List<string> Notes = new List<string>();
    }

    // .pct 数据包（XML）。读取时按不可信外部输入处理：限大小/条数、禁 DTD、逐字段限长。
    // v1（单数据集）保持旧结构；v2 用 <sets>/<set> 承载多个数据集
    internal sealed class PctPackage
    {
        internal const int FileVersion = 2;
        internal const long MaxFileBytes = 16 * 1024 * 1024;
        internal const int MaxSets = 12;
        internal const int MaxRowsPerSet = 20000;
        internal const int MaxRowsTotal = 30000;

        internal int Version;
        internal string Created = "";
        internal List<PctSet> Sets = new List<PctSet>();

        // 兼容字段：DIAG 与旧单集调用仍按 Project/Type/Rows 读写。
        // Sets 为空时由这三个字段合成一集，Write 才能继续接受旧式构造
        internal int Project;
        internal int Type;
        internal List<PctRow> Rows = new List<PctRow>();
        internal int Skipped;

        private static string Attr(XElement e, string name)
        {
            XAttribute a = e.Attribute(name);
            return a == null ? "" : a.Value;
        }

        private static int IntAttr(XElement e, string name)
        {
            int n;
            return int.TryParse(Attr(e, name), out n) ? n : 0;
        }

        // Sets 唯一真源；兼容字段仅在 Sets 为空时兜底
        private List<PctSet> EffectiveSets()
        {
            if (Sets.Count > 0) return Sets;
            List<PctSet> one = new List<PctSet>();
            if (Project >= 0 && Project <= 3 && Type >= 0 && Type <= 2)
            {
                PctSet s = new PctSet();
                s.Project = Project;
                s.Type = Type;
                s.Rows = Rows == null ? new List<PctRow>() : Rows;
                s.Skipped = Skipped;
                one.Add(s);
            }
            return one;
        }

        internal void MirrorFirst()
        {
            Skipped = 0;
            foreach (PctSet s in Sets) Skipped += s.Skipped;
            if (Sets.Count == 0) return;
            Project = Sets[0].Project;
            Type = Sets[0].Type;
            Rows = Sets[0].Rows;
        }

        private static int Writable(PctSet s)
        {
            if (s == null || s.Rows == null) return 0;
            int n = 0;
            foreach (PctRow r in s.Rows)
                if (r != null && !string.IsNullOrEmpty(r.Key)) n++;
            return n;
        }

        private static void ParseRow(XElement e, PctSet set)
        {
            string key = Attr(e, "key");
            if (string.IsNullOrEmpty(key) || key.Length > 512)
            {
                set.Skipped++;
                string name = Attr(e, "name");
                set.Note("「" + (string.IsNullOrEmpty(name) ? "未命名条目" : name) + "」缺少有效标识");
                return;
            }
            PctRow r = new PctRow();
            r.Key = key;
            r.Name = TransferStore.Limit(Attr(e, "name"), 256);
            r.Updated = TransferStore.Limit(Attr(e, "updated"), 64);
            if (e.Name.LocalName == "entry")
            {
                r.Seq = IntAttr(e, "seq");
                r.Model = TransferStore.Limit(Attr(e, "model"), 128);
                r.Intro = TransferStore.Limit(Attr(e, "intro"), 20000);
                r.Advice = TransferStore.Limit(Attr(e, "advice"), 20000);
                r.InTokens = IntAttr(e, "in");
                r.OutTokens = IntAttr(e, "out");
                if (r.InTokens < 0) r.InTokens = 0;
                if (r.OutTokens < 0) r.OutTokens = 0;
            }
            else
            {
                r.Value = TransferStore.Limit(Attr(e, "value"), 128);
                r.Label = TransferStore.Limit(Attr(e, "label"), 128);
            }
            set.Rows.Add(r);
        }

        internal static bool Read(string path, out PctPackage pkg, out string error)
        {
            pkg = null;
            error = null;
            try
            {
                FileInfo fi = new FileInfo(path);
                if (!fi.Exists) { error = "文件不存在：" + path; return false; }
                if (fi.Length > MaxFileBytes)
                {
                    error = "文件超过 " + (MaxFileBytes / 1024 / 1024) + " MB，已拒绝导入";
                    return false;
                }

                XmlReaderSettings settings = new XmlReaderSettings();
                settings.DtdProcessing = DtdProcessing.Prohibit;
                settings.XmlResolver = null;
                settings.MaxCharactersInDocument = 20 * 1024 * 1024;
                settings.IgnoreComments = true;
                settings.IgnoreProcessingInstructions = true;

                XDocument doc;
                using (XmlReader reader = XmlReader.Create(path, settings))
                    doc = XDocument.Load(reader);
                XElement root = doc.Root;
                if (root == null || root.Name.LocalName != "pctools")
                {
                    error = "不是有效的 .pct 文件（根节点应为 pctools）";
                    return false;
                }
                if (Attr(root, "app") != "PCTools") { error = "文件不是由 PCTools 导出的"; return false; }
                int ver = IntAttr(root, "version");
                if (ver <= 0 || ver > FileVersion)
                {
                    error = "不支持的文件版本：" + Attr(root, "version");
                    return false;
                }

                PctPackage p = new PctPackage();
                p.Version = ver;
                p.Created = Attr(root, "created");
                if (ver == 1)
                {
                    // v1：project/type 在根上，全文件只有一集
                    int prj = TransferStore.ProjectIndex(Attr(root, "project"));
                    int typ = TransferStore.TypeIndex(Attr(root, "type"));
                    if (prj < 0 || typ < 0)
                    {
                        error = "文件的项目或类型无法识别";
                        return false;
                    }
                    PctSet s = new PctSet();
                    s.Project = prj;
                    s.Type = typ;
                    int total = 0;
                    foreach (XElement e in root.Descendants())
                    {
                        string tag = e.Name.LocalName;
                        if (tag != "item" && tag != "entry") continue;
                        if (++total > MaxRowsPerSet)
                        {
                            error = "条目超过 " + MaxRowsPerSet + " 条，已拒绝导入";
                            return false;
                        }
                        ParseRow(e, s);
                    }
                    p.Sets.Add(s);
                }
                else
                {
                    // v2：只认根下 <sets> 的直接 <set> 子元素，避免把嵌套结构误当数据集
                    XElement setsEl = root.Element("sets");
                    if (setsEl == null || !setsEl.Elements("set").Any())
                    {
                        error = "v2 文件缺少数据集（sets）";
                        return false;
                    }
                    HashSet<int> seen = new HashSet<int>();
                    int total = 0;
                    foreach (XElement setEl in setsEl.Elements("set"))
                    {
                        if (p.Sets.Count >= MaxSets)
                        {
                            error = "数据包包含超过 " + MaxSets + " 个数据集，已拒绝导入";
                            return false;
                        }
                        int prj = TransferStore.ProjectIndex(Attr(setEl, "project"));
                        int typ = TransferStore.TypeIndex(Attr(setEl, "type"));
                        int no = p.Sets.Count + 1;
                        if (prj < 0 || typ < 0)
                        {
                            error = "第 " + no + " 个数据集的项目或类型无法识别";
                            return false;
                        }
                        int combo = prj * 3 + typ;
                        if (!seen.Add(combo))
                        {
                            error = "数据包包含重复数据集：" + TransferStore.ProjectName(prj) +
                                    " / " + TransferStore.TypeName(prj, typ);
                            return false;
                        }
                        PctSet s = new PctSet();
                        s.Project = prj;
                        s.Type = typ;
                        XElement items = setEl.Element("items");
                        if (items != null)
                        {
                            foreach (XElement e in items.Elements())
                            {
                                string tag = e.Name.LocalName;
                                if (tag != "item" && tag != "entry") continue;
                                if (s.Rows.Count >= MaxRowsPerSet)
                                {
                                    error = "第 " + no + " 个数据集超过 " + MaxRowsPerSet + " 条，已拒绝导入";
                                    return false;
                                }
                                if (++total > MaxRowsTotal)
                                {
                                    error = "条目总数超过 " + MaxRowsTotal + " 条，已拒绝导入";
                                    return false;
                                }
                                ParseRow(e, s);
                            }
                        }
                        p.Sets.Add(s);
                    }
                }

                p.MirrorFirst();
                pkg = p;
                return true;
            }
            catch (Exception ex)
            {
                error = "读取 .pct 失败：" + ex.Message;
                return false;
            }
        }

        internal bool Write(string path, out string error)
        {
            error = null;
            string tmp = null;
            try
            {
                List<PctSet> sets = EffectiveSets();
                if (sets.Count == 0) { error = "没有可导出的数据集"; return false; }
                if (sets.Count > MaxSets) { error = "数据集超过 " + MaxSets + " 个，无法导出"; return false; }

                HashSet<int> seen = new HashSet<int>();
                int total = 0;
                foreach (PctSet s in sets)
                {
                    if (s == null || s.Project < 0 || s.Project > 3 || s.Type < 0 || s.Type > 2)
                    {
                        error = "导出数据集的项目或类型无效";
                        return false;
                    }
                    if (!seen.Add(s.Project * 3 + s.Type))
                    {
                        error = "导出数据集重复：" + TransferStore.ProjectName(s.Project) +
                                " / " + TransferStore.TypeName(s.Project, s.Type);
                        return false;
                    }
                    int n = Writable(s);
                    if (n > MaxRowsPerSet)
                    {
                        error = "数据集「" + TransferStore.ProjectName(s.Project) + " / " +
                                TransferStore.TypeName(s.Project, s.Type) + "」超过 " + MaxRowsPerSet + " 条";
                        return false;
                    }
                    total += n;
                }
                if (total > MaxRowsTotal)
                {
                    error = "条目总数超过 " + MaxRowsTotal + " 条，请减少数据集";
                    return false;
                }

                XElement root;
                if (sets.Count == 1)
                {
                    // 单集仍写 v1：旧版本程序可以继续读
                    PctSet s = sets[0];
                    root = new XElement("pctools",
                        new XAttribute("version", 1),
                        new XAttribute("app", "PCTools"),
                        new XAttribute("created", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz")),
                        new XAttribute("project", TransferStore.ProjectKey(s.Project)),
                        new XAttribute("type", TransferStore.TypeKey(s.Type)),
                        new XAttribute("count", Writable(s)));
                    root.Add(BuildItems(s));
                }
                else
                {
                    XElement setsEl = new XElement("sets");
                    foreach (PctSet s in sets)
                    {
                        XElement setEl = new XElement("set",
                            new XAttribute("project", TransferStore.ProjectKey(s.Project)),
                            new XAttribute("type", TransferStore.TypeKey(s.Type)),
                            new XAttribute("count", Writable(s)));
                        setEl.Add(BuildItems(s));
                        setsEl.Add(setEl);
                    }
                    root = new XElement("pctools",
                        new XAttribute("version", 2),
                        new XAttribute("app", "PCTools"),
                        new XAttribute("created", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz")),
                        new XAttribute("sets", sets.Count),
                        new XAttribute("count", total));
                    root.Add(setsEl);
                }

                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                tmp = path + "." + Path.GetRandomFileName() + ".tmp";

                XmlWriterSettings ws = new XmlWriterSettings();
                ws.Indent = true;
                ws.Encoding = new UTF8Encoding(false);
                ws.OmitXmlDeclaration = false;
                using (XmlWriter w = XmlWriter.Create(tmp, ws))
                {
                    new XDocument(new XDeclaration("1.0", "utf-8", null), root).Save(w);
                }

                long len = new FileInfo(tmp).Length;
                if (len > MaxFileBytes)
                {
                    error = "导出内容超过 " + (MaxFileBytes / 1024 / 1024) + " MB，请减少数据集或导出范围";
                    return false;
                }
                // 回读自检：写出的文件必须能被自己的读取器按同样的数据集形状读回
                PctPackage check;
                string checkErr;
                if (!Read(tmp, out check, out checkErr) || !SameShape(check))
                {
                    error = "写入自检失败，已保留原文件：" + (string.IsNullOrEmpty(checkErr) ? "内容不一致" : checkErr);
                    return false;
                }

                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
                tmp = null;
                return true;
            }
            catch (Exception ex)
            {
                error = "写入 .pct 失败：" + ex.Message;
                DiagLog.Swallow("导出 .pct", ex);
                return false;
            }
            finally
            {
                if (tmp != null) { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
            }
        }

        private static XElement BuildItems(PctSet set)
        {
            XElement items = new XElement("items");
            foreach (PctRow r in set.Rows)
            {
                if (r == null || string.IsNullOrEmpty(r.Key)) continue;
                if (set.Type == TransferStore.TypeAnalysis)
                {
                    items.Add(new XElement("entry",
                        new XAttribute("key", Clean(r.Key)),
                        new XAttribute("name", Clean(r.Name)),
                        new XAttribute("seq", r.Seq),
                        new XAttribute("model", Clean(r.Model)),
                        new XAttribute("intro", Clean(r.Intro)),
                        new XAttribute("advice", Clean(r.Advice)),
                        new XAttribute("updated", Clean(r.Updated)),
                        new XAttribute("in", r.InTokens),
                        new XAttribute("out", r.OutTokens)));
                }
                else if (set.Type == TransferStore.TypeAdvice)
                {
                    items.Add(new XElement("item",
                        new XAttribute("key", Clean(r.Key)),
                        new XAttribute("name", Clean(r.Name)),
                        new XAttribute("label", Clean(r.Label)),
                        new XAttribute("updated", Clean(r.Updated))));
                }
                else
                {
                    items.Add(new XElement("item",
                        new XAttribute("key", Clean(r.Key)),
                        new XAttribute("name", Clean(r.Name)),
                        new XAttribute("value", Clean(r.Value)),
                        new XAttribute("updated", Clean(r.Updated))));
                }
            }
            return items;
        }

        private bool SameShape(PctPackage other)
        {
            List<PctSet> a = EffectiveSets();
            List<PctSet> b = other == null ? new List<PctSet>() : other.EffectiveSets();
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].Project != b[i].Project || a[i].Type != b[i].Type) return false;
                if (Writable(a[i]) != Writable(b[i])) return false;
            }
            return true;
        }

        // XML 1.0 不允许的控制字符会让 XAttribute 构造直接抛异常，写入前先剔除。
        // 同时丢弃孤立代理项：库里的非法 UTF-16 会让 XmlWriter 的 CheckCharacters
        // 直接抛异常，整次导出失败（以前只判断单字节，代理对会被原样带出去）
        internal static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                    {
                        sb.Append(c).Append(s[i + 1]);
                        i++;
                    }
                    continue;                   // 孤立高代理项：丢弃
                }
                if (char.IsLowSurrogate(c)) continue;
                if (c == '\t' || c == '\n' || c == '\r' || (c >= 0x20 && c != 0xFFFE && c != 0xFFFF))
                    sb.Append(c);
            }
            return sb.ToString();
        }
    }

    internal static class TransferStore
    {
        internal const int TypeBackup = 0;
        internal const int TypeAdvice = 1;
        internal const int TypeAnalysis = 2;

        internal static string ProjectKey(int project)
        {
            switch (project)
            {
                case 0: return "startup";
                case 1: return "service";
                case 2: return "task";
                default: return "ctx";
            }
        }

        internal static string TypeKey(int type)
        {
            switch (type)
            {
                case 1: return "advice";
                case 2: return "analysis";
                default: return "backup";
            }
        }

        internal static int ProjectIndex(string key)
        {
            if (key == "startup") return 0;
            if (key == "service") return 1;
            if (key == "task") return 2;
            if (key == "ctx") return 3;
            return -1;
        }

        internal static int TypeIndex(string key)
        {
            if (key == "backup") return TypeBackup;
            if (key == "advice") return TypeAdvice;
            if (key == "analysis") return TypeAnalysis;
            return -1;
        }

        internal static string ProjectName(int project)
        {
            switch (project)
            {
                case 0: return "启动项";
                case 1: return "服务";
                case 2: return "计划任务";
                default: return "右键菜单";
            }
        }

        internal static string TypeName(int project, int type)
        {
            if (type == TypeAdvice) return "AI建议";
            if (type == TypeAnalysis) return "AI分析";
            return project == 1 ? "备份启动类型" : "备份状态";
        }

        internal static string Limit(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || max <= 0) return "";
            // 先剔除 XML 非法字符与孤立代理项：截断后再清洗可能把代理对切成半个，
            // 写进库后导出时 XmlWriter 会直接抛异常
            s = PctPackage.Clean(s);
            if (s.Length <= max) return s;
            int cut = max;
            if (cut > 0 && char.IsHighSurrogate(s[cut - 1])) cut--;   // 不切断代理对
            return cut <= 0 ? "" : s.Substring(0, cut);
        }

        internal static string KindFor(int project)
        {
            return AiAnalysisStore.KindFor(project);
        }

        internal static int Count(int project, int type)
        {
            if (type == TypeBackup) return BackupStore.Count(KindFor(project));
            try
            {
                if (!AppDb.Ensure()) return 0;
                string err;
                IntPtr db = Sqlite.Open(AppDb.FilePath, out err, 500);
                if (db == IntPtr.Zero) return 0;
                try
                {
                    string table = type == TypeAdvice ? "advice" : "analysis";
                    List<string> rows = Sqlite.Column(db,
                        "SELECT COUNT(*) FROM " + table + " WHERE kind=" + Sqlite.Q(KindFor(project)) + ";", out err);
                    int n;
                    return rows.Count > 0 && int.TryParse(rows[0], out n) ? n : 0;
                }
                finally { Sqlite.Close(db); }
            }
            catch { return 0; }
        }

        // 多选导出需要 12 个组合的条数：一次开库、三条聚合查询，避免每格各开一次
        internal static int[] CountAll()
        {
            int[] n = new int[12];
            try
            {
                if (!AppDb.Ensure()) return n;
                string err;
                IntPtr db = Sqlite.Open(AppDb.FilePath, out err, 500);
                if (db == IntPtr.Zero) return n;
                try
                {
                    FillCounts(db, "SELECT kind, COUNT(*) FROM backup_state GROUP BY kind;", n, TypeBackup);
                    FillCounts(db, "SELECT kind, COUNT(*) FROM advice GROUP BY kind;", n, TypeAdvice);
                    FillCounts(db, "SELECT kind, COUNT(*) FROM analysis GROUP BY kind;", n, TypeAnalysis);
                }
                finally { Sqlite.Close(db); }
            }
            catch { }
            return n;
        }

        private static void FillCounts(IntPtr db, string sql, int[] target, int type)
        {
            string err;
            List<string[]> rows = Sqlite.Rows(db, sql, out err);
            if (err != null) return;
            foreach (string[] p in rows)
            {
                if (p.Length < 2) continue;
                int n;
                if (!int.TryParse(p[1], out n)) continue;
                for (int project = 0; project < 4; project++)
                {
                    if (p[0] != KindFor(project)) continue;
                    target[project * 3 + type] = n;
                    break;
                }
            }
        }

        internal static bool Build(int project, int type,
                                   Func<int, Dictionary<string, string>> names,
                                   out PctPackage pkg, out string error)
        {
            pkg = new PctPackage();
            pkg.Project = project;
            pkg.Type = type;
            error = null;
            string kind = KindFor(project);
            try
            {
                if (type == TypeBackup)
                {
                    bool complete;
                    string err;
                    List<BackupRow> rows = BackupStore.LoadRows(kind, out err, out complete);
                    if (!complete) { error = string.IsNullOrEmpty(err) ? "读取备份基线失败" : err; return false; }
                    foreach (BackupRow b in rows)
                    {
                        PctRow r = new PctRow();
                        r.Key = b.Key; r.Name = b.Name; r.Value = b.Value; r.Updated = b.Updated;
                        pkg.Rows.Add(r);
                    }
                }
                else
                {
                    if (!AppDb.Ensure()) { error = AppDb.LastError; return false; }
                    string err;
                    IntPtr db = Sqlite.Open(AppDb.FilePath, out err, 500);
                    if (db == IntPtr.Zero) { error = err; return false; }
                    try
                    {
                        Dictionary<string, string> nameMap = names == null ? null : names(project);
                        string sql = type == TypeAdvice
                            ? "SELECT item_key, IFNULL(label,''), IFNULL(updated,'') FROM advice WHERE kind=" +
                              Sqlite.Q(kind) + " ORDER BY item_key;"
                            : "SELECT item_key, IFNULL(seq,1), IFNULL(model,''), IFNULL(intro,''), IFNULL(advice,''), " +
                              "IFNULL(updated,''), IFNULL(in_tokens,0), IFNULL(out_tokens,0) FROM analysis WHERE kind=" +
                              Sqlite.Q(kind) + " ORDER BY item_key, seq ASC;";
                        List<string[]> rows = Sqlite.Rows(db, sql, out err);
                        if (err != null) { error = err; return false; }
                        foreach (string[] p in rows)
                        {
                            if (p.Length < 3 || string.IsNullOrEmpty(p[0])) continue;
                            PctRow r = new PctRow();
                            r.Key = p[0];
                            if (nameMap != null) nameMap.TryGetValue(r.Key, out r.Name);
                            if (type == TypeAdvice)
                            {
                                r.Label = p[1];
                                r.Updated = p.Length > 2 ? p[2] : "";
                            }
                            else
                            {
                                int seq; int.TryParse(p[1], out seq); r.Seq = seq <= 0 ? 1 : seq;
                                r.Model = p[2]; r.Intro = p[3]; r.Advice = p[4];
                                r.Updated = p[5];
                                int n; int.TryParse(p[6], out n); r.InTokens = n;
                                int.TryParse(p[7], out n); r.OutTokens = n;
                            }
                            pkg.Rows.Add(r);
                        }
                    }
                    finally { Sqlite.Close(db); }
                }
            }
            catch (Exception ex) { error = ex.Message; return false; }

            if (pkg.Rows.Count == 0)
            {
                error = "当前没有可导出的" + TypeName(project, type);
                return false;
            }
            return true;
        }

        // 按「项目 × 类型」批量构建：空集跳过并单独返回；任一非空集读取失败则整体失败，
        // 避免用户以为导出完整、实际少了数据集
        internal static bool BuildSets(IList<DatasetRef> targets,
                                       Func<int, Dictionary<string, string>> names,
                                       out PctPackage pkg, out List<string> empty, out string error)
        {
            return BuildSets(targets, names, null, out pkg, out empty, out error);
        }

        // progress：每处理完一个组合回调一次（空集也算），用于驱动导出进度窗。
        // cancelled：安全点取消（返回 true 时不再开始下一个数据集，已读出的部分不写文件）
        internal static bool BuildSets(IList<DatasetRef> targets,
                                       Func<int, Dictionary<string, string>> names,
                                       Action<int, int, string> progress,
                                       out PctPackage pkg, out List<string> empty, out string error)
        {
            return BuildSets(targets, names, progress, null, out pkg, out empty, out error);
        }

        internal static bool BuildSets(IList<DatasetRef> targets,
                                       Func<int, Dictionary<string, string>> names,
                                       Action<int, int, string> progress, Func<bool> cancelled,
                                       out PctPackage pkg, out List<string> empty, out string error)
        {
            pkg = new PctPackage();
            empty = new List<string>();
            error = null;
            if (targets == null || targets.Count == 0)
            {
                error = "请至少选择一个项目和一种类型";
                return false;
            }

            int[] counts = CountAll();
            HashSet<int> seen = new HashSet<int>();
            List<string> fails = new List<string>();
            int total = targets.Count;
            int done = 0;
            foreach (DatasetRef t in targets)
            {
                // 安全点：只在两个数据集之间检查，绝不打断正在读/正在提交的那一个
                if (cancelled != null && cancelled())
                {
                    error = "已取消：未写入文件";
                    return false;
                }
                done++;
                bool valid = t != null && t.Project >= 0 && t.Project <= 3 && t.Type >= 0 && t.Type <= 2;
                string label = valid ? ProjectName(t.Project) + " / " + TypeName(t.Project, t.Type) : "无效数据集";
                try
                {
                    if (!valid) continue;
                    int combo = t.Project * 3 + t.Type;
                    if (!seen.Add(combo)) continue;
                    if (counts[combo] == 0) { empty.Add(label); continue; }

                    PctPackage one;
                    string e;
                    if (!Build(t.Project, t.Type, names, out one, out e))
                    {
                        fails.Add(label + "：" + (string.IsNullOrEmpty(e) ? "读取失败" : e));
                        continue;
                    }
                    PctSet set = new PctSet();
                    set.Project = t.Project;
                    set.Type = t.Type;
                    set.Rows = one.Rows;
                    set.Skipped = one.Skipped;
                    pkg.Sets.Add(set);
                }
                finally
                {
                    if (progress != null) { try { progress(done, total, label); } catch { } }
                }
            }

            if (fails.Count > 0)
            {
                error = "部分数据集读取失败：" + string.Join("；", fails.ToArray());
                return false;
            }
            if (pkg.Sets.Count == 0)
            {
                error = "所选数据集都为空，未生成文件";
                return false;
            }

            // 兼容字段同步第一集：旧接口仍能读到一个有效数据集。
            // 统一走 MirrorFirst，避免这里与读取路径各写一遍、新增数据集类型时漏改
            pkg.MirrorFirst();
            return true;
        }

        internal static bool Import(int project, int type, PctPackage pkg,
                                    out int written, out int skipped, out string error)
        {
            written = 0;
            skipped = pkg == null ? 0 : pkg.Skipped;
            error = null;
            if (pkg == null) { error = "数据包为空"; return false; }
            if (pkg.Project != project || pkg.Type != type)
            {
                error = "文件属于「" + ProjectName(pkg.Project) + " / " + TypeName(pkg.Project, pkg.Type) +
                        "」，与当前选择不一致";
                return false;
            }

            PctSet set = new PctSet();
            set.Project = pkg.Project;
            set.Type = pkg.Type;
            set.Rows = pkg.Rows;
            set.Skipped = pkg.Skipped;
            return ImportSet(project, type, set, new List<string>(), out written, out skipped, out error);
        }

        // 多数据集导入：逐集执行、逐集报告；0 行集直接拒绝，绝不触发整表替换
        internal static void ImportSets(IList<PctSet> sets, out List<SetImportResult> results)
        {
            ImportSets(sets, null, null, out results);
        }

        internal static void ImportSets(IList<PctSet> sets, Action<int, int, string> progress,
                                        out List<SetImportResult> results)
        {
            ImportSets(sets, progress, null, out results);
        }

        internal static void ImportSets(IList<PctSet> sets, Action<int, int, string> progress,
                                        Func<bool> cancelled, out List<SetImportResult> results)
        {
            results = new List<SetImportResult>();
            if (sets == null) return;
            int total = sets.Count;
            for (int i = 0; i < sets.Count; i++)
            {
                // 安全点取消：当前数据集已完整提交，停在这里不会留下半套数据
                if (cancelled != null && cancelled()) break;
                PctSet s = sets[i];
                SetImportResult r = new SetImportResult();
                r.Set = s;
                if (s == null) { r.Error = "数据集为空"; }
                else if (s.Rows == null || s.Rows.Count == 0) { r.Error = "空数据集不导入"; }
                else
                {
                    int w, k;
                    string e;
                    List<string> notes = new List<string>();
                    r.Ok = ImportSet(s.Project, s.Type, s, notes, out w, out k, out e);
                    r.Written = w;
                    r.Skipped = k;
                    r.Error = e;
                    r.Notes = notes;
                }
                results.Add(r);
                if (progress != null)
                {
                    string label = s == null ? "无效数据集"
                        : ProjectName(s.Project) + " / " + TypeName(s.Project, s.Type);
                    try { progress(i + 1, total, label); } catch { }
                }
            }
        }

        private static bool ImportSet(int project, int type, PctSet set, List<string> notes,
                                      out int written, out int skipped, out string error)
        {
            written = 0;
            skipped = set == null ? 0 : set.Skipped;
            error = null;
            if (set == null) { error = "数据包为空"; return false; }
            // 解析阶段被判无效的行也带进结果，用户才能看到「哪些条目没进来、为什么」
            if (notes != null && set.SkipNotes != null) notes.AddRange(set.SkipNotes);
            List<PctRow> rows = set.Rows == null ? new List<PctRow>() : set.Rows;
            if (rows.Count == 0) { error = "空数据集不导入"; return false; }

            bool ok;
            if (type == TypeBackup) ok = ImportBackup(project, rows, notes, out written, out skipped, out error);
            else if (type == TypeAdvice) ok = ImportAdvice(project, rows, notes, out written, out skipped, out error);
            else ok = ImportAnalysis(project, rows, notes, out written, out skipped, out error);
            skipped += set.Skipped;   // 解析阶段因 key 非法被跳过的行也算进结果
            return ok;
        }

        // 跳过明细只留前 20 条：结果弹窗要能读，完整数量仍在 skipped 计数里
        private static void AddImportNote(List<string> notes, PctRow r, string reason)
        {
            if (notes == null || notes.Count >= 20) return;
            string who = r == null ? "" : (string.IsNullOrEmpty(r.Name) ? r.Key : r.Name);
            string line = "「" + (string.IsNullOrEmpty(who) ? "未命名条目" : who) + "」" + reason;
            if (!notes.Contains(line)) notes.Add(line);
        }

        internal static string ImportSemantics(int type)
        {
            if (type == TypeBackup) return "整体替换该项目的现有备份基线";
            if (type == TypeAdvice) return "按条目覆盖，文件中没有的条目保留原值";
            return "按条目整组替换（每项最多保留两份），文件中没有的条目保留原值";
        }

        private static bool ImportBackup(int project, List<PctRow> rows, List<string> notes,
                                         out int written, out int skipped, out string error)
        {
            written = 0;
            skipped = 0;
            List<BackupRow> list = new List<BackupRow>();
            foreach (PctRow r in rows)
            {
                if (r == null || string.IsNullOrEmpty(r.Key) || string.IsNullOrEmpty(r.Value))
                {
                    skipped++;
                    AddImportNote(notes, r, "缺少标识或备份值");
                    continue;
                }
                BackupRow b = new BackupRow();
                b.Key = r.Key;
                b.Name = r.Name;
                // 旧文件可能带「自动（延迟）」或任务的运行态：按当前口径归一
                b.Value = project == 1 && r.Value == "自动（延迟）" ? "自动"
                        : (project == 2 ? BackupStore.NormalizeTaskValue(r.Value) : r.Value);
                list.Add(b);
            }
            if (!BackupStore.Replace(KindFor(project), list, out error)) return false;
            written = list.Count;
            return true;
        }

        private static bool ImportAdvice(int project, List<PctRow> rows, List<string> notes,
                                         out int written, out int skipped, out string error)
        {
            written = 0;
            skipped = 0;
            error = null;
            try
            {
                if (!AppDb.Ensure()) { error = AppDb.LastError; return false; }
                IntPtr db = Sqlite.Open(AppDb.FilePath, out error, 3000);
                if (db == IntPtr.Zero) return false;
                try
                {
                    string kind = KindFor(project);
                    string now = Sqlite.Q(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    StringBuilder sb = new StringBuilder("BEGIN;");
                    foreach (PctRow r in rows)
                    {
                        if (r == null || string.IsNullOrEmpty(r.Key) || string.IsNullOrEmpty(r.Label) ||
                            !AiAdviceStore.AdviceAllowed(project, r.Label))
                        {
                            skipped++;
                            if (r == null) AddImportNote(notes, null, "条目为空");
                            else if (string.IsNullOrEmpty(r.Key)) AddImportNote(notes, r, "缺少标识");
                            else if (string.IsNullOrEmpty(r.Label)) AddImportNote(notes, r, "缺少结论");
                            else AddImportNote(notes, r, "结论标签不被接受");
                            continue;
                        }
                        sb.Append("INSERT OR REPLACE INTO advice (kind,item_key,label,updated) VALUES (")
                          .Append(Sqlite.Q(kind)).Append(',')
                          .Append(Sqlite.Q(r.Key)).Append(',')
                          .Append(Sqlite.Q(r.Label)).Append(',')
                          .Append(string.IsNullOrEmpty(r.Updated) ? now : Sqlite.Q(r.Updated)).Append(");");
                        written++;
                    }
                    sb.Append("COMMIT;");
                    string e = Sqlite.Exec(db, sb.ToString());
                    if (e != null) { Sqlite.Exec(db, "ROLLBACK;"); error = e; written = 0; return false; }
                    return true;
                }
                finally { Sqlite.Close(db); }
            }
            catch (Exception ex) { error = ex.Message; written = 0; return false; }
        }

        private static bool ImportAnalysis(int project, List<PctRow> rows, List<string> notes,
                                           out int written, out int skipped, out string error)
        {
            written = 0;
            skipped = 0;
            error = null;

            // 按 key 分组：每个 key 的旧记录整体替换，文件中未出现的 key 保留
            Dictionary<string, List<PctRow>> groups = new Dictionary<string, List<PctRow>>(StringComparer.Ordinal);
            foreach (PctRow r in rows)
            {
                if (r == null || string.IsNullOrEmpty(r.Key))
                {
                    skipped++;
                    AddImportNote(notes, r, "缺少标识");
                    continue;
                }
                List<PctRow> list;
                if (!groups.TryGetValue(r.Key, out list)) { list = new List<PctRow>(); groups[r.Key] = list; }
                list.Add(r);
            }

            try
            {
                if (!AppDb.Ensure()) { error = AppDb.LastError; return false; }
                IntPtr db = Sqlite.Open(AppDb.FilePath, out error, 3000);
                if (db == IntPtr.Zero) return false;
                try
                {
                    string kind = KindFor(project);
                    string now = Sqlite.Q(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    StringBuilder sb = new StringBuilder("BEGIN;");
                    int inChunk = 0;
                    foreach (KeyValuePair<string, List<PctRow>> kv in groups)
                    {
                        List<PctRow> list = kv.Value;
                        list.Sort(delegate(PctRow a, PctRow b) { return a.Seq.CompareTo(b.Seq); });
                        int take = Math.Min(2, list.Count);   // 与 AiAnalysisStore 一致：只保留最新两份
                        sb.Append("DELETE FROM analysis WHERE kind=").Append(Sqlite.Q(kind))
                          .Append(" AND item_key=").Append(Sqlite.Q(kv.Key)).Append(';');
                        for (int i = 0; i < take; i++)
                        {
                            PctRow r = list[i];
                            string updated = string.IsNullOrEmpty(r.Updated) ? now : Sqlite.Q(r.Updated);
                            sb.Append("INSERT OR REPLACE INTO analysis (kind,item_key,seq,model,intro,advice,updated,in_tokens,out_tokens) VALUES (")
                              .Append(Sqlite.Q(kind)).Append(',')
                              .Append(Sqlite.Q(r.Key)).Append(',')
                              .Append(i + 1).Append(',')
                              .Append(Sqlite.Q(r.Model == null ? "" : r.Model)).Append(',')
                              .Append(Sqlite.Q(r.Intro == null ? "" : r.Intro)).Append(',')
                              .Append(Sqlite.Q(r.Advice == null ? "" : r.Advice)).Append(',')
                              .Append(updated).Append(',')
                              .Append(r.InTokens).Append(',')
                              .Append(r.OutTokens).Append(");");
                            written++;
                        }
                        if (list.Count > take)
                        {
                            skipped += list.Count - take;
                            for (int extra = take; extra < list.Count; extra++)
                                AddImportNote(notes, list[extra], "同一项目超过两份，仅保留最新两份");
                        }
                        inChunk++;
                        // 每 200 个 key 提交一次：避免单条 SQL 字符串过大，失败时至少保留已完成的块
                        if (inChunk >= 200)
                        {
                            sb.Append("COMMIT;");
                            string e = Sqlite.Exec(db, sb.ToString());
                            if (e != null) { Sqlite.Exec(db, "ROLLBACK;"); error = e; written = 0; return false; }
                            sb = new StringBuilder("BEGIN;");
                            inChunk = 0;
                        }
                    }
                    sb.Append("COMMIT;");
                    string last = Sqlite.Exec(db, sb.ToString());
                    if (last != null)
                    {
                        Sqlite.Exec(db, "ROLLBACK;");
                        error = last;
                        written = 0;
                        return false;
                    }
                    AiAnalysisStore.InvalidateCache();
                    return true;
                }
                finally { Sqlite.Close(db); }
            }
            catch (Exception ex) { error = ex.Message; written = 0; return false; }
        }
    }

    // 数据集 chip 的绑定模型：标题 + 条数徽标 + 空集置灰
    public sealed class TransferChipItem
    {
        public string Title { get; set; }
        public string Badge { get; set; }
        public Brush BadgeBg { get; set; }
        public Brush BadgeFg { get; set; }
        public bool IsEmpty { get; set; }
    }

    // 导入列表一行的绑定模型：勾选状态 + 数据集信息 + 覆盖语义
    public sealed class TransferSetItem : INotifyPropertyChanged
    {
        public string Title { get; set; }
        public string Note { get; set; }
        public string CountText { get; set; }
        public string KindText { get; set; }
        public Brush KindBg { get; set; }
        public Brush KindFg { get; set; }
        public bool CanImport { get; set; }
        public string DisabledTip { get; set; }

        private bool selected;
        public bool Selected
        {
            get { return selected; }
            set
            {
                if (selected == value) return;
                selected = value;
                PropertyChangedEventHandler h = PropertyChanged;
                if (h != null) h(this, new PropertyChangedEventArgs("Selected"));
            }
        }

        internal PctSet Set;
        public event PropertyChangedEventHandler PropertyChanged;
    }

    // 「导入导出」内容页：导出为多选组合，导入为「选文件 → 勾数据集 → 执行」
    internal class TransferView : UserControl
    {
        private readonly Func<int, Dictionary<string, string>> names;
        private readonly CheckBox[] prj;
        private readonly CheckBox[] typ;
        private readonly RadioButton[] op;

        private TextBlock prjCount, typCount, sumCount, sumEmpty, impCount, hint, status;
        private ItemsControl chips, setList;
        private Button run, prjAll, prjNone, typAll, typNone, impAll, impNone, pickFile, repick;
        private Border fileCard, fileEmpty;
        private Grid setPickHead;
        private ScrollViewer setScroll;
        private TextBlock fileName, fileMeta;
        private ScrollViewer exportScroll;
        private Grid importPanel;
        private TextBlock modeHint;

        private readonly int[] counts = new int[12];   // project*3+type
        private int countSeq;
        private bool countsReady;
        private PctPackage importPkg;
        private string importPath = "";
        private List<TransferSetItem> importItems;
        private bool busy;
        private bool disposed;
        private bool lastStatusError;      // 主题切换后按原语义重取状态色

        internal event Action<int[]> Imported;

        internal TransferView(Func<int, Dictionary<string, string>> names)
        {
            this.names = names;
            FrameworkElement view = (FrameworkElement)XamlReader.Parse(Views.TransferPage);
            Content = view;

            prj = new CheckBox[]
            {
                (CheckBox)view.FindName("PrjStartup"), (CheckBox)view.FindName("PrjService"),
                (CheckBox)view.FindName("PrjTask"), (CheckBox)view.FindName("PrjCtx")
            };
            typ = new CheckBox[]
            {
                (CheckBox)view.FindName("TyBackup"), (CheckBox)view.FindName("TyAdvice"),
                (CheckBox)view.FindName("TyAnalysis")
            };
            op = new RadioButton[]
            {
                (RadioButton)view.FindName("OpExport"), (RadioButton)view.FindName("OpImport")
            };

            prjCount = (TextBlock)view.FindName("PrjCount");
            typCount = (TextBlock)view.FindName("TypCount");
            sumCount = (TextBlock)view.FindName("SumCount");
            sumEmpty = (TextBlock)view.FindName("SumEmpty");
            impCount = (TextBlock)view.FindName("ImpCount");
            hint = (TextBlock)view.FindName("TransHint");
            status = (TextBlock)view.FindName("TransStatus");
            chips = (ItemsControl)view.FindName("Chips");
            setList = (ItemsControl)view.FindName("SetList");
            run = (Button)view.FindName("BtnTransRun");
            prjAll = (Button)view.FindName("PrjAll");
            prjNone = (Button)view.FindName("PrjNone");
            typAll = (Button)view.FindName("TypAll");
            typNone = (Button)view.FindName("TypNone");
            impAll = (Button)view.FindName("ImpAll");
            impNone = (Button)view.FindName("ImpNone");
            pickFile = (Button)view.FindName("PickFile");
            repick = (Button)view.FindName("Repick");
            fileCard = (Border)view.FindName("FileCard");
            fileEmpty = (Border)view.FindName("FileEmpty");
            setPickHead = (Grid)view.FindName("SetPickHead");
            setScroll = (ScrollViewer)view.FindName("SetScroll");
            fileName = (TextBlock)view.FindName("FileName");
            fileMeta = (TextBlock)view.FindName("FileMeta");
            exportScroll = (ScrollViewer)view.FindName("ExportScroll");
            importPanel = (Grid)view.FindName("ImportPanel");
            modeHint = (TextBlock)view.FindName("ModeHint");

            foreach (CheckBox b in prj)
            {
                b.Checked += delegate { UpdateExportSummary(); };
                b.Unchecked += delegate { UpdateExportSummary(); };
            }
            foreach (CheckBox b in typ)
            {
                b.Checked += delegate { UpdateExportSummary(); };
                b.Unchecked += delegate { UpdateExportSummary(); };
            }
            foreach (RadioButton b in op) b.Checked += delegate { UpdateMode(); };

            prjAll.Click += delegate { SetAll(prj, true); };
            prjNone.Click += delegate { SetAll(prj, false); };
            typAll.Click += delegate { SetAll(typ, true); };
            typNone.Click += delegate { SetAll(typ, false); };
            impAll.Click += delegate { SetImportAll(true); };
            impNone.Click += delegate { SetImportAll(false); };
            pickFile.Click += delegate { PickImportFile(); };
            repick.Click += delegate { PickImportFile(); };
            run.Click += delegate { Run(); };
            Unloaded += delegate { disposed = true; };
            // 状态文字用的是 Palette 静态画刷：换主题后必须重取，否则深色主题下
            // 这一行仍是浅色主题的深灰/深红，几乎看不见
            Palette.Changed += delegate
            {
                if (status == null) return;
                status.Foreground = lastStatusError ? Palette.Red : Palette.Gray;
            };

            UpdateMode();
            RefreshCounts();
        }

        private bool ImportMode { get { return op[1].IsChecked == true; } }

        private static List<int> Selected(CheckBox[] boxes)
        {
            List<int> list = new List<int>();
            for (int i = 0; i < boxes.Length; i++)
                if (boxes[i].IsChecked == true) list.Add(i);
            return list;
        }

        private List<DatasetRef> Combos()
        {
            List<int> ps = Selected(prj);
            List<int> ts = Selected(typ);
            List<DatasetRef> list = new List<DatasetRef>();
            foreach (int p in ps)
                foreach (int t in ts)
                    list.Add(new DatasetRef(p, t));
            return list;
        }

        private void SetAll(CheckBox[] boxes, bool value)
        {
            foreach (CheckBox b in boxes) b.IsChecked = value;
            UpdateExportSummary();
        }

        private void SetImportAll(bool value)
        {
            if (importItems == null) return;
            foreach (TransferSetItem it in importItems)
                if (it.CanImport) it.Selected = value;
            UpdateImportSummary();
        }

        // 条数一次算全：12 个组合共用一组聚合查询
        private void RefreshCounts()
        {
            int seq = ++countSeq;
            Thread t = new Thread(delegate()
            {
                int[] c = TransferStore.CountAll();
                Ui(delegate()
                {
                    if (seq != countSeq) return;
                    Array.Copy(c, counts, counts.Length);
                    countsReady = true;
                    UpdateExportSummary();
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        private void UpdateExportSummary()
        {
            if (prjCount == null) return;
            List<int> ps = Selected(prj);
            List<int> ts = Selected(typ);
            prjCount.Text = "已选 " + ps.Count + " / 4";
            typCount.Text = "已选 " + ts.Count + " / 3";
            if (!countsReady && !ImportMode)
            {
                // 首次打开时条数还在后台统计：不要先显示「全为空」再跳变
                hint.Text = "正在统计各数据集的条数…";
                run.Content = "导出为 .pct…";
                run.Visibility = Visibility.Visible;
                run.IsEnabled = false;
                return;
            }

            List<DatasetRef> combos = Combos();
            List<TransferChipItem> items = new List<TransferChipItem>();
            List<string> empties = new List<string>();
            int total = 0;
            foreach (DatasetRef d in combos)
            {
                string title = TransferStore.ProjectName(d.Project) + " · " +
                               TransferStore.TypeName(d.Project, d.Type);
                int n = counts[d.Project * 3 + d.Type];
                total += n;
                TransferChipItem chip = new TransferChipItem();
                chip.Title = title;
                chip.Badge = n > 0 ? n + " 条" : "空";
                chip.BadgeBg = Palette.GrayBg;
                chip.BadgeFg = Palette.Gray;
                chip.IsEmpty = n == 0;
                items.Add(chip);
                if (n == 0) empties.Add(title);
            }
            chips.ItemsSource = null;
            chips.ItemsSource = items;
            sumCount.Text = "共 " + total + " 条";
            if (empties.Count > 0)
            {
                sumEmpty.Text = "已自动跳过 " + empties.Count + " 个空数据集";
                sumEmpty.Visibility = Visibility.Visible;
            }
            else sumEmpty.Visibility = Visibility.Collapsed;

            if (ImportMode) return;

            run.Visibility = Visibility.Visible;   // 从导入态切回导出时恢复底部主按钮
            string plan;
            if (ps.Count == 0 || ts.Count == 0) plan = "请至少勾选一个项目和一种类型";
            else if (total == 0) plan = "所选数据集都为空，无法导出";
            else plan = "将导出 " + combos.Count + " 个数据集（共 " + total + " 条）到单个 .pct" +
                        (empties.Count > 0 ? "；空数据集会自动跳过" : "");
            hint.Text = plan;
            run.Content = "导出为 .pct…";
            run.IsEnabled = !busy && total > 0;
        }

        private void UpdateMode()
        {
            bool import = ImportMode;
            exportScroll.Visibility = import ? Visibility.Collapsed : Visibility.Visible;
            importPanel.Visibility = import ? Visibility.Visible : Visibility.Collapsed;
            modeHint.Text = import ? "先选择文件，再勾选要导入的数据集" : "数据集 = 所选项目 × 所选类型";
            SetStatus("", false);
            UpdateExportSummary();
            UpdateImportSummary();
        }

        private void UpdateImportSummary()
        {
            if (run == null) return;
            if (importItems == null)
            {
                impCount.Text = "";
                if (!ImportMode) return;
                // 空态页面中央已有「选择 .pct 文件…」主按钮，底部再放一个同文案按钮
                // 会让人以为有两个入口；这里只保留中央那一个
                run.Visibility = Visibility.Collapsed;
                run.IsEnabled = false;
                hint.Text = "尚未选择文件。选择 .pct 后会列出文件内的数据集，再勾选要导入的部分。";
                return;
            }

            int selectable = 0, selected = 0, rows = 0;
            foreach (TransferSetItem it in importItems)
            {
                if (!it.CanImport) continue;
                selectable++;
                if (!it.Selected) continue;
                selected++;
                rows += it.Set == null ? 0 : it.Set.Count;
            }
            impCount.Text = "已选 " + selected + " / " + selectable;
            if (!ImportMode) return;

            run.Visibility = Visibility.Visible;
            run.Content = selected > 0 ? "导入选中的 " + selected + " 个数据集" : "请勾选要导入的数据集";
            run.IsEnabled = !busy && selected > 0;
            hint.Text = "文件：" + Path.GetFileName(importPath) + "；已选 " + selected + " 个数据集（" +
                        rows + " 条）。备份数据集会整体替换对应项目的基线，且无法撤销。";
        }

        private void SetStatus(string text, bool error)
        {
            if (status == null) return;
            lastStatusError = error;
            status.Text = text;
            status.Foreground = error ? Palette.Red : Palette.Gray;
            status.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void SetBusy(bool value)
        {
            busy = value;
            foreach (CheckBox b in prj) b.IsEnabled = !value;
            foreach (CheckBox b in typ) b.IsEnabled = !value;
            foreach (RadioButton b in op) b.IsEnabled = !value;
            prjAll.IsEnabled = !value;
            prjNone.IsEnabled = !value;
            typAll.IsEnabled = !value;
            typNone.IsEnabled = !value;
            impAll.IsEnabled = !value;
            impNone.IsEnabled = !value;
            pickFile.IsEnabled = !value;
            repick.IsEnabled = !value;
            if (setList != null) setList.IsEnabled = !value;
            run.IsEnabled = false;
            if (!value)
            {
                UpdateExportSummary();
                UpdateImportSummary();
            }
        }

        // 进度窗回调：不走 Ui 的 disposed 判定——模态窗关闭依赖 Finish，
        // 页面卸载后丢回调会让 ShowDialog 永远等不到关闭
        private static void Post(ProgressWindow target, Action action)
        {
            if (target == null) return;
            try { target.Dispatcher.BeginInvoke(action); } catch { }
        }

        private void Ui(Action action)
        {
            try
            {
                Dispatcher.BeginInvoke((Action)delegate
                {
                    if (disposed) return;
                    action();
                });
            }
            catch { }
        }

        private void Run()
        {
            if (busy) return;
            if (ImportMode)
            {
                if (importPkg == null) PickImportFile();
                else DoImport();
            }
            else DoExport();
        }

        private void DoExport()
        {
            List<DatasetRef> combos = Combos();
            if (combos.Count == 0) { SetStatus("请至少勾选一个项目和一种类型", true); return; }
            int totalRows = 0;
            foreach (DatasetRef d in combos) totalRows += counts[d.Project * 3 + d.Type];
            if (totalRows == 0) { SetStatus("所选数据集都为空，无法导出", true); return; }

            SaveFileDialog dlg = new SaveFileDialog();
            dlg.Filter = "PCTools 数据包 (*.pct)|*.pct";
            dlg.DefaultExt = ".pct";
            dlg.AddExtension = true;
            dlg.OverwritePrompt = true;
            string name;
            if (combos.Count == 1)
            {
                name = "PCTools_" + TransferStore.ProjectName(combos[0].Project) + "_" +
                       TransferStore.TypeName(combos[0].Project, combos[0].Type);
            }
            else name = "PCTools_多数据集_" + combos.Count + "项";
            dlg.FileName = name + "_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".pct";

            Window owner = Window.GetWindow(this);
            bool? ok = owner == null ? dlg.ShowDialog() : dlg.ShowDialog(owner);
            if (ok != true) return;
            string path = dlg.FileName;
            if (!string.Equals(Path.GetExtension(path), ".pct", StringComparison.OrdinalIgnoreCase))
                path += ".pct";

            SetBusy(true);
            SetStatus("正在汇总 " + combos.Count + " 个数据集…", false);

            int steps = combos.Count + 1;    // 最后一个进度单位留给「写文件」
            PctPackage built = null;
            List<string> emptySets = new List<string>();
            string failure = null;

            ProgressWindow pw = new ProgressWindow("导出", steps, false);
            pw.EnableSafePointCancel();      // 允许「完成当前数据集后停止」，不留下写到一半的文件
            pw.Owner = owner;
            Thread th = new Thread(delegate()
            {
                try
                {
                    string err;
                    PctPackage pkg;
                    List<string> empty;
                    bool good = TransferStore.BuildSets(combos, names,
                        delegate(int done, int all, string label)
                        {
                            Post(pw, delegate { pw.Report(done, all, label); });
                        },
                        delegate { return pw.Cancelled; },
                        out pkg, out empty, out err);
                    if (good)
                    {
                        Post(pw, delegate
                        {
                            pw.Report(combos.Count, steps, "正在写入 " + Path.GetFileName(path));
                        });
                        good = pkg.Write(path, out err);
                    }
                    built = pkg;
                    emptySets = empty;
                    if (!good) failure = string.IsNullOrEmpty(err) ? "导出失败" : err;
                }
                catch (Exception ex)
                {
                    failure = ex.Message;
                }
                finally
                {
                    // 不可取消的进度窗只能靠这里关闭；异常路径也必须走到
                    Post(pw, delegate { pw.Finish(); });
                }
            });
            th.IsBackground = true;
            // 先让窗口显示再启动 worker：数据量很小时 worker 可能在 ShowDialog 之前就结束，
            // 那时 Finish 会作用在尚未显示的窗口上，模态循环将永远等不到关闭
            pw.Loaded += delegate { th.Start(); };
            pw.ShowDialog();

            SetBusy(false);
            if (disposed) return;
            if (pw.Cancelled && failure != null &&
                failure.StartsWith("已取消", StringComparison.Ordinal))
            {
                // 安全点取消：还没写文件，明确告知「什么都没改」而不是弹「导出失败」
                SetStatus("已取消导出，未写入文件", false);
                return;
            }
            if (failure != null || built == null || built.Sets.Count == 0)
            {
                string why = failure == null ? "没有可写入的数据集" : failure;
                SetStatus("导出失败：" + why, true);
                Dialogs.Info(Window.GetWindow(this), "导出失败", "导出没有完成：\n\n" + why);
                return;
            }

            int rows = 0;
            foreach (PctSet s in built.Sets) rows += s.Count;
            StringBuilder msg = new StringBuilder();
            msg.Append("已导出 ").Append(built.Sets.Count).Append(" 个数据集，共 ")
               .Append(rows).AppendLine(" 条。");
            msg.AppendLine();
            foreach (PctSet s in built.Sets)
            {
                msg.Append("· ").Append(TransferStore.ProjectName(s.Project)).Append(" · ")
                   .Append(TransferStore.TypeName(s.Project, s.Type)).Append("：")
                   .Append(s.Count).AppendLine(" 条");
            }
            if (emptySets.Count > 0)
            {
                msg.AppendLine();
                msg.Append("已跳过 ").Append(emptySets.Count).AppendLine(" 个空数据集：");
                foreach (string e in emptySets) msg.Append("· ").AppendLine(e.Replace(" / ", " · "));
            }
            msg.AppendLine();
            msg.Append("文件：").Append(path);

            SetStatus("已导出 " + built.Sets.Count + " 个数据集（" + rows + " 条）", false);
            Dialogs.Info(Window.GetWindow(this), "导出完成", msg.ToString());
        }

        private void PickImportFile()
        {
            if (busy) return;
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Filter = "PCTools 数据包 (*.pct)|*.pct";
            dlg.CheckFileExists = true;
            dlg.Multiselect = false;
            Window owner = Window.GetWindow(this);
            bool? ok = owner == null ? dlg.ShowDialog() : dlg.ShowDialog(owner);
            if (ok != true) return;
            string path = dlg.FileName;
            if (!string.Equals(Path.GetExtension(path), ".pct", StringComparison.OrdinalIgnoreCase))
            {
                SetStatus("只能导入 .pct 文件", true);
                return;
            }
            LoadImportFile(path);
        }

        // 解析文件后先展示内容，不直接导入：勾选动作由用户决定
        private void LoadImportFile(string path)
        {
            if (busy) return;
            SetBusy(true);
            SetStatus("正在读取 " + Path.GetFileName(path) + "…", false);
            Thread th = new Thread(delegate()
            {
                string err;
                PctPackage pkg;
                bool good = PctPackage.Read(path, out pkg, out err);
                Ui(delegate()
                {
                    SetBusy(false);
                    if (!good)
                    {
                        SetStatus("读取失败：" + err, true);
                        return;
                    }
                    importPkg = pkg;
                    importPath = path;
                    BuildImportItems(pkg);
                    fileName.Text = Path.GetFileName(path);
                    string created = string.IsNullOrEmpty(pkg.Created) ? "" : " · " + pkg.Created.Replace('T', ' ');
                    fileMeta.Text = "v" + pkg.Version + " · " + pkg.Sets.Count + " 个数据集 · " +
                                    TotalRows(pkg) + " 条" + created;
                    fileEmpty.Visibility = Visibility.Collapsed;
                    fileCard.Visibility = Visibility.Visible;
                    setPickHead.Visibility = Visibility.Visible;
                    setScroll.Visibility = Visibility.Visible;
                    SetStatus("已读取 " + pkg.Sets.Count + " 个数据集，请勾选要导入的部分", false);
                    UpdateImportSummary();
                });
            });
            th.IsBackground = true;
            th.Start();
        }

        private static int TotalRows(PctPackage pkg)
        {
            int n = 0;
            foreach (PctSet s in pkg.Sets) n += s.Count;
            return n;
        }

        private void BuildImportItems(PctPackage pkg)
        {
            importItems = new List<TransferSetItem>();
            foreach (PctSet s in pkg.Sets)
            {
                TransferSetItem it = new TransferSetItem();
                it.Set = s;
                it.Title = TransferStore.ProjectName(s.Project) + " · " + TransferStore.TypeName(s.Project, s.Type);
                it.Note = TransferStore.ImportSemantics(s.Type) +
                          (s.Skipped > 0 ? "；另有 " + s.Skipped + " 条格式不完整" : "");
                it.CountText = s.Count + " 条";
                if (s.Type == TransferStore.TypeBackup)
                {
                    it.KindText = "整体替换";
                    it.KindBg = Palette.RedBg;
                    it.KindFg = Palette.Red;
                }
                else if (s.Type == TransferStore.TypeAdvice)
                {
                    it.KindText = "覆盖";
                    it.KindBg = Palette.BlueBg;
                    it.KindFg = Palette.Blue;
                }
                else
                {
                    it.KindText = "整组替换";
                    it.KindBg = Palette.OrangeBg;
                    it.KindFg = Palette.Orange;
                }
                it.CanImport = s.Count > 0;
                it.Selected = s.Count > 0;
                it.DisabledTip = s.Count > 0 ? "" : "空数据集不导入";
                it.PropertyChanged += delegate { UpdateImportSummary(); };
                importItems.Add(it);
            }
            setList.ItemsSource = null;
            setList.ItemsSource = importItems;
        }

        private void DoImport()
        {
            if (importItems == null) return;
            List<PctSet> sets = new List<PctSet>();
            int unselected = 0;
            foreach (TransferSetItem it in importItems)
            {
                if (!it.CanImport) continue;
                if (it.Selected && it.Set != null) sets.Add(it.Set);
                else unselected++;
            }
            if (sets.Count == 0) { SetStatus("请先勾选要导入的数据集", true); return; }

            StringBuilder msg = new StringBuilder();
            int rows = 0, parseSkipped = 0;
            foreach (PctSet s in sets)
            {
                rows += s.Count;
                parseSkipped += s.Skipped;
            }
            msg.Append("将导入 ").Append(sets.Count).Append(" 个数据集，共 ").Append(rows).Append(" 条：\n\n");
            foreach (PctSet s in sets)
            {
                msg.Append("· ").Append(TransferStore.ProjectName(s.Project)).Append(" / ")
                   .Append(TransferStore.TypeName(s.Project, s.Type)).Append(" · ")
                   .Append(s.Count).Append(" 条 —— ")
                   .Append(TransferStore.ImportSemantics(s.Type)).Append("\n");
            }
            if (unselected > 0) msg.Append("\n另有 ").Append(unselected).Append(" 个数据集未勾选，不会导入。");
            if (parseSkipped > 0) msg.Append("\n另有 ").Append(parseSkipped).Append(" 条格式不完整，将被跳过。");
            msg.Append("\n\n导入后无法撤销，是否继续？");

            if (!Dialogs.Confirm(Window.GetWindow(this), msg.ToString(), "开始导入"))
            {
                SetStatus("已取消导入", false);
                return;
            }

            List<PctSet> chosen = sets;
            SetBusy(true);
            SetStatus("正在导入 " + chosen.Count + " 个数据集…", false);

            List<SetImportResult> results = null;
            string crash = null;
            ProgressWindow pw = new ProgressWindow("导入", chosen.Count, false);
            pw.EnableSafePointCancel();      // 只在两个数据集之间停，避免整表替换写到一半
            pw.Owner = Window.GetWindow(this);
            Thread work = new Thread(delegate()
            {
                try
                {
                    List<SetImportResult> r;
                    TransferStore.ImportSets(chosen,
                        delegate(int done, int all, string label)
                        {
                            Post(pw, delegate { pw.Report(done, all, label); });
                        },
                        delegate { return pw.Cancelled; },
                        out r);
                    results = r;
                }
                catch (Exception ex)
                {
                    crash = ex.Message;
                }
                finally
                {
                    Post(pw, delegate { pw.Finish(); });
                }
            });
            work.IsBackground = true;
            pw.Loaded += delegate { work.Start(); };
            pw.ShowDialog();

            SetBusy(false);
            if (disposed) return;
            if (results == null) results = new List<SetImportResult>();
            if (crash != null)
            {
                SetStatus("导入失败：" + crash, true);
                Dialogs.Info(Window.GetWindow(this), "导入失败", "导入过程被中断：\n\n" + crash);
                return;
            }

            int okCount = 0, written = 0, skipped = 0;
            List<string> okLines = new List<string>();
            List<string> failLines = new List<string>();
            List<string> skipLines = new List<string>();
            List<int> pages = new List<int>();
            foreach (SetImportResult r in results)
            {
                string label = r.Set == null ? "未知数据集"
                    : TransferStore.ProjectName(r.Set.Project) + " · " +
                      TransferStore.TypeName(r.Set.Project, r.Set.Type);
                if (r.Ok)
                {
                    okCount++;
                    written += r.Written;
                    skipped += r.Skipped;
                    okLines.Add("· " + label + "：" + r.Written + " 条" +
                                (r.Skipped > 0 ? "（跳过 " + r.Skipped + " 条）" : ""));
                    if (r.Set != null && !pages.Contains(r.Set.Project)) pages.Add(r.Set.Project);
                }
                else
                {
                    failLines.Add("· " + label + "：" +
                                  (string.IsNullOrEmpty(r.Error) ? "导入失败" : r.Error));
                }
                if (r.Notes != null)
                {
                    foreach (string n in r.Notes)
                        if (skipLines.Count < 40) skipLines.Add("· " + n);
                }
            }

            StringBuilder detail = new StringBuilder();
            if (pw.Cancelled)
                detail.AppendLine("已按「停止」在当前数据集完成后中止；下面只统计已提交的部分。").AppendLine();
            detail.Append("已导入 ").Append(okCount).Append(" 个数据集，共 ")
                  .Append(written).Append(" 条");
            if (skipped > 0) detail.Append("（跳过 ").Append(skipped).Append(" 条）");
            detail.AppendLine("。");
            if (okLines.Count > 0)
            {
                detail.AppendLine();
                foreach (string line in okLines) detail.AppendLine(line);
            }
            if (failLines.Count > 0)
            {
                detail.AppendLine();
                detail.Append("未导入 ").Append(failLines.Count).AppendLine(" 个数据集：");
                foreach (string line in failLines) detail.AppendLine(line);
            }
            if (skipLines.Count > 0)
            {
                detail.AppendLine();
                detail.AppendLine("跳过明细（最多显示 40 条）：");
                foreach (string line in skipLines) detail.AppendLine(line);
            }

            if (failLines.Count > 0)
                SetStatus("导入完成 " + okCount + " 个，失败 " + failLines.Count + " 个", true);
            else
                SetStatus("已导入 " + okCount + " 个数据集（" + written + " 条）" +
                          (skipped > 0 ? "，跳过 " + skipped + " 条" : ""), false);

            // 成功项取消勾选：备份是整表替换，重复执行没有意义
            if (importItems != null)
            {
                foreach (TransferSetItem it in importItems)
                {
                    if (!it.CanImport || it.Set == null) continue;
                    foreach (SetImportResult r in results)
                    {
                        if (r.Ok && ReferenceEquals(r.Set, it.Set)) { it.Selected = false; break; }
                    }
                }
            }

            RefreshCounts();
            UpdateImportSummary();

            string title = pw.Cancelled ? "导入已中止（已完成部分保留）"
                         : (failLines.Count == 0 ? "导入完成"
                         : (okCount == 0 ? "导入失败" : "导入未完全成功"));
            Dialogs.Info(Window.GetWindow(this), title, detail.ToString());

            if (pages.Count > 0)
            {
                Action<int[]> cb = Imported;
                if (cb != null) { try { cb(pages.ToArray()); } catch { } }
            }
        }

#if DIAG
        internal void SetModeForTest(bool import)
        {
            op[import ? 1 : 0].IsChecked = true;
            UpdateMode();
        }

        // README 截图（--shots）用：四类项目全选，数据集区才有完整一屏
        internal void SelectAllProjectsForTest()
        {
            if (prjAll != null) prjAll.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }

        // 条数是后台线程算的：截图前必须等它就绪，否则会拍到「正在统计各数据集的条数…」
        internal bool CountsReadyForTest { get { return countsReady; } }

        internal void LoadFileForTest(string path) { LoadImportFile(path); }

        internal string StateForTest()
        {
            return "mode=" + (ImportMode ? "import" : "export") +
                   " sets=" + (importItems == null ? 0 : importItems.Count) +
                   " selected=" + (importItems == null ? 0 : importItems.FindAll(delegate(TransferSetItem it)
                   { return it.Selected && it.CanImport; }).Count);
        }

        // 列表是否真的「有界且可滚」：若 SetScroll 仍被无限高测量，ext 会等于 vp、scrollable 为 0
        internal string LayoutForTest()
        {
            if (setScroll == null) return "无列表";
            return "h=" + Math.Round(setScroll.ActualHeight, 1) +
                   " vp=" + Math.Round(setScroll.ViewportHeight, 1) +
                   " ext=" + Math.Round(setScroll.ExtentHeight, 1) +
                   " scrollable=" + Math.Round(setScroll.ScrollableHeight, 1);
        }

        internal bool ScrollForTest(double dy)
        {
            if (setScroll == null) return false;
            setScroll.ScrollToVerticalOffset(dy);
            setScroll.UpdateLayout();
            bool ok = setScroll.VerticalOffset > 0;
            // 复位：截图不能停在滚动中途，否则看起来像首行被裁
            setScroll.ScrollToVerticalOffset(0);
            setScroll.UpdateLayout();
            return ok;
        }

        // TSelect 的边框画在模板内的 bd 上；CheckBox 自身的布局槽含 Margin，直接测会把间距算成边框
        internal FrameworkElement PrjCardForTest(int i)
        {
            if (prj == null || i < 0 || i >= prj.Length) return null;
            CheckBox cb = prj[i];
            cb.ApplyTemplate();
            return cb.Template == null ? null : cb.Template.FindName("bd", cb) as FrameworkElement;
        }

        internal FrameworkElement FirstRowForTest()
        {
            if (setList == null) return null;
            setList.UpdateLayout();
            ContentPresenter cp = setList.ItemContainerGenerator.ContainerFromIndex(0) as ContentPresenter;
            return cp == null ? null : FindRowVisual(cp);
        }

        private static FrameworkElement FindRowVisual(DependencyObject root)
        {
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                DependencyObject c = VisualTreeHelper.GetChild(root, i);
                FrameworkElement fe = c as FrameworkElement;
                if (fe != null && fe.Name == "row") return fe;
                FrameworkElement deep = FindRowVisual(c);
                if (deep != null) return deep;
            }
            return null;
        }
#endif
    }

    // 主窗口的导入导出页接入
    public partial class MainWindow
    {
        private const int SettingsTransfer = 2;
        private TransferView transferView;

#if DIAG
        internal TransferView TransferViewForTest { get { return transferView; } }
#endif

        private void InitTransferPage()
        {
            Find<Button>("BtnTransfer").Click += delegate { ShowTransferPage(); };
        }

        private void ShowTransferPage()
        {
            if (settingsMode == SettingsTransfer) return;
            if (!ConfirmLeaveSettings()) return;
            ExitSettingsContent();

            transferView = new TransferView(AiNamesFor);
            transferView.Imported += delegate(int[] pages)
            {
                if (pages == null || pages.Length == 0) return;
                if (pages.Length == 1) RefreshPage(pages[0]);
                else RefreshAll();   // 多页导入后一次性重载，避免连续触发四轮 WMI/COM
            };
            settingsContent.Content = transferView;
            settingsMode = SettingsTransfer;

            suppressNav = true;
            nav.SelectedIndex = -1;
            suppressNav = false;

            searchDefer.Stop();
            Mouse.OverrideCursor = null;
            commandBar.Visibility = Visibility.Collapsed;
            listHost.Visibility = Visibility.Collapsed;
            settingsHost.Visibility = Visibility.Visible;
            searchHost.Visibility = Visibility.Hidden;
            SyncSettingsNav();
            UpdateToolbarState();
        }

        // 给导出的 AI 建议/分析补上可读名称；键必须用 AI 存储自己的键
        private Dictionary<string, string> AiNamesFor(int page)
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
            IEnumerable items;
            if (page == 0) items = startupData;
            else if (page == 1) items = serviceData;
            else if (page == 2) items = taskData;
            else items = ctxData;
            foreach (object o in items)
            {
                string k = AiAnalysisStore.KeyFor(page, o);
                if (!string.IsNullOrEmpty(k)) map[k] = BackupStore.NameFor(o);
            }
            return map;
        }
    }
}
