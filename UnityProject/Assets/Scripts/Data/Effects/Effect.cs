using System;

namespace GameJam.Data
{
    /// <summary>
    /// 一条效果：作用在哪个属性、怎么作用、作用多少。
    ///
    /// 【关键设计】效果只认 AttrId，不认食材 / 刀片 / 模块。
    /// 所以同一条效果可以作用在任何东西上 —— 这正是"效果类组合"的前提。
    /// 例：[ 硬度 +2 ] 既能给刀片用，也能给食材用，也能给杯子用。
    /// </summary>
    [Serializable]
    public class Effect
    {
        /// <summary>作用在哪个属性</summary>
        public AttrId target;

        /// <summary>怎么作用</summary>
        public EffectOp op;

        /// <summary>作用多少</summary>
        public int value;

        public Effect() { }

        public Effect(AttrId target, EffectOp op, int value)
        {
            this.target = target;
            this.op = op;
            this.value = value;
        }

        /// <summary>
        /// 作用到一个属性集合上。
        /// 不用关心这个集合属于谁 —— 食材、刀片、杯子都是 AttrSet。
        /// 越界由 AttrSet.Set 内部的 Clamp 兜底。
        /// </summary>
        public void Apply(AttrSet set)
        {
            if (set == null) return;

            int cur = set.Get(target);
            int next;

            switch (op)
            {
                case EffectOp.Add:
                    next = cur + value;
                    break;

                case EffectOp.Sub:
                    next = cur - value;
                    break;

                case EffectOp.MulPercent:
                    next = (int)Math.Round(cur * (100.0 + value) / 100.0,
                                           MidpointRounding.AwayFromZero);
                    break;

                case EffectOp.Set:
                    next = value;
                    break;

                case EffectOp.Min:
                    next = cur > value ? cur : value;
                    break;

                case EffectOp.Max:
                    next = cur < value ? cur : value;
                    break;

                default:
                    next = cur;
                    break;
            }

            set.Set(target, next);
        }

        public Effect Clone()
        {
            return new Effect(target, op, value);
        }

        /// <summary>给人看的描述，例如 "硬度 +2" / "温度 -50%" / "硬度 ≥ 5"。</summary>
        public string Describe()
        {
            string attr = AttrCatalog.DisplayName(target);

            switch (op)
            {
                case EffectOp.Add:        return attr + " +" + value;
                case EffectOp.Sub:        return attr + " -" + value;
                case EffectOp.MulPercent: return attr + " " + (value >= 0 ? "+" : "") + value + "%";
                case EffectOp.Set:        return attr + " = " + value;
                case EffectOp.Min:        return attr + " ≥ " + value;
                case EffectOp.Max:        return attr + " ≤ " + value;
                default:                  return attr;
            }
        }

        public override string ToString() { return Describe(); }
    }
}
