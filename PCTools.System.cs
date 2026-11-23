// 系统层：右键菜单注册表、启动项/服务/任务/菜单枚举与操作
// PCTools - Windows 10/11 System Manager
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

namespace PCTools
{
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
            // 本地化资源串可能超过 512 字符：STRSAFE_E_INSUFFICIENT_BUFFER 时扩容重试，
            // 否则超长名称会静默退化成空串，列表只能拿 DLL 文件名兜底
            const int InsufficientBuffer = unchecked((int)0x8007007A);
            try
            {
                for (int cap = 512; cap <= 8192; cap *= 4)
                {
                    StringBuilder sb = new StringBuilder(cap);
                    int hr = SHLoadIndirectString(s, sb, sb.Capacity, IntPtr.Zero);
                    if (hr == 0) return sb.Length > 0 ? sb.ToString().Trim() : "";
                    if (hr != InsufficientBuffer) return "";
                }
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

        // 只接受本机盘符上的绝对路径（X:\...），排除网络盘与未知盘。注册表值可被任意写入，
        // @\\host\share\x.dll,-1 这类引用会让 shell/版本查询去连 UNC：触发 SMB 认证外泄，
        // 或在不可达时长时间阻塞枚举线程。本机固定盘、可移动介质与光驱都走本地设备栈，
        // 不会建立 SMB 连接，因此放行（U 盘上的便携工具也需要读取处理程序描述）。
        // 注意：Z:\ 形态的「映射网络驱动器」也走 SMB，必须靠 DriveType 排除，光看盘符不够。
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

        // Blocked 键的值名按 Windows 标准是 {CLSID}。打包扩展清单里的 Clsid
        // 常是 36 位裸 GUID，这里统一补花括号；已带括号的原样保留
        private static string BlockedName(string clsid)
        {
            if (string.IsNullOrEmpty(clsid)) return clsid;
            string t = clsid.Trim();
            if (t.Length == 36 && t[0] != '{') return "{" + t.ToUpperInvariant() + "}";
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
                        // 默认值已是真实 DLL：说明经典菜单由第三方工具接管。
                        // 这里写空串会顶掉对方的 InprocServer32，和关闭路径的保护不对称
                        string cur = Convert.ToString(k.GetValue(null));
                        if (!string.IsNullOrEmpty(cur))
                            return Manager.PartialPrefix + "经典菜单已由第三方工具接管，未修改其设置";
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
                    // 只删我们写入的 InprocServer32，不用 DeleteSubKeyTree 连根删：
                    // 该 CLSID 键下可能有第三方工具写入的其它子键，整树删除会误伤且不可恢复
                    Hive(machine).DeleteSubKey(ClassicPath + @"\InprocServer32", false);
                    // 父键已空才顺手删掉；仍有其它子键时保留，交由系统/第三方管理
                    TryDeleteEmptyKey(machine, ClassicPath);
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
            if (it.Kind == CtxKind.Classic)
            {
                string err = SetClassic(true, false);
                if (err != null) return err;
                // 「对所有用户禁用」会删掉 HKLM 的覆盖壳；管理员点启用时一并恢复，
                // 否则系统级经典菜单被删除后永远没有恢复通道。普通用户只影响自己
                if (Program.IsAdmin() && !IsClassicEnabled(true))
                {
                    string merr = SetClassic(true, true);
                    if (merr != null) return merr;
                }
                if (!IsClassicEnabled(false) && !IsClassicEnabled(true))
                    return Manager.PartialPrefix + "已写入当前用户设置，但系统级经典菜单仍不可用";
                return null;
            }

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
                // 它们不受 StartupApproved 控制，界面上只读展示，不提供启停，
                // 因此不需要传 Approval 路径
                new RunSource(@"Software\Microsoft\Windows\CurrentVersion\RunOnce", null, false, "当前用户", "注册表 RunOnce"),
                new RunSource(@"Software\Microsoft\Windows\CurrentVersion\RunOnce", null, true, "所有用户", "注册表 RunOnce"),
                new RunSource(@"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce", null, true, "所有用户 (32 位)", "注册表 RunOnce"),
                new RunSource(@"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce", null, false, "当前用户 (32 位)", "注册表 RunOnce")
                // 注：Policies\Explorer\Run 由组策略下发，不受 StartupApproved 控制，
                // 列出来只会得到一个「点了禁用却没效果」的按钮，故不纳入
            };
            foreach (RunSource src in regs)
            {
                // 32 位系统上 HKLM\Software\WOW6432Node\... 会被重定向到同一个键，
                // 再枚举一次会让同一启动项显示两份，且禁用后两份状态一起变
                if (!Environment.Is64BitOperatingSystem && src.Machine &&
                    src.Reg.IndexOf("WOW6432Node", StringComparison.OrdinalIgnoreCase) >= 0) continue;
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
                            PaintStartupStatus(it);
                            it.Location = src.Location;
                            it.Scope = src.Scope;
                            it.Command = cmd;
                            it.ApprovalSubPath = src.Approval;
                            it.ApprovalName = n;
                            it.SourceRegPath = src.Reg;
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
                        PaintStartupStatus(it);
                        it.Location = "启动文件夹";
                        it.Scope = f.Scope;
                        it.Command = target;
                        it.ApprovalSubPath = f.Approval;
                        it.ApprovalName = fileName;
                        it.SourceFilePath = file;
                        it.Machine = f.Machine;
                        it.Search = (fileName + " " + target + " " + f.Scope + " 启动文件夹").ToLowerInvariant();
                        list.Add(it);
                    }
                }
                catch (Exception ex) { AddWarn(warn, "启动文件夹 " + f.Dir + "：" + ex.Message); }
            }
            AiAdviceStore.FillStartup(list);
            BackupStore.FillStartup(list);
            return list;
        }

        internal static bool IsDisabled(string approvalSubPath, bool machine, string valueName)
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
                // .url 是 INI 格式，正常只有几十字节：先限大小，避免把超大/畸形文件
                // 整个读进内存（枚举线程上发生就是一次长时间卡顿）
                FileInfo fi = new FileInfo(path);
                if (!fi.Exists || fi.Length > 64 * 1024) return path;
                // 系统默认按 ANSI 代码页保存；先按严格 UTF-8 解，
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
                {
                // EnumerationOptions.Timeout 默认 TimeSpan.MaxValue：WMI 假死时
                // Get() 会永久阻塞，后台加载线程无法返回
                searcher.Options.Timeout = TimeSpan.FromSeconds(20);
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
                                PaintServiceStatus(it);
                                string mode = Str(mo, "StartMode");
                                bool delayed;
                                it.StartType = StartTypeWithDelay(it.ServiceName, mode, out delayed);
                                // 每用户服务实例：实例值只在创建时抄自模板、之后不随修改更新，
                                // 改为显示模板服务的有效启动类型，修改后列表才能立刻反映
                                int? tpl = Manager.TemplateStart(it.ServiceName);
                                if (tpl.HasValue)
                                {
                                    string tm = tpl.Value == 2 ? "Auto" : (tpl.Value == 3 ? "Manual" : (tpl.Value == 4 ? "Disabled" : "Auto"));
                                    it.StartType = StartTypeWithDelay(Manager.TemplateName(it.ServiceName), tm, out delayed);
                                }
                                // 延迟启动不再显示成「自动（延迟）」，但信息保留在详情与搜索串里
                                it.DelayedAutoStart = delayed;
                                // 着色统一走一份判定：初次绘制与 RepaintItems 不能各写一套
                                Manager.PaintStartType(it);
                                it.ServiceType = MapServiceType(Str(mo, "ServiceType"));
                                it.Account = Str(mo, "StartName");
                                it.Path = Str(mo, "PathName");
                                if (string.IsNullOrWhiteSpace(it.Path)) it.Path = "—";
                                if (string.IsNullOrWhiteSpace(it.Name)) it.Name = it.ServiceName;
                                it.Search = (it.Name + " " + it.ServiceName + " " + it.Path + " " + it.Account + " " +
                                             it.StartType + " " + it.ServiceType +
                                             (delayed ? " 延迟自动启动" : "")).ToLowerInvariant();
                                it.Detail = it.Name + "\n服务名称：" + it.ServiceName + "\n登录身份：" + it.Account + "\n路径：" + it.Path;
                                if (delayed) it.Detail += "\n启动方式：延迟自动启动";
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
            }
            catch (Exception ex) { AddWarn(warn, "服务读取失败：" + ex.Message); }
            AiAdviceStore.FillServices(list);
            BackupStore.FillServices(list);
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

        // WMI 的 StartMode 不区分「自动（延迟）」：延迟启动只体现在注册表
        // DelayedAutoStart=1。界面按需求统一显示「自动」，延迟标记通过 out 参数
        // 留给详情与搜索串，不再出现在启动类型列里
        private static string StartTypeWithDelay(string serviceName, string mode, out bool delayed)
        {
            delayed = false;
            string mapped = MapMode(mode);
            if (mapped != "自动") return mapped;
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\" + serviceName))
                {
                    object v = k == null ? null : k.GetValue("DelayedAutoStart");
                    if (v != null && Convert.ToInt32(v) == 1) delayed = true;
                }
            }
            catch { }
            return mapped;
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
            AiAdviceStore.FillTasks(list);
            BackupStore.FillTasks(list);
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
                case 1: it.Status = "已禁用"; break;
                case 2: it.Status = "排队中"; break;
                case 3: it.Status = "就绪"; break;
                case 4: it.Status = "正在运行"; break;
                default: it.Status = "未知"; break;
            }
            // Enabled 与 State 是两件事：运行中的任务 State=4，但仍是「已启用」。
            // 备份基线必须按 Enabled 记录，不能把「正在运行」当成一种启用状态存下来
            try { it.Enabled = Convert.ToBoolean(t.Enabled); }
            catch { it.Enabled = state != 1; }   // 取不到时退回 State：只有 1 表示已禁用
            PaintTaskStatus(it);
            it.Trigger = "无触发器";
            it.Action = "—";
            bool defFailed = false;
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
            catch (Exception ex)
            {
                // 「读不到」与「真的没有」必须区分：受保护目录下的任务在非管理员下
                // 常抛 UnauthorizedAccessException，显示「无触发器」会给出错误结论
                defFailed = true;
                it.Trigger = "读取失败";
                it.Action = "读取失败";
                DiagLog.Swallow("读取任务定义 " + it.FullPath, ex);
            }
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
            if (defFailed)
                it.Detail += "\n（任务定义读取失败：可能权限不足，需以管理员身份运行后重试）";
            return it;
        }

        private static string FormatTriggers(dynamic coll)
        {
            List<string> parts = new List<string>();
            // -1 表示连 Count 都没读到：与「确实是 0 个触发器」区分开
            int total = -1;
            int lost = 0;
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
                    catch { lost++; }
                    finally { ReleaseCom(tr); }
                }
            }
            catch { }
            if (parts.Count == 0)
                return total > 0 ? "触发器读取失败（" + total + " 项）" : "无触发器";
            string r = string.Join("；", parts.Distinct().ToArray());
            // 先截断明细再追加总数：反过来会把「…共 N 个触发器」本身截掉
            if (r.Length > 110) r = r.Substring(0, 110) + "…";
            if (total > 12) r += "；…共 " + total + " 个触发器";
            if (lost > 0) r += "；另有 " + lost + " 项未读取";
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
            // -1 表示 Count 读取失败：与「确实没有操作」区分开
            int total = -1;
            int lost = 0;
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
                    catch { lost++; }
                    finally { ReleaseCom(act); }
                }
            }
            catch { }
            if (parts.Count == 0) return total > 0 ? "操作读取失败（" + total + " 项）" : "—";
            // 与 FormatTriggers 同口径：先截明细再追加总数，避免「共 N 个操作」被截掉
            string r = string.Join("；", parts.ToArray());
            if (r.Length > 150) r = r.Substring(0, 150) + "…";
            if (total > 8) r += "；…共 " + total + " 个操作";
            if (lost > 0) r += "；另有 " + lost + " 项未读取";
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
            AiAdviceStore.FillCtx(list);
            BackupStore.FillCtx(list);
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
            // 只需知道「能否列出」，惰性枚举取到第一个条目即停；
            // 枚举器必须 Dispose，否则每次刷新都会泄漏一个 FindFirstFile 句柄
            try
            {
                using (IEnumerator<string> e = Directory.EnumerateDirectories(dir).GetEnumerator())
                    return e.MoveNext();
            }
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

        // AppxManifest 解析缓存：Win11 上通常有 150–300 个包，每次刷新都全量
        // XDocument.Load + 整树扫描会让右键页多花数百毫秒到数秒，而清单在会话内几乎不变。
        // 按「路径 + 最后写入时间」失效，最多缓存 300 个包
        private static readonly Dictionary<string, XDocument> manifestCache =
            new Dictionary<string, XDocument>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, long> manifestStamp =
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private static readonly object manifestGate = new object();

        private static XDocument LoadManifest(string path)
        {
            long stamp;
            try { stamp = File.GetLastWriteTimeUtc(path).Ticks; }
            catch { return null; }
            lock (manifestGate)
            {
                XDocument hit;
                long old;
                if (manifestCache.TryGetValue(path, out hit) &&
                    manifestStamp.TryGetValue(path, out old) && old == stamp) return hit;
            }
            try
            {
                XDocument doc = XDocument.Load(path, LoadOptions.None);
                lock (manifestGate)
                {
                    if (manifestCache.Count >= 300)
                    {
                        manifestCache.Clear();
                        manifestStamp.Clear();
                    }
                    manifestCache[path] = doc;
                    manifestStamp[path] = stamp;
                }
                return doc;
            }
            catch { return null; }
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
            XDocument doc = LoadManifest(manifestPath);
            if (doc == null)
            {
                // 清单损坏/编码异常的包被跳过要有记录，否则排查「某个打包菜单项为什么没列出」无据可查
                AddWarn(warn, "清单解析 " + pkg + "：读取或解析失败");
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

        // ---------------- 统一着色：枚举阶段与 RepaintItems 共用，避免两处规则漂移 ----------------
        internal static void PaintStartupStatus(StartupItem it)
        {
            if (it.OneShot)
            {
                it.StatusBrush = Palette.Blue;
                it.StatusBgBrush = Palette.BlueBg;
                return;
            }
            bool off = it.Status == "已禁用";
            it.StatusBrush = off ? Palette.Red : Palette.Green;
            it.StatusBgBrush = off ? Palette.RedBg : Palette.GreenBg;
        }

        internal static void PaintServiceStatus(ServiceItem it)
        {
            bool running = it.Status == "正在运行";
            bool stopped = it.Status == "已停止";
            it.StatusBrush = running ? Palette.Green : (stopped ? Palette.Gray : Palette.Orange);
            it.StatusBgBrush = running ? Palette.GreenBg : (stopped ? Palette.GrayBg : Palette.OrangeBg);
        }

        internal static void PaintTaskStatus(TaskItem it)
        {
            Brush fg = Palette.Gray;
            Brush bg = Palette.GrayBg;
            if (it.Status == "就绪") { fg = Palette.Green; bg = Palette.GreenBg; }
            else if (it.Status == "正在运行") { fg = Palette.Blue; bg = Palette.BlueBg; }
            else if (it.Status == "已禁用") { fg = Palette.Red; bg = Palette.RedBg; }
            else if (it.Status == "排队中") { fg = Palette.Orange; bg = Palette.OrangeBg; }
            it.StatusBrush = fg;
            it.StatusBgBrush = bg;
        }

        internal static void ApplyCtxColors(CtxMenuItem it)
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

        // 服务启动类型的统一着色：初次枚举与 RepaintItems 共用。
        // 「自动（延迟）」同样是自动启动，必须与「自动」同色，
        // 否则任一 RepaintItems（切主题、操作后重绘）都会把它画成灰色
        internal static void PaintStartType(ServiceItem it)
        {
            bool disabled = it.StartType == "已禁用";
            bool auto = it.StartType == "自动" || it.StartType == "自动（延迟）";
            it.StartTypeBrush = disabled ? Palette.Red : (auto ? Palette.Blue : Palette.Neutral);
            it.StartTypeBgBrush = disabled ? Palette.RedBg : (auto ? Palette.BlueBg : Palette.GrayBg);
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
            catch (UnauthorizedAccessException)
            {
                return "权限不足：该操作需要管理员权限，请以管理员身份重启后重试";
            }
            catch (ManagementException ex)
            {
                DiagLog.Swallow("执行 " + op + " 页 " + page, ex);
                return "WMI 操作失败：" + ex.Message;
            }
            catch (Exception ex)
            {
                // 不再把框架异常的英文参数名直接甩给用户：分类后附类型名，完整堆栈进诊断日志
                DiagLog.Swallow("执行 " + op + " 页 " + page, ex);
                return "操作失败（" + ex.GetType().Name + "）：" + ex.Message;
            }
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
                // 列表可能是几分钟前加载的：源条目已被卸载程序删除时，写 StartupApproved
                // 只会留下一个永远残留、界面也看不到的孤儿开关，却报告“成功”
                if (!string.IsNullOrEmpty(si.SourceRegPath))
                {
                    using (RegistryKey src = hive.OpenSubKey(si.SourceRegPath))
                    {
                        if (src == null || src.GetValue(si.ApprovalName) == null)
                            return "该启动项已不存在（可能已被其它程序删除），请刷新列表后重试";
                    }
                }
                else if (!string.IsNullOrEmpty(si.SourceFilePath) && !File.Exists(si.SourceFilePath))
                    return "该启动项文件已不存在，请刷新列表后重试";

                using (RegistryKey k = hive.CreateSubKey(si.ApprovalSubPath))
                {
                    if (k == null) return "无法打开注册表项：" + si.ApprovalSubPath;
                    k.SetValue(si.ApprovalName, enabled ? ApprovalOn : ApprovalOff, RegistryValueKind.Binary);
                }
                // 回读校验：安全软件/组策略可能立刻改写该值，报成功但实际未生效更糟
                if (DataProvider.IsDisabled(si.ApprovalSubPath, si.Machine, si.ApprovalName) != !enabled)
                    return "写入后回读状态不一致，可能被安全软件或组策略拦截；请刷新列表确认";
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return "权限不足：无法写入 " + (si.Machine ? "HKLM" : "HKCU") + " 注册表，请以管理员身份运行";
            }
            catch (Exception ex) { return ex.Message; }
        }

        // 读取服务的 Start 值（0=Boot，1=System，2=Auto，3=Manual，4=Disabled）。
        // 服务名来自 SCM 枚举，不含反斜杠；读不到返回 -1。
        // 调用方必须把 -1 当作「不可判定」并拒绝操作（fail-closed）：注册表读不到时
        // 若继续交给 WMI 改，等于把唯一的 Boot/System 判据静默跳过，误改后系统可能无法引导。
        private static int ServiceStartValue(string name)
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\" + name))
                {
                    object v = k == null ? null : k.GetValue("Start");
                    return v == null ? -1 : Convert.ToInt32(v);
                }
            }
            catch (Exception ex)
            {
                DiagLog.Swallow("读取服务启动类型 " + name, ex);
                return -1;
            }
        }

        // 改启动类型（WMI ChangeServiceConfig，全程走 SCM 权限检查）。
        // 已知场景：每用户服务实例（如 cbdhsvc_729e7e / CDPUserSvc_729e7e）WMI 返回 21（无效参数），
        // 因为实例的启动类型由模板派生、登录会话重建时覆盖；正确做法是改其模板服务。
        private static string ChangeStartMode(string name, string mode)
        {
            // 只允许在 自动 / 手动 / 禁用 之间转换：0/1 没有写回通道，传进来就是调用方 bug
            if (mode != "Automatic" && mode != "Manual" && mode != "Disabled")
                return "不支持的启动类型：" + mode;

            string target = TemplateName(name);   // 每用户实例 → 模板服务
            // Boot/System 驱动（Start=0/1）是引导链的一部分：改成 Automatic/Disabled
            // 后重启可能直接 0x7B 无法引导，且本工具没有写回 0/1 的通道。
            // 读不到启动类型（-1）时同样拒绝：判据失效时继续改就是 fail-open
            int cur = ServiceStartValue(target);
            if (cur < 0)
                return "无法读取服务「" + target + "」的启动类型（注册表不可访问），" +
                       "为避免误改引导/系统驱动，已拒绝修改；请以管理员身份运行后重试";
            if (cur == 0 || cur == 1)
                return "该服务是引导启动/系统启动类型（Start=" + cur +
                       "），修改可能导致系统无法启动；本工具不提供此操作";

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
                    // ObjectGetOptions.Timeout 只约束 Get；InvokeMethod 用的是另一套
                    // InvokeMethodOptions，默认 Timeout = TimeSpan.MaxValue。
                    // WMI 提供程序假死时会让后台线程永久阻塞、进度窗无法关闭，必须显式设上限
                    mo.Options.Timeout = TimeSpan.FromSeconds(20);
                    mo.Get();
                    // 参数包 + options 重载：ManagementObject 没有 (string, object[], options) 形态
                    InvokeMethodOptions opt = new InvokeMethodOptions();
                    opt.Timeout = TimeSpan.FromSeconds(20);
                    using (ManagementBaseObject inParams = mo.GetMethodParameters("ChangeStartMode"))
                    {
                        inParams["StartMode"] = mode;
                        using (ManagementBaseObject outParams = mo.InvokeMethod("ChangeStartMode", inParams, opt))
                            return (int)Convert.ToUInt32(outParams["ReturnValue"]);
                    }
                }
            }
            catch (Exception ex) { exMsg = ex.Message; return -1; }
        }

        // 每用户服务实例：名称形如「模板名_十六进制后缀」，且同名模板服务的类型确实带
        // USER_SERVICE 位（0x40）。只按名字猜会把恰好带 _十六进制 后缀的普通第三方服务
        // 误判成实例，从而把启动类型改到同名的另一个服务上，所以必须用服务类型位确证。
        // 后缀形态的纯字符串判定只在这里实现；AiBatch 与服务管理共用，
        // 避免同一个服务在批量请求和服务管理两处被认成不同的名字
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

        // 去掉名称末尾的 _十六进制 实例后缀；全程序唯一一份包装（AiBatch 复用）
        internal static string StripInstanceTail(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string t = HexSuffixBase(s);
            return t == null ? s : t;
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
                        // 已禁用服务直接 Start 会抛原始英文异常；引导/系统驱动也不该这样拉起。
                        // 读不到启动类型时按 fail-closed 处理：宁可拒绝，也不误启动引导驱动
                        int sv = ServiceStartValue(name);
                        if (sv < 0) return "无法读取服务的启动类型，已拒绝启动操作（避免误启动引导/系统驱动）";
                        if (sv == 4) return "服务已禁用，请先把启动类型改为「手动」或「自动」再启动";
                        if (sv == 0 || sv == 1)
                            return "该服务是引导启动/系统启动类型的驱动，不能这样启动，请重启系统";
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

        // 系统工具的绝对路径：便携版常放在可被低权限用户写入的目录，
        // CreateProcess 会先搜索应用目录，裸文件名存在同名程序劫持的提权面
        private static string SystemTool(string name)
        {
            try
            {
                string p = Path.Combine(Environment.SystemDirectory, name);
                if (File.Exists(p)) return p;
            }
            catch { }
            return name;
        }

        private static string WindowsTool(string name)
        {
            try
            {
                string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), name);
                if (File.Exists(p)) return p;
            }
            catch { }
            return name;
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
                ProcessStartInfo psi = new ProcessStartInfo(SystemTool("schtasks.exe"), args);
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
                    ProcessStartInfo psi = new ProcessStartInfo(SystemTool("taskkill.exe"),
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
                ProcessStartInfo psi = new ProcessStartInfo(SystemTool("runas.exe"),
                    "/trustlevel:0x20000 \"" + WindowsTool("explorer.exe") + "\"");
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
                using (Process.Start(WindowsTool("explorer.exe"))) { }
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
        private static bool versionProbed;
        private static int vMajor = 10;
        private static int vMinor;
        private static int vBuild;
        private static byte vProductType = 1;

        private static void ProbeVersion()
        {
            if (versionProbed) return;
            versionProbed = true;
            try
            {
                RTL_OSVERSIONINFOEXW v = new RTL_OSVERSIONINFOEXW();
                v.dwOSVersionInfoSize = Marshal.SizeOf(typeof(RTL_OSVERSIONINFOEXW));
                if (RtlGetVersion(ref v) == 0)
                {
                    vMajor = v.dwMajorVersion;
                    vMinor = v.dwMinorVersion;
                    vBuild = v.dwBuildNumber;
                    vProductType = v.wProductType;
                }
            }
            catch { }
        }

        // winsqlite3.dll 从 Windows 10 1607（10.0.14393）起随系统内置。
        // 更早的系统上配置、主题与 AI 结果全部不可用，启动时直接说明并退出
        internal static bool Supported
        {
            get
            {
                ProbeVersion();
                // 探测失败（极少数受限环境）不能反过来阻止程序启动：按支持处理，
                // 后续 SQLite 打不开时会有明确的「配置库不可用」提示
                if (vBuild <= 0) return true;
                return vMajor > 10 || (vMajor == 10 && vBuild >= 14393);
            }
        }

        internal static string Describe()
        {
            if (cached != null) return cached;

            ProbeVersion();
            int major = vMajor;
            int minor = vMinor;
            int build = vBuild;
            byte productType = vProductType;    // 1 = 工作站，非 1 = 服务器

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

            // 本工具只支持 Windows 10 / 11（winsqlite3.dll 从 Win10 1607 起内置）。
            // Server 的 build 号与桌面版重叠（Server 2025 = 26100），只看 build 会把
            // Server 2025 报成 Windows 11，因此必须先按 productType 分流
            string name;
            if (productType != 1)
            {
                name = build >= 26100 ? "Windows Server 2025"
                     : build >= 20348 ? "Windows Server 2022"
                     : build >= 17763 ? "Windows Server 2019"
                     : build >= 14393 ? "Windows Server 2016"
                     : "Windows Server (build " + build + ")";
            }
            else if (major > 10 || build >= 22000)
            {
                name = "Windows 11";
                if (string.IsNullOrEmpty(display))
                    display = build >= 26200 ? "25H2" : build >= 26100 ? "24H2"
                            : build >= 22631 ? "23H2" : build >= 22621 ? "22H2" : "21H2";
            }
            else if (major == 10) name = "Windows 10";
            else name = "Windows " + major + "." + minor + "（不受支持，需 Windows 10 及以上）";

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
}
