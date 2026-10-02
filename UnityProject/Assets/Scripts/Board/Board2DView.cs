using System.Collections.Generic;
using UnityEngine;
using GameJam.Data;

namespace GameJam.Prototype
{
    /// <summary>
    /// 2D 棋盘原型 —— 和 3D 桌面原型是**同一个玩法的两种表现形式**。
    ///
    /// 【版面】照着参考图分四块
    ///
    ///   ┌───────────┬───────────────────┬──────────────┐
    ///   │ 破壁机面板 │      打出区        │  客人 / 杯子  │
    ///   │  资源计数  │  ↑  ↑  ↑  ↑       │  ┌────────┐  │
    ///   │  破壁机动画│  [牌][刀][牌]      │  │  客人   │  │
    ///   │  天平      │                   │  ├────────┤  │
    ///   │  附加卡位  │                   │  │ 杯子得分│  │
    ///   └───────────┴───────────────────┴──────────────┘
    ///   ┌──────────────────────────────────────────────┐
    ///   │                   手牌区                      │
    ///   └──────────────────────────────────────────────┘
    ///
    /// 【数据从哪来】
    /// 全部来自 GameConfig —— 和 3D 桌面原型读的是同一份配置。
    /// 所以两套表现切换时，牌组、食材、得分、目标分完全一致，
    /// 它们不是"新旧版本"，而是两条并行分支。
    ///
    /// 【一套动画串起整条链路】
    /// 打出一张牌 → 破壁机动画播放 → **打出区的牌抖动、爆浆** →
    /// 得到的分数流入右边的杯子。这条链路是参考图里标注的重点，
    /// 也是这个视图真正要验证的东西。
    /// </summary>
    public class Board2DView : MonoBehaviour
    {
        // ── 数据 ──────────────────────────────────────────────────────
        private Deck deck;
        private Ingredient blade;
        private readonly List<Ingredient> hand   = new List<Ingredient>();
        private readonly List<Ingredient> played = new List<Ingredient>();

        private int score;
        private int target = 1000;

        /// <summary>打出区最多放几张（参考图里是 4 个槽）</summary>
        private const int PlaySlots = 4;

        // ── 动画 ──────────────────────────────────────────────────────
        /// <summary>破壁机动画进度。<0 表示没在播。</summary>
        private float blendT = -1f;
        private const float BlendDuration = 1.15f;

        /// <summary>这一轮动画结算后要加的分数（动画演完才真正加上去）</summary>
        private int pendingScore;

        /// <summary>液面平滑值（0..1），别让杯子里的水位瞬间跳</summary>
        private float shownLevel;

        // ── 样式 ──────────────────────────────────────────────────────
        private Font cjk;
        private GUIStyle h1, h2, body, dim, small, bigNum, cardName, cardAttr, btn;
        private GUIStyle panelStyle, slotStyle;

        // ══════════════════════════════════════════════════════════════
        //  生命周期
        // ══════════════════════════════════════════════════════════════

        private void Start()
        {
            LoadData();
        }

        /// <summary>从配置表装载本局数据。</summary>
        private void LoadData()
        {
            GameConfig.EnsureLoaded();

            List<Deck> decks = GameConfig.Decks();
            deck = decks.Count > 0 ? decks[0] : null;

            hand.Clear();
            played.Clear();

            if (deck != null)
            {
                // 开局刀片 + 其余进手牌，和 3D 桌面原型同一套规则
                Ingredient initial = deck.InitialBlade();
                blade = initial != null ? initial.Clone() : GameConfig.DefaultBlade();

                List<Ingredient> rest = deck.InitialHandIngredients();
                for (int i = 0; i < rest.Count; i++) hand.Add(rest[i]);
            }
            else
            {
                blade = GameConfig.DefaultBlade();
            }

            // 目标分也从关卡配置读 —— 两边表现的目标分必须一致，
            // 否则同一局在 3D 和 2D 下会显示出不同的"过关线"
            target = GameConfig.Level().targetScore;
        }

        private void Update()
        {
            // 液面平滑追赶
            float want = Mathf.Clamp01(target > 0 ? (float)score / target : 0f);
            if (Mathf.Abs(shownLevel - want) > 0.0005f)
                shownLevel = Mathf.Lerp(shownLevel, want, 1f - Mathf.Exp(-6f * Time.deltaTime));

            AdvanceBlend();

            // 空格 / B 播放一次破壁机动画
            if (Input.GetKeyDown(KeyCode.B)) PlayBlend(0);
        }

