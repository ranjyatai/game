// 选关沙盘地面的平面反射叠层：把"镜像相机"渲出来的画面按主相机的屏幕坐标采样，盖在地面上（按反射率与地面混合，倒影保持被映物体的原色）。
// 菲涅尔：俯视时较弱、掠射时强；_Blur 控制漆面 / 大理石的模糊程度，_Strength 总强度。
Shader "SkyPrison/Diorama/PlanarReflection"
{
    Properties
    {
        _ReflTex ("Reflection RT", 2D) = "black" {}
        _Tint ("Tint", Color) = (0.75, 0.88, 1.1, 1)
        _Strength ("Strength", Range(0, 3)) = 0.75
        _Blur ("Blur (texels)", Range(0, 8)) = 1.6
        _FresnelMin ("Fresnel Min (looking down)", Range(0, 1)) = 0.5
        _FresnelPow ("Fresnel Power", Range(0.5, 6)) = 2
        _EdgeFade ("Edge Fade (uv)", Range(0.001, 0.2)) = 0.04
    }
    SubShader
    {
        Tags { "Queue"="Transparent+1" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest LEqual Blend SrcAlpha OneMinusSrcAlpha
        Offset -1, -1

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _ReflTex; float4 _ReflTex_TexelSize;
            fixed4 _Tint; float _Strength, _Blur, _FresnelMin, _FresnelPow, _EdgeFade;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float4 sp : TEXCOORD0; float3 wp : TEXCOORD1; float2 uv : TEXCOORD2; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.sp = ComputeScreenPos(o.pos);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.sp.xy / i.sp.w;
                float2 px = _ReflTex_TexelSize.xy * _Blur;
                float3 c = tex2D(_ReflTex, uv).rgb * 0.25;
                c += (tex2D(_ReflTex, uv + float2( px.x, 0)).rgb + tex2D(_ReflTex, uv + float2(-px.x, 0)).rgb
                    + tex2D(_ReflTex, uv + float2(0,  px.y)).rgb + tex2D(_ReflTex, uv + float2(0, -px.y)).rgb) * 0.125;
                c += (tex2D(_ReflTex, uv + px).rgb + tex2D(_ReflTex, uv - px).rgb
                    + tex2D(_ReflTex, uv + float2(px.x, -px.y)).rgb + tex2D(_ReflTex, uv + float2(-px.x, px.y)).rgb) * 0.0625;

                float3 V = normalize(_WorldSpaceCameraPos - i.wp);
                float f = lerp(_FresnelMin, 1.0, pow(1.0 - saturate(V.y), _FresnelPow));
                float2 e = min(i.uv, 1.0 - i.uv);
                float edge = smoothstep(0.0, _EdgeFade, min(e.x, e.y));
                return fixed4(c * _Tint.rgb, saturate(_Strength * f * edge));   // 按反射率盖在地面上（不是加亮），倒影保持原色
            }
            ENDCG
        }
    }
}
