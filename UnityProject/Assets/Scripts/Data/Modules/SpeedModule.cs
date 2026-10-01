using System;

namespace GameJam.Data
{
    /// <summary>
    /// 变速模块。
    ///
    /// 【和旧版的区别】
    /// 旧版是 `string targetAttribute` + `int value` —— 字符串匹配属性，
    /// 加一个属性要改所有用到的地方，也没法一次改多个属性。
    ///
    /// 现在挂一个 EffectGroup，于是：
    ///   - 一个模块可以同时改多个属性
    ///   - 模块之间可以组合
    ///   - 模块还能和食材自带效果、关卡规则进一步组合
    ///
    /// 描述文案不再手写，由 effects.Describe() 自动生成，不会和实际效果脱节。
    /// </summary>
    [Serializable]
    public class SpeedModule
    {
        /// <summary>唯一标识</summary>
        public string id;

        /// <summary>显示名，例如 "高速模块"</summary>
        public string name;

        /// <summary>效果组合 —— 这是模块的全部行为</summary>
        public EffectGroup effects;

        public SpeedModule()
        {
            id = "";
            name = "";
            effects = new EffectGroup();
        }

        public SpeedModule(string id, string name, EffectGroup effects)
        {
            this.id = id;
            this.name = name;
            this.effects = effects != null ? effects : new EffectGroup();
        }

        /// <summary>效果描述，自动生成，例如 "硬度 +2"</summary>
        public string Description()
        {
            return effects != null ? effects.Describe() : "（无效果）";
        }

        /// <summary>把这个模块的效果作用到一个属性集合上。</summary>
        public void ApplyTo(AttrSet set)
        {
            if (effects != null) effects.Apply(set);
        }

        public SpeedModule Clone()
        {
            return new SpeedModule(id, name, effects != null ? effects.Clone() : new EffectGroup());
        }

        public override string ToString()
        {
            return name + "（" + Description() + "）";
        }
    }
}
