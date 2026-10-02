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

        static AutoPlayHarness()
        {
            if (System.Environment.GetEnvironmentVariable("DSH_AUTOPLAY") != "1") return;

            outDir  = System.Environment.GetEnvironmentVariable("DSH_CAPTURE_DIR");
            if (string.IsNullOrEmpty(outDir)) outDir = Path.GetTempPath();

            capture = System.Environment.GetEnvironmentVariable("DSH_PLAYCAPTURE") == "1";

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

                // ⑥ 截图是帧末异步写的，等一拍再退
                case 5:
                    if (EditorApplication.timeSinceStartup - stageTime < 1.5) return;
                    Debug.Log("[AutoPlay] 截图完成，退出。");
                    EditorApplication.Exit(0);
                    return;
            }
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
