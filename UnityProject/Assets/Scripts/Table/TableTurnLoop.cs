using System.Collections.Generic;
using UnityEngine;
using GameJam.Data;
using GameJam.Rules;
using GameJam.Sim;

namespace GameJam.Prototype
{
    /// <summary>3D 桌面上的阶段。</summary>
    public enum TablePhase
    {
        /// <summary>开场：桌上摆着书、木牌和一根蜡烛</summary>
        Title,

        /// <summary>关卡界面：桌上一排关卡卡，选一关开打</summary>
        LevelSelect,

        /// <summary>三选一牌组</summary>
        DeckPick,

        /// <summary>选刀片（点手牌即交换）</summary>
        BladePick,

        /// <summary>选牌：可以从手牌拖一张进投放区，再按确认</summary>
        Select,

        /// <summary>模拟中：冲压动画在放，今天不接数值</summary>
        Simulating,

        /// <summary>回合结算</summary>
        TurnResult,

        /// <summary>手牌用完 —— 本关结束</summary>
        LevelEnd,

        /// <summary>本关总结算</summary>
        LevelResult,
    }

    /// <summary>
    /// 3D 桌面上的回合循环 —— 这一份才是"玩法"在桌面上跑起来的驱动源。
    ///
    /// 【它和 Flow2D 那份的关系】
    /// 规则完全相同，共用同一个 TurnState / LevelData / GameConfig：
    ///   三选一牌组 → 选刀片 → 每回合挑 1 张（食材或变速模块）放进投放区
    ///   → 确认投放 → 模拟 → 回合结算 → 下一回合
    ///   手牌用完 → 关卡结束 → 总结算
    /// 区别只在"表现"：那边是 IMGUI 文字，这边是 3D 卡牌 + 榨汁机。
    /// 所以这里绝不能再自己维护一份手牌数据 —— 那样两边迟早会对不上。
    ///
    /// 【桌面上的映射】
    ///   牌组     = 桌中央并排的三张大卡（TableChoiceRig）
    ///   刀片     = 桌中央一张卡，点手牌里的食材即交换
    ///   手牌     = 桌面近端那排卡（食材 + 模块混排）
    ///   投放区   = 桌面中间那 8 个卡槽（每回合只能用一张）
    ///   杯子     = 榨汁机的玻璃罐，液面就是得分
    ///   模拟     = 冲压动画
    /// </summary>
    public class TableTurnLoop : MonoBehaviour
    {
        public TableSetup     setup;
        public JuicerRig      juicer;
        public TableBoard     board;
        public TableChoiceRig choiceRig;

        /// <summary>
        /// v2.1 规则侧总装（刀片 / 桌面素材 / 手牌法术 / 行动机会 / 回合 / 分数）。
        ///
        /// 【什么时候是 null】
        ///   只有 TableSettings.UseRulesV21 为假、或者装配还没走到 BuildRulesV21 时才是 null。
        ///   旧流程一行都不碰它 —— 所有 v2.1 的分流点都先看 <see cref="V21"/>。
        /// </summary>
        public TableRulesV21 rulesV21;

        /// <summary>本局是否跑 v2.1 规则（转发 TableSettings，读起来短一点，也方便以后改成按关卡配）。</summary>
        public bool V21 { get { return TableSettings.UseRulesV21 && rulesV21 != null; } }

        /// <summary>开局界面（书 / 木牌 / 蜡烛）</summary>
        public TableTitleRig  titleRig;

        /// <summary>当前这一关（关卡定义 + 过程数据 + 进行状态）</summary>
        public Level level = new Level();

        /// <summary>全部关卡，顺序就是配置里的顺序</summary>
        public List<LevelData> levels = new List<LevelData>();

        /// <summary>当前在第几关（levels 的下标）</summary>
        public int levelIndex;

        /// <summary>
        /// 这一关的过程数据。**转发**到 level.turn，不是另存一份 ——
        /// TurnState 是"一关之内的过程数据"，天然属于某一关，
        /// 原来它飘在循环上，等于"这条命是谁的"没有主。
        /// 留这个属性只是让调用方少写一层 .turn。
        /// </summary>
        public TurnState turn { get { return level.turn; } }

        /// <summary>玩家做过的选择 —— 和 TurnState 互不依赖，各记各的</summary>
        public SelectionLog selections = new SelectionLog();

        /// <summary>本次开局实际展示的牌组候选（v2.1 会过滤掉旧流程牌组）。</summary>
        private Choice activeDeckChoice;

        public TablePhase phase = TablePhase.DeckPick;

        /// <summary>
        /// 已经放进投放区、等玩家按确认的那些牌。
        ///
        /// 【为什么是列表，而不是单张】
        /// 效果类单独封装（Effect / EffectGroup / EffectOp）的**目的就是并发**：
        /// 多张牌同时发动时，效果靠 EffectGroup.Append 一层层叠进 activeEffects，
        /// 而不是各改各的数值。原来这里写死"每回合只能投一张"，
        /// 等于从界面层把整套效果组合体系掐死了 —— 那份解耦就白做了。
        /// </summary>
        private readonly List<PlayCard> staged = new List<PlayCard>();

        /// <summary>暂停中（ESC 打开菜单）。</summary>
        public bool paused;

        /// <summary>设置面板开着没有（开场和暂停菜单都能打开它）。</summary>
        public bool settingsOpen;

        /// <summary>刀片位上的那张 3D 卡（只在选刀片阶段存在）</summary>
        public PlayCard bladeCard;

        /// <summary>本回合投出去的东西的名字，模拟中/结算界面显示</summary>
        public string lastPlayed = "";

        /// <summary>一行提示（"模块不能作为刀片"这类），给 HUD 显示</summary>
        public string notice = "";

        /// <summary>本次"模拟中"开始的时刻</summary>
        public float simulatingSince = -1f;

        /// <summary>
        /// 杯内模拟。一关之内只有一份，**跨回合保留** ——
        /// 规格要求粒子属性和刀片属性留到下一回合，
        /// 所以每次投放只是往里加粒子，不是重建。
        /// </summary>
        public CupSim cup = new CupSim();

        /// <summary>每个回合的行动次数（规格：两次）</summary>
        public int actionsPerTurn = 2;

        /// <summary>这个回合已经用掉的"空打"次数</summary>
        public int skipsUsed;

        /// <summary>这个回合用掉了几次行动。</summary>
        public int ActionsUsed { get { return staged.Count + skipsUsed; } }

        /// <summary>还剩几次行动。</summary>
        public int ActionsLeft
        {
            get { return Mathf.Max(0, actionsPerTurn - ActionsUsed); }
        }

        /// <summary>本次"模拟中"开始的时刻（旧字段，保留给快照对比用）</summary>

        /// <summary>手牌区布局 —— 和 TableSetup 共用同一组常量，免得两边各写一个数。</summary>
        public const float HandZ   = -0.58f;
        public const float HandGap = 0.30f;

        // ══════════════════════════════════════════════════════════════
        //  查询
        // ══════════════════════════════════════════════════════════════

        /// <summary>现在能不能拖动卡牌（选牌阶段、且没有弹窗挡着）。</summary>
        public bool CanInteract
        {
            get { return phase == TablePhase.Select && !paused && !settingsOpen; }
        }

        /// <summary>待投放的牌数。</summary>
        public int StagedCount { get { return staged.Count; } }

        /// <summary>
        /// 投放区里那几张 3D 卡（只读）。
        /// 存档要用它把"待放置的是哪几张手牌"记下来（见 TableRulesV21.CaptureStaged）——
        /// 别处不要改这个列表，改投放区一律走 Stage / Release。
        /// </summary>
        public List<PlayCard> StagedCards { get { return staged; } }

        /// <summary>手牌是不是真的空了（关卡结束的判据）。</summary>
        public bool HandEmpty { get { return turn.IsHandEmpty; } }

        /// <summary>还在开局准备阶段（牌组 / 刀片还没定）。</summary>
        public bool IsPreparing
        {
            get { return phase == TablePhase.DeckPick || phase == TablePhase.BladePick; }
        }

        /// <summary>停在场界面。</summary>
        public bool IsTitle { get { return phase == TablePhase.Title; } }

        /// <summary>停在关卡界面。</summary>
        public bool IsLevelSelect { get { return phase == TablePhase.LevelSelect; } }

        /// <summary>还有没有下一关。</summary>
        public bool HasNextLevel { get { return levelIndex + 1 < levels.Count; } }

        /// <summary>当前正在进行的那个选择环节（不在选择阶段返回 null）。</summary>
        public Choice CurrentChoice
        {
            get
            {
                if (phase == TablePhase.DeckPick)  return activeDeckChoice ?? FindChoice(GameConfig.DeckPickId);
                if (phase == TablePhase.BladePick) return FindChoice(GameConfig.BladePickId);
                return null;
            }
        }

        /// <summary>
        /// 按 id 找选择环节。
        /// 直接转发给 LevelData.FindChoice —— 原来这里自己遍历了一遍，
        /// 而数据层早就有这个方法，等于把同一件事写了两份。
        /// </summary>
        public Choice FindChoice(string id)
        {
            return level != null ? level.FindChoice(id) : null;
        }

        // ══════════════════════════════════════════════════════════════
        //  开局：牌组 → 刀片
        // ══════════════════════════════════════════════════════════════

        /// <summary>开一局。叫 Begin 是因为它由 TableSetup 在装配完成之后调用。</summary>
        public void Begin()
        {
            GameConfig.EnsureLoaded();
            level = new Level(GameConfig.Level());

            selections.Clear();
            turn.Reset();

            staged.Clear();
            V21ClearTable();
            paused          = false;
            settingsOpen    = false;
            lastPlayed      = "";
            notice          = "";
            simulatingSince = -1f;
            if (setup != null) setup.deckName = "";

            // ── 开场界面 ──
            if (titleRig != null)
            {
                titleRig.Build();
                phase = TablePhase.Title;
                return;
            }

            OpenLevelSelect();
        }

        /// <summary>点了桌上那本书（新游戏）：收起开场，打开关卡界面。</summary>
        public void ConfirmTitleStart()
        {
            if (phase != TablePhase.Title) return;

            if (titleRig != null) titleRig.Clear();

            OpenLevelSelect();
        }

        /// <summary>
        /// 直接开一关 —— **只有 <see cref="TableSettings.AutoStart"/> 为真时才会被调用**（`DSH_AUTOSTART=1`）。
        ///
        /// 【为什么需要有这个后门】用户连着两次报的都是**打包版**里的操作问题，
        ///   而在打包版里点菜单进一关要经过开场 → 关卡 → 牌组 → 刀片四个环节，
        ///   复现一次又慢又容易点歪（自动化点菜单还得跟 IMGUI 抢焦点）。
        ///   这里替玩家把这四下点击按一遍，停在"第 1 回合、什么都没动"的桌面上 ——
        ///   和编辑器探针的起点完全一致，于是**打包版也能被逐帧复现**。
        ///
        /// 【它一行规则都不碰】走的全是玩家那条路上的公开入口：
        ///   ConfirmTitleStart → 选关 → ConfirmLevelSelect → 选牌组 → ConfirmDeckPick
        ///   → 点一张核心（SwapBladeWith）→ ConfirmBladePick。
        ///   任何一步没走成都会如实打日志，绝不静默。
        /// </summary>
        public void AutoStartFirstLevel()
        {
            if (phase != TablePhase.Title)
            {
                Debug.LogWarning("[AutoStart] 当前不是开场阶段（" + phase + "），自动开局跳过。");
                return;
            }

            ConfirmTitleStart();
            if (phase != TablePhase.LevelSelect)
            {
                Debug.LogWarning("[AutoStart] 点「新游戏」之后没进选关界面（阶段 " + phase + "），自动开局停在这里。");
                return;
            }

            if (choiceRig != null) choiceRig.SelectDeck(levelIndex);      // 当前那一关
            ConfirmLevelSelect();
            if (phase != TablePhase.DeckPick)
            {
                Debug.LogWarning("[AutoStart] 确认关卡之后没进牌组界面（阶段 " + phase + "），自动开局停在这里。");
                return;
            }

            if (choiceRig != null) choiceRig.SelectDeck(TableSettings.AutoStartDeck);
            ConfirmDeckPick();
            if (phase != TablePhase.BladePick)
            {
                Debug.LogWarning("[AutoStart] 确认牌组之后没进刀片界面（阶段 " + phase + "），自动开局停在这里。");
                return;
            }

            // 选一张 H>0 的素材当核心（判据和 CoreCandidate 一致：H=0 一进关卡就爆刀）
            if (setup != null && setup.hand != null)
            {
                for (int i = 0; i < setup.hand.Count; i++)
                {
                    PlayCard c = setup.hand[i];
                    if (c == null || c.bindingMaterial == null) continue;
                    if (c.bindingMaterial.H <= 0) continue;
                    SwapBladeWith(c);
                    break;
                }
            }

            ConfirmBladePick();

            Debug.Log("[AutoStart] 已直接开一关（DSH_AUTOSTART=1）：阶段 " + phase
                      + "｜刀片 " + (rulesV21 != null ? rulesV21.blade.Describe() : "?")
                      + "｜手牌 " + (rulesV21 != null ? rulesV21.HandText() : "?")
                      + "｜本回合行动 " + (rulesV21 != null ? rulesV21.actionPoints : 0));
        }

