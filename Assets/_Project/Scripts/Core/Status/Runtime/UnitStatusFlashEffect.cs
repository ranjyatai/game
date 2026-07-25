using UnityEngine;

/// <summary>
/// 状态效果响应闪烁——每次某个状态实际产生一次效果（目前是DOT tick跳伤害）时，
/// 角色本体全身半透明呼吸一下，给玩家一个"这一下真的生效了"的即时反馈。
/// 跟状态描边（UnitStatusOutlineEffect）一样按状态定义单独配置：开关/颜色/时长/
/// 半透明强度都在 StatusDefinition 的"状态效果响应闪烁"分组里，只有开了
/// useStatusFlash 的状态触发DOT tick时才会闪，用的是这个状态自己配的参数。
/// 强度按 sin 曲线 0→1→0 起伏一次（"呼吸"感，不是硬闪），衰减完自动清零。
/// </summary>
[DisallowMultipleComponent]
public class UnitStatusFlashEffect : MonoBehaviour
{
    private static readonly int FlashActiveId = Shader.PropertyToID("_SkyPrison_StatusFlashActive");
    private static readonly int FlashProgressId = Shader.PropertyToID("_SkyPrison_StatusFlashProgress");
    private static readonly int FlashColorId = Shader.PropertyToID("_SkyPrison_StatusFlashColor");
    private static readonly int FlashAlphaDipId = Shader.PropertyToID("_SkyPrison_StatusFlashAlphaDip");

    [Header("来源")]
    [SerializeField] private UnitStatusController statusController;

    [Header("作用目标")]
    [Tooltip("勾选时每秒自动重新收集自己子物体下的所有Renderer（包括换装/运行时才生成的Spine渲染物体）。")]
    [SerializeField] private bool autoFindRenderers = true;
    [SerializeField] private Renderer[] targetRenderers;

    private MaterialPropertyBlock _block;
    private float _rendererRefreshTimer;
    private float _flashTimer;
    private float _flashDuration = 0.3f;
    private Color _flashColor = Color.white;
    private float _flashAlphaDip = 0.35f;

    public static UnitStatusFlashEffect EnsureOnRoot(GameObject unitRoot)
    {
        if (unitRoot == null)
            return null;

        UnitStatusFlashEffect effect = unitRoot.GetComponent<UnitStatusFlashEffect>();
        if (effect == null)
            effect = unitRoot.AddComponent<UnitStatusFlashEffect>();

        effect.AutoSetup();
        return effect;
    }

    private void Awake()
    {
        AutoSetup();
        _block = new MaterialPropertyBlock();
        RefreshTargetRenderers();
    }

    [ContextMenu("Auto Setup")]
    public void AutoSetup()
    {
        if (statusController == null)
            statusController = GetComponent<UnitStatusController>();
    }

    private void OnEnable()
    {
        AutoSetup();
        if (statusController != null)
        {
            statusController.DotDamageApplied -= HandleDotDamageApplied;
            statusController.DotDamageApplied += HandleDotDamageApplied;
        }
    }

    private void OnDisable()
    {
        if (statusController != null)
            statusController.DotDamageApplied -= HandleDotDamageApplied;
    }

    private void HandleDotDamageApplied(RuntimeStatusEntry entry, float amount)
    {
        StatusDefinition definition = entry != null ? entry.definition : null;
        if (definition == null || !definition.useStatusFlash)
            return;

        _flashColor = definition.statusFlashColor;
        _flashDuration = Mathf.Max(0.0001f, definition.statusFlashDuration);
        _flashAlphaDip = Mathf.Clamp01(definition.statusFlashAlphaDip);
        // 新一次tick打断上一次还没播完的呼吸，直接从头开始，不叠加。
        _flashTimer = _flashDuration;
    }

    private void LateUpdate()
    {
        _rendererRefreshTimer -= Time.unscaledDeltaTime;
        if (_rendererRefreshTimer <= 0f)
        {
            _rendererRefreshTimer = 1f;
            RefreshTargetRenderers();
        }

        bool active = _flashTimer > 0f;
        float progress = 0f;
        if (active)
        {
            _flashTimer -= Time.deltaTime;
            progress = Mathf.Clamp01(1f - Mathf.Max(0f, _flashTimer) / _flashDuration);
        }

        Apply(active, progress);
    }

    private void RefreshTargetRenderers()
    {
        if (!autoFindRenderers)
            return;

        targetRenderers = GetComponentsInChildren<Renderer>(true);
    }

    private void Apply(bool active, float progress)
    {
        if (targetRenderers == null || targetRenderers.Length == 0)
            return;

        if (_block == null)
            _block = new MaterialPropertyBlock();

        for (int i = 0; i < targetRenderers.Length; i++)
        {
            Renderer r = targetRenderers[i];
            if (r == null) continue;

            r.GetPropertyBlock(_block);
            _block.SetFloat(FlashActiveId, active ? 1f : 0f);
            _block.SetFloat(FlashProgressId, progress);
            _block.SetColor(FlashColorId, _flashColor);
            _block.SetFloat(FlashAlphaDipId, _flashAlphaDip);
            r.SetPropertyBlock(_block);
        }
    }
}
