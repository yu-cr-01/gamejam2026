using System;

namespace GameJam.Data
{
    /// <summary>
    /// 变速模块。每副牌组带 1 个，用来修改刀片或其它属性的数值。
    ///
    /// 例：高速模块 → 名字"高速模块"，效果描述"刀片硬度 +2"，
    ///     影响属性"硬度"，数值 2。
    /// </summary>
    [Serializable]
    public class SpeedModule
    {
        /// <summary>名字</summary>
        public string name;

        /// <summary>效果描述，给玩家看的，例如 "刀片硬度 +2"</summary>
        public string description;

        /// <summary>影响的属性，例如 "硬度"。今天只是记录，不做实际生效逻辑。</summary>
        public string targetAttribute;

        /// <summary>数值，例如 2</summary>
        public int value;

        public SpeedModule() { }

        public SpeedModule(string name, string description, string targetAttribute, int value)
        {
            this.name = name;
            this.description = description;
            this.targetAttribute = targetAttribute;
            this.value = value;
        }

        public SpeedModule Clone()
        {
            return new SpeedModule(name, description, targetAttribute, value);
        }

        public override string ToString() { return name; }
    }
}