        /// <summary>
        /// 打开关卡界面：v2.1 = 只用 UI 窗口选（TableHud 的关卡窗口自动弹出），
        /// 旧流程 = 桌上一排 3D 关卡卡。
        ///
        /// 关卡列表从配置读（GameConfig.Levels），加一关只是配置里多写一条。
        /// </summary>
        public void OpenLevelSelect()
        {
            // ★ 进选关界面先清掉上一关留在桌面上的 3D 素材卡。
            //   不清的话那张卡会飘在选关界面上、压在关卡卡旁边（用户截图：一张水卡一直杵在中间）。
            //   ★ 为什么放在这个入口、而不是各个调用点：
            //     进选关界面有三条路 —— 开场、退关卡、**打完一关回选关**；
            //     前两条各自调过 V21ClearTable()，第三条（关卡结束后的「返回关卡界面」）漏了，
            //     于是那张卡就跟着回来了。收进入口只写一处，以后再加路径也不会漏。
            V21ClearTable();

            // ★ 刀片卡也要一起收掉 —— V21ClearTable 只管 tableCards 里认领的桌面素材，
            //   刀片卡（这一局的友方单位）不在那份表里，只有 BuildBladeCard / KillBladeCard 认它。
            //   用户的第二张截图就是它：结算界面点「返回关卡界面」→ 那张刀片卡（水）跟着回来了，
            //   杵在选关 / 牌组选择界面中间，看起来像"上一关的卡没清掉"。
            //   这条路径原来没人调 KillBladeCard（NextLevel / ExitLevel / ReturnToTitle / RestartLevel 都调了），
            //   所以按"离开关卡就收"收进这个入口，和上面那句同一个理由。
            KillBladeCard();

            levels = GameConfig.Levels();
            if (levels.Count == 0) levels.Add(GameConfig.Level());

            if (levelIndex < 0 || levelIndex >= levels.Count) levelIndex = 0;

            level = new Level(levels[levelIndex]);
            staged.Clear();
            paused       = false;
            settingsOpen = false;

            if (choiceRig != null)
            {
                // ── v2.1：关卡只在 UI 窗口里选，桌上不摆 3D 关卡卡 ──────────
                //   用户原话：「不要关卡手牌了，就放一个 ui 就行」。
                //   ★ 不建卡**不等于**不用登记选择态：窗口点一行 → SelectDeck(i)、
                //     按「进入这一关」→ ConfirmLevelSelect() 读的都是 rig 里那个下标。
                //     所以这里改成 SetChoiceState(几关, 当前这一关)：桌上干干净净，
                //     窗口里那一行（当前关卡）默认就是选中的，确认键当场可用。
                //   ★ 旧流程（UseRulesV21 = false）照旧 BuildLevelCards 摆那排卡，一行没动。
                if (TableSettings.LevelCardsOnTable)
                    choiceRig.BuildLevelCards(levels, levelIndex);
                else
                    choiceRig.SetChoiceState(levels.Count, levelIndex);

                phase = TablePhase.LevelSelect;
                return;
            }

            StartDeckPick();   // 没有选关界面就直接进牌组
        }

        /// <summary>在关卡界面点了确认（窗口里点一行 / 按「进入这一关」）。</summary>
        public void ConfirmLevelSelect()
        {
            if (phase != TablePhase.LevelSelect) return;

            int idx = choiceRig != null ? choiceRig.DeckSelected : levelIndex;
            if (idx < 0 || idx >= levels.Count)
            {
                // 提示得说清"该去哪儿选" —— v2.1 桌上一张关卡卡都没有，
                // 还写"在桌上点一张"等于指路指到空桌子上（见 OpenLevelSelect 的分流）。
                notice = TableSettings.LevelCardsOnTable
                    ? "先在桌上点一张关卡卡"
                    : "先在关卡窗口里点一行选一关";
                return;
            }

            levelIndex = idx;
            level = new Level(levels[levelIndex]);

            choiceRig.ClearDeckCards();
            StartDeckPick();
        }

        /// <summary>
        /// 过关之后进入下一关。
        /// 没有下一关了（或者还没过关就点了）就回关卡界面。
        /// </summary>
        public void NextLevel()
        {
            if (!HasNextLevel)
            {
                notice = "已经是最后一关了";
                OpenLevelSelect();
                return;
            }

            levelIndex++;
            level = new Level(levels[levelIndex]);

            staged.Clear();
            lastPlayed = "";
            notice     = "";

            if (setup != null) setup.ClearHand();
            KillBladeCard();
            if (choiceRig != null) choiceRig.ClearDeckCards();

            StartDeckPick();
        }

        /// <summary>选择①：三选一牌组。</summary>
        private void StartDeckPick()
        {
            // ★ 进牌组选择界面同样要清掉上一关留在桌面上的 3D 素材卡。
            //   上一轮只补了选关界面（OpenLevelSelect），漏了这条 —— 于是"退关卡 → 牌组选择"
            //   这条路上那张卡还是杵在牌组卡旁边（用户第二次截图指的就是这里）。
            //   规则很简单：**只要离开关卡、进到任何一个非玩法阶段，桌面就不该留着牌**。
            V21ClearTable();

            // 刀片卡同理（它不在 tableCards 里，V21ClearTable 管不到它）
            KillBladeCard();

            Choice deckChoice = FindChoice(GameConfig.DeckPickId);
            activeDeckChoice = MakeDeckChoiceForCurrentRules(deckChoice);
            deckChoice = activeDeckChoice;

            if (deckChoice != null && deckChoice.OptionCount > 0 && choiceRig != null)
            {
                // ── v2.1：牌组只在牌组窗口里选，桌上不摆那排 3D 大卡 ──────────
                //   用户原话：「这些流派也做成窗口，去掉卡牌」。
                //   ★ 和不建关卡卡同一个道理（见 OpenLevelSelect）：不建卡**不等于**
                //     不用登记选择态 —— 窗口点一行 → SelectDeck(i)、按确认 →
                //     ConfirmDeckPick() 读的都是 rig 里那个下标。
                //   ★ 预选值传 -1：牌组是玩家要做的决定，替他默认第一副
                //     等于"什么都没点就能开局"（原来的行为就是"没选"）。
                //   ★ 旧流程（UseRulesV21 = false）照旧 BuildDeckCards 摆那排卡，一行没动。
                if (TableSettings.DeckCardsOnTable)
                    choiceRig.BuildDeckCards(deckChoice);
                else
                    choiceRig.SetChoiceState(deckChoice.OptionCount, -1);

                phase = TablePhase.DeckPick;
                return;
            }

            // 配置里没有牌组环节（或者还没接上选择界面）→ 退回"直接用第一副"
            BeginWithDefaultDeck();
        }

        /// <summary>
        /// v2.1 牌组选择只展示卡表中有定义的素材牌组；旧流程仍使用完整配置。
        /// 这样旧数据可以继续留在配置里供旧模式游玩，但不会误导新规则玩家。
        /// </summary>
        private Choice MakeDeckChoiceForCurrentRules(Choice source)
        {
            if (!V21 || source == null) return source;

            Choice filtered = source.Clone();
            filtered.options.RemoveAll(option => !IsV21Deck(option != null ? option.deck : null));
            return filtered.OptionCount > 0 ? filtered : source;
        }

        private static bool IsV21Deck(Deck deck)
        {
            if (deck == null || deck.ingredients == null || deck.ingredients.Count == 0) return false;
            for (int i = 0; i < deck.ingredients.Count; i++)
            {
                Ingredient ingredient = deck.ingredients[i];
                if (ingredient == null || CardSpecs.MaterialById(ingredient.id) == null) return false;
            }
            return true;
        }

        /// <summary>没有牌组选择环节时的兜底：直接用配置表里的第一副。</summary>
        private void BeginWithDefaultDeck()
        {
            List<Deck> decks = GameConfig.Decks();
            Deck deck = decks.Count > 0 ? decks[0] : null;

            StartLevelWith(deck);
        }

        /// <summary>确定牌组之后：数据层重开一局，再把桌面摆出来。</summary>
        private void StartLevelWith(Deck deck)
        {
            // ★ 这里**不能**再 new 一个 Level。
            //   关卡是玩家在关卡界面选的，new 一下就把选择覆盖掉、永远回到第 1 关。
            //   （原来写的就是 new Level(GameConfig.Level())，加多关卡之后成了 bug。）
            level.Begin(deck, GameConfig.DefaultBlade());

            // 记下本局用的是哪一副牌组 —— 存档要写它，读档才能按同一副牌组把这一关重开
            deckId = deck != null ? deck.id : "";

            // 新的一关 = 干净的杯子。粒子和刀片磨损都从零开始，
            // 跨回合保留说的是"关内"，不是"跨关"。
            cup = new CupSim();
            skipsUsed = 0;

            if (setup != null)
            {
                setup.deckName = deck != null ? deck.name : "";

                if (V21)
                {
                    // ★ v2.1 模式：手牌的权威在 rulesV21 里，不在 setup.hand。
                    //   所以这里**不能**调 setup.RebuildHand()（它会去读旧 TurnState 的手牌，
                    //   而旧手牌在 v2.1 里是空的 —— 会走 DealHand 的兜底分支凭空摆一张铁块）。
                    //   开局摆牌由 ConfirmBladePick（定完刀片核心之后）负责，那时才该看见手牌。
                    setup.ClearHand();
                }
                else
                {
                    setup.RebuildHand();
                }
            }

            if (V21)
            {
                // 发初始手牌：4 素材 + 1 法术（正文 §2.6 / §八），并建出卡表解析报告
                rulesV21.BeginLevel(deck, level != null ? level.TargetScore : 0);
                staged.Clear();
            }

            // ★ 两个槽的名字跟规则模式走（v2.1 = 上桌位 / 附魔位，旧流程 = 素材槽 / 法术槽）。
            //   放在这里而不是 TableSetup 启动时：设置面板改模式是"重进关卡才生效"，
            //   这个字样必须和规则同一条时间线，否则会出现"名字是新的、行为是旧的"。
            if (setup != null) setup.RefreshSlotLabels();

            BuildBladeCard();
        }

