using System.Collections.Generic;
using UnityEngine;
using GameJam.Data;

namespace GameJam.Prototype
{
    /// <summary>
    /// 卡牌数值定稿 v3.0 的**翻译层** —— 把 `Resources/Config/cards_v21.json`
    /// 读成运行时的 <see cref="Ingredient"/>（素材）和 <see cref="Spell"/>（法术）。
    ///
    /// 【文件名为什么还叫 cards_v21】
    ///   内容是 v3.0 的（策划 2026-10-08 定稿），但**路径不动**：
    ///   离线探针（Tools/RuleProbe）、存档、美术自检工具都按这个路径找卡表，
    ///   改文件名要同时改四处、还会让旧存档的"卡表从哪来"这句话失真。
    ///   名字里那个 v21 现在是历史因素 —— 认内容，别认文件名。
    ///
    /// 【它和 GameConfig 是什么关系】
    ///   GameConfig 管"这一局怎么玩"：牌组、关卡、数值（game_config.json）。
    ///   CardSpecs 管"卡牌是什么"：类型、形态、标签、形态转换、D耗尽、启动、献祭（cards_v21.json）。
    ///   两份配置、两条加载路径，互不覆盖 —— 策划改卡的描述不会碰到关卡数值。
    ///
    /// 【v3.0 与 v2.1 的两处口径变化（这一段是本文件存在的理由之一）】
    ///   ① 素材卡 H 只有三档：5 普通 / 10 稀有 / 20 传说 → 见 <see cref="SelfCheck"/>；
    ///   ② **法术不再有 h/d/v**，只有稀有度 → 法术那一节**故意不写 h/d/v 字段**，
    ///      代码里任何"法术也有三个数"的假设都是错的（DTO 层已经把关，见 SpellSpecDto 的说明）。
    ///
    /// 【为什么不直接注册进 IngredientCatalog】
    ///   IngredientCatalog 里的模板带着 H/D/V 数值，而那套数值来自旧配置；
    ///   直接覆盖注册会把老数值冲掉，正在跑的牌组会全部变成 0 属性。
    ///   所以这里做**合并**：同 id 的老模板保留数值、补上卡表的描述字段；
    ///   只有卡表才有的卡以 0 属性注册，等 ApplyValues 兜底。
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

