// RVFX MuzzleFlashesImpacts/URP/Shader/BulletTrail.shadergraph 的项目版。
//
// 颜色/透明度公式逐节点照搬原 Shader Graph（同名属性，BulletTrailEmitter 每帧写的
// _TrailLength / _HueShift 原样生效），唯一改动是 ZTest LEqual -> Always，遮挡改由
// SkyPrisonVfxHiddenFactor 按持有者落地深度判定——拖尾起点就在枪口，贴墙开枪时
// 起始那一段同样会插进墙里被吃掉。
Shader "SkyPrison/VFX/UnitAttachedBulletTrail"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1,1,1,0)
        _TrailLength ("TrailLength", Float) = 0
        _Mask_Value ("Mask_Value", Float) = 0
        _Mask_Blur ("Mask_Blur", Float) = 0
        _HueShift ("HueShift", Float) = 0

        _SkyPrison_VfxOcclusionBias ("Occlusion Bias", Range(0,2)) = 0.05
        _SkyPrison_VfxOcclusionSoftness ("Occlusion Softness", Range(0.001,2)) = 0.15
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "UnitAttachedBulletTrail"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha One, One One
            ZWrite Off
            ZTest Always
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SkyPrisonUnitAttachedVfxOcclusion.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _TrailLength;
                float _Mask_Value;
                float _Mask_Blur;
                float _HueShift;
                float _SkyPrison_VfxOcclusionBias;
                float _SkyPrison_VfxOcclusionSoftness;
            CBUFFER_END

            float4 _SkyPrison_VfxAnchorWS;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float4 positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.positionCS = positionCS;
                OUT.color = IN.color;
                OUT.uv = IN.uv;
                OUT.screenPos = ComputeScreenPos(positionCS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float4 tint = _Color * IN.color;

                // 两端渐隐：x = uv.x * TrailLength，头尾各一个 smoothstep。
                float x = IN.uv.x * _TrailLength;
                float edge2 = _Mask_Value + _Mask_Blur;
                float maskTail = smoothstep(_Mask_Value, edge2, _TrailLength - x);
                float maskHead = smoothstep(_Mask_Value, edge2, x);

                float3 rgb = SkyPrisonHueDegrees(maskTail * maskHead * tint.rgb, _HueShift);

                float2 screenUV = IN.screenPos.xy / max(IN.screenPos.w, 0.00001);
                float hidden = SkyPrisonVfxHiddenFactor(screenUV, _SkyPrison_VfxAnchorWS,
                    _SkyPrison_VfxOcclusionBias, _SkyPrison_VfxOcclusionSoftness);

                return half4(rgb, tint.a * (1.0 - hidden));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
