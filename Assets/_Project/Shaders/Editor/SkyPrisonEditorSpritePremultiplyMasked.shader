Shader "Hidden/SkyPrison/EditorSpritePremultiplyMasked"
{
    Properties
    {
        _MainTex ("Main Texture", 2D) = "white" {}
        _MaskTex ("Mask Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
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
            fixed4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float2 maskUV : TEXCOORD1;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 maskUV : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                // C# 绘制命令已经用同一套 ModelViewport 坐标写入 TEXCOORD1：
                // GL.MultiTexCoord2(1, p.x / viewportWidth, 1 - p.y / viewportHeight)。
                // 这里不要再用 ComputeScreenPos，否则 Editor / Safe / BeginGroup / RT 输出会再次走一套屏幕坐标。
                o.maskUV = v.maskUV;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 src = tex2D(_MainTex, i.uv) * _Color;
                fixed4 m = tex2D(_MaskTex, saturate(i.maskUV));

                // MaskRT 写入阶段只关心参照图层的可见覆盖。不同 Unity/Shader 路径下
                // 有时 alpha 或 rgb 之一会被预乘/混合影响，所以这里取 rgba 中最大的覆盖值，
                // 避免“MaskRT 有颜色但 alpha 采不到”导致看起来完全没裁切。
                fixed maskAlpha = max(max(m.r, m.g), max(m.b, m.a));
                maskAlpha = saturate(maskAlpha);

                src.a *= maskAlpha;
                src.rgb *= src.a;
                return src;
            }
            ENDCG
        }
    }
}
