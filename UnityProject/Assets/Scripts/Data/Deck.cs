using System;

namespace GameJam.Data
{
    /// <summary>
    /// 牌组。玩家在三选一界面里挑一个，决定这一关的初始手牌和变速模块。
    ///
    /// 结构固定：3 个食材 + 1 个变速模块。
    /// </summary>
    [Serializable]
    public class Deck
    {
        /// <summary>牌组名，例如 "牌组 A"</summary>
        public string deckName;

        /// <summary>3 个食材</summary>
        public Ingredient[] ingredients;

        /// <summary>1 个变速模块</summary>
        public SpeedModule speedModule;

        public Deck() { }

        public Deck(string deckName, Ingredient[] ingredients, SpeedModule speedModule)
        {
            this.deckName = deckName;
            this.ingredients = ingredients;
            this.speedModule = speedModule;
        }

        /// <summary>深拷贝。选牌组时用它，避免玩家改动污染原始假数据。</summary>
        public Deck Clone()
        {
            Ingredient[] copy = null;
            if (ingredients != null)
            {
                copy = new Ingredient[ingredients.Length];
                for (int i = 0; i < ingredients.Length; i++)
                {
                    copy[i] = ingredients[i] != null ? ingredients[i].Clone() : null;
                }
            }
            return new Deck(deckName, copy, speedModule != null ? speedModule.Clone() : null);
        }

        public override string ToString() { return deckName; }
    }
}
