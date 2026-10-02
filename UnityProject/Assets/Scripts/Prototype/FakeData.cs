using GameJam.Data;

namespace GameJam.Prototype
{
    /// <summary>
    /// 内置默认配置 —— 和 Assets/Resources/Config/game_config.json 内容完全一致。
    ///
    /// 【它和 JSON 的关系】
    /// 正式配置表是那个 JSON，策划改 JSON 就行，不用碰代码。
    /// 这一份只是**文件丢失 / 解析失败时的兜底**，保证游戏永远能跑起来，
    /// 不会因为配置表被误删或写坏就黑屏。
    ///
    /// GameConfig 启动时会打印实际用的是哪一份，方便排查"我明明改了配置怎么没生效"。
    ///
    /// 【纪律】改 JSON 的时候记得同步改这里。
    /// 两边不一致不会报错（JSON 优先），但下次 JSON 出问题时兜底数据就是旧的。
    /// </summary>
    public static class FakeData
    {
        public const string IronBlockId = "iron_block";

        /// <summary>构造一份和 JSON 等价的内置配置。</summary>
        public static GameConfigDto BuiltIn()
        {
            GameConfigDto c = new GameConfigDto();

            c.ingredients = new[]
            {
                Ing(IronBlockId, "铁块", 10, 0, 0),

                Ing("alien_alloy",   "外星合金", 20, 0, 0),
                Ing("water",         "水",       1, 9, 1),
                Ing("saltpeter",     "硝石",     3, 4, 8),
                Ing("sulfur",        "硫磺",     2, 1, 9),

                Ing("mithril_ingot", "秘银锭",   16, 2, 0),
                Ing("brine",         "卤水",     1, 9, 2),
                Ing("alum",          "明矾",     4, 7, 1),
                Ing("vinegar",       "醋酸",     1, 8, 3),

                Ing("tiw_block",     "钛钨胚",   26, 0, 0),
                Ing("quartz_sand",   "石英砂",   8, 2, 1),
                Ing("clay",          "黏土",     6, 5, 1),
                Ing("graphite",      "石墨",     9, 1, 2),
            };

            c.decks = new[]
            {
                Deck("deck_sulfur_niter", "硫硝爆燃", "alien_alloy",
                     new[] { "alien_alloy", "water", "saltpeter", "sulfur" },
                     Mod("mod_overload_fuse", "过载引信", Eff("V", "Add", 3))),

                Deck("deck_mercury_etch", "盐汞蚀刻", "mithril_ingot",
                     new[] { "mithril_ingot", "brine", "alum", "vinegar" },
                     Mod("mod_catalyst_chamber", "催化腔", Eff("D", "Add", 3))),

                Deck("deck_inert_wall", "惰性护壁", "tiw_block",
                     new[] { "tiw_block", "quartz_sand", "clay", "graphite" },
                     Mod("mod_damping_base", "阻尼基座", Eff("H", "Add", 4))),
            };

            c.defaultBladeId = IronBlockId;

            c.level = new LevelDto { id = "level_1", name = "第 1 关", targetScore = 1000 };

            c.texts = new TextDto
            {
                deckTitle     = "选择你的初始牌组",
                deckConfirm   = "确认选择该卡组",
                deckCancel    = "取消",
                deckHint      = "点击卡片查看详情　｜　每副牌组 = 4 张食材牌 + 1 个变速模块",
                bladeTitle    = "选择你的刀片",
                bladeConfirm  = "确认刀片，进入关卡",
                bladeHint     = "点击手牌里的食材即与当前刀片交换，可反复换　｜　模块不能作为刀片",
                moduleRejected = "模块不能作为刀片",
            };

            return c;
        }

        // ── 构造小工具 ────────────────────────────────────────────────

        private static IngredientDto Ing(string id, string name, int h, int d, int v)
        {
            return new IngredientDto { id = id, name = name, h = h, d = d, v = v };
        }

        private static EffectDto Eff(string attr, string op, int value)
        {
            return new EffectDto { attr = attr, op = op, value = value };
        }

        private static ModuleDto Mod(string id, string name, params EffectDto[] effects)
        {
            return new ModuleDto { id = id, name = name, effects = effects };
        }

        private static DeckDto Deck(string id, string name, string initialBladeId,
                                    string[] ingredientIds, params ModuleDto[] modules)
        {
            return new DeckDto
            {
                id = id,
                name = name,
                initialBladeId = initialBladeId,
                ingredientIds = ingredientIds,
                modules = modules,
            };
        }
    }
}
