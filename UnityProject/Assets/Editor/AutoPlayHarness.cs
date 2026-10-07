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
    ///   DSH_FXPROBE=1      反应特效探针（⑰⓪~⑱⑧）：破壁机摆位三张（桌面视角 / 俯视 / 榨汁机特写）
    ///                      + 把时间放慢 5 倍后连拍三张"反应发生中" + 一张配色预览。默认 0 = 完全不介入。
    ///   DSH_FX_TARGET=名   反应探针要打出去当目标的那张素材（默认"冰"；名字按包含匹配）
    ///   DSH_FX_TARGET_INDEX=n  ★ 纯 ASCII 的替身：按手牌顺序数第 n 张素材当目标（≥0 时优先于名字）
    ///   DSH_FX_LAYER=热/冷/酸/催化   启动前给刀片补哪一类附魔（默认"热"）
    ///   DSH_FX_LAYER_INDEX=n  ★ 纯 ASCII 的替身：0=热 1=冷 2=酸 3=催化（≥0 时优先于名字）
    ///   DSH_FX_LAYERS=n    补几层（默认 2 —— "遇热/遇冷"这类反应大多要求 ×2）
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
        private static bool   slotProbe;
        private static bool   fxProbe;
        private static bool   blackProbe;

        static AutoPlayHarness()
        {
            if (System.Environment.GetEnvironmentVariable("DSH_AUTOPLAY") != "1") return;

            outDir  = System.Environment.GetEnvironmentVariable("DSH_CAPTURE_DIR");
            if (string.IsNullOrEmpty(outDir)) outDir = Path.GetTempPath();

            capture = System.Environment.GetEnvironmentVariable("DSH_PLAYCAPTURE") == "1";

            // 回合循环探针：把第一张手牌放进投放区并确认，一路拍到"下一回合"。
            // 单独一个开关，因为这条路径会真的改动游戏状态，不适合默认开启。
            turnProbe = System.Environment.GetEnvironmentVariable("DSH_TURNPROBE") == "1";

            // ★ 槽位复现探针（用户报的三步：两张手牌进两个槽 → 再点一张手牌）。
            //   单独一个开关、单独一条 stage 链：它会把这一局的手牌按那个顺序打出去，
            //   混进主探针里会让后面那些"验形态变化 / 验回合推进"的步骤失去前提。
            slotProbe = System.Environment.GetEnvironmentVariable("DSH_SLOTPROBE") == "1";

            // ★ 反应特效探针（DSH_FXPROBE=1，见 ⑰⓪~ 那一段）：
            //   破壁机摆位两张 + "附魔命中目标素材"那一瞬间连拍三张。
            //   也单独一条链 —— 它会把时间放慢 5 倍来抓特效中间帧，
            //   混进主链会让后面每一步的等待时间都失去意义。
            fxProbe = System.Environment.GetEnvironmentVariable("DSH_FXPROBE") == "1";

            // ★ 黑桌探针（DSH_BLACKPROBE=1，见 ⑲⓪~⑳⑤ 那一段）：
            //   走用户报的那条来回路径（启动 → 反应特效 → 关卡结束 → 回菜单 → 再进关卡），
            //   每一步都截图 **并且把桌面渲染出来的像素量进日志** ——
            //   "哪一帧开始变黑"必须是个数字，靠眼睛看截图只会吵起来。
            blackProbe = System.Environment.GetEnvironmentVariable("DSH_BLACKPROBE") == "1";

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
                    Stage = 160;                      // 先拍一张"旧流程的槽位文案"，再继续原来的流程
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑬⓪/⑬①/⑬② **旧流程**的槽位文案对照
                //   v2.1 把「投放区（待确认）」改成了"上桌位"，这里要证明旧流程那句话
                //   一个字都没变：把一张牌摆进槽里、打开检视面板拍一张。
                case 160:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    LegacyStageAndInspect();
                    Stage = 161;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 161:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.2) return;
                    if (!Shot("legacy_slot_wording.png")) return;
                    Stage = 162;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 162:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeCloseInspectAndRelease();
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
                    // 黑桌探针顺带把"点候选核心 → 桌面刀片卡立刻跟着换"验一遍
                    // （★ 必须在 BladePick 阶段做：SwapBladeWith 只在选刀片阶段受理）
                    Stage = blackProbe ? 220 : 57;
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
                    // 三条支线各走各的（都不设 = 原来的"只用点击"那条路，行为一个字没变）
                    Stage = fxProbe ? 170 : (slotProbe ? 120 : (blackProbe ? 190 : 110));
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ══════════════════════════════════════════════════════
                //  ⑰⓪~⑰⑨ 反应特效探针（DSH_FXPROBE=1）
                //
                //  用户要的两件事各拍各的：
                //    ① 破壁机"与桌边平行"的摆位 —— 桌面视角 + 俯视各一张，
                //       并在日志里把**世界包围盒和屏幕包围盒**都打出来：
                //       "有没有出画 / 有没有压到槽位与卡"不能靠眼睛看，要能对数字。
                //    ② "两者发生反应"那一瞬间 —— 把时间放慢 5 倍再启动，
                //       这样 ShotSettle（1.6 秒，防串帧）之后的连拍才落在特效中间，
                //       而不是拍完一张特效已经没了（正常速度下特效只有 0.85 秒）。
                //
                //  ★ 放慢时间不影响判据：`Time.timeScale` 只缩放 Time.deltaTime，
                //    规则结算是一次调用跑完的（引擎不看 dt），
                //    所以"反应确实发生了""颜色是哪一类"这两件事和正常速度下一模一样。
                // ══════════════════════════════════════════════════════

                // ⑰⓪ 桌面视角：破壁机应该与桌边平行、立在桌子右侧
                case 170:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    // ★ 先截图、后量：Shot 在"距上一张不到 ShotSettle"时会返回 false 让本阶段重试，
                    //   把量测放在它前面会把同一份数据每帧打一遍（日志刷屏）
                    if (!Shot("v21_fx_place_board.png")) return;
                    LogJuicerPlacement("⑰⓪ 桌面视角");
                    Stage = 171;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑰① 切俯视
                case 171:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    GoToView("top");
                    Stage = 172;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑰② 俯视：这张看"是不是斜插"—— 与桌边平行时，立绘在俯视里是一条
                //      与世界 X 轴平行的细线（板厚方向 = 世界 Z）
                case 172:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.2) return;
                    if (!Shot("v21_fx_place_top.png")) return;
                    LogJuicerPlacement("⑰② 俯视");
                    Stage = 173;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑰③ 切回桌面视角（后面拍反应都在默认机位）
                case 173:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    GoToView("board");
                    Stage = 174;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑰④ 把"要被反应的那张素材"点上桌
                //     （默认冰；DSH_FX_TARGET=名字，或 DSH_FX_TARGET_INDEX=n 按手牌顺序选）
                case 174:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    ProbeClickHandMaterialNamed(EnvText("DSH_FX_TARGET", "冰"), EnvInt("DSH_FX_TARGET_INDEX", -1));
                    Stage = 175;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑰⑤ 放慢时间（见上面那段说明）
                case 175:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeAddFxLayers();
                    SetTimeScale(0.2f, "⑰⑤ 准备抓反应中间帧");
                    Stage = 176;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑰⑥ 启动破壁机 —— 走玩家入口（HUD 上那个按钮的同一个 ActivateJuicer），
                //      DSH_EXTRA_HEAT 负责把附魔层数调到"该触发规则"的状态（探针调味料）
                case 176:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.5) return;
                    ProbeActivateV21();
                    Stage = 177;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑰⑦~⑰⑨ 连拍三张"反应发生中"（ShotSettle 会自然把它们隔开 1.6 秒）
                //   ★ 第一张要先等 0.5 秒：启动那一帧特效才刚生成（光环半径还是 0、粒子还在卡心），
                //     直接拍只会得到一张"牌还在、什么都看不出来"的图。
                //     0.5 秒现实时间 × 0.2 倍速 = 特效时间 0.1 秒 —— 光环已经散到卡外、粒子刚呲出去。
                case 177:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.5) return;
                    if (!Shot("v21_fx_reaction_1.png")) return;
                    // 特效还活着的时候数一遍碰撞体 —— "不挡卡牌拾取"这条要有数字证据
                    Debug.Log("[AutoPlay/FX] 反应中：" + FxColliderCountText());
                    Stage = 178;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 178:
                    if (!Shot("v21_fx_reaction_2.png")) return;
                    Stage = 179;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 179:
                    if (!Shot("v21_fx_reaction_3.png")) return;
                    Stage = 180;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑱⓪ 时间恢复：后面的等待和收尾都要按正常速度算
                case 180:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    SetTimeScale(1f, "⑱⓪ 时间恢复");
                    Stage = 181;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑱① 特效该收干净了（自动销毁，桌面不留渣）—— 先查一遍再拍对照图
                case 181:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.2) return;
                    Debug.Log("[AutoPlay/FX] 特效播完之后：" + ReactionFxCountText());
                    if (!Shot("v21_fx_after.png")) return;
                    Stage = 182;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 182:
                    if (EditorApplication.timeSinceStartup - stageTime < 3.0) return;
                    Stage = 185;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ══════════════════════════════════════════════════════
                //  ⑱⑤~⑱⑦ **配色预览**（不是引擎触发的反应）
                //
                //  【为什么要这一屏】用户要的配色是五档（热=橙红、冷=青蓝、酸=黄绿、
                //   催化=淡紫、爆炸=白闪 + 更猛），但引擎侧能真的触发的没这么多：
                //   爆炸要求"粉末 + 易燃"，而当前卡表里**没有这种卡**（死规则）；
                //   催化按正文只降阈值、不直接改素材。
                //   想让策划一屏看全配色，只能直接调 ReactionFx.Play 摆出来。
                //
                //  ★ 它和"真的反应"必须分得清：真那条路在 TableRulesV21.PlayReactionFx，
                //    日志里写 [V21][反应特效]；这里只写 [AutoPlay/FX] 预览。
                // ══════════════════════════════════════════════════════

                case 185:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.5) return;
                    SetTimeScale(0.25f, "⑱⑤ 配色预览");
                    ProbeFxVariantPreview();
                    Stage = 186;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 186:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.6) return;
                    if (!Shot("v21_fx_variants.png")) return;
                    Stage = 187;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 187:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.5) return;
                    SetTimeScale(1f, "⑱⑦ 收尾");
                    Stage = 188;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 188:
                    if (EditorApplication.timeSinceStartup - stageTime < 3.0) return;
                    Finish();
                    return;

                // ══════════════════════════════════════════════════════
                //  ⑲⓪~⑳⑤ 黑桌探针（DSH_BLACKPROBE=1）
                //
                //  【它复现的是用户那句话】「桌面变黑 —— 桌子的木质表面纹理不见了」，
                //    用户走的路径是"启动 → 反应特效 → 关卡结束 → 回菜单 → 再进关卡"，
                //    所以这条链把**那条来回路径整条走一遍**，每一步都截图，
                //    并且每次都调 ProbeTableLook 把"桌面渲染出来的像素"量进日志 ——
                //    哪一帧开始黑、黑到什么程度，日志里是数字，不靠看截图下结论。
                //
                //  【为什么盯着桌面量】桌面是场景里唯一一块**大面积、朝上、纯 Standard 材质**
                //    的表面。光源没了 / 材质被换 / 贴图丢了 / 相机裁剪坏了 —— 这四类原因
                //    在截图里长得一模一样（都是一块黑），而 ProbeTableLook 一次把
                //    材质状态、光源清单、环境光、画质档位、相机参数全打出来，四类当场分开。
                // ══════════════════════════════════════════════════════

                // ⑲⓪ 第 1 回合的桌面（★ 这条链是从 ㊶ 第 1 回合接进来的，
                //      开场 / 选关 / 选牌组 / 选刀片那几张图由主链在 ⑤①~⑤⑨ 拍过，
                //      这里只拍"正式开局、桌上什么都没动"的那一帧当基准）
                case 190:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    if (!Shot("bp_01_turn1.png")) return;
                    ProbeLogSync("⑲⓪ 第 1 回合（什么都没动）");
                    ProbeTableLook("⑲⓪ 第 1 回合（什么都没动）");
                    Stage = 226;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ══════════════════════════════════════════════════════
                //  ㉕①~㉕⑦ 附魔位必过用例（用户报"附魔位不能放卡片"）
                //
                //  先把局面做成用户截图里那样：**桌上 2 张素材 + 刀片卡，手里 1 素材 + 1 法术**，
                //  然后
                //    ① 拖那张法术进「附　魔 位」→ 附魔层数必须 +1（必过）
                //    ② 拖一张素材进「附　魔 位」→ 必须被拒、且提示要说清该拖到「上　桌 位」
                //    ③ 切俯视拍一张：槽名 + 附魔层数同框
                //  三条都走玩家那条路（TableInteraction 的松手判决 / 单击分派）。
                // ══════════════════════════════════════════════════════

                // ㉕① 点一张手牌素材上桌
                case 226:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeClickHandMaterial();
                    Stage = 227;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉕② 再点一张 → 桌上两张素材（对应用户截图里的局面）
                case 227:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeClickHandMaterial();
                    Stage = 228;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 228:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    if (!Shot("bp_19_two_materials.png")) return;
                    ProbeLogSync("㉕② 桌上两张素材（复现用户截图里的局面）");
                    ProbeSpellIntoEnchantSlot("㉕③ 拖法术进附魔位（必过）");
                    Stage = 229;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉕④ 负例：素材拖进附魔位
                case 229:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeMaterialIntoEnchantSlot("㉕④ 素材拖进附魔位（应被拒）");
                    Stage = 230;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉕⑤ 切俯视（槽名和附魔层数要同框）
                case 230:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    GoToView("top");
                    Stage = 231;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉕⑥ 俯视：两个槽名牌 + 顶栏的"附魔层数"都在画面里
                case 231:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.2) return;
                    if (!Shot("bp_20_enchant_top.png")) return;
                    ProbeLogSync("㉕⑥ 俯视（附魔之后）");
                    Stage = 232;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉕⑦ 切回桌面视角，继续原来的链（收回手牌 → 启动 → …）
                case 232:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    GoToView("board");
                    Stage = 199;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑲⑨ 点手牌素材 = 上桌（后面要把它收回来）
                case 199:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeClickHandMaterial();
                    Stage = 200;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 200:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    if (!Shot("bp_09_on_table.png")) return;
                    ProbeLogSync("⑲⑨ 素材已上桌");
                    Stage = 201;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑳⓪ ★ 把桌面那张素材**拖回手牌**（用户追加的那条）
                case 201:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeWithdrawTableMaterial("⑳⓪ 拖回手牌");
                    Stage = 202;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 202:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    if (!Shot("bp_10_withdrawn.png")) return;
                    ProbeLogSync("⑳⓪ 收回之后");
                    Stage = 203;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑳① 再上桌一次（当启动目标），然后附魔 + 启动 —— 走完"反应特效"那一段
                case 203:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeClickHandMaterial();
                    ProbeAddFxLayers();
                    Stage = 204;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 204:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeActivateV21();
                    Stage = 205;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 205:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    if (!Shot("bp_11_activate.png")) return;
                    ProbeLogSync("⑳① 启动/反应特效之后");
                    ProbeTableLook("⑳① 启动/反应特效之后");
                    Stage = 206;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑳② 已经启动过的素材**不许**收回（用户要求的边界）
                case 206:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeWithdrawStartedCheck("⑳② 已启动的素材");
                    Stage = 207;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑳③ 推到关卡结束（4 回合耗尽 / 达标 / 爆刀）
                case 207:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeDriveLevelV21();
                    Stage = 208;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 208:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.2) return;
                    if (!Shot("bp_12_level_end.png")) return;
                    ProbeLogSync("⑳③ 关卡结束");
                    ProbeTableLook("⑳③ 关卡结束");
                    Stage = 209;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑳④ ★ 回菜单 —— 用户报"黑桌"最可能的第一帧就在这前后
                case 209:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeReturnToTitle();
                    Stage = 210;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 210:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.2) return;
                    if (!Shot("bp_13_back_to_title.png")) return;
                    ProbeLogSync("⑳④ 回菜单");
                    ProbeTableLook("⑳④ 回菜单");
                    Stage = 211;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑳⑤ 再进一关（第二次走同一条路 —— 累计型缺陷在这里现形）
                case 211:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    StartGame();
                    Stage = 212;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 212:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    PickLevel();
                    PickDeck();
                    Stage = 213;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 213:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeBladePickCandidate(0, "⑳⑤ 第二次进关卡·点候选");
                    ProbeConfirmBladeV21();
                    Stage = 214;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑳⑥ 第二次进关卡的桌面 —— 和 ⑲⑧ 那张逐点对照
                case 214:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.2) return;
                    if (!Shot("bp_14_reenter_turn1.png")) return;
                    ProbeLogSync("⑳⑥ 第二次进关卡");
                    ProbeTableLook("⑳⑥ 第二次进关卡");
                    Stage = 233;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉕⑧ 另一条路：**点**手牌里的法术（应该和拖进附魔位等价）
                case 233:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeClickHandSpell("㉕⑧ 点手牌法术（附魔的另一条路）");
                    Stage = 234;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 234:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.2) return;
                    if (!Shot("bp_21_spell_click.png")) return;
                    ProbeLogSync("㉕⑧ 点法术之后");
                    Stage = 215;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 215:
                    if (EditorApplication.timeSinceStartup - stageTime < 3.0) return;
                    Finish();
                    return;

                // ══════════════════════════════════════════════════════
                //  ⑳⑦~⑳⑪ 刀片候选的"连续换"验证（DSH_BLACKPROBE=1）
                //
                //  【它复现的是用户那句话】「在选择刀片的时候桌面上的刀片也要更换」——
                //    现象是"点了没视觉变化，只有按确认之后才换"。
                //    这里先拍默认门面，再连点**两张不同**的候选（第 2 张、第 3 张 ——
                //    第 1 张就是默认那张，点它看不出换没换），每次都拍一张 + 打一行
                //    "桌面刀片卡 = 谁"，最后按确认 —— 三张截图必须各不相同、
                //    日志里的卡名必须跟着走、确认之后必须还是最后点的那张。
                //
                //  ★ 两个"必须"：
                //    ① 必须在 BladePick 阶段跑：SwapBladeWith 的第一道门就是 phase == BladePick，
                //       所以它是从 ㊴（case 56，选刀片界面）岔进来的，不是从 190 那条链。
                //    ② **截图和改状态必须分成两个 stage**：ScreenCapture 是帧末异步写盘的，
                //       同一帧里先截图、后点候选，落盘的是"点完之后"的那一帧
                //       （文件头那段"一律先截图、下一个 stage 再改状态"就是这个教训）。
                // ══════════════════════════════════════════════════════

                // ⑳⑦ 默认候选的样子（还没点任何卡）
                case 220:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    if (!Shot("bp_15_blade_default.png")) return;
                    ProbeLogSync("⑳⑦ 还没点候选（默认门面）");
                    Stage = 221;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // 点第 2 张候选（第 1 张 = 默认那张，点它看不出变化）
                case 221:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeBladePickCandidate(1, "⑳⑧ 点第 2 张候选");
                    Stage = 222;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑳⑧ 桌面刀片卡应该已经换成第 2 张
                case 222:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    if (!Shot("bp_16_blade_candB.png")) return;
                    Stage = 223;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // 再点第 3 张（证明"每点一次都跟着换"，不是只换第一次）
                case 223:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeBladePickCandidate(2, "⑳⑨ 点第 3 张候选");
                    Stage = 224;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑳⑩ 第 3 张上桌的样子 + 自检（换卡不能留下"残留 view"）
                case 224:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    if (!Shot("bp_17_blade_candC.png")) return;
                    ProbeLogSync("⑳⑩ 点完候选（还没确认）");
                    Stage = 225;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑳⑪ 确认 → 桌面上那张必须和最后点的候选一致，然后回主链进第 1 回合
                case 225:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeConfirmBladeV21();
                    ProbeLogSync("⑳⑪ 刀片已确认");
                    Stage = 58;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ══════════════════════════════════════════════════════
                //  ⑫⓪~⑫⑤ 槽位复现探针（DSH_SLOTPROBE=1）
                //
                //  用户报的原话：「两张手牌塞满法术槽和素材槽之后再次点手牌就会出现这种情况」
                //  （表现：桌上的牌散开浮着、面板张数对不上、底部还挂着一张放大检视的卡）。
                //
                //  这三步走的是**玩家那条路**：
                //    拖进槽 = TableInteraction.DropCard(card, isClick:false)（松手判决）
                //    点手牌 = TableInteraction.ClickCard(card)（单击分派）
                //  探针只把"从鼠标射线认出是哪张卡"换成"由探针指定哪张"，
                //  槽位坐标由 board.SlotPosition 给 —— 和玩家把牌拖到那个框里是同一个位置。
                // ══════════════════════════════════════════════════════

                // ⑫⓪ 先拍一张"刚开始"的对照图
                case 120:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    ProbeLogSync("⑫⓪ 复现开始前");
                    if (!Shot("v21_slot_before.png")) return;
                    Stage = 121;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑫① 素材拖进「上桌位」（= 素材槽，1 号）→ 应该当场成为桌面素材并自动选为目标
                case 121:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeDropHandIntoSlot(TableTurnLoop.SlotMaterial, "⑫① 素材进上桌位");
                    Stage = 122;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 122:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.2) return;
                    ProbeLogSync("⑫① 之后");
                    if (!Shot("v21_slot_material.png")) return;
                    Stage = 123;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑫② 法术拖进「附魔位」（= 法术槽，0 号）→ 应该当场附魔到刀片
                case 123:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeDropSpellIntoSlot("⑫② 法术进附魔位");
                    Stage = 124;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 124:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.2) return;
                    ProbeLogSync("⑫② 之后");
                    if (!Shot("v21_slot_spell.png")) return;
                    Stage = 125;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑫③ 两个槽都碰过之后再点一张手牌 —— 用户报的那一步
                case 125:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeClickHandMaterial();
                    Stage = 126;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 126:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.4) return;
                    ProbeLogSync("⑫③ 之后（用户报的那一步）");
                    if (!Shot("v21_slot_click.png")) return;
                    Stage = 127;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑫④ 再点一次手牌（把"连着点"也覆盖掉），然后收工
                case 127:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeClickHandMaterial();
                    Stage = 128;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 128:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.4) return;
                    ProbeLogSync("⑫④ 连点之后");
                    if (!Shot("v21_slot_click2.png")) return;
                    Stage = 129;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑫⑤ 切到「俯视」拍一张槽位名字 —— v2.1 下这两个槽该写着「上桌位 / 附魔位」，
                //     桌面视角里它们贴着屏幕下沿、看不清全，所以单拍一张。
                case 129:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeTopView();
                    Stage = 130;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 130:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.6) return;
                    if (!Shot("v21_slot_names.png")) return;
                    Stage = 69;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ══════════════════════════════════════════════════════
                //  ⑪⑩~⑪⑥ **只用点击**的路径（用户卡住的那条）
                //
                //  用户原话"点了怎么没用？"：他点的是还压在投放区的那张盐，
                //  而旧链路必须"拖到投放区 → 按「放置到桌面」→ 再点桌上的牌选目标"。
                //  这一段就是来验"点也能走通"的：
                //    点手牌素材 → 上桌（⑪⓪）、点桌上素材 → 选目标（⑪②）、
                //    点投放区里的卡 → 上桌并选中（⑪③）、启动破壁机（⑪⑤）。
                //  走的是 TableInteraction.ClickCard —— 和玩家单击**同一条分派**，
                //  探针不模拟鼠标，只把"从射线认出是哪张卡"换成"由探针指定哪张"。
                // ══════════════════════════════════════════════════════

                // ⑪⓪ 点手牌里的素材 = 直接上桌
                case 110:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeClickHandMaterial();
                    Stage = 111;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑪① 拍"点一下之后桌面上出现了那张卡"
                case 111:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.2) return;
                    if (!Shot("v21_click_place.png")) return;
                    Stage = 112;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑪② 点桌面上的素材 = 选为启动目标
                case 112:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeClickTableCard();
                    Stage = 113;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑪③ 点投放区里待放置的卡 = 上桌 + 选为目标（用户卡住的那一步）
                case 113:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeClickStagedCard();
                    Stage = 114;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 114:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.2) return;
                    if (!Shot("v21_click_staged.png")) return;
                    Stage = 115;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ⑪⑤ 启动破壁机：日志里要出现"启动前/启动后"和引擎结算行
                case 115:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeActivateV21();
                    Stage = 116;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 116:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.4) return;
                    if (!Shot("v21_click_activate.png")) return;
                    Stage = 147;                      // 先拍刀片标记，再走原来那条"拖拽"路径
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ══════════════════════════════════════════════════════
                //  ⑪⑦~⑫③ 刀片标记 + 槽位新说法（这一轮的收尾改动）
                //   ⑪⑦/⑪⑧ 切俯视拍一张、⑪⑨/⑫⓪ 切回桌面视角再拍一张
                //           —— 用户要求"俯视和桌面视角都看得见"，两种机位各一张才算验过
                //   ⑫①~⑫③ 把一张牌摆进上桌位并打开检视面板：那里的「位置：…」
                //           在 v2.1 下应该写"上桌位（素材槽）"而不是旧的"投放区（待确认）"
                // ══════════════════════════════════════════════════════

                case 147:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    // ★ 机位名要用 TableSetup.ViewNames 里的**内部名**（"board"/"hand"/"top"…），
                    //   不是按钮上那个中文标签 —— 传中文会静默什么都不做
                    //   （第一版就踩了：俯视那张和桌面视角那张一模一样）。
                    GoToView("top");
                    Stage = 148;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 148:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.2) return;
                    if (!Shot("v21_blade_marker_top.png")) return;
                    Stage = 149;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 149:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    GoToView("board");
                    Stage = 150;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 150:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.2) return;
                    if (!Shot("v21_blade_marker_table.png")) return;
                    Stage = 151;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 151:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeStageAndInspectV21();
                    Stage = 152;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 152:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.2) return;
                    if (!Shot("v21_slot_wording.png")) return;
                    Stage = 153;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 153:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeCloseInspectAndRelease();
                    Stage = 60;                       // 接着走原来那条"拖拽"路径，两条都过一遍
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

        /// <summary>切机位（走游戏自己的公开入口 CameraRig.GoTo）。</summary>
        private static void GoToView(string name)
        {
            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            if (setup == null || setup.rig == null)
            {
                Debug.LogWarning("[AutoPlay] 找不到 CameraRig，切机位跳过（" + name + "）。");
                return;
            }

            setup.rig.GoTo(name);
            Debug.Log("[AutoPlay] 切到机位：" + name);
        }

        /// <summary>
        /// v2.1：把一张手牌素材摆进"上桌位"并打开检视面板 —— 拍那里的「位置：…」说法。
        /// </summary>
        private static void ProbeStageAndInspectV21()
        {
            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            TableInteraction it = Object.FindObjectOfType<TableInteraction>();
            TableTurnLoop loop = Loop();

            if (setup == null || it == null || loop == null || setup.board == null) return;

            PlayCard card = FirstHandMaterial(setup);
            if (card == null)
            {
                Debug.LogWarning("[AutoPlay/V21] 手牌里没有素材，槽位文案这张拍不到。");
                return;
            }

            int slot = TableTurnLoop.SlotMaterial;
            if (!setup.board.Place(slot, card)) return;

            card.SnapTo(setup.board.SlotPosition(slot));
            loop.Stage(card);
            it.Inspect(card);

            Debug.Log("[AutoPlay/V21] 把 " + card.DisplayName + " 摆进上桌位（" + slot
                      + " 号槽）并打开检视面板：位置应显示「上桌位（素材槽）」");
        }

        /// <summary>**旧流程**：同样的动作（用来对照旧文案「投放区（待确认）」一个字没变）。</summary>
        private static void LegacyStageAndInspect()
        {
            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            TableInteraction it = Object.FindObjectOfType<TableInteraction>();
            TableTurnLoop loop = Loop();

            if (setup == null || it == null || loop == null || setup.board == null) return;

            PlayCard card = null;
            for (int i = 0; i < setup.hand.Count; i++)
            {
                PlayCard c = setup.hand[i];
                if (c != null) { card = c; break; }
            }
            if (card == null)
            {
                Debug.LogWarning("[AutoPlay] 手牌是空的，旧流程槽位文案这张拍不到。");
                return;
            }

            const int slot = 0;   // 旧流程的投放位就从 0 号开始（StageAndConfirm 用的是同一个）
            if (!setup.board.Place(slot, card)) return;

            card.SnapTo(setup.board.SlotPosition(slot));
            loop.Stage(card);
            it.Inspect(card);

            Debug.Log("[AutoPlay] 旧流程：把 " + card.DisplayName + " 摆进 " + slot
                      + " 号投放位并打开检视面板：位置应显示「投放区（待确认）」（旧文案不变）");
        }

        /// <summary>收起检视面板 + 把那张牌退回手牌（两个流程共用）。</summary>
        private static void ProbeCloseInspectAndRelease()
        {
            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            TableInteraction it = Object.FindObjectOfType<TableInteraction>();
            TableTurnLoop loop = Loop();

            if (it != null) it.Inspect(null);

            if (setup == null || loop == null || setup.board == null) return;

            // 把还在槽里的牌退回去（FindObjectsOfType 找 PlayCard：两个流程都适用）
            PlayCard[] all = Object.FindObjectsOfType<PlayCard>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null || all[i].slotIndex < 0) continue;
                loop.Release(all[i]);
            }
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
            // ★ 跳过 H=0 的：PickCoreCandidate 现在会当场拒绝 H=0 的核心（H=0 一进关卡就爆刀），
            //   拿它去点会让后面每一步的前提（刀片已成立）落空。判据和 CoreCandidate 完全一致。
            for (int i = 0; i < setup.hand.Count; i++)
            {
                PlayCard c = setup.hand[i];
                if (c == null || c.bindingMaterial == null) continue;   // 绑定为空的必然是法术

                if (c.bindingMaterial.H <= 0)
                {
                    Debug.Log("[AutoPlay/V21] 跳过 H=0 的「" + c.DisplayName + "」（当核心会一进关卡就爆刀）");
                    continue;
                }

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

        // ══════════════════════════════════════════════════════════════
        //  槽位复现探针（DSH_SLOTPROBE=1）
        //
        //  【它复现的是用户那句话】「两张手牌塞满法术槽和素材槽之后再次点手牌就会出现这种情况」
        //  （桌上的牌散开浮着、面板张数对不上）。三步都走**玩家那条路**：
        //    拖进槽 = TableInteraction.DropCard(card, isClick:false)  ← 松手判决
        //    点手牌 = TableInteraction.ClickCard(card)                ← 单击分派
        //  探针只把"从鼠标射线认出是哪张卡"换成"由探针指定哪张"，
        //  卡的位置用 board.SlotPosition 摆到槽心上 —— 和玩家把牌拖到那个框里一模一样。
        // ══════════════════════════════════════════════════════════════

        /// <summary>把一张手牌拖进某个槽（松手那一下的判决）。返回它有没有被这一下处理掉。</summary>
        private static bool ProbeDropIntoSlot(PlayCard card, int slot, string what)
        {
            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            TableInteraction it = Object.FindObjectOfType<TableInteraction>();
            TableTurnLoop loop = Loop();

            if (card == null || setup == null || setup.board == null || it == null || loop == null)
            {
                Debug.LogWarning("[AutoPlay/V21] " + what + "：找不到 TableSetup / TableInteraction / board，跳过。");
                return false;
            }

            string slotName = (slot == TableTurnLoop.SlotSpell) ? "附魔位（法术槽）" : "上桌位（素材槽）";

            // 玩家的鼠标把牌拖到了槽心上 → 松手（位移 > ClickSlack，所以是"拖拽"不是"单击"）
            card.Teleport(setup.board.SlotPosition(slot), card.homeEuler);
            bool handled = it.DropCard(card, false);

            Debug.Log("[AutoPlay/V21] " + what + "：把「" + card.DisplayName + "」拖到 " + slotName
                      + "（" + slot + " 号槽）→ 被处理 " + handled
                      + "｜待放置 " + loop.StagedText
                      + "｜附魔 " + loop.rulesV21.blade.layers.Describe());
            return handled;
        }

        /// <summary>⑫① 把第一张手牌素材拖进「上桌位」。</summary>
        private static void ProbeDropHandIntoSlot(int slot, string what)
        {
            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            if (setup == null || setup.hand == null)
            {
                Debug.LogWarning("[AutoPlay/V21] " + what + "：找不到 3D 手牌，跳过。");
                return;
            }

            for (int i = 0; i < setup.hand.Count; i++)
            {
                PlayCard c = setup.hand[i];
                if (c == null || c.card == null) continue;

                // 判据和游戏里 CanStageInto 用的是同一个字段（card.IsSpell / IsMaterial）——
                // ★ 不能用 PlayCard.IsModule 当"是不是法术"：v2.1 的法术卡在数据层就是
                //   一张 SpeedModule（Card.Of(BuildSpellModule(...))），IsModule 恒为 true。
                bool match = (slot == TableTurnLoop.SlotSpell) ? c.card.IsSpell : c.card.IsMaterial;

                if (match) { ProbeDropIntoSlot(c, slot, what); return; }
            }

            Debug.LogWarning("[AutoPlay/V21] " + what + "：手牌里没有这个槽收的牌（手牌 " + setup.hand.Count + " 张），跳过。");
        }

        /// <summary>⑫② 把手牌里的法术拖进「附魔位」。</summary>
        private static void ProbeDropSpellIntoSlot(string what)
        {
            ProbeDropHandIntoSlot(TableTurnLoop.SlotSpell, what);
        }

        /// <summary>⑫⑤ 切到「俯视」机位（槽位名字在桌面视角里贴着屏幕下沿，俯视才看得全）。</summary>
        private static void ProbeTopView()
        {
            CameraRig rig = Object.FindObjectOfType<CameraRig>();
            if (rig == null)
            {
                Debug.LogWarning("[AutoPlay/V21] 找不到 CameraRig，俯视图这次拍不到。");
                return;
            }

            rig.GoTo("top");
            Debug.Log("[AutoPlay/V21] 视角切到「俯视」（当前视角 " + rig.CurrentView + "）—— 拍槽位名字");
        }

        // ══════════════════════════════════════════════════════════════
        //  反应特效探针（DSH_FXPROBE=1）用的动作 —— 见 ⑰⓪~⑱② 那一段
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 破壁机的**世界包围盒 + 屏幕包围盒 + 与槽位的关系**，一次全打进日志。
        ///
        /// 【为什么要量而不是看】"与桌边平行、不穿模、不挡卡与槽位"这三条，
        ///   肉眼看截图只能得出"好像没挡"。量出来才是：
        ///     立绘平面在 x/z 上的占地、底边是不是正好落在桌面（y=0）、
        ///     有没有和两个槽位的矩形相交、投影到屏幕有没有出画。
        ///   投影取的是包围盒的**八个角**（只投中心会漏掉边角，立体卡那边踩过同一个坑）。
        ///
        /// ★ 屏幕坐标统一成**左上原点**（和截图、和 HUD 的排版一致）：
        ///   Camera.WorldToScreenPoint 给的是左下原点，这里翻一下再打。
        /// </summary>
        private static void LogJuicerPlacement(string what)
        {
            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            if (setup == null || setup.juicer == null || setup.cam == null)
            {
                Debug.LogWarning("[AutoPlay/FX] " + what + "：找不到 TableSetup / juicer / 相机，摆位量不了。");
                return;
            }

            // 只算**活着的**渲染器：挂上美术立绘时程序化机身是整组关掉的
            Renderer[] rs = setup.juicer.GetComponentsInChildren<Renderer>();
            bool any = false;
            Bounds b = new Bounds();
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i] == null || !rs[i].enabled) continue;
                if (!any) { b = rs[i].bounds; any = true; }
                else b.Encapsulate(rs[i].bounds);
            }
            if (!any)
            {
                Debug.LogWarning("[AutoPlay/FX] " + what + "：破壁机一个渲染器都没有（立绘和程序化机身都没建出来？）。");
                return;
            }

            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x,
                                        (i & 2) == 0 ? b.min.y : b.max.y,
                                        (i & 4) == 0 ? b.min.z : b.max.z);
                Vector3 s = setup.cam.WorldToScreenPoint(c);
                float sy = Screen.height - s.y;          // 左下原点 → 左上原点
                if (s.x < x0) x0 = s.x;
                if (s.x > x1) x1 = s.x;
                if (sy < y0) y0 = sy;
                if (sy > y1) y1 = sy;
            }

            Debug.Log("[AutoPlay/FX] " + what + " 破壁机世界包围盒："
                      + "x " + b.min.x.ToString("0.000") + " ~ " + b.max.x.ToString("0.000")
                      + "，y " + b.min.y.ToString("0.000") + " ~ " + b.max.y.ToString("0.000")
                      + "，z " + b.min.z.ToString("0.000") + " ~ " + b.max.z.ToString("0.000")
                      + "（宽 " + b.size.x.ToString("0.000") + "，高 " + b.size.y.ToString("0.000") + "，厚 " + b.size.z.ToString("0.000") + "）");

            Debug.Log("[AutoPlay/FX] " + what + " 破壁机屏幕包围盒（左上原点）："
                      + "x " + x0.ToString("0.0") + " ~ " + x1.ToString("0.0")
                      + "，y " + y0.ToString("0.0") + " ~ " + y1.ToString("0.0")
                      + "　｜　屏幕 " + Screen.width + "×" + Screen.height
                      + "　｜　右边缘余量 " + (Screen.width - x1).ToString("0.0")
                      + "，上边缘余量 " + y0.ToString("0.0")
                      + (x1 <= Screen.width + 0.5f && x0 >= -0.5f && y1 <= Screen.height + 0.5f && y0 >= -0.5f
                         ? "　✓ 完整在画面内" : "　★ 有部分出画"));

            // 与两个槽位（含槽位指示块那一圈）的占地对照 —— "不挡卡与槽位"的数字版
            if (setup.board != null)
            {
                float hx = setup.board.slotSizeX * 0.5f;
                float hz = setup.board.slotSizeZ * 0.5f;

                for (int i = 0; i < setup.board.SlotCount; i++)
                {
                    Vector3 p = setup.board.SlotPosition(i);
                    bool hit = (b.min.x < p.x + hx) && (p.x - hx < b.max.x)
                            && (b.min.z < p.z + hz) && (p.z - hz < b.max.z);

                    Debug.Log("[AutoPlay/FX] " + what + " 槽位 " + i + " 占地 x "
                              + (p.x - hx).ToString("0.000") + " ~ " + (p.x + hx).ToString("0.000")
                              + "，z " + (p.z - hz).ToString("0.000") + " ~ " + (p.z + hz).ToString("0.000")
                              + "　与破壁机占地（x " + b.min.x.ToString("0.000") + " ~ " + b.max.x.ToString("0.000")
                              + "，z " + b.min.z.ToString("0.000") + " ~ " + b.max.z.ToString("0.000") + "）"
                              + (hit ? "　★ 相交（会压到落点）" : "　✓ 不相交"));
                }

                // 桌面上的卡（规则侧认领的那些）逐张量一次：特效和摆位都不该压住它们
                TableTurnLoop loop = Loop();
                if (loop != null && loop.rulesV21 != null)
                {
                    PlayCard[] all = Object.FindObjectsOfType<PlayCard>();
                    int cards = 0;
                    for (int i = 0; i < all.Length; i++)
                    {
                        if (all[i] == null || loop.rulesV21.FindTable(all[i]) == null) continue;
                        cards++;
                        Vector3 p = all[i].transform.position;
                        Debug.Log("[AutoPlay/FX] " + what + " 桌面卡「" + all[i].DisplayName + "」在 ("
                                  + p.x.ToString("0.000") + ", " + p.z.ToString("0.000") + ")"
                                  + "　与破壁机占地" + ((p.x > b.min.x && p.x < b.max.x && p.z > b.min.z && p.z < b.max.z)
                                     ? "　★ 落在破壁机占地里" : "　✓ 不重叠"));
                    }
                    if (cards == 0)
                        Debug.Log("[AutoPlay/FX] " + what + " 桌面上一张素材都还没有（这一步之前没出过牌）——"
                                  + "落点按两个槽位的矩形算，见上面两行");
                }

                // 手牌也量一下：整排手牌在 z ≈ −0.58（机器在 +Z 那侧），这条把"不挡手牌"也钉住
                if (setup.hand != null && setup.hand.Count > 0)
                {
                    float hx0 = float.MaxValue, hx1 = float.MinValue, hz0 = float.MaxValue, hz1 = float.MinValue;
                    int n = 0;
                    for (int i = 0; i < setup.hand.Count; i++)
                    {
                        if (setup.hand[i] == null) continue;
                        Vector3 p = setup.hand[i].transform.position;
                        if (p.x < hx0) hx0 = p.x;
                        if (p.x > hx1) hx1 = p.x;
                        if (p.z < hz0) hz0 = p.z;
                        if (p.z > hz1) hz1 = p.z;
                        n++;
                    }

                    if (n > 0)
                    {
                        bool hit = (b.min.x < hx1 && hx0 < b.max.x) && (b.min.z < hz1 && hz0 < b.max.z);
                        Debug.Log("[AutoPlay/FX] " + what + " 手牌 " + n + " 张 占地 x " + hx0.ToString("0.000")
                                  + " ~ " + hx1.ToString("0.000") + "，z " + hz0.ToString("0.000") + " ~ " + hz1.ToString("0.000")
                                  + "　与破壁机占地" + (hit ? "　★ 相交" : "　✓ 不相交"));
                    }
                }
            }
        }

        /// <summary>
        /// 点**指定的**手牌素材上桌 —— 走 TableInteraction.ClickCard，和玩家单击同一条路。
        ///
        /// 【为什么要能点名】默认那两条链点的是"手牌里第一张素材"，
        ///   而"哪一类附魔对哪张卡有反应"是看卡面的：水牌组里冰（遇热）会变水，
        ///   水蒸气（遇冷）对热毫无反应 —— 点错卡就拍不到任何反应，还以为是特效没做。
        ///
        /// 【为什么除了名字还留一个 index】名字是中文，而 .cmd 里写中文会被 cmd.exe
        ///   按 OEM 码页解析、整份批处理可能直接解析失败（踩过：Unity 起来时一个环境变量都没有）。
        ///   所以再给一条**纯 ASCII** 的路：`DSH_FX_TARGET_INDEX=n` 按手牌顺序数第 n 张素材。
        ///   index ≥ 0 时优先用它。
        /// </summary>
        private static void ProbeClickHandMaterialNamed(string namePart, int index)
        {
            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            TableInteraction it = Object.FindObjectOfType<TableInteraction>();
            TableTurnLoop loop = Loop();

            if (setup == null || it == null || loop == null || loop.rulesV21 == null)
            {
                Debug.LogWarning("[AutoPlay/FX] 找不到 TableSetup / TableInteraction，点名上桌跳过。");
                return;
            }

            TableRulesV21 r = loop.rulesV21;

            // 手牌里的素材（法术的 bindingMaterial 是空的，靠它区分）
            System.Collections.Generic.List<PlayCard> mats = new System.Collections.Generic.List<PlayCard>();
            for (int i = 0; i < setup.hand.Count; i++)
            {
                PlayCard c = setup.hand[i];
                if (c != null && c.bindingMaterial != null) mats.Add(c);
            }

            PlayCard pick = null;

            if (index >= 0 && index < mats.Count) pick = mats[index];

            if (pick == null)
            {
                for (int i = 0; i < mats.Count; i++)
                {
                    if (string.IsNullOrEmpty(namePart)) { pick = mats[i]; break; }
                    if (mats[i].DisplayName != null && mats[i].DisplayName.Contains(namePart)) { pick = mats[i]; break; }
                }
            }

            if (pick == null)
            {
                Debug.LogWarning("[AutoPlay/FX] 手牌里没有「" + namePart + "」（index=" + index + "），点名上桌跳过。"
                                 + "当前手牌：" + r.HandText());
                return;
            }

            int before = r.LiveTableCount();
            bool handled = it.ClickCard(pick);

            Debug.Log("[AutoPlay/FX] ★点名上桌：「" + pick.DisplayName + "」｜被处理 " + handled
                      + "｜桌面素材 " + before + " → " + r.LiveTableCount() + " 张"
                      + "｜启动目标 " + (r.selected != null ? r.selected.name : "（无）")
                      + "｜notice " + loop.notice);
        }

        /// <summary>
        /// 放慢 / 恢复正常时间 —— 抓特效中间帧用。
        ///
        /// 【为什么敢动 timeScale】规则结算是一次调用跑完的（引擎不看 Time.deltaTime），
        ///   放慢只影响"动画播多快"。所以"反应确实发生了""是什么颜色"这些判据
        ///   和正常速度下完全一样，唯一的变化是 0.85 秒的特效在现实里变成好几秒，
        ///   够 ShotSettle（1.6 秒防串帧）之后的连拍落在特效中间。
        /// </summary>
        private static void SetTimeScale(float scale, string what)
        {
            Time.timeScale = scale;

            float real = scale > 0.0001f ? (ReactionFx.Life / scale) : 0f;
            Debug.Log("[AutoPlay/FX] " + what + "：Time.timeScale = " + scale
                      + "｜特效 " + ReactionFx.Life + " 秒 → 现实里约 " + real.ToString("0.0") + " 秒");
        }

        /// <summary>场上还剩几个特效对象（播完应当都是 0 —— 特效自己销毁，不给下一帧留垃圾）。</summary>
        private static string ReactionFxCountText()
        {
            ReactionFx[] fx = Object.FindObjectsOfType<ReactionFx>();
            JuicerEcho[] echo = Object.FindObjectsOfType<JuicerEcho>();

            return "卡上特效残留 " + fx.Length + " 个、破壁机呼应残留 " + echo.Length + " 个（都应为 0）";
        }

        /// <summary>
        /// 场上特效对象里**还启用着**的碰撞体数量 —— 启用数必须是 0。
        ///
        /// 【为什么值得为它单开一行日志】拾取走的是全场景 Physics.Raycast（TableInteraction
        ///   那一句没有 LayerMask）。特效只要留一个启用的 Collider，反应那 0.85 秒里
        ///   **鼠标点不到任何一张牌** —— 而且是"过一会儿又好了"的间歇性表现，
        ///   事后靠眼睛看截图根本发现不了。所以这条要数字，不要"看起来没问题"。
        /// </summary>
        private static string FxColliderCountText()
        {
            int enabled = 0, total = 0;

            Collider[] cols = Object.FindObjectsOfType<Collider>();
            ReactionFx[] fx = Object.FindObjectsOfType<ReactionFx>();
            JuicerEcho[] echo = Object.FindObjectsOfType<JuicerEcho>();

            for (int i = 0; i < cols.Length; i++)
            {
                Collider c = cols[i];
                if (c == null) continue;

                bool underFx = false;
                for (int k = 0; k < fx.Length && !underFx; k++)
                    if (fx[k] != null && c.transform.IsChildOf(fx[k].transform)) underFx = true;
                for (int k = 0; k < echo.Length && !underFx; k++)
                    if (echo[k] != null && c.transform.IsChildOf(echo[k].transform)) underFx = true;

                if (!underFx) continue;

                total++;
                if (c.enabled) enabled++;
            }

            return "特效范围内的碰撞体：启用 " + enabled + " 个 / 共 " + total + " 个"
                 + (enabled == 0 ? "　✓ 不会挡住射线拾取" : "　★ 有启用的碰撞体，会挡住拾取");
        }

        /// <summary>
        /// 按环境变量给刀片加附魔层数（DSH_FX_LAYER=热/冷/酸/催化、DSH_FX_LAYERS=n，默认 热×2）。
        ///
        /// 【为什么需要它】和 DSH_EXTRA_HEAT 是同一个理由：开局手里只有 1 张法术 = 对应附魔 ×1，
        ///   而"遇热/遇冷"这类反应大多要求 ×2。要拍到某一种颜色的反应，
        ///   就得能把刀片带到"规则该触发"的状态 —— 走的是 BladeState 的公开入口 Add，
        ///   和自己打两张法术在规则上是同一件事，探针不碰任何规则代码。
        /// </summary>
        private static void ProbeAddFxLayers()
        {
            TableTurnLoop loop = Loop();
            if (loop == null || loop.rulesV21 == null) return;

            string name = EnvText("DSH_FX_LAYER", "热");
            int n = EnvInt("DSH_FX_LAYERS", 2);
            if (n <= 0) return;

            GameJam.Rules.LayerKind kind = GameJam.Rules.LayerKind.Heat;
            if (name == "冷") kind = GameJam.Rules.LayerKind.Cold;
            else if (name == "酸") kind = GameJam.Rules.LayerKind.Acid;
            else if (name == "催化") kind = GameJam.Rules.LayerKind.Catalyst;

            // ★ 名字是中文、写不进 ASCII 的 .cmd（见 ProbeClickHandMaterialNamed 的说明），
            //   所以再给一条数字路：0=热 1=冷 2=酸 3=催化。≥0 时优先。
            int idx = EnvInt("DSH_FX_LAYER_INDEX", -1);
            if (idx >= 0 && idx < GameJam.Rules.LayerLedger.KindCount)
                kind = (GameJam.Rules.LayerKind)idx;

            TableRulesV21 r = loop.rulesV21;
            string before = r.blade.layers.Describe();
            r.blade.layers.Add(kind, n);

            Debug.Log("[AutoPlay/FX] 附魔调味：" + LayerKindName(kind) + "×" + n + "　"
                      + before + " → " + r.blade.layers.Describe()
                      + "（走 BladeState.layers.Add，和自己打法术在规则上是同一件事）");
        }

        private static string LayerKindName(GameJam.Rules.LayerKind k)
        {
            switch (k)
            {
                case GameJam.Rules.LayerKind.Cold:     return "冷";
                case GameJam.Rules.LayerKind.Acid:     return "酸";
                case GameJam.Rules.LayerKind.Catalyst: return "催化";
            }
            return "热";
        }

        /// <summary>
        /// 五种配色一次摆全（见 ⑱⑤ 那段说明：**这不是引擎触发的反应**，是给策划看配色的）。
        ///
        /// 【为什么摆两排而不是一排】一个光环的直径上限是 0.6 世界单位、屏幕上约 530 像素，
        ///   五个摆一排要 2600 像素，屏幕只有 1470 —— 必然左右两端被切掉。
        ///   所以三前一后：第一排 z=0.05（离玩家近、不在 HUD 面板后面），
        ///   第二排 z=0.38（再往后就被顶部那块回合面板盖住了，实测过）。
        ///   两排的 x 错开半格，避免光环互相压。
        /// </summary>
        private static void ProbeFxVariantPreview()
        {
            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            if (setup == null) { Debug.LogWarning("[AutoPlay/FX] 找不到 TableSetup，配色预览跳过。"); return; }

            ReactionFx.Play(new Vector3(-0.50f, 0f, 0.05f), ReactionFxKind.Heat,     null);
            ReactionFx.Play(new Vector3( 0.00f, 0f, 0.05f), ReactionFxKind.Cold,     null);
            ReactionFx.Play(new Vector3( 0.50f, 0f, 0.05f), ReactionFxKind.Acid,     null);
            ReactionFx.Play(new Vector3(-0.25f, 0f, 0.38f), ReactionFxKind.Catalyst, null);
            ReactionFx.Play(new Vector3( 0.25f, 0f, 0.38f), ReactionFxKind.Explode,  null);

            Debug.Log("[AutoPlay/FX] 配色预览（★ 直接调 ReactionFx.Play，不是引擎触发的反应）："
                      + "热=橙红 / 冷=青蓝 / 酸=黄绿 / 催化=淡紫 / 爆炸=白闪（力度 ×" + ReactionFx.ExplodePower + "）"
                      + "｜时长 " + ReactionFx.Life + "s｜粒子 " + ReactionFx.SparkCount + " 颗/个"
                      + "（爆炸 ×" + ReactionFx.ExplodePower + "）｜光环半径上限 " + ReactionFx.RingRadius);
        }

        /// <summary>读一个字符串环境变量，没设就用默认值（和 EnvInt 一对）。</summary>
        private static string EnvText(string name, string fallback)
        {
            string v = System.Environment.GetEnvironmentVariable(name);
            return string.IsNullOrEmpty(v) ? fallback : v;
        }

        /// <summary>把"状态几张 / 场景几张"的自检摘要打进日志（探针验收就看这几行）。</summary>
        private static void ProbeLogSync(string what)
        {
            TableTurnLoop loop = Loop();
            if (loop == null || loop.rulesV21 == null) return;

            TableRulesV21 r = loop.rulesV21;

            Debug.Log("[AutoPlay/V21] " + what + "｜" + r.ViewSyncSummary()
                      + "｜桌面 " + r.TableText()
                      + "｜手牌 " + r.HandText()
                      + "｜目标 " + (r.selected != null ? r.selected.name : "（无）")
                      + "｜分数 " + r.score + "｜阶段 " + loop.phase);
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

        // ── "只用点击"那条路（⑪⑩~⑪⑥）────────────────────────────────
        // 三个探针都走 TableInteraction.ClickCard：和玩家单击同一个分派，
        // 不另开一条捷径 —— 否则测的就不是玩家那条路。

        /// <summary>点手牌里的素材 = 直接上桌（等价于拖到投放区 + 按「放置到桌面」）。</summary>
        private static void ProbeClickHandMaterial()
        {
            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            TableInteraction it = Object.FindObjectOfType<TableInteraction>();
            TableTurnLoop loop = Loop();

            if (setup == null || it == null || loop == null || loop.rulesV21 == null)
            {
                Debug.LogWarning("[AutoPlay/V21] 找不到 TableSetup / TableInteraction，单击上桌探针跳过。");
                return;
            }

            TableRulesV21 r = loop.rulesV21;
            PlayCard card = FirstHandMaterial(setup);
            if (card == null)
            {
                Debug.LogWarning("[AutoPlay/V21] 手牌里没有素材（3D 手牌 " + setup.hand.Count + " 张），单击上桌探针跳过。");
                return;
            }

            // 点之前先把"投放区压着什么"写出来：点一下会把**所有**待放置的一起送上桌
            // （这就是「放置到桌面」的语义），日志里得能看出这一下到底送了几张、为什么。
            Debug.Log("[AutoPlay/V21] 点之前：桌面素材 " + r.LiveTableCount() + " 张"
                      + "｜待放置 " + loop.StagedCount + " 张（" + loop.StagedText + "）"
                      + "｜手牌 " + r.HandText());

            int before = r.LiveTableCount();
            bool handled = it.ClickCard(card);

            Debug.Log("[AutoPlay/V21] ★点手牌素材：" + card.DisplayName + "｜被处理 " + handled
                      + "｜桌面素材 " + before + " → " + r.LiveTableCount() + " 张"
                      + "｜启动目标 " + (r.selected != null ? r.selected.name : "（无）")
                      + "｜notice " + loop.notice);
        }
        /// <summary>点桌面上的素材 = 选为启动目标（原来就有，这里顺带验一遍）。</summary>
        private static void ProbeClickTableCard()
        {
            TableInteraction it = Object.FindObjectOfType<TableInteraction>();
            TableTurnLoop loop = Loop();
            if (it == null || loop == null || loop.rulesV21 == null) return;

            TableRulesV21 r = loop.rulesV21;

            // 桌面上的 3D 卡：用规则层的 FindTable 认（不靠名字，名字会随 D 变）
            PlayCard target = null;
            PlayCard[] all = Object.FindObjectsOfType<PlayCard>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                if (r.FindTable(all[i]) == null) continue;
                target = all[i];
                break;
            }

            if (target == null)
            {
                Debug.LogWarning("[AutoPlay/V21] 桌面上一张素材都没有，点选目标探针跳过。");
                return;
            }

            bool handled = it.ClickCard(target);

            Debug.Log("[AutoPlay/V21] ★点桌面素材：" + target.DisplayName + "｜被处理 " + handled
                      + "｜启动目标 " + (r.selected != null ? r.selected.name : "（无）")
                      + "｜notice " + loop.notice);
        }

        /// <summary>
        /// 点**投放区里待放置**的卡 = 上桌 + 选为启动目标（用户卡住的那一步）。
        ///
        /// 先用游戏自己的公开入口把一张手牌摆进投放区（`board.Place` + `SnapTo` + `Stage`，
        /// 和玩家拖进去的效果一样），再"点"它 —— 这样验的才是"压在投放区那张点得动"。
        /// </summary>
        private static void ProbeClickStagedCard()
        {
            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            TableInteraction it = Object.FindObjectOfType<TableInteraction>();
            TableTurnLoop loop = Loop();

            if (setup == null || it == null || loop == null || loop.rulesV21 == null || setup.board == null)
            {
                Debug.LogWarning("[AutoPlay/V21] 找不到 TableSetup / TableInteraction / board，点投放区探针跳过。");
                return;
            }

            TableRulesV21 r = loop.rulesV21;
            PlayCard card = FirstHandMaterial(setup);
            if (card == null)
            {
                Debug.LogWarning("[AutoPlay/V21] 手牌里没有素材了，点投放区探针跳过。");
                return;
            }

            int slot = TableTurnLoop.SlotMaterial;
            if (!setup.board.Place(slot, card))
            {
                Debug.LogWarning("[AutoPlay/V21] " + slot + " 号素材槽放不下 " + card.DisplayName + "，探针跳过。");
                return;
            }

            card.SnapTo(setup.board.SlotPosition(slot));
            loop.Stage(card);

            int before = r.LiveTableCount();
            Debug.Log("[AutoPlay/V21] 先把 " + card.DisplayName + " 摆进投放区（待放置 "
                      + loop.StagedCount + " 张、桌面素材 " + before + " 张）→ 现在点它一下");

            bool handled = it.ClickCard(card);

            Debug.Log("[AutoPlay/V21] ★点投放区里的卡：" + card.DisplayName + "｜被处理 " + handled
                      + "｜桌面素材 " + before + " → " + r.LiveTableCount() + " 张"
                      + "｜待放置 " + loop.StagedCount + " 张"
                      + "｜启动目标 " + (r.selected != null ? r.selected.name : "（无）")
                      + "｜notice " + loop.notice);
        }

        // ══════════════════════════════════════════════════════════════
        //  刀片候选的"连续换"验证（DSH_BLACKPROBE=1）用的探针 —— 见 ⑳⑦~⑳⑨ 那一段
        //
        //  【它复现的是用户那句话】「在选择刀片的时候桌面上的刀片也要更换」——
        //    现象是"点了没视觉变化，只有按确认之后才换"。
        //    所以每点一次候选都要打一行"桌面刀片卡 = 谁"，让"换没换"变成日志里的字。
        //    ★ 必须在 BladePick 阶段点：SwapBladeWith 的第一道门就是 phase == BladePick。
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// ⑳⑦~⑳⑨ 用：点手牌里的第 <paramref name="index"/> 张**可当核心**的素材
        /// （H&gt;0 的素材，和 CoreCandidate 的判据同一个），然后把它**桌面上那张刀片卡的名字**打出来。
        ///
        /// 【为什么要打"桌面刀片卡 = 谁"】用户报的就是「点了没视觉变化，只有按确认之后才换」——
        ///   这件事在截图里只能看出"卡面好像没变"，日志里有卡名才是证据。
        ///   走的是玩家那条路（TableTurnLoop.SwapBladeWith → PickCoreCandidate），不另开捷径。
        /// </summary>
        private static void ProbeBladePickCandidate(int index, string what)
        {
            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            TableTurnLoop loop = Loop();
            if (setup == null || loop == null)
            {
                Debug.LogWarning("[AutoPlay/Black] " + what + "：找不到 TableSetup / TableTurnLoop，跳过。");
                return;
            }

            // 候选 = 手牌里 H>0 的素材（H=0 的会被 PickCoreCandidate 拒掉，见那里的说明）
            System.Collections.Generic.List<PlayCard> pool = new System.Collections.Generic.List<PlayCard>();
            for (int i = 0; i < setup.hand.Count; i++)
            {
                PlayCard c = setup.hand[i];
                if (c == null || c.bindingMaterial == null) continue;
                if (c.bindingMaterial.H <= 0) continue;
                pool.Add(c);
            }

            if (pool.Count == 0)
            {
                Debug.LogWarning("[AutoPlay/Black] " + what + "：手牌里没有 H>0 的素材，选不了核心。");
                return;
            }

            if (index >= pool.Count)
            {
                Debug.Log("[AutoPlay/Black] " + what + "：可当核心的素材只有 " + pool.Count +
                          " 张（要第 " + (index + 1) + " 张），改点最后一张 —— 至少证明'再点一次'不会把桌面卡点丢。");
                index = pool.Count - 1;
            }

            PlayCard card = pool[index];
            string before = BladeCardText(loop);

            bool ok = loop.SwapBladeWith(card);

            Debug.Log("[AutoPlay/Black] " + what + "：「" + card.DisplayName + "」"
                      + "（H " + card.bindingMaterial.H + " V " + card.bindingMaterial.V + "）"
                      + "｜被接受 " + ok
                      + "｜桌面刀片卡 " + before + " → " + BladeCardText(loop)
                      + "｜notice " + loop.notice);
        }

        /// <summary>桌面上那张刀片卡现在显示的是谁（没建出来就说明白）。</summary>
        private static string BladeCardText(TableTurnLoop loop)
        {
            if (loop == null || loop.bladeCard == null) return "（桌面没有刀片卡）";
            return "「" + loop.bladeCard.DisplayName + "」";
        }

        /// <summary>
        /// ⑳⓪ 用：把桌面第一张素材**拖到玩家这一侧再松手** = 收回手牌。
        ///
        /// 【为什么是 Teleport + DropCard 而不是模拟鼠标】探针不模拟输入事件，玩家那条路
        ///   在松手时的唯一判决就是 TableInteraction.DropCard —— 所以这里只把
        ///   "鼠标把牌拖到哪儿"换成"探针把牌放到哪儿"，判决函数一模一样。
        ///   拖到 z = HandZoneZ 之外（更靠近玩家）才算"收回"，这条线是游戏自己的常量。
        /// </summary>
        private static void ProbeWithdrawTableMaterial(string what)
        {
            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            TableInteraction it = Object.FindObjectOfType<TableInteraction>();
            TableTurnLoop loop = Loop();
            if (setup == null || it == null || loop == null || loop.rulesV21 == null)
            {
                Debug.LogWarning("[AutoPlay/Black] " + what + "：找不到 TableSetup / TableInteraction，跳过。");
                return;
            }

            TableRulesV21 r = loop.rulesV21;
            PlayCard target = FirstTableCard(r);
            if (target == null)
            {
                Debug.LogWarning("[AutoPlay/Black] " + what + "：桌面上没有素材卡，收回这一步跳过。");
                return;
            }

            int tableBefore = r.LiveTableCount();
            int handBefore  = setup.hand != null ? setup.hand.Count : -1;

            // 玩家的鼠标把卡拖到手牌那一片（比 HandZoneZ 再往玩家一侧 0.15，避免压线）
            target.Teleport(new Vector3(target.transform.position.x, 0.022f,
                                        TableInteraction.HandZoneZ - 0.15f),
                            target.homeEuler);
            bool handled = it.DropCard(target, false);

            Debug.Log("[AutoPlay/Black] " + what + "：把桌面上的「" + target.DisplayName + "」拖到手牌区（z " +
                      (TableInteraction.HandZoneZ - 0.15f).ToString("0.##") + "）→ 被处理 " + handled
                      + "｜桌面素材 " + tableBefore + " → " + r.LiveTableCount() + " 张"
                      + "｜3D 手牌 " + handBefore + " → " + (setup.hand != null ? setup.hand.Count : -1) + " 张"
                      + "｜notice " + loop.notice
                      + "｜" + r.ViewSyncSummary());
        }

        /// <summary>
        /// ⑳② 用：本回合**已经启动过**的素材必须收不回来（用户拍板的边界）。
        /// 桌面上找不到这样的卡就如实说"这次没测到"，绝不假装通过。
        /// </summary>
        private static void ProbeWithdrawStartedCheck(string what)
        {
            TableTurnLoop loop = Loop();
            TableInteraction it = Object.FindObjectOfType<TableInteraction>();
            if (loop == null || it == null || loop.rulesV21 == null) return;

            TableRulesV21 r = loop.rulesV21;

            PlayCard target = null;
            GameJam.Rules.MaterialState st = null;
            // 桌面卡用规则层的 FindTable 认（和 ProbeClickTableCard 同一条口径），
            // 再挑"本回合启动过"的那一张 —— 不碰 tableCards（那是规则侧的私有表）
            PlayCard[] all = Object.FindObjectsOfType<PlayCard>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                GameJam.Rules.MaterialState s = r.FindTable(all[i]);
                if (s == null || s.removed || !s.OnTable) continue;
                if (!r.StartedThisTurn(s)) continue;

                st = s;
                target = all[i];
                break;
            }

            if (st == null || target == null)
            {
                Debug.Log("[AutoPlay/Black] " + what + "：桌面上没有'本回合启动过且还在场'的素材"
                          + "（启动过的多半已经被献祭吞噬/形态变化带走了），这一次没测到 —— 不算通过也不算失败。"
                          + "｜桌面 " + r.TableText());
                return;
            }

            int tableBefore = r.LiveTableCount();
            string noticeBefore = loop.notice;
            target.Teleport(new Vector3(target.transform.position.x, 0.022f,
                                        TableInteraction.HandZoneZ - 0.15f),
                            target.homeEuler);
            bool handled = it.DropCard(target, false);

            // ★ 判据是"桌面张数有没有变 + notice 说了什么"，不是 DropCard 的返回值 ——
            //   那个返回值的意思是"这一下输入被交互层消费掉了"（拒绝也是消费），
            //   拿它当"收回成功"会读到反的结论（第一版就是这么写错的）。
            bool refused = (r.LiveTableCount() == tableBefore) && (loop.notice != noticeBefore);

            Debug.Log("[AutoPlay/Black] " + what + "：把**已启动过**的「" + st.name + "」往手牌区拖 → 输入被消费 "
                      + handled
                      + "｜桌面素材 " + tableBefore + " → " + r.LiveTableCount() + " 张"
                      + (r.LiveTableCount() == tableBefore ? "（✓ 没被收走，符合预期）" : "（★ 被收走了，不符合预期！）")
                      + "｜拒绝了 " + refused
                      + "｜notice " + loop.notice);
        }

        /// <summary>桌面上的第一张素材 3D 卡（用规则层的 FindTable 认，不靠名字 —— 名字会随 D 变）。</summary>
        private static PlayCard FirstTableCard(TableRulesV21 r)
        {
            if (r == null) return null;

            PlayCard[] all = Object.FindObjectsOfType<PlayCard>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                GameJam.Rules.MaterialState s = r.FindTable(all[i]);
                if (s == null || s.removed || !s.OnTable) continue;
                return all[i];
            }
            return null;
        }

        // ══════════════════════════════════════════════════════════════
        //  附魔位必过用例（用户报"附魔位不能放卡片"）
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// ★ 拖一张手牌**法术**进「附　魔 位」—— 用户报的"附魔位不能放卡片"的必过用例。
        ///
        /// 【为什么一行日志要拆成四段】"拖过去又弹回来"可能是四处里任何一处：
        ///   ① 落点判定没认到这个槽（FindDropTarget 的矩形 / 余量算错）
        ///   ② 槽位语义把它挡了（CanStageInto 把法术当成"不是法术"）
        ///   ③ 出牌那一步没走到（阶段不对 / 卡没绑上 TableSpellCard）
        ///   ④ 附魔本身没生效（引擎那一侧）
        ///   所以这里把 落点解析到的槽号 / 两个槽的 CanStageInto 判定 / 被处理与否 /
        ///   附魔层数前后 / notice 全打出来 —— 是哪一段断的，一眼看得出来。
        ///
        /// 走的是玩家那条路：TableInteraction.DropCard（松手判决）+ 卡被摆到槽心上。
        /// </summary>
        private static void ProbeSpellIntoEnchantSlot(string what)
        {
            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            TableInteraction it = Object.FindObjectOfType<TableInteraction>();
            TableTurnLoop loop = Loop();
            if (setup == null || it == null || loop == null || loop.rulesV21 == null || setup.board == null)
            {
                Debug.LogWarning("[AutoPlay/Slot] " + what + "：找不到 TableSetup / TableInteraction，跳过。");
                return;
            }

            PlayCard spell = FindHandSpell(setup);
            if (spell == null)
            {
                Debug.LogWarning("[AutoPlay/Slot] " + what + "：手牌里没有法术卡，跳过。");
                return;
            }

            TableRulesV21 r = loop.rulesV21;
            string layersBefore = r.blade.layers.Describe();

            // 玩家的鼠标把这张法术拖到「附魔位」的框里
            spell.Teleport(setup.board.SlotPosition(TableTurnLoop.SlotSpell), spell.homeEuler);

            int resolved = setup.board.FindDropTarget(spell.transform.position, it.snapSlackX, it.snapSlackZ);
            bool canEnchant = loop.CanStageInto(TableTurnLoop.SlotSpell, spell);
            bool canTable   = loop.CanStageInto(TableTurnLoop.SlotMaterial, spell);

            bool handled = it.DropCard(spell, false);

            Debug.Log("[AutoPlay/Slot] " + what + "：把法术「" + spell.DisplayName + "」拖到「附　魔 位」"
                      + "（槽 " + TableTurnLoop.SlotSpell + "，落点 x=" + spell.transform.position.x.ToString("0.###")
                      + " z=" + spell.transform.position.z.ToString("0.###") + "）"
                      + "\n   落点解析到的槽 = " + resolved + "（期望 " + TableTurnLoop.SlotSpell + "）"
                      + "｜CanStageInto(附魔位) = " + canEnchant + "（期望 True）"
                      + "｜CanStageInto(上桌位) = " + canTable + "（法术不该进上桌位）"
                      + "｜被处理 = " + handled
                      + "\n   附魔层数 " + layersBefore + " → " + r.blade.layers.Describe() + "（期望 +1 层）"
                      + "｜手牌 " + r.HandText()
                      + "｜notice " + loop.notice
                      + "｜" + r.ViewSyncSummary());
        }

        /// <summary>
        /// 负例：拖一张手牌**素材**进「附　魔 位」—— 应该被拒，而且提示要说清"该拖到上桌位"。
        /// （用户很可能就是踩了这一下：他往附魔位拖了素材，而旧提示写的是"法术槽只放法术"，
        ///   既没对上桌面牌子上的字、也没告诉他该放哪儿 → "附魔位不能放卡片"。）
        /// </summary>
        private static void ProbeMaterialIntoEnchantSlot(string what)
        {
            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            TableInteraction it = Object.FindObjectOfType<TableInteraction>();
            TableTurnLoop loop = Loop();
            if (setup == null || it == null || loop == null || loop.rulesV21 == null || setup.board == null) return;

            PlayCard mat = FirstHandMaterial(setup);
            if (mat == null)
            {
                Debug.Log("[AutoPlay/Slot] " + what + "：手牌里没有素材（这一步跳过，不算通过也不算失败）。");
                return;
            }

            TableRulesV21 r = loop.rulesV21;
            int tableBefore = r.LiveTableCount();

            mat.Teleport(setup.board.SlotPosition(TableTurnLoop.SlotSpell), mat.homeEuler);
            bool handled = it.DropCard(mat, false);

            Debug.Log("[AutoPlay/Slot] " + what + "：把素材「" + mat.DisplayName + "」拖到「附　魔 位」→ 被处理 "
                      + handled + "｜桌面素材 " + tableBefore + " → " + r.LiveTableCount()
                      + " 张（期望不变：素材不上桌）"
                      + "｜notice " + loop.notice);
        }

        /// <summary>点手牌里的法术（= 附魔的另一条路，应该和拖进附魔位等价）。</summary>
        private static void ProbeClickHandSpell(string what)
        {
            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            TableInteraction it = Object.FindObjectOfType<TableInteraction>();
            TableTurnLoop loop = Loop();
            if (setup == null || it == null || loop == null || loop.rulesV21 == null) return;

            PlayCard spell = FindHandSpell(setup);
            if (spell == null)
            {
                Debug.LogWarning("[AutoPlay/Slot] " + what + "：手牌里没有法术卡，跳过。");
                return;
            }

            TableRulesV21 r = loop.rulesV21;
            string before = r.blade.layers.Describe();

            bool handled = it.ClickCard(spell);

            Debug.Log("[AutoPlay/Slot] " + what + "：点手牌法术「" + spell.DisplayName + "」→ 被处理 " + handled
                      + "｜附魔层数 " + before + " → " + r.blade.layers.Describe()
                      + "｜notice " + loop.notice);
        }

        /// <summary>手牌里的法术卡（权威判据：RebuildHand 绑的 bindingSpell / TableSpellCard）。</summary>
        private static PlayCard FindHandSpell(TableSetup setup)
        {
            if (setup == null || setup.hand == null) return null;

            for (int i = 0; i < setup.hand.Count; i++)
            {
                PlayCard c = setup.hand[i];
                if (c == null) continue;
                if (c.bindingSpell != null) return c;
                if (c.GetComponent<TableSpellCard>() != null) return c;
            }
            return null;
        }

        /// <summary>回菜单（走游戏自己的公开入口 TableTurnLoop.ReturnToTitle）。</summary>
        private static void ProbeReturnToTitle()
        {
            TableTurnLoop loop = Loop();
            if (loop == null)
            {
                Debug.LogWarning("[AutoPlay/Black] 找不到 TableTurnLoop，回菜单这一步跳过。");
                return;
            }

            loop.ReturnToTitle();
            Debug.Log("[AutoPlay/Black] 已回菜单 → 阶段 " + loop.phase);
        }

        /// <summary>
        /// ★ 黑桌探针的核心：把"桌面现在渲染成什么样"量成数字。
        ///
        /// 【为什么必须回读像素，而不是只看截图】
        ///   截图要人眼看，"黑"和"很暗"之间的界线每次都能吵；而用户的报障恰恰是
        ///   "桌子的木质表面纹理不见了"。所以这里 cam.Render() 一帧到临时 RT，
        ///   再把**桌面上四个固定世界点**（近侧空地 / 远端 / 左右两侧）的像素值量出来。
        ///   同一批点在"好的时候"和"黑的时候"各自是什么数，一比就知道是哪一帧开始掉的。
        ///
        /// 【为什么还要把材质 / 光源 / 环境 / 画质一起打】
        ///   "桌面黑"至少有四类根因，在截图里长得一模一样：
        ///     ① 材质被换 / 被销毁（shader 变成 InternalErrorShader、_MainTex 丢了）
        ///     ② 光没了（方向光被销毁 / 被禁用 / 被挤出每物体光源上限）
        ///     ③ 环境光没了（ambient 被改 / 场景没有天空盒）
        ///     ④ 相机坏了（跑到桌子下面、裁剪面改坏、clearFlags 变了）
        ///   这四行一起打，看到数字的那一刻就知道是哪一类，不用猜。
        ///
        /// 【为什么取这四个点】都在桌面上、都不和卡牌 / 蜡烛 / 机器重叠，而且
        ///   分居四个方向 —— 单侧变黑（比如被什么挡住）和整面变黑能分开。
        /// </summary>
        private static void ProbeTableLook(string where)
        {
            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            Camera cam = (setup != null && setup.cam != null) ? setup.cam : Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("[AutoPlay/Black] " + where + "：没有相机，桌面量不了。");
                return;
            }

            // ── ① 桌面材质：贴图还在不在、shader 是不是被换掉了 ──
            string matText = "（找不到名为 Table 的对象）";
            GameObject tableGo = GameObject.Find("Table");
            Renderer tableR = tableGo != null ? tableGo.GetComponent<Renderer>() : null;
            Material tm = tableR != null ? tableR.sharedMaterial : null;
            if (tm != null)
            {
                Texture tex = tm.HasProperty("_MainTex") ? tm.GetTexture("_MainTex") : tm.mainTexture;
                string texText = tex != null
                    ? (tex.name + "（" + tex.width + "×" + tex.height + "）")
                    : "★NULL";

                string tiling = "";
                if (tm.HasProperty("_MainTex"))
                {
                    Vector2 sc = tm.GetTextureScale("_MainTex");
                    tiling = "　Tiling(" + sc.x.ToString("0.##") + ", " + sc.y.ToString("0.##") + ")";
                }

                matText = "shader=" + (tm.shader != null ? tm.shader.name : "★NULL")
                        + "　_Color=" + tm.color.ToString()
                        + "　_MainTex=" + texText + tiling
                        + "　_Glossiness=" + (tm.HasProperty("_Glossiness") ? tm.GetFloat("_Glossiness").ToString("0.##") : "—")
                        + "　renderer.enabled=" + (tableR != null && tableR.enabled);
            }

            // ── ② 光源清单：方向光在不在、有没有被挤掉 ──
            Light[] lights = Object.FindObjectsOfType<Light>();
            int onCount = 0;
            System.Text.StringBuilder lb = new System.Text.StringBuilder();
            for (int i = 0; i < lights.Length; i++)
            {
                Light l = lights[i];
                if (l == null) continue;
                if (l.enabled) onCount++;
                if (lb.Length > 0) lb.Append('｜');
                lb.Append(l.name).Append(' ').Append(l.type)
                  .Append(l.enabled ? " 开" : " ★关")
                  .Append(" i=").Append(l.intensity.ToString("0.##"))
                  .Append(" shadows=").Append(l.shadows);
            }

            // ── ③ 环境光 / 天空盒（Standard 材质没光的时候只剩它） ──
            string amb = "ambientMode=" + RenderSettings.ambientMode
                       + "　ambientLight=" + RenderSettings.ambientLight.ToString()
                       + "　skybox=" + (RenderSettings.skybox != null ? RenderSettings.skybox.name : "★无")
                       + "　reflection=" + RenderSettings.defaultReflectionMode
                       + " " + RenderSettings.reflectionIntensity.ToString("0.##");

            // ── ④ 相机 ──
            string camText = "pos=" + cam.transform.position.ToString("0.###")
                           + "　fwd=" + cam.transform.forward.ToString("0.##")
                           + "　near/far=" + cam.nearClipPlane.ToString("0.###") + "/" + cam.farClipPlane.ToString("0.#")
                           + "　cullingMask=" + cam.cullingMask
                           + "　clear=" + cam.clearFlags
                           + "　bg=" + cam.backgroundColor.ToString()
                           + "　fov=" + cam.fieldOfView.ToString("0.#");

            // ── ⑤ 画质档位（贴图 mip 限制 / 像素光数量都在这里） ──
            int q = QualitySettings.GetQualityLevel();
            string qText = "档位 " + q + "「" + QualitySettings.names[q] + "」"
                         + "　pixelLightCount=" + QualitySettings.pixelLightCount
                         + "　shadows=" + QualitySettings.shadows
                         + "　aniso=" + QualitySettings.anisotropicFiltering
                         + "　textureMipLimit=" + QualitySettings.globalTextureMipmapLimit
                         + "　lodBias=" + QualitySettings.lodBias.ToString("0.##");

            // ── ⑥ 桌面四个固定点的**实际渲染像素** ──
            string pxText = ProbeTablePixels(cam);

            Debug.Log("[AutoPlay/Black] " + where
                      + "\n   桌面材质：" + matText
                      + "\n   桌面像素（4 个固定点）：" + pxText
                      + "\n   光源 " + onCount + "/" + lights.Length + " 开着：" + lb
                      + "\n   环境：" + amb
                      + "\n   相机：" + camText
                      + "\n   画质：" + qText);
        }

        /// <summary>
        /// 把相机渲一帧到临时 RT，回读桌面上四个固定世界点的像素（左下原点）。
        /// 渲染完立刻把 targetTexture / RenderTexture.active 还原 —— 不还原的话
        /// 后面所有截图都会拍到那张 RT（探针自己把画面搞坏，就白测了）。
        /// </summary>
        private static string ProbeTablePixels(Camera cam)
        {
            int w = Mathf.Max(64, cam.pixelWidth);
            int h = Mathf.Max(64, cam.pixelHeight);

            RenderTexture rt = RenderTexture.GetTemporary(w, h, 24);
            RenderTexture prevActive = RenderTexture.active;
            RenderTexture prevTarget = cam.targetTexture;
            Texture2D read = null;

            try
            {
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = prevTarget;

                RenderTexture.active = rt;
                read = new Texture2D(w, h, TextureFormat.RGBA32, false);
                read.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                read.Apply(false, false);
            }
            finally
            {
                cam.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
            }

            if (read == null) return "（回读失败）";

            // 桌面上的四个固定点（世界坐标，y=0 就是桌面顶面）
            Vector3[] pts =
            {
                new Vector3( 0.00f, 0f, -0.55f),   // 近侧空地
                new Vector3( 0.00f, 0f,  0.75f),   // 远端空地
                new Vector3(-1.05f, 0f, -0.30f),   // 左侧
                new Vector3( 1.05f, 0f, -0.30f),   // 右侧
            };
            string[] names = { "近侧", "远端", "左侧", "右侧" };

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < pts.Length; i++)
            {
                Vector3 sp = cam.WorldToScreenPoint(pts[i]);
                if (sb.Length > 0) sb.Append("　");

                if (sp.z <= 0f)
                {
                    sb.Append(names[i]).Append("=（在相机背后）");
                    continue;
                }

                int x = Mathf.Clamp(Mathf.RoundToInt(sp.x), 0, w - 1);
                int y = Mathf.Clamp(Mathf.RoundToInt(sp.y), 0, h - 1);
                Color c = read.GetPixel(x, y);

                sb.Append(names[i]).Append("=(")
                  .Append(Mathf.RoundToInt(c.r * 255f)).Append(',')
                  .Append(Mathf.RoundToInt(c.g * 255f)).Append(',')
                  .Append(Mathf.RoundToInt(c.b * 255f)).Append(')');
            }

            CardFactory.DestroySafe(read);
            return sb.ToString() + "　（RT " + w + "×" + h + "）";
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
