// PCTools - Windows 11 System Manager (Startup / Services / Scheduled Tasks)
// C# WPF application (PCTools.cs + PCTools.Views.cs), compiled with the .NET Framework 4.8 in-box compiler.
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
using System.Security.Cryptography.X509Certificates;
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

namespace PCTools
{
    // ------------------------------------------------------------------ Models
    public class StartupItem : IAdviceItem, IAiAdviceItem
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
        public bool Machine { get; set; }
        // RunOnce 条目：登录时由 Explorer 直接执行，执行后自删。Windows 不读
        // StartupApproved\RunOnce，写进去既不生效也不会显示，所以这类条目只读展示
        public bool OneShot { get; set; }

        // 状态文字的自然排序权重（已启用 → 已禁用）
        public int StatusRank { get { return Rank.OfStatus(Status); } }
        public string Advice { get; set; }
        public string AdviceNote { get; set; }
        public Brush AdviceBrush { get; set; }
        public Brush AdviceBgBrush { get; set; }
        public int AdviceRank { get; set; }
        public string AiAdvice { get; set; }
        public Brush AiAdviceBrush { get; set; }
        public Brush AiAdviceBgBrush { get; set; }
        public int AiAdviceRank { get; set; }
    }

    public class ServiceItem : IAdviceItem, IAiAdviceItem
    {
        public string Name { get; set; }
        public string ServiceName { get; set; }
        public string Status { get; set; }
        public Brush StatusBrush { get; set; }
        public Brush StatusBgBrush { get; set; }
        public string StartType { get; set; }
        public Brush StartTypeBrush { get; set; }
        public Brush StartTypeBgBrush { get; set; }
        public string ServiceType { get; set; }
        public string Account { get; set; }
        public string Path { get; set; }
        public string Search { get; set; }
        public string Detail { get; set; }

        public int StatusRank { get { return Rank.OfStatus(Status); } }
        public string Advice { get; set; }
        public string AdviceNote { get; set; }
        public Brush AdviceBrush { get; set; }
        public Brush AdviceBgBrush { get; set; }
        public int AdviceRank { get; set; }
        public string AiAdvice { get; set; }
        public Brush AiAdviceBrush { get; set; }
        public Brush AiAdviceBgBrush { get; set; }
        public int AiAdviceRank { get; set; }
        public int StartTypeRank { get { return Rank.OfStartType(StartType); } }
    }

    public class TaskItem : IAdviceItem, IAiAdviceItem
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

        public int StatusRank { get { return Rank.OfStatus(Status); } }
        public string Advice { get; set; }
        public string AdviceNote { get; set; }
        public Brush AdviceBrush { get; set; }
        public Brush AdviceBgBrush { get; set; }
        public int AdviceRank { get; set; }
        public string AiAdvice { get; set; }
        public Brush AiAdviceBrush { get; set; }
        public Brush AiAdviceBgBrush { get; set; }
        public int AiAdviceRank { get; set; }
    }

    // 资源管理器右键菜单项：Shell 扩展（按 CLSID 屏蔽）或静态动词（按注册表键加 LegacyDisable）
    public class CtxMenuItem
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
        internal const string Family = "Microsoft YaHei UI";
        internal static readonly FontFamily Ui = new FontFamily("Microsoft YaHei UI, Microsoft YaHei UI Light, Microsoft YaHei, Segoe UI");
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

    internal static class CtxReg
    {
        internal const string BlockedPath = @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";
        internal const string Classes = @"Software\Classes";
        internal const string ClassicClsid = "{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";
        internal const string ClassicPath = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";

        // 枚举根：按位置分页签（0 全局 / 1 桌面 / 2 驱动器 / 3 文件 / 4 文件夹 / 5 文件和文件夹）。
        // 桌面空白处右键的真实注册位置是 Directory\Background（Cursor/Git/终端等都注册在这里），
        // DesktopBackground 只是 Win11 现代菜单的叠加层，两者都归入桌面页签。
        internal static readonly CtxRoot[] Roots = new CtxRoot[]
        {
            new CtxRoot(@"DesktopBackground", 1, "桌面背景"),
            new CtxRoot(@"Directory\Background", 1, "桌面背景"),
            new CtxRoot(@"Drive", 2, "驱动器"),
            new CtxRoot(@"*", 3, "所有文件"),
            new CtxRoot(@"Directory", 4, "文件夹"),
            new CtxRoot(@"AllFilesystemObjects", 5, "文件和文件夹")
        };

        internal static RegistryKey Hive(bool machine)
        {
            return machine ? (RegistryKey)Registry.LocalMachine : Registry.CurrentUser;
        }

        internal static string PermissionText(bool machine)
        {
            return machine
                ? "权限不足：需要管理员权限才能写入 HKEY_LOCAL_MACHINE，请点击标题栏「以管理员身份重启」后重试"
                : "权限不足：无法写入当前用户注册表";
        }

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int SHLoadIndirectString(string src, StringBuilder buf, int cch, IntPtr reserved);

        // "@C:\x\foo.dll,-1234" 这类间接字符串交给 shell 解析；普通文本原样返回，解析失败返回空串让上层降级
        internal static string Indirect(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Trim();
            if (s.Length == 0 || s[0] != '@') return s;
            // @ 后面可能带环境变量：@%SystemRoot%\system32\shell32.dll,-123
            string body = s.Substring(1).Trim();
            // 只有「,-数字」这种资源 ID 后缀才是分隔符。路径本身可能含逗号
            //（C:\Tools, Inc\a.dll,-1），按最后一个逗号硬切会把合法本地路径判成非本地
            int comma = body.LastIndexOf(',');
            string file = body;
            if (comma > 0 && IsResourceIdSuffix(body.Substring(comma + 1).Trim()))
                file = body.Substring(0, comma);
            try { file = Environment.ExpandEnvironmentVariables(file); }
            catch { }
            // 非本机路径不解析：否则一个可写的注册表值就能让程序去连任意的 UNC 目标
            if (!IsLocalPath(file)) return "";
            try
            {
                StringBuilder sb = new StringBuilder(512);
                if (SHLoadIndirectString(s, sb, sb.Capacity, IntPtr.Zero) == 0 && sb.Length > 0)
                    return sb.ToString().Trim();
            }
            catch { }
            return "";
        }

        // 间接字符串的资源 ID 后缀：可选负号 + 十进制数字（-123 / 123）
        private static bool IsResourceIdSuffix(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            int i = s[0] == '-' ? 1 : 0;
            if (i >= s.Length) return false;
            for (; i < s.Length; i++)
                if (s[i] < '0' || s[i] > '9') return false;
            return true;
        }

        internal static bool LooksLikeClsid(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            string t = s.Trim();
            if (t.Length != 38 || t[0] != '{' || t[t.Length - 1] != '}') return false;
            for (int i = 1; i < t.Length - 1; i++)
            {
                char c = t[i];
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex && c != '-') return false;
            }
            return true;
        }

        // 只接受本机固定盘上的绝对路径（X:\...）。注册表值可被任意写入，@\\host\share\x.dll,-1 这类引用
        // 会让 shell/版本查询去连 UNC：触发 SMB 认证外泄，或在不可达时长时间阻塞枚举线程。
        // 注意：Z:\ 形态的「映射网络驱动器」同样会走 SMB，必须靠 DriveType 排除，光看盘符不够。
        private sealed class DriveEntry
        {
            internal bool Fixed;
            internal long Expires;      // UTC ticks
        }

        private static readonly Dictionary<char, DriveEntry> driveKindCache = new Dictionary<char, DriveEntry>();
        private static readonly object driveGate = new object();
        // 盘符类型不是永久不变：U 盘拔出、网络盘映射变化都会改变结果。
        // 缓存 60 秒既避免每次判定都查设备表，也不会长期用错类型
        private const int DriveKindTtlSeconds = 60;

        private static bool IsFixedDrive(char letter)
        {
            char c = char.ToUpperInvariant(letter);
            long now = DateTime.UtcNow.Ticks;
            lock (driveGate)
            {
                DriveEntry hit;
                if (driveKindCache.TryGetValue(c, out hit) && hit.Expires > now) return hit.Fixed;
            }
            bool ok = false;
            try
            {
                DriveInfo di = new DriveInfo(c.ToString());
                // GetDriveType 只查本机设备表，不会触发网络 I/O
                DriveType t = di.DriveType;
                ok = t == DriveType.Fixed || t == DriveType.Removable || t == DriveType.CDRom || t == DriveType.Ram;
            }
            catch { ok = false; }
            lock (driveGate)
            {
                DriveEntry e = new DriveEntry();
                e.Fixed = ok;
                e.Expires = now + TimeSpan.FromSeconds(DriveKindTtlSeconds).Ticks;
                driveKindCache[c] = e;
            }
            return ok;
        }

        // 去掉引号、空白与 \??\ / \\?\ 设备前缀，便于统一判定
        internal static string Fixup(string p)
        {
            if (string.IsNullOrEmpty(p)) return "";
            string t = p.Trim().Trim('"');
            if (t.StartsWith(@"\??\", StringComparison.Ordinal)) t = t.Substring(4);
            else if (t.StartsWith(@"\\?\", StringComparison.Ordinal)) t = t.Substring(4);
            return t.Trim();
        }

        internal static bool IsLocalPath(string p)
        {
            string t = Fixup(p);
            if (t.Length < 4) return false;
            if (!char.IsLetter(t[0]) || t[1] != ':') return false;
            if (t[2] != '\\' && t[2] != '/') return false;
            // X:\\server\share 这种双分隔符不是合法本地路径
            if (t[3] == '\\' || t[3] == '/') return false;
            return IsFixedDrive(t[0]);
        }

        // 路径可信度分类：服务 ImagePath 常见 \SystemRoot\… 与 system32\… 这类系统相对写法，
        // 既不是本机绝对路径也不代表「网络路径」，上层据此给出不同说明
        internal enum PathKind { Local, SystemRelative, Remote }

        internal static PathKind Classify(string p)
        {
            if (IsLocalPath(p)) return PathKind.Local;
            string t = Fixup(p);
            if (t.Length == 0) return PathKind.SystemRelative;
            // 未经 ExeFromCommand 的调用（直接传 ImagePath / Run 值）也要能识别
            // %ProgramFiles%\… 这类写法，否则会被判成网络路径
            try { t = Environment.ExpandEnvironmentVariables(t).Trim(); } catch { }
            if (IsLocalPath(t)) return PathKind.Local;
            // %SystemRoot% 这类环境变量在上一行已展开，这里只需识别展开前的原始写法
            if (t.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase) ||
                t.StartsWith(@"system32\", StringComparison.OrdinalIgnoreCase))
                return PathKind.SystemRelative;
            // 裸文件名 / 无盘符的相对路径（rundll32.exe、cmd.exe、foo\bar.exe）：
            // 既不是网络路径，也不该按网络处理。以前一律落到 Remote，内置建议会把
            // 「rundll32.exe x.dll,Entry」这种最常见的自启写法显示成「网络地址」并跳过校验
            if (IsRelativePath(t)) return PathKind.SystemRelative;
            return PathKind.Remote;
        }

        // 无盘符、不是 UNC/根相对、也不是带 scheme 的 URL → 按相对路径处理
        private static bool IsRelativePath(string t)
        {
            if (string.IsNullOrEmpty(t)) return false;
            if (t[0] == '\\' || t[0] == '/') return false;
            int colon = t.IndexOf(':');
            int slash = t.IndexOfAny(new char[] { '\\', '/' });
            if (colon >= 0 && (slash < 0 || colon < slash)) return false;   // C:xxx 或 http://…
            return true;
        }

        internal static string DefaultValue(RegistryKey k)
        {
            try
            {
                object v = k.GetValue(null);
                return v == null ? "" : Convert.ToString(v);
            }
            catch { return ""; }
        }

        internal static string ClsidHandler(string clsid)
        {
            try
            {
                using (RegistryKey k = Registry.ClassesRoot.OpenSubKey(@"CLSID\" + clsid + @"\InprocServer32"))
                {
                    if (k == null) return "";
                    return Indirect(DefaultValue(k)).Trim('"');
                }
            }
            catch (Exception ex) { DiagLog.Swallow("CLSID 处理程序读取 " + clsid, ex); return ""; }
        }

        // FileVersionInfo 读盘一次要几毫秒：右键页每次刷新都会重读同一批处理程序，
        // 缓存文件描述（键为小写路径，上限防止异常机器上的无限增长）
        private sealed class DescEntry
        {
            internal string Value;
            internal long Expires;   // UTC ticks
        }

        private static readonly Dictionary<string, DescEntry> descCache = new Dictionary<string, DescEntry>(StringComparer.OrdinalIgnoreCase);
        private static readonly object descGate = new object();
        private const int DescCacheMax = 600;
        // 空描述只缓存 30 秒：新装的 Shell 扩展不必重启程序就能显示出来
        private const int DescMissTtlSeconds = 30;
        // 非空描述缓存 5 分钟：程序升级替换文件后不会一直显示旧描述
        private const int DescHitTtlSeconds = 300;

        // 缓存满员时先淘汰已过期项，腾不出空间才整体清空：
        // 直接全清会让下一次刷新对全部条目重新读盘（FileVersionInfo 逐个查）
        internal static void PurgeExpired<K, V>(Dictionary<K, V> cache, Func<V, long> expires)
        {
            List<K> dead = null;
            long now = DateTime.UtcNow.Ticks;
            foreach (KeyValuePair<K, V> kv in cache)
                if (expires(kv.Value) <= now) (dead ?? (dead = new List<K>())).Add(kv.Key);
            if (dead == null) { cache.Clear(); return; }
            for (int i = 0; i < dead.Count; i++) cache.Remove(dead[i]);
        }

        internal static string FileDesc(string path)
        {
            try
            {
                if (!IsLocalPath(path)) return "";
                string real = Fixup(path);
                string key = real.ToLowerInvariant();
                long now = DateTime.UtcNow.Ticks;
                lock (descGate)
                {
                    DescEntry hit;
                    if (descCache.TryGetValue(key, out hit) && hit.Expires > now)
                        return hit.Value;
                }
                string desc = "";
                if (File.Exists(real))
                {
                    FileVersionInfo vi = FileVersionInfo.GetVersionInfo(real);
                    desc = vi.FileDescription == null ? "" : vi.FileDescription.Trim();
                }
                // 空结果也入缓存（否则每次刷新都会重试不存在的 DLL），但带过期时间：
                // 处理程序是运行期才注册的情况很常见，永久负缓存会让它一直显示空描述
                lock (descGate)
                {
                    if (descCache.Count >= DescCacheMax) PurgeExpired(descCache, en => en.Expires);
                    DescEntry e = new DescEntry();
                    e.Value = desc;
                    // 两类都带过期：处理程序是运行期才注册的情况很常见，永久负缓存会让它
                    // 一直显示空描述；非空描述也会因程序升级而变旧
                    e.Expires = now + TimeSpan.FromSeconds(desc.Length == 0 ? DescMissTtlSeconds : DescHitTtlSeconds).Ticks;
                    descCache[key] = e;
                }
                return desc;
            }
            catch { return ""; }
        }

        // 显示名三级降级：CLSID 默认值 → 处理程序文件描述 → ContextMenuHandlers 子键名
        internal static string ExtName(string clsid, string fallback, out string handler)
        {
            handler = ClsidHandler(clsid);
            string name = "";
            try
            {
                using (RegistryKey k = Registry.ClassesRoot.OpenSubKey(@"CLSID\" + clsid))
                {
                    if (k != null) name = Indirect(DefaultValue(k));
                }
            }
            catch { }
            if (name.Length > 0) return name;
            string desc = FileDesc(handler);
            if (desc.Length > 0) return desc;
            return fallback;
        }

        // 注册范围：用户侧 / 系统侧是否各有一份
        internal static string ExtScope(string clsid)
        {
            bool u = false, m = false;
            try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Classes + @"\CLSID\" + clsid)) u = k != null; }
            catch { }
            try { using (RegistryKey k = Registry.LocalMachine.OpenSubKey(Classes + @"\CLSID\" + clsid)) m = k != null; }
            catch { }
            return ScopeText(u, m);
        }

        internal static string ScopeText(bool user, bool machine)
        {
            if (user && machine) return "当前用户 + 所有用户";
            if (user) return "当前用户";
            if (machine) return "所有用户";
            return "未注册";
        }

        // Blocked 键的值名按 Windows 标准是带大括号的 {CLSID}。历史上打包扩展与
        // 传统扩展各存了一种格式（前者带 {}、后者裸 GUID），互相读不到对方写入的
        // 屏蔽项，裸 GUID 写入的值 Shell 也可能不识别。这里在读写入口统一规格化，
        // 两种历史格式互相兼容，落库后都是 {CLSID}
        private static string BlockedName(string clsid)
        {
            if (string.IsNullOrEmpty(clsid)) return clsid;
            string t = clsid.Trim();
            if (t.Length == 0) return t;
            if (t[0] == '{') return t;
            if (LooksLikeClsid(t)) return "{" + t.ToUpperInvariant() + "}";
            return t;
        }

        internal static bool IsExtBlocked(bool machine, string clsid)
        {
            try
            {
                using (RegistryKey k = Hive(machine).OpenSubKey(BlockedPath))
                {
                    if (k == null) return false;
                    return k.GetValue(BlockedName(clsid)) != null;
                }
            }
            catch { return false; }
        }

        internal static bool IsVerbDisabled(bool machine, string relPath)
        {
            try
            {
                using (RegistryKey k = Hive(machine).OpenSubKey(Classes + @"\" + relPath))
                {
                    if (k == null) return false;
                    return k.GetValue("LegacyDisable") != null;
                }
            }
            catch { return false; }
        }

        // 0 = Win11 新菜单，1 = 经典菜单（默认值为空串），2 = 第三方工具接管（默认值是真实 DLL）
        internal static int ClassicState()
        {
            int user = ClassicStateOf(false);
            return user != 0 ? user : ClassicStateOf(true);
        }

        private static int ClassicStateOf(bool machine)
        {
            try
            {
                using (RegistryKey k = Hive(machine).OpenSubKey(ClassicPath))
                {
                    if (k == null) return 0;
                    using (RegistryKey ip = k.OpenSubKey("InprocServer32"))
                    {
                        // 没有 InprocServer32 的 CLSID 键无法承载 shell 扩展（其它工具/手工
                        // 编辑可能残留空键），不算「第三方接管」，落回另一个 hive 的状态
                        if (ip == null) return 0;
                        object v = ip.GetValue(null);
                        string s = v == null ? "" : Convert.ToString(v);
                        return string.IsNullOrEmpty(s) ? 1 : 2;
                    }
                }
            }
            catch { return 0; }
        }

        // 该 hive 下是否启用了经典菜单（空默认值的覆盖壳）
        internal static bool IsClassicEnabled(bool machine)
        {
            try
            {
                using (RegistryKey ip = Hive(machine).OpenSubKey(ClassicPath + @"\InprocServer32"))
                {
                    if (ip == null) return false;
                    string s = Convert.ToString(ip.GetValue(null));
                    return string.IsNullOrEmpty(s);
                }
            }
            catch { return false; }
        }

        // ---------------- 写入 ----------------
        internal static string SetExtBlocked(string clsid, bool machine)
        {
            try
            {
                using (RegistryKey k = Hive(machine).CreateSubKey(BlockedPath))
                {
                    if (k == null) return "无法创建注册表项：" + BlockedPath;
                    k.SetValue(BlockedName(clsid), "", RegistryValueKind.String);
                }
                return null;
            }
            catch (UnauthorizedAccessException) { return PermissionText(machine); }
            catch (Exception ex) { return ex.Message; }
        }

        internal static string ClearExtBlocked(string clsid, bool machine)
        {
            try
            {
                using (RegistryKey k = Hive(machine).OpenSubKey(BlockedPath, true))
                {
                    if (k == null || k.GetValue(BlockedName(clsid)) == null) return null;
                    k.DeleteValue(BlockedName(clsid), false);
                }
                return null;
            }
            catch (UnauthorizedAccessException) { return PermissionText(machine); }
            catch (Exception ex) { return ex.Message; }
        }

        internal static string SetVerbDisabled(string relPath, bool machine)
        {
            try
            {
                using (RegistryKey k = Hive(machine).CreateSubKey(Classes + @"\" + relPath))
                {
                    if (k == null) return "无法创建注册表项：" + Classes + @"\" + relPath;
                    k.SetValue("LegacyDisable", "", RegistryValueKind.String);
                }
                return null;
            }
            catch (UnauthorizedAccessException) { return PermissionText(machine); }
            catch (Exception ex) { return ex.Message; }
        }

        internal static string ClearVerbDisabled(string relPath, bool machine)
        {
            try
            {
                string path = Classes + @"\" + relPath;
                using (RegistryKey k = Hive(machine).OpenSubKey(path, true))
                {
                    if (k == null || k.GetValue("LegacyDisable") == null) return null;
                    k.DeleteValue("LegacyDisable", false);
                }
                TryDeleteEmptyKey(machine, path);
                return null;
            }
            catch (UnauthorizedAccessException) { return PermissionText(machine); }
            catch (Exception ex) { return ex.Message; }
        }

        // 我们只在用户侧建了一个覆盖壳，删完值后若键已空就顺手清掉，别留垃圾
        private static void TryDeleteEmptyKey(bool machine, string path)
        {
            try
            {
                using (RegistryKey k = Hive(machine).OpenSubKey(path))
                {
                    if (k == null) return;
                    if (k.SubKeyCount == 0 && k.ValueCount == 0)
                    {
                        k.Close();
                        Hive(machine).DeleteSubKey(path, false);
                    }
                }
            }
            catch { }
        }

        // on = 启用经典菜单（写入空默认值的覆盖壳）；false = 关闭经典菜单（删除覆盖壳）。
        // 与「屏蔽」语义相反，别按字面理解
        internal static string SetClassic(bool on, bool machine)
        {
            try
            {
                if (on)
                {
                    using (RegistryKey k = Hive(machine).CreateSubKey(ClassicPath + @"\InprocServer32"))
                    {
                        if (k == null) return "无法创建注册表项：" + ClassicPath;
                        k.SetValue(null, "", RegistryValueKind.String);
                    }
                }
                else
                {
                    // 默认值是真实 DLL 时说明经典菜单由第三方工具接管，删掉会破坏对方的功能
                    using (RegistryKey ip = Hive(machine).OpenSubKey(ClassicPath + @"\InprocServer32"))
                    {
                        if (ip != null)
                        {
                            string s = Convert.ToString(ip.GetValue(null));
                            if (!string.IsNullOrEmpty(s)) return "经典菜单由第三方工具接管，请在对应工具里关闭";
                        }
                    }
                    Hive(machine).DeleteSubKeyTree(ClassicPath, false);
                    // 只关掉一侧时菜单样式不会变：明确告知，而不是假装成功
                    if (IsClassicEnabled(!machine))
                        return Manager.PartialPrefix + "已关闭" + (machine ? "系统级" : "当前用户") +
                               "的经典菜单，但" + (machine ? "当前用户" : "系统级") + "设置仍在" +
                               (machine ? "" : "（需要管理员权限）");
                }
                return null;
            }
            catch (UnauthorizedAccessException) { return PermissionText(machine); }
            catch (Exception ex) { return ex.Message; }
        }

        // ---------------- 统一分派（Manager 调用） ----------------
        // 启用 = 去掉屏蔽 / 打开经典菜单；machine 只对「禁用」有意义
        internal static string EnableCtx(CtxMenuItem it)
        {
            if (it == null) return "项目为空";
            if (it.Kind == CtxKind.Classic) return SetClassic(true, false);

            if (it.Kind == CtxKind.Extension || it.Kind == CtxKind.Package)
            {
                string err = ClearExtBlocked(it.Id, false);
                if (err != null) return err;
                ClearExtBlocked(it.Id, true);        // 系统级尽力而为，无权限不算失败
                // 用户级屏蔽已解除（本次操作的目标已达成），系统级残留只是提醒，不算失败
                if (IsExtBlocked(true, it.Id))
                    return Manager.PartialPrefix + "已解除当前用户屏蔽，但系统级屏蔽仍在（需要管理员权限）";
                return null;
            }

            string e2 = ClearVerbDisabled(it.RelPath, false);
            if (e2 != null) return e2;
            ClearVerbDisabled(it.RelPath, true);
            if (IsVerbDisabled(true, it.RelPath))
                return Manager.PartialPrefix + "已解除当前用户禁用，但系统级禁用仍在（需要管理员权限）";
            return null;
        }

        internal static string DisableCtx(CtxMenuItem it, bool machine)
        {
            if (it == null) return "项目为空";
            if (it.Kind == CtxKind.Classic) return SetClassic(false, machine);
            if (it.Kind == CtxKind.Extension || it.Kind == CtxKind.Package)
                return SetExtBlocked(it.Id, machine);
            return SetVerbDisabled(it.RelPath, machine);
        }
    }

    // ------------------------------------------------------------------ Advice
    // 「建议」列判定引擎（等级 1 建议保留 / 2 可改手动 / 3 按需保留 / 4 可禁用 / 5 建议禁用）
    // 数据源：Black Viper Windows 10 服务配置表（Wayback Machine 2023-05 存档快照，265 项，
    // 默认启动类型 vs Safe 配置对比）+ Win11 社区优化清单（shoober420/windows11-scripts）
    // + 数字签名（Authenticode，微软组件优先保留）+ 路径启发式（临时目录/AppData 残留）
    // 「AI建议」列的数据契约（与内置建议分开，云端 AI 批量结果）
    internal interface IAiAdviceItem
    {
        string AiAdvice { get; set; }
        Brush AiAdviceBrush { get; set; }
        Brush AiAdviceBgBrush { get; set; }
        int AiAdviceRank { get; set; }
    }

    internal interface IAdviceItem
    {
        string Advice { get; set; }
        string AdviceNote { get; set; }
        Brush AdviceBrush { get; set; }
        Brush AdviceBgBrush { get; set; }
        int AdviceRank { get; set; }
    }

    internal static class Adviser
    {
        private sealed class Kb { internal int Rank; internal string Disp; internal string Note; }

        private sealed class SignCacheEntry
        {
            internal bool Value;
            internal long Expires;      // UTC ticks
        }

        private static readonly Dictionary<string, SignCacheEntry> signCache =
            new Dictionary<string, SignCacheEntry>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, SignCacheEntry> trustCache =
            new Dictionary<string, SignCacheEntry>(StringComparer.OrdinalIgnoreCase);
        private const int SignCacheMax = 2000;
        private const int SignCacheTtlMinutes = 30;
        private static readonly string[] RankNames = new string[] { "", "建议保留", "可改手动", "按需保留", "可禁用", "建议禁用" };

        // 只在 EnsureKb 里运行时 Split，不需要编译期常量；用 const 会在常量表多存一份约 30KB 副本
        private static readonly string ServiceKbData = @"axinstsv|1|ActiveX Installer (AxInstSV)|
ajrouter|1|AllJoyn Router Service|
appreadiness|1|App Readiness|
apphostsvc|1|Application Host Helper Service **|If installed (Automatic, Running)
appidsvc|1|Application Identity|
appinfo|2|Application Information|
alg|4|Application Layer Gateway Service|Old functionality no longer needed.
appmgmt|1|Application Management|Required for Group Policy software management
appxsvc|1|AppX Deployment Service (AppXSVC)|Windows Store integration. Cannot be disabled via services.msc
assignedaccessmanagersvc|1|AssignedAccessManager Service|
aspnet_state|1|ASP.NET State Service **|If installed: (Manual)
tzautoupdate|1|Auto Time Zone Updater|
bthavctpsvc|1|AVCTP service|Do not disable if you are using Bluetooth Audio Device or Wireless Headphones.
bits|3|Background Intelligent Transfer Service|Manual or Automatic (Delayed Start, Running) depending on other optional service
brokerinfrastructure|1|Background Tasks Infrastructure Service|Cannot be disabled via services.msc
bfe|1|Base Filtering Engine|Do not disable due to Windows Firewall integration.
bdesvc|1|BitLocker Drive Encryption Service|Do not disable if using storage encryption.
wbengine|1|Block Level Backup Engine Service|Used by Windows Backup
btagservice|1|Bluetooth Audio Gateway Service|New. Wireless headsets.
bthserv|1|Bluetooth Support Service|Disable if no requirement for Bluetooth devices is desired.
bluetoothuserservice_?????|1|Bluetooth User Support Service_?????|New
peerdistsvc|4|BranchCache|Used by Windows Update for download sharing.
camsvc|1|Capability Access Manager Service|
captureservice_?????|1|CaptureService_?????|New. OneCore Capture Service.
certpropsvc|1|Certificate Propagation|Used with Smart Card login. No harm in keeping the default Manual mode.
c2wts|1|Claims to Windows Token Service **|If installed: (Manual)
nfsclnt|4|Client for NFS|Network File System Client available in Education/Enterprise versions.
clipsvc|1|Client License Service (ClipSVC)|Cannot be disabled via services.msc
keyiso|2|CNG Key Isolation|Do not disable.
eventsystem|1|COM+ Event System|Do not disable.
comsysapp|1|COM+ System Application|Do not disable.
browser|1|Computer Browser|Network discovery of systems on local network.
cdpsvc|3|Connected Device Platform Service|
cdpusersvc_?????|1|Connected Devices Platform User Service_?????|
diagtrack|1|Connected User Experiences and Telemetry|Feedback and Diagnostics.
pimindexmaintenancesvc_?????|1|Contact Data_?????|Cannot be disabled via services.msc
coremessagingregistrar|1|CoreMessaging|Cannot be disabled via services.msc
vaultsvc|1|Credential Manager|Do not disable.
cryptsvc|1|Cryptographic Services|Do not disable.
dssvc|1|Data Sharing Service|
dusmsvc|1|Data Usage|
dcomlaunch|1|DCOM Server Process Launcher|Cannot be disabled via services.msc
dosvc|1|Delivery Optimization|
deviceassociationservice|1|Device Association Service|
deviceinstall|1|Device Install Service|
dmenrollmentsvc|1|Device Management Enrollment Service|
dsmsvc|1|Device Setup Manager|
devicepickerusersvc_?????|1|DevicePicker_?????|New. Cannot be disabled via services.msc
devicesflowusersvc_?????|1|DevicesFlow_?????|Cannot be disabled via services.msc
devquerybroker|1|DevQuery Background Discovery Broker|
dhcp|1|DHCP Client|Do not disable.
diagsvc|1|Diagnostic Execution Service|
dps|1|Diagnostic Policy Service|
wdiservicehost|1|Diagnostic Service Host|
wdisystemhost|1|Diagnostic System Host|
trkwks|1|Distributed Link Tracking Client|
msdtc|1|Distributed Transaction Coordinator|
dmwappushsvc|4|dmwappushsvc|
dnscache|3|DNS Client|Do not disable.
mapsbroker|4|Downloaded Maps Manager|I do not use Windows/Bing maps. Google for the win here.
dsrolesvc|1|DS Role Server **|If installed: (Manual)
embeddedmode|1|Embedded Mode|
efs|1|Encrypting File System (EFS)|Do not disable.
entappsvc|1|Enterprise App Management Service|Cannot be disabled via services.msc
eaphost|1|Extensible Authentication Protocol Service|
fax|3|Fax **|
fhsvc|1|File History Service|Used by Windows Backup.
fdphost|1|Function Discovery Provider Host|
fdrespub|1|Function Discovery Resource Publication|
bcastdvruserservice_?????|1|GameDVR and Broadcast User Service_?????|New. Cannot be disabled via services.msc
lfsvc|4|Geolocation Service|
graphicsperfsvc|1|GraphicsPerfSvc|
gpsvc|1|Group Policy Client|Cannot be disabled via services.msc
hns|1|Host Network Service **|If installed: (Manual)
hidserv|1|Human Interface Device Service|Manufactures function keys (volume, screen brightness, etc.), generally needed on laptops with hotkeys
hvhost|4|HV Host Service|
vmickvpexchange|4|Hyper-V Data Exchange Service|
vmicguestinterface|4|Hyper-V Guest Service Interface|
vmicshutdown|4|Hyper-V Guest Shutdown Service|
vmicheartbeat|4|Hyper-V Heartbeat Service|
vmcompute|1|Hyper-V Host Compute Service **|If installed: (Manual, Trigger Start, Running)
vmicvmsession|4|Hyper-V PowerShell Direct Service|
vmicrdv|4|Hyper-V Remote Desktop Virtualization Service|
vmictimesync|4|Hyper-V Time Synchronization Service|
vmms|3|Hyper-V Virtual Machine Management **|
vmicvss|4|Hyper-V Volume Shadow Copy Requestor|
iisadmin|3|IIS Admin Service **|Do not install.
ikeext|1|IKE and AuthIP IPsec Keying Modules|
irmon|4|Infrared monitor service|File transfer via infrared devices.
ui0detect|1|Interactive Services Detection|
sharedaccess|4|Internet Connection Sharing (ICS)|Old service no longer needed.
iphlpsvc|1|IP Helper|IPv6 translation.
ipxlatcfgsvc|1|IP Translation Configuration Service|IPv6 translation.
policyagent|1|IPsec Policy Agent|
ktmrm|1|KtmRm for Distributed Transaction Coordinator|
lltdsvc|1|Link-Layer Topology Discovery Mapper|
wlpasvc|1|Local Profile Assistant Service|
lsm|1|Local Session Manager|Cannot be disabled via services.msc
lpdsvc|3|LPD Service **|
lxssmanager|1|LxssManager **|
msmq|3|Message Queuing **|
msmqtriggers|3|Message Queuing Triggers **|
messagingservice_?????|1|MessagingService_?????|
diagnosticshub.standardcollector.service|1|Microsoft (R) Diagnostics Hub Standard Collector Service|
wlidsvc|1|Microsoft Account Sign-in Assistant|Running if using MS account to log in to computer.
appvclient|1|Microsoft App-V Client|
ftpsvc|3|Microsoft FTP Service **|
msiscsi|4|Microsoft iSCSI Initiator Service|Enable only if iSCSI device connectivity is required (rare for home user).
mskeyboardfilter|3|Microsoft Keyboard Filter **|
ngcsvc|1|Microsoft Passport|Cannot be disabled via services.msc
ngcctnrsvc|1|Microsoft Passport Container|Cannot be disabled via services.msc
swprv|2|Microsoft Software Shadow Copy Provider|Used by Windows Backup.
smphost|1|Microsoft Storage Spaces SMP|Probably One Drive functionality.
installservice|1|Microsoft Store Install Service|
smsrouter|4|Microsoft Windows SMS Router Service|
wmsrepair|3|MultiPoint Repair Service **|Leave Not Installed.
wms|3|MultiPoint Service **|Leave Not Installed.
naturalauthentication|1|Natural Authentication|Silly bio-metrics or face recognition login.
netmsmqactivator|3|Net.Msmq Listener Adapter **|Leave Not Installed.
netpipeactivator|3|Net.Pipe Listener Adapter **|Leave Not Installed.
nettcpactivator|3|Net.Tcp Listener Adapter **|Leave Not Installed.
nettcpportsharing|3|Net.Tcp Port Sharing Service **|Leave Not Installed.
netlogon|1|Netlogon|Used only with a domain controller environment.
ncdautosetup|2|Network Connected Devices Auto-Setup|
ncbservice|2|Network Connection Broker|
netman|1|Network Connections|
ncasvc|1|Network Connectivity Assistant|
netprofm|1|Network List Service|
nlasvc|1|Network Location Awareness|
netsetupsvc|1|Network Setup Service|
nsi|1|Network Store Interface Service|
cscservice|4|Offline Files|
ssh-agent|1|OpenSSH Authentication Agent|New.
defragsvc|1|Optimize Drives|
wpcmonsvc|4|Parental Controls|New.
semgrsvc|4|Payments and NFC/SE Manager|Near Field Communication is a mobile phone technology. Not needed on desktops and most laptops without NFC hardware
pnrpsvc|1|Peer Name Resolution Protocol|
p2psvc|1|Peer Networking Grouping|
p2pimsvc|1|Peer Networking Identity Manager|
perfhost|1|Performance Counter DLL Host|
pla|1|Performance Logs & Alerts|
phonesvc|4|Phone Service|Windows Phone Service.
plugplay|1|Plug and Play|Do not disable.
pnrpautoreg|1|PNRP Machine Name Publication Service|
wpdbusenum|1|Portable Device Enumerator Service|
power|1|Power|
spooler|1|Print Spooler|Do not disable.
printnotify|1|Printer Extensions and Notifications|
printworkflowusersvc_?????|1|PrintWorkflow_?????|Cannot be disabled via services.msc
wercplsupport|1|Problem Reports and Solutions Control Panel Support|
pcasvc|1|Program Compatibility Assistant Service|
qwave|1|Quality Windows Audio Video Experience|
rmsvc|1|Radio Management Service|Keep Manual for Mobile devices, Disabled stops all wireless communication, including Wi-Fi and Bluetooth
rasauto|1|Remote Access Auto Connection Manager|
rasman|1|Remote Access Connection Manager|
sessionenv|2|Remote Desktop Configuration|
termservice|2|Remote Desktop Services|
umrdpservice|2|Remote Desktop Services UserMode Port Redirector|
rpcss|1|Remote Procedure Call (RPC)|Cannot be disabled via services.msc
rpclocator|4|Remote Procedure Call (RPC) Locator|No function in Windows 10.
remoteregistry|1|Remote Registry|
retaildemo|4|Retail Demo Service|Not disabled by default? Really?
iprip|3|RIP Listener **|Leave Not Installed.
remoteaccess|1|Routing and Remote Access|
rpceptmapper|1|RPC Endpoint Mapper|Cannot be disabled via services.msc
seclogon|1|Secondary Logon|
sstpsvc|1|Secure Socket Tunneling Protocol Service|SSTP VPN Capability.
samss|1|Security Accounts Manager|
wscsvc|3|Security Center|
sensordataservice|4|Sensor Data Service|Leave as default for laptops and tablets.
sensrsvc|4|Sensor Monitoring Service|Leave as default for laptops and tablets.
sensorservice|4|Sensor Service|Leave as default for laptops and tablets.
lanmanserver|1|Server|
shpamsvc|1|Shared PC Account Manager|
shellhwdetection|1|Shell Hardware Detection|Autoplay
simptcp|3|Simple TCP/IP Services **|Keep Not Installed.
scardsvr|1|Smart Card|Not needed if Smart Card login is not in use.
scdeviceenum|4|Smart Card Device Enumeration Service|Not needed if Smart Card login is not in use.
scpolicysvc|4|Smart Card Removal Policy|Not needed if Smart Card login is not in use.
snmp|3|SNMP Service **|Keep Not Installed.
snmptrap|4|SNMP Trap|
sppsvc|3|Software Protection|Cannot be disabled via services.msc
sharedrealitysvc|1|Spatial Data Service|Virtual Reality data manager.
svsvc|1|Spot Verifier|
ssdpsrv|1|SSDP Discovery|UPnP functionality. Possible security risk.
staterepository|1|State Repository Service|Cannot be disabled via services.msc
wiarpc|1|Still Image Acquisition Events|
storsvc|1|Storage Service|Required for Windows Store.
tieringengineservice|1|Storage Tiers Management|
sysmain|1|Superfetch|
onesyncsvc_?????|1|Sync Host_?????|
sens|1|System Event Notification Service|
systemeventsbroker|3|System Events Broker|Cannot be disabled via services.msc
sgrmbroker|1|System Guard Runtime Monitor Broker|New.
schedule|1|Task Scheduler|Cannot be disabled via services.msc
lmhosts|2|TCP/IP NetBIOS Helper|
tapisrv|1|Telephony|
themes|1|Themes|
timebroker|2|Time Broker|
tabletinputservice|4|Touch Keyboard and Handwriting Panel Service|Keep in Manual for laptop touch screens and tablets.
uwfservcingsvc|1|Unified Write Filter Servicing Helper Service **|Keep Not Installed. If installed: (Disabled)
usosvc|1|Update Orchestrator Service for Windows Update|
upnphost|1|UPnP Device Host|
userdatasvc_?????|1|User Data Access_?????|Cannot be disabled via services.msc
unistoresvc_?????|1|User Data Storage_?????|Cannot be disabled via services.msc
uevagentservice|1|User Experience Virtualization Service|
usermanager|3|User Manager|
profsvc|1|User Profile Service|
vds|1|Virtual Disk|
vss|1|Volume Shadow Copy|Windows Backup.
w3logsvc|1|W3C Logging Service **|Keep Not Installed. If installed: (Manual)
walletservice|1|WalletService|
wmsvc|1|Web Management Service **|
tokenbroker|1|Web Account Manager|
webclient|1|WebClient|
wfdsconsvc|4|Wi-Fi Direct Services Connection Manager Service|Connect to wireless display.
audiosrv|1|Windows Audio|Do not disable.
audioendpointbuilder|1|Windows Audio Endpoint Builder|Do not disable.
sdrsvc|1|Windows Backup|Do not disable.
wbiosrvc|1|Windows Biometric Service|
frameserver|4|Windows Camera Frame Server|Could be needed to capture webcam frames.
wcncsvc|1|Windows Connect Now - Config Registrar|
wcmsvc|3|Windows Connection Manager|
sense|1|Windows Defender Advanced Threat Protection Service|
wdnissvc|1|Windows Defender Antivirus Network Inspection Service|Cannot be disabled via services.msc
windefend|1|Windows Defender Antivirus Service|Cannot be disabled via services.msc
mpssvc|1|Windows Defender Firewall|Cannot be disabled via services.msc
securityhealthservice|1|Windows Defender Security Center Service|Cannot be disabled via services.msc
wephostsvc|1|Windows Encryption Provider Host Service|
wersvc|1|Windows Error Reporting Service|
wecsvc|1|Windows Event Collector|
eventlog|1|Windows Event Log|
fontcache|1|Windows Font Cache Service|
stisvc|1|Windows Image Acquisition (WIA)|
wisvc|4|Windows Insider Service|Disable if not beta testing new versions of Windows via the Insider program.
msiserver|1|Windows Installer|Cannot be disabled via services.msc
licensemanager|1|Windows License Manager Service|Windows Store.
winmgmt|1|Windows Management Instrumentation|
wmpnetworksvc|2|Windows Media Player Network Sharing Service **|
icssvc|4|Windows Mobile Hotspot Service|Mobile Network Connection (3G, 4G, LTE, Etc). Disable on devices without those options
trustedinstaller|1|Windows Modules Installer|
spectrum|1|Windows Perception Service|New Virtual Reality Service
fontcache3.0.0.0|1|Windows Presentation Foundation Font Cache 3.0.0.0 **|
was|3|Windows Process Activation Service **|
wpnservice|1|Windows Push Notifications System Service|
wpnuserservice_?????|1|Windows Push Notifications User Service_?????|
pushtoinstall|1|Windows PushToInstall Service|New.
winrm|4|Windows Remote Management (WS-Management)|
wsearch|3|Windows Search|
w32time|1|Windows Time|
wuauserv|1|Windows Update|Do not disable.
waasmedicsvc|1|Windows Update Medic Service|New.
winhttpautoproxysvc|1|WinHTTP Web Proxy Auto-Discovery Service|
dot3svc|1|Wired AutoConfig|
wlansvc|2|WLAN AutoConfig|Automatic with Wireless Card Installed.
wmiapsrv|1|WMI Performance Adapter|
workfolderssvc|3|Work Folders **|
lanmanworkstation|1|Workstation|
w3svc|3|World Wide Web Publishing Service **|If installed: (Automatic, Running). Do not install.
wwansvc|4|WWAN AutoConfig|Mobile Network Connection (3G, 4G, LTE, Etc). Disable on devices without those options
xboxgipsvc|1|Xbox Accessory Management Service|
xbgm|1|Xbox Game Monitoring|Cannot be disabled via services.msc, but will not trigger start if XBOX Live Authentication is unavailable
xblauthmanager|4|Xbox Live Auth Manager|Leave in Manual if using any XBOX console PC to Console functions.
xblgamesave|4|Xbox Live Game Save|Leave in Manual if using any XBOX console PC to Console functions.
xboxnetapisvc|4|Xbox Live Networking Service|Leave in Manual if using any XBOX console PC to Console functions.";

        // 禁用即破坏系统的关键服务（Win11 社区清单）
        // 判定优先级最高，且必须与 DisableOk 互斥：同一服务不能同时出现在两张表里
        private static readonly string[] NeverDisable = new string[]
        {
            "brokerinfrastructure", "dnscache", "systemeventsbroker", "staterepository",
            "winhttpautoproxysvc", "graphicsperfsvc", "themes", "textinputmanagementservice",
            "usermanager", "audioendpointbuilder", "dcomlaunch", "rpceptmapper", "rpcss"
        };
        // 安全组件：禁用会降低系统防护，无条件建议保留
        private static readonly string[] Security = new string[]
        {
            "windefend", "wdnissvc", "wdfilter", "wdboot", "sense", "wdnisdrv",
            "securityhealthservice", "wscsvc", "mpssvc", "bfe"
        };
        // Win11 优化清单确认可禁用的服务（注册表名 + 显示名）
        // 注意：本表在逐项 KB 之后判定，只用于 KB 未收录的服务。曾被 KB 覆盖的 73 条
        // 死条目已清理（判定顺序 KB 优先，它们永远不会生效，留着会误导维护者）
        private static readonly string[] DisableOk = new string[]
        {
            "cbdhsvc", "npsmsvc", "gamingservices", "gamingservicesnet", "p9rdr", "p9rdrservice",
            "penservice", "udkusersvc", "webthreatdefusersvc",
            "netbios", "netbt", "net.tcp port sharing service",
            "avctp", "connected devices platform service", "dcsvc",
            "inventory and compatibility appraisal service", "recommended troubleshooting service"
        };
        // 常见启动程序（文件名小写 → 等级|说明）
        private static readonly string[] StartupKb = new string[]
        {
            "wechat.exe|4|微信开机自启，可禁用",
            "weixin.exe|4|微信开机自启，可禁用",
            "qq.exe|4|QQ 开机自启，可禁用",
            "tim.exe|4|TIM 开机自启，可禁用",
            "dingtalk.exe|4|钉钉开机自启，可禁用",
            "feishu.exe|4|飞书开机自启，可禁用",
            "steam.exe|4|Steam 自启，可禁用",
            "epicgameslauncher.exe|4|Epic 自启，可禁用",
            "baidunetdisk.exe|4|百度网盘自启，可禁用",
            "thunder.exe|4|迅雷自启，可禁用",
            "onedrive.exe|3|云同步，按需保留",
            "kugou.exe|4|音乐自启，可禁用",
            "cloudmusic.exe|4|音乐自启，可禁用",
            "clash-verge.exe|3|代理工具，按需保留",
            "360tray.exe|1|安全软件实时防护",
            "360safe.exe|1|安全软件",
            "hipsdaemon.exe|1|火绒防护进程",
            "hipstray.exe|1|火绒托盘",
            "usysdiag.exe|1|火绒安全组件",
            "qqpcrtp.exe|1|管家实时防护",
            "qqpctray.exe|1|管家托盘",
            "msmpeng.exe|1|Windows Defender 核心",
            "securityhealthsystray.exe|1|Windows 安全中心托盘",
            "ctfmon.exe|1|输入法/语言栏核心，禁用会导致输入切换失效"
        };

        private static string Norm(string s)
        {
            if (s == null) return "";
            return s.Trim().ToLowerInvariant();
        }

        // 名字归一：只去掉「_十六进制」实例后缀，规则与 Manager.HexSuffixBase 完全一致。
        // 以前按「首个下划线」截断，会把 my_service 这类正常名字截成 my，与另外两处判定不一致
        private static string BaseKey(string s)
        {
            s = Norm(s);
            string t = Manager.HexSuffixBase(s);
            return t == null ? s : t;
        }

        private static bool In(string[] set, string name, string disp)
        {
            string n = BaseKey(name);
            string d = Norm(disp);
            for (int i = 0; i < set.Length; i++)
                if (set[i] == n || set[i] == d) return true;
            return false;
        }

        // KB 里「模板_?????」形式表示每用户服务模板。查找侧拿到的是实例名（模板_十六进制）
        // 或实例的显示名，带占位符的键永远匹配不上，这里统一去掉占位后缀
        private static string KbKey(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int q = s.IndexOf("_?????", StringComparison.Ordinal);
            if (q > 0) s = s.Substring(0, q);
            return s.Trim().ToLowerInvariant();
        }

        // 去掉名称末尾的 _十六进制 实例后缀；全程序唯一一份包装（AiBatch 也复用它）
        internal static string StripInstanceTail(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string t = Manager.HexSuffixBase(s);
            return t == null ? s : t;
        }

        private static volatile Dictionary<string, Kb> kb;
        private static readonly object kbGate = new object();

        private static void EnsureKb()
        {
            if (kb != null) return;
            lock (kbGate)
            {
                if (kb != null) return;
                // 先建临时字典，全部装完再发布（volatile 写保证半成品不外泄）：
                // 避免其它线程看到半成品
                Dictionary<string, Kb> map = new Dictionary<string, Kb>(StringComparer.OrdinalIgnoreCase);
                string[] lines = ServiceKbData.Split('\n');
                int bad = 0;
                for (int i = 0; i < lines.Length; i++)
                {
                    string[] parts = lines[i].TrimEnd('\r').Split('|');
                    // 字段数不对就跳过；计数让「知识库少了几条」在诊断日志里可见，
                    // 而不是彻底静默（以前只能靠逐行数竖线排查）
                    if (parts.Length != 4) { bad++; continue; }
                    Kb k = new Kb();
                    int.TryParse(parts[1], out k.Rank);
                    k.Disp = parts[2];
                    k.Note = parts[3];
                    string byName = KbKey(parts[0]);
                    if (byName.Length > 0) map[byName] = k;
                    string byDisp = KbKey(parts[2]);
                    if (byDisp.Length > 0 && !map.ContainsKey(byDisp)) map[byDisp] = k;
                }
                kb = map;
                if (bad > 0) DiagLog.Note("服务知识库有 " + bad + " 行字段数不是 4，已跳过");
            }
        }

        // 服务名（含每用户实例后缀）与显示名两条路都查，兼容实例与模板两种写法
        private static bool TryKb(string name, string disp, out Kb k)
        {
            k = null;
            EnsureKb();                     // 不再依赖调用方先初始化，避免新调用点漏掉就 NRE
            Dictionary<string, Kb> map = kb;
            if (map == null) return false;
            string n = Norm(name);
            if (n.Length > 0)
            {
                if (map.TryGetValue(n, out k)) return true;
                string n2 = StripInstanceTail(n);
                if (n2 != n && map.TryGetValue(n2, out k)) return true;
            }
            string d = Norm(disp);
            if (d.Length > 0)
            {
                if (map.TryGetValue(d, out k)) return true;
                string d2 = StripInstanceTail(d);
                if (d2 != d && map.TryGetValue(d2, out k)) return true;
            }
            return false;
        }

        // ---------------- 数字签名校验 ----------------
        // 只读证书 Subject 无法证明文件可信（自签名证书同样能把 Subject 写成 Microsoft），
        // 必须走 WinVerifyTrust 校验「证书链可信 + 文件哈希未被篡改」
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustFileInfo
        {
            public int StructSize;
            public string FilePath;
            public IntPtr FileHandle;
            public IntPtr KnownSubject;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustData
        {
            public int StructSize;
            public IntPtr PolicyCallbackData;
            public IntPtr SipClientData;
            public int UiChoice;
            public int RevocationChecks;
            public int UnionChoice;
            public IntPtr FileInfo;
            public int StateAction;
            public IntPtr StateData;
            public IntPtr UrlReference;
            public int ProvFlags;
            public int UiContext;
            public IntPtr SignatureSettings;
        }

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int WinVerifyTrust(IntPtr hwnd,
            [MarshalAs(UnmanagedType.LPStruct)] Guid actionId, IntPtr data);

        private static readonly Guid VerifyV2 = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        // 0 = 签名可信；其余（含无内嵌签名）一律视为不可信
        private static bool VerifyAuthenticode(string path)
        {
            IntPtr pFile = IntPtr.Zero;
            IntPtr pData = IntPtr.Zero;
            try
            {
                WinTrustFileInfo fi = new WinTrustFileInfo();
                fi.StructSize = Marshal.SizeOf(typeof(WinTrustFileInfo));
                fi.FilePath = path;
                pFile = Marshal.AllocHGlobal(fi.StructSize);
                Marshal.StructureToPtr(fi, pFile, false);

                WinTrustData wd = new WinTrustData();
                wd.StructSize = Marshal.SizeOf(typeof(WinTrustData));
                wd.UiChoice = 2;                 // WTD_UI_NONE
                wd.RevocationChecks = 0;         // WTD_REVOKE_NONE
                wd.UnionChoice = 1;              // WTD_CHOICE_FILE
                wd.FileInfo = pFile;
                // IGNORE 仍会执行完整的链/哈希校验，但不保留验证状态，因此无需再发 CLOSE。
                // 原先把 0 当 VERIFY、再把 1 当 CLOSE，第二次实际又做了一次验签并泄漏状态句柄
                wd.StateAction = 0;              // WTD_STATEACTION_IGNORE
                wd.ProvFlags = 0x00000010 | 0x00001000;   // 不做联网吊销检查、只用缓存 URL
                pData = Marshal.AllocHGlobal(wd.StructSize);
                Marshal.StructureToPtr(wd, pData, false);

                int rc = WinVerifyTrust(IntPtr.Zero, VerifyV2, pData);
                return rc == 0;
            }
            catch (Exception ex) { DiagLog.Swallow("WinVerifyTrust " + path, ex); return false; }
            finally
            {
                // StructureToPtr 为结构里的 string 分配了非托管内存，只 FreeHGlobal 会漏掉这一块，
                // 必须先 DestroyStructure；验签按 exe 逐次调用，漏一次就是几百字节常驻
                if (pFile != IntPtr.Zero)
                {
                    try { Marshal.DestroyStructure(pFile, typeof(WinTrustFileInfo)); }
                    catch { }
                    Marshal.FreeHGlobal(pFile);
                }
                if (pData != IntPtr.Zero)
                {
                    try { Marshal.DestroyStructure(pData, typeof(WinTrustData)); }
                    catch { }
                    Marshal.FreeHGlobal(pData);
                }
            }
        }

        // 签名验证结果缓存。判定的调用链目前是单线程（列表加载有 loading 门闩），
        // 但静态字典一旦被并发读写会直接损坏，加锁的成本可以忽略，防患于未然。
        // 文件可能被原地替换/签名过期，所以缓存 30 分钟后失效。
        private static readonly object signGate = new object();

        private static bool IsMicrosoftSigned(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            lock (signGate)
            {
                SignCacheEntry hit;
                if (signCache.TryGetValue(path, out hit) && hit.Expires > DateTime.UtcNow.Ticks)
                    return hit.Value;
            }
            bool ms = false;
            string real = CtxReg.Fixup(path);
            try { real = Environment.ExpandEnvironmentVariables(real).Trim(); }
            catch { }
            if (CtxReg.IsLocalPath(real) && File.Exists(real))
            {
                try
                {
                    // 链验证与哈希完整性必须保留；VerifyAuthenticode 已禁用联网吊销检查，
                    // 不能再按路径前缀跳过，否则自签名的伪造 Subject 或篡改文件会被当成微软组件。
                    if (VerifyAuthenticode(real))
                    {
                        // X509Certificate 持有证书上下文句柄，必须释放：验签是逐 exe 调用的
                        using (X509Certificate cert = X509Certificate.CreateFromSignedFile(real))
                        {
                            if (cert != null && cert.Subject != null &&
                                cert.Subject.IndexOf("microsoft", StringComparison.OrdinalIgnoreCase) >= 0)
                                ms = true;
                        }
                    }
                }
                catch (Exception ex) { ms = false; DiagLog.Swallow("签名验证 " + real, ex); }
            }
            return CacheSign(path, ms);
        }

        private static bool CacheSign(string path, bool value)
        {
            lock (signGate)
            {
                if (signCache.Count >= SignCacheMax) CtxReg.PurgeExpired(signCache, e => e.Expires);
                signCache[path] = new SignCacheEntry
                {
                    Value = value,
                    Expires = DateTime.UtcNow.AddMinutes(SignCacheTtlMinutes).Ticks
                };
            }
            return value;
        }

        // 任意可信发布者的有效签名：用于区分「正规工具的 per-user 安装」与「来历不明的残留」
        private static bool IsTrustedSigned(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            lock (signGate)
            {
                SignCacheEntry hit;
                if (trustCache.TryGetValue(path, out hit) && hit.Expires > DateTime.UtcNow.Ticks)
                    return hit.Value;
            }
            bool ok = false;
            string real = CtxReg.Fixup(path);
            try { real = Environment.ExpandEnvironmentVariables(real).Trim(); }
            catch { }
            if (CtxReg.IsLocalPath(real) && File.Exists(real))
            {
                try { ok = VerifyAuthenticode(real); }
                catch { ok = false; }
            }
            lock (signGate)
            {
                if (trustCache.Count >= SignCacheMax) CtxReg.PurgeExpired(trustCache, e => e.Expires);
                trustCache[path] = new SignCacheEntry
                {
                    Value = ok,
                    Expires = DateTime.UtcNow.AddMinutes(SignCacheTtlMinutes).Ticks
                };
            }
            return ok;
        }

        // 可执行类扩展名：用于在「无引号 + 路径含空格 + 带参数」的命令行里定位真正的程序段
        private static readonly string[] ExeExts = new string[]
        {
            ".exe", ".com", ".bat", ".cmd", ".scr", ".pif", ".dll", ".sys",
            ".lnk", ".url", ".ps1", ".vbs", ".js", ".cpl", ".msc"
        };

        private static bool EndsWithExe(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < ExeExts.Length; i++)
                if (s.EndsWith(ExeExts[i], StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string ExeFromCommand(string cmd)
        {
            if (cmd == null) return "";
            cmd = cmd.Trim();
            if (cmd.Length == 0) return "";
            // 注册表 Run / 服务 ImagePath 常见 %ProgramFiles%\…、%LOCALAPPDATA%\… 写法。
            // 不展开会被 Classify 判成「网络路径」，内置建议给出完全错误的结论；
            // 提示词侧 Compact 本来就展开，两处必须同一口径
            try { cmd = Environment.ExpandEnvironmentVariables(cmd); }
            catch { }
            if (cmd[0] == '"')
            {
                int e = cmd.IndexOf('"', 1);
                if (e > 1) return CtxReg.Fixup(cmd.Substring(1, e - 1));
                // 只有起始引号、没有结束引号（命令被截断或手写错误）：老实现把整串当路径，
                // 后续存在性检查必然失败并给出「疑似残留启动项」的错误结论。
                // 取引号之后最长的、以可执行扩展名结尾的前缀
                string body = cmd.Substring(1);
                for (int i = body.Length; i > 0; i--)
                {
                    string cand = body.Substring(0, i).TrimEnd();
                    if (EndsWithExe(cand)) return CtxReg.Fixup(cand);
                }
                int sp0 = body.IndexOf(' ');
                return CtxReg.Fixup(sp0 > 0 ? body.Substring(0, sp0) : body);
            }
            // 整串以 .exe 结尾可能是「带空格的路径」，也可能是「程序 + 以 .exe 结尾的参数」。
            // 只有无空格、或整串确实是一个存在的本地文件时才整体返回；否则继续从右往左找程序段
            string wholeExe = null;
            if (EndsWithExe(cmd))
            {
                wholeExe = CtxReg.Fixup(cmd);
                if (cmd.IndexOf(' ') < 0 || (CtxReg.IsLocalPath(wholeExe) && File.Exists(wholeExe))) return wholeExe;
            }
            // 参数个数没有上限，固定扫 12 次会在长命令行下回溯不到程序段
            // （C:\Program Files\App\app.exe a b c … 会被截成 C:\Program）。扫到开头即可，代价可忽略
            int limit = cmd.Length;
            while (limit > 1)
            {
                int sp = cmd.LastIndexOf(' ', limit - 1);
                if (sp <= 0) break;
                string cand = cmd.Substring(0, sp).TrimEnd();
                if (EndsWithExe(cand)) return CtxReg.Fixup(cand);
                limit = sp;
            }
            // 右向左扫描没找到程序段，但整串以 .exe 结尾：这是路径含空格且文件已不存在的
            // 残留启动项（C:\Program Files\Gone\app.exe）。存在性交给上层判断，不能截成 C:\Program
            if (wholeExe != null) return wholeExe;
            int dash = cmd.IndexOf(" -", StringComparison.Ordinal);
            int slash = cmd.IndexOf(" /", StringComparison.Ordinal);
            int cut = dash;
            if (cut < 0 || (slash >= 0 && slash < cut)) cut = slash;
            if (cut > 0) return CtxReg.Fixup(cmd.Substring(0, cut).TrimEnd());
            int first = cmd.IndexOf(' ');
            if (first > 0) return CtxReg.Fixup(cmd.Substring(0, first));
            return CtxReg.Fixup(cmd);
        }

        private static void Apply(IAdviceItem m, int rank, string note)
        {
            m.AdviceRank = rank;
            m.Advice = rank >= 1 && rank <= 5 ? RankNames[rank] : "按需保留";
            m.AdviceNote = note == null ? "" : note;
        }

        internal static void AdviceStartup(List<StartupItem> list)
        {
            foreach (StartupItem it in list)
            {
                IAdviceItem m = it;
                string cmd = it.Command == null ? "" : it.Command.Trim();
                // RunOnce 允许前缀：! = 执行后删除，* = 安全模式下也运行。
                // 不剥掉前缀会把 !C:\...\a.exe 当成「网络路径」，结论完全错误
                if (it.OneShot)
                {
                    int lead = 0;
                    while (lead < cmd.Length && (cmd[lead] == '!' || cmd[lead] == '*')) lead++;
                    if (lead > 0) cmd = cmd.Substring(lead).Trim();
                }
                string exe = ExeFromCommand(cmd);
                if (exe.Length == 0)
                {
                    Apply(m, 5, "启动命令为空，疑似残留启动项");
                    continue;
                }
                // 存在性检查必须先过本地路径这一关：注册表 Run 项完全可能指向
                // \\host\share\x.exe 这类网络路径，File.Exists 在主机不可达时会按 SMB
                // 超时阻塞加载线程（每项可达十几秒），整页列表就挂住了。
                // 映射网络驱动器（Z:\）同样是 SMB，必须由 IsLocalPath 内部的 DriveType 判定排除
                CtxReg.PathKind kind = CtxReg.Classify(exe);
                if (kind == CtxReg.PathKind.Remote)
                {
                    // 启动文件夹里的 .url 快捷方式会解析出 http(s) 地址，同样走这里
                    Apply(m, 3, "目标为网络地址或网络路径，本地无法验证，按需保留");
                    continue;
                }
                if (kind == CtxReg.PathKind.SystemRelative)
                {
                    Apply(m, 3, "系统内置或相对路径，未做文件校验，按需保留");
                    continue;
                }
                if (!File.Exists(exe))
                {
                    Apply(m, 5, "目标文件不存在，疑似残留启动项");
                    continue;
                }
                bool msSigned = IsMicrosoftSigned(exe);
                string low = exe.ToLowerInvariant();
                bool riskyPath = low.Contains("\\temp\\") || low.Contains("\\tmp\\") ||
                                 low.Contains("\\appdata\\");
                string file = "";
                try { file = System.IO.Path.GetFileName(exe).ToLowerInvariant(); } catch { }
                string note;
                int rank;
                // 文件名可以任意伪造：必须先确认签名可信，且排除 Temp/AppData 高风险目录，
                // 再允许用文件名知识库给出「核心/安全组件」这类 rank 1 结论
                if (MatchStartup(file, out rank, out note) && !riskyPath &&
                    (msSigned || IsTrustedSigned(exe))) { Apply(m, rank, note); continue; }
                if (msSigned) { Apply(m, 1, "微软签名组件"); continue; }
                if (low.Contains("\\temp\\") || low.Contains("\\tmp\\")) { Apply(m, 5, "无签名程序位于临时目录，高风险"); continue; }
                if (low.Contains("\\appdata\\"))
                {
                    // %LocalAppData%\Programs、%AppData% 是正规工具默认的 per-user 安装位置，
                    // 只要签名可信就不该按「可疑残留」处理；只有无签名才保留高风险提示
                    // 启动项页只有启用/禁用两种操作，没有「手动」：给 rank 2 会指向一个
                    // 界面上不存在的动作，改为「按需保留」
                    if (IsTrustedSigned(exe)) Apply(m, 3, "已签名程序，位于用户目录，按需保留");
                    else Apply(m, 5, "无签名程序位于 AppData，请谨慎确认来源");
                    continue;
                }
                Apply(m, 3, "第三方自启，按需保留");
            }
        }

        // StartupKb 预拆分缓存：首次使用时把每行 Split 一次，避免每个启动项都重复切分
        private static volatile string[][] startupKbRows;
        private static readonly object startupKbGate = new object();

        private static string[][] StartupKbRows()
        {
            string[][] rows = startupKbRows;
            if (rows != null) return rows;
            lock (startupKbGate)
            {
                if (startupKbRows == null)
                {
                    string[][] built = new string[StartupKb.Length][];
                    for (int i = 0; i < StartupKb.Length; i++) built[i] = StartupKb[i].Split('|');
                    startupKbRows = built;   // 整体发布，参照 EnsureKb 的范式
                }
                return startupKbRows;
            }
        }

        private static bool MatchStartup(string file, out int rank, out string note)
        {
            rank = 0;
            note = "";
            if (file.Length == 0) return false;
            string[][] rows = StartupKbRows();
            for (int i = 0; i < rows.Length; i++)
            {
                string[] parts = rows[i];
                if (parts[0] == file)
                {
                    int.TryParse(parts[1], out rank);
                    note = parts[2];
                    return true;
                }
            }
            return false;
        }

        internal static void AdviceServices(List<ServiceItem> list)
        {
            EnsureKb();
            foreach (ServiceItem it in list)
            {
                IAdviceItem m = it;
                if (In(NeverDisable, it.ServiceName, it.Name)) { Apply(m, 1, "关键服务，禁用会破坏系统"); continue; }
                if (In(Security, it.ServiceName, it.Name)) { Apply(m, 1, "安全组件，建议保持启用"); continue; }
                // 判定顺序：关键服务 → 安全组件 → 逐项 KB → 批量清单 → 签名 → 兜底。
                // KB 是按服务逐条整理的（带原因说明），比批量清单可靠，所以放在 DisableOk 之前：
                // 两张表对同一服务给出相反结论时以 KB 为准，避免把 Print Spooler、Netlogon
                // 这类 KB 明确「建议保留」的服务显示成「可禁用」
                Kb k;
                if (TryKb(it.ServiceName, it.Name, out k))
                {
                    string note = k.Disp;
                    if (k.Note.Length > 0) note = note + "：" + k.Note;
                    Apply(m, k.Rank, note);
                    continue;
                }
                if (In(DisableOk, it.ServiceName, it.Name)) { Apply(m, 4, "Win11 优化清单：可禁用（相关功能被依赖时请保留）"); continue; }
                // PathName 形如「"C:\...\svchost.exe" -k netsvcs」：必须先取出可执行文件再验签，
                // 直接拿整串去查签名会恒抛异常，导致所有服务都判成「第三方」
                string rawPath = it.Path == null ? "" : it.Path;
                if (rawPath == "—" || rawPath == "-") rawPath = "";   // 列表占位符不是真实路径
                string exe = ExeFromCommand(rawPath);
                // 网络路径的服务程序不做 File.Exists / 验签：SMB 超时会阻塞加载线程（同启动项页）
                CtxReg.PathKind kind = exe.Length == 0 ? CtxReg.PathKind.SystemRelative : CtxReg.Classify(exe);
                bool exeLocal = kind == CtxReg.PathKind.Local;
                if (exeLocal && File.Exists(exe) && IsMicrosoftSigned(exe)) { Apply(m, 1, "微软签名服务"); continue; }
                if (exeLocal) Apply(m, 3, "第三方服务，确认用途后再禁用");
                else if (kind == CtxReg.PathKind.Remote) Apply(m, 3, "服务程序位于网络路径，未能验证签名，确认用途后再禁用");
                else Apply(m, 3, "系统内置驱动或路径为空，未能验证签名，确认用途后再禁用");
            }
        }

        internal static void AdviceTasks(List<TaskItem> list)
        {
            foreach (TaskItem it in list)
            {
                IAdviceItem m = it;
                string tp = Norm(it.TaskPath);
                // Edge 更新任务先判：\MicrosoftEdgeUpdate\ 同样以 \microsoft 开头，
                // 放在后面会被「Windows 自带任务」抢走，可禁用的结论就丢了
                if (tp.Contains("microsoftedgeupdate") || Norm(it.Name).Contains("microsoftedgeupdate"))
                {
                    Apply(m, 4, "Edge 更新任务，可禁用");
                    continue;
                }
                // 只认 \Microsoft 与 \Microsoft\…：\MicrosoftEdgeUpdate、\MicrosoftOffice 这类
                // 一级目录名以 Microsoft 开头的第三方任务不该算「Windows 自带」
                if (tp == "\\microsoft" || tp.StartsWith("\\microsoft\\", StringComparison.Ordinal))
                {
                    Apply(m, 1, "Windows 自带任务");
                    continue;
                }
                string exe = ExeFromCommand(it.Action);
                // 同启动项/服务页：网络路径不做存在性检查与验签，避免 SMB 超时挂住加载线程
                if (exe.Length > 0 && CtxReg.Classify(exe) == CtxReg.PathKind.Local &&
                    File.Exists(exe) && IsMicrosoftSigned(exe))
                {
                    Apply(m, 1, "微软签名程序");
                    continue;
                }
                Apply(m, 3, "第三方任务，确认用途后再禁用");
            }
        }

        // 状态胶囊颜色随等级：绿/蓝/灰/橙/红（须在 UI 线程调用）
        internal static void Paint(IAdviceItem it)
        {
            switch (it.AdviceRank)
            {
                case 1: it.AdviceBrush = Palette.Green; it.AdviceBgBrush = Palette.GreenBg; break;
                case 2: it.AdviceBrush = Palette.Blue; it.AdviceBgBrush = Palette.BlueBg; break;
                case 4: it.AdviceBrush = Palette.Orange; it.AdviceBgBrush = Palette.OrangeBg; break;
                case 5: it.AdviceBrush = Palette.Red; it.AdviceBgBrush = Palette.RedBg; break;
                default: it.AdviceBrush = Palette.Gray; it.AdviceBgBrush = Palette.GrayBg; break;
            }
        }
    }

    // ------------------------------------------------------------------ Paths
    // 可选诊断日志：默认关闭、零 IO。设置环境变量 PCTOOLS_LOG=1 后，
    // 关键路径上原本被静默吞掉的异常会追加到 %TEMP%\PCTools_debug.log，
    // 用于排查「列表莫名少了几项」这类不弹窗、不进 warn 的静默失败
    internal static class DiagLog
    {
        private static readonly bool enabled = string.Equals(
            Environment.GetEnvironmentVariable("PCTOOLS_LOG"), "1", StringComparison.OrdinalIgnoreCase);
        private static readonly object gate = new object();
        private static string path;
        private static int writes;

        internal static void Swallow(string where, Exception ex)
        {
            if (!enabled || ex == null) return;
            Append(where + " : " + ex.GetType().Name + " - " + ex.Message);
        }

        // 非异常类诊断信息（如 sqlite 返回码），同样只在 PCTOOLS_LOG=1 时落盘
        internal static void Note(string text)
        {
            if (!enabled || string.IsNullOrEmpty(text)) return;
            Append(text);
        }

        private static void Append(string text)
        {
            try
            {
                lock (gate)
                {
                    if (path == null)
                        path = Path.Combine(Path.GetTempPath(), "PCTools_debug.log");
                    // 长期开启 PCTOOLS_LOG 时日志会无限增长：每 64 次检查一次，
                    // 超过 1MB 就整体换新（诊断日志没有保留历史的价值）
                    if ((++writes & 0x3F) == 0)
                    {
                        try
                        {
                            if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024)
                                File.Delete(path);
                        }
                        catch { }
                    }
                    File.AppendAllText(path,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + Environment.NewLine,
                        Encoding.UTF8);
                }
            }
            catch { }
        }
    }

    // 程序目录与数据文件位置。所有设置（AI 配置 + 主题）都在 PCToolsAI.sqlite。
    // 优先沿用 exe 同目录（保持便携语义与既有用户的数据位置）；目录不可写时
    // （典型是装进 Program Files）回退到 %LOCALAPPDATA%\PCTools，避免配置静默失效
    internal static class AppPaths
    {
        private static readonly string exeDir = ComputeExeDir();
        private static string dataDir = ComputeDataDir();

        private static string ComputeExeDir()
        {
            string b = AppDomain.CurrentDomain.BaseDirectory;
            if (string.IsNullOrEmpty(b))
            {
                try { b = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); }
                catch { }
            }
            if (string.IsNullOrEmpty(b)) return ".";
            b = b.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            // BaseDirectory 为 "C:\" 时 TrimEnd 会得到 "C:"，Path.Combine("C:", "x")
            // 会退化成「当前目录所在盘」的相对路径，必须把分隔符补回来
            if (b.Length == 2 && b[1] == ':') b += "\\";
            return b.Length == 0 ? "\\" : b;
        }

        private static string LocalDataDir()
        {
            try
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCTools");
            }
            catch { return exeDir; }
        }

        private static bool CanWrite(string dir)
        {
            try
            {
                string probe = Path.Combine(dir, ".pctools_write_probe");
                using (FileStream fs = new FileStream(probe, FileMode.Create, FileAccess.Write,
                                                      FileShare.None, 1, FileOptions.DeleteOnClose))
                { }
                return true;
            }
            catch { return false; }
        }

        private static string ComputeDataDir()
        {
            // 1) exe 目录已有数据库：可写就沿用（便携语义）。
            //    目录后来被改成只读（便携版移进 Program Files、目录 ACL 收紧）时必须回退：
            //    sqlite 以读写方式打开只读文件一定失败，整个配置层会不可用且无法自愈
            string exeDb = Path.Combine(exeDir, "PCToolsAI.sqlite");
            if (File.Exists(exeDb))
            {
                if (CanWrite(exeDir)) { Writability = true; return exeDir; }
                string alt = LocalDataDir();
                if (!string.Equals(alt, exeDir, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        Directory.CreateDirectory(alt);
                        if (CanWrite(alt))
                        {
                            string target = Path.Combine(alt, "PCToolsAI.sqlite");
                            if (!File.Exists(target))
                                File.Copy(exeDb, target, false);
                            // File.Copy 会保留只读属性：源库只读时副本同样打不开，必须清掉
                            try { File.SetAttributes(target, FileAttributes.Normal); }
                            catch { }
                            Writability = true;
                            return alt;
                        }
                    }
                    catch { }
                }
                Writability = false;
                return exeDir;      // 连复制都失败：保持原状，界面按不可写提示
            }
            // 2) 之前因不可写而落到 LocalAppData：沿用，避免移动 exe 后配置分裂
            string local = LocalDataDir();
            if (!string.Equals(local, exeDir, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(Path.Combine(local, "PCToolsAI.sqlite")))
            {
                Writability = CanWrite(local);
                return local;
            }
            // 3) 新装：能写就放 exe 同目录（保持便携），否则回落
            if (CanWrite(exeDir)) { Writability = true; return exeDir; }
            try
            {
                Directory.CreateDirectory(local);
                Writability = CanWrite(local);
                return local;
            }
            catch { Writability = false; return exeDir; }
        }

        // 数据目录在本次启动时是否可写。false 时界面要明确提示：否则用户只会看到
        // 「保存失败」或主题设置悄悄不生效
        internal static bool Writability;

        // exe 所在目录（图标等随包资源仍写这里）
        internal static string ExeDir { get { return exeDir; } }

        internal static string Dir { get { return dataDir; } }

#if DIAG
        // 自检模式下的数据库重定向目标（见 AppDb.UseTempForTest）
        internal static string DbPathOverride;
        internal static string DbPath { get { return DbPathOverride ?? Path.Combine(Dir, "PCToolsAI.sqlite"); } }
#else
        internal static string DbPath { get { return Path.Combine(Dir, "PCToolsAI.sqlite"); } }
#endif
    }

    // ------------------------------------------------------------------ SQLite
    // 直接用系统自带的 winsqlite3.dll（Win10 1607+ / Win11 内置），无需随包附带原生库
    internal static class Sqlite
    {
        private const string Dll = "winsqlite3.dll";
        private const int OpenReadWrite = 2;
        private const int OpenCreate = 4;
        private const int Ok = 0;
        private const int Row = 100;
        private const int Done = 101;

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_close(IntPtr db);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_close_v2(IntPtr db);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_exec(IntPtr db, byte[] sql, IntPtr callback, IntPtr arg, out IntPtr errmsg);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void sqlite3_free(IntPtr p);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int nBytes, out IntPtr stmt, IntPtr tail);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_step(IntPtr stmt);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sqlite3_column_text(IntPtr stmt, int index);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_column_bytes(IntPtr stmt, int index);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_column_count(IntPtr stmt);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sqlite3_errmsg(IntPtr db);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_finalize(IntPtr stmt);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_busy_timeout(IntPtr db, int ms);

        private static byte[] Z(string text)
        {
            byte[] b = Encoding.UTF8.GetBytes(text);
            byte[] z = new byte[b.Length + 1];
            Array.Copy(b, z, b.Length);
            return z;
        }

        internal static IntPtr Open(string path, out string error)
        {
            return Open(path, out error, 3000);
        }

        // busyMs：等待其它连接释放锁的上限。后台批量写库用 3000ms 正常排队；
        // UI 线程上的读用短超时（500ms），宁可报一次读失败也不让界面卡三秒
        internal static IntPtr Open(string path, out string error, int busyMs)
        {
            error = null;
            IntPtr db = IntPtr.Zero;
            try
            {
                int rc = sqlite3_open_v2(Z(path), out db, OpenReadWrite | OpenCreate, IntPtr.Zero);
                if (rc != Ok)
                {
                    if (db != IntPtr.Zero) sqlite3_close(db);
                    error = "sqlite3_open_v2 返回 " + rc;
                    return IntPtr.Zero;
                }
                // 非模态后界面线程（主题/配置）与后台线程（分析/统计）会同时开库，
                // 默认 0ms 会直接返回 SQLITE_BUSY；给一段排队时间，用户完全无感
                try { if (busyMs > 0) sqlite3_busy_timeout(db, busyMs); }
                catch { }
                return db;
            }
            catch (Exception ex)
            {
                if (db != IntPtr.Zero) { try { sqlite3_close(db); } catch { } }
                error = ex.Message;
                DiagLog.Swallow("sqlite3_open " + Path.GetFileName(path), ex);
                return IntPtr.Zero;
            }
        }

        internal static void Close(IntPtr db)
        {
            if (db == IntPtr.Zero) return;
            try
            {
                int rc = sqlite3_close(db);
                // SQLITE_BUSY(5) 表示还有未 finalize 的语句。close_v2 会把连接标记为
                // 「僵尸」：最后一条语句 finalize 后自动释放，避免整块连接内存永久泄漏
                if (rc != Ok)
                {
                    try { sqlite3_close_v2(db); }
                    catch (Exception ex) { DiagLog.Swallow("sqlite3_close_v2", ex); }
                    DiagLog.Note("sqlite3_close 返回 " + rc + "（已改为延迟关闭）");
                }
            }
            catch (Exception ex) { DiagLog.Swallow("sqlite3_close", ex); }
        }

        internal static string Exec(IntPtr db, string sql)
        {
            try
            {
                IntPtr err;
                int rc = sqlite3_exec(db, Z(sql), IntPtr.Zero, IntPtr.Zero, out err);
                if (rc == Ok) return null;
                string msg = "";
                if (err != IntPtr.Zero)
                {
                    msg = Utf8(err);
                    sqlite3_free(err);
                }
                return string.IsNullOrEmpty(msg) ? "sqlite3_exec 返回 " + rc : msg;
            }
            catch (Exception ex) { return ex.Message; }
        }

        // 读取结果集第一列（文本），每行一个字符串
        internal static List<string> Column(IntPtr db, string sql, out string error)
        {
            error = null;
            List<string> rows = new List<string>();
            IntPtr stmt = IntPtr.Zero;
            try
            {
                if (sqlite3_prepare_v2(db, Z(sql), -1, out stmt, IntPtr.Zero) != Ok)
                {
                    // 带上 sqlite 自己的错误信息，否则线上只能看到一句无信息量的失败
                    error = "sqlite3_prepare_v2 失败：" + ErrMsg(db);
                    DiagLog.Note(error + " SQL=" + (sql.Length > 200 ? sql.Substring(0, 200) + "…" : sql));
                    return rows;
                }
                int rc;
                while ((rc = sqlite3_step(stmt)) == Row)
                {
                    IntPtr ptr = sqlite3_column_text(stmt, 0);
                    if (ptr == IntPtr.Zero) { rows.Add(""); continue; }
                    // 用 sqlite 自己记录的字节数，避免按 NUL 逐字节扫描
                    int n = sqlite3_column_bytes(stmt, 0);
                    rows.Add(n <= 0 ? "" : Utf8(ptr, n));
                }
                if (rc != Done && rc != Ok)
                {
                    error = "sqlite3_step 返回 " + rc + "：" + ErrMsg(db);
                    DiagLog.Note(error);
                }
                return rows;
            }
            catch (Exception ex) { error = ex.Message; return rows; }
            finally
            {
                if (stmt != IntPtr.Zero) { try { sqlite3_finalize(stmt); } catch { } }
            }
        }

        // 读取完整结果集：每行按列拆开。以前多字段查询靠 "a || char(1) || b" 拼成一列再
        // 用分隔符拆回去，字段里一旦出现该控制字符就会整体错位，且无法表达 NULL 与空串的区别
        internal static List<string[]> Rows(IntPtr db, string sql, out string error)
        {
            error = null;
            List<string[]> rows = new List<string[]>();
            IntPtr stmt = IntPtr.Zero;
            try
            {
                if (sqlite3_prepare_v2(db, Z(sql), -1, out stmt, IntPtr.Zero) != Ok)
                {
                    error = "sqlite3_prepare_v2 失败：" + ErrMsg(db);
                    DiagLog.Note(error + " SQL=" + (sql.Length > 200 ? sql.Substring(0, 200) + "…" : sql));
                    return rows;
                }
                int cols = sqlite3_column_count(stmt);
                if (cols <= 0) cols = 1;
                int rc;
                while ((rc = sqlite3_step(stmt)) == Row)
                {
                    string[] vals = new string[cols];
                    for (int c = 0; c < cols; c++)
                    {
                        IntPtr ptr = sqlite3_column_text(stmt, c);
                        if (ptr == IntPtr.Zero) { vals[c] = ""; continue; }
                        int n = sqlite3_column_bytes(stmt, c);
                        vals[c] = n <= 0 ? "" : Utf8(ptr, n);
                    }
                    rows.Add(vals);
                }
                if (rc != Done && rc != Ok)
                {
                    error = "sqlite3_step 返回 " + rc + "：" + ErrMsg(db);
                    DiagLog.Note(error);
                }
                return rows;
            }
            catch (Exception ex) { error = ex.Message; return rows; }
            finally
            {
                if (stmt != IntPtr.Zero) { try { sqlite3_finalize(stmt); } catch { } }
            }
        }

        private static string ErrMsg(IntPtr db)
        {
            try
            {
                IntPtr p = sqlite3_errmsg(db);
                return p == IntPtr.Zero ? "" : Utf8(p);
            }
            catch { return ""; }
        }

        // NUL 结尾字符串（errmsg 等），加长度上限防止异常指针导致的越界长扫
        private static string Utf8(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) return "";
            int len = 0;
            const int Max = 64 * 1024;
            while (len < Max && Marshal.ReadByte(ptr, len) != 0) len++;
            return len == 0 ? "" : Utf8(ptr, len);
        }

        private static string Utf8(IntPtr ptr, int len)
        {
            if (ptr == IntPtr.Zero || len <= 0) return "";
            byte[] b = new byte[len];
            Marshal.Copy(ptr, b, 0, len);
            return Encoding.UTF8.GetString(b);
        }

        // SQL 字面量转义（单引号翻倍）
        internal static string Q(string text)
        {
            if (text == null) return "''";
            // sqlite3_exec 以 NUL 结尾解析整条 SQL：值里混入 \0 会把后半条语句（可能含
            // COMMIT 或后续 INSERT）一起截掉，留下半截事务。控制字符本身也没有存储价值
            if (text.IndexOf('\0') >= 0) text = text.Replace("\0", "");
            return "'" + text.Replace("'", "''") + "'";
        }
    }


    // ------------------------------------------------------------------ AppDb
    // 数据库统一入口：一次建齐所有表、记录失败原因。
    // 任何入口（含 --selftest 这类不走主窗口的自检）打开数据库前都先 Ensure()，
    // 所以「删掉 db 文件后重开」会自动重建一个只有表结构的空库。
    internal static class AppDb
    {
        internal static string FilePath { get { return AppPaths.DbPath; } }

        // 建库 / 写库失败原因
        internal static string LastError = "";

        // 本次进程中建库建表是否成功。false 时界面必须明确提示：列表仍能枚举，
        // 但 AI 建议列全空、主题与配置保存静默失败，用户会误以为「本来就没有数据」
        internal static bool Usable { get { return ready; } }

        // 双检锁的快速路径读：ready 必须是 volatile，否则理论上另一个线程可能
        // 看到 ready=true 却读不到它前面的建表写入（CLR x64 实际不会，但不可移植）
        private static volatile bool ready;
        private static bool aclDone;
        private static DateTime aclRetryAt;          // ACL 收窄失败后的下次重试时间（UTC）
        private static long readyStamp;              // 建表成功时库文件的最后写入时间（UTC ticks）
        private static DateTime stampCheckedAt;      // 下次允许核对文件时间的时间（节流）
        private static readonly object gate = new object();
        // aclDone / aclRetryAt 是一对状态：Ensure 的快速路径可能被多个线程同时进入，
        // 不加锁会并发执行两次 DACL 收窄，甚至互相覆盖失败状态
        private static readonly object aclGate = new object();

#if DIAG
        // 自检专用：把数据库指到临时文件。--smokecache 这类链路自检要把测试行写进
        // analysis/advice 表，直接写真实 PCToolsAI.sqlite 会留下永久残留
        internal static void UseTempForTest(string path)
        {
            lock (gate)
            {
                AppPaths.DbPathOverride = path;
                ready = false;     // 强制对新文件重建表结构
            }
        }

        internal static void RestoreForTest()
        {
            lock (gate)
            {
                AppPaths.DbPathOverride = null;
                ready = false;
            }
        }
#endif

        // 五张表：AI 配置、全局键值、AI 建议、单条分析缓存、每套配置的累计用量
        internal const string Schema =
            "CREATE TABLE IF NOT EXISTS ai_config (" +
            "config_index INTEGER PRIMARY KEY, name TEXT, endpoint TEXT, model TEXT, " +
            "api_key TEXT, updated TEXT);" +
            "CREATE TABLE IF NOT EXISTS app_setting (" +
            "key TEXT PRIMARY KEY, value TEXT);" +
            "CREATE TABLE IF NOT EXISTS advice (" +
            "kind TEXT NOT NULL, item_key TEXT NOT NULL, label TEXT NOT NULL, updated TEXT, " +
            "PRIMARY KEY (kind, item_key));" +
            "CREATE TABLE IF NOT EXISTS analysis (" +
            "kind TEXT NOT NULL, item_key TEXT NOT NULL, seq INTEGER NOT NULL, " +
            "model TEXT, intro TEXT, advice TEXT, updated TEXT, " +
            "in_tokens INTEGER, out_tokens INTEGER, " +
            "PRIMARY KEY (kind, item_key, seq));" +
            "CREATE TABLE IF NOT EXISTS usage (" +
            "config_index INTEGER PRIMARY KEY, model TEXT, in_tokens INTEGER DEFAULT 0, " +
            "out_tokens INTEGER DEFAULT 0, calls INTEGER DEFAULT 0, updated TEXT);";

        internal static bool Ensure()
        {
            // 文件被删掉时 ready 不能当真，否则读全空、写只建被触及的那张表
            if (ready && File.Exists(FilePath) && StampUnchanged())
            {
                // 上次收窄 ACL 失败（瞬时占用、策略锁定）时在这里重试；内部有 60 秒节流，
                // 不会因为每次 Ensure 都跑一遍而变慢
                if (!aclDone) HardenFileAcl();
                return true;
            }
            lock (gate)
            {
                // 另一个线程可能已经建好：这里用精确比较，不再走 30 秒节流
                if (ready && File.Exists(FilePath) && StampMatches()) { if (!aclDone) HardenFileAcl(); return true; }
                // 走到这里说明要做完整建库：文件不存在（首次运行或被删除后重建）时，
                // ACL 标记必须作废——否则新建出来的文件会继承父目录的宽松权限，
                // 而 aclDone 还留着上一次的 true，收窄逻辑永远不会再执行
                if (!File.Exists(FilePath))
                {
                    aclDone = false;
                    aclRetryAt = DateTime.MinValue;
                }
                IntPtr db = Sqlite.Open(FilePath, out LastError);
                if (db == IntPtr.Zero) { ready = false; return false; }
                try
                {
                    string e = Sqlite.Exec(db, Schema);
                    if (e != null) { LastError = e; ready = false; return false; }
                    ready = true;
                    LastError = "";
                    RememberStamp();
                    HardenFileAcl();
                    return true;
                }
                finally { Sqlite.Close(db); }
            }
        }

        // 外部删表/换库后 ready 仍是 true，后续查询会全部失败且直到重启都不自愈。
        // 用库文件的最后写入时间做廉价探活：每 30 秒最多 stat 一次，时间变了就重跑建表
        private static bool StampUnchanged()
        {
            if (DateTime.UtcNow < stampCheckedAt) return true;
            stampCheckedAt = DateTime.UtcNow.AddSeconds(30);
            return StampMatches();
        }

        private static bool StampMatches()
        {
            try { return File.GetLastWriteTimeUtc(FilePath).Ticks == readyStamp; }
            catch { return true; }
        }

        private static void RememberStamp()
        {
            try { readyStamp = File.GetLastWriteTimeUtc(FilePath).Ticks; }
            catch { readyStamp = 0; }
            stampCheckedAt = DateTime.UtcNow.AddSeconds(30);
        }

        // API Key 已由 DPAPI 按用户加密，这里再把文件 DACL 收窄到「当前用户 + SYSTEM + Administrators」，
        // 形成第二道防线（密文 + 不可读）。FAT/exFAT、非属主、策略锁定等场景会失败，
        // 失败会记入 PCTOOLS_LOG 诊断日志，保持原状继续运行
        private static void HardenFileAcl()
        {
            // 状态判定与执行必须在同一把锁内：Ensure 的快速路径可被多线程并发进入
            lock (aclGate)
            {
                if (aclDone) return;
                // 失败后允许重试，但要节流：Ensure 在每次读写前都会调用，
                // 每次都去碰一次 DACL（还可能抛异常）代价过高
                if (DateTime.UtcNow < aclRetryAt) return;
                try
                {
                    string path = FilePath;
                    if (!File.Exists(path)) return;
                    FileSecurity sec = File.GetAccessControl(path);
                    sec.SetAccessRuleProtection(true, false);      // 断开继承，只留显式规则
                    WindowsIdentity id = WindowsIdentity.GetCurrent();
                    if (id != null && id.User != null)
                        sec.AddAccessRule(new FileSystemAccessRule(id.User,
                            FileSystemRights.FullControl, AccessControlType.Allow));
                    sec.AddAccessRule(new FileSystemAccessRule(
                        new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                        FileSystemRights.FullControl, AccessControlType.Allow));
                    sec.AddAccessRule(new FileSystemAccessRule(
                        new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                        FileSystemRights.FullControl, AccessControlType.Allow));
                    File.SetAccessControl(path, sec);
                    // 只有真正写成功才标记完成：以前先置位再执行，首次失败（瞬时占用、
                    // 目录被策略锁定）之后就再也不会重试，DB 重建后也不会重新收窄
                    aclDone = true;
                }
                catch (Exception ex)
                {
                    aclRetryAt = DateTime.UtcNow.AddSeconds(60);
                    DiagLog.Swallow("ACL 收窄 " + Path.GetFileName(FilePath), ex);
                }
            }
        }

        // 全局键值读写（主题、一次性告知标记等）
        internal static string GetSetting(string key)
        {
            try
            {
                if (!Ensure()) return "";
                string err;
                // UI 线程会直接调用（隐私告知标记）：短超时，避免后台写库时界面卡住
                IntPtr db = Sqlite.Open(FilePath, out err, 500);
                if (db == IntPtr.Zero) return "";
                try
                {
                    List<string> rows = Sqlite.Column(db,
                        "SELECT IFNULL(value,'') FROM app_setting WHERE key=" + Sqlite.Q(key) + ";", out err);
                    return rows.Count > 0 ? rows[0] : "";
                }
                finally { Sqlite.Close(db); }
            }
            catch { return ""; }
        }

        internal static void SetSetting(string key, string value)
        {
            try
            {
                if (!Ensure()) return;
                string err;
                IntPtr db = Sqlite.Open(FilePath, out err);
                if (db == IntPtr.Zero) return;
                try
                {
                    string e = Sqlite.Exec(db, "INSERT OR REPLACE INTO app_setting (key,value) VALUES (" +
                        Sqlite.Q(key) + "," + Sqlite.Q(value) + ");");
                    if (e != null) DiagLog.Note("写设置 " + key + " 失败：" + e);
                }
                finally { Sqlite.Close(db); }
            }
            catch (Exception ex) { DiagLog.Swallow("写设置 " + key, ex); }
        }

    }

    // AI 建议的本地存储：与 exe 同目录的 PCToolsAI.sqlite；文件不存在时列表显示空
    internal static class AiAdviceStore
    {
        internal const string KindStartup = "startup";
        internal const string KindService = "service";
        internal const string KindTask = "task";

        internal static string FilePath { get { return AppDb.FilePath; } }

        internal static string KeyStartup(StartupItem it) { return it.Name + "|" + it.Command; }
        internal static string KeyService(ServiceItem it) { return it.ServiceName; }
        internal static string KeyTask(TaskItem it) { return it.FullPath; }

        internal static Dictionary<string, string> Load(string kind)
        {
            bool complete;
            return Load(kind, out complete);
        }

        // complete=false 表示读取失败（打不开库、SQL 出错），不是「库里确实没有」。
        // 调用方只有拿到 complete=true 才可以把界面上的旧建议清空，否则一次瞬时读失败
        // 会把整列 AI 建议擦掉，看起来像「分析结果丢了」
        internal static Dictionary<string, string> Load(string kind, out bool complete)
        {
            Dictionary<string, string> map = new Dictionary<string, string>();
            complete = false;
            try
            {
                if (!File.Exists(FilePath)) { complete = true; return map; }   // 没有数据库 → 整列留空
                if (!AppDb.Ensure()) return map;
                string err;
                IntPtr db = Sqlite.Open(FilePath, out err, 500);
                if (db == IntPtr.Zero) return map;
                try
                {
                    List<string[]> rows = Sqlite.Rows(db,
                        "SELECT item_key, label FROM advice WHERE kind=" + Sqlite.Q(kind) + ";", out err);
                    if (err != null) return map;
                    foreach (string[] row in rows)
                    {
                        string key = row.Length > 0 ? row[0] : "";
                        if (key.Length > 0) map[key] = row.Length > 1 ? row[1] : "";
                    }
                    complete = true;
                }
                finally { Sqlite.Close(db); }
            }
            catch { }
            return map;
        }

        internal static bool Save(string kind, Dictionary<string, string> values, out string error)
        {
            error = null;
            if (values == null || values.Count == 0) return true;
            if (!AppDb.Ensure()) { error = AppDb.LastError; return false; }
            IntPtr db = Sqlite.Open(FilePath, out error);
            if (db == IntPtr.Zero) return false;
            try
            {
                string now = Sqlite.Q(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                string k = Sqlite.Q(kind);
                // 整批一次事务：逐条 Exec 每条都要单独提交，中途失败会留下半截数据，
                // 而调用方只能报「失败」，用户已经付过的 Token 结果就白丢了
                StringBuilder sb = new StringBuilder();
                sb.Append("BEGIN;");
                foreach (KeyValuePair<string, string> kv in values)
                {
                    // 空标签不落库：写进去会让「已分析」判定为真，而列表上什么都看不到
                    if (string.IsNullOrEmpty(kv.Key) || string.IsNullOrEmpty(kv.Value)) continue;
                    sb.Append("INSERT OR REPLACE INTO advice (kind, item_key, label, updated) VALUES (")
                      .Append(k).Append(", ").Append(Sqlite.Q(kv.Key)).Append(", ")
                      .Append(Sqlite.Q(kv.Value)).Append(", ").Append(now).Append(");");
                }
                sb.Append("COMMIT;");
                string e = Sqlite.Exec(db, sb.ToString());
                if (e != null) { Sqlite.Exec(db, "ROLLBACK;"); error = e; return false; }
                return true;
            }
            finally { Sqlite.Close(db); }
        }

        private static int RankOf(string label)
        {
            if (label == "启用") return 1;
            if (label == "手动") return 2;
            if (label == "禁用") return 3;
            return 0;
        }

        private static void Set(IAiAdviceItem it, Dictionary<string, string> map, string key)
        {
            string v;
            if (key != null && map.TryGetValue(key, out v) && !string.IsNullOrEmpty(v))
            {
                it.AiAdvice = v;
                it.AiAdviceRank = RankOf(v);
                return;
            }
            // 库里已经没有这条了（清库 / 删表 / 换库）：必须把内存里的旧结论一并清掉，
            // 否则列表继续显示已经不存在于库中的建议，而「补全分析」又认为它已完成
            it.AiAdvice = "";
            it.AiAdviceRank = 0;
        }

        internal static void FillStartup(List<StartupItem> list)
        {
            if (list == null || list.Count == 0) return;
            bool complete;
            Dictionary<string, string> map = Load(KindStartup, out complete);
            if (!complete) return;                 // 读失败：保留界面现状，不清空
            foreach (StartupItem it in list) Set(it, map, KeyStartup(it));
        }

        internal static void FillServices(List<ServiceItem> list)
        {
            if (list == null || list.Count == 0) return;
            bool complete;
            Dictionary<string, string> map = Load(KindService, out complete);
            if (!complete) return;
            foreach (ServiceItem it in list) Set(it, map, KeyService(it));
        }

        internal static void FillTasks(List<TaskItem> list)
        {
            if (list == null || list.Count == 0) return;
            bool complete;
            Dictionary<string, string> map = Load(KindTask, out complete);
            if (!complete) return;
            foreach (TaskItem it in list) Set(it, map, KeyTask(it));
        }

        // 只覆盖本次有结论的项：补全分析跳过的旧项、以及请求失败但库里仍有旧结论的项，
        // 都保持现状（数据库里也没有被改写），不能在 UI 线程重读库的前提下误清空
        private static void SetKnown(IAiAdviceItem it, Dictionary<string, string> map, string key)
        {
            string v;
            if (key != null && map.TryGetValue(key, out v) && !string.IsNullOrEmpty(v))
            {
                it.AiAdvice = v;
                it.AiAdviceRank = RankOf(v);
            }
        }

        // 批量分析收尾时已经有本次落库的标签，直接用内存结果回填，不再在 UI 线程开库
        internal static void ApplyStartup(List<StartupItem> list, Dictionary<string, string> labels)
        {
            if (list == null || labels == null) return;
            foreach (StartupItem it in list) SetKnown(it, labels, KeyStartup(it));
        }

        internal static void ApplyServices(List<ServiceItem> list, Dictionary<string, string> labels)
        {
            if (list == null || labels == null) return;
            foreach (ServiceItem it in list) SetKnown(it, labels, KeyService(it));
        }

        internal static void ApplyTasks(List<TaskItem> list, Dictionary<string, string> labels)
        {
            if (list == null || labels == null) return;
            foreach (TaskItem it in list) SetKnown(it, labels, KeyTask(it));
        }

        // 建议胶囊配色：启用=绿、手动=蓝、禁用=红（须在 UI 线程调用）
        internal static void Paint(IAiAdviceItem it)
        {
            switch (it.AiAdviceRank)
            {
                case 1: it.AiAdviceBrush = Palette.Green; it.AiAdviceBgBrush = Palette.GreenBg; break;
                case 2: it.AiAdviceBrush = Palette.Blue; it.AiAdviceBgBrush = Palette.BlueBg; break;
                case 3: it.AiAdviceBrush = Palette.Red; it.AiAdviceBgBrush = Palette.RedBg; break;
                default: it.AiAdviceBrush = Palette.Gray; it.AiAdviceBgBrush = Palette.GrayBg; break;
            }
        }
    }

    // 单条 AI 分析的本地缓存：同一个项目保留最近 2 份，供结果窗左右对比
    internal static class AiAnalysisStore
    {
        internal const string KindStartup = "startup";
        internal const string KindService = "service";
        internal const string KindTask = "task";
        internal const string KindCtx = "ctx";

        internal sealed class Entry
        {
            internal string Model = "";
            internal string Intro = "";
            internal string Advice = "";
            internal string Updated = "";
            internal int InTokens;
            internal int OutTokens;
        }

        internal static string FilePath { get { return AiAdviceStore.FilePath; } }

        // 一次查询取回该 kind 下已有记录的 item_key，供「补全分析」判断缺口。
        // error != null 时调用方必须放弃补全：把「读失败」当成「全都缺失」会重复花钱
        internal static HashSet<string> KeySet(string kind, out string error)
        {
            error = null;
            HashSet<string> set = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(kind)) return set;
            try
            {
                if (!AppDb.Ensure()) { error = AppDb.LastError; return set; }
                // 由 UI 线程直接调用（「更新AI分析」前统计缺口）：短超时避免卡住界面
                IntPtr db = Sqlite.Open(FilePath, out error, 500);
                if (db == IntPtr.Zero) return set;
                try
                {
                    // 走主键 (kind,item_key,seq) 的前缀，不需要额外索引。
                    // 只算「确实有建议正文」的行：模型偶尔只回介绍、advice 为空，
                    // 这种行若算已完成，「补全分析」就永远不会再覆盖它
                    List<string> rows = Sqlite.Column(db,
                        "SELECT item_key FROM analysis WHERE kind=" + Sqlite.Q(kind) +
                        " AND IFNULL(advice,'') <> '' GROUP BY item_key;", out error);
                    foreach (string r in rows) set.Add(r);
                }
                finally { Sqlite.Close(db); }
            }
            catch (Exception ex) { error = ex.Message; }
            return set;
        }

        internal static string KindFor(int page)
        {
            if (page == 0) return KindStartup;
            if (page == 1) return KindService;
            if (page == 2) return KindTask;
            return KindCtx;
        }

        // 与 AiAdviceStore 的 item_key 必须逐字一致：同一项目在批量建议与单条分析里指向同一个键。
        // 这里直接复用对方的实现，避免两处各写一份、改一处忘另一处导致键错位
        internal static string KeyStartup(StartupItem it) { return AiAdviceStore.KeyStartup(it); }
        internal static string KeyService(ServiceItem it) { return AiAdviceStore.KeyService(it); }
        internal static string KeyTask(TaskItem it) { return AiAdviceStore.KeyTask(it); }
        internal static string KeyCtx(CtxMenuItem it) { return it.RegPath + "|" + it.Id; }

        internal static string KeyFor(int page, object item)
        {
            if (page == 0) return KeyStartup((StartupItem)item);
            if (page == 1) return KeyService((ServiceItem)item);
            if (page == 2) return KeyTask((TaskItem)item);
            return KeyCtx((CtxMenuItem)item);
        }

        // 返回 0~2 条，[0] 最新、[1] 上一次
        internal static List<Entry> Load(string kind, string key)
        {
            string ignored;
            return Load(kind, key, out ignored);
        }

        // error != null 表示「没读成功」，与「确实没有缓存」是两件事：
        // 调用方据此提示用户，而不是直接发一次新请求（白花 Token）
        internal static List<Entry> Load(string kind, string key, out string error)
        {
            error = null;
            List<Entry> list = new List<Entry>();
            if (string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(key)) return list;
            try
            {
                if (!File.Exists(FilePath)) return list;
                // 表结构由 AppDb.Ensure 统一建：读取路径不再发 DDL，
                // 否则每次打开分析窗都要抢一次写锁，和后台写入互相阻塞
                if (!AppDb.Ensure()) { error = AppDb.LastError; return list; }
                string err;
                // 打开结果窗时会直接调用：短超时，库被占用时尽快给出「读失败」而不是挂住界面
                IntPtr db = Sqlite.Open(FilePath, out err, 500);
                if (db == IntPtr.Zero) { error = err; return list; }
                try
                {
                    string sql = "SELECT IFNULL(model,''), IFNULL(updated,''), IFNULL(in_tokens,0), " +
                                 "IFNULL(out_tokens,0), IFNULL(intro,''), IFNULL(advice,'') " +
                                 "FROM analysis WHERE kind=" + Sqlite.Q(kind) +
                                 " AND item_key=" + Sqlite.Q(key) + " ORDER BY seq ASC;";
                    List<string[]> rows = Sqlite.Rows(db, sql, out err);
                    if (err != null) { error = err; return list; }
                    foreach (string[] p in rows)
                    {
                        if (p.Length < 6) continue;
                        Entry en = new Entry();
                        en.Model = p[0];
                        en.Updated = p[1];
                        int n;
                        if (int.TryParse(p[2], out n)) en.InTokens = n;
                        if (int.TryParse(p[3], out n)) en.OutTokens = n;
                        en.Intro = p[4];
                        en.Advice = p[5];
                        list.Add(en);
                    }
                }
                finally { Sqlite.Close(db); }
            }
            catch (Exception ex) { error = ex.Message; }
            return list;
        }

        // 写入新一份：seq=1 最新、seq=2 上一次，第 3 份起丢弃
        internal static bool Save(string kind, string key, string model, string intro, string advice,
                                  int inTokens, int outTokens, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(key)) return false;
            if (!AppDb.Ensure()) { error = AppDb.LastError; return false; }
            IntPtr db = Sqlite.Open(FilePath, out error);
            if (db == IntPtr.Zero) return false;
            try
            {
                string k = Sqlite.Q(kind);
                string i = Sqlite.Q(key);
                string sql = "BEGIN;" +
                    "DELETE FROM analysis WHERE kind=" + k + " AND item_key=" + i + " AND seq>=2;" +
                    "UPDATE analysis SET seq=2 WHERE kind=" + k + " AND item_key=" + i + " AND seq=1;" +
                    "INSERT OR REPLACE INTO analysis (kind,item_key,seq,model,intro,advice,updated,in_tokens,out_tokens) VALUES (" +
                        k + "," + i + ",1," + Sqlite.Q(model) + "," + Sqlite.Q(intro) + "," + Sqlite.Q(advice) + "," +
                        Sqlite.Q(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")) + "," + inTokens + "," + outTokens + ");" +
                    "COMMIT;";
                string e = Sqlite.Exec(db, sql);
                if (e != null)
                {
                    Sqlite.Exec(db, "ROLLBACK;");
                    error = e;
                    return false;
                }
                return true;
            }
            finally { Sqlite.Close(db); }
        }

        // 批量逐项分析时的落库项
        internal sealed class SaveItem
        {
            internal string Key;
            internal string Intro;
            internal string Advice;
            internal int InTokens;
            internal int OutTokens;
        }

        // 批量写入：逐项开库/提交太浪费，但整批一个事务的风险更高——任一语句失败就
        // ROLLBACK 全部，已经花掉的 Token 换来的结果会一起丢掉。按固定块提交，
        // 块失败再逐项重试，最坏情况只损失真正写不进去的那几条
        private const int SaveBlockSize = 25;

        internal static bool SaveBatch(string kind, List<SaveItem> items, string model, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(kind) || items == null || items.Count == 0) return true;
            if (!AppDb.Ensure()) { error = AppDb.LastError; return false; }
            IntPtr db = Sqlite.Open(FilePath, out error);
            if (db == IntPtr.Zero) return false;
            try
            {
                string k = Sqlite.Q(kind);
                string now = Sqlite.Q(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                string m = Sqlite.Q(model);

                int failed = 0;
                string firstError = null;
                // 逐项重试要有总预算：每项 BEGIN 最长等 3 秒，一块 25 项全失败就是 75 秒，
                // 十几块叠加能把收尾拖到几分钟，而进度窗早已显示「完成」
                DateTime retryUntil = DateTime.UtcNow.AddSeconds(20);
                for (int i = 0; i < items.Count; i += SaveBlockSize)
                {
                    // 预算超时后剩余整块直接计失败并退出外层循环：
                    // 否则之后每一块仍会各等一次 3 秒 busy_timeout，把收尾拖长几分钟
                    if (DateTime.UtcNow > retryUntil)
                    {
                        int rest = items.Count - i;
                        failed += rest;
                        if (firstError == null)
                            firstError = "写入重试超出 20 秒预算，剩余 " + rest + " 项未保存";
                        break;
                    }
                    int n = Math.Min(SaveBlockSize, items.Count - i);
                    List<SaveItem> block = items.GetRange(i, n);
                    string e = ExecSaveBlock(db, block, k, m, now);
                    if (e == null) continue;
                    // 整块失败：逐项重试，尽量保住已经拿到的结果
                    for (int j = 0; j < block.Count; j++)
                    {
                        if (DateTime.UtcNow > retryUntil)
                        {
                            int rest = block.Count - j;
                            failed += rest;
                            if (firstError == null)
                                firstError = "写入重试超出 20 秒预算，剩余 " + rest + " 项未保存";
                            break;
                        }
                        string e2 = ExecSaveBlock(db, new List<SaveItem> { block[j] }, k, m, now);
                        if (e2 == null) continue;
                        failed++;
                        if (firstError == null) firstError = e2;
                    }
                }
                if (failed == 0) return true;
                error = failed + " 项写入失败（其余 " + (items.Count - failed) + " 项已保存）：" + firstError;
                return false;
            }
            finally { Sqlite.Close(db); }
        }

        private static string ExecSaveBlock(IntPtr db, List<SaveItem> items, string k, string m, string now)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("BEGIN;");
            foreach (SaveItem it in items)
            {
                string i = Sqlite.Q(it.Key);
                sb.Append("DELETE FROM analysis WHERE kind=").Append(k)
                  .Append(" AND item_key=").Append(i).Append(" AND seq>=2;")
                  .Append("UPDATE analysis SET seq=2 WHERE kind=").Append(k)
                  .Append(" AND item_key=").Append(i).Append(" AND seq=1;")
                  .Append("INSERT OR REPLACE INTO analysis (kind,item_key,seq,model,intro,advice,updated,in_tokens,out_tokens) VALUES (")
                  .Append(k).Append(',').Append(i).Append(",1,").Append(m).Append(',')
                  .Append(Sqlite.Q(it.Intro)).Append(',').Append(Sqlite.Q(it.Advice)).Append(',')
                  .Append(now).Append(',').Append(it.InTokens).Append(',').Append(it.OutTokens).Append(");");
            }
            sb.Append("COMMIT;");
            string e = Sqlite.Exec(db, sb.ToString());
            if (e != null) Sqlite.Exec(db, "ROLLBACK;");
            return e;
        }
    }

    // 每套 AI 配置的累计用量：key 用 AiConfig.Index；地址/模型/Key 改动并保存时清零
    internal static class AiUsageStore
    {
        internal sealed class Row
        {
            internal string Model = "";
            // 累计量用 long：sqlite 的 INTEGER 本来就是 64 位，int 在长期使用后会溢出成负数
            internal long InTokens;
            internal long OutTokens;
            internal long Calls;
        }

        private static Dictionary<int, Row> _cache;
        private static DateTime _cacheAt;
        // _cache 由 UI 线程（配置窗统计）与后台线程（分析用量写入、失效）同时触碰，
        // 读改写不加锁的话，两秒窗口内可能互相覆盖或读到撕裂的半成品
        private static readonly object cacheGate = new object();

        // 配置被删或被清空时，让 2 秒缓存立刻作废，避免刚清完又读到旧统计
        internal static void Invalidate() { lock (cacheGate) { _cache = null; } }

        // 静默累加：数据库不可写时只是没有统计，绝不影响主流程。
        // calls 供批量分析收尾时一次累加 N 次调用（默认 1 = 单次）
        internal static void Add(int index, string model, int inTokens, int outTokens, int calls)
        {
            if (index <= 0) return;
            if (calls < 1) calls = 1;
            // 双保险：即使某个调用方绕过 ReadUsage 传入负数，也不能把累计用量减负
            if (inTokens < 0) inTokens = 0;
            if (outTokens < 0) outTokens = 0;
            try
            {
                if (!AppDb.Ensure()) return;
                string err;
                IntPtr db = Sqlite.Open(AiAdviceStore.FilePath, out err);
                if (db == IntPtr.Zero) return;
                try
                {
                    string now = Sqlite.Q(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    // 两条语句放进一个事务：避免别的线程读到刚插入的空行，也少一次提交开销
                    string sql = "BEGIN;" +
                                 "INSERT OR IGNORE INTO usage (config_index,in_tokens,out_tokens,calls) VALUES (" +
                                     index + ",0,0,0);" +
                                 "UPDATE usage SET in_tokens=in_tokens+" + inTokens +
                                     ", out_tokens=out_tokens+" + outTokens +
                                     ", calls=calls+" + calls + ", model=" + Sqlite.Q(model) + ", updated=" + now +
                                 " WHERE config_index=" + index + ";" +
                                 "COMMIT;";
                    string e = Sqlite.Exec(db, sql);
                    // 统计失败不影响主流程，但不能完全无痕：否则「用量一直是 0」无从排查
                    if (e != null) DiagLog.Note("usage 累加失败：" + e);
                }
                finally { Sqlite.Close(db); }
                lock (cacheGate) { _cache = null; }
            }
            catch { }
        }

        internal static void Add(int index, string model, int inTokens, int outTokens)
        {
            Add(index, model, inTokens, outTokens, 1);
        }

        internal static Dictionary<int, Row> LoadAll()
        {
            Dictionary<int, Row> cached;
            lock (cacheGate)
            {
                cached = _cache;
                // 返回副本：调用方误改返回值时不会污染全局缓存（行对象只读，浅拷贝足够）
                if (cached != null && (DateTime.Now - _cacheAt).TotalSeconds < 2)
                    return new Dictionary<int, Row>(cached);
            }
            Dictionary<int, Row> map = new Dictionary<int, Row>();
            bool complete = false;
            try
            {
                if (!File.Exists(AiAdviceStore.FilePath)) { lock (cacheGate) { _cache = map; _cacheAt = DateTime.Now; } return map; }
                if (!AppDb.Ensure()) return map;
                string err;
                // 配置窗在 UI 线程调用：短超时，避免统计查询把界面卡住
                IntPtr db = Sqlite.Open(AiAdviceStore.FilePath, out err, 500);
                if (db == IntPtr.Zero) return map;
                try
                {
                    string sql = "SELECT config_index, IFNULL(model,''), IFNULL(in_tokens,0), " +
                                 "IFNULL(out_tokens,0), IFNULL(calls,0) FROM usage;";
                    List<string[]> rows = Sqlite.Rows(db, sql, out err);
                    // 读失败可能是空/半截结果，不能当完整统计缓存两秒
                    if (err != null) return map;
                    foreach (string[] p in rows)
                    {
                        if (p.Length < 5) continue;
                        int idx;
                        if (!int.TryParse(p[0], out idx) || idx <= 0) continue;
                        Row r = new Row();
                        r.Model = p[1];
                        long n;
                        if (long.TryParse(p[2], out n)) r.InTokens = n;
                        if (long.TryParse(p[3], out n)) r.OutTokens = n;
                        if (long.TryParse(p[4], out n)) r.Calls = n;
                        map[idx] = r;
                    }
                    complete = true;
                }
                finally { Sqlite.Close(db); }
            }
            catch { return map; }
            if (complete) lock (cacheGate) { _cache = map; _cacheAt = DateTime.Now; }
            return map;
        }

        internal static void Clear(int index)
        {
            lock (cacheGate) { _cache = null; }
            try
            {
                if (!AppDb.Ensure()) return;
                string err;
                IntPtr db = Sqlite.Open(AiAdviceStore.FilePath, out err);
                if (db == IntPtr.Zero) return;
                try
                {
                    Sqlite.Exec(db, "DELETE FROM usage WHERE config_index=" + index + ";");
                }
                finally { Sqlite.Close(db); }
            }
            catch { }
        }
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

    // 批量 AI 建议的请求计划：序号 → 存储键，每用户服务实例 → 模板键的别名
    internal sealed class BatchPlan
    {
        internal readonly Dictionary<int, string> Keys = new Dictionary<int, string>();
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

    // 批量 AI 建议：把整页项目一次性发给云端模型，只回「序号 标签」，结果落库。
    // 提示词做压缩：路径缩写、去掉命令行参数、合并每用户服务实例，显著降低请求 token。
    internal static class AiBatch
    {
        internal static string System(int page)
        {
            string common = "你是 Windows 系统管理专家。用户会先说明本机系统版本，再给出一份清单，"
                + "每行格式为「序号|名称|标识|补充信息」。补充信息里的路径已做缩写："
                + "sys32=System32、win=Windows、pf=Program Files、pf86=Program Files (x86)、"
                + "pd=ProgramData、user=用户目录、local/roaming=AppData、drv=驱动仓库，"
                + "svchost 表示系统共享服务宿主进程，mw=任务计划里的 Microsoft\\Windows 目录，"
                + "… 表示被省略的中间目录；除必要参数外命令行已省略。"
                + "这些缩写不影响判断，不要因为路径不完整而降低结论的确定性。"
                + "请逐项判断处理方式，严格按「序号 标签」每行一条输出：不要解释、不要空行、不要标题、不要多余文字。";
            if (page == 0)
                return common + "标签只能是 启用 或 禁用（启动项没有“手动”）。"
                    + "系统必需组件、安全软件给启用；可随时手动打开的第三方常驻程序（聊天、网盘、游戏平台等）给禁用。";
            if (page == 1)
                return common + "标签只能是 启用、手动 或 禁用。"
                    + "系统关键组件与安全组件给启用；仅在被其它功能调用时才需要的服务给手动；"
                    + "第三方常驻、遥测、更新器类服务给禁用。";
            return common + "标签只能是 启用 或 禁用（计划任务没有“手动”）。"
                + "Windows 自带维护任务给启用；第三方更新器、遥测、推广任务给禁用。";
        }

#if DIAG
        // 仅供 --promptstat / --aitest 等自检调用，发布版不需要带这个重载
        internal static string Build(int page, List<object> items, BatchPlan plan)
        {
            return Build(page, items, plan, null);
        }
#endif

        // scope 为整页预计算的实例归并结果。传 null 时退回「按当前 items 计算」，
        // 仅自检这类单批调用会这样传；正式批量请求必须传整页 scope
        internal static string Build(int page, List<object> items, BatchPlan plan, ServiceScope scope)
        {
            return Build(page, items, plan, scope, false);
        }

        // quick=true 只用于切批时的长度估算：按后缀字符串或已有 scope 判断实例，
        // 不再逐项读注册表查服务 Type。最终请求仍走完整 Build
        internal static string Build(int page, List<object> items, BatchPlan plan, ServiceScope scope,
                                     bool quick)
        {
            string typeName = page == 0 ? "开机启动项" : (page == 1 ? "Windows 服务" : "Windows 计划任务");
            // 单批路径（自检）：没有整页 scope 时按本批元素摸清模板与代表
            HashSet<string> plain = null;
            Dictionary<string, string> seenRep = null;
            if (page == 1 && scope == null)
            {
                plain = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                seenRep = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (object o in items)
                {
                    ServiceItem si = (ServiceItem)o;
                    bool inst = quick
                        ? Manager.HexSuffixBase(si.ServiceName) != null
                        : Manager.IsPerUserInstance(si.ServiceName);
                    if (!inst) plain.Add(si.ServiceName);
                }
            }
            StringBuilder body = new StringBuilder();
            for (int i = 0; i < items.Count; i++)
            {
                string key;
                string name;
                string id;
                string extra;
                if (page == 0)
                {
                    StartupItem it = (StartupItem)items[i];
                    key = AiAdviceStore.KeyStartup(it);
                    name = ClipName(it.Name, 56);
                    id = string.IsNullOrEmpty(it.Scope) ? it.Location : it.Location + "·" + it.Scope;
                    extra = Compact(it.Command);
                }
                else if (page == 1)
                {
                    ServiceItem it = (ServiceItem)items[i];
                    // 每用户实例（模板名_十六进制）与模板同源：模板在列表里就直接复用它的结论；
                    // 模板缺席时用第一个实例当代表，其余实例指向这一行（见 Parse 的别名回填）
                    name = it.Name;
                    id = it.ServiceName;
                    key = AiAdviceStore.KeyService(it);
                    string tpl;
                    bool inst;
                    if (quick)
                    {
                        // 有整页 scope 时直接复用注册表确证结果：带 hex 后缀但并非实例的
                        // 普通服务不能被折叠，否则长度估算偏短，实际请求可能被截断
                        inst = scope != null ? scope.Instances.Contains(key)
                                             : Manager.HexSuffixBase(it.ServiceName) != null;
                        tpl = Manager.HexSuffixBase(it.ServiceName) ?? it.ServiceName;
                    }
                    else
                    {
                        // 有整页 scope 时复用注册表确证结果（InstTpl 只装确证过的实例），
                        // 不再对每个实例重复开一次 HKLM 查模板
                        if (scope != null)
                        {
                            inst = scope.InstTpl.TryGetValue(key, out tpl);
                            if (!inst) tpl = it.ServiceName;
                        }
                        else
                        {
                            tpl = Manager.TemplateName(it.ServiceName);
                            inst = !string.Equals(tpl, it.ServiceName, StringComparison.Ordinal);
                        }
                    }
                    if (inst)
                    {
                        string target;
                        if (scope != null)
                        {
                            scope.AliasTo.TryGetValue(key, out target);
                        }
                        else
                        {
                            target = null;
                            if (plain.Contains(tpl)) target = tpl;
                            else
                            {
                                string rep;
                                if (seenRep.TryGetValue(tpl, out rep)) target = rep;
                                else seenRep[tpl] = key;
                            }
                        }
                        if (target != null)
                        {
                            plan.Aliases[key] = target;
                            plan.Skipped++;
                            continue;
                        }
                        name = Adviser.StripInstanceTail(it.Name);
                        id = tpl;
                    }
                    name = ClipName(name, 44);
                    if (string.Equals(name, id, StringComparison.OrdinalIgnoreCase)) id = "-";
                    extra = Compact(it.Path);
                }
                else
                {
                    TaskItem it = (TaskItem)items[i];
                    key = AiAdviceStore.KeyTask(it);
                    name = ClipName(DropGuid(it.Name), 56);
                    string folder = TaskFolder(it.TaskPath);
                    id = string.IsNullOrEmpty(folder) ? "-" : Clip(folder, 48);
                    extra = Compact(it.Action);
                }
                plan.Sent++;
                plan.Keys[plan.Sent] = key;
                body.AppendLine(plan.Sent + "|" + name + "|" + id + "|" + extra);
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("本机系统：" + OsInfo.Describe());
            sb.AppendLine("列表类型：" + typeName + "（共 " + plan.Sent + " 项）");
            sb.Append(body);
            return sb.ToString();
        }

        // 补充信息压缩：展开环境变量 → 去引号与命令行参数 → 目录缩写 → 限长
        private static string Compact(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "-";
            string s = raw.Replace("\r", " ").Replace("\n", " ").Trim();
            // RunOnce 允许 ! / * 前缀（执行后删除 / 安全模式也运行）：不剥掉会让模型
            // 把 "!C:\…" 当成一条异常路径，与内置建议（已剥离）的口径也不一致
            while (s.Length > 0 && (s[0] == '!' || s[0] == '*')) s = s.Substring(1).TrimStart();
            if (s.Length == 0 || s == "—") return "-";          // 界面的空值占位符不发出去
            try { s = Environment.ExpandEnvironmentVariables(s); }
            catch { }

            string prog = ProgramOf(s);
            if (prog.Length == 0) return "-";
            prog = prog.Replace('/', '\\');
            // 只有真正位于 Windows 目录下的 svchost.exe 才按共享宿主缩写；
            // 其它位置（AppData、临时目录）的同名文件必须保留完整路径，否则伪装进程会被当成系统组件
            if (IsSystemSvchost(prog)) return "svchost";
            int ds = prog.IndexOf(DriverStorePrefix, StringComparison.OrdinalIgnoreCase);
            if (ds >= 0) return DriverStore(prog, ds);
            prog = Shorten(prog);
            // 解释器类程序只看路径没有信息量，保留一小段参数
            if (IsInterpreter(prog))
            {
                string args = ArgsOf(s);
                if (args.Length > 0) prog += " " + CompactArgs(args, 44);
            }
            return Clip(prog, 72);
        }

        // 共享服务宿主：%windir%\System32\svchost.exe -k 组名 -p → svchost
        // 组名（LocalSystemNetworkRestricted 之类）只是账户与权限细节，提示词本来就不传服务账户，口径保持一致
        private static bool IsSystemSvchost(string prog)
        {
            if (prog.Length == 0) return false;
            if (!prog.EndsWith("\\svchost.exe", StringComparison.OrdinalIgnoreCase)) return false;
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (string.IsNullOrEmpty(win)) return false;
            win = win.TrimEnd('\\') + "\\";
            return prog.StartsWith(win, StringComparison.OrdinalIgnoreCase);
        }

        // 取出可执行文件本身：优先引号内，其次截到 .exe，最后取第一个空格前
        private static string ProgramOf(string s)
        {
            if (s.Length > 0 && s[0] == '"')
            {
                int q = s.IndexOf('"', 1);
                return q > 1 ? s.Substring(1, q - 1).Trim() : s.Trim('"');
            }
            int e = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (e > 0) return s.Substring(0, e + 4);
            if (s.IndexOf('\\') < 0) return s;          // 非路径（如「COM 处理程序」）整体保留
            int sp = s.IndexOf(' ');
            return sp > 0 ? s.Substring(0, sp) : s;
        }

        private static string ArgsOf(string s)
        {
            if (s.Length > 0 && s[0] == '"')
            {
                int q = s.IndexOf('"', 1);
                return q > 0 && q + 1 < s.Length ? s.Substring(q + 1).Trim() : "";
            }
            int e = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            return e > 0 && e + 4 < s.Length ? s.Substring(e + 4).Trim() : "";
        }

        // 驱动仓库路径（System32\DriverStore\FileRepository\xxx.inf_amd64_hash\a.exe）压成 drv\xxx\a.exe
        private const string DriverStorePrefix = "\\DriverStore\\FileRepository\\";

        private static string DriverStore(string prog, int at)
        {
            // 偏移量必须等于搜索串自身的长度：写死 29 会比「\DriverStore\FileRepository\」
            // （28 字符）多跳一位，包名首字符被吞掉
            string rest = prog.Substring(at + DriverStorePrefix.Length);
            int bs = rest.IndexOf('\\');
            string pkg = bs > 0 ? rest.Substring(0, bs) : rest;
            string file = bs > 0 ? rest.Substring(bs + 1) : "";
            int inf = pkg.IndexOf(".inf", StringComparison.OrdinalIgnoreCase);
            if (inf > 0) pkg = pkg.Substring(0, inf);
            return Clip("drv\\" + pkg + (file.Length > 0 ? "\\" + file : ""), 72);
        }

        // 目录缩写表：懒初始化 + volatile 发布。_pfxFrom 是发布门闩，必须最后写：
        // volatile 写有 release 语义，先写 _pfxTo 再写 _pfxFrom 能保证读者看到
        // _pfxFrom != null 时 _pfxTo 也已就绪（两步同写会有读到半对数组的窗口）
        private static volatile string[] _pfxFrom;
        private static volatile string[] _pfxTo;

        private static void EnsurePrefixes()
        {
            if (_pfxFrom != null) return;
            List<string> from = new List<string>();
            List<string> to = new List<string>();
            AddPfx(from, to, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "local\\");
            AddPfx(from, to, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "roaming\\");
            AddPfx(from, to, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "user\\");
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            AddPfx(from, to, Path.Combine(win, "System32"), "sys32\\");
            AddPfx(from, to, Path.Combine(win, "SysWOW64"), "syswow\\");
            AddPfx(from, to, Path.Combine(win, "WinSxS"), "winsxs\\");
            AddPfx(from, to, win, "win\\");
            AddPfx(from, to, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "pf86\\");
            AddPfx(from, to, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "pf\\");
            AddPfx(from, to, Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "pd\\");
            _pfxTo = to.ToArray();
            _pfxFrom = from.ToArray();   // 最后发布：它是 CompactArgs/Shorten 的就绪门闩
        }

        private static void AddPfx(List<string> from, List<string> to, string dir, string tag)
        {
            if (string.IsNullOrEmpty(dir)) return;
            string d = dir.TrimEnd('\\') + "\\";
            if (d.Length < 4) return;
            from.Add(d);
            to.Add(tag);
        }

        // 参数里出现的完整路径同样缩写，把有限的字符预算留给真正有信息量的部分
        private static string CompactArgs(string args, int max)
        {
            string s = args.Replace('/', '\\');
            EnsurePrefixes();
            for (int i = 0; i < _pfxFrom.Length; i++)
            {
                string from = _pfxFrom[i];
                int at = s.IndexOf(from, StringComparison.OrdinalIgnoreCase);
                while (at >= 0)
                {
                    s = s.Substring(0, at) + _pfxTo[i] + s.Substring(at + from.Length);
                    at = s.IndexOf(from, StringComparison.OrdinalIgnoreCase);
                }
            }
            return Clip(s, max);
        }

        private static string Shorten(string path)
        {
            EnsurePrefixes();
            for (int i = 0; i < _pfxFrom.Length; i++)
            {
                string from = _pfxFrom[i];
                if (path.Length > from.Length &&
                    string.Compare(path, 0, from, 0, from.Length, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    string tag = _pfxTo[i];
                    string rest = path.Substring(from.Length);
                    if (tag == "pf\\" || tag == "pf86\\" || tag == "pd\\" ||
                        tag == "local\\" || tag == "roaming\\" || tag == "user\\")
                        rest = Squash(rest);
                    return tag + rest;
                }
            }
            // 装在其它盘符上的 Program Files 同样归一，避免整条长路径白占字符
            string k86 = ":\\Program Files (x86)\\";
            string k64 = ":\\Program Files\\";
            if (path.IndexOf(k86, StringComparison.OrdinalIgnoreCase) == 1)
                return "pf86\\" + Squash(path.Substring(1 + k86.Length));
            if (path.IndexOf(k64, StringComparison.OrdinalIgnoreCase) == 1)
                return "pf\\" + Squash(path.Substring(1 + k64.Length));
            return path;
        }

        // 目录过深时折叠中间层：a\b\c\d\e.exe → a\…\e.exe（保留厂商段与文件名）
        private static string Squash(string rest)
        {
            string[] seg = rest.Split('\\');
            if (seg.Length <= 4) return rest;
            return seg[0] + "\\…\\" + seg[seg.Length - 1];
        }

        private static bool IsInterpreter(string path)
        {
            string f = path;
            int i = f.LastIndexOf('\\');
            if (i >= 0) f = f.Substring(i + 1);
            f = f.ToLowerInvariant();
            return f == "cmd.exe" || f == "powershell.exe" || f == "pwsh.exe"
                || f == "rundll32.exe" || f == "wscript.exe" || f == "cscript.exe"
                || f == "msiexec.exe" || f == "regsvr32.exe";
        }

        // 名称里的长 GUID 换成 {…}，保留前缀语义
        private static string DropGuid(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int i = s.IndexOf('{');
            while (i >= 0)
            {
                int j = s.IndexOf('}', i + 1);
                if (j < 0) break;
                if (j - i > 20) s = s.Substring(0, i) + "{…}" + s.Substring(j + 1);
                i = s.IndexOf('{', i + 1);
            }
            return s;
        }

        // 任务所在目录：\Microsoft\Windows\X → mw\X，去掉长 SID，根目录返回 "-"
        private static string TaskFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || path == "\\") return "";
            string s = DropSid(path).Trim('\\');
            string head = "Microsoft\\Windows";
            // 前缀命中后必须确认下一个字符是分隔符：\Microsoft\WindowsPowerShell、
            // \Microsoft\Windows Defender 这类「一级目录名以 Windows 开头」的真实目录
            // 会被误拆成 mw\PowerShell、mw\ Defender，提示词里的路径就失真了
            if (string.Compare(s, 0, head, 0, head.Length, StringComparison.OrdinalIgnoreCase) == 0 &&
                (s.Length == head.Length || s[head.Length] == '\\'))
            {
                s = s.Substring(head.Length).Trim('\\');
                return s.Length == 0 ? "mw" : "mw\\" + s;
            }
            return s;
        }

        // 任务路径里的长 SID 只留前缀：\SoftLanding\S-1-5-21-…\
        private static string DropSid(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int i = s.IndexOf("S-1-5-21-", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return s;
            int j = s.IndexOf('\\', i);
            return s.Substring(0, i) + "S-1-5-21-…" + (j >= 0 ? s.Substring(j) : "");
        }

        // 名称截断：能整体砍掉后面的括号补充信息时优先砍掉，比硬切更可读
        private static string ClipName(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            if (s.Length <= max) return s;
            int cut = s.IndexOf(" (", StringComparison.Ordinal);
            if (cut < 0) cut = s.IndexOf("（", StringComparison.Ordinal);
            if (cut >= 6 && cut <= max) return s.Substring(0, cut);
            return s.Substring(0, max) + "…";
        }

        private static string Clip(string v, int max)
        {
            if (string.IsNullOrEmpty(v)) return "";
            v = v.Replace("\r", " ").Replace("\n", " ");
            return v.Length > max ? v.Substring(0, max) + "…" : v;
        }

        // 解析「序号 标签」，序号对应 Build 里登记的 key；每用户实例沿用模板结论。
        // page 用于标签合法性校验：系统提示词虽已禁止，但模型偶尔会在启动项/任务页
        // 输出「手动」，这种标签在页面上根本没有对应操作，不能原样入库展示
        internal static Dictionary<string, string> Parse(string reply, BatchPlan plan, int page)
        {
            int discarded;
            int validSent;
            return Parse(reply, plan, page, out discarded, out validSent);
        }

        // discarded：模型确实回了内容，但编号不可用或标签非法被丢弃的数量。它和「根本没返回的行」
        // 是两回事，统计时必须分开，否则非法标签/错位编号都会被误报成「模型未返回」
        internal static Dictionary<string, string> Parse(string reply, BatchPlan plan, int page,
                                                         out int discarded)
        {
            int validSent;
            return Parse(reply, plan, page, out discarded, out validSent);
        }

        // validSent：去掉别名回填之前，模型真正为「已发送项」返回合法结论的数量。
        // 缺口只能按它算：别名 key 不在 plan.Keys 里，计入 part.Count 会让缺口偏小
        internal static Dictionary<string, string> Parse(string reply, BatchPlan plan, int page,
                                                         out int discarded, out int validSent)
        {
            Dictionary<string, string> values = new Dictionary<string, string>();
            discarded = 0;
            validSent = 0;
            if (string.IsNullOrEmpty(reply)) return values;
            bool allowManual = page == 1;
            string[] lines = reply.Replace("\r", "").Split('\n');
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                int p = 0;
                while (p < line.Length && (char.IsDigit(line[p]) || line[p] == ' ')) p++;
                // 模型回了内容但编号不可用（0 基、"第1项"、超范围）属于「返回了但不能对应」，
                // 不能算进「模型未返回」，否则统计虚高、还会诱导用户重跑白花 Token
                if (p == 0) { discarded++; continue; }
                int no;
                if (!int.TryParse(line.Substring(0, p).Trim(), out no)) { discarded++; continue; }
                string label = LabelOfLine(line.Substring(p));
                if (label.Length == 0) { discarded++; continue; }
                if (label == "手动" && !allowManual) { discarded++; continue; }   // 非服务页不认「手动」
                string key;
                if (!plan.Keys.TryGetValue(no, out key)) { discarded++; continue; }
                // 重复序号只能算一次已返回：否则 validSent 大于实际拿到结论的项数，
                // 缺口统计会把真正没返回的项目漏报
                if (!values.ContainsKey(key)) validSent++;
                values[key] = label;
            }
            foreach (KeyValuePair<string, string> kv in plan.Aliases)
            {
                string v;
                if (values.TryGetValue(kv.Value, out v)) values[kv.Key] = v;
            }
            return values;
        }

        // 从「序号」之后的剩余文本里取结论标签：只认开头那个标签（允许少量分隔符），
        // 不在整行里搜关键字——否则「1 启用（不建议禁用）」这类带解释的输出会被后面的「禁用」翻转结论
        private static string LabelOfLine(string rest)
        {
            int i = 0;
            while (i < rest.Length && (rest[i] == ' ' || rest[i] == '\t' || rest[i] == '|' || rest[i] == '｜' ||
                                       rest[i] == '、' || rest[i] == '.' || rest[i] == '：' || rest[i] == ':' ||
                                       rest[i] == '-' || rest[i] == '—' || rest[i] == '）' || rest[i] == ')'))
                i++;
            string head = rest.Substring(i);
            if (head.StartsWith("禁用", StringComparison.Ordinal)) return "禁用";
            if (head.StartsWith("手动", StringComparison.Ordinal)) return "手动";
            if (head.StartsWith("启用", StringComparison.Ordinal)) return "启用";
            // 兜底：模型写成「建议禁用」这类带前缀的形式，只在极短片段里找。
            // 必须取最先出现的标签：固定按 禁用>手动>启用 会把「建议启用（禁用会有风险）」写反
            string probe = head.Length > 10 ? head.Substring(0, 10) : head;
            int best = int.MaxValue;
            string label = "";
            int p1 = probe.IndexOf("禁用", StringComparison.Ordinal);
            int p2 = probe.IndexOf("手动", StringComparison.Ordinal);
            int p3 = probe.IndexOf("启用", StringComparison.Ordinal);
            if (p1 >= 0 && p1 < best) { best = p1; label = "禁用"; }
            if (p2 >= 0 && p2 < best) { best = p2; label = "手动"; }
            if (p3 >= 0 && p3 < best) { best = p3; label = "启用"; }
            return label;
        }
    }

    internal static class DataProvider
    {
        private const string ApproveBase = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

        internal static List<StartupItem> GetStartupItems(List<string> warn)
        {
            List<StartupItem> list = new List<StartupItem>();
            RunSource[] regs = new RunSource[]
            {
                new RunSource(@"Software\Microsoft\Windows\CurrentVersion\Run", ApproveBase + @"\Run", false, "当前用户", "注册表 Run"),
                new RunSource(@"Software\Microsoft\Windows\CurrentVersion\Run", ApproveBase + @"\Run", true, "所有用户", "注册表 Run"),
                new RunSource(@"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", ApproveBase + @"\Run32", true, "所有用户 (32 位)", "注册表 Run"),
                // 32 位程序的 per-user 启动项写在 WOW6432Node 下，以前完全没枚举到
                new RunSource(@"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", ApproveBase + @"\Run32", false, "当前用户 (32 位)", "注册表 Run"),
                // 一次性启动项（跑完自删）：以前只能去注册表里手工找。
                // 它们不受 StartupApproved 控制，界面上只读展示，不提供启停
                new RunSource(@"Software\Microsoft\Windows\CurrentVersion\RunOnce", ApproveBase + @"\RunOnce", false, "当前用户", "注册表 RunOnce"),
                new RunSource(@"Software\Microsoft\Windows\CurrentVersion\RunOnce", ApproveBase + @"\RunOnce", true, "所有用户", "注册表 RunOnce"),
                new RunSource(@"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce", ApproveBase + @"\RunOnce32", true, "所有用户 (32 位)", "注册表 RunOnce"),
                new RunSource(@"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce", ApproveBase + @"\RunOnce32", false, "当前用户 (32 位)", "注册表 RunOnce")
                // 注：Policies\Explorer\Run 由组策略下发，不受 StartupApproved 控制，
                // 列出来只会得到一个「点了禁用却没效果」的按钮，故不纳入
            };
            foreach (RunSource src in regs)
            {
                try
                {
                    RegistryKey hive = src.Machine ? (RegistryKey)Registry.LocalMachine : Registry.CurrentUser;
                    using (RegistryKey k = hive.OpenSubKey(src.Reg))
                    {
                        if (k == null) continue;
                        foreach (string n in k.GetValueNames())
                        {
                            if (string.IsNullOrWhiteSpace(n)) continue;
                            object raw = k.GetValue(n);
                            string cmd = raw == null ? "" : raw.ToString();
                            bool oneShot = src.Location.IndexOf("RunOnce", StringComparison.Ordinal) >= 0;
                            bool off = !oneShot && IsDisabled(src.Approval, src.Machine, n);
                            StartupItem it = new StartupItem();
                            it.Name = n;
                            it.OneShot = oneShot;
                            // RunOnce 的真实状态是「待执行」：Windows 不读 StartupApproved\RunOnce，
                            // 显示「已禁用」而条目照常执行会给出完全错误的结论
                            it.Status = oneShot ? "一次性" : (off ? "已禁用" : "已启用");
                            it.StatusBrush = oneShot ? Palette.Blue : (off ? Palette.Red : Palette.Green);
                            it.StatusBgBrush = oneShot ? Palette.BlueBg : (off ? Palette.RedBg : Palette.GreenBg);
                            it.Location = src.Location;
                            it.Scope = src.Scope;
                            it.Command = cmd;
                            it.ApprovalSubPath = src.Approval;
                            it.ApprovalName = n;
                            it.Machine = src.Machine;
                            // Location 也要进搜索串：否则按「RunOnce」「启动文件夹」这类
                            // 位置关键字搜不到对应条目
                            it.Search = (n + " " + cmd + " " + src.Scope + " " + src.Location).ToLowerInvariant();
                            list.Add(it);
                        }
                    }
                }
                catch (Exception ex) { AddWarn(warn, "启动项 " + src.Reg + "：" + ex.Message); }
            }

            FolderSource[] folders = new FolderSource[]
            {
                new FolderSource(Environment.GetFolderPath(Environment.SpecialFolder.Startup), ApproveBase + @"\StartupFolder", false, "当前用户"),
                new FolderSource(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), ApproveBase + @"\StartupFolder", true, "所有用户")
            };
            foreach (FolderSource f in folders)
            {
                if (string.IsNullOrEmpty(f.Dir) || !Directory.Exists(f.Dir)) continue;
                try
                {
                    foreach (string file in Directory.GetFiles(f.Dir))
                    {
                        string ext = Path.GetExtension(file).ToLowerInvariant();
                        if (ext != ".lnk" && ext != ".url" && ext != ".bat" && ext != ".cmd" && ext != ".exe") continue;
                        string target = ext == ".lnk" ? ResolveShortcut(file)
                                       : (ext == ".url" ? ResolveUrl(file) : file);
                        string fileName = Path.GetFileName(file);
                        bool off = IsDisabled(f.Approval, f.Machine, fileName);
                        StartupItem it = new StartupItem();
                        it.Name = Path.GetFileNameWithoutExtension(file);
                        it.Status = off ? "已禁用" : "已启用";
                        it.StatusBrush = off ? Palette.Red : Palette.Green;
                        it.StatusBgBrush = off ? Palette.RedBg : Palette.GreenBg;
                        it.Location = "启动文件夹";
                        it.Scope = f.Scope;
                        it.Command = target;
                        it.ApprovalSubPath = f.Approval;
                        it.ApprovalName = fileName;
                        it.Machine = f.Machine;
                        it.Search = (fileName + " " + target + " " + f.Scope + " 启动文件夹").ToLowerInvariant();
                        list.Add(it);
                    }
                }
                catch (Exception ex) { AddWarn(warn, "启动文件夹 " + f.Dir + "：" + ex.Message); }
            }
            Adviser.AdviceStartup(list);
            AiAdviceStore.FillStartup(list);
            return list;
        }

        private static bool IsDisabled(string approvalSubPath, bool machine, string valueName)
        {
            try
            {
                RegistryKey hive = machine ? (RegistryKey)Registry.LocalMachine : Registry.CurrentUser;
                using (RegistryKey k = hive.OpenSubKey(approvalSubPath))
                {
                    if (k == null) return false;
                    byte[] b = k.GetValue(valueName) as byte[];
                    if (b == null || b.Length == 0) return false;
                    return (b[0] & 1) == 1;
                }
            }
            catch (Exception ex) { DiagLog.Swallow("StartupApproved 读取 " + valueName, ex); return false; }
        }

        private static string ResolveShortcut(string path)
        {
            object shell = null;
            object lnk = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                if (shell == null) return path;
                lnk = ((dynamic)shell).CreateShortcut(path);
                if (lnk == null) return path;
                string target = ((dynamic)lnk).TargetPath as string;
                string args = ((dynamic)lnk).Arguments as string;
                if (!string.IsNullOrEmpty(target))
                    return string.IsNullOrEmpty(args) ? target : target + " " + args;
            }
            catch (Exception ex) { DiagLog.Swallow("快捷方式解析 " + path, ex); }
            finally
            {
                // 中途抛异常时也要把 RCW 放掉，否则每次刷新都会漏两个
                ReleaseCom(lnk);
                ReleaseCom(shell);
            }
            return path;
        }

        // 启动文件夹里的 .url 文件以前直接把文件路径当命令展示，既看不出真实目标，
        // File.Exists 恰好为真还会让签名启发式去读 .url 文件本身。改为解析出 URL= 行；
        // 解析失败时回退文件路径，保持旧行为
        private static string ResolveUrl(string path)
        {
            try
            {
                // .url 是 INI 格式，系统默认按 ANSI 代码页保存；先按严格 UTF-8 解，
                // 解不开（非 ASCII 且非 UTF-8 编码）再回退 ANSI，避免中文 URL 变乱码
                string text;
                byte[] raw = File.ReadAllBytes(path);
                try { text = new UTF8Encoding(false, true).GetString(raw); }
                catch { text = Encoding.Default.GetString(raw); }
                string[] lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
                foreach (string rawLine in lines)
                {
                    string line = rawLine.Trim();
                    if (line.Length < 5) continue;
                    if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                    {
                        string url = line.Substring(4).Trim();
                        if (url.Length > 0) return url;
                    }
                }
            }
            catch (Exception ex) { DiagLog.Swallow(".url 解析 " + path, ex); }
            // 失败时保持原路径，由上层按普通快捷方式处理
            return path;
        }

        internal static List<ServiceItem> GetServices(List<string> warn)
        {
            List<ServiceItem> list = new List<ServiceItem>();
            try
            {
                // 只取需要的 7 个属性：SELECT * 会让 WMI 把几十个属性全部编码回传，
                // 300 个服务下光序列化就占了大头
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                           "SELECT Name,DisplayName,State,StartMode,ServiceType,StartName,PathName FROM Win32_Service"))
                using (ManagementObjectCollection col = searcher.Get())
                {
                    foreach (ManagementObject mo in col)
                    {
                        using (mo)
                        {
                            try
                            {
                                ServiceItem it = new ServiceItem();
                                it.ServiceName = Str(mo, "Name");
                                it.Name = Str(mo, "DisplayName");
                                string state = Str(mo, "State");
                                it.Status = MapState(state);
                                it.StatusBrush = state == "Running" ? Palette.Green : (state == "Stopped" ? Palette.Gray : Palette.Orange);
                                it.StatusBgBrush = state == "Running" ? Palette.GreenBg : (state == "Stopped" ? Palette.GrayBg : Palette.OrangeBg);
                                string mode = Str(mo, "StartMode");
                                it.StartType = MapMode(mode);
                                it.StartTypeBrush = mode == "Disabled" ? Palette.Red : (mode == "Auto" ? Palette.Blue : Palette.Neutral);
                                it.StartTypeBgBrush = mode == "Disabled" ? Palette.RedBg : (mode == "Auto" ? Palette.BlueBg : Palette.GrayBg);
                                // 每用户服务实例：实例值只在创建时抄自模板、之后不随修改更新，
                                // 改为显示模板服务的有效启动类型，修改后列表才能立刻反映
                                int? tpl = Manager.TemplateStart(it.ServiceName);
                                if (tpl.HasValue)
                                {
                                    string tm = tpl.Value == 2 ? "Auto" : (tpl.Value == 3 ? "Manual" : (tpl.Value == 4 ? "Disabled" : "Auto"));
                                    it.StartType = MapMode(tm);
                                    it.StartTypeBrush = tm == "Disabled" ? Palette.Red : (tm == "Auto" ? Palette.Blue : Palette.Neutral);
                                    it.StartTypeBgBrush = tm == "Disabled" ? Palette.RedBg : (tm == "Auto" ? Palette.BlueBg : Palette.GrayBg);
                                }
                                it.ServiceType = MapServiceType(Str(mo, "ServiceType"));
                                it.Account = Str(mo, "StartName");
                                it.Path = Str(mo, "PathName");
                                if (string.IsNullOrWhiteSpace(it.Path)) it.Path = "—";
                                if (string.IsNullOrWhiteSpace(it.Name)) it.Name = it.ServiceName;
                                it.Search = (it.Name + " " + it.ServiceName + " " + it.Path + " " + it.Account + " " + it.StartType + " " + it.ServiceType).ToLowerInvariant();
                                it.Detail = it.Name + "\n服务名称：" + it.ServiceName + "\n登录身份：" + it.Account + "\n路径：" + it.Path;
                                if (tpl.HasValue)
                                    it.Detail += "\n说明：每用户服务实例，启动类型由模板服务 " +
                                                 Manager.TemplateName(it.ServiceName) + " 控制，修改后重新登录生效。";
                                list.Add(it);
                            }
                            catch (Exception ex) { AddWarn(warn, "服务项：" + ex.Message); }
                        }
                    }
                }
            }
            catch (Exception ex) { AddWarn(warn, "服务读取失败：" + ex.Message); }
            Adviser.AdviceServices(list);
            AiAdviceStore.FillServices(list);
            return list;
        }

        private static string Str(ManagementObject mo, string prop)
        {
            try { object o = mo[prop]; return o == null ? "" : o.ToString(); }
            catch { return ""; }
        }

        private static string MapState(string s)
        {
            switch (s)
            {
                case "Running": return "正在运行";
                case "Stopped": return "已停止";
                case "Paused": return "已暂停";
                case "Start Pending": return "启动中";
                case "Stop Pending": return "停止中";
                default: return string.IsNullOrEmpty(s) ? "未知" : s;
            }
        }

        private static string MapMode(string s)
        {
            switch (s)
            {
                case "Auto": return "自动";
                case "Manual": return "手动";
                case "Disabled": return "已禁用";
                case "Boot": return "引导启动";
                case "System": return "系统启动";
                default: return string.IsNullOrEmpty(s) ? "未知" : s;
            }
        }

        private static string MapServiceType(string s)
        {
            switch (s)
            {
                case "Own Process": return "独立进程";
                case "Share Process": return "共享进程";
                case "Kernel Driver": return "内核驱动";
                case "File System Driver": return "文件系统驱动";
                case "Interactive Process": return "交互进程";
                case "Unknown": return "未知";
                default: return string.IsNullOrEmpty(s) ? "未知" : s;
            }
        }

        internal static List<TaskItem> GetTasks(List<string> warn)
        {
            List<TaskItem> list = new List<TaskItem>();
            object svc = null;
            object root = null;
            try
            {
                svc = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
                dynamic s = svc;
                s.Connect();
                root = s.GetFolder("\\");
                EnumFolder(root, list, warn, 0);
            }
            catch (Exception ex) { AddWarn(warn, "计划任务读取失败：" + ex.Message); }
            finally
            {
                ReleaseCom(root);
                ReleaseCom(svc);
            }
            Adviser.AdviceTasks(list);
            AiAdviceStore.FillTasks(list);
            return list;
        }

        private static string FolderPathOf(dynamic folder)
        {
            try { return Convert.ToString(folder.Path); } catch { return ""; }
        }

        // COM 对象必须显式释放：只靠 GC 回收，反复刷新时 RCW 会越积越多
        private static void ReleaseCom(object o)
        {
            try
            {
                if (o != null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o);
            }
            catch { }
        }

        private static void EnumFolder(dynamic folder, List<TaskItem> list, List<string> warn, int depth)
        {
            // 任务计划是真实树（无环），限深只为防异常数据拖垮枚举；
            // 8 层会静默吞掉深层自定义目录，放宽到 16 层足够覆盖所有真实场景
            if (depth > 16)
            {
                AddWarn(warn, "任务文件夹层级超过 16 层，已跳过：" + Convert.ToString(FolderPathOf(folder)));
                return;
            }

            object tasks = null;
            try
            {
                tasks = folder.GetTasks(1);
                int n = Convert.ToInt32(((dynamic)tasks).Count);
                for (int i = 1; i <= n; i++)
                {
                    object item = null;
                    try
                    {
                        item = ComItem(tasks, i);
                        list.Add(ToTaskItem(item));
                    }
                    catch (Exception ex) { AddWarn(warn, "计划任务项：" + ex.Message); }
                    finally { ReleaseCom(item); }
                }
            }
            catch (Exception ex) { AddWarn(warn, "任务文件夹：" + ex.Message); }
            finally { ReleaseCom(tasks); }

            object subs = null;
            try
            {
                subs = folder.GetFolders(1);
                int n = Convert.ToInt32(((dynamic)subs).Count);
                for (int i = 1; i <= n; i++)
                {
                    object sub = null;
                    try
                    {
                        sub = ComItem(subs, i);
                        EnumFolder(sub, list, warn, depth + 1);
                    }
                    catch (Exception ex) { AddWarn(warn, "任务子文件夹：" + ex.Message); }
                    finally { ReleaseCom(sub); }
                }
            }
            catch (Exception ex) { AddWarn(warn, "任务子文件夹枚举：" + ex.Message); }
            finally { ReleaseCom(subs); }
        }

        private static dynamic ComItem(dynamic collection, int index)
        {
            try { return collection[index]; }
            catch
            {
                return collection.GetType().InvokeMember("Item",
                    BindingFlags.GetProperty | BindingFlags.Instance | BindingFlags.Public,
                    null, (object)collection, new object[] { index });
            }
        }

        private static TaskItem ToTaskItem(dynamic t)
        {
            TaskItem it = new TaskItem();
            it.Name = Convert.ToString(t.Name);
            it.FullPath = Convert.ToString(t.Path);
            int idx = it.FullPath.LastIndexOf('\\');
            it.TaskPath = idx >= 0 ? it.FullPath.Substring(0, idx + 1) : "\\";
            int state = 0;
            try { state = Convert.ToInt32(t.State); } catch { }
            switch (state)
            {
                case 1: it.Status = "已禁用"; it.StatusBrush = Palette.Red; it.StatusBgBrush = Palette.RedBg; break;
                case 2: it.Status = "排队中"; it.StatusBrush = Palette.Orange; it.StatusBgBrush = Palette.OrangeBg; break;
                case 3: it.Status = "就绪"; it.StatusBrush = Palette.Green; it.StatusBgBrush = Palette.GreenBg; break;
                case 4: it.Status = "正在运行"; it.StatusBrush = Palette.Blue; it.StatusBgBrush = Palette.BlueBg; break;
                default: it.Status = "未知"; it.StatusBrush = Palette.Gray; it.StatusBgBrush = Palette.GrayBg; break;
            }
            it.Trigger = "无触发器";
            it.Action = "—";
            // Definition / Triggers / Actions 都是独立的 COM 对象，必须逐个释放：
            // 只释放 task 和 folder 的话，每次刷新都会累积上百个 RCW，最终拖住任务计划服务
            object def = null;
            object triggers = null;
            object actions = null;
            try
            {
                def = t.Definition;
                if (def != null)
                {
                    triggers = ((dynamic)def).Triggers;
                    it.Trigger = FormatTriggers(triggers);
                    actions = ((dynamic)def).Actions;
                    it.Action = FormatActions(actions);
                }
            }
            catch { }
            finally
            {
                ReleaseCom(actions);
                ReleaseCom(triggers);
                ReleaseCom(def);
            }
            it.LastRun = "—";
            it.LastResult = "—";
            try
            {
                DateTime dt = Convert.ToDateTime(t.LastRunTime);
                it.LastRun = dt.Year > 2000 ? dt.ToString("yyyy-MM-dd HH:mm") : "从未运行";
            }
            catch { it.LastRun = "从未运行"; }
            try
            {
                int r = Convert.ToInt32(t.LastTaskResult);
                it.LastResult = r == 0 ? "成功 (0x0)" : "0x" + r.ToString("X");
            }
            catch { }
            it.Search = (it.Name + " " + it.FullPath + " " + it.Trigger + " " + it.Action).ToLowerInvariant();
            it.Detail = it.FullPath + "\n触发器：" + it.Trigger + "\n执行：" + it.Action + "\n上次结果：" + it.LastResult;
            return it;
        }

        private static string FormatTriggers(dynamic coll)
        {
            List<string> parts = new List<string>();
            int total = 0;
            try
            {
                total = Convert.ToInt32(coll.Count);
                for (int i = 1; i <= total && i <= 12; i++)
                {
                    object tr = null;
                    try
                    {
                        tr = ComItem(coll, i);
                        dynamic d = tr;
                        int tp = Convert.ToInt32(d.Type);
                        string sb = "";
                        try
                        {
                            string raw = Convert.ToString(d.StartBoundary);
                            if (!string.IsNullOrEmpty(raw) && raw.Length >= 16)
                            {
                                // 一次性触发器的日期就是关键信息（过期与否），必须带年份；
                                // 周期类触发器日期只起参考作用，沿用「月日 时分」省字符
                                sb = tp == 1
                                    ? raw.Substring(0, 16).Replace('T', ' ')
                                    : raw.Substring(5, 11).Replace('T', ' ');
                            }
                        }
                        catch { }
                        parts.Add(TriggerText(tp, sb));
                    }
                    catch { }
                    finally { ReleaseCom(tr); }
                }
            }
            catch { }
            if (parts.Count == 0) return "无触发器";
            string r = string.Join("；", parts.Distinct().ToArray());
            // 先截断明细再追加总数：反过来会把「…共 N 个触发器」本身截掉
            if (r.Length > 110) r = r.Substring(0, 110) + "…";
            if (total > 12) r += "；…共 " + total + " 个触发器";
            return r;
        }

        private static string TriggerText(int type, string sb)
        {
            switch (type)
            {
                case 0: return "事件触发";
                case 1: return ("一次性 " + sb).Trim();
                case 2: return ("每天 " + sb).Trim();
                case 3: return ("每周 " + sb).Trim();
                case 4: return ("每月 " + sb).Trim();
                case 5: return ("每月(按周) " + sb).Trim();
                case 6: return "空闲时触发";
                case 7: return "注册时触发";
                case 8: return "系统启动时触发";
                case 9: return "登录时触发";
                case 10: return "会话状态变化时触发";
                default: return ("计划触发 " + sb).Trim();
            }
        }

        private static string FormatActions(dynamic coll)
        {
            List<string> parts = new List<string>();
            int total = 0;
            try
            {
                total = Convert.ToInt32(coll.Count);
                for (int i = 1; i <= total && i <= 8; i++)
                {
                    object act = null;
                    try
                    {
                        act = ComItem(coll, i);
                        dynamic a = act;
                        int tp = Convert.ToInt32(a.Type);
                        if (tp == 0)
                        {
                            string exe = Convert.ToString(a.Path);
                            string arg = "";
                            try { arg = Convert.ToString(a.Arguments); } catch { }
                            parts.Add(string.IsNullOrEmpty(arg) ? exe : exe + " " + arg);
                        }
                        else if (tp == 5) parts.Add("COM 处理程序");
                        else parts.Add("其他操作 (" + tp + ")");
                    }
                    catch { }
                    finally { ReleaseCom(act); }
                }
            }
            catch { }
            if (parts.Count == 0) return "—";
            // 与 FormatTriggers 同口径：先截明细再追加总数，避免「共 N 个操作」被截掉
            string r = string.Join("；", parts.ToArray());
            if (r.Length > 150) r = r.Substring(0, 150) + "…";
            if (total > 8) r += "；…共 " + total + " 个操作";
            return r;
        }

        // ---------------- 资源管理器右键菜单项 ----------------
        internal static List<CtxMenuItem> GetContextMenuItems(List<string> warn)
        {
            List<CtxMenuItem> list = new List<CtxMenuItem>();
            Dictionary<string, string> cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            list.Add(ClassicItem());
            foreach (CtxRoot root in CtxReg.Roots)
            {
                try { AddExtHandlers(list, root, cache, warn); }
                catch (Exception ex) { AddWarn(warn, "右键扩展 " + root.Location + "：" + ex.Message); }
                try { AddShellVerbs(list, root, warn); }
                catch (Exception ex) { AddWarn(warn, "右键动词 " + root.Location + "：" + ex.Message); }
            }
            try { AddPackagedMenus(list, warn); }
            catch (Exception ex) { AddWarn(warn, "打包扩展：" + ex.Message); }
            return list;
        }

        // ---------------- 打包扩展（稀疏包 IExplorerCommand） ----------------
        // 「通过QQ发送到」「在终端中打开」「Open project in ChatGPT」这类 Win11 新菜单项
        // 不写传统注册表，只存在于 Appx 清单 + PackagedCom 镜像里，所以要单独枚举：
        // 1) HKLM\Software\Classes\PackagedCom\Package\<全名>\Class\{CLSID} 找到带 COM 类的包
        // 2) 读安装目录的 AppxManifest.xml，解析 desktop5:Verb（ ItemType -> 位置/页签，Clsid -> 标识）
        // 3) 显示名清单里没有（运行时由 DLL 的 GetTitle 提供），用包显示名 + 动词 Id 兜底
        // 4) 状态与开关复用 Shell Extensions\Blocked（Win11 对打包菜单项同样生效）
        private static string WindowsAppsDir()
        {
            string p = Environment.GetEnvironmentVariable("ProgramW6432");
            if (string.IsNullOrEmpty(p)) p = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            return Path.Combine(p, "WindowsApps");
        }

        private static bool CanListDir(string dir)
        {
            // 只需知道「能否列出」，惰性枚举取到第一个条目即停，
            // 不再全量枚举上千个包目录后把结果丢弃
            try { return Directory.EnumerateDirectories(dir).GetEnumerator().MoveNext(); }
            catch (UnauthorizedAccessException) { return false; }
            catch (Exception ex) { DiagLog.Swallow("目录枚举 " + dir, ex); return false; }
        }

        private static void AddPackagedMenus(List<CtxMenuItem> list, List<string> warn)
        {
            string root = @"Software\Classes\PackagedCom\Package";
            string appsDir = WindowsAppsDir();
            // WindowsApps 默认拒绝普通用户访问。以前 File.Exists 返回 false 就静默 return，
            // 非管理员下这一整类菜单项永远为空，用户完全不知道是被权限挡掉的
            if (!Directory.Exists(appsDir))
            {
                AddWarn(warn, "未找到 " + appsDir + "，打包扩展项未枚举");
                return;
            }
            if (!CanListDir(appsDir))
            {
                AddWarn(warn, "当前权限无法读取 " + appsDir + "，打包扩展项未枚举（以管理员身份重启可查看）");
                return;
            }
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(root))
            {
                if (k == null) return;
                foreach (string pkg in k.GetSubKeyNames())
                {
                    try { AddOnePackagedMenu(list, warn, k, root, pkg, appsDir); }
                    catch (Exception ex) { AddWarn(warn, "打包扩展 " + pkg + "：" + ex.Message); }
                }
            }
        }

        private static void AddOnePackagedMenu(List<CtxMenuItem> list, List<string> warn, RegistryKey packages,
                                               string root, string pkg, string appsDir)
        {
            using (RegistryKey pk = packages.OpenSubKey(pkg))
            {
                if (pk == null) return;
                // 注册表键名不区分大小写，比较也必须不区分（镜像/清单可能写成 class）
                bool hasClass = pk.GetSubKeyNames().Any(
                    n => string.Equals(n, "Class", StringComparison.OrdinalIgnoreCase));
                if (!hasClass) return;
            }

            string manifestPath = Path.Combine(appsDir, pkg, "AppxManifest.xml");
            if (!File.Exists(manifestPath)) return;
            XDocument doc;
            try { doc = XDocument.Load(manifestPath, LoadOptions.None); }
            catch (Exception ex)
            {
                // 清单损坏/编码异常的包被跳过要有记录，否则排查「某个打包菜单项为什么没列出」无据可查
                AddWarn(warn, "清单解析 " + pkg + "：" + ex.Message);
                return;
            }
            if (doc.Root == null) return;

            // 包显示名：根元素 DisplayName，ms-resource 未本地化时退回包名（取 '_' 前的部分）
            string displayName = (string)doc.Root.Attribute("DisplayName");
            if (string.IsNullOrEmpty(displayName) || displayName.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
                displayName = pkg.Split('_')[0];

            // DLL 路径表：com:Class Id -> Path
            Dictionary<string, string> dlls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (XElement cls in doc.Root.Descendants().Where(e => e.Name.LocalName == "Class"))
            {
                string cid = (string)cls.Attribute("Id");
                string path = (string)cls.Attribute("Path");
                if (!string.IsNullOrEmpty(cid) && !string.IsNullOrEmpty(path))
                    dlls[cid.Trim().Trim('{', '}').ToUpperInvariant()] = path;
            }

            List<XElement> menus = doc.Root.Descendants()
                .Where(e => e.Name.LocalName == "FileExplorerContextMenus").ToList();
            if (menus.Count == 0) return;
            List<XElement> verbs = menus.Descendants().Where(e => e.Name.LocalName == "Verb").ToList();
            if (verbs.Count == 0) return;

            foreach (XElement verb in verbs)
            {
                string id = (string)verb.Attribute("Clsid");
                if (string.IsNullOrEmpty(id)) continue;
                id = id.Trim().Trim('{', '}').ToUpperInvariant();
                string verbId = (string)verb.Attribute("Id");
                if (string.IsNullOrEmpty(verbId)) verbId = id;

                XElement itemType = verb.Ancestors().FirstOrDefault(e => e.Name.LocalName == "ItemType");
                string type = itemType == null ? "*" : ((string)itemType.Attribute("Type") ?? "*");

                string location;
                int tab;
                switch (type.ToUpperInvariant())
                {
                    case @"DIRECTORY\BACKGROUND": location = "桌面背景"; tab = 1; break;
                    case "DESKTOPBACKGROUND": location = "桌面背景"; tab = 1; break;
                    case "DRIVE": location = "驱动器"; tab = 2; break;
                    case "DIRECTORY": location = "文件夹"; tab = 4; break;
                    case "FOLDER": location = "文件夹"; tab = 4; break;
                    case "ALLFILESYSTEMOBJECTS": location = "文件和文件夹"; tab = 5; break;
                    default: location = "所有文件"; tab = 3; break;   // "*" 等
                }

                string dll;
                dlls.TryGetValue(id, out dll);
                if (string.IsNullOrEmpty(dll)) dll = "";

                CtxMenuItem it = new CtxMenuItem();
                it.Kind = CtxKind.Package;
                it.Tab = tab;
                it.Name = verbs.Count == 1 ? displayName : displayName + " · " + verbId;
                it.Location = location;
                it.Scope = "所有用户";
                it.Id = "{" + id + "}";
                it.Handler = dll;
                it.RelPath = "";
                it.RegPath = "打包：" + pkg;
                bool blockedUser = CtxReg.IsExtBlocked(false, it.Id);
                bool blockedMachine = CtxReg.IsExtBlocked(true, it.Id);
                it.Status = (blockedUser || blockedMachine) ? "已禁用" : "已启用";
                ApplyCtxColors(it);

                StringBuilder d = new StringBuilder();
                d.Append("类型：").Append(it.Kind).Append('\n');
                d.Append("包：").Append(pkg).Append('\n');
                d.Append("显示名（运行时由程序自身提供，这里是包名）\n");
                d.Append("位置类型：").Append(type).Append('\n');
                if (dll.Length > 0) d.Append("处理程序：").Append(dll).Append('\n');
                d.Append("屏蔽状态：");
                if (blockedUser && blockedMachine) d.Append("当前用户 + 所有用户");
                else if (blockedUser) d.Append("当前用户");
                else if (blockedMachine) d.Append("所有用户");
                else d.Append("未屏蔽");
                it.Detail = d.ToString();
                it.Search = (it.Name + " " + displayName + " " + verbId + " " + it.Kind + " " +
                             it.Location + " " + it.Scope + " " + it.Id + " " + dll).ToLowerInvariant();
                list.Add(it);
            }
        }

        // 告警统一入口：以前各处的上限不一致（8 / 60 / 无上限），
        // 服务项逐条失败时能把 warn 撑到几百条，界面只显示条数，等于白占内存
        private const int WarnMax = 60;
        private static void AddWarn(List<string> warn, string msg)
        {
            if (warn == null || string.IsNullOrEmpty(msg)) return;
            if (warn.Count < WarnMax) { warn.Add(msg); return; }
            // 截断提示占用最后一条，而不是额外 append：界面按 warn.Count 显示「N 项读取告警」，
            // 多加一条会让这个数字比真实告警数多 1
            warn[WarnMax - 1] = "…（告警过多，其余已省略）";
        }

        private static void ApplyCtxColors(CtxMenuItem it)
        {
            if (it.Status == "第三方接管")
            {
                it.StatusBrush = Palette.Orange; it.StatusBgBrush = Palette.OrangeBg;
            }
            else if (it.Status == "已禁用")
            {
                it.StatusBrush = Palette.Red; it.StatusBgBrush = Palette.RedBg;
            }
            else
            {
                it.StatusBrush = Palette.Green; it.StatusBgBrush = Palette.GreenBg;
            }
        }

        private static CtxMenuItem ClassicItem()
        {
            CtxMenuItem it = new CtxMenuItem();
            it.Kind = CtxKind.Classic;
            it.Tab = 0;                      // 全局页签
            it.Name = "经典右键菜单（Windows 10 样式）";
            it.Location = "全局";
            it.Scope = "当前用户";
            it.Id = CtxReg.ClassicClsid;
            it.RelPath = "";
            it.Handler = "";
            it.RegPath = @"HKEY_CURRENT_USER\" + CtxReg.ClassicPath + @"\InprocServer32";
            int st = CtxReg.ClassicState();
            it.Status = st == 1 ? "已启用" : (st == 2 ? "第三方接管" : "已禁用");
            ApplyCtxColors(it);
            it.Detail = "类型：" + it.Kind + "\n注册表：" + it.RegPath +
                        "\n说明：启用后资源管理器恢复 Windows 10 的完整右键菜单，不必再点「显示更多选项」";
            it.Search = (it.Name + " 经典菜单 右键 " + it.Status + " " + it.Id).ToLowerInvariant();
            return it;
        }

        private static void AddExtHandlers(List<CtxMenuItem> list, CtxRoot root,
                                           Dictionary<string, string> cache, List<string> warn)
        {
            using (RegistryKey k = Registry.ClassesRoot.OpenSubKey(root.Path + @"\shellex\ContextMenuHandlers"))
            {
                if (k == null) return;
                foreach (string sub in k.GetSubKeyNames())
                {
                    try
                    {
                        string id = "";
                        using (RegistryKey sk = k.OpenSubKey(sub))
                        {
                            if (sk != null) id = CtxReg.DefaultValue(sk).Trim();
                        }
                        if (!CtxReg.LooksLikeClsid(id) && CtxReg.LooksLikeClsid(sub)) id = sub;
                        if (!CtxReg.LooksLikeClsid(id)) continue;
                        id = id.Trim().ToUpperInvariant();

                        CtxMenuItem it = new CtxMenuItem();
                        string handler;
                        string cached;
                        if (cache.TryGetValue(id, out cached))
                        {
                            it.Name = cached;
                            handler = CtxReg.ClsidHandler(id);
                        }
                        else
                        {
                            it.Name = CtxReg.ExtName(id, sub, out handler);
                            cache[id] = it.Name;
                        }
                        it.Kind = CtxKind.Extension;
                        it.Location = root.Location;
                        it.Tab = root.Tab;
                        it.Id = id;
                        it.Handler = handler;
                        it.RelPath = root.Path + @"\shellex\ContextMenuHandlers\" + sub;
                        it.RegPath = @"HKEY_CLASSES_ROOT\" + it.RelPath;

                        bool blockedUser = CtxReg.IsExtBlocked(false, id);
                        bool blockedMachine = CtxReg.IsExtBlocked(true, id);
                        it.Status = (blockedUser || blockedMachine) ? "已禁用" : "已启用";
                        it.Scope = CtxReg.ExtScope(id);
                        ApplyCtxColors(it);

                        StringBuilder d = new StringBuilder();
                        d.Append("类型：").Append(it.Kind).Append('\n');
                        d.Append("注册表：").Append(it.RegPath).Append('\n');
                        if (handler.Length > 0) d.Append("处理程序：").Append(handler).Append('\n');
                        d.Append("屏蔽状态：");
                        if (blockedUser && blockedMachine) d.Append("当前用户 + 所有用户");
                        else if (blockedUser) d.Append("当前用户");
                        else if (blockedMachine) d.Append("所有用户");
                        else d.Append("未屏蔽");
                        it.Detail = d.ToString();
                        it.Search = (it.Name + " " + sub + " " + it.Kind + " " + root.Location + " " +
                                     it.Scope + " " + id + " " + handler).ToLowerInvariant();
                        list.Add(it);
                    }
                    catch (Exception ex) { AddWarn(warn, "扩展 " + sub + "：" + ex.Message); }
                }
            }
        }

        private static void AddShellVerbs(List<CtxMenuItem> list, CtxRoot root, List<string> warn)
        {
            using (RegistryKey k = Registry.ClassesRoot.OpenSubKey(root.Path + @"\shell"))
            {
                if (k == null) return;
                foreach (string sub in k.GetSubKeyNames())
                {
                    try
                    {
                        using (RegistryKey sk = k.OpenSubKey(sub))
                        {
                            if (sk == null) continue;
                            // 这类动词本来就不进用户菜单（固定到开始屏幕等），列出来只会误导
                            if (sk.GetValue("ProgrammaticAccessOnly") != null) continue;

                            string name = CtxReg.Indirect(Convert.ToString(sk.GetValue("MUIVerb")));
                            if (name.Length == 0) name = CtxReg.Indirect(CtxReg.DefaultValue(sk));
                            if (name.Length == 0) name = sub;

                            string rel = root.Path + @"\shell\" + sub;
                            bool userDef = VerbDefined(false, rel);
                            bool machineDef = VerbDefined(true, rel);
                            bool offUser = CtxReg.IsVerbDisabled(false, rel);
                            bool offMachine = CtxReg.IsVerbDisabled(true, rel);

                            CtxMenuItem it = new CtxMenuItem();
                            it.Name = name;
                            it.Kind = CtxKind.Verb;
                            it.Location = root.Location;
                            it.Tab = root.Tab;
                            it.Id = rel;
                            it.RelPath = rel;
                            it.Handler = "";
                            it.RegPath = @"HKEY_CLASSES_ROOT\" + rel;
                            it.Status = (offUser || offMachine) ? "已禁用" : "已启用";
                            it.Scope = CtxReg.ScopeText(userDef, machineDef);
                            ApplyCtxColors(it);

                            StringBuilder d = new StringBuilder();
                            d.Append("类型：").Append(it.Kind).Append('\n');
                            d.Append("注册表：").Append(it.RegPath).Append('\n');
                            d.Append("触发方式：").Append(sk.GetValue("Extended") != null ? "按住 Shift 才显示" : "始终显示").Append('\n');
                            d.Append("屏蔽状态：").Append((offUser || offMachine) ? "已禁用" : "未屏蔽");
                            it.Detail = d.ToString();
                            it.Search = (it.Name + " " + sub + " " + it.Kind + " " + root.Location + " " + rel).ToLowerInvariant();
                            list.Add(it);
                        }
                    }
                    catch (Exception ex) { AddWarn(warn, "动词 " + sub + "：" + ex.Message); }
                }
            }
        }

        // 只有带 command / MUIVerb / SubCommands / 默认值的键才算真的定义了菜单，
        // 我们自己的 LegacyDisable 覆盖壳不能算，否则作用域会被判成「当前用户」
        private static bool VerbDefined(bool machine, string rel)
        {
            try
            {
                using (RegistryKey k = CtxReg.Hive(machine).OpenSubKey(CtxReg.Classes + @"\" + rel))
                {
                    if (k == null) return false;
                    if (k.GetValue("MUIVerb") != null) return true;
                    if (k.GetValue("SubCommands") != null) return true;
                    if (CtxReg.DefaultValue(k).Length > 0) return true;
                    using (RegistryKey cmd = k.OpenSubKey("command")) return cmd != null;
                }
            }
            catch { return false; }
        }
    }

    // ------------------------------------------------------------------ Operations
    internal static class Manager
    {
        private static readonly byte[] ApprovalOn = new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        private static readonly byte[] ApprovalOff = new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

        internal static string ItemName(object item)
        {
            if (item is StartupItem) return ((StartupItem)item).Name;
            if (item is ServiceItem) return ((ServiceItem)item).Name;
            if (item is TaskItem) return ((TaskItem)item).Name;
            if (item is CtxMenuItem) return ((CtxMenuItem)item).Name;
            return "未知项目";
        }

        // 操作返回字符串的语义约定：null = 成功；以 PartialPrefix 开头 = 部分成功
        // （用户级已生效、系统级仍需提权之类的提醒，不计入失败，只在状态栏提示）；
        // 其余 = 失败原因
        internal const string PartialPrefix = "△ ";

        internal static string Apply(int page, string op, object item)
        {
            try
            {
                if (page == 0)
                {
                    StartupItem si = (StartupItem)item;
                    if (op != "enable" && op != "disable") return "启动项仅支持启用 / 禁用";
                    return ToggleStartup(si, op == "enable");
                }
                if (page == 1)
                {
                    ServiceItem sv = (ServiceItem)item;
                    if (op == "enable") return ChangeStartMode(sv.ServiceName, "Automatic");
                    if (op == "manual") return ChangeStartMode(sv.ServiceName, "Manual");
                    if (op == "disable") return ChangeStartMode(sv.ServiceName, "Disabled");
                    if (op == "start") return ControlService(sv.ServiceName, true);
                    if (op == "stop") return ControlService(sv.ServiceName, false);
                    return "不支持的操作";
                }
                if (page == 2)
                {
                    TaskItem ti = (TaskItem)item;
                    // schtasks 的参数由 Windows 重新解析：任务路径里若含双引号，可以闭合引号
                    // 再注入额外开关（如 /RU SYSTEM），而本程序常以管理员运行。
                    // schtasks 没有转义机制，只能拒绝这种异常路径
                    if (ti.FullPath.IndexOf('"') >= 0)
                        return "任务路径包含双引号，无法通过 schtasks 安全操作：" + ti.FullPath;
                    string quoted = "\"" + ti.FullPath + "\"";
                    if (op == "enable") return SchTasks("/Change /TN " + quoted + " /ENABLE");
                    if (op == "disable") return SchTasks("/Change /TN " + quoted + " /DISABLE");
                    if (op == "run") return SchTasks("/Run /TN " + quoted);
                    if (op == "stop") return SchTasks("/End /TN " + quoted);
                    return "不支持的操作";
                }
                if (page == 3)
                {
                    CtxMenuItem ci = (CtxMenuItem)item;
                    if (op == "enable") return CtxReg.EnableCtx(ci);
                    if (op == "disable") return CtxReg.DisableCtx(ci, false);
                    if (op == "disableAll") return CtxReg.DisableCtx(ci, true);
                    return "不支持的操作";
                }
                return "不支持的页面：" + page;
            }
            catch (Exception ex) { return ex.Message; }
        }

        private static string ToggleStartup(StartupItem si, bool enabled)
        {
            // RunOnce 由登录过程直接执行，不受 StartupApproved 控制：写进去既不生效，
            // 界面上却会显示「已禁用」，比不给按钮更糟
            if (si.OneShot)
                return "这是一次性启动项（RunOnce），登录时执行后会自动删除，Windows 不提供启用/停用开关。" +
                       "如需取消，请在注册表中删除该项（位置：" + si.Location + " / " + si.Scope +
                       "，值名「" + si.ApprovalName + "」）。";
            try
            {
                RegistryKey hive = si.Machine ? (RegistryKey)Registry.LocalMachine : Registry.CurrentUser;
                using (RegistryKey k = hive.CreateSubKey(si.ApprovalSubPath))
                {
                    if (k == null) return "无法打开注册表项：" + si.ApprovalSubPath;
                    k.SetValue(si.ApprovalName, enabled ? ApprovalOn : ApprovalOff, RegistryValueKind.Binary);
                }
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return "权限不足：无法写入 " + (si.Machine ? "HKLM" : "HKCU") + " 注册表，请以管理员身份运行";
            }
            catch (Exception ex) { return ex.Message; }
        }

        // 改启动类型（WMI ChangeServiceConfig，全程走 SCM 权限检查）。
        // 已知场景：每用户服务实例（如 cbdhsvc_729e7e / CDPUserSvc_729e7e）WMI 返回 21（无效参数），
        // 因为实例的启动类型由模板派生、登录会话重建时覆盖；正确做法是改其模板服务。
        private static string ChangeStartMode(string name, string mode)
        {
            string target = TemplateName(name);   // 每用户实例 → 模板服务

            string exMsg;
            int rc = TryWmiChangeStartMode(target, mode, out exMsg);
            if (rc == 0) return null;

            if (rc == 2)
                return "服务安全描述符未授权更改（常见于 WindowsApps 打包应用服务，"
                     + "services.msc 以管理员身份修改同样会被拒绝；如需强制修改需以 SYSTEM 身份调整服务权限，本工具不提供该操作）";
            if (rc > 0) return "WMI 返回码 " + rc + "（" + WmiText((uint)rc) + "）";
            return "WMI 调用失败：" + exMsg;
        }

        private static int TryWmiChangeStartMode(string name, string mode, out string exMsg)
        {
            exMsg = null;
            try
            {
                // WQL 字符串里 \ 和 " 都要转义，且必须先转义反斜杠（否则新引入的 \ 会留成裸字符）
                using (ManagementObject mo = new ManagementObject("Win32_Service.Name=\"" +
                    name.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""))
                {
                    mo.Get();
                    object raw = mo.InvokeMethod("ChangeStartMode", new object[] { mode });
                    return (int)Convert.ToUInt32(raw);
                }
            }
            catch (Exception ex) { exMsg = ex.Message; return -1; }
        }

        // 每用户服务实例：名称形如「模板名_十六进制后缀」，且同名模板服务的类型确实带
        // USER_SERVICE 位（0x40）。只按名字猜会把恰好带 _十六进制 后缀的普通第三方服务
        // 误判成实例，从而把启动类型改到同名的另一个服务上，所以必须用服务类型位确证。
        // 后缀形态的纯字符串判定只在这里实现；Adviser、AiBatch 均调用本方法，
        // 避免同一个服务在建议、批量请求、服务管理三处被认成不同的名字
        internal static string HexSuffixBase(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            int i = s.LastIndexOf('_');
            if (i < 4 || i == s.Length - 1) return null;
            string suf = s.Substring(i + 1);
            if (suf.Length < 4 || suf.Length > 8) return null;
            for (int k = 0; k < suf.Length; k++)
                if (!Uri.IsHexDigit(suf[k])) return null;
            return s.Substring(0, i);
        }

        internal static string TemplateOf(string name)
        {
            string tpl = HexSuffixBase(name);
            if (tpl == null) return null;
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + tpl))
                {
                    if (k == null) return null;
                    object v = k.GetValue("Type");
                    if (v == null) return null;
                    return (Convert.ToInt32(v) & 0x40) != 0 ? tpl : null;
                }
            }
            catch { return null; }
        }

        internal static bool IsPerUserInstance(string name)
        {
            return TemplateOf(name) != null;
        }

        // 每用户服务实例的“有效启动类型”：实例值只在创建时抄自模板、之后不随修改更新，
        // 真正决定下次登录是否启动的是模板服务的 Start 值。非实例返回 null。
        internal static int? TemplateStart(string name)
        {
            string tpl = TemplateOf(name);
            if (tpl == null) return null;
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + tpl))
                {
                    if (k == null) return null;
                    object v = k.GetValue("Start");
                    return v == null ? (int?)null : Convert.ToInt32(v);
                }
            }
            catch { return null; }
        }

        // 模板名（每用户实例去掉 _hex 后缀），非实例返回原值
        internal static string TemplateName(string name)
        {
            string tpl = TemplateOf(name);
            return tpl == null ? name : tpl;
        }

        private static string WmiText(uint rc)
        {
            switch (rc)
            {
                case 1: return "不支持";
                case 2: return "权限不足 / Access Denied";
                case 3: return "存在正在运行的依赖服务";
                case 4: return "服务控制无效";
                case 5: return "服务无法接受控制";
                case 6: return "服务未启动";
                case 8: return "服务已运行";
                case 10: return "服务已停止";
                case 21: return "无效参数（每用户服务实例不支持直接修改）";
                default: return "未知错误";
            }
        }

        private static string ControlService(string name, bool start)
        {
            try
            {
                using (ServiceController sc = new ServiceController(name))
                {
                    if (start)
                    {
                        sc.Start();
                        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
                    }
                    else
                    {
                        if (!sc.CanStop) return "该服务不允许停止";
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                    }
                }
                return null;
            }
            catch (System.ServiceProcess.TimeoutException)
            {
                // 超时只说明 15 秒内没到位：停止请求已被 SCM 接受，服务经常稍后才真正停下。
                // 复查一次当前状态，仍没到就如实说「可能稍后自行完成」，不再武断报「操作超时」
                try
                {
                    using (ServiceController sc = new ServiceController(name))
                    {
                        sc.Refresh();
                        ServiceControllerStatus now = sc.Status;
                        if (start && now == ServiceControllerStatus.Running) return null;
                        if (!start && now == ServiceControllerStatus.Stopped) return null;
                    }
                }
                catch { }
                return start
                    ? "服务在 15 秒内未达到运行状态，可能仍在启动中，稍后刷新列表查看"
                    : "服务在 15 秒内未完成停止，停止请求已接受，可能稍后自行完成";
            }
            catch (Exception ex) { return ex.Message; }
        }

        [DllImport("kernel32.dll")]
        private static extern uint GetOEMCP();

        // schtasks.exe 的控制台输出用的是 OEM 代码页（中文系统 936 恰好与 ANSI 一致，
        // 西文系统是 437/850 而 ANSI 是 1252/850）：用 Encoding.Default 解码在非中文区域
        // 会得到乱码，错误文案既看不懂，「ERROR / 错误」关键字匹配也可能失效。
        // 退出码判定仍是最可靠的兜底，这里把可读性也修对
        private static Encoding ProcEncoding
        {
            get
            {
                try { return Encoding.GetEncoding((int)GetOEMCP()); }
                catch { return Encoding.Default; }
            }
        }

        private static string SchTasks(string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe", args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = ProcEncoding;
                psi.StandardErrorEncoding = ProcEncoding;

                StringBuilder so = new StringBuilder();
                StringBuilder se = new StringBuilder();
                object outputGate = new object();
                using (Process p = new Process())
                {
                    p.StartInfo = psi;
                    // 两个流都异步读：顺序 ReadToEnd 在缓冲写满时会互相卡死
                    p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) lock (outputGate) so.AppendLine(e.Data);
                    };
                    p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) lock (outputGate) se.AppendLine(e.Data);
                    };
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (!p.WaitForExit(20000))
                    {
                        try { p.Kill(); } catch { }
                        return "schtasks 执行超时（20 秒）已终止，请重试或改用任务计划程序";
                    }
                    // 只等一小段时间让异步读取吐完剩余缓冲。无参 WaitForExit() 在子进程树
                    // 继承了管道句柄时会永久挂住（任务路径恰好叫 ERROR 也曾被误判失败）
                    if (!p.WaitForExit(3000))
                        DiagLog.Note("schtasks 异步输出排空超时，按退出码判定");

                    string all;
                    lock (outputGate) all = (so.ToString() + " " + se.ToString()).Trim();
                    int code = p.ExitCode;
                    if (code == 0) return null;
                    if (string.IsNullOrWhiteSpace(all)) return "schtasks 退出码 " + code;
                    string flat = all.Replace("\r", " ").Replace("\n", " ").Trim();
                    return flat.Length > 220 ? flat.Substring(0, 220) + "…" : flat;
                }
            }
            catch (Exception ex) { return ex.Message; }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        // 当前会话的 shell 进程：GetShellWindow 返回的就是本会话桌面窗口的宿主。
        // 按 PID 结束可以避免 taskkill /IM 在提权时连带杀掉其它登录用户的 explorer
        private static uint CurrentSessionShellPid()
        {
            try
            {
                IntPtr h = GetShellWindow();
                if (h == IntPtr.Zero) return 0;
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                return pid;
            }
            catch { return 0; }
        }

        // 判定「桌面 shell 是否已回来」必须以 shell 窗口为准，不能按进程名找 explorer：
        // 开启「在单独的进程中打开文件夹窗口」时，残留的文件夹窗口进程会让按名判定立即为真，
        // 于是函数返回成功，而桌面与任务栏其实还是空的，用户拿不到任何恢复指引。
        // GetShellWindow 返回的是本会话桌面窗口的宿主，explorer 退出后为 0，直到新 shell 建好窗口
        private static bool ShellRunning()
        {
            try
            {
                IntPtr h = GetShellWindow();
                if (h == IntPtr.Zero) return false;
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                if (pid == 0) return false;
                int sid;
                using (Process me = Process.GetCurrentProcess()) sid = me.SessionId;
                using (Process p = Process.GetProcessById((int)pid)) return p.SessionId == sid;
            }
            catch { return false; }
        }

        // 重启资源管理器让右键菜单改动生效。
        // 返回 null = 成功；否则返回给用户看的原因。
        internal static string RestartExplorer(bool elevated)
        {
            uint pid = CurrentSessionShellPid();
            try
            {
                if (pid != 0)
                {
                    // 只有确认这个 PID 确实是 explorer 才能结束：第三方 shell（Cairo、Blackbox 等）
                    // 同样持有 Shell 窗口，直接 Kill 会把用户当前的桌面环境一起打掉
                    string image = null;        // null = 进程已退出；空串 = 取不到映像名
                    try
                    {
                        using (Process p = Process.GetProcessById((int)pid)) image = p.ProcessName;
                    }
                    catch (ArgumentException) { image = null; }
                    catch { image = ""; }

                    if (image != null && image.Length == 0)
                        return "无法确认当前桌面 shell 的进程名，为避免误伤已取消操作。\n\n" +
                               "请用任务管理器手动重启资源管理器。";
                    if (image != null && !string.Equals(image, "explorer", StringComparison.OrdinalIgnoreCase))
                        return "当前桌面 shell 是 " + image + "，不是资源管理器。\n\n" +
                               "本功能只重启 explorer.exe；请改用该 shell 自带的重启方式，或先切回资源管理器。";

                    if (image != null)
                    {
                        using (Process p = Process.GetProcessById((int)pid))
                        {
                            p.Kill();
                            if (!p.WaitForExit(8000)) return "结束资源管理器超时，请手动在任务管理器中重启。";
                        }
                    }
                }
                else
                {
                    // 没有 shell 窗口（explorer 未运行或被第三方 shell 替换）：按会话过滤兜底，
                    // 不再用 /IM 无差别结束所有会话的 explorer
                    int sid;
                    using (Process me = Process.GetCurrentProcess()) sid = me.SessionId;
                    ProcessStartInfo psi = new ProcessStartInfo("taskkill.exe",
                        "/f /im explorer.exe /FI \"SESSION eq " + sid + "\"");
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    psi.WindowStyle = ProcessWindowStyle.Hidden;
                    using (Process p = Process.Start(psi))
                    {
                        if (!p.WaitForExit(8000))
                        {
                            try { p.Kill(); } catch { }
                            return "结束资源管理器超时，请手动在任务管理器中重启。";
                        }
                        // 128 = 进程本来就不存在
                        if (p.ExitCode != 0 && p.ExitCode != 128)
                            return "无法结束资源管理器（taskkill 退出码 " + p.ExitCode + "）";
                    }
                }
            }
            catch (ArgumentException) { /* 进程已退出，按已结束处理 */ }
            catch (Exception ex) { return "结束资源管理器失败：" + ex.Message; }

            // 个别系统配置了 AutoRestartShell 会自动拉回 shell，先等一会儿避免重复启动
            for (int i = 0; i < 20; i++)
            {
                Thread.Sleep(200);
                if (ShellRunning()) return null;
            }

            // 系统没有自愈，自己拉起来。管理员权限下不能直接 Process.Start
            //（会得到一个带管理员权限的资源管理器，之后它启动的所有程序都继承高权限），
            // 借一次性计划任务以受限权限启动；普通权限直接启动即可。
            string err = elevated ? StartShellViaTask() : StartShellDirect();
            if (err == null) return null;
            if (!elevated)
                return "资源管理器已被结束，但自动重启失败：" + err +
                       "\n\n请按 Ctrl+Shift+Esc 打开任务管理器 → 文件 → 运行新任务 → 输入 explorer.exe 启动。";
            // 计划任务方式不可用（组策略禁用 /RL /IT、任务名冲突）时再退一步：
            // 用 runas /trustlevel 以「受限令牌」启动 shell —— 能拉回桌面，但不会像
            // Process.Start 那样留下一个带管理员令牌的 explorer
            string err2 = StartShellRestricted();
            if (err2 == null) return null;
            return "资源管理器已被结束，但以受限权限自动重启失败：" + err +
                   "\n备用的受限令牌启动也失败：" + err2 +
                   "\n\n请按 Ctrl+Shift+Esc 打开任务管理器 → 文件 → 运行新任务 → 输入 explorer.exe，" +
                   "不要勾选「以管理员权限创建此任务」。";
        }

        // runas /trustlevel:0x20000 = 以「基本用户」受限令牌运行，不需要额外凭据，
        // 也不会弹 UAC。它给不了一个功能完整的 shell（部分扩展会加载失败），
        // 但桌面能回来，属于计划任务不可用时的最后兜底
        private static string StartShellRestricted()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("runas.exe",
                    "/trustlevel:0x20000 \"explorer.exe\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                using (Process p = Process.Start(psi))
                {
                    if (!p.WaitForExit(10000))
                    {
                        try { p.Kill(); } catch { }
                        return "runas 执行超时（10 秒）";
                    }
                }
                for (int i = 0; i < 15; i++)
                {
                    Thread.Sleep(200);
                    if (ShellRunning()) return null;
                }
                return "runas 已返回，但未检测到 explorer 进程";
            }
            catch (Exception ex) { return ex.Message; }
        }

        private static string StartShellDirect()
        {
            try
            {
                // 只发出启动命令，不等子进程：using 仅用于及时释放进程句柄
                using (Process.Start("explorer.exe")) { }
                for (int i = 0; i < 15; i++)
                {
                    Thread.Sleep(200);
                    if (ShellRunning()) return null;
                }
                return "启动命令已发出，但未检测到 explorer 进程";
            }
            catch (Exception ex) { return ex.Message; }
        }

        // 借一次性计划任务以受限（非提权）身份在交互会话里启动 shell：
        // 提权进程给自己降级的常规做法，任务用完即删。
        // /IT 让任务绑定交互式登录令牌：不勾选它时计划任务可能跑到非交互会话里，
        // 桌面看起来仍然是空的
        private static string StartShellViaTask()
        {
            int selfPid;
            // 进程对象不释放会在每次重启时泄漏一个句柄
            using (Process me = Process.GetCurrentProcess()) selfPid = me.Id;
            string name = "PCTools_ShellRestart_" + selfPid;
            string err = SchTasks("/Create /F /TN \"" + name + "\" /SC ONCE /ST 00:00 /RL LIMITED /IT /TR \"explorer.exe\"");
            if (err != null) return err;
            err = SchTasks("/Run /TN \"" + name + "\"");
            // /Run 只是把任务入队就返回，紧跟 /Delete /F 会在调度器把 explorer 拉起来之前
            // 删掉任务定义，桌面就回不来了。先轮询确认进程真的出现，再清理任务
            bool spawned = false;
            if (err == null)
            {
                // /Run 到 explorer 真正出现可能超过 3 秒（磁盘繁忙等），过早 /Delete
                // 会让任务定义先于调度被删、explorer 永远不会被拉起。给到 10 秒，
                // 本流程在后台线程执行，不阻塞界面
                for (int i = 0; i < 50; i++)
                {
                    Thread.Sleep(200);
                    if (ShellRunning()) { spawned = true; break; }
                }
            }
            // 清理是尽力而为：删不掉只是残留一个不会自触发的一次性任务，不影响功能
            SchTasks("/Delete /F /TN \"" + name + "\"");
            if (err != null) return err;
            return spawned ? null : "计划任务已触发，但未检测到 explorer 进程";
        }

        internal static bool IsPermissionError(string message)
        {
            if (string.IsNullOrEmpty(message)) return false;
            // 不要用「5 」「0x5」这种过短片段：WMI 返回码 5（服务控制无效）之类的普通失败
            // 会被误判成权限问题，进而弹出「请以管理员身份重启」的错误引导
            string[] keys = new string[] { "权限不足", "拒绝访问", "Access is denied", "Access Denied",
                                           "0x80070005", "需要提升", "Unauthorized", "请求的操作需要提升",
                                           "错误 5：", "错误 5 ", "error 5 ", "error 5:" };
            foreach (string k in keys)
                if (message.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }
    }

    // ------------------------------------------------------------------ Theme
    internal static class ThemeManager
    {
        // 主题偏好进程内缓存：IsLight() 在启动、主窗构造、每个弹窗 ApplyFrame 都会被调用，
        // 不能每次都开库；这个值只有本进程会写，写成功后同步更新缓存即可
        private static readonly object prefGate = new object();
        private static bool? prefCache;
        // bool? 不能声明为 volatile；用 volatile 标志发布，真正的读写都放在锁内
        private static volatile bool prefLoaded;
        // 最近一次实际生效的主题。DB 读取失败时用它兜底，避免把「读失败」当成「用户没设置」
        private static bool? lastAppliedLight;
        // 读失败退避截止时刻（UTC）：窗口内 SavedPreference 直接返回「读失败」，不再开库
        private static DateTime failBackoffUntil;

        // 主题保存失败原因（原 AppConfig.LastError 的替代）
        internal static string LastError = "";

        internal static bool IsLight()
        {
            string force = Environment.GetEnvironmentVariable("PCTOOLS_THEME");
            if (!string.IsNullOrEmpty(force))
                return !string.Equals(force, "dark", StringComparison.OrdinalIgnoreCase);
            bool readOk;
            bool? saved = SavedPreference(out readOk);
            if (readOk)
            {
                if (saved.HasValue)
                {
                    lock (prefGate) lastAppliedLight = saved.Value;
                    return saved.Value;
                }
                // 明确没有保存过偏好：跟随系统主题
                bool sys = SystemLight();
                lock (prefGate) lastAppliedLight = sys;
                return sys;
            }
            // 读取失败：保持当前已生效主题，不回退到系统值；下次调用会重试
            lock (prefGate) { if (lastAppliedLight.HasValue) return lastAppliedLight.Value; }
            bool fallback = SystemLight();
            lock (prefGate)
            {
                if (!lastAppliedLight.HasValue) lastAppliedLight = fallback;
                return lastAppliedLight.Value;
            }
        }

        private static bool SystemLight()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (k == null) return true;
                    object v = k.GetValue("AppsUseLightTheme", 1);
                    return Convert.ToInt32(v) != 0;
                }
            }
            catch { return true; }
        }

        // 自检/无需区分读失败的调用点用这个简化重载
        internal static bool? SavedPreference()
        {
            bool ignored;
            return SavedPreference(out ignored);
        }

        internal static bool? SavedPreference(out bool readOk)
        {
            if (prefLoaded)
                lock (prefGate) { if (prefLoaded) { readOk = true; return prefCache; } }
            // 读失败后的退避窗口：IsLight 在启动、主窗构造、每个弹窗 ApplyFrame 都会问一次，
            // 库持续不可读时不能每次调用都白开一次库（3 秒 busy 超时的失败 IO）
            if (DateTime.UtcNow < failBackoffUntil) { readOk = false; return null; }
            string s = "";
            readOk = false;
            try
            {
                if (!AppDb.Ensure()) LastError = AppDb.LastError;
                else
                {
                    string err;
                    IntPtr db = Sqlite.Open(AppDb.FilePath, out err);
                    if (db == IntPtr.Zero) LastError = err;
                    else
                    {
                        try
                        {
                            List<string> rows = Sqlite.Column(db,
                                "SELECT IFNULL(value,'') FROM app_setting WHERE key='ui.theme';", out err);
                            if (err != null) LastError = err;
                            else
                            {
                                readOk = true;
                                if (rows.Count > 0) s = rows[0].Trim().ToLowerInvariant();
                            }
                        }
                        finally { Sqlite.Close(db); }
                    }
                }
            }
            catch (Exception ex) { LastError = ex.Message; }
            bool? result = s == "light" ? (bool?)true : (s == "dark" ? (bool?)false : null);
            // 读取失败不等于「用户没设置主题」：不写缓存，退避 5 秒后允许重试
            if (!readOk) { failBackoffUntil = DateTime.UtcNow.AddSeconds(5); return result; }
            lock (prefGate)
            {
                // 另一个线程可能已经写入了更新的值（SavePreference），不要倒退
                if (prefLoaded) return prefCache;
                prefCache = result;
                prefLoaded = true;
                return prefCache;
            }
        }

        internal static bool SavePreference(bool light)
        {
            LastError = "";
            try
            {
                if (!AppDb.Ensure()) { LastError = AppDb.LastError; return false; }
                string err;
                IntPtr db = Sqlite.Open(AppDb.FilePath, out err);
                if (db == IntPtr.Zero) { LastError = err; return false; }
                try
                {
                    string e = Sqlite.Exec(db,
                        "INSERT OR REPLACE INTO app_setting (key,value) VALUES ('ui.theme'," +
                        Sqlite.Q(light ? "light" : "dark") + ");");
                    if (e != null) { LastError = e; return false; }
                }
                finally { Sqlite.Close(db); }
                lock (prefGate)
                {
                    prefCache = light;
                    prefLoaded = true;
                    lastAppliedLight = light;
                }
                return true;
            }
            catch (Exception ex) { LastError = ex.Message; return false; }
        }

        internal static void Apply(Application app, bool light)
        {
            lock (prefGate) lastAppliedLight = light;
            ResourceDictionary d = app.Resources;
            if (light)
            {
                Put(d, "AppBg", "#F3F5F8", 0.96);
                Put(d, "TitleBarBg", "#F3F5F8", 0.60);
                Put(d, "NavBg", "#F7F8FA", 0.94);
                Put(d, "ContentBg", "#FFFFFF", 0.99);
                Put(d, "CardBg", "#FFFFFF", 0.99);
                Put(d, "HeaderBg", "#F8F9FB", 0.99);
                Put(d, "BtnBg", "#FFFFFF", 0.90);
                Put(d, "HoverBg", "#EEF1F5", 0.95);
                Put(d, "PressBg", "#E2E7EE", 0.95);
                Put(d, "NavSelectedBg", "#E8EFF8", 0.98);
                Put(d, "SelectionBg", "#E8F1FC", 0.98);
                Put(d, "RowHoverBg", "#F4F6F9", 1);
                Put(d, "RowSelectedBg", "#E4EFFB", 1);
                Put(d, "ScrollThumb", "#BFC6CF", 1);
                Put(d, "ScrollThumbHover", "#9EA7B2", 1);
                Put(d, "WinBtnHover", "#EEF1F5", 1);
                Put(d, "CloseHover", "#C42B1C", 1);
                Put(d, "TextPrimary", "#1A1D21", 1);
                Put(d, "TextSecondary", "#6C737E", 1);
                Put(d, "Accent", "#0F6CBD", 1);
                Put(d, "BorderColor", "#E3E7ED", 1);
                Put(d, "GridLine", "#F0F2F6", 1);
                Put(d, "StatusBg", "#FAFBFC", 0.96);
                Put(d, "MenuBg", "#FFFFFF", 0.99);
                Put(d, "DialogBg", "#FFFFFF", 0.99);
                Put(d, "BtnBorder", "#D5DAE2", 1);
            }
            else
            {
                Put(d, "AppBg", "#1B1E22", 0.96);
                Put(d, "TitleBarBg", "#1B1E22", 0.60);
                Put(d, "NavBg", "#202327", 0.94);
                Put(d, "ContentBg", "#25282D", 0.99);
                Put(d, "CardBg", "#25282D", 0.99);
                Put(d, "HeaderBg", "#2B2F35", 0.99);
                Put(d, "BtnBg", "#2E3238", 0.90);
                Put(d, "HoverBg", "#2F343A", 0.95);
                Put(d, "PressBg", "#3A4046", 0.95);
                Put(d, "NavSelectedBg", "#2B3A4A", 0.98);
                Put(d, "SelectionBg", "#25384B", 0.98);
                Put(d, "RowHoverBg", "#2B2F35", 1);
                Put(d, "RowSelectedBg", "#25384B", 1);
                Put(d, "ScrollThumb", "#484F58", 1);
                Put(d, "ScrollThumbHover", "#606974", 1);
                Put(d, "WinBtnHover", "#2F343A", 1);
                Put(d, "CloseHover", "#C42B1C", 1);
                Put(d, "TextPrimary", "#F2F4F7", 1);
                Put(d, "TextSecondary", "#A4ACB6", 1);
                Put(d, "Accent", "#63C8FF", 1);
                Put(d, "BorderColor", "#363B42", 1);
                Put(d, "GridLine", "#2E3339", 1);
                Put(d, "StatusBg", "#202327", 0.96);
                Put(d, "MenuBg", "#262A2F", 0.99);
                Put(d, "DialogBg", "#262A2F", 0.99);
                Put(d, "BtnBorder", "#4A5058", 1);
            }
            // 滚动条模板装进应用级资源，主窗口与弹窗共用同一套外观
            try
            {
                ResourceDictionary skin = (ResourceDictionary)XamlReader.Parse(Views.ScrollSkin);
                foreach (object k in skin.Keys) d[k] = skin[k];
            }
            catch { }
            Palette.Refresh(light);
        }

        private static void Put(ResourceDictionary d, string key, string hex, double opacity)
        {
            SolidColorBrush b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Opacity = opacity;
            b.Freeze();
            d[key] = b;
        }
    }

    // XAML 常量见 PCTools.Views.cs

    // ------------------------------------------------------------------ Main window
    // 批量操作进度窗：worker 线程跑操作，这里只负责显示与「取消」意图
    public class ProgressWindow : Window
    {
        private TextBlock detail;
        private ProgressBar bar;
        private Button cancelBtn;
        private TextBlock hint;
        private TextBlock head;
        private TextBlock tokens;
        private string verb = "";
        private volatile bool cancelled;
        private bool finished;
        private bool completed;      // 已跑完但窗口保留，等用户点「关闭」
        private bool forceClose;     // 主窗口退出时强制放行
        internal Action OnCancel;    // 取消回调：让调用方中止在途请求

        internal bool Cancelled { get { return cancelled; } }

        internal bool Completed { get { return completed; } }

        internal string Detail { get { return detail == null ? "" : detail.Text; } }

        internal string Tokens { get { return tokens == null ? "" : tokens.Text; } }

        internal ProgressWindow(string verb, int total)
        {
            Title = "正在执行";
            Width = 480;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            FrameworkElement view = (FrameworkElement)XamlReader.Parse(Views.Progress);
            DialogShell.AttachTheme(view);
            DialogShell.Prepare(this, view);

            detail = (TextBlock)view.FindName("PgText");
            bar = (ProgressBar)view.FindName("PgBar");
            cancelBtn = (Button)view.FindName("BtnCancel");
            hint = (TextBlock)view.FindName("PgHint");
            head = (TextBlock)view.FindName("PgHead");
            tokens = (TextBlock)view.FindName("PgTokens");
            this.verb = verb;
            head.Text = "正在执行「" + verb + "」";
            bar.Maximum = Math.Max(1, total);
            bar.Value = 0;
            detail.Text = "0 / " + total + " · 准备中…";

            cancelBtn.Click += OnCancelClick;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e) { RequestCancel(); }

        private void OnCloseClick(object sender, RoutedEventArgs e) { Close(); }

        internal void RequestCancel()
        {
            if (completed) return;          // 已完成，按钮这时是「关闭」，不该再走取消流程
            cancelled = true;
            Action cb = OnCancel;
            if (cb != null) { try { cb(); } catch { } }
            if (cancelBtn != null)
            {
                cancelBtn.IsEnabled = false;
                cancelBtn.Content = "正在停止…";
            }
            if (detail != null) detail.Text = "正在停止，等待当前项目完成…";
        }

        internal void Report(int done, int total, string name)
        {
            Report(done, total, name, 0, 0, false);
        }

        // 逐项 AI 分析专用：顺带刷新本次累计 Token（接口没回用量时保持折叠）
        internal void Report(int done, int total, string name, int inTokens, int outTokens, bool hasUsage)
        {
            if (finished) return;
            bar.Maximum = Math.Max(1, total);
            bar.Value = done;
            detail.Text = string.IsNullOrEmpty(name)
                ? "已完成 " + done + " / " + total
                : "已完成 " + done + " / " + total + " · " + name;
            if (tokens == null) return;
            if (hasUsage)
            {
                tokens.Text = "本次已用　输入：" + inTokens.ToString("N0") +
                              " Token / 输出：" + outTokens.ToString("N0") + " Token";
                tokens.Visibility = Visibility.Visible;
            }
            else tokens.Visibility = Visibility.Collapsed;
        }

        // 结束时才真正关窗；用户点「取消」或右上角 X 只是发出取消请求
        internal void Finish()
        {
            finished = true;
            Close();
        }

        // 跑完但不自动关闭：结果写进提示行，按钮换成「关闭」，由用户决定何时返回
        internal void FinishKeepOpen(string summary)
        {
            if (finished) return;
            completed = true;
            finished = true;                 // 让 OnClosing 放行，窗口才能真正关掉
            if (bar != null) bar.Value = bar.Maximum;
            if (head != null) head.Text = "「" + verb + "」已完成";
            if (detail != null) detail.Text = summary;
            if (tokens != null) tokens.Visibility = Visibility.Collapsed;
            if (hint != null) hint.Text = "本次操作已完成，点右下角「关闭」返回";
            if (cancelBtn != null)
            {
                cancelBtn.Click -= OnCancelClick;
                cancelBtn.Click += OnCloseClick;
                cancelBtn.Content = "关闭";
                cancelBtn.IsEnabled = true;
            }
        }

        // 主窗口退出时强制关掉，不再把 Close 转成「取消」
        internal void ForceClose()
        {
            forceClose = true;
            finished = true;
            try { Close(); }
            catch { }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!finished && !forceClose)
            {
                e.Cancel = true;
                RequestCancel();
                return;
            }
            base.OnClosing(e);
        }
    }

    public class MainWindow : Window
    {
        private FrameworkElement root;
        private ListBox nav;
        private TextBox search;
        private TextBlock searchHint;
        private TextBlock pageTitle;
        private TextBlock pageSubtitle;
        private TextBlock statusText;
        private TextBlock countText;
        private TextBlock listSummary;            // 列表上方的条数与分布统计
        private Button btnAnalyzeAll;             // 逐项 AI 分析
        private bool aiBusy;                     // AI 批量任务进行中，防重入
        private ProgressWindow aiProgress;       // 当前非模态 AI 进度窗
        private bool quitting;                   // 主窗口已进入关闭流程
        private Button btnAdmin;
        private readonly DataGrid[] grids = new DataGrid[4];
        private readonly Grid[] pages = new Grid[4];
        private readonly FrameworkElement[] empties = new FrameworkElement[4];
        // 搜索防抖定时器：停止输入 200ms 后才执行一次全量过滤（UI 线程单实例，无并发）
        private readonly DispatcherTimer searchDefer =
            new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        private readonly StackPanel[] cmds = new StackPanel[4];
        private readonly string[] titles = new string[] { "启动应用", "服务管理", "计划任务", "右键菜单" };
        private readonly string[] subtitles = new string[]
        {
            "随 Windows 登录自动运行的注册表项与启动文件夹快捷方式",
            "Windows 服务及其启动类型、运行状态与登录身份",
            "任务计划程序中的任务、触发器与最近执行结果",
            "资源管理器右键菜单项：Shell 扩展、静态动词与经典菜单开关"
        };

        private List<StartupItem> startupData = new List<StartupItem>();
        private List<ServiceItem> serviceData = new List<ServiceItem>();
        private List<TaskItem> taskData = new List<TaskItem>();
        private List<CtxMenuItem> ctxData = new List<CtxMenuItem>();
        private int ctxTab = 1;                    // 0 全局 / 1 桌面 / 2 驱动器 / 3 文件 / 4 文件夹 / 5 文件和文件夹
        private readonly RadioButton[] ctxTabs = new RadioButton[6];
        private readonly List<string> warnings = new List<string>();
        private int current = 0;
        private bool collapsed = false;
        private bool isAdmin = false;
        private bool probe = false;
        private bool isLight = true;
        private ResourceDictionary themeMirror;
        private Grid iconLight;
        private Grid iconDark;
        private readonly string[] sortPath = new string[4];
        private readonly ListSortDirection[] sortDir = new ListSortDirection[4];
        // 工具条按钮与右键菜单共用同一套可操作性判定：以前菜单会置灰、工具条却照跑，
        // 选中 RunOnce 点「禁用」必然失败，刷新中点按钮还会作用在旧快照上
        private readonly List<Button> toolButtons = new List<Button>();
        private readonly Dictionary<Button, int> toolPages = new Dictionary<Button, int>();
        private readonly Dictionary<Button, string> toolOps = new Dictionary<Button, string>();
        // 隐藏页的列只能临时用设计宽度当 MinWidth，显示后必须恢复 XAML 里的最小宽度
        private readonly Dictionary<DataGridColumn, double> designMinWidth = new Dictionary<DataGridColumn, double>();
        private volatile bool loading;
        // 一次加载还没跑完时又来刷新请求：记下来，等本次结束后补跑，不能直接丢弃
        private const int NoPending = int.MinValue;
        private int pendingLoadPage = NoPending;
        private string pendingStatus;
        private bool closed;

        public MainWindow(bool probeOnly, bool admin)
        {
            probe = probeOnly;
            isAdmin = admin;
            Title = "PCTools - 系统项目管理";
            Width = 1180;
            Height = 760;
            MinWidth = 960;
            MinHeight = 600;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = false;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.CanResize;
            FontFamily = AppFont.Ui;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            FontSize = 12;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            WindowChrome chrome = new WindowChrome();
            chrome.CaptionHeight = 0;
            chrome.ResizeBorderThickness = new Thickness(5);
            chrome.GlassFrameThickness = new Thickness(0);
            chrome.CornerRadius = new CornerRadius(0);
            chrome.UseAeroCaptionButtons = false;
            WindowChrome.SetWindowChrome(this, chrome);

            root = (FrameworkElement)XamlReader.Parse(Views.Main);
            Content = root;
            // DynamicResource is resolved at runtime; mirror the theme dictionary onto the
            // root so lookups succeed even before the window joins the application tree.
            if (Application.Current != null && Application.Current.Resources != null)
            {
                themeMirror = new ResourceDictionary();
                foreach (object key in Application.Current.Resources.Keys)
                    themeMirror[key] = Application.Current.Resources[key];
                root.Resources.MergedDictionaries.Add(themeMirror);
            }
            isLight = ThemeManager.IsLight();

            nav = Find<ListBox>("Nav");
            search = Find<TextBox>("SearchBox");
            searchHint = Find<TextBlock>("SearchHint");
            pageTitle = Find<TextBlock>("PageTitle");
            pageSubtitle = Find<TextBlock>("PageSubtitle");
            statusText = Find<TextBlock>("StatusText");
            countText = Find<TextBlock>("CountText");
            listSummary = Find<TextBlock>("ListSummary");
            btnAnalyzeAll = Find<Button>("BtnAnalyzeAll");
            btnAdmin = Find<Button>("BtnAdmin");
            grids[0] = Find<DataGrid>("GridStartup");
            grids[1] = Find<DataGrid>("GridService");
            grids[2] = Find<DataGrid>("GridTask");
            grids[3] = Find<DataGrid>("GridCtx");
            foreach (DataGrid g in grids)
            {
                g.Sorting += OnGridSorting;
                DataGrid captured = g;
                g.SizeChanged += delegate { FitLastColumn(captured); };
                // 纵向滚动条出现/消失会改变可视宽度，只在 ScrollChanged 里才能拿到新值
                g.Loaded += delegate { HookFitScroll(captured); };
                // 记录 XAML 设计的最小宽度；隐藏页才临时抬高，显示时恢复，
                // 否则窄窗口下最后一列被固定列的「设计宽度」卡住，过早出现横向滚动
                foreach (DataGridColumn c in g.Columns)
                    designMinWidth[c] = c.MinWidth;
            }
            pages[0] = Find<Grid>("PageStartup");
            pages[1] = Find<Grid>("PageService");
            pages[2] = Find<Grid>("PageTask");
            pages[3] = Find<Grid>("PageCtx");
            ApplyHiddenColumnMinimums();
            empties[0] = Find<FrameworkElement>("EmptyStartup");
            empties[1] = Find<FrameworkElement>("EmptyService");
            empties[2] = Find<FrameworkElement>("EmptyTask");
            empties[3] = Find<FrameworkElement>("EmptyCtx");
            cmds[0] = Find<StackPanel>("CmdStartup");
            cmds[1] = Find<StackPanel>("CmdService");
            cmds[2] = Find<StackPanel>("CmdTask");
            cmds[3] = Find<StackPanel>("CmdCtx");
            ctxTabs[0] = Find<RadioButton>("TabCtxAll");
            ctxTabs[1] = Find<RadioButton>("TabCtxDesktop");
            ctxTabs[2] = Find<RadioButton>("TabCtxDrive");
            ctxTabs[3] = Find<RadioButton>("TabCtxFile");
            ctxTabs[4] = Find<RadioButton>("TabCtxFolder");
            ctxTabs[5] = Find<RadioButton>("TabCtxAfo");
            iconLight = Find<Grid>("IconLight");
            iconDark = Find<Grid>("IconDark");
            UpdateThemeButton();

            if (isAdmin)
            {
                btnAdmin.Content = "✓ 管理员权限";
                btnAdmin.IsEnabled = false;
            }

            WireEvents();
            SourceInitialized += OnSourceInitialized;
            Loaded += delegate
            {
                UpdateLayout();
                AlignAiIcon();
                UpdateLayout();
            };
            if (!probe)
            {
                Loaded += delegate { RefreshAll(); };
                // 建库失败在正常启动路径没有别的出口：列表照常枚举、AI 建议全空、
                // 保存静默失败，用户只会觉得「功能坏了」。这里明确告知一次
                if (!AppDb.Usable)
                {
                    Loaded += delegate
                    {
                        Dispatcher.BeginInvoke((Action)delegate
                        {
                            if (quitting || closed) return;
                            Dialogs.Info(this, "配置库不可用",
                                "无法打开配置库：\n" + AppDb.FilePath + "\n\n" +
                                (string.IsNullOrEmpty(AppDb.LastError) ? "原因未知" : AppDb.LastError) +
                                "\n\n本次运行中，AI 配置、主题与 AI 结果都无法保存。\n" +
                                "请确认该文件未被其它程序占用、所在目录可写；必要时删除该文件后重新配置。");
                        });
                    };
                }
            }
            // 主窗口退出时必须用 Closing（早于「拥有窗口」被连带关闭），
            // 否则进度窗的 OnClosing 会把关闭转成「取消」，退出流程挂住
            Closing += delegate
            {
                quitting = true;
                searchDefer.Stop();   // 关窗后不再触发延迟过滤
                if (aiProgress != null)
                {
                    // 先发取消让 worker 在下一项之前停下，再中止在途请求：
                    // 否则关窗后它仍会把整批跑完、继续写库并继续消耗 Token
                    aiProgress.RequestCancel();
                    AiClient.AbortAll();
                    aiProgress.ForceClose();
                    aiProgress = null;
                }
                else AiClient.AbortAll();
            };
            Closed += delegate { closed = true; };
        }

        private T Find<T>(string name) where T : class
        {
            object o = root.FindName(name);
            if (o == null) throw new InvalidOperationException("界面缺少控件：" + name);
            T t = o as T;
            if (t == null) throw new InvalidOperationException("控件类型不符：" + name);
            return t;
        }

        private void WireEvents()
        {
            nav.SelectionChanged += delegate { SwitchPage(nav.SelectedIndex < 0 ? 0 : nav.SelectedIndex); };
            // 搜索防抖：每次按键都要新建 4 个列表 + 反射排序 + 4 个 ItemsSource 重设，
            // 大列表连续输入会卡顿。停止输入 200ms 后才真正过滤，清空立即生效
            searchDefer.Tick += delegate
            {
                searchDefer.Stop();
                ApplyFilter();
            };
            search.TextChanged += delegate
            {
                searchHint.Visibility = string.IsNullOrEmpty(search.Text) ? Visibility.Visible : Visibility.Collapsed;
                searchDefer.Stop();
                if (string.IsNullOrEmpty(search.Text)) ApplyFilter();
                else searchDefer.Start();
            };

            Find<Button>("BtnHamburger").Click += delegate { ToggleNav(); };
            Find<Button>("BtnMin").Click += delegate { WindowState = WindowState.Minimized; };
            Find<Button>("BtnMax").Click += delegate
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            };
            Find<Button>("BtnClose").Click += delegate { Close(); };
            Find<Button>("BtnTheme").Click += delegate
            {
                SetTheme(!isLight);
                if (!ThemeManager.SavePreference(isLight))
                    SetStatus("主题已切换，但设置未能保存：" + ThemeManager.LastError);
            };
            Find<Button>("BtnAiConfig").Click += delegate { OpenAiSettings(); };
            btnAdmin.Click += delegate { Program.RestartElevated(); };

            Grid titleBar = Find<Grid>("TitleBar");
            titleBar.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (DialogShell.IsInteractive(e.OriginalSource as DependencyObject)) return;
                if (e.ClickCount == 2)
                    WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                else
                    DragMove();
            };

            Wire("BtnStartupEnable", 0, "enable");
            Wire("BtnStartupDisable", 0, "disable");
            Wire("BtnServiceEnable", 1, "enable");
            Wire("BtnServiceDisable", 1, "disable");
            Wire("BtnServiceStart", 1, "start");
            Wire("BtnServiceStop", 1, "stop");
            Wire("BtnTaskEnable", 2, "enable");
            Wire("BtnTaskDisable", 2, "disable");
            Wire("BtnTaskRun", 2, "run");
            Wire("BtnTaskStop", 2, "stop");
            Wire("BtnCtxEnable", 3, "enable");
            Wire("BtnCtxDisable", 3, "disable");
            // SelectionChanged 每页只订阅一次：原来 Wire 每调一次就追加一个相同处理器，
            // 服务/任务页各挂 4 个，选中变化会重复执行 4 遍 UpdateToolbarState
            for (int i = 0; i < grids.Length; i++)
                grids[i].SelectionChanged += delegate { UpdateToolbarState(); };
            UpdateToolbarState();
            // ToolTip 只在文本确实被省略号裁剪时才弹：TextCell/SubCell 给所有文本列
            // 挂了恒显 ToolTip，未截断的短文本悬停会弹冗余气泡
            for (int i = 0; i < grids.Length; i++)
                grids[i].AddHandler(TextBlock.ToolTipOpeningEvent,
                    new ToolTipEventHandler(TrimmedToolTipOnly));
            WireRefresh("BtnStartupRefresh", 0);
            WireRefresh("BtnServiceRefresh", 1);
            WireRefresh("BtnTaskRefresh", 2);
            WireAiAdvice("BtnStartupAi", 0);
            WireAiAdvice("BtnServiceAi", 1);
            WireAiAdvice("BtnTaskAi", 2);
            btnAnalyzeAll.Click += delegate { UpdateAiAnalysis(current, btnAnalyzeAll); };
            WireRefresh("BtnCtxRefresh", 3);
            Find<Button>("BtnCtxRestart").Click += delegate { RestartExplorerFlow(); };
            for (int i = 0; i < ctxTabs.Length; i++)
            {
                int idx = i;
                ctxTabs[i].Checked += delegate { SetCtxTab(idx); };
            }
            WireContextMenus();

            // 双击任意一行直接发起 AI 分析
            for (int i = 0; i < grids.Length; i++)
            {
                int page = i;
                grids[i].MouseDoubleClick += delegate(object s, MouseButtonEventArgs e)
                {
                    DataGridRow row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
                    if (row == null) return;          // 双击表头或空白处不处理
                    // 双击的语义是「只分析这一行」：多选状态下必须先清空，
                    // 否则 SelectedItems 里还留着其它行，单条分析的前置校验会直接报错
                    grids[page].SelectedItems.Clear();
                    row.IsSelected = true;
                    grids[page].SelectedItem = row.Item;
                    e.Handled = true;
                    AiAnalyze(page);
                };
            }
        }

        private void WireContextMenus()
        {
            for (int page = 0; page < grids.Length; page++)
            {
                int p = page;
                ContextMenu cm = BuildContextMenu(p);
                DataGrid grid = grids[p];
                grid.ContextMenu = cm;
                grid.PreviewMouseRightButtonDown += delegate(object s, MouseButtonEventArgs e)
                {
                    DataGridRow row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
                    if (row == null) return;
                    if (!row.IsSelected)
                    {
                        grid.SelectedItems.Clear();
                        row.IsSelected = true;
                    }
                    grid.CurrentItem = row.Item;
                };
                grid.ContextMenuOpening += delegate(object s, ContextMenuEventArgs e)
                {
                    if (grid.SelectedItems.Count == 0) { e.Handled = true; return; }
                    UpdateMenuState(p, cm);
                };
            }
        }

        private ContextMenu BuildContextMenu(int page)
        {
            ContextMenu cm = new ContextMenu();
            cm.Style = (Style)root.Resources["CtxMenu"];
            if (page == 0)
            {
                AddMenu(cm, page, "启用", "enable");
                AddMenu(cm, page, "禁用", "disable");
                cm.Items.Add(MakeSeparator());
                AddMenu(cm, page, "复制启动命令", "copy");
                cm.Items.Add(MakeSeparator());
                AddMenu(cm, page, "AI 分析", "ai");
                cm.Items.Add(MakeSeparator());
                AddMenu(cm, page, "刷新列表", "refresh");
            }
            else if (page == 1)
            {
                AddMenu(cm, page, "启用（自动启动）", "enable");
                AddMenu(cm, page, "改为手动", "manual");
                AddMenu(cm, page, "禁用", "disable");
                cm.Items.Add(MakeSeparator());
                AddMenu(cm, page, "启动服务", "start");
                AddMenu(cm, page, "停止服务", "stop");
                cm.Items.Add(MakeSeparator());
                AddMenu(cm, page, "复制服务名称", "copy");
                cm.Items.Add(MakeSeparator());
                AddMenu(cm, page, "AI 分析", "ai");
                cm.Items.Add(MakeSeparator());
                AddMenu(cm, page, "刷新列表", "refresh");
            }
            else if (page == 2)
            {
                AddMenu(cm, page, "启用任务", "enable");
                AddMenu(cm, page, "禁用任务", "disable");
                cm.Items.Add(MakeSeparator());
                AddMenu(cm, page, "立即运行", "run");
                AddMenu(cm, page, "停止运行", "stop");
                cm.Items.Add(MakeSeparator());
                AddMenu(cm, page, "复制任务路径", "copy");
                cm.Items.Add(MakeSeparator());
                AddMenu(cm, page, "AI 分析", "ai");
                cm.Items.Add(MakeSeparator());
                AddMenu(cm, page, "刷新列表", "refresh");
            }
            else
            {
                AddMenu(cm, page, "启用", "enable");
                AddMenu(cm, page, "禁用（仅当前用户）", "disable");
                AddMenu(cm, page, "所有用户屏蔽", "disableAll");
                cm.Items.Add(MakeSeparator());
                AddMenu(cm, page, "复制标识", "copy");
                cm.Items.Add(MakeSeparator());
                AddMenu(cm, page, "AI 分析", "ai");
                cm.Items.Add(MakeSeparator());
                AddMenu(cm, page, "刷新列表", "refresh");
            }
            return cm;
        }

        private void AddMenu(ContextMenu cm, int page, string header, string op)
        {
            MenuItem mi = new MenuItem();
            mi.Header = header;
            mi.Tag = op;
            mi.Style = (Style)root.Resources["CtxItem"];
            int p = page;
            string o = op;
            mi.Click += delegate
            {
                // 先让菜单收起，再执行（刷新等操作会阻塞 UI 线程，否则菜单会一直留着）
                if (cm.IsOpen) cm.IsOpen = false;
                Dispatcher.BeginInvoke(DispatcherPriority.Background, (Action)delegate { MenuAction(p, o); });
            };
            cm.Items.Add(mi);
        }

        private Separator MakeSeparator()
        {
            Separator sp = new Separator();
            sp.Style = (Style)root.Resources["CtxSep"];
            return sp;
        }

        private void MenuAction(int page, string op)
        {
            if (op == "refresh") { RefreshPage(page); return; }
            if (op == "copy") { CopySelection(page); return; }
            // 列表正在后台重读：此刻执行操作作用在旧数据上，刷新结束后结果还会被新数据覆盖，
            // 排队机制能救回刷新却救不回操作语义，直接拦下最稳妥
            if (loading) { SetStatus("正在读取系统项目，请稍候再执行操作"); return; }
            if (op == "ai") { AiAnalyze(page); return; }
            InvokeOperation(page, op);
        }

        // 服务页 AI 分析的提问模板（--aitest 复用同一份，确保自检验证的就是真实提示词）
        internal static string BuildServicePrompt(ServiceItem it)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("请分析下面这个 Windows 服务，并在结论中明确给出启动类型建议：" +
                          "保持自动（开机自启）、改为手动（不随开机启动，需要时再启动）还是禁用（完全禁止启动）。");
            sb.AppendLine("显示名称：" + it.Name);
            sb.AppendLine("服务名称：" + it.ServiceName);
            sb.AppendLine("运行状态：" + it.Status);
            sb.AppendLine("当前启动类型：" + it.StartType);
            sb.AppendLine("服务类型：" + it.ServiceType);
            sb.AppendLine("登录身份：" + it.Account);
            sb.AppendLine("可执行路径：" + it.Path);
            if (!string.IsNullOrEmpty(it.Advice))
                sb.AppendLine("程序内置参考：" + it.Advice +
                              (string.IsNullOrEmpty(it.AdviceNote) ? "" : "（" + it.AdviceNote + "）"));
            return sb.ToString();
        }

        // 单条 AI 分析的提问模板：AiAnalyze（单条）与 UpdateAiAnalysis（逐项）共用
        internal static string BuildAnalysisPrompt(int page, object item, out string subject)
        {
            StringBuilder sb = new StringBuilder();
            if (page == 0)
            {
                StartupItem it = (StartupItem)item;
                subject = it.Name;
                sb.AppendLine("请分析下面这个 Windows 启动项，并在结论中明确给出处理方式：" +
                              "保持启用还是禁用（启动项只有启用和禁用两种处理，没有“手动”）。");
                sb.AppendLine("名称：" + it.Name);
                sb.AppendLine("状态：" + it.Status);
                sb.AppendLine("位置类型：" + it.Location);
                sb.AppendLine("作用范围：" + it.Scope);
                sb.AppendLine("启动命令：" + it.Command);
            }
            else if (page == 1)
            {
                ServiceItem it = (ServiceItem)item;
                subject = it.Name;
                sb.Append(BuildServicePrompt(it));
            }
            else if (page == 2)
            {
                TaskItem it = (TaskItem)item;
                subject = it.Name;
                sb.AppendLine("请分析下面这个 Windows 计划任务，并在结论中明确给出处理方式：" +
                              "保持启用还是禁用（计划任务只有启用和禁用两种处理，没有“手动”）。");
                sb.AppendLine("任务名称：" + it.Name);
                sb.AppendLine("状态：" + it.Status);
                sb.AppendLine("触发器：" + it.Trigger);
                sb.AppendLine("执行操作：" + it.Action);
                sb.AppendLine("任务路径：" + it.TaskPath);
                sb.AppendLine("上次运行：" + it.LastRun + "，上次结果：" + it.LastResult);
            }
            else
            {
                CtxMenuItem it = (CtxMenuItem)item;
                subject = it.Name;
                sb.AppendLine("请分析下面这个 Windows 资源管理器右键菜单项，并在结论中明确给出处理方式：" +
                              "保持启用还是禁用（右键菜单项只有启用和禁用两种处理，没有“手动”）。");
                sb.AppendLine("名称：" + it.Name);
                sb.AppendLine("类型：" + it.Kind);
                sb.AppendLine("状态：" + it.Status);
                sb.AppendLine("位置：" + it.Location);
                sb.AppendLine("作用域：" + it.Scope);
                sb.AppendLine("标识：" + it.Id);
                sb.AppendLine("处理程序：" + it.Handler);
                sb.AppendLine("注册表：" + it.RegPath);
            }
            sb.Insert(0, "本机系统：" + OsInfo.Describe() + Environment.NewLine);
            return sb.ToString();
        }

        // 首次调用云端 AI 前的一次性告知：提示词里包含本机路径与用户名。
        // 标记按端点区分：换成另一个接口地址时必须重新确认，否则数据会被静默发往
        // 一个用户从未同意过的地址
        private bool ConfirmAiPrivacy(AiConfig cfg)
        {
            string mark = "ai.privacy." + EndpointTag(cfg.Endpoint);
            if (AppDb.GetSetting(mark) == "1") return true;
            bool ok = Dialogs.Confirm(this,
                "AI 分析需要把本机信息发送到你配置的接口：\n\n" + cfg.Endpoint + "\n\n" +
                "发送内容：启动项名称与命令行、服务名称与可执行文件路径、计划任务名称与命令行、" +
                "右键菜单项的注册表路径。这些内容可能包含 Windows 用户名、安装目录等信息。\n\n" +
                "确定继续吗？（同一接口地址只提示一次，更换接口后会重新确认）",
                "同意并继续");
            if (ok) AppDb.SetSetting(mark, "1");
            return ok;
        }

        // 端点指纹：只存 8 位哈希，避免把可能带查询参数的完整地址写进设置表
        private static string EndpointTag(string url)
        {
            string s = (url == null ? "" : url.Trim().ToLowerInvariant());
            uint h = 2166136261u;
            for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 16777619u; }
            return h.ToString("x8");
        }

        private void AiAnalyze(int page)
        {
            if (quitting || closed) return;
            // 加载中列表还是旧快照：此刻分析会把旧数据发给模型并缓存，与刷新结果错位
            if (loading) { SetStatus("正在读取系统项目，请稍候再执行操作"); return; }
            // 批量任务进行中再发单条分析会同时写 analysis / usage 两张表，
            // 3 秒 busy_timeout 用尽就会报 database is locked，也会多花一份 Token
            if (aiBusy) { SetStatus("已有 AI 任务正在进行，请等它结束后再做单条分析"); return; }
            List<object> sel = grids[page].SelectedItems.Cast<object>().ToList();
            if (sel.Count != 1)
            {
                SetStatus("AI 分析需要先选中单个项目");
                return;
            }

            string loadErr;
            AiConfig cfg = AiConfig.Load(out loadErr);
            if (loadErr != null)
            {
                // 读失败与「从未配置」必须区分：后者会打开配置窗并整表保存，
                // 在读取失败的情形下这样保存会把其余几套配置与 Key 一起抹掉
                SetStatus("读取 AI 配置失败，已取消：" + loadErr);
                Dialogs.Info(this, "读取 AI 配置失败",
                    "无法读取已保存的配置：" + loadErr +
                    "\n\n为避免覆盖库中已有的配置，本次操作已取消。\n请确认数据库文件可读：" + AppDb.FilePath);
                return;
            }
            if (!cfg.Configured)
            {
                SetStatus("尚未配置 AI 接口，已打开配置窗口");
                OpenAiSettings();
                cfg = AiConfig.Load(out loadErr);
                if (loadErr != null || !cfg.Configured) return;
            }

            string subject;
            string prompt = BuildAnalysisPrompt(page, sel[0], out subject);
            if (!ConfirmAiPrivacy(cfg)) return;
            // 页面各自一套提示词与建议标题：服务有「手动」，其余只有「启用 / 禁用」
            string heading = page == 1 ? "处理建议（启用 / 手动 / 禁用）" : "处理建议（启用 / 禁用）";
            string itemKey = AiAnalysisStore.KeyFor(page, sel[0]);
            AiResultWindow w = new AiResultWindow(subject, page, itemKey, prompt, cfg,
                                                  AiClient.SystemPromptFor(page), heading);
            w.Owner = this;
            w.ShowDialog();
        }

        private void WireAiAdvice(string name, int page)
        {
            Button b = Find<Button>(name);
            if (b == null) return;
            b.Click += delegate { UpdateAiAdvice(page, b); };
        }

        // 单行文本被裁剪（TextTrimming 生效）时才放行默认 ToolTip，未截断的取消气泡。
        // 用 FormattedText 按实际 DPI 测量文本完整宽度，与渲染宽度比较
        private static void TrimmedToolTipOnly(object sender, ToolTipEventArgs e)
        {
            TextBlock tb = sender as TextBlock;
            if (tb == null || string.IsNullOrEmpty(tb.Text)) return;
            if (tb.TextTrimming == TextTrimming.None || tb.ActualWidth <= 0) return;
            FormattedText ft = new FormattedText(tb.Text,
                System.Globalization.CultureInfo.CurrentUICulture, tb.FlowDirection,
                new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch),
                tb.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(tb).PixelsPerDip);
            if (ft.Width > tb.ActualWidth) return;   // 确实被裁剪，保留气泡
            e.Handled = true;
        }

        // 把整页项目一次性发给云端 AI，结果写入 PCToolsAI.sqlite，然后刷新列表
        private void UpdateAiAdvice(int page, Button trigger)
        {
            if (quitting || closed) return;
            // 加载中列表还是旧快照：此刻分析会把旧数据发给模型并缓存，与刷新结果错位
            if (loading) { SetStatus("正在读取系统项目，请稍候再执行操作"); return; }
            if (aiBusy) { SetStatus("已有 AI 批量任务正在进行，请等它结束"); return; }
            if (page < 0 || page > 2) return;

            // 与「更新AI分析」保持同一口径：都以当前可见（可能已被搜索筛选）的行为准，
            // 否则同一个按钮在两个地方代表不同的数据范围，用户无法预期花多少 Token
            List<object> items = grids[page].Items.Cast<object>().ToList();
            if (items.Count == 0) { SetStatus("当前列表为空，无需更新 AI 建议"); return; }
            bool filtered = search.Text.Trim().Length > 0;
            string scopeNote = filtered ? "（当前为筛选结果）" : "";

            string loadErr;
            AiConfig cfg = AiConfig.Load(out loadErr);
            if (loadErr != null)
            {
                // 读失败与「从未配置」必须区分：后者会打开配置窗并整表保存，
                // 在读取失败的情形下这样保存会把其余几套配置与 Key 一起抹掉
                SetStatus("读取 AI 配置失败，已取消：" + loadErr);
                Dialogs.Info(this, "读取 AI 配置失败",
                    "无法读取已保存的配置：" + loadErr +
                    "\n\n为避免覆盖库中已有的配置，本次操作已取消。\n请确认数据库文件可读：" + AppDb.FilePath);
                return;
            }
            if (!cfg.Configured)
            {
                SetStatus("尚未配置 AI 接口，已打开配置窗口");
                OpenAiSettings();
                cfg = AiConfig.Load(out loadErr);
                if (loadErr != null || !cfg.Configured) return;
            }
            if (!ConfirmAiPrivacy(cfg)) return;

            string system = AiBatch.System(page);
            string kind = page == 0 ? AiAdviceStore.KindStartup
                        : (page == 1 ? AiAdviceStore.KindService : AiAdviceStore.KindTask);

            int total = items.Count;
            bool ok = false;
            bool cancelled = false;
            int saved = 0;
            int skippedTotal = 0;
            int failedBatches = 0;
            int missingTotal = 0;          // 模型少返回的行数（截断或漏行）
            int discardedTotal = 0;        // 模型返回了但标签非法、被丢弃的行数
            bool truncated = false;        // finish_reason == length
            string firstFail = null;
            int inTok = 0;
            int outTok = 0;
            int usageCalls = 0;
            bool hasUsage = false;
            string error = null;
            string warning = null;         // 部分成功但结果不完整的说明（截断 / 少返回）

            aiBusy = true;
            if (trigger != null) trigger.IsEnabled = false;
            ProgressWindow pw = new ProgressWindow("更新 AI 建议", total);
            // 取消时真正中止在途请求：否则窗口一直挂着等接口返回，用户以为点了没反应
            HttpWebRequest[] holder = new HttpWebRequest[1];
            pw.OnCancel = delegate
            {
                HttpWebRequest r = holder[0];
                if (r != null) { try { r.Abort(); } catch { } }
            };
            pw.Owner = this;
            aiProgress = pw;
            pw.Closed += delegate
            {
                if (aiProgress == pw) aiProgress = null;
                // 窗口关闭时直接弹模态框会踩「Owner 已关闭」，延后一轮消息循环再收尾。
                // 走 Ui()：应用退出、Dispatcher 停摆时静默放弃，不抛未处理异常
                Ui((Action)delegate
                {
                    FinishAdviceBatch(page, trigger, total, skippedTotal, ok, saved, error, warning);
                });
            };

            Thread worker = new Thread(delegate()
            {
                try
                {
                    Ui((Action)delegate
                    {
                        pw.Report(0, total, "正在把 " + total + " 项" + scopeNote + "发送给 " + cfg.Model + " …");
                    });

                    // 整页一次发出去会超出模型上下文（服务页可达 290 项 / 20 KB+），
                    // 网关要么直接 400，要么把输出截断成前半部分。按字符预算切批，逐批请求。
                    // 切分与提示词构建都要读注册表，必须放在后台线程做
                    ServiceScope svcScope = page == 1 ? new ServiceScope(items) : null;
                    List<List<object>> chunks = new List<List<object>>();
                    AddChunks(page, items, 0, chunks, svcScope);

                    // 逐批「请求 → 解析 → 落库」：以前所有批次攒在内存里、循环结束后才写一次，
                    // 第 2 批之后任何一次失败都会让前面已经花掉 Token 的结果全部丢失
                    int done = 0;
                    // 累计「已落库的 键→标签」与待回填的别名：模板与实例被切到不同批时，
                    // 别名目标可能在前面或后面的批次里，必须等目标落库后再补一次
                    Dictionary<string, string> known = new Dictionary<string, string>();
                    Dictionary<string, string> pendingAlias = new Dictionary<string, string>();
                    // 别名在整页范围内已知（ServiceScope 已算好）：即使某个实例所在的批
                    // 全是被归并的实例、没有任何要发送的行，也能在模板落库后补上
                    if (svcScope != null)
                        foreach (KeyValuePair<string, string> kv in svcScope.AliasTo) pendingAlias[kv.Key] = kv.Value;
                    DateTime deadline = DateTime.UtcNow.AddMinutes(BatchTotalMinutes);
                    for (int ci = 0; ci < chunks.Count; ci++)
                    {
                        if (pw.Cancelled) { cancelled = true; break; }
                        if (DateTime.UtcNow > deadline)
                        {
                            failedBatches += chunks.Count - ci;
                            if (firstFail == null)
                                firstFail = "已达单次总时长上限（" + BatchTotalMinutes + " 分钟），剩余 " +
                                            (chunks.Count - ci) + " 批未发送";
                            break;
                        }
                        List<object> chunk = chunks[ci];
                        BatchPlan plan = new BatchPlan();
                        // 实例归并按整页 scope 计算：切批不得改变「谁是模板、谁是代表」
                        string prompt = AiBatch.Build(page, chunk, plan, svcScope);
                        // 整批都是「被归并的每用户实例」时没有任何行要发送：直接跳过，
                        // 否则会发一个只有表头的空清单，白花一次请求
                        if (plan.Sent == 0)
                        {
                            done += chunk.Count;
                            int skipDone = done;
                            Ui((Action)delegate { pw.Report(skipDone, total, "已完成 " + skipDone + " 项…"); });
                            continue;
                        }
                        int ciCopy = ci;
                        if (chunks.Count > 1)
                        {
                            Ui((Action)delegate
                            {
                                pw.Report(done, total, "第 " + (ciCopy + 1) + " / " + chunks.Count +
                                                        " 批（" + chunk.Count + " 项）…");
                            });
                        }

                        Dictionary<string, string> part;
                        try
                        {
                            AiReply reply = AiClient.AskReply(cfg, prompt, system, delegate(HttpWebRequest req)
                            {
                                holder[0] = req;
                                if (pw.Cancelled) { try { req.Abort(); } catch { } }
                            }, chunks.Count > 1 ? 180000 : 300000);

                            if (reply.HasUsage)
                            {
                                inTok += reply.InTokens;
                                outTok += reply.OutTokens;
                                hasUsage = true;
                                usageCalls++;
                            }
                            if (pw.Cancelled) { cancelled = true; break; }

                            int discarded;
                            int validSent;
                            part = AiBatch.Parse(reply.Text, plan, page, out discarded, out validSent);
                            discardedTotal += discarded;

                            // 输出被截断时 Parse 只能拿到前半部分：以前照样算成功，
                            // 状态栏写「已更新 N 项」，其余项目静默为空。
                            // 缺口只按「已发送项里真正拿到合法结论的数量」算：别名回填和
                            // 非法行都属于另一类统计，不能在这里抵扣
                            if (string.Equals(reply.Finish, "length", StringComparison.OrdinalIgnoreCase))
                                truncated = true;
                            int unreturned = plan.Sent - validSent;
                            if (unreturned > 0) missingTotal += unreturned;

                            // 记录本批别名，并先用「之前批次已落库」的模板结论补全
                            foreach (KeyValuePair<string, string> kv in plan.Aliases)
                            {
                                pendingAlias[kv.Key] = kv.Value;
                                if (part.ContainsKey(kv.Key)) continue;
                                string v;
                                if (known.TryGetValue(kv.Value, out v)) part[kv.Key] = v;
                            }
                        }
                        catch (Exception ex)
                        {
                            // 单批失败不影响其它批：已成功落库的部分保留，失败批次计入统计
                            if (pw.Cancelled) { cancelled = true; break; }
                            failedBatches++;
                            if (firstFail == null) firstFail = ex.Message;
                            done += chunk.Count;
                            int doneCopy = done;
                            Ui((Action)delegate { pw.Report(doneCopy, total, "已完成 " + doneCopy + " 项…"); });
                            continue;
                        }

                        if (part.Count == 0)
                        {
                            failedBatches++;
                            if (firstFail == null) firstFail = "第 " + (ci + 1) + " 批未返回可识别结果";
                        }
                        else
                        {
                            // 立即写入：中途取消或后续批次失败时，这一批的 Token 不会白花
                            string err;
                            if (AiAdviceStore.Save(kind, part, out err))
                            {
                                ok = true;
                                saved += part.Count;
                                foreach (KeyValuePair<string, string> kv in part) known[kv.Key] = kv.Value;
                            }
                            else
                            {
                                failedBatches++;
                                if (firstFail == null) firstFail = "写入数据库失败：" + err;
                            }
                        }
                        done += chunk.Count;
                        int doneCopy2 = done;
                        Ui((Action)delegate { pw.Report(doneCopy2, total, "已完成 " + doneCopy2 + " 项…"); });
                    }

                    // 后置别名回填：实例所在批先于模板批时，等模板落库后再补写一次
                    if (pendingAlias.Count > 0)
                    {
                        Dictionary<string, string> late = new Dictionary<string, string>();
                        foreach (KeyValuePair<string, string> kv in pendingAlias)
                        {
                            if (known.ContainsKey(kv.Key)) continue;
                            string v;
                            if (known.TryGetValue(kv.Value, out v)) late[kv.Key] = v;
                        }
                        if (late.Count > 0)
                        {
                            string lateErr;
                            if (AiAdviceStore.Save(kind, late, out lateErr))
                            {
                                ok = true;
                                saved += late.Count;
                            }
                            else if (firstFail == null) firstFail = "实例建议回填失败：" + lateErr;
                        }
                    }

                    // 只统计真正拿到结论的别名：模板批失败时不能仍提示「N 项按模板回填」
                    if (svcScope != null)
                        foreach (KeyValuePair<string, string> kv in svcScope.AliasTo)
                            if (known.ContainsKey(kv.Key)) skippedTotal++;

                    if (usageCalls > 0) AiUsageStore.Add(cfg.Index, cfg.Model, inTok, outTok, usageCalls);

                    if (failedBatches > 0)
                    {
                        string head = "共 " + chunks.Count + " 批，其中 " + failedBatches + " 批未完成";
                        error = firstFail == null ? head : head + "：" + firstFail;
                    }
                    else if (cancelled) error = "已取消：其余批次未发送";
                    if (!ok && error == null)
                        error = cancelled ? "已取消：本次结果未写入" : "AI 未返回可识别的结果";

                    // 少返回的项单独提示：它们不计入失败批次（那一批确实返回了部分结果），
                    // 但用户需要知道这一列还有多少是空的
                    List<string> warns = new List<string>();
                    if (missingTotal > 0 || truncated)
                        warns.Add(truncated
                            ? "输出被截断（达到 max_tokens 上限），有 " + missingTotal + " 项未返回"
                            : "有 " + missingTotal + " 项模型未返回");
                    if (discardedTotal > 0)
                        warns.Add("模型返回 " + discardedTotal + " 行非法标签，已忽略");
                    if (warns.Count > 0) warning = string.Join("；", warns.ToArray());
                }
                catch (Exception ex)
                {
                    // 取消时我们主动 Abort，接口会抛「操作已取消」；这不该当成失败弹窗。
                    // 已经记录过「部分批次失败」时保留原消息，别被收尾阶段的异常盖掉
                    if (error == null)
                        error = pw.Cancelled ? "已取消：本次结果未写入" : ex.Message;
                    if (pw.Cancelled) cancelled = true;
                }
                finally
                {
                    bool cFinal = cancelled;
                    bool uFinal = hasUsage;
                    int inFinal = inTok;
                    int outFinal = outTok;
                    bool okFinal = ok;
                    int savedFinal = saved;
                    string errFinal = error;
                    string warnFinal = warning;
                    Ui((Action)delegate
                    {
                        if (okFinal)
                        {
                            // 部分批次失败 / 中途取消时也要说清楚：只报「已更新 N 项」
                            // 会让用户以为整页都成功了
                            string summary;
                            if (cFinal) summary = "已取消：已保存 " + savedFinal + " 项（共 " + total + " 项）";
                            else if (errFinal == null) summary = "已更新 " + savedFinal + " 项（共 " + total + " 项）";
                            else summary = "已更新 " + savedFinal + " 项（共 " + total + " 项）；" + errFinal;
                            if (warnFinal != null) summary += "\n" + warnFinal;
                            summary += uFinal
                                ? "\n本次消耗　输入：" + inFinal.ToString("N0") + " Token / 输出：" + outFinal.ToString("N0") + " Token"
                                : "\n接口未返回用量";
                            pw.FinishKeepOpen(summary);
                        }
                        else if (cFinal)
                        {
                            string summary = "已取消：本次结果未写入（Token 已消耗）";
                            summary += uFinal
                                ? "\n本次消耗　输入：" + inFinal.ToString("N0") + " Token / 输出：" + outFinal.ToString("N0") + " Token"
                                : "\n接口未返回用量";
                            pw.FinishKeepOpen(summary);
                        }
                        else pw.Finish();
                    });
                }
            });
            worker.IsBackground = true;
            worker.Start();
            pw.Show();                     // 非模态：主窗口仍可继续操作
        }

        // 单个请求的提示词字符预算。按最保守的 8K 上下文端点反推：
        // 中文约 1–1.5 字符/token，4000 字符 ≈ 3–4k token，加上系统提示（约 1k）
        // 与输出配额（3072）仍在 8K 以内。早期按 12000 字符切分会直接 400 或截断
        private const int BatchCharBudget = 4000;

        // 单次「更新AI建议」的总时长上限：批数 × 单批超时最坏能拖到半小时，
        // 期间界面只能靠用户手动取消，这里给一个硬边界
        private const int BatchTotalMinutes = 12;

        // 贪心二分：整页装得下就一批，装不下就对半切，最多切 4 层（16 批）
        private static void AddChunks(int page, List<object> items, int depth, List<List<object>> outp,
                                      ServiceScope scope)
        {
            if (items.Count == 0) return;
            // 只剩一项就不再切：单条本身超预算时无法再拆，只能原样发送
            if (items.Count <= 1) { outp.Add(items); return; }
            // 到最深一层（16 批）不再无条件放行：quick 估算很便宜（不读注册表），
            // 超预算的段继续对半拆，避免最后一批悄悄超过 4000 字符被网关 400/截断。
            // 极端情况下批数可能超过 16，但失败的请求同样在浪费 Token，宁多勿超
            BatchPlan probe = new BatchPlan();
            // 长度估算不需要精确查服务类型：用 quick 模式避免每层都对注册表读一遍
            string s = AiBatch.Build(page, items, probe, scope, true);
            if (s.Length <= BatchCharBudget)
            {
                outp.Add(items);
                return;
            }
            int half = items.Count / 2;
            AddChunks(page, items.GetRange(0, half), depth + 1, outp, scope);
            AddChunks(page, items.GetRange(half, items.Count - half), depth + 1, outp, scope);
        }

        // 「更新 AI 建议」进度窗关闭后的收尾
        private void FinishAdviceBatch(int page, Button trigger, int total, int skipped,
                                       bool ok, int saved, string error, string warning)
        {
            if (quitting || closed) return;
            aiBusy = false;
            if (trigger != null) trigger.IsEnabled = true;
            ApplyFilter();

            if (!ok)
            {
                if (error == null) error = "未知原因";
                if (error.StartsWith("已取消", StringComparison.Ordinal))
                {
                    SetStatus(error);
                    return;
                }
                SetStatus("更新 AI 建议失败：" + error);
                Dialogs.Info(this, "更新 AI 建议失败", error);
                return;
            }
            RefreshPage(page);
            // 部分批次失败时把原因带进状态栏：否则只剩「已更新 N 项」，用户不知道
            // 剩下的项目为什么还是空的
            SetStatusAfterLoad("AI 建议已更新：" + saved + " 项（共 " + total + " 项）" +
                (skipped > 0 ? "，其中 " + skipped + " 项每用户实例按模板回填" : "") +
                (string.IsNullOrEmpty(error) ? "" : "；" + error) +
                (string.IsNullOrEmpty(warning) ? "" : "；" + warning));
        }

        // 逐项请求完整分析（项目介绍 + 处理建议）：进度按「请求成功数」推进，
        // 完成后窗口保留并显示本次 Token 与成败数；取消时已完成的项目保留
        private void UpdateAiAnalysis(int page, Button trigger)
        {
            if (quitting || closed) return;
            // 加载中列表还是旧快照：此刻分析会把旧数据发给模型并缓存，与刷新结果错位
            if (loading) { SetStatus("正在读取系统项目，请稍候再执行操作"); return; }
            if (aiBusy) { SetStatus("已有 AI 分析任务正在进行，请等待完成"); return; }

            List<object> items = grids[page].Items.Cast<object>().ToList();
            if (items.Count == 0) { SetStatus("当前列表为空，无需更新 AI 分析"); return; }

            string loadErr;
            AiConfig cfg = AiConfig.Load(out loadErr);
            if (loadErr != null)
            {
                // 读失败与「从未配置」必须区分：后者会打开配置窗并整表保存，
                // 在读取失败的情形下这样保存会把其余几套配置与 Key 一起抹掉
                SetStatus("读取 AI 配置失败，已取消：" + loadErr);
                Dialogs.Info(this, "读取 AI 配置失败",
                    "无法读取已保存的配置：" + loadErr +
                    "\n\n为避免覆盖库中已有的配置，本次操作已取消。\n请确认数据库文件可读：" + AppDb.FilePath);
                return;
            }
            if (!cfg.Configured)
            {
                SetStatus("尚未配置 AI 接口，已打开配置窗口");
                OpenAiSettings();
                cfg = AiConfig.Load(out loadErr);
                if (loadErr != null || !cfg.Configured) return;
            }
            if (!ConfirmAiPrivacy(cfg)) return;

            // 一次查询算出缺口，不按项目逐个开库
            string keyErr;
            HashSet<string> have = AiAnalysisStore.KeySet(AiAnalysisStore.KindFor(page), out keyErr);
            if (keyErr != null)
            {
                SetStatus("读取分析缓存失败，已取消：" + keyErr);
                return;
            }
            int total = items.Count;
            int missing = 0;
            foreach (object o in items)
                if (!have.Contains(AiAnalysisStore.KeyFor(page, o))) missing++;
            int existing = total - missing;

            bool filtered = search.Text.Trim().Length > 0;
            string subtitle = missing == 0
                ? "这 " + total + " 项都已有分析记录"
                : (existing == 0 ? "这 " + total + " 项都还没有分析记录"
                                 : "已有 " + existing + " 项，待补全 " + missing + " 项");
            string body =
                "当前列表共 " + total + " 个项目" + (filtered ? "（当前为筛选结果）" : "") + "。\n\n" +
                "· 开始分析：对全部 " + total + " 项逐项重新请求，已有结论的项目也会重新请求，" +
                "Token 消耗约为单条分析的 " + total + " 倍。\n" +
                "· 补全分析：只请求还没有分析记录的 " + missing + " 项，已有记录的直接跳过。\n\n" +
                "分析过程中可以随时取消，已完成的项目会保留；进度窗不会挡住主界面。";

            // missing=0 时「补全分析（0 项）」不该还能点：退回两按钮形态
            ConfirmChoice ch = Dialogs.Confirm3(this, subtitle, body,
                "开始分析", missing == 0 ? null : "补全分析（" + missing + " 项）");
            if (ch != ConfirmChoice.Primary && ch != ConfirmChoice.Third) return;

            if (ch == ConfirmChoice.Primary)
            {
                RunAnalysisBatch(page, items, cfg, trigger, null, total);
                return;
            }

            // 补全分析：只挑库里没有记录的项目
            if (missing == 0)
            {
                SetStatus("这 " + total + " 项都已有分析记录，无需补全");
                return;
            }
            List<object> targets = new List<object>();
            foreach (object o in items)
                if (!have.Contains(AiAnalysisStore.KeyFor(page, o))) targets.Add(o);
            RunAnalysisBatch(page, targets, cfg, trigger,
                "补全 " + targets.Count + " 项 / 共 " + total + " 项", total);
        }

        // 单个项目的完整分析：请求 → 解析 → 取出结论标签。
        // 数据库写入（analysis / usage）由调用方批量收尾时统一做，这里不逐项开库
        private static AnalyzeResult AnalyzeOne(int page, object item, AiConfig cfg, Action<HttpWebRequest> onRequest)
        {
            AnalyzeResult r = new AnalyzeResult();
            try
            {
                string subject;
                string prompt = BuildAnalysisPrompt(page, item, out subject);
                r.Subject = subject;
                AiReply reply = AiClient.AskReply(cfg, prompt, AiClient.SystemPromptFor(page), onRequest, 120000);
                r.InTokens = reply.InTokens;
                r.OutTokens = reply.OutTokens;
                r.HasUsage = reply.HasUsage;
                if (!reply.Parsed) { r.Error = "接口返回无法解析"; return r; }

                string intro, tips;
                AiResultWindow.Split(reply.Text, out intro, out tips);
                r.Intro = intro;
                r.Advice = tips;
                // 模型只回【介绍】、或输出被 max_tokens 截断时，tips 会是空串。
                // 以前这种结果照样 Ok=true 并入缓存，列表上「建议」永远空白，
                // 而「补全分析」又因为库里已有行把它当成已完成，再也补不上
                if (tips.Length == 0)
                {
                    r.Error = "接口未返回【建议】段（可能被截断），本次不写入缓存";
                    return r;
                }
                string tag, rest;
                if (AiResultWindow.TryTag(tips, out tag, out rest)) r.Tag = tag;
                r.Ok = true;
                return r;
            }
            catch (Exception ex)
            {
                r.Error = ex.Message;
                return r;
            }
        }

#if DIAG
        // 自检入口（AnalyzeForTest 用，发布版不编译）
        internal void RunAnalysisBatch(int page, List<object> items, AiConfig cfg, Button trigger)
        {
            RunAnalysisBatch(page, items, cfg, trigger, null, items == null ? 0 : items.Count);
        }
#endif

        // scope 非 null 表示「补全分析」：摘要首行写明补全几项、总数多少
        internal void RunAnalysisBatch(int page, List<object> items, AiConfig cfg, Button trigger,
                                       string scope, int totalAll)
        {
            if (items == null || items.Count == 0) return;
            aiBusy = true;
            // 与「更新AI建议」一致：进度窗非模态期间按钮先置灰，
            // 收尾时 aiBusy=false 后由 UpdateListSummary 按列表内容恢复
            if (trigger != null) trigger.IsEnabled = false;

            BatchOutcome outcome = new BatchOutcome();
            outcome.Scope = scope;
            // 右键菜单页没有「AI建议」列，只写分析缓存
            string adviceKind = page == 0 ? AiAdviceStore.KindStartup
                              : (page == 1 ? AiAdviceStore.KindService
                              : (page == 2 ? AiAdviceStore.KindTask : null));

            Dictionary<string, string> labels = new Dictionary<string, string>();
            int total = items.Count;
            ProgressWindow pw = new ProgressWindow(scope == null ? "更新 AI 分析" : "补全 AI 分析", total);
            // 取消时中止当前这条请求：否则「取消」要等接口超时（最长 120 秒）才收手
            HttpWebRequest[] holder = new HttpWebRequest[1];
            pw.OnCancel = delegate
            {
                HttpWebRequest r = holder[0];
                if (r != null) { try { r.Abort(); } catch { } }
            };
            pw.Owner = this;
            aiProgress = pw;
            pw.Closed += delegate
            {
                if (aiProgress == pw) aiProgress = null;
                // 窗口关闭时直接弹模态框会踩「Owner 已关闭」，延后一轮消息循环再收尾。
                // 走 Ui()：应用退出、Dispatcher 停摆时静默放弃
                Ui((Action)delegate { FinishAnalysisBatch(page, outcome, labels); });
            };

            Thread worker = new Thread(delegate()
            {
                int inTok = 0;
                int outTok = 0;
                bool hasUsage = false;
                int usageCalls = 0;
                // 分析缓存批量收尾写入：逐项写库是每项一次开库一次事务，N 项太浪费
                List<AiAnalysisStore.SaveItem> saveItems = new List<AiAnalysisStore.SaveItem>();
                try
                {
                    for (int i = 0; i < items.Count; i++)
                    {
                        if (pw.Cancelled) { outcome.Cancelled = true; break; }
                        object item = items[i];
                        string name = "第 " + (i + 1) + " 项";
                        try
                        {
                            AnalyzeResult r = AnalyzeOne(page, item, cfg, delegate(HttpWebRequest req)
                            {
                                holder[0] = req;
                                if (pw.Cancelled) { try { req.Abort(); } catch { } }
                            });
                            if (r.Subject.Length > 0) name = r.Subject;
                            if (r.HasUsage)
                            {
                                inTok += r.InTokens;
                                outTok += r.OutTokens;
                                hasUsage = true;
                                usageCalls++;
                            }
                            // 取消导致的 Abort 会落成一条失败记录，这里按「已取消」处理，不污染失败统计
                            if (!r.Ok && pw.Cancelled) { outcome.Cancelled = true; break; }
                            if (!r.Ok) outcome.Fails.Add("· " + name + "：" + r.Error);
                            else
                            {
                                outcome.Ok++;
                                AiAnalysisStore.SaveItem si = new AiAnalysisStore.SaveItem();
                                si.Key = AiAnalysisStore.KeyFor(page, item);
                                si.Intro = r.Intro;
                                si.Advice = r.Advice;
                                si.InTokens = r.InTokens;
                                si.OutTokens = r.OutTokens;
                                saveItems.Add(si);
                                if (adviceKind != null && r.Tag.Length > 0)
                                    labels[AiAnalysisStore.KeyFor(page, item)] = r.Tag;
                            }
                        }
                        catch (Exception ex)
                        {
                            if (pw.Cancelled) { outcome.Cancelled = true; break; }
                            outcome.Fails.Add("· " + name + "：" + ex.Message);
                        }

                        // 闭包必须捕获新的局部变量；Token 也要快照，worker 线程仍在累加。
                        // 进度按「已处理」推进：用成功数的话，连续失败时进度条会一直停在原地
                        int done = i + 1;
                        string cur = name;
                        int inSnap = inTok;
                        int outSnap = outTok;
                        bool hasSnap = hasUsage;
                        Ui((Action)delegate
                        {
                            pw.Report(done, total, cur, inSnap, outSnap, hasSnap);
                        });
                    }
                }
                finally
                {
                    // 分析缓存与用量都在收尾时一次写完（取消也保留已完成部分）
                    if (saveItems.Count > 0)
                    {
                        try
                        {
                            string e;
                            // 写库失败不是「某一项请求失败」：计入 Fails 会让「成功 + 失败」
                            // 超过总数。单独记录，在摘要里说明
                            if (!AiAnalysisStore.SaveBatch(AiAnalysisStore.KindFor(page), saveItems, cfg.Model, out e))
                                outcome.CacheError = e;
                        }
                        catch (Exception ex) { outcome.CacheError = ex.Message; }
                    }
                    if (hasUsage)
                    {
                        try { AiUsageStore.Add(cfg.Index, cfg.Model, inTok, outTok, usageCalls); }
                        catch (Exception ex) { DiagLog.Swallow("批量用量统计 " + page, ex); }
                    }
                    // 「AI建议」列只写一次：Save 内部是一次开库循环 INSERT
                    if (adviceKind != null && labels.Count > 0)
                    {
                        try
                        {
                            string e;
                            if (!AiAdviceStore.Save(adviceKind, labels, out e)) outcome.AdviceError = e;
                        }
                        catch (Exception ex) { outcome.AdviceError = ex.Message; }
                    }

                    // 失败计数必须在所有 Fails.Add 之后重算：收尾阶段的写库失败也要算进去，
                    // 否则进度窗写「失败 0 项」却附一行失败原因，收尾也不弹提示
                    outcome.Failed = outcome.Fails.Count;
                    bool usageFinal = hasUsage;
                    int inFinal = inTok;
                    int outFinal = outTok;
                    Ui((Action)delegate
                    {
                        string summary = string.IsNullOrEmpty(scope) ? "" : scope + "\n";
                        summary += "成功 " + outcome.Ok + " 项，失败 " + outcome.Failed +
                                   " 项（共 " + total + " 项）";
                        if (outcome.Cancelled)
                            summary += "\n已取消：其余 " +
                                Math.Max(0, total - outcome.Ok - outcome.Failed) + " 项未请求";
                        summary += usageFinal
                            ? "\n本次消耗　输入：" + inFinal.ToString("N0") + " Token / 输出：" + outFinal.ToString("N0") + " Token"
                            : "\n接口未返回用量";
                        if (outcome.AdviceError != null) summary += "\n「AI建议」列写入失败：" + outcome.AdviceError;
                        if (outcome.CacheError != null) summary += "\n分析缓存写入失败：" + outcome.CacheError;
                        pw.FinishKeepOpen(summary);
                    });
                }
            });
            worker.IsBackground = true;
            worker.Start();
            pw.Show();                     // 非模态：主窗口仍可继续操作，收尾在窗口关闭时做
        }

        // 逐项分析的结果汇总：worker 线程写，界面线程在进度窗关闭后读
        private sealed class BatchOutcome
        {
            internal int Ok;
            internal int Failed;
            internal bool Cancelled;
            internal string AdviceError;
            internal string CacheError;        // 分析缓存写库失败：不是某一项请求失败，不计入 Failed
            internal string Scope;             // 「补全 12 项 / 共 40 项」；普通分析为 null
            internal List<string> Fails = new List<string>();
        }

        // 进度窗关闭后的收尾：复位状态、回填「AI建议」列、报告失败项
        private void FinishAnalysisBatch(int page, BatchOutcome o, Dictionary<string, string> labels)
        {
            if (o == null) return;
            if (quitting || closed) return;    // 主窗口正在退出：不刷列表、不弹窗

            aiBusy = false;
            // 回填「AI建议」列：只重画本地数据，不重新枚举系统
            if (labels != null && labels.Count > 0 && o.AdviceError == null)
            {
                // 不再 FillX 重新开库：worker 已经把写的标签保存在 labels 里，
                // UI 线程只做内存回填，库忙时也不会卡住界面
                if (page == 0) AiAdviceStore.ApplyStartup(startupData, labels);
                else if (page == 1) AiAdviceStore.ApplyServices(serviceData, labels);
                else if (page == 2) AiAdviceStore.ApplyTasks(taskData, labels);
                RepaintItems();
            }
            // 内部会按 aiBusy 重算「更新AI分析」按钮的可用性
            ApplyFilter();

            if (o.Failed > 0 || o.CacheError != null)
            {
                StringBuilder msg = new StringBuilder();
                msg.Append("「更新 AI 分析」完成：成功 ").Append(o.Ok)
                   .Append(" 项，失败 ").Append(o.Failed).AppendLine(" 项。").AppendLine();
                int shown = Math.Min(8, o.Fails.Count);
                for (int i = 0; i < shown; i++) msg.AppendLine(o.Fails[i]);
                if (o.Fails.Count > shown) msg.AppendLine("…（其余 " + (o.Fails.Count - shown) + " 项略）");
                if (o.CacheError != null)
                    msg.AppendLine().AppendLine("分析缓存写入失败（本次结果可能没有全部保存）：").Append(o.CacheError);
                Dialogs.Info(this, "部分项目未完成", msg.ToString());
            }
            SetStatusAfterLoad((string.IsNullOrEmpty(o.Scope) ? "AI 分析已更新" : "AI 分析已补全") +
                               "：成功 " + o.Ok + " 项，失败 " + o.Failed + " 项" +
                               (o.CacheError != null ? "；缓存写入失败：" + o.CacheError : ""));
        }

        private void OpenAiSettings()
        {
            // 配置窗构造时自己 LoadAll(out loadError) 并在读失败时禁止保存，
            // 这里不需要先读一遍：传 null 只取「active 配置作为初始选中项」。
            // 以前的无参 Load() 读失败会拿到一份内存默认值，险些成为整表覆盖的入口
            AiSettingsWindow w = new AiSettingsWindow(null);
            w.Owner = this;
            w.ShowDialog();
            SetStatus(w.Saved ? "AI 配置已更新" : "未修改 AI 配置");
        }

        private void CopySelection(int page)
        {
            List<object> sel = grids[page].SelectedItems.Cast<object>().ToList();
            if (sel.Count == 0) return;
            StringBuilder sb = new StringBuilder();
            foreach (object o in sel)
            {
                if (page == 0)
                {
                    StartupItem it = (StartupItem)o;
                    sb.AppendLine(it.Name + "\t" + it.Command);
                }
                else if (page == 1)
                {
                    sb.AppendLine(((ServiceItem)o).ServiceName);
                }
                else if (page == 2)
                {
                    sb.AppendLine(((TaskItem)o).FullPath);
                }
                else
                {
                    CtxMenuItem it = (CtxMenuItem)o;
                    sb.AppendLine(it.Name + "\t" + it.Id);
                }
            }
            try
            {
                Clipboard.SetText(sb.ToString().TrimEnd());
                SetStatus("已复制 " + sel.Count + " 项到剪贴板");
            }
            catch (Exception ex) { SetStatus("复制失败：" + ex.Message); }
        }

        // 右键菜单、工具条、自检入口共用这一份判定，避免三处规则漂移
        private bool CanApply(int page, string op, List<object> sel)
        {
            if (sel == null || sel.Count == 0) return false;
            if (op == "ai") return sel.Count == 1;
            // 多选里只要混入一条 RunOnce，整批启用/禁用都不可能全部成功
            if (page == 0 && (op == "enable" || op == "disable") &&
                sel.Any(o => ((StartupItem)o).OneShot)) return false;
            if (sel.Count != 1) return true;
            object first = sel[0];
            if (page == 0)
            {
                StartupItem it = (StartupItem)first;
                if (op == "enable") return !it.OneShot && it.Status == "已禁用";
                if (op == "disable") return !it.OneShot && it.Status == "已启用";
            }
            else if (page == 1)
            {
                ServiceItem it = (ServiceItem)first;
                if (op == "enable") return it.StartType != "自动";
                if (op == "manual") return it.StartType != "手动";
                if (op == "disable") return it.StartType != "已禁用";
                if (op == "start") return it.Status == "已停止";
                if (op == "stop") return it.Status == "正在运行";
            }
            else if (page == 2)
            {
                TaskItem it = (TaskItem)first;
                if (op == "enable") return it.Status == "已禁用";
                if (op == "disable") return it.Status != "已禁用";
                if (op == "run") return it.Status != "正在运行";
                if (op == "stop") return it.Status == "正在运行";
            }
            else
            {
                CtxMenuItem it = (CtxMenuItem)first;
                if (op == "enable") return it.Status == "已禁用";
                if (op == "disable") return it.Status == "已启用";
                if (op == "disableAll") return it.Status == "已启用";
            }
            return true;
        }

        private void UpdateMenuState(int page, ContextMenu cm)
        {
            List<object> sel = grids[page].SelectedItems.Cast<object>().ToList();
            foreach (object o in cm.Items)
            {
                MenuItem mi = o as MenuItem;
                if (mi == null) continue;
                mi.IsEnabled = CanApply(page, mi.Tag as string, sel);
            }
        }

        private static T FindAncestor<T>(DependencyObject o) where T : DependencyObject
        {
            while (o != null)
            {
                T t = o as T;
                if (t != null) return t;
                o = VisualTreeHelper.GetParent(o);
            }
            return null;
        }

        private void Wire(string name, int page, string op)
        {
            Button b = Find<Button>(name);
            toolButtons.Add(b);
            toolPages[b] = page;
            toolOps[b] = op;
            b.Click += delegate { MenuAction(page, op); };   // 与右键菜单共用 loading 拦截
        }

        private void UpdateToolbarState()
        {
            foreach (Button b in toolButtons)
            {
                int page;
                string op;
                if (!toolPages.TryGetValue(b, out page) || !toolOps.TryGetValue(b, out op)) continue;
                List<object> sel = grids[page].SelectedItems.Cast<object>().ToList();
                b.IsEnabled = !loading && !quitting && !closed && CanApply(page, op, sel);
            }
        }

        private void WireRefresh(string name, int page)
        {
            Find<Button>(name).Click += delegate { RefreshPage(page); };
        }

#if DIAG
        // ---- 自检专用接口（--smoke / --probe 等诊断模式调用，发布版不编译进 exe）----
        public void ToggleNavForTest() { ToggleNav(); }

        public void SetThemeForTest(bool light) { SetTheme(light); }

        public void SetCtxTabForTest(int tab) { SetCtxTab(tab); }

        // 自检用：一条输出覆盖页签最容易错的三个点（过滤、选中态、页面可见性）
        public string CtxTabInfo()
        {
            int checkedIdx = -1;
            for (int i = 0; i < ctxTabs.Length; i++)
                if (ctxTabs[i] != null && ctxTabs[i].IsChecked == true) checkedIdx = i;
            return "tab=" + ctxTab + " checked=" + checkedIdx
                 + " rows=" + (grids[3].Items == null ? 0 : grids[3].Items.Count)
                 + " visible=" + (pages[3].Visibility == Visibility.Visible ? 1 : 0);
        }
#endif

        // 页签切换：重算过滤
        private void SetCtxTab(int tab)
        {
            if (tab == ctxTab) return;
            ctxTab = tab;
            // 回写单选钮保持选中态一致；触发 Checked -> SetCtxTab(同一值) 会在这里被拦下
            for (int i = 0; i < ctxTabs.Length; i++)
                if (ctxTabs[i] != null) ctxTabs[i].IsChecked = i == tab;
            ApplyFilter();
            if (current == 3)
                Dispatcher.BeginInvoke(new Action(delegate { RecomputeColumns(3); }), DispatcherPriority.Loaded);
            SetStatus("已切换到「" + CtxTabName(tab) + "」右键菜单");
        }

        // 右键页六个页签的显示名（状态栏文案用，必须与 XAML 单选钮一致）
        private static string CtxTabName(int tab)
        {
            switch (tab)
            {
                case 0: return "全局";
                case 1: return "桌面";
                case 2: return "驱动器";
                case 3: return "文件";
                case 4: return "文件夹";
                default: return "文件和文件夹";
            }
        }

        private void RestartExplorerFlow()
        {
            if (!Dialogs.Confirm(this,
                "确定要重启资源管理器（explorer.exe）吗？\n\n" +
                "任务栏与桌面会闪烁 1-2 秒，已打开的资源管理器窗口会关闭，未保存的窗口状态会丢失。",
                "重启资源管理器")) return;
            SetStatus("正在重启资源管理器…");
            bool elevated = isAdmin;
            Thread t = new Thread(delegate()
            {
                // 后台线程的未处理异常会让整个进程退出（AppDomain 兜底只能写日志），
                // 这里必须自捕：重启 explorer 本身就是按「可能失败」设计的场景
                try
                {
                    string err = Manager.RestartExplorer(elevated);
                    Ui((Action)delegate
                    {
                        if (err == null) { SetStatus("资源管理器已重启，右键菜单更改应已生效"); return; }
                        // 失败时桌面可能已经是空的，状态栏只有 34px 高、单行显示，
                        // 里面那段「按 Ctrl+Shift+Esc…」的恢复步骤会被裁掉，必须用弹窗给出
                        SetStatus("重启资源管理器未完成：" + err.Replace("\n", " "));
                        Dialogs.Info(this, "重启资源管理器未完成", err);
                    });
                }
                catch (Exception ex)
                {
                    DiagLog.Swallow("重启资源管理器", ex);
                    Ui((Action)delegate
                    {
                        SetStatus("重启资源管理器未完成：" + ex.Message);
                        Dialogs.Info(this, "重启资源管理器未完成",
                            ex.Message + "\n\n请手动重启资源管理器：按 Ctrl+Shift+Esc 打开任务管理器，"
                            + "找到「Windows 资源管理器」右键选择「重新启动」。");
                    });
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        // 深浅主题实时切换：刷新资源字典 -> 重涂状态色 -> 重新绑定列表
        private void SetTheme(bool light)
        {
            isLight = light;
            if (Application.Current != null)
            {
                ThemeManager.Apply(Application.Current, light);
                if (themeMirror != null)
                {
                    foreach (object key in Application.Current.Resources.Keys)
                        themeMirror[key] = Application.Current.Resources[key];
                }
            }
            RepaintItems();
            // 列表数据是同一个 List 实例，直接重设 ItemsSource 不会刷新绑定，先置空。
            // 重建前记住各页选中行与滚动位置：主题切换不该把用户的浏览状态清掉
            List<object>[] sel = new List<object>[grids.Length];
            double[] scrollPos = new double[grids.Length];
            for (int i = 0; i < grids.Length; i++)
            {
                if (grids[i] == null) continue;
                sel[i] = grids[i].SelectedItems.Cast<object>().ToList();
                ScrollViewer sv = ViewerOf(grids[i]);
                if (sv != null) scrollPos[i] = sv.VerticalOffset;
                grids[i].ItemsSource = null;
            }
            ApplyFilter();
            for (int i = 0; i < grids.Length; i++)
            {
                if (grids[i] == null || sel[i] == null || sel[i].Count == 0) continue;
                // 数据对象是同一批实例，按引用整组重选（以前只恢复第一项，多选会被悄悄吃掉）
                HashSet<object> alive = new HashSet<object>(grids[i].Items.Cast<object>());
                foreach (object o in sel[i])
                    if (alive.Contains(o)) grids[i].SelectedItems.Add(o);
            }
            UpdateThemeButton();
            ApplyDwmTheme();
            // 滚动位置等布局完成后再恢复：新 ItemsSource 的可视高度此时才确定。
            // 走 Ui()：应用退出、Dispatcher 停摆时静默放弃，不抛未处理异常；
            // 优先级仍保持 Loaded，确保在布局完成之后执行
            Ui(delegate
            {
                for (int i = 0; i < grids.Length; i++)
                {
                    if (grids[i] == null) continue;
                    ScrollViewer sv = ViewerOf(grids[i]);
                    if (sv != null && scrollPos[i] > 0 && scrollPos[i] <= sv.ExtentHeight)
                        sv.ScrollToVerticalOffset(scrollPos[i]);
                }
            }, DispatcherPriority.Loaded);
            SetStatus(light ? "已切换到浅色主题" : "已切换到深色主题");
        }

        private void UpdateThemeButton()
        {
            if (iconLight == null || iconDark == null) return;
            iconLight.Visibility = isLight ? Visibility.Visible : Visibility.Collapsed;
            iconDark.Visibility = isLight ? Visibility.Collapsed : Visibility.Visible;
            Button b = Find<Button>("BtnTheme");
            b.ToolTip = isLight ? "切换到深色主题" : "切换到浅色主题";
        }

        // 状态胶囊用的是模型上的静态画刷，主题切换后按当前文本重算一次
        private void RepaintItems()
        {
            foreach (StartupItem it in startupData)
            {
                if (it.OneShot)
                {
                    // RunOnce 是「一次性」，既不是启用也不是禁用：重绘必须保留蓝色，
                    // 否则任意一次刷新后都会变成绿色，与胶囊文案自相矛盾
                    it.StatusBrush = Palette.Blue;
                    it.StatusBgBrush = Palette.BlueBg;
                }
                else
                {
                    bool off = it.Status == "已禁用";
                    it.StatusBrush = off ? Palette.Red : Palette.Green;
                    it.StatusBgBrush = off ? Palette.RedBg : Palette.GreenBg;
                }
                AiAdviceStore.Paint(it);
            }
            foreach (ServiceItem it in serviceData)
            {
                bool running = it.Status == "正在运行";
                bool stopped = it.Status == "已停止";
                it.StatusBrush = running ? Palette.Green : (stopped ? Palette.Gray : Palette.Orange);
                it.StatusBgBrush = running ? Palette.GreenBg : (stopped ? Palette.GrayBg : Palette.OrangeBg);
                bool disabled = it.StartType == "已禁用";
                it.StartTypeBrush = disabled ? Palette.Red : (it.StartType == "自动" ? Palette.Blue : Palette.Neutral);
                it.StartTypeBgBrush = disabled ? Palette.RedBg : (it.StartType == "自动" ? Palette.BlueBg : Palette.GrayBg);
                AiAdviceStore.Paint(it);
            }
            foreach (TaskItem it in taskData)
            {
                Brush fg = Palette.Gray;
                Brush bg = Palette.GrayBg;
                if (it.Status == "就绪") { fg = Palette.Green; bg = Palette.GreenBg; }
                else if (it.Status == "正在运行") { fg = Palette.Blue; bg = Palette.BlueBg; }
                else if (it.Status == "已禁用") { fg = Palette.Red; bg = Palette.RedBg; }
                else if (it.Status == "排队中") { fg = Palette.Orange; bg = Palette.OrangeBg; }
                it.StatusBrush = fg;
                it.StatusBgBrush = bg;
                AiAdviceStore.Paint(it);
            }
            foreach (CtxMenuItem it in ctxData)
            {
                Brush fg = Palette.Green;
                Brush bg = Palette.GreenBg;
                if (it.Status == "已禁用") { fg = Palette.Red; bg = Palette.RedBg; }
                else if (it.Status == "第三方接管") { fg = Palette.Orange; bg = Palette.OrangeBg; }
                it.StatusBrush = fg;
                it.StatusBgBrush = bg;
            }
            foreach (StartupItem it in startupData) Adviser.Paint(it);
            foreach (ServiceItem it in serviceData) Adviser.Paint(it);
            foreach (TaskItem it in taskData) Adviser.Paint(it);
        }

#if DIAG
        public void SelectPageForTest(int idx)
        {
            if (idx < 0 || idx > grids.Length - 1) return;
            nav.SelectedIndex = idx;
            UpdateLayout();
            RecomputeColumns(idx);
            UpdateLayout();
        }

        // 自检用：返回各导航图标相对窗口的横向位置，用来验证折叠前后不跑位
        public string NavIconInfo()
        {
            StringBuilder sb = new StringBuilder();
            int i = 0;
            foreach (ListBoxItem item in VisualChildren<ListBoxItem>(nav))
            {
                System.Windows.Shapes.Path p =
                    VisualChildren<System.Windows.Shapes.Path>(item).FirstOrDefault();
                if (p == null) continue;
                Point pt = p.TransformToAncestor(this).Transform(new Point(0, 0));
                sb.Append("nav").Append(i).Append('=').Append(pt.X.ToString("0.0")).Append(' ');
                i++;
            }
            Button ai = Find<Button>("BtnAiConfig");
            System.Windows.Shapes.Path ap = VisualChildren<System.Windows.Shapes.Path>(ai).FirstOrDefault();
            if (ap != null)
            {
                Point apt = ap.TransformToAncestor(this).Transform(new Point(0, 0));
                sb.Append("ai=").Append(apt.X.ToString("0.0"));
            }
            return sb.ToString();
        }

        // 自检用：走与点击表头完全相同的代码路径，返回排序后的首行名称
        public string SortForTest(int page, int col)
        {
            if (page < 0 || page > grids.Length - 1 || col >= grids[page].Columns.Count) return "无效列";
            DataGrid g = grids[page];
            DataGridColumn c = g.Columns[col];
            OnGridSorting(g, new DataGridSortingEventArgs(c));
            UpdateLayout();
            object first = g.Items.Count > 0 ? g.Items[0] : null;
            string name = "";
            if (first != null)
            {
                PropertyInfo pi = first.GetType().GetProperty("Name");
                if (pi != null) name = pi.GetValue(first, null) as string;
            }
            return (c.Header == null ? "" : c.Header.ToString()) + " | " + c.SortDirection
                   + " | 首行=" + name;
        }

        public void SelectRowForTest(int page, int index)
        {
            if (page < 0 || page > grids.Length - 1) return;
            if (index < 0 || index >= grids[page].Items.Count) return;
            grids[page].SelectedIndex = index;
            grids[page].ScrollIntoView(grids[page].Items[index]);
            UpdateLayout();
        }
#endif

        private void RecomputeColumns(int page)
        {
            DataGrid g = grids[page];
            if (g == null) return;
            foreach (DataGridColumn c in g.Columns)
            {
                if (c.Width.IsStar) continue;   // 星号列交给 DataGrid 自己按可用宽度分配
                DataGridLength len = c.Width;
                c.Width = DataGridLength.Auto;
                c.Width = len;
            }
        }

        private void ToggleNav()
        {
            collapsed = !collapsed;
            nav.Width = collapsed ? 64 : 212;
            foreach (ListBoxItem item in VisualChildren<ListBoxItem>(nav))
            {
                item.HorizontalContentAlignment = HorizontalAlignment.Left;
                foreach (TextBlock tb in VisualChildren<TextBlock>(item))
                {
                    if ((tb.Tag as string) == "navtext")
                        tb.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
                }
            }
            Button ai = Find<Button>("BtnAiConfig");
            ai.HorizontalContentAlignment = HorizontalAlignment.Left;
            Find<StackPanel>("AiNavText").Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            UpdateLayout();
            AlignAiIcon();
            UpdateLayout();
        }

        // 「配置 AI」不在 ListBox 里，容器度量与菜单项天然差 1px 左右，
        // 这里按实测位置补一次左边距，保证折叠/展开两种状态下图标都在同一竖线上
        private void AlignAiIcon()
        {
            ListBoxItem first = VisualChildren<ListBoxItem>(nav).FirstOrDefault();
            Button ai = Find<Button>("BtnAiConfig");
            if (first == null || ai == null) return;
            System.Windows.Shapes.Path p0 = VisualChildren<System.Windows.Shapes.Path>(first).FirstOrDefault();
            System.Windows.Shapes.Path p1 = VisualChildren<System.Windows.Shapes.Path>(ai).FirstOrDefault();
            if (p0 == null || p1 == null) return;
            double dx = p0.TransformToAncestor(this).Transform(new Point(0, 0)).X
                        - p1.TransformToAncestor(this).Transform(new Point(0, 0)).X;
            if (Math.Abs(dx) < 0.1) return;
            Thickness m = ai.Margin;
            ai.Margin = new Thickness(m.Left + dx, m.Top, m.Right, m.Bottom);
        }

        // 标题栏拖动判定复用 DialogShell.IsInteractive（超集版本），不再各留一份

        private static IEnumerable<T> VisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) yield break;
            int n = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < n; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                T match = child as T;
                if (match != null) yield return match;
                foreach (T sub in VisualChildren<T>(child)) yield return sub;
            }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            try
            {
                IntPtr h = new WindowInteropHelper(this).Handle;
                int v = 2;
                DwmSetWindowAttribute(h, 38, ref v, 4);   // DWMWA_SYSTEMBACKDROP_TYPE = Mica
                v = 2;
                DwmSetWindowAttribute(h, 33, ref v, 4);   // DWMWA_WINDOW_CORNER_PREFERENCE = Round
                HwndSource hs = HwndSource.FromHwnd(h);
                if (hs != null && hs.CompositionTarget != null)
                    hs.CompositionTarget.BackgroundColor = Colors.Transparent;
            }
            catch { }
            ApplyDwmTheme();
        }

        private void ApplyDwmTheme()
        {
            try
            {
                IntPtr h = new WindowInteropHelper(this).Handle;
                if (h == IntPtr.Zero) return;
                int v = isLight ? 0 : 1;
                DwmSetWindowAttribute(h, 20, ref v, 4);   // immersive dark mode
            }
            catch { }
        }

        // 数据读取放到后台线程：WMI 枚举 287 个服务 + COM 遍历 190 个任务要 1.5 秒以上，
        // 同步跑在 UI 线程会让窗口出现后一段时间完全没有响应
        private void LoadAsync(int page)
        {
            if (loading)
            {
                // 已有加载在跑：把请求排队，等它结束后补跑。
                // 直接丢弃会让「操作 → 刷新」这条链路静默失效，列表停在改动前的状态
                pendingLoadPage = (pendingLoadPage == NoPending || pendingLoadPage == page) ? page : -1;
                SetStatus("正在读取系统项目，稍后自动刷新…");
                return;
            }
            loading = true;
            UpdateToolbarState();
            SetStatus(page < 0 ? "正在读取系统项目…" : "正在刷新…");
            Mouse.OverrideCursor = Cursors.Wait;

            Thread t = new Thread(delegate()
            {
                List<string> warn = new List<string>();
                List<StartupItem> a = null;
                List<ServiceItem> b = null;
                List<TaskItem> c = null;
                List<CtxMenuItem> d = null;
                try
                {
                    if (page < 0 || page == 0) a = DataProvider.GetStartupItems(warn);
                    if (page < 0 || page == 1) b = DataProvider.GetServices(warn);
                    if (page < 0 || page == 2) c = DataProvider.GetTasks(warn);
                    if (page < 0 || page == 3) d = DataProvider.GetContextMenuItems(warn);
                }
                catch (Exception ex) { warn.Add("读取失败：" + ex.Message); }
                finally
                {
                    Ui((Action)delegate
                    {
                        Mouse.OverrideCursor = null;
                        ApplyLoaded(page, a, b, c, d, warn);
                    });
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void ApplyLoaded(int page, List<StartupItem> a, List<ServiceItem> b,
                                 List<TaskItem> c, List<CtxMenuItem> d, List<string> warn)
        {
            loading = false;
            UpdateToolbarState();
            if (closed) return;
            if (a != null) startupData = a;
            if (b != null) serviceData = b;
            if (c != null) taskData = c;
            if (d != null) ctxData = d;
            warnings.Clear();
            warnings.AddRange(warn);
            RepaintItems();
            ApplyFilter();
            UpdateCount();
            string w = warnings.Count > 0 ? " · " + warnings.Count + " 项读取告警" : "";
            string baseText;
            if (pendingStatus != null)
            {
                // 操作结束后会立刻触发刷新，这条「完成：成功 N 项」不能被刷新提示盖掉
                baseText = pendingStatus;
                pendingStatus = null;
            }
            else baseText = page < 0 ? "就绪" : "就绪 · 列表已刷新";
            if (!AppPaths.Writability)
                baseText += " · 配置目录不可写（" + AppPaths.Dir + "），设置与 AI 结果无法保存";
            if (!AppDb.Usable)
            {
                string why = AppDb.LastError == null ? "" : AppDb.LastError.Trim();
                if (why.Length > 60) why = why.Substring(0, 60) + "…";
                baseText += " · 配置库不可用" + (why.Length > 0 ? "（" + why + "）" : "");
            }

            if (pendingLoadPage != NoPending)
            {
                // 期间又收到了刷新请求：先补跑，本次结果留到下一次回填时显示，
                // 免得「完成」提示已经出现、列表随后又刷成另一份数据
                int next = pendingLoadPage;
                pendingLoadPage = NoPending;
                pendingStatus = baseText;
                LoadAsync(next);
                return;
            }
            SetStatus(baseText + w);
        }

        // 读取还在进行时先挂起，等数据回填后再显示，避免状态栏被中途覆盖
        private void SetStatusAfterLoad(string text)
        {
            pendingStatus = text;
            if (!loading)
            {
                SetStatus(text);
                pendingStatus = null;
            }
        }

        public void RefreshAll()
        {
            LoadAsync(-1);
        }

        public void RefreshPage(int page)
        {
            LoadAsync(page);
        }

#if DIAG
        // 自检/截图用：同步读，保证后续测量时数据已经就位
        public void RefreshAllSync()
        {
            List<string> warn = new List<string>();
            startupData = DataProvider.GetStartupItems(warn);
            serviceData = DataProvider.GetServices(warn);
            taskData = DataProvider.GetTasks(warn);
            ctxData = DataProvider.GetContextMenuItems(warn);
            warnings.Clear();
            warnings.AddRange(warn);
            RepaintItems();
            ApplyFilter();
            UpdateCount();
            string w = warnings.Count > 0 ? " · " + warnings.Count + " 项读取告警" : "";
            SetStatus("就绪" + w);
        }
#endif

        private void SwitchPage(int idx)
        {
            current = idx;
            for (int i = 0; i < pages.Length; i++)
            {
                pages[i].Visibility = i == idx ? Visibility.Visible : Visibility.Collapsed;
                cmds[i].Visibility = i == idx ? Visibility.Visible : Visibility.Collapsed;
            }
            ApplyHiddenColumnMinimums();
            pageTitle.Text = titles[idx];
            pageSubtitle.Text = subtitles[idx];
            // 只在有残留关键字时才触发 TextChanged -> ApplyFilter；随后那次显式
            // ApplyFilter 不可省（空关键字不会触发事件），避免每次切页都双跑过滤
            if (search.Text.Length > 0) search.Text = "";
            else ApplyFilter();
            Dispatcher.BeginInvoke(new Action(delegate { RecomputeColumns(idx); }), DispatcherPriority.Loaded);
        }

        // 表头点击排序：自己维护排序字段，避免每次刷新/筛选后顺序丢失
        private void OnGridSorting(object sender, DataGridSortingEventArgs e)
        {
            DataGrid g = sender as DataGrid;
            if (g == null) return;
            int page = Array.IndexOf(grids, g);
            if (page < 0) return;
            e.Handled = true;
            string path = e.Column.SortMemberPath;
            if (string.IsNullOrEmpty(path)) return;

            // 升序 -> 降序 -> 取消排序（与 Windows 资源管理器一致）
            ListSortDirection? was = e.Column.SortDirection;
            foreach (DataGridColumn col in g.Columns) col.SortDirection = null;

            if (was == ListSortDirection.Descending)
            {
                sortPath[page] = null;
                sortDir[page] = ListSortDirection.Ascending;
                ApplyFilter();
                SetStatus("已取消排序，恢复默认顺序");
                return;
            }

            ListSortDirection dir = was == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;
            e.Column.SortDirection = dir;
            sortPath[page] = path;
            sortDir[page] = dir;
            ApplyFilter();
            SetStatus("已按「" + (e.Column.Header == null ? path : e.Column.Header.ToString()) + "」"
                      + (dir == ListSortDirection.Ascending ? "升序" : "降序") + "排序");
        }

        public void ApplyFilter()
        {
            string q = search.Text.Trim().ToLowerInvariant();
            // 换 ItemsSource 会清空选中行：每敲一个字就丢选中，右键菜单/双击分析都得重新点。
            // 必须记住整组选中项（多选时只记主项会让批量操作的范围悄悄缩小）
            List<object>[] keep = new List<object>[grids.Length];
            for (int i = 0; i < grids.Length; i++)
            {
                keep[i] = new List<object>();
                foreach (object o in grids[i].SelectedItems) keep[i].Add(o);
            }
            // 每次都必须新建列表：直接复用同一实例不会触发视图刷新，排序会静默不生效
            List<StartupItem> a = q.Length == 0 ? new List<StartupItem>(startupData)
                                                 : startupData.Where(x => x.Search.Contains(q)).ToList();
            List<ServiceItem> b = q.Length == 0 ? new List<ServiceItem>(serviceData)
                                                 : serviceData.Where(x => x.Search.Contains(q)).ToList();
            List<TaskItem> c = q.Length == 0 ? new List<TaskItem>(taskData)
                                                 : taskData.Where(x => x.Search.Contains(q)).ToList();
            IEnumerable<CtxMenuItem> src = q.Length == 0 ? ctxData
                                                         : ctxData.Where(x => x.Search.Contains(q));
            List<CtxMenuItem> d = src.Where(x => x.Tab == ctxTab).ToList();
            SortBy(a, sortPath[0], sortDir[0]);
            SortBy(b, sortPath[1], sortDir[1]);
            SortBy(c, sortPath[2], sortDir[2]);
            SortBy(d, sortPath[3], sortDir[3]);
            grids[0].ItemsSource = a;
            grids[1].ItemsSource = b;
            grids[2].ItemsSource = c;
            grids[3].ItemsSource = d;
            for (int i = 0; i < grids.Length; i++)
            {
                if (keep[i].Count == 0) continue;
                // 只补回仍然存在于新结果里的行：被筛选掉的选中项不应强行加回去
                HashSet<object> alive = new HashSet<object>();
                foreach (object o in grids[i].Items) alive.Add(o);
                try
                {
                    grids[i].SelectedItems.Clear();
                    foreach (object o in keep[i]) if (alive.Contains(o)) grids[i].SelectedItems.Add(o);
                }
                catch (Exception ex) { DiagLog.Swallow("恢复选中 页 " + i, ex); }
            }
            RestoreSortGlyphs();
            // 换数据源后 DataGrid 会沿用旧的列宽缓存，星号列不会自动铺满，这里强制重算一次
            FitLastColumn(grids[0]);
            FitLastColumn(grids[1]);
            FitLastColumn(grids[2]);
            FitLastColumn(grids[3]);
            empties[0].Visibility = a.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            empties[1].Visibility = b.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            empties[2].Visibility = c.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            empties[3].Visibility = d.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateListSummary();
        }

        private sealed class ObjectComparer : IComparer<object>
        {
            internal static readonly ObjectComparer Instance = new ObjectComparer();
            public int Compare(object a, object b) { return CompareValue(a, b); }
        }

        // 排序属性查表缓存：以前每次输入一个字符都要对每一行做一次反射 GetProperty+GetValue
        private static readonly Dictionary<string, PropertyInfo> sortPropCache = new Dictionary<string, PropertyInfo>();

        private static void SortBy<T>(List<T> list, string path, ListSortDirection dir)
        {
            if (string.IsNullOrEmpty(path) || list == null || list.Count < 2) return;
            string cacheKey = typeof(T).FullName + "|" + path;
            PropertyInfo pi;
            if (!sortPropCache.TryGetValue(cacheKey, out pi))
            {
                pi = typeof(T).GetProperty(path);
                sortPropCache[cacheKey] = pi;
            }
            if (pi == null) return;
            // OrderBy 是稳定排序：同状态的行不会因为每次点表头而互相换位
            List<T> sorted = dir == ListSortDirection.Descending
                ? list.OrderByDescending(x => pi.GetValue(x, null), ObjectComparer.Instance).ToList()
                : list.OrderBy(x => pi.GetValue(x, null), ObjectComparer.Instance).ToList();
            list.Clear();
            list.AddRange(sorted);
        }

        private static int CompareValue(object a, object b)
        {
            if (a == null) return b == null ? 0 : -1;
            if (b == null) return 1;
            string sa = a as string;
            string sb = b as string;
            if (sa != null && sb != null) return string.Compare(sa, sb, StringComparison.CurrentCulture);
            IComparable ca = a as IComparable;
            if (ca != null)
            {
                try { return ca.CompareTo(b); }
                // 类型混列等异常退化成「相等」：记下原因，否则用户只会看到顺序乱跳
                catch (Exception ex) { DiagLog.Swallow("排序比较 " + a.GetType().Name, ex); return 0; }
            }
            return 0;
        }

        // ScrollViewer 引用缓存：FitLastColumn 挂在 SizeChanged / ScrollChanged 上，
        // 每次滚动都全树遍历可视树的代价不小，缓存一次即可。
        // 改成实例字段：以前是静态字典，持有 DataGrid 强引用，窗口重建后旧实例无法回收
        private readonly Dictionary<DataGrid, ScrollViewer> viewerCache = new Dictionary<DataGrid, ScrollViewer>();

        private ScrollViewer ViewerOf(DataGrid g)
        {
            ScrollViewer sv;
            if (viewerCache.TryGetValue(g, out sv) && sv != null) return sv;
            sv = VisualChildren<ScrollViewer>(g).FirstOrDefault();
            if (sv != null) viewerCache[g] = sv;
            return sv;
        }

        private void HookFitScroll(DataGrid g)
        {
            ScrollViewer sv = ViewerOf(g);
            if (sv == null) return;
            sv.ScrollChanged += delegate(object s2, ScrollChangedEventArgs e2)
            {
                // 滚动本身不改变可用宽度，只有视口宽度变化才需要重算列宽。
                // 以前不加判断，每次滚轮都写一次列宽 -> 再触发布局 -> 滚动卡顿
                if (Math.Abs(e2.ViewportWidthChange) > 0.5) FitLastColumn(g);
            };
            FitLastColumn(g);
        }

        // 隐藏页用设计列宽兜住测量（DataGrid 在 Collapsed 下会把列压到极小）；
        // 当前页恢复 XAML 的 MinWidth，让最后一列能正常收缩
        private void ApplyHiddenColumnMinimums()
        {
            for (int i = 0; i < grids.Length; i++)
            {
                DataGrid g = grids[i];
                if (g == null) continue;
                bool hidden = pages[i] == null || pages[i].Visibility != Visibility.Visible;
                foreach (DataGridColumn c in g.Columns)
                {
                    double design;
                    if (!designMinWidth.TryGetValue(c, out design)) continue;
                    c.MinWidth = hidden && c.Width.IsAbsolute && c.Width.DisplayValue > 0
                        ? Math.Max(design, c.Width.DisplayValue)
                        : design;
                }
            }
        }

        // 最后一列按容器宽度铺满：横向滚动模式下星号列无法自行计算可用宽度。
        // 按网格分别防重入：窗口级单布尔会让四个网格互相跳过，留下没铺满的列
        private readonly HashSet<DataGrid> fitting = new HashSet<DataGrid>();

        private void FitLastColumn(DataGrid g)
        {
            if (!fitting.Add(g)) return;
            int n = g.Columns.Count;
            if (n == 0) { fitting.Remove(g); return; }
            try
            {
                double used = 0;
                for (int i = 0; i < n - 1; i++) used += g.Columns[i].ActualWidth;
                // 纵向滚动条会吃掉可视宽度；它出现与否取决于行数，所以要显式预留
                double reserve = 0;
                ScrollViewer sv = ViewerOf(g);
                if (sv != null && (sv.ExtentHeight > sv.ViewportHeight + 0.5
                                   || sv.ComputedVerticalScrollBarVisibility == Visibility.Visible))
                    reserve = 12;
                double avail = g.ActualWidth - used - 2 - reserve;
                DataGridColumn last = g.Columns[n - 1];
                double w = Math.Max(last.MinWidth, Math.Floor(avail));
                if (Math.Abs(last.ActualWidth - w) > 0.5)
                    last.Width = new DataGridLength(w);
            }
            finally { fitting.Remove(g); }
        }

        // DataGrid 换 ItemsSource 时会清掉列的排序标记，这里按当前排序状态补回
        private void RestoreSortGlyphs()
        {
            for (int i = 0; i < grids.Length; i++)
            {
                if (string.IsNullOrEmpty(sortPath[i])) continue;
                foreach (DataGridColumn c in grids[i].Columns)
                    c.SortDirection = c.SortMemberPath == sortPath[i] ? sortDir[i] : (ListSortDirection?)null;
            }
        }

        // 列表上方统计条：共 N 项 + 启用/手动/禁用分布
        // 服务按启动类型（自动→启用、手动→手动、已禁用→禁用）；其余页按状态（已禁用→禁用，其余→启用）
        private void UpdateListSummary()
        {
            if (listSummary == null) return;
            bool filtered = search.Text.Trim().Length > 0;
            int total;
            int shown = 0;
            int on = 0;
            int manual = 0;
            int off = 0;
            int oneShot = 0;
            if (current == 0)
            {
                total = startupData.Count;
                foreach (object o in grids[0].Items)
                {
                    shown++;
                    StartupItem si = (StartupItem)o;
                    // 「一次性」单列计数：算进启用会与胶囊/菜单状态矛盾
                    if (si.OneShot) oneShot++;
                    else if (si.Status == "已禁用") off++;
                    else on++;
                }
            }
            else if (current == 1)
            {
                total = serviceData.Count;
                foreach (object o in grids[1].Items)
                {
                    shown++;
                    string st = ((ServiceItem)o).StartType;
                    if (st == "已禁用") off++;
                    else if (st == "手动") manual++;
                    else on++;                       // 自动（以及异常值）都算启用，保证三项之和等于 N
                }
            }
            else if (current == 2)
            {
                total = taskData.Count;
                foreach (object o in grids[2].Items)
                {
                    shown++;
                    if (((TaskItem)o).Status == "已禁用") off++; else on++;
                }
            }
            else
            {
                total = 0;
                foreach (CtxMenuItem x in ctxData) if (x.Tab == ctxTab) total++;
                foreach (object o in grids[3].Items)
                {
                    shown++;
                    if (((CtxMenuItem)o).Status == "已禁用") off++; else on++;
                }
            }

            if (total <= 0)
            {
                listSummary.Text = "共 0 项";
                if (btnAnalyzeAll != null && !aiBusy) btnAnalyzeAll.IsEnabled = false;
                return;
            }
            string head = filtered ? "筛选后 " + shown + " / 共 " + total + " 项" : "共 " + total + " 项";
            listSummary.Text = head + " · 启用 " + on
                             + (current == 1 ? " · 手动 " + manual : "")
                             + (current == 0 && oneShot > 0 ? " · 一次性 " + oneShot : "")
                             + " · 禁用 " + off;
            if (btnAnalyzeAll != null && !aiBusy) btnAnalyzeAll.IsEnabled = shown > 0;
        }

        private void UpdateCount()
        {
            countText.Text = "启动应用 " + startupData.Count + " · 服务 " + serviceData.Count
                           + " · 计划任务 " + taskData.Count + " · 右键菜单 " + ctxData.Count;
        }

        private void SetStatus(string text)
        {
            statusText.Text = text;
            // 状态栏是单行固定高度：长文本会被省略号裁掉，挂 ToolTip 才能看全
            statusText.ToolTip = string.IsNullOrEmpty(text) || text.Length <= 40 ? null : text;
        }

        // 后台线程向 UI 投递动作的统一入口。应用退出时 Dispatcher 一旦停摆，
        // 后台线程的收尾 BeginInvoke 若抛异常，就是一条未处理的后台异常，
        // 会把本已在退出的进程写成崩溃；包一层让退出竞态静默放弃
        private void Ui(Action action)
        {
            try { Dispatcher.BeginInvoke(action); }
            catch { }
        }

        private void Ui(Action action, DispatcherPriority priority)
        {
            try { Dispatcher.BeginInvoke(action, priority); }
            catch { }
        }

        private void InvokeOperation(int page, string op)
        {
            // 菜单回调是经 Dispatcher 排队的，主窗可能在回调执行前已经关闭；
            // 此时再给 ProgressWindow 设 Owner 会抛「窗口已关闭」
            if (quitting || closed) return;
            // 工具条以前绕过菜单回调，刷新中点击会作用在旧快照上；这里统一拦下
            if (loading) { SetStatus("正在读取系统项目，请稍候再执行操作"); return; }
            List<object> items = grids[page].SelectedItems.Cast<object>().ToList();
            if (items.Count == 0)
            {
                Dialogs.Info(this, "未选择项目", "请先在列表中选择一个或多个项目（按住 Ctrl 或 Shift 可多选）。");
                return;
            }
            if (!CanApply(page, op, items))
            {
                if (page == 0 && (op == "enable" || op == "disable") &&
                    items.Any(o => ((StartupItem)o).OneShot))
                    Dialogs.Info(this, "一次性启动项不可启停",
                        "RunOnce 项执行一次后由 Windows 自动移除，没有可靠的启用/禁用开关。");
                else
                    SetStatus("当前选择不支持「" + OpText(op) + "」操作");
                return;
            }
            string verb = OpText(op);
            if (op == "disableAll" && !isAdmin)
            {
                Dialogs.Info(this, "需要管理员权限",
                    "「所有用户屏蔽」要写入所有用户共用的注册表（HKLM），需要管理员权限。\n\n" +
                    "请点击标题栏「以管理员身份重启」，或右键 PCTools.exe 选择「以管理员身份运行」后重试。");
                return;
            }
            if (op == "enable" || op == "disable" || op == "disableAll" || op == "manual" || op == "stop")
            {
                string confirm;
                if (page == 3)
                {
                    confirm = "确定要对选中的 " + items.Count + " 个右键菜单项执行「" + verb + "」操作吗？\n\n" +
                        "注意：屏蔽按 CLSID / 注册表路径生效，同一扩展注册在其它位置也会一起失效；\n" +
                        "更改需重启资源管理器（命令栏「重启资源管理器」按钮）后才会显示。";
                }
                else
                {
                    confirm = "确定要对选中的 " + items.Count + " 个项目执行「" + verb + "」操作吗？\n\n" +
                        "注意：禁用系统关键服务、启动项或计划任务可能影响系统或应用程序功能，请确认已知悉影响。";
                }
                if (!Dialogs.Confirm(this, confirm, "确认" + verb)) return;
            }

            int ok = 0;
            List<string> fails = new List<string>();
            List<string> notes = new List<string>();   // 部分成功的提醒（△ 前缀），不算失败
            // 每用户服务实例改的是模板服务，需要重新登录才生效，状态栏提示一句
            bool perUserSvc = false;
            if (page == 1)
                foreach (object o in items)
                    if (Manager.IsPerUserInstance(((ServiceItem)o).ServiceName)) { perUserSvc = true; break; }

            // 操作放到 worker 线程：停止一个服务最多要等 15 秒，多选时同步跑会把界面卡死
            ProgressWindow pw = new ProgressWindow(verb, items.Count);
            pw.Owner = this;
            Thread worker = new Thread(delegate()
            {
                try
                {
                    for (int i = 0; i < items.Count; i++)
                    {
                        if (pw.Cancelled) break;
                        object item = items[i];
                        string name = Manager.ItemName(item);
                        string err = Manager.Apply(page, op, item);
                        if (err == null) ok++;
                        else if (err.StartsWith(Manager.PartialPrefix, StringComparison.Ordinal))
                            notes.Add("· " + name + "：" + err.Substring(Manager.PartialPrefix.Length));
                        else fails.Add("· " + name + "：" + err);
                        int done = i + 1;
                        Ui(delegate { pw.Report(done, items.Count, name); });
                    }
                }
                catch (Exception ex)
                {
                    fails.Add("· 批量操作中断：" + ex.Message);
                }
                finally
                {
                    Ui(delegate { pw.Finish(); });
                }
            });
            worker.IsBackground = true;
            worker.Start();
            pw.ShowDialog();      // worker 跑完（或取消后收尾）才会真正关闭

            // 收尾时要弹模态框，主窗已关闭就什么都不做（Owner 已失效）
            if (quitting || closed) return;
            RefreshPage(page);
            bool cancelled = pw.Cancelled;

            if (fails.Count > 0)
            {
                StringBuilder msg = new StringBuilder();
                msg.Append("「").Append(verb).Append("」完成：成功 ").Append(ok)
                   .Append(" 项，失败 ").Append(fails.Count).AppendLine(" 项。").AppendLine();
                int shown = Math.Min(8, fails.Count);
                for (int i = 0; i < shown; i++) msg.AppendLine(fails[i]);
                if (fails.Count > shown) msg.AppendLine("…（其余 " + (fails.Count - shown) + " 项略）");
                // 只有确实命中权限类错误才引导提权：非管理员时把任何失败都归因于权限
                // 是错误引导（比如服务超时也会弹「请以管理员身份重启」）
                bool denied = false;
                foreach (string f in fails) if (Manager.IsPermissionError(f)) { denied = true; break; }
                if (denied)
                    msg.AppendLine().Append("存在权限不足的失败项：请点击标题栏「以管理员身份重启」，或右键 PCTools.exe 选择「以管理员身份运行」后重试。");
                Dialogs.Info(this, "部分操作未成功", msg.ToString());
                SetStatusAfterLoad("「" + verb + "」完成：成功 " + ok + " 项，失败 " + fails.Count + " 项"
                    + (notes.Count > 0 ? "，" + notes.Count + " 项部分生效" : ""));
            }
            else if (cancelled)
            {
                SetStatusAfterLoad("「" + verb + "」已取消：完成 " + ok + " 项，列表已刷新"
                    + (page == 3 ? "，重启资源管理器后生效"
                                 : (perUserSvc ? "（每用户服务改的是模板，重新登录后生效）" : "")));
            }
            else
            {
                // 全部成功（可能带部分生效的提醒）：不弹失败框，提醒走状态栏
                if (notes.Count > 0)
                {
                    StringBuilder ns = new StringBuilder();
                    foreach (string n in notes) ns.AppendLine(n);
                    Dialogs.Info(this, "「" + verb + "」已执行",
                        "成功 " + ok + " 项，其中 " + notes.Count + " 项部分生效：\n\n" + ns.ToString());
                }
                SetStatusAfterLoad("「" + verb + "」完成：成功 " + ok + " 项，列表已刷新"
                    + (notes.Count > 0 ? "，" + notes.Count + " 项部分生效" : "")
                    + (page == 3 ? "，重启资源管理器后生效"
                                 : (perUserSvc ? "（每用户服务改的是模板，重新登录后生效）" : "")));
            }
        }

        private static string OpText(string op)
        {
            switch (op)
            {
                case "enable": return "启用";
                case "disable": return "禁用";
                case "disableAll": return "所有用户屏蔽";
                case "manual": return "改为手动";
                case "start": return "启动";
                case "stop": return "停止";
                case "run": return "运行";
                default: return op;
            }
        }

#if DIAG
        // ---- 自检专用接口（发布版不编译进 exe）----
        public int StartupCount { get { return startupData.Count; } }
        public int CtxCount { get { return ctxData.Count; } }
        // 自检用：跳过确认框直接跑逐项分析，max>0 时只处理前 max 项
        public void AnalyzeForTest(int page, int max)
        {
            List<object> items = grids[page].Items.Cast<object>().ToList();
            if (max > 0 && items.Count > max) items = items.GetRange(0, max);
            AiConfig cfg = AiConfig.Load();
            if (!cfg.Configured || items.Count == 0) return;
            RunAnalysisBatch(page, items, cfg, null);
        }

        // 自检用：读列表上方统计条文案
        public string SummaryForTest() { return listSummary == null ? "" : listSummary.Text; }

        // 自检用：AI 批量任务是否还在跑（非模态后只能靠它判断收尾时机）
        public bool AiBusyForTest { get { return aiBusy; } }

        public bool IsLoading { get { return loading; } }
        public int ServiceCount { get { return serviceData.Count; } }
        public int TaskCount { get { return taskData.Count; } }
        public int WarningCount { get { return warnings.Count; } }
        public string FirstWarning { get { return warnings.Count > 0 ? warnings[0] : ""; } }
        public int Rows(int page) { return grids[page].Items.Count; }
        public int MenuItems(int page) { return grids[page].ContextMenu == null ? 0 : grids[page].ContextMenu.Items.Count; }

        public string ColumnInfo(int page)
        {
            StringBuilder sb = new StringBuilder();
            foreach (DataGridColumn c in grids[page].Columns)
            {
                sb.Append(c.Header).Append("[W=").Append(c.Width.ToString())
                  .Append(",A=").Append(((int)c.ActualWidth).ToString()).Append("] ");
            }
            sb.Append("| grid=").Append((int)grids[page].ActualWidth);
            sb.Append(" win=").Append((int)ActualWidth);
            FrameworkElement p = (FrameworkElement)VisualTreeHelper.GetParent(grids[page]);
            sb.Append(" parent=").Append(p != null ? (int)p.ActualWidth : -1);
            ScrollViewer sv = VisualChildren<ScrollViewer>(grids[page]).FirstOrDefault();
            if (sv != null) sb.Append(" viewport=").Append((int)sv.ViewportWidth).Append(" extent=").Append((int)sv.ExtentWidth);
            return sb.ToString();
        }
#endif
    }

    // ------------------------------------------------------------------ Elevation prompt
    public class PromptWindow : Window
    {
        public bool Restart { get; private set; }

        public PromptWindow()
        {
            Title = "需要管理员权限";
            Width = 540;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            FrameworkElement view = (FrameworkElement)XamlReader.Parse(Views.Prompt);
            DialogShell.AttachTheme(view);
            DialogShell.Prepare(this, view);

            Button cont = (Button)view.FindName("BtnContinue");
            Button restart = (Button)view.FindName("BtnRestartAdmin");
            cont.Click += delegate { Restart = false; Close(); };
            restart.Click += delegate { Restart = true; Close(); };
            // Enter / Esc 都选择「以普通权限继续」：回车是安全动作，不该把用户直接带到
            // UAC 弹窗（焦点落在哪个按钮上时 Enter 的语义曾不一致——窗口级 KeyDown 兜底
            // 会越过焦点的「继续」按钮直接提权）。要提权请明确点击「以管理员身份重启」
            cont.IsDefault = true;
            cont.IsCancel = true;
            cont.Focus();
        }
    }

    // ---------------------------------------------------------------- System
    // 本机系统版本：RtlGetVersion 拿真实内核版本（不受 manifest 兼容性声明影响），
    // 再补上 DisplayVersion 与 UBR 得到和 winver 一致的描述
    internal static class OsInfo
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RTL_OSVERSIONINFOEXW
        {
            public int dwOSVersionInfoSize;
            public int dwMajorVersion;
            public int dwMinorVersion;
            public int dwBuildNumber;
            public int dwPlatformId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szCSDVersion;
            public ushort wServicePackMajor;
            public ushort wServicePackMinor;
            public ushort wSuiteMask;
            public byte wProductType;
            public byte wReserved;
        }

        [DllImport("ntdll.dll", CharSet = CharSet.Unicode)]
        private static extern int RtlGetVersion(ref RTL_OSVERSIONINFOEXW info);

        private static string cached;

        internal static string Describe()
        {
            if (cached != null) return cached;

            int major = 10;
            int minor = 0;
            int build = 0;
            byte productType = 1;    // 1 = 工作站，非 1 = 服务器
            try
            {
                RTL_OSVERSIONINFOEXW v = new RTL_OSVERSIONINFOEXW();
                v.dwOSVersionInfoSize = Marshal.SizeOf(typeof(RTL_OSVERSIONINFOEXW));
                if (RtlGetVersion(ref v) == 0)
                {
                    major = v.dwMajorVersion;
                    minor = v.dwMinorVersion;
                    build = v.dwBuildNumber;
                    productType = v.wProductType;
                }
            }
            catch { }

            string display = "";
            int ubr = 0;
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (k != null)
                    {
                        object d = k.GetValue("DisplayVersion");
                        if (d != null) display = d.ToString();
                        object u = k.GetValue("UBR");
                        if (u != null) int.TryParse(u.ToString(), out ubr);
                    }
                }
            }
            catch { }

            // Win11 只能靠内部版本号判定：22000 起；更早的内核按主/次版本与产品类型命名，
            // 否则 Win7/8/Server 全会被标成 Windows 10，发给 AI 的系统版本就是错的
            string name;
            if (major > 10 || build >= 22000)
            {
                name = "Windows 11";
                if (string.IsNullOrEmpty(display))
                    display = build >= 26200 ? "25H2" : build >= 26100 ? "24H2"
                            : build >= 22631 ? "23H2" : build >= 22621 ? "22H2" : "21H2";
            }
            else if (major == 10) name = productType == 1 ? "Windows 10" : "Windows Server";
            else if (major == 6 && minor == 3) name = productType == 1 ? "Windows 8.1" : "Windows Server 2012 R2";
            else if (major == 6 && minor == 2) name = productType == 1 ? "Windows 8" : "Windows Server 2012";
            else if (major == 6 && minor == 1) name = productType == 1 ? "Windows 7" : "Windows Server 2008 R2";
            else if (major == 6) name = productType == 1 ? "Windows Vista" : "Windows Server 2008";
            else name = "Windows NT " + major + "." + minor;

            StringBuilder sb = new StringBuilder();
            sb.Append(name);
            if (!string.IsNullOrEmpty(display)) sb.Append(" ").Append(display);
            sb.Append("（build ").Append(build);
            if (ubr > 0) sb.Append(".").Append(ubr);
            sb.Append("，").Append(Environment.Is64BitOperatingSystem ? "x64" : "x86").Append("）");
            cached = sb.ToString();
            return cached;
        }
    }

    // ------------------------------------------------------------------ AI
    // API Key 的静态数据保护（DPAPI / CurrentUser）：
    // 存库格式为「dp:<base64>」，密文只能在同一用户账户下解开。UAC 提权不切换账户，
    // 普通权限录入、管理员身份运行依然可解；换用户/换机器解不开时按「未配置 Key」
    // 处理，用户重新录入即可。库中只接受 dp: 前缀的密文，其它格式一律视为无效，
    // 下次保存直接清空，不再为任何历史明文格式保留兼容路径。
    // 防护边界：挡「同机其它账户读取」「离线挂载磁盘」与 FAT/exFAT 下 ACL 收窄失效
    // 这三条暴露面；同用户权限下运行的恶意进程不在防御范围内（现状下它连解密都不需要）
    internal static class Secret
    {
        private const string Prefix = "dp:";
        // 应用级盐：不同应用的 DPAPI 密文互不通用
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PCTools.AiConfig.v1");

        // 加密失败时不允许退回明文：库里出现明文 Key，等于把「密文 + ACL」两层防护
        // 一起作废，而且界面完全看不出来。返回 false 让调用方中止保存并告知用户
        internal static bool TryProtect(string plain, out string cipher, out string error)
        {
            cipher = "";
            error = null;
            if (string.IsNullOrEmpty(plain)) return true;
            try
            {
                byte[] raw = Encoding.UTF8.GetBytes(plain);
                byte[] enc = ProtectedData.Protect(raw, Entropy, DataProtectionScope.CurrentUser);
                cipher = Prefix + Convert.ToBase64String(enc);
                return true;
            }
            catch (Exception ex)
            {
                DiagLog.Swallow("DPAPI Protect 失败", ex);
                cipher = null;
                error = ex.Message;
                return false;
            }
        }

        internal static string Unprotect(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return "";
            if (!stored.StartsWith(Prefix, StringComparison.Ordinal)) return "";   // 非当前格式，不再兼容
            try
            {
                byte[] enc = Convert.FromBase64String(stored.Substring(Prefix.Length));
                byte[] raw = ProtectedData.Unprotect(enc, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(raw);
            }
            catch { return ""; }     // 换用户/换机器/数据损坏：视为未配置，等用户重新录入
        }
    }

    // ------------------------------------------------------------------ AI
    // AI 接口配置：最多 5 套，同一时间只有一套生效（active）
    internal class AiConfig
    {
        internal const int MaxProfiles = 5;

        internal int Index = 1;
        internal string Name = "";
        internal string Endpoint = "https://api.openai.com/v1/chat/completions";
        internal string ApiKey = "";
        internal string Model = "gpt-4o-mini";

        // 由 LoadAll / SaveAll 置位：表示这一套确实在 ai_config 里有行
        private bool fromDb;

        // 库里存着 dp: 密文，但当前账户解不开（换用户 / 换机器 / DPAPI 主密钥损坏）。
        // 这种配置的 ApiKey 是空串，绝不能在保存时把它当成「用户清空了 Key」写回库，
        // 否则一份可恢复的密文会被永久抹掉
        internal bool KeyUnreadable;

        internal string Title
        {
            get { return string.IsNullOrEmpty(Name) ? "配置 " + Index : Name; }
        }

        // 保存失败时给界面用的原因（数据库位置不可写等）
        internal string LastError = "";

        internal bool Ready { get { return !string.IsNullOrEmpty(Endpoint) && !string.IsNullOrEmpty(Model); } }

        // 库里真的存过这套 + 地址与模型非空。fromDb 让这里零 IO，不必每次属性访问都查库
        internal bool Configured { get { return fromDb && Ready; } }

        // 读取全部配置。readError 非 null 表示「没读成功」，与「库里没有配置」是两件事：
        // 前者绝不能退化成一份默认配置——调用方据此弹配置窗并整表保存，会把另外几套
        // 配置与 Key 一起抹掉（SaveAll 是 DELETE + 重插）
#if DIAG
        // 仅自检使用；发布版统一走带 readError 的完整签名
        internal static List<AiConfig> LoadAll(out int active)
        {
            string ignored;
            return LoadAll(out active, out ignored);
        }
#endif

        internal static List<AiConfig> LoadAll(out int active, out string readError)
        {
            List<AiConfig> list = new List<AiConfig>();
            active = 1;
            readError = null;
            try
            {
                if (!AppDb.Ensure())
                {
                    readError = string.IsNullOrEmpty(AppDb.LastError) ? "配置库不可用" : AppDb.LastError;
                    return list;
                }
                string err;
                // 配置窗构造在 UI 线程调用这里：短超时避免后台批量落库持锁时界面卡 3 秒，
                // 读失败由 readError 走「禁止保存」路径，不丢数据
                IntPtr db = Sqlite.Open(AppDb.FilePath, out err, 500);
                if (db == IntPtr.Zero)
                {
                    readError = string.IsNullOrEmpty(err) ? "无法打开配置库" : err;
                    return list;
                }
                try
                {
                    List<string[]> rows = Sqlite.Rows(db,
                        "SELECT config_index, IFNULL(name,''), IFNULL(endpoint,''), " +
                        "IFNULL(model,''), IFNULL(api_key,'') FROM ai_config ORDER BY config_index ASC;", out err);
                    // 读失败时 rows 可能是空/半截：按失败处理，不能当成「从未配置过」
                    if (err != null) { readError = err; return list; }
                    foreach (string[] p in rows)
                    {
                        if (p.Length < 5) continue;
                        int idx;
                        if (!int.TryParse(p[0], out idx) || idx < 1 || idx > MaxProfiles) continue;
                        AiConfig c = new AiConfig();
                        c.Index = idx;
                        c.Name = p[1];
                        c.Endpoint = p[2];
                        c.Model = p[3];
                        c.ApiKey = Secret.Unprotect(p[4]);   // 只认 dp: 密文；解不开按空处理
                        // 只有 dp: 密文解不开才保留原值（换账户 / 换机器 / 数据损坏）。
                        // 非 dp: 内容是无效格式（历史明文等），按空处理并在下次保存时清掉
                        c.KeyUnreadable = p[4].StartsWith("dp:", StringComparison.Ordinal) &&
                                          c.ApiKey.Length == 0;
                        c.fromDb = true;
                        list.Add(c);
                        if (list.Count >= MaxProfiles) break;
                    }
                    // active 读失败不影响使用（回落到第一套），不当作整体读取失败
                    List<string> st = Sqlite.Column(db,
                        "SELECT IFNULL(value,'') FROM app_setting WHERE key='ai.active';", out err);
                    if (st.Count > 0)
                    {
                        int n;
                        if (int.TryParse(st[0], out n)) active = n;
                    }
                }
                finally { Sqlite.Close(db); }
            }
            catch (Exception ex)
            {
                readError = ex.Message;
                return list;
            }

            if (list.Count == 0)
            {
                // 确认读到了、但确实没有配置：引导用户填写，而不是直接抛 401
                AiConfig c = new AiConfig();
                c.Index = 1;
                c.Name = "默认配置";
                list.Add(c);
            }
            bool foundActive = false;
            foreach (AiConfig c in list) if (c.Index == active) foundActive = true;
            if (!foundActive) active = list[0].Index;
            return list;
        }

        // 当前生效的那套
        internal static AiConfig Load(out string readError)
        {
            int active;
            List<AiConfig> list = LoadAll(out active, out readError);
            foreach (AiConfig c in list) if (c.Index == active) return c;
            return list.Count > 0 ? list[0] : new AiConfig();
        }

#if DIAG
        // 旧签名：读库失败时静默返回内存默认配置。生产代码已全部改用 Load(out readError)
        //（读失败拿默认值去 SaveAll 会整表覆盖真实配置），只剩 DIAG 自检为省事还在用——
        // 自检只读不写，读失败拿默认值无害
        internal static AiConfig Load()
        {
            string ignored;
            return Load(out ignored);
        }
#endif

        // 整表重写：一次事务里清空 ai_config 再逐套写入，顺便清掉已删配置的累计用量
        internal static bool SaveAll(List<AiConfig> list, int active)
        {
            if (list == null) list = new List<AiConfig>();
            if (!AppDb.Ensure()) { Note(list, AppDb.LastError); return false; }
            string err;
            IntPtr db = Sqlite.Open(AppDb.FilePath, out err);
            if (db == IntPtr.Zero) { Note(list, err); return false; }
            try
            {
                string now = Sqlite.Q(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                StringBuilder sb = new StringBuilder();
                StringBuilder keep = new StringBuilder();

                // 先取一遍现存密文：只有 dp: 密文解不开时才原样写回；
                // 非 dp: 的历史内容不再兼容，保存时直接清空
                Dictionary<int, string> oldCipher = new Dictionary<int, string>();
                List<string[]> oldRows = Sqlite.Rows(db,
                    "SELECT config_index, IFNULL(api_key,'') FROM ai_config;", out err);
                // 读失败时不能继续：oldCipher 为空会让「解不开的 Key」走 TryProtect("")，
                // 把一份仍可恢复的密文覆盖成空串。宁可本次保存失败，也不能毁掉已有密文
                if (err != null)
                {
                    Note(list, "读取现有配置失败，已取消保存：" + err);
                    return false;
                }
                foreach (string[] row in oldRows)
                {
                    int k;
                    if (row.Length < 2 || !int.TryParse(row[0], out k)) continue;
                    oldCipher[k] = row[1];
                }

                sb.Append("BEGIN;DELETE FROM ai_config;");
                foreach (AiConfig c in list)
                {
                    string cipher;
                    string hit;
                    if (c.KeyUnreadable && string.IsNullOrEmpty(c.ApiKey) && oldCipher.TryGetValue(c.Index, out hit) &&
                        hit.StartsWith("dp:", StringComparison.Ordinal))
                    {
                        cipher = hit;                       // 沿用原密文，等用户录入新 Key 再替换
                    }
                    else
                    {
                        string perr;
                        if (!Secret.TryProtect(c.ApiKey == null ? "" : c.ApiKey, out cipher, out perr))
                        {
                            // 明文落库比保存失败严重得多：整表保存直接放弃，旧配置保持原样
                            Note(list, "无法加密 API Key（DPAPI 不可用）：" + perr +
                                       "；已取消保存，避免 Key 以明文写入数据库");
                            return false;
                        }
                    }
                    sb.Append("INSERT OR REPLACE INTO ai_config (config_index,name,endpoint,model,api_key,updated) VALUES (")
                      .Append(c.Index).Append(',').Append(Sqlite.Q(c.Name == null ? "" : c.Name)).Append(',')
                      .Append(Sqlite.Q(c.Endpoint == null ? "" : c.Endpoint)).Append(',')
                      .Append(Sqlite.Q(c.Model == null ? "" : c.Model)).Append(',')
                      .Append(Sqlite.Q(cipher)).Append(',')
                      .Append(now).Append(");");
                    if (keep.Length > 0) keep.Append(',');
                    keep.Append(c.Index);
                }
                sb.Append("INSERT OR REPLACE INTO app_setting (key,value) VALUES ('ai.active',")
                  .Append(Sqlite.Q(active.ToString())).Append(");");
                // 被删掉的配置连带清掉它的累计用量：Index 会被「新增」复用
                if (keep.Length == 0) sb.Append("DELETE FROM usage;");
                else sb.Append("DELETE FROM usage WHERE config_index NOT IN (").Append(keep.ToString()).Append(");");
                sb.Append("COMMIT;");

                string e = Sqlite.Exec(db, sb.ToString());
                if (e != null)
                {
                    Sqlite.Exec(db, "ROLLBACK;");
                    Note(list, e);
                    return false;
                }
            }
            finally { Sqlite.Close(db); }

            foreach (AiConfig c in list)
            {
                c.fromDb = true;
                if (!string.IsNullOrEmpty(c.ApiKey)) c.KeyUnreadable = false;   // 用户已录入新 Key
            }
            AiUsageStore.Invalidate();
            return true;
        }

        // 保存失败时把原因挂到每一套配置上：调用方有的读 list[0]（单套 Save 路径），
        // 有的读正在编辑的那套（配置窗 SaveCurrentProfile），挂全套两边都能取到
        private static void Note(List<AiConfig> list, string msg)
        {
            if (list == null || list.Count == 0) return;
            string why = string.IsNullOrEmpty(msg) ? "未知原因" : msg;
            foreach (AiConfig c in list) c.LastError = why;
        }
    }

    // 一次请求的完整结果：正文 + 用量。接口没回 usage 时 HasUsage=false
    internal sealed class AiReply
    {
        internal string Text = "";
        internal int InTokens;
        internal int OutTokens;
        internal bool HasUsage;
        internal bool Parsed;        // 正文确实来自 content 字段，才允许写入缓存
        internal string Finish = ""; // finish_reason：length 表示输出被截断，批量解析会缺项
    }

    internal static class AiClient
    {
        static AiClient()
        {
            // 收敛到 TLS 1.2 及以上：|= 叠加会把 SSL3 / TLS1.0 留在协商列表里。
            // 同时尽量启用 TLS 1.3（枚举值 12288，旧系统上赋这个值会被忽略/抛错）
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)12288; }
                catch { }
                ServicePointManager.Expect100Continue = false;
            }
            catch { }
        }

        private static readonly List<HttpWebRequest> inflight = new List<HttpWebRequest>();

        private static void Track(HttpWebRequest req)
        {
            lock (inflight) inflight.Add(req);
        }

        private static void Untrack(HttpWebRequest req)
        {
            lock (inflight) inflight.Remove(req);
        }

        // 进程退出 / 取消批量时需要真正中止在途请求，否则后台线程仍会把整批跑完并继续消耗 Token
        internal static void AbortAll()
        {
            HttpWebRequest[] all;
            lock (inflight) { all = inflight.ToArray(); inflight.Clear(); }
            for (int i = 0; i < all.Length; i++)
            {
                try { all[i].Abort(); } catch { }
            }
        }

        // 端点校验：必须是绝对 http(s) 地址；带 Key 时禁止远程明文 HTTP（Key 会裸奔在网络上）。
        // 本机回环地址放行，方便对接 Ollama / vLLM 这类本地推理服务
        internal static string ValidateEndpoint(string endpoint, bool hasKey)
        {
            if (string.IsNullOrEmpty(endpoint)) return "接口地址不能为空";
            Uri uri;
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out uri)) return "接口地址不是合法的绝对 URL";
            if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
                return "接口地址必须以 https:// 或 http:// 开头";
            if (uri.Scheme == Uri.UriSchemeHttp && hasKey && !uri.IsLoopback)
                return "该地址使用明文 HTTP，API Key 会在网络上明文传输；请改用 https:// 地址（本机 127.0.0.1 / localhost 除外）";
            return null;
        }

        // ---------------- 提示词：四个页面各一套，不共用模板 ----------------
        // 通用措辞只存一份：static readonly 避免 const 在每处引用各写一份 UTF-16 副本，
        // 也避免改一条格式约束要同步改 5 处。拼接顺序保持与原来逐字一致
        private static readonly string PromptLanguage =
            "请用简体中文、面向普通用户回答，语言简洁客观，不使用 Markdown 符号、不使用星号或井号。";
        private static readonly string PromptTwoParts =
            "必须严格按两段格式输出，两段标记都不可省略：";
        private static readonly string PromptLabelRule =
            "这一行不要写“建议”二字、不加标点、不写理由。";
        private static readonly string PromptReasonHead =
            "标签之后换行，再用 2~4 句说明判断依据、";
        private static readonly string PromptTail400 =
            "全文控制在 400 字以内，不要输出其它标题或开场白。";
        private static readonly string PromptTailNoIntro260 =
            "不要输出【介绍】段，也不要重复描述这个项目是什么。全文控制在 260 字以内。";

        // 启动项：程序里只有「启用 / 禁用」两种操作，提示词必须禁止出现“手动”
        internal static readonly string SystemPromptStartup =
            "你是 Windows 系统管理专家（用户会给出本机系统版本），熟悉注册表 Run 项、启动文件夹与开机自启机制。" +
            PromptLanguage + PromptTwoParts +
            "第一段以【介绍】开头，用 3~5 句说明这个启动项是什么、由哪个软件安装、启动后做什么、" +
            "作用于当前用户还是所有用户；" +
            "第二段以【建议】开头，紧跟换行；第一行只输出结论标签，从「启用」「禁用」中选择一个。" +
            "启动项只有启用和禁用两种处理，禁止把“手动”当作处理方式（如“改为手动”“建议手动”），" +
            "也不要写“保留”“按需”之类的折中说法；" +
            PromptLabelRule + PromptReasonHead + "禁用后会失去什么功能、以及如何重新启用。" + PromptTail400;

        // 服务：可用「启用 / 手动 / 禁用」三种启动类型
        internal static readonly string SystemPromptService =
            "你是 Windows 系统管理专家（用户会给出本机系统版本），熟悉 Windows 服务的启动类型、服务依赖与 svchost 分组。" +
            PromptLanguage + PromptTwoParts +
            "第一段以【介绍】开头，用 3~5 句说明这个服务是什么、由哪个组件提供、提供什么功能、" +
            "有哪些其它服务或功能依赖它；" +
            "第二段以【建议】开头，紧跟换行；第一行只输出结论标签，从「启用」「手动」「禁用」中选择一个：" +
            "「启用」表示开机自动启动，「手动」表示开机不启动、需要时由系统或用户拉起，" +
            "「禁用」表示完全禁止启动；" + PromptLabelRule +
            PromptReasonHead + "改为该启动类型后的影响、以及恢复方式。" + PromptTail400;

        // 计划任务：只有「启用 / 禁用」
        internal static readonly string SystemPromptTask =
            "你是 Windows 系统管理专家（用户会给出本机系统版本），熟悉任务计划程序的触发器、操作与任务路径。" +
            PromptLanguage + PromptTwoParts +
            "第一段以【介绍】开头，用 3~5 句说明这个计划任务是什么、由哪个软件注册、" +
            "在什么条件下触发、触发后执行什么操作；" +
            "第二段以【建议】开头，紧跟换行；第一行只输出结论标签，从「启用」「禁用」中选择一个。" +
            "计划任务只有启用和禁用两种处理，禁止把“手动”当作处理方式（如“改为手动”“建议手动”），" +
            "也不要写“按需”之类的折中说法；" +
            PromptLabelRule + PromptReasonHead + "禁用后会失去什么、以及如何重新启用。" + PromptTail400;

        // 右键菜单项：只有「启用 / 禁用」（屏蔽）
        internal static readonly string SystemPromptCtx =
            "你是 Windows 系统管理专家（用户会给出本机系统版本），熟悉资源管理器右键菜单的 Shell 扩展、静态动词与打包扩展。" +
            PromptLanguage + PromptTwoParts +
            "第一段以【介绍】开头，用 3~5 句说明这个右键菜单项是什么、由哪个软件注册、" +
            "出现在哪个位置、点击后做什么；" +
            "第二段以【建议】开头，紧跟换行；第一行只输出结论标签，从「启用」「禁用」中选择一个。" +
            "右键菜单项只有启用和禁用两种处理，禁止把“手动”当作处理方式，也不要写“清理”“删除”之类的说法；" +
            PromptLabelRule + PromptReasonHead + "禁用后会少什么功能、以及如何恢复。" + PromptTail400;

        // 按页取提示词（0 启动项 / 1 服务 / 2 计划任务 / 3 右键菜单）
        internal static string SystemPromptFor(int page)
        {
            switch (page)
            {
                case 0: return SystemPromptStartup;
                case 1: return SystemPromptService;
                case 2: return SystemPromptTask;
                default: return SystemPromptCtx;
            }
        }

        // 已经有项目介绍时只刷新处理建议：省掉重新生成介绍的 Token，
        // 与上面四套「必须两段」的提示词互不影响（首次分析仍用 SystemPromptFor）
        internal static string SystemPromptAdviceOnly(int page)
        {
            string head = "你是 Windows 系统管理专家（用户会给出本机系统版本），熟悉注册表 Run 项、启动文件夹、" +
                "Windows 服务的启动类型与服务依赖、任务计划程序与资源管理器右键菜单。" +
                PromptLanguage +
                "用户给出的这个系统项目此前已经生成过介绍，本次只需要重新评估处理方式。" +
                "必须严格按两行结构开头：第一行只写【建议】这四个字符（全角方括号，不能省略、不能与标签写在同一行），" +
                "第二行只输出结论标签，";
            string tail;
            if (page == 1)
                tail = "从「启用」「手动」「禁用」中选择一个：「启用」表示开机自动启动，" +
                    "「手动」表示开机不启动、需要时由系统或用户拉起，「禁用」表示完全禁止启动；" +
                    PromptLabelRule + PromptReasonHead + "改为该启动类型后的影响、以及恢复方式。" +
                    PromptTailNoIntro260;
            else
                tail = "从「启用」「禁用」中选择一个。这个项目只有启用和禁用两种处理，" +
                    "禁止把“手动”当作处理方式（如“改为手动”“建议手动”），也不要写“保留”“按需”之类的折中说法；" +
                    PromptLabelRule + PromptReasonHead + "禁用后会失去什么、以及如何重新启用。" +
                    PromptTailNoIntro260;
            return head + tail;
        }

        // 与页面无关的调用（连通性测试）用的兜底提示词
        internal static readonly string SystemPrompt = SystemPromptService;

        internal static string Ask(AiConfig cfg, string userPrompt)
        {
            return AskReply(cfg, userPrompt, null, null, 90000).Text;
        }

        // onRequest：把 HttpWebRequest 暴露给调用方，窗口关闭时可以 Abort 掉在途请求
        internal static string Ask(AiConfig cfg, string userPrompt, Action<HttpWebRequest> onRequest)
        {
            return AskReply(cfg, userPrompt, SystemPrompt, onRequest, 90000).Text;
        }

        // systemPrompt：各页面自己的提示词，处理建议的结论选项与措辞互不相同
        internal static string Ask(AiConfig cfg, string userPrompt, string systemPrompt,
                                   Action<HttpWebRequest> onRequest)
        {
            return AskReply(cfg, userPrompt, systemPrompt, onRequest, 90000).Text;
        }

        // 只取正文，需要用量（token）的调用方直接用 AskReply
        internal static string Ask(AiConfig cfg, string userPrompt, string systemPrompt,
                                   Action<HttpWebRequest> onRequest, int timeoutMs)
        {
            return AskReply(cfg, userPrompt, systemPrompt, onRequest, timeoutMs).Text;
        }

        // 带 HTTP 状态码的失败：用于区分「值得重试」（429/5xx/网络抖动）与「不该重试」（401/400）
        private sealed class AiHttpException : Exception
        {
            internal int Code;
            internal bool Retryable;
            internal AiHttpException(int code, string message, bool retryable)
                : base(message) { Code = code; Retryable = retryable; }
        }

        // 单次输出上限：批量建议一页几百行，不设上限时网关可能按默认值截断，
        // 截断后 Parse 只拿到前半部分却仍算成功
        private const int MaxOutputTokens = 3072;

        // timeoutMs：批量建议这类长请求需要更长的等待
        // ⚠ 全同步阻塞 API：单次最长 90/120/300 秒超时，叠加 429/5xx 重试与退避，
        // 最坏可挂数分钟。只允许在后台线程调用；在 UI 线程调用会直接冻结界面
        internal static AiReply AskReply(AiConfig cfg, string userPrompt, string systemPrompt,
                                         Action<HttpWebRequest> onRequest, int timeoutMs)
        {
            if (string.IsNullOrEmpty(systemPrompt)) systemPrompt = SystemPrompt;
            string bad = ValidateEndpoint(cfg.Endpoint, !string.IsNullOrEmpty(cfg.ApiKey));
            if (bad != null) throw new Exception(bad);

            bool includeMaxTokens = true;
            bool maxTokensRetried = false;
            int retries = 0;
            Exception last = null;
            // 429 / 5xx / 连接类失败最多重试两次：批量分析一次限流就丢一项，用户只能整批重跑
            while (true)
            {
                if (retries > 0) Thread.Sleep(retries == 1 ? 1200 : 3000);
                try
                {
                    return SendOnce(cfg, userPrompt, systemPrompt, onRequest, timeoutMs, includeMaxTokens);
                }
                catch (AiHttpException ex)
                {
                    last = ex;
                    // 个别兼容网关不认 max_tokens：降级是换参数重发，不是网络重试，
                    // 不能占用 2 次重试额度，否则末次才遇到 400 时降级永远不会真正发出
                    if (ex.Code == 400 && includeMaxTokens && !maxTokensRetried &&
                        ex.Message.IndexOf("max_tokens", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        includeMaxTokens = false;
                        maxTokensRetried = true;
                        continue;
                    }
                    if (!ex.Retryable || retries >= 2) throw;
                    retries++;
                }
            }
        }

        private static AiReply SendOnce(AiConfig cfg, string userPrompt, string systemPrompt,
                                        Action<HttpWebRequest> onRequest, int timeoutMs, bool includeMaxTokens)
        {
            string body = "{\"model\":\"" + JsonText.Escape(cfg.Model) + "\"," +
                          "\"messages\":[" +
                          "{\"role\":\"system\",\"content\":\"" + JsonText.Escape(systemPrompt) + "\"}," +
                          "{\"role\":\"user\",\"content\":\"" + JsonText.Escape(userPrompt) + "\"}]," +
                          "\"temperature\":0.3,\"stream\":false" +
                          (includeMaxTokens ? ",\"max_tokens\":" + MaxOutputTokens : "") + "}";

            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(cfg.Endpoint);
            req.Method = "POST";
            req.ContentType = "application/json";
            req.Accept = "application/json";
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.UserAgent = "PCTools/1.0";
            // 端点校验只覆盖初始 URL：允许自动重定向时，30x 会把请求（连同请求体）
            // 导向一个从未校验过的地址。这里禁止跳转，由下面的 3xx 分支明确报错
            req.AllowAutoRedirect = false;
            if (!string.IsNullOrEmpty(cfg.ApiKey))
                req.Headers["Authorization"] = "Bearer " + cfg.ApiKey;
            Track(req);
            try
            {
                if (onRequest != null)
                {
                    try { onRequest(req); }
                    catch { }
                }

                byte[] data = Encoding.UTF8.GetBytes(body);
                req.ContentLength = data.Length;
                using (Stream s = req.GetRequestStream()) s.Write(data, 0, data.Length);

                string resp;
                try
                {
                    using (WebResponse r = req.GetResponse())
                    {
                        HttpWebResponse hr = r as HttpWebResponse;
                        int sc = hr == null ? 0 : (int)hr.StatusCode;
                        if (sc >= 300 && sc < 400)
                            throw new AiHttpException(sc,
                                "接口返回了重定向（" + sc + "）：请把接口地址改成最终地址（不经过跳转）。", false);
                        resp = ReadResponse(r);
                    }
                }
                catch (WebException we)
                {
                    string detail = "";
                    int code = 0;
                    // 错误响应对象本身也持有连接，读完正文必须释放；只关流会留下响应与连接引用
                    WebResponse errResp = we.Response;
                    try
                    {
                        HttpWebResponse hr = errResp as HttpWebResponse;
                        if (hr != null) code = (int)hr.StatusCode;
                        // 错误体同样要有上限：异常或恶意网关可以用一个超大响应把内存吃穿
                        if (errResp != null) detail = ReadErrorBody(errResp);
                    }
                    catch { }
                    finally
                    {
                        if (errResp != null) { try { errResp.Close(); } catch { } }
                    }
                    // 取消（窗口关闭 / 进程退出）不是可重试错误，重试会白烧 Token
                    bool retry = code == 429 || code >= 500 ||
                                 (code == 0 && we.Status != WebExceptionStatus.RequestCanceled &&
                                  we.Status != WebExceptionStatus.Timeout);
                    throw new AiHttpException(code, DescribeHttpError(we, detail, code), retry);
                }

                AiReply reply = new AiReply();
                // 优先取 choices[].message.content：网关/错误体里若先出现别的 "content"，
                // 取第一个会拿到无关文本。定位不到再退回全文搜索
                int mi = resp.IndexOf("\"message\"", StringComparison.Ordinal);
                string content = mi >= 0 ? JsonText.ExtractString(resp.Substring(mi), "content") : "";
                if (string.IsNullOrEmpty(content)) content = JsonText.ExtractString(resp, "content");
                if (string.IsNullOrEmpty(content)) content = JsonText.ExtractJoinedStrings(resp, "content");
                if (string.IsNullOrEmpty(content))
                    reply.Text = "接口返回内容无法解析：" + (resp.Length > 400 ? resp.Substring(0, 400) : resp);
                else
                {
                    reply.Text = content.Trim();
                    reply.Parsed = true;
                }
                JsonText.ReadUsage(resp, out reply.InTokens, out reply.OutTokens, out reply.HasUsage);
                reply.Finish = JsonText.ExtractString(resp, "finish_reason");
                return reply;
            }
            finally { Untrack(req); }
        }

        // 响应体读取加上限：正常 chat 响应远小于 2MB；异常或恶意网关返回超大流时，
        // 无上限的 ReadToEnd 会把内存吃穿（相当于把本地内存暴露给远端）
        private const long MaxResponseBytes = 2 * 1024 * 1024;

        private static string ReadResponse(WebResponse r)
        {
            using (Stream s = r.GetResponseStream())
            {
                if (s == null) return "";
                byte[] buf = new byte[8192];
                long total = 0;
                using (MemoryStream ms = new MemoryStream())
                {
                    while (true)
                    {
                        int n = s.Read(buf, 0, buf.Length);
                        if (n <= 0) break;
                        total += n;
                        if (total > MaxResponseBytes)
                            throw new Exception("接口响应超过 " + (MaxResponseBytes / 1024 / 1024) +
                                                " MB 上限，已中止读取（请检查接口地址是否正确）");
                        ms.Write(buf, 0, n);
                    }
                    return Encoding.UTF8.GetString(ms.ToArray());
                }
            }
        }

        // 错误响应体只用于提取 message，不需要完整正文：上限 64KB，
        // 防止异常/恶意网关用一个超大错误体把内存吃穿
        private const long MaxErrorBytes = 64 * 1024;

        private static string ReadErrorBody(WebResponse r)
        {
            try
            {
                using (Stream s = r.GetResponseStream())
                {
                    if (s == null) return "";
                    byte[] buf = new byte[8192];
                    long total = 0;
                    using (MemoryStream ms = new MemoryStream())
                    {
                        while (total < MaxErrorBytes)
                        {
                            int n = s.Read(buf, 0, buf.Length);
                            if (n <= 0) break;
                            ms.Write(buf, 0, n);
                            total += n;
                        }
                        return Encoding.UTF8.GetString(ms.ToArray());
                    }
                }
            }
            catch { return ""; }
        }

        private static string DescribeHttpError(WebException we, string detail, int code)
        {
            string msg = "";
            if (!string.IsNullOrEmpty(detail))
            {
                msg = JsonText.ExtractString(detail, "message");
                if (string.IsNullOrEmpty(msg)) msg = detail.Length > 300 ? detail.Substring(0, 300) : detail;
            }
            if (code == 401) return "认证失败（401）：API Key 无效或未授权。" + (msg.Length > 0 ? "\n" + msg : "");
            if (code == 403) return "访问被拒绝（403）：Key 无该模型权限。" + (msg.Length > 0 ? "\n" + msg : "");
            if (code == 404) return "地址不存在（404）：请检查接口地址是否完整（通常以 /v1/chat/completions 结尾）。";
            if (code >= 300 && code < 400)
                return "接口返回了重定向（" + code + "）：请把接口地址改成最终地址（不经过跳转）。" +
                       (msg.Length > 0 ? "\n" + msg : "");
            if (code == 429) return "请求过于频繁（429）：请稍后再试。" + (msg.Length > 0 ? "\n" + msg : "");
            if (code >= 500) return "服务端错误（" + code + "）：可稍后重试。" + (msg.Length > 0 ? "\n" + msg : "");
            if (code > 0) return "请求失败（" + code + "）：" + msg;
            return "网络请求失败：" + we.Message + "（请检查接口地址、代理与网络连接）";
        }
    }

    internal static class JsonText
    {
        internal static string Escape(string s)
        {
            if (s == null) return "";
            StringBuilder sb = new StringBuilder(s.Length + 16);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else if (c >= 0xD800 && c <= 0xDBFF)
                        {
                            // 孤立代理项来自损坏编码的文件名/命令行，原样输出会构成非法 JSON
                            // 被网关 400 拒掉：能配对就成对输出，配不上替换成 U+FFFD
                            if (i + 1 < s.Length && s[i + 1] >= 0xDC00 && s[i + 1] <= 0xDFFF)
                            {
                                sb.Append(c).Append(s[i + 1]);
                                i++;
                            }
                            else sb.Append('\uFFFD');
                        }
                        else if (c >= 0xDC00 && c <= 0xDFFF) sb.Append('\uFFFD');   // 孤立低代理项
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        // 取 “key”: 123 的整数值；从最后一次出现向前找（usage 对象固定在响应 JSON 的尾部），
        // 模型正文里若回显了 "prompt_tokens":… 样例，也不会被当成真实用量计入
        internal static bool TryExtractInt(string json, string key, out int value)
        {
            value = 0;
            if (string.IsNullOrEmpty(json)) return false;
            string token = "\"" + key + "\"";
            int i = json.LastIndexOf(token, StringComparison.Ordinal);
            while (i >= 0)
            {
                int p = i + token.Length;
                while (p < json.Length && char.IsWhiteSpace(json[p])) p++;
                if (p < json.Length && json[p] == ':') p++;
                while (p < json.Length && char.IsWhiteSpace(json[p])) p++;
                int start = p;
                if (p < json.Length && json[p] == '-') p++;
                int digits = p;
                while (p < json.Length && json[p] >= '0' && json[p] <= '9') p++;
                if (p > digits)
                {
                    int v;
                    if (int.TryParse(json.Substring(start, p - start), out v)) { value = v; return true; }
                }
                i = i > 0 ? json.LastIndexOf(token, i - 1, StringComparison.Ordinal) : -1;
            }
            return false;
        }

        // 读取 usage：OpenAI 系是 prompt_tokens / completion_tokens，部分网关用 input/output_tokens
        internal static void ReadUsage(string json, out int inTokens, out int outTokens, out bool hasUsage)
        {
            inTokens = 0;
            outTokens = 0;
            hasUsage = false;
            int v;
            // 网关偶尔回负数；累计用量是只增的，不能让畸形数据把它减成负值
            // （TryExtractInt 从末次出现向前找，正文回显的 usage 样例不会误计）
            if (TryExtractInt(json, "prompt_tokens", out v)) { inTokens = Math.Max(0, v); hasUsage = true; }
            else if (TryExtractInt(json, "input_tokens", out v)) { inTokens = Math.Max(0, v); hasUsage = true; }
            if (TryExtractInt(json, "completion_tokens", out v)) { outTokens = Math.Max(0, v); hasUsage = true; }
            else if (TryExtractInt(json, "output_tokens", out v)) { outTokens = Math.Max(0, v); hasUsage = true; }
        }

        // 取第一个 “key” : "value" 中的字符串值，支持常见转义
        internal static string ExtractString(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return "";
            string token = "\"" + key + "\"";
            int i = json.IndexOf(token, StringComparison.Ordinal);
            while (i >= 0)
            {
                int p = i + token.Length;
                while (p < json.Length && char.IsWhiteSpace(json[p])) p++;
                if (p < json.Length && json[p] == ':') p++;
                while (p < json.Length && char.IsWhiteSpace(json[p])) p++;
                if (p < json.Length && json[p] == '"')
                {
                    p++;
                    StringBuilder sb = new StringBuilder();
                    while (p < json.Length)
                    {
                        char c = json[p];
                        if (c == '\\' && p + 1 < json.Length)
                        {
                            char n = json[p + 1];
                            p += 2;
                            switch (n)
                            {
                                case 'n': sb.Append('\n'); break;
                                case 'r': sb.Append('\r'); break;
                                case 't': sb.Append('\t'); break;
                                case 'b': sb.Append('\b'); break;
                                case 'f': sb.Append('\f'); break;
                                case '"': sb.Append('"'); break;
                                case '\\': sb.Append('\\'); break;
                                case '/': sb.Append('/'); break;
                                case 'u':
                                    // 四个字符不一定是合法十六进制（模型偶尔输出 "\u12G4"），
                                    // 直接 Convert.ToInt32 会抛 FormatException 把整段响应丢掉；
                                    // 解析失败时把 \u 两个字符按原文补回（其余四个字符由外层
                                    // 循环按普通字符继续输出），保证内容不丢字符
                                    if (p + 3 < json.Length)
                                    {
                                        int cp;
                                        if (int.TryParse(json.Substring(p, 4),
                                                         System.Globalization.NumberStyles.HexNumber,
                                                         System.Globalization.CultureInfo.InvariantCulture,
                                                         out cp))
                                        {
                                            sb.Append((char)cp);
                                            p += 4;
                                        }
                                        else sb.Append("\\u");
                                    }
                                    else sb.Append("\\u");
                                    break;
                                default: sb.Append(n); break;
                            }
                            continue;
                        }
                        if (c == '"') return sb.ToString();
                        sb.Append(c);
                        p++;
                    }
                    return sb.ToString();
                }
                i = json.IndexOf(token, i + token.Length, StringComparison.Ordinal);
            }
            return "";
        }

        // 兼容个别网关把 content 返回成数组：[{"type":"text","text":"…"},…] 或 ["…","…"]。
        // 逐段抽取并用换行拼接；取不到任何片段时返回空串，由上层按解析失败处理
        internal static string ExtractJoinedStrings(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return "";
            string token = "\"" + key + "\"";
            int i = json.IndexOf(token, StringComparison.Ordinal);
            while (i >= 0)
            {
                int p = i + token.Length;
                while (p < json.Length && char.IsWhiteSpace(json[p])) p++;
                if (p < json.Length && json[p] == ':') p++;
                while (p < json.Length && char.IsWhiteSpace(json[p])) p++;
                if (p < json.Length && json[p] == '[')
                {
                    int end = json.IndexOf(']', p + 1);
                    if (end > p)
                    {
                        string seg = json.Substring(p + 1, end - p - 1);
                        StringBuilder sb = new StringBuilder();
                        int from = 0;
                        // 优先取对象数组里的 "text" 字段（多模态网关的分段 content）
                        while (from < seg.Length)
                        {
                            string piece = ExtractString(seg.Substring(from), "text");
                            if (piece.Length == 0) break;
                            if (sb.Length > 0) sb.Append('\n');
                            sb.Append(piece);
                            int used = seg.IndexOf("\"text\"", from, StringComparison.Ordinal);
                            if (used < 0) break;
                            from = used + 6;
                        }
                        if (sb.Length == 0)
                        {
                            // 退回纯字符串数组 ["a","b"]：抽段内全部字符串字面量
                            //（转义序列保留原文，仅边界判断正确，兜底够用）
                            int q = 0;
                            while (q < seg.Length)
                            {
                                if (seg[q] != '"') { q++; continue; }
                                int close = q + 1;
                                while (close < seg.Length)
                                {
                                    if (seg[close] == '\\') { close += 2; continue; }
                                    if (seg[close] == '"') break;
                                    close++;
                                }
                                if (close >= seg.Length) break;
                                if (sb.Length > 0) sb.Append('\n');
                                sb.Append(seg.Substring(q + 1, close - q - 1));
                                q = close + 1;
                            }
                        }
                        if (sb.Length > 0) return sb.ToString();
                    }
                }
                i = json.IndexOf(token, i + token.Length, StringComparison.Ordinal);
            }
            return "";
        }
    }

    // ------------------------------------------------------------------ AI dialogs
    internal static class DialogShell
    {
        internal static void AttachTheme(FrameworkElement view)
        {
            if (Application.Current == null || Application.Current.Resources == null) return;
            ResourceDictionary local = new ResourceDictionary();
            foreach (object key in Application.Current.Resources.Keys)
                local[key] = Application.Current.Resources[key];
            view.Resources.MergedDictionaries.Add(local);
        }

        internal static Window Create(string title, double width, double height)
        {
            Window w = new Window();
            w.Title = title;
            w.Width = width;
            if (height > 0) w.Height = height;
            else w.SizeToContent = SizeToContent.Height;
            w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            return w;
        }

        // 弹窗统一初始化。
        // 注意：AllowsTransparency=true 的分层窗口会强制关闭 ClearType（只能用灰度抗锯齿），
        // 中文小字会发虚、带锯齿，所以这里改为不透明窗口 + WindowChrome 拿 DWM 阴影与圆角。
        internal static void Prepare(Window w, FrameworkElement view)
        {
            w.WindowStyle = WindowStyle.None;
            w.AllowsTransparency = false;
            w.ResizeMode = ResizeMode.NoResize;
            w.ShowInTaskbar = false;
            w.FontFamily = AppFont.Ui;
            w.FontSize = 12.5;
            w.UseLayoutRounding = true;
            w.SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(w, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(w, TextRenderingMode.ClearType);
            w.Background = SolidTheme("DialogBg");

            // WindowChrome：让无边框窗口仍带 DWM 阴影与 Win11 圆角（非分层窗口下文字才是 ClearType）
            WindowChrome chrome = new WindowChrome();
            chrome.CaptionHeight = 0;
            chrome.ResizeBorderThickness = new Thickness(0);
            chrome.CornerRadius = new CornerRadius(0);
            chrome.GlassFrameThickness = new Thickness(0);
            chrome.UseAeroCaptionButtons = false;
            WindowChrome.SetWindowChrome(w, chrome);


            // 卡片外框交给窗口本身：去掉透明边距、圆角与投影，否则不透明窗口下会露出灰边
            Border card = view as Border;
            if (card != null)
            {
                card.Margin = new Thickness(0);
                card.CornerRadius = new CornerRadius(0);
                card.BorderThickness = new Thickness(0);
                card.Background = Brushes.Transparent;
                card.Effect = null;
            }

            // 右上角统一加关闭按钮（四个弹窗共用）
            Grid host = new Grid();
            host.Children.Add(view);
            host.Children.Add(MakeCloseButton(w));
            w.Content = host;

            w.SourceInitialized += delegate { ApplyFrame(w); };
            EnableDrag(w, view);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorRect { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfoLite
        {
            public int Size;
            public MonitorRect Monitor;
            public MonitorRect Work;
            public uint Flags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoLite info);

        // 窗口所在显示器的工作区（DIP）。主窗在副屏时 SystemParameters.WorkArea
        // 仍是主屏尺寸，按它约束弹窗宽度会把窗口挪到主屏或直接错位
        internal static Rect WorkAreaOf(Window w)
        {
            try
            {
                if (w != null)
                {
                    IntPtr h = new WindowInteropHelper(w).Handle;
                    if (h != IntPtr.Zero)
                    {
                        IntPtr mon = MonitorFromWindow(h, 2);      // MONITOR_DEFAULTTONEAREST
                        if (mon != IntPtr.Zero)
                        {
                            MonitorInfoLite mi = new MonitorInfoLite();
                            mi.Size = Marshal.SizeOf(typeof(MonitorInfoLite));
                            if (GetMonitorInfo(mon, ref mi))
                            {
                                PresentationSource src = PresentationSource.FromVisual(w);
                                if (src != null && src.CompositionTarget != null)
                                {
                                    Matrix m = src.CompositionTarget.TransformFromDevice;
                                    Point tl = m.Transform(new Point(mi.Work.Left, mi.Work.Top));
                                    Point br = m.Transform(new Point(mi.Work.Right, mi.Work.Bottom));
                                    return new Rect(tl, br);
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return SystemParameters.WorkArea;
        }

        private static ControlTemplate closeTemplate;

        private static Button MakeCloseButton(Window w)
        {
            if (closeTemplate == null)
            {
                string xaml = @"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
                 TargetType='Button'>
  <Border x:Name='bd' CornerRadius='6' Background='Transparent'>
    <Path Width='10' Height='10' Stretch='None'
          Stroke='{Binding RelativeSource={RelativeSource TemplatedParent}, Path=Foreground}'
          StrokeThickness='1.2' Data='M 0.5,0.5 L 9.5,9.5 M 9.5,0.5 L 0.5,9.5'/>
  </Border>
  <ControlTemplate.Triggers>
    <Trigger Property='IsMouseOver' Value='True'>
      <Setter TargetName='bd' Property='Background' Value='{DynamicResource HoverBg}'/>
      <Setter Property='Foreground' Value='{DynamicResource TextPrimary}'/>
    </Trigger>
    <Trigger Property='IsPressed' Value='True'>
      <Setter TargetName='bd' Property='Background' Value='{DynamicResource PressBg}'/>
    </Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>";
                closeTemplate = (ControlTemplate)XamlReader.Parse(xaml);
            }

            Button b = new Button();
            b.Width = 30;
            b.Height = 30;
            b.Cursor = Cursors.Hand;
            b.ToolTip = "关闭";
            b.HorizontalAlignment = HorizontalAlignment.Right;
            b.VerticalAlignment = VerticalAlignment.Top;
            b.Margin = new Thickness(0, 8, 8, 0);
            b.Focusable = false;
            b.Template = closeTemplate;
            b.SetResourceReference(Control.ForegroundProperty, "TextSecondary");
            b.Click += delegate { w.Close(); };
            return b;
        }

        // 主题里的背景色带 0.99 透明度，直接用作不透明窗口背景会偏灰，这里取实色
        internal static Brush SolidTheme(string key)
        {
            try
            {
                if (Application.Current != null)
                {
                    SolidColorBrush b = Application.Current.Resources[key] as SolidColorBrush;
                    if (b != null) return new SolidColorBrush(Color.FromRgb(b.Color.R, b.Color.G, b.Color.B));
                }
            }
            catch { }
            return Brushes.White;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        internal static void ApplyFrame(Window w)
        {
            try
            {
                IntPtr h = new WindowInteropHelper(w).Handle;
                int v = 2;
                DwmSetWindowAttribute(h, 33, ref v, 4);   // 圆角
                v = ThemeManager.IsLight() ? 0 : 1;
                DwmSetWindowAttribute(h, 20, ref v, 4);   // 深色边框
            }
            catch { }
        }

        // 无边框窗口：按住空白处即可拖动
        internal static void EnableDrag(Window w, FrameworkElement view)
        {
            view.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ButtonState != MouseButtonState.Pressed) return;
                if (IsInteractive(e.OriginalSource as DependencyObject)) return;
                try { w.DragMove(); }
                catch { }
            };
        }

        // 主窗口标题栏与弹窗拖动共用：覆盖所有可交互控件（含滚动条与下拉框），
        // 否则从这些控件上按下会被误当成拖动窗口
        internal static bool IsInteractive(DependencyObject o)
        {
            while (o != null)
            {
                if (o is Button || o is TextBox || o is PasswordBox
                    || o is System.Windows.Controls.Primitives.ScrollBar
                    || o is ScrollViewer || o is ComboBox)
                    return true;
                o = VisualTreeHelper.GetParent(o);
            }
            return false;
        }
    }

    // ------------------------------------------------------------------ Confirm / info dialog
    // 确认框的三种结果；None 表示既没点按钮也没按 Esc（例如直接点右上角关闭）
    public enum ConfirmChoice { None, Cancel, Primary, Third }

    public class ConfirmWindow : Window
    {
        public ConfirmChoice Choice { get; private set; }

        // thirdText 为 null 时第三个按钮隐藏，形态与两按钮版本完全一致
        internal ConfirmWindow(string title, string subtitle, string message, string okText,
                               string thirdText, bool infoOnly)
        {
            Title = title;
            Width = 540;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            // 标题栏 + 图标区 + 按钮约 190px；内容区按工作区留余量，125% 缩放下按钮仍可点
            double cap = Math.Max(260, SystemParameters.WorkArea.Height * 0.72);
            MaxHeight = cap;

            FrameworkElement view = (FrameworkElement)XamlReader.Parse(Views.Confirm);
            DialogShell.AttachTheme(view);
            DialogShell.Prepare(this, view);
            ScrollViewer scroll = view.FindName("DlgScroll") as ScrollViewer;
            if (scroll != null) scroll.MaxHeight = Math.Max(140, cap - 190);
            // 构造期还没有 HWND，上面只能按主屏兜底；窗口加载后按所在显示器重算，
            // 主窗在副屏/竖屏时避免底部按钮被顶出可视区
            Loaded += delegate
            {
                Rect wa = DialogShell.WorkAreaOf(this);
                if (wa.IsEmpty || wa.Height <= 0) return;
                double realCap = Math.Max(260, wa.Height * 0.72);
                MaxHeight = realCap;
                if (scroll != null) scroll.MaxHeight = Math.Max(140, realCap - 190);
            };

            ((TextBlock)view.FindName("DlgTitle")).Text = title;
            ((TextBlock)view.FindName("DlgSubtitle")).Text = subtitle;
            ((TextBlock)view.FindName("DlgText")).Text = message;
            ((TextBlock)view.FindName("DlgGlyph")).Text = infoOnly ? "i" : "!";

            Button no = (Button)view.FindName("BtnNo");
            Button yes = (Button)view.FindName("BtnYes");
            Button third = (Button)view.FindName("BtnThird");
            yes.Content = okText;
            if (thirdText == null) third.Visibility = Visibility.Collapsed;
            else
            {
                third.Content = thirdText;
                third.Visibility = Visibility.Visible;   // XAML 里默认是 Collapsed，必须显式打开
            }
            if (infoOnly)
            {
                no.Visibility = Visibility.Collapsed;
                third.Visibility = Visibility.Collapsed;
                yes.IsDefault = true;
            }
            no.Click += delegate { Choice = ConfirmChoice.Cancel; Close(); };
            yes.Click += delegate { Choice = ConfirmChoice.Primary; Close(); };
            third.Click += delegate { Choice = ConfirmChoice.Third; Close(); };
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { Choice = ConfirmChoice.Cancel; Close(); }
            };
        }

        // 两按钮版本转发到五参构造（原有调用点零改动）
        internal ConfirmWindow(string title, string subtitle, string message, string okText, bool infoOnly)
            : this(title, subtitle, message, okText, null, infoOnly)
        {
        }
    }

    internal static class Dialogs
    {
        // 无可用属主窗口（如启动早期提权失败提示，主窗尚未创建）时置顶显示：
        // 无主对话框不抢焦点层级，可能被别的窗口压在底下，用户以为程序没反应
        private static void Place(ConfirmWindow w, Window owner)
        {
            if (owner != null && owner.IsVisible) w.Owner = owner;
            else w.Topmost = true;
        }

        internal static bool Confirm(Window owner, string message, string okText)
        {
            ConfirmWindow w = new ConfirmWindow("确认操作", "此操作会立即生效，请确认后继续", message, okText, false);
            Place(w, owner);
            w.ShowDialog();
            return w.Choice == ConfirmChoice.Primary;
        }

        // 三选一：副标题由调用方给（例如「已有 12 项，待补全 28 项」）
        internal static ConfirmChoice Confirm3(Window owner, string subtitle, string message,
                                               string okText, string thirdText)
        {
            ConfirmWindow w = new ConfirmWindow("确认操作", subtitle, message, okText, thirdText, false);
            Place(w, owner);
            w.ShowDialog();
            return w.Choice;
        }

        internal static void Info(Window owner, string title, string message)
        {
            ConfirmWindow w = new ConfirmWindow(title, "请查看以下信息", message, "知道了", true);
            Place(w, owner);
            w.ShowDialog();
        }
    }

    // 配置窗口左侧列表的行模型（重建 ItemsSource 即可刷新「使用中」标记）
    public class AiProfileRow
    {
        public string Title { get; set; }
        public string Sub { get; set; }
        public Visibility ActiveTag { get; set; }
        public string Stats { get; set; }
        public string StatsTip { get; set; }
    }

    public class AiSettingsWindow : Window
    {
        internal AiConfig Config { get; private set; }
        // 只有点了「保存」并真正写盘成功才为 true，供调用方决定提示什么
        internal bool Saved { get; private set; }

        private TextBox fName;
        private TextBox fEndpoint;
        private TextBox fKey;
        private TextBox fModel;
        private ListBox list;
        private TextBlock status;
        private StackPanel statusHost;
        private Button btnTest;
        private Button btnNew;
        private Button btnDel;
        private Button btnUse;
        private Button btnSaveOne;

        private List<AiConfig> profiles;
        // 打开窗口时的 地址/模型/Key 快照：只有这三项变了才清空该配置的累计用量
        private Dictionary<int, AiConfig> original;
        private Dictionary<int, AiUsageStore.Row> usageRows = new Dictionary<int, AiUsageStore.Row>();
        private int active;
        private int editing = -1;
        private bool syncing;
        private string savedKey = "";
        private bool keyMasked;
        // 用户点了「清除」：保存时写空 Key（区别于「打码未改」的沿用语义）
        private bool keyCleared;
        // 非空表示打开窗口时没读到库里的配置：此时整表保存会抹掉其余几套，禁止保存
        private string loadError;
        private int originalActive;
        // 「测试连接」的在途请求：关窗时要 Abort，否则线程会空等到 90 秒超时
        // 后台测试线程写入、窗口关闭回调读取，需要 volatile 保证可见性
        private volatile HttpWebRequest testReq;
        // Closed 事件可能早于/晚于 onRequest 回调：两边都要检查，封住重试间隙的竞态
        private volatile bool closed;
        // 保存成功后的 Close 是主动流程，不再弹「放弃修改」确认
        private bool suppressCloseConfirm;

        internal AiSettingsWindow(AiConfig cfg)
        {
            Title = "配置 AI";
            Width = 720;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            FrameworkElement view = (FrameworkElement)XamlReader.Parse(Views.AiSettings);
            DialogShell.AttachTheme(view);
            DialogShell.Prepare(this, view);

            fName = (TextBox)view.FindName("FldName");
            fEndpoint = (TextBox)view.FindName("FldEndpoint");
            fKey = (TextBox)view.FindName("FldKey");
            fModel = (TextBox)view.FindName("FldModel");
            list = (ListBox)view.FindName("FldList");
            status = (TextBlock)view.FindName("AiStatus");
            statusHost = (StackPanel)view.FindName("AiStatusHost");
            statusHost.Visibility = Visibility.Collapsed;
            btnTest = (Button)view.FindName("BtnTest");
            btnNew = (Button)view.FindName("BtnNew");
            btnDel = (Button)view.FindName("BtnDel");
            btnUse = (Button)view.FindName("BtnUse");
            btnSaveOne = (Button)view.FindName("BtnSaveOne");
            Button btnClearKey = (Button)view.FindName("BtnClearKey");

            profiles = AiConfig.LoadAll(out active, out loadError);
            if (profiles.Count == 0)
            {
                AiConfig fresh = new AiConfig();
                fresh.Index = 1;
                fresh.Name = "配置 1";
                profiles.Add(fresh);
            }
            Config = profiles[0];
            TakeSnapshot();

            int start = 0;
            int want = cfg == null ? active : cfg.Index;
            for (int i = 0; i < profiles.Count; i++) if (profiles[i].Index == want) start = i;

            // 打码串不可直接编辑：获得焦点时先清空，用户敲下的第一个字符才不会被打码文本吞掉
            fKey.GotKeyboardFocus += delegate { ClearMaskedKey(); };
            fKey.PreviewMouseLeftButtonDown += delegate { ClearMaskedKey(); };
            fKey.TextChanged += delegate
            {
                // 点过「清除」之后又输入了新 Key：清除标记必须让位，
                // 否则 EffectiveKey 一直返回空串，用户刚输入的 Key 会被静默丢弃
                if (!syncing && keyCleared && fKey.Text.Trim().Length > 0) keyCleared = false;
            };

            list.SelectionChanged += delegate
            {
                if (syncing) return;
                if (list.SelectedIndex < 0 || list.SelectedIndex == editing) return;
                CommitFields();
                SelectProfile(list.SelectedIndex);
            };

            btnNew.Click += delegate { AddProfile(); };
            btnDel.Click += delegate { RemoveProfile(); };
            btnUse.Click += delegate { UseProfile(); };
            if (btnSaveOne != null) btnSaveOne.Click += delegate { SaveCurrentProfile(); };
            if (btnClearKey != null) btnClearKey.Click += delegate { ClearKey(); };
            // 所有关闭入口（取消、Esc、右上角 X）统一在 Closing 里做未保存确认，
            // 不再出现「有的路径弹确认、有的静默丢数据」
            Closing += delegate(object s, CancelEventArgs e)
            {
                if (suppressCloseConfirm) return;
                if (!ConfirmDiscard()) e.Cancel = true;
            };
            ((Button)view.FindName("BtnCancel")).Click += delegate { Close(); };
            ((Button)view.FindName("BtnSave")).Click += delegate { SaveAndClose(); };
            btnTest.Click += delegate { TestAsync(); };
            // 关窗时中止在途的连通性测试：否则后台线程会一直等到 90 秒超时，
            // 窗口都关了还在占着连接
            Closed += delegate
            {
                closed = true;
                HttpWebRequest r = testReq;
                if (r != null) { try { r.Abort(); } catch { } }
            };
            KeyDown += delegate(object s2, KeyEventArgs e)
            {
                if (e.Key != Key.Escape) return;
                Close();   // 未保存确认统一由 Closing 处理
            };

            RebuildList();
            SelectProfile(start);
            // 读取失败时界面里只有一份内存默认值：必须明确告知不能保存，
            // 否则用户一填一存就把库里另外几套配置整表覆盖掉
            if (!string.IsNullOrEmpty(loadError))
            {
                status.Text = "读取现有配置失败：" + loadError +
                              "\n为避免覆盖库中已保存的配置，本次窗口不允许保存。";
                statusHost.Visibility = Visibility.Visible;
            }
        }

        private void RebuildList()
        {
            usageRows = AiUsageStore.LoadAll();
            List<AiProfileRow> rows = new List<AiProfileRow>();
            for (int i = 0; i < profiles.Count; i++)
            {
                AiConfig c = profiles[i];
                AiProfileRow r = new AiProfileRow();
                r.Title = c.Title;
                r.Sub = string.IsNullOrEmpty(c.Endpoint) ? "未填写接口地址" : c.Endpoint;
                r.ActiveTag = c.Index == active ? Visibility.Visible : Visibility.Collapsed;
                r.Stats = FormatStats(c.Index);
                r.StatsTip = FormatStatsTip(c.Index);
                rows.Add(r);
            }
            syncing = true;
            list.ItemsSource = rows;
            list.SelectedIndex = editing >= 0 && editing < rows.Count ? editing : 0;
            syncing = false;
            btnNew.IsEnabled = profiles.Count < AiConfig.MaxProfiles;
            btnDel.IsEnabled = profiles.Count > 1;
        }

        private void SelectProfile(int i)
        {
            if (i < 0 || i >= profiles.Count) return;
            editing = i;
            AiConfig c = profiles[i];
            syncing = true;
            fName.Text = c.Name;
            fEndpoint.Text = c.Endpoint;
            fModel.Text = c.Model;
            savedKey = c.ApiKey;
            keyMasked = !string.IsNullOrEmpty(savedKey);
            keyCleared = false;
            fKey.Text = keyMasked ? MaskKey(savedKey) : "";
            list.SelectedIndex = i;
            syncing = false;

            // 上一套配置留下的告警（Key 解不开 / 已标记清除 / 保存失败）必须随切换清掉，
            // 否则切到正常配置后那条红字还挂在下面，看起来像当前这套也有问题
            status.Text = "";
            statusHost.Visibility = Visibility.Collapsed;

            if (c.KeyUnreadable)
            {
                // 密文还在，只是当前账户解不开：必须说清楚，否则用户会以为 Key 丢了
                status.Text = "已保存的 Key 在当前 Windows 账户下无法解密（换过用户/机器或 DPAPI 数据损坏）。" +
                              "重新输入 Key 即可覆盖；不输入则保留原密文不动。";
                statusHost.Visibility = Visibility.Visible;
                fKey.ToolTip = "原 Key 仍以密文保存在库里，但当前账户解不开；请输入新 Key 覆盖。";
            }
            else
            {
                fKey.ToolTip = keyMasked
                    ? "已保存的 Key（已打码）。点进来输入会自动清空并替换为新 Key。"
                    : "与程序同目录的 PCToolsAI.sqlite（DPAPI 按当前用户加密存储，文件权限亦已限制为当前用户）";
            }
            btnUse.IsEnabled = profiles[i].Index != active;

            // 读取失败是窗口级状态，不属于某一套配置：切换后必须重新挂出来，
            // 否则用户只会在点保存时看到「禁止保存」，不知道是打开窗口时就没读到
            if (!string.IsNullOrEmpty(loadError))
            {
                status.Text = "读取现有配置失败（本窗口禁止保存）：" + loadError;
                statusHost.Visibility = Visibility.Visible;
            }
        }

        // 明确清除已保存的 Key
        private void ClearKey()
        {
            // 解不开的密文同样要允许清除：否则换账户/换机器之后，那份 Key 永远删不掉，
            // 用户以为已经清掉，密文其实还在库里
            bool unreadable = editing >= 0 && editing < profiles.Count && profiles[editing].KeyUnreadable;
            if (string.IsNullOrEmpty(savedKey) && !unreadable)
            {
                status.Text = "当前配置没有保存过 Key。";
                statusHost.Visibility = Visibility.Visible;
                return;
            }
            keyCleared = true;
            keyMasked = false;
            savedKey = "";
            syncing = true;
            fKey.Text = "";
            syncing = false;
            fKey.ToolTip = "点「保存」后清除这套配置的 API Key";
            status.Text = "已标记清除 Key，点「保存」后生效。";
            statusHost.Visibility = Visibility.Visible;
        }

        // 打码状态下点进输入框就清空，等用户输入新的 Key
        private void ClearMaskedKey()
        {
            if (!keyMasked) return;
            syncing = true;
            fKey.Text = "";
            syncing = false;
            keyMasked = false;
            fKey.ToolTip = "输入新的 API Key；留空表示保留已保存的 Key";
        }

        // 打码状态或留空都表示「沿用已保存的 Key」：点进输入框再离开不应把 Key 抹掉。
        // 输入框里有非空文本时永远以文本为准（含先点清除、再录入新 Key 的情形）；
        // 只有「明确清除且没有输入新内容」才返回空串
        private string EffectiveKey()
        {
            if (keyMasked) return savedKey;
            string t = fKey.Text.Trim();
            if (t.Length > 0) return t;
            if (keyCleared) return "";
            return savedKey;
        }

        private void CommitFields()
        {
            if (editing < 0 || editing >= profiles.Count) return;
            AiConfig c = profiles[editing];
            c.Name = fName.Text.Trim();
            c.Endpoint = fEndpoint.Text.Trim();
            c.Model = fModel.Text.Trim();
            c.ApiKey = EffectiveKey();
            // 录入了新 Key 或明确清除时，这套不再处于「密文解不开」状态
            if (keyCleared || c.ApiKey.Length > 0) c.KeyUnreadable = false;
        }

        // 当前编辑内容与打开时的快照是否有差异（不含「设为当前」）
        private bool Dirty()
        {
            CommitFields();
            if (profiles.Count != original.Count) return true;
            if (active != originalActive) return true;
            foreach (AiConfig c in profiles)
            {
                AiConfig o;
                if (!original.TryGetValue(c.Index, out o)) return true;
                if (o.Name != c.Name || o.Endpoint != c.Endpoint ||
                    o.Model != c.Model || o.ApiKey != c.ApiKey) return true;
            }
            return false;
        }

        private bool ConfirmDiscard()
        {
            if (!Dirty()) return true;
            return Dialogs.Confirm(this, "有未保存的修改，确定放弃并关闭吗？", "放弃修改");
        }

        private void AddProfile()
        {
            CommitFields();
            if (profiles.Count >= AiConfig.MaxProfiles)
            {
                status.Text = "最多只能保存 " + AiConfig.MaxProfiles + " 套配置，请先删除一套。";
                statusHost.Visibility = Visibility.Visible;
                return;
            }
            int idx = 1;
            while (true)
            {
                bool used = false;
                foreach (AiConfig c in profiles) if (c.Index == idx) used = true;
                if (!used) break;
                idx++;
            }
            AiConfig n = new AiConfig();
            n.Index = idx;
            n.Name = "配置 " + idx;
            n.Endpoint = "";
            n.Model = "";
            profiles.Add(n);
            editing = profiles.Count - 1;
            RebuildList();
            SelectProfile(editing);
            statusHost.Visibility = Visibility.Collapsed;
        }

        private void RemoveProfile()
        {
            if (profiles.Count <= 1)
            {
                status.Text = "至少要保留一套配置。";
                statusHost.Visibility = Visibility.Visible;
                return;
            }
            int i = editing < 0 ? 0 : editing;
            // 删除不可撤销（含该配置的累计用量），先确认
            if (!Dialogs.Confirm(this, "确定要删除配置「" + profiles[i].Title + "」吗？\n\n" +
                                      "该配置的接口地址、模型、Key 与累计用量会一起清除，且无法撤销。",
                                 "删除配置"))
                return;
            bool switched = false;
            if (profiles[i].Index == active)
            {
                int next = i == 0 ? 1 : 0;
                active = profiles[next].Index;
                switched = true;
            }
            profiles.RemoveAt(i);
            editing = i > 0 ? i - 1 : 0;
            RebuildList();
            SelectProfile(editing);
            if (switched)
            {
                status.Text = "当前启用的是「" + profiles[editing].Title + "」（原配置已删除），点「保存」后生效。";
                statusHost.Visibility = Visibility.Visible;
            }
            else statusHost.Visibility = Visibility.Collapsed;
        }

        private void UseProfile()
        {
            CommitFields();
            if (editing < 0) return;
            active = profiles[editing].Index;
            RebuildList();
            SelectProfile(editing);
            status.Text = "已把「" + profiles[editing].Title + "」设为当前配置，点「保存」后生效。";
            statusHost.Visibility = Visibility.Visible;
        }

        private void TakeSnapshot()
        {
            original = new Dictionary<int, AiConfig>();
            foreach (AiConfig c in profiles)
            {
                AiConfig s = new AiConfig();
                s.Index = c.Index;
                s.Name = c.Name;
                s.Endpoint = c.Endpoint;
                s.Model = c.Model;
                s.ApiKey = c.ApiKey;
                original[c.Index] = s;
            }
            originalActive = active;
        }

        // 只有接口地址、模型、Key 任一变化才算「更新了这套配置」，改名不算
        private bool IdentityChanged(AiConfig c)
        {
            AiConfig o;
            if (!original.TryGetValue(c.Index, out o)) return false;   // 新配置本来就没有统计
            return !Same(o.Endpoint, c.Endpoint) || !Same(o.Model, c.Model) || !Same(o.ApiKey, c.ApiKey);
        }

        private static bool Same(string a, string b)
        {
            return string.Equals(a == null ? "" : a, b == null ? "" : b, StringComparison.Ordinal);
        }

        // 用量缩写：999 → 999；1000 → 1k；9999 → 10k；1000000 → 1M；1.2e9 → 1.2G
        // 按 999.95 逐级进位，避免 999,999 被格式化成「1000k」
        private static string ShortNum(long n)
        {
            // long.MinValue 取负仍溢出为负，递归没有终止条件，会直接 StackOverflow
            if (n == long.MinValue) return "-9.2E";
            if (n < 0) return "-" + ShortNum(-n);
            if (n < 1000) return n.ToString("N0");
            string[] units = new string[] { "k", "M", "G", "T" };
            double d = n;
            int u = -1;
            while (d >= 999.95 && u < units.Length - 1)
            {
                d /= 1000.0;
                u++;
            }
            // 「0.#」保留一位小数并去掉多余的 .0；显式用不变文化，避免小数点变逗号
            return d.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + units[u];
        }

        private string FormatStats(int index)
        {
            AiUsageStore.Row u;
            if (!usageRows.TryGetValue(index, out u)) return "累计消耗：暂无记录";
            if (u.InTokens <= 0 && u.OutTokens <= 0 && u.Calls <= 0) return "累计消耗：暂无记录";
            return "总输入：" + ShortNum(u.InTokens) + " · 总输出：" + ShortNum(u.OutTokens);
        }

        private string FormatStatsTip(int index)
        {
            AiUsageStore.Row u;
            if (!usageRows.TryGetValue(index, out u)) return "这套配置还没有请求记录";
            if (u.InTokens <= 0 && u.OutTokens <= 0 && u.Calls <= 0) return "这套配置还没有请求记录";
            return "总输入：" + u.InTokens.ToString("N0") + " Token / 总输出：" + u.OutTokens.ToString("N0") +
                   " Token / 共 " + u.Calls + " 次请求";
        }

        // 只保存当前这一套，不关窗；地址/模型/Key 变了就清空它的累计用量
        // 读取失败时禁止保存：profiles 里只有一份内存默认值，整表保存会覆盖库中真实配置
        private bool BlockedByLoadError()
        {
            if (string.IsNullOrEmpty(loadError)) return false;
            status.Text = "读取现有配置失败，已禁止保存以免覆盖库中配置：\n" + loadError;
            statusHost.Visibility = Visibility.Visible;
            return true;
        }

        private void SaveCurrentProfile()
        {
            if (BlockedByLoadError()) return;
            CommitFields();
            if (editing < 0 || editing >= profiles.Count) return;
            AiConfig c = profiles[editing];
            if (string.IsNullOrEmpty(c.Name)) c.Name = "配置 " + c.Index;
            if (!string.IsNullOrEmpty(c.Endpoint))
            {
                string bad = AiClient.ValidateEndpoint(c.Endpoint, !string.IsNullOrEmpty(c.ApiKey));
                if (bad != null)
                {
                    status.Text = bad;
                    statusHost.Visibility = Visibility.Visible;
                    return;
                }
            }

            // 用整表保存而不是 c.Save()：后者会以库里的 active 为准，
            // 用户点了「设为当前」再按「保存此配置」就会白点一次
            if (!AiConfig.SaveAll(profiles, active))
            {
                string why = string.IsNullOrEmpty(c.LastError) ? AppDb.LastError : c.LastError;
                status.Text = "保存失败：" + why +
                              "\n数据库位于：" + AppDb.FilePath + "，请确认该位置可写。";
                statusHost.Visibility = Visibility.Visible;
                return;
            }

            bool reset = IdentityChanged(c);
            // SaveAll 会写整张表：其它配置在切换列表项时已经被 CommitFields 提交，
            // 它们的地址/模型/Key 变化也必须清用量，不能只清当前正在编辑的这一套
            foreach (AiConfig p in profiles)
                if (IdentityChanged(p)) AiUsageStore.Clear(p.Index);
            // 整表快照重取：只更新单项会让 Count 与 original 脱节，Dirty() 之后一直为真
            TakeSnapshot();
            Config = c;
            Saved = true;                       // 已经真实写盘，主窗口应提示「已更新」
            RebuildList();
            SelectProfile(editing);
            status.Text = reset
                ? "已保存「" + c.Title + "」；接口地址 / 模型 / Key 有改动，累计用量已清零。"
                : "已保存「" + c.Title + "」。";
            statusHost.Visibility = Visibility.Visible;
        }

        private static string MaskKey(string k)
        {
            if (string.IsNullOrEmpty(k)) return "";
            // 只留末 4 位：首 4 位常是同类前缀（sk-、ak 等），显示出来意义很小
            if (k.Length <= 4) return new string('*', Math.Max(3, k.Length - 1)) + k.Substring(k.Length - 1);
            int stars = Math.Max(6, Math.Min(12, k.Length - 4));
            return new string('*', stars) + k.Substring(k.Length - 4);
        }

        private string CurrentKey()
        {
            return EffectiveKey();
        }

        private void SaveAndClose()
        {
            if (BlockedByLoadError()) return;
            CommitFields();
            foreach (AiConfig c in profiles)
                if (string.IsNullOrEmpty(c.Name)) c.Name = "配置 " + c.Index;

            // 只校验刚编辑的这一套：旧配置里的历史地址不应阻塞无关的保存，
            // 真要用到它时 AskReply 还会再校验一次并给出明确原因
            if (editing >= 0 && editing < profiles.Count)
            {
                AiConfig cur = profiles[editing];
                if (!string.IsNullOrEmpty(cur.Endpoint))
                {
                    string bad = AiClient.ValidateEndpoint(cur.Endpoint, !string.IsNullOrEmpty(cur.ApiKey));
                    if (bad != null)
                    {
                        status.Text = "「" + cur.Title + "」" + bad;
                        statusHost.Visibility = Visibility.Visible;
                        return;
                    }
                }
            }

            List<int> before = new List<int>(original.Keys);

            if (!AiConfig.SaveAll(profiles, active))
            {
                string why = (profiles.Count > 0 && !string.IsNullOrEmpty(profiles[0].LastError))
                             ? profiles[0].LastError : AppDb.LastError;
                status.Text = "保存失败：" + why +
                              "\n数据库位于：" + AppDb.FilePath + "，请确认该位置可写。";
                statusHost.Visibility = Visibility.Visible;
                return;
            }

            // 被删掉的配置要先清：它的 Index 会被「新增」复用
            foreach (int k in before)
            {
                bool still = false;
                foreach (AiConfig c in profiles) if (c.Index == k) still = true;
                if (!still) AiUsageStore.Clear(k);
            }
            // 地址/模型/Key 改过的配置清零，其余继续累计
            foreach (AiConfig c in profiles) if (IdentityChanged(c)) AiUsageStore.Clear(c.Index);

            Config = profiles[editing < 0 ? 0 : editing];
            Saved = true;
            suppressCloseConfirm = true;
            Close();
        }

        private void TestAsync()
        {
            AiConfig probe = new AiConfig();
            probe.Endpoint = fEndpoint.Text.Trim();
            probe.ApiKey = CurrentKey();
            probe.Model = fModel.Text.Trim();
            int probeIndex = editing;
            string probeTitle = editing >= 0 && editing < profiles.Count ? profiles[editing].Title : "测试配置";
            if (!probe.Ready)
            {
                status.Text = "请先填写接口地址与模型名称。";
                statusHost.Visibility = Visibility.Visible;
                return;
            }
            string bad = AiClient.ValidateEndpoint(probe.Endpoint, !string.IsNullOrEmpty(probe.ApiKey));
            if (bad != null)
            {
                status.Text = bad;
                statusHost.Visibility = Visibility.Visible;
                return;
            }
            btnTest.IsEnabled = false;
            status.Text = "正在测试连接…";
            statusHost.Visibility = Visibility.Visible;
            Thread t = new Thread(delegate()
            {
                string msg;
                try
                {
                    string reply = AiClient.Ask(probe, "这是一次连通性测试，请只回复两个字：可用。",
                        delegate(HttpWebRequest r)
                        {
                            testReq = r;
                            // 窗口可能在回调之前就关了，或关闭落在两次重试之间
                            if (closed) { try { r.Abort(); } catch { } }
                        });
                    msg = "连接成功，模型回复：" + (reply.Length > 60 ? reply.Substring(0, 60) + "…" : reply);
                }
                catch (Exception ex)
                {
                    msg = "连接失败：" + ex.Message;
                }
                try
                {
                    Dispatcher.BeginInvoke((Action)delegate
                    {
                        if (!IsLoaded) return;      // 窗口已关闭，不再更新界面
                        // 测试期间用户可能切换配置或改字段：结果必须标明归属，不能看起来像当前配置的结果
                        bool same = probeIndex == editing &&
                            string.Equals(probe.Endpoint, fEndpoint.Text.Trim(), StringComparison.Ordinal) &&
                            string.Equals(probe.Model, fModel.Text.Trim(), StringComparison.Ordinal) &&
                            string.Equals(probe.ApiKey, CurrentKey(), StringComparison.Ordinal);
                        status.Text = same ? msg : "该结果属于发起测试时的「" + probeTitle + "」：\n" + msg;
                        statusHost.Visibility = Visibility.Visible;
                        btnTest.IsEnabled = true;
                    });
                }
                catch { }   // 应用退出中 Dispatcher 已停：后台线程静默放弃收尾
            });
            t.IsBackground = true;
            t.Start();
        }
    }

    public class AiResultWindow : Window
    {
        private TextBox body;
        private TextBox advice;
        private TextBlock adviceTag;
        private TextBlock meta;
        private TextBlock usage;
        private Button btnRefresh;
        private StackPanel oldPane;
        private StackPanel newPane;
        private TextBox adviceOld;
        private TextBlock tagOld;
        private TextBlock capOld;
        private TextBlock capNew;
        private Border overlay;
        private TextBlock overlayText;
        private System.Windows.Shapes.Path spinner;
        private Grid spinHost;
        private RotateTransform spinTransform;
        private DispatcherTimer spinTimer;
        private volatile bool closed;
        private volatile HttpWebRequest inflight;
        private readonly string systemPrompt;
        private readonly string adviceHeading;
        private readonly string prompt;
        private readonly AiConfig cfg;
        private readonly int page;              // -1 表示不读不写缓存（仅预览/未配置场景）
        private readonly string itemKey;
        private readonly List<AiAnalysisStore.Entry> entries = new List<AiAnalysisStore.Entry>();
        private bool busy;
        private bool hadContent;

        // 模型按【介绍】/【建议】两段返回；拿不到标记时整体作为介绍，避免丢内容
        internal static void Split(string text, out string intro, out string advice)
        {
            intro = text == null ? "" : text.Trim();
            advice = "";
            if (string.IsNullOrEmpty(intro)) return;

            int a = intro.IndexOf("【建议】", StringComparison.Ordinal);
            int aLen = 4;                                   // 命中的标记实际长度
            int b = intro.IndexOf("【介绍】", StringComparison.Ordinal);
            if (a < 0 && b < 0)
            {
                // 模型没用括号时退一步找「建议」二字：必须落在句首（行首或句末标点之后）、
                // 后面紧跟分隔符，且位于全文后 40%。正文里随便出现一次就采信会把介绍腰斩
                int c = intro.LastIndexOf("建议", StringComparison.Ordinal);
                if (c > 0 && intro.Length - c < intro.Length * 0.4 && LooksLikeAdviceMarker(intro, c))
                {
                    a = c;
                    aLen = 2;
                }
            }
            if (a < 0 && b >= 0)
            {
                // 只有「介绍」标记：介绍之后的全部内容仍作为介绍
                intro = intro.Substring(b + 4).Trim();
                return;
            }
            if (a >= 0)
            {
                string head = b >= 0 && b < a ? intro.Substring(b + 4, a - b - 4) : intro.Substring(0, a);
                int start = Math.Min(a + aLen, intro.Length);   // 「建议」落在末尾时不能越界
                advice = intro.Substring(start).Trim().TrimStart('：', ':', '、', ' ');
                intro = head;
            }
            intro = intro.Trim('\r', '\n', ' ', '\u3000');
            advice = advice.Trim('\r', '\n', ' ', '\u3000');
        }

        // 「建议」二字要么在行首，要么紧跟在句末标点之后；后面必须跟分隔符或换行。
        // 这样「本文建议保留该服务」这类正文里的普通用法不会被当成分段点
        private static bool LooksLikeAdviceMarker(string s, int at)
        {
            int i = at - 1;
            while (i >= 0 && (s[i] == ' ' || s[i] == '\u3000' || s[i] == '\t')) i--;
            if (i >= 0)
            {
                char p = s[i];
                if (p != '\n' && p != '\r' && p != '。' && p != '；' && p != ';' && p != '！' && p != '!') return false;
            }
            int j = at + 2;
            while (j < s.Length && (s[j] == ' ' || s[j] == '\u3000' || s[j] == '\t')) j++;
            if (j >= s.Length) return false;
            char n = s[j];
            return n == '：' || n == ':' || n == '、' || n == '，' || n == ',' || n == '\n' || n == '\r';
        }

        // 建议正文首行若只是结论标签，取出并返回 true，rest 是其余正文
        internal static bool TryTag(string tips, out string tag, out string rest)
        {
            rest = tips == null ? "" : tips.Trim();
            tag = "";
            int nl = rest.IndexOf('\n');
            string first = (nl < 0 ? rest : rest.Substring(0, nl)).Trim().Trim('：', ':', '。', ' ');
            if (first == "启用" || first == "手动" || first == "禁用")
            {
                tag = first;
                rest = nl < 0 ? "" : rest.Substring(nl + 1).Trim();
                return true;
            }
            return false;
        }

        // 建议首行是结论标签（启用 / 手动 / 禁用），单独放大并按结论着色，其余作为理由正文
        internal static void ApplyAdvice(TextBlock tagBlock, TextBox adviceBox, string tips)
        {
            string tag, rest;
            TryTag(tips, out tag, out rest);
            if (tagBlock != null)
            {
                tagBlock.Text = tag;
                tagBlock.Visibility = tag.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
                if (tag == "禁用") tagBlock.Foreground = Palette.Red;
                else if (tag == "手动") tagBlock.Foreground = Palette.Blue;
                else tagBlock.Foreground = Palette.Green;
            }
            adviceBox.Text = rest;
        }

        private void ShowOverlay(bool busy, string text)
        {
            overlay.Visibility = Visibility.Visible;
            overlayText.Text = text;
            spinHost.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            if (busy) spinTimer.Start(); else spinTimer.Stop();
        }

        private void HideOverlay()
        {
            spinTimer.Stop();
            overlay.Visibility = Visibility.Collapsed;
        }

        // 自检入口：不读不写缓存，每次都直接请求
        internal AiResultWindow(string subject, string prompt, AiConfig cfg,
                                string systemPrompt, string adviceHeading)
            : this(subject, -1, null, prompt, cfg, systemPrompt, adviceHeading)
        {
        }

        internal AiResultWindow(string subject, int page, string itemKey, string prompt, AiConfig cfg,
                                string systemPrompt, string adviceHeading)
        {
            this.systemPrompt = systemPrompt;
            this.adviceHeading = adviceHeading;
            this.prompt = prompt;
            this.cfg = cfg;
            this.page = page;
            this.itemKey = itemKey;

            Title = "AI 分析";
            Width = 780;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            FrameworkElement view = (FrameworkElement)XamlReader.Parse(Views.AiResult);
            DialogShell.AttachTheme(view);
            DialogShell.Prepare(this, view);
            body = (TextBox)view.FindName("AiBody");
            advice = (TextBox)view.FindName("AiAdvice");
            adviceTag = (TextBlock)view.FindName("AdviceTag");
            meta = (TextBlock)view.FindName("AiMeta");
            usage = (TextBlock)view.FindName("AiUsage");
            btnRefresh = (Button)view.FindName("BtnAiRefresh");
            oldPane = (StackPanel)view.FindName("OldPane");
            newPane = (StackPanel)view.FindName("NewPane");
            adviceOld = (TextBox)view.FindName("AiAdviceOld");
            tagOld = (TextBlock)view.FindName("AdviceTagOld");
            capOld = (TextBlock)view.FindName("AiCapOld");
            capNew = (TextBlock)view.FindName("AiCapNew");
            overlay = (Border)view.FindName("AiLoading");
            overlay.Background = DialogShell.SolidTheme("DialogBg");
            overlayText = (TextBlock)view.FindName("AiLoadingText");
            spinner = (System.Windows.Shapes.Path)view.FindName("AiSpinner");
            spinHost = (Grid)view.FindName("AiSpinHost");
            ((TextBlock)view.FindName("AiSubject")).Text = subject;
            ((TextBlock)view.FindName("AdviceHeading")).Text =
                string.IsNullOrEmpty(adviceHeading) ? "处理建议" : adviceHeading;
            ((Button)view.FindName("BtnAiClose")).Click += delegate { Close(); };
            btnRefresh.Click += delegate { StartRequest(); };

            // 转圈动画用计时器驱动，避免在运行时解析的 XAML 里挂 Storyboard
            spinTransform = new RotateTransform(0, 13, 13);
            spinner.RenderTransform = spinTransform;
            spinTimer = new DispatcherTimer();
            spinTimer.Interval = TimeSpan.FromMilliseconds(16);
            spinTimer.Tick += delegate { spinTransform.Angle = (spinTransform.Angle + 9) % 360; };
            HideOverlay();          // 默认不盖住内容：只有首次请求或出错时才显示浮层

            meta.Text = string.IsNullOrEmpty(cfg.Model)
                ? (string.IsNullOrEmpty(cfg.Endpoint) ? "尚未配置 AI 接口" : cfg.Endpoint)
                : (string.IsNullOrEmpty(cfg.Endpoint) ? cfg.Model : cfg.Model + " · " + cfg.Endpoint);

            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) Close();
            };
            // 关窗时中止在途请求，别让后台线程再空跑一分钟
            Closed += delegate
            {
                closed = true;
                if (spinTimer != null) spinTimer.Stop();
                HttpWebRequest r = inflight;
                if (r != null) { try { r.Abort(); } catch { } }
            };

            // 窗口加载后再按真实显示器收紧宽度：构造阶段还没有 HWND，
            // WorkAreaOf 只能拿到主屏，副屏较窄时对比窗会超出工作区
            Loaded += delegate
            {
                if (pendingWidth > 0) SetWidth(pendingWidth);
            };

            // 先看本地缓存：有就直接渲染，没有才请求接口
            string cacheErr = null;
            if (page >= 0 && !string.IsNullOrEmpty(itemKey))
                entries.AddRange(AiAnalysisStore.Load(AiAnalysisStore.KindFor(page), itemKey, out cacheErr));

            if (cacheErr != null)
            {
                // 读失败 ≠ 没有缓存：直接发请求会白花一次 Token，用户也不知道
                // 为什么看不到历史结论。停在这里，给一个显式的重试入口
                btnRefresh.Content = "重新读取并分析";
                if (!cfg.Configured)
                {
                    btnRefresh.Visibility = Visibility.Collapsed;
                    ShowOverlay(false, "读取本地分析缓存失败，且当前未配置接口：" + cacheErr);
                }
                else
                {
                    ShowOverlay(false, "读取本地分析缓存失败：" + cacheErr +
                                       "\n\n可以点上方「重新读取并分析」重新请求。");
                }
            }
            else if (entries.Count > 0)
            {
                Render(false);
                if (!cfg.Configured)
                {
                    meta.Text = "缓存的模型 " + entries[0].Model + " · 当前未配置接口，无法更新";
                    btnRefresh.Visibility = Visibility.Collapsed;
                }
            }
            else if (!cfg.Configured)
            {
                btnRefresh.Visibility = Visibility.Collapsed;
                ShowOverlay(false, "尚未配置 AI 接口，请先在左侧「配置 AI」中填写接口地址与模型");
            }
            else
            {
                StartRequest();
            }
        }

#if DIAG
        // 自检用：等价于点一下「更新AI分析」（发布版不编译）
        internal void RefreshForTest() { StartRequest(); }
#endif

        private void StartRequest()
        {
            if (busy) return;
            busy = true;
            hadContent = entries.Count > 0;
            // 已缓存过介绍 → 这次只刷新处理建议，省掉重新生成介绍的 Token
            bool adviceOnly = hadContent && !string.IsNullOrEmpty(entries[0].Intro);
            string oldIntro = adviceOnly ? entries[0].Intro : null;
            btnRefresh.IsEnabled = false;
            btnRefresh.Content = "正在更新…";
            usage.Text = "";
            // 有旧内容时不遮罩，避免对比内容闪烁
            if (hadContent) meta.Text = "正在请求 " + cfg.Model + (adviceOnly ? "（仅更新建议）" : "") + " …";
            else ShowOverlay(true, "正在获取 AI 数据…");

            string kind = AiAnalysisStore.KindFor(page);
            Thread t = new Thread(delegate()
            {
                AiReply reply = null;
                string err = null;
                string intro = "";
                string tips = "";
                bool cacheOk = true;
                string cacheErr = null;
                try
                {
                    string sys = adviceOnly ? AiClient.SystemPromptAdviceOnly(page) : systemPrompt;
                    string user = adviceOnly
                        ? prompt + Environment.NewLine + "这次的资料与上一次相同，只需要重新给出处理建议。"
                        : prompt;

                    reply = AiClient.AskReply(cfg, user, sys, delegate(HttpWebRequest r)
                    {
                        inflight = r;
                        if (closed) { try { r.Abort(); } catch { } }
                    }, 120000);

                    if (reply.Parsed)
                    {
                        Split(reply.Text, out intro, out tips);
                        if (adviceOnly)
                        {
                            // 模型可能省掉【建议】标记，但首行仍是结论标签，这时整段都算建议
                            if (string.IsNullOrEmpty(tips))
                            {
                                string tagOnly, restOnly;
                                if (TryTag(reply.Text, out tagOnly, out restOnly)) tips = reply.Text.Trim();
                            }
                            // 介绍沿用上一次的缓存；没拿到建议就不要覆盖可用结论
                            intro = oldIntro;
                            if (string.IsNullOrEmpty(tips)) err = "接口没有返回可识别的处理建议，本次未覆盖缓存";
                        }
                        else if (string.IsNullOrEmpty(tips))
                        {
                            // 首次分析就可能拿到「只有介绍」或截断的输出：写进缓存会让
                            // 建议列永久空白，而补全逻辑又认为已完成
                            err = "接口未返回【建议】段（可能被截断），本次未写入缓存";
                        }
                        if (err == null && page >= 0 && !string.IsNullOrEmpty(itemKey))
                            cacheOk = AiAnalysisStore.Save(kind, itemKey, cfg.Model, intro, tips,
                                                           reply.InTokens, reply.OutTokens, out cacheErr);
                    }
                    // 用量统计与缓存写库共用同一 sqlite 文件：这里抛异常不能让上面的
                    // 缓存写入白费——结果已经成功，仅统计缺口，单独捕获
                    try
                    {
                        if (reply.HasUsage)
                            AiUsageStore.Add(cfg.Index, cfg.Model, reply.InTokens, reply.OutTokens);
                    }
                    catch (Exception usageEx) { DiagLog.Swallow("用量统计", usageEx); }
                }
                catch (Exception ex) { err = ex.Message; }

                try
                {
                    Dispatcher.BeginInvoke((Action)delegate
                    {
                        inflight = null;
                        busy = false;
                        btnRefresh.IsEnabled = true;
                        btnRefresh.Content = "更新AI分析";
                        if (closed) return;
                        Finish(reply, err, intro, tips, cacheOk, cacheErr);
                    });
                }
                catch { }   // 应用退出中 Dispatcher 已停：后台线程静默放弃收尾
            });
            t.IsBackground = true;
            t.Start();
        }

        private void Finish(AiReply reply, string err, string intro, string tips, bool cacheOk, string cacheErr)
        {
            if (err != null)
            {
                if (hadContent)
                {
                    // 已有缓存时不遮挡正文，只在底部给一行摘要，完整原因进 ToolTip
                    meta.Text = "更新失败：" + FirstLine(err);
                    meta.ToolTip = err;
                    HideOverlay();
                }
                else
                {
                    meta.Text = "请求失败，请检查 AI 配置";
                    SetUsage("");
                    ShowOverlay(false, err.Trim());
                }
                return;
            }

            HideOverlay();
            if (!reply.Parsed)
            {
                SetSingle(reply.Text, "");
                meta.Text = "接口返回无法解析，未写入缓存";
                return;
            }

            bool readOk = true;
            string readErr = null;
            if (page >= 0 && !string.IsNullOrEmpty(itemKey) && cacheOk)
            {
                List<AiAnalysisStore.Entry> list =
                    AiAnalysisStore.Load(AiAnalysisStore.KindFor(page), itemKey, out readErr);
                if (readErr != null || list.Count == 0) readOk = false;
                else
                {
                    entries.Clear();
                    entries.AddRange(list);
                }
            }

            if (!cacheOk)
            {
                // 写库失败时 entries 还是打开窗口时的旧记录：继续 Render(true) 会把旧结论
                // 冒充成「本次更新」，新结果反而被丢掉。这里直接用本次内存结果渲染
                SetSingle(intro, tips);
                meta.Text = cfg.Model + " · 本次结果未能写入本地缓存";
                meta.ToolTip = string.IsNullOrEmpty(cacheErr) ? "缓存写入失败" : "缓存写入失败：" + cacheErr;
            }
            else if (!readOk)
            {
                // 写成功但回读失败也不能拿旧 entries 冒充本次结果
                SetSingle(intro, tips);
                meta.Text = cfg.Model + " · 本次结果已写入，但读取回显失败";
                meta.ToolTip = string.IsNullOrEmpty(readErr) ? "回读缓存为空" : "回读缓存失败：" + readErr;
            }
            else if (entries.Count > 0) Render(true);
            else
            {
                SetSingle(intro, tips);
                meta.Text = cfg.Model + " · 由 AI 生成，仅供参考";
            }

            if (!reply.HasUsage) SetUsage("接口未返回用量");
            body.ScrollToHome();
            advice.ScrollToHome();
        }

        // fresh=true 表示这一份是刚请求回来的；false 表示从缓存读出
        private void Render(bool fresh)
        {
            if (entries.Count >= 2) RenderCompare(entries[1], entries[0], fresh);
            else if (entries.Count == 1) RenderSingle(entries[0], fresh);
        }

        private void RenderCompare(AiAnalysisStore.Entry older, AiAnalysisStore.Entry newest, bool fresh)
        {
            oldPane.Visibility = Visibility.Visible;
            capNew.Visibility = Visibility.Visible;
            Grid.SetColumn(newPane, 2);
            Grid.SetColumnSpan(newPane, 1);
            SetWidth(1060);

            capOld.Text = "上一次 · " + ShortTime(older.Updated) + " · " + older.Model;
            capNew.Text = "最新 · " + ShortTime(newest.Updated) + " · " + newest.Model;
            ApplyAdvice(tagOld, adviceOld, older.Advice);
            ApplyAdvice(adviceTag, advice, newest.Advice);
            body.Text = newest.Intro;

            meta.Text = fresh
                ? "本次更新 · " + newest.Model
                : "缓存于 " + newest.Updated + " · 如需最新结论请点「更新AI分析」";
            SetMetaTip(newest);
            SetUsage(UsageText(newest));
        }

        private void RenderSingle(AiAnalysisStore.Entry newest, bool fresh)
        {
            oldPane.Visibility = Visibility.Collapsed;
            capNew.Visibility = Visibility.Collapsed;
            Grid.SetColumn(newPane, 0);
            Grid.SetColumnSpan(newPane, 3);
            SetWidth(780);

            body.Text = newest.Intro;
            ApplyAdvice(adviceTag, advice, newest.Advice);
            meta.Text = fresh
                ? "本次更新 · " + newest.Model + " · 由 AI 生成，仅供参考"
                : "缓存于 " + newest.Updated + " · 模型 " + newest.Model + " · 如需最新结论请点「更新AI分析」";
            SetMetaTip(newest);
            SetUsage(UsageText(newest));
        }

        // 缓存为空或解析失败时的兜底：只显示一份
        private void SetSingle(string intro, string tips)
        {
            oldPane.Visibility = Visibility.Collapsed;
            capNew.Visibility = Visibility.Collapsed;
            Grid.SetColumn(newPane, 0);
            Grid.SetColumnSpan(newPane, 3);
            SetWidth(780);
            body.Text = intro == null ? "" : intro.Trim();
            ApplyAdvice(adviceTag, advice, tips);
            SetUsage("");
        }

        private void SetMetaTip(AiAnalysisStore.Entry e)
        {
            meta.ToolTip = string.Equals(e.Model, cfg.Model, StringComparison.Ordinal)
                ? null
                : "这条结论由 " + e.Model + " 生成，当前配置的模型是 " + cfg.Model;
        }

        private void SetUsage(string text)
        {
            if (usage != null) usage.Text = text == null ? "" : text;
        }

        private static string UsageText(AiAnalysisStore.Entry e)
        {
            if (e.InTokens <= 0 && e.OutTokens <= 0) return "";
            return "输入：" + e.InTokens.ToString("N0") + " Token / 输出：" + e.OutTokens.ToString("N0") + " Token";
        }

        private static string ShortTime(string t)
        {
            // 2026-11-16 16:12:33 → 11-16 16:12
            if (string.IsNullOrEmpty(t) || t.Length < 16) return t == null ? "" : t;
            return t.Substring(5, 11);
        }

        private static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int i = s.IndexOf('\n');
            return i < 0 ? s : s.Substring(0, i);
        }

        // 窗口显示后再改宽度：SizeToContent=Height 时直接赋值不生效，先临时切到 Manual。
        // 窗口尚未加载时没有 HWND，WorkAreaOf 会退回主屏：此时先记下请求值，
        // 等 Loaded 后按窗口真正所在的显示器重新约束一次
        private double pendingWidth;

        private void SetWidth(double w)
        {
            if (!IsLoaded)
            {
                pendingWidth = w;
                Width = w;      // 先按请求值布局，Loaded 后再按真实工作区收紧
                return;
            }
            pendingWidth = 0;
            // 对比模式固定 1060 DIP：小屏或高缩放下会超出工作区，右侧内容直接看不到。
            // 用窗口所在显示器的工作区，而不是主屏
            Rect wa = DialogShell.WorkAreaOf(this);
            double max = Math.Max(720, wa.Width - 24);
            if (w > max) w = max;
            if (Math.Abs(Width - w) < 1) return;
            double delta = w - Width;
            SizeToContent saved = SizeToContent;
            SizeToContent = SizeToContent.Manual;
            Width = w;
            SizeToContent = saved;
            double left = Left - delta / 2;
            if (left < wa.Left) left = wa.Left;
            if (left + w > wa.Right) left = Math.Max(wa.Left, wa.Right - w);
            Left = left;
        }
    }

    // ------------------------------------------------------------------ Entry point
    public static class Program
    {
        private static readonly StringBuilder Log = new StringBuilder();

        // 正常双击启动（无参数）时不去碰 Console：winexe 没有控制台，每次写都是空操作，
        // 还会让启动链路多两次无意义的句柄写入。命令行模式（--dbcheck 等）保持输出
        private static bool quietConsole = true;

        private static void Emit(string line)
        {
            if (!quietConsole)
            {
                try { Console.WriteLine(line); }
                catch { }
            }
            Log.AppendLine(line);
        }

        // 启动链路逐步追踪：每一步都立刻落盘，进程挂死时日志停在挂死点之前。
        // 落盘仅在 PCTOOLS_TRACE=1 时开启：正常启动也写 %TEMP% 既无意义，又会把
        // 管理员状态等信息留在临时目录里
        private static readonly bool TraceEnabled = string.Equals(
            Environment.GetEnvironmentVariable("PCTOOLS_TRACE"), "1", StringComparison.OrdinalIgnoreCase);

        private static void TraceStep(string line)
        {
            Emit(line);
            if (TraceEnabled) Flush("trace");
        }

#if DIAG
        // ---- 以下整段为自检/诊断实现（build.cmd diag 才编译，发布版不进 exe）----

        // 启动诊断：用最小窗口复刻提示框的关键要素（XamlReader.Parse + WindowChrome +
        // SizeToContent=Height + 无 owner 的 ShowDialog），带泵探针，定位 ShowDialog 挂死触发条件
        private static void RunDiag()
        {
            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            TraceStep("diag : Application 就绪");

            Window w = new Window();
            w.Title = "diag";
            w.Width = 400;
            w.SizeToContent = SizeToContent.Height;
            w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            w.WindowStyle = WindowStyle.None;
            w.AllowsTransparency = false;
            w.ShowInTaskbar = false;
            w.FontSize = 12.5;
            WindowChrome chrome = new WindowChrome();
            chrome.CaptionHeight = 0;
            chrome.ResizeBorderThickness = new System.Windows.Thickness(0);
            chrome.CornerRadius = new System.Windows.CornerRadius(0);
            chrome.GlassFrameThickness = new System.Windows.Thickness(0);
            chrome.UseAeroCaptionButtons = false;
            WindowChrome.SetWindowChrome(w, chrome);
            FrameworkElement v = (FrameworkElement)XamlReader.Parse(
                "<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'" +
                " Padding='30' CornerRadius='10' Background='#FFF6F8FB'>" +
                "<TextBlock Text='启动诊断' FontSize='13'/></Border>");
            w.Content = v;
            TraceStep("diag : 窗口构造完成（XamlReader.Parse + WindowChrome + SizeToContent）");

            DispatcherTimer t = new DispatcherTimer();
            t.Interval = TimeSpan.FromSeconds(2);
            int ticks = 0;
            t.Tick += delegate
            {
                ticks++;
                TraceStep("diag : 泵存活 tick " + ticks);
                if (ticks >= 2) { t.Stop(); w.Close(); app.Shutdown(); }
            };
            t.Start();

            w.ShowDialog();
            TraceStep("diag : ShowDialog 返回");
        }

        // 诊断 2：完整复刻无参启动的 PromptWindow 链路（Apply + 构造 + ShowDialog），
        // 只加泵探针定时器，用于区分「PromptWindow 本身」与「ShowDialog 无定时器」
        private static void RunDiag2()
        {
            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            ThemeManager.Apply(app, ThemeManager.IsLight());
            InstallExceptionGuards(app);
            bool admin = IsAdmin();
            TraceStep("diag2 : 主题与权限就绪 admin=" + admin);

            PromptWindow prompt = new PromptWindow();
            TraceStep("diag2 : PromptWindow 构造完成");

            DispatcherTimer t = new DispatcherTimer();
            t.Interval = TimeSpan.FromSeconds(2);
            int ticks = 0;
            t.Tick += delegate
            {
                ticks++;
                TraceStep("diag2 : 泵存活 tick " + ticks);
                if (ticks >= 2) { t.Stop(); prompt.Close(); app.Shutdown(); }
            };
            t.Start();

            prompt.ShowDialog();
            TraceStep("diag2 : ShowDialog 返回");
        }

        private static void RunTrace()
        {
            TraceStep("step 1 : 进入 Main");
            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            TraceStep("step 2 : Application 就绪");
            ThemeManager.Apply(app, ThemeManager.IsLight());
            TraceStep("step 3 : 主题应用完成");
            InstallExceptionGuards(app);
            bool admin = IsAdmin();
            TraceStep("step 4 : IsAdmin = " + admin);
            PromptWindow prompt = new PromptWindow();
            TraceStep("step 5 : PromptWindow 构造完成");

            // 实验：ShowDialog 期间放一个 2 秒定时器
            // - 定时器触发 => 模态消息泵活着，问题在窗口可见性
            // - 定时器不触发 => ShowDialog 内部真正死锁
            DispatcherTimer probe = new DispatcherTimer();
            probe.Interval = TimeSpan.FromSeconds(2);
            int ticks = 0;
            probe.Tick += delegate
            {
                ticks++;
                TraceStep("timer  : 泵存活 tick " + ticks + "，尝试关闭提示框");
                if (ticks >= 2) { probe.Stop(); prompt.Close(); }
            };
            probe.Start();

            prompt.ShowDialog();
            TraceStep("step 6 : ShowDialog 返回（ticks=" + ticks + "）");
            prompt.Close();
            TraceStep("step 7 : PromptWindow 已关闭");
            MainWindow w = new MainWindow(false, admin);
            TraceStep("step 8 : MainWindow 构造完成");
            w.RefreshAllSync();
            TraceStep("step 9 : 数据同步读取完成");
            w.Show();
            TraceStep("step 10 : 主窗口已显示");
            DispatcherTimer timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += delegate
            {
                timer.Stop();
                w.Close();
                app.Shutdown();
            };
            timer.Start();
            app.Run();
            TraceStep("step 11 : 消息循环退出");
        }
#endif

        private static void Flush(string mode)
        {
            // 诊断日志落到 %TEMP%：主文件名保持原样方便查找；写失败（被占用/无权限）时
            // 再退到带 PID 的文件名，避免多实例互相截断同一份日志
            string dir = Path.GetTempPath();
            string text = Log.ToString();
            try
            {
                File.WriteAllText(Path.Combine(dir, "PCTools_" + mode + ".txt"), text, Encoding.UTF8);
                return;
            }
            catch { }
            try
            {
                int pid;
                using (Process me = Process.GetCurrentProcess()) pid = me.Id;
                File.WriteAllText(Path.Combine(dir, "PCTools_" + mode + "_" + pid + ".txt"), text, Encoding.UTF8);
            }
            catch { }
        }

        internal static bool IsAdmin()
        {
            try
            {
                WindowsPrincipal p = new WindowsPrincipal(WindowsIdentity.GetCurrent());
                return p.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        // 返回 true 表示提权实例已经启动（当前进程随后退出）；
        // 返回 false 表示用户取消了 UAC 或启动失败，调用方应继续以普通权限运行
        internal static bool RestartElevated()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location);
                psi.Verb = "runas";
                psi.UseShellExecute = true;
                // 带上标记：提权实例会等父进程把单实例锁交出来，而不是立刻判定「已在运行」。
                // 父进程是先启动子进程再退出，子进程完全可能抢在父进程释放互斥体之前启动
                psi.Arguments = ElevationArg;
                using (Process p = Process.Start(psi)) { }   // 只为释放句柄，不等子进程
                ReleaseSingleInstance();
                Application.Current.Shutdown();
                return true;
            }
            catch (Exception ex)
            {
                // 不用系统 MessageBox，保持与其它提示一致的自绘弹窗
                Dialogs.Info(Application.Current == null ? null : Application.Current.MainWindow,
                    "提权启动失败",
                    "无法以管理员身份重新启动：" + ex.Message +
                    "\n\n可以继续以普通权限使用，只是涉及系统级项目的操作会提示权限不足。");
                return false;
            }
        }

        // 提权交接标记（内部参数，不在界面上出现）
        private const string ElevationArg = "--from-elevation";

        // 持有到进程退出：字段被回收会让 Mutex 提前释放，单实例判定就失效了
        private static Mutex singleInstance;

        // waitMs > 0：用于提权交接，给父进程留出退出时间；0：立即判定
        private static bool AcquireSingleInstance(int waitMs)
        {
            try
            {
                // 用 WaitOne 而不是构造函数的 initiallyOwned：上个进程崩溃时留下的
                // 是「已放弃」的互斥体，构造方式拿不到所有权，会被误判成「已在运行」
                singleInstance = new Mutex(false, @"Local\PCTools_SingleInstance");
                try { return singleInstance.WaitOne(waitMs, false); }
                catch (AbandonedMutexException) { return true; }
            }
            catch (Exception ex)
            {
                // 命名互斥体创建失败（权限/命名冲突）不能成为拒绝启动的理由
                DiagLog.Swallow("单实例互斥体", ex);
                return true;
            }
        }

        private static void ReleaseSingleInstance()
        {
            try
            {
                if (singleInstance != null) { singleInstance.ReleaseMutex(); singleInstance.Dispose(); }
            }
            catch (Exception ex) { DiagLog.Swallow("释放单实例互斥体", ex); }
            singleInstance = null;
        }

        [STAThread]
        public static void Main(string[] args)
        {
            string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            // 只有双跑（无参数）时不写控制台；任何命令行模式都保留输出，
            // --dbcheck / --selftest 依赖 stdout 或重定向文件
            quietConsole = mode.Length == 0 || string.Equals(mode, ElevationArg, StringComparison.Ordinal);
            if (mode == "--makeicon") { MakeIcon(); return; }
#if DIAG
            // ---- 自检/诊断模式分发（构建命令 build.cmd diag 才编译；发布版不进 exe）----
            if (mode == "--selftest") { RunSelfTest(); Flush("selftest"); return; }
            if (mode == "--probe") { RunProbe(); Flush("probe"); return; }
            if (mode == "--smoke") { RunSmoke(); Flush("smoke"); return; }
            if (mode == "--smokedlg") { RunSmokeDlg(); Flush("smokedlg"); return; }
            if (mode == "--smokeai") { RunSmokeAi(); Flush("smokeai"); return; }
            if (mode == "--smokeconfirm") { RunSmokeConfirm(); Flush("smokeconfirm"); return; }
            if (mode == "--smokeprogress") { RunSmokeProgress(); Flush("smokeprogress"); return; }
            if (mode == "--smokeunconf") { RunSmokeUnconf(); Flush("smokeunconf"); return; }
            if (mode == "--smokecache") { RunSmokeCache(); Flush("smokecache"); return; }
            if (mode == "--analysis") { RunAnalysis(); Flush("analysis"); return; }
            if (mode == "--analyzeall") { RunAnalyzeAll(); Flush("analyzeall"); return; }
            if (mode == "--adviceonly") { RunAdviceOnly(); Flush("adviceonly"); return; }
            if (mode == "--smokeconfig") { RunSmokeConfig(); Flush("smokeconfig"); return; }
            if (mode == "--aitest") { RunAiTest(args); return; }
            if (mode == "--aibatch") { RunAiBatch(args); Flush("aibatch"); return; }
            if (mode == "--promptstat") { RunPromptStat(args); Flush("promptstat"); return; }
#endif
            if (mode == "--dbcheck") { RunDbCheck(); Flush("dbcheck"); return; }
#if DIAG
            if (mode == "--analyzefill") { RunAnalyzeFill(); Flush("analyzefill"); return; }
            if (mode == "--zoom") { RunZoom(args); return; }
            if (mode == "--trace") { RunTrace(); Flush("trace"); return; }
            if (mode == "--diag") { RunDiag(); Flush("diag"); return; }
            if (mode == "--diag2") { RunDiag2(); Flush("diag"); return; }
#endif

            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            InstallExceptionGuards(app);

            // 单实例：两个进程共用一个 sqlite，而 AiConfig.SaveAll 是「整表删除再插入」，
            // 后保存的一方会整体覆盖前者的修改；用量统计也会互相覆盖。
            // 必须先拿锁再碰数据库：放到 Ensure 之后，第二个实例在弹「已在运行」
            // 之前就已经并发建过库、写过主题了
            // 提权交接时（--from-elevation）等父进程最多 15 秒：它正在退出，锁很快就会释放，
            // 这里若立即判定失败，两个进程都会退出，桌面上一个窗口都不剩
            bool elevatedHandoff = string.Equals(mode, ElevationArg, StringComparison.Ordinal);
            if (!AcquireSingleInstance(elevatedHandoff ? 15000 : 0))
            {
                Dialogs.Info(null, "PCTools 已在运行",
                    "检测到已有一个 PCTools 窗口在运行。\n\n" +
                    "两个实例会同时改写同一份配置库，导致后保存的一方覆盖前面的修改，因此这里不再启动第二个。");
                return;
            }

            // 先建库（首次运行时创建一个只有表结构的空库），再读主题
            if (!AppDb.Ensure())
            {
                Log.AppendLine("启动：配置库不可用 - " + AppDb.LastError);
                AppendErrorLog("启动：配置库不可用 - " + AppDb.LastError);
            }
            ThemeManager.Apply(app, ThemeManager.IsLight());

            bool admin = IsAdmin();
            TraceStep("main : 主题与权限就绪 admin=" + admin);

            if (!admin)
            {
                PromptWindow prompt = new PromptWindow();
                prompt.ShowDialog();
                // 提权成功才退出当前进程；用户取消 UAC 时继续以普通权限启动
                if (prompt.Restart && RestartElevated()) return;
            }

            MainWindow w = new MainWindow(false, admin);
            TraceStep("main : MainWindow 构造完成");
            app.MainWindow = w;
            w.Closed += delegate { app.Shutdown(); };
            app.Run(w);
            // GUI 模式退出时把内存日志一次性落盘：TraceStep（PCTOOLS_TRACE=1）、
            // UI 异常等平时只进内存，进程退出即丢；命令行模式的 Flush 不受影响
            if (Log.Length > 0) AppendErrorLog(Log.ToString());
        }

        private static bool showingError;

        // 异常与启动期错误的持久落盘：%TEMP%\PCTools_error.log。
        // Program.Log 只在命令行模式有 Flush，GUI 模式下进程一退出内存内容就丢了，
        // UI 异常/建库失败必须当场写文件，否则排查启动挂死无据可查
        private static void AppendErrorLog(string text)
        {
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "PCTools_error.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch { }
        }

        // 兜底：UI 线程上的未捕获异常弹自绘提示并跳过，而不是让窗口瞬间消失
        private static void InstallExceptionGuards(Application app)
        {
            app.DispatcherUnhandledException += delegate(object s, DispatcherUnhandledExceptionEventArgs e)
            {
                e.Handled = true;
                Log.AppendLine("UI 异常：" + e.Exception);
                AppendErrorLog("UI 异常：" + e.Exception);
                if (showingError) return;
                showingError = true;
                try
                {
                    Dialogs.Info(Application.Current.MainWindow, "程序遇到异常",
                        e.Exception.Message + "\n\n该操作已被跳过，可以继续使用。若反复出现，请把这句提示反馈给开发者。");
                }
                catch { }
                showingError = false;
            };

            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                Exception ex = e.ExceptionObject as Exception;
                AppendErrorLog("后台线程未处理异常：" + (ex == null ? Convert.ToString(e.ExceptionObject) : ex.ToString()));
            };
        }

#if DIAG
        // 诊断用：裁剪截图指定区域并放大，便于逐像素核对细节
        private static void RunZoom(string[] args)
        {
            string log = Path.Combine(Path.GetTempPath(), "PCTools_zoom.txt");
            try
            {
                string src = args[1];
                int x = int.Parse(args[2]);
                int y = int.Parse(args[3]);
                int w = int.Parse(args[4]);
                int h = int.Parse(args[5]);
                double sc = args.Length > 6 ? double.Parse(args[6]) : 3.0;
                string outPath = args.Length > 7 ? args[7] : Path.Combine(Path.GetTempPath(), "PCTools_zoom.png");

                BitmapFrame frame = BitmapFrame.Create(new Uri(src), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                CroppedBitmap crop = new CroppedBitmap(frame, new Int32Rect(x, y, w, h));
                TransformedBitmap scaled = new TransformedBitmap(crop, new ScaleTransform(sc, sc));
                PngBitmapEncoder enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(scaled));
                using (FileStream fs = new FileStream(outPath, FileMode.Create)) enc.Save(fs);
                File.WriteAllText(log, "zoom ok -> " + outPath + " (" + (int)(w * sc) + "x" + (int)(h * sc) + ")", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                File.WriteAllText(log, "zoom fail: " + ex.GetType().Name + " / " + ex.Message, Encoding.UTF8);
            }
        }

        private static void RunSelfTest()
        {
            Emit("PCTools 自检 / 数据层");
            List<string> warn = new List<string>();
            try
            {
                List<StartupItem> s = DataProvider.GetStartupItems(warn);
                Emit("启动项     : " + s.Count + " 项" + (s.Count > 0 ? "；示例 = " + s[0].Name + " [" + s[0].Status + "]" : ""));
            }
            catch (Exception ex) { Emit("启动项     : 失败 - " + ex.Message); }
            try
            {
                List<ServiceItem> v = DataProvider.GetServices(warn);
                Emit("服务       : " + v.Count + " 项" + (v.Count > 0 ? "；示例 = " + v[0].ServiceName + " [" + v[0].Status + "/" + v[0].StartType + "]" : ""));
                int[] adv = new int[6];
                foreach (ServiceItem sv2 in v) { int r2 = sv2.AdviceRank; if (r2 >= 1 && r2 <= 5) adv[r2]++; }
                Emit("服务建议   : 保留 " + adv[1] + " / 可改手动 " + adv[2] + " / 按需 " + adv[3] + " / 可禁用 " + adv[4] + " / 禁用 " + adv[5]);
                List<ServiceItem> pu = v.Where(x => Manager.IsPerUserInstance(x.ServiceName)).ToList();
                Emit("每用户服务 : " + pu.Count + " 项" + (pu.Count > 0
                    ? "；" + string.Join("，", pu.Take(3).Select(x => x.ServiceName + "=" + x.StartType).ToArray())
                    : ""));
            }
            catch (Exception ex) { Emit("服务       : 失败 - " + ex.Message); }
            try
            {
                List<TaskItem> t = DataProvider.GetTasks(warn);
                Emit("计划任务   : " + t.Count + " 项" + (t.Count > 0 ? "；示例 = " + t[0].Name + " [" + t[0].Status + "/" + t[0].Trigger + "]" : ""));
            }
            catch (Exception ex) { Emit("计划任务   : 失败 - " + ex.Message); }
            try
            {
                List<CtxMenuItem> x = DataProvider.GetContextMenuItems(warn);
                int[] cnt = new int[6];
                foreach (CtxMenuItem i in x) if (i.Tab >= 0 && i.Tab < 6) cnt[i.Tab]++;
                CtxMenuItem ci = x.FirstOrDefault(i => i.Kind == CtxKind.Classic);
                string classic = ci == null ? "(未生成)" : ci.Status;
                Emit("右键菜单   : " + x.Count + " 项（全局/桌面/驱动器/文件/文件夹/文件和文件夹 = " +
                     cnt[0] + "/" + cnt[1] + "/" + cnt[2] + "/" + cnt[3] + "/" + cnt[4] + "/" + cnt[5] +
                     "）；经典菜单 = " + classic +
                     (x.Count > 0 ? "；示例 = " + x[0].Name + " [" + x[0].Status + "/" + x[0].Kind + "]" : ""));
            }
            catch (Exception ex) { Emit("右键菜单   : 失败 - " + ex.Message); }
            Emit("读取告警   : " + warn.Count + " 条");
            for (int i = 0; i < Math.Min(5, warn.Count); i++) Emit("  - " + warn[i]);
            Emit("管理员权限 : " + IsAdmin());
            Emit("本机系统   : " + OsInfo.Describe());
            Emit("系统主题   : " + (ThemeManager.IsLight() ? "浅色" : "深色"));
            string sample = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"第一行\\n第二行 \\\"引号\\\"\"}}]}";
            string parsed = JsonText.ExtractString(sample, "content");
            Emit("JSON 解析  : " + (parsed == "第一行\n第二行 \"引号\"" ? "通过" : "失败 -> " + parsed));
            AiConfig cfg = AiConfig.Load();
            Emit("数据库     : " + AppDb.FilePath);
            int activeIdx;
            List<AiConfig> profs = AiConfig.LoadAll(out activeIdx);
            Emit("AI 配置套数: " + profs.Count + " / " + AiConfig.MaxProfiles +
                 "；当前启用第 " + activeIdx + " 套（" + cfg.Title + "）");
            Emit("AI 配置    : " + (cfg.Configured ? "已配置" : "未配置") +
                 "；endpoint=" + (string.IsNullOrEmpty(cfg.Endpoint) ? "(空)" : cfg.Endpoint) +
                 "，model=" + (string.IsNullOrEmpty(cfg.Model) ? "(空)" : cfg.Model) +
                 "，key=" + (string.IsNullOrEmpty(cfg.ApiKey) ? "(空)" : cfg.ApiKey.Length + " 字符"));
            Emit("AI 建议库  : " + (File.Exists(AiAdviceStore.FilePath) ? AiAdviceStore.FilePath : "(尚未生成，列为空)"));
            if (File.Exists(AiAdviceStore.FilePath))
            {
                Emit("  已存建议: 启动 " + AiAdviceStore.Load(AiAdviceStore.KindStartup).Count +
                     " / 服务 " + AiAdviceStore.Load(AiAdviceStore.KindService).Count +
                     " / 任务 " + AiAdviceStore.Load(AiAdviceStore.KindTask).Count + " 项");
            }

            bool? saved = ThemeManager.SavedPreference();
            Emit("主题偏好   : " + (saved.HasValue ? (saved.Value ? "light" : "dark") : "(未保存，跟随系统)"));
            if (saved.HasValue)
            {
                // 写回同一个值，验证「保存主题不会覆盖掉 AI 配置」。
                // 写入的就是刚读到的同一份值，不改变用户设置，自检可以安全重复执行
                ThemeManager.SavePreference(saved.Value);
                AiConfig again = AiConfig.Load();
                bool kept = again.Endpoint == cfg.Endpoint && again.Model == cfg.Model && again.ApiKey == cfg.ApiKey;
                Emit("主题写回   : " + (kept ? "通过（AI 配置未被覆盖）" : "失败（AI 配置丢失）"));
            }
        }
#endif

        private static void MakeIcon()
        {
            string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrEmpty(dir)) dir = Directory.GetCurrentDirectory();
            string path = Path.Combine(dir, "PCTools.ico");
            int[] sizes = new int[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
            List<byte[]> pngs = new List<byte[]>();
            foreach (int s in sizes)
            {
                RenderTargetBitmap bmp = RenderIcon(s);
                PngBitmapEncoder enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using (MemoryStream ms = new MemoryStream())
                {
                    enc.Save(ms);
                    pngs.Add(ms.ToArray());
                }
            }
            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                bw.Write((ushort)0);
                bw.Write((ushort)1);
                bw.Write((ushort)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    int s = sizes[i];
                    bw.Write((byte)(s >= 256 ? 0 : s));
                    bw.Write((byte)(s >= 256 ? 0 : s));
                    bw.Write((byte)0);
                    bw.Write((byte)0);
                    bw.Write((ushort)1);
                    bw.Write((ushort)32);
                    bw.Write((uint)pngs[i].Length);
                    bw.Write((uint)offset);
                    offset += pngs[i].Length;
                }
                foreach (byte[] p in pngs) bw.Write(p);
            }
            Console.WriteLine("icon: " + path + " (" + new FileInfo(path).Length + " bytes)");
        }

        private static RenderTargetBitmap RenderIcon(int size)
        {
            double s = size;
            DrawingVisual dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                double rad = s * 0.23;
                LinearGradientBrush bg = new LinearGradientBrush(
                    (Color)ColorConverter.ConvertFromString("#3A9BE0"),
                    (Color)ColorConverter.ConvertFromString("#0B4E86"), 55);
                dc.DrawRoundedRectangle(bg, null, new Rect(0, 0, s, s), rad, rad);

                double x1 = s * 0.24, x2 = s * 0.76;
                double[] ys = new double[] { s * 0.36, s * 0.50, s * 0.64 };
                double[] knobs = new double[] { 0.68, 0.36, 0.56 };
                double thick = Math.Max(1.1, s * 0.052);
                Pen pen = new Pen(new SolidColorBrush(Color.FromArgb(215, 255, 255, 255)), thick);
                pen.StartLineCap = PenLineCap.Round;
                pen.EndLineCap = PenLineCap.Round;
                for (int i = 0; i < 3; i++)
                {
                    dc.DrawLine(pen, new Point(x1, ys[i]), new Point(x2, ys[i]));
                    double kx = x1 + (x2 - x1) * knobs[i];
                    dc.DrawEllipse(Brushes.White, null, new Point(kx, ys[i]), s * 0.078, s * 0.078);
                }
            }
            RenderTargetBitmap bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            return bmp;
        }

#if DIAG
        private static void RunProbe()
        {
            Emit("PCTools 自检 / 界面构建");
            try
            {
                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                ThemeManager.Apply(app, ThemeManager.IsLight());
                MainWindow w = new MainWindow(true, IsAdmin());
                Emit("XAML       : 解析成功，命名控件已就绪");
                w.RefreshAllSync();
                Emit("刷新链路   : " + w.StartupCount + " / " + w.ServiceCount + " / " + w.TaskCount + " / " + w.CtxCount + " 项");
                Emit("列表行数   : " + w.Rows(0) + " / " + w.Rows(1) + " / " + w.Rows(2) + " / " + w.Rows(3));
                Emit("读取告警   : " + w.WarningCount + (w.WarningCount > 0 ? "；首条 = " + w.FirstWarning : ""));
                Emit("右键菜单   : " + w.MenuItems(0) + " / " + w.MenuItems(1) + " / " + w.MenuItems(2) + " / " + w.MenuItems(3) + " 项");
                try
                {
                    w.Measure(new Size(1180, 760));
                    w.Arrange(new Rect(0, 0, 1180, 760));
                    w.UpdateLayout();
                    Emit("布局       : Measure / Arrange / UpdateLayout 通过");
                }
                catch (Exception ex) { Emit("布局       : 跳过 - " + ex.Message); }
                w.SelectPageForTest(0); w.UpdateLayout(); Emit("列宽(启动) : " + w.ColumnInfo(0));
                w.SelectPageForTest(1); w.UpdateLayout(); Emit("列宽(服务) : " + w.ColumnInfo(1));
                w.SelectPageForTest(2); w.UpdateLayout(); Emit("列宽(任务) : " + w.ColumnInfo(2));
                w.SelectPageForTest(3); w.UpdateLayout(); Emit("列宽(右键) : " + w.ColumnInfo(3));
                Emit("右键页签   : " + w.CtxTabInfo());
                w.SetCtxTabForTest(3); w.UpdateLayout();
                Emit("切到文件页签   : " + w.CtxTabInfo());
                w.SetCtxTabForTest(1); w.UpdateLayout();
            }
            catch (Exception ex)
            {
                Emit("失败       : " + ex.GetType().FullName + " / " + ex.Message);
                if (ex.InnerException != null) Emit("内部异常   : " + ex.InnerException.Message);
            }
        }

        private static void RunSmoke()
        {
            Emit("PCTools 自检 / 真实窗口");
            try
            {
                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                ThemeManager.Apply(app, ThemeManager.IsLight());
                MainWindow w = new MainWindow(true, IsAdmin());
                w.Show();
                Emit("窗口       : Show() 成功，句柄与 DWM 设置已应用");
                // 走真实的异步加载链路：Show 之后触发后台读取，再轮询等待回填
                w.RefreshAll();
                Emit("异步加载   : 已发起，loading=" + w.IsLoading);

                DispatcherTimer timer = new DispatcherTimer();
                timer.Interval = TimeSpan.FromMilliseconds(200);
                int ticks = 0;
                timer.Tick += delegate
                {
                    ticks++;
                    if (w.IsLoading && ticks < 60) return;      // 最多等 12 秒
                    timer.Stop();
                    Emit("异步加载   : " + (w.IsLoading ? "超时未完成" : "完成，用时约 " + (ticks * 200) + " ms"));
                    Emit("列表行数   : " + w.Rows(0) + " / " + w.Rows(1) + " / " + w.Rows(2) + " / " + w.Rows(3));
                    try
                    {
                        w.UpdateLayout();
                        w.SelectRowForTest(0, 0);
                        w.UpdateLayout();
                        Capture(w, "PCTools_preview.png");
                        w.SelectPageForTest(1);
                        w.UpdateLayout();
                        Emit("服务列宽   : " + w.ColumnInfo(1));
                        Emit("排序(服务) : " + w.SortForTest(1, 0));
                        w.UpdateLayout();
                        Capture(w, "PCTools_preview_service.png");
                        w.SelectPageForTest(2);
                        w.UpdateLayout();
                        w.SelectRowForTest(2, 0);
                        Emit("排序(任务) : " + w.SortForTest(2, 1));
                        w.UpdateLayout();
                        w.SelectRowForTest(2, 0);
                        Capture(w, "PCTools_preview_task.png");
                        w.SelectPageForTest(0);
                        w.UpdateLayout();
                        Emit("导航对齐(展开) : " + w.NavIconInfo());
                        // 第 4 页：两个页签各截一张
                        w.SelectPageForTest(3);
                        w.UpdateLayout();
                        w.SelectRowForTest(3, 0);
                        w.UpdateLayout();
                        Capture(w, "PCTools_preview_ctx.png");
                        w.SetCtxTabForTest(3);
                        w.UpdateLayout();
                        Capture(w, "PCTools_preview_ctx_file.png");
                        w.SetCtxTabForTest(1);
                        w.UpdateLayout();
                        w.ToggleNavForTest();
                        w.UpdateLayout();
                        Emit("导航对齐(折叠) : " + w.NavIconInfo());
                        Capture(w, "PCTools_preview_collapsed.png");
                        w.ToggleNavForTest();
                        w.SetThemeForTest(false);
                        w.UpdateLayout();
                        Capture(w, "PCTools_preview_dark.png");
                        Emit("窗口截图   : " + Path.Combine(Path.GetTempPath(), "PCTools_preview.png"));
                        Emit("服务页截图 : " + Path.Combine(Path.GetTempPath(), "PCTools_preview_service.png"));
                        Emit("任务页截图 : " + Path.Combine(Path.GetTempPath(), "PCTools_preview_task.png"));
                        Emit("右键页截图 : " + Path.Combine(Path.GetTempPath(), "PCTools_preview_ctx.png"));
                        Emit("文件页签截图 : " + Path.Combine(Path.GetTempPath(), "PCTools_preview_ctx_file.png"));
                        Emit("折叠截图   : " + Path.Combine(Path.GetTempPath(), "PCTools_preview_collapsed.png"));
                    }
                    catch (Exception ex) { Emit("窗口截图   : 跳过 - " + ex.Message); }
                    Emit("窗口       : 截图完成，自动关闭");
                    w.Close();
                    app.Shutdown();
                };
                timer.Start();
                app.Run();
                Emit("消息循环   : 正常退出");
            }
            catch (Exception ex)
            {
                Emit("失败       : " + ex.GetType().FullName + " / " + ex.Message);
                if (ex.InnerException != null) Emit("内部异常   : " + ex.InnerException.Message);
            }
        }

        private static void Capture(Window w, string fileName)
        {
            // 按窗口真实 DPI 渲染：150% 缩放下 96 DPI 截图会缩水 1/3，看不清 1px 边框
            DpiScale dpi = VisualTreeHelper.GetDpi(w);
            int ww = Math.Max(1, (int)Math.Round(w.ActualWidth * dpi.DpiScaleX));
            int hh = Math.Max(1, (int)Math.Round(w.ActualHeight * dpi.DpiScaleY));
            Brush oldBg = w.Background;
            w.Background = new SolidColorBrush(Color.FromRgb(243, 246, 250));
            RenderTargetBitmap bmp = new RenderTargetBitmap(ww, hh,
                96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
            bmp.Render(w);
            w.Background = oldBg;
            PngBitmapEncoder enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (FileStream fs = new FileStream(Path.Combine(Path.GetTempPath(), fileName), FileMode.Create))
                enc.Save(fs);
        }

        // 扫描按钮上下「边框环」各占几像素：按 bg→ring 线段投影，半像素混合也能识别，
        // 不能直接判「颜色相等」——底环只剩 1px 且 50% 混合时颜色是个中间值
        private static void RingOf(Window w, string name)
        {
            RingPixels(w, LogicalTreeHelper.FindLogicalNode(w, name) as Button, name);
        }

        private static void RingPixels(Window w, Button b, string label)
        {
            try
            {
                if (b == null) { Emit("  环 " + label + " : 找不到按钮"); return; }
                if (!b.IsVisible) { Emit("  环 " + label + " : 不可见，跳过"); return; }
                b.UpdateLayout();

                DpiScale dpi = VisualTreeHelper.GetDpi(w);
                Point p = b.TransformToAncestor(w).Transform(new Point(0, 0));
                int cx = (int)Math.Round((p.X + b.ActualWidth / 2) * dpi.DpiScaleX);
                int top = (int)Math.Round(p.Y * dpi.DpiScaleY);
                int bot = (int)Math.Round((p.Y + b.ActualHeight) * dpi.DpiScaleY);

                int ww = Math.Max(1, (int)Math.Round(w.ActualWidth * dpi.DpiScaleX));
                int hh = Math.Max(1, (int)Math.Round(w.ActualHeight * dpi.DpiScaleY));
                if (cx < 0 || cx >= ww || top < 0 || bot > hh || bot <= top)
                {
                    Emit("  环 " + label + " : 坐标越界，跳过");
                    return;
                }

                RenderTargetBitmap bmp = new RenderTargetBitmap(ww, hh,
                    96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
                bmp.Render(w);
                int stride = ww * 4;
                byte[] buf = new byte[stride * hh];
                bmp.CopyPixels(buf, stride, 0);

                // 底色基准从图里采样（Capture 会临时换窗口背景，不能硬编码 255）
                Color bg = Px(buf, stride, cx, Math.Min(hh - 1, bot + 12));
                SolidColorBrush rb = b.TryFindResource("BtnBorder") as SolidColorBrush;
                if (rb == null) { Emit("  环 " + label + " : 取不到 BtnBorder"); return; }
                Color ring = rb.Color;

                // 布局坐标与渲染像素存在半像素相位差（162.33 DIP × 1.5 = 243.5），
                // 所以不能按 top/bot 精确切，改为在按钮上下各放宽 3px 的范围内找环比
                int y0 = Math.Max(0, top - 3);
                int y1 = Math.Min(hh - 1, bot + 3);
                int firstRing = -1, lastRing = -1;
                for (int y = y0; y <= y1; y++)
                {
                    if (Coverage(Px(buf, stride, cx, y), ring, bg) < 0.5) continue;
                    if (firstRing < 0) firstRing = y;
                    lastRing = y;
                }
                int up = 0, down = 0;
                if (firstRing >= 0)
                {
                    for (int y = firstRing; y <= y1; y++)
                    {
                        if (Coverage(Px(buf, stride, cx, y), ring, bg) < 0.5) break;
                        up++;
                    }
                    for (int y = lastRing; y >= y0; y--)
                    {
                        if (Coverage(Px(buf, stride, cx, y), ring, bg) < 0.5) break;
                        down++;
                    }
                }

                int height = bot - top;
                // 主按钮是 Accent 纯色块，整块都会被判成「环」，这类不适用本断言
                if (up + down >= height - 2)
                {
                    Emit("  环 " + label + " : 整块同色（主按钮），跳过");
                    return;
                }
                bool ok = up >= 1 && down >= 1 && Math.Abs(up - down) <= 1;
                Emit("  环 " + label + " : 上 " + up + "px / 下 " + down + "px / 总高 " + height + "px  " +
                     (ok ? "PASS" : "FAIL"));
            }
            catch (Exception ex) { Emit("  环 " + label + " : 异常 " + ex.Message); }
        }

        // 按通道最小二乘估计「这一行有多少比例是环色」
        private static double Coverage(Color px, Color ring, Color bgc)
        {
            double num = 0, den = 0;
            Channel(px.R, ring.R, bgc.R, ref num, ref den);
            Channel(px.G, ring.G, bgc.G, ref num, ref den);
            Channel(px.B, ring.B, bgc.B, ref num, ref den);
            if (den <= 0.0001) return 0;
            double v = num / den;
            return v < 0 ? 0 : (v > 1 ? 1 : v);
        }

        private static void Channel(int pv, int rv, int bv, ref double num, ref double den)
        {
            double d = rv - bv;
            if (Math.Abs(d) < 8) return;      // 该通道区分度太低，不参与
            num += (pv - bv) * d;
            den += d * d;
        }

        private static Color Px(byte[] buf, int stride, int x, int y)
        {
            int i = y * stride + x * 4;       // Pbgra32 的字节序是 B,G,R,A
            return Color.FromRgb(buf[i + 2], buf[i + 1], buf[i]);
        }
#endif

#if DIAG
        // 离线体检：只构造批量建议提示词，不发网络请求，用于观察长度与行格式
        private static void RunPromptStat(string[] args)
        {
            int only = -1;
            if (args.Length > 1) int.TryParse(args[1], out only);
            Emit("PCTools 自检 / 批量建议提示词");
            Emit("数据库     : " + AiAdviceStore.FilePath);
            for (int page = 0; page <= 2; page++)
            {
                if (only >= 0 && page != only) continue;
                List<string> warn = new List<string>();
                List<object> items = new List<object>();
                if (page == 0) { foreach (StartupItem it in DataProvider.GetStartupItems(warn)) items.Add(it); }
                else if (page == 1) { foreach (ServiceItem it in DataProvider.GetServices(warn)) items.Add(it); }
                else { foreach (TaskItem it in DataProvider.GetTasks(warn)) items.Add(it); }
                Emit("---- 页面 " + page + " ----");
                if (items.Count == 0) { Emit("项目数量   : 0"); continue; }
                BatchPlan plan = new BatchPlan();
                string prompt = AiBatch.Build(page, items, plan);
                string dump = Path.Combine(Path.GetTempPath(), "PCTools_prompt_" + page + ".txt");
                try { File.WriteAllText(dump, prompt, Encoding.UTF8); }
                catch { }
                Emit("提示词落盘 : " + dump);
                string[] lines = prompt.Replace("\r", "").Split('\n');
                Emit("项目数量   : " + items.Count + "（发送 " + plan.Sent + "，跳过 " + plan.Skipped + "）");
                Emit("提示词长度 : " + prompt.Length + " 字符 / " + lines.Length + " 行 / 平均 " +
                     (prompt.Length / Math.Max(1, lines.Length)) + " 字符");
                int head = Math.Min(6, lines.Length);
                for (int i = 0; i < head; i++) Emit("  " + lines[i]);
                if (lines.Length > head + 1)
                {
                    Emit("  …");
                    Emit("  " + lines[lines.Length - 2]);
                }
            }
        }

        // 批量建议端到端自检：真实请求 → 解析 → 写 sqlite → 回读
        private static void RunAiBatch(string[] args)
        {
            int page = 0;
            if (args.Length > 1) int.TryParse(args[1], out page);
            Emit("PCTools 自检 / 批量 AI 建议");
            Emit("页面       : " + (page == 0 ? "启动应用" : (page == 1 ? "服务管理" : "计划任务")));
            Emit("数据库     : " + AiAdviceStore.FilePath);

            AiConfig cfg = AiConfig.Load();
            Emit("模型       : " + cfg.Model + "（" + (cfg.Configured ? "已配置" : "未配置") + "）");
            if (!cfg.Configured) return;

            List<string> warn = new List<string>();
            List<object> items = new List<object>();
            if (page == 0) { foreach (StartupItem it in DataProvider.GetStartupItems(warn)) items.Add(it); }
            else if (page == 1) { foreach (ServiceItem it in DataProvider.GetServices(warn)) items.Add(it); }
            else { foreach (TaskItem it in DataProvider.GetTasks(warn)) items.Add(it); }
            Emit("项目数量   : " + items.Count);
            if (items.Count == 0) return;

            BatchPlan plan = new BatchPlan();
            string prompt = AiBatch.Build(page, items, plan);
            Emit("提示词长度 : " + prompt.Length + " 字符");
            Emit("发送项目   : " + plan.Sent + " 项（跳过每用户实例 " + plan.Skipped + " 项）");
            string[] head = prompt.Replace("\r", "").Split('\n');
            Emit("提示词首行 : " + (head.Length > 0 ? head[0] : ""));

            DateTime t0 = DateTime.Now;
            try
            {
                string reply = AiClient.Ask(cfg, prompt, AiBatch.System(page), null, 300000);
                Emit("耗时       : " + (int)(DateTime.Now - t0).TotalMilliseconds + " ms");
                Emit("回答长度   : " + reply.Length + " 字符");
                string oneLine = reply.Replace("\r", " ").Replace("\n", " / ");
                Emit("回答样例   : " + (oneLine.Length > 260 ? oneLine.Substring(0, 260) + "…" : oneLine));

                Dictionary<string, string> values = AiBatch.Parse(reply, plan, page);
                Emit("解析结果   : " + values.Count + " / " + items.Count + " 项");

                string kind = page == 0 ? AiAdviceStore.KindStartup
                            : (page == 1 ? AiAdviceStore.KindService : AiAdviceStore.KindTask);
                string err;
                bool ok = AiAdviceStore.Save(kind, values, out err);
                Emit("写入数据库 : " + (ok ? "成功" : "失败 - " + err));

                Dictionary<string, string> back = AiAdviceStore.Load(kind);
                Emit("回读数据库 : " + back.Count + " 项");
                int shown = 0;
                foreach (KeyValuePair<string, string> kv in back)
                {
                    Emit("  样例     : " + kv.Key + " = " + kv.Value);
                    if (++shown >= 3) break;
                }
            }
            catch (Exception ex) { Emit("请求失败   : " + ex.Message); }
        }

        private static void RunAiTest(string[] args)
        {
            AiConfig cfg = AiConfig.Load();
            if (args.Length > 1) cfg.Endpoint = args[1];
            if (args.Length > 2) cfg.Model = args[2];
            // 命令行参数对同机其它用户可见（进程命令行可查）；优先从环境变量取 Key
            if (args.Length > 3)
                cfg.ApiKey = args[3];
            else
            {
                string envKey = Environment.GetEnvironmentVariable("PCTOOLS_AI_KEY");
                if (!string.IsNullOrEmpty(envKey)) cfg.ApiKey = envKey;
            }
            Emit("PCTools 自检 / AI 请求");
            Emit("接口地址   : " + cfg.Endpoint);
            Emit("模型       : " + cfg.Model);
            Emit("API Key    : " + (string.IsNullOrEmpty(cfg.ApiKey) ? "(未设置)" : "已设置")
                 + (args.Length > 3 ? "（来自命令行；建议改用环境变量 PCTOOLS_AI_KEY，避免命令行暴露）" : ""));
            DateTime t0 = DateTime.Now;
            try
            {
                string reply = AiClient.Ask(cfg, "请用一句话说明 Windows 服务是什么。");
                Emit("耗时       : " + (int)(DateTime.Now - t0).TotalMilliseconds + " ms");
                Emit("返回内容   : " + (reply.Length > 300 ? reply.Substring(0, 300) + "…" : reply));

                string itemPrompt = "请分析下面这个 Windows 启动项，是否需要随系统启动。" +
                    "名称：Clash Verge；状态：已启用；位置类型：注册表 Run；作用范围：当前用户；" +
                    "启动命令：C:/Program Files/Clash Verge/clash-verge.exe --autostart";
                string full = AiClient.Ask(cfg, itemPrompt, AiClient.SystemPromptFor(0), null);
                string intro, tips;
                AiResultWindow.Split(full, out intro, out tips);
                Emit("原始回答   : " + full);
                Emit("拆分-介绍 : " + (intro.Length == 0 ? "(空)" : intro));
                Emit("拆分-建议 : " + (tips.Length == 0 ? "(空)" : tips));
                string iFirst = tips.Length == 0 ? "" : tips.Split('\n')[0].Trim();
                Emit("启动项首行 : " + (iFirst.Length == 0 ? "(空)" : iFirst)
                     + "；合规=" + (iFirst == "启用" || iFirst == "禁用" ? "是" : "否")
                     + "；手动处理=" + ((tips.IndexOf("改为手动", StringComparison.Ordinal) >= 0 ||
                                          tips.IndexOf("建议手动", StringComparison.Ordinal) >= 0)
                                         ? "是（不应出现）" : "否"));

                ServiceItem probeSvc = new ServiceItem();
                probeSvc.Name = "Print Spooler";
                probeSvc.ServiceName = "Spooler";
                probeSvc.Status = "正在运行";
                probeSvc.StartType = "自动";
                probeSvc.ServiceType = "共享进程";
                probeSvc.Account = "LocalSystem";
                probeSvc.Path = "C:\\Windows\\System32\\spoolsv.exe";
                probeSvc.Advice = "可改手动";
                probeSvc.AdviceNote = "无需打印时可改为手动";
                string sreply = AiClient.Ask(cfg, MainWindow.BuildServicePrompt(probeSvc),
                                             AiClient.SystemPromptFor(1), null);
                string sintro, stips;
                AiResultWindow.Split(sreply, out sintro, out stips);
                Emit("服务建议原文 : " + sreply);
                Emit("服务建议拆分 : 介绍=" + (sintro.Length == 0 ? "(空)" : sintro));
                Emit("               建议=" + (stips.Length == 0 ? "(空)" : stips));
                TaskItem probeTask = new TaskItem();
                probeTask.Name = "MicrosoftEdgeUpdateTaskMachineCore";
                probeTask.Status = "就绪";
                probeTask.Trigger = "登录时";
                probeTask.Action = "C:\\Program Files (x86)\\Microsoft\\EdgeUpdate\\MicrosoftEdgeUpdate.exe /c";
                probeTask.TaskPath = "\\";
                string treply = AiClient.Ask(cfg,
                    "请分析下面这个 Windows 计划任务，并在结论中明确给出处理方式：保持启用还是禁用（计划任务只有启用和禁用两种处理，没有“手动”）。" +
                    "任务名称：" + probeTask.Name + "；状态：" + probeTask.Status +
                    "；触发器：" + probeTask.Trigger + "；执行操作：" + probeTask.Action +
                    "；任务路径：" + probeTask.TaskPath,
                    AiClient.SystemPromptFor(2), null);
                string tintro, ttips;
                AiResultWindow.Split(treply, out tintro, out ttips);
                string tFirst = ttips.Length == 0 ? "" : ttips.Split('\n')[0].Trim();
                Emit("计划任务建议 : " + (ttips.Length == 0 ? "(空)" : ttips));
                Emit("任务首行   : " + (tFirst.Length == 0 ? "(空)" : tFirst)
                     + "；合规=" + (tFirst == "启用" || tFirst == "禁用" ? "是" : "否")
                     + "；手动处理=" + ((ttips.IndexOf("改为手动", StringComparison.Ordinal) >= 0 ||
                                          ttips.IndexOf("建议手动", StringComparison.Ordinal) >= 0)
                                         ? "是（不应出现）" : "否"));

                string sFirst = stips.Length == 0 ? "" : stips.Split('\n')[0].Trim();
                Emit("建议首行   : " + (sFirst.Length == 0 ? "(空)" : sFirst));
                Emit("首行合规   : " + (sFirst == "启用" || sFirst == "手动" || sFirst == "禁用" ? "是" : "否"));
            }
            catch (Exception ex)
            {
                Emit("耗时       : " + (int)(DateTime.Now - t0).TotalMilliseconds + " ms");
                Emit("请求失败   : " + ex.Message);
            }
            Flush("aitest");
        }

        private static void RunSmokeProgress()
        {
            Emit("PCTools 自检 / 批量操作进度框");
            try
            {
                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                ThemeManager.Apply(app, ThemeManager.IsLight());

                ProgressWindow w = new ProgressWindow("禁用", 10);
                int n = 0;
                DispatcherTimer tick = new DispatcherTimer();
                tick.Interval = TimeSpan.FromMilliseconds(600);
                tick.Tick += delegate
                {
                    n++;
                    w.Report(n * 2, 10, "示例服务 " + (n * 2));
                    if (n < 3) return;
                    tick.Stop();
                    try
                    {
                        w.UpdateLayout();
                        Capture(w, "PCTools_progress.png");
                        Emit("进度框     : Show() 成功，截图已导出");
                    }
                    catch (Exception ex) { Emit("截图       : 失败 - " + ex.Message); }
                    w.Finish();

                    // 第二阶段：验证 worker 线程 + ShowDialog 的收尾逻辑（不触碰真实系统项目）
                    ProgressWindow p2 = new ProgressWindow("测试", 5);
                    Thread th = new Thread(delegate()
                    {
                        for (int i = 1; i <= 5; i++)
                        {
                            Thread.Sleep(80);
                            int k = i;
                            p2.Dispatcher.BeginInvoke((Action)delegate { p2.Report(k, 5, "项目 " + k); });
                        }
                        p2.Dispatcher.BeginInvoke((Action)delegate { p2.Finish(); });
                    });
                    th.IsBackground = true;
                    th.Start();
                    p2.ShowDialog();
                    Emit("收尾逻辑   : worker 完成后 ShowDialog 正常返回");

                    // 第三阶段：完成但保留窗口（AI 建议更新后的形态），截完图再关闭
                    ProgressWindow p3 = new ProgressWindow("更新 AI 建议", 10);
                    DispatcherTimer t3 = new DispatcherTimer();
                    t3.Interval = TimeSpan.FromMilliseconds(700);
                    t3.Tick += delegate
                    {
                        t3.Stop();
                        // 先截「运行中」：验证 Token 行会实时显示
                        p3.Report(6, 12, "正在分析 DiagTrack", 1834, 512, true);
                        p3.UpdateLayout();
                        try { Capture(p3, "PCTools_progress_tokens.png"); }
                        catch (Exception ex) { Emit("截图       : 失败 - " + ex.Message); }
                        Emit("Token 行   : " + p3.Tokens);
                        p3.Report(10, 10, "已收到结果");
                        p3.FinishKeepOpen("已更新 287 项（共 287 项）\n本次消耗　输入：28,431 Token / 输出：3,122 Token");
                        p3.UpdateLayout();
                        try { Capture(p3, "PCTools_progress_done.png"); }
                        catch (Exception ex) { Emit("截图       : 失败 - " + ex.Message); }
                        RingOf(p3, "BtnCancel");
                        Emit("停留逻辑   : FinishKeepOpen 后窗口仍在，等待用户点「关闭」");
                        p3.Close();

                        // 第四阶段：主窗口退出时的强制关闭——OnClosing 不能把 Close 转成「取消」
                        ProgressWindow p4 = new ProgressWindow("测试强制关闭", 5);
                        p4.Show();
                        p4.ForceClose();
                        Emit("强制关闭   : 未跑完也能关掉，IsVisible=" + p4.IsVisible);
                        app.Shutdown();
                    };
                    t3.Start();
                    p3.Show();
                };
                tick.Start();
                w.Show();
                app.Run();
                Emit("消息循环   : 正常退出");
            }
            catch (Exception ex)
            {
                Emit("失败       : " + ex.GetType().Name + " / " + ex.Message);
            }
        }

        private static void RunSmokeConfirm()
        {
            Emit("PCTools 自检 / 确认对话框");
            try
            {
                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                ThemeManager.Apply(app, ThemeManager.IsLight());
                ConfirmWindow c = new ConfirmWindow("确认禁用", "此操作会立即生效，请确认后继续",
                    "确定要对选中的 1 个项目执行「禁用」操作吗？\n\n注意：禁用系统关键服务、启动项或计划任务可能影响系统或应用程序功能，请确认已知悉影响。",
                    "确认禁用", false);
                DispatcherTimer timer = new DispatcherTimer();
                timer.Interval = TimeSpan.FromSeconds(1.5);
                timer.Tick += delegate
                {
                    timer.Stop();
                    try
                    {
                        c.UpdateLayout();
                        Capture(c, "PCTools_confirm.png");
                        Emit("两按钮形态 : Show() 成功，截图已导出");
                        RingOf(c, "BtnNo");
                        RingOf(c, "BtnYes");
                    }
                    catch (Exception ex) { Emit("截图       : 失败 - " + ex.Message); }
                    c.Close();

                    // 再截一张三按钮形态（「取消 / 补全分析 / 开始分析」）
                    ConfirmWindow c3 = new ConfirmWindow("确认操作", "已有 12 项，待补全 28 项",
                        "当前列表共 40 个项目。\n\n· 开始分析：对全部 40 项逐项重新请求。\n" +
                        "· 补全分析：只请求还没有分析记录的 28 项。",
                        "开始分析", "补全分析（28 项）", false);
                    DispatcherTimer t2 = new DispatcherTimer();
                    t2.Interval = TimeSpan.FromSeconds(1.2);
                    t2.Tick += delegate
                    {
                        t2.Stop();
                        try
                        {
                            c3.UpdateLayout();
                            Capture(c3, "PCTools_confirm3.png");
                            Emit("三按钮形态 : 截图已导出，默认结果为 " + c3.Choice);
                            RingOf(c3, "BtnNo");
                            RingOf(c3, "BtnThird");
                            RingOf(c3, "BtnYes");
                        }
                        catch (Exception ex) { Emit("截图       : 失败 - " + ex.Message); }
                        c3.Close();
                        app.Shutdown();
                    };
                    t2.Start();
                    c3.Show();
                };
                timer.Start();
                c.Show();
                app.Run();
                Emit("消息循环   : 正常退出");
            }
            catch (Exception ex)
            {
                Emit("失败       : " + ex.GetType().Name + " / " + ex.Message);
            }
        }

        private static void RunSmokeAi()
        {
            Emit("PCTools 自检 / AI 对话框");
            try
            {
                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                ThemeManager.Apply(app, ThemeManager.IsLight());

                AiConfig cfg = AiConfig.Load();
                Emit("数据库     : " + AppDb.FilePath);
                Emit("接口地址   : " + (string.IsNullOrEmpty(cfg.Endpoint) ? "(空)" : cfg.Endpoint));
                Emit("模型名称   : " + (string.IsNullOrEmpty(cfg.Model) ? "(空)" : cfg.Model));
                Emit("API Key    : " + (string.IsNullOrEmpty(cfg.ApiKey) ? "(未设置)" : "已设置（" + cfg.ApiKey.Length + " 字符）"));

                AiSettingsWindow s = new AiSettingsWindow(cfg);
                Window r = DialogShell.Create("AI 分析", 740, 0);
                FrameworkElement rv = (FrameworkElement)XamlReader.Parse(Views.AiResult);
                DialogShell.AttachTheme(rv);
                DialogShell.Prepare(r, rv);
                ((TextBlock)rv.FindName("AiSubject")).Text = "Clash Verge";
                ((TextBlock)rv.FindName("AdviceHeading")).Text = "处理建议（启用 / 禁用）";
                ((TextBox)rv.FindName("AiBody")).Text =
                    "这是 Clash Verge 的启动项，属于 Clash 内核的图形前端，随用户登录自动启动。" +
                    "它的作用是在后台加载订阅配置并建立本地代理端口，供浏览器与其他客户端按规则转发流量。" +
                    "启动项位于当前用户的注册表 Run 位置，只影响本账户，不会写入系统目录。";
                AiResultWindow.ApplyAdvice((TextBlock)rv.FindName("AdviceTag"), (TextBox)rv.FindName("AiAdvice"),
                    "启用\n保持启用可以让代理随登录自动就绪，浏览器与其他客户端无需额外操作即可按规则转发流量。" +
                    "禁用后开机不会自动启用代理，需要手动打开客户端；恢复时再次启用即可，不会残留配置。");
                ((TextBlock)rv.FindName("AiMeta")).Text = "预览排版示例 · 不会发起真实请求";
                Border loadMask = (Border)rv.FindName("AiLoading");
                loadMask.Background = DialogShell.SolidTheme("DialogBg");
                loadMask.Visibility = Visibility.Collapsed;

                s.Show();
                r.Show();

                DispatcherTimer timer = new DispatcherTimer();
                timer.Interval = TimeSpan.FromSeconds(1.6);
                timer.Tick += delegate
                {
                    timer.Stop();
                    try
                    {
                        s.UpdateLayout();
                        Capture(s, "PCTools_ai_settings.png");
                        RingOf(s, "BtnCancel");
                        RingOf(s, "BtnTest");
                        RingOf(s, "BtnSave");
                        RingOf(s, "BtnSaveOne");
                        RingOf(s, "BtnNew");
                        r.UpdateLayout();
                        Capture(r, "PCTools_ai_result.png");
                        // 再截一张加载浮层的样子
                        ((Border)rv.FindName("AiLoading")).Visibility = Visibility.Visible;
                        r.UpdateLayout();
                        Capture(r, "PCTools_ai_loading.png");
                        // 再截一张「两份建议左右对比」的样子
                        ((Border)rv.FindName("AiLoading")).Visibility = Visibility.Collapsed;
                        ((StackPanel)rv.FindName("OldPane")).Visibility = Visibility.Visible;
                        StackPanel np = (StackPanel)rv.FindName("NewPane");
                        Grid.SetColumn(np, 2);
                        Grid.SetColumnSpan(np, 1);
                        ((TextBlock)rv.FindName("AiCapOld")).Visibility = Visibility.Visible;
                        ((TextBlock)rv.FindName("AiCapNew")).Visibility = Visibility.Visible;
                        ((TextBlock)rv.FindName("AiCapOld")).Text = "上一次 · 11-16 09:20 · deepseek-chat";
                        ((TextBlock)rv.FindName("AiCapNew")).Text = "最新 · 11-16 15:41 · step-3.7-flash";
                        ((TextBox)rv.FindName("AiAdviceOld")).Text =
                            "保持启用。Clash Verge 需要常驻才能维持代理，禁用后浏览器会断网。";
                        TextBlock oldTag = (TextBlock)rv.FindName("AdviceTagOld");
                        oldTag.Text = "启用";
                        oldTag.Foreground = Palette.Green;
                        oldTag.Visibility = Visibility.Visible;
                        ((TextBlock)rv.FindName("AiUsage")).Text = "输入：1,284 Token / 输出：312 Token";
                        r.Width = 1060;
                        r.UpdateLayout();
                        Capture(r, "PCTools_ai_compare.png");
                        Emit("对话框     : Show() 成功，截图已导出");
                    }
                    catch (Exception ex) { Emit("截图       : 失败 - " + ex.Message); }
                    s.Close();
                    r.Close();
                    app.Shutdown();
                };
                timer.Start();
                app.Run();
                Emit("消息循环   : 正常退出");
            }
            catch (Exception ex)
            {
                Emit("失败       : " + ex.GetType().Name + " / " + ex.Message);
            }
        }

        private static void RunSmokeUnconf()
        {
            Emit("PCTools 自检 / 未配置 AI 的提示态");
            try
            {
                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                ThemeManager.Apply(app, ThemeManager.IsLight());

                AiConfig cfg = new AiConfig();
                cfg.Endpoint = "";
                cfg.ApiKey = "";
                cfg.Model = "";
                Emit("Configured : " + cfg.Configured);

                AiResultWindow w = new AiResultWindow("示例项目 · 未配置", "ignored", cfg,
                                                      AiClient.SystemPromptFor(1), "处理建议（启用 / 手动 / 禁用）");
                DispatcherTimer timer = new DispatcherTimer();
                timer.Interval = TimeSpan.FromSeconds(1.4);
                timer.Tick += delegate
                {
                    timer.Stop();
                    try
                    {
                        w.UpdateLayout();
                        Capture(w, "PCTools_ai_unconf.png");
                        Emit("对话框     : 提示态显示正常");
                    }
                    catch (Exception ex) { Emit("截图       : 失败 - " + ex.Message); }
                    w.Close();
                    app.Shutdown();
                };
                timer.Start();
                w.Show();
                app.Run();
                Emit("消息循环   : 正常退出");
            }
            catch (Exception ex)
            {
                Emit("失败       : " + ex.GetType().Name + " / " + ex.Message);
            }
        }

        // 缓存链路自检：预置两份结果 → 打开结果窗应直接读缓存、不发起请求，并显示左右对比。
        // 测试行写进临时数据库：以前直接写真实 PCToolsAI.sqlite，__smokecache__ 行会永久残留
        private static void RunSmokeCache()
        {
            Emit("PCTools 自检 / AI 分析缓存与对比");
            string tempDb = Path.Combine(Path.GetTempPath(), "PCTools_smokecache.sqlite");
            AppDb.UseTempForTest(tempDb);
            try
            {
                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                ThemeManager.Apply(app, ThemeManager.IsLight());

                string key = "__smokecache__";
                string err;
                AiAnalysisStore.Save(AiAnalysisStore.KindStartup, key, "deepseek-chat",
                    "这是上一次分析的项目介绍。Clash Verge 是 Clash 内核的图形前端，随登录启动。",
                    "启用\n保持启用可以让代理随登录自动就绪，禁用后浏览器会断网。", 1200, 300, out err);
                AiAnalysisStore.Save(AiAnalysisStore.KindStartup, key, "step-3.7-flash",
                    "这是最新一次分析的项目介绍。Clash Verge 随用户登录自动启动，负责加载订阅配置并建立本地代理端口，" +
                    "供浏览器与其他客户端按规则转发流量。",
                    "启用\n保持启用可以让代理随登录自动就绪，浏览器与其他客户端无需额外操作即可按规则转发流量；" +
                    "禁用后需要手动打开客户端，恢复时再次启用即可。", 1284, 312, out err);

                List<AiAnalysisStore.Entry> list = AiAnalysisStore.Load(AiAnalysisStore.KindStartup, key);
                Emit("缓存条数   : " + list.Count + "（应为 2，第 3 次写入会挤掉最旧的）");
                for (int i = 0; i < list.Count; i++)
                    Emit("  第" + (i + 1) + "份   : " + list[i].Model + " / 输入 " + list[i].InTokens +
                         " / 输出 " + list[i].OutTokens + " / " + list[i].Updated);

                AiConfig cfg = AiConfig.Load();
                AiResultWindow w = new AiResultWindow("Clash Verge", 0, key, "ignored-by-cache", cfg,
                                                      AiClient.SystemPromptFor(0), "处理建议（启用 / 禁用）");
                Emit("打开方式   : 有缓存 → 不发起请求");
                DispatcherTimer timer = new DispatcherTimer();
                timer.Interval = TimeSpan.FromSeconds(1.4);
                timer.Tick += delegate
                {
                    timer.Stop();
                    try
                    {
                        w.UpdateLayout();
                        Capture(w, "PCTools_ai_cache.png");
                        Emit("对话框     : 缓存渲染成功，截图已导出");
                    }
                    catch (Exception ex) { Emit("截图       : 失败 - " + ex.Message); }
                    w.Close();
                    app.Shutdown();
                };
                timer.Start();
                w.Show();
                app.Run();
                Emit("消息循环   : 正常退出");
            }
            catch (Exception ex)
            {
                Emit("失败       : " + ex.GetType().Name + " / " + ex.Message);
            }
            finally
            {
                AppDb.RestoreForTest();
                try { if (File.Exists(tempDb)) File.Delete(tempDb); }
                catch { }
                Emit("临时库     : " + tempDb + " 已清理（真实数据库未写入）");
            }
        }

        // 单条分析端到端：真实请求 → 写缓存 → 再次更新 → 左右对比
        private static void RunAnalysis()
        {
            Emit("PCTools 自检 / 单条 AI 分析（请求与缓存）");
            try
            {
                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                ThemeManager.Apply(app, ThemeManager.IsLight());

                AiConfig cfg = AiConfig.Load();
                Emit("模型       : " + cfg.Model + "（" + (cfg.Configured ? "已配置" : "未配置") + "）");
                if (!cfg.Configured) return;

                List<string> warn = new List<string>();
                List<ServiceItem> svcs = DataProvider.GetServices(warn);
                ServiceItem it = null;
                foreach (ServiceItem x in svcs) if (x.ServiceName == "Spooler") it = x;
                if (it == null && svcs.Count > 0) it = svcs[0];
                if (it == null) { Emit("失败       : 服务列表为空，没有可分析对象"); return; }

                string key = AiAnalysisStore.KeyService(it);
                string prompt = "本机系统：" + OsInfo.Describe() + Environment.NewLine + MainWindow.BuildServicePrompt(it);
                int baseline = AiAnalysisStore.Load(AiAnalysisStore.KindService, key).Count;
                Emit("分析对象   : " + it.Name + "（" + it.ServiceName + "）");
                Emit("缓存状态   : " + (baseline == 0 ? "无缓存，本次会请求接口" : "已有 " + baseline + " 份，直接读缓存"));

                AiResultWindow w = new AiResultWindow(it.Name, 1, key, prompt, cfg,
                                                      AiClient.SystemPromptFor(1), "处理建议（启用 / 手动 / 禁用）");
                int ticks = 0;
                bool refreshed = false;
                bool waitShot = false;
                DispatcherTimer timer = new DispatcherTimer();
                timer.Interval = TimeSpan.FromSeconds(1);
                timer.Tick += delegate
                {
                    ticks++;
                    int now = AiAnalysisStore.Load(AiAnalysisStore.KindService, key).Count;
                    try
                    {
                        if (baseline > 0)
                        {
                            // 有缓存：应当立即出内容，不发起请求
                            timer.Stop();
                            w.UpdateLayout();
                            Capture(w, "PCTools_ai_cached_open.png");
                            Emit("缓存命中   : 打开即渲染，未发起请求");
                            w.Close();
                            app.Shutdown();
                            return;
                        }
                        if (now >= 1 && !refreshed)
                        {
                            // 第一次请求已落库：截「单份」形态，然后触发一次更新
                            w.UpdateLayout();
                            Capture(w, "PCTools_ai_first.png");
                            Emit("首次请求   : 已写入缓存，用时约 " + ticks + " 秒");
                            refreshed = true;
                            w.RefreshForTest();
                            return;
                        }
                        if (refreshed && now >= 2 && !waitShot)
                        {
                            waitShot = true;      // 窗口变宽要等 HWND 真正 resize，下一格再截
                            return;
                        }
                        if (waitShot)
                        {
                            timer.Stop();
                            w.UpdateLayout();
                            Capture(w, "PCTools_ai_second.png");
                            Emit("再次更新   : 缓存已保留 2 份，窗口显示左右对比（宽 " +
                                 (int)w.ActualWidth + "）");
                            w.Close();
                            app.Shutdown();
                            return;
                        }
                        if (ticks > 120)
                        {
                            timer.Stop();
                            Emit("超时       : 120 秒内没有拿到结果（当前缓存 " + now + " 份）");
                            w.Close();
                            app.Shutdown();
                        }
                    }
                    catch (Exception ex)
                    {
                        timer.Stop();
                        Emit("截图       : 失败 - " + ex.Message);
                        w.Close();
                        app.Shutdown();
                    }
                };
                timer.Start();
                w.Show();
                app.Run();
                Emit("消息循环   : 正常退出");
            }
            catch (Exception ex)
            {
                Emit("失败       : " + ex.GetType().Name + " / " + ex.Message);
            }
        }

        // 配置窗自检：点一下「保存此配置」，确认写盘成功、窗口不关、累计用量不被清空
        private static void RunSmokeConfig()
        {
            Emit("PCTools 自检 / 保存单个配置");
            try
            {
                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                ThemeManager.Apply(app, ThemeManager.IsLight());

                AiConfig cfg = AiConfig.Load();
                AiUsageStore.Row before = null;
                Dictionary<int, AiUsageStore.Row> map = AiUsageStore.LoadAll();
                if (map.ContainsKey(cfg.Index)) before = map[cfg.Index];
                Emit("当前配置   : 第 " + cfg.Index + " 套 / " + cfg.Title);
                Emit("保存前用量 : " + (before == null ? "无记录"
                     : "输入 " + before.InTokens + " / 输出 " + before.OutTokens));

                AiSettingsWindow w = new AiSettingsWindow(cfg);
                w.Show();
                DispatcherTimer timer = new DispatcherTimer();
                timer.Interval = TimeSpan.FromSeconds(1.2);
                timer.Tick += delegate
                {
                    timer.Stop();
                    try
                    {
                        FrameworkElement root = (FrameworkElement)w.Content;
                        Button saveOne = (Button)LogicalTreeHelper.FindLogicalNode(root, "BtnSaveOne");
                        Emit("按钮       : " + (saveOne == null ? "未找到" : "已找到「" + saveOne.Content + "」"));
                        if (saveOne != null)
                        {
                            saveOne.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            w.UpdateLayout();
                            Emit("点击后     : Saved=" + w.Saved + " / 窗口仍打开=" + w.IsVisible);
                            TextBlock st = (TextBlock)LogicalTreeHelper.FindLogicalNode(root, "AiStatus");
                            Emit("状态栏     : " + (st == null ? "(空)" : st.Text));
                            AiUsageStore.Row after = null;
                            Dictionary<int, AiUsageStore.Row> m2 = AiUsageStore.LoadAll();
                            if (m2.ContainsKey(cfg.Index)) after = m2[cfg.Index];
                            Emit("保存后用量 : " + (after == null ? "无记录"
                                 : "输入 " + after.InTokens + " / 输出 " + after.OutTokens));
                        }
                        Capture(w, "PCTools_ai_settings_saved.png");
                        Emit("对话框     : 截图已导出");
                    }
                    catch (Exception ex) { Emit("失败       : " + ex.Message); }
                    w.Close();
                    app.Shutdown();
                };
                timer.Start();
                app.Run();
                Emit("消息循环   : 正常退出");
            }
            catch (Exception ex)
            {
                Emit("失败       : " + ex.GetType().Name + " / " + ex.Message);
            }
        }

        // 逐项分析自检：在启动项页跑 2 项，验证进度按成功推进、完成态停留与 Token 摘要
        private static void RunAnalyzeAll()
        {
            Emit("PCTools 自检 / 逐项 AI 分析");
            try
            {
                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                ThemeManager.Apply(app, ThemeManager.IsLight());
                MainWindow w = new MainWindow(true, IsAdmin());
                w.Show();
                w.RefreshAllSync();
                w.SelectPageForTest(0);
                Emit("列表条数   : " + w.Rows(0));
                Emit("统计条     : " + w.SummaryForTest());

                ProgressWindow seen = null;
                bool shot = false;
                bool probed = false;
                int ticks = 0;
                DateTime t0 = DateTime.Now;
                DispatcherTimer watch = new DispatcherTimer();
                watch.Interval = TimeSpan.FromMilliseconds(300);
                watch.Tick += delegate
                {
                    ticks++;
                    if (ticks > 400)
                    {
                        watch.Stop();
                        Emit("超时       : 120 秒内没有跑完（可能接口不通）");
                        w.Close();
                        app.Shutdown();
                        return;
                    }
                    // 非模态验证：窗口还在跑的时候切页，确认主界面真的能用
                    if (!shot && !probed)
                    {
                        foreach (Window win in Application.Current.Windows)
                        {
                            ProgressWindow pp = win as ProgressWindow;
                            if (pp == null || pp.Completed) continue;
                            probed = true;
                            w.SelectPageForTest(1);
                            int rows1 = w.Rows(1);
                            string sum1 = w.SummaryForTest();
                            w.SelectPageForTest(0);
                            Emit("非模态操作 : 分析进行中切到服务页，行数 " + rows1 +
                                 "，统计条「" + sum1 + "」");
                            break;
                        }
                    }
                    // 第一段：抓进度窗完成态
                    if (!shot)
                    {
                        foreach (Window win in Application.Current.Windows)
                        {
                            ProgressWindow p = win as ProgressWindow;
                            if (p == null || !p.Completed) continue;
                            seen = p;
                            try
                            {
                                p.UpdateLayout();
                                Capture(p, "PCTools_analyze_all_done.png");
                            }
                            catch (Exception ex) { Emit("截图       : 失败 - " + ex.Message); }
                            Emit("窗口形态   : 非模态，主界面同时可用（统计条 " + w.SummaryForTest() + "）");
                            shot = true;
                            p.Close();          // 关窗 → Closed → 收尾
                            return;
                        }
                        return;
                    }
                    // 第二段：等收尾复位后再退出
                    if (!w.AiBusyForTest)
                    {
                        watch.Stop();
                        Emit("耗时       : " + (int)(DateTime.Now - t0).TotalMilliseconds + " ms");
                        Emit("进度窗     : " + (seen == null ? "未出现" : "完成态已截图并关闭"));
                        if (seen != null)
                            Emit("完成摘要   : " + seen.Detail.Replace("\r", "").Replace("\n", " / "));
                        Emit("统计条     : " + w.SummaryForTest() + "（AI建议列应已有值）");
                        w.Close();
                        app.Shutdown();
                        return;
                    }
                };

                watch.Start();
                w.AnalyzeForTest(0, 2);        // 非模态：立刻返回，靠消息循环推进
                app.Run();
                Emit("消息循环   : 正常退出");
            }
            catch (Exception ex)
            {
                Emit("失败       : " + ex.GetType().Name + " / " + ex.Message);
            }
        }

        // 只刷建议的提示词自检：真实请求一次，看 token 与标签是否正常
        private static void RunAdviceOnly()
        {
            Emit("PCTools 自检 / 只刷新处理建议");
            AiConfig cfg = AiConfig.Load();
            Emit("模型       : " + cfg.Model + "（" + (cfg.Configured ? "已配置" : "未配置") + "）");
            if (!cfg.Configured) return;
            List<string> warn = new List<string>();
            List<ServiceItem> svcs = DataProvider.GetServices(warn);
            ServiceItem it = null;
            foreach (ServiceItem x in svcs) if (x.ServiceName == "Spooler") it = x;
            if (it == null && svcs.Count > 0) it = svcs[0];
            if (it == null) { Emit("失败       : 服务列表为空，没有可分析对象"); return; }

            string subject;
            string prompt = MainWindow.BuildAnalysisPrompt(1, it, out subject) +
                            Environment.NewLine + "这次的资料与上一次相同，只需要重新给出处理建议。";
            DateTime t0 = DateTime.Now;
            try
            {
                AiReply r = AiClient.AskReply(cfg, prompt, AiClient.SystemPromptAdviceOnly(1), null, 120000);
                Emit("耗时       : " + (int)(DateTime.Now - t0).TotalMilliseconds + " ms");
                Emit("用量       : 输入 " + r.InTokens + " / 输出 " + r.OutTokens);
                Emit("可解析     : " + (r.Parsed ? "是" : "否"));
                string intro, tips;
                AiResultWindow.Split(r.Text, out intro, out tips);
                Emit("介绍段长度 : " + intro.Length + "（应为 0 或很短）");
                string tag, rest;
                AiResultWindow.TryTag(tips, out tag, out rest);
                Emit("结论标签   : " + (tag.Length == 0 ? "(未识别)" : tag));
                string one = r.Text.Replace("\r", " ").Replace("\n", " / ");
                Emit("回答样例   : " + (one.Length > 220 ? one.Substring(0, 220) + "…" : one));
            }
            catch (Exception ex) { Emit("请求失败   : " + ex.Message); }
        }
#endif

        // 数据库自检：建库与五张表概况（支持工具，发布版保留）
        private static void RunDbCheck()
        {
            Emit("PCTools 自检 / 配置库");
            Emit("数据库     : " + AppDb.FilePath);
            Emit("文件状态   : " + (File.Exists(AppDb.FilePath)
                 ? (new FileInfo(AppDb.FilePath).Length / 1024) + " KB" : "不存在"));

            Emit("Ensure     : " + (AppDb.Ensure() ? "通过" : "失败 - " + AppDb.LastError));

            string err;
            IntPtr db = Sqlite.Open(AppDb.FilePath, out err);
            if (db == IntPtr.Zero) { Emit("打开失败   : " + err); return; }
            try
            {
                string[] tables = new string[] { "ai_config", "app_setting", "advice", "analysis", "usage" };
                foreach (string t in tables)
                {
                    List<string> rows = Sqlite.Column(db, "SELECT COUNT(*) FROM " + t + ";", out err);
                    Emit("  " + t + " : " + (rows.Count > 0 ? rows[0] : "?") + " 行");
                }
                List<string[]> list = Sqlite.Rows(db,
                    "SELECT config_index, IFNULL(name,''), IFNULL(model,''), IFNULL(api_key,'') " +
                    "FROM ai_config ORDER BY config_index;", out err);
                foreach (string[] p in list)
                {
                    if (p.Length < 4) continue;
                    Emit("  第 " + p[0] + " 套     : " + p[1] + " / " + p[2] + " / key " + ShortKey(p[3]));
                }
                List<string> act = Sqlite.Column(db,
                    "SELECT IFNULL(value,'') FROM app_setting WHERE key='ai.active';", out err);
                Emit("当前启用   : " + (act.Count > 0 ? act[0] : "(未设置)"));
                List<string> th = Sqlite.Column(db,
                    "SELECT IFNULL(value,'') FROM app_setting WHERE key='ui.theme';", out err);
                Emit("主题偏好   : " + (th.Count > 0 ? th[0] : "(未设置，跟随系统)"));
            }
            finally { Sqlite.Close(db); }

            string dbErr;
            AiConfig cfg = AiConfig.Load(out dbErr);
            Emit("生效配置   : " + cfg.Title + " / " + cfg.Model + " / " +
                 (cfg.Configured ? "已配置" : "未配置") +
                 (dbErr != null ? "（读库失败：" + dbErr + "）" : ""));
        }

        private static string ShortKey(string key)
        {
            // 只报长度、不回显任何片段：dbcheck 输出会写到 %TEMP%\PCTools_dbcheck.txt，
            // 对同机其它进程可读；以前打印前 4 + 后 4 位，密文残留场景会外泄真实 Key
            if (string.IsNullOrEmpty(key)) return "(空)";
            return "长度 " + key.Length + " 字符";
        }

#if DIAG
        // 补全口径自检：只统计三个列表的缺口，不发请求（零成本确认 KeySet 与按页取键都正确）
        private static void RunAnalyzeFill()
        {
            Emit("PCTools 自检 / 补全分析缺口（只统计，不请求）");
            List<string> warn = new List<string>();

            List<StartupItem> st = DataProvider.GetStartupItems(warn);
            string e;
            HashSet<string> h0 = AiAnalysisStore.KeySet(AiAnalysisStore.KindStartup, out e);
            if (e != null) Emit("启动项读失败 : " + e);
            int m0 = 0;
            foreach (StartupItem it in st)
                if (!h0.Contains(AiAnalysisStore.KeyStartup(it))) m0++;
            Emit("启动项     : 共 " + st.Count + " 项 / 已有 " + (st.Count - m0) + " / 缺失 " + m0);

            List<ServiceItem> sv = DataProvider.GetServices(warn);
            HashSet<string> h1 = AiAnalysisStore.KeySet(AiAnalysisStore.KindService, out e);
            if (e != null) Emit("服务读失败   : " + e);
            int m1 = 0;
            foreach (ServiceItem it in sv)
                if (!h1.Contains(AiAnalysisStore.KeyService(it))) m1++;
            Emit("服务       : 共 " + sv.Count + " 项 / 已有 " + (sv.Count - m1) + " / 缺失 " + m1);

            List<TaskItem> tk = DataProvider.GetTasks(warn);
            HashSet<string> h2 = AiAnalysisStore.KeySet(AiAnalysisStore.KindTask, out e);
            if (e != null) Emit("计划任务读失败: " + e);
            int m2 = 0;
            foreach (TaskItem it in tk)
                if (!h2.Contains(AiAnalysisStore.KeyTask(it))) m2++;
            Emit("计划任务   : 共 " + tk.Count + " 项 / 已有 " + (tk.Count - m2) + " / 缺失 " + m2);
            Emit("判定口径   : analysis 表里没有该项目的记录即算缺失（补全只请求这些）");
        }

        private static void RunSmokeDlg()
        {
            Emit("PCTools 自检 / 权限对话框");
            try
            {
                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                ThemeManager.Apply(app, ThemeManager.IsLight());
                PromptWindow p = new PromptWindow();
                DispatcherTimer timer = new DispatcherTimer();
                timer.Interval = TimeSpan.FromSeconds(2);
                timer.Tick += delegate
                {
                    timer.Stop();
                    try
                    {
                        p.UpdateLayout();
                        int ww = Math.Max(1, (int)p.ActualWidth);
                        int hh = Math.Max(1, (int)p.ActualHeight);
                        Brush oldBg = p.Background;
                        p.Background = new SolidColorBrush(Color.FromRgb(240, 243, 247));
                        RenderTargetBitmap bmp = new RenderTargetBitmap(ww, hh, 96, 96, PixelFormats.Pbgra32);
                        bmp.Render(p);
                        p.Background = oldBg;
                        PngBitmapEncoder enc = new PngBitmapEncoder();
                        enc.Frames.Add(BitmapFrame.Create(bmp));
                        string shot = Path.Combine(Path.GetTempPath(), "PCTools_dialog.png");
                        using (FileStream fs = new FileStream(shot, FileMode.Create)) enc.Save(fs);
                        Emit("对话框截图 : " + shot);
                    }
                    catch (Exception ex) { Emit("截图失败   : " + ex.Message); }
                    Emit("对话框     : 显示 2 秒后自动关闭");
                    p.Close();
                    app.Shutdown();
                };
                timer.Start();
                p.Show();
                Emit("对话框     : Show() 成功");
                app.Run();
                Emit("消息循环   : 正常退出");
            }
            catch (Exception ex) { Emit("失败       : " + ex.GetType().FullName + " / " + ex.Message); }
        }
#endif
        // ---- 自检/诊断实现结束 ----
    }
}
