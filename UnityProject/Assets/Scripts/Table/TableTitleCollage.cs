using UnityEngine;

namespace GameJam.Prototype
{
    public partial class TableHud
    {
        private Texture2D collagePaper, collageSeal, collageWater;
        private Font collageFont;
        private GUIStyle collageHeading;
        private static readonly Color CollageInk = new Color(0.10f, 0.105f, 0.095f);
        private static readonly Color CollageRed = new Color(0.52f, 0.09f, 0.065f);

        private void EnsureCollageArt()
        {
            if (collagePaper != null) return;
            collagePaper = new Texture2D(384, 448, TextureFormat.RGBA32, false);
            collagePaper.name = "TornExperimentLetter";
            Color[] pixels = new Color[384 * 448];
            for (int y = 0; y < 448; y++)
            for (int x = 0; x < 384; x++)
            {
                float left = 4f + 2f*Mathf.Sin(y*0.17f) + 1.5f*Mathf.Sin(y*0.63f);
                float right = 379f + 2f*Mathf.Sin(y*0.23f);
                float bottom = 5f + 2.8f*Mathf.Sin(x*0.24f) + 1.2f*Mathf.Sin(x*0.87f);
                float top = 441f + 2.6f*Mathf.Sin(x*0.19f);
                float edge = Mathf.Min(Mathf.Min(x-left,right-x), Mathf.Min(y-bottom,top-y));
                float noise = Mathf.PerlinNoise(x*0.81f,y*0.81f)*0.09f;
                float wash = Mathf.PerlinNoise(x*0.017f+4f,y*0.017f)*0.05f;
                Color paper = new Color(0.91f-noise-wash, 0.82f-noise-wash, 0.67f-noise-wash);
                paper.a = Mathf.Clamp01(edge);
                if (edge < 3f) paper *= new Color(0.85f,0.81f,0.74f,1f);
                pixels[y*384+x] = paper;
            }
            collagePaper.SetPixels(pixels);
            collagePaper.Apply(false,false);
            collageSeal = new Texture2D(192,192,TextureFormat.RGBA32,false);
            collageSeal.name = "RedAcceptanceSeal";
            pixels = new Color[192*192];
            for (int y=0;y<192;y++)
            for (int x=0;x<192;x++)
            {
                float dx=(x-95.5f)/96f,dy=(y-95.5f)/96f;
                float angle=Mathf.Atan2(dy,dx), radius=Mathf.Sqrt(dx*dx+dy*dy);
                float outer=0.91f+0.022f*Mathf.Sin(angle*23f)+0.014f*Mathf.Sin(angle*37f);
                float grain=Mathf.PerlinNoise(x*.63f,y*.63f);
                Color wax=new Color(.48f+grain*.13f,.065f+grain*.035f,.045f+grain*.025f);
                if (Mathf.Abs(radius-.76f)<.014f || Mathf.Abs(radius-.81f)<.009f) wax=new Color(.81f,.46f,.27f);
                wax.a=Mathf.Clamp01((outer-radius)*100f);
                pixels[y*192+x]=wax;
            }
            collageSeal.SetPixels(pixels);
            collageSeal.Apply(false,false);
            collageWater = Resources.Load<Texture2D>("Art/Card/card_water_img");
            collageFont = GameUiTheme.Font;
            collageHeading = new GUIStyle(GUI.skin.label) { font = collageFont, fontSize=58,
                wordWrap=false, padding=new RectOffset(0,0,0,0) };
            collageHeading.normal.textColor=CollageInk;
            collageHeading.hover.textColor=CollageInk;
        }

        private static void CollageLine(Vector2 start, Vector2 end, Color ink, float thickness=2f)
        {
            Matrix4x4 previous=GUI.matrix;
            Vector2 delta=end-start;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg,start);
            MenuFill(new Rect(start.x,start.y,delta.magnitude,thickness),ink);
            GUI.matrix=previous;
        }

