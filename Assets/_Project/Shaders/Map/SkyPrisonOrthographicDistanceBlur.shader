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

        /// 深度缓冲值 → 「离相机多远」。正交和透视的换算完全不同，必须分开。
        ///
        /// 原来直接用 LinearEyeDepth(rawDepth, _ZBufferParams) —— 那是透视投影的公式。
        /// 本项目相机是正交的（这个 Feature 名字里就写着 Orthographic），透视公式在正交下
        /// 算出来的是无意义的值，遮罩近乎常数，表现就是「怎么调参数都没有景深」。
        ///
        /// 正交下深度在 near..far 之间是线性的，直接按 d01 插值即可。
        /// unity_OrthoParams.w 正交时为 1、透视时为 0，两条路径都保留，切相机也不会坏。
        float SkyPrisonSceneEyeDepth(float rawDepth)
        {
        #if defined(UNITY_REVERSED_Z)
            float d01 = 1.0 - rawDepth;
        #else
            float d01 = rawDepth;
        #endif
            float ortho = lerp(_ProjectionParams.y, _ProjectionParams.z, d01);
            float persp = LinearEyeDepth(rawDepth, _ZBufferParams);
            return lerp(persp, ortho, unity_OrthoParams.w);
        }

        /// 只虚后景，不虚前景 —— 比对焦面近的一律保持清晰。
        /// 这是按参考画面定的：那种观感里近处几乎没有虚化，虚的是远景。
        float GetDepthBlurMask(float2 uv)
        {
            float rawDepth = SampleSceneDepth(uv);
            float eyeDepth = SkyPrisonSceneEyeDepth(rawDepth);
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

                // 三段连续过渡：清晰 → 半分辨率模糊 → 四分之一分辨率模糊。
                //
                // 原来是 lerp(original, blur, m)，即「清晰图和模糊图按比例混合」。
                // 混合不等于模糊：清晰图始终有一份权重叠在上面，出来的是发灰、带重影的
                // 「蒙了一层」的观感，而不是散焦。m 又被 intensity 卡在 0.449，
                // 意味着最远处仍保留 55% 的清晰图，所以怎么调都不像真正的景深。
                //
                // 现在改成沿模糊程度串联：m 从 0 到 0.5 是「清晰 → 半分辨率」，
                // 0.5 到 1 是「半分辨率 → 四分之一分辨率」。任意时刻都只在相邻两级之间
                // 插值，清晰图在 m 超过 0.5 后完全退出，远处是真的被模糊图取代。
                float4 result = m < 0.5
                    ? lerp(original, halfBlur, saturate(m * 2.0))
                    : lerp(halfBlur, quarterBlur, saturate(m * 2.0 - 1.0));

                // 抖动，打散 8-bit 量化台阶。
                //
                // 中间纹理（半/四分之一分辨率）直接继承屏幕颜色格式，通常是 8-bit。
                // 模糊本身会抹掉正常画面里遮盖量化台阶的高频细节/噪点，于是原本被盖住的
                // 色阶台阶在低对比度渐变区域（阴天天空、雾气墙面这类大面积同色渐变）
                // 会暴露成一圈圈色带——用户反馈的"颗粒感、网格感"就是这个，不是采样网格
                // 或核函数错了（核函数是标准 13-tap 高斯，权重和为 1，没问题）。
                //
                // 加一点点抖动噪声，量级远小于一个量化台阶（1/255 ≈ 0.004），
                // 视觉上不可见，但能让台阶边界随机化，肉眼看起来重新变得连续。
                float dither = (InterleavedGradientNoise(input.positionCS.xy, 0) - 0.5) * (1.0 / 255.0);
                result.rgb += dither * m;

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
