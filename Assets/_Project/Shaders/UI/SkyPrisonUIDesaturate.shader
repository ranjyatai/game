Shader "UI/SkyPrison/Desaturate"
{
    // 专用于 RawImage：把纹理以灰度渲染，同时保留 Unity UI 的裁剪/透明度行为。
    Properties
    {
        _MainTex    ("Texture", 2D)       = "white" {}
        [PerRendererData] _Color ("Tint", Color) = (1,1,1,1)
        _Saturation ("Saturation", Range(0,1)) = 0
        // 对比度：以 _ContrastPivot 为中心拉开明暗，默认 1 = 完全不影响(向下兼容)。实时磨砂窗口用它把「亮度 0.5 的一片灰」里的轮廓拉出来
        _Contrast ("Contrast", Range(0.5,4)) = 1
        _ContrastPivot ("Contrast Pivot", Range(0,1)) = 0.5
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
        _ScanlineScrollSpeed ("扫描线滚动速度", Range(-5, 5)) = 0.12

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

        // 径向扭曲会把边缘附近的UV往外推，推到贴图[0,1]范围之外——纹理wrapMode是
        // Clamp的话，超界部分会被钳在最边缘那一列像素上，如果那一列正好挨着场景里
        // 对比强烈的物体(比如集装箱)，钳出来的画面会显得像"撕裂了一道口子露出别的
        // 东西"，边界还会因为扭曲幅度按行变化而参差不齐。真正的解法不是把扭曲调
        // 弱(那样会削弱效果本身)，是让实际显示的画面本来就用贴图中间裁小一圈的
        // 区域(=1时不裁，向下兼容)，把扭曲需要的"往外推"空间提前让出来，扭曲
        // 再怎么推也推不到贴图真正的边界。1=不裁切(向下兼容，已有用法不用改)。
        _EdgeSafetyZoom ("边缘扭曲安全裁切(1=不裁)", Range(0.5, 1)) = 1

        // 水平单边淡出：从这个UV.x开始往右淡到透明，1=不淡出(向下兼容，已有用法
        // 不用改，共享的全局材质实例不会受影响)。用来给磨砂背景做"左侧干净、
        // 右侧暗淡淡出"的条状底色，不用另外叠一张遮罩贴图。
        _HorizontalFadeStart ("水平淡出起点(UV.x)，1=不淡出", Range(0,1)) = 1
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
            float     _Contrast;
            float     _ContrastPivot;
            float4    _ClipRect;
            fixed4    _DuotoneColor;
            float     _DuotoneStrength;
            float     _ScanlineCount;
            float     _ScanlineStrength;
            float     _ScanlineScrollSpeed;
            float     _JitterStrength;
            float     _JitterSpeed;
            float     _UnscaledTime;
            float     _DistortionStrength;
            float     _DistortionPulseSpeed;
            float     _HorizontalFadeStart;
            float     _EdgeSafetyZoom;

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

                // 先按安全裁切量把UV往中心收一圈，把"扭曲需要往外推"的空间提前让出来——
                // 必须放在径向扭曲计算之前，扭曲是在这一步产出的uv基础上再往外推的，
                // 顺序反了起不到"提前留出安全边距"的作用。=1时(_EdgeSafetyZoom默认值)
                // 这一步完全是恒等变换，不影响没设这个属性的其它调用方(设置/存档窗口)。
                uv = (uv - 0.5) * _EdgeSafetyZoom + 0.5;

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
                // 此时 col 已经乘过顶点色(窗口把 blurImage.color 压到 0.5 之类的亮度)，对比度中心也要跟着乘这个亮度，
                // 否则中心(画面平均亮度，没压暗)比被压暗的像素高得多，整块会被一路拉成纯黑。tint=1 时跟原来完全一样。
                float tint = dot(i.color.rgb, float3(0.299, 0.587, 0.114));
                float pivot = _ContrastPivot * tint;
                col.rgb = saturate((col.rgb - pivot) * _Contrast + pivot);

                // 常驻横向扫描线（电子屏幕质感）——命中抖动块的区域，线条相位额外叠加
                // 同一个动态 wobble，扫描线边缘清晰，局部抖动在这上面才真正看得出来。
                float scanJitter = wobble * _JitterStrength * 60.0;
                float scanY = i.uv.y - _UnscaledTime * _ScanlineScrollSpeed;
                float scan = sin((scanY * _ScanlineCount + scanJitter) * 3.14159265) * 0.5 + 0.5;
                col.rgb *= 1.0 - _ScanlineStrength * (1.0 - scan) * 0.5;


                // 屏幕空间抖动：暗部渐变在 8-bit 下会显成一条条色阶断层，叠 ±~1 个码值的
                // 三角分布噪声把台阶打散（移动时的实时磨砂层尤其需要）。成本极低。
                float2 sp   = i.vertex.xy;
                float  n1   = frac(sin(dot(sp,        float2(12.9898, 78.233))) * 43758.5453);
                float  n2   = frac(sin(dot(sp + 7.31, float2(39.346, 11.135))) * 24634.6345);
                float  tri  = (n1 + n2) - 1.0;        // [-1,1] 三角分布，边缘更少
                col.rgb    += tri * (1.0 / 255.0);

                // 水平单边淡出：edge0 卡到最高 0.999，避免 _HorizontalFadeStart=1 时
                // smoothstep(1,1,x) 两个边界相等触发除零/未定义行为——钳到 0.999 之后
                // 默认值实际效果依然是"几乎不淡出"，向下兼容不影响其它共享这份材质的窗口。
                float fadeStart = min(_HorizontalFadeStart, 0.999);
                float fade = 1.0 - smoothstep(fadeStart, 1.0, uv.x);
                col.a *= fade;

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
