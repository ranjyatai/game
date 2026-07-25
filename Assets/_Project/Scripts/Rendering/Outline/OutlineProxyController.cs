using UnityEngine;

[DefaultExecutionOrder(100)]
public class OutlineProxyController : MonoBehaviour
{
    [Header("Version")]
    [SerializeField] private string scriptVersion = "V2 - 2026-05-20 - Legacy Normal Outline Rollback";
    [SerializeField] private int compileTouchVersion = 2026052006;

    [Header("Source")]
    [SerializeField] private UnitOutlineState outlineState;

    [Header("Normal Outline Proxies")]
    [SerializeField] private GameObject outlineProxyPlayer;
    [SerializeField] private GameObject outlineProxyEnemy;
    [SerializeField] private GameObject outlineProxyItem;
    [SerializeField] private GameObject outlineProxyAlly;

    [Header("Options")]
    [SerializeField] private bool applyOnAwake = true;
    [SerializeField] private bool applyEveryFrame = false;
    [Tooltip("回滚旧描边表现：普通描边默认开启，由 UnitOutlineState 决定显示哪个普通代理。")]
    [SerializeField] private bool normalOutlineChannelEnabled = true;

    [Header("Debug")]
    [SerializeField] private bool debugCurrentNormalVisible;
    [SerializeField] private string debugApplyReason = "";

    private void Awake()
    {
        ResolveReferences();

        if (applyOnAwake)
            ApplyState();
    }

    private void OnEnable()
    {
        ResolveReferences();

        if (applyOnAwake)
            ApplyState();
    }

    private void LateUpdate()
    {
        if (applyEveryFrame)
            ApplyState();
    }

    [ContextMenu("Resolve References")]
    public void ResolveReferences()
    {
        if (outlineState == null)
            outlineState = GetComponentInParent<UnitOutlineState>();
    }

    [ContextMenu("Apply State")]
    public void ApplyState()
    {
        ResolveReferences();

        debugCurrentNormalVisible = false;
        debugApplyReason = "";

        if (outlineState == null)
        {
            CloseAllNormalProxies("missing UnitOutlineState");
            return;
        }

        if (!normalOutlineChannelEnabled)
        {
            CloseAllNormalProxies("normal outline channel disabled");
            return;
        }

        bool enabled = outlineState.OutlineEnabled;
        OutlineState state = outlineState.CurrentState;

        SetProxy(outlineProxyPlayer, enabled && state == OutlineState.Player);
        SetProxy(outlineProxyEnemy, enabled && state == OutlineState.Enemy);
        SetProxy(outlineProxyItem, enabled && state == OutlineState.Item);
        SetProxy(outlineProxyAlly, enabled && state == OutlineState.Ally);

        debugCurrentNormalVisible = enabled && (state == OutlineState.Player || state == OutlineState.Enemy || state == OutlineState.Item || state == OutlineState.Ally);
        debugApplyReason = debugCurrentNormalVisible ? $"legacy normal visible: {state}" : "outline disabled or unsupported state";
    }

    public void SetState(OutlineState state)
    {
        if (outlineState == null)
            ResolveReferences();

        if (outlineState == null)
            return;

        outlineState.SetOutlineState(state);
        ApplyState();
    }

    public void SetEnabled(bool enabled)
    {
        if (outlineState == null)
            ResolveReferences();

        if (outlineState == null)
            return;

        outlineState.SetOutlineEnabled(enabled);
        ApplyState();
    }

    private void CloseAllNormalProxies(string reason)
    {
        SetProxy(outlineProxyPlayer, false);
        SetProxy(outlineProxyEnemy, false);
        SetProxy(outlineProxyItem, false);
        SetProxy(outlineProxyAlly, false);

        debugCurrentNormalVisible = false;
        debugApplyReason = reason;
    }

    private void SetProxy(GameObject proxy, bool active)
    {
        if (proxy == null)
            return;

        if (proxy.activeSelf != active)
            proxy.SetActive(active);
    }
}
