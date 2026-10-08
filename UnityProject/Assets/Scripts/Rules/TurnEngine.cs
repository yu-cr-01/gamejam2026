using System;
using System.Collections.Generic;
using System.Text;
using GameJam.Data;

namespace GameJam.Rules
{
    /// <summary>
    /// 结算参数 —— **只放正文确实没写的数**。
    ///
    /// 【正文写了的，一律写死在代码里，不进这里】
    ///   每回合 5 次行动机会、每关 4 回合（§2.6）、启动消耗 1 行动机会 + 1 刀片H（§四.1）、
    ///   爆刀 = 当前分数 ×2（§五 / §八）、V 档位映射（§2.2）……
    ///   这些是规则，不是旋钮；放进配置只会让"改配置"变成"改规则"。
    ///
    /// 【这里每一条都是"正文没给"或"正文自相矛盾"】
    ///   每条都写了为什么需要它、默认值怎么来的。策划拍板后改这里一处即可。
    /// </summary>
    public class TurnRules
    {
        /// <summary>
        /// 【正文自相矛盾，做成开关】启动时要不要消耗 1 层附魔。
        ///
        ///   规则表 F：「启动消耗：启动时无论是否发生效果，消耗1层附魔层数」
        ///   §2.4 / §七-7：「启动时不消耗附魔层数：附魔层数仅在回合结束衰减」
        ///
        /// 默认**按规则表 F 实现（true）**。消耗谁：本次**第一个命中规则**的那一类附魔；
        /// 如果一类都没命中，就从检查顺序（热→冷→酸）里第一个有层的类型里扣 1。
        /// 催化层永远不在这里消耗（规则表 E：催化层数本身不消耗）。
        /// </summary>
        public bool ConsumeLayerOnActivate = true;

        /// <summary>
        /// 【正文没写这一步】要不要执行卡牌自己的「启动」文本。
        ///
        ///   v3 §四 的启动流程里**没有**"按素材的启动文本结算"这一步 —— 形态变化全由附魔规则表驱动。
        ///   卡表里的 startup 是 v2.1 的遗留写法（例如水的"消耗刀片所有热层数，额外产生一张水蒸气"），
        ///   v3 用规则表 + transitions 表达了同一件事。
        ///
        ///   默认 false = 严格按 v3 跑；打开就多跑一步（位置：附魔链之后、D-1 之前），
        ///   把卡表里那些 v2.1 文本也执行掉。RuleReport 会把它们列出来供策划决定删还是留。
        /// </summary>
        public bool ApplyCardStartupText = false;

        /// <summary>【正文没给】关卡目标分。§2.6 只说"达到目标分即胜利"，没写分值。0 = 不判定胜利。</summary>
        public int TargetScore = 0;

        /// <summary>【正文没给】直接得分法术"根据当前刀片冷层数直接加分"里每层算几分（结晶）。</summary>
        public int ScorePerLayerDefault = 1;

        /// <summary>【正文没给】卡牌文本写"获得一定分数"时的占位值（汞蒸气 / 熔融玻璃）。</summary>
        public int PlaceholderScorePerLayer = 5;

        /// <summary>固定随机种子："变为一张随机法术卡"要可复现，否则日志对不上。</summary>
        public int RandomSeed = 20261006;

        public TurnRules Clone() { return (TurnRules)MemberwiseClone(); }
    }

    /// <summary>
    /// 一关的状态（正文 §九「游戏状态管理」列的那些）。
    ///
    /// 【为什么单独一个类】
    ///   引擎的每个动作都在改同一组东西：分数、刀片、桌面、手牌、行动机会、空白卡计数。
    ///   把它们塞进参数列表会让每次调用都带一长串 ref，而且"这一关现在什么样"没有归属。
    ///   v3 的关卡是**独立**的（§2.6：献祭与消耗不影响下一关），所以一个 LevelRun = 一关。
    /// </summary>
    public class LevelRun
    {
        /// <summary>正文 §2.6：每回合初始 5 次行动机会（正文写死的规则，不是配置）</summary>
        public const int ActionPointsPerTurn = 5;

        /// <summary>正文 §2.6：每关 4 回合（正文写死的规则，不是配置）</summary>
        public const int TurnsPerLevel = 4;

        public BladeState blade = new BladeState("", "（未选刀片核心）", 0, 0);

        /// <summary>桌面素材（可被启动的目标）</summary>
        public List<MaterialState> table = new List<MaterialState>();

        /// <summary>手牌（卡名；素材与法术共用一副手牌，正文 §2.1）</summary>
        public List<string> hand = new List<string>();

        public int score;

        /// <summary>空白卡计数（正文 §2.1：需单独统计数量）</summary>
        public int blankCount;

        /// <summary>当前回合（从 1 开始）</summary>
        public int turnIndex = 1;

        public int actionPoints = ActionPointsPerTurn;

        /// <summary>本回合已经启动了几次（"实际最后一次启动"就是它==本回合总次数的那一次）</summary>
        public int startsThisTurn;

        public bool levelOver;
        public bool bursted;

        /// <summary>爆刀时的分数倍率（正文 §五：当前分数 ×2）</summary>
        public const int BurstMultiplier = 2;

        public string Describe()
        {
            return "第 " + turnIndex + "/" + TurnsPerLevel + " 回合　行动机会 " + actionPoints + "/" + ActionPointsPerTurn +
                   "　分数 " + score + "　刀片 " + blade.Describe() +
                   "　桌面 " + table.Count + " 张　手牌 " + hand.Count + " 张　空白卡 " + blankCount +
                   (levelOver ? "　【关卡已结束" + (bursted ? "·爆刀" : "") + "】" : "");
        }

        public MaterialState Find(string name)
        {
            for (int i = 0; i < table.Count; i++)
                if (table[i] != null && !table[i].removed && table[i].name == name) return table[i];
            return null;
        }
    }

    /// <summary>一次结算的结果：分数变化、生成的卡、刀片状态、全过程中文日志。</summary>
    public class TurnResult
    {
        public int turnIndex;

        /// <summary>本次启动的 V 得分（目标素材 V + 刀片 V）</summary>
        public int activationScore;

        /// <summary>规则文本带来的加分（献祭效果 / 直接得分法术 / 酸爆…）</summary>
        public int ruleScore;

        /// <summary>爆刀把当前分数翻倍带来的增量</summary>
        public int burstBonus;

        /// <summary>本次结算对关卡总分的净增量（三项相加）</summary>
        public int ScoreDelta { get { return activationScore + ruleScore + burstBonus; } }

        public bool bursted;

        /// <summary>爆刀用的倍率（1 = 没爆刀；正文默认 2，酸爆这类卡可以改成 3）</summary>
        public int burstMultiplier = 1;

        /// <summary>本次调用被拒绝（行动机会不足 / 目标已被移除 / 关卡已结束）</summary>
        public bool rejected;

        public List<ProducedCard> produced = new List<ProducedCard>();
        public List<string> log = new List<string>();

        /// <summary>本次结算里各类附魔被消耗的层数（"每消耗一层加分"要用）。</summary>
        public readonly Dictionary<LayerKind, int> consumed = new Dictionary<LayerKind, int>();

        /// <summary>⚠ 警告：规则解析出来了但执行不了、卡表缺卡、随机法术抽不出来之类。**别忽略这个列表**。</summary>
        public List<string> warnings = new List<string>();

        public BladeState blade;
        public LevelRun state;

        /// <summary>调用方最关心的那几张卡的快照文字（日志尾部用）</summary>
        public string summary = "";

