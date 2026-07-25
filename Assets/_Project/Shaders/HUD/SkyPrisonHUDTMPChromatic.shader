Shader "Sky Prison/UI/HUD TMP Chromatic V83"
{
    Properties
    {
        _MainTex ("Font Atlas", 2D) = "white" {}
        _FaceColor ("Face Color", Color) = (1,1,1,1)
        _Color ("Tint", Color) = (1,1,1,1)

        [Header(True Chromatic Aberration)]
        _ChromaticAmount ("真实色收差强度 / 像素", Range(0, 24)) = 6
        _ChromaticAngle ("真实色收差方向 / 角度", Range(0, 360)) = 0
        _ChromaticSoftness ("真实色收差混合", Range(0, 1)) = 1
        _ChromaticAlphaBoost ("透明度增强", Range(0.5, 2)) = 1

        [Header(Energy Fringe)]
        _EnergyFringeStrength ("文字能量红蓝边强度", Range(0, 4)) = 1.1
        _EnergyFringeWidth ("文字能量红蓝边宽度 / 像素", Range(0, 24)) = 3
        _EnergyFringeAlpha ("文字能量红蓝边透明度", Range(0, 2)) = 0.7

        [Header(Legacy Geometry Fringe)]
        _GeometryFringeStrength ("旧边缘色散强度", Range(0, 4)) = 0
        _GeometryFringeWidth ("旧边缘色散宽度 / 像素", Range(0, 24)) = 0
        _GeometryFringeOutputBoost ("旧边缘色散增强", Range(0, 4)) = 0
        _GeometryFringeDebug ("旧边缘色散调试", Range(0, 1)) = 0

        [Header(Channel Tint)]
        _RedTint ("Red Tint", Color) = (1,0.18,0.12,1)
        _GreenTint ("Green Tint", Color) = (1,1,1,1)
        _BlueTint ("Blue Tint", Color) = (0.18,0.52,1,1)

        [Header(HUD Color)]
        _HUDBrightness ("亮度", Range(0, 4)) = 1
        _HUDContrast ("对比度", Range(0, 3)) = 1
        _HUDEmission ("自发光增益", Range(0, 2)) = 0
        _HUDLightStrength ("光强兼容参数", Range(0, 4)) = 1
        _HUDSourcePreserve ("源图保留", Range(0, 1)) = 0.85
        _HUDSourceFloor ("源图下限", Range(0, 1)) = 0.65
        _HUDSaturation ("饱和度", Range(0, 3)) = 1

        [Header(Backdrop Compatibility)]
        _UsePSBackdropRecipe ("Use PS Backdrop Recipe", Range(0, 1)) = 0
        _BackdropSampleStrength ("Backdrop Sample Strength", Range(0, 2)) = 0
        _BackdropExposure ("Backdrop Exposure", Range(0, 4)) = 1
        _BackdropFallbackColor ("Backdrop Fallback Color", Color) = (0.03,0.08,0.13,1)

        [HideInInspector] _SkyPrisonBlendMode ("Sky Prison Blend Mode", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10
        [Enum(UnityEngine.Rendering.BlendOp)] _BlendOp ("Blend Op", Float) = 0

        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
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
        BlendOp [_BlendOp]
        Blend [_SrcBlend] [_DstBlend]
        ColorMask [_ColorMask]

        Pass
        {
            Name "TrueChromaticTMP"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            fixed4 _FaceColor;
            fixed4 _Color;
            float4 _ClipRect;

            float _ChromaticAmount;
            float _ChromaticAngle;
            float _ChromaticSoftness;
            float _ChromaticAlphaBoost;

            float _EnergyFringeStrength;
            float _EnergyFringeWidth;
            float _EnergyFringeAlpha;

            float _GeometryFringeStrength;
            float _GeometryFringeWidth;
            float _GeometryFringeOutputBoost;
            float _GeometryFringeDebug;

            float4 _RedTint;
            float4 _GreenTint;
            float4 _BlueTint;

            float _HUDBrightness;
            float _HUDContrast;
            float _HUDEmission;
            float _HUDLightStrength;
            float _HUDSourcePreserve;
            float _HUDSourceFloor;
            float _HUDSaturation;

            struct appdata_t
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
            };

            v2f vert(appdata_t v)
            {
                v2f o;
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color = v.color * _Color * _FaceColor;
                return o;
            }

            float3 AdjustSaturation(float3 c, float sat)
            {
                float luma = dot(c, float3(0.2126, 0.7152, 0.0722));
                return lerp(float3(luma, luma, luma), c, sat);
            }

            fixed4 SampleTMP(float2 uv)
            {
                fixed4 s = tex2D(_MainTex, saturate(uv));
                fixed4 c = fixed4(1, 1, 1, s.a);

                c.rgb = saturate((c.rgb - 0.5) * _HUDContrast + 0.5);
                c.rgb = saturate(c.rgb * _HUDBrightness + _HUDEmission);
                c.rgb = AdjustSaturation(c.rgb, _HUDSaturation);

                return c;
            }

            float SampleAlpha(float2 uv)
            {
                return SampleTMP(uv).a;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float amountPx = max(0.0, _ChromaticAmount);
                float splitMix = saturate(_ChromaticSoftness);

                float angleRad = radians(_ChromaticAngle);
                float2 dir = float2(cos(angleRad), sin(angleRad));
                float2 splitOffset = dir * amountPx * _MainTex_TexelSize.xy;

                fixed4 centerSample = SampleTMP(i.uv);
                fixed4 redSample    = SampleTMP(i.uv - splitOffset);
                fixed4 blueSample   = SampleTMP(i.uv + splitOffset);

                centerSample *= i.color;
                redSample *= i.color;
                blueSample *= i.color;

                fixed4 splitColor;
                splitColor.r = redSample.r * _RedTint.r;
                splitColor.g = centerSample.g * _GreenTint.g;
                splitColor.b = blueSample.b * _BlueTint.b;
                splitColor.a = max(centerSample.a, max(redSample.a, blueSample.a));
                splitColor.a = saturate(splitColor.a * _ChromaticAlphaBoost);

                fixed4 finalColor;
                finalColor.rgb = lerp(centerSample.rgb, splitColor.rgb, splitMix);
                finalColor.a = lerp(centerSample.a, splitColor.a, splitMix);

                float fringePx = max(0.0, _EnergyFringeWidth);
                float2 fringeOffset = dir * fringePx * _MainTex_TexelSize.xy;

                float alphaCenter = centerSample.a;
                float alphaLeft   = SampleAlpha(i.uv - fringeOffset);
                float alphaRight  = SampleAlpha(i.uv + fringeOffset);

                float redEdge = saturate(alphaLeft - alphaCenter) + saturate(alphaCenter - alphaRight);
                float blueEdge = saturate(alphaRight - alphaCenter) + saturate(alphaCenter - alphaLeft);
                redEdge = saturate(redEdge * _EnergyFringeStrength);
                blueEdge = saturate(blueEdge * _EnergyFringeStrength);

                float energyAlpha = saturate(_EnergyFringeAlpha);
                finalColor.rgb += _RedTint.rgb * redEdge * energyAlpha;
                finalColor.rgb += _BlueTint.rgb * blueEdge * energyAlpha;
                finalColor.a = saturate(max(finalColor.a, max(redEdge, blueEdge) * energyAlpha * i.color.a));

                #ifdef UNITY_UI_CLIP_RECT
                finalColor.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif

                return finalColor;
            }
            ENDCG
        }
    }

    FallBack "TextMeshPro/Distance Field"
}