        private void CollageSheet(Rect at, Color tint, float angle, bool shadow=true)
        {
            Matrix4x4 previous=GUI.matrix;
            GUIUtility.RotateAroundPivot(angle,at.center);
            Color previousColor=GUI.color;
            if (shadow)
            {
                GUI.color=new Color(0f,0f,0f,.35f);
                GUI.DrawTexture(new Rect(at.x+7f,at.y+9f,at.width,at.height),collagePaper);
            }
            GUI.color=tint;
            GUI.DrawTexture(at,collagePaper);
            GUI.color=previousColor;
            GUI.matrix=previous;
        }

        private void CollageTape(Rect at, float angle)
        {
            Matrix4x4 previous=GUI.matrix;
            GUIUtility.RotateAroundPivot(angle,at.center);
            MenuFill(at,new Color(.73f,.62f,.39f,.68f));
            for(int i=0;i<6;i++)
                MenuFill(new Rect(at.x+i*at.width/6f,at.y+2f,1f,at.height-4f),new Color(.92f,.83f,.59f,.20f));
            GUI.matrix=previous;
        }

        private bool CollageTab(Rect at,string label,string note,float angle,bool enabled)
        {
            Matrix4x4 previous=GUI.matrix;
            GUIUtility.RotateAroundPivot(angle,at.center);
            bool hot=at.Contains(Event.current.mousePosition)&&enabled;
            CollageSheet(at,hot?new Color(1.06f,1.03f,.94f):new Color(.87f,.82f,.69f),0f);
            MenuText(new Rect(at.x+13f,at.y+9f,at.width-26f,25f),label,18,enabled?CollageInk:new Color(.42f,.39f,.32f));
            MenuText(new Rect(at.x+13f,at.y+38f,at.width-26f,17f),note,10,new Color(.40f,.33f,.24f));
            if(hot) CollageLine(new Vector2(at.x+13f,at.y+34f),new Vector2(at.xMax-18f,at.y+33f),CollageRed);
            bool was=GUI.enabled;
            GUI.enabled=was&&enabled;
            bool clicked=GUI.Button(at,GUIContent.none,GUIStyle.none);
            GUI.enabled=was;
            GUI.matrix=previous;
            return clicked;
        }