        public int ProducedCount(string name)
        {
            int n = 0;
            for (int i = 0; i < produced.Count; i++)
                if (produced[i].name == name) n += produced[i].count;
            return n;
        }

        public bool HasProduced(string name) { return ProducedCount(name) > 0; }

        public ProduceKind KindOfProduced(string name)
        {
            for (int i = 0; i < produced.Count; i++)
                if (produced[i].name == name) return produced[i].kind;
            return ProduceKind.Auto;
        }

        public string ProducedText()
        {
            if (produced.Count == 0) return "（无产出）";
            List<string> parts = new List<string>();
            for (int i = 0; i < produced.Count; i++) parts.Add(produced[i].ToString());
            return string.Join("、", parts.ToArray());
        }

        public string ScoreLine()
        {
            string s = "启动 " + activationScore + " 分";
            if (ruleScore != 0) s += " + 规则 " + ruleScore + " 分";
            if (burstBonus != 0) s += " + 爆刀 ×" + burstMultiplier + " 追加 " + burstBonus + " 分";
            return s + " = " + ScoreDelta + " 分";
        }

        public string LogText() { return string.Join("\n", log.ToArray()); }

        public bool LogContains(string keyword)
        {
            for (int i = 0; i < log.Count; i++)
                if (log[i] != null && log[i].Contains(keyword)) return true;
            return false;
        }
    }

    /// <summary>一个回合的结果（回合开始 + 若干次启动 + 回合结束）。</summary>
    public class RoundResult
    {
        public int turnIndex;
        public List<TurnResult> starts = new List<TurnResult>();
        public TurnResult beginTurn;
        public TurnResult endTurn;

        public List<string> log = new List<string>();

        public int scoreBefore;
        public int scoreAfter;
        public int ScoreDelta { get { return scoreAfter - scoreBefore; } }

        public int StartCount { get { return starts.Count; } }

        public string LogText() { return string.Join("\n", log.ToArray()); }

        public bool LogContains(string keyword)
        {
            for (int i = 0; i < log.Count; i++)
                if (log[i] != null && log[i].Contains(keyword)) return true;
            return false;
        }
    }

    /// <summary>
    /// 启动结算引擎（正文 §四「启动结算流程」，顺序写死在这里）：
    ///
    ///   ① 消耗资源：1 行动机会 + 1 刀片H
    ///   ② 素材结算：按 热 → 冷 → 酸 查附魔规则表（催化只降阈值），每种只触发最高优先级一条
    ///        · 形态变化 → 旧卡移出桌面、新卡进手牌（D 重置为满）、本次不再 D-1、素材结算结束
    ///        · 溶解移除 → 旧卡移出桌面、素材结算结束
    ///        · 仅改属性（H-1 / H-2 / 刀片热+1）→ 继续检查下一个附魔
    ///        · 都没让目标消失 → D-1；D≤0 → D耗尽（法术 + 副产物进手牌；无产物则空白卡 +1）
    ///   ③ 结算收益：本次得分 = 目标素材V + 刀片V（目标已移除时用启动时的原始卡 V）
    ///   ④ 检查爆刀：H≤0 → 立即爆刀、关卡结束、**当前分数 ×2**
    ///   ⑤ 献祭吞噬：若本次是本回合最后一次启动，且目标结算后仍有 D 剩余 → 并入刀片
    ///        （移出桌面、刀片H += 卡H、刀片V += 卡V；带「献祭」标签的卡在此触发它的献祭效果）
    ///
    /// 【为什么顺序写死在这里】
    ///   顺序本身就是规则：②先变形再③算分，所以"用启动时的原始卡V"才有意义；
    ///   ④在③之后，所以爆刀的双倍能吃到本次得分。散在调用方就要每次重新推导一遍。
    ///   每一步都打中文日志，玩家说"我明明这么打却没过"时能一条条对回去。
    /// </summary>
    public class TurnEngine
    {
        public readonly TurnRules rules;

        private readonly ICardLookup lookup;
        private readonly RuleContext ctx;
        private readonly Random rng;

        private readonly Dictionary<string, RuleParseResult> startupCache = new Dictionary<string, RuleParseResult>();
        private readonly Dictionary<string, RuleParseResult> sacrificeCache = new Dictionary<string, RuleParseResult>();
        private readonly Dictionary<string, RuleParseResult> transitionCache = new Dictionary<string, RuleParseResult>();
        private readonly Dictionary<string, RuleParseResult> exhaustCache = new Dictionary<string, RuleParseResult>();

        /// <summary>挂在刀片上的"每次启动都生效"的被动（卡被吞噬后登记：铜的"其他素材D-2"、铁的"D-3"）。</summary>
        private readonly List<RuleClause> bladePassives = new List<RuleClause>();

        /// <summary>
        /// 与 <see cref="bladePassives"/> **一一对应**的来源记录（存档要存"这条被动是从哪张卡的哪句话来的"）。
        ///
        /// 【为什么不用另建一套查找】被动本体的 RuleClause 里只有一个 sentence，
        ///   没有"它出自哪张卡、哪个字段"—— 而读档时必须知道从哪段文本重新解析它。
        ///   登记的那一刻是唯一同时握着卡和句子的地方，顺手记一份最省事，
        ///   也保证了"导出多少条 = 现在生效多少条"（两个列表永远同增同清）。
        /// </summary>
        private readonly List<SaveBladePassive> bladePassiveSource = new List<SaveBladePassive>();

        public TurnEngine() : this(new TurnRules(), EmptyCardLookup.Instance) { }

        public TurnEngine(TurnRules rules) : this(rules, EmptyCardLookup.Instance) { }

        public TurnEngine(TurnRules rules, ICardLookup lookup)
        {
            this.rules = rules != null ? rules : new TurnRules();
            this.lookup = lookup != null ? lookup : EmptyCardLookup.Instance;
            this.ctx = new RuleContext(this.lookup);
            this.rng = new Random(this.rules.RandomSeed);
        }

        public int BladePassiveCount { get { return bladePassives.Count; } }

        public void ClearBladePassives()
        {
            bladePassives.Clear();
            bladePassiveSource.Clear();
        }

        /// <summary>
        /// 导出刀片被动（存档用）。返回的是**副本** —— 调用方拿去写盘，改它不影响场上的被动。
        /// </summary>
        public List<SaveBladePassive> ExportBladePassives()
        {
            return new List<SaveBladePassive>(bladePassiveSource);
        }

        /// <summary>
        /// 按存档里的记录重建刀片被动（读档用）。
        ///
        /// 【为什么按"原文重新解析"而不是把被动也序列化成数据】
        ///   被动的本体是一棵 RuleClause（条件 + 操作 + 层数类别 + 数值），
        ///   把它整个序列化就等于给规则解析结果定一份"对外格式" ——
        ///   以后解析器加一个字段，旧存档就会读回一棵**缺字段的**规则树，
        ///   而它不会报错，只会算错分。按保存下来的原文重新解析，
        ///   拿到的永远是"用当前解析器读这段文本"的结果，和当初登记时同一个口径。
        ///
        /// 【重建不出来怎么办】如实记进 <paramref name="error"/> 并**跳过那一条**，
        ///   不静默、也不让整个读档失败（少一条被动比读不进这一关要好，
        ///   而且调用方会把它打出来）。
        ///
        /// 返回重建成功的条数。
        /// </summary>
        public int ImportBladePassives(List<SaveBladePassive> records, out string error)
        {
            ClearBladePassives();
            error = "";
            if (records == null) return 0;

            int ok = 0;
            for (int i = 0; i < records.Count; i++)
            {
                SaveBladePassive rec = records[i];
                if (rec == null) continue;

                RuleParseResult pr = RuleText.ParseField(rec.text, RuleField.Sacrifice, rec.cardId, rec.cardName, ctx);

                RuleClause hit = null;
                for (int c = 0; c < pr.clauses.Count; c++)
                {
                    RuleClause cl = pr.clauses[c];
                    if (cl == null || cl.trigger != RuleTrigger.EachStartup) continue;
                    if (cl.sentence != rec.sentence) continue;
                    hit = cl;
                    break;
                }

                if (hit == null)
                {
                    error += "第 " + (i + 1) + " 条刀片被动没能重建（来源卡「" + rec.cardName + "」，句子「" +
                             rec.sentence + "」）—— 已跳过；";
                    continue;
                }

                bladePassives.Add(hit);
                bladePassiveSource.Add(rec);
                ok++;
            }
            return ok;
        }

