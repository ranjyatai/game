Shader "Custom/FrontOccluder/Shadows/Projector"
{
    // V12 - 2026-06-06 - stencil occluder guard.
    // Commercial rule:
    // - Shadow is always drawn below the character (Transparent-80).
    // - Shadow never darkens pixels occupied by foreground occluder visuals.
    // - This is independent from character occlusion authorization.
    // - Requires SkyPrisonShadowOccluderStencilWriter pass/feature to write stencil Ref 1.
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Shadow Tint / Strength", Color) = (0,0,0,0.35)

        [Toggle] _UseOcclusionMask ("Use Occlusion Mask", Float) = 1
        [NoScaleOffset] _OcclusionTex ("Occlusion Texture", 2D) = "black" {}
        [Toggle] _UseGlobalShadowOccluderMask ("Use Global Shadow Occluder Mask", Float) = 1
        [NoScaleOffset] _SkyPrison_GlobalShadowOccluderMask ("Global Shadow Occluder Mask", 2D) = "black" {}

        [Toggle] _InvertMask ("Invert Mask", Float) = 0
        [Toggle] _FlipMaskY ("Flip Mask Y", Float) = 0
        [Toggle] _SampleBothY ("Sample Both Y Directions", Float) = 0
        _MaskThreshold ("Mask Threshold", Range(0,1)) = 0.5
        _MaskSoftness ("Mask Softness", Range(0.001,0.2)) = 0.02
        _InsideMaskAlpha ("Inside Mask Alpha", Range(0,1)) = 0.0
        _MaskDilatePixels ("Mask Dilate Pixels", Range(0,8)) = 1.0
        _MaskUvOffset ("Mask UV Offset", Vector) = (0,0,0,0)

        // Compatibility-only material fields retained so old scripts/materials do not break.
        [Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp ("Stencil Comparison Legacy Unused", Float) = 6
        _StencilRef ("Stencil Reference Legacy Unused", Float) = 1
        [Toggle] _ShadowSelfDepthEstablished ("Shadow Self Depth Established Legacy", Float) = 1
        [Toggle] _UseSceneDepthForegroundGuard ("Use Scene Depth Foreground Guard Legacy", Float) = 0
        [Toggle] _UseShadowCarrierDepthGuard ("Use Shadow Carrier Depth Guard Legacy", Float) = 0
        _ShadowCarrierEyeDepth ("Shadow Carrier Eye Depth Legacy", Float) = 0
        _ShadowCarrierWorldPosition ("Shadow Carrier World Position Legacy", Vector) = (0,0,0,1)
        _CarrierDepthForegroundBias ("Carrier Depth Foreground Bias Legacy", Range(0,0.25)) = 0.025
        _CarrierDepthForegroundSoftness ("Carrier Depth Foreground Softness Legacy", Range(0.0001,0.25)) = 0.018
        [Enum(UnityEngine.Rendering.CompareFunction)] _SkyPrison_StageShadowZTest_LegacyUnused ("Legacy ZTest Unused", Float) = 8
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "Queue"="Transparent-80"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
            "CanUseSpriteAtlas"="True"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest Always
            Blend DstColor Zero

            // Hard commercial guard:
            // foreground occluder visuals write Ref 1 before transparents;
            // shadow pixels with Ref 1 are never drawn.
            Stencil
            {
                Ref 1
                ReadMask 255
                Comp NotEqual
                Pass Keep
                Fail Keep
                ZFail Keep
            }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            TEXTURE2D(_OcclusionTex);
            SAMPLER(sampler_OcclusionTex);
            float4 _OcclusionTex_TexelSize;

            TEXTURE2D(_SkyPrison_GlobalShadowOccluderMask);
            SAMPLER(sampler_SkyPrison_GlobalShadowOccluderMask);
            float4 _SkyPrison_GlobalShadowOccluderMask_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _UseOcclusionMask;
                half _UseGlobalShadowOccluderMask;
                half _InvertMask;
                half _FlipMaskY;
                half _SampleBothY;
                half _MaskThreshold;
                half _MaskSoftness;
                half _InsideMaskAlpha;
                half _MaskDilatePixels;
                float4 _MaskUvOffset;
                half _StencilComp;
                half _StencilRef;
                half _ShadowSelfDepthEstablished;
                half _UseSceneDepthForegroundGuard;
                half _UseShadowCarrierDepthGuard;
                float _ShadowCarrierEyeDepth;
                float4 _ShadowCarrierWorldPosition;
                half _CarrierDepthForegroundBias;
                half _CarrierDepthForegroundSoftness;
                half _SkyPrison_StageShadowZTest_LegacyUnused;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                float4 screenPos  : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.color = IN.color;
                OUT.screenPos = ComputeScreenPos(OUT.positionCS);
                return OUT;
            }

            half SampleTextureMaskRaw(float2 uv, bool useGlobal)
            {
                uv = saturate(uv);
                half3 c = useGlobal
                    ? SAMPLE_TEXTURE2D(_SkyPrison_GlobalShadowOccluderMask, sampler_SkyPrison_GlobalShadowOccluderMask, uv).rgb
                    : SAMPLE_TEXTURE2D(_OcclusionTex, sampler_OcclusionTex, uv).rgb;
                return saturate(max(c.r, max(c.g, c.b)));
            }

            half SampleMaskRaw(float2 uv)
            {
                uv = saturate(uv);

                half m = SampleTextureMaskRaw(uv, false);
                if (_UseGlobalShadowOccluderMask > 0.5h)
                    m = max(m, SampleTextureMaskRaw(uv, true));

                half px = max(_MaskDilatePixels, 0.0h);
                if (px > 0.001h)
                {
                    float2 t = abs(_OcclusionTex_TexelSize.xy) * px;
                    float2 uv1 = saturate(uv + float2( t.x, 0));
                    float2 uv2 = saturate(uv + float2(-t.x, 0));
                    float2 uv3 = saturate(uv + float2(0,  t.y));
                    float2 uv4 = saturate(uv + float2(0, -t.y));

                    m = max(m, SampleTextureMaskRaw(uv1, false));
                    m = max(m, SampleTextureMaskRaw(uv2, false));
                    m = max(m, SampleTextureMaskRaw(uv3, false));
                    m = max(m, SampleTextureMaskRaw(uv4, false));

                    if (_UseGlobalShadowOccluderMask > 0.5h)
                    {
                        m = max(m, SampleTextureMaskRaw(uv1, true));
                        m = max(m, SampleTextureMaskRaw(uv2, true));
                        m = max(m, SampleTextureMaskRaw(uv3, true));
                        m = max(m, SampleTextureMaskRaw(uv4, true));
                    }
                }
                return saturate(m);
            }

            half SampleOcclusionMask(float2 screenUV)
            {
                screenUV = saturate(screenUV + _MaskUvOffset.xy);
                float2 uvA = screenUV;
                if (_FlipMaskY > 0.5h)
                    uvA.y = 1.0 - uvA.y;

                half maskA = SampleMaskRaw(uvA);

                if (_SampleBothY > 0.5h)
                {
                    float2 uvB = screenUV;
                    uvB.y = 1.0 - uvB.y;
                    half maskB = SampleMaskRaw(uvB);
                    maskA = max(maskA, maskB);
                }
                return saturate(maskA);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                half shadowAlpha = tex.a * IN.color.a * _Color.a;
                if (shadowAlpha < 0.001h)
                    discard;

                float2 screenUV = IN.screenPos.xy / max(IN.screenPos.w, 0.00001);
                screenUV = saturate(screenUV);

                // RT mask remains as a secondary soft guard. Stencil is the primary hard pixel guard.
                if (_UseOcclusionMask > 0.5h)
                {
                    half maskValue = SampleOcclusionMask(screenUV);
                    half softness = max(_MaskSoftness, 0.0001h);
                    half mask = smoothstep(
                        saturate(_MaskThreshold - softness),
                        saturate(_MaskThreshold + softness),
                        maskValue
                    );

                    if (_InvertMask > 0.5h)
                        mask = 1.0h - mask;

                    half alphaFactor = lerp(1.0h, _InsideMaskAlpha, mask);
                    shadowAlpha *= alphaFactor;
                    if (shadowAlpha < 0.001h)
                        discard;
                }

                half3 shadowColor = saturate(_Color.rgb * IN.color.rgb * tex.rgb);
                half3 multiplyFactor = lerp(half3(1.0h, 1.0h, 1.0h), shadowColor, shadowAlpha);
                return half4(multiplyFactor, 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
