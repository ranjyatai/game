// 灯光投影（角色轮廓·深度贴花版）：影子能落在屏幕上看到的任何表面（地面、斜坡、台阶、墙、箱子）。
// 由 SkyPrisonCharacterEnvironmentLightReceiver 每帧驱动，旧的 DrawMesh 版（LampSilhouetteShadow）保留作退路。
//
// 两步：
//  1. Mask Pass：把角色网格「扶正」后（和旧版同一套：以根节点为原点，按网格自己的上/右轴拆出
//     离脚高度 h 和左右偏移 u）画进一张小 RT，横轴 = u，纵轴 = h。部件重叠处用 BlendOp Max 取并集，
//     所以旧版那套「部件重叠去重」计数图不再需要——RT 里每个像素只有一个 alpha。
//  2. Decal Pass：画一个盒体，盒体内每个像素读相机深度还原世界坐标 P，沿投影方向 Dv 反推到角色那张
//     竖直纸片所在的平面，得到 (u, h)，去 Mask RT 里查 alpha。
//
// 投影方向 Dv = (背离灯的水平单位方向 * K, -1)：高度 h 的点落到地面的水平位移 = K*h。
// 这和旧版「平行倾斜（剪切）」是同一个投影，所以「整个轮廓只被拉长、不会局部放大」的特点原样保留，
// 不会出现真实点光源阴影那种离灯近的头被放大的反透视。区别只有一个：落点由深度图决定，而不是一张水平面。
//
// 渲染规则：Transparent-80（永远在角色下面）、ZTest Always、乘法混合。
// 和旧版的区别：不再做模板测试（见 Decal Pass 里的说明），前景遮挡关系由深度图保证。
Shader "Hidden/SkyPrison/LampShadowDepthDecal"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
        _ShadowColor ("Shadow Color", Color) = (0.01, 0.01, 0.08, 1)
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-80" "RenderType"="Transparent" "IgnoreProjector"="True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        TEXTURE2D(_SkyPrison_LampMaskTex);

        CBUFFER_START(UnityPerMaterial)
            half4 _ShadowColor;
        CBUFFER_END

        // 每帧由脚本通过 MaterialPropertyBlock 设置。
        float4 _SkyPrison_LampDecalFoot;   // xyz = 脚底地面处的世界坐标（根节点 XZ + 地面高度）, w = 轮廓贴图覆盖的角色高度 H
        float4 _SkyPrison_LampDecalRight;  // x,y = 角色纸片「右」方向（水平单位向量的 XZ）, z = 半宽, w = 脚底贴地偏移（仅贴花用）
        float4 _SkyPrison_LampDecalDv;     // xyz = 投影方向, w = 整体强度 0~1
        float4 _SkyPrison_LampDecalClip;   // xy = 影子水平方向（单位向量 XZ）, z = 从脚沿该方向算起，墙挡住灯光的位置（无墙 = 很大）

        // 投影灯：影子逐像素按「这盏灯把这块表面照多亮」压暗，和旧版同一套 URP 衰减。
        float4 _SkyPrison_LampShadowLight;   // xyz = 灯位置, w = range
        float4 _SkyPrison_LampShadowSpot;    // xyz = 灯朝向, w = 1 聚光 / 0 点光
        float4 _SkyPrison_LampShadowCone;    // x = cos(外半角), y = cos(内半角), z = 强度×颜色亮度
        #define SKYPRISON_LAMP_SHADOW_FULL_BRIGHTNESS 0.25

        // normal：表面法线（由深度重建出来的世界坐标求导得到）。旧版写死「地面朝上」，
        // 现在影子能落在墙和斜坡上，法线必须按实际表面算。
        half LampLitAt(float3 pos, float3 normal)
        {
            float3 toL = _SkyPrison_LampShadowLight.xyz - pos;
            float d2 = max(dot(toL, toL), 1e-4);
            float range = max(_SkyPrison_LampShadowLight.w, 1e-3);
            float f = saturate(1.0 - (d2 / (range * range)) * (d2 / (range * range)));
            float atten = (f * f) / d2;
            float3 L = toL * rsqrt(d2);
            if (_SkyPrison_LampShadowSpot.w > 0.5)
            {
                float cosA = dot(_SkyPrison_LampShadowSpot.xyz, -L);
                float t = saturate((cosA - _SkyPrison_LampShadowCone.x) / max(_SkyPrison_LampShadowCone.y - _SkyPrison_LampShadowCone.x, 1e-4));
                atten *= t * t;
            }
            float brightness = _SkyPrison_LampShadowCone.z * atten * saturate(dot(normal, L));
            return (half)saturate(brightness / SKYPRISON_LAMP_SHADOW_FULL_BRIGHTNESS);
        }
        ENDHLSL

        // ---------- 1. 轮廓 Mask：把扶正后的角色画进 RT（横轴 u，纵轴 h） ----------
        Pass
        {
            Name "Mask"
            // 自定义 LightMode：URP 不会在正常相机流程里调度它，只由脚本用 CommandBuffer 显式画。
            Tags { "LightMode"="SkyPrisonLampMask" }

            Cull Off
            ZWrite Off
            ZTest Always
            Blend One One
            BlendOp Max

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vertMask
            #pragma fragment fragMask

            struct MaskAttributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct MaskVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            MaskVaryings vertMask(MaskAttributes IN)
            {
                MaskVaryings OUT;
                float3 wp = TransformObjectToWorld(IN.positionOS.xyz);

                // 扶正：和旧版一模一样。Spine 网格为了正对镜头整体绕 X 轴倾斜了 45°，
                // 逻辑上角色是垂直站立的，按网格自己的上轴拆出离脚高度，右轴拆出左右偏移。
                float3 root = TransformObjectToWorld(float3(0, 0, 0));
                float3 upW = normalize(mul((float3x3)UNITY_MATRIX_M, float3(0, 1, 0)));
                float3 d = wp - root;

                float3 right = float3(_SkyPrison_LampDecalRight.x, 0, _SkyPrison_LampDecalRight.y);
                float u = dot(d, right);
                float h = root.y + dot(d, upW) - _SkyPrison_LampDecalFoot.y;

                float2 muv = float2(u / max(_SkyPrison_LampDecalRight.z, 1e-3) * 0.5 + 0.5,
                                    h / max(_SkyPrison_LampDecalFoot.w, 1e-3));
                float cy = muv.y * 2.0 - 1.0;
                #if UNITY_UV_STARTS_AT_TOP
                cy = -cy;   // 直接写裁剪坐标不会被引擎翻转；D3D 的纹理第 0 行在上，要手动翻，采样时 v = h/H 才对得上
                #endif
                OUT.positionCS = float4(muv.x * 2.0 - 1.0, cy, 0.0, 1.0);
                OUT.uv = IN.uv;
                OUT.color = IN.color;
                return OUT;
            }

            half4 fragMask(MaskVaryings IN) : SV_Target
            {
                half a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).a * IN.color.a;
                return half4(a, 0, 0, 0);
            }
            ENDHLSL
        }

        // ---------- 2. 深度贴花：把 Mask 投到屏幕上看到的任意表面 ----------
        Pass
        {
            Name "Decal"
            Tags { "LightMode"="UniversalForward" }

            Cull Front
            ZWrite Off
            ZTest Always
            Blend DstColor Zero

            // 没有模板测试：旧版靠 Stencil NotEqual 1 避开「前景物体写的模板」，但那个模板是装饰物的整个
            // 可视几何，分不出「物体挡住了地面」和「这个像素就是物体自己的表面」，结果落在高台、斜坡、箱子
            // 自己表面上的影子也被一起拒绝了。深度贴花读的是最靠近相机的那个表面的深度：前景物体挡着地面时，
            // 该像素采样到的就是物体的深度，影子自然落在物体上而不是被挡住的地面上，不需要模板。

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vertDecal
            #pragma fragment fragDecal

            struct DecalAttributes { float4 positionOS : POSITION; };
            struct DecalVaryings { float4 positionCS : SV_POSITION; };

            DecalVaryings vertDecal(DecalAttributes IN)
            {
                DecalVaryings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 fragDecal(DecalVaryings IN) : SV_Target
            {
                float2 uv = IN.positionCS.xy / _ScaledScreenParams.xy;
                float depth = SampleSceneDepth(uv).x;
                #if !UNITY_REVERSED_Z
                depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, depth);
                #endif
                float3 P = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);

                // 求导必须在任何提前返回之前做（非均匀控制流里求导无定义）。
                float3 dPx = ddx(P);
                float3 dPy = ddy(P);

                // 只在盒体范围内起作用。
                float3 lp = TransformWorldToObject(P);
                if (max(abs(lp.x), max(abs(lp.y), abs(lp.z))) > 0.5)
                    return half4(1, 1, 1, 1);

                float3 foot = _SkyPrison_LampDecalFoot.xyz;
                // 墙挡光：脚到墙的这一段之外，灯照不到（被墙遮住了），不画影子。只按距离裁，常数开销。
                if (dot(P.xz - foot.xz, _SkyPrison_LampDecalClip.xy) > _SkyPrison_LampDecalClip.z)
                    return half4(1, 1, 1, 1);
                float3 right = float3(_SkyPrison_LampDecalRight.x, 0, _SkyPrison_LampDecalRight.y);
                float3 planeN = float3(-right.z, 0, right.x);       // 角色纸片平面的水平法线
                float3 Dv = _SkyPrison_LampDecalDv.xyz;
                float denom = dot(Dv, planeN);
                if (abs(denom) < 1e-4)
                    return half4(1, 1, 1, 1);                       // 灯在纸片侧面：纸片侧着对灯，压成一条线

                // 沿 Dv 把表面点反推回角色平面。s < 0 说明表面在纸片「上游」（灯那一侧），影子不会落在那里。
                float s = dot(P - foot, planeN) / denom;
                // 放宽到 -w：脚底贴地偏移 w 会把精灵最低点的影子放到 s = -(接触余量) 的位置（稍微越过根节点往灯那边），
                // 让影子起点钻到脚下、被角色自己盖住。裁在 0 的话，这一截会被整段切掉，反而在脚趾和影子之间留一道缝。
                if (s < -_SkyPrison_LampDecalRight.w)
                    return half4(1, 1, 1, 1);

                float3 C = P - s * Dv;
                float u = dot(C - foot, right);
                // 脚底贴地：精灵最低点离根节点 0.07 米，按物理算影子会从"悬空的脚底"往外投，和脚之间留一道缝，
                // 看上去隔了层玻璃。这里把精灵当成最低点落在地面上（再加一点接触余量让影子起点压在脚下、被角色盖住）：
                // 查遮罩时的高度 = 表面点对应的离地高度 + 偏移。Mask Pass 不用这个偏移，两边坐标不会错位。
                float h = C.y - foot.y + _SkyPrison_LampDecalRight.w;
                float2 muv = float2(u / max(_SkyPrison_LampDecalRight.z, 1e-3) * 0.5 + 0.5,
                                    h / max(_SkyPrison_LampDecalFoot.w, 1e-3));
                if (muv.x < 0.0 || muv.x > 1.0 || muv.y < 0.0 || muv.y > 1.0)
                    return half4(1, 1, 1, 1);

                half shape = SAMPLE_TEXTURE2D_LOD(_SkyPrison_LampMaskTex, sampler_LinearClamp, muv, 0).r;

                // 表面法线：由重建出来的世界坐标求导，并翻到朝向相机的一侧。
                float3 nrm = normalize(cross(dPy, dPx));
                if (dot(nrm, _WorldSpaceCameraPos - P) < 0.0)
                    nrm = -nrm;

                half strength = saturate(_SkyPrison_LampDecalDv.w) * LampLitAt(P, nrm);
                half3 shadowRgb = saturate(_ShadowColor.rgb);
                // 乘法混合：1 = 不变，越小越暗。
                return half4(lerp(half3(1, 1, 1), shadowRgb, shape * strength), 1.0h);
            }
            ENDHLSL
        }
    }
}
