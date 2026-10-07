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

            // ★ 打包版的自动开局后门（`DSH_AUTOSTART=1`，默认关）。
            //   【为什么必须有】用户连着两次报的都是**打包版**里的操作问题
            //   （"附魔位放不上去"），而在打包版里点菜单点进一关要经过
            //   开场 → 关卡 → 牌组 → 刀片四个环节，复现一次成本极高、还容易点歪。
            //   有了它，`DSH_AUTOSTART=1` 一跑就停在"第 1 回合、什么都没动"的桌面上，
            //   和编辑器探针的起点完全一致 —— 打包版也能被逐帧复现。
            //   AutoStartDeck 是选第几副牌组（默认 0 = 配置里第一副）。
            AutoStart     = System.Environment.GetEnvironmentVariable("DSH_AUTOSTART") == "1";
            string deckEnv = System.Environment.GetEnvironmentVariable("DSH_AUTOSTART_DECK");
            int deckIdx;
            if (!string.IsNullOrEmpty(deckEnv) && int.TryParse(deckEnv, out deckIdx) && deckIdx >= 0)
                AutoStartDeck = deckIdx;
        }

        /// <summary>
        /// 启动后直接进第 1 回合（跳过开场 / 关卡 / 牌组 / 刀片四个环节）。
        /// **默认关**，只有 `DSH_AUTOSTART=1` 时才为真 —— 它不改任何规则，
        /// 只是替玩家把那四下点击按了一遍（走的还是那几个公开入口）。
        /// </summary>
        public static bool AutoStart;

        /// <summary>自动开局选第几副牌组（`DSH_AUTOSTART_DECK=n`，默认 0）。</summary>
        public static int AutoStartDeck;

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
        /// 开局准备环节的**大卡摆不摆到桌面上**（牌组一排 / 关卡一排，同一个开关）。
        ///
        /// 【用户怎么说的】先是关卡：「不要关卡手牌了，就放一个 ui 就行」；
        ///   再是牌组：「这些流派也做成窗口，去掉卡牌」。
        ///   那两排卡都离得远、字小，还占着桌面正中间（6 副牌组时铺满整屏，
        ///   蜡烛从第一张中间穿出来、破壁机压在第 4/5 张上）——
        ///   而关卡窗口 / 牌组窗口一屏就能把每张卡上那几行字列全。
        ///   v2.1 只留窗口；**旧流程那两排卡一行没动**（开关翻过来就原样回来）。
        ///
        /// 【为什么两件事合成一个开关，而不是各留一个】它们是同一条时间线上的同一件事
        ///   （"选择环节怎么选"），而且都等于 `!UseRulesV21`：分成两个的话，
        ///   以后要加第三种模式就得记住"两处都要改"，而漏掉一处的表现是
        ///   "牌组只有窗口、关卡还摆着卡"这种半吊子状态。
        ///   下面两个名字只是**读起来是哪件事**（调用点写 DeckCardsOnTable 比
        ///   写 ChoiceCardsOnTable 更清楚），值永远一致。
        ///
        /// 【为什么收在这里】"建不建卡"（TableTurnLoop）和"提示文案怎么写"（TableHud）
        ///   问的是同一件事，各写各的话迟早会出现"卡已经没有了、提示还写着点桌上的卡"。
        /// </summary>
        public static bool ChoiceCardsOnTable { get { return !UseRulesV21; } }

        /// <summary>牌组是不是摆成桌上的一排 3D 大卡（= 上面的总开关）。</summary>
        public static bool DeckCardsOnTable { get { return ChoiceCardsOnTable; } }

        /// <summary>关卡是不是摆成桌上的一排 3D 大卡（= 上面的总开关）。</summary>
        public static bool LevelCardsOnTable { get { return ChoiceCardsOnTable; } }

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
