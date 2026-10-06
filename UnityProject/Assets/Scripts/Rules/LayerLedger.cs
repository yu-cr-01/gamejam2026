using System;
using System.Collections.Generic;

namespace GameJam.Rules
{
    /// <summary>刀片上的四种附魔层数。</summary>
    public enum LayerKind
    {
        /// <summary>热（火焰、余温、白磷/碳/硫磺献祭都给这个）</summary>
        Heat = 0,

        /// <summary>冷（冰霜、水/冰/汞系列献祭给这个）</summary>
        Cold = 1,

        /// <summary>酸（酸蚀、铜/硫磺系列献祭给这个）</summary>
        Acid = 2,

        /// <summary>催化（催化术、盐系列献祭给这个）</summary>
        Catalyst = 3,
    }

    /// <summary>
    /// 一种层数的两份计数：**会衰退的** 和 **不衰退的**。
    ///
    /// 【为什么分两份而不是给每层打标记】
    ///   规则里只出现两种问法：
    ///     「消耗所有热层数」—— 不区分衰退与否，两份一起消耗
    ///     「每回合结束衰减」—— 只动衰退那份
    ///   没有出现过"指定消耗某一层"的说法，所以两份计数就够了，
    ///   不必维护一个逐层的列表（那样每次消耗还要挑层、还要处理顺序）。
    ///
    /// 【消耗顺序】
    ///   部分消耗（"额外消耗1点"这类）**先吃衰退层**：不衰退层的价值就在于留得住，
    ///   先吃它等于把"不衰退"这个设计意图吃掉了。
    ///   整体消耗（"消耗所有X层数"）两份都吃 —— 策划备注里写明了这条。
    /// </summary>
    [Serializable]
    public class LayerStack
    {
        /// <summary>会随回合衰退的层数</summary>
        public int decaying;

        /// <summary>不衰退的层数（不随回合衰减，也不会因为启动减少）</summary>
        public int permanent;

        public int Total { get { return decaying + permanent; } }

        public bool IsEmpty { get { return Total <= 0; } }

        public LayerStack Clone()
        {
            return new LayerStack { decaying = decaying, permanent = permanent };
        }
    }

    /// <summary>
    /// 刀片身上的四种层数账本 —— 规则的**唯一**读写入口。
    ///
    /// 【为什么集中成一个账本】
    ///   几乎每条规则都在动层数：献祭加层、启动消耗层、法术翻倍层、回合末衰减。
    ///   如果让每张卡各写各的（card.layers.heat++ 之类），
    ///   "不衰退"和"消耗顺序"这两条约定就会散落在几十处，改一条规则要翻遍全场。
    ///   这里把语义收进四个方法：Add / Consume / ConsumeAll / DecayTurn，
    ///   卡牌规则只描述"加几层、消耗几层"，不碰计数器。
    /// </summary>
    public class LayerLedger
    {
        public const int KindCount = 4;

        private readonly LayerStack[] stacks = new LayerStack[KindCount];

        public LayerLedger()
        {
            for (int i = 0; i < KindCount; i++) stacks[i] = new LayerStack();
        }

        public LayerStack Stack(LayerKind kind) { return stacks[(int)kind]; }

        public int Count(LayerKind kind) { return stacks[(int)kind].Total; }

        /// <summary>这类层数"有附魔"吗（规则里的「若刀片有热附魔」）。</summary>
        public bool Has(LayerKind kind) { return stacks[(int)kind].Total > 0; }

        /// <summary>加层。permanent = true 表示这批层数不衰退。</summary>
        public void Add(LayerKind kind, int amount, bool permanent = false)
        {
            if (amount <= 0) return;
            LayerStack s = stacks[(int)kind];
            if (permanent) s.permanent += amount;
            else s.decaying += amount;
        }

        /// <summary>
        /// 部分消耗：**先吃衰退层**，不够再吃不衰退层。
        /// 返回真正消耗掉的层数（不够就消耗现有的全部）。
        /// </summary>
        public int Consume(LayerKind kind, int amount)
        {
            if (amount <= 0) return 0;

            LayerStack s = stacks[(int)kind];
            int taken = 0;

            int a = Math.Min(amount, s.decaying);
            s.decaying -= a;
            taken += a;

            int b = Math.Min(amount - taken, s.permanent);
            s.permanent -= b;
            taken += b;

            return taken;
        }

