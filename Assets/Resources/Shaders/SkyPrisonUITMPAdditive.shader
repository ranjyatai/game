// TextMeshPro 文字的加算版本：用于夜视仪战术识别的文字。
// 采样 TMP 的 SDF 字体图集，按距离场算出字形覆盖率，再用 Blend SrcAlpha One 叠加到背景上。
// 只做基本字形（不支持描边 / 底衬 / 斜面等 TMP 特效）；粗体按 TMP 写在 texcoord0.w 的符号加粗一点。
// ZTest Always：单位站在墙后时字也不被挡。
// 放在 Resources 下：脚本用 Shader.Find 取，保证打包时一定带上。
Shader "SkyPrison/UI/TMP Additive"
{
    Properties
    {
        _MainTex ("Font Atlas", 2D) = "white" {}
        _Weight ("Weight", Range(-0.2, 0.2)) = 0.03
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float4 texcoord0 : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float bold : TEXCOORD1;
            };

            sampler2D _MainTex;
            float _Weight;

            v2f vert (appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord0.xy;
                o.bold = v.texcoord0.w < 0 ? 1 : 0;
                o.color = v.color;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float d = tex2D(_MainTex, i.uv).a;
                float edge = 0.5 - _Weight - i.bold * 0.06;
                float aa = max(fwidth(d) * 0.75, 1e-4);
                float coverage = smoothstep(edge - aa, edge + aa, d);

                float4 c = i.color;
                c.a *= coverage;
                return c;
            }
            ENDCG
        }
    }
}
