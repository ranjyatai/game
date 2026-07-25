Shader "SkyPrison/Animation Layer FX/FC Retro Filter"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _EffectOpacity ("Effect Opacity", Range(0,1)) = 1.0
        _BrightnessGain ("Brightness Gain", Range(0,3)) = 1.08
        _SaturationGain ("Saturation Gain", Range(0,3)) = 1.18
        _ContrastGain ("Contrast Gain", Range(0,3)) = 1.12

        _PixelScale ("Pixel Scale", Range(1,16)) = 4.0
        _PaletteMode ("Palette Mode", Range(0,1)) = 1.0
        _DitherStrength ("Dither Strength", Range(0,1)) = 0.18
        _DitherScale ("Dither Scale", Range(1,8)) = 1.0

        _ScanlineStrength ("Scanline Strength", Range(0,1)) = 0.22
        _ScanlineDensity ("Scanline Density", Range(80,480)) = 240.0
        _GrilleStrength ("CRT Grille Strength", Range(0,1)) = 0.12
        _GrilleDensity ("CRT Grille Density", Range(160,960)) = 640.0
        _VignetteStrength ("Vignette Strength", Range(0,1)) = 0.18
        _CRTCurve ("CRT Curve", Range(0,1)) = 0.0

        _NoiseAmount ("Noise Amount", Range(0,1)) = 0.035
        _JitterAmount ("Horizontal Jitter", Range(0,0.02)) = 0.0015
        _ChromaticShift ("Chromatic Shift", Range(0,0.02)) = 0.0015

        _AlphaMode ("Alpha Keep/Effect", Range(0,1)) = 0.0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
            "PreviewType"="Plane"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.5
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            float4 _Color;

            float _SkyPrisonTime;
            float _PreviewTime;

            float _EffectOpacity;
            float _BrightnessGain;
            float _SaturationGain;
            float _ContrastGain;
            float _PixelScale;
            float _PaletteMode;
            float _DitherStrength;
            float _DitherScale;
            float _ScanlineStrength;
            float _ScanlineDensity;
            float _GrilleStrength;
            float _GrilleDensity;
            float _VignetteStrength;
            float _CRTCurve;
            float _NoiseAmount;
            float _JitterAmount;
            float _ChromaticShift;
            float _AlphaMode;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Bayer4(float2 pixel)
            {
                float2 p = fmod(floor(pixel), 4.0);
                float x = p.x;
                float y = p.y;

                float v = 0.0;
                if (y < 0.5)
                {
                    if (x < 0.5) v = 0.0;
                    else if (x < 1.5) v = 8.0;
                    else if (x < 2.5) v = 2.0;
                    else v = 10.0;
                }
                else if (y < 1.5)
                {
                    if (x < 0.5) v = 12.0;
                    else if (x < 1.5) v = 4.0;
                    else if (x < 2.5) v = 14.0;
                    else v = 6.0;
                }
                else if (y < 2.5)
                {
                    if (x < 0.5) v = 3.0;
                    else if (x < 1.5) v = 11.0;
                    else if (x < 2.5) v = 1.0;
                    else v = 9.0;
                }
                else
                {
                    if (x < 0.5) v = 15.0;
                    else if (x < 1.5) v = 7.0;
                    else if (x < 2.5) v = 13.0;
                    else v = 5.0;
                }

                return (v / 15.0) - 0.5;
            }

            float3 ClosestPaletteColor(float3 c)
            {
                // FC / NES-inspired compact palette. Not a full hardware palette;
                // tuned for readable layer-filter use.
                float3 p0  = float3(0.000, 0.000, 0.000);
                float3 p1  = float3(0.098, 0.098, 0.098);
                float3 p2  = float3(0.250, 0.250, 0.250);
                float3 p3  = float3(0.470, 0.470, 0.470);
                float3 p4  = float3(0.760, 0.760, 0.760);
                float3 p5  = float3(1.000, 1.000, 1.000);

                float3 p6  = float3(0.905, 0.353, 0.063); // orange
                float3 p7  = float3(0.969, 0.839, 0.710); // skin highlight
                float3 p8  = float3(0.710, 0.192, 0.129); // brick red
                float3 p9  = float3(0.902, 0.612, 0.129); // gold

                float3 p10 = float3(0.000, 0.678, 0.000); // green
                float3 p11 = float3(0.741, 1.000, 0.094); // lime
                float3 p12 = float3(0.224, 0.741, 1.000); // sky cyan
                float3 p13 = float3(0.360, 0.580, 0.988); // sky blue
                float3 p14 = float3(0.118, 0.518, 0.000); // dark green
                float3 p15 = float3(0.600, 0.294, 0.047); // brown

                float bestD = 1e9;
                float3 best = c;

                float d;
                d = dot(c-p0, c-p0);   if (d < bestD) { bestD = d; best = p0; }
                d = dot(c-p1, c-p1);   if (d < bestD) { bestD = d; best = p1; }
                d = dot(c-p2, c-p2);   if (d < bestD) { bestD = d; best = p2; }
                d = dot(c-p3, c-p3);   if (d < bestD) { bestD = d; best = p3; }
                d = dot(c-p4, c-p4);   if (d < bestD) { bestD = d; best = p4; }
                d = dot(c-p5, c-p5);   if (d < bestD) { bestD = d; best = p5; }
                d = dot(c-p6, c-p6);   if (d < bestD) { bestD = d; best = p6; }
                d = dot(c-p7, c-p7);   if (d < bestD) { bestD = d; best = p7; }
                d = dot(c-p8, c-p8);   if (d < bestD) { bestD = d; best = p8; }
                d = dot(c-p9, c-p9);   if (d < bestD) { bestD = d; best = p9; }
                d = dot(c-p10, c-p10); if (d < bestD) { bestD = d; best = p10; }
                d = dot(c-p11, c-p11); if (d < bestD) { bestD = d; best = p11; }
                d = dot(c-p12, c-p12); if (d < bestD) { bestD = d; best = p12; }
                d = dot(c-p13, c-p13); if (d < bestD) { bestD = d; best = p13; }
                d = dot(c-p14, c-p14); if (d < bestD) { bestD = d; best = p14; }
                d = dot(c-p15, c-p15); if (d < bestD) { bestD = d; best = p15; }

                return best;
            }

            float2 CRTCurveUV(float2 uv)
            {
                float2 p = uv * 2.0 - 1.0;
                float2 offset = abs(p.yx) / float2(6.0, 4.0);
                p = p + p * offset * offset;
                return p * 0.5 + 0.5;
            }

            fixed4 SampleSprite(float2 uv)
            {
                float inside =
                    step(0.0, uv.x) * step(uv.x, 1.0) *
                    step(0.0, uv.y) * step(uv.y, 1.0);
                fixed4 col = tex2D(_MainTex, saturate(uv)) * _Color;
                col.a *= inside;
                return col;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float time = max(max(_SkyPrisonTime, _PreviewTime), _Time.y);

                float2 uv = i.uv;
                float2 curvedUv = CRTCurveUV(uv);
                uv = lerp(uv, curvedUv, _CRTCurve);

                float2 texSize = _MainTex_TexelSize.zw;
                float2 pixelGrid = max(_PixelScale, 1.0).xx;
                float2 pixelUv = (floor(uv * texSize / pixelGrid) * pixelGrid + pixelGrid * 0.5) / texSize;

                float jitterLine = floor(pixelUv.y * texSize.y / max(pixelGrid.y, 1.0));
                float jitter = (Hash21(float2(jitterLine, floor(time * 24.0))) - 0.5) * _JitterAmount;
                pixelUv.x += jitter;

                float2 caDir = float2(_ChromaticShift, 0.0);
                fixed4 baseCol = SampleSprite(pixelUv);
                if (baseCol.a <= 0.0001)
                    return fixed4(0,0,0,0);

                float3 splitCol;
                splitCol.r = SampleSprite(pixelUv + caDir).r;
                splitCol.g = baseCol.g;
                splitCol.b = SampleSprite(pixelUv - caDir).b;

                float3 col = lerp(baseCol.rgb, splitCol, saturate(_ChromaticShift * 80.0));

                // contrast / saturation / brightness
                float luma = dot(col, float3(0.299, 0.587, 0.114));
                col = lerp(luma.xxx, col, _SaturationGain);
                col = (col - 0.5) * _ContrastGain + 0.5;
                col *= _BrightnessGain;

                float2 ditherPixel = floor(pixelUv * texSize / max(_DitherScale, 1.0));
                float dither = Bayer4(ditherPixel) * _DitherStrength;
                col += dither;

                float3 paletteCol = ClosestPaletteColor(saturate(col));
                col = lerp(saturate(col), paletteCol, _PaletteMode);

                float scanline = 1.0 - _ScanlineStrength * (0.5 + 0.5 * cos(3.14159265 * (uv.y + time * 0.008) * _ScanlineDensity));
                float grille = 1.0 - _GrilleStrength * (0.5 + 0.5 * cos(3.14159265 * uv.x * _GrilleDensity));
                col *= scanline * grille;

                float vignette = uv.x * uv.y * (1.0 - uv.x) * (1.0 - uv.y);
                vignette = clamp(pow(16.0 * vignette, 0.30), 0.0, 1.0);
                col = lerp(col, col * vignette, _VignetteStrength);

                float noise = (Hash21(floor(uv * texSize) + floor(time * 60.0)) - 0.5) * _NoiseAmount;
                col += noise;

                float3 finalRgb = lerp(baseCol.rgb, saturate(col), _EffectOpacity);

                float finalAlpha = baseCol.a;
                if (_AlphaMode > 0.5)
                    finalAlpha = saturate(baseCol.a + _EffectOpacity * 0.05);

                return fixed4(saturate(finalRgb), finalAlpha);
            }
            ENDCG
        }
    }

    Fallback Off
}
