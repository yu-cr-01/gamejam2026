using UnityEngine;
using GameJam.Rules;

namespace GameJam.Prototype
{
    /// <summary>
    /// 反应特效归哪一类 —— **颜色和力度只由它决定**。
    ///
    /// 【为什么把"爆炸"单列成一类，而不是当成另一种颜色的附魔】
    ///   爆炸（热规则2：粉末 + 易燃）在引擎里也是一次"形态变化"（目标变空白卡），
    ///   光看结果分不出来。但它在规则表里是**最猛的一条**（目标 H 归零、刀片热 +1），
    ///   玩家该看到的是"炸了"，而不是"一次橙色的变形"。所以它自带颜色（白）和力度（×1.9）。
    /// </summary>
    public enum ReactionFxKind
    {
        /// <summary>热附魔打出来的反应（燃烧 / 遇热反应 / 液体→气态 / 金属→熔融…）—— 橙红。</summary>
        Heat = 0,

        /// <summary>冷附魔 —— 青蓝。</summary>
        Cold = 1,

        /// <summary>酸附魔 —— 黄绿。</summary>
        Acid = 2,

        /// <summary>催化（只降阈值，一般不单独出反应；留着是为了"命中哪类就染哪色"这条不出现例外）—— 淡紫。</summary>
        Catalyst = 3,

        /// <summary>爆炸（粉末 + 易燃）—— 白闪 + 更猛的迸发。</summary>
        Explode = 4,
    }

    /// <summary>
    /// 「两者发生反应」那一瞬间的特效：目标素材卡位置上的**粒子迸发 + 扩散光环 + 卡面发光**，
    /// 外加破壁机那一侧的呼应（见 <see cref="JuicerEcho"/>）。
    ///
    /// 【为什么全部用代码生成，一张图都不加】
    ///   美术资源（Assets/Resources/**）是封版的东西：加一张贴图就要美术再出一版、还要过导入设置。
    ///   这里要的"光环 / 光点"本来就是纯程序图形（一圈带 alpha 衰减的环、一个软边圆点），
    ///   贴图在 <see cref="FxTexture"/> 里按像素算一次、缓存起来 —— 既不加资源，也不会有导入设置踩坑。
    ///
    /// 【为什么没有碰撞体】
    ///   拾取走的是**全场景** Physics.Raycast（TableInteraction 那一句没有 LayerMask）。
    ///   特效只要带一个启用的 Collider，反应发生的那 0.85 秒里鼠标就点不到任何牌了
    ///   （而且是"反应一播完又好了"这种最难查的间歇性 bug）。
    ///   所以这里每个 primitive 都过一遍 <see cref="JuicerRig.Strip"/>，把碰撞体关掉。
    ///
    /// 【为什么不用每帧改材质颜色来淡出】
    ///   每帧改 material.color 会**复制出材质实例**（Unity 的老毛病，编辑模式下还会警告
    ///   "This will leak materials into the scene"）。这里：
    ///     · 每个特效只 new 4 个材质（光环 / 光点 / 白闪 / 卡面辉光），整段特效共用，
    ///       特效对象销毁时一起 Destroy —— 一次反应 4 个，不是每帧 4 个；
    ///     · 淡出走 **MaterialPropertyBlock**（逐渲染器覆盖 _Color，不碰材质本身），
    ///       再叠一层"缩小到 0"，即使 MPB 在某条管线上不生效也看得出粒子在消失。
    ///
    /// 【时长为什么是 0.85 秒】
    ///   太短（< 0.4s）在 60fps 下只有 20 来帧，玩家只会看到"闪了一下不知道发生了什么"；
    ///   太长（> 1.2s）会和下一次启动、以及卡牌飞向罐口的动画叠在一起。
    ///   0.85 秒刚好覆盖"卡面亮起 → 迸发 → 光环扩散到底"，又在下一次操作之前收干净。
    /// </summary>
    public class ReactionFx : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════════════
        //  调参区 —— 颜色 / 时长 / 数量都在这几个常量里，改特效只动这一段
        // ══════════════════════════════════════════════════════════════

        /// <summary>整段特效的总时长（秒）。到点自动销毁，不留垃圾。</summary>
        public const float Life = 0.85f;

        /// <summary>扩散光环用多久扩散到最大半径（秒）。比总时长短：让光环先散完、光点再落干净。</summary>
        public const float RingLife = 0.55f;

