using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 把场景/预制体里已经序列化的 UnitOverheadQuestMarker 尺寸推到当前标准值。
///
/// 为什么需要：这个组件是 UnitOverheadUIView.EnsureStructure() 在运行时自动挂的，
/// 挂上之后会随场景一起保存。之后再改代码里的 [SerializeField] 默认值，
/// 对已经存在的实例完全无效——它们带着旧值，表现是「改了代码画面没变化」。
///
/// 这是「运行时自动挂载 + 序列化字段」这个组合的固有问题。要么别序列化，
/// 要么就得有这么一个校正器。选后者是因为尺寸确实需要能在 Inspector 里试。
///
/// 用版本号守住，改了标准值就把版本号 +1，下次重载自动推平。
/// </summary>
[InitializeOnLoad]
public static class SkyPrisonQuestMarkerSizeSync
{
    private const string LogPrefix = "[SkyPrison QuestMarkerSize]";
    private const string VersionKey = "SkyPrison.QuestMarkerSizeSync.Version";

    /// <summary>改标准值时，这个版本号要一起 +1，否则不会重跑。</summary>
    private const int Version = 2;

    /// <summary>和 UnitOverheadQuestMarker.iconSize 的默认值保持一致。</summary>
    private const float StandardIconSize = 28.1f; // 2026-08-14：再放大1.2倍

    static SkyPrisonQuestMarkerSizeSync()
    {
        EditorApplication.delayCall += RunOnce;
    }

    private static void RunOnce()
    {
        if (EditorPrefs.GetInt(VersionKey, 0) >= Version)
            return;

        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += RunOnce;
            return;
        }

        EditorPrefs.SetInt(VersionKey, Version);
        Run();
    }

    [MenuItem("天空囚笼/地图/把头顶任务图标尺寸推到标准值", false, 121)]
    public static void Run()
    {
        int changed = 0;

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded)
                continue;

            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (var marker in root.GetComponentsInChildren<UnitOverheadQuestMarker>(true))
                    changed += Push(marker, markScene: true);
        }

        foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                AssetDatabase.GUIDToAssetPath(guid));
            if (prefab == null)
                continue;

            foreach (var marker in prefab.GetComponentsInChildren<UnitOverheadQuestMarker>(true))
                changed += Push(marker, markScene: false);
        }

        AssetDatabase.SaveAssets();

        if (changed == 0)
        {
            Debug.Log($"{LogPrefix} 已经是标准值 {StandardIconSize}，无需改动。");
            return;
        }

        for (int i = 0; i < SceneManager.sceneCount; i++)
            EditorSceneManager.MarkSceneDirty(SceneManager.GetSceneAt(i));
        bool saved = EditorSceneManager.SaveOpenScenes();

        Debug.Log(
            $"{LogPrefix} 把 {changed} 个头顶任务图标的尺寸推到 {StandardIconSize}。" +
            $"场景保存：{(saved ? "成功" : "失败或被取消")}。");
    }

    private static int Push(UnitOverheadQuestMarker marker, bool markScene)
    {
        if (marker == null)
            return 0;

        var so = new SerializedObject(marker);
        SerializedProperty p = so.FindProperty("iconSize");
        if (p == null || Mathf.Approximately(p.floatValue, StandardIconSize))
            return 0;

        p.floatValue = StandardIconSize;
        so.ApplyModifiedPropertiesWithoutUndo();

        if (markScene)
            EditorSceneManager.MarkSceneDirty(marker.gameObject.scene);
        else
            EditorUtility.SetDirty(marker);

        return 1;
    }
}
