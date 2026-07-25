using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
[DefaultExecutionOrder(7900)]
public sealed class SkyPrisonRuntimeBootCoordinator : MonoBehaviour
{
    [Header("Version")]
    [SerializeField] private string scriptVersion = "V3 - 2026-06-10 - presentation effects allowed by default + clear Phase5 state";

    [Header("Phase Gates")]
    [SerializeField] private bool runBootOnStart = true;
    [SerializeField] private bool closeStatusAutoTickDuringBoot = true;
    [SerializeField] private bool openStatusTickAfterHudReady = true;
    [SerializeField] private bool openPresentationEffectsAfterGameplayReady = true;

    [Header("Wait Limits")]
    [SerializeField] private float waitPlayerTimeout = 2f;
    [SerializeField] private float waitHudTimeout = 2f;
    [SerializeField] private int stableFramesBeforeGameplay = 0;

    [Header("Scene Names")]
    [SerializeField] private string runtimeSystemsRootName = "SkyPrisonRuntimeSystems";
    [SerializeField] private string runtimeUIDriverName = "SkyPrisonRuntimeUIDriver_Runtime";

    [Header("Ownership Audit")]
    [SerializeField] private bool runAuditAfterEveryPhase = true;
    [SerializeField] private bool runAuditAfterBoot = true;
    [SerializeField] private bool keepAuditingAfterBoot = false;
    [SerializeField] private float auditIntervalSeconds = 1.0f;
    [SerializeField] private bool warnOnHudInternalCamera = true;
    [SerializeField] private bool warnOnHudPostProcessRuntime = true;
    [SerializeField] private bool warnOnMultipleMainCameras = true;
    [SerializeField] private bool warnOnStatusTickBeforeGameplayOpen = true;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;
    [SerializeField, TextArea(6, 16)] private string bootTimeline = "";
    [SerializeField, TextArea(8, 20)] private string ownershipAuditReport = "";

    [Header("Runtime State - Read Only")]
    [SerializeField] private bool bootRunning;
    [SerializeField] private bool sceneFoundationReady;
    [SerializeField] private bool unitFoundationReady;
    [SerializeField] private bool occlusionFoundationReady;
    [SerializeField] private bool hudFoundationReady;
    [SerializeField] private bool gameplayTickOpen;
    [SerializeField] private bool presentationEffectsPhaseReached;
    [SerializeField] private bool presentationEffectsOpen;
    [SerializeField] private Camera mainCamera;
    [SerializeField] private SkyPrisonUnitRuntimeIdentity currentPlayer;
    [SerializeField] private SkyPrisonRuntimeUIDriver runtimeUIDriver;
    [SerializeField] private int statusControllersLocked;
    [SerializeField] private int statusControllersOpened;

    [Header("Audit Counters - Read Only")]
    [SerializeField] private int auditActiveCameras;
    [SerializeField] private int auditMainCameraCandidates;
    [SerializeField] private int auditTargetTextureCameras;
    [SerializeField] private int auditHudInternalCameras;
    [SerializeField] private int auditHudPostProcessRenderers;
    [SerializeField] private int auditRuntimeHudInstances;
    [SerializeField] private int auditStatusControllers;
    [SerializeField] private int auditStatusAutoTickEnabled;
    [SerializeField] private int auditDebugAppliers;
    [SerializeField] private int auditDebugApplierTickDrivers;

    private static SkyPrisonRuntimeBootCoordinator instance;
    private readonly List<string> timelineLines = new List<string>(48);
    private float nextAuditRealtime;

    public static SkyPrisonRuntimeBootCoordinator Instance => instance;
    public static bool IsGameplayTickOpen => instance != null && instance.gameplayTickOpen;
    public static bool IsPresentationEffectsOpen => instance != null && instance.presentationEffectsOpen;

