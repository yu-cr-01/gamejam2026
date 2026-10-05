using System.Collections.Generic;
using UnityEngine;
using GameJam.Data;

namespace GameJam.Prototype
{
    /// <summary>
    /// 榨汁机 + 液体罐 + 液压冲压模具。全部代码生成，不依赖任何外部模型。
    ///
    /// 【整体结构】—— 上压下接
    ///
    ///            ┌────────────┐  ← 顶板
    ///            │   ▓▓ 冲头   │  ← 冲头（上下运动）
    ///            │   ▒▒ 残渣   │  ← 榨汁剩下的残渣，被冲头压成刀片
    ///            ├────────────┤  ← 模具底板
    ///      ║     │            │     ║   ← 四根立柱（把机器架高）
    ///      ║     └────────────┘     ║
    ///      ║      ┌──────────┐      ║
    ///      ║      │  液体罐   │      ║   ← 罐子就在榨汁机下面，接榨出来的汁
    ///      ╚══════└──────────┘══════╝
    ///                    桌面
    ///
    /// 【为什么立柱是四根外露的、而不是一个整壳】
    /// 机身如果做成实心圆柱，里面的冲头和残渣就被挡住了，什么效果都看不见。
    /// 四柱开放式机架正是液压机本来的样子，冲压过程一览无余。
    ///
    /// 【刻度就是得分面板】
    /// 罐身上的每一道刻度环对应一个分数档位，液面高度 = 当前得分 / 目标分。
    /// 玩家不用看 HUD，看罐子里有多少汁就知道打到哪了。
    /// </summary>
    public class JuicerRig : MonoBehaviour
    {
        // ── 尺寸（世界单位，桌面顶面 y = 0）──────────────────────────
        private const float JarRadius  = 0.088f;   // 罐半径
        private const float JarHeight  = 0.250f;   // 罐高
        private const float JarBaseY   = 0.012f;   // 罐底离桌面
        private const float DieTopY    = 0.400f;   // 模具底板顶面
        private const float TopPlateY  = 0.700f;   // 顶板
        private const float PostR      = 0.152f;   // 立柱到中心的距离
        private const float PostThick  = 0.013f;
        private const float PlateR     = 0.125f;
        private const float PlateH     = 0.022f;

        private const float RamR       = 0.046f;
        private const float RamH       = 0.090f;
        private const float RamRestY   = 0.545f;   // 冲头静止高度（中心）
        private const float RamUpY     = 0.600f;   // 抬起
        private const float RamDownY   = 0.452f;   // 压到底

        /// <summary>刻度档数（目标分切成几段）。5 段 = 6 条刻度线，罐身上看得清。</summary>
        private const int TickDivisions = 5;

        // ── 颜色 ──────────────────────────────────────────────────────
        private static readonly Color GlassColor   = new Color(0.80f, 0.90f, 0.97f, 0.26f);
        private static readonly Color LiquidColor  = new Color(0.98f, 0.62f, 0.18f, 0.86f);
        private static readonly Color TickColor    = new Color(0.94f, 0.96f, 0.98f, 0.90f);
        private static readonly Color TickHotColor = new Color(1.00f, 0.76f, 0.26f, 1.00f);
        private static readonly Color RamColor     = new Color(0.80f, 0.83f, 0.88f);
        private static readonly Color ResidueColor = new Color(0.52f, 0.36f, 0.19f);
        private static readonly Color BladeColor   = new Color(0.90f, 0.93f, 0.97f);

        // ── 运行时 ────────────────────────────────────────────────────
        private Transform liquid;          // 液面
        private Renderer[] tickRenderers;  // 刻度环
        private float[]    tickValues;     // 每条刻度对应的分数

        private Transform ram;
        private Transform residue;
        private Transform blade;

        // 程序化机身的两组根节点 —— 有美术立绘时要整组关掉（见 Build 末尾）
        private GameObject frameRoot;
        private GameObject pressRoot;

        /// <summary>
        /// 有美术立绘（Art/Juicer/gameblender_*）时，是否把程序搭的机身盖掉。
        /// true  = 立绘就是这台机器（Frame + Press 全关，只留液体罐和刻度）
        /// false = 立绘和程序化机身同时出现（要对比造型时用）
        /// 想只盖一半（比如保留刀片挤出来的反馈），把下面关掉、自己 SetActive 对应节点即可。
        /// </summary>
        public const bool HideProceduralMachineWhenArtPresent = true;

        private int   score;
        private int   target = 1000;

        private float shownLevel;          // 平滑后的液面（0..1）
        private float wantLevel;           // 目标液面

