using System.Collections.Generic;
using UnityEngine;

namespace GameJam.Prototype
{
    /// <summary>
    /// 《邪恶铭刻》风格的开场 —— 3D 版。
    ///
    /// 【和 2D 那版的关系】
    /// 同一个思路（见 Board/TitleScreen.cs）：开场不是"标题 + 按钮列表"，
    /// 而是一个**场景**。「新游戏」是一本摊在桌上的书，其余选项是刻在木板上的字。
    /// 区别只在于这边的东西是**真的摆在那张 3D 桌子上**的立体物件，能转视角绕着看，
    /// 而那边是 IMGUI 画在屏幕上的。
    ///
    /// 【为什么桌上还有台破壁机】
    /// 榨汁机是这一局的主角，开场就该看见它。玩家一进来看到的画面是
    /// "一张桌子、一台机器、一根蜡烛、一本书"，比空桌子加一个按钮有说服力得多。
    ///
    /// 【全部程序化生成】
    /// 书、木牌、蜡烛、火苗都是代码搭出来的，没有素材依赖，
    /// 改一个数就能调气氛。
    /// </summary>
    public class TableTitleRig : MonoBehaviour
    {
        public Camera        cam;
        public TableSetup    setup;
        public TableTurnLoop loop;
        public Transform     root;

        /// <summary>菜单项 id —— 和点哪块木头对应。</summary>
        public const string IdNew      = "new";
        public const string IdContinue = "continue";
        public const string IdSettings = "settings";
        public const string IdQuit     = "quit";

        /// <summary>点机器也是开始 —— 它才是这一局的主角。</summary>
        public const string IdJuicer   = "juicer";

        private class MenuItem
        {
            public GameObject go;
            public Renderer   body;
            public Color      bodyBase;
            public string     id;
            public bool       enabled;
            public float      lift;
        }

        private readonly List<MenuItem> items = new List<MenuItem>();

        /// <summary>鼠标底下那一项（null = 没有）。</summary>
        public string Hovered { get; private set; }

        // ── 蜡烛 ──────────────────────────────────────────────────────
        private Transform   flame;
        private Light       candleLight;
        private float       flicker = 1f;

        // ── 榨汁机（点它启动）──────────────────────────────────────────
        private GameObject juicerProxy;    // 只在开场存在的点击代理
        private Renderer   juicerGlow;     // 悬停时桌面上亮起的一圈暖光
        private float      glowA;

        /// <summary>点了机器之后的启动延时。-1 表示没在启动。</summary>
        private float startingAt = -1f;

        /// <summary>启动动画时长。点了立刻切屏的话，玩家看不到机器被启动。</summary>
        private const float StartDelay = 1.3f;

        /// <summary>正在启动（这段时间锁输入）。</summary>
        public bool IsStarting { get { return startingAt >= 0f; } }

        // ── 布局 ──────────────────────────────────────────────────────
        // 书摆在桌子正中偏远端 —— 就是后面三选一牌组出现的位置，
        // 视线落点从开场到选牌是连贯的。
        public static readonly Vector3 BookAt   = new Vector3(0f, 0f, 0.16f);
        private const float BookW = 0.62f;
        private const float BookD = 0.46f;
        private const float BookH = 0.100f;

        // 三块木牌靠在近侧，排成一行
        private const float PlaqueZ = -0.24f;
        private const float PlaqueGap = 0.40f;

        // 蜡烛在左后角：不和书、木牌、榨汁机抢位置
        private static readonly Vector3 CandleAt = new Vector3(-0.80f, 0f, 0.34f);

        // 火苗基准尺寸。
        //
        // ★ 必须只在这里写一次：TickCandle 每帧都会拿它重算 localScale，
        //   如果建模那边另外写一组数，每帧都会被覆盖掉 —— 改了不生效还不知道为什么。
        private const float FlameW = 0.050f;
        private const float FlameH = 0.095f;

        private static readonly Color WoodDark   = new Color(0.240f, 0.165f, 0.105f);
        private static readonly Color WoodLight  = new Color(0.420f, 0.300f, 0.190f);
        private static readonly Color Leather    = new Color(0.270f, 0.150f, 0.100f);
        private static readonly Color Gold       = new Color(0.760f, 0.590f, 0.300f);
        private static readonly Color Parchment  = new Color(0.780f, 0.730f, 0.620f);
        private static readonly Color CarvedInk  = new Color(0.180f, 0.150f, 0.110f);

