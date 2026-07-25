#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-only installer for the occlusion render resources asset.
///
/// It creates a serialized dependency chain instead of relying on Always Included Shaders:
/// ScreenSpaceOutlineSystem -> SkyPrisonOcclusionRenderResources.asset -> M_UnitHiddenMaskComposite.mat -> shader.
/// </summary>
public static class SkyPrisonOcclusionRenderResourcesInstaller
{
    private const string ResourceFolder = "Assets/_Project/Resources/Occlusion";
    private const string ResourceAssetPath = ResourceFolder + "/SkyPrisonOcclusionRenderResources.asset";
    private const string CompositeMaterialPath = ResourceFolder + "/M_UnitHiddenMaskComposite.mat";
    private const string CompositeShaderName = "Hidden/SkyPrison/UnitHiddenMaskComposite";

    [MenuItem("Tools/Sky Prison/Occlusion/Create Or Repair Render Resources")]
    public static void CreateOrRepair()
    {
        EnsureFolder(ResourceFolder);

        Shader compositeShader = FindCompositeShader();
        if (compositeShader == null)
        {
            Debug.LogError("[SkyPrisonOcclusionRenderResourcesInstaller] Could not find shader: " + CompositeShaderName +
                           "\nMake sure UnitHiddenMaskComposite.shader is imported and its Shader name is exactly this string.");
            return;
        }

        Material compositeMaterial = AssetDatabase.LoadAssetAtPath<Material>(CompositeMaterialPath);
        if (compositeMaterial == null)
        {
            compositeMaterial = new Material(compositeShader)
            {
                name = "M_UnitHiddenMaskComposite"
            };
            AssetDatabase.CreateAsset(compositeMaterial, CompositeMaterialPath);
        }
        else if (compositeMaterial.shader != compositeShader)
        {
            compositeMaterial.shader = compositeShader;
            EditorUtility.SetDirty(compositeMaterial);
        }

        SkyPrisonOcclusionRenderResources resources = AssetDatabase.LoadAssetAtPath<SkyPrisonOcclusionRenderResources>(ResourceAssetPath);
        if (resources == null)
        {
            resources = ScriptableObject.CreateInstance<SkyPrisonOcclusionRenderResources>();
            AssetDatabase.CreateAsset(resources, ResourceAssetPath);
        }

        SerializedObject so = new SerializedObject(resources);
        so.FindProperty("unitHiddenMaskCompositeMaterial").objectReferenceValue = compositeMaterial;
        so.FindProperty("unitHiddenMaskCompositeShader").objectReferenceValue = compositeShader;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(resources);

        int repairedManagers = AssignResourcesToSceneManagers(resources, compositeMaterial, compositeShader);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[SkyPrisonOcclusionRenderResourcesInstaller] Render resources repaired. " +
                  "resources=" + ResourceAssetPath + ", material=" + CompositeMaterialPath +
                  ", managers=" + repairedManagers);
    }

    private static int AssignResourcesToSceneManagers(SkyPrisonOcclusionRenderResources resources, Material material, Shader shader)
    {
        int count = 0;
        ScreenSpaceOutlineRTManager[] managers = Object.FindObjectsByType<ScreenSpaceOutlineRTManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < managers.Length; i++)
        {
            ScreenSpaceOutlineRTManager manager = managers[i];
            if (manager == null)
                continue;

            SerializedObject so = new SerializedObject(manager);
            SerializedProperty renderResourcesProp = so.FindProperty("renderResources");
            SerializedProperty materialProp = so.FindProperty("unitHiddenMaskCompositeMaterial");
            SerializedProperty shaderProp = so.FindProperty("unitHiddenMaskCompositeShader");

            if (renderResourcesProp != null)
                renderResourcesProp.objectReferenceValue = resources;
            if (materialProp != null)
                materialProp.objectReferenceValue = material;
            if (shaderProp != null)
                shaderProp.objectReferenceValue = shader;

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(manager);
            count++;
        }

        return count;
    }

    private static Shader FindCompositeShader()
    {
        Shader shader = Shader.Find(CompositeShaderName);
        if (shader != null)
            return shader;

        string[] guids = AssetDatabase.FindAssets("UnitHiddenMaskComposite t:Shader");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            Shader candidate = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (candidate != null && candidate.name == CompositeShaderName)
                return candidate;
        }

        return null;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
            return;

        string[] parts = folder.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
#endif
