using System.Collections.Generic;
using GameJam.Data;

namespace GameJam.Prototype
{
    /// <summary>
    /// 全部假数据。数值是瞎填的，等策划慢慢设计。
    ///
    /// 【职责】只负责"产出数据"，不持有任何游戏状态。
    /// 每次取用都返回新对象，玩过一局不会污染这里的原始配置。
    ///
    /// 换成正式配置表（Excel / JSON / ScriptableObject）时，只要保持
    /// BuildDecks() / BuildLevel() / DefaultBlade() 三个入口不变，其它代码不用动。
    /// </summary>
    public static class FakeData
    {
        // ── 食材 id ───────────────────────────────────────────────────
        public const string IronBlockId  = "iron_block";
        public const string IceCubeId    = "ice_cube";
        public const string LemonId      = "lemon";
        public const string ChocolateId  = "chocolate";
        public const string ChiliId      = "chili";
        public const string HardCandyId  = "hard_candy";
        public const string MilkId       = "milk";
        public const string CoffeeBeanId = "coffee_bean";
        public const string ButterId     = "butter";

        // ── 模块 id ───────────────────────────────────────────────────
        public const string HighSpeedId  = "high_speed";

        // ── 关卡 / 选择 id ────────────────────────────────────────────
        public const string Level1Id     = "level_1";
        public const string DeckPickId   = "deck_pick";
        public const string BladePickId  = "blade_pick";

        // ══════════════════════════════════════════════════════════════
        //  食材图鉴
        // ══════════════════════════════════════════════════════════════

        /// <summary>注册全部食材模板。幂等，可以反复调。</summary>
        public static void EnsureCatalog()
        {
            IngredientCatalog.Clear();

            // 属性不再是一个个字段，而是一个属性集合
            R(IronBlockId,  "铁块",   new AttrSet().With(AttrId.Hardness, 10));
            R(IceCubeId,    "冰块",   new AttrSet().With(AttrId.Hardness, 8)
                                                  .With(AttrId.Temperature, -15)
                                                  .With(AttrId.Water, 10));
            R(LemonId,      "柠檬",   new AttrSet().With(AttrId.Hardness, 3)
                                                  .With(AttrId.Temperature, 20)
                                                  .With(AttrId.Acidity, 9)
                                                  .With(AttrId.Water, 8));

            R(ChocolateId,  "巧克力", new AttrSet().With(AttrId.Hardness, 4)
                                                  .With(AttrId.Temperature, 25)
                                                  .With(AttrId.Acidity, 1)
                                                  .With(AttrId.Sugar, 7)
                                                  .With(AttrId.Oil, 6)
                                                  .With(AttrId.Water, 2));
            R(ChiliId,      "辣椒",   new AttrSet().With(AttrId.Hardness, 3)
                                                  .With(AttrId.Temperature, 22)
                                                  .With(AttrId.Acidity, 3)
                                                  .With(AttrId.Sugar, 2)
                                                  .With(AttrId.Oil, 1)
                                                  .With(AttrId.Water, 7));
            R(HardCandyId,  "硬糖",   new AttrSet().With(AttrId.Hardness, 9)
                                                  .With(AttrId.Temperature, 20)
                                                  .With(AttrId.Acidity, 1)
                                                  .With(AttrId.Sugar, 10)
                                                  .With(AttrId.Water, 1));

            R(MilkId,       "牛奶",   new AttrSet().With(AttrId.Hardness, 1)
                                                  .With(AttrId.Temperature, 5)
                                                  .With(AttrId.Acidity, 2)
                                                  .With(AttrId.Sugar, 4)
                                                  .With(AttrId.Oil, 4)
                                                  .With(AttrId.Water, 10));
            R(CoffeeBeanId, "咖啡豆", new AttrSet().With(AttrId.Hardness, 8)
                                                  .With(AttrId.Temperature, 20)
                                                  .With(AttrId.Acidity, 5)
                                                  .With(AttrId.Sugar, 1)
                                                  .With(AttrId.Oil, 3)
                                                  .With(AttrId.Water, 2));
            R(ButterId,     "黄油",   new AttrSet().With(AttrId.Hardness, 2)
                                                  .With(AttrId.Temperature, 10)
                                                  .With(AttrId.Acidity, 1)
                                                  .With(AttrId.Sugar, 1)
                                                  .With(AttrId.Oil, 10)
                                                  .With(AttrId.Water, 3));
        }

