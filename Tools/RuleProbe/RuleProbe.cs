using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GameJam.Data;
using GameJam.Rules;

namespace GameJam.Tools
{
    /// <summary>
    /// 规则内核的离线断言 —— `powershell.exe -File Tools/RuleProbe/run.ps1` 直接跑，不用开 Unity。
    ///
    /// 【为什么断言写在代码里而不是文档里】
    ///   规则最容易错的地方不是"想不到"，是**改动时的连带**：
    ///   比如把冷热冲突从"成对抵消"改成"后附魔覆盖先附魔"，层数规则的断言全绿但启动流程会变。
    ///   这里一条规则一个断言，改完跑一次就知道有没有踩坏别的。
    ///
    /// 【四段】
    ///   一、层数语义（LayerLedger / BladeState）—— 纯状态
    ///   二、中文解析（RuleText）—— 一个小句能不能认出来
    ///   三、附魔规则表（EnchantRules）—— 热/冷/酸 17 条规则命中的是哪一条
    ///   四、端到端场景 —— **用 cards_v21.json 的真实文案**跑启动流程的六步，
    ///       每个场景一行中文结果；失败时能看到是"哪条规则没生效"。
    ///   最后整份打印「规则解析报告」（未识别清单 + 规则表接不上 + 正文矛盾）。
    ///
    /// 【数值从哪来】
    ///   正文 §2.2 写明 V 是枚举（极低=1/低=2/中=3/高=5/极高=8），但"暂时还没有设计具体数值"，
    ///   卡表里因此没有 h/d/v。探针支持卡表里的可选 h/d/v 字段，缺的时候用**测试夹具值**
    ///   （见 FixtureValues），只为把"得分 = 目标V + 刀片V"这条公式验证出来。
    /// </summary>
    public static class RuleProbe
    {
        private static int passed;
        private static int failed;
        private static readonly List<string> failures = new List<string>();

        public static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("=== 规则内核离线校验 ===");

            // ── 一、层数语义 ──────────────────────────────────────────
            Layers_AddAndCount();
            Layers_ConsumePrefersDecaying();
            Layers_ConsumeAllTakesPermanentToo();
            Layers_DecayOnlyDecaying();
            Layers_PermanentNeverDecays();
            Layers_Double();
            Layers_OverwriteConflict();
            Layers_CatalystThreshold();
            Layers_Describe();

            // ── 二、中文解析 ──────────────────────────────────────────
            Console.WriteLine();
            Console.WriteLine("=== 中文规则解析（单元）===");
            Parser_SplitSentencesAndKinds();
            Parser_ConditionForms();
            Parser_OpForms();
            Values_VGradeMapping();

            // ── 三、四 ────────────────────────────────────────────────
            JsonCards cards = JsonCards.Load(args);

            Console.WriteLine();
            Console.WriteLine("=== 附魔规则表（EnchantRules）===");
            if (cards == null)
            {
                Fail("附魔规则表", "读不到 cards_v21.json，规则表与端到端场景全部没跑");
            }
            else
            {
                Console.WriteLine("  素材 " + cards.materials.Count + " 张 · 法术 " + cards.spells.Count + " 张");
                Table_MatchCases(cards);

                Console.WriteLine();
                Console.WriteLine("=== 端到端场景（cards_v21.json 真实文案 + v3 启动流程）===");
                string sampleLog = Scenario1_Burn(cards);
                Scenario2_LiquidToGas(cards);
                Scenario3_CatalystThreshold(cards);
                Scenario4_StepOrderAndSacrifice(cards);
                Scenario5_Dissolve(cards);
                Scenario6_ExhaustToHand(cards);
                Scenario7_SacrificeTiming(cards);
                Scenario8_Burst(cards);
                Scenario9_ScoreFormula(cards);
                Scenario10_HeatColdOverwrite(cards);
                Scenario11_ExplodeUnreachable(cards);
                Report_NoSilentLoss(cards);

                Console.WriteLine();
                Console.WriteLine("=== 日志样例（场景1 燃烧 完整启动，逐步可解释）===");
                Console.WriteLine(sampleLog);
            }

            Console.WriteLine();
            Console.WriteLine("通过 " + passed + " 项，失败 " + failed + " 项");
            for (int i = 0; i < failures.Count; i++) Console.WriteLine("  ✗ " + failures[i]);
            return failed == 0 ? 0 : 1;
        }

        // ── 断言 / 打印小工具 ─────────────────────────────────────────

        private static void Check(string what, int expect, int actual)
        {
            if (expect == actual) { passed++; return; }
            failed++;
            failures.Add(what + "：期望 " + expect + "，实际 " + actual);
        }

        private static void Check(string what, string expect, string actual)
        {
            if (expect == actual) { passed++; return; }
            failed++;
            failures.Add(what + "：期望「" + expect + "」，实际「" + actual + "」");
        }

        private static void Check(string what, bool expect, bool actual)
        {
            if (expect == actual) { passed++; return; }
            failed++;
            failures.Add(what + "：期望 " + (expect ? "是" : "否") + "，实际 " + (actual ? "是" : "否"));
        }

        private static void CheckKind(string what, ProduceKind expect, ProduceKind actual)
        {
            if (expect == actual) { passed++; return; }
            failed++;
            failures.Add(what + "：期望产出的类别是 " + expect + "，实际 " + actual);
        }

        private static void CheckLayer(string what, LayerKind expect, LayerKind actual)
        {
            if (expect == actual) { passed++; return; }
            failed++;
            failures.Add(what + "：期望层数类别是「" + LayerLedger.Name(expect) + "」，实际「" + LayerLedger.Name(actual) + "」");
        }

        private static void CheckCond(string what, RuleConditionKind expect, RuleConditionKind actual)
        {
            if (expect == actual) { passed++; return; }
            failed++;
            failures.Add(what + "：期望条件种类 " + expect + "，实际 " + actual);
        }

        private static void CheckOutcome(string what, EnchantOutcome expect, EnchantOutcome actual)
        {
            if (expect == actual) { passed++; return; }
            failed++;
            failures.Add(what + "：期望结算类型是 " + EnchantRules.OutcomeName(expect) + "，实际 " + EnchantRules.OutcomeName(actual));
        }

        /// <summary>日志里必须出现某个关键词 —— 用来验证"这条规则真的走过"。</summary>
        private static void CheckLog(string what, TurnResult r, string keyword)
        {
            Check(what + "（日志里应出现「" + keyword + "」）", true, r.LogContains(keyword));
        }

        private static void CheckLog(string what, RoundResult r, string keyword)
        {
            Check(what + "（日志里应出现「" + keyword + "」）", true, r.LogContains(keyword));
        }

        private static void Fail(string what, string why)
        {
            failed++;
            failures.Add(what + "：" + why);
        }

        private static int ScenarioStart() { return failed; }

        private static void ScenarioEnd(string name, int failBefore, string detail)
        {
            int n = failed - failBefore;
            if (n == 0) Console.WriteLine("  ✓ " + name + "：" + detail);
            else Console.WriteLine("  ✗ " + name + "：" + detail + "　（" + n + " 项断言没过，见末尾清单）");
        }

        /// <summary>场景默认配置：只改正文没写的东西，规则一律按默认（ConsumeLayerOnActivate = true）。</summary>
        private static TurnRules ProbeRules()
        {
            return new TurnRules();
        }

        // ══════════════════════════════════════════════════════════════
        //  一、层数语义
        // ══════════════════════════════════════════════════════════════

        private static void Layers_AddAndCount()
        {
            LayerLedger l = new LayerLedger();
            l.Add(LayerKind.Heat, 2);
            l.Add(LayerKind.Heat, 1, true);
            Check("加层：总层数", 3, l.Count(LayerKind.Heat));
            Check("加层：不衰退层数", 1, l.Stack(LayerKind.Heat).permanent);
            Check("加层：有附魔", true, l.Has(LayerKind.Heat));
            Check("加层：没碰过的类别是 0", 0, l.Count(LayerKind.Acid));
        }

