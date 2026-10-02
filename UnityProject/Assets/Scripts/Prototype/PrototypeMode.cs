namespace GameJam.Prototype
{
    /// <summary>
    /// 原型模式开关。
    ///
    /// 现在有三套原型，互相独立、共用一个数据层（GameConfig）：
    ///   Flow2D  —— IMGUI 纯文字流程原型（验证状态流转，最稳）
    ///   Table3D —— 3D 桌面原型（碰撞 / 摆放 / 视角切换）
    ///   Board2D —— 2D 棋盘原型（左破壁机 / 中打出区 / 右客人杯子 / 下手牌）
    ///
    /// Table3D 和 Board2D 是**同一个玩法的两种表现形式**，
    /// 数据、规则、配置表完全共用，只是画法不同 ——
    /// 所以它们不是"新旧版本"，而是两条并行的分支，随时可以来回切。
    ///
    /// 每个 Bootstrap 都读这个值，只启动对应的那一套。
    /// 想切换就改下面这一行，然后重新 Play。
    /// </summary>
    public static class PrototypeMode
    {
        public enum Kind
        {
            /// <summary>IMGUI 文字流程原型</summary>
            Flow2D = 0,

            /// <summary>3D 桌面原型（分支 A）</summary>
            Table3D = 1,

            /// <summary>2D 棋盘原型（分支 B）</summary>
            Board2D = 2,
        }

        /// <summary>当前启用哪套原型。改这里切换。</summary>
        public static Kind Current = Kind.Board2D;
    }
}
