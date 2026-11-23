// 主窗口：四页列表、筛选排序、批量操作与 AI 入口
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
    public partial class MainWindow : Window
    {
        // 跨线程保存「当前在途请求」，供 UI 线程点取消时 Abort。
        // 局部 HttpWebRequest[] 的元素赋值不保证对另一线程立即可见，
        // 用 volatile 字段确保取消信号能及时作用到最新请求。
        private sealed class RequestSlot
        {
            internal volatile HttpWebRequest Value;
        }

        private FrameworkElement root;
        private ListBox nav;
        private TextBox search;
        private TextBlock searchHint;
        private TextBlock titleVersion;
        private TextBlock pageTitle;
        private TextBlock pageSubtitle;
        private TextBlock statusText;
        private TextBlock countText;
        private TextBlock listSummary;            // 列表上方的条数与分布统计
        private Button btnAnalyzeAll;             // 逐项 AI 分析
        private bool aiBusy;                     // AI 批量任务进行中，防重入
        // 每次 AI 任务递增的令牌：摘要窗可能在「新任务已启动」之后才关闭，
        // 收尾时只有令牌仍是当前值才允许释放 aiBusy，否则会误放新任务的锁
        private int aiTaskSeq;
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
        // 每次加载递增：超时看门狗放弃本次加载后，旧线程的迟到回调按序号丢弃，
        // 不会把已经超时的结果盖回界面
        private int loadSeq;
        private DispatcherTimer loadWatchdog;
        // 仍在后台跑的加载线程数（含已被看门狗放弃、但卡在 WMI/COM 里的）
        private int liveLoads;
        // 有加载被看门狗放弃且线程仍未返回：此时不能再起新线程，否则会持续堆积
        private bool loadAbandoned;
        private bool closed;
        // 本次批量操作是否涉及每用户服务实例：worker 写、ShowDialog 返回后 UI 读
        private volatile bool lastPerUserService;

        public MainWindow(bool probeOnly, bool admin)
        {
            probe = probeOnly;
            isAdmin = admin;
            Title = AppInfo.WindowTitle;
            // 尺寸单位是 DIP，manifest 为 PerMonitorV2：1366×768@125% 的工作区只有
            // 约 1093×582 DIP，固定 1180×760 会让右侧标题栏按钮和底部状态栏落到屏幕外。
            // 初始尺寸与最小尺寸都按工作区夹取，保证窗口始终完整可见
            Rect wa = SystemParameters.WorkArea;
            double initW = 1180;
            double initH = 760;
            if (wa.Width > 0 && wa.Height > 0)
            {
                initW = Math.Min(initW, Math.Max(640, wa.Width - 24));
                initH = Math.Min(initH, Math.Max(420, wa.Height - 24));
            }
            Width = initW;
            Height = initH;
            MinWidth = Math.Min(960, initW);
            MinHeight = Math.Min(600, initH);
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
            titleVersion = Find<TextBlock>("TitleVersion");
            if (titleVersion != null) titleVersion.Text = "v" + AppInfo.Version;
            pageTitle = Find<TextBlock>("PageTitle");
            pageSubtitle = Find<TextBlock>("PageSubtitle");
            statusText = Find<TextBlock>("StatusText");
            // 读取告警明细只存在内存里：状态栏给出条数，点击即可查看是哪几项失败
            statusText.Cursor = Cursors.Hand;
            statusText.MouseLeftButtonUp += delegate { ShowReadWarnings(); };
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
            InitBackupBar();
            InitSettingsPages();
            InitTransferPage();
            // WMI 与任务计划 COM 都没有可用的取消/超时通道：服务假死时后台线程
            // 可能永不返回，loading 会一直为 true，整个界面卡在「正在读取」且无法自愈
            loadWatchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
            loadWatchdog.Tick += delegate
            {
                loadWatchdog.Stop();
                if (!loading || closed) return;
                loadSeq++;                 // 旧线程的迟到回调按序号丢弃
                loading = false;
                // 记下「已放弃但线程仍在跑」：再点刷新不能又起一个线程，
                // 否则 WMI 假死时每点一次就多一个永久阻塞的线程与 RCW
                loadAbandoned = true;
                pendingLoadPage = NoPending;
                Mouse.OverrideCursor = null;
                UpdateToolbarState();
                SetStatus("读取系统项目超时：WMI 或任务计划服务无响应。本次结果已放弃，请稍后重试");
                DiagLog.Note("读取超时看门狗触发，已放弃本次加载");
            };
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
                // 旧版库被改名备份后重建：明确告知备份位置，用户仍可回滚取证
                if (AppDb.Recreated)
                {
                    Loaded += delegate
                    {
                        Dispatcher.BeginInvoke((Action)delegate
                        {
                            if (quitting || closed) return;
                            Dialogs.Info(this, "配置库已重建",
                                "检测到旧版配置库（" + (AppDb.RecreatedFrom ?? "旧格式") +
                                "），已按当前版本要求重建。\n\n" +
                                "原库已备份为：\n" + (AppDb.RecreatedBackup ?? "(未知)") + "\n\n" +
                                "AI 接口配置、历史建议与分析缓存需要重新录入或生成；" +
                                "如需回滚，请关闭本程序后用上面的备份文件覆盖\n" + AppDb.FilePath);
                        });
                    };
                }
                // 无版本号的历史库（v1.0）已无损升级：只需要轻提示，不必重新配置
                if (AppDb.UpgradedFromLegacy)
                {
                    Loaded += delegate
                    {
                        Dispatcher.BeginInvoke((Action)delegate
                        {
                            if (quitting || closed) return;
                            Dialogs.Info(this, "配置库已无损升级",
                                "检测到 v1.0 的配置库（没有版本号，结构与当前一致），" +
                                "已直接补齐表结构并写入版本号。\n\n" +
                                "AI 接口配置、历史建议与分析缓存均已保留，无需重新录入。");
                        });
                    };
                }
                // 本账户无法读取 exe 同目录的库，已在 LocalAppData 新建独立库
                if (AppPaths.SplitDatabase)
                {
                    Loaded += delegate
                    {
                        Dispatcher.BeginInvoke((Action)delegate
                        {
                            if (quitting || closed) return;
                            Dialogs.Info(this, "已启用独立配置库",
                                "无法读取程序目录下的配置库（权限不足、被杀软锁定或文件只读），" +
                                "已在当前用户目录新建一份独立配置：\n" + AppDb.FilePath + "\n\n" +
                                "本账户的 AI 配置与历史结果保存在这份新库里；" +
                                "原库未被修改，换回有权限的账户仍可继续使用。");
                        });
                    };
                }
            }
            // 主窗口退出时必须用 Closing（早于「拥有窗口」被连带关闭），
            // 否则进度窗的 OnClosing 会把关闭转成「取消」，退出流程挂住
            Closing += delegate(object s, CancelEventArgs e)
            {
                // 配置页有未保存修改时先确认；取消则留在当前页
                if (!ConfirmLeaveSettings()) { e.Cancel = true; return; }
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
            nav.SelectionChanged += delegate
            {
                if (suppressNav) return;
                int idx = nav.SelectedIndex;
                if (idx < 0) return;
                if (!ConfirmLeaveSettings())
                {
                    // 用户选择留在设置页：把导航选中态退回“无选中”
                    suppressNav = true;
                    nav.SelectedIndex = -1;
                    suppressNav = false;
                    return;
                }
                SwitchPage(idx);
            };
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
                    DialogShell.DragWindow(this);
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
            // 挂了恒显 ToolTip，未截断的短文本悬停会弹冗余气泡。
            // ToolTipOpening 是 Direct 事件，必须挂在 TextBlock 本身；
            // 挂在 DataGrid 上既拿不到正确的 sender，Direct 路由下也不会触发
            for (int i = 0; i < grids.Length; i++)
            {
                DataGrid g = grids[i];
                g.LoadingRow += delegate(object s, DataGridRowEventArgs e)
                {
                    DataGridRow row = e.Row;
                    // 单元格在 LoadingRow 时还没生成，等布局完成再挂
                    row.Dispatcher.BeginInvoke((Action)delegate { AttachCellToolTip(row); },
                                               DispatcherPriority.Loaded);
                };
            }
            WireRefresh("BtnStartupRefresh", 0);
            WireRefresh("BtnServiceRefresh", 1);
            WireRefresh("BtnTaskRefresh", 2);
            WireAiAdvice("BtnStartupAi", 0);
            WireAiAdvice("BtnServiceAi", 1);
            WireAiAdvice("BtnTaskAi", 2);
            WireAiAdvice("BtnCtxAi", 3);
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
            // 每页只描述自己的特有项；分隔线用 null 表示，
            // 「AI 分析 + 刷新列表」是四页共用的尾部，只写一次
            List<string[]> items = new List<string[]>();
            if (page == 0)
            {
                items.Add(new[] { "启用", "enable" });
                items.Add(new[] { "禁用", "disable" });
                items.Add(null);
                items.Add(new[] { "复制启动命令", "copy" });
            }
            else if (page == 1)
            {
                items.Add(new[] { "启用（自动启动）", "enable" });
                items.Add(new[] { "改为手动", "manual" });
                items.Add(new[] { "禁用", "disable" });
                items.Add(null);
                items.Add(new[] { "启动服务", "start" });
                items.Add(new[] { "停止服务", "stop" });
                items.Add(null);
                items.Add(new[] { "复制服务名称", "copy" });
            }
            else if (page == 2)
            {
                items.Add(new[] { "启用任务", "enable" });
                items.Add(new[] { "禁用任务", "disable" });
                items.Add(null);
                items.Add(new[] { "立即运行", "run" });
                items.Add(new[] { "停止运行", "stop" });
                items.Add(null);
                items.Add(new[] { "复制任务路径", "copy" });
            }
            else
            {
                items.Add(new[] { "启用", "enable" });
                items.Add(new[] { "禁用（仅当前用户）", "disable" });
                items.Add(new[] { "所有用户屏蔽", "disableAll" });
                items.Add(null);
                items.Add(new[] { "复制标识", "copy" });
            }
            items.Add(null);
            items.Add(new[] { "AI 分析", "ai" });
            items.Add(null);
            items.Add(new[] { "刷新列表", "refresh" });

            foreach (string[] it in items)
            {
                if (it == null) { cm.Items.Add(MakeSeparator()); continue; }
                AddMenu(cm, page, it[0], it[1]);
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
            if (op == "refresh") { RefreshPageForced(page); return; }
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

        // 端点指纹：只存 16 位哈希，避免把可能带查询参数的完整地址写进设置表。
        // 以前用 32 位 FNV：理论上可构造碰撞，让新端点复用旧端点的「已同意」记录、
        // 跳过一次性隐私提示。换成 SHA-256 前 8 字节（64 位），碰撞成本足够高
        private static string EndpointTag(string url)
        {
            string s = (url == null ? "" : url.Trim().ToLowerInvariant());
            try
            {
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
                    StringBuilder sb = new StringBuilder(16);
                    for (int i = 0; i < 8; i++) sb.Append(h[i].ToString("x2"));
                    return sb.ToString();
                }
            }
            catch
            {
                // 理论上不可用（SHA256 是内置实现）：退化为长度 + 首尾字符，仍比固定值安全
                if (s.Length == 0) return "0";
                return s.Length.ToString("x") + ((int)s[0]).ToString("x");
            }
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
                // 配置页不是模态的，无法在当前调用栈里等用户填完再继续；
                // 切页后直接返回，避免用户以为这次 AI 操作已经排队
                OpenAiSettings();
                SetStatus("尚未配置 AI 接口，已切换到「配置 AI」页；完成配置后请重新执行");
                return;
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

            // 单条分析成功后会写 advice 表；这里按本次的 key 回填列表的「AI建议」列，
            // 不重新枚举系统，也不清空其它项的旧结论。AdviceAllowed 已在写入侧与
            // Apply* 的 SetKnown 中再次校验，历史非法标签不会被带进来。
            Dictionary<string, string> one = new Dictionary<string, string>();
            string label;
            bool complete;
            Dictionary<string, string> all = AiAdviceStore.Load(AiAnalysisStore.KindFor(page), out complete);
            if (complete && all.TryGetValue(itemKey, out label)) one[itemKey] = label;
            if (one.Count > 0)
            {
                if (page == 0) AiAdviceStore.ApplyStartup(startupData, one);
                else if (page == 1) AiAdviceStore.ApplyServices(serviceData, one);
                else if (page == 2) AiAdviceStore.ApplyTasks(taskData, one);
                else if (page == 3) AiAdviceStore.ApplyCtx(ctxData, one);
                RepaintItems();
                // 模型类没有 INotifyPropertyChanged，只改内存属性不会通知绑定；
                // 必须重设 ItemsSource 才能让新建议立刻出现在可见行上
                ApplyFilter();
            }
        }

        private void WireAiAdvice(string name, int page)
        {
            Button b = Find<Button>(name);
            if (b == null) return;
            b.Click += delegate { UpdateAiAdvice(page, b); };
        }

        // 行内所有单元格文本挂上「仅裁剪时才显示气泡」的判定。
        // 行会被回收复用，先 Remove 再 Add 保证不会重复订阅
        private static void AttachCellToolTip(DependencyObject root)
        {
            foreach (TextBlock tb in VisualChildren<TextBlock>(root))
            {
                tb.RemoveHandler(ToolTipService.ToolTipOpeningEvent,
                                 new ToolTipEventHandler(TrimmedToolTipOnly));
                tb.AddHandler(ToolTipService.ToolTipOpeningEvent,
                              new ToolTipEventHandler(TrimmedToolTipOnly));
            }
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
            // 单元格模板外层 Border 还有 7,2 的内边距（不在 TextBlock.Padding 上），
            // 只比较 ActualWidth 会把「已被 Padding 裁掉一点」的文本判成未裁剪
            double avail = tb.ActualWidth - tb.Padding.Left - tb.Padding.Right - 8;
            if (avail <= 0) return;                  // 宽度未知：保守放行气泡
            if (ft.Width > avail) return;            // 确实被裁剪，保留气泡
            e.Handled = true;
        }

        // 批量建议失败项的展示名：重试必须保留原始对象，名字只用于弹窗
        private static string AdviceItemName(int page, object item)
        {
            if (page == 0)
            {
                StartupItem s = item as StartupItem;
                if (s != null) return s.Name;
            }
            else if (page == 1)
            {
                ServiceItem s = item as ServiceItem;
                if (s != null) return s.Name;
            }
            else if (page == 2)
            {
                TaskItem s = item as TaskItem;
                if (s != null) return s.Name;
            }
            else if (page == 3)
            {
                CtxMenuItem s = item as CtxMenuItem;
                if (s != null) return s.Name;
            }
            return "未命名项目";
        }

        // 同一键只记一次失败；键直接复用分析/建议共用的 KeyFor
        private static void AddAdviceFailOnce(int page, object item, string reason,
                                              Dictionary<string, object> allByKey,
                                              List<AiFailedItem> failed)
        {
            if (item == null) return;
            string key = AiAnalysisStore.KeyFor(page, item);
            for (int i = 0; i < failed.Count; i++)
                if (string.Equals(failed[i].Key, key, StringComparison.Ordinal)) return;
            AiFailedItem f = new AiFailedItem();
            f.Page = page;
            f.Item = item;
            f.Key = key;
            f.Name = AdviceItemName(page, item);
            f.Reason = string.IsNullOrEmpty(reason) ? "结论未写入缓存" : reason;
            failed.Add(f);
        }

        // 逐项收集本批失败：part 里已有结论的跳过；其余优先用 Parse 的逐项原因，
        // 没有逐项原因时用批次级原因（异常/写库失败等）。这样已成功项不会进入重试列表。
        private static void CollectAdviceFails(int page, BatchPlan plan,
                                               Dictionary<string, string> part,
                                               Dictionary<int, string> missing,
                                               string batchReason,
                                               Dictionary<string, object> allByKey,
                                               List<AiFailedItem> failed,
                                               bool ignorePart = false)
        {
            foreach (KeyValuePair<int, string> kv in plan.Keys)
            {
                // 写库失败时 part 虽有结论但未落盘，必须忽略 part，整批按失败重试
                if (!ignorePart && part != null && part.ContainsKey(kv.Value)) continue;
                object item;
                if (!plan.Items.TryGetValue(kv.Key, out item) || item == null)
                    if (!allByKey.TryGetValue(kv.Value, out item)) continue;
                string reason;
                if (missing == null || !missing.TryGetValue(kv.Key, out reason) || string.IsNullOrEmpty(reason))
                    reason = string.IsNullOrEmpty(batchReason) ? "结论未写入缓存" : batchReason;
                AddAdviceFailOnce(page, item, reason, allByKey, failed);
            }
        }

        // 把整页项目一次性发给云端 AI，结果写入 PCToolsAI.sqlite，然后刷新列表。
        // retryItems/retryCfg 非空时表示失败项重试：跳过前置确认，只处理这些项目
        private void UpdateAiAdvice(int page, Button trigger, List<object> retryItems = null,
                                    AiConfig retryCfg = null, bool retry = false)
        {
            if (quitting || closed) return;
            if (page < 0 || page > 3) return;
            if (!retry)
            {
                // 加载中列表还是旧快照：此刻分析会把旧数据发给模型并缓存，与刷新结果错位
                if (loading) { SetStatus("正在读取系统项目，请稍候再执行操作"); return; }
                if (aiBusy) { SetStatus("已有 AI 批量任务正在进行，请等它结束"); return; }
            }

            // 重试只处理失败项；首次仍以当前可见（可能已被搜索筛选）的行为准
            List<object> items = retry && retryItems != null
                ? retryItems
                : grids[page].Items.Cast<object>().ToList();
            if (items == null || items.Count == 0) { SetStatus("当前列表为空，无需更新 AI 建议"); return; }
            bool filtered = search.Text.Trim().Length > 0;
            string scopeNote = retry ? "（重试失败项）" : (filtered ? "（当前为筛选结果）" : "");

            AiConfig cfg;
            if (retry && retryCfg != null) cfg = retryCfg;
            else
            {
                string loadErr;
                cfg = AiConfig.Load(out loadErr);
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
                    // 配置已改为内容页：切过去就返回，不能像弹窗时代那样立刻重载配置。
                    // 否则这里读到的仍是旧配置（未配置），请求会在没有任何提示的情况下中止
                    SetStatus("尚未配置 AI 接口，已切换到「配置 AI」页；完成配置后请重新执行");
                    OpenAiSettings();
                    return;
                }
                if (!ConfirmAiPrivacy(cfg)) return;
            }

            string system = AiBatch.System(page);
            string kind = AiAnalysisStore.KindFor(page);

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
            List<AiFailedItem> failed = new List<AiFailedItem>();
            // worker 写入、进度窗关闭后的收尾读取：用于把本次标签直接回填内存
            Dictionary<string, string> known = new Dictionary<string, string>();

            aiBusy = true;
            int taskId = ++aiTaskSeq;
            if (trigger != null) trigger.IsEnabled = false;
            // 构造/显示进度窗失败时必须复位 aiBusy，否则本次运行内所有 AI 功能
            // 都会被 4997/5165 的守卫拒绝，只能重启进程
            ProgressWindow pw = null;
            try
            {
            pw = new ProgressWindow(retry ? "重试 AI 建议" : "更新 AI 建议", total);
            // 取消时真正中止在途请求：否则窗口一直挂着等接口返回，用户以为点了没反应
            RequestSlot holder = new RequestSlot();
            pw.OnCancel = delegate
            {
                HttpWebRequest r = holder.Value;
                if (r != null) { try { r.Abort(); } catch { } }
            };
            pw.Owner = this;
            aiProgress = pw;
            pw.Closed += delegate
            {
                if (aiProgress == pw) aiProgress = null;
                // worker 还没真正结束（取消后 3 秒强制关窗）：此时只解除模态，不释放
                // aiBusy、不弹收尾提示。旧 worker 可能仍卡在 HTTP 里，随后继续写
                // advice/usage；让它与第二个任务并发只会互相覆盖甚至 database is locked
                if (!pw.WorkerDone)
                {
                    SetStatus("进度窗口已关闭，后台仍在收尾；完成后会自动回填结果");
                    return;
                }
                // 窗口关闭时直接弹模态框会踩「Owner 已关闭」，延后一轮消息循环再收尾。
                // 走 Ui()：应用退出、Dispatcher 停摆时静默放弃，不抛未处理异常
                Ui((Action)delegate
                {
                    FinishAdviceBatch(page, trigger, total, skippedTotal, ok, saved, error, warning,
                                      failed, cfg, known, taskId);
                });
            };
            // 批量完成后进度窗会保留显示摘要；忙碌标记不在这里释放：
            // 窗口被强制关闭时也会触发本回调，提前释放会让第二个 AI 任务与仍在写库的
            // 旧 worker 并发执行。释放统一放到 worker 的 finally（见下）
            pw.OnCompleted = null;

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
                    AddChunks(page, items, chunks, svcScope);

                    // 键 → 原始项：失败项必须保留对象才能定向重试
                    Dictionary<string, object> allByKey = new Dictionary<string, object>();
                    foreach (object it in items)
                    {
                        string k = AiAnalysisStore.KeyFor(page, it);
                        if (!allByKey.ContainsKey(k)) allByKey[k] = it;
                    }

                    // 逐批「请求 → 解析 → 落库」：以前所有批次攒在内存里、循环结束后才写一次，
                    // 第 2 批之后任何一次失败都会让前面已经花掉 Token 的结果全部丢失
                    int done = 0;
                    // 累计「已落库的 键→标签」与待回填的别名：模板与实例被切到不同批时，
                    // 别名目标可能在前面或后面的批次里，必须等目标落库后再补一次
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
                            string timeReason = "已达单次总时长上限（" + BatchTotalMinutes + " 分钟），剩余 " +
                                                (chunks.Count - ci) + " 批未发送";
                            if (firstFail == null) firstFail = timeReason;
                            for (int rest = ci; rest < chunks.Count; rest++)
                                foreach (object restItem in chunks[rest])
                                    AddAdviceFailOnce(page, restItem, timeReason, allByKey, failed);
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

                        Dictionary<string, string> part = null;
                        Dictionary<int, string> missing = null;
                        string batchReason = null;
                        bool saveFailed = false;
                        try
                        {
                            AiReply reply = AiClient.AskReply(cfg, prompt, system, delegate(HttpWebRequest req)
                            {
                                holder.Value = req;
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
                            part = AiBatch.Parse(reply.Text, plan, page, out discarded, out validSent,
                                                 out missing);
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
                            CollectAdviceFails(page, plan, part, missing, ex.Message, allByKey, failed);
                            done += chunk.Count;
                            int doneCopy = done;
                            Ui((Action)delegate { pw.Report(doneCopy, total, "已完成 " + doneCopy + " 项…"); });
                            continue;
                        }

                        if (part.Count == 0)
                        {
                            failedBatches++;
                            batchReason = "第 " + (ci + 1) + " 批未返回可识别结果";
                            if (firstFail == null) firstFail = batchReason;
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
                                saveFailed = true;
                                batchReason = "写入数据库失败：" + err;
                                if (firstFail == null) firstFail = batchReason;
                            }
                        }
                        CollectAdviceFails(page, plan, part, missing, batchReason, allByKey, failed, saveFailed);
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
                                // 同步进内存结果：收尾不再整页重枚举，别名项也要能立刻回填
                                foreach (KeyValuePair<string, string> kv in late) known[kv.Key] = kv.Value;
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
                    // worker 的所有写库动作都已结束：从这一刻起才允许释放忙碌标记。
                    // 必须在投递 Finish 之前置位，否则强制关窗时 Closed 回调会读到 false
                    pw.MarkWorkerDone();
                    bool cFinal = cancelled;
                    bool uFinal = hasUsage;
                    int inFinal = inTok;
                    int outFinal = outTok;
                    bool okFinal = ok;
                    int savedFinal = saved;
                    string errFinal = error;
                    string warnFinal = warning;
                    // 收尾回填与状态复位：走 Ui 保证在界面线程执行；即使进度窗已被
                    // 强制关闭，结果仍会回填到列表，aiBusy 也会被真正释放
                    Dictionary<string, string> knownFinal = known;
                    Ui((Action)delegate
                    {
                        ReleaseAi(taskId);
                        if (trigger != null) trigger.IsEnabled = true;
                        if (!quitting && !closed) ApplyAdviceLabels(page, knownFinal);
                        UpdateToolbarState();
                    });
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
            catch
            {
                aiBusy = false;
                if (trigger != null) trigger.IsEnabled = true;
                // 后台线程可能已经开始请求：一并中止，避免继续消耗 Token
                AiClient.AbortAll();
                if (pw != null) { try { pw.ForceClose(); } catch { } }
                aiProgress = null;
                throw;
            }
        }

        // 单个请求的提示词字符预算。按最保守的 8K 上下文端点反推：
        // 中文约 1–1.5 字符/token，4000 字符 ≈ 3–4k token，加上系统提示（约 1k）
        // 与输出配额（3072）仍在 8K 以内。早期按 12000 字符切分会直接 400 或截断
        private const int BatchCharBudget = 4000;

        // 单次「更新AI建议」的总时长上限：批数 × 单批超时最坏能拖到半小时，
        // 期间界面只能靠用户手动取消，这里给一个硬边界
        private const int BatchTotalMinutes = 12;

        // 贪心二分：整页装得下就一批，装不下就对半切，直到每段都不超过字符预算
        private static void AddChunks(int page, List<object> items, List<List<object>> outp,
                                      ServiceScope scope)
        {
            if (items.Count == 0) return;
            // 只剩一项就不再切：单条本身超预算时无法再拆，只能原样发送
            if (items.Count <= 1) { outp.Add(items); return; }
            BatchPlan probe = new BatchPlan();
            // 长度估算不需要精确查服务类型：用 quick 模式避免每层都对注册表读一遍
            string s = AiBatch.Build(page, items, probe, scope, true);
            if (s.Length <= BatchCharBudget)
            {
                outp.Add(items);
                return;
            }
            int half = items.Count / 2;
            AddChunks(page, items.GetRange(0, half), outp, scope);
            AddChunks(page, items.GetRange(half, items.Count - half), outp, scope);
        }

        // 批量已落库的标签直接回填内存并重绘：不重新枚举 WMI/COM（1.5 秒以上）。
        // 未覆盖的项保持原值：ApplyX 只写有结论的键。完成回调与关闭收尾共用
        private void ApplyAdviceLabels(int page, Dictionary<string, string> labels)
        {
            if (labels == null || labels.Count == 0) return;
            if (page == 0) AiAdviceStore.ApplyStartup(startupData, labels);
            else if (page == 1) AiAdviceStore.ApplyServices(serviceData, labels);
            else if (page == 2) AiAdviceStore.ApplyTasks(taskData, labels);
            else AiAdviceStore.ApplyCtx(ctxData, labels);
            RepaintItems();
            ApplyFilter();
        }

        // 只有仍是「当前任务」时才释放忙碌标记：摘要窗可能在新任务启动后才关闭
        private void ReleaseAi(int taskId)
        {
            if (taskId == aiTaskSeq) aiBusy = false;
        }

        // 「更新 AI 建议」进度窗关闭后的收尾
        private void FinishAdviceBatch(int page, Button trigger, int total, int skipped,
                                       bool ok, int saved, string error, string warning,
                                       List<AiFailedItem> failed, AiConfig cfg,
                                       Dictionary<string, string> labels, int taskId)
        {
            if (quitting || closed) return;
            ReleaseAi(taskId);
            if (trigger != null) trigger.IsEnabled = true;
            ApplyAdviceLabels(page, labels);
            ApplyFilter();

            if (!string.IsNullOrEmpty(error) && error.StartsWith("已取消", StringComparison.Ordinal))
            {
                SetStatus(error);
                return;
            }

            bool hasFailed = failed != null && failed.Count > 0;
            if (!ok || hasFailed)
            {
                if (!hasFailed)
                {
                    if (error == null) error = "未知原因";
                    SetStatus("更新 AI 建议失败：" + error);
                    Dialogs.Info(this, "更新 AI 建议失败", error);
                    return;
                }

                // 标签已在本方法开头回填并重绑，这里不再整页重新枚举系统
                // 先写状态栏再弹窗：点重试后新任务会立刻启动，不能被它的状态覆盖
                SetStatusAfterLoad("AI 建议已更新：" + saved + " 项（共 " + total + " 项）" +
                    (skipped > 0 ? "，其中 " + skipped + " 项每用户实例按模板回填" : "") +
                    (string.IsNullOrEmpty(error) ? "" : "；" + error) +
                    (string.IsNullOrEmpty(warning) ? "" : "；" + warning));

                StringBuilder msg = new StringBuilder();
                msg.Append("「更新 AI 建议」完成：成功 ").Append(saved)
                   .Append(" 项，失败 ").Append(failed.Count).AppendLine(" 项。").AppendLine();
                int shown = Math.Min(8, failed.Count);
                for (int i = 0; i < shown; i++)
                    msg.AppendLine("· " + failed[i].Name + "：" + failed[i].Reason);
                if (failed.Count > shown)
                    msg.AppendLine("…（其余 " + (failed.Count - shown) + " 项略）");
                if (!string.IsNullOrEmpty(error)) msg.AppendLine().AppendLine(error);
                if (!string.IsNullOrEmpty(warning)) msg.AppendLine(warning);

                if (Dialogs.InfoRetry(this, "部分项目未完成", msg.ToString(), "未完成项目重试") ==
                    ConfirmChoice.Third)
                {
                    List<object> retryItems = new List<object>();
                    foreach (AiFailedItem f in failed) retryItems.Add(f.Item);
                    UpdateAiAdvice(page, trigger, retryItems, cfg, true);
                }
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
                // 配置页不是模态的，无法在当前调用栈里等用户填完再继续；
                // 切页后直接返回，避免用户以为这次 AI 操作已经排队
                OpenAiSettings();
                SetStatus("尚未配置 AI 接口，已切换到「配置 AI」页；完成配置后请重新执行");
                return;
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
                // 按页校验：仅服务页允许「手动」。非法标签在源头清空，
                // 避免写入 advice 表后列表显示一个无对应操作的建议
                if (AiResultWindow.TryTag(tips, out tag, out rest) &&
                    AiAdviceStore.AdviceAllowed(page, tag)) r.Tag = tag;
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
                                       string scope, int totalAll, bool retry = false)
        {
            if (items == null || items.Count == 0) return;
            aiBusy = true;
            int taskId = ++aiTaskSeq;
            // 与「更新AI建议」一致：进度窗非模态期间按钮先置灰，
            // 收尾时 aiBusy=false 后由 UpdateListSummary 按列表内容恢复
            if (trigger != null) trigger.IsEnabled = false;

            // 构造/显示进度窗失败时必须复位 aiBusy，否则本次运行内所有 AI 功能
            // 都会被守卫拒绝，只能重启进程
            ProgressWindow pw = null;
            try
            {
            BatchOutcome outcome = new BatchOutcome();
            // 重试只针对失败项，不再沿用「补全 N 项」的描述
            outcome.Scope = retry ? null : scope;
            outcome.Cfg = cfg;
            outcome.Trigger = trigger;
            outcome.TotalAll = totalAll;
            string adviceKind = AiAnalysisStore.KindFor(page);

            Dictionary<string, string> labels = new Dictionary<string, string>();
            int total = items.Count;
            string pwTitle = retry ? "重试 AI 分析" : (scope == null ? "更新 AI 分析" : "补全 AI 分析");
            pw = new ProgressWindow(pwTitle, total);
            // 取消时中止当前这条请求：否则「取消」要等接口超时（最长 120 秒）才收手
            RequestSlot holder = new RequestSlot();
            pw.OnCancel = delegate
            {
                HttpWebRequest r = holder.Value;
                if (r != null) { try { r.Abort(); } catch { } }
            };
            pw.Owner = this;
            aiProgress = pw;
            pw.Closed += delegate
            {
                if (aiProgress == pw) aiProgress = null;
                // worker 未真正结束时（取消后强制关窗）不释放忙碌标记、不弹收尾提示：
                // 它可能仍卡在 HTTP 调用里，随后继续写 analysis/usage
                if (!pw.WorkerDone)
                {
                    SetStatus("进度窗口已关闭，后台仍在收尾；完成后会自动回填结果");
                    return;
                }
                // 窗口关闭时直接弹模态框会踩「Owner 已关闭」，延后一轮消息循环再收尾。
                // 走 Ui()：应用退出、Dispatcher 停摆时静默放弃
                Ui((Action)delegate { FinishAnalysisBatch(page, outcome, labels, taskId); });
            };
            // 忙碌标记改由 worker 的 finally 释放（见下）：OnCompleted 在强制关窗时
            // 也会被触发，正是「旧 worker 还在写库、新任务已经启动」的来源
            pw.OnCompleted = null;

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
                                holder.Value = req;
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
                            if (!r.Ok) outcome.AddFail(page, item, name, r.Error,
                                                       AiAnalysisStore.KeyFor(page, item));
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
                                if (adviceKind != null && r.Tag.Length > 0 &&
                                    AiAdviceStore.AdviceAllowed(page, r.Tag))
                                    labels[AiAnalysisStore.KeyFor(page, item)] = r.Tag;
                            }
                        }
                        catch (Exception ex)
                        {
                            if (pw.Cancelled) { outcome.Cancelled = true; break; }
                            outcome.AddFail(page, item, name, ex.Message,
                                            AiAnalysisStore.KeyFor(page, item));
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
                    // 所有缓存/用量/建议写库都已结束：先置「worker 已完成」，
                    // 再投递摘要与状态复位。强制关窗时 Closed 回调据此跳过收尾，
                    // 由这里统一释放 aiBusy，不会再出现两个 worker 并发写库
                    pw.MarkWorkerDone();
                    Ui((Action)delegate
                    {
                        ReleaseAi(taskId);
                        if (trigger != null) trigger.IsEnabled = true;
                        if (!quitting && !closed && outcome.AdviceError == null)
                            ApplyAdviceLabels(page, labels);
                        UpdateToolbarState();
                    });
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
            catch
            {
                aiBusy = false;
                if (trigger != null) trigger.IsEnabled = true;
                AiClient.AbortAll();
                if (pw != null) { try { pw.ForceClose(); } catch { } }
                aiProgress = null;
                throw;
            }
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
            // 失败项的可重发信息：重试必须使用原始 item，不能只用展示文案
            internal List<AiFailedItem> FailedItems = new List<AiFailedItem>();
            internal AiConfig Cfg;
            internal Button Trigger;
            internal int TotalAll;

            internal void AddFail(int page, object item, string name, string reason, string key)
            {
                AiFailedItem f = new AiFailedItem();
                f.Page = page;
                f.Item = item;
                f.Key = key;
                f.Name = name;
                f.Reason = reason;
                FailedItems.Add(f);
                Fails.Add("· " + name + "：" + reason);
            }
        }

        // 进度窗关闭后的收尾：复位状态、回填「AI建议」列、报告失败项
        private void FinishAnalysisBatch(int page, BatchOutcome o, Dictionary<string, string> labels, int taskId)
        {
            if (o == null) return;
            if (quitting || closed) return;    // 主窗口正在退出：不刷列表、不弹窗

            ReleaseAi(taskId);
            // 回填「AI建议」列：只重画本地数据，不重新枚举系统
            if (o.AdviceError == null) ApplyAdviceLabels(page, labels);
            // 内部会按 aiBusy 重算「更新AI分析」按钮的可用性
            ApplyFilter();

            StringBuilder msg = null;
            if (o.Failed > 0 || o.CacheError != null)
            {
                msg = new StringBuilder();
                msg.Append("「更新 AI 分析」完成：成功 ").Append(o.Ok)
                   .Append(" 项，失败 ").Append(o.Failed).AppendLine(" 项。").AppendLine();
                int shown = Math.Min(8, o.Fails.Count);
                for (int i = 0; i < shown; i++) msg.AppendLine(o.Fails[i]);
                if (o.Fails.Count > shown) msg.AppendLine("…（其余 " + (o.Fails.Count - shown) + " 项略）");
                if (o.CacheError != null)
                    msg.AppendLine().AppendLine("分析缓存写入失败（本次结果可能没有全部保存）：").Append(o.CacheError);
            }
            // 先写状态栏再弹窗：点重试后新任务会立刻启动，不能被它的状态覆盖
            SetStatusAfterLoad((string.IsNullOrEmpty(o.Scope) ? "AI 分析已更新" : "AI 分析已补全") +
                               "：成功 " + o.Ok + " 项，失败 " + o.Failed + " 项" +
                               (o.CacheError != null ? "；缓存写入失败：" + o.CacheError : ""));
            if (msg == null) return;

            if (o.FailedItems.Count > 0 && o.Cfg != null)
            {
                if (Dialogs.InfoRetry(this, "部分项目未完成", msg.ToString(), "未完成项目重试") ==
                    ConfirmChoice.Third)
                {
                    List<object> retryItems = new List<object>();
                    foreach (AiFailedItem f in o.FailedItems) retryItems.Add(f.Item);
                    RunAnalysisBatch(page, retryItems, o.Cfg, o.Trigger, null, o.TotalAll, true);
                }
            }
            else Dialogs.Info(this, "部分项目未完成", msg.ToString());
        }

        private void OpenAiSettings()
        {
            ShowAiSettingsPage();
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

        private static bool IsBootStartType(string startType)
        {
            return startType == "引导启动" || startType == "系统启动";
        }

        // 启动类型必须能明确识别为四种常规类型之一，才允许在界面上操作。
        // WMI 读取失败时 StartType 会落到「未知」，此时 IsBootStartType 判不出来，
        // 放行就等于把唯一判据绕过；这类行连同 Boot/System 一起按 fail-closed 拒绝
        private static bool IsKnownServiceStartType(string startType)
        {
            return startType == "自动" || startType == "自动（延迟）" ||
                   startType == "手动" || startType == "已禁用";
        }

        // 右键菜单、工具条、自检入口共用这一份判定，避免三处规则漂移
        private bool CanApply(int page, string op, List<object> sel)
        {
            if (sel == null || sel.Count == 0) return false;
            if (op == "ai") return sel.Count == 1;
            // 多选里只要混入一条 RunOnce，整批启用/禁用都不可能全部成功
            if (page == 0 && (op == "enable" || op == "disable") &&
                sel.Any(o => ((StartupItem)o).OneShot)) return false;
            // 引导/系统启动的服务（驱动）不允许改启动类型或启停：多选也整批拒绝，
            // 否则一次「全选 → 禁用」就可能让系统无法引导。启动类型未知（WMI 读取失败）
            // 同样拒绝：界面的判据已经失效，不能当成普通服务放行
            if (page == 1 && (op == "enable" || op == "manual" || op == "disable" ||
                              op == "start" || op == "stop") &&
                sel.Any(o => !IsKnownServiceStartType(((ServiceItem)o).StartType) ||
                             IsBootStartType(((ServiceItem)o).StartType))) return false;
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
            bool listActive = settingsMode == SettingsNone;
            foreach (Button b in toolButtons)
            {
                int page;
                string op;
                if (!toolPages.TryGetValue(b, out page) || !toolOps.TryGetValue(b, out op)) continue;
                List<object> sel = grids[page].SelectedItems.Cast<object>().ToList();
                b.IsEnabled = listActive && !loading && !quitting && !closed && CanApply(page, op, sel);
            }
            if (btnBackup != null)
                btnBackup.IsEnabled = listActive && !loading && !backupBusy && !quitting && !closed && AppDb.Usable;
            if (chkBackupOnly != null)
                // 筛选只读已加载数据，加载中切换是安全的：ApplyLoaded 结束会再跑一次
                // ApplyFilter。原来带 !loading，每次刷新/启停后约 1.5 秒内复选框被禁用，
                // 而模板又没有禁用外观，用户只会觉得「点了没反应」
                chkBackupOnly.IsEnabled = listActive && !quitting && !closed;
        }

        private void WireRefresh(string name, int page)
        {
            // 用户显式点刷新：即使上次已被看门狗放弃，也给一个带确认的强制重试入口。
            // 以前 loadAbandoned 会永久拦住刷新，WMI 一次假死就只能重启程序
            Find<Button>(name).Click += delegate { RefreshPageForced(page); };
        }

        private void RefreshPageForced(int page)
        {
            if (loadAbandoned)
            {
                if (!Dialogs.Confirm(this,
                    "上次读取在 45 秒内没有返回（WMI 或任务计划服务无响应），后台可能仍有线程卡住。\n\n" +
                    "强制再启动一次读取可能让卡住的线程与 RCW 继续堆积；若只是偶发卡顿，重试通常可以恢复。\n\n" +
                    "确定要继续吗？",
                    "强制重新读取")) return;
                loadAbandoned = false;
            }
            LoadAsync(page);
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
                // 已打开的弹窗（如 AI 批量进度窗）各自持有一份资源快照，必须同步刷新
                DialogShell.RefreshTheme();
                if (themeMirror != null)
                {
                    foreach (object key in Application.Current.Resources.Keys)
                        themeMirror[key] = Application.Current.Resources[key];
                }
            }
            RepaintItems();
            // ApplyFilter 会为当前页新建 List 并重设 ItemsSource，足以让新画刷生效；
            // 其他页在切页时重绑。它内部已保存/恢复选中、焦点行与滚动位置，
            // 这里不要再手动置空 ItemsSource，否则非当前页的选中会被清掉
            ApplyFilter();
            UpdateThemeButton();
            ApplyDwmTheme();
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

        // 状态胶囊用的是模型上的静态画刷，主题切换后按当前文本重算一次。
        // 具体配色统一由 DataProvider 的四个 Paint* 决定，枚举阶段与重绘共用同一份规则
        private void RepaintItems()
        {
            foreach (StartupItem it in startupData)
            {
                DataProvider.PaintStartupStatus(it);
                BackupStore.PaintStartup(it);
                AiAdviceStore.Paint(it);
            }
            foreach (ServiceItem it in serviceData)
            {
                DataProvider.PaintServiceStatus(it);
                Manager.PaintStartType(it);
                BackupStore.PaintService(it);
                AiAdviceStore.Paint(it);
            }
            foreach (TaskItem it in taskData)
            {
                DataProvider.PaintTaskStatus(it);
                BackupStore.PaintTask(it);
                AiAdviceStore.Paint(it);
            }
            foreach (CtxMenuItem it in ctxData)
            {
                DataProvider.ApplyCtxColors(it);
                BackupStore.PaintCtx(it);
                AiAdviceStore.Paint(it);
            }
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
            Button transfer = Find<Button>("BtnTransfer");
            transfer.HorizontalContentAlignment = HorizontalAlignment.Left;
            Find<StackPanel>("TransferNavText").Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
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
            // 两个底部按钮都要补同样的偏移，折叠后图标才会保持同一条竖线
            Button transfer = Find<Button>("BtnTransfer");
            Button[] buttons = new Button[] { ai, transfer };
            foreach (Button b in buttons)
            {
                if (b == null) continue;
                Thickness m = b.Margin;
                b.Margin = new Thickness(m.Left + dx, m.Top, m.Right, m.Bottom);
            }
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
                if (hs != null) hs.AddHook(WndProc);
            }
            catch { }
            ApplyDwmTheme();
        }

        // ------------------------------------------------------------ 最大化边界
        // WindowStyle=None + WindowChrome 的窗口，Windows 默认按 resize 边框外扩最大化矩形：
        // 底部压住任务栏、顶部被屏幕裁掉一截（48 高的标题栏只剩 40，图标与搜索框看着都偏上）。
        // 在 WM_GETMINMAXINFO 里把最大化矩形钉到当前显示器的工作区。
        private const int WM_GETMINMAXINFO = 0x0024;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MinMaxInfo
        {
            public NativePoint Reserved;
            public NativePoint MaxSize;
            public NativePoint MaxPosition;
            public NativePoint MinTrackSize;
            public NativePoint MaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public NativeRect Monitor;
            public NativeRect Work;
            public uint Flags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WM_GETMINMAXINFO) return IntPtr.Zero;
            try
            {
                IntPtr mon = MonitorFromWindow(hwnd, 2);          // MONITOR_DEFAULTTONEAREST
                MonitorInfo mi = new MonitorInfo();
                mi.Size = Marshal.SizeOf(typeof(MonitorInfo));
                if (!GetMonitorInfo(mon, ref mi)) return IntPtr.Zero;

                MinMaxInfo mmi = (MinMaxInfo)Marshal.PtrToStructure(lParam, typeof(MinMaxInfo));
                int w = mi.Work.Right - mi.Work.Left;
                int h = mi.Work.Bottom - mi.Work.Top;
                // 相对显示器原点的偏移：任务栏在上/左、副屏负坐标时同样成立
                mmi.MaxPosition.X = mi.Work.Left - mi.Monitor.Left;
                mmi.MaxPosition.Y = mi.Work.Top - mi.Monitor.Top;
                mmi.MaxSize.X = w;
                mmi.MaxSize.Y = h;
                // 只抬高、不压低：副屏比主屏大时不被系统默认的上限卡住，也不影响拖拽
                if (mmi.MaxTrackSize.X < w) mmi.MaxTrackSize.X = w;
                if (mmi.MaxTrackSize.Y < h) mmi.MaxTrackSize.Y = h;

                // handled=true 会跳过 WPF 对这条消息的默认处理（其中包含 MinWidth/MinHeight），
                // 最小跟踪尺寸必须自己补回来，否则窗口能被拖到任意小
                DpiScale dpi = VisualTreeHelper.GetDpi(this);
                int minW = (int)Math.Ceiling(MinWidth * dpi.DpiScaleX);
                int minH = (int)Math.Ceiling(MinHeight * dpi.DpiScaleY);
                if (mmi.MinTrackSize.X < minW) mmi.MinTrackSize.X = minW;
                if (mmi.MinTrackSize.Y < minH) mmi.MinTrackSize.Y = minH;

                Marshal.StructureToPtr(mmi, lParam, true);
                handled = true;
            }
            catch { }
            return IntPtr.Zero;
        }

#if DIAG
        // 自检：最大化后窗口矩形应等于当前显示器工作区，标题栏两个元素应垂直居中
        internal string MaxSizeForTest()
        {
            try
            {
                IntPtr h = new WindowInteropHelper(this).Handle;
                NativeRect r;
                if (!GetWindowRect(h, out r)) return "最大化 : 取窗口矩形失败";
                MonitorInfo mi = new MonitorInfo();
                mi.Size = Marshal.SizeOf(typeof(MonitorInfo));
                if (!GetMonitorInfo(MonitorFromWindow(h, 2), ref mi)) return "最大化 : 取工作区失败";

                int dw = Math.Max(Math.Abs(r.Left - mi.Work.Left), Math.Abs(r.Right - mi.Work.Right));
                int dh = Math.Max(Math.Abs(r.Top - mi.Work.Top), Math.Abs(r.Bottom - mi.Work.Bottom));
                double btnY = CenterYOf("BtnMax");
                double searchY = CenterYOf("SearchHost");
                bool ok = dw <= 1 && dh <= 1 &&
                          Math.Abs(btnY - 24) <= 1.5 && Math.Abs(searchY - 24) <= 1.5;
                return "最大化 : 窗口[" + r.Left + "," + r.Top + "," + r.Right + "," + r.Bottom +
                       "] 工作区[" + mi.Work.Left + "," + mi.Work.Top + "," + mi.Work.Right + "," + mi.Work.Bottom +
                       "] 偏差 " + dw + "x" + dh + "px；按钮中心Y=" + btnY.ToString("0.0") +
                       " 搜索框中心Y=" + searchY.ToString("0.0") + "  " + (ok ? "PASS" : "FAIL");
            }
            catch (Exception ex) { return "最大化 : 异常 " + ex.Message; }
        }

        private double CenterYOf(string name)
        {
            FrameworkElement e = Find<FrameworkElement>(name);
            if (e == null) return -1;
            try { return e.TransformToAncestor(this).Transform(new Point(0, e.ActualHeight / 2)).Y; }
            catch { return -1; }
        }

        // 搜索框在标题栏里必须上下等距；占位符左沿必须与输入文字起点（32.34）对齐
        internal string SearchBoxForTest()
        {
            FrameworkElement bar = Find<FrameworkElement>("TitleBar");
            FrameworkElement box = Find<FrameworkElement>("SearchBox");
            FrameworkElement hint = Find<FrameworkElement>("SearchHint");
            if (bar == null || box == null) return "搜索框 : 找不到元素";
            try
            {
                Point bt = bar.TransformToAncestor(this).Transform(new Point(0, 0));
                Point bx = box.TransformToAncestor(this).Transform(new Point(0, 0));
                double top = bx.Y - bt.Y;
                double bottom = (bt.Y + bar.ActualHeight) - (bx.Y + box.ActualHeight);
                double dx = -1;
                if (hint != null)
                {
                    Point hp = hint.TransformToAncestor(this).Transform(new Point(0, 0));
                    dx = hp.X - (bx.X + 32.34);
                }
                bool gapOk = Math.Abs(top - bottom) <= 1.5;
                bool hintOk = dx >= -1.5 && dx <= 1.5;
                return "搜索框 : 上 " + top.ToString("0.0") + " / 下 " + bottom.ToString("0.0") +
                       " DIP；提示缩进 " + dx.ToString("0.0") + " DIP  " +
                       (gapOk && hintOk ? "PASS" : "FAIL");
            }
            catch (Exception ex) { return "搜索框 : 异常 " + ex.Message; }
        }
#endif

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
            // 上次加载已被看门狗放弃、线程却还卡在 WMI/COM 里：再起一个只会继续堆积
            // 线程、RCW 与 WMI 连接。等它返回（或用户重启）比盲目重试更安全
            if (loadAbandoned)
            {
                SetStatus("上次读取仍未返回（WMI/任务计划服务无响应），请稍后重试；若持续无响应请重启本程序");
                return;
            }
            loading = true;
            int seq = ++loadSeq;
            UpdateToolbarState();
            SetStatus(page < 0 ? "正在读取系统项目…" : "正在刷新…");
            Mouse.OverrideCursor = Cursors.Wait;
            loadWatchdog.Stop();
            loadWatchdog.Start();
            Interlocked.Increment(ref liveLoads);

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
                        // 线程真正返回才清「已放弃」：看门狗放弃后迟到的结果仍按 seq 丢弃，
                        // 但并发计数必须在这里减，才能允许下一次加载
                        if (Interlocked.Decrement(ref liveLoads) == 0) loadAbandoned = false;
                        Mouse.OverrideCursor = null;
                        ApplyLoaded(page, a, b, c, d, warn, seq);
                    });
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void ApplyLoaded(int page, List<StartupItem> a, List<ServiceItem> b,
                                 List<TaskItem> c, List<CtxMenuItem> d, List<string> warn, int seq)
        {
            // 看门狗已经放弃本次加载：迟到的结果直接丢弃。必须先比对序号再停表，
            // 否则旧线程会误停掉新一轮加载刚启动的看门狗
            if (seq != loadSeq) return;
            loadWatchdog.Stop();
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
            string w = warnings.Count > 0 ? " · " + warnings.Count + " 项读取告警（点击查看）" : "";
            string baseText;
            if (pendingStatus != null)
            {
                // 操作结束后会立刻触发刷新，这条「完成：成功 N 项」不能被刷新提示盖掉
                baseText = pendingStatus;
                pendingStatus = null;
            }
            else baseText = page < 0 ? "就绪" : "就绪 · 列表已刷新";
            // 附加提示与基础状态分开拼接：pendingStatus 若带着「目录不可写/库不可用」
            // 后缀进入下一轮，再拼一次就会重复两遍甚至多遍
            string suffix = "";
            if (!AppPaths.Writability)
                suffix += " · 配置目录不可写（" + AppPaths.Dir + "），设置与 AI 结果无法保存";
            else if (AppPaths.SplitDatabase)
                suffix += " · 正在使用当前用户的独立配置库（" + AppPaths.Dir + "）";
            if (!AppDb.Usable)
            {
                string why = AppDb.LastError == null ? "" : AppDb.LastError.Trim();
                if (why.Length > 60) why = why.Substring(0, 60) + "…";
                suffix += " · 配置库不可用" + (why.Length > 0 ? "（" + why + "）" : "");
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
            SetStatus(baseText + suffix + w);
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
            string w = warnings.Count > 0 ? " · " + warnings.Count + " 项读取告警（点击查看）" : "";
            SetStatus("就绪" + w);
        }
#endif

        private void SwitchPage(int idx)
        {
            if (idx < 0 || idx >= pages.Length) return;
            LeaveSettingsToList();
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
            SyncBackupBar();
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
            if (settingsMode != SettingsNone) return;   // 设置页没有列表可过滤
            string q = search.Text.Trim().ToLowerInvariant();
            int page = current < 0 || current >= grids.Length ? 0 : current;
            DataGrid grid = grids[page];
            // 换 ItemsSource 会清空选中行：每敲一个字就丢选中，右键菜单/双击分析都得重新点。
            // 必须记住整组选中项（多选时只记主项会让批量操作的范围悄悄缩小）
            List<object> keep = new List<object>();
            foreach (object o in grid.SelectedItems) keep.Add(o);
            object currentItem = grid.CurrentItem;
            ScrollViewer viewer = ViewerOf(grid);
            double offset = viewer == null ? 0 : viewer.VerticalOffset;

            // 过滤仍对四页各做一次（纯内存字符串比较），但排序、ItemsSource 重设、
            // 列宽重算、选中恢复只针对当前页：以前每次按键都重建四个 DataGrid，
            // 容器/焦点/滚动全部作废，长列表下明显卡顿
            List<StartupItem> a = q.Length == 0 ? new List<StartupItem>(startupData)
                                                 : startupData.Where(x => x.Search.Contains(q)).ToList();
            List<ServiceItem> b = q.Length == 0 ? new List<ServiceItem>(serviceData)
                                                 : serviceData.Where(x => x.Search.Contains(q)).ToList();
            List<TaskItem> c = q.Length == 0 ? new List<TaskItem>(taskData)
                                                 : taskData.Where(x => x.Search.Contains(q)).ToList();
            IEnumerable<CtxMenuItem> src = q.Length == 0 ? ctxData
                                                         : ctxData.Where(x => x.Search.Contains(q));
            // 备份基线筛选：未备份项（BackupDiff=true）也会进入结果，
            // 否则备份后新增的项目在“只看不一致”时会被漏掉
            if (backupOnly[0]) a = a.Where(x => x.BackupDiff).ToList();
            if (backupOnly[1]) b = b.Where(x => x.BackupDiff).ToList();
            if (backupOnly[2]) c = c.Where(x => x.BackupDiff).ToList();
            List<CtxMenuItem> d = src.Where(x => x.Tab == ctxTab &&
                                                 (!backupOnly[3] || x.BackupDiff)).ToList();

            int shown;
            if (page == 0)
            {
                SortBy(a, sortPath[0], sortDir[0]);
                grid.ItemsSource = a;
                shown = a.Count;
            }
            else if (page == 1)
            {
                SortBy(b, sortPath[1], sortDir[1]);
                grid.ItemsSource = b;
                shown = b.Count;
            }
            else if (page == 2)
            {
                SortBy(c, sortPath[2], sortDir[2]);
                grid.ItemsSource = c;
                shown = c.Count;
            }
            else
            {
                SortBy(d, sortPath[3], sortDir[3]);
                grid.ItemsSource = d;
                shown = d.Count;
            }

            if (keep.Count > 0)
            {
                // 只补回仍然存在于新结果里的行：被筛选掉的选中项不应强行加回去
                HashSet<object> alive = new HashSet<object>();
                foreach (object o in grid.Items) alive.Add(o);
                try
                {
                    grid.SelectedItems.Clear();
                    foreach (object o in keep) if (alive.Contains(o)) grid.SelectedItems.Add(o);
                }
                catch (Exception ex) { DiagLog.Swallow("恢复选中 页 " + page, ex); }
            }
            // 焦点行与滚动位置也要恢复：只恢复选中会让键盘连选和浏览位置每次按键都被打回顶部
            if (currentItem != null && grid.Items.Contains(currentItem))
                grid.CurrentItem = currentItem;
            if (offset > 0)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    ScrollViewer v = ViewerOf(grid);
                    if (v != null) v.ScrollToVerticalOffset(offset);
                }), DispatcherPriority.Loaded);
            }

            RestoreSortGlyphs();
            // 换数据源后 DataGrid 会沿用旧的列宽缓存，星号列不会自动铺满，这里强制重算一次
            FitLastColumn(grid);
            empties[page].Visibility = shown == 0 ? Visibility.Visible : Visibility.Collapsed;
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
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return -1;
            if (b == null) return 1;
            string sa = a as string;
            string sb = b as string;
            // 用 OrdinalIgnoreCase 而不是 CurrentCulture：区域排序对可忽略字符
            //（连字符、软连字符、变音符）不保证传递性，会让比较器不满足严格弱序
            if (sa != null && sb != null) return string.Compare(sa, sb, StringComparison.OrdinalIgnoreCase);
            if (a.GetType() == b.GetType())
            {
                IComparable ca = a as IComparable;
                if (ca != null)
                {
                    try { return ca.CompareTo(b); }
                    // 比较失败也必须给出稳定顺序，不能退化成一个固定的 0：
                    // 0 会让 a<b、b<c 但 a==c，同样破坏传递性
                    catch (Exception ex)
                    {
                        DiagLog.Swallow("排序比较 " + a.GetType().Name, ex);
                        return string.Compare(a.ToString(), b.ToString(), StringComparison.Ordinal);
                    }
                }
            }
            // 类型混列：按类型全名定序，保证任意三元组都一致
            int byType = string.Compare(a.GetType().FullName, b.GetType().FullName, StringComparison.Ordinal);
            if (byType != 0) return byType;
            return string.Compare(a.ToString(), b.ToString(), StringComparison.Ordinal);
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
            if (listSummary == null || settingsMode != SettingsNone) return;
            bool filtered = search.Text.Trim().Length > 0 || backupOnly[current];
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

        // 读取告警明细：没有告警时点击状态栏不做任何事
        private void ShowReadWarnings()
        {
            if (warnings.Count == 0) return;
            StringBuilder sb = new StringBuilder();
            // 明细可能上百条：只渲染前 200 条，条数仍是真实值（不再被 60 条上限吃掉）
            int shown = Math.Min(200, warnings.Count);
            for (int i = 0; i < shown; i++) sb.AppendLine("· " + warnings[i]);
            if (warnings.Count > shown)
                sb.AppendLine("…（其余 " + (warnings.Count - shown) + " 条未显示，可设置 PCTOOLS_LOG=1 后复查）");
            Dialogs.Info(this, "读取告警（共 " + warnings.Count + " 项）", sb.ToString());
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
            // 取消后进度窗可能先关、worker 还在后台收尾：计数与结果列表必须加锁，
            // 否则 UI 线程读到正在被 worker 修改的 List 会抛「集合已修改」
            object stateGate = new object();
            bool workerDone = false;
            // 每用户服务实例改的是模板服务，需要重新登录才生效，状态栏提示一句。
            // 判定要读 HKLM 服务 Type，不能在 UI 线程对每个选中项各开一次注册表
            lastPerUserService = false;

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
                        if (page == 1 && Manager.IsPerUserInstance(((ServiceItem)item).ServiceName))
                            lastPerUserService = true;
                        string name = Manager.ItemName(item);
                        string err = Manager.Apply(page, op, item);
                        lock (stateGate)
                        {
                            if (err == null) ok++;
                            else if (err.StartsWith(Manager.PartialPrefix, StringComparison.Ordinal))
                                notes.Add("· " + name + "：" + err.Substring(Manager.PartialPrefix.Length));
                            else fails.Add("· " + name + "：" + err);
                        }
                        int done = i + 1;
                        Ui(delegate { pw.Report(done, items.Count, name); });
                    }
                }
                catch (Exception ex)
                {
                    lock (stateGate) fails.Add("· 批量操作中断：" + ex.Message);
                }
                finally
                {
                    lock (stateGate) workerDone = true;
                    Ui(delegate { pw.Finish(); });
                }
            });
            worker.IsBackground = true;
            worker.Start();
            pw.ShowDialog();      // worker 跑完（或取消后收尾）才会真正关闭

            // 收尾时要弹模态框，主窗已关闭就什么都不做（Owner 已失效）
            if (quitting || closed) return;
            bool cancelled = pw.Cancelled;
            bool abandoned;
            bool perUserSvc;
            lock (stateGate)
            {
                abandoned = !workerDone;
                perUserSvc = lastPerUserService;
                fails = new List<string>(fails);
                notes = new List<string>(notes);
            }
            // 进度窗被强制关闭、worker 仍在跑：此时结果不完整，刷新列表会把
            // 尚未执行完的项目当成「没变」；交给用户稍后手动刷新
            if (abandoned)
            {
                SetStatusAfterLoad("「" + verb + "」已取消：后台仍有操作在收尾，完成后请刷新列表确认");
                return;
            }
            RefreshPage(page);

            if (fails.Count > 0)
            {
                StringBuilder msg = new StringBuilder();
                msg.Append("「").Append(verb).Append("」完成：成功 ").Append(ok + notes.Count)
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
                SetStatusAfterLoad("「" + verb + "」完成：成功 " + (ok + notes.Count) + " 项，失败 " + fails.Count + " 项"
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
                        "成功 " + (ok + notes.Count) + " 项，其中 " + notes.Count + " 项部分生效：\n\n" + ns.ToString());
                }
                SetStatusAfterLoad("「" + verb + "」完成：成功 " + (ok + notes.Count) + " 项，列表已刷新"
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
            string unusedReadErr;
            AiConfig cfg = AiConfig.Load(out unusedReadErr);
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
        // 自检口径：返回已加载的数据条数（ApplyFilter 只绑定当前页，网格行数不代表加载量）
        public int Rows(int page)
        {
            if (page == 1) return serviceData.Count;
            if (page == 2) return taskData.Count;
            if (page == 3) return ctxData.Count;
            return startupData.Count;
        }
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
}
