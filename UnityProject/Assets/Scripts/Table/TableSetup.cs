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
            DealHand();
            BuildJuicer();
            BuildInteraction();
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

            // 4 列 × 2 行。卡是 0.24 × 0.335，留一点间隙
            board.BuildGrid(4, 2, 0.30f, 0.40f, new Vector3(0f, 0f, 0.17f));

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
        }

        // ── 容器 ──────────────────────────────────────────────────────
        private void BuildRoots()
        {
            GameObject go = new GameObject("Cards");
            go.transform.SetParent(transform, false);
            cardsRoot = go.transform;
        }

        // ── 发牌 ──────────────────────────────────────────────────────
        private void DealHand()
        {
            // 手牌来自配置表里第一副牌组的"初始手牌"——
            // 也就是牌组里除了开局刀片以外的那些牌。
            // 3D 桌面原型只是把同一份配置换个画法，不另建一套数据。
            GameConfig.EnsureLoaded();

            List<Deck> decks = GameConfig.Decks();
            Deck deck = decks.Count > 0 ? decks[0] : null;
            deckName = deck != null ? deck.name : "";

            List<Ingredient> data = deck != null
                ? deck.InitialHandIngredients()
                : new List<Ingredient>();

            // 兜底：配置表空掉了也至少有一张牌可摆
            if (data.Count == 0)
            {
                Ingredient fallback = GameConfig.DefaultBlade();
                if (fallback != null) data.Add(fallback);
            }
            if (data.Count == 0) return;

            const float z = -0.58f;
            const float gap = 0.30f;
            float startX = -(data.Count - 1) * gap * 0.5f;

            for (int i = 0; i < data.Count; i++)
            {
                // 手牌摆成微微的扇形，朝向相机一侧张开
                float fan = (i - (data.Count - 1) * 0.5f) * 4f;
                Vector3 home = new Vector3(startX + i * gap, 0f, z);
                Vector3 euler = new Vector3(0f, fan, 0f);

                PlayCard card = CardFactory.Create(data[i], cardsRoot, home, euler);
                hand.Add(card);
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
