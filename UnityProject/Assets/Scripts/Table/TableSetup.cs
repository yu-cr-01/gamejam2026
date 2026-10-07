using System.Collections.Generic;
using UnityEngine;
using GameJam.Data;

namespace GameJam.Prototype
{
    /// <summary>
    /// 程序化搭出整个 3D 桌面场景。
    ///
    /// 【为什么不手摆场景】
    /// .unity 场景文件是 YAML，多人协作极易冲突，而且在 GUI 里才能编辑。
    /// 全部用代码生成的好处：场景文件保持空的，谁改代码谁负责，
    /// 合并冲突从"整个场景报废"变成"一个文件的几行"。
    ///
    /// 【坐标系约定】
    ///   桌面顶面 = y 0
    ///   远端 = +Z，近端（玩家这侧）= -Z
    ///   相机在 -Z 侧斜俯视，所以卡面文字的上方向是 +Z
    ///
    /// 布局：
    ///        ┌──────────────────────┐  +Z 远端
    ///        │   [槽][槽][槽][槽]   │
    ///        │   [槽][槽][槽][槽]   │
    ///        │   [手牌][手牌][手牌] │
    ///        └──────────────────────┘  -Z 近端（相机）
    /// </summary>
    public class TableSetup : MonoBehaviour
    {
        public Camera           cam;
        public CameraRig        rig;
        public TableBoard       board;
        public TableInteraction interaction;
        public Transform        cardsRoot;

        /// <summary>桌面上的回合循环 —— 手牌、投放区、得分的唯一驱动源</summary>
        public TableTurnLoop    turnLoop;

        /// <summary>牌组 / 刀片两个选择环节的桌面表现</summary>
        public TableChoiceRig   choiceRig;

        /// <summary>开场界面（书 / 木牌 / 蜡烛）</summary>
        public TableTitleRig    titleRig;

        /// <summary>视角名，供 HUD 和快捷键使用。最后两个是自由转头和榨汁机特写。</summary>
        public static readonly string[] ViewNames =
        {
            BoardView, "hand", "top", "juicer", CameraRig.FreeView
        };

        public static readonly string[] ViewLabels =
        {
            "桌面视角", "手牌特写", "俯视", "榨汁机特写", "自由视角"
        };

        /// <summary>
        /// 「桌面视角」的机位名。**它的机位不是写死的坐标，是算出来的**（见 <see cref="ReframeBoardView"/>）——
        /// 所以这里给它一个常量名，免得"算出来的机位"和 HUD / 快捷键里那个字符串哪天对不上。
        /// </summary>
        public const string BoardView = "board";

        /// <summary>
        /// 「开场界面」那一屏的机位名（**故意不在 <see cref="ViewNames"/> 里** ——
        /// 玩家能选的视角还是那五个，这一条只是开场自己停的地方）。
        ///
        /// 【为什么要有它】用户原话：「不是，你怎么换摄像头视角了？换回来」——配图是开场那一屏。
        ///   根因是「桌面视角」被改成了**按桌上内容拟合**（那是为了修"手牌被视口下边缘切掉"，
        ///   是用户要的改动 ✓），而开场界面用的**就是**桌面视角这台机位
        ///   （TableSetup.Awake 里 BuildCamera 之后 SnapTo 它，之后进开场再没有别人动机位）——
        ///   于是"修手牌取景"顺手把开场那一屏也重新构图了（这是副作用 ✗）。
        ///   修法是把两件事拆开：开场停在**原来那组写死的坐标**上（见 <see cref="TitleEye"/>），
        ///   桌面视角继续按内容拟合。取景回到"和以前一样"，手牌修复一点没退。
        ///
        /// 【它是怎么被套用的】进开场（<see cref="TableTitleRig.Build"/>）时切过来，
        ///   离开开场（<see cref="TableTitleRig.Clear"/>）时切回桌面视角 ——
        ///   两处都在开场 rig 自己身上，不改回合循环的阶段机。
        /// </summary>
        public const string TitleView = "title";

        /// <summary>
        /// 开场那一屏的机位 —— **就是从 755313c（"回合循环搬到 3D 桌面上跑"）起
        /// BuildCamera 里一直写死的那一组坐标**，一个数都没动过：
        ///   相机 (0, 1.05, −1.02)、注视 (0, 0, 0.10)（fov 还是 <see cref="BuildCamera"/> 里的 42°）。
        ///
        /// 【为什么不直接让开场也用 BoardView】因为 BoardView 现在**不是坐标、是拟合结果**
        ///   （见 <see cref="ReframeBoardView"/>）：它按"手牌那一排也在画面里"往外退，
        ///   算出来比这组坐标远 ~25%、还往右挪了 ~0.17。开场那一屏上只有书 / 木牌 / 蜡烛 /
        ///   破壁机，没有任何必须装下的手牌 —— 那就没有任何理由改它的构图。
        ///
        /// 【为什么两个常量写在一起还留着 BoardView 那次注册】那次注册现在是**兜底**：
        ///   拟合万一算不出来（点集空着），画面至少还有相机可用。两者同源（同一个 pose），
        ///   所以"兜底"和"开场"永远是一台机位，不会各写一份。
        /// </summary>
        private static readonly Vector3 TitleEye  = new Vector3(0f, 1.05f, -1.02f);
        private static readonly Vector3 TitleLook = new Vector3(0f, 0f, 0.10f);

        /// <summary>
        /// 桌面材质里那个"还没贴木纹之前"的染色。
        ///
        /// ★ 只在木纹贴图缺失（ProceduralArt 被换掉 / 取不到）时才是主角 —— 见 TableTint 的
        ///   说明为什么它必须浅：深色染色会把木纹贴图的亮度再乘一次，桌面就成了黑的。
        /// </summary>
        private static readonly Color TableColor  = new Color(0.62f, 0.50f, 0.42f);

        /// <summary>
        /// 卡槽指示块（也就是吸附判定用的"框"）的尺寸。两处共用，别各写一个数。
        ///
        /// ★ public 是给 **桌面素材级联的布局自检**用的（TableRulesV21.CollectLayoutObstacles）：
        ///   那边要按"框的真实矩形"量"卡有没有压到框"。让自检自己抄一份尺寸的话，
        ///   改了这里、自检还按旧尺寸判，就会变成"框被压住了而日志说没事"。
        /// </summary>
        public const float SlotSizeX = 0.255f;
        public const float SlotSizeZ = 0.350f;

        public readonly List<PlayCard> hand = new List<PlayCard>();

        /// <summary>本局用的牌组名 —— 右键检视面板要显示"这张牌属于哪副牌组"。</summary>
        public string deckName = "";

        /// <summary>榨汁机 + 液体罐（得分面板在罐身的刻度上）</summary>
        public JuicerRig juicer;

        // ─────────────────────────────────────────────────────────────

