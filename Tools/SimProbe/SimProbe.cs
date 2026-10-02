using System;
using System.Collections.Generic;
using GameJam.Data;
using GameJam.Sim;
using UnityEngine;

/// <summary>
/// CupSim 的独立验证程序。
///
/// 【为什么能单独跑】
/// CupSim 是纯逻辑，不碰渲染也不继承 MonoBehaviour，只用到 Vector2 / Mathf /
/// Random 这些 UnityEngine 的**数学类型**（在 CoreModule 里，不需要编辑器）。
/// 所以可以把它连同数据层一起编成一个控制台程序直接跑 ——
/// 不用占 Unity 的工程锁，也不用等编辑器启动。
///
/// 规格里那些"每 1 秒一次""固定 5 秒""飘字要限量""硬度归零爆刀"
/// 全是可以用断言卡住的规则，光靠看截图是验不出来的。
/// </summary>
static class SimProbe
{
    static int fails;

    static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  [PASS] " : "  [FAIL] ") + what);
        if (!ok) fails++;
    }

    static Ingredient Make(string id, string name, int h, int d, int v)
    {
        AttrSet a = new AttrSet();
        a.Set(AttrId.Salt, h);
        a.Set(AttrId.Mercury, d);
        a.Set(AttrId.Sulfur, v);
        return new Ingredient(id, name, a);
    }

    /// <summary>造一个"杯里已经有三样食材"的回合状态。</summary>
    static TurnState MakeTurn()
    {
        TurnState t = new TurnState();
        t.blade = Make("alien_alloy", "外星合金", 20, 0, 0);   // 硬度 20
        t.cup.Add(Make("water", "水", 1, 9, 1));
        t.cup.Add(Make("saltpeter", "硝石", 3, 4, 8));
        t.cup.Add(Make("sulfur", "硫磺", 2, 1, 9));
        return t;
    }

    const float DT = 1f / 60f;

    static void Main()
    {
        Console.WriteLine("=== CupSim 规格校验 ===");

        // ── ① 固定 5 秒 ──
        CupSim sim = new CupSim();
        sim.Begin(MakeTurn());

        Check(Math.Abs(CupSim.Duration - 5f) < 0.001f, "模拟时长常量为 5 秒");

        float t = 0f;
        while (t < 4.9f) { sim.Tick(DT); t += DT; }
        Check(!sim.Finished, "跑到 4.9 秒时还没结束");

        while (t < 5.2f) { sim.Tick(DT); t += DT; }
        Check(sim.Finished, "跑到 5.2 秒时已经结束");

        int afterFinish = sim.roundScore;
        for (int i = 0; i < 120; i++) sim.Tick(DT);
        Check(sim.roundScore == afterFinish, "结束之后不再加分（Ticked 额外 2 秒）");

        // ── ② 粒子生成与约束 ──
        CupSim a = new CupSim();
        a.Begin(MakeTurn());
        Check(a.particles.Count == 3, "三样食材变成三颗粒子");

        bool inside = true;
        for (int step = 0; step < 600; step++)
        {
            a.Tick(DT);
            for (int i = 0; i < a.particles.Count; i++)
            {
                SimParticle p = a.particles[i];
                float dist = (p.pos - CupSim.Center).magnitude;
                if (dist > CupSim.CupRadius - p.radius + 0.0005f) inside = false;
                if (float.IsNaN(p.pos.x) || float.IsNaN(p.pos.y)) inside = false;
            }
        }
        Check(inside, "10 秒里没有任何粒子跑出杯壁，也没有 NaN");

        // ── ③ 每 1 秒一次反应 ──
        // 水(1,9,1)+硝石(3,4,8)+硫磺(2,1,9)：汞性合计 14、盐性合计 6
        // → 「乳化」阈值(汞8/盐5)第一秒就该满足
        CupSim b = new CupSim();
        b.Begin(MakeTurn());

        int emulsifyAt1 = 0;
        // ★ 别写成"60 帧 × 1/60"：浮点累加出来是 0.9999999，
        //   差一点点没到 1.0，反应还没触发 —— 测试会假失败。
        //   定时器上差一个 tick 是家常便饭，这里直接推到阈值过掉为止。
        while (b.elapsed < 1.0f) b.Tick(DT);

        for (int i = 0; i < b.reactionLog.Count; i++)
            if (b.reactionLog[i] == "乳化") emulsifyAt1++;
        Check(emulsifyAt1 == 1, "第 1 秒恰好触发一次「乳化」（实际 " + emulsifyAt1 + " 次）");

        while (!b.Finished) b.Tick(DT);

        int emulsifyTotal = 0, meltTotal = 0;
        for (int i = 0; i < b.reactionLog.Count; i++)
        {
            if (b.reactionLog[i] == "乳化") emulsifyTotal++;
            if (b.reactionLog[i] == "融化") meltTotal++;
        }

        // 5 秒 → 5 次结算；第 5 次温度才到 65，所以融化只在最后几次出现
        Check(emulsifyTotal == 5, "整场触发 5 次「乳化」（5 秒 × 每 1 秒一次，实际 " + emulsifyTotal + " 次）");
        Check(meltTotal >= 1, "温度升上去之后触发过「融化」（实际 " + meltTotal + " 次）");
        Check(b.roundScore > 0, "本回合得分 > 0（实际 " + b.roundScore + "）");

        // ── ④ 飘字限量 + 会消失 ──
        CupSim c = new CupSim();
        c.Begin(MakeTurn());

        int maxSeen = 0;
        for (int step = 0; step < 360; step++)
        {
            c.Tick(DT);
            if (c.floaters.Count > maxSeen) maxSeen = c.floaters.Count;
        }
        Check(maxSeen <= CupSim.MaxFloaters,
              "飘字数量从没超过上限 " + CupSim.MaxFloaters + "（实际峰值 " + maxSeen + "）");

        // 停一会儿，飘字应该全消失
        for (int step = 0; step < 240; step++) c.Tick(DT);
        Check(c.floaters.Count == 0, "停下 4 秒后飘字全部消失（剩 " + c.floaters.Count + " 条）");

        c.ClearFloaters();
        Check(c.floaters.Count == 0, "ClearFloaters 能清空");

        // ── ⑤ 刀片磨损与爆刀 ──
        CupSim d = new CupSim();
        TurnState soft = new TurnState();
        soft.blade = Make("soft", "软刀", 4, 0, 0);       // 硬度只有 4
        soft.cup.Add(Make("water", "水", 1, 9, 1));
        d.Begin(soft);

        // ★ 这里**只能有一个**推进循环。
        //   上一版我加新循环时忘了删旧的 while (!d.Finished) d.Tick(DT);
        //   结果新循环启动时模拟早就结束了，循环体一次都没进，
        //   angleWhenBroken 一直是哨兵值 -1，报出来的失败是假的。
        float angleWhenBroken = -1f;
        while (!d.Finished)
        {
            d.Tick(DT);
            if (d.bladeBroken && angleWhenBroken < 0f) angleWhenBroken = d.bladeAngle;
        }

        Check(d.bladeBroken, "硬度 4 的刀会在 5 秒内爆掉");
        Check(Math.Abs(d.bladeHardness) < 0.001f, "爆刀之后硬度停在 0，不会变成负数");

        // 断掉之后刀片应该**停在原地**，不是归零 ——
        // 归零会让刀片瞬间跳回水平位置，看着像闪了一下。
        Check(angleWhenBroken >= 0f && Math.Abs(d.bladeAngle - angleWhenBroken) < 0.001f,
              "爆刀之后刀片冻结在断裂时的角度（" + angleWhenBroken.ToString("0.0") + "°）");

        // ── ⑥ 跨回合保留 ──
        CupSim e = new CupSim();
        TurnState turn = MakeTurn();
        e.Begin(turn);
        while (!e.Finished) e.Tick(DT);

        int p1 = e.particles.Count;
        float tempAfterTurn1 = e.temp;
        e.roundScore = 0;
        e.reactionLog.Clear();

        // 第二回合：只加一样新食材
        turn.cup.Clear();
        turn.cup.Add(Make("clay", "黏土", 6, 5, 1));
        e.Begin(turn);

        Check(e.particles.Count == p1 + 1,
              "第二回合粒子是累加的（" + p1 + " → " + e.particles.Count + "），不是重建");
        Check(Math.Abs(e.temp - tempAfterTurn1) < 0.001f, "杯温跨回合保留（" + e.temp.ToString("0.0") + "°）");
        Check(e.roundScore == 0, "新回合的本回合得分从 0 开始");
        Check(e.totalScore > 0, "总得分继续累积（" + e.totalScore + "）");

        Console.WriteLine();
        Console.WriteLine(fails == 0 ? "全部通过" : (fails + " 项不通过"));
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
