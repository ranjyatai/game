Shader "UI/SkyPrison/Desaturate"
{
    // 专用于 RawImage：把纹理以灰度渲染，同时保留 Unity UI 的裁剪/透明度行为。
    Properties
    {
        _MainTex    ("Texture", 2D)       = "white" {}
        [PerRendererData] _Color ("Tint", Color) = (1,1,1,1)
        _Saturation ("Saturation", Range(0,1)) = 0
        _ClipRect   ("Clip Rect", vector) = (-32767,-32767,32767,32767)

        // 双色调（duotone）：灰度亮度按这个颜色重新上色，强度 0 时完全不影响原有的
        // 纯灰度效果（向下兼容，已有用法不用改）。用来做"冷绿荧光屏"这类单色终端质感，
        // 而不是死板的黑白。
        _DuotoneColor    ("双色调颜色", Color)       = (0.42, 0.92, 0.68, 1)
        _DuotoneStrength ("双色调强度", Range(0,1))  = 0

        // 常驻横向扫描线，强度 0 时完全不影响原有效果——电子屏幕质感，跟
        // SkyPrisonUIScreenGlitch 用的是同一套做法。
        _ScanlineCount    ("扫描线密度", Range(40, 600)) = 240
        _ScanlineStrength ("扫描线强度", Range(0, 1))    = 0

        // 电子屏幕抖动：不是偶尔冒一下的故障块，是整个画面持续存在的、幅度很小的
        // 左右抖动——按行给一点各自不同步的横向偏移，幅度 0 时完全不影响原有效果。
        _JitterStrength ("抖动幅度", Range(0, 0.01)) = 0
        _JitterSpeed    ("抖动速度", Range(0, 20))   = 6

        // Unity 内置的 _Time 是跟着 Time.timeScale 缩放的游戏时间，暂停时 timeScale=0，
        // 所有用 _Time 驱动的动画会当场冻结——这个背景本来就是暂停期间显示的，必须
        // 由 C# 每帧手动传一个不受 timeScale 影响的时间值进来，不能用内置 _Time。
        _UnscaledTime ("外部传入的不缩放时间", Float) = 0

        // 径向扭曲（暂停菜单"景深加强"效果）：以画面中心为基准往外推/往内收，
        // 越靠边缘扭曲越明显（越靠中心几乎不变形），强度 0 时完全不影响原有效果。
        // 叠加一个很慢的呼吸式脉动（±15%），不是死板的固定形变。
        _DistortionStrength   ("径向扭曲强度", Range(0, 0.3)) = 0
        _DistortionPulseSpeed ("扭曲呼吸速度", Range(0, 5))   = 0
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
            float     _Saturation;
            float4    _ClipRect;
            fixed4    _DuotoneColor;
            float     _DuotoneStrength;
            float     _ScanlineCount;
            float     _ScanlineStrength;
            float     _JitterStrength;
            float     _JitterSpeed;
            float     _UnscaledTime;
            float     _DistortionStrength;
            float     _DistortionPulseSpeed;

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

            fixed4 frag(v2f i) : SV_Target
            {
                // 电子屏幕抖动：全屏用同一个偏移量，看起来就是整张图当一整块刚体在挪，
                // 不像内容自己在噪。改成按横向条带分组（不是逐像素、也不是整屏一份），
                // 每个条带各自独立离散跳变——同一时刻不同条带的偏移量互不相同，视觉上
                // 才是"画面内部在乱"而不是"整张图在移动"。时间上仍然是离散跳变（不插值），
                // 短促、突然，不是连续正弦那种水波纹。幅度 0 时 wobble 恒为 0。
                float band = floor(i.uv.y * 40.0);
                float tick = floor(_UnscaledTime * _JitterSpeed);
                float wobble = frac(sin(dot(float2(tick, band), float2(12.9898, 78.233))) * 43758.5453) * 2.0 - 1.0;
                float2 uv = i.uv + float2(wobble * _JitterStrength, 0.0);

                // 四边对称夹紧：竖直线（左右边）在垂直中点夹得最紧、往上下角上撇开；
                // 横线（上下边）在水平中点夹得最紧、往左右角上撇开——两个方向对称叠加，
                // 不再只做单一方向。
                float  dx     = abs(uv.x - 0.5);
                float  dy     = abs(uv.y - 0.5);
                float  pulse  = 1.0 + sin(_UnscaledTime * _DistortionPulseSpeed) * 0.15;
                // 之前用线性的 (4d-1) 算强度，中间过零点是个硬拐角，弯出来的形状是尖的
                // "><"而不是圆润的")("。改用半个余弦周期——同样在 d=0 时取-1（夹紧）、
                // d=0.5 时取+1（撇开），但中间是平滑过渡，没有尖角。
                float  curveX = -cos(dy * 6.2831853);
                float  curveY = -cos(dx * 6.2831853);
                float  factorX = -_DistortionStrength * pulse * curveX;
                float  factorY = -_DistortionStrength * pulse * curveY;
                uv.x += (uv.x - 0.5) * factorX;
                uv.y += (uv.y - 0.5) * factorY;

                fixed4 col = tex2D(_MainTex, uv) * i.color;

                // 亮度灰度（Rec.601 权重，视觉上比均值更准）
                float lum  = dot(col.rgb, fixed3(0.299, 0.587, 0.114));
                fixed3 gray = fixed3(lum, lum, lum);

                // 双色调：灰度按亮度重新映射到 _DuotoneColor（暗部趋黑、亮部趋向目标色），
                // 强度 0 时完全跳过，纯灰度效果不受影响。
                fixed3 duotone = lerp(fixed3(0, 0, 0), _DuotoneColor.rgb, lum);
                gray = lerp(gray, duotone, _DuotoneStrength);

                col.rgb = lerp(gray, col.rgb, _Saturation);

                // 常驻横向扫描线（电子屏幕质感）——命中抖动块的区域，线条相位额外叠加
                // 同一个动态 wobble，扫描线边缘清晰，局部抖动在这上面才真正看得出来。
                float scanJitter = wobble * _JitterStrength * 60.0;
                float scan = sin((i.uv.y * _ScanlineCount + scanJitter) * 3.14159265) * 0.5 + 0.5;
                col.rgb *= 1.0 - _ScanlineStrength * (1.0 - scan) * 0.5;


                // 屏幕空间抖动：暗部渐变在 8-bit 下会显成一条条色阶断层，叠 ±~1 个码值的
                // 三角分布噪声把台阶打散（移动时的实时磨砂层尤其需要）。成本极低。
                float2 sp   = i.vertex.xy;
                float  n1   = frac(sin(dot(sp,        float2(12.9898, 78.233))) * 43758.5453);
                float  n2   = frac(sin(dot(sp + 7.31, float2(39.346, 11.135))) * 24634.6345);
                float  tri  = (n1 + n2) - 1.0;        // [-1,1] 三角分布，边缘更少
                col.rgb    += tri * (1.0 / 255.0);

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
