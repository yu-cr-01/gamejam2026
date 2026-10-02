using System.IO;
using UnityEditor;
using UnityEngine;
using GameJam.Prototype;   // TableInteraction / TableSetup 在这个命名空间里

namespace GameJam.EditorTools
{
    /// <summary>
    /// 命令行启动 Unity 时自动进入 Play 模式，可选自动截图后退出。
    /// **只在编辑器里存在，不进包；不设环境变量时完全不介入正常开发。**
    ///
    /// 【为什么需要它】
    /// 1. 让"启动 → 进 Play"一步到位，省掉手点。
    /// 2. TablePreviewCapture 走的是 Camera.Render()，**拍不到 IMGUI** ——
    ///    HUD 和右键检视面板都是 OnGUI 画的，要验证它们只能在真 Play 模式下
    ///    用 ScreenCapture 截 Game 视图。
    ///
    /// 【环境变量】
    ///   DSH_AUTOPLAY=1     启动后自动进 Play（不设则完全不介入）
    ///   DSH_PLAYCAPTURE=1  进 Play 后自动截图并退出（不给就停在 Play 里给人玩）
    ///   DSH_CAPTURE_DIR    截图输出目录，默认系统临时目录
    ///
    /// 【★ 踩过的坑：进 Play 会丢静态委托】
    /// 进入 Play 模式会触发域重载（domain reload），所有静态字段和
    /// EditorApplication.update 的订阅**全部清空**。第一版把"是否已启动"
    /// 存在静态字段里，重载后既没重新注册、又因为守卫直接 return，
    /// 结果排程器在进 Play 的那一帧就死了（日志里 Tick 正好停在 30 次）。
    /// 所以现在：**每次静态构造都重新注册 update**，进度存进 SessionState
    /// （它能跨域重载存活）。
    /// </summary>
    [InitializeOnLoad]
    public static class AutoPlayHarness
    {
        private const string KeyStage = "DSH_AutoPlayStage";

        // 这两个不需要跨重载：重载后各自会被重新赋值
        private static int    frames;
        private static double stageTime;

        private static string outDir;
        private static bool   capture;
        private static bool   turnProbe;

        static AutoPlayHarness()
        {
            if (System.Environment.GetEnvironmentVariable("DSH_AUTOPLAY") != "1") return;

            outDir  = System.Environment.GetEnvironmentVariable("DSH_CAPTURE_DIR");
            if (string.IsNullOrEmpty(outDir)) outDir = Path.GetTempPath();

            capture = System.Environment.GetEnvironmentVariable("DSH_PLAYCAPTURE") == "1";

            // 回合循环探针：把第一张手牌放进投放区并确认，一路拍到"下一回合"。
            // 单独一个开关，因为这条路径会真的改动游戏状态，不适合默认开启。
            turnProbe = System.Environment.GetEnvironmentVariable("DSH_TURNPROBE") == "1";

            // ★ 每次域重载都要订阅，否则进 Play 之后就再也没人推进流程了
            EditorApplication.update += Tick;
        }

        /// <summary>进度存 SessionState —— 静态字段扛不住域重载。</summary>
        private static int Stage
        {
            get { return SessionState.GetInt(KeyStage, 0); }
            set { SessionState.SetInt(KeyStage, value); }
        }