        /// <summary>白闪用时（秒）。爆炸才有，要的就是"一瞬间"。</summary>
        public const float FlashLife = 0.20f;

        /// <summary>卡面发光用时（秒）。最短的一个 —— 卡马上要飞进机器，光留久了会跟着飞。</summary>
        public const float CardGlowLife = 0.40f;

        /// <summary>迸发粒子数（爆炸会自动乘 <see cref="ExplodePower"/>）。</summary>
        public const int SparkCount = 20;

        /// <summary>粒子初速（世界单位/秒）。卡宽才 0.24，所以 1.0 左右就是"呲出去一小把"。</summary>
        public const float SparkSpeed = 1.05f;

        /// <summary>粒子下坠（世界单位/秒²）。有下坠才像迸出来的碎屑，而不是一圈匀速扩散的光点。</summary>
        public const float SparkGravity = 1.8f;

        /// <summary>粒子边长（世界单位）。</summary>
        public const float SparkSize = 0.045f;

        /// <summary>光环扩散到的最大半径（世界单位）。卡的对角半径约 0.21 —— 取 0.30 让它最后明显散到卡外。</summary>
        public const float RingRadius = 0.30f;

        /// <summary>
        /// 光环的最大不透明度。
        /// 1 = 一圈实心橙（第一版就是这个值，实机上像桌上贴了个甜甜圈、还糊住了牌）；
        /// 0.55 = 太淡（暗桌面上几乎只剩一圈褐色，看不出"反应发生了"）；
        /// 0.70 是两版实跑对比之后留下的值：一眼看得见，又不抢卡面。
        /// </summary>
        public const float RingAlpha = 0.70f;

        /// <summary>卡面辉光的最大不透明度。比光环亮一点 —— 它要说明"被点的是这张卡"。</summary>
        public const float GlowAlpha = 0.80f;

        /// <summary>爆炸的"更猛"系数：粒子数、速度、光环半径、白闪大小都乘它。</summary>
        public const float ExplodePower = 1.9f;

        /// <summary>破壁机那侧呼应（发光 + 轻震）的时长（秒）。比卡上的特效短一点，看着像"被这一下震到"。</summary>
        public const float EchoLife = 0.5f;

        // ── 颜色映射（用户口径：热=橙红、冷=青蓝、酸=黄绿、催化=淡紫、爆炸=白）──
        private static readonly Color HeatTint     = new Color(1.00f, 0.36f, 0.12f);
        private static readonly Color ColdTint     = new Color(0.34f, 0.84f, 1.00f);
        private static readonly Color AcidTint     = new Color(0.72f, 0.96f, 0.20f);
        private static readonly Color CatalystTint = new Color(0.78f, 0.62f, 1.00f);
        private static readonly Color ExplodeTint  = new Color(1.00f, 1.00f, 1.00f);

        /// <summary>这一类反应用什么颜色（破壁机那侧的呼应也用它，两处必须同色）。</summary>
        public static Color TintOf(ReactionFxKind kind)
        {
            switch (kind)
            {
                case ReactionFxKind.Cold:     return ColdTint;
                case ReactionFxKind.Acid:     return AcidTint;
                case ReactionFxKind.Catalyst: return CatalystTint;
                case ReactionFxKind.Explode:  return ExplodeTint;
            }
            return HeatTint;
        }

        /// <summary>这一类反应的中文名（日志和回报里要写"这次是什么反应、什么颜色"）。</summary>
        public static string KindName(ReactionFxKind kind)
        {
            switch (kind)
            {
                case ReactionFxKind.Cold:     return "冷（青蓝）";
                case ReactionFxKind.Acid:     return "酸（黄绿）";
                case ReactionFxKind.Catalyst: return "催化（淡紫）";
                case ReactionFxKind.Explode:  return "爆炸（白闪）";
            }
            return "热（橙红）";
        }

