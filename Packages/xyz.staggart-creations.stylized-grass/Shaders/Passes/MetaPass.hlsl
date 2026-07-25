
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MetaInput.hlsl"
//#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UniversalMetaPass.hlsl"

#include "../Libraries/Color.hlsl"
#include "../Libraries/Lighting.hlsl"
#include "LightingPass.hlsl"

struct MetaAttributes
{
    float4 positionOS   : POSITION;
    float4 color        : COLOR;
    float3 normalOS     : NORMAL;
    float2 uv0          : TEXCOORD0;
    float2 uv1          : TEXCOORD1;
    float2 uv2          : TEXCOORD2;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

Varyings VertexMeta(MetaAttributes input)
{
    Varyings output = (Varyings)0;
    output.positionCS = UnityMetaVertexPosition(input.positionOS.xyz, input.uv1, input.uv2);
    //output.uv = TRANSFORM_TEX(input.uv0, _BaseMap);

    input.normalOS = lerp(input.normalOS, float3(0,1,0), _NormalFlattening);
    input.normalOS = lerp(input.normalOS, normalize(input.positionOS.xyz), _NormalSpherify * lerp(1, input.color[_VertexColorShadingChannel], _NormalSpherifyMask));
    
    float posOffset = ObjectPosRand01();
    VertexOutput vertexData = GetVertexOutput(input.positionOS, input.normalOS, 0, posOffset, (WindSettings)0, (BendSettings)0);
    
    float3 positionWS = vertexData.positionWS;
    
    //Vertex color
    output.color.rgb = ApplyVertexColor(input.positionOS.xyz, positionWS.xyz, _BaseColor.rgb, input.color[_VertexColorShadingChannel], _OcclusionStrength, _VertexDarkening, posOffset);
    //Pass vertex color alpha-channel to fragment stage. Used in some shading functions such as translucency
    output.color.a = input.color[_VertexColorShadingChannel];
    
    #ifdef EDITOR_VISUALIZATION
    UnityEditorVizData(input.positionOS.xyz, input.uv0, input.uv1, input.uv2, output.VizUV, output.LightCoord);
    #endif
    
    return output;
}

half4 FragmentMeta(Varyings input) : SV_Target
{
    SurfaceData surfaceData = (SurfaceData)0;
    ModifySurfaceData(input, surfaceData);
    
    InputData inputData;
    //Standard URP function barely changes, but adds things like clear coat and detail normals
    PopulateLightingInputData(input, surfaceData.normalTS, inputData);
	
    //Get main light first, need attenuation to mask wind gust
    Light mainLight = GetMainLight(inputData.shadowCoord);
    
    TranslucencyData tData = (TranslucencyData)0;
    tData.strengthDirect = _TranslucencyDirect;
    tData.strengthIndirect = _TranslucencyIndirect;
    tData.exponent = _TranslucencyFalloff;
    tData.thickness = saturate(input.color.a);
    tData.offset = _TranslucencyOffset;
    tData.light = mainLight;
    
    ApplyTranslucency(surfaceData, inputData, tData);
    
    MetaInput metaInput;
    metaInput.Albedo = surfaceData.albedo;
    //metaInput.Albedo = float3(1,0,0);
    metaInput.Emission = surfaceData.emission;
    
    return float4(metaInput.Albedo, surfaceData.alpha);
    #ifdef EDITOR_VISUALIZATION
    metaInput.VizUV = input.VizUV;
    metaInput.LightCoord = input.LightCoord;
    #endif
    
    AlphaClip(surfaceData.alpha, input.positionCS.xyz, input.positionWS.xyz);
    
    return UnityMetaFragment(metaInput);
}