using System;
using System.Collections.Generic;
using System.IO;
using GameJam.Data;

namespace GameJam.Rules
{
    /// <summary>一次附魔检查的结算类型（正文 §四.2 的三种分支）。</summary>
    public enum EnchantOutcome
    {
        /// <summary>无变化（规则表里的"其他 / 无变化"行）：继续检查下一个附魔类型。</summary>
        None = 0,

        /// <summary>形态变化：旧卡移出桌面、新卡进手牌、新卡 D 重置为满；
        /// 本次启动不再对旧卡 D-1；**素材结算结束，不再检查后续附魔**。</summary>
        FormChange = 1,

        /// <summary>溶解移除：旧卡移出桌面、不产生副产物；素材结算结束。</summary>
        Dissolve = 2,

        /// <summary>仅修改属性（如 H-1 / H-2 / 刀片热+1）：**继续检查下一个附魔类型**。</summary>
        AttributeOnly = 3,

        /// <summary>析出副产物：副产物进手牌，目标保留（按"仅改属性"继续检查）。</summary>
        ByProduct = 4,
    }

    /// <summary>一次附魔检查的结果。</summary>
    public class EnchantMatch
    {
        /// <summary>有没有命中规则（"无变化 / 其他"不算命中）。</summary>
        public bool triggered;

        public LayerKind kind;

        /// <summary>命中的是规则表里第几优先级（数字越小越优先）。</summary>
        public int priority;

        /// <summary>规则名：燃烧 / 爆炸 / 液体→气态 / 金属→溶液 …</summary>
        public string ruleName = "";

        /// <summary>条件原文（日志里说明"为什么命中"）。</summary>
        public string conditionText = "";

        public EnchantOutcome outcome = EnchantOutcome.None;

        /// <summary>形态变化的主产物 / 析出的副产物（卡名）。</summary>
        public string productName = "";

        /// <summary>额外产物（"火焰、碳"里的第二个）。</summary>
        public readonly List<string> extraProducts = new List<string>();

        /// <summary>目标 H 变化（酸性：金属 -2、固体 -1）。</summary>
        public int targetHDamage;

        /// <summary>爆炸：目标 H 归零。</summary>
        public bool setTargetHZero;

        /// <summary>燃烧：目标 D 立即归零。</summary>
        public bool setTargetDZero;

        /// <summary>爆炸：刀片热层数 +1。</summary>
        public int heatLayerGain;

        /// <summary>酸优先级3：H 归零后要变"该卡指定的粉末形态"。</summary>
        public bool needsPowderProduct;

        /// <summary>规则命中了，但卡表没给产物（新增卡牌忘了标连锁产物）。**不许静默**。</summary>
        public bool productMissing;

        /// <summary>
        /// 卡表明说这条反应"无变化"（例如「遇酸 + 酸 → 无变化（不溶于酸）」）：
        /// 规则算命中，但结果就是不变 —— 既不是缺口，也不该变形。
        /// </summary>
        public bool declaredNoChange;

        public string note = "";

        public bool ChangedShape { get { return outcome == EnchantOutcome.FormChange || outcome == EnchantOutcome.Dissolve; } }

        public string Describe()
        {
            if (!triggered) return "没有命中规则（" + (string.IsNullOrEmpty(conditionText) ? "无变化" : conditionText) + "）";

            string s = "优先级" + priority + "「" + ruleName + "」";
            if (declaredNoChange) s += " → 该卡声明无变化";
            else if (!string.IsNullOrEmpty(productName)) s += " → " + productName;
            else if (productMissing) s += "（⚠ 卡表没给产物）";
            return s;
        }
    }

