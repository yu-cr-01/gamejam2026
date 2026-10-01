using System;
using System.Collections.Generic;

namespace GameJam.Data
{
    /// <summary>
    /// 一次选择的结果记录。
    ///
    /// 【为什么要单独记】选择本身（Choice）是"配置"，记录是"过程"。
    /// 分开之后：配置可以复用，记录可以回溯 ——
    /// 将来做复盘、成就、存档、录像回放，读的都是这个记录，不用碰配置。
    /// </summary>
    [Serializable]
    public class ChoiceRecord
    {
        /// <summary>对应哪个选择（Choice.id）</summary>
        public string choiceId;

        /// <summary>玩家选了哪些候选（ChoiceOption.id，按选择顺序）</summary>
        public List<string> pickedOptionIds = new List<string>();

        /// <summary>在第几回合做的。关卡开始前的选择记为 0。</summary>
        public int turnIndex;

        public ChoiceRecord() { choiceId = ""; }

        public ChoiceRecord(string choiceId, int turnIndex)
        {
            this.choiceId = choiceId;
            this.turnIndex = turnIndex;
        }

        public bool IsEmpty { get { return pickedOptionIds == null || pickedOptionIds.Count == 0; } }

        /// <summary>只选了一个的时候取那一个，否则返回空串。</summary>
        public string SinglePick
        {
            get
            {
                if (pickedOptionIds != null && pickedOptionIds.Count == 1) return pickedOptionIds[0];
                return "";
            }
        }

        public override string ToString()
        {
            string picks = IsEmpty ? "（未选）" : string.Join("、", pickedOptionIds.ToArray());
            return "[回合" + turnIndex + "] " + choiceId + " → " + picks;
        }
    }
}
