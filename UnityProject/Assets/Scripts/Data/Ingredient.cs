using System;

namespace GameJam.Data
{
    /// <summary>
    /// 食材。游戏里所有可投放进杯子的东西。
    ///
    /// 数值范围（今天只定义结构，不做校验和逻辑）：
    ///   硬度 / 酸性 / 糖分 / 油脂 / 水分 : 0 ~ 10
    ///   温度                          : -20 ~ 300
    /// </summary>
    [Serializable]
    public class Ingredient
    {
        /// <summary>名字</summary>
        public string name;

        /// <summary>硬度 0~10</summary>
        public int hardness;

        /// <summary>温度 -20~300</summary>
        public int temperature;

        /// <summary>酸性 0~10</summary>
        public int acidity;

        /// <summary>糖分 0~10</summary>
        public int sugar;

        /// <summary>油脂 0~10</summary>
        public int oil;

        /// <summary>水分 0~10</summary>
        public int water;

        public Ingredient() { }

        public Ingredient(string name, int hardness, int temperature,
                          int acidity, int sugar, int oil, int water)
        {
            this.name = name;
            this.hardness = hardness;
            this.temperature = temperature;
            this.acidity = acidity;
            this.sugar = sugar;
            this.oil = oil;
            this.water = water;
        }

        /// <summary>深拷贝。手牌会被消耗，所以必须复制，不能引用同一份数据。</summary>
        public Ingredient Clone()
        {
            return new Ingredient(name, hardness, temperature, acidity, sugar, oil, water);
        }

        public override string ToString() { return name; }
    }
}