        private static void Layers_ConsumePrefersDecaying()
        {
            LayerLedger l = new LayerLedger();
            l.Add(LayerKind.Heat, 2);          // 2 层会衰退
            l.Add(LayerKind.Heat, 2, true);    // 2 层不衰退
            int taken = l.Consume(LayerKind.Heat, 1);

            Check("部分消耗：消耗掉 1 层", 1, taken);
            Check("部分消耗：先吃衰退层", 1, l.Stack(LayerKind.Heat).decaying);
            Check("部分消耗：不衰退层不动", 2, l.Stack(LayerKind.Heat).permanent);
        }

        private static void Layers_ConsumeAllTakesPermanentToo()
        {
            LayerLedger l = new LayerLedger();
            l.Add(LayerKind.Cold, 1);
            l.Add(LayerKind.Cold, 2, true);

            int taken = l.ConsumeAll(LayerKind.Cold);
            Check("整体消耗：返回总层数", 3, taken);
            Check("整体消耗：不衰退层也被清空", 0, l.Count(LayerKind.Cold));
        }

        private static void Layers_DecayOnlyDecaying()
        {
            LayerLedger l = new LayerLedger();
            l.Add(LayerKind.Heat, 3);
            l.Add(LayerKind.Heat, 2, true);

            l.DecayTurn();
            Check("回合衰减：衰退层 -1", 2, l.Stack(LayerKind.Heat).decaying);
            Check("回合衰减：不衰退层不变", 2, l.Stack(LayerKind.Heat).permanent);
        }

        private static void Layers_PermanentNeverDecays()
        {
            LayerLedger l = new LayerLedger();
            l.Add(LayerKind.Acid, 1, true);

            for (int i = 0; i < 10; i++) l.DecayTurn();
            Check("不衰退：跑十个回合还在", 1, l.Count(LayerKind.Acid));
        }

        private static void Layers_Double()
        {
            LayerLedger l = new LayerLedger();
            l.Add(LayerKind.Acid, 2);
            l.Add(LayerKind.Acid, 1, true);

            l.Double(LayerKind.Acid);
            Check("翻倍：衰退层 ×2", 4, l.Stack(LayerKind.Acid).decaying);
            Check("翻倍：不衰退层 ×2", 2, l.Stack(LayerKind.Acid).permanent);
        }

        /// <summary>规则表 F：冷热冲突是"后附魔覆盖先附魔，清空对方全部层数"，不是成对抵消。</summary>
        private static void Layers_OverwriteConflict()
        {
            LayerLedger l = new LayerLedger();
            l.Add(LayerKind.Heat, 3);
            l.Add(LayerKind.Heat, 2, true);   // 不衰退的热

            l.ApplyConflict(LayerKind.Cold);  // 后附魔的是冷 → 热整类清零
            Check("冷热覆盖：后附魔的冷把热清空（含不衰退层）", 0, l.Count(LayerKind.Heat));

            l.Add(LayerKind.Cold, 2);
            l.ApplyConflict(LayerKind.Heat);  // 后附魔的是热 → 冷整类清零
            Check("冷热覆盖：反向也一样（冷清空）", 0, l.Count(LayerKind.Cold));

            l.Add(LayerKind.Acid, 2);
            l.ApplyConflict(LayerKind.Catalyst);   // 酸催共存，谁都不清
            Check("酸催共存：催化不冲突，酸还在", 2, l.Count(LayerKind.Acid));
        }

        /// <summary>正文 §五 / 规则表 E：催≥1 → 阈值 -1（热≥2→热≥1、冷≥3→冷≥2、酸≥2→酸≥1）；催≥3 再 -1。</summary>
        private static void Layers_CatalystThreshold()
        {
            LayerLedger l = new LayerLedger();
            Check("催化阈值：没有催化时热阈值不变（2）", 2, l.LowerThreshold(2));
            Check("催化阈值：没有催化时热阈值不变（3）", 3, l.LowerThreshold(3));

            l.Add(LayerKind.Catalyst, 1);
            Check("催化阈值：催1 → 热≥2 变 1", 1, l.LowerThreshold(2));
            Check("催化阈值：催1 → 热≥3 变 2", 2, l.LowerThreshold(3));
            Check("催化阈值：催1 → 冷≥3 变 2", 2, l.LowerThreshold(3));
            Check("催化阈值：阈值不会低于 1", 1, l.LowerThreshold(1));

            l.Add(LayerKind.Catalyst, 2);   // 催化 3 层
            Check("催化阈值：催3 → 热≥3 变 1（再 -1）", 1, l.LowerThreshold(3));
        }

        private static void Layers_Describe()
        {
            LayerLedger l = new LayerLedger();
            l.Add(LayerKind.Heat, 2);
            l.Add(LayerKind.Heat, 1, true);
            Check("摘要文字", "热×3（含1不衰退）", l.Describe());
        }

        // ══════════════════════════════════════════════════════════════
        //  二、中文解析 / 数值
        // ══════════════════════════════════════════════════════════════

        private static void Parser_SplitSentencesAndKinds()
        {
            List<string> sentences = RuleText.SplitSentences(
                "不消耗刀片H。消耗刀片所有热层数，额外产生一张水蒸气；消耗刀片所有冷层数，额外产生一张冰。");
            Check("切句：；和。都切", 3, sentences.Count);

            List<LayerKind> kinds = RuleText.ParseKinds("冷/热/酸");
            Check("层数类别：冷/热/酸 认出 3 类", 3, kinds.Count);
            CheckLayer("层数类别：第一类是冷", LayerKind.Cold, kinds[0]);
            CheckLayer("层数类别：催化不会被漏掉", LayerKind.Catalyst, RuleText.ParseKinds("不衰退的催化")[0]);
        }

        private static void Parser_ConditionForms()
        {
            RuleCondition c;
            string why;

            Check("条件：刀片热层数≥3", true, RuleText.TryParseCondition("刀片热层数≥3", out c, out why));
            Check("条件：阈值读成 3", 3, c.threshold);
            CheckLayer("条件：类别是热", LayerKind.Heat, c.layers[0]);

            Check("条件：有热/酸附魔（斜杠并列展开）", true, RuleText.TryParseCondition("刀片有热/酸附魔", out c, out why));
            Check("条件：附魔类别有 2 类", 2, c.layers.Length);

            Check("条件：若有冷/热/酸 → 任意层数", true, RuleText.TryParseCondition("有冷/热/酸", out c, out why));
            CheckCond("条件：任意层数种类正确", RuleConditionKind.HasAnyLayer, c.kind);

            Check("条件：遇到水时 → 遇到某张卡", true, RuleText.TryParseCondition("遇到水时", out c, out why));
            Check("条件：遇到的卡是水", "水", c.cardName);

            Check("条件：每当/每次 → 认不出来（并说明原因）", false, RuleText.TryParseCondition("每次获得冷层数时", out c, out why));
            Check("条件：反应式的失败原因写清了", true, why.Contains("反应式触发"));
        }

        private static void Parser_OpForms()
        {
            RuleParseResult pr = RuleText.ParseField(
                "不消耗刀片H。消耗刀片所有热层数，额外产生一张水蒸气；消耗刀片所有冷层数，额外产生一张冰；消耗刀片所有酸层数，额外产生一张空白卡。",
                RuleField.Startup, "water", "水", null);

            Check("op：4 句全部解析成功", 0, pr.unrecognized.Count);
            Check("op：切成 4 句", 4, pr.sentenceCount);
            Check("op：句子账目对得上", true, pr.Balanced);

            bool hasNoConsumeH = false, hasConsumeAllHeat = false, hasProduceBlank = false;
            for (int i = 0; i < pr.clauses.Count; i++)
                for (int k = 0; k < pr.clauses[i].actions.Count; k++)
                {
                    RuleAction a = pr.clauses[i].actions[k];
                    if (a.op == RuleOp.BladeNoConsumeH) hasNoConsumeH = true;
                    if (a.op == RuleOp.ConsumeAllLayers && a.HasKind(LayerKind.Heat)) hasConsumeAllHeat = true;
                    if (a.op == RuleOp.ProduceCard && a.produce == ProduceKind.Blank) hasProduceBlank = true;
                }

            Check("op：认出不消耗刀片H（v3 已失效，但解析要认得）", true, hasNoConsumeH);
            Check("op：认出消耗所有热层数", true, hasConsumeAllHeat);
            Check("op：认出空白卡产出", true, hasProduceBlank);

            RuleParseResult pr2 = RuleText.ParseField(
                "若刀片有冷层数，消耗所有冷层数，以及和冷层数相当的H，每消耗一层，获得5分；不消耗刀片H，且刀片H+1。",
                RuleField.Startup, "molten_iron", "熔融铁", null);

            Check("op：熔融铁这句全部解析成功", 0, pr2.unrecognized.Count);

            bool damageFromConsumed = false, scorePerConsumed = false, harden = false;
            for (int i = 0; i < pr2.clauses.Count; i++)
                for (int k = 0; k < pr2.clauses[i].actions.Count; k++)
                {
                    RuleAction a = pr2.clauses[i].actions[k];
                    if (a.op == RuleOp.BladeDamageH && a.amountSource == RuleAmountSource.ConsumedLayers) damageFromConsumed = true;
                    if (a.op == RuleOp.ScorePerConsumedLayer && a.amount == 5) scorePerConsumed = true;
                    if (a.op == RuleOp.BladeHardenH && a.amount == 1) harden = true;
                }

            Check("op：扣H 的来源=已消耗层数", true, damageFromConsumed);
            Check("op：每消耗一层 5 分", true, scorePerConsumed);
            Check("op：刀片H+1", true, harden);
        }

