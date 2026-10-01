using System;

namespace GameJam.Data
{
    /// <summary>
    /// 关卡数据。今天只有 1 关，目标分 1000。
    /// </summary>
    [Serializable]
    public class LevelData
    {
        /// <summary>关卡名</summary>
        public string levelName;

        /// <summary>目标分（整数），例如 1000</summary>
        public int targetScore;

        /// <summary>
        /// 初始手牌 —— 就是所选牌组的 3 个食材。
        /// 这里存的是"模板"，实际开局会 Clone 一份到 TurnState.hand。
        /// </summary>
        public Ingredient[] startingHand;

        public LevelData() { }

        public LevelData(string levelName, int targetScore, Ingredient[] startingHand)
        {
            this.levelName = levelName;
            this.targetScore = targetScore;
            this.startingHand = startingHand;
        }

        /// <summary>由牌组构建关卡：初始手牌 = 牌组的 3 个食材。</summary>
        public static LevelData FromDeck(Deck deck, string levelName, int targetScore)
        {
            return new LevelData(levelName, targetScore,
                                 deck != null ? deck.ingredients : new Ingredient[0]);
        }

        public override string ToString() { return levelName; }
    }
}