        // ══════════════════════════════════════════════════════════════
        //  从引擎结算结果里认出"这一次到底发生了什么反应"
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 读一次启动结算的结果，判断"要不要播特效、播哪一类"。
        ///
        /// 【为什么是读日志，而不是让引擎多给一个字段】
        ///   Assets/Scripts/Rules/** 是封版基线（另有 210 项断言守着），不许为了表现层改签名。
        ///   而引擎**已经把该说的都写在中文日志里了**，而且写得很规整：
        ///     · 命中行：`· 检查热附魔：优先级4「热反应（遇热）」 → 水　（实际阈值：…）`
        ///       —— 「」里是规则名、前面是附魔类型，两者都是稳定格式（引擎自己的 Describe()）。
        ///     · 结果行：`形态变化：旧卡「冰」移出桌面` / `溶解移除：旧卡移出桌面，不产生副产物`
        ///       / `目标 H 归零（爆炸（粉末 + 易燃））` / `目标 D 立即归零（燃烧（易燃，非粉末））`
        ///       / `进手牌：水（素材） ← 析出副产物`
        ///   这里同时认"命中"和"结果"两件事：**只命中但什么都没变**（该卡声明"无变化"、
        ///   或卡表缺产物）不播特效 —— 否则玩家会看到"炸了一下但牌还在原地"，
        ///   那比不播更误导。
        ///
        /// ★ 如果以后引擎愿意在 TurnResult 上加结构化字段（命中类型 / 结果 / 产物名），
        ///   这里换成读字段即可，调用点（TableRulesV21.PlayReactionFx）一个字都不用改。
        /// </summary>
        public static bool TryReadFromLog(TurnResult r, out ReactionFxKind kind, out string rule, out string outcome)
        {
            kind = ReactionFxKind.Heat;
            rule = "";
            outcome = "";

            if (r == null || r.log == null) return false;

            bool hit = false;
            LayerKind hitKind = LayerKind.Heat;

            bool formChange = false;   // 形态变化（含"燃烧"——它是 D 归零后的形态变化）
            bool dissolve   = false;   // 溶解移除
            bool explode    = false;   // 爆炸：目标 H 归零（规则表里只有爆炸走这一条）
            bool burn       = false;   // 燃烧：目标 D 立即归零
            bool byProduct  = false;   // 析出副产物（目标保留）

            for (int i = 0; i < r.log.Count; i++)
            {
                string line = r.log[i];
                if (string.IsNullOrEmpty(line)) continue;

                // ① 第一次命中的附魔 = 这次反应的颜色归属（热→冷→酸的检查顺序在引擎里写死）
                if (!hit && line.Contains("· 检查") && line.Contains("优先级") && line.Contains("「"))
                {
                    hit = true;
                    hitKind = KindOfCheckLine(line);
                    rule = Between(line, "「", "」");
                }

                // ② 结果行：只要有任意一条真的改变了目标，就算"发生了反应"
                if (line.Contains("形态变化：旧卡「")) formChange = true;
                else if (line.Contains("溶解移除：旧卡移出桌面")) dissolve = true;
                else if (line.Contains("目标 H 归零（")) explode = true;
                else if (line.Contains("目标 D 立即归零（")) burn = true;
                else if (line.Contains("← 析出副产物")) byProduct = true;
            }

            if (!hit) return false;
            if (!(formChange || dissolve || explode || burn || byProduct)) return false;

            // 爆炸优先于附魔类型：它自带白闪和更大的力度，颜色不该再按"热"染成橙红
            kind = explode ? ReactionFxKind.Explode : FromLayerKind(hitKind);

            if (explode)            outcome = "爆炸";
            else if (burn)          outcome = "燃烧";
            else if (formChange)    outcome = "形态变化";
            else if (dissolve)      outcome = "溶解";
            else if (byProduct)     outcome = "析出";

            return true;
        }

        /// <summary>
        /// 命中行前六个字里的附魔类型：`　· 检查热附魔：优先级4「…」` → 热。
        /// 取不到就按热处理 —— 猜错颜色只是难看，漏播特效才是真的没做。
        /// </summary>
        private static LayerKind KindOfCheckLine(string line)
        {
            if (line.Contains("检查冷附魔")) return LayerKind.Cold;
            if (line.Contains("检查酸附魔")) return LayerKind.Acid;
            if (line.Contains("检查催化附魔")) return LayerKind.Catalyst;
            return LayerKind.Heat;
        }

        private static ReactionFxKind FromLayerKind(LayerKind k)
        {
            switch (k)
            {
                case LayerKind.Cold:     return ReactionFxKind.Cold;
                case LayerKind.Acid:     return ReactionFxKind.Acid;
                case LayerKind.Catalyst: return ReactionFxKind.Catalyst;
            }
            return ReactionFxKind.Heat;
        }

        /// <summary>取两个标记之间的那一段（日志里规则名写在「」里）。</summary>
        private static string Between(string s, string open, string close)
        {
            int a = s.IndexOf(open);
            if (a < 0) return "";
            a += open.Length;

            int b = s.IndexOf(close, a);
            if (b < 0) return s.Substring(a);

            return s.Substring(a, b - a);
        }

