using System;

namespace GameJam.Data
{
    /// <summary>
    /// 食材。游戏里一切可投放的东西都用它表示 —— 手牌、刀片、杯内物。
    ///
    /// 【和旧版的区别】
    /// 旧版：6 个写死的 int 字段（hardness / temperature / ...）
    /// 现在：一个 AttrSet + 一个 EffectGroup
    ///
    ///   - 属性走 AttrSet  → 加新属性不用改这个类
    ///   - 效果走 EffectGroup → 食材可以自带效果，能和其他效果组合
    ///
    /// 刀片本质上也是一个 Ingredient，只是用途不同，所以不用单独建类。
    /// </summary>
    [Serializable]
    public class Ingredient
    {
        /// <summary>唯一标识，用于查表、存盘、去重。建议用英文小写，例如 "iron_block"</summary>
        public string id;

        /// <summary>显示名，例如 "铁块"</summary>
        public string name;

        /// <summary>基础属性</summary>
        public AttrSet attrs;

        /// <summary>
        /// 食材自带的效果。投放时参与效果组合。
        /// 今天假数据全部留空，等策划设计。
        /// </summary>
        public EffectGroup effects;

        // ══════════════════════════════════════════════════════════════
        //  卡牌需求设计 v2.1 的描述性字段
        //
        //  这一批不是数值，是**规则文本**：形态 / 标签 / 形态转换 / D耗尽 /
        //  启动 / 献祭。它们来自策划的规格表（Resources/Config/cards_v21.json），
        //  由 CardSpecs 读进来挂在这里。
        //
        //  【为什么不塞进 attrs / effects】
        //    形态转换和献祭是**条件规则**，不是"给某个属性加个值" ——
        //    "易燃 + 热 → 火焰"里既有触发条件又有产出，硬塞进 EffectGroup
        //    会把效果系统变成一个什么都装的袋子。所以先原样存文本，
        //    规则引擎（CardRules）再按自己的模型解释它们。
        //
        //  【对旧数据的影响】
        //    全部有默认值（空），老的 game_config.json 少写这些字段照样能读。
        // ══════════════════════════════════════════════════════════════

        /// <summary>规格里的系列，例如 "水"、"铁"、"通用与特殊"。只用于图鉴分组。</summary>
        public string series = "";

        /// <summary>
        /// 卡牌类型：素材 / 法术（v3.0 原文第 2 列「类型」）。
        ///
        /// 【为什么要单独存一个字，而不是"在 materials 数组里就是素材"】
        ///   产出、D耗尽、形态转换给的都是**中文卡名**，运行时要反查"这是素材还是法术"
        ///   （AddProducedToHand 先查素材表再查法术表就是这个原因）。
        ///   把类型写在卡上，反查时不必依赖"它在哪个数组里"这个隐含约定，
        ///   将来法术也能进同一张表时不用改调用方。
        /// </summary>
        public string cardType = "";

        /// <summary>
        /// 稀有度：普通 / 稀有 / 传说（v3.0）。
        /// 素材这一档 = H 的三档（5 普通 / 10 稀有 / 20 传说），由卡表同时落成字段，
        /// 方便策划对表；卡表漏写时由 <see cref="Prototype.CardSpecs"/> 按 H 反推。
        /// </summary>
        public string rarity = "";

        /// <summary>形态：固体 / 液体 / 气体 / 粉末。空串表示规格没写。</summary>
        public string form = "";

        /// <summary>标签："固体 / 易燃 / 遇热 / 献祭" 这些。规则引擎按标签匹配触发条件。</summary>
        public string[] tags = new string[0];

        /// <summary>形态转换：触发条件 → 变成什么。</summary>
        public FormChange[] transitions = new FormChange[0];

        /// <summary>D 耗尽后变成什么（可能多张）。</summary>
        public string[] exhaust = new string[0];

        /// <summary>启动效果（原文）。</summary>
        public string startup = "";

        /// <summary>献祭效果（原文）。</summary>
        public string sacrifice = "";

        /// <summary>策划备注 / 需求说明（原文）。</summary>
        public string note = "";

        // ── v2.1 的数值（和上面的 attrs 并存，两套模型互不干扰）──────────
        //
        // 【为什么另开三个字段，不塞进 AttrSet】
        //   attrs 是**旧模型**的属性（盐性/汞性/硫性），旧玩法的杯内模拟、刀片、
        //   效果系统全都在用它。v2.1 的 H/D/V 是**新模型**的三属性：
        //     H = 硬度（刀片被打的就是它）　V = 计分用的倾向　D = 耐久（归零 = D耗尽）
        //   两者数值口径不同（例如旧的水是 硫性1，新模型要 V=2）。
        //   硬塞进同一个 AttrSet 会让旧玩法跟着变，所以新开字段，
        //   由 CardSpecs 从 cards_v21.json 填，旧配置一个字节都不用动。
        //
        // 数值来源：正文 §2.2 只给了 V 的档位映射（极低1 低2 中3 高5 极高8），
        // 具体数值"等待第二周设计关卡一并处理" —— 现在卡表里是**占位值**（见 JSON 的 _valuesComment）。
        // ──────────────────────────────────────────────────────────────

