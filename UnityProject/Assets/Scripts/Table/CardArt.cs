using System.Collections.Generic;
using UnityEngine;
using GameJam.Data;

namespace GameJam.Prototype
{
    /// <summary>
    /// 正式卡面美术 —— 美术（nicole）2026-10-04 交付的 UI_REs1004。
    ///
    /// 【这批图是什么】
    ///   card_common_bg        136x182  卡底（**通用**，所有卡共用）
    ///   card_{元素}_bg         ~96x110  元素底色块，垫在插画后面
    ///   card_{元素}_img        ~96x110  元素插画
    ///   card_name_bg           62x26    名字牌（贴左上角）
    ///   card_number_bg         38x86    数值牌（贴右侧偏下）
    ///   另外两张 *_bg_bg 是牌子的备选/描边版，效果图里没出现，先不用。
    ///
    /// 【版面是从美术的效果图里量出来的，不是猜的】
    ///   美术同时给了 4 张「card_{元素}_效果图.png」（136x182，带标注）和 card文字标注.png。
    ///   把每个切片在这些效果图里做遮罩模板匹配，得到唯一低误差位置：
    ///     名字牌 (4,4)   数值牌 (97,95)   插画：ice(15,31) water(20,39) vagour(19,29) 合金(14,43)
    ///   下面 IllustPos 就是这份测量结果。改版面 = 改这几个数（不需要重导图）。
    ///
    /// 【元素是怎么定的】
    ///   美术给的是**物态皮肤**，不是逐张食材插画（效果图上写的名字就是 ice/water/vapour/alloy）：
    ///     固（盐性 H 主导）→ ice    液（汞性 D 主导）→ water
    ///     气（硫性 V 主导）→ vagour  金属锭（刀片牌）  → extraterrestrialalloy
    ///   金属锭单独走合金皮：铁块/外星合金/秘银锭/钛钨胚正好就是四张"刀片"。
    ///   变速模块**不给皮肤**，继续用程序化冷色 —— 这是刻意的：
    ///   策划要靠冷暖把"模块"和"食材"一眼分开，套上皮肤这层区分就没了。
    ///   改映射只动 ElementOf()，一处。
    ///
    /// 【没有美术时怎么办】
    ///   任何一个切片缺失 → 返回 null → 调用方退回 ProceduralArt.CardFace，不会花屏。
    /// </summary>
    public static class CardArt
    {
        public const string ArtRoot = "Art/Card/";

        /// <summary>效果图参考尺寸 —— 美术出图的坐标系（左上角原点）。</summary>
        private const int RefW = 136;
        private const int RefH = 182;

        /// <summary>名字牌 / 数值牌在效果图里的左上角。</summary>
        private static readonly Vector2Int NamePlatePos   = new Vector2Int(4, 4);
        private static readonly Vector2Int NumberPlatePos = new Vector2Int(97, 95);

        /// <summary>各元素插画在效果图里的左上角（模板匹配量出来的）。</summary>
        private static readonly Dictionary<string, Vector2Int> IllustPos =
            new Dictionary<string, Vector2Int>
            {
                { "ice",                   new Vector2Int(15, 31) },
                { "water",                 new Vector2Int(20, 39) },
                { "vagour",                new Vector2Int(19, 29) },
                { "extraterrestrialalloy", new Vector2Int(14, 43) },
            };

        /// <summary>金属锭（= 四张刀片牌）走合金皮。</summary>
        private static readonly HashSet<string> MetalIds = new HashSet<string>
        {
            "iron_block", "alien_alloy", "mithril_ingot", "tiw_block",
        };

        /// <summary>
        /// 有美术时文字的落点（卡面局部坐标，世界单位）。
        /// 换算：效果图 x/136-0.5 → ×卡宽 0.24；y 从上往下 (0.5-y/182) → ×卡深 0.335。
        /// </summary>
        public static readonly Vector3 NameOnPlate   = new Vector3(-0.058f, 0f, 0.136f);
        public static readonly Vector3 StatsOnPlate  = new Vector3( 0.085f, 0f, -0.087f);

        /// <summary>有美术时名字要缩到牌子宽度以内（牌子只占卡宽 46%）。</summary>
        public const float NameOnPlateSize  = 0.0075f;
        public const float StatsOnPlateSize = 0.0038f;

        /// <summary>牌子是暖棕色，字用深墨色（程序化卡面用的是白字，两套不能混）。</summary>
        public static readonly Color InkOnPlate = new Color(0.145f, 0.132f, 0.118f);

