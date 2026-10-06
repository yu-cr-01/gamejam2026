using System;
using GameJam.Data;

namespace GameJam.Rules
{
    /// <summary>
    /// 刀片：整局的核心状态。
    ///
    ///    H（硬度/珍稀度）—— 本关的启动次数池，每启动一次 -1，归零就是**爆刀**。
    ///    V（得分）      —— 得分加成，每次启动得分 = 目标素材V + 刀片V。
    ///    层数           —— 四种附魔层（热/冷/酸/催化），规则的绝大部分产出与消耗都落在这里。
    ///
    /// 【为什么 H 和层数放在一个对象里】
    ///   规则里"扣 H 换分数"（酸爆）、"层数翻倍"（盐）、"附魔层数≥3 触发"（冰/碳/铜/铁）
    ///   这些条件同时读 H 和层数，把它们拆成两个对象会让每条规则都要拿两个引用，
    ///   而且"爆刀"这种跨两者的判定没有明确归属。这里合成一个 BladeState，
    ///   由它自己回答 IsBursted / Describe —— 结算代码只需要问它。
    ///
    /// 【V 是 v3 正文补上的】
    ///   《策划书总结 v3》§2.2：素材作为刀片核心或并入刀片时 H 全额加到刀片 H，
    ///   V 同理加成；§四.3：每次启动得分 = 目标素材V + 刀片V。
    ///   所以刀片也要持有 V，不能只算素材那一半。
    /// </summary>
    public class BladeState
    {
        /// <summary>刀片卡 id（就是当前装在刀片槽里的那张素材）</summary>
        public string cardId = "";

        /// <summary>显示名</summary>
        public string name = "";

        /// <summary>硬度 / 启动次数池。≤0 = 爆刀。</summary>
        public int H;

        /// <summary>得分加成（刀片核心卡的 V + 所有被吞噬素材的 V）。</summary>
        public int V;

        /// <summary>四种附魔层数</summary>
        public readonly LayerLedger layers = new LayerLedger();

        public BladeState() { }

        public BladeState(string cardId, string name, int h)
        {
            this.cardId = cardId;
            this.name = name;
            this.H = h;
        }

        public BladeState(string cardId, string name, int h, int v)
        {
            this.cardId = cardId;
            this.name = name;
            this.H = h;
            this.V = v;
        }

        public bool IsBursted { get { return H <= 0; } }

        /// <summary>扣 H（酸爆、素材反噬、启动消耗、爆刀前的那一下都走这里）。返回实际扣掉的值。</summary>
        public int Damage(int amount)
        {
            if (amount <= 0) return 0;
            int before = H;
            H -= amount;
            return before - H;
        }

        /// <summary>加 H（硬化、汞启动、硫磺粉献祭、吞噬素材）。</summary>
        public void Harden(int amount)
        {
            if (amount > 0) H += amount;
        }

        /// <summary>加 V（吞噬素材并入刀片）。</summary>
        public void AddV(int amount)
        {
            if (amount > 0) V += amount;
        }

        /// <summary>这层附魔是否"有"（规则里写「若刀片有热附魔」）。</summary>
        public bool HasEnchant(LayerKind kind) { return layers.Has(kind); }

        public BladeState Clone()
        {
            BladeState b = new BladeState(cardId, name, H, V);
            // layers 是 readonly 字段，逐类拷过去
            for (int i = 0; i < LayerLedger.KindCount; i++)
            {
                LayerKind k = (LayerKind)i;
                LayerStack s = layers.Stack(k);
                b.layers.Add(k, s.decaying);
                b.layers.Add(k, s.permanent, true);
            }
            return b;
        }

        public string Describe()
        {
            return name + " H=" + H + " V=" + V + (IsBursted ? "（已爆刀）" : "") + "　" + layers.Describe();
        }
    }

    /// <summary>
    /// 场上的一张素材（桌面上的）。
    ///
    ///   H  —— 珍稀度/耐久。酸蚀规则会扣它（金属 -2、固体 -1），
    ///         被刀片吞噬时全额加到刀片 H 上。
    ///   D  —— 使用次数。启动结算完若没发生形态变化就 -1；≤0 触发「D耗尽」。
    ///   V  —— 得分。每次启动的得分里含它。
    ///
    /// 【为什么 H/D 都放在这里】
    ///   v3 正文把三属性写死了：H 是"作为刀片核心或并入刀片时才动"的存量，
    ///   D 是"在桌面上被启动的次数"，两者都只在素材的运行时状态里变化，
    ///   属性表（AttrSet）里那份是卡面基础值，不该被结算改。
    /// </summary>
    public class MaterialState
    {
        /// <summary>当前形态对应的卡（形态变化后换成新卡，新卡从手牌出）</summary>
        public Ingredient card;

        /// <summary>耐久/使用次数，归零 = D 耗尽</summary>
        public int D = 3;

        /// <summary>这张卡的 D 满值（"形态变化后新卡 D 重置为满"要用它）</summary>
        public int fullD = 3;

        /// <summary>珍稀度（酸蚀扣的就是它；被刀片吞噬时加到刀片 H）</summary>
        public int H;

        /// <summary>得分值（每次启动得分 = 这张卡的 V + 刀片 V）</summary>
        public int V;

        /// <summary>这张卡已经从桌面移除（形态变化 / 溶解 / D耗尽 / 被吞噬）</summary>
        public bool removed;

        /// <summary>这张卡还会在场上待几个回合（旧的兼容字段，v3 流程用 removed）</summary>
        public bool consumed;

        public MaterialState(Ingredient card, int d)
        {
            this.card = card;
            this.D = d;
            this.fullD = d;
        }

        public MaterialState(Ingredient card, int d, int h)
        {
            this.card = card;
            this.D = d;
            this.fullD = d;
            this.H = h;
        }

        public MaterialState(Ingredient card, int d, int h, int v)
        {
            this.card = card;
            this.D = d;
            this.fullD = d;
            this.H = h;
            this.V = v;
        }

        public string id { get { return card != null ? card.id : ""; } }
        public string name { get { return card != null ? card.name : ""; } }

        /// <summary>还在桌面上（没被移除）</summary>
        public bool OnTable { get { return !removed; } }

        public bool IsExhausted { get { return D <= 0; } }

        /// <summary>扣 D，返回是否因此耗尽。</summary>
        public bool Deplete(int amount)
        {
            if (amount <= 0) return IsExhausted;
            D -= amount;
            return IsExhausted;
        }

        /// <summary>扣 H（酸蚀：金属 -2、固体 -1）。返回实际扣掉的值。</summary>
        public int DamageH(int amount)
        {
            if (amount <= 0) return 0;
            int before = H;
            H -= amount;
            return before - H;
        }

        public bool HasTag(string tag) { return card != null && card.HasTag(tag); }

        public string Describe()
        {
            return name + "（H=" + H + " D=" + D + "/" + fullD + " V=" + V +
                   (card != null && !string.IsNullOrEmpty(card.form) ? " · " + card.form : "") +
                   (removed ? " · 已移出桌面" : "") + "）";
        }
    }
}
