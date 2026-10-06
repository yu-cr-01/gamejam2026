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
    ///   DSH_TURNPROBE=1    跑回合循环探针（v2.1 / 旧流程各一条，按 TableSettings.UseRulesV21 选）
    ///
    /// 【★ 命令行怎么用：-executeMethod 必须指向一个**方法**】
    ///   Unity 的 -executeMethod 只认 `类.方法`，不能指向一个带 [InitializeOnLoad] 的静态类本身。
    ///   原来这里没有入口方法，直接用
    ///       -executeMethod GameJam.EditorTools.AutoPlayHarness
    ///   跑，Unity 会报
    ///       executeMethod class 'EditorTools' could not be found
    ///   然后**以退出码 1 直接结束**（日志里只有这一行，看起来像"Unity 什么都没干"）。
    ///   所以这里补一个 Run()：它什么都不做，作用是"让这个类被加载一次"。
    ///   真正的排程仍然在静态构造里 —— 那里会根据 DSH_AUTOPLAY 决定介不介入。
    ///
    ///   正确用法：
    ///     Unity.exe -batchmode -projectPath &lt;工程&gt; -executeMethod GameJam.EditorTools.AutoPlayHarness.Run -quit -logFile &lt;日志&gt;
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

        /// <summary>
        /// 命令行 -executeMethod 的入口。
        ///
        /// 【它是故意"空"的】要做的事在静态构造函数里（那里能保证"进 Play 之后域重载
        /// 也会重新注册排程"）。这个方法唯一的职责是让 Unity 找得到这个类、
        /// 从而触发静态构造 —— 真正的排程一行都不在这里，
        /// 否则就会出现"两条路各自走一遍排程"的鬼影。
        /// </summary>
        public static void Run()
        {
            bool on = System.Environment.GetEnvironmentVariable("DSH_AUTOPLAY") == "1";
            Debug.Log("[AutoPlay] 入口已调用（DSH_AUTOPLAY=" + (on ? "1" : "未设置") + "）。"
                      + (on ? "排程由静态构造接管，接下来会自动进 Play。" : "没有设置环境变量，本次不介入。"));
        }

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
            // 收尾计时归零。放在这里是因为进 Play 会触发域重载，
            // 静态字段全被清空 —— 上一版把"已经等了多久"存在静态字段里，
            // 重载后 it 变成 0，超时判断直接失效（和文件顶部那段"进 Play 会丢静态委托"是同一类坑）。
            if (Stage < 29) finishSince = -1.0;

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
                    if (!Shot("play_board.png")) return;
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
                    if (!Shot("play_inspect.png")) return;
                    Stage = 5;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑥ 截图是帧末异步写盘的，要多等一会儿再退，
                //    否则进程先结束，文件根本没落盘（踩过：日志显示截了，磁盘上没有）。
                case 5:
                    if (EditorApplication.timeSinceStartup - stageTime < 4.0) return;

                    if (turnProbe)
                    {
                        // v2.1 和旧流程的探针是两条路（阶段含义都不一样），
                        // 靠 TableSettings.UseRulesV21 选一条 —— 和游戏里的开关是同一个值，
                        // 所以探针测到的就是玩家会走的那条路。
                        Stage = TableSettings.UseRulesV21 ? 40 : 19;
                        return;
                    }

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
                    if (!Shot("title.png")) return;
                    Stage = 31;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉛ 点桌上那本书 = 新游戏
                case 31:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.2) return;
                    StartGame();
                    Stage = 32;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉜ 关卡界面
                case 32:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.4) return;
                    if (!Shot("level_select.png")) return;
                    Stage = 33;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉝ 选第一关进入
                case 33:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    PickLevel();
                    Stage = 20;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑳ 三选一牌组
                case 20:
                    if (!Shot("choice_deck.png")) return;
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
                    if (!Shot("choice_blade.png")) return;
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
                    if (!Shot("turn_start.png")) return;
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
                    if (!Shot("turn_simulating.png")) return;
                    Stage = 27;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉗ 拍"回合结算"
                case 27:
                    if (EditorApplication.timeSinceStartup - stageTime < 2.4) return;
                    if (!Shot("turn_result.png")) return;
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
                    if (!Shot("turn_next.png")) return;
                    Stage = 30;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 30:
                    if (EditorApplication.timeSinceStartup - stageTime < 4.0) return;
                    Finish();
                    return;

                // ══════════════════════════════════════════════════════
                //  v2.1 回合循环探针
                //
                //  和上面那条旧流程的探针**分开写**，不共用 stage 号：
                //  v2.1 多了"选刀片核心"和"放置到桌面（出牌）"两个动作，
                //  而旧流程的"投放并确认 → 模拟 → 回合结算"在 v2.1 里没有对应物。
                //  硬凑成一条会让两边都读不懂。
                //
                //  一律走游戏自己的公开入口（TableTurnLoop / TableRulesV21 的
                //  ConfirmBladePick / Confirm / ActivateJuicer），不另开捷径 ——
                //  否则测的就不是玩家那条路。
                // ══════════════════════════════════════════════════════

                // ㊵ 开场界面
                case 40:
                    if (!Shot("v21_title.png")) return;
                    Stage = 51;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊱ 点书 = 新游戏
                case 51:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.2) return;
                    StartGame();
                    Stage = 52;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊲ 关卡界面
                case 52:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.4) return;
                    if (!Shot("v21_level_select.png")) return;
                    Stage = 53;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 53:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    PickLevel();
                    Stage = 54;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊳ 三选一牌组
                case 54:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    if (!Shot("v21_choice_deck.png")) return;
                    Stage = 55;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 55:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    PickDeck();
                    Stage = 56;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊴ 选刀片核心（手牌 = 4 素材 + 1 法术）
                case 56:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.4) return;
                    if (!Shot("v21_choice_blade.png")) return;
                    Stage = 57;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 57:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeCorePickV21();
                    Stage = 58;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊵ 确认刀片 → 第 1 回合开始
                case 58:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    ProbeConfirmBladeV21();
                    Stage = 59;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊶ 第 1 回合的桌面（手牌 3 素材 + 1 法术）
                case 59:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.4) return;
                    if (!Shot("v21_turn1.png")) return;
                    Stage = 60;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊷ 出牌（放置到桌面）—— 不消耗行动机会
                case 60:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeStageMaterialV21();
                    Stage = 61;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 61:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.2) return;
                    if (!Shot("v21_played.png")) return;
                    Stage = 71;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑦① 打法术（附魔到刀片，不消耗行动机会）
                case 71:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeCastSpellV21();
                    Stage = 72;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 72:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.2) return;
                    if (!Shot("v21_spell.png")) return;
                    Stage = 62;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊸ 启动破壁机：消耗 1 行动机会 + 1 刀片 H
                case 62:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeActivateV21();
                    Stage = 63;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 63:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.6) return;
                    if (!Shot("v21_stamping.png")) return;
                    Stage = 64;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 64:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.6) return;
                    if (!Shot("v21_activated.png")) return;
                    Stage = 65;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊹ 结束本回合（附魔 −1）→ 看第 2 回合的行动机会是否回到 5
                case 65:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    ProbeEndRoundV21();
                    Stage = 66;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 66:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.4) return;
                    if (!Shot("v21_turn2.png")) return;
                    Stage = 67;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊺ 再启动一次，然后把剩余回合一口气推完，验证"4 回合耗尽 → 关卡结束"
                case 67:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeDriveLevelV21();
                    Stage = 68;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 68:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.4) return;
                    if (!Shot("v21_level_end.png")) return;
                    Stage = 69;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 69:
                    if (EditorApplication.timeSinceStartup - stageTime < 4.0) return;
                    Finish();
                    return;
            }
        }

        /// <summary>这个类实例里所有截过的文件 —— 退出前要确认它们真的落盘了。</summary>
        private static readonly System.Collections.Generic.List<string> shotPaths =
            new System.Collections.Generic.List<string>();

        /// <summary>
        /// 截一张图。**返回 false 表示"这一帧还不能截"**，调用方要停在本阶段、下一帧再调。
        ///
        /// 【为什么要这样】ScreenCapture.CaptureScreenshot 是帧末异步写盘的，
        /// 同一帧里连拍两次只有最后一次生效。探针的 stage 之间原来只隔 0.6~0.8 秒，
        /// 结果日志按顺序截了 13 张、磁盘上的内容却整体滞后好几步 ——
        /// 拿这种图验收等于看错东西。这里强制两张之间至少隔 ShotSettle 秒。
        /// </summary>
        private static bool Shot(string name)
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - lastShotAt < ShotSettle) return false;
            lastShotAt = now;

            string path = Path.Combine(outDir, name);
            // ScreenCapture 拍的是整个 Game 视图 —— **包含 IMGUI**，
            // 这正是 Camera.Render() 那条路拍不到的部分。
            ScreenCapture.CaptureScreenshot(path);
            shotPaths.Add(path);
            Debug.Log("[AutoPlay] 截图 " + path);
            return true;
        }

        private static double lastShotAt = -999.0;

        /// <summary>
        /// 每次截图后给足时间让它真的写进磁盘。
        ///
        /// 【为什么是 1.6 秒而不是"下一帧就好"】ScreenCapture.CaptureScreenshot 是帧末异步写盘的，
        /// 而且**同一帧里连续两次调用只有最后一次生效**。原来 stage 之间只隔 0.6~0.8 秒，
        /// 结果日志里明明按顺序截了 13 张，磁盘上的内容却整体滞后好几步
        /// （"启动后"的那张拍到的其实是后面的状态）—— 拿这种图去验收等于看错东西。
        /// 慢一点没关系，探针本来就是无人值守跑的。
        /// </summary>
        private const double ShotSettle = 1.6;

        /// <summary>
        /// 退出前把所有截图等到落盘。
        ///
        /// 【为什么不能拍完就退】ScreenCapture.CaptureScreenshot 是**帧末异步写盘**的。
        /// 原来最后一张拍完只等了 4 秒就 EditorApplication.Exit(0)，
        /// 结果日志里 14 行"截图"、磁盘上只有 8 个 png ——
        /// 丢掉的正好是最后几张（也就是最想看的那几张）。
        /// 现在：逐个检查文件是否已经出现且非空，全齐了才退；超时也退，但会把缺哪些写进日志。
        /// </summary>
        private static void Finish()
        {
            // ★ 这个方法每帧都会被 Tick 调一次，所以"已经等了多久"必须存在字段里。
            //   写成局部变量的话每帧都从 0 开始，永远等不到超时。
            if (finishSince < 0) finishSince = EditorApplication.timeSinceStartup;

            bool allLanded = AllShotsLanded();
            bool timedOut   = EditorApplication.timeSinceStartup - finishSince > 15.0;

            if (!allLanded && !timedOut) return;    // 下一帧再查（update 回调里不能阻塞）

            if (!allLanded) Debug.LogWarning("[AutoPlay] 有截图没落盘：" + MissingShotsText());
            else            Debug.Log("[AutoPlay] " + shotPaths.Count + " 张截图全部落盘。");

            Debug.Log("[AutoPlay] 截图完成，退出。");
            EditorApplication.Exit(0);
        }

        private static double finishSince = -1.0;

        private static bool AllShotsLanded()
        {
            for (int i = 0; i < shotPaths.Count; i++)
            {
                try
                {
                    FileInfo fi = new FileInfo(shotPaths[i]);
                    if (!fi.Exists || fi.Length <= 0) return false;
                }
                catch { return false; }
            }
            return true;
        }

        private static string MissingShotsText()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < shotPaths.Count; i++)
            {
                try
                {
                    FileInfo fi = new FileInfo(shotPaths[i]);
                    if (!fi.Exists || fi.Length <= 0)
                    {
                        if (sb.Length > 0) sb.Append('、');
                        sb.Append(Path.GetFileName(shotPaths[i]));
                    }
                }
                catch { }
            }
            return sb.Length > 0 ? sb.ToString() : "（无）";
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

        private static void PickLevel()
        {
            TableChoiceRig rig = Object.FindObjectOfType<TableChoiceRig>();
            TableTurnLoop loop = Object.FindObjectOfType<TableTurnLoop>();

            if (rig == null || loop == null)
            {
                Debug.LogWarning("[AutoPlay] 找不到 TableChoiceRig / TableTurnLoop，选关探针跳过。");
                return;
            }

            rig.SelectDeck(0);
            loop.ConfirmLevelSelect();

            Debug.Log("[AutoPlay] 关卡已选 → 阶段 " + loop.phase
                      + "，当前关卡 " + loop.level.Name);
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

        // ══════════════════════════════════════════════════════════════
        //  v2.1 探针的动作
        //
        //  每一步都打一行日志 —— 探针的价值就在这些行里：
        //  跑完之后看日志能确认"回合 / 行动机会 / 得分"确实在变，
        //  而不是只有一句"没报错"。
        // ══════════════════════════════════════════════════════════════

        private static TableTurnLoop Loop() { return Object.FindObjectOfType<TableTurnLoop>(); }

        private static void ProbeCorePickV21()
        {
            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            TableTurnLoop loop = Loop();
            if (setup == null || loop == null || loop.rulesV21 == null)
            {
                Debug.LogWarning("[AutoPlay/V21] 找不到 TableSetup / TableTurnLoop，选核心探针跳过。");
                return;
            }

            TableRulesV21 r = loop.rulesV21;
            Debug.Log("[AutoPlay/V21] 初始手牌：素材 " + r.hand.Count + " 张｜法术 " + r.handSpells.Count
                      + " 张 → " + r.HandText());

            // 点手牌里的第一张素材 = 选它当刀片核心（走玩家那条 SwapBladeWith 路）
            for (int i = 0; i < setup.hand.Count; i++)
            {
                PlayCard c = setup.hand[i];
                if (c == null || c.bindingMaterial == null) continue;   // 绑定为空的必然是法术

                bool ok = loop.SwapBladeWith(c);
                Debug.Log("[AutoPlay/V21] 拿 " + c.DisplayName + " 当刀片核心 → " + ok);
                return;
            }

            Debug.LogWarning("[AutoPlay/V21] 手牌里没有素材卡（3D 手牌 " + setup.hand.Count + " 张），选核心探针跳过。");
        }

        private static void ProbeConfirmBladeV21()
        {
            TableTurnLoop loop = Loop();
            if (loop == null) return;

            loop.ConfirmBladePick();

            TableRulesV21 r = loop.rulesV21;
            if (r == null) return;

            Debug.Log("[AutoPlay/V21] 刀片已确认 → 阶段 " + loop.phase
                      + "｜刀片 " + r.blade.Describe()
                      + "｜回合 " + r.turnIndex + "/" + GameJam.Rules.LevelRun.TurnsPerLevel
                      + "｜行动机会 " + r.actionPoints + "/" + GameJam.Rules.LevelRun.ActionPointsPerTurn
                      + "｜手牌 " + r.HandText());
        }

        /// <summary>v2.1 的出牌：把第一张手牌素材放进素材槽 → 按「放置到桌面」。</summary>
        private static void ProbeStageMaterialV21()
        {
            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            TableTurnLoop loop = Loop();

            if (setup == null || loop == null || setup.board == null)
            {
                Debug.LogWarning("[AutoPlay/V21] 找不到 TableSetup / TableTurnLoop / board，出牌探针跳过。");
                return;
            }

            PlayCard card = FirstHandMaterial(setup);
            if (card == null)
            {
                Debug.LogWarning("[AutoPlay/V21] 3D 手牌里没有素材（手牌 " + setup.hand.Count + " 张），出牌探针跳过。");
                return;
            }

            int slot = TableTurnLoop.SlotMaterial;
            if (!setup.board.Place(slot, card))
            {
                Debug.LogWarning("[AutoPlay/V21] " + slot + " 号素材槽放不下这张牌（可能已占）。");
                return;
            }

            card.SnapTo(setup.board.SlotPosition(slot));
            loop.Stage(card);
            loop.Confirm();     // v2.1 下 Confirm = 出牌（不消耗行动机会）

            TableRulesV21 r = loop.rulesV21;
            if (r == null) return;

            Debug.Log("[AutoPlay/V21] 出牌：" + card.DisplayName
                      + "｜阶段 " + loop.phase
                      + "｜桌面素材 " + r.LiveTableCount() + " 张"
                      + "｜3D 手牌 " + setup.hand.Count + " 张"
                      + "｜行动机会 " + r.actionPoints + "/" + GameJam.Rules.LevelRun.ActionPointsPerTurn
                      + "（出牌不消耗行动机会，这里应该还是满的）"
                      + "｜目标 " + (r.selected != null ? r.selected.name : "（无）")
                      + "｜notice " + loop.notice);
        }

        /// <summary>
        /// 打出手牌里的法术（附魔到刀片）—— 验证"法术不消耗行动机会"和"附魔层数会加上去"。
        /// 走的是玩家那条 OnSpellCardClicked，不另开捷径。
        /// </summary>
        private static void ProbeCastSpellV21()
        {
            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            TableTurnLoop loop = Loop();
            if (setup == null || loop == null || loop.rulesV21 == null) return;

            TableRulesV21 r = loop.rulesV21;

            PlayCard spellCard = null;
            for (int i = 0; i < setup.hand.Count; i++)
            {
                PlayCard c = setup.hand[i];
                if (c != null && c.bindingSpell != null) { spellCard = c; break; }
            }

            if (spellCard == null)
            {
                Debug.LogWarning("[AutoPlay/V21] 手牌里没有法术卡（3D 手牌 " + setup.hand.Count + " 张），法术探针跳过。");
                return;
            }

            int apBefore = r.actionPoints;
            string layersBefore = r.blade.layers.Describe();

            bool handled = r.OnSpellCardClicked(spellCard);

            Debug.Log("[AutoPlay/V21] 法术：" + spellCard.DisplayName
                      + "｜被处理 " + handled
                      + "｜行动机会 " + apBefore + " → " + r.actionPoints + "（应不变）"
                      + "｜附魔 " + layersBefore + " → " + r.blade.layers.Describe()
                      + "｜手牌 " + r.HandText());
        }

        /// <summary>v2.1 的启动：直接走 ActivateJuicer（HUD 上那个按钮的同一个入口）。</summary>
        private static void ProbeActivateV21()
        {
            TableTurnLoop loop = Loop();
            if (loop == null || loop.rulesV21 == null) return;

            TableRulesV21 r = loop.rulesV21;

            Debug.Log("[AutoPlay/V21] 启动前：回合 " + r.turnIndex + "/" + GameJam.Rules.LevelRun.TurnsPerLevel
                      + "｜行动机会 " + r.actionPoints + "/" + GameJam.Rules.LevelRun.ActionPointsPerTurn
                      + "｜分数 " + r.score + "/" + r.targetScore
                      + "｜刀片 " + r.blade.Describe()
                      + "｜目标 " + (r.selected != null ? r.selected.Describe() : "（无）"));

            loop.ActivateJuicer();

            Debug.Log("[AutoPlay/V21] 启动后：回合 " + r.turnIndex + "/" + GameJam.Rules.LevelRun.TurnsPerLevel
                      + "｜行动机会 " + r.actionPoints + "/" + GameJam.Rules.LevelRun.ActionPointsPerTurn
                      + "｜分数 " + r.score + "/" + r.targetScore
                      + "｜刀片 " + r.blade.Describe()
                      + "｜附魔 " + r.blade.layers.Describe()
                      + "｜桌面素材 " + r.LiveTableCount() + " 张"
                      + "｜手牌 " + r.HandText()
                      + (r.levelOver ? "｜★ 关卡已结束（" + r.endReason + "）" : ""));
        }

        private static void ProbeEndRoundV21()
        {
            TableTurnLoop loop = Loop();
            if (loop == null || loop.rulesV21 == null) return;

            TableRulesV21 r = loop.rulesV21;
            int before = r.turnIndex;
            int apBefore = r.actionPoints;

            loop.EndRoundOrLevel();

            Debug.Log("[AutoPlay/V21] 结束回合：回合 " + before + " → " + r.turnIndex
                      + "｜行动机会 " + apBefore + " → " + r.actionPoints
                      + "（新回合应重置为 " + GameJam.Rules.LevelRun.ActionPointsPerTurn + "）"
                      + "｜附魔 " + r.blade.layers.Describe()
                      + "（每回合结束所有附魔 −1）"
                      + "｜桌面素材 " + r.LiveTableCount() + " 张（桌面素材保留）"
                      + "｜分数 " + r.score
                      + "｜阶段 " + loop.phase);
        }

        /// <summary>
        /// 把这一关剩下的回合推完：能启动就启动，不能启动就出牌，再不能就结束回合。
        ///
        /// 【为什么每轮都查一次"有没有进展"】这个循环一旦某一步静默失败
        /// （比如出牌没上桌），"条件不满足 → 再试一次"就会变成死循环，
        /// 表现是探针日志刷屏、Unity 卡住不退出。
        /// 所以每轮记一次"手牌数 + 桌面数 + 分数 + 回合数"，四个都不动就直接收工，
        /// 并且把 notice 打出来 —— 那才是真正卡住的原因。
        /// </summary>
        private static void ProbeDriveLevelV21()
        {
            TableTurnLoop loop = Loop();
            if (loop == null || loop.rulesV21 == null) return;

            TableRulesV21 r = loop.rulesV21;

            int guard = 0;
            int lastKey = -1;
            int sameKeyRounds = 0;

            while (guard++ < 64 && !r.levelOver)
            {
                int key = r.HandCount * 1000000 + r.LiveTableCount() * 10000 + r.score * 10 + r.turnIndex;
                if (key == lastKey)
                {
                    sameKeyRounds++;
                    if (sameKeyRounds >= 2)
                    {
                        Debug.LogWarning("[AutoPlay/V21] 连续 " + sameKeyRounds +
                                         " 轮没有任何变化 → 停手。notice = " + loop.notice +
                                         "｜手牌 " + r.HandText() + "｜桌面 " + r.TableText());
                        break;
                    }
                }
                else
                {
                    sameKeyRounds = 0;
                    lastKey = key;
                }

                bool progressed = false;

                if (r.CanActivate && r.selected != null && !r.selected.removed && r.selected.D > 0)
                {
                    ProbeActivateV21();
                    progressed = true;
                }
                else if (r.LiveTableCount() == 0 && r.hand.Count > 0)
                {
                    ProbeStageMaterialV21();
                    progressed = true;
                }

                if (progressed) continue;

                int turnBefore = r.turnIndex;
                loop.EndRoundOrLevel();

                // 结束回合也没换回合（关卡已结束 / 阶段不对）→ 下一轮就会因为 key 不变而收工
                if (r.turnIndex == turnBefore && !r.levelOver) { /* 交给 key 检查收工 */ }
            }

            Debug.Log("[AutoPlay/V21] 关卡收尾：回合 " + r.turnIndex + "/" + GameJam.Rules.LevelRun.TurnsPerLevel
                      + "｜分数 " + r.score + "/" + r.targetScore
                      + "｜关卡结束 = " + r.levelOver + "（原因：" + r.endReason + "）"
                      + "｜爆刀 = " + r.bursted
                      + "｜阶段 " + loop.phase);
        }

        /// <summary>手牌里的第一张素材（法术的 bindingMaterial 是空的，靠它区分）。</summary>
        private static PlayCard FirstHandMaterial(TableSetup setup)
        {
            if (setup == null || setup.hand == null) return null;

            for (int i = 0; i < setup.hand.Count; i++)
            {
                PlayCard c = setup.hand[i];
                if (c == null || c.bindingMaterial == null) continue;
                return c;
            }
            return null;
        }

        private static void OpenInspect()
        {
            TableInteraction it = Object.FindObjectOfType<TableInteraction>();
            TableSetup setup = Object.FindObjectOfType<TableSetup>();

            if (it != null && setup != null && setup.hand.Count > 0 && setup.hand[0].card != null)
            {
                it.Inspect(setup.hand[0]);
                Debug.Log("[AutoPlay] 已打开检视面板：" + setup.hand[0].DisplayName);
            }
            else
            {
                Debug.LogWarning("[AutoPlay] 没找到 TableInteraction 或手牌，检视面板这次拍不到。");
            }
        }
    }
}
