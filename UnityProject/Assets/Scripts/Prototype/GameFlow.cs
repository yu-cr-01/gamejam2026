using System.Collections.Generic;
using UnityEngine;
using GameJam.Data;

namespace GameJam.Prototype
{
    /// <summary>游戏阶段。</summary>
    public enum GameFlowState
    {
        MainMenu,
        /// <summary>通用选择环节 —— 关卡声明几个就跑几个，不再为每种选择各写一个状态</summary>
        Choice,
        TurnStart,
        Simulating,
        TurnResult,
        LevelEnd,
        LevelResult,
    }

    /// <summary>
    /// 骨架原型：只有文字和按钮，没有图片、动画、音效。
    ///
    /// 【这一版的关键改动】
    /// 旧版状态机里写死了 DeckSelect / BladeSelect 两个状态，
    /// 换成"五选二食材"就得改状态机。
    ///
    /// 现在只有一个通用的 Choice 状态：它按顺序遍历 LevelData.choices，
    /// 每个选择跑完记进 SelectionLog，全部跑完后才由"选择记录"反推出
    /// 本局要用哪副牌组、哪个刀片 —— 状态机不需要认识"牌组"这个概念。
    ///
    /// 用 IMGUI 而不是 UGUI：不需要 Canvas / Prefab / 场景文件，空场景按 Play 就能跑。
    /// </summary>
    [DisallowMultipleComponent]
    public class GameFlow : MonoBehaviour
    {
        // ── 运行时状态 ────────────────────────────────────────────────
        public GameFlowState State { get; private set; }

        private List<Deck> decks;
        private LevelData level;
        private TurnState turn;
        private SelectionLog selections;

        private int choiceIndex;          // 当前跑到第几个选择环节
        private int highlightIndex;       // 当前选中项（键盘光标 / 鼠标悬停 / 点击 都改它）
        private List<string> pendingPicks = new List<string>();  // 本环节已勾选项（多选时用）
        private string lastPlayed = "";

        // 鼠标悬停判定用：IMGUI 拿不到"当前帧的矩形"，只能记录上一帧的，
        // 这是 IMGUI 里做 hover 的标准做法。
        private Rect[] optionRects = new Rect[0];
        private Vector2 lastMousePos = new Vector2(-1f, -1f);
        private bool mouseMoved;
        private Vector2 posAtLastKey = new Vector2(-999f, -999f);   // 上次按 ↑↓ 时的鼠标位置
        private bool showDebug = true;                              // 调试显示，排查完关掉

        // ── 界面样式 ──────────────────────────────────────────────────
        private Font cjkFont;
        private GUIStyle h1, h2, body, btn, btnPicked, dim;
        private Vector2 scroll;

        // ─────────────────────────────────────────────────────────────
        void Awake() { ResetAll(); }

        public void ResetAll()
        {
            decks = FakeData.BuildDecks();
            level = FakeData.BuildLevel(decks);
            turn = new TurnState();
            selections = new SelectionLog();
            choiceIndex = 0;
            highlightIndex = 0;
            pendingPicks.Clear();
            lastPlayed = "";
            State = GameFlowState.MainMenu;
            scroll = Vector2.zero;
        }

        private void GoTo(GameFlowState next)
        {
            State = next;
            scroll = Vector2.zero;
        }

        private Choice CurrentChoice { get { return level != null ? level.ChoiceAt(choiceIndex) : null; } }

        // ── 选择流程 ──────────────────────────────────────────────────

        /// <summary>确认当前选择环节，然后进入下一个环节。多选时传多个 id。</summary>
        private void ConfirmChoice(params string[] optionIds)
        {
            Choice c = CurrentChoice;
            if (c == null) return;

            selections.Record(c.id, 0, optionIds);
            pendingPicks.Clear();
            choiceIndex++;

            if (choiceIndex >= level.ChoiceCount)
            {
                StartLevelFromSelections();
                GoTo(GameFlowState.TurnStart);
            }
            else
            {
                ResetHighlightForCurrent();   // 换到下一个选择环节，光标归位
                GoTo(GameFlowState.Choice);
            }
        }

        /// <summary>
        /// 把 ↑↓ 光标放到当前选择环节的默认项上；
        /// 没配默认项就放第一项。
        /// </summary>
        private void ResetHighlightForCurrent()
        {
            highlightIndex = 0;
            Choice c = CurrentChoice;
            if (c == null || c.options == null) return;

            ChoiceOption def = c.DefaultOption;
            if (def == null) return;

            for (int i = 0; i < c.options.Count; i++)
            {
                if (c.options[i] == def) { highlightIndex = i; break; }
            }
        }

