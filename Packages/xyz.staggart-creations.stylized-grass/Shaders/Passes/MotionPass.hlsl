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

#if defined(LOD_FADE_CROSSFADE)
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#endif

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MotionVectorsCommon.hlsl"

struct MotionAttributes
{
	float4 positionOS             : POSITION;
	float4 color             : COLOR;
	#if _ALPHATEST_ON
	float2 uv                   : TEXCOORD0;
	#endif
	float3 positionOld          : TEXCOORD4;
	#if _ADD_PRECOMPUTED_VELOCITY
	float3 alembicMotionVector  : TEXCOORD5;
	#endif
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
	float4 positionCS                 : SV_POSITION;
	float4 positionCSNoJitter         : POSITION_CS_NO_JITTER;
	float4 previousPositionCSNoJitter : PREV_POSITION_CS_NO_JITTER;
	
	float2 uv           : TEXCOORD0;
#ifdef _ALPHATEST_ON
	float4 positionWS   : TEXCOORD1;
#endif
	
	UNITY_VERTEX_INPUT_INSTANCE_ID
	UNITY_VERTEX_OUTPUT_STEREO
};

Varyings MotionVertex(MotionAttributes input)
{
	Varyings output = (Varyings)0;
	UNITY_SETUP_INSTANCE_ID(input);
	UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

	output.uv = TRANSFORM_TEX(input.uv, _BaseMap);

	float posOffset = ObjectPosRand01();

	WindSettings wind = PopulateWindSettings(_WindAmbientStrength, _WindSpeed, _WindDirection, _WindSwinging, input.color[_VertexColorWindChannel], _WindObjectRand, _WindVertexRand, _WindRandStrength, _WindGustStrength, _WindGustFreq, _WindGustSpeed);
	BendSettings bending = PopulateBendSettings(_BendMode, input.color[_VertexColorBendingChannel], _BendPushStrength, _BendFlattenStrength, _PerspectiveCorrection, _BillboardingVerticalRotation);
	
	//Applies all the vertex displacement and transformations to various coordinate spaces
	VertexOutput vertexData = GetVertexOutput(input.positionOS, 0, 0, posOffset, wind, bending);

#ifdef _ALPHATEST_ON
	output.positionWS.xyz = vertexData.positionWS;
#endif

	#if defined(APLICATION_SPACE_WARP_MOTION)
	// We do not need jittered position in ASW
	output.positionCSNoJitter = mul(_NonJitteredViewProjMatrix, mul(UNITY_MATRIX_M, input.positionOS));
	output.positionCS = output.positionCSNoJitter;
	#else
	// Jittered. Match the frame.
	output.positionCS = vertexData.positionCS;
	output.positionCSNoJitter = mul(_NonJitteredViewProjMatrix, mul(UNITY_MATRIX_M, input.positionOS));
	#endif
	
	float4 prevPos = (unity_MotionVectorsParams.x == 1) ? float4(input.positionOld, 1) : input.positionOS;
	
	#if _ADD_PRECOMPUTED_VELOCITY
	prevPos = prevPos - float4(input.alembicMotionVector, 0);
	#endif
	
	output.previousPositionCSNoJitter = mul(_PrevViewProjMatrix, mul(UNITY_PREV_MATRIX_M, prevPos));

	return output;
}

half4 MotionFragment(Varyings input) : SV_TARGET
{
	UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

#ifdef _ALPHATEST_ON
	float alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a;

	alpha = ComposeAlpha(alpha, _Cutoff, input.positionCS.xyz, input.positionWS.xyz, _FadeNear, _FadeFar, _FadeAngleThreshold);
	AlphaClip(alpha, input.positionCS.xyz, input.positionWS.xyz);
#endif

	#if defined(APLICATION_SPACE_WARP_MOTION)
	return float4(CalcAswNdcMotionVectorFromCsPositions(input.positionCSNoJitter, input.previousPositionCSNoJitter), 1);
	#else
	return float4(CalcNdcMotionVectorFromCsPositions(input.positionCSNoJitter, input.previousPositionCSNoJitter), 0, 0);
	#endif
}