        /// <summary>
        /// 整体消耗：两份都清空（策划备注：「不衰退」也会被"消耗所有 X 层数"消耗）。
        /// 返回清掉的总层数 —— 很多规则的收益就是"每消耗一层得 X 分"。
        /// </summary>
        public int ConsumeAll(LayerKind kind)
        {
            LayerStack s = stacks[(int)kind];
            int all = s.Total;
            s.decaying = 0;
            s.permanent = 0;
            return all;
        }

        /// <summary>层数翻倍（盐的启动、酸爆）。两份各自翻倍。</summary>
        public void Double(LayerKind kind)
        {
            LayerStack s = stacks[(int)kind];
            s.decaying *= 2;
            s.permanent *= 2;
        }

        /// <summary>
        /// 回合结束的衰减：**只掉衰退层，每类掉 1 层**。
        /// 不衰退层永远不动 —— 这是"不衰退"的全部含义（见策划备注）。
        /// </summary>
        public void DecayTurn()
        {
            for (int i = 0; i < KindCount; i++)
                if (stacks[i].decaying > 0) stacks[i].decaying--;
        }

        /// <summary>
        /// 冷热冲突：**后附魔覆盖先附魔，清空对方全部层数**（策划规则表 F 原文）。
        ///
        /// 【这里一开始写错了】
        ///   卡表备注写的是"冷热之间会按照规则互相抵消"，我先按**成对抵消**实现了
        ///   （热的 3 层配冷的 3 层，各扣 3）。读完正文才知道"规则"指的是
        ///   「后附魔覆盖先附魔」—— 是**后到的一方把对方整类清零**，不是按数量对消。
        ///   例：先有热×1，再打一张冰霜（冷×1）→ 冷×1，热清零（不是两边都归零）。
        ///
        /// 酸、催化与谁都不冲突（规则表 F：酸催共存），所以这里只处理冷热这一对。
        /// </summary>
        public void ApplyConflict(LayerKind incoming)
        {
            if (incoming == LayerKind.Heat) stacks[(int)LayerKind.Cold].decaying = 0;
            else if (incoming == LayerKind.Cold) stacks[(int)LayerKind.Heat].decaying = 0;

            if (incoming == LayerKind.Heat) stacks[(int)LayerKind.Cold].permanent = 0;
            else if (incoming == LayerKind.Cold) stacks[(int)LayerKind.Heat].permanent = 0;
        }

        /// <summary>
        /// 催化对触发阈值的修正：催≥1 → 阈值 -1；催≥3 再 -1（当前 Demo 不开放，先把规则写对）。
        /// 例：热"热≥2 改变形态"这条，有 1 层催化时只要 1 层热就能触发。
        /// </summary>
        public int LowerThreshold(int baseThreshold)
        {
            int t = baseThreshold;
            int cat = Count(LayerKind.Catalyst);
            if (cat >= 1) t -= 1;
            if (cat >= 3) t -= 1;
            return t < 1 ? 1 : t;
        }

        public LayerLedger Clone()
        {
            LayerLedger copy = new LayerLedger();
            for (int i = 0; i < KindCount; i++) copy.stacks[i] = stacks[i].Clone();
            return copy;
        }

        /// <summary>一行摘要，例如 "热×3（含1不衰退） 酸×2"。</summary>
        public string Describe()
        {
            List<string> parts = new List<string>();
            for (int i = 0; i < KindCount; i++)
            {
                LayerStack s = stacks[i];
                if (s.IsEmpty) continue;

                string t = Name((LayerKind)i) + "×" + s.Total;
                if (s.permanent > 0) t += "（含" + s.permanent + "不衰退）";
                parts.Add(t);
            }
            return parts.Count == 0 ? "（无层数）" : string.Join(" ", parts);
        }

        public static string Name(LayerKind kind)
        {
            switch (kind)
            {
                case LayerKind.Heat:     return "热";
                case LayerKind.Cold:     return "冷";
                case LayerKind.Acid:     return "酸";
                case LayerKind.Catalyst: return "催化";
            }
            return "?";
        }

        /// <summary>中文名 → 枚举（读策划文本里的"热/冷/酸/催化"用）。</summary>
        public static bool TryParse(string text, out LayerKind kind)
        {
            kind = LayerKind.Heat;
            if (string.IsNullOrEmpty(text)) return false;

            if (text.Contains("热")) { kind = LayerKind.Heat; return true; }
            if (text.Contains("冷")) { kind = LayerKind.Cold; return true; }
            if (text.Contains("酸")) { kind = LayerKind.Acid; return true; }
            if (text.Contains("催化")) { kind = LayerKind.Catalyst; return true; }
            return false;
        }
    }
}
