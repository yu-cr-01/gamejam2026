using System.Collections.Generic;
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
    ///   DSH_LAYOUTPROBE=1  桌面素材级联探针（㉔⓪~㉕⑤）：从第 1 回合起一张张素材上桌，
    ///                      每一档都出「桌面视角 + 俯视」截图并打一段布局报告
    ///                      （每张的占地、上缘露出多少、有没有压到刀片位/槽位框/手牌），
    ///                      再把中间那张拖回手牌、看级联自己收拢。默认 0 = 完全不介入。
    ///   DSH_LEVELPROBE=1   选关探针（㉖⓪~㉖⑤）：验用户那句「不要关卡手牌了，就放一个 ui 就行」——
    ///                      v2.1 的关卡界面应当**桌上一张 3D 关卡卡都没有**、只剩关卡窗口，
    ///                      窗口里点一行能选中、按「进入这一关」能开局；旧流程
    ///                      （DSH_RULES_V21=0）那排卡必须原样还在。两个模式共用同一条链，
    ///                      每一步都把"rig 认领几张 / 场景里有几个 BigCard_*"打进日志，
    ///                      截图一一对照。默认 0 = 完全不介入。
    ///   DSH_DECKPROBE=1    选牌组探针（㉜⓪~㉜⑦）：验用户那句「这些流派也做成窗口，去掉卡牌」——
    ///                      v2.1 的牌组界面应当**桌上一张 3D 牌组卡都没有**、只剩牌组窗口，
    ///                      窗口里点一行能选中、按「确认选择该卡组」能进选刀片；旧流程
    ///                      （DSH_RULES_V21=0）那排卡必须原样还在。和选关探针同一套做法
    ///                      （两个模式共用一条链、每一步都把"rig 认领几张 / 场景里几个
    ///                      BigCard_*"打进日志），只是把"选第 2 关"换成"选第 3 副牌"。
    ///                      默认 0 = 完全不介入。
    ///   DSH_FRAMEPROBE=1   取景探针（㉘⓪~㉘④）：验用户那句「手牌最下面那张被视口下边缘切了」——
    ///                      在手牌最宽的"选刀片"那一屏（5 张），把 Game 视图切成
    ///                      当前比例 / 竖屏 900×1600 / 打包版 1600×900 各拍一张，
    ///                      并把**手牌那一排在屏幕上占的包围盒和四条边余量**打进日志。
    ///                      默认 0 = 完全不介入（不会去动 Game 视图的分辨率）。
    ///   DSH_OVERLAPPROBE=1 开场取景 + 相交清单探针（㉙⓪~㉙⑨）：验用户那两句话
    ///                      「你怎么换摄像头视角了？换回来」和「把卡牌和破壁机和计分板穿模的 bug 改一改」——
    ///                      ① 开场那一屏（桌面视角 + 俯视）各拍一张，并把**相机此刻的真实机位**
    ///                         （位置 / 朝向 / fov / 宽高比）打进日志，好和"以前那组写死的坐标"逐个数字对；
    ///                      ② 把桌上这些东西两两量一遍世界包围盒（蜡烛 / 量筒 / 破壁机立绘与机身 /
    ///                         两个槽位框 / 两块槽名牌 / 桌面素材级联列 / 刀片卡与「刀 片」标记 / 手牌那一排），
    ///                         世界 AABB、桌面 XZ、**屏幕 AABB** 三种口径都报，逐对给"重叠多少 / 隔多少"。
    ///                      默认 0 = 完全不介入。
    ///   DSH_OVERLAP_BASELINE=1  配合它用：开场多拍一张**历史机位**（0, 1.05, −1.02 → 0, 0, 0.10）的基线图。
    ///                      这是探针动作（只在副本工程里跑），用来证明"还原回去就是以前那一屏"。
    ///   DSH_CARDFACEPROBE=1 卡面文字探针（㛢⓪~㛢⑨）：验两件用户拿截图报上来的事 ——
    ///                      ① 牌组选择界面（6 张大卡）在**桌面视角 / 俯视**下，每一行文字
    ///                         （标题 / 食材 / 模块 / 开局刀片）都必须看得见
    ///                         （用户那张"下半屏三张牌一个字都没有"就是偏俯视拍的）；
    ///                      ② 手牌与刀片卡**卡面特写**：H/D/V 三个数字要各自落在数值牌上的
    ///                         菱形 / 圆形 / 方形里、居中、不压边框，两位数（16/26）和零值都拍。
    ///                      每一步都先把"每张卡上文字的 sortingOrder vs 卡面的 sortingOrder"
    ///                      打进日志（"文字排在卡面之后"是个可以逐条核对的数字），再截图。
    ///                      默认 0 = 完全不介入（不会临时注册 cardface 机位）。
    ///   DSH_SAVEPROBE=1    存档 / 读档探针（㉞⓪~㉟⑤）：验用户点名的那件事 ——
    ///                      "保存当前关卡状态，读取后能接着打"。造一个有内容的局面
    ///                      （桌面 2 张素材、打了法术、刀片带"不衰退"层）→ 在暂停菜单里
    ///                      点「保　存」→ 退回开场 → 按「继　续」，两边的数字逐条对照；
    ///                      反例也走一遍：把存档改坏之后「继续」必须是灰的 / 明确报错、
    ///                      **不进半残状态**。默认 0 = 完全不介入。
    ///   DSH_SACPROBE=1     献祭吞噬取证探针（㊲⑨~㊶③）：在**真游戏**里抓到一次"真的吞噬成功"——
    ///                      之前所有跑法都在吞噬之前先爆刀（刀片 H 归零）就结束了，
    ///                      所以 `TurnEngine.ResolveSacrifice` 这条分支从来没被实跑覆盖过。
    ///                      这条链造的局面（牌组 0「硫硝爆燃」、刀片核心 = 硝石 H12 V1）：
    ///                        第 1 回合：水 上桌 → 启动 2 次（手牌还没空 → 不是最后一次，不吞噬）
    ///                          外星合金 / 硫磺上桌、火焰打完 → **手牌空**
    ///                          ⇒ 这时每一次启动都必然是"本回合最后一次"
    ///                          · 启动 硫磺（易燃 + 热×1）→ 燃烧形态变化 → 「本次献祭不生效」（反例①）
    ///                          · 形态变化产出的硫磺气打出去 → 手牌又空
    ///                          · 启动 外星合金（D3→2 仍有剩余）→ **吞噬成功**（刀片 H+4、V+2）
    ///                          · 启动 水（D1）→ D 耗尽移除 → 「本次献祭不生效」（反例②）
    ///                        第二段（㊵⓪~㊶③）：手牌故意留一张，把 5 次行动机会打到 0 ——
    ///                          验"用掉最后一个行动机会的那一次"算不算"本回合最后一次启动"。
    ///                      顺带把被吞噬那张卡**飞向罐口**的中间帧拍下来
    ///                      （把 timeScale 压到 0.3；IsConsuming 的张数也逐帧进日志）。
    ///                      默认 0 = 完全不介入。
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

        /// <summary>卡面探针切 Game 视图尺寸前，把原尺寸记在这两个键里（探针跑完要还回去）。</summary>
        private const string KeyGvW = "DSH_AutoPlayGvW";
        private const string KeyGvH = "DSH_AutoPlayGvH";

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
        private static bool   layoutProbe;
        private static bool   levelProbe;
        private static bool   deckProbe;
        private static bool   framingProbe;
        private static bool   overlapProbe;
        private static bool   cardFaceProbe;
        private static bool   saveProbe;
        private static bool   sacProbe;

        /// <summary>
        /// 相交探针拍完之后回哪一步 —— 进探针时按**来路**记下（开场那条链和牌组/关卡那条链
        /// 各有各的下一步：v2.1 是 51/96，旧流程是 31/33/21）。
        /// 用一个字段而不是各写一份 stage，是为了让"拍完接回原链"只有一处实现。
        /// </summary>
        private static int    overlapResume;

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

            // ★ 桌面素材级联探针（DSH_LAYOUTPROBE=1，见 ㉔⓪~㉕⑤ 那一段）：
            //   用户拍板的是"像蜘蛛纸牌一样堆叠素材区"，所以这一条链专门验**故意重叠**的排布：
            //   每张被压住的卡名字还得露着、最靠玩家那张完整、没压到刀片位/槽位框/手牌。
            //   也单独一条链 —— 它会把三张素材一次性铺到桌面上，混进主链会让后面
            //   "验形态变化 / 验回合推进"那几步的前提（桌面只有一张）失效。
            layoutProbe = System.Environment.GetEnvironmentVariable("DSH_LAYOUTPROBE") == "1";

            // ★ 选关探针（DSH_LEVELPROBE=1，见 ㉖⓪~㉖⑤ 那一段）：
            //   用户对关卡的要求从"最好单独开一个窗口"变成了「不要关卡手牌了，就放一个 ui 就行」——
            //   这一条链就验这件事：v2.1 桌上一张 3D 关卡卡都不该有（只剩窗口），
            //   而旧流程那排卡必须还在。也单独一条链，且两个模式共用 ——
            //   "有没有少东西"要两张图并排看才算数。
            levelProbe = System.Environment.GetEnvironmentVariable("DSH_LEVELPROBE") == "1";

            // ★ 选牌组探针（DSH_DECKPROBE=1，见 ㉜⓪~㉜⑦ 那一段）：
            //   用户接着对牌组说「这些流派也做成窗口，去掉卡牌」—— 同一件事再来一遍，
            //   所以这条链和选关那条是**同一个形状**（关窗看空桌 → 点一行 → 确认 → 接回主链），
            //   截图命名也一样（deck_ui_*），两个模式并排看就齐了。
            deckProbe = System.Environment.GetEnvironmentVariable("DSH_DECKPROBE") == "1";

            // ★ 取景探针（DSH_FRAMEPROBE=1，见 ㉘⓪~㉘④ 那一段）：
            //   用户报的是"手牌最下面那张被视口下边缘切了一半"，而"装不装得下"是
            //   按**宽高比**算出来的 —— 所以这一条链要能把 Game 视图切成两个分辨率各拍一张。
            //   单独一个开关：它会去动 Game 视图的分辨率（虽然只动副本工程），
            //   混进别的链会让那些"按屏幕坐标量"的日志换一个口径。
            framingProbe = System.Environment.GetEnvironmentVariable("DSH_FRAMEPROBE") == "1";

            // ★ 开场取景 + 相交清单探针（DSH_OVERLAPPROBE=1，见 ㉙⓪~㉙⑨ 那一段）：
            //   它验的两件事都**只能在真跑起来的时候量**：机位是算出来的（不是常量），
            //   世界包围盒要等美术立绘 / 量筒 / 蜡烛都建出来才有。所以单独一个开关。
            overlapProbe = System.Environment.GetEnvironmentVariable("DSH_OVERLAPPROBE") == "1";

            // ★ 卡面文字探针（DSH_CARDFACEPROBE=1，见 㛢⓪~㛢⑨ 那一段）：
            //   验两件都是"用户拿截图报上来的"事：
            //     ① 三个数字（H/D/V）要各自落进数值牌上的菱形/圆形/方形里 —— 要**卡面特写**才看得清；
            //     ② **所有**卡面文字在俯视和斜视下都得看得见 —— 用户那张"牌组选择界面下半屏
            //        三张牌一个字都没有"是偏俯视机位拍的，所以这一条必须两种机位各拍一次。
            //   也单独一条链：它会把镜头怼到卡面上（临时注册一个 cardface 机位），
            //   混进别的链会让那些"按默认机位量"的日志换一个口径。
            cardFaceProbe = System.Environment.GetEnvironmentVariable("DSH_CARDFACEPROBE") == "1";

            // ★ 存档 / 读档探针（DSH_SAVEPROBE=1，见 ㉞⓪~㉟⑤ 那一段）：
            //   用户点名要的是"保存当前关卡状态、读取后能接着打"，所以这条链要**走玩家那条路**
            //   （暂停菜单里点保存 → 回开场 → 按继续），每一步都把两边的数字打进日志；
            //   反例（改坏存档）也走一遍 —— "不许半读半不读"这条只有真去改坏它才验得到。
            //   单独一条链：它会**覆盖存档文件**，混进主链会让别的验收步骤失去前提。
            saveProbe = System.Environment.GetEnvironmentVariable("DSH_SAVEPROBE") == "1";

            // ★ 献祭吞噬取证探针（DSH_SACPROBE=1，见 ㊲⑧~㊳⑨ 那一段）：
            //   它要的是"真游戏里的一次吞噬成功"，而这条分支**从来只有离线断言覆盖**——
            //   之前的自动试玩总是在吞噬之前先爆刀就结束了。所以单独一条链：
            //   造一个"本回合最后一次启动 + 目标 D 没耗尽 + 刀片 H 够用"的局面。
            //   也单独一个开关：它会把刀片 H 一路用到只剩 1~4、并且**故意让两张卡离场**，
            //   混进主链会让后面那些"验形态变化 / 验回合推进"的步骤失去前提。
            sacProbe = System.Environment.GetEnvironmentVariable("DSH_SACPROBE") == "1";

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
                    if (overlapProbe) { overlapResume = 31; Stage = 288; stageTime = EditorApplication.timeSinceStartup; return; }
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
                    // 选关探针要在这一屏多拍几张（关掉窗口看那排 3D 关卡卡还在不在）
                    // 相交探针也走这一屏：旧流程的**关卡卡排**就在这里（用户要求"别只量 v2.1"）
                    if (overlapProbe) { overlapResume = 33; Stage = 285; stageTime = EditorApplication.timeSinceStartup; return; }
                    Stage = levelProbe ? 260 : 33;
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
                    if (overlapProbe) { overlapResume = 21; Stage = 285; stageTime = EditorApplication.timeSinceStartup; return; }
                    // 选牌组探针要在这一屏多拍几张（关掉窗口看那排 3D 牌组卡还在不在）
                    Stage = deckProbe ? 321 : 21;
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
                    // ★ 存档探针的**冷启动读档**一段（㊱⑥~㊲③）：开机什么都没进，直接按「继续」——
                    //   打包版验收踩到的"读档后卡表解析报告没建出来"只有这条路复现
                    //   （同一会话里先进过一关的话，报告早在 BeginLevel 里建好了，测不出这个洞）。
                    if (saveProbe && ProbeColdLoadAvailable())
                    {
                        Stage = 366;
                        stageTime = EditorApplication.timeSinceStartup;
                        return;
                    }
                    // 相交探针要在**开场这一屏**多拍两张（机位 + 相交清单），拍完回 51 接回主链
                    if (overlapProbe) { overlapResume = 51; Stage = 288; stageTime = EditorApplication.timeSinceStartup; return; }
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
                    // 选关探针要在这一屏多拍几张（关掉窗口看桌面到底有没有 3D 关卡卡）
                    Stage = levelProbe ? 260 : 97;
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
                    // 卡面文字探针：牌组卡排就在这一屏（用户那张"下半屏一个字都没有"的截图）
                    if (cardFaceProbe) { Stage = 310; stageTime = EditorApplication.timeSinceStartup; return; }
                    // 相交探针：牌组卡排就在这一屏（用户新截图说的"蜡烛插穿第一张牌组卡"）
                    if (overlapProbe) { overlapResume = 96; Stage = 285; stageTime = EditorApplication.timeSinceStartup; return; }
                    // 选牌组探针要在这一屏多拍几张（关掉窗口看桌面到底有没有 3D 牌组卡）
                    Stage = deckProbe ? 321 : 96;
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
                    // 取景探针在这一屏做（手里 5 张 = 手牌最宽的一档），拍完回 57 接回主链
                    // 献祭探针要点**点名的那张**当核心（硝石 H12 —— 刀片 H 够 5 次启动），
                    // 所以它自己一步（㊲⑨），不走主链的"第一张"
                    Stage = sacProbe ? 379 : (framingProbe ? 280 : (blackProbe ? 220 : 57));
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
                    // 取景探针要接着在**这一屏**量"提示块压不压手牌"（见 ㉚⓪ 那一段的说明：
                    // 提示块只在正式回合画，开局准备那几屏根本没有它）；
                    // 其余支线各走各的（都不设 = 原来的"只用点击"那条路，行为一个字没变）
                    // 献祭吞噬取证探针要从"第 1 回合、什么都没动"这一屏起步（见 ㊲⑧ 那一段）
                    Stage = sacProbe ? 380
                          : (saveProbe ? 340
                          : (framingProbe ? 270
                          : (cardFaceProbe ? 300
                          : (overlapProbe ? 296
                          : (layoutProbe ? 240
                          : (fxProbe ? 170 : (slotProbe ? 120 : (blackProbe ? 190 : 110))))))));
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ══════════════════════════════════════════════════════
                //  ㉚⓪~㉚④ 取景探针第二段：**操作提示压不压手牌**（DSH_FRAMEPROBE=1）
                //
                //  【为什么必须换到"第 1 回合"这一屏】提示块只在**正式回合**画
                //    （TableHud.OnGUI：开场 / 选关 / 开局准备各走各的分支，IsPreparing 那一档
                //      根本不画提示）—— 在"选刀片"那一屏量，提示块是空的，等于没量。
                //    而用户报的"牌被压住"正是**正式回合**那一屏（提示块 + 手牌排在同一带）。
                //
                //  【量什么】ProbeFramingReport 把手牌包围盒和 HUD 这一帧**真画出来**的矩形
                //    （提示块 / 检视窗口 / v2.1 面板）逐对求交，压住了就把重叠像素数写进日志 ——
                //    "没被任何 UI 压住"是个数字，不是"我看着还行"。
                //    三种比例各拍一张：1470×1167（编辑器那块面板的像素尺寸，宽高比 1.26）、
                //    1600×900（打包版）、900×1600（竖屏，最窄的一档）。
                // ══════════════════════════════════════════════════════

                // ㉚⓪ 1600×900（上一条链切过来的就是这个分辨率）
                case 270:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.2) return;

                    // 状态守卫（和 ㉘⓪ 那条同一个理由）：这一屏要量"提示块压不压手牌"，
                    // 前提是手里还有牌。这台机器上同时跑着别的 Unity 实例，它的自动化脚本
                    // 会点到最前面那个窗口 —— 点到这边就等于替玩家把牌全打出去了（实测踩过：
                    // 走到第 1 回合时手里 0 张，量出来的是"手牌是空的"）。
                    // 遇到这种局面就**走游戏自己的入口重开一局**（Begin → 点书 → 选关 →
                    // 选牌组 → 选刀片），再从 57 重新走到这一屏。
                    if (HandCardCount() == 0)
                    {
                        Debug.LogWarning("[AutoPlay/取景] 走到第 1 回合时手里一张牌都没有"
                                         + "（被别的进程点掉了）—— 重开一局再走一遍。");
                        Loop().Begin();
                        ProbeEnsureBladePick();
                        Stage = 57;
                        stageTime = EditorApplication.timeSinceStartup;
                        return;
                    }

                    if (Mathf.Abs(Object.FindObjectOfType<TableSetup>().cam.aspect - 1600f / 900f) > 0.02f)
                        ForceCameraAspect(1600f / 900f);
                    if (!Shot("fit_10_1600x900_ui.png")) return;
                    ProbeFramingReport("㉚⓪ 打包版比例 1600×900｜第 1 回合（提示块 + 手牌）");
                    Stage = 271;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉚① 切竖屏
                case 271:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ClearCameraAspect();
                    SetGameViewSize(900, 1600);
                    Stage = 272;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉚② 竖屏
                case 272:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.6) return;
                    if (Mathf.Abs(Object.FindObjectOfType<TableSetup>().cam.aspect - 900f / 1600f) > 0.02f)
                        ForceCameraAspect(900f / 1600f);
                    if (!Shot("fit_11_portrait_ui.png")) return;
                    ProbeFramingReport("㉚② 竖屏 900×1600｜第 1 回合（提示块 + 手牌）");
                    Stage = 273;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉚③ 切回编辑器那块面板自己的像素尺寸（宽高比 1.26 —— 用户编辑器就是这个比例）
                case 273:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ClearCameraAspect();
                    SetGameViewSize(1470, 1167);
                    Stage = 274;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉚④ 1.26（编辑器比例）→ 拍完验"设置里关掉提示"
                case 274:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.6) return;
                    if (Mathf.Abs(Object.FindObjectOfType<TableSetup>().cam.aspect - 1470f / 1167f) > 0.02f)
                        ForceCameraAspect(1470f / 1167f);
                    if (!Shot("fit_12_editor_ui.png")) return;
                    ProbeFramingReport("㉚④ 编辑器比例 1470×1167｜第 1 回合（提示块 + 手牌）");
                    Stage = 275;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉚⑤ 设置里把提示关掉 —— 用户口径是"ShowHints=false 时照旧全隐"，
                //      这一条**必须自己验**：改了摆法之后，"关掉就不画"还得成立。
                case 275:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.6) return;
                    TableSettings.ShowHints = false;
                    Debug.Log("[AutoPlay/取景] 提示开关 → ShowHints=false（这一屏提示块应当整块不画）");
                    Stage = 276;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉚⑥ 关掉提示的这一屏：日志里会写明"提示块：未画"
                case 276:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.2) return;
                    if (!Shot("fit_13_no_hints_ui.png")) return;
                    ProbeFramingReport("㉚⑥ ShowHints=false｜第 1 回合");
                    // ★ 还原**必须另起一个 stage**：CaptureScreenshot 是**帧末**写盘的，
                    //   同一帧里把开关改回去 → 落盘的就是改回去那一帧（图里又出现提示块了，
                    //   实测踩过：日志说"提示块未画"、图上却还在）。文件头那条规矩对"改回来"同样成立。
                    Stage = 277;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉚⑦ 还原提示开关，接回主链（后面那些回归图要照旧）
                case 277:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.5) return;
                    TableSettings.ShowHints = true;
                    Debug.Log("[AutoPlay/取景] 提示开关 → 已还原 ShowHints=true");
                    Stage = layoutProbe ? 240 : 110;
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
                //  ㉔⓪~㉕⑤ 桌面素材级联排布（DSH_LAYOUTPROBE=1）
                //
                //  【它验的是用户拍板的那句话】「像蜘蛛纸牌一样堆叠素材区」——
                //    一列级联、后一张压住前一张的下半截、**每张被压住的卡都还看得见名字**
                //    （名字在卡面上部，露出来的上缘正是它）、最靠玩家那张完整可见，
                //    而这一列不许压到刀片位 / 两个槽位框（附魔位·上桌位）/ 手牌那一排。
                //
                //  【为什么每一步都打一段 TableLayoutReport】级联是"故意压"的，
                //    "压得对不对"没法靠看缩略图定（错开量小 1 厘米，名字就被切一半，
                //    而图上看不出来）。报告把每张的占地矩形、上缘露出多少、
                //    和五个障碍有没有交集全量成数字 —— 违反时日志里直接是 ★，
                //    判据和实机自检（TableRulesV21.VerifyTableLayout）是同一份实现。
                //
                //  【为什么还要跑一遍"收回手牌"】级联的位置全部由状态算出来
                //    （SyncTableVisuals 的第③步），所以抽掉中间那张之后整列必须**自己收拢** ——
                //    这一步验的就是"收得回来"，顺带看 ViewSyncSummary 的"残留 0 张"。
                //
                //  ★ 一律"先截图、后量"：Shot 在距上一张不到 ShotSettle 时会返回 false
                //    让本阶段重试，把量测放在它前面会把同一份数据每帧打一遍（日志刷屏）。
                // ══════════════════════════════════════════════════════

                // ㉔⓪ 第 1 回合、桌面 0 张素材（对照图：列还没起）
                case 240:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    if (!Shot("layout_00_empty_board.png")) return;
                    ProbeLayoutReport("㉔⓪ 桌面 0 张素材（对照）");
                    Stage = 241;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉔① 出第 1 张素材（走玩家那条路：摆进上桌位 + 确认）
                case 241:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeStageMaterialV21();
                    Stage = 242;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉔② 1 张：第一张在最上方（离玩家最远）
                case 242:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.4) return;
                    if (!Shot("layout_01_one_board.png")) return;
                    ProbeLayoutReport("㉔② 级联 1 张");
                    Stage = 243;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉔③ 出第 2 张（追加到级联末尾 = 最靠玩家那一端）
                case 243:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeStageMaterialV21();
                    Stage = 244;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉔④ 2 张：后一张压住前一张的下半截，前一张露出名字
                case 244:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.4) return;
                    if (!Shot("layout_02_two_board.png")) return;
                    ProbeLayoutReport("㉔④ 级联 2 张");
                    Stage = 245;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉔⑤ 出第 3 张 —— 就是用户截图里"右上角那张"的位置（原来是压在刀片卡上的那个）
                case 245:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeStageMaterialV21();
                    Stage = 246;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉔⑥⑦ 3 张 + 刀片卡：桌面视角一张（用户看的就是这个机位）
                //       —— 两张被压住的必须都还看得见名字，最后一张完整
                case 246:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.4) return;
                    if (!Shot("layout_03_three_board.png")) return;
                    ProbeLayoutReport("㉔⑦ 级联 3 张 + 刀片卡（桌面视角）");
                    ProbeTextMeshDiag("㉔⑦ 桌面视角：名字/属性两个 TextMesh 的渲染状态");
                    ProbeLogSync("㉔⑦ 级联 3 张（状态 vs 画面）");
                    Stage = 247;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉔⑧ 切俯视
                case 247:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    GoToView("top");
                    Stage = 248;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉔⑨ 俯视：这一张看"级联方向对不对"—— 从上往下看，每张只露出上缘那一条
                case 248:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.2) return;
                    if (!Shot("layout_04_three_top.png")) return;
                    ProbeLayoutReport("㉔⑨ 级联 3 张（俯视）");
                    ProbeTextMeshDiag("㉔⑨ 俯视：名字/属性两个 TextMesh 的渲染状态");
                    Stage = 249;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉕⓪ 切回桌面视角（后面拍"收回手牌"也在这个机位）
                case 249:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    GoToView("board");
                    Stage = 250;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉕① 把**中间那张**拖回手牌区（抽中间的才看得出"整列会不会自己收拢"）
                case 250:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeWithdrawTableCardAt(1, "㉕① 抽掉级联中间那张");
                    Stage = 251;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉕②③ 收回之后：剩 2 张、整列收拢；顺带看"状态与画面一致｜残留 0 张"
                case 251:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.4) return;
                    if (!Shot("layout_05_after_withdraw_board.png")) return;
                    ProbeLayoutReport("㉕③ 收回手牌之后（剩 2 张）");
                    ProbeLogSync("㉕③ 收回手牌之后");
                    Stage = 252;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉕④⑤ 俯视再看一眼收拢后的两列张
                case 252:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    GoToView("top");
                    Stage = 253;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 253:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.2) return;
                    if (!Shot("layout_06_after_withdraw_top.png")) return;
                    ProbeLayoutReport("㉕⑤ 收回手牌之后（俯视）");
                    Stage = 256;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉕⑦⑧ 切「手牌特写」拍一张 —— 手牌和桌面卡走的是**同一个造卡出口**
                //       （CardFactory.BuildCard → AddText 的名字那一行），
                //       所以"名字看得见"这条在手牌上也必须成立（顺带看牌组选择界面那张
                //       也在同一批截图里：v21_choice_deck.png）
                case 256:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    GoToView("hand");
                    Stage = 257;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 257:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.4) return;
                    if (!Shot("layout_07_hand_view.png")) return;
                    ProbeTextMeshDiag("㉕⑧ 手牌特写：手牌上名字/属性 TextMesh 的渲染状态");
                    Stage = 254;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉕⑥ 离线扫描：n = 1..8 的级联坐标逐档验一遍（不用真的凑出 8 张素材）
                //      —— "以后张数变多会不会撞"当下就有答案
                case 254:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    ProbeCascadeSweep(8);
                    Stage = 255;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 255:
                    if (EditorApplication.timeSinceStartup - stageTime < 3.0) return;
                    Finish();
                    return;

                // ══════════════════════════════════════════════════════
                //  ㉘⓪~㉘④ 取景探针：手牌那一排在任何宽高比下都完整可见（DSH_FRAMEPROBE=1）
                //
                //  【它验的是用户那句话】「v2.1 里手牌那张法术卡被视口下边缘切掉了一半 ——
                //    它挂在画面最底部、「附 魔 位 / 上 桌 位」两行字的下方，只露出上半截」。
                //
                //  【为什么必须换着分辨率拍】"装不装得下"是**按宽高比**算出来的
                //    （水平半角 = 垂直半角 × aspect）：竖着的窗口和 1600×900 拟合出来的
                //    不是同一台机位。只在自己这台机器的面板比例下拍一张，等于只验了一半 ——
                //    而用户点名的两个比例恰好是"编辑器 Free Aspect（接近竖长）"和"打包版 1600×900"。
                //
                //  【为什么接在"选刀片"这一屏】那一刻手里正好 **5 张**（4 素材 + 1 法术），
                //    是手牌最宽的一档：手牌越宽，纵向越容易沉出下边缘、横向越容易出两边。
                //    拍完接回主链（㊼ 选核心 → ㊽ 确认 → ㊾ 第 1 回合），后面流程一个字不变。
                //
                //  【量什么】ProbeFramingReport 把每张手牌的四角用**真实投影**
                //    （Camera.WorldToScreenPoint）投到屏幕上，报出屏幕包围盒和四条边余量 ——
                //    "完整可见"是个数字（余量 > 0），不是"我看着还行"。
                // ══════════════════════════════════════════════════════

                // ㉘⓪ 编辑器当前比例（Game 视图 Free Aspect 那种）
                case 280:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    // ★ 先确认"手里真有 5 张"：这一屏会被别人动过（见 ProbeEnsureBladePick）
                    ProbeEnsureBladePick();
                    if (!Shot("fit_00_free_5cards.png")) return;
                    ProbeFramingReport("㉘⓪ 编辑器当前比例｜5 张手牌（选刀片那一屏）");
                    Stage = 281;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉘① 切成竖屏分辨率：宽高比 < 1 时水平可视范围最窄，横向最容易出画
                case 281:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    SetGameViewSize(900, 1600);
                    Stage = 282;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉘② 竖屏：整排必须都在画面里（含最外侧那两张的角）
                case 282:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.6) return;   // 等宽高比变化触发取景重算
                    ProbeEnsureBladePick();          // 再确认一次状态（这一条链全程都可能被别的进程点到）
                    // 分辨率切不动（内部 API 在这个版本上变了）→ 退到"只压相机比例"：
                    // 取景是按 cam.aspect 算的，压了比例算出来的就是同一台机位，照样能验。
                    if (Mathf.Abs(Object.FindObjectOfType<TableSetup>().cam.aspect - 900f / 1600f) > 0.02f)
                        ForceCameraAspect(900f / 1600f);
                    if (!Shot("fit_01_portrait_5cards.png")) return;
                    ProbeFramingReport("㉘② 竖屏 900×1600｜5 张手牌");
                    Stage = 283;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉘③ 切成打包版那个分辨率
                case 283:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ClearCameraAspect();                 // 先把兜底压的比例还原，再试着真换分辨率
                    SetGameViewSize(1600, 900);
                    Stage = 284;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉘④ 1600×900（打包版的比例）：同上，整排完整可见
                case 284:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.6) return;
                    ProbeEnsureBladePick();
                    if (Mathf.Abs(Object.FindObjectOfType<TableSetup>().cam.aspect - 1600f / 900f) > 0.02f)
                        ForceCameraAspect(1600f / 900f);
                    if (!Shot("fit_02_1600x900_5cards.png")) return;
                    ProbeFramingReport("㉘④ 打包版比例 1600×900｜5 张手牌");
                    Stage = 279;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉘⑤ 把兜底压上去的相机比例还原，接回主链 —— 后面那些回归图要在
                //      **这台机器原本的比例**下拍（不然黑边会跟着进 layout_* 那几张）
                //      ★ 用 279 而不是 285：285~299 那一段被相交探针（㉙⓪）占了，
                //        两条链各写各的 stage 号，抢号会直接编不过（switch 重复标签）。
                case 279:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.5) return;
                    ClearCameraAspect();
                    Stage = 57;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ══════════════════════════════════════════════════════
                //  ㉙⓪~㉙⑨ 开场取景 + 相交清单（DSH_OVERLAPPROBE=1）
                //
                //  【它验的是用户这两句话】
                //    ① 「不是，你怎么换摄像头视角了？换回来」—— 配图是开场那一屏。
                //       "机位有没有被换掉"不能靠看两张缩略图：**先把它量成数字**。
                //       LogCameraPose 把相机此刻的位置 / 朝向 / fov / 宽高比打出来，
                //       和 BuildCamera 里那组历史坐标（0, 1.05, −1.02）→（0, 0, 0.10）逐个数对。
                //    ② 「把卡牌和破壁机和计分板穿模的 bug 改一改」——
                //       ProbeOverlapReport 把桌上这些东西两两量一遍世界包围盒。
                //
                //  【为什么"穿模"要用三种口径量】
                //    用户看到的"叠在一起"有两种，长得像、根因完全不同：
                //      · 世界 AABB 相交 = 两个东西真的占同一块空间（深度上打架）；
                //      · 世界 AABB **不相交**、屏幕 AABB 相交 = 它们在不同的深度上，
                //        只是这个机位下前后投影叠在一起（量筒压在蜡烛底座旁就是这一种 ——
                //        量筒在 z ≈ +0.10、蜡烛在 z ≈ +0.34，隔了 24 厘米）。
                //    只报一种，另一种就会被当成"没事"。所以三种都报。
                //
                //  【为什么先拍开场这一屏】用户点名的就是它（书 / 木牌 / 蜡烛 / 破壁机同框），
                //    而且这一屏**桌上没有卡**，相交清单最干净（正好把用户报的
                //    "量筒 × 蜡烛"单独拎出来）。
                // ══════════════════════════════════════════════════════

                // ㉙⑩ 定下"改前 / 改后同一个窗口尺寸"这一条：切成 1600×900（打包版比例）。
                //
                //  【为什么非要换】用户那张"6 副牌组排成一行、蜡烛从第一张卡中间穿出来、
                //    破壁机压在第 4/5 张上"的截图是**宽窗口**下拍的：宽高比一大，
                //    那一排就从 2 行 3 张变成 1 行 6 张、铺满整屏 —— 窄窗口里根本复现不出来。
                //    打包版也是 1600×900，所以这一档就是玩家真正看到的比例。
                //    ★ 原来那个 SetGameViewSize 在这台机器上抛 NRE（见 SetGameViewSizeEx 的说明），
                //      这里走自己那一份；切不动就照当前尺寸拍（两边同口径仍然成立）。
                case 288:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.6) return;
                    if (EnvInt("DSH_OVERLAP_SIZE16X9", 1) == 1) SetGameViewSizeEx(1600, 900);
                    Debug.Log("[AutoPlay/相交] ㉙⑩ 这一轮所有截图都用同一个 Game 视图尺寸："
                              + Screen.width + "×" + Screen.height
                              + "（改前 / 改后同口径，才能逐对数字对比）");
                    Stage = 290;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉙⓪ 开场界面：桌面视角（用户配图那一屏）
                case 290:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.8) return;   // 等分辨率切换生效
                    if (!Shot("ov_10_title_board.png")) return;
                    LogCameraPose("㉙⓪ 开场·桌面视角");
                    ProbeOverlapReport("㉙⓪ 开场·桌面视角");
                    Stage = 291;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉙① 切俯视
                case 291:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    GoToView("top");
                    Stage = 292;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉙② 开场·俯视：这一张看"谁和谁在桌面上真的占同一块地方"
                case 292:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.2) return;
                    if (!Shot("ov_11_title_top.png")) return;
                    LogCameraPose("㉙② 开场·俯视");
                    ProbeOverlapReport("㉙② 开场·俯视");
                    Stage = 293;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉙③ 回桌面视角；开了 DSH_OVERLAP_BASELINE=1 就**多拍一张历史机位的基线**
                //
                //  ★ 这里那组坐标不是新写的：它就是 BuildCamera 里从 755313c（3D 桌面第一版）
                //    起一直没动过的那一行 `rig.Register("board", (0, 1.05, −1.02), (0, 0, 0.10))`。
                //    临时重设它 = 把"以前那一屏"在同一帧、同一个窗口尺寸下再拍一遍，
                //    于是"还原之后和以前一样"这句话有图可对（而不是拿两张隔了好几天的图比）。
                //    纯探针动作：只在副本工程里跑，不改工程、不改游戏里的任何常量。
                case 293:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    GoToView("board");

                    if (EnvInt("DSH_OVERLAP_BASELINE", 0) == 1)
                    {
                        TableSetup s = Object.FindObjectOfType<TableSetup>();
                        if (s != null && s.rig != null)
                        {
                            s.rig.Register("board", new Vector3(0f, 1.05f, -1.02f), new Vector3(0f, 0f, 0.10f));
                            s.rig.SnapTo("board");
                            Debug.Log("[AutoPlay/相交] ㉙③ 已把「桌面视角」临时设回历史坐标 "
                                      + "(0, 1.05, −1.02) → (0, 0, 0.10)（探针动作，不改工程）");
                            Stage = 294;
                            stageTime = EditorApplication.timeSinceStartup;
                            return;
                        }
                    }

                    Stage = overlapResume;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉙④ 历史机位下的开场界面（基线图）
                case 294:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    if (!Shot("ov_12_title_history.png")) return;
                    LogCameraPose("㉙④ 开场·历史机位（基线）");
                    Stage = overlapResume;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ══════════════════════════════════════════════════════
                //  ㉙⑤~㉙⑦ 牌组卡排 / 关卡卡排（用户追加的那一条：蜡烛插穿第一张牌组卡）
                //
                //  【它验的是用户新截图的这一幕】牌组选择界面（6 副）：
                //    蜡烛从第一张卡（硫硝爆燃）中间穿出来、破壁机立绘压在第 4/5 张上。
                //
                //  【为什么这条链要能被两条路进来】牌组卡排（v2.1 与旧流程都有）和
                //    关卡卡排（只有旧流程在桌上摆 3D 卡）用的是同一套布局（TableChoiceRig.LayoutCards），
                //    所以这里只写一条链，来路各记各的"下一步"（overlapResume）——
                //    v2.1 从 ⑤④ 牌组界面进来回 ⑨⑥，旧流程从 ㉜ 关卡界面进来回 ㉝。
                // ══════════════════════════════════════════════════════

                // ㉙⑤ 牌组/关卡卡排：桌面视角
                case 285:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.2) return;
                    if (!Shot("ov_30_bigrow_board.png")) return;
                    LogCameraPose("㉙⑤ 卡排·桌面视角");
                    ProbeOverlapReport("㉙⑤ 卡排·桌面视角");
                    Stage = 286;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉙⑥ 切俯视（俯视最能看清"卡片和物件在桌面上是不是占同一块地方"）
                case 286:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    GoToView("top");
                    Stage = 287;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 287:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.2) return;
                    if (!Shot("ov_31_bigrow_top.png")) return;
                    LogCameraPose("㉙⑦ 卡排·俯视");
                    ProbeOverlapReport("㉙⑦ 卡排·俯视");
                    GoToView("board");
                    Stage = overlapResume;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ══════════════════════════════════════════════════════
                //  㛢⓪~㛢⑨ 卡面文字探针（DSH_CARDFACEPROBE=1）
                //
                //  【它验的是用户拿截图报上来的两件事】
                //    ① 「牌组选择界面**下面一排三张牌完全没有文字**」——
                //       用户那屏是**偏俯视**机位，而同一屏上面一排的标题却看得见：
                //       说明"文字和卡面谁盖谁"这件事当时是**交给距离去排**的
                //       （Transparent 队列里按包围盒中心离相机多远排），机位一换就翻脸。
                //       修法是把次序写死（CardFactory.CardTextSortingOrder = 1，
                //       卡面是 0），所以这里必须**俯视 + 斜视各拍一次**才叫验过。
                //    ② 三个属性从"三行字（H 盐性 12 …）"改成"三个数字分别进
                //       数值牌上的菱形/圆形/方形"—— 这一条不看特写根本量不了：
                //       数字有没有居中、有没有压到形状边框、两位数挤不挤得下，
                //       在整桌那一屏里只有几个像素。
                //
                //  【为什么每一步都先打一份"文字次序"清单】"文字排在卡面之后"是个**数字**
                //    （每个文字渲染器的 sortingOrder 对卡面的 sortingOrder），
                //    截图只能证明"这一屏这一次没出事"，清单能证明"每一张卡的每一行都排在后面"。
                //    两者都要有 —— 这也正是用户上一轮"只给名字设了次序"能漏过去的原因。
                //
                //  【为什么单开一个临时机位】卡面特写要把镜头怼到一张牌上（离地约 0.4 米），
                //    而游戏里的机位都是"装得下整张桌子 / 整排手牌"的。临时 Register 一个
                //    `cardface` 机位属于**探针动作**（只在副本工程里跑，不改工程里任何常量），
                //    拍完切回 board，和相交探针那套做法一致。
                // ══════════════════════════════════════════════════════

                // 㛢⓪ 刀片卡特写（斜视 62°）：三属性里数字最多的那一张
                //   （DSH_DECK_INDEX=1 → 刀片是秘银锭 H=16 D=2 V=0：两位数 + 一位数 + 零值，
                //    三种情况在同一张卡上，正好一次拍全）
                case 300:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    if (!ShotCardCloseup("cf_10_blade_oblique.png", BladeCard(), 62f)) return;
                    Debug.Log("[AutoPlay/卡面] 㛢⓪ 刀片卡特写（62°）：" + CardStatsText(BladeCard()));
                    Stage = 301;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // 㛢① 同一张刀片卡的更陡机位（78°）—— 俯视下三个形状最容易看清
                case 301:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    if (!ShotCardCloseup("cf_11_blade_top.png", BladeCard(), 78f)) return;
                    Stage = 302;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // 㛢②~㛢④ 手牌里每一张素材各来一张俯视特写（一位数的那几张）
                case 302:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    if (!ShotCardCloseup("cf_20_hand0_top.png", HandMaterial(0), 78f)) return;
                    Debug.Log("[AutoPlay/卡面] 㛢② 手牌素材 #0：" + CardStatsText(HandMaterial(0)));
                    Stage = 303;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 303:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    if (!ShotCardCloseup("cf_21_hand1_top.png", HandMaterial(1), 78f)) return;
                    Debug.Log("[AutoPlay/卡面] 㛢③ 手牌素材 #1：" + CardStatsText(HandMaterial(1)));
                    Stage = 304;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 304:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    if (!ShotCardCloseup("cf_22_hand2_top.png", HandMaterial(2), 78f)) return;
                    Debug.Log("[AutoPlay/卡面] 㛢④ 手牌素材 #2：" + CardStatsText(HandMaterial(2)));
                    Stage = 305;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // 㛢⑤ 造三张**对照卡**：两位数 / 一位数 / 零值各一张。
                //   【为什么非要造】真实卡表里 v2.1 素材的 H/D/V 全是 1 位数、没有 0 ——
                //   而用户点名要看"两位数放不放得下""零值会不会偏小偏空"。
                //   造法走的是 CardFactory.Create 那条真路（只临时改运行期的 attrs，立刻改回）。
                case 305:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    BuildDemoStatCards();
                    Stage = 306;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // 㛢⑥~㛢⑧ 三张对照卡各一张俯视特写
                case 306:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    if (!ShotCardCloseup("cf_40_demo_12_3_1.png", DemoCard(0), 78f)) return;
                    Stage = 307;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 307:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    if (!ShotCardCloseup("cf_41_demo_3_3_3.png", DemoCard(1), 78f)) return;
                    Stage = 308;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 308:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    if (!ShotCardCloseup("cf_42_demo_8_0_0.png", DemoCard(2), 78f)) return;
                    Stage = 309;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // 㛢⑨ 量三个数字（字号 / 墨迹盒 / 余量 / 居中误差）→ 销毁对照卡 → 接回主链
                case 309:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeStatDigitReport("㛢⑨ 三个数字的实测（字号 / 墨迹盒 / 余量 / 居中误差）");
                    DestroyDemoStatCards();
                    // 回归：桌面素材级联的自检 + "状态与画面一致 / 残留 0 张"
                    //   （对照卡是探针自己造的、挂在独立根节点下，这里刚销毁 —— 它不该出现在残留里）
                    ProbeLogSync("㛢⑨ 卡面特写之后（回归：状态与画面一致 / 残留 0 张）");
                    GoToView("board");
                    Stage = 110;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // 㛢⑩ 牌组选择界面·桌面视角（斜视）：每一行文字都得看得见
                //
                //  ★ 开拍之前先把 Game 视图**钉到固定尺寸**。理由：这一条链要出
                //    **改前 / 改后对照图**（同一个探针跑两遍，中间只改一处渲染次序），
                //    而"上一遍跑完留下的窗口尺寸"会让两遍落在不同宽高比上 ——
                //    实测第二遍变成 3840×2160，牌组卡的排布和字号全跟着变，两张图没法并排看。
                //    显式钉住，两遍才是同一机位、同一批卡、同一个窗口。
                case 310:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    SetGameViewSizeEx(CardFaceProbeW, CardFaceProbeH);
                    SessionState.SetInt(KeyGvW, CardFaceProbeW);
                    SessionState.SetInt(KeyGvH, CardFaceProbeH);
                    Debug.Log("[AutoPlay/卡面] 㛢⑩ Game 视图钉到 " + CardFaceProbeW + "×" + CardFaceProbeH
                              + "（现在 " + Screen.width + "×" + Screen.height
                              + "）—— 改前 / 改后两遍必须同口径");
                    Stage = 311;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 311:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.6) return;
                    if (!Shot("cf_30_decks_board.png")) return;
                    ProbeCardTextReport("㛢⑩ 牌组界面·桌面视角");
                    ProbeLogSync("㛢⑩ 牌组界面·桌面视角（回归：状态与画面一致 / 残留 0 张）");
                    Stage = 312;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // 㛢⑪ 牌组选择界面·俯视（★ 用户那张截图就是这一档）
                case 312:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    GoToView("top");
                    Stage = 313;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 313:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.2) return;
                    if (!Shot("cf_31_decks_top.png")) return;
                    ProbeCardTextReport("㛢⑪ 牌组界面·俯视");
                    // 㛢⑫ 再拉近一点拍两张**牌组卡**的特写：整屏看不出一行字有没有被盖住，
                    //   特写下"这一行在不在"是一眼的事（牌组卡的标题/三行说明都在近端）
                    Stage = 314;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 314:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    if (!ShotBigCardCloseup("cf_32_deckcard0_top.png", 0)) return;
                    Stage = 315;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 315:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    if (!ShotBigCardCloseup("cf_33_deckcard3_top.png", 3)) return;
                    Stage = 316;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // 㛢⑬~㛢⑯ 把 Game 视图切成**窄窗口**再来一遍。
                //
                //  【为什么非要切】用户那张"下面一排三张牌完全没有文字"的截图里，6 副牌组是
                //    **3 + 3 两排**的；而 1470×1167 这种偏宽的窗口下 TableChoiceRig 会把它们排成
                //    **一行 6 张**（日志 [V21][牌组排] 会写"1 行"）—— 那样"下面一排"根本不存在，
                //    等于没验到用户报的那一幕。竖屏 900×1600 才会折成两排。
                //    （这和取景探针切成竖屏是同一个理由：宽高比一变，布局就换一种排法。）
                case 316:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    SessionState.SetInt(KeyGvW, Screen.width);
                    SessionState.SetInt(KeyGvH, Screen.height);
                    SetGameViewSizeEx(900, 1600);
                    Debug.Log("[AutoPlay/卡面] 㛢⑬ Game 视图切成 900×1600（原 "
                              + SessionState.GetInt(KeyGvW, 0) + "×" + SessionState.GetInt(KeyGvH, 0)
                              + "）—— 牌组卡这一屏在窄窗口下才会排成两排");
                    Stage = 317;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // 㛢⑭ 窄窗口·桌面视角：**下面那一排**的文字必须在
                case 317:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.6) return;
                    if (!Shot("cf_34_decks_narrow_board.png")) return;
                    ProbeCardTextReport("㛢⑭ 牌组界面·窄窗口 900×1600·桌面视角");
                    Stage = 318;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // 㛢⑮ 窄窗口·俯视（★ 用户那张就是俯视）
                case 318:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    GoToView("top");
                    Stage = 319;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 319:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.6) return;
                    if (!Shot("cf_35_decks_narrow_top.png")) return;
                    ProbeCardTextReport("㛢⑮ 牌组界面·窄窗口·俯视");
                    ProbeLogSync("㛢⑮ 牌组界面·窄窗口（回归：状态与画面一致 / 残留 0 张）");
                    Stage = 320;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // 㛢⑯ 把 Game 视图和机位都还原，接回主链（㊴ 选刀片之前）
                case 320:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    SetGameViewSizeEx(SessionState.GetInt(KeyGvW, 1470), SessionState.GetInt(KeyGvH, 1167));
                    GoToView("board");
                    Stage = 96;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉙⑤⑥ 进关之后往桌上出两张素材（走玩家那条路：摆进上桌位 + 确认）。
                //   为什么要两张：一张看不出"级联"，而量筒/蜡烛/破壁机与**整列级联**的关系
                //   正是用户报的那一类（卡牌 × 破壁机 × 计分板）。
                case 296:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeStageMaterialV21();
                    Stage = 297;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 297:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeStageMaterialV21();
                    Stage = 298;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉙⑦ 桌面视角：级联 + 刀片卡 + 量筒 + 蜡烛 + 破壁机同框
                case 298:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.4) return;
                    if (!Shot("ov_20_inlevel_board.png")) return;
                    LogCameraPose("㉙⑦ 进关后·桌面视角");
                    ProbeOverlapReport("㉙⑦ 进关后·桌面视角");
                    ProbeLogSync("㉙⑦ 进关后（2 张素材在桌上）");
                    Stage = 299;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉙⑧ 切俯视
                case 299:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    GoToView("top");
                    Stage = 289;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉙⑨ 进关后·俯视：这一张看"整列级联到底有没有顶到破壁机"
                case 289:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.2) return;
                    if (!Shot("ov_21_inlevel_top.png")) return;
                    LogCameraPose("㉙⑨ 进关后·俯视");
                    ProbeOverlapReport("㉙⑨ 进关后·俯视");
                    ProbeLogSync("㉙⑨ 进关后（俯视）");
                    Finish();
                    return;

                // ══════════════════════════════════════════════════════
                //  ㉖⓪~㉖⑤ 选关：v2.1 只剩 UI 窗口（DSH_LEVELPROBE=1）
                //
                //  【它验的是用户这句话】「不要关卡手牌了，就放一个 ui 就行」——
                //    v2.1 的关卡界面桌上一张 3D 关卡卡都不该有，选关全在关卡窗口里；
                //    旧流程（DSH_RULES_V21=0）那排卡必须原样还在、流程照旧。
                //    两个模式**共用这一条链**（入口分别接在 case 32 / case 52 上），
                //    所以两边拍出来的文件名一样、位置一样，可以并排对照。
                //
                //  【为什么非要关掉窗口再拍一张】第一张图里窗口正好盖在桌子中间 ——
                //    而那里正是那排关卡卡原来的位置。"看不见"和"没有"是两件事，
                //    所以 ㉖① 把窗口收起来拍一张空桌面，并把两个数字打进日志：
                //    rig 认领了几张大卡、场景里还有几个 BigCard_* 物体
                //    （口径分开是因为"列表清了、物体还在"这种残留只有后一个数抓得到）。
                //
                //  【点一行 / 按确认走的是谁】HUD 的 PickLevelInWindow / ConfirmLevelInWindow ——
                //    和窗口里那两个按钮**同一对方法**。IMGUI 的按钮探针点不了（它不模拟输入事件），
                //    但"绕过按钮"不等于"绕过那条路"：选中态、确认态、窗口自动收起
                //    全在这两个方法里，按钮和探针共用。
                //
                //  【为什么点第 2 行而不是第 1 行】默认选中的就是第 1 关（当前关卡）。
                //    点第 2 行、确认之后看关卡真的变成第 2 关，才能证明
                //    "确认用的是窗口选的那一关"，而不是"永远进第 1 关"。
                // ══════════════════════════════════════════════════════

                // ㉖⓪ 关卡界面：窗口自动弹着 —— 玩家看到的这一屏
                case 260:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    // ★ 先截图、后量（和这一屏别处同一条规矩）：Shot 在"距上一张不到 ShotSettle"时
                    //   会返回 false 让本阶段重试，把日志放在它前面就会同一份数据每帧打一遍（刷屏）。
                    if (!Shot("level_ui_00_select.png")) return;
                    LogLevelSelectState("㉖⓪ 进关卡界面");
                    Stage = 261;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉖① 先把窗口收起来：这一张看的是"桌子中间到底有没有那排卡"
                case 261:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    SetLevelWindow(false);
                    Stage = 262;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 262:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    if (!Shot("level_ui_01_table.png")) return;
                    LogLevelSelectState("㉖① 关掉窗口看桌面");
                    LogCardRow("㉖① 关卡");
                    Stage = 263;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉖② 重新开窗口，点第 2 行（点完先记日志，图留给下一个 stage 拍 ——
                //      同一帧里截图 + 改状态，落盘的是改完之后那一帧，这条坑文件头写过）
                case 263:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    SetLevelWindow(true);
                    Stage = 264;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 264:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    PickLevelRow(1);
                    Stage = 265;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉖③ 点完那一行：高亮 / 「已选」「▸ 当前关卡」两处文案都该跟着走
                case 265:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    if (!Shot("level_ui_02_row_picked.png")) return;
                    Stage = 266;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉖④ 按「进入这一关」（窗口底部那个按钮的同一个入口）
                case 266:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    ConfirmLevelRow();
                    Stage = 267;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉖⑤ 进关之后：v2.1 这里应该是牌组界面 + 「状态与画面一致｜残留 0 张」。
                //      拍完接回主链（v2.1 → 55，旧流程 → 20），后面的选牌组 / 选刀片 /
                //      第 1 回合按原样跑完 —— 顺带证明"从窗口进的那一关真的开起来了"。
                case 267:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.4) return;
                    if (!Shot("level_ui_03_after_confirm.png")) return;
                    ProbeLogSync("㉖⑤ 从窗口进关之后");
                    // v2.1 接回主链的 55（选牌组）；★ 两个探针一起开时走 54 ——
                    // 那一屏才是"牌组界面"，选牌组探针的入口就挂在它后面（否则会被跳过）。
                    Stage = TableSettings.UseRulesV21 ? (deckProbe ? 54 : 55) : 20;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ══════════════════════════════════════════════════════
                //  ㉜⓪~㉜⑦ 选牌组：v2.1 只剩 UI 窗口（DSH_DECKPROBE=1）
                //
                //  【它验的是用户这句话】「这些流派也做成窗口，去掉卡牌」——
                //    v2.1 的牌组界面桌上一张 3D 牌组卡都不该有，选牌组全在牌组窗口里；
                //    旧流程（DSH_RULES_V21=0）那排卡必须原样还在、流程照旧。
                //    两个模式**共用这一条链**（入口分别接在 case 20 / case 54 上），
                //    所以两边拍出来的文件名一样、位置一样，可以并排对照。
                //
                //  【和选关探针（㉖⓪~㉖⑤）是同一个形状】关窗看空桌 → 点一行 → 按确认 → 接回主链。
                //    连"为什么要关掉窗口再拍一张"都一样：窗口正好盖在桌子中间，
                //    而那里正是那排卡原来的位置 —— "看不见"和"没有"是两件事。
                //
                //  【为什么点第 3 副】牌组**不预选**（和关卡不同：关卡默认停在当前那一关）。
                //    点第 3 副、确认之后看进关用的确实是第 3 副（刀片 / 手牌跟着变），
                //    才能证明"确认用的是窗口选的那一副"，而不是"永远第一副"。
                //
                //  【点一行 / 按确认走的是谁】HUD 的 PickDeckInWindow / ConfirmDeckInWindow ——
                //    和窗口里那两个按钮**同一对方法**（IMGUI 按钮探针点不了，
                //    但绕过按钮 ≠ 绕过那条路）。
                // ══════════════════════════════════════════════════════

                // ㉜⓪ 牌组界面：窗口自动弹着 —— 玩家看到的这一屏
                case 321:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    // ★ 先截图、后量：Shot 在"距上一张不到 ShotSettle"时会返回 false
                    //   让本阶段重试，把日志放在它前面就会同一份数据每帧打一遍（刷屏）。
                    if (!Shot("deck_ui_00_select.png")) return;
                    LogDeckPickState("㉜⓪ 进牌组界面");
                    Stage = 322;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉜① 先把窗口收起来：这一张看的是"桌子中间到底有没有那排卡"
                case 322:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    SetDeckWindow(false);
                    Stage = 323;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 323:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    if (!Shot("deck_ui_01_table.png")) return;
                    LogDeckPickState("㉜① 关掉窗口看桌面");
                    LogCardRow("㉜① 牌组");
                    Stage = 324;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉜② 重新开窗口，点第 3 副（点完先记日志，图留给下一个 stage 拍）
                case 324:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    SetDeckWindow(true);
                    Stage = 325;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 325:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    PickDeckRow(2);
                    Stage = 326;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉜③ 点完那一行：高亮 / 「已选牌组」都该跟着走
                case 326:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    if (!Shot("deck_ui_02_row_picked.png")) return;
                    Stage = 327;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉜④ 按「确认选择该卡组」（窗口底部那个按钮的同一个入口）
                case 327:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    ConfirmDeckRow();
                    Stage = 328;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉜⑤ 确认之后：应该是"选刀片"那一屏 + 「状态与画面一致｜残留 0 张」。
                //      拍完接回主链（v2.1 → 56，旧流程 → 22），后面的选刀片 / 第 1 回合
                //      按原样跑完 —— 顺带证明"从窗口定的那一副真的生效了"。
                case 328:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.4) return;
                    if (!Shot("deck_ui_03_after_confirm.png")) return;
                    ProbeLogSync("㉜⑤ 定完牌组之后");
                    LogDeckPickState("㉜⑤ 定完牌组之后");
                    Stage = TableSettings.UseRulesV21 ? 56 : 22;
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

                // ══════════════════════════════════════════════════════
                //  ㉞⓪~㉟⑤ 存档 / 读档探针（DSH_SAVEPROBE=1）
                //
                //  【它验的是用户点名的那件事】"保存当前关卡状态，读取后能接着打"。
                //   所以这一条链不是"调一下 API 看返回值"，而是**走玩家那条路**：
                //     造一个有内容的局面 → 暂停菜单里点「保　存」→ 退回开场 → 按「继　续」
                //     → 两边的数字逐条对照（回合 / 行动机会 / 分数 / 刀片 H·V·层数 /
                //       桌面张数 / 手牌张数 / 被动条数 / 空白卡）
                //   反例也走一遍：**把存档改坏**之后「继续」必须是灰的 / 明确报错，
                //   而且局面一个字节都不动（"不许半读半不读"这条只有真去改坏它才验得到）。
                //
                //  【stage 号从 340 起】270~284 / 285~299 / 300~328 已经被
                //   取景 / 相交 / 卡面 / 选关 / 牌组那几条探针占满了，另起一段不会撞号。
                //
                //  【每一步都先截图、下一个 stage 再改状态】ScreenCapture 是帧末异步写盘的，
                //   同一帧里先截图后改状态，落盘的就是"改完之后"的那一帧（文件头那条教训）。
                // ══════════════════════════════════════════════════════

                // ㉞⓪ 状态守卫 + 第一张手牌素材上桌
                //     ★ 这台机器上同时跑着别的 Unity 实例，它的自动化会点到最前面那个窗口 ——
                //       点到这边就等于替玩家把牌打出去了。阶段不对/手牌空就**重开一局**再走回这一屏，
                //       守卫只在真的动过状态时打一行日志（别人踩过"守卫每帧刷屏"的坑）。
                case 340:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    if (!ProbeSaveGuard("㉞⓪ 造局面之前"))
                    {
                        Stage = 57;                       // 回去重新点核心 → 确认刀片 → 第 1 回合
                        stageTime = EditorApplication.timeSinceStartup;
                        return;
                    }
                    ProbeClickHandMaterial();
                    Stage = 341;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉞① 第二张素材上桌（桌面要有 2 张 —— 存档才有"桌面"这一项可验）
                case 341:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeClickHandMaterial();
                    Stage = 342;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉞② 启动破壁机一次：分数 / 行动机会 / 刀片 H / D / 本回合已启动 全都有内容了
                case 342:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeActivateV21();
                    Stage = 343;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉞③ 记一下手牌：读档后的回归要用**还留在手里的那张法术**，
                //     所以这一步不打卡，只如实把"手里还有什么"写进日志（一张法术都没有就要说出来）
                case 343:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.6) return;
                    ProbeSaveHandCheckpoint("㉞③ 存档前的手牌");
                    Stage = 344;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉞④ 再补一点层数：热 2（衰退）+ 热 1（**不衰退**）+ 催化 1
                //     ★ "不衰退"那一份必须真有一条 —— 只存总数就会丢的那一项，实机也要验到
                case 344:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeSaveAddLayers();
                    Stage = 345;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉞⑤ 打开暂停菜单（用户点名的入口：Esc 菜单里那两项）
                case 345:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    SetPaused(true);
                    Stage = 346;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉞⑥ 截图：菜单上「保　存 / 读　取」两项 + 底下那行"存档位：…"
                case 346:
                    if (!Shot("sv_01_pause_menu.png")) return;
                    ProbeSaveLogNumbers("㉞⑥ 存档前（暂停菜单里）");
                    Stage = 347;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉞⑦ 存档：走 TableTurnLoop.SaveGame —— 和「保　存」按钮按下去是同一个入口
                //     （它内部会写盘 + **把文件读回来逐字段比一遍**，日志里能看到自检结果）
                case 347:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeSaveNow();
                    Stage = 348;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉞⑧ 收起暂停菜单（存档结果那行 notice 在顶部提示条上）
                case 348:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    SetPaused(false);
                    Stage = 349;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉞⑨ 截图：存档瞬间的桌面（和后面"读档后"那张同机位，肉眼应几乎一致）
                case 349:
                    if (!Shot("sv_02_saved.png")) return;
                    ProbeLogSync("㉞⑨ 存档瞬间");
                    ProbeTableLook("㉞⑨ 存档瞬间（顺带看木纹 shader）");
                    Stage = 350;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉟⓪ 退回开场（存档还在 —— 这是"关掉游戏再进来"的替身）
                case 350:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeReturnToTitle();
                    Stage = 351;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉟① 开场：存档在 → 「继　续」应该是亮的（截图 + 机位 + 亮/灰状态都记下来）
                case 351:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.2) return;
                    if (!Shot("sv_03_title_continue_on.png")) return;
                    LogCameraPose("㉟① 开场（存档在 → 「继续」应亮）");
                    ProbeSaveLogContinueState("㉟① 存档还在时");
                    Stage = 352;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉟② 反例①：把存档**改坏**（先备份好档原文），再让开场重新判定一次
                case 352:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.6) return;
                    ProbeSaveCorruptFile();
                    ProbeSaveRebuildTitle("㉟② 改坏存档之后重新判定");
                    Stage = 353;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉟③ 截图：这一档读不出来了 → 「继　续」必须是灰的
                case 353:
                    if (!Shot("sv_04_title_continue_gray.png")) return;
                    Stage = 354;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉟④ 按「继　续」：牌是灰的 → 玩家那一下本来就不该生效（如实挡掉、不进关卡）
                case 354:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.6) return;
                    ProbeSavePressContinue("㉟④ 灰着的「继续」被按了一下");
                    Stage = 355;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉟⑤ 再**直接走一遍读档入口**（模拟"牌子还亮着时点下去"这一下，比如存档刚被改坏）：
                //     必须明确报错，而且局面一个字节都不动 —— "不许半读半不读"就验在这里
                case 355:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.6) return;
                    ProbeSaveLoadRejected();
                    Stage = 356;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉟⑥ 截图：坏档被拒之后仍然停在开场（没有半残的牌桌）
                case 356:
                    if (!Shot("sv_05_load_rejected.png")) return;
                    Stage = 357;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉟⑦ 把好档写回去 → 「继续」应该又亮（说明"禁用"是跟着存档状态走的，不是一次性的）
                case 357:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.6) return;
                    ProbeSaveRestoreFile();
                    ProbeSaveRebuildTitle("㉟⑦ 恢复好档之后重新判定");
                    Stage = 358;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉟⑧ 截图：牌子又亮了
                case 358:
                    if (!Shot("sv_06_title_continue_back.png")) return;
                    Stage = 359;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㉟⑨ 按「继　续」—— 这一次真的读档（走 TableTitleRig.ActivateContinue，
                //     和玩家点那块木牌是同一个入口）
                case 359:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.6) return;
                    ProbeSavePressContinue("㉟⑨ 按「继续」读档");
                    Stage = 360;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊱⓪ 截图：读档后的桌面（与 ㉞⑨ 同机位）+ 逐条对照两边的数字
                case 360:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.2) return;
                    if (!Shot("sv_07_loaded.png")) return;
                    ProbeSaveCompareNumbers("㊱⓪ 读档后逐条对照");
                    ProbeLogSync("㊱⓪ 读档后");
                    ProbeTableLook("㊱⓪ 读档后（木纹 shader 也在这里看）");
                    Stage = 361;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊱① 回归①：读档之后**拖法术进附魔位**还能附魔（层数要 +1）
                case 361:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeSpellIntoEnchantSlot("㊱① 读档后拖法术进附魔位（必过）");
                    Stage = 362;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊱② 回归②：读档之后**没启动过的素材能拖回手牌**（收下）
                case 362:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeWithdrawTableCardAt(0, "㊱② 读档后把桌面第 1 张拖回手牌");
                    Stage = 363;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊱③ 回归③：**已启动过的素材收不回来**（这条正是"本回合是否启动过"那个字段在管）
                case 363:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeWithdrawStartedCheck("㊱③ 读档后：已启动过的素材收不回来");
                    Stage = 364;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊱④ 收尾：拍一张回归之后的桌面，再走"坏档时菜单里那行"那一段
                case 364:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    if (!Shot("sv_08_after_regression.png")) return;
                    ProbeLogSync("㊱④ 回归之后");
                    Stage = 375;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 365:
                    if (EditorApplication.timeSinceStartup - stageTime < 3.0) return;
                    Finish();
                    return;

                // ══════════════════════════════════════════════════════
                //  ㊱⑥~㊲③ 冷启动读档 + F2 报告（DSH_SAVEPROBE=1，从开场那一屏岔进来）
                //
                //  【它补的是哪条路】打包版验收报的那一条：**开机什么都没进，直接按「继续」**。
                //   报告原来只在 BeginLevel（开一关）里建，而读档不经过它 → report 是 null
                //   → 顶栏写"卡表解析报告没建出来 —— 无法确认哪些规则没实现"、F2 一片空白。
                //   玩法与存读档本身都对，但玩家/策划会以为卡表坏了。
                //   所以这一段把"冷启动 → 读档 → 顶栏那行 + F2 全文"整条走一遍并拍下来。
                //
                //  【前提】磁盘上要有一份**能读**的存档（前一次 DSH_SAVEPROBE 跑完会留下）。
                //   没有就直接回主链 —— 绝不假装通过。
                // ══════════════════════════════════════════════════════

                // ㊱⑥ 按「继续」之前：报告应当**还没建**（这正是要复现的前提，如实记下来）
                case 366:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.6) return;
                    ProbeReportState("㊱⑥ 冷启动·按「继续」之前");
                    ProbeSavePressContinue("㊱⑥ 冷启动按「继续」读档");
                    Stage = 367;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊱⑦ 读档后的桌面：顶栏那行应当是"✓ 卡表规则文本全部已识别（175 句）"
                case 367:
                    if (!Shot("sv_09_cold_loaded_hud.png")) return;
                    ProbeReportState("㊱⑦ 冷启动读档之后（报告必须已建出）");
                    ProbeLogSync("㊱⑦ 冷启动读档之后");
                    Stage = 368;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊱⑧ 替玩家按一下 F2
                case 368:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.6) return;
                    SetRulesReport(true);
                    Stage = 369;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊱⑨ 报告头部（"素材 32 张 · 法术 8 张｜句子 175 条…"那行在不在这张里）
                case 369:
                    if (!Shot("sv_10_cold_report_top.png")) return;
                    ProbeReportState("㊱⑨ F2 报告头部");
                    Stage = 370;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊲⓪ 滚到底（"最后一行只显示一半"这个毛病只在底部才看得见）
                case 370:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.6) return;
                    ScrollRulesReportToEnd();
                    Stage = 371;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊲① 报告底部
                case 371:
                    if (!Shot("sv_11_cold_report_bottom.png")) return;
                    Stage = 372;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊲② 关掉报告（后面那张图不该盖着它）
                case 372:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.6) return;
                    SetRulesReport(false);
                    Stage = 373;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊲③ 收尾：这一段只验"冷启动读档 + 报告"，别的都不做（存档保持不动，留给下一跑）
                case 373:
                    if (EditorApplication.timeSinceStartup - stageTime < 3.0) return;
                    Finish();
                    return;

                // ══════════════════════════════════════════════════════
                //  ㊲⑤~㊲⑦ 反例②：存档坏掉时，**玩家看得见的地方**要写出原因（DSH_SAVEPROBE=1）
                //
                //  【为什么还要这一段】反例已经验过"按钮变灰 + 点了被挡 + 局面一个字节不动"，
                //   但那三件事都得**看日志**才知道为什么。玩家在游戏里能看到的只有暂停菜单
                //   底下那行"存档位：…（★ 读不出来：…）"—— 这一段就把它拍下来，
                //   证明原因写在玩家看得见的地方，而不是只在日志里。
                //   顺带确认：坏档不影响卡表解析报告（报告是卡表的只读快照，和存档无关）。
                // ══════════════════════════════════════════════════════

                // ㊲⑤ 把存档改坏 + 打开暂停菜单
                case 375:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.6) return;
                    ProbeSaveCorruptFile();
                    SetPaused(true);
                    Stage = 376;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊲⑥ 截图：菜单里那行"存档位：…（★ 读不出来：第 N 行第 M 列…）"
                case 376:
                    if (!Shot("sv_12_pause_corrupt_save.png")) return;
                    ProbeSaveLogStatusLine("㊲⑥ 坏档时菜单里那行");
                    ProbeReportState("㊲⑥ 坏档时（报告不受影响，仍应建着）");
                    Stage = 377;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊲⑦ 关菜单 + 把好档写回去（留给下一跑的冷启动那一段用）
                case 377:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.6) return;
                    SetPaused(false);
                    ProbeSaveRestoreFile();
                    Stage = 365;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ══════════════════════════════════════════════════════
                //  ㊲⑨~㊳⑨ 献祭吞噬**取证**探针（DSH_SACPROBE=1）
                //
                //  【它要拿到的是"真游戏里的一次吞噬成功"】
                //    TurnEngine.ResolveSacrifice（启动结算第 ⑤ 步）早就实现了、离线断言也全绿，
                //    但真机从来没抓到过 —— 之前的跑法总是在吞噬之前先爆刀（刀片 H 归零）
                //    就结束了。所以这条链专门造一个"本回合实际使用的最后一次启动 +
                //    目标结算后仍有 D 剩余 + 刀片 H 够用"的局面，而且三件事在**同一条链**里：
                //      · 吞噬成功（第 ⑤ 步写"并入刀片…刀片 H/V 变化"）
                //      · 反例①：目标在本回合被形态变化带走 → "本次献祭不生效"
                //      · 反例②：目标 D 耗尽被带走 → "本次献祭不生效"
                //
                //  【局面怎么造（每一步都走玩家那条路）】
                //    牌组 0「硫硝爆燃」（DSH_DECK_INDEX=0，默认就是它）：
                //      开局手牌 = 外星合金(H4 D3 V2) / 水(H2 D3 V2) / 硝石(H12 D3 V1) / 硫磺(H4 D3 V2) + 火焰
                //      ★ 刀片核心**点名选硝石**（H=12 —— 这条链要 5 次启动，H4 的核心第 4 次就爆刀了，
                //        这正是以前从来没抓到吞噬的原因）：㊲⑨ 那一步点它，确认后刀片 = 硝石 H12 V1
                //    第 1 回合（行动机会 5，刚好够这条链的 5 次启动）：
                //      ① 水 上桌（手牌还剩 3 张）
                //      ② 启动 水 ×2：手牌没空 → IsLastStartForSure()=false
                //         → 第 ⑤ 步应当写"本次不是本回合实际使用的最后一次启动 → 不吞噬"
                //         → 顺便把水的 D 打到 1（后面拿它当"D 耗尽"反例）
                //      ③ 外星合金、硫磺上桌（手牌剩 1 张 = 火焰）
                //      ④ 火焰打完（附魔 热×1）→ **手牌空**
                //         ⇒ HandCount==0 使 IsLastStartForSure() 恒为 true：
                //            从这里开始的每一次启动都必然是"本回合最后一次"
                //      ⑤ 启动 硫磺（易燃 + 热≥1 → 燃烧：D 立即归零 + 形态变化成硫磺气）
                //         → 第 ⑤ 步应当写"目标已因形态变化…→ 本次献祭不生效"（反例①）
                //         → 形态变化产出的「硫磺气」进手牌，把它打出去 → **手牌又空了**
                //      ⑥ 启动 外星合金（惰性、热层已被上一步吃掉 → 打不出任何规则）
                //         → D 3→2 仍有剩余 → **吞噬成功**（刀片 H+4、V+2）
                //      ⑦ 启动 水（D=1）→ D 1→0 → D 耗尽移除
                //         → 第 ⑤ 步应当写"目标已因形态变化 / 溶解 / D耗尽移出桌面 → 本次献祭不生效"（反例②）
                //
                //  【飞行动画（ConsumeInto）怎么验】被吞噬 / 溶解 / D 耗尽的卡本该走
                //    PlayCard.ConsumeInto：飞向破壁机罐口、0.55 秒后自毁
                //    （TableRulesV21.SyncVisualsAfterActivate ①，那里明写着"让它自己飞完再消失"）。
                //    而 Shot 的 ShotSettle 是 1.6 秒（防串帧），正常速度下第一张连拍就已经飞完了 ——
                //    所以这一段的连拍走 SacShotNow（直接 ScreenCapture，不排队），
                //    并且把 Time.timeScale 压到 0.3（0.55 秒 → 现实约 1.8 秒）。
                //    规则结算是一次调用跑完的（引擎不看 Time.deltaTime），
                //    所以放慢只影响动画，判据一个字都没变（和 DSH_FXPROBE 同一个理由）。
                //    ★ 探针每一帧都把 PlayCard.IsConsuming 数出来 —— "在飞"必须是个数字。
                //
                //  ★ 一律先截图、下一个 stage 再改状态：ScreenCapture 是帧末异步写盘的，
                //    同一帧里先截图后改状态，落盘的是改完之后那一帧（文件头那条教训）。
                // ══════════════════════════════════════════════════════

                // ㊲⑨ 点名选刀片核心 = 硝石（H12）→ 确认（㊴⓪ 那一步会接着走）
                case 379:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeSacPickCore("硝石");
                    Stage = 58;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊳⓪ 状态守卫 + 前提检查 + 水 上桌
                case 380:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    if (!ProbeSacGuardAt("㊳⓪ 第 1 回合开局之前（手牌 4 张、桌面 0 张）", 1, 0, 4))
                    {
                        Stage = 379;                      // 重开之后：点核心 → 确认刀片 → 第 1 回合
                        stageTime = EditorApplication.timeSinceStartup;
                        return;
                    }
                    ProbeSacSetupCheck();
                    Debug.Log("[AutoPlay/献祭] ㊳⓪ 第 1 回合开局：" + ProbeSacNumbers());
                    ProbeSacPlayHandMaterial("水", "㊳⓪ 水 上桌（后面拿它当 D 耗尽反例）");
                    Stage = 381;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊳① 非最后一次启动 ①（手牌还有 3 张 → 第 ⑤ 步应当写"不是最后一次"）
                case 381:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    ProbeSacSelectTable("水");
                    ProbeSacActivate("㊳① 第 1 次启动（手牌没空 → 不是最后一次 → 不该吞噬）");
                    Stage = 382;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊳② 非最后一次启动 ②（把水的 D 从 3 打到 1）
                case 382:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    ProbeSacSelectTable("水");
                    ProbeSacActivate("㊳② 第 2 次启动（手牌没空 → 不是最后一次 → 不该吞噬）");
                    Stage = 383;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊳③ 外星合金 上桌（后面拿它当**吞噬成功**的目标：惰性、打不出任何规则）
                case 383:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    ProbeSacPlayHandMaterial("外星合金", "㊳③ 外星合金 上桌");
                    Stage = 384;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊳④ 硫磺 上桌（后面拿它当**形态变化**反例：易燃 + 热≥1）
                case 384:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    ProbeSacPlayHandMaterial("硫磺", "㊳④ 硫磺 上桌");
                    Stage = 385;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊳⑤ 截图：三张素材都在桌上（吞噬 / 反例之前的对照帧）
                case 385:
                    if (!Shot("sac_01_three_materials.png")) return;
                    Debug.Log("[AutoPlay/献祭] ㊳⑤ 三张素材上桌（手牌还剩 1 张法术）：" + ProbeSacNumbers());
                    Stage = 386;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊳⑥ 放慢时间 + 火焰打完（热×1；手牌空 = "最后一次启动"的前提）
                //   ★ 放慢是为了顺带验"法术卡飞进罐口"那一段（它是另一条 ConsumeInto 路径）
                case 386:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    SetTimeScale(0.3f, "㊳⑥ 打完火焰（顺带抓法术卡飞向罐口）");
                    ProbeSacPlaySpell();
                    Stage = 387;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊳⑦⑧ 连拍：法术卡飞行（0.45 秒 → 0.3 倍速约 1.5 秒）
                case 387:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.2) return;
                    SacShotNow("sac_02_spell_flight_a.png");
                    Debug.Log("[AutoPlay/献祭] ㊳⑦ 法术卡飞行第 1 帧：" + ProbeSacFlightText());
                    Stage = 388;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 388:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.2) return;
                    SacShotNow("sac_03_spell_flight_b.png");
                    Debug.Log("[AutoPlay/献祭] ㊳⑧ 法术卡飞行第 2 帧：" + ProbeSacFlightText());
                    Stage = 389;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊳⑨ 时间恢复（让那张卡自己飞完自毁）
                case 389:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.5) return;
                    SetTimeScale(1f, "㊳⑨ 时间恢复");
                    Stage = 390;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊴⓪ 截图：**反例①之前**的桌面（三张素材 + 面板上刀片的旧值 H10 V1）
                case 390:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.2) return;
                    if (!Shot("sac_04_before_counters.png")) return;
                    Debug.Log("[AutoPlay/献祭] ㊴⓪ 反例之前（手牌空 → 之后每次启动都必然是「本回合最后一次」）：" + ProbeSacNumbers());
                    if (!ProbeSacGuardAt("㊴⓪ 反例之前（第 1 回合、桌面 3 张、手牌空）", 1, 3, 0))
                    {
                        Stage = 379;
                        stageTime = EditorApplication.timeSinceStartup;
                        return;
                    }
                    Stage = 391;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊴① 反例①：启动 硫磺（易燃 + 热×1）→ 燃烧形态变化 → 「本次献祭不生效」
                case 391:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeSacSelectTable("硫磺");
                    ProbeSacActivate("㊴① 第 3 次启动（最后一次；硫磺易燃 + 热×1 → 期望「形态变化 → 献祭不生效」）");
                    Stage = 392;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊴② 截图：反例① 之后（桌上是 水 / 外星合金 两张，硫磺那张已经不在；硫磺气在手里）
                case 392:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.6) return;
                    if (!Shot("sac_05_counter_form_change.png")) return;
                    Debug.Log("[AutoPlay/献祭] ㊴② 反例① 形态变化之后：" + ProbeSacNumbers());
                    Debug.Log("[AutoPlay/献祭] ㊴② 形态变化那张卡的飞行状态：" + ProbeSacFlightText());
                    Stage = 393;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊴③ 把形态变化产出的「硫磺气」打出去 → 手牌又空了（最后一次启动的前提）
                case 393:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    if (!ProbeSacGuardAt("㊴③ 打硫磺气之前（第 1 回合、桌面 2 张、手牌 1 张）", 1, 2, 1))
                    {
                        Stage = 379;
                        stageTime = EditorApplication.timeSinceStartup;
                        return;
                    }
                    ProbeSacPlayHandMaterial("硫磺气", "㊴③ 硫磺气 上桌（形态变化的产物，打出去 = 手牌又空了）");
                    Stage = 394;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊴④ ★ 吞噬成功：启动 外星合金（D3→2 仍有剩余、惰性、无附魔层）
                case 394:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    if (!ProbeSacGuardAt("㊴④ 吞噬之前（第 1 回合、桌面 3 张、手牌空）", 1, 3, 0))
                    {
                        Stage = 379;
                        stageTime = EditorApplication.timeSinceStartup;
                        return;
                    }
                    ProbeSacSelectTable("外星合金");
                    ProbeSacActivate("㊴④ 第 4 次启动（最后一次；目标 D=3 → 期望**吞噬成功**）");
                    Stage = 395;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊴⑤ 吞噬那一帧之后 0.2 秒的连拍 + 飞行状态（"有没有真的在飞"在这一行里）
                case 395:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.2) return;
                    SacShotNow("sac_06_sacrifice_0p2s.png");
                    Debug.Log("[AutoPlay/献祭] ㊴⑤ 吞噬后 0.2 秒：" + ProbeSacFlightText());
                    Stage = 396;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊴⑥ 截图：**吞噬之后**的桌面（外星合金从桌上消失 + 面板刀片新值 H12 V3）
                case 396:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.6) return;
                    if (!Shot("sac_07_after_sacrifice.png")) return;
                    Debug.Log("[AutoPlay/献祭] ㊴⑥ 吞噬之后：" + ProbeSacNumbers());
                    Debug.Log("[AutoPlay/献祭] ㊴⑥ 被吞噬那张卡的飞行状态：" + ProbeSacFlightText());
                    Stage = 397;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊴⑦ 反例②：启动 水（D=1）→ D 耗尽移除 → 「本次献祭不生效」
                case 397:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeSacSelectTable("水");
                    ProbeSacActivate("㊴⑦ 第 5 次启动（最后一次；目标 D=1 → 期望「D耗尽 → 献祭不生效」）");
                    Stage = 398;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊴⑧ 截图：反例② 之后的桌面
                case 398:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.6) return;
                    if (!Shot("sac_08_after_d_exhaust.png")) return;
                    Debug.Log("[AutoPlay/献祭] ㊴⑧ 反例② D 耗尽之后：" + ProbeSacNumbers());
                    Debug.Log("[AutoPlay/献祭] ㊴⑧ D 耗尽那张卡的飞行状态：" + ProbeSacFlightText());
                    ProbeLogSync("㊴⑧ 反例② 之后的收尾");
                    Stage = 400;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ══════════════════════════════════════════════════════
                //  ㊵⓪~㊵⑨ **"行动机会用完的那一次"** 到底算不算最后一次启动？
                //
                //  【为什么要单独验它】正文 §四.5 的口径是"每回合**实际使用的**最后一次启动"
                //    （原文还有一句："最后一次启动不一定是第 5 次行动…这为增加启动次数的卡牌
                //      留出了设计空间"）。而 TableRulesV21.IsLastStartForSure() 认三种"确定"的情况：
                //      行动机会用完 / 手牌空了 / 已到目标分
                //   可它在 ActivateJuicer 里是**扣行动机会之前**问的，而 CanActivate 又要求
                //      actionPoints > 0 —— 于是 `actionPoints <= 0` 那一条永远为假（死条件），
                //    表现就是：**把最后一个行动机会用掉的那一次启动，不会被认作"最后一次"**。
                //    这一段的局面：手牌留一张火焰（HandCount=1，永不满足"手牌空"），
                //    用 5 次启动把行动机会打到 0，最后一次的目标（外星合金 D=2）结算后仍有 D 剩余
                //    → 按正文该被吞噬；探针把第 ⑤ 步的原文、CanActivate / BlockReason
                //      和"结束回合之后那张卡还在不在"一起记下来。
                //  ★ 它只取证、不改规则：探针一行玩法代码都不碰。
                // ══════════════════════════════════════════════════════

                // ㊵⓪ 重开一局（走游戏自己的入口），回到"选刀片"那一屏
                case 400:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    ProbeSacRestartForApTest();
                    Stage = 401;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊵① 还是点名硝石当核心（H12 —— 这条段要 5 次启动）
                case 401:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeSacPickCore("硝石");
                    Stage = 402;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊵② 确认刀片 → 第 1 回合
                case 402:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeConfirmBladeV21();
                    Stage = 403;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊵③ 出 水 / 外星合金 / 硫磺（**火焰留着不打** —— 手牌不空，才测得到"行动机会用完"那一条）
                case 403:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    if (!ProbeSacGuardAt("㊵③ 第二段开局（第 1 回合、桌面 0 张、手牌 4 张）", 1, 0, 4))
                    {
                        Stage = 400;
                        stageTime = EditorApplication.timeSinceStartup;
                        return;
                    }
                    Debug.Log("[AutoPlay/献祭] ㊵③ 第二段开局：" + ProbeSacNumbers());
                    ProbeSacPlayHandMaterial("水", "㊵③ 水 上桌");
                    Stage = 404;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 404:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    ProbeSacPlayHandMaterial("外星合金", "㊵④ 外星合金 上桌（最后一次启动的目标）");
                    Stage = 405;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 405:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    ProbeSacPlayHandMaterial("硫磺", "㊵⑤ 硫磺 上桌（**火焰故意留在手里**：手牌不空）");
                    Stage = 406;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊵⑥~㊵⑨ 连打 5 次启动：前 4 次手牌不空 → 都不是最后一次；
                //      第 5 次（行动机会 1 → 0）才是**本回合实际使用的最后一次**
                case 406:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    ProbeSacSelectTable("水");
                    ProbeSacActivate("㊵⑥ 启动 1/5（行动机会 5→4；手牌不空 → 不是最后一次）");
                    Stage = 407;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 407:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    ProbeSacSelectTable("外星合金");
                    ProbeSacActivate("㊵⑦ 启动 2/5（行动机会 4→3；手牌不空 → 不是最后一次）");
                    Stage = 408;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 408:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    ProbeSacSelectTable("硫磺");
                    ProbeSacActivate("㊵⑧ 启动 3/5（行动机会 3→2；手牌不空 → 不是最后一次）");
                    Stage = 409;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 409:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    ProbeSacSelectTable("水");
                    ProbeSacActivate("㊵⑨ 启动 4/5（行动机会 2→1；手牌不空 → 不是最后一次）");
                    Stage = 410;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊶⓪ ★ 第 5 次启动 = 用掉**最后一个**行动机会的那一次（正文口径里就是"最后一次"）
                case 410:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    ProbeSacSelectTable("外星合金");
                    ProbeSacActivate("㊶⓪ 启动 5/5（行动机会 1→0 = 本回合实际使用的最后一次启动；" +
                                     "目标 D=2 → 按正文该被吞噬）");
                    Debug.Log("[AutoPlay/献祭] ㊶⓪ 这一下之后还能做什么：CanActivate=" + ProbeSacCanActivateText());
                    Stage = 411;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊶① 截图：行动机会 0/5 的桌面（如果没吞噬，目标那张还杵在桌上）
                case 411:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.6) return;
                    if (!Shot("sac_09_ap_used_up_on_table.png")) return;
                    Debug.Log("[AutoPlay/献祭] ㊶① 行动机会用完（0/5）：" + ProbeSacNumbers());
                    Debug.Log("[AutoPlay/献祭] ㊶① 还能不能启动：" + ProbeSacCanActivateText());
                    Stage = 412;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊶② 结束回合（行动机会 0 → 玩家唯一的操作），看那张卡会不会在回合末被补吞
                case 412:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeSacEndRound();
                    Stage = 413;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊶③ 截图：第 2 回合的桌面 —— 目标卡还在 = "最后一次启动"这一条确实漏了
                case 413:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.6) return;
                    if (!Shot("sac_10_ap_used_up_next_round.png")) return;
                    Debug.Log("[AutoPlay/献祭] ㊶③ 第 2 回合（目标那张卡应当已经被吞噬、结果还在）："
                              + ProbeSacNumbers());
                    ProbeLogSync("㊶③ 献祭探针收尾");
                    Stage = 414;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 414:
                    if (EditorApplication.timeSinceStartup - stageTime < 3.0) return;
                    // ㊸⓪~㊹⑨ 那一段（430+）是同一个开关下的**取证续段**：它自己会重开一局，
                    // 所以 400~414 那一局的结论一个字都不受影响。
                    Stage = 430;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ══════════════════════════════════════════════════════
                //  ㊸⓪~㊹⑨ 两个 bug 修好之后的**实跑取证**（DSH_SACPROBE=1）
                //
                //  【要证的两件事】
                //   Bug A：`IsLastStartForSure()` 原来第一条判据是死条件 `actionPoints <= 0`，
                //     于是"把最后一个行动机会用掉的那一次启动"（AP 1→0）不被认作最后一次，
                //     第 ⑤ 步写"本次不是本回合实际使用的最后一次启动 → 不吞噬"，目标卡留在桌上。
                //     修成 `actionPoints <= 1` 之后，那一次必须当场写"并入刀片"。
                //   Bug B：`SyncVisualsAfterActivate` 第①步原来遍历**规则侧的 table**
                //     （引擎 `RemoveFromTable` 已经把那一项从 List 里摘掉了）→ 循环永远进不去，
                //     飞行动画是死代码，"正在飞的卡：0 张"。改成遍历 tableCards（视图侧）之后，
                //     必须真能抓到"卡在桌面与破壁机之间"的中间帧。
                //
                //  【局面怎么造（每一步都走玩家那条路，一行玩法代码都不碰）】
                //   牌组 0「硫硝爆燃」+ 刀片核心点名**硝石 H12**（H4 的核心第 4 次就爆刀 ——
                //   那正是以前从来没抓到吞噬的原因）。
                //   ★★ 三张素材的 D **都是 3**，而一回合只有 5 次启动 —— 所以"五次要摊在两张卡上"
                //      这条路是死的：谁被打到第 3 下，谁的第 3 下就是 `D-1 → D=0` →
                //      当场触发「D耗尽」（`ExhaustTarget` 把 `removed=true` 并移出桌面），
                //      第 ⑤ 步只会写反例②那句"已因…D耗尽移出桌面 → 本次献祭不生效"。
                //      第一版探针就是这么摆的（水打 3 下），实跑日志抓到的正是那句 ——
                //      所以**第 5 次必须打一张这一回合还没被打过的卡**（D 3→2 > 0）。
                //   第 1 回合（行动机会 5，刚好 5 次启动）：
                //     ① 水 / 外星合金 / 硫磺 上桌；**火焰故意留在手里**（手牌不空
                //        → "手牌空"那一条永远不成立，只剩"用掉最后一个行动机会"能命中）
                //     ② 启动 1/5、2/5 打**水**（水带遇热：第 1 下先把火焰那 1 层热吃掉、
                //        第 2 下无层数不反应），D 3→2→1（仍在桌上）
                //     ③ 启动 3/5、4/5 打**外星合金**（惰性、不响应热/冷/酸），D 3→2→1（仍在桌上）
                //     ④ ★ 启动 5/5（行动机会 1→0）打**硫磺**（这一回合**第一次**被打，
                //        无热层所以不会"易燃 + 热"形态变化）：D 3→2 > 0 → 目标仍在桌上
                //        → 按正文 §四.5 必须**当场吞噬**
                //        （★ 这就是修好的那一条：修前 `actionPoints <= 0` 是死条件，
                //          这一次会被判成"不是最后一次" → 第 ⑤ 步写"不吞噬"）
                //   第 2 回合（行动机会重置 5；三张素材都只剩 D ≥ 1 → 谁都不会耗尽）：
                //     ⑤ 拖「硫磺」回手牌 —— 它第 1 回合启动过 → **必须被拒**
                //     ⑥ 把本回合还没启动过的「水」打上桌 → 拖回手牌 → **必须被收下**
                //        （同一张卡、同一个入口，只有"本回合启动过没有"不同）
                //
                //  【飞行动画怎么抓】`Shot()` 的 ShotSettle 是 1.6 秒（防串帧），
                //   而 ConsumeInto 的寿命只有 0.55 秒（法术 0.45）—— 正常速度下连第一张都拍不到。
                //   所以这一段：`SetTimeScale(0.8)`（0.55 秒 → 现实约 0.69 秒）+
                //   `SacShotNow`（直接 ScreenCapture，不排队、绕开 ShotSettle）连拍两帧（间隔 0.2 秒）。
                //   规则结算是一次调用跑完的（引擎不看 Time.deltaTime），放慢只影响动画，
                //   判据一个字都不变（和 DSH_FXPROBE 同一个理由）。
                //   ★ 每一帧都把 `PlayCard.IsConsuming` **数出来** —— "在飞"必须是个数字。
                //   ★ 0.8 是**中间档**：1.0 太快（下一帧就飞完，中间态一帧都拍不到），
                //     0.3 太慢（0.55 秒的动画拖到现实 1.8 秒，第二次启动要等它）
                // ══════════════════════════════════════════════════════

                // ㊸⓪ 重开一局（走游戏自己的入口），回到"选刀片"那一屏
                case 430:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    ProbeSacRestartForApTest();
                    Stage = 431;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊸① 还是点名硝石当核心（H12 —— 这一段要 5 次启动）
                case 431:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeSacPickCore("硝石");
                    Stage = 432;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊸② 确认刀片 → 第 1 回合
                case 432:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeConfirmBladeV21();
                    Stage = 433;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊸③ 出 水 / 外星合金 / 硫磺（**火焰留着不打** —— 手牌不空，才测得到"用掉最后一个行动机会"那一条）
                case 433:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    if (!ProbeSacGuardAt("㊸③ 第 1 回合开局（第 1 回合、桌面 0 张、手牌 4 张）", 1, 0, 4))
                    {
                        Stage = 430;                      // 重开之后：点核心 → 确认刀片 → 第 1 回合
                        stageTime = EditorApplication.timeSinceStartup;
                        return;
                    }
                    ProbeSacSetupCheck();
                    Debug.Log("[AutoPlay/献祭] ㊸③ 这一段开局（**手牌故意留一张火焰**）：" + ProbeSacNumbers());
                    ProbeSacPlayHandMaterial("水", "㊸③ 水 上桌（第 1、4 次启动的目标）");
                    Stage = 434;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊸④㊸⑤ 外星合金 / 硫磺上桌
                case 434:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    ProbeSacPlayHandMaterial("外星合金", "㊸④ 外星合金 上桌（第 2、3、5 次启动的目标 = 被吞噬那张）");
                    Stage = 435;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 435:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    ProbeSacPlayHandMaterial("硫磺", "㊸⑤ 硫磺 上桌（打一次；后面拿它验「本回合启动过的不能收回手牌」）");
                    Stage = 436;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊸⑥ 截图：三张素材上桌的对照帧（桌上 3 张、手里 1 张法术、行动机会满 5）
                case 436:
                    if (!Shot("sac21_01_three_on_table.png")) return;
                    Debug.Log("[AutoPlay/献祭] ㊸⑥ 三张素材上桌、手里留着一张火焰（行动机会满 5）："
                              + ProbeSacNumbers());
                    Stage = 437;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊸⑦ 启动 1/5 打**水**（热×1 在这一步被吃掉；手牌不空 → 按判据**不该**吞噬）
                case 437:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    ProbeSacSelectTable("水");
                    ProbeSacActivate("㊸⑦ 启动 1/5 打「水」（行动机会 5→4；热×1 被这一步吃掉）");
                    ProbeSacDumpLastActivate("㊸⑦ 这一下照理不该吞噬（手牌不空、行动机会还剩 4）");
                    Stage = 438;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊸⑧ 启动 2/5 继续打**水**（D 2→1；水带遇热但热层已被上一步吃掉 → 不会形态变化）
                case 438:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    ProbeSacSelectTable("水");
                    ProbeSacActivate("㊸⑧ 启动 2/5 打「水」（行动机会 4→3；D 2→1，仍在桌上）");
                    Stage = 439;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊸⑨ 启动 3/5 打**外星合金**（惰性、无规则；D 3→2）
                case 439:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    ProbeSacSelectTable("外星合金");
                    ProbeSacActivate("㊸⑨ 启动 3/5 打「外星合金」（行动机会 3→2；D 3→2）");
                    Stage = 440;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊹⓪ 启动 4/5 继续打**外星合金**（D 2→1）—— 打完之后三张卡的 D 都 ≥ 1，
                //      第 5 次打的是**这一回合还没被打过**的硫磺（D 3→2 > 0）
                case 440:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    ProbeSacSelectTable("外星合金");
                    ProbeSacActivate("㊹⓪ 启动 4/5 打「外星合金」（行动机会 2→1；D 2→1）");
                    Stage = 441;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊹① ★ 第 5 次启动 = **用掉最后一个行动机会的那一次**（AP 1→0）
                //   ★ 打的是这一回合第一次被启动的「硫磺」：D 3→2 > 0 → 目标结算后仍在桌上
                //     → 按正文 §四.5 必须**当场吞噬**（刀片 H+4 V+2）
                //   ★ 压到 0.15 倍速：这一次会真吞噬，"飞向罐口"只有 0.55 秒（现实 3.7 秒）
                case 441:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    SetTimeScale(0.15f, "㊹① 第 5 次启动（这一次会吞噬 → 要把飞行中间态拍下来）");
                    ProbeSacSelectTable("硫磺");
                    ProbeSacActivate("㊹① 启动 5/5 打「硫磺」（行动机会 1→0 = 本回合实际使用的最后一次启动；" +
                                     "目标 D=3 → 结算后 D=2 > 0 → 期望**吞噬成功**、刀片 H+4 V+2）");
                    Stage = 442;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊹② ★★ 证据行：这一下的 ①「本回合最后一次启动」那一行 + ⑤ 判定的每一行 ★★
                //   ★ 这一档**故意不等**（0.15 倍速下卡还在半路）—— 要看的数字在 lastLog 里，
                //     和动画进度无关；等久了会把飞行中间态等过去。
                case 442:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.05) return;
                    ProbeSacDumpLastActivate("㊹② 第 5 次启动（AP 1→0）的原始日志");
                    Debug.Log("[AutoPlay/献祭] ㊹② 这一下之后还能做什么：" + ProbeSacCanActivateText());
                    Stage = 443;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊹③㊹④ 连拍：卡在桌面与破壁机之间的中间态
                //   （0.15 倍速 → 0.55 秒的动画拖到现实约 3.7 秒；这两档各等 0.25 秒
                //     = 游戏内约 0.037 / 0.075 秒 → 卡刚离开桌面、还没到罐口）
                case 443:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.25) return;
                    SacShotNow("sac21_02_consume_flying_a.png");
                    Debug.Log("[AutoPlay/献祭] ㊹③ 吞噬飞行第 1 帧：" + ProbeSacFlyingCountText("㊹③ 飞行第 1 帧"));
                    Debug.Log("[AutoPlay/献祭] ㊹③ 吞噬飞行第 1 帧（逐张坐标）：" + ProbeSacFlightText());
                    Stage = 444;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 444:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.25) return;
                    SacShotNow("sac21_03_consume_flying_b.png");
                    Debug.Log("[AutoPlay/献祭] ㊹④ 吞噬飞行第 2 帧：" + ProbeSacFlyingCountText("㊹④ 飞行第 2 帧"));
                    Debug.Log("[AutoPlay/献祭] ㊹④ 吞噬飞行第 2 帧（逐张坐标）：" + ProbeSacFlightText());
                    Stage = 445;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊹⑤ 飞完之后：卡自己飞完自毁了（IsConsuming 应当回到 0）
                case 445:
                    if (EditorApplication.timeSinceStartup - stageTime < 4.0) return;
                    SetTimeScale(1f, "㊹⑤ 时间恢复（那张卡应当已经自己飞完自毁）");
                    Debug.Log("[AutoPlay/献祭] ㊹⑤ 飞完之后（应当 0 张在飞）："
                              + ProbeSacFlyingCountText("㊹⑤ 飞完之后"));
                    Stage = 446;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊹⑥ ★ 截图：吞噬之后的桌面 + 面板上的新数字（刀片 H/V + 桌面张数）
                case 446:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.4) return;
                    if (!Shot("sac21_04_after_consume.png")) return;
                    Debug.Log("[AutoPlay/献祭] ㊹⑥ 吞噬之后：" + ProbeSacNumbers());
                    Debug.Log("[AutoPlay/献祭] ㊹⑥ 吞噬之后（再数一遍在飞的卡）："
                              + ProbeSacFlyingCountText("㊹⑥ 吞噬之后"));
                    ProbeLogSync("㊹⑥ 吞噬之后（回归：状态与画面一致 / 残留 0 张）");
                    Stage = 447;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊹⑦ 结束回合 → 第 2 回合（行动机会重置 5；附魔层数每回合结束 −1）
                case 447:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeSacEndRound();
                    Stage = 448;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊹⑧ 第 2 回合：桌面剩 水（D=1）+ 外星合金（D=1）—— 两张谁挨一下都会 D耗尽，
                //   所以"收回手牌"那两帧必须留到第 4 回合（那时桌上剩的是 D 还被没打过的硫磺）。
                case 448:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    if (!ProbeSacGuardAt("㊹⑧ 第 2 回合（第 2 回合、桌面 2 张、手牌 0 素材 + 1 法术）", 2, 2, 1))
                    {
                        Stage = 430;
                        stageTime = EditorApplication.timeSinceStartup;
                        return;
                    }
                    Debug.Log("[AutoPlay/献祭] ㊹⑧ 第 2 回合开局（桌面两张的 D 都只剩 1）：" + ProbeSacNumbers());
                    // ★ 时间倍率兜底：㊹① 把它压到 0.15 是为了拍飞行中间态，
                    //   万一 ㊹⑤ 那一档没跑到（守卫重开 / 被别的进程点掉），
                    //   后面每一档的等待时间都会按错的口径算 —— 这里再钉一次 1.0。
                    if (Mathf.Abs(Time.timeScale - 1f) > 0.0001f)
                        SetTimeScale(1f, "㊹⑧ 兜底（上一段的 0.15 倍速没被还原）");
                    Stage = 449;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊹⑨ 第 2 回合：把 D=1 的「水」用掉（启动一下 → D 0 → D耗尽移出桌面）
                //   ★ 这一步是给第 4 回合腾干净桌子：D=1 的卡再挨一下就没了，
                //     留在桌上只会污染"收回手牌"那两帧的前提。
                case 449:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    ProbeSacSelectTable("水");
                    ProbeSacActivate("㊹⑨ 第 2 回合启动「水」（D=1 → D耗尽移出桌面；行动机会 5→4）");
                    ProbeSacDumpLastActivate("㊹⑨ 这一下不该吞噬（手牌不空、行动机会还剩 4）");
                    Stage = 450;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊺⓪ 第 2 → 第 3 回合
                case 450:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeSacEndRound();
                    Stage = 451;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊺① 第 3 回合：把 D=1 的「外星合金」也用掉（同样 D耗尽移出桌面）
                case 451:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    ProbeSacSelectTable("外星合金");
                    ProbeSacActivate("㊺① 第 3 回合启动「外星合金」（D=1 → D耗尽移出桌面；行动机会 5→4）");
                    ProbeSacDumpLastActivate("㊺① 这一下不该吞噬（手牌不空、行动机会还剩 4）");
                    Stage = 452;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊺② 法术那条飞行动画：打出「火焰」→ 它也是走 ConsumeInto（另一条路径：
                //   TableRulesV21.OnSpellCardClicked 里 `view.ConsumeInto(JuicerMouth(view), 0.45f)`），
                //   而"打完立刻 RebuildHand → TableSetup.ClearHand"以前会把它当场销毁 ——
                //   所以这一档同样要抓到"卡在桌面与罐口之间"的中间帧。
                //   ★ 出牌/法术都不消耗行动机会，所以第 3 回合打它不影响任何数字。
                case 452:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    SetTimeScale(0.15f, "㊺② 打出「火焰」（顺带抓法术卡飞向罐口）");
                    ProbeSacPlaySpell();
                    Stage = 453;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 453:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.25) return;
                    SacShotNow("sac21_07_spell_flight_a.png");
                    Debug.Log("[AutoPlay/献祭] ㊺③ 法术卡飞行第 1 帧：" + ProbeSacFlyingCountText("㊺③ 法术飞行第 1 帧"));
                    Debug.Log("[AutoPlay/献祭] ㊺③ 法术卡飞行第 1 帧（逐张坐标）：" + ProbeSacFlightText());
                    Stage = 454;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 454:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.25) return;
                    SacShotNow("sac21_08_spell_flight_b.png");
                    Debug.Log("[AutoPlay/献祭] ㊺④ 法术卡飞行第 2 帧：" + ProbeSacFlyingCountText("㊺④ 法术飞行第 2 帧"));
                    Debug.Log("[AutoPlay/献祭] ㊺④ 法术卡飞行第 2 帧（逐张坐标）：" + ProbeSacFlightText());
                    Stage = 455;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊺⑤ 法术飞完之后：时间恢复 + 截图 + 级联布局报告（回归：`[V21][布局]` 不许有警告）
                case 455:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.4) return;
                    SetTimeScale(1f, "㊺⑤ 时间恢复");
                    Debug.Log("[AutoPlay/献祭] ㊺⑤ 法术飞完之后（应当 0 张在飞）："
                              + ProbeSacFlyingCountText("㊺⑤ 法术飞完之后"));
                    if (!Shot("sac21_09_after_spell_layout.png")) return;
                    Debug.Log("[AutoPlay/献祭] ㊺⑤ 法术打完之后：" + ProbeSacNumbers());
                    ProbeLayoutReport("㊺⑤ 法术打完之后（回归：级联布局不变式）");
                    ProbeLogSync("㊺⑤ 法术打完之后（回归：状态与画面一致 / 残留 0 张）");
                    ProbeSacWoodShader("㊺⑤ 木纹材质读回");
                    Stage = 456;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊺⑥ 第 3 → 第 4 回合
                case 456:
                    if (EditorApplication.timeSinceStartup - stageTime < 0.8) return;
                    ProbeSacEndRound();
                    Stage = 457;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊺⑦ 第 4 回合：**桌面空、手里只剩两张空白卡**（前几张素材要么被吞噬、要么 D耗尽）
                //   ★★ 这一段踩到的那条不变式（如实写在这里，省得下一个人再试一遍）：
                //     素材一旦挨到 D≤0 就被 `ExhaustTarget` **永久**移出桌面并成一张空白卡，
                //     而"被启动过 → 本回合不能收回手牌"这条记录在 `EndRound` 时清零。
                //     所以"第 N 回合启动过、第 N+1 回合还在桌上、又还有 D 可扣"这张卡**不存在**
                //     —— 能拒的那一张必须**在同一回合里**先被启动、再被拖。
                //     这一局的 3 张素材在第 1 回合就全部被打过了（3 张 D=3，一回合 5 次启动），
                //     所以"拒绝收回"这个反例只能靠**这一回合新打上桌的一张**来造。
                //   —— 于是这一段改成：先把本回合的空白卡打上桌 → 拖回手牌（**期望被收下**）；
                //     而"启动过的收不回来"那条由存档探针的 ㊱③
                //     （`ProbeWithdrawStartedCheck`，它专门造了"同一回合、已启动"的局面）覆盖。
                case 457:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    if (!ProbeSacGuardAt("㊺⑦ 第 4 回合（第 4 回合、桌面 0 张、手牌 1 张 = 空白卡）", 4, 0, 1))
                    {
                        Stage = 430;
                        stageTime = EditorApplication.timeSinceStartup;
                        return;
                    }
                    Debug.Log("[AutoPlay/献祭] ㊺⑦ 第 4 回合开局（桌面已空 —— 素材都被吞噬 / D耗尽带走了）："
                              + ProbeSacNumbers());
                    Stage = 458;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊺⑧ 把本回合**没启动过**的「空白卡」打上桌（本回合第一次，D 满 3）
                case 458:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    ProbeSacPlayHandMaterial("空白卡", "㊺⑧ 第 4 回合：空白卡 上桌（本回合还没启动过）");
                    Stage = 459;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊺⑨ ★ 正例：拖它回手牌 → **必须被收下**（本回合没启动过）
                case 459:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    ProbeSacWithdrawNamed("空白卡", "㊺⑨ 拖「空白卡」回手牌（本回合没启动过 → 期望被收下）");
                    ProbeLogSync("㊺⑨ 收回手牌之后");
                    Stage = 460;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊻⓪ ★ 反例（同一回合里先启动、再拖）：把空白卡再打上桌 → **启动一次**
                //   （D 3→2，仍在桌上）→ 然后拖它 ↓
                case 460:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    ProbeSacPlayHandMaterial("空白卡", "㊻⓪ 第 4 回合：空白卡 再上桌（准备验「启动过的收不回来」）");
                    Stage = 461;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                case 461:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    if (!ProbeSacSelectTable("空白卡"))
                    {
                        Debug.LogWarning("[AutoPlay/献祭] ㊻① 桌面上没有空白卡（\"启动过的收不回来\"这一帧的前提不成立）"
                                         + "：" + ProbeSacNumbers());
                        Stage = 463;
                        stageTime = EditorApplication.timeSinceStartup;
                        return;
                    }
                    ProbeSacActivate("㊻① 第 4 回合启动空白卡（行动机会 5→4；D 3→2，仍在桌上）");
                    ProbeSacDumpLastActivate("㊻① 这一下不该吞噬（手牌不空、行动机会还剩 4）");
                    Stage = 462;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊻② ★★ 反例：拖**本回合刚启动过**的「空白卡」回手牌 → **必须被拒**
                case 462:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.0) return;
                    ProbeSacWithdrawNamed("空白卡", "㊻② 拖「空白卡」回手牌（它本回合刚启动过 → 期望被拒）");
                    Stage = 463;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊻③ 截图：收回之后（手牌里多了一张、桌面少了一张）+ 布局/同步回归
                case 463:
                    if (!Shot("sac21_05_withdraw_back.png")) return;
                    Debug.Log("[AutoPlay/献祭] ㊻③ 收回手牌之后：" + ProbeSacNumbers());
                    ProbeSacWoodShader("㊻③ 木纹材质再读一次");
                    ProbeLogSync("㊻③ 收回手牌之后（回归：状态与画面一致 / 残留 0 张）");
                    ProbeLayoutReport("㊻③ 收回手牌之后（回归：级联布局不变式）");
                    Stage = 464;
                    stageTime = EditorApplication.timeSinceStartup;
                    return;

                // ㊻④ 收尾
                case 464:
                    if (EditorApplication.timeSinceStartup - stageTime < 3.0) return;
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

            // ★ 桌上 0 张不是"量不了"，是**本该如此**：v2.1 的关卡只在 UI 窗口里选
            //   （用户："不要关卡手牌了，就放一个 ui 就行"），这里再报一条
            //   "投影失败（相机没就位？）"纯属误导 —— 报个 0 张就够说明问题了。
            if (rig.BigCardCount == 0)
            {
                Debug.Log("[AutoPlay/V21] " + what + "卡：桌上一张都没有（"
                          + (TableSettings.UseRulesV21 ? "v2.1 关卡只走 UI 窗口" : "★ 旧流程不该为空")
                          + "）。");
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
                      + "　｜　张数 " + rig.BigCardCount
                      + "　｜　行数 " + rig.LastLayoutRows
                      + "　缩放 " + rig.LastLayoutScale.ToString("0.00")
                      + "　｜　右边缘余量 " + (Screen.width - x1).ToString("0.0")
                      + "，下边缘余量 " + (Screen.height - y1).ToString("0.0"));
        }

        // ── 选关探针（㉖⓪~㉖⑤，DSH_LEVELPROBE=1）用的三个口子 ──────────────

        /// <summary>
        /// 把"这一屏的选关状态"打成一行日志：模式 / 桌上大卡张数 / 选中态 / 窗口状态 / 当前关卡。
        ///
        /// 【为什么要分两个张数】rig 认领的（deckCards）和场景里真实存在的
        ///   （名字以 BigCard_ 开头的物体）是两回事 —— "列表清了、物体还杵在桌上"
        ///   这种残留只有把场景扫一遍才抓得到，而它正是"关掉 3D 卡"最容易留下的尾巴。
        /// </summary>
        private static void LogLevelSelectState(string what)
        {
            TableChoiceRig rig = Object.FindObjectOfType<TableChoiceRig>();
            TableTurnLoop loop = Loop();
            TableHud hud = Object.FindObjectOfType<TableHud>();

            if (rig == null || loop == null)
            {
                Debug.LogWarning("[AutoPlay/Level] 找不到 TableChoiceRig / TableTurnLoop，" + what + " 这次记不了。");
                return;
            }

            int inScene = 0;
            Transform[] all = Object.FindObjectsOfType<Transform>();
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].name.StartsWith("BigCard_")) inScene++;

            string levelName = loop.level != null ? loop.level.Name : "（无）";

            Debug.Log("[AutoPlay/Level] " + what
                      + "｜模式 " + (TableSettings.UseRulesV21 ? "v2.1" : "旧流程")
                      + "（桌上一排 3D 关卡卡：" + (TableSettings.LevelCardsOnTable ? "有" : "没有") + "）"
                      + "｜大卡 rig " + rig.BigCardCount + " 张 / 场景 " + inScene + " 个"
                      + "｜rig 选中 " + rig.DeckSelected
                      + "｜窗口 " + (hud != null ? (hud.LevelWindowOpen ? "开" : "关") : "?")
                      + (hud != null ? "、窗口选中 " + hud.LevelWindowPick : "")
                      + "｜当前关卡 " + (loop.levelIndex + 1) + "/" + loop.levels.Count + "「" + levelName + "」"
                      + "｜阶段 " + loop.phase);
        }

        /// <summary>
        /// 关卡窗口里点一行 —— 走 HUD 的 PickLevelInWindow，
        /// 和那个按钮是**同一个方法**（探针点不了 IMGUI 按钮，但不能绕过这条路）。
        /// </summary>
        private static void PickLevelRow(int i)
        {
            TableHud hud = Object.FindObjectOfType<TableHud>();
            if (hud == null)
            {
                Debug.LogWarning("[AutoPlay/Level] 找不到 TableHud，窗口点行这次验不了。");
                return;
            }

            hud.PickLevelInWindow(i);
            Debug.Log("[AutoPlay/Level] 在关卡窗口里点了第 " + (i + 1) + " 行（索引 " + i + "）。");
            LogLevelSelectState("㉖② 点完之后");
        }

        /// <summary>
        /// 按关卡窗口底部的「进入这一关」—— 同样走 HUD 的公开口子，
        /// 和按钮共用 ConfirmLevelInWindow（它里面就是 turnLoop.ConfirmLevelSelect()）。
        /// </summary>
        private static void ConfirmLevelRow()
        {
            TableHud hud = Object.FindObjectOfType<TableHud>();
            TableTurnLoop loop = Loop();
            if (hud == null || loop == null)
            {
                Debug.LogWarning("[AutoPlay/Level] 找不到 TableHud / TableTurnLoop，窗口确认这次验不了。");
                return;
            }

            int beforeIdx = loop.levelIndex;
            string beforeName = loop.level != null ? loop.level.Name : "（无）";

            hud.ConfirmLevelInWindow();

            string afterName = loop.level != null ? loop.level.Name : "（无）";

            // 目标分从**配置里那一关**读（LevelTargetScore 收的是 LevelData）——
            // v2.1 显示的是 60、旧流程显示 levels[].targetScore，两边都得对得上窗口里那一行。
            GameJam.Data.LevelData ld =
                (loop.levelIndex >= 0 && loop.levelIndex < loop.levels.Count)
                    ? loop.levels[loop.levelIndex] : null;
            int target = TableSettings.LevelTargetScore(ld);

            Debug.Log("[AutoPlay/Level] 按「进入这一关」：确认前第 " + (beforeIdx + 1) + " 关「" + beforeName
                      + "」→ 确认后第 " + (loop.levelIndex + 1) + " 关「" + afterName
                      + "」｜目标分 " + target
                      + "｜阶段 " + loop.phase
                      + "｜窗口 " + (hud.LevelWindowOpen ? "还开着（★ 应该收起来）" : "已自动收起"));
        }

        /// <summary>开 / 关关卡窗口（HUD 的公开口子）。</summary>
        private static void SetLevelWindow(bool open)
        {
            TableHud hud = Object.FindObjectOfType<TableHud>();
            if (hud == null) return;

            hud.SetLevelWindowOpen(open);
            Debug.Log("[AutoPlay/V21] 关卡窗口 " + (open ? "已打开" : "已关闭"));
        }

        // ── 选牌组探针（㉜⓪~㉜⑦，DSH_DECKPROBE=1）用的四个口子 ──────────────
        //   和选关探针那四个是一对一对的（Log/Pick/Confirm/SetWindow），
        //   连日志前缀的口径都一样：认领张数与场景张数分开报。

        /// <summary>
        /// 把"这一屏的选牌组状态"打成一行日志：模式 / 桌上大卡张数 / 选中态 / 窗口状态 / 候选项顺序。
        ///
        /// 【为什么要把候选项顺序也打出来】"点第 3 副、确认之后生效的是第 3 副"这句话，
        ///   光看"牌组已定：某某"证明不了 —— 得先有"第 3 副本来叫什么"。
        ///   顺手把**卡片顺序和窗口行顺序**钉在同一份日志里（两边都读 Choice.options，
        ///   顺序本来就该一致；不一致的话这里会一眼看出来）。
        /// </summary>
        private static void LogDeckPickState(string what)
        {
            TableChoiceRig rig = Object.FindObjectOfType<TableChoiceRig>();
            TableTurnLoop loop = Loop();
            TableHud hud = Object.FindObjectOfType<TableHud>();
            TableSetup setup = Object.FindObjectOfType<TableSetup>();

            if (rig == null || loop == null)
            {
                Debug.LogWarning("[AutoPlay/Deck] 找不到 TableChoiceRig / TableTurnLoop，" + what + " 这次记不了。");
                return;
            }

            int inScene = 0;
            Transform[] all = Object.FindObjectsOfType<Transform>();
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].name.StartsWith("BigCard_")) inScene++;

            // 候选项：牌组阶段用 CurrentChoice，退出这一屏之后退回配置里那份
            GameJam.Data.Choice c = loop.CurrentChoice;
            if (c == null) c = loop.FindChoice(GameConfig.DeckPickId);

            string order = "（无）";
            if (c != null && c.options != null)
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                for (int i = 0; i < c.options.Count; i++)
                {
                    if (sb.Length > 0) sb.Append(" / ");
                    sb.Append(i + 1).Append('.').Append(c.options[i] != null ? c.options[i].title : "（空）");
                }
                order = sb.ToString();
            }

            Debug.Log("[AutoPlay/Deck] " + what
                      + "｜模式 " + (TableSettings.UseRulesV21 ? "v2.1" : "旧流程")
                      + "（桌上一排 3D 牌组卡：" + (TableSettings.DeckCardsOnTable ? "有" : "没有") + "）"
                      + "｜大卡 rig " + rig.BigCardCount + " 张 / 场景 " + inScene + " 个"
                      + "｜rig 选中 " + rig.DeckSelected
                      + "｜窗口 " + (hud != null ? (hud.DeckWindowOpen ? "开" : "关") : "?")
                      + (hud != null ? "、窗口选中 " + hud.DeckWindowPick : "")
                      + "｜候选项 " + (c != null ? c.options.Count : 0) + " 副：" + order
                      + "｜已定牌组「" + (setup != null ? setup.deckName : "?") + "」"
                      + "｜手牌 " + (setup != null && setup.hand != null ? setup.hand.Count : -1) + " 张"
                      + "｜阶段 " + loop.phase);
        }

        /// <summary>
        /// 牌组窗口里点一行 —— 走 HUD 的 PickDeckInWindow，
        /// 和那个按钮是**同一个方法**（探针点不了 IMGUI 按钮，但不能绕过这条路）。
        /// </summary>
        private static void PickDeckRow(int i)
        {
            TableHud hud = Object.FindObjectOfType<TableHud>();
            if (hud == null)
            {
                Debug.LogWarning("[AutoPlay/Deck] 找不到 TableHud，窗口点行这次验不了。");
                return;
            }

            hud.PickDeckInWindow(i);
            Debug.Log("[AutoPlay/Deck] 在牌组窗口里点了第 " + (i + 1) + " 副（索引 " + i + "）。");
            LogDeckPickState("㉜② 点完之后");
        }

        /// <summary>
        /// 按牌组窗口底部的「确认选择该卡组」—— 同样走 HUD 的公开口子，
        /// 和按钮共用 ConfirmDeckInWindow（它里面就是 turnLoop.ConfirmDeckPick()）。
        ///
        /// 【为什么要打"刀片 / 手牌"】"牌组真的生效了"不能只看阶段变了 ——
        ///   牌组决定的就是开局刀片和初始手牌，这两个数变了才算数
        ///   （v2.1 的手牌要等选了核心才发，所以这一屏手牌还是 0，刀片名才是判据）。
        /// </summary>
        private static void ConfirmDeckRow()
        {
            TableHud hud = Object.FindObjectOfType<TableHud>();
            TableTurnLoop loop = Loop();
            TableSetup setup = Object.FindObjectOfType<TableSetup>();

            if (hud == null || loop == null)
            {
                Debug.LogWarning("[AutoPlay/Deck] 找不到 TableHud / TableTurnLoop，窗口确认这次验不了。");
                return;
            }

            string beforeDeck = setup != null ? setup.deckName : "?";
            int beforePick = hud.DeckWindowPick;

            // 窗口里那一行到底对应哪一副：从**同一份配置**读出来，
            // 这样"确认之后生效的是不是它"才有判据（只看阶段变了证明不了这件事）。
            string wantDeck = "（无）", wantBlade = "（无）";
            GameJam.Data.Choice c = loop.FindChoice(GameConfig.DeckPickId);
            if (c != null && c.options != null && beforePick >= 0 && beforePick < c.options.Count
                && c.options[beforePick] != null)
            {
                GameJam.Data.ChoiceOption o = c.options[beforePick];
                wantDeck = o.title;

                GameJam.Data.Ingredient b = o.deck != null ? o.deck.InitialBlade() : null;
                wantBlade = (o.deck != null ? o.deck.name : "?")
                            + "｜开局刀片 " + (b != null ? b.name : "（无）");
            }

            hud.ConfirmDeckInWindow();

            Debug.Log("[AutoPlay/Deck] 按「确认选择该卡组」：窗口选中第 " + (beforePick + 1) + " 副「"
                      + wantDeck + "」= " + wantBlade
                      + "｜确认前牌组「" + beforeDeck + "」→ 确认后牌组「"
                      + (setup != null ? setup.deckName : "?") + "」"
                      + "｜手牌 " + (setup != null && setup.hand != null ? setup.hand.Count : -1) + " 张"
                      + "｜阶段 " + loop.phase
                      + "｜窗口 " + (hud.DeckWindowOpen ? "还开着（★ 应该收起来）" : "已自动收起"));
        }

        /// <summary>开 / 关牌组窗口（HUD 的公开口子，和 Esc 菜单里那一项同一个入口）。</summary>
        private static void SetDeckWindow(bool open)
        {
            TableHud hud = Object.FindObjectOfType<TableHud>();
            if (hud == null) return;

            hud.SetDeckWindowOpen(open);
            Debug.Log("[AutoPlay/V21] 牌组窗口 " + (open ? "已打开" : "已关闭"));
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

        /// <summary>
        /// 把"手牌那一排现在占屏幕哪一块"量成数字（主链/取景探针共用）。
        ///
        /// 【为什么要拿 WorldToScreenPoint 再量一遍】游戏里那条 [V21][取景] 自检证明的是
        ///   "**算出来的机位**装得下"；这里量的是**这一帧真正画出来的东西** ——
        ///   相机此刻的实际位置、当前宽高比、每张牌的真实坐标。
        ///   两条都对上，"手牌完整可见"才算数（少任何一条都可能是"算对了但没生效"）。
        /// </summary>
        private static void ProbeFramingReport(string what)
        {
            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            if (setup == null || setup.cam == null)
            {
                Debug.LogWarning("[AutoPlay/取景] 找不到 TableSetup / Camera，这一屏量不了。");
                return;
            }

            Camera cam = setup.cam;
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            int cards = 0;
            bool behind = false;

            if (setup.hand != null)
            {
                for (int i = 0; i < setup.hand.Count; i++)
                {
                    PlayCard c = setup.hand[i];
                    if (c == null || c.slotIndex >= 0) continue;      // 已经放进槽里的不算"手牌那一排"
                    cards++;

                    // 四个角按卡的偏航算（手牌是扇形张开的，最外侧那一点就在角上）
                    float yaw = c.transform.eulerAngles.y * Mathf.Deg2Rad;
                    float cos = Mathf.Cos(yaw), sin = Mathf.Sin(yaw);
                    for (int sx = -1; sx <= 1; sx += 2)
                    {
                        for (int sz = -1; sz <= 1; sz += 2)
                        {
                            float lx = sx * CardFactory.CardWidth * 0.5f;
                            float lz = sz * CardFactory.CardDepth * 0.5f;
                            Vector3 corner = new Vector3(lx * cos + lz * sin, 0f, -lx * sin + lz * cos);

                            Vector3 sp = cam.WorldToScreenPoint(c.transform.position + corner);
                            if (sp.z <= 0f) { behind = true; continue; }   // 在相机背后，投影没有意义

                            if (sp.x < x0) x0 = sp.x;
                            if (sp.x > x1) x1 = sp.x;
                            if (sp.y < y0) y0 = sp.y;
                            if (sp.y > y1) y1 = sp.y;
                        }
                    }
                }
            }

            if (cards == 0)
            {
                Debug.Log("[AutoPlay/取景] " + what + "：手牌是空的，这一屏没有手牌可量。");
                return;
            }

            // WorldToScreenPoint 的原点在左下角：所以 y0 就是"离画面下边缘多少像素"。
            // ★ 边界取**相机的视口**（cam.pixelRect）而不是整块屏幕：取景探针的兜底办法是
            //   把相机压成目标比例（见 ForceCameraAspect），那时画面四周是黑边、
            //   真正的画面只是视口那一块 —— 拿屏幕边界量会量出一个假的"完整可见"。
            float left = x0 - cam.pixelRect.xMin;
            float right = cam.pixelRect.xMax - x1;
            float bottom = y0 - cam.pixelRect.yMin;
            float top = cam.pixelRect.yMax - y1;
            float worst = Mathf.Min(Mathf.Min(left, right), Mathf.Min(bottom, top));
            bool ok = !behind && worst > 0f;

            // ★★「手牌有没有被 UI 压住」**不能靠眼睛看**：把 HUD 这一帧真画出来的矩形
            //    （提示块 + v2.1 那几块面板）和手牌包围盒逐对求交，写成像素数。
            //    用户那次报的正是"牌在画面里、但提示压在上面"—— 只量"在不在画面里"量不出来。
            string uiText = "UI 压住：HUD 没找到（量不了）";
            TableHud hud = Object.FindObjectOfType<TableHud>();
            if (hud != null)
            {
                // 手牌的屏幕包围盒换成 IMGUI 那套坐标（左上原点）
                Rect handBox = Rect.MinMaxRect(x0, Screen.height - y1, x1, Screen.height - y0);

                float worstArea = 0f;
                string worstName = "";

                UiOverlap(handBox, hud.HintBlockScreenRect, "提示块", ref worstArea, ref worstName);
                UiOverlap(handBox, hud.InspectScreenRect, "检视窗口", ref worstArea, ref worstName);

                List<Rect> panels = hud.PanelScreenRects;
                if (panels != null)
                    for (int i = 0; i < panels.Count; i++)
                        UiOverlap(handBox, panels[i], "v2.1 面板 " + i, ref worstArea, ref worstName);

                uiText = worstArea > 0f
                    ? ("★ UI 压住手牌：" + worstName)
                    : ("UI 压住：无（提示块 / 检视窗口 / " + (panels != null ? panels.Count : 0) + " 块面板都不相交）");

                // 提示块这一帧**到底画没画**也写出来：ShowHints=false 那一屏要能一眼看出"未画"
                Rect hr = hud.HintBlockScreenRect;
                uiText += hr.width > 0f
                    ? ("｜提示块 " + hr.width.ToString("0") + "×" + hr.height.ToString("0")
                       + " px @ y " + hr.y.ToString("0") + "~" + hr.yMax.ToString("0"))
                    : "｜提示块：未画";
            }

            Debug.Log("[AutoPlay/取景] " + what
                      + "：屏幕 " + Screen.width + "×" + Screen.height
                      + "（宽高比 " + cam.aspect.ToString("0.00") + "）"
                      + "｜视口 " + cam.pixelRect.width.ToString("0") + "×" + cam.pixelRect.height.ToString("0")
                      + "｜视角 " + (setup.rig != null ? setup.rig.CurrentView : "?")
                      + "｜机位 (" + cam.transform.position.x.ToString("0.00") + ", "
                      + cam.transform.position.y.ToString("0.00") + ", "
                      + cam.transform.position.z.ToString("0.00") + ")"
                      + "｜手牌 " + cards + " 张的屏幕包围盒 x ∈ [" + x0.ToString("0") + ", " + x1.ToString("0")
                      + "] y ∈ [" + y0.ToString("0") + ", " + y1.ToString("0") + "]"
                      + "（" + (x1 - x0).ToString("0") + " × " + (y1 - y0).ToString("0") + " px）"
                      + "｜四边余量 左 " + left.ToString("0") + " 右 " + right.ToString("0")
                      + " 下 " + bottom.ToString("0") + " 上 " + top.ToString("0") + " px"
                      + "｜" + uiText
                      + (ok ? " —— ✓ 完整可见" : " —— ★ 出画了"));
        }

        /// <summary>
        /// 两个屏幕矩形（左上原点）相交了多少 —— 把最严重那一对的名字与尺寸记进 ref。
        /// 取景探针用它把"UI 压住手牌"变成数字（提示块压住牌那次，就是这么量出来的）。
        /// </summary>
        private static void UiOverlap(Rect hand, Rect ui, string name, ref float worstArea, ref string worstName)
        {
            if (ui.width <= 0f || ui.height <= 0f) return;

            float w = Mathf.Min(hand.xMax, ui.xMax) - Mathf.Max(hand.xMin, ui.xMin);
            float h = Mathf.Min(hand.yMax, ui.yMax) - Mathf.Max(hand.yMin, ui.yMin);
            if (w <= 0f || h <= 0f) return;

            float area = w * h;
            if (area <= worstArea) return;

            worstArea = area;
            worstName = name + "（重叠 " + w.ToString("0") + "×" + h.ToString("0") + " px）";
        }

        // ══════════════════════════════════════════════════════════════
        //  相交清单：桌上这些东西**两两**量世界包围盒（DSH_OVERLAPPROBE=1）
        //
        //  【为什么要两两量，而不是"看一眼截图"】
        //   用户说的"穿模"有两种，长得像、根因完全不同：
        //     ① 世界包围盒真的相交 —— 两个东西占同一块空间（深度上打架、会闪面）；
        //     ② 世界包围盒**不相交**、屏幕包围盒相交 —— 它们在不同的深度上，
        //        只是这个机位下前后投影叠在一起。
        //   只报一种，另一种就会被当成"没事"。所以这里三种口径各量一次：
        //   世界 AABB（三维）/ 桌面 XZ（谁占了同一块桌面）/ 屏幕 AABB（这一帧画面上叠没叠）。
        // ══════════════════════════════════════════════════════════════

        /// <summary>一件要被两两求交的东西：名字 + 世界包围盒 + 屏幕包围盒（左上原点）。</summary>
        private class BoundItem
        {
            public string name;
            public Bounds box;
            public bool   onScreen;
            public float  px0, py0, px1, py1;

            /// <summary>
            /// 同一个"整体"的多个口径（比如破壁机整机和它那块立绘）—— **组内不互相判交**：
            /// 立绘本来就是机身的一部分，父子之间"相交"是废话，混进清单里会让人以为还有一处穿模。
            /// </summary>
            public string group;
        }

        /// <summary>
        /// 相机此刻的**真实**机位 —— "有没有被换掉"的原始数字。
        ///
        /// 【为什么不能只看 rig.CurrentView】视角名一样不代表机位一样：桌面视角这个名字下，
        ///   坐标既可能是写死的常量、也可能是每次按内容拟合出来的。要对历史坐标，
        ///   只能把相机的位置 / 朝向 / fov 打出来逐个数比。
        /// </summary>
        private static void LogCameraPose(string what)
        {
            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            if (setup == null || setup.cam == null)
            {
                Debug.LogWarning("[AutoPlay/机位] " + what + "：找不到 TableSetup / 相机，机位量不了。");
                return;
            }

            Camera cam = setup.cam;
            Vector3 p = cam.transform.position;
            Vector3 e = cam.transform.eulerAngles;

            // 视轴打在桌面（y = 0）上的那一点 —— 就是这批机位的"注视点"口径
            Vector3 look = p + cam.transform.forward * (p.y / Mathf.Max(0.0001f, -cam.transform.forward.y));

            Debug.Log("[AutoPlay/机位] " + what
                      + "：视角 " + (setup.rig != null ? setup.rig.CurrentView : "?")
                      + "｜机位 (" + p.x.ToString("0.000") + ", " + p.y.ToString("0.000") + ", " + p.z.ToString("0.000") + ")"
                      + "｜朝向 (" + e.x.ToString("0.0") + ", " + e.y.ToString("0.0") + ", " + e.z.ToString("0.0") + ")"
                      + "｜fov " + cam.fieldOfView.ToString("0.##")
                      + "｜视轴落桌点 (" + look.x.ToString("0.000") + ", " + look.z.ToString("0.000") + ")"
                      + "｜宽高比 " + cam.aspect.ToString("0.000")
                      + "｜屏幕 " + Screen.width + "×" + Screen.height);
        }

        /// <summary>把一个物体（含子物体）**活着的**渲染器合成一个世界包围盒，加进清单。</summary>
        private static void AddPropBounds(List<BoundItem> list, string name, GameObject root, Camera cam,
                                          string group = null)
        {
            if (root == null) return;

            Renderer[] rs = root.GetComponentsInChildren<Renderer>();
            bool any = false;
            Bounds b = new Bounds();
            for (int i = 0; i < rs.Length; i++)
            {
                Renderer r = rs[i];
                if (r == null || !r.enabled) continue;                 // 关掉的（比如被立绘顶掉的机身）不算
                if (!r.gameObject.activeInHierarchy) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            if (!any) return;

            AddBox(list, name, b, cam, group);
        }

        /// <summary>把一个世界包围盒加进清单，顺带按当前相机算出它的屏幕包围盒。</summary>
        private static void AddBox(List<BoundItem> list, string name, Bounds b, Camera cam, string group = null)
        {
            BoundItem it = new BoundItem();
            it.name  = name;
            it.box   = b;
            it.group = group;

            if (cam != null)
            {
                float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                bool ok = true;

                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x,
                                            (i & 2) == 0 ? b.min.y : b.max.y,
                                            (i & 4) == 0 ? b.min.z : b.max.z);
                    Vector3 s = cam.WorldToScreenPoint(c);
                    if (s.z <= 0f) { ok = false; break; }              // 角在相机背后，投影没意义
                    float sy = Screen.height - s.y;                    // 左下原点 → 左上原点
                    if (s.x  < x0) x0 = s.x;
                    if (s.x  > x1) x1 = s.x;
                    if (sy   < y0) y0 = sy;
                    if (sy   > y1) y1 = sy;
                }

                it.onScreen = ok;
                if (ok) { it.px0 = x0; it.py0 = y0; it.px1 = x1; it.py1 = y1; }
            }

            list.Add(it);
        }

        /// <summary>一根轴上的重叠量：正数 = 重叠、负数 = 隔着多少（报告里两种都要看得见）。</summary>
        private static float AxisGap(float a0, float a1, float b0, float b1)
        {
            return Mathf.Min(a1, b1) - Mathf.Max(a0, b0);
        }

        private static string AxisText(string axis, float gap)
        {
            return axis + (gap > 0f ? (" 重叠 " + gap.ToString("0.000")) : (" 隔 " + (-gap).ToString("0.000")));
        }

        /// <summary>
        /// 桌上这些东西的世界包围盒清单 —— 用户点名的那几样一个不少。
        ///
        /// 【为什么按根节点名找蜡烛和量筒】它们是 TableTitleRig / ScoreCylinderRig 各自建的，
        ///   TableSetup 上没有引用（量筒只有 juicer.scoreBoard 那一条路）。按名字找是探针的做法，
        ///   游戏里那两处各自有常量（这次改动把它们收成常量了）。
        ///   ★ 量筒的包围盒**含左边那排刻度数字** —— 用户截图里压在蜡烛旁边的正是那几个数，
        ///     所以数字必须是这个盒子的一部分，漏掉它就等于把用户报的那一幕量没了。
        /// </summary>
        private static List<BoundItem> CollectBoundItems(TableSetup setup, Camera cam)
        {
            List<BoundItem> list = new List<BoundItem>();

            // ① 蜡烛 / 量筒
            AddPropBounds(list, "蜡烛（TitleCandle）", GameObject.Find("TitleCandle"), cam);
            AddPropBounds(list, "量筒（ScoreCylinder，含刻度数字）", GameObject.Find("ScoreCylinder"), cam);

            // ② 破壁机：整机 + 立绘单独一份（立绘是玩家真正看到的那个剪影）
            if (setup.juicer != null)
            {
                AddPropBounds(list, "破壁机（立绘+机身，活着的）", setup.juicer.gameObject, cam, "juicer");

                Transform art = setup.juicer.transform.Find("BlenderArt");
                if (art != null) AddPropBounds(list, "破壁机立绘（BlenderArt）", art.gameObject, cam, "juicer");
            }

            // ③ 两个槽位框（框平时是隐形的，按 board 的矩形算）+ 两块槽名牌
            if (setup.board != null)
            {
                for (int i = 0; i < setup.board.SlotCount; i++)
                {
                    Vector3 sp = setup.board.SlotPosition(i);
                    AddBox(list, "槽位框 " + i,
                           new Bounds(new Vector3(sp.x, 0.0022f, sp.z),
                                      new Vector3(setup.board.slotSizeX, 0.0001f, setup.board.slotSizeZ)), cam);
                }
            }
            AddPropBounds(list, "槽名牌 0", GameObject.Find("SlotLabel0"), cam);
            AddPropBounds(list, "槽名牌 1", GameObject.Find("SlotLabel1"), cam);

            // ④ 卡：手牌那一排 / 刀片卡（含「刀 片」标记）/ 桌面素材级联 —— 都是场景里真实那几张
            TableTurnLoop loop = Loop();
            PlayCard[] all = Object.FindObjectsOfType<PlayCard>();

            bool  anyHand = false, anyTable = false;
            Bounds handBox = new Bounds(), tableBox = new Bounds();
            int   handN = 0, tableN = 0;

            for (int i = 0; i < all.Length; i++)
            {
                PlayCard c = all[i];
                if (c == null || c.markedForDestroy) continue;

                if (setup.hand != null && setup.hand.Contains(c))
                {
                    if (!anyHand) { handBox = CardBounds(c); anyHand = true; }
                    else handBox.Encapsulate(CardBounds(c));
                    handN++;
                    continue;
                }

                if (loop != null && loop.bladeCard == c)
                {
                    AddPropBounds(list, "刀片卡（" + c.DisplayName + "）", c.gameObject, cam, "blade");

                    // 「刀 片」那两个字是**贴在卡前方桌面上**的（不在卡身里），单独量一份
                    TextMesh[] marks = c.GetComponentsInChildren<TextMesh>();
                    bool anyMark = false;
                    Bounds markBox = new Bounds();
                    for (int k = 0; k < marks.Length; k++)
                    {
                        if (marks[k] == null || marks[k].text == null) continue;
                        if (marks[k].text.IndexOf('刀') < 0) continue;

                        Renderer mr = marks[k].GetComponent<Renderer>();
                        if (mr == null || !mr.enabled) continue;

                        if (!anyMark) { markBox = mr.bounds; anyMark = true; }
                        else markBox.Encapsulate(mr.bounds);
                    }
                    if (anyMark) AddBox(list, "刀片标记「刀 片」", markBox, cam, "blade");
                    continue;
                }

                if (!anyTable) { tableBox = CardBounds(c); anyTable = true; }
                else tableBox.Encapsulate(CardBounds(c));
                tableN++;
            }

            if (anyHand)  AddBox(list, "手牌那一排（" + handN + " 张）", handBox, cam);
            if (anyTable) AddBox(list, "桌面素材级联（" + tableN + " 张）", tableBox, cam);

            // ⑤ 牌组卡排 / 关卡卡排 —— TableChoiceRig.BuildOne 建的那些大卡，名字是 "BigCard_" + id。
            //
            //  【为什么要**逐张**列】用户新截图的症状就是逐张的："蜡烛从**第一张**（硫硝爆燃）中间穿出来、
            //    破壁机压在第 **4/5** 张上" —— 合成一个"整排"的盒子只能说"整排和机器有关",
            //    说不出是哪几张；而修法（换行 / 缩排）恰恰是按张算的。
            if (loop != null && loop.choiceRig != null && loop.choiceRig.root != null)
            {
                Transform[] kids = loop.choiceRig.root.GetComponentsInChildren<Transform>(true);
                int bigN = 0;

                for (int i = 0; i < kids.Length; i++)
                {
                    Transform t = kids[i];
                    if (t == null) continue;
                    if (!t.name.StartsWith("BigCard_")) continue;

                    bigN++;
                    AddPropBounds(list, "大卡 " + t.name.Substring("BigCard_".Length), t.gameObject, cam);
                }

                Debug.Log("[AutoPlay/相交]   　（这一屏有 " + bigN + " 张大卡：" + loop.choiceRig.LastLayoutRows
                          + " 行、缩放 " + loop.choiceRig.LastLayoutScale.ToString("0.00") + "）");
            }

            return list;
        }

        /// <summary>一张 3D 卡的占地（含卡身上的文字）—— 按它所有活着的渲染器合成。</summary>
        private static Bounds CardBounds(PlayCard c)
        {
            Renderer[] rs = c.GetComponentsInChildren<Renderer>();
            bool any = false;
            Bounds b = new Bounds();
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i] == null || !rs[i].enabled) continue;
                if (!rs[i].gameObject.activeInHierarchy) continue;
                if (!any) { b = rs[i].bounds; any = true; }
                else b.Encapsulate(rs[i].bounds);
            }
            return any ? b : new Bounds(c.transform.position, Vector3.one * 0.01f);
        }

        /// <summary>
        /// 相交清单报告：逐件打世界 / 屏幕包围盒，再**两两**判一次，把有关系的成对列出来。
        ///
        /// 判据（三种口径全报，缺一种就会把另一类"穿模"漏掉）：
        ///   · 世界 AABB：三根轴都重叠才算"占同一块空间"；
        ///   · 桌面 XZ：卡是平躺的，这一口径专门看"谁压在谁的地盘上"；
        ///   · 屏幕 AABB：这一帧画面上叠没叠（"看起来穿模"就是它）。
        /// </summary>
        private static void ProbeOverlapReport(string what)
        {
            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            if (setup == null) { Debug.LogWarning("[AutoPlay/相交] 找不到 TableSetup，这一屏量不了。"); return; }

            List<BoundItem> list = CollectBoundItems(setup, setup.cam);

            Debug.Log("[AutoPlay/相交] " + what + "｜共 " + list.Count + " 件东西（屏幕 "
                      + Screen.width + "×" + Screen.height + "，视角 "
                      + (setup.rig != null ? setup.rig.CurrentView : "?") + "）：");

            for (int i = 0; i < list.Count; i++)
            {
                BoundItem it = list[i];
                Bounds b = it.box;
                Debug.Log("[AutoPlay/相交]   　" + it.name
                          + "：世界 x " + b.min.x.ToString("0.000") + " ~ " + b.max.x.ToString("0.000")
                          + "，y " + b.min.y.ToString("0.000") + " ~ " + b.max.y.ToString("0.000")
                          + "，z " + b.min.z.ToString("0.000") + " ~ " + b.max.z.ToString("0.000")
                          + (it.onScreen
                             ? ("｜屏幕 x " + it.px0.ToString("0") + " ~ " + it.px1.ToString("0")
                                + "，y " + it.py0.ToString("0") + " ~ " + it.py1.ToString("0"))
                             : "｜（有角落在相机背后，屏幕盒没意义）"));
            }

            int pairs = 0, hits = 0;
            for (int i = 0; i < list.Count; i++)
            {
                for (int j = i + 1; j < list.Count; j++)
                {
                    pairs++;
                    BoundItem a = list[i], b = list[j];

                    // 同一个"整体"的两个口径（破壁机整机 × 它那块立绘、刀片卡 × 卡上那两个字）
                    // 不互相判交：父子之间有交集是必然的，混进清单会让人以为还有一处穿模。
                    if (!string.IsNullOrEmpty(a.group) && a.group == b.group) continue;

                    float gx = AxisGap(a.box.min.x, a.box.max.x, b.box.min.x, b.box.max.x);
                    float gy = AxisGap(a.box.min.y, a.box.max.y, b.box.min.y, b.box.max.y);
                    float gz = AxisGap(a.box.min.z, a.box.max.z, b.box.min.z, b.box.max.z);

                    bool world = gx > 0f && gy > 0f && gz > 0f;
                    bool xz    = gx > 0f && gz > 0f;

                    float sx = 0f, sy = 0f;
                    bool screen = a.onScreen && b.onScreen;
                    if (screen)
                    {
                        sx = AxisGap(a.px0, a.px1, b.px0, b.px1);
                        sy = AxisGap(a.py0, a.py1, b.py0, b.py1);
                        screen = sx > 0f && sy > 0f;
                    }

                    if (!world && !xz && !screen) continue;

                    hits++;
                    Debug.Log("[AutoPlay/相交]   ★ " + a.name + " × " + b.name + "："
                              + (world ? "世界 AABB 相交" : "世界 AABB 不相交")
                              + "（x " + AxisText("x", gx) + "、y " + AxisText("y", gy) + "、z " + AxisText("z", gz) + "）"
                              + "｜桌面 XZ " + (xz ? "重叠" : "错开")
                              + "｜屏幕 AABB "
                              + (screen
                                 ? ("重叠 " + sx.ToString("0") + " × " + sy.ToString("0") + " px")
                                 : (a.onScreen && b.onScreen ? "不相交" : "没量到")));
                }
            }

            Debug.Log("[AutoPlay/相交] " + what + "｜结论：" + pairs + " 对里 "
                      + (hits == 0 ? "✓ 一对都没有关系（不相交、不叠影）"
                                   : ("★ " + hits + " 对有关系（世界相交 / 桌面 XZ 重叠 / 屏幕叠影）")));
        }

        /// <summary>
        /// 把 Game 视图切成一个固定分辨率（**自己这一份，不走 SetGameViewSize**）。
        ///
        /// 【为什么要另写一份】原来那个 SetGameViewSize 在这台机器上抛 NRE
        ///   （"Object reference not set..."）：它在 `currentGroup` 这个对象上找 `GetGroup`，
        ///   而那个方法在 **GameViewSizes**（sizes）上、不在 group 上 —— 拿到 null 再 Invoke 就是 NRE。
        ///   这一份把每一步都拆开检查、失败时**说清卡在哪一步**，并且把"选中新尺寸"这一步也走完
        ///   （只 AddCustomSize 不选中，视图还是旧的）。
        ///
        /// 【为什么非要换分辨率】用户那张"6 副牌组排成一行、蜡烛插穿第一张卡"的截图是**宽窗口**下拍的：
        ///   宽高比一大，那一排就从 2 行 3 张变成 1 行 6 张、铺满整屏 —— 窄窗口里根本复现不出来。
        ///   它改的是**副本工程**的 Game 视图设置，不碰用户那份工程。
        /// </summary>
        private static bool SetGameViewSizeEx(int w, int h)
        {
            const string tag = "[AutoPlay/相交] 切 Game 视图 " + "";

            try
            {
                System.Type gvType    = System.Type.GetType("UnityEditor.GameView,UnityEditor");
                System.Type sizesType = System.Type.GetType("UnityEditor.GameViewSizes,UnityEditor");
                System.Type sizeType  = System.Type.GetType("UnityEditor.GameViewSize,UnityEditor");
                System.Type groupType = System.Type.GetType("UnityEditor.GameViewSizeGroupType,UnityEditor");

                if (gvType == null || sizesType == null || sizeType == null || groupType == null)
                { Debug.LogWarning(tag + "拿不到 GameViewSizes 类型。"); return false; }

                EditorWindow gv = EditorWindow.GetWindow(gvType);
                if (gv == null) { Debug.LogWarning(tag + "拿不到 Game 视图窗口。"); return false; }

                System.Reflection.PropertyInfo inst = typeof(ScriptableSingleton<>).MakeGenericType(sizesType)
                    .GetProperty("instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                object sizes = inst != null ? inst.GetValue(null) : null;
                if (sizes == null) { Debug.LogWarning(tag + "GameViewSizes.instance 是空的。"); return false; }

                object standalone = System.Enum.Parse(groupType, "Standalone");
                System.Reflection.MethodInfo getGroup = sizesType.GetMethod("GetGroup", new[] { groupType });
                if (getGroup == null) { Debug.LogWarning(tag + "GameViewSizes 上没有 GetGroup。"); return false; }

                object group = getGroup.Invoke(sizes, new object[] { standalone });
                if (group == null) { Debug.LogWarning(tag + "GetGroup(Standalone) 返回空。"); return false; }

                System.Reflection.ConstructorInfo ctor = null;
                System.Reflection.ParameterInfo[] ps = null;
                foreach (System.Reflection.ConstructorInfo c in sizeType.GetConstructors())
                    if (c.GetParameters().Length == 4) { ctor = c; ps = c.GetParameters(); break; }

                if (ctor == null) { Debug.LogWarning(tag + "GameViewSize 没有 4 参数构造。"); return false; }

                object kind = ps[0].ParameterType.IsEnum
                    ? System.Enum.Parse(ps[0].ParameterType, "FixedResolution")
                    : (object)1;

                object size = ctor.Invoke(new object[] { kind, w, h, "DSH " + w + "x" + h });

                System.Reflection.MethodInfo add = group.GetType().GetMethod("AddCustomSize", new[] { sizeType });
                if (add == null) { Debug.LogWarning(tag + "尺寸组上没有 AddCustomSize。"); return false; }
                add.Invoke(group, new object[] { size });

                System.Reflection.MethodInfo total = group.GetType().GetMethod("GetTotalCount");
                int index = total != null ? (int)total.Invoke(group, null) - 1 : -1;

                System.Reflection.MethodInfo sel = gvType.GetMethod("SizeSelectionCallback",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic);
                if (sel == null) { Debug.LogWarning(tag + "Game 视图上没有 SizeSelectionCallback。"); return false; }

                sel.Invoke(gv, new object[] { index, null });
                gv.Repaint();

                Debug.Log(tag + "已切到 " + w + "×" + h + "（尺寸组第 " + index + " 项）");
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning(tag + "失败：" + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 把编辑器的 Game 视图切到一个**固定分辨率**（取景探针按比例验收用）。
        ///
        /// 【为什么要动它】"装不装得下"随**宽高比**变：竖着的窗口和 1600×900 拟合出来的
        ///   不是同一台机位。要在一趟探针里同时验两种比例，就得能把 Game 视图换成两个分辨率 ——
        ///   否则只能验"我这台机器当前面板"的那一个，而用户点名的两个比例里
        ///   有一个（打包版 1600×900）在这台机器上根本复现不出来。
        ///
        /// ★ **实现只有一份**：㉙⓪ 那条相交链的 <see cref="SetGameViewSizeEx"/>（这台机器上实测能切成
        ///   1600×900 ✓）。我这份原来自己写了一份反射，写错了 `GetGroup` 找在哪个对象上
        ///   （它是 GameViewSizes 的方法，不是 currentGroup 的）→ 一 Invoke 就是一句
        ///   "Object reference not set"，日志里看不出卡在哪一步。两份反射实现迟早走岔，
        ///   所以这里只做转发 —— 要改就改那一份。
        /// </summary>
        private static bool SetGameViewSize(int w, int h)
        {
            return SetGameViewSizeEx(w, h);
        }

        /// <summary>
        /// 兜底：内部 API 用不了时**只压相机的宽高比**（公开 API，一定能成）。
        ///
        /// 【为什么这样也算验过】"装不装得下"是**投影**的事：Camera.aspect 一改，
        ///   游戏里的取景（TableSetup.Update → ReframeBoardView）就按新比例重算 ——
        ///   和真把窗口拉成那个比例**算出来的是同一台机位**。
        ///   视口矩形按比例居中，渲染不会被拉伸，所以图上量到的"完整可见"是真的。
        ///
        /// 【它验不到什么】HUD 是按 Screen 尺寸排的，这里 Screen 没变 ——
        ///   所以"窄窗口下 HUD 会不会盖住手牌"这一条**不算验过**（日志里写明）。
        /// </summary>
        private static void ForceCameraAspect(float aspect)
        {
            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            if (setup == null || setup.cam == null) { Debug.LogWarning("[AutoPlay/取景] 没有相机，压不了比例。"); return; }

            Camera cam = setup.cam;
            cam.aspect = aspect;

            float panel = (float)Screen.width / Mathf.Max(1, Screen.height);
            float rw, rh;
            if (aspect <= panel) { rh = 1f; rw = aspect / panel; }
            else                 { rw = 1f; rh = panel / aspect; }

            cam.rect = new Rect((1f - rw) * 0.5f, (1f - rh) * 0.5f, rw, rh);

            Debug.Log("[AutoPlay/取景] 兜底：只压相机比例 —— 宽高比 " + aspect.ToString("0.000")
                      + "，3D 视口 " + (Screen.width * rw).ToString("0") + "×" + (Screen.height * rh).ToString("0")
                      + "（HUD 仍按整块 " + Screen.width + "×" + Screen.height + " 排，这一条不算验过）");
        }

        /// <summary>把 <see cref="ForceCameraAspect"/> 压上去的东西还原（相机比例 + 视口矩形）。</summary>
        private static void ClearCameraAspect()
        {
            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            if (setup == null || setup.cam == null) return;

            setup.cam.ResetAspect();
            setup.cam.rect = new Rect(0f, 0f, 1f, 1f);
            Debug.Log("[AutoPlay/取景] 相机比例已还原（宽高比 " + setup.cam.aspect.ToString("0.000") + "）");
        }

        /// <summary>手牌那一排现在有几张（不含已经放进槽里的）—— 状态守卫用。</summary>
        private static int HandCardCount()
        {
            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            if (setup == null || setup.hand == null) return 0;

            int n = 0;
            for (int i = 0; i < setup.hand.Count; i++)
                if (setup.hand[i] != null && setup.hand[i].slotIndex < 0) n++;

            return n;
        }

        /// <summary>
        /// 取景探针要的那一屏：**v2.1 的"选刀片"**（手里 5 张 = 手牌最宽的一档）。
        ///
        /// 【为什么要能"修状态"】这条链验的是"手牌那一排装不装得下"，前提是手里真有 5 张。
        ///   而这一局的状态**可能被别人动过**：这台机器上同时跑着第二个 Unity 实例
        ///   （另一份副本、另一条探针），它的自动化脚本会去点"最前面那个 Unity 窗口"——
        ///   点到这边就等于替玩家把牌组确认掉、一路打到关卡结束。
        ///   实测踩过一次：探针还没开始跑，画面里这一关已经打完了、手里 0 张，
        ///   于是三张"验收图"拍的全是空手牌。
        ///   所以关键 stage 之前先**走游戏自己的入口**把状态摆回"选刀片"，
        ///   并把"修之前是什么阶段"打进日志 —— 再被点一下，日志里也看得出来。
        /// </summary>
        private static void ProbeEnsureBladePick()
        {
            TableTurnLoop loop = Loop();
            if (loop == null) { Debug.LogWarning("[AutoPlay/取景] 找不到 TableTurnLoop，状态没法摆。"); return; }

            TablePhase before = loop.phase;

            if (loop.IsTitle)                       loop.ConfirmTitleStart();
            if (loop.IsLevelSelect)                 PickLevel();
            if (loop.phase == TablePhase.DeckPick)  PickDeck();

            if (before != loop.phase)
            {
                string hand = loop.rulesV21 != null
                    ? (loop.rulesV21.hand.Count + " 素材 + " + loop.rulesV21.handSpells.Count + " 法术")
                    : "（规则侧不在）";

                Debug.Log("[AutoPlay/取景] 状态摆位：" + before + " → " + loop.phase + "，初始手牌 " + hand);
            }
            else if (loop.phase != TablePhase.BladePick)
            {
                // ★ 只在**真的动过阶段**时打日志：这台机器上局面可能被别的进程点到别处去了
                //   （比如已经在打牌了），那时这个守卫摆不动它 —— 每帧打一行会把日志刷爆
                //   （实测：一个 stage 打了几百行，真正要看的行全被冲没了）。
                Debug.LogWarning("[AutoPlay/取景] 现在不在「选刀片」那一屏（阶段 " + loop.phase
                                 + "），状态守卫摆不回去 —— 这一屏的取景图不作数。");
            }
        }

        /// <summary>切机位（走游戏自己的公开入口 CameraRig.GoTo）。</summary>
        private static void GoToView(string name)
        {            TableSetup setup = Object.FindObjectOfType<TableSetup>();
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

        // ══════════════════════════════════════════════════════════════
        //  桌面素材级联探针（DSH_LAYOUTPROBE=1）用的动作 —— 见 ㉔⓪~㉕⑥ 那一段
        //
        //  【为什么要"报告"而不是"截图 + 眼睛"】级联是故意重叠的布局：
        //    错开量小 1 厘米，被压住那张的名字就被切掉一半 —— 而在一张 1470 像素宽的
        //    游戏截图里，半行小字的差别根本看不出来。所以每一步都打一段
        //    TableRulesV21.TableLayoutReport：占地矩形、上缘露出多少、和五个障碍有没有
        //    交集，全是数字。报告里的判据和实机自检是**同一份实现**（CheckCascade），
        //    不存在"探针说没事、实机却报警"。
        // ══════════════════════════════════════════════════════════════

        /// <summary>把桌面素材级联的排布打一段进日志（位置 / 谁压住谁 / 障碍逐条判决）。</summary>
        private static void ProbeLayoutReport(string what)
        {
            TableTurnLoop loop = Loop();
            if (loop == null || loop.rulesV21 == null) return;

            Debug.Log("[AutoPlay/Layout] " + what + "\n" + loop.rulesV21.TableLayoutReport());
        }

        /// <summary>
        /// 级联算式的**离线扫描**：n = 1..maxCount 逐档算出坐标并验一遍
        /// （见 TableRulesV21.CascadeSweepReport）—— 不用真的凑出 8 张素材，
        /// 就能回答"以后张数变多会不会撞"。
        /// </summary>
        private static void ProbeCascadeSweep(int maxCount)
        {
            TableTurnLoop loop = Loop();
            if (loop == null || loop.rulesV21 == null) return;

            Debug.Log("[AutoPlay/Layout] " + loop.rulesV21.CascadeSweepReport(maxCount));
        }

        /// <summary>
        /// 诊断：把桌面卡（含刀片卡）身上那几个 TextMesh 的**渲染状态**打出来。
        ///
        /// 【为什么要它】级联的验收条件是"每张被压住的卡都还看得见**名字**"，
        ///   而"看不见"至少有三种完全不同的原因，截图里长得一模一样：
        ///     ① 被别的卡真挡住了（布局问题 —— 这一版要修的就是它）；
        ///     ② 被视锥剔掉了 / 材质队列不对（渲染问题，和布局无关）；
        ///     ③ 文字压根没建出来（数据问题）。
        ///   所以这里一次把 **世界坐标 / 屏幕坐标 / isVisible / shader / 渲染队列** 全打出来，
        ///   再拿"名字"和"属性"两行互相对照 —— 同在一张卡上、只差一个局部坐标，
        ///   一行可见一行不可见，原因当场就分得开（属性那行是能看见的，见截图）。
        /// </summary>
        private static void ProbeTextMeshDiag(string what)
        {
            TableTurnLoop loop = Loop();
            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            if (loop == null || loop.rulesV21 == null || setup == null || setup.cam == null) return;

            TableRulesV21 r = loop.rulesV21;
            Camera cam = setup.cam;

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("[AutoPlay/Diag] ").Append(what)
              .Append("｜相机 ").Append(cam.transform.position.ToString("0.###"))
              .Append("　朝向 ").Append(cam.transform.forward.ToString("0.###"));

            PlayCard[] all = Object.FindObjectsOfType<PlayCard>();
            for (int i = 0; i < all.Length; i++)
            {
                PlayCard pc = all[i];
                if (pc == null) continue;

                bool isBlade = loop.bladeCard == pc;
                if (!isBlade && r.FindTable(pc) == null) continue;

                TextMesh[] tms = pc.GetComponentsInChildren<TextMesh>(true);
                for (int k = 0; k < tms.Length; k++)
                {
                    TextMesh tm = tms[k];
                    if (tm == null) continue;

                    MeshRenderer mr = tm.GetComponent<MeshRenderer>();
                    Vector3 sp = cam.WorldToScreenPoint(tm.transform.position);

                    sb.Append("\n   ").Append(isBlade ? "刀片卡" : "桌面卡")
                      .Append("「").Append(pc.DisplayName).Append("」")
                      .Append(" 文本=\"").Append(tm.text).Append('"')
                      .Append(" 世界=").Append(tm.transform.position.ToString("0.####"))
                      .Append(" 屏幕=(").Append(sp.x.ToString("0")).Append(',')
                      .Append(sp.y.ToString("0")).Append(',').Append(sp.z.ToString("0.##")).Append(')')
                      .Append(" isVisible=").Append(mr != null ? mr.isVisible.ToString() : "无渲染器")
                      .Append(" 材质=").Append(mr != null && mr.sharedMaterial != null && mr.sharedMaterial.shader != null
                                                  ? mr.sharedMaterial.shader.name : "?")
                      .Append(" 队列=").Append(mr != null && mr.sharedMaterial != null
                                                  ? mr.sharedMaterial.renderQueue.ToString() : "?")
                      .Append(" enabled=").Append(mr != null ? mr.enabled.ToString() : "-");
                }
            }

            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// 把级联里第 <paramref name="index"/> 张（按 r.table 的顺序 = 上桌顺序）拖回手牌区。
        ///
        /// 【为什么按序号点名，而不是像 ProbeWithdrawTableMaterial 那样随便挑一张】
        ///   这一步要验的是"**抽掉中间那张**之后整列会不会自己收拢" ——
        ///   抽最后一张只是列变短，看不出收拢。
        ///
        /// 【走的是玩家那条路】Teleport 到手牌区 + TableInteraction.DropCard（松手判决），
        ///   探针只把"鼠标把牌拖到哪儿"换成"由探针指定"；判决函数一模一样。
        /// </summary>
        private static void ProbeWithdrawTableCardAt(int index, string what)
        {
            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            TableInteraction it = Object.FindObjectOfType<TableInteraction>();
            TableTurnLoop loop = Loop();

            if (setup == null || it == null || loop == null || loop.rulesV21 == null)
            {
                Debug.LogWarning("[AutoPlay/Layout] " + what + "：找不到 TableSetup / TableInteraction，跳过。");
                return;
            }

            TableRulesV21 r = loop.rulesV21;

            // 级联顺序 = r.table 里"还活着"的那些的顺序（SyncTableVisuals 就是照它摆的）
            GameJam.Rules.MaterialState st = null;
            int k = 0;
            for (int i = 0; i < r.table.Count; i++)
            {
                GameJam.Rules.MaterialState s = r.table[i];
                if (s == null || s.removed || !s.OnTable) continue;
                if (k == index) { st = s; break; }
                k++;
            }

            if (st == null)
            {
                Debug.LogWarning("[AutoPlay/Layout] " + what + "：桌面只有 " + r.LiveTableCount() +
                                 " 张素材（要抽第 " + (index + 1) + " 张），跳过。｜桌面 " + r.TableText());
                return;
            }

            // 状态 → 3D 卡：用规则层的 FindTable 认（不靠名字，名字会随 D 变）
            PlayCard target = null;
            PlayCard[] all = Object.FindObjectsOfType<PlayCard>();
            for (int i = 0; i < all.Length && target == null; i++)
            {
                if (all[i] == null) continue;
                if (r.FindTable(all[i]) == st) target = all[i];
            }

            if (target == null)
            {
                Debug.LogWarning("[AutoPlay/Layout] " + what + "：「" + st.name + "」没有对应的 3D 卡，跳过。");
                return;
            }

            int tableBefore = r.LiveTableCount();
            int handBefore  = setup.hand != null ? setup.hand.Count : -1;

            // 玩家的鼠标把这张卡拖到手牌那一片（比 HandZoneZ 再往玩家一侧 0.15，避免压线）
            target.Teleport(new Vector3(target.transform.position.x, 0.022f,
                                        TableInteraction.HandZoneZ - 0.15f),
                            target.homeEuler);
            bool handled = it.DropCard(target, false);

            Debug.Log("[AutoPlay/Layout] " + what + "：把级联里第 " + (index + 1) + " 张「" + st.name + "」"
                      + "拖回手牌区（z " + (TableInteraction.HandZoneZ - 0.15f).ToString("0.##") + "）→ 被处理 " + handled
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

        // ══════════════════════════════════════════════════════════════
        //  存档 / 读档探针（DSH_SAVEPROBE=1）用的动作 —— 见 ㉞⓪~㉟⑤ 那一段
        //
        //  【它们只做两件事】把"现在的数字"打进日志，和**按玩家那一下的同一个入口**做事
        //    （保存 = TableTurnLoop.SaveGame，读档 = TableTitleRig.ActivateContinue）。
        //    探针不自己去写文件、也不自己造状态 —— 那样测到的就不是玩家那条路。
        // ══════════════════════════════════════════════════════════════

        /// <summary>存档前那一行数字（读档后要和它逐条对照）。</summary>
        private static string saveProbeBeforeNumbers = "（还没存过）";

        /// <summary>好档的原文备份 —— 反例要把它改坏，验完再写回去。</summary>
        private static string saveProbeBackupText = "";

        /// <summary>状态守卫重试了几次（防止"局面一直被别的进程点掉"时无限重开）。</summary>
        private static int saveProbeGuardRetries;

        /// <summary>
        /// 状态守卫：确认局面在"第 1 回合、什么都没动"，不对就**走游戏自己的入口重开一局**。
        ///
        /// 【为什么需要它】这台机器上同时跑着别的 Unity 实例，它的自动化脚本会点"最前面那个窗口" ——
        ///   点到这边就等于替玩家把牌打出去了（取景探针踩过：走到第 1 回合时手里 0 张，
        ///   于是那一屏的验收图全是空手牌）。
        ///
        /// ★ 日志**只在真的动过状态时**打一行：每帧都打会把要看的行冲没
        ///   （别人踩过这个坑：一个 stage 打了几百行，真正要看的行全被冲走了）。
        /// ★ 最多重开两次：只有一次机会的话，"一直被点"会让探针卡在这一屏来回跑。
        /// </summary>
        private static bool ProbeSaveGuard(string who)
        {
            TableTurnLoop loop = Loop();
            if (loop == null || loop.rulesV21 == null)
            {
                Debug.LogWarning("[AutoPlay/存档] " + who + "：找不到 TableTurnLoop / 规则侧，这条链跳过。");
                return false;
            }

            TableRulesV21 r = loop.rulesV21;

            bool ok = loop.phase == TablePhase.Select && !r.levelOver && r.turnIndex == 1
                      && r.actionPoints == GameJam.Rules.LevelRun.ActionPointsPerTurn
                      && HandCardCount() > 0;

            if (ok) return true;

            if (saveProbeGuardRetries >= 2)
            {
                Debug.LogWarning("[AutoPlay/存档] " + who + "：连着 " + saveProbeGuardRetries +
                                 " 次都没能把局面摆回第 1 回合（阶段 " + loop.phase + "、3D 手牌 " +
                                 HandCardCount() + " 张）→ 不再重开，后面的数字只能当作废。");
                return true;
            }

            saveProbeGuardRetries++;

            Debug.LogWarning("[AutoPlay/存档] " + who + "：局面已经被动过（阶段 " + loop.phase +
                             "、回合 " + r.turnIndex + "、行动机会 " + r.actionPoints +
                             "、3D 手牌 " + HandCardCount() + " 张）→ 重开一局再走到这一屏。");

            loop.Begin();                 // 回开场
            ProbeEnsureBladePick();       // 点书 → 选关 → 选牌组，停在「选刀片」（它自己会打"摆位"日志）
            return false;
        }

        /// <summary>给刀片补层数（探针调味）：热 2 衰退 + **热 1 不衰退** + 催化 1。</summary>
        private static void ProbeSaveAddLayers()
        {
            TableTurnLoop loop = Loop();
            if (loop == null || loop.rulesV21 == null) return;

            GameJam.Rules.BladeState b = loop.rulesV21.blade;
            if (b == null) return;

            string before = b.layers.Describe();

            // ★ 特意带上一条"不衰退"的层：存档里那两份计数**必须分开写**
            //   （只存总数的话，读回来要么永远不掉、要么下次回合结束就掉光）
            b.layers.Add(GameJam.Rules.LayerKind.Heat, 2);
            b.layers.Add(GameJam.Rules.LayerKind.Heat, 1, true);
            b.layers.Add(GameJam.Rules.LayerKind.Catalyst, 1);

            Debug.Log("[AutoPlay/存档] 探针调味：刀片附魔 " + before + " → " + b.layers.Describe() +
                      "（其中热×1 是**不衰退**的 —— 存档要能把它单独存回来）");
        }

        /// <summary>存档前的手牌检查：读档后的"拖法术进附魔位"要用还留在手里的那张法术。</summary>
        private static void ProbeSaveHandCheckpoint(string what)
        {
            TableTurnLoop loop = Loop();
            if (loop == null || loop.rulesV21 == null) return;

            TableRulesV21 r = loop.rulesV21;
            Debug.Log("[AutoPlay/存档] " + what + "：素材 " + r.hand.Count + " 张｜法术 " + r.handSpells.Count +
                      " 张｜" + r.HandText() +
                      "｜桌面 " + r.LiveTableCount() + " 张：" + r.TableText());

            if (r.handSpells.Count == 0)
                Debug.LogWarning("[AutoPlay/存档] " + what + "：手里一张法术都没有 —— " +
                                 "读档后那一项回归（拖法术进附魔位）会测不到，如实记下。");
        }

        /// <summary>"存档要比对的那几个数字"——一行（存档前 / 读档后各打一次）。</summary>
        private static string ProbeSaveNumbers()
        {
            TableTurnLoop loop = Loop();
            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            if (loop == null || loop.rulesV21 == null) return "（规则侧不在）";

            TableRulesV21 r = loop.rulesV21;
            GameJam.Rules.BladeState b = r.blade;

            return "回合 " + r.turnIndex + "/" + GameJam.Rules.LevelRun.TurnsPerLevel
                 + "｜行动机会 " + r.actionPoints + "/" + GameJam.Rules.LevelRun.ActionPointsPerTurn
                 + "｜分数 " + r.score + "/" + r.targetScore
                 + "｜刀片 " + (b != null ? b.name : "?") + " H" + (b != null ? b.H : 0) + " V" + (b != null ? b.V : 0)
                 + "｜附魔 " + (b != null ? b.layers.Describe() : "（无）")
                 + "｜桌面 " + r.LiveTableCount() + " 张"
                 + "｜手牌 " + r.hand.Count + " 素材 + " + r.handSpells.Count + " 法术"
                 + "｜被动 " + r.BladePassiveCount + " 条"
                 + "｜空白卡 " + r.blankCount
                 + "｜3D 手牌 " + (setup != null && setup.hand != null ? setup.hand.Count : -1) + " 张";
        }

        private static void ProbeSaveLogNumbers(string what)
        {
            Debug.Log("[AutoPlay/存档] " + what + "：" + ProbeSaveNumbers());
        }

        /// <summary>存档：走 TableTurnLoop.SaveGame —— 就是暂停菜单「保　存」按钮按下去那件事。</summary>
        private static void ProbeSaveNow()
        {
            TableTurnLoop loop = Loop();
            if (loop == null) { Debug.LogWarning("[AutoPlay/存档] 找不到 TableTurnLoop，存档跳过。"); return; }

            saveProbeBeforeNumbers = ProbeSaveNumbers();
            Debug.Log("[AutoPlay/存档] 存档前 " + saveProbeBeforeNumbers);

            bool ok = loop.SaveGame();

            string size = "（文件不在）";
            try
            {
                System.IO.FileInfo fi = new System.IO.FileInfo(TableTurnLoop.SavePath);
                if (fi.Exists) size = fi.Length + " 字节";
            }
            catch (System.Exception) { }

            Debug.Log("[AutoPlay/存档] 保存" + (ok ? "成功" : "★ 失败") +
                      "｜文件 " + TableTurnLoop.SavePath + "（" + size + "）｜notice " + loop.notice);
        }

        /// <summary>把开场那块「继　续」的亮/灰状态打进日志（用户口径：没档 / 坏档必须是灰的）。</summary>
        private static void ProbeSaveLogContinueState(string what)
        {
            TableTurnLoop loop = Loop();
            TableTitleRig rig = Object.FindObjectOfType<TableTitleRig>();
            if (rig == null)
            {
                Debug.LogWarning("[AutoPlay/存档] 找不到 TableTitleRig，" + what + " 的牌子状态记不了。");
                return;
            }

            bool fileThere = false;
            try { fileThere = System.IO.File.Exists(TableTurnLoop.SavePath); } catch (System.Exception) { }

            Debug.Log("[AutoPlay/存档] " + what + "：「继　续」" + (rig.ContinueEnabled ? "亮着（能读）" : "灰着（读不了）") +
                      "｜提示「" + rig.ContinueHint + "」｜存档文件在不在 " + fileThere +
                      "｜阶段 " + (loop != null ? loop.phase.ToString() : "?"));
        }

        /// <summary>反例：把存档文件**改坏**（先备份好档原文，验完再写回去）。</summary>
        private static void ProbeSaveCorruptFile()
        {
            string path = TableTurnLoop.SavePath;
            try
            {
                saveProbeBackupText = System.IO.File.ReadAllText(path);
                System.IO.File.WriteAllText(path, "{ \"version\": 1, \"kind\": \"v21-table-save\", \"state\": { ",
                                            new System.Text.UTF8Encoding(false));

                Debug.LogWarning("[AutoPlay/存档] 反例：存档已被**改坏**（截断成半截 JSON）—— " +
                                 "好档原文 " + saveProbeBackupText.Length + " 字符已备份，验完写回去。｜" + path);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[AutoPlay/存档] 反例：改坏存档失败 —— " + e.GetType().Name + " " + e.Message);
            }
        }

        /// <summary>把备份的好档写回去（反例验完之后）。</summary>
        private static void ProbeSaveRestoreFile()
        {
            if (string.IsNullOrEmpty(saveProbeBackupText))
            {
                Debug.LogWarning("[AutoPlay/存档] 没有备份文本可恢复 —— 存档保持在坏的状态。");
                return;
            }

            try
            {
                System.IO.File.WriteAllText(TableTurnLoop.SavePath, saveProbeBackupText,
                                            new System.Text.UTF8Encoding(false));
                Debug.Log("[AutoPlay/存档] 好档已写回（" + saveProbeBackupText.Length + " 字符）｜" + TableTurnLoop.SavePath);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[AutoPlay/存档] 写回好档失败 —— " + e.GetType().Name + " " + e.Message);
            }
        }

        /// <summary>让开场重新判定一次"这一档能不能读"（= 玩家回一趟开场时发生的事）。</summary>
        private static void ProbeSaveRebuildTitle(string why)
        {
            TableTitleRig rig = Object.FindObjectOfType<TableTitleRig>();
            if (rig == null)
            {
                Debug.LogWarning("[AutoPlay/存档] 找不到 TableTitleRig，" + why + " 判定不了。");
                return;
            }

            rig.Build();
            ProbeSaveLogContinueState(why);
        }

        /// <summary>按一下「继　续」（走 ActivateContinue = 玩家点那块木牌的同一条路）。</summary>
        private static void ProbeSavePressContinue(string what)
        {
            TableTitleRig rig = Object.FindObjectOfType<TableTitleRig>();
            TableTurnLoop loop = Loop();
            if (rig == null || loop == null)
            {
                Debug.LogWarning("[AutoPlay/存档] 找不到 TableTitleRig / TableTurnLoop，" + what + " 跳过。");
                return;
            }

            string before = loop.phase.ToString();
            rig.ActivateContinue();

            Debug.Log("[AutoPlay/存档] " + what + "：阶段 " + before + " → " + loop.phase +
                      "｜notice " + loop.notice + "｜" + loop.StateSummary());
        }

        /// <summary>
        /// 反例：坏档时**直接走读档入口**（模拟"牌子还亮着时点下去"这一下）。
        /// 判据三条：返回 false、阶段没变、两边的数字一个都没变。
        /// </summary>
        private static void ProbeSaveLoadRejected()
        {
            TableTurnLoop loop = Loop();
            if (loop == null) { Debug.LogWarning("[AutoPlay/存档] 找不到 TableTurnLoop，坏档用例跳过。"); return; }

            string before     = ProbeSaveNumbers();
            string phaseBefore = loop.phase.ToString();
            int tableBefore   = loop.rulesV21 != null ? loop.rulesV21.LiveTableCount() : -1;
            int handBefore    = loop.rulesV21 != null ? (loop.rulesV21.hand.Count + loop.rulesV21.handSpells.Count) : -1;

            bool ok = loop.ContinueFromSave();

            int tableAfter = loop.rulesV21 != null ? loop.rulesV21.LiveTableCount() : -1;
            int handAfter  = loop.rulesV21 != null ? (loop.rulesV21.hand.Count + loop.rulesV21.handSpells.Count) : -1;
            string after   = ProbeSaveNumbers();
            bool untouched = (before == after) && (tableBefore == tableAfter) && (handBefore == handAfter);

            Debug.LogWarning("[AutoPlay/存档] ㉟⑤ 坏档直接走读档入口：返回 " + ok + "（期望 false）" +
                             "｜阶段 " + phaseBefore + " → " + loop.phase + "（期望还停在开场）" +
                             "｜桌面 " + tableBefore + " → " + tableAfter + " 张｜手牌 " + handBefore + " → " + handAfter +
                             " 张｜局面一个字节都没动 = " + untouched +
                             "｜notice " + loop.notice +
                             "\n   拒绝前 " + before + "\n   拒绝后 " + after);

            // 读档失败之后，开场那块牌子应当回到灰的（用户口径第二条）
            ProbeSaveRebuildTitle("㉟⑤ 坏档被拒之后");
        }

        /// <summary>读档后逐条对照：存档前那一行 vs 现在这一行。</summary>
        private static void ProbeSaveCompareNumbers(string what)
        {
            string now = ProbeSaveNumbers();
            bool same = (now == saveProbeBeforeNumbers);

            Debug.Log("[AutoPlay/存档] " + what + "：" +
                      "\n   存档前 " + saveProbeBeforeNumbers +
                      "\n   " + (same ? "✓ 读档后 " : "★ 读档后 ") + now +
                      "\n   逐条一致 = " + same + "（回合/行动机会/分数/刀片 H·V/附魔层数/桌面/手牌/被动/空白卡/3D 手牌）");
        }

        /// <summary>
        /// 冷启动读档那一段能不能跑：停在开场 + 磁盘上有一份**能读**的存档。
        ///
        /// 【它专治哪个洞】打包版验收报的"读档后卡表解析报告没建出来"——
        ///   只有"开机什么都没进、直接按「继续」"这条路才复现（同一会话里先进过一关的话，
        ///   报告早在 BeginLevel 里建好了）。所以这一段的前提必须如实打出来。
        /// </summary>
        private static bool ProbeColdLoadAvailable()
        {
            TableTurnLoop loop = Loop();
            if (loop == null || loop.rulesV21 == null) return false;
            if (loop.phase != TablePhase.Title) return false;

            GameJam.Rules.SaveFileDto file;
            string error;
            if (!TableSaveIO.TryLoad(out file, out error)) return false;

            // 用和「继续」完全相同的两道门（能解析 + 状态建得出来），但**不打它那几行日志** ——
            // 免得和 TableTitleRig.CanContinueFromSave 各打一遍（那次是 Build 时打的）
            BuiltSaveState built;
            if (!loop.rulesV21.TryBuildSaveState(file.state, out built, out error)) return false;

            Debug.Log("[AutoPlay/存档] 冷启动读档可用：" + file.ShortLine() +
                      "｜此刻卡表解析报告 = " + (loop.rulesV21.report == null
                          ? "★ 还没建出来（正是要验的那种情形）" : "已建出（这一段只求走通）"));

            return true;
        }

        /// <summary>
        /// 卡表解析报告现在是什么状态 —— 打包版那个洞要盯的就是它。
        /// `UnrecognizedCount` 返回 -1 = **报告没建出来**（顶栏会写"无法确认哪些规则没实现"、F2 一片空白）。
        /// </summary>
        private static void ProbeReportState(string what)
        {
            TableTurnLoop loop = Loop();
            if (loop == null || loop.rulesV21 == null)
            {
                Debug.LogWarning("[AutoPlay/存档] " + what + "：找不到规则侧，报告状态记不了。");
                return;
            }

            TableRulesV21 r = loop.rulesV21;
            int n = r.UnrecognizedCount;

            if (n < 0)
                Debug.LogWarning("[AutoPlay/存档] " + what +
                                 "：★ 卡表解析报告没建出来（顶栏会写「无法确认哪些规则没实现」，F2 也是空的）");
            else
                Debug.Log("[AutoPlay/存档] " + what + "：报告已建出，未识别 " + n + " 条｜" +
                          (r.report != null ? r.report.SummaryLine() : "（report 对象为空？）"));
        }

        /// <summary>暂停菜单里那行"存档位：…"（反例②要拍的就是它：原因写在玩家看得见的地方）。</summary>
        private static void ProbeSaveLogStatusLine(string what)
        {
            TableTurnLoop loop = Loop();
            Debug.LogWarning("[AutoPlay/存档] " + what + "：" + TableSaveIO.StatusLine() +
                             "｜阶段 " + (loop != null ? loop.phase.ToString() : "?"));
        }

        // ══════════════════════════════════════════════════════════════
        //  献祭吞噬取证探针（DSH_SACPROBE=1）用的动作 —— 见 ㊲⑧~㊳⑨ 那一段
        //
        //  【它们只做两件事】把"现在的数字"打进日志，和**按玩家那一下的同一个入口**做事
        //    （点手牌 = TableInteraction.ClickCard → DropCard → PlayCardV21；
        //      点桌面素材 = TableInteraction.ClickCard → OnTableCardClicked → Select；
        //      启动 = TableTurnLoop.ActivateJuicer；结束回合 = TableTurnLoop.EndRoundOrLevel）。
        //    探针不自己改状态 —— 那样测到的就不是玩家那条路了。
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 连拍"飞行中间帧"专用的截图：**绕开 ShotSettle**（1.6 秒的防串帧间隔）。
        ///
        /// 【为什么必须绕】被吞噬的卡只飞 0.55 秒（ConsumeInto 的 lifeSeconds），
        ///   而 ShotSettle 是 1.6 秒 —— 按 Shot() 的节奏，第一张就已经飞完自毁了。
        ///   所以这里不排队、直接拍，靠 stage 之间的 0.2 秒间隔落在飞行中间。
        ///   仍然是**不同帧**各拍一张，所以没有踩"同一帧连拍两次只有最后一次生效"那个坑
        ///   （文件头那条教训说的是同一帧里连调两次）。
        /// 路径照样记进 shotPaths —— 退出前 Finish() 会确认它们真的落盘。
        /// </summary>
        private static void SacShotNow(string name)
        {
            string path = Path.Combine(outDir, name);
            ScreenCapture.CaptureScreenshot(path);
            shotPaths.Add(path);

            Debug.Log("[AutoPlay/献祭] 连拍 " + path + "（" + Screen.width + "×" + Screen.height +
                      "，绕开 ShotSettle —— 飞行只有 0.55 秒）");
        }

        /// <summary>状态守卫重试了几次（防止"局面一直被别的进程点掉"时无限重开）。</summary>
        private static int sacGuardRetries;

        /// <summary>
        /// 状态守卫：局面不对就**走游戏自己的入口重开一局**。
        ///
        /// 【为什么需要它】这台机器上同时跑着别的 Unity 实例，它的自动化脚本会点
        ///   "最前面那个窗口" —— 点到这边就等于替玩家把牌打出去了（取景探针踩过：
        ///   走到第 1 回合时手里 0 张）。这条链对状态特别敏感（刀片 H 只剩个位数、
        ///   手牌必须是空的），被点一下前提就没了。
        ///   ★ 日志**只在真的动过状态时**打一行：每帧都打会把要看的行冲没。
        ///   ★ 最多重开 3 次：只有一次机会的话，"一直被点"会让探针卡在这一屏来回跑。
        /// </summary>
        private static bool ProbeSacGuardAt(string who, int expectTurn, int expectTable, int expectHand)
        {
            TableTurnLoop loop = Loop();
            if (loop == null || loop.rulesV21 == null)
            {
                Debug.LogWarning("[AutoPlay/献祭] " + who + "：找不到 TableTurnLoop / 规则侧，这条链跳过。");
                return false;
            }

            TableRulesV21 r = loop.rulesV21;

            bool ok = loop.phase == TablePhase.Select && !r.levelOver && !r.bursted
                      && (expectTurn < 0  || r.turnIndex == expectTurn)
                      && (expectTable < 0 || r.LiveTableCount() == expectTable)
                      && (expectHand < 0  || HandCardCount() == expectHand);

            if (ok) return true;

            if (sacGuardRetries >= 3)
            {
                Debug.LogWarning("[AutoPlay/献祭] " + who + "：连着 " + sacGuardRetries +
                                 " 次都没能把局面摆回预期（阶段 " + loop.phase + "、回合 " + r.turnIndex +
                                 "、桌面 " + r.LiveTableCount() + " 张、3D 手牌 " + HandCardCount() +
                                 " 张）→ 不再重开，下面照跑、结果只能当作废。");
                return true;
            }

            sacGuardRetries++;

            Debug.LogWarning("[AutoPlay/献祭] " + who + "：局面已经被动过（阶段 " + loop.phase +
                             "、回合 " + r.turnIndex + "、行动机会 " + r.actionPoints +
                             "、桌面 " + r.LiveTableCount() + " 张、3D 手牌 " + HandCardCount() +
                             " 张、关卡结束 " + r.levelOver + "、爆刀 " + r.bursted +
                             "）→ 重开一局重新走这条链。");

            loop.Begin();                 // 回开场
            ProbeEnsureBladePick();       // 点书 → 选关 → 选牌组，停在「选刀片」（它自己会打"摆位"日志）
            return false;
        }

        /// <summary>
        /// 这一局的前提对不对（牌组 0「硫硝爆燃」+ 刀片核心 = 硝石 H12）。
        /// 不对**只报警不重开** —— 重开也会得到同一副牌，改变不了什么，但日志里必须留痕：
        /// 刀片 H 不够时这条链会先爆刀，那时"没抓到吞噬"就不是规则的问题。
        /// </summary>
        private static void ProbeSacSetupCheck()
        {
            TableTurnLoop loop = Loop();
            if (loop == null || loop.rulesV21 == null) return;

            TableRulesV21 r = loop.rulesV21;
            GameJam.Rules.BladeState b = r.blade;

            bool coreOk = b != null && !string.IsNullOrEmpty(b.name) && b.name.Contains("硝石");

            Debug.Log("[AutoPlay/献祭] 这一局的前提（期望：牌组 0「硫硝爆燃」、核心 = 硝石 H12 V1）："
                      + "刀片 " + (b != null ? b.name : "（无）")
                      + " H" + (b != null ? b.H : 0) + " V" + (b != null ? b.V : 0)
                      + "（第 ⑤ 步会把被吞噬那张卡的 H/V 加进来）"
                      + "｜起始手牌 " + r.HandText()
                      + (coreOk ? "｜✓ 核心是硝石（H=12：够这条链的 5 次启动不爆刀）"
                                : "｜★ 核心不是硝石 —— H 预算可能不够（一爆刀这条链就断），下面照跑、如实记录"));
        }

        /// <summary>
        /// "要看的数字"一行打完（回合 / 行动机会 / 分数 / 刀片 H·V / 附魔 / 桌面 / 手牌 / 目标 / 3D 手牌）。
        /// 每一张截图旁边都配一行它 —— 图和数字必须能互相对上。
        /// </summary>
        private static string ProbeSacNumbers()
        {
            TableTurnLoop loop = Loop();
            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            if (loop == null || loop.rulesV21 == null) return "（规则侧不在）";

            TableRulesV21 r = loop.rulesV21;
            GameJam.Rules.BladeState b = r.blade;

            return "回合 " + r.turnIndex + "/" + GameJam.Rules.LevelRun.TurnsPerLevel
                 + "｜行动机会 " + r.actionPoints + "/" + GameJam.Rules.LevelRun.ActionPointsPerTurn
                 + "｜分数 " + r.score + "/" + r.targetScore
                 + "｜刀片 " + (b != null ? b.name : "（无）")
                 + " H" + (b != null ? b.H : 0) + " V" + (b != null ? b.V : 0)
                 + "｜附魔 " + (b != null ? b.layers.Describe() : "（无）")
                 + "｜桌面素材 " + r.LiveTableCount() + " 张：" + r.TableText()
                 + "｜手牌 " + r.hand.Count + " 素材 + " + r.handSpells.Count + " 法术（3D " +
                   (setup != null && setup.hand != null ? setup.hand.Count : -1) + " 张）"
                 + "｜启动目标 " + (r.selected != null ? r.selected.Describe() : "（无）")
                 + "｜阶段 " + loop.phase
                 + (r.levelOver ? "｜★ 关卡已结束（" + r.endReason + "）" : "");
        }

        /// <summary>
        /// 日志里用的卡名（把换行压成一行）。
        ///
        /// 【为什么必须压】法术卡的 DisplayName 是**卡面正文**拼出来的、里面带换行
        ///   （实测"火焰"那张的名字是 `火焰\n附魔到刀片，可叠加。`）——
        ///   直接拼进日志会把"世界坐标/离家多远/缩放"那一行劈成两行，
        ///   验收的人看到的就是"「火焰"后面什么都没有（第一版飞行动画那两行就是这么断的）。
        /// </summary>
        private static string OneLineCardName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "（无名）";
            return s.Replace("\r", " ").Replace("\n", " ").Trim();
        }

        /// <summary>
        /// 正在飞向罐口的卡（<see cref="PlayCard.IsConsuming"/>）：卡名 / 世界坐标 / 离罐口还有多远 / 缩放。
        ///
        /// 【为什么它必须是数字】"这张卡在飞"和"这张卡被直接删了"在截图上长得一模一样
        ///   （都是一张缩小、位置偏了的卡），而这一次要证的恰恰是"飞行动画真的在跑、
        ///   并且是自己飞完才消失"。把离罐口的距离量出来，两帧一比就知道它在路上。
        /// </summary>
        private static string ProbeSacFlightText()
        {
            JuicerRig juicer = Object.FindObjectOfType<JuicerRig>();
            Vector3 mouth = juicer != null ? juicer.MouthWorld : Vector3.zero;

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("飞行动画（PlayCard.ConsumeInto）：罐口 (")
              .Append(mouth.x.ToString("0.###")).Append(", ")
              .Append(mouth.y.ToString("0.###")).Append(", ")
              .Append(mouth.z.ToString("0.###")).Append(")");

            int flying = 0;
            PlayCard[] all = Object.FindObjectsOfType<PlayCard>();
            for (int i = 0; i < all.Length; i++)
            {
                PlayCard pc = all[i];
                if (pc == null || !pc.IsConsuming) continue;

                flying++;
                Vector3 p = pc.transform.position;

                sb.Append("\n   · 「").Append(OneLineCardName(pc.DisplayName)).Append("」世界 (")
                  .Append(p.x.ToString("0.###")).Append(", ")
                  .Append(p.y.ToString("0.###")).Append(", ")
                  .Append(p.z.ToString("0.###")).Append(")｜离罐口 ")
                  .Append(Vector3.Distance(p, mouth).ToString("0.###"))
                  .Append("｜缩放 ").Append(pc.transform.localScale.x.ToString("0.###"));
            }

            sb.Append(flying == 0
                ? "｜正在飞的卡：0 张（要么还没被吸走、要么已经飞完自毁了）"
                : "｜正在飞的卡：" + flying + " 张");

            return sb.ToString();
        }

        /// <summary>点手牌里的第一张素材上桌（走玩家那条 TableInteraction.ClickCard）。</summary>
        private static void ProbeSacPlayFirstMaterial(string what)
        {
            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            TableInteraction it = Object.FindObjectOfType<TableInteraction>();
            TableTurnLoop loop = Loop();

            if (setup == null || it == null || loop == null || loop.rulesV21 == null)
            {
                Debug.LogWarning("[AutoPlay/献祭] " + what + "：找不到 TableSetup / TableInteraction，跳过。");
                return;
            }

            PlayCard card = FirstHandMaterial(setup);
            if (card == null)
            {
                Debug.LogWarning("[AutoPlay/献祭] " + what + "：手牌里没有素材（" + loop.rulesV21.HandText() + "），跳过。");
                return;
            }

            int before = loop.rulesV21.LiveTableCount();
            bool handled = it.ClickCard(card);

            Debug.Log("[AutoPlay/献祭] ★" + what + "：「" + card.DisplayName + "」被处理 " + handled
                      + "｜桌面素材 " + before + " → " + loop.rulesV21.LiveTableCount() + " 张"
                      + "｜notice " + loop.notice
                      + "\n   " + ProbeSacNumbers());
        }

        /// <summary>点手牌里的法术（= 附魔到刀片，和拖进附魔位等价）。</summary>
        private static void ProbeSacPlaySpell()
        {
            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            TableInteraction it = Object.FindObjectOfType<TableInteraction>();
            TableTurnLoop loop = Loop();

            if (setup == null || it == null || loop == null || loop.rulesV21 == null)
            {
                Debug.LogWarning("[AutoPlay/献祭] 点手牌法术：找不到 TableSetup / TableInteraction，跳过。");
                return;
            }

            PlayCard spell = FindHandSpell(setup);
            if (spell == null)
            {
                Debug.LogWarning("[AutoPlay/献祭] 点手牌法术：手里没有法术卡，跳过。");
                return;
            }

            string before = loop.rulesV21.blade != null ? loop.rulesV21.blade.layers.Describe() : "（无刀片）";
            bool handled = it.ClickCard(spell);

            Debug.Log("[AutoPlay/献祭] ★点手牌法术：「" + spell.DisplayName + "」被处理 " + handled
                      + "｜附魔 " + before + " → "
                      + (loop.rulesV21.blade != null ? loop.rulesV21.blade.layers.Describe() : "（无刀片）")
                      + "｜notice " + loop.notice
                      + "\n   " + ProbeSacNumbers());
        }

        /// <summary>
        /// 点桌面上某张素材（按素材名包含匹配）= 把它设为启动目标。
        /// 走玩家那条 TableInteraction.ClickCard → RouteCardClick → OnTableCardClicked。
        /// </summary>
        private static bool ProbeSacSelectTable(string namePart)
        {
            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            TableInteraction it = Object.FindObjectOfType<TableInteraction>();
            TableTurnLoop loop = Loop();

            if (setup == null || it == null || loop == null || loop.rulesV21 == null)
            {
                Debug.LogWarning("[AutoPlay/献祭] 点桌面素材：找不到 TableSetup / TableInteraction，跳过。");
                return false;
            }

            TableRulesV21 r = loop.rulesV21;

            PlayCard pick = null;
            GameJam.Rules.MaterialState pickState = null;

            // 桌面上的 3D 卡：用规则层的 FindTable 认（不靠卡面名字，名字里带 H/D/V）
            PlayCard[] all = Object.FindObjectsOfType<PlayCard>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;

                GameJam.Rules.MaterialState st = r.FindTable(all[i]);
                if (st == null || st.removed || !st.OnTable) continue;

                if (string.IsNullOrEmpty(namePart) || (st.name != null && st.name.Contains(namePart)))
                {
                    pick = all[i];
                    pickState = st;
                    break;
                }
            }

            if (pick == null)
            {
                Debug.LogWarning("[AutoPlay/献祭] 点桌面素材：桌面上没有「" + namePart + "」（现在桌面 "
                                 + r.LiveTableCount() + " 张：" + r.TableText() + "），跳过。");
                return false;
            }

            bool handled = it.ClickCard(pick);

            Debug.Log("[AutoPlay/献祭] ★点桌面素材：「" + pick.DisplayName + "」（" + pickState.Describe() + "）被处理 "
                      + handled + "｜启动目标 " + (r.selected != null ? r.selected.Describe() : "（无）")
                      + "｜notice " + loop.notice);
            return handled;
        }

        /// <summary>
        /// 启动破壁机（玩家入口 <see cref="TableTurnLoop.ActivateJuicer"/>，和 HUD 上那个按钮同一个）。
        ///
        /// 【为什么要"启动前 / 启动后 / 飞行动画"三行】
        ///   引擎自己那几行（【启动】… ⑤ 献祭吞噬判定…）是判定本身；
        ///   这里这三行是**同一件事在桌面这一侧的数字**：刀片 H/V 各变了多少、
        ///   桌面少了几张、有没有卡真的在飞。两边对得上，才叫"图和日志互证"。
        /// </summary>
        private static void ProbeSacActivate(string what)
        {
            TableTurnLoop loop = Loop();
            if (loop == null || loop.rulesV21 == null)
            {
                Debug.LogWarning("[AutoPlay/献祭] " + what + "：找不到 TableTurnLoop / 规则侧，跳过。");
                return;
            }

            TableRulesV21 r = loop.rulesV21;

            int hBefore = r.blade != null ? r.blade.H : 0;
            int vBefore = r.blade != null ? r.blade.V : 0;
            int tableBefore = r.LiveTableCount();

            Debug.Log("[AutoPlay/献祭] ▶ " + what + "｜启动前：" + ProbeSacNumbers());

            loop.ActivateJuicer();

            int hAfter = r.blade != null ? r.blade.H : 0;
            int vAfter = r.blade != null ? r.blade.V : 0;

            Debug.Log("[AutoPlay/献祭] ▶ " + what + "｜启动后：刀片 H " + hBefore + " → " + hAfter +
                      "（Δ" + (hAfter - hBefore) + "）｜刀片 V " + vBefore + " → " + vAfter +
                      "（Δ" + (vAfter - vBefore) + "）｜桌面素材 " + tableBefore + " → " + r.LiveTableCount() + " 张"
                      + "｜notice " + loop.notice);
            Debug.Log("[AutoPlay/献祭] ▶ " + what + "｜启动后：" + ProbeSacNumbers());
            Debug.Log("[AutoPlay/献祭] ▶ " + what + "｜" + ProbeSacFlightText());
        }

        /// <summary>结束本回合（走 TableTurnLoop.EndRoundOrLevel），把新回合的数字打出来。</summary>
        private static void ProbeSacEndRound()
        {
            TableTurnLoop loop = Loop();
            if (loop == null || loop.rulesV21 == null) return;

            TableRulesV21 r = loop.rulesV21;
            int turnBefore = r.turnIndex;
            int apBefore = r.actionPoints;
            string tableBefore = r.TableText();

            loop.EndRoundOrLevel();

            Debug.Log("[AutoPlay/献祭] ★结束回合：回合 " + turnBefore + " → " + r.turnIndex
                      + "｜行动机会 " + apBefore + " → " + r.actionPoints
                      + "（新回合应重置为 " + GameJam.Rules.LevelRun.ActionPointsPerTurn + "）"
                      + "｜桌面素材保留：" + tableBefore + " → " + r.TableText()
                      + "（D 不随回合恢复，这正是反例①的前提）"
                      + "｜阶段 " + loop.phase
                      + "\n   " + ProbeSacNumbers());
        }

        /// <summary>
        /// 点名选刀片核心（走玩家那条 TableTurnLoop.SwapBladeWith → PickCoreCandidate）。
        ///
        /// 【为什么要能点名】主链那一步（ProbeCorePickV21）点的是"手牌第一张"，
        ///   而这条链的 H 预算要求核心的 H 尽可能大：牌组 0 里 H 最大的是**硝石 H12**
        ///   （外星合金/硫磺 H4、水 H2）。核心 H4 时第 4 次启动就爆刀 —— 那正是
        ///   "以前从来没抓到吞噬"的原因，所以这里必须点得动它。
        ///   找不到点名的那张就退回"H 最大的那张"，并把实际选了谁打进日志。
        /// </summary>
        private static void ProbeSacPickCore(string namePart)
        {
            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            TableTurnLoop loop = Loop();
            if (setup == null || loop == null || loop.rulesV21 == null)
            {
                Debug.LogWarning("[AutoPlay/献祭] 选核心：找不到 TableSetup / TableTurnLoop，跳过。");
                return;
            }

            PlayCard pick = null;
            PlayCard best = null;

            for (int i = 0; i < setup.hand.Count; i++)
            {
                PlayCard c = setup.hand[i];
                if (c == null || c.bindingMaterial == null) continue;     // 绑定为空的必然是法术
                if (c.bindingMaterial.H <= 0) continue;                   // H=0 当核心会一进关卡就爆刀

                if (best == null || c.bindingMaterial.H > best.bindingMaterial.H) best = c;

                if (pick == null && !string.IsNullOrEmpty(namePart) &&
                    c.DisplayName != null && c.DisplayName.Contains(namePart)) pick = c;
            }

            if (pick == null) pick = best;
            if (pick == null)
            {
                Debug.LogWarning("[AutoPlay/献祭] 选核心：手牌里没有可当核心的素材（3D 手牌 "
                                 + setup.hand.Count + " 张），跳过。");
                return;
            }

            bool ok = loop.SwapBladeWith(pick);

            Debug.Log("[AutoPlay/献祭] ★点名选刀片核心：「" + pick.DisplayName + "」"
                      + "（H" + pick.bindingMaterial.H + " V" + pick.bindingMaterial.V + "）被接受 " + ok
                      + "｜notice " + loop.notice);
        }

        /// <summary>
        /// 点名把手牌里某张素材打上桌（按卡名包含匹配）——
        /// 走玩家那条 TableInteraction.ClickCard → DropCard → TableTurnLoop.PlayCardV21。
        /// 找不到就用第一张素材（并把"没找到"写进日志，不静默）。
        /// </summary>
        private static void ProbeSacPlayHandMaterial(string namePart, string what)
        {
            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            TableInteraction it = Object.FindObjectOfType<TableInteraction>();
            TableTurnLoop loop = Loop();

            if (setup == null || it == null || loop == null || loop.rulesV21 == null)
            {
                Debug.LogWarning("[AutoPlay/献祭] " + what + "：找不到 TableSetup / TableInteraction，跳过。");
                return;
            }

            TableRulesV21 r = loop.rulesV21;

            PlayCard pick = null;
            for (int i = 0; i < setup.hand.Count; i++)
            {
                PlayCard c = setup.hand[i];
                if (c == null || c.bindingMaterial == null) continue;
                if (string.IsNullOrEmpty(namePart) || (c.DisplayName != null && c.DisplayName.Contains(namePart)))
                { pick = c; break; }
            }

            if (pick == null)
            {
                pick = FirstHandMaterial(setup);
                Debug.LogWarning("[AutoPlay/献祭] " + what + "：手牌里没有「" + namePart + "」（"
                                 + r.HandText() + "）→ 退回打第一张素材。");
            }
            if (pick == null)
            {
                Debug.LogWarning("[AutoPlay/献祭] " + what + "：手牌里一张素材都没有，跳过。");
                return;
            }

            int before = r.LiveTableCount();
            bool handled = it.ClickCard(pick);

            Debug.Log("[AutoPlay/献祭] ★" + what + "：「" + pick.DisplayName + "」被处理 " + handled
                      + "｜桌面素材 " + before + " → " + r.LiveTableCount() + " 张"
                      + "｜notice " + loop.notice
                      + "\n   " + ProbeSacNumbers());
        }

        /// <summary>
        /// 第二段（㊵⓪）用：回开场重开一局，停在「选刀片」那一屏。
        /// 走的是游戏自己的入口（TableTurnLoop.Begin + ProbeEnsureBladePick），不另开捷径。
        /// </summary>
        private static void ProbeSacRestartForApTest()
        {
            TableTurnLoop loop = Loop();
            if (loop == null)
            {
                Debug.LogWarning("[AutoPlay/献祭] 第二段：找不到 TableTurnLoop，重开跳过。");
                return;
            }

            loop.Begin();
            ProbeEnsureBladePick();

            Debug.Log("[AutoPlay/献祭] ★第二段开始（验「行动机会用完的那一次算不算最后一次启动」）："
                      + "已回开场并重开到「选刀片」那一屏｜阶段 " + loop.phase);
        }

        /// <summary>
        /// 把**这一次启动**的引擎日志原文按证据形状重排一遍（㊵①~㊷⓪ 那一段用）。
        ///
        /// 【为什么不直接翻 [V21] 启动那一大坨】那一段是"启动（…）\n【启动】…① ② ③ ④ ⑤…"，
        ///   要证的那三条（"本回合最后一次启动"标记 / ⑤ 判定 / 并入刀片那一行）夹在中间，
        ///   而且每一步都有一堆中间行；验收的人得自己在几百行里找。
        ///   这里**只挑那三条 + 第 ⑤ 步的每一行**（顺序原样保留），
        ///   加上"第几行/共几行"的坐标 —— 拿这一行就能对着 lastLog 原文复核。
        /// 【为什么判据用 StartsWith 而不是 Contains】⑤ 那一行原文本身就带换行 + 步骤号，
        ///   用 Contains 会把别处的引用一起捞进来（比如 HUD 提示里那句"最后一次启动会触发献祭吞噬"）。
        /// </summary>
        private static void ProbeSacDumpLastActivate(string what)
        {
            TableTurnLoop loop = Loop();
            if (loop == null || loop.rulesV21 == null)
            {
                Debug.LogWarning("[AutoPlay/献祭] " + what + "：找不到规则侧，最后一次启动的日志没法打。");
                return;
            }

            TableRulesV21 r = loop.rulesV21;

            Debug.Log("[AutoPlay/献祭] " + what + "｜本次启动日志共 " + r.lastLog.Count + " 行"
                      + "｜下面只抄①「本回合最后一次启动」那一行、②⑤ 判定的每一行、③ 并入刀片那一行：");

            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            for (int i = 0; i < r.lastLog.Count; i++)
            {
                string s = r.lastLog[i];
                if (string.IsNullOrEmpty(s)) continue;

                bool tail = s.StartsWith("　　本次不是")               // ← 修前会写这一句
                         || s.StartsWith("　　目标已因")
                         || s.StartsWith("　　目标 D 已耗尽")
                         || s.StartsWith("　　「");                      // ← 修后应当写这一句（并入刀片）

                // 【启动】那一行就是"这一次算不算最后一次"的判词（isLastStart 的那个 true/false）
                if (s.StartsWith("【启动】") || s.StartsWith("　· 检查") || tail || s.Contains("← 本回合最后一次启动"))
                {
                    sb.Append("\n   第 ").Append(i).Append(" 行｜").Append(s);
                }
            }

            Debug.Log("[AutoPlay/献祭] " + what + "｜抄完（原文见上一条 [V21] 启动日志）" + sb);
            Debug.Log("[AutoPlay/献祭] " + what + "｜抄完之后的数字：" + ProbeSacNumbers());
        }

        /// <summary>
        /// 这一帧"桌面与破壁机之间"到底有没有卡（㊷①~㊷⑨ 的飞行帧用）。
        ///
        /// 【为什么把 IsConsuming 和"计数"分开打】
        ///   `ProbeSacFlightText()` 已经给了每张飞行卡的**坐标 + 离罐口多远 + 缩放**，
        ///   那是"它在路上"的连续证据；这里再补两个**计数**（IsConsuming 张数、
        ///   TableSetup.hand 里的空引用数）—— 前者是"有几张在飞"，
        ///   后者能区分"卡被销毁了"和"卡被摘出列表了"（ClearHand 跳过飞行卡之后，
        ///   列表里会留下还没销毁的空引用，那个数正好是"我放走了一张正在飞的卡"的指纹）。
        /// </summary>
        private static string ProbeSacFlyingCountText(string what)
        {
            TableSetup setup = Object.FindObjectOfType<TableSetup>();

            int consuming = 0;
            PlayCard[] all = Object.FindObjectsOfType<PlayCard>();
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].IsConsuming) consuming++;

            int nulls = 0;
            if (setup != null && setup.hand != null)
                for (int i = 0; i < setup.hand.Count; i++)
                    if (setup.hand[i] == null) nulls++;

            string head = "[" + what + "] PlayCard.IsConsuming = " + consuming
                        + " 张｜3D 手牌表里 " + (setup != null && setup.hand != null ? setup.hand.Count : -1)
                        + " 个格（其中空引用 " + nulls + " 个）";

            return consuming > 0
                ? head + "　✓ 有卡在桌面与罐口之间"
                : head + "　（这一帧没有卡在飞）";
        }

        /// <summary>
        /// 把桌面上**点名的那张**素材拖回手牌（走玩家那条 TableInteraction.DropCard）——
        /// ㊷⑦⑧ 用：一次要被收下、一次要被拒（同一张卡、同一个入口，只有"本回合启动过没有"不同）。
        ///
        /// 【为什么不复用 ProbeWithdrawTableMaterial】它抓的是"桌面第一张"（FirstTableCard），
        ///   而这两帧要点的必须是**指定的那一张**；而且它把被判词写死成 [AutoPlay/Black]，
        ///   混进献祭链之后分不清哪条链在说话。
        /// </summary>
        private static void ProbeSacWithdrawNamed(string namePart, string what)
        {
            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            TableInteraction it = Object.FindObjectOfType<TableInteraction>();
            TableTurnLoop loop = Loop();

            if (setup == null || it == null || loop == null || loop.rulesV21 == null)
            {
                Debug.LogWarning("[AutoPlay/献祭] " + what + "：找不到 TableSetup / TableInteraction，跳过。");
                return;
            }

            TableRulesV21 r = loop.rulesV21;

            PlayCard pick = null;
            GameJam.Rules.MaterialState st = null;

            PlayCard[] all = Object.FindObjectsOfType<PlayCard>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;

                GameJam.Rules.MaterialState s = r.FindTable(all[i]);
                if (s == null || s.removed || !s.OnTable) continue;

                if (string.IsNullOrEmpty(namePart) || (s.name != null && s.name.Contains(namePart)))
                { pick = all[i]; st = s; break; }
            }

            if (pick == null)
            {
                Debug.LogWarning("[AutoPlay/献祭] " + what + "：桌面上没有「" + namePart + "」（现在桌面 "
                                 + r.LiveTableCount() + " 张：" + r.TableText() + "），跳过。");
                return;
            }

            // 先把**规则自己的判词**问出来（这一段要证的正是"本回合启动过的不能收"）
            string reason;
            bool can = r.CanWithdraw(st, out reason);

            int tableBefore = r.LiveTableCount();
            int handBefore  = setup.hand != null ? setup.hand.Count : -1;

            // 玩家的鼠标把卡拖到手牌那一片（比 HandZoneZ 再往玩家一侧 0.15，避免压线）
            pick.Teleport(new Vector3(pick.transform.position.x, 0.022f,
                                      TableInteraction.HandZoneZ - 0.15f),
                          pick.homeEuler);
            bool handled = it.DropCard(pick, false);

            Debug.Log("[AutoPlay/献祭] ★" + what + "：把桌面上的「" + pick.DisplayName + "」拖回手牌区"
                      + "｜CanWithdraw=" + can + "（" + (can ? "应当被收下" : "应当被拒：" + reason) + "）"
                      + "｜被处理 " + handled
                      + "｜桌面素材 " + tableBefore + " → " + r.LiveTableCount() + " 张"
                      + "｜3D 手牌 " + handBefore + " → " + (setup.hand != null ? setup.hand.Count : -1) + " 张"
                      + "｜notice " + loop.notice);
        }

        /// <summary>
        /// 木纹那一项回归（用户报过"桌子的木质表面纹理不见了"）：把**桌子那个 renderer 上的材质**
        /// 现在挂的是哪个 shader / 哪张贴图直接读回来打一行。
        ///
        /// 【它和游戏自己那行 [TableSetup] 桌面材质 是什么关系】
        ///   游戏只在**建桌子那一刻**打一次（`tableLogged` 保证只打一次）——
        ///   那行证明"开局是对的"；这一行是在**这一局打完 5 次启动、结束过一次回合之后**再读一次，
        ///   证明"中途没被人换掉 / 贴图没被释放"。两行不是重复：一行是入口，一行是出口。
        /// 【为什么按材质反查而不是抓名字】桌子的 GameObject 叫 "Table"，
        ///   但 `MakeMaterial` 造出来的材质名不一定带这个字 —— 所以两个口径都收，
        ///   并把命中数打出来（命中 0 个要看得见，不能打印一行空话）。
        /// </summary>
        private static void ProbeSacWoodShader(string what)
        {
            Renderer[] all = Object.FindObjectsOfType<Renderer>();
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            int shown = 0;
            bool anyStandard = false;

            for (int i = 0; i < all.Length; i++)
            {
                Renderer rd = all[i];
                if (rd == null || rd.sharedMaterial == null) continue;

                string n  = rd.gameObject.name;
                string mn = rd.sharedMaterial.name;

                bool wood = (n == "Table")
                         || (mn != null && (mn.Contains("Wood") || mn.Contains("Table") || mn.Contains("木")));
                if (!wood || shown >= 4) continue;

                Material m = rd.sharedMaterial;
                Shader sh = m.shader;
                Texture tex = m.HasProperty("_MainTex") ? m.mainTexture : null;

                if (sh != null && sh.name == "Standard") anyStandard = true;

                sb.Append("\n   · GO「").Append(n).Append("」｜材质 ").Append(mn)
                  .Append("｜shader=").Append(sh != null ? sh.name : "（null）")
                  .Append("｜_Color=").Append(m.color.ToString())
                  .Append("｜_MainTex=").Append(tex != null ? (tex.name + " " + tex.width + "×" + tex.height) : "（无）");

                shown++;
            }

            Debug.Log("[AutoPlay/献祭] " + what + "：桌子的木纹材质读回 " + shown + " 处"
                      + (shown == 0 ? "（★ 一处都没找到 —— 这一项没验到）" : sb.ToString())
                      + (shown == 0 ? "" : (anyStandard ? "　✓ 含 shader=Standard" : "　★ 没有一处是 Standard")));
        }

        /// <summary>现在还能不能启动 / 不能的话原因是什么（HUD 上就是这两句）。</summary>
        private static string ProbeSacCanActivateText()
        {
            TableTurnLoop loop = Loop();
            if (loop == null || loop.rulesV21 == null) return "（规则侧不在）";

            TableRulesV21 r = loop.rulesV21;
            return "CanActivate=" + r.CanActivate
                 + "｜BlockReason「" + r.BlockReason + "」"
                 + "｜行动机会 " + r.actionPoints + "/" + GameJam.Rules.LevelRun.ActionPointsPerTurn
                 + "｜手牌 " + r.HandText();
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

        // ══════════════════════════════════════════════════════════════
        //  卡面文字探针（DSH_CARDFACEPROBE=1，㛢⓪~㛢⑫）用的口子
        //
        //  【这一节只做三件事】认牌 / 怼近拍 / 把"文字排行"和"数字实测"量成数字。
        //   一个游戏规则都不碰 —— 拍特写只是临时注册一个 cardface 机位（探针动作）。
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 卡面特写的取景边距 —— 比 FrameTableBounds 的默认 1.1 略紧一点，但比 1.0 松：
        /// 实测 1.02 那一档在 78° 俯视下会把卡的下缘切掉几十个像素（公式只按盒子中心估深度，
        /// 卡又是"很薄但很长"的那种），1.12 能把整张卡完整放进画面还留一圈背景。
        /// </summary>
        private const float CardFaceCloseupMargin = 1.12f;

        /// <summary>
        /// 卡面探针用的 Game 视图尺寸 —— **钉死**，好让"改前 / 改后"两遍落在同一个窗口上。
        /// 1470×1167 是这台机器上 Game 视图的常用尺寸（宽高比 1.26，牌组 6 张排成一行）。
        /// </summary>
        private const int CardFaceProbeW = 1470;
        private const int CardFaceProbeH = 1167;

        /// <summary>桌上那张刀片卡（它不在任何列表里，只能按引用认 —— 见 TableTurnLoop.bladeCard）。</summary>
        private static PlayCard BladeCard()
        {
            TableTurnLoop loop = Loop();
            return loop != null ? loop.bladeCard : null;
        }

        /// <summary>手牌里第 <paramref name="index"/> 张**素材**（法术的 bindingMaterial 是空的，靠它区分）。</summary>
        private static PlayCard HandMaterial(int index)
        {
            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            if (setup == null || setup.hand == null || index < 0) return null;

            int seen = 0;
            for (int i = 0; i < setup.hand.Count; i++)
            {
                PlayCard c = setup.hand[i];
                if (c == null || c.bindingMaterial == null) continue;
                if (seen == index) return c;
                seen++;
            }
            return null;
        }

        /// <summary>
        /// 一张 3D 卡**不含文字**的世界包围盒。
        ///
        /// 【为什么要把文字排掉】文字渲染器的包围盒是"墨迹盒"，高只有几毫米、
        ///   又贴在卡的两端 —— 算进去会把取景拉偏（特写就会歪、还会变远）。
        ///   这里只要卡身 + 卡面：那才是"这张牌占的地方"。
        /// </summary>
        private static Bounds CardBodyBounds(PlayCard card)
        {
            Bounds b = new Bounds(card != null ? card.transform.position : Vector3.zero, Vector3.one * 0.01f);
            if (card == null) return b;

            Renderer[] rs = card.GetComponentsInChildren<Renderer>();
            bool any = false;
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i] == null) continue;
                if (rs[i].GetComponent<TextMesh>() != null) continue;   // 文字不算
                if (!any) { b = rs[i].bounds; any = true; }
                else b.Encapsulate(rs[i].bounds);
            }
            return b;
        }

        /// <summary>
        /// 把镜头怼到一张卡的卡面上拍一张。
        ///
        /// 【为什么临时注册一个机位】游戏里的机位全是"装得下整桌 / 整排手牌"的，
        ///   最紧的那一档下手牌也只占屏幕一小块 —— 三个只有二十几像素的装饰形状
        ///   在那个距离上根本看不出"数字有没有压到边框"。
        ///   所以按卡的包围盒算一个只装得下这一张牌的机位，**复用**
        ///   CameraRig.FrameTableBounds 那一套投影（不在这里另写一份取景算式）。
        ///   SnapTo 立刻到位 —— GoTo 是平滑过渡，截图会拍到中途。
        ///
        /// tiltDeg 是俯角：62° 接近游戏里的桌面视角，78° 接近正上方俯视。
        /// 牌不在（还没建出来）时打一条警告并返回 true（本阶段放行，别卡死）。
        /// </summary>
        private static bool ShotCardCloseup(string name, PlayCard card, float tiltDeg)
        {
            if (card == null)
            {
                Debug.LogWarning("[AutoPlay/卡面] 要拍的那张牌现在不在（" + name + "），这一步跳过。");
                return true;
            }

            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            if (setup == null || setup.rig == null) return true;

            setup.rig.FrameTableBounds("cardface", CardBodyBounds(card), tiltDeg, CardFaceCloseupMargin, false);
            setup.rig.SnapTo("cardface");
            return Shot(name);
        }

        /// <summary>
        /// 牌组卡排里第 <paramref name="index"/> 张大卡（TableChoiceRig.BuildOne 建的，
        /// 名字是 "BigCard_" + id）拍一张特写 —— 用户那张"下半屏一个字都没有"的截图就是这一排。
        ///
        /// 取哪一张按**屏幕位置**排（先上下、再左右）：用户说的"下面一排"就是屏幕口径，
        /// 而物体层级顺序 / 名字顺序都跟摆位无关。
        /// </summary>
        private static bool ShotBigCardCloseup(string name, int index)
        {
            TableChoiceRig rig = Object.FindObjectOfType<TableChoiceRig>();
            TableSetup setup = Object.FindObjectOfType<TableSetup>();
            if (rig == null || setup == null || setup.rig == null) return true;

            List<Transform> cards = new List<Transform>();
            Transform[] all = Object.FindObjectsOfType<Transform>();
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].name.StartsWith("BigCard_")) cards.Add(all[i]);

            if (cards.Count == 0)
            {
                Debug.LogWarning("[AutoPlay/卡面] 桌上一张牌组卡都没有，" + name + " 拍不到。");
                return true;
            }

            Camera cam = Camera.main != null ? Camera.main : Object.FindObjectOfType<Camera>();
            if (cam != null)
            {
                cards.Sort(delegate (Transform a, Transform b)
                {
                    Vector3 sa = cam.WorldToScreenPoint(a.position);
                    Vector3 sb = cam.WorldToScreenPoint(b.position);
                    if (Mathf.Abs(sa.y - sb.y) > 1f) return sa.y.CompareTo(sb.y);   // 先按屏幕上下
                    return sa.x.CompareTo(sb.x);                                   // 同一排再按左右
                });
            }

            Transform pick = cards[Mathf.Clamp(index, 0, cards.Count - 1)];

            Bounds b = new Bounds(pick.position, Vector3.one * 0.01f);
            Renderer[] rs = pick.GetComponentsInChildren<Renderer>();
            bool any = false;
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i] == null || rs[i].GetComponent<TextMesh>() != null) continue;
                if (!any) { b = rs[i].bounds; any = true; }
                else b.Encapsulate(rs[i].bounds);
            }

            setup.rig.FrameTableBounds("cardface", b, 78f, CardFaceCloseupMargin, false);
            setup.rig.SnapTo("cardface");
            return Shot(name);
        }

        /// <summary>一张卡上的 H/D/V 三个值，写成一行（日志用；读的是卡自己绑的数据）。</summary>
        private static string CardStatsText(PlayCard card)
        {
            if (card == null) return "（没有这张牌）";

            string label = card.DisplayName;
            GameJam.Data.Ingredient ing = card.card != null ? card.card.ingredient : null;
            if (ing == null || ing.attrs == null) return label + "（模块 / 没有三属性）";

            return label + "　H=" + ing.attrs.Get(GameJam.Data.AttrId.Salt)
                         + " D=" + ing.attrs.Get(GameJam.Data.AttrId.Mercury)
                         + " V=" + ing.attrs.Get(GameJam.Data.AttrId.Sulfur);
        }

        /// <summary>
        /// 把场上每一张卡（3D 手牌 / 刀片卡 / 桌面卡，以及牌组大卡）的**每一行文字**
        /// 和**卡面**的渲染次序比一次，逐张报一行、最后给一个总数。
        ///
        /// 【为什么这条清单比截图重要】"文字排在卡面之后"是用户那两张截图（桌面视角名字不见、
        ///   俯视牌组卡整片不见）的**同一个根因**，而它是个可以逐条核对的数字：
        ///   文字 sortingOrder &gt; 卡面 sortingOrder，就永远排在后面，跟机位无关。
        ///   截图只能证明"这一屏这一次没事"，清单能证明"每一张卡的每一行都排对了"。
        ///
        /// "被显式关掉"的行不算错：级联会把被压住那几张的数值关掉（见 CardFactory.CardTextSortingOrder
        /// 里"为什么只抬名字不抬 H/D/V"那一段），那是有意为之。
        /// </summary>
        private static void ProbeCardTextReport(string what)
        {
            Debug.Log("[AutoPlay/卡面] ═══ " + what + "：卡面文字次序 ═══");

            int cards = 0, bad = 0;

            Transform[] roots = Object.FindObjectsOfType<Transform>();
            for (int i = 0; i < roots.Length; i++)
            {
                Transform t = roots[i];
                if (t == null) continue;

                bool isCard = t.GetComponent<PlayCard>() != null || t.name.StartsWith("BigCard_");
                if (!isCard) continue;

                int faceOrder   = FaceSortingOrder(t);
                int textOrder   = TextSortingOrderOf(t);
                int textLines   = 0, textBad = 0, textOff = 0;

                Renderer[] rs = t.GetComponentsInChildren<Renderer>();
                for (int k = 0; k < rs.Length; k++)
                {
                    if (rs[k] == null || rs[k].GetComponent<TextMesh>() == null) continue;
                    textLines++;
                    if (rs[k].sortingOrder <= faceOrder) textBad++;
                    if (!rs[k].enabled) textOff++;
                }

                cards++;
                bad += textBad;

                string line = "[AutoPlay/卡面] " + (textBad == 0 ? "✓" : "✗") + " " + t.name
                            + "：文字 " + textLines + " 行、卡面 order " + faceOrder
                            + "、文字 order " + textOrder
                            + "、排错 " + textBad + " 行、显式关掉 " + textOff + " 行";

                if (textBad == 0) Debug.Log(line);
                else Debug.LogWarning(line + "　→ ★ 这些行排在卡面之前，换个机位就会被卡面盖住");
            }

            Debug.Log("[AutoPlay/卡面] ═══ " + what + " 汇总：卡 " + cards + " 张，排错文字 "
                      + bad + " 行（必须 0）═══");

            if (bad > 0)
                Debug.LogWarning("[AutoPlay/卡面] ★ " + what + "：有 " + bad
                                 + " 行文字排在卡面**之前** —— 正是用户报的那两个症状的根因。");
        }

        /// <summary>这张卡的**卡面**渲染次序（名字叫 Face 的那个 Quad；找不到就按 0 算）。</summary>
        private static int FaceSortingOrder(Transform cardRoot)
        {
            Renderer[] rs = cardRoot.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i] == null) continue;
                if (rs[i].GetComponent<TextMesh>() != null) continue;
                if (rs[i].name != "Face") continue;
                return rs[i].sortingOrder;
            }
            return 0;
        }

        /// <summary>这张卡上第一个文字渲染器的 sortingOrder（日志里报一个代表值）。</summary>
        private static int TextSortingOrderOf(Transform cardRoot)
        {
            Renderer[] rs = cardRoot.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i] == null) continue;
                if (rs[i].GetComponent<TextMesh>() == null) continue;
                return rs[i].sortingOrder;
            }
            return -1;
        }

        /// <summary>
        /// 把三个数字的**实测结果**报出来：字号 / 墨迹盒 / 它那一格的内圈尺寸 / 四条边的余量 /
        /// 居中误差。
        ///
        /// 【为什么"余量"和"居中"必须是个数字】用户要的是"不压形状边框、居中"，
        ///   而形状边框在卡面上只有一两个像素宽 —— 缩略图里根本看不出来。
        ///   这里把"墨迹盒"和"形状内圈"都换成**效果图像素**（和 CardArt 量版面用的是同一套坐标），
        ///   余量为负就是压上了；居中误差是"墨迹盒中心"和"形状中心"之差，同样是像素。
        ///
        /// 【为什么量的是网格、不是 Renderer.bounds】bounds 是**世界** AABB：
        ///   卡是带偏航摆的，一转角度同一个墨迹盒的 AABB 就被撑大（实测同一串 "12"、
        ///   同一个字号在不同卡上量出 12.0 / 12.9 px 两个值）。网格顶点在**局部**坐标里，
        ///   和朝向无关，才是这个串真实的宽高。
        /// </summary>
        private static void ProbeStatDigitReport(string what)
        {
            Debug.Log("[AutoPlay/卡面] ═══ " + what + " ═══");

            // 对照卡优先报（它们就是为"两位数 / 一位数 / 零值"这三种情况造的）
            for (int i = 0; i < demoCards.Count; i++)
                if (demoCards[i] != null) ReportStatDigits(demoCards[i].transform, "对照卡" + (i + 1));

            // 场上真实卡再报几张（正式美术卡面 = 牌面上有三个同名的数值文字对象）
            Transform[] roots = Object.FindObjectsOfType<Transform>();
            int done = 0;
            for (int i = 0; i < roots.Length && done < 3; i++)
            {
                Transform t = roots[i];
                if (t == null || t.GetComponent<PlayCard>() == null) continue;
                if (CountStatTexts(t) < 3) continue;
                if (IsDemoCard(t)) continue;

                done++;
                ReportStatDigits(t, null);
            }

            if (done == 0 && demoCards.Count == 0)
                Debug.LogWarning("[AutoPlay/卡面] 场上没有带正式美术卡面（三个数字）的牌，这一步量不到东西。");
        }

        /// <summary>这张卡上有几个"数值文字"对象（正式美术卡面 = 3 个，程序化卡面 = 1 个）。</summary>
        private static int CountStatTexts(Transform cardRoot)
        {
            int n = 0;
            Renderer[] rs = cardRoot.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < rs.Length; i++)
                if (rs[i] != null && rs[i].name == CardFactory.StatsTextObject) n++;
            return n;
        }

        private static bool IsDemoCard(Transform t)
        {
            for (Transform p = t; p != null; p = p.parent)
                if (p.name == "ProbeDemoCards") return true;
            return false;
        }

        /// <summary>量一张卡上那三个数字，一行一个。</summary>
        private static void ReportStatDigits(Transform cardRoot, string label)
        {
            Renderer[] rs = cardRoot.GetComponentsInChildren<Renderer>();
            int slots = 0;

            for (int k = 0; k < rs.Length && slots < CardArt.StatSlots.Length; k++)
            {
                Renderer r = rs[k];
                if (r == null || r.name != CardFactory.StatsTextObject) continue;

                GameObject go = r.gameObject;
                TextMesh tm = go.GetComponent<TextMesh>();
                string digits = tm != null ? tm.text : "?";
                string head = label != null ? label : cardRoot.name;

                Vector3 inkCenter, inkSize;
                if (!CardFactory.InkBox(go, out inkCenter, out inkSize))
                {
                    Debug.LogWarning("[AutoPlay/卡面]   " + head + " 的「" + digits
                                     + "」：量不到墨迹盒（TextMesh 网格为空）");
                    slots++;
                    continue;
                }

                float size = go.transform.localScale.x;

                // 墨迹盒（局部单位 × 字号）→ 效果图像素：卡深 0.335 对应效果图 182 px
                float inkPxW = inkSize.x * size / CardFactory.CardDepth * 182f;
                float inkPxH = inkSize.y * size / CardFactory.CardDepth * 182f;

                CardArt.StatSlot s = CardArt.StatSlots[slots];
                float innerW = 2f * s.halfW * CardArt.StatFitMargin;
                float innerH = 2f * s.halfH * CardArt.StatFitMargin;

                // 居中误差：墨迹盒中心（卡根节点局部）与形状中心之差，同样换成效果图像素
                Vector2 at = CardArt.EffectPxToLocal(s.centerPx);
                Vector3 actual = go.transform.localPosition
                               + go.transform.localRotation * (inkCenter * size);
                float dxPx = (actual.x - at.x) / CardFactory.CardWidth * 136f;
                float dzPx = (actual.z - at.y) / CardFactory.CardDepth * 182f;

                bool fit = inkPxW <= innerW + 0.01f && inkPxH <= innerH + 0.01f;
                bool centered = Mathf.Abs(dxPx) <= 0.5f && Mathf.Abs(dzPx) <= 0.5f;

                string slotName = slots == 0 ? "H(菱形)" : (slots == 1 ? "D(圆形)" : "V(方形)");
                string line = "[AutoPlay/卡面]   " + head + " " + slotName + "「" + digits + "」"
                            + "｜字号 " + size.ToString("0.00000")
                            + "｜墨迹 " + inkPxW.ToString("0.0") + "×" + inkPxH.ToString("0.0") + " px"
                            + "｜这一格可用 " + innerW.ToString("0.0") + "×" + innerH.ToString("0.0") + " px"
                            + "｜余量 横 " + (innerW - inkPxW).ToString("0.0")
                            + " 纵 " + (innerH - inkPxH).ToString("0.0")
                            + "｜居中误差 " + dxPx.ToString("0.00") + "," + dzPx.ToString("0.00") + " px";

                if (fit && centered) Debug.Log(line + "　✓");
                else Debug.LogWarning(line + "　★ " + (fit ? "居中偏了" : "压到边框了"));

                slots++;
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  对照卡（两位数 / 一位数 / 零值）
        //
        //  【为什么要临时造卡】用户要看的是"两位数也放得下""零值会不会偏小偏空"，
        //    而**真实卡表里这两种情况都没有**：cards_v21.json 里所有素材的 H/D/V
        //    都是 1 位数（1~6）、没有 0。所以这里把某一副食材的三属性临时改成三种组合，
        //    各造一张卡拍特写 —— 走的还是 CardFactory.Create 那条真路，一个字都不绕。
        //
        //  ★ 探针动作，只在副本工程里跑：改的是**运行期**的 attrs，每造一张立刻改回原值；
        //    对照卡挂在一个独立的根节点下（不在 cardsRoot 里），拍完整个销毁 ——
        //    所以"桌面素材残留几张"那条自检不会被它们污染。
        // ══════════════════════════════════════════════════════════════

        /// <summary>三张对照卡的三属性值：H / D / V。</summary>
        private static readonly int[][] DemoStatValues =
        {
            new int[] { 12, 3, 1 },    // 用户报的那张卡：H 两位数 + D/V 一位数
            new int[] {  3, 3, 3 },    // 全一位数：三个数字应该一样大
            new int[] {  8, 0, 0 },    // 零值：会不会偏小 / 形状显得空
        };

        private static GameObject     demoRoot;
        private static List<PlayCard> demoCards = new List<PlayCard>();

        private static void BuildDemoStatCards()
        {
            PlayCard src = HandMaterial(0);
            if (src == null || src.card == null || src.card.ingredient == null)
            {
                Debug.LogWarning("[AutoPlay/卡面] 手牌里没有素材，对照卡造不出来。");
                return;
            }

            GameJam.Data.Ingredient ing = src.card.ingredient;
            int h0 = ing.attrs.Get(GameJam.Data.AttrId.Salt);
            int d0 = ing.attrs.Get(GameJam.Data.AttrId.Mercury);
            int v0 = ing.attrs.Get(GameJam.Data.AttrId.Sulfur);

            // 摆在桌子下面（y = −1.0）：那里什么都没有，特写背景干净，也不会挡到桌上的任何东西
            demoRoot = new GameObject("ProbeDemoCards");
            demoRoot.transform.position = new Vector3(0f, -1.0f, 0f);

            for (int i = 0; i < DemoStatValues.Length; i++)
            {
                int[] v = DemoStatValues[i];

                ing.attrs.Set(GameJam.Data.AttrId.Salt,    v[0]);
                ing.attrs.Set(GameJam.Data.AttrId.Mercury, v[1]);
                ing.attrs.Set(GameJam.Data.AttrId.Sulfur,  v[2]);

                Vector3 home = demoRoot.transform.position + new Vector3((i - 1) * 0.55f, 0f, 0f);
                PlayCard c = CardFactory.Create(src.card, demoRoot.transform, home, Vector3.zero);
                if (c != null) demoCards.Add(c);

                Debug.Log("[AutoPlay/卡面] 对照卡 " + (i + 1) + "：H=" + v[0] + " D=" + v[1] + " V=" + v[2]
                          + "（卡面皮取自「" + src.DisplayName + "」，这三个值只是运行期临时改的）");
            }

            // ★ 立刻改回去：这份 attrs 是**游戏正在用的那一份**（手牌那张卡、刀片都指着它）
            ing.attrs.Set(GameJam.Data.AttrId.Salt,    h0);
            ing.attrs.Set(GameJam.Data.AttrId.Mercury, d0);
            ing.attrs.Set(GameJam.Data.AttrId.Sulfur,  v0);
        }

        private static void DestroyDemoStatCards()
        {
            if (demoRoot != null) CardFactory.DestroySafe(demoRoot);
            demoRoot = null;
            demoCards.Clear();
        }

        private static PlayCard DemoCard(int i)
        {
            return (i >= 0 && i < demoCards.Count) ? demoCards[i] : null;
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
