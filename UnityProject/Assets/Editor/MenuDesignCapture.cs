using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using GameJam.Prototype;

namespace GameJam.EditorTools
{
    /// <summary>Isolated-project Play mode captures, including IMGUI menus.</summary>
    [InitializeOnLoad]
    public static class MenuDesignCapture
    {
        private const string Prefix = "GameJam.MenuCapture.";
        private static double next;

        static MenuDesignCapture()
        {
            if (SessionState.GetBool(Prefix + "Active", false)) EditorApplication.update += Tick;
        }

        public static void Run()
        {
            if (Environment.GetEnvironmentVariable("DSH_MENU_CAPTURE") != "1")
                throw new InvalidOperationException("Run captures in a project copy with DSH_MENU_CAPTURE=1.");
            if (PlayerSettings.companyName != "GameJamPreviewOnly")
                throw new InvalidOperationException("The capture copy must use companyName GameJamPreviewOnly to isolate PlayerPrefs.");
            string output = Environment.GetEnvironmentVariable("DSH_CAPTURE_DIR");
            if (string.IsNullOrEmpty(output)) throw new InvalidOperationException("DSH_CAPTURE_DIR is required.");
            Directory.CreateDirectory(output);
            SessionState.SetString(Prefix + "Output", output);
            SessionState.SetInt(Prefix + "Stage", 0);
            SessionState.SetBool(Prefix + "Active", true);
            Type gameView = Type.GetType("UnityEditor.GameView,UnityEditor");
            EditorWindow.GetWindow(gameView).Show();
            typeof(AutoPlayHarness).GetMethod("SetGameViewSizeEx", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { 1280, 720 });
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        private static void Shot(string name)
        {
            string path = Path.Combine(SessionState.GetString(Prefix + "Output", ""), name);
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log("[MenuCapture] " + path + " | " + Screen.width + "x" + Screen.height);
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            if (EditorApplication.timeSinceStartup < next) return;
            TableTurnLoop loop = UnityEngine.Object.FindObjectOfType<TableTurnLoop>();
            if (loop == null) return;
            int stage = SessionState.GetInt(Prefix + "Stage", 0);
            switch (stage)
            {
                case 0:
                    loop.ReturnToTitle();
                    GameObject stale=new GameObject("SlotLabel1");
                    stale.transform.SetParent(loop.setup.board.transform,false);
                    stale.AddComponent<TextMesh>().text="上 桌 位";
                    TableHud reloadHud=UnityEngine.Object.FindObjectOfType<TableHud>();
                    reloadHud.enabled=false;reloadHud.enabled=true;
                    next = EditorApplication.timeSinceStartup + 2.0;
                    break;
                case 1:
                    Shot("menu_title.png");
                    next = EditorApplication.timeSinceStartup + 1.0;
                    break;
                case 2:
                    CampaignRulesV1.Progress.tutorialCompleted = true;
                    loop.titleRig.ActivateMenu(TableTitleRig.IdNew);
                    if (!loop.IsLevelSelect) throw new InvalidOperationException("New game did not enter level selection.");
                    next = EditorApplication.timeSinceStartup + 1.5;
                    break;
                case 3:
                    TableHud hud = UnityEngine.Object.FindObjectOfType<TableHud>();
                    hud.PickLevelInWindow(Mathf.Min(1, loop.levels.Count-1));
                    next = EditorApplication.timeSinceStartup + 1.0;
                    break;
                case 4:
                    Shot("menu_levels.png");
                    next = EditorApplication.timeSinceStartup + 1.0;
                    break;
                case 5:
                    loop.ReturnToTitle();
                    loop.AutoStartFirstLevel();
                    if (loop.rulesV21 != null) loop.rulesV21.score = 36;
                    if (loop.juicer != null) loop.juicer.SetScore(36,60);
                    next = EditorApplication.timeSinceStartup + 1.5;
                    break;
                case 6:
                    Shot("menu_brief.png");
                    next = EditorApplication.timeSinceStartup + 1.0;
                    break;
                case 7:
                    for (int i = 0; i < 8 && loop.phase == TablePhase.CampaignDialog; i++) loop.ContinueCampaignDialog();
                    next = EditorApplication.timeSinceStartup + 1.0;
                    break;
                case 8:
                    VerifyCenteredCamera(loop.setup);
                    if(GameObject.Find("SlotLabel0")!=null||GameObject.Find("SlotLabel1")!=null)
                        throw new InvalidOperationException("Floating legacy slot labels still exist.");
                    foreach(TextMesh text in UnityEngine.Object.FindObjectsOfType<TextMesh>())
                        if(text.font!=GameUiTheme.Font)throw new InvalidOperationException("A world label is using a different font: "+text.name);
                    foreach(TextMesh caption in UnityEngine.Object.FindObjectsOfType<TextMesh>())
                    {
                        if(caption.name!="CaptionText")continue;
                        Bounds letters=caption.GetComponent<Renderer>().bounds;
                        Bounds frame=caption.transform.parent.Find("CaptionFrame").GetComponent<Renderer>().bounds;
                        if(letters.min.x<frame.min.x-.003f||letters.max.x>frame.max.x+.003f||letters.min.z<frame.min.z-.003f||letters.max.z>frame.max.z+.003f)
                            throw new InvalidOperationException("Slot lettering extends outside its frame.");
                    }
                    Shot("scoreboard_large.png");
                    next = EditorApplication.timeSinceStartup + 1.5;
                    break;
                case 9:
                    loop.paused=true; next=EditorApplication.timeSinceStartup+1; break;
                case 10:
                    Shot("ui_pause.png"); next=EditorApplication.timeSinceStartup+1; break;
                case 11:
                    loop.paused=false; loop.settingsOpen=true; next=EditorApplication.timeSinceStartup+1; break;
                case 12:
                    Shot("ui_settings.png"); next=EditorApplication.timeSinceStartup+1; break;
                case 13:
                    loop.settingsOpen=false; loop.phase=TablePhase.TurnResult; next=EditorApplication.timeSinceStartup+1; break;
                case 14:
                    Shot("ui_turn_result.png"); next=EditorApplication.timeSinceStartup+1; break;
                case 15:
                    loop.phase=TablePhase.Select; loop.EndRoundOrLevel();
                    for(int i=0;i<8&&loop.phase==TablePhase.CampaignDialog;i++)loop.ContinueCampaignDialog();
                    if(loop.phase!=TablePhase.Shop)throw new InvalidOperationException("Round completion did not enter the material shop.");
                    next=EditorApplication.timeSinceStartup+1; break;
                case 16:
                    Shot("ui_shop.png"); next=EditorApplication.timeSinceStartup+1; break;
                case 17:
                    loop.rulesV21.score=132; loop.rulesV21.levelOver=true;
                    loop.rulesV21.endReason="回合结束，实验记录已封存。";
                    loop.phase=TablePhase.LevelEnd; next=EditorApplication.timeSinceStartup+1; break;
                case 18:
                    Shot("ui_level_end.png"); next=EditorApplication.timeSinceStartup+1; break;
                case 19:
                    loop.level.turn.score=loop.rulesV21.score; loop.level.turn.targetScore=loop.rulesV21.targetScore;
                    loop.level.Finish(); loop.phase=TablePhase.LevelResult;
                    next=EditorApplication.timeSinceStartup+1; break;
                case 20:
                    Shot("ui_level_result.png"); next=EditorApplication.timeSinceStartup+1; break;
                case 21:
                    UnityEngine.Object.FindObjectOfType<CardBrowser>().SetOpen(true);
                    next=EditorApplication.timeSinceStartup+1; break;
                case 22:
                    Shot("ui_catalog.png"); next=EditorApplication.timeSinceStartup+1; break;
                case 23:
                    UnityEngine.Object.FindObjectOfType<CardBrowser>().SetOpen(false);
                    loop.ReturnToTitle(); loop.titleRig.ActivateMenu(TableTitleRig.IdNew);
                    TableHud deckHud=UnityEngine.Object.FindObjectOfType<TableHud>();
                    deckHud.PickLevelInWindow(0);deckHud.ConfirmLevelInWindow();
                    next=EditorApplication.timeSinceStartup+1; break;
                case 24:
                    Shot("ui_decks.png"); next=EditorApplication.timeSinceStartup+1; break;
                case 25:
                    TableHud chooseHud=UnityEngine.Object.FindObjectOfType<TableHud>();
                    chooseHud.PickDeckInWindow(0);chooseHud.ConfirmDeckInWindow();
                    next=EditorApplication.timeSinceStartup+1; break;
                case 26:
                    Shot("ui_blade.png"); next=EditorApplication.timeSinceStartup+1; break;
                case 27:
                    UnityEngine.Object.FindObjectOfType<TableHud>().SetRulesReportOpen(true);
                    next=EditorApplication.timeSinceStartup+1; break;
                case 28:
                    Shot("ui_rules.png"); next=EditorApplication.timeSinceStartup+1; break;
                default:
                    foreach (string name in new[] { "menu_title.png", "menu_levels.png", "menu_brief.png", "scoreboard_large.png", "ui_pause.png", "ui_settings.png", "ui_turn_result.png", "ui_shop.png", "ui_level_end.png", "ui_level_result.png", "ui_catalog.png", "ui_decks.png", "ui_blade.png", "ui_rules.png" })
                        if (!File.Exists(Path.Combine(SessionState.GetString(Prefix + "Output", ""), name)))
                            throw new IOException("Missing screenshot: " + name);
                    Debug.Log("[MenuCapture] Complete; title activation and level selection checked.");
                    SessionState.SetBool(Prefix + "Active", false);
                    EditorApplication.update -= Tick;
                    EditorApplication.Exit(0);
                    return;
            }
            SessionState.SetInt(Prefix + "Stage", stage + 1);
        }

        private static void VerifyCenteredCamera(TableSetup setup)
        {
            CameraRig rig = UnityEngine.Object.FindObjectOfType<CameraRig>();
            Camera camera = rig.cam;
            Bounds table = (Bounds)typeof(TableSetup).GetField("tableBounds", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(setup);
            var points = (System.Collections.Generic.List<Vector3>)typeof(TableSetup)
                .GetField("boardFitPoints", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(setup);
            float originalAspect = camera.aspect;
            foreach (float aspect in new[] { 4f / 3f, 1460f / 1017f, 16f / 9f, 21f / 9f })
            {
                camera.aspect = aspect;
                setup.ReframeBoardView(false);
                rig.SnapTo("hand");
                rig.SnapTo(TableSetup.BoardView);
                if (Mathf.Abs(camera.transform.position.x - table.center.x) > .0001f
                    || Mathf.Abs(camera.transform.forward.x) > .0001f
                    || Mathf.Abs(camera.transform.right.y) > .0001f)
                    throw new InvalidOperationException("The board camera has drifted from the table axis.");
                foreach (float z in new[] { table.min.z, table.center.z, table.max.z })
                {
                    Vector3 screen = camera.WorldToViewportPoint(new Vector3(table.center.x, 0f, z));
                    if (Mathf.Abs(screen.x - .5f) > .0001f)
                        throw new InvalidOperationException("The table axis is not at the screen center.");
                }
                Vector2 lo, hi;
                if (!rig.ViewNdcRange(TableSetup.BoardView, points, out lo, out hi)
                    || lo.x < -.91f || hi.x > .91f || lo.y < -.91f || hi.y > .91f)
                    throw new InvalidOperationException("Centered framing clips playable content.");
                Debug.Log("[CameraCapture] Centered axis and content margins PASS; aspect=" + aspect);
            }
            camera.aspect = originalAspect;
            camera.ResetAspect();
            setup.ReframeBoardView(true);
        }
    }
}
