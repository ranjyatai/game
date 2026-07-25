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
//#define DEBUG_BEND_AREA
//#define DEBUG_BEND_VECTORS

#if UNITY_VERSION >= 60010000
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GBufferOutput.hlsl"
#else
#define GBufferFragOutput FragmentOutput //Struct. Deprecated
#define PackGBuffersBRDFData BRDFDataToGbuffer //Function. Deprecated
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityGBuffer.hlsl"
#endif
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"

// Sky Prison project integration: keep stylized grass inside the map's gray-green atmosphere.
// This is intentionally local and conservative: it does not touch wind, alpha clip, shadows or lighting setup.
#ifndef SKYPRISON_GRASS_ENV_GRADE_ON
#define SKYPRISON_GRASS_ENV_GRADE_ON 1
#endif

float3 SkyPrisonApplyGrassEnvironmentGrade(float3 color)
{
#if SKYPRISON_GRASS_ENV_GRADE_ON
	// The grass package's default greens are too saturated for Sky Prison's foggy industrial maps.
	// Apply a near-field grade first, then let Unity fog handle distance fade afterwards.
	const float3 envTint = float3(0.58, 0.66, 0.56);
	const float envStrength = 0.52;
	const float desaturation = 0.42;
	const float brightnessCeiling = 0.58;

	float luminance = dot(color, float3(0.299, 0.587, 0.114));
	color = lerp(color, luminance.xxx, desaturation);
	color = lerp(color, color * envTint, envStrength);

	float maxChannel = max(color.r, max(color.g, color.b));
	if (maxChannel > brightnessCeiling)
	{
		color *= brightnessCeiling / maxChannel;
	}
#endif
	return color;
}

struct Varyings
{
	float4 uv                       : TEXCOORD0;
	DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 1);
	
//#if defined(REQUIRES_WORLD_SPACE_POS_INTERPOLATOR) //Always needed
	float3 positionWS               : TEXCOORD2;
//#endif
	
	half3  normalWS                 : TEXCOORD3;

#ifdef _NORMALMAP
	half4 tangentWS                 : TEXCOORD4;  // xyz: tangent, w: sign
#endif

	half4 fogFactorAndVertexLight   : TEXCOORD6; // x: fogFactor, yzw: vertex light

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
	float4 shadowCoord              : TEXCOORD8; // compute shadow coord per-vertex for the main light
#endif
	
	#ifdef DYNAMICLIGHTMAP_ON
	float2  dynamicLightmapUV : TEXCOORD9; // Dynamic lightmap UVs
	#endif
	
	#ifdef USE_APV_PROBE_OCCLUSION
	float4 probeOcclusion : TEXCOORD10;
	#endif
		
	float4 positionCS               : SV_POSITION;
	float4 color					: COLOR0;
	
	#ifdef EDITOR_VISUALIZATION
	float2 VizUV        : TEXCOORD8;
	float4 LightCoord   : TEXCOORD9;
	#endif
	
	UNITY_VERTEX_INPUT_INSTANCE_ID
	UNITY_VERTEX_OUTPUT_STEREO
};