        private static void Tick()
        {
            switch (Stage)
            {
                // ① 等编辑器把场景加载稳了再进 Play
                case 0:
                    if (++frames < 30) return;
                    Stage = 1;
                    if (!EditorApplication.isPlayingOrWillChangePlaymode)
                        EditorApplication.EnterPlaymode();
                    return;

                // ② 已在 Play 里（域重载之后从这里继续），开始计时
                case 1:
                    if (!EditorApplication.isPlaying) return;
                    stageTime = EditorApplication.timeSinceStartup;
                    Stage = 2;
                    return;

                // ③ 给相机、字体、材质两秒就位
                case 2:
                    if (EditorApplication.timeSinceStartup - stageTime < 2.0) return;

                    Debug.Log("[AutoPlay] 已进入 Play 模式。");

                    if (!capture)
                    {
                        EditorApplication.update -= Tick;   // 停在这，交给人玩
                        return;
                    }

                    Directory.CreateDirectory(outDir);
                    Shot("play_board.png");
                    Stage = 3;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ④ 打开第一张手牌的检视面板
                case 3:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.2) return;
                    OpenInspect();
                    Stage = 4;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑤ 拍检视面板
                case 4:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.2) return;
                    Shot("play_inspect.png");
                    Stage = 5;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑥ 截图是帧末异步写盘的，要多等一会儿再退，
                //    否则进程先结束，文件根本没落盘（踩过：日志显示截了，磁盘上没有）。
                case 5:
                    if (EditorApplication.timeSinceStartup - stageTime < 4.0) return;

                    if (turnProbe) { Stage = 19; return; }

                    Finish();
                    return;

                // ══════════════════════════════════════════════════════
                //  回合循环探针
                //
                //  ★ 一律先截图、下一个 stage 再改状态。
                //    ScreenCapture.CaptureScreenshot 是帧末才写盘的：
                //    同一帧里先截图、后改状态，落盘的是改完之后那一帧。
                //    （踩过：第一版"回合结算"拍到的是下一回合的选牌界面。）
                // ══════════════════════════════════════════════════════