        void Awake()
        {
            ClearDefaultSceneObjects();
            BuildCamera();
            BuildLights();
            BuildTable();
            BuildSlots();
            BuildRoots();
            BuildJuicer();
            BuildTurnLoop();      // 建出组件、接好引用（还不开始）
            BuildInteraction();   // HUD 和交互层都要能拿到 turnLoop
            BuildChoiceRig();     // 牌组 / 刀片的桌面表现
            BuildTitleRig();      // 开场界面

            // ★ 桌面视角的机位**放到这里才算**：它要按"桌上有什么"（两个槽位框、槽名牌、
            //   刀片位、级联列、破壁机）拟合，这些上面几步才建出来。
            //   BuildCamera 里那次注册只是兜底机位（算不出来时画面不至于没有相机）。
            ReframeBoardView(true);

            // ★ 最后才开始。Begin 会先停在开场界面，
            //   那时候桌面、HUD、交互、开场 rig 必须都已经就位 ——
            //   以前是在 BuildTurnLoop 里就 Begin 的，顺序一旦动过就会踩空。
            turnLoop.Begin();

            // 打包版复现用的后门（默认关，见 TableSettings.AutoStart）：
            // 环境变量 DSH_AUTOSTART=1 时替玩家把"点书 → 进第 1 关 → 确认牌组 → 确认刀片"
            // 按一遍，直接停在"第 1 回合、什么都没动"的桌面上 —— 和编辑器探针同一起点。
            if (TableSettings.AutoStart) turnLoop.AutoStartFirstLevel();
        }

        /// <summary>上一次算「桌面视角」时的宽高比（−1 = 还没算过）。</summary>
        private float boardAspect = -1f;

        /// <summary>
        /// 宽高比一变就重算「桌面视角」。
        ///
        /// 【为什么必须每帧看着它】"装得下"这件事**随比例变**：水平半角 = 垂直半角 × aspect。
        ///   编辑器里拖一下 Game 视图面板的大小、打包版被玩家拉窗口，比例都会变；
        ///   只在启动时算一次的话，比例一变手牌就又被下边缘切掉了（这正是用户报的那一幕）。
        ///   每帧只做一次比值比较，真的变了才重算。
        /// </summary>
        void Update()
        {
            if (cam == null) return;
            if (Mathf.Approximately(cam.aspect, boardAspect)) return;

            // 正看着桌面视角就立刻套用新机位（否则玩家会看到"手牌还在画面外"的那一帧）；
            // 在别的视角 / 自由转头里只更新机位，不动玩家当前的镜头。
            bool active = rig != null && rig.CurrentView == BoardView;
            ReframeBoardView(active);
        }

        // ── 开场界面 ──────────────────────────────────────────────────
        private void BuildTitleRig()
        {
            GameObject go = new GameObject("TableTitleRig");
            go.transform.SetParent(transform, false);

            titleRig = go.AddComponent<TableTitleRig>();
            titleRig.cam   = cam;
            titleRig.setup = this;
            titleRig.loop  = turnLoop;
            titleRig.root  = go.transform;

            turnLoop.titleRig = titleRig;
        }

        // ── 回合循环 ──────────────────────────────────────────────────
        private void BuildTurnLoop()
        {
            GameObject go = new GameObject("TableTurnLoop");
            go.transform.SetParent(transform, false);

            turnLoop = go.AddComponent<TableTurnLoop>();
            turnLoop.setup  = this;
            turnLoop.juicer = juicer;
            turnLoop.board  = board;

            BuildRulesV21(go);
        }

        /// <summary>
        /// v2.1 的规则侧总装。
        ///
        /// 【为什么挂在同一个 GameObject 上】
        ///   两者是"阶段机 / 规则状态"的一对，生命周期完全一致（一关一起开、一起关）。
        ///   分两个节点的话，清桌面时要在两处分别 Destroy，漏一个就留下一桌幽灵卡。
        ///
        /// 【为什么不管开关都建出来】
        ///   建一个空对象几乎没有成本，而"开关一开却发现组件是 null"会让所有分流点
        ///   全部静默走旧流程 —— 那是最难查的一类问题（看起来开关没生效）。
        ///   TableTurnLoop.V21 里的 `rulesV21 != null` 只是防御，不是正常路径。
        /// </summary>
        private void BuildRulesV21(GameObject host)
        {
            turnLoop.rulesV21 = host.AddComponent<TableRulesV21>();
            turnLoop.rulesV21.loop      = turnLoop;
            turnLoop.rulesV21.cardsRoot = cardsRoot;
            turnLoop.rulesV21.juicer    = juicer;
        }

        // ── 选择环节（牌组 / 刀片）────────────────────────────────────
        private void BuildChoiceRig()
        {
            GameObject go = new GameObject("TableChoiceRig");
            go.transform.SetParent(transform, false);

            choiceRig = go.AddComponent<TableChoiceRig>();
            choiceRig.cam   = cam;
            choiceRig.setup = this;
            choiceRig.loop  = turnLoop;
            choiceRig.root  = go.transform;   // 牌组卡片挂在自己下面，拆的时候一起走

            turnLoop.choiceRig = choiceRig;
        }

        // ── 榨汁机 + 液体罐 ───────────────────────────────────────────

        /// <summary>
        /// 破壁机在桌面上的落点（世界单位，桌面顶面 y = 0）。**挪机器只改这一个常量。**
        ///
        /// 【为什么是 (0.72, 0, 0.20)】
        ///   · x = 0.72：仍然在桌子右侧（原来 0.74，位置几乎没动），
        ///     同时让开中间的卡位（x ∈ [−0.45, 0.45]）与刀片（桌面中心）。
        ///     ★ 这个值当年是"再往右就出画"的边界：那时桌面视角是个写死的机位，
        ///     水平可视半宽只有 ±1.0 世界单位（相机 (0, 1.05, −1.02)、fov 42°、宽高比 ≈1.8）。
        ///     现在桌面视角是**按内容拟合**出来的（见 <see cref="ReframeBoardView"/>），
        ///     机器整个被算进"必须装下的点集"里，所以右边还有余量 ——
        ///     想更贴右，先看那边拟合出来的边距，而不是先改这个坐标。
        ///   · z = 0.20：比桌面中心（0.06）稍靠远端，机器站在"卡位后面一排"，
        ///     与 z = −0.20 的两个槽位、z = −0.58 的手牌完全错开，不压卡牌落点。
        ///   · 整机**不旋转**（见下面 localRotation）—— 机器与桌子的两条边都平行。
        /// </summary>
        private static readonly Vector3 JuicerAt = new Vector3(0.72f, 0f, 0.20f);

        /// <summary>
        /// 整机缩放。原始尺寸（高 0.70）在桌面上像个 70 厘米的机器、压过卡牌；
        /// 缩完约 0.53 高，和 0.335 深的卡牌比例才对得上。
        /// </summary>
        private const float JuicerScale = 0.75f;

        private void BuildJuicer()
        {
            GameObject go = new GameObject("JuicerRig");
            go.transform.SetParent(transform, false);

            go.transform.localPosition = JuicerAt;

            // ★ 世界轴对齐、不转：这就是"与桌子平行"的那一半。
            //   另一半在立绘自己身上（BlenderArt.yawDegrees = 0，平面与桌边平行、正面朝玩家）——
            //   两者是父子，一起搬一起转，不会分家。
            //   写出来（而不是靠默认值）是为了让"机器是正着摆的"这件事在代码里看得见。
            go.transform.localRotation = Quaternion.identity;

            go.transform.localScale = Vector3.one * JuicerScale;

            juicer = go.AddComponent<JuicerRig>();
            juicer.Build();
        }

        // ── 清掉空场景自带的相机和灯，避免两套渲染叠在一起 ──
        private void ClearDefaultSceneObjects()
        {
            Camera[] cams = Object.FindObjectsOfType<Camera>();
            for (int i = 0; i < cams.Length; i++) cams[i].enabled = false;

            Light[] lights = Object.FindObjectsOfType<Light>();
            for (int i = 0; i < lights.Length; i++) lights[i].enabled = false;
        }