        /// <summary>
        /// 按 id 只查**卡表**（不退回旧图鉴）—— 自检与"这张卡到底是不是 v3.0 表里的"要用它。
        /// <see cref="Material"/> 会把旧食材图鉴也当来源，分不清"表里有"和"图鉴里有"，
        /// 校验 H 三档时不能含糊。
        /// </summary>
        public static Ingredient MaterialById(string id)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(id)) return null;

            for (int i = 0; i < materials.Count; i++)
                if (materials[i] != null && materials[i].id == id) return materials[i];

            return null;
        }

        /// <summary>
        /// 按 id 只查**法术表**（返回模板，不要改）。
        /// 和 <see cref="MaterialById"/> 成对：两者都不退回旧配置，
        /// 所以"卡表里到底有没有这张卡"能问出一个明确答案。
        /// </summary>
        public static Spell SpellById(string id)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(id)) return null;

            for (int i = 0; i < spells.Count; i++)
                if (spells[i] != null && spells[i].id == id) return spells[i];

            return null;
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
            to.cardType  = from.cardType;
            to.rarity    = from.rarity;
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

            // ★ 数值从旧图鉴里带过来（同 id 的话），卡表没给数值时才用
            AttrSet attrs = null;
            Ingredient old = IngredientCatalog.Get(d.id);
            if (old != null && old.attrs != null) attrs = old.attrs.Clone();

            Ingredient ing = new Ingredient(d.id, d.name, attrs);
            ing.series = d.series != null ? d.series : "";
            ing.cardType = !string.IsNullOrEmpty(d.cardType) ? d.cardType : "素材";
            ing.form = d.form != null ? d.form : "";
            ing.tags = d.tags != null ? d.tags : new string[0];
            ing.exhaust = d.exhaust != null ? d.exhaust : new string[0];
            ing.startup = d.startup != null ? d.startup : "";
            ing.sacrifice = d.sacrifice != null ? d.sacrifice : "";
            ing.note = d.note != null ? d.note : "";

            // v3.0 数值：卡表里给了就用（哪怕全是 0，也是"策划明确写了 0"），
            // 没给这一节才退回旧图鉴带过来的 attrs 换算（H=盐性、V=硫性）
            ing.h = d.h;
            ing.d = d.d;
            ing.v = d.v;
            ing.vGrade = d.vGrade != null ? d.vGrade : "";

            // 稀有度：卡表写了就用；**没写就按 H 反推**（v3.0 口径 5普通/10稀有/20传说）。
            // 这样策划只补 H 也不会让卡面缺一栏，同时"忘了写稀有度"不会变成静默的空白。
            ing.rarity = !string.IsNullOrEmpty(d.rarity) ? d.rarity : RarityOfH(ing.h);

            if (ing.h == 0 && ing.v == 0 && old != null && old.attrs != null)
            {
                ing.h = old.attrs.Get(AttrId.Salt);
                ing.v = old.attrs.Get(AttrId.Sulfur);
                ing.vGrade = "（旧值）";
                if (string.IsNullOrEmpty(d.rarity)) ing.rarity = RarityOfH(ing.h);
            }

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
            sp.cardType = !string.IsNullOrEmpty(d.cardType) ? d.cardType : "法术";
            sp.rarity = d.rarity != null ? d.rarity : "";
            sp.enchant = d.enchant != null ? d.enchant : "";
            sp.category = d.category != null ? d.category : "";
            sp.requirement = d.requirement != null ? d.requirement : "";
            sp.sources = d.sources != null ? d.sources : new string[0];
            return sp;
        }

        // ── v3.0 口径的自检（H 只有三档 / 稀有度对得上）────────────────

        /// <summary>v3.0 只允许出现的三个 H 值：5=普通、10=稀有、20=传说。</summary>
        public static readonly int[] AllowedH = new int[] { 5, 10, 20 };

        /// <summary>H 三档 → 稀有度文字。卡表漏写 rarity 时按这个补。</summary>
        public static string RarityOfH(int h)
        {
            if (h == 5) return "普通";
            if (h == 10) return "稀有";
            if (h == 20) return "传说";
            return "";
        }

        /// <summary>H 是不是三个合法档位之一（0 = 卡表没给数值，不算违反，由 ApplyValues 兜底）。</summary>
        public static bool IsAllowedH(int h)
        {
            for (int i = 0; i < AllowedH.Length; i++)
                if (AllowedH[i] == h) return true;
            return h == 0;
        }

        /// <summary>这个 H 违规定时，日志里给一句"哪个数不合法"。</summary>
        public static string AllowedHText()
        {
            return AllowedH[0] + " / " + AllowedH[1] + " / " + AllowedH[2];
        }

        /// <summary>
        /// v3.0 卡表自检：**素材卡 H 只允许 5 / 10 / 20**，且 rarity 必须和 H 那一档一致。
        ///
        /// 【为什么这条检查必须存在】
        ///   策划定稿把 H 从"随便给数"改成了**三档枚举**（5 普通 / 10 稀有 / 20 传说）。
        ///   枚举最容易出的错是"打错一个字"：H 写成 15 或 50，卡照样能跑、照样能出牌，
        ///   但稀有度、卡面强度、以及"传说卡该有多硬"的整套设计就悄悄崩了 ——
        ///   这种错不会报错、也不会崩，只会让数值对不上账。
        ///   所以加载卡表时当场验一遍：违反就**明确报警**（不是 Debug.Log 里混过去），
        ///   H 三档自检的结论同时进图鉴界面和启动日志（见 TableRulesV21 的开局自检）。
        ///
        /// 【返回值】不符合的条目，一行一张卡；全对就是空表。
        /// 面板/日志要显示哪几条不合法，直接读它。
        /// </summary>
        public static List<string> SelfCheck()
        {
            EnsureLoaded();

            List<string> problems = new List<string>();

            for (int i = 0; i < materials.Count; i++)
            {
                Ingredient m = materials[i];
                if (m == null) continue;

                string where = m.name + "（" + m.id + "）";

                if (m.cardType != "素材")
                    problems.Add(where + "：类型是「" + m.cardType + "」，素材表里应该是「素材」");

                if (!IsAllowedH(m.h))
                {
                    problems.Add(where + "：H=" + m.h + " 不在 v3.0 的三档里（只允许 " + AllowedHText() + "）");
                    continue;    // H 都不合法了，再比稀有度没有意义
                }

                if (m.h == 0) continue;      // 卡表没给数值（老表口径），交给 ApplyValues 兜底，不算违反

                string expect = RarityOfH(m.h);
                if (m.rarity != expect)
                {
                    problems.Add(where + "：H=" + m.h + " 对应稀有度「" + expect + "」，卡表写的是「" +
                                 (string.IsNullOrEmpty(m.rarity) ? "（空）" : m.rarity) + "」");
                }
            }

            for (int i = 0; i < spells.Count; i++)
            {
                Spell s = spells[i];
                if (s == null) continue;

                if (s.cardType != "法术")
                    problems.Add(s.name + "（" + s.id + "）：类型是「" + s.cardType + "」，法术表里应该是「法术」");

                // ★ 法术不再要求 H/D/V（v3.0），但**稀有度必须写**：
                //   它是法术唯一的数值口径，漏了卡面上就什么都不剩了。
                if (string.IsNullOrEmpty(s.rarity))
                    problems.Add(s.name + "（" + s.id + "）：v3.0 法术只标稀有度，但这一项是空的");
            }

            return problems;
        }
    }
}
