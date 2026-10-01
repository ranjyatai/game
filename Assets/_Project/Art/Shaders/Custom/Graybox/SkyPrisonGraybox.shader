// 白模（Graybox）着色器：白色底 + 世界空间网格。
//
// 网格按世界坐标投影（取法线主轴的那个平面），不读模型 UV——白模在摆放工具里
// 会被随意缩放，UV 网格会跟着被拉伸，失去「一格 = 固定米数」的意义。这里一格
// 永远是 _GridSize 米，每 _MajorEvery 格一条粗线，任何缩放下都能直接数尺寸。
//
// 光照走 URP 的 UniversalFragmentBlinnPhong：主光阴影、附加灯光、Forward+ 的
// 光照循环、雾都和场景里其它物体一致，白模放进去看到的明暗就是真实的光照关系。
// ShadowCaster / DepthOnly / DepthNormals 直接复用 URP 自带 pass，所以 CBUFFER
// 里保留了它们引用的 _BaseMap_ST / _BaseColor / _Cutoff。
Shader "SkyPrison/Graybox"
{
    Properties
    {
        _BaseColor ("底色", Color) = (0.86, 0.86, 0.86, 1)
        _MinorLineColor ("细线颜色", Color) = (0.62, 0.62, 0.62, 1)
        _MajorLineColor ("粗线颜色", Color) = (0.42, 0.42, 0.42, 1)
        _GridSize ("每格米数", Float) = 1
        _MajorEvery ("每几格一条粗线", Float) = 5
        _MinorLineWidth ("细线宽度(像素)", Range(0.5, 4)) = 1
        _MajorLineWidth ("粗线宽度(像素)", Range(0.5, 6)) = 2
        _Smoothness ("光滑度", Range(0, 1)) = 0.1

        // URP 共用 pass 需要的字段，白模本身不用。
        [HideInInspector] _BaseMap ("BaseMap", 2D) = "white" {}
        [HideInInspector] _Cutoff ("Cutoff", Float) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "UniversalMaterialType" = "SimpleLit"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _MinorLineColor;
            half4 _MajorLineColor;
            float _GridSize;
            float _MajorEvery;
            float _MinorLineWidth;
            float _MajorLineWidth;
            half _Smoothness;
            half _Cutoff;
        CBUFFER_END

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                half   fogFactor  : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return OUT;
            }

            // 到最近网格线的距离（以屏幕像素计），fwidth 保证任何缩放/距离下线宽稳定、不闪。
            float GridLineMask(float2 p, float cell, float widthPx)
            {
                float2 g = p / max(cell, 1e-4);
                float2 fw = max(fwidth(g), 1e-5);
                float2 distPx = abs(frac(g - 0.5) - 0.5) / fw;
                float d = min(distPx.x, distPx.y);
                return 1.0 - saturate(d - widthPx * 0.5 + 0.5);
            }

            half3 GridAlbedo(float3 positionWS, float3 normalWS)
            {
                // 按法线主轴选投影平面：顶/底面用 XZ，侧面用 ZY 或 XY。
                float3 n = abs(normalWS);
                float2 p = (n.y >= n.x && n.y >= n.z) ? positionWS.xz
                         : (n.x >= n.z)               ? positionWS.zy
                                                      : positionWS.xy;

                float minor = GridLineMask(p, _GridSize, _MinorLineWidth);
                float major = GridLineMask(p, _GridSize * max(_MajorEvery, 1.0), _MajorLineWidth);

                half3 c = _BaseColor.rgb;
                c = lerp(c, _MinorLineColor.rgb, minor);
                c = lerp(c, _MajorLineColor.rgb, major);
                return c;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float3 normalWS = normalize(IN.normalWS);

                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.positionCS = IN.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                inputData.fogCoord = IN.fogFactor;
                inputData.vertexLighting = half3(0, 0, 0);
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = GridAlbedo(IN.positionWS, normalWS);
                surface.alpha = 1;
                surface.specular = half3(0.04, 0.04, 0.04);
                surface.smoothness = _Smoothness;
                surface.occlusion = 1;

                half4 color = UniversalFragmentBlinnPhong(inputData, surface);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1;
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"
            ENDHLSL
        }
    }

    Fallback Off
}