        // ── 相机 + 机位 ───────────────────────────────────────────────
        private void BuildCamera()
        {
            GameObject go = new GameObject("TableCamera");
            go.transform.SetParent(transform, false);
            cam = go.AddComponent<Camera>();
            cam.fieldOfView = 42f;
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 30f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.055f, 0.06f, 0.075f);
            go.AddComponent<AudioListener>();

            rig = go.AddComponent<CameraRig>();
            rig.cam = cam;

            // 三个固定机位。
            // ★ 「桌面视角」这里给的只是**兜底机位**（按"4 张手牌那会儿"的旧坐标）：
            //   真正的机位由 ReframeBoardView 按"桌上有什么 + 当前宽高比"拟合出来，
            //   而那一刻（Awake 里 BuildCamera 这一段）槽位框、破壁机都还不存在。
            //   留这一份的意义是"拟合万一算不出来（比如点集空着），画面也还有相机可用"，
            //   不是"这就是桌面视角"。
            //
            // ★★ 开场那一屏的机位（TitleView）**就是这同一组坐标**（见 TitleEye）★★
            //   它必须是**独立的**一条：桌面视角会被 ReframeBoardView 按内容重算，
            //   而开场那一屏的构图要和以前一模一样 —— 两条名字分开，"改一条不会顺手改另一条"。
            rig.Register(TitleView, TitleEye, TitleLook);
            rig.Register(BoardView, TitleEye, TitleLook);
            rig.Register("hand",  new Vector3(0f, 0.52f, -1.00f), new Vector3(0f, 0f, -0.56f));
            rig.Register("top",   new Vector3(0f, 1.55f, 0.02f),  new Vector3(0f, 0f, 0.02f));

            // 榨汁机特写：从近侧斜上方看机器。
            // ★ 注视点**跟着 JuicerAt 走**（而不是写死一组坐标）：
            //   机器一挪、特写还盯着旧坐标的话，"榨汁机特写"就变成拍空气 ——
            //   摆位参数集中在 JuicerAt 一处，这条机位也得跟着它，否则"改一处即可"就不成立。
            //   偏移量是按旧机位 (0.74, 0.20, 0.16) 反推的，所以机器没挪时构图与以前一模一样。
            rig.Register("juicer", new Vector3(0.30f, 0.46f, -0.42f),
                                   JuicerAt + new Vector3(0.02f, 0.20f, -0.04f));

            // 自由转头的锚点：比"桌面视角"稍微退后一点，
            // 站定了能看全整张桌子，然后**原地转头**看细节（位置不再变）。
            rig.Register(CameraRig.FreeView,
                         new Vector3(0f, 1.16f, -1.28f), new Vector3(0f, 0f, 0.04f));

            // ★ 开局停在**开场那一屏的机位**上（不是桌面视角）：Awake 走完就是开场界面，
            //   而桌面视角的机位此刻还没拟合出来（ReframeBoardView 在 Awake 末尾才跑）。
            rig.SnapTo(TitleView);
        }