        public ICardLookup Lookup { get { return lookup; } }

        // ══════════════════════════════════════════════════════════════
        //  关卡 / 回合框架
        // ══════════════════════════════════════════════════════════════

        /// <summary>开局选刀片核心：该卡 H 成为刀片初始 H、V 成为刀片初始 V，卡从手牌移除（§2.5）。</summary>
        public TurnResult BeginLevel(LevelRun state, MaterialState core)
        {
            TurnResult r = New(state);
            if (state == null || core == null)
            {
                Log(r, "【开局】没有刀片核心，关卡无法开始");
                r.rejected = true;
                return r;
            }

            state.blade = new BladeState(core.id, core.name, core.H, core.V);
            core.removed = true;
            RemoveFromTable(state, core);

            Log(r, "【开局】刀片核心：" + core.name + " → 刀片 H=" + core.H + " V=" + core.V +
                    "（该卡从手牌移除，本关不再参与出牌）");
            Log(r, "　" + state.Describe());
            return r;
        }

        /// <summary>回合开始：行动机会重置为 5，附魔层数不变（§三.1）。</summary>
        public TurnResult BeginTurn(LevelRun state)
        {
            TurnResult r = New(state);
            if (state == null) return r;

            state.actionPoints = LevelRun.ActionPointsPerTurn;
            state.startsThisTurn = 0;

            Log(r, "【回合 " + state.turnIndex + " 开始】行动机会重置为 " + state.actionPoints +
                    "；附魔层数不变：" + state.blade.layers.Describe());
            Log(r, "　" + state.Describe());
            return r;
        }

        /// <summary>回合结束：所有附魔层数 -1、归零消失；桌面素材保留（§三.6）。</summary>
        public TurnResult EndTurn(LevelRun state)
        {
            TurnResult r = New(state);
            if (state == null) return r;

            Log(r, "【回合 " + state.turnIndex + " 结束】");
            string before = state.blade.layers.Describe();
            state.blade.layers.DecayTurn();
            Log(r, "　附魔衰减（每类衰退层 -1、归零消失）：" + before + " → " + state.blade.layers.Describe());
            Log(r, "　（「不衰退」的层是授予时就带上的修饰，不参与这次衰减）");
            Log(r, "　冷热冲突已由「后附魔覆盖先附魔」在附魔时处理，回合末无需再抵消");
            Log(r, "　桌面素材保留：" + TableText(state));

            if (state.turnIndex >= LevelRun.TurnsPerLevel)
            {
                state.levelOver = true;
                Log(r, "　⚠ 第 " + state.turnIndex + " 回合结束，" + LevelRun.TurnsPerLevel + " 回合已用尽 → 关卡结束，结算最终分数 " + state.score);
            }
            else
            {
                state.turnIndex++;
            }
            return r;
        }

        /// <summary>
        /// 玩家主动结束关卡：按当前分数结算，**不触发爆刀双倍**（正文 §八）。
        /// </summary>
        public TurnResult EndLevelByChoice(LevelRun state)
        {
            TurnResult r = New(state);
            if (state == null) return r;

            state.levelOver = true;
            Log(r, "【主动结束关卡】按当前分数 " + state.score + " 结算（正文 §八：主动结束不触发爆刀双倍）");
            return r;
        }

        /// <summary>
        /// 跑一个完整回合：回合开始 → 按顺序启动 <paramref name="order"/> 里的目标 → 回合结束。
        ///
        /// 【"最后一次启动"是这一项决定的】
        ///   正文 §四 说明："最后一次启动不一定是第 5 次行动" —— 所以这里不做"第 5 次"的判断，
        ///   而是**把 order 的最后一项当作实际最后一次启动**（行动机会提前用尽的话，结束的那次就是最后一次）。
        ///   这样加行动机会的卡牌延后最后一次启动时，献祭判定自动跟着走。
        /// </summary>
        public RoundResult RunRound(LevelRun state, List<MaterialState> order)
        {
            RoundResult round = new RoundResult();
            if (state == null) { round.log.Add("（没有关卡状态）"); return round; }

            round.turnIndex = state.turnIndex;
            round.scoreBefore = state.score;

            round.beginTurn = BeginTurn(state);
            Append(round, round.beginTurn);

            List<MaterialState> list = order != null ? order : new List<MaterialState>();
            for (int i = 0; i < list.Count; i++)
            {
                bool isLast = (i == list.Count - 1);
                TurnResult st = StartBlade(state, list[i], isLast);
                round.starts.Add(st);
                Append(round, st);
                if (state.levelOver || st.rejected) break;   // 爆刀/关卡结束 或 启动被拒绝 → 这个回合到此为止
            }

            if (!state.levelOver)
            {
                round.endTurn = EndTurn(state);
                Append(round, round.endTurn);
            }
            else
            {
                round.log.Add("（关卡已结束，跳过回合结束的附魔衰减）");
            }

            round.scoreAfter = state.score;
            round.log.Add("【回合小结】启动 " + round.StartCount + " 次｜分数 " + round.scoreBefore + " → " + round.scoreAfter +
                          "（+" + round.ScoreDelta + "）｜" + state.Describe());
            return round;
        }

        // ══════════════════════════════════════════════════════════════
        //  启动结算（正文 §四）
        // ══════════════════════════════════════════════════════════════

