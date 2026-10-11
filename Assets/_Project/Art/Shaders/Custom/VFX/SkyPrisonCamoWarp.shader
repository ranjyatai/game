Shader "Hidden/SP/CamoWarp"
{
    // 光学迷彩的歪曲层(液化 / 歪みツール的感觉)：由 SkyPrisonCamoWarpFeature 在透明物体画完之后实时截一张画面(_SkyPrisonCamoGrab，
    // 含血迹、角色等透明物体)，再在怪人周围一块比身体大的面片上读模糊后的屏幕遮罩 _SkyPrisonCamoMask，
    // 按遮罩的强度和坡度去推挤这张画面：内部往外鼓(凸透镜一样的放大)、再加一点随时间流动的旋涡，
    // 遮罩边缘平滑淡到 0——没有轮廓线、没有部件接缝。整层 alpha = 遮罩强度，背景采样和最终画面的细微差异也会柔和淡出。
    // (以前读 _CameraOpaqueTexture，里面没有血迹 / 角色，经过时会把它们"擦掉"——10-12 改成实时截图。)
    Properties
    {
        _WarpStrength ("推挤强度", Range(0, 0.2)) = 0.06
        _Bulge        ("鼓起(放大)", Range(-1, 1)) = 0.6
        _Swirl        ("旋涡", Range(0, 1)) = 0.35
        _Chroma       ("色散", Range(0, 0.5)) = 0.12
        _FlowSpeed    ("流动速度", Range(0, 5)) = 1.2
        _MaskFlipY    ("遮罩上下翻转", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent-100" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off ZTest Always Cull Off

        Pass
        {
            Name "CamoWarp"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_SkyPrisonCamoGrab); SAMPLER(sampler_SkyPrisonCamoGrab);
            float3 Grab(float2 uv) { return SAMPLE_TEXTURE2D(_SkyPrisonCamoGrab, sampler_SkyPrisonCamoGrab, uv).rgb; }

            TEXTURE2D(_SkyPrisonCamoMask); SAMPLER(sampler_SkyPrisonCamoMask);
            float4 _SkyPrisonCamoMask_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                float _WarpStrength, _Bulge, _Swirl, _Chroma, _FlowSpeed, _MaskFlipY;
            CBUFFER_END

            struct A { float4 pos : POSITION; };
            struct V { float4 pos : SV_POSITION; float4 sp : TEXCOORD0; };
            V vert(A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.sp = ComputeScreenPos(o.pos); return o; }

            float Mask(float2 uv) { if (_MaskFlipY > 0.5) uv.y = 1.0 - uv.y; return SAMPLE_TEXTURE2D(_SkyPrisonCamoMask, sampler_SkyPrisonCamoMask, uv).r; }

            float4 frag(V i) : SV_Target
            {
                float2 uv = i.sp.xy / max(i.sp.w, 0.0001);
                float w = Mask(uv);
                if (w < 0.003) discard;
                float2 e = _SkyPrisonCamoMask_TexelSize.xy * 2.0;
                float2 g = float2(Mask(uv + float2(e.x, 0)) - Mask(uv - float2(e.x, 0)), Mask(uv + float2(0, e.y)) - Mask(uv - float2(0, e.y)));
                float t = _Time.y * _FlowSpeed;
                // 鼓起：沿坡度往外推(采样点往遮罩更强处靠 = 放大)；旋涡：垂直于坡度、随时间来回
                float2 perp = float2(-g.y, g.x);
                float2 offset = (g * _Bulge + perp * _Swirl * sin(t + uv.y * 12.0)) * _WarpStrength * 8.0;
                offset += float2(sin(uv.y * 40.0 + t * 1.7), cos(uv.x * 33.0 + t * 1.3)) * 0.0012 * w;
                float3 c;
                c.r = Grab(uv + offset * (1.0 - _Chroma)).r;
                c.g = Grab(uv + offset).g;
                c.b = Grab(uv + offset * (1.0 + _Chroma)).b;
                return float4(c, saturate(w * 3.0));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
