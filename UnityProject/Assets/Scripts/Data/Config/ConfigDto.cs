using System;

namespace GameJam.Data
{
    /// <summary>
    /// 配置表的数据传输对象（DTO）。
    ///
    /// 【为什么要有这一层】
    /// 运行时的 Ingredient / Deck / SpeedModule 挂着一堆 [NonSerialized] 运行时引用、
    /// 用 AttrSet 的定长数组存属性 —— 这些都不适合直接写进给策划看的配置表。
    ///
    /// 所以配置表用一套"扁平的、字段名和 JSON 一一对应"的 DTO，
    /// 由 GameConfig 统一翻译成运行时对象。
    /// 好处：**策划改 JSON、程序员改 DTO，两边结构永远一致**
    /// （因为 JSON 就是 DTO 的序列化形式，Unity 的 JsonUtility 直接认）。
    ///
    /// 【属性为什么写成 h/d/v 三个 int】
    /// 因为策划表里就是 H / D / V 三列。写成 AttrSet.values 数组的话，
    /// 策划要在配置里数下标，且以后加属性会让所有旧配置错位。
    /// 这里显式列出三个字母，加属性时 DTO 和 GameConfig 一起改，旧配置一眼能看出缺什么。
    /// </summary>
    [Serializable]
    public class IngredientDto
    {
        public string id;
        public string name;

        /// <summary>H 盐性（固体倾向）</summary>
        public int h;

        /// <summary>D 汞性（液体倾向）</summary>
        public int d;

        /// <summary>V 硫性（气体倾向）</summary>
        public int v;
    }

    /// <summary>一条效果。attr 写 "H"/"D"/"V"，op 写 Add/Sub/MulPercent/Set/Min/Max。</summary>
    [Serializable]
    public class EffectDto
    {
        public string attr;
        public string op;
        public int value;
    }

    /// <summary>
    /// 变速模块。
    /// 注意**没有"效果描述"字段** —— 描述由 effects 自动生成（SpeedModule.Description()），
    /// 手写描述迟早和实际效果脱节。
    /// </summary>
    [Serializable]
    public class ModuleDto
    {
        public string id;
        public string name;
        public EffectDto[] effects;
    }

    /// <summary>
    /// 一副牌组。
    ///
    /// ingredients 是**食材 id 的引用**，实际数据在根节点的 ingredients 表里 ——
    /// 同一个食材被多副牌组用到时只定义一次。
    /// </summary>
    [Serializable]
    public class DeckDto
    {
        public string id;
        public string name;

        /// <summary>开局当刀片的那张牌的 id。留空则退回列表第一张。</summary>
        public string initialBladeId;

        /// <summary>这副牌的全部食材，按顺序。第一张通常就是初始刀片。</summary>
        public string[] ingredientIds;

        public ModuleDto[] modules;
    }

    /// <summary>关卡。</summary>
    [Serializable]
    public class LevelDto
    {
        public string id;
        public string name;

        /// <summary>第一关目标分，例如 1000</summary>
        public int targetScore;
    }

    /// <summary>
    /// 界面文案。策划可以直接改，不用碰代码。
    /// </summary>
    [Serializable]
    public class TextDto
    {
        public string deckTitle;        // 选择你的初始牌组
        public string deckConfirm;      // 确认选择该卡组
        public string deckCancel;       // 取消
        public string bladeTitle;       // 选择你的刀片
        public string bladeConfirm;     // 确认刀片，进入关卡
        public string moduleRejected;   // 模块不能作为刀片
        public string deckHint;
        public string bladeHint;
    }

    /// <summary>整份配置表的根节点。</summary>
    [Serializable]
    public class GameConfigDto
    {        /// <summary>全部食材图鉴。牌组通过 id 引用它们。</summary>
        public IngredientDto[] ingredients;

        public DeckDto[] decks;

        /// <summary>所有牌组之外的兜底刀片 id（铁块）。牌组为空时才会用到。</summary>
        public string defaultBladeId;

        /// <summary>
        /// 全部关卡，按顺序。
        /// 每个关卡自己带着完整的选择环节（见 GameConfig.BuildLevel），
        /// 所以加一关只是在配置里多写一条，不用改代码。
        /// </summary>
        public LevelDto[] levels;

        /// <summary>旧的单关字段。levels 没配时退回用它，保持老配置还能读。</summary>
        public LevelDto level;

        public TextDto texts;
    }

    // ══════════════════════════════════════════════════════════════════
    //  卡牌需求设计 v2.1（Resources/Config/cards_v21.json）
    //
    //  单独一份文件、单独一套 DTO，不塞进 GameConfigDto：
    //  game_config.json 装的是"这一局怎么玩"（牌组 / 关卡 / 数值），
    //  cards_v21.json 装的是"卡牌长什么样、有什么规则"（形态 / 标签 / 转换 / 献祭）。
    //  两者的改动节奏完全不同 —— 策划改卡的描述不该动到关卡配置。
    // ══════════════════════════════════════════════════════════════════

    /// <summary>一次形态转换，两半分开存："易燃 + 热" → "火焰"。</summary>
    [Serializable]
    public class FormChangeDto
    {
        public string trigger;
        public string result;
    }

    /// <summary>一张素材卡（materials 数组元素）。</summary>
    [Serializable]
    public class MaterialSpecDto
    {
        public string id;
        public string name;

        /// <summary>系列/分组，例如 "水"、"铁"、"通用与特殊"。</summary>
        public string series;

        /// <summary>形态：固体 / 液体 / 气体 / 粉末。</summary>
        public string form;

        /// <summary>标签，例如 ["固体","易燃","遇热","献祭"]。</summary>
        public string[] tags;

        public FormChangeDto[] transitions;

        /// <summary>D 耗尽后变成什么（可能多张）。</summary>
        public string[] exhaust;

        public string startup;
        public string sacrifice;
        public string note;
    }

    /// <summary>一个法术（spells 数组元素）。</summary>
    [Serializable]
    public class SpellSpecDto
    {
        public string id;
        public string name;
        public string series;

        /// <summary>附魔类型：热 / 冷 / 酸 / 催化（不用附魔的留空）。</summary>
        public string enchant;

        /// <summary>分类，例如 "直接得分"。</summary>
        public string category;

        /// <summary>需求 / 效果原文。</summary>
        public string requirement;

        /// <summary>来源（谁产出它）。</summary>
        public string[] sources;
    }

    /// <summary>cards_v21.json 的根节点。</summary>
    [Serializable]
    public class CardSpecFileDto
    {
        public MaterialSpecDto[] materials;
        public SpellSpecDto[] spells;
    }
}
