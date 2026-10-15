using System.Collections.Generic;
using UnityEngine;

namespace GameJam.Prototype
{
    public partial class TableHud
    {
        private bool paperHudStyled, artTurnDetails, artViewsOpen;
        private bool artAdvancedSettings;
        private Vector2 artResultScroll;
        private void OnEnable()
        {
            // Refresh surviving scene objects after a script reload as well as a fresh Play session.
            TableSetup table=Object.FindObjectOfType<TableSetup>();
            if(table!=null)table.RefreshSlotLabels();
            foreach(TextMesh text in Object.FindObjectsOfType<TextMesh>())
            {
                text.font=GameUiTheme.Font;
                Renderer renderer=text.GetComponent<Renderer>();
                if(renderer!=null)renderer.sharedMaterial=GameUiTheme.Font.material;
            }
        }
        private void ApplyPaperHudStyles()
        {
            if(paperHudStyled||panelBox==null)return;
            foreach(GUIStyle style in new[]{panelBox,panelBoxInner,panelGroup,panelGroupGlass,panelBoxGlass,reportBoxGlass})GameUiTheme.Panel(style);
            foreach(GUIStyle style in new[]{h1,body,h1Panel,bodyPanel,titleBig,titleSub,titleHint})GameUiTheme.Text(style,GameUiTheme.Ink);
            foreach(GUIStyle style in new[]{dim,dimPanel})GameUiTheme.Text(style,GameUiTheme.Muted);
            foreach(GUIStyle style in new[]{btn,btnRow,btnClose})GameUiTheme.ButtonStyle(style);
            foreach(GUIStyle style in new[]{btnOn,btnOnRow})GameUiTheme.ButtonStyle(style,true);
            paperHudStyled=true;
        }
        private bool ArtButton(Rect at,string text,bool primary,bool enabled=true)
        {
            bool was=GUI.enabled;
            GUI.enabled=was&&enabled;
            bool clicked=GUI.Button(at,text,primary?btnOn:btn);
            GUI.enabled=was;
            if(clicked)GUI.FocusControl(null);
            return clicked;
        }
        private Matrix4x4 ArtDialog(string title,string tag,float w,float h,out Rect page)
        {
            float width,height,scale;
            Matrix4x4 previous=BeginMenuCanvas(out width,out height,out scale);
            page=new Rect((width-w)*.5f,(height-h)*.5f,w,h);
            MenuFill(new Rect(0,0,width,height),new Color(.03f,.025f,.02f,.65f));
            GameUiTheme.DrawSheet(page);
            MenuText(new Rect(page.x+30,page.y+23,w-60,22),tag,13,GameUiTheme.Red);
            MenuText(new Rect(page.x+30,page.y+58,w-60,42),title,30,GameUiTheme.Ink);
            MenuFill(new Rect(page.x+30,page.y+109,w-60,2),GameUiTheme.Ink);
            v21PanelRects.Add(new Rect(page.x*scale,page.y*scale,page.width*scale,page.height*scale));
            return previous;
        }
        private void ArtMetric(Rect at,string label,string value)
        {
            GameUiTheme.DrawSheet(at);
            MenuText(new Rect(at.x+16,at.y+12,at.width-32,24),label,13,GameUiTheme.Muted);
            MenuText(new Rect(at.x+16,at.y+37,at.width-32,46),value,31,GameUiTheme.Ink);
        }
        private void ArtLog(Rect viewport,string text,ref Vector2 scroll)
        {
            GameUiTheme.DrawSheet(viewport);
            GUIStyle style=new GUIStyle(bodyPanel){fontSize=15,wordWrap=true};
            float w=viewport.width-48;
            float height=Mathf.Max(viewport.height-28,style.CalcHeight(new GUIContent(text),w));
            Rect inner=new Rect(viewport.x+14,viewport.y+14,viewport.width-28,viewport.height-28);
            scroll=GUI.BeginScrollView(inner,scroll,new Rect(0,0,w,height),false,false);
            GUI.Label(new Rect(0,0,w,height),text,style);
            GUI.EndScrollView();
        }
        private void DrawArtTurnToolbar()
        {
            TableRulesV21 r=turnLoop.rulesV21;if(r==null)return;
            float width,height,scale;
            Matrix4x4 previous=BeginMenuCanvas(out width,out height,out scale);
            float w=Mathf.Min(840,width-220);
            Rect basePage=new Rect((width-w)*.5f,16,w,170);
            Rect page=basePage;
            page.position+=turnPanelDrag.offset/scale;
            GameUiTheme.DrawSheet(page);
            float x=page.x+22,y=page.y+16;
            MenuText(new Rect(x,y,200,20),"游 戏 中",12,GameUiTheme.Red);
            MenuText(new Rect(x,y+26,210,40),"回合 "+r.turnIndex.ToString("00")+" / 04",27,GameUiTheme.Ink);
            MenuText(new Rect(x+225,y,200,20),"剩余行动",12,GameUiTheme.Muted);
            for(int i=0;i<GameJam.Rules.LevelRun.ActionPointsPerTurn;i++)
            {
                Rect dot=new Rect(x+225+i*29,y+29,22,25);
                MenuFill(dot,i<r.actionPoints?GameUiTheme.Red:new Color(.52f,.46f,.36f,.55f));
                MenuText(dot,(i+1).ToString(),14,GameUiTheme.Cream,TextAnchor.MiddleCenter);
            }
            MenuText(new Rect(page.xMax-240,y,208,20),"得分 / 目标",12,GameUiTheme.Muted);
            MenuText(new Rect(page.xMax-240,y+25,208,43),r.score+" / "+r.targetScore,29,GameUiTheme.Ink);
            float bw=(w-62)/4;
            bool can=r.CanActivate&&r.selected!=null&&!r.selected.removed;
            if(ArtButton(new Rect(x,y+78,bw,37),"启动破壁机",true,can))turnLoop.ActivateJuicer();
            if(ArtButton(new Rect(x+bw+6,y+78,bw,37),"自动选择目标",false))turnLoop.SelectNewestTarget();
            if(ArtButton(new Rect(x+(bw+6)*2,y+78,bw,37),"结束本回合",false))turnLoop.EndRoundOrLevel();
            if(ArtButton(new Rect(x+(bw+6)*3,y+78,bw,37),artTurnDetails?"收起详情":"查看详情",false))artTurnDetails=!artTurnDetails;
            string hint=can?"加工目标："+r.TargetText:r.levelOver?"本关已结束，请查看结算。":r.actionPoints<=0?"行动已用完，请结束回合。":
                r.blade==null?"先选择素材，作为刀片核心。":r.blade.H<=0?"刀片硬度已耗尽，请查看本关结算。":
                r.LiveTableCount()==0?"把素材放进右侧框中，再选择需要加工的卡牌。":"选择桌面素材，准备启动。";
            MenuText(new Rect(x,y+124,w-44,24),hint,13,GameUiTheme.Muted);
            GUI.matrix=previous;
            EndPanelFrame(turnPanelDrag,new Rect(basePage.x*scale,basePage.y*scale,w*scale,170*scale),previous);
            if(artTurnDetails)
            {
                previous=BeginMenuCanvas(out width,out height,out scale);
                Rect notes=new Rect(page.x,page.yMax+10,w,204);
                GameUiTheme.DrawSheet(notes);
                v21PanelRects.Add(new Rect(notes.x*scale,notes.y*scale,w*scale,notes.height*scale));
                GUILayout.BeginArea(new Rect(notes.x+18,notes.y+14,w-36,180));
                GUILayout.Label("刀片："+(r.blade!=null?r.blade.Describe():"无"),bodyPanel);
                GUILayout.Label("附魔："+(r.blade!=null?r.blade.layers.Describe():"无"),dimPanel);
                GUILayout.Label("手牌：素材 "+r.hand.Count+" / 法术 "+r.handSpells.Count+"　桌面 "+r.LiveTableCount()+" 张",dimPanel);
                GUILayout.Label("本回合启动 "+r.startsThisTurn+" 次；出牌不消耗行动，启动消耗 1 次行动和 1 点刀片 H。",dimPanel);
                DrawRulesWarning(r);
                string placeholder=r.PlaceholderWarning();if(!string.IsNullOrEmpty(placeholder))GUILayout.Label(placeholder,dimPanel);
                GUILayout.EndArea();GUI.matrix=previous;
            }
        }
        private void DrawArtViews()
        {
            float w=148,x=Screen.width-w-16;
            Rect box=new Rect(x,16,w,artViewsOpen?228:43);
            GUI.Box(box,GUIContent.none,panelBox);
            if(GUI.Button(new Rect(x+7,23,w-14,28),artViewsOpen?"收起视角":"切换视角",btn))artViewsOpen=!artViewsOpen;
            if(!artViewsOpen)return;
            for(int i=0;i<TableSetup.ViewNames.Length;i++)
                if(GUI.Button(new Rect(x+7,58+i*32,w-14,28),TableSetup.ViewLabels[i],setup.rig.CurrentView==TableSetup.ViewNames[i]?btnOn:btn))
                {setup.rig.GoTo(TableSetup.ViewNames[i]);artViewsOpen=false;}
        }
        private void DrawArtTurnResult()
        {
            TableRulesV21 r=turnLoop.rulesV21;if(r==null)return;
            Rect page;Matrix4x4 previous=ArtDialog("回合结算","第 "+r.turnIndex+" 回合",880,570,out page);
            ArtMetric(new Rect(page.x+28,page.y+132,260,96),"累计得分",r.score+" 分");
            ArtMetric(new Rect(page.x+302,page.y+132,260,96),"距离目标",Mathf.Max(0,r.targetScore-r.score)+" 分");
            ArtMetric(new Rect(page.x+576,page.y+132,276,96),"回合进度",r.turnIndex+" / 4");
            MenuText(new Rect(page.x+30,page.y+250,820,45),"本次启动："+(string.IsNullOrEmpty(r.lastSummary)?"本回合还没有启动":r.lastSummary),16,GameUiTheme.Ink);
            List<string> log=r.LastLogTail(12);
            string text=log.Count==0?"本回合暂无记录。\n刀片："+(r.blade!=null?r.blade.Describe():"无"):string.Join("\n",log.ToArray());
            ArtLog(new Rect(page.x+28,page.y+306,824,158),text,ref artResultScroll);
            bool over=r.levelOver||r.turnIndex>=GameJam.Rules.LevelRun.TurnsPerLevel;
            if(ArtButton(new Rect(page.x+28,page.y+495,402,46),"返回桌面",false))turnLoop.phase=TablePhase.Select;
            if(ArtButton(new Rect(page.x+448,page.y+495,404,46),over?"结束本关 →":"下一回合 →",true))turnLoop.NextTurn();
            GUI.matrix=previous;
        }
        private void DrawArtLevelResult()
        {
            TableRulesV21 r=turnLoop.rulesV21;if(r==null)return;
            Rect page;Matrix4x4 previous=ArtDialog(turnLoop.Passed?"通关成功":"未达目标","关 卡 结 算   /   "+turnLoop.level.Name,860,538,out page);
            ArtMetric(new Rect(page.x+28,page.y+132,390,104),"最终得分",r.score+" 分");
            ArtMetric(new Rect(page.x+434,page.y+132,398,104),"通关目标",r.targetScore+" 分");
            string record="结束原因："+(string.IsNullOrEmpty(r.endReason)?"结算完成":r.endReason)+"\n刀片："+(r.blade!=null?r.blade.Describe():"无")
                +"\n回合 "+r.turnIndex+" / 4　桌面残留 "+r.LiveTableCount()+" 张";
            if(r.warnings.Count>0)record+="\n有 "+r.warnings.Count+" 条规则警告，可按 F2 查看报告。";
            ArtLog(new Rect(page.x+28,page.y+257,804,163),record,ref artResultScroll);
            if(ArtButton(new Rect(page.x+28,page.y+456,390,47),"选择关卡",false))turnLoop.OpenLevelSelect();
            string next=turnLoop.Passed?(turnLoop.HasNextLevel?"下一关 →":"选择关卡"):"重试本关 →";
            if(ArtButton(new Rect(page.x+434,page.y+456,398,47),next,true))
            {if(turnLoop.Passed){if(turnLoop.HasNextLevel)turnLoop.NextLevel();else turnLoop.OpenLevelSelect();}else turnLoop.RestartLevel();}
            GUI.matrix=previous;
        }
        private void DrawArtShop()
        {
            if(turnLoop==null||turnLoop.rulesV21==null||turnLoop.shopVisit==null)return;
            TableRulesV21 r=turnLoop.rulesV21;ShopVisitV30 shop=turnLoop.shopVisit;
            Rect page;Matrix4x4 previous=ArtDialog(shop.merchant.name+"的材料摊","商 店",1060,580,out page);
            MenuText(new Rect(page.x+28,page.y+126,1004,28),ShopGreeting(shop.merchant.id)+"　　可用分数 "+r.score+"　空白卡 "+r.blankCount,15,GameUiTheme.Muted);
            float cw=190;
            for(int i=0;i<shop.offers.Count&&i<5;i++)
            {
                ShopOfferV30 offer=shop.offers[i];Rect card=new Rect(page.x+28+i*(cw+13),page.y+176,cw,270);
                GameUiTheme.DrawSheet(card);
                MenuText(new Rect(card.x+13,card.y+14,cw-26,23),(i+1).ToString("00")+" / "+(offer.spell?"法术":"素材"),13,GameUiTheme.Red);
                Texture2D face=offer.spell?CardArt.SpellScrollIllustration:ArtOfferFace(offer.id);
                if(face!=null)GUI.DrawTexture(new Rect(card.x+39,card.y+45,112,103),face,ScaleMode.ScaleToFit);
                MenuText(new Rect(card.x+13,card.y+151,cw-26,45),offer.name,21,GameUiTheme.Ink,TextAnchor.MiddleCenter);
                MenuText(new Rect(card.x+13,card.y+197,cw-26,21),RarityLabel(offer.rarity)+" / "+offer.price+" 分",13,GameUiTheme.Muted,TextAnchor.MiddleCenter);
                string label=!offer.available?"属性待定":offer.bought?"已收下":r.score<offer.price?"分数不足":"收下材料";
                if(ArtButton(new Rect(card.x+13,card.y+229,cw-26,29),label,false,shop.CanPayFor(offer,r.score)))turnLoop.BuyShopOffer(i);
            }
            bool canPay=!shop.blankCardSpent&&shop.rerollsRemaining==0&&r.blankCount>0;
            bool free=shop.blankCardSpent&&shop.rerollsRemaining>0;
            if(ArtButton(new Rect(page.x+28,page.y+489,320,45),"消耗空白卡重抽",false,canPay))turnLoop.AskShopReroll();
            if(ArtButton(new Rect(page.x+366,page.y+489,320,45),"再次重抽 / 剩 "+shop.rerollsRemaining+" 次",false,free))turnLoop.RerollShopForFree();
            if(ArtButton(new Rect(page.x+704,page.y+489,328,45),"继续游戏 →",true))turnLoop.LeaveShop();
            GUI.matrix=previous;
            if(turnLoop.shopRollConfirmationOpen)DrawShopRollConfirmation(shop);
        }
        private void DrawArtPause()
        {
            Rect page;Matrix4x4 previous=ArtDialog("暂停","",820,574,out page);
            float x=page.x+28,y=page.y+133,w=374;
            if(ArtButton(new Rect(x,y,w,46),"继续游戏 →",true))turnLoop.paused=false;
            if(ArtButton(new Rect(x+w+16,y,w,46),"设置",false))turnLoop.settingsOpen=true;
            if(ArtButton(new Rect(x,y+62,w,43),"保存游戏",false,turnLoop.CanSave)){turnLoop.SaveGame();turnLoop.paused=false;}
            if(ArtButton(new Rect(x+w+16,y+62,w,43),"读取存档",false,TableSaveIO.Exists)){turnLoop.paused=false;turnLoop.ContinueFromSave();}
            if(ArtButton(new Rect(x,y+120,w,43),"选择关卡",false)){turnLoop.paused=false;deckWindowOpen=false;SetLevelWindowOpen(true);}
            if(ArtButton(new Rect(x+w+16,y+120,w,43),"查看牌组",false)){turnLoop.paused=false;levelWindowOpen=false;SetDeckWindowOpen(true);}
            if(ArtButton(new Rect(x,y+178,w,43),"卡牌图鉴 / F1",false)){turnLoop.paused=false;if(browser!=null)browser.SetOpen(true);}
            if(ArtButton(new Rect(x+w+16,y+178,w,43),"规则记录 / F2",false)){turnLoop.paused=false;rulesReportOpen=true;}
            if(ArtButton(new Rect(x,y+236,w,43),"返回主菜单",false))turnLoop.ExitLevel();
            if(ArtButton(new Rect(x+w+16,y+236,w,43),"退出游戏",false))turnLoop.QuitGame();
            GameUiTheme.DrawSheet(new Rect(x,y+305,764,104));
            MenuText(new Rect(x+15,y+318,734,45),TableSaveIO.StatusLine(),14,GameUiTheme.Ink);
            MenuText(new Rect(x+15,y+365,734,32),turnLoop.CanSave?"Esc 可直接回到桌面。保存会更新现有记录。":"当前无法保存："+turnLoop.SaveBlockReason,13,GameUiTheme.Muted);
            GUI.matrix=previous;
        }
        private void DrawArtSettings()
        {
            Rect page;Matrix4x4 previous=ArtDialog("设置","",760,550,out page);
            float x=page.x+30,y=page.y+139;
            MenuText(new Rect(x,y,330,32),"转头灵敏度　"+TableSettings.LookSensitivity.ToString("0.0"),20,GameUiTheme.Ink);
            TableSettings.LookSensitivity=GUI.HorizontalSlider(new Rect(x+355,y+10,315,24),TableSettings.LookSensitivity,.5f,8f);
            MenuText(new Rect(x,y+66,330,32),"显示操作指引",20,GameUiTheme.Ink);
            if(ArtButton(new Rect(x+355,y+61,315,37),TableSettings.ShowHints?"已开启":"已关闭",TableSettings.ShowHints))TableSettings.ShowHints=!TableSettings.ShowHints;
            if(ArtButton(new Rect(x,y+122,670,37),artAdvancedSettings?"收起高级选项":"高级选项",false))artAdvancedSettings=!artAdvancedSettings;
            if(artAdvancedSettings)
            {
                MenuText(new Rect(x,y+176,320,29),"规则模式（下次开局生效）",17,GameUiTheme.Ink);
                if(ArtButton(new Rect(x+355,y+169,315,35),TableSettings.UseRulesV21?"当前卡牌规则":"旧版模拟规则",false))
                {TableSettings.UseRulesV21=!TableSettings.UseRulesV21;turnLoop.notice="规则模式将在下一次进入关卡时生效。";}
                MenuText(new Rect(x,y+225,320,29),"显示诊断数据",17,GameUiTheme.Ink);
                if(ArtButton(new Rect(x+355,y+218,315,35),TableSettings.ShowDebugInfo?"已开启":"已关闭",false))TableSettings.ShowDebugInfo=!TableSettings.ShowDebugInfo;
            }
            else MenuText(new Rect(x,y+183,670,78),"可调整视角灵敏度和操作指引。",17,GameUiTheme.Muted);
            if(ArtButton(new Rect(x,page.yMax-76,320,43),"恢复默认设置",false))TableSettings.ResetToDefault();
            if(ArtButton(new Rect(x+350,page.yMax-76,320,43),"完成 →",true))turnLoop.settingsOpen=false;
            GUI.matrix=previous;
        }
        private void DrawArtHints()
        {
            HintBlockScreenRect=new Rect();
            if(!TableSettings.ShowHints&&!TableSettings.ShowDebugInfo)return;
            float w=Mathf.Min(580,Screen.width-36);
            Rect box=new Rect(18,Screen.height-128,w,104);
            GUI.Box(box,GUIContent.none,panelBox);
            GUI.Label(new Rect(box.x+15,box.y+12,w-30,82),"点选或拖动手牌 → 放入对应纸框 → 选择素材 → 启动破壁机。\n右键查看卡牌；F1 翻阅图鉴；F2 查看规则记录；Esc 暂停。",bodyPanel);
            HintBlockScreenRect=box;
        }
        private void DrawArtLevelEnd()
        {
            if(turnLoop==null||turnLoop.rulesV21==null)return;
            TableRulesV21 r=turnLoop.rulesV21;
            Rect page;Matrix4x4 previous=ArtDialog(r.bursted?"刀片已损坏":"关卡结束","",790,432,out page);
            ArtMetric(new Rect(page.x+28,page.y+132,350,97),"最终得分",r.score+" 分");
            ArtMetric(new Rect(page.x+396,page.y+132,366,97),"通关目标",r.targetScore+" 分");
            MenuText(new Rect(page.x+30,page.y+253,730,65),"结束原因："+(string.IsNullOrEmpty(r.endReason)?"关卡结束":r.endReason),18,GameUiTheme.Ink);
            if(ArtButton(new Rect(page.x+30,page.y+352,730,44),"查看结算 →",true))turnLoop.ShowLevelResult();
            GUI.matrix=previous;
        }
        private Texture2D ArtOfferFace(string id)
        {
            foreach(GameJam.Data.Ingredient material in CardSpecs.Materials())
                if(material!=null&&material.id==id)return CardArt.FaceV30(material);
            return CardArt.Face(id);
        }
        private void DrawArtDecks()
        {
            GameJam.Data.Choice choice=turnLoop!=null?turnLoop.CurrentChoice:null;
            if(choice==null&&turnLoop!=null)choice=turnLoop.FindChoice(GameConfig.DeckPickId);
            if(choice==null||choice.options==null||choice.options.Count==0)return;
            Rect page;Matrix4x4 previous=ArtDialog("选择牌组","开 局 准 备",1040,588,out page);
            int cols=Mathf.Min(3,choice.options.Count);float cw=(966-(cols-1)*16f)/cols;
            Rect viewport=new Rect(page.x+28,page.y+132,984,344);
            float contentHeight=Mathf.Max(344,Mathf.Ceil(choice.options.Count/(float)cols)*338);
            deckWindowScroll=GUI.BeginScrollView(viewport,deckWindowScroll,new Rect(0,0,966,contentHeight),false,false);
            for(int i=0;i<choice.options.Count;i++)
            {
                GameJam.Data.ChoiceOption option=choice.options[i];if(option==null)continue;
                Rect card=new Rect((i%cols)*(cw+16),(i/cols)*338,cw-6,324);
                GameUiTheme.DrawSheet(card);
                bool picked=deckWindowPick==i;
                MenuFill(new Rect(card.x+9,card.y+9,card.width-18,6),picked?GameUiTheme.Red:GameUiTheme.Muted);
                MenuText(new Rect(card.x+20,card.y+29,card.width-40,24),"牌组 "+(i+1).ToString("00")+(picked?" / 已选定":""),14,GameUiTheme.Red);
                MenuText(new Rect(card.x+20,card.y+72,card.width-40,40),option.title,24,GameUiTheme.Ink);
                MenuFill(new Rect(card.x+20,card.y+121,card.width-40,1),GameUiTheme.Ink);
                MenuText(new Rect(card.x+20,card.y+140,card.width-40,24),"起始材料",14,GameUiTheme.Muted);
                MenuText(new Rect(card.x+20,card.y+174,card.width-40,73),TableChoiceRig.DeckIngredientsLine(option),19,GameUiTheme.Ink);
                MenuText(new Rect(card.x+20,card.y+253,card.width-40,58),TableChoiceRig.DeckModuleLine(option)+"\n"+TableChoiceRig.DeckBladeLine(option),14,GameUiTheme.Muted);
                bool enabled=GUI.enabled;GUI.enabled=enabled&&option.enabled;
                if(GUI.Button(card,GUIContent.none,GUIStyle.none)){GUI.FocusControl(null);PickDeckInWindow(i);}
                GUI.enabled=enabled;
            }
            GUI.EndScrollView();
            bool selecting=turnLoop.phase==TablePhase.DeckPick;
            MenuText(new Rect(page.x+30,page.y+508,580,42),selecting?"选好牌组后，再选择刀片核心。":"当前牌组仅供查看。",15,GameUiTheme.Muted);
            if(ArtButton(new Rect(page.xMax-350,page.y+503,320,47),selecting?"确认这份牌组 →":"关闭",true,!selecting||deckWindowPick>=0))
            {if(selecting)ConfirmDeckInWindow();else deckWindowOpen=false;}
            GUI.matrix=previous;
        }
        private void DrawArtBladePick()
        {
            TableRulesV21 r=turnLoop.rulesV21;if(r==null)return;
            MaterialCard candidate=turnLoop.bladeCoreCard!=null?turnLoop.bladeCoreCard:r.CoreCandidate();
            float width,height,scale;Matrix4x4 previous=BeginMenuCanvas(out width,out height,out scale);
            Rect page=new Rect((width-780)*.5f,24,780,225);GameUiTheme.DrawSheet(page);
            v21PanelRects.Add(new Rect(page.x*scale,page.y*scale,page.width*scale,page.height*scale));
            MenuText(new Rect(page.x+24,page.y+18,730,24),"开 局 准 备   /   选 择 核 心",13,GameUiTheme.Red);
            MenuText(new Rect(page.x+24,page.y+54,730,38),"选择刀片核心",27,GameUiTheme.Ink);
            MenuText(new Rect(page.x+24,page.y+110,420,34),candidate!=null?candidate.name+"　H "+candidate.H+" / V "+candidate.V:"没有可选的素材核心",21,GameUiTheme.Ink);
            MenuText(new Rect(page.x+24,page.y+156,400,49),"点击下方素材牌可改选。\n核心卡会移出手牌，H / V 成为刀片属性。",14,GameUiTheme.Muted);
            if(ArtButton(new Rect(page.x+456,page.y+152,300,47),"开始游戏 →",true,candidate!=null))turnLoop.ConfirmBladePick();
            GUI.matrix=previous;
        }
    }
}
