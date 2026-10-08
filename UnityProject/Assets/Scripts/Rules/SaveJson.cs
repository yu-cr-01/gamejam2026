using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GameJam.Rules
{
    /// <summary>
    /// 存档用的**极小 JSON 读写器**（写给人看的那一份）。
    ///
    /// 【为什么不用 JsonUtility】
    ///   ① **离线探针也要走同一份实现**。Tools/RuleProbe 编的是 `Assets/Scripts/Rules/**`
    ///      这一层（见 Tools/RuleProbe/run.ps1 的源文件清单），而 JsonUtility 在
    ///      UnityEngine.JSONSerializeModule 里 —— 那条路引不到它。若游戏里用 JsonUtility、
    ///      离线用别的，就变成"两套存取实现"，而"离线全绿、实机不对"正是这么来的
    ///      （工程里已经踩过一次：卡面数值两套口径）。
    ///   ② **缺字段必须能报错**。JsonUtility 分不清「字段缺失」和「字段是 0」：
    ///      存档里少了 H，它会静默读成 H=0 —— 那正是这一版明令不许的"半读半不读"。
    ///      手写映射可以对每一个字段说清"缺了谁、在哪一层"。
    ///   ③ 只用到三种值（int / bool / string）和两种容器（对象 / 数组），
    ///      为这点需求引入反射式序列化不划算，而且写出来的字段顺序没法控制
    ///      （存档是给人看的，顺序得按"关卡 → 刀片 → 桌面 → 手牌"排）。
    ///
    /// 【它不认识任何业务字段】这一份只管 JSON 语法；"哪个字段叫什么、缺了怎么办"
    /// 全在 <see cref="LevelSaveJson"/> 里，那才是存档的 schema。
    /// </summary>
    public static class SaveJson
    {
        // ══════════════════════════════════════════════════════════════
        //  写
        // ══════════════════════════════════════════════════════════════

        /// <summary>缩进两个空格 —— 存档要能直接拿记事本打开读，不写紧凑格式。</summary>
        private const string Indent = "  ";

        /// <summary>
        /// 写 JSON。用法是"链式开合"：
        ///   w.Obj(); w.Put("version", 1); w.Arr("layers"); w.Obj(); … w.End(); w.End();
        /// 每个 Obj/Arr 都要有一个 End，配平由调用方保证（<see cref="LevelSaveJson.Write"/> 是唯一调用点）。
        /// </summary>
        public sealed class Writer
        {
            private readonly StringBuilder sb = new StringBuilder();
            private readonly List<bool> scopeHasItems = new List<bool>();   // 每个容器里已经有内容了吗
            private readonly List<bool> scopeIsArray = new List<bool>();    // 这个容器是数组吗（数组元素没有键）

            public string Text { get { return sb.ToString(); } }

            /// <summary>开一个对象（可以带键）。</summary>
            public Writer Obj(string key = null)
            {
                WriteKeyIfAny(key);
                sb.Append('{');
                Push(false);
                return this;
            }

            /// <summary>开一个数组（可以带键）。</summary>
            public Writer Arr(string key = null)
            {
                WriteKeyIfAny(key);
                sb.Append('[');
                Push(true);
                return this;
            }

            /// <summary>关掉当前容器（对象或数组）。</summary>
            public Writer End()
            {
                bool isArray = scopeIsArray[scopeIsArray.Count - 1];
                bool had = scopeHasItems[scopeHasItems.Count - 1];
                scopeIsArray.RemoveAt(scopeIsArray.Count - 1);
                scopeHasItems.RemoveAt(scopeHasItems.Count - 1);

                if (had) NewLineIndent();
                sb.Append(isArray ? ']' : '}');
                return this;
            }

            public Writer Put(string key, string value) { WriteKeyIfAny(key); WriteString(value); return this; }
            public Writer Put(string key, int value)    { WriteKeyIfAny(key); sb.Append(value.ToString(CultureInfo.InvariantCulture)); return this; }
            public Writer Put(string key, bool value)   { WriteKeyIfAny(key); sb.Append(value ? "true" : "false"); return this; }

            // ── 内部 ──────────────────────────────────────────────────

            private void Push(bool isArray)
            {
                scopeIsArray.Add(isArray);
                scopeHasItems.Add(false);
            }

            private void WriteKeyIfAny(string key)
            {
                if (scopeHasItems.Count > 0 && scopeHasItems[scopeHasItems.Count - 1]) sb.Append(',');
                if (scopeHasItems.Count > 0) scopeHasItems[scopeHasItems.Count - 1] = true;

                if (scopeIsArray.Count > 0 && scopeIsArray[scopeIsArray.Count - 1])
                {
                    // 数组元素：换行 + 缩进，不写键
                    NewLineIndent();
                    return;
                }

                if (key != null)
                {
                    NewLineIndent();
                    WriteString(key);
                    sb.Append(": ");
                }
            }

            private void NewLineIndent()
            {
                sb.Append('\n');
                for (int i = 0; i < scopeHasItems.Count; i++) sb.Append(Indent);
            }

            private void WriteString(string s)
            {
                // null 一律写成空串：存档里出现 null 只会给"读回来"添一条没必要的分支
                if (s == null) s = "";

                sb.Append('"');
                for (int i = 0; i < s.Length; i++)
                {
                    char c = s[i];
                    switch (c)
                    {
                        case '"':  sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\b': sb.Append("\\b");  break;
                        case '\f': sb.Append("\\f");  break;
                        case '\n': sb.Append("\\n");  break;
                        case '\r': sb.Append("\\r");  break;
                        case '\t': sb.Append("\\t");  break;
                        default:
                            // 中文**原样写出去**（存档要人能看懂）；只转义控制字符
                            if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                            else         sb.Append(c);
                            break;
                    }
                }
                sb.Append('"');
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  读
        //
        //  产出的是"通用值"：Dictionary<string,object> / List<object> / string / double / bool / null。
        //  映射成存档 DTO 是 LevelSaveJson 的事 —— 那一层才知道哪个字段是必需的。
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 解析一段 JSON 文本。**任何语法问题都返回 false + 一句带位置的原因**，
        /// 绝不"尽力猜"—— 存档读坏了必须当场停下（见 LevelSaveJson.Read 的说明）。
        /// </summary>
        public static bool TryParse(string text, out Dictionary<string, object> root, out string error)
        {
            root = null;
            error = "";

            if (string.IsNullOrEmpty(text)) { error = "文件是空的"; return false; }

            int i = 0;
            try
            {
                SkipWs(text, ref i);
                object v = ParseValue(text, ref i, 0);
                SkipWs(text, ref i);

                if (i < text.Length) { error = Error(text, i, "末尾还有多余的内容"); return false; }

                root = v as Dictionary<string, object>;
                if (root == null) { error = "最外层不是一个 JSON 对象"; return false; }
                return true;
            }
            catch (FormatException e)
            {
                error = e.Message;
                return false;
            }
        }

        /// <summary>最大嵌套深度 —— 坏文件（几万个 '['）不该把栈打爆。</summary>
        private const int MaxDepth = 32;

        private static object ParseValue(string t, ref int i, int depth)
        {
            if (depth > MaxDepth) throw new FormatException(Error(t, i, "嵌套太深"));

            SkipWs(t, ref i);
            if (i >= t.Length) throw new FormatException(Error(t, i, "值还没写完就到文件末尾了"));

            char c = t[i];

            if (c == '{') return ParseObject(t, ref i, depth + 1);
            if (c == '[') return ParseArray(t, ref i, depth + 1);
            if (c == '"') return ParseString(t, ref i);
            if (c == 't') { Expect(t, ref i, "true");  return true; }
            if (c == 'f') { Expect(t, ref i, "false"); return false; }
            if (c == 'n') { Expect(t, ref i, "null");  return null; }

            return ParseNumber(t, ref i);
        }

        private static Dictionary<string, object> ParseObject(string t, ref int i, int depth)
        {
            Dictionary<string, object> o = new Dictionary<string, object>();
            i++;                                   // '{'
            SkipWs(t, ref i);

            if (i < t.Length && t[i] == '}') { i++; return o; }

            while (true)
            {
                SkipWs(t, ref i);
                if (i >= t.Length || t[i] != '"')
                    throw new FormatException(Error(t, i, "对象的键必须是字符串"));

                string key = ParseString(t, ref i);

                SkipWs(t, ref i);
                if (i >= t.Length || t[i] != ':') throw new FormatException(Error(t, i, "键「" + key + "」后面少了冒号"));
                i++;

                object v = ParseValue(t, ref i, depth);
                o[key] = v;

                SkipWs(t, ref i);
                if (i >= t.Length) throw new FormatException(Error(t, i, "对象没有收尾的 '}'"));

                if (t[i] == ',') { i++; continue; }
                if (t[i] == '}') { i++; return o; }

                throw new FormatException(Error(t, i, "对象里出现了既不是 ',' 也不是 '}' 的字符 '" + t[i] + "'"));
            }
        }

        private static List<object> ParseArray(string t, ref int i, int depth)
        {
            List<object> a = new List<object>();
            i++;                                   // '['
            SkipWs(t, ref i);

            if (i < t.Length && t[i] == ']') { i++; return a; }

            while (true)
            {
                a.Add(ParseValue(t, ref i, depth));

                SkipWs(t, ref i);
                if (i >= t.Length) throw new FormatException(Error(t, i, "数组没有收尾的 ']'"));

                if (t[i] == ',') { i++; continue; }
                if (t[i] == ']') { i++; return a; }

                throw new FormatException(Error(t, i, "数组里出现了既不是 ',' 也不是 ']' 的字符 '" + t[i] + "'"));
            }
        }

        private static string ParseString(string t, ref int i)
        {
            i++;                                   // 开头的引号
            StringBuilder sb = new StringBuilder();

            while (true)
            {
                if (i >= t.Length) throw new FormatException(Error(t, i, "字符串没有收尾的引号"));

                char c = t[i++];
                if (c == '"') return sb.ToString();

                if (c != '\\') { sb.Append(c); continue; }

                if (i >= t.Length) throw new FormatException(Error(t, i, "转义符后面没有东西"));
                char e = t[i++];

                switch (e)
                {
                    case '"':  sb.Append('"');  break;
                    case '\\': sb.Append('\\'); break;
                    case '/':  sb.Append('/');  break;
                    case 'b':  sb.Append('\b'); break;
                    case 'f':  sb.Append('\f'); break;
                    case 'n':  sb.Append('\n'); break;
                    case 'r':  sb.Append('\r'); break;
                    case 't':  sb.Append('\t'); break;
                    case 'u':
                    {
                        if (i + 4 > t.Length) throw new FormatException(Error(t, i, "\\u 后面不足 4 位"));
                        string hex = t.Substring(i, 4);
                        int code;
                        if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                            throw new FormatException(Error(t, i, "\\u" + hex + " 不是合法的十六进制"));
                        sb.Append((char)code);
                        i += 4;
                        break;
                    }
                    default:
                        throw new FormatException(Error(t, i - 1, "认不出的转义 \\" + e));
                }
            }
        }

        private static double ParseNumber(string t, ref int i)
        {
            int start = i;
            while (i < t.Length && (char.IsDigit(t[i]) || t[i] == '-' || t[i] == '+' || t[i] == '.' ||
                                    t[i] == 'e' || t[i] == 'E'))
                i++;

            string s = t.Substring(start, i - start);
            double d;
            if (s.Length == 0 || !double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                throw new FormatException(Error(t, start, "这里应该是一个数字，实际是「" + Preview(t, start) + "」"));

            return d;
        }

        private static void Expect(string t, ref int i, string word)
        {
            if (i + word.Length > t.Length || t.Substring(i, word.Length) != word)
                throw new FormatException(Error(t, i, "这里应该是 " + word + "，实际是「" + Preview(t, i) + "」"));
            i += word.Length;
        }

        private static void SkipWs(string t, ref int i)
        {
            while (i < t.Length && (t[i] == ' ' || t[i] == '\t' || t[i] == '\n' || t[i] == '\r')) i++;
        }

        /// <summary>给坏文件报错用：位置 + 附近十几个字符。人要能拿它去记事本里找到那一行。</summary>
        private static string Error(string t, int i, string why)
        {
            int line = 1, col = 1;
            for (int k = 0; k < i && k < t.Length; k++)
            {
                if (t[k] == '\n') { line++; col = 1; }
                else col++;
            }
            return "JSON 第 " + line + " 行第 " + col + " 列：" + why + "（附近「" + Preview(t, i) + "」）";
        }

        private static string Preview(string t, int i)
        {
            if (i >= t.Length) return "（文件末尾）";
            int n = Math.Min(16, t.Length - i);
            return t.Substring(i, n).Replace("\n", "\\n").Replace("\r", "");
        }
    }
}
