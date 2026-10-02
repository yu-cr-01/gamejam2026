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

        /// <summary>
        /// 开局自动装到刀片槽上的那张牌的 id。
        ///
        /// 【为什么要做成配置而不是写死"第一张"】
        /// 策划文档里牌组是「外星合金、水、硝石、硫磺」，开局外星合金当刀片。
        /// 但把"第一张当刀片"写进代码，将来想让水开局当刀片就得改代码。
        /// 留成配置项：填了就按填的来；没填才退回第一张；牌组为空才用兜底铁块。
        /// </summary>
        public string initialBladeId;

        /// <summary>附带的变速模块。结构上是列表，方便以后一副牌带多个模块。</summary>
        public List<SpeedModule> modules = new List<SpeedModule>();

        public Deck() { id = ""; name = ""; }

        public Deck(string id, string name)
        {
            this.id = id;
            this.name = name;
        }

        /// <summary>指定哪张牌开局当刀片（传食材 id）。</summary>
        public Deck WithInitialBlade(string ingredientId)
        {
            initialBladeId = ingredientId;
            return this;
        }

        /// <summary>
        /// 算出开局当刀片的那张牌在 ingredients 里的下标。
        ///
        /// 查找顺序：
        ///   1. initialBladeId 指定的那张
        ///   2. 找不到就退回第一张
        ///   3. 牌组为空返回 -1（由调用方决定用兜底刀片）
        /// </summary>
        public int InitialBladeIndex()
        {
            if (ingredients == null || ingredients.Count == 0) return -1;

            if (!string.IsNullOrEmpty(initialBladeId))
            {
                for (int i = 0; i < ingredients.Count; i++)
                {
                    if (ingredients[i] != null && ingredients[i].id == initialBladeId) return i;
                }
            }
            return 0;
        }

        /// <summary>开局当刀片的那张牌（没找到返回 null）。</summary>
        public Ingredient InitialBlade()
        {
            int i = InitialBladeIndex();
            return i >= 0 ? ingredients[i] : null;
        }

        /// <summary>开局进手牌的那些牌（即除了初始刀片以外的全部）。</summary>
        public List<Ingredient> InitialHandIngredients()
        {
            List<Ingredient> result = new List<Ingredient>();
            if (ingredients == null) return result;

            int skip = InitialBladeIndex();
            for (int i = 0; i < ingredients.Count; i++)
            {
                if (i == skip) continue;
                if (ingredients[i] != null) result.Add(ingredients[i].Clone());
            }
            return result;
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
            d.initialBladeId = initialBladeId;
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
