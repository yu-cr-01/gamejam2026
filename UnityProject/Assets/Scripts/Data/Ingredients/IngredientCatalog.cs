using System.Collections.Generic;

namespace GameJam.Data
{
    /// <summary>
    /// 食材图鉴：id → 食材模板。
    ///
    /// 【职责单一】这里只管"注册和查表"，不管数据具体是什么。
    /// 具体有哪些食材由 FakeData（或以后的正式配置表）注册进来。
    /// 换成读 Excel / JSON / ScriptableObject 时，只要改成往这里注册即可，
    /// 所有依赖图鉴的代码一行都不用动。
    ///
    /// 【重要】取用一律走 Create()，它返回深拷贝。
    /// 手牌会被消耗、属性会被效果修改，直接拿模板会污染全局数据。
    /// </summary>
    public static class IngredientCatalog
    {
        private static readonly Dictionary<string, Ingredient> Templates =
            new Dictionary<string, Ingredient>();

        /// <summary>已注册的食材数量</summary>
        public static int Count { get { return Templates.Count; } }

        /// <summary>注册一个食材模板。同 id 重复注册会覆盖。</summary>
        public static void Register(Ingredient template)
        {
            if (template == null || string.IsNullOrEmpty(template.id)) return;
            Templates[template.id] = template;
        }

        /// <summary>清空图鉴（重新加载配置时用）。</summary>
        public static void Clear()
        {
            Templates.Clear();
        }

        public static bool Has(string id)
        {
            return !string.IsNullOrEmpty(id) && Templates.ContainsKey(id);
        }

        /// <summary>取模板本身（只读用途，别改它）。</summary>
        public static Ingredient Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            Ingredient t;
            return Templates.TryGetValue(id, out t) ? t : null;
        }

        /// <summary>按 id 取一份深拷贝，可以随便改。</summary>
        public static Ingredient Create(string id)
        {
            Ingredient t = Get(id);
            return t != null ? t.Clone() : null;
        }

        /// <summary>批量取拷贝，例如 CreateMany("iron_block", "ice_cube", "lemon")</summary>
        public static List<Ingredient> CreateMany(params string[] ids)
        {
            List<Ingredient> list = new List<Ingredient>();
            if (ids == null) return list;
            for (int i = 0; i < ids.Length; i++)
            {
                Ingredient ing = Create(ids[i]);
                if (ing != null) list.Add(ing);
            }
            return list;
        }

        /// <summary>遍历所有模板。</summary>
        public static IEnumerable<Ingredient> All()
        {
            foreach (KeyValuePair<string, Ingredient> kv in Templates) yield return kv.Value;
        }
    }
}
