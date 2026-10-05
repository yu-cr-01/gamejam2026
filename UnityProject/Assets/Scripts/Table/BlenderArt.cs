using System.Collections.Generic;
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

        // ── 摆放：Play 中可以实时拖，满意后把默认值写回这里 ──────────────
        /// <summary>
        /// 绕 Y 轴的偏转角（度）。0 = 正对默认机位，37 左右 ≈ 正对「榨汁机特写」机位。
        /// 取 25 是"斜着放"：从默认机位看过去有透视、不像一块贴在屏幕上的板子。
        /// </summary>
        [Tooltip("立绘偏转角（度）。0=正对默认机位，37≈正对榨汁机特写")]
        public float yawDegrees = 25f;

        /// <summary>相对机身中心往后（+Z）放多少：让液体罐和飞过来的卡牌都在它前面。</summary>
        [Tooltip("立绘相对机身中心往后的距离（机身缩放前的本地单位）")]
        public float zOffset = 0.10f;

        /// <summary>整体缩放系数。1 = 按"机身高度 = 桌面到顶板 0.70"推出来的尺寸。</summary>
        [Tooltip("立绘缩放系数（1 = 按机身与顶板等高推算的尺寸）")]
        public float sizeMultiplier = 1f;

        /// <summary>true = 改回"永远正对相机"的旧行为（比较两种摆法时用）。</summary>
        [Tooltip("改成永远正对相机（旧行为）")]
        public bool faceCamera = false;

        // ── "3D 版"：按剪影挤出的厚度 ────────────────────────────────────
        /// <summary>挤出厚度（世界单位）。0.06 ≈ 机身宽度的 9%，看着像一块厚板而不是纸片。</summary>
        private const float ThicknessWorld = 0.06f;

        /// <summary>剪影网格横向格数（纵向按贴图比例算）。128 格 ≈ 每个格子 4.5 个像素。</summary>
        private const int CutoutCols = 128;

        /// <summary>
        /// 建网格时判定"这一格有内容"的 alpha 阈值。
        /// 取得比 0.5 低：让几何比可见剪影**大一圈**，边缘交给贴图自己的 alpha 收尾，
        /// 这样格子的锯齿藏在近乎全透明的地方，看不出来。
        /// </summary>
        private const float CutoutAlpha = 0.2f;

        /// <summary>侧面颜色（深色，模拟厚板的切面）。</summary>
        private static readonly Color WallColor = new Color(0.16f, 0.17f, 0.19f, 1f);

        /// <summary>背面染色：同一张图压暗，从背后看是"机器的背面"而不是镜像的正面。</summary>
        private static readonly Color BackTint = new Color(0.34f, 0.35f, 0.38f, 1f);

        private JuicerRig rig;
        private Camera cam;
        private Material wallMat;      // 子网格 0：侧面（纯深色）
        private Material backMat;      // 子网格 1：背面（同一张图染深）
        private Material frontMat;     // 子网格 2：正面（原图）
        private Texture2D[] moveFrames;
        private Texture2D idleFrame;

        private float frameT;
        private int shownIndex = -1;
        private float centerLocalY;
        private float heightWorld;
        private float widthWorld;
        private float localW;          // 立绘在父节点本地单位下的宽/高（已除掉整机缩放）
        private float localH;

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

            GameObject go = new GameObject("BlenderArt");
            go.transform.SetParent(owner.transform, false);

            // 不带碰撞体：拾取用的是全场景 Physics.Raycast（见 JuicerRig.Strip 的注释）
            MeshFilter mf = go.AddComponent<MeshFilter>();

            // 立绘的"3D 版"：按贴图 alpha 的剪影挤出一层厚度 ——
            // 正面贴原图、背面同图染深、侧面纯深色，看着像一块立在桌上的厚板而不是纸片。
            // 用四帧的**并集**剪影建一次网格：动画只换贴图，不用重建几何。
            float depthLocal = ThicknessWorld / Mathf.Max(0.0001f, owner.transform.lossyScale.y);
            mf.sharedMesh = BuildCutout(idle, moves, CutoutCols, depthLocal);

            MeshRenderer r = go.AddComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            // ★ 用 sharedMaterial 而不是 material：
            //   material 会**复制**一份材质实例出来，编辑模式下 Unity 会警告
            //   "This will leak materials into the scene"（TablePreviewCapture 就是在编辑模式跑）。
            //   这几份材质是本组件自己 new 出来的，不挂在任何资源上，改它不会污染别人。
            //
            // ★ 子网格顺序 = 绘制顺序（都是 Transparent 且不写深度）：
            //   侧面 → 背面 → 正面。正面最后画，保证从正面看永远是它盖住其它两层。
            Material wall = CardFactory.MakeUnlit(null);
            wall.color = WallColor;
            Material back = CardFactory.MakeUnlit(idle);
            back.color = BackTint;
            Material front = CardFactory.MakeUnlit(idle);
            r.sharedMaterials = new Material[] { wall, back, front };

            BlenderArt art = go.AddComponent<BlenderArt>();
            art.rig = owner;
            art.idleFrame = idle;
            art.moveFrames = moves;
            art.wallMat = wall;
            art.backMat = back;
            art.frontMat = front;
            art.Setup();
            return art;
        }

        /// <summary>
        /// 按 alpha 剪影把立绘挤出一层厚度，得到"稍微 3D"的网格。
        ///
        /// 【做法】
        ///   把贴图降采样成 cols x rows 的格子，任一帧在该格 alpha 超过阈值就算"实心"，
        ///   然后生成：正面（贴原图）+ 背面（同图，材质染深）+ 侧壁（格子边界处挤出的立面）。
        ///   三个子网格 = 三个材质，顺序就是绘制顺序（正面最后画）。
        ///
        /// 【为什么用四帧的并集】
        ///   动画只换贴图不重建网格 —— 冲压时 move_02 的机身顶部是空的，
        ///   并集网格在那里留着面片，但因为贴图也透明，什么都看不到，不会露出"半截机器"。
        ///
        /// 【为什么阈值取 0.2 而不是 0.5】
        ///   几何略大于可见剪影，边缘由贴图自己的 alpha 收边，格子的锯齿就藏在全透明区域里。
        /// </summary>
        private static Mesh BuildCutout(Texture2D idle, Texture2D[] moves, int cols, float depth)
        {
            int w = idle.width, h = idle.height;
            int rows = Mathf.Max(4, Mathf.RoundToInt(cols * (h / (float)w)));

            // 1) 采集并集剪影
            Color32[][] frames = new Color32[moves.Length + 1][];
            frames[0] = idle.GetPixels32();
            for (int i = 0; i < moves.Length; i++) frames[i + 1] = moves[i].GetPixels32();

            bool[] solid = new bool[cols * rows];
            for (int j = 0; j < rows; j++)
            {
                int py = Mathf.Clamp((int)((j + 0.5f) / rows * h), 0, h - 1);
                for (int i = 0; i < cols; i++)
                {
                    int px = Mathf.Clamp((int)((i + 0.5f) / cols * w), 0, w - 1);
                    int index = py * w + px;

                    bool on = false;
                    for (int f = 0; f < frames.Length && !on; f++)
                        on = frames[f][index].a >= CutoutAlpha * 255f;

                    solid[j * cols + i] = on;
                }
            }

            // 2) 顶点：前后两片格子网（正面在 -Z 一侧 = 朝向相机那面）
            int vx = cols + 1, vy = rows + 1;
            Vector3[] vertices = new Vector3[vx * vy * 2];
            Vector2[] uvs = new Vector2[vx * vy * 2];
            float halfZ = depth * 0.5f;

            for (int j = 0; j < vy; j++)
            {
                float v = j / (float)rows;
                for (int i = 0; i < vx; i++)
                {
                    float u = i / (float)cols;
                    int front = j * vx + i;
                    int back = vx * vy + front;

                    vertices[front] = new Vector3(u - 0.5f, v - 0.5f, -halfZ);
                    vertices[back] = new Vector3(u - 0.5f, v - 0.5f, halfZ);
                    uvs[front] = new Vector2(u, v);
                    uvs[back] = new Vector2(u, v);
                }
            }

            // 3) 索引：三个子网格
            List<int> walls = new List<int>();
            List<int> backTris = new List<int>();
            List<int> frontTris = new List<int>();

            for (int j = 0; j < rows; j++)
            {
                for (int i = 0; i < cols; i++)
                {
                    if (!solid[j * cols + i]) continue;

                    int a = j * vx + i;             // 左下
                    int b = a + 1;                  // 右下
                    int c = a + vx;                 // 左上
                    int d = c + 1;                  // 右上
                    int off = vx * vy;

                    // 正面（法线朝 -Z）
                    frontTris.Add(a); frontTris.Add(c); frontTris.Add(d);
                    frontTris.Add(a); frontTris.Add(d); frontTris.Add(b);

                    // 背面（反向绕序，法线朝 +Z）
                    backTris.Add(off + a); backTris.Add(off + d); backTris.Add(off + c);
                    backTris.Add(off + a); backTris.Add(off + b); backTris.Add(off + d);

                    // 侧壁：四邻里有空格就沿那条边挤一堵墙
                    if (i == 0 || !solid[j * cols + i - 1])          // 左
                        AddWall(walls, a, c, off, true);
                    if (i == cols - 1 || !solid[j * cols + i + 1])   // 右
                        AddWall(walls, b, d, off, false);
                    if (j == 0 || !solid[(j - 1) * cols + i])        // 下
                        AddWall(walls, a, b, off, true);
                    if (j == rows - 1 || !solid[(j + 1) * cols + i]) // 上
                        AddWall(walls, c, d, off, false);
                }
            }

            Mesh mesh = new Mesh();
            mesh.name = "BlenderArtCutout";
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.subMeshCount = 3;
            mesh.SetTriangles(walls, 0);
            mesh.SetTriangles(backTris, 1);
            mesh.SetTriangles(frontTris, 2);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// 一段侧壁：底边两个格点 (e0,e1) 在正面一侧，背面一侧是它们的 +off 副本。
        /// flip 只用来定绕序，让法线朝外（左右两侧、上下两侧的绕序刚好相反）。
        /// </summary>
        private static void AddWall(List<int> tris, int e0, int e1, int off, bool flip)
        {
            int f0 = e0, f1 = e1, b0 = off + e0, b1 = off + e1;

            if (!flip)
            {
                tris.Add(f0); tris.Add(b0); tris.Add(b1);
                tris.Add(f0); tris.Add(b1); tris.Add(f1);
            }
            else
            {
                tris.Add(f0); tris.Add(b1); tris.Add(b0);
                tris.Add(f0); tris.Add(f1); tris.Add(b1);
            }
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
            localW = widthWorld / scale;
            localH = heightWorld / scale;

            cam = Camera.main;
            if (cam == null) cam = Object.FindObjectOfType<Camera>();

            Place();
            Show(0, true);
        }

        private void LateUpdate()
        {
            // 每帧套一次摆放：Inspector 里拖 yawDegrees / zOffset / sizeMultiplier 立即生效
            Place();
            UpdateFrame();
        }

        /// <summary>
        /// 摆位。默认是**固定角度**（斜着立在桌上，不跟相机转）；
        /// faceCamera = true 时退回"永远正对相机 + 往相机远侧推"的旧行为。
        /// </summary>
        private void Place()
        {
            // 缩放每次重算，sizeMultiplier 才能在 Play 中实时拖
            float k = sizeMultiplier > 0.0001f ? sizeMultiplier : 0.0001f;
            transform.localScale = new Vector3(localW * k, localH * k, 1f);

            if (!faceCamera)
            {
                transform.localRotation = Quaternion.Euler(0f, yawDegrees, 0f);
                transform.localPosition = new Vector3(0f, centerLocalY, zOffset);
                return;
            }

            if (cam == null) return;

            Vector3 fwd = cam.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude <= 0.0001f) return;

            fwd.Normalize();
            transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);

            Vector3 anchor = transform.parent != null
                ? transform.parent.TransformPoint(new Vector3(0f, centerLocalY, zOffset))
                : new Vector3(0f, centerLocalY, zOffset);
            transform.position = anchor - fwd * zOffset;
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
            if (frontMat == null) return;
            if (!force && moveIndex == shownIndex) return;

            shownIndex = moveIndex;
            Texture2D tex = moveIndex < 0 ? idleFrame : moveFrames[moveIndex];

            // 同上：走 sharedMaterial，别在编辑模式下复制材质。
            // 正面和背面都要换 —— 并集网格里"这一帧没有内容"的格子靠背面的 alpha 一起透明，
            // 只换正面的话，冲压到 move_02 时会在机器顶部露出一块深色背面。
            frontMat.mainTexture = tex;
            if (backMat != null) backMat.mainTexture = tex;
        }
    }
}
