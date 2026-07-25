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

#ifndef GRASS_COMMON_INCLUDED
#define GRASS_COMMON_INCLUDED

#ifdef UNITY_GRAPHFUNCTIONS_LW_INCLUDED //Shader Graph
#define SHADER_GRAPH
#endif

#if UNITY_VERSION >= 60010000
#define RENDERING_LAYERS_DATA_TYPE uint
#else
#define RENDERING_LAYERS_DATA_TYPE float4
#endif

#include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"

#if defined(LOD_FADE_CROSSFADE)
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#endif

half _TerrainDetailDistance;

float4 _ColorMapUV;
float4 _ColorMapParams;
//X: Color map available
//Y: Color map has scale data
TEXTURE2D(_ColorMap); SAMPLER(sampler_ColorMap);
float4 _ColorMap_TexelSize;

float4 _PlayerSphere;
//XYZ: Position
//W: Radius

#if !defined(SHADERPASS_SHADOWCASTER ) || !defined(SHADERPASS_DEPTHONLY)
#define LIGHTING_PASS
#else
//Never any normal maps in depth/shadow passes
#undef _NORMALMAP
#endif

#ifndef SHADER_GRAPH
//Attributes shared per pass, varyings declared separately per pass
struct Attributes
{
	float4 positionOS   : POSITION;
	float4 color		: COLOR0;
#if defined(LIGHTING_PASS) || defined(SHADERPASS_DEPTHNORMALS)
	float3 normalOS     : NORMAL;
#endif 
#if defined(_NORMALMAP) || defined(CURVEDWORLD_NORMAL_TRANSFORMATION_ON)
	float4 tangentOS    : TANGENT;
	float4 uv           : TEXCOORD0;
	//XY: Basemap UV
	//ZW: Bumpmap UV
#else
	float2 uv           : TEXCOORD0;
#endif
	
	float2 staticLightmapUV   : TEXCOORD1;
	float2 dynamicLightmapUV  : TEXCOORD2;

	UNITY_VERTEX_INPUT_INSTANCE_ID
};
#endif

#include "Bending.hlsl"
#include "Wind.hlsl"

//---------------------------------------------------------------//

float ObjectPosRand01()
{
	float4x4 modelTransform = GetObjectToWorldMatrix();
	return saturate(frac(modelTransform[0][2] + modelTransform[1][2] + modelTransform[2][2]));
}

float3 GetPivotPos() 
{
	return TransformObjectToWorld(float3(0,0,0));
}

float DistanceFadeFactor(float3 wPos, float4 near, float4 far)
{
	float pixelDist = length(GetCameraPositionWS().xyz - wPos.xyz);

	if (_TerrainDetailDistance > 1.0 && far.z) //Z=Enabled
	{
		float border = _TerrainDetailDistance+30;
		far.x = min(far.x, border - 30);
		far.y = min(far.y, border);
	}
	
	//Distance based scalar
	float nearFactor = saturate((pixelDist - near.x) / near.y);
	float farFactor = saturate((pixelDist - far.x) / (far.x-far.y));
	
	return saturate(nearFactor * farFactor);
}

void DistanceFadeFactor_half(float3 wPos, float4 near, float4 far, out float fadeFactor)
{
	fadeFactor = DistanceFadeFactor(wPos, near, far);
}

float PlayerFadeFactor(float3 wPos)
{
	if(_PlayerSphere.w > 0)
	{
		const float pixelDist = length(_PlayerSphere.xyz - wPos.xyz);

		const float nearFactor = saturate((pixelDist - (_PlayerSphere.w * 0.5)) / _PlayerSphere.w);

		return 1-nearFactor;
	}
	else
	{
		return 0;
	}
}

float3 DeriveNormal(float3 positionWS)
{
	float3 dpx = ddx(positionWS);
	float3 dpy = ddy(positionWS);
	return normalize(cross(dpx, dpy));
}

float AngleFadeFactor(float3 positionWS, float angleThreshold)
{
	float viewAngle = (dot(DeriveNormal(positionWS), -normalize(GetCameraPositionWS() - positionWS))) * 90;

	float factor = smoothstep(0.25, 1, saturate(viewAngle / (angleThreshold)));
	return factor;
}

void ApplyLODCrossfade(float2 clipPos)
{
#if defined(LOD_FADE_CROSSFADE)
	LODFadeCrossFade(clipPos.xyxy);
#endif
}

TEXTURE2D(_DitheringNoise);
float4 _DitheringScaleOffset;

float Dithering(float2 coords, float t)
{
	//return t * (InterleavedGradientNoise(coords, 0) + t);

	#if defined(SHADERPASS_SHADOWCASTER)
	//_Dithering_Offset.xy *= 0;
	#endif
	
	half2 uv = (coords.xy * _DitheringScaleOffset.xy) + _DitheringScaleOffset.zw;

	half d = SAMPLE_TEXTURE2D_LOD(_DitheringNoise, sampler_PointRepeat, uv, 0).a;
	
	return smoothstep(0, d, t);
}

