Shader "Spine/SpineOccludedTransparent"
{
    Properties
    {
        _MainTex ("Main Texture", 2D) = "white" {}
        _TintColor ("Tint Color", Color) = (1,1,1,1)

        _OccludedAlpha ("Occluded Alpha", Range(0,1)) = 0.65
        [Toggle] _StraightAlphaInput ("Straight Alpha Texture", Float) = 0
        [Toggle] _FlipMaskY ("Flip Mask Y", Float) = 0

        _MaskThreshold ("Mask Threshold", Range(0,1)) = 0.5
        _MaskSoftness ("Mask Softness", Range(0.001,0.2)) = 0.02
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
        Blend SrcAlpha OneMinusSrcAlpha
        Fog { Mode Off }

        Pass
        {
            Name "OccludedOnly"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _OcclusionTex;

            float4 _TintColor;
            float _OccludedAlpha;
            float _StraightAlphaInput;
            float _FlipMaskY;
            float _MaskThreshold;
            float _MaskSoftness;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos       : SV_POSITION;
                float2 uv        : TEXCOORD0;
                float4 color     : COLOR;
                float4 screenPos : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                o.screenPos = ComputeScreenPos(o.pos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 texColor = tex2D(_MainTex, i.uv);

                if (_StraightAlphaInput > 0.5)
                {
                    texColor.rgb *= texColor.a;
                }

                fixed4 col = texColor * i.color;
                col.rgb *= _TintColor.rgb;
                col.a *= _TintColor.a;
                col.a *= _OccludedAlpha;

                float2 screenUV = i.screenPos.xy / i.screenPos.w;

                if (_FlipMaskY > 0.5)
                {
                    screenUV.y = 1.0 - screenUV.y;
                }

                fixed4 maskTex = tex2D(_OcclusionTex, screenUV);
                float maskValue = maskTex.r;

                float mask = smoothstep(
                    _MaskThreshold - _MaskSoftness,
                    _MaskThreshold + _MaskSoftness,
                    maskValue
                );

                // Proxy：只显示白色遮挡区
                col.a *= mask;

                clip(col.a - 0.001);
                return col;
            }
            ENDCG
        }
    }
}