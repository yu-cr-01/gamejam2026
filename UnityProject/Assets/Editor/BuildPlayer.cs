using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameJam.EditorTools
{
    /// <summary>
    /// Windows 64 位独立播放器的构建入口。
    ///
    /// 【为什么需要这个脚本】
    ///   本工程是"空场景 + 代码搭桌子"（RuntimeInitializeOnLoadMethod 启动），
    ///   仓库里**一个 .unity 场景文件都没有**，ProjectSettings/EditorBuildSettings.asset
    ///   的 m_Scenes 也是空的 —— BuildPipeline 拿到空场景列表会直接报错，
    ///   所以这里必须先保证"至少有一个启用的场景"，再构建。
    ///
    /// 【命令行怎么用：-executeMethod 必须指向一个**方法**】
    ///   ✔ Unity.exe -quit -batchmode -projectPath &lt;工程&gt; ^
    ///       -executeMethod GameJam.EditorTools.BuildPlayer.BuildWindows ^
    ///       -logFile &lt;日志&gt;
    ///   ✘ -executeMethod GameJam.EditorTools.BuildPlayer
    ///     （直接写类名会报 executeMethod class 'BuildPlayer' could not be found，退出码 1）
    ///
    /// 【退出码】Unity 加了 -quit 之后，**即使构建失败也会返回 0**。
    ///   所以失败时这里主动调 EditorApplication.Exit(1)，
    ///   否则外层脚本没法判断到底成没成。
    /// </summary>
    public static class BuildPlayer
    {
        // ── 产物位置 ──────────────────────────────────────────────────
        // 相对工程根目录。Builds/ 已经在 .gitignore 里，不进 Git。
        private const string OutputDirRel = "Builds/v21_win";

        /// <summary>exe 文件名（同时决定 xxx_Data 目录名）。</summary>
        private const string ExeName = "破壁机计划.exe";

        // ── 给策划看的版本信息 ────────────────────────────────────────
        private const string ProductName = "破壁机计划";

        /// <summary>批处理构建入口。菜单：工具 / 构建 Windows 试玩包。</summary>
        [MenuItem("工具/构建 Windows 试玩包 (v2.1)")]
        public static void BuildWindows()
        {
            try
            {
                RunBuild();
            }
            catch (Exception e)
            {
                Debug.LogError("[Build] 构建抛异常：" + e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }

        private static void RunBuild()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;

            Debug.Log("[Build] ── 构建开始 ──────────────────────────────");
            Debug.Log("[Build] 工程：" + projectRoot);
            Debug.Log("[Build] Unity：" + Application.unityVersion);
            Debug.Log("[Build] 平台：StandaloneWindows64");

            // ── 1. 播放器设置：只动"给玩家看"的那几项 ────────────────
            ApplyPlayerSettings();

            // ── 2. 保证至少有一个启用的场景 ───────────────────────────
            string[] scenes = ResolveScenes();
            Debug.Log("[Build] 构建场景 " + scenes.Length + " 个：");
            for (int i = 0; i < scenes.Length; i++) Debug.Log("[Build]   [" + i + "] " + scenes[i]);

            // ── 3. 清空输出目录 ───────────────────────────────────────
            string outDir = Path.GetFullPath(Path.Combine(projectRoot, OutputDirRel));
            if (Directory.Exists(outDir))
            {
                Debug.Log("[Build] 清掉旧产物：" + outDir);
                Directory.Delete(outDir, true);
            }
            Directory.CreateDirectory(outDir);

            string exePath = Path.Combine(outDir, ExeName);

            // ── 4. 构建 ───────────────────────────────────────────────
            BuildPlayerOptions opt = new BuildPlayerOptions();
            opt.scenes           = scenes;
            opt.locationPathName = exePath;
            opt.target           = BuildTarget.StandaloneWindows64;
            opt.targetGroup      = BuildTargetGroup.Standalone;
            opt.options          = BuildOptions.None;   // ★ 正式包，不是 Development Build

            BuildReport report = BuildPipeline.BuildPlayer(opt);
            BuildSummary sum = report.summary;

            Debug.Log("[Build] 结果：" + sum.result
                      + "｜错误 " + sum.totalErrors
                      + "｜警告 " + sum.totalWarnings
                      + "｜耗时 " + sum.totalTime);

            if (sum.result != BuildResult.Succeeded)
            {
                Debug.LogError("[Build] ✘ 构建失败，result=" + sum.result
                               + "，errors=" + sum.totalErrors);
                for (int i = 0; i < report.steps.Length; i++)
                {
                    BuildStep step = report.steps[i];
                    for (int j = 0; j < step.messages.Length; j++)
                    {
                        BuildStepMessage m = step.messages[j];
                        if (m.type == LogType.Error || m.type == LogType.Exception)
                            Debug.LogError("[Build][步骤 " + step.name + "] " + m.content);
                    }
                }
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            // ── 5. 打印产物路径与大小 ─────────────────────────────────
            if (!File.Exists(exePath))
            {
                Debug.LogError("[Build] ✘ 构建报告说成功，但 exe 不存在：" + exePath);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            long total = 0;
            string[] files = Directory.GetFiles(outDir, "*", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                try { total += new FileInfo(files[i]).Length; } catch { }
            }

            Debug.Log("[Build] ✔ 构建成功");
            Debug.Log("[Build] exe：" + exePath);
            Debug.Log("[Build] exe 体积：" + Mb(new FileInfo(exePath).Length));
            Debug.Log("[Build] 整个目录：" + outDir);
            Debug.Log("[Build] 目录体积：" + Mb(total) + "（" + files.Length + " 个文件）");
            Debug.Log("[Build] Player.log 会写到：" + PlayerLogPath());
            Debug.Log("[Build] ── 构建结束 ──────────────────────────────");

            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        private static string Mb(long bytes)
        {
            return (bytes / 1024.0 / 1024.0).ToString("0.00") + " MB";
        }

        /// <summary>
        /// Player.log 的落盘位置：
        ///   %USERPROFILE%\AppData\LocalLow\&lt;公司名&gt;\&lt;产品名&gt;\Player.log
        /// LocalApplicationData 是 ...\AppData\Local，往上一级才是 ...\AppData。
        /// </summary>
        private static string PlayerLogPath()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            DirectoryInfo appData = Directory.GetParent(local);          // ...\AppData
            string root = appData != null ? appData.FullName : local;
            return Path.Combine(root, "LocalLow",
                                PlayerSettings.companyName, PlayerSettings.productName, "Player.log");
        }

        // ── 播放器设置 ────────────────────────────────────────────────
        // 这几项会写回 ProjectSettings/ProjectSettings.asset，属于"改工程设置"，
        // 已经在交付回报里逐条列出。
        private static void ApplyPlayerSettings()
        {
            // 产品名：决定窗口标题和 Player.log 的落盘目录。
            // 默认值 UnityProject 会把日志丢到 LocalLow\DefaultCompany\UnityProject\，
            // 策划和我们都分不清哪个是哪个。
            PlayerSettings.productName = ProductName;

            // 窗口化 1600×900：默认是"无边框全屏 + 原生分辨率"，
            // 策划的显示器尺寸未知，多屏环境下无边框全屏容易跑到副屏上去；
            // 窗口化还能让他一边开着 试玩说明.txt 一边玩。
            PlayerSettings.fullScreenMode      = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth  = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow     = true;

            // 切出去看说明时别把游戏冻住（默认 Run In Background = false，
            // 失焦后画面定格，容易被误认成卡死）。
            PlayerSettings.runInBackground = true;

            AssetDatabase.SaveAssets();
        }

        // ── 场景 ──────────────────────────────────────────────────────
        /// <summary>
        /// 拿到"构建用场景列表"，按三级兜底：
        ///   1) Build Settings 里已经有启用的场景 → 直接用（正常路径，什么都不改）
        ///   2) Build Settings 是空的，但 Assets 下有 .unity → 全部登记进去
        ///   3) 一个场景都没有 → 生成一个**空场景**（不带默认相机/灯，
        ///      因为 TableSetup.ClearDefaultSceneObjects 会自己清场，
        ///      多余的主相机还会和 TableCamera 抢 AudioListener）
        /// </summary>
        private static string[] ResolveScenes()
        {
            // ── 1 ──
            List<string> enabled = new List<string>();
            EditorBuildSettingsScene[] configured = EditorBuildSettings.scenes;
            if (configured != null)
            {
                for (int i = 0; i < configured.Length; i++)
                {
                    EditorBuildSettingsScene s = configured[i];
                    if (s != null && s.enabled && !string.IsNullOrEmpty(s.path)) enabled.Add(s.path);
                }
            }
            if (enabled.Count > 0)
            {
                Debug.Log("[Build] 场景来源：EditorBuildSettings（已有 " + enabled.Count + " 个启用场景，不改动）");
                return enabled.ToArray();
            }

            // ── 2 ──
            List<string> found = new List<string>();
            string[] guids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path)) continue;
                if (!path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)) continue;
                if (IsUnderEditorFolder(path)) continue;   // Editor 目录里的资源不进包
                found.Add(path);
            }
            found.Sort(StringComparer.Ordinal);
            if (found.Count > 0)
            {
                Debug.Log("[Build] 场景来源：Assets 下扫到的 .unity（登记进 Build Settings）");
                RegisterScenes(found);
                return found.ToArray();
            }

            // ── 3 ──
            Debug.Log("[Build] Assets 下一个 .unity 都没有 → 生成空场景 Assets/Scenes/Bootstrap.unity");
            string scenePath = CreateEmptyScene("Assets/Scenes", "Bootstrap.unity");
            List<string> one = new List<string>();
            one.Add(scenePath);
            RegisterScenes(one);
            return one.ToArray();
        }

        private static bool IsUnderEditorFolder(string assetPath)
        {
            string p = assetPath.Replace('\\', '/');
            return p.StartsWith("Assets/Editor/", StringComparison.OrdinalIgnoreCase)
                || p.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// 建一个**空**场景并落盘。
        ///
        /// 【为什么不能图省事用 NewSceneMode.Additive】
        ///   批处理模式下 Unity 开局就是一个**没保存过的 Untitled 场景**，
        ///   这时候 Additive 会直接抛：
        ///     InvalidOperationException: Cannot create a new scene additively
        ///     with an untitled scene unsaved.
        ///   所以只能走 Single（关掉当前场景再建新的）。
        ///
        /// 【Single 的副作用怎么收】
        ///   交互模式下 Single 会把用户正在编的场景顶掉，太脏 ——
        ///   所以先记下当前场景路径，建完立刻切回去；
        ///   如果用户当前场景本身就是没存过的 Untitled（没路径可回），
        ///   那就明确报错让他先存盘，而不是把他的工作区吞掉。
        /// </summary>
        private static string CreateEmptyScene(string folder, string fileName)
        {
            Scene active = EditorSceneManager.GetActiveScene();
            string previousPath = active.IsValid() ? active.path : null;
            bool previousUntitled = string.IsNullOrEmpty(previousPath);

            if (previousUntitled && !Application.isBatchMode)
            {
                throw new Exception(
                    "当前打开的是一个没保存过的 Untitled 场景。请先 Ctrl+S 存盘"
                    + "（或打开一个已有场景），再执行构建 —— 否则没法在旁边新建空场景。");
            }

            if (!AssetDatabase.IsValidFolder(folder))
            {
                string parent = folder.Substring(0, folder.LastIndexOf('/'));
                string leaf   = folder.Substring(folder.LastIndexOf('/') + 1);
                if (!AssetDatabase.IsValidFolder(parent))
                    AssetDatabase.CreateFolder("Assets", parent.Substring("Assets/".Length));
                AssetDatabase.CreateFolder(parent, leaf);
            }

            string scenePath = folder + "/" + fileName;

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            bool ok = EditorSceneManager.SaveScene(scene, scenePath);

            // 把用户原来的场景切回去（批处理模式下 previousPath 是空的，跳过）
            if (!previousUntitled && File.Exists(previousPath))
                EditorSceneManager.OpenScene(previousPath, OpenSceneMode.Single);

            AssetDatabase.Refresh();

            if (!ok || !File.Exists(scenePath))
                throw new Exception("空场景创建失败：" + scenePath);

            Debug.Log("[Build] 空场景已生成：" + scenePath);
            return scenePath;
        }

        private static void RegisterScenes(List<string> paths)
        {
            List<EditorBuildSettingsScene> list = new List<EditorBuildSettingsScene>();
            for (int i = 0; i < paths.Count; i++)
                list.Add(new EditorBuildSettingsScene(paths[i], true));
            EditorBuildSettings.scenes = list.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("[Build] 已写回 EditorBuildSettings：" + paths.Count + " 个场景");
        }
    }
}
