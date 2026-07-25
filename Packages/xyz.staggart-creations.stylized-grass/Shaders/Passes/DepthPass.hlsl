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

struct Varyings
{
	float2 uv           : TEXCOORD0;
	float4 positionCS   : SV_POSITION;
#ifdef _ALPHATEST_ON
	float4 positionWS   : TEXCOORD1;
#endif

	#ifdef SHADERPASS_DEPTHNORMALS
	float3 normalWS		: TEXCOORD2;
	#endif
	UNITY_VERTEX_INPUT_INSTANCE_ID
	UNITY_VERTEX_OUTPUT_STEREO
};

Varyings DepthOnlyVertex(Attributes input)
{
	Varyings output = (Varyings)0;
	UNITY_SETUP_INSTANCE_ID(input);
	UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

	output.uv = TRANSFORM_TEX(input.uv, _BaseMap);

	float posOffset = ObjectPosRand01();

	WindSettings wind = PopulateWindSettings(_WindAmbientStrength, _WindSpeed, _WindDirection, _WindSwinging, input.color[_VertexColorWindChannel], _WindObjectRand, _WindVertexRand, _WindRandStrength, _WindGustStrength, _WindGustFreq, _WindGustSpeed);
	BendSettings bending = PopulateBendSettings(_BendMode, input.color[_VertexColorBendingChannel], _BendPushStrength, _BendFlattenStrength, _PerspectiveCorrection, _BillboardingVerticalRotation);
	
	#ifdef SHADERPASS_DEPTHNORMALS
	input.normalOS = lerp(input.normalOS, float3(0,1,0), _NormalFlattenDepthNormals);
	#endif
	
	VertexOutput vertexData = GetVertexOutput(input.positionOS, input.normalOS, 0, posOffset, wind, bending);

	output.positionCS = vertexData.positionCS;
#ifdef _ALPHATEST_ON
	output.positionWS.xyz = vertexData.positionWS;
#endif

	#ifdef SHADERPASS_DEPTHNORMALS
	output.normalWS = vertexData.normalWS;
	#endif

	return output;
}

half4 DepthOnlyFragment(Varyings input) : SV_TARGET
{
	UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

#ifdef _ALPHATEST_ON
	float alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a;

	alpha = ComposeAlpha(alpha, _Cutoff, input.positionCS.xyz, input.positionWS.xyz, _FadeNear, _FadeFar, _FadeAngleThreshold);
	AlphaClip(alpha, input.positionCS.xyz, input.positionWS.xyz);
#endif

	return input.positionCS.z;
}

void DepthNormalsFragment(
	Varyings input
   , out half4 outNormalWS : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
   , out RENDERING_LAYERS_DATA_TYPE outRenderingLayers : SV_Target1
#endif
)
{
	UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

#ifdef SHADERPASS_DEPTHNORMALS
	#ifdef _ALPHATEST_ON
	float alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a;
	
	alpha = ComposeAlpha(alpha, _Cutoff, input.positionCS.xyz, input.positionWS.xyz, _FadeNear, _FadeFar, _FadeAngleThreshold);
	AlphaClip(alpha, input.positionCS.xyz, input.positionWS.xyz);
	#endif
	
	outNormalWS = half4(input.normalWS, 0.0);

	#ifdef _WRITE_RENDERING_LAYERS
	#if UNITY_VERSION >= 60010000
	outRenderingLayers = EncodeMeshRenderingLayer();
	#else
	uint renderingLayers = GetMeshRenderingLayer();
	outRenderingLayers = float4(EncodeMeshRenderingLayer(renderingLayers), 0, 0, 0);
	#endif
	#endif
	
#else
	outNormalWS = 0;
#endif
}