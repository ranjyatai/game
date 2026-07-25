using UnityEngine;

public enum OutlineState
{
    None,
    Player,
    Enemy,
    Item,
    Ally
}

[DefaultExecutionOrder(9100)]
public class UnitOutlineState : MonoBehaviour
{
    [Header("Outline State")]
    [SerializeField] private bool outlineEnabled = true;
    [SerializeField] private OutlineState currentState = OutlineState.Player;

    [Header("Auto References")]
    [SerializeField] private UnitDefinitionRuntimeBinder runtimeBinder;

    [Header("Normal Outline Proxies")]
    [SerializeField] private GameObject outlineProxyPlayer;
    [SerializeField] private GameObject outlineProxyEnemy;
    [SerializeField] private GameObject outlineProxyItem;
    [SerializeField] private GameObject outlineProxyAlly;

    [Header("Options")]
    [SerializeField] private bool autoFindOnAwake = true;
    [SerializeField] private bool applyOnEnable = true;
    [SerializeField] private bool enforceEveryLateUpdate = true;
    [SerializeField] private bool autoReadFromUnitDefinition = true;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    public bool OutlineEnabled => outlineEnabled;
    public OutlineState CurrentState => currentState;

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

        if (autoReadFromUnitDefinition)
            RefreshFromDefinition();

        ApplyState();
    }

    private void OnEnable()
    {
        if (applyOnEnable)
        {
            if (autoReadFromUnitDefinition)
                RefreshFromDefinition();

            ApplyState();
        }
    }

    private void LateUpdate()
    {
        if (enforceEveryLateUpdate)
        {
            if (autoReadFromUnitDefinition)
                RefreshFromDefinition();

            ApplyState();
        }
    }

    [ContextMenu("Auto Setup")]
    public void AutoSetup()
    {
        if (runtimeBinder == null)
            runtimeBinder = GetComponent<UnitDefinitionRuntimeBinder>();

        outlineProxyPlayer = FindDeepChildExact("OutlineProxy_Player");
        outlineProxyEnemy = FindDeepChildExact("OutlineProxy_Enemy");
        outlineProxyItem = FindDeepChildExact("OutlineProxy_Item");
        outlineProxyAlly = FindDeepChildExact("OutlineProxy_Ally");
    }

    [ContextMenu("Refresh From Definition")]
    public void RefreshFromDefinition()
    {
        if (runtimeBinder == null)
            runtimeBinder = GetComponent<UnitDefinitionRuntimeBinder>();

        if (runtimeBinder == null || runtimeBinder.UnitDefinitionAsset == null)
            return;

        UnitDefinition ud = runtimeBinder.UnitDefinitionAsset;
        if (ud == null)
            return;

        currentState = MapOutlineGroupToState(ud.ResolvedOutlineGroup);
        outlineEnabled = ud.useSharedOutlineGroup && ud.HasOutline;

        if (debugLogs)
        {
            Debug.Log(
                $"[UnitOutlineState] {name} <- UD {ud.name}, resolved={ud.ResolvedOutlineGroup}, enabled={outlineEnabled}, state={currentState}",
                this
            );
        }
    }

    public void SetOutlineEnabled(bool enabled)
    {
        outlineEnabled = enabled;
        ApplyState();
    }

    public void SetOutlineState(OutlineState state)
    {
        currentState = state;
        ApplyState();
    }

    [ContextMenu("Apply State")]
    public void ApplyState()
    {
        SetProxyActive(outlineProxyPlayer, false);
        SetProxyActive(outlineProxyEnemy, false);
        SetProxyActive(outlineProxyItem, false);
        SetProxyActive(outlineProxyAlly, false);

        if (!outlineEnabled)
        {
            if (debugLogs)
                Debug.Log($"[UnitOutlineState] {name} -> disabled", this);

            return;
        }

        switch (currentState)
        {
            case OutlineState.Player:
                SetProxyActive(outlineProxyPlayer, true);
                break;

            case OutlineState.Enemy:
                SetProxyActive(outlineProxyEnemy, true);
                break;

            case OutlineState.Item:
                SetProxyActive(outlineProxyItem, true);
                break;

            case OutlineState.Ally:
                SetProxyActive(outlineProxyAlly, true);
                break;

            case OutlineState.None:
            default:
                break;
        }

        if (debugLogs)
        {
            Debug.Log($"[UnitOutlineState] {name} -> state={currentState}, enabled={outlineEnabled}", this);
        }
    }

    private static OutlineState MapOutlineGroupToState(OutlineGroup group)
    {
        switch (group)
        {
            case OutlineGroup.Player:
                return OutlineState.Player;
            case OutlineGroup.Enemy:
                return OutlineState.Enemy;
            case OutlineGroup.Ally:
                return OutlineState.Ally;
            case OutlineGroup.Item:
                return OutlineState.Item;
            default:
                return OutlineState.None;
        }
    }

    private static void SetProxyActive(GameObject go, bool active)
    {
        if (go == null)
            return;

        if (go.activeSelf != active)
            go.SetActive(active);
    }

    private GameObject FindDeepChildExact(string exactName)
    {
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name == exactName)
                return t.gameObject;
        }

        return null;
    }
}