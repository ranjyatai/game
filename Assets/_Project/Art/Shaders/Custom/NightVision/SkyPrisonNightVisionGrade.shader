// 夜视仪调色：只作用在"世界画面"上（Main Camera 画完之后、Overlay 相机开始画之前），
// 所以后面才画的头顶 UI（名字、血条、任务图标）和战术识别框不会被染色，保留原色。
//
// 真实夜视仪靠自动增益把对比度拉开，弱对比的细节被放大、暗部不是死黑：
//   曝光 -> 以中灰为轴的幂函数对比度 -> 中间调抬亮 -> 单色黄绿 -> 高光柔和压缩 -> 暗角 -> 胶片噪点
// 暗角和噪点放在这里而不是 Volume 里，是因为 Volume 的后处理在最后统一跑，会连头顶 UI 一起盖住。
Shader "Hidden/SkyPrison/NightVisionGrade"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        // Pass 0：调色
        Pass
        {
            Name "NightVisionGrade"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _NVWeight;
            float _NVExposure;     // EV
            float _NVContrast;     // 幂指数，1 = 不变
            float _NVPivot;        // 对比度的轴（亮度），高于它的更亮、低于它的更暗
            float _NVGammaLift;    // 0~1，中间调抬亮
            float _NVMono;         // 0~1，单色化程度
            float4 _NVTint;        // 单色的 R/G/B 输出增益
            float _NVVignette;     // 0~1
            float _NVGrain;        // 0~1
            float _NVTime;

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float3 Shoulder(float3 c)
            {
                // 0.8 以下原样，之上柔和压缩到 1，避免亮部直接削平成一片。
                float3 over = max(c - 0.8, 0.0);
                float3 comp = 0.8 + 0.2 * (1.0 - exp(-over / 0.2));
                return lerp(c, comp, step(0.8, c));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float4 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                float3 g = src.rgb * exp2(_NVExposure);
                g = _NVPivot * pow(max(g / _NVPivot, 1e-4), _NVContrast);
                g = pow(max(g, 0.0), 1.0 - _NVGammaLift);

                float lum = dot(g, float3(0.30, 0.59, 0.11));
                g = lerp(g, lum * _NVTint.rgb, _NVMono);
                g = Shoulder(g);

                float2 d = uv - 0.5;
                d.x *= _ScreenParams.x / _ScreenParams.y;
                float vig = smoothstep(0.95, 0.30, length(d));
                g *= lerp(1.0, vig, _NVVignette);

                float n = Hash21(uv * _ScreenParams.xy + _NVTime) - 0.5;
                g *= 1.0 + n * _NVGrain * 0.35;

                return half4(lerp(src.rgb, g, _NVWeight), src.a);
            }
            ENDHLSL
        }

        // Pass 1：原样拷贝回去
        Pass
        {
            Name "NightVisionCopy"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
            }
            ENDHLSL
        }
    }
}
