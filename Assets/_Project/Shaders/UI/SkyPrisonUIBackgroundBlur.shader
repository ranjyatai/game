Shader "Hidden/SkyPrison/UIBackgroundBlur"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always
        ZWrite Off
        Cull Off
        Blend Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        float2 _UIBlur_TexelSize; // 1/width, 1/height of current source
        float  _UIBlur_Radius;    // pixel spread per tap

        // 9-tap separable Gaussian weights (sigma ≈ 2)
        static const float W[5] = { 0.2270270270, 0.1945945946, 0.1216216216, 0.0540540541, 0.0162162162 };

        float4 SampleSrc(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
        }

        // Pass 0 — bilinear downsample (plain blit at lower res)
        float4 FragDownsample(Varyings input) : SV_Target
        {
            return SampleSrc(input.texcoord);
        }

        // Pass 1 — horizontal Gaussian
        float4 FragBlurH(Varyings input) : SV_Target
        {
            float2 uv     = input.texcoord;
            float2 offset = float2(_UIBlur_TexelSize.x * _UIBlur_Radius, 0.0);
            float4 col    = SampleSrc(uv) * W[0];
            UNITY_UNROLL
            for (int i = 1; i < 5; i++)
            {
                col += SampleSrc(uv + offset * i) * W[i];
                col += SampleSrc(uv - offset * i) * W[i];
            }
            return col;
        }

        // Pass 2 — vertical Gaussian
        float4 FragBlurV(Varyings input) : SV_Target
        {
            float2 uv     = input.texcoord;
            float2 offset = float2(0.0, _UIBlur_TexelSize.y * _UIBlur_Radius);
            float4 col    = SampleSrc(uv) * W[0];
            UNITY_UNROLL
            for (int i = 1; i < 5; i++)
            {
                col += SampleSrc(uv + offset * i) * W[i];
                col += SampleSrc(uv - offset * i) * W[i];
            }
            return col;
        }
        ENDHLSL

        Pass { Name "Downsample" HLSLPROGRAM #pragma vertex Vert #pragma fragment FragDownsample ENDHLSL }
        Pass { Name "BlurH"      HLSLPROGRAM #pragma vertex Vert #pragma fragment FragBlurH      ENDHLSL }
        Pass { Name "BlurV"      HLSLPROGRAM #pragma vertex Vert #pragma fragment FragBlurV      ENDHLSL }
    }
}
