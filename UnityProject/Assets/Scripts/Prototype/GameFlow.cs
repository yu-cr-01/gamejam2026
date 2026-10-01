using System.Collections.Generic;
using UnityEngine;
using GameJam.Data;

namespace GameJam.Prototype
{
    /// <summary>游戏阶段。顺序即流程图顺序。</summary>
    public enum GameFlowState
    {
        MainMenu,     // 主菜单
        DeckSelect,   // 三选一牌组
        BladeSelect,  // 选刀片
        TurnStart,    // 回合开始：显示手牌，等玩家选一个
        Simulating,   // 投放后：模拟运行
        TurnResult,   // 回合结算
        LevelEnd,     // 关卡结束
        LevelResult,  // 关卡结算
    }

    /// <summary>
    /// 骨架原型：只有文字和按钮，没有图片、动画、音效。
    ///
    /// 用 IMGUI（OnGUI）而不是 UGUI，好处是：
    ///   1. 不需要 Canvas / EventSystem / Prefab / 场景，空场景直接能跑
    ///   2. 整个原型只有代码，版本管理和合并都干净
    /// 等玩法定下来再换成正式 UI。
    ///
    /// 运行方式见 Assets/Scripts/Prototype/PrototypeBootstrap.cs
    /// </summary>
    [DisallowMultipleComponent]
    public class GameFlow : MonoBehaviour
    {
        // ── 运行时状态 ────────────────────────────────────────────────
        public GameFlowState State { get; private set; }

        private List<Deck> decks;
        private Deck selectedDeck;
        private LevelData level;
        private TurnState turn;
        private string lastPlayed = "";

        // ── 界面样式 ──────────────────────────────────────────────────
        private Font cjkFont;
        private GUIStyle h1, h2, body, btn;
        private Vector2 scroll;

        // ─────────────────────────────────────────────────────────────
        void Awake()
        {
            ResetAll();
        }

        /// <summary>回到初始状态。</summary>
        public void ResetAll()
        {
            decks = FakeData.AllDecks();
            selectedDeck = null;
            level = null;
            turn = new TurnState();
            lastPlayed = "";
            State = GameFlowState.MainMenu;
            scroll = Vector2.zero;
        }

        private void GoTo(GameFlowState next)
        {
            State = next;
            scroll = Vector2.zero;
        }

        // ── 样式 / 中文字体 ───────────────────────────────────────────
        // Unity 内置 GUI 字体不含中文字形，不换字体的话中文会显示成方块。
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
                h1 = new GUIStyle(GUI.skin.label);
                h1.fontSize = 30;
                h1.fontStyle = FontStyle.Bold;
                h1.wordWrap = true;

                h2 = new GUIStyle(GUI.skin.label);
                h2.fontSize = 22;
                h2.fontStyle = FontStyle.Bold;

                body = new GUIStyle(GUI.skin.label);
                body.fontSize = 18;
                body.wordWrap = true;

                btn = new GUIStyle(GUI.skin.button);
                btn.fontSize = 19;
                btn.wordWrap = true;

                btn.alignment = TextAnchor.MiddleLeft;
                btn.padding = new RectOffset(16, 16, 8, 8);

