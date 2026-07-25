using UnityEngine;

/// <summary>
/// 高层建筑"可显示高度"淡出——建筑自己底部往上超过 heightFadeThreshold 的部分，
/// 在接下来 heightFadeDistance 这段距离内逐渐淡出到全透明，避免高楼把镜头和地图
/// 背景之间的视野挡得太死（见 [[game-design-and-progress]] 记忆"渲染/演出资源策略"）。
///
/// 只在 Awake 算一次——建筑是静态的，底部高度不会变，不需要每帧重算（跟
/// OccluderVisualFader 那种要响应"是否被遮挡"实时变化的场景不一样）。用
/// MaterialPropertyBlock 传参数，不碰 sharedMaterial，避免材质实例泄漏
/// （同 OccluderVisualFader 的做法）。
///
/// 这套流程本身是通用基础设施——目前用 SkyPrisonHeightFadeTestStructure.shader 验证，
/// 真正的建筑美术资产到位后，把高度淡出这几行 Shader 逻辑（SkyPrisonHeightFade.hlsl）
/// 搬进正式建筑材质用的 Shader 即可，这个 C# 组件不需要跟着换。
/// </summary>
[DisallowMultipleComponent]
public class SkyPrisonHeightFadeController : MonoBehaviour
{
    [Header("Visuals")]
    [SerializeField] private Renderer[] targetRenderers;
    [SerializeField] private bool autoFindRenderersOnAwake = true;
    [SerializeField] private bool includeInactiveChildren = true;

    [Header("Height Fade")]
    [Tooltip("从建筑自己底部往上算，超过这个高度（米）才开始淡出。")]
    [SerializeField] private float heightFadeThreshold = 10f;
    [Tooltip("淡出经过的距离（米）——超过 threshold 之后，再经过这段距离完全淡到透明。")]
    [SerializeField] private float heightFadeDistance = 2f;

    [Tooltip("建筑自己的“地面高度”参考点。留空则自动用所有 Renderer Bounds 的最低点。\n" +
             "地形不平、建筑底部嵌进斜坡里时，可以手动指定一个更准确的参考点。")]
    [SerializeField] private Transform groundReferenceOverride;

    private MaterialPropertyBlock _propertyBlock;

    private static readonly int HeightFadeBaseYId    = Shader.PropertyToID("_HeightFadeBaseY");
    private static readonly int HeightFadeThresholdId = Shader.PropertyToID("_HeightFadeThreshold");
    private static readonly int HeightFadeDistanceId  = Shader.PropertyToID("_HeightFadeDistance");

    private void Reset()
    {
        AutoResolveRenderers();
    }

    private void Awake()
    {
        if (autoFindRenderersOnAwake || targetRenderers == null || targetRenderers.Length == 0)
            AutoResolveRenderers();

        ApplyHeightFadeParams();
    }

    [ContextMenu("Auto Resolve Renderers")]
    public void AutoResolveRenderers()
    {
        targetRenderers = GetComponentsInChildren<Renderer>(includeInactiveChildren);
    }

    /// <summary>由 TerrainDecorationRuntimeApplier 按 TerrainDecorationDefinition 的
    /// heightFadeThreshold/heightFadeDistance 配置——放置工具/运行时加载装饰物时统一走
    /// 这一个入口，不用手动在每个实例的 Inspector 上分别填。</summary>
    public void Configure(float threshold, float distance)
    {
        heightFadeThreshold = threshold;
        heightFadeDistance  = distance;
        ApplyHeightFadeParams();
    }

    /// <summary>重新计算并应用一次高度淡出参数——改了 Inspector 里的
    /// threshold/distance，或者运行时挪动了建筑位置后，手动调用刷新。</summary>
    [ContextMenu("Apply Height Fade Params")]
    public void ApplyHeightFadeParams()
    {
        if (targetRenderers == null || targetRenderers.Length == 0)
            return;

        float baseY = ResolveBaseY();

        if (_propertyBlock == null)
            _propertyBlock = new MaterialPropertyBlock();

        for (int i = 0; i < targetRenderers.Length; i++)
        {
            Renderer renderer = targetRenderers[i];
            if (renderer == null) continue;

            Material shared = renderer.sharedMaterial;
            if (shared == null || !shared.HasProperty(HeightFadeBaseYId)) continue;

            renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetFloat(HeightFadeBaseYId, baseY);
            _propertyBlock.SetFloat(HeightFadeThresholdId, heightFadeThreshold);
            _propertyBlock.SetFloat(HeightFadeDistanceId, heightFadeDistance);
            renderer.SetPropertyBlock(_propertyBlock);
        }
    }

    private float ResolveBaseY()
    {
        if (groundReferenceOverride != null)
            return groundReferenceOverride.position.y;

        bool hasBounds = false;
        Bounds combined = default;
        for (int i = 0; i < targetRenderers.Length; i++)
        {
            Renderer renderer = targetRenderers[i];
            if (renderer == null) continue;

            if (!hasBounds)
            {
                combined = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                combined.Encapsulate(renderer.bounds);
            }
        }

        return hasBounds ? combined.min.y : transform.position.y;
    }
}
