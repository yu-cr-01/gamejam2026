using System.Collections.Generic;
using GameJam.Data;

namespace GameJam.Prototype
{
    /// <summary>
    /// 全部假数据。数值是瞎填的，等策划慢慢设计。
    ///
    /// 注意：这里只提供"模板"，运行时一律用 Clone()，
    /// 免得玩过一局之后原始数据被改掉。
    /// </summary>
    public static class FakeData
    {
        // ── 食材 ───────────────────────────────────────────────────────
        //                          名字      硬度 温度 酸性 糖分 油脂 水分
        public static Ingredient IronBlock() { return new Ingredient("铁块",  10,   0,   0,   0,   0,   0); }
        public static Ingredient IceCube()   { return new Ingredient("冰块",   8, -15,   0,   0,   0,  10); }
        public static Ingredient Lemon()     { return new Ingredient("柠檬",   3,  20,   9,   0,   0,   8); }

        public static Ingredient Chocolate() { return new Ingredient("巧克力", 4,  25,   1,   7,   6,   2); }
        public static Ingredient Chili()     { return new Ingredient("辣椒",   3,  22,   3,   2,   1,   7); }
        public static Ingredient HardCandy() { return new Ingredient("硬糖",   9,  20,   1,  10,   0,   1); }

        public static Ingredient Milk()      { return new Ingredient("牛奶",   1,   5,   2,   4,   4,  10); }
        public static Ingredient CoffeeBean(){ return new Ingredient("咖啡豆", 8,  20,   5,   1,   3,   2); }
        public static Ingredient Butter()    { return new Ingredient("黄油",   2,  10,   1,   1,  10,   3); }

        /// <summary>按名字取食材模板，找不到返回 null。</summary>
        public static Ingredient ByName(string name)
        {
            switch (name)
            {
                case "铁块":   return IronBlock();
                case "冰块":   return IceCube();
                case "柠檬":   return Lemon();
                case "巧克力": return Chocolate();
                case "辣椒":   return Chili();
                case "硬糖":   return HardCandy();
                case "牛奶":   return Milk();
                case "咖啡豆": return CoffeeBean();
                case "黄油":   return Butter();
                default:       return null;
            }
        }

        // ── 变速模块 ───────────────────────────────────────────────────
        public static SpeedModule HighSpeed()
        {
            return new SpeedModule("高速模块", "刀片硬度 +2", "硬度", 2);
        }

        // ── 牌组 ───────────────────────────────────────────────────────
        public static List<Deck> AllDecks()
        {
            List<Deck> decks = new List<Deck>();

            decks.Add(new Deck("牌组 A",
                new[] { IronBlock(), IceCube(), Lemon() },
                HighSpeed()));

            decks.Add(new Deck("牌组 B",
                new[] { Chocolate(), Chili(), HardCandy() },
                HighSpeed()));

            decks.Add(new Deck("牌组 C",
                new[] { Milk(), CoffeeBean(), Butter() },
                HighSpeed()));

            return decks;
        }

        // ── 关卡 ───────────────────────────────────────────────────────
        public const string LevelName = "第 1 关";
        public const int TargetScore = 1000;

        /// <summary>按所选牌组构建关卡。</summary>
        public static LevelData BuildLevel(Deck deck)
        {
            return LevelData.FromDeck(deck, LevelName, TargetScore);
        }
    }
}
