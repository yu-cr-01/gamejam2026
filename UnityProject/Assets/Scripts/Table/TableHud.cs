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

            HandleConfirmKey();
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
                case TablePhase.Title:     turnLoop.ConfirmTitleStart(); break;
                case TablePhase.DeckPick:  turnLoop.ConfirmDeckPick();  break;
                case TablePhase.BladePick: turnLoop.ConfirmBladePick(); break;

                default:
                    if (turnLoop.CanInteract && turnLoop.Staged != null) turnLoop.Confirm();
                    break;
            }
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
            }
        }

        private void OnGUI()
        {
            EnsureStyles();
            if (setup == null || interaction == null) return;

            DrawViewButtons();

            // 开场 / 开局准备 / 正式回合是先后关系，三种信息栏不会同时出现
            if (turnLoop != null && turnLoop.IsTitle)         DrawTitleOverlay();
            else if (turnLoop != null && turnLoop.IsPreparing) DrawChoicePanel();
            else
            {
                DrawHints();
                DrawTurnPanel();
            }

            DrawPhaseOverlay();

            // 检视面板和划过信息条是同一个位置，二选一
            if (interaction.Inspected != null) DrawInspectPanel();
            else                               DrawCardInfo();
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
            const float w = 470f, h = 190f;
            float y = Screen.height - h - 14f;

            GUI.Label(new Rect(16f, y, w, 24f), "拖动卡牌放到桌面中间的投放区", h1);
            GUI.Label(new Rect(16f, y + 28f, w, 22f), "每回合只能投一张；再放一张会把上一张退回手牌", dim);
            GUI.Label(new Rect(16f, y + 48f, w, 22f), "放好后按「确认投放」或回车 —— 牌会被吸进罐子", dim);
            GUI.Label(new Rect(16f, y + 68f, w, 22f), "拖回手牌 = 反悔，可以重新挑", dim);
            GUI.Label(new Rect(16f, y + 88f, w, 22f), "右键单击卡牌 → 查看完整数据（Esc 关闭）", dim);
            GUI.Label(new Rect(16f, y + 108f, w, 22f), "右键拖动 / 中键拖动 → 原地转头（活动范围 120° 锥）", dim);
            GUI.Label(new Rect(16f, y + 128f, w, 22f), "1 / 2 / 3 固定视角　　4 自由视角　　G 开关物理", dim);
            GUI.Label(new Rect(16f, y + 150f, w, 22f),
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

            TurnState t = turnLoop.turn;

            const float w = 660f;
            float x = (Screen.width - w) * 0.5f;
            const float y = 14f;

            GUI.Box(new Rect(x, y, w, 126f), GUIContent.none, panelBox);

            GUI.Label(new Rect(x + 18f, y + 8f, w - 36f, 30f),
                      "第 " + t.turnNumber + " 回合　　得分 " + t.score + " / " + t.targetScore,
                      h1Panel);

            GUI.Label(new Rect(x + 18f, y + 40f, w - 36f, 24f),
                      "当前刀片：" + t.BladeName() + "　　" + t.BladeAttrLine(), bodyPanel);

            GUI.Label(new Rect(x + 18f, y + 64f, w - 36f, 22f),
                      "已应用模块：" + turnLoop.AppliedModulesText(), dimPanel);

            GUI.Label(new Rect(x + 18f, y + 90f, w - 200f, 26f),
                      "待投放：" + turnLoop.StagedText,
                      turnLoop.Staged != null ? bodyPanel : dimPanel);

            bool canConfirm = turnLoop.CanInteract && turnLoop.Staged != null;

            bool oldEnabled = GUI.enabled;
            GUI.enabled = canConfirm;

            if (GUI.Button(new Rect(x + w - 172f, y + 86f, 154f, 34f), "确认投放", btn))
            {
                GUI.FocusControl(null);
                turnLoop.Confirm();
            }

            GUI.enabled = oldEnabled;
        }

        // ══════════════════════════════════════════════════════════════
        //  阶段浮层：模拟中 / 回合结算 / 关卡结束 / 总结算
        // ══════════════════════════════════════════════════════════════

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

        /// <summary>模拟中 —— 纯过场，不接数值。</summary>
        private void DrawSimulating()
        {
            const float w = 470f, h = 158f;
            Rect r = CenterBox(w, h);
            GUI.Box(r, GUIContent.none, panelBox);

            GUI.Label(new Rect(r.x + 22f, r.y + 18f, w - 44f, 34f), "模拟中…", h1Panel);
            GUI.Label(new Rect(r.x + 22f, r.y + 58f, w - 44f, 26f),
                      "本回合投放：" + turnLoop.lastPlayed, bodyPanel);
            GUI.Label(new Rect(r.x + 22f, r.y + 90f, w - 44f, 50f),
                      "今天不做真实模拟 —— 得分、反应、爆刀都不算，\n等冲压动作走完自动进入回合结算。", dimPanel);
        }

        /// <summary>回合结算。</summary>
        private void DrawTurnResult()
        {
            TurnState t = turnLoop.turn;

            const float w = 580f, h = 262f;
            Rect r = CenterBox(w, h);
            GUI.Box(r, GUIContent.none, panelBox);

            GUI.Label(new Rect(r.x + 22f, r.y + 16f, w - 44f, 32f),
                      "第 " + t.turnNumber + " 回合结算", h1Panel);

            float y = r.y + 58f;
            GUI.Label(new Rect(r.x + 22f, y, w - 44f, 24f), "本回合得分：0　（今天不做数值）", bodyPanel); y += 24f;
            GUI.Label(new Rect(r.x + 22f, y, w - 44f, 24f),
                      "关卡总得分：" + t.score + " / " + t.targetScore, bodyPanel); y += 24f;
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
            const float w = 470f, h = 138f;
            Rect r = CenterBox(w, h);
            GUI.Box(r, GUIContent.none, panelBox);

            GUI.Label(new Rect(r.x + 22f, r.y + 20f, w - 44f, 34f), "手牌已用完，关卡结束", h1Panel);

            if (GUI.Button(new Rect(r.x + 22f, r.y + 76f, w - 44f, 42f), "进入结算", btn))
            {
                GUI.FocusControl(null);
                turnLoop.ShowLevelResult();
            }
        }

        private void DrawLevelResult()
        {
            TurnState t = turnLoop.turn;

            const float w = 580f, h = 246f;
            Rect r = CenterBox(w, h);
            GUI.Box(r, GUIContent.none, panelBox);

            GUI.Label(new Rect(r.x + 22f, r.y + 16f, w - 44f, 34f),
                      "总分 " + t.score + " / 目标分 " + t.targetScore + "　"
                      + (turnLoop.Passed ? "通过" : "未通过"),
                      h1Panel);

            float y = r.y + 62f;
            GUI.Label(new Rect(r.x + 22f, y, w - 44f, 24f), "刀片：" + t.BladeName(), bodyPanel); y += 26f;
            GUI.Label(new Rect(r.x + 22f, y, w - 44f, 24f), "　" + t.BladeAttrLine(), bodyPanel); y += 26f;
            GUI.Label(new Rect(r.x + 22f, y, w - 44f, 24f),
                      "已投放模块：" + turnLoop.AppliedModulesText(), bodyPanel); y += 26f;
            GUI.Label(new Rect(r.x + 22f, y, w - 44f, 44f), "杯内：" + t.CupNames(), dimPanel);
        }

        // ── 划过时的小信息条 ──────────────────────────────────────────
        private void DrawCardInfo()
        {
            PlayCard card = interaction.FocusCard;
            if (card == null) return;
            if (card.data == null && card.module == null) return;

            const float w = 320f;
            float h = 130f + AttrCatalog.Count * 22f;

            GUI.Box(new Rect(16f, 14f, w, h), GUIContent.none, panelBox);

            GUI.Label(new Rect(30f, 26f, w - 28f, 28f), card.DisplayName, h1Panel);

            float y = 60f;

            if (card.IsModule)
            {
                GUI.Label(new Rect(30f, y, w - 28f, 22f), "类型：变速模块", bodyPanel);
                GUI.Label(new Rect(30f, y + 24f, w - 28f, 44f),
                          "效果：" + card.module.Description(), bodyPanel);
                y += 72f;
            }
            else
            {
                Ingredient ing = card.data;
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
            if (card == null) return;
            if (card.data == null && card.module == null) return;

            // 两类牌的主题色来源不同：模块走冷色区，食材按 id 哈希取色
            Color accent = card.IsModule
                ? ProceduralArt.ModuleColor(card.module.id)
                : ProceduralArt.IngredientColor(card.data.id);

            // 模块没有"自己的属性"，印记固定给液体形；食材取数值最高的那个
            AttrId dominant = card.IsModule
                ? AttrId.Mercury
                : ProceduralArt.DominantAttr(card.data);

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
                GUILayout.Label(card.module.Description(), bodyPanel);
                GUILayout.Label("投出去之后效果并入本局，作用在当前刀片上；"
                                + "之后换刀片不会把这份加成带走。", dimPanel);
                GUILayout.EndVertical();
            }
            else
            {
                Ingredient ing = card.data;

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
