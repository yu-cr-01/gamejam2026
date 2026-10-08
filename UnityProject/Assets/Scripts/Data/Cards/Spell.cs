using System;

namespace GameJam.Data
{
    /// <summary>法术作用到哪。目前只有刀片一种，留着是为了以后加"作用于杯子"这类。</summary>
    public enum SpellTarget
    {
        /// <summary>作用在当前刀片上（硬化术 = 使刀片 H＋10）</summary>
        Blade = 0,
    }

    /// <summary>
    /// 法术牌。
    ///
    /// 【和素材的区别】
    ///   素材 —— 有 H/D/V 三属性和形态，投进杯子参与反应，会被刀片攻击
    ///   法术 —— 没有属性、不进杯子、不参与反应，打出来就立刻并入本局效果
    ///
    /// 【★ 一个发现：变速模块和法术其实是同一种机制】
    ///   过载引信「硫性 +3」和硬化术「刀片 H＋10」，
    ///   拆开看都是"打出来 → 往 activeEffects 里塞一条效果"。
    ///   所以新规格里的「法术」和旧的「变速模块」在实现上是同一个东西，
    ///   区别只在叫法和文案。
    ///
    ///   这也是为什么 Spell 直接复用 EffectGroup 而不是自己发明一套 ——
    ///   效果系统本来就是为"多个效果叠起来"设计的（Append 会依次作用），
    ///   法术叠法术、法术叠食材，都走同一条路。
    ///
    /// 【为什么还是单独一个类，不复用 SpeedModule】
    /// 机制像，语义不同。模块是"机器转速"，法术是"你打出的牌"。
    /// 混用的话，以后法术要加"只在本回合生效""消耗一次启动机会"这类规则，
    /// 就会被模块的语义绑住。所以类型分开、底层都走 EffectGroup。
    /// </summary>
    [Serializable]
    public class Spell
    {
        /// <summary>唯一标识</summary>
        public string id;

        /// <summary>显示名，例如 "硬化术"</summary>
        public string name;

        /// <summary>作用到哪</summary>
        public SpellTarget target = SpellTarget.Blade;

        /// <summary>效果组合。描述由它自动生成，不手写，免得文案和数值脱节。</summary>
        public EffectGroup effects;

        // ── 卡牌需求设计 v2.1 的描述性字段（见 Ingredient 里那段说明）──────

        /// <summary>规格里的系列 / 分组，例如 "法术"、"通用与特殊"。</summary>
        public string series = "";

        /// <summary>
        /// 卡牌类型：法术（v3.0 原文第 2 列「类型」）。
        /// 卡表里写的是「法术」，这里原样存；不写就默认「法术」——
        /// 因为能走到这个类的就一定是法术（素材走 Ingredient）。
        /// </summary>
        public string cardType = "法术";

        /// <summary>
        /// 稀有度：普通 / 稀有 / 传说（v3.0）。
        /// ★ 法术**没有 h/d/v**（v3.0 原文：「法术卡不标 H/D/V，只标稀有度」），
        ///   所以稀有度就是法术上唯一的数值口径 —— 卡面、图鉴都靠它显示强度。
        /// </summary>
        public string rarity = "";

        /// <summary>附魔类型：热 / 冷 / 酸 / 催化；不用附魔的法术留空。</summary>
        public string enchant = "";

        /// <summary>分类，例如 "直接得分"；没有就空串。</summary>
        public string category = "";

        /// <summary>需求（效果原文），例如 "附魔到刀片，可叠加"。</summary>
        public string requirement = "";

        /// <summary>来源（哪些卡 D 耗尽 / 献祭产出的），原文列表。</summary>
        public string[] sources = new string[0];

        public Spell()
        {
            id = "";
            name = "";
            effects = new EffectGroup();
        }

        public Spell(string id, string name)
        {
            this.id = id;
            this.name = name;
            effects = new EffectGroup();
        }

