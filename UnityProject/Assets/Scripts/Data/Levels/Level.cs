using System;

namespace GameJam.Data
{
    /// <summary>一关的运行状态。</summary>
    public enum LevelState
    {
        /// <summary>还没开始</summary>
        Ready,

        /// <summary>进行中</summary>
        Playing,

        /// <summary>打过目标分了</summary>
        Cleared,

        /// <summary>手牌打完但没到目标分</summary>
        Failed,

        /// <summary>中途退出（既没赢也没输）</summary>
        Abandoned,
    }

    /// <summary>
    /// 一关的**运行时**对象。
    ///
    /// 【和 LevelData 的分工】
    ///   LevelData —— 这一关是**什么**：id、名字、目标分、自带的选择环节、关卡规则。
    ///                它是配置，可以 Clone、可以存盘，一局里不会变。
    ///   Level     —— 这一关**现在怎么样了**：进行中还是已经结束、打了多少分、
    ///                手牌剩几张、开过几次。
    /// 前者是"关卡的定义"，后者是"一次通关过程"。混在一起的话，
    /// 想重开一关就得把定义也重建一遍，配置里的东西会被运行时状态污染。
    ///
    /// 【为什么要把 turn 收进来】
    /// TurnState 是"这一关之内的过程数据"，它天然属于某一关。
    /// 原来它挂在 TableTurnLoop 上飘着，等于"这条命是谁的"没有主。
    /// 现在 Level 拿着它，退出关卡时连状态一起丢掉就行。
    ///
    /// 【它不管什么】
    /// 不管界面、不管阶段流转、不管怎么画。那些是 TableTurnLoop 的事。
    /// 它只回答"这一关开始了没、结束了没、过没过"。
    /// </summary>
    [Serializable]
    public class Level
    {
        /// <summary>关卡定义（配置）</summary>
        public LevelData data;

        /// <summary>这一关之内的过程数据（手牌 / 刀片 / 杯内 / 得分）</summary>
        public TurnState turn;

        /// <summary>现在处于什么状态</summary>
        public LevelState state = LevelState.Ready;

        /// <summary>这一关被开过几次（重开也算一次）</summary>
        public int attempts;

        public Level()
        {
            data = new LevelData();
            turn = new TurnState();
        }

        public Level(LevelData d)
        {
            data = d != null ? d : new LevelData();
            turn = new TurnState();
        }

        // ── 定义（转发给 data，省得调用方到处 .data）────────────────

        public string Id          { get { return data != null ? data.id : ""; } }
        public string Name        { get { return data != null ? data.name : ""; } }
        public int    TargetScore { get { return data != null ? data.targetScore : 0; } }

        public int ChoiceCount
        {
            get { return data != null ? data.ChoiceCount : 0; }
        }

        public Choice ChoiceAt(int index)
        {
            return data != null ? data.ChoiceAt(index) : null;
        }

        public Choice FindChoice(string choiceId)
        {
            return data != null ? data.FindChoice(choiceId) : null;
        }

        // ── 状态查询 ─────────────────────────────────────────────────

        /// <summary>这一关已经结束了（不管是通关、失败还是中途退出）。</summary>
        public bool IsOver
        {
            get
            {
                return state == LevelState.Cleared
                    || state == LevelState.Failed
                    || state == LevelState.Abandoned;
            }
        }

        /// <summary>这一关正在进行中。</summary>
        public bool IsPlaying { get { return state == LevelState.Playing; } }

        /// <summary>分数够不够。今天得分恒为 0，所以实际上一定不达标。</summary>
        public bool IsCleared
        {
            get { return turn != null && turn.score >= TargetScore; }
        }

        /// <summary>还剩多少张手牌（食材 + 模块）。</summary>
        public int CardsLeft { get { return turn != null ? turn.HandCount : 0; } }

        // ── 生命周期 ─────────────────────────────────────────────────

        /// <summary>
        /// 开始这一关：按牌组把手牌和刀片准备好。
        /// 会连 turn 一起重置 —— 重开一关不该带着上一把的杯子和分数。
        /// </summary>
        public void Begin(Deck deck, Ingredient fallbackBlade = null)
        {
            turn.PrepareLoadout(data, deck, fallbackBlade);
            attempts++;
            state = LevelState.Playing;
        }

        /// <summary>
        /// 收关：按分数判定过关还是失败。
        /// 手牌打完走到结算界面时调用。
        /// </summary>
        public void Finish()
        {
            if (state != LevelState.Playing) return;
            state = IsCleared ? LevelState.Cleared : LevelState.Failed;
        }

        /// <summary>中途退出这一关。既不算赢也不算输，进度丢掉。</summary>
        public void Abandon()
        {
            if (state == LevelState.Ready) return;
            state = LevelState.Abandoned;
        }

        /// <summary>一行的状态描述，给界面用。</summary>
        public string StateText()
        {
            switch (state)
            {
                case LevelState.Ready:     return "未开始";
                case LevelState.Playing:   return "进行中";
                case LevelState.Cleared:   return "已通关";
                case LevelState.Failed:    return "未达标";
                case LevelState.Abandoned: return "已退出";
                default:                   return "?";
            }
        }

        public override string ToString()
        {
            return Name + "（" + StateText() + "　目标 " + TargetScore + "）";
        }
    }
}
