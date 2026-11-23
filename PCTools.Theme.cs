// 主题：浅色/深色资源字典构建与偏好读写
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
                // 主题切换在 UI 线程上执行：短超时，不能因为后台写库把界面冻住
                IntPtr db = Sqlite.Open(AppDb.FilePath, out err, 500);
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
                Put(d, "HoverBg", "#E2E8F0", 0.95);
                Put(d, "PressBg", "#DCE3EC", 0.95);
                Put(d, "NavSelectedBg", "#E8EFF8", 0.98);
                Put(d, "SelectionBg", "#E8F1FC", 0.98);
                Put(d, "RowHoverBg", "#F4F6F9", 1);
                Put(d, "RowSelectedBg", "#E4EFFB", 1);
                Put(d, "ScrollThumb", "#BFC6CF", 1);
                Put(d, "ScrollThumbHover", "#9EA7B2", 1);
                Put(d, "WinBtnHover", "#E2E8F0", 1);
                Put(d, "WinBtnPress", "#D6DEE8", 1);
                Put(d, "CloseHover", "#C42B1C", 1);
                Put(d, "TextPrimary", "#1A1D21", 1);
                Put(d, "TextSecondary", "#6C737E", 1);
                Put(d, "Accent", "#0F6CBD", 1);
                Put(d, "SuccessFg", "#107C10", 1);
                Put(d, "SuccessBg", "#E8F6E8", 1);
                Put(d, "DangerFg", "#C42B1C", 1);
                Put(d, "BorderColor", "#E3E7ED", 1);
                Put(d, "CheckBorder", "#C2C9D3", 1);
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
                Put(d, "HoverBg", "#33383F", 0.95);
                Put(d, "PressBg", "#3A4046", 0.95);
                Put(d, "NavSelectedBg", "#2B3A4A", 0.98);
                Put(d, "SelectionBg", "#25384B", 0.98);
                Put(d, "RowHoverBg", "#2B2F35", 1);
                Put(d, "RowSelectedBg", "#25384B", 1);
                Put(d, "ScrollThumb", "#484F58", 1);
                Put(d, "ScrollThumbHover", "#606974", 1);
                Put(d, "WinBtnHover", "#343A42", 1);
                Put(d, "WinBtnPress", "#3D444C", 1);
                Put(d, "CloseHover", "#C42B1C", 1);
                Put(d, "TextPrimary", "#F2F4F7", 1);
                Put(d, "TextSecondary", "#A4ACB6", 1);
                Put(d, "Accent", "#63C8FF", 1);
                Put(d, "SuccessFg", "#6CCB5F", 1);
                Put(d, "SuccessBg", "#20301C", 1);
                Put(d, "DangerFg", "#FF99A4", 1);
                Put(d, "BorderColor", "#363B42", 1);
                Put(d, "CheckBorder", "#4B5159", 1);
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
}