        // ── 不可选项（"继续"）的配色 ──
        // ★ 这里踩过一次：原来木牌用 (0.215,0.180,0.145)、字用 (0.30,0.28,0.26)，
        //   两块颜色明度几乎一样，截图上就是一坨灰、字完全读不出来。
        //   "不可用"要靠**整体压暗**表达，不能靠"把字也调暗"。
        //   现在牌子更暗、字反而更亮，对比度够了，同时一眼看出它是灰的。
        private static readonly Color PlaqueOffBody = new Color(0.150f, 0.128f, 0.108f);
        private static readonly Color PlaqueOffInk  = new Color(0.520f, 0.490f, 0.450f);

        // ══════════════════════════════════════════════════════════════
        //  建 / 拆
        // ══════════════════════════════════════════════════════════════

        public void Build()
        {
            Clear();

            BuildBook();
            // "继续"不加括号说明 —— 括号会让这行字比其他两块长出一截，
            // 而且横排三块的对齐会被撑歪。存档状态改由底部提示行交代。
            BuildPlaque(IdContinue, "继　　续",       -PlaqueGap, false);
            BuildPlaque(IdSettings, "设　　置",        0f,         true);
            BuildPlaque(IdQuit,     "退　　出",        PlaqueGap,  true);

            BuildCandle();
            BuildJuicerTrigger();
        }

        public void Clear()
        {
            for (int i = 0; i < items.Count; i++)
                CardFactory.DestroySafe(items[i].go);
            items.Clear();

            // 点击代理和辉光跟着开场一起走 —— 回合里不该有任何残留碰撞体
            CardFactory.DestroySafe(juicerProxy);
            CardFactory.DestroySafe(juicerGlow != null ? juicerGlow.gameObject : null);
            juicerProxy = null;
            juicerGlow  = null;
            glowA       = 0f;

            Hovered = null;
            flame = null;
            candleLight = null;
            startingAt = -1f;
        }

        // ── 榨汁机：点它启动 ─────────────────────────────────────────
        private void BuildJuicerTrigger()
        {
            JuicerRig j = setup != null ? setup.juicer : null;
            if (j == null) return;

            // ★ 机器自己的碰撞体是**故意关掉**的（JuicerRig.Strip —— 开着会挡住
            //   卡牌的射线拾取）。所以这里另挂一个只在开场存在的代理碰撞体：
            //   它跟着开场一起建、一起销毁，回合循环里完全不存在。
            //
            //   尺寸用机器的**本地空间**给，外面那层 0.75 的缩放会自动带上。
            GameObject proxy = new GameObject("TitleJuicerProxy");
            proxy.transform.SetParent(j.transform, false);
            proxy.transform.localPosition = new Vector3(0f, 0.35f, 0f);

            BoxCollider bc = proxy.AddComponent<BoxCollider>();
            bc.size = new Vector3(0.40f, 0.74f, 0.40f);
            juicerProxy = proxy;

            // 悬停时桌面上亮起一圈暖光 —— 机器本身已经是全画面最亮的东西，
            // 再往上提亮看不出变化，不如在它脚下点一盏灯。
            GameObject g = GameObject.CreatePrimitive(PrimitiveType.Quad);
            g.name = "TitleJuicerGlow";
            g.transform.SetParent(j.transform, false);
            g.transform.localPosition = new Vector3(0f, 0.006f, 0f);
            g.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            g.transform.localScale    = new Vector3(1.30f, 1.30f, 1f);

            Collider gc = g.GetComponent<Collider>();
            if (gc != null) gc.enabled = false;

            Renderer gr = g.GetComponent<Renderer>();
            if (gr != null)
            {
                gr.material = CardFactory.MakeUnlit(ProceduralArt.RadialGlow());
                gr.material.color = new Color(1f, 0.72f, 0.34f, 0f);
                gr.enabled = false;
            }
            juicerGlow = gr;
        }

        // ── 那本书 = 「新游戏」 ───────────────────────────────────────

