using UnityEngine;

namespace GameJam.Prototype
{
    /// <summary>
    /// 量筒得分板 —— 桌面左侧、**蜡烛正下方**那支细长玻璃量筒。
    ///
    /// 【为什么从榨汁机的罐子搬到这儿】
    ///   罐身刻度（JuicerRig.BuildJar）本来就是得分面板：液面 = 得分 / 目标分。
    ///   问题是它长在机器身上：机器在桌子右后角、还被美术立绘挡掉一大半，
    ///   玩家想知道"还差多少分"得先把视线挪到画面最右上角去找那半截罐子。
    ///   搬到蜡烛下面之后 ——
    ///     · 蜡烛是画面左侧唯一的高物件，量筒贴着它的底，视线扫过去就有把"尺子"；
    ///     · 玻璃筒 + 液面 + 刻度环 + 数字这套语言和罐子一模一样，不用重新学；
    ///     · 榨汁机那边只剩"机器"，冲压动作不再被罐子挤在四柱中间。
    ///   液面高度仍然 = 得分 / 目标分，越过的刻度（环 + 数字）点亮成琥珀色。
    ///
    /// 【谁喂它分数】
    ///   唯一入口仍然是 <see cref="JuicerRig.SetScore"/>（TableTurnLoop.SyncJuicer 和
    ///   TableRulesV21 都只认那一个方法），由 JuicerRig 内部转发过来 ——
    ///   得分面板换地方纯粹是表现层的事，规则层一行都没改。
    ///
    /// 【摆位：为什么"相对蜡烛"而不是写死一个坐标】
    ///   蜡烛是 TableTitleRig 在 Begin() 里才建的，而本组件是 TableSetup.BuildJuicer()
    ///   里跟着榨汁机一起建的 —— 建的那一刻蜡烛还不存在，拿不到它的 Transform。
    ///   所以：先按镜像常量摆一次，之后每帧再看一眼蜡烛在哪、跟过去。
    ///   好处是蜡烛挪了位置（改 TableTitleRig.CandleAt）量筒自己会跟着走，
    ///   不会出现"蜡烛搬走了、量筒留在原地"的两处各写一个数。
    ///
    /// 【为什么是"往近端让 0.28"而不是"紧贴着蜡烛底"】
    ///   桌面视角是 42° 俯视，投影下来有个反直觉的换算：
    ///     桌面往近端（−Z）挪 1 个单位 ≈ 屏幕上往下 490 像素，
    ///     而物件自己长高 1 个单位只有 718 像素 —— 比例接近，但方向不同。
    ///   换句话说 **"往近端让一点"和"长高一截"在屏幕上效果相当**，
    ///   所以想让它落在蜡烛下面，靠的是让位、不是高度。
    ///   （第一版按"3D 里贴着蜡烛底"给了 0.10 的让位，屏幕上只低了 49 像素，
    ///     整支量筒全糊在蜡烛身上 —— 3D 里的"贴着"和画面上的"下面"不是一回事。）
    ///   0.28 是量出来的：筒顶正好差几像素压在蜡烛底上，既像"贴着蜡烛"，又不挡它。
    ///
    /// 【尺寸为什么这么小】
    ///   蜡烛高 0.179，卡牌 0.24×0.335 摆在前排 z≈−0.4 一带。
    ///   量筒 0.20 高（比蜡烛略高一点点）、半径 0.026（比蜡烛的 0.030 细），
    ///   屏幕上约占 144 像素高 —— 够看清 6 道刻度和数字，又不会戳到画面中央。
    ///   再高就会顶到前排的关卡卡（那排卡在屏幕上的左边缘正好切在这块区域）。
    /// </summary>
    public class ScoreCylinderRig : MonoBehaviour
    {
        // ── 尺寸（世界单位，桌面顶面 y = 0）──────────────────────────
        /// <summary>筒身半径。比蜡烛（0.030）细一档，"量筒"的细长感靠这个。</summary>
        private const float TubeR = 0.026f;
        /// <summary>筒身底面：坐在底座上，所以从底座的厚度开始。</summary>
        private const float TubeBotY = 0.014f;
        /// <summary>筒身顶面。</summary>
        private const float TubeTopY = 0.190f;
        /// <summary>底座半径 / 厚度。比筒身粗一圈的厚玻璃盘 —— 没有它量筒像根插在桌上的吸管。</summary>
        private const float FootR = 0.036f;
        private const float FootH = 0.014f;
        /// <summary>口沿：略往外翻的一圈，量筒的"嘴"就在这儿。</summary>
        private const float RimR = 0.031f;
        private const float RimH = 0.011f;

