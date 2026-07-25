Shader "Hidden/SkyPrison/OccluderSolidMask"
{
    // V2 - Solid fallback mask for occluder proxy renderers.
    // The clean Feature can draw occluders with their original materials for exact alpha shape.
    // This shader is only the fallback for proxy meshes that already have exact geometry.

    Properties
    {
        _MaskColor ("Mask Color", Color) = (1,1,1,1)
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
            Name "OccluderSolidMask"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            half4 _MaskColor;

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return _MaskColor;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
