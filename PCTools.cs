// PCTools - Windows 10/11 System Manager (Startup / Services / Scheduled Tasks)
// C# WPF application, compiled with the .NET Framework 4.8 in-box compiler.
// Source layout: PCTools.cs (models) + Storage / System / Ai / Theme / MainWindow /
// Dialogs / Program files + Views (XAML) + Diag (self-test, DIAG only).
// Build: see build.cmd in this folder.

using System;
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
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using System.Xml.Linq;
using Microsoft.Win32;

// 程序集版本元数据：csc 没有 Win32 版本资源时，至少让文件属性/崩溃报告
// 能读到明确的版本与产品名，而不是空白。全局特性必须放在所有 using 之后
[assembly: AssemblyTitle("PCTools")]
[assembly: AssemblyDescription("Windows 启动项 / 服务 / 计划任务 / 右键菜单管理工具")]
[assembly: AssemblyProduct("PCTools")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace PCTools
{
    // ------------------------------------------------------------------ Models
    // 四类系统项目的视图模型与批量计划对象。窗口与业务逻辑分别见：
    // PCTools.MainWindow.cs / PCTools.System.cs / PCTools.Ai.cs / PCTools.Storage.cs

    public class StartupItem : IAiAdviceItem, IBackupItem
    {
        public string Name { get; set; }
        public string Status { get; set; }
        public Brush StatusBrush { get; set; }
        public Brush StatusBgBrush { get; set; }
        public string Location { get; set; }
        public string Scope { get; set; }
        public string Command { get; set; }
        public string Search { get; set; }
        public string ApprovalSubPath { get; set; }
        public string ApprovalName { get; set; }
        // 源条目位置：注册表 Run 键或启动文件夹中的文件。写入 StartupApproved 前
        // 必须先确认源仍在，否则会给已被卸载程序删除的条目留下孤儿开关
        public string SourceRegPath { get; set; }
        public string SourceFilePath { get; set; }
        public bool Machine { get; set; }
        // RunOnce 条目：登录时由 Explorer 直接执行，执行后自删。Windows 不读
        // StartupApproved\RunOnce，写进去既不生效也不会显示，所以这类条目只读展示
        public bool OneShot { get; set; }

        // 状态文字的自然排序权重（已启用 → 已禁用）
        public int StatusRank { get { return Rank.OfStatus(Status); } }
        public string AiAdvice { get; set; }
        public Brush AiAdviceBrush { get; set; }
        public Brush AiAdviceBgBrush { get; set; }
        public int AiAdviceRank { get; set; }
        public string BackupText { get; set; }
        public Brush BackupBrush { get; set; }
        public Brush BackupBgBrush { get; set; }
        public int BackupRank { get; set; }
        public bool BackupDiff { get { return string.IsNullOrEmpty(BackupText) || BackupText == "未备份" || BackupText != Status; } }
    }

    public class ServiceItem : IAiAdviceItem, IBackupItem
    {
        public string Name { get; set; }
        public string ServiceName { get; set; }
        public string Status { get; set; }
        public Brush StatusBrush { get; set; }
        public Brush StatusBgBrush { get; set; }
        public string StartType { get; set; }
        public Brush StartTypeBrush { get; set; }
        public Brush StartTypeBgBrush { get; set; }
        // 延迟自动启动只用于详情/搜索：界面上按需求统一显示「自动」
        public bool DelayedAutoStart { get; set; }
        public string ServiceType { get; set; }
        public string Account { get; set; }
        public string Path { get; set; }
        public string Search { get; set; }
        public string Detail { get; set; }

        public int StatusRank { get { return Rank.OfStatus(Status); } }
        public string AiAdvice { get; set; }
        public Brush AiAdviceBrush { get; set; }
        public Brush AiAdviceBgBrush { get; set; }
        public int AiAdviceRank { get; set; }
        public int StartTypeRank { get { return Rank.OfStartType(StartType); } }
        public string BackupText { get; set; }
        public Brush BackupBrush { get; set; }
        public Brush BackupBgBrush { get; set; }
        public int BackupRank { get; set; }
        public bool BackupDiff { get { return string.IsNullOrEmpty(BackupText) || BackupText == "未备份" || BackupText != StartType; } }
    }

    public class TaskItem : IAiAdviceItem, IBackupItem
    {
        public string Name { get; set; }
        public string Status { get; set; }
        public Brush StatusBrush { get; set; }
        public Brush StatusBgBrush { get; set; }
        public string Trigger { get; set; }
        public string Action { get; set; }
        public string TaskPath { get; set; }
        public string LastRun { get; set; }
        public string LastResult { get; set; }
        public string Search { get; set; }
        public string FullPath { get; set; }
        public string Detail { get; set; }
        // 任务是否启用（与「正在运行/就绪」无关）：备份基线只记录启用状态，
        // 否则运行中的任务会被备份成「正在运行」，下次对比必然误报不一致
        public bool Enabled { get; set; }
        public string BackupCurrent { get { return Enabled ? "已启用" : "已禁用"; } }

        public int StatusRank { get { return Rank.OfStatus(Status); } }
        public string AiAdvice { get; set; }
        public Brush AiAdviceBrush { get; set; }
        public Brush AiAdviceBgBrush { get; set; }
        public int AiAdviceRank { get; set; }
        public string BackupText { get; set; }
        public Brush BackupBrush { get; set; }
        public Brush BackupBgBrush { get; set; }
        public int BackupRank { get; set; }
        public bool BackupDiff { get { return string.IsNullOrEmpty(BackupText) || BackupText == "未备份" || BackupText != BackupCurrent; } }
    }

    // 资源管理器右键菜单项：Shell 扩展（按 CLSID 屏蔽）或静态动词（按注册表键加 LegacyDisable）
    public class CtxMenuItem : IAiAdviceItem, IBackupItem
    {
        public string Name { get; set; }
        public string Status { get; set; }
        public Brush StatusBrush { get; set; }
        public Brush StatusBgBrush { get; set; }
        public string Kind { get; set; }        // CtxKind 三个常量之一
        public string Location { get; set; }    // 桌面背景 / 所有文件 / 文件夹 …
        public string Scope { get; set; }       // 当前用户 / 所有用户 / 两者
        public string Id { get; set; }          // CLSID 或 shell 动词相对路径
        public string Handler { get; set; }     // 处理程序 DLL 路径，可为空
        public string RegPath { get; set; }     // HKEY_CLASSES_ROOT\… 完整路径
        public string RelPath { get; set; }     // 相对 Software\Classes 的路径，写 LegacyDisable 用
        public string Detail { get; set; }
        public string Search { get; set; }
        public int Tab { get; set; }            // 与 CtxRoot.Tab 一致：0 全局 / 1 桌面 / 2 驱动器 / 3 文件 / 4 文件夹 / 5 文件和文件夹

        public int StatusRank { get { return Rank.OfStatus(Status); } }
        public string AiAdvice { get; set; }
        public Brush AiAdviceBrush { get; set; }
        public Brush AiAdviceBgBrush { get; set; }
        public int AiAdviceRank { get; set; }
        public string BackupText { get; set; }
        public Brush BackupBrush { get; set; }
        public Brush BackupBgBrush { get; set; }
        public int BackupRank { get; set; }
        public bool BackupDiff { get { return string.IsNullOrEmpty(BackupText) || BackupText == "未备份" || BackupText != Status; } }
    }

    internal static class CtxKind
    {
        internal const string Extension = "Shell 扩展";
        internal const string Verb = "静态动词";
        internal const string Package = "打包扩展";
        internal const string Classic = "全局开关";
    }

    internal static class Rank
    {
        internal static int OfStatus(string s)
        {
            switch (s)
            {
                case "正在运行": return 0;
                case "已启用": return 1;
                // 服务过渡态：启动中视同启用，停止中视同已停止，已暂停介于两者之间，
                // 以前这三类都落到 default(6)，排序时被排到已禁用之后，明显反直觉
                case "启动中": return 1;
                case "一次性": return 1;      // RunOnce：登录时执行，与「已启用」同组
                case "已暂停": return 2;
                case "就绪": return 2;
                case "排队中": return 3;
                case "停止中": return 4;
                case "已停止": return 4;
                case "已禁用": return 5;
                default: return 6;
            }
        }

        internal static int OfStartType(string s)
        {
            switch (s)
            {
                case "自动": return 0;
                case "自动（延迟）": return 0;
                case "手动": return 1;
                case "已禁用": return 2;
                default: return 3;
            }
        }
    }

    internal static class AppFont
    {
        internal static readonly FontFamily Ui = new FontFamily("Microsoft YaHei UI, Microsoft YaHei UI Light, Microsoft YaHei, Segoe UI");
    }

    // 版本号的唯一来源是程序集元数据：界面、窗口标题、User-Agent 都从这里取，
    // 不允许再写字面量，否则改版本时必然漏掉某处
    internal static class AppInfo
    {
        internal const string Name = "PCTools";
        internal static readonly string Version = ReadVersion();
        internal static readonly string Display = Name + " v" + Version;
        internal static readonly string WindowTitle = Display + " - 系统项目管理";

        private static string ReadVersion()
        {
            try { return typeof(AppInfo).Assembly.GetName().Version.ToString(3); }
            catch { return "1.1.0"; }
        }
    }

    internal static class Palette
    {
        internal static Brush Green;
        internal static Brush Red;
        internal static Brush Gray;
        internal static Brush Blue;
        internal static Brush Orange;
        internal static Brush Neutral;
        internal static Brush GreenBg;
        internal static Brush RedBg;
        internal static Brush GrayBg;
        internal static Brush BlueBg;
        internal static Brush OrangeBg;

        static Palette() { Refresh(true); }

        // 状态色随主题切换：浅色用深字浅底，深色用亮字深底
        internal static void Refresh(bool light)
        {
            if (light)
            {
                Green = Make("#107C10"); GreenBg = Make("#E8F6E8");
                Red = Make("#C42B1C"); RedBg = Make("#FCEBE9");
                Gray = Make("#6B7280"); GrayBg = Make("#EFF1F4");
                Blue = Make("#0F6CBD"); BlueBg = Make("#E4F0FB");
                Orange = Make("#B26A00"); OrangeBg = Make("#FDF3E2");
                Neutral = Make("#5B606B");
            }
            else
            {
                Green = Make("#6CCB5F"); GreenBg = Make("#20301C");
                Red = Make("#FF99A4"); RedBg = Make("#3A2024");
                Gray = Make("#A4ACB6"); GrayBg = Make("#2E3238");
                Blue = Make("#63C8FF"); BlueBg = Make("#13293B");
                Orange = Make("#E8B15C"); OrangeBg = Make("#332817");
                Neutral = Make("#B6BDC7");
            }
        }

        internal static Brush Make(string hex)
        {
            SolidColorBrush b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }
    }

    // ------------------------------------------------------------------ Data layer
    internal class RunSource
    {
        internal string Reg;
        internal string Approval;
        internal bool Machine;
        internal string Scope;
        internal string Location;
        internal RunSource(string reg, string approval, bool machine, string scope, string location)
        {
            Reg = reg; Approval = approval; Machine = machine; Scope = scope; Location = location;
        }
    }

    internal class FolderSource
    {
        internal string Dir;
        internal string Approval;
        internal bool Machine;
        internal string Scope;
        internal FolderSource(string dir, string approval, bool machine, string scope)
        {
            Dir = dir; Approval = approval; Machine = machine; Scope = scope;
        }
    }

    // ------------------------------------------------------------------ Context menu registry
    // 资源管理器右键菜单的读写集中在这里：DataProvider 只读、Manager 只写，共用同一套路径常量。
    internal class CtxRoot
    {
        internal string Path;        // 相对 Software\Classes
        internal int Tab;            // 0 全局 / 1 桌面 / 2 驱动器 / 3 文件 / 4 文件夹 / 5 文件和文件夹
        internal string Location;    // 位置列文案
        internal CtxRoot(string path, int tab, string location)
        {
            Path = path; Tab = tab; Location = location;
        }
    }

    // 「AI建议」列的数据契约（云端 AI 批量结果）
    internal interface IAiAdviceItem
    {
        string AiAdvice { get; set; }
        Brush AiAdviceBrush { get; set; }
        Brush AiAdviceBgBrush { get; set; }
        int AiAdviceRank { get; set; }
    }

    // 「备份状态 / 备份启动类型」列的数据契约。
    // BackupDiff 是计算属性：未备份（BackupText 为空或「未备份」）也算不一致，
    // 这样备份之后新增的项目不会在筛选时被漏掉
    internal interface IBackupItem
    {
        string BackupText { get; set; }
        Brush BackupBrush { get; set; }
        Brush BackupBgBrush { get; set; }
        int BackupRank { get; set; }
        bool BackupDiff { get; }
    }

    // 单条分析的结果：逐项批量与自检共用。
    // Intro/Advice 由批量调用方在收尾时统一写库（AnalyzeOne 本身不再逐项开库）
    internal sealed class AnalyzeResult
    {
        internal bool Ok;
        internal string Subject = "";
        internal string Error = "";
        internal string Tag = "";
        internal string Intro = "";
        internal string Advice = "";
        internal int InTokens;
        internal int OutTokens;
        internal bool HasUsage;
    }

    // 批量任务的失败项：必须保留原始业务对象，重试时才能定向重发，不能只存展示文案
    internal sealed class AiFailedItem
    {
        internal int Page;
        internal object Item;
        internal string Key = "";
        internal string Name = "";
        internal string Reason = "";
    }

    // 批量 AI 建议的请求计划：序号 → 存储键 / 原始项，每用户服务实例 → 模板键的别名
    internal sealed class BatchPlan
    {
        internal readonly Dictionary<int, string> Keys = new Dictionary<int, string>();
        internal readonly Dictionary<int, object> Items = new Dictionary<int, object>();
        internal readonly Dictionary<string, string> Aliases = new Dictionary<string, string>();
        internal int Sent;
        internal int Skipped;
    }

    // 服务页的「每用户实例 → 模板」归并必须在整页范围内预先算好。
    // 分批请求时若只按批内元素计算，模板与实例被切到不同批，实例会被当成独立代表，
    // 同一底层服务出现两条互相矛盾的建议，还多花一份 Token。
    // AliasTo 只记录「不发送、复用某条结论」的实例；不在表里的实例就是本批代表
    internal sealed class ServiceScope
    {
        internal readonly HashSet<string> Plain = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<string, string> AliasTo = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // 已经用注册表确证过的每用户实例 key。quick 估算直接查它，不再靠字符串后缀猜
        internal readonly HashSet<string> Instances = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // 实例 key → 注册表确证的模板名。Build 非 quick 路径复用，避免对每个实例再开一次 HKLM
        internal readonly Dictionary<string, string> InstTpl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> reps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        internal ServiceScope(List<object> all)
        {
            if (all == null) return;
            // IsPerUserInstance 每次都要开 HKLM 读服务 Type：只遍历一遍，把实例攒下来
            // 留给第二段处理（Plain 必须先收集完，别名归并才能判断「模板在列表里」）
            List<ServiceItem> insts = null;
            for (int i = 0; i < all.Count; i++)
            {
                ServiceItem si = all[i] as ServiceItem;
                if (si == null) continue;
                if (Manager.IsPerUserInstance(si.ServiceName))
                {
                    if (insts == null) insts = new List<ServiceItem>();
                    insts.Add(si);
                }
                else Plain.Add(si.ServiceName);
            }
            if (insts == null) return;
            for (int i = 0; i < insts.Count; i++)
            {
                ServiceItem si = insts[i];
                string tpl = Manager.TemplateName(si.ServiceName);
                string instKey = AiAdviceStore.KeyService(si);
                Instances.Add(instKey);
                InstTpl[instKey] = tpl;
                if (Plain.Contains(tpl)) { AliasTo[instKey] = tpl; continue; }
                string rep;
                if (reps.TryGetValue(tpl, out rep)) { AliasTo[instKey] = rep; continue; }
                reps[tpl] = instKey;      // 第一个实例作为代表，其余指向它
            }
        }
    }
}
