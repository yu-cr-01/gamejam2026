namespace GameJam.Prototype
{
    /// <summary>
    /// 桌面原型的设置项。
    ///
    /// 【为什么是静态类而不是 MonoBehaviour】
    /// 这几个值要被三四个地方读：转头灵敏度在 TableInteraction、
    /// 提示开关在 TableHud、开场和暂停菜单都要能改。做成组件就得在每个
    /// 使用点 FindObjectOfType 一遍，纯属自找麻烦。
    ///
    /// 【这一版是壳子】
    /// 结构先立起来，值有几项、界面长什么样都定了；
    /// 存档、音量、画质这些等真需要了再往里加。
    /// 当前挂上去的三项都是**真的能改东西**的，不是摆着好看。
    /// </summary>
    public static class TableSettings
    {
        /// <summary>右键 / 中键转头灵敏度（度 / 鼠标单位）。</summary>
        public static float LookSensitivity = 3.2f;

        /// <summary>是否显示左下角的操作提示。</summary>
        public static bool ShowHints = true;

        /// <summary>是否显示物理 / 视角这类调试状态。</summary>
        public static bool ShowDebugInfo = false;

        public static void ResetToDefault()
        {
            LookSensitivity = 3.2f;
            ShowHints       = true;
            ShowDebugInfo   = false;
        }
    }
}
