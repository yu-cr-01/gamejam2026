using UnityEngine;

namespace GameJam.Prototype
{
    /// <summary>
    /// 卡面上那**三个数字**的"按形状自适应"—— 它要**晚一帧**才做，理由只有一条：
    ///
    ///   **TextMesh 的网格不是在 AddComponent 那一帧就生成出来的。**
    ///   实测（副本工程跑 DSH_CARDFACEPROBE）：刚 AddComponent + 设完 text 就去读
    ///   `MeshFilter.sharedMesh`，顶点是空的 —— 于是"数字能塞多大"算不出来，
    ///   三个数字全落回保底字号 0.0042（日志里那一行
    ///   "量不到数字的墨迹盒（TextMesh 网格为空）"就是它）；而下一帧再读就有了。
    ///
    /// 所以这个组件只干一件事：每帧问一次 <see cref="CardFactory.FitStatNumbers"/>，
    /// 量到了就收工（`applied`）。最多试 <see cref="MaxTries"/> 帧 ——
    /// 试完还量不到就留在"保底字号"上（那个档肯定不压形状边框），并且**只吵一次**。
    ///
    /// 【为什么三个数字由一个组件一起管，而不是每个数字挂一个】
    ///   三个数字要**一起定字号**（基准 = 三个格里最紧的一格装一位数时的大小，
    ///   见 CardFactory.FitStatNumbers ②）。各挂一个的话，三个组件谁先量到网格谁先算 ——
    ///   先算的那个会拿一个只由自己决定的字号，和后来那两个对不上。
    ///   一个组件、一次调用、三个数字一起定，是这件事唯一不会出错的做法。
    ///
    /// [ExecuteInEditMode] 是为了编辑模式下那套截图（TablePreviewCapture 走 Camera.Render）：
    ///   它不跑 Play、没有 Update 循环，加了之后编辑器的 update 也会调到这里。
    ///   组件只读自己的网格、只改自己的 transform，幂等、无副作用。
    /// </summary>
    [ExecuteInEditMode]
    public class CardStatNumberFit : MonoBehaviour
    {
        /// <summary>三个数字的文字对象，顺序 = H / D / V（见 <see cref="CardArt.StatSlots"/>）。</summary>
        public GameObject[] numbers;

        /// <summary>文字离卡面的高度（跟着卡厚走，见 CardFactory.AddText 的 localY）。</summary>
        public float textY;

        /// <summary>最多等多少帧。12 帧 ≈ 0.2 秒（60fps），足够跨过"网格还没建出来"那一段。</summary>
        private const int MaxTries = 12;

        private bool applied;
        private int  tries;

        private void Update()
        {
            if (applied) return;

            if (CardFactory.FitStatNumbers(numbers, textY))
            {
                applied = true;
                return;
            }

            if (++tries < MaxTries) return;

            applied = true;

            // 只在播放模式吵：编辑模式的截图不跑 Play，量不到是常态，不该刷日志
            if (Application.isPlaying)
                Debug.LogWarning("[CardFactory] 卡面三个数字等了 " + MaxTries
                                 + " 帧也没等到 TextMesh 网格，留在保底字号 —— 数字会略小、略偏上。");
        }
    }
}
