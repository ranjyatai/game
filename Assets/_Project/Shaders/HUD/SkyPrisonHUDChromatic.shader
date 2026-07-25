Shader "Sky Prison/UI/HUD Chromatic V83"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _ChromaticAmount ("Chromatic Amount Pixels", Range(0, 64)) = 8
        _ChromaticAngle ("Chromatic Angle", Range(0, 360)) = 0
        _ChromaticSoftness ("Chromatic Mix", Range(0, 1)) = 1
        _ChromaticAlphaBoost ("Alpha Boost", Range(0.5, 2)) = 1

        _EnergyFringeStrength ("Energy Fringe Strength", Range(0, 4)) = 0.35
        _EnergyFringeWidth ("Energy Fringe Width Pixels", Range(0, 64)) = 4
        _EnergyFringeAlpha ("Energy Fringe Alpha", Range(0, 1)) = 0

        // V88:
        // Rect edge chromatic uses screen-space UV derivatives, not _MainTex_TexelSize.
        // This is essential for Source Image=None, because Unity's default white texture is 1x1
        // and _MainTex_TexelSize would make the "edge" cover the entire bar.
        _RectEdgeFringeStrength ("Rect Edge Fringe Strength", Range(0, 1)) = 0.35
        _RectEdgeFringeWidth ("Rect Edge Fringe Width Pixels", Range(0, 32)) = 3
        _RectEdgeAlpha ("Rect Edge Alpha Add", Range(0, 1)) = 0

        _RedTint ("Red Tint", Color) = (1,0.12,0.05,1)
        _GreenTint ("Green Tint", Color) = (1,1,1,1)
        _BlueTint ("Blue Tint", Color) = (0.08,0.42,1,1)

        _HUDBrightness ("HUD Brightness", Range(0, 4)) = 1
        _HUDContrast ("HUD Contrast", Range(0, 3)) = 1
        _HUDEmission ("HUD Emission", Range(0, 2)) = 0
        _HUDSaturation ("HUD Saturation", Range(0, 3)) = 1

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

        [HideInInspector] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
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
        BlendOp [_BlendOp]
        Blend [_SrcBlend] [_DstBlend]
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;

            float _ChromaticAmount;
            float _ChromaticAngle;
            float _ChromaticSoftness;
            float _ChromaticAlphaBoost;

            float _EnergyFringeStrength;
            float _EnergyFringeWidth;
            float _EnergyFringeAlpha;

            float _RectEdgeFringeStrength;
            float _RectEdgeFringeWidth;
            float _RectEdgeAlpha;

            float4 _RedTint;
            float4 _GreenTint;
            float4 _BlueTint;

            float _HUDBrightness;
            float _HUDContrast;
            float _HUDEmission;
            float _HUDSaturation;

            struct appdata_t
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            float3 AdjustSaturation(float3 c, float sat)
            {
                float luma = dot(c, float3(0.2126, 0.7152, 0.0722));
                return lerp(float3(luma, luma, luma), c, sat);
            }

            fixed4 SampleUI(float2 uv)
            {
                fixed4 c = tex2D(_MainTex, uv) + _TextureSampleAdd;
                c.rgb = (c.rgb - 0.5) * max(0.0, _HUDContrast) + 0.5;
                c.rgb = c.rgb * max(0.0, _HUDBrightness) + _HUDEmission;
                c.rgb = AdjustSaturation(c.rgb, max(0.0, _HUDSaturation));
                c.rgb = saturate(c.rgb);
                return c;
            }

            float EdgeFade(float distanceUv, float widthUv)
            {
                widthUv = max(widthUv, 0.000001);
                return saturate(1.0 - distanceUv / widthUv);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float angleRad = radians(_ChromaticAngle);
                float2 dir = float2(cos(angleRad), sin(angleRad));

                // Texture RGB split. This works for real sprites/textures.
                float amountPx = max(0.0, _ChromaticAmount);
                float2 splitOffset = dir * amountPx * _MainTex_TexelSize.xy;

                fixed4 centerSample = SampleUI(i.uv);
                fixed4 redSample = SampleUI(i.uv - splitOffset);
                fixed4 blueSample = SampleUI(i.uv + splitOffset);

                centerSample *= i.color;
                redSample *= i.color;
                blueSample *= i.color;

                fixed4 splitColor;
                splitColor.r = redSample.r;
                splitColor.g = centerSample.g;
                splitColor.b = blueSample.b;
                splitColor.a = centerSample.a;

                fixed4 finalColor;
                finalColor.rgb = lerp(centerSample.rgb, splitColor.rgb, saturate(_ChromaticSoftness));
                finalColor.a = saturate(centerSample.a * _ChromaticAlphaBoost);

                // Texture alpha fringe for real sprites only.
                float fringePx = max(0.0, _EnergyFringeWidth);
                float2 fringeOffset = dir * fringePx * _MainTex_TexelSize.xy;
                fixed4 fringeL = SampleUI(i.uv - fringeOffset) * i.color;
                fixed4 fringeR = SampleUI(i.uv + fringeOffset) * i.color;
                float outer = saturate(max(fringeL.a, fringeR.a) - centerSample.a);
                float3 textureTint = lerp(_RedTint.rgb, _BlueTint.rgb, step(fringeL.a, fringeR.a));
                finalColor.rgb = lerp(finalColor.rgb, textureTint, saturate(outer * _EnergyFringeStrength));
                finalColor.a = saturate(finalColor.a + outer * _EnergyFringeAlpha);

                // V88 Rect edge tint for Source Image=None / flat UI rectangles.
                // Use fwidth(i.uv) to convert "pixels" to local UV width.
                // This keeps the fringe at the actual screen-space edge instead of flooding the full rect.
                float2 uvPerPixel = max(fwidth(i.uv), float2(0.000001, 0.000001));
                float2 edgeUv = uvPerPixel * max(0.0, _RectEdgeFringeWidth);

                float horizontalWeight = abs(dir.x);
                float verticalWeight = abs(dir.y);

                float leftEdge = EdgeFade(i.uv.x, edgeUv.x);
                float rightEdge = EdgeFade(1.0 - i.uv.x, edgeUv.x);
                float bottomEdge = EdgeFade(i.uv.y, edgeUv.y);
                float topEdge = EdgeFade(1.0 - i.uv.y, edgeUv.y);

                float redEdge = 0.0;
                float blueEdge = 0.0;

                if (dir.x >= 0.0)
                {
                    redEdge += leftEdge * horizontalWeight;
                    blueEdge += rightEdge * horizontalWeight;
                }
                else
                {
                    redEdge += rightEdge * horizontalWeight;
                    blueEdge += leftEdge * horizontalWeight;
                }

                if (dir.y >= 0.0)
                {
                    redEdge += bottomEdge * verticalWeight;
                    blueEdge += topEdge * verticalWeight;
                }
                else
                {
                    redEdge += topEdge * verticalWeight;
                    blueEdge += bottomEdge * verticalWeight;
                }

                redEdge = saturate(redEdge);
                blueEdge = saturate(blueEdge);

                float redMix = saturate(redEdge * _RectEdgeFringeStrength * centerSample.a);
                float blueMix = saturate(blueEdge * _RectEdgeFringeStrength * centerSample.a);

                finalColor.rgb = lerp(finalColor.rgb, _RedTint.rgb, redMix);
                finalColor.rgb = lerp(finalColor.rgb, _BlueTint.rgb, blueMix);
                finalColor.a = saturate(finalColor.a + max(redEdge, blueEdge) * _RectEdgeAlpha * centerSample.a);

                #ifdef UNITY_UI_CLIP_RECT
                finalColor.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(finalColor.a - 0.001);
                #endif

                return finalColor;
            }
            ENDCG
        }
    }

    FallBack "UI/Default"
}
