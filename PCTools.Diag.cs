using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using System.Xml.Linq;
using Microsoft.Win32;

namespace PCTools
{
    // 诊断/自检实现：只在 build.cmd diag 时编译，发布版不进 exe。
    // 与 PCTools.cs 的 Program 是同一个 partial 类，可直接访问 Log/Emit/Flush 等私有成员。
    public static partial class Program
    {
#if DIAG
        internal static bool TryRunDiagMode(string mode, string[] args)
        {
            if (mode == "--selftest") { RunSelfTest(); Flush("selftest"); return true; }
            if (mode == "--probe") { RunProbe(); Flush("probe"); return true; }
            if (mode == "--smoke") { RunSmoke(); Flush("smoke"); return true; }
            if (mode == "--smokedlg") { RunSmokeDlg(); Flush("smokedlg"); return true; }
            if (mode == "--smokeai") { RunSmokeAi(); Flush("smokeai"); return true; }
            if (mode == "--smokeconfirm") { RunSmokeConfirm(); Flush("smokeconfirm"); return true; }
            if (mode == "--smokeprogress") { RunSmokeProgress(); Flush("smokeprogress"); return true; }
            if (mode == "--smokeunconf") { RunSmokeUnconf(); Flush("smokeunconf"); return true; }
            if (mode == "--smokecache") { RunSmokeCache(); Flush("smokecache"); return true; }
            if (mode == "--analysis") { RunAnalysis(); Flush("analysis"); return true; }
            if (mode == "--analyzeall") { RunAnalyzeAll(); Flush("analyzeall"); return true; }
            if (mode == "--adviceonly") { RunAdviceOnly(); Flush("adviceonly"); return true; }
            if (mode == "--smokeconfig") { RunSmokeConfig(); Flush("smokeconfig"); return true; }
            if (mode == "--smokesettings") { RunSmokeSettings(); Flush("smokesettings"); return true; }
            if (mode == "--shots") { RunShots(); Flush("shots"); return true; }
            if (mode == "--smokemax") { RunSmokeMax(); Flush("smokemax"); return true; }
            if (mode == "--smokebackup") { RunSmokeBackup(); Flush("smokebackup"); return true; }
            if (mode == "--smoketransfer") { RunSmokeTransfer(); Flush("smoketransfer"); return true; }
            if (mode == "--aitest") { RunAiTest(args); return true; }
            if (mode == "--aibatch") { RunAiBatch(args); Flush("aibatch"); return true; }
            if (mode == "--promptstat") { RunPromptStat(args); Flush("promptstat"); return true; }
            if (mode == "--analyzefill") { RunAnalyzeFill(); Flush("analyzefill"); return true; }
            if (mode == "--zoom") { RunZoom(args); return true; }
            if (mode == "--trace") { RunTrace(); Flush("trace"); return true; }
            if (mode == "--diag") { RunDiag(); Flush("diag"); return true; }
            if (mode == "--diag2") { RunDiag2(); Flush("diag"); return true; }
            return false;
        }

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
            string unusedReadErr;
            AiConfig cfg = AiConfig.Load(out unusedReadErr);
            Emit("数据库     : " + AppDb.FilePath);
            int activeIdx;
            string profilesErr;
            List<AiConfig> profs = AiConfig.LoadAll(out activeIdx, out profilesErr);
            Emit("AI 配置套数: " + profs.Count + " / " + AiConfig.MaxProfiles +
                 "；当前启用第 " + activeIdx + " 套（" + cfg.Title + "）");
            Emit("AI 配置    : " + (cfg.Configured ? "已配置" : "未配置") +
                 "；endpoint=" + (string.IsNullOrEmpty(cfg.Endpoint) ? "(空)" : cfg.Endpoint) +
                 "，model=" + (string.IsNullOrEmpty(cfg.Model) ? "(空)" : cfg.Model) +
                 "，key=" + (string.IsNullOrEmpty(cfg.ApiKey) ? "(空)" : cfg.ApiKey.Length + " 字符"));
            Emit("AI 建议库  : " + (File.Exists(AiAdviceStore.FilePath) ? AiAdviceStore.FilePath : "(尚未生成，列为空)"));
            if (File.Exists(AiAdviceStore.FilePath))
            {
                bool okRead;
                Emit("  已存建议: 启动 " + AiAdviceStore.Load(AiAdviceStore.KindStartup, out okRead).Count +
                     " / 服务 " + AiAdviceStore.Load(AiAdviceStore.KindService, out okRead).Count +
                     " / 任务 " + AiAdviceStore.Load(AiAdviceStore.KindTask, out okRead).Count + " 项");
            }

            // 失败项重试依赖 Parse 的逐项缺失原因：用最小样例验证合法/非法/缺省三种分支
            try
            {
                BatchPlan plan = new BatchPlan();
                plan.Sent = 3;
                plan.Keys[1] = "k1";
                plan.Items[1] = "item1";
                plan.Keys[2] = "k2";
                plan.Items[2] = "item2";
                plan.Keys[3] = "k3";
                plan.Items[3] = "item3";
                int discarded;
                int validSent;
                Dictionary<int, string> missing;
                Dictionary<string, string> retryParsed =
                    AiBatch.Parse("1 启用\n2 手动\n", plan, 0, out discarded, out validSent, out missing);
                bool ok = retryParsed.Count == 1 && retryParsed.ContainsKey("k1") &&
                          validSent == 1 && discarded == 1 && missing.Count == 2 &&
                          missing.ContainsKey(2) && missing.ContainsKey(3) &&
                          plan.Items.Count == plan.Sent;
                Emit("重试解析自检 : " + (ok ? "通过（合法/非法/缺省均可定位）"
                                            : "失败（失败项定位不正确）"));
            }
            catch (Exception ex) { Emit("重试解析自检 : 失败 - " + ex.Message); }

