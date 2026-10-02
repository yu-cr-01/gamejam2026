using System.Collections.Generic;
using UnityEngine;
using GameJam.Data;

namespace GameJam.Prototype
{
    /// <summary>
    /// 程序化占位美术 —— 全部用代码算出来，不依赖任何外部图片。
    ///
    /// 【为什么不用网上下的免费素材】
    ///   1. 没有版权和署名问题，jam 拿去投稿不用查许可
    ///   2. 不用管导入设置、图集、压缩格式，改一行参数重新 Play 就变
    ///   3. **它占的正是将来美术要占的坑** —— 卡面构图、色带位置、
    ///      图标框、卡槽角标的尺寸都定好了，美术只要把 PNG 换进去即可
    ///
    /// 【换成正式美术时怎么改】
    ///   把 CardFace / SlotFrame / TableSurface 三个方法的返回值换成
    ///   Resources.Load&lt;Texture2D&gt;("Art/xxx") 就行，调用方一行都不用动。
    ///   尺寸约定写在下面几个常量里。
    /// </summary>
    public static class ProceduralArt
    {
        // ── 尺寸约定（美术出图按这个比例出）────────────────────────────
        /// <summary>卡面贴图宽（对应世界尺寸 0.24）</summary>
        public const int CardTexW = 256;
        /// <summary>卡面贴图高（对应世界尺寸 0.335）</summary>
        public const int CardTexH = 358;

        // 卡面版面（V 从下往上；v=1 是远端，也就是屏幕上方向）
        private const float BandBottom  = 0.780f;   // 名字色带的底边
        private const float IconBottom  = 0.300f;   // 图标占位框的底边
        private const float IconTop     = 0.700f;   // 图标占位框的顶边
        private const float IconHalfW   = 0.290f;   // 图标占位框半宽（占卡宽比例）

        // ── 配色 ──────────────────────────────────────────────────────
        private static readonly Color Paper    = new Color(0.930f, 0.912f, 0.866f);
        private static readonly Color PaperDim = new Color(0.858f, 0.836f, 0.786f);
        private static readonly Color CardEdge = new Color(0.130f, 0.118f, 0.104f);

        /// <summary>12 色食材调色板。同一 id 永远拿到同一个颜色。</summary>
        private static readonly Color[] Palette =
        {
            new Color(0.878f, 0.376f, 0.227f),   // 橙红
            new Color(0.910f, 0.639f, 0.239f),   // 橙黄
            new Color(0.851f, 0.780f, 0.310f),   // 黄
            new Color(0.561f, 0.749f, 0.290f),   // 黄绿
            new Color(0.310f, 0.639f, 0.420f),   // 绿
            new Color(0.275f, 0.659f, 0.627f),   // 青
            new Color(0.290f, 0.529f, 0.769f),   // 蓝
            new Color(0.431f, 0.435f, 0.769f),   // 靛
            new Color(0.604f, 0.396f, 0.769f),   // 紫
            new Color(0.761f, 0.369f, 0.620f),   // 品红
            new Color(0.690f, 0.541f, 0.416f),   // 棕
            new Color(0.549f, 0.561f, 0.596f),   // 灰
        };

        // ── 缓存 ──────────────────────────────────────────────────────
        private static readonly Dictionary<int, Texture2D> faceCache = new Dictionary<int, Texture2D>();
        private static Texture2D slotFrameTex;
        private static Texture2D tableTex;

        // ══════════════════════════════════════════════════════════════
        //  食材配色
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 按 id 稳定分配一个颜色。用 FNV-1a 哈希，同一个 id 每次跑都是同一个色，
        /// 但不同的 id 会散开到不同色相上 —— 玩家靠颜色就能认出是哪张牌。
        /// </summary>
        public static Color IngredientColor(string id)
        {
            if (string.IsNullOrEmpty(id)) return Palette[Palette.Length - 1];
            int h = StableHash(id);
            int i = h % Palette.Length;
            if (i < 0) i += Palette.Length;
            return Palette[i];
        }

        /// <summary>在给定底色上选一个能看清的字色（深底配白字，浅底配墨字）。</summary>
        public static Color InkOn(Color bg)
        {
            float lum = 0.299f * bg.r + 0.587f * bg.g + 0.114f * bg.b;
            return lum > 0.58f ? CardEdge : new Color(0.985f, 0.975f, 0.955f);
        }

