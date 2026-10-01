using System;
using System.Collections.Generic;

namespace GameJam.Data
{
    /// <summary>
    /// 选择记录簿：把玩家一局里做过的所有选择按顺序记下来。
    ///
    /// 【为什么独立一个类】
    /// "玩家选了什么"是过程数据，不属于牌组、不属于关卡、也不属于回合状态。
    /// 单独维护之后，将来的复盘 / 成就 / 存档 / 回放都只读它。
    ///
    /// 补一句：回合内的选择会带上 turnIndex，所以能还原"第几回合选了什么"。
    /// </summary>
    [Serializable]
    public class SelectionLog
    {
        public List<ChoiceRecord> records = new List<ChoiceRecord>();

        public int Count { get { return records != null ? records.Count : 0; } }

        public void Add(ChoiceRecord record)
        {
            if (record == null) return;
            records.Add(record);
        }

        /// <summary>快捷记一条：log.Record("deck_pick", 0, "deck_a")</summary>
        public void Record(string choiceId, int turnIndex, params string[] pickedOptionIds)
        {
            ChoiceRecord r = new ChoiceRecord(choiceId, turnIndex);
            if (pickedOptionIds != null)
                for (int i = 0; i < pickedOptionIds.Length; i++)
                    if (!string.IsNullOrEmpty(pickedOptionIds[i])) r.pickedOptionIds.Add(pickedOptionIds[i]);
            records.Add(r);
        }

        public bool HasChoice(string choiceId)
        {
            return Find(choiceId) != null;
        }

        public ChoiceRecord Find(string choiceId)
        {
            if (records == null || string.IsNullOrEmpty(choiceId)) return null;
            for (int i = 0; i < records.Count; i++)
                if (records[i] != null && records[i].choiceId == choiceId) return records[i];
            return null;
        }

        /// <summary>某个选择只选了一项时，直接取那一项；否则返回空串。</summary>
        public string SinglePick(string choiceId)
        {
            ChoiceRecord r = Find(choiceId);
            return r != null ? r.SinglePick : "";
        }

        /// <summary>某个选择选的所有项。</summary>
        public List<string> Picks(string choiceId)
        {
            ChoiceRecord r = Find(choiceId);
            return r != null && r.pickedOptionIds != null
                 ? new List<string>(r.pickedOptionIds)
                 : new List<string>();
        }

        public ChoiceRecord Latest()
        {
            if (records == null || records.Count == 0) return null;
            return records[records.Count - 1];
        }

        public void Clear()
        {
            records.Clear();
        }

        public string Describe()
        {
            if (Count == 0) return "（还没做过选择）";
            List<string> parts = new List<string>();
            for (int i = 0; i < records.Count; i++)
                if (records[i] != null) parts.Add(records[i].ToString());
            return string.Join("\n", parts.ToArray());
        }
    }
}
