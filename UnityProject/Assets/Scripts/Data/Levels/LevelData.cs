using System;
using System.Collections.Generic;

namespace GameJam.Data
{
    /// <summary>
    /// 关卡数据。
    ///
    /// 【关键变化】关卡自己声明"有哪些选择环节"。
    /// 旧版的流程是写死在状态机里的：主菜单 → 三选一 → 选刀片 → 回合…
    /// 现在状态机只做一件事：按顺序把 choices 里的选择跑一遍。
    ///
    /// 所以：
    ///   第 1 关：3 选 1 牌组 → 选刀片
    ///   第 2 关：5 选 2 食材 → 选刀片 → 选模块
    ///   第 3 关：不选，直接开打
    /// 三种关卡用同一套状态机，只是 LevelData 不同。
    /// </summary>
    [Serializable]
    public class LevelData
    {
        /// <summary>唯一标识</summary>
        public string id;

        /// <summary>关卡名，例如 "第 1 关"</summary>
        public string name;

        /// <summary>目标分</summary>
        public int targetScore;

        /// <summary>这一关要走的选择环节，按顺序</summary>
        public List<Choice> choices = new List<Choice>();

        /// <summary>关卡级效果（全场生效的规则），参与效果组合</summary>
        public EffectGroup rules = new EffectGroup();

        public LevelData() { id = ""; name = ""; }

        public LevelData(string id, string name, int targetScore)
        {
            this.id = id;
            this.name = name;
            this.targetScore = targetScore;
        }

        public LevelData WithChoice(Choice c)
        {
            if (c != null) choices.Add(c);
            return this;
        }

        public LevelData WithRules(EffectGroup g)
        {
            if (g != null) rules = g;
            return this;
        }

        public int ChoiceCount { get { return choices != null ? choices.Count : 0; } }

        public Choice FindChoice(string choiceId)
        {
            if (choices == null || string.IsNullOrEmpty(choiceId)) return null;
            for (int i = 0; i < choices.Count; i++)
                if (choices[i] != null && choices[i].id == choiceId) return choices[i];
            return null;
        }

        public Choice ChoiceAt(int index)
        {
            if (choices == null || index < 0 || index >= choices.Count) return null;
            return choices[index];
        }

        public LevelData Clone()
        {
            LevelData l = new LevelData(id, name, targetScore);
            l.rules = rules != null ? rules.Clone() : new EffectGroup();
            l.choices = new List<Choice>();
            if (choices != null)
                for (int i = 0; i < choices.Count; i++)
                    if (choices[i] != null) l.choices.Add(choices[i].Clone());
            return l;
        }

        public override string ToString()
        {
            return name + "（目标 " + targetScore + "，选择环节 " + ChoiceCount + " 个）";
        }
    }
}
