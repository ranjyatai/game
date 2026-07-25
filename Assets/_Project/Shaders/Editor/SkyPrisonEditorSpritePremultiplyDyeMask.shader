Shader "Hidden/SkyPrison/EditorSpritePremultiplyDyeMask"
{
    Properties
    {
        _MainTex ("Sprite", 2D) = "white" {}
        _MaskTex ("Alpha Mask", 2D) = "white" {}
        _DyeMaskTex ("Clean Dye Coverage Mask", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _DyeColorR ("Dye R", Color) = (1,1,1,1)
        _DyeColorG ("Dye G", Color) = (1,1,1,1)
        _DyeColorB ("Dye B", Color) = (1,1,1,1)
        _DyeColorA ("Dye A", Color) = (1,1,1,1)
        _DyeBaseR ("Dye Base R", Color) = (1,1,1,1)
        _DyeBaseG ("Dye Base G", Color) = (1,1,1,1)
        _DyeBaseB ("Dye Base B", Color) = (1,1,1,1)
        _DyeBaseA ("Dye Base A", Color) = (1,1,1,1)
        _DyeEnabled ("Dye Enabled", Vector) = (0,0,0,0)
        _UseAlphaMask ("Use Alpha Mask", Float) = 0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _MaskTex;
            sampler2D _DyeMaskTex;
            fixed4 _Color;
            fixed4 _DyeColorR;
            fixed4 _DyeColorG;
            fixed4 _DyeColorB;
            fixed4 _DyeColorA;
            fixed4 _DyeBaseR;
            fixed4 _DyeBaseG;
            fixed4 _DyeBaseB;
            fixed4 _DyeBaseA;
            float4 _DyeEnabled;
            float _UseAlphaMask;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float2 screenUv : TEXCOORD1;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 screenUv : TEXCOORD1;
                fixed4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.screenUv = v.screenUv;
                o.color = v.color * _Color;
                return o;
            }

            float Luma(float3 c)
            {
                return dot(c, float3(0.299, 0.587, 0.114));
            }

            fixed3 RecolorByBase(fixed3 src, fixed3 baseColor, fixed3 targetColor)
            {
                // CleanCoverageMask 已经把区域边缘清洗好，Shader 只做固有色替换。
                // 以固有色亮度为基准保留原图明暗，不再做原始 RGB mask 判断。
                float srcLum = max(Luma(src), 0.001);
                float baseLum = max(Luma(baseColor), 0.035);
                float shade = srcLum / baseLum;
                shade = pow(saturate(shade), 0.92) * min(1.75, max(0.35, shade));
                fixed3 dyed = targetColor.rgb * shade;

                // 线稿/极暗区保护，避免黑线变彩色噪点。
                float darkKeep = 1.0 - smoothstep(0.025, 0.16, srcLum);
                dyed = lerp(dyed, src, darkKeep * 0.72);

                // 高光稍微保留白度，不让亮面完全变成塑料色。
                float high = smoothstep(0.78, 1.0, srcLum);
                dyed = lerp(dyed, max(dyed, src), high * 0.28);
                return saturate(dyed);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 src = tex2D(_MainTex, i.uv) * i.color;
                fixed4 clean = tex2D(_DyeMaskTex, i.uv);
                fixed3 original = src.rgb;

                float wr = saturate(clean.r) * _DyeEnabled.x;
                float wg = saturate(clean.g) * _DyeEnabled.y;
                float wb = saturate(clean.b) * _DyeEnabled.z;
                float wa = saturate(clean.a) * _DyeEnabled.w;

                // 已清洗的 coverage 仍可能因为平滑产生轻微叠加，这里限制总权重，避免发灰。
                float total = max(1.0, wr + wg + wb + wa);
                wr /= total; wg /= total; wb /= total; wa /= total;

                fixed3 cr = RecolorByBase(original, _DyeBaseR.rgb, _DyeColorR.rgb);
                fixed3 cg = RecolorByBase(original, _DyeBaseG.rgb, _DyeColorG.rgb);
                fixed3 cb = RecolorByBase(original, _DyeBaseB.rgb, _DyeColorB.rgb);
                fixed3 ca = RecolorByBase(original, _DyeBaseA.rgb, _DyeColorA.rgb);

                fixed3 target = original;
                target = lerp(target, cr, wr);
                target = lerp(target, cg, wg);
                target = lerp(target, cb, wb);
                target = lerp(target, ca, wa);

                float coverage = saturate(wr + wg + wb + wa);
                src.rgb = lerp(original, target, coverage);

                if (_UseAlphaMask > 0.5)
                {
                    fixed4 alphaMask = tex2D(_MaskTex, i.screenUv);
                    src.a *= alphaMask.a;
                    src.rgb *= alphaMask.a;
                }

                src.rgb *= src.a;
                return src;
            }
            ENDCG
        }
    }
}
