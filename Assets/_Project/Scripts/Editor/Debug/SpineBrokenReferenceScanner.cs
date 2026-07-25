#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SpineBrokenReferenceScanner_ReflectionV3
{
    private const string MenuRoot = "Tools/Sky Prison/Debug/Spine空引用扫描 V3/";

    [MenuItem(MenuRoot + "扫描Prefab资源")]
    public static void ScanProjectPrefabs()
    {
        int checkedObjects = 0;
        int checkedComponents = 0;
        int brokenCount = 0;

        string[] guids = AssetDatabase.FindAssets("t:Prefab");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                continue;

            ScanRoot(prefab, path, ref checkedObjects, ref checkedComponents, ref brokenCount);
        }

        Debug.Log($"[Spine扫描V3完成] Prefab数量: {guids.Length}, 检查对象: {checkedObjects}, 检查Spine组件: {checkedComponents}, 空引用数量: {brokenCount}");
    }

    [MenuItem(MenuRoot + "扫描当前打开场景")]
    public static void ScanCurrentOpenScenes()
    {
        int checkedObjects = 0;
        int checkedComponents = 0;
        int brokenCount = 0;

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.IsValid() || !scene.isLoaded)
                continue;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                string source = string.IsNullOrEmpty(scene.path) ? scene.name : scene.path;
                ScanRoot(root, source, ref checkedObjects, ref checkedComponents, ref brokenCount);
            }
        }

        Debug.Log($"[Spine扫描V3完成] 当前场景检查对象: {checkedObjects}, 检查Spine组件: {checkedComponents}, 空引用数量: {brokenCount}");
    }

    [MenuItem(MenuRoot + "扫描Build Settings启用场景")]
    public static void ScanEnabledBuildScenes()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Debug.LogWarning("[Spine扫描V3取消] 当前场景未保存，已取消扫描Build场景。");
            return;
        }

        string activeScenePath = SceneManager.GetActiveScene().path;

        int checkedSceneCount = 0;
        int checkedObjects = 0;
        int checkedComponents = 0;
        int brokenCount = 0;

        foreach (EditorBuildSettingsScene buildScene in EditorBuildSettings.scenes)
        {
            if (buildScene == null || !buildScene.enabled || string.IsNullOrEmpty(buildScene.path))
                continue;

            Scene scene = EditorSceneManager.OpenScene(buildScene.path, OpenSceneMode.Single);
            checkedSceneCount++;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                ScanRoot(root, buildScene.path, ref checkedObjects, ref checkedComponents, ref brokenCount);
            }
        }

        if (!string.IsNullOrEmpty(activeScenePath))
            EditorSceneManager.OpenScene(activeScenePath, OpenSceneMode.Single);

        Debug.Log($"[Spine扫描V3完成] Build场景数量: {checkedSceneCount}, 检查对象: {checkedObjects}, 检查Spine组件: {checkedComponents}, 空引用数量: {brokenCount}");
    }

    private static void ScanRoot(GameObject root, string sourcePath, ref int checkedObjects, ref int checkedComponents, ref int brokenCount)
    {
        if (root == null)
            return;

        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform transform in transforms)
        {
            if (transform == null)
                continue;

            checkedObjects++;
            Component[] components = transform.GetComponents<Component>();

            foreach (Component component in components)
            {
                if (component == null)
                {
                    Debug.LogError($"[Missing Script] {sourcePath} / {GetHierarchyPath(transform)}", root);
                    continue;
                }

                Type type = component.GetType();
                if (!IsLikelySpineComponent(type))
                    continue;

                FieldInfo field = FindFieldInTypeHierarchy(type, "skeletonDataAsset");
                if (field == null)
                {
                    // 很多 Spine 辅助组件、代理组件、材质组件本来就没有 skeletonDataAsset。
                    // 这里不报错，避免把正常组件误判成 Build 阻断问题。
                    continue;
                }

                checkedComponents++;

                UnityEngine.Object value = null;
                try
                {
                    value = field.GetValue(component) as UnityEngine.Object;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[Spine扫描V3读取失败] {type.FullName}: {sourcePath} / {GetHierarchyPath(transform)}\n{ex.GetType().Name}: {ex.Message}", root);
                    brokenCount++;
                    continue;
                }

                if (value == null)
                {
                    Debug.LogError($"[Spine空引用] {type.FullName} 缺少 skeletonDataAsset: {sourcePath} / {GetHierarchyPath(transform)}", root);
                    brokenCount++;
                }
            }
        }
    }

    private static bool IsLikelySpineComponent(Type type)
    {
        if (type == null)
            return false;

        string fullName = type.FullName ?? string.Empty;
        if (!fullName.StartsWith("Spine.Unity", StringComparison.Ordinal))
            return false;

        // 只筛和骨架实例直接相关的组件，避开辅助材质/代理/特效组件。
        while (type != null)
        {
            string name = type.Name;
            if (name == "SkeletonAnimation" ||
                name == "SkeletonMecanim" ||
                name == "SkeletonGraphic" ||
                name == "SkeletonAnimationBase" ||
                name == "SkeletonRenderer" ||
                name == "SkeletonRendererCustomMaterials" ||
                name == "SkeletonRendererCustomMaterialOverride")
            {
                return true;
            }

            type = type.BaseType;
        }

        return false;
    }

    private static FieldInfo FindFieldInTypeHierarchy(Type type, string fieldName)
    {
        while (type != null)
        {
            FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null)
                return field;

            type = type.BaseType;
        }

        return null;
    }

    private static string GetHierarchyPath(Transform transform)
    {
        if (transform == null)
            return string.Empty;

        string path = transform.name;
        Transform current = transform.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }
}
#endif
