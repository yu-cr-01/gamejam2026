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
        private TableTurnLoop    turnLoop;

        private Font cjkFont;
        private GUIStyle h1, body, dim, btn, btnOn;

        // ── 面板专用样式 ──
        // 单独一套是因为 h1/body/dim 是给"没有背景"的场景配的：
        // dim 本身就是浅灰，铺到浅色面板上直接糊掉看不见。
        // 面板统一走"深色底 + 浅色字"，和游戏整体调子也一致。
        private GUIStyle panelBox, panelBoxInner, panelGroup, h1Panel, bodyPanel, dimPanel, btnClose;

        // ── v2.1 面板：半透明底板（用户反馈"回合数 UI 挡住上面那张牌"）──
        // 底透、字不透：只换底板贴图的 alpha，文字样式一个像素都不动。
        private GUIStyle panelGroupGlass, panelBoxGlass, reportBoxGlass, scrollGlass;

        // ── v2.1 面板：可拖动 ──
        private const float PanelKeepOnScreen = 40f;   // 拖到天边也至少有这么多像素留在屏幕里（见 ClampPanelOffset）
        private const float GlassAlpha       = 0.78f;  // 回合 / 选刀片 / 结算这一档
        private const float GlassAlphaReport = 0.88f;  // 规则报告单独更实一点（理由见 EnsureStyles）

        /// <summary>
        /// 一块面板的拖动状态。
        ///
        /// 【为什么每块面板各一份】位置得各记各的（把回合面板拖到左边，
        /// 不该把结算面板一起带走），"鼠标压在哪块上"也只能按各自的矩形判断。
        /// </summary>
        private sealed class PanelDrag
        {
            /// <summary>相对"设计位置"的位移（设计位置 = 居中 / 顶部那些算出来的位置）。</summary>
            public Vector2 offset;

            /// <summary>
            /// 上一帧面板矩形，**没有**加位移的那一份 ——
            /// 它和 GUI.matrix 里拿到的 Event.mousePosition 处在同一个坐标系（见 HandlePanelDrag）。
            /// </summary>
            public Rect lastRect;

            public bool dragging;
        }

        private readonly PanelDrag turnPanelDrag   = new PanelDrag();
        private readonly PanelDrag bladePanelDrag  = new PanelDrag();
        private readonly PanelDrag turnResultDrag  = new PanelDrag();
        private readonly PanelDrag levelEndDrag    = new PanelDrag();
        private readonly PanelDrag levelResultDrag = new PanelDrag();
        private readonly PanelDrag reportDrag      = new PanelDrag();
        private readonly PanelDrag levelWindowDrag = new PanelDrag();

        // BeginCenterPanel / EndCenterPanel 之间传状态（End 时才知道面板画在哪、要还原哪个矩阵）
        private PanelDrag centerPanelDrag;
        private Matrix4x4 centerPanelPrevMatrix;

        // 每帧记录"v2.1 面板在屏幕上的矩形"（= 设计矩形 + 位移）。
        // Update 里拿它判断鼠标是不是压在面板上 —— 见 UpdatePanelPickBlock。
        private readonly List<Rect> v21PanelRects = new List<Rect>();
        private bool pickBlocked;                                          // 上一帧是不是已经关过碰撞体
        private readonly List<Collider> pickBlockedColliders = new List<Collider>();   // 只记自己关过的，回头原样打开

        // ── 开场界面的大标题 ──
        private GUIStyle titleBig, titleSub, titleHint;

        // ── 杯内 2D 模拟视图 ──
        private CupSimView cupView;

        // ── v2.1 的规则解析报告面板（F2）──
        private bool     rulesReportOpen;
        private Vector2  rulesReportScroll;

        // ── 关卡窗口（B：用户要"单独一个窗口显示关卡"）──
        private bool     levelWindowOpen;
        private Vector2  levelWindowScroll;
        private int      levelWindowPick = -1;      // 窗口里点中的那一关（-1 = 还没点）
        private bool     wasLevelSelect;            // 上一帧是不是关卡界面（用来"进关卡界面自动弹窗"）
        // 本次运行内记下每关过没过：Level 的运行状态只对"当前这一关"有效，
        // 换关之后就丢了 —— 窗口要显示"已通过/未通过"就得自己记一份。
        private readonly Dictionary<int, bool> levelCleared = new Dictionary<int, bool>();

        // ── 卡牌图鉴（F1，实现在 CardBrowser）──
        private CardBrowser browser;

        // 报告内容的排版高度 / 滚动区可见高度。
        // 滚动到底要用它们算偏移 —— 见 ScrollRulesReportToEnd 里为什么不能
        // "塞一个超大值让 Unity 自己夹"。
        private float    reportContentH;
        private float    reportViewH;

        // v2.1 回合结算屏里那块"引擎日志"的滚动位置（日志条数不定，得能滚）
        private Vector2  turnResultLogScroll;

        // BeginCenterPanel 算出来的面板宽度：面板里那些"定宽按钮"要按它夹一下，
        // 否则窗口很窄时按钮比面板还宽，又会被顶出去
        private float    centerPanelW;

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
            turnLoop = Object.FindObjectOfType<TableTurnLoop>();
            browser = Object.FindObjectOfType<CardBrowser>();
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

            HandleMenuKeys();
            HandleConfirmKey();
            HandleRulesKeys();

            // 鼠标压在 v2.1 面板上时，把桌面卡牌的拾取先关掉（防点击穿透，见该方法说明）
            UpdatePanelPickBlock();
        }

        /// <summary>
        /// F2 = 打开"卡牌规则解析报告"（未识别清单全文）。
        ///
        /// 【为什么必须有这个入口】"3 条规则没实现"这句话本身没法行动 ——
        /// 策划要知道是哪三张卡的哪句话、为什么没认出来、该怎么改。
        /// 报告就是为这个存在的（RuleReport），不给入口等于没做。
        /// </summary>
        private void HandleRulesKeys()
        {
            if (Input.GetKeyDown(KeyCode.F2)) rulesReportOpen = !rulesReportOpen;
        }

        /// <summary>
        /// 打开 / 关闭规则解析报告 —— **自动试玩探针用**（平时玩家按 F2）。
        ///
        /// 【为什么要有这个口子】报告平时是关着的，而"统计行会不会被裁、
        /// 最后一行能不能完整滚出来"这两个毛病**只有把面板打开、拍下来才看得见**。
        /// 探针不该去伪造一个 F2 按键（输入事件在编辑器里塞不进去），
        /// 所以这里开一个和 F2 完全等价的方法，走的是同一个字段。
        /// </summary>
        public void SetRulesReportOpen(bool open)
        {
            rulesReportOpen = open;
        }

        /// <summary>
        /// 把报告滚到最底 —— 自动试玩探针用。
        ///
        /// 【为什么需要它】报告是一屏滚动的，一张截图只能拍到头部，
        /// 而"最下面一行只显示一半"这个毛病**只在底部才看得见**。
        /// 玩家自己拖滚动条到的就是这个位置，这里只是替他拖到底。
        ///
        /// 【为什么不用 float.MaxValue 让 Unity 自己夹】内容比可见区矮的时候
        /// 根本不会出现滚动条，那个超大值就没人夹，整块内容会被推出可视区。
        /// 用上一帧量到的两个高度自己算，任何情况下都是"正好到底"。
        /// </summary>
        public void ScrollRulesReportToEnd()
        {
            rulesReportScroll.y = Mathf.Max(0f, reportContentH - reportViewH);
        }

        /// <summary>
        /// 把 v2.1 回合面板整体挪一段 —— **自动试玩探针用**（玩家用鼠标拖）。
        ///
        /// 【为什么需要它】探针不模拟鼠标输入（它只走游戏自己的公开入口），
        ///   而"面板能拖走、拖了不会出屏"这两件事只能靠截图看。这里喂的是
        ///   **和鼠标拖动完全相同**的那条路：同一个 offset 字段、
        ///   同一个 ClampPanelOffset 夹取、同一个 GUI.matrix 绘制。
        ///   换句话说，除了"位移是谁给的"，其余全是玩家那条路。
        /// </summary>
        public void DragTurnPanelBy(Vector2 delta)
        {
            turnPanelDrag.offset = ClampPanelOffset(turnPanelDrag.offset + delta, turnPanelDrag.lastRect);
        }

        /// <summary>
        /// 回合面板当前的位移（探针拿它验证"夹取生效了没有" —— 喂一个巨大的 delta 之后，
        /// 这个值应该停在"面板还有 40 像素留在屏幕里"的那个位置，而不是真的飞出去）。
        /// </summary>
        public Vector2 TurnPanelOffset { get { return turnPanelDrag.offset; } }

        /// <summary>
        /// 开关关卡窗口 —— 自动试玩探针用（玩家走 Esc 菜单那一项，同一条路）。
        /// 打开时把选中停在当前关卡上，和菜单里点开的行为一致。
        /// </summary>
        public void SetLevelWindowOpen(bool open)
        {
            levelWindowOpen = open;
            if (open && turnLoop != null) levelWindowPick = turnLoop.levelIndex;
        }

        /// <summary>
        /// 回车 / 小键盘回车 = 当前阶段的确认键。
        ///
        /// 【为什么要有键盘这一路】
        /// 卡是拖到桌面上放的，放完之后手还在鼠标上，
        /// 要么横跨半个屏幕去够按钮、要么就得放下鼠标 —— 回车省掉这一步。
        /// </summary>
        private void HandleConfirmKey()
        {
            if (turnLoop == null) return;
            if (!Input.GetKeyDown(KeyCode.Return) && !Input.GetKeyDown(KeyCode.KeypadEnter)) return;

            switch (turnLoop.phase)
            {
                case TablePhase.Title:       turnLoop.ConfirmTitleStart(); break;
                case TablePhase.LevelSelect: turnLoop.ConfirmLevelSelect(); break;
                case TablePhase.DeckPick:    turnLoop.ConfirmDeckPick();  break;
                case TablePhase.BladePick:   turnLoop.ConfirmBladePick(); break;

                default:
                    if (turnLoop.CanInteract && turnLoop.StagedCount > 0) turnLoop.Confirm();
                    break;
            }
        }

        /// <summary>
        /// ESC 一层一层退：先关检视面板，再关设置，最后才是暂停菜单。
        ///
        /// 三件事都由这一处决定 —— 之前检视面板的 Esc 归 TableInteraction 管、
        /// 别的归这里管，两边各读一次 Input.GetKeyDown(Escape)，
        /// 就会出现"关掉面板的同时把游戏也暂停了"。
        /// </summary>
        private void HandleMenuKeys()
        {
            if (turnLoop == null) return;
            if (!Input.GetKeyDown(KeyCode.Escape)) return;

            if (interaction != null && interaction.Inspected != null)
            {
                interaction.Inspect(null);
                return;
            }

            if (turnLoop.settingsOpen) { turnLoop.settingsOpen = false; return; }

            // 关卡窗口 / 卡牌图鉴也在"一层一层退"的链子上：它们在暂停菜单之上，
            // 所以要先关它们，不然按 Esc 会跳过一层（面板还在，人以为没反应）。
            if (browser != null && browser.IsOpen) { browser.SetOpen(false); return; }
            if (levelWindowOpen) { levelWindowOpen = false; return; }

            // 开场、开局准备、总结算没有"暂停"这回事
            if (turnLoop.IsTitle || turnLoop.IsPreparing) return;
            if (turnLoop.phase == TablePhase.LevelResult) return;

            turnLoop.paused = !turnLoop.paused;
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
                // ★ 标题不要用 FontStyle.Bold。
                //   雅黑这类中文字体没有单独的粗体字面，Unity 只能"伪粗体"——
                //   把同一个字形错开一两像素叠着画几遍。英文字母小字号下还看得过去，
                //   22 号的中文就会糊成一团双影（截图放大后非常明显）。
                //   要拉开层次就加大字号、提亮颜色，别动 Bold。
                h1   = new GUIStyle(GUI.skin.label)  { fontSize = 23, wordWrap = true };
                h1.normal.textColor = new Color(0.93f, 0.95f, 0.98f);

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

                // 给"把面板当 GUILayout 容器用"的那一份：组的底是**内容排完之后**
                // 才按组矩形画的，所以内容多高、面板就多高 —— v2.1 那几块面板
                // 就是靠这个不再裁字的（详见 DrawTurnPanelV21 的说明）。
                // margin 清零是因为组会把样式的 margin 也算进摆放位置，
                // 留 4 像素的 margin 居中就会偏几像素；清零后组矩形 = 算好的矩形。
                panelGroup = new GUIStyle(panelBox)
                {
                    margin = new RectOffset(0, 0, 0, 0)
                };

                // ── v2.1 面板：半透明底板 ──
                // 【为什么不直接用 GUI.color 罩一层】GUI.color 会把这块区域里画的
                //   **所有东西**（包括文字）一起乘上 alpha —— 面板是透了，字也跟着糊了。
                //   要"底透、字不透"，只能让底板贴图自己带 alpha（见 TintAlpha）。
                //
                // 【两档 alpha】回合 / 选刀片 / 结算走 0.78：能透出后面的牌，字仍然清楚。
                //   规则报告走 0.88 更实一点 —— 那上面是几百行密排的中文，
                //   桌子透过来太多会明显影响读字，报告是"查资料"，可读性优先。
                //
                // 复制失败（拿不到那张贴图）时**保持不透明**：宁可不透，
                // 也不能把面板整个画没了（new GUIStyle(panelBox) 自带那张不透明底）。
                Texture2D glassMid    = TintAlpha(ProceduralArt.PanelBackdrop(), GlassAlpha);
                Texture2D glassReport = TintAlpha(ProceduralArt.PanelBackdrop(), GlassAlphaReport);

                panelGroupGlass = new GUIStyle(panelGroup);
                panelBoxGlass   = new GUIStyle(panelBox);
                reportBoxGlass  = new GUIStyle(panelBox);

                if (glassMid != null)
                {
                    panelGroupGlass.normal.background = glassMid;
                    panelBoxGlass.normal.background   = glassMid;
                }
                if (glassReport != null) reportBoxGlass.normal.background = glassReport;

                // 滚动区的底板也得跟着透：不然面板中间会被它盖回不透明，
                // 半透明就只剩边框那一圈了（回合结算的日志区、报告正文区都是它）。
                // 除了背景，其它（padding / margin）照抄皮肤里那份，滚动区几何不变。
                scrollGlass = new GUIStyle(GUI.skin.scrollView);
                scrollGlass.normal.background = null;

                h1Panel   = new GUIStyle(h1)   { fontSize = 21 };
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

                // ── 开场界面的大标题 ──
                // 同样不用 Bold —— 见上面 h1 那段关于伪粗体的说明。
                // 标题靠字号（56）和暖色拉开层次，不靠加粗。
                titleBig  = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 56, alignment = TextAnchor.MiddleCenter
                };
                titleSub  = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 19, alignment = TextAnchor.MiddleCenter
                };
                titleHint = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 17, alignment = TextAnchor.MiddleCenter
                };

                titleBig.normal.textColor  = new Color(0.94f, 0.88f, 0.74f);
                titleSub.normal.textColor  = new Color(0.60f, 0.55f, 0.46f);
                titleHint.normal.textColor = new Color(0.88f, 0.74f, 0.46f);

                if (cjkFont != null)
                {
                    titleBig.font = cjkFont; titleSub.font = cjkFont; titleHint.font = cjkFont;
                }

                // 杯内 2D 视图自己建样式，但字体跟 HUD 共用一份
                cupView = new CupSimView();
                cupView.Setup(cjkFont);
            }
        }

        /// <summary>
        /// 把一张贴图整张**乘一个 alpha** 复制一份（原图不动）。
        ///
        /// 【为什么是"复制 ProceduralArt 那张"而不是另画一张半透明底】
        ///   底板是 32×32 + 9 宫格拉伸（border 8）：圆角、描边、填充全在那张图里。
        ///   照它改 alpha，v2.1 面板的形状就和别的面板**完全一致**；
        ///   以后谁调了那张底图，这边自动跟着变，不会长出第二套"长得不太一样"的面板。
        ///
        /// 【为什么不能直接改原图】那张是 ProceduralArt 全局缓存的，暂停菜单 / 设置 /
        ///   检视窗口都在用；就地改 alpha 会把旧流程面板一起变透明（用户没让改那些）。
        /// </summary>
        private static Texture2D TintAlpha(Texture2D src, float alpha)
        {
            if (src == null) return null;

            Color32[] px;
            try { px = src.GetPixels32(); }
            catch { return null; }   // 贴图不可读时退化成"不透明"，绝不抛出去把 OnGUI 打死

            for (int i = 0; i < px.Length; i++)
                px[i].a = (byte)Mathf.Clamp(Mathf.RoundToInt(px[i].a * alpha), 0, 255);

            Texture2D tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply(false, false);
            tex.hideFlags = HideFlags.HideAndDontSave;   // 别在场景卸载时被销毁（和 HeaderStyle 同一处理）
            return tex;
        }

        // ══════════════════════════════════════════════════════════════
        //  v2.1 面板：拖动 + 防"点击穿透"
        // ══════════════════════════════════════════════════════════════

        /// <summary>位移矩阵：把面板整体挪到 offset 处。布局代码一行都不用改。</summary>
        private static Matrix4x4 OffsetMatrix(Vector2 offset)
        {
            return Matrix4x4.TRS(new Vector3(offset.x, offset.y, 0f), Quaternion.identity, Vector3.one);
        }

        /// <summary>IMGUI 的鼠标坐标：**左上原点**（和 Event.mousePosition 同一套）。</summary>
        private static Vector2 ScreenMouse()
        {
            return new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        }

        /// <summary>面板在屏幕上的矩形 = 设计矩形 + 位移。</summary>
        private static Rect ScreenRect(Rect rect, Vector2 offset)
        {
            return new Rect(rect.x + offset.x, rect.y + offset.y, rect.width, rect.height);
        }

        /// <summary>
        /// 处理一块面板的拖动，并把它这一帧的矩形记下来。
        ///
        /// 【为什么必须在**内容画完之后**调用】IMGUI 的事件是"先画的先拿"：
        ///   面板上的按钮 / 滚动条先画、先消费掉按下的那一下，所以点按钮不会变成拖面板；
        ///   只有落在没人消费的地方（标题、空白、文字）才会走到这里开始拖 ——
        ///   正好就是"标题栏 / 空白处可拖"，不用再自己划一块拖动区。
        ///
        /// 【为什么用 Event.delta 而不是"鼠标位置 − 按下位置"】拖动时面板自己也在动，
        ///   用绝对位置算会互相追、发飘；delta 是事件自带的位移，跟手。
        ///
        /// 【★ 几何判定用 Input.mousePosition 而不是 Event.mousePosition】
        ///   面板的位移是拿 GUI.matrix 做的，而"矩阵里的 Event.mousePosition 到底是
        ///   矩阵内坐标还是屏幕坐标"这件事依赖 Unity 内部的 clip/matrix 处理，
        ///   一旦猜错，面板拖走之后就再也按不中它（差一个 offset）。
        ///   这里换成一条没有歧义的路：**几何一律用 Input.mousePosition 换算的屏幕坐标**
        ///   （左上原点），和面板的屏幕矩形比；事件对象只回答另一个问题 ——
        ///   "按下的这一下有没有被别的控件吃掉"（被按钮 Use 掉时 type 会变成 Used）。
        ///   屏幕坐标和"面板画在哪"是同一套数，矩阵怎么变都不会算错。
        /// </summary>
        private void HandlePanelDrag(PanelDrag d)
        {
            Rect rect = d.lastRect;   // PanelFrameEnd 刚记下的那一份

            Event e = Event.current;
            if (e == null) return;

            if (!d.dragging)
            {
                if (e.type == EventType.MouseDown && e.button == 0 &&
                    ScreenRect(rect, d.offset).Contains(ScreenMouse()))
                {
                    d.dragging = true;
                    e.Use();
                }
                return;
            }

            if (e.type == EventType.MouseDrag && e.button == 0)
            {
                d.offset = ClampPanelOffset(d.offset + e.delta, rect);
                e.Use();
            }
            else if (e.type == EventType.MouseUp)
            {
                d.dragging = false;
                e.Use();
            }
        }

        /// <summary>
        /// 把位移夹住，保证面板至少有 <see cref="PanelKeepOnScreen"/> 像素留在屏幕里。
        ///
        /// 【为什么是"留 40 像素"而不是"完全不许出屏"】面板比窗口还大的时候
        ///   （小窗口 + 报告面板）"完全不出屏"根本无解，硬夹会把面板弹回中间、
        ///   拖起来像卡住。留 40 像素的意思很明确：**任何情况下都还能抓住它拖回来**。
        /// </summary>
        private static Vector2 ClampPanelOffset(Vector2 offset, Rect rect)
        {
            float minX = PanelKeepOnScreen - rect.width  - rect.x;
            float maxX = Screen.width  - PanelKeepOnScreen - rect.x;
            float minY = PanelKeepOnScreen - rect.height - rect.y;
            float maxY = Screen.height - PanelKeepOnScreen - rect.y;

            // 面板比屏幕还宽/还高时 min 会跑到 max 后面去，夹取就没有意义了 ——
            // 取中点（= 让它居中），至少不会左右乱跳。
            if (minX > maxX) { float mid = (minX + maxX) * 0.5f; minX = mid; maxX = mid; }
            if (minY > maxY) { float mid = (minY + maxY) * 0.5f; minY = mid; maxY = mid; }

            offset.x = Mathf.Clamp(offset.x, minX, maxX);
            offset.y = Mathf.Clamp(offset.y, minY, maxY);
            return offset;
        }

        /// <summary>把面板这一帧的屏幕矩形记下来（UpdatePanelPickBlock 要用）。</summary>
        private static void RecordPanelRect(List<Rect> into, Rect rect, Vector2 offset)
        {
            into.Add(ScreenRect(rect, offset));
        }

        /// <summary>
        /// 鼠标是不是压在 v2.1 面板上 —— **给拾取那一路用的钩子**（TableInteraction 现在还没读它）。
        ///
        /// 【为什么会有点击穿透】卡牌拾取走的是 `Input.mousePosition` + 全场景
        ///   `Physics.Raycast`（见 TableInteraction.RaycastCard），它**完全不看 IMGUI**：
        ///   在面板上按一下，底下的牌照样会被选中、被拿起，甚至把手牌法术直接打出去。
        ///   正解是拾取那一路开头加一行 `if (TableHud.PointerOverPanel) return;`，
        ///   但那个文件不归这里改 —— 所以我也做了一层自带的兜底，见 UpdatePanelPickBlock。
        /// </summary>
        public static bool PointerOverPanel { get; private set; }

        /// <summary>
        /// 鼠标压在 v2.1 面板上时，把桌面卡牌的碰撞体临时关掉 —— 兜底防"点击穿透"。
        ///
        /// 【为什么是关碰撞体，而不是在 IMGUI 里拦事件】拾取读的是 `Input` + `Physics.Raycast`，
        ///   和 IMGUI 的事件系统没有关系，Event.Use() 拦不住它；全场景射线里唯一能
        ///   "说话"的东西就是碰撞体本身（JuicerRig 那边也写过：机器艺术件故意不带碰撞体，
        ///   否则会挡住拾取）。这里只是**临时**关掉，鼠标一离开面板就原样打开。
        ///
        /// 【为什么用上一帧的面板矩形就够】按下鼠标那一帧，鼠标早就已经在面板上了
        ///   （不然怎么会点到面板），所以碰撞体在前一帧就关掉了，这一下自然不会落到牌上。
        ///   反过来"鼠标刚进面板的同一帧就按下"人做不到。
        ///
        /// 【只动自己关过的那些】关过谁记在 pickBlockedColliders 里，恢复时逐个打开；
        ///   被吞掉的卡（ConsumeInto 自己关了碰撞体）不抢着开，免得把它的动画搅了。
        ///
        /// 【旧流程的面板不在这个名单里】它们还是老样子（那几块用户没让动）——
        ///   要一起治的话，把它们的矩形也 RecordPanelRect 进来就行。
        /// </summary>
        private void UpdatePanelPickBlock()
        {
            Vector2 m = ScreenMouse();   // 和拖动命中判定用同一套屏幕坐标

            bool over = false;
            for (int i = 0; i < v21PanelRects.Count; i++)
            {
                if (v21PanelRects[i].Contains(m)) { over = true; break; }
            }

            PointerOverPanel = over;

            if (over == pickBlocked) return;   // 状态没变就别去翻碰撞体（每帧翻一遍纯浪费）
            pickBlocked = over;

            if (over)
            {
                PlayCard[] cards = Object.FindObjectsOfType<PlayCard>();
                for (int i = 0; i < cards.Length; i++)
                {
                    if (cards[i] == null) continue;

                    Collider[] cols = cards[i].GetComponentsInChildren<Collider>();
                    for (int c = 0; c < cols.Length; c++)
                    {
                        if (cols[c] == null || !cols[c].enabled) continue;   // 本来就关着的不是我们关的
                        cols[c].enabled = false;
                        pickBlockedColliders.Add(cols[c]);
                    }
                }
            }
            else
            {
                for (int i = 0; i < pickBlockedColliders.Count; i++)
                {
                    Collider col = pickBlockedColliders[i];
                    if (col == null) continue;   // 卡已经被销毁/吞掉了

                    PlayCard card = col.GetComponentInParent<PlayCard>();
                    if (card != null && card.IsConsuming) continue;

                    col.enabled = true;
                }
                pickBlockedColliders.Clear();
            }
        }

        private void OnGUI()
        {
            EnsureStyles();
            if (setup == null || interaction == null) return;

            // 这一趟画了哪些 v2.1 面板（拖动后的真实屏幕矩形）——每趟重记，
            // Update 里拿最后一次的结果判断鼠标压在哪（见 UpdatePanelPickBlock）
            v21PanelRects.Clear();

            // ── 图鉴开着的时候，HUD 整帧让开 ──
            //   图鉴是"翻开查资料"的一整屏，而它的 OnGUI 跑在 TableHud **前面**
            //   （实测：回合条、卡牌信息条会浮在图鉴上面，左边那排卡面被挡掉一半）。
            //   抢执行顺序没用（[DefaultExecutionOrder] 影响不到 OnGUI），
            //   所以改成这里让位：开着图鉴时这一层什么都不画。
            if (browser != null && browser.IsOpen) return;

            DrawViewButtons();

            // 开场 / 开局准备 / 正式回合是先后关系，三种信息栏不会同时出现
            if (turnLoop != null && turnLoop.IsTitle)           DrawTitleOverlay();
            else if (turnLoop != null && turnLoop.IsLevelSelect) DrawLevelSelectPanel();
            else if (turnLoop != null && turnLoop.IsPreparing)   DrawChoicePanel();
            else
            {
                DrawHints();
                DrawTurnPanel();
            }

            DrawPhaseOverlay();

            // 弹窗盖在最上面。设置优先于暂停 —— 设置是从暂停里开出来的。
            if (turnLoop != null && turnLoop.settingsOpen)    DrawSettingsPanel();
            else if (turnLoop != null && turnLoop.paused)     DrawPausePanel();

            // 规则解析报告盖在弹窗之上（它是"查资料"，任何时候都该能翻）
            if (rulesReportOpen) DrawRulesReportPanel();

            // 关卡窗口：进关卡界面自动弹，Esc 菜单里也能随时开关
            TrackLevelWindow();
            if (levelWindowOpen) DrawLevelWindow();

            // 检视面板和划过信息条是同一个位置，二选一
            if (interaction.Inspected != null) DrawInspectPanel();
            else                               DrawCardInfo();
        }

        // ══════════════════════════════════════════════════════════════
        //  关卡窗口（B）
        //
        //  【用户要的是什么】最早是"最好单独开一个窗口给我显示关卡" —— 桌上那排 3D 关卡卡
        //   离得远、字小、还占着桌面；窗口里一屏能看全：名字 / 目标分 / 状态，点一行就选中。
        //   后来用户直接拍板："不要关卡手牌了，就放一个 ui 就行" —— 于是 v2.1 里
        //   这个窗口从"和 3D 卡并存的第二条路"变成**唯一**的选关方式
        //   （TableSettings.LevelCardsOnTable；旧流程那排卡一行没动）。
        //
        //  【和现有确认流程的关系】点一行 = `choiceRig.SelectDeck(i)`（和从前点 3D 卡
        //   完全同一个入口，所以"当前关卡"的高亮、已选文案都跟着走），确认键 =
        //   `turnLoop.ConfirmLevelSelect()`（原流程）。没有另开一条捷径 ——
        //   v2.1 只是**不建那排卡**，选择和确认这两件事一条都没绕过。
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 进入关卡界面（LevelSelect）时自动把窗口打开；离开就留着状态不强制关
        /// （玩家可能正拖着它看）。另外顺手记一下每关过没过。
        /// </summary>
        private void TrackLevelWindow()
        {
            if (turnLoop == null) return;

            RecordLevelOutcome();

            bool nowLevelSelect = turnLoop.IsLevelSelect;
            if (nowLevelSelect && !wasLevelSelect)
            {
                levelWindowOpen = true;
                levelWindowPick = turnLoop.levelIndex;   // 默认停在当前关卡上
            }
            else if (!nowLevelSelect && wasLevelSelect)
            {
                // 选完关就自动收起来 —— 这一屏是"翻关卡"，进了牌桌再挡着牌就碍事了。
                // （玩家自己在中途从 Esc 菜单开出来的那份不受影响：没有阶段切换就不走这里。）
                // 上一版只"自动开、手动关"，实机截图里开局后它还杵在屏幕中间挡着桌子。
                levelWindowOpen = false;
            }
            wasLevelSelect = nowLevelSelect;
        }

        /// <summary>
        /// 把"这一关过没过"记进本次运行的账本。
        ///
        /// 【为什么要自己记】`Level.state` 是**当前这一关**的运行状态，
        ///   `NextLevel()` 一换关就换成新对象了 —— 窗口要显示"已通过 / 未通过"
        ///   就必须在它还活着的时候抄一份（只在内存里，重启即忘，够用）。
        /// </summary>
        private void RecordLevelOutcome()
        {
            if (turnLoop == null || turnLoop.level == null) return;
            if (turnLoop.levelIndex < 0) return;

            GameJam.Data.LevelState st = turnLoop.level.state;
            if (st == GameJam.Data.LevelState.Cleared)        levelCleared[turnLoop.levelIndex] = true;
            else if (st == GameJam.Data.LevelState.Failed)    levelCleared[turnLoop.levelIndex] = false;
        }

        /// <summary>关卡窗口：一屏列出全部关卡，点一行选中、按确认进入。</summary>
        private void DrawLevelWindow()
        {
            if (turnLoop == null || turnLoop.levels == null || turnLoop.levels.Count == 0) return;

            // 尺寸照报告面板那一套：跟着屏幕走，小窗口收缩（列表用滚动区，永远放得下）
            float w = Mathf.Max(360f, Mathf.Min(620f, Screen.width - 32f));
            float h = Mathf.Max(220f, Mathf.Min(560f, Screen.height - 32f));
            Rect box = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);

            Matrix4x4 prevMatrix = GUI.matrix;
            GUI.matrix = OffsetMatrix(levelWindowDrag.offset) * prevMatrix;

            GUI.Box(box, GUIContent.none, panelBoxGlass);
            GUILayout.BeginArea(new Rect(box.x + 14f, box.y + 12f, w - 28f, h - 24f));

            GUILayout.BeginHorizontal();
            GUILayout.Label("关　卡", h1Panel, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("关闭", btn, GUILayout.Width(72f), GUILayout.Height(28f)))
            {
                GUI.FocusControl(null);
                levelWindowOpen = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.Label(TableSettings.LevelCardsOnTable
                                ? "点一行选中它，再按下面的确认键 —— 和点桌上那排关卡卡是同一条路。"
                                : "点一行选中它，再按下面的确认键 —— 关卡就在这个窗口里选。",
                            dimPanel);
            GUILayout.Space(4f);

            levelWindowScroll = GUILayout.BeginScrollView(levelWindowScroll, false, false,
                                                          GUI.skin.horizontalScrollbar,
                                                          GUI.skin.verticalScrollbar,
                                                          scrollGlass,
                                                          GUILayout.ExpandHeight(true));

            for (int i = 0; i < turnLoop.levels.Count; i++)
            {
                LevelData lv = turnLoop.levels[i];
                if (lv == null) continue;

                int target = TableSettings.UseRulesV21 && TableSettings.V21TargetScore > 0
                    ? TableSettings.V21TargetScore
                    : lv.targetScore;

                string row = lv.name + "　　目标分 " + target + "　　" + LevelStatusText(i);

                bool picked = (i == levelWindowPick);
                if (GUILayout.Button(row, picked ? btnOn : btn, GUILayout.ExpandWidth(true), GUILayout.Height(30f)))
                {
                    GUI.FocusControl(null);
                    PickLevelInWindow(i);
                }
            }

            GUILayout.Space(14f);
            GUILayout.EndScrollView();

            // ── 确认 ──
            //   必须在关卡界面（LevelSelect）才能确认 —— 打到一半点"进入这一关"
            //   等于中途换关（规则状态会串）。按钮灰着的时候一定要写清为什么，
            //   不然玩家只会以为坏了。
            bool canConfirm = turnLoop.IsLevelSelect && levelWindowPick >= 0;

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();

            bool oldEnabled = GUI.enabled;
            GUI.enabled = canConfirm;
            if (GUILayout.Button("进入这一关", btn, GUILayout.ExpandWidth(true), GUILayout.Height(38f)))
            {
                GUI.FocusControl(null);
                ConfirmLevelInWindow();
            }
            GUI.enabled = oldEnabled;
            GUILayout.EndHorizontal();

            if (!canConfirm)
                GUILayout.Label(turnLoop.IsLevelSelect ? "先在上面点一行选一关" 
                                                       : "现在不在关卡界面 —— 窗口只是查看；"
                                                         + "回关卡界面（Esc → 退出关卡，或打完这一关）才能进关",
                                dimPanel);

            GUILayout.EndArea();

            EndPanelFrame(levelWindowDrag, box, prevMatrix);
        }

        /// <summary>
        /// 窗口里点了一行：选中它，并让 rig 里的选中态跟着走。
        ///
        /// 【为什么是 public】自动试玩探针要验"点一行能选中"这件事，
        ///   而它点不了 IMGUI 的按钮。让它调**同一个方法**（而不是自己去改
        ///   levelWindowPick / SelectDeck），探针验的就真是按钮走的那条路 ——
        ///   这个工程在这上面踩过坑：探针绕开鼠标那条路，界面建好了却不响应都测不出来。
        /// </summary>
        public void PickLevelInWindow(int i)
        {
            levelWindowPick = i;

            // ★ 只在关卡界面同步给 rig：其它阶段那排卡是**牌组卡**，
            //   这时候调 SelectDeck 等于偷偷改掉牌组选择（界面上看不出来，很坑）。
            if (turnLoop.IsLevelSelect && turnLoop.choiceRig != null)
                turnLoop.choiceRig.SelectDeck(i);
        }

        /// <summary>
        /// 窗口底部「进入这一关」按下去要做的事 —— 按钮和探针共用这一个入口。
        ///
        /// 【为什么确认成功就把窗口收起来】这一屏是"翻关卡"，
        ///   进了牌桌再挡着牌就碍事了（实机截图里它正好杵在桌子中间）。
        /// </summary>
        public void ConfirmLevelInWindow()
        {
            if (turnLoop == null) return;

            turnLoop.ConfirmLevelSelect();
            if (!turnLoop.IsLevelSelect) levelWindowOpen = false;
        }

        /// <summary>关卡窗口开着没有（探针用 —— "进关卡界面自动弹窗"这件事得能验证）。</summary>
        public bool LevelWindowOpen { get { return levelWindowOpen; } }

        /// <summary>关卡窗口里当前点中的是第几关（-1 = 还没点；探针用）。</summary>
        public int LevelWindowPick { get { return levelWindowPick; } }

        /// <summary>一行末尾的状态文字。</summary>
        private string LevelStatusText(int i)
        {
            if (turnLoop.levelIndex == i) return "▸ 当前关卡";

            bool cleared;
            if (levelCleared.TryGetValue(i, out cleared)) return cleared ? "已通过" : "未通过";

            return "未打过";
        }

        // ══════════════════════════════════════════════════════════════
        //  暂停菜单 / 设置
        // ══════════════════════════════════════════════════════════════

        private void DrawPausePanel()
        {
            // 312 → 396：多了「关卡」和「卡牌图鉴」两项，面板跟着长高。
            // ★ 高度必须 = 66（标题）+ 6×(40+6)（六个按钮）+ 两条说明的 44 ——
            //   第一版只加到 358，最后一个「退出游戏」正好压在下面两行说明上。
            const float w = 400f, h = 396f;
            Rect r = CenterBox(w, h);
            GUI.Box(r, GUIContent.none, panelBox);

            GUI.Label(new Rect(r.x + 22f, r.y + 16f, w - 44f, 34f), "暂　停", h1Panel);

            float by = r.y + 66f;
            const float bh = 40f;
            const float gap = 6f;

            if (GUI.Button(new Rect(r.x + 22f, by, w - 44f, bh), "继续游戏", btn))
            {
                GUI.FocusControl(null);
                turnLoop.paused = false;
            }
            by += bh + gap;

            if (GUI.Button(new Rect(r.x + 22f, by, w - 44f, bh), "设　　置", btn))
            {
                GUI.FocusControl(null);
                turnLoop.settingsOpen = true;
            }
            by += bh + gap;

            // ── 关卡窗口 / 卡牌图鉴 ──
            //   用户原话："最好单独开一个窗口给我显示关卡"、图鉴"入口做明显"。
            //   两项都做成"从这里开"，不再只靠一个没人知道的快捷键。
            if (GUI.Button(new Rect(r.x + 22f, by, w - 44f, bh),
                           levelWindowOpen ? "关卡（已打开）" : "关　　卡", btn))
            {
                GUI.FocusControl(null);
                turnLoop.paused = false;      // 让开位置：窗口是独立一层，别和暂停菜单叠着
                levelWindowOpen = !levelWindowOpen;
                if (levelWindowOpen) levelWindowPick = turnLoop.levelIndex;
            }
            by += bh + gap;

            if (GUI.Button(new Rect(r.x + 22f, by, w - 44f, bh), "卡牌图鉴（F1）", btn))
            {
                GUI.FocusControl(null);
                turnLoop.paused = false;
                if (browser != null) browser.Toggle();
            }
            by += bh + gap;

            if (GUI.Button(new Rect(r.x + 22f, by, w - 44f, bh), "退出关卡", btn))
            {
                GUI.FocusControl(null);
                turnLoop.ExitLevel();
            }
            by += bh + gap;

            if (GUI.Button(new Rect(r.x + 22f, by, w - 44f, bh), "退出游戏", btn))
            {
                GUI.FocusControl(null);
                turnLoop.QuitGame();
            }

            GUI.Label(new Rect(r.x + 22f, r.y + h - 44f, w - 44f, 20f),
                      "退出关卡：放弃这一把，回到开场（不算失败）", dimPanel);
            GUI.Label(new Rect(r.x + 22f, r.y + h - 24f, w - 44f, 20f),
                      "Esc 也可以直接继续", dimPanel);
        }

        /// <summary>
        /// 设置面板 —— 这一版是壳子。
        ///
        /// 结构先立起来（有哪几项、长什么样、从哪儿进），
        /// 但里面挂的三项都是**真能改东西**的，不是摆着好看：
        /// 转头灵敏度接 TableInteraction，操作提示和调试信息接 DrawHints。
        /// 音量、画质、存档这些等真需要了再往里加。
        /// </summary>
        private void DrawSettingsPanel()
        {
            const float w = 470f, h = 336f;
            Rect r = CenterBox(w, h);
            GUI.Box(r, GUIContent.none, panelBox);

            GUI.Label(new Rect(r.x + 22f, r.y + 16f, w - 44f, 34f), "设　置", h1Panel);
            GUI.Label(new Rect(r.x + 22f, r.y + 52f, w - 44f, 22f),
                      "（壳子：结构先立起来，挂上去的值都是生效的）", dimPanel);

            float y = r.y + 88f;

            // ── 规则模式开关 ─────────────────────────────────────────
            //   ★ 只改"下一关用哪套"，不做中途切换：两套玩法的状态完全不一样
            //     （v2.1 是 BladeState + 桌面素材，旧流程是 TurnState + 杯内粒子），
            //     打到一半换引擎只会留下半套脏状态 —— 那比"要多点一次重开"糟糕得多。
            GUI.Label(new Rect(r.x + 22f, y, 210f, 26f), "规则模式", bodyPanel);
            if (GUI.Button(new Rect(r.x + 232f, y, 200f, 28f),
                           TableSettings.UseRulesV21 ? "v2.1 规则（默认）" : "旧流程",
                           btn))
            {
                GUI.FocusControl(null);
                TableSettings.UseRulesV21 = !TableSettings.UseRulesV21;
                turnLoop.notice = "规则模式已切成「" + (TableSettings.UseRulesV21 ? "v2.1" : "旧流程") +
                                  "」—— 退出关卡重进（或重开本关）才生效";
            }
            y += 32f;

            GUI.Label(new Rect(r.x + 22f, y, w - 44f, 20f),
                      "两套玩法状态不通用，所以只对**下一关**生效；中途不换引擎。", dimPanel);
            y += 26f;

            GUI.Label(new Rect(r.x + 22f, y, 210f, 26f),
                      "转头灵敏度　" + TableSettings.LookSensitivity.ToString("0.0"), bodyPanel);

            TableSettings.LookSensitivity =
                GUI.HorizontalSlider(new Rect(r.x + 232f, y + 8f, w - 258f, 20f),
                                     TableSettings.LookSensitivity, 0.5f, 8f);
            y += 36f;

            GUI.Label(new Rect(r.x + 22f, y, 210f, 26f), "操作提示", bodyPanel);
            if (GUI.Button(new Rect(r.x + 232f, y, 110f, 28f),
                           TableSettings.ShowHints ? "开" : "关", btn))
            {
                GUI.FocusControl(null);
                TableSettings.ShowHints = !TableSettings.ShowHints;
            }
            y += 36f;

            GUI.Label(new Rect(r.x + 22f, y, 210f, 26f), "调试信息", bodyPanel);
            if (GUI.Button(new Rect(r.x + 232f, y, 110f, 28f),
                           TableSettings.ShowDebugInfo ? "开" : "关", btn))
            {
                GUI.FocusControl(null);
                TableSettings.ShowDebugInfo = !TableSettings.ShowDebugInfo;
            }
            y += 36f;

            // ── 占位数值开关 ─────────────────────────────────────────
            //   卡表里 h/d/v 都是 0 的卡（数值还没定）要不要兜一套临时值。
            //   关掉之后那些卡的 H 就是 0 —— 刀片选它会一进关卡就爆刀。
            //   所以这个开关必须有，而且默认开；HUD 上会标明用的是占位数值。
            GUI.Label(new Rect(r.x + 22f, y, 240f, 26f), "卡表没数值时用占位值", bodyPanel);
            if (GUI.Button(new Rect(r.x + 232f, y, 110f, 28f),
                           TableSettings.UsePlaceholderCardValues ? "开" : "关", btn))
            {
                GUI.FocusControl(null);
                TableSettings.UsePlaceholderCardValues = !TableSettings.UsePlaceholderCardValues;
            }
            y += 34f;

            if (GUI.Button(new Rect(r.x + 22f, y, 130f, 32f), "恢复默认", btn))
            {
                GUI.FocusControl(null);
                TableSettings.ResetToDefault();
            }

            if (GUI.Button(new Rect(r.x + w - 152f, y, 130f, 32f), "关　闭", btn))
            {
                GUI.FocusControl(null);
                turnLoop.settingsOpen = false;
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  关卡界面
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 关卡界面：v2.1 = 桌上一张卡都没有，选关全在关卡窗口里做；
        /// 旧流程 = 桌上一排关卡卡，点一张再确认。
        /// 牌组 / 刀片那两个环节照旧是"桌上一排大卡点一张"，文案各归各的。
        /// </summary>
        private void DrawLevelSelectPanel()
        {
            if (turnLoop == null) return;

            const float w = 660f;
            const float h = 168f;
            float x = (Screen.width - w) * 0.5f;
            const float y = 14f;

            GUI.Box(new Rect(x, y, w, h), GUIContent.none, panelBox);

            GUI.Label(new Rect(x + 18f, y + 8f, w - 36f, 30f), "选择关卡", h1Panel);

            int sel = turnLoop.choiceRig != null ? turnLoop.choiceRig.DeckSelected : -1;

            string picked = "还没选";
            if (sel >= 0 && sel < turnLoop.levels.Count && turnLoop.levels[sel] != null)
                picked = turnLoop.levels[sel].name;

            GUI.Label(new Rect(x + 18f, y + 44f, w - 240f, 24f), "已选：" + picked, bodyPanel);

            // ★ 指路的话必须和桌面上真实存在的东西对上：卡已经没有了还写"点桌上的一张卡"，
            //   玩家只会去一张空桌子上找（见 TableSettings.LevelCardsOnTable）。
            GUI.Label(new Rect(x + 18f, y + 70f, w - 240f, 44f),
                      TableSettings.LevelCardsOnTable
                          ? "点桌上的一张关卡卡选中它，再按确认。\n金色那张是你现在所在的关卡。"
                          : "在关卡窗口里点一行选中它，再按确认。\n带「▸ 当前关卡」的那一行是你现在所在的关卡。",
                      dimPanel);

            if (!string.IsNullOrEmpty(turnLoop.notice))
                GUI.Label(new Rect(x + 18f, y + 120f, w - 240f, 24f), turnLoop.notice, dimPanel);

            bool old = GUI.enabled;
            GUI.enabled = (sel >= 0);

            if (GUI.Button(new Rect(x + w - 200f, y + 116f, 182f, 38f), "进入这一关", btn))
            {
                GUI.FocusControl(null);
                turnLoop.ConfirmLevelSelect();
            }

            GUI.enabled = old;
        }

        // ══════════════════════════════════════════════════════════════
        //  开场界面
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 开场只画标题和一行提示 —— 菜单项本身是桌上的立体物件
        /// （书、木牌），由 TableTitleRig 搭出来、玩家直接用鼠标点。
        ///
        /// 这跟 2D 那版的分工是一样的，只是那边整幅画面都是画出来的，
        /// 这边把"物件"那部分交给了 3D。
        /// </summary>
        private void DrawTitleOverlay()
        {
            if (turnLoop == null || turnLoop.titleRig == null) return;

            GUI.Label(new Rect(0f, Screen.height * 0.075f, Screen.width, 74f),
                      "破 壁 机 计 划", titleBig);

            GUI.Label(new Rect(0f, Screen.height * 0.075f + 72f, Screen.width, 30f),
                      "一台机器，一个人，和一桌不肯认输的材料", titleSub);

            GUI.Label(new Rect(0f, Screen.height - 96f, Screen.width, 30f),
                      turnLoop.titleRig.IsStarting
                          ? "启 动 中 …"
                          : HintForTitle(turnLoop.titleRig.Hovered), titleHint);
        }

        private static string HintForTitle(string id)
        {
            switch (id)
            {
                case TableTitleRig.IdNew:      return "翻开它，开始这一局";
                case TableTitleRig.IdJuicer:   return "按下开关，启动破壁机";
                case TableTitleRig.IdContinue: return "还没有存档";
                case TableTitleRig.IdSettings: return "设置还没做";
                case TableTitleRig.IdQuit:     return "离开这张桌子";

                // 没悬停时把能点的都列出来。
                //
                // 原来这里只写了"桌上那本书是「新游戏」"—— 结果桌上另外三块木牌
                // 没人找得到。把菜单做成物件是没有"这是菜单"的视觉提示的，
                // 深色木头趴在深色桌面上尤其不明显，所以至少要用文字兜住。
                default:
                    return "桌上能点的：书（新游戏）　继续　设置　退出　榨汁机（启动）";
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  开局准备：三选一牌组 / 选刀片
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 牌组和刀片两个环节的信息栏。位置和回合信息栏相同。
        ///
        /// 两段共用一个面板：它们的结构完全一样 ——
        /// 标题、几行状态、右下角一个确认键，只是内容不同。
        /// </summary>
        private void DrawChoicePanel()
        {
            if (turnLoop == null) return;

            // ── v2.1 分流 ──────────────────────────────────────────────
            //   v2.1 的"选刀片"不是从配置里挑一把，而是**从初始手牌里点一张素材**
            //   （正文 §2.5）。旧那一屏显示的是 turn.BladeName() / BladeAttrLine()，
            //   在 v2.1 里那个 blade 是 null，会显示"（未选择）" —— 看不出规则。
            if (turnLoop.V21 && turnLoop.phase == TablePhase.BladePick) { DrawBladePickV21(); return; }

            bool deckPhase = (turnLoop.phase == TablePhase.DeckPick);
            Choice c = turnLoop.CurrentChoice;

            TextDto texts = GameConfig.Texts();

            const float w = 660f;
            const float h = 168f;
            float x = (Screen.width - w) * 0.5f;
            const float y = 14f;

            // 内容区宽度要避开右下角的确认键，否则长句会钻到按钮底下
            const float textW = w - 250f;

            GUI.Box(new Rect(x, y, w, h), GUIContent.none, panelBox);

            string title = (c != null && !string.IsNullOrEmpty(c.prompt))
                ? c.prompt
                : (deckPhase ? "选择你的初始牌组" : "选择你的刀片");

            GUI.Label(new Rect(x + 18f, y + 8f, w - 36f, 30f), title, h1Panel);

            float ty = y + 44f;

            if (deckPhase)
            {
                int sel = turnLoop.choiceRig != null ? turnLoop.choiceRig.DeckSelected : -1;

                string picked = "还没选";
                if (sel >= 0 && c != null && sel < c.options.Count && c.options[sel] != null)
                    picked = c.options[sel].title;

                GUI.Label(new Rect(x + 18f, ty, textW, 24f), "已选牌组：" + picked, bodyPanel);
                ty += 26f;

                GUI.Label(new Rect(x + 18f, ty, textW, 24f),
                          c != null && !string.IsNullOrEmpty(c.hint) ? c.hint
                          : "在桌上点一张牌组卡选中它，再按确认。", dimPanel);
            }
            else
            {
                GUI.Label(new Rect(x + 18f, ty, textW, 24f),
                          "当前刀片：" + turnLoop.turn.BladeName(), bodyPanel);
                ty += 24f;

                GUI.Label(new Rect(x + 18f, ty, textW, 24f),
                          "　" + turnLoop.turn.BladeAttrLine(), bodyPanel);
                ty += 26f;

                GUI.Label(new Rect(x + 18f, ty, textW, 24f),
                          c != null && !string.IsNullOrEmpty(c.hint) ? c.hint
                          : "点手牌里的食材即与当前刀片交换。", dimPanel);
            }

            ty += 26f;

            if (!string.IsNullOrEmpty(turnLoop.notice))
                GUI.Label(new Rect(x + 18f, ty, textW, 24f), turnLoop.notice, dimPanel);

            string label = deckPhase
                ? (texts != null && !string.IsNullOrEmpty(texts.deckConfirm) ? texts.deckConfirm : "确认选择该卡组")
                : (texts != null && !string.IsNullOrEmpty(texts.bladeConfirm) ? texts.bladeConfirm : "确认刀片，进入关卡");

            bool canConfirm = deckPhase
                ? (turnLoop.choiceRig != null && turnLoop.choiceRig.DeckSelected >= 0)
                : true;

            bool oldEnabled = GUI.enabled;
            GUI.enabled = canConfirm;

            if (GUI.Button(new Rect(x + w - 210f, y + 112f, 192f, 40f), label, btn))
            {
                GUI.FocusControl(null);
                if (deckPhase) turnLoop.ConfirmDeckPick();
                else           turnLoop.ConfirmBladePick();
            }

            GUI.enabled = oldEnabled;
        }

        /// <summary>
        /// v2.1 的选刀片核心面板。
        ///
        /// 【为什么必须单独一屏】这一步是 v2.1 特有的一条规则（正文 §2.5）：
        ///   玩家从**初始手牌**里点一张素材当核心，该卡的 H → 刀片初始 H、
        ///   V → 刀片初始 V，而且这张卡**移出手牌、本关不再参与出牌**。
        ///   旧的选刀片面版显示的是配置里那把刀片的属性，在 v2.1 里根本没有那回事。
        ///
        /// 手牌张数和"点哪张"都写出来 —— 不写的话玩家不知道要干什么，
        /// 只会一直按确认，然后拿到一张默认的第一张牌。
        ///
        /// 【★ 布局这一版为什么换掉】原来是写死 660×168 + 每行 24 像素：
        ///   第三行"点桌上手牌里的一张素材即改选…"在窄窗口下折成两行，
        ///   第二行被 24 像素的矩形裁掉；面板宽 660 在小窗口下还会横出屏幕。
        ///   现在宽按屏幕收缩、高按内容长（BeginCenterPanel 的说明）。
        /// </summary>
        private void DrawBladePickV21()
        {
            TableRulesV21 r = turnLoop.rulesV21;
            if (r == null) return;

            MaterialCard cand = turnLoop.bladeCoreCard != null
                ? turnLoop.bladeCoreCard
                : r.CoreCandidate();

            BeginCenterPanel(660f, bladePanelDrag);

            GUILayout.Label("选择你的刀片核心", h1Panel);

            GUILayout.Label("当前候选：" + (cand != null
                                       ? cand.name + "（H " + cand.H + " · V " + cand.V + "）"
                                       : "（没有可当核心的素材）"),
                            bodyPanel);

            GUILayout.Label("初始手牌：素材 " + r.hand.Count + " 张 + 法术 " + r.handSpells.Count + " 张"
                            + "（正文 §2.6）", bodyPanel);

            GUILayout.Label("点桌上手牌里的一张素材即改选。核心卡的 H/V 就是刀片的初始 H/V，该卡移出手牌。",
                            dimPanel);

            if (!string.IsNullOrEmpty(turnLoop.notice)) GUILayout.Label(turnLoop.notice, dimPanel);

            GUILayout.Space(6f);

            bool canConfirm = cand != null;

            bool oldEnabled = GUI.enabled;
            GUI.enabled = canConfirm;

            // 确认键靠右（和旧版一样在右下角），但不再写死横坐标：
            // 水平 FlexibleSpace 把它顶到右沿，面板多宽都贴边。
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("确认刀片，进入关卡", btn,
                                 GUILayout.Width(PanelButtonW(240f)), GUILayout.Height(40f)))
            {
                GUI.FocusControl(null);
                turnLoop.ConfirmBladePick();
            }
            GUILayout.EndHorizontal();

            GUI.enabled = oldEnabled;

            EndCenterPanel();
        }

        /// <summary>
        /// 屏幕正中开一块**按内容长高**的面板（内容摆完调 EndCenterPanel）。
        ///
        /// 【为什么不用 CenterBox + 写死高度】这几块 v2.1 面板的文字都会折行
        ///   （窗口越窄折得越多），写死高度必然裁字；而"先把每行文字量一遍高度
        ///   再画底"要把文字和样式维护两遍、还容易和 GUILayout 的实际排版差几像素。
        ///   GUILayout.BeginVertical(panelGroup) 的底是**内容排完之后**才按组矩形画的，
        ///   内容多高面板就多高 —— 两边都不用猜。
        ///
        /// 【上下留白怎么来的】外面套一层"占满屏幕的 Area + 上下两个 FlexibleSpace"，
        ///   内容矮的时候面板自然居中；内容比屏幕还高时两个留白双双收成 0，
        ///   面板从屏幕顶端开始排（不会像"居中"那样把上下两头都切掉）。
        ///
        /// 【可拖动】整块面板的位移走 `GUI.matrix`（见 OffsetMatrix）：
        ///   里面这套按内容长高的布局一行不用改，鼠标命中也会跟着矩阵走。
        ///   位移存在面板自己那份 <see cref="PanelDrag"/> 里，所以各面板互不影响。
        /// </summary>
        private void BeginCenterPanel(float designW, PanelDrag drag)
        {
            // 宽度跟着屏幕收缩：设计宽度是给大窗口的，小窗口下按屏幕减 32 ——
            // 少了这个 Min，面板右边会伸出屏幕，按钮看得见点不到。
            centerPanelW = Mathf.Max(280f, Mathf.Min(designW, Screen.width - 32f));

            centerPanelDrag = drag;
            centerPanelPrevMatrix = GUI.matrix;
            GUI.matrix = OffsetMatrix(drag.offset) * GUI.matrix;

            GUILayout.BeginArea(new Rect(0f, 0f, Screen.width, Screen.height));
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.BeginVertical(panelGroupGlass, GUILayout.Width(centerPanelW));
        }

        private void EndCenterPanel()
        {
            GUILayout.EndVertical();

            // 组矩形 = 面板这一帧画在哪。★ 必须在 EndArea **之前**取：
            //   EndArea 之后 GetLastRect 拿到的是那个占满屏幕的 Area，不是面板。
            //   （EndVertical 之后取是合法的：父组里最后一项就是刚关掉的这个组；
            //     反过来"刚 Begin 就取"才非法，那个坑在报告面板那边踩过。）
            Rect rect = GUILayoutUtility.GetLastRect();

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.EndArea();

            PanelFrameEnd(centerPanelDrag, rect);

            GUI.matrix = centerPanelPrevMatrix;
            centerPanelDrag = null;
        }

        /// <summary>
        /// 面板里那种"定宽按钮"的实际宽度：设计宽度在小窗口下要跟着面板一起缩，
        /// 否则按钮比面板还宽，又被顶出去（缩到 72 就不再缩了，再窄就不是按钮了）。
        /// </summary>
        private float PanelButtonW(float designW)
        {
            return Mathf.Max(72f, Mathf.Min(designW, centerPanelW - 26f));
        }

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
        //
        // 【为什么这块不能再钉在屏幕左下角】手牌那一排就在画面底部（取景是按相机视锥拟合出来的，
        //   见 TableSetup.ReframeBoardView），两者本来落在同一条带子上：实测 1470×1167 下
        //   提示压住左边两三张牌、1600×900 下压住牌的上沿。用户拍板：**挪提示、不再退取景**。
        //   所以现在改成：先把手牌那一排在屏幕上的上沿**量出来**，整块提示抬到它上面去。
        //
        // 【为什么是"量"而不是"抬到某个固定高度"】手牌在屏幕上的高度随窗口比例变
        //   （手牌上沿：1470×1167 → 285px、1600×900 → 217px、竖屏 900×1600 → 601px），
        //   写死任何一个数都只对一种比例成立 —— 换一种比例手牌又被压住。
        private void DrawHints()
        {
            if (!TableSettings.ShowHints) { HintBlockScreenRect = new Rect(); return; }   // 设置里关掉 → 整块不画

            const float w = 480f;

            // v2.1 的操作方式完全不同（出牌不消耗行动、启动要点桌面素材、法术是点一下），
            // 提示必须跟着换 —— 写旧那一套会让玩家一直找不到"怎么启动"。
            if (turnLoop != null && turnLoop.V21)
            {
                DrawHintBlock(w, v21Hints);
                return;
            }

            DrawHintBlock(w, legacyHints);
        }

        /// <summary>
        /// v2.1 的操作提示。**顺序就是重要性** —— 放不下时从后往前截（见 <see cref="DrawHintBlock"/>）。
        /// </summary>
        private static readonly string[] v21Hints =
        {
            "点手牌里的素材 = 直接上桌（拖到「上桌位」再点「上　桌」也行）",
            // 两个槽的名字必须和桌面上的牌子一字不差（「附　魔 位」在左、「上　桌 位」在右）——
            // 这条原来写的是旧流程的"待上桌的卡"，而 v2.1 是落槽即生效，那句话在 v2.1 里没有对应物，
            // 玩家照着做只会白拖一趟（用户报的"附魔位不能放卡片"就是从这种错位里来的）。
            "拖「法术」到左边的「附　魔 位」= 直接附魔到刀片（不消耗行动机会）",
            "拖「素材」到右边的「上　桌 位」= 直接上桌（和点一下等价）",
            "点桌面上的素材 = 选它当「启动破壁机」的目标（会抬起来）",
            // 收回手牌这一行是用户拍板新加的功能，必须写在提示里 ——
            // 不写的话玩家只会得出"上桌就再也拿不回来了"，而这正是他提的那条。
            "把桌面上的素材往下拖到手牌那一片 = 收回手牌（只在本回合、还没启动过时能收）",
            "「启动破壁机」消耗 1 行动机会 + 1 点刀片 H",
            "每回合 5 次行动、每关 4 回合；本回合最后一次启动会献祭吞噬目标",
            "刀片 H 归零 = 爆刀，关卡结束、当前分数 ×2",
            "右键单击卡牌 → 查看完整数据　｜　右键拖动 → 转头",
            "F1 → 卡牌图鉴（素材 / 法术全在这）　｜　F2 → 卡牌规则解析报告　｜　F3 → 落点判定区（调试）",
            "Esc → 菜单（继续 / 设置 / 关卡 / 卡牌图鉴 / 退出关卡 / 退出游戏）",
        };

        /// <summary>
        /// 旧流程（先摆好、再按确认那套）的操作提示。★ 旧流程那句话**一个字都没改**，
        /// 只是摆法换成和 v2.1 共用同一个算式（旧流程的手牌也在画面底部，会撞上同一件事）。
        /// </summary>
        private static readonly string[] legacyHints =
        {
            "拖动卡牌放到桌面中间的投放区",
            "一次可以放多张 —— 确认时效果会叠加发动",
            "放好后按「确认投放」或回车 —— 牌会被吸进罐子",
            "拖回手牌 = 反悔，可以重新挑",
            "右键单击卡牌 → 查看完整数据（Esc 关闭）",
            "右键拖动 / 中键拖动 → 原地转头（活动范围 120° 锥）",
            "1 / 2 / 3 固定视角　　4 自由视角　　G 开关物理",
            "F1 → 卡牌图鉴　｜　Esc → 菜单（继续 / 设置 / 关卡 / 卡牌图鉴 / 返回开场 / 退出游戏）",
        };

        /// <summary>提示块与手牌那一排之间留的空（像素）—— 别让字贴着牌的边。</summary>
        private const float HintHandClearance = 12f;

        /// <summary>提示块离屏幕上沿至少留这么多：窗口再矮，也别把字顶到屏幕外面去。</summary>
        private const float HintTopKeep = 8f;

        /// <summary>
        /// 提示块这一帧**真正画在屏幕上的矩形**（左上原点）—— 探针用它量"有没有压到手牌"。
        /// 宽或高为 0 = 这一帧没画（设置里关掉提示的时候）。
        /// </summary>
        public Rect HintBlockScreenRect { get; private set; }

        /// <summary>v2.1 那几块面板这一帧的屏幕矩形（左上原点）—— 同上，给探针量"谁压住了手牌"。</summary>
        public List<Rect> PanelScreenRects { get { return v21PanelRects; } }

        /// <summary>检视窗口这一帧的屏幕矩形（左上原点）；没开着时是空矩形。同上，给探针量重叠。</summary>
        public Rect InspectScreenRect
        {
            get
            {
                if (interaction == null || interaction.Inspected == null || interaction.Inspected.card == null)
                    return new Rect();
                return inspectRect;
            }
        }

        /// <summary>
        /// 画一整块操作提示：**底边落在手牌那一排的上沿之上**，一行都不许压在牌上。
        ///
        /// 【放不下怎么办】按行截断（列表本身按重要性排），最少留一行 ——
        ///   "提示少几行"是小事，"牌被压住"是用户点名要修的事。
        /// </summary>
        private void DrawHintBlock(float w, string[] lines)
        {
            float bottomGap = HintBottomGap();                        // 底边离屏幕底多少像素
            float topLimit  = HintTopLimit();                         // 上边界（屏幕 y，自上而下）
            float room      = Screen.height - bottomGap - topLimit;   // 这一块能用多高

            // 第一行高 24、之后每行间距 20 —— 整块高 = 20n + 30
            int fit = Mathf.Clamp(Mathf.FloorToInt((room - 30f) / 20f), 1, lines.Length);
            float y0 = Screen.height - bottomGap - (20f * fit + 30f);

            // 记下这一帧画在哪（探针要拿它量"有没有压到手牌"）
            HintBlockScreenRect = new Rect(16f, y0, w, 20f * fit + 30f);

            GUI.Label(new Rect(16f, y0, w, 24f), lines[0], h1);
            for (int i = 1; i < fit; i++)
                GUI.Label(new Rect(16f, y0 + 28f + (i - 1) * 20f, w, 22f), lines[i], dim);

            if (TableSettings.ShowDebugInfo)
                GUI.Label(new Rect(16f, y0 + 28f + (fit - 1) * 20f, w, 22f),
                          "物理：" + (interaction.PhysicsOn ? "开（受重力）" : "关（脚本控制）")
                          + "　　视角：" + (setup != null && setup.rig != null && setup.rig.IsFreeLook
                                            ? "自由转头中" : "固定机位"), dim);
        }

        /// <summary>
        /// 提示块**底边**离屏幕底多少像素：手牌那一排在画面上就让到它上沿之上（+12px）；
        /// 手上没牌就照旧贴底 14px（老行为一个字没变）。
        /// </summary>
        private float HintBottomGap()
        {
            float handTop;
            if (!HandScreenTop(out handTop)) return 14f;

            // 上限取屏幕一半：万一量出来的上沿离谱（比如那张牌正被拖到半空），
            // 提示也不会被整块推出屏幕 —— 宁可少显示几行，也不让字跑到画面外。
            return Mathf.Min(handTop + HintHandClearance, Screen.height * 0.5f);
        }

        /// <summary>
        /// 提示块的**上边界**（屏幕 y，自上而下）：不许越过它。取"顶部回合面板的下沿 + 8"。
        ///
        /// 【为什么要躲回合面板】面板在顶部居中（宽 660）：竖屏 900 宽时它横跨 x 120~780，
        ///   和左下角这块提示在横向上是重叠的 —— 抬得太高就会糊在面板下沿上。
        ///   面板这一帧没画（lastRect 还是空矩形）时退回屏幕上沿 + 8。
        /// </summary>
        private float HintTopLimit()
        {
            Rect p = ScreenRect(turnPanelDrag.lastRect, turnPanelDrag.offset);
            if (p.height <= 0f) return HintTopKeep;
            return Mathf.Max(HintTopKeep, p.yMax + 8f);
        }

        /// <summary>
        /// 手牌那一排在屏幕上的**上沿**（自下而上的 y，像素）。手上没牌 / 拿不到相机时返回 false。
        ///
        /// 【为什么取"最高的那张的上沿"】提示块要躲开的是**整排**：只要有一张牌的上沿高过
        ///   提示块底边，那张牌就被压住 —— 所以取所有手牌里最高的那个上沿，不是平均、也不是最低。
        ///
        /// 【为什么用卡心 ± 半个占地】牌是平躺的、还带扇形偏航，屏幕上那个"上沿"就是远端那个角。
        ///   按 ReframeHandView 量占地的**同一个口径**（|cos|·卡深 + |sin|·卡宽）算出世界半深，
        ///   再投一次屏幕 —— 比投四个角便宜，这点误差对"文字躲开牌"没有意义。
        /// </summary>
        private bool HandScreenTop(out float top)
        {
            top = 0f;
            if (setup == null || setup.cam == null || setup.hand == null) return false;

            bool any = false;
            float highest = float.MinValue;

            for (int i = 0; i < setup.hand.Count; i++)
            {
                PlayCard c = setup.hand[i];
                if (c == null || c.slotIndex >= 0) continue;      // 放进槽里的不算"手牌那一排"

                float yaw = c.transform.eulerAngles.y * Mathf.Deg2Rad;
                float halfZ = (Mathf.Abs(Mathf.Cos(yaw)) * CardFactory.CardDepth
                             + Mathf.Abs(Mathf.Sin(yaw)) * CardFactory.CardWidth) * 0.5f;

                Vector3 near = setup.cam.WorldToScreenPoint(c.transform.position - Vector3.forward * halfZ);
                Vector3 far  = setup.cam.WorldToScreenPoint(c.transform.position + Vector3.forward * halfZ);
                if (near.z <= 0f || far.z <= 0f) continue;        // 在相机背后，投影没有意义

                float yTop = Mathf.Max(near.y, far.y);
                if (!any || yTop > highest) { highest = yTop; any = true; }
            }

            if (!any) return false;
            top = highest;
            return true;
        }

        // ══════════════════════════════════════════════════════════════
        //  回合信息栏（顶部居中）
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 顶部信息栏：回合数 / 得分 / 当前刀片 / 待投放 + 确认键。
        ///
        /// 放屏幕正上方而不是左上角：左上角已经被卡牌详情和检视面板占了，
        /// 而且这条信息是全局状态，放正中最显眼、也最像"桌面上的那块牌子"。
        /// </summary>
        private void DrawTurnPanel()
        {
            if (turnLoop == null) return;
            if (turnLoop.IsPreparing) return;   // 这段由 DrawChoicePanel 负责

            // ── v2.1 分流 ──────────────────────────────────────────────
            //   两套玩法的状态完全不一样（v2.1 没有"已应用模块 / 投放区待确认"这回事，
            //   多出的是回合 x/4、行动机会 x/5、刀片 H/V、四种附魔层数）。
            //   旧那一份一行都没删，关掉开关就回到它。
            if (turnLoop.V21) { DrawTurnPanelV21(); return; }

            TurnState t = turnLoop.turn;

            const float w = 660f;
            float x = (Screen.width - w) * 0.5f;
            const float y = 14f;

            GUI.Box(new Rect(x, y, w, 158f), GUIContent.none, panelBox);

            GUI.Label(new Rect(x + 18f, y + 8f, w - 36f, 30f),
                      turnLoop.level.Name + "　第 " + t.turnNumber + " 回合"
                      + "　　得分 " + t.score + " / " + t.targetScore,
                      h1Panel);

            GUI.Label(new Rect(x + 18f, y + 40f, w - 36f, 24f),
                      "当前刀片：" + t.BladeName() + "　　" + t.BladeAttrLine(), bodyPanel);

            GUI.Label(new Rect(x + 18f, y + 64f, w - 36f, 22f),
                      "已应用模块：" + turnLoop.AppliedModulesText(), dimPanel);

            GUI.Label(new Rect(x + 18f, y + 90f, w - 200f, 24f),
                      "待投放：" + turnLoop.StagedText,
                      turnLoop.StagedCount > 0 ? bodyPanel : dimPanel);

            GUI.Label(new Rect(x + 18f, y + 112f, w - 200f, 22f),
                      "本回合行动 " + turnLoop.ActionsUsed + " / " + turnLoop.actionsPerTurn
                      + "　（还剩 " + turnLoop.ActionsLeft + " 次）",
                      turnLoop.ActionsLeft > 0 ? bodyPanel : dimPanel);

            bool canConfirm = turnLoop.CanInteract
                           && (turnLoop.StagedCount > 0
                               || (turnLoop.cup != null && turnLoop.cup.particles.Count > 0));

            bool oldEnabled = GUI.enabled;
            GUI.enabled = canConfirm;

            if (GUI.Button(new Rect(x + w - 172f, y + 84f, 154f, 32f), "确认投放", btn))
            {
                GUI.FocusControl(null);
                turnLoop.Confirm();
            }

            GUI.enabled = turnLoop.CanInteract && turnLoop.ActionsLeft > 0;

            if (GUI.Button(new Rect(x + w - 172f, y + 120f, 154f, 26f), "空打（跳过）", btn))
            {
                GUI.FocusControl(null);
                turnLoop.SkipAction();
            }

            GUI.enabled = oldEnabled;
        }

        // ══════════════════════════════════════════════════════════════
        //  v2.1 状态区（回合 x/4、行动机会 x/5、刀片 H/V、四种附魔层数）
        //
        //  【为什么单独一块，而不是把旧信息栏改几个字】
        //   旧信息栏的每一行在 v2.1 里都没有对应物（已应用模块、投放区待确认、
        //   空打次数），而 v2.1 要看的（刀片 H/V、附魔层数、本回合已启动几次）
        //   旧的一行都没有。硬拼成一块的结果是两边都看不懂。
        //   所以两块并存、由开关选一块 —— 旧的那一块一行不删。
        // ══════════════════════════════════════════════════════════════

        private void DrawTurnPanelV21()
        {
            TableRulesV21 r = turnLoop.rulesV21;
            if (r == null) return;

            // 宽度跟着屏幕收缩：660 是设计宽度（和旧信息栏同宽），
            // 窗口更窄时按屏幕减 24。少了这个 Min，面板会横着伸出屏幕 ——
            // 右边那几个按钮既看不见也点不到，文字也会在屏幕外被裁掉。
            float w = Mathf.Max(260f, Mathf.Min(660f, Screen.width - 24f));
            float x = (Screen.width - w) * 0.5f;
            const float y = 14f;

            // ★ 面板高度不再写死（上一版是 const h = 254f + 每行写死 22 像素）。
            //   "规则模式：v2.1（本分支默认）｜出牌不消耗行动机会，只有「启动破壁机」
            //   消耗 1 次 + 1 点刀片 H" 这一行在 1280 宽的窗口下就会折成两行，
            //   写死的 22 像素把第二行直接裁掉 —— 玩家说的"文字显示一半"就是它。
            //   现在整块交给 GUILayout.BeginVertical(panelGroup)：组的底是
            //   **内容排完之后**才按组矩形画的，内容多高面板就多高，
            //   既不裁字、也不会在底部留一块空（先量高度再画底要维护两遍文字，不要）。
            //
            // ★ 半透明 + 可拖动：底板换 panelGroupGlass（0.78，能透出后面的牌，
            //   文字样式没动所以字还是实心的）；整块位移走 GUI.matrix，
            //   下面这套按内容长高的布局一行都不用改。
            //   报告面板开着的时候**不接拖动**：它是画在这块上面的（见 OverlayOnTop），
            //   鼠标点在报告上却把回合面板拖走就太怪了。
            Matrix4x4 prevMatrix = GUI.matrix;
            GUI.matrix = OffsetMatrix(turnPanelDrag.offset) * prevMatrix;

            GUILayout.BeginArea(new Rect(x, y, w, Mathf.Max(60f, Screen.height - y - 14f)));
            GUILayout.BeginVertical(panelGroupGlass);

            // ── ① 模式 + 回合 / 行动机会 / 分数（最关键的一行放最上面）──
            GUILayout.Label("第 " + r.turnIndex + "/" + GameJam.Rules.LevelRun.TurnsPerLevel + " 回合"
                            + "　　行动机会 " + r.actionPoints + "/" + GameJam.Rules.LevelRun.ActionPointsPerTurn
                            + "　　总分 " + r.score + " / " + r.targetScore, h1Panel);

            // ── ② 模式 + 未识别规则警告 ──
            GUILayout.Label("规则模式：" + (TableSettings.UseRulesV21 ? "v2.1（本分支默认）" : "旧流程")
                            + "　｜　出牌不消耗行动机会，只有「启动破壁机」消耗 1 次 + 1 点刀片 H",
                            dimPanel);

            DrawRulesWarning(r);

            // ── ②b 占位数值提示 ──
            //   卡表里 h/d/v 全 0 的卡会兜一套临时值（否则刀片 H=0、一进关卡就爆刀）。
            //   必须写出来 —— 不然玩家会把这套临时值当成策划定的数值。
            string ph = r.PlaceholderWarning();
            if (!string.IsNullOrEmpty(ph)) GUILayout.Label(ph, dimPanel);

            // ── ③ 刀片（H / V / 四种附魔层数）──
            GUILayout.Label("刀片：" + (r.blade != null ? r.blade.Describe() : "（无）")
                            + (r.blade != null && r.blade.V <= 0 ? "　← 得分加成 0（该核心卡的 V 就是 0）" : ""),
                            bodyPanel);

            GUILayout.Label("附魔层数：" + (r.blade != null ? r.blade.layers.Describe() : "（无）")
                            + "　　本回合已启动 " + r.startsThisTurn + " 次"
                            + (r.startsThisTurn > 0 ? "（最后一次启动会触发献祭吞噬）" : ""),
                            bodyPanel);

            // ── ④ 目标素材 ──
            GUILayout.Label("启动目标：" + r.TargetText, r.selected != null ? bodyPanel : dimPanel);

            // ── ⑤ 手牌 / 桌面 ──
            // ★ 刀片卡要单独点名：它和素材卡长得一模一样（同尺寸、同卡面路数，
            //   盐和水连 H/D/V 都可能完全相同），不点出来玩家就会数出
            //   "面板写桌面素材 3 张、画面里 4 张卡"。卡上那个「刀片」字样就是它。
            GUILayout.Label("手牌：素材 " + r.hand.Count + " 张｜法术 " + r.handSpells.Count + " 张"
                            + "　桌面素材 " + r.LiveTableCount() + " 张"
                            + (turnLoop.bladeCard != null ? " ＋ 刀片卡 1 张（桌上前方带「刀片」字样的那张）" : "")
                            + (turnLoop.StagedCount > 0 ? "　待上桌 " + turnLoop.StagedText : ""),
                            dimPanel);

            bool canActivate = r.CanActivate && r.selected != null && !r.selected.removed;

            // 不能启动的原因直接写出来 —— 按钮灰着却不解释，玩家只会以为是 bug。
            // ★ 但"原因"必须指向**玩家现在真做得到**的那件事：见 V21BlockHint。
            if (!canActivate)
            {
                Color prev = GUI.color;
                GUI.color = new Color(1f, 0.62f, 0.42f);
                GUILayout.Label(V21BlockHint(r), dimPanel);
                GUI.color = prev;
            }

            // ── ⑥ 按钮 ──
            // 四个按钮平分一行：旧版是 x+18 / x+202 / x+364 / x+504 四个**写死的横坐标**，
            // 面板一窄第四个就压到第三个上面去（挤成一坨没法点）。
            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();

            bool oldEnabled = GUI.enabled;
            GUI.enabled = canActivate;

            if (GUILayout.Button("启动破壁机", btn, GUILayout.ExpandWidth(true), GUILayout.Height(34f)))
            {
                GUI.FocusControl(null);
                turnLoop.ActivateJuicer();
            }

            GUI.enabled = oldEnabled;

            if (GUILayout.Button(TableSettings.UseRulesV21 ? "上　桌" : "放置到桌面",
                                 btn, GUILayout.ExpandWidth(true), GUILayout.Height(34f)))
            {
                GUI.FocusControl(null);
                turnLoop.Confirm();     // v2.1 下这一条 = 出牌（不消耗行动机会）
            }

            if (GUILayout.Button("结束本回合", btn, GUILayout.ExpandWidth(true), GUILayout.Height(34f)))
            {
                GUI.FocusControl(null);
                turnLoop.EndRoundOrLevel();
            }

            if (GUILayout.Button("自动选目标", btn, GUILayout.ExpandWidth(true), GUILayout.Height(34f)))
            {
                GUI.FocusControl(null);
                turnLoop.SelectNewestTarget();
            }

            GUILayout.EndHorizontal();

            GUILayout.EndVertical();

            // 组矩形 = 面板这一帧画在哪。★ 必须在 EndArea **之前**取，
            //   而且它是 Area 内的相对坐标，要加回 Area 自己的原点 (x, y)；
            //   这一份 rect 是"没加位移"的（和 GUI.matrix 里的鼠标坐标同一套，见 HandlePanelDrag）。
            Rect panelRect = GUILayoutUtility.GetLastRect();
            panelRect.x += x;
            panelRect.y += y;

            GUILayout.EndArea();

            PanelFrameEnd(turnPanelDrag, panelRect);

            GUI.matrix = prevMatrix;
        }

        /// <summary>
        /// 这块面板上面是不是还盖着别人（报告 / 设置 / 暂停 / 右键检视窗口）。
        ///
        /// 【为什么要问这一句】IMGUI 的事件是"先画的先拿"，而我这些 v2.1 面板
        ///   **都画在那些弹窗前面**：鼠标点在弹窗上，底下这块却先一步把这一下消费掉 ——
        ///   结果是在报告上拖一下、动的是底下的回合面板，更糟的是弹窗上的按钮
        ///   会因为拿不到 MouseDown 而点不动（按钮是按下时记 hotControl、抬起时才触发）。
        ///   所以只要上面还有别人，就不接拖动。矩形照记（防穿透那件事跟谁在最上面无关）。
        /// </summary>
        private bool OverlayOnTop(PanelDrag self)
        {
            if (rulesReportOpen && self != reportDrag) return true;
            if (turnLoop != null && (turnLoop.settingsOpen || turnLoop.paused)) return true;
            if (interaction != null && interaction.Inspected != null) return true;
            return false;
        }

        /// <summary>
        /// 不能启动时该做什么 —— 提示必须指向**玩家现在真做得到**的那件事。
        ///
        /// 【为什么不能直接用规则层的 BlockReason】它只说"为什么不能启动"，
        ///   不知道场上实况。用户卡住那局的原话是「先在桌面上点一张素材当启动目标」，
        ///   而那时桌面素材 0 张、他点的那张盐还压在投放区 ——
        ///   提示在指挥一件做不到的事，玩家的感受就是"点了怎么没用"。
        ///   规则层（TableRulesV21）不归这里改，所以在 HUD 这一层按
        ///   "启动前提 / 投放区 / 桌面 / 手牌"四段实况给话。
        ///
        /// 【顺序不能反】先看 CanActivate：行动机会用完、爆刀、关卡结束这三种，
        ///   玩家再怎么摆牌也启动不了，说别的都是误导。
        /// </summary>
        private string V21BlockHint(TableRulesV21 r)
        {
            // ① 启动的硬前提不满足（行动机会 / 刀片 / 关卡状态）→ 照规则层说
            if (!r.CanActivate) return "⚠ " + r.BlockReason;

            // ② 选了目标却仍不能启动（目标被吞了 / D 耗尽…）→ 也照规则层说
            if (r.selected != null) return "⚠ " + r.BlockReason;

            int table = r.LiveTableCount();

            // ③ 投放区压着牌、桌上却空着：先把那张放上去（这正是用户卡住的那一步）
            //   ★ 提示里不再写旧按钮名「放置到桌面」——v2.1 那个按钮已经叫「上　桌」，
            //     而且现在点那张牌本身就能上桌，指路要指向玩家眼前的东西。
            if (table <= 0 && turnLoop.StagedCount > 0)
                return "⚠ 待上桌还压着 " + turnLoop.StagedCount + " 张："
                       + "点它一下（或点「上　桌」）就上桌，上桌后自动选为启动目标";

            // ④ 桌上空、待上桌也空：告诉玩家"点手牌"这条更省事的路
            if (table <= 0)
                return "⚠ 桌面还没有素材：点手牌里的素材直接上桌（也可以拖到「上桌位」再点「上　桌」）";

            // ⑤ 桌上有牌但没选目标
            return "⚠ 桌面上有 " + table + " 张素材，但还没选目标：点其中一张把它选为启动目标";
        }

        /// <summary>
        /// 未实现规则的醒目提示（v2.1 状态栏里的那一行）。
        ///
        /// 【这条提示的存在意义】"没实现"最坏的样子不是缺功能，而是**看起来实现了**。
        ///   所以只要解析报告里有未识别的句子，这一行就必须是醒目的，
        ///   而且要给一个能查到"是哪几句"的入口（F2）。
        ///   报告没建出来时也不能装作干净 —— 那会写成"报告没建出来"。
        ///
        /// 【★ 为什么从 GUI.Label(Rect) 改成 GUILayout.Label】
        ///   上一版画在一个写死 22 像素高的矩形里：这句话一折行，第二行就被裁。
        ///   交给 GUILayout 之后它自己按文字长度要高度，多长都完整显示。
        /// </summary>
        private void DrawRulesWarning(TableRulesV21 r)
        {
            int n = r.UnrecognizedCount;

            if (n < 0)
            {
                GUILayout.Label("⚠ 卡表解析报告没建出来 —— 无法确认哪些规则没实现", dimPanel);
                return;
            }

            if (n == 0)
            {
                GUILayout.Label("✓ 卡表规则文本全部已识别（"
                                + (r.report != null ? r.report.ParsedSentences + " 句" : "") + "）",
                                dimPanel);
                return;
            }

            // 用暖红色，和别的灰字拉开 —— 这是"别信这一条"的信号
            Color prev = GUI.color;
            GUI.color = new Color(1f, 0.62f, 0.42f);
            GUILayout.Label("⚠ " + n + " 条规则未实现（这些规则不会生效），按 F2 查看", bodyPanel);
            GUI.color = prev;
        }

        private void DrawPhaseOverlay()
        {
            if (turnLoop == null) return;

            switch (turnLoop.phase)
            {
                case TablePhase.Simulating:  DrawSimulating();  break;
                case TablePhase.TurnResult:  DrawTurnResult();  break;
                case TablePhase.LevelEnd:    DrawLevelEnd();    break;
                case TablePhase.LevelResult: DrawLevelResult(); break;
            }
        }

        private static Rect CenterBox(float w, float h)
        {
            return new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
        }

        /// <summary>
        /// 模拟中 —— Day 3 这里是"模拟中…"占位，现在换成真实的杯内 2D 模拟。
        /// 这一屏只负责把 CupSim 画出来，规则一条都不在这。
        /// </summary>
        private void DrawSimulating()
        {
            if (turnLoop == null || turnLoop.cup == null) return;

            const float w = 540f, h = 540f;
            Rect r = CenterBox(w, h);

            cupView.Draw(r, turnLoop.cup, turnLoop.turn.turnNumber, panelBox);
        }

        /// <summary>回合结算。</summary>
        private void DrawTurnResult()
        {
            // ── v2.1 分流 ──────────────────────────────────────────────
            //   v2.1 没有"杯内粒子模拟"这一屏（分数由引擎即时结算），
            //   这一屏改成"本回合打出来的账"：得分来源 + 结算日志尾部。
            if (turnLoop.V21) { DrawTurnResultV21(); return; }

            TurnState t = turnLoop.turn;

            const float w = 580f, h = 262f;
            Rect r = CenterBox(w, h);
            GUI.Box(r, GUIContent.none, panelBox);

            GUI.Label(new Rect(r.x + 22f, r.y + 16f, w - 44f, 32f),
                      "第 " + t.turnNumber + " 回合结算", h1Panel);

            float y = r.y + 58f;
            GUI.Label(new Rect(r.x + 22f, y, w - 44f, 24f),
                      "本回合得分：" + turnLoop.cup.roundScore, bodyPanel); y += 24f;
            GUI.Label(new Rect(r.x + 22f, y, w - 44f, 24f),
                      "本关总得分：" + t.score + " / " + t.targetScore, bodyPanel); y += 24f;
            GUI.Label(new Rect(r.x + 22f, y, w - 44f, 24f),
                      "本回合反应：" + turnLoop.cup.ReactionSummary(), dimPanel); y += 24f;
            GUI.Label(new Rect(r.x + 22f, y, w - 44f, 24f), "杯内食材：" + t.CupNames(), bodyPanel); y += 24f;
            GUI.Label(new Rect(r.x + 22f, y, w - 44f, 46f), "剩余手牌：" + turnLoop.HandSummary(), dimPanel); y += 50f;

            if (t.IsHandEmpty)
                GUI.Label(new Rect(r.x + 22f, y, w - 44f, 24f), "手牌已用完。", dimPanel);

            string label = t.IsHandEmpty ? "结束本关" : "下一回合";

            if (GUI.Button(new Rect(r.x + w - 190f, r.y + h - 62f, 168f, 42f), label, btn))
            {
                GUI.FocusControl(null);
                turnLoop.NextTurn();
            }
        }

        private void DrawLevelEnd()
        {
            // ── v2.1 分流 ──────────────────────────────────────────────
            //   v2.1 的关卡结束有四个原因（正文 §八），必须写清是哪一个 ——
            //   "爆刀"和"4 回合用完"是两种完全不同的结果，混成一句
            //   "手牌已用完，关卡结束"会让玩家以为是自己出牌出少了。
            if (turnLoop.V21)
            {
                TableRulesV21 r = turnLoop.rulesV21;
                if (r == null) return;

                // 【★ 布局】不再写死 520×176：结束原因是引擎写的中文句子
                //   （"刀片 H 归零，爆刀"、"4 回合用完了"…长度不定），
                //   写死高度时一折行就被裁。现在高按内容长、宽按屏幕收缩。
                BeginCenterPanel(520f, levelEndDrag);

                GUILayout.Label(r.bursted ? "爆　刀 —— 关卡结束" : "关卡结束", h1Panel);

                GUILayout.Label("结束原因：" + (string.IsNullOrEmpty(r.endReason) ? "（未记录）" : r.endReason),
                                bodyPanel);

                GUILayout.Label("最终分数：" + r.score + " / 目标分 " + r.targetScore
                                + "　（爆刀时当前分数 ×2，正文 §五）", bodyPanel);

                GUILayout.Label("刀片：" + (r.blade != null ? r.blade.Describe() : "（无）"), dimPanel);

                GUILayout.Space(8f);

                if (GUILayout.Button("进入结算", btn, GUILayout.ExpandWidth(true), GUILayout.Height(40f)))
                {
                    GUI.FocusControl(null);
                    turnLoop.ShowLevelResult();
                }

                EndCenterPanel();
                return;
            }

            const float w = 470f, h = 138f;
            Rect r2 = CenterBox(w, h);
            GUI.Box(r2, GUIContent.none, panelBox);

            GUI.Label(new Rect(r2.x + 22f, r2.y + 20f, w - 44f, 34f), "手牌已用完，关卡结束", h1Panel);

            if (GUI.Button(new Rect(r2.x + 22f, r2.y + 76f, w - 44f, 42f), "进入结算", btn))
            {
                GUI.FocusControl(null);
                turnLoop.ShowLevelResult();
            }
        }

        private void DrawLevelResult()
        {
            // ── v2.1 分流 ──────────────────────────────────────────────
            //   旧那一屏列的是"已投放模块 / 杯内食材"，v2.1 里这两个概念都不存在。
            if (turnLoop.V21) { DrawLevelResultV21(); return; }

            TurnState t = turnLoop.turn;

            const float w = 580f, h = 316f;
            Rect r = CenterBox(w, h);
            GUI.Box(r, GUIContent.none, panelBox);

            GUI.Label(new Rect(r.x + 22f, r.y + 16f, w - 44f, 34f),
                      turnLoop.level.Name + "　总分 " + t.score + " / 目标分 " + t.targetScore
                      + "　" + (turnLoop.Passed ? "通过" : "未通过"),
                      h1Panel);

            float y = r.y + 56f;

            // 默认放行要说出来，不然"0 分也算通过"看着像 bug
            if (turnLoop.level.passByDefault)
            {
                GUI.Label(new Rect(r.x + 22f, y, w - 44f, 22f),
                          "（数值模拟还没接，本关默认放行）", dimPanel);
            }
            y += 26f;

            GUI.Label(new Rect(r.x + 22f, y, w - 44f, 24f), "刀片：" + t.BladeName(), bodyPanel); y += 26f;
            GUI.Label(new Rect(r.x + 22f, y, w - 44f, 24f), "　" + t.BladeAttrLine(), bodyPanel); y += 26f;
            GUI.Label(new Rect(r.x + 22f, y, w - 44f, 24f),
                      "已投放模块：" + turnLoop.AppliedModulesText(), bodyPanel); y += 26f;
            GUI.Label(new Rect(r.x + 22f, y, w - 44f, 44f), "杯内：" + t.CupNames(), dimPanel);
            y += 50f;

            string label = turnLoop.Passed
                ? (turnLoop.HasNextLevel ? "进入下一关" : "已是最后一关")
                : "重试本关";

            if (GUI.Button(new Rect(r.x + 22f, y, 230f, 40f), label, btn))
            {
                GUI.FocusControl(null);
                if (turnLoop.Passed) turnLoop.NextLevel();
                else                 turnLoop.RestartLevel();
            }

            if (GUI.Button(new Rect(r.x + w - 210f, y, 188f, 40f), "返回关卡界面", btn))
            {
                GUI.FocusControl(null);
                turnLoop.OpenLevelSelect();
            }
        }

        // ── 划过时的小信息条 ──────────────────────────────────────────
        private void DrawCardInfo()
        {
            PlayCard card = interaction.FocusCard;
            if (card == null) return;
            if (card.card == null) return;

            const float w = 320f;
            float h = 130f + AttrCatalog.Count * 22f;

            GUI.Box(new Rect(16f, 14f, w, h), GUIContent.none, panelBox);

            GUI.Label(new Rect(30f, 26f, w - 28f, 28f), card.DisplayName, h1Panel);

            float y = 60f;

            if (card.IsModule)
            {
                GUI.Label(new Rect(30f, y, w - 28f, 22f), "类型：变速模块", bodyPanel);
                GUI.Label(new Rect(30f, y + 24f, w - 28f, 44f),
                          "效果：" + card.card.Describe(), bodyPanel);
                y += 72f;
            }
            else
            {
                Ingredient ing = card.card.ingredient;
                foreach (AttrDef def in AttrCatalog.All())
                {
                    int v = ing.attrs.Get(def.id);
                    GUI.Label(new Rect(30f, y, w - 28f, 22f),
                              def.Label + "：" + def.Format(v), v != 0 ? bodyPanel : dimPanel);
                    y += 22f;
                }
            }

            GUI.Label(new Rect(30f, y + 4f, w - 28f, 22f), PositionText(card), dimPanel);
            GUI.Label(new Rect(30f, y + 26f, w - 28f, 22f), "右键单击查看完整数据", dimPanel);
        }

        // ══════════════════════════════════════════════════════════════
        //  v2.1：回合结算 / 关卡总结算 / 规则解析报告
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// v2.1 的回合结算屏。
        ///
        /// 【为什么不复用旧那一屏】旧屏列的是"杯内得分 / 本回合反应 / 杯内食材"，
        /// 这三个概念在 v2.1 里都不存在（分数由引擎即时算，没有杯内模拟）。
        /// 这里改成玩家真正要对账的东西：总分、本次启动的得分构成、以及引擎的原始日志。
        /// 日志是**照贴**的 —— 玩家说"我明明这么打却没过"时，一条条对回去。
        ///
        /// 【★ 布局这一版为什么换掉】原来是写死 660×430 + 日志每行 19 像素：
        ///   引擎日志是中文长句，折一行就和下面的按钮叠在一起（按钮画在
        ///   box.y + h - 56，日志按行数往下推，两边都不让谁）。
        ///   现在日志单独给一块滚动区，面板多高它就多高 ——
        ///   日志再长也只是滚动区变窄，按钮永远在面板里。
        /// </summary>
        private void DrawTurnResultV21()
        {
            TableRulesV21 r = turnLoop.rulesV21;
            if (r == null) return;

            // 尺寸跟着屏幕走：660×560 是设计尺寸，小窗口下按屏幕收缩
            // （写死的话小窗口里面板比屏幕还大，底部按钮直接跑到屏幕外）
            float w = Mathf.Max(320f, Mathf.Min(660f, Screen.width - 32f));
            float h = Mathf.Max(220f, Mathf.Min(560f, Screen.height - 32f));
            Rect box = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);

            // 半透明底板 + 可拖动（位移走 GUI.matrix，矩形只有 box 一处要跟着变）
            Matrix4x4 prevMatrix = GUI.matrix;
            GUI.matrix = OffsetMatrix(turnResultDrag.offset) * prevMatrix;

            GUI.Box(box, GUIContent.none, panelBoxGlass);
            GUILayout.BeginArea(new Rect(box.x + 14f, box.y + 12f, w - 28f, h - 24f));

            GUILayout.Label("第 " + r.turnIndex + " 回合" + (r.levelOver ? "（关卡已结束）" : "进行中"), h1Panel);

            GUILayout.Label("总分：" + r.score + " / " + r.targetScore
                            + "　　刀片：" + (r.blade != null ? r.blade.Describe() : "（无）"), bodyPanel);

            GUILayout.Label("附魔：" + (r.blade != null ? r.blade.layers.Describe() : "（无）")
                            + "　　行动机会 " + r.actionPoints + "/" + GameJam.Rules.LevelRun.ActionPointsPerTurn,
                            bodyPanel);

            GUILayout.Label("本次启动：" + (string.IsNullOrEmpty(r.lastSummary) ? "（这一回合还没启动过）" : r.lastSummary),
                            dimPanel);

            GUILayout.Label("桌面素材：" + r.LiveTableCount() + " 张　剩余手牌：" + r.HandText(), dimPanel);

            // ── 引擎日志尾部（结算顺序照贴，方便一条条对）──
            // 每行不写死高度：日志是中文长句，折行之后整行都要在（原来写死 20 像素，
            // 折出来的第二行被裁 —— 对账时最需要看的那半句正好没了）。
            GUILayout.Space(2f);
            GUILayout.Label("── 上一次结算的引擎日志 ──", bodyPanel);

            turnResultLogScroll = GUILayout.BeginScrollView(turnResultLogScroll, false, false,
                                                            GUI.skin.horizontalScrollbar,
                                                            GUI.skin.verticalScrollbar,
                                                            scrollGlass,
                                                            GUILayout.ExpandHeight(true));

            List<string> tail = r.LastLogTail(12);
            if (tail.Count == 0)
            {
                GUILayout.Label("（还没有启动过 —— 点顶栏的「启动破壁机」）", dimPanel);
            }
            else
            {
                for (int i = 0; i < tail.Count; i++) GUILayout.Label(tail[i], dimPanel);
            }

            GUILayout.Space(14f);   // 末行别贴着下沿（贴着看着也像被切了）

            GUILayout.EndScrollView();

            // ── 按钮 ──
            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();

            bool over = r.levelOver || r.turnIndex >= GameJam.Rules.LevelRun.TurnsPerLevel;

            if (GUILayout.Button(over ? "结束本关" : "下一回合", btn,
                                 GUILayout.ExpandWidth(true), GUILayout.Height(42f)))
            {
                GUI.FocusControl(null);
                turnLoop.NextTurn();
            }

            if (GUILayout.Button("继续操作桌面", btn, GUILayout.ExpandWidth(true), GUILayout.Height(42f)))
            {
                GUI.FocusControl(null);
                turnLoop.phase = TablePhase.Select;
            }

            GUILayout.EndHorizontal();

            GUILayout.EndArea();

            EndPanelFrame(turnResultDrag, box, prevMatrix);
        }

        /// <summary>
        /// v2.1 的关卡总结算。列出 v2.1 真正有的东西（刀片 / 附魔 / 桌面 / 日志）。
        ///
        /// 【★ 布局】原来是写死 660×420 + 每行 22 像素：标题那一行
        ///   （关卡名 + 总分数 + 通过/未通过）在窄窗口下折行就被 34 像素的矩形裁掉，
        ///   面板宽 660 在小窗口下还横出屏幕。现在宽按屏幕收缩、高按内容长。
        /// </summary>
        private void DrawLevelResultV21()
        {
            TableRulesV21 r = turnLoop.rulesV21;
            if (r == null) return;

            BeginCenterPanel(660f, levelResultDrag);

            GUILayout.Label(turnLoop.level.Name + "　总分 " + r.score + " / 目标分 " + r.targetScore
                            + "　" + (turnLoop.Passed ? "通过" : "未通过"), h1Panel);

            GUILayout.Label("结束原因：" + (string.IsNullOrEmpty(r.endReason) ? "（未记录）" : r.endReason), bodyPanel);
            GUILayout.Label("刀片：" + (r.blade != null ? r.blade.Describe() : "（无）"), bodyPanel);
            GUILayout.Label("最终附魔：" + (r.blade != null ? r.blade.layers.Describe() : "（无）"), bodyPanel);
            GUILayout.Label("打过的回合：" + r.turnIndex + "/" + GameJam.Rules.LevelRun.TurnsPerLevel
                            + "　桌面残留素材：" + r.LiveTableCount() + " 张", dimPanel);

            // 默认放行要说出来，不然"0 分也算通过"看着像 bug
            if (turnLoop.level.passByDefault)
                GUILayout.Label("（Level.passByDefault 还开着：没到目标分也算通过）", dimPanel);

            if (r.warnings.Count > 0)
                GUILayout.Label("⚠ 本关有 " + r.warnings.Count + " 条引擎警告（卡表缺产物之类），F2 看报告", dimPanel);

            GUILayout.Space(8f);

            string label = turnLoop.Passed
                ? (turnLoop.HasNextLevel ? "进入下一关" : "已是最后一关")
                : "重试本关";

            // 两个按钮平分一行：旧版是 x+22 与 x+w-230 两个写死的横坐标，
            // 面板一窄就在中间叠在一起
            GUILayout.BeginHorizontal();

            if (GUILayout.Button(label, btn, GUILayout.ExpandWidth(true), GUILayout.Height(40f)))
            {
                GUI.FocusControl(null);
                if (turnLoop.Passed) turnLoop.NextLevel();
                else                 turnLoop.RestartLevel();
            }

            if (GUILayout.Button("返回关卡界面", btn, GUILayout.ExpandWidth(true), GUILayout.Height(40f)))
            {
                GUI.FocusControl(null);
                turnLoop.OpenLevelSelect();
            }

            GUILayout.EndHorizontal();

            EndCenterPanel();
        }

        /// <summary>
        /// 卡牌规则解析报告（F2）—— **未实现规则必须能被看见**。
        ///
        /// 【为什么这块要单独做】
        ///   "⚠ 3 条规则未实现"这句话本身没法行动：策划要知道是哪张卡的哪句话、
        ///   为什么没认出来、该怎么改。RuleReport 就是为这个存在的，
        ///   不给入口等于没做 —— 未实现的规则会看起来像生效了。
        ///
        /// 【万一报告建不出来】不能装作干净：这里会明写"报告没建出来"，
        ///   因为"没有未识别规则"和"不知道有没有未识别规则"是两回事。
        ///
        /// 【★ 排版为什么整个换成 GUILayout】
        ///   上一版是"写死矩形 + 每行写死像素高"：统计行写死 24 像素高、
        ///   滚动内容写死 4600 像素高。窗口一窄，100 多个字的统计行折成两行，
        ///   第二行直接被那个 24 像素的矩形裁掉（截图里右边少一截就是这个）；
        ///   内容实际比 4600 还高，"滚到底"也只能滚到 4600 处 ——
        ///   最后一行永远差半行出不来。两处是同一个病根：
        ///   **用手写的数字去猜排版结果**。现在高度全部由 GUILayout 自己算
        ///   （它量的就是真正的换行结果），没有数字要猜了。
        /// </summary>
        private void DrawRulesReportPanel()
        {
            TableRulesV21 r = turnLoop != null ? turnLoop.rulesV21 : null;

            // ── 面板尺寸跟着屏幕走 ──
            // 940×640 是设计尺寸（大窗口下就是这么大，正文一行放得下）；
            // 小窗口下按 Screen 减 32 收缩。两个 Min 缺一不可 ——
            // 少了它们，面板会比屏幕还大：右边那一截连同"关闭"按钮都在屏幕外，
            // 看得见半行字、却点不到按钮。下限只在窗口小到没法看时才生效。
            float w = Mathf.Max(240f, Mathf.Min(940f, Screen.width - 32f));
            float h = Mathf.Max(160f, Mathf.Min(640f, Screen.height - 32f));
            Rect box = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);

            GUI.Box(box, GUIContent.none, reportBoxGlass);

            // 半透明（报告是 0.88，比别的面板实一点：上面是几百行密排中文，可读性优先）
            // + 可拖动：位移走 GUI.matrix，下面那套排版一行都不用改。
            Matrix4x4 prevMatrix = GUI.matrix;
            GUI.matrix = OffsetMatrix(reportDrag.offset) * prevMatrix;

            // 14 / 12 对的是 panelBox 的 padding(12) 与边框(8)：内容从"框里面"起，
            // 不压在圆角边框上。★ 高度这里只减面板自己的内边距，
            // 标题栏 / 统计行占掉多少**不用自己减** —— 滚动区是 ExpandHeight，
            // 它自己会吃掉剩下的空间（旧版 h-100 那个 100 就是手算出来、还算错的）。
            GUILayout.BeginArea(new Rect(box.x + 14f, box.y + 12f, w - 28f, h - 24f));

            // ── 标题栏 ──
            // 标题 ExpandWidth、按钮定宽：窗口再窄也是标题折行，按钮不会被挤出去
            GUILayout.BeginHorizontal();
            GUILayout.Label("卡牌规则解析报告（v2.1）", h1Panel, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("关闭 (F2)", btn, GUILayout.Width(104f), GUILayout.Height(28f)))
            {
                GUI.FocusControl(null);
                rulesReportOpen = false;
            }
            GUILayout.EndHorizontal();

            if (r == null || r.report == null)
            {
                GUILayout.Label("⚠ 报告没建出来 —— 现在**无法确认**哪些规则没实现。"
                                + "先看 Console 里 [V21] 那条警告。", bodyPanel);
                GUILayout.EndArea();
                EndPanelFrame(reportDrag, box, prevMatrix);
                return;
            }

            // ── 统计行 ──
            // 拆成两行画（按 SummaryLine 里的"｜"拆），两行都**不设 GUILayout.Width**,
            // 所以窗口再窄也只是继续折行，不会像原来那样被矩形裁掉右半边。
            // 第一行固定是"素材 / 法术 / 句子 / 已解析 / 未识别"——
            // "未识别 5"这个最该被看见的数绝不会被挤到看不见的地方去。
            string[] summary = SplitSummary(r.report.SummaryLine());
            for (int i = 0; i < summary.Length; i++) GUILayout.Label(summary[i], bodyPanel);
            GUILayout.Space(2f);

            // ── 滚动区 ──
            // ★ 不传 viewRect：内容高度由 GUILayout 量，所以"能不能滚到最后一行"
            //   不再取决于某个写死的常数（上一版那个 4600 就是这么来的坑）。
            //   ExpandHeight(true) 让滚动区吃掉剩下的全部高度，
            //   面板多高它就多高 —— 标题栏 / 统计行多高都不影响它。
            //
            // ★ 滚动区的顶边要在 BeginScrollView **之前**量（报告"滚到底"要用它算
            //   可见高度）。不能写成"BeginScrollView 之后立刻 GetLastRect()"——
            //   BeginScrollView 内部就是一个 group，"刚开组就取 last"是非法的：
            //   实测每帧刷一条 "You cannot call GetLast immediately after beginning
            //   a group" 控制台错误，而且取回来的是上一帧的脏矩形，
            //   会把后面的排版整体带偏。GetRect(0,0) 是合法的零尺寸占位，
            //   返回的就是下一个控件要被摆到的位置。
            float scrollTop = GUILayoutUtility.GetRect(0f, 0f).y;

            rulesReportScroll = GUILayout.BeginScrollView(rulesReportScroll, false, false,
                                                          GUI.skin.horizontalScrollbar,
                                                          GUI.skin.verticalScrollbar,
                                                          scrollGlass,
                                                          GUILayout.ExpandHeight(true));

            ReportSection("未识别清单（这些句子不会生效 —— 必须给策划确认）", UnrecognizedLines(r));
            ReportSection("规则表接不上（标签标了、连锁产物没写：命中却不变形）", FlatLines(r.report.tableGaps));
            ReportSection("可疑产出（产出的卡名不在卡表里）", FlatLines(r.report.unknownProducts));
            ReportSection("占位数值（正文没给数，用了默认值）", FlatLines(r.report.placeholders));
            ReportSection("v3 已取消、卡表里还留着的 v2.1 写法（写了也不生效）", FlatLines(r.report.superseded));
            ReportSection("当前模型执行不了的条目", FlatLines(r.report.unsupported));
            ReportSection("本关引擎打过的警告（运行时）", FlatLines(r.warnings));
            ReportSection("正文自相矛盾 / 没写清（已按一个明确选择实现，待策划拍板）",
                          FlatLines(r.report.conflicts));

            // 末尾留白：最后一行贴着滚动区下沿时，看着和被切了一半没区别
            GUILayout.Space(16f);

            // 最后一块内容的底 = 报告的实际内容高度（滚动区内部坐标，0 就是顶部）
            reportContentH = GUILayoutUtility.GetLastRect().yMax;

            GUILayout.EndScrollView();

            // 可见高度 = Area 高度 − 滚动区上面用掉的部分。
            // （滚动区是 Area 里最后一个控件，且 ExpandHeight(true)，剩下的都归它。）
            reportViewH = Mathf.Max(0f, (h - 24f) - scrollTop);

            GUILayout.EndArea();

            EndPanelFrame(reportDrag, box, prevMatrix);
        }

        /// <summary>
        /// 一块面板画完之后统一收尾：记矩形 → 接拖动 → 记屏幕矩形（防穿透用）。
        ///
        /// 【为什么"记矩形"和"接拖动"要分开】`d.lastRect` 不只是拖动命中判定用的，
        ///   出屏夹取（ClampPanelOffset）和探针喂位移都靠它。上面盖着别人时**不接拖动**，
        ///   但矩形必须照记 —— 不然那段时间 lastRect 一直是 (0,0,0,0)，
        ///   夹取会退化成"以屏幕左上角当面板"来算（实测：喂 (0,260) 得到的是 (40,260)，
        ///   面板被顶到左上角、之后连复位都回不去）。这个坑踩过一次，别再合并回去。
        /// </summary>
        private void PanelFrameEnd(PanelDrag d, Rect rect)
        {
            d.lastRect = rect;

            if (!OverlayOnTop(d)) HandlePanelDrag(d);
            RecordPanelRect(v21PanelRects, rect, d.offset);
        }

        /// <summary>GUI.Box + BeginArea 那种面板的收尾：接上面那套，再把矩阵还原。</summary>
        private void EndPanelFrame(PanelDrag drag, Rect box, Matrix4x4 prevMatrix)
        {
            PanelFrameEnd(drag, box);
            GUI.matrix = prevMatrix;
        }

        /// <summary>
        /// 把 SummaryLine() 按"｜"拆成最多两段（一行太长，拆开显示）。
        ///
        /// 【为什么不干脆让它自己折】折在哪随窗口宽度乱跳，最该被看见的
        ///   "未识别 5"经常被折到第二行去。按分隔符拆死：第一段永远是
        ///   "素材 / 法术 / 句子 / 已解析 / 未识别"，第二段是其余四个计数。
        ///   两段各自还会 wordWrap，所以窗口再窄也只是多折一行，不会丢字。
        ///
        /// SummaryLine() 在 Rules 层（另有人在改，不动它），这里只负责排版。
        /// </summary>
        private static string[] SplitSummary(string s)
        {
            string[] parts = s.Split(new string[] { " ｜ " }, System.StringSplitOptions.None);
            if (parts.Length <= 2) return parts;

            string head = parts[0] + " ｜ " + parts[1];
            string tail = "";
            for (int i = 2; i < parts.Length; i++)
            {
                if (tail.Length > 0) tail += " ｜ ";
                tail += parts[i];
            }
            return new string[] { head, tail };
        }

        /// <summary>
        /// 报告里的一行：缩进层级 + 文字 + 是否用亮色。
        ///
        /// 【为什么不把"原句 / 原因 / 建议"拼成一句话】拼起来是一条几百字的长串：
        ///   窗口一窄折成四五段，看不出哪段是原因哪段是建议，策划改表时没法照着改。
        ///   拆成一行一条、各自缩进，换行之后的续行也从缩进位开始，对得齐。
        ///   （用空格填缩进不行：中文字体下空格宽度不稳，一折行就散了。）
        /// </summary>
        private struct ReportLine
        {
            public string text;
            public int    indent;   // 0 = 条目首行，1 = "原句/原因/建议"这类展开行
            public bool   strong;   // 条目首行用亮色，和展开行拉开层次

            public ReportLine(string text, int indent, bool strong)
            {
                this.text = text;
                this.indent = indent;
                this.strong = strong;
            }
        }

        /// <summary>
        /// "未识别清单"的每一行。
        ///
        /// 【为什么这三行必须分开】这一节是整份报告里唯一"必须照着改"的部分：
        ///   策划要拿原句去卡表里搜、照着原因判断、按建议改写。
        ///   原来是拼成"卡·字段　原句：xx　→ 原因（建议：yy）"一整句，
        ///   窗口一小就折成一团 —— 现在原句 / 原因 / 已认出 / 建议各占一行。
        /// </summary>
        private List<ReportLine> UnrecognizedLines(TableRulesV21 r)
        {
            List<ReportLine> lines = new List<ReportLine>();

            for (int i = 0; i < r.report.unrecognized.Count; i++)
            {
                GameJam.Rules.UnrecognizedRule u = r.report.unrecognized[i];
                if (u == null) continue;

                // 带上 cardId：策划是拿着 id 去 cards_v21.json 里定位的
                string head = u.cardName + " · " + u.field;
                if (!string.IsNullOrEmpty(u.cardId)) head += "　（" + u.cardId + "）";

                lines.Add(new ReportLine(head, 0, true));
                lines.Add(new ReportLine("原句：" + u.sentence, 1, false));
                lines.Add(new ReportLine("原因：" + u.reason, 1, false));
                if (!string.IsNullOrEmpty(u.partial))    lines.Add(new ReportLine("已认出：" + u.partial, 1, false));
                if (!string.IsNullOrEmpty(u.suggestion)) lines.Add(new ReportLine("建议：" + u.suggestion, 1, false));
            }
            return lines;
        }

        /// <summary>
        /// 把本来就是"一句一条"的清单（接不上 / 占位值 / 可疑产出…）包成报告行。
        /// 这些条目没有"原句/原因/建议"的结构，逐条显示就够。
        /// </summary>
        private static List<ReportLine> FlatLines(List<string> src)
        {
            List<ReportLine> lines = new List<ReportLine>();
            if (src == null) return lines;

            for (int i = 0; i < src.Count; i++) lines.Add(new ReportLine(src[i], 0, false));
            return lines;
        }

        /// <summary>
        /// 报告里的一节：标题 + 若干行。
        ///
        /// **没有 y 参数** —— 位置由 GUILayout 往下推。这样某一行折成两行时，
        /// 后面的内容会自动让位，不会被裁掉（上一版每行 +40 像素的写法，
        /// 一旦折行就整节错位）。
        /// </summary>
        private void ReportSection(string title, List<ReportLine> lines)
        {
            int n = lines != null ? lines.Count : 0;

            GUILayout.Space(6f);
            GUILayout.Label("── " + title + "：" + n + " ──", h1Panel);

            if (n == 0)
            {
                GUILayout.Label("　　（无）", dimPanel);
                return;
            }

            for (int i = 0; i < n; i++)
            {
                ReportLine line = lines[i];

                // 条目之间空一点：一屏里几十条，不留缝就糊成一片
                if (line.indent == 0 && i > 0) GUILayout.Space(8f);

                GUILayout.BeginHorizontal();
                GUILayout.Space(14f + line.indent * 22f);   // 缩进；续行也对得齐

                // ★ ExpandWidth(true) 是必须的：不给它，"折行后的长文本"会按
                //   自己的理想宽度撑出去，横向被裁 —— 这正是横向裁切的来源。
                GUILayout.Label(line.text, line.strong ? bodyPanel : dimPanel,
                                GUILayout.ExpandWidth(true));
                GUILayout.EndHorizontal();
            }
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
            if (interaction.Inspected == null || interaction.Inspected.card == null) return;

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
            if (card == null) return;
            if (card.card == null) return;

            // 两类牌的主题色来源不同：模块走冷色区，食材按 id 哈希取色。
            // Card.id 对两类都等于底层数据的 id，所以这里不用再分情况。
            Color accent = card.IsModule
                ? ProceduralArt.ModuleColor(card.card.id)
                : ProceduralArt.IngredientColor(card.card.id);

            // 模块没有"自己的属性"，印记固定给液体形；食材取数值最高的那个
            AttrId dominant = card.IsModule
                ? AttrId.Mercury
                : ProceduralArt.DominantAttr(card.card.ingredient);

            AttrDef domDef = AttrCatalog.Get(dominant);

            const float headerH = 42f;
            const float closeW  = 34f;
            float width = inspectRect.width;

            // ── 标题条：卡面同款主题色，兼作拖拽把手 ──
            GUILayout.BeginHorizontal();

            GUILayout.Box(card.DisplayName, HeaderStyle(accent),
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
            GUILayout.Label("类型：" + card.TypeTag, bodyPanel);
            GUILayout.Label("牌组：" + (setup != null && !string.IsNullOrEmpty(setup.deckName)
                                       ? setup.deckName : "（未知）"), bodyPanel);
            GUILayout.Label("位置：" + PositionText(card), bodyPanel);

            if (!card.IsModule)
                GUILayout.Label("主属性：" + (domDef != null
                                            ? domDef.Label + "（" + domDef.tendency + "）"
                                            : "?"), dimPanel);

            GUILayout.EndVertical();

            GUILayout.EndHorizontal();

            if (card.IsModule)
            {
                // ── 模块：显示效果，而不是三属性 ──
                // 模块本身没有属性值，它的数值是加到刀片上去的。
                GUILayout.Space(10f);
                GUILayout.Label("── 模块效果 ──", h1Panel);
                GUILayout.Space(4f);

                GUILayout.BeginVertical(panelBoxInner);
                GUILayout.Label(card.card.Describe(), bodyPanel);
                GUILayout.Label("投出去之后效果并入本局，作用在当前刀片上；"
                                + "之后换刀片不会把这份加成带走。", dimPanel);
                GUILayout.EndVertical();
            }
            else
            {
                Ingredient ing = card.card.ingredient;

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
            }

            GUILayout.Space(6f);
            GUILayout.Label("拖动标题栏移动窗口　｜　右键空白处或 Esc 关闭", dimPanel);
        }

        /// <summary>
        /// 卡牌现在在哪 —— 检视面板 / 划过信息条上要显示。
        ///
        /// 【v2.1 的说法和旧流程不一样】v2.1 把两个槽位的语义换了（素材槽 = 上桌位、
        ///   法术槽 = 附魔位，桌子上的牌子由 TableSetup.RefreshSlotLabels 改的），
        ///   所以这里也得跟着说同一套词 —— 否则玩家看着桌上的「上桌位」牌子，
        ///   检视面板却写着"投放区（待确认）"，又是一句对不上的旧话。
        ///   旧流程（UseRulesV21 = false）那句原样保留。
        /// </summary>
        private string PositionText(PlayCard card)
        {
            if (card.IsDragging) return "拿在手上";

            if (card.slotIndex >= 0)
            {
                if (!TableSettings.UseRulesV21) return "投放区（待确认）";

                if (card.slotIndex == TableTurnLoop.SlotSpell)    return "附魔位（法术槽）";
                if (card.slotIndex == TableTurnLoop.SlotMaterial) return "上桌位（素材槽）";
                return "上桌位 / 附魔位";
            }

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
            // 同样不设 Bold —— 见 EnsureStyles 里关于伪粗体的说明
            st.alignment = TextAnchor.MiddleLeft;
            st.padding = new RectOffset(16, 16, 6, 6);
            if (cjkFont != null) st.font = cjkFont;

            headerStyles[key] = st;
            return st;
        }
    }
}
