Shader "SkyPrison/FogOfWarOverlay"
{
    Properties
    {
        _FogColor ("Fog Color", Color) = (0.35, 0.35, 0.35, 1.0)
        _VisibleColor ("Visible Color", Color) = (1, 1, 1, 1)
        _GlobalFogStrength ("Global Fog Strength", Range(0, 1)) = 0.62
        _SoftEdgeWidth ("Soft Edge Width", Range(0.01, 16.0)) = 3.5
        _AngleSoftness ("Angle Softness", Range(0.01, 45.0)) = 8.0
        _NoiseStrength ("Noise Strength", Range(0, 0.25)) = 0.035
        _NoiseScale ("Noise Scale", Range(0.1, 80.0)) = 18.0
        _NoiseSpeed ("Noise Speed", Range(0, 3.0)) = 0.12
        // 实验功能，默认关：用相机深度图还原像素的真实世界位置(解决 45° 俯视下墙立面被当成「墙后面」)。
        // 在现在的相机栈(Base + Overlay)里实测深度图对不上，打开会整片发暗，先别开。
        _WallFaceShift ("Wall Face Shift Scale (1 = true wall height)", Range(0, 2)) = 1
        _UnitBodyHeight ("Unit Body Height (m, exempt from wall shadow)", Range(0, 8)) = 3.5
        _UnitBodyHalfWidth ("Unit Body Half Width (m)", Range(0, 3)) = 1.0
        [Toggle] _UseDepthPos ("Use Depth World Position (experimental)", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+100"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "FogOfWarOverlay_Multiply"
            Cull Off
            ZWrite Off
            ZTest Always

            // Multiplicative darkening:
            // source RGB = 1 means no change, source RGB < 1 darkens the already rendered scene.
            // This preserves existing light/shadow contrast instead of covering it with a flat alpha color.
            Blend DstColor Zero

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"

            #define MAX_VISION_SOURCES 16

            fixed4 _FogColor;
            fixed4 _VisibleColor;
            half _GlobalFogStrength;
            half _SoftEdgeWidth;
            half _AngleSoftness;
            half _NoiseStrength;
            half _NoiseScale;
            half _NoiseSpeed;

            // 迷雾面片是一张贴在地面高度的平面。45° 俯视下，墙的立面 / 高处的东西在屏幕上盖着的是「它后面的地面」，
            // 按平面位置算视线就会把北墙立面也当成墙后面去变黑。用相机深度图把每个像素还原成它真正看到的那个表面的世界位置：
            // 表面点 = 相机 + (平面点 - 相机) * (表面的视深 / 平面点的视深)。
            sampler2D_float _CameraDepthTexture;
            half _UseDepthPos;

            // 视线扇形：每行一个视野源、每列一个方向(绕一圈)，值 = 该方向上被挡视线的墙挡在多远(米)；没挡住是 9999
            sampler2D _VisionLosTex;
            half _UseVisionLos;
            half _WallFaceShift;
            half _UnitBodyHeight;
            half _UnitBodyHalfWidth;

            #define LOS_RAYS 192.0      // 要和 SkyPrisonFogMaskRenderer.LosRays 一致

            static float g_losJitter = 0.0;      // 本像素的角度抖动(±半个采样间隔)，frag 里先算好

            // 手动线性插值：32 位浮点贴图在不少显卡上不支持线性过滤，会退化成最近邻，每条射线之间的距离一格一格跳，画面上就是一条条细条纹
            float2 SampleLos(float u, float v)
            {
                float x = u * LOS_RAYS - 0.5;
                float x0 = floor(x);
                float f = x - x0;
                float2 a = tex2Dlod(_VisionLosTex, float4((x0 + 0.5) / LOS_RAYS, v, 0, 0)).rg;
                float2 b = tex2Dlod(_VisionLosTex, float4((x0 + 1.5) / LOS_RAYS, v, 0, 0)).rg;
                // 相邻两条射线距离差很大 = 墙的边缘(一条量到墙、一条没量到)：不能插值，插出来的是一格里从近到远扫过去的「刺」，取离得近的那一格
                if (abs(a.x - b.x) > 1.5) return f < 0.5 ? a : b;
                return lerp(a, b, f);
            }
            half _WallRevealDepth;
            half _WallShadowSoftness;

            int _VisionSourceCount;
            float4 _VisionSourceOriginRadius[MAX_VISION_SOURCES];
            float4 _VisionSourceForwardAngle[MAX_VISION_SOURCES];
            float4 _VisionSourceFlags[MAX_VISION_SOURCES];
            float4 _VisionSourceLosOrigin[MAX_VISION_SOURCES];      // 视线射线的发射点(角色脚下)；视野圆本身仍然以 _VisionSourceOriginRadius 为中心

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            half HashNoise(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            half EvaluateSourceVisibility(float3 worldPos, int index)
            {
                float4 originRadius = _VisionSourceOriginRadius[index];
                float4 forwardAngle = _VisionSourceForwardAngle[index];
                float4 flags = _VisionSourceFlags[index];

                float3 origin = originRadius.xyz;
                float radius = max(originRadius.w, 0.0001);
                float softEdge = max(_SoftEdgeWidth, 0.0001);

                float3 delta = worldPos - origin;
                delta.y = 0.0;

                float distance = length(delta);
                if (distance > radius + softEdge)
                    return 0.0h;

                half radiusVisible = 1.0h - saturate((distance - radius) / softEdge);

                // 墙后面是黑的：这个方向上的挡视线距离 + 露出墙面的一点点之后，渐变到看不见
                // 迷雾平面贴在很低的高度(0.08)，45° 俯视下，比平面高 h 米的地面在屏幕上会被「推」到北边 h÷tan(俯角) 米处(站在 3.5 米高的高台上就偏 3.5 米)。
                // 先把像素位置按这个视野源脚下的高度还原回它真正所在的那层地面，再做墙遮挡 / 身体柱的判断。
                float3 camF3 = -UNITY_MATRIX_V[2].xyz;
                float2 camFwd = normalize(camF3.xz + float2(0.0001, 0.0));
                float cotPitch = length(camF3.xz) / max(-camF3.y, 0.05);
                float4 losOrg = _VisionSourceLosOrigin[index];
                float2 dl = worldPos.xz - losOrg.xz - camFwd * ((losOrg.y - worldPos.y) * cotPitch);      // 相对射线发射点(角色脚下)，不是相对视野圆中心(头顶锚点)
                if (_UseVisionLos > 0.5 && dot(dl, dl) > 1e-8)
                {
                    float u = atan2(dl.y, dl.x) / 6.2831853 + 0.5;
                    float v = (index + 0.5) / MAX_VISION_SOURCES;
                    // 在角度上多采几次再平均：墙的影子边缘是沿射线方向的一刀切，这样边缘会像真实的半影一样，离得越远越宽(角度固定 → 宽度和距离成正比)
                    float soft = max(_WallShadowSoftness, 0.01);
                    // 45° 俯视：墙的立面在屏幕上盖住的是「墙后面(屏幕上方)的地面」，所以屏幕上落在立面上的像素，它真正看到的是墙面，不是它盖着的那块地。
                    // 做法：这个像素方向上量到的墙高 H → 把像素位置沿相机朝向的反方向(往屏幕下方)挪 H ÷ tan(俯角)，再用挪后位置的方向 / 距离去判断视线。
                    //   · 朝北的墙：立面像素挪到墙前面 → 亮；立面再往后才是真正被挡住的地面 → 黑
                    //   · 东西向的墙：像素挪的是南北方向，仍然在墙的另一侧，射线照样被这堵墙挡住 → 黑，不会漏光(上一版把距离沿视野源方向缩短，会把东西向墙后面的地面救亮)
                    // 只有朝相机前方(屏幕上方)的像素才挪：往屏幕下方挪对「相机这一侧(南边)的墙」是挪向墙、还越过墙，会把墙前面本来亮的地面误判成墙后面
                    float facing = saturate(dot(dl / max(length(dl), 0.0001), camFwd));
                    half lit = 0.0h;
                    [unroll]
                    for (int k = -4; k <= 4; k++)
                    {
                        float uk = u + k * 0.0085 + g_losJitter;         // 0.0085 ≈ 3.1°：9 次采样一共盖 ±12°；g_losJitter 是逐像素的抖动，把 9 档台阶打散成细腻的渐变
                        float2 info = SampleLos(uk, v);
                        // 只挪已经在墙后面的像素(它们才是立面盖着的区域)；墙前面的像素本来就亮，挪一个墙高反而会越过别的墙被判成黑(玩家身边的三角黑块就是这样来的)
                        float behind = step(info.x, length(dl));
                        float2 pShift = dl - camFwd * (min(info.y * cotPitch, 40.0) * _WallFaceShift * facing * behind);
                        float dS = length(pShift);
                        float uS = atan2(pShift.y, pShift.x) / 6.2831853 + 0.5;
                        float2 info2 = (info.y > 0.0) ? SampleLos(uS + k * 0.0085 + g_losJitter, v) : info;
                        lit += (dS < 0.0001) ? 1.0h : (1.0h - saturate((dS - (info2.x + _WallRevealDepth)) / soft));
                    }
                    // 角色自己的「身体柱」：精灵是立着的，上半身的像素在屏幕上盖着的是他身后(屏幕上方)的地面。身后如果有墙，上半身就会落进墙影里被压暗；
                    // 所以脚下往屏幕上方 _UnitBodyHeight 米、左右各 _UnitBodyHalfWidth 米这一块，不做墙遮挡(只做半径)
                    float2 camRight = float2(camFwd.y, -camFwd.x);
                    float colLat = abs(dot(dl, camRight));
                    float colFwd = dot(dl, camFwd);
                    float inColumn = step(colLat, _UnitBodyHalfWidth) * step(-0.5, colFwd) * step(colFwd, _UnitBodyHeight * cotPitch);
                    radiusVisible *= lerp(lit / 9.0h, 1.0h, inColumn);
                }

                bool useCircle = flags.x > 0.5;
                bool useFacingDirection = flags.y > 0.5;

                if (useCircle || !useFacingDirection || distance <= 0.0001)
                    return radiusVisible;

                float3 forward = forwardAngle.xyz;
                forward.y = 0.0;
                forward = normalize(forward + float3(0.0001, 0.0, 0.0));

                float3 dir = normalize(delta);
                float dotValue = clamp(dot(forward, dir), -1.0, 1.0);
                float angleToTarget = degrees(acos(dotValue));
                float halfAngle = max(forwardAngle.w * 0.5, 0.0001);
                float angleSoftness = max(_AngleSoftness, 0.0001);

                half angleVisible = 1.0h - saturate((angleToTarget - halfAngle) / angleSoftness);
                return min(radiusVisible, angleVisible);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 surfacePos = i.worldPos;
                if (_UseDepthPos > 0.5)
                {
                    float2 suv = i.pos.xy / _ScreenParams.xy;
                    float raw = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, suv);
                    float surfEye = LinearEyeDepth(raw);
                    float planeEye = -mul(UNITY_MATRIX_V, float4(i.worldPos, 1.0)).z;
                    float ratio = clamp(surfEye / max(planeEye, 0.001), 0.05, 3.0);
                    surfacePos = _WorldSpaceCameraPos + (i.worldPos - _WorldSpaceCameraPos) * ratio;
                }
                i.worldPos = surfacePos;

                // 交错梯度噪声：每个像素一个 0~1 的值，用来抖动半影采样的角度
                g_losJitter = (frac(52.9829189 * frac(dot(i.pos.xy, float2(0.06711056, 0.00583715)))) - 0.5) * 0.0085;

                half visibility = 0.0h;

                [loop]
                for (int s = 0; s < MAX_VISION_SOURCES; s++)
                {
                    if (s >= _VisionSourceCount)
                        break;

                    visibility = max(visibility, EvaluateSourceVisibility(i.worldPos, s));

                    if (visibility >= 0.999h)
                        break;
                }

                visibility = saturate(visibility);

                half noise = HashNoise(i.worldPos.xz * _NoiseScale + _Time.y * _NoiseSpeed);
                half fogAmount = (1.0h - visibility) * _GlobalFogStrength;
                fogAmount += (noise - 0.5h) * _NoiseStrength * (1.0h - visibility);
                fogAmount = saturate(fogAmount);

                // _FogColor.rgb is used as the darkest multiplier in fully fogged pixels.
                // (1,1,1) = unchanged, lower value = darker. Shadow contrast is preserved.
                fixed3 clearMultiplier = fixed3(1.0, 1.0, 1.0);
                fixed3 fogMultiplier = saturate(_FogColor.rgb);
                fixed3 multiplier = lerp(clearMultiplier, fogMultiplier, fogAmount);

                return fixed4(multiplier, 1.0);
            }
            ENDCG
        }
    }

    FallBack Off
}