        /// <summary>硬度（v2.1）。刀片被打、被酸爆扣的都是它。</summary>
        public int h;

        /// <summary>耐久初始值（v2.1）。归零 = D 耗尽。场上实例的 D 由 MaterialState 持有。</summary>
        public int d;

        /// <summary>计分倾向（v2.1）。每次启动得分 = 目标素材 V + 刀片 V。</summary>
        public int v;

        /// <summary>V 的档位文字（极低/低/中/高/极高），卡表里和 v 一起给，方便策划核对。</summary>
        public string vGrade = "";

        public Ingredient()
        {
            id = "";
            name = "";
            attrs = new AttrSet();
            effects = new EffectGroup();
        }

        public Ingredient(string id, string name, AttrSet attrs)
        {
            this.id = id;
            this.name = name;
            this.attrs = attrs != null ? attrs : new AttrSet();
            this.effects = new EffectGroup();
        }

        /// <summary>链式挂效果：new Ingredient(...).WithEffect(某组合)</summary>
        public Ingredient WithEffect(EffectGroup group)
        {
            if (group != null) effects = group;
            return this;
        }

        /// <summary>链式挂一条效果</summary>
        public Ingredient WithEffect(AttrId target, EffectOp op, int value)
        {
            effects.Add(target, op, value);
            return this;
        }

        /// <summary>
        /// 算出这个食材"实际生效"的属性：基础属性 + 自带效果 + 额外传入的效果组合。
        /// 不改动自身数据。
        /// </summary>
        public AttrSet ResolveAttrs(EffectGroup extra = null)
        {
            AttrSet result = attrs != null ? attrs.Clone() : new AttrSet();
            if (effects != null) effects.Apply(result);
            if (extra != null) extra.Apply(result);
            return result;
        }

        public Ingredient Clone()
        {
            Ingredient c = new Ingredient(
                id,
                name,
                attrs != null ? attrs.Clone() : new AttrSet());
            c.effects = effects != null ? effects.Clone() : new EffectGroup();

            c.series   = series;
            c.cardType = cardType;
            c.rarity   = rarity;
            c.form     = form;
            c.tags     = tags != null ? (string[])tags.Clone() : new string[0];
            c.exhaust  = exhaust != null ? (string[])exhaust.Clone() : new string[0];
            c.startup  = startup;
            c.sacrifice = sacrifice;
            c.note     = note;
            c.h = h; c.d = d; c.v = v; c.vGrade = vGrade;

            if (transitions != null)
            {
                c.transitions = new FormChange[transitions.Length];
                for (int i = 0; i < transitions.Length; i++)
                    c.transitions[i] = transitions[i] != null ? transitions[i].Clone() : new FormChange();
            }
            return c;
        }

        /// <summary>有没有标签（按规格文本精确匹配）。</summary>
        public bool HasTag(string tag)
        {
            if (string.IsNullOrEmpty(tag) || tags == null) return false;
            for (int i = 0; i < tags.Length; i++)
                if (tags[i] == tag) return true;
            return false;
        }

        /// <summary>卡面上要显示的一行：形态 + 标签，例如 "固体 · 易燃 · 遇热"。</summary>
        public string FormAndTags()
        {
            string s = form;
            if (tags != null && tags.Length > 0)
            {
                string t = string.Join(" · ", tags);
                s = string.IsNullOrEmpty(s) ? t : s + " · " + t;
            }
            return s;
        }

        /// <summary>形态转换的整段文字，例如 "易燃 + 热 → 火焰　｜　遇酸 + 酸 → 空白卡"。</summary>
        public string TransitionsText()
        {
            if (transitions == null || transitions.Length == 0) return "";
            string[] parts = new string[transitions.Length];
            for (int i = 0; i < transitions.Length; i++)
            {
                FormChange f = transitions[i];
                if (f == null) continue;
                parts[i] = string.IsNullOrEmpty(f.trigger) ? f.result : f.trigger + " → " + f.result;
            }
            return string.Join("　｜　", parts);
        }

        /// <summary>非零属性描述，例如 "铁块（硬度 10）"</summary>
        public string Describe()
        {
            string a = attrs != null ? attrs.DescribeNonZero() : "（无属性）";
            return name + "（" + a + "）";
        }

        public override string ToString() { return name; }
    }
}
