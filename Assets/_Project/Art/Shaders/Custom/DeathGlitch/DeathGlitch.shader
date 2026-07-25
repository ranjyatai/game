Shader "SkyPrison/PostProcess/DeathGlitch"
{
    Properties
    {
        _Intensity   ("Intensity",   Range(0,1))      = 0.0
        _Speed       ("Speed",       Range(0.1,8))    = 2.5
        _BlockSize   ("Block Size",  Range(0.02,0.4)) = 0.10
        _RGBShift    ("RGB Shift",   Range(0,0.02))   = 0.006
        _ScanlineStr ("Scanline",    Range(0,1))      = 0.06
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off

        Pass
        {
            Name "DeathGlitch"

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _DeathGlitchIntensity;
            float _DeathGlitchSpeed;
            float _DeathGlitchBlockSize;
            float _DeathGlitchRGBShift;
            float _DeathGlitchScanline;

            float Rand(float2 co)
            {
                return frac(sin(dot(co, float2(127.1, 311.7))) * 43758.5453);
            }

            float OrderedDither(float2 screenPos)
            {
                const float4x4 m = float4x4(
                     0, 8, 2,10,
                    12, 4,14, 6,
                     3,11, 1, 9,
                    15, 7,13, 5);
                int2 p = int2(fmod(screenPos, 4));
                return (m[p.x][p.y] + 1.0) / 17.0;
            }

            half3 Quantize(half3 rgb, float levels, float dither)
            {
                rgb *= levels;
                rgb = floor(rgb + dither);
                return rgb / levels;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float2 uv     = IN.texcoord;
                float  ins    = _DeathGlitchIntensity;
                float2 screen = uv * _ScreenParams.xy;

                half4 orig = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv);

                // ── 时钟 ──────────────────────────────────────────────────────
                float spd   = _DeathGlitchSpeed;
                float fastT = floor(_Time.y * spd * 1.2);
                float midT  = floor(_Time.y * spd * 0.5);
                float slowT = floor(_Time.y * spd * 0.08);

                // ── 连续性：三档强度叠加，取代单一 burst ──────────────────────
                // tier0: 极轻微，始终存在（>0 就生效）
                // tier1: 中等，约 50% 时间
                // tier2: 爆发，约 20% 时间
                float tier0 = ins * 0.08;
                float tier1 = step(0.50, Rand(float2(slowT,       3.1))) * ins * 0.45;
                float tier2 = step(0.80, Rand(float2(slowT * 0.5, 7.3))) * ins * 1.0;
                float activity = saturate(tier0 + tier1 + tier2); // 综合活跃度

                float blockSize = max(_DeathGlitchBlockSize, 0.02);

                // ── 多尺度行分组：大块 + 小块混合，避免高度一致 ───────────────
                float bigRow   = floor(uv.y / (blockSize * 2.5));
                float midRow   = floor(uv.y / blockSize);
                float smallRow = floor(uv.y / (blockSize * 0.35));

                // 按位置随机选择本行用哪种块尺度
                float scaleChoice = Rand(float2(bigRow, midT * 0.3));
                float row = scaleChoice < 0.4 ? bigRow
                          : scaleChoice < 0.75 ? midRow
                          : smallRow;

                // ── 横向位移 ───────────────────────────────────────────────────
                float rr    = Rand(float2(row, midT));
                float shift = 0.0;
                if (rr > (1.0 - ins * 0.5))
                    shift = (Rand(float2(row, midT + 2.3)) - 0.5) * ins * 0.12;
                shift *= activity;

                float2 uvS = float2(uv.x + shift, uv.y);

                bool outOfBounds = uvS.x < 0.0 || uvS.x > 1.0;

                half4 glitched;
                float dither = OrderedDither(screen);

                if (outOfBounds)
                {
                    // ── 超界噪声：彩色色块 + 量化，参考 shadertoy CRT 风格 ──────
                    float n1 = Rand(float2(uv.x * 173.3 + fastT * 0.37, row + slowT));
                    float n2 = Rand(float2(uv.y * 291.7 + slowT,        uv.x * 112.1 + midT));
                    float n3 = Rand(float2(row * 0.5   + midT * 1.3,    uv.y * 67.9));

                    // 行级别颜色偏向（让同一行内有主色调，视觉更整齐）
                    float rowColor = Rand(float2(row * 1.1, slowT + 1.7));
                    half3 noise;
                    if (rowColor < 0.28)
                        noise = Quantize(half3(n1, n2 * 0.25, n3 * 0.18), 5.0, dither); // 红
                    else if (rowColor < 0.56)
                        noise = Quantize(half3(n1 * 0.2, n2, n3 * 0.35), 5.0, dither);  // 绿
                    else if (rowColor < 0.78)
                        noise = Quantize(half3(n1 * 0.18, n2 * 0.3, n3), 5.0, dither);  // 蓝
                    else
                        noise = Quantize(half3(n1, n2 * 0.9, n3 * 0.15), 5.0, dither);  // 黄

                    // 叠加细粒度亮度闪烁
                    float sparkle = step(0.88, Rand(float2(uv.x * 41.3 + fastT, row)));
                    noise = saturate(noise + sparkle * 0.5);

                    glitched = half4(noise, 1.0);
                }
                else
                {
                    // ── 正常区域：色差 ──────────────────────────────────────────
                    float2 toCenter = uv - 0.5;
                    float  dist     = length(toCenter);
                    float2 aberDir  = dist > 0.001 ? normalize(toCenter) : float2(1, 0);
                    // 色差强度随 activity 变化（tier2 时最强）
                    float aberAmt   = _DeathGlitchRGBShift * ins * dist * activity;

                    half r = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp,
                                 uvS + aberDir * aberAmt).r;
                    half g = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uvS).g;
                    half b = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp,
                                 uvS - aberDir * aberAmt * 0.7).b;
                    glitched = half4(r, g, b, 1.0);

                    if (abs(shift) > 0.001)
                        glitched.rgb = Quantize(glitched.rgb, 8.0, dither * 0.5);

                    // 局部反色（tier2 才出现）
                    float invBlock = step(1.0 - ins * 0.08, Rand(float2(row * 0.6, slowT)))
                                   * tier2;
                    glitched.rgb = lerp(glitched.rgb, 1.0 - glitched.rgb, invBlock * 0.75);
                }

                // ── 扫描线 ─────────────────────────────────────────────────────
                float scan     = sin(uv.y * _ScreenParams.y * 3.14159) * 0.5 + 0.5;
                float scanDark = _DeathGlitchScanline * ins * (1.0 - scan) * 0.4;

                // ── 合成 ──────────────────────────────────────────────────────
                // 超界时 mixFactor 跟 activity 走（始终有点东西）
                // 正常区域 tier0 贡献位移但混合很轻
                float mixFactor = outOfBounds
                    ? saturate(activity * 0.9)
                    : saturate(activity * 0.85);

                half4 col = lerp(orig, glitched, mixFactor);
                col.rgb *= 1.0 - scanDark;

                return col;
            }
            ENDHLSL
        }
    }
}
