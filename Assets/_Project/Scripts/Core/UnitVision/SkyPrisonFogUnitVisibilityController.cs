using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 单位专用：战争迷雾显隐控制。
///
/// 规则：
/// 1. 只处理 UnitDefinition.defineType == Character 的单位。
/// 2. 敌方/可隐藏单位进入玩家视野外时，本体 Renderer、投影代理、UI 一起隐藏。
/// 3. 地形、装饰物、道具、可破坏物不要挂这个脚本；即使误挂，默认也不会隐藏非 Character 定义。
/// </summary>
[DisallowMultipleComponent]
public class SkyPrisonFogUnitVisibilityController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UnitDefinitionRuntimeBinder runtimeBinder;

    [Tooltip("单位本体/Spine/模型/影子代理等 Renderer。单位进入迷雾时会一起隐藏，避免影子泄露位置。")]
    [SerializeField] private Renderer[] targetRenderers;

    [Tooltip("血条、状态图标、描边根节点等。单位进入迷雾时会一起隐藏。")]
    [SerializeField] private GameObject[] targetGameObjectRoots;

    [Header("Auto Collect")]
    [SerializeField] private bool autoCollectRenderers = true;
    [SerializeField] private bool includeInactiveChildren = true;

    [Tooltip("开启后，只允许 Character 类型单位被这个控制器隐藏。地形/装饰/道具误挂该脚本时不会被隐藏。")]
    [SerializeField] private bool onlyApplyToCharacterDefinitions = true;

    [Header("Apply Mode")]
    [SerializeField] private bool hideByRendererEnabled = true;

    [Tooltip("不建议打开。关闭根节点会导致控制器自己也停止工作。需要隐藏 UI 时请填 targetGameObjectRoots。")]
    [SerializeField] private bool disableGameObjectWhenHidden = false;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    private bool lastVisibleState = true;
    private bool hasAppliedOnce = false;

    private void Awake()
    {
        ResolveReferences();
        ApplyVisibilityImmediate(force: true);
    }

    private void OnEnable()
    {
        ResolveReferences();
        ApplyVisibilityImmediate(force: true);
    }

    private void LateUpdate()
    {
        ApplyVisibilityImmediate(force: false);
    }

    [ContextMenu("Refresh Renderer Cache")]
    public void ResolveReferences()
    {
        if (runtimeBinder == null)
            runtimeBinder = GetComponent<UnitDefinitionRuntimeBinder>();

        if (runtimeBinder == null)
            runtimeBinder = GetComponentInParent<UnitDefinitionRuntimeBinder>();

        if (autoCollectRenderers || targetRenderers == null || targetRenderers.Length == 0)
            targetRenderers = CollectOwnUnitRenderers();
    }

    [ContextMenu("Apply Visibility Now")]
    public void ApplyVisibilityImmediate()
    {
        ApplyVisibilityImmediate(force: true);
    }

    private void ApplyVisibilityImmediate(bool force)
    {
        if (runtimeBinder == null || runtimeBinder.UnitDefinitionAsset == null)
            return;

        UnitDefinition ud = runtimeBinder.UnitDefinitionAsset;

        // 地形/装饰物/道具/可破坏物不应该被单位迷雾脚本隐藏。
        // 这样即使误挂了该脚本，也不会把场景投影一起关掉。
        if (onlyApplyToCharacterDefinitions && ud.defineType != UnitDefineType.Character)
        {
            ApplyVisibleState(true);
            lastVisibleState = true;
            hasAppliedOnce = true;
            return;
        }

        SkyPrisonVisionManager manager = SkyPrisonVisionManager.Instance;
        if (manager == null)
            return;

        bool visible = !manager.ShouldHideUnitFromPlayer(runtimeBinder);
        if (!force && hasAppliedOnce && visible == lastVisibleState)
            return;

        lastVisibleState = visible;
        hasAppliedOnce = true;
        ApplyVisibleState(visible);

        if (debugLogs)
            Debug.Log($"[SkyPrisonFogUnitVisibilityController] {name}: visible={visible}, defineType={ud.defineType}", this);
    }

    private Renderer[] CollectOwnUnitRenderers()
    {
        Renderer[] found = GetComponentsInChildren<Renderer>(includeInactiveChildren);
        if (found == null || found.Length == 0)
            return found;

        // 只收集属于同一个 UnitDefinitionRuntimeBinder 层级的 Renderer。
        // 避免单位根下如果临时挂了外部装饰/特效，被误收入单位显隐列表。
        List<Renderer> results = new List<Renderer>(found.Length);
        for (int i = 0; i < found.Length; i++)
        {
            Renderer r = found[i];
            if (r == null)
                continue;

            UnitDefinitionRuntimeBinder ownerBinder = r.GetComponentInParent<UnitDefinitionRuntimeBinder>();
            if (ownerBinder != null && ownerBinder != runtimeBinder)
                continue;

            results.Add(r);
        }

        return results.ToArray();
    }

    private void ApplyVisibleState(bool visible)
    {
        if (targetRenderers != null)
        {
            for (int i = 0; i < targetRenderers.Length; i++)
            {
                Renderer r = targetRenderers[i];
                if (r == null)
                    continue;

                if (hideByRendererEnabled)
                    r.enabled = visible;
            }
        }

        if (targetGameObjectRoots != null)
        {
            for (int i = 0; i < targetGameObjectRoots.Length; i++)
            {
                GameObject root = targetGameObjectRoots[i];
                if (root == null)
                    continue;

                root.SetActive(visible);
            }
        }

        if (disableGameObjectWhenHidden)
        {
            // 不直接关根节点，避免把自己也关掉。
            // 保留字段只是为了兼容旧 prefab 上的序列化数据。
        }
    }
}
