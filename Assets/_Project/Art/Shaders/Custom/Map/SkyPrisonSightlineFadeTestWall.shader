Shader "SkyPrison/SightlineFadeTestWall"
{
    // 验证用的最小测试墙——纯色方块，接入相机挡视线淡出判定。
    //
    // 2026-08-14：原来用抖动裁切(clip)做淡出，效果是逐像素随机消失，实测颗粒感太强、
    // 看着像噪点。改成真正的alpha混合——整块区域平滑淡到同一个透明度，不再是
    // 逐像素随机的抖动图案。_MinAlpha 留 0 就是"淡到完全看不见"，调高就是"淡到
    // 半透明但还留一点存在感"，两种效果用同一个滑条切换。
    Properties
    {
        _Color ("Color", Color) = (0.5, 0.5, 0.55, 1)
        _SkyPrison_SightlineFadeRadius ("Fade Radius (屏幕宽度比例)", Range(0.01, 0.5)) = 0.08
        _SkyPrison_SightlineFadeSoftness ("Fade Softness", Range(0.001, 0.5)) = 0.06
        _SkyPrison_SightlineDepthSoftness ("Depth Softness (世界单位，深度翻转时的过渡带)", Range(0.01, 5)) = 0.5
        _SkyPrison_MinAlpha ("Min Alpha When Blocking (0=完全看不见，调高=半透明)", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SkyPrisonSightlineFade.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _SkyPrison_SightlineFadeRadius;
                float _SkyPrison_SightlineFadeSoftness;
                float _SkyPrison_SightlineDepthSoftness;
                float _SkyPrison_MinAlpha;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 screenUV = input.positionCS.xy / _ScaledScreenParams.xy;

                float fade = SkyPrisonSightlineFadeAmount(input.positionWS, screenUV,
                    _SkyPrison_SightlineFadeRadius, _SkyPrison_SightlineFadeSoftness,
                    _SkyPrison_SightlineDepthSoftness);

                // fade=0（没挡路）→ alpha=1（完全不透明）
                // fade=1（挡得最实）→ alpha=_MinAlpha（0=看不见，调高=留一点半透明）
                half4 col = _Color;
                col.a = lerp(1.0, _SkyPrison_MinAlpha, fade);
                return col;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