        /// <summary>正文 §2.2：V 枚举 极低=1、低=2、中=3、高=5、极高=8。</summary>
        private static void Values_VGradeMapping()
        {
            Check("V档位：极低=1", 1, CardValues.VGradeValue("极低"));
            Check("V档位：低=2", 2, CardValues.VGradeValue("低"));
            Check("V档位：中=3", 3, CardValues.VGradeValue("中"));
            Check("V档位：高=5", 5, CardValues.VGradeValue("高"));
            Check("V档位：极高=8", 8, CardValues.VGradeValue("极高"));
            Check("V档位：卡表直接写数字也认", 7, CardValues.ParseV("7"));
        }

        // ══════════════════════════════════════════════════════════════
        //  三、附魔规则表
        // ══════════════════════════════════════════════════════════════

        private static void Table_MatchCases(JsonCards cards)
        {
            int mark = ScenarioStart();

            // 热≥2 + 液体 → 优先级3（不是优先级4的"遇热"）
            BladeState b = new BladeState("b", "刀片", 10, 0);
            b.layers.Add(LayerKind.Heat, 2);
            EnchantMatch m = MatchCase(cards, b, cards.Material("水"), "水", LayerKind.Heat);
            Check("规则表：热≥2 + 液体 命中优先级3", 3, m.priority);
            Check("规则表：水的气态产物是水蒸气", "水蒸气", m.productName);
            CheckOutcome("规则表：结果是形态变化", EnchantOutcome.FormChange, m.outcome);

            // 热≤1 + 液体 → 什么都不命中（阈值要真的挡得住）
            BladeState b1 = new BladeState("b", "刀片", 10, 0);
            b1.layers.Add(LayerKind.Heat, 1);
            EnchantMatch m1 = MatchCase(cards, b1, cards.Material("水"), "水", LayerKind.Heat);
            Check("规则表：热=1 + 液体 不命中（阈值2）", false, m1.triggered);

            // 酸≥1 + 金属 → 优先级1，H-2，变溶液
            BladeState ba = new BladeState("b", "刀片", 10, 0);
            ba.layers.Add(LayerKind.Acid, 1);
            EnchantMatch ma = MatchCase(cards, ba, cards.Material("铜"), "铜", LayerKind.Acid);
            Check("规则表：酸≥1 + 金属 命中优先级1", 1, ma.priority);
            Check("规则表：金属→溶液 目标 H-2", 2, ma.targetHDamage);
            Check("规则表：铜变成铜溶液", "铜溶液", ma.productName);

            // 酸≥2 + 固体（非金属/非可溶）→ 优先级3，H-1，仅改属性
            BladeState bb = new BladeState("b", "刀片", 10, 0);
            bb.layers.Add(LayerKind.Acid, 2);
            EnchantMatch mb = MatchCase(cards, bb, cards.Material("玻璃"), "玻璃", LayerKind.Acid);
            Check("规则表：酸≥2 + 固体 命中优先级3", 3, mb.priority);
            Check("规则表：固体 H-1", 1, mb.targetHDamage);
            CheckOutcome("规则表：仅改属性（继续检查下一类）", EnchantOutcome.AttributeOnly, mb.outcome);

            // 酸≥1 + 粉末 → 溶解移除
            BladeState bc = new BladeState("b", "刀片", 10, 0);
            bc.layers.Add(LayerKind.Acid, 1);
            EnchantMatch mc = MatchCase(cards, bc, cards.Material("硫磺粉"), "硫磺粉", LayerKind.Acid);
            CheckOutcome("规则表：酸≥1 + 粉末 命中溶解移除", EnchantOutcome.Dissolve, mc.outcome);

            // 冷=2 + 溶液 → 优先级3 析出（冷≥3 会被"液体→固态"抢先，这里专门验 2 这一档）
            BladeState bd = new BladeState("b", "刀片", 10, 0);
            bd.layers.Add(LayerKind.Cold, 2);
            EnchantMatch md = MatchCase(cards, bd, cards.Material("铜溶液"), "铜溶液", LayerKind.Cold);
            Check("规则表：冷=2 + 溶液 命中优先级3（析出）", 3, md.priority);
            CheckOutcome("规则表：析出结果是副产物", EnchantOutcome.ByProduct, md.outcome);
            Check("规则表：铜溶液析出铜", "铜", md.productName);

            // 冷≥3 + 液体 → 优先级2（溶液也走这条，按优先级实现）
            BladeState be = new BladeState("b", "刀片", 10, 0);
            be.layers.Add(LayerKind.Cold, 3);
            EnchantMatch me = MatchCase(cards, be, cards.Material("铜溶液"), "铜溶液", LayerKind.Cold);
            Check("规则表：冷≥3 + 液体 抢先于溶液析出（优先级2）", 2, me.priority);

            // 粉末 + 易燃 + 热1：按优先级1走燃烧（爆炸规则永远轮不到）—— 正文矛盾，见 Conflicts()
            //  卡表里当前没有"粉末+易燃"的卡，所以用一张探针合成卡来验证规则表本身
            BladeState bf = new BladeState("b", "刀片", 10, 0);
            bf.layers.Add(LayerKind.Heat, 1);
            Ingredient powderCard = cards.Synthetic("粉末易燃测试卡", "粉末", new string[] { "粉末", "易燃" });
            EnchantMatch mf = EnchantRules.Check(LayerKind.Heat, bf, new MaterialState(powderCard, 3), new List<TransitionRule>());
            Check("规则表：粉末+易燃 被优先级1（燃烧）截获，爆炸不可达", 1, mf.priority);
            Check("规则表：命中的是燃烧而不是爆炸", "燃烧（易燃）", mf.ruleName);
            Check("规则表：卡表没给燃烧产物 → productMissing 被标出来", true, mf.productMissing);

            // 玻璃对热附魔：一条都不命中
            BladeState bg = new BladeState("b", "刀片", 10, 0);
            bg.layers.Add(LayerKind.Heat, 3);
            EnchantMatch mg = MatchCase(cards, bg, cards.Material("玻璃"), "玻璃", LayerKind.Heat);
            Check("规则表：玻璃 + 热3 不命中任何热规则", false, mg.triggered);

            ScenarioEnd("规则表 11 个命中用例", mark, "优先级 / 产物 / 结果类型 都按正文表");
        }

        private static EnchantMatch MatchCase(JsonCards cards, BladeState blade, Ingredient card, string name, LayerKind kind)
        {
            MaterialState ms = new MaterialState(card, 3);
            List<TransitionRule> rx = cards.Reactions(name);
            return EnchantRules.Check(kind, blade, ms, rx);
        }

        // ══════════════════════════════════════════════════════════════
        //  四、端到端场景
        // ══════════════════════════════════════════════════════════════

        private static LevelRun NewLevel(JsonCards cards, string coreName, int coreH, int coreV)
        {
            LevelRun s = new LevelRun();
            s.blade = new BladeState(coreName, coreName, coreH, coreV);
            return s;
        }

        private static MaterialState Put(JsonCards cards, LevelRun s, string name, int d)
        {
            CardValues cv = cards.ValuesOf(name);
            MaterialState m = new MaterialState(cards.Material(name), d, cv.H, cv.V);
            s.table.Add(m);
            return m;
        }

