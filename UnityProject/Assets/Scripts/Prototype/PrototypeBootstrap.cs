using UnityEngine;

namespace GameJam.Prototype
{
    /// <summary>
    /// 让原型在**任意场景**里都能跑起来。
    ///
    /// 为什么用 RuntimeInitializeOnLoadMethod 而不是"往场景里摆一个 GameObject"：
    ///   1. 工程里不需要预先做好 .unity 场景文件 —— 新建空场景按 Play 就能跑
    ///   2. 场景文件是 YAML，多人同时改极易冲突，能不碰就不碰
    ///   3. 游戏逻辑与场景解耦，将来换成正式 UI 时这份代码直接删掉即可
    ///
    /// 这一份只负责 Flow2D（IMGUI 文字流程原型）；
    /// 3D 桌面原型由 TablePrototypeBootstrap 负责。
    /// 两套靠 PrototypeMode.Current 二选一。
    ///
    /// 如果你更想手动控制，把下面整个方法注释掉，
    /// 然后在场景里新建空物体、挂上 GameFlow 组件即可。
    /// </summary>
    public static class PrototypeBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (PrototypeMode.Current != PrototypeMode.Kind.Flow2D) return;

            // 场景里已经手动挂了就不再重复创建
            if (Object.FindObjectOfType<GameFlow>() != null) return;

            // 留一台"什么都不渲染"的相机专门刷背景色。
            // 一台相机都不留的话，Game 视图正中会打出 "No cameras rendering"，
            // 而且空场景的天空盒会从 IMGUI 面板背后透出来 —— 蓝白色调很出戏。
            Camera cam = Camera.main;
            if (cam == null)
            {
                GameObject camGo = new GameObject("Flow2DBackgroundCamera");
                Object.DontDestroyOnLoad(camGo);
                cam = camGo.AddComponent<Camera>();
                cam.tag = "MainCamera";
            }
            cam.enabled = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.075f, 0.082f, 0.098f);
            cam.cullingMask = 0;      // 只负责清屏，不渲染任何物体
            cam.orthographic = true;
            cam.depth = -100;

            Camera[] cams = Object.FindObjectsOfType<Camera>();
            for (int i = 0; i < cams.Length; i++)
                if (cams[i] != null && cams[i] != cam) cams[i].enabled = false;

            GameObject go = new GameObject("[GameFlow]");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<GameFlow>();
        }
    }
}
