using System;
using System.Collections.Generic;
using UnityEngine;
using GameJam.Data;

namespace GameJam.Sim
{
    /// <summary>一个飘字。位置用杯内归一化坐标（0..1），画的时候再映射到屏幕。</summary>
    public class SimFloater
    {
        public string text;
        public Vector2 pos;
        public float age;
        public float life;
        public Color color;
        public bool big;
    }

    /// <summary>一颗食材粒子。</summary>
    public class SimParticle
    {
        public string name;
        public string id;

        /// <summary>杯内位置，归一化 0..1</summary>
        public Vector2 pos;

        /// <summary>归一化速度（每秒走多少个杯宽）</summary>
        public Vector2 vel;

        /// <summary>归一化半径</summary>
        public float radius;

        /// <summary>成分（来自食材本身）</summary>
        public AttrSet attrs;

        /// <summary>当前温度 —— 跟着杯温走</summary>
        public float temp;

        /// <summary>当前硬度 —— 会被刀片磨掉</summary>
        public float hardness;

        /// <summary>已经被磨掉多少（0..1），用来画粒子变淡 / 变小</summary>
        public float wear;
    }

    /// <summary>
    /// 一条反应规则。
    ///
    /// 【为什么是"阈值与"而不是一个函数】
    /// 策划要能自己调。写成 Func&lt;&gt; 的话规则就焊死在代码里了，
    /// 而这一版策划还在定"先做哪个反应"。
    /// 阈值与能覆盖绝大多数情况（比如"汞性够高 且 盐性够高 → 乳化"），
    /// 而且整条规则就是一串数字，将来挪进配置表直接就能用。
    /// 真需要更复杂的组合时，再在这里加一个 allOf 列表即可。
    /// </summary>
    [Serializable]
    public class ReactionDef
    {
        public string id;

        /// <summary>反应名，例如「乳化」</summary>
        public string name;

        /// <summary>飘字文案，例如「乳化！+50」。留空则按 name + 得分自动拼。</summary>
        public string floatText;

        /// <summary>这次反应得多少分</summary>
        public int score;

        /// <summary>条件一：哪一项属性</summary>
        public AttrId attrA = AttrId.Mercury;

        /// <summary>条件一：杯内该项加起来要到多少</summary>
        public int thresholdA = 8;

        /// <summary>条件二</summary>
        public AttrId attrB = AttrId.Salt;
        public int thresholdB = 5;

        /// <summary>温度下限（0 表示不看温度）</summary>
        public float minTemp;

        /// <summary>一行说明，界面上写"为什么触发了"</summary>
        public string hint;

        public ReactionDef() { id = ""; name = ""; floatText = ""; hint = ""; }

        public ReactionDef(string id, string name, int score,
                           AttrId a, int ta, AttrId b, int tb,
                           float minTemp, string floatText, string hint)
        {
            this.id = id; this.name = name; this.score = score;
            this.attrA = a; this.thresholdA = ta;
            this.attrB = b; this.thresholdB = tb;
            this.minTemp = minTemp;
            this.floatText = floatText;
            this.hint = hint;
        }

        /// <summary>文案：没配就自动拼，免得手写文案和数值脱节。</summary>
        public string Text()
        {
            if (!string.IsNullOrEmpty(floatText)) return floatText;
            return name + "！+" + score;
        }
    }

    /// <summary>
    /// 第一版先做哪几个反应 —— 这是**占位数值**。
    ///
    /// ★ 规格里"需要策划提供"的三项（先做哪 1~2 个状态变化、先做哪个反应、
    ///   得分数值）还没有定论，所以下面这两条是按规格给的飘字样例
    ///   （「融化！」「乳化！+50」）反推的占位规则，数值全是可以直接改的。
    ///   等策划给了真值，改这里就行，不用动 CupSim。
    /// </summary>
    public static class ReactionCatalog
    {
        private static List<ReactionDef> cached;

        public static List<ReactionDef> All()
        {
            if (cached != null) return cached;

            cached = new List<ReactionDef>();

            // 乳化：汞性（液体倾向）和盐性（固体倾向）都够高 ——
            // 一相分散到另一相里，正是破壁机干的事
            cached.Add(new ReactionDef(
                "emulsify", "乳化", 50,
                AttrId.Mercury, 8, AttrId.Salt, 5, 0f,
                "乳化！+50",
                "汞性 ≥ 8 且 盐性 ≥ 5"));

            // 融化：温度够高就把固体倾向化掉
            cached.Add(new ReactionDef(
                "melt", "融化", 20,
                AttrId.Salt, 4, AttrId.Sulfur, 0, 60f,
                "融化！+20",
                "温度 ≥ 60 且 盐性 ≥ 4"));

            // ★ 只留这两条。
            //   一开始还写了第三条「研磨」（杯里有东西就给 10 分），
            //   结果它每秒都和另外两条一起触发，飘字一秒刷三条 ——
            //   而规格给的飘字样例本来就只有「融化！」「乳化！+50」两个。
            //   没触发任何反应的那些秒，用一条"研磨中…"的提示兜底就够了。

            return cached;
        }

