using System.Collections.Generic;
using UnityEngine;
using GameJam.Data;

namespace GameJam.Prototype
{
    /// <summary>
    /// 卡牌总览（开发/验收用界面）：把 `cards_v21.json` 里的**素材与法术全列出来**，
    /// 每张都带卡面 —— 有美术就用美术，没有就退到程序化占位卡面。
    ///
    /// 【为什么单独一个组件，不塞进 TableHud】
    ///   TableHud 是"玩的时候要看的"（阶段提示、按钮、结算）；
    ///   这个是"查资料用的"，跟玩法无关，也不该跟着关卡阶段变。
    ///   放独立组件还有一个好处：以后真要删掉它，删一个文件 + 一行 AddComponent 就行。
    ///
    /// 【为什么卡面不直接读 PNG】
    ///   素材用 CardArt（正式美术 + 元素映射），法术没有美术，走 ProceduralArt 的程序化卡面。
    ///   两条路都返回 Texture2D，这里只负责画 —— 美术到位后这里一行都不用改。
    ///
    /// 按键：F1 开关（在 Inspector 里可以改）。
    /// </summary>
    public class CardBrowser : MonoBehaviour
    {
        public KeyCode toggleKey = KeyCode.F1;

        private bool open;
        private string filter = "";
        private bool showMaterials = true;
        private bool showSpells = true;
        private Vector2 scroll;

        // 卡片尺寸
        private const float CardW = 96f;
        private const float CardH = 134f;
        private const float RowGap = 10f;

        private GUIStyle title, body, dim, btn, btnOn, field;
        private Font cjkFont;

        // ══════════════════════════════════════════════════════════════

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
            {
                open = !open;
                if (open) CardSpecs.EnsureRegistered();   // 打开时把 v2.1 的卡并进图鉴
            }
        }

        private void OnGUI()
        {
            if (!open) return;
            EnsureStyles();

            float w = Screen.width, h = Screen.height;

            // 铺一层底：直接用半透明纯色，比 9 宫格拉伸省事
            Color prev = GUI.color;
            GUI.color = new Color(0.05f, 0.06f, 0.08f, 0.97f);
            GUI.DrawTexture(new Rect(0f, 0f, w, h), Texture2D.whiteTexture);
            GUI.color = prev;

            // ── 顶栏 ──────────────────────────────────────────────────
            GUILayout.BeginArea(new Rect(18f, 12f, w - 36f, 44f));
            GUILayout.BeginHorizontal();

            GUILayout.Label("卡牌总览 v2.1", title);
            GUILayout.Space(12f);

            showMaterials = GUILayout.Toggle(showMaterials, " 素材 ", showMaterials ? btnOn : btn, GUILayout.Width(70f));
            showSpells    = GUILayout.Toggle(showSpells, " 法术 ", showSpells ? btnOn : btn, GUILayout.Width(70f));

            GUILayout.Space(12f);
            GUILayout.Label("筛选：", dim, GUILayout.Width(46f));
            filter = GUILayout.TextField(filter != null ? filter : "", field, GUILayout.Width(180f));

            GUILayout.FlexibleSpace();
            GUILayout.Label(CardSpecs.Source, dim);
            GUILayout.Space(10f);
            if (GUILayout.Button("关闭 (F1)", btn, GUILayout.Width(96f))) open = false;

            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            // ── 列表 ──────────────────────────────────────────────────
            List<Ingredient> materials = showMaterials ? CardSpecs.Materials() : null;
            List<Spell> spells = showSpells ? CardSpecs.Spells() : null;

            GUILayout.BeginArea(new Rect(18f, 64f, w - 36f, h - 82f));
            scroll = GUILayout.BeginScrollView(scroll, false, true);

            string lastSeries = "";
            if (materials != null)
            {
                for (int i = 0; i < materials.Count; i++)
                {
                    Ingredient ing = materials[i];
                    if (ing == null || !Match(ing.name, ing.id)) continue;

                    if (ing.series != lastSeries)
                    {
                        lastSeries = ing.series;
                        GUILayout.Space(6f);
                        GUILayout.Label("── " + (string.IsNullOrEmpty(ing.series) ? "（未分组）" : ing.series) + " ──", title);
                    }
                    DrawMaterial(ing);
                }
            }

            if (spells != null)
            {
                GUILayout.Space(14f);
                GUILayout.Label("── 法术 ──", title);
                for (int i = 0; i < spells.Count; i++)
                {
                    Spell sp = spells[i];
                    if (sp == null || !Match(sp.name, sp.id)) continue;
                    DrawSpell(sp);
                }
            }

            GUILayout.Space(20f);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private bool Match(string name, string id)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            string f = filter.Trim();
            if (string.IsNullOrEmpty(f)) return true;

            if (!string.IsNullOrEmpty(name) && name.Contains(f)) return true;
            if (!string.IsNullOrEmpty(id) && id.ToLowerInvariant().Contains(f.ToLowerInvariant())) return true;
            return false;
        }

        // ── 一行一张卡 ────────────────────────────────────────────────

