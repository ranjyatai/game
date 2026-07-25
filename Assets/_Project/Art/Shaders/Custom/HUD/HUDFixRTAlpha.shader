Shader "Hidden/SkyPrison/HUDFixRTAlpha"
{
    SubShader
    {
        Tags { "RenderType"="Transparent" }
        ZTest Always ZWrite Off Cull Off
        Blend Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;

            fixed4 frag(v2f_img i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);
                // URP 17 把 clear 背景的 alpha 写成 1（不是 0）。
                // clear 后背景 RGB 精确等于 0；最暗的 HUD 元素（5.5% alpha 的槽背景）RGB ≈ 0.044。
                // 用 0.005 阶跃函数：RGB≈0 → alpha=0（透明），有内容 → alpha=1（不透明）。
                // URP 17 bug: clear background gets alpha=1 instead of alpha=0.
                // Fix: background pixels (RGB≈0) → alpha=0.
                // Content pixels → keep original alpha (preserves semi-transparency).
                float lum = max(max(col.r, col.g), col.b);
                col.a = lum < 0.005 ? 0.0 : col.a;
                return col;
            }
            ENDCG
        }
    }
}
