Shader "SkyPrison/Animation Layer FX/Neural Connected"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _EffectOpacity ("Effect Opacity", Range(0,1)) = 1.0
        _BaseMix ("Base Mix", Range(0,1)) = 0.28
        _BrightnessGain ("Brightness Gain", Range(0,4)) = 1.25

        _NodeColor ("Node Color", Color) = (0.10, 0.42, 1.00, 1.0)
        _LineColor ("Line Color", Color) = (0.05, 0.22, 0.65, 1.0)
        _CoreColor ("Core Highlight Color", Color) = (0.62, 0.92, 1.00, 1.0)

        _NodeCount ("Node Count", Range(8,60)) = 42.0
        _LayerCount ("Layer Count", Range(1,3)) = 3.0
        _ConnectDistance ("Connect Distance", Range(0.05,1.5)) = 0.55
        _LineThickness ("Line Thickness", Range(0.0001,0.02)) = 0.0012

        _NodeSize ("Node Size", Range(0.0005,0.03)) = 0.004
        _NodeGlow ("Node Glow", Range(0,8)) = 1.0
        _LineGlow ("Line Glow", Range(0,8)) = 1.0

        _MotionSpeed ("Motion Speed", Range(-5,5)) = 1.0
        _OrbitStrength ("Orbit Strength", Range(0,1)) = 0.0
        _OrbitCenter ("Orbit Center", Vector) = (0.5, 0.5, 0, 0)
        _OrbitRadius ("Orbit Radius", Range(0,1)) = 0.10

        _DepthSpread ("Depth Spread", Range(0,2)) = 1.0
        _ParallaxOffset ("Layer Parallax Offset", Vector) = (0.035, -0.02, 0, 0)
        _PulseStrength ("Pulse Strength", Range(0,2)) = 0.45
        _PulseSpeed ("Pulse Speed", Range(-10,10)) = 2.0

        _UVDistort ("UV Distort", Range(0,0.08)) = 0.006
        _ChromaticShift ("Chromatic Shift", Range(0,0.03)) = 0.002
        _NoiseAmount ("Noise Amount", Range(0,1)) = 0.012
        _VignetteStrength ("Vignette Strength", Range(0,1)) = 0.15

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
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            float4 _Color;

            float _SkyPrisonTime;
            float _PreviewTime;

            float _EffectOpacity;
            float _BaseMix;
            float _BrightnessGain;

            float4 _NodeColor;
            float4 _LineColor;
            float4 _CoreColor;

            float _NodeCount;
            float _LayerCount;
            float _ConnectDistance;
            float _LineThickness;

            float _NodeSize;
            float _NodeGlow;
            float _LineGlow;

            float _MotionSpeed;
            float _OrbitStrength;
            float4 _OrbitCenter;
            float _OrbitRadius;

            float _DepthSpread;
            float4 _ParallaxOffset;
            float _PulseStrength;
            float _PulseSpeed;

            float _UVDistort;
            float _ChromaticShift;
            float _NoiseAmount;
            float _VignetteStrength;

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

            float Hash11(float p)
            {
                return frac(sin(p * 12345.564) * 7658.76);
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float LineDistance(float2 p, float2 a, float2 b)
            {
                float2 pa = p - a;
                float2 ba = b - a;
                float h = clamp(dot(pa, ba) / max(dot(ba, ba), 1e-5), 0.0, 1.0);
                return length(pa - ba * h);
            }

            float2 GetNodePos(float i, float layer, float depth, float2 uv, float t)
            {
                float seed = i * 123.45 + layer * 567.89;
                float pSpeed = 1.0 + depth * 0.5;

                float2 home = float2(
                    sin(t * 0.2 * pSpeed + seed),
                    cos(t * 0.3 * pSpeed + seed * 1.1)
                ) * (0.4 + depth * 0.3 * _DepthSpread);

                float2 center = _OrbitCenter.xy * 2.0 - 1.0;
                float aspect = _MainTex_TexelSize.y / max(_MainTex_TexelSize.x, 1e-5);
                center.x *= aspect;

                float2 orbit = center + float2(cos(t + seed), sin(t + seed)) * (_OrbitRadius * max(depth, 0.001));
                float2 p = lerp(home, orbit, _OrbitStrength);

                p += _ParallaxOffset.xy * (layer - 1.0) * depth;
                return p;
            }

            fixed4 SampleSprite(float2 uv)
            {
                float inside =
                    step(0.0, uv.x) * step(uv.x, 1.0) *
                    step(0.0, uv.y) * step(uv.y, 1.0);

                fixed4 c = tex2D(_MainTex, saturate(uv)) * _Color;
                c.a *= inside;
                return c;
            }

            float3 RenderNeural(float2 uv, float t)
            {
                float3 finalCol = 0.0;

                float maxLayers = clamp(_LayerCount, 1.0, 3.0);
                float maxNodes = clamp(_NodeCount, 8.0, 60.0);

                [loop]
                for (int li = 1; li <= 3; li++)
                {
                    if ((float)li > maxLayers)
                        break;

                    float layer = (float)li;
                    float depth = layer / max(maxLayers, 1.0);
                    float pSize = _NodeSize * (1.0 / max(depth, 0.001));
                    float pDist = _ConnectDistance * depth;
                    float3 layerCol = 0.0;

                    [loop]
                    for (int ii = 0; ii < 60; ii++)
                    {
                        if ((float)ii >= maxNodes)
                            break;

                        float i = (float)ii;
                        float2 p = GetNodePos(i, layer, depth, uv, t);
                        float d = length(uv - p);

                        float pulse = 1.0 + sin(t * _PulseSpeed + i * 0.73 + layer) * 0.5 * _PulseStrength;
                        float node = (pSize / (d + 0.001)) * depth * pulse;
                        node = min(node, 2.0);

                        layerCol += lerp(_NodeColor.rgb, _CoreColor.rgb, saturate(node * 0.45)) * node * _NodeGlow;

                        [loop]
                        for (int jj = 0; jj < 60; jj++)
                        {
                            if (jj <= ii)
                                continue;
                            if ((float)jj >= maxNodes)
                                break;

                            float j = (float)jj;
                            float2 p2 = GetNodePos(j, layer, depth, uv, t);
                            float distPoints = length(p - p2);

                            if (distPoints < pDist)
                            {
                                float dLine = LineDistance(uv, p, p2);
                                float brightness = (1.0 - distPoints / max(pDist, 1e-4));
                                float lineGlow = (_LineThickness / (dLine + 0.0001)) * brightness * depth;
                                lineGlow = min(lineGlow, 1.75);

                                float flow = sin(t * _PulseSpeed + i * 0.37 + j * 0.19 + layer);
                                float flowPulse = 1.0 + flow * 0.35 * _PulseStrength;

                                layerCol += _LineColor.rgb * lineGlow * flowPulse * _LineGlow;
                            }
                        }
                    }

                    finalCol += layerCol;
                }

                return pow(saturate(finalCol), 0.8.xxx);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float time = max(max(_SkyPrisonTime, _PreviewTime), _Time.y) * _MotionSpeed;

                float2 uv01 = i.uv;
                float2 centered = uv01 * 2.0 - 1.0;
                float aspect = _MainTex_TexelSize.y / max(_MainTex_TexelSize.x, 1e-5);
                centered.x *= aspect;

                float2 distort = float2(
                    sin(centered.y * 9.0 + time * 0.7),
                    cos(centered.x * 8.0 - time * 0.6)
                ) * _UVDistort;

                fixed4 baseCol = SampleSprite(uv01 + distort);
                if (baseCol.a <= 0.0001)
                    return fixed4(0,0,0,0);

                float3 fx = RenderNeural(centered, time);

                float2 shiftDir = normalize(centered + float2(1e-5, 0.0));
                float2 ca = shiftDir * _ChromaticShift * saturate(length(fx));
                float3 fxR = RenderNeural(centered + ca, time);
                float3 fxB = RenderNeural(centered - ca, time);
                fx = float3(fxR.r, fx.g, fxB.b);

                float rad = length(centered);
                float vignette = smoothstep(1.55, 0.10, rad);
                fx *= lerp(1.0, vignette, _VignetteStrength);

                float noise = (Hash21(floor(uv01 * _MainTex_TexelSize.zw) + floor(time * 24.0)) - 0.5) * _NoiseAmount;
                fx += noise;
                fx *= _BrightnessGain;

                float3 effectRgb = baseCol.rgb * _BaseMix + fx;
                float3 finalRgb = lerp(baseCol.rgb, saturate(effectRgb), _EffectOpacity);

                float effectMask = saturate(length(fx) * 0.25);
                float finalAlpha = lerp(baseCol.a, saturate(baseCol.a + effectMask * 0.25), _AlphaMode);

                return fixed4(saturate(finalRgb), finalAlpha);
            }
            ENDCG
        }
    }

    Fallback Off
}