        /// <summary>链式挂效果：new Spell("harden", "硬化术").With(AttrId.Salt, 10)</summary>
        public Spell With(AttrId target, EffectOp op, int value)
        {
            // ★ Append 只收 EffectGroup，不收单条 Effect —— 要包一层。
            //   直接 Append(new Effect(...)) 编译不过（试过了）。
            EffectGroup g = new EffectGroup(name);
            g.Add(target, op, value);
            effects.Append(g);
            return this;
        }

        public Spell With(AttrId target, int value)
        {
            return With(target, EffectOp.Add, value);
        }

        /// <summary>效果描述，自动生成，例如 "盐性 +10"</summary>
        public string Description()
        {
            return effects != null ? effects.Describe() : "（无效果）";
        }

        /// <summary>界面上的完整一行，例如 "使刀片 盐性 +10"</summary>
        public string FullText()
        {
            string where = target == SpellTarget.Blade ? "使刀片 " : "使 ";
            return where + Description();
        }

        /// <summary>打出去：效果并进本局。</summary>
        public void Cast(TurnState turn)
        {
            if (turn == null || effects == null) return;
            turn.activeEffects.Append(effects);
        }

        public Spell Clone()
        {
            Spell s = new Spell(id, name);
            s.target = target;
            s.effects = effects != null ? effects.Clone() : new EffectGroup();
            s.series = series;
            s.cardType = cardType;
            s.rarity = rarity;
            s.enchant = enchant;
            s.category = category;
            s.requirement = requirement;
            s.sources = sources != null ? (string[])sources.Clone() : new string[0];
            return s;
        }

        /// <summary>
        /// 卡面上要显示的一行：附魔类型 + 分类 + 稀有度，例如 "附魔：热 · 稀有"。
        ///
        /// 【为什么把稀有度放在这里】v3.0 的法术**没有 H/D/V**，
        ///   卡面属性区那三个格子对法术是空的（见 CardFactory：模块壳不画数字）。
        ///   如果稀有度也不显示，玩家在卡面上就看不出这张法术强不强 ——
        ///   而稀有度是 v3.0 给法术的唯一强度口径。所以它必须有一个能看见的落点。
        /// </summary>
        public string KindText()
        {
            string s = "";
            if (!string.IsNullOrEmpty(enchant)) s = "附魔：" + enchant;
            if (!string.IsNullOrEmpty(category)) s = string.IsNullOrEmpty(s) ? category : s + " · " + category;
            if (!string.IsNullOrEmpty(rarity))   s = string.IsNullOrEmpty(s) ? rarity : s + " · " + rarity;
            return s;
        }

        /// <summary>卡面正文：优先用规格里的「需求」原文，没有才退回自动生成的效果描述。</summary>
        public string CardText()
        {
            if (!string.IsNullOrEmpty(requirement)) return requirement;
            return Description();
        }

        public override string ToString()
        {
            return name + "（" + Description() + "）";
        }
    }

    /// <summary>
    /// 法术表。
    ///
    /// 【为什么先写在这一份代码里，不直接进配置表】
    /// 配置表那边现在装的是"食材 / 牌组 / 模块"，法术是新概念，
    /// 等牌组结构定下来（每副牌带几张法术）再一起挪进 JSON。
    /// 现在先有个能跑的表，Format 和 EffectGroup 都已经是配置友好的形状
    /// （一串属性 + 运算 + 数值），挪过去不用改结构。
    /// </summary>
    public static class SpellCatalog
    {
        private static System.Collections.Generic.List<Spell> cached;

        public static System.Collections.Generic.List<Spell> All()
        {
            if (cached != null) return cached;

            cached = new System.Collections.Generic.List<Spell>();

            // 硬化术：使刀片 H＋10
            // H 就是盐性（固体倾向），刀片硬度一直走的是这一项
            cached.Add(new Spell("harden", "硬化术").With(AttrId.Salt, 10));

            return cached;
        }

        public static Spell Find(string id)
        {
            System.Collections.Generic.List<Spell> all = All();
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].id == id) return all[i];
            return null;
        }

        public static void Reload() { cached = null; }
    }
}
