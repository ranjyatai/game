Shader "Hidden/SkyPrison/UIBlurBlit"
{
    // 专门给 Graphics.Blit 使用的高斯模糊 shader。
    // 使用标准顶点 UV，不依赖 Blit.hlsl 的 vertex-ID 全屏三角。
    Properties
    {
        _MainTex         ("Source", 2D)     = "white" {}
        _UIBlur_TexelSize("Texel Size", Vector) = (0,0,0,0)
        _UIBlur_Radius   ("Radius", Float)  = 2.5
    }
    SubShader
    {
        ZTest Always  ZWrite Off  Cull Off  Blend Off

        HLSLINCLUDE
        #include "UnityCG.cginc"

        sampler2D _MainTex;
        float2    _UIBlur_TexelSize;
        float     _UIBlur_Radius;

        static const float W[5] = {
            0.2270270270, 0.1945945946, 0.1216216216, 0.0540540541, 0.0162162162
        };

        struct Attributes { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
        struct Varyings   { float4 pos    : SV_POSITION; float2 uv : TEXCOORD0; };

        Varyings Vert(Attributes IN)
        {
            Varyings o;
            o.pos = UnityObjectToClipPos(IN.vertex);
            o.uv  = IN.uv;
            return o;
        }

        float4 FragDownsample(Varyings IN) : SV_Target
        {
            return tex2D(_MainTex, IN.uv);
        }

        float4 FragBlurH(Varyings IN) : SV_Target
        {
            float2 uv  = IN.uv;
            float2 off = float2(_UIBlur_TexelSize.x * _UIBlur_Radius, 0.0);
            float4 col = tex2D(_MainTex, uv) * W[0];
            for (int i = 1; i < 5; i++)
            {
                col += tex2D(_MainTex, uv + off * i) * W[i];
                col += tex2D(_MainTex, uv - off * i) * W[i];
            }
            return col;
        }

        float4 FragBlurV(Varyings IN) : SV_Target
        {
            float2 uv  = IN.uv;
            float2 off = float2(0.0, _UIBlur_TexelSize.y * _UIBlur_Radius);
            float4 col = tex2D(_MainTex, uv) * W[0];
            for (int i = 1; i < 5; i++)
            {
                col += tex2D(_MainTex, uv + off * i) * W[i];
                col += tex2D(_MainTex, uv - off * i) * W[i];
            }
            return col;
        }
        ENDHLSL

        Pass { Name "Downsample" HLSLPROGRAM #pragma vertex Vert #pragma fragment FragDownsample ENDHLSL }
        Pass { Name "BlurH"      HLSLPROGRAM #pragma vertex Vert #pragma fragment FragBlurH      ENDHLSL }
        Pass { Name "BlurV"      HLSLPROGRAM #pragma vertex Vert #pragma fragment FragBlurV      ENDHLSL }
    }
}
