using System;
using System.Collections.Generic;

namespace GameJam.Data
{
    /// <summary>
    /// 一次选择：给玩家若干候选，让他挑 N 个。
    ///
    /// 【这是把"三选一"从界面里抽出来的关键】
    /// 旧版把"三选一牌组"写死在状态机里，换成"五选二刀片"就得改状态机。
    /// 现在关卡自己声明有哪些选择，"三选一"只是 pickCount=1 且 options 有 3 个的一个实例。
    ///
    /// 用法举例：
    ///     三选一牌组 = new Choice("deck_pick", "选择你的牌组", 1).AddOptions(...)
    ///     五选二食材 = new Choice("ingredient_pick", "挑两样食材", 2).AddOptions(...)
    ///     默认刀片   = new Choice("blade_pick", "选刀片", 1).WithDefault("iron_block")
    /// </summary>
    [Serializable]
    public class Choice
    {
        /// <summary>选择 id，例如 "deck_pick"。用于记录玩家选了什么。</summary>
        public string id;

        /// <summary>界面标题，例如 "三选一牌组"</summary>
        public string prompt;

        /// <summary>补充说明（可为空）</summary>
        public string hint;

        /// <summary>要选几个。三选一 = options 有 3 个、pickCount = 1。</summary>
        public int pickCount = 1;

        /// <summary>是否允许跳过不选</summary>
        public bool allowSkip;

        /// <summary>默认选中项的 id。刀片那种"默认铁块"就用它。</summary>
        public string defaultOptionId;

        /// <summary>候选项</summary>
        public List<ChoiceOption> options = new List<ChoiceOption>();

        public Choice() { id = ""; prompt = ""; hint = ""; }

        public Choice(string id, string prompt, int pickCount = 1)
        {
            this.id = id;
            this.prompt = prompt;
            this.pickCount = pickCount;
            this.hint = "";
        }

        public Choice WithHint(string h) { hint = h; return this; }

        public Choice WithDefault(string optionId) { defaultOptionId = optionId; return this; }

        public Choice AddOptions(params ChoiceOption[] items)
        {
            if (items != null)
                for (int i = 0; i < items.Length; i++)
                    if (items[i] != null) options.Add(items[i]);
            return this;
        }

        public int OptionCount { get { return options != null ? options.Count : 0; } }

        /// <summary>按 id 找候选。</summary>
        public ChoiceOption Find(string optionId)
        {
            if (options == null || string.IsNullOrEmpty(optionId)) return null;
            for (int i = 0; i < options.Count; i++)
                if (options[i] != null && options[i].id == optionId) return options[i];
            return null;
        }

        /// <summary>
        /// 默认候选 —— 只认显式配置的 defaultOptionId。
        ///
        /// 【注意】没配就是没配，不要退回第一个可选项。
        /// 之前退回第一个，导致"三选一牌组"这种没有默认项的选择
        /// 也会把牌组 A 标成已选，界面看起来像只有它被选上了。
        /// </summary>
        public ChoiceOption DefaultOption
        {
            get
            {
                if (string.IsNullOrEmpty(defaultOptionId)) return null;
                return Find(defaultOptionId);
            }
        }

        /// <summary>把选择结果记成一条记录。</summary>
        public ChoiceRecord MakeRecord(int turnIndex, params string[] pickedOptionIds)
        {
            ChoiceRecord r = new ChoiceRecord(id, turnIndex);
            if (pickedOptionIds != null)
                for (int i = 0; i < pickedOptionIds.Length; i++)
                    if (!string.IsNullOrEmpty(pickedOptionIds[i])) r.pickedOptionIds.Add(pickedOptionIds[i]);
            return r;
        }

        public Choice Clone()
        {
            Choice c = new Choice(id, prompt, pickCount);
            c.hint = hint;
            c.allowSkip = allowSkip;
            c.defaultOptionId = defaultOptionId;
            c.options = new List<ChoiceOption>();
            if (options != null)
            {
                for (int i = 0; i < options.Count; i++)
                {
                    ChoiceOption o = options[i];
                    if (o == null) continue;
                    ChoiceOption n = new ChoiceOption(o.id, o.title, o.kind, o.payloadId);
                    n.subtitle = o.subtitle;
                    n.enabled = o.enabled;
                    n.deck = o.deck;
                    n.ingredient = o.ingredient != null ? o.ingredient.Clone() : null;
                    n.module = o.module;
                    c.options.Add(n);
                }
            }
            return c;
        }

        public override string ToString()
        {
            return prompt + "（" + OptionCount + " 选 " + pickCount + "）";
        }
    }
}