        private void BuildBook()
        {
            GameObject go = NewRoot("TitleBook", BookAt);

            // 书体（合着的书：一个厚方块）
            GameObject body = Box(go.transform, "Body",
                                  new Vector3(BookW, BookH, BookD),
                                  new Vector3(0f, BookH * 0.5f, 0f),
                                  Leather);

            // 书脊：靠左一条凸起，颜色更深，一眼能看出这是本书
            Box(go.transform, "Spine",
                new Vector3(0.055f, BookH + 0.014f, BookD),
                new Vector3(-BookW * 0.5f + 0.020f, (BookH + 0.014f) * 0.5f, 0f),
                new Color(0.190f, 0.105f, 0.070f));

            // 书口：右侧露出的纸页
            Box(go.transform, "Pages",
                new Vector3(0.030f, BookH * 0.86f, BookD * 0.94f),
                new Vector3(BookW * 0.5f - 0.012f, BookH * 0.5f, 0f),
                Parchment);

            // 封面：最上面一层薄板
            float coverY = BookH + 0.004f;
            Box(go.transform, "Cover",
                new Vector3(BookW, 0.014f, BookD),
                new Vector3(0f, coverY, 0f),
                new Color(0.320f, 0.185f, 0.120f));

            // 烫金边框：四条细木条围一圈。比贴图省事，而且近看有厚度。
            float top = coverY + 0.014f;
            float fx = BookW * 0.5f - 0.075f;
            float fz = BookD * 0.5f - 0.070f;
            Box(go.transform, "FrameFar",  new Vector3(fx * 2f, 0.004f, 0.010f), new Vector3(0f, top, fz), Gold);
            Box(go.transform, "FrameNear", new Vector3(fx * 2f, 0.004f, 0.010f), new Vector3(0f, top, -fz), Gold);
            Box(go.transform, "FrameL",    new Vector3(0.010f, 0.004f, fz * 2f), new Vector3(-fx, top, 0f), Gold);
            Box(go.transform, "FrameR",    new Vector3(0.010f, 0.004f, fz * 2f), new Vector3(fx, top, 0f), Gold);

            // 封面上的字。
            //
            // ★ localZ 一律取 0 或负值。
            //   牌组卡那边踩过：文字放在正的 localZ 上完全不渲染，
            //   同一张卡负半区的几行都正常。根因没查到，先按规律避开。
            float textY = top + 0.004f;
            CardFactory.AddText(go.transform, "新　游　戏", 0f, 0.0150f, Gold, textY);
            CardFactory.AddText(go.transform, "翻 开 它",  -0.115f, 0.0068f,
                                new Color(0.70f, 0.60f, 0.42f), textY);

            MenuItem it = new MenuItem();
            it.go       = go;
            it.body     = body != null ? body.GetComponent<Renderer>() : null;
            it.bodyBase = WoodDark;
            it.id       = IdNew;
            it.enabled  = true;
            items.Add(it);
        }

        // ── 刻字的木牌 ───────────────────────────────────────────────

        private void BuildPlaque(string id, string label, float x, bool enabled)
        {
            GameObject go = NewRoot("TitlePlaque_" + id, new Vector3(x, 0f, PlaqueZ));

            GameObject body = Box(go.transform, "Body",
                                  new Vector3(0.34f, 0.024f, 0.12f),
                                  new Vector3(0f, 0.012f, 0f),
                                  enabled ? WoodLight : PlaqueOffBody);

            // 描边：四周一圈更深的木线，像牌子是嵌进桌面的
            Box(go.transform, "EdgeFar",  new Vector3(0.34f, 0.026f, 0.008f), new Vector3(0f, 0.013f,  0.056f), WoodDark);
            Box(go.transform, "EdgeNear", new Vector3(0.34f, 0.026f, 0.008f), new Vector3(0f, 0.013f, -0.056f), WoodDark);

            Color ink = enabled ? CarvedInk : PlaqueOffInk;
            CardFactory.AddText(go.transform, label, 0f, 0.0072f, ink, 0.0242f);

            MenuItem it = new MenuItem();
            it.go       = go;
            it.body     = body != null ? body.GetComponent<Renderer>() : null;
            it.bodyBase = enabled ? WoodLight : PlaqueOffBody;
            it.id       = id;
            it.enabled  = enabled;
            items.Add(it);
        }

        // ── 蜡烛 ─────────────────────────────────────────────────────

