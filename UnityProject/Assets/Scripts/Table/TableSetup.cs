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

        /// <summary>视角名，供 HUD 和快捷键使用</summary>
        public static readonly string[] ViewNames = { "board", "hand", "top" };
        public static readonly string[] ViewLabels = { "桌面视角", "手牌特写", "俯视" };

        private static readonly Color TableColor  = new Color(0.22f, 0.17f, 0.14f);
        private static readonly Color MarkerColor = new Color(0.30f, 0.34f, 0.42f);

        public readonly List<PlayCard> hand = new List<PlayCard>();

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
            BuildInteraction();
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

            // 三个机位
            rig.Register("board", new Vector3(0f, 1.05f, -1.02f), new Vector3(0f, 0f, 0.10f));
            rig.Register("hand",  new Vector3(0f, 0.52f, -1.00f), new Vector3(0f, 0f, -0.56f));
            rig.Register("top",   new Vector3(0f, 1.55f, 0.02f),  new Vector3(0f, 0f, 0.02f));

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

            slotMarkers = new Renderer[board.SlotCount];
            for (int i = 0; i < board.SlotCount; i++)
            {
                GameObject m = GameObject.CreatePrimitive(PrimitiveType.Cube);
                m.name = "Slot" + i;
                m.transform.SetParent(go.transform, false);
                m.transform.position = board.SlotPosition(i) + new Vector3(0f, 0.0025f, 0f);
                m.transform.localScale = new Vector3(0.255f, 0.003f, 0.35f);

                Collider c = m.GetComponent<Collider>();
                if (c != null) c.enabled = false;      // 别挡住卡牌的射线

                Renderer mr = m.GetComponent<Renderer>();
                if (mr != null) mr.material = MakeMaterial(MarkerColor, 0.15f);
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
            FakeData.EnsureCatalog();

            List<Ingredient> data = IngredientCatalog.CreateMany(
                FakeData.IronBlockId, FakeData.IceCubeId, FakeData.LemonId);

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
