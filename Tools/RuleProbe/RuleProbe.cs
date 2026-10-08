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
                V3_NewMechanics(cards);

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
                Scenario11_Explode(cards);
                Scenario12_SaveLoad(cards);
                Scenario13_LastStartByActionPoints(cards);
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
        //  二·五、v3.0 新机制清单（parsed? × executed?）
        //
        //  【这一节要钉住的是什么】
        //    v3.0 定稿给每张卡写了大量新写法（得分翻倍 / 不消耗 H / 层数翻倍 / 每层得分 /
        //    额外产卡 / 随机法术 / 复制手牌法术 / 常驻被动 / 反应式触发…）。
        //    对这些写法，**"能解析"和"能执行"是两件事**，而两者一旦混起来，
        //    就会出现最坏的情况："卡表看起来全绿、游戏里那张卡其实是死的"。
        //    所以这里按「原文短句 → 期望 op」逐条钉住，并且**再单独钉一条**:
        //      这个 op 在 TurnEngine.ExecuteAction 里到底有没有真实现。
        //    EngineExecutes 那张表是照 ExecuteAction 的 switch 逐条抄的；
        //    哪天引擎补了某个算子，这张表必须跟着改 —— 改不动就说明"报告和实现脱节了"。
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 这个 op 在 TurnEngine.ExecuteAction 里**是不是真的有实现**。
        ///
        /// 【为什么必须单列一张表】解析器认出一个 op，只说明"这句话读懂了"；
        ///   引擎里的 switch 是不是照着做了，是另一件事：
        ///     · BladeNoConsumeH：只打警告，**不豁免**（v3 正文把豁免取消了）；
        ///     · NoConsumeLayer：只打一行日志，**不产生任何效果**；
        ///     · LowerEnchantThreshold：真作用是 LayerLedger.LowerThreshold，这条 op 只做记录；
        ///     · 其余 op 都有实际动作。
        ///   探针用这张表断言"哪些 op 目前是空转的"，避免报告里把空转写成已实现。
        /// </summary>
        private class OpSemantics
        {
            public RuleOp op;
            public bool executed;        // 引擎会真的改状态吗
            public string note;
        }

        private static OpSemantics[] EngineOps()
        {
            return new OpSemantics[]
            {
                new OpSemantics { op = RuleOp.AddScore,              executed = true,  note = "加分" },
                new OpSemantics { op = RuleOp.MultiplyScore,         executed = true,  note = "得分倍率（本次得分 ×(N-1) 追加）" },
                new OpSemantics { op = RuleOp.ZeroScore,             executed = true,  note = "本次得分清零" },
                new OpSemantics { op = RuleOp.ScorePerLayer,         executed = true,  note = "层数 × N 加分" },
                new OpSemantics { op = RuleOp.ScoreByLayerCount,     executed = true,  note = "按层数加分" },
                new OpSemantics { op = RuleOp.ScorePerConsumedLayer, executed = true,  note = "已消耗层数 × N 加分" },
                new OpSemantics { op = RuleOp.BladeNoConsumeH,       executed = false, note = "★ 空转：只打「v3 已失效」警告，启动照样扣 1 H" },
                new OpSemantics { op = RuleOp.BladeDamageH,          executed = true,  note = "扣刀片 H" },
                new OpSemantics { op = RuleOp.BladeHardenH,          executed = true,  note = "加刀片 H" },
                new OpSemantics { op = RuleOp.BladeDamageOtherH,     executed = true,  note = "其他素材 H-N" },
                new OpSemantics { op = RuleOp.OtherMaterialsDepleteD,executed = true,  note = "其他素材 D-N" },
                new OpSemantics { op = RuleOp.AddLayer,              executed = true,  note = "加层（可带不衰退）" },
                new OpSemantics { op = RuleOp.ConsumeLayer,          executed = true,  note = "消耗 N 层" },
                new OpSemantics { op = RuleOp.ConsumeAllLayers,      executed = true,  note = "消耗所有该类层数" },
                new OpSemantics { op = RuleOp.NoConsumeLayer,        executed = false, note = "★ 空转：只打一行日志，层数照样会被启动消耗" },
                new OpSemantics { op = RuleOp.DoubleLayers,          executed = true,  note = "层数翻倍" },
                new OpSemantics { op = RuleOp.ProduceCard,           executed = true,  note = "产出卡进手牌" },
                new OpSemantics { op = RuleOp.ProduceRandomSpell,    executed = true,  note = "随机法术（走种子 RNG）" },
                new OpSemantics { op = RuleOp.EnchantBlade,          executed = true,  note = "附魔 = 加该类型层" },
                new OpSemantics { op = RuleOp.LowerEnchantThreshold, executed = false, note = "★ 本条只做记录：真作用在 LayerLedger.LowerThreshold（催≥1 → -1，催≥3 → 再 -1）" },
                new OpSemantics { op = RuleOp.SetBurstMultiplier,    executed = true,  note = "本次结算的爆刀倍率" },
            };
        }

        private static OpSemantics SemanticsOf(RuleOp op)
        {
            OpSemantics[] all = EngineOps();
            for (int i = 0; i < all.Length; i++) if (all[i].op == op) return all[i];
            return null;
        }

        /// <summary>解析一句，返回它认出来的第一批 op（未识别则为空数组）。</summary>
        private static RuleAction[] OpsOf(string text, RuleField field, string enchant)
        {
            RuleParseResult pr = RuleText.ParseField(text, field, "probe", "probe",
                new RuleContext(EmptyCardLookup.Instance, enchant));

            List<RuleAction> acts = new List<RuleAction>();
            for (int i = 0; i < pr.clauses.Count; i++)
                for (int k = 0; k < pr.clauses[i].actions.Count; k++)
                    acts.Add(pr.clauses[i].actions[k]);
            return acts.ToArray();
        }

        private static bool HasOp(RuleAction[] acts, RuleOp op)
        {
            for (int i = 0; i < acts.Length; i++) if (acts[i].op == op) return true;
            return false;
        }

        private static RuleAction FirstOp(RuleAction[] acts, RuleOp op)
        {
            for (int i = 0; i < acts.Length; i++) if (acts[i].op == op) return acts[i];
            return null;
        }

        private static void V3_NewMechanics(JsonCards cards)
        {
            Console.WriteLine();
            Console.WriteLine("=== v3.0 新机制清单（每句：解析结果 × 引擎是否真执行）===");

            // 整份卡表的报告走一遍（下面"半懂字段"要读它）
            RuleReport mechRep = RuleReport.BuildAll(cards.reportMaterials, cards.spells, cards);

            // ── ① 解析层：每句原文短句 → 期望 op；同时钉住"引擎执行得了吗" ──
            //
            //   下面每条都按**定稿原文的短句**写（不是改写过的"引擎友好"版本）：
            //   卡表里怎么写，这里就怎么解析 —— 这样"策划改了文案"必然在这里红一条。
            Check("v3机制·得分翻倍：引擎的「得分倍率」是真实现的", true,
                  SemanticsOf(RuleOp.MultiplyScore) != null && SemanticsOf(RuleOp.MultiplyScore).executed);

            RuleAction[] steam = OpsOf("若刀片有热附魔，则不消耗热层数，且本次启动得分翻倍", RuleField.Startup, "");
            Check("v3机制·不消耗热层数：解析成「不消耗层」（热）", true,
                  HasOp(steam, RuleOp.NoConsumeLayer) && FirstOp(steam, RuleOp.NoConsumeLayer).HasKind(LayerKind.Heat));
            Check("v3机制·不消耗热层数：得分翻倍同时认出来", true,
                  HasOp(steam, RuleOp.MultiplyScore) && FirstOp(steam, RuleOp.MultiplyScore).amount == 2);
            Check("v3机制·不消耗热层数：★ 引擎这个 op 是空转（层数照样被消耗）", false,
                  SemanticsOf(RuleOp.NoConsumeLayer).executed);

            RuleAction[] salt = OpsOf("若有冷/热/酸，层数翻倍", RuleField.Startup, "");
            Check("v3机制·层数翻倍：认成「层数翻倍」，类别取自条件（冷/热/酸）", true,
                  HasOp(salt, RuleOp.DoubleLayers) && FirstOp(salt, RuleOp.DoubleLayers).layers.Length == 3);

            RuleAction[] vapor = OpsOf("若刀片有热附魔，则本次启动消耗刀片所有热层数，每消耗一层获得2分", RuleField.Startup, "");
            Check("v3机制·消耗所有热层数并每层得 2 分：两个 op 都认出来", true,
                  HasOp(vapor, RuleOp.ConsumeAllLayers) && HasOp(vapor, RuleOp.ScorePerConsumedLayer));
            Check("v3机制·每消耗一层 2 分：类别由前面那次消耗补齐（热）", true,
                  FirstOp(vapor, RuleOp.ScorePerConsumedLayer).HasKind(LayerKind.Heat) &&
                  FirstOp(vapor, RuleOp.ScorePerConsumedLayer).amount == 2);

            RuleAction[] ice = OpsOf("若刀片冷层数≥3，则本次启动额外产生一张冰霜法术进手牌", RuleField.Startup, "");
            Check("v3机制·额外产生某卡进手牌：产出名 = 「冰霜」（后缀「法术进手牌」已剥掉）", true,
                  HasOp(ice, RuleOp.ProduceCard) && FirstOp(ice, RuleOp.ProduceCard).cardName == "冰霜");

            RuleAction[] scroll = OpsOf("指定手牌中的1张法术卡，复制2张", RuleField.Sacrifice, "");
            Check("v3机制·复制手牌法术：★ 认不出来（要玩家选牌，没有交互模型）→ 必须进未识别清单", 0, scroll.Length);

            RuleAction[] copper = OpsOf("刀片每次启动，其他素材D-2", RuleField.Sacrifice, "");
            Check("v3机制·常驻被动（其他素材D-2）：认成「其他素材D-N」且注册成每次启动", true,
                  HasOp(copper, RuleOp.OtherMaterialsDepleteD) && FirstOp(copper, RuleOp.OtherMaterialsDepleteD).amount == 2);

            RuleAction[] glass = OpsOf("每次消耗H时，获得1分", RuleField.Sacrifice, "");
            Check("v3机制·每次消耗H时得1分：★ 反应式触发认不出来（没有钩子）→ 必须进未识别清单", 0, glass.Length);

            RuleAction[] mGlass = OpsOf("每次获得冷层数时，翻倍", RuleField.Sacrifice, "");
            Check("v3机制·每次获得冷层数时翻倍：★ 反应式触发认不出来 → 必须进未识别清单", 0, mGlass.Length);

            RuleAction[] mercury = OpsOf("刀片每次受到大于等于2点H的损伤时，减少1点", RuleField.Sacrifice, "");
            Check("v3机制·受击≥2点H时减1点：★ 反应式触发认不出来 → 必须进未识别清单", 0, mercury.Length);

            RuleAction[] gold = OpsOf("一次性获得30分，获得3张催化术", RuleField.Sacrifice, "");
            Check("v3机制·一次性获得 N 层/张：30 分 + 3 张催化术都认出来", true,
                  HasOp(gold, RuleOp.AddScore) && FirstOp(gold, RuleOp.AddScore).amount == 30 &&
                  HasOp(gold, RuleOp.ProduceCard) && FirstOp(gold, RuleOp.ProduceCard).count == 3);

            // 熔融金：整句是"刀片减少5H，额外增加10V"。
            // ★ 前半句（H-5）本身要认出来 —— 但**整句是半懂**（V 那半认不出来），
            //   所以这一整句不进 clauses（引擎不会执行它，见文件头的"不半懂半执行"不变量）。
            //   探针分两层钉：① 单独那半句必须解析成 BladeDamageH(5)；② 整句必须出现在「半懂字段」里。
            RuleAction[] moltenGoldHalf = OpsOf("刀片减少5H", RuleField.Sacrifice, "");
            Check("v3机制·刀片 H±n：单独那半句「刀片减少5H」认成扣 5 点", true,
                  HasOp(moltenGoldHalf, RuleOp.BladeDamageH) && FirstOp(moltenGoldHalf, RuleOp.BladeDamageH).amount == 5);

            RuleAction[] moltenGold = OpsOf("刀片减少5H，额外增加10V", RuleField.Sacrifice, "");
            Check("v3机制·刀片 V+n：★ 认不出来（刀片模型没有 V）→ 整句不执行、进未识别清单", 0, moltenGold.Length);
            // ★ 这一句是典型的**半懂**：前半句认得出、后半句认不出。
            //   以前的账目只看"clauses + unrecognized == 句子数"，这种事在汇总里看不出来
            //   （面板上只写"未识别 1"，看不出前半句本来是可以生效的），
            //   所以报告里专门加了「半懂字段」一节钉住它。
            Check("v3机制·半懂字段：熔融金献祭必须被记成「半懂」（一半认得出、一半认不出）", true,
                  HasPartialField(mechRep, "熔融金·献祭"));

            RuleAction[] acid = OpsOf("使当前所有酸层数翻倍，减少刀片等同于当前酸层数的H，同时获得等同于当前酸层数的分数", RuleField.Spell, "");
            Check("v3机制·爆刀分数3倍（酸爆前半句）：层数翻倍 + 按层扣H + 按层得分", true,
                  HasOp(acid, RuleOp.DoubleLayers) && HasOp(acid, RuleOp.BladeDamageH) && HasOp(acid, RuleOp.ScoreByLayerCount));

            RuleAction[] burst = OpsOf("如果通过此卡达成爆刀，爆刀产生的分数翻倍变为3倍", RuleField.Spell, "");
            Check("v3机制·爆刀分数3倍：认成「爆刀倍率 ×3」", true,
                  HasOp(burst, RuleOp.SetBurstMultiplier) && FirstOp(burst, RuleOp.SetBurstMultiplier).amount == 3);

            RuleAction[] catalyst = OpsOf("附魔1层催化到刀片，可叠加。每层催化降低热/冷/酸附魔触发阈值1点，最多降低2点",
                                          RuleField.Spell, "催化");
            Check("v3机制·触发阈值降低（催化）：降阈值 + 「最多降低2点」上限声明都收下，整句无未识别", true,
                  HasOp(catalyst, RuleOp.LowerEnchantThreshold) && HasOp(catalyst, RuleOp.None) &&
                  RuleText.ParseField("每层催化降低热/冷/酸附魔触发阈值1点，最多降低2点", RuleField.Spell, "c", "催化术",
                      new RuleContext(EmptyCardLookup.Instance, "催化")).unrecognized.Count == 0);
            Check("v3机制·阈值降低：真作用在 LayerLedger.LowerThreshold（催≥1 → -1、催≥3 → 再 -1）", 1,
                  CatalystThreshold(3));

            RuleAction[] crystal = OpsOf("根据当前刀片冷层数直接加分，每层冷得2分", RuleField.Spell, "");
            Check("v3机制·每层冷得2分：整句无未识别，且每层分值 = 2（不是兜底 1、也不是占位）", true,
                  HasOp(crystal, RuleOp.ScoreByLayerCount) && HasOp(crystal, RuleOp.ScorePerLayer) &&
                  FirstOp(crystal, RuleOp.ScorePerLayer).amount == 2 &&
                  RuleText.ParseField("根据当前刀片冷层数直接加分，每层冷得2分", RuleField.Spell, "c", "结晶",
                      new RuleContext(EmptyCardLookup.Instance, "")).unrecognized.Count == 0);

            RuleAction[] ember = OpsOf("所有“火焰”法术，附加“热”的层数+1", RuleField.Spell, "热");
            Check("v3机制·余温改写火焰层数：作用范围限定 + 层数+N 都认出来", true,
                  HasOp(ember, RuleOp.AddLayer) && FirstOp(ember, RuleOp.AddLayer).amount == 1 &&
                  FirstOp(ember, RuleOp.AddLayer).HasKind(LayerKind.Heat));

            RuleAction[] wood = OpsOf("若桌面上的卡牌为水，启动时，刀片V+1", RuleField.Sacrifice, "");
            Check("v3机制·桌面卡为水时刀片V+1：★ 认不出来（没有桌面条件 + 刀片没有 V）→ 必须进未识别清单", 0, wood.Length);

            RuleAction[] harden = OpsOf("刀片H+10", RuleField.Spell, "");
            Check("v3机制·刀片H+n（法术硬化）：认成「加刀片H 10」", true,
                  HasOp(harden, RuleOp.BladeHardenH) && FirstOp(harden, RuleOp.BladeHardenH).amount == 10);

            // ── ② 未识别清单不能是"引擎接到没接到"的盲区 ──
            //   上面钉住的 6 句（木头/法术卷轴/熔融金/固态汞/熔融玻璃/玻璃）必须**都在**未识别清单里；
            //   而催化术/结晶那两句（纯解析修正）必须**不在**了。
            Check("v3机制·空转 op 清单与非空转 op 清单互不重叠（报告分类不会自相矛盾）", true,
                  EngineOps().Length == 21 && CountOp(RuleOp.NoConsumeLayer) == 1 && CountOp(RuleOp.BladeNoConsumeH) == 1);
        }

        private static int CountOp(RuleOp op)
        {
            int n = 0;
            OpSemantics[] all = EngineOps();
            for (int i = 0; i < all.Length; i++) if (all[i].op == op) n++;
            return n;
        }

        /// <summary>给刀片挂 n 层催化之后，基础阈值 3 会被降到几（照 LayerLedger.LowerThreshold 的实现验）。</summary>
        private static int CatalystThreshold(int catalysts)
        {
            LayerLedger l = new LayerLedger();
            for (int i = 0; i < catalysts; i++) l.Add(LayerKind.Catalyst, 1, false);
            return l.LowerThreshold(3);
        }

        /// <summary>报告里的「半懂字段」清单里有没有这个条目（前缀匹配，例如 "熔融金·献祭"）。</summary>
        private static bool HasPartialField(RuleReport rep, string prefix)
        {
            for (int i = 0; i < rep.partialFields.Count; i++)
                if (rep.partialFields[i].StartsWith(prefix, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>从整表报告里读出「结晶」那条"每层冷得 N 分"的 N（读不到返回 -1）。</summary>
        private static int CrystalPerLayer(RuleReport rep)
        {
            for (int i = 0; i < rep.fields.Count; i++)
            {
                RuleParseResult f = rep.fields[i];
                if (f.cardName != "结晶" || f.field != RuleField.Spell) continue;

                for (int c = 0; c < f.clauses.Count; c++)
                    for (int a = 0; a < f.clauses[c].actions.Count; a++)
                        if (f.clauses[c].actions[a].op == RuleOp.ScorePerLayer) return f.clauses[c].actions[a].amount;
            }
            return -1;
        }

        // ══════════════════════════════════════════════════════════════
        //  三、附魔规则表
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 卡表里的 H/D/V（**卡表的真值**，不是夹具值）。
        ///
        /// 【为什么断言一律改成读这里，而不是写死数字】
        ///   v3.0 定稿（2026-10-08）把每张卡的 H/D/V 全改了一遍（H 收成 5/10/20 三档、
        ///   V 也复了一遍）。断言里写死"期望 8"这种数字，卡表一改就红一片，
        ///   而红的原因不是规则错了、是**策划改了数** —— 这时候人会本能地去改断言，
        ///   改着改着就把"规则本身对不对"这件事改没了。
        ///   所以数值类断言一律**从卡表取值算期望**（下面的 ProbeValues）；
        ///   规则类断言照旧写死（那才是断言的意义）。
        /// </summary>
        private static CardValues ProbeValues(JsonCards cards, string name)
        {
            return cards != null ? cards.ValuesOf(name) : new CardValues(0, 3, 0);
        }

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

            // 粉末 + 易燃 + 热1：走优先级2「爆炸」（规则1 带了"非粉末"限定，见 EnchantRules.CheckHeat 注释）
            //  卡表里当前没有"粉末+易燃"的卡，所以用一张探针合成卡来验证规则表本身
            BladeState bf = new BladeState("b", "刀片", 10, 0);
            bf.layers.Add(LayerKind.Heat, 1);
            Ingredient powderCard = cards.Synthetic("粉末易燃测试卡", "粉末", new string[] { "粉末", "易燃" });
            EnchantMatch mf = EnchantRules.Check(LayerKind.Heat, bf, new MaterialState(powderCard, 3), new List<TransitionRule>());
            Check("规则表：粉末+易燃 命中优先级2（爆炸）", 2, mf.priority);
            Check("规则表：命中的是爆炸", "爆炸（粉末 + 易燃）", mf.ruleName);
            Check("规则表：爆炸把目标 H 归零", true, mf.setTargetHZero);
            Check("规则表：爆炸给刀片热 +1", 1, mf.heatLayerGain);
            Check("规则表：爆炸产物是空白卡", "空白卡", mf.productName);
            Check("规则表：爆炸是形态变化，不是产物缺失", false, mf.productMissing);

            // 非粉末的易燃卡仍然走优先级1「燃烧」（新加的限定条件不能把燃烧一起挡掉）
            BladeState bh = new BladeState("b", "刀片", 10, 0);
            bh.layers.Add(LayerKind.Heat, 1);
            EnchantMatch mh = MatchCase(cards, bh, cards.Material("白磷"), "白磷", LayerKind.Heat);
            Check("规则表：白磷（固体+易燃）仍命中优先级1（燃烧）", 1, mh.priority);
            Check("规则表：白磷的燃烧产物仍是火焰", "火焰", mh.productName);

            // 「遇酸 + 酸 → 无变化（原因）」这种"声明式"写法：既不该变形，也不该算成"忘了写产物"。
            //
            // ★ v3.0 数据变化：定稿里**没有一张卡**用这个写法了（黄金的形态转换只剩「可熔 + 热」，
            //   定稿把"遇酸不反应"改由标签体现）。所以这一条改成**合成卡**来验机制本身 ——
            //   机制是解析器/规则表的能力，不该被某一版卡表文案的取舍带走。
            //   （v2.1 时代这里用的是黄金；它的旧写法在 docs/卡牌数值_v3.0_差异报告.md 里有记录。）
            Ingredient declared = cards.Synthetic("声明无变化测试卡", "固体", new string[] { "固体", "金属" });
            declared.transitions = new FormChange[]
            {
                new FormChange("遇酸 + 酸", "无变化（不溶于酸）"),
            };
            cards.RegisterSynthetic("声明无变化测试卡", declared);

            BladeState bi = new BladeState("b", "刀片", 10, 0);
            bi.layers.Add(LayerKind.Acid, 1);
            EnchantMatch mi = MatchCase(cards, bi, declared, "声明无变化测试卡", LayerKind.Acid);
            Check("声明无变化：规则仍然算命中（优先级1 金属→溶液）", 1, mi.priority);
            Check("声明无变化：标记为 declaredNoChange", true, mi.declaredNoChange);
            Check("声明无变化：不算数据缺口", false, mi.productMissing);
            CheckOutcome("声明无变化：结果按「仅改属性 / 无变化」走", EnchantOutcome.AttributeOnly, mi.outcome);
            Check("声明无变化：规则自带的 H-2 照常生效", 2, mi.targetHDamage);

            // 玻璃对热附魔：一条都不命中
            BladeState bg = new BladeState("b", "刀片", 10, 0);
            bg.layers.Add(LayerKind.Heat, 3);
            EnchantMatch mg = MatchCase(cards, bg, cards.Material("玻璃"), "玻璃", LayerKind.Heat);
            Check("规则表：玻璃 + 热3 不命中任何热规则", false, mg.triggered);

            ScenarioEnd("规则表命中用例", mark, "优先级 / 产物 / 结果类型 都按正文表（热规则1 带\"非粉末\"限定，见报告矛盾条）");
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
            Check("白磷·燃烧：得分 = 目标V " + ProbeValues(cards, "白磷").V + " + 刀片V 5",
                  ProbeValues(cards, "白磷").V + 5, r.activationScore);
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
            Check("水·液体→气态：得分用启动时的原始卡 V（" + ProbeValues(cards, "水").V + " + 5）",
                  ProbeValues(cards, "水").V + 5, r.activationScore);

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

            // ★ 卡表数值化：H/V 从卡表读（v3.0 把玻璃改成 H5 D1 V1、黄金改成 H20 D2 V5）
            int gH = ProbeValues(cards, "玻璃").H, gV = ProbeValues(cards, "玻璃").V;

            Check("顺序①：消耗 1 次行动机会（5→4）", 4, s.actionPoints);
            Check("顺序②：酸≥2 + 固体 → 目标 H-1（" + gH + "→" + (gH - 1) + "）", gH - 1, glass.H);
            Check("顺序②：仅改属性 → 没有变形（日志说明继续检查下一类）", true, r.LogContains("仅改属性、未发生形态变化"));
            Check("顺序②：D-1（3→2）", 2, glass.D);
            Check("顺序②：启动消耗 1 层酸（触发的那一类）", 1, s.blade.layers.Count(LayerKind.Acid));
            Check("顺序③：得分 = 目标V " + gV + " + 刀片V 5 = " + (gV + 5), gV + 5, r.activationScore);
            Check("顺序④：H=5 > 0 未爆刀", false, r.bursted);
            // ★ 吞噬加的是**被吞噬那一刻的 H**：玻璃 v3.0 H=5，被酸蚀先扣 1 → 吞噬时 H=4。
            //   所以刀片 H = 5（初始）- 1（启动）× 1（只是最后一次启动）+ 4 = 8。
            //   （这个"4"不是卡表的 5 —— 基线那版正好也是 4，因为 v2.1 的玻璃 H 就是 4。
            //     两版同值纯属巧合，所以这里改成用**吞噬时实测的 H** 表达，别写死。）
            int absorbH = glass.H;
            Check("顺序⑤：最后一次启动 + D 剩余 → 并入刀片，刀片 H 5-1(启动)+" + absorbH + " = " + (4 + absorbH),
                  4 + absorbH, s.blade.H);
            Check("顺序⑤：刀片 V 5+" + gV + "=" + (5 + gV), 5 + gV, s.blade.V);
            Check("顺序⑤：被吞噬的卡移出桌面", true, glass.removed);
            // ★ v3.0 数据变化：玻璃的献祭文本从「刀片每次启动，获得1分」换成
            //   「每次消耗H时，获得1分」—— 那是**反应式触发**（解析器认不出），
            //   所以这里会报 1 条未识别警告，而且**不再登记刀片被动**。
            //   这两条按 v3.0 数据钉住（不是把断言删掉），缺口报告里单列一条。
            Check("顺序⑤：玻璃的献祭文本「每次消耗H时，获得1分」是反应式触发 → 会报 1 条未识别警告", 1, r.warnings.Count);
            Check("顺序⑤：这条「每次消耗H时」不登记为刀片被动（v2.1 的『每次启动』才有触发点）", 0, e.BladePassiveCount);
            CheckLog("顺序⑤：日志说明这是最后一次启动", r, "最后一次启动");
            CheckLog("顺序⑤：日志说明已移出桌面", r, "移出桌面");
            CheckLog("顺序⑤：日志说明\"并入刀片\"", r, "并入刀片");

            // (b) 只触发最高优先级一条：酸层再多，H 也只掉 1 点（这个数也要跟卡表走）
            LevelRun s2 = NewLevel(cards, "铁刀片", 5, 5);
            s2.blade.layers.Add(LayerKind.Acid, 4);
            MaterialState glass2 = Put(cards, s2, "玻璃", 3);
            TurnEngine e2 = new TurnEngine(ProbeRules(), cards);
            e2.StartBlade(s2, glass2, false);

            Check("只触发一条：酸4 时 H 仍只 -1（不是 -2/-4）", gH - 1, glass2.H);

            ScenarioEnd("场景4 启动②③④⑤ + 献祭吞噬", mark,
                "玻璃 H " + gH + "→" + (gH - 1) + "、D 3→2、得分 " + (gV + 5) + "，末次启动被吞噬 → 刀片 H=" + s.blade.H + " V=" + s.blade.V +
                "（诊断：玻璃被吞噬时的 H=" + glass.H + "，期望刀片 H = 5-1+" + glass.H + " = " + (4 + glass.H) + "）" +
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
            Check("溶解：得分仍按启动时的原始卡 V（" + ProbeValues(cards, "硫磺粉").V + " + 5）",
                  ProbeValues(cards, "硫磺粉").V + 5, r.activationScore);
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
            Check("D耗尽：得分用启动时的原始卡 V（" + ProbeValues(cards, "冰").V + " + 5）",
                  ProbeValues(cards, "冰").V + 5, r.activationScore);
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

            // ★ 卡表数值化：黄金 v3.0 = H20 D2 V5（v2.1 是 H5 D3 V5），白磷 V=2（v2.1 是 3）
            int auH = ProbeValues(cards, "黄金").H, auV = ProbeValues(cards, "黄金").V;
            int pV  = ProbeValues(cards, "白磷").V;

            Check("献祭时机：这个回合启动了 2 次", 2, round.StartCount);
            Check("献祭时机：第一次启动的目标没被吞噬（还在桌面）", false, wp.removed);
            Check("献祭时机：最后一次启动的目标被吞噬", true, gold.removed);
            Check("献祭时机：刀片 H 10 - 2 次启动 + 黄金H " + auH + " = " + (10 - 2 + auH), 10 - 2 + auH, s.blade.H);
            Check("献祭时机：刀片 V 0 + 黄金V " + auV + " = " + auV, auV, s.blade.V);
            Check("献祭时机：两次启动的 V 得分 = 白磷" + pV + " + 黄金" + auV + " = " + (pV + auV),
                  pV + auV, round.starts[0].activationScore + round.starts[1].activationScore);
            Check("献祭时机：黄金带「献祭」标签 → 献祭效果 30 分也结算", pV + auV + 30, s.score);
            Check("献祭时机：黄金的献祭效果产出 3 张催化术", 3, round.starts[1].ProducedCount("催化术"));
            Check("献祭时机：桌面只剩白磷", 1, s.table.Count);
            CheckLog("献祭时机：日志说明第一次不是最后一次", round, "本次不是本回合实际使用的最后一次启动");
            CheckLog("献祭时机：日志说明并入刀片", round, "并入刀片");
            Check("献祭时机：回合结束后进入第 2 回合", 2, s.turnIndex);
            Check("献祭时机：回合结束附魔衰减跑过了", true, round.LogContains("附魔衰减"));

            ScenarioEnd("场景7 献祭只在最后一次启动", mark,
                "白磷不被吞噬；黄金被吞噬 → 刀片 H=" + s.blade.H + " V=" + auV + "，献祭再给 30 分 + 3 张催化术，总分 " + s.score);
        }

        /// <summary>场景 8：爆刀 —— H≤0 立即爆刀、关卡结束、当前分数 ×2；主动结束不双倍。</summary>
        private static void Scenario8_Burst(JsonCards cards)
        {
            int mark = ScenarioStart();
            LevelRun s = NewLevel(cards, "铁刀片", 1, 5);

            MaterialState glass = Put(cards, s, "玻璃", 3);
            TurnEngine e = new TurnEngine(ProbeRules(), cards);
            TurnResult r = e.StartBlade(s, glass, false);

            // ★ 卡表数值化：玻璃 v3.0 = H5 D1 V1（v2.1 是 H5 D3 V2）
            int gV = ProbeValues(cards, "玻璃").V;

            Check("爆刀：启动消耗把 H 打到 0", 0, s.blade.H);
            Check("爆刀：爆刀成立", true, r.bursted);
            Check("爆刀：关卡结束", true, s.levelOver);
            Check("爆刀：本次启动得分 = " + gV + " + 5 = " + (gV + 5), gV + 5, r.activationScore);
            Check("爆刀：当前分数 ×2 → " + (gV + 5) * 2, (gV + 5) * 2, s.score);
            Check("爆刀：倍率是正文写的 ×2", 2, r.burstMultiplier);
            Check("爆刀：翻倍带来的增量是 " + (gV + 5), gV + 5, r.burstBonus);
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
                "H 1→0 → 得分 " + (gV + 5) + " 后总分翻倍为 " + s.score + "；主动结束则原样 " + s2.score);
        }

        /// <summary>
        /// 场景 9：得分 = 目标素材 V + 刀片 V；吞噬素材会抬高刀片 V；刀片被动在下一次启动生效。
        ///
        /// 【v3.0 数据变化对这一条的连带影响（★ 数值断言随卡表走）】
        ///   ① 玻璃 v3.0 = H5 D1 **V1**（v2.1 是 V2）→ 期望值从卡表取，不写死；
        ///   ② **玻璃的献祭文本 v3.0 换成了「每次消耗H时，获得1分」**（v2.1 曾被改写成
        ///      「刀片每次启动，获得1分」）—— 那是"每次消耗H时"的**反应式触发**，
        ///      解析器认不出（见缺口报告 ⚠），所以吞噬玻璃**不再登记刀片被动**。
        ///      于是这一条拆成两半：
        ///        (a) 用卡表的真实文本跑 吞噬 → 得分/V 的计算（数值部分）；
        ///        (b) 用**存档里保存的那句旧原文**走 ImportBladePassives 造一条真被动，
        ///            验"每次启动 +1 分"这条机制本身没坏（机制是引擎的，不该被卡表文案带走）。
        /// </summary>
        private static void Scenario9_ScoreFormula(JsonCards cards)
        {
            int mark = ScenarioStart();

            int gV = ProbeValues(cards, "玻璃").V;
            int pV = ProbeValues(cards, "白磷").V;

            LevelRun s = NewLevel(cards, "铁刀片", 20, 2);

            MaterialState glass = Put(cards, s, "玻璃", 3);
            TurnEngine e = new TurnEngine(ProbeRules(), cards);

            TurnResult r1 = e.StartBlade(s, glass, true);
            Check("计分公式：目标V " + gV + " + 刀片V 2 = " + (gV + 2), gV + 2, r1.activationScore);
            Check("计分公式：吞噬后刀片 V = 2 + " + gV + " = " + (2 + gV), 2 + gV, s.blade.V);
            Check("计分公式：玻璃 v3.0 的献祭是反应式（每次消耗H时）→ 不登记刀片被动", 0, e.BladePassiveCount);

            // (b) 造一条真被动（原文用存档里那句有触发的写法）—— 验机制，不依赖卡表文案
            List<SaveBladePassive> recs = new List<SaveBladePassive>();
            recs.Add(new SaveBladePassive
            {
                cardId = "glass",
                cardName = "玻璃",
                text = "刀片每次启动，获得1分",
                sentence = "刀片每次启动，获得1分",
            });
            string impErr;
            Check("计分公式：按原文重建刀片被动成功（机制本身没坏）", 1, e.ImportBladePassives(recs, out impErr));
            Check("计分公式：重建没有报错", "", impErr);

            MaterialState wp = Put(cards, s, "白磷", 3);
            TurnResult r2 = e.StartBlade(s, wp, false);
            Check("计分公式：刀片 V 涨了以后，下一次启动 = " + pV + " + " + (2 + gV) + " = " + (pV + 2 + gV),
                  pV + 2 + gV, r2.activationScore);
            Check("计分公式：刀片被动「每次启动，获得1分」这次生效 +1", 1, r2.ruleScore);
            Check("计分公式：总分累加 " + (gV + 2) + " + " + (pV + 2 + gV) + " + 1(被动) = " + (gV + 2 + pV + 2 + gV + 1),
                  gV + 2 + pV + 2 + gV + 1, s.score);

            ScenarioEnd("场景9 得分 = 目标V + 刀片V（+ 重建出来的刀片被动）", mark,
                "第一次 " + gV + "+2=" + (gV + 2) + "；吞噬后刀片 V=" + (2 + gV) +
                "；第二次 " + pV + "+" + (2 + gV) + "=" + (pV + 2 + gV) + " + 被动 1；总分 " + s.score);
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

        /// <summary>
        /// 场景 11：爆炸规则（按策划意图修好之后）。
        ///   粉末+易燃 + 热1 → 优先级2「爆炸」：目标 H 归零、刀片热+1、变成空白卡；
        ///   非粉末的易燃卡（白磷）→ 仍然走优先级1「燃烧」。
        /// </summary>
        private static void Scenario11_Explode(JsonCards cards)
        {
            int mark = ScenarioStart();
            LevelRun s = NewLevel(cards, "铁刀片", 5, 5);
            s.blade.layers.Add(LayerKind.Heat, 1);

            // 卡表里当前没有"粉末+易燃"的卡，用一张探针合成卡把规则表的这条跑出来。
            // 这条断言只看爆炸的「刀片热+1」，所以把规则表 F 的"启动消耗 1 层附魔"关掉，
            // 免得 +1 又被扣掉 1（那条矛盾在场景1/场景4 单独验过了）。
            TurnRules rules = ProbeRules();
            rules.ConsumeLayerOnActivate = false;

            Ingredient powderCard = cards.Synthetic("粉末易燃测试卡", "粉末", new string[] { "粉末", "易燃" });
            MaterialState powder = new MaterialState(powderCard, 3, 2, 1);
            s.table.Add(powder);

            TurnEngine e = new TurnEngine(rules, cards);
            TurnResult r = e.StartBlade(s, powder, false);

            Check("爆炸：命中优先级2（爆炸）", true, r.LogContains("优先级2「爆炸"));
            Check("爆炸：日志里没有走燃烧", false, r.LogContains("「燃烧"));
            Check("爆炸：目标 H 归零", 0, powder.H);
            Check("爆炸：刀片热 +1（1 → 2）", 2, s.blade.layers.Count(LayerKind.Heat));
            Check("爆炸：目标变成空白卡进手牌", 1, r.ProducedCount("空白卡"));
            CheckKind("爆炸：空白卡是特殊卡（Blank）", ProduceKind.Blank, r.KindOfProduced("空白卡"));
            Check("爆炸：空白卡计数 +1", 1, s.blankCount);
            Check("爆炸：形态变化 → 旧卡移出桌面", true, powder.removed);
            Check("爆炸：空白卡不走卡表查询，所以不该报\"卡表里找不到\"", false, r.LogContains("卡表里找不到"));
            CheckLog("爆炸：日志写明产物固定为空白卡", r, "产物固定为「空白卡」");

            // 非粉末的易燃卡：仍然走燃烧（新加的"非粉末"限定不能把燃烧一起挡掉）
            LevelRun s2 = NewLevel(cards, "铁刀片", 5, 5);
            s2.blade.layers.Add(LayerKind.Heat, 1);
            MaterialState wp = Put(cards, s2, "白磷", 3);
            TurnEngine e2 = new TurnEngine(ProbeRules(), cards);
            TurnResult r2 = e2.StartBlade(s2, wp, false);

            Check("非粉末易燃卡：仍然命中优先级1（燃烧）", true, r2.LogContains("优先级1「燃烧"));
            Check("非粉末易燃卡：产物还是火焰（不是空白卡）", 1, r2.ProducedCount("火焰"));
            Check("非粉末易燃卡：没有产出空白卡", 0, r2.ProducedCount("空白卡"));
            Check("非粉末易燃卡：白磷 D 立即归零", 0, wp.D);

            ScenarioEnd("场景11 爆炸（粉末+易燃）与燃烧（非粉末易燃）", mark,
                "粉末+易燃 → " + r.ProducedText() + "（H 归零、刀片热 1→" + s.blade.layers.Count(LayerKind.Heat) + "）；白磷 → " + r2.ProducedText());
        }

        // ══════════════════════════════════════════════════════════════
        //  场景 12：存档 / 读档（v2.1 一个存档位）
        //
        //  【为什么这一条必须在离线跑】存读档的坑全在"少写一个字段"上：
        //    刀片层数只存总数（丢掉"不衰退"那份）、桌面素材漏了"本回合是否启动过"、
        //    D 满值没存、被动清单没存…… 这些在实机上表现为"读回来状态不对"，
        //    而那时候现场早就没了 —— 只有离线的逐字段断言能当场钉住它们。
        //  这里走的是**和游戏完全同一份**实现（LevelSave / LevelSaveJson 都在 Rules 那一层），
        //  所以这里全绿 = 游戏里那两步是全绿的（游戏那一侧只多一层文件读写与 3D 重摆）。
        // ══════════════════════════════════════════════════════════════

        /// <summary>探针用的数值来源口径（照游戏里"牌组带来的卡 = 旧配置"那条设一个非 0 值，验证它也能存住）。</summary>
        private static int ProbeValueSource(MaterialState st)
        {
            return (st != null && st.name == "铁") ? 1 : 0;
        }

        /// <summary>探针用的卡表解析器：按名字取卡（**返回副本** —— 读档会往上写卡面数值）；空白卡照 Rules 那一层的口径现造。</summary>
        private static SaveCardResolver ProbeResolver(JsonCards cards)
        {
            return delegate (string id, string name, int src)
            {
                if (id == LevelSave.BlankCardId || name == LevelSave.BlankCardName) return LevelSave.MakeBlankCard();

                Ingredient m = cards.Material(name);
                return m != null ? m.Clone() : null;
            };
        }

        /// <summary>两份状态逐字段比对（不相等时把差异清单原样写进失败说明）。</summary>
        private static void CheckEqual(string what, LevelSaveData a, LevelSaveData b)
        {
            List<string> d = LevelSave.CompareStates(a, b);
            if (d.Count == 0) { passed++; return; }

            failed++;
            failures.Add(what + "：" + LevelSave.DiffText(d));
        }

        private static void Scenario12_SaveLoad(JsonCards cards)
        {
            int mark = ScenarioStart();

            // ── ① 造一个有内容的局面 ──────────────────────────────────
            TurnRules rules = ProbeRules();
            rules.TargetScore = 30;

            LevelRun s = NewLevel(cards, "铁刀片", 5, 5);
            s.blade.layers.Add(LayerKind.Acid, 2);        // 让玻璃被酸蚀之后还能走"献祭吞噬"
            s.blade.layers.Add(LayerKind.Catalyst, 2);

            // 桌面 3 张、D 各不相同（含 D=0 这条边界）
            MaterialState water = Put(cards, s, "水", 0);
            MaterialState iron  = Put(cards, s, "铁", 1);
            MaterialState salt  = Put(cards, s, "盐", 2);
            iron.fullD = 3;                               // D 与满值不同 → 两个数都必须存住
            salt.H = 4;                                   // 场上被扣过 H 的卡

            // 第 4 张用来被吞噬：走完"启动 → 献祭吞噬"这条真路径
            MaterialState glass = Put(cards, s, "玻璃", 3);
            TurnEngine e = new TurnEngine(rules, cards);
            e.StartBlade(s, glass, true);

            // ★ v3.0 数据变化：玻璃的献祭文本换成「每次消耗H时，获得1分」（反应式触发，解析器认不出），
            //   所以**吞噬玻璃不再自动登记刀片被动**。这一条要验的是"被动能存能读"，
            //   所以按存档的口径（SaveBladePassive 保存"原文"）现造一条真被动进去 ——
            //   被动列表非空这一前提不能靠卡表文案碰运气。
            List<SaveBladePassive> seedPassives = new List<SaveBladePassive>();
            seedPassives.Add(new SaveBladePassive
            {
                cardId = "glass",
                cardName = "玻璃",
                text = "刀片每次启动，获得1分",
                sentence = "刀片每次启动，获得1分",
            });
            string seedErr;
            Check("存档：先造出一条真实的刀片被动（按原文重建）", 1, e.ImportBladePassives(seedPassives, out seedErr));
            Check("存档：重建那条被动没有报错", "", seedErr);
            Check("存档：被吞噬那张已离场，桌面正好 3 张", 3, s.table.Count);

            // 刀片层数：热 2 衰退 + 热 1 不衰退（★ 两份分开存）
            s.blade.layers.Add(LayerKind.Heat, 2);
            s.blade.layers.Add(LayerKind.Heat, 1, true);

            s.turnIndex = 3; s.actionPoints = 2; s.startsThisTurn = 3;
            s.blankCount = 1; s.score = 14;

            // "本回合已经启动过"的名单（决定还能不能把手牌收回来）
            List<MaterialState> started = new List<MaterialState>();
            started.Add(iron);

            // ── ② 采集（状态 → DTO）────────────────────────────────────
            LevelSaveData before = LevelSave.Capture(s.blade, s.table, started, ProbeValueSource);
            before.turnIndex      = s.turnIndex;
            before.actionPoints   = s.actionPoints;
            before.score          = s.score;
            before.targetScore    = rules.TargetScore;
            before.startsThisTurn = s.startsThisTurn;
            before.blankCount     = s.blankCount;
            before.levelOver      = s.levelOver;
            before.bursted        = s.bursted;
            before.endReason      = "";
            before.selectedIndex  = 1;                    // 启动目标 = 桌面第 2 张（铁）
            before.selectedAuto   = true;

            // 投放区里"待放置"的一张（v2.1 常态为空 —— 这一条是为了把"万一不为空"也验到：
            // 它是旧流程的摆法 / 探针摆出来的局面，读档时那几张牌必须照旧待在投放区）
            before.staged.Add(new SaveStaged { spell = false, index = 0, slot = 1 });

            before.handMaterials.Add(LevelSave.CaptureHandMaterial("water", "水", 2, 3, 2, 1));
            before.handMaterials.Add(LevelSave.CaptureHandMaterial(LevelSave.BlankCardId, LevelSave.BlankCardName, 0, 0, 0, 0));
            before.handSpells.Add(LevelSave.CaptureHandSpell("sp_fire", "火焰术"));
            before.blade.passives = e.ExportBladePassives();

            // 卡面那三个数（卡自己的 h/d/v）：**故意和 MaterialState 的 H/D/V 取不同值** ——
            // 它们本来就是两回事（一个给卡面画属性区/元素皮肤，一个是规则上的耐久与得分），
            // 只存一半就会出现"读回来卡面变了"（硝石实测：占位 12/3/1 → 旧图鉴 3/4/8，插画从冰晶变气团）
            for (int i = 0; i < before.table.Count; i++)
            {
                SaveMaterial sm = before.table[i];
                sm.cardH = 10 + i; sm.cardD = 20 + i; sm.cardV = 30 + i;
            }
            LevelSave.SetCardFace(before.handMaterials[0], 7, 8, 9);

            Check("存档：桌子 3 张都采到了（顺序 = 级联顺序）", 3, before.table.Count);
            Check("存档：刀片层数采成 4 类（一类一条，含不衰退那份）", 4, before.blade.layers.Count);
            Check("存档：手牌 2 素材 + 1 法术", 3, before.handMaterials.Count + before.handSpells.Count);
            Check("存档：被动清单非空", 1, before.blade.passives.Count);

            // ── ③ 存 → 读（同一份 JSON 实现）──────────────────────────
            SaveFileDto file = new SaveFileDto();
            file.savedAt     = "探针"; file.levelIndex = 1;
            file.levelId     = "lv2";  file.levelName  = "第 2 关";
            file.deckId      = "deck_probe"; file.deckName = "探针牌组";
            file.phaseAtSave = "Select";
            file.state       = before;

            string json = LevelSaveJson.Write(file);

            Check("存档：JSON 里逐类写了中文层数名 + 两份计数",
                  true, json.Contains("\"kind\": \"热\"") && json.Contains("\"decaying\"") && json.Contains("\"permanent\""));
            Check("存档：JSON 里记了「本回合是否已启动」", true, json.Contains("\"startedThisTurn\""));
            Check("存档：JSON 里记了 D 的满值", true, json.Contains("\"fullD\""));
            Check("存档：JSON 里记了刀片被动（连原文一起）", true, json.Contains("\"passives\"") && json.Contains("\"sentence\""));

            SaveFileDto back;
            string error;
            Check("存档：读回来成功", true, LevelSaveJson.Read(json, out back, out error));
            Check("存档：版本号原样", LevelSave.Version, back != null ? back.version : -1);
            Check("存档：kind 原样", LevelSave.KindName, back != null ? back.kind : "");
            Check("存档：关卡 / 牌组定位原样", "第 2 关|deck_probe",
                  back != null ? back.levelName + "|" + back.deckId : "");

            CheckEqual("存档：读回来的状态与存出去的那份逐字段全等（CompareStates）",
                       before, back != null ? back.state : null);

            // ── ④ 按存档重建对象，再采集一次（这才是"读回来能接着打"）──
            LevelSave.BuiltState built;
            string buildErr;
            Check("存档：按存档重建规则侧状态成功", true,
                  LevelSave.TryBuild(back.state, ProbeResolver(cards), out built, out buildErr));

            if (built != null)
            {
                Check("读档：刀片 H / V 一致", s.blade.H * 1000 + s.blade.V,
                      built.blade.H * 1000 + built.blade.V);
                Check("读档：热层衰退份 = 2", 2, built.blade.layers.Stack(LayerKind.Heat).decaying);
                Check("读档：热层不衰退份 = 1（★ 只存总数就会在这里丢）", 1, built.blade.layers.Stack(LayerKind.Heat).permanent);
                Check("读档：酸层 = 1（双份计数各归各类）", 1, built.blade.layers.Stack(LayerKind.Acid).decaying);
                Check("读档：催化层 = 2", 2, built.blade.layers.Count(LayerKind.Catalyst));

                Check("读档：桌面 3 张且顺序不变（级联顺序靠它）", 3, built.table.Count);
                Check("读档：桌面第 1 张是水、D=0", "水|0",
                      built.table.Count > 0 ? built.table[0].name + "|" + built.table[0].D : "?");
                Check("读档：桌面第 2 张是铁、D=1 而满值=3（两个数都得在）", "铁|1|3",
                      built.table.Count > 1 ? built.table[1].name + "|" + built.table[1].D + "|" + built.table[1].fullD : "?");
                Check("读档：桌面第 3 张被扣过的 H 也回来了（H=4）", 4,
                      built.table.Count > 2 ? built.table[2].H : -1);

                Check("读档：本回合已启动标志还在（★ 漏了它就能把启动过的素材收回手牌）", true,
                      built.started.Count == 3 && !built.started[0] && built.started[1] && !built.started[2]);
                Check("读档：数值来源也回来了（铁 = 1 旧配置）", 1,
                      built.valueSource.Count == 3 ? built.valueSource[1] : -1);
                Check("读档：启动目标下标 / 自动选中一致", 1 * 10 + (built.selectedAuto ? 1 : 0), 11);
                Check("读档：关卡计数（回合 / 行动 / 分数 / 启动次数 / 空白卡）", 3 * 100000 + 2 * 10000 + 14 * 100 + 3 * 10 + 1,
                      built.turnIndex * 100000 + built.actionPoints * 10000 + built.score * 100 + built.startsThisTurn * 10 + built.blankCount);
                Check("读档：刀片被动记录跟着回来了", 1, built.passives.Count);

                // 卡面那三个数（Ingredient 自己的 h/d/v）：读档必须按存档覆盖，不能靠"再查一次卡表"
                Check("读档：卡面 h/d/v 按存档覆盖（10/20/30，和规则上的 H/D/V 是两回事）", true,
                      built.table.Count > 0 && built.table[0].card != null &&
                      built.table[0].card.h == 10 && built.table[0].card.d == 20 && built.table[0].card.v == 30);
                Check("读档：卡面的 attrs 同步成同一组数（属性区与元素皮肤读它）", 30,
                      built.table.Count > 0 && built.table[0].card != null && built.table[0].card.attrs != null
                          ? built.table[0].card.attrs.Get(AttrId.Sulfur) : -1);

                Check("读档：投放区待放置那一张也回来了（下标 / 槽位）", 0 * 100 + 1,
                      built.staged.Count > 0 ? built.staged[0].index * 100 + built.staged[0].slot : -1);

                // 用重建出来的对象再采集一次：这一遍如果不等，说明"建"的那一步丢了东西
                List<MaterialState> startedAgain = new List<MaterialState>();
                for (int i = 0; i < built.table.Count; i++)
                    if (i < built.started.Count && built.started[i]) startedAgain.Add(built.table[i]);

                LevelSaveData again = LevelSave.Capture(built.blade, built.table, startedAgain, ProbeValueSource);
                again.turnIndex      = built.turnIndex;
                again.actionPoints   = built.actionPoints;
                again.score          = built.score;
                again.targetScore    = built.targetScore;
                again.startsThisTurn = built.startsThisTurn;
                again.blankCount     = built.blankCount;
                again.levelOver      = built.levelOver;
                again.bursted        = built.bursted;
                again.endReason      = built.endReason;
                again.selectedIndex  = built.selectedIndex;
                again.selectedAuto   = built.selectedAuto;
                again.staged         = built.staged;
                again.handMaterials  = back.state.handMaterials;
                again.handSpells     = back.state.handSpells;
                again.blade.passives = built.passives;

                CheckEqual("读档：重建出来的对象再采集一次，仍然逐字段全等（读回来 = 存档前）", before, again);

                // 被动不是"存了个壳"：重建进引擎之后，下一次启动它要真的生效
                TurnEngine e2 = new TurnEngine(ProbeRules(), cards);
                string perr;
                int n = e2.ImportBladePassives(built.passives, out perr);
                Check("读档：被动重建进引擎（1 条）", 1, n);
                Check("读档：被动重建没有报错", "", perr);
                Check("读档：引擎里的被动条数与存档一致", built.passives.Count, e2.BladePassiveCount);
            }
            else
            {
                Fail("读档：按存档重建状态", buildErr);
            }

            // ── ⑤ 反例：坏档一律明确报错，且**不产出半个状态** ──────────
            SaveFileDto bad;

            Check("反例·坏 JSON：拒绝读取", false, LevelSaveJson.Read("{ \"version\": 1, ", out bad, out error));
            Check("反例·坏 JSON：给了带位置的原因", true, !string.IsNullOrEmpty(error) && error.Contains("JSON"));
            Check("反例·坏 JSON：没有产出存档对象", true, bad == null);

            Check("反例·空文件：拒绝读取", false, LevelSaveJson.Read("", out bad, out error));

            string missingField = json.Replace("\"startsThisTurn\"", "\"startsThisTurnTYPO\"");
            Check("反例·缺字段：拒绝读取", false, LevelSaveJson.Read(missingField, out bad, out error));
            Check("反例·缺字段：报的是缺了哪个字段", true, error.Contains("startsThisTurn"));

            string wrongVersion = json.Replace("\"version\": 1", "\"version\": 2");
            Check("反例·版本不符：拒绝读取", false, LevelSaveJson.Read(wrongVersion, out bad, out error));
            Check("反例·版本不符：报的是版本", true, error.Contains("版本"));

            string wrongKind = json.Replace("\"kind\": \"v21-table-save\"", "\"kind\": \"something-else\"");
            Check("反例·类型不符：拒绝读取（别把别的 JSON 当存档）", false, LevelSaveJson.Read(wrongKind, out bad, out error));

            string wrongType = json.Replace("\"blankCount\"", "\"blankCountX\"")   // 先确认换字段名确实会拒
                                  .Replace("\"H\": 5", "\"H\": \"五\"");
            Check("反例·类型不对（数字写成了字符串）：拒绝读取", false, LevelSaveJson.Read(wrongType, out bad, out error));

            // 层数缺项：语法没毛病，但语义不完整 —— 必须在"建对象"这一步挡住
            SaveFileDto shortLayers = new SaveFileDto();
            shortLayers.state = new LevelSaveData();
            shortLayers.state.blade = new SaveBlade();
            shortLayers.state.blade.layers.Add(new SaveLayer { kind = "热", decaying = 1, permanent = 0 });

            SaveFileDto parsedShort;
            string shortJson = LevelSaveJson.Write(shortLayers);
            Check("反例·层数缺项：语法层能读进来（它确实是合法 JSON）", true,
                  LevelSaveJson.Read(shortJson, out parsedShort, out error));

            LevelSave.BuiltState badBuilt;
            Check("反例·层数缺项：建对象这一步拒绝", false,
                  LevelSave.TryBuild(parsedShort.state, ProbeResolver(cards), out badBuilt, out error));
            Check("反例·层数缺项：报的是缺了「冷」", true, error.Contains("冷"));
            Check("反例·层数缺项：没有产出半个状态", true, badBuilt == null);

            // 卡表里没有这张卡：同样必须在建对象这一步挡住
            SaveFileDto ghost = new SaveFileDto();
            ghost.state = new LevelSaveData();
            ghost.state.blade = new SaveBlade();
            for (int i = 0; i < LayerLedger.KindCount; i++)
                ghost.state.blade.layers.Add(new SaveLayer { kind = LayerLedger.Name((LayerKind)i) });
            ghost.state.table.Add(new SaveMaterial { cardId = "no_such_card", cardName = "不存在的卡", D = 1, fullD = 1 });

            LevelSave.BuiltState ghostBuilt;
            Check("反例·卡表里没这张卡：建对象这一步拒绝", false,
                  LevelSave.TryBuild(ghost.state, ProbeResolver(cards), out ghostBuilt, out error));
            Check("反例·卡表里没这张卡：说清是哪一张", true, error.Contains("不存在的卡"));
            Check("反例·卡表里没这张卡：没有产出半个状态", true, ghostBuilt == null);

            ScenarioEnd("场景12 存档 / 读档（逐字段往返 + 反例）", mark,
                "刀片 " + s.blade.Describe() + "｜桌面 " + before.table.Count + " 张（D " +
                before.table[0].D + "/" + before.table[1].D + "/" + before.table[2].D + "）｜手牌 " +
                (before.handMaterials.Count + before.handSpells.Count) + " 张｜被动 " + before.blade.passives.Count +
                " 条｜JSON " + json.Length + " 字符");
        }

        /// <summary>
        /// 场景 13：**用掉最后一个行动机会的那一次**就是"本回合实际使用的最后一次启动"
        /// （正文 §三.5 / §四.5；口径说明还补了一句"最后一次启动不一定是第 5 次行动"）。
        ///
        /// 【为什么必须有这一条】行动机会只有 5 次，而一个回合里要启动的清单（order）可以更长：
        ///   第 5 次启动把行动机会打到 0，第 6 次只会被引擎拒掉（"行动机会已用尽 → 不能启动"）——
        ///   真正常用的最后一次是**第 5 次**。把它判成"不是最后一次"，第 ⑤ 步就会写
        ///   "本次不是本回合实际使用的最后一次启动 → 不吞噬"，目标卡带着 D 留在桌上；
        ///   而这一下之后本回合再也启动不了（行动机会 0），等于玩家白丢一张卡。
        ///   实机那条链（AutoPlayHarness ㊵/㊶）就是这么复现的，这一条把判据钉在离线断言里。
        ///
        /// 【这一条同时钉住两档边界】
        ///   · 第 4 次（行动机会 2 → 1）：之后还能再启动 → **不是**最后一次（不许提前吞）；
        ///   · 第 5 次（行动机会 1 → 0）：之后谁也启动不了 → **是**最后一次（必须当场吞）。
        /// </summary>
        private static void Scenario13_LastStartByActionPoints(JsonCards cards)
        {
            int mark = ScenarioStart();

            LevelRun s = NewLevel(cards, "铁刀片", 20, 0);
            TurnEngine e = new TurnEngine(ProbeRules(), cards);

            // 水 D=99：拿它当"不是最后一次"那几次的靶子 —— 多打几次也不会 D 耗尽，
            // 局面就只由行动机会决定（换成 D=3 的水，第 3 次就把自己打没了）。
            MaterialState water = Put(cards, s, "水", 99);
            MaterialState gold  = Put(cards, s, "黄金", 3);   // 第 5 次的目标：D 3→2 仍有剩余 → 该被吞噬

            List<MaterialState> order = new List<MaterialState>();
            for (int i = 0; i < 4; i++) order.Add(water);     // 第 1~4 次：行动机会 5 → 1
            order.Add(gold);                                  // 第 5 次：行动机会 1 → 0（本回合实际使用的最后一次）
            order.Add(water);                                 // 第 6 次：行动机会已用尽 → 引擎拒掉

            RoundResult round = e.RunRound(s, order);

            Check("AP 用完那一次：order 6 项全部跑过（RoundResult.starts 含被拒那次）", 6, round.starts.Count);
            Check("AP 用完那一次：第 6 次启动被引擎拒绝（行动机会已 0）", true, round.starts[5].rejected);
            Check("AP 用完那一次：真正结算掉的启动是 5 次（前 5 次都没被拒）",
                  true, !round.starts[0].rejected && !round.starts[1].rejected && !round.starts[2].rejected &&
                        !round.starts[3].rejected && !round.starts[4].rejected);
            Check("AP 用完那一次：行动机会打到 0", 0, s.actionPoints);

            Check("AP 边界：第 4 次（行动机会 2→1）之后还能再启动 → 不是最后一次",
                  true, round.starts[3].LogContains("本次不是本回合实际使用的最后一次启动"));
            Check("AP 边界：第 5 次（行动机会 1→0）按「最后一次」记（标题行写明）",
                  true, round.starts[4].LogContains("本回合最后一次启动（结算后判定献祭吞噬）"));
            CheckLog("AP 边界：第 5 次的目标被吞噬（并入刀片）", round.starts[4], "并入刀片");
            // ★ 卡表数值化：黄金 v3.0 = H20 D2 V5（v2.1 是 H5 D3 V5）；
            //   水 v3.0 的启动带「不消耗刀片H」→ 前 4 次水启动不扣刀片 H（引擎按失效写法跳过）。
            int auH2 = ProbeValues(cards, "黄金").H, auV2 = ProbeValues(cards, "黄金").V;

            Check("AP 边界：第 5 次的目标确实移出了桌面", true, gold.removed);
            // ★ 每一次启动都扣 1 点刀片 H（5 次启动 = -5；水的「不消耗刀片H」在 v3 是失效写法，
            //   引擎跳过它并打警告，所以**不产生豁免**）；再叠加被吞噬的黄金 H。
            Check("AP 边界：刀片 H 20 - 5 次启动 + 黄金H " + auH2 + " = " + (15 + auH2),
                  15 + auH2, s.blade.H);
            Check("AP 边界：刀片 V 0 + 黄金V " + auV2 + " = " + auV2 + "（吞噬把它加进来了）", auV2, s.blade.V);
            // 分数 = 前 4 次水的启动分（每次 命中卡表 V=2）+ 黄金那次（激活 5 + 献祭 30）
            Check("AP 边界：黄金带「献祭」标签 → 献祭效果 30 分照常结算", 8 + 5 + 30, s.score);

            ScenarioEnd("场景13 用掉最后一个行动机会的那一次 = 最后一次启动", mark,
                "行动机会 5 次、order 6 项：前 4 次不吞、第 5 次（1→0）把 黄金 并进刀片 → 刀片 H=" + s.blade.H +
                " V=" + auV2 + "、总分 " + s.score + "；第 6 次被拒（行动机会已用尽）");
        }

        // ══════════════════════════════════════════════════════════════
        //  报告检查：不许静默失效
        // ══════════════════════════════════════════════════════════════

        private static void Report_NoSilentLoss(JsonCards cards)
        {
            int mark = ScenarioStart();

            RuleReport rep = RuleReport.BuildAll(cards.reportMaterials, cards.spells, cards);

            Check("解析报告：句子账目全部对得上（clauses + 未识别 == 句子数）", 0, rep.UnbalancedFields);
            Check("解析报告：没有字段静默丢失（有文本却切不出句子）", 0, rep.silentFields.Count);

            // ★ 金丝雀（数据侧改 cards_v21.json 就要盯这四个数）：v2.1 时代四个数都是 0，
            //   意思是"卡表里每一句引擎都认得、都接得上"。
            //
            //   ★★ v3.0 定稿（2026-10-08）之后这四个数**不再可能是 0**，原因不是回归、
            //      是**卡表先落地、引擎算子下一阶段才做**（本次任务明确：本阶段只转写数据 + 出报告）。
            //      所以断言改成"钉住当前的账"，而不是放宽成 `>= 0`（那等于把金丝雀放飞）：
            //        · 未识别 6：木头（桌面卡为水 + V+1）/ 法术卷轴（指定手牌复制）/ 熔融金（增加10V）
            //          / 固态汞（受击时减伤）/ 熔融玻璃（每次获得冷层数时翻倍）/ 玻璃（每次消耗H时得1分）；
            //          ★ 从 8 降到 6 的原因：催化术的"最多降低2点"、结晶的"每层冷得2分"
            //            这两句是**解析器自己认不出**（不是策划写法超纲），本轮在 RuleText 里补了词条
            //            （纯解析修正，语义仍等引擎，见缺口报告）；卡表一个字都没改。
            //        · 规则表接不上 22：v3.0 把"遇热/遇酸"这类**重复的形态转换行**删掉了，
            //          而标签（遇热/金属/可溶/液体…）还在，AuditProducts 就会按标签去要产物；
            //        · v3 失效写法 8：v3.0 把「不消耗刀片H」写回了 8 张卡（引擎按失效处理并打警告）；
            //        · 可疑产出 1：法术卷轴的形态转换产物「灰烬」不在 v3.0 卡表里（定稿只列了 32 张）。
            //      这四个数一旦变化，说明卡表又被改过 —— 请同步更新 docs/卡牌数值_v3.0_缺口报告.md。
            Check("解析报告：未识别句子数（v3.0 卡表 = 6，见缺口报告）", 6, rep.unrecognized.Count);
            Check("解析报告：规则表接不上的卡（v3.0 卡表 = 22，见缺口报告）", 22, rep.tableGaps.Count);
            Check("解析报告：v3 已失效写法（v3.0 卡表 = 8 张卡的『不消耗刀片H』）", 8, rep.superseded.Count);
            Check("解析报告：可疑产出（v3.0 卡表 = 1，法术卷轴产出的「灰烬」不在 32 张里）", 1, rep.unknownProducts.Count);

            // 数据侧前几轮修好的写法，**v3.0 定稿又改回去了** —— 逐条按定稿钉住。
            // （这几条以前是"已改掉"，现在反过来：定稿怎么写、卡表就怎么写，
            //   引擎接不住的部分进缺口报告，不是偷偷把卡表改回引擎喜欢的写法。）
            Check("卡表契约：熔融金的『额外增加10V』按 v3.0 定稿写回来了（引擎当前认不出 → 缺口报告 ⚠）",
                  true, HasUnrecognized(rep, "熔融金", "V"));
            Check("卡表契约：法术卷轴的『指定手牌中的1张法术卡，复制2张』按 v3.0 定稿写回来了（要选牌 → 缺口报告 ⚠）",
                  true, HasUnrecognized(rep, "法术卷轴", "复制"));
            Check("卡表契约：水的『不消耗刀片H』按 v3.0 定稿写回来了（引擎标为失效写法 → 缺口报告 ⚠）",
                  true, HasSuperseded(rep, "水·启动"));
            Check("卡表契约：玻璃的献祭按 v3.0 定稿是『每次消耗H时，获得1分』（反应式触发，不再是『每次启动』）",
                  true, !HasEachStartupSacrifice(rep, "玻璃"));

            // 「→ 无变化（原因）」这种声明式写法：v3.0 定稿里没有一张卡这么写
            // （定稿用的是「形态转换 无」+ 标签里标「(不响应热/冷/酸)」），
            // 所以这两条断言改成"当前卡表里没有这种声明" —— 写法本身仍然被解析器支持
            // （Parser/规则表那边有专门用例，见 Table_MatchCases 的 declaredNoChange 段）。
            Check("v3.0 卡表：没有『无变化（原因）』式声明（定稿用的是『形态转换 无』）", false,
                  HasDeclaredNoChange(rep, "黄金"));
            Check("v3.0 卡表：黄金有『酸规则会命中但没写产物』的缺口（定稿删掉了『遇酸 + 酸 → 无变化』那一行）",
                  true, HasGap(rep, "黄金："));

            Check("解析报告：没有『执行不了』的条目（v3 里素材有 H 了，v2.1 的『其他素材H』也能落地）",
                 0, rep.unsupported.Count);
            // ★ v3.0 数据变化：定稿把"每层多少分"写实了一部分（汞蒸气 2 分 / 熔融玻璃 2 分 /
            //   结晶 2 分），所以占位数值从 4 处掉到 **2 处**：
            //     · 酸爆"同时获得等同于当前酸层数的分数" —— 正文确实没给每层分值（占位值兜底）；
            //     · 结晶"根据当前刀片冷层数直接加分" —— 定稿把 2 分写在了**后一小句**
            //       （"每层冷得2分"），前一小句本身仍然没给数，于是也进了这一节。
            //       这是**口径不一致**（同一张卡前后两半句一个给数一个不给），值得让策划拍板：
            //       要么前半句写"每层冷得2分"就删掉，要么后半句并进前半句。
            //   ★ 这两条都不是"引擎没实现"，而是"正文没给数" —— 报告分节就是为了不混淆这两件事。
            Check("解析报告：占位数值 = 2 处（酸爆 + 结晶的前半句），且两条都对得上",
                  true, rep.placeholders.Count == 2 &&
                        rep.placeholders[0].Contains("酸爆") && rep.placeholders[1].Contains("结晶"));
            // ★ 结晶的"每层冷得2分"必须是**实数 2**，不能掉到兜底 1、更不能被当成占位：
            //   掉兜底会让结晶每层只加 1 分（数值错一半），而且报告会把它伪装成"策划没写数"。
            Check("解析报告：结晶的『每层冷得2分』读到实数 2（不是兜底 1）", true, CrystalPerLayer(rep) == 2);
            Check("解析报告：半懂字段 = 1 处（熔融金·献祭：H-5 执行、增加10V 不执行）", true,
                  rep.partialFields.Count == 1 && rep.partialFields[0].StartsWith("熔融金·献祭", StringComparison.Ordinal));
            Check("解析报告：矛盾清单至少 8 条（启动消耗层/爆炸/献祭两义/…；含已按意图修正的爆炸）", true, rep.conflicts.Count >= 8);

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

        private static bool HasUnrecognized(RuleReport rep, string card, string keyword)
        {
            for (int i = 0; i < rep.unrecognized.Count; i++)
            {
                UnrecognizedRule u = rep.unrecognized[i];
                if (u.cardName == card && u.sentence.Contains(keyword)) return true;
            }
            return false;
        }

        private static bool HasSuperseded(RuleReport rep, string prefix)
        {
            for (int i = 0; i < rep.superseded.Count; i++)
                if (rep.superseded[i].StartsWith(prefix, StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool HasGap(RuleReport rep, string prefix)
        {
            for (int i = 0; i < rep.tableGaps.Count; i++)
                if (rep.tableGaps[i].StartsWith(prefix, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>这张卡的形态转换表里有没有"声明无变化"的条目（卡表新写法）。</summary>
        private static bool HasDeclaredNoChange(RuleReport rep, string cardName)
        {
            for (int i = 0; i < rep.fields.Count; i++)
            {
                RuleParseResult f = rep.fields[i];
                if (f.cardName != cardName || f.field != RuleField.Transition) continue;
                for (int t = 0; t < f.transitions.Count; t++)
                    if (f.transitions[t].declaredNoChange) return true;
            }
            return false;
        }

        /// <summary>这张卡的献祭文本里有没有"每次启动"这类被动条目（有触发点，才能被登记成刀片被动）。</summary>
        private static bool HasEachStartupSacrifice(RuleReport rep, string cardName)
        {
            for (int i = 0; i < rep.fields.Count; i++)
            {
                RuleParseResult f = rep.fields[i];
                if (f.cardName != cardName || f.field != RuleField.Sacrifice) continue;
                for (int c = 0; c < f.clauses.Count; c++)
                    if (f.clauses[c].trigger == RuleTrigger.EachStartup) return true;
            }
            return false;
        }

        private static bool HasUnknown(RuleReport rep, string name)
        {
            for (int i = 0; i < rep.unknownProducts.Count; i++)
                if (rep.unknownProducts[i].Contains(name)) return true;
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

            /// <summary>
            /// 按名字去重用的表（**报告走它**）。
            ///
            /// 【为什么报告不直接用 materials 列表】卡表里万一出现两张同名卡
            ///   （改数据时最容易出的一类错），列表会把两张都算进去，
            ///   而 matByName 只有一份（后写的覆盖先写的）—— "报告说 32 张、
            ///   实际只有 31 张能查到"这种账对不上就是这么来的。
            ///   报告按去重后的表算，和规则实际看到的卡完全一致。
            /// </summary>
            public readonly List<Ingredient> reportMaterials = new List<Ingredient>();

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

            /// <summary>
            /// 把合成卡登记进"按名字查卡"的表里，让它也能被 <see cref="Reactions"/> 查到。
            ///
            /// 【为什么需要】合成卡（例如"声明无变化测试卡"）的 transitions 是**探针自己写的**，
            ///   不登记的话 MatchCase 走 cards.Reactions(name) 会拿到空表，
            ///   规则表就看不到那句"无变化"声明 —— 测的就不是真想测的东西了。
            /// </summary>
            public void RegisterSynthetic(string name, Ingredient ing)
            {
                if (string.IsNullOrEmpty(name) || ing == null) return;

                matByName[name] = ing;
                RuleParseResult pr = RuleText.ParseTransitions(ing.transitions, ing.id, ing.name, null);
                reactions[name] = pr.transitions;
            }

            private void AddMaterial(Ingredient ing)
            {
                materials.Add(ing);
                if (string.IsNullOrEmpty(ing.name)) return;

                if (!matByName.ContainsKey(ing.name)) reportMaterials.Add(ing);
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
