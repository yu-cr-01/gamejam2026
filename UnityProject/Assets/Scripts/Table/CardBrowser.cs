using System.Collections.Generic;
using UnityEngine;
using GameJam.Data;

namespace GameJam.Prototype
{
    /// <summary>
    /// 卡牌图鉴（开发/验收用界面）：把 `cards_v21.json` 里的**素材与法术全列出来**，
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
    /// 按键：F1 开关（在 Inspector 里可以改）。**Esc 菜单里也有入口** ——
    /// 光有快捷键等于没入口：用户反馈"其实已经有了，但没人发现"。
    ///
    /// 【★ 和 HUD 的前后关系】OnGUI 的先后由脚本执行顺序决定，实测这个组件的 OnGUI
    ///   跑在 TableHud **前面**，于是回合条 / 卡牌信息条会浮在图鉴上面，
    ///   左边那排卡面被挡掉一半，看起来像界面坏了。
    ///   （先试过 [DefaultExecutionOrder] 把本组件排到最后 —— 对 OnGUI 不起作用。）
    ///   现在的做法是反过来：**图鉴开着时 TableHud 整帧不画**（见 TableHud.OnGUI），
    ///   一整屏的"查资料"界面本来就该独占屏幕。
    /// </summary>
    public class CardBrowser : MonoBehaviour
    {
        public KeyCode toggleKey = KeyCode.F1;

        private bool open;
        private string filter = "";
        private string seriesJump = "";      // 按系列跳转（见 DrawSeriesJump）
        private bool showMaterials = true;
        private bool showSpells = true;
        private Vector2 scroll;
        private Vector2 seriesScroll;

        // 卡片尺寸
        private const float CardW = 96f;
        private const float CardH = 134f;
        private const float RowGap = 10f;

        private GUIStyle title, body, dim, btn, btnOn, field;
        private Font cjkFont;

