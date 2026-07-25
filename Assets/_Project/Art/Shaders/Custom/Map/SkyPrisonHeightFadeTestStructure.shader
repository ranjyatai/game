Shader "SkyPrison/HeightFadeTestStructure"
{
    // 高层建筑"可显示高度"淡出——测试用简单Lit Shader，只用来验证淡出效果本身，
    // 不是最终建筑要用的正式Shader（真正的建筑材质到位后，把 SkyPrisonHeightFade.hlsl
    // 里的 GetHeightFadeAlpha() 那几行搬进正式Shader/Shader Graph Custom Function
    // 节点即可，见该文件顶部注释）。
    //
    // _HeightFadeBaseY 由 SkyPrisonHeightFadeController.cs 按这个物体 Renderer Bounds
    // 的最低点算一次，通过 MaterialPropertyBlock 传进来，不是这里配置的固定值。

    Properties
    {
        _MainTex  ("主贴图", 2D) = "white" {}
        _BaseColor ("基础颜色", Color) = (0.6, 0.6, 0.65, 1)

        _HeightFadeThreshold ("可显示高度（从建筑自己底部往上算，米）", Float) = 10
        _HeightFadeDistance  ("淡出距离（米）", Range(0.1, 10)) = 2
    }

    SubShader
    {
        Tags
        {
            "Queue"          = "Geometry"
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Back

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/_Project/Art/Shaders/Custom/Map/SkyPrisonHeightFade.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _BaseColor;
                float  _HeightFadeThreshold;
                float  _HeightFadeDistance;
                // 每个实例各自的地面高度——由 SkyPrisonHeightFadeController 用
                // MaterialPropertyBlock 传，不是全局共享同一个值。
                float  _HeightFadeBaseY;
            CBUFFER_END

            struct Attributes
            {
                float4 posOS   : POSITION;
                float3 normalOS: NORMAL;
                float2 uv      : TEXCOORD0;
            };

            struct Varyings
            {
                float4 posHCS   : SV_POSITION;
                float2 uv       : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 posWS    : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.posOS.xyz);
                OUT.posHCS   = posInputs.positionCS;
                OUT.posWS    = posInputs.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv       = TRANSFORM_TEX(IN.uv, _MainTex);
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);

                Light mainLight = GetMainLight();
                float ndotl = saturate(dot(normalize(IN.normalWS), mainLight.direction));
                float3 lit = tex.rgb * _BaseColor.rgb * (ndotl * mainLight.color + unity_AmbientSky.rgb);

                // 抖动裁切代替Alpha混合——材质保持Opaque，不会碰贴图自身alpha通道。
                ClipHeightFade(IN.posHCS, IN.posWS.y, _HeightFadeBaseY, _HeightFadeThreshold, _HeightFadeDistance);

                return float4(lit, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