        private void AdvanceBlend()
        {
            if (blendT < 0f) return;

            blendT += Time.deltaTime;
            if (blendT < BlendDuration) return;

            // 动画演完才真正加分 —— 分数和表现对齐，不会"数字先跳、动画后播"
            score += pendingScore;
            pendingScore = 0;
            blendT = -1f;
        }

        /// <summary>播放破壁机动画。extra 是额外加的分（打出牌时传 0，让牌自己算）。</summary>
        private void PlayBlend(int extra)
        {
            if (blendT >= 0f) return;          // 正在播就不重入
            blendT = 0f;
            pendingScore = extra;
        }

        // ══════════════════════════════════════════════════════════════
        //  样式
        // ══════════════════════════════════════════════════════════════

        private void EnsureStyles()
        {
            if (cjk == null)
            {
                string[] candidates =
                {
                    "Microsoft YaHei", "微软雅黑", "SimHei", "SimSun",
                    "PingFang SC", "Noto Sans CJK SC", "Arial Unicode MS"
                };
                cjk = Font.CreateDynamicFontFromOSFont(candidates, 20);
            }

            if (h1 != null) return;

            h1       = Make(28, FontStyle.Bold, new Color(0.96f, 0.97f, 0.99f));
            h2       = Make(21, FontStyle.Bold, new Color(0.94f, 0.95f, 0.98f));
            body     = Make(16, FontStyle.Normal, new Color(0.88f, 0.91f, 0.95f));
            dim      = Make(14, FontStyle.Normal, new Color(0.62f, 0.68f, 0.77f));
            small    = Make(12, FontStyle.Normal, new Color(0.58f, 0.63f, 0.72f));
            bigNum   = Make(40, FontStyle.Bold, new Color(1.00f, 0.82f, 0.36f));
            cardName = Make(17, FontStyle.Bold, new Color(0.14f, 0.13f, 0.12f));
            cardAttr = Make(13, FontStyle.Normal, new Color(0.28f, 0.26f, 0.24f));

            btn = new GUIStyle(GUI.skin.button) { fontSize = 15 };
            if (cjk != null) btn.font = cjk;

            // 面板底：和 3D 那边共用一个程序化贴图，走 9 宫格拉伸
            panelStyle = new GUIStyle(GUI.skin.box)
            {
                border = new RectOffset(ProceduralArt.PanelBorder, ProceduralArt.PanelBorder,
                                        ProceduralArt.PanelBorder, ProceduralArt.PanelBorder)
            };
            panelStyle.normal.background = ProceduralArt.PanelBackdrop();

            // 空槽：一块压暗的底，表示"这里可以放"
            slotStyle = new GUIStyle(GUI.skin.box);
            Texture2D slotTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            slotTex.SetPixel(0, 0, new Color(0.18f, 0.20f, 0.25f, 0.85f));
            slotTex.Apply();
            slotTex.hideFlags = HideFlags.HideAndDontSave;
            slotStyle.normal.background = slotTex;
        }

        private GUIStyle Make(int size, FontStyle fs, Color c)
        {
            GUIStyle s = new GUIStyle(GUI.skin.label)
            {
                fontSize = size, fontStyle = fs, wordWrap = true,
                alignment = TextAnchor.UpperLeft
            };
            s.normal.textColor = c;
            if (cjk != null) s.font = cjk;
            return s;
        }

        // ══════════════════════════════════════════════════════════════
        //  版面
        // ══════════════════════════════════════════════════════════════

        private void OnGUI()
        {
            EnsureStyles();

            float W = Screen.width;
            float H = Screen.height;

            // 纯 2D 视图，先把整屏压成深色底 ——
            // 不然背后空场景的天空盒会透出来，和面板的深色调子打架
            DrawRect(new Rect(0f, 0f, W, H), new Color(0.075f, 0.082f, 0.098f));

            float topY = 0.015f * H;
            float topH = 0.760f * H;
            float handY = 0.790f * H;
            float handH = 0.195f * H;

            // 左 26% / 中 45% / 右 26%（大致对应参考图的比例）
            float leftX = 0.010f * W,  leftW = 0.260f * W;
            float midX  = 0.281f * W,  midW  = 0.450f * W;
            float rightX = 0.742f * W, rightW = 0.248f * W;

            DrawLeftPanel(new Rect(leftX, topY, leftW, topH));
            DrawCenterPanel(new Rect(midX, topY, midW, topH));
            DrawRightPanel(new Rect(rightX, topY, rightW, topH));
            DrawHand(new Rect(leftX, handY, W - leftX - 0.010f * W, handH));

            // 左下角操作提示
            GUI.Label(new Rect(leftX + 8f, handY + handH - 22f, 600f, 20f),
                      "点手牌打出　｜　B / 空格 播放破壁机动画　｜　" + deckNameText(), small);
        }

