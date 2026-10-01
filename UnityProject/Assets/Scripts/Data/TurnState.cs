using System;
using System.Collections.Generic;

namespace GameJam.Data
{
    /// <summary>
    /// 回合状态。一关之内的运行时数据。
    ///
    /// 今天只定义结构 + 假数据，结算逻辑一律返回 0。
    /// </summary>
    [Serializable]
    public class TurnState
    {
        /// <summary>当前回合数，从 1 开始</summary>
        public int turnNumber;

        /// <summary>手牌列表（打出一张就移除一张）</summary>
        public List<Ingredient> hand = new List<Ingredient>();

        /// <summary>当前刀片（也是一个食材），默认铁块</summary>
        public Ingredient currentBlade;

        /// <summary>杯内食材列表（本回合投放进去的）</summary>
        public List<Ingredient> cupIngredients = new List<Ingredient>();

        /// <summary>当前得分</summary>
        public int currentScore;

        /// <summary>本回合投放的变速模块（来自所选牌组）</summary>
        public SpeedModule speedModule;

        /// <summary>本关目标分，用于结算界面显示</summary>
        public int targetScore;

        /// <summary>手牌是否已空 —— 关卡结束条件之一</summary>
        public bool IsHandEmpty { get { return hand == null || hand.Count == 0; } }

        /// <summary>
        /// 开始新的一关。手牌从关卡的初始手牌深拷贝而来。
        /// </summary>
        /// <param name="defaultBlade">
        /// 默认刀片。炼金玩法里刀片也是食材，原型阶段固定传铁块。
        /// 传 null 时退回"取手牌第一张"。
        /// </param>
        public void StartLevel(LevelData level, Deck deck, Ingredient defaultBlade = null)
        {
            turnNumber = 1;
            currentScore = 0;
            targetScore = level != null ? level.targetScore : 0;
            cupIngredients = new List<Ingredient>();
            hand = new List<Ingredient>();

            if (level != null && level.startingHand != null)
            {
                foreach (Ingredient ing in level.startingHand)
                {
                    if (ing != null) hand.Add(ing.Clone());
                }
            }

            speedModule = deck != null && deck.speedModule != null
                        ? deck.speedModule.Clone()
                        : null;

            // 默认刀片：原型阶段固定用铁块，传 null 时退回手牌第一张
            if (defaultBlade != null)      currentBlade = defaultBlade.Clone();
            else if (hand.Count > 0)       currentBlade = hand[0].Clone();
            else                           currentBlade = null;
        }

        /// <summary>进入下一回合。</summary>
        public void NextTurn()
        {
            turnNumber++;
            cupIngredients = new List<Ingredient>();
        }

        /// <summary>手牌的名字串起来，用于界面显示，例如 "铁块、冰块、柠檬"</summary>
        public string HandNames()
        {
            if (hand == null || hand.Count == 0) return "（空）";
            List<string> names = new List<string>();
            foreach (Ingredient i in hand) { if (i != null) names.Add(i.name); }
            return string.Join("、", names.ToArray());
        }

        public string CupNames()
        {
            if (cupIngredients == null || cupIngredients.Count == 0) return "（空）";
            List<string> names = new List<string>();
            foreach (Ingredient i in cupIngredients) { if (i != null) names.Add(i.name); }
            return string.Join("、", names.ToArray());
        }

        public string BladeName()
        {
            return currentBlade != null ? currentBlade.name : "（未选择）";
        }
    }
}
