Shader "Hidden/SkyPrison/MaskMultiplyComposite"
{
    // V4 - Blend-safe composite + alpha-only debug/final overlay. Required by V20+ Feature.
    // Pass 0: Output HiddenMask = CharacterMask * OccluderMask.
    // Pass 1: Optional fullscreen debug overlay for Character / Occluder / Hidden / HiddenEdge.

    Properties
    {
        _CharacterMaskTex ("Character Mask", 2D) = "black" {}
        _OccluderMaskTex ("Occluder Mask", 2D) = "black" {}
        _Threshold ("Threshold", Range(0,1)) = 0.01
        _SampleBothY ("Sample Both Y Directions", Float) = 0
        _DebugViewMode ("Debug View Mode", Float) = 0
        _DebugTint ("Debug Tint", Color) = (1,1,1,0.6)
        _DebugAlpha ("Debug Alpha", Range(0,1)) = 0.6
        _DebugEdgeWidthPixels ("Debug Edge Width Pixels", Range(1,12)) = 2
        _DebugTexelSize ("Debug Texel Size", Vector) = (0.001,0.001,1024,1024)
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Cull Off
        ZWrite Off
        ZTest Always

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D_X(_CharacterMaskTex);
        SAMPLER(sampler_CharacterMaskTex);
        TEXTURE2D_X(_OccluderMaskTex);
        SAMPLER(sampler_OccluderMaskTex);

        float _Threshold;
        float _SampleBothY;
        float _DebugViewMode;
        half4 _DebugTint;
        float _DebugAlpha;
        float _DebugEdgeWidthPixels;
        float4 _DebugTexelSize;

        struct Attributes
        {
            uint vertexID : SV_VertexID;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
        };

        Varyings Vert(Attributes input)
        {
            Varyings output;
            output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
            output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
            return output;
        }

        half MaxRGBA(half4 c)
        {
            return max(max(c.r, c.g), max(c.b, c.a));
        }

        half SampleTex(TEXTURE2D_X_PARAM(tex, sampler_tex), float2 uv)
        {
            half m = MaxRGBA(SAMPLE_TEXTURE2D_X(tex, sampler_tex, uv));
            if (_SampleBothY > 0.5)
            {
                float2 uv2 = uv;
                uv2.y = 1.0 - uv2.y;
                m = max(m, MaxRGBA(SAMPLE_TEXTURE2D_X(tex, sampler_tex, uv2)));
            }
            return saturate(m);
        }

        half SampleCharacter(float2 uv)
        {
            return SampleTex(TEXTURE2D_X_ARGS(_CharacterMaskTex, sampler_CharacterMaskTex), uv);
        }

        half SampleOccluder(float2 uv)
        {
            return SampleTex(TEXTURE2D_X_ARGS(_OccluderMaskTex, sampler_OccluderMaskTex), uv);
        }

        half SampleHidden(float2 uv)
        {
            // 试过用 smoothstep 在阈值附近做平滑过渡——阈值本身很小(默认0.01)，稍微
            // 一柔化就会把遮罩里大量接近0的漏光/噪声也一起放大，导致整个画面泛白。
            // 撤回，改回原来的硬判断，抗锯齿改从别的地方（分辨率/滤波）处理。
            return step(_Threshold, SampleCharacter(uv) * SampleOccluder(uv));
        }

        half SampleHiddenEdge(float2 uv)
        {
            half h = SampleHidden(uv);
            if (h <= 0.001)
                return 0;

            float2 t = abs(_DebugTexelSize.xy) * max(1.0, _DebugEdgeWidthPixels);
            half nMin = 1;
            nMin = min(nMin, SampleHidden(uv + float2( t.x, 0)));
            nMin = min(nMin, SampleHidden(uv + float2(-t.x, 0)));
            nMin = min(nMin, SampleHidden(uv + float2(0,  t.y)));
            nMin = min(nMin, SampleHidden(uv + float2(0, -t.y)));
            nMin = min(nMin, SampleHidden(uv + float2( t.x,  t.y)));
            nMin = min(nMin, SampleHidden(uv + float2(-t.x,  t.y)));
            nMin = min(nMin, SampleHidden(uv + float2( t.x, -t.y)));
            nMin = min(nMin, SampleHidden(uv + float2(-t.x, -t.y)));
            return saturate(h * (1.0 - nMin));
        }
        ENDHLSL

        Pass
        {
            Name "MaskMultiplyComposite"
            Blend One Zero
            ColorMask RGBA

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                half h = SampleHidden(input.uv);
                return half4(h, h, h, h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DebugMaskOverlay"
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGBA

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                half v = 0;

                if (_DebugViewMode < 1.5)
                    v = SampleCharacter(input.uv);
                else if (_DebugViewMode < 2.5)
                    v = SampleOccluder(input.uv);
                else if (_DebugViewMode < 3.5)
                    v = SampleHidden(input.uv);
                else
                    v = SampleHiddenEdge(input.uv);

                half a = saturate(v * _DebugAlpha * _DebugTint.a);
                return half4(_DebugTint.rgb, a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