    /// <summary>
    /// 附魔规则表 —— 正文「附魔规则表」B/C/D/E 三节的硬编码实现。
    ///
    /// 【为什么是硬编码表，而不是从卡牌文本解析】
    ///   v2.1 的写法是每张卡自己写"易燃 + 热 → 火焰"，v3 正文改成了**统一规则表**：
    ///   触发条件只看三件事 —— 附魔类型 + 层数阈值 + 目标标签/形态。
    ///   卡牌只负责"指定自己的产物"（卡表的 transitions 就是这份产物表）。
    ///   所以这里查表，产物从卡自己的 transitions 里取。
    ///
    /// 【检查顺序与"只触发一条"】正文 A 节：
    ///   热 → 冷 → 酸 → 催化（催化不直接改素材，只降阈值）。
    ///   每种附魔内按优先级从高到低，**只触发最高优先级的一条**。
    ///   · 形态变化 / 溶解 → 素材结算结束，不再检查后面的附魔
    ///   · 仅改属性 → 继续检查下一个附魔类型
    ///
    /// 【两处正文没写清、这里按最保守方式实现（已在回报里列出）】
    ///   1. "目标为溶液"：v3 的标签分类里没有"溶液"，卡表里也没有这个标签。
    ///      这里按 **卡名以「溶液」结尾 或 带「溶液」标签** 判定。
    ///   2. "目标为金属"：v3 的标签分类里没有"金属"，但规则表用它、卡表里也标了。
    ///      这里按 **带「金属」标签** 判定。
    /// </summary>
    public static class EnchantRules
    {
        /// <summary>素材侧的检查顺序（催化只修阈值，不在这里）。</summary>
        public static readonly LayerKind[] MaterialCheckOrder =
        {
            LayerKind.Heat, LayerKind.Cold, LayerKind.Acid,
        };

        public static string OutcomeName(EnchantOutcome o)
        {
            switch (o)
            {
                case EnchantOutcome.FormChange:    return "形态变化";
                case EnchantOutcome.Dissolve:      return "溶解移除";
                case EnchantOutcome.AttributeOnly: return "仅改属性";
                case EnchantOutcome.ByProduct:     return "析出副产物";
            }
            return "无变化";
        }

        public static bool IsMetal(Ingredient c) { return c != null && c.HasTag("金属"); }

        /// <summary>"溶液"：正文没定义这个类别，按卡名后缀 / 标签判定（见类注释）。</summary>
        public static bool IsSolution(Ingredient c)
        {
            if (c == null) return false;
            if (c.HasTag("溶液")) return true;
            return c.name != null && c.name.EndsWith("溶液", StringComparison.Ordinal);
        }

        public static bool IsForm(Ingredient c, string form) { return c != null && c.form == form; }

        /// <summary>
        /// 查表：这次启动里，<paramref name="kind"/> 这种附魔对 <paramref name="target"/> 触发哪一条。
        /// <paramref name="reactions"/> 是这张卡自己的产物表（由卡表 transitions 解析而来）。
        /// </summary>
        public static EnchantMatch Check(LayerKind kind, BladeState blade, MaterialState target, List<TransitionRule> reactions)
        {
            EnchantMatch m = new EnchantMatch { kind = kind };

            if (blade == null || target == null || target.card == null) return m;

            int layers = blade.layers.Count(kind);
            if (layers <= 0)
            {
                m.conditionText = LayerLedger.Name(kind) + "×0（没有这层附魔）";
                return m;
            }

            switch (kind)
            {
                case LayerKind.Heat:  return CheckHeat(layers, blade, target.card, reactions, m);
                case LayerKind.Cold:  return CheckCold(layers, blade, target.card, reactions, m);
                case LayerKind.Acid:  return CheckAcid(layers, blade, target.card, reactions, m);
            }

            // 催化附魔不直接改变素材（正文 A 节）
            m.conditionText = "催化只修正其他附魔的触发阈值，不直接改变素材";
            return m;
        }

        // ── B、热附魔规则（7 条，优先级从高到低）────────────────────────

