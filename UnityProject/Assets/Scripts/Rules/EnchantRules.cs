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

        public string note = "";

        public bool ChangedShape { get { return outcome == EnchantOutcome.FormChange || outcome == EnchantOutcome.Dissolve; } }

        public string Describe()
        {
            if (!triggered) return "没有命中规则（" + (string.IsNullOrEmpty(conditionText) ? "无变化" : conditionText) + "）";
            return "优先级" + priority + "「" + ruleName + "」" +
                   (string.IsNullOrEmpty(productName) ? "" : " → " + productName) +
                   (productMissing ? "（⚠ 卡表没给产物）" : "");
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
            if (layers >= t1 && c.HasTag("易燃"))
            {
                m.priority = 1;
                m.ruleName = "燃烧（易燃）";
                m.conditionText = "热≥" + t1 + " 且 目标有易燃" + th;
                m.setTargetDZero = true;
                if (FindProduct(rx, "易燃", LayerKind.Heat, m))
                {
                    m.outcome = EnchantOutcome.FormChange;
                    m.note = "产物来自卡表 transitions 的「易燃 + 热」条目" +
                             "；正文的「D立即归零」指旧卡不再参与 D-1（形态变化本来就不 D-1，两者一致）";
                }
                m.triggered = true;
                return m;
            }

            // 2. 热≥1 且 粉末且易燃 → 爆炸：目标 H 归零，刀片热层数 +1，目标变为空白卡
            if (layers >= t1 && IsForm(c, "粉末") && c.HasTag("易燃"))
            {
                m.priority = 2;
                m.ruleName = "爆炸（粉末 + 易燃）";
                m.conditionText = "热≥" + t1 + " 且 目标为粉末且有易燃" + th;
                m.setTargetHZero = true;
                m.heatLayerGain = 1;
                m.productName = "空白卡";
                m.outcome = EnchantOutcome.FormChange;
                m.triggered = true;
                return m;
            }

            // 3. 热≥2 且 液体 → 该卡指定的气态形态
            if (layers >= t2 && IsForm(c, "液体"))
            {
                m.priority = 3;
                m.ruleName = "液体 → 气态";
                m.conditionText = "热≥" + t2 + " 且 目标为液体" + th;
                if (FindProductAny(rx, LayerKind.Heat, m, "液体", "遇热"))
                {
                    m.outcome = EnchantOutcome.FormChange;
                    m.note = m.productMissing ? "" : "产物来自卡表 transitions（优先「液体 + 热」，没有则退回「遇热 + 热」）";
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
                if (FindProduct(rx, "遇热", LayerKind.Heat, m)) m.outcome = EnchantOutcome.FormChange;
                m.triggered = true;
                return m;
            }

            // 5. 热≥3 且 金属 → 该卡指定的熔融形态
            if (layers >= t3 && IsMetal(c))
            {
                m.priority = 5;
                m.ruleName = "金属 → 熔融";
                m.conditionText = "热≥" + t3 + " 且 目标为金属" + th;
                if (FindProductAny(rx, LayerKind.Heat, m, "可熔", "遇热", "液体")) m.outcome = EnchantOutcome.FormChange;
                m.triggered = true;
                return m;
            }

            // 6. 热≥3 且 可熔 → 该卡指定的液态形态
            if (layers >= t3 && c.HasTag("可熔"))
            {
                m.priority = 6;
                m.ruleName = "可熔 → 液态";
                m.conditionText = "热≥" + t3 + " 且 目标有可熔" + th;
                if (FindProductAny(rx, LayerKind.Heat, m, "可熔", "遇热")) m.outcome = EnchantOutcome.FormChange;
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
                if (FindProductAny(rx, LayerKind.Cold, m, "遇冷", "气体")) m.outcome = EnchantOutcome.FormChange;
                m.triggered = true;
                return m;
            }

            // 2. 冷≥3 且 液体 → 该卡指定的固态形态
            if (layers >= t3 && IsForm(c, "液体"))
            {
                m.priority = 2;
                m.ruleName = "液体 → 固态";
                m.conditionText = "冷≥" + t3 + " 且 目标为液体" + th;
                if (FindProductAny(rx, LayerKind.Cold, m, "遇冷", "液体")) m.outcome = EnchantOutcome.FormChange;
                m.triggered = true;
                return m;
            }

            // 3. 冷≥2 且 溶液 → 析出该卡指定的固态副产物（副产物进手牌，目标保留）
            if (layers >= t2 && IsSolution(c))
            {
                m.priority = 3;
                m.ruleName = "溶液 → 析出固态副产物";
                m.conditionText = "冷≥" + t2 + " 且 目标为溶液" + th;
                if (FindProductAny(rx, LayerKind.Cold, m, "遇冷", "可溶"))
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
                if (FindProduct(rx, "遇冷", LayerKind.Cold, m)) m.outcome = EnchantOutcome.FormChange;
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
                if (FindProductAny(rx, LayerKind.Acid, m, "遇酸", "可溶")) m.outcome = EnchantOutcome.FormChange;
                m.triggered = true;
                return m;
            }

            // 2. 酸≥1 且 可溶 → 该卡指定的溶液
            if (layers >= t1 && c.HasTag("可溶"))
            {
                m.priority = 2;
                m.ruleName = "可溶 → 溶液";
                m.conditionText = "酸≥" + t1 + " 且 目标有可溶" + th;
                if (FindProductAny(rx, LayerKind.Acid, m, "可溶", "遇酸")) m.outcome = EnchantOutcome.FormChange;
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
                if (FindProduct(rx, "遇酸", LayerKind.Acid, m)) m.outcome = EnchantOutcome.FormChange;
                m.triggered = true;
                return m;
            }

            // 其他 → 无变化
            m.conditionText = "酸×" + layers + "，" + c.FormAndTags() + " 不满足任何酸规则" + th;
            return m;
        }

        // ── 产物查找（"该卡指定的X形态" = 卡表 transitions 里对应标签的那一条）──

        private static bool FindProduct(List<TransitionRule> rx, string tag, LayerKind kind, EnchantMatch m)
        {
            string name;
            List<string> extras;
            bool ok = Lookup(rx, kind, out name, out extras, tag);

            if (ok)
            {
                m.productName = name;
                m.extraProducts.Clear();
                for (int i = 0; i < extras.Count; i++) m.extraProducts.Add(extras[i]);
            }
            else
            {
                m.productMissing = true;
                m.outcome = EnchantOutcome.AttributeOnly;
                m.note = "卡表 transitions 里没有「" + tag + " + " + LayerLedger.Name(kind) + "」的产物 —— 请补上连锁产物";
            }
            return ok;
        }

        private static bool FindProductAny(List<TransitionRule> rx, LayerKind kind, EnchantMatch m, params string[] tags)
        {
            for (int i = 0; i < tags.Length; i++)
            {
                string name;
                List<string> extras;
                if (Lookup(rx, kind, out name, out extras, tags[i]))
                {
                    m.productName = name;
                    m.extraProducts.Clear();
                    for (int e = 0; e < extras.Count; e++) m.extraProducts.Add(extras[e]);
                    if (i > 0) m.note = "产物取自「" + tags[i] + " + " + LayerLedger.Name(kind) + "」（首选标签「" + tags[0] + "」没有条目）";
                    return true;
                }
            }

            m.productMissing = true;
            m.outcome = EnchantOutcome.AttributeOnly;
            m.note = "卡表 transitions 里没有 " + string.Join("/", tags) + " + " + LayerLedger.Name(kind) + " 的产物 —— 请补上连锁产物";
            return false;
        }

        private static bool Lookup(List<TransitionRule> rx, LayerKind kind, out string name, out List<string> extras, string tag)
        {
            name = "";
            extras = new List<string>();
            if (rx == null) return false;

            for (int i = 0; i < rx.Count; i++)
            {
                TransitionRule t = rx[i];
                if (t == null || !t.recognized || t.first == null) continue;
                if (t.triggerTag != tag) continue;
                if (t.condition == null || t.condition.layers == null || t.condition.layers.Length == 0) continue;
                if (t.condition.layers[0] != kind) continue;

                name = t.first.cardName;
                for (int e = 0; e < t.extra.Count; e++) extras.Add(t.extra[e].cardName);
                return true;
            }
            return false;
        }

        // ── 静态体检：卡表接进规则表了吗 ─────────────────────────────

        /// <summary>卡表里有没有这条反应的产物条目。</summary>
        public static bool HasProduct(List<TransitionRule> rx, LayerKind kind, params string[] tags)
        {
            string name;
            List<string> extras;
            for (int i = 0; i < tags.Length; i++)
                if (Lookup(rx, kind, out name, out extras, tags[i])) return true;
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

            // 热
            if (flam && !HasProduct(rx, LayerKind.Heat, "易燃"))
                gaps.Add("热规则1（燃烧：易燃）会命中，但缺「易燃 + 热」的燃烧产物");

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

            list.Add("【爆炸规则不可达】热规则1（热≥1 且易燃）优先级高于热规则2（热≥1 且粉末且易燃）：粉末+易燃的卡永远被规则1先截获，" +
                     "规则2「爆炸：目标H归零，刀片热+1，目标变为空白卡」按字面实现永远触发不了。引擎按正文优先级老实实现（爆炸=死代码），建议把爆炸提到优先级1或给规则1加「非粉末」限定。");

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
            w.WriteLine("  1 热≥1 且 易燃        → 燃烧：D 归零，变为该卡指定的燃烧产物");
            w.WriteLine("  2 热≥1 且 粉末且易燃  → 爆炸：目标 H 归零，刀片热+1，变为空白卡");
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
