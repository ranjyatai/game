Shader "UI/SkyPrison/BarNoise"
{
    // 细长条（血条灰色段）专用的颗粒噪点：横/竖颗粒数分开控制，明暗围绕基色上下浮动，
    // 不会像 TVStatic 那样在细条上被平均成一片暗色。颗粒数由脚本按"条的像素尺寸 / 颗粒大小"传入。
    Properties
    {
        _MainTex      ("Texture (未使用，保持 Unity UI 兼容)", 2D) = "white" {}
        [PerRendererData] _Color ("基色(与 Image 颜色相乘，默认白)", Color) = (1, 1, 1, 1)
        _ClipRect     ("Clip Rect", vector) = (-32767,-32767,32767,32767)

        _CountX       ("横向颗粒数", Float) = 120
        _CountY       ("纵向颗粒数", Float) = 6
        _NoiseSpeed   ("噪点刷新速度", Range(1, 60)) = 18
        _Contrast     ("明暗对比", Range(0, 1)) = 0.55
        _ClumpSize    ("成团大小(颗粒数)", Range(2, 30)) = 9
        _ClumpStrength("成团强度", Range(0, 1)) = 0.35
        _Skew         ("偏暗程度(越大亮点越少)", Range(1, 4)) = 1.0
        _TearChance   ("整行撕裂几率", Range(0, 0.5)) = 0.12
        // 透明缺口(uv 空间: xMin, yMin, xMax, yMax)，由脚本写入；(0,0,0,0)=无缺口。真正"挖掉"露出后面的底槽
        _Hole0 ("缺口0", Vector) = (0,0,0,0)
        _Hole1 ("缺口1", Vector) = (0,0,0,0)
        _Hole2 ("缺口2", Vector) = (0,0,0,0)
        _Hole3 ("缺口3", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        Cull Off  Lighting Off  ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float4 worldPos : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            fixed4 _Color;
            float4 _ClipRect;
            float _CountX;
            float _CountY;
            float _NoiseSpeed;
            float _Contrast;
            float _ClumpSize;
            float _ClumpStrength;
            float _Skew;
            float _TearChance;
            float4 _Hole0; float4 _Hole1; float4 _Hole2; float4 _Hole3;

            float InHole(float2 uv, float4 h)
            {
                return step(h.x, uv.x) * step(uv.x, h.z) * step(h.y, uv.y) * step(uv.y, h.w);
            }

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPos = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            float Rand(float2 co)
            {
                return frac(sin(dot(co, float2(12.9898, 78.233))) * 43758.5453);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float frame  = floor(_Time.y * _NoiseSpeed);
                float frame2 = floor(_Time.y * _NoiseSpeed * 0.35);   // 团块刷新得更慢

                float2 cell = floor(i.uv * float2(_CountX, _CountY));

                // 整行撕裂：偶尔某一行整体横向错位，像信号不稳
                float rowRand = Rand(float2(cell.y, frame * 3.7));
                float tear = step(1.0 - _TearChance, rowRand);
                cell.x += tear * floor(Rand(float2(cell.y + 11.0, frame)) * _CountX);

                // 细颗粒：整体偏暗，偶尔蹿出亮点
                float n = pow(Rand(cell + frame * 17.317), _Skew);

                // 团块：更大的明暗块，让颗粒聚成一团团亮区/暗区，不再均匀
                float2 clumpCell = floor(cell / float2(_ClumpSize, max(1.0, _ClumpSize * 0.25)));
                float clump = Rand(clumpCell + frame2 * 5.31);
                float clumpMul = lerp(1.0, lerp(0.35, 1.55, clump), _ClumpStrength);

                float lum = (1.0 + (n - 0.5) * 2.0 * _Contrast) * clumpMul;
                fixed4 result = fixed4(saturate(i.color.rgb * lum), i.color.a);
                float hole = saturate(InHole(i.uv, _Hole0) + InHole(i.uv, _Hole1) + InHole(i.uv, _Hole2) + InHole(i.uv, _Hole3));
                result.a *= (1.0 - hole);
                result.a *= UnityGet2DClipping(i.worldPos.xy, _ClipRect);
                #ifdef UNITY_UI_ALPHACLIP
                clip(result.a - 0.001);
                #endif
                return result;
            }
            ENDCG
        }
    }
}
