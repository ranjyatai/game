Shader "Hidden/SkyPrison/StencilToMask"
{
    // V1 - Copies current camera stencil == ref into a white mask RT.
    Properties
    {
        [IntRange] _StencilRef ("Stencil Ref", Range(0,255)) = 41
        [IntRange] _StencilReadMask ("Stencil Read Mask", Range(0,255)) = 255
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend One Zero
        ColorMask RGBA

        Pass
        {
            Name "StencilToMask"
            Stencil
            {
                Ref [_StencilRef]
                Comp Equal
                Pass Keep
                ReadMask [_StencilReadMask]
                WriteMask 0
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
                return half4(1,1,1,1);
            }
            ENDHLSL
        }
    }
}
