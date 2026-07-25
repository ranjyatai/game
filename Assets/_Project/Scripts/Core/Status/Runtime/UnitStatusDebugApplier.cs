using UnityEngine;

[DisallowMultipleComponent]
public class UnitStatusDebugApplier : MonoBehaviour
{
    [Header("测试状态")]
    [SerializeField] private StatusDefinition testStatus;
    [SerializeField] private int initialStacks = 1;
    [SerializeField] private float durationOverride = -1f;

    [Header("触发方式")]
    [SerializeField] private bool applyOnStart = true;
    [SerializeField] private KeyCode applyKey = KeyCode.F7;
    [SerializeField] private KeyCode removeKey = KeyCode.F8;

    [Header("启动时序")]
    [Tooltip("Start 后延迟多久才自动添加测试状态。这里必须延迟 Apply 本身，而不是只延迟 Tick，因为部分状态可能在 ApplyStatus 内部立即产生效果。")]
    [SerializeField] private float startApplyDelay = 0.35f;

    [Tooltip("测试状态添加后，再延迟多久才允许本调试器驱动 TickStatuses。")]
    [SerializeField] private float startTickDelayAfterApply = 0.05f;

    [Tooltip("是否由本调试器驱动 UnitStatusController.TickStatuses。若场景里已经有正式状态系统驱动 Tick，请关闭它，避免双 Tick。")]
    [SerializeField] private bool driveStatusTick = true;

    [Tooltip("本调试器驱动 Tick 时，自动关闭 UnitStatusController 自带的 Update Tick，避免同一个状态被双 Tick 或绕过启动闸门。")]
    [SerializeField] private bool takeOverControllerAutoTick = true;

    [Header("运行时总控")]
    [Tooltip("开启后，自动添加测试状态会等待 SkyPrisonRuntimeBootCoordinator 打开 Gameplay Tick。此时本脚本只负责 Apply/Remove，不再抢状态 Tick 主权。")]
    [SerializeField] private bool waitForRuntimeBootCoordinator = true;
    [SerializeField] private bool disableLocalTickDriverWhenBootCoordinatorExists = true;

    [Header("Version")]
    [SerializeField] private string scriptVersion = "V5 - 2026-06-10 - boot-coordinator aware debug status applier";

    [Header("调试")]
    [SerializeField] private bool debugLogs = true;

    [Header("运行时诊断 - 只读")]
    [SerializeField] private string runtimeDiagnostic = "-";
    [SerializeField] private float runtimeSinceEnable;
    [SerializeField] private bool runtimeAutoApplyDone;
    [SerializeField] private bool runtimeTickGateOpen;
    [SerializeField] private int runtimeApplyCount;
    [SerializeField] private int runtimeTickCount;

    private UnitStatusController controller;
    private bool controllerAutoTickBeforeTakeover = true;
    private bool controllerTickTakenOver;
    private float applyDelayTimer;
    private float tickDelayTimer;
    private bool waitingAutoApply;

    private void Awake()
    {
        controller = UnitStatusController.EnsureOnRoot(gameObject);
        ApplyTickOwnershipIfNeeded();
        ResetStartupGates();
    }

    private void OnEnable()
    {
        if (controller == null)
            controller = UnitStatusController.EnsureOnRoot(gameObject);

        ApplyTickOwnershipIfNeeded();
        ResetStartupGates();
    }

    private void OnDisable()
    {
        RestoreTickOwnershipIfNeeded();
    }

    private void Start()
    {
        if (!applyOnStart)
        {
            waitingAutoApply = false;
            runtimeDiagnostic = "ApplyOnStart=False，等待手动按键。";
            return;
        }

        waitingAutoApply = true;
        runtimeDiagnostic = $"等待自动添加测试状态。applyDelay={startApplyDelay:0.###}s";
    }

    private void Update()
    {
        runtimeSinceEnable += Time.deltaTime;

        if (controller == null)
            controller = UnitStatusController.EnsureOnRoot(gameObject);

        ApplyTickOwnershipIfNeeded();

        TickAutoApplyGate();
        TickStatusGate();

        if (!ShouldDeferTickToBootCoordinator() && driveStatusTick && runtimeTickGateOpen && controller != null)
        {
            controller.TickStatusesExternal(Time.deltaTime, "UnitStatusDebugApplier gate-open manual tick");
            runtimeTickCount++;
        }

        if (Input.GetKeyDown(applyKey))
            ApplyNowManual();

        if (Input.GetKeyDown(removeKey))
            RemoveNow();
    }


    private void ApplyTickOwnershipIfNeeded()
    {
        if (controller == null)
            return;

        if (ShouldDeferTickToBootCoordinator())
        {
            // 之前这里只是单纯不再接管，但如果 Awake() 抢在 BootCoordinator 单例出现前
            // 先跑过一次接管（不同物体的 Awake 顺序不保证先后），autoTickInUpdate 已经被
            // 关掉、controllerTickTakenOver 已经是 true——这里必须显式交还控制权，
            // 不然没人会把它改回 true，这个单位的状态系统从此再也没人驱动。
            RestoreTickOwnershipIfNeeded();
            return;
        }

        if (!driveStatusTick || !takeOverControllerAutoTick)
            return;

        if (!controllerTickTakenOver)
        {
            controllerAutoTickBeforeTakeover = controller.AutoTickInUpdate;
            controllerTickTakenOver = true;
        }

        controller.SetAutoTickInUpdate(false, "UnitStatusDebugApplier owns delayed test tick");
    }

