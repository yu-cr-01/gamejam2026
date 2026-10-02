using System.Collections.Generic;
using UnityEngine;
using GameJam.Data;

namespace GameJam.Prototype
{
    /// <summary>
    /// 配置入口 —— 全游戏唯一读配置的地方。
    ///
    /// 【读什么】
    /// 优先读 Assets/Resources/Config/game_config.json（策划改这个文件）。
    /// 文件不存在或解析失败 → 退回 FakeData.BuiltIn() 的内置默认。
    /// 两条路走的是**同一套构建代码**，所以不存在"JSON 能用但兜底数据行为不一样"。
    ///
    /// 【为什么 DTO 和构建分开】
    /// DTO 只描述"配置长什么样"，构建负责把它翻译成运行时的
    /// Deck / Ingredient / SpeedModule / LevelData。
    /// 将来换成 Excel 导表或 ScriptableObject，只要产出同一个 DTO，这里一行都不用改。
    ///
    /// 【对外保持不变的三件套】
    ///   GameConfig.Decks()         —— 所有牌组（每次返回新对象）
    ///   GameConfig.Level()         —— 关卡（含它自己声明的选择环节）
    ///   GameConfig.DefaultBlade()  —— 兜底刀片（铁块）
    /// </summary>
    public static class GameConfig
    {
        /// <summary>Resources 下的路径，不带扩展名。</summary>
        public const string ResourcePath = "Config/game_config";

        // ── 选择环节的 id ─────────────────────────────────────────────
        public const string DeckPickId  = "deck_pick";
        public const string BladePickId = "blade_pick";

        private static bool loaded;
        private static string source = "（未加载）";

        private static List<Deck> decks = new List<Deck>();
        private static LevelData level;

        /// <summary>全部关卡，顺序就是配置里的顺序。</summary>
        private static List<LevelData> levels = new List<LevelData>();
        private static Ingredient defaultBlade;
        private static TextDto texts = new TextDto();

        /// <summary>实际生效的配置来源，界面/日志上显示，方便排查配置没生效。</summary>
        public static string Source { get { EnsureLoaded(); return source; } }

        /// <summary>强制下次访问时重新加载。改完 JSON 不想重启 Play 时可以调。</summary>
        public static void Reload()
        {
            loaded = false;
            source = "（未加载）";
        }

        public static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;

            GameConfigDto dto;
            if (TryLoadFromJson(out dto))
            {
                source = "配置文件 game_config.json";
            }
            else
            {
                dto = FakeData.BuiltIn();
                source = "内置默认（FakeData.BuiltIn）";
            }

            Build(dto);
        }

        // ── 对外三件套（都返回新对象，玩过一局不污染原始配置）──────────

        public static List<Deck> Decks()
        {
            EnsureLoaded();
            List<Deck> copy = new List<Deck>();
            for (int i = 0; i < decks.Count; i++)
                if (decks[i] != null) copy.Add(decks[i].Clone());
            return copy;
        }

        public static LevelData Level()
        {
            EnsureLoaded();
            return level != null ? level.Clone() : new LevelData("level_1", "第 1 关", 1000);
        }

        /// <summary>
        /// 全部关卡（克隆）。
        /// 返回克隆而不是原对象：调用方会往 LevelData 上挂运行时状态，
        /// 直接给出去的话第二次读到的就是被改过的配置。
        /// </summary>
        public static List<LevelData> Levels()
        {
            EnsureLoaded();

            List<LevelData> result = new List<LevelData>();
            for (int i = 0; i < levels.Count; i++)
                if (levels[i] != null) result.Add(levels[i].Clone());

            return result;
        }

        public static Ingredient DefaultBlade()
        {
            EnsureLoaded();
            return defaultBlade != null ? defaultBlade.Clone() : null;
        }

        public static TextDto Texts()
        {
            EnsureLoaded();
            return texts;
        }

        // ── 读文件 ────────────────────────────────────────────────────

        private static bool TryLoadFromJson(out GameConfigDto dto)
        {
            dto = null;

            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.LogWarning("[GameConfig] 找不到 Resources/" + ResourcePath
                                 + ".json，改用内置默认配置。");
                return false;
            }

