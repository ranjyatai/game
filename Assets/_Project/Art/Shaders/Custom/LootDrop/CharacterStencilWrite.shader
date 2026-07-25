// 用于 URP Renderer Feature：把角色网格的屏幕轮廓写入模板缓冲（Ref=1）
// 不输出任何颜色，不影响深度，只标记"这里有角色"
Shader "Hidden/SkyPrison/CharacterStencilWrite"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "StencilWrite"
            ColorMask 0
            ZWrite Off
            ZTest Always     // 不管深度，只要是角色网格的像素就标记

            Stencil
            {
                Ref  1
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 posOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 posHCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.posHCS = TransformObjectToHClip(IN.posOS.xyz);
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack Off
}
