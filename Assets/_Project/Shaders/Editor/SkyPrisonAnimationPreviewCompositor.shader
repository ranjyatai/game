Shader "Hidden/SkyPrison/AnimationPreviewCompositor"
{
    Properties
    {
        _BaseTex ("Base", 2D) = "black" {}
        _LayerTex ("Layer", 2D) = "black" {}
        _Mode ("Mode", Int) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _BaseTex;
            sampler2D _LayerTex;
            int _Mode;

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
                o.uv = v.uv;
                return o;
            }

            float3 SafeUnpremul(float3 rgb, float a)
            {
                return a > 1e-5 ? rgb / a : 0;
            }

            float3 BlendMode(float3 b, float3 s, int mode)
            {
                if (mode == 1) return min(b, s);                              // 变暗
                if (mode == 2) return b * s;                                  // 正片叠底
                if (mode == 3) return 1.0 - (1.0 - b) / max(s, 1e-4);          // 颜色加深
                if (mode == 4) return max(b + s - 1.0, 0.0);                  // 线性加深
                if (mode == 5) return max(b, s);                              // 变亮
                if (mode == 6) return 1.0 - (1.0 - b) * (1.0 - s);            // 滤色
                if (mode == 7) return b / max(1.0 - s, 1e-4);                 // 颜色减淡
                if (mode == 8) return lerp(2.0 * b * s, 1.0 - 2.0 * (1.0 - b) * (1.0 - s), step(0.5, b)); // 叠加
                if (mode == 9)                                                // 柔光，近似 Photoshop soft light
                {
                    float3 d = (b <= 0.25) ? (((16.0 * b - 12.0) * b + 4.0) * b) : sqrt(max(b, 0.0));
                    return lerp(b - (1.0 - 2.0 * s) * b * (1.0 - b), b + (2.0 * s - 1.0) * (d - b), step(0.5, s));
                }
                if (mode == 10) return lerp(2.0 * b * s, 1.0 - 2.0 * (1.0 - b) * (1.0 - s), step(0.5, s)); // 强光
                if (mode == 11) return abs(b - s);                            // 差值
                if (mode == 12) return b + s - 2.0 * b * s;                   // 排除

                // HSL 系列先安全降级为当前层颜色，避免错误色相转换污染预览。
                // 后续需要更精确再单独加 RGB/HSL 转换。
                if (mode >= 13 && mode <= 16) return s;
                return s;                                                     // 正常
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float4 baseP = tex2D(_BaseTex, i.uv);
                float4 layerP = tex2D(_LayerTex, i.uv);

                float ba = saturate(baseP.a);
                float la = saturate(layerP.a);
                float3 b = saturate(SafeUnpremul(baseP.rgb, ba));
                float3 s = saturate(SafeUnpremul(layerP.rgb, la));

                float3 blended = saturate(BlendMode(b, s, _Mode));
                float outA = la + ba * (1.0 - la);

                // Premultiplied alpha 合成。base/layer 都来自 EditorSpritePremultiply 管线。
                float3 outStraight = (outA > 1e-5) ? ((blended * la) + (b * ba * (1.0 - la))) / outA : 0;
                return float4(saturate(outStraight) * outA, outA);
            }
            ENDCG
        }
    }
    Fallback Off
}
