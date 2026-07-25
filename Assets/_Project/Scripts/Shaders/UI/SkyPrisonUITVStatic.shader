Shader "UI/SkyPrison/TVStatic"
{
    // 空存档预览用的"无信号雪花屏"：不依赖任何贴图输入，纯程序化噪点 + 常驻扫描线，
    // 跟 SkyPrisonUIScreenGlitch 保持同一套"电子屏幕质感"基调。
    Properties
    {
        _MainTex          ("Texture (未使用，占位保持 Unity UI 管线兼容)", 2D) = "white" {}
        [PerRendererData] _Color ("Tint", Color)        = (1,1,1,1)
        _ClipRect         ("Clip Rect", vector)          = (-32767,-32767,32767,32767)

        _NoiseScale       ("噪点颗粒密度", Range(50, 800))  = 260
        _NoiseSpeed       ("噪点刷新速度", Range(1, 60))    = 24
        _Brightness       ("整体亮度", Range(0, 1))         = 0.55
        _ScanlineStrength ("扫描线强度", Range(0, 1))       = 0.2

        _WiggleAmount     ("扭动幅度", Range(0, 0.3))       = 0.06
        _WiggleFreq       ("扭动纹理疏密", Range(1, 20))    = 6
        _WiggleSpeed      ("扭动速度", Range(0.2, 10))      = 2
    }
    SubShader
    {
        Tags
        {
            "Queue"           = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType"      = "Transparent"
            "PreviewType"     = "Plane"
        }
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

            struct appdata
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 uv       : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 uv       : TEXCOORD0;
                float4 worldPos : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4    _Color;
            float4    _ClipRect;

            float _NoiseScale;
            float _NoiseSpeed;
            float _Brightness;
            float _ScanlineStrength;

            float _WiggleAmount;
            float _WiggleFreq;
            float _WiggleSpeed;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPos = v.vertex;
                o.vertex   = UnityObjectToClipPos(v.vertex);
                o.uv       = v.uv;
                o.color    = v.color * _Color;
                return o;
            }

            float Rand(float2 co)
            {
                return frac(sin(dot(co, float2(12.9898, 78.233))) * 43758.5453);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // 扭动：用一层平滑流动的正弦场轻微"揉"一下采样坐标，让细颗粒噪点本身
                // 呈现出蠕动/扭曲感，而不是每帧原地闪烁的规整网格——之前加的低频块状
                // 明暗调制（大波浪）改变了整体明暗层次、而且插值网格本身太规则，看起来
                // 像斜纹布而不是雪花屏，这里换成"扭曲采样坐标"，颜色深浅完全不受影响。
                float2 wiggle = float2(
                    sin(i.uv.y * _WiggleFreq + _Time.y * _WiggleSpeed),
                    cos(i.uv.x * _WiggleFreq * 1.3 - _Time.y * _WiggleSpeed * 0.8)
                ) * _WiggleAmount;

                // 每帧按固定速度切一次噪点格子（不是连续插值），才有经典"雪花"的跳变感，
                // 而不是平滑漂移的柔和噪声。
                float frame = floor(_Time.y * _NoiseSpeed);
                float2 grid = floor((i.uv + wiggle) * _NoiseScale);
                float n = Rand(grid + frame * 17.317);

                fixed3 col = fixed3(n, n, n) * _Brightness;

                // 常驻横向扫描线，跟其他电子屏幕特效保持一致的质感
                float scan = sin(i.uv.y * 240 * 3.14159265) * 0.5 + 0.5;
                col *= 1.0 - _ScanlineStrength * (1.0 - scan) * 0.5;

                fixed4 result = fixed4(col, 1) * i.color;
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
