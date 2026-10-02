using UnityEngine;

namespace GameJam.Prototype
{
    /// <summary>
    /// 《邪恶铭刻》风格的开场界面。
    ///
    /// 【为什么这么设计】
    /// 邪恶铭刻的开场不是"标题 + 按钮列表"，而是一个**场景**：
    /// 你坐在一张木桌前，桌上一支蜡烛，对面有个看不清的东西，
    /// 而"开始新游戏"是一本可以翻开的书。
    /// 界面本身就是叙事的一部分 —— 把菜单项做成场景里的**物件**，
    /// 而不是浮在画面上的控件。
    ///
    /// 这里用同样的思路做：暗房、跳动的烛光、木桌边沿，
    /// 「新游戏」是一本摊在桌上的书，其余选项是刻在木桌上的字。
    ///
    /// 【为什么不用图片素材】
    /// 全部程序化生成：暗角、辉光、火苗、木纹都是算出来的。
    /// 好处是改一个数就能调整气氛（烛光多亮、木头多暗、火苗多大），
    /// 不用等美术出图，也没有素材授权问题。
    ///
    /// 【调用方】
    /// 由 Board2DView 持有：它在 Title 状态下每帧调 Draw()，
    /// 返回的 StartRequested 为真时切到棋盘。
    /// </summary>
    public class TitleScreen
    {
        /// <summary>玩家点了「新游戏」</summary>
        public bool StartRequested { get; private set; }

        // ── 样式 ──────────────────────────────────────────────────────
        private Font cjk;
        private GUIStyle titleStyle, subtitleStyle, bookTitleStyle, bookSubStyle, footerStyle;
        private GUIStyle menuItem, menuItemDisabled;

        // ── 布局缓存（按钮要用）───────────────────────────────────────
        private Rect bookRect;

        // ══════════════════════════════════════════════════════════════

        private void EnsureStyles()
        {
            if (cjk == null)
            {
                string[] candidates =
                {
                    "Microsoft YaHei", "微软雅黑", "STKaiti", "KaiTi", "楷体",
                    "SimHei", "SimSun", "PingFang SC", "Noto Sans CJK SC"
                };
                cjk = Font.CreateDynamicFontFromOSFont(candidates, 24);
            }

            if (titleStyle != null) return;

            titleStyle = Make(58, FontStyle.Bold, new Color(0.94f, 0.88f, 0.74f),
                              TextAnchor.MiddleCenter);
            subtitleStyle = Make(19, FontStyle.Normal, new Color(0.60f, 0.55f, 0.46f),
                                 TextAnchor.MiddleCenter);

            bookTitleStyle = Make(30, FontStyle.Bold, new Color(0.93f, 0.84f, 0.62f),
                                  TextAnchor.MiddleCenter);
            bookSubStyle = Make(15, FontStyle.Normal, new Color(0.70f, 0.62f, 0.46f),
                                TextAnchor.MiddleCenter);

            footerStyle = Make(14, FontStyle.Normal, new Color(0.42f, 0.39f, 0.34f),
                               TextAnchor.LowerLeft);

            // 菜单项：没有底、没有边框，只有文字颜色在悬停时变暖 ——
            // 像刻在木头上的字被烛光照亮，而不是一个"按钮控件"
            menuItem = new GUIStyle(GUI.skin.button)
            {
                fontSize = 24, alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(0, 0, 0, 0)
            };
            menuItem.normal.background = null;
            menuItem.hover.background  = null;
            menuItem.active.background = null;
            menuItem.focused.background = null;
            menuItem.normal.textColor  = new Color(0.66f, 0.62f, 0.54f);
            menuItem.hover.textColor   = new Color(1.00f, 0.78f, 0.34f);
            menuItem.active.textColor  = new Color(1.00f, 0.88f, 0.55f);

            menuItemDisabled = new GUIStyle(menuItem);
            menuItemDisabled.normal.textColor = new Color(0.34f, 0.32f, 0.29f);
            menuItemDisabled.hover.textColor  = new Color(0.34f, 0.32f, 0.29f);

            if (cjk != null)
            {
                titleStyle.font = cjk; subtitleStyle.font = cjk;
                bookTitleStyle.font = cjk; bookSubStyle.font = cjk;
                footerStyle.font = cjk; menuItem.font = cjk;
                menuItemDisabled.font = cjk;
            }
        }

