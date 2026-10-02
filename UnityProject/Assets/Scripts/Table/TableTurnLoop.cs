using System.Collections.Generic;
using UnityEngine;
using GameJam.Data;

namespace GameJam.Prototype
{
    /// <summary>3D 桌面上的阶段。</summary>
    public enum TablePhase
    {
        /// <summary>开场：桌上摆着书、木牌和一根蜡烛</summary>
        Title,

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

        /// <summary>开场界面（书 / 木牌 / 蜡烛）</summary>
        public TableTitleRig  titleRig;

        /// <summary>本关的过程数据（手牌 / 刀片 / 杯内 / 得分）</summary>
        public TurnState turn = new TurnState();

        /// <summary>玩家做过的选择 —— 和 TurnState 互不依赖，各记各的</summary>
        public SelectionLog selections = new SelectionLog();

        /// <summary>当前关卡（选择环节、目标分都在它身上）</summary>
        public LevelData level;

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

        /// <summary>模拟停留时长。今天没有真模拟，只是个过场。</summary>
        private const float SimulateSeconds = 1.4f;

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
        /// LevelData 只暴露 ChoiceAt/ChoiceCount，没有按 id 查的接口 ——
        /// 走一遍就够了，不值得为它给数据层加方法。
        /// </summary>
        public Choice FindChoice(string id)
        {
            if (level == null) return null;

            for (int i = 0; i < level.ChoiceCount; i++)
            {
                Choice c = level.ChoiceAt(i);
                if (c != null && c.id == id) return c;
            }
            return null;
        }

        // ══════════════════════════════════════════════════════════════
        //  开局：牌组 → 刀片
        // ══════════════════════════════════════════════════════════════

        /// <summary>开一局。叫 Begin 是因为它由 TableSetup 在装配完成之后调用。</summary>
        public void Begin()
        {
            GameConfig.EnsureLoaded();
            level = GameConfig.Level();

            selections.Clear();
            turn.Reset();

            staged.Clear();
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

            StartDeckPick();
        }

        /// <summary>点了桌上那本书（新游戏）：收起开场，进入三选一牌组。</summary>
        public void ConfirmTitleStart()
        {
            if (phase != TablePhase.Title) return;

            if (titleRig != null) titleRig.Clear();

            StartDeckPick();
        }

        /// <summary>选择①：三选一牌组。</summary>
        private void StartDeckPick()
        {
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
            turn.PrepareLoadout(level, deck, GameConfig.DefaultBlade());

            if (setup != null)
            {
                setup.deckName = deck != null ? deck.name : "";
                setup.RebuildHand();
            }

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

            Choice c = FindChoice(GameConfig.BladePickId);
            if (c != null)
            {
                string[] picked = turn.blade != null
                    ? new string[] { turn.blade.id }
                    : new string[0];
                selections.Record(c.id, 0, picked);
            }

            KillBladeCard();

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

            if (turn.blade == null || setup == null || setup.cardsRoot == null) return;

            bladeCard = CardFactory.Create(turn.blade, setup.cardsRoot,
                                           TableChoiceRig.BladeSpot, Vector3.zero);
        }

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
        private void ReplaceHandCard(int index, Ingredient data)
        {
            if (setup == null || setup.hand == null) return;
            if (index < 0 || index >= setup.hand.Count) return;

            PlayCard old = setup.hand[index];
            if (old == null) return;

            Vector3 home  = old.homePosition;
            Vector3 euler = old.homeEuler;

            CardFactory.DestroySafe(old.gameObject);

            setup.hand[index] = CardFactory.Create(data, setup.cardsRoot, home, euler);
        }

        // ══════════════════════════════════════════════════════════════
        //  投放区
        // ══════════════════════════════════════════════════════════════

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

            staged.Add(card);
            lastPlayed = "";
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
            if (staged.Count == 0) return;

            // 先整批拿出来再处理 —— 处理过程会动 setup.hand，边遍历边改会漏牌
            List<PlayCard> batch = new List<PlayCard>(staged);
            staged.Clear();

            List<string> played = new List<string>();

            for (int i = 0; i < batch.Count; i++)
            {
                PlayCard card = batch[i];
                if (card == null) continue;

                string name = card.DisplayName;

                // ── 1. 先动数据层 ──
                if (card.IsModule)
                {
                    int idx = turn.modules != null ? turn.modules.IndexOf(card.module) : -1;
                    SpeedModule m = idx >= 0 ? turn.PlayModule(idx) : null;
                    if (m != null) name = m.name;
                }
                else
                {
                    int idx = turn.hand != null ? turn.hand.IndexOf(card.data) : -1;
                    Ingredient ing = idx >= 0 ? turn.PlayFromHand(idx) : null;
                    if (ing != null) name = ing.name;
                }

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

            // ── 3. 进模拟 ──
            phase = TablePhase.Simulating;
            simulatingSince = Time.time;
            if (juicer != null) juicer.PlayStamp();
        }

        // ══════════════════════════════════════════════════════════════
        //  回合推进
        // ══════════════════════════════════════════════════════════════

        /// <summary>结算界面按「下一回合」。手牌空了就直接进关卡结束。</summary>
        public void NextTurn()
        {
            if (turn.IsHandEmpty)
            {
                phase = TablePhase.LevelEnd;
                return;
            }

            turn.NextTurn();
            staged.Clear();
            lastPlayed = "";
            phase = TablePhase.Select;

            LayoutHand();
        }

        /// <summary>回到开场：桌面清干净，重新开一局。</summary>
        public void ReturnToTitle()
        {
            paused       = false;
            settingsOpen = false;
            staged.Clear();
            lastPlayed = "";
            notice     = "";

            turn.Reset();
            selections.Clear();

            if (choiceRig != null) choiceRig.ClearDeckCards();
            KillBladeCard();

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

        /// <summary>退出游戏。编辑器里是停止 Play，出包后是真退出。</summary>
        public void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>关卡结束 → 总结算。</summary>
        public void ShowLevelResult()
        {
            phase = TablePhase.LevelResult;
        }

        /// <summary>本关是否达标（今天得分恒为 0，所以实际上一定不达标）。</summary>
        public bool Passed { get { return turn.score >= turn.targetScore; } }

        /// <summary>
        /// 把得分推给罐子的液面。
        ///
        /// target 一定要挡住 0：JuicerRig.SetScore 里算的是 score / target，
        /// 目标分为 0 会得到 NaN，液面高度直接变成乱值。
        /// </summary>
        public void SyncJuicer()
        {
            if (juicer == null) return;
            juicer.SetScore(turn.score, Mathf.Max(1, turn.targetScore));
        }

        // ══════════════════════════════════════════════════════════════
        //  每帧
        // ══════════════════════════════════════════════════════════════

        void Update()
        {
            // 暂停 / 设置面板开着的时候，模拟计时也停住 ——
            // 否则暂停回来会发现"模拟中"直接跳过了
            if (paused || settingsOpen) return;

            if (phase != TablePhase.Simulating) return;

            bool longEnough = (Time.time - simulatingSince) >= SimulateSeconds;

            // 冲压动画没放完就先不切，否则结算面板会盖在机器动作上
            bool machineIdle = (juicer == null) || !juicer.IsStamping;

            if (longEnough && machineIdle) phase = TablePhase.TurnResult;
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

            if (turn.hand != null)
                for (int i = 0; i < turn.hand.Count; i++)
                    if (turn.hand[i] != null) names.Add(turn.hand[i].name);

            if (turn.modules != null)
                for (int i = 0; i < turn.modules.Count; i++)
                    if (turn.modules[i] != null) names.Add(turn.modules[i].name);

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
