using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(9500)]
public class UnitOccludedOutlineProxyGate : MonoBehaviour, IOcclusionStateReceiver
{
    public enum OutlineCamp
    {
        None,
        Player,
        Enemy,
        Ally,
        Item
    }

    [Header("Auto References")]
    [SerializeField] private UnitDefinitionRuntimeBinder runtimeBinder;
    [SerializeField] private UnitOutlineState unitOutlineState;

    [Header("Normal Outline Proxies")]
    [SerializeField] private GameObject outlineProxyPlayerNormal;
    [SerializeField] private GameObject outlineProxyEnemyNormal;
    [SerializeField] private GameObject outlineProxyAllyNormal;
    [SerializeField] private GameObject outlineProxyItemNormal;

    [Header("Occluded Outline Proxies")]
    [SerializeField] private GameObject outlineProxyPlayerOccluded;
    [SerializeField] private GameObject outlineProxyEnemyOccluded;
    [SerializeField] private GameObject outlineProxyAllyOccluded;
    [SerializeField] private GameObject outlineProxyItemOccluded;

    [Header("Options")]
    [SerializeField] private bool autoFindOnAwake = true;
    [SerializeField] private bool autoRefreshCamp = true;
    [Tooltip("建议开启：如果旧 OutlineProxyController 仍在 LateUpdate 里维护普通代理，本脚本会在更晚执行顺序重新应用遮挡代理状态，避免被覆盖。")]
    [SerializeField] private bool enforceEveryLateUpdate = true;
    [Tooltip("遮挡状态下如果没有专用 OutlineProxy_xxx_Occluded，则自动退回使用普通 OutlineProxy_xxx，避免只有 Player 有遮挡描边、Enemy/Ally/Item 缺失。")]
    [SerializeField] private bool useNormalProxyWhenOccludedProxyMissing = true;

    [Header("Commercial Exit Hysteresis")]
    [Tooltip("商业级遮挡退出保护：进入遮挡立即生效；退出遮挡延迟若干帧，避免单位边缘还在物体后面时提前跳到物体前面。")]
    [SerializeField] private bool useCommercialExitHysteresis = true;
    [Tooltip("遮挡物通知 false 后仍保持 OccludedProxy 的帧数。0 表示立即释放。")]
    [SerializeField] private int exitHoldFrames = 4;

    [Header("Detected")]
    [SerializeField] private OutlineCamp activeCamp = OutlineCamp.None;
    [SerializeField] private bool hiddenPartOutlineAllowed = true;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private int activeOccluderCount = 0;
    [SerializeField] private bool currentOccluded = false;
    [SerializeField] private bool finalOccludedVisible = false;
    [SerializeField] private bool finalNormalVisible = false;


    // V3 Build-safe public state for RenderFeature.
    // Do not rely on private-field reflection in Player builds / IL2CPP.
    public OutlineCamp ActiveCamp => activeCamp;
    public bool CurrentOccluded => currentOccluded;
    public bool FinalOccludedVisible => finalOccludedVisible;
    public bool HiddenPartOutlineAllowed => hiddenPartOutlineAllowed;
    public int ActiveOccluderCount => activeOccluderCount;

    // V4: Build-safe public active occluder references for unit-authorized occluder masks.
    // The RenderFeature must not use a global OccluderMask_All for every unit, otherwise a rear unit can make a different front unit intersect the same global occluder.
    public int GetActiveOccluders(List<MonoBehaviour> results)
    {
        if (results == null)
            return 0;

        results.Clear();
        foreach (var kv in activeOccluderRefs)
        {
            MonoBehaviour occluder = kv.Value;
            if (occluder == null)
                continue;
            if (!activeOccluders.Contains(kv.Key))
                continue;
            results.Add(occluder);
        }
        return results.Count;
    }

