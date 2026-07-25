using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Experimental.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DebugFlagBatchTools
{
    public static readonly string[] DefaultBoolFieldNames =
    {
        "debugLogs",
        "debugDraw",
        "debugMode",
        "showDebug",
        "enableDebug",
        "drawGizmos",
        "showGizmos"
    };

    public struct BatchOptions
    {
        public bool includeInactive;
        public string[] fieldNames;
    }

    public struct BatchResult
    {
        public int changedComponentCount;
        public int changedFieldCount;
        public int scannedComponentCount;
        public int matchedComponentCount;
        public int matchedFieldCount;
        public List<string> enabledLines;
    }

    // ========= 菜单 =========

    [MenuItem("Tools/Debug/打开 Debug 批处理工具")]
    public static void OpenWindow()
    {
        DebugFlagBatchWindow.OpenWindow();
    }

    [MenuItem("Tools/Debug/列出当前场景已开启 Debug")]
    public static void ListEnabledDebugFlagsInOpenScene()
    {
        BatchOptions options = CreateDefaultOptions();
        BatchResult result = CollectEnabledFlagsInOpenScene(options);

        if (result.enabledLines.Count == 0)
        {
            Debug.Log("[DebugFlagBatchTools] 当前场景没有已开启的 Debug 开关。");
            return;
        }

        Debug.Log("[DebugFlagBatchTools] 当前场景已开启 Debug 的组件：\n" + string.Join("\n", result.enabledLines));
    }

    [MenuItem("Tools/Debug/关闭当前场景全部 Debug")]
    public static void DisableAllDebugFlagsInOpenScene()
    {
        BatchOptions options = CreateDefaultOptions();
        BatchResult result = DisableFlagsInOpenScene(options);

        Debug.Log(
            $"[DebugFlagBatchTools] 当前场景 Debug 已关闭。扫描组件={result.scannedComponentCount}, " +
            $"命中组件={result.matchedComponentCount}, 修改组件={result.changedComponentCount}, 修改字段={result.changedFieldCount}"
        );
    }

    [MenuItem("Tools/Debug/关闭当前选中对象全部 Debug")]
    public static void DisableAllDebugFlagsInSelection()
    {
        BatchOptions options = CreateDefaultOptions();
        BatchResult result = DisableFlagsInSelection(options);

        Debug.Log(
            $"[DebugFlagBatchTools] 当前选中对象 Debug 已关闭。扫描组件={result.scannedComponentCount}, " +
            $"命中组件={result.matchedComponentCount}, 修改组件={result.changedComponentCount}, 修改字段={result.changedFieldCount}"
        );
    }

    [MenuItem("Tools/Debug/关闭当前场景 + 选中对象全部 Debug")]
    public static void DisableDebugFlagsInSceneAndSelection()
    {
        BatchOptions options = CreateDefaultOptions();

        BatchResult sceneResult = DisableFlagsInOpenScene(options);
        BatchResult selectionResult = DisableFlagsInSelection(options);

        Debug.Log(
            $"[DebugFlagBatchTools] 场景 + 选中对象 Debug 已关闭。总扫描组件={sceneResult.scannedComponentCount + selectionResult.scannedComponentCount}, " +
            $"总命中组件={sceneResult.matchedComponentCount + selectionResult.matchedComponentCount}, " +
            $"总修改组件={sceneResult.changedComponentCount + selectionResult.changedComponentCount}, " +
            $"总修改字段={sceneResult.changedFieldCount + selectionResult.changedFieldCount}"
        );
    }

    // ========= 对外 API =========

    public static BatchOptions CreateDefaultOptions()
    {
        return new BatchOptions
        {
            includeInactive = true,
            fieldNames = DefaultBoolFieldNames
        };
    }

    public static BatchResult CollectEnabledFlagsInOpenScene(BatchOptions options)
    {
        BatchResult result = CreateEmptyResult();

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
            return result;

        GameObject[] roots = scene.GetRootGameObjects();
        foreach (GameObject root in roots)
            CollectEnabledFlagsFromHierarchy(root, options, ref result);

        return result;
    }

    public static BatchResult CollectEnabledFlagsInSelection(BatchOptions options)
    {
        BatchResult result = CreateEmptyResult();

        GameObject[] targets = GetSelectedGameObjects();
        foreach (GameObject go in targets)
            CollectEnabledFlagsFromHierarchy(go, options, ref result);

        return result;
    }

    public static BatchResult CollectEnabledFlagsInCurrentPrefabStage(BatchOptions options)
    {
        BatchResult result = CreateEmptyResult();

        PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage == null || stage.prefabContentsRoot == null)
            return result;

        CollectEnabledFlagsFromHierarchy(stage.prefabContentsRoot, options, ref result);
        return result;
    }

    public static BatchResult DisableFlagsInOpenScene(BatchOptions options)
    {
        BatchResult result = CreateEmptyResult();

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
            return result;

        GameObject[] roots = scene.GetRootGameObjects();
        foreach (GameObject root in roots)
            DisableFlagsInHierarchy(root, options, ref result);

        if (result.changedComponentCount > 0)
            EditorSceneManager.MarkSceneDirty(scene);

        return result;
    }

    public static BatchResult DisableFlagsInSelection(BatchOptions options)
    {
        BatchResult result = CreateEmptyResult();

        GameObject[] targets = GetSelectedGameObjects();
        foreach (GameObject go in targets)
            DisableFlagsInHierarchy(go, options, ref result);

        return result;
    }

    public static BatchResult DisableFlagsInCurrentPrefabStage(BatchOptions options)
    {
        BatchResult result = CreateEmptyResult();

        PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage == null || stage.prefabContentsRoot == null)
            return result;

        DisableFlagsInHierarchy(stage.prefabContentsRoot, options, ref result);

        if (result.changedComponentCount > 0)
            EditorSceneManager.MarkSceneDirty(stage.scene);

        return result;
    }

    public static BatchResult DisableFlagsInSelectedPrefabAssets(BatchOptions options)
    {
        BatchResult result = CreateEmptyResult();

        Object[] selected = Selection.objects;
        foreach (Object obj in selected)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path))
                continue;

            GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefabRoot == null)
                continue;

            GameObject tempRoot = PrefabUtility.LoadPrefabContents(path);
            if (tempRoot == null)
                continue;

            try
            {
                DisableFlagsInHierarchy(tempRoot, options, ref result);

                if (result.changedComponentCount > 0)
                    PrefabUtility.SaveAsPrefabAsset(tempRoot, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(tempRoot);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        return result;
    }

    // ========= 核心实现 =========

    private static BatchResult CreateEmptyResult()
    {
        return new BatchResult
        {
            changedComponentCount = 0,
            changedFieldCount = 0,
            scannedComponentCount = 0,
            matchedComponentCount = 0,
            matchedFieldCount = 0,
            enabledLines = new List<string>()
        };
    }

    private static void CollectEnabledFlagsFromHierarchy(GameObject root, BatchOptions options, ref BatchResult result)
    {
        if (root == null)
            return;

        MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(options.includeInactive);
        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour == null)
                continue;

            result.scannedComponentCount++;

            SerializedObject so = new SerializedObject(behaviour);
            bool matchedThisComponent = false;

            for (int i = 0; i < options.fieldNames.Length; i++)
            {
                SerializedProperty prop = so.FindProperty(options.fieldNames[i]);
                if (prop == null)
                    continue;

                if (prop.propertyType != SerializedPropertyType.Boolean)
                    continue;

                result.matchedFieldCount++;
                matchedThisComponent = true;

                if (prop.boolValue)
                {
                    result.enabledLines.Add($"{GetHierarchyPath(behaviour.gameObject)} -> {behaviour.GetType().Name}.{prop.name} = true");
                }
            }

            if (matchedThisComponent)
                result.matchedComponentCount++;
        }
    }

    private static void DisableFlagsInHierarchy(GameObject root, BatchOptions options, ref BatchResult result)
    {
        if (root == null)
            return;

        MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(options.includeInactive);
        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour == null)
                continue;

            result.scannedComponentCount++;

            bool matchedThisComponent = false;
            bool changedThisComponent = false;
            SerializedObject so = new SerializedObject(behaviour);

            for (int i = 0; i < options.fieldNames.Length; i++)
            {
                SerializedProperty prop = so.FindProperty(options.fieldNames[i]);
                if (prop == null)
                    continue;

                if (prop.propertyType != SerializedPropertyType.Boolean)
                    continue;

                result.matchedFieldCount++;
                matchedThisComponent = true;

                if (prop.boolValue)
                {
                    prop.boolValue = false;
                    result.changedFieldCount++;
                    changedThisComponent = true;
                }
            }

            if (matchedThisComponent)
                result.matchedComponentCount++;

            if (changedThisComponent)
            {
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(behaviour);
                result.changedComponentCount++;
            }
        }
    }

    private static GameObject[] GetSelectedGameObjects()
    {
        List<GameObject> result = new List<GameObject>();

        Object[] selected = Selection.objects;
        foreach (Object obj in selected)
        {
            if (obj is GameObject go)
                result.Add(go);
        }

        return result.ToArray();
    }

    public static string GetHierarchyPath(GameObject go)
    {
        if (go == null)
            return "(null)";

        string path = go.name;
        Transform current = go.transform.parent;

        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }
}
