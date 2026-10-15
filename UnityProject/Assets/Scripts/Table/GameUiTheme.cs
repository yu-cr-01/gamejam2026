using UnityEngine;

namespace GameJam.Prototype
{
    /// <summary>Shared card-like lettering and opaque paper UI for every game screen.</summary>
    public static class GameUiTheme
    {
        public static readonly Color Ink = new Color(.13f,.12f,.10f);
        public static readonly Color Muted = new Color(.36f,.30f,.23f);
        public static readonly Color Red = new Color(.53f,.10f,.065f);
        public static readonly Color Cream = new Color(.94f,.86f,.71f);
        private static Font font;
        private static Texture2D paper, charcoal;
        private static readonly Texture2D[,] buttons = new Texture2D[2,3];
        private static GUISkin skin;
        private static GUIStyle paperSheet, darkSheet;
        public static void DrawSheet(Rect at,bool dark=false)
        {
            if(paperSheet==null)
            {
                paperSheet=new GUIStyle(GUI.skin.box){border=new RectOffset(8,8,8,8)};
                paperSheet.normal.background=Paper;
                darkSheet=new GUIStyle(paperSheet);darkSheet.normal.background=Charcoal;
            }
            GUI.Box(at,GUIContent.none,dark?darkSheet:paperSheet);
        }
        public static Font Font
        {
            get
            {
                if(font==null) font=UnityEngine.Font.CreateDynamicFontFromOSFont(
                    new[] { "KaiTi", "楷体", "STKaiti", "SimSun", "Microsoft YaHei", "Noto Sans CJK SC" },48);
                return font;
            }
        }
        public static Texture2D Paper { get { if(paper==null)paper=MakePaper(new Color(.87f,.78f,.63f)); return paper; } }
        public static Texture2D Charcoal { get { if(charcoal==null)charcoal=MakePaper(new Color(.17f,.19f,.18f));return charcoal; } }
        public static Texture2D Button(bool selected,int state)
        {
            int index=selected?1:0;
            if(buttons[index,state]==null)
                buttons[index,state]=MakePaper(selected||state>0 ? new Color(state==2?.42f:.56f,.13f,.085f):new Color(.76f,.65f,.47f));
            return buttons[index,state];
        }
        private static Texture2D MakePaper(Color baseColor)
        {
            const int size=96;
            Texture2D texture=new Texture2D(size,size,TextureFormat.RGBA32,false);
            texture.name="CardPaperUI";
            Color[] pixels=new Color[size*size];
            for(int y=0;y<size;y++)
            for(int x=0;x<size;x++)
            {
                float edge=Mathf.Min(Mathf.Min(x-1.5f-Mathf.Sin(y*.8f)*.7f, size-2.5f-x+Mathf.Sin(y*.9f)*.7f),
                                     Mathf.Min(y-1.5f-Mathf.Sin(x*.6f)*.7f,size-2.5f-y+Mathf.Sin(x*.8f)*.7f));
                float grain=(Mathf.PerlinNoise(x*.8f,y*.8f)-.5f)*.055f;
                Color color=baseColor+new Color(grain,grain,grain,0);
                if(edge<2.2f)color=Ink;
                else if(edge<3.3f)color=Color.Lerp(baseColor,Ink,.36f);
                color.a=Mathf.Clamp01(edge);
                pixels[y*size+x]=color;
            }
            texture.SetPixels(pixels);texture.Apply(false,false);return texture;
        }
        public static void Text(GUIStyle style,Color color)
        {
            if(style==null)return;
            style.font=Font;style.fontStyle=FontStyle.Normal;
            foreach(GUIStyleState state in new[] {style.normal,style.hover,style.active,style.focused,style.onNormal,style.onHover,style.onActive,style.onFocused})state.textColor=color;
        }
        public static void Panel(GUIStyle style)
        {
            if(style==null)return;
            style.normal.background=Paper;
            style.border=new RectOffset(8,8,8,8);
            Text(style,Ink);
        }
        public static void ButtonStyle(GUIStyle style,bool selected=false)
        {
            if(style==null)return;
            Text(style,selected?Cream:Ink);
            style.border=new RectOffset(8,8,8,8);
            style.normal.background=Button(selected,0);
            style.hover.background=Button(selected,1);
            style.active.background=Button(selected,2);
            style.hover.textColor=Cream;style.active.textColor=Cream;
            style.focused.background=style.normal.background;
            style.onNormal.background=Button(true,0);
            style.onHover.background=Button(true,1);
            style.onActive.background=Button(true,2);
        }
        public static void Apply()
        {
            if(skin==null)
            {
                skin=Object.Instantiate(GUI.skin);skin.name="SharedCardPaperSkin";skin.font=Font;
                Text(skin.label,Ink);Panel(skin.box);Panel(skin.window);
                Text(skin.toggle,Ink);Text(skin.textField,Ink);Text(skin.textArea,Ink);
                skin.textField.normal.background=Paper;skin.textArea.normal.background=Paper;
                ButtonStyle(skin.button);
                skin.horizontalSlider.normal.background=Charcoal;
                skin.horizontalSliderThumb.normal.background=Button(true,0);
                skin.verticalScrollbarThumb.normal.background=Button(true,0);
                skin.horizontalScrollbarThumb.normal.background=Button(true,0);
                skin.verticalScrollbar.normal.background=Charcoal;
                skin.horizontalScrollbar.normal.background=Charcoal;
            }
            GUI.skin=skin;
        }
    }
}
