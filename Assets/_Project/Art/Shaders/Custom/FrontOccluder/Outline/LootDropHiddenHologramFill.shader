Shader "SkyPrison/LootDrop/HiddenHologramFill"
{
    // 2026-07-19：掉落物"被遮挡时"表现，跟角色同一套语言——不是描边，是全息点阵填充
    // （世界空间网格 + 周期性从下到上扫一条柔和横带），细节做法照搬
    // SpineOcclusionComposite.shader 里角色用的那套：填充不需要找轮廓边缘，天生不会有
    // "贴近的另一个单位描边线被盖住"这个问题。
    //
    // 混合方案（卡轮廓+叠加网格）：轮廓填充走正常预乘alpha混合，按渲染顺序正确遮挡
    // 背后的东西（跟角色贴近/掉落物贴近角色时能看出前后关系）；网格线/扫描横带叠加在
    // 轮廓之上，不计入 alpha，只加亮不参与遮挡判断。
    //
    // 渲染体只在被遮挡时激活（LootDropVisual 按 UnitOcclusionMaterialReceiver.
    // CurrentOccluded 开关）。

    Properties
    {
        _FillColor("Fill Color", Color) = (1, 1, 1, 1)
        _SilhouetteAlpha("Silhouette Alpha (occludes, controls front/back)", Range(0,1)) = 0
        _GlowAlpha("Glow Alpha (grid/scan, additive, no occlusion)", Range(0, 1)) = 0.25
        _GridDensity("Grid Density", Range(1, 40)) = 14
        _GridLineWidth("Grid Line Width", Range(0.01, 0.49)) = 0.08
        _GridBright("Grid Brightness", Range(0, 3)) = 0.6
        _CycleLength("Sweep Cycle Length (sec)", Range(1, 10)) = 4.0
        _SweepRangeY("Sweep Height Range (world units)", Range(0.5, 6)) = 2.0
        _TrailLength("Trail Fade Length (world units)", Range(0.05, 1.5)) = 0.3
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+60"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "HologramFill"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend One OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _FillColor;
            float _SilhouetteAlpha;
            float _GlowAlpha;
            float _GridDensity;
            float _GridLineWidth;
            float _GridBright;
            float _CycleLength;
            float _SweepRangeY;
            float _TrailLength;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float relativeY : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                // 相对自己根节点高度扫描，跟世界绝对坐标无关（掉落物悬浮/位置各不相同）
                o.relativeY = o.worldPos.y - unity_ObjectToWorld._m13;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 gridPos  = i.worldPos.xz * _GridDensity;
                float2 cellFrac = frac(gridPos);
                float2 toLine   = min(cellFrac, 1.0 - cellFrac);
                float  aa       = fwidth(min(toLine.x, toLine.y));
                float  gridMask = 1.0 - smoothstep(_GridLineWidth - aa, _GridLineWidth + aa, min(toLine.x, toLine.y));

                float cycleT = frac(_Time.y / _CycleLength);
                float sweepFade = smoothstep(0.0, 0.08, cycleT);
                float bandY = cycleT * _SweepRangeY;
                float dist  = i.relativeY - bandY;

                float front = 1.0 - smoothstep(0.0, 0.03, dist);
                float trail = 1.0 - smoothstep(0.0, _TrailLength, -dist);
                float waveMask = saturate(front * trail) * sweepFade;

                // 轮廓填充：正常alpha混合，负责前后遮挡关系
                float silA = _SilhouetteAlpha * _FillColor.a;
                // 网格+扫描：叠加发光，不参与遮挡
                float glowAdd = saturate(gridMask * _GridBright * 0.6 + waveMask) * _GlowAlpha * _FillColor.a;

                if (silA <= 0.001 && glowAdd <= 0.001)
                    discard;

                float3 outRgb = _FillColor.rgb * (silA + glowAdd * 0.6);
                return float4(outRgb, silA);
            }
            ENDCG
        }
    }
}