        /// <summary>按下「确认选择该卡组」。</summary>
        public void ConfirmDeckPick()
        {
            if (phase != TablePhase.DeckPick) return;

            int idx = choiceRig != null ? choiceRig.DeckSelected : -1;
            if (idx < 0)
            {
                // 提示得说清"该去哪儿选" —— v2.1 桌上没有牌组卡了，
                // 还写"在桌上点一张"等于指路指到空桌子上（见 StartDeckPick 的分流）。
                notice = TableSettings.DeckCardsOnTable
                    ? "先在桌上点一张牌组卡"
                    : "先在牌组窗口里点一行选一副";
                return;
            }

            Choice c = activeDeckChoice ?? FindChoice(GameConfig.DeckPickId);
            if (c == null || idx >= c.options.Count) return;

            ChoiceOption o = c.options[idx];
            if (o == null || o.deck == null) return;

            selections.Record(c.id, 0, o.id);

            choiceRig.ClearDeckCards();
            StartLevelWith(o.deck);

            notice = "牌组已定：" + o.deck.name;
            phase  = TablePhase.BladePick;
        }

        /// <summary>按下「确认刀片，进入关卡」。</summary>
        public void ConfirmBladePick()
        {
            if (phase != TablePhase.BladePick) return;

            // ── v2.1 分流 ──────────────────────────────────────────────
            //   正文 §2.5：核心的 H → 刀片初始 H、V → 刀片初始 V，该卡移出手牌。
            //   这里之前是 TablePhase.BladePick，确认之后才真正成立刀片。
            if (V21)
            {
                MaterialCard mc = bladeCoreCard != null ? bladeCoreCard : rulesV21.CoreCandidate();
                if (mc == null)
                {
                    notice = "没有可当刀片核心的素材（手牌里没有 H>0 的素材卡）";
                    return;
                }

                rulesV21.ChooseCore(mc);
                bladeCoreCard = null;

                if (!rulesV21.HasCore)
                {
                    notice = "刀片核心「" + mc.name + "」的 H 是 0 —— H=0 一进关卡就爆刀，换一张";
                    return;
                }

                rulesV21.StartFirstTurn();

                // 刀片是这一局的**友方单位**，整局都坐在桌面中心（和旧流程同一条理由）
                BuildBladeCard();

                // 手牌：定完核心之后才摆出来（核心那张已经被移出手牌了）
                rulesV21.RebuildHand();

                notice = "刀片已定：" + rulesV21.blade.name +
                         "（H " + rulesV21.blade.H + " · V " + rulesV21.blade.V + "）" +
                         "　本关 " + GameJam.Rules.LevelRun.TurnsPerLevel + " 回合 × " +
                         GameJam.Rules.LevelRun.ActionPointsPerTurn + " 次行动";

                // ★ 开一关就查一遍"槽位语义三者是否一致"（下标 / 牌子 / 收什么）。
                //   用户报的"附魔位不能放卡片"在界面上只表现为"拖过去又弹回来"，
                //   日志里必须有这一行，才分得清是哪一处走偏（见 SlotSemanticReport）。
                SlotSemanticReport();

                phase = TablePhase.Select;
                SyncJuicer();
                return;
            }

            Choice c = FindChoice(GameConfig.BladePickId);
            if (c != null)
            {
                string[] picked = turn.blade != null
                    ? new string[] { turn.blade.id }
                    : new string[0];
                selections.Record(c.id, 0, picked);
            }

            // ★ 不销毁刀片卡。
            //   刀片是这一局的**友方单位**，整局都该坐在桌面中心 ——
            //   原来确认之后就把卡收掉了，玩家打到一半就看不见自己的单位了。
            //   换刀片 / 过关 / 回开场时由 BuildBladeCard / KillBladeCard 负责换掉。
            //
            // KillBladeCard();

            notice = "刀片已定：" + turn.BladeName();
            phase  = turn.IsHandEmpty ? TablePhase.LevelEnd : TablePhase.Select;

            SyncJuicer();
        }

        // ══════════════════════════════════════════════════════════════
        //  刀片位
        // ══════════════════════════════════════════════════════════════

        private void BuildBladeCard()
        {
            KillBladeCard();

            // ── v2.1：刀片位的门面 ──
            //   ① **核心已经定了**（ConfirmBladePick 之后）→ 用真正的那张核心卡。
            //      ★ 这里绝对不能再问 CoreCandidate()：ChooseCore 已经把核心移出手牌了，
            //        而 CoreCandidate 问的是"手牌里第一张素材"，于是会答成*另一张*卡 ——
            //        实机踩到的就是它：核心是水（面板写着 刀片 水 H=2 V=2），
            //        桌上摆出来的却是冰的卡面（H5 D2 V3）。
            //        玩家看到的是"面板写桌面素材 3 张、画面里 4 张卡，而且有两张一模一样的冰"，
            //        也就是那条"规则状态与 3D 卡不同步"的报障。
            //   ② 还没定核心（选刀片阶段）→ 才用候选核心当门面：
            //      正文 §2.5 的刀片是玩家从手里挑的，选之前桌上得有个东西可看。
            //      这里取手牌第一张素材 —— 和 ConfirmBladePick 的兜底口径一致
            //      （两边不一致就会出现"桌上摆着 A、确认下去变成 B"）。
            //
            //   ③ ★ 玩家点过候选（bladeCoreCard 不为空）→ **必须摆他点的那张**。
            //      用户报的是「点了没视觉变化，只有按确认之后才换」：根因就是这里
            //      一直问 CoreCandidate()（"手牌里第一张素材"），玩家点第二、第三张时
            //      它答的仍然是第一张 —— 于是桌面刀片卡纹丝不动。
            //      优先级：定下来的核心 > 玩家点的候选 > 兜底候选，三者取第一个非空。
            if (V21)
            {
                MaterialCard core = rulesV21.coreCard;
                if (core == null) core = bladeCoreCard;
                if (core == null) core = rulesV21.CoreCandidate();
                if (core == null || core.card == null) return;

                bladeCard = CardFactory.Create(Card.Of(core.card), setup.cardsRoot,
                                               TableChoiceRig.BladeSpot, Vector3.zero);
                MarkBladeCard(bladeCard);
                return;
            }

            Card c = turn.blade != null ? Card.Of(turn.blade) : null;
            if (c == null) return;

            bladeCard = CardFactory.Create(c, setup.cardsRoot,
                                           TableChoiceRig.BladeSpot, Vector3.zero);
        }

        /// <summary>
        /// 给刀片卡挂一个「刀片」标记 —— 让它在桌上一眼能和素材卡分开。
        ///
        /// 【为什么必须有】v2.1 的刀片卡和桌面素材卡**长得一模一样**：同尺寸、同卡面路数，
        ///   盐和水连 H/D/V 都可能完全相同（皮肤由物态决定）。于是玩家数牌时会说
        ///   "面板写桌面素材 3 张、画面里却有 4 张卡"—— 那第 4 张其实是刀片卡。
        ///   上一轮修的是"刀片卡用错了卡面"，这一轮补的是"认不出它是刀片"。
        ///
        /// 【为什么是一段平贴桌面的 3D 文字，而不是贴纸/描边】
        ///   ① 俯视和桌面视角都得看得见 → 平贴在桌面上、和卡面用同一个朝向约定
        ///      （CardFactory.AddText 那套 LookRotation，见那里的推导），从任何机位看都不变形；
        ///   ② **不能挡住卡面的名字和 H/D/V** → 挂在卡的**近侧之外**（localZ 负方向，
        ///      卡面自己的名字在 +0.126、属性在 −0.112，卡身到 ±0.16 为止，
        ///      所以 −0.225 已经在卡外、落在桌面上），而且高度只有 2 毫米，不遮任何东西；
        ///   ③ 挂在卡的 transform 下 → 卡被拖走/销毁时标记跟着走，不用单独维护生命周期。
        ///
        /// 【字号】按 CardFactory 那段实测标定（world 字高 ≈ size × 3.4）取 0.0105，
        ///   字高约 0.036（卡宽 0.24 的 15%，比卡面属性行 0.0038 大一档），
        ///   在俯视机位下也读得清；颜色用暖金（和"当前关卡"同一个色系），
        ///   在深色木桌上是唯一的亮点，不靠形状认。
        /// </summary>
        private void MarkBladeCard(PlayCard card)
        {
            if (card == null) return;

            CardFactory.AddText(card.transform, "刀 片", -0.225f, 0.0105f,
                                new Color(0.98f, 0.76f, 0.32f), 0.002f);
        }

        /// <summary>
        /// v2.1：玩家点了手牌里的一张素材，把它记为刀片核心的**候选**。
        /// 真正生效在 ConfirmBladePick —— 和旧流程一样，点只是选，确认才算数。
        /// </summary>
        public bool PickCoreCandidate(MaterialCard mc)
        {
            if (mc == null || mc.card == null) return false;

            // ★ H=0 的候选当场拒绝，而且**不动桌面上那张刀片卡**。
            //   【为什么提前到这里拒】原来只有 ConfirmBladePick 里有一道 HasCore 检查，
            //   而那时候 ChooseCore 已经把这卡移出手牌、刀片也已经换成它了 ——
            //   玩家看到的是"点了确认，牌没了、刀片还是 0，还得自己再点一张"。
            //   现在点的时候就拦下来，桌面上的刀片卡保持上一张的样子（用户要求的口径）。
            if (mc.H <= 0)
            {
                notice = "刀片核心「" + mc.name + "」的 H 是 0 —— H=0 一进关卡就爆刀，换一张";
                Debug.Log("[V21] 刀片核心候选被拒：" + mc.name + "（H=0），桌面刀片卡不动");
                return false;
            }

            bladeCoreCard = mc;

            // 刀片位换成这张卡，同时**整副手牌重建一次** —— 否则卡牌的 homePosition
            // 还停在旧位置，确认之后 LayoutHand 会把剩下的牌重新排一遍，
            // 那张被点过的牌会先跳回去再让位，看起来像点错了。
            rulesV21.RebuildHand();
            BuildBladeCard();

            // 桌面上那张卡到底换成了谁 —— 用户报的正是"看不出换没换"，
            // 所以日志里要有卡名，而不是只写"已刷新"
            Debug.Log("[V21] 刀片核心候选 → 桌面刀片卡已刷新：「"
                      + (bladeCard != null ? bladeCard.DisplayName : "（没建出来）") + "」"
                      + "（候选 " + mc.name + " H=" + mc.H + " V=" + mc.V + "，还没确认）");

            notice = "刀片核心候选：" + mc.name + "（H " + mc.H + " · V " + mc.V + "）—— 按确认进入关卡";
            return true;
        }

        /// <summary>v2.1 的刀片核心候选（玩家点过的那张，没点过就是手牌第一张）。</summary>
        public MaterialCard bladeCoreCard;

        private void KillBladeCard()
        {
            if (bladeCard == null) return;

            CardFactory.DestroySafe(bladeCard.gameObject);
            bladeCard = null;
        }

        /// <summary>
        /// 点手牌里的一张，和当前刀片对调。
        ///
        /// 【数据层是"原位替换"】
        /// SwapBladeWithHand 换完之后，turn.hand[idx] 拿到的就是原来那张刀片 ——
        /// 不是移除再追加，所以手牌不会少人、刀片也不会凭空消失。
        ///
        /// 【表现层必须整张重建】
        /// 卡面贴图和文字都是造卡时烤好的，改 data 字段不会让它们变。
        /// 所以这里把两张卡都销毁重建，位置沿用原来的（槽位不动，只换内容）。
        /// </summary>
        public bool SwapBladeWith(PlayCard handCard)
        {
            if (phase != TablePhase.BladePick) return false;
            if (handCard == null || setup == null || setup.hand == null) return false;

            // ── v2.1：点一张手牌素材 = 选它当刀片核心（正文 §2.5）──
            //   注意这里**换的是"候选"**，不是刀片本身：刀片要等确认才成立，
            //   确认之前被选中的卡仍然留在手牌里（所以能来回换）。
            if (V21)
            {
                MaterialCard mc = rulesV21.FindHandMaterial(handCard);
                if (mc == null)
                {
                    notice = "法术不能作为刀片核心 —— 刀片核心必须是一张素材";
                    return false;
                }
                return PickCoreCandidate(mc);
            }

            // 模块不能当刀片 —— 规则上的硬约束，说清楚，别静默失败
            if (handCard.IsModule)
            {
                notice = "模块不能作为刀片";
                return false;
            }

            int idx = setup.hand.IndexOf(handCard);
            if (idx < 0) return false;

            if (!turn.SwapBladeWithHand(idx)) return false;

            ReplaceHandCard(idx, turn.hand[idx]);
            BuildBladeCard();

            notice = "已换刀片：" + turn.BladeName();
            return true;
        }