        /// <summary>场景 1：热附魔 + 易燃 → 燃烧（形态变化，D 归零，产物进手牌；不再走 D 耗尽）。</summary>
        private static string Scenario1_Burn(JsonCards cards)
        {
            int mark = ScenarioStart();
            LevelRun s = NewLevel(cards, "铁刀片", 5, 5);
            s.blade.layers.Add(LayerKind.Heat, 1);

            MaterialState wp = Put(cards, s, "白磷", 3);
            TurnEngine e = new TurnEngine(ProbeRules(), cards);
            TurnResult r = e.StartBlade(s, wp, true);

            Check("白磷·燃烧：带易燃 + 热1 → 命中燃烧", true, r.LogContains("燃烧"));
            Check("白磷·燃烧：旧卡移出桌面", true, wp.removed);
            Check("白磷·燃烧：目标 D 立即归零", 0, wp.D);
            Check("白磷·燃烧：燃烧产物「火焰」进手牌", 1, r.ProducedCount("火焰"));
            CheckKind("白磷·燃烧：火焰是法术卡", ProduceKind.Spell, r.KindOfProduced("火焰"));
            Check("白磷·燃烧：形态变化不再走 D 耗尽 → 火焰只产出一次", 1, r.ProducedCount("火焰"));
            Check("白磷·燃烧：得分 = 目标V 3 + 刀片V 5", 8, r.activationScore);
            Check("白磷·燃烧：启动消耗 1 层附魔（规则表 F）", 0, s.blade.layers.Count(LayerKind.Heat));
            CheckLog("白磷·燃烧：日志有「形态变化」", r, "形态变化");
            CheckLog("白磷·燃烧：日志有「素材结算结束」的顺序说明", r, "不再检查后续附魔");

            ScenarioEnd("场景1 白磷·燃烧", mark,
                "白磷 " + (wp.removed ? "移出桌面" : "还在桌面") + "，D=" + wp.D + "，产出 " + r.ProducedText() + "，得分 " + r.ScoreDelta);

            return r.LogText();
        }

        /// <summary>场景 2：热≥2 + 液体 → 形态变化（水 → 水蒸气）。</summary>
        private static void Scenario2_LiquidToGas(JsonCards cards)
        {
            int mark = ScenarioStart();
            LevelRun s = NewLevel(cards, "铁刀片", 5, 5);
            s.blade.layers.Add(LayerKind.Heat, 2);

            MaterialState water = Put(cards, s, "水", 3);
            TurnEngine e = new TurnEngine(ProbeRules(), cards);
            TurnResult r = e.StartBlade(s, water, true);

            Check("水·液体→气态：命中优先级3", true, r.LogContains("优先级3"));
            Check("水·液体→气态：水蒸气进手牌", 1, r.ProducedCount("水蒸气"));
            CheckKind("水·液体→气态：水蒸气是素材卡", ProduceKind.Material, r.KindOfProduced("水蒸气"));
            Check("水·液体→气态：旧卡移出桌面", true, water.removed);
            Check("水·液体→气态：热 2 → 启动消耗 1 层 → 1", 1, s.blade.layers.Count(LayerKind.Heat));
            Check("水·液体→气态：得分用启动时的原始卡 V（2 + 5）", 7, r.activationScore);

            ScenarioEnd("场景2 水·液体→气态", mark,
                "水 → " + r.ProducedText() + "，热剩 " + s.blade.layers.Count(LayerKind.Heat) + "，得分 " + r.ScoreDelta);
        }

        /// <summary>场景 3：催化把热阈值 2 降到 1（催1：热≥2→热≥1）。</summary>
        private static void Scenario3_CatalystThreshold(JsonCards cards)
        {
            int mark = ScenarioStart();

            // (a) 热1、没有催化：热规则3/4 都要热≥2 → 一条都不命中
            LevelRun a = NewLevel(cards, "铁刀片", 5, 5);
            a.blade.layers.Add(LayerKind.Heat, 1);
            MaterialState w1 = Put(cards, a, "水", 3);
            TurnEngine ea = new TurnEngine(ProbeRules(), cards);
            TurnResult ra = ea.StartBlade(a, w1, false);

            Check("催化阈值：热1无催化 → 水不变形", true, ra.LogContains("都没有触发规则"));
            Check("催化阈值：热1无催化 → 水仍在桌面", false, w1.removed);
            Check("催化阈值：热1无催化 → D-1 = 2", 2, w1.D);

            // (b) 热1 + 催1：阈值降为 1 → 液体→气态 命中
            LevelRun b = NewLevel(cards, "铁刀片", 5, 5);
            b.blade.layers.Add(LayerKind.Heat, 1);
            b.blade.layers.Add(LayerKind.Catalyst, 1);
            MaterialState w2 = Put(cards, b, "水", 3);
            TurnEngine eb = new TurnEngine(ProbeRules(), cards);
            TurnResult rb = eb.StartBlade(b, w2, false);

            Check("催化阈值：热1+催1 → 阈值降到 1 → 水变水蒸气", 1, rb.ProducedCount("水蒸气"));
            Check("催化阈值：日志里写明阈值被催化降过", true, rb.LogContains("催化×1"));
            Check("催化阈值：催化层不参与启动消耗（规则表 E）", 1, b.blade.layers.Count(LayerKind.Catalyst));

            ScenarioEnd("场景3 催化·阈值修正", mark,
                "无催化热1 → 不变形；催1 时同一个热1 → " + rb.ProducedText());
        }

        /// <summary>场景 4：启动流程第②③④⑤步的顺序 + "仅改属性要继续检查" + 献祭吞噬。</summary>
        private static void Scenario4_StepOrderAndSacrifice(JsonCards cards)
        {
            int mark = ScenarioStart();
            LevelRun s = NewLevel(cards, "铁刀片", 5, 5);
            s.blade.layers.Add(LayerKind.Acid, 2);

            MaterialState glass = Put(cards, s, "玻璃", 3);
            TurnEngine e = new TurnEngine(ProbeRules(), cards);
            TurnResult r = e.StartBlade(s, glass, true);

            Check("顺序①：消耗 1 次行动机会（5→4）", 4, s.actionPoints);
            Check("顺序②：酸≥2 + 固体 → 目标 H-1（5→4）", 4, glass.H);
            Check("顺序②：仅改属性 → 没有变形（日志说明继续检查下一类）", true, r.LogContains("仅改属性、未发生形态变化"));
            Check("顺序②：D-1（3→2）", 2, glass.D);
            Check("顺序②：启动消耗 1 层酸（触发的那一类）", 1, s.blade.layers.Count(LayerKind.Acid));
            Check("顺序③：得分 = 目标V 2 + 刀片V 5 = 7", 7, r.activationScore);
            Check("顺序④：H=5 > 0 未爆刀", false, r.bursted);
            Check("顺序⑤：最后一次启动 + D 剩余 → 并入刀片，刀片 H 5-1(启动)+4 = 8", 8, s.blade.H);
            Check("顺序⑤：刀片 V 5+2=7", 7, s.blade.V);
            Check("顺序⑤：被吞噬的卡移出桌面", true, glass.removed);
            Check("顺序⑤：玻璃带「献祭」标签，但它的献祭文本解析不了 → 有警告", true, r.warnings.Count > 0);
            CheckLog("顺序⑤：日志说明这是最后一次启动", r, "最后一次启动");
            CheckLog("顺序⑤：日志说明已移出桌面", r, "移出桌面");
            CheckLog("顺序⑤：日志说明\"并入刀片\"", r, "并入刀片");

            // (b) 只触发最高优先级一条：酸层再多，H 也只掉 1 点
            LevelRun s2 = NewLevel(cards, "铁刀片", 5, 5);
            s2.blade.layers.Add(LayerKind.Acid, 4);
            MaterialState glass2 = Put(cards, s2, "玻璃", 3);
            TurnEngine e2 = new TurnEngine(ProbeRules(), cards);
            e2.StartBlade(s2, glass2, false);

            Check("只触发一条：酸4 时 H 仍只 -1（不是 -2/-4）", 4, glass2.H);

            ScenarioEnd("场景4 启动②③④⑤ + 献祭吞噬", mark,
                "玻璃 H 5→4、D 3→2、得分 7，末次启动被吞噬 → 刀片 H=" + s.blade.H + " V=" + s.blade.V +
                "；酸4 时 H 也只 -1（只触发最高优先级一条）");
        }

