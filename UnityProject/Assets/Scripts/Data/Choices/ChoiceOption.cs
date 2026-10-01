using System;

namespace GameJam.Data
{
    /// <summary>
    /// 一个候选项 —— 玩家在某个选择环节里能点的其中一个。
    ///
    /// 【解耦要点】
    /// 选项本身不存牌组 / 食材的数据副本，只存：
    ///   - 展示信息（title / subtitle）
    ///   - 引用 id（payloadId）
    ///   - 运行时的对象引用（payload，不参与序列化）
    ///
    /// 所以同一副牌组既能出现在"三选一"，也能出现在"换牌"界面，
    /// 不用复制两份数据。
    /// </summary>
    [Serializable]
    public class ChoiceOption
    {
        /// <summary>选项 id，例如 "deck_a"</summary>
        public string id;

        /// <summary>主标题，界面按钮上大字显示</summary>
        public string title;

        /// <summary>副标题，一行说明，例如 "铁块、冰块、柠檬 + 高速模块"</summary>
        public string subtitle;

        /// <summary>这个选项背后是什么</summary>
        public ChoiceOptionKind kind;

        /// <summary>引用 id —— 指向 Deck.id / Ingredient.id / SpeedModule.id</summary>
        public string payloadId;

        /// <summary>是否可选（比如未解锁的牌组）</summary>
        public bool enabled = true;

        // ── 运行时解析出来的对象引用（不参与序列化）────────────────────
        // 原型阶段由 FakeData 直接挂上；将来改成读配置表时，
        // 只需要按 payloadId 从 Catalog 里解析出来填在这里，其它代码不用动。

        [NonSerialized] public Deck deck;
        [NonSerialized] public Ingredient ingredient;
        [NonSerialized] public SpeedModule module;

        public ChoiceOption() { id = ""; title = ""; subtitle = ""; }

        public ChoiceOption(string id, string title, ChoiceOptionKind kind, string payloadId = "")
        {
            this.id = id;
            this.title = title;
            this.kind = kind;
            this.payloadId = payloadId.Length > 0 ? payloadId : id;
            this.subtitle = "";
        }

        public ChoiceOption WithSubtitle(string s) { subtitle = s; return this; }
        public ChoiceOption WithDeck(Deck d) { deck = d; kind = ChoiceOptionKind.Deck; return this; }
        public ChoiceOption WithIngredient(Ingredient i) { ingredient = i; return this; }
        public ChoiceOption WithModule(SpeedModule m) { module = m; kind = ChoiceOptionKind.Module; return this; }

        /// <summary>有没有挂上实际对象</summary>
        public bool HasPayload
        {
            get { return deck != null || ingredient != null || module != null; }
        }

        public override string ToString() { return title; }
    }
}