        // ── 冲压动画 ──────────────────────────────────────────────────
        private enum Phase { Idle, RamRise, RamFall, Squash, BladeOut, RamRetract }

        private Phase phase = Phase.Idle;
        private float phaseT;

        public bool IsStamping { get { return phase != Phase.Idle; } }

        /// <summary>
        /// 罐口的世界坐标 —— 卡牌被投进去时的落点。
        ///
        /// 走 TransformPoint 而不是自己拼一个世界坐标：
        /// 整个榨汁机挂在 TableSetup 下、并且整体缩放过 0.75，
        /// 手写坐标一旦挪机器就会飞到桌子外面去。
        /// </summary>
        public Vector3 MouthWorld
        {
            get { return transform.TransformPoint(new Vector3(0f, JarBaseY + JarHeight + 0.07f, 0f)); }
        }

        // ══════════════════════════════════════════════════════════════
        //  建模
        // ══════════════════════════════════════════════════════════════

        public void Build()
        {
            BuildJar();
            BuildFrame();
            BuildPress();

            // 美术给了整机立绘就顶掉程序化机身（交的是 2D 立绘，不是模型贴图）
            //
            // ★ 整段包 try / catch：立绘出任何问题都只该是"这次的立绘没挂上"，
            //   绝不能让它冒出去 —— Build() 是在 TableSetup.Awake() 里调的，
            //   一抛异常后面的发牌 / HUD / 交互全都不执行，表现就是"桌上一张卡都没有"。
            //   （真踩过：贴图不可读时 GetPixels32 抛异常，卡全没了查了半天。）
            BlenderArt art = null;
            try
            {
                art = BlenderArt.Attach(this);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[JuicerRig] 立绘挂载失败，继续用程序化机身：" + e);
            }

            if (art != null && HideProceduralMachineWhenArtPresent)
            {
                if (frameRoot != null) frameRoot.SetActive(false);
                if (pressRoot != null) pressRoot.SetActive(false);
            }

            SetScore(0, target);
            shownLevel = 0f;
            ApplyLevel();
        }

        // ── 液体罐 + 刻度 ─────────────────────────────────────────────

        private void BuildJar()
        {
            GameObject root = new GameObject("Jar");
            root.transform.SetParent(transform, false);

            Material glass  = MakeGlass(GlassColor);
            Material liquidMat = MakeGlass(LiquidColor);

            float centerY = JarBaseY + JarHeight * 0.5f;

            // 玻璃外壳
            Cyl(root.transform, "Glass", JarRadius, JarHeight,
                new Vector3(0f, centerY, 0f), glass);

            // 罐底加厚一点，看着有底
            Cyl(root.transform, "Bottom", JarRadius * 0.96f, 0.012f,
                new Vector3(0f, JarBaseY + 0.006f, 0f), glass);

            // 液体：圆柱本身做满高，靠 scale.y 压出液面高度。
            // 这样液面变化只是改一个 scale，不用重建模型。
            GameObject liq = Cyl(root.transform, "Liquid", JarRadius * 0.92f, 1f,
                                 new Vector3(0f, JarBaseY, 0f), liquidMat);
            liquid = liq.transform;

            BuildTicks(root.transform);
        }

        private void BuildTicks(Transform parent)
        {
            GameObject ticks = new GameObject("Ticks");
            ticks.transform.SetParent(parent, false);

            int n = TickDivisions + 1;
            tickRenderers = new Renderer[n];
            tickValues    = new float[n];

            Material tickMat = CardFactory.MakeUnlit(null);
            tickMat.color = TickColor;

            for (int i = 0; i < n; i++)
            {
                float t = (float)i / TickDivisions;                  // 0..1
                float y = JarBaseY + t * JarHeight;

                tickValues[i] = t;   // 先存 0..1，SetScore 时换算成分数

                // 刻度环：比罐子略粗一圈的薄圆盘，读起来就是量杯上的刻度线
                GameObject ring = Cyl(ticks.transform, "Tick" + i,
                                      JarRadius * 1.045f, 0.0022f,
                                      new Vector3(0f, y, 0f), tickMat);

                tickRenderers[i] = ring.GetComponent<Renderer>();

                // 数字标签：放在罐子正前方（相机那一侧），面朝相机
                {
                    GameObject lbl = new GameObject("Label" + i);
                    lbl.transform.SetParent(ticks.transform, false);
                    lbl.transform.localPosition = new Vector3(0f, y, -JarRadius - 0.030f);
                    lbl.transform.localRotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);

                    TextMesh tm = lbl.AddComponent<TextMesh>();
                    tm.text = i.ToString();          // 占位，SetScore 时按真实分数改
                    tm.anchor = TextAnchor.MiddleCenter;
                    tm.alignment = TextAlignment.Center;
                    tm.fontSize = 48;
                    tm.characterSize = 1f;
                    tm.color = TickColor;

                    Font f = CardFactory.CjkFont();
                    if (f != null)
                    {
                        tm.font = f;
                        MeshRenderer mr = lbl.GetComponent<MeshRenderer>();
                        if (mr != null) mr.sharedMaterial = f.material;
                    }

                    // 字号用实测比例换算（TextMesh 世界字高 ≈ localScale × 3.4）
                    lbl.transform.localScale = Vector3.one * 0.0060f;
                    lbl.name = "Label" + i;
                }
            }
        }

