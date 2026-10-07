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
        /// <summary>
        /// 用 v2.1 规则引擎跑回合循环。
        ///
        /// 【为什么是"默认开的开关"而不是直接换掉旧流程】
        ///   v2.1 的回合循环（4 回合 × 5 次行动、出牌不消耗行动、刀片 H/V 无 D、献祭吞噬）
        ///   和旧流程（每回合 2 次行动、出牌算行动、杯内粒子模拟计分）是**两套玩法**，
        ///   不是同一套规则的两个版本。旧流程一行都没删，靠这个开关分流 ——
        ///   关掉就能回到旧流程，方便对着同一张桌子做 A/B 对比。
        ///
        /// 【本分支（feat/rules-v21-turnloop）默认 true】
        ///   整合验收就是要看 v2.1 跑起来。开关在 HUD 的 v2.1 状态区里显示，
        ///   设置面板里能改（改完要重进关卡才生效，见 TableHud 的说明）。
        /// </summary>
        public static bool UseRulesV21 = true;

        /// <summary>
        /// 环境变量 `DSH_RULES_V21=0/1` 覆盖上面的默认值（**启动时读一次**）。
        ///
        /// 【为什么要有这个后门】验证"旧流程还活着"要跑同一份自动试玩两次
        /// （一次 v2.1、一次旧流程）。没有它就得"改代码 → 编译 → 跑 → 改回来"，
        /// 一旦忘了改回来，分支的默认值就被悄悄改掉了 —— 这比多十行代码危险得多。
        ///
        /// 也方便验收：想知道旧流程什么样，不用碰代码，加个环境变量跑一次就行。
        /// </summary>
        static TableSettings()
        {
            string env = System.Environment.GetEnvironmentVariable("DSH_RULES_V21");
            if (env == "0") UseRulesV21 = false;
            else if (env == "1") UseRulesV21 = true;
        }

        /// <summary>
        /// 卡表里还没写数值时用的**占位数值口径**。
        ///
        /// 【为什么这个开关归 TableSettings 而不是散在代码里】
        ///   正文 §2.2 说 H/D/V 的"具体数值等待第二周设计关卡一并处理"，
        ///   所以 cards_v21.json 里可能只有规则文本、没有 h/d/v。
        ///   这种时候整张桌子上所有卡的 H 都是 0 —— 玩家一选刀片核心就爆刀。
        ///   占位数值是唯一能让整条链跑起来的东西，它属于"设置"，不属于规则。
        ///
        /// 值为 true 时：卡表里那张卡三个数全是 0 的话，用 TableRulesV21 里那张
        /// 和离线探针同口径的临时数值表补上，并在 HUD 上标出"占位数值"。
        /// 值为 false 时：老老实实按卡表来（全 0 就是全 0，HUD 会显示"等策划补数值"）。
        /// </summary>
        public static bool UsePlaceholderCardValues = true;

        /// <summary>
        /// v2.1 模式的**关卡目标分**（HUD 上「总分 x / y」里的 y 就是它）。
        ///
        /// 【为什么必须有这个值，而不是继续用 levels[].targetScore】
        ///   game_config.json 的 `levels[].targetScore`（1000 / 1500 / 2000）是**旧流程**的数值：
        ///   旧流程一回合内靠杯内粒子模拟一次结算就能拿几百分，1000 是照那个口径定的。
        ///   而 v2.1 的每次启动得分只有「目标素材 V + 刀片 V」（正文 §四.3），
        ///   V 档位是 1~8，一关 4 回合 × 5 次行动 = 最多 20 次启动 → 满分也就一两百分，
        ///   **永远达不到 1000**：玩家只会看到"4 回合耗尽"，像是规则没生效。
        ///   所以 v2.1 走自己这一个目标分，旧流程继续用 levels[].targetScore，两边互不影响。
        ///
        /// 【60 是怎么来的 —— 这是占位数，待策划定】
        ///   正文 §2.6 只说"达到目标分即胜利"，**没有给任何目标分数值**。
        ///   60 ≈ 一关里正常打完十几次启动的得分（按平均每次 4~6 分估），
        ///   目的是让第一版**能收关**、能验证"达标结束"这条路径。
        ///   正式数值请策划连同 §2.2 的 H/D/V 一起定，然后改这里一处即可。
        /// </summary>
        public static int V21TargetScore = 60;

        /// <summary>
        /// 这一关在当前规则模式下**实际**的目标分 —— 界面上的"目标分"一律走这里。
        ///
        /// 【为什么要有这个函数，而不是各处直接读 lv.targetScore】
        ///   3D 关卡卡、关卡窗口、HUD 顶栏三处都要显示目标分。如果一处读旧数值、一处读 v2.1 的 60，
        ///   同一屏上就会出现"目标分 1000"和"目标分 60"打架（真出现过），玩家只会以为坏了一个。
        ///   判断逻辑收在这里一处，以后加新模式也只改这里。
        /// </summary>
        public static int LevelTargetScore(GameJam.Data.LevelData level)
        {
            if (UseRulesV21) return V21TargetScore;            // v2.1 走专用目标分
            return level != null ? level.targetScore : 0;      // 旧流程读关卡自己的数值
        }

        /// <summary>右键 / 中键转头灵敏度（度 / 鼠标单位）。</summary>
        public static float LookSensitivity = 3.2f;

        /// <summary>是否显示左下角的操作提示。</summary>
        public static bool ShowHints = true;

        /// <summary>是否显示物理 / 视角这类调试状态。</summary>
        public static bool ShowDebugInfo = false;

        public static void ResetToDefault()
        {
            UseRulesV21               = true;
            UsePlaceholderCardValues  = true;
            V21TargetScore            = 60;
            LookSensitivity           = 3.2f;
            ShowHints                 = true;
            ShowDebugInfo             = false;
        }
    }
}
