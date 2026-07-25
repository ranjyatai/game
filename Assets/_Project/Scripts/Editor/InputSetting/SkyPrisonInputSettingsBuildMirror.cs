#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Keeps the runtime-readable Resources copy of SkyPrisonInputSettings in sync.
/// Source of truth stays at Assets/_Project/Data/Settings/SkyPrisonInputSettings.asset.
/// Build/runtime reads Resources.Load("SkyPrisonInputSettings") without requiring manual prefab dragging.
/// </summary>
public sealed class SkyPrisonInputSettingsBuildMirror : IPreprocessBuildWithReport
{
    private const string SourcePath = SkyPrisonInputSettings.DefaultAssetPath;
    private const string ResourceFolder = "Assets/_Project/Resources";
    private const string ResourceAssetPath = ResourceFolder + "/SkyPrisonInputSettings.asset";

    public int callbackOrder => -5000;

    public void OnPreprocessBuild(BuildReport report)
    {
        SyncInputSettingsToResources(log: true);
    }

    [MenuItem("Tools/Sky Prison/Input/同步输入设置到 Resources(Build)")]
    public static void SyncInputSettingsToResourcesMenu()
    {
        SyncInputSettingsToResources(log: true);
    }

    public static bool SyncInputSettingsToResources(bool log)
    {
        SkyPrisonInputSettings source = AssetDatabase.LoadAssetAtPath<SkyPrisonInputSettings>(SourcePath);
        if (source == null)
        {
            Debug.LogError("[SkyPrisonInputSettingsBuildMirror] Source input settings not found: " + SourcePath);
            return false;
        }

        source.EnsureDefaults();
        EditorUtility.SetDirty(source);

        if (!AssetDatabase.IsValidFolder(ResourceFolder))
        {
            EnsureFolder("Assets/_Project", "Resources");
        }

        // Replace the mirror every time so build never uses stale key bindings.
        if (AssetDatabase.LoadAssetAtPath<Object>(ResourceAssetPath) != null)
        {
            AssetDatabase.DeleteAsset(ResourceAssetPath);
        }

        bool copied = AssetDatabase.CopyAsset(SourcePath, ResourceAssetPath);
        if (!copied)
        {
            Debug.LogError("[SkyPrisonInputSettingsBuildMirror] Failed to copy input settings to: " + ResourceAssetPath);
            return false;
        }

        SkyPrisonInputSettings mirror = AssetDatabase.LoadAssetAtPath<SkyPrisonInputSettings>(ResourceAssetPath);
        if (mirror != null)
        {
            mirror.EnsureDefaults();
            EditorUtility.SetDirty(mirror);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (log)
            Debug.Log("[SkyPrisonInputSettingsBuildMirror] Synced input settings mirror: " + SourcePath + " -> " + ResourceAssetPath);

        return true;
    }

    private static void EnsureFolder(string parent, string child)
    {
        string full = parent + "/" + child;
        if (AssetDatabase.IsValidFolder(full))
            return;

        if (!AssetDatabase.IsValidFolder(parent))
        {
            Directory.CreateDirectory(parent);
            AssetDatabase.Refresh();
        }

        AssetDatabase.CreateFolder(parent, child);
    }
}
#endif