    public GameObject OutlineProxyPlayerOccluded => outlineProxyPlayerOccluded;
    public GameObject OutlineProxyEnemyOccluded => outlineProxyEnemyOccluded;
    public GameObject OutlineProxyAllyOccluded => outlineProxyAllyOccluded;
    public GameObject OutlineProxyItemOccluded => outlineProxyItemOccluded;

    public GameObject GetOccludedProxyForActiveCamp()
    {
        return GetOccludedProxyForCamp(activeCamp);
    }

    public GameObject GetOccludedProxyForCamp(OutlineCamp camp)
    {
        switch (camp)
        {
            case OutlineCamp.Player: return outlineProxyPlayerOccluded != null ? outlineProxyPlayerOccluded : outlineProxyPlayerNormal;
            case OutlineCamp.Enemy: return outlineProxyEnemyOccluded != null ? outlineProxyEnemyOccluded : outlineProxyEnemyNormal;
            case OutlineCamp.Ally: return outlineProxyAllyOccluded != null ? outlineProxyAllyOccluded : outlineProxyAllyNormal;
            case OutlineCamp.Item: return outlineProxyItemOccluded != null ? outlineProxyItemOccluded : outlineProxyItemNormal;
            default: return null;
        }
    }

    private readonly HashSet<int> activeOccluders = new();
    private readonly Dictionary<int, MonoBehaviour> activeOccluderRefs = new Dictionary<int, MonoBehaviour>();
    private readonly Dictionary<int, int> pendingReleaseFrames = new Dictionary<int, int>();

    private bool lastAppliedOccluded = false;
    private bool hasAppliedOnce = false;
    private OutlineCamp lastAppliedCamp = OutlineCamp.None;
    private bool lastAppliedHiddenAllowed = false;
    private bool lastAppliedFallbackMode = false;

    private void Reset()
    {
        AutoSetup();
    }

    private void OnValidate()
    {
        AutoSetup();
    }

    private void Awake()
    {
        if (autoFindOnAwake)
            AutoSetup();

        ApplyState(false, true, true);
    }

    private void OnEnable()
    {
        if (autoFindOnAwake)
            AutoSetup();

        activeOccluders.Clear();
        activeOccluderRefs.Clear();
        pendingReleaseFrames.Clear();
        activeOccluderCount = 0;
        currentOccluded = false;
        finalOccludedVisible = false;
        finalNormalVisible = false;

        hasAppliedOnce = false;
        lastAppliedOccluded = false;
        lastAppliedCamp = OutlineCamp.None;
        lastAppliedHiddenAllowed = false;
        lastAppliedFallbackMode = false;

        ApplyState(false, true, true);
    }

    private void LateUpdate()
    {
        if (autoRefreshCamp)
            RefreshCamp();

        TickPendingReleases();

        if (enforceEveryLateUpdate)
            ApplyState(currentOccluded, true, false);
    }

    private void OnDisable()
    {
        activeOccluders.Clear();
        activeOccluderRefs.Clear();
        pendingReleaseFrames.Clear();
        activeOccluderCount = 0;
        currentOccluded = false;
        finalOccludedVisible = false;
        finalNormalVisible = false;

        hasAppliedOnce = false;
        lastAppliedOccluded = false;
        lastAppliedCamp = OutlineCamp.None;
        lastAppliedHiddenAllowed = false;
        lastAppliedFallbackMode = false;

        ApplyState(false, true, true);
    }

