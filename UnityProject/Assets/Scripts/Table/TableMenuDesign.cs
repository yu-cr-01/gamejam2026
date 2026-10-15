using System.Collections.Generic;
using UnityEngine;
using GameJam.Data;

namespace GameJam.Prototype
{
    public partial class TableHud
    {
        private static readonly Color MenuInk = GameUiTheme.Ink;
        private static readonly Color MenuGold = new Color(0.77f, 0.60f, 0.33f);
        private static readonly Color MenuPaper = GameUiTheme.Cream;
        private static readonly Color MenuMuted = new Color(0.62f, 0.65f, 0.57f);
        private readonly Dictionary<int, GUIStyle> menuTextStyles = new Dictionary<int, GUIStyle>();
        private Vector2 campaignDesignScroll;
        private string campaignDesignKey;
        private static Rect menuPointerRect;
        public static bool MenuPointerBlocked
        {
            get { return menuPointerRect.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y)); }
        }

        private Matrix4x4 BeginMenuCanvas(out float width, out float height, out float scale)
        {
            scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            scale = Mathf.Max(0.1f, scale);
            width = Screen.width / scale;
            height = Screen.height / scale;
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            return previous;
        }

        private void MenuText(Rect at, string text, int size, Color color, TextAnchor alignment = TextAnchor.UpperLeft)
        {
            int key = size * 10 + (int)alignment;
            GUIStyle style;
            if (!menuTextStyles.TryGetValue(key, out style))
            {
                style = new GUIStyle(GUI.skin.label) { font = cjkFont, fontSize = size, alignment = alignment,
                    wordWrap = true, padding = new RectOffset(0, 0, 0, 0) };
                menuTextStyles.Add(key, style);
            }
            style.normal.textColor = color;
            style.hover.textColor = color;
            style.active.textColor = color;
            style.focused.textColor = color;
            GUI.Label(at, text, style);
        }

        private static void MenuFill(Rect at, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(at, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static void MenuBorder(Rect at, Color color, float stroke = 1f)
        {
            MenuFill(new Rect(at.x, at.y, at.width, stroke), color);
            MenuFill(new Rect(at.x, at.yMax - stroke, at.width, stroke), color);
            MenuFill(new Rect(at.x, at.y, stroke, at.height), color);
            MenuFill(new Rect(at.xMax - stroke, at.y, stroke, at.height), color);
        }

        private void MenuSheet(Rect at, bool paper)
        {
            MenuFill(new Rect(at.x+7f,at.y+9f,at.width,at.height),new Color(0,0,0,.3f));
            GameUiTheme.DrawSheet(at,!paper);
        }

        private bool MenuButton(Rect at, string text, bool primary, bool enabled = true)
        {
            return ArtButton(at,text,primary,enabled);
        }

        private void DrawDesignedTitle()
        {
            DrawCollageTitle();
        }

        private int MenuLevelTarget(LevelData level)
        {
            return TableSettings.UseRulesV21 && TableSettings.V21TargetScore > 0 ? TableSettings.V21TargetScore : level.targetScore;
        }

        private void DrawDesignedCampaign()
        {
            if (turnLoop == null) return;
            float width, height, scale;
            Matrix4x4 previous = BeginMenuCanvas(out width, out height, out scale);
            Rect page = new Rect((width - 850f) * 0.5f, (height - 556f) * 0.5f, 850f, 556f);
            MenuFill(new Rect(0f, 0f, width, height), new Color(0.015f, 0.025f, 0.02f, 0.68f));
            MenuSheet(page, false);
            v21PanelRects.Add(new Rect(page.x*scale, page.y*scale, page.width*scale, page.height*scale));
            MenuText(new Rect(page.x+30f, page.y+25f, 720f, 22f), "提 示", 12, MenuGold);
            MenuText(new Rect(page.x+30f, page.y+63f, 790f, 48f), turnLoop.campaignOverlayTitle, 30, MenuPaper);
            Rect sheet = new Rect(page.x+26f, page.y+132f, page.width-52f, 306f);
            MenuSheet(sheet, true);
            string content = turnLoop.campaignOverlayText ?? "";
            if (turnLoop.campaignOverlayCallouts != null)
                foreach (string callout in turnLoop.campaignOverlayCallouts)
                    if (!string.IsNullOrEmpty(callout)) content += "\n\n" + callout;
            string key = turnLoop.campaignOverlayTitle + content;
            if (campaignDesignKey != key) { campaignDesignScroll = Vector2.zero; campaignDesignKey = key; }
            GUIStyle prose = new GUIStyle(GUI.skin.label) { font = cjkFont, fontSize = 20, wordWrap = true,
                padding = new RectOffset(0,0,0,0) };
            prose.normal.textColor = MenuInk;
            prose.hover.textColor = MenuInk;
            float textW = sheet.width-72f;
            float textH = Mathf.Max(sheet.height-46f, prose.CalcHeight(new GUIContent(content), textW));
            Rect viewport = new Rect(sheet.x+24f, sheet.y+22f, sheet.width-48f, sheet.height-44f);
            campaignDesignScroll = GUI.BeginScrollView(viewport, campaignDesignScroll, new Rect(0f,0f,textW,textH), false, false);
            GUI.Label(new Rect(0f,0f,textW,textH), content, prose);
            GUI.EndScrollView();
            MenuText(new Rect(page.x+30f, page.y+480f, 450f, 32f), "", 14, MenuMuted);
            if (MenuButton(new Rect(page.xMax-300f, page.y+474f, 270f, 48f), "继 续   →", true,
                !turnLoop.paused && !turnLoop.settingsOpen)) turnLoop.ContinueCampaignDialog();
            GUI.matrix = previous;
        }

        private void OnDestroy()
        {
            if (collagePaper != null) CardFactory.DestroySafe(collagePaper);
            if (collageSeal != null) CardFactory.DestroySafe(collageSeal);
            menuPointerRect = new Rect();
        }

        private void DrawDesignedLevels()
        {
            if (turnLoop == null || turnLoop.levels == null || turnLoop.levels.Count == 0) return;
            float width, height, scale;
            Matrix4x4 previous = BeginMenuCanvas(out width, out height, out scale);
            float w = Mathf.Min(1120f, width - 64f);
            Rect page = new Rect((width - w) * 0.5f, (height - 598f) * 0.5f, w, 598f);
            MenuFill(new Rect(0f, 0f, width, height), new Color(0.015f, 0.025f, 0.02f, 0.60f));
            MenuSheet(page, false);
            v21PanelRects.Add(new Rect(page.x*scale, page.y*scale, page.width*scale, page.height*scale));
            float x = page.x + 32f, y = page.y;
            MenuText(new Rect(x, y + 27f, 550f, 24f), "关 卡", 12, MenuGold);
            MenuText(new Rect(x, y + 62f, 600f, 45f), "选择关卡", 31, MenuPaper);
            MenuText(new Rect(x, y + 114f, 650f, 28f), "", 14, MenuMuted);
            string closeText = turnLoop.IsLevelSelect ? "返回主菜单" : "关闭";
            if (MenuButton(new Rect(page.xMax - 160f, y + 33f, 128f, 34f), closeText, false))
            {
                levelWindowOpen = false;
                if (turnLoop.IsLevelSelect) turnLoop.ReturnToTitle();
            }
            MenuFill(new Rect(x, y + 155f, w - 64f, 1f), MenuGold);
            float leftWidth = w * 0.61f;
            Rect list = new Rect(x, y + 177f, leftWidth, 349f);
            float cardW = (leftWidth - 30f) * 0.5f;
            float contentHeight = Mathf.Max(list.height, Mathf.Ceil(turnLoop.levels.Count / 2f) * 160f);
            levelWindowScroll = GUI.BeginScrollView(list, levelWindowScroll, new Rect(0f, 0f, leftWidth - 18f, contentHeight), false, false);
            for (int i = 0; i < turnLoop.levels.Count; i++)
            {
                LevelData level = turnLoop.levels[i];
                if (level == null) continue;
                Rect card = new Rect((i % 2) * (cardW + 12f), (i / 2) * 160f, cardW, 147f);
                bool picked = i == levelWindowPick;
                bool hot = card.Contains(Event.current.mousePosition);
                MenuFill(card, picked ? new Color(0.88f, 0.81f, 0.67f) : hot ? new Color(0.17f, 0.25f, 0.21f) : new Color(0.10f, 0.16f, 0.135f));
                MenuBorder(card, picked ? MenuGold : new Color(0.28f, 0.35f, 0.28f), picked ? 2f : 1f);
                Color ink = picked ? MenuInk : MenuPaper;
                MenuText(new Rect(card.x + 17f, card.y + 12f, 60f, 46f), (i + 1).ToString("00"), 33, picked ? new Color(0.43f, 0.32f, 0.17f) : MenuGold);
                MenuText(new Rect(card.x + 85f, card.y + 18f, cardW - 100f, 22f), picked ? "已选择" : "关卡", 11, picked ? MenuInk : MenuMuted, TextAnchor.MiddleRight);
                string name = level.name;
                int separator = name.IndexOf('·');
                if (separator >= 0) name = name.Substring(separator + 1).Trim();
                MenuText(new Rect(card.x + 17f, card.y + 65f, cardW - 30f, 34f), name, 22, ink);
                MenuText(new Rect(card.x + 17f, card.y + 115f, cardW - 30f, 22f), "目标 " + MenuLevelTarget(level) + " 分     /     " + LevelStatusText(i).Replace("▸ ", ""), 12, ink);
                if (GUI.Button(card, GUIContent.none, GUIStyle.none)) { GUI.FocusControl(null); PickLevelInWindow(i); }
            }
            GUI.EndScrollView();
            Rect detail = new Rect(x + leftWidth + 24f, y + 177f, w - leftWidth - 88f, 349f);
            MenuSheet(detail, true);
            int selected = Mathf.Clamp(levelWindowPick, 0, turnLoop.levels.Count - 1);
            LevelData pickedLevel = turnLoop.levels[selected];
            if (pickedLevel != null)
            {
                float dx = detail.x + 24f;
                MenuText(new Rect(dx, detail.y + 22f, detail.width - 48f, 22f), "关 卡   /   " + (selected+1).ToString("00"), 12, new Color(0.43f,0.34f,0.20f));
                MenuText(new Rect(dx, detail.y + 62f, detail.width - 48f, 66f), pickedLevel.name, 23, MenuInk);
                MenuFill(new Rect(dx, detail.y + 138f, detail.width - 48f, 1f), MenuGold);
                MenuText(new Rect(dx, detail.y + 159f, detail.width - 48f, 23f), "通关目标", 14, MenuInk);
                MenuText(new Rect(dx, detail.y + 190f, detail.width - 48f, 62f), MenuLevelTarget(pickedLevel) + " 分", 43, MenuInk);
                MenuText(new Rect(dx, detail.y + 272f, detail.width - 48f, 56f), "在 " + GameJam.Rules.LevelRun.TurnsPerLevel + " 个回合内挑战目标。", 14, MenuInk);
            }
            MenuText(new Rect(x, y + 550f, leftWidth, 24f), "选择关卡后点击开始。", 13, MenuMuted);
            if (MenuButton(new Rect(detail.x, y + 541f, detail.width, 38f), "开 始   →", true, turnLoop.IsLevelSelect && levelWindowPick >= 0))
                ConfirmLevelInWindow();
            GUI.matrix = previous;
        }
    }
}
