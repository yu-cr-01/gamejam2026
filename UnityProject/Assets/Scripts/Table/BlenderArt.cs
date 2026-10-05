using UnityEngine;

namespace GameJam.Prototype
{
    /// <summary>
    /// 破壁机立绘 —— 美术（nicole）2026-10-04 交付的 gameblender 序列。
    ///
    /// 【美术给的使用说明】（原话在 docs/art/UI_REs1004/gameblender 的截图里）
    ///   「静止时使用 normal ｜ 动画时使用 01/02/03 循环播放 帧率 10 帧/秒」
    ///   所以：不冲压时贴 normal，`JuicerRig.IsStamping` 为真时按 10 帧/秒循环三张 move 帧。
    ///
    /// 【为什么要立一块 Quad】
    ///   交的是整机 2D 立绘（574x672，机身占 y 93~671），不是 3D 模型贴图，
    ///   所以它顶掉的是**程序搭的那台机器**：挂上立绘后 JuicerRig 会把
    ///   Frame（四柱/顶板/模具板/进料口）和 Press（冲头/残渣/刀片）整组关掉，
    ///   只留液体罐和刻度 —— 罐子上的液面就是得分面板，那个不能盖。
    ///   （想改回去：JuicerRig.HideProceduralMachineWhenArtPresent = false）
    ///
    /// 【对位是怎么算的】
    ///   立绘里机身高度 578 像素，映射到"桌面 → 顶板"的 0.70 世界单位（JuicerRig.TopPlateY），
    ///   于是 1 像素 = 0.001211 世界单位；整图 574x672 → 0.695 x 0.814 世界单位，
    ///   底边贴桌面（y=0），中心抬到 0.407。立绘在整机节点下，再乘 0.75 的整体缩放。
    ///
    /// 【朝向 / 前后】
    ///   立绘是 2D 的，绕 Y 轴始终面向相机（这台机器允许玩家小幅转头），
    ///   并沿相机视线往远侧推 0.22 —— 保证它**永远在液体罐和飞过来的卡牌后面**，
    ///   不会挡住得分液面，也不会挡住卡牌落点。
    /// </summary>
    public class BlenderArt : MonoBehaviour
    {
        public const string ArtRoot = "Art/Juicer/";

        /// <summary>静止帧 / 动画帧（美术交付的文件名）。</summary>
        public const string IdleFrameName = "gameblender_normal_img";
        public static readonly string[] MoveFrameNames =
        {
            "gameblender_move_01_img",
            "gameblender_move_02_img",
            "gameblender_move_03_img",
        };

        /// <summary>美术指定的播放帧率。</summary>
        public const float FrameRate = 10f;

        // 立绘对位（世界单位）
        private const float MachineWorldHeight = 0.70f;    // 机身高度 = 桌面到顶板
        private const int   MachinePixelsTop   = 93;       // 立绘里机身的顶
        private const int   MachinePixelsBottom = 671;     // 立绘里机身的底（贴到图的下边缘）
        private const int   ArtPixelsW = 574;
        private const int   ArtPixelsH = 672;

        private const float PushBackWorld = 0.22f;         // 往相机远侧推的距离
        private const float ArtZ = 0f;                     // 基准面（整机中心面）

        private JuicerRig rig;
        private Camera cam;
        private Renderer quad;
        private Texture2D[] moveFrames;
        private Texture2D idleFrame;

        private float frameT;
        private int shownIndex = -1;
        private float centerLocalY;
        private float heightWorld;
        private float widthWorld;

        /// <summary>
        /// 有美术就挂一块立绘并返回组件；缺任何一张图就返回 null（调用方保持纯程序化）。
        /// </summary>
        public static BlenderArt Attach(JuicerRig owner)
        {
            if (owner == null) return null;

            Texture2D idle = Resources.Load<Texture2D>(ArtRoot + IdleFrameName);
            if (idle == null)
            {
                Debug.Log("[BlenderArt] 没有 " + ArtRoot + IdleFrameName + "，破壁机继续用程序化造型。");
                return null;
            }

            Texture2D[] moves = new Texture2D[MoveFrameNames.Length];
            for (int i = 0; i < MoveFrameNames.Length; i++)
            {
                moves[i] = Resources.Load<Texture2D>(ArtRoot + MoveFrameNames[i]);
                if (moves[i] == null)
                {
                    Debug.LogWarning("[BlenderArt] 缺动画帧 " + ArtRoot + MoveFrameNames[i] +
                                     "，破壁机继续用程序化造型。");
                    return null;
                }
            }

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "BlenderArt";
            go.transform.SetParent(owner.transform, false);

            // 拾取用的是全场景 Physics.Raycast（见 JuicerRig.Strip 的注释），立绘不能带碰撞体
            Collider col = go.GetComponent<Collider>();
            if (col != null) CardFactory.DestroySafe(col);

            Renderer r = go.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            // ★ 用 sharedMaterial 而不是 material：
            //   material 会**复制**一份材质实例出来，编辑模式下 Unity 会警告
            //   "This will leak materials into the scene"（TablePreviewCapture 就是在编辑模式跑）。
            //   这份材质是本组件自己 new 出来的，不挂在任何资源上，改它不会污染别人。
            r.sharedMaterial = CardFactory.MakeUnlit(idle);

            BlenderArt art = go.AddComponent<BlenderArt>();
            art.rig = owner;
            art.idleFrame = idle;
            art.moveFrames = moves;
            art.quad = r;
            art.Setup();
            return art;
        }

        private void Setup()
        {
            float scale = 0.75f;
            Vector3 ls = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
            if (Mathf.Abs(ls.y) > 0.0001f) scale = ls.y;

            float pixelsPerWorld = (MachinePixelsBottom - MachinePixelsTop) / MachineWorldHeight;
            heightWorld = ArtPixelsH / pixelsPerWorld;
            widthWorld  = ArtPixelsW / pixelsPerWorld;

            // 立绘底边贴桌面（机身底 = 图的下边缘），所以中心在半个图高处
            centerLocalY = (heightWorld * 0.5f) / scale;

            transform.localScale = new Vector3(widthWorld / scale, heightWorld / scale, 1f);

            cam = Camera.main;
            if (cam == null) cam = Object.FindObjectOfType<Camera>();

            Show(0, true);
        }

        private void LateUpdate()
        {
            // 2D 立绘永远面向相机，并推到相机远侧 —— 保证不挡罐子和卡牌
            if (cam != null)
            {
                Vector3 fwd = cam.transform.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude > 0.0001f)
                {
                    fwd.Normalize();
                    transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);

                    Vector3 anchor = transform.parent != null
                        ? transform.parent.TransformPoint(new Vector3(0f, centerLocalY, ArtZ))
                        : new Vector3(0f, centerLocalY, ArtZ);
                    transform.position = anchor - fwd * PushBackWorld;
                }
            }

            UpdateFrame();
        }

        private void UpdateFrame()
        {
            if (rig != null && rig.IsStamping)
            {
                // 冲压中：01/02/03 循环，10 帧/秒
                frameT += Time.deltaTime * FrameRate;
                int idx = Mathf.FloorToInt(frameT) % moveFrames.Length;
                Show(idx, false);
            }
            else
            {
                frameT = 0f;
                Show(-1, false);
            }
        }

        private void Show(int moveIndex, bool force)
        {
            if (quad == null) return;
            if (!force && moveIndex == shownIndex) return;

            shownIndex = moveIndex;
            // 同上：走 sharedMaterial，别在编辑模式下复制材质
            quad.sharedMaterial.mainTexture = moveIndex < 0 ? idleFrame : moveFrames[moveIndex];
        }
    }
}
