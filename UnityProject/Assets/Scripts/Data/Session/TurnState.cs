using System;
using System.Collections.Generic;

namespace GameJam.Data
{
    /// <summary>
    /// 回合状态：一关之内的过程数据。
    ///
    /// 【和选择记录的分工】
    ///   TurnState    管"现在场上是什么"（手牌 / 刀片 / 杯内 / 得分）
    ///   SelectionLog 管"玩家做过什么选择"
    /// 两者互不依赖 —— 回合状态推进不用知道玩家当初怎么选的。
    ///
    /// 【效果组合挂在哪】
    /// activeEffects 是本局累积的效果组合（牌组模块 + 关卡规则 + 已投食材）。
    /// 它作用在任何一个 AttrSet 上，不关心那个集合属于谁。
    /// </summary>
    [Serializable]
    public class TurnState
    {
        /// <summary>当前回合数，从 1 开始</summary>
        public int turnNumber = 1;

        /// <summary>手牌</summary>
        public List<Ingredient> hand = new List<Ingredient>();

        /// <summary>当前刀片（也是一个 Ingredient）</summary>
        public Ingredient blade;

        /// <summary>杯内食材</summary>
        public List<Ingredient> cup = new List<Ingredient>();

        /// <summary>当前得分</summary>
        public int score;

        /// <summary>本关目标分</summary>
        public int targetScore;

        /// <summary>本局累积的效果组合（牌组模块 + 关卡规则 + 食材自带效果）</summary>
        public EffectGroup activeEffects = new EffectGroup("本局效果");

        public bool IsHandEmpty { get { return hand == null || hand.Count == 0; } }

        // ── 生命周期 ──────────────────────────────────────────────────

        /// <summary>开始一关。手牌从牌组深拷贝而来。</summary>
        public void StartLevel(LevelData level, Deck deck, Ingredient defaultBlade = null)
        {
            Reset();
            targetScore = level != null ? level.targetScore : 0;

            if (deck != null && deck.ingredients != null)
                for (int i = 0; i < deck.ingredients.Count; i++)
                    if (deck.ingredients[i] != null) hand.Add(deck.ingredients[i].Clone());

            // 本局效果 = 关卡规则 组合 牌组模块效果
            activeEffects = new EffectGroup("本局效果");
            if (level != null) activeEffects.Append(level.rules);
            if (deck != null) activeEffects.Append(deck.CombinedModuleEffects());

            // 刀片：优先用指定的默认刀片，否则取手牌第一张
            if (defaultBlade != null)        blade = defaultBlade.Clone();
            else if (hand.Count > 0)         blade = hand[0].Clone();
            else                             blade = null;
        }

        /// <summary>清空一切。</summary>
        public void Reset()
        {
            turnNumber = 1;
            score = 0;
            targetScore = 0;
            hand = new List<Ingredient>();
            cup = new List<Ingredient>();
            blade = null;
            activeEffects = new EffectGroup("本局效果");
        }

        /// <summary>进入下一回合。</summary>
        public void NextTurn()
        {
            turnNumber++;
            cup = new List<Ingredient>();
        }

        // ── 动作 ──────────────────────────────────────────────────────

        /// <summary>从手牌打出一张，放进杯子。返回打出的那张。</summary>
        public Ingredient PlayFromHand(int index)
        {
            if (hand == null || index < 0 || index >= hand.Count) return null;
            Ingredient ing = hand[index];
            hand.RemoveAt(index);
            if (ing != null) cup.Add(ing);
            return ing;
        }

        // ── 查询 ──────────────────────────────────────────────────────

        public List<string> HandNamesList()
        {
            List<string> names = new List<string>();
            if (hand != null)
                for (int i = 0; i < hand.Count; i++)
                    if (hand[i] != null) names.Add(hand[i].name);
            return names;
        }

        public string HandNames()
        {
            List<string> n = HandNamesList();
            return n.Count > 0 ? string.Join("、", n.ToArray()) : "（空）";
        }

        public string CupNames()
        {
            List<string> names = new List<string>();
            if (cup != null)
                for (int i = 0; i < cup.Count; i++)
                    if (cup[i] != null) names.Add(cup[i].name);
            return names.Count > 0 ? string.Join("、", names.ToArray()) : "（空）";
        }

        public string BladeName()
        {
            return blade != null ? blade.name : "（未选择）";
        }

        /// <summary>刀片叠加本局效果之后的实际属性。</summary>
        public AttrSet BladeResolvedAttrs()
        {
            if (blade == null) return new AttrSet();
            return blade.ResolveAttrs(activeEffects);
        }

        /// <summary>刀片实际硬度 —— 走的是效果组合，不是读死字段。</summary>
        public int BladeHardness()
        {
            return BladeResolvedAttrs().Get(AttrId.Hardness);
        }
    }
}
