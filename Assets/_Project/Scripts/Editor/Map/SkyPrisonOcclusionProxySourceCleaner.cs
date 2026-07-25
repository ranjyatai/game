#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Sky Prison scene-instance occlusion proxy source cleaner.
/// V1 - 2026-05-22 - keep model proxy as sole mask source when a valid model proxy exists.
///
/// Purpose:
/// - Fix mixed occlusion-mask source cases where FrontOccluderProxy_Model and
///   FrontOccluderProxy_Box/__AutoFrontOccluderShapeClone are all active/renderable.
/// - Do NOT delete nodes.
/// - Do NOT touch VisualRoot.
/// - Do NOT touch BackTrigger / FrontTrigger logic.
/// - Do NOT change physics colliders.
/// - Only disables non-model proxy renderers when a valid model proxy renderer exists.
/// </summary>
public static class SkyPrisonOcclusionProxySourceCleaner_V1
{
    private const string MenuRoot = "Tools/Sky Prison/Map/遮挡矫正/";
    private const string ModelPath = "RuleRoot/FrontOccluderRoot/FrontOccluderProxy_Model";
    private const string BoxPath = "RuleRoot/FrontOccluderRoot/FrontOccluderProxy_Box";

    [MenuItem(MenuRoot + "净化选中物体Mask来源/只保留Model代理", false, 1540)]
    public static void CleanSelected()
    {
        GameObject[] selected = Selection.gameObjects;
        if (selected == null || selected.Length == 0)
        {
            EditorUtility.DisplayDialog("遮挡代理来源净化", "请先在 Hierarchy 里选择一个或多个地图物体实例。", "OK");
            return;
        }

        int roots = 0;
        int cleaned = 0;
        int disabled = 0;

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Sky Prison Clean Selected Occlusion Proxy Sources");

        foreach (GameObject go in selected)
        {
            if (go == null)
                continue;

            Transform root = FindDecorationRoot(go.transform);
            if (root == null)
                continue;

            roots++;
            if (CleanOne(root, out int disabledHere))
            {
                cleaned++;
                disabled += disabledHere;
            }
        }

        Undo.CollapseUndoOperations(undoGroup);

        Debug.Log($"[SkyPrisonOcclusionProxySourceCleaner V1] Selected clean done. roots={roots}, cleaned={cleaned}, disabledNonModelRenderers={disabled}");
        EditorUtility.DisplayDialog("遮挡代理来源净化", $"完成。\nroots={roots}\ncleaned={cleaned}\ndisabledNonModelRenderers={disabled}", "OK");
    }

    [MenuItem(MenuRoot + "净化场景全部Mask来源/Model优先", false, 1541)]
    public static void CleanAllInScene()
    {
        Transform[] all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        HashSet<Transform> roots = new HashSet<Transform>();
        foreach (Transform t in all)
        {
            if (t == null)
                continue;

            Transform root = FindDecorationRoot(t);
            if (root != null)
                roots.Add(root);
        }

        int cleaned = 0;
        int disabled = 0;

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Sky Prison Clean All Occlusion Proxy Sources");

        foreach (Transform root in roots)
        {
            if (CleanOne(root, out int disabledHere))
            {
                cleaned++;
                disabled += disabledHere;
            }
        }

        Undo.CollapseUndoOperations(undoGroup);

        Debug.Log($"[SkyPrisonOcclusionProxySourceCleaner V1] Scene clean done. roots={roots.Count}, cleaned={cleaned}, disabledNonModelRenderers={disabled}");
        EditorUtility.DisplayDialog("遮挡代理来源净化", $"完成。\nroots={roots.Count}\ncleaned={cleaned}\ndisabledNonModelRenderers={disabled}", "OK");
    }

    private static bool CleanOne(Transform root, out int disabledCount)
    {
        disabledCount = 0;
        if (root == null)
            return false;

        Transform model = root.Find(ModelPath);
        Transform box = root.Find(BoxPath);

        if (model == null || box == null)
            return false;

        Renderer[] modelRenderers = model.GetComponentsInChildren<Renderer>(true);
        bool hasValidModelRenderer = false;
        foreach (Renderer r in modelRenderers)
        {
            if (r == null)
                continue;

            // A valid model proxy means there is a renderer that can be used as mask source.
            // Keep it enabled. If the parent is inactive, runtime trigger may still activate it later.
            hasValidModelRenderer = true;

            if (!r.enabled)
            {
                Undo.RecordObject(r, "Enable model proxy renderer");
                r.enabled = true;
                EditorUtility.SetDirty(r);
            }
        }

        if (!hasValidModelRenderer)
            return false;

        Renderer[] boxRenderers = box.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in boxRenderers)
        {
            if (r == null)
                continue;

            // Do not disable renderers that are actually under the model branch by accident.
            if (r.transform == model || r.transform.IsChildOf(model))
                continue;

            if (r.enabled)
            {
                Undo.RecordObject(r, "Disable non-model proxy renderer");
                r.enabled = false;
                disabledCount++;
                EditorUtility.SetDirty(r);
            }
        }

        EditorUtility.SetDirty(root.gameObject);
        return disabledCount > 0;
    }

    private static Transform FindDecorationRoot(Transform t)
    {
        if (t == null)
            return null;

        Transform current = t;
        while (current != null)
        {
            if (current.Find("VisualRoot") != null && current.Find("RuleRoot/FrontOccluderRoot") != null)
                return current;

            current = current.parent;
        }

        return null;
    }
}
#endif
