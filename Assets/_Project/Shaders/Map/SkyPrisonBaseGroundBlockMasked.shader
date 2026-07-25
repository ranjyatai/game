Shader "SkyPrison/Map/BaseGroundBlockMasked"
{
    Properties
    {
        _BaseMap("Base Texture", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (0.34, 0.34, 0.34, 1)
        _GroundShapeMask("Ground Shape Mask", 2D) = "white" {}
        _MaskThreshold("Mask Threshold", Range(0, 1)) = 0.01
        _MaskSoftness("Mask Edge Softness", Range(0.0001, 0.5)) = 0.02
        _ReceiveShadowStrength("Receive Shadow Strength", Range(0, 1)) = 1
        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5

        // URP Lit compatible render state. These are intentionally present so the material behaves like URP/Lit.
        _Surface("__surface", Float) = 0.0
        _Blend("__blend", Float) = 0.0
        _Cull("__cull", Float) = 2.0
        [ToggleUI] _AlphaClip("__clip", Float) = 1.0
        [HideInInspector] _SrcBlend("__src", Float) = 1.0
        [HideInInspector] _DstBlend("__dst", Float) = 0.0
        [HideInInspector] _SrcBlendAlpha("__srcA", Float) = 1.0
        [HideInInspector] _DstBlendAlpha("__dstA", Float) = 0.0
        [HideInInspector] _ZWrite("__zw", Float) = 1.0
        [HideInInspector] _BlendModePreserveSpecular("_BlendModePreserveSpecular", Float) = 1.0
        [HideInInspector] _AlphaToMask("__alphaToMask", Float) = 1.0
        [ToggleUI] _ReceiveShadows("Receive Shadows", Float) = 1.0
        _QueueOffset("Queue offset", Float) = 0.0
        [HideInInspector] _MainTex("BaseMap", 2D) = "white" {}
        [HideInInspector] _Color("Base Color", Color) = (1,1,1,1)

        _SurfaceIndexMap("Surface Material Index Map", 2D) = "black" {}
        _UseSurfaceIndexMap("Use Surface Index Map", Float) = 0
        _SurfaceTex0("Surface Texture 0", 2D) = "white" {}
        _SurfaceTex1("Surface Texture 1", 2D) = "white" {}
        _SurfaceTex2("Surface Texture 2", 2D) = "white" {}
        _SurfaceTex3("Surface Texture 3", 2D) = "white" {}
        _SurfaceTex4("Surface Texture 4", 2D) = "white" {}
        _SurfaceTex5("Surface Texture 5", 2D) = "white" {}
        _SurfaceTex6("Surface Texture 6", 2D) = "white" {}
        _SurfaceTex7("Surface Texture 7", 2D) = "white" {}
        _SurfaceColor0("Surface Color 0", Color) = (1,1,1,1)
        _SurfaceColor1("Surface Color 1", Color) = (1,1,1,1)
        _SurfaceColor2("Surface Color 2", Color) = (1,1,1,1)
        _SurfaceColor3("Surface Color 3", Color) = (1,1,1,1)
        _SurfaceColor4("Surface Color 4", Color) = (1,1,1,1)
        _SurfaceColor5("Surface Color 5", Color) = (1,1,1,1)
        _SurfaceColor6("Surface Color 6", Color) = (1,1,1,1)
        _SurfaceColor7("Surface Color 7", Color) = (1,1,1,1)
        _TextureAntiShimmer("Texture Anti Shimmer", Range(0, 1)) = 0.95
        _DetailFadeStart("Detail Fade Start", Range(0.0005, 0.08)) = 0.0035
        _DetailFadeEnd("Detail Fade End", Range(0.001, 0.12)) = 0.018
        _SurfaceTextureStrength("Legacy Texture Strength", Range(0, 1)) = 0.35
        _SurfaceTextureWeight("Surface Texture Weight", Range(0, 1)) = 1.0
        _SurfaceColorTintStrength("Surface Color Tint Strength", Range(0, 1)) = 0.15
        _FallbackColorStrength("Fallback Color Strength", Range(0, 1)) = 1.0
        _SurfaceMipBias("Surface Mip Bias", Range(0, 8)) = 3.0
        _SurfaceHasTexture0("Surface Has Texture 0", Float) = 0
        _SurfaceHasTexture1("Surface Has Texture 1", Float) = 0
        _SurfaceHasTexture2("Surface Has Texture 2", Float) = 0
        _SurfaceHasTexture3("Surface Has Texture 3", Float) = 0
        _SurfaceHasTexture4("Surface Has Texture 4", Float) = 0
        _SurfaceHasTexture5("Surface Has Texture 5", Float) = 0
        _SurfaceHasTexture6("Surface Has Texture 6", Float) = 0
        _SurfaceHasTexture7("Surface Has Texture 7", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Blend[_SrcBlend][_DstBlend], [_SrcBlendAlpha][_DstBlendAlpha]
            ZWrite[_ZWrite]
            ZTest LEqual
            Offset -1, -1
            Cull[_Cull]
            AlphaToMask[_AlphaToMask]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local _RECEIVE_SHADOWS_OFF
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _SURFACE_TYPE_TRANSPARENT
            #pragma shader_feature_local_fragment _ _ALPHAPREMULTIPLY_ON _ALPHAMODULATE_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 baseUV : TEXCOORD0;
                float2 dataUV : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float3 normalWS : TEXCOORD3;
                float4 shadowCoord : TEXCOORD4;
                float fogCoord : TEXCOORD5;
            };

            TEXTURE2D(_BaseMap);              SAMPLER(sampler_BaseMap);
            TEXTURE2D(_GroundShapeMask);      SAMPLER(sampler_GroundShapeMask);
            TEXTURE2D(_SurfaceIndexMap);      SAMPLER(sampler_SurfaceIndexMap);
            TEXTURE2D(_SurfaceTex0);          SAMPLER(sampler_SurfaceTex0);
            TEXTURE2D(_SurfaceTex1);          SAMPLER(sampler_SurfaceTex1);
            TEXTURE2D(_SurfaceTex2);          SAMPLER(sampler_SurfaceTex2);
            TEXTURE2D(_SurfaceTex3);          SAMPLER(sampler_SurfaceTex3);
            TEXTURE2D(_SurfaceTex4);          SAMPLER(sampler_SurfaceTex4);
            TEXTURE2D(_SurfaceTex5);          SAMPLER(sampler_SurfaceTex5);
            TEXTURE2D(_SurfaceTex6);          SAMPLER(sampler_SurfaceTex6);
            TEXTURE2D(_SurfaceTex7);          SAMPLER(sampler_SurfaceTex7);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _MaskThreshold;
                float _MaskSoftness;
                float _ReceiveShadowStrength;
                float _Cutoff;
                float _UseSurfaceIndexMap;
                float4 _SurfaceColor0;
                float4 _SurfaceColor1;
                float4 _SurfaceColor2;
                float4 _SurfaceColor3;
                float4 _SurfaceColor4;
                float4 _SurfaceColor5;
                float4 _SurfaceColor6;
                float4 _SurfaceColor7;
                float _TextureAntiShimmer;
                float _DetailFadeStart;
                float _DetailFadeEnd;
                float _SurfaceTextureStrength;
                float _SurfaceTextureWeight;
                float _SurfaceColorTintStrength;
                float _FallbackColorStrength;
                float _SurfaceMipBias;
                float _SurfaceHasTexture0;
                float _SurfaceHasTexture1;
                float _SurfaceHasTexture2;
                float _SurfaceHasTexture3;
                float _SurfaceHasTexture4;
                float _SurfaceHasTexture5;
                float _SurfaceHasTexture6;
                float _SurfaceHasTexture7;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

                output.positionHCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = NormalizeNormalPerVertex(normalInputs.normalWS);
#if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                output.shadowCoord = ComputeScreenPos(positionInputs.positionCS);
#else
                output.shadowCoord = TransformWorldToShadowCoord(positionInputs.positionWS);
#endif
                output.baseUV = TRANSFORM_TEX(input.uv, _BaseMap);
                output.dataUV = saturate(input.uv);
                output.fogCoord = ComputeFogFactor(positionInputs.positionCS.z);
                return output;
            }

            half4 GetSurfaceColor(int index)
            {
                if (index == 1) return _SurfaceColor1;
                if (index == 2) return _SurfaceColor2;
                if (index == 3) return _SurfaceColor3;
                if (index == 4) return _SurfaceColor4;
                if (index == 5) return _SurfaceColor5;
                if (index == 6) return _SurfaceColor6;
                if (index == 7) return _SurfaceColor7;
                return _SurfaceColor0;
            }

            half4 SampleSurfaceRaw(int index, float2 uv)
            {
                if (index == 1) return SAMPLE_TEXTURE2D_BIAS(_SurfaceTex1, sampler_SurfaceTex1, uv, _SurfaceMipBias);
                if (index == 2) return SAMPLE_TEXTURE2D_BIAS(_SurfaceTex2, sampler_SurfaceTex2, uv, _SurfaceMipBias);
                if (index == 3) return SAMPLE_TEXTURE2D_BIAS(_SurfaceTex3, sampler_SurfaceTex3, uv, _SurfaceMipBias);
                if (index == 4) return SAMPLE_TEXTURE2D_BIAS(_SurfaceTex4, sampler_SurfaceTex4, uv, _SurfaceMipBias);
                if (index == 5) return SAMPLE_TEXTURE2D_BIAS(_SurfaceTex5, sampler_SurfaceTex5, uv, _SurfaceMipBias);
                if (index == 6) return SAMPLE_TEXTURE2D_BIAS(_SurfaceTex6, sampler_SurfaceTex6, uv, _SurfaceMipBias);
                if (index == 7) return SAMPLE_TEXTURE2D_BIAS(_SurfaceTex7, sampler_SurfaceTex7, uv, _SurfaceMipBias);
                return SAMPLE_TEXTURE2D_BIAS(_SurfaceTex0, sampler_SurfaceTex0, uv, _SurfaceMipBias);
            }

            half GetSurfaceHasTexture(int index)
            {
                if (index == 1) return saturate(_SurfaceHasTexture1);
                if (index == 2) return saturate(_SurfaceHasTexture2);
                if (index == 3) return saturate(_SurfaceHasTexture3);
                if (index == 4) return saturate(_SurfaceHasTexture4);
                if (index == 5) return saturate(_SurfaceHasTexture5);
                if (index == 6) return saturate(_SurfaceHasTexture6);
                if (index == 7) return saturate(_SurfaceHasTexture7);
                return saturate(_SurfaceHasTexture0);
            }

            half4 SampleSurface(int index, float2 uv)
            {
                half4 tint = GetSurfaceColor(index);
                half4 tex = SampleSurfaceRaw(index, uv);
                half hasTexture = GetSurfaceHasTexture(index);
                half textureWeight = saturate(_SurfaceTextureWeight);
                half tintStrength = saturate(_SurfaceColorTintStrength);
                half fallbackStrength = saturate(_FallbackColorStrength);

                half3 tintColor = max(tint.rgb, 0.0h);
                half3 tintedTexture = tex.rgb * lerp(half3(1.0h, 1.0h, 1.0h), tintColor, tintStrength);
                half3 textureDriven = lerp(tintColor, tintedTexture, textureWeight);
                half3 fallbackDriven = lerp(half3(1.0h, 1.0h, 1.0h), tintColor, fallbackStrength);

                half4 result;
                result.rgb = lerp(fallbackDriven, textureDriven, hasTexture);
                result.a = 1.0h;
                return result;
            }

            half ComputeDetailFade(float2 uv)
            {
                float2 dx = ddx(uv);
                float2 dy = ddy(uv);
                float footprint = max(length(dx), length(dy));
                footprint *= 2.25;
                float fade = saturate((footprint - _DetailFadeStart) / max(0.0001, _DetailFadeEnd - _DetailFadeStart));
                return (half)(fade * saturate(_TextureAntiShimmer));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half mask = SAMPLE_TEXTURE2D(_GroundShapeMask, sampler_GroundShapeMask, input.dataUV).a;
                clip(mask - _MaskThreshold);

                half4 baseColor;
                if (_UseSurfaceIndexMap > 0.5)
                {
                    half encoded = SAMPLE_TEXTURE2D(_SurfaceIndexMap, sampler_SurfaceIndexMap, input.dataUV).r;
                    int surfaceIndex = (int)round(saturate(encoded) * 255.0);
                    surfaceIndex = clamp(surfaceIndex, 0, 7);
                    baseColor = SampleSurface(surfaceIndex, input.baseUV);
                    half detailFade = ComputeDetailFade(input.baseUV);
                    baseColor = lerp(baseColor, GetSurfaceColor(surfaceIndex), detailFade);
                }
                else
                {
                    baseColor = SAMPLE_TEXTURE2D_BIAS(_BaseMap, sampler_BaseMap, input.baseUV, _SurfaceMipBias) * _BaseColor;
                    half detailFade = ComputeDetailFade(input.baseUV);
                    baseColor = lerp(baseColor, _BaseColor, detailFade);
                }

                // URP Lit path: this lets the ground participate in the same realtime shadow receiving path as URP/Lit.
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = NormalizeNormalPerPixel(input.normalWS);
                inputData.viewDirectionWS = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                inputData.shadowCoord = input.shadowCoord;
                inputData.fogCoord = input.fogCoord;
                inputData.vertexLighting = half3(0, 0, 0);
                inputData.bakedGI = SampleSH(inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionHCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = baseColor.rgb;
                surfaceData.alpha = 1.0h;
                surfaceData.metallic = 0.0h;
                surfaceData.specular = half3(0.05h, 0.05h, 0.05h);
                surfaceData.smoothness = 0.35h;
                surfaceData.normalTS = half3(0, 0, 1);
                surfaceData.occlusion = 1.0h;
                surfaceData.emission = half3(0, 0, 0);

                half4 lit = UniversalFragmentPBR(inputData, surfaceData);

                // Optional receiver strength: 1 means full URP shadow. Lower values soften the visual result.
                // We approximate by blending toward the unshadowed/albedo-lit value only when users reduce strength.
                if (_ReceiveShadowStrength < 0.999)
                {
                    Light mainLight = GetMainLight(input.shadowCoord);
                    half shadow = mainLight.shadowAttenuation;
                    half inv = saturate(1.0h - shadow);
                    lit.rgb = lerp(lit.rgb + baseColor.rgb * inv * 0.35h, lit.rgb, saturate(_ReceiveShadowStrength));
                }

                lit.rgb = MixFog(lit.rgb, input.fogCoord);
                lit.a = 1.0h;
                return lit;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthOnlyVert
            #pragma fragment DepthOnlyFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct DepthOnlyAttributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct DepthOnlyVaryings
            {
                float4 positionHCS : SV_POSITION;
                float2 dataUV : TEXCOORD0;
            };

            TEXTURE2D(_GroundShapeMask); SAMPLER(sampler_GroundShapeMask);

            CBUFFER_START(UnityPerMaterial)
                float _MaskThreshold;
            CBUFFER_END

            DepthOnlyVaryings DepthOnlyVert(DepthOnlyAttributes input)
            {
                DepthOnlyVaryings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = positionInputs.positionCS;
                output.dataUV = saturate(input.uv);
                return output;
            }

            half4 DepthOnlyFrag(DepthOnlyVaryings input) : SV_Target
            {
                half mask = SAMPLE_TEXTURE2D(_GroundShapeMask, sampler_GroundShapeMask, input.dataUV).a;
                clip(mask - _MaskThreshold);
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
