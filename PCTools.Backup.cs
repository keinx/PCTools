// 备份基线：四个列表页的“备份状态 / 备份启动类型”列与覆盖式写入
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
    // 一次备份中的一行：Key 用于定位，Name/Value 用于展示与导出的可读性
    internal sealed class BackupRow
    {
        internal string Key;
        internal string Name;
        internal string Value;
        internal string Updated;
    }

    internal static class BackupStore
    {
        // 与 AI 建议/分析共用同一套 kind 字面量，避免第四份 page→kind 映射
        internal static string KindFor(int page)
        {
            return AiAnalysisStore.KindFor(page);
        }

        // 备份键必须能稳定定位到“被操作的那个对象”，不能用会随命令变化的组合键：
        // 启动项用 机器+审批路径+值名（区分 HKCU/HKLM、Run/RunOnce、启动文件夹），
        // 服务用服务名，任务用完整路径，右键菜单用 注册表路径+标识
        internal static string KeyFor(int page, object item)
        {
            if (item == null) return null;
            if (page == 0)
            {
                StartupItem it = item as StartupItem;
                if (it == null) return null;
                string scope = it.ApprovalSubPath;
                if (string.IsNullOrEmpty(scope)) scope = it.SourceRegPath;
                if (string.IsNullOrEmpty(scope)) scope = it.Location;
                string name = string.IsNullOrEmpty(it.ApprovalName) ? it.Name : it.ApprovalName;
                if (string.IsNullOrEmpty(name)) return null;
                return (it.Machine ? "M" : "U") + "|" + (scope == null ? "" : scope) + "|" + name;
            }
            if (page == 1)
            {
                ServiceItem it = item as ServiceItem;
                return it == null || string.IsNullOrEmpty(it.ServiceName) ? null : it.ServiceName;
            }
            if (page == 2)
            {
                TaskItem it = item as TaskItem;
                return it == null || string.IsNullOrEmpty(it.FullPath) ? null : it.FullPath;
            }
            CtxMenuItem ci = item as CtxMenuItem;
            if (ci == null || string.IsNullOrEmpty(ci.Id)) return null;
            return (ci.RegPath == null ? "" : ci.RegPath) + "|" + ci.Id;
        }

        internal static string NameFor(object item)
        {
            string name = Manager.ItemName(item);
            return name == "未知项目" ? "" : name;
        }

        // 服务页备份的是启动类型；其余三页备份的是状态
        internal static string ValueFor(int page, object item)
        {
            if (page == 1)
            {
                ServiceItem it = item as ServiceItem;
                return it == null ? "" : it.StartType;
            }
            if (page == 0)
            {
                StartupItem it = item as StartupItem;
                return it == null ? "" : it.Status;
            }
            if (page == 2)
            {
                TaskItem it = item as TaskItem;
                return it == null ? "" : it.BackupCurrent;
            }
            CtxMenuItem ci = item as CtxMenuItem;
            return ci == null ? "" : ci.Status;
        }

        // 读取某一页的备份基线。complete=false 表示读失败，调用方不能据此清空界面
        internal static Dictionary<string, string> Load(string kind, out bool complete)
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
            string err;
            bool done;
            List<BackupRow> rows = LoadRows(kind, out err, out done);
            complete = done;
            foreach (BackupRow r in rows) map[r.Key] = r.Value;
            return map;
        }

        internal static List<BackupRow> LoadRows(string kind, out string error, out bool complete)
        {
            List<BackupRow> rows = new List<BackupRow>();
            error = null;
            complete = false;
            if (string.IsNullOrEmpty(kind)) { complete = true; return rows; }
            try
            {
                if (!AppDb.Ensure()) { error = AppDb.LastError; return rows; }
                // 由后台枚举线程调用：短超时，避免和 AI 批量写库抢锁时长期阻塞
                IntPtr db = Sqlite.Open(AppDb.FilePath, out error, 500);
                if (db == IntPtr.Zero) return rows;
                try
                {
                    List<string[]> data = Sqlite.Rows(db,
                        "SELECT item_key, IFNULL(name,''), IFNULL(value,''), IFNULL(updated,'') " +
                        "FROM backup_state WHERE kind=" + Sqlite.Q(kind) + " ORDER BY item_key;", out error);
                    if (error != null) return rows;
                    foreach (string[] p in data)
                    {
                        if (p.Length < 4 || string.IsNullOrEmpty(p[0])) continue;
                        BackupRow r = new BackupRow();
                        r.Key = p[0];
                        r.Name = p[1];
                        r.Value = p[2];
                        r.Updated = p[3];
                        rows.Add(r);
                    }
                    complete = true;
                }
                finally { Sqlite.Close(db); }
            }
            catch (Exception ex) { error = ex.Message; }
            return rows;
        }

        // 覆盖式写入：先删该页全部旧基线，再插入本次快照（单事务，失败回滚保留旧数据）
        internal static bool Replace(string kind, IList<BackupRow> rows, out string error)
        {
            error = null;
            try
            {
                if (!AppDb.Ensure()) { error = AppDb.LastError; return false; }
                IntPtr db = Sqlite.Open(AppDb.FilePath, out error, 3000);
                if (db == IntPtr.Zero) return false;
                try
                {
                    string now = Sqlite.Q(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    StringBuilder sb = new StringBuilder();
                    sb.Append("BEGIN;");
                    sb.Append("DELETE FROM backup_state WHERE kind=").Append(Sqlite.Q(kind)).Append(';');
                    if (rows != null)
                    {
                        foreach (BackupRow r in rows)
                        {
                            if (r == null || string.IsNullOrEmpty(r.Key)) continue;
                            sb.Append("INSERT OR REPLACE INTO backup_state (kind,item_key,name,value,updated) VALUES (")
                              .Append(Sqlite.Q(kind)).Append(',')
                              .Append(Sqlite.Q(r.Key)).Append(',')
                              .Append(Sqlite.Q(r.Name == null ? "" : r.Name)).Append(',')
                              .Append(Sqlite.Q(r.Value == null ? "" : r.Value)).Append(',')
                              .Append(now).Append(");");
                        }
                    }
                    sb.Append("COMMIT;");
                    string e = Sqlite.Exec(db, sb.ToString());
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
            catch (Exception ex)
            {
                error = ex.Message;
                DiagLog.Swallow("写入备份基线", ex);
                return false;
            }
        }

        internal static int Count(string kind)
        {
            try
            {
                if (!AppDb.Ensure()) return 0;
                string err;
                IntPtr db = Sqlite.Open(AppDb.FilePath, out err, 500);
                if (db == IntPtr.Zero) return 0;
                try
                {
                    List<string> rows = Sqlite.Column(db,
                        "SELECT COUNT(*) FROM backup_state WHERE kind=" + Sqlite.Q(kind) + ";", out err);
                    int n;
                    return rows.Count > 0 && int.TryParse(rows[0], out n) ? n : 0;
                }
                finally { Sqlite.Close(db); }
            }
            catch { return 0; }
        }

        // ---------------- 枚举后回填 ----------------
        internal static void FillStartup(List<StartupItem> list)
        {
            bool complete;
            Dictionary<string, string> map = Load(KindFor(0), out complete);
            ApplyMap(0, list, map);
        }

        internal static void FillServices(List<ServiceItem> list)
        {
            bool complete;
            Dictionary<string, string> map = Load(KindFor(1), out complete);
            ApplyMap(1, list, map);
        }

        internal static void FillTasks(List<TaskItem> list)
        {
            bool complete;
            Dictionary<string, string> map = Load(KindFor(2), out complete);
            ApplyMap(2, list, map);
        }

        internal static void FillCtx(List<CtxMenuItem> list)
        {
            bool complete;
            Dictionary<string, string> map = Load(KindFor(3), out complete);
            ApplyMap(3, list, map);
        }

        // 备份成功后用内存快照回填：不再开库，也不重新枚举 WMI/COM
        internal static void ApplyTo(int page, IList items, IList<BackupRow> rows)
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (rows != null)
                foreach (BackupRow r in rows)
                    if (r != null && !string.IsNullOrEmpty(r.Key)) map[r.Key] = r.Value;
            ApplyMap(page, items, map);
        }

        private static void ApplyMap(int page, IList items, Dictionary<string, string> map)
        {
            ApplyMap(page, items, map, null);
        }

        private static void ApplyMap(int page, IList items, Dictionary<string, string> map,
                                     Func<string, string> templateOf)
        {
            if (items == null) return;
            // 惰性：全部精确命中时（备份成功后的回填路径）不建索引、不读注册表
            Dictionary<string, string> byBase = null;
            foreach (object item in items)
            {
                IBackupItem it = item as IBackupItem;
                if (it == null) continue;
                string value = null;
                bool known = TryResolve(page, item, map, ref byBase, templateOf, out value) &&
                             !string.IsNullOrEmpty(value);
                if (known && page == 2) value = NormalizeTaskValue(value);
                it.BackupText = known ? value : "未备份";
                it.BackupRank = known ? RankFor(page, value) : 99;
                Paint(it, page);
            }
        }

        // 精确键 → 模板名 → 基名索引。只有确证是每用户服务实例（注册表 Type & 0x40）才兜底，
        // 第三方恰好叫 Foo_1a2b 的服务不会被误折叠
        private static bool TryResolve(int page, object item, Dictionary<string, string> map,
                                       ref Dictionary<string, string> byBase,
                                       Func<string, string> templateOf, out string value)
        {
            value = null;
            if (map == null) return false;
            string key = KeyFor(page, item);
            if (key != null && map.TryGetValue(key, out value)) return true;
            if (page != 1) return false;
            ServiceItem si = item as ServiceItem;
            if (si == null || string.IsNullOrEmpty(si.ServiceName)) return false;
            string tpl = templateOf == null ? Manager.TemplateOf(si.ServiceName)
                                            : templateOf(si.ServiceName);
            if (string.IsNullOrEmpty(tpl)) return false;
            if (map.TryGetValue(tpl, out value)) return true;
            if (byBase == null) byBase = BuildBaseMap(map);
            return byBase.TryGetValue(tpl, out value);
        }

        // 旧基线里同一模板可能存了任一会话的实例键，先到先得；值冲突留痕便于排查
        private static Dictionary<string, string> BuildBaseMap(Dictionary<string, string> map)
        {
            Dictionary<string, string> d =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> kv in map)
            {
                if (string.IsNullOrEmpty(kv.Value)) continue;
                string b = Manager.HexSuffixBase(kv.Key);
                if (b == null) continue;
                if (d.ContainsKey(b))
                {
                    if (d[b] != kv.Value) DiagLog.Note("备份基线同一模板存在多个值：" + b);
                    continue;
                }
                d[b] = kv.Value;
            }
            return d;
        }

        // 早期基线存的是任务的运行态（就绪/正在运行/排队中）。运行态不是启用状态，
        // 读回时统一归一到「已启用」，否则升级后所有运行中的任务都会显示不一致
        internal static string NormalizeTaskValue(string value)
        {
            if (value == "已启用" || value == "已禁用") return value;
            if (value == "就绪" || value == "正在运行" || value == "排队中") return "已启用";
            return value;
        }

        private static int RankFor(int page, string value)
        {
            if (string.IsNullOrEmpty(value) || value == "未备份") return 99;
            return page == 1 ? Rank.OfStartType(value) : Rank.OfStatus(value);
        }

        // ---------------- 主题重绘 ----------------
        // 备份列按「备份值本身」着色，与相邻的状态列保持同一套语义色：
        // 已禁用红、已启用绿、自动蓝、一次性蓝、第三方接管橙、未备份灰。
        // 差异不再用橙色整格表示——那会让「已禁用」也显示成绿色以外的杂色，
        // 与状态列对不上；差异改由左上角「只看不一致」筛选呈现
        internal static void PaintStartup(StartupItem it) { Paint((IBackupItem)it, 0); }
        internal static void PaintService(ServiceItem it) { Paint((IBackupItem)it, 1); }
        internal static void PaintTask(TaskItem it) { Paint((IBackupItem)it, 2); }
        internal static void PaintCtx(CtxMenuItem it) { Paint((IBackupItem)it, 3); }

        private static void Paint(IBackupItem it, int page)
        {
            if (it == null) return;
            string v = it.BackupText;
            if (string.IsNullOrEmpty(v) || v == "未备份")
            {
                it.BackupBrush = Palette.Gray;
                it.BackupBgBrush = Palette.GrayBg;
                return;
            }
            if (page == 1)
            {
                // 与 Manager.PaintStartType 完全一致：自动蓝、手动中性、已禁用红
                if (v == "已禁用") { it.BackupBrush = Palette.Red; it.BackupBgBrush = Palette.RedBg; }
                else if (v == "自动" || v == "自动（延迟）") { it.BackupBrush = Palette.Blue; it.BackupBgBrush = Palette.BlueBg; }
                else if (v == "手动") { it.BackupBrush = Palette.Neutral; it.BackupBgBrush = Palette.GrayBg; }
                else { it.BackupBrush = Palette.Gray; it.BackupBgBrush = Palette.GrayBg; }
                return;
            }
            if (page == 0 && v == "一次性") { it.BackupBrush = Palette.Blue; it.BackupBgBrush = Palette.BlueBg; }
            else if (page == 3 && v == "第三方接管") { it.BackupBrush = Palette.Orange; it.BackupBgBrush = Palette.OrangeBg; }
            else if (v == "已禁用") { it.BackupBrush = Palette.Red; it.BackupBgBrush = Palette.RedBg; }
            else if (v == "已启用") { it.BackupBrush = Palette.Green; it.BackupBgBrush = Palette.GreenBg; }
            else { it.BackupBrush = Palette.Gray; it.BackupBgBrush = Palette.GrayBg; }
        }

#if DIAG
        // 自检专用：注入模板解析器，正/反用例不依赖本机是否装了每用户服务
        internal static void ApplyMapForTest(int page, IList items,
            Dictionary<string, string> map, Func<string, string> templateOf)
        {
            ApplyMap(page, items, map, templateOf);
        }
#endif
    }

    // 列表左上“只看不一致”与右上“备份状态/备份启动类型”按钮
    public partial class MainWindow
    {
        private Button btnBackup;
        private CheckBox chkBackupOnly;
        private bool syncBackupChk;      // 程序设置 IsChecked 时抑制事件
        private bool backupBusy;
        private readonly bool[] backupOnly = new bool[4];   // 每页独立

        private void InitBackupBar()
        {
            btnBackup = Find<Button>("BtnBackup");
            chkBackupOnly = Find<CheckBox>("ChkBackupOnly");
            btnBackup.Click += delegate { RunBackup(current); };
            chkBackupOnly.Checked += delegate
            {
                if (syncBackupChk) return;
                backupOnly[current] = true;
                ApplyFilter();
            };
            chkBackupOnly.Unchecked += delegate
            {
                if (syncBackupChk) return;
                backupOnly[current] = false;
                ApplyFilter();
            };
            SyncBackupBar();
        }

        // 四页共用一套控件：切页时只换文案与勾选状态
        private void SyncBackupBar()
        {
            if (btnBackup == null || chkBackupOnly == null) return;
            bool service = current == 1;
            syncBackupChk = true;
            chkBackupOnly.Content = service ? "只显示与备份启动类型不一致的项目"
                                            : "只显示与备份状态不一致的项目";
            chkBackupOnly.IsChecked = backupOnly[current];
            btnBackup.Content = service ? "备份启动类型" : "备份状态";
            btnBackup.ToolTip = service
                ? "把当前页全部服务的启动类型保存为对比基线（覆盖旧基线，不受搜索筛选影响）"
                : "把当前页全部项目的状态保存为对比基线（覆盖旧基线，不受搜索筛选影响）";
            syncBackupChk = false;
        }

        private void RunBackup(int page)
        {
            if (settingsMode != SettingsNone) return;
            if (loading || backupBusy || quitting || closed) return;
            if (!AppDb.Usable)
            {
                Dialogs.Info(this, "配置库不可用",
                    "无法写入备份基线：\n" + AppDb.FilePath + "\n\n" +
                    (string.IsNullOrEmpty(AppDb.LastError) ? "原因未知" : AppDb.LastError));
                return;
            }

            List<object> all;
            if (page == 0) all = startupData.Cast<object>().ToList();
            else if (page == 1) all = serviceData.Cast<object>().ToList();
            else if (page == 2) all = taskData.Cast<object>().ToList();
            else all = ctxData.Cast<object>().ToList();   // 右键菜单含全部页签
            if (all.Count == 0) { SetStatus("当前页没有可备份的项目"); return; }

            string what = page == 1 ? "启动类型" : "状态";
            if (!Dialogs.Confirm(this,
                "将用当前页全部 " + all.Count + " 项的" + what + "覆盖已有备份基线。\n\n" +
                "本次备份不受搜索和筛选影响；原有备份值会被整体替换。",
                "覆盖备份")) return;

            List<BackupRow> rows = new List<BackupRow>(all.Count);
            foreach (object o in all)
            {
                string key = BackupStore.KeyFor(page, o);
                if (string.IsNullOrEmpty(key)) continue;
                BackupRow r = new BackupRow();
                r.Key = key;
                r.Name = BackupStore.NameFor(o);
                r.Value = BackupStore.ValueFor(page, o);
                rows.Add(r);
            }
            if (rows.Count == 0) { SetStatus("当前页没有可备份的项目"); return; }

            string kind = BackupStore.KindFor(page);
            backupBusy = true;
            UpdateToolbarState();
            SetStatus("正在备份" + what + "（共 " + rows.Count + " 项）…");

            Thread t = new Thread(delegate()
            {
                string err;
                bool ok = BackupStore.Replace(kind, rows, out err);
                Ui(delegate
                {
                    backupBusy = false;
                    UpdateToolbarState();
                    if (quitting || closed) return;
                    if (!ok)
                    {
                        SetStatus("备份失败：" + err);
                        Dialogs.Info(this, "备份失败", string.IsNullOrEmpty(err) ? "原因未知" : err);
                        return;
                    }
                    BackupStore.ApplyTo(page, all, rows);
                    RepaintItems();
                    if (current == page) ApplyFilter();
                    SetStatus("已备份 " + rows.Count + " 项" + what + "（原备份已覆盖）");
                });
            });
            t.IsBackground = true;
            t.Start();
        }

#if DIAG
        // 自检专用：同步备份一页并返回错误（null=成功）
        public string BackupForTest(int page)
        {
            if (!AppDb.Usable) return "配置库不可用";
            List<object> all;
            if (page == 0) all = startupData.Cast<object>().ToList();
            else if (page == 1) all = serviceData.Cast<object>().ToList();
            else if (page == 2) all = taskData.Cast<object>().ToList();
            else all = ctxData.Cast<object>().ToList();
            List<BackupRow> rows = new List<BackupRow>();
            foreach (object o in all)
            {
                string key = BackupStore.KeyFor(page, o);
                if (string.IsNullOrEmpty(key)) continue;
                BackupRow r = new BackupRow();
                r.Key = key;
                r.Name = BackupStore.NameFor(o);
                r.Value = BackupStore.ValueFor(page, o);
                rows.Add(r);
            }
            string err;
            if (!BackupStore.Replace(BackupStore.KindFor(page), rows, out err)) return err;
            BackupStore.ApplyTo(page, all, rows);
            RepaintItems();
            ApplyFilter();
            return null;
        }

        public int SetBackupOnlyForTest(int page, bool value)
        {
            backupOnly[page] = value;
            if (current != page) return grids[page].Items.Count;
            syncBackupChk = true;
            chkBackupOnly.IsChecked = value;
            syncBackupChk = false;
            ApplyFilter();
            return grids[page].Items.Count;
        }

        public bool BackupOnlyEnabledForTest
        {
            get { return chkBackupOnly != null && chkBackupOnly.IsEnabled; }
        }

        public string BackupTextForTest(int page, int index)
        {
            if (index < 0 || index >= grids[page].Items.Count) return "";
            object o = grids[page].Items[index];
            IBackupItem it = o as IBackupItem;
            return it == null ? "" : it.BackupText;
        }

        public int BackupMissingForTest(int page)
        {
            int n = 0;
            foreach (object o in grids[page].Items)
            {
                IBackupItem it = o as IBackupItem;
                if (it != null && (string.IsNullOrEmpty(it.BackupText) || it.BackupText == "未备份")) n++;
            }
            return n;
        }
#endif
    }
}