        // ── 四柱机架 ──────────────────────────────────────────────────

        private void BuildFrame()
        {
            GameObject root = new GameObject("Frame");
            root.transform.SetParent(transform, false);
            frameRoot = root;

            Material metal = MakeMetal(1.00f);

            // 四根立柱：从桌面一直顶到顶板，罐子就在它们中间
            for (int i = 0; i < 4; i++)
            {
                float ang = Mathf.Deg2Rad * (45f + i * 90f);
                Vector3 p = new Vector3(Mathf.Cos(ang) * PostR, TopPlateY * 0.5f, Mathf.Sin(ang) * PostR);
                Cyl(root.transform, "Post" + i, PostThick, TopPlateY, p, metal);
            }

            // 模具底板（下压台）
            Cyl(root.transform, "DiePlate", PlateR, PlateH,
                new Vector3(0f, DieTopY - PlateH * 0.5f, 0f), metal);

            // 顶板
            Cyl(root.transform, "TopPlate", PlateR, PlateH,
                new Vector3(0f, TopPlateY + PlateH * 0.5f, 0f), metal);

            // 顶上的进料口，说明"东西是从上面进去的"
            Cyl(root.transform, "Hopper", PlateR * 0.42f, 0.070f,
                new Vector3(0f, TopPlateY + PlateH + 0.035f, 0f), MakeMetal(0.85f));
        }

        // ── 冲压部分 ──────────────────────────────────────────────────

        private void BuildPress()
        {
            GameObject root = new GameObject("Press");
            root.transform.SetParent(transform, false);
            pressRoot = root;

            // 冲头：挂在顶板下面，靠改 y 上下运动
            GameObject ramGo = Cyl(root.transform, "Ram", RamR, RamH,
                                   new Vector3(0f, RamRestY, 0f), MakeMetal(1.15f));
            ram = ramGo.transform;

            // 残渣：一坨压扁的球，代表榨完汁剩下的渣
            GameObject res = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            res.name = "Residue";
            res.transform.SetParent(root.transform, false);
            res.transform.localPosition = new Vector3(0f, DieTopY + 0.014f, 0f);
            res.transform.localScale = new Vector3(0.108f, 0.030f, 0.108f);
            Strip(res);
            res.GetComponent<Renderer>().material = MakeLit(ResidueColor, 0.05f, 0f);
            residue = res.transform;

            // 刀片：初始藏起来，冲压时才出现
            GameObject bl = Cyl(root.transform, "Blade", 0.054f, 0.007f,
                                new Vector3(0f, DieTopY + 0.008f, 0f), MakeLit(BladeColor, 0.60f, 0.15f));
            blade = bl.transform;

            // 刀片上的齿，让它一眼是"刀片"而不是一枚硬币
            for (int i = 0; i < 8; i++)
            {
                float ang = Mathf.Deg2Rad * i * 45f;
                GameObject tooth = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tooth.name = "Tooth" + i;
                tooth.transform.SetParent(bl.transform, false);
                tooth.transform.localPosition = new Vector3(Mathf.Cos(ang) * 0.56f, 0f, Mathf.Sin(ang) * 0.56f);
                tooth.transform.localRotation = Quaternion.Euler(0f, -i * 45f, 0f);
                tooth.transform.localScale = new Vector3(0.20f, 1.0f, 0.34f);
                Strip(tooth);
                tooth.GetComponent<Renderer>().material = MakeLit(BladeColor, 0.55f, 0.85f);
            }

            SetBladeVisible(false);
            SetResidue(1f);
        }

        // ══════════════════════════════════════════════════════════════
        //  得分 → 液面 + 刻度高亮
        // ══════════════════════════════════════════════════════════════

