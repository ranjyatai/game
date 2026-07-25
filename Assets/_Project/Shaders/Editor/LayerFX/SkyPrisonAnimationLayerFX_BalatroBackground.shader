Shader "SkyPrison/Animation Layer FX/Balatro Background"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _EffectOpacity ("Effect Opacity", Range(0,1)) = 1.0
        _BaseMix ("Base Mix", Range(0,1)) = 0.35
        _BrightnessGain ("Brightness Gain", Range(0,4)) = 1.0

        _SpinRotation ("Spin Rotation", Range(-10,10)) = -2.0
        _SpinSpeed ("Spin Speed", Range(-20,20)) = 7.0
        _Offset ("Offset", Vector) = (0,0,0,0)
        _Colour1 ("Colour 1", Color) = (0.871, 0.267, 0.231, 1.0)
        _Colour2 ("Colour 2", Color) = (0.0, 0.42, 0.706, 1.0)
        _Colour3 ("Colour 3", Color) = (0.086, 0.137, 0.145, 1.0)
        _Contrast ("Contrast", Range(0.1,8.0)) = 3.5
        _Lighting ("Lighting", Range(0,2.0)) = 0.4
        _SpinAmount ("Spin Amount", Range(0,1.0)) = 0.25
        _PixelFilter ("Pixel Filter", Range(32,2048)) = 745.0
        _SpinEase ("Spin Ease", Range(0,4.0)) = 1.0
        _RotateOverTime ("Rotate Over Time", Range(0,1)) = 0.0

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
            float _BaseMix;
            float _BrightnessGain;
            float _SpinRotation;
            float _SpinSpeed;
            float4 _Offset;
            float4 _Colour1;
            float4 _Colour2;
            float4 _Colour3;
            float _Contrast;
            float _Lighting;
            float _SpinAmount;
            float _PixelFilter;
            float _SpinEase;
            float _RotateOverTime;
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

            float4 BalatroEffect(float2 screenSize, float2 screenCoords, float time)
            {
                float pixelSize = length(screenSize.xy) / max(_PixelFilter, 1.0);
                float2 uv = (floor(screenCoords.xy * (1.0 / pixelSize)) * pixelSize - 0.5 * screenSize.xy) / length(screenSize.xy) - _Offset.xy;
                float uvLen = length(uv);

                float speed = (_SpinRotation * _SpinEase * 0.2);
                speed = lerp(speed, time * speed, step(0.5, _RotateOverTime));
                speed += 302.2;

                float newPixelAngle = atan2(uv.y, uv.x) + speed - _SpinEase * 20.0 * (_SpinAmount * uvLen + (1.0 - _SpinAmount));
                float2 mid = (screenSize.xy / length(screenSize.xy)) / 2.0;
                uv = (float2(uvLen * cos(newPixelAngle) + mid.x, uvLen * sin(newPixelAngle) + mid.y) - mid);

                uv *= 30.0;
                speed = time * _SpinSpeed;
                float2 uv2 = float2(uv.x + uv.y, uv.x + uv.y);

                [unroll(5)]
                for (int i = 0; i < 5; i++)
                {
                    uv2 += sin(max(uv.x, uv.y)) + uv;
                    uv += 0.5 * float2(cos(5.1123314 + 0.353 * uv2.y + speed * 0.131121), sin(uv2.x - 0.113 * speed));
                    float delta = cos(uv.x + uv.y) - sin(uv.x * 0.711 - uv.y);
                    uv -= float2(delta, delta);
                }

                float contrastMod = (0.25 * _Contrast + 0.5 * _SpinAmount + 1.2);
                float paintRes = min(2.0, max(0.0, length(uv) * 0.035 * contrastMod));
                float c1p = max(0.0, 1.0 - contrastMod * abs(1.0 - paintRes));
                float c2p = max(0.0, 1.0 - contrastMod * abs(paintRes));
                float c3p = 1.0 - min(1.0, c1p + c2p);
                float light = (_Lighting - 0.2) * max(c1p * 5.0 - 4.0, 0.0) + _Lighting * max(c2p * 5.0 - 4.0, 0.0);

                float4 col = (0.3 / max(_Contrast, 0.001)) * _Colour1
                    + (1.0 - 0.3 / max(_Contrast, 0.001))
                    * (_Colour1 * c1p + _Colour2 * c2p + float4(c3p * _Colour3.rgb, c3p * _Colour1.a))
                    + float4(light, light, light, 0.0);

                col.rgb *= _BrightnessGain;
                return saturate(col);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float time = max(max(_SkyPrisonTime, _PreviewTime), _Time.y);
                fixed4 baseCol = tex2D(_MainTex, i.uv) * _Color;
                if (baseCol.a <= 0.0001)
                    return fixed4(0,0,0,0);

                float2 screenSize = _MainTex_TexelSize.zw;
                float2 screenCoords = i.uv * screenSize;
                float4 fx = BalatroEffect(screenSize, screenCoords, time);

                float3 blended = lerp(baseCol.rgb, saturate(fx.rgb + baseCol.rgb * _BaseMix), _EffectOpacity);
                float effectAlpha = saturate(baseCol.a + _EffectOpacity * 0.6);
                float finalAlpha = lerp(baseCol.a, effectAlpha, _AlphaMode);

                return fixed4(saturate(blended), finalAlpha);
            }
            ENDCG
        }
    }

    Fallback Off
}
