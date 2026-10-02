using System.Collections.Generic;
using UnityEngine;
using GameJam.Data;
using GameJam.Sim;

namespace GameJam.Prototype
{
    /// <summary>
    /// 「模拟中…」那一屏的 2D 杯内视图。
    ///
    /// 【为什么单独一个类】
    /// TableHud 已经八百多行了，再往里塞一整套杯内绘制会失控。
    /// 这里只负责**把 CupSim 画出来**，一条规则都不判 ——
    /// 粒子怎么动、什么时候反应、得多少分，全在 CupSim 里。
    ///
    /// 【为什么自己建样式】
    /// 不复用 TableHud 那套是因为它绑着面板用的深色底语义。
    /// 这里只有几种文字，自己建更清楚，也不会因为改 HUD 的样式把这屏带崩。
    ///
    /// 【坐标】
    /// CupSim 里全是归一化坐标（中心 0.5，半径 0.44）。
    /// 这里唯一的换算就是"归一化 → 屏幕像素"，除此之外不做任何几何判断。
    /// </summary>
    public class CupSimView
    {
        private Font cjk;

        private GUIStyle title, sub, floaterSmall, floaterBig, hint;

        /// <summary>杯子的绘制区域（正方形，像素）</summary>
        private Rect cupArea;

        public void Setup(Font font)
        {
            cjk = font;

            title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22, alignment = TextAnchor.MiddleCenter
            };
            title.normal.textColor = new Color(0.94f, 0.96f, 0.99f);

            sub = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15, alignment = TextAnchor.MiddleCenter
            };
            sub.normal.textColor = new Color(0.62f, 0.68f, 0.78f);