        private static readonly Dictionary<string, Texture2D> cache =
            new Dictionary<string, Texture2D>();

        // ══════════════════════════════════════════════════════════════
        //  对外
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 这张牌用哪套元素皮肤；没有对应皮肤时返回 null（调用方退回程序化卡面）。
        /// </summary>
        public static string ElementOf(Card c)
        {
            if (c == null) return null;
            return ElementOf(c.id, c.ingredient, c.IsModule);
        }

        /// <summary>
        /// 皮肤选择的**实际规则** —— 不依赖 Card，策划表 / 自检工具可以直接问
        /// （菜单：工具 → 检查美术接入）。要改映射就改这里，一处。
        /// </summary>
        public static string ElementOf(string id, Ingredient ingredient, bool isModule)
        {
            if (isModule || string.IsNullOrEmpty(id)) return null;   // 模块保持程序化，见类注释
            if (MetalIds.Contains(id)) return "extraterrestrialalloy";

            switch (ProceduralArt.DominantAttr(ingredient))
            {
                case AttrId.Salt:    return "ice";         // 固
                case AttrId.Mercury: return "water";       // 液
                default:             return "vagour";      // 气（硫性）
            }
        }

        /// <summary>
        /// 拼一张完整卡面贴图。任何一个必要切片缺失都返回 null。
        /// 拼好的贴图会按元素缓存，连续发牌不会反复重算。
        /// </summary>
        public static Texture2D Face(Card c)
        {
            string element = ElementOf(c);
            if (element == null) return null;
            return Face(element);
        }

        public static Texture2D Face(string element)
        {
            if (string.IsNullOrEmpty(element)) return null;

            Texture2D cached;
            if (cache.TryGetValue(element, out cached)) return cached;

            Texture2D composed = null;
            try
            {
                composed = Compose(element);
            }
            catch (System.Exception e)
            {
                // 最可能的一种：贴图没开 Read/Write，GetPixels32() 会抛
                // "Texture ... is not readable"（导入设置见 Editor/ArtImportSettings.cs）。
                // 卡面拼不出来只能退回程序化，但**绝不能把整桌子的搭建带崩**。
                Debug.LogWarning("[CardArt] 拼卡面失败，该元素退回程序化卡面：" + e.Message);
            }

            cache[element] = composed;      // null 也缓存：缺图时别每张牌都去 Resources 查一遍
            return composed;
        }

        // ══════════════════════════════════════════════════════════════
        //  拼接
        // ══════════════════════════════════════════════════════════════

        private static Texture2D Compose(string element)
        {
            Vector2Int pos;
            if (!IllustPos.TryGetValue(element, out pos)) return null;

            Texture2D common   = Load("card_common_bg");
            Texture2D elemImg  = Load("card_" + element + "_img");
            if (common == null || elemImg == null) return null;

            Texture2D elemBg   = Load("card_" + element + "_bg");
            Texture2D nameBg   = Load("card_name_bg");
            Texture2D numberBg = Load("card_number_bg");

            // 先按效果图的坐标系拼（136x182），最后整体缩到卡面贴图尺寸
            Color32[] canvas = new Color32[RefW * RefH];   // 默认全透明

            Blit(canvas, common, 0, 0);

            // 元素底色块居中垫在插画后面（两者尺寸差 0~2 像素，按中心对齐）
            if (elemBg != null)
            {
                int bx = pos.x + (elemImg.width  - elemBg.width)  / 2;
                int by = pos.y + (elemImg.height - elemBg.height) / 2;
                Blit(canvas, elemBg, bx, by);
            }

            Blit(canvas, elemImg, pos.x, pos.y);
            if (nameBg   != null) Blit(canvas, nameBg,   NamePlatePos.x,   NamePlatePos.y);
            if (numberBg != null) Blit(canvas, numberBg, NumberPlatePos.x, NumberPlatePos.y);

            return ToFaceTexture(canvas);
        }

