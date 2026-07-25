// 专用角色 presence 捕获 shader
// 由 CharacterPresenceFeature 通过 cmd.DrawRenderer 使用
// 输出 R=1 在角色不透明区域，配合 R8 presence RT 使用
Shader "Hidden/SP/CharPresenceCapture"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        ZTest  Always
        ZWrite Off
        Blend  One Zero
        ColorMask R
        Cull   Off

        Pass
        {
            Name "CharPresenceCapture"

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
            CBUFFER_END

            struct Attributes
            {
                float4 posOS  : POSITION;
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
            };

            struct Varyings
            {
                float4 posHCS : SV_POSITION;
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.posHCS = TransformObjectToHClip(IN.posOS.xyz);
                OUT.uv     = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color  = IN.color;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * IN.color;
                clip(tex.a - 0.01);
                return float4(1, 1, 1, 1);
            }
            ENDHLSL
        }
    }
}