        private static EnchantMatch CheckHeat(int layers, BladeState blade, Ingredient c, List<TransitionRule> rx, EnchantMatch m)
        {
            int t1 = blade.layers.LowerThreshold(1);
            int t2 = blade.layers.LowerThreshold(2);
            int t3 = blade.layers.LowerThreshold(3);
            string th = "（实际阈值：热≥" + t1 + "/" + t2 + "/" + t3 + "，催化×" + blade.layers.Count(LayerKind.Catalyst) + "）";

            // 1. 热≥1 且 易燃 → 燃烧：D 立即归零，变为该卡指定的燃烧产物
            //
            //  ★ 这里多了一条正文没写的限定「且 不是粉末」——**按策划意图实现，待策划确认**。
            //    正文的矛盾：规则1（热≥1 且 易燃）的优先级高于规则2（热≥1 且 粉末且易燃），
            //    而"粉末 + 易燃"必然同时满足这两条 → 规则2「爆炸」永远轮不到（死代码）。
            //    策划的意图明显是"粉末 + 易燃 = 爆炸"，所以让规则1 只管非粉末、规则2 只对粉末生效。
            //    **优先级顺序仍然保持正文原样（1 在 2 之前）**，只加了限定条件，
            //    所以：白磷（固体+易燃）照旧燃烧；粉末+易燃 走爆炸。
            //    如果策划确认要按字面实现（即接受爆炸不可达），把 `&& !IsForm(c, "粉末")` 去掉即可。
            if (layers >= t1 && c.HasTag("易燃") && !IsForm(c, "粉末"))
            {
                m.priority = 1;
                m.ruleName = "燃烧（易燃，非粉末）";
                m.conditionText = "热≥" + t1 + " 且 目标有易燃且不是粉末" + th;
                m.setTargetDZero = true;
                if (FindProduct(rx, "易燃", LayerKind.Heat, m) && !m.declaredNoChange)
                {
                    m.note = "产物来自卡表 transitions 的「易燃 + 热」条目" +
                             "；正文的「D立即归零」指旧卡不再参与 D-1（形态变化本来就不 D-1，两者一致）";
                }
                m.triggered = true;
                return m;
            }

            // 2. 热≥1 且 粉末且易燃 → 爆炸：目标 H 归零，刀片热层数 +1，目标变为空白卡
            //    产物是**写死的「空白卡」**（v3 §2.1 的特殊卡：无特性、数值全 0、可并入刀片），
            //    不从卡表 transitions 取 —— 所以卡表里没有"空白卡"这张卡也不会缺产物；
            //    引擎按名字特判成 Blank（空白卡计数 +1），不会报"卡表里找不到这张卡"。
            if (layers >= t1 && IsForm(c, "粉末") && c.HasTag("易燃"))
            {
                m.priority = 2;
                m.ruleName = "爆炸（粉末 + 易燃）";
                m.conditionText = "热≥" + t1 + " 且 目标为粉末且有易燃" + th;
                m.setTargetHZero = true;
                m.heatLayerGain = 1;
                m.productName = "空白卡";
                m.outcome = EnchantOutcome.FormChange;
                m.note = "产物固定为「空白卡」（特殊卡，不走卡表查询）；旧卡 H 归零、刀片热 +1";
                m.triggered = true;
                return m;
            }

            // 3. 热≥2 且 液体 → 该卡指定的气态形态
            if (layers >= t2 && IsForm(c, "液体"))
            {
                m.priority = 3;
                m.ruleName = "液体 → 气态";
                m.conditionText = "热≥" + t2 + " 且 目标为液体" + th;
                if (FindProductAny(rx, LayerKind.Heat, m, "液体", "遇热") && !m.declaredNoChange && !m.productMissing)
                {
                    m.note = "产物来自卡表 transitions（优先「液体 + 热」，没有则退回「遇热 + 热」）";
                }
                m.triggered = true;
                return m;
            }

            // 4. 热≥2 且 遇热 → 该卡指定的热反应
            if (layers >= t2 && c.HasTag("遇热"))
            {
                m.priority = 4;
                m.ruleName = "热反应（遇热）";
                m.conditionText = "热≥" + t2 + " 且 目标有遇热" + th;
                FindProduct(rx, "遇热", LayerKind.Heat, m);
                m.triggered = true;
                return m;
            }

            // 5. 热≥3 且 金属 → 该卡指定的熔融形态
            if (layers >= t3 && IsMetal(c))
            {
                m.priority = 5;
                m.ruleName = "金属 → 熔融";
                m.conditionText = "热≥" + t3 + " 且 目标为金属" + th;
                FindProductAny(rx, LayerKind.Heat, m, "可熔", "遇热", "液体");
                m.triggered = true;
                return m;
            }

            // 6. 热≥3 且 可熔 → 该卡指定的液态形态
            if (layers >= t3 && c.HasTag("可熔"))
            {
                m.priority = 6;
                m.ruleName = "可熔 → 液态";
                m.conditionText = "热≥" + t3 + " 且 目标有可熔" + th;
                FindProductAny(rx, LayerKind.Heat, m, "可熔", "遇热");
                m.triggered = true;
                return m;
            }

            // 7. 其他 → 无变化
            m.conditionText = "热×" + layers + "，" + c.FormAndTags() + " 不满足任何热规则" + th;
            return m;
        }

        // ── C、冷附魔规则（5 条）─────────────────────────────────────

