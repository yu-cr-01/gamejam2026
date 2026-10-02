using System.Collections.Generic;
using UnityEngine;
using GameJam.Data;

namespace GameJam.Prototype
{
    /// <summary>
    /// 桌面原型的 HUD：视角按钮、操作提示、卡牌详情。
    ///
    /// 用 IMGUI（OnGUI）而不是 UGUI —— 不需要 Canvas / EventSystem / Prefab，
    /// 而且这块以后要换成正式的 3D 卡牌 UI，用 IMGUI 的代码删起来干净。
    ///
    /// 【两种详情】
    ///   鼠标划过   → 左上角一个小信息条（看一眼就走的场合）
    ///   右键点一下 → 整块检视面板，把三属性的完整说明都摊开（研究牌的场合）
    /// 两种不会同时出现，检视面板是小信息条的超集。
    /// </summary>
    public class TableHud : MonoBehaviour
    {
        private TableInteraction interaction;
        private TableSetup       setup;

        private Font cjkFont;
        private GUIStyle h1, body, dim, btn, btnOn;

        /// <summary>按主题色缓存的标题条样式（见 HeaderStyle）</summary>
        private readonly Dictionary<string, GUIStyle> headerStyles = new Dictionary<string, GUIStyle>();

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

            // 检视面板和划过信息条是同一个位置，二选一
            if (interaction.Inspected != null) DrawInspectPanel();
            else                               DrawCardInfo();
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
            const float w = 400f, h = 122f;
            float y = Screen.height - h - 14f;

            GUI.Label(new Rect(16f, y, w, 24f), "拖动卡牌放到桌面卡槽", h1);
            GUI.Label(new Rect(16f, y + 28f, w, 22f), "松手时附近有空槽就吸附，否则回手牌", dim);
            GUI.Label(new Rect(16f, y + 48f, w, 22f), "右键卡牌 → 查看完整数据（Esc 关闭）", dim);
            GUI.Label(new Rect(16f, y + 68f, w, 22f), "1 / 2 / 3 切换视角　　G 开关物理（碰撞演示）", dim);
            GUI.Label(new Rect(16f, y + 88f, w, 22f),
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
                          def.Label + "：" + def.Format(v), v != 0 ? body : dim);
                y += 20f;
            }

            if (!card.IsDragging && card.slotIndex >= 0)
                GUI.Label(new Rect(28f, y + 4f, w - 24f, 22f), "已放在卡槽 " + (card.slotIndex + 1), dim);
        }

        // ══════════════════════════════════════════════════════════════
        //  右键检视面板
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 右键卡牌弹出的完整数据面板。
        ///
        /// 和卡面上那三行缩略不同，这里把每个属性的**作用说明**整段摊开 ——
        /// 卡面只够放"是什么"，"意味着什么"得在这里看。
        /// 面板用 GUILayout 自动排版，属性说明长短不一也不会错位。
        /// </summary>
        private void DrawInspectPanel()
        {
            PlayCard card = interaction.Inspected;
            if (card == null || card.data == null) return;

            Ingredient ing = card.data;
            Color accent = ProceduralArt.IngredientColor(ing.id);
            AttrId dominant = ProceduralArt.DominantAttr(ing);
            AttrDef domDef = AttrCatalog.Get(dominant);

            // 宽度固定，高度撑满可视区域（内容超了会自动滚动条，不用自己算）
            GUILayout.BeginArea(new Rect(16f, 14f, 430f, Screen.height - 28f), GUI.skin.box);
            GUILayout.BeginVertical();

            // 标题条 —— 用卡面同款主题色，一眼对得上桌上那张
            GUILayout.Box(ing.name, HeaderStyle(accent),
                          GUILayout.Height(50f), GUILayout.ExpandWidth(true));

            GUILayout.Space(10f);

            // 印记 + 基本信息
            GUILayout.BeginHorizontal();

            GUILayout.Label(ProceduralArt.Emblem(dominant, accent, 96),
                            GUILayout.Width(88f), GUILayout.Height(88f));

            GUILayout.BeginVertical();
            GUILayout.Label("类型：食材", body);
            GUILayout.Label("牌组：" + (setup != null && !string.IsNullOrEmpty(setup.deckName)
                                       ? setup.deckName : "（未知）"), body);
            GUILayout.Label("位置：" + PositionText(card), body);
            GUILayout.Label("主属性：" + (domDef != null
                                        ? domDef.Label + "（" + domDef.tendency + "）"
                                        : "?"), dim);
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();

            GUILayout.Space(12f);
            GUILayout.Label("── 三属性完整数据 ──", h1);
            GUILayout.Space(4f);

            foreach (AttrDef def in AttrCatalog.All())
            {
                int v = ing.attrs.Get(def.id);
                bool isDominant = (def.id == dominant);

                GUILayout.BeginVertical(GUI.skin.box);

                GUILayout.Label(def.Label + "　" + def.Format(v) + "　（" + def.tendency + "）"
                                + (isDominant ? "　← 主属性" : ""),
                                v != 0 ? body : dim);

                GUILayout.Label(def.description, dim);

                GUILayout.EndVertical();
                GUILayout.Space(4f);
            }

            GUILayout.Space(8f);
            GUILayout.Label("右键空白处或按 Esc 关闭", dim);

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        /// <summary>卡牌现在在哪 —— 检视面板上要显示。</summary>
        private string PositionText(PlayCard card)
        {
            if (card.IsDragging) return "拿在手上";
            if (card.slotIndex >= 0) return "桌面卡槽 " + (card.slotIndex + 1);
            return "手牌";
        }

        /// <summary>
        /// 用主题色填充的标题条样式。
        ///
        /// IMGUI 没有"设置背景色再画矩形"的直接办法（GUI.color 只给贴图着色，
        /// 不会凭空涂出一个矩形），所以为每个颜色生成一张 1×1 贴图当 GUIStyle 背景，
        /// 再按颜色缓存 —— 每张牌的颜色是稳定的，实际只会建十来个。
        /// </summary>
        private GUIStyle HeaderStyle(Color c)
        {
            string key = Mathf.RoundToInt(c.r * 255f) + "_"
                       + Mathf.RoundToInt(c.g * 255f) + "_"
                       + Mathf.RoundToInt(c.b * 255f);

            GUIStyle st;
            if (headerStyles.TryGetValue(key, out st) && st != null) return st;

            Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, c);
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;   // 别让它在场景卸载时被销毁

            st = new GUIStyle(GUI.skin.box);
            st.normal.background = tex;
            st.normal.textColor = ProceduralArt.InkOn(c);   // 浅底配墨字、深底配白字
            st.fontSize = 26;
            st.fontStyle = FontStyle.Bold;
            st.alignment = TextAnchor.MiddleLeft;
            st.padding = new RectOffset(16, 16, 6, 6);
            if (cjkFont != null) st.font = cjkFont;

            headerStyles[key] = st;
            return st;
        }
    }
}
