// 商店货架卡片图标底部透明度渐隐——用户明确要求"图标自己淡出"，不是叠一层
// 深色遮罩在上面（那样看着像贴了块脏色块）。普通 UI Image 没法做半透明渐变遮罩，
// 这里写个最小的 UI 专用 shader，按 UV.y 把纹理自身的 alpha 线性衰减掉。
Shader "SkyPrison/UI/VerticalFadeIcon"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _FadeStart ("Fade Start (UV.y, 全不透明的下边界)", Range(0,1)) = 0.55
        _FadeEnd ("Fade End (UV.y, 全透明的下边界)", Range(0,1)) = 0.0
        _Saturation ("Saturation (0=黑白 1=原色，售罄货架卡片用)", Range(0,1)) = 1

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float _FadeStart;
            float _FadeEnd;
            float _Saturation;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, IN.texcoord) * IN.color;
                float denom = max(0.0001, (_FadeStart - _FadeEnd));
                float t = saturate((IN.texcoord.y - _FadeEnd) / denom);
                c.a *= t;
                // 售罄货架卡片图标要变黑白（用户明确要求）——按亮度算灰度值，
                // 用 _Saturation 在原色和灰度之间插值，0=纯黑白，1=原色不变。
                float gray = dot(c.rgb, float3(0.299, 0.587, 0.114));
                c.rgb = lerp(fixed3(gray, gray, gray), c.rgb, _Saturation);
                return c;
            }
            ENDCG
        }
    }
}
