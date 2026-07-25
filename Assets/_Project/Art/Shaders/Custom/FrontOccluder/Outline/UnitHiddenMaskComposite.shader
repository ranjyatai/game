Shader "Hidden/SkyPrison/UnitHiddenMaskComposite"
{
    Properties
    {
        _MainTex ("Source", 2D) = "black" {}
        _SilhouetteTex ("Unit Silhouette", 2D) = "black" {}
        _OccluderMaskTex ("Occluder Mask", 2D) = "black" {}
        _SilhouetteThreshold ("Silhouette Threshold", Range(0,1)) = 0.5
        _MaskThreshold ("Mask Threshold", Range(0,1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
            "Queue"="Overlay"
            "IgnoreProjector"="True"
        }

        Cull Off
        ZWrite Off
        ZTest Always

        // Pass 0: UnitHidden = UnitSilhouette ∩ AuthorizedOccluderMask.
        Pass
        {
            Name "IntersectSilhouetteAndOccluder"
            Blend Off
            ColorMask RGBA

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _SilhouetteTex;
            sampler2D _OccluderMaskTex;
            float _SilhouetteThreshold;
            float _MaskThreshold;

            fixed4 frag(v2f_img i) : SV_Target
            {
                fixed4 s = tex2D(_SilhouetteTex, i.uv);
                fixed4 o = tex2D(_OccluderMaskTex, i.uv);

                float silhouette = max(max(s.r, s.g), max(s.b, s.a));
                float occluder = max(max(o.r, o.g), max(o.b, o.a));

                float hit = step(_SilhouetteThreshold, silhouette) * step(_MaskThreshold, occluder);
                return fixed4(hit, hit, hit, hit);
            }
            ENDCG
        }

        // Pass 1: Add/copy a mask into the destination RT.
        // Used by ScreenSpaceOutlineRTManager to accumulate per-unit hidden masks
        // and raw authorized occluder masks after the destination has been cleared.
        Pass
        {
            Name "AccumulateMask"
            Blend One One
            ColorMask RGBA

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;

            fixed4 frag(v2f_img i) : SV_Target
            {
                fixed4 src = tex2D(_MainTex, i.uv);
                float v = max(max(src.r, src.g), max(src.b, src.a));
                return fixed4(v, v, v, v);
            }
            ENDCG
        }
    }

    FallBack Off
}