    private void RestoreTickOwnershipIfNeeded()
    {
        if (controller == null || !controllerTickTakenOver)
            return;

        controller.SetAutoTickInUpdate(controllerAutoTickBeforeTakeover, "UnitStatusDebugApplier disabled restore");
        controllerTickTakenOver = false;
    }

    private bool ShouldDeferTickToBootCoordinator()
    {
        if (!disableLocalTickDriverWhenBootCoordinatorExists)
            return false;

        SkyPrisonRuntimeBootCoordinator coordinator = SkyPrisonRuntimeBootCoordinator.Instance;
        if (coordinator == null)
            return false;

        // 之前只要总控单例存在就无条件让路——但总控的状态Tick闸门要等玩家按E揭幕
        // （SceneLoader.OnGameplayResume）才会打开。如果是直接在地图场景按Play测试，
        // 跳过了主菜单→读条→揭幕这套流程，闸门可能整个测试会话都不会打开，调试器
        // 却因为"单例存在"就一直让路、不自己驱动Tick——两边互相等对方，DOT/时长
        // 永远卡住。只有闸门真的已经打开时才让路，没开就自己顶上去驱动。
        return coordinator.GameplayTickOpen;
    }

    private bool IsBootGateOpenForAutoApply()
    {
        if (!waitForRuntimeBootCoordinator)
            return true;

        SkyPrisonRuntimeBootCoordinator coordinator = SkyPrisonRuntimeBootCoordinator.Instance;
        return coordinator == null || coordinator.GameplayTickOpen;
    }

    private void ResetStartupGates()
    {
        runtimeSinceEnable = 0f;
        runtimeAutoApplyDone = false;
        runtimeTickGateOpen = false;
        runtimeApplyCount = 0;
        runtimeTickCount = 0;

        applyDelayTimer = Mathf.Max(0f, startApplyDelay);
        tickDelayTimer = Mathf.Max(0f, startTickDelayAfterApply);
        waitingAutoApply = false;
        runtimeDiagnostic = $"Startup gates reset. bootWait={waitForRuntimeBootCoordinator}, driveStatusTick={driveStatusTick}, deferTickToBoot={ShouldDeferTickToBootCoordinator()}, controllerAutoTick={(controller != null ? controller.AutoTickInUpdate.ToString() : "-")}";
    }

    private void TickAutoApplyGate()
    {
        if (!waitingAutoApply || runtimeAutoApplyDone)
            return;

        if (!IsBootGateOpenForAutoApply())
        {
            runtimeDiagnostic = "等待 RuntimeBootCoordinator 打开 Gameplay Tick 后再自动 Apply。";
            return;
        }

        applyDelayTimer -= Time.deltaTime;
        if (applyDelayTimer > 0f)
        {
            runtimeDiagnostic = $"等待自动 Apply。remain={applyDelayTimer:0.###}s";
            return;
        }

        waitingAutoApply = false;
        ApplyNowInternal("AutoDelayedStart");
    }

    private void TickStatusGate()
    {
        if (runtimeTickGateOpen || !runtimeAutoApplyDone)
            return;

        tickDelayTimer -= Time.deltaTime;
        if (tickDelayTimer > 0f)
        {
            runtimeDiagnostic = $"已 Apply，等待 Tick 开门。remain={tickDelayTimer:0.###}s";
            return;
        }

        runtimeTickGateOpen = true;
        runtimeDiagnostic = $"Tick gate open. driveStatusTick={driveStatusTick}";

        if (debugLogs)
            Debug.Log($"[UnitStatusDebugApplier] Tick gate open. driveStatusTick={driveStatusTick}", this);
    }

    private void ApplyNowManual()
    {
        waitingAutoApply = false;
        ApplyNowInternal("ManualKey");
    }

    [ContextMenu("Apply Test Status")]
    public void ApplyNow()
    {
        waitingAutoApply = false;
        ApplyNowInternal("ContextMenu");
    }

    private void ApplyNowInternal(string reason)
    {
        if (controller == null)
            controller = UnitStatusController.EnsureOnRoot(gameObject);

        if (controller == null || testStatus == null)
        {
            runtimeDiagnostic = "缺少 UnitStatusController 或 testStatus。";
            if (debugLogs)
                Debug.LogWarning("[UnitStatusDebugApplier] 缺少 UnitStatusController 或 testStatus。", this);
            return;
        }

        controller.ApplyStatus(testStatus, gameObject, Mathf.Max(1, initialStacks), durationOverride);

        runtimeAutoApplyDone = true;
        runtimeTickGateOpen = startTickDelayAfterApply <= 0f;
        tickDelayTimer = Mathf.Max(0f, startTickDelayAfterApply);
        runtimeApplyCount++;
        runtimeDiagnostic = $"Applied by {reason}: {testStatus.statusId} | Active={controller.ActiveStatuses.Count} | Visible={controller.VisibleStatuses.Count}";

        if (debugLogs)
            Debug.Log($"[UnitStatusDebugApplier] 已添加测试状态：{testStatus.statusId} | reason={reason} | Active={controller.ActiveStatuses.Count} | Visible={controller.VisibleStatuses.Count} | tickGateOpen={runtimeTickGateOpen}", this);
    }

    [ContextMenu("Remove Test Status")]
    public void RemoveNow()
    {
        if (controller == null || testStatus == null)
            return;

        bool removed = controller.RemoveStatus(testStatus.statusId);
        runtimeDiagnostic = $"Removed {testStatus.statusId}. removed={removed} | Active={controller.ActiveStatuses.Count}";

        if (debugLogs)
            Debug.Log($"[UnitStatusDebugApplier] 移除测试状态：{testStatus.statusId} | removed={removed} | Active={controller.ActiveStatuses.Count}", this);
    }
}