Varyings LitPassVertex(Attributes input)
{
	Varyings output = (Varyings)0;
	
	UNITY_SETUP_INSTANCE_ID(input);
	UNITY_TRANSFER_INSTANCE_ID(input, output);
	UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

	float posOffset = ObjectPosRand01();

	WindSettings wind = PopulateWindSettings(_WindAmbientStrength, _WindSpeed, _WindDirection, _WindSwinging, input.color[_VertexColorWindChannel], _WindObjectRand, _WindVertexRand, _WindRandStrength, _WindGustStrength, _WindGustFreq, _WindGustSpeed);
	BendSettings bending = PopulateBendSettings(_BendMode, input.color[_VertexColorBendingChannel], _BendPushStrength, _BendFlattenStrength, _PerspectiveCorrection, _BillboardingVerticalRotation);
	
	//Original vertex normals should be perpendicular to the vertex face!
	//For lighting, force the normals straight up. Later in the LitPassVertex the normals can be modified through parameters

	#ifdef LIGHTING_PASS
	input.normalOS = lerp(input.normalOS, float3(0,1,0), _NormalFlattening);
	input.normalOS = lerp(input.normalOS, normalize(input.positionOS.xyz), _NormalSpherify * lerp(1, input.color[_VertexColorShadingChannel], _NormalSpherifyMask));
	#endif
	
	//Apply transformations and bending/wind (Can't use GetVertexPositionInputs, because it would amount to double matrix transformations)
	float4 tangentOS = float4(1, 0, 0, 1);
	#if defined(_NORMALMAP) || defined(CURVEDWORLD_NORMAL_TRANSFORMATION_ON)
	tangentOS = input.tangentOS;
	#endif
	
	VertexOutput vertexData = GetVertexOutput(input.positionOS, input.normalOS, tangentOS, posOffset, wind, bending);
	
	//#if defined(REQUIRES_WORLD_SPACE_POS_INTERPOLATOR)
	output.positionWS = vertexData.positionWS;
	//#endif
	output.normalWS = vertexData.normalWS;
	
	//Vertex color
	output.color.rgb = ApplyVertexColor(input.positionOS.xyz, vertexData.positionWS.xyz, _BaseColor.rgb, input.color[_VertexColorShadingChannel], _OcclusionStrength, _VertexDarkening, posOffset);
	//Pass vertex color alpha-channel to fragment stage. Used in some shading functions such as translucency
	output.color.a = input.color[_VertexColorShadingChannel];

	//output.color.a *= vertexInputs.positionOS.y;
	
	half fogFactor = ComputeFogFactor(vertexData.positionCS.z);

#if defined(REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR) || defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
	real sign = input.tangentOS.w * GetOddNegativeScale();
	half4 tangentWS = half4(normalInput.tangentWS.xyz, sign);
#endif
#if defined(REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR)
	output.tangentWS = tangentWS;
#endif
	
#if defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
	half3 viewDirWS = GetWorldSpaceViewDir(vertexData.positionWS);
	half3 viewDirTS = GetViewDirectionTangentSpace(tangentWS, output.normalWS, viewDirWS);
	output.viewDirTS = viewDirTS;
#endif

	OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, output.staticLightmapUV);
	#ifdef DYNAMICLIGHTMAP_ON
	output.dynamicLightmapUV = input.dynamicLightmapUV.xy * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
	#endif

	OUTPUT_SH4(vertexData.positionWS, output.normalWS.xyz, GetWorldSpaceNormalizeViewDir(vertexData.positionWS), output.vertexSH, output.probeOcclusion);
	
#ifdef _ADDITIONAL_LIGHTS_VERTEX
	//Apply per-vertex light if enabled in pipeline
	//Pass to fragment shader to apply in Lighting function
	half3 vertexLight = VertexLighting(vertexData.positionWS, vertexData.normalWS);
	output.fogFactorAndVertexLight = half4(fogFactor, vertexLight);
#else
	output.fogFactorAndVertexLight.x = fogFactor;
	output.fogFactorAndVertexLight.yzw = 0;
#endif
	
#if _NORMALMAP || defined(CURVEDWORLD_NORMAL_TRANSFORMATION_ON)
	output.uv.zw = TRANSFORM_TEX(input.uv, _BumpMap);
	output.tangentWS = vertexData.tangentWS;
#else
	//Initialize with 0
	output.uv.zw = 0;
#endif

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
	//GetShadowCoord function must be used, in order for normalized screen coords to be calculated (Screen-space shadows)
	VertexPositionInputs vertexPositionInputs = (VertexPositionInputs)0;
	vertexPositionInputs.positionWS = vertexData.positionWS;
	vertexPositionInputs.positionCS = vertexData.positionCS; //used to compute screen pos
	output.shadowCoord = GetShadowCoord(vertexPositionInputs);
#endif

	output.uv.xy = TRANSFORM_TEX(input.uv, _BaseMap);
	output.positionCS = vertexData.positionCS;

	return output;
}