        private GUIStyle Make(int size, FontStyle fs, Color c, TextAnchor anchor)
        {
            GUIStyle s = new GUIStyle(GUI.skin.label)
            {
                fontSize = size, fontStyle = fs, alignment = anchor, wordWrap = true
            };
            s.normal.textColor = c;
            if (cjk != null) s.font = cjk;
            return s;
        }

        // ══════════════════════════════════════════════════════════════
        //  绘制
        // ══════════════════════════════════════════════════════════════

        public void Draw(Rect full)
        {
            EnsureStyles();

            // 烛光位置：右上偏中。整幅画面的明暗都围着它转。
            Vector2 candleAt = new Vector2(full.width * 0.74f, full.height * 0.52f);

            DrawBackdrop(full, candleAt);
            DrawTable(full);
            DrawCandle(candleAt, full.height);
            DrawTitleAndMenu(full);

            // 底部小字
            GUI.Label(new Rect(24f, full.height - 34f, 620f, 26f),
                      "烛光会一直烧着，直到你翻开那本书", footerStyle);
        }

        /// <summary>
        /// 底色 + 烛光。
        ///
        /// 火苗的亮度用**两个不同频率的正弦叠加**来抖，而不是 Random ——
        /// 随机每帧重抽会变成刺眼的噪点闪烁，
        /// 两个不成倍数的频率叠起来才有"呼吸"感。
        /// </summary>
        private void DrawBackdrop(Rect full, Vector2 candleAt)
        {
            // 底色：近黑，但偏向暖褐（纯黑会显得像没画东西）
            DrawRect(full, new Color(0.035f, 0.028f, 0.022f));

            float t = Time.time;
            float flicker = 0.82f
                          + 0.10f * Mathf.Sin(t * 3.1f)
                          + 0.06f * Mathf.Sin(t * 7.7f + 1.3f)
                          + 0.04f * Mathf.Sin(t * 13.9f + 2.7f);

            // 大范围辉光：整个房间被照亮的感觉
            float bigR = full.height * 1.75f;
            Texture2D glow = ProceduralArt.RadialGlow();

            Color prev = GUI.color;
            GUI.color = new Color(1.00f, 0.62f, 0.24f, 0.20f * flicker);
            GUI.DrawTexture(new Rect(candleAt.x - bigR * 0.5f, candleAt.y - bigR * 0.5f, bigR, bigR), glow);

            // 小范围亮芯
            float nearR = full.height * 0.72f;
            GUI.color = new Color(1.00f, 0.74f, 0.38f, 0.26f * flicker);
            GUI.DrawTexture(new Rect(candleAt.x - nearR * 0.5f, candleAt.y - nearR * 0.5f, nearR, nearR), glow);
            GUI.color = prev;

            // 四角压暗：把视线收拢到中间那本书上
            float vig = full.height * 1.35f;
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            DrawCornerVignette(full, vig);
            GUI.color = prev;
        }

        /// <summary>四角暗角。用一个中心透明、边缘全黑的径向图铺四边，比画四个角省事。</summary>
        private void DrawCornerVignette(Rect full, float size)
        {
            Texture2D glow = ProceduralArt.RadialGlow();

            // 把辉光图反过来用：画在比屏幕大的位置，只在边缘露出"外圈"
            float big = size * 2f;
            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.62f);
            GUI.DrawTexture(new Rect(full.center.x - big * 0.5f, full.center.y - big * 0.5f, big, big), glow);
            GUI.color = prev;
        }

