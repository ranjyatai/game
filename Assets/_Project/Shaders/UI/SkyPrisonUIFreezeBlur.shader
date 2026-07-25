Shader "Hidden/SkyPrison/UIFreezeBlur"
{
    // Graphics.Blit 专用：CGPROGRAM + vert_img + UnityCG.cginc
    // 循环展开，避免 SM3 动态数组索引产生的全黑输出。
    Properties
    {
        _MainTex          ("", 2D)    = "" {}
        _UIBlur_TexelSizeX("", Float) = 0
        _UIBlur_TexelSizeY("", Float) = 0
        _UIBlur_Radius    ("", Float) = 1.0
    }
    SubShader
    {
        ZTest Always  ZWrite Off  Cull Off  Blend Off

        CGINCLUDE
        #include "UnityCG.cginc"

        sampler2D _MainTex;
        float _UIBlur_TexelSizeX;
        float _UIBlur_TexelSizeY;
        float _UIBlur_Radius;

        // Gaussian weights (sigma≈1.0, 9-tap)
        // W0=0.2270 W1=0.1946 W2=0.1216 W3=0.0541 W4=0.0162
        #define W0 0.2270270270
        #define W1 0.1945945946
        #define W2 0.1216216216
        #define W3 0.0540540541
        #define W4 0.0162162162

        fixed4 fragCopy(v2f_img i) : SV_Target
        {
            return tex2D(_MainTex, i.uv);
        }

        fixed4 fragH(v2f_img i) : SV_Target
        {
            float2 d = float2(_UIBlur_TexelSizeX * _UIBlur_Radius, 0.0);
            fixed4 c  = tex2D(_MainTex, i.uv          ) * W0;
            c        += tex2D(_MainTex, i.uv + d      ) * W1;
            c        += tex2D(_MainTex, i.uv - d      ) * W1;
            c        += tex2D(_MainTex, i.uv + d * 2.0) * W2;
            c        += tex2D(_MainTex, i.uv - d * 2.0) * W2;
            c        += tex2D(_MainTex, i.uv + d * 3.0) * W3;
            c        += tex2D(_MainTex, i.uv - d * 3.0) * W3;
            c        += tex2D(_MainTex, i.uv + d * 4.0) * W4;
            c        += tex2D(_MainTex, i.uv - d * 4.0) * W4;
            return c;
        }

        fixed4 fragV(v2f_img i) : SV_Target
        {
            float2 d = float2(0.0, _UIBlur_TexelSizeY * _UIBlur_Radius);
            fixed4 c  = tex2D(_MainTex, i.uv          ) * W0;
            c        += tex2D(_MainTex, i.uv + d      ) * W1;
            c        += tex2D(_MainTex, i.uv - d      ) * W1;
            c        += tex2D(_MainTex, i.uv + d * 2.0) * W2;
            c        += tex2D(_MainTex, i.uv - d * 2.0) * W2;
            c        += tex2D(_MainTex, i.uv + d * 3.0) * W3;
            c        += tex2D(_MainTex, i.uv - d * 3.0) * W3;
            c        += tex2D(_MainTex, i.uv + d * 4.0) * W4;
            c        += tex2D(_MainTex, i.uv - d * 4.0) * W4;
            return c;
        }
        ENDCG

        Pass { CGPROGRAM #pragma vertex vert_img #pragma fragment fragCopy ENDCG }
        Pass { CGPROGRAM #pragma vertex vert_img #pragma fragment fragH    ENDCG }
        Pass { CGPROGRAM #pragma vertex vert_img #pragma fragment fragV    ENDCG }
    }
}