        private static EnchantMatch CheckCold(int layers, BladeState blade, Ingredient c, List<TransitionRule> rx, EnchantMatch m)
        {
            int t2 = blade.layers.LowerThreshold(2);
            int t3 = blade.layers.LowerThreshold(3);
            string th = "（实际阈值：冷≥2→" + t2 + "、冷≥3→" + t3 + "，催化×" + blade.layers.Count(LayerKind.Catalyst) + "）";

            // 1. 冷≥2 且 气体 → 该卡指定的液态形态
            if (layers >= t2 && IsForm(c, "气体"))
            {
                m.priority = 1;
                m.ruleName = "气体 → 液态";
                m.conditionText = "冷≥" + t2 + " 且 目标为气体" + th;
                FindProductAny(rx, LayerKind.Cold, m, "遇冷", "气体");
                m.triggered = true;
                return m;
            }

            // 2. 冷≥3 且 液体 → 该卡指定的固态形态
            if (layers >= t3 && IsForm(c, "液体"))
            {
                m.priority = 2;
                m.ruleName = "液体 → 固态";
                m.conditionText = "冷≥" + t3 + " 且 目标为液体" + th;
                FindProductAny(rx, LayerKind.Cold, m, "遇冷", "液体");
                m.triggered = true;
                return m;
            }

            // 3. 冷≥2 且 溶液 → 析出该卡指定的固态副产物（副产物进手牌，目标保留）
            if (layers >= t2 && IsSolution(c))
            {
                m.priority = 3;
                m.ruleName = "溶液 → 析出固态副产物";
                m.conditionText = "冷≥" + t2 + " 且 目标为溶液" + th;
                if (FindProductAny(rx, LayerKind.Cold, m, "遇冷", "可溶") &&
                    m.outcome == EnchantOutcome.FormChange)   // 声明"无变化"时保持 AttributeOnly
                {
                    m.outcome = EnchantOutcome.ByProduct;
                    m.note = "析出：副产物进手牌，目标保留（正文没写目标是否保留，按「析出」字面取保留）";
                }
                m.triggered = true;
                return m;
            }

            // 4. 冷≥1 且 遇冷 → 该卡指定的冷反应
            if (layers >= blade.layers.LowerThreshold(1) && c.HasTag("遇冷"))
            {
                m.priority = 4;
                m.ruleName = "冷反应（遇冷）";
                m.conditionText = "冷≥" + blade.layers.LowerThreshold(1) + " 且 目标有遇冷" + th;
                FindProduct(rx, "遇冷", LayerKind.Cold, m);
                m.triggered = true;
                return m;
            }

            // 5. 其他 → 无变化
            m.conditionText = "冷×" + layers + "，" + c.FormAndTags() + " 不满足任何冷规则" + th;
            return m;
        }

        // ── D、酸附魔规则（5 条）────────────────────────────────────

        private static EnchantMatch CheckAcid(int layers, BladeState blade, Ingredient c, List<TransitionRule> rx, EnchantMatch m)
        {
            int t1 = blade.layers.LowerThreshold(1);
            int t2 = blade.layers.LowerThreshold(2);
            string th = "（实际阈值：酸≥1→" + t1 + "、酸≥2→" + t2 + "，催化×" + blade.layers.Count(LayerKind.Catalyst) + "）";

            // 1. 酸≥1 且 金属 → 该卡指定的溶液，目标 H-2
            if (layers >= t1 && IsMetal(c))
            {
                m.priority = 1;
                m.ruleName = "金属 → 溶液（H-2）";
                m.conditionText = "酸≥" + t1 + " 且 目标为金属" + th;
                m.targetHDamage = 2;
                FindProductAny(rx, LayerKind.Acid, m, "遇酸", "可溶");
                m.triggered = true;
                return m;
            }

            // 2. 酸≥1 且 可溶 → 该卡指定的溶液
            if (layers >= t1 && c.HasTag("可溶"))
            {
                m.priority = 2;
                m.ruleName = "可溶 → 溶液";
                m.conditionText = "酸≥" + t1 + " 且 目标有可溶" + th;
                FindProductAny(rx, LayerKind.Acid, m, "可溶", "遇酸");
                m.triggered = true;
                return m;
            }

            // 3. 酸≥2 且 固体 → 目标 H-1；若 H 归零则变为该卡指定的粉末形态
            if (layers >= t2 && IsForm(c, "固体"))
            {
                m.priority = 3;
                m.ruleName = "固体 H-1（H 归零 → 粉末）";
                m.conditionText = "酸≥" + t2 + " 且 目标为固体" + th;
                m.targetHDamage = 1;
                m.needsPowderProduct = true;
                m.outcome = EnchantOutcome.AttributeOnly;
                m.triggered = true;
                m.note = "粉末形态：卡表的 transitions 里没有指向粉末的条目，H 归零时只能扣 H、无法变形（数据缺口）";
                return m;
            }

            // 4. 酸≥1 且 粉末 → 溶解移除，不产生副产物
            if (layers >= t1 && IsForm(c, "粉末"))
            {
                m.priority = 4;
                m.ruleName = "粉末 → 溶解移除";
                m.conditionText = "酸≥" + t1 + " 且 目标为粉末" + th;
                m.outcome = EnchantOutcome.Dissolve;
                m.triggered = true;
                return m;
            }

            // 5. 酸≥1 且 遇酸 → 该卡指定的酸反应
            if (layers >= t1 && c.HasTag("遇酸"))
            {
                m.priority = 5;
                m.ruleName = "酸反应（遇酸）";
                m.conditionText = "酸≥" + t1 + " 且 目标有遇酸" + th;
                FindProduct(rx, "遇酸", LayerKind.Acid, m);
                m.triggered = true;
                return m;
            }

            // 其他 → 无变化
            m.conditionText = "酸×" + layers + "，" + c.FormAndTags() + " 不满足任何酸规则" + th;
            return m;
        }

