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
            return s;
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
