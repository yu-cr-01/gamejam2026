namespace GameJam.Data
{
    /// <summary>
    /// 属性 ID —— 素材与刀片共用的 H / D / V 三属性。
    ///
    /// 【为什么是 3 个而不是 6 个】
    /// 旧版是 6 个按"厨房直觉"起的属性（硬度/温度/酸性/糖分/油脂/水分），
    /// H = 硬度，D = 耐久，V = 计分倾向。
    ///
    /// 数值必须连续从 0 开始 —— AttrSet 用 (int)AttrId 当下标。
    /// </summary>
    public enum AttrId
    {
        /// <summary>硬度。素材用于抗损伤，刀片归零时爆刀。</summary>
        H = 0,

        /// <summary>耐久。素材归零时触发 D 耗尽。</summary>
        D = 1,

        /// <summary>计分倾向。启动得分由目标 V 与刀片 V 相加。</summary>
        V = 2,
    }
}