            try
            {
                GameConfigDto parsed = JsonUtility.FromJson<GameConfigDto>(asset.text);
                if (parsed == null || parsed.decks == null || parsed.decks.Length == 0)
                {
                    Debug.LogWarning("[GameConfig] 配置表里没有牌组，改用内置默认配置。");
                    return false;
                }
                dto = parsed;
                Debug.Log("[GameConfig] 已从配置文件加载 " + parsed.decks.Length + " 副牌组。");
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[GameConfig] 配置表解析失败，改用内置默认配置：" + e.Message);
                return false;
            }
        }

        // ── 构建运行时对象 ────────────────────────────────────────────

        private static void Build(GameConfigDto dto)
        {
            // ① 食材图鉴 —— 全部注册进 Catalog，牌组只按 id 引用
            IngredientCatalog.Clear();
            if (dto.ingredients != null)
            {
                for (int i = 0; i < dto.ingredients.Length; i++)
                {
                    IngredientDto d = dto.ingredients[i];
                    if (d == null || string.IsNullOrEmpty(d.id)) continue;

                    AttrSet attrs = new AttrSet();
                    attrs.Set(AttrId.Salt, d.h);
                    attrs.Set(AttrId.Mercury, d.d);
                    attrs.Set(AttrId.Sulfur, d.v);

                    IngredientCatalog.Register(new Ingredient(d.id, d.name, attrs));
                }
            }

            // ② 兜底刀片（铁块）
            defaultBlade = string.IsNullOrEmpty(dto.defaultBladeId)
                ? null
                : IngredientCatalog.Create(dto.defaultBladeId);

            // ③ 牌组
            decks = new List<Deck>();
            if (dto.decks != null)
            {
                for (int i = 0; i < dto.decks.Length; i++)
                {
                    Deck deck = BuildDeck(dto.decks[i]);
                    if (deck != null) decks.Add(deck);
                }
            }

            // ④ 文案
            texts = dto.texts != null ? dto.texts : new TextDto();
            FillTextDefaults(texts);

            // ⑤ 关卡（自动带上两个选择环节）
            // 关卡：优先读 levels 数组，没有就退回旧的单个 level 字段。
            // 两条路都走 BuildLevel —— 每个关卡自己带着完整的牌组选择环节，
            // 所以加一关只是在配置里多写一条。
            levels = new List<LevelData>();

            if (dto.levels != null && dto.levels.Length > 0)
            {
                for (int i = 0; i < dto.levels.Length; i++)
                    levels.Add(BuildLevel(dto.levels[i], decks));
            }
            else
            {
                levels.Add(BuildLevel(dto.level, decks));
            }

            level = levels[0];   // 兼容只认单关的旧调用
        }

        private static Deck BuildDeck(DeckDto dto)
        {
            if (dto == null || string.IsNullOrEmpty(dto.id)) return null;

            Deck deck = new Deck(dto.id, dto.name);
            deck.initialBladeId = dto.initialBladeId;

            if (dto.ingredientIds != null)
            {
                for (int i = 0; i < dto.ingredientIds.Length; i++)
                {
                    Ingredient ing = IngredientCatalog.Create(dto.ingredientIds[i]);
                    if (ing != null) deck.ingredients.Add(ing);
                }
            }

            if (dto.modules != null)
            {
                for (int i = 0; i < dto.modules.Length; i++)
                {
                    SpeedModule m = BuildModule(dto.modules[i]);
                    if (m != null) deck.WithModule(m);
                }
            }

            return deck;
        }

        private static SpeedModule BuildModule(ModuleDto dto)
        {
            if (dto == null || string.IsNullOrEmpty(dto.id)) return null;

            EffectGroup g = new EffectGroup(dto.name + " 效果");
            if (dto.effects != null)
            {
                for (int i = 0; i < dto.effects.Length; i++)
                {
                    EffectDto e = dto.effects[i];
                    if (e == null) continue;

                    AttrId attr;
                    if (!AttrCatalog.TryParse(e.attr, out attr))
                    {
                        Debug.LogWarning("[GameConfig] 模块 " + dto.id
                                         + " 里有无法识别的属性 \"" + e.attr + "\"，已跳过。");
                        continue;
                    }

                    EffectOp op;
                    if (!TryParseOp(e.op, out op))
                    {
                        Debug.LogWarning("[GameConfig] 模块 " + dto.id
                                         + " 里有无法识别的运算 \"" + e.op + "\"，已跳过。");
                        continue;
                    }

                    g.Add(attr, op, e.value);
                }
            }

            // 描述不写在配置里，由效果自动生成 —— 不会和实际效果脱节
            return new SpeedModule(dto.id, dto.name, g);
        }

        private static bool TryParseOp(string s, out EffectOp op)
        {
            op = EffectOp.Add;
            if (string.IsNullOrEmpty(s)) return false;
            if (System.Enum.TryParse<EffectOp>(s.Trim(), true, out op)) return true;

            // 容错：允许配置里写中文或符号
            switch (s.Trim())
            {
                case "+": case "加":   op = EffectOp.Add;        return true;
                case "-": case "减":   op = EffectOp.Sub;        return true;
                case "%": case "百分比": op = EffectOp.MulPercent; return true;
                case "=": case "设为": op = EffectOp.Set;        return true;
                default: return false;
            }
        }

        /// <summary>
        /// 关卡自己声明"有哪些选择环节" —— 状态机不认识牌组和刀片。
        /// 想加第三个环节（比如选模块）就在这里 WithChoice 追加一条。
        /// </summary>
        private static LevelData BuildLevel(LevelDto dto, List<Deck> decks)
        {
            string id     = (dto != null && !string.IsNullOrEmpty(dto.id))   ? dto.id   : "level_1";
            string name   = (dto != null && !string.IsNullOrEmpty(dto.name)) ? dto.name : "第 1 关";
            int    target = dto != null ? dto.targetScore : 1000;

            LevelData lv = new LevelData(id, name, target);

            // ── 选择①：三选一牌组（卡片形态）──
            Choice deckChoice = new Choice(DeckPickId, texts.deckTitle, 1)
                .WithHint(texts.deckHint)
                .WithView(ChoiceView.DeckCards);

            for (int i = 0; i < decks.Count; i++)
            {
                Deck d = decks[i];

                // 副标题：模块名 + 自动生成的效果描述
                string moduleText = "无";
                if (d.modules != null && d.modules.Count > 0 && d.modules[0] != null)
                    moduleText = d.modules[0].name + "（" + d.modules[0].Description() + "）";

                deckChoice.AddOptions(
                    new ChoiceOption(d.id, d.name, ChoiceOptionKind.Deck, d.id)
                        .WithSubtitle("变速模块：" + moduleText)
                        .WithDeck(d));
            }
            lv.WithChoice(deckChoice);

            // ── 选择②：选刀片（交换形态）──
            // 这一环节没有 options —— 它的候选项就是玩家手上的牌，
            // 由 TurnState 的刀片 + 手牌提供，点一下即交换。
            Choice bladeChoice = new Choice(BladePickId, texts.bladeTitle, 1)
                .WithHint(texts.bladeHint)
                .WithView(ChoiceView.BladeSwap);
            lv.WithChoice(bladeChoice);

            return lv;
        }

        /// <summary>配置里漏写的文案用默认值补上，避免界面出现空白。</summary>
        private static void FillTextDefaults(TextDto t)
        {
            if (t == null) return;
            if (string.IsNullOrEmpty(t.deckTitle))      t.deckTitle      = "选择你的初始牌组";
            if (string.IsNullOrEmpty(t.deckConfirm))    t.deckConfirm    = "确认选择该卡组";
            if (string.IsNullOrEmpty(t.deckCancel))     t.deckCancel     = "取消";
            if (string.IsNullOrEmpty(t.deckHint))       t.deckHint       = "点击卡片查看详情";
            if (string.IsNullOrEmpty(t.bladeTitle))     t.bladeTitle     = "选择你的刀片";
            if (string.IsNullOrEmpty(t.bladeConfirm))   t.bladeConfirm   = "确认刀片，进入关卡";
            if (string.IsNullOrEmpty(t.bladeHint))      t.bladeHint      = "点击手牌里的食材即与当前刀片交换";
            if (string.IsNullOrEmpty(t.moduleRejected)) t.moduleRejected = "模块不能作为刀片";
        }
    }
}
