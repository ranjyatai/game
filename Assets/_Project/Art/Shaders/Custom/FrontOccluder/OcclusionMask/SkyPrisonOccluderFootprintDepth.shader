Shader "SkyPrison/OccluderFootprintDepth"
{
    // 遮挡物落地深度。
    //
    // 每个片元输出的不是自己表面的眼深度，而是「这个物体根节点」的眼深度——
    // 整个物体在这张图里是一个平坦的常数值。
    //
    // 为什么需要它：2.5D 的遮挡语义是「谁的脚在前面」，不是「谁的表面离相机近」。
    // 45 度俯视下，高的物体顶部会朝相机倾过来，叉车顶棚的几何深度可以比站在
    // 叉车前面的角色脚底还小。直接用 _CameraDepthTexture 比较，同一台叉车会
    // 出现「顶部判在前、底部判在后」，角色被从中间切开（实测表现为头绿身红）。
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

            // 由 CPU 在每个 DrawRenderer 之前用 SetGlobalVector 写入。
            // 命令缓冲是顺序执行的，所以「设值 → 画这一个」是可靠的配对。
            float4 _SkyPrison_FootprintRootWS;

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                // 逐顶点算好即可——整个物体是同一个值，插值不会改变它。
                float  footprintEye : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);

                // 落地坐标由 CPU 在每次 DrawRenderer 之前显式写进来，
                // 不从 UNITY_MATRIX_M 取。
                //
                // 原因：静态合批会把顶点烘进世界空间、并把 unity_ObjectToWorld 退化成
                // 单位阵，那时 _m03/_m13/_m23 得到的是世界原点而不是物体位置。
                // 实测就栽在这：草（未合批）写入正常，叉车（合批）写进去的值不是它的
                // 落地深度，纹理里叉车深度的像素数为 0，表现为叉车完全不遮挡。
                // 显式传值对合批、GPU Instancing、SRP Batcher 都成立。
                float3 rootVS = mul(UNITY_MATRIX_V, float4(_SkyPrison_FootprintRootWS.xyz, 1.0)).xyz;
                output.footprintEye = -rootVS.z;

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