        // ── 产物查找（"该卡指定的X形态" = 卡表 transitions 里对应标签的那一条）──
        //
        //  三种情况要分清（这是"规则命中却不变形"最容易含糊的地方）：
        //    ① 有产物条目      → 形态变化
        //    ② 显式声明"无变化" → 该卡明说没有这个形态：规则仍算命中，但结果是"不变"，
        //                         **不算数据缺口**（卡表用「遇酸 + 酸 → 无变化（不溶于酸）」表达的）
        //    ③ 压根没有这一条   → 数据缺口：productMissing，报进规则解析报告

        private enum ReactionFind { NotFound = 0, Product = 1, NoChange = 2 }

        /// <summary>返回值：true = 这张卡对这条反应**有明确说法**（有产物 或 声明无变化）；false = 卡表没写。</summary>
        private static bool FindProduct(List<TransitionRule> rx, string tag, LayerKind kind, EnchantMatch m)
        {
            string name;
            List<string> extras;
            string reason;
            ReactionFind f = Find(rx, kind, out name, out extras, out reason, tag);

            if (f == ReactionFind.Product)
            {
                m.productName = name;
                m.extraProducts.Clear();
                for (int i = 0; i < extras.Count; i++) m.extraProducts.Add(extras[i]);
                m.outcome = EnchantOutcome.FormChange;
                return true;
            }

            if (f == ReactionFind.NoChange)
            {
                DeclareNoChange(m, tag, kind, reason);
                return true;
            }

            m.productMissing = true;
            m.outcome = EnchantOutcome.AttributeOnly;
            m.note = "卡表 transitions 里没有「" + tag + " + " + LayerLedger.Name(kind) + "」的产物 —— 请补上连锁产物";
            return false;
        }

        private static bool FindProductAny(List<TransitionRule> rx, LayerKind kind, EnchantMatch m, params string[] tags)
        {
            for (int i = 0; i < tags.Length; i++)
            {
                string name;
                List<string> extras;
                string reason;
                ReactionFind f = Find(rx, kind, out name, out extras, out reason, tags[i]);

                if (f == ReactionFind.Product)
                {
                    m.productName = name;
                    m.extraProducts.Clear();
                    for (int e = 0; e < extras.Count; e++) m.extraProducts.Add(extras[e]);
                    m.outcome = EnchantOutcome.FormChange;
                    if (i > 0) m.note = "产物取自「" + tags[i] + " + " + LayerLedger.Name(kind) + "」（首选标签「" + tags[0] + "」没有条目）";
                    return true;
                }

                if (f == ReactionFind.NoChange)
                {
                    DeclareNoChange(m, tags[i], kind, reason);
                    return true;
                }
            }

            m.productMissing = true;
            m.outcome = EnchantOutcome.AttributeOnly;
            m.note = "卡表 transitions 里没有 " + string.Join("/", tags) + " + " + LayerLedger.Name(kind) + " 的产物 —— 请补上连锁产物";
            return false;
        }

