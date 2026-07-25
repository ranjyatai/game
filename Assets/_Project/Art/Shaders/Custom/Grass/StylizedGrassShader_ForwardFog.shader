//Stylized Grass Shader
//Staggart Creations (http://staggart.xyz)
//Copyright protected under Unity Asset Store EULA

%asset_version%
%unity_version%
%compiler_version%

%shader_name%
{
	Properties
	{
		[MainTexture] _BaseMap("Albedo", 2D) = "white" {}
		_Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
		[MaterialEnum(Both,0,Front,1,Back,2)] _Cull("Render faces", Float) = 0
		[Toggle] _AlphaToCoverage("Alpha to coverage", Float) = 0.0
		
		[MaterialEnum(Red,0,Green,1,Blue,2,Alpha,3)] _VertexColorShadingChannel("Vertex Color Shading Channel", Float) = 0.0
		[MaterialEnum(Red,0,Green,1,Blue,2,Alpha,3)] _VertexColorWindChannel("Vertex Color Wind Channel", Float) = 0.0
		[MaterialEnum(Red,0,Green,1,Blue,2,Alpha,3)] _VertexColorBendingChannel("Vertex Color Bending Channel", Float) = 0.0

		//[Header(Shading)]
		[MainColor] _BaseColor("Color", Color) = (0.49, 0.89, 0.12, 1.0)
		_HueVariation("Hue Variation (Alpha = Intensity)", Color) = (1, 0.63, 0, 0.15)
		_HueVariationHeight("Hue Variation Height", Range(0.0, 1.0)) = 0.0
		_ColorMapStrength("Colormap Strength", Range(0.0, 1.0)) = 0.0
		_ColorMapHeight("Colormap Height", Range(0.0, 1.0)) = 1.0
		_ScalemapInfluence("Scale influence", vector) = (0,1,0,0)
		_OcclusionStrength("Ambient Occlusion", Range(0.0, 1.0)) = 0.25
		_VertexDarkening("Random Darkening", Range(0, 1)) = 0.1
		_Smoothness("Smoothness", Range(0.0, 1.0)) = 0.0
		_TranslucencyDirect("Translucency (Direct)", Range(0.0, 1.0)) = 1
		_TranslucencyIndirect("Translucency (Indirect)", Range(0.0, 1.0)) = 0.0
		_TranslucencyFalloff("Translucency Falloff", Range(1.0, 8.0)) = 4.0
		_TranslucencyOffset("Translucency Offset", Range(0.0, 1.0)) = 0.0
		[HDR] _EmissionColor("Emission Color", Color) = (0,0,0)
		
		_NormalFlattening("Normal Flattening",Range(0.0, 1.0)) = 1.0
		_NormalSpherify("Normal Spherifying",Range(0.0, 1.0)) = 0.0
		_NormalSpherifyMask("Normal Spherifying (tip mask)",Range(0.0, 1.0)) = 0.0
		_NormalFlattenDepthNormals("Normal Spherifying (DepthNormals pass)",Range(0.0, 1.0)) = 1.0

		_BumpScale("Normal Map Strength",Range(0.0, 1.0)) = 1.0
		_BumpMap("Normal Map", 2D) = "bump" {}
		_BendPushStrength("Push Strength (XZ)", Range(0.0, 1.0)) = 1.0
		[MaterialEnum(Per Vertex,0,Uniform,1)]_BendMode("Bend Mode", Float) = 0.0
		_BendFlattenStrength("Flatten Strength (Y)", Range(0.0, 1.0)) = 1.0
		_BendTint("Bending tint", Color) = (1, 1, 1, 1.0)
		_PerspectiveCorrection("Perspective Correction", Range(0.0, 1.0)) = 1.0
		_BillboardingVerticalRotation("Billboarding, vertical rotation", Range(0.0, 1.0)) = 0.0

		//[Header(Wind)]
		_WindAmbientStrength("Ambient Strength", Range(0.0, 1.0)) = 0.2
		_WindSpeed("Ambient Speed", Float) = 3.0
		_WindDirection("Direction", vector) = (1,0,0,0)
		_WindVertexRand("Vertex randomization", Range(0.0, 1.0)) = 0.6
		_WindObjectRand("Object randomization", Range(0.0, 1.0)) = 0.5
		_WindRandStrength("Random per-object strength", Range(0.0, 1.0)) = 0.5
		_WindSwinging("Swinging", Range(0.0, 1.0)) = 0.15
		_WindGustStrength("Gusting strength", Range(0.0, 1.0)) = 0.2
		_WindGustFreq("Gusting frequency", Range(0.0, 10.0)) = 4
		_WindGustSpeed("Gusting Speed", Float) = 4
		[NoScaleOffset] _WindMap("Wind map", 2D) = "black" {}
		_WindGustTint("Max Gusting tint", Range(0.0, 3.0)) = 0.1

		//[Header(Rendering)]
		[MinMaxSlider(0, 25)] _FadeNear("Near", vector) = (0.25, 0.5, 0, 0)
		[MinMaxSlider(0, 500)] _FadeFar("Far", vector) = (450, 500, 1, 0)
		_FadeAngleThreshold("Angle fading threshold", Range(0.0, 90.0)) = 15
		
		//Keyword states
		[MaterialEnum(Unlit,0,Simple,1,Advanced,2)]_LightingMode("Lighting Mode", Float) = 2.0
		[Toggle] _Scalemap("Scale grass by scalemap", Float) = 0.0
		[Toggle] _Billboard("Billboard", Float) = 0.0
		[ToggleOff] _ReceiveShadows("Receive Shadows", Float) = 1.0
		[ToggleOff] _SpecularHighlights("Specular Highlights", Float) = 1.0
		[Toggle] _EnvironmentReflections("Environment Reflections", Float) = 1.0
		[Toggle(_FADING)] _FadingOn("Distance/Angle Fading", Float) = 0.0
		
		// Editmode props
		[HideInInspector] _QueueOffset("Queue offset", Float) = 0.0

		/* start CurvedWorld */
		//[CurvedWorldBendSettings] _CurvedWorldBendSettings("0|1|1", Vector) = (0, 0, 0, 0)
		/* end CurvedWorld */
		
		//Vegetation Studio Pro v1.4.0+
		_LODDebugColor ("LOD Debug color", Color) = (1,1,1,1)
		
		[HideInInspector][NoScaleOffset]unity_Lightmaps("unity_Lightmaps", 2DArray) = "" {}
		[HideInInspector][NoScaleOffset]unity_LightmapsInd("unity_LightmapsInd", 2DArray) = "" {}
		[HideInInspector][NoScaleOffset]unity_ShadowMasks("unity_ShadowMasks", 2DArray) = "" {}
		
		/* start FoliageRenderer */
//		[HideInInspector] _TerrainAlbedoProvided("Blend with Albedo Shader", Float) = 0
//		
//		[HideInInspector]_TerrainSize("Terrain Size", Vector) = (0,0,0,0)
//		[HideInInspector]_TerrainPosition("Terrain Position", Vector) = (0,0,0,0)
//		_TerrainYOffset("Y Offset", Float) = 0
//		
//		[HideInInspector]_TerrainAlbedoC("Terrain", 2D) = "black" {}
//	    [HideInInspector]_TerrainAlbedoL("TerrainL", 2D) = "black" {}
//	    [HideInInspector]_TerrainAlbedoR("TerrainR", 2D) = "black" {}
//	    [HideInInspector]_TerrainAlbedoU("TerrainU", 2D) = "black" {}
//	    [HideInInspector]_TerrainAlbedoUL("TerrainUL", 2D) = "black" {}
//	    [HideInInspector]_TerrainAlbedoUR("TerrainUR", 2D) = "black" {}
//	    [HideInInspector]_TerrainAlbedoB("TerrainB", 2D) = "black" {}
//	    [HideInInspector]_TerrainAlbedoBL("TerrainBL", 2D) = "black" {}
//	    [HideInInspector]_TerrainAlbedoBR("TerrainBR", 2D) = "black" {}
		/* end FoliageRenderer */
	}

	SubShader
	{
		Tags{
			"RenderType" = "Opaque"
			"Queue" = "AlphaTest"
			"RenderPipeline" = "UniversalPipeline"
			"IgnoreProjector" = "True"
			"UniversalMaterialType" = "Lit"
			%tags%
		}
		
		HLSLINCLUDE
        //Custom directives:
        %custom_directives%
        //%global_defines%
		#define REQUIRES_WORLD_SPACE_POS_INTERPOLATOR

		//Hard coded features
		#define _ALPHATEST_ON 1

		//Uncomment to compile out these calculations
		//#define DISABLE_WIND
		//#define DISABLE_BENDING

		//Uncomment to enable
		//#define MASKING_SPHERE_DISPLACEMENT
		
        %pragma_target%

		/* start CurvedWorld */
		//#define CURVEDWORLD_BEND_TYPE_CLASSICRUNNER_X_POSITIVE
		//#define CURVEDWORLD_BEND_ID_1
		//#pragma shader_feature_local CURVEDWORLD_DISABLED_ON
		//#pragma shader_feature_local CURVEDWORLD_NORMAL_TRANSFORMATION_ON
		//#include "Assets/Amazing Assets/Curved World/Shaders/Core/CurvedWorldTransform.cginc"
		/* end CurvedWorld */
		
        //%defines%
        
        //Rendering (integration)
        %define_renderer_integration%
        
        //Need to trick GPU Instancer into believing this shader is supported. Whilst only the shader compiled from this template is actually supported
        /* GPUInstancerSetup.hlsl */ //GPUShaderUtility.IsShaderInstanced() will check for this!
		
		ENDHLSL

		// ------------------------------------------------------------------
		//  Forward pass. Shades all light in a single pass. GI + emission + Fog
		Pass
		{
			Name "ForwardLit"
			Tags{ "LightMode" = "UniversalForward" }

			AlphaToMask [_AlphaToCoverage]
			Blend One Zero, One Zero
			Cull [_Cull]
			ZWrite On

			HLSLPROGRAM
			
			#define VEGETATION_SHADER //In place for projects that use a custom RP or modified URP and require specific behaviour for vegetation
			#define SHADERPASS_FORWARD

            // -------------------------------------
            // Shader Stages
            #pragma vertex LitPassVertex
            #pragma fragment LightingPassFragment
            
			// -------------------------------------
			// Material Keywords
			#pragma shader_feature_local _NORMALMAP
			#pragma shader_feature_local_vertex _SCALEMAP
			#pragma shader_feature_local_vertex _BILLBOARD
			#pragma shader_feature_local_fragment _FADING
			#pragma shader_feature_local _ _SIMPLE_LIGHTING _ADVANCED_LIGHTING
			#pragma shader_feature_local_fragment _SPECULARHIGHLIGHTS_OFF
			#pragma shader_feature_local_fragment _ENVIRONMENTREFLECTIONS_OFF
			#pragma shader_feature_local _RECEIVE_SHADOWS_OFF
			
            // -------------------------------------
            // Universal Pipeline keywords
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            // -------------------------------------
            // Unity defined keywords
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile _ USE_LEGACY_LIGHTMAPS
            #pragma multi_compile_fog
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            		
            // GPU Instancing
            #pragma multi_compile_instancing
            %instancing_options%
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            
            //--------------------------------------
            // Defines
			#if !_SIMPLE_LIGHTING && !_ADVANCED_LIGHTING
			#define _UNLIT
			#undef _NORMALMAP
			#endif
				
            // -------------------------------------
            // Includes
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"

			#include_library "Libraries/Input.hlsl"
			#include_library "Libraries/Common.hlsl"
			#include_library "Libraries/Color.hlsl"
			#include_library "Libraries/Lighting.hlsl"

			#include_library "Passes/LightingPass.hlsl"
			
            %include_renderer_integration_library%
			
            //#include "UnityCG.cginc" //Test
            #if defined(UNITY_SHADER_VARIABLES_INCLUDED) || defined(UNITY_CG_INCLUDED)
            #error "Fatal error: a shader library from the Built-in Render Pipeline was compiled into the shader. This is most likely caused by the rendering integration, make absolutely sure it is URP-compatible!"
            #endif
			ENDHLSL
		}

		Pass
		{
			Name "ShadowCaster"
			Tags{ "LightMode" = "ShadowCaster" }

			ZWrite On
			ZTest LEqual
			Cull[_Cull]

			HLSLPROGRAM
			
			#define SHADERPASS_SHADOWCASTER
			
			// -------------------------------------
            // Shader Stages
			#pragma vertex ShadowPassVertex
			#pragma fragment ShadowPassFragment

            // -------------------------------------
            // Material Keywords
			#pragma shader_feature_local_vertex _SCALEMAP
			#pragma shader_feature_local_vertex _BILLBOARD
			#pragma shader_feature_local_fragment _FADING
			
            // -------------------------------------
            // Unity defined keywords
            #pragma multi_compile _ LOD_FADE_CROSSFADE

			// GPU Instancing
            #pragma multi_compile_instancing
            %instancing_options%
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            
            // -------------------------------------
            // Includes
			#include_library "Libraries/Input.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
			#include_library_with_pragmas "Libraries/Common.hlsl"

			#include_library "Passes/ShadowPass.hlsl"
            %include_renderer_integration_library%
			ENDHLSL
		}
		
		//Deferred rendering
		// SkyPrison change:
		// Disabled UniversalGBuffer pass so this grass shader is forced onto ForwardLit.
		// ForwardLit already computes fogFactor and applies MixFog in LightingPass.hlsl,
		// which prevents the grass from visually ignoring the map environment fog.


		Pass
		{
			Name "DepthOnly"
			Tags{ "LightMode" = "DepthOnly" }

			ZWrite On
			ColorMask 0
			Cull[_Cull]

			HLSLPROGRAM

			#define SHADERPASS_DEPTHONLY

			// -------------------------------------
			// Material Keywords
			#pragma multi_compile _ LOD_FADE_CROSSFADE
			#pragma shader_feature_local_vertex _SCALEMAP
			#pragma shader_feature_local_vertex _BILLBOARD
			#pragma shader_feature_local_fragment _FADING

			// GPU Instancing
            #pragma multi_compile_instancing
            %instancing_options%
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            
			#include_library "Libraries/Input.hlsl"
			#include_library_with_pragmas "Libraries/Common.hlsl"

			#pragma vertex DepthOnlyVertex
			#pragma fragment DepthOnlyFragment
			
			#include_library "Passes/DepthPass.hlsl"
            %include_renderer_integration_library%
			
			ENDHLSL
		}

		// This pass is used when drawing to a _CameraNormalsTexture texture
		Pass
		{
			Name "DepthNormals"
			Tags{ "LightMode" = "DepthNormals" }
            
			ZWrite On
			Cull[_Cull]

			HLSLPROGRAM

			#define SHADERPASS_DEPTHNORMALS
			
			#pragma vertex DepthOnlyVertex
			#pragma fragment DepthNormalsFragment

			// -------------------------------------
			// Material Keywords
			#pragma multi_compile _ LOD_FADE_CROSSFADE
			#pragma shader_feature_local_vertex _SCALEMAP
			#pragma shader_feature_local_vertex _BILLBOARD
			#pragma shader_feature_local_fragment _FADING
			
			// Universal Pipeline keywords
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
			
            // GPU Instancing
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            %instancing_options%
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            
			#include_library "Libraries/Input.hlsl"
			#include_library_with_pragmas "Libraries/Common.hlsl"

			#include_library "Passes/DepthPass.hlsl"
            %include_renderer_integration_library%
			
			ENDHLSL
		}

        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }
            
            ColorMask RG

            HLSLPROGRAM
            
            #define SHADERPASS_MOTION_VECTORS
            
            #pragma vertex MotionVertex	
			#pragma fragment MotionFragment
            
            //#pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma shader_feature_local_vertex _ADD_PRECOMPUTED_VELOCITY

            //Universal Pipeline keywords
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            
            // GPU Instancing
            #pragma multi_compile_instancing
            %instancing_options%
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            
			#include_library "Libraries/Input.hlsl"
			#include_library_with_pragmas "Libraries/Common.hlsl"

			#include_library "Passes/MotionPass.hlsl"
            %include_renderer_integration_library%
			
            ENDHLSL
        }

		// Used for Baking GI. This pass is stripped from build.
        // This pass it not used during regular rendering, only for lightmap baking.
        Pass
        {
            Name "Meta"
            Tags { "LightMode" = "Meta" }
            
            Cull Off

            HLSLPROGRAM
            #pragma target 2.0
            
            #define SHADERPASS_META
            #pragma vertex VertexMeta
            #pragma fragment FragmentMeta
            
            //#pragma shader_feature_local_fragment _EMISSION
            //#pragma shader_feature_local_fragment _SPECGLOSSMAP
            #pragma shader_feature EDITOR_VISUALIZATION
            #define _EMISSION 1
            #define _ALPHATEST_ON 1

            #include_library "Libraries/Input.hlsl"
			#include_library_with_pragmas "Libraries/Common.hlsl"

			#include_library "Passes/MetaPass.hlsl"

            ENDHLSL
        }

	}//Subshader

	FallBack "Hidden/Universal Render Pipeline/FallbackError"
	CustomEditor "sc.stylizedgrass.editor.MaterialUI"

}//Shader
