#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Synchronizes the corrected Scene instance back into the PF_TD_* runtime template prefab.
/// Visual PF is never modified here. Only the runtime container prefab is written.
/// </summary>
public static class SkyPrisonTerrainDecorationTemplateSyncUtility
{
    private const string DefaultTemplateFolder = "Assets/_Project/Prefabs/TerrainDecorations/Custom";

    private static readonly string[] GeneratedRootNames =
    {
        "RuleRoot",
        "ManualProxies",
        "ShadowCasterRoot",
        "StencilWriterRoot",
        "MossRoot"
    };

    [MenuItem("Tools/Sky Prison/Map/地形装饰物/模板/把当前矫正实例写回源头模板")]
    public static void SyncSelectedInstanceToRuntimeTemplateMenu()
    {
        GameObject selected = Selection.activeGameObject;
        GameObject sourceRoot = ResolveTerrainDecorationRuntimeRoot(selected);
        if (sourceRoot == null)
        {
            EditorUtility.DisplayDialog("写回模板失败", "请先在 Hierarchy 中选中一个已摆放地形装饰物实例，或它的任意子节点。", "知道了");
            return;
        }

        TerrainDecorationRuntimeBinder binder = sourceRoot.GetComponent<TerrainDecorationRuntimeBinder>();
        if (binder == null || binder.definition == null)
        {
            EditorUtility.DisplayDialog("写回模板失败", "当前实例没有 TerrainDecorationRuntimeBinder，或 Binder.definition 为空。", "知道了");
            return;
        }

        bool ok = SyncInstanceToRuntimeTemplate(sourceRoot, binder.definition, true);
        if (ok)
            EditorUtility.DisplayDialog("写回模板完成", "已把当前实例的 RuleRoot / CollisionRoot / FrontOccluder / BackTrigger 等矫正结果写回 PF_TD_* 源头模板。\n之后新放出来的同类物体会继承这套结果。", "知道了");
    }

    public static bool SyncBestSceneInstanceToRuntimeTemplate(TerrainDecorationDefinition definition, bool log)
    {
        if (definition == null)
            return false;

        TerrainDecorationRuntimeBinder[] binders = Object.FindObjectsOfType<TerrainDecorationRuntimeBinder>(true);
        for (int i = 0; i < binders.Length; i++)
        {
            TerrainDecorationRuntimeBinder binder = binders[i];
            if (binder == null || !IsSameDefinition(binder.definition, definition))
                continue;

            GameObject root = ResolveTerrainDecorationRuntimeRoot(binder.gameObject);
            if (root == null)
                continue;

            return SyncInstanceToRuntimeTemplate(root, definition, log);
        }

        return false;
    }

