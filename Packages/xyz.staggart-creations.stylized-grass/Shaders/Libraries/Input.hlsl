// Stylized Grass Shader © Staggart Creations (http://staggart.xyz)
// COPYRIGHT PROTECTED UNDER THE UNITY ASSET STORE EULA (https://unity.com/legal/as-terms)
//
// ⚠️ WARNING: UNAUTHORIZED USE OR DISTRIBUTION IS STRICTLY PROHIBITED
// • Copying, referencing, or reverse-engineering this source code for the creation of new Asset Store or derivative products,
//   or any other publicly distributed content is strictly forbidden and will result in legal action.
// • Studying this file for the purpose of reproducing its functionality in your own assets or tools is not permitted.
// • If you are viewing this file as a reference, please close it immediately to avoid unintentional design influence or potential EULA violations.
// • Uploading this file or any derivative of it to a public GitHub or similar repository will trigger an automated DMCA takedown request.
// • Studying to understand for personal, educational or integration purposes is allowed, studying to reproduce is not.

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"

CBUFFER_START(UnityPerMaterial)
float4 _BaseColor;
float4 _BaseMap_ST;
float4 _BumpMap_ST;
float4 _HueVariation;
float4 _BendTint;
float4 _EmissionColor;
half4 _WindDirection;
half4 _ScalemapInfluence;
half _HueVariationHeight;
half _ColorMapStrength;
half _ColorMapHeight;
half _Cutoff;
half _Smoothness;
half _TranslucencyDirect;
half _TranslucencyIndirect;
half _TranslucencyOffset;
half _TranslucencyFalloff;
half _OcclusionStrength;
half _VertexDarkening;
half _BumpScale;

half _NormalFlattening;
half _NormalSpherify;
half _NormalSpherifyMask;
half _NormalFlattenDepthNormals;
//X: Spherify
//Y: Spherify tip mask
//Z: Flatten bottom
//W:

bool _FadingOn;
half4 _FadeFar;
half4 _FadeNear;
half _FadeAngleThreshold;

//Bending
half _BendPushStrength;
half _BendMode;
half _BendFlattenStrength;
half _PerspectiveCorrection;
half _BillboardingVerticalRotation;

//Wind
half _WindAmbientStrength;
float _WindSpeed;
half _WindVertexRand;
half _WindObjectRand;
half _WindRandStrength;
half _WindSwinging;
half _WindGustStrength;
half _WindGustFreq;
float _WindGustSpeed;
half _WindGustTint;

//Vegetation Studio Pro
half4 _LODDebugColor;

int _VertexColorShadingChannel;
int _VertexColorWindChannel;
int _VertexColorBendingChannel;
CBUFFER_END

#ifdef UNITY_DOTS_INSTANCING_ENABLED
UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
    UNITY_DOTS_INSTANCED_PROP(float4, _BaseColor)
UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)

#define _BaseColor UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _BaseColor)
#endif