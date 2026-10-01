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
            return c;
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
