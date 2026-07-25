Shader "Hidden/SkyPrison/StencilClearOnly"
{
    // V1 - Fullscreen stencil reset. ColorMask 0, does not touch final color.
    Properties
    {
        [IntRange] _StencilWriteMask ("Stencil Write Mask", Range(0,255)) = 255
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Cull Off
        ZWrite Off
        ZTest Always
        ColorMask 0

        Pass
        {
            Name "StencilClearOnly"
            Stencil
            {
                Ref 0
                Comp Always
                Pass Replace
                ReadMask 255
                WriteMask [_StencilWriteMask]
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return half4(0,0,0,0);
            }
            ENDHLSL
        }
    }
}
