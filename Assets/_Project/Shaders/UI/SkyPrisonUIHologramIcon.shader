Shader "UI/SkyPrison/HologramIcon"
{
    // 快捷物品图标的全息投影观感：扫描线扫过 + 沿图标轮廓的边缘辉光 + 轻微闪烁。
    // 只在贴图自身的alpha范围内叠加效果（不会画出图标轮廓之外的东西），配合外层
    // RectMask2D 做"撑满槽位、多余部分裁掉"的效果，两者互不冲突。
    Properties
    {
        _MainTex        ("Texture", 2D)       = "white" {}
        [PerRendererData] _Color ("Tint", Color) = (1,1,1,1)
        _ClipRect       ("Clip Rect", vector) = (-32767,-32767,32767,32767)
        _ScanSpeed      ("Scan Speed", Range(0, 6))    = 1.6
        _ScanFrequency  ("Scan Frequency", Range(1, 40)) = 14
        _ScanIntensity  ("Scan Intensity", Range(0, 1))  = 0.28
        _RimColor       ("Rim Color", Color) = (0.85, 0.95, 1, 1)
        _RimPower       ("Rim Power", Range(0, 40)) = 10
        _FlickerSpeed   ("Flicker Speed", Range(0, 10)) = 2.2
        _FlickerAmount  ("Flicker Amount", Range(0, 0.3)) = 0.08
        _Brightness     ("Brightness Lift", Range(1, 3)) = 1.15
        _InnerGlowColor ("Inner Glow Color", Color) = (0.75, 0.92, 1, 1)
        _InnerGlowAmount("Inner Glow Amount", Range(0, 1)) = 0.1
        _Desaturate     ("Desaturate", Range(0, 1)) = 0
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
                float4 vertex : POSITION;
                float4 color  : COLOR;
                float2 uv     : TEXCOORD0;
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

            sampler2D _MainTex;
            float4    _MainTex_TexelSize;
            fixed4    _Color;
            float4    _ClipRect;
            float     _ScanSpeed;
            float     _ScanFrequency;
            float     _ScanIntensity;
            fixed4    _RimColor;
            float     _RimPower;
            float     _FlickerSpeed;
            float     _FlickerAmount;
            float     _Brightness;
            fixed4    _InnerGlowColor;
            float     _InnerGlowAmount;
            float     _Desaturate;

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
                fixed4 tex = tex2D(_MainTex, i.uv);
                fixed4 col = tex * i.color;

                // 单纯的"贴图颜色×冷白色调"是乘法混色，只会让暗部更暗，永远点不亮——但伽马
                // 提亮/加法辉光力度太大又会把中间调一起往亮处拉、压掉对比度，看着发灰发雾、
                // 没有实感。这里把力度调轻（_Brightness 从1.7降到1.15，_InnerGlowAmount 从
                // 0.35降到0.1），只是给暗部一点点提升，不整体拉平。
                col.rgb = pow(saturate(col.rgb), 1.0 / _Brightness);
                col.rgb += _InnerGlowColor.rgb * _InnerGlowColor.a * _InnerGlowAmount * tex.a;

                // 边缘辉光：贴图alpha在轮廓边界变化最快，用相邻像素alpha的梯度近似
                // "距离轮廓边缘有多近"，越靠近边缘越亮。
                float2 texel = _MainTex_TexelSize.xy;
                float aUp    = tex2D(_MainTex, i.uv + float2(0, texel.y)).a;
                float aDown  = tex2D(_MainTex, i.uv - float2(0, texel.y)).a;
                float aLeft  = tex2D(_MainTex, i.uv - float2(texel.x, 0)).a;
                float aRight = tex2D(_MainTex, i.uv + float2(texel.x, 0)).a;
                float edge = saturate((abs(aUp - aDown) + abs(aLeft - aRight)) * _RimPower);
                col.rgb += edge * _RimColor.rgb * _RimColor.a * tex.a;

                // 扫描线：沿UV.y方向滑动的亮带，只在图标轮廓内可见。
                float scan = sin(i.uv.y * _ScanFrequency - _Time.y * _ScanSpeed) * 0.5 + 0.5;
                scan = pow(scan, 3.0) * _ScanIntensity;
                col.rgb += scan * tex.a;

                // 整体轻微闪烁，全息投影不太稳定的观感。
                float flicker = 1.0 + sin(_Time.y * _FlickerSpeed) * _FlickerAmount;
                col.rgb *= flicker;

                // 冷却中：变灰。用两份材质切换（_Desaturate=0/1），不是每帧改同一份材质的
                // 参数——四个槽位共用同一个材质资产，改参数会一次性影响全部槽位，切换材质
                // 引用才能做到"只有正在冷却的那个槽位变灰"。
                float luminance = dot(col.rgb, float3(0.299, 0.587, 0.114));
                col.rgb = lerp(col.rgb, luminance.xxx, _Desaturate);

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
