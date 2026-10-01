using UnityEngine;

namespace GameJam.Prototype
{
    /// <summary>
    /// 3D 桌面原型的启动器。
    ///
    /// 和文字流程原型一样用 RuntimeInitializeOnLoadMethod ——
    /// 新建空场景按 Play 就能跑，不需要摆物体、不需要做场景文件。
    ///
    /// 两套原型靠 PrototypeMode.Current 二选一，不会同时启动。
    /// </summary>
    public static class TablePrototypeBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (PrototypeMode.Current != PrototypeMode.Kind.Table3D) return;

            // 场景里已经手动挂了就不再重复创建
            if (Object.FindObjectOfType<TableSetup>() != null) return;

            GameObject go = new GameObject("[TablePrototype]");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<TableSetup>();

            Debug.Log("[Table3D] 桌面原型已启动。拖动卡牌放上桌，1/2/3 切视角，G 开关物理。");
        }
    }
}