        /// <summary>木桌：底部三分之一铺桌面木纹，上沿压一道深色，像是桌边。</summary>
        private void DrawTable(Rect full)
        {
            float tableTop = full.height * 0.66f;
            Rect table = new Rect(0f, tableTop, full.width, full.height - tableTop);

            Color prev = GUI.color;
            GUI.color = new Color(0.62f, 0.50f, 0.38f);      // 木纹图偏亮，压下来
            GUI.DrawTexture(table, ProceduralArt.TableSurface(), ScaleMode.StretchToFill);
            GUI.color = prev;

            // 上沿一道暗边，把"桌面"和"后面的黑"分开
            DrawRect(new Rect(0f, tableTop - 3f, full.width, 3f), new Color(0f, 0f, 0f, 0.55f));
            DrawRect(new Rect(0f, tableTop, full.width, 2f), new Color(0.80f, 0.66f, 0.48f, 0.18f));
        }

        /// <summary>蜡烛：蜡身 + 火苗 + 一小片暖光落在桌面上。</summary>
        private void DrawCandle(Vector2 at, float screenH)
        {
            float t = Time.time;
            float flicker = 0.82f
                          + 0.10f * Mathf.Sin(t * 3.1f)
                          + 0.06f * Mathf.Sin(t * 7.7f + 1.3f)
                          + 0.04f * Mathf.Sin(t * 13.9f + 2.7f);

            // 蜡烛高度跟着屏幕走，换分辨率不会变形
            float waxH = screenH * 0.17f;
            float waxW = waxH * 0.30f;
            float baseY = at.y + waxH * 0.5f;
            Rect wax = new Rect(at.x - waxW * 0.5f, baseY - waxH, waxW, waxH);

            // 桌面上的一小片暖光（画在蜡烛之前，像被蜡烛照亮的）
            Texture2D glow = ProceduralArt.RadialGlow();
            float poolR = waxW * 7f;
            Color prev = GUI.color;
            GUI.color = new Color(1.00f, 0.66f, 0.28f, 0.20f * flicker);
            GUI.DrawTexture(new Rect(at.x - poolR * 0.5f, baseY - poolR * 0.30f, poolR, poolR * 0.6f), glow);
            GUI.color = prev;

            // 蜡身：上浅下深
            DrawRect(wax, new Color(0.86f, 0.82f, 0.72f));
            DrawRect(new Rect(wax.x, wax.y + wax.height * 0.55f, wax.width, wax.height * 0.45f),
                     new Color(0.72f, 0.67f, 0.57f));
            // 顶面
            DrawRect(new Rect(wax.x, wax.y - 3f, wax.width, 5f), new Color(0.93f, 0.90f, 0.82f));

            // 芯
            DrawRect(new Rect(at.x - 1f, wax.y - 7f, 2f, 8f), new Color(0.15f, 0.11f, 0.08f));

            // 火苗：宽高各自抖，而且频率不同 —— 火才会"扭"
            float fH = screenH * 0.062f * (0.93f + 0.10f * Mathf.Sin(t * 5.3f));
            float fW = fH * 0.44f * (0.92f + 0.13f * Mathf.Sin(t * 8.1f + 0.9f));
            float fX = at.x + Mathf.Sin(t * 4.7f) * fH * 0.055f;

            Color p2 = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.96f);
            GUI.DrawTexture(new Rect(fX - fW * 0.5f, wax.y - 6f - fH, fW, fH),
                            ProceduralArt.Flame(), ScaleMode.StretchToFill);
            GUI.color = p2;

