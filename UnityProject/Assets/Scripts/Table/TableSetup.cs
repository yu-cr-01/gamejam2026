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

        private static readonly Color TableColor  = new Color(0.20f, 0.155f, 0.125f);

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
        private void BuildJuicer()
        {
            GameObject go = new GameObject("JuicerRig");
            go.transform.SetParent(transform, false);

            // 放桌子右侧偏内。卡槽占 x ∈ [−0.45, 0.45]、手牌在 z=−0.58，
            // 这里两边都不挡。
            go.transform.localPosition = new Vector3(0.74f, 0f, 0.16f);

            // 整体缩到 0.75。
            // 原始尺寸（高 0.70）在桌面上像个 70 厘米的机器，压过卡牌；
            // 缩完约 0.53 高，和 0.335 深的卡牌比例才对得上。
            go.transform.localScale = Vector3.one * 0.75f;

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

            // 榨汁机特写：从近侧斜上方看罐子和冲压腔
            rig.Register("juicer", new Vector3(0.30f, 0.46f, -0.42f),
                                   new Vector3(0.74f, 0.20f, 0.16f));

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
                r.material = MakeMaterial(TableColor, 0.05f);

                // 程序化木纹。立方体顶面的 UV 是 0..1 铺满整面，
                // 直接贴会被拉成长条，所以给一个接近"每格 0.9 米见方"的平铺次数。
                r.material.mainTexture = ProceduralArt.TableSurface();
                r.material.mainTextureScale = new Vector2(3f, 2.1f);
            }
        }

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
        /// </summary>
        private void BuildSlotLabels(Transform parent)
        {
            // 顺序跟着槽的下标走：下标 0 在左、1 在右
            string[] names = { "法　术 槽", "素　材 槽" };

            for (int i = 0; i < board.SlotCount && i < names.Length; i++)
            {
                Vector3 at = board.SlotPosition(i)
                           + new Vector3(0f, 0f, -(SlotSizeZ * 0.5f + 0.055f));

                GameObject go = new GameObject("SlotLabel" + i);
                go.transform.SetParent(parent, false);
                go.transform.position = at;

                // localZ = 0。这几张桌面文字只能落在负半区或原点 ——
                // 正的 localZ 完全不渲染，原因见 TableChoiceRig 里那段说明。
                CardFactory.AddText(go.transform, names[i], 0f, 0.0072f,
                                    new Color(0.60f, 0.56f, 0.50f), 0.0022f);
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
        /// </summary>
        private void DealHand()
        {
            hand.Clear();
            if (turnLoop == null || cardsRoot == null) return;

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
        }

        // ── 工具 ──────────────────────────────────────────────────────
        private static Material MakeMaterial(Color color, float glossiness)
        {
            Shader sh = Shader.Find("Standard");
            if (sh == null) sh = Shader.Find("Diffuse");

            Material m = new Material(sh);
            m.color = color;
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", glossiness);
            if (m.HasProperty("_Metallic"))   m.SetFloat("_Metallic", 0f);
            return m;
        }
    }
}