void ModifySurfaceData(Varyings input, out SurfaceData surfaceData)
{
	float4 albedoAlpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv.xy);

	//If MSAA is enabled an issue occurs where any vertex interpolated values are abnormal if the geometry occupies 1px on screen
	//Clamping the value resolves these artefacts for the most part, otherwise causes fireflies
	half mask = saturate(input.color.a);

	half heightGradient = saturate(sqrt(mask));
	//Apply hue variation
	if(_HueVariation.a > 0)
	{
		float posOffset = ObjectPosRand01();
		half hueMask = (1-smoothstep(_HueVariationHeight, 0, mask)) * _HueVariation.a;
		albedoAlpha.rgb = lerp(albedoAlpha.rgb, _HueVariation.rgb, posOffset * hueMask);
	}
	
	//Apply ambient occlusion and random darkening from vertex stage
	albedoAlpha.rgb *= input.color.rgb;

	#ifndef SHADERPASS_META //Do not apply during light baking, as the bounce color of the terrain is already incorperated into the lightmap
	//Apply color map per-pixel
	if (_ColorMapParams.x == 1) 
	{
		float colorMapMask = smoothstep(_ColorMapHeight, 1.0 + _ColorMapHeight, heightGradient);
		albedoAlpha.rgb = lerp(ApplyColorMap(input.positionWS.xyz, albedoAlpha.rgb, _ColorMapStrength), albedoAlpha.rgb, colorMapMask);
	}
	#endif

	#ifndef SHADERPASS_META
	if(_BendTint.a > 0 && (_BendPushStrength > 0 || _BendFlattenStrength > 0))
	{
		float4 bendVector = GetBendVector(input.positionWS.xyz);
		float bendingMask = saturate(bendVector.a * HeightDistanceWeight(input.positionWS, bendVector.xyz));
		albedoAlpha.rgb = lerp(albedoAlpha.rgb, albedoAlpha.rgb * _BendTint.rgb, saturate(bendingMask * heightGradient * _BendTint.a));
	}
	#endif

	surfaceData.albedo = saturate(albedoAlpha.rgb * _LODDebugColor.rgb);
	//Not using specular setup, free to use this to pass data
	surfaceData.specular = float3(0, 0, 0);
	surfaceData.metallic = 0.0;
	surfaceData.smoothness = lerp(0.0, _Smoothness, mask);
#ifdef _NORMALMAP
	surfaceData.normalTS = SampleNormal(input.uv.zw, TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap), _BumpScale);
#else
	surfaceData.normalTS = float3(0.5, 0.5, 1.0);
#endif
	surfaceData.emission = lerp(half3(0, 0, 0), _EmissionColor.rgb, mask);
	surfaceData.occlusion = 1.0;
	surfaceData.alpha = albedoAlpha.a;
	
	surfaceData.clearCoatMask = 0.0h;
	surfaceData.clearCoatSmoothness = 0.0h;

	//Debug
	//surfaceData.albedo = surfaceData.smoothness.xxx;
}

