namespace GameJam.Data
{
    /// <summary>
    /// 效果的运算方式。
    ///
    /// 把"怎么作用"和"作用多少"分开，效果才是可组合的数据。
    /// 加一种新运算只改这里 + Effect.Apply 里的一个 switch 分支。
    /// </summary>
    public enum EffectOp
    {
        /// <summary>加：硬度 +2</summary>
        Add = 0,

        /// <summary>减：硬度 -2</summary>
        Sub = 1,

        /// <summary>按百分比乘：value=150 表示 ×1.5，value=-50 表示 ×0.5</summary>
        MulPercent = 2,

        /// <summary>直接设值：温度 = 100</summary>
        Set = 3,

        /// <summary>下限约束：硬度不低于 5（当前值更高就不动）</summary>
        Min = 4,

        /// <summary>上限约束：硬度不高于 5（当前值更低就不动）</summary>
        Max = 5,
    }
}