        /// <summary>卡表把这条反应声明成"无变化"：规则命中，但结果就是不变。</summary>
        private static void DeclareNoChange(EnchantMatch m, string tag, LayerKind kind, string reason)
        {
            m.declaredNoChange = true;
            m.productMissing = false;
            m.outcome = EnchantOutcome.AttributeOnly;
            m.note = "卡表把「" + tag + " + " + LayerLedger.Name(kind) + "」声明为无变化" +
                     (string.IsNullOrEmpty(reason) ? "" : "：" + reason) +
                     "（按「仅改属性 / 无变化」处理：继续检查下一个附魔类型；规则自带的改属性照常生效）";
        }

        private static ReactionFind Find(List<TransitionRule> rx, LayerKind kind, out string name, out List<string> extras, out string noChangeReason, string tag)
        {
            name = "";
            extras = new List<string>();
            noChangeReason = "";
            if (rx == null) return ReactionFind.NotFound;

            for (int i = 0; i < rx.Count; i++)
            {
                TransitionRule t = rx[i];
                if (t == null || !t.recognized) continue;
                if (t.triggerTag != tag) continue;
                if (t.condition == null || t.condition.layers == null || t.condition.layers.Length == 0) continue;
                if (t.condition.layers[0] != kind) continue;

                if (t.declaredNoChange || t.first == null)
                {
                    noChangeReason = t.noChangeReason;
                    return ReactionFind.NoChange;
                }

                name = t.first.cardName;
                for (int e = 0; e < t.extra.Count; e++) extras.Add(t.extra[e].cardName);
                return ReactionFind.Product;
            }
            return ReactionFind.NotFound;
        }

        // ── 静态体检：卡表接进规则表了吗 ─────────────────────────────

        /// <summary>
        /// 卡表里有没有这条反应的条目 —— **产物条目和"声明无变化"都算有**
        /// （后者是卡表明说"这张卡没有这个形态"，不该再报成缺口）。
        /// </summary>
        public static bool HasProduct(List<TransitionRule> rx, LayerKind kind, params string[] tags)
        {
            string name;
            List<string> extras;
            string reason;
            for (int i = 0; i < tags.Length; i++)
                if (Find(rx, kind, out name, out extras, out reason, tags[i]) != ReactionFind.NotFound) return true;
            return false;
        }

