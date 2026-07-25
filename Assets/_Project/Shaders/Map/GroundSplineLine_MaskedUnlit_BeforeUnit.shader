Shader "SkyPrison/GroundSplineLine_MaskedUnlit_BeforeUnit"
{
    // V2 - 2026-05-25
    // Build-safe masked unlit shader for ground spline / road line.
    // Internal shader name is intentionally unchanged.

    Properties
    {
        _MainTex ("Main Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _SplineMaskTex ("Spline Mask Texture", 2D) = "white" {}
        _SplineMaskEnabled ("Spline Mask Enabled", Float) = 0
        _SplineMaskStrength ("Spline Mask Strength", Range(0,1)) = 1
        _SplineMaskThreshold ("Spline Mask Threshold", Range(0,1)) = 0.35
        _SplineMaskSoftness ("Spline Mask Softness", Range(0.001,0.5)) = 0.1
        _SplineMaskWorldSize ("Spline Mask World Size", Float) = 3
        _SplineMaskInvert ("Spline Mask Invert", Float) = 0
        _SplineMaskOffset ("Spline Mask Offset", Vector) = (0,0,0,0)

        _AlphaClip ("Alpha Clip", Range(0,1)) = 0.001
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent-50"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
            "PreviewType"="Plane"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha
        Fog { Mode Off }

        Pass
        {
            Name "GroundSplineLineMaskedUnlit"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            sampler2D _SplineMaskTex;
            float4 _SplineMaskTex_ST;
            float _SplineMaskEnabled;
            float _SplineMaskStrength;
            float _SplineMaskThreshold;
            float _SplineMaskSoftness;
            float _SplineMaskWorldSize;
            float _SplineMaskInvert;
            float4 _SplineMaskOffset;

            float _AlphaClip;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 maskUV : TEXCOORD1;
                fixed4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);

                float worldSize = max(abs(_SplineMaskWorldSize), 0.0001);
                float2 baseMaskUV = (v.uv1 + _SplineMaskOffset.xy) / worldSize;
                o.maskUV = baseMaskUV * _SplineMaskTex_ST.xy + _SplineMaskTex_ST.zw;

                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv) * _Color * i.color;

                if (_SplineMaskEnabled > 0.5 && _SplineMaskStrength > 0.001)
                {
                    float maskRaw = tex2D(_SplineMaskTex, i.maskUV).r;
                    if (_SplineMaskInvert > 0.5)
                        maskRaw = 1.0 - maskRaw;

                    float keep = smoothstep(
                        _SplineMaskThreshold,
                        _SplineMaskThreshold + max(_SplineMaskSoftness, 0.001),
                        maskRaw);

                    float finalKeep = lerp(1.0, keep, saturate(_SplineMaskStrength));
                    col.a *= finalKeep;
                }

                clip(col.a - max(_AlphaClip, 0.0001));
                return col;
            }
            ENDCG
        }
    }

    FallBack Off
}