        private void DrawMaterial(Ingredient ing)
        {
            GUILayout.BeginHorizontal(GUILayout.MinHeight(CardH + RowGap));

            Rect r = GUILayoutUtility.GetRect(CardW, CardH, GUILayout.Width(CardW), GUILayout.Height(CardH));
            GUI.DrawTexture(r, MaterialFace(ing), ScaleMode.ScaleToFit);

            GUILayout.BeginVertical();
            GUILayout.Label(ing.name + "（" + ing.id + "）　" + AttrText(ing.attrs), title);

            string kind = ing.FormAndTags();
            if (!string.IsNullOrEmpty(kind)) GUILayout.Label(kind, body);

            string tr = ing.TransitionsText();
            if (!string.IsNullOrEmpty(tr)) GUILayout.Label("形态转换：" + tr, dim);

            if (ing.exhaust != null && ing.exhaust.Length > 0)
                GUILayout.Label("D耗尽：" + string.Join(" + ", ing.exhaust), dim);

            if (!string.IsNullOrEmpty(ing.startup))   GUILayout.Label("启动：" + ing.startup, dim);
            if (!string.IsNullOrEmpty(ing.sacrifice)) GUILayout.Label("献祭：" + ing.sacrifice, dim);
            if (!string.IsNullOrEmpty(ing.note))      GUILayout.Label("备注：" + ing.note, dim);

            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.Space(RowGap);
        }

        private void DrawSpell(Spell sp)
        {
            GUILayout.BeginHorizontal(GUILayout.MinHeight(CardH + RowGap));

            Rect r = GUILayoutUtility.GetRect(CardW, CardH, GUILayout.Width(CardW), GUILayout.Height(CardH));
            GUI.DrawTexture(r, SpellFace(sp), ScaleMode.ScaleToFit);

            GUILayout.BeginVertical();
            GUILayout.Label(sp.name + "（" + sp.id + "）　" + sp.series, title);

            string kind = sp.KindText();
            if (!string.IsNullOrEmpty(kind)) GUILayout.Label(kind, body);

            string text = sp.CardText();
            if (!string.IsNullOrEmpty(text)) GUILayout.Label("需求：" + text, dim);

            if (sp.sources != null && sp.sources.Length > 0)
                GUILayout.Label("来源：" + string.Join(" / ", sp.sources), dim);

            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.Space(RowGap);
        }

        private string AttrText(AttrSet attrs)
        {
            if (attrs == null) return "";
            string s = attrs.DescribeNonZero();
            return string.IsNullOrEmpty(s) ? "（无数值，等策划补）" : s;
        }

        // ── 卡面：有美术用美术，没有就用程序化占位 ─────────────────────

        private Texture2D MaterialFace(Ingredient ing)
        {
            string element = CardArt.ElementOf(ing.id, ing, false);
            if (element != null)
            {
                Texture2D art = CardArt.Face(element);
                if (art != null) return art;
            }
            return ProceduralArt.CardFace(ProceduralArt.IngredientColor(ing.id),
                                          ProceduralArt.DominantAttr(ing));
        }

        private Texture2D SpellFace(Spell sp)
        {
            // 法术没有专属美术 —— 走程序化卡面；附魔类型映射成印记形状，
            // 这样一眼能分出热/冷/酸（映射只是展示口径，不代表数值）
            return ProceduralArt.CardFace(ProceduralArt.ModuleColor(sp.id), DominantOfEnchant(sp.enchant));
        }

        private static AttrId DominantOfEnchant(string enchant)
        {
            if (enchant == "热") return AttrId.Sulfur;     // 气/升腾
            if (enchant == "冷") return AttrId.Mercury;    // 液/凝结
            if (enchant == "酸") return AttrId.Salt;       // 固/腐蚀
            return AttrId.Mercury;
        }

        // ── 样式 ──────────────────────────────────────────────────────

        private void EnsureStyles()
        {
            if (title != null) return;

            if (cjkFont == null)
            {
                string[] candidates =
                {
                    "Microsoft YaHei", "微软雅黑", "SimHei", "SimSun",
                    "PingFang SC", "Noto Sans CJK SC", "Arial Unicode MS"
                };
                cjkFont = Font.CreateDynamicFontFromOSFont(candidates, 18);
            }

            // ★ 中文不要用 FontStyle.Bold（雅黑没有独立粗体，伪粗体会糊成双影）
            title = new GUIStyle(GUI.skin.label) { fontSize = 19, wordWrap = true };
            title.normal.textColor = new Color(0.93f, 0.95f, 0.98f);

            body = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            body.normal.textColor = new Color(0.87f, 0.90f, 0.94f);

            dim = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
            dim.normal.textColor = new Color(0.62f, 0.68f, 0.77f);

            btn = new GUIStyle(GUI.skin.button) { fontSize = 14 };
            btnOn = new GUIStyle(btn);
            btnOn.normal.textColor = new Color(0.35f, 0.95f, 0.60f);

            field = new GUIStyle(GUI.skin.textField) { fontSize = 14 };

            if (cjkFont != null)
            {
                title.font = cjkFont; body.font = cjkFont; dim.font = cjkFont;
                btn.font = cjkFont; btnOn.font = cjkFont; field.font = cjkFont;
            }
        }
    }
}
