using System;

namespace GameJam.Data
{
    /// <summary>
    /// 一次形态转换：**什么条件 → 变成什么**。
    ///
    /// 规格里的写法是「易燃 + 热 → 火焰（D立即归零）」，这里拆成两半存：
    ///   trigger = "易燃 + 热"     —— 左侧：标签/属性条件
    ///   result  = "火焰（D立即归零）" —— 右侧：变什么（括号里是附加说明）
    ///
    /// 【为什么存文本而不是拆成枚举】
    ///   策划还在改这张表（v2.1 是第二版），现在就把它固化成枚举，
    ///   等于每次改文案都要改代码。规则引擎（CardRules）负责把这些文本
    ///   解释成"条件 + 产物"，解释不动的那部分原样显示给人看，
    ///   —— 先能跑、能显示，再逐步把常用写法升级成结构化字段。
    /// </summary>
    [Serializable]
    public class FormChange
    {
        /// <summary>触发条件，例如 "易燃 + 热"、"遇冷 + 冷"、"液体 + 热"。</summary>
        public string trigger = "";

        /// <summary>转换结果，例如 "火焰"、"水蒸气"、"空白卡（D立即归零）"。</summary>
        public string result = "";

        public FormChange() { }

        public FormChange(string trigger, string result)
        {
            this.trigger = trigger != null ? trigger : "";
            this.result = result != null ? result : "";
        }

        public FormChange Clone()
        {
            return new FormChange(trigger, result);
        }

        /// <summary>一行显示："易燃 + 热 → 火焰"。</summary>
        public string Describe()
        {
            if (string.IsNullOrEmpty(trigger)) return result;
            return trigger + " → " + result;
        }

        public override string ToString() { return Describe(); }
    }
}
