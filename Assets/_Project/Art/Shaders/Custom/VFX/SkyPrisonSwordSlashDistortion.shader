Shader "Hidden/SP/SwordSlashDistortion"
{
    // 剑挥砍轨迹的空气扭曲——采样 _CameraOpaqueTexture（URP不透明纹理），沿着
    // TrailRenderer 横截方向偏移UV，制造"背后的画面被划开一道"的折射感，再叠加一层
    // 沿轨迹中心最亮的高光条，让划痕本身也有一点视觉存在感（纯扭曲在静止背景前
    // 很容易完全看不出来）。
    //
    // TrailRenderer 顶点UV约定：uv.x = 沿轨迹长度方向 0(尾)→1(头)，
    // uv.y = 横截方向 0→1（中心=0.5）。顶点色alpha由 TrailRenderer 自己的
    // Color Gradient 驱动（头部不透明、尾部淡出），这里直接乘进去，不用在shader里
    // 重新算生命周期淡出。

    Properties
    {
        _DistortionStrength ("扭曲强度", Range(0, 0.08)) = 0.03
        _EdgeSoftness       ("边缘柔和度", Range(0.05, 1)) = 0.5
        _OpacityCap         ("整体不透明度上限", Range(0, 1)) = 0.55
        _GlintColor         ("边缘高光颜色", Color) = (1, 1, 1, 1)
        _GlintIntensity     ("边缘高光强度", Range(0, 2)) = 0.25
    }

    SubShader
    {
        Tags
        {
            "Queue"          = "Transparent"
            "RenderType"     = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector"= "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "SwordSlashDistortion"
            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float  _DistortionStrength;
                float  _EdgeSoftness;
                float  _OpacityCap;
                float4 _GlintColor;
                float  _GlintIntensity;
            CBUFFER_END

            struct Attributes
            {
                float4 posOS : POSITION;
                float2 uv    : TEXCOORD0;
                float4 color : COLOR; // TrailRenderer Color Gradient（生命周期淡出）
            };

            struct Varyings
            {
                float4 posHCS    : SV_POSITION;
                float2 uv        : TEXCOORD0;
                float4 color     : COLOR;
                float4 screenPos : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.posHCS    = TransformObjectToHClip(IN.posOS.xyz);
                OUT.uv        = IN.uv;
                OUT.color     = IN.color;
                OUT.screenPos = ComputeScreenPos(OUT.posHCS);
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                // 横截方向落到中心最强、两侧柔和过渡到0——避免整条拖尾是一个硬边矩形。
                float widthFalloff = 1.0 - saturate(abs(IN.uv.y - 0.5) * 2.0);
                widthFalloff = smoothstep(0.0, _EdgeSoftness, widthFalloff);

                float strength = widthFalloff * IN.color.a;

                float2 screenUV = IN.screenPos.xy / max(IN.screenPos.w, 0.0001);

                // 沿横截方向偏移采样点——中心两侧往反方向拉，读起来像"画面被划开"，
                // 强度随 strength 衰减，轨迹尾部/边缘自然收平，不会露出生硬的采样边界。
                float2 offsetDir  = float2(IN.uv.y - 0.5, 0.0);
                float2 distortedUV = screenUV + offsetDir * _DistortionStrength * strength;

                float3 sceneColor = SampleSceneColor(distortedUV);
                // 高光只是给形状一点边缘可读性用，不能盖过折射本身——强度调低、
                // 整体alpha额外乘一层上限，让背后画面被拉扯的效果能透出来，
                // 不会整条拖尾糊成一片白。
                float3 glint = _GlintColor.rgb * _GlintIntensity * strength;

                return float4(sceneColor + glint, strength * _OpacityCap);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