float ComposeAlpha(float alpha, float cutoff, float3 clipPos, float3 wPos, float4 fadeParamsNear, float4 fadeParamsFar, float angleThreshold)
{
	float f = 1.0;

	#if _FADING
	
	f *= DistanceFadeFactor(wPos, fadeParamsNear, fadeParamsFar);
	f -= PlayerFadeFactor(wPos);

	//Don't perform for cast shadows. Otherwise fading is calculated based on the light direction relative to the surface, not the camera
	#if !defined(SHADERPASS_SHADOWCASTER)
	if(angleThreshold > -1)
	{
		float NdotV = AngleFadeFactor(wPos, angleThreshold);

		f *= NdotV;
	}
	#endif
	
	float dither = Dithering(clipPos.xy, f);

	alpha = min((alpha - cutoff), (dither - 0.5));
	#else
	alpha -= cutoff;
	#endif

	return alpha;
}

void AlphaClip(float alpha, float3 clipPos, float3 wPos)
{
	#ifdef _ALPHATEST_ON
	clip(alpha);
	#endif

	#if defined(SHADERPASS_SHADOWCASTER)
	//Using clip-space position causes pixel swimming as the camera moves
	ApplyLODCrossfade(wPos.xz * 32);
	#else
	ApplyLODCrossfade(clipPos.xy);
	#endif
}

//UV Utilities
float2 BoundsToWorldUV(in float3 wPos, in float4 b)
{
	return (wPos.xz * b.z) - (b.xy * b.z);
}

//Color map UV
float2 GetColorMapUV(in float3 wPos)
{
	return BoundsToWorldUV(wPos, _ColorMapUV);
}

float4 SampleColorMapTextureLOD(in float3 wPos)
{
	float2 uv = GetColorMapUV(wPos);

	return SAMPLE_TEXTURE2D_LOD(_ColorMap, sampler_ColorMap, uv, 0).rgba;
}

//---------------------------------------------------------------//
//Vertex transformation

struct VertexInputs
{
	float4 positionOS;
	float3 normalOS;
#if defined(_NORMALMAP) || defined(CURVEDWORLD_NORMAL_TRANSFORMATION_ON)
	float4 tangentOS;
#endif
};

//Struct that holds both VertexPositionInputs and VertexNormalInputs
struct VertexOutput {
	//Positions
	float3 positionWS; // World space position
	float3 positionVS; // View space position
	float4 positionCS; // Homogeneous clip space position
	float4 positionNDC;// Homogeneous normalized device coordinates
	float3 viewDir;// Homogeneous normalized device coordinates

	//Normals
#if defined(_NORMALMAP) || defined(CURVEDWORLD_NORMAL_TRANSFORMATION_ON)
	real4 tangentWS;
#endif
	float3 normalWS;
};

//Physically correct, but doesn't look great
//#define RECALC_NORMALS

float3 RotateAroundAxis( float3 center, float3 original, float3 u, float angle )
{
	original -= center;
	float C = cos( angle );
	float S = sin( angle );
	float t = 1 - C;
	float m00 = t * u.x * u.x + C;
	float m01 = t * u.x * u.y - S * u.z;
	float m02 = t * u.x * u.z + S * u.y;
	float m10 = t * u.x * u.y + S * u.z;
	float m11 = t * u.y * u.y + C;
	float m12 = t * u.y * u.z - S * u.x;
	float m20 = t * u.x * u.z - S * u.y;
	float m21 = t * u.y * u.z + S * u.x;
	float m22 = t * u.z * u.z + C;
	float3x3 finalMatrix = float3x3( m00, m01, m02, m10, m11, m12, m20, m21, m22 );
	return mul( finalMatrix, original ) + center;
}