    /// <summary>
    /// Phase0~Phase3（场景/单位/遮挡/HUD）四项就绪状态的完成占比，0~1。
    /// 供 loading screen 的进度条真实反映初始化进度，而不是靠固定时间估算——
    /// 真的卡住时进度条会如实停住，不会撒谎说"快好了"。
    /// </summary>
    public static float BootProgress01
    {
        get
        {
            if (instance == null) return 0f;
            int done = 0;
            const int total = 4;
            if (instance.sceneFoundationReady) done++;
            if (instance.unitFoundationReady) done++;
            if (instance.occlusionFoundationReady) done++;
            if (instance.hudFoundationReady) done++;
            return (float)done / total;
        }
    }
    public string Version => scriptVersion;
    public bool GameplayTickOpen => gameplayTickOpen;
    public bool PresentationEffectsPhaseReached => presentationEffectsPhaseReached;
    public bool PresentationEffectsOpen => presentationEffectsOpen;
    public SkyPrisonRuntimeUIDriver RuntimeUIDriver => runtimeUIDriver;
    public string OwnershipAuditReport => ownershipAuditReport;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoEnsureAfterSceneLoad()
    {
        if (!Application.isPlaying)
            return;

        EnsureInScene();
    }

    public static SkyPrisonRuntimeBootCoordinator EnsureInScene()
    {
        if (instance != null)
            return instance;

        SkyPrisonRuntimeBootCoordinator existing = FindObjectOfType<SkyPrisonRuntimeBootCoordinator>();
        if (existing != null)
        {
            instance = existing;
            return existing;
        }

        GameObject root = GameObject.Find("SkyPrisonRuntimeSystems");
        if (root == null)
            root = new GameObject("SkyPrisonRuntimeSystems");

        instance = root.AddComponent<SkyPrisonRuntimeBootCoordinator>();
        return instance;
    }

    // 进任何地图都必须走完整读条流程（黑屏→异步加载→BootCoordinator四阶段→按确认键
    // 揭幕），不能是可以被绕过的"形式"——正常玩家路径（WorldMapWindowController.
    // OnEnterChapter → SceneLoader.LoadScene）本来就会走这套，但在编辑器里直接对着
    // 地图场景点 Play 会跳过 SceneLoader，AI/状态 tick 等一大堆东西都不会正确初始化。
    // 这里检测："当前场景是不是地图场景(在 MapSceneRegistry 名单里)，但又不是通过
    // SceneLoader 正常进来的(SceneLoader.IsLoading==false)"——命中就立刻强制重新走
    // 一遍正常读条流程，自己这一轮的启动直接放弃（反正马上要重新加载场景，整个对象
    // 都会被销毁重建）。不在名单里的场景（主菜单、LoadingScene 等）不受影响。
    private bool _redirectedToProperLoad;

    private bool TryRedirectIfLoadingWasBypassed()
    {
        if (SceneLoader.IsLoading)
            return false; // 正常走 SceneLoader 进来的，不用管

        MapSceneRegistry registry = MapSceneRegistry.LoadOrNull();
        if (registry == null)
            return false; // 名单还没建过（比如从没在编辑器里跑过一次重建），保守起见不拦截

        Scene scene = SceneManager.GetActiveScene();
        if (!registry.ContainsScene(scene.name, scene.path))
            return false; // 不是地图场景（主菜单/LoadingScene等），不用管

        _redirectedToProperLoad = true;
        Debug.Log($"[SkyPrisonRuntimeBootCoordinator] 检测到地图场景「{scene.name}」绕过了 SceneLoader 直接进入，强制重新走完整读条流程。", this);
        SceneLoader.LoadScene(scene.name);
        return true;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("[SkyPrisonRuntimeBootCoordinator] Duplicate coordinator disabled.", this);
            enabled = false;
            return;
        }

        instance = this;

        if (TryRedirectIfLoadingWasBypassed())
        {
            enabled = false;
            return;
        }

        AddTimeline("Awake");

        if (closeStatusAutoTickDuringBoot)
            CloseAllStatusAutoTick("Coordinator Awake");

        if (runAuditAfterEveryPhase)
            RunOwnershipAudit("Awake");

