// 状态描边（灼烧等）的专属外发光。由 UnitStatusOutlineGlowFeature 驱动：
//   pass 0  降采样：单位专属轮廓蒙版（全分辨率）→ 1/4 分辨率
//   pass 1  横向高斯模糊
//   pass 2  纵向高斯模糊
//   pass 3  合成：模糊结果 × (1 - 原蒙版) × 噪波起伏 × 辉光颜色，加法叠到相机颜色
//
// 和 Volume 的全局 Bloom 完全无关：只处理这一个单位自己的轮廓，所以把辉光调大不会让
// 场景里其它亮的东西（灯光照亮的地面、枪口火光）一起发光。
// 扣掉 (1 - 原蒙版) 是为了只在身体外面发光，角色本体不被染成一片红。
Shader "Hidden/SkyPrison/StatusOutlineGlow"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

    float4 _GlowColor;          // rgb = 辉光外侧颜色，偏红（已含强度和淡入淡出）
    float4 _GlowColorInner;     // rgb = 辉光内侧颜色，偏黄白（同上）
    float  _GlowStep;           // 模糊采样步长（1/4 分辨率下的纹素数）
    float4 _GlowTexel;          // xy = 1/4 分辨率纹素大小
    TEXTURE2D(_GlowPresence);   // 全分辨率原蒙版，合成时扣掉身体内部
    SAMPLER(sampler_GlowPresence);
    TEXTURE2D(_GlowNoiseTex);   // 强弱起伏用的噪波（和描边流动共用那张溶解噪波）

    // 被遮挡像素：HologramCoverageFeature 每帧画的计数图（>0 = 这个像素上有被挡住的角色部件）。
    // 降采样时把这些像素从轮廓里扣掉，辉光只从没被挡住的身体部分往外发——躲在墙后的那半身
    // 不发光，辉光也就不会画到墙上。和 Spine 遮挡着色器里硬描边的遮挡同一个判定来源。
    TEXTURE2D(_SP_HoloCoverage);
    float _SP_HoloCoverageActive;
    float4 _GlowCenter;         // xy = 单位中心的屏幕 UV，z = 宽高比
    float4 _GlowNoise;          // x = 噪波密度（每屏幕高度几格），y = 流动速度，z = 起伏幅度 0~1

    // 9 tap 高斯（σ≈2）。两轮横纵之后足够平滑，不会出现条纹。
    static const float kWeights[5] = { 0.2270270270, 0.1945945946, 0.1216216216, 0.0540540541, 0.0162162162 };

    float SampleBlit(float2 uv)
    {
        return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).r;
    }

    float SampleVisiblePresence(float2 uv)
    {
        float p = SampleBlit(uv);
        if (_SP_HoloCoverageActive > 0.5)
            p *= 1.0 - saturate(SAMPLE_TEXTURE2D_LOD(_SP_HoloCoverage, sampler_PointClamp, uv, 0).r);
        return p;
    }

    float Blur(float2 uv, float2 dir)
    {
        float2 stepUV = dir * _GlowTexel.xy * _GlowStep;
        float sum = SampleBlit(uv) * kWeights[0];
        [unroll] for (int i = 1; i < 5; i++)
        {
            sum += SampleBlit(uv + stepUV * i) * kWeights[i];
            sum += SampleBlit(uv - stepUV * i) * kWeights[i];
        }
        return sum;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off

        Pass
        {
            Name "Downsample"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target
            {
                // 源是全分辨率：四个双线性采样合起来覆盖 4x4 纹素，降到 1/4 不闪。
                float2 t = _BlitTexture_TexelSize.xy;
                float v = SampleVisiblePresence(i.texcoord + t * float2(-1, -1))
                        + SampleVisiblePresence(i.texcoord + t * float2( 1, -1))
                        + SampleVisiblePresence(i.texcoord + t * float2(-1,  1))
                        + SampleVisiblePresence(i.texcoord + t * float2( 1,  1));
                return half4(v * 0.25, 0, 0, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "BlurH"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target { return half4(Blur(i.texcoord, float2(1, 0)), 0, 0, 1); }
            ENDHLSL
        }

        Pass
        {
            Name "BlurV"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target { return half4(Blur(i.texcoord, float2(0, 1)), 0, 0, 1); }
            ENDHLSL
        }

        Pass
        {
            Name "Composite"
            Blend One One
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target
            {
                float glow = SampleBlit(i.texcoord);
                float inside = SAMPLE_TEXTURE2D_LOD(_GlowPresence, sampler_GlowPresence, i.texcoord, 0).r;
                // 模糊后的值在轮廓边缘约 0.5、往外衰减；×2 让紧贴轮廓处接近满强度。
                float falloff = saturate(glow * 2.0);
                float outer = falloff * (1.0 - saturate(inside));

                // 强弱起伏：以单位中心为原点的屏幕空间噪波（跟着单位走，不会在角色身上"滑"），
                // 两个八度叠加、缓慢向上流动。弱处压到 1-幅度，强处抬到 1+幅度，
                // 做出「有的地方亮、有的地方几乎没有」的火光感，而不是一圈等宽等亮的光环。
                float2 np = (i.texcoord - _GlowCenter.xy) * float2(_GlowCenter.z, 1.0) * _GlowNoise.x;
                float flow = _Time.y * _GlowNoise.y;
                float n1 = SAMPLE_TEXTURE2D_LOD(_GlowNoiseTex, sampler_LinearRepeat, np + float2(0.0, -flow), 0).r;
                float n2 = SAMPLE_TEXTURE2D_LOD(_GlowNoiseTex, sampler_LinearRepeat, np * 2.3 + float2(0.37, -flow * 1.7), 0).r;
                float n = smoothstep(0.2, 0.8, n1 * 0.65 + n2 * 0.35);
                outer *= lerp(1.0 - _GlowNoise.z, 1.0 + _GlowNoise.z, n);

                // 内黄白外红：falloff 贴着轮廓接近 1、往外衰减到 0，直接拿它当温度——
                // 贴边处用内侧色，越往外越偏外侧色。平方让黄白只占靠里的一窄圈，大部分散光是红的。
                float heat = falloff * falloff;
                float3 col = lerp(_GlowColor.rgb, _GlowColorInner.rgb, heat);
                return half4(col * outer, 0);
            }
            ENDHLSL
        }
    }
}
