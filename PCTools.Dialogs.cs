// 对话框：进度、确认、提示、AI 配置与结果窗
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
        private readonly bool allowCancel = true;
        private bool finished;
        private bool completed;      // 已跑完但窗口保留，等用户点「关闭」
        private bool forceClose;     // 主窗口退出时强制放行
        private DispatcherTimer cancelWatch;   // 取消后若 worker 迟迟不返回，强制关窗释放模态锁
        internal Action OnCancel;    // 取消回调：让调用方中止在途请求
        internal Action OnCompleted; // 跑完但仍留窗时调用：主窗先复位忙碌状态，不必等用户关窗

        internal bool Cancelled { get { return cancelled; } }

#if DIAG
        internal bool CancelVisibleForTest
        {
            get { return cancelBtn != null && cancelBtn.Visibility == Visibility.Visible; }
        }
#endif

        internal bool Completed { get { return completed; } }

        internal string Detail { get { return detail == null ? "" : detail.Text; } }

        internal string Tokens { get { return tokens == null ? "" : tokens.Text; } }

        internal ProgressWindow(string verb, int total)
            : this(verb, total, true)
        {
        }

        // allowCancel=false：导入/导出这类逐条写库、中途关窗会留下半套数据的操作，
        // 不提供取消入口，并忽略标题栏 X 与 Alt+F4，保证 ShowDialog 只在 worker 跑完后返回
        internal ProgressWindow(string verb, int total, bool allowCancel)
        {
            this.allowCancel = allowCancel;
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

            if (allowCancel) cancelBtn.Click += OnCancelClick;
            else cancelBtn.Visibility = Visibility.Collapsed;
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
            // worker 可能卡在 WMI/SCM 调用里（WMI 已设 20 秒上限，SCM 的 15 秒等待也在
            // 单次操作内），但它毕竟是后台线程：不能让模态窗无限期挡住主界面。
            // 3 秒内没结束就强制关窗，worker 在后台自行收尾（Abort 已由 OnCancel 触发）
            if (cancelWatch == null)
            {
                cancelWatch = new DispatcherTimer();
                cancelWatch.Interval = TimeSpan.FromSeconds(3);
                cancelWatch.Tick += delegate
                {
                    cancelWatch.Stop();
                    if (finished) return;
                    if (detail != null) detail.Text = "仍在等待后台操作返回…已关闭进度窗，结果可能不完整";
                    ForceClose();
                };
            }
            cancelWatch.Stop();
            cancelWatch.Start();
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
            if (cancelWatch != null) cancelWatch.Stop();
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
            // 已完成：主窗的「AI 忙碌」必须立刻复位。留窗只是给用户看摘要，
            // 不该让整个 AI 功能在用户点关闭之前一直提示「已有任务」
            Action done = OnCompleted;
            if (done != null) { try { done(); } catch { } }
        }

        // 主窗口退出时强制关掉，不再把 Close 转成「取消」
        internal void ForceClose()
        {
            if (cancelWatch != null) cancelWatch.Stop();
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
                // 不可取消的任务：把 X / Alt+F4 也一并忽略，避免窗口先关、worker 还在写库
                if (!allowCancel) return;
                RequestCancel();
                return;
            }
            base.OnClosing(e);
        }
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

    // ------------------------------------------------------------------ AI dialogs
    internal static class DialogShell
    {
        // 已打开的弹窗：主题切换时重新灌入资源字典。
        // 用弱引用，窗口关闭后不阻止回收
        private sealed class ThemedDialog
        {
            internal WeakReference View;
            internal WeakReference Window;
            internal ResourceDictionary Local;
        }

        private static readonly List<ThemedDialog> dialogs = new List<ThemedDialog>();

        internal static void AttachTheme(FrameworkElement view)
        {
            if (Application.Current == null || Application.Current.Resources == null) return;
            // 启动早期（单实例拒绝提示）主题尚未装载：先应用一套浅色默认主题，
            // 否则 DynamicResource 全部解析失败，主按钮会变成白字透明底
            if (Application.Current.Resources.Count == 0)
            {
                try { ThemeManager.Apply(Application.Current, true); }
                catch { }
            }
            ResourceDictionary local = new ResourceDictionary();
            foreach (object key in Application.Current.Resources.Keys)
                local[key] = Application.Current.Resources[key];
            view.Resources.MergedDictionaries.Add(local);
            dialogs.Add(new ThemedDialog { View = new WeakReference(view), Local = local });
        }

        // 主题切换后刷新已打开弹窗的快照，避免深色主窗旁边挂着一个纯白弹窗
        internal static void RefreshTheme()
        {
            if (Application.Current == null || Application.Current.Resources == null) return;
            for (int i = dialogs.Count - 1; i >= 0; i--)
            {
                ThemedDialog d = dialogs[i];
                FrameworkElement view = d.View.Target as FrameworkElement;
                Window w = d.Window == null ? null : d.Window.Target as Window;
                if (view == null || (d.Window != null && w == null)) { dialogs.RemoveAt(i); continue; }
                try
                {
                    d.Local.Clear();
                    foreach (object key in Application.Current.Resources.Keys)
                        d.Local[key] = Application.Current.Resources[key];
                    if (w != null) w.Background = SolidTheme("DialogBg");
                }
                catch (Exception ex) { DiagLog.Swallow("刷新弹窗主题", ex); }
            }
        }

        internal static void BindWindow(Window w, FrameworkElement view)
        {
            for (int i = dialogs.Count - 1; i >= 0; i--)
            {
                if (!object.ReferenceEquals(dialogs[i].View.Target, view)) continue;
                dialogs[i].Window = new WeakReference(w);
                // 窗口关闭时把词条与资源快照一起清掉：只靠 RefreshTheme 清理的话，
                // 不切主题就会一直累积（每个弹窗一份完整资源字典）
                w.Closed += delegate { RemoveDialog(view); };
                return;
            }
        }

        private static void RemoveDialog(FrameworkElement view)
        {
            for (int i = dialogs.Count - 1; i >= 0; i--)
            {
                if (!object.ReferenceEquals(dialogs[i].View.Target, view)) continue;
                try { dialogs[i].Local.Clear(); }
                catch (Exception ex) { DiagLog.Swallow("释放弹窗主题快照", ex); }
                dialogs.RemoveAt(i);
            }
        }

        // 弹窗统一初始化。
        // 注意：AllowsTransparency=true 的分层窗口会强制关闭 ClearType（只能用灰度抗锯齿），
        // 中文小字会发虚、带锯齿，所以这里改为不透明窗口 + WindowChrome 拿 DWM 阴影与圆角。
        internal static void Prepare(Window w, FrameworkElement view)
        {
            Prepare(w, view, false);
        }

        // scrollable=true：把内容放进 ScrollViewer。用于高度可能超出工作区的表单窗
        //（配置 AI），MaxHeight 生效后底部按钮仍可通过滚动到达
        internal static void Prepare(Window w, FrameworkElement view, bool scrollable)
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


            // 卡片外框由窗口本身承担：XAML 侧已不再声明 Margin/CornerRadius/阴影，
            // 这里无需再逐项清零


            // 右上角统一加关闭按钮（四个弹窗共用）
            Grid host = new Grid();
            if (scrollable)
            {
                ScrollViewer sv = new ScrollViewer();
                sv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                sv.Content = view;
                host.Children.Add(sv);
            }
            else host.Children.Add(view);
            host.Children.Add(MakeCloseButton(w));
            w.Content = host;
            BindWindow(w, view);

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
                DragWindow(w);
            };
        }

        // 无边框窗口拖动。WPF 的 DragMove 在最大化状态下会抛 InvalidOperationException，
        // 不处理就会落到全局兜底，用户每拖一次标题栏看到一次「程序遇到异常」。
        // 最大化时先按鼠标横向比例还原窗口，把指针留在标题栏内，再继续拖动
        internal static void DragWindow(Window w)
        {
            try
            {
                if (w.WindowState == WindowState.Maximized)
                {
                    Rect rb = w.RestoreBounds;
                    Point p = Mouse.GetPosition(w);
                    double ratio = w.ActualWidth <= 0 ? 0.5 : p.X / w.ActualWidth;
                    if (ratio < 0) ratio = 0; else if (ratio > 1) ratio = 1;
                    Point screen = w.PointToScreen(p);
                    w.WindowState = WindowState.Normal;
                    if (!rb.IsEmpty && rb.Width > 0 && rb.Height > 0)
                    {
                        w.Left = screen.X - rb.Width * ratio;
                        w.Top = screen.Y - Math.Min(Math.Max(p.Y, 0), 18);
                    }
                }
                w.DragMove();
            }
            catch
            {
                // 鼠标已松开、窗口正在关闭：拖动失败无需打扰用户
            }
        }

        // 主窗口标题栏与弹窗拖动共用：覆盖所有可交互控件（含滚动条与下拉框），
        // 否则从这些控件上按下会被误当成拖动窗口。
        // 不包含 ScrollViewer：配置/结果窗的整块内容都包在滚动区里，把它算作可交互
        // 会让窗口任何位置都无法拖动（小屏遮住主界面时移不开）
        internal static bool IsInteractive(DependencyObject o)
        {
            while (o != null)
            {
                if (o is Button || o is TextBox || o is PasswordBox
                    || o is System.Windows.Controls.Primitives.ScrollBar
                    || o is ComboBox)
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
                // 仅信息弹窗也可以带一个操作按钮（例如“未完成项目重试”）：
                // thirdText 为 null 时仍是原来的单按钮形态
                if (thirdText == null) third.Visibility = Visibility.Collapsed;
                yes.IsDefault = true;
                // 只剩一个按钮：Esc 与点它等价。不要再单独挂 KeyDown，
                // 否则同一个 Esc 会同时触发 IsCancel 与 KeyDown 两次 Close()
                yes.IsCancel = true;
            }
            no.Click += delegate { Choice = ConfirmChoice.Cancel; Close(); };
            yes.Click += delegate { Choice = ConfirmChoice.Primary; Close(); };
            third.Click += delegate { Choice = ConfirmChoice.Third; Close(); };
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

        // 仅信息 + 一个操作按钮：底部显示「知道了」和重试按钮，返回 Third 表示点击了重试
        internal static ConfirmChoice InfoRetry(Window owner, string title, string message, string retryText)
        {
            ConfirmWindow w = new ConfirmWindow(title, "请查看以下信息", message, "知道了", retryText, true);
            Place(w, owner);
            w.ShowDialog();
            return w.Choice;
        }
    }

    // 配置窗口左侧列表的行模型（重建 ItemsSource 即可刷新「使用中」标记）
    // 「配置 AI」的窗口外壳：主窗口已改为内容页，这里保留窗口形态供自检与外部入口复用。
    // 全部业务逻辑在 AiSettingsView 中，窗口只负责尺寸、主题与未保存确认
    public class AiSettingsWindow : Window
    {
        private readonly AiSettingsView view;

        internal bool Saved { get { return view != null && view.Saved; } }

#if DIAG
        internal AiSettingsView ViewForTest { get { return view; } }
#endif

        internal AiSettingsWindow(AiConfig cfg)
        {
            Title = "配置 AI";
            Width = 720;
            SizeToContent = SizeToContent.Height;
            MaxHeight = Math.Max(360, SystemParameters.WorkArea.Height * 0.86);
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            view = new AiSettingsView(cfg, false);
            DialogShell.AttachTheme(view);
            DialogShell.Prepare(this, view, true);

            Loaded += delegate
            {
                Rect wa = DialogShell.WorkAreaOf(this);
                if (!wa.IsEmpty && wa.Height > 0) MaxHeight = Math.Max(360, wa.Height * 0.86);
            };
            Closing += delegate(object s, CancelEventArgs e)
            {
                if (!view.ConfirmDiscard()) e.Cancel = true;
            };
            Closed += delegate { view.Abort(); };
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) Close();
            };
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