        /// <summary>
        /// 静态体检：**这张卡会被规则表命中，但卡表没给它产物**。
        ///
        /// 正文 G 节说"新增卡牌时，只需在卡牌定义中标注标签、连锁产物、D耗尽产物，
        /// 即可自动接入本规则表" —— 那"标了标签忘了产物"就是数据 bug：
        /// 启动时规则命中却不变形，玩起来像这张卡是死的。这里把它们一次列出来。
        ///
        /// 只报**可达**的规则：优先级更高的规则如果必然先命中，就不再要求产物
        /// （例：有易燃的卡总被热规则1截获，就不该要求它写「液体 + 热」）。
        /// </summary>
        public static List<string> AuditProducts(Ingredient c, List<TransitionRule> rx)
        {
            List<string> gaps = new List<string>();
            if (c == null) return gaps;

            bool flam = c.HasTag("易燃");
            bool hot = c.HasTag("遇热");
            bool melt = c.HasTag("可熔");
            bool metal = IsMetal(c);
            bool sol = IsSolution(c);
            bool soluble = c.HasTag("可溶");
            bool acidTag = c.HasTag("遇酸");
            bool coldTag = c.HasTag("遇冷");

            bool liquid = IsForm(c, "液体");
            bool gas = IsForm(c, "气体");
            bool powder = IsForm(c, "粉末");
            bool solid = IsForm(c, "固体");

            // 热（规则1 带了"非粉末"限定，见 CheckHeat 的注释：把燃烧让给非粉末的易燃卡，
            //     粉末+易燃 走规则2 爆炸，所以这里也不该再要求粉末卡写燃烧产物）
            if (flam && !powder && !HasProduct(rx, LayerKind.Heat, "易燃"))
                gaps.Add("热规则1（燃烧：易燃且非粉末）会命中，但缺「易燃 + 热」的燃烧产物");

            if (liquid && !flam && !HasProduct(rx, LayerKind.Heat, "液体", "遇热"))
                gaps.Add("热规则3（液体→气态）会命中，但缺「液体 + 热」或「遇热 + 热」的产物");

            if (hot && !flam && !liquid && !HasProduct(rx, LayerKind.Heat, "遇热"))
                gaps.Add("热规则4（遇热反应）会命中，但缺「遇热 + 热」的产物");

            if (metal && !flam && !liquid && !hot && !HasProduct(rx, LayerKind.Heat, "可熔", "遇热", "液体"))
                gaps.Add("热规则5（金属→熔融）会命中，但缺「可熔 + 热」的产物");

            if (melt && !flam && !liquid && !hot && !metal && !HasProduct(rx, LayerKind.Heat, "可熔", "遇热"))
                gaps.Add("热规则6（可熔→液态）会命中，但缺「可熔 + 热」的产物");

            // 冷（规则1需要气体、规则2需要液体、规则3需要溶液、规则4门槛最低，各自可达）
            if (gas && !HasProduct(rx, LayerKind.Cold, "遇冷", "气体"))
                gaps.Add("冷规则1（气体→液态）会命中，但缺「遇冷 + 冷」的产物");

            if (liquid && !HasProduct(rx, LayerKind.Cold, "遇冷", "液体"))
                gaps.Add("冷规则2（液体→固态）会命中，但缺「遇冷 + 冷」的产物");

            if (sol && !HasProduct(rx, LayerKind.Cold, "遇冷", "可溶"))
                gaps.Add("冷规则3（溶液→析出副产物，冷=2 这一档）会命中，但缺「遇冷 + 冷」的副产物");

            if (coldTag && !HasProduct(rx, LayerKind.Cold, "遇冷"))
                gaps.Add("冷规则4（遇冷反应）会命中，但缺「遇冷 + 冷」的产物");

            // 酸
            if (metal && !HasProduct(rx, LayerKind.Acid, "遇酸", "可溶"))
                gaps.Add("酸规则1（金属→溶液）会命中，但缺「遇酸 + 酸」的产物（只会扣 H-2，不变形）");

            if (soluble && !metal && !HasProduct(rx, LayerKind.Acid, "可溶", "遇酸"))
                gaps.Add("酸规则2（可溶→溶液）会命中，但缺「可溶 + 酸」的产物");

            if (acidTag && !metal && !soluble && !powder && !HasProduct(rx, LayerKind.Acid, "遇酸"))
                gaps.Add("酸规则5（遇酸反应）会命中，但缺「遇酸 + 酸」的产物");

            return gaps;
        }

