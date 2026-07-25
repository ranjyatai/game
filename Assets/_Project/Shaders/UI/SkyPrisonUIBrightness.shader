Shader "UI/SkyPrison/Brightness"
{
    // 亮度校准图预览专用：伽马曲线提亮暗部，_Brightness=1 时原样输出。
    Properties
    {
        _MainTex    ("Texture", 2D)       = "white" {}
        [PerRendererData] _Color ("Tint", Color) = (1,1,1,1)
        _ClipRect   ("Clip Rect", vector) = (-32767,-32767,32767,32767)
        _Brightness ("Brightness", Range(0.4, 2.5)) = 1
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
            fixed4    _Color;
            float4    _ClipRect;
            float     _Brightness;

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
                fixed4 col = tex2D(_MainTex, i.uv) * i.color;

                // 伽马提亮：_Brightness>1 把暗部往上拉（校准图的隐藏图案逐级浮现），
                // _Brightness<1 反向压暗。1.0 时 pow(x,1)=x，原样输出。
                col.rgb = pow(col.rgb, 1.0 / _Brightness);

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
