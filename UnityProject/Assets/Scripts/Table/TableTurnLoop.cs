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
                if (phase == TablePhase.DeckPick)  return FindChoice(GameConfig.DeckPickId);
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
        /// 打开关卡界面：桌上一排关卡卡。
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
                choiceRig.BuildLevelCards(levels, levelIndex);
                phase = TablePhase.LevelSelect;
                return;
            }

            StartDeckPick();   // 没有选关界面就直接进牌组
        }

        /// <summary>在关卡界面点了一张卡、按了确认。</summary>
        public void ConfirmLevelSelect()
        {
            if (phase != TablePhase.LevelSelect) return;

            int idx = choiceRig != null ? choiceRig.DeckSelected : levelIndex;
            if (idx < 0 || idx >= levels.Count)
            {
                notice = "先在桌上点一张关卡卡";
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

            if (deckChoice != null && deckChoice.OptionCount > 0 && choiceRig != null)
            {
                choiceRig.BuildDeckCards(deckChoice);
                phase = TablePhase.DeckPick;
                return;
            }

            // 配置里没有牌组环节（或者还没接上选择界面）→ 退回"直接用第一副"
            BeginWithDefaultDeck();
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
                notice = "先在桌上点一张牌组卡";
                return;
            }

            Choice c = FindChoice(GameConfig.DeckPickId);
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
            if (V21)
            {
                MaterialCard core = rulesV21.coreCard != null ? rulesV21.coreCard : rulesV21.CoreCandidate();
                if (core == null || core.card == null) return;

                bladeCard = CardFactory.Create(Card.Of(core.card), setup.cardsRoot,
                                               TableChoiceRig.BladeSpot, Vector3.zero);
                return;
            }

            Card c = turn.blade != null ? Card.Of(turn.blade) : null;
            if (c == null) return;

            bladeCard = CardFactory.Create(c, setup.cardsRoot,
                                           TableChoiceRig.BladeSpot, Vector3.zero);
        }

        /// <summary>
        /// v2.1：玩家点了手牌里的一张素材，把它记为刀片核心的**候选**。
        /// 真正生效在 ConfirmBladePick —— 和旧流程一样，点只是选，确认才算数。
        /// </summary>
        public bool PickCoreCandidate(MaterialCard mc)
        {
            if (mc == null || mc.card == null) return false;

            bladeCoreCard = mc;

            // 刀片位换成这张卡，同时**整副手牌重建一次** —— 否则卡牌的 homePosition
            // 还停在旧位置，确认之后 LayoutHand 会把剩下的牌重新排一遍，
            // 那张被点过的牌会先跳回去再让位，看起来像点错了。
            rulesV21.RebuildHand();
            BuildBladeCard();

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
        /// </summary>
        public bool CanStageInto(int slot, PlayCard card)
        {
            if (card == null || card.card == null) return false;

            if (slot == SlotMaterial) return card.card.IsMaterial;
            if (slot == SlotSpell)    return card.card.IsSpell;

            return true;
        }

        /// <summary>放错槽了：说清楚这个槽收什么、你手里这张是什么。</summary>
        public void RejectSlot(PlayCard card, int slot)
        {
            string want = (slot == SlotMaterial) ? "素材" : "法术";
            string got  = (card != null && card.card != null) ? card.card.TypeTag : "?";

            notice = (slot == SlotMaterial ? "素材槽" : "法术槽")
                   + "只放" + want + "　——　" + (card != null ? card.DisplayName : "?")
                   + " 是" + got;
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

            StartDeckPick();
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
