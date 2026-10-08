using System;
using System.IO;
using System.Text;
using GameJam.Rules;
using UnityEngine;

namespace GameJam.Prototype
{
    /// <summary>
    /// v2.1 的**存档落盘**：`Application.persistentDataPath` 下的一个 JSON（`save_v21.json`）。
    ///
    /// 【为什么只有一个存档位】
    ///   正文里每关是独立的（§八），玩家要的也只是"保存当前关卡状态、能接着打"。
    ///   多存档位会立刻带出"读哪一个 / 覆盖哪一个 / 怎么命名"一整套界面，
    ///   而这一版要的是"能存能读"。所以固定一个文件名，**存 = 覆盖，读 = 读它**；
    ///   玩家想手动管理，直接去那个目录删文件就行（路径在日志和暂停菜单里都写着）。
    ///
    /// 【为什么落成 JSON 而不是二进制】
    ///   ① 用户明确要求"人能看懂、能手动删"；
    ///   ② 出问题时，第一件要做的事就是**把存档打开看一眼** ——
    ///      二进制档只能靠再写一个工具去读它，那是另一个 bug 源。
    ///   格式本身由 <see cref="LevelSaveJson"/> 说了算（Rules 那一层，离线也能测）。
    ///
    /// 【容错口径】读档只有两种结果：**完整读出来**，或者**明确报错、什么都不动**
    ///   （见 <see cref="TryLoad"/>）。没有"读到一半"这种状态 ——
    ///   半读半不读会把玩家的局面搞坏，比"读不了"糟糕得多。
    /// </summary>
    public static class TableSaveIO
    {
        /// <summary>存档文件名。**一个存档位**，改它就是换存档位。</summary>
        public const string FileName = "save_v21.json";

        /// <summary>存档目录（Unity 的 persistentDataPath；日志里会打出来）。</summary>
        public static string Dir
        {
            get
            {
                try { return Application.persistentDataPath; }
                catch (Exception) { return System.IO.Path.GetTempPath(); }
            }
        }

        /// <summary>存档文件的完整路径。</summary>
        public static string Path { get { return System.IO.Path.Combine(Dir, FileName); } }

