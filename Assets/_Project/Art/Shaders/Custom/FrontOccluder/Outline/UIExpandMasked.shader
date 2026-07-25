Shader "Custom/FrontOccluder/Outline/UIExpandMasked"
{
    // V6 - 2026-05-21
    // Fix: fail closed in production and make debug previews alpha-safe.
    // Fix: prevent Spine attachment seams / internal alpha holes from being treated as outline.
    // Method: subtract a slightly dilated silhouette blocker from the expanded hidden region.
    // This keeps the outside occlusion outline, while sealing small internal gaps caused by Spine pieces.

    Properties
    {
        [NoScaleOffset] _MainTex("Silhouette RT", 2D) = "black" {}
        [NoScaleOffset] _MaskTex("Unit Hidden Mask RT", 2D) = "black" {}
        [NoScaleOffset] _GateTex("Occluded Gate RT", 2D) = "black" {}

        _OutlineColor("Outline Color", Color) = (1, 0.92, 0.15, 1)
        _OccludedOutlineWidth("Occluded Outline Width", Range(1, 32)) = 8
        _OuterOutlineWidth("Outer Outline Width", Range(0, 16)) = 0

        // Seals tiny internal cracks in Spine silhouettes before outline extraction.
        // 0 = old behavior. 1~3 usually removes attachment seams. Higher values may erase narrow intentional gaps.
        _SilhouetteSealPixels("Silhouette Seal Pixels", Range(0, 8)) = 2

        [Toggle] _UseGate("Use Gate", Float) = 1
        _MaskThreshold("Mask Threshold", Range(0,1)) = 0.5
        _GateThreshold("Gate Threshold", Range(0,1)) = 0.5
        _DebugMode("Debug Mode", Float) = 0
        // 0 Final / 1 Silhouette / 2 UnitHiddenMask / 3 Gate / 4 Hidden / 5 HiddenExpanded / 6 HiddenOutline / 7 OuterOutline / 8 SilhouetteBlocker
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="False"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "UIExpandMasked"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            sampler2D _MaskTex;
            sampler2D _GateTex;

            float4 _OutlineColor;
            float _OccludedOutlineWidth;
            float _OuterOutlineWidth;
            float _SilhouetteSealPixels;
            float _UseGate;
            float _MaskThreshold;
            float _GateThreshold;
            float _DebugMode;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                return o;
            }

            half SampleSilhouette(float2 uv)
            {
                return tex2D(_MainTex, uv).a;
            }

            // _MaskTex is the unit-level hidden mask already composed as:
            // UnitSilhouette ∩ UnitAuthorizedOccluderMask.
            half SampleUnitHiddenMask01(float2 uv)
            {
                return step(_MaskThreshold, tex2D(_MaskTex, uv).r);
            }

            half SampleGate01(float2 uv)
            {
                return (_UseGate > 0.5) ? step(_GateThreshold, tex2D(_GateTex, uv).r) : 1.0h;
            }

            half SampleHidden(float2 uv)
            {
                half s = SampleSilhouette(uv);
                half m = SampleUnitHiddenMask01(uv);
                half g = SampleGate01(uv);
                return s * m * g;
            }

            float2 Dir16(int idx)
            {
                if (idx == 0)  return normalize(float2( 1.0,  0.0));
                if (idx == 1)  return normalize(float2(-1.0,  0.0));
                if (idx == 2)  return normalize(float2( 0.0,  1.0));
                if (idx == 3)  return normalize(float2( 0.0, -1.0));
                if (idx == 4)  return normalize(float2( 1.0,  1.0));
                if (idx == 5)  return normalize(float2(-1.0,  1.0));
                if (idx == 6)  return normalize(float2( 1.0, -1.0));
                if (idx == 7)  return normalize(float2(-1.0, -1.0));
                if (idx == 8)  return normalize(float2( 1.0,  0.5));
                if (idx == 9)  return normalize(float2( 0.5,  1.0));
                if (idx == 10) return normalize(float2(-1.0,  0.5));
                if (idx == 11) return normalize(float2(-0.5,  1.0));
                if (idx == 12) return normalize(float2( 1.0, -0.5));
                if (idx == 13) return normalize(float2( 0.5, -1.0));
                if (idx == 14) return normalize(float2(-1.0, -0.5));
                return              normalize(float2(-0.5, -1.0));
            }

            half DilateSilhouette16(float2 uv, int maxRadius)
            {
                half expanded = 0.0h;
                float2 texel = _MainTex_TexelSize.xy;
                [unroll]
                for (int r = 1; r <= 32; r++)
                {
                    if (r > maxRadius) break;
                    [unroll]
                    for (int k = 0; k < 16; k++)
                    {
                        expanded = max(expanded, SampleSilhouette(uv + Dir16(k) * texel * r));
                    }
                }
                return expanded;
            }

            half DilateSilhouette16WithCenter(float2 uv, int maxRadius)
            {
                half expanded = SampleSilhouette(uv);
                float2 texel = _MainTex_TexelSize.xy;
                [unroll]
                for (int r = 1; r <= 8; r++)
                {
                    if (r > maxRadius) break;
                    [unroll]
                    for (int k = 0; k < 16; k++)
                    {
                        expanded = max(expanded, SampleSilhouette(uv + Dir16(k) * texel * r));
                    }
                }
                return expanded;
            }

            half DilateHidden16(float2 uv, int maxRadius)
            {
                half expanded = 0.0h;
                float2 texel = _MainTex_TexelSize.xy;
                [unroll]
                for (int r = 1; r <= 40; r++)
                {
                    if (r > maxRadius) break;
                    [unroll]
                    for (int k = 0; k < 16; k++)
                    {
                        expanded = max(expanded, SampleHidden(uv + Dir16(k) * texel * r));
                    }
                }
                return expanded;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                half silhouette = SampleSilhouette(i.uv);
                half unitHiddenMask = SampleUnitHiddenMask01(i.uv);
                half gate = SampleGate01(i.uv);

                int sealPixels = (int)round(saturate(_SilhouetteSealPixels / 8.0) * 8.0);

                // This is the key difference from V4:
                // V4 did: hiddenOutline = hiddenExpanded - raw silhouette.
                // If raw silhouette has tiny internal alpha holes from Spine attachments, those holes become outline.
                // V5 subtracts a sealed/dilated silhouette blocker, so small internal cracks are treated as inside the unit.
                half silhouetteBlocker = (sealPixels > 0)
                    ? DilateSilhouette16WithCenter(i.uv, sealPixels)
                    : silhouette;

                half hidden = silhouette * unitHiddenMask * gate;

                int hiddenRadius = (int)round(_OccludedOutlineWidth + sealPixels);
                half hiddenExpanded = DilateHidden16(i.uv, hiddenRadius);

                // UnitHiddenMask is already clipped by each unit silhouette before four-channel integration.
                // Do NOT multiply the final outline by mask at the current pixel, otherwise the outside edge dies.
                half hiddenOutline = saturate(hiddenExpanded - silhouetteBlocker);

                half outerOutline = 0.0h;
                if (_OuterOutlineWidth > 0.5)
                {
                    int outerRadius = (int)round(_OuterOutlineWidth + sealPixels);
                    half silhouetteExpanded = DilateSilhouette16(i.uv, outerRadius);
                    outerOutline = saturate(silhouetteExpanded - silhouetteBlocker) * unitHiddenMask * gate;
                }

                half finalAlpha = saturate(max(hiddenOutline, outerOutline)) * _OutlineColor.a;

                if (_DebugMode > 0.5 && _DebugMode < 1.5)
                    return fixed4(silhouette, silhouette, silhouette, silhouette);
                if (_DebugMode > 1.5 && _DebugMode < 2.5)
                    return fixed4(unitHiddenMask, unitHiddenMask, unitHiddenMask, unitHiddenMask);
                if (_DebugMode > 2.5 && _DebugMode < 3.5)
                    return fixed4(gate, gate, gate, gate);
                if (_DebugMode > 3.5 && _DebugMode < 4.5)
                    return fixed4(hidden, hidden, hidden, hidden);
                if (_DebugMode > 4.5 && _DebugMode < 5.5)
                    return fixed4(hiddenExpanded, hiddenExpanded, hiddenExpanded, hiddenExpanded);
                if (_DebugMode > 5.5 && _DebugMode < 6.5)
                    return fixed4(hiddenOutline, hiddenOutline, hiddenOutline, hiddenOutline);
                if (_DebugMode > 6.5 && _DebugMode < 7.5)
                    return fixed4(outerOutline, outerOutline, outerOutline, outerOutline);
                if (_DebugMode > 7.5 && _DebugMode < 8.5)
                    return fixed4(silhouetteBlocker, silhouetteBlocker, silhouetteBlocker, silhouetteBlocker);

                // Fail closed: if there is no outline alpha, do not write a full-screen transparent quad.
                // This also protects against stale UI/material states in standalone builds.
                if (finalAlpha <= 0.001h)
                    discard;

                return fixed4(_OutlineColor.rgb, finalAlpha);
            }
            ENDHLSL
        }
    }
}
