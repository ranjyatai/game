Shader "SkyPrison/Animation Layer FX/StarNest Overlay"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _EffectOpacity ("Effect Opacity", Range(0,1)) = 1.0
        _BaseMix ("Base Mix", Range(0,1)) = 0.20

        _Zoom ("Zoom", Range(0.1, 2.0)) = 0.8
        _Tile ("Tile", Range(0.1, 2.0)) = 0.85
        _Speed ("Speed", Range(0.0, 0.2)) = 0.01
        _Brightness ("Brightness", Range(0.0, 0.02)) = 0.0015
        _DarkMatter ("Dark Matter", Range(0.0, 1.0)) = 0.3
        _DistFading ("Distance Fading", Range(0.0, 1.0)) = 0.73
        _Saturation ("Saturation", Range(0.0, 2.0)) = 0.85

        _RotationA1 ("Rotation A1", Range(-6.28318, 6.28318)) = 0.5
        _RotationA2 ("Rotation A2", Range(-6.28318, 6.28318)) = 0.8
        _CameraOffset ("Camera Offset", Vector) = (1.0, 0.5, 0.5, 0.0)
        _FlowDirection ("Flow Direction", Vector) = (2.0, 1.0, -2.0, 0.0)
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
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
            float _Zoom;
            float _Tile;
            float _Speed;
            float _Brightness;
            float _DarkMatter;
            float _DistFading;
            float _Saturation;
            float _RotationA1;
            float _RotationA2;
            float4 _CameraOffset;
            float4 _FlowDirection;

            static const int iterations = 17;
            static const int volsteps = 20;
            static const float formuparam = 0.53;
            static const float stepsize = 0.1;

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
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            float2x2 Rot(float a)
            {
                float s = sin(a);
                float c = cos(a);
                return float2x2(c, s, -s, c);
            }

            float3 ComputeStarNest(float2 uv)
            {
                float2 centered = uv - 0.5;
                float aspect = _MainTex_TexelSize.y / max(_MainTex_TexelSize.x, 1e-5);
                centered.y *= aspect;

                float3 dir = float3(centered * _Zoom, 1.0);
                float time = max(_SkyPrisonTime, _PreviewTime) * _Speed + 0.25;

                float2x2 rot1 = Rot(_RotationA1);
                float2x2 rot2 = Rot(_RotationA2);
                dir.xz = mul(rot1, dir.xz);
                dir.xy = mul(rot2, dir.xy);

                float3 from = _CameraOffset.xyz;
                from += _FlowDirection.xyz * time;
                from.xz = mul(rot1, from.xz);
                from.xy = mul(rot2, from.xy);

                float s = 0.1;
                float fade = 1.0;
                float3 v = 0.0;

                [loop]
                for (int r = 0; r < volsteps; r++)
                {
                    float3 p = from + s * dir * 0.5;
                    p = abs(_Tile.xxx - fmod(p, _Tile.xxx * 2.0));

                    float pa = 0.0;
                    float a = 0.0;

                    [loop]
                    for (int i = 0; i < iterations; i++)
                    {
                        float denom = max(dot(p, p), 1e-4);
                        p = abs(p) / denom - formuparam;
                        float lp = length(p);
                        a += abs(lp - pa);
                        pa = lp;
                    }

                    float dm = max(0.0, _DarkMatter - a * a * 0.001);
                    a *= a * a;

                    if (r > 6)
                        fade *= 1.0 - dm;

                    v += fade;
                    v += float3(s, s * s, s * s * s * s) * a * _Brightness * fade;
                    fade *= _DistFading;
                    s += stepsize;
                }

                v = lerp(length(v).xxx, v, _Saturation);
                return saturate(v * 0.01);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 baseCol = tex2D(_MainTex, i.uv) * _Color;
                float baseAlpha = baseCol.a;
                if (baseAlpha <= 0.0001)
                    return 0;

                float3 fx = ComputeStarNest(i.uv);
                float3 composite = lerp(baseCol.rgb, baseCol.rgb * _BaseMix + fx, _EffectOpacity);
                return float4(saturate(composite), baseAlpha);
            }
            ENDCG
        }
    }
}
