namespace GameJam.Data
{
    /// <summary>
    /// 候选项的种类 —— 说明这个选项背后是什么东西。
    ///
    /// 有了它，同一个选择界面既能放牌组、也能放刀片、也能放食材 / 模块，
    /// 不用为每种选择各写一套界面和状态。
    /// </summary>
    public enum ChoiceOptionKind
    {
        /// <summary>牌组</summary>
        Deck = 0,

        /// <summary>刀片（本质是食材，但用途不同，单独标出来方便界面区分）</summary>
        Blade = 1,

        /// <summary>普通食材</summary>
        Ingredient = 2,

        /// <summary>变速模块</summary>
        Module = 3,

        /// <summary>纯展示项，选了不产生实际效果（比如"跳过"）</summary>
        Misc = 4,
    }
}
