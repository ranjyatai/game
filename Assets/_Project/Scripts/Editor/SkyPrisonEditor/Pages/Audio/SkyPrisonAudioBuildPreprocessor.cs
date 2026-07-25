#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// V3 - 2026-05-29 - fixed canonical settings source + build value diagnostics
/// Automatically builds a runtime audio resource catalog before Unity Build.
///
/// This closes the Build reference chain:
/// Resources/SkyPrisonRuntimeAudioCatalog
///   -> SkyPrisonAudioGlobalSettings
///   -> SkyPrisonAudioPackage assets
///   -> GroundSurfaceMaterialDefinition assets
///   -> AudioClip assets
///
/// Runtime code must not depend on AssetDatabase. This preprocessor is Editor-only.
/// </summary>
public sealed class SkyPrisonAudioBuildPreprocessor : IPreprocessBuildWithReport
{
    private const string CatalogFolder = "Assets/_Project/Data/Runtime/Resources";
    private const string CatalogPath = CatalogFolder + "/SkyPrisonRuntimeAudioCatalog.asset";

    private static readonly string[] AudioPackageSearchFolders =
    {
        "Assets/_Project/Audio",
        "Assets/_Project/Data"
    };

    private static readonly string[] AudioClipSearchFolders =
    {
        "Assets/_Project/Audio"
    };

    private static readonly string[] GroundSurfaceSearchFolders =
    {
        "Assets/_Project/Data"
    };

    public int callbackOrder => -5000;

    public void OnPreprocessBuild(BuildReport report)
    {
        CollectAndSaveCatalog(log: true);
    }

    // V2: Manual Tools menu entry removed.
    // Keep this editor-only build preprocessor alive so runtime audio resources
    // are still collected automatically before Build.

    public static SkyPrisonRuntimeAudioCatalog CollectAndSaveCatalog(bool log)
    {
        EnsureFolder(CatalogFolder);

        SkyPrisonRuntimeAudioCatalog catalog = AssetDatabase.LoadAssetAtPath<SkyPrisonRuntimeAudioCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<SkyPrisonRuntimeAudioCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }

        SkyPrisonAudioGlobalSettings globalSettings = SkyPrisonAudioGlobalSettings.FindOrCreateEditorAsset();
        if (globalSettings != null)
            EditorUtility.SetDirty(globalSettings);
        AssetDatabase.SaveAssets();

        List<SkyPrisonAudioPackage> audioPackages = FindAssetsOfType<SkyPrisonAudioPackage>(AudioPackageSearchFolders);
        List<GroundSurfaceMaterialDefinition> groundSurfaces = FindAssetsOfType<GroundSurfaceMaterialDefinition>(GroundSurfaceSearchFolders);
        List<AudioClip> audioClips = FindAssetsOfType<AudioClip>(AudioClipSearchFolders);

        // Also keep audio packages referenced directly by ground surface materials.
        for (int i = 0; i < groundSurfaces.Count; i++)
        {
            GroundSurfaceMaterialDefinition surface = groundSurfaces[i];
            if (surface == null || surface.surfaceAudioPackage == null)
                continue;

            if (!audioPackages.Contains(surface.surfaceAudioPackage))
                audioPackages.Add(surface.surfaceAudioPackage);
        }

        catalog.globalSettings = globalSettings;
        catalog.audioPackages = audioPackages
            .Where(x => x != null)
            .Distinct()
            .OrderBy(x => AssetDatabase.GetAssetPath(x), StringComparer.OrdinalIgnoreCase)
            .ToList();

        catalog.groundSurfaceMaterials = groundSurfaces
            .Where(x => x != null)
            .Distinct()
            .OrderBy(x => AssetDatabase.GetAssetPath(x), StringComparer.OrdinalIgnoreCase)
            .ToList();

        catalog.forceIncludedAudioClips = audioClips
            .Where(x => x != null)
            .Distinct()
            .OrderBy(x => AssetDatabase.GetAssetPath(x), StringComparer.OrdinalIgnoreCase)
            .ToList();

        catalog.SetGeneratedInfo(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), "SkyPrisonAudioBuildPreprocessor V3");

        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (log)
        {
            Debug.Log(
                "[SkyPrisonAudioBuildPreprocessor] Runtime audio catalog updated: " + CatalogPath +
                "\nGlobalSettings=" + (globalSettings != null ? AssetDatabase.GetAssetPath(globalSettings) : "null") +
                "\nGlobalSettingsValues=" + (globalSettings != null ? globalSettings.BuildDebugSummary() : "null") +
                "\nAudioPackages=" + catalog.audioPackages.Count +
                "\nGroundSurfaceMaterials=" + catalog.groundSurfaceMaterials.Count +
                "\nForceIncludedAudioClips=" + catalog.forceIncludedAudioClips.Count);
        }

        return catalog;
    }

    private static List<T> FindAssetsOfType<T>(string[] preferredFolders) where T : UnityEngine.Object
    {
        List<T> result = new List<T>();
        HashSet<string> seen = new HashSet<string>();

        string filter = "t:" + typeof(T).Name;
        string[] folders = preferredFolders != null && preferredFolders.Length > 0
            ? preferredFolders.Where(AssetDatabase.IsValidFolder).ToArray()
            : null;

        string[] guids = folders != null && folders.Length > 0
            ? AssetDatabase.FindAssets(filter, folders)
            : AssetDatabase.FindAssets(filter);

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (string.IsNullOrWhiteSpace(path) || !seen.Add(path))
                continue;

            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
                result.Add(asset);
        }

        return result;
    }

    private static void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
            return;

        string[] parts = folderPath.Split('/');
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