    public static bool SyncInstanceToRuntimeTemplate(GameObject sourceRoot, TerrainDecorationDefinition definition, bool log)
    {
        if (sourceRoot == null || definition == null)
            return false;

        GameObject runtimeRoot = ResolveTerrainDecorationRuntimeRoot(sourceRoot);
        if (runtimeRoot == null)
            return false;

        string templatePath = GetOrCreateRuntimeTemplatePath(definition);
        if (string.IsNullOrEmpty(templatePath))
            return false;

        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(templatePath);
        bool saved = false;

        try
        {
            TerrainDecorationRuntimeBinder sourceBinder = runtimeRoot.GetComponent<TerrainDecorationRuntimeBinder>();

            TerrainDecorationRuntimeBinder targetBinder = prefabRoot.GetComponent<TerrainDecorationRuntimeBinder>();
            if (targetBinder == null)
                targetBinder = prefabRoot.AddComponent<TerrainDecorationRuntimeBinder>();

            TerrainDecorationRuntimeApplier targetApplier = prefabRoot.GetComponent<TerrainDecorationRuntimeApplier>();
            if (targetApplier == null)
                targetApplier = prefabRoot.AddComponent<TerrainDecorationRuntimeApplier>();

            targetBinder.definition = definition;
            targetBinder.instanceId = "template";
            targetBinder.selectedVariantId = sourceBinder != null && !string.IsNullOrWhiteSpace(sourceBinder.selectedVariantId)
                ? sourceBinder.selectedVariantId
                : GetFirstVariantId(definition);
            targetBinder.randomSeed = sourceBinder != null ? sourceBinder.randomSeed : targetBinder.randomSeed;
            targetBinder.placementEuler = sourceBinder != null ? sourceBinder.placementEuler : targetBinder.placementEuler;
            targetBinder.visualLocalEuler = sourceBinder != null ? sourceBinder.visualLocalEuler : targetBinder.visualLocalEuler;
            targetBinder.finalScale = sourceBinder != null ? sourceBinder.finalScale : targetBinder.finalScale;

            DisableRuntimeApplierAutoApply(targetApplier);
            targetApplier.EnsureStandardStructure(true);
            targetApplier.ApplyDefinition();
            DisableRuntimeApplierAutoApply(targetApplier);

            // Copy corrected generated/rule roots from Scene instance into the runtime template.
            // Do not copy VisualRoot. VisualRoot is generated from the definition variant PF.
            for (int i = 0; i < GeneratedRootNames.Length; i++)
                ReplaceChildFromSource(runtimeRoot.transform, prefabRoot.transform, GeneratedRootNames[i]);

            EditorUtility.SetDirty(prefabRoot);
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, templatePath);
            saved = true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        GameObject savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(templatePath);
        if (savedPrefab != null)
            EditorGUIUtility.PingObject(savedPrefab);

        if (log)
            Debug.Log($"[TerrainDecoration] 已把实例矫正结果写回源头模板：{templatePath}", savedPrefab);

        return saved;
    }

    private static void ReplaceChildFromSource(Transform sourceRoot, Transform targetRoot, string childName)
    {
        if (sourceRoot == null || targetRoot == null || string.IsNullOrEmpty(childName))
            return;

        Transform sourceChild = sourceRoot.Find(childName);
        if (sourceChild == null)
            return;

        Transform oldTargetChild = targetRoot.Find(childName);
        if (oldTargetChild != null)
            Object.DestroyImmediate(oldTargetChild.gameObject);

        GameObject clone = Object.Instantiate(sourceChild.gameObject);
        clone.name = childName;
        clone.transform.SetParent(targetRoot, false);
        clone.transform.localPosition = sourceChild.localPosition;
        clone.transform.localRotation = sourceChild.localRotation;
        clone.transform.localScale = sourceChild.localScale;
    }

    private static string GetOrCreateRuntimeTemplatePath(TerrainDecorationDefinition definition)
    {
        string existingPath = FindRuntimeTemplatePath(definition);
        if (!string.IsNullOrEmpty(existingPath))
            return existingPath;

        EnsureFolderExists(DefaultTemplateFolder);

        string safeId = MakeSafeId(!string.IsNullOrWhiteSpace(definition.decorationId) ? definition.decorationId : definition.name);
        if (string.IsNullOrWhiteSpace(safeId))
            safeId = "terrain_decoration";

        string path = AssetDatabase.GenerateUniqueAssetPath($"{DefaultTemplateFolder}/PF_TD_{safeId}.prefab");
        GameObject root = new GameObject($"PF_TD_{safeId}");
        try
        {
            TerrainDecorationRuntimeBinder binder = root.AddComponent<TerrainDecorationRuntimeBinder>();
            TerrainDecorationRuntimeApplier applier = root.AddComponent<TerrainDecorationRuntimeApplier>();
            binder.definition = definition;
            binder.instanceId = "template";
            binder.selectedVariantId = GetFirstVariantId(definition);
            DisableRuntimeApplierAutoApply(applier);
            applier.EnsureStandardStructure(true);
            applier.ApplyDefinition();
            DisableRuntimeApplierAutoApply(applier);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }

        return path;
    }