        // ══════════════════════════════════════════════════════════════
        //  卡面
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 生成一张卡面贴图。
        ///
        /// 版面（从下往上）：
        ///   0.00 - 0.26  属性区（纸色压暗一点，和上面拉开层次）
        ///   0.30 - 0.70  **图标占位框** —— 将来食材插画放这里
        ///   0.78 - 1.00  **名字色带** —— 食材主题色，名字用对比色写在上面
        ///
        /// 图标框里画一个几何印记，按"三属性里最高的那个"取形状：
        ///   盐性 H → 方形（固体）　汞性 D → 圆形（液体）　硫性 V → 菱形（气体）
        /// 这样不看字也能一眼分出这张牌偏哪一路。
        /// </summary>
        public static Texture2D CardFace(Color accent, AttrId dominant)
        {
            int key = (Mathf.RoundToInt(accent.r * 255f) << 16)
                    | (Mathf.RoundToInt(accent.g * 255f) << 8)
                    |  Mathf.RoundToInt(accent.b * 255f);
            key = key * 4 + (int)dominant;

            Texture2D cached;
            if (faceCache.TryGetValue(key, out cached) && cached != null) return cached;

            int W = CardTexW, H = CardTexH;
            Color32[] px = new Color32[W * H];

            float halfW = W * 0.5f;
            float halfH = H * 0.5f;

            Color bandDark = accent * 0.72f; bandDark.a = 1f;
            Color iconFill = Color.Lerp(Paper, accent, 0.16f);

            // 图标框在像素空间的范围
            float iconCx = 0f;
            float iconCy = (IconBottom + IconTop) * 0.5f * H - halfH;
            float iconHalfW = IconHalfW * W;
            float iconHalfH = (IconTop - IconBottom) * 0.5f * H;

            for (int y = 0; y < H; y++)
            {
                float py = (y + 0.5f) - halfH;
                float v = (y + 0.5f) / H;

                for (int x = 0; x < W; x++)
                {
                    float px_ = (x + 0.5f) - halfW;

                    // ① 卡片外形：圆角矩形，外面全透明
                    float dCard = RoundRectSDF(px_, py, halfW - 3f, halfH - 3f, 20f);
                    float cardA = Mathf.Clamp01(0.5f - dCard);
                    if (cardA <= 0.002f) { px[y * W + x] = new Color32(0, 0, 0, 0); continue; }

                    // ② 底：整张纸色，下半属性区压暗
                    Color c = v < 0.26f ? PaperDim : Paper;

                    // ③ 名字色带
                    if (v >= BandBottom)
                    {
                        c = accent;
                        // 色带底部压一道深色，和纸面分开
                        if (v < BandBottom + 0.018f) c = bandDark;
                    }

                    // ④ 图标占位框
                    float dIcon = RoundRectSDF(px_ - iconCx, py - iconCy, iconHalfW, iconHalfH, 14f);
                    if (dIcon < 0f)
                    {
                        c = iconFill;

                        // 框内几何印记（描边）
                        float markR = Mathf.Min(iconHalfW, iconHalfH) * 0.52f;
                        float dMark;
                        switch (dominant)
                        {
                            case AttrId.Mercury: dMark = Mathf.Abs(new Vector2(px_ - iconCx, py - iconCy).magnitude - markR); break;
                            case AttrId.Sulfur:  dMark = Mathf.Abs(Mathf.Abs(px_ - iconCx) + Mathf.Abs(py - iconCy) - markR * 1.25f); break;
                            default:             dMark = Mathf.Abs(RoundRectSDF(px_ - iconCx, py - iconCy, markR * 0.82f, markR * 0.82f, markR * 0.22f)); break;
                        }
                        if (dMark < 2.2f) c = Color.Lerp(c, accent, 0.85f);
                    }

                    // ⑤ 图标框描边（虚线感：按坐标做棋盘式间断，一眼看出"这里待填图"）
                    float dIconEdge = Mathf.Abs(dIcon) - 1.6f;
                    if (dIconEdge < 0f)
                    {
                        int dash = ((x / 9) + (y / 9)) % 2;
                        if (dash == 0) c = Color.Lerp(c, accent, 0.9f);
                    }

                    // ⑥ 卡片外描边
                    float dEdge = Mathf.Abs(dCard) - 1.4f;
                    if (dEdge < 0f) c = Color.Lerp(c, CardEdge, 0.55f);

                    // ⑦ 纸面细微纹理，避免纯色死板
                    float n = Mathf.PerlinNoise(x * 0.11f, y * 0.11f) - 0.5f;
                    c.r += n * 0.020f; c.g += n * 0.020f; c.b += n * 0.020f;

                    c.a = cardA;
                    px[y * W + x] = c;
                }
            }

            Texture2D t = NewTex(W, H);
            t.SetPixels32(px);
            t.Apply(false, false);

            faceCache[key] = t;
            return t;
        }

