// 屏幕横带的字用的混合着色器：只取字形的 alpha(旧版 UI Text 的字体贴图)，混合方式可选。
//   _Mul = 0：加算(Blend SrcAlpha One)——黑底上叠红 / 绿 / 蓝三份字，重叠处是白
//   _Mul = 1：相乘(Blend DstColor Zero)——白底上叠青 / 品红 / 黄三份字，重叠处是黑
Shader "SkyPrison/UI/BlendText"
{
    Properties
    {
        [PerRendererData] _MainTex ("Font Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 1
        _Mul ("Multiply Mode (0/1)", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Cull Off Lighting Off ZWrite Off ZTest Always
        Blend [_SrcBlend] [_DstBlend]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };

            sampler2D _MainTex;
            fixed4 _Color;
            float _Mul;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float a = tex2D(_MainTex, i.uv).a * i.color.a;
                if (_Mul > 0.5) return fixed4(lerp(fixed3(1,1,1), i.color.rgb, a), 1);   // 相乘：字形处乘上色，别处乘 1(不变)
                return fixed4(i.color.rgb, a);                                           // 加算
            }
            ENDCG
        }
    }
}
