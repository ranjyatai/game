Shader "Custom/FrontOccluder/Outline/OutlineProxySilhouette"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Main Texture", 2D) = "white" {}
        _Color ("Silhouette Color", Color) = (1,0,1,1)
        [Toggle] _StraightAlphaInput ("Straight Alpha Texture", Float) = 0
        _AlphaCutoff ("Alpha Cutoff", Range(0,1)) = 0.01
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
            Name "Silhouette"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _Color;
            float _StraightAlphaInput;
            float _AlphaCutoff;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float2 uv    : TEXCOORD0;
                float4 color : COLOR;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.uv);

                if (_StraightAlphaInput > 0.5)
                {
                    tex.rgb *= tex.a;
                }

                float alpha = tex.a * i.color.a * _Color.a;
                clip(alpha - _AlphaCutoff);

                fixed4 col;
                col.rgb = _Color.rgb;
                col.a = alpha;
                return col;
            }
            ENDCG
        }
    }
}