using System.Collections.Generic;
using UnityEngine;
using GameJam.Data;

namespace GameJam.Prototype
{
    /// <summary>
    /// 卡牌需求设计 v2.1 的**翻译层** —— 把 `Resources/Config/cards_v21.json`
    /// 读成运行时的 <see cref="Ingredient"/>（素材）和 <see cref="Spell"/>（法术）。
    ///
    /// 【它和 GameConfig 是什么关系】
    ///   GameConfig 管"这一局怎么玩"：牌组、关卡、数值（game_config.json）。
    ///   CardSpecs 管"卡牌是什么"：形态、标签、形态转换、D耗尽、启动、献祭（cards_v21.json）。
    ///   两份配置、两条加载路径，互不覆盖 —— 策划改卡的描述不会碰到关卡数值。
    ///
    /// 【为什么不直接注册进 IngredientCatalog】
    ///   IngredientCatalog 里的模板带着 H/D/V 数值，而那套数值来自旧配置；
    ///   v2.1 表里**没有数值**（只有规则文本）。直接覆盖注册会把老数值冲掉，
    ///   正在跑的牌组会全部变成 0 属性。
    ///   所以这里做**合并**：同 id 的老模板保留数值、补上 v2.1 的描述字段；
    ///   只有 v2.1 才有的卡（冰、水蒸气、铜…）以 0 属性注册，等策划补数值。
    ///
    /// 【取用一律走 Clone】
    ///   返回的是拷贝，调用方随便改；模板本身别动。
    /// </summary>
    public static class CardSpecs
    {
        public const string ResourcePath = "Config/cards_v21";

        private static List<Ingredient> materials;
        private static List<Spell> spells;
        private static string source = "（未加载）";
        private static bool registered;

        /// <summary>实际生效的来源，界面上显示用（排查"配置没生效"时很有用）。</summary>
        public static string Source { get { EnsureLoaded(); return source; } }

        /// <summary>强制下次重新读。</summary>
        public static void Reload()
        {
            materials = null;
            spells = null;
            source = "（未加载）";
            registered = false;
        }

        // ── 取表 ──────────────────────────────────────────────────────

        /// <summary>全部素材卡（v2.1 表里的顺序）。返回的是模板，不要改。</summary>
        public static List<Ingredient> Materials()
        {
            EnsureLoaded();
            return materials;
        }

        /// <summary>全部法术卡。</summary>
        public static List<Spell> Spells()
        {
            EnsureLoaded();
            return spells;
        }

        /// <summary>按 id 取素材模板（v2.1 表优先，其次旧的食材图鉴）。</summary>
        public static Ingredient Material(string id)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(id)) return null;

            for (int i = 0; i < materials.Count; i++)
                if (materials[i] != null && materials[i].id == id) return materials[i];