        /// <summary>存档在不在（不判断内容是否可读 —— 那个由 <see cref="TryLoad"/> 回答）。</summary>
        public static bool Exists
        {
            get
            {
                try { return File.Exists(Path); }
                catch (Exception) { return false; }
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  读
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 读盘 + 解析 + 校验。**任何一步不对都返回 false + 一句能照着查的原因**，
        /// <paramref name="file"/> 一定是 null（调用方据此"什么都不做"）。
        /// </summary>
        public static bool TryLoad(out SaveFileDto file, out string error)
        {
            file = null;
            error = "";

            string text;
            if (!ReadText(out text, out error)) return false;

            if (!LevelSaveJson.Read(text, out file, out error))
            {
                file = null;
                error = "存档文件解析失败：" + error;
                return false;
            }
            return true;
        }

        /// <summary>把存档原文读出来（反例探针要备份 / 改坏它，所以单独一个入口）。</summary>
        public static bool ReadText(out string text, out string error)
        {
            text = "";
            error = "";

            string path = Path;
            if (!Exists) { error = "没有存档文件（" + path + "）"; return false; }

            try
            {
                // ReadAllText 会自动识别并吃掉 BOM（文件被人用记事本另存过也不至于读不了）
                text = File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception e)
            {
                error = "读存档文件失败：" + e.GetType().Name + " " + e.Message + "（" + path + "）";
                return false;
            }

            if (string.IsNullOrEmpty(text)) { error = "存档文件是空的（" + path + "）"; return false; }
            return true;
        }

        // ══════════════════════════════════════════════════════════════
        //  写 / 删
        // ══════════════════════════════════════════════════════════════

        /// <summary>把存档写成 JSON 落盘（先写临时文件再改名：写到一半崩了不会毁掉上一份好档）。</summary>
        public static bool Write(SaveFileDto file, out string error)
        {
            error = "";
            if (file == null) { error = "没有可写的存档对象"; return false; }

            string json;
            try { json = LevelSaveJson.Write(file); }
            catch (Exception e)
            {
                error = "存档序列化失败：" + e.GetType().Name + " " + e.Message;
                return false;
            }

            return WriteText(json, out error);
        }

        /// <summary>写原文（反例探针恢复好档也走这里）。</summary>
        public static bool WriteText(string json, out string error)
        {
            error = "";
            if (string.IsNullOrEmpty(json)) { error = "要写的内容是空的"; return false; }

            string path = Path;
            string tmp  = path + ".tmp";

            try
            {
                string dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                // ★ 不带 BOM：带 BOM 的 JSON 有些解析器会当场拒收，
                //   而"存档被别的工具打开过"这件事迟早会发生，源头就别留坑。
                File.WriteAllText(tmp, json, new UTF8Encoding(false));

                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception e)
            {
                error = "写存档失败：" + e.GetType().Name + " " + e.Message + "（" + path + "）";
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { }
                return false;
            }

            return true;
        }

        /// <summary>
        /// 删掉存档（"重新开始本关"会调它 —— 见 <see cref="TableTurnLoop.RestartLevel"/> 的说明）。
        /// 文件本来就不在也算成功。
        /// </summary>
        public static bool Delete(out string error)
        {
            error = "";
            try
            {
                if (File.Exists(Path)) File.Delete(Path);
                cachedStamp = DateTime.MinValue;    // 摘要缓存作废
                return true;
            }
            catch (Exception e)
            {
                error = "删存档失败：" + e.GetType().Name + " " + e.Message + "（" + Path + "）";
                return false;
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  一行摘要（暂停菜单每帧都要显示它，所以带缓存）
        // ══════════════════════════════════════════════════════════════

        private static string cachedLine = "";
        private static DateTime cachedStamp = DateTime.MinValue;

        /// <summary>
        /// "存档位：save_v21.json（第 1 关 · 第 2 回合 · 12 分）" 或 "（还没有存档）"。
        ///
        /// 【为什么要缓存】IMGUI 每帧会调 OnGUI 好几趟（Layout / Repaint），
        ///   而这里要读盘 + 解析。缓存以**文件的最后写入时间**为键：
        ///   存档被改过（自己存 / 手动改坏）就会重算，没改过就一行字符串。
        /// </summary>
        public static string StatusLine()
        {
            if (!Exists)
            {
                cachedStamp = DateTime.MinValue;
                cachedLine = "存档位：" + FileName + "（还没有存档）";
                return cachedLine;
            }

            DateTime stamp;
            try { stamp = File.GetLastWriteTimeUtc(Path); }
            catch (Exception) { stamp = DateTime.MinValue; }

            if (cachedLine.Length > 0 && stamp == cachedStamp) return cachedLine;

            cachedStamp = stamp;

            SaveFileDto f;
            string error;
            if (!TryLoad(out f, out error)) cachedLine = "存档位：" + FileName + "（★ 读不出来：" + Trim(error, 40) + "）";
            else                            cachedLine = "存档位：" + FileName + "（" + f.ShortLine() + "）";

            return cachedLine;
        }

        /// <summary>把过长的报错截短（菜单那一行放不下整段 JSON 解析错误；全文在日志里）。</summary>
        private static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        /// <summary>存档路径的一行说明（日志里打，回报里也要写清落盘在哪）。</summary>
        public static string PathLine()
        {
            return "存档路径 " + Path + "（目录 " + Dir + "）";
        }
    }

    /// <summary>
    /// 读档时**先建好**的那一批新对象（规则侧 + 手牌壳）。
    ///
    /// 【为什么要有这个中间物】"读档失败 → 什么都不动"这条要求需要把
    ///   "解析 + 造对象"和"覆盖现有状态"彻底分开：
    ///   前者可能失败（卡表里没这张卡 / 层数缺项 / 存档缺字段），失败时现场必须原封不动；
    ///   后者不可能失败（只是赋值），所以放在 <see cref="TableRulesV21.CommitSaveState"/> 里一次做完。
    /// </summary>
    public sealed class BuiltSaveState
    {
        /// <summary>规则侧（刀片 / 桌面素材 / 计数 / 层数 / 被动记录）</summary>
        public LevelSave.BuiltState rules;

        /// <summary>手牌素材（壳 + 数值来源）</summary>
        public readonly System.Collections.Generic.List<MaterialCard> hand =
            new System.Collections.Generic.List<MaterialCard>();

        /// <summary>手牌法术</summary>
        public readonly System.Collections.Generic.List<SpellCard> handSpells =
            new System.Collections.Generic.List<SpellCard>();
    }
}
