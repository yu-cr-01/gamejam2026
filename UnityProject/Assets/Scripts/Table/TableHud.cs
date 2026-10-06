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
        private GUIStyle panelBox, panelBoxInner, h1Panel, bodyPanel, dimPanel, btnClose;

        // ── 开场界面的大标题 ──
        private GUIStyle titleBig, titleSub, titleHint;

        // ── 杯内 2D 模拟视图 ──
        private CupSimView cupView;

        // ── v2.1 的规则解析报告面板（F2）──
        private bool     rulesReportOpen;
        private Vector2  rulesReportScroll;

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

        private void OnGUI()
        {
            EnsureStyles();
            if (setup == null || interaction == null) return;

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

            // 检视面板和划过信息条是同一个位置，二选一
            if (interaction.Inspected != null) DrawInspectPanel();
            else                               DrawCardInfo();
        }

        // ══════════════════════════════════════════════════════════════
        //  暂停菜单 / 设置
        // ══════════════════════════════════════════════════════════════

        private void DrawPausePanel()
        {
            const float w = 400f, h = 312f;
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
        /// 关卡界面：桌上一排关卡卡，点一张再确认。
        /// 和牌组/刀片那两个环节同一个形态 —— 都是"桌上一排大卡点一张"。
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
            GUI.Label(new Rect(x + 18f, y + 70f, w - 240f, 44f),
                      "点桌上的一张关卡卡选中它，再按确认。\n金色那张是你现在所在的关卡。", dimPanel);

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
        /// </summary>
        private void DrawBladePickV21()
        {
            TableRulesV21 r = turnLoop.rulesV21;
            if (r == null) return;

            const float w = 660f;
            const float h = 168f;
            float x = (Screen.width - w) * 0.5f;
            const float y = 14f;
            const float textW = w - 250f;

            GUI.Box(new Rect(x, y, w, h), GUIContent.none, panelBox);

            GUI.Label(new Rect(x + 18f, y + 8f, w - 36f, 30f), "选择你的刀片核心", h1Panel);

            float ty = y + 44f;

            MaterialCard cand = turnLoop.bladeCoreCard != null
                ? turnLoop.bladeCoreCard
                : r.CoreCandidate();

            GUI.Label(new Rect(x + 18f, ty, textW, 24f),
                      "当前候选：" + (cand != null
                                     ? cand.name + "（H " + cand.H + " · V " + cand.V + "）"
                                     : "（没有可当核心的素材）"),
                      bodyPanel);
            ty += 26f;

            GUI.Label(new Rect(x + 18f, ty, textW, 24f),
                      "初始手牌：素材 " + r.hand.Count + " 张 + 法术 " + r.handSpells.Count + " 张"
                      + "（正文 §2.6）", bodyPanel);
            ty += 26f;

            GUI.Label(new Rect(x + 18f, ty, textW, 24f),
                      "点桌上手牌里的一张素材即改选。核心卡的 H/V 就是刀片的初始 H/V，该卡移出手牌。",
                      dimPanel);
            ty += 24f;

            if (!string.IsNullOrEmpty(turnLoop.notice))
                GUI.Label(new Rect(x + 18f, ty, textW, 24f), turnLoop.notice, dimPanel);

            bool canConfirm = cand != null;

            bool oldEnabled = GUI.enabled;
            GUI.enabled = canConfirm;

            if (GUI.Button(new Rect(x + w - 210f, y + 112f, 192f, 40f), "确认刀片，进入关卡", btn))
            {
                GUI.FocusControl(null);
                turnLoop.ConfirmBladePick();
            }

            GUI.enabled = oldEnabled;
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
        private void DrawHints()
        {
            if (!TableSettings.ShowHints) return;

            const float w = 480f, h = 214f;
            float y = Screen.height - h - 14f;

            // v2.1 的操作方式完全不同（出牌不消耗行动、启动要点桌面素材、法术是点一下），
            // 提示必须跟着换 —— 写旧那一套会让玩家一直找不到"怎么启动"。
            if (turnLoop != null && turnLoop.V21)
            {
                GUI.Label(new Rect(16f, y, w, 24f), "把素材拖到桌面中间的投放区 → 按「放置到桌面」", h1);
                GUI.Label(new Rect(16f, y + 28f, w, 22f), "点桌面上的素材 = 选它当「启动破壁机」的目标（会抬起来）", dim);
                GUI.Label(new Rect(16f, y + 48f, w, 22f), "点手牌里的法术 = 直接附魔到刀片（不消耗行动机会）", dim);
                GUI.Label(new Rect(16f, y + 68f, w, 22f), "「启动破壁机」消耗 1 行动机会 + 1 点刀片 H", dim);
                GUI.Label(new Rect(16f, y + 88f, w, 22f), "每回合 5 次行动、每关 4 回合；本回合最后一次启动会献祭吞噬目标", dim);
                GUI.Label(new Rect(16f, y + 108f, w, 22f), "刀片 H 归零 = 爆刀，关卡结束、当前分数 ×2", dim);
                GUI.Label(new Rect(16f, y + 128f, w, 22f), "右键单击卡牌 → 查看完整数据　｜　右键拖动 → 转头", dim);
                GUI.Label(new Rect(16f, y + 148f, w, 22f), "F2 → 卡牌规则解析报告（哪些规则没实现看这里）", dim);
                GUI.Label(new Rect(16f, y + 168f, w, 22f), "Esc → 菜单（继续 / 设置 / 退出关卡 / 退出游戏）", dim);

                if (TableSettings.ShowDebugInfo)
                    GUI.Label(new Rect(16f, y + 190f, w, 22f),
                              "物理：" + (interaction.PhysicsOn ? "开（受重力）" : "关（脚本控制）")
                              + "　　视角：" + (setup != null && setup.rig != null && setup.rig.IsFreeLook
                                                ? "自由转头中" : "固定机位"), dim);
                return;
            }

            GUI.Label(new Rect(16f, y, w, 24f), "拖动卡牌放到桌面中间的投放区", h1);
            GUI.Label(new Rect(16f, y + 28f, w, 22f), "一次可以放多张 —— 确认时效果会叠加发动", dim);
            GUI.Label(new Rect(16f, y + 48f, w, 22f), "放好后按「确认投放」或回车 —— 牌会被吸进罐子", dim);
            GUI.Label(new Rect(16f, y + 68f, w, 22f), "拖回手牌 = 反悔，可以重新挑", dim);
            GUI.Label(new Rect(16f, y + 88f, w, 22f), "右键单击卡牌 → 查看完整数据（Esc 关闭）", dim);
            GUI.Label(new Rect(16f, y + 108f, w, 22f), "右键拖动 / 中键拖动 → 原地转头（活动范围 120° 锥）", dim);
            GUI.Label(new Rect(16f, y + 128f, w, 22f), "1 / 2 / 3 固定视角　　4 自由视角　　G 开关物理", dim);
            GUI.Label(new Rect(16f, y + 148f, w, 22f), "Esc → 菜单（继续 / 设置 / 返回开场 / 退出游戏）", dim);

            if (TableSettings.ShowDebugInfo)
                GUI.Label(new Rect(16f, y + 170f, w, 22f),
                          "物理：" + (interaction.PhysicsOn ? "开（受重力）" : "关（脚本控制）")
                          + "　　视角：" + (setup != null && setup.rig != null && setup.rig.IsFreeLook
                                            ? "自由转头中" : "固定机位"), dim);
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

            const float w = 660f;
            float x = (Screen.width - w) * 0.5f;
            const float y = 14f;

            // 面板比旧的高：v2.1 要多显示刀片 / 附魔 / 目标素材这三行，
            // 下面还要塞四个按钮（启动 / 放置 / 结束回合 / 自动选目标）。
            // 254 = 8 + 30 + 22 + 24 + 22×4 + 24 + 4 + 34 + 44（"不能启动的原因"那行在最下面）。
            const float h = 254f;
            GUI.Box(new Rect(x, y, w, h), GUIContent.none, panelBox);

            float ty = y + 8f;

            // ── ① 模式 + 回合 / 行动机会 / 分数（最关键的一行放最上面）──
            GUI.Label(new Rect(x + 18f, ty, w - 36f, 30f),
                      "第 " + r.turnIndex + "/" + GameJam.Rules.LevelRun.TurnsPerLevel + " 回合"
                      + "　　行动机会 " + r.actionPoints + "/" + GameJam.Rules.LevelRun.ActionPointsPerTurn
                      + "　　总分 " + r.score + " / " + r.targetScore,
                      h1Panel);
            ty += 30f;

            // ── ② 模式 + 未识别规则警告 ──
            GUI.Label(new Rect(x + 18f, ty, w - 36f, 22f),
                      "规则模式：" + (TableSettings.UseRulesV21 ? "v2.1（本分支默认）" : "旧流程")
                      + "　｜　出牌不消耗行动机会，只有「启动破壁机」消耗 1 次 + 1 点刀片 H",
                      dimPanel);
            ty += 22f;

            DrawRulesWarning(x + 18f, ty, w - 36f, r);
            ty += 24f;

            // ── ②b 占位数值提示 ──
            //   卡表里 h/d/v 全 0 的卡会兜一套临时值（否则刀片 H=0、一进关卡就爆刀）。
            //   必须写出来 —— 不然玩家会把这套临时值当成策划定的数值。
            string ph = r.PlaceholderWarning();
            if (!string.IsNullOrEmpty(ph))
            {
                GUI.Label(new Rect(x + 18f, ty, w - 36f, 22f), ph, dimPanel);
                ty += 22f;
            }

            // ── ③ 刀片（H / V / 四种附魔层数）──
            GUI.Label(new Rect(x + 18f, ty, w - 36f, 22f),
                      "刀片：" + (r.blade != null ? r.blade.Describe() : "（无）")
                      + (r.blade != null && r.blade.V <= 0 ? "　← 得分加成 0（该核心卡的 V 就是 0）" : ""),
                      bodyPanel);
            ty += 22f;

            GUI.Label(new Rect(x + 18f, ty, w - 36f, 22f),
                      "附魔层数：" + (r.blade != null ? r.blade.layers.Describe() : "（无）")
                      + "　　本回合已启动 " + r.startsThisTurn + " 次"
                      + (r.startsThisTurn > 0 ? "（最后一次启动会触发献祭吞噬）" : ""),
                      bodyPanel);
            ty += 22f;

            // ── ④ 目标素材 ──
            GUI.Label(new Rect(x + 18f, ty, w - 36f, 22f),
                      "启动目标：" + r.TargetText,
                      r.selected != null ? bodyPanel : dimPanel);
            ty += 22f;

            // ── ⑤ 手牌 / 桌面 ──
            GUI.Label(new Rect(x + 18f, ty, w - 36f, 22f),
                      "手牌：素材 " + r.hand.Count + " 张｜法术 " + r.handSpells.Count + " 张"
                      + "　桌面素材 " + r.LiveTableCount() + " 张"
                      + (turnLoop.StagedCount > 0 ? "　投放区待放置 " + turnLoop.StagedText : ""),
                      dimPanel);
            ty += 24f;

            // ── ⑥ 按钮 ──
            float by = y + h - 46f;

            bool canActivate = r.CanActivate && r.selected != null && !r.selected.removed;

            bool oldEnabled = GUI.enabled;
            GUI.enabled = canActivate;

            if (GUI.Button(new Rect(x + 18f, by, 176f, 34f), "启动破壁机", btn))
            {
                GUI.FocusControl(null);
                turnLoop.ActivateJuicer();
            }

            GUI.enabled = oldEnabled;

            if (GUI.Button(new Rect(x + 202f, by, 154f, 34f), "放置到桌面", btn))
            {
                GUI.FocusControl(null);
                turnLoop.Confirm();     // v2.1 下这一条 = 出牌（不消耗行动机会）
            }

            if (GUI.Button(new Rect(x + 364f, by, 132f, 34f), "结束本回合", btn))
            {
                GUI.FocusControl(null);
                turnLoop.EndRoundOrLevel();
            }

            if (GUI.Button(new Rect(x + 504f, by, 138f, 34f), "自动选目标", btn))
            {
                GUI.FocusControl(null);
                turnLoop.SelectNewestTarget();
            }

            // 不能启动的原因直接写出来 —— 按钮灰着却不解释，玩家只会以为是 bug
            if (!canActivate)
                GUI.Label(new Rect(x + 18f, by - 20f, w - 36f, 20f),
                          "⚠ " + r.BlockReason, dimPanel);
        }

        /// <summary>
        /// 未实现规则的醒目提示。
        ///
        /// 【这条提示的存在意义】"没实现"最坏的样子不是缺功能，而是**看起来实现了**。
        ///   所以只要解析报告里有未识别的句子，这一行就必须是醒目的，
        ///   而且要给一个能查到"是哪几句"的入口（F2）。
        ///   报告没建出来时也不能装作干净 —— 那会写成"报告没建出来"。
        /// </summary>
        private void DrawRulesWarning(float x, float y, float w, TableRulesV21 r)
        {
            int n = r.UnrecognizedCount;

            if (n < 0)
            {
                GUI.Label(new Rect(x, y, w, 22f), "⚠ 卡表解析报告没建出来 —— 无法确认哪些规则没实现", dimPanel);
                return;
            }

            if (n == 0)
            {
                GUI.Label(new Rect(x, y, w, 22f),
                          "✓ 卡表规则文本全部已识别（" + (r.report != null ? r.report.ParsedSentences + " 句" : "") + "）",
                          dimPanel);
                return;
            }

            // 用暖红色，和别的灰字拉开 —— 这是"别信这一条"的信号
            Color prev = GUI.color;
            GUI.color = new Color(1f, 0.62f, 0.42f);
            GUI.Label(new Rect(x, y, w, 22f),
                      "⚠ " + n + " 条规则未实现（这些规则不会生效），按 F2 查看", bodyPanel);
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

                const float vw = 520f, vh = 176f;
                Rect vr = CenterBox(vw, vh);
                GUI.Box(vr, GUIContent.none, panelBox);

                GUI.Label(new Rect(vr.x + 22f, vr.y + 18f, vw - 44f, 34f),
                          r.bursted ? "爆　刀 —— 关卡结束" : "关卡结束", h1Panel);

                GUI.Label(new Rect(vr.x + 22f, vr.y + 58f, vw - 44f, 22f),
                          "结束原因：" + (string.IsNullOrEmpty(r.endReason) ? "（未记录）" : r.endReason),
                          bodyPanel);

                GUI.Label(new Rect(vr.x + 22f, vr.y + 82f, vw - 44f, 22f),
                          "最终分数：" + r.score + " / 目标分 " + r.targetScore
                          + "　（爆刀时当前分数 ×2，正文 §五）",
                          bodyPanel);

                GUI.Label(new Rect(vr.x + 22f, vr.y + 106f, vw - 44f, 22f),
                          "刀片：" + (r.blade != null ? r.blade.Describe() : "（无）"), dimPanel);

                if (GUI.Button(new Rect(vr.x + 22f, vr.y + vh - 54f, vw - 44f, 40f), "进入结算", btn))
                {
                    GUI.FocusControl(null);
                    turnLoop.ShowLevelResult();
                }
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
        /// </summary>
        private void DrawTurnResultV21()
        {
            TableRulesV21 r = turnLoop.rulesV21;
            if (r == null) return;

            const float w = 660f, h = 430f;
            Rect box = CenterBox(w, h);
            GUI.Box(box, GUIContent.none, panelBox);

            GUI.Label(new Rect(box.x + 22f, box.y + 14f, w - 44f, 32f),
                      "第 " + r.turnIndex + " 回合" + (r.levelOver ? "（关卡已结束）" : "进行中"), h1Panel);

            float y = box.y + 52f;

            GUI.Label(new Rect(box.x + 22f, y, w - 44f, 24f),
                      "总分：" + r.score + " / " + r.targetScore
                      + "　　刀片：" + (r.blade != null ? r.blade.Describe() : "（无）"), bodyPanel);
            y += 24f;

            GUI.Label(new Rect(box.x + 22f, y, w - 44f, 24f),
                      "附魔：" + (r.blade != null ? r.blade.layers.Describe() : "（无）")
                      + "　　行动机会 " + r.actionPoints + "/" + GameJam.Rules.LevelRun.ActionPointsPerTurn,
                      bodyPanel);
            y += 24f;

            GUI.Label(new Rect(box.x + 22f, y, w - 44f, 24f),
                      "本次启动：" + (string.IsNullOrEmpty(r.lastSummary) ? "（这一回合还没启动过）" : r.lastSummary),
                      dimPanel);
            y += 24f;

            GUI.Label(new Rect(box.x + 22f, y, w - 44f, 24f),
                      "桌面素材：" + r.LiveTableCount() + " 张　　剩余手牌：" + r.HandText(), dimPanel);
            y += 30f;

            // ── 引擎日志尾部（结算顺序照贴，方便一条条对）──
            GUI.Label(new Rect(box.x + 22f, y, w - 44f, 22f), "── 上一次结算的引擎日志 ──", bodyPanel);
            y += 24f;

            List<string> tail = r.LastLogTail(12);
            for (int i = 0; i < tail.Count; i++)
            {
                GUI.Label(new Rect(box.x + 26f, y, w - 48f, 20f), tail[i], dimPanel);
                y += 19f;
            }
            if (tail.Count == 0)
                GUI.Label(new Rect(box.x + 26f, y, w - 48f, 20f), "（还没有启动过 —— 点顶栏的「启动破壁机」）", dimPanel);

            // ── 按钮 ──
            bool over = r.levelOver || r.turnIndex >= GameJam.Rules.LevelRun.TurnsPerLevel;
            string label = over ? "结束本关" : "下一回合";

            if (GUI.Button(new Rect(box.x + 22f, box.y + h - 56f, 220f, 42f), label, btn))
            {
                GUI.FocusControl(null);
                turnLoop.NextTurn();
            }

            if (GUI.Button(new Rect(box.x + w - 242f, box.y + h - 56f, 220f, 42f), "继续操作桌面", btn))
            {
                GUI.FocusControl(null);
                turnLoop.phase = TablePhase.Select;
            }
        }

        /// <summary>v2.1 的关卡总结算。列出 v2.1 真正有的东西（刀片 / 附魔 / 桌面 / 日志）。</summary>
        private void DrawLevelResultV21()
        {
            TableRulesV21 r = turnLoop.rulesV21;
            if (r == null) return;

            const float w = 660f, h = 420f;
            Rect box = CenterBox(w, h);
            GUI.Box(box, GUIContent.none, panelBox);

            GUI.Label(new Rect(box.x + 22f, box.y + 14f, w - 44f, 34f),
                      turnLoop.level.Name + "　总分 " + r.score + " / 目标分 " + r.targetScore
                      + "　" + (turnLoop.Passed ? "通过" : "未通过"), h1Panel);

            float y = box.y + 54f;

            GUI.Label(new Rect(box.x + 22f, y, w - 44f, 22f),
                      "结束原因：" + (string.IsNullOrEmpty(r.endReason) ? "（未记录）" : r.endReason), bodyPanel);
            y += 24f;

            GUI.Label(new Rect(box.x + 22f, y, w - 44f, 22f),
                      "刀片：" + (r.blade != null ? r.blade.Describe() : "（无）"), bodyPanel);
            y += 24f;

            GUI.Label(new Rect(box.x + 22f, y, w - 44f, 22f),
                      "最终附魔：" + (r.blade != null ? r.blade.layers.Describe() : "（无）"), bodyPanel);
            y += 24f;

            GUI.Label(new Rect(box.x + 22f, y, w - 44f, 22f),
                      "打过的回合：" + r.turnIndex + "/" + GameJam.Rules.LevelRun.TurnsPerLevel
                      + "　桌面残留素材：" + r.LiveTableCount() + " 张", dimPanel);
            y += 26f;

            // 默认放行要说出来，不然"0 分也算通过"看着像 bug
            if (turnLoop.level.passByDefault)
            {
                GUI.Label(new Rect(box.x + 22f, y, w - 44f, 22f),
                          "（Level.passByDefault 还开着：没到目标分也算通过）", dimPanel);
                y += 22f;
            }

            if (r.warnings.Count > 0)
            {
                GUI.Label(new Rect(box.x + 22f, y, w - 44f, 22f),
                          "⚠ 本关有 " + r.warnings.Count + " 条引擎警告（卡表缺产物之类），F2 看报告", dimPanel);
            }

            string label = turnLoop.Passed
                ? (turnLoop.HasNextLevel ? "进入下一关" : "已是最后一关")
                : "重试本关";

            if (GUI.Button(new Rect(box.x + 22f, box.y + h - 56f, 230f, 40f), label, btn))
            {
                GUI.FocusControl(null);
                if (turnLoop.Passed) turnLoop.NextLevel();
                else                 turnLoop.RestartLevel();
            }

            if (GUI.Button(new Rect(box.x + w - 230f, box.y + h - 56f, 208f, 40f), "返回关卡界面", btn))
            {
                GUI.FocusControl(null);
                turnLoop.OpenLevelSelect();
            }
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
        /// </summary>
        private void DrawRulesReportPanel()
        {
            TableRulesV21 r = turnLoop != null ? turnLoop.rulesV21 : null;

            float w = Mathf.Min(940f, Screen.width - 40f);
            float h = Mathf.Min(640f, Screen.height - 40f);
            Rect box = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);

            GUI.Box(box, GUIContent.none, panelBox);

            GUI.Label(new Rect(box.x + 20f, box.y + 12f, w - 180f, 32f),
                      "卡牌规则解析报告（v2.1）", h1Panel);

            if (GUI.Button(new Rect(box.x + w - 130f, box.y + 12f, 112f, 30f), "关闭 (F2)", btn))
            {
                GUI.FocusControl(null);
                rulesReportOpen = false;
            }

            if (r == null || r.report == null)
            {
                GUI.Label(new Rect(box.x + 20f, box.y + 56f, w - 40f, 24f),
                          "⚠ 报告没建出来 —— 现在**无法确认**哪些规则没实现。"
                          + "先看 Console 里 [V21] 那条警告。", bodyPanel);
                return;
            }

            GUI.Label(new Rect(box.x + 20f, box.y + 54f, w - 40f, 24f),
                      r.report.SummaryLine(), bodyPanel);

            // 只读文字：用 ScrollView 包（这里不是 GUILayout.Window，没有窗口自动适配的问题）
            Rect view = new Rect(box.x + 16f, box.y + 84f, w - 32f, h - 100f);
            rulesReportScroll = GUI.BeginScrollView(view, rulesReportScroll, new Rect(0f, 0f, w - 56f, 4600f));

            float y = 0f;
            y = ReportSection(y, w - 60f,
                              "未识别清单（这些句子不会生效 —— 必须给策划确认）",
                              UnrecognizedLines(r));

            y = ReportSection(y, w - 60f, "规则表接不上（标签标了、连锁产物没写：命中却不变形）",
                              r.report.tableGaps);

            y = ReportSection(y, w - 60f, "可疑产出（产出的卡名不在卡表里）",
                              r.report.unknownProducts);

            y = ReportSection(y, w - 60f, "占位数值（正文没给数，用了默认值）",
                              r.report.placeholders);

            y = ReportSection(y, w - 60f, "v3 已取消、卡表里还留着的 v2.1 写法（写了也不生效）",
                              r.report.superseded);

            y = ReportSection(y, w - 60f, "当前模型执行不了的条目", r.report.unsupported);

            y = ReportSection(y, w - 60f, "本关引擎打过的警告（运行时）", r.warnings);

            ReportSection(y, w - 60f, "正文自相矛盾 / 没写清（已按一个明确选择实现，待策划拍板）",
                          r.report.conflicts);

            GUI.EndScrollView();
        }

        private List<string> UnrecognizedLines(TableRulesV21 r)
        {
            List<string> lines = new List<string>();
            for (int i = 0; i < r.report.unrecognized.Count; i++)
            {
                GameJam.Rules.UnrecognizedRule u = r.report.unrecognized[i];
                if (u == null) continue;

                lines.Add(u.cardName + "·" + u.field + "　原句：" + u.sentence
                          + "　→ " + u.reason
                          + (string.IsNullOrEmpty(u.suggestion) ? "" : "（建议：" + u.suggestion + "）"));
            }
            return lines;
        }

        /// <summary>报告里的一节。返回下一节的起始 y（自增式排版，免得每节都算一遍偏移）。</summary>
        private float ReportSection(float y, float w, string title, List<string> lines)
        {
            int n = lines != null ? lines.Count : 0;

            GUI.Label(new Rect(8f, y, w, 24f), "── " + title + "：" + n + " ──", h1Panel);
            y += 26f;

            if (n == 0)
            {
                GUI.Label(new Rect(20f, y, w - 20f, 20f), "（无）", dimPanel);
                y += 22f;
            }
            else
            {
                for (int i = 0; i < n; i++)
                {
                    GUI.Label(new Rect(20f, y, w - 20f, 40f), "· " + lines[i], dimPanel);
                    y += 40f;
                }
            }

            return y + 10f;
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

        /// <summary>卡牌现在在哪 —— 检视面板上要显示。</summary>
        private string PositionText(PlayCard card)
        {
            if (card.IsDragging) return "拿在手上";
            if (card.slotIndex >= 0) return "投放区（待确认）";
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
