using System;

namespace GameJam.Data
{
    /// <summary>一张牌是什么。</summary>
    public enum CardKind
    {
        /// <summary>食材牌：投进杯子，参与模拟</summary>
        Ingredient = 0,

        /// <summary>变速模块：投出去就生效，不进杯子</summary>
        Module = 1,

        // 将来要加的种类（比如关卡规则牌、状态牌）在这里追加，
        // 然后把 Card 里那几个 switch 补一条分支即可 ——
        // 其余地方（手牌、界面、投放流程）都不用动。
    }

    /// <summary>
    /// 一张牌。
    ///
    /// 【为什么需要这个类】
    /// 在这之前，"一张牌"在数据层里其实是两个互不相干的类型：
    /// Ingredient 和 SpeedModule。于是"这张是食材还是模块"这个判断
    /// 被抄在了四个文件里 —— TableHud ×5、GameFlow ×5、PlayCard ×3、
    /// TableTurnLoop ×2，一共 15 处 IsModule 分支。
    /// 手牌也因此分成 hand / modules 两个列表，每处都要分别遍历一遍。
    ///
    /// 加第三种牌的时候，那 15 处都得改。
    ///
    /// 现在收拢成这一个类：
    ///   数据层只有一种手牌类型（List&lt;Card&gt;），
    ///   界面不再问"你是哪种"，只读 kind / TypeTag / Effects，
    ///   出牌走唯一的 PlayInto，分支只在这里出现一次。
    ///
    /// 【为什么不用基类 + 继承】
    /// Ingredient 还要当刀片用、要被牌组持有、要被配置表直接反序列化 ——
    /// 让它去继承一个 CardBase 会把"食材"和"手里的牌"两件事绑死。
    /// 所以这里是**组合**：Card 持有 Ingredient 或 SpeedModule，
    /// 两者保持独立，谁都不认识 Card。
    /// </summary>
    [Serializable]
    public class Card
    {
        public string   id;
        public string   name;
        public CardKind kind;

        /// <summary>食材牌背后的数据（不是食材牌时为 null）</summary>
        public Ingredient ingredient;

        /// <summary>模块牌背后的数据（不是模块牌时为 null）</summary>
        public SpeedModule module;

        public Card() { id = ""; name = ""; }

        // ── 构造 ──────────────────────────────────────────────────────

        public static Card Of(Ingredient ing)
        {
            if (ing == null) return null;
            Card c = new Card();
            c.kind = CardKind.Ingredient;
            c.ingredient = ing;
            c.id = ing.id;
            c.name = ing.name;
            return c;
        }

        public static Card Of(SpeedModule mod)
        {
            if (mod == null) return null;
            Card c = new Card();
            c.kind = CardKind.Module;
            c.module = mod;
            c.id = mod.id;
            c.name = mod.name;
            return c;
        }

        // ── 门面：调用方只需要读这几个，不用再问"你是哪种" ─────────────

        public bool IsIngredient { get { return kind == CardKind.Ingredient; } }
        public bool IsModule     { get { return kind == CardKind.Module; } }

        /// <summary>类型标签，界面直接显示。</summary>
        public string TypeTag { get { return IsModule ? "变速模块" : "食材"; } }

        /// <summary>这张牌带的效果。两类都有，取不到就给空组合。</summary>
        public EffectGroup Effects
        {
            get
            {
                if (IsModule)     return module != null ? module.effects : null;
                if (IsIngredient) return ingredient != null ? ingredient.effects : null;
                return null;
            }
        }

        /// <summary>这张牌的属性数值。模块没有自己的属性值（它的数值是加到刀片上的），返回 null。</summary>
        public AttrSet Attrs
        {
            get { return IsIngredient && ingredient != null ? ingredient.attrs : null; }
        }

        /// <summary>一行描述：食材写三属性摘要，模块写效果。</summary>
        public string Describe()
        {
            if (IsModule)
                return module != null ? module.Description() : "（无效果）";

            if (ingredient != null && ingredient.attrs != null)
                return ingredient.attrs.DescribeLabeled();

            return "（无数据）";
        }

        // ── 出牌 ──────────────────────────────────────────────────────

        /// <summary>
        /// 把这张牌打出去。
        ///
        /// ★★ 整个工程里"食材还是模块"的分支，**只应该在这里出现一次** ★★
        ///    食材：进杯子 + 自带效果并进本局效果
        ///    模块：效果并进本局效果 + 记进已应用列表（不进杯子）
        ///
        /// 在这里加第三种牌时，只需要给这个 switch 补一条。
        /// </summary>
        public void PlayInto(TurnState turn)
        {
            if (turn == null) return;

            switch (kind)
            {
                case CardKind.Ingredient:
                    if (ingredient == null) return;

                    if (turn.cup != null) turn.cup.Add(ingredient);

                    // 食材自带的 effects 也要并进来 —— 和模块对称。
                    // 这条曾经漏过：activeEffects 的注释一直写着"含食材自带效果"，
                    // 但 PlayFromHand 从来没 Append 过，食材的效果一直是丢的。
                    if (ingredient.effects != null) turn.activeEffects.Append(ingredient.effects);
                    break;

                case CardKind.Module:
                    if (module == null) return;

                    if (module.effects != null) turn.activeEffects.Append(module.effects);
                    turn.appliedModules.Add(module);
                    break;
            }
        }

        // ── 杂项 ──────────────────────────────────────────────────────

        public Card Clone()
        {
            if (IsModule)     return Of(module != null ? module.Clone() : null);
            if (IsIngredient) return Of(ingredient != null ? ingredient.Clone() : null);
            return null;
        }

        public override string ToString()
        {
            return name + "（" + TypeTag + "）";
        }
    }
}