        public static void Reload() { cached = null; }
    }

    /// <summary>
    /// 杯内模拟。
    ///
    /// 【它是纯逻辑，不画任何东西】
    /// 粒子、温度、刀片磨损、反应判定全在这里；怎么画是 CupSimView 的事。
    /// 分开的理由很实际：这一堆规则是策划要反复调的，
    /// 而渲染是美术的事 —— 混在一起的话，调一个数值也要先看懂半屏画法。
    ///
    /// 【坐标】
    /// 杯内一律用归一化坐标：中心 (0.5, 0.5)，内壁半径 CupRadius。
    /// 这样换分辨率、换面板尺寸都不用改这里的任何数字。
    ///
    /// 【生命周期】
    /// 一关之内只有一份（挂在 TableTurnLoop 上），跨回合保留 ——
    /// 规格要求"粒子属性、刀片属性保留到下一回合"。
    /// 每次投放只是往里加粒子，不是重建。
    /// </summary>
    public class CupSim
    {
        // ── 规格给的固定值 ────────────────────────────────────────────
        /// <summary>模拟固定跑 5 秒</summary>
        public const float Duration = 5f;

        /// <summary>每 1 秒发生一次反应</summary>
        public const float ReactionInterval = 1f;

        /// <summary>飘字同时最多几条 —— 规格要求限制数量</summary>
        public const int MaxFloaters = 8;

        // ── 杯内几何 ──────────────────────────────────────────────────
        public static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
        public const float CupRadius = 0.44f;
        public const float ParticleRadius = 0.055f;

        // ── 运动参数（都是"手感值"，可调）────────────────────────────
        private const float BladePush = 0.55f;    // 刀片往外甩的力度
        private const float Repel = 1.6f;         // 粒子互斥
        private const float Damping = 2.2f;       // 阻尼，免得越甩越快

        // ── 运行状态 ──────────────────────────────────────────────────
        public readonly List<SimParticle> particles = new List<SimParticle>();
        public readonly List<SimFloater> floaters = new List<SimFloater>();
        public readonly List<string> reactionLog = new List<string>();

        public float elapsed;
        public float temp;              // 杯温，**跨回合保留**
        public float bladeAngle;        // 刀片角度（度）
        public float bladeSpeed = 60f;  // 度/秒。规格：默认 60，将来接变速模块
        public float bladeHardness;     // 刀片硬度，**跨回合保留**
        public bool  bladeBroken;
        public int   roundScore;        // 本回合得分
        public int   totalScore;        // 本关累计

        private float nextReactionAt;
        private bool  initialized;

        // ── 随机源 ────────────────────────────────────────────────────
        //
        // ★ 故意**不用 UnityEngine.Random**。
        //   两个原因，第二个才是要命的：
        //   1. 它是引擎的内部调用（ECall），离开 Unity 进程直接抛
        //      "ECall methods must be packaged into a system module" ——
        //      模拟就没法单独跑测试了。
        //   2. 它没有种子，**结果不可复现**。同一手牌两次跑出不同分数，
        //      玩家说"我明明这么打却没过"时根本没法回放。
        //   xorshift32 只要几行，还能固定种子复现。
        private uint rngState = 0x9E3779B9;

        /// <summary>固定随机种子。传 0 会被换成 1（xorshift 的状态不能为 0）。</summary>
        public void SetSeed(int seed)
        {
            rngState = (uint)(seed == 0 ? 1 : seed);
        }

        private float Rand01()
        {
            rngState ^= rngState << 13;
            rngState ^= rngState >> 17;
            rngState ^= rngState << 5;
            return (rngState & 0xFFFFFFu) / (float)0x1000000;
        }

        private float RandRange(float a, float b)
        {
            return a + (b - a) * Rand01();
        }

        public bool Finished { get { return elapsed >= Duration; } }

        /// <summary>还剩几秒（画倒计时用）</summary>
        public float Remain { get { return Mathf.Max(0f, Duration - elapsed); } }