        /// <summary>
        /// 处理键盘：↑↓ 只移动光标，Enter / 空格 才确认。
        ///
        /// 返回 true 表示这次按键产生了状态变化。
        /// 注意 DrawChoice 拿到返回值也不会提前 return —— 提前返回会让本帧绘制的
        /// 元素数量和 Layout/Repaint 不一致，IMGUI 会报 GUILayout 不匹配。
        /// </summary>
        private bool HandleChoiceKeys(Choice c)
        {
            Event e = Event.current;
            if (e == null || c == null || c.options == null) return false;

            int n = c.options.Count;
            if (n == 0) return false;

            if (e.type != EventType.KeyDown) return false;

            if (e.keyCode == KeyCode.UpArrow)
            {
                highlightIndex = (highlightIndex - 1 + n) % n;   // 到顶就回到底部
                posAtLastKey = e.mousePosition;                  // 记住此刻鼠标位置，
                e.Use();                                         // 鼠标不动就不让悬停把选中拽回去
                return true;
            }
            if (e.keyCode == KeyCode.DownArrow)
            {
                highlightIndex = (highlightIndex + 1) % n;
                posAtLastKey = e.mousePosition;
                e.Use();
                return true;
            }
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter ||
                e.keyCode == KeyCode.Space)
            {
                e.Use();

                if (c.pickCount <= 1)
                {
                    // 单选：确认光标所在项
                    if (highlightIndex < 0 || highlightIndex >= n) return false;
                    ChoiceOption o = c.options[highlightIndex];
                    if (o != null) ConfirmChoice(o.id);
                }
                else if (pendingPicks.Count == c.pickCount)
                {
                    // 多选：勾满才允许确认
                    ConfirmChoice(pendingPicks.ToArray());
                }
                return true;
            }
            return false;
        }

        /// <summary>点一个候选项。单选=选中并立即确认；多选=切换勾选，选满自动确认。</summary>
        private void OnOptionClicked(Choice c, ChoiceOption o)
        {
            if (c.pickCount <= 1)
            {
                ConfirmChoice(o.id);
                return;
            }

            if (pendingPicks.Contains(o.id)) pendingPicks.Remove(o.id);
            else if (pendingPicks.Count < c.pickCount) pendingPicks.Add(o.id);

            // 选满就自动进入下一环节，不需要再点确认按钮
            if (pendingPicks.Count == c.pickCount) ConfirmChoice(pendingPicks.ToArray());
        }

        /// <summary>
        /// 所有选择跑完后，从选择记录里反推本局配置。
        /// 这里不硬编码 "deck_pick" —— 只找"被选中的、带牌组的选项"。
        /// 所以关卡想改成第 3 个环节才选牌组，这段代码也不用动。
        /// </summary>
        private void StartLevelFromSelections()
        {
            turn.StartLevel(level, ResolvePickedDeck(), ResolvePickedBlade());
        }

        private Deck ResolvePickedDeck()
        {
            foreach (Choice c in level.choices)
            {
                List<string> picks = selections.Picks(c.id);
                for (int i = 0; i < picks.Count; i++)
                {
                    ChoiceOption o = c.Find(picks[i]);
                    if (o != null && o.deck != null) return o.deck;
                }
            }
            return decks.Count > 0 ? decks[0] : null;   // 兜底
        }

        private Ingredient ResolvePickedBlade()
        {
            foreach (Choice c in level.choices)
            {
                List<string> picks = selections.Picks(c.id);
                for (int i = 0; i < picks.Count; i++)
                {
                    ChoiceOption o = c.Find(picks[i]);
                    if (o != null && o.ingredient != null) return o.ingredient;
                }
            }
            return FakeData.DefaultBlade();              // 兜底：默认铁块
        }

