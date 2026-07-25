using UnityEngine;

public class OccluderVisualFader : MonoBehaviour
{
    [Header("Visuals")]
    [SerializeField] private Renderer[] targetRenderers;
    [SerializeField] private bool autoFindRenderersOnAwake = false;
    [SerializeField] private bool includeInactiveChildren = true;

    [Header("Fade")]
    [SerializeField] private float normalAlpha = 1f;
    [SerializeField] private float occludedAlpha = 0.35f;
    [SerializeField] private float fadeSpeed = 8f;

    [Header("Runtime")]
    [SerializeField] private bool isOccluded;
    [SerializeField] private bool debugLogs = false;

    private float currentAlpha = 1f;
    private MaterialPropertyBlock propertyBlock;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private void Reset()
    {
        AutoResolveRenderers();
    }

    private void Awake()
    {
        if (autoFindRenderersOnAwake || targetRenderers == null || targetRenderers.Length == 0)
            AutoResolveRenderers();

        EnsurePropertyBlock();
        currentAlpha = normalAlpha;
        ApplyAlpha(currentAlpha);
    }

    private void OnEnable()
    {
        EnsurePropertyBlock();
        currentAlpha = normalAlpha;
        ApplyAlpha(currentAlpha);
    }

    private void OnDisable()
    {
        currentAlpha = normalAlpha;
        ApplyAlpha(currentAlpha);
        isOccluded = false;
    }

    private void Update()
    {
        float targetAlpha = isOccluded ? occludedAlpha : normalAlpha;
        currentAlpha = Mathf.Lerp(currentAlpha, targetAlpha, Time.deltaTime * fadeSpeed);

        if (Mathf.Abs(currentAlpha - targetAlpha) < 0.01f)
            currentAlpha = targetAlpha;

        ApplyAlpha(currentAlpha);
    }

    [ContextMenu("Auto Resolve Renderers")]
    public void AutoResolveRenderers()
    {
        targetRenderers = GetComponentsInChildren<Renderer>(includeInactiveChildren);
    }

    // 兼容旧版 TerrainDecorationRuntimeApplier / SimpleDirectionalOccluder 的调用名。
    [ContextMenu("Refresh Runtime Materials")]
    public void CacheRuntimeMaterials()
    {
        // 新版不再访问 renderer.material，避免编辑器模式下实例化材质泄漏。
        EnsurePropertyBlock();
        ApplyAlpha(currentAlpha <= 0f ? normalAlpha : currentAlpha);
    }

    public void ConfigureTargetRenderers(Renderer[] renderers, float targetOccludedAlpha, float targetFadeDuration)
    {
        targetRenderers = renderers ?? new Renderer[0];
        occludedAlpha = Mathf.Clamp01(targetOccludedAlpha);

        if (targetFadeDuration <= 0.0001f)
            fadeSpeed = 1000f;
        else
            fadeSpeed = Mathf.Max(0.01f, 1f / targetFadeDuration);

        EnsurePropertyBlock();
        currentAlpha = normalAlpha;
        ApplyAlpha(currentAlpha);
    }

    public void SetOccluded(bool occluded)
    {
        isOccluded = occluded;

        if (debugLogs)
            Debug.Log($"[OccluderVisualFader] SetOccluded = {occluded}", this);
    }

    public bool IsOccluded()
    {
        return isOccluded;
    }

    private void EnsurePropertyBlock()
    {
        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();
    }

    private void ApplyAlpha(float alpha)
    {
        if (targetRenderers == null)
            return;

        EnsurePropertyBlock();

        for (int i = 0; i < targetRenderers.Length; i++)
        {
            Renderer renderer = targetRenderers[i];
            if (renderer == null)
                continue;

            Material shared = renderer.sharedMaterial;
            if (shared == null)
                continue;

            int colorProperty = -1;
            if (shared.HasProperty(BaseColorId))
                colorProperty = BaseColorId;
            else if (shared.HasProperty(ColorId))
                colorProperty = ColorId;
            else
                continue;

            Color color = shared.GetColor(colorProperty);
            color.a = Mathf.Clamp01(alpha);

            renderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(colorProperty, color);
            renderer.SetPropertyBlock(propertyBlock);

            if (debugLogs)
                Debug.Log($"[OccluderVisualFader] {renderer.name} alpha = {alpha}", renderer);
        }
    }
}
