Shader "SkyPrison/OccluderFootprintDepth"
{
    // 遮挡物落地深度。
    //
    // 每个顶点输出的不是自己的原始眼深度，而是「自己正下方地面点」的眼深度——
    // 世界坐标 X/Z 保留，Y 清零投影到地面，再算这个投影点的眼深度。
    //
    // 为什么需要它：2.5D 的遮挡语义是「谁的脚在前面」，不是「谁的表面离相机近」。
    // 45 度俯视下，高的物体顶部会朝相机倾过来，叉车顶棚的几何深度可以比站在
    // 叉车前面的角色脚底还小。直接用 _CameraDepthTexture 比较，同一台叉车会
    // 出现「顶部判在前、底部判在后」，角色被从中间切开（实测表现为头绿身红）。
    // 先把 Y 清零再算深度，正好去掉「高度」这个干扰量，比的还是地面位置。
    //
    // 2026-08-19 之前的版本：整个物体共用一个常数值（物体根节点的落地深度）。
    // 对紧凑物体（箱子、叉车）成立，但对横跨很宽、中间镂空的门/桥/护栏类结构
    // （比如两根柱子架一根横梁的电线架）不成立——两根柱子实际落地深度差很远，
    // 玩家走到结构中点时全结构会同时整体翻转遮挡状态。改成逐顶点算之后，近柱、
    // 远柱、横梁各自对应自己正下方的地面位置，不再共用一个值，也不需要为宽
    // 结构额外配置锚点。
    //
    // 注意这张图只决定「挡不挡」，不决定「藏哪些像素」。角色露在遮挡物轮廓
    // 外面的像素采样到的是清空值（远平面），自然不会被藏，正常显示。
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "OccluderFootprintDepth"
            // 用 Min 混合而不是深度测试。
            //
            // 深度测试比的是「几何表面谁最近」，但这张图要的是「落地点谁最近」，
            // 两者在 45 度俯视下经常不一致（高物体的顶棚表面很近、落地点却很远），
            // 用 ZTest 会让顶棚那个片元胜出，写进去的却是它物体的落地深度，结果错乱。
            // BlendOp Min 直接对输出值取最小，语义上就是「这个像素上落地最靠前的遮挡物」，
            // 也顺带省掉了深度附件。
            ZWrite Off
            ZTest Always
            Cull Back
            BlendOp Min
            Blend One One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                // 每个顶点各自的落地深度，三角形内部由光栅化线性插值——同一个三角形
                // 横跨两侧地面深度差异较大的地方（比如门形结构的横梁）会平滑过渡，
                // 不会再出现整个物体只有一个值的硬边界。
                float  footprintEye : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);

                // 逐顶点算落地深度：世界坐标 X/Z 保留，Y 清零投影到地面，再算这个
                // 投影点的眼深度——不再是整个物体共用一个常数值。
                //
                // TransformObjectToWorld 在静态合批下依然正确：合批会把顶点烘进世界
                // 空间、把 unity_ObjectToWorld 退化成单位阵，此时该函数等价于原样
                // 返回已经是世界坐标的 positionOS。这跟"从矩阵平移列直接取物体根
                // 节点位置"是两回事——后者才是老版本真正会被合批坑到的操作（叉车
                // 因此写出了错误的深度，纹理里 0 个像素匹配），这里没有这个问题。
                float3 vertexWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 groundWS = float3(vertexWS.x, 0.0, vertexWS.z);

                float3 groundVS = mul(UNITY_MATRIX_V, float4(groundWS, 1.0)).xyz;
                output.footprintEye = -groundVS.z;

                return output;
            }

            float frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return input.footprintEye;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