        /// <summary>场景 5：酸≥1 + 粉末 → 溶解移除，不产生副产物。</summary>
        private static void Scenario5_Dissolve(JsonCards cards)
        {
            int mark = ScenarioStart();
            LevelRun s = NewLevel(cards, "铁刀片", 5, 5);
            s.blade.layers.Add(LayerKind.Acid, 1);

            MaterialState powder = Put(cards, s, "硫磺粉", 3);
            TurnEngine e = new TurnEngine(ProbeRules(), cards);
            TurnResult r = e.StartBlade(s, powder, true);

            Check("溶解：旧卡移出桌面", true, powder.removed);
            Check("溶解：不产生副产物", 0, r.produced.Count);
            Check("溶解：手牌没有变化", 0, s.hand.Count);
            Check("溶解：得分仍按启动时的原始卡 V（2 + 5）", 7, r.activationScore);
            CheckLog("溶解：日志有「溶解移除」", r, "溶解移除");
            CheckLog("溶解：目标已被移除 → 献祭不生效", r, "本次献祭不生效");

            ScenarioEnd("场景5 硫磺粉·溶解移除", mark,
                "酸1 + 粉末 → 移出桌面、无副产物、得分 " + r.ScoreDelta);
        }

        /// <summary>场景 6：D 耗尽的产物（法术 + 副产物）直接进手牌；无产物则空白卡 +1。</summary>
        private static void Scenario6_ExhaustToHand(JsonCards cards)
        {
            int mark = ScenarioStart();

            // (a) 冰 D=1：本次启动 D→0 → D耗尽 → 水 + 空白卡 进手牌
            LevelRun s = NewLevel(cards, "铁刀片", 5, 5);
            MaterialState ice = Put(cards, s, "冰", 1);
            TurnEngine e = new TurnEngine(ProbeRules(), cards);
            TurnResult r = e.StartBlade(s, ice, true);

            Check("D耗尽：产物「水」进手牌", 1, r.ProducedCount("水"));
            CheckKind("D耗尽：水是素材副产物", ProduceKind.Material, r.KindOfProduced("水"));
            Check("D耗尽：产物「空白卡」进手牌", 1, r.ProducedCount("空白卡"));
            Check("D耗尽：空白卡计数 +1", 1, s.blankCount);
            Check("D耗尽：原卡移出桌面", true, ice.removed);
            Check("D耗尽：得分用启动时的原始卡 V（3 + 5）", 8, r.activationScore);
            Check("D耗尽：目标已移除 → 献祭不生效", true, r.LogContains("本次献祭不生效"));

            // (b) D=2：只掉 1 点，不触发 D 耗尽
            LevelRun s2 = NewLevel(cards, "铁刀片", 5, 5);
            MaterialState ice2 = Put(cards, s2, "冰", 2);
            TurnEngine e2 = new TurnEngine(ProbeRules(), cards);
            TurnResult r2 = e2.StartBlade(s2, ice2, false);

            Check("D耗尽：D=2 时只掉到 1，不触发", 1, ice2.D);
            Check("D耗尽：D=2 时没有产物", 0, r2.produced.Count);

            // (c) 没有任何产物的卡 → 兜底生成空白卡并计数
            LevelRun s3 = NewLevel(cards, "铁刀片", 5, 5);
            MaterialState bare = new MaterialState(cards.Synthetic("无产物测试卡"), 1, 1, 1);
            s3.table.Add(bare);
            TurnEngine e3 = new TurnEngine(ProbeRules(), cards);
            TurnResult r3 = e3.StartBlade(s3, bare, false);

            Check("D耗尽兜底：无产物 → 生成 1 张空白卡", 1, r3.ProducedCount("空白卡"));
            Check("D耗尽兜底：空白卡计数 +1", 1, s3.blankCount);

            ScenarioEnd("场景6 D耗尽产物进手牌", mark,
                "冰 D=1 → " + r.ProducedText() + "（空白卡计数 " + s.blankCount + "）；无产物卡 → 兜底空白卡");
        }

        /// <summary>场景 7：献祭只在"本回合实际使用的最后一次启动"生效。</summary>
        private static void Scenario7_SacrificeTiming(JsonCards cards)
        {
            int mark = ScenarioStart();
            LevelRun s = NewLevel(cards, "铁刀片", 10, 0);

            MaterialState wp = Put(cards, s, "白磷", 3);     // 第一次启动（不是最后一次）
            MaterialState gold = Put(cards, s, "黄金", 3);   // 第二次 = 最后一次

            TurnEngine e = new TurnEngine(ProbeRules(), cards);
            RoundResult round = e.RunRound(s, new List<MaterialState> { wp, gold });

            Check("献祭时机：这个回合启动了 2 次", 2, round.StartCount);
            Check("献祭时机：第一次启动的目标没被吞噬（还在桌面）", false, wp.removed);
            Check("献祭时机：最后一次启动的目标被吞噬", true, gold.removed);
            Check("献祭时机：刀片 H 10 - 2 次启动 + 黄金H 5 = 13", 13, s.blade.H);
            Check("献祭时机：刀片 V 0 + 黄金V 5 = 5", 5, s.blade.V);
            Check("献祭时机：两次启动的 V 得分 = 白磷3 + 黄金5 = 8", 8, round.starts[0].activationScore + round.starts[1].activationScore);
            Check("献祭时机：黄金带「献祭」标签 → 献祭效果 30 分也结算", 38, s.score);
            Check("献祭时机：黄金的献祭效果产出 3 张催化术", 3, round.starts[1].ProducedCount("催化术"));
            Check("献祭时机：桌面只剩白磷", 1, s.table.Count);
            CheckLog("献祭时机：日志说明第一次不是最后一次", round, "本次不是本回合实际使用的最后一次启动");
            CheckLog("献祭时机：日志说明并入刀片", round, "并入刀片");
            Check("献祭时机：回合结束后进入第 2 回合", 2, s.turnIndex);
            Check("献祭时机：回合结束附魔衰减跑过了", true, round.LogContains("附魔衰减"));

            ScenarioEnd("场景7 献祭只在最后一次启动", mark,
                "白磷不被吞噬；黄金被吞噬 → 刀片 H=13 V=5，献祭再给 30 分 + 3 张催化术，总分 " + s.score);
        }

        /// <summary>场景 8：爆刀 —— H≤0 立即爆刀、关卡结束、当前分数 ×2；主动结束不双倍。</summary>
        private static void Scenario8_Burst(JsonCards cards)
        {
            int mark = ScenarioStart();
            LevelRun s = NewLevel(cards, "铁刀片", 1, 5);

            MaterialState glass = Put(cards, s, "玻璃", 3);
            TurnEngine e = new TurnEngine(ProbeRules(), cards);
            TurnResult r = e.StartBlade(s, glass, false);

            Check("爆刀：启动消耗把 H 打到 0", 0, s.blade.H);
            Check("爆刀：爆刀成立", true, r.bursted);
            Check("爆刀：关卡结束", true, s.levelOver);
            Check("爆刀：本次启动得分 = 2 + 5 = 7", 7, r.activationScore);
            Check("爆刀：当前分数 ×2 → 14", 14, s.score);
            Check("爆刀：倍率是正文写的 ×2", 2, r.burstMultiplier);
            Check("爆刀：翻倍带来的增量是 7", 7, r.burstBonus);
            CheckLog("爆刀：日志写明爆刀并结束关卡", r, "爆刀，关卡结束");
            Check("爆刀：爆刀后不再吞噬（关卡已结束）", false, glass.removed);

            // 主动结束关卡：不触发双倍
            LevelRun s2 = NewLevel(cards, "铁刀片", 5, 5);
            s2.score = 20;
            TurnEngine e2 = new TurnEngine(ProbeRules(), cards);
            TurnResult r2 = e2.EndLevelByChoice(s2);

            Check("主动结束：分数不变（不触发爆刀双倍）", 20, s2.score);
            Check("主动结束：关卡结束", true, s2.levelOver);
            CheckLog("主动结束：日志说明不双倍", r2, "不触发爆刀双倍");

            ScenarioEnd("场景8 爆刀 ×2", mark,
                "H 1→0 → 得分 7 后总分翻倍为 " + s.score + "；主动结束则原样 " + s2.score);
        }

