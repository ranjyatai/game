// 选关沙盘：鼠标指到据点时盖在模型上的冷绿色"扫描线 / 全息"叠加层（叠加混合，不遮挡原材质）。
// 由 SkyPrisonRaidMapScreen 在悬停的据点所有 MeshRenderer 上追加这个材质，离开后撤掉。
Shader "SkyPrison/Diorama/Hologram"
{
    Properties
    {
        _Color ("Tint", Color) = (0.25, 1.0, 0.68, 1)
        _Fade ("Fade", Range(0, 1)) = 0
        _Intensity ("Intensity", Range(0, 4)) = 1.6
        _ScanScale ("Scan Band Scale", Float) = 0.9
        _LineScale ("Fine Line Scale", Float) = 14
        _ScanSpeed ("Scan Speed", Float) = 0.7
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+20" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        ZTest LEqual
        Cull Back

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Fade, _Intensity, _ScanScale, _LineScale, _ScanSpeed;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 wpos : TEXCOORD0; float3 wnrm : TEXCOORD1; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.wnrm = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 vdir = normalize(_WorldSpaceCameraPos - i.wpos);
                float rim = pow(1.0 - saturate(dot(normalize(i.wnrm), vdir)), 2.2);            // 轮廓发光
                float y = i.wpos.y;                                                              // 沙盘放在 y≈-3000 处，取小数部分即可
                float band = frac(y * _ScanScale - _Time.y * _ScanSpeed);                       // 一条自下而上扫过的亮带
                band = pow(saturate(1.0 - band * 3.0), 2.0);
                float lines = step(0.55, frac(y * _LineScale));                                  // 细密的横向扫描线
                float flick = 0.92 + 0.08 * sin(_Time.y * 38.0);
                float3 c = _Color.rgb * (0.16 + rim * 1.1 + band * 1.0 + lines * 0.22) * _Intensity * flick;
                return fixed4(c * _Fade, 1);
            }
            ENDCG
        }
    }
}
