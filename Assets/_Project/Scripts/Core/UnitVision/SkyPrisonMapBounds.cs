using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 关卡可玩区域 / 地图边界定义。
/// 第一版作用：
/// 1. 给战争迷雾提供精确覆盖范围
/// 2. 以后给小地图、触发器、孵化器、撤离区复用
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class SkyPrisonMapBounds : MonoBehaviour
{
    public enum BoundsSourceMode
    {
        Manual = 0,
        FromGroundRoot = 1,
    }

    [Header("Source")]
    public BoundsSourceMode sourceMode = BoundsSourceMode.FromGroundRoot;
    public Transform groundRoot;

    [Header("Manual Bounds")]
    public Vector3 center = Vector3.zero;
    public Vector3 size = new Vector3(64f, 6f, 64f);

    [Header("From Ground Root")]
    public bool includeInactiveRenderers = true;
    public Vector3 boundsPadding = new Vector3(2f, 2f, 2f);

    [Header("Debug")]
    public bool drawGizmo = true;
    public Color gizmoColor = new Color(0.2f, 0.85f, 1f, 0.75f);
    public bool drawLabel = true;

    [SerializeField] private Bounds resolvedBounds;

    public Bounds ResolvedBounds => resolvedBounds;

    private void OnEnable()
    {
        RefreshBounds();
    }

    private void OnValidate()
    {
        RefreshBounds();
    }

    [ContextMenu("Refresh Bounds")]
    public void RefreshBounds()
    {
        if (sourceMode == BoundsSourceMode.FromGroundRoot)
        {
            if (groundRoot == null)
            {
                GameObject go = GameObject.Find("GroundRoot");
                if (go != null)
                    groundRoot = go.transform;
            }

            if (groundRoot != null)
            {
                Renderer[] renderers = groundRoot.GetComponentsInChildren<Renderer>(includeInactiveRenderers);
                if (renderers != null && renderers.Length > 0)
                {
                    Bounds b = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++)
                        b.Encapsulate(renderers[i].bounds);

                    b.Expand(boundsPadding);
                    resolvedBounds = b;
                    center = b.center;
                    size = b.size;
                    return;
                }
            }
        }

        resolvedBounds = new Bounds(center, size);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!drawGizmo)
            return;

        RefreshBounds();

        Gizmos.color = gizmoColor;
        Gizmos.DrawWireCube(resolvedBounds.center, resolvedBounds.size);

        Color fill = gizmoColor;
        fill.a *= 0.08f;
        Gizmos.color = fill;
        Gizmos.DrawCube(resolvedBounds.center, resolvedBounds.size);

        if (drawLabel)
            Handles.Label(resolvedBounds.center + Vector3.up * (resolvedBounds.extents.y + 0.2f), $"MapBounds\n{resolvedBounds.size.x:0.#} x {resolvedBounds.size.z:0.#}");
    }
#endif
}
