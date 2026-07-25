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

#define USE_SHADOW_BIAS

#ifdef USE_SHADOW_BIAS
float3 _LightDirection;
#endif

struct Varyings
{
	float4 positionCS   : SV_POSITION;
	float2 uv           : TEXCOORD0;
#ifdef _ALPHATEST_ON
	float3 positionWS   : TEXCOORD1;
#endif
	UNITY_VERTEX_INPUT_INSTANCE_ID
	UNITY_VERTEX_OUTPUT_STEREO
};

Varyings ShadowPassVertex(Attributes input)
{
	Varyings output;
	UNITY_SETUP_INSTANCE_ID(input);
	UNITY_TRANSFER_INSTANCE_ID(input, output);
	UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

	output.uv.xy = TRANSFORM_TEX(input.uv, _BaseMap);

	float posOffset = ObjectPosRand01();

	//Compose parameter structs
	WindSettings wind = PopulateWindSettings(_WindAmbientStrength, _WindSpeed, _WindDirection, _WindSwinging, input.color[_VertexColorWindChannel], _WindObjectRand, _WindVertexRand, _WindRandStrength, _WindGustStrength, _WindGustFreq, _WindGustSpeed);
	BendSettings bending = PopulateBendSettings(_BendMode, input.color[_VertexColorBendingChannel], _BendPushStrength, _BendFlattenStrength, _PerspectiveCorrection, _BillboardingVerticalRotation);
	
	VertexOutput vertexData = GetVertexOutput(input.positionOS, 0, 0, posOffset, wind, bending);

#ifdef USE_SHADOW_BIAS
	float4 positionCS = TransformWorldToHClip(ApplyShadowBias(vertexData.positionWS, vertexData.normalWS, _LightDirection));
#else
	//Skip depth bias, to keep shadows touching base of mesh (results in some acne)
	float4 positionCS = TransformWorldToHClip(vertexData.positionWS);
#endif

	output.positionCS = positionCS;

#if UNITY_REVERSED_Z
	output.positionCS.z = min(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
#else
	output.positionCS.z = max(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
#endif

	#ifdef _ALPHATEST_ON
	output.positionWS = vertexData.positionWS;
	#endif
	
	return output;
}

half4 ShadowPassFragment(Varyings input) : SV_TARGET
{
	UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

#ifdef _ALPHATEST_ON
	float alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a;
	
	alpha = ComposeAlpha(alpha, _Cutoff, input.positionCS.xyz, input.positionWS.xyz, _FadeNear, _FadeFar, _FadeAngleThreshold);
	AlphaClip(alpha, input.positionCS.xyz, input.positionWS.xyz);
#endif
	return 0;
}