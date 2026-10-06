using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using GameJam.Data;

namespace GameJam.Rules
{
    // ══════════════════════════════════════════════════════════════════
    //  一、词汇表 —— 中文规则里能认出来的每一种"动作"（op）
    //
    //  这一节是**策划文案和代码之间的合同**：
    //  策划写的句子只要落在这张表里，引擎就能跑；落在表外的一律进
    //  「未识别清单」（见文件末尾的 RuleReport），绝不静默失效。
    // ══════════════════════════════════════════════════════════════════

    /// <summary>操作（op）。每个 op 的字段含义见 <see cref="RuleAction"/>。</summary>
    public enum RuleOp
    {
        /// <summary>声明性句子（"无。"、"不响应热、冷、酸"、"可叠加"）：什么都不做，但**不算未识别**。</summary>
        None = 0,

        // ── 得分 ─────────────────────────────────────────────────────
        /// <summary>固定加分。字段：amount（分）。"一次性获得30分。"</summary>
        AddScore = 1,
        /// <summary>得分倍率。字段：amount（倍数，2 = 翻倍）。"本次启动得分翻倍。"</summary>
        MultiplyScore = 2,
        /// <summary>本次启动得分为 0。无字段。"本次启动得分为0。"</summary>
        ZeroScore = 3,
        /// <summary>每有 1 层加分。字段：layers（哪几类，逐个累加）、amount（每层多少分）。"每有1层冷/热/酸，获得1分。"</summary>
        ScorePerLayer = 4,
        /// <summary>每消耗 1 层加分。字段：layers、amount（每层多少分）；layers 缺省时取本句前面那次消耗的类别。</summary>
        ScorePerConsumedLayer = 5,
        /// <summary>按某类层数直接加分。字段：layers、amount（每层多少分，正文没给时按 1）。"根据当前刀片冷层数直接加分。"</summary>
        ScoreByLayerCount = 6,

        // ── 刀片 ─────────────────────────────────────────────────────
        /// <summary>本次启动不消耗刀片 H（标记）。无字段。"不消耗刀片H。"</summary>
        BladeNoConsumeH = 10,
        /// <summary>扣刀片 H。字段：amount 或 amountSource（等同于当前/已消耗的层数）、layers。</summary>
        BladeDamageH = 11,
        /// <summary>加刀片 H。字段：amount。"刀片H＋10"</summary>
        BladeHardenH = 12,
        /// <summary>额外消耗其他素材的 H。字段：amount（正文没给数值时按 1）。⚠ 当前模型里素材只有 D，这条只记日志。</summary>
        BladeDamageOtherH = 13,
        /// <summary>其他素材 D - N。字段：amount。"刀片每次启动，其他素材D-2。"</summary>
        OtherMaterialsDepleteD = 14,

        // ── 层数 ─────────────────────────────────────────────────────
        /// <summary>加层。字段：layers、amount、permanent（"不衰退"）。"刀片附带一层不衰退的冷。"</summary>
        AddLayer = 20,
        /// <summary>消耗 N 层。字段：layers、amount。"消耗1层热"</summary>
        ConsumeLayer = 21,
        /// <summary>消耗所有某类层数（两份计数都吃）。字段：layers。"消耗刀片所有热层数"</summary>
        ConsumeAllLayers = 22,
        /// <summary>本次启动不消耗该类层数（标记）。字段：layers。"不消耗热层数"</summary>
        NoConsumeLayer = 23,
        /// <summary>层数翻倍。字段：layers（缺省时取条件里的类别）。"使当前所有酸层数翻倍"</summary>
        DoubleLayers = 24,

        // ── 产出 ─────────────────────────────────────────────────────
        /// <summary>产出一张/多张具体的卡。字段：cardName、count、produce（素材 / 法术 / 空白卡 / 自动）。</summary>
        ProduceCard = 30,
        /// <summary>变为一张随机法术卡。字段：count。"变为一张随机法术卡"</summary>
        ProduceRandomSpell = 31,

        // ── 法术 / 附魔 ───────────────────────────────────────────────
        /// <summary>附魔到刀片（加该附魔类型的层）。字段：layers。"附魔到刀片，可叠加。"</summary>
        EnchantBlade = 40,
        /// <summary>降低附魔触发阈值。字段：amount（默认见 TurnRules.CatalystThresholdReduce）。"降低热/冷/酸附魔触发阈值。"</summary>
        LowerEnchantThreshold = 41,
        /// <summary>设置爆刀分数倍率。字段：amount。"如果通过此卡达成爆刀，爆刀产生的分数翻倍变为3倍。"</summary>
        SetBurstMultiplier = 42,
    }

    /// <summary>op 里那个数字从哪来。</summary>
    public enum RuleAmountSource
    {
        /// <summary>字面数字（amount 字段）。</summary>
        Fixed = 0,
        /// <summary>取"当前某类层数"—— 执行到这一条那一刻的值（所以同一句里先翻倍，这里读到的就是翻倍后的）。</summary>
        CurrentLayers = 1,
        /// <summary>取"本次已经消耗掉的某类层数"。</summary>
        ConsumedLayers = 2,
    }

    /// <summary>产出的卡算哪一类。</summary>
    public enum ProduceKind
    {
        /// <summary>先按素材表找，找不到再按法术表找。</summary>
        Auto = 0,
        Material = 1,
        Spell = 2,
        /// <summary>空白卡：卡表里没有这张卡，纯占位。</summary>
        Blank = 3,
    }

    /// <summary>条件种类。</summary>
    public enum RuleConditionKind
    {
        /// <summary>无条件（句子没有"若…"前缀）。</summary>
        Always = 0,
        /// <summary>某类层数 ≥ N。字段：layers、threshold。</summary>
        LayerAtLeast = 1,
        /// <summary>刀片有某类附魔（多类 = 或）。字段：layers。"若刀片有热/酸附魔"</summary>
        HasEnchant = 2,
        /// <summary>有任意冷/热/酸。字段：layers。"若有冷/热/酸"</summary>
        HasAnyLayer = 3,
        /// <summary>带某标签（形态转换的左侧也可能是形态，例如"液体"）。字段：tag。</summary>
        HasTagOrForm = 4,
        /// <summary>遇到某张卡。字段：cardName。"遇到水时，H+1。"</summary>
        EncounterCard = 5,
    }

    /// <summary>这句话什么时候生效。</summary>
    public enum RuleTrigger
    {
        /// <summary>立刻执行（启动 / 打出法术的那一刻）。</summary>
        Immediate = 0,
        /// <summary>每次启动都执行（"刀片每次启动，其他素材D-2"，挂在刀片卡上）。</summary>
        EachStartup = 1,
    }

    /// <summary>规则文本来自哪个字段（未识别清单要报"哪张卡的哪个字段"）。</summary>
    public enum RuleField
    {
        Startup = 0,
        Sacrifice = 1,
        Transition = 2,
        Exhaust = 3,
        Spell = 4,
    }

    // ══════════════════════════════════════════════════════════════════
    //  二、解析结果的数据结构
    // ══════════════════════════════════════════════════════════════════

    /// <summary>一张产出的卡（"额外产生一张水蒸气"的结果）。</summary>
    public class ProducedCard
    {
        public string name = "";

        /// <summary>算素材、法术还是空白卡；<see cref="ProduceKind.Auto"/> = 卡表里没这张卡。</summary>
        public ProduceKind kind = ProduceKind.Auto;

        public int count = 1;

        /// <summary>谁产出的（卡名 / 阶段），日志和报告里用来追溯。</summary>
        public string from = "";

        public override string ToString()
        {
            return count > 1 ? name + "×" + count : name;
        }
    }

    /// <summary>一条解析出来的操作。字段用不上的留默认值。</summary>
    public class RuleAction
    {
        public RuleOp op = RuleOp.None;

        /// <summary>涉及的层数类别（可为空：由上下文/修正补齐）。</summary>
        public LayerKind[] layers = EmptyKinds;

        /// <summary>数值。<see cref="RuleText.PlaceholderAmount"/>（-1）= 正文写的是"一定分数"，用配置里的占位值。</summary>
        public int amount;

        /// <summary>加层时：这批层数"不衰退"。</summary>
        public bool permanent;

        /// <summary>amount 的来源（字面 / 当前层数 / 已消耗层数）。</summary>
        public RuleAmountSource amountSource = RuleAmountSource.Fixed;

        /// <summary>产出的卡名（ProduceCard）。</summary>
        public string cardName = "";

        /// <summary>产出张数。</summary>
        public int count = 1;

        /// <summary>产出算素材还是法术。</summary>
        public ProduceKind produce = ProduceKind.Auto;

        /// <summary>原文小句（日志和报告都直接引用它，方便和策划对照）。</summary>
        public string text = "";

        /// <summary>附加说明（占位数值、上下文补全之类）。</summary>
        public string note = "";

        /// <summary>解析得出来、但当前模型执行不了（例：素材没有 H）。</summary>
        public bool unsupported;

        /// <summary>执行不了的原因（要写清楚，别让它静默失效）。</summary>
        public string unsupportedReason = "";

        public static readonly LayerKind[] EmptyKinds = new LayerKind[0];

        public bool HasKind(LayerKind k)
        {
            for (int i = 0; i < layers.Length; i++) if (layers[i] == k) return true;
            return false;
        }
    }

    /// <summary>条件。句子没有"若…"时是 Always。</summary>
    public class RuleCondition
    {
        public RuleConditionKind kind = RuleConditionKind.Always;

        /// <summary>涉及的层数类别（层数阈值 / 附魔 / 任意冷热酸）。</summary>
        public LayerKind[] layers = RuleAction.EmptyKinds;

