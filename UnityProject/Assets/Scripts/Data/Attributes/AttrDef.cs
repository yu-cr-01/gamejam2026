using System;

namespace GameJam.Data
{
    /// <summary>
    /// 属性定义：一条属性长什么样。
    ///
    /// 和"属性数值"分开 —— 定义全局只有一份，数值每个食材各有一份。
    /// 这样界面、校验、数值策划都读定义，不用到处硬编码范围。
    ///
    /// 【三属性体系下的字段】
    /// 策划表的一行有 4 列：属性(字母) / 炼金名 / 代表 / 作用。
    /// 对应到这里就是 letter / displayName / tendency / description。
    /// 界面想显示 "H 盐性" 就用 Label，想显示作用就取 description。
    /// </summary>
    [Serializable]
    public class AttrDef
    {
        /// <summary>属性 ID</summary>
        public AttrId id;

        /// <summary>字母代号，例如 "H" / "D" / "V"</summary>
        public string letter;

        /// <summary>炼金名，例如 "盐性"。界面上主显示就用它。</summary>
        public string displayName;

        /// <summary>代表什么倾向，例如 "固体倾向"</summary>
        public string tendency;

        /// <summary>作用说明，例如 "耐久、生命值。H 归零，固态原材变粉末；刀片 H 归零爆刀。"</summary>
        public string description;

        /// <summary>最小值（含）</summary>
        public int min;

        /// <summary>最大值（含）</summary>
        public int max;

        /// <summary>单位，例如 "℃"，没有就留空</summary>
        public string unit;

        /// <summary>在界面上显示的步进方向 —— 这个属性越大通常越"强"还是越"弱"</summary>
        public bool higherIsStronger;

        public AttrDef() { letter = ""; displayName = ""; tendency = ""; description = ""; unit = ""; }

        public AttrDef(AttrId id, string letter, string displayName, string tendency,
                       string description, int min, int max,
                       string unit = "", bool higherIsStronger = true)
        {
            this.id = id;
            this.letter = letter;
            this.displayName = displayName;
            this.tendency = tendency;
            this.description = description;
            this.min = min;
            this.max = max;
            this.unit = unit;
            this.higherIsStronger = higherIsStronger;
        }

        /// <summary>界面用的短标签，例如 "H 盐性"。</summary>
        public string Label
        {
            get
            {
                return string.IsNullOrEmpty(letter) ? displayName : letter + " " + displayName;
            }
        }

        /// <summary>把数值夹到合法范围。所有写入属性值的地方都要过这一道。</summary>
        public int Clamp(int value)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>带单位的显示，例如 "20" / "20℃"</summary>
        public string Format(int value)
        {
            return unit.Length > 0 ? value + unit : value.ToString();
        }

        /// <summary>标签 + 数值，例如 "H 盐性 20"。</summary>
        public string FormatLabeled(int value)
        {
            return Label + " " + Format(value);
        }

        public override string ToString() { return Label; }
    }
}
