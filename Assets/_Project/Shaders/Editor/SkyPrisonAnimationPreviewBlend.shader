Shader "Hidden/SkyPrison/AnimationPreviewBlendV2"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Cull Off Lighting Off ZWrite Off ZTest Always

        CGINCLUDE
        #include "UnityCG.cginc"

        sampler2D _MainTex;
        float4 _MainTex_ST;
        fixed4 _Color;

        struct appdata
        {
            float4 vertex : POSITION;
            float2 uv : TEXCOORD0;
            fixed4 color : COLOR;
        };

        struct v2f
        {
            float4 vertex : SV_POSITION;
            float2 uv : TEXCOORD0;
            fixed4 color : COLOR;
        };

        v2f vert(appdata v)
        {
            v2f o;
            o.vertex = UnityObjectToClipPos(v.vertex);
            o.uv = TRANSFORM_TEX(v.uv, _MainTex);
            o.color = v.color * _Color;
            return o;
        }

        fixed4 frag(v2f i) : SV_Target
        {
            fixed4 c = tex2D(_MainTex, i.uv) * i.color;
            return c;
        }

        fixed4 fragDarkStrong(v2f i) : SV_Target
        {
            fixed4 c = tex2D(_MainTex, i.uv) * i.color;
            c.rgb *= 0.82;
            return c;
        }

        fixed4 fragLightStrong(v2f i) : SV_Target
        {
            fixed4 c = tex2D(_MainTex, i.uv) * i.color;
            c.rgb = saturate(c.rgb * 1.18);
            return c;
        }

        fixed4 fragOverlayPreview(v2f i) : SV_Target
        {
            fixed4 c = tex2D(_MainTex, i.uv) * i.color;
            c.rgb = saturate((c.rgb - 0.5) * 1.35 + 0.5);
            return c;
        }
        ENDCG

        // 0 Normal
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }

        // 1 Darken
        Pass
        {
            BlendOp Min
            Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }

        // 2 Multiply
        Pass
        {
            Blend DstColor OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }

        // 3 Color Burn - stronger dark preview
        Pass
        {
            Blend DstColor OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragDarkStrong
            ENDCG
        }

        // 4 Linear Burn - subtractive dark preview
        Pass
        {
            BlendOp RevSub
            Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragDarkStrong
            ENDCG
        }

        // 5 Lighten
        Pass
        {
            BlendOp Max
            Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }

        // 6 Screen
        Pass
        {
            Blend OneMinusDstColor One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }

        // 7 Color Dodge - stronger bright preview
        Pass
        {
            Blend OneMinusDstColor One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragLightStrong
            ENDCG
        }

        // 8 Overlay - preview approximation
        Pass
        {
            Blend DstColor OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragOverlayPreview
            ENDCG
        }

        // 9 Soft Light - soft overlay approximation
        Pass
        {
            Blend DstColor OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }

        // 10 Hard Light - stronger overlay approximation
        Pass
        {
            Blend DstColor SrcColor
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragOverlayPreview
            ENDCG
        }

        // 11 Difference - visible fixed-function approximation
        Pass
        {
            BlendOp RevSub
            Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }

        // 12 Exclusion - softer difference approximation
        Pass
        {
            BlendOp RevSub
            Blend One OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }

        // 13 Hue fallback: normal alpha until full RT color-component compositor is added.
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }

        // 14 Saturation fallback
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }

        // 15 Color fallback
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }

        // 16 Luminosity fallback
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }
    }
    Fallback Off
}