        /// <summary>液体半径（筒身内壁）。</summary>
        private const float LiquidR = 0.0235f;
        /// <summary>液面量程的下端 = 刻度上的 0 分。</summary>
        private const float LiquidBotY = 0.018f;
        /// <summary>液面量程的上端 = 刻度上的目标分（满筒）。</summary>
        private const float LiquidTopY = 0.184f;

        /// <summary>刻度档数。5 段 = 6 道刻度环 —— 和榨汁机罐身同样的档数，读法不变。</summary>
        private const int TickDivisions = 5;

        /// <summary>刻度环半径系数：比筒身粗一点点，读起来才是"刻在玻璃上的线"。</summary>
        private const float TickRingK = 1.07f;
        private const float TickRingH = 0.0018f;

        /// <summary>数字标签的字号系数（TextMesh 世界字高 ≈ localScale × 3.4，和罐子同一个数）。</summary>
        private const float LabelSize = 0.0060f;
        /// <summary>数字离筒壁往前（−Z）多少 —— 相机在近端，往前才不会被玻璃盖住。</summary>
        private const float LabelForward = 0.021f;

        // ── 摆位 ──────────────────────────────────────────────────────
        /// <summary>蜡烛的物体名（TableTitleRig.BuildCandle → NewRoot("TitleCandle", CandleAt)）。</summary>
        private const string CandleObjectName = "TitleCandle";

        /// <summary>
        /// 相对蜡烛的水平偏移（桌面空间）。
        /// x 给 0：屏幕上是"正下方"最直白的读法 —— 偏一点就成了"斜下方的另一个瓶子"。
        /// z 给 −0.28：见类注释里那段俯视投影的换算。
        /// </summary>
        private const float CandleOffsetX = 0f;
        private const float CandleOffsetZ = -0.28f;

        /// <summary>
        /// 找不到蜡烛时的兜底坐标 —— 和 TableTitleRig.CandleAt 同一个值。
        /// 那个字段是 private，为它开一个 public 属性要动开场那个文件；而这里只是兜底：
        /// 蜡烛正常都在（开场建一次、进关卡也不销毁），真正用的是它自己的坐标。
        /// </summary>
        private static readonly Vector3 CandleFallback = new Vector3(-0.80f, 0f, 0.34f);

        /// <summary>最多找几次蜡烛（每 30 帧一次）。找不到就用兜底坐标，不再每帧 Find。</summary>
        private const int CandleLookupTries = 10;
        private const int CandleLookupEvery = 30;

        // ── 运行时 ────────────────────────────────────────────────────
        private Transform  liquid;          // 液面
        private Renderer[] tickRenderers;   // 刻度环
        private TextMesh[] tickLabels;      // 刻度数字
        private float[]    tickValues;      // 每道刻度对应的档位（0..1）

        private Transform candle;
        private int       lookupTries;
        private int       nextLookupFrame;

        private int   score;
        private int   target = 1000;

        private float shownLevel;           // 平滑后的液面（0..1）
        private float wantLevel;            // 目标液面

        /// <summary>液面是不是已经在追目标值（截图工具用它判断要不要 Snap）。</summary>
        public bool LevelSettled { get { return Mathf.Abs(shownLevel - wantLevel) <= 0.0005f; } }

        // ══════════════════════════════════════════════════════════════
        //  建
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 在 host 下建一支量筒并返回它。
        ///
        /// host 传的是**桌子**（TableSetup），不是榨汁机机身 ——
        /// 机身整体缩放过 0.75，挂进去量筒会跟着缩、坐标还要反着算回桌面空间；
        /// 而"蜡烛在哪、量筒就在哪"这套定位本来就是桌面空间的。
        /// </summary>
        public static ScoreCylinderRig Build(Transform host)
        {
            GameObject go = new GameObject("ScoreCylinder");
            if (host != null) go.transform.SetParent(host, false);

            ScoreCylinderRig rig = go.AddComponent<ScoreCylinderRig>();
            rig.BuildParts();
            return rig;
        }