            return IngredientCatalog.Get(id);
        }

        /// <summary>按 id 取法术模板。</summary>
        public static Spell Spell(string id)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(id)) return null;

            for (int i = 0; i < spells.Count; i++)
                if (spells[i] != null && spells[i].id == id) return spells[i];

            return null;
        }

        /// <summary>按名字找卡（形态转换 / D耗尽 表里写的是中文名）。</summary>
        public static Ingredient MaterialByName(string name)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(name)) return null;

            for (int i = 0; i < materials.Count; i++)
                if (materials[i] != null && materials[i].name == name) return materials[i];

            // 旧图鉴里也找一遍（老的 13 种食材）
            foreach (Ingredient ing in IngredientCatalog.All())
                if (ing != null && ing.name == name) return ing;

            return null;
        }

        /// <summary>按名字找法术。</summary>
        public static Spell SpellByName(string name)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(name)) return null;

            for (int i = 0; i < spells.Count; i++)
                if (spells[i] != null && spells[i].name == name) return spells[i];

            return null;
        }

        // ── 注册（合并）──────────────────────────────────────────────

        /// <summary>
        /// 把 v2.1 的卡并进旧图鉴：同 id 保留老数值、补描述字段；新卡以 0 属性注册。
        /// 幂等，而且会在图鉴被清空后（GameConfig.Reload）自动补回。
        /// </summary>
        public static void EnsureRegistered()
        {
            EnsureLoaded();
            if (registered && materials.Count > 0 && IngredientCatalog.Has(materials[0].id)) return;

            for (int i = 0; i < materials.Count; i++)
            {
                Ingredient spec = materials[i];
                if (spec == null) continue;

                Ingredient existing = IngredientCatalog.Get(spec.id);
                if (existing == null)
                {
                    // v2.1 独有的卡：没有数值，先按 0 属性注册（等策划补 H/D/V）
                    Ingredient blank = new Ingredient(spec.id, spec.name, new AttrSet());
                    CopySpec(spec, blank);
                    IngredientCatalog.Register(blank);
                }
                else
                {
                    CopySpec(spec, existing);
                }
            }

            registered = true;
        }

        private static void CopySpec(Ingredient from, Ingredient to)
        {
            to.series    = from.series;
            to.form      = from.form;
            to.tags      = from.tags;
            to.transitions = from.transitions;
            to.exhaust   = from.exhaust;
            to.startup   = from.startup;
            to.sacrifice = from.sacrifice;
            to.note      = from.note;
        }

        // ── 加载 ──────────────────────────────────────────────────────

        private static void EnsureLoaded()
        {
            if (materials != null) return;

            materials = new List<Ingredient>();
            spells = new List<Spell>();

            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                source = "没有找到 " + ResourcePath + ".json（卡牌描述为空）";
                Debug.LogWarning("[CardSpecs] " + source);
                return;
            }

            CardSpecFileDto dto = null;
            try
            {
                dto = JsonUtility.FromJson<CardSpecFileDto>(asset.text);
            }
            catch (System.Exception e)
            {
                source = "cards_v21.json 解析失败";
                Debug.LogError("[CardSpecs] " + source + "：" + e.Message);
                return;
            }

            if (dto != null && dto.materials != null)
            {
                for (int i = 0; i < dto.materials.Length; i++)
                {
                    Ingredient ing = Build(dto.materials[i]);
                    if (ing != null) materials.Add(ing);
                }
            }

            if (dto != null && dto.spells != null)
            {
                for (int i = 0; i < dto.spells.Length; i++)
                {
                    Spell sp = Build(dto.spells[i]);
                    if (sp != null) spells.Add(sp);
                }
            }

            source = ResourcePath + ".json（素材 " + materials.Count + " · 法术 " + spells.Count + "）";
        }

        private static Ingredient Build(MaterialSpecDto d)
        {
            if (d == null || string.IsNullOrEmpty(d.id)) return null;

            // ★ 数值从旧图鉴里带过来（同 id 的话），v2.1 表本身没有数值
            AttrSet attrs = null;
            Ingredient old = IngredientCatalog.Get(d.id);
            if (old != null && old.attrs != null) attrs = old.attrs.Clone();

            Ingredient ing = new Ingredient(d.id, d.name, attrs);
            ing.series = d.series != null ? d.series : "";
            ing.form = d.form != null ? d.form : "";
            ing.tags = d.tags != null ? d.tags : new string[0];
            ing.exhaust = d.exhaust != null ? d.exhaust : new string[0];
            ing.startup = d.startup != null ? d.startup : "";
            ing.sacrifice = d.sacrifice != null ? d.sacrifice : "";
            ing.note = d.note != null ? d.note : "";

            if (d.transitions != null)
            {
                ing.transitions = new FormChange[d.transitions.Length];
                for (int i = 0; i < d.transitions.Length; i++)
                {
                    FormChangeDto t = d.transitions[i];
                    ing.transitions[i] = t == null
                        ? new FormChange()
                        : new FormChange(t.trigger, t.result);
                }
            }
            return ing;
        }

        private static Spell Build(SpellSpecDto d)
        {
            if (d == null || string.IsNullOrEmpty(d.id)) return null;

            Spell sp = new Spell(d.id, d.name);
            sp.series = d.series != null ? d.series : "";
            sp.enchant = d.enchant != null ? d.enchant : "";
            sp.category = d.category != null ? d.category : "";
            sp.requirement = d.requirement != null ? d.requirement : "";
            sp.sources = d.sources != null ? d.sources : new string[0];
            return sp;
        }
    }
}