        /// <summary>层数阈值 N。</summary>
        public int threshold = 1;

        /// <summary>标签或形态（HasTagOrForm）。</summary>
        public string tag = "";

        /// <summary>卡名（EncounterCard）。</summary>
        public string cardName = "";

        /// <summary>条件原文（日志里原样引用）。</summary>
        public string text = "";

        public static RuleCondition Always()
        {
            return new RuleCondition { kind = RuleConditionKind.Always, text = "（无条件）" };
        }

        public static string KindsText(LayerKind[] kinds)
        {
            if (kinds == null || kinds.Length == 0) return "?";
            string[] parts = new string[kinds.Length];
            for (int i = 0; i < kinds.Length; i++) parts[i] = LayerLedger.Name(kinds[i]);
            return string.Join("/", parts);
        }

        /// <summary>中文描述，日志里直接用。</summary>
        public string Describe()
        {
            switch (kind)
            {
                case RuleConditionKind.LayerAtLeast:
                    return "刀片" + KindsText(layers) + "层数≥" + threshold;
                case RuleConditionKind.HasEnchant:
                    return "刀片有" + KindsText(layers) + "附魔";
                case RuleConditionKind.HasAnyLayer:
                    return "有任意" + KindsText(layers);
                case RuleConditionKind.HasTagOrForm:
                    return "带" + tag + "（标签或形态）";
                case RuleConditionKind.EncounterCard:
                    return "遇到" + cardName;
            }
            return "（无条件）";
        }
    }

    /// <summary>一个"条件 → 操作列表"。一句话 = 一个 clause。</summary>
    public class RuleClause
    {
        public RuleCondition condition = RuleCondition.Always();
        public RuleTrigger trigger = RuleTrigger.Immediate;
        public List<RuleAction> actions = new List<RuleAction>();

        /// <summary>原句（含条件那半句）。</summary>
        public string sentence = "";

        public string note = "";
    }

    /// <summary>
    /// 一条形态转换规则：触发（标签/形态 + 层数）→ 结果卡。
    /// 结果写在第一个的是"这张素材变成什么"，其余的算额外产出。
    /// </summary>
    public class TransitionRule
    {
        public RuleCondition condition = RuleCondition.Always();

        /// <summary>左侧的标签/形态（"易燃"、"可熔"、"液体"）。</summary>
        public string triggerTag = "";

        /// <summary>结果里"变成什么"（第一个产物）。</summary>
        public RuleAction first;

        /// <summary>额外的产出（"火焰、碳" 里的第二个及以后）。</summary>
        public List<RuleAction> extra = new List<RuleAction>();

        /// <summary>括号说明里写了"（D立即归零）"。</summary>
        public bool dToZero;

        /// <summary>括号里的其它说明（原样留着给日志）。</summary>
        public string annotation = "";

        public bool recognized;
        public string reason = "";
        public string sourceText = "";

        /// <summary>条件的完整中文描述：标签/形态 + 层数阈值（两半都要满足）。</summary>
        public string DescribeCondition()
        {
            return "「" + triggerTag + "」（标签或形态）+ " +
                   RuleCondition.KindsText(condition.layers) + "层数≥" + condition.threshold;
        }

        public string Describe()
        {
            string s = condition.Describe() + " → " + (first != null ? first.cardName : "?");
            if (extra.Count > 0)
            {
                for (int i = 0; i < extra.Count; i++) s += "、" + extra[i].cardName;
            }
            if (dToZero) s += "（D立即归零）";
            return s;
        }
    }

    /// <summary>解析不了的一句话。**这是这份代码最重要的输出之一**：它保证"没实现"不会被当成"已实现"。</summary>
    public class UnrecognizedRule
    {
        public string cardId = "";
        public string cardName = "";
        public string field = "";

        /// <summary>原句（策划可以直接搜到）。</summary>
        public string sentence = "";

        /// <summary>为什么没解析成功。</summary>
        public string reason = "";

        /// <summary>这句话里已经认出来的部分（方便对照改写）。</summary>
        public string partial = "";

        /// <summary>给策划的改写建议。</summary>
        public string suggestion = "";
    }

    /// <summary>一个字段的解析结果。不变量：<c>clauses.Count + unrecognized.Count == sentenceCount</c>。</summary>
    public class RuleParseResult
    {
        public string cardId = "";
        public string cardName = "";
        public RuleField field = RuleField.Startup;

        /// <summary>切句后的句子数（空句不算）。</summary>
        public int sentenceCount;

        public List<RuleClause> clauses = new List<RuleClause>();
        public List<UnrecognizedRule> unrecognized = new List<UnrecognizedRule>();
        public List<TransitionRule> transitions = new List<TransitionRule>();

        public bool IsFullyParsed { get { return unrecognized.Count == 0; } }

        /// <summary>账目对不对得上（对不上就是解析器自己的 bug，探针会盯着这条）。</summary>
        public bool Balanced { get { return sentenceCount == clauses.Count + unrecognized.Count; } }

        public string FieldLabel { get { return RuleText.FieldName(field); } }
    }

    /// <summary>
    /// 一张法术的规格（RuleText 只认这几个字段）。
    ///
    /// 【为什么不直接用 Data.Spell】
    ///   离线探针（Tools/RuleProbe）只编译 Rules + 少量数据文件，
    ///   引不到 Data.Spell 那一串（它挂着 EffectGroup / TurnState）。
    ///   用一个只有 5 个字符串的小结构，Rules 层就完全不依赖运行时类型。
    /// </summary>
    public struct SpellSpec
    {
        public string id;
        public string name;

        /// <summary>附魔类型：热 / 冷 / 酸 / 催化；不用附魔的留空。</summary>
        public string enchant;

        public string category;

        /// <summary>需求（效果原文）。</summary>
        public string requirement;

        public SpellSpec(string id, string name, string enchant, string category, string requirement)
        {
            this.id = id;
            this.name = name;
            this.enchant = enchant;
            this.category = category;
            this.requirement = requirement;
        }
    }

    /// <summary>
    /// 卡面数值（正文 §2.2 的三属性）：H 珍稀度 / D 使用次数 / V 得分。
    ///
    /// 【为什么 V 用"档位"而不是直接给数】
    ///   正文 §2.2 明确写了 V 是一个枚举：**极低=1、低=2、中=3、高=5、极高=8**，
    ///   而"暂时还没有设计具体数值，等待第二周设计关卡一并处理"。
    ///   所以卡表里现在既没有数字也没有档位；引擎这边先把**档位→数字**的映射实现好，
    ///   卡表补上 `"v": "中"` 或 `"v": 3` 就能直接用。
    /// </summary>
    public struct CardValues
    {
        /// <summary>珍稀度：作为刀片核心或并入刀片时全额加到刀片 H</summary>
        public int H;

        /// <summary>使用次数满值（形态变化后的新卡 D 重置为满，用的就是它）</summary>
        public int D;

        /// <summary>得分值</summary>
        public int V;

        /// <summary>V 的档位原文（"中" / "极高"），日志里显示用</summary>
        public string vGrade;

        public CardValues(int h, int d, int v)
        {
            H = h;
            D = d;
            V = v;
            vGrade = "";
        }

        public CardValues(int h, int d, int v, string vGrade)
        {
            H = h;
            D = d;
            V = v;
            this.vGrade = vGrade != null ? vGrade : "";
        }

        public bool IsEmpty { get { return H == 0 && D == 0 && V == 0; } }

        /// <summary>正文 §2.2 的 V 档位映射：极低=1、低=2、中=3、高=5、极高=8。认不出来返回 0。</summary>
        public static int VGradeValue(string grade)
        {
            if (string.IsNullOrEmpty(grade)) return 0;

            string g = grade.Trim();
            switch (g)
            {
                case "极低": return 1;
                case "低":   return 2;
                case "中":   return 3;
                case "高":   return 5;
                case "极高": return 8;
            }

            int n;
            if (int.TryParse(g, out n)) return n;   // 卡表直接写数字也认
            return 0;
        }

        /// <summary>卡表里的 v 字段："中" 或 "3" 都能读。</summary>
        public static int ParseV(string text)
        {
            return VGradeValue(text);
        }

        public string Describe()
        {
            return "H=" + H + " D=" + D + " V=" + V + (string.IsNullOrEmpty(vGrade) ? "" : "（" + vGrade + "）");
        }
    }

    /// <summary>卡表查询（"这个名字是素材还是法术"、以及卡面数值）。探针和 Unity 各给一份实现。</summary>
    public interface ICardLookup
    {
        /// <summary>按中文名取素材模板；没有就 null。</summary>
        Ingredient Material(string name);

        /// <summary>这个名字是不是法术。</summary>
        bool IsSpell(string name);

        /// <summary>全部法术名（"变为一张随机法术卡"要用）。</summary>
        List<string> SpellNames();

        /// <summary>法术的附魔类型（"附魔到刀片"要靠它；不是法术就空串）。</summary>
        string EnchantOf(string spellName);

        /// <summary>卡面数值（H/D/V）。卡表还没填数值时返回全 0 + D=3 的兜底。</summary>
        CardValues ValuesOf(string cardName);
    }

    /// <summary>没有卡表时的兜底：什么都查不到，但引擎仍能跑（名字按原文用）。</summary>
    public class EmptyCardLookup : ICardLookup
    {
        public static readonly EmptyCardLookup Instance = new EmptyCardLookup();

        public Ingredient Material(string name) { return null; }
        public bool IsSpell(string name) { return false; }
        public List<string> SpellNames() { return new List<string>(); }
        public string EnchantOf(string spellName) { return ""; }
        public CardValues ValuesOf(string cardName) { return new CardValues(0, 3, 0); }
    }

