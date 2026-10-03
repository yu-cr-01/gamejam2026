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

        // 三选一牌组：展开详情的那张卡片下标，-1 表示还没展开
        private int deckDetailIndex = -1;

        // 刀片 + 手牌是否已经初始化过（进入"选刀片"环节时初始化一次）
        private bool loadoutReady;

        // 一次性提示条（"模块不能作为刀片"），到时间自动消失
        private string toast = "";
        private float toastUntil;

        // 鼠标悬停判定用：IMGUI 拿不到"当前帧的矩形"，只能记录上一帧的，
        // 这是 IMGUI 里做 hover 的标准做法。
        private Rect[] optionRects = new Rect[0];
        private Vector2 lastMousePos = new Vector2(-1f, -1f);
        private bool mouseMoved;
        private Vector2 posAtLastKey = new Vector2(-999f, -999f);   // 上次按 ↑↓ 时的鼠标位置
        private bool showDebug = false;                             // 调试显示，排查完关掉

        // ── 界面样式 ──────────────────────────────────────────────────
        private Font cjkFont;
        private GUIStyle h1, h2, body, btn, btnPicked, dim, toastStyle;
        private Vector2 scroll;

        // ─────────────────────────────────────────────────────────────
        void Awake() { ResetAll(); }

        public void ResetAll()
        {
            // 每次回主菜单都重读配置表 —— 策划改完 JSON 不用重启 Unity，
            // 退回主菜单再进一次就生效，调试期很省事。
            GameConfig.Reload();
            GameConfig.EnsureLoaded();

            decks = GameConfig.Decks();
            level = GameConfig.Level();
            turn = new TurnState();
            selections = new SelectionLog();
            choiceIndex = 0;
            highlightIndex = 0;
            pendingPicks.Clear();
            lastPlayed = "";
            deckDetailIndex = -1;
            loadoutReady = false;
            toast = "";
            toastUntil = 0f;
            State = GameFlowState.MainMenu;
            scroll = Vector2.zero;

            SkipChoicesForDebug();
        }

        /// <summary>
        /// 调试用：跳过牌组/刀片两个选择环节，直接进回合循环。
        ///
        /// 触发方式是在工程根目录放一个 Temp/skip_choices 空文件。
        /// 整段包在 UNITY_EDITOR 里，出包时会被编译器直接裁掉，正式版本不存在这个后门。
        ///
        /// 为什么要这么绕：验证"回合循环"这一屏必须先过两道鼠标点击的选择题，
        /// 而截图自动化点不了鼠标，环境变量也传不进一个已经在跑的编辑器进程。
        /// 用文件当开关，可以在编辑器不重启的前提下随时开、随时关。
        /// </summary>
        private static bool DebugSkipRequested()
        {
#if UNITY_EDITOR
            try
            {
                string root = System.IO.Path.Combine(Application.dataPath, "..");
                return System.IO.File.Exists(System.IO.Path.Combine(root, "Temp/skip_choices"));
            }
            catch { return false; }
#else
            return false;
#endif
        }

        private void SkipChoicesForDebug()
        {
            if (!DebugSkipRequested()) return;
            if (level == null || level.ChoiceCount == 0) return;

            // 牌组：取第一个候选。LevelData 只暴露 ChoiceAt/ChoiceCount，没有按 id 查的接口，
            // 这里自己走一遍就行 —— 反正是调试路径，不值得为它给数据层加方法。
            for (int i = 0; i < level.ChoiceCount; i++)
            {
                Choice c = level.ChoiceAt(i);
                if (c == null || c.id != GameConfig.DeckPickId) continue;
                if (c.options.Count > 0) selections.Record(c.id, 0, c.options[0].id);
                break;
            }

            // 刀片 + 手牌：按已记录的牌组准备
            EnsureLoadout();

            choiceIndex = level.ChoiceCount;
            State = GameFlowState.TurnStart;
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

            // 「选刀片」环节比较特殊：玩家已经在 turn 上把刀片换来换去，
            // 这里没有"候选项"可选，只需要把最终刀片记进选择记录然后前进。
            if (c.view == ChoiceView.BladeSwap)
            {
                selections.Record(c.id, 0,
                    turn.blade != null ? new string[] { turn.blade.id } : new string[0]);
                FinishChoiceFlow();
                return;
            }

            selections.Record(c.id, 0, optionIds);
            pendingPicks.Clear();
            choiceIndex++;

            if (choiceIndex >= level.ChoiceCount) FinishChoiceFlow();
            else
            {
                ResetHighlightForCurrent();   // 换到下一个选择环节，光标归位
                GoTo(GameFlowState.Choice);
            }
        }

        /// <summary>
        /// 全部选择跑完，进入回合循环。
        ///
        /// 如果刚跑的是「换刀片」环节，turn 里已经有玩家换好的刀片和手牌，
        /// 直接用；否则（关卡没有换刀片环节）从选择记录里反推后初始化一次。
        /// </summary>
        private void FinishChoiceFlow()
        {
            if (!loadoutReady)
            {
                turn.PrepareLoadout(level, ResolvePickedDeck(), ResolvePickedBlade());
                loadoutReady = true;
            }
            GoTo(GameFlowState.TurnStart);
        }

        /// <summary>
        /// 进入「选刀片」环节前把刀片和手牌准备好。
        /// 牌组里指定的那张牌装到刀片槽，其余进手牌 —— 手牌里没有铁块，
        /// 铁块只有在它当刀片被换下时才回到手上。
        /// </summary>
        private void EnsureLoadout()
        {
            if (loadoutReady) return;
            turn.PrepareLoadout(level, ResolvePickedDeck(), ResolvePickedBlade());
            loadoutReady = true;
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
        /// 从选择记录里反推玩家选了哪副牌组。
        /// 这里不硬编码 "deck_pick" —— 只找"被选中的、带牌组的选项"。
        /// 所以关卡想改成第 3 个环节才选牌组，这段代码也不用动。
        /// </summary>
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
            return GameConfig.DefaultBlade();            // 兜底：配置表里的默认刀片（铁块）
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

                // 一次性提示条（"模块不能作为刀片"）
                toastStyle = new GUIStyle(GUI.skin.box)
                {
                    fontSize = 19, wordWrap = true,
                    alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(14, 14, 10, 10)
                };
                toastStyle.normal.textColor = new Color(1f, 0.72f, 0.25f);

                if (cjkFont != null)
                {
                    h1.font = cjkFont; h2.font = cjkFont; body.font = cjkFont;
                    dim.font = cjkFont; btn.font = cjkFont; btnPicked.font = cjkFont;
                    toastStyle.font = cjkFont;
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
        /// 选择环节总入口 —— 按 Choice.view 分派画法。
        ///
        /// 状态机这边仍然只有一个"选择环节"状态，它不认识牌组和刀片，
        /// 只是照着环节自己声明的形态去画。
        /// 想加新形态（比如"五选二食材"），加一个 ChoiceView 分支即可。
        /// </summary>
        private void DrawChoice()
        {
            Choice c = CurrentChoice;
            if (c == null) { GUILayout.Label("（没有更多选择环节）", body); return; }

            switch (c.view)
            {
                case ChoiceView.DeckCards: DrawChoiceDeckCards(c); return;
                case ChoiceView.BladeSwap: DrawChoiceBladeSwap(c); return;
                default:                   DrawChoiceList(c);      return;
            }
        }

        /// <summary>
        /// 通用竖排列表。
        ///
        /// 选中：↑↓ 移动 / 鼠标悬停 / 鼠标点击 —— 三者改的都是同一个 highlightIndex，
        ///       也就是"鼠标挪到哪就选中哪，不用再按一次才选中"。
        /// 确认：Enter 或点一下候选项。
        /// </summary>
        private void DrawChoiceList(Choice c)
        {
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

        // ══════════════════════════════════════════════════════════════
        //  三选一牌组界面（ChoiceView.DeckCards）
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 三张牌组卡横排。每张卡显示：牌组名 / 全部食材牌的名称与属性 / 变速模块名与效果。
        /// 点卡片标题展开详情，详情下方才是「确认选择该卡组」和「取消」。
        ///
        /// 用 GUILayout.BeginHorizontal + 固定宽度来实现"横排三等分"：
        /// 卡片数量不是写死的 3，跟着 options.Count 走，策划给 4 副牌也不会排版崩。
        /// </summary>
        private void DrawChoiceDeckCards(Choice c)
        {
            TextDto t = GameConfig.Texts();
            int n = c.options.Count;

            GUILayout.Label(c.prompt, h1);
            if (!string.IsNullOrEmpty(c.hint)) GUILayout.Label(c.hint, dim);
            GUILayout.Space(12f);

            // 三等分宽度。窗口太窄时给个下限，避免卡片被压成一条线。
            float avail = Mathf.Max(660f, Screen.width - 60f - 24f);
            const float gap = 12f;
            float cardW = n > 0 ? Mathf.Max(200f, (avail - gap * (n - 1)) / n) : avail;

            GUILayout.BeginHorizontal();

            for (int i = 0; i < n; i++)
            {
                ChoiceOption o = c.options[i];
                if (o == null) continue;

                Deck d = o.deck;
                bool selected = (i == deckDetailIndex);

                GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(cardW));

                // 卡片标题本身就是按钮 —— "点击卡组查看详细信息"
                if (GUILayout.Button((selected ? "▣ " : "▢ ") + o.title,
                                     selected ? btnPicked : btn, GUILayout.Height(46f)))
                {
                    GUI.FocusControl(null);
                    deckDetailIndex = selected ? -1 : i;   // 再点一次收起
                }

                GUILayout.Space(6f);

                if (d != null)
                {
                    GUILayout.Label("食材牌 " + d.ingredients.Count + " 张", dim);
                    for (int k = 0; k < d.ingredients.Count; k++)
                    {
                        Ingredient ing = d.ingredients[k];
                        if (ing == null) continue;

                        bool isBlade = (ing.id == d.initialBladeId);
                        GUILayout.Label("　" + ing.name + (isBlade ? "　[开局刀片]" : ""), body);
                        GUILayout.Label("　　　" + ing.attrs.DescribeLabeled(), dim);
                    }

                    GUILayout.Space(6f);

                    for (int k = 0; k < d.modules.Count; k++)
                    {
                        SpeedModule m = d.modules[k];
                        if (m == null) continue;
                        GUILayout.Label("变速模块：" + m.name, body);
                        GUILayout.Label("　　　" + m.Description(), dim);
                    }
                }
                else
                {
                    GUILayout.Label("（这副牌组没有解析出数据）", dim);
                }

                GUILayout.EndVertical();

                if (i < n - 1) GUILayout.Space(gap);
            }

            GUILayout.EndHorizontal();

            GUILayout.Space(16f);

            // ── 详情区（没展开就只提示一句）──
            if (deckDetailIndex < 0 || deckDetailIndex >= n || c.options[deckDetailIndex] == null)
            {
                GUILayout.Label("点击上面的卡片查看详细信息。", dim);
                return;
            }

            ChoiceOption sel = c.options[deckDetailIndex];
            DrawDeckDetail(sel);

            GUILayout.Space(14f);
            GUILayout.BeginHorizontal();

            if (GUILayout.Button(t.deckConfirm + "：" + sel.title, btn, GUILayout.Height(56f)))
            {
                GUI.FocusControl(null);
                ConfirmChoice(sel.id);
                return;
            }

            GUILayout.Space(10f);

            if (GUILayout.Button(t.deckCancel, btn, GUILayout.Width(150f), GUILayout.Height(56f)))
            {
                GUI.FocusControl(null);
                deckDetailIndex = -1;      // 收起详情，回到三卡总览
            }

            GUILayout.EndHorizontal();
        }

        /// <summary>牌组详情：每张牌标出它开局是进刀片槽还是进手牌，并附三属性含义。</summary>
        private void DrawDeckDetail(ChoiceOption o)
        {
            Deck d = o.deck;
            GUILayout.Label("详情　" + o.title, h2);
            if (d == null) return;

            GUILayout.BeginVertical(GUI.skin.box);

            for (int k = 0; k < d.ingredients.Count; k++)
            {
                Ingredient ing = d.ingredients[k];
                if (ing == null) continue;

                bool isBlade = (ing.id == d.initialBladeId);
                GUILayout.Label("· " + ing.name
                                + (isBlade ? "　【开局装在刀片槽】" : "　【开局进手牌】"), body);
                GUILayout.Label("　　" + ing.attrs.DescribeLabeled(), dim);
            }

            for (int k = 0; k < d.modules.Count; k++)
            {
                SpeedModule m = d.modules[k];
                if (m == null) continue;
                GUILayout.Label("· 变速模块：" + m.name + "　→　" + m.Description(), body);
            }

            GUILayout.Space(8f);
            GUILayout.Label("—— 三属性含义 ——", dim);
            foreach (AttrDef def in AttrCatalog.All())
            {
                GUILayout.Label("　" + def.Label + "（" + def.tendency + "）：" + def.description, dim);
            }

            GUILayout.EndVertical();
        }

        // ══════════════════════════════════════════════════════════════
        //  选刀片界面（ChoiceView.BladeSwap）
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 上方当前刀片（名称 + 全部三属性），下方手牌。
        ///
        /// 点手牌里的**食材** → 立刻与当前刀片交换，原刀片回到手牌同一个位置。
        /// 点**模块** → 不交换，弹一句"模块不能作为刀片"。
        ///
        /// 这个界面没有任何"候选项"——玩家手上的牌就是候选项，
        /// 所以它不需要 Choice.options，走得是 turn 上的刀片 + 手牌。
        /// </summary>
        private void DrawChoiceBladeSwap(Choice c)
        {
            EnsureLoadout();

            TextDto t = GameConfig.Texts();
            Deck deck = ResolvePickedDeck();

            GUILayout.Label(c.prompt, h1);
            if (!string.IsNullOrEmpty(c.hint)) GUILayout.Label(c.hint, dim);
            GUILayout.Space(12f);

            // ── 一次性提示（"模块不能作为刀片"）──
            if (!string.IsNullOrEmpty(toast) && Time.realtimeSinceStartup < toastUntil)
            {
                GUILayout.Label("⚠ " + toast, toastStyle);
                GUILayout.Space(10f);
            }

            // ── 当前刀片 ──
            GUILayout.Label("当前刀片", h2);
            GUILayout.BeginVertical(GUI.skin.box);

            if (turn.blade == null)
            {
                GUILayout.Label("（没有刀片）", body);
            }
            else
            {
                GUILayout.Label(turn.blade.name, h2);

                // 挂上本局效果之后的实际属性 —— 界面上看到的就是真正会用来结算的值
                AttrSet resolved = turn.BladeResolvedAttrs();
                foreach (AttrDef def in AttrCatalog.All())
                {
                    GUILayout.Label("　" + def.FormatLabeled(resolved.Get(def.id))
                                    + "　（" + def.tendency + "）", body);
                }
            }

            GUILayout.EndVertical();
            GUILayout.Space(16f);

            // ── 手牌 ──
            GUILayout.Label("手牌（点击食材即换成刀片，可反复换）", h2);
            GUILayout.Space(6f);

            if (turn.hand == null || turn.hand.Count == 0)
            {
                // 正常不会有这种情况（牌组至少 4 张），留着保证代码健壮
                GUILayout.Label("手牌里没有食材，无法更换刀片。", dim);
            }

            // ★ 一条循环 —— 手牌里食材和模块是混在一起的，按类型决定点了做什么。
            //   以前是"先遍历 turn.hand 列食材、再遍历 deck.modules 列模块"两套，
            //   而且模块那份读的还是**牌组**而不是手牌，两边早就对不上了。
            for (int i = 0; i < turn.hand.Count; i++)
            {
                Card hc = turn.hand[i];
                if (hc == null) continue;

                string label = "【" + hc.TypeTag + "】" + hc.name + "\n　　" + hc.Describe();

                if (GUILayout.Button(label, btn, GUILayout.Height(64f)))
                {
                    GUI.FocusControl(null);

                    if (hc.IsModule)
                    {
                        // 模块不能当刀片 —— 规则在数据层拦着，这里只负责说清楚
                        toast = t.moduleRejected;
                        toastUntil = Time.realtimeSinceStartup + 2.5f;
                    }
                    else
                    {
                        toast = "";                    // 换成功就把上次的提示清掉
                        turn.SwapBladeWithHand(i);
                    }

                    return;                            // 布局已经变了，本帧到此为止
                }
            }

            GUILayout.Space(18f);

            if (GUILayout.Button(t.bladeConfirm, btn, GUILayout.Height(56f)))
            {
                GUI.FocusControl(null);
                ConfirmChoice();
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  回合循环（Day 3）
        //  版面：顶部信息栏 / 中部杯内食材 / 底部手牌
        // ══════════════════════════════════════════════════════════════

        /// <summary>手牌里被点选的那一项（handEntries 的下标）。-1 = 没选。</summary>
        private int handSelected = -1;

        /// <summary>进入「模拟中」的时刻，用来做 1 秒占位。</summary>
        private float simulatingSince = -1f;

        /// <summary>
        /// 手牌显示项 —— 把食材和模块合成一张列表，靠类型标签区分。
        ///
        /// 数据层里它们是两个列表（Ingredient / SpeedModule 类型不同），
        /// 但界面上玩家看到的应该是**一条手牌**，所以在这里合并一次。
        /// </summary>
        private struct HandEntry
        {
            public bool isModule;
            public int index;      // 在 turn.hand 或 turn.modules 里的下标
            public string name;
            public string tag;     // 「食材」/「模块」
        }

        private readonly List<HandEntry> handEntries = new List<HandEntry>();

        private void RebuildHandEntries()
        {
            handEntries.Clear();

            // ★ 一条循环 —— 手牌现在是一个列表（Card），
            //   以前要分别遍历 hand 和 modules 两个列表、写两遍。
            if (turn.hand != null)
                for (int i = 0; i < turn.hand.Count; i++)
                {
                    Card c = turn.hand[i];
                    if (c == null) continue;

                    handEntries.Add(new HandEntry
                    {
                        isModule = c.IsModule,
                        index    = i,
                        name     = c.name,
                        tag      = c.TypeTag
                    });
                }
        }

        private void DrawTurnStart()
        {
            // 进这一帧就已经没牌了 → 直接进关卡结束，不显示选择界面
            if (turn.IsHandEmpty) { GoTo(GameFlowState.LevelEnd); return; }

            RebuildHandEntries();
            if (handSelected >= handEntries.Count) handSelected = -1;

            DrawTurnHeader();
            GUILayout.Space(16f);
            DrawCupSection();
            GUILayout.Space(16f);
            DrawHandSection();
        }

        /// <summary>顶部信息栏：回合数 / 得分 / 当前刀片（全部属性）</summary>
        private void DrawTurnHeader()
        {
            GUILayout.Label("第 " + turn.turnNumber + " 回合", h1);
            GUILayout.Label("得分：" + turn.score + "　（今天固定 0，不做真实模拟）", dim);
            GUILayout.Space(8f);

            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.Label("当前刀片：" + turn.BladeName(), h2);

            AttrSet resolved = turn.BladeResolvedAttrs();
            foreach (AttrDef def in AttrCatalog.All())
            {
                GUILayout.Label("　" + def.FormatLabeled(resolved.Get(def.id))
                                + "　（" + def.tendency + "）", body);
            }

            if (turn.appliedModules != null && turn.appliedModules.Count > 0)
            {
                string s = "";
                for (int i = 0; i < turn.appliedModules.Count; i++)
                {
                    if (turn.appliedModules[i] == null) continue;
                    if (s.Length > 0) s += "、";
                    s += turn.appliedModules[i].name;
                }
                GUILayout.Label("　已应用模块：" + s, dim);
            }

            GUILayout.EndVertical();
        }

        /// <summary>中部：杯内食材（今天只列名字，不参与模拟）</summary>
        private void DrawCupSection()
        {
            GUILayout.Label("杯内食材", h2);

            GUILayout.BeginVertical(GUI.skin.box);
            if (turn.cup == null || turn.cup.Count == 0)
            {
                GUILayout.Label("（空）", dim);
            }
            else
            {
                for (int i = 0; i < turn.cup.Count; i++)
                    if (turn.cup[i] != null) GUILayout.Label("· " + turn.cup[i].name, body);
            }
            GUILayout.EndVertical();
        }

        /// <summary>底部：手牌区。点一项 → 高亮 + 亮出详细数据 + 「确认投放」</summary>
        private void DrawHandSection()
        {
            GUILayout.Label("手牌", h2);
            GUILayout.Label("请选择要投放的食材或模块", dim);
            GUILayout.Space(6f);

            for (int i = 0; i < handEntries.Count; i++)
            {
                HandEntry e = handEntries[i];
                bool picked = (i == handSelected);

                string label = (picked ? "◆ " : "◇ ") + e.name + "　【" + e.tag + "】";
                if (GUILayout.Button(label, picked ? btnPicked : btn, GUILayout.Height(46f)))
                {
                    GUI.FocusControl(null);
                    handSelected = picked ? -1 : i;   // 再点一次取消选择
                    return;
                }
            }

            if (handSelected < 0) return;

            HandEntry sel = handEntries[handSelected];

            GUILayout.Space(12f);
            GUILayout.BeginVertical(GUI.skin.box);

            Card selCard = turn.hand[sel.index];
            if (selCard != null)
            {
                GUILayout.Label(selCard.TypeTag + "：" + selCard.name, h2);
                GUILayout.Label("　" + selCard.Describe(), body);
                GUILayout.Label(selCard.IsModule
                                ? "　投放后立即并入本局效果，作用在当前刀片上。"
                                : "　投放后进杯子，参与杯内模拟。", dim);
            }

            GUILayout.EndVertical();
            GUILayout.Space(10f);

            if (GUILayout.Button("确认投放", btn, GUILayout.Height(52f)))
            {
                GUI.FocusControl(null);
                ConfirmPlaySelected(sel);
            }
        }

        /// <summary>执行投放。做什么由 Card.PlayInto 决定，这里不再分情况。</summary>
        private void ConfirmPlaySelected(HandEntry e)
        {
            Card c = turn.Play(e.index);
            lastPlayed = c != null ? c.name : "";

            handSelected = -1;
            simulatingSince = -1f;
            GoTo(GameFlowState.Simulating);
        }

        /// <summary>手牌摘要（食材 + 模块），结算界面用。</summary>
        private string HandSummary()
        {
            if (turn.HandCount == 0) return "（空）";

            string s = "";
            // 一条循环 —— 手牌是一个列表，食材和模块都在里面
            for (int i = 0; i < turn.hand.Count; i++)
            {
                if (turn.hand[i] == null) continue;
                if (s.Length > 0) s += "、";
                s += turn.hand[i].name;
            }
            return s;
        }

        /// <summary>模拟中 —— 今天纯占位，1 秒后自动进入结算，也可以点「继续」跳过。</summary>
        private void DrawSimulating()
        {
            if (simulatingSince < 0f) simulatingSince = Time.realtimeSinceStartup;

            GUILayout.Label("模拟中…", h1);
            GUILayout.Label("本回合投放：" + lastPlayed, body);
            GUILayout.Label("今天不做真实模拟，1 秒后自动进入结算（也可点「继续」）。", dim);
            GUILayout.Space(24f);

            // ★ 先画按钮再判断超时。
            //   写成 if (超时 || GUILayout.Button(...)) 的话，
            //   超时那一帧按钮根本不会被画出来 —— Layout 和 Repaint 的元素数量不一致，IMGUI 会报错。
            bool clicked = GUILayout.Button("继续", btn, GUILayout.Height(52f));
            bool timedOut = Time.realtimeSinceStartup - simulatingSince >= 1f;

            if (clicked || timedOut)
            {
                simulatingSince = -1f;
                GoTo(GameFlowState.TurnResult);
            }
        }

        private void DrawTurnResult()
        {
            GUILayout.Label("第 " + turn.turnNumber + " 回合结算", h1);
            GUILayout.Label("本回合得分：0　（今天不做数值）", body);
            GUILayout.Label("当前关卡总得分：" + turn.score, body);
            GUILayout.Space(12f);
            GUILayout.Label("剩余手牌：" + HandSummary(), dim);
            GUILayout.Label("杯内食材：" + turn.CupNames(), dim);
            GUILayout.Space(18f);

            if (turn.IsHandEmpty) GUILayout.Label("手牌已用完 —— 点「下一回合」将结束本关。", body);

            if (GUILayout.Button("下一回合", btn, GUILayout.Height(52f)))
            {
                if (turn.IsHandEmpty) GoTo(GameFlowState.LevelEnd);
                else
                {
                    turn.NextTurn();
                    handSelected = -1;
                    GoTo(GameFlowState.TurnStart);
                }
            }
        }

        private void DrawLevelEnd()
        {
            GUILayout.Label("手牌已用完，关卡结束", h1);
            GUILayout.Space(22f);
            if (GUILayout.Button("进入结算", btn, GUILayout.Height(52f))) GoTo(GameFlowState.LevelResult);
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