        // ══════════════════════════════════════════════════════════════
        //  一次模拟
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 开始本回合的模拟。
        /// 只往里**加**粒子，不清空 —— 上一回合的食材还在杯子里，
        /// 它们的属性和温度要留到这一回合继续算。
        /// </summary>
        public void Begin(TurnState turn)
        {
            elapsed = 0f;
            nextReactionAt = ReactionInterval;
            roundScore = 0;
            reactionLog.Clear();
            floaters.Clear();

            // 刀片：整关只有一次初始化，之后磨损累积
            if (!initialized)
            {
                initialized = true;
                bladeHardness = turn != null ? turn.BladeHardness() : 0f;
                temp = 20f;                       // 室温起步
            }
            // 换了刀片就换硬度 —— 但保留"已经磨掉多少"这件事由外面决定，
            // 这里只保证有一把能用的刀
            if (bladeHardness <= 0f && !bladeBroken)
                bladeHardness = turn != null ? turn.BladeHardness() : 0f;

            if (turn != null && turn.cup != null)
            {
                for (int i = 0; i < turn.cup.Count; i++)
                    if (turn.cup[i] != null) AddParticle(turn.cup[i]);
            }
        }

        /// <summary>加一颗食材粒子：随机落在杯内。</summary>
        public SimParticle AddParticle(Ingredient ing)
        {
            SimParticle p = new SimParticle();
            p.id = ing.id;
            p.name = ing.name;
            p.attrs = ing.attrs;
            p.radius = ParticleRadius;
            p.wear = 0f;

            // 硬度取自食材的盐性（固体倾向）—— 和 TurnState.BladeHardness 同一套口径
            p.hardness = ing.attrs != null ? ing.attrs.Get(AttrId.Salt) : 0f;
            p.temp = temp;

            // 随机落在杯内，但别贴着圆心（那里是刀片），也别压到别的粒子
            float ang = Rand01() * Mathf.PI * 2f;
            float r = Mathf.Lerp(CupRadius * 0.35f, CupRadius * 0.78f, Rand01());
            p.pos = Center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
            p.vel = Vector2.zero;

            particles.Add(p);
            return p;
        }

        // ══════════════════════════════════════════════════════════════
        //  每帧推进
        // ══════════════════════════════════════════════════════════════

        public void Tick(float dt)
        {
            if (dt <= 0f) return;

            UpdateFloaters(dt);

            if (Finished) return;

            elapsed += dt;

            // 刀片转（爆刀之后就不转了 —— 视觉上要停下来）
            if (!bladeBroken) bladeAngle += bladeSpeed * dt;
            if (bladeAngle > 360f) bladeAngle -= 360f;

            UpdateParticles(dt);

            // 每满 1 秒结算一次反应。
            // 用 while 而不是 if：掉帧时一帧可能跨过多个整秒，
            // 用 if 会漏掉那几次反应，得分就跟帧率挂钩了。
            while (!bladeBroken && elapsed >= nextReactionAt)
            {
                nextReactionAt += ReactionInterval;
                ReactionTick();
            }
        }