        /// <summary>
        /// 正文里**互相矛盾 / 没写清**的地方（原文如此，没有替策划做选择，只列出来）。
        /// 这些都在代码里按"一个明确选择"实现了，选择结果见每条的括号。
        /// </summary>
        public static List<string> Conflicts()
        {
            List<string> list = new List<string>();

            list.Add("【启动是否消耗附魔层】§2.4「启动时不消耗附魔层数」+ §七-7 同义，但规则表 F「启动消耗：启动时无论是否发生效果，消耗1层附魔层数」。" +
                     "引擎按 F 实现：TurnRules.ConsumeLayerOnActivate（默认 true），消耗的是本次**第一个命中规则**的那类附魔、1 层；没命中则从检查顺序里第一个有层的类型扣。待策划拍板。");

            list.Add("【爆炸规则的优先级冲突 —— 已按意图修正，待确认】正文热规则1（热≥1 且易燃）优先级高于热规则2（热≥1 且粉末且易燃），" +
                     "而「粉末 + 易燃」必然同时满足两条 → 按字面实现规则2「爆炸」永远轮不到（死代码）。" +
                     "引擎**按策划意图**给规则1 加了「且不是粉末」限定（优先级顺序仍保持正文原样：1 在 2 之前），" +
                     "于是：非粉末的易燃卡 → 燃烧；粉末+易燃 → 爆炸（H 归零、刀片热+1、变空白卡）。" +
                     "如果策划要按字面实现，去掉 EnchantRules.CheckHeat 规则1 里的 `&& !IsForm(c, \"粉末\")` 即可。");

            list.Add("【『献祭』一词两义】§2.3 的性质标签「献祭」= 被刀片吞噬时触发效果；§四.5 的「献祭吞噬」= 每回合最后一次启动把目标并入刀片。" +
                     "引擎把两者接在一起：并入刀片是动作，卡表的 sacrifice 文本是那张卡被吞噬时触发的效果。");

            list.Add("【燃烧与 D 耗尽两条路径】热规则1 写「燃烧：目标D立即归零，变为该卡指定的燃烧产物」，§四.2 又写「若D≤0 触发D耗尽」。" +
                     "引擎按形态变化处理：燃烧 = 形态变化（产物进手牌、新卡 D 重置为满），不对旧卡再走一次 D耗尽，避免同一张卡产出两次。");

            list.Add("【『溶液』不在标签表里】冷规则3 用「目标为溶液」判定，但 §2.3 的三类标签里没有「溶液」，卡表也没标。" +
                     "引擎按「卡名以溶液结尾 或 带溶液标签」判定。");

            list.Add("【『金属』不在标签表里】热规则5 / 酸规则1 用「目标为金属」判定，但 §2.3 的性质标签列表里没有「金属」（卡表里倒是一直当标签用）。" +
                     "引擎按「带金属标签」判定。");

            list.Add("【溶液在冷附魔下的优先级】溶液卡都是液体，冷≥3 时冷规则2（液体→固态）先于冷规则3（溶液→析出），" +
                     "所以析出只在冷=2 时发生。引擎按优先级实现，实际表现是「冷2 析出、冷≥3 直接凝固」，请确认是否符合预期。");

            list.Add("【规则表字母 E、F 各出现两次】（E：催化附魔规则 / 形态变化与D处理；F：阈值修正示例 / 附魔衰减与冲突）。" +
                     "引用规则表时建议改用编号（如 B-3、D-1），否则口头沟通容易串。");

            list.Add("【『该卡指定的粉末形态』没有数据来源】酸规则3 要求「H 归零则变为该卡指定的粉末形态」，" +
                     "但卡表的 transitions 触发标签里没有『粉末』这一类（只有 易燃/遇热/遇冷/遇酸/可熔/可溶/液体），" +
                     "所以这条子效果现在无法实现：H 归零只会扣 H、不变形。建议给 transitions 的触发加一类『粉末』标签，或改由 D 耗尽/连锁产物表表达。");

            return list;
        }

        // ── 规则表打印（给策划对照）──────────────────────────────────

        public static void PrintTable(TextWriter w)
        {
            w.WriteLine("热附魔（按优先级）");
            w.WriteLine("  1 热≥1 且 易燃（非粉末） → 燃烧：D 归零，变为该卡指定的燃烧产物");
            w.WriteLine("  2 热≥1 且 粉末且易燃   → 爆炸：目标 H 归零，刀片热+1，变为空白卡");
            w.WriteLine("  3 热≥2 且 液体        → 变为该卡指定的气态形态");
            w.WriteLine("  4 热≥2 且 遇热        → 触发该卡指定的热反应");
            w.WriteLine("  5 热≥3 且 金属        → 变为该卡指定的熔融形态");
            w.WriteLine("  6 热≥3 且 可熔        → 变为该卡指定的液态形态");
            w.WriteLine("  7 其他                → 无变化");
            w.WriteLine("冷附魔（按优先级）");
            w.WriteLine("  1 冷≥2 且 气体        → 变为该卡指定的液态形态");
            w.WriteLine("  2 冷≥3 且 液体        → 变为该卡指定的固态形态");
            w.WriteLine("  3 冷≥2 且 溶液        → 析出该卡指定的固态副产物（进手牌）");
            w.WriteLine("  4 冷≥1 且 遇冷        → 触发该卡指定的冷反应");
            w.WriteLine("  5 其他                → 无变化");
            w.WriteLine("酸附魔（按优先级）");
            w.WriteLine("  1 酸≥1 且 金属        → 变为该卡指定的溶液，目标 H-2");
            w.WriteLine("  2 酸≥1 且 可溶        → 变为该卡指定的溶液");
            w.WriteLine("  3 酸≥2 且 固体        → 目标 H-1；H 归零则变粉末形态");
            w.WriteLine("  4 酸≥1 且 粉末        → 溶解移除，不产生副产物");
            w.WriteLine("  5 酸≥1 且 遇酸        → 触发该卡指定的酸反应");
            w.WriteLine("催化附魔：不直接改变素材，只在检查热/冷/酸时降低触发阈值（催≥1 → -1，催≥3 → 再 -1）");
        }
    }
}
