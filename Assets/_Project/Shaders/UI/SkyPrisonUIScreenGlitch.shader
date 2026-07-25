Shader "UI/SkyPrison/ScreenGlitch"
{
    // 专用于 RawImage：常驻横向扫描线 + 偶尔（低频率）出现的几处小块故障
    // （随机位置的小方块，不是贯穿整行的横条，位置每次触发都不固定）。
    // 结构照搬 SkyPrisonUIDesaturate.shader，保留 Unity UI 的裁剪/透明度行为。
    Properties
    {
        _MainTex          ("Texture", 2D)              = "white" {}
        [PerRendererData] _Color ("Tint", Color)        = (1,1,1,1)
        _ClipRect         ("Clip Rect", vector)          = (-32767,-32767,32767,32767)

        _ScanlineCount    ("扫描线密度", Range(40, 600))  = 240
        _ScanlineStrength ("扫描线强度", Range(0, 1))     = 0.25

        _GlitchInterval   ("故障检测间隔(秒)", Range(0.3, 6))    = 1.2
        _GlitchChance     ("故障触发概率", Range(0, 1))          = 0.25
        _GlitchDuration   ("单次故障持续占比", Range(0.02, 0.5)) = 0.10
        _PatchWidth       ("故障块宽度", Range(0.02, 0.4))       = 0.12
        _PatchHeight      ("故障块高度", Range(0.02, 0.4))       = 0.08
        _StripShiftAmount ("故障块横向跳动幅度", Range(0, 0.2))  = 0.04
        _RGBShiftAmount   ("故障块RGB分离幅度", Range(0, 0.05))  = 0.004
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
                float4 vertex      : SV_POSITION;
                fixed4 color       : COLOR;
                float2 uv          : TEXCOORD0;
                float4 worldPos    : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4    _Color;
            float4    _ClipRect;

            float _ScanlineCount;
            float _ScanlineStrength;
            float _GlitchInterval;
            float _GlitchChance;
            float _GlitchDuration;
            float _PatchWidth;
            float _PatchHeight;
            float _StripShiftAmount;
            float _RGBShiftAmount;

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

            float Rand(float seed)
            {
                return frac(sin(seed * 12.9898) * 43758.5453);
            }

            // 判断当前像素是否落在以 seed 为随机源、位置随机的一个小故障块内，
            // 命中的话返回该块的横向跳动方向(±1)，否则返回 0。
            float PatchShiftDir(float2 uv, float seed, float active)
            {
                float2 center = float2(Rand(seed), Rand(seed + 91.7));
                float inPatch = step(abs(uv.x - center.x), _PatchWidth  * 0.5)
                              * step(abs(uv.y - center.y), _PatchHeight * 0.5);
                float dir = Rand(seed + 173.3) > 0.5 ? 1.0 : -1.0;
                return inPatch * active * dir;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;

                // ── 低频触发：每隔 _GlitchInterval 秒判定一次是否要来几处小故障，
                // 触发后只在这一段时间开头的一小部分（_GlitchDuration 占比）里生效，
                // 大部分时间画面完全正常，符合"偶尔、别太频繁"的要求。
                float tickIndex = floor(_Time.y / _GlitchInterval);
                float tickPhase = frac(_Time.y / _GlitchInterval);
                float triggered = step(1.0 - _GlitchChance, Rand(tickIndex));
                float active    = triggered * step(tickPhase, _GlitchDuration);

                // 两处独立的随机小故障块，位置和方向每次触发都不一样，不固定在角落
                float shiftA = PatchShiftDir(uv, tickIndex * 3.7,  active);
                float shiftB = PatchShiftDir(uv, tickIndex * 9.1 + 500.0, active);
                float shift  = shiftA != 0.0 ? shiftA : shiftB;
                bool  inAnyPatch = shift != 0.0;

                fixed4 col;
                if (inAnyPatch)
                {
                    float2 uvSample = float2(uv.x + shift * _StripShiftAmount, uv.y);
                    fixed r = tex2D(_MainTex, uvSample + float2(_RGBShiftAmount, 0)).r;
                    fixed g = tex2D(_MainTex, uvSample).g;
                    fixed b = tex2D(_MainTex, uvSample - float2(_RGBShiftAmount, 0)).b;
                    fixed a = tex2D(_MainTex, uvSample).a;
                    col = fixed4(r, g, b, a) * i.color;
                }
                else
                {
                    col = tex2D(_MainTex, uv) * i.color;
                }

                // ── 常驻横向扫描线（电子屏幕质感）──────────────────────────────
                float scan = sin(uv.y * _ScanlineCount * 3.14159265) * 0.5 + 0.5;
                col.rgb *= 1.0 - _ScanlineStrength * (1.0 - scan) * 0.5;

                // Unity UI 软裁剪（Mask 组件等）
                col.a *= UnityGet2DClipping(i.worldPos.xy, _ClipRect);
                #ifdef UNITY_UI_ALPHACLIP
                clip(col.a - 0.001);
                #endif
                return col;
            }
            ENDCG
        }
    }
}
