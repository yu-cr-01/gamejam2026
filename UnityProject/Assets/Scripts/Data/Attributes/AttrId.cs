namespace GameJam.Data
{
    /// <summary>
    /// 属性 ID。
    ///
    /// 【为什么单独一个枚举】
    /// 原来 6 个属性是写死在 Ingredient 上的 6 个 int 字段，
    /// 想加一个新属性（比如"辣度""黏度"）就得改 Ingredient、改所有出效果的地方。
    /// 现在属性是一个 ID，食材只持有一个 AttrSet，加属性只改这里 + AttrCatalog。
    ///
    /// 数值必须连续从 0 开始 —— AttrSet 用 (int)AttrId 当下标。
    /// </summary>
    public enum AttrId
    {
        Hardness    = 0,   // 硬度
        Temperature = 1,   // 温度
        Acidity     = 2,   // 酸性
        Sugar       = 3,   // 糖分
        Oil         = 4,   // 油脂
        Water       = 5,   // 水分
        // 以后追加新属性写在这里，并在 AttrCatalog 里补一条定义
    }
}
