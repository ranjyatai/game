// 标记的光柱 / 地面光环(加算)：
//   _Mode = 0：竖直光柱——中心亮、两侧淡，从底部向上渐隐，柱身有向上流动的条纹和轻微闪烁；
//   _Mode = 1：地面光环——两圈同心细环加一点内圈填充，缓慢呼吸。
// 小地图 3D 舞台里和游戏世界里共用(只是 _Intensity 不同)。
Shader "SkyPrison/MinimapBeam"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Float) = 1.3
        _Mode ("Mode (0 beam / 1 ring / 2 digit)", Float) = 0
        // 模板测试：立体数字(模式 2)由七段数码管的方块拼成，段与段在拐角处互相重叠(双面绘制又叠一层)，加算混合会让重叠处更亮。
        // 数字材质把比较设成 NotEqual + Replace，每个像素只画第一次；光柱 / 光环保持 Always + Keep，不受影响。
        _StencilRef ("Stencil Ref", Float) = 0
        _StencilComp ("Stencil Comp", Float) = 8
        _StencilPass ("Stencil Pass", Float) = 0
        _StencilMask ("Stencil Read/Write Mask", Float) = 255
    }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest LEqual
        Blend One One

        Pass
        {
            Stencil
            {
                Ref [_StencilRef]
                ReadMask [_StencilMask]
                WriteMask [_StencilMask]
                Comp [_StencilComp]
                Pass [_StencilPass]
            }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            fixed4 _Color;
            float _Intensity;
            float _Mode;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float a;
                if (_Mode > 1.5)
                {
                    // 立体数字：整体一层均匀的发光，带缓慢呼吸和偶发闪烁
                    float flick = 0.94 + 0.06 * sin(_Time.y * 17.0);
                    a = (0.78 + 0.22 * sin(_Time.y * 2.6)) * flick;
                }
                else if (_Mode < 0.5)
                {
                    float v = i.uv.y;
                    float x = abs(i.uv.x * 2.0 - 1.0);
                    float core = pow(saturate(1.0 - x), 2.2);
                    float fade = pow(saturate(1.0 - v), 1.25);
                    float start = smoothstep(0.0, 0.03, v);
                    float streak = 0.62 + 0.38 * sin((v * 9.0 - _Time.y * 1.7) * 6.2831);
                    float flick = 0.92 + 0.08 * sin(_Time.y * 21.0);
                    a = core * fade * start * streak * flick;
                }
                else
                {
                    float2 p = i.uv * 2.0 - 1.0;
                    float r = length(p);
                    float outer = 1.0 - smoothstep(0.0, 0.05, abs(r - 0.8));
                    float inner = 1.0 - smoothstep(0.0, 0.035, abs(r - 0.46));
                    float fillA = saturate(1.0 - r) * 0.22;
                    float pulse = 0.72 + 0.28 * sin(_Time.y * 3.0);
                    a = (outer + inner * 0.65 + fillA) * pulse * step(r, 1.0);
                }
                a *= _Intensity;
                return fixed4(_Color.rgb * a, a);
            }
            ENDCG
        }
    }
}