        public TurnResult StartBlade(LevelRun state, MaterialState target, bool isLastStart)
        {
            TurnResult r = New(state);
            if (state == null) return r;

            Log(r, "【启动】目标 " + (target != null ? target.Describe() : "（无）") +
                    (isLastStart ? "　← 本回合最后一次启动（结算后判定献祭吞噬）" : ""));

            if (state.levelOver)
            {
                Log(r, "　关卡已结束，不能再启动");
                r.rejected = true;
                return r;
            }
            if (target == null || target.removed)
            {
                Log(r, "　目标不在桌面上（可能已被形态变化 / 溶解 / D耗尽 / 吞噬移走）→ 本次启动无效");
                r.rejected = true;
                return r;
            }
            if (state.actionPoints <= 0)
            {
                Log(r, "　行动机会已用尽（0/" + LevelRun.ActionPointsPerTurn + "）→ 不能启动");
                r.rejected = true;
                return r;
            }

            // ── ① 消耗资源 ────────────────────────────────────────────
            state.actionPoints--;
            state.startsThisTurn++;
            int hp = state.blade.Damage(1);
            Log(r, "① 消耗资源：行动机会 -1（剩 " + state.actionPoints + "/" + LevelRun.ActionPointsPerTurn + "）" +
                    "｜ 刀片 H-" + hp + " → H=" + state.blade.H);

            ApplyBladePassives(r, state);

            // ── ② 素材结算 ────────────────────────────────────────────
            Log(r, "② 素材结算：" + target.name + "（" + target.card.FormAndTags() + "）" +
                    "　H=" + target.H + " D=" + target.D + "/" + target.fullD + " V=" + target.V);

            int originalV = target.V;
            List<TransitionRule> reactions = TransitionsOf(target.card).transitions;
            bool removedByEnchant = ResolveEnchantChain(r, state, target, reactions);

            // ②b 卡牌自己的「启动」文本（v2.1 卡表遗留；v3 正文没有这一步，默认不跑 —— 见 TurnRules）
            if (rules.ApplyCardStartupText && !removedByEnchant && !target.removed)
                ExecuteCardStartupText(r, state, target);

            // ⑤ 目标还在 → D-1；D≤0 → D 耗尽
            if (!target.removed)
            {
                if (target.D > 0)
                {
                    target.D--;
                    Log(r, "　　D-1 → D=" + target.D + "/" + target.fullD);
                }

                if (target.D <= 0)
                {
                    Log(r, "　　D ≤ 0 → 触发「D耗尽」");
                    ExhaustTarget(r, state, target);
                }
            }

            // ── ③ 结算收益 ────────────────────────────────────────────
            int targetV = target.removed ? originalV : target.V;
            int bladeV = state.blade.V;
            r.activationScore = targetV + bladeV;
            state.score += r.activationScore;

            Log(r, "③ 结算收益：目标 V=" + targetV + (target.removed ? "（目标已移除 → 用启动时的原始卡 V=" + originalV + "）" : "") +
                    " + 刀片 V=" + bladeV + " = " + r.activationScore + " 分　→ 总分 " + state.score);

            // ── ④ 检查爆刀 ────────────────────────────────────────────
            if (state.blade.IsBursted) Burst(r, state);
            else Log(r, "④ 检查爆刀：H=" + state.blade.H + " > 0，未爆刀");

            // ── ⑤ 献祭吞噬 ────────────────────────────────────────────
            ResolveSacrifice(r, state, target, isLastStart);

            Finish(r, state);
            return r;
        }

        /// <summary>热 → 冷 → 酸 依次查规则表。返回"目标是否已被形态变化/溶解移除"。</summary>
        private bool ResolveEnchantChain(TurnResult r, LevelRun state, MaterialState target, List<TransitionRule> reactions)
        {
            LayerKind triggeredKind = LayerKind.Catalyst;
            bool anyTrigger = false;
            bool removed = false;

            for (int i = 0; i < EnchantRules.MaterialCheckOrder.Length; i++)
            {
                LayerKind kind = EnchantRules.MaterialCheckOrder[i];
                EnchantMatch m = EnchantRules.Check(kind, state.blade, target, reactions);

                if (!m.triggered)
                {
                    Log(r, "　· 检查" + LayerLedger.Name(kind) + "附魔：" + m.Describe());
                    continue;
                }

                if (!anyTrigger) triggeredKind = kind;   // 记住第一类命中的（"启动消耗附魔"扣它）
                anyTrigger = true;

                Log(r, "　· 检查" + LayerLedger.Name(kind) + "附魔：" + m.Describe() + "　（" + m.conditionText + "）");
                if (!string.IsNullOrEmpty(m.note)) Log(r, "　　" + m.note);
                if (m.productMissing)
                    Warn(r, "「" + target.name + "」被" + LayerLedger.Name(kind) + "附魔的" + m.ruleName +
                            "命中，但卡表没有给它产物 → 这次不会变形（请补连锁产物）");

                // 属性类效果（与形态变化可以同时发生：酸规则1 是"变溶液 + H-2"）
                ApplyEnchantAttributes(r, state, target, m);

                switch (m.outcome)
                {
                    case EnchantOutcome.FormChange:
                        DoFormChange(r, state, target, m);
                        Log(r, "　　（正文 A 节：形态变化 → 素材结算结束，不再检查后续附魔）");
                        removed = true;
                        break;

                    case EnchantOutcome.Dissolve:
                        target.removed = true;
                        RemoveFromTable(state, target);
                        Log(r, "　　溶解移除：旧卡移出桌面，不产生副产物 → 素材结算结束");
                        Log(r, "　　（正文 A 节：移除 → 不再检查后续附魔）");
                        removed = true;
                        break;

                    case EnchantOutcome.ByProduct:
                        AddToHand(r, state, m.productName, "析出副产物");
                        for (int e = 0; e < m.extraProducts.Count; e++) AddToHand(r, state, m.extraProducts[e], "析出副产物");
                        Log(r, "　　目标保留（析出只拿走副产物）→ 继续检查下一个附魔类型");
                        break;

                    default:
                        if (m.declaredNoChange)
                            Log(r, "　　该卡把这条反应声明为「无变化」→ 继续检查下一个附魔类型");
                        else
                            Log(r, "　　仅改属性、未发生形态变化 → 继续检查下一个附魔类型");
                        break;
                }

                if (removed) break;   // 形态变化 / 溶解 → 不再检查后续附魔（正文 A 节）
            }

            if (!anyTrigger)
                Log(r, "　（热/冷/酸三种附魔都没有触发规则）");

            // 规则表 F：启动消耗 1 层附魔（正文自相矛盾，见 TurnRules.ConsumeLayerOnActivate）
            ConsumeActivationLayer(r, state, anyTrigger, triggeredKind);

            return removed;
        }

        private void ApplyEnchantAttributes(TurnResult r, LevelRun state, MaterialState target, EnchantMatch m)
        {
            if (m.targetHDamage > 0)
            {
                int real = target.DamageH(m.targetHDamage);
                Log(r, "　　目标 H-" + real + " → H=" + target.H + "（" + m.ruleName + "）");
            }

            if (m.setTargetHZero)
            {
                target.H = 0;
                Log(r, "　　目标 H 归零（" + m.ruleName + "）");
            }

            if (m.setTargetDZero)
            {
                target.D = 0;
                Log(r, "　　目标 D 立即归零（" + m.ruleName + "）");
            }

            if (m.heatLayerGain > 0)
            {
                GrantLayers(r, state, LayerKind.Heat, m.heatLayerGain, false, m.ruleName);
            }

            if (m.needsPowderProduct && target.H <= 0)
            {
                Warn(r, "「" + target.name + "」H 归零，按规则表该变「粉末形态」，但卡表的 transitions 里没有粉末产物 → 只扣了 H");
                Log(r, "　　⚠ 卡表没给粉末形态产物，无法变形（已记进规则解析报告的数据缺口）");
            }
        }

        private void DoFormChange(TurnResult r, LevelRun state, MaterialState target, EnchantMatch m)
        {
            string oldName = target.name;
            target.removed = true;
            RemoveFromTable(state, target);

            Log(r, "　　形态变化：旧卡「" + oldName + "」移出桌面（本关不再回来）");

            if (string.IsNullOrEmpty(m.productName))
            {
                Warn(r, "「" + oldName + "」形态变化没有产物 → 这次启动什么都没得到");
                return;
            }

            AddToHand(r, state, m.productName, LayerLedger.Name(m.kind) + "附魔 → 形态变化");
            for (int i = 0; i < m.extraProducts.Count; i++)
                AddToHand(r, state, m.extraProducts[i], LayerLedger.Name(m.kind) + "附魔 → 形态变化");

            Log(r, "　　新卡进手牌，D 重置为满；本次启动不再对旧卡 D-1");
        }