        private string deckNameText()
        {
            return "牌组：" + (deck != null ? deck.name : "（无）");
        }

        // ── 左：破壁机面板 ────────────────────────────────────────────

        private void DrawLeftPanel(Rect r)
        {
            GUI.Box(r, GUIContent.none, panelStyle);

            float pad = 12f;
            float x = r.x + pad;
            float w = r.width - pad * 2f;
            float y = r.y + pad;

            GUI.Label(new Rect(x, y, w, 30f), "破壁机", h2);
            y += 34f;

            // ── 资源计数行：拿三属性当三种资源 ──
            DrawResourceRow(new Rect(x, y, w, 46f));
            y += 54f;

            // ── 破壁机小动画 ──
            float boxH = Mathf.Min(200f, r.height * 0.36f);
            GUI.Label(new Rect(x, y, w, 22f), "破壁机运转", dim);
            y += 24f;

            DrawMiniBlender(new Rect(x, y, w, boxH));
            y += boxH + 16f;

            // ── 附加卡位 ──
            // （原来这里还有一台「天平」，照参考图画的装饰件。
            //   它在我们的规则里没有任何对应物，确认不需要，已删掉。）
            if (y + 60f < r.y + r.height)
            {
                GUI.Label(new Rect(x, y, w, 22f), "附加位", dim);

                Rect slot = new Rect(x, y + 24f, w * 0.42f, 56f);
                GUI.Box(slot, GUIContent.none, slotStyle);
                GUI.Label(new Rect(slot.x + 10f, slot.y + slot.height * 0.5f - 11f,
                                   slot.width - 20f, 22f), "×1", body);
            }
        }

        /// <summary>三种资源（就是 H / D / V 三属性）当前的合计值。</summary>
        private void DrawResourceRow(Rect r)
        {
            int h = 0, d = 0, v = 0;
            for (int i = 0; i < played.Count; i++)
            {
                if (played[i] == null) continue;
                h += played[i].attrs.Get(AttrId.Salt);
                d += played[i].attrs.Get(AttrId.Mercury);
                v += played[i].attrs.Get(AttrId.Sulfur);
            }

            AttrId[] ids = { AttrId.Salt, AttrId.Mercury, AttrId.Sulfur };
            int[] vals = { h, d, v };

            float cell = r.width / 3f;
            for (int i = 0; i < 3; i++)
            {
                Rect c = new Rect(r.x + i * cell, r.y, cell - 6f, r.height);

                GUI.Box(c, GUIContent.none, slotStyle);
                GUI.Label(new Rect(c.x + 8f, c.y + 6f, c.width - 16f, 22f),
                          AttrCatalog.Letter(ids[i]) + " " + AttrCatalog.DisplayName(ids[i]), small);
                GUI.Label(new Rect(c.x + 8f, c.y + 22f, c.width - 16f, 24f), vals[i].ToString(), h2);
            }
        }

