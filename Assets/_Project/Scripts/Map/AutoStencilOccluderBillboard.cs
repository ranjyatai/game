using UnityEngine;

[ExecuteAlways]
public class AutoStencilOccluderBillboard : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform targetRoot;
    [SerializeField] private Camera targetCamera;

    [Header("Bounds")]
    [SerializeField] private Vector3 boundsPadding = new Vector3(0f, 0.15f, 0f);

    [Header("Placement")]
    [SerializeField] private float forwardOffset = 0.02f;
    [SerializeField] private float heightScale = 1.15f;
    [SerializeField] private float widthScale = 0.95f;

    [Header("Auto Update")]
    [SerializeField] private bool updateInPlayMode = true;
    [SerializeField] private bool updateInEditMode = true;

    [Header("Filters")]
    [SerializeField] private bool ignoreThisObjectAndChildren = true;
    [SerializeField] private bool ignoreDisabledRenderers = true;

    private void LateUpdate()
    {
        bool shouldUpdate =
            (Application.isPlaying && updateInPlayMode) ||
            (!Application.isPlaying && updateInEditMode);

        if (!shouldUpdate)
            return;

        Refresh();
    }

    [ContextMenu("Refresh Occluder")]
    public void Refresh()
    {
        if (targetRoot == null || targetCamera == null)
            return;

        if (!TryGetRendererBounds(targetRoot, out Bounds bounds))
            return;

        bounds.Expand(boundsPadding);

        Vector3 camForward = targetCamera.transform.forward;
        Vector3 lookDir = -camForward;
        lookDir.y = 0f;

        if (lookDir.sqrMagnitude < 0.000001f)
            lookDir = Vector3.forward;

        lookDir.Normalize();
        transform.rotation = Quaternion.LookRotation(lookDir, Vector3.up);

        Vector3 toCamera = (targetCamera.transform.position - bounds.center);
        if (toCamera.sqrMagnitude < 0.000001f)
            return;

        toCamera.Normalize();

        // 用世界轴 extents 近似沿相机方向的半深度
        float halfDepthApprox =
            Mathf.Abs(Vector3.Dot(Vector3.right, toCamera)) * bounds.extents.x +
            Mathf.Abs(Vector3.Dot(Vector3.up, toCamera)) * bounds.extents.y +
            Mathf.Abs(Vector3.Dot(Vector3.forward, toCamera)) * bounds.extents.z;

        if (!IsFinite(halfDepthApprox))
            return;

        Vector3 frontPoint = bounds.center + toCamera * halfDepthApprox;
        Vector3 finalPos = frontPoint + toCamera * forwardOffset;

        if (!IsFinite(finalPos))
            return;

        transform.position = finalPos;

        float autoWidth = Mathf.Max(bounds.size.x, bounds.size.z) * widthScale;
        float autoHeight = bounds.size.y * heightScale;

        if (!float.IsFinite(autoWidth) || !float.IsFinite(autoHeight))
            return;

        autoWidth = Mathf.Max(0.001f, autoWidth);
        autoHeight = Mathf.Max(0.001f, autoHeight);

        transform.localScale = new Vector3(autoWidth, autoHeight, 1f);
    }

    private bool TryGetRendererBounds(Transform root, out Bounds bounds)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);

        bool hasBounds = false;
        bounds = default;

        foreach (Renderer r in renderers)
        {
            if (r == null)
                continue;

            if (ignoreDisabledRenderers && !r.enabled)
                continue;

            if (ignoreThisObjectAndChildren)
            {
                if (r.transform == transform || r.transform.IsChildOf(transform))
                    continue;
            }

            // 忽略粒子、UI、可疑对象都可以后面再扩展
            Bounds rb = r.bounds;

            if (!IsFinite(rb.center) || !IsFinite(rb.size))
                continue;

            if (rb.size.sqrMagnitude < 0.000001f)
                continue;

            if (!hasBounds)
            {
                bounds = rb;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(rb);
            }
        }

        return hasBounds;
    }

    private static bool IsFinite(float v)
    {
        return !float.IsNaN(v) && !float.IsInfinity(v);
    }

    private static bool IsFinite(Vector3 v)
    {
        return IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);
    }
}