                // ⑲ 开场界面（书 / 木牌 / 蜡烛）
                case 19:
                    Shot("title.png");
                    Stage = 31;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉛ 点桌上那本书 = 新游戏
                case 31:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.2) return;
                    StartGame();
                    Stage = 20;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑳ 三选一牌组
                case 20:
                    Shot("choice_deck.png");
                    Stage = 21;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉑ 选第一副牌组并确认
                case 21:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    PickDeck();
                    Stage = 22;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉒ 选刀片
                case 22:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.4) return;
                    Shot("choice_blade.png");
                    Stage = 23;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉓ 换一把刀片再确认
                case 23:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    SwapBlade();
                    ConfirmBlade();
                    Stage = 24;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉔ 正式回合：第 1 回合的选牌界面
                case 24:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.4) return;
                    Shot("turn_start.png");
                    Stage = 25;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉕ 把第一张手牌放进投放区并按确认。
                //    走的是和玩家点按钮**完全相同**的入口（Stage + Confirm），
                //    不是另写一条捷径 —— 否则测的就不是玩家那条路了。
                case 25:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    StageAndConfirm();
                    Stage = 26;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉖ 冲压刚开始，拍"模拟中"
                case 26:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.6) return;
                    Shot("turn_simulating.png");
                    Stage = 27;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉗ 拍"回合结算"
                case 27:
                    if (EditorApplication.timeSinceStartup - stageTime < 2.4) return;
                    Shot("turn_result.png");
                    Stage = 28;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉘ 确认结算拍到了，再推进回合
                case 28:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    AdvanceTurn();
                    Stage = 29;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉙ 下一回合的选牌界面
                case 29:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.2) return;
                    Shot("turn_next.png");
                    Stage = 30;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 30:
                    if (EditorApplication.timeSinceStartup - stageTime < 4.0) return;
                    Finish();
                    return;
            }
        }

        private static void Finish()
        {
            Debug.Log("[AutoPlay] 截图完成，退出。");
            EditorApplication.Exit(0);
        }

        // ── 探针用的动作 ──────────────────────────────────────────────
        // 全部走游戏自己的公开入口（SelectDeck / ConfirmDeckPick / SwapBladeWith ...），
        // 这样探针测到的失败就一定是玩家会遇到的失败。

        private static void StartGame()
        {
            TableTurnLoop loop = Object.FindObjectOfType<TableTurnLoop>();
            if (loop == null) return;

            loop.ConfirmTitleStart();
            Debug.Log("[AutoPlay] 已点「新游戏」→ 阶段 " + loop.phase);
        }

        private static void PickDeck()
        {
            TableChoiceRig rig = Object.FindObjectOfType<TableChoiceRig>();
            TableTurnLoop loop = Object.FindObjectOfType<TableTurnLoop>();

            if (rig == null || loop == null)
            {
                Debug.LogWarning("[AutoPlay] 找不到 TableChoiceRig / TableTurnLoop，牌组探针跳过。");
                return;
            }

            rig.SelectDeck(0);
            loop.ConfirmDeckPick();

            Debug.Log("[AutoPlay] 牌组已确认 → 阶段 " + loop.phase
                      + "，手牌 " + loop.turn.HandCount + " 张，刀片 " + loop.turn.BladeName());
        }

        private static void SwapBlade()
        {
            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            TableTurnLoop loop = Object.FindObjectOfType<TableTurnLoop>();

            if (setup == null || loop == null || setup.hand == null || setup.hand.Count == 0)
            {
                Debug.LogWarning("[AutoPlay] 手牌是空的，换刀片探针跳过。");
                return;
            }

            // 挑一张食材 —— 模块当不了刀片
            for (int i = 0; i < setup.hand.Count; i++)
            {
                PlayCard c = setup.hand[i];
                if (c == null || c.IsModule) continue;

                bool ok = loop.SwapBladeWith(c);

                Debug.Log("[AutoPlay] 拿 " + c.DisplayName + " 换刀片 → " + ok
                          + "，现在刀片 = " + loop.turn.BladeName());
                return;
            }
        }

        private static void ConfirmBlade()
        {
            TableTurnLoop loop = Object.FindObjectOfType<TableTurnLoop>();
            if (loop == null) return;

            loop.ConfirmBladePick();
            Debug.Log("[AutoPlay] 刀片已确认 → 阶段 " + loop.phase);
        }

        /// <summary>把第一张手牌放进 0 号投放位并确认。</summary>
        private static void StageAndConfirm()
        {
            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            TableTurnLoop loop = Object.FindObjectOfType<TableTurnLoop>();

            if (setup == null || loop == null)
            {
                Debug.LogWarning("[AutoPlay] 找不到 TableSetup / TableTurnLoop，回合探针跳过。");
                return;
            }
            if (setup.board == null || setup.hand.Count == 0)
            {
                Debug.LogWarning("[AutoPlay] 投放区或手牌是空的，回合探针跳过。");
                return;
            }

            PlayCard card = setup.hand[0];
            if (!setup.board.Place(0, card))
            {
                Debug.LogWarning("[AutoPlay] 0 号位放不下这张牌。");
                return;
            }

            card.SnapTo(setup.board.SlotPosition(0));
            loop.Stage(card);
            loop.Confirm();

            Debug.Log("[AutoPlay] 已投放并确认：" + card.DisplayName
                      + "，现在阶段 = " + loop.phase);
        }

        private static void AdvanceTurn()
        {
            TableTurnLoop loop = Object.FindObjectOfType<TableTurnLoop>();
            if (loop == null) return;

            Debug.Log("[AutoPlay] 回合结算阶段 = " + loop.phase
                      + "，剩余手牌 " + loop.turn.HandCount + " 张 → 推进");
            loop.NextTurn();
        }

        private static void OpenInspect()
        {
            TableInteraction it = Object.FindObjectOfType<TableInteraction>();
            TableSetup setup = Object.FindObjectOfType<TableSetup>();

            if (it != null && setup != null && setup.hand.Count > 0 && setup.hand[0].data != null)
            {
                it.Inspect(setup.hand[0]);
                Debug.Log("[AutoPlay] 已打开检视面板：" + setup.hand[0].data.name);
            }
            else
            {
                Debug.LogWarning("[AutoPlay] 没找到 TableInteraction 或手牌，检视面板这次拍不到。");
            }
        }

        private static void Shot(string name)
        {
            string path = Path.Combine(outDir, name);
            // ScreenCapture 拍的是整个 Game 视图 —— **包含 IMGUI**，
            // 这正是 Camera.Render() 那条路拍不到的部分。
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log("[AutoPlay] 截图 " + path);
        }
    }
}
