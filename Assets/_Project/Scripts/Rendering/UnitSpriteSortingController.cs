using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Runtime sorting controller for 2.5D units.
///
/// Responsibilities:
/// 1. Treat the unit body as one render-sorted object.
/// 2. Keep foot/stage shadow renderers below the unit body.
/// 3. Use a stable anchor, usually CollisionRoot / foot capsule, to calculate sorting order.
///
/// Important:
/// - This component does not move the unit.
/// - This component does not play animations.
/// - UnitDefinitionRuntimeApplier may auto-add this component and call AutoBind(...).
/// </summary>
[DisallowMultipleComponent]
[ExecuteAlways]
public class UnitSpriteSortingController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform sortAnchor;
    [SerializeField] private Transform bodyRoot;
    [SerializeField] private SortingGroup bodySortingGroup;
    [SerializeField] private Transform shadowRoot;

    [Header("Sorting")]
    [SerializeField] private bool enableSorting = true;
    [SerializeField] private Vector3 sortAxis = new Vector3(0f, 0f, -1f);
    // orderMultiplier=100 意味着深度差小于 0.005 个世界单位的两个单位会被 RoundToInt
    // 算出完全相同的 sortingOrder——两个胶囊体角色只要贴近到这个距离内（正常走近/
    // 擦肩而过时必然会经过这个窗口），谁盖住谁就变成"平局"，Unity 对相同 sortingOrder
    // 的绘制顺序不保证跟深度关系一致，这个瞬间就会表现成"遮挡判断出错/慢半拍"——
    // 玩家完全不动、只有敌人走近也会触发，因为问题不在移动逻辑，在排序精度本身。
    // 提高到 1000（0.0005 单位精度）把这个"平局窗口"缩小一个数量级；min/maxOrder
    // 相应放大，避免场景稍大一点的深度值就被卡在夹取范围顶到同一个值上（那样反而
    // 会在场景边缘制造出新的大片"平局区"）。
    [SerializeField] private int orderMultiplier = 1000;
    [SerializeField] private int orderOffset = 0;
    [SerializeField] private int minOrder = -300000;
    [SerializeField] private int maxOrder = 300000;

    [Header("Body")]
    [SerializeField] private bool autoCreateBodySortingGroup = true;
    [SerializeField] private bool forceBodySortingLayer = false;
    [SerializeField] private string bodySortingLayerName = "Default";

    [Header("Shadow / Projection")]
    [SerializeField] private bool manageShadowRenderers = true;
    [SerializeField] private int shadowOrderOffset = -20;
    [SerializeField] private bool forceShadowSortingLayer = false;
    [SerializeField] private string shadowSortingLayerName = "Default";
    [SerializeField] private string[] shadowNameKeywords =
    {
        "ShadowRoot",
        "Shadow_Projector",
        "Shadow",
        "Projector",
        "StageShadow",
        "BlobShadow",
        "CircleShadow",
        "投影",
        "阴影",
        "影"
    };

    [Header("Debug")]
    [SerializeField] private bool updateInEditMode = true;
    [SerializeField] private bool drawDebug;
    [SerializeField, HideInInspector] private int currentBodyOrder;
    [SerializeField, HideInInspector] private int currentShadowOrder;

    private readonly List<Renderer> bodyRenderers = new List<Renderer>(16);
    private readonly List<Renderer> shadowRenderers = new List<Renderer>(8);

    public Transform SortAnchor => sortAnchor;
    public int CurrentBodyOrder => currentBodyOrder;
    public int CurrentShadowOrder => currentShadowOrder;

    private void Reset()
    {
        AutoResolveReferences(true);
        ApplySorting();
    }

    private void Awake()
    {
        AutoResolveReferences(false);
        ApplySorting();
    }

    private void OnEnable()
    {
        AutoResolveReferences(false);
        ApplySorting();
    }

    private void OnValidate()
    {
        if (!isActiveAndEnabled)
            return;

        AutoResolveReferences(false);
        ApplySorting();
    }

    private void LateUpdate()
    {
        if (!enableSorting)
            return;

        if (!Application.isPlaying && !updateInEditMode)
            return;

        ApplySorting();
    }

    /// <summary>
    /// Backward-compatible entry used by UnitDefinitionRuntimeApplier.
    /// Binds the sorting anchor and optional body root, then refreshes cached renderers.
    /// </summary>
    public void Bind(Transform anchor, Transform visualRoot)
    {
        if (anchor != null)
            sortAnchor = anchor;

        if (visualRoot != null && !IsShadowTransform(visualRoot))
            bodyRoot = visualRoot;

        AutoResolveReferences(true);
        ApplySorting();
    }

    /// <summary>
    /// Called by UnitDefinitionRuntimeApplier.
    /// </summary>
    public void AutoBind(Transform anchor)
    {
        if (anchor != null)
            sortAnchor = anchor;

        AutoResolveReferences(true);
        ApplySorting();
    }

    /// <summary>
    /// Called by external tools when no explicit anchor is available.
    /// </summary>
    public void AutoBind()
    {
        AutoResolveReferences(true);
        ApplySorting();
    }

    public void ForceRefresh()
    {
        AutoResolveReferences(true);
        ApplySorting();
    }

    private void AutoResolveReferences(bool force)
    {
        if (sortAnchor == null || force)
            sortAnchor = ResolveSortAnchor();

        if (shadowRoot == null || force)
            shadowRoot = ResolveNamedChild(transform, "ShadowRoot");

        if (bodyRoot == null || force)
            bodyRoot = ResolveBodyRoot();

        if (bodySortingGroup == null || force)
            bodySortingGroup = ResolveBodySortingGroup();

        RefreshRendererCaches();
    }

    private Transform ResolveSortAnchor()
    {
        Transform collisionRoot = ResolveNamedChild(transform, "CollisionRoot");
        if (collisionRoot != null)
            return collisionRoot;

        CapsuleCollider capsule = GetComponentInChildren<CapsuleCollider>(true);
        if (capsule != null)
            return capsule.transform;

        Collider anyCollider = GetComponentInChildren<Collider>(true);
        if (anyCollider != null)
            return anyCollider.transform;

        return transform;
    }

    private Transform ResolveBodyRoot()
    {
        Transform visualRoot = ResolveNamedChild(transform, "VisualRoot");
        if (visualRoot != null && !IsShadowTransform(visualRoot))
            return visualRoot;

        Transform spineObject = ResolveNamedChild(transform, "Spine GameObject");
        if (spineObject != null && !IsShadowTransform(spineObject))
            return spineObject;

        Transform spineObjectNoSpace = ResolveNamedChild(transform, "SpineGameObject");
        if (spineObjectNoSpace != null && !IsShadowTransform(spineObjectNoSpace))
            return spineObjectNoSpace;

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            if (IsShadowTransform(renderer.transform))
                continue;

            return renderer.transform;
        }

        return transform;
    }

    private SortingGroup ResolveBodySortingGroup()
    {
        if (bodyRoot != null)
        {
            SortingGroup groupOnRoot = bodyRoot.GetComponent<SortingGroup>();
            if (groupOnRoot != null)
                return groupOnRoot;
        }

        SortingGroup[] groups = GetComponentsInChildren<SortingGroup>(true);
        foreach (SortingGroup group in groups)
        {
            if (group == null)
                continue;

            if (IsShadowTransform(group.transform))
                continue;

            return group;
        }

        if (!autoCreateBodySortingGroup)
            return null;

        Transform target = bodyRoot != null ? bodyRoot : transform;
        if (IsShadowTransform(target))
            target = transform;

        SortingGroup created = target.GetComponent<SortingGroup>();
        if (created == null)
            created = target.gameObject.AddComponent<SortingGroup>();

        return created;
    }

    private void RefreshRendererCaches()
    {
        bodyRenderers.Clear();
        shadowRenderers.Clear();

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            if (IsShadowTransform(renderer.transform))
            {
                shadowRenderers.Add(renderer);
                continue;
            }

            bodyRenderers.Add(renderer);
        }
    }

    private void ApplySorting()
    {
        if (!enableSorting)
            return;

        Transform anchor = sortAnchor != null ? sortAnchor : transform;
        currentBodyOrder = CalculateOrder(anchor.position);
        currentShadowOrder = Mathf.Clamp(currentBodyOrder + shadowOrderOffset, minOrder, maxOrder);

        ApplyBodyOrder(currentBodyOrder);
        ApplyShadowOrder(currentShadowOrder);
    }

    private int CalculateOrder(Vector3 worldPosition)
    {
        Vector3 axis = sortAxis;
        if (axis.sqrMagnitude <= 0.0001f)
            axis = new Vector3(0f, 0f, -1f);

        axis.Normalize();

        float depth = Vector3.Dot(worldPosition, axis);
        int order = Mathf.RoundToInt(depth * orderMultiplier) + orderOffset;
        return Mathf.Clamp(order, minOrder, maxOrder);
    }

    private void ApplyBodyOrder(int order)
    {
        if (bodySortingGroup != null)
        {
            bodySortingGroup.sortingOrder = order;

            if (forceBodySortingLayer && !string.IsNullOrEmpty(bodySortingLayerName))
                bodySortingGroup.sortingLayerName = bodySortingLayerName;

            return;
        }

        foreach (Renderer renderer in bodyRenderers)
        {
            if (renderer == null)
                continue;

            renderer.sortingOrder = order;

            if (forceBodySortingLayer && !string.IsNullOrEmpty(bodySortingLayerName))
                renderer.sortingLayerName = bodySortingLayerName;
        }
    }

    private void ApplyShadowOrder(int order)
    {
        if (!manageShadowRenderers)
            return;

        foreach (Renderer renderer in shadowRenderers)
        {
            if (renderer == null)
                continue;

            renderer.sortingOrder = order;

            if (forceShadowSortingLayer && !string.IsNullOrEmpty(shadowSortingLayerName))
                renderer.sortingLayerName = shadowSortingLayerName;
        }
    }

    private bool IsShadowTransform(Transform target)
    {
        if (target == null)
            return false;

        Transform t = target;
        while (t != null && t != transform.parent)
        {
            string name = t.name;
            if (ContainsShadowKeyword(name))
                return true;

            t = t.parent;
        }

        return false;
    }

    private bool ContainsShadowKeyword(string value)
    {
        if (string.IsNullOrEmpty(value) || shadowNameKeywords == null)
            return false;

        string lower = value.ToLowerInvariant();
        for (int i = 0; i < shadowNameKeywords.Length; i++)
        {
            string keyword = shadowNameKeywords[i];
            if (string.IsNullOrEmpty(keyword))
                continue;

            if (lower.Contains(keyword.ToLowerInvariant()))
                return true;
        }

        return false;
    }

    private static Transform ResolveNamedChild(Transform root, string childName)
    {
        if (root == null || string.IsNullOrEmpty(childName))
            return null;

        if (string.Equals(root.name, childName, StringComparison.OrdinalIgnoreCase))
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            Transform result = ResolveNamedChild(child, childName);
            if (result != null)
                return result;
        }

        return null;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!drawDebug)
            return;

        Transform anchor = sortAnchor != null ? sortAnchor : transform;
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(anchor.position, 0.08f);
        Gizmos.DrawLine(anchor.position, anchor.position + sortAxis.normalized);
    }
#endif
}