#if DIAG
        // 自检入口：不读不写缓存，每次都直接请求
        internal AiResultWindow(string subject, string prompt, AiConfig cfg,
                                string systemPrompt, string adviceHeading)
            : this(subject, -1, null, prompt, cfg, systemPrompt, adviceHeading)
        {
        }
#endif

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
            // 正文/建议两个 TextBox 自带滚动，限制窗口高度即可保证底部按钮可见
            MaxHeight = Math.Max(360, SystemParameters.WorkArea.Height * 0.88);
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
                // 构造期没有 HWND，只能按主屏兜底；加载后按窗口所在显示器重算
                Rect wa = DialogShell.WorkAreaOf(this);
                if (!wa.IsEmpty && wa.Height > 0) MaxHeight = Math.Max(360, wa.Height * 0.88);
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
                        {
                            cacheOk = AiAnalysisStore.Save(kind, itemKey, cfg.Model, intro, tips,
                                                           reply.InTokens, reply.OutTokens, out cacheErr);
                            // 逐项分析成功后同步写入「AI建议」缓存：批量分析路径会写，
                            // 单条分析此前只写 analysis，导致列表建议列始终空白。
                            // 标签先按页校验，避免写入页面上无对应操作的「手动」
                            if (cacheOk)
                            {
                                string tag, rest;
                                if (TryTag(tips, out tag, out rest) && AiAdviceStore.AdviceAllowed(page, tag))
                                {
                                    Dictionary<string, string> one = new Dictionary<string, string>();
                                    one[itemKey] = tag;
                                    string adviceErr;
                                    // 写失败不推翻已成功的分析缓存，仅记录供状态栏提示
                                    AiAdviceStore.Save(kind, one, out adviceErr);
                                }
                            }
                        }
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
}
