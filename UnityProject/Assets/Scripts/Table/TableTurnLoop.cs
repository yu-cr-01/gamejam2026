using System.Collections.Generic;
using UnityEngine;
using GameJam.Data;

namespace GameJam.Prototype
{
    /// <summary>3D 桌面上的回合阶段。</summary>
    public enum TablePhase
    {
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
    /// 规则完全相同，共用同一个 TurnState / GameConfig：
    ///   每回合从手牌里挑 1 张（食材或变速模块）→ 放进投放区 → 确认投放
    ///   → 模拟 → 回合结算 → 下一回合
    ///   手牌用完 → 关卡结束 → 总结算
    /// 区别只在"表现"：那边是 IMGUI 文字，这边是 3D 卡牌 + 榨汁机。
    /// 所以这里绝不能再自己维护一份手牌数据 —— 那样两边迟早会对不上。
    ///
    /// 【桌面上的映射】
    ///   手牌     = 桌面近端那排卡（食材 + 模块混排）
    ///   投放区   = 桌面中间那 8 个卡槽（每回合只能用一张）
    ///   杯子     = 榨汁机的玻璃罐，液面就是得分
    ///   模拟     = 冲压动画
    /// </summary>
    public class TableTurnLoop : MonoBehaviour
    {
        public TableSetup setup;
        public JuicerRig  juicer;
        public TableBoard board;

        /// <summary>本关的过程数据（手牌 / 刀片 / 杯内 / 得分）</summary>
        public TurnState turn = new TurnState();

        public TablePhase phase = TablePhase.Select;

        /// <summary>已经放进投放区、等玩家按确认的那张牌（null = 还没放）</summary>
        public PlayCard Staged { get; private set; }

        /// <summary>本回合投出去的东西的名字，模拟中/结算界面显示</summary>
        public string lastPlayed = "";

        /// <summary>本次"模拟中"开始的时刻</summary>
        public float simulatingSince = -1f;

        /// <summary>模拟停留时长。今天没有真模拟，只是个过场。</summary>
        private const float SimulateSeconds = 1.4f;

        /// <summary>手牌区布局 —— 和 TableSetup 共用同一组常量，免得两边各写一个数。</summary>
        public const float HandZ   = -0.58f;
        public const float HandGap = 0.30f;

        /// <summary>现在能不能拖动卡牌（只有选牌阶段可以）。</summary>
        public bool CanInteract { get { return phase == TablePhase.Select; } }

        /// <summary>手牌是不是真的空了（关卡结束的判据）。</summary>
        public bool HandEmpty { get { return turn.IsHandEmpty; } }

        // ══════════════════════════════════════════════════════════════
        //  开局
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 开一局：按配置表准备手牌和刀片。
        ///
        /// 牌组固定取配置表里的第一副。3D 桌面这边还没做"选牌组"的界面，
        /// 那两步选择属于另一条分支（Flow2D / Board2D），不是回合循环本身的事。
        /// </summary>
        public void Begin()
        {
            GameConfig.EnsureLoaded();

            List<Deck> decks = GameConfig.Decks();
            Deck deck = decks.Count > 0 ? decks[0] : null;

            turn.PrepareLoadout(GameConfig.Level(), deck, GameConfig.DefaultBlade());

            if (setup != null) setup.deckName = deck != null ? deck.name : "";

            Staged          = null;
            lastPlayed      = "";
            simulatingSince = -1f;
            phase = turn.IsHandEmpty ? TablePhase.LevelEnd : TablePhase.Select;

            SyncJuicer();
        }

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
        //  投放区
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 某张牌落进了投放区。
        ///
        /// 【每回合只留一张】
        /// 再放一张会把上一张**退回手牌**，而不是让它留在桌上 ——
        /// 留在桌上玩家会以为两张都会被投进去，然后发现只结算了一张。
        /// 规则是每回合一张，界面上就得让这件事一眼可见。
        /// </summary>
        public void Stage(PlayCard card)
        {
            if (card == null) return;
            if (Staged == card) return;

            if (Staged != null) Release(Staged);

            Staged = card;
            lastPlayed = "";
        }

        /// <summary>把一张牌从投放区退回手牌（等于"我反悔了"）。</summary>
        public void Release(PlayCard card)
        {
            if (card == null) return;

            if (board != null) board.Clear(card);
            card.ReturnHome();

            if (Staged == card) Staged = null;

            LayoutHand();
        }

        // ══════════════════════════════════════════════════════════════
        //  确认投放
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 按下「确认投放」：牌真正进数据层，然后飞进罐子。
        ///
        /// 落子权在确认键上，不在"放上投放区"那一刻 ——
        /// 放上去只是摆好，玩家还能拖回来。
        /// </summary>
        public void Confirm()
        {
            if (phase != TablePhase.Select) return;

            PlayCard card = Staged;
            if (card == null) return;

            Staged = null;

            string playedName = card.DisplayName;

            // ── 1. 先动数据层 ──
            if (card.IsModule)
            {
                int idx = turn.modules != null ? turn.modules.IndexOf(card.module) : -1;
                SpeedModule m = idx >= 0 ? turn.PlayModule(idx) : null;
                if (m != null) playedName = m.name;
            }
            else
            {
                int idx = turn.hand != null ? turn.hand.IndexOf(card.data) : -1;
                Ingredient ing = idx >= 0 ? turn.PlayFromHand(idx) : null;
                if (ing != null) playedName = ing.name;
            }

            lastPlayed = playedName;

            // ── 2. 再动表现层 ──
            if (board != null) board.Clear(card);
            if (setup != null && setup.hand != null) setup.hand.Remove(card);

            Vector3 mouth = juicer != null
                ? juicer.MouthWorld
                : card.transform.position + Vector3.up * 0.4f;
            card.ConsumeInto(mouth, 0.8f);

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
            Staged     = null;
            lastPlayed = "";
            phase = TablePhase.Select;

            LayoutHand();
        }

        /// <summary>关卡结束 → 总结算。</summary>
        public void ShowLevelResult()
        {
            phase = TablePhase.LevelResult;
        }

        /// <summary>本关是否达标（今天得分恒为 0，所以实际上一定不达标）。</summary>
        public bool Passed { get { return turn.score >= turn.targetScore; } }

        // ══════════════════════════════════════════════════════════════
        //  每帧
        // ══════════════════════════════════════════════════════════════

        void Update()
        {
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

        /// <summary>待投放的那张牌的名字（没放就是提示语）。</summary>
        public string StagedText
        {
            get { return Staged != null ? Staged.DisplayName : "（还没放牌）"; }
        }

        /// <summary>手牌摘要：食材和模块一起列。</summary>
        public string HandSummary()
        {
            if (turn.HandCount == 0) return "（空）";

            List<string> names = new List<string>();

            if (turn.hand != null)
                for (int i = 0; i < turn.hand.Count; i++)
                    if (turn.hand[i] != null) names.Add(turn.hand[i].name);

            // 已经放进投放区的那张也算还在手上，别让它从摘要里消失
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
