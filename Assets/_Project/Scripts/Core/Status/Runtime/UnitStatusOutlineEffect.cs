using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 状态描边发光（比如灼烧持续描边）。跟 SkyPrisonCharacterEnvironmentLightReceiver
/// 同一套做法：通过 MaterialPropertyBlock 喂 Spine-Skeleton.shader 已有的
/// _SkyPrison_StatusOutline* 参数，不建材质实例、不破坏SRP Batching。
/// 多个状态同时启用描边时，取 ActiveStatuses 里第一个 useStatusOutline=true 的。
///
/// 轮廓边缘蒙版由 UnitStatusOutlinePresenceFeature 每帧单独渲染（只画这一个单位的
/// Character2D层部件，跟其他单位隔离），通过 SetPresenceRenderTexture 喂进来，
/// 避免多个单位贴在一起时描边边界被"焊"在一起（见该Feature头部注释）。
/// </summary>
[DisallowMultipleComponent]
public class UnitStatusOutlineEffect : MonoBehaviour
{
    public const int PresenceLayer = 8; // Character2D，跟 CharacterPresenceFeature 用同一层

    private static readonly int OutlineColorId = Shader.PropertyToID("_SkyPrison_StatusOutlineColor");
    private static readonly int OutlineWidthId = Shader.PropertyToID("_SkyPrison_StatusOutlineWidthPixels");
    private static readonly int OutlineIntensityId = Shader.PropertyToID("_SkyPrison_StatusOutlineIntensity");
    private static readonly int OutlineWidthVarianceId = Shader.PropertyToID("_SkyPrison_StatusOutlineWidthVariance");
    private static readonly int OutlineFlowSpeedId = Shader.PropertyToID("_SkyPrison_StatusOutlineFlowSpeed");
    private static readonly int OutlineNoiseScaleId = Shader.PropertyToID("_SkyPrison_StatusOutlineNoiseScale");
    private static readonly int DissolveNoiseTexId = Shader.PropertyToID("_SkyPrison_DissolveNoiseTex");
    private static readonly int PresenceTexId = Shader.PropertyToID("_SkyPrison_StatusOutlinePresence");
    private static readonly int PresenceActiveId = Shader.PropertyToID("_SkyPrison_StatusOutlinePresenceActive");

    private static Texture2D s_DefaultNoise;
    private static bool s_DefaultNoiseLoadAttempted;

    [Header("来源")]
    [SerializeField] private UnitStatusController statusController;

    [Header("作用目标")]
    [Tooltip("勾选时每秒自动重新收集自己子物体下的所有Renderer（包括换装/运行时才生成的Spine渲染物体）。")]
    [SerializeField] private bool autoFindRenderers = true;
    [SerializeField] private Renderer[] targetRenderers;

    private MaterialPropertyBlock _block;
    private float _rendererRefreshTimer;
    private float _currentIntensity;
    private Color _currentColor = Color.white;
    private float _currentWidth = 3f;
    private float _currentWidthVariance = 0.6f;
    private float _currentFlowSpeed = 0.6f;
    private float _currentNoiseScale = 1.2f;

    // 缓动淡入淡出：不是简单按固定速率线性推近目标值，而是记录"朝当前方向已经走了
    // 多久"，用 smoothstep 缓动曲线换算成强度——点燃快、熄灭慢，像真的火苗，不是
    // 硬切/线性的机械感。方向切换时按当前强度反推等效已耗时，保证不跳变。
    private bool _fadingIn;
    private float _fadeElapsed;
    private StatusDefinition _lastActiveDefinition;

    private Renderer[] _presenceRenderers = System.Array.Empty<Renderer>();
    private RenderTexture _presenceRT;
    private bool _presenceActive;

    /// <summary>0~1的当前描边强度，UnitStatusOutlinePresenceFeature 用这个判断这个单位
    /// 这一帧要不要花代价渲染专属蒙版。</summary>
    public float CurrentIntensity => _currentIntensity;

    /// <summary>本单位 Character2D 层的渲染器列表，供 Feature 只画这些、不画到别的单位的蒙版里。</summary>
    public IReadOnlyList<Renderer> PresenceRenderers => _presenceRenderers;

