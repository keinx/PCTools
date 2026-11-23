// 设置页：可复用的「配置 AI」视图 + 主窗口的设置页切换逻辑
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
using System.Windows.Controls.Primitives;
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
    public class AiProfileRow
    {
        public string Title { get; set; }
        public string Sub { get; set; }
        // Id 是配置身份（AiConfig.Index），不是列表下标：删过再新增时两者会不一致
        public int Id { get; set; }
        public bool Active { get; set; }
        public bool CanDelete { get; set; }
        public string Stats { get; set; }
        public string StatsTip { get; set; }
    }

    // 「配置 AI」的可复用视图：窗口外壳与主窗口内容页共用同一套逻辑
    internal class AiSettingsView : UserControl
    {
        private readonly bool pageMode;   // true = 主窗口内容页；false = 独立窗口
        // 只有点了「保存」并真正写盘成功才为 true，供调用方决定提示什么
        internal bool Saved { get; private set; }
        internal event Action SavedChanged;

        private TextBox fName;
        private TextBox fEndpoint;
        private TextBox fKey;
        private TextBox fModel;
        private ListBox list;
        private TextBlock status;
        private StackPanel statusHost;
        private Button btnTest;
        private Button btnNew;
        private Button btnSaveOne;
        private Button btnClearOne;

        private List<AiConfig> profiles;
        // 打开窗口时的 地址/模型/Key 快照：只有这三项变了才清空该配置的累计用量
        private Dictionary<int, AiConfig> original;
        private Dictionary<int, AiUsageStore.Row> usageRows = new Dictionary<int, AiUsageStore.Row>();
        private bool usageUnavailable;
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
#if DIAG
        // 自检要连续点行内「删除」，跳过确认框，否则模态框会把消息循环卡住
        private bool skipConfirmForTest;
#endif


        internal AiSettingsView(AiConfig cfg, bool pageMode)
        {
            this.pageMode = pageMode;

            FrameworkElement view = (FrameworkElement)XamlReader.Parse(Views.AiSettings);
            Content = view;

            // 上限只有一份来源：徽标文案随常量走，避免以后改数字时漏掉页面文案
            TextBlock capBadge = (TextBlock)view.FindName("AiCapBadge");
            if (capBadge != null) capBadge.Text = "最多 " + AiConfig.MaxProfiles + " 套";

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
            btnSaveOne = (Button)view.FindName("BtnSaveOne");
            btnClearOne = (Button)view.FindName("BtnClearOne");

            profiles = AiConfig.LoadAll(out active, out loadError);
            if (profiles.Count == 0)
            {
                AiConfig fresh = new AiConfig();
                fresh.Index = 1;
                fresh.Name = "配置 1";
                profiles.Add(fresh);
            }
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
            // 行内「激活 / 删除」按钮在 DataTemplate 里，不能写 Click=
            list.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnRowButtonClick));
            if (btnSaveOne != null) btnSaveOne.Click += delegate { SaveCurrentProfile(); };
            if (btnClearOne != null) btnClearOne.Click += delegate { ClearProfile(); };
            // 页面模式没有「取消/关闭」按钮：只留保存；窗口模式仍由外壳的 Closing 确认
            Button btnCancel = (Button)view.FindName("BtnCancel");
            Button btnSave = (Button)view.FindName("BtnSave");
            if (pageMode)
            {
                btnCancel.Visibility = Visibility.Collapsed;
                btnSave.Content = "保存全部";
                btnSave.ToolTip = "保存全部配置并停留在本页";
            }
            btnCancel.Click += delegate
            {
                Window w = Window.GetWindow(this);
                if (w != null) w.Close();
            };
            btnSave.Click += delegate
            {
                if (!SaveAll()) return;
                if (pageMode) return;
                Window w = Window.GetWindow(this);
                if (w != null) w.Close();
            };
            btnTest.Click += delegate { TestAsync(); };
            // 页面被切走 / 窗口关闭时都要中止在途测试：否则线程会空等到 90 秒超时
            Unloaded += delegate { Abort(); };

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
            // 删除后 editing 可能是旧位置：先进这里夹到合法范围，避免高亮与表单错位
            if (profiles.Count > 0)
                editing = Math.Max(0, Math.Min(editing, profiles.Count - 1));
            bool usageReadOk;
            usageRows = AiUsageStore.LoadAll(out usageReadOk);
            usageUnavailable = !usageReadOk;
            List<AiProfileRow> rows = new List<AiProfileRow>();
            for (int i = 0; i < profiles.Count; i++)
            {
                AiConfig c = profiles[i];
                AiProfileRow r = new AiProfileRow();
                r.Title = c.Title;
                r.Sub = string.IsNullOrEmpty(c.Endpoint) ? "未填写接口地址" : c.Endpoint;
                r.Id = c.Index;
                r.Active = c.Index == active;
                r.CanDelete = profiles.Count > 1;
                r.Stats = FormatStats(c.Index);
                r.StatsTip = FormatStatsTip(c.Index);
                rows.Add(r);
            }
            syncing = true;
            list.ItemsSource = rows;
            list.SelectedIndex = editing >= 0 && editing < rows.Count ? editing : 0;
            syncing = false;
            btnNew.IsEnabled = profiles.Count < AiConfig.MaxProfiles;
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

            // 读取失败是窗口级状态，不属于某一套配置：切换后必须重新挂出来，
            // 否则用户只会在点保存时看到「禁止保存」，不知道是打开窗口时就没读到
            if (!string.IsNullOrEmpty(loadError))
            {
                status.Text = "读取现有配置失败（本窗口禁止保存）：" + loadError;
                statusHost.Visibility = Visibility.Visible;
            }
        }

        // 明确清除已保存的 Key
        // 清空当前这套的四个输入框；只改编辑态，点「保存此配置」后才写库。
        // 这里刻意不调 CommitFields：否则切到别的配置再点「保存全部」，
        // 会在用户没点保存的情况下把这套的 Key/地址一起抹掉
        private void ClearProfile()
        {
            syncing = true;
            fName.Text = "";
            fEndpoint.Text = "";
            fModel.Text = "";
            fKey.Text = "";
            syncing = false;
            savedKey = "";
            keyMasked = false;
            keyCleared = true;
            fKey.ToolTip = "输入新的 API Key；清空后点「保存此配置」才会写库";
            status.Text = "已清空输入框，点「保存此配置」后写入。";
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

        internal bool IsDirty { get { return Dirty(); } }

        internal bool ConfirmDiscard()
        {
            if (!Dirty()) return true;
            return Dialogs.Confirm(Window.GetWindow(this), "有未保存的修改，确定放弃吗？", "放弃修改");
        }

        // 中止在途的连通性测试，并让后台回调不再写已卸载的控件
        internal void Abort()
        {
            closed = true;
            HttpWebRequest r = testReq;
            if (r != null) { try { r.Abort(); } catch { } }
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

        private int PositionOf(int id)
        {
            for (int i = 0; i < profiles.Count; i++) if (profiles[i].Index == id) return i;
            return -1;
        }

        // 按配置身份删除：行内按钮拿到的是 Id，不能当列表下标用
        private void RemoveProfile(int id)
        {
            CommitFields();
            if (profiles.Count <= 1)
            {
                status.Text = "至少要保留一套配置。";
                statusHost.Visibility = Visibility.Visible;
                return;
            }
            int pos = PositionOf(id);
            if (pos < 0) return;
            bool skipConfirm = false;
#if DIAG
            skipConfirm = skipConfirmForTest;
#endif
            // 删除不可撤销（含该配置的累计用量），先确认
            if (!skipConfirm && !Dialogs.Confirm(Window.GetWindow(this), "确定要删除配置「" + profiles[pos].Title + "」吗？\n\n" +
                                      "该配置的接口地址、模型、Key 与累计用量会一起清除，且无法撤销。",
                                 "删除配置"))
                return;
            bool wasActive = profiles[pos].Index == active;
            bool wasEditing = pos == editing;
            int keepId = (wasEditing || editing < 0) ? -1 : profiles[Math.Min(editing, profiles.Count - 1)].Index;
            profiles.RemoveAt(pos);
            if (wasActive) active = profiles[Math.Min(pos, profiles.Count - 1)].Index;
            if (wasEditing) editing = Math.Min(pos, profiles.Count - 1);
            else editing = PositionOf(keepId);
            editing = Math.Max(0, Math.Min(editing, profiles.Count - 1));
            RebuildList();
            SelectProfile(editing);
            if (wasActive)
            {
                status.Text = "当前启用的是「" + profiles[editing].Title + "」（原配置已删除），点「保存」后生效。";
                statusHost.Visibility = Visibility.Visible;
            }
            else statusHost.Visibility = Visibility.Collapsed;
        }

        private void ActivateProfile(int id)
        {
            if (id == active) return;
            // 行内按钮不会触发 ListBox 的选中变化，未提交的输入必须在这里自己收口
            CommitFields();
            int pos = PositionOf(id);
            if (pos < 0) return;
            active = id;
            editing = pos;
            RebuildList();
            SelectProfile(editing);
            status.Text = "已把「" + profiles[editing].Title + "」设为当前配置，点「保存」后生效。";
            statusHost.Visibility = Visibility.Visible;
        }

        // 行内「激活 / 删除」的 Click 统一在这里接：DataTemplate 是运行时解析的，
        // 没有 code-behind 根对象，写不了 Click=
        private void OnRowButtonClick(object sender, RoutedEventArgs e)
        {
            Button b = e.OriginalSource as Button;
            if (b == null) return;
            string action = b.Tag as string;
            if (action == null) return;                  // 滚动条的上下翻页按钮也会冒泡进来
            AiProfileRow row = b.DataContext as AiProfileRow;
            if (row == null) return;
            e.Handled = true;
            if (action == "activate") ActivateProfile(row.Id);
            else if (action == "delete") RemoveProfile(row.Id);
        }

#if DIAG
        internal int CountForTest { get { return profiles == null ? 0 : profiles.Count; } }
        internal int ActiveForTest { get { return active; } }
        internal string EditingNameForTest
        {
            get { return editing >= 0 && editing < profiles.Count ? profiles[editing].Title : ""; }
        }
        internal string FieldNameForTest { get { return fName.Text; } }
        internal void SkipConfirmForTest() { skipConfirmForTest = true; }
        internal void AddForTest() { AddProfile(); }
        internal void ClearForTest() { ClearProfile(); }

        // 模拟点击行内按钮：走真实的冒泡路径（ListBox 的 AddHandler），验证 Id→位置换算
        internal bool ClickRowForTest(int pos, string action)
        {
            if (list == null || pos < 0 || pos >= list.Items.Count) return false;
            list.UpdateLayout();
            ListBoxItem item = list.ItemContainerGenerator.ContainerFromIndex(pos) as ListBoxItem;
            if (item == null)
            {
                list.ScrollIntoView(list.Items[pos]);
                list.UpdateLayout();
                item = list.ItemContainerGenerator.ContainerFromIndex(pos) as ListBoxItem;
            }
            if (item == null) return false;
            item.ApplyTemplate();
            Button b = FindRowButton(item, action);
            if (b == null) return false;
            b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            list.UpdateLayout();
            return true;
        }

        private static Button FindRowButton(DependencyObject root, string action)
        {
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                DependencyObject c = VisualTreeHelper.GetChild(root, i);
                Button b = c as Button;
                if (b != null && (b.Tag as string) == action) return b;
                Button deep = FindRowButton(c, action);
                if (deep != null) return deep;
            }
            return null;
        }
#endif

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
            if (usageUnavailable) return "用量统计：暂不可用";
            AiUsageStore.Row u;
            if (!usageRows.TryGetValue(index, out u)) return "累计消耗：暂无记录";
            if (u.InTokens <= 0 && u.OutTokens <= 0 && u.Calls <= 0) return "累计消耗：暂无记录";
            return "总输入：" + ShortNum(u.InTokens) + " · 总输出：" + ShortNum(u.OutTokens);
        }

        private string FormatStatsTip(int index)
        {
            if (usageUnavailable) return "读取用量数据库失败，本次显示的值不代表真实消耗";
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
            if (!AiConfig.SaveAll(profiles, active, 1200))
            {
                string why = string.IsNullOrEmpty(c.LastError) ? AppDb.LastError : c.LastError;
                status.Text = "保存失败：" + why +
                              "\n数据库位于：" + AppDb.FilePath + "，请确认该位置可写。";
                statusHost.Visibility = Visibility.Visible;
                return;
            }

            bool reset = IdentityChanged(c);
            // SaveAll 会写整张表：其它配置在切换列表项时已经被 CommitFields 提交，
            // 它们的地址/模型/Key 变化也必须清用量，不能只清当前正在编辑的这一套。
            // 合并成一次开库：逐套 Clear 各等一次 500ms busy_timeout，UI 线程会明显卡顿
            List<int> resetIds = new List<int>();
            foreach (AiConfig p in profiles)
                if (IdentityChanged(p)) resetIds.Add(p.Index);
            AiUsageStore.ClearMany(resetIds);
            // 整表快照重取：只更新单项会让 Count 与 original 脱节，Dirty() 之后一直为真
            TakeSnapshot();
            Saved = true;                       // 已经真实写盘，主窗口应提示「已更新」
            Action doneOne = SavedChanged;
            if (doneOne != null) { try { doneOne(); } catch { } }
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

        private bool SaveAll()
        {
            if (BlockedByLoadError()) return false;
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
                        return false;
                    }
                }
            }

            List<int> before = new List<int>(original.Keys);

            if (!AiConfig.SaveAll(profiles, active, 1200))
            {
                string why = (profiles.Count > 0 && !string.IsNullOrEmpty(profiles[0].LastError))
                             ? profiles[0].LastError : AppDb.LastError;
                status.Text = "保存失败：" + why +
                              "\n数据库位于：" + AppDb.FilePath + "，请确认该位置可写。";
                statusHost.Visibility = Visibility.Visible;
                return false;
            }

            // 被删掉的配置要先清：它的 Index 会被「新增」复用。
            // 删除项与身份变更合并成一次开库，避免 UI 线程逐套等待 busy_timeout
            List<int> clearIds = new List<int>();
            foreach (int k in before)
            {
                bool still = false;
                foreach (AiConfig c in profiles) if (c.Index == k) still = true;
                if (!still) clearIds.Add(k);
            }
            // 地址/模型/Key 改过的配置清零，其余继续累计
            foreach (AiConfig c in profiles) if (IdentityChanged(c)) clearIds.Add(c.Index);
            AiUsageStore.ClearMany(clearIds);

            Saved = true;
            Action done = SavedChanged;
            if (done != null) { try { done(); } catch { } }
            return true;
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

    // 主窗口的设置页：配置 AI（导入导出页在 PCTools.Transfer.cs 中接入）
    public partial class MainWindow
    {
        private const int SettingsNone = 0;
        private const int SettingsAi = 1;
        private int settingsMode = SettingsNone;
        private bool suppressNav;          // 程序设置 nav.SelectedIndex 时抑制 SelectionChanged
        private AiSettingsView aiView;
        private Grid settingsHost;
        private ContentControl settingsContent;
        private Grid commandBar;
        private Border listHost;
        private Grid searchHost;

        private void InitSettingsPages()
        {
            settingsHost = Find<Grid>("SettingsHost");
            settingsContent = Find<ContentControl>("SettingsContent");
            commandBar = Find<Grid>("CommandBar");
            listHost = Find<Border>("ListHost");
            searchHost = Find<Grid>("SearchHost");
        }

        private void ShowAiSettingsPage()
        {
            if (settingsMode == SettingsAi) return;
            if (!ConfirmLeaveSettings()) return;
            ExitSettingsContent();

            aiView = new AiSettingsView(null, true);
            aiView.SavedChanged += delegate { SetStatus("AI 配置已更新"); };
            settingsContent.Content = aiView;
            settingsMode = SettingsAi;

            // 设置页没有对应的列表项：清掉导航选中态，避免看起来仍停在某一页
            suppressNav = true;
            nav.SelectedIndex = -1;
            suppressNav = false;

            searchDefer.Stop();
            Mouse.OverrideCursor = null;
            commandBar.Visibility = Visibility.Collapsed;
            listHost.Visibility = Visibility.Collapsed;
            settingsHost.Visibility = Visibility.Visible;
            searchHost.Visibility = Visibility.Hidden;   // Hidden 保持标题栏三段布局不跳
            SyncSettingsNav();
            UpdateToolbarState();
        }

        // 回到四个列表页：由 SwitchPage 调用（未保存确认已在导航事件里做过）
        private void LeaveSettingsToList()
        {
            if (settingsMode == SettingsNone) return;
            settingsMode = SettingsNone;
            settingsHost.Visibility = Visibility.Collapsed;
            commandBar.Visibility = Visibility.Visible;
            listHost.Visibility = Visibility.Visible;
            searchHost.Visibility = Visibility.Visible;
            ExitSettingsContent();
            SyncSettingsNav();
            // 进设置页时 UpdateToolbarState 把备份栏与工具按钮禁用了；这里必须复位，
            // 否则返回列表后要等一次行选中才会恢复，表现为「复选框有时候点不动」
            UpdateToolbarState();
        }

        private void ExitSettingsContent()
        {
            if (aiView != null)
            {
                AiSettingsView old = aiView;
                aiView = null;
                try { old.Abort(); } catch { }
            }
            transferView = null;   // 页面被移除后不再保留引用，后台回调由 disposed 自行放弃
            if (settingsContent != null) settingsContent.Content = null;
        }

        private bool ConfirmLeaveSettings()
        {
            if (aiView != null && aiView.IsDirty)
                return Dialogs.Confirm(this,
                    "「配置 AI」有未保存的修改，切换页面会放弃这些修改，确定继续吗？",
                    "放弃修改");
            return true;
        }

        private void SyncSettingsNav()
        {
            Button ai = Find<Button>("BtnAiConfig");
            if (ai != null) ai.Tag = settingsMode == SettingsAi ? "on" : null;
            Button transfer = Find<Button>("BtnTransfer");
            if (transfer != null) transfer.Tag = settingsMode == SettingsTransfer ? "on" : null;
        }

#if DIAG
        public void OpenSettingsForTest(int mode)
        {
            if (mode == SettingsAi) ShowAiSettingsPage();
            else ShowTransferPage();
        }

        public string SettingsStateForTest()
        {
            return "mode=" + settingsMode + " host=" + settingsHost.Visibility +
                   " list=" + listHost.Visibility + " search=" + searchHost.Visibility;
        }
#endif
    }
}
