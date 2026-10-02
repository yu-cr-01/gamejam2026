using System.IO;
using UnityEditor;
using UnityEngine;
using GameJam.Prototype;

namespace GameJam.EditorTools
{
    /// <summary>
    /// 3D 桌面原型的离屏截图工具（**只在编辑器里存在，不进包**）。
    ///
    /// 【为什么需要它】
    /// 桌面原型的场景是代码在运行时搭出来的，编辑器里没有 .unity 场景可以看。
    /// 于是"文字有没有镜像""版面好不好看"这类问题只能靠人肉 Play 一遍再看，
    /// 改一次看一次，来回很慢。
    ///
    /// 这个工具在**编辑模式**下手动跑一遍 TableSetup 的构建流程，
    /// 把相机渲染到 RenderTexture 再存成 PNG —— 不用进 Play 模式，
    /// 命令行也能跑，于是"改代码 → 出图 → 看图"可以自动串起来。
    ///
    /// 【用法】
    ///   菜单：工具 → 截图 3D 桌面
    ///   命令行：
    ///     Unity.exe -projectPath &lt;工程&gt; -executeMethod
    ///       GameJam.EditorTools.TablePreviewCapture.Capture -quit
    ///   输出目录用环境变量 DSH_CAPTURE_DIR 指定，默认系统临时目录。
    /// </summary>
    public static class TablePreviewCapture
    {
        private const int ShotWidth  = 1143;
        private const int ShotHeight = 510;

        /// <summary>要出图的机位名（对应 CameraRig 里注册的 board / hand / top / juicer / free）。</summary>
        private static readonly string[] Views = { "board", "juicer", "top", "hand", CameraRig.FreeView };

        [MenuItem("工具/截图 3D 桌面")]
        public static void CaptureFromMenu()
        {
            string dir = RunCapture();
            if (!string.IsNullOrEmpty(dir)) EditorUtility.RevealInFinder(dir);
        }

        /// <summary>命令行入口 —— 跑完直接退出进程，失败返回非 0 退出码。</summary>
        public static void Capture()
        {
            string dir = RunCapture();
            EditorApplication.Exit(string.IsNullOrEmpty(dir) ? 1 : 0);
        }

        /// <summary>真正干活的部分。成功返回输出目录，失败返回 null。</summary>
        private static string RunCapture()
        {
            string dir = System.Environment.GetEnvironmentVariable("DSH_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.GetTempPath();

            GameObject root = null;
            try
            {
                Directory.CreateDirectory(dir);

                // ① 把整套桌面搭起来。
                //    TableSetup 的构建写在 Awake 里，编辑模式下不会自动调，
                //    所以用 SendMessage 手动触发一次。
                root = new GameObject("[PreviewTable]");
                TableSetup setup = root.AddComponent<TableSetup>();
                root.SendMessage("Awake", SendMessageOptions.DontRequireReceiver);

                if (setup.cam == null || setup.rig == null)
                {
                    Debug.LogError("[截图] TableSetup 没能搭出相机，先确认脚本编译没报错。");
                    return null;
                }

                setup.cam.aspect = (float)ShotWidth / ShotHeight;

                // 给罐子灌点汁，好确认液面和刻度对得上。
                // 编辑模式下 Update 不跑，所以要用 Snap 版本一步设到位。
                if (setup.juicer != null)
                {
                    setup.juicer.SetScore(600, 1000);
                    setup.juicer.SnapLevelToScore();
                }

                for (int i = 0; i < Views.Length; i++)
                {
                    setup.rig.SnapTo(Views[i]);

                    string path = Path.Combine(dir, "table_" + Views[i] + ".png");
                    RenderToFile(setup.cam, path);

                    Debug.Log("[截图] 已写出 " + path);
                }

                return dir;
            }
            catch (System.Exception e)
            {
                Debug.LogError("[截图] 失败：" + e);
                return null;
            }
            finally
            {
                if (root != null) Object.DestroyImmediate(root);
            }
        }

        /// <summary>把一台相机渲染成一张 PNG。</summary>
        private static void RenderToFile(Camera cam, string path)
        {
            RenderTexture rt = new RenderTexture(ShotWidth, ShotHeight, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 4;

            RenderTexture prevActive = RenderTexture.active;
            RenderTexture prevTarget = cam.targetTexture;

            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;

            Texture2D tex = new Texture2D(ShotWidth, ShotHeight, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0f, 0f, ShotWidth, ShotHeight), 0, 0);
            tex.Apply(false, false);

            RenderTexture.active = prevActive;
            cam.targetTexture = prevTarget;

            File.WriteAllBytes(path, tex.EncodeToPNG());

            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }
}
