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
            "board", "hand", "top", "juicer", CameraRig.FreeView
        };

        public static readonly string[] ViewLabels =
        {
            "桌面视角", "手牌特写", "俯视", "榨汁机特写", "自由视角"
        };

        /// <summary>
        /// 桌面材质里那个"还没贴木纹之前"的染色。
        ///
        /// ★ 只在木纹贴图缺失（ProceduralArt 被换掉 / 取不到）时才是主角 —— 见 TableTint 的
        ///   说明为什么它必须浅：深色染色会把木纹贴图的亮度再乘一次，桌面就成了黑的。
        /// </summary>
        private static readonly Color TableColor  = new Color(0.62f, 0.50f, 0.42f);

        /// <summary>卡槽指示块（也就是吸附判定用的"框"）的尺寸。两处共用，别各写一个数。</summary>
        private const float SlotSizeX = 0.255f;
        private const float SlotSizeZ = 0.350f;

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

            // ★ 最后才开始。Begin 会先停在开场界面，
            //   那时候桌面、HUD、交互、开场 rig 必须都已经就位 ——
            //   以前是在 BuildTurnLoop 里就 Begin 的，顺序一旦动过就会踩空。
            turnLoop.Begin();
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
        ///     ★ 这是"再往右就出画"的边界值附近：桌面视角的水平可视半宽约 ±1.0 世界单位
        ///     （相机 (0, 1.05, −1.02)、fov 42°、宽高比 ≈1.8），立绘宽 0.52、剪影右缘再往外 0.22，
        ///     所以 x 过了 0.8 就会把进料斗那一块切在屏幕右沿外。
        ///     想更贴右边，得先动 <see cref="BuildCamera"/> 里 "board" 那个机位。
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

            // 三个固定机位
            rig.Register("board", new Vector3(0f, 1.05f, -1.02f), new Vector3(0f, 0f, 0.10f));
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

            rig.SnapTo("board");
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

            // 顺序跟着槽的下标走：下标 0 在左、1 在右
            string[] namesV21    = { "附　魔 位", "上　桌 位" };
            string[] namesLegacy = { "法　术 槽", "素　材 槽" };
            string[] names = TableSettings.UseRulesV21 ? namesV21 : namesLegacy;

            for (int i = 0; i < board.SlotCount && i < names.Length; i++)
            {
                Vector3 at = board.SlotPosition(i)
                           + new Vector3(0f, 0f, -(SlotSizeZ * 0.5f + 0.055f));

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