        public void SetScore(int newScore, int newTarget)
        {
            score  = Mathf.Max(0, newScore);
            target = Mathf.Max(1, newTarget);

            wantLevel = Mathf.Clamp01((float)score / target);

            // 刻度数字：把 0..1 换算回真实分数
            int n = tickValues != null ? tickValues.Length : 0;
            for (int i = 0; i < n; i++)
            {
                Transform lbl = transform.Find("Jar/Ticks/Label" + i);
                if (lbl == null) continue;
                TextMesh tm = lbl.GetComponent<TextMesh>();
                if (tm != null) tm.text = Mathf.RoundToInt(tickValues[i] * target).ToString();
            }

            RefreshTickHighlight();
        }

        /// <summary>
        /// 把液面瞬间设到当前得分，不做平滑。
        /// 给编辑器截图工具用 —— 编辑模式下 Update 不跑，光调 SetScore 液面永远是 0。
        /// </summary>
        public void SnapLevelToScore()
        {
            shownLevel = wantLevel;
            ApplyLevel();
            RefreshTickHighlight();
        }

        /// <summary>液面已经越过的那几条刻度点亮成琥珀色 —— 一眼看出打到第几档。</summary>
        private void RefreshTickHighlight()
        {
            if (tickRenderers == null) return;

            for (int i = 0; i < tickRenderers.Length; i++)
            {
                if (tickRenderers[i] == null) continue;

                bool passed = tickValues[i] <= shownLevel + 0.001f;
                tickRenderers[i].material.color = passed ? TickHotColor : TickColor;
            }
        }

        private void ApplyLevel()
        {
            if (liquid == null) return;

            // ★ 内置圆柱是"半径 0.5、高 2"的，所以：
            //   scale.y 要填 **半高**，位置 y 要填 **底面 + 半高**（圆柱以中心为原点）。
            //   直接把高度填进 scale.y 会得到两倍高、并且从罐子中间往上长。
            float h = JarHeight * shownLevel;
            liquid.localScale = new Vector3(JarRadius * 1.84f, Mathf.Max(0.0001f, h * 0.5f), JarRadius * 1.84f);
            liquid.localPosition = new Vector3(0f, JarBaseY + h * 0.5f, 0f);
        }

        // ══════════════════════════════════════════════════════════════
        //  冲压
        // ══════════════════════════════════════════════════════════════

        /// <summary>开始一次冲压：抬起 → 下压 → 残渣被压实 → 刀片被挤出来 → 抬回。</summary>
        public void PlayStamp()
        {
            if (IsStamping) return;

            // 每次冲压先补一坨新残渣 —— 上一次的已经被压成刀片用掉了
            SetResidue(1f);
            SetBladeVisible(false);

            phase = Phase.RamRise;
            phaseT = 0f;
        }

        private void Update()
        {
            // 液面平滑追赶目标值，不要瞬间跳
            if (Mathf.Abs(shownLevel - wantLevel) > 0.0005f)
            {
                shownLevel = Mathf.Lerp(shownLevel, wantLevel, 1f - Mathf.Exp(-6f * Time.deltaTime));
                ApplyLevel();
                RefreshTickHighlight();
            }

            TickStamp();
        }

        private void TickStamp()
        {
            if (phase == Phase.Idle) return;

            phaseT += Time.deltaTime;

            switch (phase)
            {
                case Phase.RamRise:
                    if (MoveRam(RamRestY, RamUpY, 0.18f)) { phase = Phase.RamFall; phaseT = 0f; }
                    break;

                case Phase.RamFall:
                    if (MoveRam(RamUpY, RamDownY, 0.20f)) { phase = Phase.Squash; phaseT = 0f; }
                    break;

                case Phase.Squash:
                    // 残渣被压扁、摊开 —— 这就是"冲压模具"的动作本身
                    {
                        float k = Mathf.Clamp01(phaseT / 0.10f);
                        SetResidue(Mathf.Lerp(1f, 0.28f, k));
                        if (k >= 1f) { phase = Phase.BladeOut; phaseT = 0f; }
                    }
                    break;

                case Phase.BladeOut:
                    // 刀片从被压实的残渣里挤出来，残渣同时被用掉 ——
                    // 这就是"刀片是残渣冲压出来的"那层意思
                    {
                        float k = Mathf.Clamp01(phaseT / 0.22f);
                        SetBladeVisible(true);
                        float s = Mathf.Lerp(0.15f, 1f, k * k * (3f - 2f * k));
                        blade.localScale = new Vector3(0.108f * s, 0.0035f, 0.108f * s);
                        SetResidue(Mathf.Lerp(0.28f, 0.05f, k));
                        if (k >= 1f) { phase = Phase.RamRetract; phaseT = 0f; }
                    }
                    break;

                case Phase.RamRetract:
                    if (MoveRam(RamDownY, RamRestY, 0.22f))
                    {
                        phase = Phase.Idle;
                        phaseT = 0f;
                    }
                    break;
            }
        }