//Combination of GetVertexPositionInputs and GetVertexNormalInputs with bending
VertexOutput GetVertexOutput(float4 positionOS, float3 normalOS, float4 tangentOS, float rand, WindSettings s, BendSettings b)
{
	VertexOutput data = (VertexOutput)0;

#if defined(CURVEDWORLD_IS_INSTALLED) && !defined(CURVEDWORLD_DISABLED_ON) && !defined(DEFAULT_VERTEX)
#if defined(CURVEDWORLD_NORMAL_TRANSFORMATION_ON) && defined(LIGHTING_PASS)
	CURVEDWORLD_TRANSFORM_VERTEX_AND_NORMAL(positionOS, normalOS, tangentOS)
#else
	CURVEDWORLD_TRANSFORM_VERTEX(positionOS)
#endif
#endif

#if _BILLBOARD	
	//Local vector towards camera
	float3 camDir = camDir = normalize(positionOS.xyz - TransformWorldToObject(_WorldSpaceCameraPos.xyz));;
	camDir.y = lerp(0, camDir.y, b.billboardingVerticalRotation); //Cylindrical billboarding if 0
	
	float3 forward = camDir;
	float3 right = normalize(cross(float3(0,1,0), forward));
	float3 up = cross(forward, right);

	float4x4 lookatMatrix = {
		right.x,            up.x,            forward.x,       0,
        right.y,            up.y,            forward.y,       0,
        right.z,            up.z,            forward.z,       0,
        0, 0, 0,  1
    };
	
	normalOS = normalize(mul(float4(normalOS , 0.0), lookatMatrix)).xyz;
	positionOS.xyz = mul((float4x4)lookatMatrix, positionOS.xyzw).xyz;	
#endif
	
	float3 wPos = TransformObjectToWorld(positionOS.xyz);

	float scaleMap = 1.0;
#if _SCALEMAP
	if(_ColorMapParams.y > 0)
	{
		scaleMap = SampleColorMapTextureLOD(wPos).a;

		//Scale axes in object-space
		positionOS.x = lerp(positionOS.x, positionOS.x * scaleMap, _ScalemapInfluence.x);
		positionOS.y = lerp(positionOS.y, positionOS.y * scaleMap, _ScalemapInfluence.y);
		positionOS.z = lerp(positionOS.z, positionOS.z * scaleMap, _ScalemapInfluence.z);
		wPos = TransformObjectToWorld(positionOS.xyz);
	}
#else
#endif

	float3 pivotPos = GetPivotPos();
	float4 windVec = GetWindOffset(positionOS.xyz, wPos, rand, s) * scaleMap; //Less wind on shorter grass
	
	#if ADVANCED_BENDING
	b.mode = 1;
	#endif
	
	//b.mask = 1.0;
	float3 worldPos = lerp(wPos, pivotPos, b.mode);
	float4 bendVec = GetBendOffset(worldPos, b);

	float3 offsets = lerp(windVec.xyz, bendVec.xyz, bendVec.a);
	
	#if ADVANCED_BENDING
	offsets.y = 0;
	#endif

	//Perspective correction
	data.viewDir = normalize(GetCameraPositionWS().xyz - wPos);

	#if !_BILLBOARD	&& !defined(SHADERPASS_META)
	float3 perspUp = float3(0,1,0);

	#if _ADVANCED_LIGHTING
	//Upward normal of object, taking into account its rotation
	//perspUp = TransformWorldToObjectDir(perspUp);
	#endif
	
	ApplyPerspectiveCorrection(offsets, wPos, perspUp, data.viewDir, b.mask, b.perspectiveCorrection);
	#endif
	
	float3 offsetPos = wPos;
	//Apply bend offset
	offsetPos.xz += offsets.xz;
	offsetPos.y -= offsets.y;
	
	#if ADVANCED_BENDING
	half3 offsetDir = normalize(offsets - wPos);
	wPos = RotateAroundAxis(pivotPos, offsetPos, normalize(cross(float3(0,1,0), offsetDir)), 0);
	#else
	wPos = offsetPos;
	#endif

	#if defined(MASKING_SPHERE_DISPLACEMENT) && !defined(SHADERPASS_META)
	//Displace away from GrassMaskingSphere component
	if(_PlayerSphere.w > 0)
	{
		float3 delta = wPos.xyz - _PlayerSphere.xyz;
		float3 pushDir = normalize(delta);
		if(length(delta) < _PlayerSphere.w)
		{
			wPos = _PlayerSphere.xyz + (pushDir * _PlayerSphere.w);
		}
	} 
	#endif

	//Vertex positions in various coordinate spaces
	data.positionWS = wPos;
	data.positionVS = TransformWorldToView(data.positionWS);
	data.positionCS = TransformWorldToHClip(data.positionWS);                       
	
	float4 ndc = data.positionCS * 0.5f;
	data.positionNDC.xy = float2(ndc.x, ndc.y * _ProjectionParams.x) + ndc.w;
	data.positionNDC.zw = data.positionCS.zw;

#if !defined(SHADERPASS_SHADOWCASTER) && !defined(SHADERPASS_DEPTHONLY) //Skip normal derivative during shadow and depth passes

#if _ADVANCED_LIGHTING && defined(RECALC_NORMALS)
	float3 oPos = TransformWorldToObject(wPos); //object-space position after displacement in world-space
	float3 bentNormals = lerp(normalOS, normalize(oPos - positionOS.xyz), abs(offsets.x + offsets.z) * 0.5); //weight is length of wind/bend vector
#else
	float3 bentNormals = normalOS;
#endif

	data.normalWS = TransformObjectToWorldNormal(bentNormals);
#ifdef _NORMALMAP
	data.tangentWS.xyz = TransformObjectToWorldDir(tangentOS.xyz);
	real sign = tangentOS.w * GetOddNegativeScale();
	data.tangentWS.w = sign;
#endif
#endif

	return data;
}
#endif