        /// <summary>
        /// 左面板里那台小破壁机 —— 用矩形拼出来的简化版。
        ///
        /// 动画进度直接读对外的 blendT，所以它和中间打出区的抖动是**同一根时间轴**，
        /// 不会出现"机器压完了牌还在抖"这种对不上的情况。
        /// </summary>
        private void DrawMiniBlender(Rect r)
        {
            GUI.Box(r, GUIContent.none, slotStyle);

            float cx = r.center.x;
            float pad = 14f;

            // 冲压进度 0..1（动画前 55% 是在压，后面是出刀片 + 冲头抬回）
            float k = blendT >= 0f ? Mathf.Clamp01(blendT / (BlendDuration * 0.55f)) : 0f;

            // ── 四柱机架 ──
            Color frame = new Color(0.55f, 0.59f, 0.66f);
            float frameW = r.width - pad * 2f;
            float frameH = r.height - pad * 2f - 42f;
            float frameX = r.x + pad;
            float frameTop = r.y + pad;

            DrawRect(new Rect(frameX, frameTop, 4f, frameH), frame);
            DrawRect(new Rect(frameX + frameW - 4f, frameTop, 4f, frameH), frame);

            // 顶板 / 底板
            DrawRect(new Rect(frameX, frameTop, frameW, 7f), frame);
            DrawRect(new Rect(frameX, frameTop + frameH * 0.62f, frameW, 7f), frame);

            // ── 冲头：从上往下压 ──
            float ramTop = frameTop + 10f;
            float ramBottom = frameTop + frameH * 0.62f - 12f;
            float ramY = Mathf.Lerp(ramTop, ramBottom, k * k * (3f - 2f * k));
            DrawRect(new Rect(cx - 13f, ramY, 26f, 26f), new Color(0.82f, 0.85f, 0.90f));

            // ── 残渣：被压扁、摊开 ──
            float squash = Mathf.Lerp(1f, 0.3f, k);
            float spread = Mathf.Lerp(1f, 1.5f, k);
            float resW = 40f * spread;
            float resH = 14f * squash;
            float resY = frameTop + frameH * 0.62f - resH;
            DrawRect(new Rect(cx - resW * 0.5f, resY, resW, resH), new Color(0.48f, 0.34f, 0.19f));

            // 冲压到底时来一下爆浆
            if (blendT >= 0f && k > 0.92f)
                DrawSplatter(new Vector2(cx, resY), 26f, 0.35f);

            // ── 罐子 + 液面（液面就是得分） ──
            float jarW = 46f;
            float jarH = 40f;
            float jarX = cx - jarW * 0.5f;
            float jarY = r.y + r.height - 52f;

            DrawRect(new Rect(jarX, jarY, jarW, jarH), new Color(0.24f, 0.30f, 0.38f));

            float liqH = jarH * Mathf.Clamp01(shownLevel);
            if (liqH > 0.5f)
                DrawRect(new Rect(jarX + 2f, jarY + jarH - liqH, jarW - 4f, liqH),
                         new Color(0.98f, 0.62f, 0.18f, 0.92f));

            // 罐身上的三道刻度
            for (int i = 1; i <= 3; i++)
            {
                float ty = jarY + jarH - jarH * (i / 4f);
                DrawRect(new Rect(jarX - 4f, ty, 5f, 1.5f), new Color(0.85f, 0.89f, 0.94f));
            }

            GUI.Label(new Rect(cx + jarW * 0.5f + 6f, jarY + jarH * 0.45f, 70f, 22f),
                      score.ToString(), body);
        }

        // ── 中：打出区 ────────────────────────────────────────────────

        private void DrawCenterPanel(Rect r)
        {
            GUI.Box(r, GUIContent.none, panelStyle);

            float pad = 14f;
            float x = r.x + pad;
            float w = r.width - pad * 2f;
            float y = r.y + pad;

            GUI.Label(new Rect(x, y, w, 30f), "打出区", h2);
            y += 32f;
            GUI.Label(new Rect(x, y, w, 20f), "上方是当前回合打出的牌，下方是刀片", small);
            y += 28f;

            // ── 四个空槽（参考图里那排箭头） ──
            float slotGap = 8f;
            float slotW = (w - slotGap * (PlaySlots - 1)) / PlaySlots;
            float slotH = Mathf.Min(96f, r.height * 0.20f);

            for (int i = 0; i < PlaySlots; i++)
            {
                Rect s = new Rect(x + i * (slotW + slotGap), y, slotW, slotH);
                GUI.Box(s, GUIContent.none, slotStyle);

                if (i < played.Count && played[i] != null)
                {
                    DrawCardFace(s, played[i], true);
                }
                else
                {
                    GUI.Label(s, "↓", h1);
                }
            }
            y += slotH + 18f;

            // ── 刀片 ──
            GUI.Label(new Rect(x, y, w, 22f), "刀片", dim);
            y += 24f;

            Rect bladeRect = new Rect(x, y, Mathf.Min(150f, w * 0.34f), 54f);
            GUI.Box(bladeRect, GUIContent.none, slotStyle);

            if (blade != null)
            {
                GUI.Label(new Rect(bladeRect.x + 10f, bladeRect.y + 6f, bladeRect.width - 20f, 22f),
                          blade.name, body);
                GUI.Label(new Rect(bladeRect.x + 10f, bladeRect.y + 28f, bladeRect.width - 20f, 20f),
                          blade.attrs.DescribeLabeledNonZero(), small);
            }
        }

        // ── 右：客人 / 杯子 ───────────────────────────────────────────