        /// <summary>把某张手牌换成另一份数据（位置不变）。</summary>
        private void ReplaceHandCard(int index, Card c)
        {
            if (setup == null || setup.hand == null) return;
            if (index < 0 || index >= setup.hand.Count) return;
            if (c == null) return;

            PlayCard old = setup.hand[index];
            if (old == null) return;

            Vector3 home  = old.homePosition;
            Vector3 euler = old.homeEuler;

            CardFactory.DestroySafe(old.gameObject);

            setup.hand[index] = CardFactory.Create(c, setup.cardsRoot, home, euler);
        }

        // ══════════════════════════════════════════════════════════════
        //  投放区
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 卡槽下标 —— 0 左、1 右。和 TableSetup.BuildSlots 的顺序一致。
        ///
        /// 规格原话是"左边一个右边一个，一个法术槽一个素材槽"，
        /// 按列举顺序理解为**左法术、右素材**。要换回来只需要对调这两行，
        /// 槽的归属规则是跟着下标走的，别处不用动。
        /// </summary>
        public const int SlotSpell    = 0;
        public const int SlotMaterial = 1;

        /// <summary>
        /// 这个槽收不收这张牌。
        ///
        /// 素材槽只收素材、法术槽只收法术。规则跟着槽走而不是跟着牌走 ——
        /// 牌自己不知道"我该放哪"，是桌面规定了哪里放什么。
        ///
        /// 【v2.1 为什么不能只看 card.card.IsSpell】数据层的 Card 只有"素材 / 模块"两种 kind，
        ///   而 v2.1 的法术卡**就是用模块壳装的**（Card.Of(BuildSpellModule(...))，
        ///   见 TableRulesV21 里那段"表现层将就"）—— 换句话说 Card 这一层分不出
        ///   "变速模块"和"v2.1 法术"。3D 卡身上的 bindingSpell / bindingMaterial 才是权威
        ///   （TableRulesV21.RebuildHand 绑的），所以 v2.1 一律问它。
        ///   只认 Card.IsSpell 的写法踩过一次：附魔位把法术当成"不是法术"拒掉，
        ///   玩家的感受就是**"附魔位不能放卡片"**。
        /// </summary>
        public bool CanStageInto(int slot, PlayCard card)
        {
            if (card == null) return false;

            if (V21 && rulesV21 != null)
            {
                bool spell    = rulesV21.IsHandSpellCard(card);
                bool material = card.bindingMaterial != null || rulesV21.IsTableCard(card);

                // 两张绑定都没有的卡（探针手工造的 / 旧流程留下的）→ 退回按 Card 判，
                // 不然这种卡会"哪个槽都放不进去"，看起来和这次的 bug 一模一样、却查不出原因
                if (!spell && !material && card.card != null)
                {
                    spell    = card.card.IsSpell;
                    material = card.card.IsMaterial;
                }

                if (slot == SlotSpell)    return spell;
                if (slot == SlotMaterial) return material;
                return true;
            }

            if (card.card == null) return false;

            if (slot == SlotMaterial) return card.card.IsMaterial;
            if (slot == SlotSpell)    return card.card.IsSpell;

            return true;
        }

        /// <summary>
        /// 放错槽了：说清楚这个槽收什么、你手里这张是什么、**该放哪儿**。
        ///
        /// 【v2.1 的说法必须和桌面上的牌子一致】牌子上写的是「附　魔 位 / 上　桌 位」，
        ///   提示里却写"法术槽 / 素材槽"的话，玩家会以为自己看错了槽
        ///   （这两个名字是上一版流程留下的，见 TableSetup.V21SlotNames）。
        ///   用户报的"附魔位不能放卡片"就是这么来的：他往附魔位拖了一张**素材**，
        ///   提示只说"法术槽只放法术"，既没对上牌子上的字、也没告诉他该拖到哪儿 ——
        ///   所以提示必须写成「往哪儿放」。
        /// </summary>
        public void RejectSlot(PlayCard card, int slot)
        {
            bool v21 = V21 && TableSettings.UseRulesV21;

            bool isSpell = v21 && rulesV21 != null && IsHandSpell(card);
            string got = v21 ? (isSpell ? "法术" : "素材")
                             : ((card != null && card.card != null) ? card.card.TypeTag : "?");

            string here = (slot == SlotMaterial)
                ? (v21 ? "「上　桌 位」" : "素材槽")
                : (v21 ? "「附　魔 位」" : "法术槽");
            string there = (slot == SlotMaterial)
                ? (v21 ? "「附　魔 位」" : "法术槽")
                : (v21 ? "「上　桌 位」" : "素材槽");
            string want = (slot == SlotMaterial) ? "素材" : "法术";

            notice = here + "只收" + want + "　——　" + (card != null ? card.DisplayName : "?")
                   + " 是" + got + "（" + got + "请拖到 " + there + "）";
        }

        /// <summary>
        /// 槽位语义自检：**下标 / 桌面牌子上的字 / 这个槽收什么** 三者必须一致。
        ///
        /// 【为什么要常驻这一条】"附魔位放不了法术"这类问题在界面上只表现为
        ///   "拖过去又弹回来了"，日志里什么都没有 —— 而它可能是三处里的任何一处走偏：
        ///     ① 常量把两个槽的下标写反了（SlotSpell/SlotMaterial 对调）
        ///     ② 桌面牌子上的字和下标的顺序不一致（玩家照牌子放，代码按另一套收）
        ///     ③ 槽位的世界坐标顺序和牌子不一致（左边的牌子刻在右边的槽上）
        ///   三者都是"改一处忘了另一处"造成的，所以每次开一关查一遍、写进日志。
        ///
        /// 返回一行摘要（探针直接打进日志），不一致时同时打 LogWarning。
        /// </summary>
        public string SlotSemanticReport()
        {
            if (setup == null || setup.board == null) return "槽位自检：没有桌面 / 卡槽，跳过";

            bool ok = true;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            sb.Append("槽位自检：槽数 ").Append(setup.board.SlotCount);
            if (setup.board.SlotCount < 2) { ok = false; sb.Append("（★ 少于 2 个）"); }

            // ① 左右顺序：下标 0 的 x 必须小于下标 1（牌子是按下标从左往右刻的）
            float x0 = setup.board.SlotPosition(0).x;
            float x1 = setup.board.SlotPosition(1).x;
            bool leftFirst = x0 < x1;
            if (!leftFirst) ok = false;

            sb.Append("｜下标 0 在").Append(leftFirst ? "左" : "★右")
              .Append("（x ").Append(x0.ToString("0.###")).Append(" / ").Append(x1.ToString("0.###")).Append("）");

            // ② 名字和语义：v2.1 的牌子 [0] 必须含"附魔"、[1] 必须含"上桌"，
            //    而 SlotSpell 必须指 [0]（附魔位）、SlotMaterial 必须指 [1]（上桌位）
            if (TableSettings.UseRulesV21)
            {
                string[] names = TableSetup.V21SlotNames;
                bool nameOk = names != null && names.Length >= 2
                           && Squash(names[SlotSpell]).Contains("附魔")
                           && Squash(names[SlotMaterial]).Contains("上桌");
                if (!nameOk) ok = false;

                sb.Append("｜牌子 [").Append(SlotSpell).Append("]=").Append(names != null && names.Length > 0 ? names[SlotSpell] : "?")
                  .Append("（收法术）、[").Append(SlotMaterial).Append("]=").Append(names != null && names.Length > 1 ? names[SlotMaterial] : "?")
                  .Append("（收素材）→ ").Append(nameOk ? "一致" : "★名字与语义不一致");

                // ③ 行为：拿手牌里的法术/素材各问一次 CanStageInto，确认"附魔位收法术、上桌位收素材"
                PlayCard handSpell = null, handMaterial = null;
                if (setup.hand != null)
                {
                    for (int i = 0; i < setup.hand.Count; i++)
                    {
                        PlayCard c = setup.hand[i];
                        if (c == null) continue;
                        if (handSpell == null && IsHandSpell(c)) handSpell = c;
                        if (handMaterial == null && c.bindingMaterial != null) handMaterial = c;
                    }
                }

                if (handSpell != null)
                {
                    bool intoEnchant = CanStageInto(SlotSpell, handSpell);
                    if (!intoEnchant) ok = false;
                    sb.Append("｜法术「").Append(OneLine(handSpell.DisplayName)).Append("」进附魔位=").Append(intoEnchant ? "收" : "★被拒");
                }
                if (handMaterial != null)
                {
                    bool intoTable = CanStageInto(SlotMaterial, handMaterial);
                    if (!intoTable) ok = false;
                    sb.Append("｜素材「").Append(OneLine(handMaterial.DisplayName)).Append("」进上桌位=").Append(intoTable ? "收" : "★被拒");
                }

                // ④ ★ 落点区域 vs 牌子位置 —— 用户第二次报的就是这一条：
                //    语义全对，可是"照着牌子上的字去放"落到了判定区外面
                //    （牌子刻在框外面 + 拖动抬卡带来的透视差，两条叠在一起）。
                //    所以每个槽量两件事：
                //      · 牌子中心能不能被判到它自己的槽（不能 = 玩家照牌子放必然失败）
                //      · 判定区近端离手牌那一排还剩多少厘米
                //        （≤0 = 判定区压到手牌上，"拖回手牌反悔"会变成"又出了一张牌"）
                if (setup.board == null)
                {
                    ok = false;
                    sb.Append("｜落点区域：没有卡槽，量不了");
                }
                else if (setup.interaction == null)
                {
                    sb.Append("｜落点区域：没有 TableInteraction（余量在它身上），这一步量不了");
                }
                else
                {
                    TableInteraction it = setup.interaction;
                    for (int i = 0; i < setup.board.SlotCount && i < 2; i++)
                    {
                        Vector3 label = setup.SlotLabelPosition(i);
                        int hit = setup.board.FindDropTarget(label, it.snapSlackX,
                                                             it.snapSlackZ + it.snapSlackNearZ);
                        if (hit != i) ok = false;

                        // 判定区近端上限（朝玩家那一侧）= 牌子中心 + 牌子离框的距离 − 两个 z 余量
                        float nearLimit = label.z + TableSetup.SlotLabelGap
                                        - it.snapSlackZ - it.snapSlackNearZ;
                        float labelMargin = (label.z - nearLimit) * 100f;          // >0 = 牌子在区里
                        float handMargin  = (nearLimit - TableTurnLoop.HandZ) * 100f;  // >0 = 没压到手牌

                        sb.Append("｜槽 ").Append(i).Append(" 牌子 z=").Append(label.z.ToString("0.###"))
                          .Append(" → 牌子中心判到 ").Append(hit).Append(hit == i ? " ✓" : " ★")
                          .Append("，牌子离判定区近端 ").Append(labelMargin.ToString("0.#")).Append(" 厘米")
                          .Append("，判定区离手牌 ").Append(handMargin.ToString("0.#")).Append(" 厘米");

                        if (labelMargin <= 0f) { ok = false; sb.Append("（★ 牌子掉到判定区外）"); }
                        if (handMargin <= 0f)  { ok = false; sb.Append("（★ 判定区压到手牌上）"); }

                        // ⑤ ★★ 真正的那条防线：模拟"玩家把卡**看着压在牌子**上"再松手。
                        //   玩家是照着自己看到的那张卡放的，而拖动时卡被抬起 DragLift，
                        //   "看到的位置"和"鼠标在地面的位置"差着一段透视差（相机斜看桌面）。
                        //   这里先反解出"卡看着压在牌子中心时，鼠标落在地面的哪一点"，
                        //   再喂给**判决用的那个函数**（JudgeDropPoint），看它判到哪个槽。
                        //   用户第二次报的"照着牌子放却放不上去"就是这一步判不到槽 ——
                        //   ②③④ 全绿也照样复现，所以缺了 ⑤ 就等于没防住。
                        if (setup.cam == null)
                        {
                            sb.Append("｜（没有相机，「压牌子」这一步量不了）");
                        }
                        else
                        {
                            float dragY = 0.022f + PlayCard.DragLift;           // 拖动时卡中心的高度
                            Vector3 c = setup.cam.transform.position;
                            float t = (dragY - c.y) / (0f - c.y);               // 反解：地面点 → 抬高后的位置
                            Vector3 mouseGround = new Vector3(
                                c.x + t * (label.x - c.x), dragY, c.z + t * (label.z - c.z));

                            Vector3 judged = it.JudgeDropPoint(mouseGround);
                            int aimHit = setup.board.FindDropTarget(judged, it.snapSlackX,
                                                                    it.snapSlackZ + it.snapSlackNearZ);
                            float aimMargin = (judged.z - nearLimit) * 100f;

                            sb.Append("｜压牌子松手 → 判决落点 z=").Append(judged.z.ToString("0.###"))
                              .Append(" 判到 ").Append(aimHit).Append(aimHit == i ? " ✓" : " ★判不到")
                              .Append("（离判定区近端 ").Append(aimMargin.ToString("0.#")).Append(" 厘米）");

                            if (aimHit != i) ok = false;
                            // ★ 余量太小也要报警：用户第二次报的"照着牌子放却放不上去"，
                            //   改前那版在这一步实测只剩 **1.8 厘米** —— 判据本身"能过"，
                            //   但玩家把手往下一压就出区。只看"过不过"的检查会放它过去，
                            //   所以这里连"太薄"一起管（阈值 5 厘米）。
                            else if (aimMargin < 5f)
                            {
                                ok = false;
                                sb.Append("（★ 余量太小：玩家手一抖就出区）");
                            }
                        }
                    }
                }
            }

            string line = sb.ToString();
            if (!ok) Debug.LogWarning("[V21][槽位自检] ★ " + line);
            else     Debug.Log("[V21][槽位自检] ✓ " + line);

            return line;
        }