            // 火苗外面再叠一层柔光，边缘才不会像剪纸
            float haloR = fH * 3.2f;
            GUI.color = new Color(1.00f, 0.72f, 0.34f, 0.30f * flicker);
            GUI.DrawTexture(new Rect(fX - haloR * 0.5f, wax.y - 6f - fH * 0.6f - haloR * 0.5f,
                                     haloR, haloR), glow);
            GUI.color = prev;
        }

        // ══════════════════════════════════════════════════════════════
        //  标题 + 那本书 + 菜单
        // ══════════════════════════════════════════════════════════════

        private void DrawTitleAndMenu(Rect full)
        {
            // 标题
            GUI.Label(new Rect(0f, full.height * 0.070f, full.width, 78f), "破 壁 机 计 划", titleStyle);
            GUI.Label(new Rect(0f, full.height * 0.070f + 74f, full.width, 30f),
                      "一台机器，一个人，和一桌不肯认输的材料", subtitleStyle);

            // ── 那本书 = 「新游戏」 ──
            float bw = Mathf.Min(full.width * 0.30f, 360f);
            float bh = bw * 0.68f;
            bookRect = new Rect(full.width * 0.5f - bw * 0.5f, full.height * 0.395f, bw, bh);

            bool hot = bookRect.Contains(Event.current.mousePosition);

            // 书影
            DrawRect(new Rect(bookRect.x + 8f, bookRect.y + 10f, bookRect.width, bookRect.height),
                     new Color(0f, 0f, 0f, 0.45f));

            // 封面（皮面）+ 书脊
            DrawRect(bookRect, hot ? new Color(0.34f, 0.19f, 0.12f) : new Color(0.27f, 0.15f, 0.10f));
            DrawRect(new Rect(bookRect.x, bookRect.y, bookRect.width * 0.085f, bookRect.height),
                     new Color(0.20f, 0.11f, 0.07f));
            // 书页侧面
            DrawRect(new Rect(bookRect.x + bookRect.width - 7f, bookRect.y + 5f, 7f, bookRect.height - 10f),
                     new Color(0.78f, 0.73f, 0.62f));

            // 烫金边
            DrawFrame(new Rect(bookRect.x + bookRect.width * 0.085f + 12f, bookRect.y + 12f,
                               bookRect.width - bookRect.width * 0.085f - 26f, bookRect.height - 24f),
                      hot ? new Color(1.00f, 0.80f, 0.38f, 0.85f) : new Color(0.72f, 0.56f, 0.30f, 0.60f), 2f);

            GUI.Label(new Rect(bookRect.x, bookRect.y + bookRect.height * 0.30f,
                               bookRect.width, 44f), "新 游 戏", bookTitleStyle);
            GUI.Label(new Rect(bookRect.x, bookRect.y + bookRect.height * 0.30f + 42f,
                               bookRect.width, 26f), hot ? "翻开它" : "—— 翻 开 ——", bookSubStyle);

            if (GUI.Button(bookRect, GUIContent.none, GUIStyle.none)) StartRequested = true;

            // ── 其余选项：刻在桌面上的字 ──
            float mx = full.width * 0.5f - 90f;
            float my = full.height * 0.80f;
            float mw = 260f;
            float mh = 34f;

            GUI.enabled = false;
            GUI.Button(new Rect(mx, my, mw, mh), "继 续（无存档）", menuItemDisabled);
            GUI.enabled = true;

            if (GUI.Button(new Rect(mx, my + mh + 4f, mw, mh), "设　　置", menuItem)) { /* 待接 */ }
            if (GUI.Button(new Rect(mx, my + (mh + 4f) * 2f, mw, mh), "退　　出", menuItem))
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  小工具
        // ══════════════════════════════════════════════════════════════

        private static void DrawRect(Rect r, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
        }

        private static void DrawFrame(Rect r, Color c, float thickness)
        {
            DrawRect(new Rect(r.x, r.y, r.width, thickness), c);
            DrawRect(new Rect(r.x, r.y + r.height - thickness, r.width, thickness), c);
            DrawRect(new Rect(r.x, r.y, thickness, r.height), c);
            DrawRect(new Rect(r.x + r.width - thickness, r.y, thickness, r.height), c);
        }
    }
}