        // ══════════════════════════════════════════════════════════════

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey)) Toggle();
        }

        /// <summary>
        /// 开 / 关图鉴 —— **给 HUD 的 Esc 菜单用**。
        ///
        /// 【为什么要有这个方法】图鉴原来只有 F1 一个入口，用户根本没发现它存在
        ///   （原话："其实已经有了，用户没发现"）。菜单里那一项要能直接开，
        ///   就不能让 HUD 去伪造一个 F1 按键 —— 那和"只有快捷键"没区别。
        ///   Esc 关面板也走它（见 TableHud.HandleMenuKeys 的一层层退）。
        /// </summary>
        public void Toggle()
        {
            SetOpen(!open);
        }

        /// <summary>直接指定开关（Esc 链上用）。打开时会把 v2.1 的卡并进图鉴。</summary>
        public void SetOpen(bool want)
        {
            open = want;
            if (open)
            {
                CardSpecs.EnsureRegistered();   // 打开时把 v2.1 的卡并进图鉴
                filter = "";                    // 每次打开都从"全部"开始，免得上次的筛选把人绕晕
            }
        }

        /// <summary>图鉴现在开着吗（HUD 的 Esc 链要问）。</summary>
        public bool IsOpen { get { return open; } }

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

            GUILayout.Label("卡牌图鉴 v2.1", title);
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

            // ── 系列跳转行 ────────────────────────────────────────────
            //   素材有 30 多张、按系列分组；只靠滚动条找人太慢，给一排"跳到某系列"。
            //   和上面的文字筛选是**与**关系（都满足才显示），点「全部」清掉。
            DrawSeriesJump(new Rect(18f, 62f, w - 36f, 32f));

            // ── 列表 ──────────────────────────────────────────────────
            List<Ingredient> materials = showMaterials ? CardSpecs.Materials() : null;
            List<Spell> spells = showSpells ? CardSpecs.Spells() : null;

            GUILayout.BeginArea(new Rect(18f, 102f, w - 36f, h - 120f));
            scroll = GUILayout.BeginScrollView(scroll, false, true);

            string lastSeries = "";
            if (materials != null)
            {
                for (int i = 0; i < materials.Count; i++)
                {
                    Ingredient ing = materials[i];
                    if (ing == null || !Match(ing.name, ing.id)) continue;
                    if (!MatchSeries(ing.series)) continue;

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

        /// <summary>系列跳转的过滤。和文字筛选是"与"关系。</summary>
        private bool MatchSeries(string series)
        {
            if (string.IsNullOrEmpty(seriesJump)) return true;
            return series == seriesJump;
        }

        /// <summary>
        /// 一排"跳到某系列"的按钮。
        ///
        /// 【为什么要它】素材三十多张、按系列分组，滚动条翻半天才能到"火药"那一组。
        ///   系列名从卡表现取（不写死），以后加系列这里自动多一个按钮。
        /// </summary>
        private void DrawSeriesJump(Rect area)
        {
            List<Ingredient> all = CardSpecs.Materials();
            if (all == null || all.Count == 0) return;

            // 去重（卡表按系列连排，顺序保留）
            List<string> series = new List<string>();
            for (int i = 0; i < all.Count; i++)
            {
                Ingredient ing = all[i];
                if (ing == null || string.IsNullOrEmpty(ing.series)) continue;
                if (!series.Contains(ing.series)) series.Add(ing.series);
            }
            if (series.Count == 0) return;

            GUILayout.BeginArea(area);
            seriesScroll = GUILayout.BeginScrollView(seriesScroll, false, false);

            GUILayout.BeginHorizontal();
            GUILayout.Label("系列：", dim, GUILayout.Width(46f));

            bool allOn = string.IsNullOrEmpty(seriesJump);
            if (GUILayout.Button("全部", allOn ? btnOn : btn, GUILayout.Width(56f))) seriesJump = "";

            for (int i = 0; i < series.Count; i++)
            {
                string s = series[i];
                bool on = (seriesJump == s);
                if (GUILayout.Button(s, on ? btnOn : btn, GUILayout.Width(88f)))
                    seriesJump = on ? "" : s;   // 再点一下 = 取消
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ── 一行一张卡 ────────────────────────────────────────────────

        private void DrawMaterial(Ingredient ing)
        {
            GUILayout.BeginHorizontal(GUILayout.MinHeight(CardH + RowGap));

            Rect r = GUILayoutUtility.GetRect(CardW, CardH, GUILayout.Width(CardW), GUILayout.Height(CardH));
            GUI.DrawTexture(r, MaterialFace(ing), ScaleMode.ScaleToFit);

            GUILayout.BeginVertical();
            GUILayout.Label(ing.name + "（" + ing.id + "）　" + AttrText(ing.attrs), title);

            // ── v2.1 的 H / D / V ──
            //   这是 v2.1 玩法真正在用的三个数（H 硬度、D 耐久、V 计分倾向），
            //   和上面那行旧属性（硫性/汞性…）是**两套口径**，所以分开显示、写清名字，
            //   不能混成一行 —— 混了策划会拿着旧数值去对 v2.1 的账。
            //   V=0 的卡在卡表里就是"数值还没定"，这里照实说，不写 0（写 0 会被当成"价值是 0"）。
            GUILayout.Label("H 硬度 " + ing.h + "　D 耐久 " + ing.d + "　"
                            + (ing.v == 0
                               ? "V 数值未定"
                               : "V 计分 " + ing.v + (string.IsNullOrEmpty(ing.vGrade) ? "" : "（" + ing.vGrade + "）")),
                            ing.v == 0 ? dim : body);

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

            // ★ v3.0：法术**不标 H/D/V**（原文：「法术卡不标 H/D/V，只标稀有度」）。
            //   所以这里**故意不画**属性那一行，改画一句口径说明 ——
            //   否则策划看着"别的卡都有三个数、法术没有"，会以为是数据漏了。
            GUILayout.Label("法术：不标 H/D/V（v3.0 口径），只标稀有度"
                            + (string.IsNullOrEmpty(sp.rarity) ? "（★ 这张没写稀有度）" : "：" + sp.rarity),
                            string.IsNullOrEmpty(sp.rarity) ? dim : body);

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