                if (cjkFont != null)
                {
                    h1.font = cjkFont; h2.font = cjkFont;
                    body.font = cjkFont; btn.font = cjkFont;
                }
            }
        }

        private static string StateLabel(GameFlowState s)
        {
            switch (s)
            {
                case GameFlowState.MainMenu:    return "主菜单";
                case GameFlowState.DeckSelect:  return "三选一牌组";
                case GameFlowState.BladeSelect: return "选刀片";
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

            float w = Mathf.Max(520f, Screen.width - 60f);
            float h = Mathf.Max(400f, Screen.height - 60f);
            GUILayout.BeginArea(new Rect(30f, 30f, w, h));
            scroll = GUILayout.BeginScrollView(scroll);

            GUILayout.Label("【当前阶段】" + StateLabel(State), h2);
            GUILayout.Space(14f);

            switch (State)
            {
                case GameFlowState.MainMenu:    DrawMainMenu();    break;
                case GameFlowState.DeckSelect:  DrawDeckSelect();  break;
                case GameFlowState.BladeSelect: DrawBladeSelect(); break;
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
            GUILayout.Label("骨架原型：只验证流程能否走通，数值和结算都是假的。", body);
            GUILayout.Space(24f);

            if (GUILayout.Button("开始游戏", btn, GUILayout.Height(52f)))
            {
                GoTo(GameFlowState.DeckSelect);
            }
        }

        private void DrawDeckSelect()
        {
            GUILayout.Label("三选一牌组", h1);
            GUILayout.Space(10f);

            for (int i = 0; i < decks.Count; i++)
            {
                Deck d = decks[i];
                GUILayout.Label(d.deckName, h2);
                GUILayout.Label("    食材：" + IngredientNames(d.ingredients), body);
                if (d.speedModule != null)
                {
                    GUILayout.Label("    变速模块：" + d.speedModule.name
                                    + "（" + d.speedModule.description + "）", body);
                }
                GUILayout.Space(4f);

                if (GUILayout.Button("选牌组 " + d.deckName.Replace("牌组 ", ""), btn, GUILayout.Height(46f)))
                {
                    selectedDeck = d.Clone();
                    level = FakeData.BuildLevel(selectedDeck);
                    turn.StartLevel(level, selectedDeck, FakeData.IronBlock());
                    GoTo(GameFlowState.BladeSelect);
                }
                GUILayout.Space(18f);
            }
        }

        private void DrawBladeSelect()
        {
            GUILayout.Label("选刀片，默认铁块", h1);
            GUILayout.Label("当前刀片：" + turn.BladeName()
                            + "（硬度 " + (turn.currentBlade != null ? turn.currentBlade.hardness : 0) + "）", body);
            if (turn.speedModule != null)
            {
                GUILayout.Label("变速模块：" + turn.speedModule.name
                                + " → " + turn.speedModule.description, body);
            }
            GUILayout.Space(12f);

            DrawDataPanel();

            GUILayout.Space(16f);
            if (GUILayout.Button("下一步", btn, GUILayout.Height(52f)))
            {
                GoTo(GameFlowState.TurnStart);
            }
        }

        private void DrawTurnStart()
        {
            GUILayout.Label("回合 " + turn.turnNumber + "，手牌：" + turn.HandNames(), h1);
            GUILayout.Label("刀片：" + turn.BladeName()
                            + "    杯内：" + turn.CupNames()
                            + "    得分：" + turn.currentScore, body);
            GUILayout.Space(14f);

            if (turn.hand.Count == 0)
            {
                GUILayout.Label("手牌已空。", body);
                GUILayout.Space(10f);
                if (GUILayout.Button("下一回合", btn, GUILayout.Height(52f)))
                {
                    GoTo(GameFlowState.LevelEnd);
                }
                return;
            }

            GUILayout.Label("点击手牌打出：", h2);
            GUILayout.Space(6f);

            for (int i = 0; i < turn.hand.Count; i++)
            {
                Ingredient ing = turn.hand[i];
                if (GUILayout.Button(Describe(ing), btn, GUILayout.Height(46f)))
                {
                    turn.hand.RemoveAt(i);
                    turn.cupIngredients.Add(ing);
                    lastPlayed = ing.name;
                    GoTo(GameFlowState.Simulating);
                    break;   // 列表已被修改，必须立刻跳出
                }
            }

            GUILayout.Space(14f);
            DrawDataPanel();
        }

        private void DrawSimulating()
        {
            GUILayout.Label("模拟中…", h1);
            GUILayout.Label("本回合打出：" + lastPlayed, body);
            GUILayout.Label("今天不做真实模拟，点一下直接进结算。", body);
            GUILayout.Space(24f);

            if (GUILayout.Button("回合结束", btn, GUILayout.Height(52f)))
            {
                GoTo(GameFlowState.TurnResult);
            }
        }

        private void DrawTurnResult()
        {
            GUILayout.Label("回合结算，得分 " + turn.currentScore, h1);
            GUILayout.Label("本回合得分固定为 0（今天不做数值）。", body);
            GUILayout.Label("剩余手牌：" + turn.HandNames(), body);
            GUILayout.Label("杯内食材：" + turn.CupNames(), body);
            GUILayout.Space(18f);

            if (turn.IsHandEmpty)
            {
                GUILayout.Label("手牌已空 —— 再点一次将结束本关。", body);
            }

            if (GUILayout.Button("下一回合", btn, GUILayout.Height(52f)))
            {
                if (turn.IsHandEmpty)
                {
                    GoTo(GameFlowState.LevelEnd);
                }
                else
                {
                    turn.NextTurn();
                    GoTo(GameFlowState.TurnStart);
                }
            }
        }

        private void DrawLevelEnd()
        {
            GUILayout.Label("关卡结束", h1);
            GUILayout.Label("手牌已空。", body);
            GUILayout.Space(24f);

            if (GUILayout.Button("查看结算", btn, GUILayout.Height(52f)))
            {
                GoTo(GameFlowState.LevelResult);
            }
        }

        private void DrawLevelResult()
        {
            bool passed = turn.currentScore >= turn.targetScore;
            GUILayout.Label("总分 " + turn.currentScore + " / 目标分 " + turn.targetScore
                            + "，" + (passed ? "通过" : "失败"), h1);
            GUILayout.Space(24f);

            if (GUILayout.Button("回主菜单", btn, GUILayout.Height(52f)))
            {
                ResetAll();
            }
        }

        // ── 辅助 ─────────────────────────────────────────────────────

        /// <summary>食材的完整属性，用于"能看到假数据"。</summary>
        private static string Describe(Ingredient ing)
        {
            return ing.name
                 + "    硬度 " + ing.hardness
                 + " · 温度 " + ing.temperature
                 + " · 酸性 " + ing.acidity
                 + " · 糖分 " + ing.sugar
                 + " · 油脂 " + ing.oil
                 + " · 水分 " + ing.water;
        }

        private static string IngredientNames(Ingredient[] arr)
        {
            if (arr == null || arr.Length == 0) return "（空）";
            List<string> names = new List<string>();
            foreach (Ingredient i in arr) { if (i != null) names.Add(i.name); }
            return string.Join("、", names.ToArray());
        }

        private void DrawDataPanel()
        {
            if (selectedDeck == null) return;

            GUILayout.Label("── 假数据 ──", h2);
            GUILayout.Label("所选牌组：" + selectedDeck.deckName, body);

            if (selectedDeck.ingredients != null)
            {
                foreach (Ingredient ing in selectedDeck.ingredients)
                {
                    if (ing != null) GUILayout.Label("    " + Describe(ing), body);
                }
            }
            if (selectedDeck.speedModule != null)
            {
                SpeedModule m = selectedDeck.speedModule;
                GUILayout.Label("    变速模块：" + m.name
                                + "    影响属性：" + m.targetAttribute
                                + "    数值：" + m.value, body);
            }
            GUILayout.Label("关卡：" + (level != null ? level.levelName : "—")
                            + "    目标分：" + (level != null ? level.targetScore : 0), body);
        }
    }
}
