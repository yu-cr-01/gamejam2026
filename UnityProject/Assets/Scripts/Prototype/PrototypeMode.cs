namespace GameJam.Prototype
{
    /// <summary>
    /// 原型模式开关。
    ///
    /// 现在有两套原型：
    ///   Flow2D  —— IMGUI 纯文字流程原型（验证状态流转，最稳）
    ///   Table3D —— 3D 桌面原型（碰撞 / 摆放 / 视角切换，走《邪恶铭刻》方向）
    ///
    /// 两个 Bootstrap 都读这个值，只启动对应的那一套。
    /// 想切换就改下面这一行，然后重新 Play。
    /// </summary>
    public static class PrototypeMode
    {
        public enum Kind
        {
            /// <summary>IMGUI 文字流程原型</summary>
            Flow2D = 0,

            /// <summary>3D 桌面原型</summary>
            Table3D = 1,
        }

        /// <summary>当前启用哪套原型。改这里切换。</summary>
        public static Kind Current = Kind.Table3D;
    }
}