        SceneLoader.OnGameplayResume += HandleSceneLoaderGameplayResume;
    }

    private void OnDestroy()
    {
        SceneLoader.OnGameplayResume -= HandleSceneLoaderGameplayResume;
    }

    private void Start()
    {
        if (_redirectedToProperLoad)
            return;

        if (runBootOnStart)
            StartBoot();
    }

    private void Update()
    {
        if (!keepAuditingAfterBoot || bootRunning || !Application.isPlaying)
            return;

        if (Time.realtimeSinceStartup < nextAuditRealtime)
            return;

        nextAuditRealtime = Time.realtimeSinceStartup + Mathf.Max(0.1f, auditIntervalSeconds);
        RunOwnershipAudit("Periodic");
    }

    [ContextMenu("Boot Coordinator/Start Boot")]
    public void StartBoot()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[SkyPrisonRuntimeBootCoordinator] Boot can only run in Play mode.", this);
            return;
        }

        StopAllCoroutines();
        StartCoroutine(BootRoutine());
    }

    [ContextMenu("Boot Coordinator/Run Ownership Audit Now")]
    public void RunOwnershipAuditNow()
    {
        RunOwnershipAudit("Manual");
    }

    private IEnumerator BootRoutine()
    {
        bootRunning = true;
        sceneFoundationReady = false;
        unitFoundationReady = false;
        occlusionFoundationReady = false;
        hudFoundationReady = false;
        gameplayTickOpen = false;
        presentationEffectsPhaseReached = false;
        presentationEffectsOpen = false;
        statusControllersOpened = 0;
        timelineLines.Clear();
        AddTimeline("BOOT START");

        if (closeStatusAutoTickDuringBoot)
            CloseAllStatusAutoTick("Boot start");

        yield return PhaseSceneFoundation();
        yield return PhaseUnitFoundation();
        yield return PhaseOcclusionFoundation();
        yield return PhaseHudFoundation();

        int stableFrames = Mathf.Max(0, stableFramesBeforeGameplay);
        for (int i = 0; i < stableFrames; i++)
            yield return null;

        OpenGameplayTick();
        if (!_isGameScene)
            OpenPresentationEffectsIfAllowed();

        bootRunning = false;
        AddTimeline("BOOT COMPLETE");

        if (runAuditAfterBoot)
            RunOwnershipAudit("After Boot Complete");
    }

    private IEnumerator PhaseSceneFoundation()
    {
        AddTimeline("Phase0 Scene Foundation: begin");

        mainCamera = Camera.main;
        if (mainCamera == null)
            mainCamera = FindObjectOfType<Camera>();

        EnsureRuntimeSystemsRoot();
        runtimeUIDriver = FindObjectOfType<SkyPrisonRuntimeUIDriver>();

        sceneFoundationReady = mainCamera != null;
        AddTimeline($"Phase0 Scene Foundation: mainCamera={(mainCamera != null ? mainCamera.name : "<null>")} uiDriver={(runtimeUIDriver != null ? runtimeUIDriver.name : "<null>")}");
        if (runAuditAfterEveryPhase)
            RunOwnershipAudit("After Phase0");
        yield return null;
    }

    private IEnumerator PhaseUnitFoundation()
    {
        AddTimeline("Phase1 Unit Foundation: wait player");

        float start = Time.realtimeSinceStartup;
        while (SkyPrisonPlayerAuthority.CurrentPlayerUnit == null && Time.realtimeSinceStartup - start < waitPlayerTimeout)
        {
            SkyPrisonPlayerAuthority authority = FindObjectOfType<SkyPrisonPlayerAuthority>();
            if (authority != null)
            {
                authority.RefreshRegisteredUnitsFromScene();
                if (authority.CurrentPlayer == null)
                    authority.ChooseInitialPlayer();
            }
            yield return null;
        }

        currentPlayer = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
        unitFoundationReady = currentPlayer != null;
        if (closeStatusAutoTickDuringBoot)
            CloseAllStatusAutoTick("Phase1 Unit Foundation");
        AddTimeline($"Phase1 Unit Foundation: player={(currentPlayer != null ? currentPlayer.name : "<null>")}");
        if (runAuditAfterEveryPhase)
            RunOwnershipAudit("After Phase1");
    }

    private IEnumerator PhaseOcclusionFoundation()
    {
        AddTimeline("Phase2 Occlusion Foundation: settle render/proxy systems");

        mainCamera = Camera.main != null ? Camera.main : mainCamera;
        occlusionFoundationReady = mainCamera != null;
        if (closeStatusAutoTickDuringBoot)
            CloseAllStatusAutoTick("Phase2 Occlusion Foundation");
        AddTimeline($"Phase2 Occlusion Foundation: ready={occlusionFoundationReady}");
        if (runAuditAfterEveryPhase)
            RunOwnershipAudit("After Phase2");
        yield break;
    }

    private IEnumerator PhaseHudFoundation()
    {
        AddTimeline("Phase3 HUD Foundation: begin");

        runtimeUIDriver = FindObjectOfType<SkyPrisonRuntimeUIDriver>();
        if (runtimeUIDriver == null)
        {
            Transform root = EnsureRuntimeSystemsRoot();
            GameObject driverObject = new GameObject(runtimeUIDriverName);
            driverObject.transform.SetParent(root, false);
            runtimeUIDriver = driverObject.AddComponent<SkyPrisonRuntimeUIDriver>();
            AddTimeline("Phase3 HUD Foundation: created RuntimeUIDriver");
        }

        float start = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - start < waitHudTimeout)
        {
            bool ready = runtimeUIDriver != null && runtimeUIDriver.EnsureHudReadyForBoot(false);
            if (ready)
                break;
            yield return null;
        }

        hudFoundationReady = runtimeUIDriver != null && runtimeUIDriver.IsHudReadyForBoot;
        if (closeStatusAutoTickDuringBoot)
            CloseAllStatusAutoTick("Phase3 HUD Foundation");
        AddTimeline($"Phase3 HUD Foundation: ready={hudFoundationReady} hud={(runtimeUIDriver != null && runtimeUIDriver.HudInstance != null ? runtimeUIDriver.HudInstance.name : "<null>")}");
        if (runAuditAfterEveryPhase)
            RunOwnershipAudit("After Phase3");
        yield return null;
    }

    private bool _isGameScene;

    /// <summary>
    /// Boot 阶段结束时调用：只通知 SceneLoader "系统已就绪"，
    /// 不打开 AI/状态 tick —— 真正打开 tick 要等玩家按 E、揭幕那一刻，
    /// 由 HandleSceneLoaderGameplayResume 负责，避免黑屏期间怪物已经在行动。
    /// </summary>
    private void OpenGameplayTick()
    {
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        _isGameScene = sceneName != "LoadingScene" && sceneName != "MainMenu";

        // SceneLoader.LoadRoutine 不管目标是不是游戏场景，一律会等 SignalReady() 才继续
        // 进度条（阶段3→4），这里以前只在游戏场景分支里调用它——导致返回 MainMenu 这类
        // 非游戏场景时 SignalReady 永远不会触发，读条卡在原地干等到 10 秒超时才勉强
        // 继续，表现为"读条面板一直盖在上面下不去"。两个分支都必须调用 SignalReady()，
        // 只是"是否要等玩家按 E 才打开 AI/状态 tick"这一步按场景类型区分。
        SceneLoader.SignalReady();

        if (_isGameScene)
        {
            AddTimeline($"Phase4 Boot Ready: SignalReady sent, waiting for reveal to open AI tick. scene={sceneName}");
        }
        else
        {
            AddTimeline($"Phase4 Gameplay Tick: OPEN immediately (non-game scene) scene={sceneName}");
            ActuallyOpenGameplayTick();
        }
    }

    /// <summary>由 SceneLoader.OnGameplayResume 触发（玩家按 E、揭幕开始的那一刻）。</summary>
    private void HandleSceneLoaderGameplayResume()
    {
        if (!_isGameScene) return;
        ActuallyOpenGameplayTick();
        OpenPresentationEffectsIfAllowed();
        AddTimeline("Reveal: Gameplay Tick OPEN (post player confirm)");
    }

    private void ActuallyOpenGameplayTick()
    {
        gameplayTickOpen = true;

        if (!openStatusTickAfterHudReady)
            return;

        // 复用黑屏阶段缓存的列表，避免在玩家按 E 揭幕的这一帧现查全场景造成卡顿。
        UnitStatusController[] controllers = _cachedStatusControllers ?? FindObjectsOfType<UnitStatusController>(true);
        statusControllersOpened = 0;
        for (int i = 0; i < controllers.Length; i++)
        {
            UnitStatusController controller = controllers[i];
            if (controller == null)
                continue;
            controller.SetAutoTickInUpdate(true, "RuntimeBootCoordinator Gameplay Tick Open");
            statusControllersOpened++;
        }

        AddTimeline($"Status Tick Opened: {statusControllersOpened}");
        if (runAuditAfterEveryPhase)
            RunOwnershipAudit("After Gameplay Tick Open");
    }

    private void OpenPresentationEffectsIfAllowed()
    {
        presentationEffectsPhaseReached = true;

        bool allowed = openPresentationEffectsAfterGameplayReady;
        presentationEffectsOpen = allowed;

        AddTimeline($"Phase5 Presentation Effects: {(allowed ? "OPEN" : "SKIPPED_RUNTIME_POSTPROCESS")}");

        if (runtimeUIDriver != null)
            runtimeUIDriver.SetPresentationEffectsEnabled(allowed, "BootCoordinator Phase5");

        if (runAuditAfterEveryPhase)
            RunOwnershipAudit("After Phase5");
    }

    // Boot 阶段（黑屏期间，反正 timeScale=0）顺手缓存一份，避免玩家按 E 揭幕那一刻
    // 才现查 FindObjectsOfType——大场景这一下能有明显卡顿，导致"按 E 后要等一会才进地图"。
    private UnitStatusController[] _cachedStatusControllers;

    private void CloseAllStatusAutoTick(string reason)
    {
        UnitStatusController[] controllers = FindObjectsOfType<UnitStatusController>(true);
        _cachedStatusControllers = controllers;
        statusControllersLocked = 0;
        for (int i = 0; i < controllers.Length; i++)
        {
            UnitStatusController controller = controllers[i];
            if (controller == null)
                continue;
            controller.SetAutoTickInUpdate(false, reason);
            statusControllersLocked++;
        }
        AddTimeline($"Status AutoTick Closed: {statusControllersLocked} | {reason}");
    }

    private Transform EnsureRuntimeSystemsRoot()
    {
        GameObject root = GameObject.Find(runtimeSystemsRootName);
        if (root == null)
            root = gameObject;
        if (root.name != runtimeSystemsRootName)
            root.name = runtimeSystemsRootName;
        return root.transform;
    }

    private void RunOwnershipAudit(string reason)
    {
        Camera[] cameras = FindObjectsOfType<Camera>(true);
        auditActiveCameras = 0;
        auditMainCameraCandidates = 0;
        auditTargetTextureCameras = 0;
        auditHudInternalCameras = 0;

        List<string> cameraLines = new List<string>(12);
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera cam = cameras[i];
            if (cam == null)
                continue;

            if (cam.gameObject.activeInHierarchy && cam.enabled)
                auditActiveCameras++;

            bool mainCandidate = cam.CompareTag("MainCamera") || cam.name == "Main Camera" || cam == Camera.main;
            if (mainCandidate)
                auditMainCameraCandidates++;

            bool hasTargetTexture = cam.targetTexture != null;
            if (hasTargetTexture)
                auditTargetTextureCameras++;

            bool hudInternal = HasComponentTypeName(cam.gameObject, "SkyPrisonHUDInternalRenderCameraTag") || cam.name.StartsWith("__SkyPrisonHUDModuleRTCamera", StringComparison.Ordinal);
            if (hudInternal)
                auditHudInternalCameras++;

            if (cameraLines.Count < 8)
            {
                cameraLines.Add($"- {cam.name} active={cam.gameObject.activeInHierarchy && cam.enabled} main={mainCandidate} rt={(cam.targetTexture != null ? cam.targetTexture.name : "<none>")} hudInternal={hudInternal} layer={LayerMask.LayerToName(cam.gameObject.layer)}");
            }
        }

        MonoBehaviour[] behaviours = FindObjectsOfType<MonoBehaviour>(true);
        auditHudPostProcessRenderers = 0;
        auditDebugAppliers = 0;
        auditDebugApplierTickDrivers = 0;
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour mb = behaviours[i];
            if (mb == null)
                continue;

            string typeName = mb.GetType().Name;
            if (typeName.StartsWith("SkyPrisonHUDModulePostProcessRenderer", StringComparison.Ordinal))
                auditHudPostProcessRenderers++;

            if (typeName == "UnitStatusDebugApplier")
            {
                auditDebugAppliers++;
                if (ReadBoolMember(mb, "driveStatusTick", false))
                    auditDebugApplierTickDrivers++;
            }
        }

        GameObject[] allObjects = FindObjectsOfType<GameObject>(true);
        auditRuntimeHudInstances = 0;
        for (int i = 0; i < allObjects.Length; i++)
        {
            GameObject go = allObjects[i];
            if (go != null && go.name.StartsWith("SkyPrisonBattleHUD_Runtime", StringComparison.Ordinal))
                auditRuntimeHudInstances++;
        }

        UnitStatusController[] controllers = FindObjectsOfType<UnitStatusController>(true);
        auditStatusControllers = controllers.Length;
        auditStatusAutoTickEnabled = 0;
        for (int i = 0; i < controllers.Length; i++)
        {
            UnitStatusController controller = controllers[i];
            if (controller != null && controller.AutoTickInUpdate)
                auditStatusAutoTickEnabled++;
        }

        List<string> warnings = new List<string>(8);
        if (warnOnMultipleMainCameras && auditMainCameraCandidates != 1)
            warnings.Add($"WARN main-camera-candidates={auditMainCameraCandidates}");
        if (warnOnHudInternalCamera && auditHudInternalCameras > 0 && !presentationEffectsPhaseReached)
            warnings.Add($"WARN HUD internal cameras exist before presentation phase: {auditHudInternalCameras}");
        if (warnOnHudPostProcessRuntime && auditHudPostProcessRenderers > 0 && !presentationEffectsPhaseReached)
            warnings.Add($"WARN HUD post-process renderers exist before presentation phase: {auditHudPostProcessRenderers}");
        if (warnOnStatusTickBeforeGameplayOpen && !gameplayTickOpen && auditStatusAutoTickEnabled > 0)
            warnings.Add($"WARN status auto tick open before gameplay: {auditStatusAutoTickEnabled}/{auditStatusControllers}");
        if (auditRuntimeHudInstances > 1)
            warnings.Add($"WARN duplicate runtime HUD instances: {auditRuntimeHudInstances}");

        ownershipAuditReport =
            $"[Boot Audit] reason={reason} f={Time.frameCount} t={Time.realtimeSinceStartup:0.000}\n" +
            $"MainCamera={(Camera.main != null ? Camera.main.name : "<null>")} sceneMain={(mainCamera != null ? mainCamera.name : "<null>")}\n" +
            $"Cameras active={auditActiveCameras} mainCandidates={auditMainCameraCandidates} targetRT={auditTargetTextureCameras} hudInternal={auditHudInternalCameras}\n" +
            $"HUD instances={auditRuntimeHudInstances} hudPostProcessRenderers={auditHudPostProcessRenderers} presentationPhaseReached={presentationEffectsPhaseReached} presentationAllowed={presentationEffectsOpen}\n" +
            $"Status controllers={auditStatusControllers} autoTickEnabled={auditStatusAutoTickEnabled} gameplayOpen={gameplayTickOpen}\n" +
            $"DebugAppliers={auditDebugAppliers} driveTick={auditDebugApplierTickDrivers}\n" +
            (warnings.Count > 0 ? string.Join("\n", warnings) + "\n" : "OK no ownership warning\n") +
            string.Join("\n", cameraLines);

        if (debugLogs && warnings.Count > 0)
            Debug.LogWarning(ownershipAuditReport, this);
        else if (debugLogs)
            Debug.Log(ownershipAuditReport, this);
    }

    private static bool HasComponentTypeName(GameObject go, string typeName)
    {
        if (go == null || string.IsNullOrEmpty(typeName))
            return false;

        Component[] components = go.GetComponents<Component>();
        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i];
            if (component != null && component.GetType().Name == typeName)
                return true;
        }
        return false;
    }

    private static bool ReadBoolMember(object target, string name, bool fallback)
    {
        if (target == null || string.IsNullOrEmpty(name))
            return fallback;

        Type type = target.GetType();
        FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null && field.FieldType == typeof(bool))
            return (bool)field.GetValue(target);

        PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property != null && property.PropertyType == typeof(bool) && property.GetIndexParameters().Length == 0)
            return (bool)property.GetValue(target, null);

        return fallback;
    }

    private void AddTimeline(string message)
    {
        string line = $"[Boot] f={Time.frameCount} t={Time.realtimeSinceStartup:0.000} {message}";
        timelineLines.Add(line);
        while (timelineLines.Count > 24)
            timelineLines.RemoveAt(0);
        bootTimeline = string.Join("\n", timelineLines);
        if (debugLogs)
            Debug.Log(line, this);
    }
}