//This function is a testament to how convoluted cross-compatibility between difference URP versions has become
void PopulateLightingInputData(Varyings input, half3 normalTS, out InputData inputData)
{
	inputData = (InputData)0;
	
	//#if defined(REQUIRES_WORLD_SPACE_POS_INTERPOLATOR)
	inputData.positionWS = input.positionWS.xyz;
	//#endif

	//Using GetWorldSpaceViewDir returns a constant vector for orthographic camera's, which isn't useful
	half3 viewDirWS = normalize(_WorldSpaceCameraPos - (input.positionWS.xyz));
	
	half3x3 tangentToWorld = 0;
	#if defined(_NORMALMAP)
	float sgn = input.tangentWS.w; // should be either +1 or -1
	float3 bitangent = sgn * cross(input.normalWS.xyz, input.tangentWS.xyz);
	tangentToWorld = half3x3(input.tangentWS.xyz, bitangent.xyz, input.normalWS.xyz);;
	#endif
	
#if defined(_NORMALMAP)
	inputData.normalWS = TransformTangentToWorld(normalTS, tangentToWorld);
#else
	inputData.normalWS = input.normalWS;
#endif
	inputData.normalWS = NormalizeNormalPerPixel(inputData.normalWS);
	
	inputData.viewDirectionWS = viewDirWS;

	#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR) //No shadow cascades
	inputData.shadowCoord = input.shadowCoord;
	#elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
	inputData.shadowCoord = TransformWorldToShadowCoord(inputData.positionWS);
	#else
	inputData.shadowCoord = float4(0, 0, 0, 0);
	#endif
	
	inputData.positionCS = input.positionCS;
	inputData.tangentToWorld = tangentToWorld; //Not actually using this value of the InputData struct, but URP12+ dictates it

	inputData.fogCoord = input.fogFactorAndVertexLight.x;
	inputData.vertexLighting = input.fogFactorAndVertexLight.yzw;
	
	#if defined(DYNAMICLIGHTMAP_ON)
	inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.dynamicLightmapUV.xy, input.vertexSH, inputData.normalWS);
	#elif !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
	inputData.bakedGI = SAMPLE_GI(input.vertexSH, GetAbsolutePositionWS(inputData.positionWS), inputData.normalWS, inputData.viewDirectionWS, input.positionCS.xy, input.probeOcclusion, input.probeOcclusion);
	#else
	inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, inputData.normalWS);
	#endif

	inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
	inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);

	#if defined(DEBUG_DISPLAY)
	#if defined(DYNAMICLIGHTMAP_ON)
	inputData.dynamicLightmapUV = input.dynamicLightmapUV;
	#endif
	#if defined(LIGHTMAP_ON)
	inputData.staticLightmapUV = input.staticLightmapUV;
	#else
	inputData.vertexSH = input.vertexSH;
	#endif
	#endif
}

#if defined(SHADERPASS_DEFERRED)
GBufferFragOutput LightingPassFragment(Varyings input)
#else
void LightingPassFragment(Varyings input, out half4 outColor : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
	, out RENDERING_LAYERS_DATA_TYPE outRenderingLayers : SV_Target1
#endif
	)
