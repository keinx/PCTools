// 存储层：诊断日志、数据目录、SQLite 封装与各配置/缓存表
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
            // 单次探测失败不能直接判定目录不可写：杀软实时扫描新建文件、磁盘瞬时忙、
            // 另一个实例同时探测同名文件都会让一次 Create 失败，进而把数据目录
            // 永久切到 LocalAppData，出现两份库交替生效
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    // 随机文件名 + CreateNew：固定名探测文件会被同目录的低权限进程
                    // 预置成符号链接/硬链接，FileMode.Create 跟随链接后就能让本程序
                    // （提权后是管理员令牌）截断任意可写文件
                    string probe = Path.Combine(dir, "." + Path.GetRandomFileName());
                    using (FileStream fs = new FileStream(probe, FileMode.CreateNew, FileAccess.Write,
                                                          FileShare.None, 1, FileOptions.DeleteOnClose))
                    { }
                    return true;
                }
                catch
                {
                    if (attempt < 2) Thread.Sleep(120);
                }
            }
            return false;
        }

        // 把 exe 侧配置库复制到回退目录。目标已存在时必须比较时间：
        // 目标较新说明用户已在 LocalAppData 侧更新过，不能被 exe 侧的旧库覆盖；
        // 复制走临时文件 + 原子替换，避免中断后留下截断的库让后续启动永远打不开
        private static bool CopyDatabaseIfNeeded(string source, string target)
        {
            string tmp = null;
            try
            {
                if (File.Exists(target))
                {
                    if (File.GetLastWriteTimeUtc(target) >= File.GetLastWriteTimeUtc(source)) return true;
                }
                // 临时文件同样用随机名 + 不覆盖：预置同名链接可让 File.Copy 跟随链接，
                // 把目标指向的任意文件截断
                tmp = target + "." + Path.GetRandomFileName() + ".tmp";
                File.Copy(source, tmp, false);
                FileAttributes attr = File.GetAttributes(tmp);
                if ((attr & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(tmp, attr & ~FileAttributes.ReadOnly);
                if (new FileInfo(tmp).Length == 0)
                {
                    File.Delete(tmp);
                    tmp = null;
                    return File.Exists(target);
                }
                // target 若本身是链接，Delete 只删链接项，不会写到链接目标
                if (File.Exists(target)) File.Delete(target);
                File.Move(tmp, target);
                tmp = null;
                return true;
            }
            catch (Exception ex)
            {
                DiagLog.Swallow("复制配置库 " + Path.GetFileName(source), ex);
                return File.Exists(target);
            }
            finally
            {
                if (tmp != null)
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                }
            }
        }

        // 目录可写不代表库文件可读写：从只读介质复制、安装器收紧文件 ACL、
        // 杀软把库锁成只读时，目录探测会通过，但 SQLite 每次都打不开。
        // 这里按实际打开方式（读写）试一次，失败就走回退逻辑
        private static bool CanOpenDb(string db)
        {
            try
            {
                using (FileStream fs = new FileStream(db, FileMode.Open, FileAccess.ReadWrite,
                                                      FileShare.ReadWrite))
                { }
                return true;
            }
            catch (Exception ex)
            {
                DiagLog.Swallow("打开配置库 " + Path.GetFileName(db), ex);
                return false;
            }
        }

        private static string ComputeDataDir()
        {
            string exeDb = Path.Combine(exeDir, "PCToolsAI.sqlite");
            string local = LocalDataDir();
            string localDb = Path.Combine(local, "PCToolsAI.sqlite");
            bool sameDir = string.Equals(local, exeDir, StringComparison.OrdinalIgnoreCase);
            // exe 放在网络共享时 SQLite 会直接在 SMB 上读写：断线/多会话可能损坏库，
            // 且每次 stat 都要过网络。强制回退到本机 LocalAppData
            bool uncExe = exeDir.StartsWith(@"\\", StringComparison.Ordinal);

            // 1) exe 目录已有数据库：目录与库文件都可用才沿用（便携语义）。
            //    只看目录可写不够：库被设成只读时 sqlite 以读写方式打开一定失败，
            //    整个配置层会不可用且无法自愈
            if (!uncExe && File.Exists(exeDb) && CanWrite(exeDir) && CanOpenDb(exeDb))
            {
                // 两个位置都有库时选较新的：曾因目录不可写回退到 LocalAppData，
                // 目录恢复可写后若无条件用 exe 侧旧库，用户会看到配置「回到旧版本」。
                // 0 字节的库（上次写入中途失败）不参与比较，否则会顶掉有数据的旧库
                if (!sameDir && File.Exists(localDb) && new FileInfo(localDb).Length > 0 &&
                    CanOpenDb(localDb) &&
                    File.GetLastWriteTimeUtc(localDb) > File.GetLastWriteTimeUtc(exeDb) &&
                    CanWrite(local))
                {
                    Writability = true;
                    return local;
                }
                Writability = true;
                return exeDir;
            }
            // 2) exe 侧有库但不可用：复制到 LocalAppData 后沿用
            if (!uncExe && File.Exists(exeDb))
            {
                if (!sameDir)
                {
                    try
                    {
                        Directory.CreateDirectory(local);
                        if (CanWrite(local) &&
                            CopyDatabaseIfNeeded(exeDb, localDb) && CanOpenDb(localDb))
                        {
                            Writability = true;
                            return local;
                        }
                    }
                    catch (Exception ex) { DiagLog.Swallow("回退数据目录", ex); }
                }
                // 复制失败且目标不可用：宁可明确报「不可写」，也不要在 alt 新建空库
                Writability = false;
                return exeDir;
            }
            // 3) 之前因不可写而落到 LocalAppData：沿用，避免移动 exe 后配置分裂
            if (!sameDir && File.Exists(localDb))
            {
                Writability = CanWrite(local);
                return local;
            }
            // 4) 新装：能写就放 exe 同目录（保持便携），否则回落
            if (!uncExe && CanWrite(exeDir)) { Writability = true; return exeDir; }
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
        // Ensure 的快速路径在 lock(gate) 外读取它，必须与 ready 一样保证跨线程可见
        private static volatile bool aclDone;
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
                Recreated = false;
                RecreatedFrom = null;
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

        // 表结构版本：写进 SQLite 的 user_version。
        // 旧版本库不再做增量迁移：按产品要求直接删除并新建（见 Ensure）；
        // Migrate 仅保留给「同版本库被外部工具改坏、偶发缺列」的修复路径
        private const int SchemaVersion = 2;

        // 本次启动是否因旧版本库而删库重建：界面需要明确告知一次，不能静默丢配置
        internal static bool Recreated;
        internal static string RecreatedFrom;
        // 同版本补列检查每个进程只做一次，避免每次文件写入后都跑 19 次 PRAGMA
        private static bool repairChecked;
        // 任务基线曾在旧版本中记录运行态（就绪/正在运行/排队中），语义上都等于「已启用」。
        // 每进程归一一次，避免导出的 .pct 把运行态当成启用状态带出去
        private static bool backupValuesChecked;

        // 五张表：AI 配置、全局键值、AI 建议、单条分析缓存、每套配置的累计用量
        internal const string Schema =            "CREATE TABLE IF NOT EXISTS ai_config (" +
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
            "out_tokens INTEGER DEFAULT 0, calls INTEGER DEFAULT 0, updated TEXT);" +
            // 覆盖式备份基线：每个项目只保留最近一次“备份状态/备份启动类型”
            "CREATE TABLE IF NOT EXISTS backup_state (" +
            "kind TEXT NOT NULL, item_key TEXT NOT NULL, name TEXT, value TEXT NOT NULL, " +
            "updated TEXT, PRIMARY KEY (kind, item_key));";

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
                IntPtr db = Sqlite.Open(FilePath, out LastError, 1500);
                if (db == IntPtr.Zero) { ready = false; return false; }
                try
                {
                    int ver = SchemaVersionOf(db);
                    // 库版本高于当前程序：可能是更新版 PCTools 建的，不能删。
                    // 标记不可用并说明原因，避免旧程序把新结构写坏
                    if (ver > SchemaVersion)
                    {
                        LastError = "配置库版本 " + ver + " 高于本程序支持的版本 " + SchemaVersion +
                                    "，请使用较新版本的 PCTools";
                        ready = false;
                        return false;
                    }
                    // 旧版本库（含没有版本号但有业务表的历史库）不再迁移：直接删除并新建。
                    // ver==0 且没有任何业务表 = 全新空库，走正常建表
                    if ((ver > 0 && ver < SchemaVersion) || (ver == 0 && HasUserTables(db)))
                    {
                        string old = ver > 0 ? "v" + ver : "无版本号";
                        Sqlite.Close(db);
                        db = IntPtr.Zero;
                        if (!ResetDatabaseFiles(old)) { ready = false; return false; }
                        db = Sqlite.Open(FilePath, out LastError, 1500);
                        if (db == IntPtr.Zero) { ready = false; return false; }
                        ver = 0;
                    }

                    string e = Sqlite.Exec(db, Schema);
                    if (e != null) { LastError = e; ready = false; return false; }
                    if (ver < SchemaVersion)
                    {
                        // 新建/重建的库：Schema 已是最新结构，只写版本号
                        Sqlite.Exec(db, "PRAGMA user_version=" + SchemaVersion + ";");
                    }
                    else if (!repairChecked)
                    {
                        // 同版本库的偶发缺列修复：每进程一次，保留 Migrate 的兜底价值
                        Migrate(db);
                        repairChecked = true;
                    }
                    if (!backupValuesChecked)
                    {
                        // 旧版把任务的运行态写进了基线：就绪/正在运行/排队中 → 已启用。
                        // 幂等 UPDATE，只影响语义错误的行；新库无行时不产生写入
                        string ne = Sqlite.Exec(db,
                            "UPDATE backup_state SET value='已启用' WHERE kind='task' " +
                            "AND value IN ('就绪','正在运行','排队中');");
                        if (ne == null) backupValuesChecked = true;
                    }
                    ready = true;
                    LastError = "";
                    RememberStamp();
                    HardenFileAcl();
                    return true;
                }
                finally { if (db != IntPtr.Zero) Sqlite.Close(db); }
            }
        }

        // 幂等补列：表名与列名全部硬编码，不接受外部输入。
        // PRAGMA table_info 返回 cid/name/type/notnull/dflt_value/pk，name 在下标 1
        private static int SchemaVersionOf(IntPtr db)
        {
            string err;
            List<string> rows = Sqlite.Column(db, "PRAGMA user_version;", out err);
            int v;
            if (err != null || rows.Count == 0 || !int.TryParse(rows[0], out v)) return -1;
            return v;
        }

        // 库里是否已有本程序的业务表：用于区分「全新空库」与「没有版本号的历史库」
        private static bool HasUserTables(IntPtr db)
        {
            string err;
            List<string> rows = Sqlite.Column(db,
                "SELECT count(*) FROM sqlite_master WHERE type='table' AND name IN " +
                "('ai_config','app_setting','advice','analysis','usage','backup_state');", out err);
            int n;
            return err == null && rows.Count > 0 && int.TryParse(rows[0], out n) && n > 0;
        }

        // 删除旧版配置库及其伴生文件（-wal/-shm/-journal）。
        // 删除后由调用方重新建库；失败时保留原文件并返回 false
        private static bool ResetDatabaseFiles(string oldVersion)
        {
            try
            {
                string[] suffixes = new string[] { "", "-wal", "-shm", "-journal" };
                foreach (string suffix in suffixes)
                {
                    string p = FilePath + suffix;
                    if (File.Exists(p)) File.Delete(p);
                }
                Recreated = true;
                RecreatedFrom = oldVersion;
                DiagLog.Note("旧版配置库（" + oldVersion + "）已删除并按新版本重建");
                return true;
            }
            catch (Exception ex)
            {
                LastError = "旧版配置库无法删除：" + ex.Message;
                DiagLog.Swallow("删除旧版配置库", ex);
                return false;
            }
        }

        private static void Migrate(IntPtr db)
        {
            EnsureColumn(db, "ai_config", "name", "TEXT");
            EnsureColumn(db, "ai_config", "endpoint", "TEXT");
            EnsureColumn(db, "ai_config", "model", "TEXT");
            EnsureColumn(db, "ai_config", "api_key", "TEXT");
            EnsureColumn(db, "ai_config", "updated", "TEXT");
            EnsureColumn(db, "app_setting", "value", "TEXT");
            EnsureColumn(db, "advice", "label", "TEXT");
            EnsureColumn(db, "advice", "updated", "TEXT");
            EnsureColumn(db, "analysis", "model", "TEXT");
            EnsureColumn(db, "analysis", "intro", "TEXT");
            EnsureColumn(db, "analysis", "advice", "TEXT");
            EnsureColumn(db, "analysis", "updated", "TEXT");
            EnsureColumn(db, "analysis", "in_tokens", "INTEGER");
            EnsureColumn(db, "analysis", "out_tokens", "INTEGER");
            EnsureColumn(db, "usage", "model", "TEXT");
            EnsureColumn(db, "usage", "in_tokens", "INTEGER DEFAULT 0");
            EnsureColumn(db, "usage", "out_tokens", "INTEGER DEFAULT 0");
            EnsureColumn(db, "usage", "calls", "INTEGER DEFAULT 0");
            EnsureColumn(db, "usage", "updated", "TEXT");
            Sqlite.Exec(db, "PRAGMA user_version=" + SchemaVersion + ";");
        }

        private static void EnsureColumn(IntPtr db, string table, string column, string type)
        {
            string err;
            List<string[]> rows = Sqlite.Rows(db, "PRAGMA table_info(" + table + ");", out err);
            if (err != null) return;
            foreach (string[] r in rows)
                if (r.Length > 1 && string.Equals(r[1], column, StringComparison.OrdinalIgnoreCase)) return;
            Sqlite.Exec(db, "ALTER TABLE " + table + " ADD COLUMN " + column + " " + type + ";");
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
                    sec.SetAccessRuleProtection(true, false);      // 断开继承并丢弃继承 ACE
                    // 只断继承不够：已有的显式 Everyone/Users 宽松 ACE 会原样保留。
                    // 先清空现存显式规则，再写入三条最小授权
                    AuthorizationRuleCollection rules =
                        sec.GetAccessRules(true, false, typeof(SecurityIdentifier));
                    foreach (FileSystemAccessRule rule in rules)
                    {
                        try { sec.RemoveAccessRuleAll(rule); }
                        catch (Exception ex) { DiagLog.Swallow("清理库 ACL " + rule.IdentityReference, ex); }
                    }
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
                // 由 UI 线程直接调用（隐私告知标记、主题偏好）：短超时，后台批量写库时
                // 宁可这一次写失败，也不要让界面卡满 3 秒
                IntPtr db = Sqlite.Open(FilePath, out err, 500);
                if (db == IntPtr.Zero) { DiagLog.Note("写设置 " + key + " 打开库失败：" + err); return; }
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
        internal const string KindCtx = "ctx";

        internal static string FilePath { get { return AppDb.FilePath; } }

        internal static string KeyStartup(StartupItem it) { return it.Name + "|" + it.Command; }
        internal static string KeyService(ServiceItem it) { return it.ServiceName; }
        internal static string KeyTask(TaskItem it) { return it.FullPath; }
        internal static string KeyCtx(CtxMenuItem it) { return it.RegPath + "|" + it.Id; }

        // 标签合法性统一判定：只有服务页（page==1）存在「手动」这一档操作，
        // 启动项/计划任务/右键菜单页只有「启用 / 禁用」。批量建议解析、逐项分析、
        // 历史库读取三处必须共用这一份口径，否则会写入页面上根本无法执行的建议。
        internal static bool AdviceAllowed(int page, string label)
        {
            if (label == "启用" || label == "禁用") return true;
            return label == "手动" && page == 1;
        }

        internal static int PageOf(IAiAdviceItem it)
        {
            if (it is StartupItem) return 0;
            if (it is ServiceItem) return 1;
            if (it is TaskItem) return 2;
            return 3;
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
            // 历史库里可能残留旧版写入的非法标签（例如启动页的「手动」），
            // 读取时按页过滤，避免页面显示一个根本没有对应操作的建议
            if (key != null && map.TryGetValue(key, out v) && AdviceAllowed(PageOf(it), v))
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

        internal static void FillCtx(List<CtxMenuItem> list)
        {
            if (list == null || list.Count == 0) return;
            bool complete;
            Dictionary<string, string> map = Load(KindCtx, out complete);
            if (!complete) return;
            foreach (CtxMenuItem it in list) Set(it, map, KeyCtx(it));
        }

        // 只覆盖本次有结论的项：补全分析跳过的旧项、以及请求失败但库里仍有旧结论的项，
        // 都保持现状（数据库里也没有被改写），不能在 UI 线程重读库的前提下误清空
        private static void SetKnown(IAiAdviceItem it, Dictionary<string, string> map, string key)
        {
            string v;
            if (key != null && map.TryGetValue(key, out v) && AdviceAllowed(PageOf(it), v))
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

        internal static void ApplyCtx(List<CtxMenuItem> list, Dictionary<string, string> labels)
        {
            if (list == null || labels == null) return;
            foreach (CtxMenuItem it in list) SetKnown(it, labels, KeyCtx(it));
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
        // 「补全分析」前统计缺口会在 UI 线程调用 KeySet；连续点击/反复打开确认框时
        // 没必要每次开库。按文件时间戳 + 2 秒 TTL 缓存，写入侧主动清空
        private static readonly Dictionary<string, HashSet<string>> keySetCache =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        private static long keySetStamp;
        private static DateTime keySetAt = DateTime.MinValue;
        private static readonly object keySetGate = new object();

        internal static HashSet<string> KeySet(string kind, out string error)
        {
            error = null;
            HashSet<string> set = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(kind)) return set;
            long stamp = 0;
            try { stamp = File.GetLastWriteTimeUtc(FilePath).Ticks; } catch { }
            lock (keySetGate)
            {
                HashSet<string> hit;
                if (keySetAt != DateTime.MinValue && keySetStamp == stamp &&
                    (DateTime.UtcNow - keySetAt).TotalSeconds < 2 &&
                    keySetCache.TryGetValue(kind, out hit))
                    return new HashSet<string>(hit, StringComparer.Ordinal);
            }
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
                    lock (keySetGate)
                    {
                        if (keySetStamp != stamp) { keySetCache.Clear(); keySetStamp = stamp; }
                        keySetCache[kind] = new HashSet<string>(set, StringComparer.Ordinal);
                        keySetAt = DateTime.UtcNow;
                    }
                }
                finally { Sqlite.Close(db); }
            }
            catch (Exception ex) { error = ex.Message; }
            return set;
        }

        private static void InvalidateKeySet()
        {
            lock (keySetGate) { keySetCache.Clear(); keySetAt = DateTime.MinValue; }
        }

        // 导入等外部写入后由调用方显式失效「已分析项」缓存
        internal static void InvalidateCache()
        {
            InvalidateKeySet();
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
        internal static string KeyCtx(CtxMenuItem it) { return AiAdviceStore.KeyCtx(it); }

        internal static string KeyFor(int page, object item)
        {
            if (page == 0) return KeyStartup((StartupItem)item);
            if (page == 1) return KeyService((ServiceItem)item);
            if (page == 2) return KeyTask((TaskItem)item);
            return KeyCtx((CtxMenuItem)item);
        }

        // 返回 0~2 条，[0] 最新、[1] 上一次。
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
                InvalidateKeySet();
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
                InvalidateKeySet();
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

        // readOk=false 表示「读取失败」而非「确实没有用量」。调用方据此显示
        // 「统计暂不可用」，避免把一次瞬时读错误报成「累计消耗：暂无记录」
        internal static Dictionary<int, Row> LoadAll(out bool readOk)
        {
            Dictionary<int, Row> cached;
            lock (cacheGate)
            {
                cached = _cache;
                // 返回副本：调用方误改返回值时不会污染全局缓存（行对象只读，浅拷贝足够）
                if (cached != null && (DateTime.Now - _cacheAt).TotalSeconds < 2)
                {
                    readOk = true;                   // 只有读取成功的结果才会进缓存
                    return new Dictionary<int, Row>(cached);
                }
            }
            Dictionary<int, Row> map = new Dictionary<int, Row>();
            bool complete = false;
            readOk = false;
            try
            {
                if (!File.Exists(AiAdviceStore.FilePath))
                {
                    // 缓存与返回值必须是两份：调用方拿到的是自己的副本，误改不会污染缓存
                    lock (cacheGate) { _cache = new Dictionary<int, Row>(map); _cacheAt = DateTime.Now; }
                    readOk = true;                   // 没有数据库 → 确实没有用量
                    return map;
                }
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
            if (complete)
            {
                lock (cacheGate) { _cache = new Dictionary<int, Row>(map); _cacheAt = DateTime.Now; }
                readOk = true;
            }
            return map;
        }

        internal static void Clear(int index)
        {
            lock (cacheGate) { _cache = null; }
            try
            {
                if (!AppDb.Ensure()) return;
                string err;
                // 配置保存时由 UI 线程顺带清理用量：短超时，避免后台写库时界面卡住
                IntPtr db = Sqlite.Open(AiAdviceStore.FilePath, out err, 500);
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
}