        private void BuildCandle()
        {
            GameObject go = NewRoot("TitleCandle", CandleAt);

            // 蜡身
            Cyl(go.transform, "Wax", 0.030f, 0.170f, new Vector3(0f, 0.085f, 0f),
                new Color(0.870f, 0.830f, 0.730f));

            // 顶面稍微亮一点，看着像有烛泪积在上面
            Cyl(go.transform, "WaxTop", 0.032f, 0.010f, new Vector3(0f, 0.174f, 0f),
                new Color(0.930f, 0.900f, 0.820f));

            // 烛芯
            Cyl(go.transform, "Wick", 0.0035f, 0.020f, new Vector3(0f, 0.188f, 0f),
                new Color(0.140f, 0.110f, 0.080f));

            // 火苗：一张永远朝着相机的面片
            GameObject f = GameObject.CreatePrimitive(PrimitiveType.Quad);
            f.name = "Flame";
            f.transform.SetParent(go.transform, false);
            f.transform.localPosition = new Vector3(0f, 0.228f, 0f);
            f.transform.localScale    = new Vector3(FlameW, FlameH, 1f);

            Collider fc = f.GetComponent<Collider>();
            if (fc != null) fc.enabled = false;      // 别挡住菜单的射线

            Renderer fr = f.GetComponent<Renderer>();
            if (fr != null) fr.material = CardFactory.MakeUnlit(ProceduralArt.Flame());
            flame = f.transform;

            // 点光源：整张桌子的暖色都从这儿来
            GameObject lg = new GameObject("CandleLight");
            lg.transform.SetParent(go.transform, false);
            lg.transform.localPosition = new Vector3(0f, 0.235f, 0f);

            candleLight = lg.AddComponent<Light>();
            candleLight.type      = LightType.Point;
            candleLight.color     = new Color(1.00f, 0.72f, 0.38f);
            candleLight.range     = 3.2f;
            candleLight.intensity = 1.6f;
            candleLight.shadows   = LightShadows.None;   // 点光阴影很贵，这个尺寸看不出来
        }

        // ══════════════════════════════════════════════════════════════
        //  输入 + 烛光
        // ══════════════════════════════════════════════════════════════

        void Update()
        {
            TickCandle();

            if (loop == null || loop.phase != TablePhase.Title) return;
            if (cam == null) return;

            // 设置面板开着的时候，底下的东西不再响应鼠标 ——
            // 不然会隔着面板把书点开
            if (loop.settingsOpen)
            {
                Animate();
                return;
            }

            // 启动动画期间锁输入 —— 连点几下不能重复触发（PlayStamp 重入会打断动画）
            if (startingAt >= 0f)
            {
                Animate();

                if (Time.time - startingAt >= StartDelay)
                {
                    startingAt = -1f;
                    loop.ConfirmTitleStart();
                }
                return;
            }

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            Hovered = HitItem(ray);

            if (Input.GetMouseButtonDown(0) && !string.IsNullOrEmpty(Hovered))
                Activate(Hovered);

            Animate();
        }

        /// <summary>
        /// 烛火抖动。
        ///
        /// 三个不成倍数的正弦叠加，而不是 Random ——
        /// 每帧重抽会变成刺眼的噪点闪烁，叠起来才有"呼吸"感。
        /// 和 2D 那版用的是同一组频率，两边看起来才是同一根蜡烛。
        /// </summary>
        private void TickCandle()
        {
            float t = Time.time;
            flicker = 0.82f
                    + 0.10f * Mathf.Sin(t * 3.1f)
                    + 0.06f * Mathf.Sin(t * 7.7f + 1.3f)
                    + 0.04f * Mathf.Sin(t * 13.9f + 2.7f);

            if (candleLight != null) candleLight.intensity = 2.4f * flicker;

            if (flame == null) return;

            // 宽高各自抖、频率不同，火苗才会"扭"而不是整体缩放
            float fh = (0.93f + 0.10f * Mathf.Sin(t * 5.3f));
            float fw = (0.92f + 0.13f * Mathf.Sin(t * 8.1f + 0.9f));
            flame.localScale = new Vector3(FlameW * fw, FlameH * fh, 1f);

            // 火苗永远正对相机 —— 侧看时不能变成一张纸片
            if (cam != null)
                flame.rotation = Quaternion.LookRotation(flame.position - cam.transform.position);
        }

        /// <summary>
        /// 射线打到哪一项（返回 id，没打到返回 null）。
        ///
        /// 公开出来是给编辑器工具做命中测试用的：机器的碰撞体是单独加的代理，
        /// 尺寸给错就会点不到，而**这种错在静态截图里完全看不出来** ——
        /// 画面一模一样，只是点下去没反应。
        /// </summary>
        public string HitTest(Ray ray) { return HitItem(ray); }

        private string HitItem(Ray ray)
        {
            RaycastHit hit;
            if (!Physics.Raycast(ray, out hit, 50f)) return null;

            // 从命中的碰撞体往上找，看是不是某一块菜单物件（或者机器的点击代理）。
            // 不用 transform.root —— 那会一路找到整张桌子。
            Transform t = hit.collider.transform;
            while (t != null)
            {
                if (juicerProxy != null && t == juicerProxy.transform) return IdJuicer;

                for (int i = 0; i < items.Count; i++)
                    if (items[i].go != null && items[i].go == t.gameObject) return items[i].id;
                t = t.parent;
            }
            return null;
        }

