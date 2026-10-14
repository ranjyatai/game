// 战术地图的 3D 全息模型(参考：战术沙盘那种白色半透明的 X 光建筑模型)：
//   · 场景里真实的网格(建筑 / 墙 / 管道 / 台阶……)用这个材质 GPU 实例化重画进舞台相机；
//   · 加算混合 + 不写深度 + 不测深度：所有面叠加，能看穿楼体看到里面的结构；
//   · 面几乎透明，亮的是：菲涅尔轮廓、硬棱线(法线突变处)、地面细网格、一道上扫的光带，偶发整体闪烁；
//   · 只在「走过的格子」(_ExploredTex)里出现，边缘按多点采样的探索遮罩平滑渐隐(没有噪点溶解)。
// 顶点在「舞台」里：世界坐标 - 舞台原点 = 相对地图中心的米数(y 已按夸张倍数放大)；探索遮罩 / 俯视抓图的 uv 由它算出来。
// 输出 (rgb, a) 加算进带透明底的 RT；窗口那一侧用 SkyPrison/UI/Premultiplied 显示。
Shader "SkyPrison/MinimapHologram"
{
    Properties
    {
        _MainTex ("Top-down Capture", 2D) = "black" {}
        _ExploredTex ("Explored Mask", 2D) = "black" {}
        _Tint ("Glow Tint", Color) = (1, 1, 1, 1)
        _Alpha ("Overall Alpha", Range(0, 2)) = 1
        _HMax ("Height Range (stage units)", Float) = 25
        _GridStep ("Floor Grid Step (m)", Float) = 2
        _FloorH ("Floor Height Threshold (stage units)", Float) = 3
        _StageOrigin ("Stage Origin", Vector) = (0, -4000, 0, 0)
        _MapSize ("Map Size XZ (m)", Vector) = (400, 400, 0, 0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest Always
        Blend One One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 wpos : TEXCOORD0;
                float3 wnor : TEXCOORD1;
                float4 sp : TEXCOORD2;
            };

            sampler2D _MainTex;
            sampler2D _ExploredTex;
            float4 _ExploredTex_TexelSize;
            fixed4 _Tint;
            float _Alpha;
            float _HMax;
            float _GridStep;
            float _FloorH;
            float4 _StageOrigin;
            float4 _MapSize;

            v2f vert (appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.wnor = UnityObjectToWorldNormal(v.normal);
                o.sp = ComputeScreenPos(o.pos);
                return o;
            }

            // 探索遮罩：3×3 多点采样，等于把格子边缘再模糊一圈，渐隐才柔和
            float exploredSoft(float2 uv)
            {
                float2 t = _ExploredTex_TexelSize.xy * 1.6;
                float s = 0.0;
                s += tex2D(_ExploredTex, uv).r * 4.0;
                s += tex2D(_ExploredTex, uv + float2( t.x, 0)).r * 2.0;
                s += tex2D(_ExploredTex, uv + float2(-t.x, 0)).r * 2.0;
                s += tex2D(_ExploredTex, uv + float2(0,  t.y)).r * 2.0;
                s += tex2D(_ExploredTex, uv + float2(0, -t.y)).r * 2.0;
                s += tex2D(_ExploredTex, uv + float2( t.x,  t.y)).r;
                s += tex2D(_ExploredTex, uv + float2(-t.x,  t.y)).r;
                s += tex2D(_ExploredTex, uv + float2( t.x, -t.y)).r;
                s += tex2D(_ExploredTex, uv + float2(-t.x, -t.y)).r;
                return s / 16.0;
            }

            // 屏幕空间抗锯齿的细线：v 的整数位置上画一条约 px 个像素粗的线；线太密时淡出，避免摩尔纹
            float gl(float v, float px)
            {
                float fw = max(fwidth(v), 1e-5);
                float d = abs(frac(v - 0.5) - 0.5);
                float ln = 1.0 - smoothstep(0.0, px * fw, d);
                return ln * saturate(1.0 - fw * 2.2);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 lpos = i.wpos - _StageOrigin.xyz;                      // 相对地图中心(x,z 米)、相对地面(y 舞台单位)
                float2 uv = lpos.xz / max(_MapSize.xy, float2(1, 1)) + 0.5;
                float e = exploredSoft(uv);
                float edge = smoothstep(0.04, 0.55, e);                      // 探索边缘的柔和渐隐
                if (edge < 0.004) discard;

                float3 N = normalize(i.wnor);
                float3 V = normalize(_WorldSpaceCameraPos - i.wpos);
                // 轮廓和棱线做成「线」而不是光晕：用阈值判断，边界只有一两个像素的过渡，看着才利落
                // 轮廓光只留给「几乎贴着视线」的那一道窄边(阈值 0.82~0.97)：之前阈值太低，墙在中等斜角下整面都被点亮，加算叠加后偏亮 / 爆白
                float rim = smoothstep(0.82, 0.97, 1.0 - saturate(abs(dot(N, V))));
                // 水平面(地面 / 屋顶)干脆不吃轮廓光：镜头压低时整块地面都处在掠射角
                float flat = saturate(abs(N.y) * 1.6 - 0.2);
                rim *= (1.0 - flat);
                float crease = smoothstep(0.10, 0.26, length(fwidth(N)));                    // 硬棱线：法线在相邻像素间突变的地方(方正的建筑边角)
                float h01 = saturate(lpos.y / max(0.01, _HMax));
                float cap = dot(tex2D(_MainTex, uv).rgb, float3(0.3333, 0.3333, 0.3333));

                // 地面细网格：只画在靠近地面的朝上的面上
                float stp = max(0.5, _GridStep);
                float onFloor = step(lpos.y, _FloorH) * saturate(N.y * 2.0 - 0.6);
                float grid = (max(gl(lpos.x / stp, 1.1), gl(lpos.z / stp, 1.1)) * 0.6
                            + max(gl(lpos.x / (stp * 10.0), 1.5), gl(lpos.z / (stp * 10.0), 1.5)) * 0.4) * onFloor;

                // 扫描光带：随时间向上扫；偶发闪烁
                float band = saturate(1.0 - abs(h01 - frac(_Time.y * 0.11)) / 0.02);      // 上扫光带收窄(之前 0.06 太宽)
                float slot = floor(_Time.y * 14.0);
                float flick = (frac(sin(slot * 12.9898) * 43758.5453) > 0.92) ? 0.75 : 1.0;
                // 扫描细线压到几乎看不见(之前太明显，在画面里变成一条条横带，像压缩伪影)
                // 按真实的像素行算(i.pos.y 是屏幕像素坐标)：约 4 个像素一道细线，和分辨率无关；之前按屏幕高度的比例算，在高分辨率下变成十几个像素一道的粗横带
                float scan = 0.97 + 0.03 * sin(i.pos.y * 1.5708 + _Time.y * 4.0);
                // 胶片颗粒：屏幕空间的随机噪点，每秒换几十次，给 X 光那种脏脏的、有颗粒的质感
                float2 gp = floor(i.sp.xy / max(0.0001, i.sp.w) * 900.0);
                float grain = frac(sin(dot(gp + floor(_Time.y * 24.0), float2(12.9898, 78.233))) * 43758.5453);

                // 面压得更淡，线条 / 轮廓更亮：黑白反差更大(加算：多层叠在一起会越叠越亮，像 X 光)
                // 面要「始终看得见」：有一层托住体积的底光，竖直的墙面比水平的地面 / 屋顶亮一点，这样不管从哪个角度看，墙和地都分得出来
                float wall = 1.0 - saturate(abs(N.y));
                float face = (0.040 + 0.045 * h01 + 0.035 * saturate(cap * 2.2) + 0.055 * wall) * scan;
                float lineA = rim * 0.45 + crease * 1.05 + grid * 0.60 + band * 0.22;      // (注意：line 是 HLSL 保留字，不能当变量名)
                float a = (face + lineA) * edge * flick * _Alpha;
                a = (1.0 - exp(-a * 1.8)) * 0.72;                                          // 软限幅：低亮度基本线性，高亮度收敛到 ~0.9，单层不会爆白(多层叠加的 X 光效果仍然保留)
                a *= 0.78 + 0.44 * grain;

                // 纯灰度的白(不再往青蓝色拉)；Tint 默认就是白，想要冷色调可以在材质里调
                float3 col = _Tint.rgb;
                return fixed4(col * a, a * 0.7);
            }
            ENDCG
        }
    }
}