    public static UnitStatusOutlineEffect EnsureOnRoot(GameObject unitRoot)
    {
        if (unitRoot == null)
            return null;

        UnitStatusOutlineEffect effect = unitRoot.GetComponent<UnitStatusOutlineEffect>();
        if (effect == null)
            effect = unitRoot.AddComponent<UnitStatusOutlineEffect>();

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

    private void LateUpdate()
    {
        _rendererRefreshTimer -= Time.unscaledDeltaTime;
        if (_rendererRefreshTimer <= 0f)
        {
            _rendererRefreshTimer = 1f;
            RefreshTargetRenderers();
        }

        StatusDefinition active = ResolveActiveOutlineDefinition();
        if (active != null)
            _lastActiveDefinition = active;

        bool isActive = active != null;
        if (isActive != _fadingIn)
        {
            // 方向刚切换：按当前已有强度反推"如果从头按新方向的时长走，已经相当于走了多久"，
            // 这样切换瞬间强度不会跳变，缓动曲线接得上。
            StatusDefinition paramsSource = active ?? _lastActiveDefinition;
            float durationForNewDirection = isActive
                ? (paramsSource != null ? Mathf.Max(0.0001f, paramsSource.statusOutlineFadeInSeconds) : 0.35f)
                : (paramsSource != null ? Mathf.Max(0.0001f, paramsSource.statusOutlineFadeOutSeconds) : 0.6f);
            float equivalentT = isActive ? _currentIntensity : (1f - _currentIntensity);
            _fadeElapsed = equivalentT * durationForNewDirection;
            _fadingIn = isActive;
        }

        StatusDefinition timingSource = active ?? _lastActiveDefinition;
        float fadeInSeconds = timingSource != null ? Mathf.Max(0.0001f, timingSource.statusOutlineFadeInSeconds) : 0.35f;
        float fadeOutSeconds = timingSource != null ? Mathf.Max(0.0001f, timingSource.statusOutlineFadeOutSeconds) : 0.6f;
        float duration = _fadingIn ? fadeInSeconds : fadeOutSeconds;

        _fadeElapsed += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(_fadeElapsed / duration);
        float eased = t * t * (3f - 2f * t); // smoothstep
        _currentIntensity = _fadingIn ? eased : (1f - eased);

        if (active != null)
        {
            _currentColor = active.statusOutlineColor;
            _currentWidth = Mathf.Max(1f, active.statusOutlineWidthPixels);
            _currentWidthVariance = Mathf.Clamp01(active.statusOutlineWidthVariance);
            _currentFlowSpeed = active.statusOutlineFlowSpeed;
            _currentNoiseScale = active.statusOutlineNoiseScale;
        }

        Apply();
    }

    private StatusDefinition ResolveActiveOutlineDefinition()
    {
        if (statusController == null)
            return null;

        IReadOnlyList<RuntimeStatusEntry> statuses = statusController.ActiveStatuses;
        if (statuses == null)
            return null;

        for (int i = 0; i < statuses.Count; i++)
        {
            RuntimeStatusEntry entry = statuses[i];
            if (entry == null || entry.definition == null)
                continue;

            if (!entry.definition.useStatusOutline)
                continue;

            return entry.definition;
        }

        return null;
    }

    private void RefreshTargetRenderers()
    {
        if (!autoFindRenderers)
            return;

        targetRenderers = GetComponentsInChildren<Renderer>(true);

        int count = 0;
        for (int i = 0; i < targetRenderers.Length; i++)
        {
            if (targetRenderers[i] != null && targetRenderers[i].gameObject.layer == PresenceLayer)
                count++;
        }

        _presenceRenderers = new Renderer[count];
        int idx = 0;
        for (int i = 0; i < targetRenderers.Length; i++)
        {
            if (targetRenderers[i] != null && targetRenderers[i].gameObject.layer == PresenceLayer)
                _presenceRenderers[idx++] = targetRenderers[i];
        }
    }

    /// <summary>UnitStatusOutlinePresenceFeature 每帧调用，喂这个单位专属的轮廓蒙版RT。
    /// RT内容会在这一帧渲染管线跑到对应Pass时才真正填好，这里只是绑引用，跟
    /// CharacterPresenceFeature通过Shader.SetGlobalTexture提前绑定同一个思路。</summary>
    public void SetPresenceRenderTexture(RenderTexture rt, bool active)
    {
        _presenceRT = rt;
        _presenceActive = active && rt != null;
    }

    private void Apply()
    {
        if (targetRenderers == null || targetRenderers.Length == 0)
            return;

        if (_block == null)
            _block = new MaterialPropertyBlock();

        Texture2D noiseTex = GetDefaultNoiseTexture();

        for (int i = 0; i < targetRenderers.Length; i++)
        {
            Renderer r = targetRenderers[i];
            if (r == null) continue;

            r.GetPropertyBlock(_block);
            _block.SetColor(OutlineColorId, _currentColor);
            _block.SetFloat(OutlineWidthId, _currentWidth);
            _block.SetFloat(OutlineIntensityId, _currentIntensity);
            _block.SetFloat(OutlineWidthVarianceId, _currentWidthVariance);
            _block.SetFloat(OutlineFlowSpeedId, _currentFlowSpeed);
            _block.SetFloat(OutlineNoiseScaleId, _currentNoiseScale);
            // 死亡溶解和状态描边流动共用同一张噪波贴图属性——两个系统都只是想要一张
            // 通用噪波，共享没有副作用；死亡时 UnitDeathController 会用它自己指定的
            // 贴图覆盖这个值，互不冲突。
            if (noiseTex != null)
                _block.SetTexture(DissolveNoiseTexId, noiseTex);

            if (_presenceRT != null)
                _block.SetTexture(PresenceTexId, _presenceRT);
            _block.SetFloat(PresenceActiveId, _presenceActive ? 1f : 0f);

            r.SetPropertyBlock(_block);
        }
    }

    private static Texture2D GetDefaultNoiseTexture()
    {
        if (s_DefaultNoise != null || s_DefaultNoiseLoadAttempted)
            return s_DefaultNoise;

        s_DefaultNoiseLoadAttempted = true;

        s_DefaultNoise = Resources.Load<Texture2D>("VFX/Dissolve/SkyPrison_DissolveNoise");
        if (s_DefaultNoise == null)
        {
            Sprite sprite = Resources.Load<Sprite>("VFX/Dissolve/SkyPrison_DissolveNoise");
            if (sprite != null)
                s_DefaultNoise = sprite.texture;
        }

        return s_DefaultNoise;
    }
}