    public void SetOccludedBy(MonoBehaviour occluder, bool isOccluded)
    {
        if (occluder == null)
            return;

        int id = occluder.GetInstanceID();

        if (isOccluded)
        {
            pendingReleaseFrames.Remove(id);
            activeOccluders.Add(id);
            activeOccluderRefs[id] = occluder;
        }
        else
        {
            if (useCommercialExitHysteresis && exitHoldFrames > 0 && activeOccluders.Contains(id))
                pendingReleaseFrames[id] = Mathf.Max(1, exitHoldFrames);
            else
            {
                activeOccluders.Remove(id);
                activeOccluderRefs.Remove(id);
            }
        }

        activeOccluderCount = activeOccluders.Count;

        bool nextOccluded = activeOccluders.Count > 0;
        if (nextOccluded == currentOccluded && !autoRefreshCamp)
            return;

        currentOccluded = nextOccluded;

        if (autoRefreshCamp)
            RefreshCamp();

        ApplyState(currentOccluded, false, false);

        if (debugLogs)
        {
            Debug.Log(
                $"[UnitOccludedOutlineProxyGate] {name}, camp={activeCamp}, allowed={hiddenPartOutlineAllowed}, occluded={currentOccluded}, active={activeOccluderCount}",
                this
            );
        }
    }


    private void TickPendingReleases()
    {
        if (pendingReleaseFrames.Count == 0)
            return;

        List<int> keys = new List<int>(pendingReleaseFrames.Keys);
        bool changed = false;

        for (int i = 0; i < keys.Count; i++)
        {
            int id = keys[i];
            if (!pendingReleaseFrames.TryGetValue(id, out int frames))
                continue;

            frames--;
            if (frames > 0)
            {
                pendingReleaseFrames[id] = frames;
                continue;
            }

            pendingReleaseFrames.Remove(id);
            if (activeOccluders.Remove(id))
            {
                activeOccluderRefs.Remove(id);
                changed = true;
            }
        }

        if (!changed)
            return;

        activeOccluderCount = activeOccluders.Count;
        bool nextOccluded = activeOccluderCount > 0;
        if (nextOccluded == currentOccluded)
            return;

        currentOccluded = nextOccluded;
        if (autoRefreshCamp)
            RefreshCamp();

        ApplyState(currentOccluded, false, false);
    }

    [ContextMenu("Auto Setup")]
    public void AutoSetup()
    {
        ResolveRuntimeBinder();
        ResolveUnitOutlineState();
        ResolveNormalOutlineProxies();
        ResolveOccludedProxies();
        RefreshCamp();
    }

    [ContextMenu("Resolve Runtime Binder")]
    public void ResolveRuntimeBinder()
    {
        if (runtimeBinder == null)
            runtimeBinder = GetComponent<UnitDefinitionRuntimeBinder>();
    }

    [ContextMenu("Resolve UnitOutlineState")]
    public void ResolveUnitOutlineState()
    {
        if (unitOutlineState == null)
            unitOutlineState = GetComponent<UnitOutlineState>();
    }

    [ContextMenu("Resolve Normal Outline Proxies")]
    public void ResolveNormalOutlineProxies()
    {
        outlineProxyPlayerNormal = FindDeepChildGameObjectExact("OutlineProxy_Player");
        outlineProxyEnemyNormal = FindDeepChildGameObjectExact("OutlineProxy_Enemy");
        outlineProxyAllyNormal = FindDeepChildGameObjectExact("OutlineProxy_Ally");
        outlineProxyItemNormal = FindDeepChildGameObjectExact("OutlineProxy_Item");
    }

    [ContextMenu("Resolve Occluded Proxies")]
    public void ResolveOccludedProxies()
    {
        outlineProxyPlayerOccluded = FindDeepChildGameObjectExact("OutlineProxy_Player_Occluded");
        outlineProxyEnemyOccluded = FindDeepChildGameObjectExact("OutlineProxy_Enemy_Occluded");
        outlineProxyAllyOccluded = FindDeepChildGameObjectExact("OutlineProxy_Ally_Occluded");
        outlineProxyItemOccluded = FindDeepChildGameObjectExact("OutlineProxy_Item_Occluded");
    }

