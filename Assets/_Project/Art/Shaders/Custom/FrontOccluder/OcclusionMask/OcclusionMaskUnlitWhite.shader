Shader "Custom/FrontOccluder/OcclusionMask/UnlitWhite"
{
    Properties
    {
        _MainTex ("Alpha Source Texture", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.35
        _MaskColor ("Mask Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "Queue"="AlphaTest"
            "RenderType"="TransparentCutout"
            "IgnoreProjector"="True"
        }

        Pass
        {
            Name "SkyPrisonFrontOccluderMaskWriter"

            Cull Off
            ZWrite On
            ZTest LEqual

            // 旧链路必须输出颜色。不能改成 ColorMask 0。
            ColorMask RGBA

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Cutoff;
            fixed4 _MaskColor;

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
                fixed4 tex = tex2D(_MainTex, i.uv);

                // 透明贴图区域不写遮挡 Mask。
                clip(tex.a - _Cutoff);

                // 不透明代理体区域写白色 Mask，供 SpineOcclusionComposite 屏幕空间读取。
                return _MaskColor;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
