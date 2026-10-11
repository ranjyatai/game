// 战术地图的地形着色器：把俯视抓图(_MainTex)按亮度染成冷绿色，只在「走过的格子」(_ExploredTex)里显示，
// 没走过的地方只剩一层很暗的底色。UI 用，顶点色的 alpha 控制整体透明度。
Shader "SkyPrison/UI/MinimapTerrain"
{
    Properties
    {
        [PerRendererData] _MainTex ("Terrain Capture", 2D) = "black" {}
        _ExploredTex ("Explored Mask", 2D) = "black" {}
        _Tint ("Tint", Color) = (0.42, 0.92, 0.68, 1)
        _Base ("Unexplored Base", Color) = (0.03, 0.05, 0.05, 1)
        _Gain ("Brightness Gain", Float) = 2.2
        _Gamma ("Brightness Gamma", Float) = 0.65
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Cull Off Lighting Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };

            sampler2D _MainTex;
            sampler2D _ExploredTex;
            fixed4 _Tint;
            fixed4 _Base;
            float _Gain;
            float _Gamma;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed3 t = tex2D(_MainTex, i.uv).rgb;
                float lum = saturate(pow(dot(t, float3(0.3333, 0.3333, 0.3333)), _Gamma) * _Gain);
                float e = smoothstep(0.15, 0.85, tex2D(_ExploredTex, i.uv).r);      // 格子贴图是双线性的，边缘自然羽化
                fixed3 lit = _Tint.rgb * (0.12 + 0.88 * lum);
                return fixed4(lerp(_Base.rgb, lit, e), i.color.a);
            }
            ENDCG
        }
    }
}