        /// <summary>场景 9：得分 = 目标素材 V + 刀片 V；吞噬素材会抬高刀片 V。</summary>
        private static void Scenario9_ScoreFormula(JsonCards cards)
        {
            int mark = ScenarioStart();
            LevelRun s = NewLevel(cards, "铁刀片", 20, 2);

            MaterialState glass = Put(cards, s, "玻璃", 3);   // V=2（夹具）
            TurnEngine e = new TurnEngine(ProbeRules(), cards);

            TurnResult r1 = e.StartBlade(s, glass, true);
            Check("计分公式：目标V 2 + 刀片V 2 = 4", 4, r1.activationScore);
            Check("计分公式：吞噬后刀片 V = 2 + 2 = 4", 4, s.blade.V);

            MaterialState wp = Put(cards, s, "白磷", 3);      // V=3（夹具）
            TurnResult r2 = e.StartBlade(s, wp, false);
            Check("计分公式：刀片 V 涨了以后，下一次启动 = 3 + 4 = 7", 7, r2.activationScore);
            Check("计分公式：总分累加 4 + 7 = 11", 11, s.score);

            ScenarioEnd("场景9 得分 = 目标V + 刀片V", mark,
                "第一次 2+2=4；吞噬后刀片 V=4；第二次 3+4=7；总分 " + s.score);
        }

        /// <summary>场景 10：冷热冲突 —— 后附魔覆盖先附魔，清空对方全部层数。</summary>
        private static void Scenario10_HeatColdOverwrite(JsonCards cards)
        {
            int mark = ScenarioStart();

            // (a) 先热后冷：打一张冰霜（附魔冷）
            LevelRun s = NewLevel(cards, "铁刀片", 5, 5);
            s.blade.layers.Add(LayerKind.Heat, 2);
            TurnEngine e = new TurnEngine(ProbeRules(), cards);
            TurnResult r = e.PlaySpell(s, cards.Spell("冰霜"));

            Check("冷热覆盖：热的 2 层被冷清空", 0, s.blade.layers.Count(LayerKind.Heat));
            Check("冷热覆盖：冷 +1", 1, s.blade.layers.Count(LayerKind.Cold));
            Check("冷热覆盖：冰霜还让刀片 H+1（5→6）", 6, s.blade.H);
            CheckLog("冷热覆盖：日志写明后附魔覆盖先附魔", r, "后附魔覆盖先附魔");

            // (b) 先冷后热：打一张火焰（附魔热）
            LevelRun s2 = NewLevel(cards, "铁刀片", 5, 5);
            s2.blade.layers.Add(LayerKind.Cold, 3);
            TurnEngine e2 = new TurnEngine(ProbeRules(), cards);
            TurnResult r2 = e2.PlaySpell(s2, cards.Spell("火焰"));

            Check("冷热覆盖：冷的 3 层被热清空", 0, s2.blade.layers.Count(LayerKind.Cold));
            Check("冷热覆盖：热 +1", 1, s2.blade.layers.Count(LayerKind.Heat));
            Check("冷热覆盖：法术不消耗行动机会", LevelRun.ActionPointsPerTurn, s2.actionPoints);

            // (c) 酸催共存：酸不会被热清掉
            LevelRun s3 = NewLevel(cards, "铁刀片", 5, 5);
            s3.blade.layers.Add(LayerKind.Acid, 2);
            TurnEngine e3 = new TurnEngine(ProbeRules(), cards);
            e3.PlaySpell(s3, cards.Spell("火焰"));

            Check("酸催共存：打热附魔不碰酸层", 2, s3.blade.layers.Count(LayerKind.Acid));

            ScenarioEnd("场景10 冷热覆盖", mark,
                "热2 → 冰霜 → 冷1（热清空）；冷3 → 火焰 → 热1（冷清空）；酸不受影响");
        }

        /// <summary>场景 11：正文矛盾 —— "粉末+易燃"永远被"燃烧"截获，"爆炸"规则不可达。</summary>
        private static void Scenario11_ExplodeUnreachable(JsonCards cards)
        {
            int mark = ScenarioStart();
            LevelRun s = NewLevel(cards, "铁刀片", 5, 5);
            s.blade.layers.Add(LayerKind.Heat, 1);

            // 卡表里当前没有"粉末+易燃"的卡，用一张探针合成卡把规则表的这条矛盾跑出来
            Ingredient powderCard = cards.Synthetic("粉末易燃测试卡", "粉末", new string[] { "粉末", "易燃" });
            MaterialState powder = new MaterialState(powderCard, 3, 2, 1);
            s.table.Add(powder);

            TurnEngine e = new TurnEngine(ProbeRules(), cards);
            TurnResult r = e.StartBlade(s, powder, false);

            Check("爆炸不可达：命中优先级1（燃烧），不是优先级2（爆炸）", true, r.LogContains("优先级1「燃烧"));
            Check("爆炸不可达：整局日志里没有出现「爆炸」", false, r.LogContains("爆炸"));
            Check("爆炸不可达：卡表没给燃烧产物 → 有警告", true, r.warnings.Count > 0);
            Check("爆炸不可达：燃烧把 D 归零 → D耗尽照常触发（正文两条路径的冲突现场）", true, powder.removed);
            Check("爆炸不可达：这张卡没有任何 D 耗尽产物 → 兜底空白卡", 1, r.ProducedCount("空白卡"));
            Check("爆炸不可达：空白卡计数 +1", 1, s.blankCount);

            ScenarioEnd("场景11 爆炸规则不可达（正文矛盾）", mark,
                "粉末+易燃 走优先级1燃烧；卡表没产物 → 警告 + D归零后 D耗尽 → " + r.ProducedText());
        }

        // ══════════════════════════════════════════════════════════════
        //  报告检查：不许静默失效
        // ══════════════════════════════════════════════════════════════

        private static void Report_NoSilentLoss(JsonCards cards)
        {
            int mark = ScenarioStart();

            RuleReport rep = RuleReport.BuildAll(cards.materials, cards.spells, cards);

            Check("解析报告：句子账目全部对得上（clauses + 未识别 == 句子数）", 0, rep.UnbalancedFields);
            Check("解析报告：没有字段静默丢失（有文本却切不出句子）", 0, rep.silentFields.Count);

            // ★ 金丝雀：策划改了 cards_v21.json 之后这个数字会变 —— 那就该有人来看这份清单。
            Check("解析报告：未识别句子数（金丝雀，改卡表后会变）", 5, rep.unrecognized.Count);

            CheckUnrecognized(rep, "熔融金", "献祭", "额外增加10V");
            CheckUnrecognized(rep, "法术卷轴", "献祭", "复制2张");
            CheckUnrecognized(rep, "熔融玻璃", "献祭", "翻倍");
            CheckUnrecognized(rep, "玻璃", "献祭", "获得1分");
            CheckUnrecognized(rep, "固态汞", "献祭", "损伤");

            Check("解析报告：没有『执行不了』的条目（v3 里素材有 H 了，v2.1 的『其他素材H』也能落地）",
                 0, rep.unsupported.Count);
            Check("解析报告：占位数值至少 3 处（一定分数 / 没给每层分值）", true, rep.placeholders.Count >= 3);
            Check("解析报告：未知产出里有『灰烬』（法术卷轴转换的目标不在卡表里）", true, HasUnknown(rep, "灰烬"));
            Check("解析报告：规则表接不上清单非空（标了标签忘了连锁产物）", true, rep.tableGaps.Count >= 1);
            Check("解析报告：冰缺「遇热 + 热」产物被列出来", true, HasGap(rep, "冰：热规则4"));
            Check("解析报告：黄金缺「遇酸 + 酸」产物被列出来", true, HasGap(rep, "黄金：酸规则1"));
            Check("解析报告：v3 已失效写法清单非空（水的『不消耗刀片H』等）", true, rep.superseded.Count >= 1);
            Check("解析报告：矛盾清单至少 8 条（启动消耗层/爆炸不可达/献祭两义/…）", true, rep.conflicts.Count >= 8);

            ScenarioEnd("报告·不静默失效", mark,
                "句子 " + rep.TotalSentences + " 条：已解析 " + rep.ParsedSentences + " · 未识别 " + rep.unrecognized.Count +
                "（金丝雀）· 规则表接不上 " + rep.tableGaps.Count + " · v3 失效写法 " + rep.superseded.Count +
                " · 正文矛盾 " + rep.conflicts.Count);

            Console.WriteLine();
            Console.WriteLine("=== 规则解析报告（给策划）===");
            rep.Print(Console.Out);

            Console.WriteLine();
            Console.WriteLine("=== 附魔规则表（引擎里硬编码的那张表，正文 B/C/D/E 节）===");
            EnchantRules.PrintTable(Console.Out);
        }

