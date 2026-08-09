// Made with Amplify Shader Editor v1.9.9.9
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "BOXOPHOBIC/Atmospherics/Height Fog Standalone"
{
	Properties
	{
		[StyledBanner(Height Fog Standalone)] _Banner( "Banner", Float ) = 0
		[StyledCategory(Fog Settings, 5, 10)] _FogCat( "[ Fog Cat]", Float ) = 1
		_FogIntensity( "Fog Intensity", Range( 0, 1 ) ) = 1
		[Enum(X Axis,0,Y Axis,1,Z Axis,2)][Space(10)] _FogAxisMode( "Fog Axis Mode", Float ) = 1
		[Enum(Multiply Distance and Height,0,Additive Distance and Height,1)] _FogLayersMode( "Fog Layers Mode", Float ) = 0
		[Enum(Perspective,0,Orthographic,1,Both,2)] _FogCameraMode( "Fog Camera Mode", Float ) = 0
		[HDR][Space(10)] _FogColorStart( "Fog Color Start", Color ) = ( 0.4411765, 0.722515, 1, 0 )
		[HDR] _FogColorEnd( "Fog Color End", Color ) = ( 0.4411765, 0.722515, 1, 0 )
		_FogColorDuo( "Fog Color Duo", Range( 0, 1 ) ) = 1
		[Space(10)] _FogDistanceStart( "Fog Distance Start", Float ) = 0
		_FogDistanceEnd( "Fog Distance End", Float ) = 100
		_FogDistanceFalloff( "Fog Distance Falloff", Range( 1, 8 ) ) = 2
		[Space(10)] _FogHeightStart( "Fog Height Start", Float ) = 0
		_FogHeightEnd( "Fog Height End", Float ) = 100
		_FogHeightFalloff( "Fog Height Falloff", Range( 1, 8 ) ) = 2
		[Space(10)] _FarDistanceHeight( "Far Distance Height", Float ) = 0
		_FarDistanceOffset( "Far Distance Offset", Float ) = 0
		[StyledCategory(Skybox Settings)] _SkyboxCat( "[ Skybox Cat ]", Float ) = 1
		_SkyboxFogIntensity( "Skybox Fog Intensity", Range( 0, 1 ) ) = 0
		_SkyboxFogHeight( "Skybox Fog Height", Range( 0, 8 ) ) = 1
		_SkyboxFogFalloff( "Skybox Fog Falloff", Range( 1, 8 ) ) = 2
		_SkyboxFogOffset( "Skybox Fog Offset", Range( -1, 1 ) ) = 0
		_SkyboxFogBottom( "Skybox Fog Bottom", Range( 0, 1 ) ) = 0
		_SkyboxFogFill( "Skybox Fog Fill", Range( 0, 1 ) ) = 0
		[StyledCategory(Directional Settings)] _DirectionalCat( "[ Directional Cat ]", Float ) = 1
		[HDR] _DirectionalColor( "Directional Color", Color ) = ( 1, 0.8280286, 0.6084906, 0 )
		_DirectionalIntensity( "Directional Intensity", Range( 0, 1 ) ) = 1
		_DirectionalFalloff( "Directional Falloff", Range( 1, 8 ) ) = 2
		[StyledVector(18)] _DirectionalDir( "Directional Dir", Vector ) = ( 1, 1, 1, 0 )
		[StyledCategory(Noise Settings)] _NoiseCat( "[ Noise Cat ]", Float ) = 1
		_NoiseIntensity( "Noise Intensity", Range( 0, 1 ) ) = 1
		_NoiseMin( "Noise Min", Range( 0, 1 ) ) = 0
		_NoiseMax( "Noise Max", Range( 0, 1 ) ) = 1
		_NoiseScale( "Noise Scale", Float ) = 30
		[StyledVector(18)] _NoiseSpeed( "Noise Speed", Vector ) = ( 0.5, 0.5, 0, 0 )
		[Space(10)] _NoiseDistanceEnd( "Noise Distance End", Float ) = 200
		[StyledCategory(Advanced Settings)] _AdvancedCat( "[ Advanced Cat ]", Float ) = 1
		_JitterIntensity( "Jitter Intensity", Float ) = 0
		[HideInInspector] _FogAxisOption( "_FogAxisOption", Vector ) = ( 0, 0, 0, 0 )
		[HideInInspector] _HeightFogStandalone( "_HeightFogStandalone", Float ) = 1
		[HideInInspector] _IsHeightFogShader( "_IsHeightFogShader", Float ) = 1


		//_TessPhongStrength( "Tess Phong Strength", Range( 0, 1 ) ) = 0.5
		//_TessValue( "Tess Max Tessellation", Range( 1, 32 ) ) = 16
		//_TessMin( "Tess Min Distance", Float ) = 10
		//_TessMax( "Tess Max Distance", Float ) = 25
		//_TessEdgeLength ( "Tess Edge length", Range( 2, 50 ) ) = 16
		//_TessMaxDisp( "Tess Max Displacement", Float ) = 25

		[HideInInspector] _QueueOffset("_QueueOffset", Float) = 0
        [HideInInspector] _QueueControl("_QueueControl", Float) = -1

        [HideInInspector][NoScaleOffset] unity_Lightmaps("unity_Lightmaps", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset] unity_LightmapsInd("unity_LightmapsInd", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset] unity_ShadowMasks("unity_ShadowMasks", 2DArray) = "" {}

		[HideInInspector][ToggleUI] _AddPrecomputedVelocity("Add Precomputed Velocity", Float) = 1
		//[HideInInspector][ToggleUI] _ReceiveShadows("Receive Shadows", Float) = 1.0

		[HideInInspector] _XRMotionVectorsPass("_XRMotionVectorsPass", Float) = 1

		//[HideInInspector] _AlphaClip("__clip", Float) = 0.0
	}

	SubShader
	{
		PackageRequirements
		{
			"com.unity.render-pipelines.universal": "[17.0,18.0]"
		}

		

		

		Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" "UniversalMaterialType"="Unlit" }

	LOD 0

		Cull Front
		AlphaToMask Off

		

		HLSLINCLUDE
		#pragma target 3.0
		#pragma prefer_hlslcc gles
		// ensure rendering platforms toggle list is visible

		#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
		#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Filtering.hlsl"

		#ifndef ASE_TESS_FUNCS
		#define ASE_TESS_FUNCS
		float4 FixedTess( float tessValue )
		{
			return tessValue;
		}

		float CalcDistanceTessFactor (float4 vertex, float minDist, float maxDist, float tess, float4x4 o2w, float3 cameraPos )
		{
			float3 wpos = mul(o2w,vertex).xyz;
			float dist = distance (wpos, cameraPos);
			float f = clamp(1.0 - (dist - minDist) / (maxDist - minDist), 0.01, 1.0) * tess;
			return f;
		}

		float4 CalcTriEdgeTessFactors (float3 triVertexFactors)
		{
			float4 tess;
			tess.x = 0.5 * (triVertexFactors.y + triVertexFactors.z);
			tess.y = 0.5 * (triVertexFactors.x + triVertexFactors.z);
			tess.z = 0.5 * (triVertexFactors.x + triVertexFactors.y);
			tess.w = (triVertexFactors.x + triVertexFactors.y + triVertexFactors.z) / 3.0f;
			return tess;
		}

		float CalcEdgeTessFactor (float3 wpos0, float3 wpos1, float edgeLen, float3 cameraPos, float4 scParams )
		{
			float dist = distance (0.5 * (wpos0+wpos1), cameraPos);
			float len = distance(wpos0, wpos1);
			float f = max(len * scParams.y / (edgeLen * dist), 1.0);
			return f;
		}

		float DistanceFromPlane (float3 pos, float4 plane)
		{
			float d = dot (float4(pos,1.0f), plane);
			return d;
		}

		bool WorldViewFrustumCull (float3 wpos0, float3 wpos1, float3 wpos2, float cullEps, float4 planes[6] )
		{
			float4 planeTest;
			planeTest.x = (( DistanceFromPlane(wpos0, planes[0]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos1, planes[0]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos2, planes[0]) > -cullEps) ? 1.0f : 0.0f );
			planeTest.y = (( DistanceFromPlane(wpos0, planes[1]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos1, planes[1]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos2, planes[1]) > -cullEps) ? 1.0f : 0.0f );
			planeTest.z = (( DistanceFromPlane(wpos0, planes[2]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos1, planes[2]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos2, planes[2]) > -cullEps) ? 1.0f : 0.0f );
			planeTest.w = (( DistanceFromPlane(wpos0, planes[3]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos1, planes[3]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos2, planes[3]) > -cullEps) ? 1.0f : 0.0f );
			return !all (planeTest);
		}

		float4 DistanceBasedTess( float4 v0, float4 v1, float4 v2, float tess, float minDist, float maxDist, float4x4 o2w, float3 cameraPos )
		{
			float3 f;
			f.x = CalcDistanceTessFactor (v0,minDist,maxDist,tess,o2w,cameraPos);
			f.y = CalcDistanceTessFactor (v1,minDist,maxDist,tess,o2w,cameraPos);
			f.z = CalcDistanceTessFactor (v2,minDist,maxDist,tess,o2w,cameraPos);

			return CalcTriEdgeTessFactors (f);
		}

		float4 EdgeLengthBasedTess( float4 v0, float4 v1, float4 v2, float edgeLength, float4x4 o2w, float3 cameraPos, float4 scParams )
		{
			float3 pos0 = mul(o2w,v0).xyz;
			float3 pos1 = mul(o2w,v1).xyz;
			float3 pos2 = mul(o2w,v2).xyz;
			float4 tess;
			tess.x = CalcEdgeTessFactor (pos1, pos2, edgeLength, cameraPos, scParams);
			tess.y = CalcEdgeTessFactor (pos2, pos0, edgeLength, cameraPos, scParams);
			tess.z = CalcEdgeTessFactor (pos0, pos1, edgeLength, cameraPos, scParams);
			tess.w = (tess.x + tess.y + tess.z) / 3.0f;
			return tess;
		}

		float4 EdgeLengthBasedTessCull( float4 v0, float4 v1, float4 v2, float edgeLength, float maxDisplacement, float4x4 o2w, float3 cameraPos, float4 scParams, float4 planes[6] )
		{
			float3 pos0 = mul(o2w,v0).xyz;
			float3 pos1 = mul(o2w,v1).xyz;
			float3 pos2 = mul(o2w,v2).xyz;
			float4 tess;

			if (WorldViewFrustumCull(pos0, pos1, pos2, maxDisplacement, planes))
			{
				tess = 0.0f;
			}
			else
			{
				tess.x = CalcEdgeTessFactor (pos1, pos2, edgeLength, cameraPos, scParams);
				tess.y = CalcEdgeTessFactor (pos2, pos0, edgeLength, cameraPos, scParams);
				tess.z = CalcEdgeTessFactor (pos0, pos1, edgeLength, cameraPos, scParams);
				tess.w = (tess.x + tess.y + tess.z) / 3.0f;
			}
			return tess;
		}
		#endif //ASE_TESS_FUNCS
		ENDHLSL

		
		Pass
		{
			
			Name "Forward"
			Tags { "LightMode"="UniversalForwardOnly" }

			Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
			ZWrite Off
			ZTest Always
			ZClip False
			Offset 0,0
			ColorMask RGBA

			

			HLSLPROGRAM

			#define _RECEIVE_SHADOWS_OFF
			#define _SURFACE_TYPE_TRANSPARENT 1
			#define ASE_VERSION 19909
			#define ASE_SRP_VERSION 170004
			#define REQUIRE_DEPTH_TEXTURE 1


			#pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3

			#pragma multi_compile_fragment _ DEBUG_DISPLAY

			#pragma vertex vert
			#pragma fragment frag

			// Option "Keep Lighting Variants" @david please keep this note for future changes in Unlit  lighting
			//#define UNLIT_REALTIME_LIGHTING 1
			//#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
			//#pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
			//#pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
			//#pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
			//#pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
			//#pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
			//#pragma multi_compile _ REFLECTION_PROBE_ROTATION
			//#pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
			//#pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
			//#pragma multi_compile _ SHADOWS_SHADOWMASK
			//#pragma multi_compile_fragment _ _LIGHT_LAYERS
			//#pragma multi_compile_fragment _ _LIGHT_COOKIES
			//#pragma multi_compile _ _CLUSTER_LIGHT_LOOP

			#define SHADERPASS SHADERPASS_UNLIT

			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
			#if ( UNITY_VERSION >= 60010000 )
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
			#else
			#pragma multi_compile_fog
			#endif
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Debug/Debugging3D.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceData.hlsl"

			#if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

			#define ASE_NEEDS_FRAG_SCREEN_POSITION_NORMALIZED
			#define ASE_NEEDS_WORLD_POSITION
			#define ASE_NEEDS_FRAG_WORLD_POSITION
			#define ASE_NEEDS_FRAG_POSITION
			#define ASE_NEEDS_FRAG_SCREEN_POSITION
			#pragma multi_compile_local AHF_CAMERAMODE_PERSPECTIVE AHF_CAMERAMODE_ORTHOGRAPHIC AHF_CAMERAMODE_BOTH
			//Atmospheric Height Fog Defines
			//#define AHF_DISABLE_NOISE3D
			//#define AHF_DISABLE_DIRECTIONAL
			//#define AHF_DISABLE_SKYBOXFOG
			//#define AHF_DISABLE_FALLOFF
			//#define AHF_DEBUG_WORLDPOS


			#if defined(ASE_EARLY_Z_DEPTH_OPTIMIZE) && (SHADER_TARGET >= 45)
				#define ASE_SV_DEPTH SV_DepthLessEqual
				#define ASE_SV_POSITION_QUALIFIERS linear noperspective centroid
			#else
				#define ASE_SV_DEPTH SV_Depth
				#define ASE_SV_POSITION_QUALIFIERS
			#endif

			struct Attributes
			{
				float4 positionOS : POSITION;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct PackedVaryings
			{
				ASE_SV_POSITION_QUALIFIERS float4 positionCS : SV_POSITION;
				float4 positionWSAndFogFactor : TEXCOORD0;
				half3 normalWS : TEXCOORD1;
				half4 tangentWS : TEXCOORD2;
				float4 ase_texcoord3 : TEXCOORD3;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			half4 _DirectionalColor;
			half4 _FogColorStart;
			half4 _FogColorEnd;
			half3 _NoiseSpeed;
			half3 _DirectionalDir;
			half3 _FogAxisOption;
			half _SkyboxFogOffset;
			half _FogIntensity;
			half _NoiseIntensity;
			half _NoiseDistanceEnd;
			half _FarDistanceHeight;
			half _SkyboxFogFalloff;
			half _NoiseMax;
			half _NoiseMin;
			half _SkyboxFogBottom;
			half _NoiseScale;
			half _FogLayersMode;
			half _FogHeightFalloff;
			half _FogHeightStart;
			float _FarDistanceOffset;
			half _SkyboxFogHeight;
			half _FogHeightEnd;
			float _Banner;
			half _SkyboxFogFill;
			half _HeightFogStandalone;
			half _IsHeightFogShader;
			half _FogCameraMode;
			half _FogDistanceStart;
			half _FogDistanceEnd;
			half _FogDistanceFalloff;
			half _FogColorDuo;
			half _JitterIntensity;
			half _DirectionalIntensity;
			half _DirectionalFalloff;
			half _FogCat;
			half _SkyboxCat;
			half _DirectionalCat;
			half _NoiseCat;
			half _AdvancedCat;
			half _FogAxisMode;
			half _SkyboxFogIntensity;
			float _AlphaClip;
			float _Cutoff;
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			

			float4 mod289( float4 x )
			{
				return x - floor(x * (1.0 / 289.0)) * 289.0;
			}
			
			float4 perm( float4 x )
			{
				return mod289(((x * 34.0) + 1.0) * x);
			}
			
			float SimpleNoise3D( float3 p )
			{
				    float3 a = floor(p);
				    float3 d = p - a;
				    d = d * d * (3.0 - 2.0 * d);
				    float4 b = a.xxyy + float4(0.0, 1.0, 0.0, 1.0);
				    float4 k1 = perm(b.xyxy);
				    float4 k2 = perm(k1.xyxy + b.zzww);
				    float4 c = k2 + a.zzzz;
				    float4 k3 = perm(c);
				    float4 k4 = perm(c + 1.0);
				    float4 o1 = frac(k3 * (1.0 / 41.0));
				    float4 o2 = frac(k4 * (1.0 / 41.0));
				    float4 o3 = o2 * d.z + o1 * (1.0 - d.z);
				    float2 o4 = o3.yw * d.x + o3.xz * (1.0 - d.x);
				    return o4.y * d.y + o4.x * (1.0 - d.y);
			}
			

			PackedVaryings VertexFunction( Attributes input  )
			{
				PackedVaryings output = (PackedVaryings)0;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				output.ase_texcoord3 = input.positionOS;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = input.positionOS.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = defaultVertexValue;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					input.positionOS.xyz = vertexValue;
				#else
					input.positionOS.xyz += vertexValue;
				#endif

				input.normalOS = input.normalOS;
				input.tangentOS = input.tangentOS;

				VertexPositionInputs vertexInput = GetVertexPositionInputs( input.positionOS.xyz );
				VertexNormalInputs normalInput = GetVertexNormalInputs( input.normalOS, input.tangentOS );

				float fogFactor = 0;
				#if defined(ASE_FOG) && !defined(_FOG_FRAGMENT)
					fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
				#endif

				output.positionCS = vertexInput.positionCS;
				output.positionWSAndFogFactor = float4( vertexInput.positionWS, fogFactor );
				output.normalWS = normalInput.normalWS;
				output.tangentWS = half4( normalInput.tangentWS, ( input.tangentOS.w > 0.0 ? 1.0 : -1.0 ) * GetOddNegativeScale() );;
				return output;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 positionOS : INTERNALTESSPOS;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct TessellationFactors
			{
				float edge[3] : SV_TessFactor;
				float inside : SV_InsideTessFactor;
			};

			VertexControl vert ( Attributes input )
			{
				VertexControl output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				output.positionOS = input.positionOS;
				output.normalOS = input.normalOS;
				output.tangentOS = input.tangentOS;
				
				return output;
			}

			TessellationFactors TessellationFunction (InputPatch<VertexControl,3> input)
			{
				TessellationFactors output;
				float4 tf = 1;
				float tessValue = _TessValue; float tessMin = _TessMin; float tessMax = _TessMax;
				float edgeLength = _TessEdgeLength; float tessMaxDisp = _TessMaxDisp;
				#if defined(ASE_FIXED_TESSELLATION)
				tf = FixedTess( tessValue );
				#elif defined(ASE_DISTANCE_TESSELLATION)
				tf = DistanceBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, tessValue, tessMin, tessMax, GetObjectToWorldMatrix(), _WorldSpaceCameraPos );
				#elif defined(ASE_LENGTH_TESSELLATION)
				tf = EdgeLengthBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams );
				#elif defined(ASE_LENGTH_CULL_TESSELLATION)
				tf = EdgeLengthBasedTessCull(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, tessMaxDisp, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams, unity_CameraWorldClipPlanes );
				#endif
				output.edge[0] = tf.x; output.edge[1] = tf.y; output.edge[2] = tf.z; output.inside = tf.w;
				return output;
			}

			[domain("tri")]
			[partitioning("fractional_odd")]
			[outputtopology("triangle_cw")]
			[patchconstantfunc("TessellationFunction")]
			[outputcontrolpoints(3)]
			VertexControl HullFunction(InputPatch<VertexControl, 3> patch, uint id : SV_OutputControlPointID)
			{
				return patch[id];
			}

			[domain("tri")]
			PackedVaryings DomainFunction(TessellationFactors factors, OutputPatch<VertexControl, 3> patch, float3 bary : SV_DomainLocation)
			{
				Attributes output = (Attributes) 0;
				output.positionOS = patch[0].positionOS * bary.x + patch[1].positionOS * bary.y + patch[2].positionOS * bary.z;
				output.normalOS = patch[0].normalOS * bary.x + patch[1].normalOS * bary.y + patch[2].normalOS * bary.z;
				output.tangentOS = patch[0].tangentOS * bary.x + patch[1].tangentOS * bary.y + patch[2].tangentOS * bary.z;
				
				#if defined(ASE_PHONG_TESSELLATION)
				float3 pp[3];
				for (int i = 0; i < 3; ++i)
					pp[i] = output.positionOS.xyz - patch[i].normalOS * (dot(output.positionOS.xyz, patch[i].normalOS) - dot(patch[i].positionOS.xyz, patch[i].normalOS));
				float phongStrength = _TessPhongStrength;
				output.positionOS.xyz = phongStrength * (pp[0]*bary.x + pp[1]*bary.y + pp[2]*bary.z) + (1.0f-phongStrength) * output.positionOS.xyz;
				#endif
				UNITY_TRANSFER_INSTANCE_ID(patch[0], output);
				return VertexFunction(output);
			}
			#else
			PackedVaryings vert ( Attributes input )
			{
				return VertexFunction( input );
			}
			#endif

			half4 frag ( PackedVaryings input
						#if defined( ASE_DEPTH_WRITE_ON )
						,out float outputDepth : ASE_SV_DEPTH
						#endif
						#ifdef _WRITE_RENDERING_LAYERS
						#if ( UNITY_VERSION >= 60020000 )
						, out uint outRenderingLayers : SV_Target1
						#else
						, out float4 outRenderingLayers : SV_Target1
						#endif
						#endif
						 ) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

				#if defined( _SURFACE_TYPE_TRANSPARENT )
					const bool isTransparent = true;
				#else
					const bool isTransparent = false;
				#endif

				#if defined(LOD_FADE_CROSSFADE)
					LODFadeCrossFade( input.positionCS );
				#endif

				#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
					float4 shadowCoord = TransformWorldToShadowCoord( input.positionWSAndFogFactor.xyz );
				#else
					float4 shadowCoord = float4(0, 0, 0, 0);
				#endif

				// @diogo: mikktspace compliant
				float renormFactor = 1.0 / max( FLT_MIN, length( input.normalWS ) );

				float3 PositionWS = input.positionWSAndFogFactor.xyz;
				float3 PositionRWS = GetCameraRelativePositionWS( PositionWS );
				half3 ViewDirWS = GetWorldSpaceNormalizeViewDir( PositionWS );
				float4 ShadowCoord = shadowCoord;
				float4 ScreenPosNorm = float4( GetNormalizedScreenSpaceUV( input.positionCS ), input.positionCS.zw );
				float4 ClipPos = ComputeClipSpacePosition( ScreenPosNorm.xy, input.positionCS.z ) * input.positionCS.w;
				float4 ScreenPos = ComputeScreenPos( ClipPos );
				float3 TangentWS = input.tangentWS.xyz * renormFactor;
				float3 BitangentWS = cross( input.normalWS, input.tangentWS.xyz ) * input.tangentWS.w * renormFactor;
				float3 NormalWS = input.normalWS * renormFactor;

				float depthLinearEye218_g1067 = LinearEyeDepth( SHADERGRAPH_SAMPLE_SCENE_DEPTH( ScreenPosNorm.xy ), _ZBufferParams );
				float4 unityObjectToClipPos224_g1067 = TransformWorldToHClip( TransformObjectToWorld( ( input.ase_texcoord3.xyz ).xyz ) );
				float4 computeScreenPos225_g1067 = ComputeScreenPos( unityObjectToClipPos224_g1067 );
				half3 WorldPosFromDepth_SRP567_g1067 = ( _WorldSpaceCameraPos - ( depthLinearEye218_g1067 * ( ( _WorldSpaceCameraPos - PositionWS ) / computeScreenPos225_g1067.w ) ) );
				float3 objToView587_g1067 = TransformWorldToView( TransformObjectToWorld(input.ase_texcoord3.xyz) );
				float depth01_572_g1067 = SHADERGRAPH_SAMPLE_SCENE_DEPTH( ScreenPosNorm.xy );
				float lerpResult577_g1067 = lerp( ( 1.0 - depth01_572_g1067 ) , depth01_572_g1067 , saturate( _ProjectionParams.x ));
				float lerpResult579_g1067 = lerp( _ProjectionParams.y , _ProjectionParams.z , lerpResult577_g1067);
				float3 appendResult582_g1067 = (float3(objToView587_g1067.x , objToView587_g1067.y , -lerpResult579_g1067));
				float3 viewToWorld583_g1067 = mul( UNITY_MATRIX_I_V, float4( appendResult582_g1067, 1.0 ) ).xyz;
				half3 WorldPosFromDepth_SRP_Ortho584_g1067 = viewToWorld583_g1067;
				float3 lerpResult593_g1067 = lerp( WorldPosFromDepth_SRP567_g1067 , WorldPosFromDepth_SRP_Ortho584_g1067 , ( unity_OrthoParams.w + ( _FogCameraMode * 0.0 ) ));
				#if defined( AHF_CAMERAMODE_PERSPECTIVE )
				float3 staticSwitch598_g1067 = WorldPosFromDepth_SRP567_g1067;
				#elif defined( AHF_CAMERAMODE_ORTHOGRAPHIC )
				float3 staticSwitch598_g1067 = WorldPosFromDepth_SRP_Ortho584_g1067;
				#elif defined( AHF_CAMERAMODE_BOTH )
				float3 staticSwitch598_g1067 = lerpResult593_g1067;
				#else
				float3 staticSwitch598_g1067 = WorldPosFromDepth_SRP567_g1067;
				#endif
				half3 WorldPosFromDepth253_g1067 = staticSwitch598_g1067;
				float3 WorldPosition2_g1067 = WorldPosFromDepth253_g1067;
				float temp_output_7_0_g1070 = _FogDistanceStart;
				float temp_output_155_0_g1067 = saturate( ( ( distance( WorldPosition2_g1067 , _WorldSpaceCameraPos ) - temp_output_7_0_g1070 ) / ( _FogDistanceEnd - temp_output_7_0_g1070 ) ) );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch467_g1067 = temp_output_155_0_g1067;
				#else
				float staticSwitch467_g1067 = ( 1.0 - pow( ( 1.0 - abs( temp_output_155_0_g1067 ) ) , _FogDistanceFalloff ) );
				#endif
				half FogDistanceMask12_g1067 = staticSwitch467_g1067;
				float3 lerpResult258_g1067 = lerp( (_FogColorStart).rgb , (_FogColorEnd).rgb , ( ( FogDistanceMask12_g1067 * FogDistanceMask12_g1067 * FogDistanceMask12_g1067 ) * _FogColorDuo ));
				float3 normalizeResult318_g1067 = normalize( ( WorldPosition2_g1067 - _WorldSpaceCameraPos ) );
				float dotResult145_g1067 = dot( normalizeResult318_g1067 , _DirectionalDir );
				float4 ScreenPos3_g1069 = ScreenPos;
				float2 UV13_g1069 = ( ( (ScreenPos3_g1069).xy / (ScreenPos3_g1069).z ) * (_ScaledScreenParams).xy );
				float3 Magic14_g1069 = float3( 0.06711056, 0.00583715, 52.98292 );
				float dotResult16_g1069 = dot( UV13_g1069 , (Magic14_g1069).xy );
				float lerpResult494_g1067 = lerp( 0.0 , frac( ( frac( dotResult16_g1069 ) * (Magic14_g1069).z ) ) , ( _JitterIntensity * 0.1 ));
				half Jitter502_g1067 = lerpResult494_g1067;
				float temp_output_140_0_g1067 = ( saturate( (( dotResult145_g1067 + Jitter502_g1067 )*0.5 + 0.5) ) * _DirectionalIntensity );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch470_g1067 = temp_output_140_0_g1067;
				#else
				float staticSwitch470_g1067 = pow( abs( temp_output_140_0_g1067 ) , _DirectionalFalloff );
				#endif
				float DirectionalMask30_g1067 = staticSwitch470_g1067;
				float3 lerpResult40_g1067 = lerp( lerpResult258_g1067 , (_DirectionalColor).rgb , DirectionalMask30_g1067);
				#ifdef AHF_DISABLE_DIRECTIONAL
				float3 staticSwitch442_g1067 = lerpResult258_g1067;
				#else
				float3 staticSwitch442_g1067 = lerpResult40_g1067;
				#endif
				half3 Input_Color6_g1068 = staticSwitch442_g1067;
				#ifdef UNITY_COLORSPACE_GAMMA
				float3 staticSwitch1_g1068 = Input_Color6_g1068;
				#else
				float3 staticSwitch1_g1068 = ( Input_Color6_g1068 * ( ( Input_Color6_g1068 * ( ( Input_Color6_g1068 * 0.305306 ) + 0.6821711 ) ) + 0.01252288 ) );
				#endif
				float3 temp_output_256_0_g1067 = staticSwitch1_g1068;
				half Drawers696_g1067 = ( _FogCat + _SkyboxCat + _DirectionalCat + _NoiseCat + _AdvancedCat );
				#ifdef AHF_DUMMY
				float3 staticSwitch702_g1067 = ( temp_output_256_0_g1067 + Drawers696_g1067 );
				#else
				float3 staticSwitch702_g1067 = temp_output_256_0_g1067;
				#endif
				half3 Final_Color462_g1067 = staticSwitch702_g1067;
				half3 AHF_FogAxisOption181_g1067 = ( _FogAxisOption + ( _FogAxisMode * 0.0 ) );
				float3 break159_g1067 = ( WorldPosition2_g1067 * AHF_FogAxisOption181_g1067 );
				float temp_output_7_0_g1071 = _FogDistanceEnd;
				float temp_output_643_0_g1067 = saturate( ( ( distance( WorldPosition2_g1067 , _WorldSpaceCameraPos ) - temp_output_7_0_g1071 ) / ( ( _FogDistanceEnd + _FarDistanceOffset ) - temp_output_7_0_g1071 ) ) );
				half FogDistanceMaskFar645_g1067 = ( temp_output_643_0_g1067 * temp_output_643_0_g1067 );
				float lerpResult614_g1067 = lerp( _FogHeightEnd , ( _FogHeightEnd + _FarDistanceHeight ) , FogDistanceMaskFar645_g1067);
				float temp_output_7_0_g1072 = lerpResult614_g1067;
				float temp_output_167_0_g1067 = saturate( ( ( ( break159_g1067.x + break159_g1067.y + break159_g1067.z ) - temp_output_7_0_g1072 ) / ( _FogHeightStart - temp_output_7_0_g1072 ) ) );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch468_g1067 = temp_output_167_0_g1067;
				#else
				float staticSwitch468_g1067 = pow( abs( temp_output_167_0_g1067 ) , _FogHeightFalloff );
				#endif
				half FogHeightMask16_g1067 = staticSwitch468_g1067;
				float lerpResult328_g1067 = lerp( ( FogDistanceMask12_g1067 * FogHeightMask16_g1067 ) , saturate( ( FogDistanceMask12_g1067 + FogHeightMask16_g1067 ) ) , _FogLayersMode);
				float mulTime204_g1067 = _TimeParameters.x * 2.0;
				float3 temp_output_197_0_g1067 = ( ( WorldPosition2_g1067 * ( 1.0 / _NoiseScale ) ) + ( -_NoiseSpeed * mulTime204_g1067 ) );
				float3 p1_g1076 = temp_output_197_0_g1067;
				float localSimpleNoise3D1_g1076 = SimpleNoise3D( p1_g1076 );
				float temp_output_7_0_g1075 = _NoiseMin;
				float temp_output_7_0_g1074 = _NoiseDistanceEnd;
				half NoiseDistanceMask7_g1067 = saturate( ( ( distance( WorldPosition2_g1067 , _WorldSpaceCameraPos ) - temp_output_7_0_g1074 ) / ( 0.0 - temp_output_7_0_g1074 ) ) );
				float lerpResult198_g1067 = lerp( 1.0 , saturate( ( ( localSimpleNoise3D1_g1076 - temp_output_7_0_g1075 ) / ( _NoiseMax - temp_output_7_0_g1075 ) ) ) , ( NoiseDistanceMask7_g1067 * _NoiseIntensity ));
				half NoiseSimplex3D24_g1067 = lerpResult198_g1067;
				#ifdef AHF_DISABLE_NOISE3D
				float staticSwitch42_g1067 = lerpResult328_g1067;
				#else
				float staticSwitch42_g1067 = ( lerpResult328_g1067 * NoiseSimplex3D24_g1067 );
				#endif
				float temp_output_454_0_g1067 = ( staticSwitch42_g1067 * _FogIntensity );
				float3 normalizeResult169_g1067 = normalize( ( WorldPosition2_g1067 - _WorldSpaceCameraPos ) );
				float3 break170_g1067 = ( normalizeResult169_g1067 * AHF_FogAxisOption181_g1067 );
				float temp_output_449_0_g1067 = ( ( break170_g1067.x + break170_g1067.y + break170_g1067.z ) + -_SkyboxFogOffset );
				float temp_output_7_0_g1073 = _SkyboxFogHeight;
				float temp_output_176_0_g1067 = saturate( ( ( abs( temp_output_449_0_g1067 ) - temp_output_7_0_g1073 ) / ( 0.0 - temp_output_7_0_g1073 ) ) );
				float saferPower309_g1067 = abs( temp_output_176_0_g1067 );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch469_g1067 = temp_output_176_0_g1067;
				#else
				float staticSwitch469_g1067 = pow( saferPower309_g1067 , _SkyboxFogFalloff );
				#endif
				float lerpResult179_g1067 = lerp( saturate( ( staticSwitch469_g1067 + ( _SkyboxFogBottom * step( temp_output_449_0_g1067 , 0.0 ) ) ) ) , 1.0 , _SkyboxFogFill);
				half SkyboxFogHeightMask108_g1067 = ( lerpResult179_g1067 * _SkyboxFogIntensity );
				float depth01_118_g1067 = SHADERGRAPH_SAMPLE_SCENE_DEPTH( ScreenPosNorm.xy );
				#ifdef UNITY_REVERSED_Z
				float staticSwitch123_g1067 = depth01_118_g1067;
				#else
				float staticSwitch123_g1067 = ( 1.0 - depth01_118_g1067 );
				#endif
				half SkyboxFogMask95_g1067 = ( 1.0 - ceil( staticSwitch123_g1067 ) );
				float lerpResult112_g1067 = lerp( temp_output_454_0_g1067 , SkyboxFogHeightMask108_g1067 , SkyboxFogMask95_g1067);
				#ifdef AHF_DISABLE_SKYBOXFOG
				float staticSwitch455_g1067 = temp_output_454_0_g1067;
				#else
				float staticSwitch455_g1067 = lerpResult112_g1067;
				#endif
				#ifdef AHF_DUMMY
				float staticSwitch705_g1067 = ( staticSwitch455_g1067 + Drawers696_g1067 );
				#else
				float staticSwitch705_g1067 = staticSwitch455_g1067;
				#endif
				half Final_Alpha463_g1067 = staticSwitch705_g1067;
				float4 appendResult114_g1067 = (float4(Final_Color462_g1067 , Final_Alpha463_g1067));
				float4 appendResult457_g1067 = (float4(WorldPosition2_g1067 , 1.0));
				#ifdef AHF_DEBUG_WORLDPOS
				float4 staticSwitch456_g1067 = appendResult457_g1067;
				#else
				float4 staticSwitch456_g1067 = appendResult114_g1067;
				#endif
				
				float3 BakedAlbedo = 0;
				float3 BakedEmission = 0;
				float3 Color = (staticSwitch456_g1067).xyz;
				float Alpha = (staticSwitch456_g1067).w;
				#if defined( _ALPHATEST_ON )
					float AlphaClipThreshold = _Cutoff;
					float AlphaClipThresholdShadow = 0.5;
				#endif


				#if defined( ASE_DEPTH_WRITE_ON )
					input.positionCS.z = input.positionCS.z;
				#endif

				#if defined( _ALPHATEST_ON )
					AlphaDiscard( Alpha, AlphaClipThreshold );
				#endif

				#if defined(MAIN_LIGHT_CALCULATE_SHADOWS) && defined(ASE_CHANGES_WORLD_POS)
					ShadowCoord = TransformWorldToShadowCoord( PositionWS );
				#endif

				InputData inputData = (InputData)0;
				inputData.positionWS = PositionWS;
				inputData.positionCS = input.positionCS;
				inputData.normalizedScreenSpaceUV = ScreenPosNorm.xy;
				inputData.normalWS = NormalWS;
				inputData.viewDirectionWS = ViewDirWS;

				#if defined(_SCREEN_SPACE_OCCLUSION) && !defined(_SURFACE_TYPE_TRANSPARENT) && defined(UNLIT_DEFAULT_SSAO)
					float2 normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
					AmbientOcclusionFactor aoFactor = GetScreenSpaceAmbientOcclusion(normalizedScreenSpaceUV);
					Color.rgb *= aoFactor.directAmbientOcclusion;
				#endif

				#ifdef ASE_FOG
					inputData.fogCoord = InitializeInputDataFog(float4(inputData.positionWS, 1.0), input.positionWSAndFogFactor.w);
				#endif

				#if defined(_DBUFFER) && defined(UNLIT_DEFAULT_DECAL_BLENDING)
					ApplyDecalToBaseColor(input.positionCS, Color);
				#endif

				#ifdef ASE_FOG
					#ifdef TERRAIN_SPLAT_ADDPASS
						Color.rgb = MixFogColor(Color.rgb, half3(0,0,0), inputData.fogCoord);
					#else
						Color.rgb = MixFog(Color.rgb, inputData.fogCoord);
					#endif
				#endif

				#if defined( ASE_DEPTH_WRITE_ON )
					outputDepth = input.positionCS.z;
				#endif

				#ifdef _WRITE_RENDERING_LAYERS
					#if ( UNITY_VERSION >= 60020000 )
					outRenderingLayers = EncodeMeshRenderingLayer();
					#else
					uint renderingLayers = GetMeshRenderingLayer();
					outRenderingLayers = float4( EncodeMeshRenderingLayer( renderingLayers ), 0, 0, 0 );
					#endif
				#endif

				#if defined( ASE_OPAQUE_KEEP_ALPHA )
					return half4( Color, Alpha );
				#else
					return half4( Color, OutputAlpha( Alpha, isTransparent ) );
				#endif
			}
			ENDHLSL
		}

	
	}
	

	

	CustomEditor "AtmosphericHeightFog.MaterialGUI"
	FallBack "Hidden/Shader Graph/FallbackError"
	
	Fallback Off
}
/*ASEBEGIN
Version=19909
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;1093;-3328,-4736;Inherit;False;Property;_Banner;Banner;0;0;Create;True;0;0;0;True;1;StyledBanner(Height Fog Standalone);False;0;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;1107;-3136,-4736;Half;False;Property;_HeightFogStandalone;_HeightFogStandalone;43;1;[HideInInspector];Create;False;0;0;0;True;0;False;1;1;1;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;1106;-2880,-4736;Half;False;Property;_IsHeightFogShader;_IsHeightFogShader;44;1;[HideInInspector];Create;False;0;0;0;True;0;False;1;1;1;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.FunctionNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;1138;-3328,-4608;Inherit;False;Base;1;;1067;13c50910e5b86de4097e1181ba121e0e;38,360,1,380,1,372,1,384,1,476,1,450,1,370,1,374,1,378,1,386,1,555,1,557,1,388,1,550,1,368,1,349,1,376,1,382,1,347,1,351,1,339,1,392,1,355,1,116,1,364,1,361,1,366,1,704,1,597,1,354,1,99,1,500,1,603,1,681,1,345,1,685,1,343,1,700,1;0;3;FLOAT4;113;FLOAT3;86;FLOAT;87
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;1120;-3072,-4608;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;ExtraPrePass;0;0;ExtraPrePass;6;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;255;False;;255;False;;255;False;;7;False;;1;False;;1;False;;1;False;;7;False;;1;False;;1;False;;1;False;;False;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;0;True;14;all;0;False;True;1;1;False;;0;False;;0;1;False;;0;False;;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;True;True;True;True;0;False;;False;False;False;False;False;False;False;True;False;255;False;;255;False;;255;False;;7;False;;1;False;;1;False;;1;False;;7;False;;1;False;;1;False;;1;False;;False;True;1;False;;True;3;False;;True;True;0;False;;0;False;;False;True;0;False;False;0;Hidden/InternalErrorShader;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;1121;-3072,-4608;Float;False;True;-1;2;AtmosphericHeightFog.MaterialGUI;0;27;BOXOPHOBIC/Atmospherics/Height Fog Standalone;2992e84f91cbeb14eab234972e07ea9d;True;Forward;0;1;Forward;10;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;1;False;;False;False;False;False;False;False;False;False;False;True;False;255;False;;255;False;;255;False;;7;False;;1;False;;1;False;;1;False;;7;False;;1;False;;1;False;;1;False;;False;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Transparent=RenderType;Queue=Transparent=Queue=0;UniversalMaterialType=Unlit;True;2;True;14;all;0;False;True;1;5;False;;10;False;;1;1;False;;10;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;True;True;True;True;0;False;;False;False;False;False;False;False;False;True;False;255;False;;255;False;;255;False;;7;False;;1;False;;1;False;;1;False;;7;False;;1;False;;1;False;;1;False;;False;True;2;False;;True;7;False;;True;False;0;False;;0;False;;False;True;1;LightMode=UniversalForwardOnly;False;False;0;;0;0;Standard;31;Surface;1;638871778064737377;  Keep Alpha;0;0;  Blend;0;0;Two Sided;2;638871778099624056;Alpha Clipping;0;638871778108831928;  Use Shadow Threshold;0;0;Forward Only;0;0;Cast Shadows;0;0;Receive Shadows;0;0;Receive SSAO;0;638923248986423004;Default Decal Blending;0;639119259971817262;Motion Vectors;0;638923248989534623;  Add Precomputed Velocity;0;0;  XR Motion Vectors;0;0;GPU Instancing;0;0;LOD CrossFade;0;0;Built-in Fog;0;0;Meta Pass;0;0;Extra Pre Pass;0;0;Tessellation;0;0;  Phong;0;0;  Strength;0.5,False,;0;  Type;0;0;  Tess;16,False,;0;  Min;10,False,;0;  Max;25,False,;0;  Edge Length;16,False,;0;  Max Displacement;25,False,;0;Write Depth;0;0;  Early Z;0;0;Vertex Position;1;0;0;13;False;True;False;False;False;False;False;False;False;False;False;False;False;False;;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;1122;-3072,-4608;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;ShadowCaster;0;2;ShadowCaster;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;255;False;;255;False;;255;False;;7;False;;1;False;;1;False;;1;False;;7;False;;1;False;;1;False;;1;False;;False;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;0;True;14;all;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;False;False;True;False;False;False;False;0;False;;False;False;False;False;False;False;False;False;False;True;1;False;;True;3;False;;False;False;True;1;LightMode=ShadowCaster;False;False;0;Hidden/InternalErrorShader;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;1123;-3072,-4608;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;DepthOnly;0;3;DepthOnly;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;255;False;;255;False;;255;False;;7;False;;1;False;;1;False;;1;False;;7;False;;1;False;;1;False;;1;False;;False;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;0;True;14;all;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;False;False;True;False;False;False;False;0;False;;False;False;False;False;False;False;False;False;False;True;1;False;;False;False;False;True;1;LightMode=DepthOnly;False;False;0;Hidden/InternalErrorShader;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;1124;-3072,-4608;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;Meta;0;4;Meta;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;255;False;;255;False;;255;False;;7;False;;1;False;;1;False;;1;False;;7;False;;1;False;;1;False;;1;False;;False;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;0;True;14;all;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;2;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;LightMode=Meta;False;False;0;Hidden/InternalErrorShader;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;1128;-3072,-4558;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;Universal2D;0;5;Universal2D;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;3;True;14;all;0;False;True;1;1;False;;0;False;;0;1;False;;0;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;True;True;True;True;0;False;;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;True;1;False;;True;3;False;;True;True;0;False;;0;False;;False;True;1;LightMode=Universal2D;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;1129;-3072,-4558;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;SceneSelectionPass;0;6;SceneSelectionPass;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;3;True;14;all;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;2;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;LightMode=SceneSelectionPass;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;1130;-3072,-4558;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;ScenePickingPass;0;7;ScenePickingPass;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;3;True;14;all;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;LightMode=Picking;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;1131;-3072,-4558;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;DepthNormals;0;8;DepthNormals;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;3;True;14;all;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;False;;True;3;False;;False;False;True;1;LightMode=DepthNormalsOnly;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;1132;-3072,-4558;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;DepthNormalsOnly;0;9;DepthNormalsOnly;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;3;True;14;all;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;False;;True;3;False;;False;False;True;1;LightMode=DepthNormalsOnly;False;True;11;d3d11;metal;vulkan;xboxone;xboxseries;playstation;ps4;ps5;switch;switch2;webgpu;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;1139;-3072,-4508;Float;False;False;-1;3;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;MotionVectors;0;10;MotionVectors;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;5;True;14;all;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;True;True;False;False;0;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;LightMode=MotionVectors;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;1140;-3072,-4508;Float;False;False;-1;3;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;XRMotionVectors;0;11;XRMotionVectors;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;5;True;14;all;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;True;True;True;True;0;False;;False;False;False;False;False;False;False;True;True;1;False;;255;False;;1;False;;7;False;;3;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;False;True;1;LightMode=XRMotionVectors;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;1141;-3072,-4508;Float;False;False;-1;3;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;GBuffer;0;12;GBuffer;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;5;True;14;all;0;False;True;1;1;False;;0;False;;0;1;False;;0;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;True;True;True;True;0;False;;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;True;1;False;;True;3;False;;True;True;0;False;;0;False;;False;True;1;LightMode=UniversalGBuffer;False;True;12;d3d11;gles;metal;vulkan;xboxone;xboxseries;playstation;ps4;ps5;switch;switch2;webgpu;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;1105;-3328,-4864;Inherit;False;919.8825;100;Drawers;0;;1,0.475862,0,1;0;0
WireConnection;1121;2;1138;86
WireConnection;1121;3;1138;87
ASEEND*/
//CHKSM=E7260F2359CEAB6DE481337875167EF6FF36A7CB