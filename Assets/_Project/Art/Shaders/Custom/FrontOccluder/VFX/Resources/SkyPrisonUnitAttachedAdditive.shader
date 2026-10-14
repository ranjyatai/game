// RVFX MuzzleFlashesImpacts/URP/Shader/Additive.shadergraph 的项目版。
//
// 颜色/透明度公式逐节点照搬原 Shader Graph（同名属性，运行时直接换 shader，材质上
// 的数值原样沿用），只改两处：
//   1. ZTest LEqual -> Always，遮挡改由 SkyPrisonVfxHiddenFactor 按持有者落地深度判定。
//   2. 去掉原版的软粒子淡出（Scene Depth - 片元深度）/ _DepthFade。那一项在贴墙时
//      同样会把火光淡没——片元在墙后，差值为负，saturate 后直接是 0，就算 ZTest
//      改成 Always 也照样看不见。ZTest Always 下不存在需要软化的穿插硬边，删掉无损。
//      _DepthFade 属性保留只是为了让原材质上的值不报丢失。
Shader "SkyPrison/VFX/UnitAttachedAdditive"
{
    Properties
    {
        _MainTex ("MainTex", 2D) = "white" {}
        [HDR] _Color ("Color", Color) = (1,1,1,0)
        _Color_Power ("Color_Power", Float) = 1
        _AngleMaskFactor ("AngleMaskFactor", Float) = 0.3
        _DepthFade ("DepthFade (unused)", Float) = 0

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
            Name "UnitAttachedAdditive"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha One, One One
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SkyPrisonUnitAttachedVfxOcclusion.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float _Color_Power;
                float _AngleMaskFactor;
                float _DepthFade;
                float _SkyPrison_VfxOcclusionBias;
                float _SkyPrison_VfxOcclusionSoftness;
            CBUFFER_END

            // 每个渲染器各自的持有者锚点，MaterialPropertyBlock 写入，不进材质 CBUFFER。
            float4 _SkyPrison_VfxAnchorWS;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                float4 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
                float4 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float4 screenPos  : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.color = IN.color;
                OUT.uv = IN.uv;
                OUT.screenPos = ComputeScreenPos(pos.positionCS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv.xy);

                // BaseColor = Hue( pow(|vc.rgb * (Color.rgb * tex.rgb)|, Color_Power), uv0.z )
                float3 rgb = abs(IN.color.rgb * (_Color.rgb * tex.rgb));
                rgb = pow(rgb, _Color_Power);
                rgb = SkyPrisonHueDegrees(rgb, IN.uv.z);

                // Alpha = vc.a * smoothstep(AMF, AMF + 0.1, |N·V|) * (1 - hidden)
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                float angle = abs(dot(normalize(IN.normalWS), viewDirWS));
                float angleMask = smoothstep(_AngleMaskFactor, _AngleMaskFactor + 0.1, angle);

                float2 screenUV = IN.screenPos.xy / max(IN.screenPos.w, 0.00001);
                float hidden = SkyPrisonVfxHiddenFactor(screenUV, _SkyPrison_VfxAnchorWS,
                    _SkyPrison_VfxOcclusionBias, _SkyPrison_VfxOcclusionSoftness);

                float alpha = IN.color.a * angleMask * (1.0 - hidden);
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
