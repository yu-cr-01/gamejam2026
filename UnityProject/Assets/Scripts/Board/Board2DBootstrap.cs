using UnityEngine;

namespace GameJam.Prototype
{
    /// <summary>
    /// 2D 棋盘原型的启动器。
    ///
    /// 和另外两套一样用 RuntimeInitializeOnLoadMethod ——
    /// 新建空场景按 Play 就能跑，不需要摆物体、不需要做场景文件。
    ///
    /// 三套原型靠 PrototypeMode.Current 三选一，不会同时启动。
    /// Table3D 和 Board2D 是同一个玩法的两种表现形式，共用同一份配置表。
    /// </summary>
    public static class Board2DBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (PrototypeMode.Current != PrototypeMode.Kind.Board2D) return;

            if (Object.FindObjectOfType<Board2DView>() != null) return;

            // ★ 不能把所有相机都 disable 掉。
            //   一个启用的相机都没有时，Unity 会在 Game 视图正中打出
            //   "Display 1 / No cameras rendering"，正好压在棋盘中间，很显眼。
            //   正确做法是**留一台启用但什么都不渲染**的相机：
            //   cullingMask 清成 0 + 纯色清屏，它只负责把背景刷成深色。
            Camera cam = Camera.main;
            if (cam == null)
            {
                GameObject camGo = new GameObject("Board2DBackgroundCamera");
                Object.DontDestroyOnLoad(camGo);
                cam = camGo.AddComponent<Camera>();
                cam.tag = "MainCamera";
            }

            cam.enabled = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.075f, 0.082f, 0.098f);
            cam.cullingMask = 0;            // 什么都不渲染，只清屏
            cam.orthographic = true;        // 别让它因为透视参数报什么警告

            // 其余相机（如果有第二台）才关掉，避免两套渲染叠在一起
            Camera[] cams = Object.FindObjectsOfType<Camera>();
            for (int i = 0; i < cams.Length; i++)
                if (cams[i] != null && cams[i] != cam) cams[i].enabled = false;

            Light[] lights = Object.FindObjectsOfType<Light>();
            for (int i = 0; i < lights.Length; i++) lights[i].enabled = false;

            GameObject go = new GameObject("[Board2D]");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<Board2DView>();

            Debug.Log("[Board2D] 2D 棋盘原型已启动。点手牌打出，B/空格 播放破壁机动画。");
        }
    }
}
