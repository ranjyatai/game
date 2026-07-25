Shader "Hidden/SkyPrison/OrthographicDistanceBlur"
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
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        TEXTURE2D_X(_SkyPrisonBlurHalfTex);
        SAMPLER(sampler_SkyPrisonBlurHalfTex);
        TEXTURE2D_X(_SkyPrisonBlurQuarterTex);
        SAMPLER(sampler_SkyPrisonBlurQuarterTex);

        float _SkyPrisonFocusDistance;
        float _SkyPrisonBlurRange;
        float _SkyPrisonMaxRadius;
        float _SkyPrisonIntensity;
        float _SkyPrisonMaskMode;
        float _SkyPrisonScreenBlurStartY;
        float _SkyPrisonScreenBlurEndY;
        float _SkyPrisonScreenFocusY;
        float _SkyPrisonScreenClearHalfHeight;
        float _SkyPrisonScreenFocusFadeRange;
        float _SkyPrisonDebugShowMask;
        float _SkyPrisonHalfBlurRadiusScale;
        float _SkyPrisonQuarterBlurRadiusScale;

        float4 SampleBlit(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
        }

        float GetDepthBlurMask(float2 uv)
        {
            float rawDepth = SampleSceneDepth(uv);
            float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
            return saturate((eyeDepth - _SkyPrisonFocusDistance) / max(0.0001, _SkyPrisonBlurRange));
        }

        float GetScreenYBlurMask(float2 uv)
        {
            return saturate((uv.y - _SkyPrisonScreenBlurStartY) / max(0.0001, _SkyPrisonScreenBlurEndY - _SkyPrisonScreenBlurStartY));
        }

        float GetScreenFocusBandBlurMask(float2 uv)
        {
            float outsideClearBand = abs(uv.y - _SkyPrisonScreenFocusY) - _SkyPrisonScreenClearHalfHeight;
            return saturate(outsideClearBand / max(0.0001, _SkyPrisonScreenFocusFadeRange));
        }

        float GetBlurMask(float2 uv)
        {
            float depthMask = GetDepthBlurMask(uv);
            float screenMask = GetScreenYBlurMask(uv);
            float focusBandMask = GetScreenFocusBandBlurMask(uv);

            float blurMask = screenMask;
            if (_SkyPrisonMaskMode < 0.5)
                blurMask = depthMask;
            else if (_SkyPrisonMaskMode > 1.5 && _SkyPrisonMaskMode < 2.5)
                blurMask = max(depthMask, screenMask);
            else if (_SkyPrisonMaskMode > 2.5)
                blurMask = focusBandMask;

            return smoothstep(0.0, 1.0, blurMask) * saturate(_SkyPrisonIntensity);
        }

        // Stable 13-tap normalized Gaussian. This is used on already-downsampled buffers,
        // so it produces a much more camera-like softness than a full-res sparse smear.
        float4 Gaussian13(float2 uv, float2 dir)
        {
            float4 c = 0.0;
            c += SampleBlit(uv) * 0.1996756275;

            c += SampleBlit(uv + dir * 1.0) * 0.1762131228;
            c += SampleBlit(uv - dir * 1.0) * 0.1762131228;

            c += SampleBlit(uv + dir * 2.0) * 0.1209853623;
            c += SampleBlit(uv - dir * 2.0) * 0.1209853623;

            c += SampleBlit(uv + dir * 3.0) * 0.0647587978;
            c += SampleBlit(uv - dir * 3.0) * 0.0647587978;

            c += SampleBlit(uv + dir * 4.0) * 0.0269954833;
            c += SampleBlit(uv - dir * 4.0) * 0.0269954833;

            c += SampleBlit(uv + dir * 5.0) * 0.0087643044;
            c += SampleBlit(uv - dir * 5.0) * 0.0087643044;

            c += SampleBlit(uv + dir * 6.0) * 0.0022159632;
            c += SampleBlit(uv - dir * 6.0) * 0.0022159632;

            return c;
        }
        ENDHLSL

        Pass
        {
            Name "SkyPrisonDownsampleBox"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDownsample

            half4 FragDownsample(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float2 t = _BlitTexture_TexelSize.xy;

                // 4-tap box prefilter. This creates a real low-frequency blur buffer base
                // before the gaussian pass, avoiding the harsh full-res soft-filter look.
                float4 c = 0.0;
                c += SampleBlit(uv + t * float2(-0.5, -0.5));
                c += SampleBlit(uv + t * float2( 0.5, -0.5));
                c += SampleBlit(uv + t * float2(-0.5,  0.5));
                c += SampleBlit(uv + t * float2( 0.5,  0.5));
                return c * 0.25;
            }
            ENDHLSL
        }

        Pass
        {
            Name "SkyPrisonGaussianHorizontal"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragGaussianHorizontal

            half4 FragGaussianHorizontal(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float radius = max(0.0, _SkyPrisonMaxRadius * _SkyPrisonHalfBlurRadiusScale);
                float2 dir = float2(_BlitTexture_TexelSize.x * radius / 6.0, 0.0);
                float4 c = Gaussian13(uv, dir);
                c.a = SampleBlit(uv).a;
                return c;
            }
            ENDHLSL
        }

        Pass
        {
            Name "SkyPrisonGaussianVertical"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragGaussianVertical

            half4 FragGaussianVertical(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float radius = max(0.0, _SkyPrisonMaxRadius * _SkyPrisonHalfBlurRadiusScale);
                float2 dir = float2(0.0, _BlitTexture_TexelSize.y * radius / 6.0);
                float4 c = Gaussian13(uv, dir);
                c.a = SampleBlit(uv).a;
                return c;
            }
            ENDHLSL
        }

        Pass
        {
            Name "SkyPrisonQuarterGaussianHorizontal"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragQuarterGaussianHorizontal

            half4 FragQuarterGaussianHorizontal(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float radius = max(0.0, _SkyPrisonMaxRadius * _SkyPrisonQuarterBlurRadiusScale);
                float2 dir = float2(_BlitTexture_TexelSize.x * radius / 6.0, 0.0);
                float4 c = Gaussian13(uv, dir);
                c.a = SampleBlit(uv).a;
                return c;
            }
            ENDHLSL
        }

        Pass
        {
            Name "SkyPrisonQuarterGaussianVertical"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragQuarterGaussianVertical

            half4 FragQuarterGaussianVertical(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float radius = max(0.0, _SkyPrisonMaxRadius * _SkyPrisonQuarterBlurRadiusScale);
                float2 dir = float2(0.0, _BlitTexture_TexelSize.y * radius / 6.0);
                float4 c = Gaussian13(uv, dir);
                c.a = SampleBlit(uv).a;
                return c;
            }
            ENDHLSL
        }

        Pass
        {
            Name "SkyPrisonGaussianPyramidComposite"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragComposite

            half4 FragComposite(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float4 original = SampleBlit(uv);
                float m = GetBlurMask(uv);

                float4 halfBlur = SAMPLE_TEXTURE2D_X(_SkyPrisonBlurHalfTex, sampler_LinearClamp, uv);
                float4 quarterBlur = SAMPLE_TEXTURE2D_X(_SkyPrisonBlurQuarterTex, sampler_LinearClamp, uv);

                // Softly transition between half-res and quarter-res buffers. The heavy blur
                // is only used where the mask is high, preventing the "flat oily" look.
                float heavy = smoothstep(0.45, 1.0, m);
                float4 blur = lerp(halfBlur, quarterBlur, heavy);

                float4 result = lerp(original, blur, m);
                result.a = original.a;
                return result;
            }
            ENDHLSL
        }

        Pass
        {
            Name "SkyPrisonDebugMask"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDebugMask

            half4 FragDebugMask(Varyings input) : SV_Target
            {
                float m = GetBlurMask(input.texcoord);
                return float4(m, 0.0, 1.0 - m, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "SkyPrisonCopy"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragCopy

            half4 FragCopy(Varyings input) : SV_Target
            {
                return SampleBlit(input.texcoord);
            }
            ENDHLSL
        }
    }
}