        private void UpdateParticles(float dt)
        {
            for (int i = 0; i < particles.Count; i++)
            {
                SimParticle p = particles[i];

                // ── 刀片往外甩 ──
                Vector2 off = p.pos - Center;
                float d = off.magnitude;
                if (d < 0.001f) { off = new Vector2(1f, 0f); d = 0.001f; }

                // 离中心越近推得越狠 —— 刀片在中间，那儿线速度最小但搅动最强
                float push = BladePush * dt / Mathf.Max(0.15f, d);
                if (bladeBroken) push = 0f;
                p.vel += off.normalized * push;

                // ── 粒子互斥（只算 i 之后那些，避免同一对算两遍）──
                for (int j = i + 1; j < particles.Count; j++)
                {
                    SimParticle q = particles[j];
                    Vector2 diff = p.pos - q.pos;
                    float dist = diff.magnitude;
                    float minD = p.radius + q.radius;

                    if (dist > 0.0001f && dist < minD)
                    {
                        Vector2 f = diff.normalized * (Repel * (minD - dist) * dt);
                        p.vel += f;
                        q.vel -= f;
                    }
                }

                p.pos += p.vel * dt;
                p.vel *= Mathf.Exp(-Damping * dt);   // 帧率无关的阻尼

                // ── 撞内壁就贴着走 ──
                Vector2 fromC = p.pos - Center;
                float len = fromC.magnitude;
                float maxLen = CupRadius - p.radius;
                if (len > maxLen && len > 0.0001f)
                {
                    Vector2 n = fromC / len;
                    p.pos = Center + n * maxLen;

                    // 把向外的速度分量去掉，剩下的沿着壁滑 —— 看起来像被甩到杯壁上
                    float vn = Vector2.Dot(p.vel, n);
                    if (vn > 0f) p.vel -= n * vn;
                }

                p.temp = temp;
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  每 1 秒的一次结算
        // ══════════════════════════════════════════════════════════════

        private void ReactionTick()
        {
            int tick = Mathf.RoundToInt(elapsed);

            // ① 摩擦生热：刀片转就发热
            temp += 9f;

            // ② 刀片磨损；硬度归零就爆刀
            if (!bladeBroken)
            {
                // ★ 占位数值。规格只说了"硬度为 0 或更低就爆刀"，没给磨损速率。
                //   取 1.5 是为了让 H=20 的刀大约撑 13 次结算（≈ 2~3 个回合），
                //   偶尔见到爆刀但不会每回合都爆。
                //   策划给了真实磨损速率之后改这一个数就行。
                bladeHardness -= 1.5f;
                if (bladeHardness <= 0f)
                {
                    bladeHardness = 0f;
                    bladeBroken = true;
                    AddFloater("爆刀！", Center + new Vector2(0f, 0.18f),
                               new Color(1f, 0.36f, 0.28f), true);
                    reactionLog.Add("爆刀");
                }
            }

            // ③ 粒子被磨：硬度掉、磨损涨（画面上变小变淡）
            for (int i = 0; i < particles.Count; i++)
            {
                SimParticle p = particles[i];
                p.hardness = Mathf.Max(0f, p.hardness - 1.5f);
                p.wear = Mathf.Clamp01(p.wear + 0.12f);
            }

            // ④ 判反应
            List<ReactionDef> defs = ReactionCatalog.All();
            for (int i = 0; i < defs.Count; i++)
            {
                ReactionDef def = defs[i];
                if (!Matches(def)) continue;

                roundScore += def.score;
                totalScore += def.score;
                reactionLog.Add(def.name);

                // 飘字落在杯内随机一点，别全叠在正中
                Vector2 at = Center + new Vector2(
                    RandRange(-0.18f, 0.18f),
                    RandRange(-0.10f, 0.10f));

                AddFloater(def.Text(), at, new Color(1f, 0.86f, 0.42f), def.score >= 50);
            }

            // ⑤ 什么反应都没触发时，给一条"还在跑"的反馈
            //    —— 前几秒通常不够阈值，静悄悄的话像卡住了
            if (tick > 0 && reactionLog.Count == 0)
                AddFloater("研磨中…", Center + new Vector2(0f, -0.22f),
                           new Color(0.66f, 0.72f, 0.82f), false);
        }

        /// <summary>反应条件：杯内属性总量 + 温度。</summary>
        public bool Matches(ReactionDef def)
        {
            if (def == null || particles.Count == 0) return false;
            if (temp < def.minTemp) return false;

            int sumA = 0, sumB = 0;
            for (int i = 0; i < particles.Count; i++)
            {
                AttrSet a = particles[i].attrs;
                if (a == null) continue;
                if (def.thresholdA > 0) sumA += a.Get(def.attrA);
                if (def.thresholdB > 0) sumB += a.Get(def.attrB);
            }

            return sumA >= def.thresholdA && sumB >= def.thresholdB;
        }

        /// <summary>杯内某项属性的总量 —— 界面上显示"离反应还差多少"。</summary>
        public int CupAttr(AttrId id)
        {
            int sum = 0;
            for (int i = 0; i < particles.Count; i++)
                if (particles[i].attrs != null) sum += particles[i].attrs.Get(id);
            return sum;
        }

        // ══════════════════════════════════════════════════════════════
        //  飘字
        // ══════════════════════════════════════════════════════════════

        public void AddFloater(string text, Vector2 at, Color color, bool big)
        {
            if (string.IsNullOrEmpty(text)) return;

            SimFloater f = new SimFloater();
            f.text  = text;
            f.pos   = at;
            f.age   = 0f;
            f.life  = big ? 1.8f : 1.3f;
            f.color = color;
            f.big   = big;

            floaters.Add(f);

            // 规格：飘字数量过多时限制数量。
            // 超出就丢**最老的** —— 丢最新的会让刚发生的事看不见。
            while (floaters.Count > MaxFloaters) floaters.RemoveAt(0);
        }

        private void UpdateFloaters(float dt)
        {
            for (int i = floaters.Count - 1; i >= 0; i--)
            {
                SimFloater f = floaters[i];
                f.age += dt;
                f.pos += new Vector2(0f, 0.09f * dt);   // 慢慢往上飘

                // 规格：飘字显示后需要消失，别永久占屏幕
                if (f.age >= f.life) floaters.RemoveAt(i);
            }
        }

        /// <summary>规格：模拟结束后清除所有飘字。</summary>
        public void ClearFloaters() { floaters.Clear(); }

        /// <summary>本回合反应的文字回顾，结算界面用。</summary>
        public string ReactionSummary()
        {
            if (reactionLog.Count == 0) return "（没有触发反应）";
            return string.Join("、", reactionLog.ToArray());
        }
    }
}