        private void DrawRightPanel(Rect r)
        {
            GUI.Box(r, GUIContent.none, panelStyle);

            float pad = 12f;
            float x = r.x + pad;
            float w = r.width - pad * 2f;
            float y = r.y + pad;

            // 上半：客人
            float guestH = (r.height - pad * 2f) * 0.58f;
            Rect guest = new Rect(x, y, w, guestH);
            GUI.Box(guest, GUIContent.none, slotStyle);
            GUI.Label(new Rect(guest.x + 10f, guest.y + 8f, guest.width - 20f, 30f), "兔子", h2);

            // 客人占位形象：一个圆脑袋 + 两只耳朵
            DrawRect(new Rect(guest.center.x - 26f, guest.y + 66f, 52f, 52f), new Color(0.86f, 0.84f, 0.80f));
            DrawRect(new Rect(guest.center.x - 20f, guest.y + 40f, 12f, 30f), new Color(0.86f, 0.84f, 0.80f));
            DrawRect(new Rect(guest.center.x + 8f, guest.y + 40f, 12f, 30f), new Color(0.86f, 0.84f, 0.80f));

            GUI.Label(new Rect(guest.x + 10f, guest.y + guest.height - 46f, guest.width - 20f, 40f),
                      "客人占位\n（形象与行为待定）", small);

            y += guestH + 12f;

            // 下半：杯子 / 得分版
            float cupH = r.y + r.height - pad - y;
            DrawCup(new Rect(x, y, w, cupH));
        }

        /// <summary>
        /// 杯子 / 得分版。
        /// 左边是一条竖直的杯子，液面 = 得分；右边是刻度尺，刻度上写着分数。
        /// 「刻度就是得分面板」这条和 3D 那边是同一个设定，只是换了个画法。
        /// </summary>
        private void DrawCup(Rect r)
        {
            GUI.Box(r, GUIContent.none, slotStyle);
            GUI.Label(new Rect(r.x + 10f, r.y + 6f, r.width - 20f, 24f), "杯子 / 得分版", dim);

            float cupTop = r.y + 34f;
            float cupBottom = r.y + r.height - 34f;
            float cupH = Mathf.Max(40f, cupBottom - cupTop);

            float barW = 34f;
            float barX = r.x + 14f;

            // 杯体
            DrawRect(new Rect(barX, cupTop, barW, cupH), new Color(0.22f, 0.27f, 0.34f));

            // 液面
            float liqH = cupH * Mathf.Clamp01(shownLevel);
            if (liqH > 0.5f)
                DrawRect(new Rect(barX + 2f, cupTop + cupH - liqH, barW - 4f, liqH),
                         new Color(0.98f, 0.62f, 0.18f, 0.92f));

            // 刻度尺：5 段 6 条
            const int div = 5;
            float labelX = barX + barW + 10f;
            float labelW = r.width - (labelX - r.x) - 12f;

            for (int i = 0; i <= div; i++)
            {
                float t = (float)i / div;
                float ty = cupTop + cupH * (1f - t);

                bool passed = t <= shownLevel + 0.001f;
                Color tickCol = passed ? new Color(1.00f, 0.76f, 0.26f) : new Color(0.62f, 0.68f, 0.77f);

                DrawRect(new Rect(barX - 5f, ty - 1f, barW + 10f, 2f), tickCol);

                GUI.Label(new Rect(labelX, ty - 11f, labelW, 22f),
                          Mathf.RoundToInt(t * target).ToString(),
                          passed ? body : small);
            }

            // 当前分数，压在杯子正中间
            GUI.Label(new Rect(r.x + 6f, r.y + r.height * 0.42f, r.width - 12f, 44f),
                      score.ToString(), bigNum);
        }

        // ── 下：手牌 ──────────────────────────────────────────────────

        private void DrawHand(Rect r)
        {
            GUI.Box(r, GUIContent.none, panelStyle);

            float pad = 12f;
            float x = r.x + pad;
            float y = r.y + pad;

            GUI.Label(new Rect(x, y, 200f, 24f), "手牌区", h2);
            y += 26f;

            if (hand.Count == 0)
            {
                GUI.Label(new Rect(x, y + 20f, 400f, 24f), "手牌已空", dim);
                return;
            }

            float cardW = Mathf.Min(130f, (r.width - pad * 2f - 10f * (hand.Count - 1)) / Mathf.Max(1, hand.Count));
            float cardH = r.height - (y - r.y) - pad;

            for (int i = 0; i < hand.Count; i++)
            {
                Rect c = new Rect(x + i * (cardW + 10f), y, cardW, cardH);

                if (GUI.Button(c, GUIContent.none, GUIStyle.none))
                {
                    PlayCardAt(i);
                    return;      // 布局变了，本帧到此为止
                }

                DrawCardFace(c, hand[i], false);
            }
        }

