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
        private static readonly Dictionary<int, Texture2D> emblemCache = new Dictionary<int, Texture2D>();
        private static Texture2D slotFrameTex;
        private static Texture2D tableTex;
        private static Texture2D panelTex;

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

        /// <summary>
        /// 变速模块的配色。
        ///
        /// 食材走的是全色相的中低饱和色，模块统一压进冷色区（青 → 蓝紫），
        /// 桌上一眼就能分出"这张不是食材"，不用去读名字。
        ///
        /// 仍然拿 id 哈希当色相来源、而不是写死一个蓝色：
        /// 一局里手上可能同时有两三个模块，全一个颜色就完全分不出来了。
        /// </summary>
        public static Color ModuleColor(string id)
        {
            Color c = IngredientColor(id);

            float h, s, v;
            Color.RGBToHSV(c, out h, out s, out v);

            // 把原来的色相折进 0.50..0.72 这一段，保留各自的差异
            h = Mathf.Lerp(0.50f, 0.72f, Mathf.Repeat(h * 3.7f, 1f));
            s = Mathf.Lerp(s, 0.42f, 0.35f);   // 收一点饱和度，更像金属件
            v = Mathf.Lerp(v, 0.60f, 0.30f);

            return Color.HSVToRGB(h, s, v);
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

        // ══════════════════════════════════════════════════════════════
        //  实心圆
        // ══════════════════════════════════════════════════════════════

        private static Texture2D discTex;

        /// <summary>
        /// 实心圆，边缘留 1.5 像素过渡。
        ///
        /// RadialGlow 是"中心亮、往外淡出"的辉光，拿来当面片会糊成一团；
        /// 杯内的食材粒子和杯子本身要的是**实心**的圆，所以单独出一张。
        /// 想换颜色 / 大小就用 GUI.color 和绘制矩形，不用重新生成贴图。
        /// </summary>
        public static Texture2D Disc()
        {
            if (discTex != null) return discTex;

            const int S = 128;
            discTex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            discTex.hideFlags = HideFlags.HideAndDontSave;

            float r = S * 0.5f - 1f;
            float cx = S * 0.5f, cy = S * 0.5f;

            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    float dx = x + 0.5f - cx;
                    float dy = y + 0.5f - cy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);

                    float a = Mathf.Clamp01((r - d) / 1.5f);   // 硬边太扎眼，留一点过渡
                    discTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }

            discTex.Apply();
            discTex.wrapMode = TextureWrapMode.Clamp;
            return discTex;
        }

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
                        float dMark = ShapeSDF(px_ - iconCx, py - iconCy, dominant, markR);
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
        //  窗口底图
        // ══════════════════════════════════════════════════════════════

        /// <summary>窗口底图按 9 宫格拉伸时的边距。</summary>
        public const int PanelBorder = 8;

        /// <summary>
        /// 窗口 / 面板底图：深色填充 + 一圈浅描边 + 圆角。
        ///
        /// 只做 32×32 就够了 —— 配 GUIStyle.border = 8 走 **9 宫格拉伸**，
        /// 四个角的圆角原样保留，中间随窗口大小自由拉伸，拉多大都不糊。
        /// 这就是 NGUI / UGUI 里那些 panel 贴图的标准做法。
        ///
        /// 底色调深而不是浅：HUD 的文字样式本来就是给深底配的浅色字，
        /// 换成浅底图那些字会直接糊掉看不见。
        /// </summary>
        public static Texture2D PanelBackdrop()
        {
            if (panelTex != null) return panelTex;

            const int S = 32;
            Color32[] px = new Color32[S * S];

            Color fill = new Color(0.112f, 0.126f, 0.152f);
            Color edge = new Color(0.400f, 0.455f, 0.545f);

            float half = S * 0.5f;

            for (int y = 0; y < S; y++)
            {
                float py = (y + 0.5f) - half;
                for (int x = 0; x < S; x++)
                {
                    float pxx = (x + 0.5f) - half;
                    float d = RoundRectSDF(pxx, py, half - 0.5f, half - 0.5f, 6f);

                    Color c = (d > -1.3f) ? edge : fill;   // 靠近轮廓的一圈画成描边色
                    c.a = Mathf.Clamp01(0.5f - d);         // 圆角外自然透明，边缘抗锯齿

                    px[y * S + x] = c;
                }
            }

            panelTex = NewTex(S, S);
            panelTex.SetPixels32(px);
            panelTex.Apply(false, false);
            return panelTex;
        }

        // ══════════════════════════════════════════════════════════════
        //  拉丝金属
        // ══════════════════════════════════════════════════════════════

        private static Texture2D metalTex;

        /// <summary>
        /// 拉丝金属：沿一个方向的高频细纹 + 大块明暗。
        /// 榨汁机机身的圆柱侧面 UV 是环向的，所以竖纹贴上去正好是"环向拉丝"。
        /// </summary>
        public static Texture2D BrushedMetal()
        {
            if (metalTex != null) return metalTex;

            const int S = 256;
            Color32[] px = new Color32[S * S];

            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    float u = (float)x / S;
                    float v = (float)y / S;

                    // 高频细纹（沿 U 变化）
                    float brush = Mathf.PerlinNoise(u * 240f, v * 4f);
                    // 大块明暗，避免整块死板
                    float broad = Mathf.PerlinNoise(u * 3f + 11f, v * 3f + 7f);

                    float t = Mathf.Clamp01(0.72f + (brush - 0.5f) * 0.35f + (broad - 0.5f) * 0.30f);

                    // 冷灰的金属色，整体压暗一点，免得在暗场里太跳
                    float g = 0.44f + t * 0.30f;
                    Color c = new Color(g * 0.98f, g, g * 1.04f);
                    c.a = 1f;
                    px[y * S + x] = c;
                }
            }

            metalTex = NewTex(S, S);
            metalTex.wrapMode = TextureWrapMode.Repeat;
            metalTex.SetPixels32(px);
            metalTex.Apply(false, false);
            return metalTex;
        }

        // ══════════════════════════════════════════════════════════════
        //  烛光（开场界面用）
        // ══════════════════════════════════════════════════════════════

        private static Texture2D glowTex;
        private static Texture2D flameTex;

        /// <summary>
        /// 径向辉光：中心白、向外平滑衰减到全透明。
        /// 用 GUI.color 染成暖琥珀色就是烛光，拉扁拉长就是火苗的外焰 ——
        /// 只做一张白图，靠染色和拉伸派生出多种用法。
        /// </summary>
        public static Texture2D RadialGlow()
        {
            if (glowTex != null) return glowTex;

            const int S = 128;
            Color32[] px = new Color32[S * S];
            float half = S * 0.5f;

            for (int y = 0; y < S; y++)
            {
                float py = (y + 0.5f - half) / half;
                for (int x = 0; x < S; x++)
                {
                    float pxx = (x + 0.5f - half) / half;
                    float d = Mathf.Sqrt(pxx * pxx + py * py);

                    // ★ 平方衰减。
                    //   线性衰减会在边界上留一圈看得见的硬边，
                    //   平方之后是"越往外越快地淡掉"，才像光。
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a;

                    px[y * S + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }

            glowTex = NewTex(S, S);
            glowTex.SetPixels32(px);
            glowTex.Apply(false, false);
            return glowTex;
        }

        /// <summary>
        /// 火苗：上尖下圆的水滴形，从底部的近白芯过渡到顶端的橙红。
        /// 贴图本身是静态的 —— 抖动交给调用方用位移和缩放做，那样才像在跳。
        /// </summary>
        public static Texture2D Flame()
        {
            if (flameTex != null) return flameTex;

            const int W = 64, H = 96;
            Color32[] px = new Color32[W * H];

            Color hot  = new Color(1.00f, 0.96f, 0.82f);
            Color mid  = new Color(1.00f, 0.68f, 0.22f);
            Color cool = new Color(0.92f, 0.36f, 0.09f);

            for (int y = 0; y < H; y++)
            {
                float v = (float)y / (H - 1);          // 0 = 底，1 = 顶

                // 水滴廓形：底部最宽，往上收成尖
                float widthAt = Mathf.Sin(Mathf.Pow(v, 0.62f) * Mathf.PI) * 0.92f + 0.08f;
                float halfW = W * 0.5f * widthAt;

                for (int x = 0; x < W; x++)
                {
                    float dx = Mathf.Abs((x + 0.5f) - W * 0.5f);

                    float ax = Mathf.Clamp01(1f - dx / Mathf.Max(0.5f, halfW));
                    float ay = Mathf.Clamp01(1f - Mathf.Pow(v, 2.4f));
                    float a = Mathf.Pow(ax, 0.85f) * ay;

                    Color c = v < 0.45f
                        ? Color.Lerp(hot, mid, v / 0.45f)
                        : Color.Lerp(mid, cool, (v - 0.45f) / 0.55f);

                    c.a = Mathf.Pow(Mathf.Clamp01(a), 1.35f);
                    px[y * W + x] = c;
                }
            }

            flameTex = NewTex(W, H);
            flameTex.SetPixels32(px);
            flameTex.Apply(false, false);
            return flameTex;
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

        /// <summary>
        /// 属性印记的形状 —— 返回"到轮廓的距离"。
        ///   H 盐性（固体）→ 方形　　D 汞性（液体）→ 圆形　　V 硫性（气体）→ 菱形
        /// 卡面和检视面板共用这一个函数，保证两处看到的是同一个符号。
        /// </summary>
        private static float ShapeSDF(float px, float py, AttrId kind, float r)
        {
            switch (kind)
            {
                case AttrId.Mercury:
                    return Mathf.Abs(new Vector2(px, py).magnitude - r);

                case AttrId.Sulfur:
                    return Mathf.Abs(Mathf.Abs(px) + Mathf.Abs(py) - r * 1.25f);

                default:   // Salt
                    return Mathf.Abs(RoundRectSDF(px, py, r * 0.82f, r * 0.82f, r * 0.22f));
            }
        }

        /// <summary>三属性里数值最高的那个 —— 决定印记形状。没有数据时按盐性算。</summary>
        public static AttrId DominantAttr(Ingredient ing)
        {
            AttrId best = AttrId.Salt;
            if (ing == null || ing.attrs == null) return best;

            int bestValue = int.MinValue;
            foreach (AttrDef def in AttrCatalog.All())
            {
                int v = ing.attrs.Get(def.id);
                if (v > bestValue) { bestValue = v; best = def.id; }
            }
            return best;
        }

        /// <summary>
        /// 单独出一张方形印记图（带透明底），给检视面板这类 2D 界面用。
        /// 形状规则和卡面完全一致，所以面板上和卡上看到的是同一个符号。
        /// </summary>
        public static Texture2D Emblem(AttrId dominant, Color accent, int size)
        {
            int key = size * 31 + (int)dominant
                    + (Mathf.RoundToInt(accent.r * 255f) << 8)
                    + (Mathf.RoundToInt(accent.g * 255f) << 16)
                    + (Mathf.RoundToInt(accent.b * 255f) << 24);

            Texture2D cached;
            if (emblemCache.TryGetValue(key, out cached) && cached != null) return cached;

            Color32[] px = new Color32[size * size];
            float half = size * 0.5f;
            float r = half * 0.62f;
            float stroke = Mathf.Max(2f, size * 0.045f);

            for (int y = 0; y < size; y++)
            {
                float py = (y + 0.5f) - half;
                for (int x = 0; x < size; x++)
                {
                    float pxx = (x + 0.5f) - half;
                    float d = ShapeSDF(pxx, py, dominant, r);

                    Color c;
                    if (d < 0f)
                    {
                        // 内部：淡色填充，中心再实一点，看起来像个印章
                        float t = Mathf.Clamp01(-d / (r * 0.8f));
                        c = Color.Lerp(accent, Paper, 0.55f - 0.30f * t);
                        c.a = 0.30f + 0.30f * t;
                    }
                    else
                    {
                        c = accent;
                        c.a = Mathf.Clamp01(stroke - d);
                    }

                    px[y * size + x] = c;
                }
            }

            Texture2D tex = NewTex(size, size);
            tex.SetPixels32(px);
            tex.Apply(false, false);

            emblemCache[key] = tex;
            return tex;
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
