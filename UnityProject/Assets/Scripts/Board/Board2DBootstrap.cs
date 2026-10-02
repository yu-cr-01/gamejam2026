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

            // 棋盘是纯 2D 的，场景自带的主相机只会露个天空盒出来，先关掉
            Camera[] cams = Object.FindObjectsOfType<Camera>();
            for (int i = 0; i < cams.Length; i++) cams[i].enabled = false;
            Light[] lights = Object.FindObjectsOfType<Light>();
            for (int i = 0; i < lights.Length; i++) lights[i].enabled = false;

            GameObject go = new GameObject("[Board2D]");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<Board2DView>();

            Debug.Log("[Board2D] 2D 棋盘原型已启动。点手牌打出，B/空格 播放破壁机动画。");
        }
    }
}