        /// <summary>这张 3D 手牌卡是不是法术（权威判据：RebuildHand 绑的 TableSpellCard）。</summary>
        private static bool IsHandSpell(PlayCard c)
        {
            if (c == null) return false;
            if (c.bindingSpell != null) return true;
            return c.GetComponent<TableSpellCard>() != null;
        }

        /// <summary>
        /// 比对牌名字样之前先把空白去掉 —— 牌子上的字是**排版过的**
        /// （"附　魔 位" 里有一个全角空格和一个半角空格），
        /// 直接 Contains("附魔") 永远不成立，自检会天天误报"名字与语义不一致"。
        /// 第一版就是这么写错的：实测日志里它确实报了 ★不一致，而功能其实是好的 ——
        /// 自检误报比不检更坏（下次真出问题没人信它）。
        /// </summary>
        private static string Squash(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\u3000", "").Replace(" ", "").Replace("\t", "");
        }

        /// <summary>把可能带换行的名字压成一行（法术卡的名字里带需求原文，是两行的）。</summary>
        private static string OneLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\r", " ").Replace("\n", " ");
        }

        /// <summary>
        /// 某张牌落进了投放区，记为待投放。
        ///
        /// 放多少张都行 —— 投放区有 8 个格子，玩家的手牌也就那几张。
        /// 之前这里是"再放一张就把上一张退回手牌"，理由是"玩家会以为两张都会
        /// 被投进去"；其实真正的毛病是**没说清楚**，而不是不让放。
        /// 现在顶部信息栏会把待投放的张数和名字全列出来。
        /// </summary>
        public void Stage(PlayCard card)
        {
            if (card == null) return;
            if (staged.Contains(card)) return;

            // 规格：每回合只有两次行动机会，投放一次算一次
            //
            // ★ v2.1 没有这条限制：正文 §2.6"手牌无上限，出牌不消耗行动机会"。
            //   在 v2.1 模式下拦这一下，等于把"出牌自由"这条规则又掐回去了 ——
            //   玩家手里 3 张素材会被告知"行动用完了"，而那时行动机会明明是 5 次。
            if (!V21 && ActionsLeft <= 0)
            {
                notice = "这个回合的行动用完了（" + actionsPerTurn + " 次）";
                return;
            }

            staged.Add(card);
            lastPlayed = "";
        }

        /// <summary>
        /// 空打：把一次行动直接跳过。
        /// 规格里"所有行动可跳过"，所以两次都跳过是允许的 ——
        /// 但那样杯里就啥也没有，Confirm 那边会拦住。
        /// </summary>
        public void SkipAction()
        {
            if (phase != TablePhase.Select) return;
            if (ActionsLeft <= 0) return;

            skipsUsed++;
            notice = "跳过了一次行动（还可以空打 " + ActionsLeft + " 次）";
        }

        /// <summary>把一张牌从投放区退回手牌（等于"我反悔了"）。</summary>
        public void Release(PlayCard card)
        {
            if (card == null) return;

            if (board != null) board.Clear(card);
            card.ReturnHome();

            staged.Remove(card);

            LayoutHand();
        }

        // ══════════════════════════════════════════════════════════════
        //  确认投放
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 按下「确认投放」：投放区里的牌**一起**进数据层，然后依次飞进罐子。
        ///
        /// 【一次投多张，效果是叠加的】
        /// 食材进杯子（PlayFromHand），模块的效果并进 activeEffects（PlayModule），
        /// 两者都按投放顺序走一遍。多个模块的效果靠 EffectGroup.Append 叠起来，
        /// 所以"两张加硫性、一张加汞性"同时发动时，它们是在同一份效果组合里
        /// 依次作用的，而不是互相覆盖 —— 这正是效果类单独封装要换来的东西。
        ///
        /// 落子权仍然在确认键上，不在"放上投放区"那一刻：放上去只是摆好，
        /// 玩家还能一张张拖回来。
        /// </summary>
        public void Confirm()
        {
            if (phase != TablePhase.Select) return;

            // ── v2.1 分流 ──────────────────────────────────────────────
            //   出牌不消耗行动机会（正文 §2.6）、只有"启动破壁机"才吃行动机会。
            if (V21) { ConfirmV21(); return; }

            // 规格：无食材时不得开始回合 —— 至少得有一张食材牌打出去。
            // 两次行动全空打、而且杯里本来就没东西，这一回合就没意义。
            if (staged.Count == 0 && cup.particles.Count == 0)
            {
                notice = "至少投放一张食材才能开始模拟";
                return;
            }

            if (staged.Count == 0)
            {
                // 全空打但杯里还有上一回合的食材 —— 允许，直接进模拟
                notice = "本回合空打";
                StartSimulation();
                return;
            }

            // 先整批拿出来再处理 —— 处理过程会动 setup.hand，边遍历边改会漏牌
            List<PlayCard> batch = new List<PlayCard>(staged);
            staged.Clear();

            List<string> played = new List<string>();

            for (int i = 0; i < batch.Count; i++)
            {
                PlayCard card = batch[i];
                if (card == null) continue;

                string name = card.DisplayName;

                // ★ 一条路径 —— 打出去的后果由 Card.PlayInto 决定。
                //   以前这里是"食材走 PlayFromHand、模块走 PlayModule"两套，
                //   界面层凭空多出一处业务判断。
                int idx = turn.hand != null ? turn.hand.IndexOf(card.card) : -1;
                Card playedCard = idx >= 0 ? turn.Play(idx) : null;
                if (playedCard != null) name = playedCard.name;

                played.Add(name);

                // ── 2. 再动表现层 ──
                if (board != null) board.Clear(card);
                if (setup != null && setup.hand != null) setup.hand.Remove(card);

                Vector3 mouth = juicer != null
                    ? juicer.MouthWorld
                    : card.transform.position + Vector3.up * 0.4f;

                // 落点错开一点、寿命依次加长 —— 看起来是一张接一张被吸进去，
                // 全叠在同一个点上会糊成一坨，也看不出先后
                float spread = (i - (batch.Count - 1) * 0.5f) * 0.05f;
                card.ConsumeInto(mouth + new Vector3(spread, 0f, 0f), 0.8f + i * 0.14f);
            }

            lastPlayed = played.Count > 0 ? string.Join("、", played.ToArray()) : "";

            LayoutHand();
            SyncJuicer();

            StartSimulation();
        }

        /// <summary>
        /// 进入真实的杯内模拟（替掉 Day 3 那个"模拟中…"占位）。
        ///
        /// 模拟是**纯逻辑**（CupSim），这里只负责起跑和收尾；
        /// 画成什么样是 CupSimView 的事。
        /// </summary>
        private void StartSimulation()
        {
            cup.Begin(turn);          // 把本回合投进去的食材变成粒子，上一回合的还留着
            phase = TablePhase.Simulating;
            simulatingSince = Time.time;

            if (juicer != null) juicer.PlayStamp();
        }

        // ══════════════════════════════════════════════════════════════
        //  v2.1：出牌 / 启动 / 回合 / 关卡
        //
        //  【分工】这一节只做"阶段机该做的事"：把 3D 上的动作翻成对 TableRulesV21 的调用，
        //  再把结果翻回阶段和提示。一条规则都不在这里实现 ——
        //  规则全在 TableRulesV21 → TurnEngine 那条路上。
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// v2.1 的「确认投放」= **出牌**：投放区里的素材上桌、法术立即生效。
        /// 两者都**不消耗行动机会**（正文 §2.6、§七-10/11）。
        ///
        /// 【v2.1 下它已经不是主路了 —— 主路是 PlayCardV21】
        ///   这一版把"落槽即结算"做进去之后，牌一进「上桌位 / 附魔位」就当场生效，
        ///   不会停在"待放置"这个中间态。所以这个方法现在的定位是：
        ///     ① 老习惯（把牌摆到桌上、再按一下「放置到桌面」）不至于静默失效；
        ///     ② 自动试玩的 ProbeStageMaterialV21 走的就是它。
        ///   它**一行规则都不写** —— 逐张转发给 PlayCardV21，和"点手牌"是同一个入口。
        /// </summary>
        public void ConfirmV21()
        {
            if (rulesV21 == null) return;

            if (rulesV21.levelOver)
            {
                notice = "关卡已结束（" + rulesV21.endReason + "）";
                return;
            }

            if (staged.Count == 0)
            {
                notice = "v2.1 出牌不用按确认：点手牌就上桌，把它拖进「上桌位 / 附魔位」也一样";
                return;
            }

            List<PlayCard> batch = new List<PlayCard>(staged);

            int mats = 0, spells = 0;

            for (int i = 0; i < batch.Count; i++)
            {
                PlayCard card = batch[i];
                if (card == null) continue;

                bool isSpell = rulesV21.IsHandSpellCard(card);

                // ★ 出牌只有 PlayCardV21 这一条路 —— 它自己会清槽位 / 摘 staged / 建 3D 卡。
                //   这里再补一句 ReturnHome 是给"打不出去"的情况收尾（阶段不对、已经不是手牌了）。
                if (PlayCardV21(card))
                {
                    if (isSpell) spells++; else mats++;
                }
                else
                {
                    card.ReturnHome();
                }
            }

            lastPlayed = mats + " 张素材上桌" + (spells > 0 ? "，" + spells + " 张法术生效" : "");
            notice = lastPlayed + "　（出牌不消耗行动机会，行动机会还剩 " + rulesV21.actionPoints +
                     " 次 —— 点「启动破壁机」才消耗）";
        }

