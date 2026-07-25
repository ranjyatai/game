Shader "SkyPrison/FogOfWarOverlay"
{
    Properties
    {
        _FogColor ("Fog Color", Color) = (0.35, 0.35, 0.35, 1.0)
        _VisibleColor ("Visible Color", Color) = (1, 1, 1, 1)
        _GlobalFogStrength ("Global Fog Strength", Range(0, 1)) = 0.62
        _SoftEdgeWidth ("Soft Edge Width", Range(0.01, 16.0)) = 3.5
        _AngleSoftness ("Angle Softness", Range(0.01, 45.0)) = 8.0
        _NoiseStrength ("Noise Strength", Range(0, 0.25)) = 0.035
        _NoiseScale ("Noise Scale", Range(0.1, 80.0)) = 18.0
        _NoiseSpeed ("Noise Speed", Range(0, 3.0)) = 0.12
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+100"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "FogOfWarOverlay_Multiply"
            Cull Off
            ZWrite Off
            ZTest Always

            // Multiplicative darkening:
            // source RGB = 1 means no change, source RGB < 1 darkens the already rendered scene.
            // This preserves existing light/shadow contrast instead of covering it with a flat alpha color.
            Blend DstColor Zero

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"

            #define MAX_VISION_SOURCES 16

            fixed4 _FogColor;
            fixed4 _VisibleColor;
            half _GlobalFogStrength;
            half _SoftEdgeWidth;
            half _AngleSoftness;
            half _NoiseStrength;
            half _NoiseScale;
            half _NoiseSpeed;

            int _VisionSourceCount;
            float4 _VisionSourceOriginRadius[MAX_VISION_SOURCES];
            float4 _VisionSourceForwardAngle[MAX_VISION_SOURCES];
            float4 _VisionSourceFlags[MAX_VISION_SOURCES];

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            half HashNoise(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            half EvaluateSourceVisibility(float3 worldPos, int index)
            {
                float4 originRadius = _VisionSourceOriginRadius[index];
                float4 forwardAngle = _VisionSourceForwardAngle[index];
                float4 flags = _VisionSourceFlags[index];

                float3 origin = originRadius.xyz;
                float radius = max(originRadius.w, 0.0001);
                float softEdge = max(_SoftEdgeWidth, 0.0001);

                float3 delta = worldPos - origin;
                delta.y = 0.0;

                float distance = length(delta);
                if (distance > radius + softEdge)
                    return 0.0h;

                half radiusVisible = 1.0h - saturate((distance - radius) / softEdge);

                bool useCircle = flags.x > 0.5;
                bool useFacingDirection = flags.y > 0.5;

                if (useCircle || !useFacingDirection || distance <= 0.0001)
                    return radiusVisible;

                float3 forward = forwardAngle.xyz;
                forward.y = 0.0;
                forward = normalize(forward + float3(0.0001, 0.0, 0.0));

                float3 dir = normalize(delta);
                float dotValue = clamp(dot(forward, dir), -1.0, 1.0);
                float angleToTarget = degrees(acos(dotValue));
                float halfAngle = max(forwardAngle.w * 0.5, 0.0001);
                float angleSoftness = max(_AngleSoftness, 0.0001);

                half angleVisible = 1.0h - saturate((angleToTarget - halfAngle) / angleSoftness);
                return min(radiusVisible, angleVisible);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                half visibility = 0.0h;

                [loop]
                for (int s = 0; s < MAX_VISION_SOURCES; s++)
                {
                    if (s >= _VisionSourceCount)
                        break;

                    visibility = max(visibility, EvaluateSourceVisibility(i.worldPos, s));

                    if (visibility >= 0.999h)
                        break;
                }

                visibility = saturate(visibility);

                half noise = HashNoise(i.worldPos.xz * _NoiseScale + _Time.y * _NoiseSpeed);
                half fogAmount = (1.0h - visibility) * _GlobalFogStrength;
                fogAmount += (noise - 0.5h) * _NoiseStrength * (1.0h - visibility);
                fogAmount = saturate(fogAmount);

                // _FogColor.rgb is used as the darkest multiplier in fully fogged pixels.
                // (1,1,1) = unchanged, lower value = darker. Shadow contrast is preserved.
                fixed3 clearMultiplier = fixed3(1.0, 1.0, 1.0);
                fixed3 fogMultiplier = saturate(_FogColor.rgb);
                fixed3 multiplier = lerp(clearMultiplier, fogMultiplier, fogAmount);

                return fixed4(multiplier, 1.0);
            }
            ENDCG
        }
    }

    FallBack Off
}
