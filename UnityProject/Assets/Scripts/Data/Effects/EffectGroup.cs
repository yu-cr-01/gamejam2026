using System;
using System.Collections.Generic;

namespace GameJam.Data
{
    /// <summary>
    /// 效果组合：一串效果的集合，按顺序执行。
    ///
    /// 【这是"效果类组合"的载体】
    /// 单个 Effect 只是一条效果，真正能复用和拼装的是 EffectGroup：
    ///
    ///     EffectGroup 高速模块 = [ 硬度 +2 ]
    ///     EffectGroup 辣椒叠加 = [ 温度 +5, 酸性 +1 ]
    ///     EffectGroup 组合     = 高速模块 + 辣椒叠加      ← 直接相加
    ///
    /// 组还可以嵌套组（用 Append），所以任意深度组合都支持：
    ///     关卡规则 = [ 全场硬度 +1 ] + 玩家身上所有模块 + 食材自身效果
    ///
    /// 【执行顺序】按列表顺序。所以 [ ×2, +3 ] 和 [ +3, ×2 ] 结果不同 ——
    /// 想控制顺序就调整加入顺序。
    /// </summary>
    [Serializable]
    public class EffectGroup
    {
        /// <summary>组合名，方便调试和界面显示，例如 "高速模块效果"</summary>
        public string name;

        /// <summary>效果列表，按顺序执行</summary>
        public List<Effect> effects = new List<Effect>();

        public EffectGroup() { name = ""; }

        public EffectGroup(string name)
        {
            this.name = name;
        }

        public EffectGroup(string name, params Effect[] items)
        {
            this.name = name;
            if (items != null)
            {
                for (int i = 0; i < items.Length; i++)
                {
                    if (items[i] != null) effects.Add(items[i]);
                }
            }
        }

        /// <summary>是否没有任何效果</summary>
        public bool IsEmpty { get { return effects == null || effects.Count == 0; } }

        public int Count { get { return effects != null ? effects.Count : 0; } }

        // ── 组合（链式，方便写数据）──────────────────────────────────

        /// <summary>追加一条效果</summary>
        public EffectGroup Add(Effect e)
        {
            if (e != null) effects.Add(e);
            return this;
        }

        /// <summary>追加一条效果（直接给参数）</summary>
        public EffectGroup Add(AttrId target, EffectOp op, int value)
        {
            effects.Add(new Effect(target, op, value));
            return this;
        }

        /// <summary>把另一个组合整个追加进来（嵌套组合）</summary>
        public EffectGroup Append(EffectGroup other)
        {
            if (other == null || other.effects == null) return this;
            for (int i = 0; i < other.effects.Count; i++)
            {
                Effect e = other.effects[i];
                if (e != null) effects.Add(e);
            }
            return this;
        }

        // ── 执行 ──────────────────────────────────────────────────────

        /// <summary>按顺序作用到属性集合上。</summary>
        public void Apply(AttrSet set)
        {
            if (set == null || effects == null) return;
            for (int i = 0; i < effects.Count; i++)
            {
                if (effects[i] != null) effects[i].Apply(set);
            }
        }

        /// <summary>作用一份拷贝并返回，不改原集合。</summary>
        public AttrSet ApplyToCopy(AttrSet source)
        {
            AttrSet copy = source != null ? source.Clone() : new AttrSet();
            Apply(copy);
            return copy;
        }

        // ── 工具 ──────────────────────────────────────────────────────

        /// <summary>把所有效果拼成一句话，例如 "硬度 +2、温度 +5"。</summary>
        public string Describe()
        {
            if (IsEmpty) return "（无效果）";
            List<string> parts = new List<string>();
            for (int i = 0; i < effects.Count; i++)
            {
                if (effects[i] != null) parts.Add(effects[i].Describe());
            }
            return string.Join("、", parts.ToArray());
        }

        public EffectGroup Clone()
        {
            EffectGroup g = new EffectGroup(name);
            if (effects != null)
            {
                for (int i = 0; i < effects.Count; i++)
                {
                    if (effects[i] != null) g.effects.Add(effects[i].Clone());
                }
            }
            return g;
        }

        /// <summary>把若干组合并成一个新的（不改动原组）。</summary>
        public static EffectGroup Combine(string name, params EffectGroup[] groups)
        {
            EffectGroup result = new EffectGroup(name);
            if (groups != null)
            {
                for (int i = 0; i < groups.Length; i++) result.Append(groups[i]);
            }
            return result;
        }

        public override string ToString()
        {
            return (name != null && name.Length > 0 ? name + "：" : "") + Describe();
        }
    }
}