#endif
{
	UNITY_SETUP_INSTANCE_ID(input);
	UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

	WindSettings wind = (WindSettings)0;
	if(_WindGustTint > 0)
	{
		wind = PopulateWindSettings(_WindAmbientStrength, _WindSpeed, _WindDirection, _WindSwinging, input.color[_VertexColorWindChannel], _WindObjectRand, _WindVertexRand, _WindRandStrength, _WindGustStrength, _WindGustFreq, _WindGustSpeed);
	}
	SurfaceData surfaceData;
	//Can't use standard function, since including LitInput.hlsl breaks the SRP batcher
	ModifySurfaceData(input, surfaceData);
	
	surfaceData.alpha = ComposeAlpha(surfaceData.alpha, _Cutoff, input.positionCS.xyz, input.positionWS.xyz, _FadeNear, _FadeFar, _FadeAngleThreshold);
	//return float4(surfaceData.alpha.xxx, 1.0);
	AlphaClip(surfaceData.alpha, input.positionCS.xyz, input.positionWS.xyz);
	
	InputData inputData;
	//Standard URP function barely changes, but adds things like clear coat and detail normals
	PopulateLightingInputData(input, surfaceData.normalTS, inputData);
	
	//Get main light first, need attenuation to mask wind gust
	Light mainLight = GetMainLight(inputData.shadowCoord);

	//Tint by wind gust
	if(_WindGustTint > 0)
	{
		float gustStrength = wind.gustStrength;
		wind.gustStrength = 1;
		float gust = SampleGustMap(input.positionWS.xyz, wind);
		surfaceData.albedo += gust * _WindGustTint * gustStrength * (mainLight.shadowAttenuation) * saturate(input.color.a);
		surfaceData.albedo = saturate(surfaceData.albedo);
	}

	#ifdef DEBUG_BEND_VECTORS
	float4 bendVector = GetBendVector(input.positionWS).xyzw;

	float dist = HeightDistanceWeight(input.positionWS, bendVector.xyz);
	//return float4(bendVector.aaa, 1.0);
	//return float4(saturate(bendVector.yyy), 1.0);
	surfaceData.albedo = saturate(bendVector * 0.5 + 0.5);
	#endif
	
	//SETUP_DEBUG_TEXTURE_DATA(inputData, input.uv.xy);
	
	#ifdef _DBUFFER
	ApplyDecalToSurfaceData(input.positionCS, surfaceData, inputData);
	#endif

	TranslucencyData tData = (TranslucencyData)0;
	tData.strengthDirect = _TranslucencyDirect;
	tData.strengthIndirect = _TranslucencyIndirect;
	tData.exponent = _TranslucencyFalloff;
	tData.thickness = saturate(input.color.a);
	tData.offset = _TranslucencyOffset;
	tData.light = mainLight;

	surfaceData.alpha = 1.0;
		
	//Deferred
#if defined(SHADERPASS_DEFERRED)
	// in LitForwardPass GlobalIllumination (and temporarily LightingPhysicallyBased) are called inside UniversalFragmentPBR
	// in Deferred rendering we store the sum of these values (and of emission as well) in the GBuffer
	BRDFData brdfData;
	surfaceData.albedo = SkyPrisonApplyGrassEnvironmentGrade(surfaceData.albedo);
	InitializeBRDFData(surfaceData.albedo, surfaceData.metallic, surfaceData.specular, surfaceData.smoothness, surfaceData.alpha, brdfData);
	
	MixRealtimeAndBakedGI(mainLight, inputData.normalWS, inputData.bakedGI, inputData.shadowMask);
	half3 color = GlobalIllumination(brdfData, inputData.bakedGI, surfaceData.occlusion, inputData.positionWS, inputData.normalWS, inputData.viewDirectionWS);

	ApplyTranslucency(surfaceData, inputData, tData);
	
	return PackGBuffersBRDFData(brdfData, inputData, surfaceData.smoothness, surfaceData.emission + color, surfaceData.occlusion);

	//Forward
#else

	float3 finalColor = ApplyLighting(surfaceData, inputData, tData);
	finalColor = SkyPrisonApplyGrassEnvironmentGrade(finalColor);
	finalColor = MixFog(finalColor, inputData.fogCoord);
	
	outColor = half4(finalColor, surfaceData.alpha);

	//Debugging
	//outColor = float4(AngleFadeFactor(input.positionWS, _FadeAngleThreshold).xxx, 1.0); return;
	//outColor = float4(DistanceFadeFactor(input.positionWS, _FadeNear, _FadeFar).xxx, 1.0); return;
	//outColor = float4(HeightDistanceWeight(input.positionWS.y, GetBendVector(input.positionWS).y).xxx * GetBendVector(input.positionWS).a, 1.0); return;
	//outColor = float4(InterleavedNoise(input.positionCS.xy, PlayerFaceFactor(input.positionWS)).xxx, 1.0); return;
	//outColor = float4(Dithering(input.uv.xy, 0.5).xxx, 1.0);
	//outColor = float4(SAMPLE_TEXTURE2D_LOD(_DitheringNoise, sampler_PointRepeat, input.uv.xy, 0).aaa, 1.0);
	
	#ifdef DEBUG_BEND_AREA
	float2 bendUV = GetBendMapUV(input.positionWS);
	return float4(any(bendUV.xy) ? bendUV.xy : float2(0,0), 0, 1.0);

	float edgeMask = BoundsEdgeMask(input.positionWS.xz);
	return float4(edgeMask.xxx, 1.0);
	return float4(lerp(float3(1,0,0), float3(0,1,0), edgeMask > 0 ? 1 : 0), 1.0);
	#endif
	
	#ifdef _WRITE_RENDERING_LAYERS
	#if UNITY_VERSION >= 60010000
	outRenderingLayers = EncodeMeshRenderingLayer();
	#else
	uint renderingLayers = GetMeshRenderingLayer();
	outRenderingLayers = float4(EncodeMeshRenderingLayer(renderingLayers), 0, 0, 0);
	#endif
	#endif
	
	//return half4(finalColor, surfaceData.alpha);
#endif
}