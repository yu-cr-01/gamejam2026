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

        // ── 面板专用样式 ──
        // 单独一套是因为 h1/body/dim 是给"没有背景"的场景配的：
        // dim 本身就是浅灰，铺到浅色面板上直接糊掉看不见。
        // 面板统一走"深色底 + 浅色字"，和游戏整体调子也一致。
        private GUIStyle panelBox, panelBoxInner, h1Panel, bodyPanel, dimPanel, btnClose;

        // ── 检视窗口 ──
        private const int   InspectWindowId = 0x54A1;
        private Rect        inspectRect = new Rect(16f, 14f, 450f, 520f);
        private bool        inspectPlaced;

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
            if (setup == null || setup.rig == null) return;

            // 1 / 2 / 3 / 4 切机位（4 = 自由转头）。
            // 写成循环，以后加机位不用再补一行。
            for (int i = 0; i < TableSetup.ViewNames.Length && i < 9; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                    setup.rig.GoTo(TableSetup.ViewNames[i]);
            }

            HandleScoreDemoKeys();
        }

        // ── 得分 / 冲压的演示按键 ─────────────────────────────────────
        // 数值系统还没接进来，先用手动按键把罐子和冲压效果跑起来，
        // 好确认表现对不对。等回合结算接上之后这几个键就可以删掉。
        private int demoScore;

        private void HandleScoreDemoKeys()
        {
            if (setup == null || setup.juicer == null) return;

            bool changed = false;

            if (Input.GetKeyDown(KeyCode.LeftBracket))  { demoScore -= 100; changed = true; }
            if (Input.GetKeyDown(KeyCode.RightBracket)) { demoScore += 100; changed = true; }
            if (Input.GetKeyDown(KeyCode.Alpha0))       { demoScore = 0;    changed = true; }

            if (changed)
            {
                demoScore = Mathf.Clamp(demoScore, 0, 1200);
                setup.juicer.SetScore(demoScore, 1000);
            }

            if (Input.GetKeyDown(KeyCode.S)) setup.juicer.PlayStamp();
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

                // ── 面板：深色底 + 浅色字 ──
                // border 走 9 宫格，所以底图只有 32×32，拉到多大圆角都不变形。
                panelBox = new GUIStyle(GUI.skin.box)
                {
                    border = new RectOffset(ProceduralArt.PanelBorder, ProceduralArt.PanelBorder,
                                            ProceduralArt.PanelBorder, ProceduralArt.PanelBorder),
                    padding = new RectOffset(12, 12, 12, 12)
                };
                panelBox.normal.background = ProceduralArt.PanelBackdrop();

                panelBoxInner = new GUIStyle(panelBox)
                {
                    padding = new RectOffset(9, 9, 7, 7)
                };

                h1Panel   = new GUIStyle(h1)   { fontSize = 19 };
                bodyPanel = new GUIStyle(body) { fontSize = 15 };
                dimPanel  = new GUIStyle(dim)  { fontSize = 13 };
                h1Panel.normal.textColor   = new Color(0.95f, 0.96f, 0.98f);
                bodyPanel.normal.textColor = new Color(0.87f, 0.90f, 0.94f);
                dimPanel.normal.textColor  = new Color(0.62f, 0.68f, 0.77f);

                btnClose = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 20, fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(0, 0, 0, 0)
                };

                if (cjkFont != null)
                {
                    h1Panel.font = cjkFont; bodyPanel.font = cjkFont;
                    dimPanel.font = cjkFont; btnClose.font = cjkFont;
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
            const float w = 440f, h = 168f;
            float y = Screen.height - h - 14f;

            GUI.Label(new Rect(16f, y, w, 24f), "拖动卡牌放到桌面卡槽", h1);
            GUI.Label(new Rect(16f, y + 28f, w, 22f), "松手时附近有空槽就吸附，否则回手牌", dim);
            GUI.Label(new Rect(16f, y + 48f, w, 22f), "右键单击卡牌 → 查看完整数据（Esc 关闭）", dim);
            GUI.Label(new Rect(16f, y + 68f, w, 22f), "右键拖动 / 中键拖动 → 原地转头（位置固定）", dim);
            GUI.Label(new Rect(16f, y + 88f, w, 22f), "1 / 2 / 3 固定视角　　4 自由视角　　G 开关物理", dim);
            GUI.Label(new Rect(16f, y + 108f, w, 22f),
                      "罐子得分面板：　[ 减 100　　] 加 100　　0 归零　　S 冲压刀片", dim);
            GUI.Label(new Rect(16f, y + 130f, w, 22f),
                      "物理：" + (interaction.PhysicsOn ? "开（受重力）" : "关（脚本控制）")
                      + "　　视角：" + (setup != null && setup.rig != null && setup.rig.IsFreeLook
                                        ? "自由转头中" : "固定机位"), dim);
        }

        // ── 划过时的小信息条 ──────────────────────────────────────────
        private void DrawCardInfo()
        {
            PlayCard card = interaction.FocusCard;
            if (card == null || card.data == null) return;

            const float w = 320f;
            float h = 130f + AttrCatalog.Count * 22f;

            GUI.Box(new Rect(16f, 14f, w, h), GUIContent.none, panelBox);

            Ingredient ing = card.data;
            GUI.Label(new Rect(30f, 26f, w - 28f, 28f), ing.name, h1Panel);

            float y = 60f;
            foreach (AttrDef def in AttrCatalog.All())
            {
                int v = ing.attrs.Get(def.id);
                GUI.Label(new Rect(30f, y, w - 28f, 22f),
                          def.Label + "：" + def.Format(v), v != 0 ? bodyPanel : dimPanel);
                y += 22f;
            }

            GUI.Label(new Rect(30f, y + 4f, w - 28f, 22f),
                      card.IsDragging ? "拿在手上"
                      : card.slotIndex >= 0 ? "已放在卡槽 " + (card.slotIndex + 1)
                      : "在手牌里", dimPanel);

            GUI.Label(new Rect(30f, y + 26f, w - 28f, 22f), "右键单击查看完整数据", dimPanel);
        }

        // ══════════════════════════════════════════════════════════════
        //  右键检视窗口（可拖拽）
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 右键卡牌弹出的完整数据**窗口**。
        ///
        /// 【为什么用 GUILayout.Window 而不是自己画固定面板】
        /// GUI.Window 自带了拖拽移动、置顶绘制、焦点独占这三件麻烦事。
        /// 自己用 BeginArea 实现"拖标题栏挪窗口"得手动记录按下点、算偏移、
        /// 每帧重设矩形，还很容易和别的输入打架（比如转视角的右键拖动）。
        ///
        /// 窗口化真正解决的问题：面板原来钉死在左上角，
        /// 挡住的地方正好是你想看的牌。现在能拖走。
        /// </summary>
        private void DrawInspectPanel()
        {
            if (interaction.Inspected == null || interaction.Inspected.data == null) return;

            if (!inspectPlaced)
            {
                inspectPlaced = true;

                // ★ 初始高度故意给小。
                //   GUILayout.Window 的自动适配是"只增不减"的：它会按内容高度把窗口
                //   撑大，但不会把你传入的过大的高度收回来。所以一开始给 560，
                //   底部就会永远空着；给一个小值，它自己会长到刚好够。
                inspectRect = new Rect(16f, 14f, 460f, 120f);
            }

            ClampInspectRect();

            // 返回值是拖拽之后的最新矩形，必须接回来，否则拖不动。
            //
            // ★ 不要在这里手动改 height。
            //   GUILayout.Window 本身就会把窗口高度收缩到内容高度（Unity 的经典行为），
            //   前提是内容里没有"强制撑满"的东西（ScrollView / ExpandHeight）。
            //   之前两版之所以底部空一大块，正是因为内容里放了 ScrollView ——
            //   它在 Window 里永远撑满可用空间，窗口自然收不回去。
            //   去掉 ScrollView 之后，自动适配就能正常工作了。
            inspectRect = GUILayout.Window(InspectWindowId, inspectRect,
                                           DrawInspectContents, GUIContent.none, panelBox);
        }

        /// <summary>
        /// 别让窗口被拖到完全看不见 —— 至少留 100 像素在屏幕内，好抓回来。
        /// 没有这一步，一次手滑把窗口推出边界就再也找不回来了。
        /// </summary>
        private void ClampInspectRect()
        {
            const float keep = 100f;
            inspectRect.x = Mathf.Clamp(inspectRect.x, -(inspectRect.width - keep), Screen.width - keep);
            inspectRect.y = Mathf.Clamp(inspectRect.y, 0f, Mathf.Max(0f, Screen.height - 34f));
        }

        /// <summary>窗口内容。id 是 GUI.Window 回调要求的参数。</summary>
        private void DrawInspectContents(int id)
        {
            PlayCard card = interaction.Inspected;
            if (card == null || card.data == null) return;

            Ingredient ing = card.data;
            Color accent = ProceduralArt.IngredientColor(ing.id);
            AttrId dominant = ProceduralArt.DominantAttr(ing);
            AttrDef domDef = AttrCatalog.Get(dominant);

            const float headerH = 42f;
            const float closeW  = 34f;
            float width = inspectRect.width;

            // ── 标题条：卡面同款主题色，兼作拖拽把手 ──
            GUILayout.BeginHorizontal();

            GUILayout.Box(ing.name, HeaderStyle(accent),
                          GUILayout.Height(headerH - 8f), GUILayout.ExpandWidth(true));

            if (GUILayout.Button("×", btnClose, GUILayout.Width(closeW), GUILayout.Height(headerH - 8f)))
                interaction.Inspect(null);

            GUILayout.EndHorizontal();

            // ★ 拖拽区域要把右边关闭按钮那块**挖掉**，
            //   否则 DragWindow 会把按钮的点击一起吃掉，× 就点不动了。
            GUI.DragWindow(new Rect(0f, 0f, Mathf.Max(20f, width - closeW - 26f), headerH));

            // ── 内容区 ──
            // ★ 这里**故意不用 ScrollView**。
            //   滚动视图在 Window 里永远撑满可用空间，于是"量内容高度"这个动作
            //   永远量到窗口高度本身，窗口就永远收不回去（踩过两次）。
            //   卡牌数据是固定长度（印记 + 4 行 + 3 个属性块），直接自然排布就行；
            //   真长到超过屏幕时，下面的高度夹取会兜住，不会溢出到看不见。
            GUILayout.Space(4f);

            // 印记 + 基本信息
            GUILayout.BeginHorizontal();

            GUILayout.Label(ProceduralArt.Emblem(dominant, accent, 96),
                            GUILayout.Width(88f), GUILayout.Height(88f));

            GUILayout.BeginVertical();
            GUILayout.Label("类型：食材", bodyPanel);
            GUILayout.Label("牌组：" + (setup != null && !string.IsNullOrEmpty(setup.deckName)
                                       ? setup.deckName : "（未知）"), bodyPanel);
            GUILayout.Label("位置：" + PositionText(card), bodyPanel);
            GUILayout.Label("主属性：" + (domDef != null
                                        ? domDef.Label + "（" + domDef.tendency + "）"
                                        : "?"), dimPanel);
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();

            GUILayout.Space(10f);
            GUILayout.Label("── 三属性完整数据 ──", h1Panel);
            GUILayout.Space(4f);

            foreach (AttrDef def in AttrCatalog.All())
            {
                int v = ing.attrs.Get(def.id);
                bool isDominant = (def.id == dominant);

                GUILayout.BeginVertical(panelBoxInner);

                GUILayout.Label(def.Label + "　" + def.Format(v) + "　（" + def.tendency + "）"
                                + (isDominant ? "　← 主属性" : ""),
                                v != 0 ? bodyPanel : dimPanel);

                GUILayout.Label(def.description, dimPanel);

                GUILayout.EndVertical();
                GUILayout.Space(4f);
            }

            GUILayout.Space(6f);
            GUILayout.Label("拖动标题栏移动窗口　｜　右键空白处或 Esc 关闭", dimPanel);
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