        private void BuildParts()
        {
            // 玻璃 / 果汁的取色和材质配方都从 JuicerRig 拿：
            // 得分面板只有一套配色，罐子和量筒是同一个东西的前后两版，
            // 各抄一份 MakeGlass（那 20 行里少设一个关键字就会渲染成不透明）迟早走岔。
            Material glass  = JuicerRig.MakeGlass(JuicerRig.GlassColor);
            Material liquidMat = JuicerRig.MakeGlass(JuicerRig.LiquidColor);

            // 底座
            JuicerRig.Cyl(transform, "Foot", FootR, FootH,
                          new Vector3(0f, FootH * 0.5f, 0f), glass);

            // 筒身
            float tubeH = TubeTopY - TubeBotY;
            JuicerRig.Cyl(transform, "Tube", TubeR, tubeH,
                          new Vector3(0f, TubeBotY + tubeH * 0.5f, 0f), glass);

            // 口沿
            JuicerRig.Cyl(transform, "Rim", RimR, RimH,
                          new Vector3(0f, TubeTopY + RimH * 0.5f, 0f), glass);

            // 液体：和罐子同一招 —— 圆柱本身做满高，靠 scale.y 压出液面，
            // 液面变化只是改一个 scale，不用重建模型（见 ApplyLevel）。
            GameObject liq = JuicerRig.Cyl(transform, "Liquid", LiquidR, 1f,
                                           new Vector3(0f, LiquidBotY, 0f), liquidMat);
            liquid = liq.transform;

            BuildTicks();
            PlaceUnderCandle();
        }

        private void BuildTicks()
        {
            GameObject ticks = new GameObject("Ticks");
            ticks.transform.SetParent(transform, false);

            int n = TickDivisions + 1;
            tickRenderers = new Renderer[n];
            tickLabels    = new TextMesh[n];
            tickValues    = new float[n];

            Material tickMat = CardFactory.MakeUnlit(null);
            tickMat.color = JuicerRig.TickColor;

            for (int i = 0; i < n; i++)
            {
                float t = (float)i / TickDivisions;                  // 0..1
                float y = LiquidBotY + t * (LiquidTopY - LiquidBotY);

                tickValues[i] = t;   // 先存 0..1，SetScore 时换算成真实分数

                // 刻度环：比筒身略粗一圈的薄圆盘，和罐子上的刻度线是同一种东西
                GameObject ring = JuicerRig.Cyl(ticks.transform, "Tick" + i,
                                                TubeR * TickRingK, TickRingH,
                                                new Vector3(0f, y, 0f), tickMat);
                tickRenderers[i] = ring.GetComponent<Renderer>();

                AddTickLabel(ticks.transform, "Label" + i,
                             new Vector3(0f, y, -TubeR - LabelForward), i.ToString());
                tickLabels[i] = ticks.transform.Find("Label" + i).GetComponent<TextMesh>();
            }
        }

        /// <summary>
        /// 一道刻度的数字。**立着、面朝近端相机** —— 不是 CardFactory.AddText 那种
        /// 平躺在桌面上的字（那个是给卡面和桌面标签用的，用在这里会变成趴在地上）。
        ///
        /// 朝向沿用罐子刻度数字那一套：TextMesh 的可读面朝局部 −Z，
        /// 让局部 +Z 指向世界 +Z（LookRotation(forward, up)），可读面就正对相机。
        /// </summary>
        private static void AddTickLabel(Transform parent, string name, Vector3 localPos, string text)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            go.transform.localScale    = Vector3.one * LabelSize;

            TextMesh tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontSize = 48;              // 配合 localScale 缩放，实际字号由 LabelSize 决定
            tm.characterSize = 1f;
            tm.color = JuicerRig.TickColor;
            tm.richText = false;