        // ══════════════════════════════════════════════════════════════
        //  播放
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 在 <paramref name="worldPos"/>（目标素材卡的位置）播一段反应特效。
        ///
        /// <paramref name="follow"/> 是那张 3D 卡：卡面辉光挂在它下面，
        /// 【为什么要挂上去】卡片紧接着就会被 ConsumeInto 吸向罐口 ——
        ///   辉光要是留在地上，就成了"牌飞走了、地上还亮着一块"。
        ///   挂在卡下面，它会跟着卡一起飞、一起缩小、一起销毁。
        /// </summary>
        public static ReactionFx Play(Vector3 worldPos, ReactionFxKind kind, Transform follow)
        {
            GameObject go = new GameObject("ReactionFx_" + kind);
            go.transform.position = worldPos;

            ReactionFx fx = go.AddComponent<ReactionFx>();
            fx.kind  = kind;
            fx.tint  = TintOf(kind);
            fx.power = (kind == ReactionFxKind.Explode) ? ExplodePower : 1f;
            fx.follow = follow;
            fx.Build();
            return fx;
        }

        // ── 运行时 ────────────────────────────────────────────────────

        private ReactionFxKind kind;
        private Color tint = Color.white;
        private float power = 1f;
        private Transform follow;

        private Transform ring;
        private Transform flash;
        private Transform cardGlow;
        private readonly System.Collections.Generic.List<Transform> sparks =
            new System.Collections.Generic.List<Transform>();
        private readonly System.Collections.Generic.List<Vector3> sparkVel =
            new System.Collections.Generic.List<Vector3>();
        private readonly System.Collections.Generic.List<Renderer> sparkRenderers =
            new System.Collections.Generic.List<Renderer>();

        private Material ringMat;
        private Material sparkMat;
        private Material flashMat;
        private Material glowMat;

        /// <summary>
        /// ★ 只能在这里（Build 里）new，**不能写成字段初始化器**：
        ///   `new MaterialPropertyBlock()` 会调进 Unity 原生层（CreateImpl），
        ///   而字段初始化器是在 MonoBehaviour 的**构造函数**里跑的 —— Unity 直接抛
        ///   "CreateImpl is not allowed to be called from a MonoBehaviour constructor"。
        ///   实跑踩过：那个异常发生在 AddComponent 中途，组件建出来了但字段是 null，
        ///   于是每帧 Update 在第一颗粒子就 NullReference 中断 ——
        ///   表现是"光环不淡出、粒子挤成一圈不动、特效永远不消失"。
        /// </summary>
        private MaterialPropertyBlock mpb;

        private float t;
        private Camera cam;

        /// <summary>材质属性名。用字符串而不是 Shader.PropertyToID 的静态字段：后者同样是"构造期调 Unity API"。</summary>
        private const string ColorProp = "_Color";