        /// <summary>把 136x182 的稿子按宽度等比放大到卡面贴图，纵向居中补透明边。</summary>
        private static Texture2D ToFaceTexture(Color32[] canvas)
        {
            int outW = ProceduralArt.CardTexW;
            int outH = Mathf.RoundToInt(RefH * ((float)outW / RefW));      // 256x343
            int oy   = (ProceduralArt.CardTexH - outH) / 2;                // 上下各留 7 像素

            Texture2D tex = NewTex(ProceduralArt.CardTexW, ProceduralArt.CardTexH);
            Color32[] dst = tex.GetPixels32();

            float sx = (float)RefW / outW;
            float sy = (float)RefH / outH;

            for (int y = 0; y < outH; y++)
            {
                float fy = (y + 0.5f) * sy - 0.5f;
                int y0 = Mathf.Clamp(Mathf.FloorToInt(fy), 0, RefH - 1);
                int y1 = Mathf.Min(y0 + 1, RefH - 1);
                float wy = Mathf.Clamp01(fy - y0);

                for (int x = 0; x < outW; x++)
                {
                    float fx = (x + 0.5f) * sx - 0.5f;
                    int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, RefW - 1);
                    int x1 = Mathf.Min(x0 + 1, RefW - 1);
                    float wx = Mathf.Clamp01(fx - x0);

                    Color c = Bilerp(canvas[y0 * RefW + x0], canvas[y0 * RefW + x1],
                                     canvas[y1 * RefW + x0], canvas[y1 * RefW + x1], wx, wy);
                    dst[(oy + y) * ProceduralArt.CardTexW + x] = c;
                }
            }

            tex.SetPixels32(dst);
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>
        /// 源图按左上角 (x,y) 做 source-over 叠加（straight alpha）。
        ///
        /// ★ 这里有一次坐标系换算，别删：
        ///   IllustPos / NamePlatePos 里的 y 是**从 PNG 顶边往下量**的（跟美术的效果图一致），
        ///   而 Texture2D.GetPixels32() 的行序是**从底边往上**（Unity 贴图 V=0 在下）。
        ///   所以目标行的起点是 RefH - y - src.height。
        ///   漏了这一步不会报任何错，只是整张卡面上下镜像 —— 卡面拼图能出图、取样像素也正常，
        ///   只有跟美术的效果图逐像素比才会发现（ArtCheck 出的 face_*.png 就是干这个用的）。
        /// </summary>
        private static void Blit(Color32[] dst, Texture2D src, int x, int y)
        {
            Color32[] s = src.GetPixels32();
            int w = src.width, h = src.height;
            int rowStart = RefH - y - h;          // PNG 顶边坐标 → 贴图底边坐标

            for (int sy = 0; sy < h; sy++)
            {
                int dy = rowStart + sy;
                if (dy < 0 || dy >= RefH) continue;

                for (int sx = 0; sx < w; sx++)
                {
                    int dx = x + sx;
                    if (dx < 0 || dx >= RefW) continue;

                    Color32 sp = s[sy * w + sx];
                    if (sp.a == 0) continue;

                    float sa = sp.a / 255f;
                    int di = dy * RefW + dx;
                    Color32 dp = dst[di];

                    float da = dp.a / 255f;
                    float oa = sa + da * (1f - sa);
                    if (oa <= 0f) { dst[di] = new Color32(0, 0, 0, 0); continue; }

                    float r = (sp.r * sa + dp.r * da * (1f - sa)) / oa;
                    float g = (sp.g * sa + dp.g * da * (1f - sa)) / oa;
                    float b = (sp.b * sa + dp.b * da * (1f - sa)) / oa;
                    dst[di] = new Color32((byte)Mathf.Clamp(Mathf.RoundToInt(r), 0, 255),
                                          (byte)Mathf.Clamp(Mathf.RoundToInt(g), 0, 255),
                                          (byte)Mathf.Clamp(Mathf.RoundToInt(b), 0, 255),
                                          (byte)Mathf.Clamp(Mathf.RoundToInt(oa * 255f), 0, 255));
                }
            }
        }

        private static Color Bilerp(Color32 c00, Color32 c10, Color32 c01, Color32 c11, float wx, float wy)
        {
            Color a = Color.Lerp(c00, c10, wx);
            Color b = Color.Lerp(c01, c11, wx);
            return Color.Lerp(a, b, wy);
        }

        private static Texture2D Load(string name)
        {
            Texture2D t = Resources.Load<Texture2D>(ArtRoot + name);
            if (t == null)
                Debug.LogWarning("[CardArt] 缺贴图：" + ArtRoot + name + "（该卡退回程序化卡面）");
            return t;
        }

        private static Texture2D NewTex(int w, int h)
        {
            Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.filterMode = FilterMode.Bilinear;
            t.wrapMode   = TextureWrapMode.Clamp;
            t.SetPixels32(new Color32[w * h]);     // 全透明
            return t;
        }
    }
}