            // 数字是 ASCII，但字体仍然走工程那条中文字体 —— 和罐子、卡面上的字同一套字形，
            // 而且以后想在这行加"分"字不用换字体。
            Font f = CardFactory.CjkFont();
            if (f != null)
            {
                tm.font = f;
                MeshRenderer mr = go.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = f.material;
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  摆位：蜡烛正下方
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 摆到蜡烛正下方。每帧调一次：蜡烛是开场 rig 在 Begin() 里建的，
        /// 比本组件晚，所以这里要"边跑边看"；找不到就用兜底坐标。
        ///
        /// 直接写 transform.position 是够的：父节点是 TableSetup 那层（不旋转、不缩放），
        /// 桌面空间就是世界空间。真挂到会转的节点下时要改成 InverseTransformPoint。
        /// </summary>
        private void PlaceUnderCandle()
        {
            if (candle == null && lookupTries < CandleLookupTries && Time.frameCount >= nextLookupFrame)
            {
                lookupTries++;
                nextLookupFrame = Time.frameCount + CandleLookupEvery;

                GameObject go = GameObject.Find(CandleObjectName);
                if (go != null) candle = go.transform;
            }

            Vector3 at = candle != null ? candle.position : CandleFallback;
            Vector3 want = new Vector3(at.x + CandleOffsetX, 0f, at.z + CandleOffsetZ);

            // 位置没变就别写 —— 每帧写一次 Transform 会白白把层级标脏
            if ((transform.position - want).sqrMagnitude > 1e-8f) transform.position = want;
        }

        // ══════════════════════════════════════════════════════════════
        //  得分 → 液面 + 刻度
        // ══════════════════════════════════════════════════════════════

        public void SetScore(int newScore, int newTarget)
        {
            score  = Mathf.Max(0, newScore);
            target = Mathf.Max(1, newTarget);

            wantLevel = Mathf.Clamp01((float)score / target);

            // 刻度数字 = 档位 × 目标分。目标分每关都不一样，所以数字是算出来的、不能写死
            // （罐子那边是每次 SetScore 拿 transform.Find("Jar/Ticks/Label"+i) 找的，
            //   路径一改就静默不更新；这里建模时就把引用存下来了。）
            int n = tickLabels != null ? tickLabels.Length : 0;
            for (int i = 0; i < n; i++)
            {
                if (tickLabels[i] == null) continue;
                tickLabels[i].text = Mathf.RoundToInt(tickValues[i] * target).ToString();
            }

            // 编辑模式下 Update 不跑，光设 wantLevel 液面永远是空的 ——
            // TablePreviewCapture 就是在编辑模式调这个方法的，所以在那儿直接一步到位。
            if (!Application.isPlaying) SnapLevelToScore();
        }

        /// <summary>
        /// 把液面瞬间设到当前得分，不做平滑。
        /// 和 JuicerRig.SnapLevelToScore 是同一个口子：编辑模式截图用
        /// （Update 不跑，液面不会自己追过去）。
        /// </summary>
        public void SnapLevelToScore()
        {
            PlaceUnderCandle();     // 编辑模式下 Update 不跑，位置也在这里补一次
            shownLevel = wantLevel;
            ApplyLevel();
            RefreshTickHighlight();
        }

        /// <summary>
        /// 把液面已经越过的那几道刻度点亮成琥珀色，刻度数字跟着一起点。
        ///
        /// 数字也点：现在量筒是**唯一**的得分面板，扫一眼要能直接读"打到第几档"，
        /// 而不是先看液面停在哪两条线之间再数（罐子时代旁边还有 HUD 兜着，这里没有）。
        /// </summary>
        private void RefreshTickHighlight()
        {
            if (tickRenderers == null) return;

            for (int i = 0; i < tickRenderers.Length; i++)
            {
                bool passed = tickValues[i] <= shownLevel + 0.001f;

                if (tickRenderers[i] != null)
                {
                    // ★ sharedMaterial 而不是 material：
                    //   TablePreviewCapture 走的是**编辑模式**，那里访问 renderer.material
                    //   会复制一份材质实例、Unity 会报 "This will leak materials into the scene"
                    //   （BlenderArt 里为同一件事专门写过一段注释）。
                    //   这些材质是本组件自己 new 出来的、不挂在任何资源上，直接改没有副作用。
                    tickRenderers[i].sharedMaterial.color = passed ? JuicerRig.TickHotColor : JuicerRig.TickColor;
                }

                if (tickLabels[i] != null)
                    tickLabels[i].color = passed ? JuicerRig.TickHotColor : JuicerRig.TickColor;
            }
        }

        private void ApplyLevel()
        {
            if (liquid == null) return;

            // ★ 和罐子同一套换算：内置圆柱是"半径 0.5、高 2"的，所以
            //   scale.y 要填**半高**、位置 y 要填**底面 + 半高**（圆柱以中心为原点）。
            //   直接把高度填进 scale.y 会得到两倍高、而且从筒子中间往上长。
            float h = (LiquidTopY - LiquidBotY) * shownLevel;
            liquid.localScale = new Vector3(LiquidR * 2f, Mathf.Max(0.0001f, h * 0.5f), LiquidR * 2f);
            liquid.localPosition = new Vector3(0f, LiquidBotY + h * 0.5f, 0f);
        }

        private void Update()
        {
            PlaceUnderCandle();

            // 液面平滑追赶目标值 —— 得分是"掉"下来的，一跳到位看不出来涨了多少
            if (Mathf.Abs(shownLevel - wantLevel) > 0.0005f)
            {
                shownLevel = Mathf.Lerp(shownLevel, wantLevel, 1f - Mathf.Exp(-6f * Time.deltaTime));
                ApplyLevel();
                RefreshTickHighlight();
            }
        }
    }
}
