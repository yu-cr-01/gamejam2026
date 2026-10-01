using System;

namespace GameJam.Data
{
    /// <summary>
    /// 属性定义：一条属性长什么样。
    ///
    /// 和"属性数值"分开 —— 定义全局只有一份，数值每个食材各有一份。
    /// 这样界面、校验、数值策划都读定义，不用到处硬编码范围。
    /// </summary>
    [Serializable]
    public class AttrDef
    {
        /// <summary>属性 ID</summary>
        public AttrId id;

        /// <summary>显示名，例如 "硬度"</summary>
        public string displayName;

        /// <summary>最小值（含）</summary>
        public int min;

        /// <summary>最大值（含）</summary>
        public int max;

        /// <summary>单位，例如 "℃"，没有就留空</summary>
        public string unit;

        /// <summary>在界面上显示的步进方向 —— 这个属性越大通常越"强"还是越"弱"</summary>
        public bool higherIsStronger;

        public AttrDef() { }

        public AttrDef(AttrId id, string displayName, int min, int max,
                       string unit = "", bool higherIsStronger = true)
        {
            this.id = id;
            this.displayName = displayName;
            this.min = min;
            this.max = max;
            this.unit = unit;
            this.higherIsStronger = higherIsStronger;
        }

        /// <summary>把数值夹到合法范围。所有写入属性值的地方都要过这一道。</summary>
        public int Clamp(int value)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>带单位的显示，例如 "20℃" / "10"</summary>
        public string Format(int value)
        {
            return unit.Length > 0 ? value + unit : value.ToString();
        }

        public override string ToString() { return displayName; }
    }
}