        private void Animate()
        {
            float k = 1f - Mathf.Exp(-14f * Time.deltaTime);

            for (int i = 0; i < items.Count; i++)
            {
                MenuItem it = items[i];
                if (it.go == null) continue;

                bool hot = it.enabled && it.id == Hovered;

                // 悬停时抬起来一点，像被手指按住翘起一角
                it.lift = Mathf.Lerp(it.lift, hot ? 0.014f : 0f, k);

                Vector3 p = it.go.transform.position;
                it.go.transform.position = new Vector3(p.x, it.lift, p.z);

                if (it.body != null && it.body.material != null)
                {
                    Color want = it.bodyBase * (hot ? 1.45f : 1f);
                    want.a = 1f;
                    it.body.material.color = Color.Lerp(it.body.material.color, want, k);
                }
            }

            AnimateJuicerGlow(k);
        }

        /// <summary>
        /// 机器脚下那圈光。
        /// 悬停时稳定亮着；按下之后改成脉动 —— 让"正在启动"看得出来，
        /// 而不是点完一片安静、一秒后才突然切屏。
        /// </summary>
        private void AnimateJuicerGlow(float k)
        {
            if (juicerGlow == null || juicerGlow.material == null) return;

            float want;
            if (startingAt >= 0f)
                want = 0.42f + 0.24f * Mathf.Sin(Time.time * 16f);
            else
                want = (Hovered == IdJuicer) ? 0.55f : 0f;

            glowA = Mathf.Lerp(glowA, want, k);

            juicerGlow.enabled = glowA > 0.02f;

            Color c = juicerGlow.material.color;
            c.a = glowA;
            juicerGlow.material.color = c;
        }

        /// <summary>
        /// 截图工具用：把悬停辉光强制点亮。
        ///
        /// 这圈光平时只在鼠标悬停（或启动中）才出现，而编辑模式下 Update 不跑，
        /// 静态预览图里就永远看不到它 —— 等于这块效果没人验过。
        /// 这里开个口子让 TablePreviewCapture 能把它点亮，和它已有的
        /// "给罐子灌 600 分好确认液面"是同一类东西。
        /// </summary>
        public void PreviewHighlight(bool on)
        {
            if (juicerGlow == null || juicerGlow.material == null) return;

            glowA = on ? 0.55f : 0f;
            juicerGlow.enabled = on;

            Color c = juicerGlow.material.color;
            c.a = glowA;
            juicerGlow.material.color = c;
        }

        /// <summary>点了某一项。</summary>
        private void Activate(string id)
        {
            for (int i = 0; i < items.Count; i++)
                if (items[i].id == id && !items[i].enabled) return;

            switch (id)
            {
                case IdNew:
                    if (loop != null) loop.ConfirmTitleStart();
                    break;

                // 点机器也是开始。先让它动起来，过一拍再切屏 ——
                // 点了立刻进牌组选择的话，玩家根本看不到自己启动了机器。
                case IdJuicer:
                    startingAt = Time.time;
                    if (setup != null && setup.juicer != null) setup.juicer.PlayStamp();
                    break;

                case IdQuit:
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#else
                    Application.Quit();
#endif
                    break;

                // 设置：打开设置面板（壳子，但里面的值都是生效的）
                case IdSettings:
                    if (loop != null) loop.settingsOpen = true;
                    break;

                default:
                    break;
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  建模小工具
        // ══════════════════════════════════════════════════════════════

        private GameObject NewRoot(string name, Vector3 at)
        {
            GameObject go = new GameObject(name);
            if (root != null) go.transform.SetParent(root, false);
            go.transform.position = at;
            return go;
        }

        private static GameObject Box(Transform parent, string name, Vector3 scale,
                                      Vector3 localPos, Color color)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localScale    = scale;
            go.transform.localPosition = localPos;

            Renderer r = go.GetComponent<Renderer>();
            if (r != null) r.material = Mat(color);
            return go;
        }

        private static void Cyl(Transform parent, string name, float radius, float height,
                                Vector3 localPos, Color color)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);

            // Cylinder 原始高度是 2，所以 y 缩放取一半
            go.transform.localScale    = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            go.transform.localPosition = localPos;

            Collider c = go.GetComponent<Collider>();
            if (c != null) c.enabled = false;    // 点菜单不该打到蜡烛上

            Renderer r = go.GetComponent<Renderer>();
            if (r != null) r.material = Mat(color);
        }

        private static Material Mat(Color c)
        {
            Shader sh = Shader.Find("Standard");
            if (sh == null) sh = Shader.Find("Diffuse");

            Material m = new Material(sh);
            m.color = c;
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.06f);
            if (m.HasProperty("_Metallic"))   m.SetFloat("_Metallic", 0f);
            return m;
        }
    }
}