    /// <summary>解析时的上下文（这张卡是什么、法术的附魔类型是什么）。</summary>
    public class RuleContext
    {
        public ICardLookup lookup;

        /// <summary>法术的附魔类型（"附魔到刀片"这句话本身没写类型，靠它补）。</summary>
        public string enchantKind = "";

        public RuleContext() { }

        public RuleContext(ICardLookup lookup) { this.lookup = lookup; }

        public RuleContext(ICardLookup lookup, string enchantKind)
        {
            this.lookup = lookup;
            this.enchantKind = enchantKind != null ? enchantKind : "";
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  三、解析器
    //
    //  流程：切句（；/。） → 认"触发前缀" → 认条件（若…，则… / …时，…）
    //        → 切小句（，） → 小句逐个匹配 op → 修正（把"每消耗一层"接上消耗的类别）
    //  任何一步认不出来：**整句**进未识别清单（不半懂半不懂地执行一半）。
    // ══════════════════════════════════════════════════════════════════
    public static class RuleText
    {
        /// <summary>正文写"获得一定分数"这类没给数值的地方，用这个哨兵值，结算时替换成 TurnRules 里的占位值。</summary>
        public const int PlaceholderAmount = -1;

        private static readonly Regex ReThreshold = new Regex(@"(?:≥|>=|＞|>|大于等于|不低于|至少)\s*(\d+)", RegexOptions.Compiled);
        private static readonly Regex ReHPlus     = new Regex(@"H\s*(?:\+|＋)\s*(\d+)", RegexOptions.Compiled);
        private static readonly Regex ReHIncrease = new Regex(@"H\s*(?:增加|提高)\s*(\d+)", RegexOptions.Compiled);
        private static readonly Regex ReGainH     = new Regex(@"(?:额外)?获得\s*(\d+)\s*H", RegexOptions.Compiled);
        private static readonly Regex ReScoreNum  = new Regex(@"获得\s*(\d+)\s*分", RegexOptions.Compiled);
        private static readonly Regex ReProduce   = new Regex(@"(?:产生|变为|获得|得到)\s*(?:额外)?\s*(?<n>[0-9一二两三四五六七八九十]+)?\s*张\s*(?<name>[^，。；,、]+)", RegexOptions.Compiled);
        private static readonly Regex ReLayerPlus = new Regex(@"层数\s*(?:\+|＋)\s*(\d+)", RegexOptions.Compiled);
        private static readonly Regex ReDMinus    = new Regex(@"D\s*[-－−–]\s*(\d+)", RegexOptions.Compiled);
        private static readonly Regex ReLayerNum  = new Regex(@"(\d+)\s*层", RegexOptions.Compiled);
        private static readonly Regex ReHDamage   = new Regex(@"(\d+)\s*点?\s*(?:刀片)?\s*H", RegexOptions.Compiled);
        private static readonly Regex ReBurstMul  = new Regex(@"(\d+)\s*倍", RegexOptions.Compiled);
        private static readonly Regex ReTag       = new Regex(@"(?:带|有)\s*(?<tag>[^，。；、\s]+?)\s*标签", RegexOptions.Compiled);
        private static readonly Regex ReEncounter = new Regex(@"遇到?\s*(?<name>[^，。；、\s]+?)\s*时", RegexOptions.Compiled);

        public static string FieldName(RuleField f)
        {
            switch (f)
            {
                case RuleField.Startup:    return "启动";
                case RuleField.Sacrifice:  return "献祭";
                case RuleField.Transition: return "形态转换";
                case RuleField.Exhaust:    return "D耗尽";
                case RuleField.Spell:      return "法术需求";
            }
            return "?";
        }

        public static string OpName(RuleOp op)
        {
            switch (op)
            {
                case RuleOp.None:                   return "声明";
                case RuleOp.AddScore:               return "固定加分";
                case RuleOp.MultiplyScore:          return "得分倍率";
                case RuleOp.ZeroScore:              return "得分为0";
                case RuleOp.ScorePerLayer:          return "每层加分";
                case RuleOp.ScorePerConsumedLayer:  return "每消耗一层加分";
                case RuleOp.ScoreByLayerCount:      return "按层数加分";
                case RuleOp.BladeNoConsumeH:        return "不消耗刀片H";
                case RuleOp.BladeDamageH:           return "扣刀片H";
                case RuleOp.BladeHardenH:           return "加刀片H";
                case RuleOp.BladeDamageOtherH:      return "扣其他素材H";
                case RuleOp.OtherMaterialsDepleteD: return "其他素材D-N";
                case RuleOp.AddLayer:               return "加层";
                case RuleOp.ConsumeLayer:           return "消耗层";
                case RuleOp.ConsumeAllLayers:       return "消耗所有层";
                case RuleOp.NoConsumeLayer:         return "不消耗层";
                case RuleOp.DoubleLayers:           return "层数翻倍";
                case RuleOp.ProduceCard:            return "产出卡";
                case RuleOp.ProduceRandomSpell:     return "随机法术卡";
                case RuleOp.EnchantBlade:           return "附魔刀片";
                case RuleOp.LowerEnchantThreshold:  return "降低触发阈值";
                case RuleOp.SetBurstMultiplier:     return "爆刀倍率";
            }
            return op.ToString();
        }

        // ── 切句 / 切小句 ─────────────────────────────────────────────

        /// <summary>按 ；/; 。 换行切句。句号在"（…）"里也会切 —— 目前正文没有这种写法。</summary>
        public static List<string> SplitSentences(string text)
        {
            List<string> list = new List<string>();
            if (string.IsNullOrEmpty(text)) return list;

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '；' || c == ';' || c == '。' || c == '\n')
                {
                    string s = sb.ToString().Trim();
                    if (s.Length > 0) list.Add(s);
                    sb.Length = 0;
                }
                else sb.Append(c);
            }
            string last = sb.ToString().Trim();
            if (last.Length > 0) list.Add(last);
            return list;
        }