        private void Build()
        {
            cam = Camera.main;
            if (cam == null) cam = FindObjectOfType<Camera>();

            mpb = new MaterialPropertyBlock();

            // ── 扩散光环：平躺在桌面上（俯视和桌面视角都看得见），从卡心向外扩散 ──
            ringMat = CardFactory.MakeUnlit(FxTexture.Ring());
            ringMat.color = WithAlpha(tint, RingAlpha);   // 底色就带一点透明，别糊成一整圈实心橙
            ring = MakeQuad("Ring", transform, ringMat, RingRadius * 0.30f, RingRadius * 0.30f);
            ring.localRotation = Quaternion.Euler(90f, 0f, 0f);   // Quad 躺平后法线朝上
            ring.localPosition = new Vector3(0f, 0.014f, 0f);     // 抬到卡面之上，别和卡身穿插

            // ── 迸发粒子：小光点，从卡心朝外上方飞、带下坠 ──
            //   ★ 颜色同时写在材质上（sparkMat.color）和逐个渲染器的 MPB 里：
            //     MPB 负责"每颗粒子自己的 alpha"，材质负责"就算 MPB 不生效也还是这个颜色" ——
            //     少了材质这一份，粒子会渲染成默认的白色（踩过：一圈白光点，和附魔颜色对不上）。
            sparkMat = CardFactory.MakeUnlit(FxTexture.Dot());
            sparkMat.color = WithAlpha(tint, 1f);

            int n = Mathf.RoundToInt(SparkCount * power);
            for (int i = 0; i < n; i++)
            {
                Transform s = MakeQuad("Spark" + i, transform, sparkMat, SparkSize, SparkSize);

                // 方向：绕 Y 轴均分 + 一点随机，抬高角 20°~70° ——
                // 均分保证"哪一边都呲得到"（纯随机会出现一边空、一边挤），
                // 随机只用来打破均分带来的机械感。
                float ang = (i / (float)n) * Mathf.PI * 2f + Random.Range(-0.22f, 0.22f);
                float up  = Random.Range(0.35f, 1.20f);
                Vector3 dir = new Vector3(Mathf.Cos(ang), up, Mathf.Sin(ang)).normalized;

                float speed = SparkSpeed * power * Random.Range(0.7f, 1.3f);
                sparkVel.Add(dir * speed);
                sparks.Add(s);
                sparkRenderers.Add(s.GetComponent<Renderer>());

                s.localPosition = new Vector3(Mathf.Cos(ang) * 0.03f, 0.030f, Mathf.Sin(ang) * 0.03f);
            }

            // ── 白闪：只有爆炸有，正对相机的一整块亮斑 ──
            if (kind == ReactionFxKind.Explode)
            {
                flashMat = CardFactory.MakeUnlit(FxTexture.Dot());
                flashMat.color = Color.white;
                flash = MakeQuad("Flash", transform, flashMat, 0.62f, 0.62f);
                flash.localPosition = new Vector3(0f, 0.06f, 0f);
            }

            // ── 卡面辉光：挂在卡下面，跟着卡飞进机器 ──
            if (follow != null)
            {
                glowMat = CardFactory.MakeUnlit(FxTexture.Dot());
                glowMat.color = tint;

                cardGlow = MakeQuad("CardGlow", follow, glowMat,
                                    CardFactory.CardWidth * 1.15f, CardFactory.CardDepth * 1.10f);
                cardGlow.localPosition = new Vector3(0f, CardFactory.CardThick * 0.5f + 0.0038f, 0f);
                cardGlow.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
        }

        /// <summary>
        /// 造一块 Quad 特效片。★ 一定要 Strip：拾取是全场景 Physics.Raycast，
        /// 天上飘一个带碰撞体的光点，鼠标就点不到牌了（见类注释）。
        /// </summary>
        private static Transform MakeQuad(string name, Transform parent, Material mat, float w, float h)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.transform.SetParent(parent, false);

            JuicerRig.Strip(go);        // 关掉 Quad 自带的 MeshCollider

            MeshRenderer r = go.GetComponent<MeshRenderer>();
            if (r != null)
            {
                r.sharedMaterial = mat;                 // 共用材质，不复制实例
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }

            go.transform.localScale = new Vector3(w, h, 1f);
            return go.transform;
        }

        private void Update()
        {
            t += Time.deltaTime;

            // ⓪ ★ 先判"到点了没"，再动任何东西：
            //    这段动画有好几处取引用，任何一处出问题（比如某个对象被别处销毁）
            //    都不该让一个特效永远留在场上 —— 那就成了"桌上一直有个橙色圈"的鬼影。
            if (t >= Life)
            {
                CardFactory.DestroySafe(gameObject);
                return;
            }

            // ① 光环：easeOut 扩散 + alpha 收干净
            if (ring != null)
            {
                float k = Mathf.Clamp01(t / RingLife);
                float e = 1f - (1f - k) * (1f - k);          // easeOut：一开始散得快，收尾慢
                float d = Mathf.Lerp(RingRadius * 0.30f, RingRadius * power, e) * 2f;
                ring.localScale = new Vector3(d, d, 1f);
                SetFade(ring.GetComponent<Renderer>(), WithAlpha(tint, RingAlpha * Mathf.Pow(1f - k, 1.4f)));
            }

            // ② 粒子：抛体运动 + 缩小到 0（缩小是主淡出，MPB 的 alpha 只是加成）
            for (int i = 0; i < sparks.Count; i++)
            {
                Transform s = sparks[i];
                if (s == null) continue;

                Vector3 v = sparkVel[i];
                v.y -= SparkGravity * Time.deltaTime;
                sparkVel[i] = v;

                s.localPosition += v * Time.deltaTime;

                float k = Mathf.Clamp01(t / Life);
                float sc = SparkSize * power * (1f - k);
                s.localScale = new Vector3(sc, sc, 1f);

                if (cam != null) s.rotation = cam.transform.rotation;   // 光点永远正对相机
                if (i < sparkRenderers.Count)
                    SetFade(sparkRenderers[i], WithAlpha(tint, 1f - k));
            }

            // ③ 白闪：先撑到最大、再迅速收掉
            if (flash != null)
            {
                float k = Mathf.Clamp01(t / FlashLife);
                float d = Mathf.Lerp(0.35f, 0.62f * power, k) * 2f;
                flash.localScale = new Vector3(d, d, 1f);
                if (cam != null) flash.rotation = cam.transform.rotation;
                SetFade(flash.GetComponent<Renderer>(), WithAlpha(Color.white, Mathf.Pow(1f - k, 0.8f)));
            }

            // ④ 卡面辉光：正弦包络（亮起—灭掉），不是线性淡出，短促才像"被点了一下"
            if (cardGlow != null)
            {
                float k = Mathf.Clamp01(t / CardGlowLife);
                SetFade(cardGlow.GetComponent<Renderer>(), WithAlpha(tint, Mathf.Sin(Mathf.PI * k) * GlowAlpha));
            }

            // ⑤ 到点自毁由 Update 开头那一段负责（连材质一起带走，见 OnDestroy）
        }