        // ── 样式 / 中文字体 ───────────────────────────────────────────
        private void EnsureStyles()
        {
            if (cjkFont == null)
            {
                string[] candidates =
                {
                    "Microsoft YaHei", "微软雅黑", "SimHei", "SimSun",
                    "PingFang SC", "Noto Sans CJK SC", "Arial Unicode MS"
                };
                cjkFont = Font.CreateDynamicFontFromOSFont(candidates, 20);
            }

            if (h1 == null)
            {
                h1 = new GUIStyle(GUI.skin.label)  { fontSize = 30, fontStyle = FontStyle.Bold, wordWrap = true };
                h2 = new GUIStyle(GUI.skin.label)  { fontSize = 21, fontStyle = FontStyle.Bold, wordWrap = true };
                body = new GUIStyle(GUI.skin.label){ fontSize = 17, wordWrap = true };
                dim  = new GUIStyle(GUI.skin.label){ fontSize = 15, wordWrap = true };
                dim.normal.textColor = new Color(0.62f, 0.66f, 0.74f);

                btn = new GUIStyle(GUI.skin.button) { fontSize = 18, wordWrap = true,
                                                      alignment = TextAnchor.MiddleLeft,
                                                      padding = new RectOffset(16, 16, 8, 8) };
                btnPicked = new GUIStyle(btn);
                btnPicked.normal.textColor = new Color(0.35f, 0.95f, 0.60f);

                if (cjkFont != null)
                {
                    h1.font = cjkFont; h2.font = cjkFont; body.font = cjkFont;
                    dim.font = cjkFont; btn.font = cjkFont; btnPicked.font = cjkFont;
                }
            }
        }

        private static string StateLabel(GameFlowState s)
        {
            switch (s)
            {
                case GameFlowState.MainMenu:    return "主菜单";
                case GameFlowState.Choice:      return "选择环节";
                case GameFlowState.TurnStart:   return "回合开始";
                case GameFlowState.Simulating:  return "模拟中";
                case GameFlowState.TurnResult:  return "回合结算";
                case GameFlowState.LevelEnd:    return "关卡结束";
                case GameFlowState.LevelResult: return "关卡结算";
                default:                        return s.ToString();
            }
        }

