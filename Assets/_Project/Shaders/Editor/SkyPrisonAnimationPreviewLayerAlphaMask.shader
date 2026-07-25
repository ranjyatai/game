Shader "Hidden/SkyPrison/AnimationPreviewLayerAlphaMask"
{
    Properties
    {
        _MainTex ("Effect Layer", 2D) = "white" {}
        _AlphaTex ("Original Alpha", 2D) = "white" {}
        _AlphaThreshold ("Alpha Threshold", Float) = 0.015
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend One Zero

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _AlphaTex;
            float _AlphaThreshold;
            float4 _MainTex_ST;

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

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);
                float maskA = tex2D(_AlphaTex, i.uv).a;

                // Some PSD/PSB exports leave tiny alpha or matte color in nominally transparent pixels.
                // If a layer shader such as glitch writes a full rectangular effect, those tiny alpha values
                // can become visible as a large block.  Threshold the original silhouette very lightly, then
                // clamp the processed shader result back to that silhouette.
                maskA = saturate((maskA - _AlphaThreshold) / max(1e-5, 1.0 - _AlphaThreshold));

                col.rgb *= maskA;
                col.a *= maskA;
                return col;
            }
            ENDCG
        }
    }
    Fallback Off
}