            bool prefReadOk;
            bool? saved = ThemeManager.SavedPreference(out prefReadOk);
            Emit("主题偏好   : " + (saved.HasValue ? (saved.Value ? "light" : "dark") : "(未保存，跟随系统)"));
            if (saved.HasValue)
            {
                // 写回同一个值，验证「保存主题不会覆盖掉 AI 配置」。
                // 写入的就是刚读到的同一份值，不改变用户设置，自检可以安全重复执行
                ThemeManager.SavePreference(saved.Value);
                string rereadErr;
                AiConfig again = AiConfig.Load(out rereadErr);
                bool kept = again.Endpoint == cfg.Endpoint && again.Model == cfg.Model && again.ApiKey == cfg.ApiKey;
                Emit("主题写回   : " + (kept ? "通过（AI 配置未被覆盖）" : "失败（AI 配置丢失）"));
            }
        }

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
                        // 真实库 + 真实服务列表：每用户服务（_xxxxx 会话后缀）必须靠模板基名命中基线
                        Emit("服务未备份 : " + w.BackupMissingForTest(1) + " / " + w.Rows(1));
                        w.SelectPageForTest(2);
                        w.UpdateLayout();
                        w.SelectRowForTest(2, 0);
                        Emit("排序(任务) : " + w.SortForTest(2, 2));
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
            RingPixels(w, LogicalTreeHelper.FindLogicalNode(w, name) as FrameworkElement,
                       name, "BtnBorder", false);
        }

        // brushKey 传 null 表示取元素自身的 BorderBrush（列表行选中后是 Accent，写死 BorderColor 会误报）；
        // bgInside 为 true 时底色从元素内部左侧留白采样，避免采到紧邻下一行的边框
        private static void RingPixels(Window w, FrameworkElement b, string label,
                                       string brushKey, bool bgInside)
        {
            try
            {
                if (b == null) { Emit("  环 " + label + " : 找不到元素"); return; }
                if (!b.IsVisible) { Emit("  环 " + label + " : 不可见，跳过"); return; }
                if (b.ActualWidth < 4 || b.ActualHeight < 4) { Emit("  环 " + label + " : 尺寸过小，跳过"); return; }
                b.UpdateLayout();

                DpiScale dpi = VisualTreeHelper.GetDpi(w);
                Point p = b.TransformToAncestor(w).Transform(new Point(0, 0));
                int cx = (int)Math.Round((p.X + b.ActualWidth / 2) * dpi.DpiScaleX);
                int left = (int)Math.Round(p.X * dpi.DpiScaleX);
                int top = (int)Math.Round(p.Y * dpi.DpiScaleY);
                int bot = (int)Math.Round((p.Y + b.ActualHeight) * dpi.DpiScaleY);
                int mid = (top + bot) / 2;

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
                Color bg = bgInside
                    ? Px(buf, stride, Math.Min(ww - 1, left + 6), mid)
                    : Px(buf, stride, cx, Math.Min(hh - 1, bot + 12));
                Color ring;
                if (string.IsNullOrEmpty(brushKey))
                {
                    Border own = b as Border;
                    SolidColorBrush ob = own == null ? null : own.BorderBrush as SolidColorBrush;
                    if (ob == null) { Emit("  环 " + label + " : 元素没有实色 BorderBrush"); return; }
                    ring = ob.Color;
                }
                else
                {
                    SolidColorBrush rb = b.TryFindResource(brushKey) as SolidColorBrush;
                    if (rb == null) { Emit("  环 " + label + " : 取不到 " + brushKey); return; }
                    ring = rb.Color;
                }

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

        // 离线体检：只构造批量建议提示词，不发网络请求，用于观察长度与行格式
        private static void RunPromptStat(string[] args)
        {
            int only = -1;
            if (args.Length > 1) int.TryParse(args[1], out only);
            Emit("PCTools 自检 / 批量建议提示词");
            Emit("数据库     : " + AiAdviceStore.FilePath);
            for (int page = 0; page <= 3; page++)
            {
                if (only >= 0 && page != only) continue;
                List<string> warn = new List<string>();
                List<object> items = new List<object>();
                if (page == 0) { foreach (StartupItem it in DataProvider.GetStartupItems(warn)) items.Add(it); }
                else if (page == 1) { foreach (ServiceItem it in DataProvider.GetServices(warn)) items.Add(it); }
                else if (page == 2) { foreach (TaskItem it in DataProvider.GetTasks(warn)) items.Add(it); }
                else { foreach (CtxMenuItem it in DataProvider.GetContextMenuItems(warn)) items.Add(it); }
                Emit("---- 页面 " + page + " ----");
                if (items.Count == 0) { Emit("项目数量   : 0"); continue; }
                BatchPlan plan = new BatchPlan();
                string prompt = AiBatch.Build(page, items, plan, null);
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
            Emit("页面       : " + (page == 0 ? "启动应用"
                                : (page == 1 ? "服务管理" : (page == 2 ? "计划任务" : "右键菜单"))));
            Emit("数据库     : " + AiAdviceStore.FilePath);

            string unusedReadErr;
            AiConfig cfg = AiConfig.Load(out unusedReadErr);
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
            string prompt = AiBatch.Build(page, items, plan, null);
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

                int selDiscarded, selValidSent;
                Dictionary<int, string> selMissing;
                Dictionary<string, string> values = AiBatch.Parse(reply, plan, page,
                    out selDiscarded, out selValidSent, out selMissing);
                Emit("解析结果   : " + values.Count + " / " + items.Count + " 项");

                string kind = AiAnalysisStore.KindFor(page);
                string err;
                bool ok = AiAdviceStore.Save(kind, values, out err);
                Emit("写入数据库 : " + (ok ? "成功" : "失败 - " + err));

                bool adviceReadOk;
                Dictionary<string, string> back = AiAdviceStore.Load(kind, out adviceReadOk);
                Emit("回读数据库 : " + (adviceReadOk ? back.Count + " 项" : "读取失败"));
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
            string unusedReadErr;
            AiConfig cfg = AiConfig.Load(out unusedReadErr);
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

                        // 第五阶段：不可取消进度窗（导入/导出同款）。worker 由 Loaded 启动，
                        // 取消按钮必须隐藏，Finish 后 ShowDialog 必须正常返回
                        ProgressWindow p5 = new ProgressWindow("导出", 3, false);
                        p5.Loaded += delegate
                        {
                            Thread th5 = new Thread(delegate()
                            {
                                for (int i = 1; i <= 3; i++)
                                {
                                    Thread.Sleep(60);
                                    int k = i;
                                    p5.Dispatcher.BeginInvoke((Action)delegate { p5.Report(k, 3, "数据集 " + k); });
                                }
                                p5.Dispatcher.BeginInvoke((Action)delegate { p5.Finish(); });
                            });
                            th5.IsBackground = true;
                            th5.Start();
                        };
                        p5.ShowDialog();
                        Emit("不可取消   : ShowDialog 正常返回，取消按钮可见=" + p5.CancelVisibleForTest +
                             "，IsVisible=" + p5.IsVisible);

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

                string unusedReadErr;
                AiConfig cfg = AiConfig.Load(out unusedReadErr);
                Emit("数据库     : " + AppDb.FilePath);
                Emit("接口地址   : " + (string.IsNullOrEmpty(cfg.Endpoint) ? "(空)" : cfg.Endpoint));
                Emit("模型名称   : " + (string.IsNullOrEmpty(cfg.Model) ? "(空)" : cfg.Model));
                Emit("API Key    : " + (string.IsNullOrEmpty(cfg.ApiKey) ? "(未设置)" : "已设置（" + cfg.ApiKey.Length + " 字符）"));

                AiSettingsWindow s = new AiSettingsWindow(cfg);
                Window r = new Window();
                r.Title = "AI 分析";
                r.Width = 740;
                r.SizeToContent = SizeToContent.Height;
                r.WindowStartupLocation = WindowStartupLocation.CenterOwner;
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

                string cacheReadErr;
                List<AiAnalysisStore.Entry> list = AiAnalysisStore.Load(AiAnalysisStore.KindStartup, key, out cacheReadErr);
                Emit("缓存条数   : " + list.Count + "（应为 2，第 3 次写入会挤掉最旧的）");
                for (int i = 0; i < list.Count; i++)
                    Emit("  第" + (i + 1) + "份   : " + list[i].Model + " / 输入 " + list[i].InTokens +
                         " / 输出 " + list[i].OutTokens + " / " + list[i].Updated);

                string unusedReadErr;
                AiConfig cfg = AiConfig.Load(out unusedReadErr);
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

                string unusedReadErr;
                AiConfig cfg = AiConfig.Load(out unusedReadErr);
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
                string baselineErr;
                int baseline = AiAnalysisStore.Load(AiAnalysisStore.KindService, key, out baselineErr).Count;
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
                    string nowErr;
                    int now = AiAnalysisStore.Load(AiAnalysisStore.KindService, key, out nowErr).Count;
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
        // 备份基线自检：整页备份 → 基线回填 → 不一致筛选 → .pct 往返
        private static void RunSmokeBackup()
        {
            Emit("PCTools 自检 / 备份基线");
            // 备份链路写的是真实库：自检必须重定向到临时库，不能给用户留下测试基线
            string temp = Path.Combine(Path.GetTempPath(),
                "PCTools_smokebackup_" + Process.GetCurrentProcess().Id + ".sqlite");
            try
            {
                AppDb.UseTempForTest(temp);
                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                ThemeManager.Apply(app, ThemeManager.IsLight());

                MainWindow w = new MainWindow(true, IsAdmin());
                w.Show();
                w.RefreshAllSync();
                w.SelectPageForTest(0);
                int all = w.Rows(0);
                Emit("启动项数量 : " + all);

                string err = w.BackupForTest(0);
                Emit("备份写入   : " + (err == null ? "成功" : "失败 - " + err));
                Emit("基线行数   : " + TransferStore.Count(0, 0));
                Emit("首行备份值 : " + w.BackupTextForTest(0, 0));

                Emit("只显示不一致: " + w.SetBackupOnlyForTest(0, true) + " / " + all);
                w.UpdateLayout();
                Capture(w, "PCTools_preview_backup_filter.png");
                w.SetBackupOnlyForTest(0, false);
                w.UpdateLayout();
                Capture(w, "PCTools_preview_backup.png");

                w.SelectPageForTest(1);
                string errS = w.BackupForTest(1);
                Emit("服务备份   : " + (errS == null ? "成功" : "失败 - " + errS));
                Emit("服务首行值 : " + w.BackupTextForTest(1, 0));
                Emit("服务未备份 : " + w.BackupMissingForTest(1) + " / " + w.Rows(1));

                // 每用户服务跨会话兜底：注入解析器，不依赖本机注册表
                List<ServiceItem> probe = new List<ServiceItem>();
                ServiceItem p1 = new ServiceItem();
                p1.ServiceName = "AarSvc_55e45";
                p1.StartType = "手动";
                probe.Add(p1);
                ServiceItem p2 = new ServiceItem();
                p2.ServiceName = "Vendor_9f9f9f";
                p2.StartType = "手动";
                probe.Add(p2);
                Dictionary<string, string> fake = new Dictionary<string, string>(StringComparer.Ordinal);
                fake["AarSvc_61045"] = "手动";
                fake["Vendor_1a2b3c"] = "自动";
                BackupStore.ApplyMapForTest(1, probe, fake, delegate(string s)
                {
                    return s != null && s.StartsWith("AarSvc_", StringComparison.OrdinalIgnoreCase)
                        ? "AarSvc" : null;
                });
                Emit("兜底用例   : 「" + p1.BackupText + "」/「" + p2.BackupText + "」  " +
                     (p1.BackupText == "手动" && p2.BackupText == "未备份" ? "PASS" : "FAIL"));
                w.UpdateLayout();
                Capture(w, "PCTools_preview_backup_service.png");

                w.SelectPageForTest(2);
                string errT = w.BackupForTest(2);
                Emit("任务备份   : " + (errT == null ? "成功" : "失败 - " + errT));
                string taskVals = "";
                for (int i = 0; i < Math.Min(6, w.Rows(2)); i++)
                    taskVals += w.BackupTextForTest(2, i) + " ";
                Emit("任务备份值 : " + taskVals.Trim());
                w.UpdateLayout();
                Capture(w, "PCTools_preview_backup_task.png");
                w.SelectPageForTest(0);

                string path = Path.Combine(Path.GetTempPath(), "PCTools_smokebackup.pct");
                PctPackage pkg;
                string ex;
                bool ok = TransferStore.Build(0, 0, null, out pkg, out ex) && pkg.Write(path, out ex);
                Emit("导出 .pct  : " + (ok ? pkg.Rows.Count + " 条" : "失败 - " + ex));

                PctPackage back;
                string re;
                bool rd = PctPackage.Read(path, out back, out re);
                Emit("回读 .pct  : " + (rd ? back.Rows.Count + " 条 / " + TransferStore.ProjectName(back.Project) : "失败 - " + re));
                if (rd)
                {
                    int wr, sk;
                    string ie;
                    bool im = TransferStore.Import(0, 0, back, out wr, out sk, out ie);
                    Emit("导入       : " + (im ? "写入 " + wr + " / 跳过 " + sk : "失败 - " + ie));
                }
                try { File.Delete(path); } catch { }

                w.Close();
                app.Shutdown();
                Emit("消息循环   : 正常退出");
            }
            catch (Exception ex)
            {
                Emit("失败       : " + ex.GetType().Name + " / " + ex.Message);
            }
            finally
            {
                AppDb.RestoreForTest();
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }

        // 导入导出自检：临时库上跑备份/建议/分析三种负载，避免污染真实数据
        private static void RunSmokeTransfer()
        {
            Emit("PCTools 自检 / 导入导出");
            string temp = Path.Combine(Path.GetTempPath(),
                "PCTools_smoketransfer_" + Process.GetCurrentProcess().Id + ".sqlite");
            try
            {
                AppDb.UseTempForTest(temp);

                // 备份
                PctPackage b = new PctPackage();
                b.Project = 1; b.Type = 0;
                PctRow r1 = new PctRow(); r1.Key = "svcA"; r1.Name = "服务 A"; r1.Value = "自动"; b.Rows.Add(r1);
                PctRow r2 = new PctRow(); r2.Key = "svcB"; r2.Name = "服务 B"; r2.Value = "手动"; b.Rows.Add(r2);
                int wr, sk; string err;
                bool ok = TransferStore.Import(1, 0, b, out wr, out sk, out err);
                Emit("备份导入   : " + (ok ? "写入 " + wr : "失败 - " + err) + " / 库中 " + TransferStore.Count(1, 0));

                // 建议
                PctPackage a = new PctPackage();
                a.Project = 1; a.Type = 1;
                PctRow a1 = new PctRow(); a1.Key = "svcA"; a1.Label = "启用"; a.Rows.Add(a1);
                PctRow a2 = new PctRow(); a2.Key = "svcB"; a2.Label = "手动"; a.Rows.Add(a2);
                ok = TransferStore.Import(1, 1, a, out wr, out sk, out err);
                Emit("建议导入   : " + (ok ? "写入 " + wr : "失败 - " + err) + " / 库中 " + TransferStore.Count(1, 1));

                // 分析（同一 key 两条 + 另一 key 一条）
                PctPackage n = new PctPackage();
                n.Project = 1; n.Type = 2;
                PctRow n1 = new PctRow(); n1.Key = "svcA"; n1.Seq = 1; n1.Intro = "介绍一"; n1.Advice = "启用"; n.Rows.Add(n1);
                PctRow n2 = new PctRow(); n2.Key = "svcA"; n2.Seq = 2; n2.Intro = "介绍二"; n2.Advice = "禁用"; n.Rows.Add(n2);
                PctRow n3 = new PctRow(); n3.Key = "svcB"; n3.Seq = 1; n3.Intro = "介绍三"; n3.Advice = "手动"; n.Rows.Add(n3);
                ok = TransferStore.Import(1, 2, n, out wr, out sk, out err);
                Emit("分析导入   : " + (ok ? "写入 " + wr : "失败 - " + err) + " / 库中 " + TransferStore.Count(1, 2));

                // 文件往返
                string path = Path.Combine(Path.GetTempPath(), "PCTools_smoketransfer.pct");
                bool wok = n.Write(path, out err);
                PctPackage back; string re;
                bool rok = PctPackage.Read(path, out back, out re);
                Emit("XML 往返   : " + (wok && rok ? back.Rows.Count + " 条 / " + TransferStore.ProjectName(back.Project) : "失败 - " + (wok ? re : err)));
                Emit("单集版本   : v" + back.Version + " / 数据集 " + back.Sets.Count);
                try { File.Delete(path); } catch { }

                // 多数据集 v2 往返：三集各自保留项目/类型/行数
                PctPackage multi = new PctPackage();
                PctSet ms1 = new PctSet(); ms1.Project = 2; ms1.Type = 0;
                PctRow mr1 = new PctRow(); mr1.Key = "taskA"; mr1.Name = "任务 A"; mr1.Value = "已启用"; ms1.Rows.Add(mr1);
                PctSet ms2 = new PctSet(); ms2.Project = 1; ms2.Type = 1;
                PctRow mr2 = new PctRow(); mr2.Key = "svcA"; mr2.Label = "禁用"; ms2.Rows.Add(mr2);
                PctSet ms3 = new PctSet(); ms3.Project = 0; ms3.Type = 2;
                PctRow mr3 = new PctRow(); mr3.Key = "startA"; mr3.Seq = 1; mr3.Intro = "介绍"; mr3.Advice = "保留"; ms3.Rows.Add(mr3);
                multi.Sets.Add(ms1); multi.Sets.Add(ms2); multi.Sets.Add(ms3);
                string mp = Path.Combine(Path.GetTempPath(), "PCTools_smoketransfer_multi.pct");
                bool mw = multi.Write(mp, out err);
                PctPackage mback; string mre;
                bool mrok = PctPackage.Read(mp, out mback, out mre);
                string mver = "";
                try { mver = XDocument.Load(mp).Root.Attribute("version").Value; } catch { }
                Emit("多集往返   : " + (mw && mrok ? "v" + mver + " / " + mback.Sets.Count + " 集" : "失败 - " + (mw ? mre : err)));
                try { File.Delete(mp); } catch { }

                // 重复数据集必须整包拒绝
                string dupPath = Path.Combine(Path.GetTempPath(), "PCTools_smoketransfer_dup.pct");
                File.WriteAllText(dupPath, "<?xml version=\"1.0\"?><pctools version=\"2\" app=\"PCTools\"><sets>" +
                    "<set project=\"service\" type=\"backup\"><items/></set>" +
                    "<set project=\"service\" type=\"backup\"><items/></set></sets></pctools>");
                PctPackage dupPkg; string dupErr;
                Emit("重复数据集 : " + (PctPackage.Read(dupPath, out dupPkg, out dupErr) ? "错误地接受了" : "已拒绝"));
                try { File.Delete(dupPath); } catch { }

                // 更高版本必须被拒绝
                string v3Path = Path.Combine(Path.GetTempPath(), "PCTools_smoketransfer_v3.pct");
                File.WriteAllText(v3Path, "<?xml version=\"1.0\"?><pctools version=\"3\" app=\"PCTools\"><sets>" +
                    "<set project=\"startup\" type=\"backup\"><items/></set></sets></pctools>");
                PctPackage v3Pkg; string v3Err;
                Emit("版本 3     : " + (PctPackage.Read(v3Path, out v3Pkg, out v3Err) ? "错误地接受了" : "已拒绝"));
                try { File.Delete(v3Path); } catch { }

                // 单集 20001 行必须被拒绝
                StringBuilder big = new StringBuilder();
                big.Append("<?xml version=\"1.0\"?><pctools version=\"2\" app=\"PCTools\"><sets>");
                big.Append("<set project=\"startup\" type=\"backup\"><items>");
                for (int i = 0; i <= 20000; i++)
                    big.Append("<item key=\"k").Append(i).Append("\" value=\"v\"/>");
                big.Append("</items></set></sets></pctools>");
                string bigPath = Path.Combine(Path.GetTempPath(), "PCTools_smoketransfer_big.pct");
                File.WriteAllText(bigPath, big.ToString());
                PctPackage bigPkg; string bigErr;
                Emit("行数超限   : " + (PctPackage.Read(bigPath, out bigPkg, out bigErr) ? "错误地接受了" : "已拒绝"));
                try { File.Delete(bigPath); } catch { }

                // 多集导入：0 行集必须被拒绝，非空集分别落库
                List<PctSet> batch = new List<PctSet>();
                PctSet bs1 = new PctSet(); bs1.Project = 2; bs1.Type = 0;
                PctRow br1 = new PctRow(); br1.Key = "taskA"; br1.Value = "已启用"; bs1.Rows.Add(br1);
                PctRow br2 = new PctRow(); br2.Key = "taskB"; br2.Value = "已禁用"; bs1.Rows.Add(br2);
                PctRow badRow = new PctRow(); badRow.Key = "taskC"; badRow.Name = "缺值条目"; badRow.Value = "";
                bs1.Rows.Add(badRow);   // 验证跳过明细能给出原因
                PctSet bs2 = new PctSet(); bs2.Project = 1; bs2.Type = 1;
                PctRow br3 = new PctRow(); br3.Key = "svcA"; br3.Label = "启用"; bs2.Rows.Add(br3);
                PctRow br4 = new PctRow(); br4.Key = "svcB"; br4.Label = "手动"; bs2.Rows.Add(br4);
                PctSet bs3 = new PctSet(); bs3.Project = 0; bs3.Type = 0;   // 空集
                batch.Add(bs1); batch.Add(bs2); batch.Add(bs3);
                List<SetImportResult> results;
                int progressCalls = 0;
                TransferStore.ImportSets(batch,
                    delegate(int done, int all, string label) { progressCalls++; },
                    out results);
                int okSets = 0, emptySets = 0;
                foreach (SetImportResult rr in results)
                {
                    if (rr.Ok) okSets++;
                    else if (rr.Set != null && rr.Set.Rows.Count == 0) emptySets++;
                }
                Emit("多集导入   : " + results.Count + " 集 / 成功 " + okSets + " / 空集拒绝 " + emptySets +
                     " / 任务备份 " + TransferStore.Count(2, 0) + " / 服务建议 " + TransferStore.Count(1, 1));
                string note = results.Count > 0 && results[0].Notes.Count > 0 ? results[0].Notes[0] : "无";
                Emit("进度回调   : " + progressCalls + " / " + batch.Count + " 集");
                Emit("跳过明细   : " + (results.Count > 0 ? results[0].Notes.Count : 0) + " 条 / " + note +
                     " / 跳过 " + (results.Count > 0 ? results[0].Skipped : 0));

                // 非 .pct 内容应被拒绝
                string bad = Path.Combine(Path.GetTempPath(), "PCTools_bad.pct");
                File.WriteAllText(bad, "<notpct/>");
                PctPackage junk; string je;
                Emit("非法文件   : " + (PctPackage.Read(bad, out junk, out je) ? "错误地接受了" : "已拒绝"));
                try { File.Delete(bad); } catch { }

                Emit("临时库     : " + temp + "（已清理）");
            }
            catch (Exception ex)
            {
                Emit("失败       : " + ex.GetType().Name + " / " + ex.Message);
            }
            finally
            {
                AppDb.RestoreForTest();
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }

        // 设置页自检：确认「配置 AI」「导入导出」能在主窗口内容区正常渲染
        private static void RunSmokeSettings()
        {
            Emit("PCTools 自检 / 设置页");
            string pct = BuildSettingsPct();
            try
            {
                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                ThemeManager.Apply(app, ThemeManager.IsLight());

                MainWindow w = new MainWindow(true, IsAdmin());
                w.Show();
                w.UpdateLayout();
                w.OpenSettingsForTest(1);

                DispatcherTimer timer = new DispatcherTimer();
                timer.Interval = TimeSpan.FromMilliseconds(500);
                int step = 0;
                timer.Tick += delegate
                {
                    step++;
                    w.UpdateLayout();
                    if (step == 1)
                    {
                        Capture(w, "PCTools_preview_ai_page.png");
                        Emit("配置 AI 页 : 截图已导出");
                        w.OpenSettingsForTest(2);
                    }
                    else if (step == 2)
                    {
                        Capture(w, "PCTools_preview_transfer.png");
                        Emit("导入导出页 : 截图已导出");
                        if (w.TransferViewForTest != null)
                        {
                            // 卡片边框画在 TSelect 模板的 bd 上，取 CheckBox 自身会带入 8 DIP 外边距
                            RingPixels(w, w.TransferViewForTest.PrjCardForTest(1), "PrjService", null, true);
                        }
                        if (w.TransferViewForTest != null) w.TransferViewForTest.SetModeForTest(true);
                    }
                    else if (step == 3)
                    {
                        // 未选文件的空态：确认底部不再出现第二个「选择 .pct 文件」
                        Capture(w, "PCTools_preview_transfer_empty.png");
                        Emit("导入空态   : " + (w.TransferViewForTest == null ? "无视图" : w.TransferViewForTest.StateForTest()));
                        if (pct != null && w.TransferViewForTest != null)
                            w.TransferViewForTest.LoadFileForTest(pct);
                        else Emit("导入态     : 未生成测试文件");
                    }
                    else if (step == 4)
                    {
                        Capture(w, "PCTools_preview_transfer_import.png");
                        Emit("导入态     : " + (w.TransferViewForTest == null ? "无视图" : w.TransferViewForTest.StateForTest()));
                        if (w.TransferViewForTest != null)
                        {
                            Emit("列表       : " + w.TransferViewForTest.LayoutForTest());
                            RingPixels(w, w.TransferViewForTest.FirstRowForTest(), "SetRow0", null, true);
                            Emit("滚动断言   : " + (w.TransferViewForTest.ScrollForTest(40) ? "PASS" : "FAIL"));
                        }
                        w.Width = 960;
                        w.Height = 600;
                    }
                    else if (step == 5)
                    {
                        // 先拍导入窄窗（此前这一步拍的是导出页，导入态窄窗完全没有覆盖）
                        w.UpdateLayout();
                        Capture(w, "PCTools_preview_transfer_import_narrow.png");
                        Emit("窄窗口     : 960x600 导入截图已导出");
                        if (w.TransferViewForTest != null) w.TransferViewForTest.SetModeForTest(false);
                    }
                    else if (step == 6)
                    {
                        w.UpdateLayout();
                        Capture(w, "PCTools_preview_transfer_narrow.png");
                        Emit("窄窗口     : 960x600 导出截图已导出");
                        w.OpenSettingsForTest(1);
                    }
                    else if (step == 7)
                    {
                        w.UpdateLayout();
                        Capture(w, "PCTools_preview_ai_narrow.png");
                        Emit("窄窗口     : 960x600 配置 AI 截图已导出");
                        w.SelectPageForTest(0);
                    }
                    else if (step == 8)
                    {
                        w.UpdateLayout();
                        Emit("返回列表   : " + w.SettingsStateForTest());
                        Emit("备份筛选可用 : " + w.BackupOnlyEnabledForTest);
                        Capture(w, "PCTools_preview_settings_back.png");
                        timer.Stop();
                        w.Close();
                        app.Shutdown();
                    }
                };
                timer.Start();
                app.Run();
                Emit("消息循环   : 正常退出");
            }
            catch (Exception ex)
            {
                Emit("失败       : " + ex.GetType().Name + " / " + ex.Message);
            }
            finally
            {
                if (pct != null) { try { File.Delete(pct); } catch { } }
            }
        }

        // README / docs 截图：切到临时库并种一套演示数据（三套 AI 配置 + 四页基线），
        // 不触碰用户真实的 PCToolsAI.sqlite；窗口尺寸与正常启动一致（1180x760）
        private static void RunShots()
        {
            Emit("PCTools 截图 / README");
            string db = Path.Combine(Path.GetTempPath(), "PCTools_shots.sqlite");
            string pct = null;
            try
            {
                try { if (File.Exists(db)) File.Delete(db); } catch { }
                AppDb.UseTempForTest(db);
                SeedShotData();

                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                ThemeManager.Apply(app, true);

                MainWindow w = new MainWindow(true, IsAdmin());
                w.Show();
                w.RefreshAll();

                DispatcherTimer timer = new DispatcherTimer();
                timer.Interval = TimeSpan.FromMilliseconds(400);
                int step = 0;
                int waits = 0;
                timer.Tick += delegate
                {
                    // 先等异步枚举完成：否则会拍到空列表或「正在加载…」状态栏
                    if (w.IsLoading) return;
                    // 数据集条数在后台线程统计，没算完就截会拍到「正在统计各数据集的条数…」
                    if (step >= 6 && step <= 7 && w.TransferViewForTest != null &&
                        !w.TransferViewForTest.CountsReadyForTest && waits++ < 40) return;

                    w.UpdateLayout();
                    step++;
                    if (step == 1)
                    {
                        // 四页各写一次基线：备份列、导出数据集与「未备份」统计才有真实内容
                        for (int p = 0; p < 4; p++)
                        {
                            string err = w.BackupForTest(p);
                            if (err != null) Emit("基线       : 第 " + p + " 页失败 - " + err);
                        }
                        w.SelectRowForTest(0, 0);
                        w.UpdateLayout();
                        Capture(w, "PCTools_shot_01_startup.png");
                        Emit("01 启动应用 : 已导出");
                        w.SelectPageForTest(1);
                    }
                    else if (step == 2)
                    {
                        Capture(w, "PCTools_shot_02_services.png");
                        Emit("02 服务管理 : 已导出，未备份 " + w.BackupMissingForTest(1) + " / " + w.Rows(1));
                        w.SelectPageForTest(2);
                    }
                    else if (step == 3)
                    {
                        w.SelectRowForTest(2, 0);
                        w.UpdateLayout();
                        Capture(w, "PCTools_shot_03_tasks.png");
                        Emit("03 计划任务 : 已导出");
                        w.SelectPageForTest(3);
                    }
                    else if (step == 4)
                    {
                        w.SelectRowForTest(3, 0);
                        w.UpdateLayout();
                        Capture(w, "PCTools_shot_04_contextmenu.png");
                        Emit("04 右键菜单 : 已导出");
                        w.OpenSettingsForTest(1);
                    }
                    else if (step == 5)
                    {
                        Capture(w, "PCTools_shot_05_aiconfig.png");
                        Emit("05 配置 AI  : 已导出");
                        w.OpenSettingsForTest(2);
                    }
                    else if (step == 6)
                    {
                        if (w.TransferViewForTest != null) w.TransferViewForTest.SelectAllProjectsForTest();
                    }
                    else if (step == 7)
                    {
                        Capture(w, "PCTools_shot_06_transfer.png");
                        Emit("06 导入导出 : 已导出（导出态）");
                        pct = BuildShotsPct();
                        if (pct != null && w.TransferViewForTest != null)
                        {
                            w.TransferViewForTest.SetModeForTest(true);
                            w.TransferViewForTest.LoadFileForTest(pct);
                        }
                        else Emit("07 导入     : 测试包生成失败");
                    }
                    else if (step == 8)
                    {
                        Capture(w, "PCTools_shot_07_import.png");
                        Emit("07 导入     : " + (w.TransferViewForTest == null ? "无视图" : w.TransferViewForTest.StateForTest()));
                        w.SelectPageForTest(0);
                        w.SetThemeForTest(false);
                    }
                    else if (step == 9)
                    {
                        w.UpdateLayout();
                        Capture(w, "PCTools_shot_08_dark.png");
                        Emit("08 深色主题 : 已导出");
                        timer.Stop();
                        w.Close();
                        app.Shutdown();
                    }
                };
                timer.Start();
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
                if (pct != null) { try { File.Delete(pct); } catch { } }
                try { if (File.Exists(db)) File.Delete(db); } catch { }
            }
        }

        // 演示用配置：不写 Key，接口地址全部用保留域名 / 本机回环，不含任何真实凭据
        private static void SeedShotData()
        {
            try
            {
                if (!AppDb.Ensure()) { Emit("演示数据   : 建库失败 - " + AppDb.LastError); return; }

                List<AiConfig> list = new List<AiConfig>();
                AiConfig a = new AiConfig();
                a.Index = 1; a.Name = "OpenAI 官方";
                a.Endpoint = "https://api.openai.com/v1/chat/completions";
                a.Model = "gpt-4o-mini";
                AiConfig b = new AiConfig();
                b.Index = 2; b.Name = "Azure 部署";
                b.Endpoint = "https://example.openai.azure.com/openai/deployments/gpt-4o/chat/completions?api-version=2024-10-21";
                b.Model = "gpt-4o";
                AiConfig c = new AiConfig();
                c.Index = 3; c.Name = "本地 Ollama";
                c.Endpoint = "http://127.0.0.1:11434/v1/chat/completions";
                c.Model = "qwen2.5:7b";
                list.Add(a); list.Add(b); list.Add(c);

                bool ok = AiConfig.SaveAll(list, 1);
                // 累计用量：只给两套配置造数，第三套显示「暂无记录」也是真实状态
                AiUsageStore.Add(1, "gpt-4o-mini", 128430, 32180, 42);
                AiUsageStore.Add(3, "qwen2.5:7b", 12140, 4860, 6);
                Emit("演示数据   : 配置 " + list.Count + " 套，" + (ok ? "写入成功" : "写入失败"));
            }
            catch (Exception ex) { Emit("演示数据   : 跳过 - " + ex.Message); }
        }

        // 用当前临时库里的四页基线拼一个 .pct，导入截图里就是真实数据集与条数
        private static string BuildShotsPct()
        {
            try
            {
                List<DatasetRef> targets = new List<DatasetRef>();
                for (int p = 0; p < 4; p++) targets.Add(new DatasetRef(p, 0));
                PctPackage pkg;
                List<string> empty;
                string error;
                if (!TransferStore.BuildSets(targets, null, out pkg, out empty, out error))
                {
                    Emit("测试包     : " + error);
                    return null;
                }
                string path = Path.Combine(Path.GetTempPath(), "PCTools_备份基线_20261123.pct");
                return pkg.Write(path, out error) ? path : null;
            }
            catch (Exception ex) { Emit("测试包     : " + ex.Message); return null; }
        }

        // 构造一个三分集的 v2 包，仅用于导入态截图
        // 最大化：窗口矩形必须等于当前显示器工作区，标题栏元素垂直居中
        private static void RunSmokeMax()
        {
            Emit("PCTools 自检 / 最大化");
            Application app = new Application();
            try
            {
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                ThemeManager.Apply(app, ThemeManager.IsLight());

                MainWindow w = new MainWindow(true, IsAdmin());
                w.Show();
                w.UpdateLayout();
                Emit("还原态     : " + w.Width.ToString("0") + "x" + w.Height.ToString("0"));

                DispatcherTimer timer = new DispatcherTimer();
                timer.Interval = TimeSpan.FromMilliseconds(500);
                int step = 0;
                timer.Tick += delegate
                {
                    step++;
                    w.UpdateLayout();
                    if (step == 1)
                    {
                        w.WindowState = WindowState.Maximized;
                    }
                    else if (step == 2)
                    {
                        Emit(w.MaxSizeForTest());
                        Emit(w.SearchBoxForTest());
                        Capture(w, "PCTools_preview_max.png");
                        w.WindowState = WindowState.Normal;
                    }
                    else if (step == 3)
                    {
                        Emit("还原后     : " + w.Width.ToString("0") + "x" + w.Height.ToString("0") +
                             " state=" + w.WindowState);
                        timer.Stop();
                        w.Close();
                        app.Shutdown();
                    }
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

        private static string BuildSettingsPct()
        {
            try
            {
                PctPackage p = new PctPackage();
                // 10 个数据集：导入列表必须超过一屏，否则 ScrollableHeight 恒为 0，滚动断言会假通过
                for (int i = 0; i < 10; i++)
                {
                    PctSet s = new PctSet();
                    s.Project = i % 4;
                    s.Type = i % 3;
                    if (s.Type == 0)
                    {
                        s.Rows.Add(BackupPctRow("Svc" + i, "示例服务 " + (i + 1), i % 2 == 0 ? "自动" : "手动"));
                        s.Rows.Add(BackupPctRow("SvcB" + i, "示例服务 B" + (i + 1), "已禁用"));
                    }
                    else if (s.Type == 1)
                    {
                        s.Rows.Add(AdvicePctRow("\\Cli" + i, "示例项目 " + (i + 1), "启用"));
                    }
                    else
                    {
                        s.Rows.Add(AnalysisPctRow("M|Run|App" + i, "示例项 " + (i + 1), 1, "示例说明", "示例建议"));
                    }
                    p.Sets.Add(s);
                }
                string path = Path.Combine(Path.GetTempPath(), "PCTools_settings_import.pct");
                string err;
                return p.Write(path, out err) ? path : null;
            }
            catch { return null; }
        }

        private static PctRow BackupPctRow(string key, string name, string value)
        {
            PctRow r = new PctRow();
            r.Key = key; r.Name = name; r.Value = value;
            r.Updated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            return r;
        }

        private static PctRow AdvicePctRow(string key, string name, string label)
        {
            PctRow r = new PctRow();
            r.Key = key; r.Name = name; r.Label = label;
            r.Updated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            return r;
        }

        private static PctRow AnalysisPctRow(string key, string name, int seq, string intro, string advice)
        {
            PctRow r = new PctRow();
            r.Key = key; r.Name = name; r.Seq = seq; r.Model = "test-model";
            r.Intro = intro; r.Advice = advice; r.InTokens = 10; r.OutTokens = 20;
            r.Updated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            return r;
        }

        private static void RunSmokeConfig()
        {
            Emit("PCTools 自检 / 保存单个配置");
            // 与 --smokecache 一致：保存按钮会写真实库（刷新 ai_config 的 updated），
            // 自检必须重定向到临时库，不能污染用户数据
            string tempDb = Path.Combine(Path.GetTempPath(),
                "PCTools_smokeconfig_" + Process.GetCurrentProcess().Id + ".sqlite");
            try
            {
                AppDb.UseTempForTest(tempDb);
                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                ThemeManager.Apply(app, ThemeManager.IsLight());

                string unusedReadErr;
                AiConfig cfg = AiConfig.Load(out unusedReadErr);
                AiUsageStore.Row before = null;
                bool usageOk0;
                Dictionary<int, AiUsageStore.Row> map = AiUsageStore.LoadAll(out usageOk0);
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
                            bool usageOk1;
                            Dictionary<int, AiUsageStore.Row> m2 = AiUsageStore.LoadAll(out usageOk1);
                            if (m2.ContainsKey(cfg.Index)) after = m2[cfg.Index];
                            Emit("保存后用量 : " + (after == null ? "无记录"
                                 : "输入 " + after.InTokens + " / 输出 " + after.OutTokens));
                        }
                        // 行内「激活 / 删除」：验证 Id→位置换算与清空表单，走真实的冒泡点击
                        AiSettingsView v = w.ViewForTest;
                        if (v != null)
                        {
                            v.SkipConfirmForTest();
                            v.AddForTest();
                            v.AddForTest();
                            w.UpdateLayout();
                            bool clickA = v.ClickRowForTest(1, "activate");
                            bool clickD = v.ClickRowForTest(0, "delete");
                            v.ClearForTest();
                            bool ok = clickA && clickD && v.CountForTest == 2 && v.ActiveForTest == 2 &&
                                      v.EditingNameForTest == "配置 2" && v.FieldNameForTest == "";
                            Emit("行内操作   : 激活" + (clickA ? "OK" : "失败") +
                                 " / 删除" + (clickD ? "OK" : "失败") +
                                 " / 剩余 " + v.CountForTest + " 套 / active=" + v.ActiveForTest +
                                 " / 编辑「" + v.EditingNameForTest + "」/ 清空后名称「" +
                                 v.FieldNameForTest + "」  " + (ok ? "PASS" : "FAIL"));
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

                // 上限行为：10 套可写可读，第 11 套必须被拒绝
                List<AiConfig> ten = new List<AiConfig>();
                for (int i = 1; i <= AiConfig.MaxProfiles; i++)
                {
                    AiConfig one = new AiConfig();
                    one.Index = i;
                    one.Name = "上限测试 " + i;
                    ten.Add(one);
                }
                int active10;
                string e10;
                bool saved10 = AiConfig.SaveAll(ten, 1);
                int readCount = AiConfig.LoadAll(out active10, out e10).Count;
                Emit("配置上限   : " + (saved10 && readCount == AiConfig.MaxProfiles
                     ? AiConfig.MaxProfiles + " 套可保存/读回" : "失败 - 保存 " + saved10 + " / 读回 " + readCount));
                AiConfig extra = new AiConfig();
                extra.Index = AiConfig.MaxProfiles + 1;
                ten.Add(extra);
                Emit("超限保存   : " + (AiConfig.SaveAll(ten, 1) ? "错误地接受了" : "已拒绝"));
            }
            catch (Exception ex)
            {
                Emit("失败       : " + ex.GetType().Name + " / " + ex.Message);
            }
            finally
            {
                try { if (File.Exists(tempDb)) File.Delete(tempDb); } catch { }
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
            string unusedReadErr;
            AiConfig cfg = AiConfig.Load(out unusedReadErr);
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
    }
}
