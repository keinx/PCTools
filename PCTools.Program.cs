// 程序入口：单实例、提权、异常兜底与启动流程
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
    // ------------------------------------------------------------------ Entry point
    public static partial class Program
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
                // 子进程会先抢数据目录锁：父进程必须先让出，否则它重试几百毫秒后就
                // 判定「已有实例」直接退出，桌面上一个窗口都不剩
                DataLock.Release();
                // 不能只启动后立刻退出：子进程可能因 XAML 解析失败、被杀软拦截而在
                // 启动早期崩溃，父进程若已退出，桌面上一个窗口都不会剩
                Process child = Process.Start(psi);
                if (child != null)
                {
                    using (child)
                    {
                        try { child.WaitForInputIdle(5000); }
                        catch { }
                        if (child.HasExited && child.ExitCode != 0)
                        {
                            // 提权失败：当前进程继续以普通权限运行，锁必须拿回来
                            string relockErr;
                            DataLock.TryAcquire(out relockErr);
                            Dialogs.Info(Application.Current == null ? null : Application.Current.MainWindow,
                                "提权启动失败",
                                "管理员实例启动后立即退出（退出码 " + child.ExitCode + "）。\n\n" +
                                "可以继续以普通权限使用，或查看 %TEMP%\\PCTools_error.log 了解原因。");
                            return false;
                        }
                    }
                }
                else
                {
                    string relockErr;
                    DataLock.TryAcquire(out relockErr);
                    return false;
                }
                ReleaseSingleInstance();
                Application.Current.Shutdown();
                return true;
            }
            catch (Exception ex)
            {
                // 用户取消 UAC / 启动异常：当前进程继续运行，把数据目录锁拿回来
                string relockErr;
                DataLock.TryAcquire(out relockErr);
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

        // 互斥体尝试结果：Acquired=拿到锁；Occupied=确实已有实例（不应再试下一层）；
        // Unavailable=这一层命名空间建不出来（普通权限账户的典型情况），需回退
        private enum MutexAttempt { Acquired, Occupied, Unavailable }

        // waitMs > 0：用于提权交接，给父进程留出退出时间；0：立即判定
        private static bool AcquireSingleInstance(int waitMs)
        {
            // 先用 Global 命名空间：它能跨会话互斥，防止远程桌面/快速切换下第二个
            // 会话同时写同一份便携版数据库。普通权限账户没有 SeCreateGlobalPrivilege，
            // 创建会抛「拒绝访问」，此时回退到 Local（会话内互斥），不比原状差。
            MutexAttempt g = TryAcquireMutex(@"Global\PCTools_SingleInstance", waitMs);
            if (g == MutexAttempt.Acquired) return true;
            if (g == MutexAttempt.Occupied) return false;    // 已确认全局有实例，不回退

            MutexAttempt l = TryAcquireMutex(@"Local\PCTools_SingleInstance", waitMs);
            if (l != MutexAttempt.Unavailable) return l == MutexAttempt.Acquired;
            // 两层都建不出来：不能成为拒绝启动的理由
            DiagLog.Note("单实例互斥体两层命名空间均不可用，放行启动");
            return true;
        }

        private static MutexAttempt TryAcquireMutex(string name, int waitMs)
        {
            Mutex m = null;
            try
            {
                // 用 WaitOne 而不是构造函数的 initiallyOwned：上个进程崩溃时留下的
                // 是「已放弃」的互斥体，构造方式拿不到所有权，会被误判成「已在运行」
                m = new Mutex(false, name);
                bool got;
                try { got = m.WaitOne(waitMs, false); }
                catch (AbandonedMutexException) { got = true; }
                if (got) { singleInstance = m; return MutexAttempt.Acquired; }
                m.Close();                       // 已有实例持有：这一层已确认
                return MutexAttempt.Occupied;
            }
            catch (Exception ex)
            {
                // 命名互斥体创建失败（权限/命名冲突）：让调用方尝试下一层命名空间
                if (m != null) { try { m.Close(); } catch { } }
                DiagLog.Note("单实例互斥体 " + name + " 不可用：" + ex.Message);
                return MutexAttempt.Unavailable;
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

        // 数据目录级互斥（第二道锁）。Global 互斥体由提权实例创建时，默认 DACL
        // 只授予该账户与管理员：另一个账户打开会抛 UnauthorizedAccessException，
        // 被当成「这一层建不出来」而回退到 Local，于是两个账户同时写同一份便携库。
        // 互斥体权限无法可靠修复，改用数据目录里的锁文件兜底——只有真正共用
        // 同一份 PCToolsAI.sqlite 的实例才会互相拦住
        internal static class DataLock
        {
            private static FileStream held;

            internal static bool TryAcquire(out string error)
            {
                return TryAcquire(out error, 600);
            }

            // waitMs：提权交接时父进程可能刚释放锁，给子进程更长的重试窗口；
            // 普通启动 600ms 足够区分「真有一个实例」与「杀软瞬时占用」
            internal static bool TryAcquire(out string error, int waitMs)
            {
                error = null;
                // 目录本来就不可写：数据库不会落在这里，无需互斥
                if (!AppPaths.Writability) return true;
                int tries = Math.Max(1, waitMs / 150);
                try
                {
                    Directory.CreateDirectory(AppPaths.Dir);
                    string path = Path.Combine(AppPaths.Dir, ".pctools.lock");
                    for (int attempt = 0; attempt < tries; attempt++)
                    {
                        try
                        {
                            held = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                                                  FileShare.None, 64, FileOptions.None);
                            break;
                        }
                        catch (IOException)
                        {
                            if (attempt == tries - 1) throw;
                            Thread.Sleep(150);
                        }
                    }
                    // 写入 PID 便于用户判断「是谁占着」；失败不影响持锁
                    try
                    {
                        byte[] b = Encoding.UTF8.GetBytes(Process.GetCurrentProcess().Id.ToString());
                        held.SetLength(0);
                        held.Write(b, 0, b.Length);
                        held.Flush();
                    }
                    catch { }
                    return true;
                }
                catch (IOException)
                {
                    error = "另一个 PCTools 实例正在使用同一配置目录：\n" + AppPaths.Dir;
                    return false;
                }
                catch (UnauthorizedAccessException)
                {
                    // 无法创建锁文件（目录 ACL 特殊）：退回互斥体判定，不因此拒绝启动
                    return true;
                }
                catch (Exception ex)
                {
                    DiagLog.Swallow("数据目录锁", ex);
                    return true;
                }
            }

            // 提权交接前主动让出：子进程启动后第一件事就是抢这把锁，
            // 父进程若等到 Shutdown 才释放，子进程会误判「已有实例」而直接退出
            internal static void Release()
            {
                try
                {
                    if (held != null) { held.Dispose(); held = null; }
                }
                catch { }
            }
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
            // 自检/诊断模式统一由 PCTools.Diag.cs 分发（build.cmd diag 才编译）
            if (TryRunDiagMode(mode, args)) return;
#endif
            if (mode == "--dbcheck") { RunDbCheck(); Flush("dbcheck"); return; }

            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            InstallExceptionGuards(app);

            // 只支持 Windows 10 1607+：更早的系统没有 winsqlite3.dll，
            // 配置、主题与 AI 结果都会不可用，直接说明原因比带病启动更清楚
            if (!OsInfo.Supported)
            {
                Dialogs.Info(null, "系统版本不受支持",
                    "PCTools 需要 Windows 10（1607 及以上）或 Windows 11。\n\n" +
                    "当前系统：" + OsInfo.Describe() + "\n\n" +
                    "旧系统缺少程序依赖的系统组件（winsqlite3.dll），配置与 AI 功能无法工作。");
                return;
            }

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

            // 第二道互斥：跨账户/跨会话时 Global 互斥体可能根本建不出来（DACL 限制），
            // 但两个实例仍共用同一份便携库。锁文件只在真正共用数据目录时才会冲突
            string lockError;
            if (!DataLock.TryAcquire(out lockError, elevatedHandoff ? 15000 : 600))
            {
                Dialogs.Info(null, "配置目录被占用", lockError +
                    "\n\n如果前一个窗口已关闭，请稍等几秒再试；仍失败时重启一次即可释放锁文件。");
                return;
            }

            // 先建库（首次运行时创建一个只有表结构的空库），再读主题
            if (!AppDb.Ensure())
            {
                Log.AppendLine("启动：配置库不可用 - " + AppDb.LastError);
                AppendErrorLog("启动：配置库不可用 - " + AppDb.LastError);
            }
            CleanupTempDiagnostics();
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

            MainWindow w;
            try
            {
                w = new MainWindow(false, admin);
            }
            catch (Exception ex)
            {
                // 构造发生在消息循环之前，不会进入 DispatcherUnhandledException：
                // 以前任何 x:Name 拼写错误都表现为「双击无反应」，连日志都没有
                AppendErrorLog("主窗口构造失败：" + ex);
                Dialogs.Info(null, "启动失败",
                    "主窗口初始化失败：" + ex.Message +
                    "\n\n这通常是界面资源缺失或控件契约不匹配。\n" +
                    "详细信息已写入：%TEMP%\\PCTools_error.log");
                return;
            }
            TraceStep("main : MainWindow 构造完成");
            app.MainWindow = w;
            w.Closed += delegate { app.Shutdown(); };
            app.Run(w);
            // GUI 模式退出时把内存日志一次性落盘：TraceStep（PCTOOLS_TRACE=1）、
            // UI 异常等平时只进内存，进程退出即丢；命令行模式的 Flush 不受影响
            // 正常启动不落盘：TraceStep 的常规启动行只进内存，PCTOOLS_TRACE=1 时
            // 才在退出时写 %TEMP%，避免把管理员状态长期留在临时目录
            if (TraceEnabled && Log.Length > 0) AppendErrorLog(Log.ToString());
        }

        private static bool showingError;

        // 异常与启动期错误的持久落盘：%TEMP%\PCTools_error.log。
        // Program.Log 只在命令行模式有 Flush，GUI 模式下进程一退出内存内容就丢了，
        // UI 异常/建库失败必须当场写文件，否则排查启动挂死无据可查
        private const long ErrorLogMaxBytes = 1024 * 1024;   // 1 MB 后轮转，避免异常风暴写爆磁盘
        private static readonly object errorLogGate = new object();

        private static void AppendErrorLog(string text)
        {
            string path = Path.Combine(Path.GetTempPath(), "PCTools_error.log");
            lock (errorLogGate)
            {
                try
                {
                    string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + Environment.NewLine;
                    FileInfo fi = new FileInfo(path);
                    if (fi.Exists && fi.Length + Encoding.UTF8.GetByteCount(line) > ErrorLogMaxBytes)
                    {
                        // 保留上一轮作为 .1（覆盖式轮转）：只留两代，且始终有当前可写文件
                        string backup = path + ".1";
                        if (File.Exists(backup)) File.Delete(backup);
                        File.Move(path, backup);
                    }
                    File.AppendAllText(path, line, Encoding.UTF8);
                }
                catch { }
            }
        }

        // %TEMP% 下的诊断残留：dbcheck/trace 含配置元数据与本地路径，不应长期留存。
        // 只清理超过 7 天的文件，当前正在排查的那一份不会被删
        private static void CleanupTempDiagnostics()
        {
            try
            {
                string dir = Path.GetTempPath();
                string[] names = new string[]
                {
                    "PCTools_dbcheck.txt", "PCTools_trace.txt",
                    "PCTools_error.log.1", "PCTools_debug.log"
                };
                foreach (string n in names)
                {
                    try
                    {
                        string p = Path.Combine(dir, n);
                        if (File.Exists(p) && (DateTime.UtcNow - File.GetLastWriteTimeUtc(p)).TotalDays > 7)
                            File.Delete(p);
                    }
                    catch { }
                }
            }
            catch { }
        }

        // 兜底：UI 线程上的未捕获异常弹自绘提示并跳过，而不是让窗口瞬间消失。
        // 只拦「可恢复」异常：内存耗尽、访问冲突一类继续运行只会把损坏状态写进库，
        // 记录后放行让进程退出，避免用户看到「时好时坏」的界面却无从诊断
        private static int uiExceptionCount;

        private static void InstallExceptionGuards(Application app)
        {
            app.DispatcherUnhandledException += delegate(object s, DispatcherUnhandledExceptionEventArgs e)
            {
                int n = ++uiExceptionCount;
                Log.AppendLine("UI 异常 #" + n + "：" + e.Exception);
                string recent = DiagLog.Recent(20);
                AppendErrorLog("UI 异常 #" + n + "：" + e.Exception +
                               (recent.Length == 0 ? "" : "\n最近诊断：\n" + recent));

                bool fatal = e.Exception is OutOfMemoryException ||
                             e.Exception is AccessViolationException ||
                             e.Exception is BadImageFormatException ||
                             e.Exception is InsufficientMemoryException;
                e.Handled = !fatal;
                if (fatal) return;          // 交给 CLR：带错误日志退出，而不是继续半损坏运行

                if (showingError) return;
                showingError = true;
                try
                {
                    Dialogs.Info(Application.Current.MainWindow, "程序遇到异常",
                        e.Exception.Message + "\n\n该操作已被跳过，可以继续使用。" +
                        "\n本次运行已记录 " + n + " 次异常；反复出现请把 %TEMP%\\PCTools_error.log 反馈给开发者。");
                }
                catch { }
                finally { showingError = false; }
            };

            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                Exception ex = e.ExceptionObject as Exception;
                AppendErrorLog("后台线程未处理异常：" + (ex == null ? Convert.ToString(e.ExceptionObject) : ex.ToString()));
            };
        }


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
                string[] tables = new string[] { "ai_config", "app_setting", "advice", "analysis", "usage", "backup_state" };
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

        // ---- 自检/诊断实现结束 ----
    }
}
