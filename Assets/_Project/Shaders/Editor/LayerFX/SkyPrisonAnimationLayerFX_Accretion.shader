Shader "SkyPrison/Animation Layer FX/Accretion"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _EffectOpacity ("Effect Opacity", Range(0,1)) = 1.0
        _BaseMix ("Base Mix", Range(0,1)) = 0.22
        _BrightnessGain ("Brightness Gain", Range(0,4)) = 1.18

        _CoreColorA ("Core Color A", Color) = (1.00, 0.40, 0.12, 1.0)
        _CoreColorB ("Core Color B", Color) = (0.30, 0.65, 1.00, 1.0)
        _HotColor ("Hot Highlight", Color) = (1.00, 0.92, 0.68, 1.0)

        _RaySteps ("Ray Steps", Range(6,40)) = 20
        _TurbulenceIterations ("Turbulence Iterations", Range(1,10)) = 7
        _TurbulenceStrength ("Turbulence Strength", Range(0,2)) = 1.0

        _TunnelRadius ("Tunnel Radius", Range(0.5,10.0)) = 5.0
        _AccretionThickness ("Accretion Thickness", Range(0.02,2.0)) = 0.40
        _DepthPull ("Depth Pull", Range(0,1.0)) = 0.20

        _SpinSpeed ("Spin Speed", Range(-10,10)) = 1.0
        _RefractionByStep ("Step Refraction", Range(0,1.0)) = 0.30
        _MarchScale ("March Scale", Range(0.05,4.0)) = 1.0

        _GlowStrength ("Glow Strength", Range(0,6)) = 1.0
        _RingContrast ("Ring Contrast", Range(0,4)) = 1.0
        _ChromaticShift ("Chromatic Shift", Range(0,0.03)) = 0.004
        _UVDistort ("UV Distort", Range(0,0.08)) = 0.010
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

            float4 _CoreColorA;
            float4 _CoreColorB;
            float4 _HotColor;

            float _RaySteps;
            float _TurbulenceIterations;
            float _TurbulenceStrength;

            float _TunnelRadius;
            float _AccretionThickness;
            float _DepthPull;

            float _SpinSpeed;
            float _RefractionByStep;
            float _MarchScale;

            float _GlowStrength;
            float _RingContrast;
            float _ChromaticShift;
            float _UVDistort;
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

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float3 AccretionSample(float2 uv, float time)
            {
                float aspect = _MainTex_TexelSize.y / max(_MainTex_TexelSize.x, 1e-5);
                float2 p2 = uv * 2.0 - 1.0;
                p2.x *= aspect;

                float z = 0.0;
                float d = 0.0;
                float4 o = 0.0;

                [loop]
                for (int si = 0; si < 40; si++)
                {
                    if (si >= (int)_RaySteps)
                        break;

                    float i = (float)si + 1.0;

                    float3 ray = normalize(float3(p2 * 2.0, 0.0) - float3(1.0, 1.0, aspect));
                    float3 p = z * ray + 0.1;

                    // Polar-cylinder transform from the original compact shader.
                    p = float3(
                        atan2(p.y / 0.2, p.x) * 2.0,
                        p.z / 3.0,
                        length(p.xy) - _TunnelRadius - z * _DepthPull
                    );

                    [loop]
                    for (int ti = 1; ti <= 10; ti++)
                    {
                        if (ti > (int)_TurbulenceIterations)
                            break;

                        float tf = (float)ti;
                        p += sin(p.yzx * tf + time * _SpinSpeed + _RefractionByStep * i) / tf * _TurbulenceStrength;
                    }

                    float4 distVec = float4(_AccretionThickness * cos(p) - _AccretionThickness, p.z);
                    d = length(distVec);

                    z += d * _MarchScale;

                    float4 phase = p.x + i * 0.4 + z + float4(6.0, 1.0, 2.0, 0.0);
                    float4 colorPulse = 1.0 + cos(phase);
                    o += colorPulse / max(d, 0.015);
                }

                o = tanh(o * o / 400.0);

                float3 fx = o.rgb;
                float heat = saturate(length(fx));
                float3 tint = lerp(_CoreColorA.rgb, _CoreColorB.rgb, saturate(fx.b + fx.g * 0.35));
                tint = lerp(tint, _HotColor.rgb, smoothstep(0.72, 1.15, heat));

                fx *= tint * _GlowStrength;
                fx *= _BrightnessGain;

                return saturate(fx);
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

            fixed4 frag(v2f i) : SV_Target
            {
                float time = max(max(_SkyPrisonTime, _PreviewTime), _Time.y);

                float2 center = i.uv - 0.5;
                float radius = length(center);
                float2 swirl = float2(-center.y, center.x);
                float2 uvDistort = swirl * sin(radius * 18.0 - time * _SpinSpeed * 2.0) * _UVDistort;

                fixed4 baseCol = SampleSprite(i.uv + uvDistort);
                if (baseCol.a <= 0.0001)
                    return fixed4(0,0,0,0);

                float3 fx = AccretionSample(i.uv + uvDistort, time);

                float2 ca = normalize(center + float2(1e-5, 0.0)) * _ChromaticShift * saturate(length(fx));
                float3 rFx = AccretionSample(i.uv + uvDistort + ca, time);
                float3 bFx = AccretionSample(i.uv + uvDistort - ca, time);
                fx = float3(rFx.r, fx.g, bFx.b);

                float ring = smoothstep(0.08, 0.42, radius) * (1.0 - smoothstep(0.48, 0.86, radius));
                fx += lerp(_CoreColorB.rgb, _CoreColorA.rgb, ring) * ring * 0.18 * _RingContrast;

                float vignette = smoothstep(1.2, 0.12, radius * 2.0);
                fx *= lerp(1.0, vignette, _VignetteStrength);

                float noise = (Hash21(floor(i.uv * _MainTex_TexelSize.zw) + floor(time * 24.0)) - 0.5) * _NoiseAmount;
                fx += noise;

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