        /// <summary>按逗号切小句。</summary>
        private static List<string> SplitSegments(string text)
        {
            List<string> list = new List<string>();
            if (string.IsNullOrEmpty(text)) return list;

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '，' || c == ',' || c == ';' || c == '；')
                {
                    string s = sb.ToString().Trim();
                    if (s.Length > 0) list.Add(s);
                    sb.Length = 0;
                }
                else sb.Append(c);
            }
            string last = sb.ToString().Trim();
            if (last.Length > 0) list.Add(last);
            return list;
        }

        /// <summary>把"每有1层冷/热/酸"和后面的"获得1分"合成一句 —— 数值和类别在两个小句里，拆开各自都读不出完整含义。</summary>
        private static List<string> MergeSplitSegments(string text)
        {
            List<string> raw = SplitSegments(text);
            List<string> merged = new List<string>();

            for (int i = 0; i < raw.Count; i++)
            {
                string seg = raw[i];
                bool wantsNext =
                    (seg.Contains("每有") || seg.Contains("每消耗")) &&
                    seg.Contains("层") && !seg.Contains("分");

                if (wantsNext && i + 1 < raw.Count && raw[i + 1].Contains("分"))
                {
                    merged.Add(seg + "，" + raw[i + 1]);
                    i++;
                    continue;
                }
                merged.Add(seg);
            }
            return merged;
        }

        // ── 层数类别 ──────────────────────────────────────────────────

        /// <summary>
        /// 中文里的层数类别 → 枚举，**可以多个**（"冷/热/酸"）。
        /// 注意：不能直接用 LayerLedger.TryParse —— 它只认第一个命中的类别。
        /// </summary>
        public static List<LayerKind> ParseKinds(string text)
        {
            List<LayerKind> list = new List<LayerKind>();
            if (string.IsNullOrEmpty(text)) return list;

            for (int i = 0; i < text.Length; i++)
            {
                LayerKind k;
                bool hit = false;

                if (i + 1 < text.Length && text[i] == '催' && text[i + 1] == '化') { k = LayerKind.Catalyst; hit = true; i++; }
                else if (text[i] == '热') { k = LayerKind.Heat; hit = true; }
                else if (text[i] == '冷') { k = LayerKind.Cold; hit = true; }
                else if (text[i] == '酸') { k = LayerKind.Acid; hit = true; }
                else k = LayerKind.Heat;

                if (hit && !list.Contains(k)) list.Add(k);
            }
            return list;
        }

        private static LayerKind[] Kinds(string text)
        {
            return ParseKinds(text).ToArray();
        }

        // ── 数字 ──────────────────────────────────────────────────────

        private static bool IsCnNum(char c)
        {
            switch (c)
            {
                case '零': case '〇': case '一': case '两': case '二': case '三': case '四':
                case '五': case '六': case '七': case '八': case '九': case '十':
                    return true;
            }
            return false;
        }

        /// <summary>"3" / "一" / "两" / "十" → 数字。</summary>
        public static int ParseCountToken(string t, int fallback)
        {
            if (string.IsNullOrEmpty(t)) return fallback;
            t = t.Trim();

            int n;
            if (int.TryParse(t, out n)) return n;

            if (t.Length == 1)
            {
                switch (t[0])
                {
                    case '零': case '〇': return 0;
                    case '一': return 1;
                    case '两': case '二': return 2;
                    case '三': return 3;
                    case '四': return 4;
                    case '五': return 5;
                    case '六': return 6;
                    case '七': return 7;
                    case '八': return 8;
                    case '九': return 9;
                    case '十': return 10;
                }
            }
            return fallback;
        }

        /// <summary>取单位词前面那个数（"获得6层热" → 6；"附带一层不衰退的冷" → 1）。</summary>
        private static int NumBefore(string text, string unit)
        {
            int i = text.IndexOf(unit, StringComparison.Ordinal);
            if (i <= 0) return 1;

            int j = i - 1;
            StringBuilder sb = new StringBuilder();
            while (j >= 0 && (char.IsDigit(text[j]) || IsCnNum(text[j])))
            {
                sb.Insert(0, text[j]);
                j--;
            }
            if (sb.Length == 0) return 1;
            return ParseCountToken(sb.ToString(), 1);
        }

        /// <summary>"…获得5分" → 5；"…获得一定分数" → 占位哨兵。</summary>
        private static int ScoreAmount(string seg)
        {
            if (seg.Contains("一定分")) return PlaceholderAmount;

            Match m = ReScoreNum.Match(seg);
            if (m.Success) return int.Parse(m.Groups[1].Value);
            return 1;
        }

        // ── 条件 ──────────────────────────────────────────────────────

        /// <summary>认条件。认不出来时 reason 要说清楚为什么、建议怎么改。</summary>
        public static bool TryParseCondition(string text, out RuleCondition cond, out string reason)
        {
            cond = RuleCondition.Always();
            reason = "";

            string t = (text != null ? text : "").Trim();
            if (t.Length == 0) return true;

            // 反应式触发：没有结算时机（层数账本和 H 变更都没有回调钩子）
            if (t.Contains("每次") || t.Contains("每当"))
            {
                reason = "『" + t + "』是反应式触发（每当/每次…时），当前回合结算里没有这个触发点";
                return false;
            }

            // 标签 / 形态
            Match tag = ReTag.Match(t);
            if (tag.Success)
            {
                cond = new RuleCondition
                {
                    kind = RuleConditionKind.HasTagOrForm,
                    tag = tag.Groups["tag"].Value,
                    text = t,
                };
                return true;
            }

            // 遇到某张卡
            if (t.Contains("遇到") || t.Contains("遇"))
            {
                Match enc = ReEncounter.Match(t);
                if (enc.Success)
                {
                    cond = new RuleCondition
                    {
                        kind = RuleConditionKind.EncounterCard,
                        cardName = enc.Groups["name"].Value,
                        text = t,
                    };
                    return true;
                }
            }

            List<LayerKind> kinds = ParseKinds(t);
            if (kinds.Count == 0)
            {
                reason = "『" + t + "』里既没有层数类别（热/冷/酸/催化），也没有标签 / 遇到的卡";
                return false;
            }

            bool anyThree = kinds.Count == 3 &&
                            kinds.Contains(LayerKind.Cold) &&
                            kinds.Contains(LayerKind.Heat) &&
                            kinds.Contains(LayerKind.Acid);

            // 层数 ≥ N
            if (t.Contains("层数"))
            {
                Match th = ReThreshold.Match(t);
                if (th.Success)
                {
                    cond = new RuleCondition
                    {
                        kind = RuleConditionKind.LayerAtLeast,
                        layers = kinds.ToArray(),
                        threshold = int.Parse(th.Groups[1].Value),
                        text = t,
                    };
                    return true;
                }
            }

            // 有 X 附魔 / 有 X（层数）/ 有任意冷热酸
            if (t.Contains("有"))
            {
                cond = new RuleCondition
                {
                    kind = (anyThree || t.Contains("任意")) ? RuleConditionKind.HasAnyLayer : RuleConditionKind.HasEnchant,
                    layers = kinds.ToArray(),
                    threshold = 1,
                    text = t,
                };
                return true;
            }

            reason = "『" + t + "』没认出是哪一种条件（层数阈值 / 有某类附魔 / 带标签 / 遇到某张卡）";
            return false;
        }

        // ── 小句 → 单个 op ────────────────────────────────────────────

        private static RuleAction Act(RuleOp op, string text, string note)
        {
            return new RuleAction { op = op, text = text, note = note != null ? note : "" };
        }

        private static bool IsDeclaration(string s)
        {
            if (s == "无" || s.StartsWith("无。")) return true;
            if (s.StartsWith("无额外效果")) return true;
            if (s.StartsWith("不响应")) return true;
            if (s.StartsWith("可叠加")) return true;
            if (s.StartsWith("每回合结束衰减")) return true;
            return false;
        }

        private static string StripFillers(string seg)
        {
            string s = seg.Trim();
            string[] fillers = { "则本次启动", "同时", "以及", "但是", "额外", "本次启动", "则", "且", "并", "但", "本次" };

            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < fillers.Length; i++)
                {
                    if (s.StartsWith(fillers[i], StringComparison.Ordinal))
                    {
                        s = s.Substring(fillers[i].Length).Trim();
                        changed = true;
                    }
                }
            }
            return s;
        }

        /// <summary>一个小句 → 一个 op。认不出来时 reason 说明为什么。</summary>
        private static bool TryMatchSegment(string rawSeg, RuleContext ctx, out RuleAction action, out string reason)
        {
            action = null;
            reason = "";

            string s = StripFillers(rawSeg);
            if (s.Length == 0)
            {
                action = Act(RuleOp.None, rawSeg, "连接词，没有实际效果");
                return true;
            }

            List<LayerKind> kinds = ParseKinds(s);

            // 0) 声明
            if (IsDeclaration(s)) { action = Act(RuleOp.None, rawSeg, "声明性句子"); return true; }

            // 1) 随机法术卡
            if (s.Contains("随机法术")) { action = Act(RuleOp.ProduceRandomSpell, rawSeg, ""); action.count = 1; return true; }

            // 2) 降低触发阈值（★ 必须排在"附魔"前面："降低热/冷/酸附魔触发阈值"里也有"附魔"两个字）
            if (s.Contains("阈值"))
            {
                action = Act(RuleOp.LowerEnchantThreshold, rawSeg, "具体降多少见 TurnRules.CatalystThresholdReduce");
                action.amount = 1;
                return true;
            }

            // 3) 作用范围限定（余温："所有“火焰”法术，附加“热”的层数+1"）：这句话本身没有动作，只是限定对象
            if (s.StartsWith("所有", StringComparison.Ordinal) && s.Contains("法术") &&
                !s.Contains("产生") && !s.Contains("获得") && !s.Contains("消耗") && !s.Contains("层") && !s.Contains("H"))
            {
                action = Act(RuleOp.None, rawSeg, "作用范围限定：只叠加在指定的法术上");
                return true;
            }

            // 4) 附魔到刀片
            if (s.Contains("附魔"))
            {
                List<LayerKind> ek = kinds.Count > 0 ? kinds : ParseKinds(ctx != null ? ctx.enchantKind : "");
                if (ek.Count == 0) { reason = "『附魔』没写附魔类型，上下文（法术的 enchant 字段）也是空的"; return false; }
                action = Act(RuleOp.EnchantBlade, rawSeg, "附魔 = 给刀片加一层该类型层数");
                action.layers = ek.ToArray();
                action.amount = 1;
                return true;
            }

            // 4) 消耗所有某类层数（★ "不消耗…层数" 要排在这之前，否则会被当成消耗）
            if (s.Contains("消耗") && !s.Contains("不消耗") && s.Contains("层数") && (s.Contains("所有") || s.Contains("全部")))
            {
                if (kinds.Count == 0) { reason = "『消耗所有…层数』没写是哪一类层数（热/冷/酸/催化）"; return false; }
                action = Act(RuleOp.ConsumeAllLayers, rawSeg, "两份计数（衰退/不衰退）都消耗");
                action.layers = kinds.ToArray();
                return true;
            }

            // 5) 消耗 N 层
            if (s.Contains("消耗") && !s.Contains("不消耗") && s.Contains("层数"))
            {
                if (kinds.Count == 0) { reason = "『消耗…层数』没写是哪一类层数"; return false; }
                action = Act(RuleOp.ConsumeLayer, rawSeg, "先吃衰退层，不够再吃不衰退层");
                action.layers = kinds.ToArray();
                Match m = ReLayerNum.Match(s);
                action.amount = m.Success ? int.Parse(m.Groups[1].Value) : 1;
                return true;
            }

            // 6) 不消耗某类层数
            if (s.Contains("不消耗") && s.Contains("层数"))
            {
                if (kinds.Count == 0) { reason = "『不消耗…层数』没写是哪一类层数"; return false; }
                action = Act(RuleOp.NoConsumeLayer, rawSeg, "本回合该类层数不会被启动/形态转换消耗");
                action.layers = kinds.ToArray();
                return true;
            }

            // 7) 层数翻倍
            if (s.Contains("层数") && s.Contains("翻倍"))
            {
                action = Act(RuleOp.DoubleLayers, rawSeg, kinds.Count == 0 ? "没写哪类层数，按条件里的类别处理" : "");
                action.layers = kinds.ToArray();
                return true;
            }

            // 8) 层数 + N（余温："附加“热”的层数+1"）
            Match plus = ReLayerPlus.Match(s);
            if (plus.Success)
            {
                if (kinds.Count == 0) { reason = "『层数+N』没写是哪一类层数"; return false; }
                action = Act(RuleOp.AddLayer, rawSeg, "");
                action.layers = kinds.ToArray();
                action.amount = int.Parse(plus.Groups[1].Value);
                return true;
            }

            // 9) 按层数扣 H（"减少刀片等同于当前酸层数的H"、"以及和冷层数相当的H"）
            if (s.Contains("H") && s.Contains("层数") && (s.Contains("相当") || s.Contains("等同于") || s.Contains("相当于")))
            {
                action = Act(RuleOp.BladeDamageH, rawSeg, "扣多少 = 层数");
                action.layers = kinds.ToArray();
                action.amountSource = s.Contains("当前") ? RuleAmountSource.CurrentLayers : RuleAmountSource.ConsumedLayers;
                return true;
            }

            // 10) 其他素材 D - N
            Match dm = ReDMinus.Match(s);
            if (dm.Success && s.Contains("素材"))
            {
                action = Act(RuleOp.OtherMaterialsDepleteD, rawSeg, "");
                action.amount = int.Parse(dm.Groups[1].Value);
                return true;
            }

            // 11) 额外消耗其他素材 H（v3 里素材有 H 了，这条能落地）
            if (s.Contains("其他素材") && s.Contains("H"))
            {
                action = Act(RuleOp.BladeDamageOtherH, rawSeg, "每张其他素材 H-1（正文没给数值，按 1）");
                action.amount = 1;
                return true;
            }

            // 12) 不消耗刀片 H
            if (s.Contains("不消耗") && s.Contains("H"))
            {
                action = Act(RuleOp.BladeNoConsumeH, rawSeg, "跳过本次启动的固有 H 消耗");
                return true;
            }

            // 13) 得分清零 / 倍率
            if (s.Contains("得分为0") || s.Contains("得分为 0") || s.Contains("得分归零"))
            {
                action = Act(RuleOp.ZeroScore, rawSeg, "");
                return true;
            }
            if (s.Contains("得分") && (s.Contains("翻倍") || s.Contains("×2") || s.Contains("x2") || s.Contains("X2")))
            {
                action = Act(RuleOp.MultiplyScore, rawSeg, "");
                action.amount = 2;
                return true;
            }

            // 14) 每消耗一层加分（"每消耗一层，获得5分"、"每消耗一层获得一定分数"）
            if (s.Contains("每消耗") && s.Contains("层"))
            {
                action = Act(RuleOp.ScorePerConsumedLayer, rawSeg, "");
                action.layers = kinds.ToArray();
                action.amount = ScoreAmount(s);
                return true;
            }

            // 15) 每有 1 层加分（"每有1层冷/热/酸，获得1分"）
            if (s.Contains("每有") && s.Contains("层") && s.Contains("分"))
            {
                action = Act(RuleOp.ScorePerLayer, rawSeg, "");
                action.layers = kinds.ToArray();
                action.amount = ScoreAmount(s);
                return true;
            }

            // 16) 按层数直接加分（结晶）
            if (s.Contains("直接加分"))
            {
                if (kinds.Count == 0) { reason = "『直接加分』没写按哪一类层数"; return false; }
                action = Act(RuleOp.ScoreByLayerCount, rawSeg, "正文没给每层分值，按每层 1 分");
                action.layers = kinds.ToArray();
                action.amount = 1;
                return true;
            }

            // 17) 等同于…层数的分数（酸爆）
            if (s.Contains("等同于") && s.Contains("分"))
            {
                if (kinds.Count == 0) { reason = "『等同于…层数的分数』没写是哪一类层数"; return false; }
                action = Act(RuleOp.ScoreByLayerCount, rawSeg, "正文没给每层分值，按每层 1 分");
                action.layers = kinds.ToArray();
                action.amount = 1;
                return true;
            }

            // 18) 固定加分
            Match score = ReScoreNum.Match(s);
            if (score.Success)
            {
                action = Act(RuleOp.AddScore, rawSeg, "");
                action.amount = int.Parse(score.Groups[1].Value);
                return true;
            }

            // 19) 加层（"附带一层不衰退的冷"、"一次性获得6层热"）
            if (s.Contains("层") && (s.Contains("获得") || s.Contains("附带") || s.Contains("增加")))
            {
                if (kinds.Count == 0) { reason = "加层没写是哪一类层数（热/冷/酸/催化）"; return false; }
                action = Act(RuleOp.AddLayer, rawSeg, "");
                action.layers = kinds.ToArray();
                action.amount = NumBefore(s, "层");
                action.permanent = s.Contains("不衰退");
                if (action.permanent) action.note = "不衰退：不随回合衰减，但会被『消耗所有 X 层数』消耗";
                return true;
            }

            // 20) 加刀片 H
            Match hp = ReHPlus.Match(s);
            if (hp.Success) { action = Act(RuleOp.BladeHardenH, rawSeg, ""); action.amount = int.Parse(hp.Groups[1].Value); return true; }

            Match hi = ReHIncrease.Match(s);
            if (hi.Success) { action = Act(RuleOp.BladeHardenH, rawSeg, ""); action.amount = int.Parse(hi.Groups[1].Value); return true; }

            Match hg = ReGainH.Match(s);
            if (hg.Success) { action = Act(RuleOp.BladeHardenH, rawSeg, ""); action.amount = int.Parse(hg.Groups[1].Value); return true; }

            // 21) 扣刀片 H（"使刀片额外消耗2H"、"本次启动额外消耗1点刀片H"、"刀片减少5H"）
            if (s.Contains("H") && (s.Contains("消耗") || s.Contains("减少") || s.Contains("扣") || s.Contains("损伤")))
            {
                action = Act(RuleOp.BladeDamageH, rawSeg, "");
                Match hm = ReHDamage.Match(s);
                action.amount = hm.Success ? int.Parse(hm.Groups[1].Value) : 1;
                return true;
            }

            // 22) 产出卡（"额外产生一张水蒸气"、"获得3张催化术"、"产生1张酸爆"）
            Match pd = ReProduce.Match(s);
            if (pd.Success)
            {
                string name = pd.Groups["name"].Value.Trim();
                int count = ParseCountToken(pd.Groups["n"].Value, 1);
                bool blank = false;

                if (name.Contains("空白卡")) { blank = true; name = "空白卡"; }
                else
                {
                    name = TrimProduceName(name, ctx, s);
                    if (name.Length == 0) { reason = "『产出』后面没读出卡名"; return false; }
                }

                action = Act(RuleOp.ProduceCard, rawSeg, blank ? "空白卡：卡表里没有这张卡，纯占位" : "");
                action.cardName = name;
                action.count = count <= 0 ? 1 : count;
                action.produce = blank ? ProduceKind.Blank : ResolveKind(name, ctx, s);
                return true;
            }

            reason = "这句话没匹配到任何已知操作（见 RuleText 的 op 词汇表）";
            return false;
        }

        /// <summary>去掉产出名后面的说明词（"进手牌"、以及当卡表认得时的"法术"后缀）。</summary>
        private static string TrimProduceName(string name, RuleContext ctx, string seg)
        {
            string s = name.Trim();
            string[] tails = { "进手牌", "入手牌", "进手", "入手", "进牌" };

            for (int i = 0; i < tails.Length; i++)
                if (s.EndsWith(tails[i], StringComparison.Ordinal))
                    s = s.Substring(0, s.Length - tails[i].Length).Trim();

            if (s.EndsWith("法术", StringComparison.Ordinal))
            {
                string stripped = s.Substring(0, s.Length - 2).Trim();
                ICardLookup lk = ctx != null ? ctx.lookup : null;

                if (lk != null)
                {
                    // 卡表优先：整名认得就不动它（"法术卷轴"是素材，不能砍成"卷轴"）
                    bool fullKnown = lk.Material(s) != null || lk.IsSpell(s);
                    bool stripKnown = stripped.Length > 0 && (lk.Material(stripped) != null || lk.IsSpell(stripped));
                    if (!fullKnown && stripKnown) s = stripped;
                    else if (!fullKnown && !stripKnown) s = stripped;   // 名字都不认得：按"X法术"的写法砍掉后缀更可能对
                }
                else if (!s.StartsWith("法术", StringComparison.Ordinal))
                {
                    s = stripped;
                }
            }

            if (seg.Contains("法术") && s.Length == 0) s = name.Trim();
            return s;
        }

        /// <summary>产出算素材还是法术。</summary>
        private static ProduceKind ResolveKind(string name, RuleContext ctx, string seg)
        {
            ICardLookup lk = ctx != null ? ctx.lookup : null;
            if (lk != null)
            {
                if (lk.Material(name) != null) return ProduceKind.Material;
                if (lk.IsSpell(name)) return ProduceKind.Spell;
            }
            if (seg.Contains("法术")) return ProduceKind.Spell;
            return ProduceKind.Auto;
        }

        // ── 修正：把上下文里的层数类别补上 ─────────────────────────────

        /// <summary>
        /// "消耗所有冷层数，以及和冷层数相当的H，每消耗一层，获得5分" ——
        /// 后半句的"每消耗一层"没写类别，靠这句前面那次消耗补。
        /// </summary>
        private static void FixUp(RuleClause clause)
        {
            LayerKind[] lastConsumed = null;

            for (int i = 0; i < clause.actions.Count; i++)
            {
                RuleAction a = clause.actions[i];

                if (a.op == RuleOp.ConsumeAllLayers || a.op == RuleOp.ConsumeLayer)
                    if (a.layers != null && a.layers.Length > 0) lastConsumed = a.layers;

                bool needsKind =
                    (a.op == RuleOp.ScorePerConsumedLayer ||
                     a.op == RuleOp.DoubleLayers ||
                     a.op == RuleOp.ScoreByLayerCount ||
                     a.op == RuleOp.ScorePerLayer ||
                     (a.op == RuleOp.BladeDamageH && a.amountSource != RuleAmountSource.Fixed)) &&
                    (a.layers == null || a.layers.Length == 0);

                if (!needsKind) continue;

                if (lastConsumed != null && (a.op == RuleOp.ScorePerConsumedLayer || a.op == RuleOp.BladeDamageH))
                {
                    a.layers = lastConsumed;
                    a.note = Append(a.note, "类别取自本句前面那次消耗：" + RuleCondition.KindsText(lastConsumed));
                }
                else if (clause.condition != null && clause.condition.layers != null && clause.condition.layers.Length > 0)
                {
                    a.layers = clause.condition.layers;
                    a.note = Append(a.note, "类别取自条件：" + RuleCondition.KindsText(clause.condition.layers));
                }
            }
        }

        private static string Append(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b;
            return a + "；" + b;
        }

        // ── 整段字段解析 ──────────────────────────────────────────────

        /// <summary>解析一个规则字段（启动 / 献祭 / 法术需求）。</summary>
        public static RuleParseResult ParseField(string text, RuleField field, string cardId, string cardName, RuleContext ctx)
        {
            RuleParseResult res = new RuleParseResult
            {
                cardId = cardId != null ? cardId : "",
                cardName = cardName != null ? cardName : "",
                field = field,
            };

            List<string> sentences = SplitSentences(text);
            RuleTrigger carryTrigger = RuleTrigger.Immediate;

            for (int i = 0; i < sentences.Count; i++)
            {
                string sentence = sentences[i];
                res.sentenceCount++;

                RuleTrigger trig = carryTrigger;
                string s = sentence;

                // 触发前缀："刀片每次启动，其他素材D-2。"
                if (s.StartsWith("刀片每次启动", StringComparison.Ordinal)) { trig = RuleTrigger.EachStartup; s = s.Substring("刀片每次启动".Length).Trim(); }
                else if (s.StartsWith("每次启动", StringComparison.Ordinal)) { trig = RuleTrigger.EachStartup; s = s.Substring("每次启动".Length).Trim(); }

                while (s.StartsWith("，", StringComparison.Ordinal) || s.StartsWith(",", StringComparison.Ordinal)) s = s.Substring(1).Trim();

                if (s.Length == 0)
                {
                    // 纯触发标记句（"刀片每次启动；其他素材D-2。"这种写法）
                    carryTrigger = trig;
                    RuleClause mark = new RuleClause { trigger = trig, sentence = sentence, note = "触发标记" };
                    mark.actions.Add(Act(RuleOp.None, sentence, trig == RuleTrigger.EachStartup ? "每次启动都生效" : "触发标记"));
                    res.clauses.Add(mark);
                    continue;
                }
                carryTrigger = RuleTrigger.Immediate;

                // 爆刀相关的句子：它说的是"爆刀那一刻"的规则，不是普通 op，单独认
                if (s.Contains("爆刀") && s.Contains("倍"))
                {
                    Match bm = ReBurstMul.Match(s);
                    int mul = bm.Success ? int.Parse(bm.Groups[1].Value) : 3;

                    RuleClause bc = new RuleClause { trigger = trig, sentence = sentence, note = "只在本次结算真的爆刀时生效" };
                    RuleAction ba = Act(RuleOp.SetBurstMultiplier, s, "正文默认爆刀 ×2（LevelRun.BurstMultiplier），这条卡把它改成 ×" + mul);
                    ba.amount = mul;
                    bc.actions.Add(ba);
                    res.clauses.Add(bc);
                    continue;
                }

                // 声明句
                if (IsDeclaration(s))
                {
                    RuleClause dc = new RuleClause { trigger = trig, sentence = sentence, note = "声明" };
                    dc.actions.Add(Act(RuleOp.None, s, "声明性句子"));
                    res.clauses.Add(dc);
                    continue;
                }

                // 条件
                string condText = "";
                string rest = s;
                bool hasCond = false;

                if (s.StartsWith("若", StringComparison.Ordinal))
                {
                    int comma = s.IndexOf('，');
                    if (comma > 1) { condText = s.Substring(1, comma - 1).Trim(); rest = s.Substring(comma + 1).Trim(); }
                    else { condText = s.Substring(1).Trim(); rest = ""; }
                    hasCond = true;
                }
                else
                {
                    int shi = s.IndexOf("时，", StringComparison.Ordinal);
                    if (shi > 0)
                    {
                        condText = s.Substring(0, shi + 1).Trim();
                        rest = s.Substring(shi + 2).Trim();
                        hasCond = true;
                    }
                }

                RuleCondition cond = RuleCondition.Always();
                if (hasCond)
                {
                    string why;
                    if (!TryParseCondition(condText, out cond, out why))
                    {
                        res.unrecognized.Add(MakeUnrecognized(res, sentence, why, "", Suggest(why, condText)));
                        continue;
                    }
                }
                else if (s.Contains("每次") || s.Contains("每当"))
                {
                    string why = "这句话里出现了『每次/每当』，但当前回合结算只有『启动』这一个触发点，认不出它什么时候生效";
                    res.unrecognized.Add(MakeUnrecognized(res, sentence, why, "", Suggest(why, s)));
                    continue;
                }

                // 小句 → op
                List<string> segs = MergeSplitSegments(rest);
                if (segs.Count == 0)
                {
                    res.unrecognized.Add(MakeUnrecognized(res, sentence, "条件后面没有可执行的内容", cond.Describe(), "把条件后面补上动作，例如『若…，则产生一张冰霜法术进手牌』"));
                    continue;
                }

                RuleClause clause = new RuleClause { condition = cond, trigger = trig, sentence = sentence };
                bool ok = true;
                string failSeg = "";
                string failWhy = "";

                for (int k = 0; k < segs.Count; k++)
                {
                    RuleAction a;
                    string why;
                    if (!TryMatchSegment(segs[k], ctx, out a, out why))
                    {
                        ok = false;
                        failSeg = segs[k];
                        failWhy = why;
                        break;
                    }
                    clause.actions.Add(a);
                }

                if (!ok)
                {
                    res.unrecognized.Add(MakeUnrecognized(res, sentence,
                        "小句『" + failSeg + "』没识别：" + failWhy,
                        DescribeActions(clause.actions),
                        Suggest(failWhy, failSeg)));
                    continue;
                }

                FixUp(clause);
                res.clauses.Add(clause);
            }

            return res;
        }

        /// <summary>把一组 op 概括成一行（给未识别条目的"已认出的部分"用）。</summary>
        public static string DescribeActions(List<RuleAction> actions)
        {
            if (actions == null || actions.Count == 0) return "";

            List<string> parts = new List<string>();
            for (int i = 0; i < actions.Count; i++)
            {
                RuleAction a = actions[i];
                string one = OpName(a.op);
                if (a.op == RuleOp.AddLayer || a.op == RuleOp.ConsumeAllLayers || a.op == RuleOp.ConsumeLayer ||
                    a.op == RuleOp.DoubleLayers || a.op == RuleOp.NoConsumeLayer || a.op == RuleOp.EnchantBlade)
                    one += "(" + RuleCondition.KindsText(a.layers) + (a.amount > 0 ? "×" + a.amount : "") + ")";
                else if (a.op == RuleOp.ProduceCard)
                    one += "(" + a.cardName + (a.count > 1 ? "×" + a.count : "") + ")";
                else if (a.amount != 0 && a.amountSource == RuleAmountSource.Fixed)
                    one += "(" + a.amount + ")";
                parts.Add(one);
            }
            return string.Join(" + ", parts.ToArray());
        }

        private static UnrecognizedRule MakeUnrecognized(RuleParseResult res, string sentence, string reason, string partial, string suggestion)
        {
            return new UnrecognizedRule
            {
                cardId = res.cardId,
                cardName = res.cardName,
                field = res.FieldLabel,
                sentence = sentence,
                reason = reason,
                partial = partial,
                suggestion = suggestion,
            };
        }

        /// <summary>按原因给一条改写建议（策划看的）。</summary>
        private static string Suggest(string reason, string seg)
        {
            if (reason.Contains("反应式触发"))
                return "『每当/每次…时』没有结算时机：改成『每次启动时…』（挂在刀片/素材的启动或献祭里）或写成一次性效果";

            if (reason.Contains("手牌") || seg.Contains("手牌") || seg.Contains("指定") || seg.Contains("复制"))
                return "这句要玩家选牌（指定手牌里的某张卡），引擎没法自动结算：请拆成能自动跑的写法（例如『进手牌2张随机法术卡』），或把它标成需要交互的效果";

            if (reason.Contains("没匹配到任何已知操作"))
                return "对照 op 词汇表改写，或把这条规则拆成几句（每句一个动作）";

            if (reason.Contains("V") || seg.Contains("V"))
                return "刀片模型里只有 H + 四种层数，没有 V：请把 V 换成 H 或某一类层数，或先补一份 V 的规则说明";

            if (reason.Contains("附魔类型"))
                return "在法术表的 enchant 字段里写上附魔类型（热/冷/酸/催化），或把类型写进句子（例如『附魔冷到刀片』）";

            if (reason.Contains("哪一类层数"))
                return "补上类别：热 / 冷 / 酸 / 催化（多类用『/』，例如『消耗所有冷/热层数』）";

            if (reason.Contains("条件"))
                return "条件请对齐这几种写法：『若刀片热层数≥3』『若刀片有热附魔』『若有冷/热/酸』『带某标签』『遇到某张卡』";

            if (reason.Contains("卡名"))
                return "产出请写成『产生一张<卡名>法术进手牌』或『获得N张<卡名>』，卡名要和卡表里的名字完全一致";

            return "对照 RuleText 的 op 词汇表改写这一句；改完跑一次 Tools/RuleProbe/run.ps1，未识别清单会告诉你还有哪句没认出来";
        }

        // ── 形态转换 / D耗尽 / 法术需求 ────────────────────────────────

        /// <summary>解析一张卡的形态转换表（trigger + result 两半）。</summary>
        public static RuleParseResult ParseTransitions(FormChange[] changes, string cardId, string cardName, RuleContext ctx)
        {
            RuleParseResult res = new RuleParseResult
            {
                cardId = cardId != null ? cardId : "",
                cardName = cardName != null ? cardName : "",
                field = RuleField.Transition,
            };

            if (changes == null || changes.Length == 0) return res;

            for (int i = 0; i < changes.Length; i++)
            {
                FormChange fc = changes[i];
                if (fc == null) continue;

                // 空表项不算一句
                if (string.IsNullOrEmpty(fc.trigger) && string.IsNullOrEmpty(fc.result)) continue;

                res.sentenceCount++;

                string source = fc.Describe();
                string trigger = (fc.trigger != null ? fc.trigger : "").Trim();
                string result = (fc.result != null ? fc.result : "").Trim();

                // "无。不响应热、冷、酸。" 这种是声明，不是转换
                if (trigger.Length == 0 && (IsDeclaration(result) || result.StartsWith("无")))
                {
                    RuleClause dc = new RuleClause { sentence = source, note = "声明（无形态转换）" };
                    dc.actions.Add(Act(RuleOp.None, result, "声明性句子"));
                    res.clauses.Add(dc);
                    continue;
                }

                TransitionRule tr = new TransitionRule { sourceText = source };

                // 左侧："易燃 + 热" / "液体 + 热"
                string[] halves = trigger.Split('+');
                string tag = halves.Length > 0 ? halves[0].Trim() : "";
                string kindText = halves.Length > 1 ? halves[1].Trim() : "";

                List<LayerKind> kinds = ParseKinds(kindText);
                if (kinds.Count == 0) kinds = ParseKinds(trigger);

                if (tag.Length == 0 || kinds.Count == 0)
                {
                    tr.recognized = false;
                    tr.reason = "触发条件『" + trigger + "』没读懂：需要『标签/形态 + 层数类别』，例如『易燃 + 热』";
                    res.transitions.Add(tr);
                    res.unrecognized.Add(MakeUnrecognized(res, source, tr.reason, "", "触发写成『<标签或形态> + <层数类别>』，例如『遇冷 + 冷』『液体 + 热』"));
                    continue;
                }

                tr.triggerTag = tag;
                tr.condition = new RuleCondition
                {
                    kind = RuleConditionKind.HasTagOrForm,
                    tag = tag,
                    layers = kinds.ToArray(),
                    threshold = 1,
                    text = trigger,
                };

                // 括号说明："火焰（D立即归零）"
                string namePart = result;
                int lp = result.IndexOf('（');
                if (lp < 0) lp = result.IndexOf('(');
                if (lp >= 0)
                {
                    int rp = result.IndexOf('）', lp);
                    if (rp < 0) rp = result.IndexOf(')', lp);
                    string inner = rp > lp ? result.Substring(lp + 1, rp - lp - 1) : result.Substring(lp + 1);

                    tr.annotation = inner;
                    if (inner.Contains("D") && (inner.Contains("归零") || inner.Contains("0"))) tr.dToZero = true;

                    namePart = result.Substring(0, lp).Trim();
                }

                // 结果可能多张："火焰、碳"、"水 + 空白卡"
                string[] products = namePart.Split(new char[] { '、', '，', ',', '+' }, StringSplitOptions.RemoveEmptyEntries);

                for (int p = 0; p < products.Length; p++)
                {
                    string nm = products[p].Trim();
                    if (nm.Length == 0) continue;
                    if (nm.StartsWith("无")) continue;

                    RuleAction a = Act(RuleOp.ProduceCard, nm, "");
                    a.cardName = nm;
                    a.count = 1;
                    a.produce = nm.Contains("空白卡") ? ProduceKind.Blank : ResolveKind(nm, ctx, nm);

                    if (tr.first == null) tr.first = a;
                    else tr.extra.Add(a);
                }

                if (tr.first == null)
                {
                    tr.recognized = false;
                    tr.reason = "结果『" + result + "』里没读出卡名";
                    res.transitions.Add(tr);
                    res.unrecognized.Add(MakeUnrecognized(res, source, tr.reason, tr.condition.Describe(), "结果写成卡名，多张用『、』分隔，例如『火焰、碳』"));
                    continue;
                }

                tr.recognized = true;
                res.transitions.Add(tr);

                // 账目：一句 = 一个 clause
                RuleClause clause = new RuleClause { condition = tr.condition, sentence = source, note = "形态转换（结算时用 transitions）" };
                clause.actions.Add(tr.first);
                for (int e = 0; e < tr.extra.Count; e++) clause.actions.Add(tr.extra[e]);
                res.clauses.Add(clause);
            }

            return res;
        }

        /// <summary>解析 D 耗尽表（"空白卡" / "火焰 + 余温" / "变为一张随机法术卡"）。</summary>
        public static RuleParseResult ParseExhaust(string[] exhaust, string cardId, string cardName, RuleContext ctx)
        {
            RuleParseResult res = new RuleParseResult
            {
                cardId = cardId != null ? cardId : "",
                cardName = cardName != null ? cardName : "",
                field = RuleField.Exhaust,
            };

            if (exhaust == null) return res;

            for (int i = 0; i < exhaust.Length; i++)
            {
                string raw = (exhaust[i] != null ? exhaust[i] : "").Trim();
                if (raw.Length == 0) continue;

                res.sentenceCount++;
                RuleClause clause = new RuleClause { sentence = raw, note = "D 耗尽产出" };

                if (raw.Contains("随机法术"))
                {
                    clause.actions.Add(Act(RuleOp.ProduceRandomSpell, raw, ""));
                    res.clauses.Add(clause);
                    continue;
                }

                string[] parts = raw.Split(new char[] { '、', '，', ',', '+' }, StringSplitOptions.RemoveEmptyEntries);
                bool any = false;

                for (int p = 0; p < parts.Length; p++)
                {
                    string nm = parts[p].Trim();
                    if (nm.Length == 0) continue;

                    RuleAction a = Act(RuleOp.ProduceCard, nm, "");
                    a.cardName = nm.Contains("空白卡") ? "空白卡" : TrimProduceName(nm, ctx, nm);
                    a.count = 1;
                    a.produce = nm.Contains("空白卡") ? ProduceKind.Blank : ResolveKind(a.cardName, ctx, nm);
                    clause.actions.Add(a);
                    any = true;
                }

                if (!any)
                {
                    res.unrecognized.Add(MakeUnrecognized(res, raw, "『D耗尽』这一项里没读出卡名", "", "写卡名即可，多张用『、』或『+』分隔"));
                    continue;
                }
                res.clauses.Add(clause);
            }

            return res;
        }

        /// <summary>解析法术的「需求」原文（附魔类型从 spell.enchant 补）。</summary>
        public static RuleParseResult ParseSpell(SpellSpec spell, RuleContext ctx)
        {
            RuleContext c = new RuleContext(ctx != null ? ctx.lookup : null, spell.enchant);
            return ParseField(spell.requirement, RuleField.Spell, spell.id, spell.name, c);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  四、解析报告
    //
    //  【它存在的唯一理由】
    //    规则是中文，解析必然有认不出的写法。最坏的情况不是"没实现"，
    //    而是"看起来实现了其实没生效" —— 策划改完文案以为生效了，测试打一局没看出来，
    //    上线才发现这张卡是死的。所以：
    //      · 认不出的句子 → 未识别清单（原句 + 哪张卡 + 哪个字段 + 为什么 + 怎么改）
    //      · 认得出但当前模型执行不了的 → 单独一节
    //      · 正文写"一定分数"这类没给数值的 → 单独一节（占位值在哪配置）
    //      · 产出的卡名不在卡表里的 → 单独一节
    //    四节都空，才敢说"这份卡表全都能跑"。
    // ══════════════════════════════════════════════════════════════════
    public class RuleReport
    {
        public readonly List<RuleParseResult> fields = new List<RuleParseResult>();
        public readonly List<UnrecognizedRule> unrecognized = new List<UnrecognizedRule>();

        /// <summary>解析出来了、但当前模型执行不了（例：素材没有 H）。格式："卡·字段：原句 → 原因"。</summary>
        public readonly List<string> unsupported = new List<string>();

        /// <summary>正文没给数值、用了占位值的地方。</summary>
        public readonly List<string> placeholders = new List<string>();

        /// <summary>产出的卡名在卡表里找不到。</summary>
        public readonly List<string> unknownProducts = new List<string>();

        /// <summary>规则表可能命中这张卡，但卡表没给它产物（"标了标签忘了连锁产物"）。</summary>
        public readonly List<string> tableGaps = new List<string>();

        /// <summary>正文自相矛盾 / 没写清的地方（照抄 EnchantRules.Conflicts）。</summary>
        public readonly List<string> conflicts = new List<string>();

        /// <summary>v3 正文已经取消、但卡表里还留着的 v2.1 写法（写了也不生效）。</summary>
        public readonly List<string> superseded = new List<string>();

        /// <summary>有文本、但一条句子都没解析出来（说明切句 / 字段读取出了岔子，不是策划的问题）。</summary>
        public readonly List<string> silentFields = new List<string>();

        public int materialCount;
        public int spellCount;
        public int TotalSentences { get; private set; }
        public int ParsedSentences { get; private set; }
        public int UnbalancedFields { get; private set; }

        /// <summary>四节都空 = 这份卡表全都能跑。</summary>
        public bool Clean
        {
            get
            {
                return unrecognized.Count == 0 && unsupported.Count == 0 &&
                       unknownProducts.Count == 0 && silentFields.Count == 0 &&
                       tableGaps.Count == 0 && superseded.Count == 0;
            }
        }

        public void Add(RuleParseResult pr)
        {
            if (pr == null) return;

            fields.Add(pr);
            TotalSentences += pr.sentenceCount;
            ParsedSentences += pr.clauses.Count;
            if (!pr.Balanced) UnbalancedFields++;

            for (int i = 0; i < pr.unrecognized.Count; i++) unrecognized.Add(pr.unrecognized[i]);

            for (int i = 0; i < pr.clauses.Count; i++)
                Scan(pr, pr.clauses[i].actions);

            for (int i = 0; i < pr.transitions.Count; i++)
            {
                TransitionRule tr = pr.transitions[i];
                if (tr.first != null) Scan(pr, new List<RuleAction> { tr.first });
                Scan(pr, tr.extra);
            }
        }

        private void Scan(RuleParseResult pr, List<RuleAction> actions)
        {
            if (actions == null) return;

            for (int i = 0; i < actions.Count; i++)
            {
                RuleAction a = actions[i];
                string where = pr.cardName + "·" + pr.FieldLabel;

                if (a.unsupported)
                    unsupported.Add(where + "：「" + a.text + "」→ " + a.unsupportedReason);

                if (a.op == RuleOp.BladeNoConsumeH)
                    superseded.Add(where + "：「" + a.text + "」—— v3 正文 §四.1 改为启动固定消耗 1 刀片H，" +
                                   "没有豁免机制，这条写法已失效（引擎会跳过它并打警告）");

                if (a.amount == RuleText.PlaceholderAmount)
                    placeholders.Add(where + "：「" + a.text + "」（正文写「一定分数」，占位值见 TurnRules.PlaceholderScorePerLayer）");

                if (a.amount > 0 && !string.IsNullOrEmpty(a.note) && a.note.Contains("正文没给每层分值"))
                    placeholders.Add(where + "：「" + a.text + "」（正文没给每层分值，按 TurnRules.ScorePerLayerDefault）");
            }
        }

        /// <summary>记录一个产出名，卡表里没有就进"可疑产出"。</summary>
        public void NoteProduct(string where, string name, ProduceKind kind, ICardLookup lookup)
        {
            if (kind == ProduceKind.Blank || name == "空白卡") return;
            if (string.IsNullOrEmpty(name)) return;   // "变为一张随机法术卡"这类没有具体卡名

            if (lookup != null && (lookup.Material(name) != null || lookup.IsSpell(name))) return;

            string line = where + "：产出「" + name + "」不在卡表里（既不是素材也不是法术）";
            if (!unknownProducts.Contains(line)) unknownProducts.Add(line);
        }

        /// <summary>把整份卡表过一遍。materials / spells 就是 cards_v21.json 的内容。</summary>
        public static RuleReport BuildAll(List<Ingredient> materials, List<SpellSpec> spells, ICardLookup lookup)
        {
            RuleReport rep = new RuleReport();
            RuleContext ctx = new RuleContext(lookup);

            if (materials != null)
            {
                rep.materialCount = materials.Count;
                for (int i = 0; i < materials.Count; i++)
                {
                    Ingredient m = materials[i];
                    if (m == null) continue;

                    string id = m.id;
                    string nm = m.name;

                    RuleParseResult startup = RuleText.ParseField(m.startup, RuleField.Startup, id, nm, ctx);
                    RuleParseResult sac = RuleText.ParseField(m.sacrifice, RuleField.Sacrifice, id, nm, ctx);
                    RuleParseResult tr = RuleText.ParseTransitions(m.transitions, id, nm, ctx);
                    RuleParseResult ex = RuleText.ParseExhaust(m.exhaust, id, nm, ctx);

                    rep.CheckSilent(startup, m.startup);
                    rep.CheckSilent(sac, m.sacrifice);
                    rep.CheckSilent(tr, m.transitions != null && m.transitions.Length > 0 ? "（有形态转换表）" : "");
                    rep.CheckSilent(ex, m.exhaust != null && m.exhaust.Length > 0 ? "（有D耗尽表）" : "");

                    rep.Add(startup);
                    rep.Add(sac);
                    rep.Add(tr);
                    rep.Add(ex);

                    // 产出名要能在卡表里找到
                    for (int k = 0; k < tr.transitions.Count; k++)
                    {
                        TransitionRule t = tr.transitions[k];
                        if (t.first != null) rep.NoteProduct(nm + "·形态转换", t.first.cardName, t.first.produce, lookup);
                        for (int e = 0; e < t.extra.Count; e++)
                            rep.NoteProduct(nm + "·形态转换", t.extra[e].cardName, t.extra[e].produce, lookup);
                    }
                    for (int k = 0; k < ex.clauses.Count; k++)
                        for (int a = 0; a < ex.clauses[k].actions.Count; a++)
                        {
                            RuleAction act = ex.clauses[k].actions[a];
                            if (act.op != RuleOp.ProduceCard) continue;   // 随机法术卡、声明句没有具体卡名
                            rep.NoteProduct(nm + "·D耗尽", act.cardName, act.produce, lookup);
                        }

                    // 规则表能不能真的接上这张卡（正文 G 节："标了标签与连锁产物即可自动接入"）
                    List<string> gaps = EnchantRules.AuditProducts(m, tr.transitions);
                    for (int g = 0; g < gaps.Count; g++) rep.tableGaps.Add(nm + "：" + gaps[g]);
                }
            }

            if (spells != null)
            {
                rep.spellCount = spells.Count;
                for (int i = 0; i < spells.Count; i++)
                {
                    SpellSpec sp = spells[i];
                    RuleParseResult pr = RuleText.ParseSpell(sp, ctx);
                    rep.CheckSilent(pr, sp.requirement);
                    rep.Add(pr);
                }
            }

            // 正文自相矛盾 / 没写清的地方（照抄，便于一眼看到"哪些是要策划拍板的"）
            List<string> conflicts = EnchantRules.Conflicts();
            for (int i = 0; i < conflicts.Count; i++) rep.conflicts.Add(conflicts[i]);

            return rep;
        }

        private void CheckSilent(RuleParseResult pr, string sourceText)
        {
            if (pr == null) return;
            if (string.IsNullOrEmpty(sourceText)) return;
            if (pr.sentenceCount > 0) return;

            silentFields.Add(pr.cardName + "·" + pr.FieldLabel + "：字段里有文本，但切不出一句（原文：" + sourceText + "）");
        }

        /// <summary>按卡的解析情况，例如 "启动 4 句已解析 / 0 未识别"。</summary>
        public string SummaryLine()
        {
            return "素材 " + materialCount + " 张 · 法术 " + spellCount + " 张 ｜ 句子 " + TotalSentences +
                   " 条：已解析 " + ParsedSentences + " · 未识别 " + unrecognized.Count +
                   " ｜ 执行不了 " + unsupported.Count + " · 占位数值 " + placeholders.Count +
                   " · 可疑产出 " + unknownProducts.Count + " · 规则表接不上 " + tableGaps.Count +
                   " · v3 已失效写法 " + superseded.Count;
        }

        public void Print(System.IO.TextWriter w)
        {
            w.WriteLine(SummaryLine());
            w.WriteLine("字段账目对不上的：" + UnbalancedFields + " 个（正常应为 0）");

            Section(w, "未识别清单（这些句子不会生效，必须给策划确认）", unrecognized.Count);
            for (int i = 0; i < unrecognized.Count; i++)
            {
                UnrecognizedRule u = unrecognized[i];
                w.WriteLine(" " + (i + 1) + ". [" + u.cardName + " · " + u.field + "]　" + u.cardId);
                w.WriteLine("    原句：" + u.sentence);
                w.WriteLine("    原因：" + u.reason);
                if (!string.IsNullOrEmpty(u.partial)) w.WriteLine("    已认出：" + u.partial);
                w.WriteLine("    建议：" + u.suggestion);
            }

            Section(w, "已识别、但当前模型执行不了", unsupported.Count);
            for (int i = 0; i < unsupported.Count; i++) w.WriteLine(" · " + unsupported[i]);

            Section(w, "占位数值（正文没给数，先用了配置里的默认值）", placeholders.Count);
            for (int i = 0; i < placeholders.Count; i++) w.WriteLine(" · " + placeholders[i]);

            Section(w, "可疑产出（产出的卡名不在卡表里）", unknownProducts.Count);
            for (int i = 0; i < unknownProducts.Count; i++) w.WriteLine(" · " + unknownProducts[i]);

            Section(w, "规则表接不上（标签标了、连锁产物没写：启动时规则命中却不变形）", tableGaps.Count);
            for (int i = 0; i < tableGaps.Count; i++) w.WriteLine(" · " + tableGaps[i]);

            Section(w, "v3 已取消、卡表里还留着的 v2.1 写法（写了也不生效）", superseded.Count);
            for (int i = 0; i < superseded.Count; i++) w.WriteLine(" · " + superseded[i]);

            Section(w, "正文自相矛盾 / 没写清（按一个明确选择实现了，待策划拍板）", conflicts.Count);
            for (int i = 0; i < conflicts.Count; i++) w.WriteLine(" · " + conflicts[i]);

            Section(w, "字段静默丢失（有文本却一句都没解析出来 —— 这是解析器的锅，不是策划的）", silentFields.Count);
            for (int i = 0; i < silentFields.Count; i++) w.WriteLine(" · " + silentFields[i]);
        }

        public void Print() { Print(Console.Out); }

        private static void Section(System.IO.TextWriter w, string title, int count)
        {
            w.WriteLine();
            w.WriteLine("── " + title + "：" + count + " ──");
            if (count == 0) w.WriteLine(" （无）");
        }

        public override string ToString() { return SummaryLine(); }
    }
}
