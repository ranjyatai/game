using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(10900)]
public sealed class SkyPrisonRuntimeSystemsBootstrapper : MonoBehaviour
{
    [Header("Version")]
    [SerializeField] private string scriptVersion = "V2 - 2026-06-10 - ensure runtime boot coordinator before authority and UI";

    [Header("Create Runtime Systems")]
    [SerializeField] private bool ensureRuntimeBootCoordinator = true;
    [SerializeField] private bool ensurePlayerAuthorityBootstrap = true;
    [SerializeField] private bool ensureRuntimeUIDriver = true;
    [SerializeField] private bool ensureWindowManager = true;
    [SerializeField] private bool ensureInventory = true;
    [SerializeField] private bool ensureCurrency = true;
    [SerializeField] private bool ensureItemPickup = true;
    // 2026-07-19：原 ensureHiddenOutlineOverlay（V16废弃管线的Canvas覆盖层）已连同
    // SkyPrisonHiddenOutlineCanvasOverlay_V3 脚本一起删除。
    [SerializeField] private bool ensureStatusIconBar = true;

    [Header("Runtime Object Names")]
    [SerializeField] private string runtimeSystemsRootName = "SkyPrisonRuntimeSystems";
    [SerializeField] private string runtimeBootCoordinatorName = "SkyPrisonRuntimeBootCoordinator_Runtime";
    [SerializeField] private string playerAuthorityBootstrapName = "SkyPrisonPlayerAuthorityBootstrap_Runtime";
    [SerializeField] private string runtimeUIDriverName = "SkyPrisonRuntimeUIDriver_Runtime";
    [SerializeField] private string windowManagerName = "SkyPrisonWindowManager_Runtime";
    [SerializeField] private string inventoryName = "SkyPrisonInventory_Runtime";
    [SerializeField] private string itemPickupName = "SkyPrisonItemPickup_Runtime";

    [Header("Scene Loading")]
    [SerializeField] private bool rescanAfterSceneLoaded = true;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    private static bool sceneLoadedSubscribed;

    public string Version => scriptVersion;

    /// <summary>在这些场景里不启动游戏运行时系统（主菜单、读条过渡等纯 UI 场景）。</summary>
    private static readonly string[] SkipScenes = { "MainMenu", "LoadingScene" };

    private static bool IsSkipScene(string sceneName)
    {
        foreach (var s in SkipScenes)
            if (sceneName == s) return true;
        return false;
    }

