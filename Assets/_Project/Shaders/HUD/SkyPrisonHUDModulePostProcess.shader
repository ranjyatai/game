Shader "Sky Prison/UI/HUD Module PostProcess Clean V32"
{
    Properties
    {
        [PerRendererData] _MainTex ("Module RT", 2D) = "white" {}

        _ChromaticAmount ("Chromatic Amount", Range(0, 24)) = 2
        _ChromaticAngle ("Chromatic Angle", Range(0, 6.28318)) = 0
        _ChromaticSoftness ("Chromatic Softness", Range(0.001, 1)) = 0.15
        _ChromaticAlphaBoost ("Chromatic Alpha Boost", Range(0, 4)) = 1

        _HUDSourcePreserve ("HUD Source Preserve", Range(0, 1)) = 0.45
        _HUDSourceFloor ("HUD Source Floor", Range(0, 1)) = 0.35
        _HUDBrightness ("HUD Brightness", Range(0, 3)) = 1
        _HUDContrast ("HUD Contrast", Range(0, 3)) = 1
        _HUDEmission ("HUD Emission", Range(0, 3)) = 0
        _HUDSaturation ("HUD Saturation", Range(0, 2)) = 1

        _GeometryFringeStrength ("Geometry Fringe Strength", Range(0, 4)) = 0.35
        _GeometryFringeWidth ("Geometry Fringe Width", Range(0, 16)) = 2
        _GeometryFringeOutputBoost ("Geometry Fringe Output Boost", Range(0, 4)) = 1

        _Color ("Tint", Color) = (1,1,1,1)
        // Set at runtime by SkyPrisonHUDModulePostProcessRenderer: RT_width / (display_canvas_width * canvas_scaleFactor).
        // Makes _ChromaticAmount mean "screen pixels" regardless of RT resolution or display scale.
        [HideInInspector] _ScreenPixelScale ("Screen Pixel Scale", Float) = 1

        [HideInInspector] _SrcBlend ("Src Blend", Float) = 5
        [HideInInspector] _DstBlend ("Dst Blend", Float) = 10
        [HideInInspector] _BlendOp ("Blend Op", Float) = 0
        [HideInInspector] _SkyPrisonBlendMode ("Sky Prison Blend Mode", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always

        BlendOp [_BlendOp]
        Blend [_SrcBlend] [_DstBlend]

        Pass
        {
            Name "HUDModulePostProcessClean"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;

            float4 _Color;

            float _ChromaticAmount;
            float _ChromaticAngle;
            float _ChromaticSoftness;
            float _ChromaticAlphaBoost;

            float _HUDSourcePreserve;
            float _HUDSourceFloor;
            float _HUDBrightness;
            float _HUDContrast;
            float _HUDEmission;
            float _HUDSaturation;

            float _GeometryFringeStrength;
            float _GeometryFringeWidth;
            float _GeometryFringeOutputBoost;
            float _ScreenPixelScale;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            float3 ApplySaturation(float3 col, float saturation)
            {
                float luma = dot(col, float3(0.2126, 0.7152, 0.0722));
                return lerp(float3(luma, luma, luma), col, saturation);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;

                float angle = _ChromaticAngle;
                float2 dir = float2(cos(angle), sin(angle));

                float2 chromaOffset = dir * _MainTex_TexelSize.xy * _ChromaticAmount * _ScreenPixelScale;

                fixed4 centerSample = tex2D(_MainTex, uv);
                fixed4 redSample    = tex2D(_MainTex, uv + chromaOffset);
                fixed4 blueSample   = tex2D(_MainTex, uv - chromaOffset);

                // Additive chromatic overlay: preserve original center content,
                // ADD the displaced R/B channel deltas on top (fringe glow, not channel replacement).
                float3 chromaDelta = float3(
                    redSample.r  - centerSample.r,
                    0.0,
                    blueSample.b - centerSample.b
                );
                float3 finalRGB = centerSample.rgb + chromaDelta;

                // URP17 render-graph bakes alpha=1 into all cleared background pixels,
                // so we cannot use RT alpha to detect background vs content.
                // Instead derive "union presence" from luminance across all three samples.
                float rLum = max(redSample.r,    max(redSample.g,    redSample.b));
                float gLum = max(centerSample.r, max(centerSample.g, centerSample.b));
                float bLum = max(blueSample.r,   max(blueSample.g,   blueSample.b));
                float unionAlpha = max(rLum, max(gLum, bLum));

                float alphaMask = smoothstep(0.001, max(0.001, _ChromaticSoftness), unionAlpha);
                alphaMask = saturate(alphaMask * _ChromaticAlphaBoost);

                float3 sourceFloor = centerSample.rgb * _HUDSourceFloor;
                finalRGB = max(finalRGB, sourceFloor);

                float2 fringeOffset = dir * _MainTex_TexelSize.xy * _GeometryFringeWidth;
                float alphaA = tex2D(_MainTex, uv + fringeOffset).a;
                float alphaB = tex2D(_MainTex, uv - fringeOffset).a;
                float edge = abs(alphaA - alphaB);

                float3 fringeColor = float3(redSample.r, 0.0, blueSample.b);
                finalRGB += fringeColor * edge * _GeometryFringeStrength * _GeometryFringeOutputBoost;

                finalRGB = (finalRGB - 0.5) * _HUDContrast + 0.5;
                finalRGB *= _HUDBrightness;
                finalRGB += finalRGB * _HUDEmission;
                finalRGB = ApplySaturation(finalRGB, _HUDSaturation);

                fixed4 outCol;
                outCol.rgb = saturate(finalRGB) * _Color.rgb * i.color.rgb;
                outCol.a = alphaMask * _Color.a * i.color.a;

                return outCol;
            }

            ENDCG
        }
    }
}