        /// <summary>
        /// v2.1：**把这一张牌打出去** —— 这条路上唯一的那个函数。
        ///
        /// 【三条入口，一个出口】
        ///   ① 点手牌（素材 / 法术）　　　　→ TableInteraction.RouteCardClick
        ///   ② 拖进「上桌位」（素材槽）　　→ TableInteraction.DropCard
        ///   ③ 拖进「附魔位」（法术槽）　　→ TableInteraction.DropCard
        ///   三条都调它。落槽即结算（把牌拖进槽 = 立刻生效），所以槽不会"占住"，
        ///   v2.1 下不存在"槽已满 / 待放置"这种中间态。
        ///
        /// 【为什么必须收成一个函数】—— 这次那个 bug 的根因
        ///   原来"点手牌"走 TableInteraction.TryClickPlace（自己 Stage + Confirm），
        ///   "拖进槽"走 EndDrag 的落槽分支（board.Place + Stage 摆着不动）。
        ///   两条路各记各的状态：同一次操作被两边都碰过之后，规则状态说"这张牌上了桌"，
        ///   3D 卡却被摆回槽位/手牌位 —— 表现就是"牌散开浮在桌上、面板张数对不上"。
        ///   收成一条之后，"谁把牌打出去、打完谁负责摆 3D 卡"只有一个答案。
        ///
        /// 【语义】素材 → 上桌并自动选为启动目标（PlayMaterial → Select(st, true)）；
        ///   法术 → 附魔到刀片（OnSpellCardClicked）。两者都不消耗行动机会（正文 §2.6 / §七-11）。
        ///
        /// 返回 true = 这一下已经被消费掉，调用方不要再做别的（回手牌 / 落槽 / 重排手牌）。
        /// </summary>
        public bool PlayCardV21(PlayCard card)
        {
            if (!V21 || rulesV21 == null || card == null) return false;

            // 只有"能操作"的时候才出得了牌：选刀片核心 / 结算 / 关卡结束 / 暂停设置里都不行
            if (!CanInteract) return false;

            // ★ 先把这张牌从"待放置"里摘干净：它马上就要生效，不该再占着槽位或留在 staged 里
            if (board != null) board.Clear(card);
            staged.Remove(card);

            // ── 法术：附魔到刀片（打完即消失，动画与提示都在 OnSpellCardClicked 里）──
            if (rulesV21.OnSpellCardClicked(card)) return true;

            // ── 素材：上桌（只动规则状态，3D 卡由下面统一对齐）──
            MaterialCard mc = rulesV21.FindHandMaterial(card);
            if (mc == null) return false;      // 不是手牌素材 → 交给调用方（例如点桌上的卡 = 选目标）

            // ★ 素材上桌**不走 ConsumeInto**（那张牌不是被机器吸走的）。
            //   一开始写成"上桌 + 飞进罐口"，结果是同一张牌在桌上和罐口各出现一次 ——
            //   "被吸进去"这个动作用在启动破壁机那一次才成立。
            MaterialState st = rulesV21.PlayMaterial(mc);
            if (st == null) return false;

            // 表现层对齐：手牌重排、桌面素材建卡（PlayMaterial 只动规则状态）
            rulesV21.RebuildHand();
            rulesV21.SyncTableVisuals();
            rulesV21.SyncJuicer();

            lastPlayed = "1 张素材上桌";
            notice = "上桌：" + st.name + "（已自动选为启动目标）　出牌不消耗行动机会，还剩 "
                     + rulesV21.actionPoints + " 次 —— 点「启动破壁机」才消耗";
            return true;
        }

        /// <summary>
        /// v2.1 的「启动破壁机」：消耗 1 行动机会 + 1 刀片H，对选中素材跑一次完整结算。
        /// 结算、计分、爆刀、献祭吞噬全部在 TurnEngine 里（正文 §四）。
        /// </summary>
        public void ActivateJuicer()
        {
            if (rulesV21 == null) return;

            if (!rulesV21.CanActivate)
            {
                notice = rulesV21.BlockReason;
                return;
            }
            if (rulesV21.selected == null)
            {
                notice = "先在桌面上点一张素材当启动目标";
                return;
            }

            GameJam.Rules.TurnResult r = rulesV21.TryActivate(rulesV21.selected);
            if (r == null) return;

            if (r.rejected)
            {
                notice = "启动被拒绝 —— 看结算日志";
                return;
            }

            // 结算摘要：分数变化 + 产出（玩家最关心的两件事）
            string line = r.ScoreLine();
            if (r.produced.Count > 0) line += "　产出 " + r.ProducedText();

            if (rulesV21.levelOver)
            {
                line += rulesV21.bursted ? "　★ 爆刀，关卡结束（分数 ×2）" : "　★ 达到目标分，关卡结束";
            }

            notice = line;

            // 爆刀 / 达标 → 直接进关卡结束（不再等回合一格一格走）
            if (rulesV21.levelOver) phase = TablePhase.LevelEnd;
        }

        /// <summary>v2.1：自动选最新上桌的素材当启动目标。</summary>
        public void SelectNewestTarget()
        {
            if (rulesV21 == null) return;

            rulesV21.SelectNewestTableMaterial();
            notice = rulesV21.selected != null
                ? "启动目标：" + rulesV21.selected.name + "（D " + rulesV21.selected.D + "/" + rulesV21.selected.fullD + "）"
                : "桌面上没有素材了";
        }

        /// <summary>
        /// v2.1：结束本回合（附魔衰减）或结束关卡。
        /// 到一个回合的行动机会用完之后，这是唯一该按的键。
        /// </summary>
        public void EndRoundOrLevel()
        {
            if (rulesV21 == null) return;

            if (rulesV21.levelOver) { phase = TablePhase.LevelEnd; return; }

            rulesV21.EndRound();

            if (rulesV21.levelOver)
            {
                notice = "4 回合已用尽 → 关卡结束，最终分数 " + rulesV21.score;
                phase = TablePhase.LevelEnd;
                return;
            }

            staged.Clear();
            lastPlayed = "";
            notice = "第 " + rulesV21.turnIndex + " 回合开始：行动机会 " + rulesV21.actionPoints + "/" +
                     GameJam.Rules.LevelRun.ActionPointsPerTurn + "；附魔层数 −1：" +
                     rulesV21.blade.layers.Describe();

            rulesV21.RebuildHand();
            rulesV21.SyncTableVisuals();
            rulesV21.SyncJuicer();
        }

        // ══════════════════════════════════════════════════════════════
        //  回合推进
        // ══════════════════════════════════════════════════════════════

        /// <summary>结算界面按「下一回合」。手牌空了就直接进关卡结束。</summary>
        public void NextTurn()
        {
            // ── v2.1 分流 ──────────────────────────────────────────────
            //   关卡结束条件是"4 回合耗尽 / 爆刀 / 主动结束 / 达到目标分"（正文 §八），
            //   **不是手牌空了** —— 手牌空了只要回合还没用完就能继续（D耗尽还在产物）。
            if (V21)
            {
                if (rulesV21.levelOver || rulesV21.turnIndex >= GameJam.Rules.LevelRun.TurnsPerLevel)
                {
                    if (!rulesV21.levelOver && string.IsNullOrEmpty(rulesV21.endReason))
                        rulesV21.endReason = "4 回合耗尽";
                    phase = TablePhase.LevelEnd;
                    return;
                }

                staged.Clear();
                lastPlayed = "";
                notice     = "";
                phase = TablePhase.Select;

                rulesV21.SelectNewestTableMaterial();
                rulesV21.RebuildHand();
                rulesV21.SyncTableVisuals();
                return;
            }

            if (turn.IsHandEmpty)
            {
                phase = TablePhase.LevelEnd;
                return;
            }

            turn.NextTurn();
            staged.Clear();
            skipsUsed  = 0;          // 新回合，行动次数重置
            lastPlayed = "";
            notice     = "";
            phase = TablePhase.Select;

            LayoutHand();
        }

        /// <summary>
        /// 退出关卡：这一把既不算赢也不算输，进度丢掉，回到开场。
        ///
        /// 和"手牌打完"不是一回事 —— 那条路会走到结算界面、由 Level.Finish()
        /// 判定过没过。中途退出只是放弃，不该记一次失败。
        /// </summary>
        public void ExitLevel()
        {
            if (level != null) level.Abandon();

            // 退出关卡 → 回**关卡界面**，不是回开场。
            // 退的是这一关，不是整局游戏。
            staged.Clear();
            paused       = false;
            settingsOpen = false;
            lastPlayed   = "";
            notice       = "";

            if (setup != null) setup.ClearHand();
            V21ClearTable();
            KillBladeCard();

            OpenLevelSelect();
        }

        /// <summary>回到开场：桌面清干净，等玩家重新开始。</summary>
        public void ReturnToTitle()
        {
            paused       = false;
            settingsOpen = false;
            staged.Clear();
            lastPlayed = "";
            notice     = "";

            // 换一关全新的 —— 上一把的分数、杯内食材、进行状态都不该带过来
            level = new Level(GameConfig.Level());
            selections.Clear();

            if (choiceRig != null) choiceRig.ClearDeckCards();
            KillBladeCard();
            V21ClearTable();

            if (setup != null)
            {
                // ★ 用 ClearHand 而不是 RebuildHand：后者会调 DealHand，
                //   而手牌为空时 DealHand 会走兜底分支凭空摆出一张铁块。
                setup.ClearHand();
                setup.deckName = "";
            }

            SyncJuicer();

            if (titleRig != null)
            {
                titleRig.Build();
                phase = TablePhase.Title;
            }
            else
            {
                StartDeckPick();
            }
        }

        /// <summary>没过关时重打这一关。</summary>
        public void RestartLevel()
        {
            if (levels.Count == 0) { OpenLevelSelect(); return; }

            if (levelIndex < 0 || levelIndex >= levels.Count) levelIndex = 0;
            level = new Level(levels[levelIndex]);

            staged.Clear();
            lastPlayed = "";
            notice     = "";

            if (setup != null) setup.ClearHand();
            KillBladeCard();
            V21ClearTable();
            bladeCoreCard = null;

            // ★ 重开本关 = 这一把不要了，存档跟着作废。
            //   【为什么顺手清掉，而不是留着】留着的话玩家重开之后再按「继续」，
            //   会回到**重开之前**的局面 —— 看起来像"重开没生效"，而且他刚做的决定被推翻了。
            //   反过来的代价（想反悔刚才那次重开）远小于这个困惑，所以口径选"清掉"。
            DiscardSave("重新开始本关");

            StartDeckPick();
        }

        // ══════════════════════════════════════════════════════════════
        //  存档 / 读档（v2.1，一个存档位）
        //
        //  【谁能存】只有正式回合（Select 阶段、关卡还没结束）—— 存档要的是"能接着打"，
        //    开局准备 / 结算屏那些中间态存下来没有意义（读回来也没有对应的入口）。
        //  【读档都做哪几件事】按顺序：关卡与牌组定位 → 规则状态落地（两阶段，失败则什么都不动）
        //    → **走现成的权威同步**重摆 3D（手牌 / 桌面 / 刀片卡 / 量筒）→ 阶段回到可操作
        //    → 自检（CompareStates 逐字段比 + ViewSyncSummary 状态与画面一致）。
        //  【一个字节的规则都不在这里】这里只做装配与自检，结算语义全在 TableRulesV21 → TurnEngine。
        // ══════════════════════════════════════════════════════════════

        /// <summary>本局用的牌组 id（读档要按同一副牌组重开这一关，见 FindDeckById）。</summary>
        public string deckId = "";

        /// <summary>本次运行里最近一次保存的状态 —— 读档自检的参照物（跨进程读档时它是 null）。</summary>
        public LevelSaveData lastSavedState;

        /// <summary>现在能不能存（正式回合 + v2.1 + 关卡没结束）。不能存时 <see cref="SaveBlockReason"/> 说明原因。</summary>
        public bool CanSave
        {
            get
            {
                if (!V21 || rulesV21 == null) return false;
                if (levels == null || levels.Count == 0) return false;
                if (rulesV21.levelOver) return false;
                return phase == TablePhase.Select;
            }
        }