    [ContextMenu("Refresh Camp")]
    public void RefreshCamp()
    {
        ResolveRuntimeBinder();
        ResolveUnitOutlineState();

        if (TryReadCampFromUnitDefinition(out var campFromDefinition, out var allowHiddenFromDefinition))
        {
            activeCamp = campFromDefinition;
            hiddenPartOutlineAllowed = allowHiddenFromDefinition;
            return;
        }

        if (TryReadCampFromUnitOutlineState(out var campFromOutlineState))
        {
            activeCamp = campFromOutlineState;
            hiddenPartOutlineAllowed = activeCamp != OutlineCamp.None;
            return;
        }

        if (TryReadCampFromTag(out var campFromTag))
        {
            activeCamp = campFromTag;
            hiddenPartOutlineAllowed = activeCamp != OutlineCamp.None;
            return;
        }

        activeCamp = DetectCampFromNormalOutlineProxyFallback();
        hiddenPartOutlineAllowed = activeCamp != OutlineCamp.None;
    }

    private bool TryReadCampFromUnitDefinition(out OutlineCamp camp, out bool allowHidden)
    {
        camp = OutlineCamp.None;
        allowHidden = false;

        if (runtimeBinder == null || runtimeBinder.UnitDefinitionAsset == null)
            return false;

        UnitDefinition ud = runtimeBinder.UnitDefinitionAsset;
        if (ud == null)
            return false;

        camp = MapOutlineGroupToCamp(ud.ResolvedOutlineGroup);
        allowHidden = ud.EnableHiddenPartOutline;
        return true;
    }

    private static OutlineCamp MapOutlineGroupToCamp(OutlineGroup group)
    {
        switch (group)
        {
            case OutlineGroup.Player:
                return OutlineCamp.Player;
            case OutlineGroup.Enemy:
                return OutlineCamp.Enemy;
            case OutlineGroup.Ally:
                return OutlineCamp.Ally;
            case OutlineGroup.Item:
                return OutlineCamp.Item;
            default:
                return OutlineCamp.None;
        }
    }

    private bool TryReadCampFromUnitOutlineState(out OutlineCamp camp)
    {
        camp = OutlineCamp.None;

        if (unitOutlineState == null)
            return false;

        switch (unitOutlineState.CurrentState)
        {
            case OutlineState.Player:
                camp = OutlineCamp.Player;
                return true;
            case OutlineState.Enemy:
                camp = OutlineCamp.Enemy;
                return true;
            case OutlineState.Ally:
                camp = OutlineCamp.Ally;
                return true;
            case OutlineState.Item:
                camp = OutlineCamp.Item;
                return true;
            default:
                return false;
        }
    }

    private bool TryReadCampFromTag(out OutlineCamp camp)
    {
        camp = OutlineCamp.None;

        string s = tag;
        if (string.IsNullOrWhiteSpace(s))
            return false;

        s = s.ToLowerInvariant();

        if (s.Contains("player"))
        {
            camp = OutlineCamp.Player;
            return true;
        }

        if (s.Contains("enemy"))
        {
            camp = OutlineCamp.Enemy;
            return true;
        }

        if (s.Contains("ally") || s.Contains("friendly") || s.Contains("npc"))
        {
            camp = OutlineCamp.Ally;
            return true;
        }

        if (s.Contains("item") || s.Contains("loot") || s.Contains("destructible"))
        {
            camp = OutlineCamp.Item;
            return true;
        }

        return false;
    }

    private OutlineCamp DetectCampFromNormalOutlineProxyFallback()
    {
        if (outlineProxyPlayerNormal != null)
            return OutlineCamp.Player;

        if (outlineProxyEnemyNormal != null)
            return OutlineCamp.Enemy;

        if (outlineProxyAllyNormal != null)
            return OutlineCamp.Ally;

        if (outlineProxyItemNormal != null)
            return OutlineCamp.Item;

        return OutlineCamp.None;
    }

