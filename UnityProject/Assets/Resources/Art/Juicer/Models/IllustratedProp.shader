Shader "GameJam/IllustratedProp"
{
    Properties
    {
        _MainTex ("Paint and paper grain", 2D) = "white" {}
        _Color ("Paint tint", Color) = (1,1,1,1)
        _OutlineColor ("Ink contour", Color) = (0.018,0.021,0.024,1)
        _OutlineWidth ("Contour width", Float) = 0.006
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "InkContour"
            Cull Front
            ZWrite On
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _OutlineWidth;
            fixed4 _OutlineColor;
            float4 vert(appdata_base v) : SV_POSITION
            {
                v.vertex.xyz += normalize(v.normal) * _OutlineWidth;
                return UnityObjectToClipPos(v.vertex);
            }
            fixed4 frag() : SV_Target { return _OutlineColor; }
            ENDCG
        }
        Pass
        {
            Name "Paint"
            Tags { "LightMode"="ForwardBase" }
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            struct varyings { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float3 normal:TEXCOORD1; };
            varyings vert(appdata_base v)
            {
                varyings o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                return o;
            }
            fixed4 frag(varyings i) : SV_Target
            {
                float light = saturate(dot(normalize(i.normal), normalize(_WorldSpaceLightPos0.xyz)));
                float wash = lerp(0.72, 1.12, floor(light * 3.0) / 3.0);
                return fixed4(tex2D(_MainTex, i.uv).rgb * _Color.rgb * wash, 1);
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}