        /// <summary>D 耗尽：产物进手牌；没有任何产物 → 空白卡 + 计数 1；原卡移出桌面。</summary>
        private void ExhaustTarget(TurnResult r, LevelRun state, MaterialState target)
        {
            RuleParseResult pr = ExhaustOf(target.card);

            if (pr.unrecognized.Count > 0)
                Warn(r, target.name + " 的「D耗尽」有 " + pr.unrecognized.Count + " 项没解析成功");

            int n = 0;
            int producedBefore = r.produced.Count;
            for (int c = 0; c < pr.clauses.Count; c++)
            {
                List<RuleAction> acts = pr.clauses[c].actions;
                for (int a = 0; a < acts.Count; a++)
                {
                    if (acts[a].op == RuleOp.None) continue;
                    ExecuteAction(r, state, target, acts[a]);
                    n++;
                }
            }

            if (r.produced.Count == producedBefore)
            {
                Log(r, "　　这张卡没有任何 D 耗尽产物 → 生成一张空白卡进手牌");
                AddToHand(r, state, "空白卡", "D耗尽（无产物）");
            }
            else if (n == 0)
            {
                Warn(r, target.name + " 的「D耗尽」没有可执行的条目");
            }

            target.removed = true;
            RemoveFromTable(state, target);
            Log(r, "　　原卡「" + target.name + "」移出桌面");
        }

        /// <summary>④ 爆刀：H≤0 → 立即爆刀、关卡结束、当前分数 ×2。</summary>
        private void Burst(TurnResult r, LevelRun state)
        {
            int mul = r.burstMultiplier > 1 ? r.burstMultiplier : LevelRun.BurstMultiplier;
            int before = state.score;
            int after = before * mul;

            state.score = after;
            state.levelOver = true;
            state.bursted = true;
            r.bursted = true;
            r.burstMultiplier = mul;
            r.burstBonus = after - before;

            Log(r, "④ 检查爆刀：H=" + state.blade.H + " ≤ 0 → **爆刀，关卡结束**，当前分数 ×" + mul);
            Log(r, "　　" + before + " × " + mul + " = " + after + " 分（正文 §五：刀片H≤0立即爆刀，当前分数×2）" +
                    (mul != LevelRun.BurstMultiplier ? "（倍率被卡牌改成 ×" + mul + "）" : ""));
        }

