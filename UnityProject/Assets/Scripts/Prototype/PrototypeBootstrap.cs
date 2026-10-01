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
    /// 如果你更想手动控制，把下面整个方法注释掉，
    /// 然后在场景里新建空物体、挂上 GameFlow 组件即可。
    /// </summary>
    public static class PrototypeBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            // 场景里已经手动挂了就不再重复创建
            if (Object.FindObjectOfType<GameFlow>() != null) return;

            GameObject go = new GameObject("[GameFlow]");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<GameFlow>();
        }
    }
}