    private static string FindRuntimeTemplatePath(TerrainDecorationDefinition definition)
    {
        if (definition == null)
            return null;

        EnsureFolderExists(DefaultTemplateFolder);

        string safeId = MakeSafeId(!string.IsNullOrWhiteSpace(definition.decorationId) ? definition.decorationId : definition.name);
        string expectedName = "PF_TD_" + safeId;
        string expectedPath = $"{DefaultTemplateFolder}/{expectedName}.prefab";
        GameObject expected = AssetDatabase.LoadAssetAtPath<GameObject>(expectedPath);
        if (IsRuntimeTemplateForDefinition(expected, definition))
            return expectedPath;

        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { DefaultTemplateFolder });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (IsRuntimeTemplateForDefinition(prefab, definition))
                return path;
        }

        return null;
    }

    private static bool IsRuntimeTemplateForDefinition(GameObject prefab, TerrainDecorationDefinition definition)
    {
        if (prefab == null || definition == null)
            return false;

        TerrainDecorationRuntimeBinder binder = prefab.GetComponent<TerrainDecorationRuntimeBinder>();
        TerrainDecorationRuntimeApplier applier = prefab.GetComponent<TerrainDecorationRuntimeApplier>();
        if (binder == null || applier == null)
            return false;

        return IsSameDefinition(binder.definition, definition);
    }

    private static bool IsSameDefinition(TerrainDecorationDefinition a, TerrainDecorationDefinition b)
    {
        if (a == b)
            return true;
        if (a == null || b == null)
            return false;

        string pa = AssetDatabase.GetAssetPath(a);
        string pb = AssetDatabase.GetAssetPath(b);
        return !string.IsNullOrEmpty(pa) && pa == pb;
    }

    private static GameObject ResolveTerrainDecorationRuntimeRoot(GameObject go)
    {
        if (go == null)
            return null;

        TerrainDecorationRuntimeBinder binder = go.GetComponentInParent<TerrainDecorationRuntimeBinder>(true);
        if (binder != null)
            return binder.gameObject;

        TerrainDecorationRuntimeApplier applier = go.GetComponentInParent<TerrainDecorationRuntimeApplier>(true);
        return applier != null ? applier.gameObject : null;
    }

    private static void DisableRuntimeApplierAutoApply(TerrainDecorationRuntimeApplier applier)
    {
        if (applier == null)
            return;

        SerializedObject so = new SerializedObject(applier);
        SerializedProperty applyOnEnable = so.FindProperty("applyOnEnable");
        SerializedProperty applyInEditMode = so.FindProperty("applyInEditMode");
        if (applyOnEnable != null)
            applyOnEnable.boolValue = false;
        if (applyInEditMode != null)
            applyInEditMode.boolValue = false;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(applier);
    }

    private static string GetFirstVariantId(TerrainDecorationDefinition definition)
    {
        if (definition != null && definition.variants != null && definition.variants.Count > 0 && definition.variants[0] != null)
        {
            string id = definition.variants[0].variantId;
            if (!string.IsNullOrWhiteSpace(id))
                return id;
        }

        return "default";
    }

    private static string MakeSafeId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "terrain_decoration";

        value = value.Trim().ToLowerInvariant();
        System.Text.StringBuilder sb = new System.Text.StringBuilder(value.Length);
        bool lastUnderscore = false;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            bool ok = char.IsLetterOrDigit(c) || c == '_';
            if (!ok)
                c = '_';

            if (c == '_')
            {
                if (lastUnderscore)
                    continue;
                lastUnderscore = true;
            }
            else
            {
                lastUnderscore = false;
            }

            sb.Append(c);
        }

        string result = sb.ToString().Trim('_');
        return string.IsNullOrWhiteSpace(result) ? "terrain_decoration" : result;
    }

    private static void EnsureFolderExists(string folderPath)
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