        private static void CheckUnrecognized(RuleReport rep, string card, string field, string keyword)
        {
            for (int i = 0; i < rep.unrecognized.Count; i++)
            {
                UnrecognizedRule u = rep.unrecognized[i];
                if (u.cardName == card && u.field == field && u.sentence.Contains(keyword))
                {
                    passed++;
                    return;
                }
            }
            failed++;
            failures.Add("未识别清单：应该收进「" + card + " · " + field + "」里含『" + keyword + "』的那句，但清单里没有 —— 说明它被静默丢掉了");
        }

        private static bool HasUnknown(RuleReport rep, string name)
        {
            for (int i = 0; i < rep.unknownProducts.Count; i++)
                if (rep.unknownProducts[i].Contains(name)) return true;
            return false;
        }

        private static bool HasGap(RuleReport rep, string name)
        {
            for (int i = 0; i < rep.tableGaps.Count; i++)
                if (rep.tableGaps[i].Contains(name)) return true;
            return false;
        }

        // ══════════════════════════════════════════════════════════════
        //  卡表读取（cards_v21.json）
        //
        //  【为什么自己写一个 JSON 读取】
        //    Unity 侧走的是 JsonUtility（UnityEngine.JSONSerializeModule），
        //    离线探针只引 CoreModule，引不到它；而且探针的整个意义就是"不开 Unity"。
        //    这份 JSON 是我们自己维护的、格式很干净，一个小解析器足够稳。
        // ══════════════════════════════════════════════════════════════

        private class JsonCards : ICardLookup
        {
            public readonly List<Ingredient> materials = new List<Ingredient>();
            public readonly List<SpellSpec> spells = new List<SpellSpec>();

            private readonly Dictionary<string, Ingredient> matByName = new Dictionary<string, Ingredient>();
            private readonly Dictionary<string, string> spellEnchant = new Dictionary<string, string>();
            private readonly List<string> spellNames = new List<string>();
            private readonly Dictionary<string, CardValues> values = new Dictionary<string, CardValues>();
            private readonly Dictionary<string, List<TransitionRule>> reactions = new Dictionary<string, List<TransitionRule>>();

            public Ingredient Material(string name)
            {
                if (string.IsNullOrEmpty(name)) return null;
                Ingredient v;
                return matByName.TryGetValue(name, out v) ? v : null;
            }

            public bool IsSpell(string name)
            {
                return !string.IsNullOrEmpty(name) && spellEnchant.ContainsKey(name);
            }

            public List<string> SpellNames() { return spellNames; }

            public string EnchantOf(string spellName)
            {
                if (string.IsNullOrEmpty(spellName)) return "";
                string v;
                return spellEnchant.TryGetValue(spellName, out v) ? v : "";
            }

            public CardValues ValuesOf(string cardName)
            {
                CardValues v;
                if (!string.IsNullOrEmpty(cardName) && values.TryGetValue(cardName, out v)) return v;
                return new CardValues(0, 3, 0);
            }

            /// <summary>这张卡的"产物表"（由卡表 transitions 解析而来）—— 规则表靠它取"该卡指定的X形态"。</summary>
            public List<TransitionRule> Reactions(string cardName)
            {
                List<TransitionRule> v;
                if (!string.IsNullOrEmpty(cardName) && reactions.TryGetValue(cardName, out v)) return v;
                return new List<TransitionRule>();
            }

            public SpellSpec Spell(string name)
            {
                for (int i = 0; i < spells.Count; i++)
                    if (spells[i].name == name) return spells[i];
                return new SpellSpec();
            }

            /// <summary>探针自己造的卡（验证"规则表命中但卡表没有产物"与"D耗尽无产物 → 空白卡"两条路径）。</summary>
            public Ingredient Synthetic(string name)
            {
                return Synthetic(name, "固体", new string[0]);
            }

            public Ingredient Synthetic(string name, string form, string[] tags)
            {
                Ingredient ing = new Ingredient("probe:" + name, name, new AttrSet());
                ing.form = form;
                ing.tags = tags != null ? tags : new string[0];
                ing.transitions = new FormChange[0];
                ing.exhaust = new string[0];
                return ing;
            }

            private void AddMaterial(Ingredient ing)
            {
                materials.Add(ing);
                if (string.IsNullOrEmpty(ing.name)) return;

                matByName[ing.name] = ing;

                // 产物表：把 transitions 解析一遍（规则表要用 triggerTag + 层数类别 定位产物）
                RuleParseResult pr = RuleText.ParseTransitions(ing.transitions, ing.id, ing.name, null);
                reactions[ing.name] = pr.transitions;
            }

            private void AddSpell(SpellSpec sp)
            {
                spells.Add(sp);
                if (!string.IsNullOrEmpty(sp.name))
                {
                    spellEnchant[sp.name] = sp.enchant != null ? sp.enchant : "";
                    spellNames.Add(sp.name);
                }
            }

            private void AddValues(string name, CardValues cv)
            {
                if (!string.IsNullOrEmpty(name)) values[name] = cv;
            }

            /// <summary>卡表里已有 h/d/v 就用真值（策划补数值后自动生效），没有才用夹具值。</summary>
            public void AddValuesFor(string name, Dictionary<string, object> d)
            {
                if (d == null || string.IsNullOrEmpty(name)) return;
                if (!d.ContainsKey("h") && !d.ContainsKey("d") && !d.ContainsKey("v"))
                {
                    AddValues(name, FixtureValues(name));
                    return;
                }

                string vRaw = GetStr(d, "v");
                int v = GetInt(d, "v");
                if (v == 0) v = CardValues.ParseV(vRaw);

                int dd = GetInt(d, "d");
                AddValues(name, new CardValues(GetInt(d, "h"), dd > 0 ? dd : 3, v, vRaw));
            }

            // ── 加载 ──────────────────────────────────────────────────

            public static JsonCards Load(string[] args)
            {
                string path = FindCardsJson(args);
                if (path == null)
                {
                    Console.WriteLine("  ✗ 找不到 Resources/Config/cards_v21.json");
                    Console.WriteLine("    （run.ps1 会把 UnityProject 路径当参数传进来；也可以手工跑：ruleprobe.dll <UnityProject 路径>）");
                    return null;
                }
                Console.WriteLine("  卡表：" + path);

                string text;
                try { text = File.ReadAllText(path, Encoding.UTF8); }
                catch (Exception ex) { Console.WriteLine("  ✗ 读不了卡表：" + ex.Message); return null; }

                Dictionary<string, object> root;
                try { root = MiniJson.Parse(text) as Dictionary<string, object>; }
                catch (Exception ex) { Console.WriteLine("  ✗ 卡表 JSON 解析失败：" + ex.Message); return null; }

                if (root == null) { Console.WriteLine("  ✗ 卡表根节点不是 JSON 对象"); return null; }

                JsonCards c = new JsonCards();

                List<object> mats = GetList(root, "materials");
                for (int i = 0; i < mats.Count; i++)
                {
                    Dictionary<string, object> d = mats[i] as Dictionary<string, object>;
                    if (d == null) continue;
                    Ingredient ing = BuildMaterial(d);
                    if (ing == null) continue;
                    c.AddMaterial(ing);
                    c.AddValuesFor(ing.name, d);   // 卡表里有 h/d/v 就用真值，否则用夹具值
                }

                List<object> sps = GetList(root, "spells");
                for (int i = 0; i < sps.Count; i++)
                {
                    Dictionary<string, object> d = sps[i] as Dictionary<string, object>;
                    if (d == null) continue;
                    c.AddSpell(new SpellSpec(
                        GetStr(d, "id"), GetStr(d, "name"), GetStr(d, "enchant"),
                        GetStr(d, "category"), GetStr(d, "requirement")));
                }

                return c;
            }

