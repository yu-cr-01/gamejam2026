using System;
using System.Collections.Generic;

namespace GameJam.Data
{
    /// <summary>
    /// 牌组：玩家在"三选一"里挑的那个东西。
    ///
    /// 【职责】只描述"这副牌里有什么"，不关心怎么被选、被谁选。
    /// 选择逻辑在 Choices 那一层，牌组本身不需要知道。
    /// </summary>
    [Serializable]
    public class Deck
    {
        /// <summary>唯一标识，例如 "deck_a"</summary>
        public string id;

        /// <summary>牌组名，例如 "牌组 A"</summary>
        public string name;

        /// <summary>初始手牌用的食材</summary>
        public List<Ingredient> ingredients = new List<Ingredient>();

        /// <summary>附带的变速模块。结构上是列表，方便以后一副牌带多个模块。</summary>
        public List<SpeedModule> modules = new List<SpeedModule>();

        public Deck() { id = ""; name = ""; }

        public Deck(string id, string name)
        {
            this.id = id;
            this.name = name;
        }

        public Deck WithIngredients(params Ingredient[] items)
        {
            if (items != null)
            {
                for (int i = 0; i < items.Length; i++)
                    if (items[i] != null) ingredients.Add(items[i]);
            }
            return this;
        }

        /// <summary>接受 List 的重载 —— 方便直接接 IngredientCatalog.CreateMany() 的返回值。</summary>
        public Deck WithIngredients(List<Ingredient> items)
        {
            if (items != null)
            {
                for (int i = 0; i < items.Count; i++)
                    if (items[i] != null) ingredients.Add(items[i]);
            }
            return this;
        }

        public Deck WithModule(SpeedModule m)
        {
            if (m != null) modules.Add(m);
            return this;
        }

        /// <summary>
        /// 把牌组里所有模块的效果合并成一个组合。
        /// 这就是"效果类组合"的第一种用法：模块 → 合并
        /// </summary>
        public EffectGroup CombinedModuleEffects()
        {
            EffectGroup g = new EffectGroup(name + " 模块效果");
            if (modules != null)
            {
                for (int i = 0; i < modules.Count; i++)
                    if (modules[i] != null) g.Append(modules[i].effects);
            }
            return g;
        }

        public List<string> IngredientNames()
        {
            List<string> names = new List<string>();
            if (ingredients != null)
                for (int i = 0; i < ingredients.Count; i++)
                    if (ingredients[i] != null) names.Add(ingredients[i].name);
            return names;
        }

        public string DescribeIngredients()
        {
            List<string> n = IngredientNames();
            return n.Count > 0 ? string.Join("、", n.ToArray()) : "（空）";
        }

        public Deck Clone()
        {
            Deck d = new Deck(id, name);
            d.ingredients = new List<Ingredient>();
            d.modules = new List<SpeedModule>();

            if (ingredients != null)
                for (int i = 0; i < ingredients.Count; i++)
                    if (ingredients[i] != null) d.ingredients.Add(ingredients[i].Clone());

            if (modules != null)
                for (int i = 0; i < modules.Count; i++)
                    if (modules[i] != null) d.modules.Add(modules[i].Clone());

            return d;
        }

        public override string ToString() { return name; }
    }
}