        /// <summary>不能存的原因（按钮灰着就得说清为什么，不然玩家只会以为坏了）。</summary>
        public string SaveBlockReason
        {
            get
            {
                if (!V21 || rulesV21 == null) return "旧流程没有存档位（这是 v2.1 的功能）";
                if (levels == null || levels.Count == 0) return "还没进关卡，没有可保存的东西";
                if (rulesV21.levelOver) return "关卡已经结束了 —— 存档是给「接着打」用的";
                if (phase != TablePhase.Select) return "只能在正式回合里保存（现在是 " + phase + "）";
                return "";
            }
        }

        /// <summary>存档文件的完整路径（日志与回报里都写它）。</summary>
        public static string SavePath { get { return TableSaveIO.Path; } }

        /// <summary>
        /// 保存当前关卡状态到磁盘（暂停菜单「保　存」与探针走的是同一个入口）。
        ///
        /// 【写盘之后立刻回读一遍再比对】序列化少写一个字段，表现是"读回来状态不对"，
        ///   而那时候玩家已经打了半个回合、现场早没了。所以在**存档这一步**就把文件读回来，
        ///   和写出去的那份逐字段比一遍：不相等当场 LogWarning 并把差异列出来。
        /// </summary>
        public bool SaveGame()
        {
            if (!CanSave)
            {
                notice = "现在不能保存：" + SaveBlockReason;
                Debug.LogWarning("[V21][存档] 保存被拒：" + SaveBlockReason);
                return false;
            }

            SaveFileDto f = new SaveFileDto();
            f.version     = LevelSave.Version;
            f.kind        = LevelSave.KindName;
            f.savedAt     = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            f.levelIndex  = levelIndex;
            f.levelId     = level != null ? level.Id : "";
            f.levelName   = level != null ? level.Name : "";
            f.deckId      = deckId;
            f.deckName    = setup != null ? setup.deckName : "";
            f.phaseAtSave = phase.ToString();
            f.state       = rulesV21.CaptureSaveState();

            string error;
            if (!TableSaveIO.Write(f, out error))
            {
                notice = "保存失败：" + error;
                Debug.LogError("[V21][存档] 保存失败：" + error);
                return false;
            }

            lastSavedState = f.state;

            Debug.Log("[V21][存档] 已保存｜" + TableSaveIO.PathLine() + "｜" + LevelSaveJson.VersionLine(f) +
                      "｜" + f.Describe());

            // ── 写盘自检：把刚写下去的文件读回来，和写出去的那份逐字段比 ──
            SaveFileDto back;
            string readErr;
            if (!TableSaveIO.TryLoad(out back, out readErr))
            {
                Debug.LogWarning("[V21][存档] 写盘自检失败：文件读不回来 —— " + readErr);
            }
            else
            {
                List<string> diffs = LevelSave.CompareStates(f.state, back.state);
                if (diffs.Count == 0)
                    Debug.Log("[V21][存档] ✓ 写盘自检：读回来的状态与存出去的那份" + LevelSave.DiffText(diffs) +
                              "（" + DescribeStateNumbers(back.state) + "）");
                else
                    Debug.LogWarning("[V21][存档] ★ 写盘自检不通过：" + LevelSave.DiffText(diffs));

                // 顺手把字段清单打一行（回报里要贴"存了哪些字段"）
                Debug.Log("[V21][存档] 文件大小 " + FileSizeText() + "｜字段：version/kind/savedAt/levelIndex/levelId/levelName/deckId/deckName/phaseAtSave" +
                          " + state{turnIndex,actionPoints,score,targetScore,startsThisTurn,blankCount,levelOver,bursted,endReason," +
                          "selectedIndex,selectedAuto,staged[]{spell,index,slot},blade{cardId,name,H,V,layers[4]{kind,decaying,permanent},passives[]{cardId,cardName,text,sentence}}," +
                          "table[]{cardId,cardName,H,D,fullD,V,removed,consumed,startedThisTurn,valueSource}," +
                          "handMaterials[]{cardId,cardName,H,D,V,valueSource,spell},handSpells[]}");
            }

            notice = "已保存到 " + TableSaveIO.Path + "　（" + f.Describe() + "）";
            return true;
        }

        /// <summary>存档文件大小（写盘自检那行日志用）。</summary>
        private static string FileSizeText()
        {
            try
            {
                System.IO.FileInfo fi = new System.IO.FileInfo(TableSaveIO.Path);
                return fi.Exists ? fi.Length + " 字节" : "（文件不在）";
            }
            catch (System.Exception) { return "（量不到）"; }
        }

        /// <summary>
        /// 读档：把存档恢复到场上，并回到"能接着打"的那一屏。
        ///
        /// 【失败时一定什么都不做】所有可能失败的事（读盘 / 解析 / 卡表对不上）都排在
        ///   "改任何现有状态"之前；任何一步不对就明确报错、把「继续」按回禁用，现场原封不动。
        /// </summary>
        public bool ContinueFromSave()
        {
            if (!V21 || rulesV21 == null)
            {
                FailContinue("存档是 v2.1 规则的；当前「规则模式」是旧流程 —— 把它切回 v2.1 再读");
                return false;
            }

            SaveFileDto file;
            string error;
            if (!TableSaveIO.TryLoad(out file, out error))
            {
                FailContinue(error);
                return false;
            }

            Debug.Log("[V21][存档] 开始读档｜" + TableSaveIO.PathLine() + "｜" + LevelSaveJson.VersionLine(file) +
                      "｜存档里写着：" + file.Describe() + "（存于阶段 " + file.phaseAtSave + "）");

            // ── ① 先把状态**全建好**（这一步只读存档；失败则现场原封不动）──
            BuiltSaveState built;
            if (!rulesV21.TryBuildSaveState(file.state, out built, out error))
            {
                FailContinue(error);
                return false;
            }

            // ── ② 关卡与牌组定位 ──
            if (levels == null || levels.Count == 0)
            {
                levels = GameConfig.Levels();
                if (levels.Count == 0) levels.Add(GameConfig.Level());
            }

            if (file.levelIndex < 0 || file.levelIndex >= levels.Count)
            {
                FailContinue("存档里的关卡下标 " + file.levelIndex + " 超出了关卡表（共 " + levels.Count +
                             " 关）—— 配置可能改过，拒绝读到别的关上");
                return false;
            }

            int idx = file.levelIndex;
            Deck deck = FindDeckById(file.deckId);
            if (deck == null && !string.IsNullOrEmpty(file.deckId))
                Debug.LogWarning("[V21][存档] 存档里的牌组 id「" + file.deckId +
                                 "」在配置里找不到 → 退回默认牌组（刀片与手牌仍按存档恢复，不受影响）");

            // ── ③ 到这里才动现场：这一关按存档重开（旧状态机的壳），再把规则状态覆盖上去 ──
            levelIndex = idx;
            level = new Level(levels[idx]);
            level.Begin(deck, GameConfig.DefaultBlade());

            cup = new CupSim();
            skipsUsed = 0;
            staged.Clear();
            bladeCoreCard = null;
            lastPlayed    = "";
            paused        = false;
            settingsOpen  = false;

            if (setup != null)
            {
                setup.deckName = file.deckName;
                setup.ClearHand();          // 先清干净：RebuildHand 会照规则侧重摆
            }

            KillBladeCard();
            V21ClearTable();

            deckId = file.deckId;

            string passiveError;
            rulesV21.CommitSaveState(built, out passiveError);

            // ── ④ 表现层：**全部走现成的权威同步**（一个坐标都不自己算）──
            //   ★ 从开场进来时先把开场收掉：titleRig.Clear() 同时负责"机位还回桌面视角"
            //     （见那里的说明）—— 自己另写一遍就会漏掉机位，读档后玩家看到的还是开场那一屏的构图。
            //     走"新游戏"那条路时是同一个调用（ConfirmTitleStart → titleRig.Clear）。
            if (phase == TablePhase.Title && titleRig != null) titleRig.Clear();

            BuildBladeCard();               // 刀片卡 + 「刀 片」标记
            rulesV21.RebuildHand();         // 手牌重摆（含残留清扫 + 手牌自检）
            RestoreStagedFromSave(built.rules.staged);   // 待放置的牌（v2.1 常态为空）
            rulesV21.SyncTableVisuals();    // 桌面素材按级联重摆（含布局自检）
            SyncJuicer();                   // 量筒 / 得分板按分数刷新
            if (setup != null) setup.RefreshSlotLabels();

            // ── ⑤ 阶段：关卡已结束的存档回到结算屏，否则回到"能接着打"的出牌阶段 ──
            phase = rulesV21.levelOver ? TablePhase.LevelEnd : TablePhase.Select;

            // ── ⑥ 自检：逐字段比对 + 状态与画面一致 ──
            LevelSaveData now = rulesV21.CaptureSaveState();
            List<string> diffs = LevelSave.CompareStates(file.state, now);

            if (diffs.Count == 0)
            {
                Debug.Log("[V21][存档] ✓ 读档自检 CompareStates：读回来的状态与存档前逐字段全等" +
                          "（存档前 " + DescribeStateNumbers(file.state) + "）" +
                          "｜读回来后 " + DescribeStateNumbers(now));
            }
            else
            {
                Debug.LogWarning("[V21][存档] ★ 读档自检不通过（" + diffs.Count + " 处不同）：" +
                                 LevelSave.DiffText(diffs) +
                                 "\n   存档前 " + DescribeStateNumbers(file.state) +
                                 "\n   读回来后 " + DescribeStateNumbers(now));
            }

            Debug.Log("[V21][存档] ✓ 读档完成｜" + rulesV21.ViewSyncSummary() +
                      "｜阶段 " + phase + "｜" + StateSummary());

            notice = "已读取存档：" + file.Describe();
            return true;
        }

        /// <summary>
        /// 读档时把"投放区里待放置的那几张"恢复出来（**必须在 RebuildHand 之后调**）。
        ///
        /// 【为什么在 RebuildHand 之后】待放置是按"手牌下标"记的，得先有新的 3D 手牌卡，
        ///   才能用 bindingMaterial / bindingSpell 把下标认回那几张卡（名字会随 D 变，不能用名字）。
        /// 【v2.1 常态是空表】落槽即结算，玩家打不出这个状态；这一段是给旧流程的摆法和探针兜底的。
        /// </summary>
        public void RestoreStagedFromSave(List<SaveStaged> list)
        {
            staged.Clear();
            if (list == null || list.Count == 0) return;
            if (rulesV21 == null || setup == null || setup.hand == null) return;

            int n = 0;
            for (int i = 0; i < list.Count; i++)
            {
                SaveStaged g = list[i];
                if (g == null) continue;

                PlayCard pc = FindStagedHandView(g);
                if (pc == null)
                {
                    Debug.LogWarning("[V21][存档] 投放区待放置第 " + (i + 1) + " 项（手牌下标记不住的那张）" +
                                     "找不到对应的 3D 手牌卡 → 这一张不摆回投放区");
                    continue;
                }

                staged.Add(pc);
                if (g.slot >= 0 && board != null && board.Place(g.slot, pc))
                    pc.SnapTo(board.SlotPosition(g.slot));
                n++;
            }

            Debug.Log("[V21][存档] 投放区待放置已恢复 " + n + "/" + list.Count + " 张（" + StagedText + "）");
        }

        /// <summary>按存档记的（手牌下标 + 素材/法术）找回那张 3D 手牌卡。</summary>
        private PlayCard FindStagedHandView(SaveStaged g)
        {
            for (int i = 0; i < setup.hand.Count; i++)
            {
                PlayCard pc = setup.hand[i];
                if (pc == null) continue;

                if (g.spell)
                {
                    if (pc.bindingSpell != null && g.index >= 0 && g.index < rulesV21.handSpells.Count &&
                        rulesV21.handSpells[g.index] == pc.bindingSpell) return pc;
                }
                else
                {
                    if (pc.bindingMaterial != null && g.index >= 0 && g.index < rulesV21.hand.Count &&
                        rulesV21.hand[g.index] == pc.bindingMaterial) return pc;
                }
            }
            return null;
        }