    private void ApplyState(bool occluded, bool silent, bool force)
    {
        bool needApply =
            force ||
            !hasAppliedOnce ||
            lastAppliedOccluded != occluded ||
            lastAppliedCamp != activeCamp ||
            lastAppliedHiddenAllowed != hiddenPartOutlineAllowed ||
            lastAppliedFallbackMode != useNormalProxyWhenOccludedProxyMissing;

        if (!needApply)
            return;

        // 先全部关闭，防止双态共存
        SetProxyActive(outlineProxyPlayerNormal, false);
        SetProxyActive(outlineProxyEnemyNormal, false);
        SetProxyActive(outlineProxyAllyNormal, false);
        SetProxyActive(outlineProxyItemNormal, false);

        SetProxyActive(outlineProxyPlayerOccluded, false);
        SetProxyActive(outlineProxyEnemyOccluded, false);
        SetProxyActive(outlineProxyAllyOccluded, false);
        SetProxyActive(outlineProxyItemOccluded, false);

        finalOccludedVisible = false;
        finalNormalVisible = false;

        if (!hiddenPartOutlineAllowed)
        {
            hasAppliedOnce = true;
            lastAppliedOccluded = occluded;
            lastAppliedCamp = activeCamp;
            lastAppliedHiddenAllowed = hiddenPartOutlineAllowed;
            lastAppliedFallbackMode = useNormalProxyWhenOccludedProxyMissing;

            if (debugLogs && !silent)
            {
                Debug.Log(
                    $"[UnitOccludedOutlineProxyGate] ApplyState -> {name}, hidden part outline disabled by rule.",
                    this
                );
            }
            return;
        }

        GameObject targetProxy = GetProxyForCamp(activeCamp, occluded, out bool usedOccludedProxy);
        if (targetProxy != null)
        {
            SetProxyActive(targetProxy, true);
            finalOccludedVisible = occluded;
            finalNormalVisible = !occluded || !usedOccludedProxy;
        }

        hasAppliedOnce = true;
        lastAppliedOccluded = occluded;
        lastAppliedCamp = activeCamp;
        lastAppliedHiddenAllowed = hiddenPartOutlineAllowed;
        lastAppliedFallbackMode = useNormalProxyWhenOccludedProxyMissing;

        if (debugLogs && !silent)
        {
            Debug.Log(
                $"[UnitOccludedOutlineProxyGate] ApplyState -> {name}, camp={activeCamp}, allowed={hiddenPartOutlineAllowed}, occluded={occluded}, normal={finalNormalVisible}, occludedVisible={finalOccludedVisible}",
                this
            );
        }
    }

    private GameObject GetProxyForCamp(OutlineCamp camp, bool occluded, out bool usedOccludedProxy)
    {
        usedOccludedProxy = false;

        GameObject normal = null;
        GameObject occludedProxy = null;

        switch (camp)
        {
            case OutlineCamp.Player:
                normal = outlineProxyPlayerNormal;
                occludedProxy = outlineProxyPlayerOccluded;
                break;
            case OutlineCamp.Enemy:
                normal = outlineProxyEnemyNormal;
                occludedProxy = outlineProxyEnemyOccluded;
                break;
            case OutlineCamp.Ally:
                normal = outlineProxyAllyNormal;
                occludedProxy = outlineProxyAllyOccluded;
                break;
            case OutlineCamp.Item:
                normal = outlineProxyItemNormal;
                occludedProxy = outlineProxyItemOccluded;
                break;
        }

        if (!occluded)
            return normal;

        if (occludedProxy != null)
        {
            usedOccludedProxy = true;
            return occludedProxy;
        }

        if (useNormalProxyWhenOccludedProxyMissing)
            return normal;

        return null;
    }

    private static void SetProxyActive(GameObject go, bool active)
    {
        if (go == null)
            return;

        if (go.activeSelf != active)
            go.SetActive(active);
    }

    private GameObject FindDeepChildGameObjectExact(string exactName)
    {
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name == exactName)
                return t.gameObject;
        }

        return null;
    }
}
