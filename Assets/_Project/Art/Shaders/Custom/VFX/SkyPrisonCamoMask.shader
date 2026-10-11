Shader "Hidden/SP/CamoMask"
{
    // 光学迷彩的屏幕空间遮罩(SkyPrisonCamoMaskRenderer 每帧在主相机渲染前画)：
    //   Pass 0：把迷彩单位的 Spine 网格按图集 alpha 画成白色剪影，乘上权重(移动程度 × 未现形部分)，多个单位取最大值；
    //   Pass 1：可分离高斯模糊(_BlurDir = 方向 × 步长，单位 UV)，把剪影糊成边缘柔软、平滑淡出的"影响范围"——
    //           这样后面的歪曲没有部件接缝、也没有轮廓断层(用户 10-12：要液化/歪曲工具那种感觉，不要描边)。
    Properties { _MainTex ("", 2D) = "white" {} }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off

        Pass
        {
            Name "Silhouette"
            BlendOp Max
            Blend One One
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_CamoAtlas); SAMPLER(sampler_CamoAtlas);
            float _CamoVertexAlphaScale, _CamoWeight;
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float a : TEXCOORD1; };
            V vert(A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.uv = i.uv; o.a = saturate(i.color.a * _CamoVertexAlphaScale); return o; }
            float4 frag(V i) : SV_Target
            {
                float a = SAMPLE_TEXTURE2D(_CamoAtlas, sampler_CamoAtlas, i.uv).a * i.a * _CamoWeight;
                return a.xxxx;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Blur"
            Blend Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _BlurDir;
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.uv = i.uv; return o; }
            float4 frag(V i) : SV_Target
            {
                const float w[5] = { 0.227027, 0.1945946, 0.1216216, 0.054054, 0.016216 };
                float s = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).r * w[0];
                [unroll] for (int k = 1; k < 5; k++)
                {
                    s += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + _BlurDir.xy * k).r * w[k];
                    s += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv - _BlurDir.xy * k).r * w[k];
                }
                return s.xxxx;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