        /// <summary>读档失败：明确报错 + 让开场那个「继续」回到禁用状态，**现场一个字节都不动**。</summary>
        private void FailContinue(string why)
        {
            Debug.LogError("[V21][存档] 读取失败：" + why + "　—— 「继续」保持/回到禁用状态，当前局面不动" +
                           "（存档：" + TableSaveIO.Path + "）");
            notice = "读档失败：" + why;

            // 停在场界面时重建一次开场 —— 它建牌子时会重新判断"这一档能不能读"，
            // 于是坏档当场变灰（用户报的就是"改坏存档后按继续"这一下）。
            if (phase == TablePhase.Title && titleRig != null) titleRig.Build();
        }

        /// <summary>"存档前 / 读回来后"那两行数字（逐条对照用，日志里一眼能比）。</summary>
        public static string DescribeStateNumbers(LevelSaveData s)
        {
            if (s == null) return "（没有状态）";

            return "回合 " + s.turnIndex + "/" + GameJam.Rules.LevelRun.TurnsPerLevel
                 + "｜行动机会 " + s.actionPoints + "/" + GameJam.Rules.LevelRun.ActionPointsPerTurn
                 + "｜分数 " + s.score + "/" + s.targetScore
                 + "｜刀片 " + s.blade.name + " H" + s.blade.H + " V" + s.blade.V
                 + "｜附魔 " + s.LayersText()
                 + "｜桌面 " + s.LiveTableCount() + " 张"
                 + "｜手牌 " + s.handMaterials.Count + " 素材 + " + s.handSpells.Count + " 法术"
                 + "｜被动 " + (s.blade.passives != null ? s.blade.passives.Count : 0) + " 条"
                 + "｜空白卡 " + s.blankCount;
        }

        /// <summary>当前规则状态的一行摘要（探针在存档前后各打一次，两条一比就知道有没有变）。</summary>
        public string StateSummary()
        {
            if (rulesV21 == null) return "（规则侧不在）";

            return "回合 " + rulesV21.turnIndex + "/" + GameJam.Rules.LevelRun.TurnsPerLevel
                 + "｜行动机会 " + rulesV21.actionPoints + "/" + GameJam.Rules.LevelRun.ActionPointsPerTurn
                 + "｜分数 " + rulesV21.score + "/" + rulesV21.targetScore
                 + "｜刀片 " + (rulesV21.blade != null ? rulesV21.blade.name : "（无）")
                 + " H" + (rulesV21.blade != null ? rulesV21.blade.H : 0)
                 + " V" + (rulesV21.blade != null ? rulesV21.blade.V : 0)
                 + "｜附魔 " + (rulesV21.blade != null ? rulesV21.blade.layers.Describe() : "（无）")
                 + "｜桌面 " + rulesV21.LiveTableCount() + " 张"
                 + "｜手牌 " + rulesV21.hand.Count + " 素材 + " + rulesV21.handSpells.Count + " 法术"
                 + "｜被动 " + rulesV21.BladePassiveCount + " 条"
                 + "｜空白卡 " + rulesV21.blankCount
                 + "｜阶段 " + phase;
        }

        /// <summary>按 id 找牌组（找不到返回配置里第一副；一副都没有返回 null）。</summary>
        private static Deck FindDeckById(string id)
        {
            List<Deck> decks = GameConfig.Decks();
            if (decks == null || decks.Count == 0) return null;

            if (!string.IsNullOrEmpty(id))
                for (int i = 0; i < decks.Count; i++)
                    if (decks[i] != null && decks[i].id == id) return decks[i];

            return decks[0];
        }

        /// <summary>把存档作废（"重新开始本关"调它；详见那里的说明）。</summary>
        public void DiscardSave(string why)
        {
            lastSavedState = null;

            if (!TableSaveIO.Exists) return;

            string error;
            if (TableSaveIO.Delete(out error))
                Debug.Log("[V21][存档] " + why + " → 已清掉存档（" + TableSaveIO.Path + "）");
            else
                Debug.LogWarning("[V21][存档] " + why + "：清存档失败 —— " + error);
        }

        /// <summary>退出游戏。编辑器里是停止 Play，出包后是真退出。</summary>
        public void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>关卡结束 → 总结算。到这里才真正判定这一关过没过。</summary>
        public void ShowLevelResult()
        {
            if (level != null) level.Finish();
            phase = TablePhase.LevelResult;
        }

        /// <summary>本关是否达标。判定在 Level 里，这里只是转发。</summary>
        public bool Passed { get { return level != null && level.IsCleared; } }

        /// <summary>
        /// 把得分推给罐子的液面。
        ///
        /// target 一定要挡住 0：JuicerRig.SetScore 里算的是 score / target，
        /// 目标分为 0 会得到 NaN，液面高度直接变成乱值。
        /// </summary>
        public void SyncJuicer()
        {
            if (juicer == null) return;

            // v2.1 的分数在 rulesV21 里；旧 TurnState 的那个只跟不过来一半是不行的
            // （两边各推一次液面，会出现"按一下按钮液面跳回去"的鬼影）
            if (V21) { juicer.SetScore(rulesV21.score, Mathf.Max(1, rulesV21.targetScore)); return; }

            juicer.SetScore(turn.score, Mathf.Max(1, turn.targetScore));
        }

        /// <summary>v2.1：把上一关留在桌面上的素材 3D 卡清掉（退关卡 / 重开 / 回开场都要）。</summary>
        /// <summary>
        /// 这个阶段是不是"菜单类"（开场 / 选牌组 / 选关 / 关卡结算）——
        /// 菜单上不该出现上一关留下的桌面素材卡。
        ///
        /// 【为什么按阶段判断，而不是在每个入口清一次】
        ///   入口有四五条（回开场、退关卡、进选关、进牌组选择、打完一关回选关…），
        ///   逐条补已经漏过两次：先漏"打完一关回选关"，再漏"退关卡回牌组选择"。
        ///   阶段是**状态的函数**，按它判断不会随新增路径而失效。
        ///   注意 LevelEnd 不算菜单类：爆刀/达标那一下还要在桌面上播冲压与结算表现。
        /// </summary>
        private static bool IsMenuPhase(TablePhase p)
        {
            return p == TablePhase.Title
                || p == TablePhase.DeckPick
                || p == TablePhase.LevelSelect
                || p == TablePhase.LevelResult;
        }

        private void V21ClearTable()
        {
            if (rulesV21 != null) rulesV21.ClearTable();
        }

        // ══════════════════════════════════════════════════════════════
        //  每帧
        // ══════════════════════════════════════════════════════════════

        void Update()
        {
            // ★ 兜底：只要当前停在"菜单类"阶段，桌面上就不该留着上一关的素材卡。
            //   入口级的清理写过三次了（回开场 / 进选关 / 进牌组选择），但"漏一条路径"已经发生两次
            //   （先是"打完一关回选关"漏，再是"退关卡回牌组选择"漏）—— 与其继续打补丁，
            //   不如按**阶段**统一兜一句：菜单阶段本来就不该有牌。
            //   放在 Update 最前面：暂停/设置面板开着时也照样清（那两种情况更不该留着牌）。
            if (V21 && rulesV21 != null && IsMenuPhase(phase)) rulesV21.ClearTable();

            // 暂停 / 设置面板开着的时候，模拟也停住 ——
            // 否则暂停回来会发现模拟凭空跑了半截
            if (paused || settingsOpen) return;

            // v2.1 没有"模拟中"这个阶段（分数由引擎即时结算，不等粒子模拟），
            // 所以这一段整块跳过 —— 但旧流程那一行都不删。
            if (V21) return;

            if (phase != TablePhase.Simulating) return;

            cup.Tick(Time.deltaTime);

            if (!cup.Finished) return;

            // 规格：模拟结束后清除所有飘字，再进回合结算
            cup.ClearFloaters();

            // 得分以 cup 为准 —— 它跨回合累积，turn.score 跟着它走，
            // 免得两边各加一次、越差越多
            turn.score = cup.totalScore;

            SyncJuicer();
            phase = TablePhase.TurnResult;
        }

        // ══════════════════════════════════════════════════════════════
        //  手牌布局
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 手牌第 index 张（共 count 张）应该摆在哪。
        ///
        /// 发牌和重排共用这一套算法 —— 两边各写一遍的话，
        /// 开局摆出来的位置和投放之后收拢出来的位置迟早对不上。
        /// </summary>
        public static void HandSlot(int index, int count, out Vector3 pos, out Vector3 euler)
        {
            if (count < 1) count = 1;

            float startX = -(count - 1) * HandGap * 0.5f;
            float fan    = (index - (count - 1) * 0.5f) * 4f;   // 微微的扇形，朝向相机一侧张开

            pos   = new Vector3(startX + index * HandGap, 0f, HandZ);
            euler = new Vector3(0f, fan, 0f);
        }

        /// <summary>
        /// 重新排一遍手牌。
        ///
        /// 投放掉一张之后剩下的牌要往中间收拢，否则手牌中间会留一个洞。
        /// 已经放进投放区的那张不动 —— 它有自己的位置。
        /// </summary>
        public void LayoutHand()
        {
            if (setup == null || setup.hand == null) return;

            List<PlayCard> h = setup.hand;

            // 被投出去的已经从表里摘掉了，但对象还在飞 —— 顺手清掉空引用
            for (int i = h.Count - 1; i >= 0; i--)
                if (h[i] == null) h.RemoveAt(i);

            int n = 0;
            for (int i = 0; i < h.Count; i++)
                if (h[i] != null && h[i].slotIndex < 0) n++;

            if (n == 0) return;

            int k = 0;
            for (int i = 0; i < h.Count; i++)
            {
                PlayCard c = h[i];
                if (c == null || c.slotIndex >= 0) continue;

                Vector3 pos, euler;
                HandSlot(k, n, out pos, out euler);
                c.SetHome(pos, euler);
                k++;
            }

            // 手牌数量/位置变了，"手牌特写"机位也要跟着重算 ——
            // 不然五张牌的时候最外侧两张会被切在画面外（机位是按固定位置注册的）。
            setup.ReframeHandView();
        }

        // ══════════════════════════════════════════════════════════════
        //  给 HUD 用的只读文本
        // ══════════════════════════════════════════════════════════════

        /// <summary>待投放的牌：几张、都是什么。</summary>
        public string StagedText
        {
            get
            {
                if (staged.Count == 0) return "（还没放牌）";

                List<string> names = new List<string>();
                for (int i = 0; i < staged.Count; i++)
                    if (staged[i] != null) names.Add(staged[i].DisplayName);

                return names.Count + " 张：" + string.Join("、", names.ToArray());
            }
        }

        /// <summary>手牌摘要：食材和模块一起列。</summary>
        public string HandSummary()
        {
            if (turn.HandCount == 0) return "（空）";

            List<string> names = new List<string>();

            // 一条循环 —— 手牌现在是一个列表
            if (turn.hand != null)
                for (int i = 0; i < turn.hand.Count; i++)
                    if (turn.hand[i] != null) names.Add(turn.hand[i].name);

            return names.Count > 0 ? string.Join("、", names.ToArray()) : "（空）";
        }

        /// <summary>已经投出去的模块名字（顶部信息栏显示"已应用"）。</summary>
        public string AppliedModulesText()
        {
            if (turn.appliedModules == null || turn.appliedModules.Count == 0) return "（无）";

            List<string> names = new List<string>();
            for (int i = 0; i < turn.appliedModules.Count; i++)
                if (turn.appliedModules[i] != null) names.Add(turn.appliedModules[i].name);

            return names.Count > 0 ? string.Join("、", names.ToArray()) : "（无）";
        }
    }
}
