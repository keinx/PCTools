// AI 层：Key 加密、配置、HTTP 客户端、JSON 解析与批量提示词
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

        internal static string Unprotect(string stored, out bool unreadable)
        {
            unreadable = false;
            if (string.IsNullOrEmpty(stored)) return "";
            if (!stored.StartsWith(Prefix, StringComparison.Ordinal)) return "";   // 非当前格式，不再兼容
            try
            {
                byte[] enc = Convert.FromBase64String(stored.Substring(Prefix.Length));
                byte[] raw = ProtectedData.Unprotect(enc, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(raw);
            }
            catch
            {
                // 只有「确实是 dp: 密文但解不开」才标记 unreadable；保存时要原样写回，
                // 否则一份仍可恢复的密文会被空串覆盖
                unreadable = true;
                return "";
            }
        }
    }

    // ------------------------------------------------------------------ AI
    // AI 接口配置：最多 10 套，同一时间只有一套生效（active）
    internal class AiConfig
    {
        internal const int MaxProfiles = 10;

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
                        bool unreadable;
                        c.ApiKey = Secret.Unprotect(p[4], out unreadable);   // 只认 dp: 密文；解不开按空处理
                        // 只有 dp: 密文解不开才保留原值（换账户 / 换机器 / 数据损坏）。
                        // 非 dp: 内容是无效格式（历史明文等），按空处理并在下次保存时清掉
                        c.KeyUnreadable = unreadable;
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
        // 当前生效配置的短缓存：Load 会在 UI 线程被调用（打开分析窗、AI 批量前），
        // 每次开库都要和后台写库抢锁。按文件时间戳 + 3 秒 TTL 失效，SaveAll 主动清空
        private static AiConfig cfgCache;
        private static long cfgCacheStamp;
        private static DateTime cfgCacheAt;
        private static readonly object cfgGate = new object();

        internal static AiConfig Load(out string readError)
        {
            readError = null;
            long stamp = 0;
            try { stamp = File.GetLastWriteTimeUtc(AppDb.FilePath).Ticks; }
            catch { }
            lock (cfgGate)
            {
                if (cfgCache != null && cfgCacheStamp == stamp &&
                    (DateTime.UtcNow - cfgCacheAt).TotalSeconds < 3) return cfgCache;
            }
            int active;
            List<AiConfig> list = LoadAll(out active, out readError);
            AiConfig hit = null;
            foreach (AiConfig c in list) if (c.Index == active) { hit = c; break; }
            if (hit == null) hit = list.Count > 0 ? list[0] : new AiConfig();
            if (readError == null)
            {
                lock (cfgGate) { cfgCache = hit; cfgCacheStamp = stamp; cfgCacheAt = DateTime.UtcNow; }
            }
            return hit;
        }

        // 整表重写：一次事务里清空 ai_config 再逐套写入，顺便清掉已删配置的累计用量
        internal static bool SaveAll(List<AiConfig> list, int active)
        {
            if (list == null) list = new List<AiConfig>();
            // 整表重写前先挡住非法输入：编号越界或重复会被 INSERT OR REPLACE 静默合并，
            // 写出 11 号行后 LoadAll 又会把它过滤掉，下一次保存就永久丢失该套配置
            if (list.Count > MaxProfiles)
            {
                Note(list, "配置套数超过上限 " + MaxProfiles + "，已取消保存");
                return false;
            }
            HashSet<int> seenIdx = new HashSet<int>();
            foreach (AiConfig c in list)
            {
                if (c == null)
                {
                    Note(list.Where(x => x != null).ToList(), "存在空的配置项，已取消保存");
                    return false;
                }
                if (c.Index < 1 || c.Index > MaxProfiles)
                {
                    Note(list, "配置编号 " + c.Index + " 超出 1.." + MaxProfiles + "，已取消保存");
                    return false;
                }
                if (!seenIdx.Add(c.Index))
                {
                    Note(list, "配置编号重复：" + c.Index + "，已取消保存");
                    return false;
                }
            }
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
            lock (cfgGate) { cfgCache = null; }
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

#if DIAG
        // 仅自检使用：连通性测试走两参简写
        internal static string Ask(AiConfig cfg, string userPrompt)
        {
            return AskReply(cfg, userPrompt, null, null, 90000).Text;
        }
#endif

        // onRequest：把 HttpWebRequest 暴露给调用方，窗口关闭时可以 Abort 掉在途请求
        internal static string Ask(AiConfig cfg, string userPrompt, Action<HttpWebRequest> onRequest)
        {
            return AskReply(cfg, userPrompt, SystemPrompt, onRequest, 90000).Text;
        }

#if DIAG
        // 仅自检使用：显式指定系统提示词与超时的长写法
        internal static string Ask(AiConfig cfg, string userPrompt, string systemPrompt,
                                   Action<HttpWebRequest> onRequest)
        {
            return AskReply(cfg, userPrompt, systemPrompt, onRequest, 90000).Text;
        }

        internal static string Ask(AiConfig cfg, string userPrompt, string systemPrompt,
                                   Action<HttpWebRequest> onRequest, int timeoutMs)
        {
            return AskReply(cfg, userPrompt, systemPrompt, onRequest, timeoutMs).Text;
        }
#endif

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
            req.UserAgent = "PCTools/" + AppInfo.Version;
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
            if (page == 3)
                return common + "标签只能是 启用 或 禁用（右键菜单项没有“手动”）。"
                    + "Shell 扩展、系统自带或安全相关菜单给启用；第三方软件注册的推广、更新器、遥测类菜单给禁用。";
            return common + "标签只能是 启用 或 禁用（计划任务没有“手动”）。"
                + "Windows 自带维护任务给启用；第三方更新器、遥测、推广任务给禁用。";
        }

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
            string typeName = page == 0 ? "开机启动项"
                            : (page == 1 ? "Windows 服务"
                            : (page == 2 ? "Windows 计划任务" : "资源管理器右键菜单项"));
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
                        name = Manager.StripInstanceTail(it.Name);
                        id = tpl;
                    }
                    name = ClipName(name, 44);
                    if (string.Equals(name, id, StringComparison.OrdinalIgnoreCase)) id = "-";
                    extra = Compact(it.Path);
                }
                else if (page == 3)
                {
                    CtxMenuItem it = (CtxMenuItem)items[i];
                    key = AiAdviceStore.KeyCtx(it);
                    name = ClipName(it.Name, 56);
                    id = string.IsNullOrEmpty(it.Scope) ? it.Kind : it.Kind + "·" + it.Scope;
                    string extraRaw = string.IsNullOrEmpty(it.Handler) ? it.Id : it.Id + " " + it.Handler;
                    extra = Compact(extraRaw);
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
                plan.Items[plan.Sent] = items[i];
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
            // 把 "!C:\…" 当成一条异常路径，与启动项页的既有口径也不一致
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
        // 输出「手动」，这种标签在页面上根本没有对应操作，不能原样入库展示。
        // discarded=模型回了内容但编号/标签不可用的数量；validSent=别名回填前真正拿到
        // 合法结论的已发送项数量；missing=按请求序号给出的逐项未取得结论原因。
        internal static Dictionary<string, string> Parse(string reply, BatchPlan plan, int page,
                                                         out int discarded, out int validSent,
                                                         out Dictionary<int, string> missing)
        {
            Dictionary<string, string> values = new Dictionary<string, string>();
            discarded = 0;
            validSent = 0;
            missing = new Dictionary<int, string>();
            Dictionary<int, string> bad = new Dictionary<int, string>();
            string[] lines = string.IsNullOrEmpty(reply)
                ? new string[0]
                : reply.Replace("\r", "").Split('\n');
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
                if (label.Length == 0)
                {
                    if (!bad.ContainsKey(no)) bad[no] = "标签无法解析";
                    discarded++; continue;
                }
                if (!AiAdviceStore.AdviceAllowed(page, label))
                {
                    if (!bad.ContainsKey(no)) bad[no] = "返回标签不可用";
                    discarded++; continue;
                }
                string key;
                if (!plan.Keys.TryGetValue(no, out key)) { discarded++; continue; }
                // 重复序号只能算一次已返回：否则 validSent 大于实际拿到结论的项数，
                // 缺口统计会把真正没返回的项目漏报
                if (!values.ContainsKey(key)) validSent++;
                values[key] = label;
            }
            foreach (KeyValuePair<int, string> kv in plan.Keys)
            {
                if (values.ContainsKey(kv.Value)) continue;
                string why;
                missing[kv.Key] = bad.TryGetValue(kv.Key, out why)
                    ? why
                    : "模型未返回该行（可能被截断）";
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
}
