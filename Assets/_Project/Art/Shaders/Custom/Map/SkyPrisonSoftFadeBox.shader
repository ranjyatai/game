Shader "SkyPrison/SoftFadeBox"
{
    Properties
    {
        _Color ("Color", Color) = (0, 0, 0, 1)
        _FadeWidthWorld ("Fade Width (世界空间米数，不随缩放变化)", Range(0.01, 20)) = 0.6
        _Intensity ("Intensity", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            // 逐材质属性必须包在 UnityPerMaterial CBUFFER 里，这是 URP SRP Batcher的
            // 硬性要求——不包的话，这些值在开了SRP Batcher的项目里不会按材质正确
            // 绑定，GPU实际读到的可能是未初始化的全局默认值(0)而不是材质文件里存的
            // 真实值。上一版就是漏了这个：材质文件里_Intensity确实是1，但渲染时
            // 读到的很可能是0，col.a *= fade * 0 恒为0，表现就是"完全透明"——
            // 常数alpha的排查版之所以能看见，正是因为它完全没读这两个属性。
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _FadeWidthWorld;
                float _Intensity;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                return output;
            }

            // 单轴衰减，坐标范围是内置Cube网格原始的 -0.5..0.5。
            // |p|在(0.5 - fadeWidth*0.5)之前保持1（不透明区），
            // 之后smoothstep平滑降到0（贴着面的边缘完全透明）。
            float AxisFade(float p, float fadeWidth)
            {
                float solidEdge = 0.5 - saturate(fadeWidth) * 0.5;
                return 1.0 - smoothstep(solidEdge, 0.5, abs(p));
            }

            // 物体局部空间内，射线(ro,rd)跟 [-0.5,0.5]^3 盒子求交（slab method）。
            // 射线没穿过盒子时 tmin > tmax。
            void RayBoxIntersect(float3 ro, float3 rd, out float tmin, out float tmax)
            {
                float3 invD = 1.0 / rd;
                float3 t0 = (-0.5 - ro) * invD;
                float3 t1 = ( 0.5 - ro) * invD;
                float3 tSmall = min(t0, t1);
                float3 tBig   = max(t0, t1);
                tmin = max(max(tSmall.x, tSmall.y), tSmall.z);
                tmax = min(min(tBig.x, tBig.y), tBig.z);
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 关键修正：立方体网格只有表面、没有内部，表面上每个像素在自己所在的
                // 那个面的法线方向上恒等于边界值——用"这个像素本身离中心多远"来判断
                // 不透明度，算出来处处都是"恰好在边界上"，六个面无一例外，整个表面
                // 淡成了透明。
                //
                // 改成从相机往这个像素方向反推一条射线，求它穿过盒子局部空间的入射/
                // 出射点，取中点判断离中心多近——这样表面上的像素代表的是"这条视线
                // 穿过了多厚的雾"，视线正对盒子中轴时穿得深、离中心近，边缘掠射时
                // 穿得浅、离中心远，才有"中心实、边缘虚"的视觉。
                float3 rayOriginWS = _WorldSpaceCameraPos;
                float3 rayDirWS = normalize(input.positionWS - rayOriginWS);

                float3 rayOriginOS = TransformWorldToObject(rayOriginWS);
                float3 rayDirOS = normalize(mul((float3x3)unity_WorldToObject, rayDirWS));

                float tmin, tmax;
                RayBoxIntersect(rayOriginOS, rayDirOS, tmin, tmax);
                tmin = max(tmin, 0.0); // 相机在盒子内部时，入射点钳到相机自己

                float3 midOS = rayOriginOS + rayDirOS * (tmin + tmax) * 0.5;

                // 之前 _FadeWidth 是"半边长的比例"，箱子拉伸放大后同样的比例对应的
                // 绝对宽度也跟着放大——越拉越散。改成世界空间固定米数：先从
                // unity_ObjectToWorld 矩阵的三根列向量长度取出物体在世界空间里
                // 每根轴的实际缩放（世界单位/局部单位），把"固定的世界米数"换算成
                // 每根轴各自的局部空间比例——箱子越大，同样的世界米数占的局部比例
                // 越小，边缘淡出的绝对宽度就不会跟着放大。
                float3 worldScale = float3(
                    length(unity_ObjectToWorld._m00_m10_m20),
                    length(unity_ObjectToWorld._m01_m11_m21),
                    length(unity_ObjectToWorld._m02_m12_m22));
                float3 localFadeWidth = saturate(_FadeWidthWorld / max(worldScale, 0.0001));

                float fade = AxisFade(midOS.x, localFadeWidth.x)
                           * AxisFade(midOS.y, localFadeWidth.y)
                           * AxisFade(midOS.z, localFadeWidth.z);

                half4 col = _Color;
                col.a *= fade * _Intensity;
                return col;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