        /// <summary>
        /// 用 MaterialPropertyBlock 覆盖这一块特效片的 _Color（含 alpha）。
        /// 不改材质本身 = 不产生材质实例、也不影响共用同一材质的其它粒子。
        ///
        /// ★ 只改 alpha、颜色照抄传进来的那个：颜色一旦写在材质上（见 Build），
        ///   这里也写成同一个值，两条路都对得上；MPB 万一不生效，
        ///   至少颜色还是对的，只是淡出会变成"靠缩小消失"。
        /// </summary>
        private void SetFade(Renderer r, Color c)
        {
            if (r == null || mpb == null) return;

            mpb.SetColor(ColorProp, c);
            r.SetPropertyBlock(mpb);
        }

        private static Color WithAlpha(Color c, float a)
        {
            return new Color(c.r, c.g, c.b, Mathf.Clamp01(a));
        }

        private void OnDestroy()
        {
            // 这四个材质是本组件 new 出来的（不挂在任何资源上），必须自己带走
            if (ringMat  != null) Destroy(ringMat);
            if (sparkMat != null) Destroy(sparkMat);
            if (flashMat != null) Destroy(flashMat);
            if (glowMat  != null) Destroy(glowMat);
        }

        /// <summary>
        /// 破壁机呼应那块辉光用的贴图（和粒子同一张软边圆点）。
        /// 走这个方法拿，是为了让"两张贴图只有一份"这件事在类外面也说得清 ——
        /// 谁都不许自己再 new 一张。
        /// </summary>
        internal static Texture2D EchoDot() { return FxTexture.Dot(); }

        // ══════════════════════════════════════════════════════════════
        //  程序化贴图（绝不落盘，也就绝不进 Assets/Resources）
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 光环 / 光点两张贴图，整个进程只算一次。
        ///
        /// 【为什么用 static 缓存而不是每个特效各算一份】
        ///   256×256 的逐像素计算不贵、但也不该每次反应都做一遍；
        ///   更重要的是它们**不会被销毁**（不挂在任何对象上），所以不存在泄漏 ——
        ///   省下来的是每次反应 6 万次 SetPixels 的开销。
        ///
        /// 【为什么 RGB 全是白、颜色靠材质染】
        ///   同一张贴图要服务五种颜色，烘死颜色就得有五张图。
        /// </summary>
        internal static class FxTexture
        {
            private static Texture2D ring;
            private static Texture2D dot;

            /// <summary>一圈软边圆环：半径 45%~52% 之间亮，内外都收到透明。</summary>
            public static Texture2D Ring()
            {
                if (ring != null) return ring;

                const int N = 128;
                ring = NewTex(N, "ReactionFxRing");

                Color32[] px = new Color32[N * N];
                for (int y = 0; y < N; y++)
                {
                    for (int x = 0; x < N; x++)
                    {
                        float dx = (x + 0.5f) / N * 2f - 1f;
                        float dy = (y + 0.5f) / N * 2f - 1f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);

                        // 内边 0.30 → 亮环中心 0.45 → 外边 0.62 全透明
                        // （环带宽度取 0.16 而不是 0.12：窄环在实机上像一根实心橡皮筋，
                        //   宽一点、边缘更软，才像"扩散的光"）
                        float a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.45f) / 0.16f);
                        a = a * a * (3f - 2f * a);            // SmoothStep：边缘不要硬切