        private void DrawCollageTitle()
        {
            if(turnLoop==null||turnLoop.titleRig==null)return;
            EnsureCollageArt();
            float width,height,scale;
            Matrix4x4 original=BeginMenuCanvas(out width,out height,out scale);
            float pageY=(height-532f)*.5f-20f;
            Rect page=new Rect(87f,pageY,525f,532f);
            menuPointerRect=new Rect(40f*scale,(pageY-42f)*scale,650f*scale,650f*scale);
            MenuFill(new Rect(0f,0f,width,30f),new Color(0f,0f,0f,.18f));
            CollageSheet(new Rect(page.x-14f,page.y+12f,page.width,page.height),new Color(.56f,.48f,.36f),-5.0f);
            Matrix4x4 canvas=GUI.matrix;
            GUIUtility.RotateAroundPivot(-2.4f,page.center);
            CollageSheet(page,Color.white,0f);
            float x=page.x+37f,y=page.y+32f;
            MenuText(new Rect(x,y,390f,22f),"卡 牌 策 略",13,CollageRed);
            CollageLine(new Vector2(x,y+32f),new Vector2(page.xMax-36f,y+34f),CollageInk,2.4f);
            GUI.Label(new Rect(x-3f,y+84f,350f,158f),"破壁机\n计　划",collageHeading);
            MenuText(new Rect(x,y+246f,350f,26f),"组合素材，达到目标分数。",17,CollageInk);
            CollageLine(new Vector2(x,y+280f),new Vector2(x+274f,y+278f),CollageInk,1.6f);
            MenuText(new Rect(x,y+303f,288f,79f),"选择刀片与素材\n启动破壁机得分",16,CollageInk);

            Rect sample=new Rect(page.x+363f,page.y+101f,122f,158f);
            Matrix4x4 letter=GUI.matrix;
            GUIUtility.RotateAroundPivot(7f,sample.center);
            CollageSheet(sample,new Color(.88f,.85f,.76f),0f);
            if(collageWater!=null)GUI.DrawTexture(new Rect(sample.x+20f,sample.y+18f,82f,92f),collageWater,ScaleMode.ScaleToFit);
            MenuText(new Rect(sample.x+10f,sample.y+116f,sample.width-20f,30f),"样本 A  /  水",12,CollageInk,TextAnchor.MiddleCenter);
            GUI.matrix=letter;
            CollageTape(new Rect(sample.x+26f,sample.y-5f,75f,22f),-4f);

            bool available=!turnLoop.titleRig.IsStarting&&!turnLoop.settingsOpen&&!turnLoop.paused;
            Rect seal=new Rect(page.x+354f,page.y+352f,136f,136f);
            bool hot=Vector2.Distance(Event.current.mousePosition,seal.center)<64f&&available;
            Color old=GUI.color;
            GUI.color=hot?new Color(1.19f,1.12f,1.03f):Color.white;
            GUI.DrawTexture(seal,collageSeal);
            GUI.color=old;
            MenuText(new Rect(seal.x+18f,seal.y+29f,100f,77f),"开始\n游戏",25,new Color(.95f,.85f,.65f),TextAnchor.MiddleCenter);
            bool enabled=GUI.enabled;
            GUI.enabled=enabled&&available;
            bool start=GUI.Button(seal,GUIContent.none,GUIStyle.none)&&hot;
            GUI.enabled=enabled;
            GUI.matrix=canvas;
            CollageTape(new Rect(page.x+74f,page.y-16f,132f,36f),-9f);
            CollageTape(new Rect(page.xMax-126f,page.yMax-3f,108f,31f),14f);
            if(start){GUI.FocusControl(null);turnLoop.titleRig.ActivateMenu(TableTitleRig.IdNew);}

            float tabsY=page.yMax+21f;
            if(CollageTab(new Rect(84f,tabsY,214f,68f),turnLoop.titleRig.ContinueEnabled?"继续游戏":"暂无存档", "CONTINUE",-3f,available&&turnLoop.titleRig.ContinueEnabled))
                turnLoop.titleRig.ActivateMenu(TableTitleRig.IdContinue);
            if(CollageTab(new Rect(314f,tabsY+3f,152f,68f),"设置", "SETTINGS",2f,available))
                turnLoop.titleRig.ActivateMenu(TableTitleRig.IdSettings);
            if(CollageTab(new Rect(482f,tabsY-2f,152f,68f),"退出游戏", "EXIT",-4f,available))
                turnLoop.titleRig.ActivateMenu(TableTitleRig.IdQuit);

            Rect tag=new Rect(width-430f,height*.64f,332f,119f);
            Matrix4x4 tagMatrix=GUI.matrix;
            GUIUtility.RotateAroundPivot(4f,tag.center);
            CollageSheet(tag,new Color(.90f,.85f,.72f),0f);
            MenuText(new Rect(tag.x+22f,tag.y+17f,290f,34f),"破壁机",27,CollageRed);
            MenuText(new Rect(tag.x+22f,tag.y+62f,286f,42f),"选择素材后启动",13,CollageInk);
            GUI.matrix=tagMatrix;
            CollageTape(new Rect(tag.x+96f,tag.y-8f,115f,26f),-7f);
            CollageLine(new Vector2(tag.x+135f,tag.y-10f),new Vector2(tag.x+105f,tag.y-38f),new Color(.73f,.63f,.43f),2f);
            CollageLine(new Vector2(tag.x+105f,tag.y-38f),new Vector2(tag.x+108f,tag.y-25f),new Color(.73f,.63f,.43f),2f);
            GUI.matrix=original;
        }
    }
}