        // ── 灯光 ──────────────────────────────────────────────────────
        private void BuildLights()
        {
            GameObject key = new GameObject("KeyLight");
            key.transform.SetParent(transform, false);
            key.transform.rotation = Quaternion.Euler(52f, -34f, 0f);
            Light kl = key.AddComponent<Light>();
            kl.type = LightType.Directional;
            kl.intensity = 1.05f;
            kl.color = new Color(1f, 0.96f, 0.88f);       // 暖光，像灯下的桌面
            kl.shadows = LightShadows.Soft;

            GameObject fill = new GameObject("FillLight");
            fill.transform.SetParent(transform, false);
            fill.transform.rotation = Quaternion.Euler(18f, 150f, 0f);
            Light fl = fill.AddComponent<Light>();
            fl.type = LightType.Directional;
            fl.intensity = 0.30f;
            fl.color = new Color(0.72f, 0.78f, 0.95f);    // 冷补光，拉开层次
            fl.shadows = LightShadows.None;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.16f, 0.16f, 0.20f);
        }

        // ── 桌子 ──────────────────────────────────────────────────────
        private void BuildTable()
        {
            GameObject table = GameObject.CreatePrimitive(PrimitiveType.Cube);
            table.name = "Table";
            table.transform.SetParent(transform, false);
            table.transform.localScale = new Vector3(2.7f, 0.10f, 1.85f);
            table.transform.localPosition = new Vector3(0f, -0.05f, 0.06f);   // 顶面落在 y = 0

            // 量一次桌子的占地：取景要拿"远沿"当"画面别把桌子切一半"的那条线（见 CollectBoardFitPoints）。
            // 立方体网格是 1×1×1、又没转过，所以 position ± localScale/2 就是它的世界包围盒。
            tableBounds = new Bounds(table.transform.position, table.transform.localScale);

            Renderer r = table.GetComponent<Renderer>();
            if (r != null)
            {
                // 程序化木纹。立方体顶面的 UV 是 0..1 铺满整面，
                // 直接贴会被拉成长条，所以给一个接近"每格 0.9 米见方"的平铺次数。
                Texture2D wood = ProceduralArt.TableSurface();

                // ★ 有木纹 → 染色取近白（贴图自己带颜色，染色只负责微调色温）；
                //   木纹拿不到（程序化美术被换掉 / 出图失败）→ 退回纯色桌面，
                //   这时 _Color 就是全部反照率，所以用能看清的中间调 TableColor。
                r.material = MakeMaterial(wood != null ? TableTint : TableColor, 0.05f);

                if (wood != null)
                {
                    r.material.mainTexture = wood;
                    r.material.mainTextureScale = new Vector2(3f, 2.1f);
                }

                LogTableMaterialOnce(r.material, wood);
            }
        }

        /// <summary>
        /// ★★ 桌面材质的颜色必须是**接近白色**的染色，不能是又一个深色 ★★
        ///
        /// 【为什么】（用户报的"桌面变黑 / 木纹不见了"就是这一条）
        ///   Standard 材质的最终反照率 = _Color × _MainTex。木纹贴图本身已经是深木色
        ///   （0.15~0.25），再乘一个深色 _Color（原来是 0.20/0.155/0.125）之后
        ///   有效反照率只剩 **0.03~0.05** —— 而这张桌子受到的总光照只有 1.2 上下，
        ///   于是整张桌面渲染出来只有 4%~6% 的灰，**看起来就是纯黑，木纹更是完全不可见**。
        ///   实测（DSH_BLACKPROBE 的黑桌探针，桌面四个固定点）：
        ///     深色 _Color：近侧 (41,35,31)，四舍五入就是黑；
        ///     白 _Color  ：同样的点回到 60~70 这一档（和历史上好看的版本一致），木纹可辨。
        ///
        /// 【为什么不是把光照调亮】光照是"气氛"（暖光 + 冷补光 + 烛光），整套构图都按它调过；
        ///   而"暗色 × 暗色 = 更暗"是纯粹的口径错误 —— 贴图已经承担了颜色，
        ///   _Color 只该当染色用。改这一处，桌面立刻回到设计意图上的深木色。
        /// </summary>
        private static readonly Color TableTint = new Color(1f, 0.97f, 0.93f);

        /// <summary>
        /// 打一次"桌面贴图到底长什么样"的证据行（只打一次）。
        ///
        /// 【为什么留这一行】"桌面是黑的"这件事在编辑器里看不见、在打包版里才现形，
        ///   而它有两类完全不同的原因，截图长得一模一样：
        ///     ① 贴图没了 / 越界采样（mip 链缺失 + 画质档位把贴图降到半分辨率）
        ///     ② 反照率被乘暗（_Color × 贴图 双重变暗，见 TableTint）
        ///   这一行同时给出"画质档位 / 贴图 mip 层数 / 贴图名 / 采样模式"，
        ///   下一次再有人报黑桌，对着日志就能分清是哪一类（也能证明 ① 已经被修掉）。
        /// </summary>
        private static void LogTableMaterialOnce(Material m, Texture2D wood)
        {
            if (tableLogged) return;
            tableLogged = true;

            Debug.Log("[TableSetup] 桌面材质：shader=" + (m.shader != null ? m.shader.name : "NULL")
                      + "｜_Color=" + m.color.ToString()
                      + "｜木纹贴图=" + (wood != null ? wood.name + " " + wood.width + "×" + wood.height : "NULL")
                      + "｜mip 层数=" + (wood != null ? wood.mipmapCount.ToString() : "-")
                      + "（>1 = 有 mip 链；=1 且画质档位限制贴图分辨率时颜色会出错）"
                      + "｜画质档位=" + QualitySettings.GetQualityLevel()
                      + "「" + QualitySettings.names[QualitySettings.GetQualityLevel()] + "」"
                      + "｜贴图分辨率限制=" + QualitySettings.globalTextureMipmapLimit
                      + "（0 = 不限制）"
                      + "｜各向异性=" + QualitySettings.anisotropicFiltering
                      + "｜色彩空间=" + (QualitySettings.activeColorSpace == ColorSpace.Linear ? "Linear" : "Gamma"));
        }

        private static bool tableLogged;

        // ── 卡槽标记 ──────────────────────────────────────────────────
        private Renderer[] slotMarkers;

        private void BuildSlots()
        {
            GameObject go = new GameObject("TableBoard");
            go.transform.SetParent(transform, false);
            board = go.AddComponent<TableBoard>();

            // ★ 只有两个卡槽：**左法术、右素材**，摆在玩家面前（近侧）。
            //
            //   原来是 4 列 × 2 行共 8 个、摆在桌子中间，那是"每回合随便放几张"
            //   时期的产物。现在规则是"每回合最多 1 张素材 + 1 张法术"，
            //   8 个槽里 6 个永远是空的 —— 空槽会让玩家以为能多放。
            //
            //   z 从 0.17 挪到 -0.20：中间那段要空出来给刀片
            //   （刀片是"友方单位"，坐镇桌面中心）。
            board.BuildGrid(2, 1, 0.46f, 0f, new Vector3(0f, 0f, -0.20f));

            // ★★ 「上 桌 位」跟着**桌面素材的级联列**走（用户口径：那三个字要落在整列级联的**下方**）★★
            //
            //   【为什么它不能留在 −0.23】素材上桌之后不再摊在两个槽位上，而是堆成一列级联
            //   （见 TableRulesV21 顶部那段）。如果素材槽还钉在旧的 x = +0.23，
            //   框和名牌就会孤零零留在级联列的**左下角**、和那摞卡对不上号；
            //   用户看到的就是"上桌位跑到整列下面去了"。
            //
            //   【现在它摆在哪】框心 = (TableCascadeX, TableCascadeNearLimitZ) ——
            //   也就是**级联末端（新上桌那张）的落点**：
            //     · 拖一张素材到这一格 = 它成为级联最靠玩家的那张，正好落在框里（落点和框心重合）；
            //     · 槽名牌由 SlotLabelPosition 自动画在框的**近侧**（再往玩家 0.21），
            //       于是「上 桌 位」三个字永远在整列级联的**下方**，且不会被任何一张卡压住
            //       （TableRulesV21 的布局自检把它当障碍逐帧量，压到就打警告）。
            //   ★ 坐标不在这里另写一份：列锚点/末端落点都由 TableRulesV21 的公开常量给，
            //     改了级联那边，槽位自动跟着走。
            //   ★ 这个槽两种模式共用（旧流程里它是"素材槽"，先摆好再按确认）——
            //     旧流程的语义不受影响（框在哪儿，拖进去就落在哪儿），所以不做模式分叉：
            //     分叉会带来"切了模式而标记块没跟着挪"的另一类不一致（标记块在 Awake 建一次）。
            if (board.IsValidSlot(TableTurnLoop.SlotMaterial))
            {
                board.slots[TableTurnLoop.SlotMaterial] =
                    new Vector3(TableRulesV21.TableCascadeX, 0f, TableRulesV21.TableCascadeNearLimitZ);
            }

            // 把卡槽矩形尺寸告诉 board —— 吸附判定要用它当"框"。
            // 和下面指示块的 scale 用同一组常量，免得两边各写一个数、改一处忘一处。
            board.slotSizeX = SlotSizeX;
            board.slotSizeZ = SlotSizeZ;

            slotMarkers = new Renderer[board.SlotCount];
            for (int i = 0; i < board.SlotCount; i++)
            {
                // 用 Quad 而不是 Cube：
                //   Quad 默认躺在 XY 平面，Euler(90,0,0) 把它放平后法线朝上、
                //   局部 +Y 转到世界 +Z（远端 = 屏幕上方向），贴图方向正好对。
                //   尺寸按 Quad 的 1×1 单位算，所以 scale 直接就是世界尺寸。
                GameObject m = GameObject.CreatePrimitive(PrimitiveType.Quad);
                m.name = "Slot" + i;
                m.transform.SetParent(go.transform, false);
                m.transform.position = board.SlotPosition(i) + new Vector3(0f, 0.0022f, 0f);
                m.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                m.transform.localScale = new Vector3(SlotSizeX, SlotSizeZ, 1f);

                Collider c = m.GetComponent<Collider>();
                if (c != null) c.enabled = false;      // 别挡住卡牌的射线

                // 贴图只在四个角画了 L 形短角标，其余全透明 ——
                // 比原来那种不透明方块低调得多。
                // 初始 alpha = 0：平时完全隐形，拿起牌要放的时候才由
                // TableInteraction 淡入（8 个槽 × 4 个角标常亮的话桌面全是碎线）。
                Renderer mr = m.GetComponent<Renderer>();
                if (mr != null)
                {
                    mr.material = CardFactory.MakeUnlit(ProceduralArt.SlotFrame());

                    Color idle = TableInteraction.MarkerIdle;
                    idle.a = 0f;
                    mr.material.color = idle;
                    mr.enabled = false;     // 初始隐形，拿起牌时由 TableInteraction 打开
                }
                slotMarkers[i] = mr;
            }

            BuildSlotLabels(go.transform);
        }

        /// <summary>
        /// 把槽位名字刻在桌面上（每个槽靠玩家这一侧）。
        /// 两个槽收的东西不一样，不写清楚玩家会往法术槽里拖素材。
        ///
        /// 【v2.1 与旧流程的字样不一样】见 <see cref="RefreshSlotLabels"/> ——
        /// 这两个槽在 v2.1 里是"上桌位 / 附魔位"（落槽即生效），
        /// 在旧流程里才是"法术槽 / 素材槽"（先摆好、再按确认）。所以这里只记住父节点，
        /// 真正的字样由 RefreshSlotLabels 按当前模式写。
        /// </summary>
        private void BuildSlotLabels(Transform parent)
        {
            slotLabelsParent = parent;
            RefreshSlotLabels();
        }

        /// <summary>槽位名字的 3D 文字对象（要按规则模式重建，所以留一份引用）。</summary>
        private readonly List<GameObject> slotLabels = new List<GameObject>();
        private Transform slotLabelsParent;

        /// <summary>
        /// 两个槽在桌面上刻的字 —— **顺序就是槽位下标顺序**（[0] 在左、[1] 在右）。
        ///
        /// ★ 这是"槽位语义"的唯一出处之一，另一处是 <see cref="TableTurnLoop.SlotSpell"/> /
        ///   <see cref="TableTurnLoop.SlotMaterial"/>。两边必须对得上：
        ///   下标 0 = 附魔位 = 收法术，下标 1 = 上桌位 = 收素材。
        ///   改任何一边都要同时改另一边 —— TableTurnLoop.SlotSemanticReport() 会在开一关时
        ///   把这条一致性查一遍（名字里写着"附魔"、行为却是"收素材"这种事，
        ///   玩家只会看到"这个槽放不了牌"，而日志里什么都看不出来）。
        /// </summary>
        public static readonly string[] V21SlotNames    = { "附　魔 位", "上　桌 位" };

        /// <summary>旧流程的字样（先摆好、再按确认那套）。顺序同上。</summary>
        public static readonly string[] LegacySlotNames = { "法　术 槽", "素　材 槽" };

        /// <summary>
        /// 槽名牌刻在**框外面多远**（世界单位，朝玩家这一侧）。
        ///
        /// ★ 这个数不是随便定的：框的近边在 z = −0.375，牌子中心 = −0.375 − 这个值。
        ///   它是"牌子要看得清"和"牌子必须落在吸附判定区里"之间挤出来的 ——
        ///   判定区的近端上限 ≈ −0.595（见 TableInteraction.snapSlackNearZ 那段推导），
        ///   取 0.035 之后牌子中心在 −0.41，离近端上限还有 0.185，
        ///   连"照着牌子的下半截放"也还落在判定区里。
        ///   原来取 0.055（牌子中心 −0.43），叠上拖动抬卡带来的透视差就顶在边上 ——
        ///   用户第二次报的"照着牌子放却放不上去"就是这两条叠出来的。
        ///
        /// ★ 改这个数必须同时看 TableInteraction.snapSlackNearZ，
        ///   并且 TableTurnLoop.SlotSemanticReport() 会把"牌子在不在判定区里、余量多少厘米"
        ///   每次开一关量一遍（那是这一条的常驻防线）。
        /// </summary>
        public const float SlotLabelGap = 0.035f;

        /// <summary>
        /// 第 i 个槽的**牌子中心**世界坐标 —— 渲染和自检共用这一个算式，
        /// 免得"牌子挪了、判定区没跟着挪"或者反过来。
        /// </summary>
        public Vector3 SlotLabelPosition(int i)
        {
            if (board == null || !board.IsValidSlot(i)) return Vector3.zero;
            return board.SlotPosition(i) + new Vector3(0f, 0f, -(SlotSizeZ * 0.5f + SlotLabelGap));
        }

        /// <summary>
        /// 按**当前规则模式**重写两个槽的名字。
        ///
        /// 【v2.1 为什么必须换字样】这两个槽在 v2.1 里已经不是"待投放区"了 ——
        ///   它们是两个**入口**：素材拖进去 = 上桌、法术拖进去 = 附魔，落槽即生效。
        ///   还写着"法术槽 / 素材槽"的话，玩家会照旧流程的意思去用
        ///   （"先放进去，再按确认"），而那个中间态在 v2.1 里已经不存在了 ——
        ///   名字和行为不一致，是这类"操作了但状态没跟上"的温床。
        ///   旧流程（UseRulesV21 = false）保持原来的字样与行为，一个字都不动。
        ///
        /// 【为什么重建而不是改字】名字是烤出来的 TextMesh 贴图，
        ///   重建最省事、也不会和旧对象的材质串味。
        ///   调用点在开始一关时（TableTurnLoop.StartLevelWith）——
        ///   设置面板里改模式是"重进关卡才生效"，这个名字跟规则走同一条时间线。
        /// </summary>
        public void RefreshSlotLabels()
        {
            for (int i = slotLabels.Count - 1; i >= 0; i--)
                if (slotLabels[i] != null) CardFactory.DestroySafe(slotLabels[i]);
            slotLabels.Clear();

            if (slotLabelsParent == null || board == null) return;

            // 顺序跟着槽的下标走：下标 0 在左、1 在右（见 V21SlotNames 的说明）
            string[] names = TableSettings.UseRulesV21 ? V21SlotNames : LegacySlotNames;

            for (int i = 0; i < board.SlotCount && i < names.Length; i++)
            {
                // 位置走 SlotLabelPosition（渲染和自检共用同一个算式，见那里的说明）
                Vector3 at = SlotLabelPosition(i);

                GameObject go = new GameObject("SlotLabel" + i);
                go.transform.SetParent(slotLabelsParent, false);
                go.transform.position = at;

                // localZ = 0。这几张桌面文字只能落在负半区或原点 ——
                // 正的 localZ 完全不渲染，原因见 TableChoiceRig 里那段说明。
                CardFactory.AddText(go.transform, names[i], 0f, 0.0072f,
                                    new Color(0.60f, 0.56f, 0.50f), 0.0022f);
                slotLabels.Add(go);
            }
        }

        // ── 容器 ──────────────────────────────────────────────────────
        private void BuildRoots()
        {
            GameObject go = new GameObject("Cards");
            go.transform.SetParent(transform, false);
            cardsRoot = go.transform;
        }

        // ── 发牌 ──────────────────────────────────────────────────────

        /// <summary>
        /// 按当前 TurnState 重新摆一遍手牌。
        ///
        /// 确认牌组之前手牌是空的，所以发牌不能放在 Awake 里做 ——
        /// 那会走兜底分支、凭空摆出一张"铁块"。
        /// 换刀片之后也会用到它（刀片和手牌对调，两边内容都变了）。
        /// </summary>
        /// <summary>
        /// 清空手牌（连 3D 卡一起销毁）。
        /// 和 RebuildHand 分开是有原因的：RebuildHand 会调 DealHand，
        /// 而 DealHand 在"手牌是空的"时会走兜底分支凭空摆出一张铁块 ——
        /// 回开场那种"就是要清空"的场合不能走那条路。
        /// </summary>
        public void ClearHand()
        {
            for (int i = hand.Count - 1; i >= 0; i--)
                if (hand[i] != null) CardFactory.DestroySafe(hand[i].gameObject);

            hand.Clear();
        }

        public void RebuildHand()
        {
            ClearHand();
            DealHand();
        }

        /// <summary>
        /// 按数据层的手牌摆出 3D 卡。
        ///
        /// 【为什么不再自己去读牌组】
        /// 原来这里是 deck.InitialHandIngredients()，自己算一遍初始手牌 ——
        /// 于是桌面上摆的牌和 TurnState 里记的牌是两份互相独立的数据，
        /// 谁改了规则另一份都不知道。而且那份实现漏掉了变速模块，
        /// 模块永远进不了玩家手里。
        /// 现在唯一的数据来源是 turnLoop.turn，这里只负责把它画出来。
        ///
        /// 【v2.1 为什么整段绕开】
        ///   v2.1 的手牌（4 素材 + 1 法术）在 TableRulesV21 里，不在 TurnState 里 ——
        ///   而且是"素材 + 法术"两种形状，这里每张都按 Ingredient 造卡会造错。
        ///   所以这里**直接返回**，v2.1 的手牌由 TableRulesV21.RebuildHand() 全权负责。
        ///
        ///   ★ 这里不能写成"调 rulesV21.RebuildHand() 再返回"：
        ///     那个方法内部会调 setup.ClearHand() + 逐张造卡，而 setup.RebuildHand()
        ///     又会调回 DealHand —— 两边互相调用就是无限递归。
        ///     （TableRulesV21.RebuildHand 不调 setup.RebuildHand，所以单向是安全的。）
        /// </summary>
        private void DealHand()
        {
            hand.Clear();
            if (turnLoop == null || cardsRoot == null) return;

            if (turnLoop.V21) return;

            TurnState st = turnLoop.turn;
            System.Collections.Generic.List<Card> cards = st.hand;

            Vector3 pos, euler;

            // 兜底：配置表空掉了也至少摆一张，免得桌面光着没法验证
            if (cards == null || cards.Count == 0)
            {
                Ingredient fb = GameConfig.DefaultBlade();
                if (fb == null) return;

                TableTurnLoop.HandSlot(0, 1, out pos, out euler);
                hand.Add(CardFactory.Create(Card.Of(fb), cardsRoot, pos, euler));
                return;
            }

            int total = cards.Count;
            int k = 0;

            // ★ 一条循环就够了 —— 食材和模块现在都是"一张牌"。
            //   以前要分别遍历 hand 和 modules 两个列表，写两遍。
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i] == null) continue;
                TableTurnLoop.HandSlot(k, total, out pos, out euler);
                hand.Add(CardFactory.Create(cards[i], cardsRoot, pos, euler));
                k++;
            }

            ReframeHandView();
        }

        /// <summary>「手牌特写」的俯角。太低会把牌压成一条，太高就看不出牌的立体感。</summary>
        private const float HandViewTiltDeg = 52f;

        /// <summary>
        /// 按**当前手牌的真实范围**重算「手牌特写」机位。
        ///
        /// 以前这个机位是写死的坐标，是按"4 张牌"调的；牌一多（换刀片后 5 张、
        /// 或者扇形张开）最外侧两张就被切在画面外。
        /// 现在每次手牌重新布局都按包围盒算一次，几张牌都装得下 ——
        /// 装不下的时候也是整体拉远，而不是把牌切掉。
        ///
        /// 只更新机位、不切镜头：发牌过程中抢镜头会很跳，切视角仍然是玩家按键那一下。
        /// </summary>
        public void ReframeHandView()
        {
            // ★ 桌面视角那台机位也得跟着重算：它要装下的东西里就有手牌那一排，
            //   张数一变（发牌 / 出牌 / 收回手牌 / 产出的卡进手牌）横向宽度就变了。
            //   放在这个方法的**最前面**、也放在"手牌为空就返回"之前 ——
            //   空手牌时取景按"至少 5 张"算（见 CollectBoardFitPoints），不该被这一句跳过。
            //   applyNow = true：玩家正看着桌面视角时立刻套用。5 张以内算出来的机位是同一个，
            //   所以出牌那一刻镜头并不会动 —— 只有真的超过 5 张（画面要装不下了）才动。
            ReframeBoardView(true);

            if (rig == null || cam == null || hand == null || hand.Count == 0) return;

            bool any = false;
            Bounds box = new Bounds();

            for (int i = 0; i < hand.Count; i++)
            {
                PlayCard c = hand[i];
                if (c == null) continue;

                // 手牌是扇形张开的，按卡的偏航算它真正的占地，
                // 直接用 0.24 x 0.335 的轴对齐盒会漏掉外侧那点宽度
                float yaw = c.transform.eulerAngles.y * Mathf.Deg2Rad;
                float cos = Mathf.Abs(Mathf.Cos(yaw));
                float sin = Mathf.Abs(Mathf.Sin(yaw));
                float ex = CardFactory.CardWidth * cos + CardFactory.CardDepth * sin;
                float ez = CardFactory.CardDepth * cos + CardFactory.CardWidth * sin;

                Bounds cb = new Bounds(c.transform.position, new Vector3(ex, 0.02f, ez));
                if (!any) { box = cb; any = true; }
                else box.Encapsulate(cb);
            }

            if (!any) return;

            box.Expand(new Vector3(0.07f, 0f, 0.07f));      // 边上留一圈空，别贴着画面边
            rig.FrameTableBounds("hand", box, HandViewTiltDeg, 1.06f);
        }

        // ══════════════════════════════════════════════════════════════
        //  「桌面视角」的取景
        //
        //  用户报的那一幕：v2.1 的第 1 回合里，手牌最下面那张卡**只露出上半截** ——
        //  它挂在画面最底部、「附 魔 位 / 上 桌 位」两行字的下方，被视口下边缘切掉。
        //
        //  【根因】桌面视角原来是个写死的机位 (0, 1.05, −1.02) → (0, 0, 0.10)。
        //    它的下边缘在桌面高度上只到 z ≈ −0.51，而手牌那一排在 z = −0.58 ± 卡深一半
        //    （−0.7475 ~ −0.4125）—— 也就是说**手牌那一排天生就在画面外**，
        //    只有最远的那 3 厘米探进画面里。在 fov 42° 的固定垂直视角下，
        //    这一点和宽高比无关：16:9 切、竖屏也切。
        //
        //  【为什么不去挪手牌那一排】它后面 3.7 厘米就是「附 魔 位 / 上 桌 位」两块槽名牌
        //    （z = −0.41），再往前是槽位框（近沿 z = −0.375）。往回挪会顶到槽位框，
        //    往玩家这边挪只会掉得更低 —— 那两样都是明令不许动的几何。
        //    所以能动的只有**取景**：让相机退到"手牌那一排也在画面里"的距离上。
        //
        //  【为什么不是一个更远的写死坐标】可视范围随**宽高比**变（水平半角 =
        //    垂直半角 × aspect）。写死的坐标只能对一个比例成立 —— 编辑器 Free Aspect
        //    和打包版 1600×900 就会有一个是错的。这里每次按当前 cam.aspect 拟合。
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 「桌面视角」的俯角 —— 和原来那个写死机位的角度一样（atan2(1.05, 1.12) ≈ 43.2°）。
        /// 取景改的是"退多远、画面中心对着哪"，**看桌子的角度一个字没改**。
        /// </summary>
        private const float BoardViewTiltDeg = 43.2f;

        /// <summary>
        /// 「桌面视角」边上至少留多少：1.10 = 每个方向至少留 1/1.1 半屏（约 4.5%）。
        /// 用户点名的是"手牌不能贴边被切"，留这一圈就是"别贴边"的余量 ——
        /// 顺带也把 HUD 那几行字压过来的高度让开了。
        /// </summary>
        private const float BoardViewMargin = 1.10f;

        /// <summary>
        /// 手牌那一排**至少按几张算**取景：正文 §2.6 的初始手牌是 4 素材 + 1 法术。
        ///
        /// 【为什么要有这个下限】取景要是跟着当前张数走，出掉一张牌镜头就往回缩一点 ——
        /// 玩家每出一张牌画面都动一下，比"多留一张的位置"难看得多。
        /// 所以张数少时按 5 张算（机位稳定），真的超过 5 张（产出的卡进手牌）才跟着放大。
        /// </summary>
        private const int BoardFitHandCards = 5;

        /// <summary>取景拟合用的点集（复用同一个 List：每次发牌 / 出牌都会重算一遍）。</summary>
        private readonly List<Vector3> boardFitPoints = new List<Vector3>();

        /// <summary>其中"手牌那一排"的那几个点 —— 自检要单独量它（用户点名的是它）。</summary>
        private readonly List<Vector3> handFitPoints = new List<Vector3>();

        /// <summary>这次取景按几张手牌算的（日志里要写清楚"量的是几张"）。</summary>
        private int boardFitHandCount;

        /// <summary>
        /// 重算「桌面视角」的机位。
        ///
        /// 【什么东西必须装得下】见 <see cref="CollectBoardFitPoints"/> 的清单 ——
        ///   原则是"玩家要看、要点的东西一个都不切"：手牌那一排、两个槽位框与槽名牌、
        ///   刀片卡、素材级联那一列、破壁机。
        ///
        /// 【为什么 applyNow 单独一个参数】比例变了 / 手牌变了的时候，如果玩家正看着
        ///   桌面视角，就该**立刻**套用（不然会出现"手牌还在画面外"的那一帧）；
        ///   而玩家在别的视角或自由转头里时，只更新机位、不动他当前的镜头。
        ///   发牌途中抢镜头很跳，所以调用点只在"比例变了"和"手牌重排"这两处传 true。
        /// </summary>
        public void ReframeBoardView(bool applyNow)
        {
            if (rig == null || cam == null) return;

            CollectBoardFitPoints(boardFitPoints, handFitPoints);
            if (boardFitPoints.Count == 0) return;

            if (!rig.FramePoints(BoardView, boardFitPoints, BoardViewTiltDeg, BoardViewMargin, false))
            {
                // 拟合算不出来（点集异常）时保留旧机位，但必须留痕 ——
                // 静默保留会表现成"取景改了但画面没变"，最难查的那种。
                Debug.LogWarning("[V21][取景] 桌面视角拟合失败（点 " + boardFitPoints.Count
                                 + " 个），保留原机位。");
                return;
            }

            boardAspect = cam.aspect;

            if (applyNow && rig.CurrentView == BoardView && !rig.IsFreeLook) rig.SnapTo(BoardView);

            VerifyBoardFraming();
        }

        /// <summary>
        /// 「桌面视角」必须装下的点集。
        ///
        /// 【为什么是点集，而不是一个包围盒】见 <see cref="CameraRig.FramePoints"/>：
        ///   包围盒把不同深度的东西当成同一层，而手牌那一排正是离相机最近的一层 ——
        ///   按盒估出来的距离偏小，牌照样会被切。
        ///
        /// 【为什么每一项都从游戏自己那份几何里读】槽位框走 board.SlotPosition + SlotSize*、
        ///   槽名牌走 SlotLabelPosition、级联列走 TableRulesV21 的公开常量、手牌走
        ///   TableTurnLoop.HandSlot —— 这里一个坐标都不另抄。抄一份的后果是
        ///   "那边挪了、取景还按旧位置算"，也就是这一轮要修的那类问题。
        ///   ★ 桌面素材的级联排布、槽位框几何、刀片位**一个都没动**，这里只是把它们读出来。
        /// </summary>
        private void CollectBoardFitPoints(List<Vector3> all, List<Vector3> handPts)
        {
            all.Clear();
            handPts.Clear();

            // ① 手牌那一排：按"至少 5 张、实际更多就按实际"算每张卡的四个角。
            //    卡的角按偏航算（扇形是张开的），最外侧那一点就在角上。
            int live = 0;
            if (hand != null)
                for (int i = 0; i < hand.Count; i++)
                    if (hand[i] != null && hand[i].slotIndex < 0) live++;

            int n = Mathf.Max(BoardFitHandCards, live);
            for (int i = 0; i < n; i++)
            {
                Vector3 pos, euler;
                TableTurnLoop.HandSlot(i, n, out pos, out euler);
                AddCorners(handPts, pos, CardFactory.CardWidth, CardFactory.CardDepth, euler.y);
            }
            all.AddRange(handPts);

            // ② 两个槽位框 + 两块槽名牌（玩家要照着它们拖牌；字必须看得见）
            if (board != null)
            {
                for (int i = 0; i < board.SlotCount; i++)
                {
                    AddCorners(all, board.SlotPosition(i), SlotSizeX, SlotSizeZ, 0f);

                    // 名牌的尺寸走 TableRulesV21 那份（布局自检把它当障碍量，是同一个矩形）
                    AddCorners(all, SlotLabelPosition(i),
                               TableRulesV21.SlotLabelHalfW * 2f, TableRulesV21.SlotLabelHalfD * 2f, 0f);
                }
            }

            // ③ 刀片卡（桌心，正文不许动）+ 桌面素材的级联列（一列，放不下会镜像到左边）
            AddCorners(all, TableChoiceRig.BladeSpot, CardFactory.CardWidth, CardFactory.CardDepth, 0f);

            float cascadeZ0 = TableRulesV21.TableCascadeNearLimitZ - CardFactory.CardDepth * 0.5f;
            float cascadeZ1 = TableRulesV21.TableCascadeFirstZ     + CardFactory.CardDepth * 0.5f;
            AddCorners(all, new Vector3( TableRulesV21.TableCascadeX, 0f, (cascadeZ0 + cascadeZ1) * 0.5f),
                       CardFactory.CardWidth, cascadeZ1 - cascadeZ0, 0f);
            AddCorners(all, new Vector3(-TableRulesV21.TableCascadeX, 0f, (cascadeZ0 + cascadeZ1) * 0.5f),
                       CardFactory.CardWidth, cascadeZ1 - cascadeZ0, 0f);

            // ④ 破壁机：玩家要点它启动，整机得在画面里
            AddBoxCorners(all, JuicerFootprint());

            // ⑤ 桌子远沿 —— 只要一个点：它的作用是"画面别只装下玩法内容、把桌子切一半"，
            //    而桌面是块平板，两个桌角出画不丢任何玩法信息（真要装下 ±1.35 的桌角，
            //    在竖屏比例下得多退 10%，换来的只是两块空木板）。
            all.Add(new Vector3(0f, 0f, tableBounds.max.z));

            boardFitHandCount = n;
        }

        /// <summary>
        /// 破壁机的世界占地（取景拟合的输入之一）。
        ///
        /// 【为什么尺寸由立绘自己报】立绘的宽高是按像素反算的（见 BlenderArt.Setup：
        ///   机身 0.70 米对应立绘上 93~671 像素，整机再套 0.75 的缩放）。
        ///   在这里抄一份 0.52 × 0.61，美术换一张立绘取景就过期了 ——
        ///   而"机器被切掉一半"正是这一轮要修的那类问题。
        ///   拿不到立绘（美术没挂上 / 退回程序化机身）时按设计尺寸估：机身 0.70 × 0.75 ≈ 0.53 高、0.50 宽。
        /// </summary>
        private Bounds JuicerFootprint()
        {
            float w = 0.50f, h = 0.53f;

            BlenderArt art = juicer != null ? juicer.GetComponentInChildren<BlenderArt>() : null;
            if (art != null)
            {
                Vector2 ws = art.WorldSize;
                if (ws.x > 0.05f && ws.y > 0.05f) { w = ws.x; h = ws.y; }
            }

            // 立绘沿 +Z 推了 zOffset（见 BlenderArt.Place），这里按 0.25 的厚度把它罩住 ——
            // 取景关心的是"宽高装不装得下"，前后这点厚度对边距没有可见影响。
            Vector3 center = JuicerAt + new Vector3(0f, h * 0.5f, 0.10f);
            return new Bounds(center, new Vector3(w, h, 0.25f));
        }

        /// <summary>
        /// 把一个**平躺的矩形**（卡 / 槽位框 / 槽名牌）的四个角加进点集。
        /// yawDeg 是它绕 Y 的偏航：要按旋转后的半宽半深算角点 ——
        /// 扇形张开时最外侧那一点正好在角上，只加卡心会把它漏掉
        /// （和 ReframeHandView 量手牌占地是同一个口径）。
        /// </summary>
        private static void AddCorners(List<Vector3> pts, Vector3 center, float width, float depth, float yawDeg)
        {
            float yaw = yawDeg * Mathf.Deg2Rad;
            float c = Mathf.Cos(yaw), s = Mathf.Sin(yaw);

            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    float lx = sx * width * 0.5f;
                    float lz = sz * depth * 0.5f;
                    pts.Add(center + new Vector3(lx * c + lz * s, 0f, -lx * s + lz * c));
                }
            }
        }

        /// <summary>把一个世界包围盒的八个角加进点集。</summary>
        private static void AddBoxCorners(List<Vector3> pts, Bounds b)
        {
            for (int i = 0; i < 8; i++)
            {
                pts.Add(new Vector3((i & 1) == 0 ? b.min.x : b.max.x,
                                    (i & 2) == 0 ? b.min.y : b.max.y,
                                    (i & 4) == 0 ? b.min.z : b.max.z));
            }
        }

        /// <summary>
        /// 桌子的世界包围盒 —— 取景只要它的远沿那一个点（近沿在玩家身后，本来就不该进画面）。
        /// 在 BuildTable 里量一次，免得"桌子挪了 / 放大了"这里还按旧尺寸算。
        /// </summary>
        private Bounds tableBounds;

        /// <summary>
        /// 「取景」自检：把点集按**桌面视角那个机位**投一遍，量出离画面边最近的余量。
        ///
        /// 【为什么必须打日志、还要报警】取景是算出来的，算错了不会有任何报错 ——
        ///   只会切掉一张牌，而"切掉一点点"在缩略图上看不出来。
        ///   所以每次重算机位都把**手牌那一排**的上下左右余量量成数字写进日志；
        ///   真的出画就打警告（和素材级联的 [V21][布局] 是同一个套路）。
        /// </summary>
        private void VerifyBoardFraming()
        {
            Vector2 lo, hi;
            if (!rig.ViewNdcRange(BoardView, boardFitPoints, out lo, out hi))
            {
                Debug.LogWarning("[V21][取景] 桌面视角的点集投不出来（有点落在相机背后），"
                                 + "这次没法自检取景。");
                return;
            }

            // NDC → 像素：画面高的一半就是 1（宽那一半按宽高比换算，这里只报手牌的下边缘余量，
            // 它永远落在垂直方向上 —— 手牌是画面里最靠下的一排）。
            float aspect = Mathf.Max(0.2f, cam.aspect);
            float halfHPx = Screen.height * 0.5f;

            Vector2 hlo, hhi;
            bool handOk = rig.ViewNdcRange(BoardView, handFitPoints, out hlo, out hhi);

            string handText = handOk
                ? ("手牌那一排 " + boardFitHandCount + " 张：上下余量 "
                   + ((1f - hhi.y) * halfHPx).ToString("0") + " / " + ((1f + hlo.y) * halfHPx).ToString("0")
                   + " px，左右余量 "
                   + ((1f - hhi.x) * halfHPx * aspect).ToString("0") + " / "
                   + ((1f + hlo.x) * halfHPx * aspect).ToString("0") + " px")
                : "手牌那一排：投影异常";

            string allText = "全部 " + boardFitPoints.Count + " 个点：上下余量 "
                + ((1f - hi.y) * halfHPx).ToString("0") + " / " + ((1f + lo.y) * halfHPx).ToString("0")
                + " px，左右余量 "
                + ((1f - hi.x) * halfHPx * aspect).ToString("0") + " / "
                + ((1f + lo.x) * halfHPx * aspect).ToString("0") + " px";

            Debug.Log("[V21][取景] 桌面视角 " + Screen.width + "×" + Screen.height
                      + "（宽高比 " + aspect.ToString("0.00") + "）：" + handText + "；" + allText);

            // 越界才报警（正常的一局里一次都不该出现）
            bool handOut = !handOk || hlo.x < -1f || hlo.y < -1f || hhi.x > 1f || hhi.y > 1f;
            bool anyOut  = lo.x < -1f || lo.y < -1f || hi.x > 1f || hi.y > 1f;

            if (handOut)
                Debug.LogWarning("[V21][取景] ★ 手牌那一排出画了（NDC x ∈ [" + hlo.x.ToString("0.00")
                                 + ", " + hhi.x.ToString("0.00") + "]，y ∈ [" + hlo.y.ToString("0.00")
                                 + ", " + hhi.y.ToString("0.00") + "]）—— 取景拟合没生效？");
            else if (anyOut)
                Debug.LogWarning("[V21][取景] 有该看见的东西出画了（NDC x ∈ [" + lo.x.ToString("0.00")
                                 + ", " + hi.x.ToString("0.00") + "]，y ∈ [" + lo.y.ToString("0.00")
                                 + ", " + hi.y.ToString("0.00") + "]）");
        }

        // ── 交互层 ────────────────────────────────────────────────────
        private void BuildInteraction()
        {
            GameObject go = new GameObject("TableInteraction");
            go.transform.SetParent(transform, false);

            interaction = go.AddComponent<TableInteraction>();
            interaction.cam = cam;
            interaction.rig = rig;
            interaction.board = board;
            interaction.cardsRoot = cardsRoot;
            interaction.slotMarkers = slotMarkers;
            interaction.turnLoop = turnLoop;   // 放进投放区的牌要报给回合循环

            go.AddComponent<TableHud>();     // HUD 需要用到 interaction，用 GetComponent 拿
            go.AddComponent<CardBrowser>();  // 卡牌总览（F1）：查 v2.1 的素材/法术，不参与玩法
        }

        // ── 工具 ──────────────────────────────────────────────────────
        private static Material MakeMaterial(Color color, float glossiness)
        {
            // ★ 统一走 CardFactory 的出口（见那里关于"打包版里 Standard 会被剥掉"的说明）
            Material m = new Material(CardFactory.StdShader());
            m.color = color;
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", glossiness);
            if (m.HasProperty("_Metallic"))   m.SetFloat("_Metallic", 0f);
            return m;
        }
    }
}