        // ══════════════════════════════════════════════════════════════
        //  卡槽角标
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 卡槽标记贴图 —— **只画四个角标，不是一整圈边框**。
        ///
        /// 之前用的是不透明方块，视觉上像桌面被贴了八张贴纸，很抢戏。
        /// 现在只在四个角画 L 形短角标（UI 里表示"可放置区域"的通用做法），
        /// 中间和四周全透明，卡放上去之后角标基本被盖住，不干扰画面。
        ///
        /// 【贴图只出白色】
        /// 颜色由材质 .color 决定 —— TableInteraction 拖动时会把它染成
        /// 灰蓝（平时）或绿色（悬停到可用槽）。贴图只负责形状和透明度，
        /// 这样"哪一段是角标"和"角标什么颜色"两件事互不干扰。
        /// </summary>
        public static Texture2D SlotFrame()
        {
            if (slotFrameTex != null) return slotFrameTex;

            int W = CardTexW, H = CardTexH;
            Color32[] px = new Color32[W * H];

            const float margin = 16f;   // 距卡片边缘
            const float bracket = 62f;  // 角标每条边的长度
            const float stroke = 2.4f;  // 角标粗细

            float halfW = W * 0.5f - margin;
            float halfH = H * 0.5f - margin;

            for (int y = 0; y < H; y++)
            {
                float py = (y + 0.5f) - H * 0.5f;
                for (int x = 0; x < W; x++)
                {
                    float pxx = (x + 0.5f) - W * 0.5f;

                    // 到圆角矩形轮廓的距离；只保留轮廓附近
                    float d = Mathf.Abs(RoundRectSDF(pxx, py, halfW, halfH, 14f));
                    float a = 0f;

                    if (d < stroke)
                    {
                        // 只留下靠近四个角的那几段轮廓 —— 整圈边框于是变成四个 L 形角标
                        bool nearCornerX = Mathf.Abs(pxx) > halfW - bracket;
                        bool nearCornerY = Mathf.Abs(py) > halfH - bracket;

                        if (nearCornerX && nearCornerY)
                            a = Mathf.Clamp01(stroke - d) * 0.90f;
                    }

                    px[y * W + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }

            slotFrameTex = NewTex(W, H);
            slotFrameTex.SetPixels32(px);
            slotFrameTex.Apply(false, false);
            return slotFrameTex;
        }

        // ══════════════════════════════════════════════════════════════
        //  桌面木纹
        // ══════════════════════════════════════════════════════════════

        /// <summary>程序化木纹：沿 X 拉长的条带 + 两层噪声扰动。</summary>
        public static Texture2D TableSurface()
        {
            if (tableTex != null) return tableTex;

            const int W = 512, H = 512;
            Color32[] px = new Color32[W * H];

            Color dark  = new Color(0.150f, 0.108f, 0.082f);
            Color light = new Color(0.252f, 0.190f, 0.142f);

            for (int y = 0; y < H; y++)
            {
                float v = (float)y / H;
                for (int x = 0; x < W; x++)
                {
                    float u = (float)x / W;

                    // 长条纹理：X 方向频率低、Y 方向频率高
                    float grain = Mathf.PerlinNoise(u * 2.6f, v * 34f);
                    float fine  = Mathf.PerlinNoise(u * 31f + 7.3f, v * 96f + 2.1f);
                    float t = Mathf.Clamp01(grain * 0.72f + fine * 0.28f);

                    // 再叠一层很淡的大块明暗，避免整张桌子一个调子
                    t *= 0.88f + 0.24f * Mathf.PerlinNoise(u * 1.4f + 19f, v * 1.4f + 5f);

                    Color c = Color.Lerp(dark, light, Mathf.Clamp01(t));
                    px[y * W + x] = c;
                }
            }

            tableTex = NewTex(W, H);
            tableTex.wrapMode = TextureWrapMode.Repeat;
            tableTex.SetPixels32(px);
            tableTex.Apply(false, false);
            return tableTex;
        }

        // ══════════════════════════════════════════════════════════════
        //  工具
        // ══════════════════════════════════════════════════════════════

        /// <summary>圆角矩形的有符号距离场：&lt;0 在内部，&gt;0 在外部，绝对值就是到轮廓的距离。</summary>
        private static float RoundRectSDF(float px, float py, float halfW, float halfH, float radius)
        {
            float r = Mathf.Min(radius, Mathf.Min(halfW, halfH));
            float qx = Mathf.Abs(px) - (halfW - r);
            float qy = Mathf.Abs(py) - (halfH - r);
            float ax = Mathf.Max(qx, 0f);
            float ay = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ax * ax + ay * ay) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        /// <summary>FNV-1a —— 比 string.GetHashCode() 稳，跨运行、跨平台结果一致。</summary>
        private static int StableHash(string s)
        {
            unchecked
            {
                uint h = 2166136261u;
                for (int i = 0; i < s.Length; i++)
                {
                    h ^= s[i];
                    h *= 16777619u;
                }
                return (int)(h & 0x7FFFFFFF);
            }
        }

        private static Texture2D NewTex(int w, int h)
        {
            Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            t.anisoLevel = 2;
            return t;
        }
    }
}