        private static void R(string id, string name, AttrSet attrs)
        {
            IngredientCatalog.Register(new Ingredient(id, name, attrs));
        }

        // ══════════════════════════════════════════════════════════════
        //  变速模块
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 高速模块：刀片硬度 +2
        ///
        /// 注意效果是 EffectGroup，不是"字符串属性名 + 一个数"。
        /// 所以这个模块以后想同时改多个属性、或者和别的模块组合，都不用改类。
        /// </summary>
        public static SpeedModule HighSpeed()
        {
            EffectGroup g = new EffectGroup("高速模块效果")
                .Add(AttrId.Hardness, EffectOp.Add, 2);
            return new SpeedModule(HighSpeedId, "高速模块", g);
        }

        /// <summary>示例：效果组合的用法 —— 可以造出"模块 + 模块"的复合效果。</summary>
        public static SpeedModule CombinedExample()
        {
            EffectGroup g = EffectGroup.Combine("复合模块效果",
                new EffectGroup("A").Add(AttrId.Hardness, EffectOp.Add, 2),
                new EffectGroup("B").Add(AttrId.Sugar, EffectOp.MulPercent, 50));
            return new SpeedModule("combined_demo", "复合模块（示例）", g);
        }

        // ══════════════════════════════════════════════════════════════
        //  牌组
        // ══════════════════════════════════════════════════════════════

        public static List<Deck> BuildDecks()
        {
            EnsureCatalog();
            List<Deck> decks = new List<Deck>();

            decks.Add(new Deck("deck_a", "牌组 A")
                .WithIngredients(IngredientCatalog.CreateMany(IronBlockId, IceCubeId, LemonId))
                .WithModule(HighSpeed()));

            decks.Add(new Deck("deck_b", "牌组 B")
                .WithIngredients(IngredientCatalog.CreateMany(ChocolateId, ChiliId, HardCandyId))
                .WithModule(HighSpeed()));

            decks.Add(new Deck("deck_c", "牌组 C")
                .WithIngredients(IngredientCatalog.CreateMany(MilkId, CoffeeBeanId, ButterId))
                .WithModule(HighSpeed()));

            return decks;
        }

        // ══════════════════════════════════════════════════════════════
        //  关卡 —— 自己声明有哪些选择环节
        // ══════════════════════════════════════════════════════════════

        public static LevelData BuildLevel(List<Deck> decks)
        {
            EnsureCatalog();

            LevelData level = new LevelData(Level1Id, "第 1 关", 1000);

            // ── 选择 ①：三选一牌组 ──
            Choice deckChoice = new Choice(DeckPickId, "三选一牌组", 1)
                .WithHint("选一副牌组，决定初始手牌和变速模块");

            for (int i = 0; i < decks.Count; i++)
            {
                Deck d = decks[i];
                string moduleText = d.modules.Count > 0
                    ? d.modules[0].name + "（" + d.modules[0].Description() + "）"
                    : "无";

                deckChoice.AddOptions(
                    new ChoiceOption(d.id, d.name, ChoiceOptionKind.Deck, d.id)
                        .WithSubtitle("食材：" + d.DescribeIngredients() + "　｜　变速模块：" + moduleText)
                        .WithDeck(d));
            }
            level.WithChoice(deckChoice);

            // ── 选择 ②：选刀片（默认铁块）──
            Choice bladeChoice = new Choice(BladePickId, "选刀片，默认铁块", 1)
                .WithHint("刀片本质也是食材，默认铁块")
                .WithDefault(IronBlockId);

            foreach (string bid in new[] { IronBlockId, IceCubeId, HardCandyId })
            {
                Ingredient ing = IngredientCatalog.Get(bid);
                if (ing == null) continue;
                bladeChoice.AddOptions(
                    new ChoiceOption(bid, ing.name, ChoiceOptionKind.Blade, bid)
                        .WithSubtitle("硬度 " + ing.attrs.Get(AttrId.Hardness))
                        .WithIngredient(ing));
            }
            level.WithChoice(bladeChoice);

            return level;
        }

        /// <summary>默认刀片 —— 任务要求固定铁块。</summary>
        public static Ingredient DefaultBlade()
        {
            EnsureCatalog();
            return IngredientCatalog.Create(IronBlockId);
        }
    }
}
