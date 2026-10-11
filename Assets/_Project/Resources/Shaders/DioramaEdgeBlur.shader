// 选关沙盘的"移轴 / 边缘景深"：画面中心清晰，越靠边缘越糊（用 RT 的 mip 链做模糊，不额外跑后处理）。
// 只用在 SkyPrisonRaidMapScreen 里显示沙盘 RT 的那张 RawImage 上。
Shader "SkyPrison/UI/DioramaEdgeBlur"
{
    Properties
    {
        _MainTex ("Diorama RT", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _ClearRadius ("Clear Radius", Range(0, 1)) = 0.42
        _FeatherRadius ("Feather Radius", Range(0.05, 1.5)) = 0.62
        _MaxLod ("Max Blur LOD", Range(0, 6)) = 3.2
        _MarkerRect ("Marker Keep-Sharp Rect (x0,y0,x1,y1)", Vector) = (0,0,0,0)
        _MarkerOn ("Marker Keep-Sharp On", Float) = 0
        _Gray ("Gray (0=彩色 1=黑白)", Range(0, 1)) = 0
        _Blur9 ("Soft 9-tap (0/1)", Float) = 0
        _FadeX0 ("Left Fade Start (uv.x)", Float) = 0
        _FadeW ("Left Fade Width (uv.x, 0=off)", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _ClearRadius, _FeatherRadius, _MaxLod; float4 _MarkerRect; float _MarkerOn; float _Gray; float _Blur9; float4 _ClipRect; float _FadeX0, _FadeW;

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float4 worldPos : TEXCOORD1; };

            v2f vert(appdata v)
            {
                v2f o;
                o.worldPos = v.vertex;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 d = (i.uv - 0.5) * float2(1.0, 1.25);               // 上下边缘比左右更糊一点（移轴感）
                float r = length(d) * 2.0;
                float t = saturate((r - _ClearRadius) / max(0.0001, _FeatherRadius));
                t = t * t * (3.0 - 2.0 * t);
                float dm = max(max(_MarkerRect.x - i.uv.x, i.uv.x - _MarkerRect.z), max(_MarkerRect.y - i.uv.y, i.uv.y - _MarkerRect.w));
                t *= 1.0 - (1.0 - smoothstep(0.0, 0.05, dm)) * _MarkerOn;                 // 标记区域内保持清晰
                float lod = t * _MaxLod;
                fixed4 c = tex2Dlod(_MainTex, float4(i.uv, 0, lod));
                if (_Blur9 > 0.5)
                {
                    float2 o = float2(0.0045, 0.008);                       // 毛玻璃：在已经很糊的 mip 上再做一次 9 点平均，边缘更平滑
                    c *= 0.25;
                    c += (tex2Dlod(_MainTex, float4(i.uv + float2(o.x, 0), 0, lod)) + tex2Dlod(_MainTex, float4(i.uv - float2(o.x, 0), 0, lod))
                        + tex2Dlod(_MainTex, float4(i.uv + float2(0, o.y), 0, lod)) + tex2Dlod(_MainTex, float4(i.uv - float2(0, o.y), 0, lod))) * 0.125;
                    c += (tex2Dlod(_MainTex, float4(i.uv + o, 0, lod)) + tex2Dlod(_MainTex, float4(i.uv - o, 0, lod))
                        + tex2Dlod(_MainTex, float4(i.uv + float2(o.x, -o.y), 0, lod)) + tex2Dlod(_MainTex, float4(i.uv + float2(-o.x, o.y), 0, lod))) * 0.0625;
                }
                c.rgb = lerp(c.rgb, dot(c.rgb, float3(0.299, 0.587, 0.114)).xxx, _Gray);
                fixed4 outc = fixed4(c.rgb, 1) * fixed4(i.color.rgb, i.color.a);
                if (_FadeW > 0.0) { float f = saturate((i.uv.x - _FadeX0) / _FadeW); outc.a *= f * f * (3.0 - 2.0 * f); }   // 窗口左边缘向左淡出
                #ifdef UNITY_UI_CLIP_RECT
                outc.a *= UnityGet2DClipping(i.worldPos.xy, _ClipRect);      // RectMask2D 的裁剪：展开动画靠它
                #endif
                return outc;
            }
            ENDCG
        }
    }
}