        /// <summary>把冲头从 fromY 平滑挪到 toY。返回是否已经到位。</summary>
        private bool MoveRam(float fromY, float toY, float seconds)
        {
            if (ram == null) return true;

            float k = Mathf.Clamp01(phaseT / seconds);
            float e = k * k * (3f - 2f * k);          // SmoothStep，起停自然

            ram.localPosition = new Vector3(0f, Mathf.Lerp(fromY, toY, e), 0f);
            return k >= 1f;
        }

        private void SetResidue(float squash)
        {
            if (residue == null) return;

            // 压扁时横向摊开 —— 体积大致守恒，看着才像被压的
            float spread = 1f + (1f - squash) * 0.55f;
            residue.localScale = new Vector3(0.108f * spread, 0.030f * squash, 0.108f * spread);
            residue.localPosition = new Vector3(0f, DieTopY + 0.015f * squash, 0f);
        }

        private void SetBladeVisible(bool on)
        {
            if (blade != null) blade.gameObject.SetActive(on);
        }

        // ══════════════════════════════════════════════════════════════
        //  材质 / 建模小工具
        // ══════════════════════════════════════════════════════════════

        private static Shader Std()
        {
            Shader sh = Shader.Find("Standard");
            if (sh == null) sh = Shader.Find("Diffuse");
            return sh;
        }

        private static Material MakeLit(Color c, float gloss, float metallic)
        {
            Material m = new Material(Std());
            m.color = c;
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", gloss);
            if (m.HasProperty("_Metallic"))   m.SetFloat("_Metallic", metallic);
            return m;
        }

        /// <summary>
        /// 机身：拉丝金属贴图。
        ///
        /// ★ 金属度必须给低。
        ///   高金属度（0.65 以上）的材质**完全依赖环境反射**才有亮度 ——
        ///   这个场景没有反射探针、环境光又很暗，结果整个机器渲染成纯黑，
        ///   什么都看不出来（第一版就是这样）。降到 0.10 靠漫反射出亮度。
        /// </summary>
        private static Material MakeMetal(float brightness)
        {
            Material m = MakeLit(new Color(brightness * 0.95f, brightness * 0.97f, brightness * 1.00f),
                                 0.30f, 0.10f);
            m.mainTexture = ProceduralArt.BrushedMetal();
            m.mainTextureScale = new Vector2(3f, 1f);
            return m;
        }

        /// <summary>
        /// 透明材质。
        /// Standard 的透明模式不是改个颜色就行 —— 要手动把 _Mode / 混合因子 /
        /// 关键字 / 渲染队列 一起设成 Transparent，少一项就会渲染成不透明。
        /// </summary>
        private static Material MakeGlass(Color c)
        {
            Material m = new Material(Std());
            m.color = c;

            if (m.HasProperty("_Mode"))
            {
                m.SetFloat("_Mode", 3f);                                   // 3 = Transparent
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.DisableKeyword("_ALPHATEST_ON");
                m.EnableKeyword("_ALPHABLEND_ON");
                m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                m.renderQueue = 3000;
            }

            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.85f);
            if (m.HasProperty("_Metallic"))   m.SetFloat("_Metallic", 0.1f);
            return m;
        }

        /// <summary>
        /// 造一根圆柱。
        /// Unity 的内置圆柱是"半径 0.5、高 2"（y 从 −1 到 +1），
        /// 所以要拿 直径 和 半高 去缩，直接填半径和高度会差一倍。
        /// </summary>
        private static GameObject Cyl(Transform parent, string name,
                                      float radius, float height, Vector3 pos, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);

            Strip(go);
            go.GetComponent<Renderer>().material = mat;
            return go;
        }

        /// <summary>
        /// 拆掉碰撞体。
        /// 卡牌的拾取用的是全场景 Physics.Raycast，机器要是带碰撞体，
        /// 从它前面划过去的射线就会被挡住，卡点不动。
        /// </summary>
        private static void Strip(GameObject go)
        {
            Collider c = go.GetComponent<Collider>();
            if (c != null) c.enabled = false;
        }
    }
}