        /// <summary>把手牌第 i 张打到中间，并播放一轮破壁机动画。</summary>
        private void PlayCardAt(int index)
        {
            if (index < 0 || index >= hand.Count) return;
            if (played.Count >= PlaySlots) played.RemoveAt(0);   // 打满了就把最早那张挤掉

            Ingredient ing = hand[index];
            hand.RemoveAt(index);
            played.Add(ing);

            // 分数先记账，等动画演完再加 —— 分数和表现对齐
            int gain = ing.attrs.Get(AttrId.Salt) * 8
                     + ing.attrs.Get(AttrId.Mercury) * 8
                     + ing.attrs.Get(AttrId.Sulfur) * 8;
            PlayBlend(gain);
        }

        // ══════════════════════════════════════════════════════════════
        //  绘制小工具
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 画一张牌。
        ///
        /// 破壁机动画播放时，打出区的牌会**抖动 + 爆浆** ——
        /// 抖动直接改绘制矩形的位置（不是改数据），所以动画结束就自动复原，
        /// 不需要"记住原来的位置再还原"那套状态管理。
        /// </summary>
        private void DrawCardFace(Rect r, Ingredient ing, bool canShake)
        {
            if (ing == null) return;

            Color accent = ProceduralArt.IngredientColor(ing.id);
            Rect draw = r;

            if (canShake && blendT >= 0f)
            {
                // 抖动幅度随动画先增后减，收尾时自然停住
                float p = Mathf.Clamp01(blendT / BlendDuration);
                float amp = Mathf.Sin(p * Mathf.PI) * 5f;

                draw.x += Mathf.Sin(Time.time * 62f) * amp;
                draw.y += Mathf.Cos(Time.time * 71f) * amp * 0.6f;
            }

            // 卡面底
            DrawRect(draw, new Color(0.93f, 0.91f, 0.86f));

            // 顶部主题色带
            float bandH = Mathf.Min(22f, draw.height * 0.26f);
            DrawRect(new Rect(draw.x, draw.y, draw.width, bandH), accent);

            // 名字（色带上）
            GUI.Label(new Rect(draw.x + 8f, draw.y + 2f, draw.width - 16f, bandH),
                      ing.name, cardName);

            // 三属性（一行一个，和 3D 卡面同一套版式）
            float ty = draw.y + bandH + 4f;
            foreach (AttrDef def in AttrCatalog.All())
            {
                GUI.Label(new Rect(draw.x + 8f, ty, draw.width - 16f, 18f),
                          def.Label + " " + ing.attrs.Get(def.id), cardAttr);
                ty += 17f;
            }

            // 破壁机正在压它 → 叠一层爆浆
            if (canShake && blendT >= 0f)
            {
                float p = Mathf.Clamp01(blendT / BlendDuration);
                if (p > 0.35f && p < 0.95f)
                    DrawSplatter(draw.center, draw.width * 0.42f, (p - 0.35f) / 0.60f);
            }
        }

        /// <summary>爆浆：中心几颗随机方向的液滴，随进度往外飞并淡出。</summary>
        private void DrawSplatter(Vector2 center, float radius, float k)
        {
            float alpha = Mathf.Clamp01(1f - k) * 0.85f;
            if (alpha <= 0.01f) return;

            Color juice = new Color(0.98f, 0.62f, 0.18f, alpha);

            for (int i = 0; i < 7; i++)
            {
                // 固定角度 + 固定长度抖动 —— 不用 Random，免得每帧闪成噪点
                float ang = i * 51.43f * Mathf.Deg2Rad;
                float dist = radius * (0.45f + 0.55f * k) * (0.7f + 0.3f * Mathf.Sin(i * 12.9f));

                float px = center.x + Mathf.Cos(ang) * dist;
                float py = center.y + Mathf.Sin(ang) * dist;
                float size = Mathf.Lerp(9f, 3f, k);

                DrawRect(new Rect(px - size * 0.5f, py - size * 0.5f, size, size), juice);
            }
        }

        /// <summary>画一块纯色矩形。IMGUI 里最省事的办法就是贴一张 1×1 白图再染色。</summary>
        private void DrawRect(Rect r, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
        }
    }
}
