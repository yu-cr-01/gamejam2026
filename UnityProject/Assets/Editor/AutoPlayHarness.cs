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
    ///   DSH_DECK_INDEX=n   探针选第 n 副牌组（默认 0 = 配置里第一副，老行为不变）
    ///   DSH_SPELL_COUNT=n  v2.1 探针开局打 n 张法术（默认 1；打 2 张才凑得到热≥2，
    ///                      而形态变化规则大多要求热≥2，验变形时要设它）
    ///   DSH_EXTRA_HEAT=n   启动前给刀片补 n 层热（探针调味料，默认 0；见 ProbeActivateV21）
    ///                      v2.1 那条里还包含"打开 F2 规则报告 → 拍头部 → 滚到底 → 拍底部"，
    ///                      用来验报告面板有没有裁字（截图日志里带窗口尺寸）
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
                    Stage = 97;
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
                    Stage = 96;
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
                    Stage = 73;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ══════════════════════════════════════════════════════
                //  F2 规则解析报告（⑦③~⑦⑦）
                //
                //  【为什么单独几步】报告平时是关着的，而它最要命的两个毛病
                //  ——"统计行右边被裁"和"最下面一行只显示一半"—— 一个在顶部、
                //  一个在**滚动条拉到底**时才看得见。一张图拍不到两处，
                //  所以这里是"打开 → 拍头部 → 滚到底 → 拍底部 → 关掉"。
                //  打开 / 滚动走的都是 HUD 的公开入口（SetRulesReportOpen /
                //  ScrollRulesReportToEnd），和玩家按 F2、拖滚动条是同一条路。
                // ══════════════════════════════════════════════════════

                // ⑦③ 打开报告（等价于替玩家按一下 F2）
                case 73:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.6) return;
                    SetRulesReport(true);
                    Stage = 74;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑦④ 拍报告头部（统计行在不在这张里）
                case 74:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    if (!Shot("v21_rules_report_top.png")) return;
                    Stage = 75;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑦⑤ 滚到最底
                case 75:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.6) return;
                    ScrollRulesReportToEnd();
                    Stage = 76;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑦⑥ 拍报告底部（最后一行完不完整看这张）
                case 76:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    if (!Shot("v21_rules_report_bottom.png")) return;
                    Stage = 77;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑦⑦ 关掉报告，后面的 stage 拍到的还是正常的桌面
                case 77:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.6) return;
                    SetRulesReport(false);
                    Stage = 90;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ══════════════════════════════════════════════════════
                //  ⑨⓪~⑨⑤ v2.1 回合面板：半透明 + 可拖动
                //
                //  【为什么这么拍】"透不透"必须有一张**牌在面板底下**的图才看得出来，
                //   而面板默认钉在屏幕顶部、牌在桌子中间 —— 所以流程是
                //   "先拍默认位置 → 把面板往下拖 260 像素压到牌上 → 再拍"。
                //   两张一比就是用户要的对照图，同时把"拖动"这件事也验了。
                //
                //  【拖动是谁给的】探针不模拟鼠标输入（它只走游戏自己的公开入口），
                //   这里调的是 HUD 的 `DragTurnPanelBy` —— 和鼠标拖动**同一条路**：
                //   同一个 offset 字段、同一个夹取、同一个 GUI.matrix 绘制，
                //   唯一的差别只是"位移是谁喂的"。最后一档喂一个巨大的位移，
                //   用日志里的 TurnPanelOffset 证明夹取生效（面板没飞出去）。
                // ══════════════════════════════════════════════════════

                // ⑨⓪ 面板在默认位置（顶部中间）—— 半透明底板、字还是实心的
                case 90:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    if (!Shot("v21_panel_glass.png")) return;
                    Stage = 91;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑨① 把面板往下拖到桌面上（牌在那里）
                case 91:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    DragTurnPanel(0f, 260f);
                    Stage = 92;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑨② 面板压在牌上 —— 这张看"能不能透出牌"
                case 92:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    if (!Shot("v21_panel_over_cards.png")) return;
                    Stage = 93;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑨③ 喂一个巨大的位移：应该被夹在"还有 40 像素留在屏幕里"的位置
                case 93:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    DragTurnPanel(5000f, 5000f);
                    Stage = 94;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑨④ 拍"拖到天边"的样子（证明没被拖出屏幕、还能拖回来）
                case 94:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    if (!Shot("v21_panel_clamped.png")) return;
                    Stage = 95;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑨⑤ 复位，后面的 stage 拍到的还是原来那块桌面
                case 95:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    ResetTurnPanel();
                    Stage = 99;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ══════════════════════════════════════════════════════
                //  ⑨⑥~⑨⑨ 本轮新加的三件事
                //   ⑨⑥ 牌组一排（6 副）全在屏内        —— 任务 A
                //   ⑨⑦ 关卡一排 + 关卡窗口             —— 任务 A / B
                //   ⑨⑧ 卡牌图鉴（F1）                  —— 任务 C
                //   ⑨⑨ Esc 菜单里的两个新入口           —— 任务 B / C
                //  每个 stage 都先把**投影到屏幕上的包围盒**打进日志，
                //  再截图 —— "有没有跑出屏幕"这件事不能靠眼睛看。
                // ══════════════════════════════════════════════════════

                // ⑨⑥ 牌组卡：量 + 拍（插在拍完牌组界面之后、选牌组之前）
                case 96:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    LogCardRow("牌组");
                    if (!Shot("v21_decks_layout.png")) return;
                    Stage = 55;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑨⑦ 关卡卡：关掉窗口再量 + 拍（窗口挡着卡片测不准）
                case 97:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    SetLevelWindow(false);
                    Stage = 98;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 98:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    LogCardRow("关卡");
                    if (!Shot("v21_levels_layout.png")) return;
                    Stage = 53;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑨⑨ 卡牌图鉴（开 → 拍 → 关），再拍 Esc 菜单里的两个新入口
                case 99:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    SetBrowser(true);
                    Stage = 100;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 100:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.2) return;
                    if (!Shot("v21_card_browser.png")) return;
                    Stage = 101;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 101:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    SetBrowser(false);
                    SetPaused(true);
                    Stage = 102;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 102:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    if (!Shot("v21_pause_menu.png")) return;
                    Stage = 103;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 103:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    SetPaused(false);
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

            // 把窗口尺寸一并记进日志：HUD 的排版是按 Screen 算的，
            // 验收"小窗口下会不会裁字"时必须知道这张图是在多大的窗口里拍的
            // （不然两张图对不上号，说不清哪张是"大窗口"）。
            Debug.Log("[AutoPlay] 截图 " + path + "（" + Screen.width + "×" + Screen.height + "）");
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

            int idx = DeckIndex();
            rig.SelectDeck(idx);
            loop.ConfirmDeckPick();

            Debug.Log("[AutoPlay] 牌组已确认（第 " + idx + " 副）→ 阶段 " + loop.phase
                      + "，手牌 " + loop.turn.HandCount + " 张，刀片 " + loop.turn.BladeName());
        }

        /// <summary>
        /// 探针选第几副牌组，默认 0（= 配置里的第一副，**老行为一个字没变**）。
        ///
        /// 【为什么要这个环境变量】v2.1 的三副牌组（deck_water_v21 等）排在旧牌组后面，
        /// 想用探针验"v2.1 卡表真的会形态变化"就必须能选到它们。
        /// 没有这个开关就只能改代码、跑完再改回来 —— 和 TableSettings 的
        /// `DSH_RULES_V21` 是同一个理由（见那里的说明），而且很容易忘了改回来，
        /// 那样分支默认行为就被悄悄改掉了。
        /// </summary>
        private static int DeckIndex()
        {
            string raw = System.Environment.GetEnvironmentVariable("DSH_DECK_INDEX");
            int idx;
            if (!string.IsNullOrEmpty(raw) && int.TryParse(raw, out idx) && idx >= 0) return idx;
            return 0;
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

        /// <summary>
        /// 打开 / 关闭 F2 规则解析报告 —— 走 HUD 自己的入口（玩家按 F2 走的是同一个字段）。
        ///
        /// 【为什么不模拟按键】按键是 Input 层的输入，编辑器探针塞不进去；
        /// 而报告是 HUD 的私有状态，外面只能用这个口子开。
        /// </summary>
        private static void SetRulesReport(bool open)
        {
            TableHud hud = Object.FindObjectOfType<TableHud>();
            if (hud == null)
            {
                Debug.LogWarning("[AutoPlay/V21] 找不到 TableHud，规则报告这次拍不到。");
                return;
            }

            hud.SetRulesReportOpen(open);
            Debug.Log("[AutoPlay/V21] 规则解析报告 " + (open ? "已打开" : "已关闭"));
        }

        /// <summary>把报告滚到最底 —— "最后一行只显示一半"这个毛病只在底部才看得见。</summary>
        private static void ScrollRulesReportToEnd()
        {
            TableHud hud = Object.FindObjectOfType<TableHud>();
            if (hud == null) return;

            hud.ScrollRulesReportToEnd();
            Debug.Log("[AutoPlay/V21] 规则解析报告已滚到底");
        }

        /// <summary>
        /// 把 v2.1 回合面板拖一段（走 HUD 的公开口子，和鼠标拖动同一条路）。
        ///
        /// 【为什么不用真鼠标】探针不模拟输入事件；面板能不能拖、拖了会不会出屏，
        /// 只能靠"喂位移 + 截图 + 打印夹取后的结果"来看。这里打印的
        /// TurnPanelOffset 是**夹取之后**的值，所以"巨大位移也没飞出去"这件事有据可查。
        /// </summary>
        private static void DragTurnPanel(float dx, float dy)
        {
            TableHud hud = Object.FindObjectOfType<TableHud>();
            if (hud == null)
            {
                Debug.LogWarning("[AutoPlay/V21] 找不到 TableHud，面板拖动这次验不了。");
                return;
            }

            hud.DragTurnPanelBy(new Vector2(dx, dy));
            Debug.Log("[AutoPlay/V21] 回合面板拖 (" + dx + ", " + dy + ") → 位移现在是 "
                      + hud.TurnPanelOffset + "（屏幕 " + Screen.width + "×" + Screen.height + "）");
        }

        /// <summary>把回合面板挪回原位（探针收尾用，后面的截图才是"正常桌面"）。</summary>
        private static void ResetTurnPanel()
        {
            TableHud hud = Object.FindObjectOfType<TableHud>();
            if (hud == null) return;

            Vector2 back = hud.TurnPanelOffset;
            hud.DragTurnPanelBy(new Vector2(-back.x, -back.y));
            Debug.Log("[AutoPlay/V21] 回合面板已复位 → 位移 " + hud.TurnPanelOffset);
        }

        /// <summary>
        /// 把一排大卡（牌组 / 关卡）投影到屏幕上的包围盒打进日志。
        ///
        /// 【为什么必须量】"6 副牌组有没有跑出屏幕"这件事，肉眼看截图只能看出
        ///   "好像没出"，量出来才是"最右边缘 x = 1287 < 1459 − 边距"。
        ///   投影的是卡身包围盒的八个角（不是中心点）—— 立体卡只投中心会漏掉边角。
        /// </summary>
        private static void LogCardRow(string what)
        {
            TableChoiceRig rig = Object.FindObjectOfType<TableChoiceRig>();
            if (rig == null)
            {
                Debug.LogWarning("[AutoPlay/V21] 找不到 TableChoiceRig，" + what + "卡量不了。");
                return;
            }

            float x0, y0, x1, y1;
            if (!rig.CardScreenBounds(out x0, out y0, out x1, out y1))
            {
                Debug.LogWarning("[AutoPlay/V21] " + what + "卡投影失败（相机没就位？）");
                return;
            }

            Debug.Log("[AutoPlay/V21] " + what + "卡屏幕包围盒（像素，左上原点）："
                      + "x " + x0.ToString("0.0") + " ~ " + x1.ToString("0.0")
                      + "，y " + y0.ToString("0.0") + " ~ " + y1.ToString("0.0")
                      + "　｜　屏幕 " + Screen.width + "×" + Screen.height
                      + "　｜　行数 " + rig.LastLayoutRows
                      + "　缩放 " + rig.LastLayoutScale.ToString("0.00")
                      + "　｜　右边缘余量 " + (Screen.width - x1).ToString("0.0")
                      + "，下边缘余量 " + (Screen.height - y1).ToString("0.0"));
        }

        /// <summary>开 / 关关卡窗口（HUD 的公开口子）。</summary>
        private static void SetLevelWindow(bool open)
        {
            TableHud hud = Object.FindObjectOfType<TableHud>();
            if (hud == null) return;

            hud.SetLevelWindowOpen(open);
            Debug.Log("[AutoPlay/V21] 关卡窗口 " + (open ? "已打开" : "已关闭"));
        }

        /// <summary>开 / 关卡牌图鉴（走的和 Esc 菜单里那一项同一个入口）。</summary>
        private static void SetBrowser(bool open)
        {
            CardBrowser b = Object.FindObjectOfType<CardBrowser>();
            if (b == null)
            {
                Debug.LogWarning("[AutoPlay/V21] 找不到 CardBrowser，图鉴这次拍不到。");
                return;
            }

            b.SetOpen(open);
            Debug.Log("[AutoPlay/V21] 卡牌图鉴 " + (open ? "已打开" : "已关闭"));
        }

        /// <summary>开 / 关暂停菜单（拍 Esc 菜单里新增的两个入口）。</summary>
        private static void SetPaused(bool paused)
        {
            TableTurnLoop loop = Loop();
            if (loop == null) return;

            loop.paused = paused;
            Debug.Log("[AutoPlay/V21] 暂停菜单 " + (paused ? "已打开" : "已关闭"));
        }

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
        ///
        /// 【为什么张数可配（DSH_SPELL_COUNT）】v2.1 的形态变化规则大多要求**热≥2**：
        ///   只打 1 张火焰 = 热×1，水/冰这些卡的「液体→气态」「热反应」一条都命中不了，
        ///   日志里看不到变形。要验"形态变化真的发生"就得能凑到热≥2。
        ///   默认 1 张 = 老行为不变。
        /// </summary>
        private static void ProbeCastSpellV21()
        {
            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            TableTurnLoop loop = Loop();
            if (setup == null || loop == null || loop.rulesV21 == null) return;

            TableRulesV21 r = loop.rulesV21;
            int wanted = EnvInt("DSH_SPELL_COUNT", 1);

            Debug.Log("[AutoPlay/V21] 手牌里的法术：" + r.handSpells.Count + " 张｜本步要打 " + wanted + " 张");

            PlayCard lastSpellCard = null;
            string lastSpellName = "";
            if (r.handSpells.Count > 0 && r.handSpells[0] != null) lastSpellName = r.handSpells[0].name;
            bool replayed = false;

            for (int n = 0; n < wanted; n++)
            {
                PlayCard spellCard = null;
                for (int i = 0; i < setup.hand.Count; i++)
                {
                    PlayCard c = setup.hand[i];
                    if (c != null && c.bindingSpell != null) { spellCard = c; break; }
                }

                // 手牌里的法术打完了、但还要继续凑层数：
                // **复用刚才那张 3D 法术卡**再点一次，走的还是 OnSpellCardClicked
                // （它内部按 TableSpellCard.state 认牌，和这张卡还在不在手牌无关）。
                // 不另造卡、也不自己实现附魔逻辑 —— 那样测的就不是玩家那条路了。
                if (spellCard == null)
                {
                    if (lastSpellCard == null)
                    {
                        Debug.LogWarning("[AutoPlay/V21] 没有可打的法术了（已打 " + n + "/" + wanted +
                                         " 张），法术探针提前收工。");
                        return;
                    }
                    spellCard = lastSpellCard;
                    replayed = true;

                    Debug.Log("[AutoPlay/V21] 手牌里的法术已经打完了，改为重放同一张「" + lastSpellName +
                              "」继续凑附魔层数（仍走 OnSpellCardClicked）。");
                }

                lastSpellCard = spellCard;

                int apBefore = r.actionPoints;
                string layersBefore = r.blade.layers.Describe();

                bool handled = r.OnSpellCardClicked(spellCard);

                Debug.Log("[AutoPlay/V21] 法术（第 " + (n + 1) + "/" + wanted + " 张" +
                          (replayed ? "·重放" : "") + "）：" + spellCard.DisplayName
                          + "｜被处理 " + handled
                          + "｜行动机会 " + apBefore + " → " + r.actionPoints + "（应不变）"
                          + "｜附魔 " + layersBefore + " → " + r.blade.layers.Describe()
                          + "｜手牌 " + r.HandText());
            }

            r.RebuildHand();
        }

        /// <summary>读一个整数环境变量，没设或读不出就用默认值（探针开关统一走这里）。</summary>
        private static int EnvInt(string name, int fallback)
        {
            string raw = System.Environment.GetEnvironmentVariable(name);
            int v;
            if (!string.IsNullOrEmpty(raw) && int.TryParse(raw, out v) && v >= 0) return v;
            return fallback;
        }

        /// <summary>
        /// v2.1 的启动：直接走 ActivateJuicer（HUD 上那个按钮的同一个入口）。
        ///
        /// 【DSH_EXTRA_HEAT 是干什么的】v2.1 的形态变化规则大多要求**热≥2**
        ///   （液体→气态、遇热反应都是热≥2），而开局手里只有 1 张法术 = 热×1，
        ///   纯自动流程永远看不到变形。"打第二张法术"走不通 ——
        ///   法术卡打完就从手牌移除了，PlaySpell 会拒绝同一张卡（实测日志：
        ///   「附魔 热×1 → 热×1｜被处理 True」，层数没涨）。
        ///   所以给探针一个**调味料开关**：启动前直接补几层热，把引擎带到
        ///   "规则该触发"的状态，再用真入口 ActivateJuicer 跑一次完整结算。
        ///   它只影响探针，不参与任何游戏规则；默认 0 = 完全不变。
        /// </summary>
        private static void ProbeActivateV21()
        {
            TableTurnLoop loop = Loop();
            if (loop == null || loop.rulesV21 == null) return;

            TableRulesV21 r = loop.rulesV21;

            // DSH_EXTRA_HEAT=n：启动前给刀片补 n 层热（探针调味料，见方法说明）。
            int extraHeat = EnvInt("DSH_EXTRA_HEAT", 0);
            if (extraHeat > 0)
            {
                r.blade.layers.Add(GameJam.Rules.LayerKind.Heat, extraHeat);
                Debug.Log("[AutoPlay/V21] 探针调味：给刀片补 " + extraHeat + " 层热 → " +
                          r.blade.layers.Describe());
            }

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