                        px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                    }
                }
                ring.SetPixels32(px);
                ring.Apply(true, false);   // 生成 mip（见 FxTexture.NewTex 的说明）
                return ring;
            }

            /// <summary>一个软边圆点：中心实、往外二次衰减到 0（做粒子和白闪共用）。</summary>
            public static Texture2D Dot()
            {
                if (dot != null) return dot;

                const int N = 64;
                dot = NewTex(N, "ReactionFxDot");

                Color32[] px = new Color32[N * N];
                for (int y = 0; y < N; y++)
                {
                    for (int x = 0; x < N; x++)
                    {
                        float dx = (x + 0.5f) / N * 2f - 1f;
                        float dy = (y + 0.5f) / N * 2f - 1f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);

                        float a = Mathf.Clamp01(1f - d);
                        a = a * a;                            // 二次衰减：中心亮、边缘柔和

                        px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                    }
                }
                dot.SetPixels32(px);
                dot.Apply(true, false);    // 生成 mip（见 FxTexture.NewTex 的说明）
                return dot;
            }

            private static Texture2D NewTex(int n, string name)
            {
                // ★ 建 mip 链（第 4 个参数 = true）+ Apply(true) 生成 mip —— 和 ProceduralArt.NewTex 同一条理由：
                //   粒子只有 0.045 世界单位大，屏幕上是**缩小采样**，没有 mip 就只有 mip 0 可采；
                //   而打包版会按画质档位限制贴图分辨率，一张没有 mip 链的贴图被降级时颜色就错了
                //   （实机表现：编辑器里是亮的光点，打包版里暗得几乎看不见）。
                Texture2D t = new Texture2D(n, n, TextureFormat.RGBA32, true);
                t.name = name;
                t.wrapMode = TextureWrapMode.Clamp;   // 不重复：边缘采样不会把对面的亮边拉过来
                t.filterMode = FilterMode.Bilinear;
                return t;
            }
        }
    }

    /// <summary>
    /// 破壁机那一侧的呼应：**机身亮一下 + 整机轻震**。
    ///
    /// 【为什么要"呼应"】反应发生在桌上的素材卡身上，但"是破壁机干的"这件事
    ///   只有机器自己也动一下才说得通 —— 否则像桌上凭空炸了一下。
    ///   冲压动作（JuicerRig.PlayStamp）已经在那条链路上播了，这里补的是**同时性**：
    ///   壳亮 + 抖，和卡片上的光环同一帧起、同一帧收。
    ///
    /// 【为什么发光块要按机身的实际包围盒算】
    ///   有美术立绘时程序化机身是整组关掉的（JuicerRig.HideProceduralMachineWhenArtPresent），
    ///   机器"有多大、中心在哪"随配置而变；写死一个尺寸，挂上立绘就会亮错位置。
    ///   所以这里拿**当前活着的渲染器**的包围盒来定辉光块的位置和大小。
    ///
    /// 【为什么震动只改 localPosition】
    ///   整机的坐标是 TableSetup 建好之后没人再动的（BlenderArt 每帧改的是自己的局部坐标），
    ///   所以这里存一份基准位置、到点还原，就不会把机器搬歪 ——
    ///   万一被别的代码插了一脚，还原时也用"自己的基准 + 0"。
    /// </summary>
    public class JuicerEcho : MonoBehaviour
    {
        private JuicerRig rig;
        private Vector3 baseLocalPos;
        private Transform glow;
        private Material glowMat;
        private float power = 1f;
        private float t;

        /// <summary>亮一下的最大幅度 / 震动幅度（世界单位）—— 都要小，机器只该"嗡"一下。</summary>
        private const float ShakeAmplitude = 0.012f;

        /// <summary>辉光最亮时的不透明度。机器是背景，不该亮过卡上的特效。</summary>
        private const float GlowAlpha = 0.75f;

        /// <summary>材质属性名（同上：字符串常量，别在字段初始化器里调 Shader.PropertyToID）。</summary>
        private const string ColorProp = "_Color";

        /// <summary>
        /// ★ 和 ReactionFx 里同一个坑：`new MaterialPropertyBlock()` 不能当字段初始化器，
        ///   它在 MonoBehaviour 构造期调 Unity 原生层，会抛
        ///   "CreateImpl is not allowed to be called from a MonoBehaviour constructor"，
        ///   然后每帧 NullReference —— 表现是辉光不亮、机器一直抖、呼应对象永不消失。
        ///   所以在 Build 里 new。
        /// </summary>
        private MaterialPropertyBlock mpb;

        public static void Play(JuicerRig owner, Color tint, float power)
        {
            if (owner == null) return;

            GameObject go = new GameObject("JuicerEcho");
            go.transform.SetParent(owner.transform, false);

            JuicerEcho e = go.AddComponent<JuicerEcho>();
            e.rig   = owner;
            e.power = power > 0f ? power : 1f;
            e.Build(tint);
        }

        private void Build(Color tint)
        {
            mpb = new MaterialPropertyBlock();
            baseLocalPos = rig.transform.localPosition;

            // 机身实际占多大：只算**活着的**渲染器 —— 立绘顶掉程序化机身时，
            // 被 SetActive(false) 的那些正好不在里面（这正是不写死尺寸的原因）。
            Renderer[] rs = rig.GetComponentsInChildren<Renderer>();
            bool any = false;
            Bounds b = new Bounds(rig.transform.position + Vector3.up * 0.3f, Vector3.one * 0.4f);
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i] == null || !rs[i].enabled) continue;
                if (!any) { b = rs[i].bounds; any = true; }
                else b.Encapsulate(rs[i].bounds);
            }

            Vector3 ls = rig.transform.lossyScale;
            float sx = Mathf.Abs(ls.x) > 0.0001f ? ls.x : 1f;
            float sy = Mathf.Abs(ls.y) > 0.0001f ? ls.y : 1f;

            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "EchoGlow";
            quad.transform.SetParent(transform, false);

            JuicerRig.Strip(quad);      // 同上：拾取不许被挡

            glowMat = CardFactory.MakeUnlit(ReactionFx.EchoDot());
            glowMat.color = tint;

            MeshRenderer r = quad.GetComponent<MeshRenderer>();
            if (r != null)
            {
                r.sharedMaterial = glowMat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }

            // 放在机身正前方、正对相机（Quad 的可见面朝 -Z，正是默认机位那一侧），
            // 比机身大 45% —— 看着是"整台机器亮了一圈"，而不是贴了一块方片。
            //
            // ★ 必须落在机身**前表面之前**（z 比 b.min.z 还小）：
            //   透明物之间是按到相机的距离排序画的，摆在机身厚度中间会被自己的正面盖住 ——
            //   表现就是"机器边上亮了一圈、机身本身一点没亮"。
            quad.transform.position = new Vector3(b.center.x, b.center.y, b.min.z - 0.03f);
            quad.transform.rotation = Quaternion.identity;
            quad.transform.localScale = new Vector3(b.size.x * 1.45f / sx, b.size.y * 1.45f / sy, 1f);

            glow = quad.transform;
        }

        private void Update()
        {
            t += Time.deltaTime;

            // ⓪ 先判"到点了没"：这样后面任何一步出问题都不会留下一个永远在抖的机器
            if (t >= ReactionFx.EchoLife)
            {
                rig.transform.localPosition = baseLocalPos;   // 还原（OnDestroy 里还会再来一次，幂等）
                CardFactory.DestroySafe(gameObject);
                return;
            }

            float k = Mathf.Clamp01(t / ReactionFx.EchoLife);

            // 亮一下：正弦包络（起—亮—灭），结束前一定回到 0
            if (glow != null && mpb != null)
            {
                Color c = glowMat != null ? glowMat.color : Color.white;
                mpb.SetColor(ColorProp, new Color(c.r, c.g, c.b, Mathf.Sin(Mathf.PI * k) * GlowAlpha));

                Renderer r = glow.GetComponent<Renderer>();
                if (r != null) r.SetPropertyBlock(mpb);
            }

            // 轻震：衰减正弦，只动 localPosition，到点精确还原
            float damp = 1f - k;
            float off = Mathf.Sin(t * 42f) * ShakeAmplitude * power * damp * damp;
            if (rig != null) rig.transform.localPosition = baseLocalPos + new Vector3(0f, off, 0f);
        }

        private void OnDestroy()
        {
            if (rig != null) rig.transform.localPosition = baseLocalPos;   // 还原，别把机器震歪
            if (glowMat != null) Destroy(glowMat);
        }
    }
}
