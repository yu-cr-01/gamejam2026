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
    /// 【为什么"往下"要靠往近端让位，而不是靠贴着蜡烛】
    ///   桌面视角是 42° 俯视，投影下来有个反直觉的换算：
    ///     桌面往近端（−Z）挪 1 个单位 ≈ 屏幕上往下 490 像素，
    ///     而物件自己长高 1 个单位只有 570 像素 —— 两者量级相当，但**方向完全不同**。
    ///   也就是说想让一个东西出现在另一个东西"下面"，靠的是往镜头这边让位，
    ///   不是把它加高。第一版按"3D 里贴着蜡烛底"只让了 0.10，屏幕上才低 49 像素，
    ///   整支量筒糊在蜡烛身上 —— 3D 里的"贴着"和画面上的"下面"不是一回事。
    ///   最后让 0.18：筒口那圈正好落在蜡烛底下方几个像素，是"贴着蜡烛底"的读法。
    ///
    /// 【尺寸为什么这么小】
    ///   蜡烛高 0.179、卡牌 0.24×0.335（前排 z≈−0.4 一带铺开）。
    ///   量筒 0.20 高（比蜡烛的蜡身略高）、半径 0.024（比蜡烛的 0.030 细），
    ///   屏幕上约占 115 像素高 —— 够排下 6 道刻度环和数字，又不会戳到画面中央。
    ///   再往下/往右挪就会压到前排那几张关卡卡（它们在屏幕上的左边缘正好切在这块区域）。
    /// </summary>
    /// </summary>
    public class ScoreCylinderRig : MonoBehaviour
    {
        // ── 尺寸（世界单位，桌面顶面 y = 0）──────────────────────────
        /// <summary>筒身半径。比蜡烛（0.030）细一档，"量筒"的细长感靠这个。</summary>
        private const float TubeR = 0.024f;
        /// <summary>筒身底面：坐在底座上，所以从底座的厚度开始。</summary>
        private const float TubeBotY = 0.014f;
        /// <summary>筒身顶面。</summary>
        private const float TubeTopY = 0.190f;
        /// <summary>底座半径 / 厚度。比筒身粗一圈的厚玻璃盘 —— 没有它量筒像根插在桌上的吸管。</summary>
        private const float FootR = 0.034f;
        private const float FootH = 0.014f;

        /// <summary>口沿：筒口外翻的一圈薄环（只有环，不能是实心盘 —— 见 BuildRingMesh 的注释）。</summary>
        private const float RimR = 0.0305f;

        /// <summary>液体半径（筒身内壁）。</summary>
        private const float LiquidR = 0.0215f;
        /// <summary>液面量程的下端 = 刻度上的 0 分。</summary>
        private const float LiquidBotY = 0.018f;
        /// <summary>液面量程的上端 = 刻度上的目标分（满筒）。</summary>
        private const float LiquidTopY = 0.184f;

        /// <summary>刻度档数。5 段 = 6 道刻度环 —— 和榨汁机罐身同样的档数，读法不变。</summary>
        private const int TickDivisions = 5;

        /// <summary>
        /// 刻度环：比筒身粗出去多少 / 环本身的宽度。
        /// 环要"贴着玻璃外面一点点"，太宽像法兰盘、太窄在这个机位下只剩一个像素。
        /// </summary>
        private const float TickRingRise = 0.0006f;
        private const float TickRingWidth = 0.0034f;

        /// <summary>
        /// 数字标签的字号系数（TextMesh 世界字高 ≈ localScale × 3.4，和罐子同一个数）。
        ///
        /// 比罐子（0.0060）还小一档：罐子半径 0.088、数字摆在罐子正前方有地方，
        /// 这支筒子半径只有 0.024 —— 编号只能摆到筒身**左边**去（见 AddTickLabel），
        /// 而且关卡目标分是四位数（1000 / 1500 / 2000），字号再大"1000"就要顶出屏幕了。
        /// </summary>
        private const float LabelSize = 0.0058f;

        /// <summary>数字右边缘离筒壁留的空（数字是右对齐的，从这个 x 往左排）。</summary>
        private const float LabelGap = 0.008f;

        // ── 摆位 ──────────────────────────────────────────────────────
        /// <summary>
        /// 蜡烛的物体名 —— 走 TableTitleRig 那一个常量（见那里的说明：名字只写一次）。
        /// </summary>
        private const string CandleObjectName = TableTitleRig.CandleName;

        /// <summary>
        /// 相对蜡烛的水平偏移（桌面空间）。这两个数是**照着屏幕量出来的**，不是随手填的：
        ///
        /// · z 给 −0.38：桌面视角下"往近端让 1 个单位 ≈ 屏幕上往下 470 像素"，
        ///   让多少 = "筒口那圈要落在蜡烛底下方多远"。
        ///
        ///   ★ 这个数改过一次，改的原因写在下面（用户报的"穿模"）：
        ///     旧值 −0.24 是照"两支的**底**在屏幕上对齐"调的，算下来筒口那圈
        ///     **正好落在蜡烛底座上**（实拍：筒口顶在蜡烛底 ±4 像素内）——
        ///     于是屏幕上那六个数（1000/800/600/400/200/0）就贴着蜡烛底座排成一串，
        ///     用户的原话是「量筒与蜡烛几乎叠在一起……数字就压在蜡烛底座旁」。
        ///     （注意：这两样在**世界**里从来没相交 —— 量筒在 z ≈ +0.10、蜡烛在 z ≈ +0.34，
        ///       隔了 24 厘米；用户看到的是**屏幕**上前后投影叠在一起。所以修法只能是
        ///       "把其中一个在屏幕上挪开"，而不是"解决深度打架"。）
        ///     现在 −0.38：筒口那圈落到蜡烛底下方约 66 像素（同一机位、同一窗口尺寸实测），
        ///     既明确分开、又还是"挂在蜡烛下面"的读法。再大就脱开成一瓶孤零零的管子了。
        ///
        /// · x 给 +0.062：**透视会让"往下"的物件同时往画面外侧跑** ——
        ///   同一个世界 x，越靠近相机投影出来越靠左（蜡烛自己也是这样，它是斜的）。
        ///   不补这一点，量筒的底会落在蜡烛左边 70 像素处，看着是"左下方另一根管子"；
        ///   补上之后两支的**底**在屏幕上基本对齐，一眼就是"蜡烛下面那支量筒"。
        ///   往右再加就会压到前排素材级联在屏幕上的左边缘（那一列铺得很宽）。
        /// </summary>
        private const float CandleOffsetX = 0.062f;
        private const float CandleOffsetZ = -0.38f;

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
        /// <summary>
        /// 筒身玻璃的取色 = JuicerRig.GlassColor 但**更实一点**（alpha 0.26 → 0.42）。
        /// 罐子那边背后是立绘和机器、还有一片亮桌面，0.26 就够看出玻璃；
        /// 这支筒子孤零零立在画面左侧的暗桌面上，同样的透明度在截图里几乎看不见管子，
        /// 只剩六道环悬在空中，读不出"这是一支玻璃量筒"。
        /// </summary>
        private static readonly Color TubeGlassColor =
            new Color(JuicerRig.GlassColor.r, JuicerRig.GlassColor.g, JuicerRig.GlassColor.b, 0.42f);

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
            Material glass  = JuicerRig.MakeGlass(TubeGlassColor);
            Material liquidMat = JuicerRig.MakeGlass(JuicerRig.LiquidColor);

            // 底座
            JuicerRig.Cyl(transform, "Foot", FootR, FootH,
                          new Vector3(0f, FootH * 0.5f, 0f), glass);

            // 筒身
            float tubeH = TubeTopY - TubeBotY;
            JuicerRig.Cyl(transform, "Tube", TubeR, tubeH,
                          new Vector3(0f, TubeBotY + tubeH * 0.5f, 0f), glass);

            // 口沿：一圈薄环（不是实心盘 —— 实心盘会把筒口封成一个盖）
            AddRing(transform, "Rim", TubeTopY, BuildRingMesh(TubeR + 0.0008f, RimR, 32), glass);

            // 液体：和罐子同一招 —— 圆柱本身做满高，靠 scale.y 压出液面，
            // 液面变化只是改一个 scale，不用重建模型（见 ApplyLevel）。
            GameObject liq = JuicerRig.Cyl(transform, "Liquid", LiquidR, 1f,
                                           new Vector3(0f, LiquidBotY, 0f), liquidMat);
            liquid = liq.transform;

            BuildTicks();

            // ★ 建完必须先把液面压到 0 高度。
            //   Update 只在 |shownLevel − wantLevel| > 0.0005 时才 ApplyLevel，
            //   而开局两者都是 0 —— 不主动压这一次的话，液体圆柱会停在建模时的"满高"，
            //   桌面上直接立起一根 1 米高的橙柱子（正好把量筒整个盖住，截图里踩过）。
            //   （榨汁机那边的 Build 末尾是无条件 ApplyLevel 的，所以罐子没这个毛病。）
            ApplyLevel();

            PlaceUnderCandle();
        }

        /// <summary>
        /// 一道平躺圆环的网格（刻度环 / 口沿共用的几何）。
        ///
        /// 【为什么不用 JuicerRig.Cyl 造一个薄圆盘】
        /// 薄圆盘的**上表面是一整块实心圆**。桌面视角是 43° 俯视，
        /// 这块实心圆投影出来就是一个白色椭圆饼；六道叠在一起像条毛毛虫，
        /// 完全读不出"刻度线"（第一版就是这么干的，截图里一眼假）。
        /// 圆环中间是空的，剩下的那圈才是真正像刻度线的东西。
        ///
        /// 法线朝上（+Y）：相机永远在桌面上方（自由转头最低也只到 −14°），看不到环的背面，
        /// 所以不做双面 —— 省一半三角形。
        /// </summary>
        private static Mesh BuildRingMesh(float innerR, float outerR, int segs)
        {
            Vector3[] verts = new Vector3[segs * 2];
            int[] tris = new int[segs * 6];

            for (int i = 0; i < segs; i++)
            {
                float a = Mathf.PI * 2f * i / segs;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);

                verts[i * 2]     = new Vector3(c * innerR, 0f, s * innerR);   // 内圈
                verts[i * 2 + 1] = new Vector3(c * outerR, 0f, s * outerR);   // 外圈
            }

            for (int i = 0; i < segs; i++)
            {
                int n  = (i + 1) % segs;
                int v0 = i * 2, v1 = i * 2 + 1, v2 = n * 2, v3 = n * 2 + 1;

                // 这个绕序算出来的法线是 +Y（从上方看是逆时针）
                tris[i * 6]     = v0; tris[i * 6 + 1] = v2; tris[i * 6 + 2] = v1;
                tris[i * 6 + 3] = v1; tris[i * 6 + 4] = v2; tris[i * 6 + 5] = v3;
            }

            Mesh mesh = new Mesh();
            mesh.name = "ScoreRing";
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>把一道环挂到 parent 下、抬到 y 高度。返回它的渲染器（点亮刻度要用）。</summary>
        private static MeshRenderer AddRing(Transform parent, string name, float y,
                                            Mesh mesh, Material mat)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, y, 0f);

            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;

            // 桌面上的东西一律不带碰撞体（见 JuicerRig.Strip：会挡住卡牌的射线拾取）。
            // 网格是自己建的本来就没有 —— 这里只是把阴影关掉：一圈薄环投出来的影子
            // 是一整块圆盘，比环本身还显眼。
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return mr;
        }

        private void BuildTicks()
        {
            GameObject ticks = new GameObject("Ticks");
            ticks.transform.SetParent(transform, false);

            int n = TickDivisions + 1;
            tickRenderers = new Renderer[n];
            tickLabels    = new TextMesh[n];
            tickValues    = new float[n];

            // ★ 每道刻度**各要一份材质实例**。
            //   刻度是"越过的点亮成琥珀色"，六道环各有各的颜色；共用一个材质的话
            //   写最后一次的那个颜色会把六道全染成一样（全亮或全不亮）。
            //   罐子那边是靠 renderer.material 的"访问即克隆"拿到实例的 ——
            //   那在编辑模式会报材质泄漏，这里改成建的时候就分好，读写都走 sharedMaterial。
            Material tickProto = CardFactory.MakeUnlit(null);
            tickProto.color = JuicerRig.TickColor;

            // 六道环尺寸完全一样 → 网格只建一次，六个渲染器共用
            float inner = TubeR + TickRingRise;
            Mesh ringMesh = BuildRingMesh(inner, inner + TickRingWidth, 32);

            for (int i = 0; i < n; i++)
            {
                float t = (float)i / TickDivisions;                  // 0..1
                float y = LiquidBotY + t * (LiquidTopY - LiquidBotY);

                tickValues[i] = t;   // 先存 0..1，SetScore 时换算成真实分数

                Material tickMat = new Material(tickProto);
                tickMat.color = JuicerRig.TickColor;

                tickRenderers[i] = AddRing(ticks.transform, "Tick" + i, y, ringMesh, tickMat);

                // 数字：筒身左边、和环同高同 z（z 用 0 = 筒轴平面，右对齐的锚点正好在环的外缘外）
                tickLabels[i] = AddTickLabel(ticks.transform, "Label" + i,
                                             new Vector3(-(TubeR + LabelGap), y, 0f), i.ToString());
            }
        }

        /// <summary>
        /// 一道刻度的数字。**立在筒身左边、右对齐**，和它那道环同一个高度、
        /// 同一个 z（筒轴平面）—— 就是真量筒上那种"刻度线 + 旁边的数字"的排法。
        ///
        /// 【为什么不像罐子那样摆在筒身正前方】
        ///   ① 筒子太细：数字居中摆在正前方时，"1000"这种四位数比筒身还宽，
        ///      会横跨到蜡烛身上去（第一版就是这样，标题界面一屏的"1000/800/600…"糊成一团）；
        ///   ② 摆在"前方"（−Z）会被透视**往下拽**：这个机位下越靠近相机的点屏幕上越低，
        ///      数字会掉到自己那条刻度线下面二十几个像素，看着像串位。
        ///   摆到左边、和环同高同 z，这两个毛病一起没了 —— 右边正好是蜡烛，也腾出了地方。
        ///
        /// 朝向沿用罐子刻度数字那一套：TextMesh 的可读面朝局部 −Z，
        /// 让局部 +Z 指向世界 +Z（LookRotation(forward, up)），可读面就正对相机。
        /// 返回 TextMesh，让调用方自己存着 —— 不靠 transform.Find 去找回来。
        /// </summary>
        private static TextMesh AddTickLabel(Transform parent, string name, Vector3 localPos, string text)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            go.transform.localScale    = Vector3.one * LabelSize;

            TextMesh tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.anchor = TextAnchor.MiddleRight;      // 锚点 = 文字右缘 → 位数变多时往左长，不压到筒身
            tm.alignment = TextAlignment.Right;
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

            return tm;
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

            // 分数一变就先按**当前**液面点一次刻度（罐子也是这么做的）。
            // 少了这一句，0 分开局那发 SetScore 之后没人再动过液面，
            // "0" 那道刻度就一直是不亮的白色 —— 一眼看着像"最低档还没到"。
            // 液面动画随后每帧再点一次，越过的刻度是跟着液面一档一档亮起来的。
            RefreshTickHighlight();

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
                Color want = passed ? JuicerRig.TickHotColor : JuicerRig.TickColor;

                // 颜色没变就不写：液面在追目标分的那一秒里这个方法每帧都跑，
                // 而 TextMesh.color 一赋值就会重建一次文字网格，六行字白重建六十次。
                if (tickRenderers[i] != null && tickRenderers[i].sharedMaterial.color != want)
                {
                    // ★ sharedMaterial 而不是 material：
                    //   TablePreviewCapture 走的是**编辑模式**，那里访问 renderer.material
                    //   会复制一份材质实例、Unity 会报 "This will leak materials into the scene"
                    //   （BlenderArt 里为同一件事专门写过一段注释）。
                    //   这些材质是本组件自己 new 出来的、不挂在任何资源上，直接改没有副作用。
                    tickRenderers[i].sharedMaterial.color = want;
                }

                if (tickLabels[i] != null && tickLabels[i].color != want)
                    tickLabels[i].color = want;
            }
        }

        private void ApplyLevel()
        {
            if (liquid == null) return;

            // 0 分就是"一滴都没有"：这么薄的圆盘只剩一个亮橙色的点，
            // 看着像筒底积了一层汁 —— 干脆整块藏起来，空筒就是空的。
            bool any = shownLevel > 0.002f;
            if (liquid.gameObject.activeSelf != any) liquid.gameObject.SetActive(any);
            if (!any) return;

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
            VerifyCandleSeparation();

            // 液面平滑追赶目标值 —— 得分是"掉"下来的，一跳到位看不出来涨了多少
            if (Mathf.Abs(shownLevel - wantLevel) > 0.0005f)
            {
                shownLevel = Mathf.Lerp(shownLevel, wantLevel, 1f - Mathf.Exp(-6f * Time.deltaTime));
                ApplyLevel();
                RefreshTickHighlight();
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  自检：量筒和蜡烛**在屏幕上**不许贴到一起
        //
        //  【为什么这条非要有】用户报的那一幕「量筒与蜡烛几乎叠在一起，数字就压在蜡烛底座旁」
        //    是**屏幕空间**的事：这两样在世界里隔了 24 厘米（量筒 z ≈ +0.10、蜡烛 z ≈ +0.34），
        //    任何"世界包围盒相交"的自检都判它们没问题 —— 而玩家看到的确实是一坨。
        //    所以判据必须是"把它们投到屏幕上、量两块屏幕包围盒之间还剩多少像素"。
        //
        //  【为什么只在"有东西变了"的时候量】这个东西的位置 / 相机机位 / 窗口分辨率
        //    任何一个变了，屏幕间距都会变（自由转头转到某个角度也可能把它们叠起来）——
        //    但每帧都打日志就是刷屏。所以：每 15 帧看一次、四个输入都没变就直接跳过，
        //    而且只在**结论翻转**时说话（正常一局日志里最多两行：开局 ✓、哪天坏了 ★）。
        // ══════════════════════════════════════════════════════════════

        /// <summary>结论翻转时最多打几行（防"玩家一直转视角"把日志刷满）。</summary>
        private const int SepLineCap = 6;

        /// <summary>上次量的时候"结论是什么"：−1 = 还没量过、0 = 分开、1 = 叠影。</summary>
        private int sepState = -1;
        private int sepLines;

        private Camera    sepCam;
        private Vector3   sepLastPos    = new Vector3(9999f, 9999f, 9999f);
        private Vector3   sepLastCamPos = new Vector3(9999f, 9999f, 9999f);
        private Quaternion sepLastCamRot;
        private int       sepLastW, sepLastH;

        private void VerifyCandleSeparation()
        {
            if (candle == null) return;                     // 蜡烛还没建出来/还没找到，量不了
            if (Time.frameCount % 15 != 0) return;

            if (sepCam == null)
            {
                sepCam = Camera.main;
                if (sepCam == null) sepCam = FindObjectOfType<Camera>();
                if (sepCam == null) return;
            }

            // 四个输入（自己的位置 / 相机位姿 / 窗口分辨率）都没变 → 结论不可能变，跳过
            bool moved = (transform.position - sepLastPos).sqrMagnitude > 1e-8f
                      || (sepCam.transform.position - sepLastCamPos).sqrMagnitude > 1e-8f
                      || Quaternion.Angle(sepCam.transform.rotation, sepLastCamRot) > 0.05f
                      || Screen.width != sepLastW || Screen.height != sepLastH;
            if (!moved) return;

            sepLastPos    = transform.position;
            sepLastCamPos = sepCam.transform.position;
            sepLastCamRot = sepCam.transform.rotation;
            sepLastW      = Screen.width;
            sepLastH      = Screen.height;

            float ax0, ay0, ax1, ay1, bx0, by0, bx1, by1;
            if (!ScreenBox(transform, sepCam, out ax0, out ay0, out ax1, out ay1)) return;
            if (!ScreenBox(candle,    sepCam, out bx0, out by0, out bx1, out by1)) return;

            // 两个屏幕盒在上下 / 左右两个方向上各自的间隙：任一方向有正间隙 = 画面上不重叠
            float gapX = Mathf.Max(bx0 - ax1, ax0 - bx1);
            float gapY = Mathf.Max(by0 - ay1, ay0 - by1);
            bool  apart = gapX > 0f || gapY > 0f;
            float nearest = Mathf.Max(gapX, gapY);          // 真正"离多远"取两者里大的那个

            int state = apart ? 0 : 1;
            if (state == sepState) return;
            sepState = state;

            if (sepLines >= SepLineCap) return;
            sepLines++;

            string text = "[V21][得分板] 量筒与蜡烛的屏幕包围盒：量筒 x " + ax0.ToString("0") + "~" + ax1.ToString("0")
                        + "、y " + ay0.ToString("0") + "~" + ay1.ToString("0")
                        + "｜蜡烛 x " + bx0.ToString("0") + "~" + bx1.ToString("0")
                        + "、y " + by0.ToString("0") + "~" + by1.ToString("0")
                        + "　（屏幕 " + Screen.width + "×" + Screen.height + "）";

            if (apart)
                Debug.Log(text + "　→ ✓ 分开 " + nearest.ToString("0") + " px");
            else
                Debug.LogWarning(text + "　→ ★ 叠影 " + (-gapX).ToString("0") + " × " + (-gapY).ToString("0")
                                 + " px —— 量筒的刻度数字会压在蜡烛身上（改 CandleOffsetX / CandleOffsetZ）");
        }

        /// <summary>
        /// 一个物体（含子物体，只算活着的渲染器）在屏幕上的包围盒（左上原点）。
        /// 有角落在相机背后就返回 false —— 那时候屏幕盒没有意义。
        /// </summary>
        private static bool ScreenBox(Transform root, Camera cam,
                                      out float x0, out float y0, out float x1, out float y1)
        {
            x0 = float.MaxValue; y0 = float.MaxValue;
            x1 = float.MinValue; y1 = float.MinValue;

            Renderer[] rs = root.GetComponentsInChildren<Renderer>();
            bool any = false;

            for (int i = 0; i < rs.Length; i++)
            {
                Renderer r = rs[i];
                if (r == null || !r.enabled) continue;
                if (!r.gameObject.activeInHierarchy) continue;

                Bounds b = r.bounds;
                any = true;

                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = new Vector3((c & 1) == 0 ? b.min.x : b.max.x,
                                                 (c & 2) == 0 ? b.min.y : b.max.y,
                                                 (c & 4) == 0 ? b.min.z : b.max.z);
                    Vector3 s = cam.WorldToScreenPoint(corner);
                    if (s.z <= 0f) return false;

                    float sy = Screen.height - s.y;
                    if (s.x < x0) x0 = s.x;
                    if (s.x > x1) x1 = s.x;
                    if (sy  < y0) y0 = sy;
                    if (sy  > y1) y1 = sy;
                }
            }

            return any;
        }
    }
}