        // ─────────────────────────────────────────────────────────────
        void OnGUI()
        {
            EnsureStyles();

            float w = Mathf.Max(560f, Screen.width - 60f);
            float h = Mathf.Max(400f, Screen.height - 60f);
            GUILayout.BeginArea(new Rect(30f, 30f, w, h));
            scroll = GUILayout.BeginScrollView(scroll);

            GUILayout.Label("【当前阶段】" + StateLabel(State)
                            + (State == GameFlowState.Choice && level != null
                               ? "　（第 " + (choiceIndex + 1) + " / " + level.ChoiceCount + " 个）" : ""), h2);
            GUILayout.Space(14f);

            switch (State)
            {
                case GameFlowState.MainMenu:    DrawMainMenu();    break;
                case GameFlowState.Choice:      DrawChoice();      break;
                case GameFlowState.TurnStart:   DrawTurnStart();   break;
                case GameFlowState.Simulating:  DrawSimulating();  break;
                case GameFlowState.TurnResult:  DrawTurnResult();  break;
                case GameFlowState.LevelEnd:    DrawLevelEnd();    break;
                case GameFlowState.LevelResult: DrawLevelResult(); break;
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ── 各阶段界面 ────────────────────────────────────────────────

        private void DrawMainMenu()
        {
            GUILayout.Label("主菜单", h1);
            GUILayout.Label("骨架原型：只验证流程，数值和结算都是假的。", body);
            GUILayout.Space(10f);
            GUILayout.Label("关卡：" + level.name + "　目标分 " + level.targetScore
                            + "　选择环节 " + level.ChoiceCount + " 个", dim);
            GUILayout.Space(20f);

            if (GUILayout.Button("开始游戏", btn, GUILayout.Height(52f)))
            {
                choiceIndex = 0;
                pendingPicks.Clear();
                GUI.FocusControl(null);
                ResetHighlightForCurrent();
                GoTo(GameFlowState.Choice);
            }
        }

        /// <summary>
        /// 通用选择界面。
        ///
        /// 选中：↑↓ 移动 / 鼠标悬停 / 鼠标点击 —— 三者改的都是同一个 highlightIndex，
        ///       也就是"鼠标挪到哪就选中哪，不用再按一次才选中"。
        /// 确认：Enter 或「下一步」按钮。
        /// </summary>
        private void DrawChoice()
        {
            Choice c = CurrentChoice;
            if (c == null) { GUILayout.Label("（没有更多选择环节）", body); return; }

            bool multi = c.pickCount > 1;
            int n = c.options.Count;

            // ── 鼠标是否移动过 ──
            // 只在 Repaint 事件里比较一次鼠标位置。
            Vector2 mp = Event.current.mousePosition;
            if (Event.current.type == EventType.Repaint)
            {
                mouseMoved = (mp != lastMousePos);
                lastMousePos = mp;
            }

            // ── 悬停改选中 ──
            // 用上一帧记录的矩形判定（IMGUI 在 Layout 阶段拿不到本帧矩形）。
            // 只有鼠标确实移动过、且不是"刚按完 ↑↓ 还没动鼠标"时才生效 ——
            // 否则鼠标停在某一项上不动时，悬停判定会每帧把选中拽回去，键盘 ↑↓ 就失效了。
            bool mouseActive = mouseMoved && mp != posAtLastKey;
            if (mouseActive && Event.current.type != EventType.Layout && optionRects.Length == n)
            {
                for (int i = 0; i < n; i++)
                {
                    if (optionRects[i].Contains(mp))
                    {
                        if (highlightIndex != i) highlightIndex = i;
                        break;
                    }
                }
            }

            // ── 临时调试显示（排查悬停失效用，定位后删掉）──
            if (showDebug)
            {
                string ri = "";
                for (int i = 0; i < optionRects.Length; i++)
                    ri += " [" + i + "]" + optionRects[i].x.ToString("F0") + "," + optionRects[i].y.ToString("F0")
                        + " " + optionRects[i].width.ToString("F0") + "x" + optionRects[i].height.ToString("F0");
                GUILayout.Label("DBG evt=" + Event.current.type
                                + "　mouse=" + mp.x.ToString("F0") + "," + mp.y.ToString("F0")
                                + "　moved=" + mouseMoved
                                + "　active=" + mouseActive
                                + "　hl=" + highlightIndex
                                + "　rects=" + ri, dim);
            }

            GUILayout.Label(c.prompt, h1);
            if (!string.IsNullOrEmpty(c.hint)) GUILayout.Label(c.hint, dim);

            // 键盘只移动光标，不直接跳转 —— 跳转交给确认动作
            HandleChoiceKeys(c);

            GUILayout.Label(multi
                ? "鼠标移到哪就选哪　↑↓ 移动　　□ 勾选，选满 " + c.pickCount + " 项自动继续"
                : "鼠标移到哪就选哪　↑↓ 移动　　点击 / Enter 确认", dim);
            GUILayout.Space(14f);

            if (optionRects.Length != n) optionRects = new Rect[n];

            for (int i = 0; i < n; i++)
            {
                ChoiceOption o = c.options[i];
                if (o == null) continue;

                bool isChecked = pendingPicks.Contains(o.id);
                bool isCursor  = (i == highlightIndex);

                // 单选：光标即选中，用 ●/○
                // 多选：光标和勾选是两件事，用 ■/□ 表示勾选，光标靠绿色区分
                string mark = multi ? (isChecked ? "■ " : "□ ")
                                    : (isCursor  ? "● " : "○ ");

                string label = mark + o.title
                               + (string.IsNullOrEmpty(o.subtitle) ? "" : "\n　　" + o.subtitle);

                GUIStyle st = (multi ? isChecked : isCursor) ? btnPicked : btn;

                float hgt = string.IsNullOrEmpty(o.subtitle) ? 46f : 64f;
                Rect r = GUILayoutUtility.GetRect(0f, hgt, GUILayout.ExpandWidth(true));

                // ★ 只在 Repaint 事件里记录矩形。
                // Layout 阶段 GetRect 返回的是占位值 —— 带 ExpandWidth 时宽度还没解析，是 0，
                // 坐标也是 0。记录下来会让悬停判定永远命中不了（三个矩形全是 0x64）。
                if (Event.current.type == EventType.Repaint) optionRects[i] = r;

                if (GUI.Button(r, label, st))
                {
                    highlightIndex = i;
                    GUI.FocusControl(null);      // 清掉按钮焦点，否则 Space/Enter 会二次触发
                    OnOptionClicked(c, o);
                    return;
                }
            }

            // 不再提供「下一步」按钮 ——
            // 单选：点一下就确认进入下一环节
            // 多选：选满自动进入下一环节
            // 这里只留一行状态提示。
            if (multi && pendingPicks.Count < c.pickCount)
            {
                GUILayout.Space(10f);
                GUILayout.Label("还需选 " + (c.pickCount - pendingPicks.Count) + " 项，选满自动继续", dim);
            }
        }

        private void DrawTurnStart()
        {
            GUILayout.Label("回合 " + turn.turnNumber + "，手牌：" + turn.HandNames(), h1);
            GUILayout.Label("刀片：" + turn.BladeName() + "（实际硬度 " + turn.BladeHardness() + "）"
                            + "　　杯内：" + turn.CupNames()
                            + "　　得分：" + turn.score, body);
            GUILayout.Space(14f);

            if (turn.hand.Count == 0)
            {
                GUILayout.Label("手牌已空。", body);
                GUILayout.Space(10f);
                if (GUILayout.Button("下一回合", btn, GUILayout.Height(52f))) GoTo(GameFlowState.LevelEnd);
                return;
            }

            GUILayout.Label("点击手牌打出：", h2);
            GUILayout.Space(6f);

            for (int i = 0; i < turn.hand.Count; i++)
            {
                Ingredient ing = turn.hand[i];
                string label = ing.name + "\n　　" + ing.attrs.DescribeAll();
                if (GUILayout.Button(label, btn, GUILayout.Height(64f)))
                {
                    Ingredient played = turn.PlayFromHand(i);
                    lastPlayed = played != null ? played.name : "";
                    GoTo(GameFlowState.Simulating);
                    return;
                }
            }

            GUILayout.Space(14f);
            DrawDataPanel();
        }

        private void DrawSimulating()
        {
            GUILayout.Label("模拟中…", h1);
            GUILayout.Label("本回合打出：" + lastPlayed, body);
            GUILayout.Label("今天不做真实模拟，点一下直接进结算。", dim);
            GUILayout.Space(24f);

            if (GUILayout.Button("回合结束", btn, GUILayout.Height(52f))) GoTo(GameFlowState.TurnResult);
        }

        private void DrawTurnResult()
        {
            GUILayout.Label("回合结算，得分 " + turn.score, h1);
            GUILayout.Label("本回合得分固定为 0（今天不做数值）。", body);
            GUILayout.Label("剩余手牌：" + turn.HandNames(), body);
            GUILayout.Label("杯内食材：" + turn.CupNames(), body);
            GUILayout.Space(18f);

            if (turn.IsHandEmpty) GUILayout.Label("手牌已空 —— 再点一次将结束本关。", body);

            if (GUILayout.Button("下一回合", btn, GUILayout.Height(52f)))
            {
                if (turn.IsHandEmpty) GoTo(GameFlowState.LevelEnd);
                else { turn.NextTurn(); GoTo(GameFlowState.TurnStart); }
            }
        }

        private void DrawLevelEnd()
        {
            GUILayout.Label("关卡结束", h1);
            GUILayout.Label("手牌已空。", body);
            GUILayout.Space(24f);
            if (GUILayout.Button("查看结算", btn, GUILayout.Height(52f))) GoTo(GameFlowState.LevelResult);
        }

        private void DrawLevelResult()
        {
            bool passed = turn.score >= turn.targetScore;
            GUILayout.Label("总分 " + turn.score + " / 目标分 " + turn.targetScore
                            + "，" + (passed ? "通过" : "失败"), h1);
            GUILayout.Space(18f);

            DrawSelectionLog();

            GUILayout.Space(18f);
            if (GUILayout.Button("回主菜单", btn, GUILayout.Height(52f))) ResetAll();
        }

        // ── 数据面板（演示解耦后的结构）───────────────────────────────

        private void DrawDataPanel()
        {
            GUILayout.Label("── 当前数据 ──", h2);

            Deck d = ResolvePickedDeck();
            if (d != null)
            {
                GUILayout.Label("牌组：" + d.name + "　（" + d.DescribeIngredients() + "）", body);
                for (int i = 0; i < d.modules.Count; i++)
                {
                    SpeedModule m = d.modules[i];
                    GUILayout.Label("　变速模块：" + m.name + "　效果组合 → " + m.Description(), body);
                }
            }

            if (!turn.activeEffects.IsEmpty)
                GUILayout.Label("本局效果组合：" + turn.activeEffects.Describe(), body);
        }

        private void DrawSelectionLog()
        {
            GUILayout.Label("── 选择记录（SelectionLog）──", h2);
            if (selections.Count == 0) { GUILayout.Label("（无）", dim); return; }

            for (int i = 0; i < selections.records.Count; i++)
            {
                ChoiceRecord r = selections.records[i];
                if (r == null) continue;
                Choice c = level.FindChoice(r.choiceId);
                string names = "";
                for (int k = 0; k < r.pickedOptionIds.Count; k++)
                {
                    ChoiceOption o = c != null ? c.Find(r.pickedOptionIds[k]) : null;
                    if (k > 0) names += "、";
                    names += (o != null ? o.title : r.pickedOptionIds[k]);
                }
                GUILayout.Label("　" + (c != null ? c.prompt : r.choiceId) + " → " + names, body);
            }
        }
    }
}