            private static Ingredient BuildMaterial(Dictionary<string, object> d)
            {
                string id = GetStr(d, "id");
                if (string.IsNullOrEmpty(id)) return null;

                Ingredient ing = new Ingredient(id, GetStr(d, "name"), new AttrSet());
                ing.series = GetStr(d, "series");
                ing.form = GetStr(d, "form");
                ing.tags = GetStrings(d, "tags");
                ing.exhaust = GetStrings(d, "exhaust");
                ing.startup = GetStr(d, "startup");
                ing.sacrifice = GetStr(d, "sacrifice");
                ing.note = GetStr(d, "note");

                List<object> trs = GetList(d, "transitions");
                ing.transitions = new FormChange[trs.Count];
                for (int i = 0; i < trs.Count; i++)
                {
                    Dictionary<string, object> t = trs[i] as Dictionary<string, object>;
                    ing.transitions[i] = t == null ? new FormChange() : new FormChange(GetStr(t, "trigger"), GetStr(t, "result"));
                }

                return ing;
            }

            /// <summary>
            /// 测试夹具数值。正文 §2.2 说 V 的档位映射有了、具体数值"等待第二周设计关卡一并处理"，
            /// 卡表里因此没有 h/d/v —— 这里给用到的卡填临时值，只为验证
            /// "得分 = 目标素材V + 刀片V" 与 "献祭：刀片H += 卡H" 两条公式。
            /// **卡表补上 h/d/v 后，这段夹具就该删掉。**
            /// </summary>
            private static CardValues FixtureValues(string name)
            {
                switch (name)
                {
                    case "白磷":   return new CardValues(3, 3, CardValues.VGradeValue("中"), "中");
                    case "水":     return new CardValues(2, 3, CardValues.VGradeValue("低"), "低");
                    case "冰":     return new CardValues(5, 2, CardValues.VGradeValue("中"), "中");
                    case "水蒸气": return new CardValues(1, 2, CardValues.VGradeValue("低"), "低");
                    case "盐":     return new CardValues(2, 3, CardValues.VGradeValue("低"), "低");
                    case "玻璃":   return new CardValues(5, 3, CardValues.VGradeValue("低"), "低");
                    case "硫磺粉": return new CardValues(2, 2, CardValues.VGradeValue("低"), "低");
                    case "黄金":   return new CardValues(5, 3, CardValues.VGradeValue("高"), "高");
                    case "铜":     return new CardValues(4, 3, CardValues.VGradeValue("低"), "低");
                    case "铁":     return new CardValues(6, 3, CardValues.VGradeValue("中"), "中");
                    case "空白卡": return new CardValues(0, 0, 0, "极低");
                }
                return new CardValues(1, 3, 1, "极低");
            }

            /// <summary>读整数字段（数字或数字字符串都认；"中"这种档位文字返回 0）。</summary>
            private static int GetInt(Dictionary<string, object> d, string key)
            {
                object v;
                if (d == null || !d.TryGetValue(key, out v) || v == null) return 0;

                if (v is double) return (int)(double)v;

                int n;
                if (int.TryParse(v.ToString(), out n)) return n;
                return 0;
            }

            private static string FindCardsJson(string[] args)
            {
                const string rel = "Assets/Resources/Config/cards_v21.json";
                const string rel2 = "UnityProject/Assets/Resources/Config/cards_v21.json";

                if (args != null && args.Length > 0 && !string.IsNullOrEmpty(args[0]))
                {
                    string a = args[0];
                    if (File.Exists(a)) return a;
                    if (Directory.Exists(a))
                    {
                        string p1 = Path.Combine(a, rel);
                        if (File.Exists(p1)) return p1;
                        string p2 = Path.Combine(a, rel2);
                        if (File.Exists(p2)) return p2;
                    }
                }

                return WalkUp(Environment.CurrentDirectory) ?? WalkUp(AppContext.BaseDirectory);
            }

            private static string WalkUp(string start)
            {
                const string rel = "Assets/Resources/Config/cards_v21.json";
                const string rel2 = "UnityProject/Assets/Resources/Config/cards_v21.json";

                try
                {
                    DirectoryInfo d = new DirectoryInfo(start);
                    for (int i = 0; i < 8 && d != null; i++)
                    {
                        string p1 = Path.Combine(d.FullName, rel);
                        if (File.Exists(p1)) return p1;
                        string p2 = Path.Combine(d.FullName, rel2);
                        if (File.Exists(p2)) return p2;
                        d = d.Parent;
                    }
                }
                catch { }
                return null;
            }

            private static string GetStr(Dictionary<string, object> d, string key)
            {
                object v;
                if (d != null && d.TryGetValue(key, out v) && v != null) return v as string ?? v.ToString();
                return "";
            }

            private static List<object> GetList(Dictionary<string, object> d, string key)
            {
                object v;
                if (d != null && key != null && d.TryGetValue(key, out v))
                {
                    List<object> l = v as List<object>;
                    if (l != null) return l;
                }
                return new List<object>();
            }

            private static string[] GetStrings(Dictionary<string, object> d, string key)
            {
                List<object> l = GetList(d, key);
                List<string> res = new List<string>();
                for (int i = 0; i < l.Count; i++) if (l[i] != null) res.Add(l[i].ToString());
                return res.ToArray();
            }
        }

        /// <summary>够用就好的 JSON 解析器（对象 / 数组 / 字符串 / 数字 / bool / null）。</summary>
        private static class MiniJson
        {
            public static object Parse(string s)
            {
                int i = 0;
                return Value(s, ref i);
            }

            private static void Ws(string s, ref int i)
            {
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            }

            private static object Value(string s, ref int i)
            {
                Ws(s, ref i);
                if (i >= s.Length) return null;

                char c = s[i];
                if (c == '{') return Obj(s, ref i);
                if (c == '[') return Arr(s, ref i);
                if (c == '"') return Str(s, ref i);
                if (c == 't') { i += 4; return true; }
                if (c == 'f') { i += 5; return false; }
                if (c == 'n') { i += 4; return null; }
                return Num(s, ref i);
            }

            private static Dictionary<string, object> Obj(string s, ref int i)
            {
                Dictionary<string, object> d = new Dictionary<string, object>();
                i++;
                while (i < s.Length)
                {
                    Ws(s, ref i);
                    if (i >= s.Length) break;
                    if (s[i] == '}') { i++; break; }
                    if (s[i] == ',') { i++; continue; }

                    string k = Str(s, ref i);
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ':') i++;
                    d[k] = Value(s, ref i);
                }
                return d;
            }

            private static List<object> Arr(string s, ref int i)
            {
                List<object> l = new List<object>();
                i++;
                while (i < s.Length)
                {
                    Ws(s, ref i);
                    if (i >= s.Length) break;
                    if (s[i] == ']') { i++; break; }
                    if (s[i] == ',') { i++; continue; }
                    l.Add(Value(s, ref i));
                }
                return l;
            }

            private static string Str(string s, ref int i)
            {
                Ws(s, ref i);
                if (i >= s.Length || s[i] != '"') return "";

                i++;
                StringBuilder sb = new StringBuilder();
                while (i < s.Length && s[i] != '"')
                {
                    char c = s[i];
                    if (c == '\\' && i + 1 < s.Length)
                    {
                        i++;
                        char e = s[i];
                        switch (e)
                        {
                            case 'n': sb.Append('\n'); break;
                            case 't': sb.Append('\t'); break;
                            case 'r': sb.Append('\r'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'u':
                                if (i + 4 < s.Length)
                                {
                                    int code;
                                    if (int.TryParse(s.Substring(i + 1, 4),
                                            System.Globalization.NumberStyles.HexNumber,
                                            System.Globalization.CultureInfo.InvariantCulture, out code))
                                        sb.Append((char)code);
                                    i += 4;
                                }
                                break;
                            default: sb.Append(e); break;
                        }
                    }
                    else sb.Append(c);
                    i++;
                }
                if (i < s.Length && s[i] == '"') i++;
                return sb.ToString();
            }

            private static object Num(string s, ref int i)
            {
                int start = i;
                while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' || s[i] == '.' || s[i] == 'e' || s[i] == 'E')) i++;

                string t = s.Substring(start, i - start);
                double d;
                if (double.TryParse(t, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out d)) return d;
                return t;
            }
        }
    }
}
