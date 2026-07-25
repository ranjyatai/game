Shader "SkyPrison/Animation Layer FX/Dynamic Noise Glitch LegacyBoost 140"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Intensity ("Intensity", Range(0, 2)) = 1
        _NoiseAmount ("Noise Amount", Range(0, 1)) = 0.18
        _GlitchAmount ("Glitch Amount", Range(0, 1)) = 0.035
        _RgbSplit ("RGB Split", Range(0, 1)) = 0.018
        _LineCount ("Glitch Line Count", Range(8, 240)) = 72
        _Speed ("Speed", Range(0, 20)) = 8
        _BrightnessGain ("Brightness Gain", Range(0.5, 2.5)) = 1.40
        _NoiseBrightnessBias ("Noise Brightness Bias", Range(-1, 1)) = 0.18
        _ScanlineDarken ("Scanline Darken", Range(0, 1)) = 0.22
        _Color ("Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
        }

        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Intensity;
            float _NoiseAmount;
            float _GlitchAmount;
            float _RgbSplit;
            float _LineCount;
            float _Speed;
            float _BrightnessGain;
            float _NoiseBrightnessBias;
            float _ScanlineDarken;
            fixed4 _Color;
            float _SkyPrisonTime;
            float _PreviewTime;
            float4 _SkyPrisonLayerRect;
            float4 _SkyPrisonLayerRectPixels;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            fixed4 SafeSample(float2 uv)
            {
                if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
                    return fixed4(0, 0, 0, 0);
                return tex2D(_MainTex, uv);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 rectPos = _SkyPrisonLayerRect.xy;
                float2 rectSize = max(_SkyPrisonLayerRect.zw, float2(1e-5, 1e-5));

                float2 localUv = (i.uv - rectPos) / rectSize;
                if (localUv.x < 0.0 || localUv.x > 1.0 || localUv.y < 0.0 || localUv.y > 1.0)
                    return fixed4(0, 0, 0, 0);

                float t = max(_SkyPrisonTime, _PreviewTime) * max(0.01, _Speed);
                float intensity = saturate(_Intensity);

                float lineId = floor(localUv.y * max(1.0, _LineCount));
                float lineNoise = Hash12(float2(lineId, floor(t * 2.0)));
                float burst = step(0.72, lineNoise) * intensity;
                float localOffset = (lineNoise - 0.5) * _GlitchAmount * burst;

                float fineNoise = Hash12(floor(localUv * float2(420.0, 260.0)) + floor(t * 18.0));
                float jitter = (fineNoise - 0.5) * _GlitchAmount * 0.20 * intensity;

                float2 shiftedLocalUv = localUv + float2(localOffset + jitter, 0.0);
                float split = _RgbSplit * burst * intensity;

                float2 uv = rectPos + shiftedLocalUv * rectSize;
                float2 uvR = rectPos + (shiftedLocalUv + float2(split, 0.0)) * rectSize;
                float2 uvB = rectPos + (shiftedLocalUv - float2(split, 0.0)) * rectSize;

                fixed4 c = (shiftedLocalUv.x < 0.0 || shiftedLocalUv.x > 1.0 || shiftedLocalUv.y < 0.0 || shiftedLocalUv.y > 1.0)
                    ? fixed4(0, 0, 0, 0)
                    : SafeSample(uv);

                fixed4 cr = (shiftedLocalUv.x + split < 0.0 || shiftedLocalUv.x + split > 1.0) ? c : SafeSample(uvR);
                fixed4 cb = (shiftedLocalUv.x - split < 0.0 || shiftedLocalUv.x - split > 1.0) ? c : SafeSample(uvB);

                c.r = cr.r;
                c.b = cb.b;

                // 保留之前那种偏“脏、颗粒、故障”的观感，但让噪声略偏亮，避免整体压暗。
                float2 pixelInLayer = localUv * max(_SkyPrisonLayerRectPixels.zw, float2(1.0, 1.0));
                float grain = Hash12(pixelInLayer + floor(t * 30.0));
                float centeredGrain = (grain - 0.5 + _NoiseBrightnessBias);
                c.rgb += centeredGrain * _NoiseAmount * intensity * c.a;

                // 保留旧版扫描线的质感，但暗化强度做成可控，默认比旧版轻。
                float scan = sin((localUv.y * _LineCount + t) * 6.28318) * 0.5 + 0.5;
                float scanMul = lerp(1.0, 0.86 + scan * 0.18, _ScanlineDarken * intensity);
                c.rgb *= scanMul;

                // 最后统一补亮。默认 1.40，目标是接近正常模式基础亮度，同时保留旧版 glitch 味道。
                c.rgb *= lerp(1.0, _BrightnessGain, intensity);
                c.rgb = saturate(c.rgb);

                c.rgb *= _Color.rgb;
                c.a *= _Color.a;
                return c;
            }
            ENDCG
        }
    }

    FallBack Off
}