    // 每次 Play 都重置订阅状态，防止 Editor 关闭 Domain Reload 时静态残留导致漏订阅
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ResetSubscription()
    {
        if (sceneLoadedSubscribed)
        {
            SceneManager.sceneLoaded -= HandleSceneLoadedStatic;
            sceneLoadedSubscribed = false;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreateAfterSceneLoad()
    {
        if (!Application.isPlaying)
            return;

        // 第一个场景是 MainMenu / LoadingScene 时跳过，但仍订阅后续场景事件
        if (!IsSkipScene(SceneManager.GetActiveScene().name))
        {
            // 单位之间不互相推动：UnitBody 层自碰撞关闭
            int unitBodyLayer = LayerMask.NameToLayer("UnitBody");
            if (unitBodyLayer >= 0)
                Physics.IgnoreLayerCollision(unitBodyLayer, unitBodyLayer, true);

            EnsureRuntimeSystemsForCurrentScene();
        }

        if (!sceneLoadedSubscribed)
        {
            SceneManager.sceneLoaded += HandleSceneLoadedStatic;
            sceneLoadedSubscribed = true;
        }
    }

    private static void HandleSceneLoadedStatic(Scene scene, LoadSceneMode mode)
    {
        if (!Application.isPlaying)
            return;

        if (IsSkipScene(scene.name))
            return;

        EnsureRuntimeSystemsForCurrentScene();
    }

    /// <summary>
    /// Public entry for a formal loading flow.
    /// Call this once the map loading/readiness step has finished.
    /// It is safe to call multiple times.
    /// </summary>
    public static SkyPrisonRuntimeSystemsBootstrapper EnsureRuntimeSystemsForCurrentScene()
    {
        SkyPrisonRuntimeSystemsBootstrapper existing = FindObjectOfType<SkyPrisonRuntimeSystemsBootstrapper>();
        if (existing != null)
        {
            existing.EnsureRuntimeSystems();
            return existing;
        }

        GameObject root = GameObject.Find("SkyPrisonRuntimeSystems");
        if (root == null)
            root = new GameObject("SkyPrisonRuntimeSystems");

        SkyPrisonRuntimeSystemsBootstrapper bootstrapper = root.AddComponent<SkyPrisonRuntimeSystemsBootstrapper>();
        bootstrapper.EnsureRuntimeSystems();
        return bootstrapper;
    }

    private void Awake()
    {
        EnsureRuntimeSystems();
    }

    private void OnEnable()
    {
        if (rescanAfterSceneLoaded)
            SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDisable()
    {
        if (rescanAfterSceneLoaded)
            SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void Start()
    {
        EnsureRuntimeSystems();
        LockCursorForGameplay();
        // SignalReady 已迁移到 SkyPrisonRuntimeBootCoordinator.OpenGameplayTick()
        // 在 Phase4 完成（AI/状态系统全部开启）后才发信号，加载界面此时才揭幕
    }

    private static void LockCursorForGameplay()
    {
        // 这里只设默认初始态（鼠标模式：Confined；手柄模式会被 PlayerAimFacingController
        // 改成 Locked）。Cursor.visible 这个一次性初始值会在下一帧被 SkyPrisonCustomCursor
        // 接管改写（找到自定义光标图标就强制隐藏系统光标），不用在这里纠结对不对。
        Cursor.lockState = CursorLockMode.Confined;
        Cursor.visible   = true;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureRuntimeSystems();
    }

    [ContextMenu("Bootstrapper/Ensure Runtime Systems")]
    public void EnsureRuntimeSystems()
    {
        Transform root = EnsureRootTransform();

        if (ensureRuntimeBootCoordinator)
            EnsureChildComponent<SkyPrisonRuntimeBootCoordinator>(root, runtimeBootCoordinatorName);

        if (ensurePlayerAuthorityBootstrap)
            EnsureChildComponent<SkyPrisonPlayerAuthorityBootstrap>(root, playerAuthorityBootstrapName);

        if (ensureRuntimeUIDriver)
            EnsureChildComponent<SkyPrisonRuntimeUIDriver>(root, runtimeUIDriverName);

        if (ensureWindowManager)
            EnsureChildComponent<SkyPrison.Runtime.UI.SkyPrisonWindowManager_V1>(root, windowManagerName);

        if (ensureInventory)
            EnsureChildComponent<InventoryRuntimeBootstrap>(root, inventoryName);

        if (ensureCurrency)
            EnsureChildComponent<CurrencyRuntime>(root, "SkyPrisonCurrency_Runtime");

        if (ensureItemPickup)
            EnsureChildComponent<SkyPrison.Runtime.UI.SkyPrisonItemPickupController>(root, itemPickupName);

        if (ensureStatusIconBar)
            PlayerHUDStatusIconBar.EnsureInScene();

        if (debugLogs)
            Debug.Log("[SkyPrisonRuntimeSystemsBootstrapper] Runtime authority and UI systems ensured.", this);
    }

    private Transform EnsureRootTransform()
    {
        GameObject rootObject = GameObject.Find(runtimeSystemsRootName);
        if (rootObject == null)
            rootObject = gameObject;

        if (rootObject.name != runtimeSystemsRootName)
            rootObject.name = runtimeSystemsRootName;

        return rootObject.transform;
    }

    private T EnsureChildComponent<T>(Transform root, string objectName) where T : Component
    {
        T existing = FindObjectOfType<T>();
        if (existing != null)
            return existing;

        Transform child = root.Find(objectName);
        GameObject childObject;
        if (child != null)
        {
            childObject = child.gameObject;
        }
        else
        {
            childObject = new GameObject(objectName);
            childObject.transform.SetParent(root, false);
        }

        T component = childObject.GetComponent<T>();
        if (component == null)
            component = childObject.AddComponent<T>();

        return component;
    }
}