            floaterSmall = new GUIStyle(GUI.skin.label)
            {
                fontSize = 17, alignment = TextAnchor.MiddleCenter
            };
            floaterBig = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26, alignment = TextAnchor.MiddleCenter
            };

            hint = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13, alignment = TextAnchor.MiddleCenter, wordWrap = true
            };
            hint.normal.textColor = new Color(0.55f, 0.60f, 0.70f);

            if (cjk != null)
            {
                title.font = cjk; sub.font = cjk;
                floaterSmall.font = cjk; floaterBig.font = cjk; hint.font = cjk;
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  绘制
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 画这一屏。panel 是给整块面板用的矩形（由调用方决定放哪、多大）。
        /// </summary>
        public void Draw(Rect panel, CupSim sim, int turnNumber, GUIStyle panelBox)
        {
            if (sim == null || panelBox == null) return;

            GUI.Box(panel, GUIContent.none, panelBox);

            float pad = 18f;

            // ── 标题行 ──
            GUI.Label(new Rect(panel.x + pad, panel.y + 10f, panel.width - pad * 2f, 30f),
                      "第 " + turnNumber + " 回合 · 模拟中", title);

            GUI.Label(new Rect(panel.x + pad, panel.y + 40f, panel.width - pad * 2f, 22f),
                      "剩余 " + sim.Remain.ToString("0.0") + " 秒　｜　本回合得分 "
                      + sim.roundScore + "　｜　刀片转速 "
                      + Mathf.RoundToInt(sim.bladeSpeed) + "°/秒",
                      sub);

            // ── 杯子 ──
            float side = Mathf.Min(panel.width - pad * 2f, panel.height - 150f);
            side = Mathf.Max(160f, side);

            cupArea = new Rect(panel.x + (panel.width - side) * 0.5f,
                               panel.y + 68f, side, side);

            DrawCup(sim);
            DrawBlade(sim);
            DrawParticles(sim);
            DrawFloaters(sim);

            // ── 底部状态 ──
            string state = sim.bladeBroken
                ? "刀片已断，模拟中止"
                : "杯温 " + Mathf.RoundToInt(sim.temp) + "°　｜　刀片硬度 "
                  + Mathf.RoundToInt(sim.bladeHardness);

            GUI.Label(new Rect(panel.x + pad, panel.y + panel.height - 40f,
                               panel.width - pad * 2f, 20f), state, sub);

            GUI.Label(new Rect(panel.x + pad, panel.y + panel.height - 22f,
                               panel.width - pad * 2f, 18f),
                      "杯内 " + sim.particles.Count + " 份食材　｜　"
                      + "盐性 " + sim.CupAttr(AttrId.Salt)
                      + "　汞性 " + sim.CupAttr(AttrId.Mercury)
                      + "　硫性 " + sim.CupAttr(AttrId.Sulfur), hint);
        }

        // ── 杯子本体 ─────────────────────────────────────────────────

        private void DrawCup(CupSim sim)
        {
            Texture2D disc = ProceduralArt.Disc();
            float r = cupArea.width * 0.5f;

            // 杯壁：外圈比内径大一圈
            Color prev = GUI.color;
            GUI.color = new Color(0.30f, 0.34f, 0.42f, 0.95f);
            GUI.DrawTexture(new Rect(cupArea.center.x - r, cupArea.center.y - r,
                                     r * 2f, r * 2f), disc);

            // 杯内液面：一眼能看出温度 —— 冷是青蓝、热是橙红
            float t = Mathf.InverseLerp(20f, 100f, sim.temp);
            Color liquid = Color.Lerp(new Color(0.16f, 0.22f, 0.30f),
                                      new Color(0.42f, 0.22f, 0.12f), t);

            float ir = r * (CupSim.CupRadius / (CupSim.CupRadius + 0.03f));
            GUI.color = liquid;
            GUI.DrawTexture(new Rect(cupArea.center.x - ir, cupArea.center.y - ir,
                                     ir * 2f, ir * 2f), disc);
            GUI.color = prev;
        }

        // ── 刀片：一个旋转的矩形 ─────────────────────────────────────

        private void DrawBlade(CupSim sim)
        {
            float r = cupArea.width * 0.5f;
            float len = r * 0.72f;      // 刀片长
            float thick = Mathf.Max(4f, r * 0.09f);

            Matrix4x4 old = GUI.matrix;
            Color prev = GUI.color;

            // ★ IMGUI 没有"旋转的矩形"这种东西，只能转坐标系再画正矩形。
            //   绕刀片中心转，画完必须把矩阵还回去 —— 忘了还，
            //   后面所有控件都会跟着歪，而且很难查。
            GUIUtility.RotateAroundPivot(sim.bladeAngle, cupArea.center);

            GUI.color = sim.bladeBroken
                ? new Color(0.42f, 0.36f, 0.34f)
                : new Color(0.86f, 0.90f, 0.96f);

            float y = cupArea.center.y - thick * 0.5f;
            GUI.DrawTexture(new Rect(cupArea.center.x - len * 0.5f, y, len, thick),
                            Texture2D.whiteTexture);

            // 另一叶（转 90 度）—— 两叶刀看起来才像破壁机
            float y2 = cupArea.center.y - len * 0.5f;
            GUI.DrawTexture(new Rect(cupArea.center.x - thick * 0.5f, y2, thick, len),
                            Texture2D.whiteTexture);

            GUI.matrix = old;
            GUI.color = prev;
        }

        // ── 粒子 ─────────────────────────────────────────────────────

        private void DrawParticles(CupSim sim)
        {
            Texture2D disc = ProceduralArt.Disc();
            Color prev = GUI.color;

            for (int i = 0; i < sim.particles.Count; i++)
            {
                SimParticle p = sim.particles[i];
                Rect rc = CupRect(p.pos, p.radius);

                // 颜色按食材 id 稳定分配 —— 和 3D 卡面是同一个色，
                // 玩家能把"杯子里的这一团"和"刚才投进去的那张牌"对上
                Color c = ProceduralArt.IngredientColor(p.id);

                // 被磨掉的粒子变小、变透明
                float k = 1f - p.wear * 0.55f;
                c.a = 0.95f - p.wear * 0.35f;

                GUI.color = c;
                GUI.DrawTexture(new Rect(rc.center.x - rc.width * 0.5f * k,
                                         rc.center.y - rc.height * 0.5f * k,
                                         rc.width * k, rc.height * k), disc);
            }

            GUI.color = prev;
        }

        // ── 飘字 ─────────────────────────────────────────────────────

        private void DrawFloaters(CupSim sim)
        {
            for (int i = 0; i < sim.floaters.Count; i++)
            {
                SimFloater f = sim.floaters[i];

                // 规格：飘字必须会消失。最后 40% 的时间拿来淡出。
                float t = f.life > 0f ? f.age / f.life : 1f;
                float alpha = t < 0.6f ? 1f : Mathf.InverseLerp(1f, 0.6f, t);

                Color c = f.color;
                c.a = Mathf.Clamp01(alpha);

                GUIStyle st = f.big ? floaterBig : floaterSmall;
                Color old = st.normal.textColor;
                st.normal.textColor = c;

                Rect rc = CupRect(f.pos, 0f);
                GUI.Label(new Rect(rc.center.x - 120f, rc.center.y - 16f, 240f, 34f),
                          f.text, st);

                st.normal.textColor = old;
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  坐标换算
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 归一化坐标 → 屏幕矩形。
        /// 注意 IMGUI 的 y 是向下的，而模拟里的 y 是向上的（数学坐标系），
        /// 所以这里要翻一次 —— 忘了翻的话整个杯子会上下颠倒。
        /// </summary>
        private Rect CupRect(Vector2 n, float radius)
        {
            float cx = cupArea.x + n.x * cupArea.width;
            float cy = cupArea.y + (1f - n.y) * cupArea.height;
            float rp = radius * cupArea.width;

            return new Rect(cx - rp, cy - rp, rp * 2f, rp * 2f);
        }
    }
}
