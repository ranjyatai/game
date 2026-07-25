Shader "Hidden/SkyPrison/ShadowOccluderStencilWriter"
{
    // V1 - 2026-06-06
    // Writes foreground occluder visual geometry into the Main Camera stencil buffer only.
    // Used by stage/contact shadows so they can be rejected by actual Main Camera pixels,
    // independently from character occlusion authorization.
    Properties
    {
        _StencilRef ("Stencil Ref", Float) = 1
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 8 // Always
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+50" "RenderType"="Opaque" }
        Pass
        {
            Name "ShadowOccluderStencilOnly"
            Tags { "LightMode"="UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest [_ZTest]
            ColorMask 0

            Stencil
            {
                Ref [_StencilRef]
                WriteMask [_StencilWriteMask]
                Comp Always
                Pass Replace
                Fail Keep
                ZFail Keep
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return half4(0,0,0,0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