        /// <summary>⑤ 献祭吞噬：本回合最后一次启动 + 结算后仍有 D 剩余 → 并入刀片。</summary>
        private void ResolveSacrifice(TurnResult r, LevelRun state, MaterialState target, bool isLastStart)
        {
            Log(r, "⑤ 献祭吞噬判定：");

            if (state.levelOver)
            {
                Log(r, "　　关卡已结束（爆刀），本次不再吞噬");
                return;
            }
            if (!isLastStart)
            {
                Log(r, "　　本次不是本回合实际使用的最后一次启动 → 不吞噬");
                return;
            }
            if (target.removed)
            {
                Log(r, "　　目标已因形态变化 / 溶解 / D耗尽移出桌面 → 本次献祭不生效");
                return;
            }
            if (target.D <= 0)
            {
                Log(r, "　　目标 D 已耗尽 → 本次献祭不生效");
                return;
            }

            target.removed = true;
            RemoveFromTable(state, target);

            int h = target.H;
            int v = target.V;
            state.blade.Harden(h);
            state.blade.AddV(v);

            Log(r, "　　「" + target.name + "」并入刀片：移出桌面（本关不再回来）" +
                    "｜刀片 H+" + h + " → " + state.blade.H +
                    "｜刀片 V+" + v + " → " + state.blade.V);

            // 正文 §2.3：性质标签「献祭」= 被刀片吞噬时触发效果
            if (target.card != null && target.card.HasTag("献祭"))
            {
                RuleParseResult pr = SacrificeOf(target.card);
                if (pr.clauses.Count > 0)
                {
                    Log(r, "　　「" + target.name + "」带「献祭」标签 → 触发它的献祭效果");

                    for (int i = 0; i < pr.clauses.Count; i++)
                    {
                        RuleClause c = pr.clauses[i];

                        // "刀片每次启动，…"（玻璃改成这种写法了）：吞噬发生在本次启动的
                        // 计分之后，所以这条不能当场结算 —— 登记成刀片被动，从**下一次**启动开始生效。
                        if (c.trigger == RuleTrigger.EachStartup)
                        {
                            bladePassives.Add(c);

                            // 顺手记下"这条被动出自哪张卡的哪句话"（存档要存它，见 bladePassiveSource）
                            SaveBladePassive src = new SaveBladePassive();
                            src.cardId   = target.card != null ? target.card.id : "";
                            src.cardName = target.name;
                            src.text     = target.card != null ? target.card.sacrifice : "";
                            src.sentence = c.sentence;
                            bladePassiveSource.Add(src);

                            Log(r, "　　登记为刀片被动（从下一次启动开始，每次启动都生效）：" + c.sentence);
                            continue;
                        }

                        string why;
                        if (!ConditionHolds(c.condition, state, target, out why))
                        {
                            Log(r, "　　「" + c.sentence + "」条件不成立" + why + " → 跳过");
                            continue;
                        }

                        Log(r, "　　「" + c.sentence + "」");
                        for (int k = 0; k < c.actions.Count; k++) ExecuteAction(r, state, target, c.actions[k]);
                    }
                }
                else if (!string.IsNullOrEmpty(target.card.sacrifice))
                {
                    Warn(r, "「" + target.name + "」带「献祭」标签，但献祭文本没解析出任何效果：" + target.card.sacrifice);
                }
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  法术（§2.1：附魔法术 / 直接得分法术 / 影响素材法术）
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 打出一张法术：不消耗行动机会（§2.6）、无代价。
        /// 三类法术走同一条路：解析「需求」原文 → 执行 op（附魔 / 加分 / 影响素材）→ 检查爆刀。
        /// </summary>
        public TurnResult PlaySpell(LevelRun state, SpellSpec spell)
        {
            TurnResult r = New(state);
            if (state == null) return r;

            Log(r, "【法术】" + spell.name + (string.IsNullOrEmpty(spell.enchant) ? "" : "（附魔：" + spell.enchant + "）") +
                    "：结算「需求」原文（不消耗行动机会）");

            if (state.levelOver) { Log(r, "　关卡已结束，法术打不出去"); r.rejected = true; return r; }

            if (string.IsNullOrEmpty(spell.requirement))
            {
                Log(r, "　（这张法术没有写「需求」，无效果）");
                return r;
            }

            RuleParseResult pr = RuleText.ParseSpell(spell, ctx);
            for (int i = 0; i < pr.unrecognized.Count; i++)
                Warn(r, "法术「" + spell.name + "」有一句没解析成功：" + pr.unrecognized[i].sentence +
                        "（" + pr.unrecognized[i].reason + "）");

            ExecuteClauses(r, state, null, pr, "法术");

            // §五 / §八：刀片H≤0 立即爆刀（法术扣 H 也一样）
            Log(r, "④ 检查爆刀（法术同样适用：H≤0 立即爆刀）");
            if (state.blade.IsBursted) Burst(r, state);
            else Log(r, "　H=" + state.blade.H + " > 0，未爆刀");

            Finish(r, state);
            return r;
        }

        // ══════════════════════════════════════════════════════════════
        //  卡牌文本 op 的执行
        // ══════════════════════════════════════════════════════════════

        private void ExecuteCardStartupText(TurnResult r, LevelRun state, MaterialState target)
        {
            RuleParseResult pr = StartupOf(target.card);
            if (pr.clauses.Count == 0 && pr.unrecognized.Count == 0) return;

            Log(r, "　（卡牌自身的「启动」文本 —— v3 正文没有这一步，这里按 v2.1 卡表保留执行）");
            if (pr.unrecognized.Count > 0)
                Warn(r, target.name + " 的「启动」有 " + pr.unrecognized.Count + " 句没解析成功（见规则解析报告）—— 这几条不会生效");

            ExecuteClauses(r, state, target, pr, "启动文本");
        }

        private void ExecuteClauses(TurnResult r, LevelRun state, MaterialState self, RuleParseResult pr, string from)
        {
            for (int i = 0; i < pr.clauses.Count; i++)
            {
                RuleClause clause = pr.clauses[i];
                string why;
                if (!ConditionHolds(clause.condition, state, self, out why))
                {
                    Log(r, "　　「" + clause.sentence + "」条件不成立" + why + " → 跳过");
                    continue;
                }

                Log(r, "　　「" + clause.sentence + "」");
                for (int k = 0; k < clause.actions.Count; k++) ExecuteAction(r, state, self, clause.actions[k]);
            }
        }

        private void ApplyBladePassives(TurnResult r, LevelRun state)
        {
            if (bladePassives.Count == 0) return;

            Log(r, "　· 刀片被动（吞噬登记，共 " + bladePassives.Count + " 条）");
            for (int i = 0; i < bladePassives.Count; i++)
            {
                RuleClause c = bladePassives[i];
                Log(r, "　　「" + c.sentence + "」");
                for (int k = 0; k < c.actions.Count; k++) ExecuteAction(r, state, null, c.actions[k]);
            }
        }

        private void ExecuteAction(TurnResult r, LevelRun state, MaterialState self, RuleAction a)
        {
            BladeState blade = state.blade;

            switch (a.op)
            {
                case RuleOp.None:
                    Log(r, "　　（" + a.text + "）" + (string.IsNullOrEmpty(a.note) ? "" : " —— " + a.note));
                    break;

                // ── 得分 ──
                case RuleOp.AddScore:
                    r.ruleScore += a.amount;
                    state.score += a.amount;
                    Log(r, "　　固定加分：" + a.amount + " 分（「" + a.text + "」）→ 总分 " + state.score);
                    break;

                case RuleOp.ZeroScore:
                {
                    int cut = r.activationScore + r.ruleScore;
                    r.ruleScore -= cut;
                    state.score -= cut;
                    Log(r, "　　「本次启动得分为0」：本次得分清零（- " + cut + " 分）→ 总分 " + state.score);
                    break;
                }

                case RuleOp.MultiplyScore:
                {
                    int mul = a.amount > 1 ? a.amount : 2;
                    int gain = (r.activationScore + r.ruleScore) * (mul - 1);
                    r.ruleScore += gain;
                    state.score += gain;
                    Log(r, "　　得分倍率 ×" + mul + "：本次得分增加 " + gain + " 分 → 总分 " + state.score);
                    break;
                }

                case RuleOp.ScorePerLayer:
                case RuleOp.ScoreByLayerCount:
                {
                    int per = PerLayerAmount(a);
                    int layers = SumLayers(blade, a.layers);
                    int v = per * layers;
                    r.ruleScore += v;
                    state.score += v;
                    Log(r, "　　每层加分：" + DescribeLayers(blade, a.layers) + " × " + per + " = " + v + " 分" +
                            PlaceholderNote(a) + " → 总分 " + state.score);
                    break;
                }

                case RuleOp.ScorePerConsumedLayer:
                {
                    int per = PerLayerAmount(a);
                    int consumed = SumConsumed(r, a.layers);
                    int v = per * consumed;
                    r.ruleScore += v;
                    state.score += v;
                    Log(r, "　　每消耗一层加分：已消耗 " + consumed + " 层 × " + per + " = " + v + " 分" +
                            PlaceholderNote(a) + " → 总分 " + state.score);
                    break;
                }

                // ── 刀片 ──
                case RuleOp.BladeNoConsumeH:
                    Warn(r, "「" + a.text + "」在 v3 已失效：启动固定消耗 1 刀片H（§四.1），没有豁免机制");
                    Log(r, "　　⚠ 「不消耗刀片H」已失效（v3 改为启动固定扣 1 H，且扣在结算之前）→ 跳过");
                    break;

                case RuleOp.BladeDamageH:
                {
                    int amount = ResolveAmount(a, state, r);
                    int real = blade.Damage(amount);
                    Log(r, "　　扣刀片 H-" + real + AmountSourceNote(a, state, r) + " → H=" + blade.H);
                    if (blade.IsBursted) Log(r, "　　⚠ H ≤ 0，爆刀检查在收益结算之后（见 §四.4）");
                    break;
                }

                case RuleOp.BladeHardenH:
                {
                    int amount = ResolveAmount(a, state, r);
                    blade.Harden(amount);
                    Log(r, "　　加刀片 H+" + amount + " → H=" + blade.H);
                    break;
                }

                case RuleOp.BladeDamageOtherH:
                {
                    int n = 0;
                    for (int i = 0; i < state.table.Count; i++)
                    {
                        MaterialState m = state.table[i];
                        if (m == null || m == self || m.removed) continue;
                        int real = m.DamageH(a.amount);
                        n++;
                        Log(r, "　　" + m.name + " H-" + real + " → H=" + m.H);
                    }
                    if (n == 0) Log(r, "　　（没有其他素材，这条没有作用对象）");
                    break;
                }

                case RuleOp.OtherMaterialsDepleteD:
                {
                    int n = 0;
                    for (int i = 0; i < state.table.Count; i++)
                    {
                        MaterialState m = state.table[i];
                        if (m == null || m == self || m.removed) continue;
                        int before = m.D;
                        bool exhausted = m.Deplete(a.amount);
                        n++;
                        Log(r, "　　" + m.name + " D-" + a.amount + "：" + before + " → " + m.D +
                                (exhausted ? "（D≤0，等它自己启动时才会走 D耗尽）" : ""));
                    }
                    if (n == 0) Log(r, "　　（没有其他素材，这条没有作用对象）");
                    break;
                }

                // ── 层数 ──
                case RuleOp.AddLayer:
                    for (int i = 0; i < a.layers.Length; i++)
                        GrantLayers(r, state, a.layers[i], a.amount, a.permanent, "「" + a.text + "」");
                    break;

                case RuleOp.EnchantBlade:
                    for (int i = 0; i < a.layers.Length; i++)
                        GrantLayers(r, state, a.layers[i], a.amount > 0 ? a.amount : 1, false, "附魔");
                    break;

                case RuleOp.ConsumeLayer:
                    for (int i = 0; i < a.layers.Length; i++)
                    {
                        LayerKind k = a.layers[i];
                        int before = blade.layers.Count(k);
                        int taken = blade.layers.Consume(k, a.amount);
                        AddConsumed(r, k, taken);
                        Log(r, "　　消耗" + LayerLedger.Name(k) + "×" + a.amount + "：实际消耗 " + taken +
                                "（" + before + " → " + blade.layers.Count(k) + "）");
                    }
                    break;

                case RuleOp.ConsumeAllLayers:
                    for (int i = 0; i < a.layers.Length; i++)
                    {
                        LayerKind k = a.layers[i];
                        int before = blade.layers.Count(k);
                        int taken = blade.layers.ConsumeAll(k);
                        AddConsumed(r, k, taken);
                        Log(r, "　　消耗所有" + LayerLedger.Name(k) + "层数：实际消耗 " + taken + " 层（" + before + " → 0）");
                    }
                    break;

                case RuleOp.NoConsumeLayer:
                    Log(r, "　　（「" + a.text + "」：v3 里附魔层数只在回合末衰减，启动本来就不按这个条目标记处理）");
                    break;

                case RuleOp.DoubleLayers:
                    for (int i = 0; i < a.layers.Length; i++)
                    {
                        LayerKind k = a.layers[i];
                        int before = blade.layers.Count(k);
                        blade.layers.Double(k);
                        Log(r, "　　层数翻倍：" + LayerLedger.Name(k) + " " + before + " → " + blade.layers.Count(k) +
                                "（" + blade.layers.Describe() + "）");
                    }
                    break;

                // ── 产出 ──
                case RuleOp.ProduceCard:
                    AddToHand(r, state, a.cardName, self != null ? self.name : "规则", a.count);
                    break;

                case RuleOp.ProduceRandomSpell:
                {
                    List<string> names = lookup != null ? lookup.SpellNames() : null;
                    if (names == null || names.Count == 0)
                    {
                        Warn(r, "规则「" + a.text + "」要产出一张随机法术卡，但卡表里一个法术都没有");
                        Log(r, "　　⚠ 卡表里没有法术，随机法术卡产不出来");
                        break;
                    }
                    string pick = names[rng.Next(names.Count)];
                    AddToHand(r, state, pick, "随机法术（种子 " + rules.RandomSeed + "，卡表 " + names.Count + " 张法术）");
                    break;
                }

                // ── 附魔修正 / 爆刀倍率 ──
                case RuleOp.LowerEnchantThreshold:
                    Log(r, "　　降低附魔触发阈值：由催化层数在查表时生效（LayerLedger.LowerThreshold）");
                    break;

                case RuleOp.SetBurstMultiplier:
                    r.burstMultiplier = a.amount > 0 ? a.amount : 3;
                    Log(r, "　　⚠ 若本次结算爆刀，爆刀倍率按 ×" + r.burstMultiplier + " 计（正文默认 ×2；原文「" + a.text + "」）");
                    break;
            }
        }

        /// <summary>
        /// 授予附魔层：**先覆盖对方**（规则表 F：后附魔覆盖先附魔，清空对方全部层数），再加层。
        /// 所有加层都走这里 —— 冷热冲突只有这一处实现。
        /// </summary>
        private void GrantLayers(TurnResult r, LevelRun state, LayerKind kind, int amount, bool permanent, string from)
        {
            if (amount <= 0) return;

            BladeState blade = state.blade;
            LayerKind opposite = LayerKind.Catalyst;
            bool hasOpposite = false;

            if (kind == LayerKind.Heat) { opposite = LayerKind.Cold; hasOpposite = true; }
            else if (kind == LayerKind.Cold) { opposite = LayerKind.Heat; hasOpposite = true; }

            if (hasOpposite)
            {
                int cleared = blade.layers.Count(opposite);
                if (cleared > 0)
                {
                    blade.layers.ApplyConflict(kind);
                    Log(r, "　　冷热冲突：后附魔覆盖先附魔 → " + LayerLedger.Name(opposite) + "×" + cleared + " 被清空");
                }
                else
                {
                    blade.layers.ApplyConflict(kind);   // 幂等：没有对方层数时什么都不做
                }
            }

            int before = blade.layers.Count(kind);
            blade.layers.Add(kind, amount, permanent);
            Log(r, "　　" + from + "：附魔 " + LayerLedger.Name(kind) + " +" + amount + (permanent ? "（不衰退）" : "") +
                    "：" + before + " → " + blade.layers.Count(kind) + "（" + blade.layers.Describe() + "）");
        }

        /// <summary>规则表 F：启动消耗 1 层附魔（正文自相矛盾，默认按 F 实现）。</summary>
        private void ConsumeActivationLayer(TurnResult r, LevelRun state, bool anyTrigger, LayerKind triggeredKind)
        {
            if (!rules.ConsumeLayerOnActivate) return;

            LayerKind k = LayerKind.Catalyst;
            bool found = false;

            if (anyTrigger && triggeredKind != LayerKind.Catalyst && state.blade.layers.Has(triggeredKind))
            {
                k = triggeredKind;
                found = true;
            }
            else
            {
                for (int i = 0; i < EnchantRules.MaterialCheckOrder.Length; i++)
                {
                    if (state.blade.layers.Has(EnchantRules.MaterialCheckOrder[i]))
                    {
                        k = EnchantRules.MaterialCheckOrder[i];
                        found = true;
                        break;
                    }
                }
            }

            if (!found)
            {
                Log(r, "　· 启动消耗附魔：热/冷/酸都没有层数，没有可消耗的");
                return;
            }

            int taken = state.blade.layers.Consume(k, 1);
            Log(r, "　· 启动消耗附魔（规则表 F，与 §2.4 矛盾，见 TurnRules.ConsumeLayerOnActivate）：" +
                    LayerLedger.Name(k) + "-" + taken + " → " + state.blade.layers.Describe());
        }

        // ── 条件判定 ──────────────────────────────────────────────────

        private bool ConditionHolds(RuleCondition c, LevelRun state, MaterialState self, out string why)
        {
            why = "";
            if (c == null) return true;

            BladeState blade = state.blade;

            switch (c.kind)
            {
                case RuleConditionKind.Always:
                    return true;

                case RuleConditionKind.LayerAtLeast:
                {
                    if (c.layers.Length == 0) { why = "（条件里没有层数类别）"; return false; }
                    LayerKind k = c.layers[0];
                    int threshold = blade.layers.LowerThreshold(c.threshold);
                    int have = blade.layers.Count(k);
                    if (have >= threshold) return true;
                    why = "（" + LayerLedger.Name(k) + "×" + have + " < 阈值 " + threshold +
                          (threshold != c.threshold ? "，催化把 " + c.threshold + " 降到了 " + threshold : "") + "）";
                    return false;
                }

                case RuleConditionKind.HasEnchant:
                case RuleConditionKind.HasAnyLayer:
                {
                    for (int i = 0; i < c.layers.Length; i++)
                        if (blade.layers.Has(c.layers[i])) return true;
                    why = "（刀片上这些层数都是 0）";
                    return false;
                }

                case RuleConditionKind.HasTagOrForm:
                {
                    if (self == null || self.card == null) { why = "（没有可判定的素材）"; return false; }
                    if (self.card.HasTag(c.tag) || self.card.form == c.tag) return true;
                    why = "（这张素材的标签/形态是「" + self.card.FormAndTags() + "」，没有「" + c.tag + "」）";
                    return false;
                }

                case RuleConditionKind.EncounterCard:
                {
                    for (int i = 0; i < state.table.Count; i++)
                    {
                        MaterialState m = state.table[i];
                        if (m == null || m == self || m.removed) continue;
                        if (m.name == c.cardName) return true;
                    }
                    why = "（桌面上没有「" + c.cardName + "」）";
                    return false;
                }
            }
            return true;
        }

        // ── 产出 / 手牌 / 桌面 ────────────────────────────────────────

        /// <summary>产出一张（或 N 张）卡进手牌（素材与法术都进同一副手牌；空白卡额外计数）。</summary>
        private void AddToHand(TurnResult r, LevelRun state, string name, string from, int count = 1)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (count <= 0) count = 1;

            ProduceKind kind = ProduceKind.Auto;
            if (lookup != null)
            {
                if (lookup.Material(name) != null) kind = ProduceKind.Material;
                else if (lookup.IsSpell(name)) kind = ProduceKind.Spell;
            }

            bool blank = name == "空白卡";
            if (blank)
            {
                kind = ProduceKind.Blank;
                state.blankCount += count;
            }

            ProducedCard pc = new ProducedCard { name = name, kind = kind, count = count, from = from };
            r.produced.Add(pc);
            for (int i = 0; i < count; i++) state.hand.Add(name);

            Log(r, "　　进手牌：" + name + (count > 1 ? "×" + count : "") + "（" + ProduceKindName(kind) + "）" +
                    (string.IsNullOrEmpty(from) ? "" : " ← " + from) +
                    (blank ? "　【空白卡计数 " + state.blankCount + "】" : ""));

            if (kind == ProduceKind.Auto)
                Warn(r, "产出「" + name + "」在卡表里找不到（既不是素材也不是法术）—— 请策划补卡或改名字");
        }

        private static void RemoveFromTable(LevelRun state, MaterialState m)
        {
            if (state == null || m == null) return;
            state.table.Remove(m);
        }

        private static void Append(RoundResult round, TurnResult r)
        {
            if (r == null) return;
            for (int i = 0; i < r.log.Count; i++) round.log.Add(r.log[i]);
        }

        // ── 数值小工具 ────────────────────────────────────────────────

        private int PerLayerAmount(RuleAction a)
        {
            if (a.amount == RuleText.PlaceholderAmount) return rules.PlaceholderScorePerLayer;
            if (a.amount <= 0) return rules.ScorePerLayerDefault;
            return a.amount;
        }

        private int ResolveAmount(RuleAction a, LevelRun state, TurnResult r)
        {
            switch (a.amountSource)
            {
                case RuleAmountSource.CurrentLayers:
                    return SumLayers(state.blade, a.layers);
                case RuleAmountSource.ConsumedLayers:
                    return SumConsumed(r, a.layers);
            }
            if (a.amount == RuleText.PlaceholderAmount) return rules.PlaceholderScorePerLayer;
            return a.amount;
        }

        private int SumLayers(BladeState blade, LayerKind[] kinds)
        {
            int n = 0;
            if (kinds == null) return 0;
            for (int i = 0; i < kinds.Length; i++) n += blade.layers.Count(kinds[i]);
            return n;
        }

        private int SumConsumed(TurnResult r, LayerKind[] kinds)
        {
            int n = 0;
            if (kinds == null) return 0;
            for (int i = 0; i < kinds.Length; i++)
            {
                int v;
                if (r.consumed.TryGetValue(kinds[i], out v)) n += v;
            }
            return n;
        }

        private static void AddConsumed(TurnResult r, LayerKind kind, int n)
        {
            if (n <= 0) return;
            int v;
            r.consumed.TryGetValue(kind, out v);
            r.consumed[kind] = v + n;
        }

        private string DescribeLayers(BladeState blade, LayerKind[] kinds)
        {
            if (kinds == null || kinds.Length == 0) return "层数 0";
            List<string> parts = new List<string>();
            for (int i = 0; i < kinds.Length; i++)
                parts.Add(LayerLedger.Name(kinds[i]) + "×" + blade.layers.Count(kinds[i]));
            return string.Join("+", parts.ToArray());
        }

        private string PlaceholderNote(RuleAction a)
        {
            if (a.amount == RuleText.PlaceholderAmount)
                return "（正文写「一定分数」→ 占位值 " + rules.PlaceholderScorePerLayer + "，TurnRules.PlaceholderScorePerLayer）";
            if (!string.IsNullOrEmpty(a.note)) return "（" + a.note + "）";
            return "";
        }

        private string AmountSourceNote(RuleAction a, LevelRun state, TurnResult r)
        {
            if (a.amountSource == RuleAmountSource.CurrentLayers)
                return "（= 当前" + RuleCondition.KindsText(a.layers) + "层数 " + SumLayers(state.blade, a.layers) + "）";
            if (a.amountSource == RuleAmountSource.ConsumedLayers)
                return "（= 本句已消耗的" + RuleCondition.KindsText(a.layers) + "层数 " + SumConsumed(r, a.layers) + "）";
            return "";
        }

        private static string ProduceKindName(ProduceKind k)
        {
            switch (k)
            {
                case ProduceKind.Material: return "素材";
                case ProduceKind.Spell:    return "法术";
                case ProduceKind.Blank:    return "空白卡";
            }
            return "？卡表里没有这张卡";
        }

        private static string TableText(LevelRun state)
        {
            if (state.table.Count == 0) return "（空）";
            List<string> parts = new List<string>();
            for (int i = 0; i < state.table.Count; i++) parts.Add(state.table[i].Describe());
            return string.Join("；", parts.ToArray());
        }

        // ── 解析缓存 ──────────────────────────────────────────────────

        private static string Key(Ingredient card, RuleField f)
        {
            if (card == null) return f + "|（空卡）";
            string id = !string.IsNullOrEmpty(card.id) ? card.id : card.name;
            return f + "|" + id + "|" + card.name;
        }

        public RuleParseResult StartupOf(Ingredient card)
        {
            string k = Key(card, RuleField.Startup);
            RuleParseResult pr;
            if (startupCache.TryGetValue(k, out pr)) return pr;

            pr = RuleText.ParseField(card != null ? card.startup : "", RuleField.Startup,
                card != null ? card.id : "", card != null ? card.name : "", ctx);
            startupCache[k] = pr;
            return pr;
        }

        public RuleParseResult SacrificeOf(Ingredient card)
        {
            string k = Key(card, RuleField.Sacrifice);
            RuleParseResult pr;
            if (sacrificeCache.TryGetValue(k, out pr)) return pr;

            pr = RuleText.ParseField(card != null ? card.sacrifice : "", RuleField.Sacrifice,
                card != null ? card.id : "", card != null ? card.name : "", ctx);
            sacrificeCache[k] = pr;
            return pr;
        }

        public RuleParseResult TransitionsOf(Ingredient card)
        {
            string k = Key(card, RuleField.Transition);
            RuleParseResult pr;
            if (transitionCache.TryGetValue(k, out pr)) return pr;

            pr = RuleText.ParseTransitions(card != null ? card.transitions : null,
                card != null ? card.id : "", card != null ? card.name : "", ctx);
            transitionCache[k] = pr;
            return pr;
        }

        public RuleParseResult ExhaustOf(Ingredient card)
        {
            string k = Key(card, RuleField.Exhaust);
            RuleParseResult pr;
            if (exhaustCache.TryGetValue(k, out pr)) return pr;

            pr = RuleText.ParseExhaust(card != null ? card.exhaust : null,
                card != null ? card.id : "", card != null ? card.name : "", ctx);
            exhaustCache[k] = pr;
            return pr;
        }

        // ── 杂项 ──────────────────────────────────────────────────────

        private TurnResult New(LevelRun state)
        {
            TurnResult r = new TurnResult();
            r.state = state;
            r.blade = state != null ? state.blade : null;
            r.turnIndex = state != null ? state.turnIndex : 0;
            return r;
        }

        private void Finish(TurnResult r, LevelRun state)
        {
            if (rules.TargetScore > 0 && state.score >= rules.TargetScore && !state.levelOver)
            {
                state.levelOver = true;
                Log(r, "★ 达到目标分 " + rules.TargetScore + "（当前 " + state.score + "）→ 关卡结束");
            }

            r.summary = state.Describe();
            Log(r, "—— " + r.ScoreLine() + " ｜ 产出 " + r.ProducedText() + " ｜ " + state.Describe());
        }

        private static void Log(TurnResult r, string line) { r.log.Add(line); }

        private static void Warn(TurnResult r, string line)
        {
            r.warnings.Add(line);
            r.log.Add("　⚠ " + line);
        }
    }
}
