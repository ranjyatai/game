Shader "UI/SkyPrison/DepartWarp"
{
    // 出发转场：截屏贴图的四周向中心凹陷（四边中点夹得最紧，和暂停菜单的「系统屏幕化」同一条曲线，只是强度放大很多），
    // _Strength 越大凹得越深、边缘越糊（径向模糊）、红蓝色差越开；_Collapse 把整张画面吸向中心并压黑，到 1 就是全黑。
    // 采样超出贴图范围的部分直接是黑，凹进去的边缘自然沉进黑里。
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Strength ("凹陷强度", Range(0, 1)) = 0
        _Collapse ("吸入中心", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Cull Off Lighting Off ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t { float4 vertex : POSITION; float4 color : COLOR; float2 texcoord : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            sampler2D _MainTex; float _Strength; float _Collapse;

            v2f vert(appdata_t v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o;
            }

            // 一次采样：越界 = 黑
            float3 Tap(float2 uv)
            {
                float2 inside = step(0.0, uv) * step(uv, 1.0);
                return tex2D(_MainTex, saturate(uv)).rgb * (inside.x * inside.y);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float dx = abs(uv.x - 0.5), dy = abs(uv.y - 0.5);
                // 和暂停菜单一样的半个余弦曲线：四边中点夹得最紧、往四个角上撇开。暂停用 0.075，这里最大到 0.6。
                float D = _Strength * 0.6;
                float2 p = uv - 0.5;
                p.x *= 1.0 + D * cos(dy * 6.2831853);
                p.y *= 1.0 + D * cos(dx * 6.2831853);
                // 吸入中心：采样范围越拉越大，画面缩成中间一小块再消失
                float c = _Collapse * _Collapse * _Collapse;
                p *= 1.0 + c * 40.0;

                // 边缘径向模糊 + 红蓝色差：离中心越远越明显，随强度变大
                float r = length(i.uv - 0.5) * 1.4142;
                float spread = _Strength * (0.02 + 0.10 * r * r);
                float ab = _Strength * 0.02 * (0.3 + r);
                float3 acc = 0.0;
                const int N = 6;
                for (int k = 0; k < N; k++)
                {
                    float t = (k / (N - 1.0)) * 2.0 - 1.0;
                    float s = 1.0 + t * spread;
                    acc.r += Tap(0.5 + p * s * (1.0 + ab)).r;
                    acc.g += Tap(0.5 + p * s).g;
                    acc.b += Tap(0.5 + p * s * (1.0 - ab)).b;
                }
                float3 col = acc / N;

                // 越凹越暗：边缘先沉下去，最后整体压黑
                col *= 1.0 - _Strength * 0.55 * smoothstep(0.15, 1.0, r);
                col *= 1.0 - smoothstep(0.75, 1.0, _Collapse);
                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
}
