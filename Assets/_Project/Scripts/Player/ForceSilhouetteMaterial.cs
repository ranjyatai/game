using UnityEngine;

[DefaultExecutionOrder(10000)]
[RequireComponent(typeof(MeshRenderer))]
public class ForceSilhouetteMaterial : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private MeshRenderer targetRenderer;
    [SerializeField] private Material silhouetteMaterial;

    [Header("Options")]
    [SerializeField] private bool applyOnEnable = true;
    [SerializeField] private bool enforceEveryLateUpdate = true;
    [SerializeField] private bool debugLogs = false;

    private void Awake()
    {
        ResolveRenderer();
    }

    private void OnEnable()
    {
        ResolveRenderer();

        if (applyOnEnable)
            ApplySilhouetteMaterial(false);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (targetRenderer == null)
            targetRenderer = GetComponent<MeshRenderer>();
    }
#endif

    private void LateUpdate()
    {
        if (!enforceEveryLateUpdate)
            return;

        ApplySilhouetteMaterial(false);
    }

    [ContextMenu("Apply Silhouette Material Now")]
    public void ApplyNow()
    {
        ApplySilhouetteMaterial(true);
    }

    [ContextMenu("Auto Find Renderer")]
    public void ResolveRenderer()
    {
        if (targetRenderer == null)
            targetRenderer = GetComponent<MeshRenderer>();
    }

    private void ApplySilhouetteMaterial(bool forceLog)
    {
        ResolveRenderer();

        if (targetRenderer == null || silhouetteMaterial == null)
            return;

        Material[] mats = targetRenderer.sharedMaterials;
        if (mats == null || mats.Length == 0)
            return;

        bool changed = false;

        for (int i = 0; i < mats.Length; i++)
        {
            if (mats[i] != silhouetteMaterial)
            {
                mats[i] = silhouetteMaterial;
                changed = true;
            }
        }

        if (changed)
        {
            targetRenderer.sharedMaterials = mats;

            if (debugLogs || forceLog)
            {
                Debug.Log(
                    $"[ForceSilhouetteMaterial] Applied '{silhouetteMaterial.name}' to '{targetRenderer.name}' on '{name}'",
                    this
                );
            }
        }
    }
}