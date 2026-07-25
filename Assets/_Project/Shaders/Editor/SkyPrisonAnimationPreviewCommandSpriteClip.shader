Shader "Hidden/SkyPrison/AnimationPreview/CommandSpriteClip"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        _ViewportSize ("Viewport Size", Vector) = (1,1,0,0)
        _SpriteCenter ("Sprite Center", Vector) = (0,0,0,0)
        _SpriteSize ("Sprite Size", Vector) = (1,1,0,0)
        _SpriteUV ("Sprite UV", Vector) = (0,0,1,1)
        _SpriteAngle ("Sprite Angle", Float) = 0
        _SpriteMirrored ("Sprite Mirrored", Float) = 0
        _FlipViewportY ("Flip Viewport Y", Float) = 1
        _FlipSpriteV ("Flip Sprite V", Float) = 1
        _EditorGammaFix ("Editor Gamma Fix", Float) = 1
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4 _Tint;
            float4 _ViewportSize;
            float4 _SpriteCenter;
            float4 _SpriteSize;
            float4 _SpriteUV;
            float _SpriteAngle;
            float _SpriteMirrored;
            float _FlipViewportY;
            float _FlipSpriteV;
            float _EditorGammaFix;

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
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Unity IMGUI/Graphics.DrawTexture and sprite texture sampling use opposite Y conventions here.
                // Stage 27-L: treat the submitted quad as an editor viewport in top-left GUI space.
                float2 viewportUv = i.uv;
                if (_FlipViewportY > 0.5)
                    viewportUv.y = 1.0 - viewportUv.y;

                float2 p = viewportUv * max(_ViewportSize.xy, float2(1.0, 1.0));
                float2 q = p - _SpriteCenter.xy;

                float a = radians(_SpriteMirrored > 0.5 ? -_SpriteAngle : _SpriteAngle);
                float s = sin(-a);
                float c = cos(-a);
                q = float2(c * q.x - s * q.y, s * q.x + c * q.y);

                if (_SpriteMirrored > 0.5)
                    q.x = -q.x;

                float2 local01 = q / max(_SpriteSize.xy, float2(0.0001, 0.0001)) + 0.5;

                clip(local01.x);
                clip(local01.y);
                clip(1.0 - local01.x);
                clip(1.0 - local01.y);

                float2 sprite01 = local01;
                if (_FlipSpriteV > 0.5)
                    sprite01.y = 1.0 - sprite01.y;

                float2 suv = _SpriteUV.xy + sprite01 * _SpriteUV.zw;
                fixed4 col = tex2D(_MainTex, suv) * _Tint;

                // Match the old editor GUI texture path more closely in Linear color projects.
                // Without this, the command shader path can look too harsh/high-contrast compared with Full/IMGUI.
                #ifndef UNITY_COLORSPACE_GAMMA
                if (_EditorGammaFix > 0.5)
                    col.rgb = LinearToGammaSpace(col.rgb);
                #endif

                clip(col.a - 0.001);
                return col;
            }
            ENDCG
        }
    }
}
