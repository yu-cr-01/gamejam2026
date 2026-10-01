using UnityEngine;
using GameJam.Data;

namespace GameJam.Prototype
{
    /// <summary>
    /// 桌面原型的 HUD：视角按钮、操作提示、当前卡的属性详情。
    ///
    /// 用 IMGUI（OnGUI）而不是 UGUI —— 不需要 Canvas / EventSystem / Prefab，
    /// 而且这块以后要换成正式的 3D 卡牌 UI，用 IMGUI 的代码删起来干净。
    /// </summary>
    public class TableHud : MonoBehaviour
    {
        private TableInteraction interaction;
        private TableSetup       setup;

        private Font cjkFont;
        private GUIStyle h1, body, dim, btn, btnOn;

        private void Start()
        {
            interaction = GetComponent<TableInteraction>();
            if (interaction == null) interaction = Object.FindObjectOfType<TableInteraction>();
            setup = Object.FindObjectOfType<TableSetup>();
        }

        private void Update()
        {
            // 1 / 2 / 3 切机位
            if (setup == null || setup.rig == null) return;

            if (Input.GetKeyDown(KeyCode.Alpha1)) setup.rig.GoTo(TableSetup.ViewNames[0]);
            if (Input.GetKeyDown(KeyCode.Alpha2)) setup.rig.GoTo(TableSetup.ViewNames[1]);
            if (Input.GetKeyDown(KeyCode.Alpha3)) setup.rig.GoTo(TableSetup.ViewNames[2]);
        }

        private void EnsureStyles()
        {
            if (cjkFont == null)
            {
                string[] candidates =
                {
                    "Microsoft YaHei", "微软雅黑", "SimHei", "SimSun",
                    "PingFang SC", "Noto Sans CJK SC", "Arial Unicode MS"
                };
                cjkFont = Font.CreateDynamicFontFromOSFont(candidates, 18);
            }

            if (h1 == null)
            {
                h1   = new GUIStyle(GUI.skin.label)  { fontSize = 22, fontStyle = FontStyle.Bold, wordWrap = true };
                body = new GUIStyle(GUI.skin.label)  { fontSize = 16, wordWrap = true };
                dim  = new GUIStyle(GUI.skin.label)  { fontSize = 14, wordWrap = true };
                dim.normal.textColor = new Color(0.62f, 0.66f, 0.74f);

                btn   = new GUIStyle(GUI.skin.button) { fontSize = 15 };
                btnOn = new GUIStyle(btn);
                btnOn.normal.textColor = new Color(0.35f, 0.95f, 0.60f);

                if (cjkFont != null)
                {
                    h1.font = cjkFont; body.font = cjkFont; dim.font = cjkFont;
                    btn.font = cjkFont; btnOn.font = cjkFont;
                }
            }
        }

        private void OnGUI()
        {
            EnsureStyles();
            if (setup == null || interaction == null) return;

            DrawViewButtons();
            DrawHints();
            DrawCardInfo();
        }

        // ── 右上角：视角切换 ──────────────────────────────────────────
        private void DrawViewButtons()
        {
            const float w = 110f, h = 32f, pad = 8f;
            float x = Screen.width - w - 16f;
            float y = 14f;

            GUI.Label(new Rect(x - 96f, y + 6f, 90f, 24f), "视角：", body);

            for (int i = 0; i < TableSetup.ViewNames.Length; i++)
            {
                string name = TableSetup.ViewNames[i];
                bool on = setup.rig != null && setup.rig.CurrentView == name;

                if (GUI.Button(new Rect(x, y, w, h), TableSetup.ViewLabels[i], on ? btnOn : btn))
                    if (setup.rig != null) setup.rig.GoTo(name);

                y += h + pad;
            }
        }

        // ── 左下角：操作提示 ──────────────────────────────────────────
        private void DrawHints()
        {
            const float w = 380f, h = 96f;
            float y = Screen.height - h - 14f;

            GUI.Label(new Rect(16f, y, w, 24f), "拖动卡牌放到桌面卡槽", h1);
            GUI.Label(new Rect(16f, y + 28f, w, 22f), "松手时附近有空槽就吸附，否则回手牌", dim);
            GUI.Label(new Rect(16f, y + 48f, w, 22f), "1 / 2 / 3 切换视角　　G 开关物理（碰撞演示）", dim);
            GUI.Label(new Rect(16f, y + 68f, w, 22f),
                      "物理：" + (interaction.PhysicsOn ? "开（受重力）" : "关（脚本控制）"), dim);
        }

        // ── 左上角：当前卡详情 ────────────────────────────────────────
        private void DrawCardInfo()
        {
            PlayCard card = interaction.FocusCard;
            if (card == null || card.data == null) return;

            const float w = 300f, h = 190f;
            GUI.Box(new Rect(16f, 14f, w, h), GUIContent.none);

            Ingredient ing = card.data;
            GUI.Label(new Rect(28f, 22f, w - 24f, 28f), ing.name, h1);

            float y = 56f;
            foreach (AttrDef def in AttrCatalog.All())
            {
                int v = ing.attrs.Get(def.id);
                GUI.Label(new Rect(28f, y, w - 24f, 22f),
                          def.displayName + "：" + def.Format(v), v != 0 ? body : dim);
                y += 20f;
            }

            if (!card.IsDragging && card.slotIndex >= 0)
                GUI.Label(new Rect(28f, y + 4f, w - 24f, 22f), "已放在卡槽 " + (card.slotIndex + 1), dim);
        }
    }
}
