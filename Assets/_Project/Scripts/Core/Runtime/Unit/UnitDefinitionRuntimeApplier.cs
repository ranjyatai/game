using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Spine;
using Spine.Unity;

// V30 - 2026-06-10: Visual-root only billboard, root billboard cleanup, and one-shot visual-bounds capsule calibration.
// V28 - 2026-06-10: CameraFacingBillboard is visual-root only; never auto-add billboard on unit physics root.
// Keeps the four OutlineProxy_*_Occluded nodes as the current minimum unit occlusion baseline.
// Adds missing visual-only CameraFacingBillboard on VisualRoot and SpinePartOcclusionSampler on character units.
// Adds runtime auto-ensure for TerrainGroundMotorV5 on character unit root, binding it to CollisionRoot body collider.


[DefaultExecutionOrder(9200)]
public class UnitDefinitionRuntimeApplier : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UnitDefinitionRuntimeBinder runtimeBinder;
    [SerializeField] private UnitOutlineState unitOutlineState;
    [SerializeField] private UnitOccludedOutlineProxyGate occludedOutlineProxyGate;
    [SerializeField] private UnitOcclusionMaterialReceiver unitOcclusionMaterialReceiver;
    [SerializeField] private Rigidbody targetRigidbody;
    [SerializeField] private CapsuleCollider collisionCapsule;
    [SerializeField] private Transform unitPhysicsProbeRoot;
    [SerializeField] private Collider unitPhysicsProbeCollider;
    [SerializeField] private MonoBehaviour unitPhysicsProbeComponent;
    [SerializeField] private Transform shadowProjector;
    [SerializeField] private UnitMovementController movementController;
    [SerializeField] private TerrainGroundMotorV5 terrainGroundMotor;
    [SerializeField] private UnitLoadRuntime loadRuntime;
    [SerializeField] private UnitActionController actionController;
    [SerializeField] private SkyPrisonPlayerInputRouter playerInputRouter;
    [SerializeField] private UnitFootstepAudioEmitter footstepAudioEmitter;
    [SerializeField] private SpineFootstepEventRelay spineFootstepEventRelay;
    [SerializeField] private Transform unitAudioRoot;
    [SerializeField] private Transform footstepAudioPoint;
    [SerializeField] private AudioSource footstepAudioSource;
    [SerializeField] private UnitDeathController deathController;
    [SerializeField] private UnitDeathDropController deathDropController;
    [SerializeField] private UnitOverheadUIView overheadView;
    [SerializeField] private UnitOverheadHealthBridge overheadHealthBridge;
    [SerializeField] private UnitHealthController healthController;
    [SerializeField] private SkyPrisonFogUnitVisibilityController fogUnitVisibilityController;
    [SerializeField] private AIBehaviorPackageRuntimeController aiBehaviorRuntimeController;
    [SerializeField] private UnitPerceptionRuntime perceptionRuntime;

    [Header("Options")]
    [SerializeField] private bool autoFindOnAwake = true;
    [SerializeField] private bool applyOnAwake = true;
    [SerializeField] private bool applyOnEnable = true;
    [SerializeField] private bool enforceEveryLateUpdate = false;

    [Header("Movement")]
    [SerializeField] private bool autoEnsureMovementControllerForCharacterUnits = true;
    [SerializeField] private bool autoEnsureTerrainGroundMotorForCharacterUnits = true;
    [SerializeField] private bool configureTerrainGroundMotorBodyCollider = true;

    [Header("Formal Player Control / Load")]
    [SerializeField] private bool autoEnsureLoadRuntimeForCharacterUnits = true;
    [SerializeField] private bool autoEnsureActionControllerForCharacterUnits = true;
    [SerializeField] private bool autoEnsurePlayerInputRouterForPlayerUnits = true;

    [Header("Unit Audio Auto Setup")]
    [SerializeField] private bool autoEnsureUnitAudioNodesForCharacterUnits = true;
    [SerializeField] private bool autoEnsureFootstepAudioEmitterForCharacterUnits = true;
    [SerializeField] private bool autoEnsureSpineFootstepEventRelayForCharacterUnits = true;
    [SerializeField] private string unitAudioRootName = "AudioRoot";
    [SerializeField] private string footstepAudioPointName = "FootstepAudioPoint";

    [Header("Physics Auto Setup")]
    [SerializeField] private bool autoEnsureRigidbodyForCharacterUnits = true;
    [SerializeField] private bool autoEnsureCollisionCapsuleForCharacterUnits = true;
    [SerializeField] private bool autoEnsureUnitPhysicsProbeForCharacterUnits = true;
    [SerializeField] private string autoCollisionRootName = "CollisionRoot";
    [SerializeField] private string defaultUnitPhysicsProbeRootName = "UnitPhysicsProbe";
    [SerializeField] private bool keepAutoCapsuleBottomOnUnitOrigin = true;
    [SerializeField] private float defaultCapsuleHeight = 1.8f;
    [SerializeField] private float defaultCapsuleRadius = 0.35f;

    [Header("Auto Capsule Calibration / Foot Root Convention")]
    [SerializeField] private bool autoCalibrateCapsuleFromFinalVisualBounds = true;
    [SerializeField] private bool runCapsuleCalibrationEvenWhenDefinitionOverridesShape = true;
    [SerializeField] private bool calibrateCapsuleImmediatelyOnApply = true;
    [SerializeField] private bool calibrateCapsuleAfterVisualSettles = true;
    [SerializeField, Min(0)] private int capsuleVisualSettleFrames = 1;
    [SerializeField] private bool capsuleRootIsFootPoint = true;
    [SerializeField] private bool keepCurrentCapsuleRadiusOnAutoCalibration = true;
    [SerializeField] private bool autoCalibrateCapsuleRadiusFromVisualWidth = false;
    [SerializeField, Range(0.05f, 1.5f)] private float capsuleRadiusFromVisualWidthFactor = 0.22f;
    [SerializeField, Range(0.5f, 1.5f)] private float capsuleHeightScaleFromVisualBounds = 1f;
    [SerializeField] private float capsuleTopPadding = 0f;
    [SerializeField] private float capsuleBottomPadding = 0f;
    [SerializeField] private float capsuleMinimumHeight = 0.35f;
    [SerializeField] private float capsuleMinimumRadius = 0.05f;
    [SerializeField] private bool resetCapsuleTransformPitchRollOnCalibration = true;
    [SerializeField] private bool logCapsuleAutoCalibration = false;

    private Coroutine pendingCapsuleCalibrationRoutine;

    [Header("Unit Physics Probe / Judgement")]
    [SerializeField] private Vector3 defaultUnitPhysicsProbeCenter = new Vector3(0f, 0.65f, 0f);
    [SerializeField] private float defaultUnitPhysicsProbeHeight = 1.15f;
    [SerializeField] private float defaultUnitPhysicsProbeRadius = 0.36f;

    [Header("Combat Runtime")]
    [SerializeField] private UnitActionModuleRuntime combatModuleRuntime;
    [SerializeField] private UnitCombatHurtbox combatHurtbox;
    [SerializeField] private bool autoEnsureCombatModuleRuntimeForCharacterUnits = true;
    [Tooltip("自动创建 Hitbox 时挂载的父节点名称，对应层级里的 HitRoot。")]
    [SerializeField] private string hitRootName = "HitRoot";
    [Tooltip("自动创建 Hurtbox 时挂载的父节点名称，对应层级里的 CollisionRoot。")]
    [SerializeField] private string hurtboxRootName = "CollisionRoot";
    [Tooltip("自动扫描 Spine DefaultSkin 中的 BoundingBoxAttachment，建立追随 Collider 作为 Hitbox。")]
    [SerializeField] private bool autoEnsureSpineBoundingBoxHitbox = true;

    [Header("AI Behavior")]
    [SerializeField] private bool autoEnsureAIBehaviorRuntimeForUnitsWithPackage = true;
    [SerializeField] private bool autoApplyAIBehaviorPackage = true;

    [Header("Perception Runtime")]
    [SerializeField] private bool autoEnsurePerceptionRuntimeForCharacterUnits = true;
    [SerializeField] private bool enablePerceptionRuntimeForCharacterUnits = true;
    [SerializeField] private bool defaultPerceptionSensorsOnlyForAIUnits = true;

    [Header("Fog Of War")]
    [SerializeField] private bool autoEnsureFogVisibilityController = true;
    [SerializeField] private bool enableFogVisibilityForAllCharacterUnits = true;
    [SerializeField] private bool disableFogVisibilityForNonCharacterUnits = true;


    [Header("Visual Runtime Auto Setup")]
    [Tooltip("Character units should keep this root component when their visual needs to face the gameplay camera.")]
    [SerializeField] private bool autoEnsureCameraFacingBillboardForCharacterUnits = true;

    [Tooltip("Character units should keep this root component so Spine body parts can participate in the sensitive occlusion sampling route.")]
    [SerializeField] private bool autoEnsureSpinePartOcclusionSamplerForCharacterUnits = true;

    [Tooltip("Type name used for reflection based auto setup. Kept as text so this applier does not create hard compile coupling.")]
    [SerializeField] private string cameraFacingBillboardTypeName = "CameraFacingBillboard";

    [Tooltip("Type name used for reflection based auto setup. Kept as text so this applier does not create hard compile coupling.")]
    [SerializeField] private string spinePartOcclusionSamplerTypeName = "SpinePartOcclusionSampler";

    [Header("Occlusion Material Auto Setup")]
    [SerializeField] private bool autoEnsureOcclusionMaterialReceiverForCharacterUnits = true;
    [SerializeField] private bool autoBindSpineOcclusionMaterials = true;
    [SerializeField] private bool includeOnlyRealVisualRenderersForOcclusion = true;
    [SerializeField] private string visualRootNameForOcclusion = "VisualRoot";
    [SerializeField] private string spineObjectNameHintForOcclusion = "Spine GameObject";
    [SerializeField] private string occlusionCompositeShaderNameKeyword = "SpineOcclusionComposite";

    [Header("Occlusion Material Build Fallback")]
    [Tooltip("Build 中不能用 AssetDatabase 自动搜索材质。这里可以显式拖入 M_Player_OcclusionComposite。为空时会用 Shader.Find 创建运行时 Composite 材质。")]
    [SerializeField] private Material fallbackSpineOcclusionCompositeMaterial;

    [Tooltip("当找不到预设 Composite 材质时，运行时用 Spine/SpineOcclusionComposite Shader 复制当前 Spine 主材质生成一个 Composite 材质。")]
    [SerializeField] private bool createRuntimeOcclusionCompositeMaterial = true;

    [Tooltip("运行时生成 Composite 材质时使用的 Shader 名称。")]
    [SerializeField] private string runtimeOcclusionCompositeShaderName = "Spine/SpineOcclusionComposite";

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    private UnitDefinition lastAppliedDefinition;
    private bool hasAppliedOnce = false;

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

        if (applyOnAwake)
            ApplyDefinition(forceApply: true);
    }

    private void OnEnable()
    {
        if (applyOnEnable)
            ApplyDefinition(forceApply: true);
    }

    private void LateUpdate()
    {
        if (enforceEveryLateUpdate)
            ApplyDefinition(forceApply: false);
    }

    [ContextMenu("Auto Setup")]
    public void AutoSetup()
    {
        if (runtimeBinder == null)
            runtimeBinder = GetComponent<UnitDefinitionRuntimeBinder>();

        if (unitOutlineState == null)
            unitOutlineState = GetComponent<UnitOutlineState>();

        if (occludedOutlineProxyGate == null)
            occludedOutlineProxyGate = GetComponent<UnitOccludedOutlineProxyGate>();

        if (unitOcclusionMaterialReceiver == null)
            unitOcclusionMaterialReceiver = GetComponent<UnitOcclusionMaterialReceiver>();

        if (targetRigidbody == null)
            targetRigidbody = GetComponent<Rigidbody>();

        if (collisionCapsule == null)
            collisionCapsule = FindCollisionCapsule();

        if (unitPhysicsProbeRoot == null)
            unitPhysicsProbeRoot = FindUnitPhysicsProbeRoot(null);

        if (unitPhysicsProbeCollider == null && unitPhysicsProbeRoot != null)
            unitPhysicsProbeCollider = unitPhysicsProbeRoot.GetComponent<Collider>();

        if (unitPhysicsProbeComponent == null && unitPhysicsProbeRoot != null)
            unitPhysicsProbeComponent = FindUnitPhysicsProbeComponent(unitPhysicsProbeRoot.gameObject);

        if (shadowProjector == null)
            shadowProjector = FindShadowProjector();

        if (movementController == null)
            movementController = GetComponent<UnitMovementController>();

        if (terrainGroundMotor == null)
            terrainGroundMotor = GetComponent<TerrainGroundMotorV5>();

        if (loadRuntime == null)
            loadRuntime = GetComponent<UnitLoadRuntime>();

        if (actionController == null)
            actionController = GetComponent<UnitActionController>();

        if (playerInputRouter == null)
            playerInputRouter = GetComponent<SkyPrisonPlayerInputRouter>();

        if (footstepAudioEmitter == null)
            footstepAudioEmitter = GetComponent<UnitFootstepAudioEmitter>();

        if (spineFootstepEventRelay == null)
            spineFootstepEventRelay = GetComponent<SpineFootstepEventRelay>();

        if (unitAudioRoot == null)
            unitAudioRoot = FindDirectOrDeepChild(unitAudioRootName);

        if (footstepAudioPoint == null)
        {
            Transform searchRoot = unitAudioRoot != null ? unitAudioRoot : transform;
            footstepAudioPoint = FindDirectChild(searchRoot, footstepAudioPointName);
            if (footstepAudioPoint == null)
                footstepAudioPoint = FindDirectOrDeepChild(footstepAudioPointName);
        }

        if (footstepAudioSource == null && footstepAudioPoint != null)
            footstepAudioSource = footstepAudioPoint.GetComponent<AudioSource>();

        if (deathController == null)
            deathController = GetComponent<UnitDeathController>();

        if (overheadView == null)
            overheadView = GetComponent<UnitOverheadUIView>() ?? GetComponentInChildren<UnitOverheadUIView>(true);

        if (overheadHealthBridge == null)
            overheadHealthBridge = GetComponent<UnitOverheadHealthBridge>() ?? GetComponentInChildren<UnitOverheadHealthBridge>(true);

        if (healthController == null)
            healthController = GetComponent<UnitHealthController>() ?? GetComponentInChildren<UnitHealthController>(true);

        if (fogUnitVisibilityController == null)
            fogUnitVisibilityController = GetComponent<SkyPrisonFogUnitVisibilityController>();

        if (aiBehaviorRuntimeController == null)
            aiBehaviorRuntimeController = GetComponent<AIBehaviorPackageRuntimeController>();

        if (aiBehaviorRuntimeController == null)
            aiBehaviorRuntimeController = GetComponentInParent<AIBehaviorPackageRuntimeController>();

        if (aiBehaviorRuntimeController == null)
            aiBehaviorRuntimeController = GetComponentInChildren<AIBehaviorPackageRuntimeController>(true);

        if (perceptionRuntime == null)
            perceptionRuntime = GetComponent<UnitPerceptionRuntime>();

        if (combatModuleRuntime == null)
            combatModuleRuntime = GetComponent<UnitActionModuleRuntime>();

        if (combatHurtbox == null)
            combatHurtbox = GetComponentInChildren<UnitCombatHurtbox>(true);
    }

    [ContextMenu("Apply Definition")]
    public void ApplyDefinition()
    {
        ApplyDefinition(true);
    }

    public void ApplyDefinition(bool forceApply)
    {
        if (runtimeBinder == null)
            runtimeBinder = GetComponent<UnitDefinitionRuntimeBinder>();

        if (runtimeBinder == null || runtimeBinder.UnitDefinitionAsset == null)
        {
            if (debugLogs)
                Debug.LogWarning($"[UnitDefinitionRuntimeApplier] {name}: RuntimeBinder or UnitDefinitionAsset missing.", this);
            return;
        }

        UnitDefinition ud = runtimeBinder.UnitDefinitionAsset;
        if (ud == null)
            return;

        if (!forceApply && hasAppliedOnce && ReferenceEquals(lastAppliedDefinition, ud))
            return;

        EnsurePhysicsComponentsForDefinition(ud);
        EnsureUnitCollisionJudgementForDefinition(ud);
        EnsureMovementControllerForDefinition(ud);
        EnsureTerrainGroundMotorForDefinition(ud);
        EnsureLoadRuntimeForDefinition(ud);
        EnsureFormalControlForDefinition(ud);
        EnsureCombatRuntimeForDefinition(ud);
        EnsureUnitAudioForDefinition(ud);
        EnsureAIBehaviorRuntimeForDefinition(ud);
        EnsurePerceptionRuntimeForDefinition(ud);
        EnsureVisualRuntimeUtilityComponentsForDefinition(ud);

        // V26: Do not drive old UnitOutlineState / old normal OutlineProxy route here.
        // Occluded proxy gate is still required by current unit occlusion baseline.
        ApplyToOcclusionReceiver(ud);
        ApplyToOccludedProxyGate();
        RefreshSpine43RendererBindingsFromRuntimeBinder();
        EnsureFootstepRelayBindingAfterVisualRefresh(ud);
        ApplyToPhysics(ud);
        ApplyToCollisionShape(ud);
        ScheduleOrRunCapsuleAutoCalibration(ud);
        ApplyToShadowProjector(ud);
        ApplyToMovement(ud);
        ApplyToLoadRuntime(ud);
        ApplyToDeath(ud);
        ApplyToOverheadUI(ud);
        ApplyToFogVisibility(ud);
        ApplyToPerceptionRuntime(ud);

        // 非玩家角色自动挂 LOD 睡眠控制器
        bool isNonPlayerCharacter = ud.defineType == UnitDefineType.Character
                                 && ud.characterIdentity != CharacterIdentity.Player;
        if (isNonPlayerCharacter && GetComponent<UnitLODSleepController>() == null)
            gameObject.AddComponent<UnitLODSleepController>();

        lastAppliedDefinition = ud;
        hasAppliedOnce = true;

        if (debugLogs)
        {
            Debug.Log(
                $"[UnitDefinitionRuntimeApplier] {name}: define={ud.defineType}, moveType={ud.movementType}, jumpHeight={ud.idealJumpHeight}, kinematic={ud.useKinematicBody}, allowPush={ud.allowPhysicalPush}",
                this
            );
        }
    }


    [ContextMenu("Ensure Unit Physics Components")]
    public void EnsureUnitPhysicsComponentsFromCurrentDefinition()
    {
        if (runtimeBinder == null)
            runtimeBinder = GetComponent<UnitDefinitionRuntimeBinder>();

        UnitDefinition ud = runtimeBinder != null ? runtimeBinder.UnitDefinitionAsset : null;
        EnsurePhysicsComponentsForDefinition(ud);
        EnsureUnitCollisionJudgementForDefinition(ud);
        ApplyToPhysics(ud);
        ApplyToCollisionShape(ud);
        ScheduleOrRunCapsuleAutoCalibration(ud);
    }


    private void RefreshSpine43RendererBindingsFromRuntimeBinder()
    {
        if (runtimeBinder == null)
            runtimeBinder = GetComponent<UnitDefinitionRuntimeBinder>();

        if (runtimeBinder == null)
            return;

        MethodInfo method = runtimeBinder.GetType().GetMethod(
            "RefreshSpine43RendererBindingsIfPossible",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (method == null)
            return;

        try
        {
            method.Invoke(runtimeBinder, null);
        }
        catch (Exception ex)
        {
            if (debugLogs)
                Debug.LogWarning($"[UnitDefinitionRuntimeApplier] {name}: Spine 4.3 binding refresh failed: {ex.Message}", this);
        }
    }

    private void EnsurePhysicsComponentsForDefinition(UnitDefinition ud)
    {
        if (ud == null)
            return;

        bool isCharacterUnit = ud.defineType == UnitDefineType.Character;
        if (!isCharacterUnit)
            return;

        if (targetRigidbody == null)
            targetRigidbody = GetComponent<Rigidbody>();

        if (targetRigidbody == null && autoEnsureRigidbodyForCharacterUnits)
        {
            targetRigidbody = gameObject.AddComponent<Rigidbody>();

            if (debugLogs)
                Debug.Log($"[UnitDefinitionRuntimeApplier] {name}: Auto added Rigidbody.", this);
        }

        if (collisionCapsule == null)
            collisionCapsule = FindCollisionCapsule();

        if (collisionCapsule == null && autoEnsureCollisionCapsuleForCharacterUnits)
        {
            Transform collisionRoot = FindOrCreateCollisionRoot();
            collisionCapsule = collisionRoot.GetComponent<CapsuleCollider>();

            if (collisionCapsule == null)
                collisionCapsule = collisionRoot.gameObject.AddComponent<CapsuleCollider>();

            collisionCapsule.direction = 1;
            collisionCapsule.isTrigger = false;

            if (!ud.overrideCollisionShape)
            {
                ApplyDefaultCapsuleShape();
            }

            if (debugLogs)
                Debug.Log($"[UnitDefinitionRuntimeApplier] {name}: Auto added CapsuleCollider under {collisionRoot.name}.", this);
        }

        if (movementController == null)
            movementController = GetComponent<UnitMovementController>();

        if (movementController != null && collisionCapsule != null)
            movementController.AssignMovementCapsule(collisionCapsule);
    }

    private Transform FindOrCreateCollisionRoot()
    {
        string rootName = string.IsNullOrWhiteSpace(autoCollisionRootName) ? "CollisionRoot" : autoCollisionRootName;

        Transform collisionRoot = transform.Find(rootName);
        if (collisionRoot != null)
            return collisionRoot;

        GameObject root = new GameObject(rootName);
        UndoSafeSetParent(root.transform, transform);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;
        return root.transform;
    }

    private static void UndoSafeSetParent(Transform child, Transform parent)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
            UnityEditor.Undo.SetTransformParent(child, parent, "Create Unit Collision Root");
        else
            child.SetParent(parent, false);
#else
        child.SetParent(parent, false);
#endif
    }

    private void EnsureUnitCollisionJudgementForDefinition(UnitDefinition ud)
    {
        if (ud == null)
            return;

        bool isCharacterUnit = ud.defineType == UnitDefineType.Character;
        if (!isCharacterUnit)
        {
            SetUnitPhysicsProbeActive(false);
            return;
        }

        if (collisionCapsule == null)
            collisionCapsule = FindCollisionCapsule();

        if (collisionCapsule != null && ud.enableUnitBodyCollision)
            ApplyLayerByName(collisionCapsule.gameObject, ud.unitBodyLayerName);

        bool shouldHaveProbe = autoEnsureUnitPhysicsProbeForCharacterUnits && ud.enableUnitPhysicsProbe;
        if (!shouldHaveProbe)
        {
            SetUnitPhysicsProbeActive(false);
            return;
        }

        Transform probeRoot = FindOrCreateUnitPhysicsProbeRoot(ud);
        if (probeRoot == null)
            return;

        unitPhysicsProbeRoot = probeRoot;
        ResetUnitPhysicsProbeRootTransform(unitPhysicsProbeRoot);
        unitPhysicsProbeRoot.gameObject.SetActive(true);
        ApplyLayerByName(unitPhysicsProbeRoot.gameObject, ud.unitPhysicsProbeLayerName);

        CapsuleCollider probeCapsule = unitPhysicsProbeRoot.GetComponent<CapsuleCollider>();
        if (probeCapsule == null)
            probeCapsule = unitPhysicsProbeRoot.gameObject.AddComponent<CapsuleCollider>();

        probeCapsule.enabled = true;
        probeCapsule.isTrigger = true;
        probeCapsule.direction = 1;
        probeCapsule.center = ud.unitPhysicsProbeLocalCenter;
        probeCapsule.radius = Mathf.Max(0.01f, ud.unitPhysicsProbeRadius);
        probeCapsule.height = Mathf.Max(Mathf.Max(0.01f, ud.unitPhysicsProbeHeight), probeCapsule.radius * 2f);
        unitPhysicsProbeCollider = probeCapsule;

        unitPhysicsProbeComponent = EnsureUnitPhysicsProbeComponent(unitPhysicsProbeRoot.gameObject);

        if (debugLogs)
        {
            Debug.Log(
                $"[UnitDefinitionRuntimeApplier] {name}: ensured UnitPhysicsProbe, layer={unitPhysicsProbeRoot.gameObject.layer}, radius={probeCapsule.radius}, height={probeCapsule.height}.",
                this
            );
        }
    }

    private Transform FindOrCreateUnitPhysicsProbeRoot(UnitDefinition ud)
    {
        string rootName = null;
        if (ud != null && !string.IsNullOrWhiteSpace(ud.unitPhysicsProbeRootName))
            rootName = ud.unitPhysicsProbeRootName;

        if (string.IsNullOrWhiteSpace(rootName))
            rootName = string.IsNullOrWhiteSpace(defaultUnitPhysicsProbeRootName) ? "UnitPhysicsProbe" : defaultUnitPhysicsProbeRootName;

        Transform probeRoot = transform.Find(rootName);
        if (probeRoot != null)
        {
            ResetUnitPhysicsProbeRootTransform(probeRoot);
            return probeRoot;
        }

        GameObject go = new GameObject(rootName);
        UndoSafeSetParent(go.transform, transform);
        ResetUnitPhysicsProbeRootTransform(go.transform);
        return go.transform;
    }

    private static void ResetUnitPhysicsProbeRootTransform(Transform probeRoot)
    {
        if (probeRoot == null)
            return;

        // UnitPhysicsProbe 是“包围角色中心”的触发器根节点，不能继承旧的前方/右侧偏移。
        // 真正的高度和半径只写在 CapsuleCollider.center / radius / height 上。
        probeRoot.localPosition = Vector3.zero;
        probeRoot.localRotation = Quaternion.identity;
        probeRoot.localScale = Vector3.one;
    }

    private Transform FindUnitPhysicsProbeRoot(UnitDefinition ud)
    {
        string rootName = null;
        if (ud != null && !string.IsNullOrWhiteSpace(ud.unitPhysicsProbeRootName))
            rootName = ud.unitPhysicsProbeRootName;

        if (string.IsNullOrWhiteSpace(rootName))
            rootName = string.IsNullOrWhiteSpace(defaultUnitPhysicsProbeRootName) ? "UnitPhysicsProbe" : defaultUnitPhysicsProbeRootName;

        Transform direct = transform.Find(rootName);
        if (direct != null)
            return direct;

        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t != null && t.name == rootName)
                return t;
        }

        return null;
    }

    private void SetUnitPhysicsProbeActive(bool active)
    {
        if (unitPhysicsProbeRoot == null)
            unitPhysicsProbeRoot = FindUnitPhysicsProbeRoot(null);

        if (unitPhysicsProbeRoot != null)
            unitPhysicsProbeRoot.gameObject.SetActive(active);
    }

    private MonoBehaviour EnsureUnitPhysicsProbeComponent(GameObject target)
    {
        if (target == null)
            return null;

        MonoBehaviour existing = FindUnitPhysicsProbeComponent(target);
        if (existing != null)
            return existing;

        Type probeType = FindTypeByName("SkyPrisonUnitPhysicsProbe");
        if (probeType == null || !typeof(MonoBehaviour).IsAssignableFrom(probeType))
        {
            if (debugLogs)
                Debug.LogWarning("[UnitDefinitionRuntimeApplier] SkyPrisonUnitPhysicsProbe type was not found. Probe collider was still created.", this);
            return null;
        }

        Component component = target.AddComponent(probeType);
        return component as MonoBehaviour;
    }

    private MonoBehaviour FindUnitPhysicsProbeComponent(GameObject target)
    {
        if (target == null)
            return null;

        Type probeType = FindTypeByName("SkyPrisonUnitPhysicsProbe");
        if (probeType == null)
            return null;

        Component component = target.GetComponent(probeType);
        return component as MonoBehaviour;
    }

    private static Type FindTypeByName(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return null;

        Type direct = Type.GetType(typeName);
        if (direct != null)
            return direct;

        AppDomain domain = AppDomain.CurrentDomain;
        var assemblies = domain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            Type type = assemblies[i].GetType(typeName);
            if (type != null)
                return type;
        }

        return null;
    }

    private static bool ApplyLayerByName(GameObject go, string layerName)
    {
        if (go == null || string.IsNullOrWhiteSpace(layerName))
            return false;

        int layer = LayerMask.NameToLayer(layerName);
        if (layer < 0)
            return false;

        go.layer = layer;
        return true;
    }

    private void EnsureMovementControllerForDefinition(UnitDefinition ud)
    {
        if (ud == null)
            return;

        if (movementController == null)
            movementController = GetComponent<UnitMovementController>();

        bool shouldHaveMovementController =
            autoEnsureMovementControllerForCharacterUnits &&
            ud.defineType == UnitDefineType.Character;

        if (movementController == null && shouldHaveMovementController)
        {
            movementController = gameObject.AddComponent<UnitMovementController>();

            if (debugLogs)
                Debug.Log($"[UnitDefinitionRuntimeApplier] {name}: Auto added UnitMovementController.", this);
        }

        if (movementController == null)
            return;

        if (targetRigidbody == null)
            targetRigidbody = GetComponent<Rigidbody>();

        if (collisionCapsule == null)
            collisionCapsule = FindCollisionCapsule();

        if (collisionCapsule != null)
            movementController.AssignMovementCapsule(collisionCapsule);
    }



    private void EnsureTerrainGroundMotorForDefinition(UnitDefinition ud)
    {
        if (ud == null)
            return;

        bool shouldHaveTerrainGroundMotor =
            autoEnsureTerrainGroundMotorForCharacterUnits &&
            ud.defineType == UnitDefineType.Character;

        if (terrainGroundMotor == null)
            terrainGroundMotor = GetComponent<TerrainGroundMotorV5>();

        if (terrainGroundMotor == null && shouldHaveTerrainGroundMotor)
        {
            terrainGroundMotor = gameObject.AddComponent<TerrainGroundMotorV5>();

            if (debugLogs)
                Debug.Log($"[UnitDefinitionRuntimeApplier] {name}: Auto added TerrainGroundMotorV5.", this);
        }

        if (terrainGroundMotor == null)
            return;

        terrainGroundMotor.readUnityInput = false;
        terrainGroundMotor.collisionRootName = string.IsNullOrWhiteSpace(autoCollisionRootName) ? "CollisionRoot" : autoCollisionRootName;
        terrainGroundMotor.autoFindBodyColliderInChildren = true;
        terrainGroundMotor.includeInactiveBodyCollider = true;
        terrainGroundMotor.preferCollisionRootCollider = true;
        terrainGroundMotor.ignoreTriggerBodyColliders = true;

        if (configureTerrainGroundMotorBodyCollider)
        {
            Collider bodyCollider = FindMovementBodyCollider();
            if (bodyCollider != null)
                terrainGroundMotor.bodyColliderOverride = bodyCollider;
        }
    }


    private void EnsureLoadRuntimeForDefinition(UnitDefinition ud)
    {
        if (ud == null)
            return;

        if (loadRuntime == null)
            loadRuntime = GetComponent<UnitLoadRuntime>();

        bool shouldHaveLoadRuntime =
            autoEnsureLoadRuntimeForCharacterUnits &&
            ud.defineType == UnitDefineType.Character;

        if (loadRuntime == null && shouldHaveLoadRuntime)
        {
            loadRuntime = gameObject.AddComponent<UnitLoadRuntime>();

            if (debugLogs)
                Debug.Log($"[UnitDefinitionRuntimeApplier] {name}: Auto added UnitLoadRuntime.", this);
        }

        if (movementController != null && loadRuntime != null)
            movementController.AssignLoadRuntime(loadRuntime);
    }

    private void EnsureFormalControlForDefinition(UnitDefinition ud)
    {
        if (ud == null)
            return;

        bool isCharacter = ud.defineType == UnitDefineType.Character;
        bool isPlayer = isCharacter && ud.characterIdentity == CharacterIdentity.Player;

        if (actionController == null)
            actionController = GetComponent<UnitActionController>();

        if (actionController == null && isCharacter && autoEnsureActionControllerForCharacterUnits)
        {
            actionController = gameObject.AddComponent<UnitActionController>();

            if (debugLogs)
                Debug.Log($"[UnitDefinitionRuntimeApplier] {name}: Auto added UnitActionController.", this);
        }

        if (playerInputRouter == null)
            playerInputRouter = GetComponent<SkyPrisonPlayerInputRouter>();

        if (playerInputRouter == null && isPlayer && autoEnsurePlayerInputRouterForPlayerUnits)
        {
            playerInputRouter = gameObject.AddComponent<SkyPrisonPlayerInputRouter>();

            if (debugLogs)
                Debug.Log($"[UnitDefinitionRuntimeApplier] {name}: Auto added SkyPrisonPlayerInputRouter.", this);
        }

        if (playerInputRouter != null)
            playerInputRouter.enabled = isPlayer;

        // 玩家单位自动挂载鼠标/手柄朝向控制器
        if (isPlayer)
        {
            PlayerAimFacingController aimFacing = GetComponent<PlayerAimFacingController>();
            if (aimFacing == null)
            {
                aimFacing = gameObject.AddComponent<PlayerAimFacingController>();

                if (debugLogs)
                    Debug.Log($"[UnitDefinitionRuntimeApplier] {name}: Auto added PlayerAimFacingController.", this);
            }
            aimFacing.enabled = true;
        }

        if (movementController != null && isPlayer)
        {
            movementController.ConfigureAsExternalExecutor(true);
            if (loadRuntime != null)
                movementController.AssignLoadRuntime(loadRuntime);
        }

        // 注册玩家骨骼到外观系统，并应用存档中的外观设置
        if (isPlayer && AppearanceRuntime.Instance != null)
        {
            var skeleton = FindBestFootstepSkeletonAnimation();
            if (skeleton != null)
                AppearanceRuntime.Instance.RegisterPlayerSkeleton(skeleton);
        }

        // 自动挂载本局统计追踪器
        if (isPlayer && GetComponent<SessionStatsTracker>() == null)
            gameObject.AddComponent<SessionStatsTracker>();

        // 自动挂载属性加成接入器（装备/组件词条 → UnitBattleStatRuntime）
        if (isPlayer && GetComponent<EquipmentStatApplier>() == null)
            gameObject.AddComponent<EquipmentStatApplier>();

        // 自动挂载科技树加成接入器（科技节点奖励 → UnitBattleStatRuntime）
        if (isPlayer && GetComponent<TechTreeStatApplier>() == null)
            gameObject.AddComponent<TechTreeStatApplier>();

        // 自动挂载负重等级控制器（装备总重 → LP消耗倍率 / 移速倍率）
        if (isPlayer && GetComponent<EquipmentWeightController>() == null)
            gameObject.AddComponent<EquipmentWeightController>();
    }


    private void EnsureUnitAudioForDefinition(UnitDefinition ud)
    {
        if (ud == null)
            return;

        bool isCharacter = ud.defineType == UnitDefineType.Character;
        if (!isCharacter)
            return;

        if (!autoEnsureUnitAudioNodesForCharacterUnits && !autoEnsureFootstepAudioEmitterForCharacterUnits && !autoEnsureSpineFootstepEventRelayForCharacterUnits)
            return;

        if (unitAudioRoot == null)
            unitAudioRoot = FindDirectOrDeepChild(unitAudioRootName);

        if (unitAudioRoot == null && autoEnsureUnitAudioNodesForCharacterUnits)
        {
            GameObject go = new GameObject(string.IsNullOrWhiteSpace(unitAudioRootName) ? "AudioRoot" : unitAudioRootName);
            UndoSafeSetParent(go.transform, transform);
            ResetLocalTransform(go.transform);
            unitAudioRoot = go.transform;

            if (debugLogs)
                Debug.Log($"[UnitDefinitionRuntimeApplier] {name}: Auto created AudioRoot.", this);
        }

        if (footstepAudioPoint == null)
        {
            Transform searchRoot = unitAudioRoot != null ? unitAudioRoot : transform;
            footstepAudioPoint = FindDirectChild(searchRoot, footstepAudioPointName);
        }

        if (footstepAudioPoint == null && autoEnsureUnitAudioNodesForCharacterUnits)
        {
            Transform parent = unitAudioRoot != null ? unitAudioRoot : transform;
            GameObject go = new GameObject(string.IsNullOrWhiteSpace(footstepAudioPointName) ? "FootstepAudioPoint" : footstepAudioPointName);
            UndoSafeSetParent(go.transform, parent);
            ResetLocalTransform(go.transform);
            footstepAudioPoint = go.transform;

            if (debugLogs)
                Debug.Log($"[UnitDefinitionRuntimeApplier] {name}: Auto created FootstepAudioPoint.", this);
        }

        if (footstepAudioSource == null && footstepAudioPoint != null)
            footstepAudioSource = footstepAudioPoint.GetComponent<AudioSource>();

        if (footstepAudioSource == null && footstepAudioPoint != null && autoEnsureUnitAudioNodesForCharacterUnits)
        {
            footstepAudioSource = footstepAudioPoint.gameObject.AddComponent<AudioSource>();
            footstepAudioSource.playOnAwake = false;
            footstepAudioSource.loop = false;
            footstepAudioSource.spatialBlend = 1f;

            if (debugLogs)
                Debug.Log($"[UnitDefinitionRuntimeApplier] {name}: Auto added footstep AudioSource.", this);
        }

        if (footstepAudioEmitter == null)
            footstepAudioEmitter = GetComponent<UnitFootstepAudioEmitter>();

        if (footstepAudioEmitter == null && autoEnsureFootstepAudioEmitterForCharacterUnits)
        {
            footstepAudioEmitter = gameObject.AddComponent<UnitFootstepAudioEmitter>();

            if (debugLogs)
                Debug.Log($"[UnitDefinitionRuntimeApplier] {name}: Auto added UnitFootstepAudioEmitter.", this);
        }

        if (footstepAudioEmitter != null)
        {
            footstepAudioEmitter.ConfigureAudioNodes(unitAudioRoot, footstepAudioPoint, footstepAudioSource);
            footstepAudioEmitter.SetUnitDefaultFootstepPackage(ud.defaultFootstepAudioPackage);
        }

        if (spineFootstepEventRelay == null)
            spineFootstepEventRelay = GetComponent<SpineFootstepEventRelay>();

        if (spineFootstepEventRelay == null && autoEnsureSpineFootstepEventRelayForCharacterUnits)
        {
            spineFootstepEventRelay = gameObject.AddComponent<SpineFootstepEventRelay>();

            if (debugLogs)
                Debug.Log($"[UnitDefinitionRuntimeApplier] {name}: Auto added SpineFootstepEventRelay.", this);
        }

        if (spineFootstepEventRelay != null && footstepAudioEmitter != null)
            spineFootstepEventRelay.Configure(footstepAudioEmitter);

        // 自动添加跳跃脚步声桥接器，在起跳/落地时触发 jumpstep_up / jumpstep_down 音轨
        UnitJumpFootstepAudioBridge jumpBridge = GetComponent<UnitJumpFootstepAudioBridge>();
        if (jumpBridge == null && footstepAudioEmitter != null)
        {
            jumpBridge = gameObject.AddComponent<UnitJumpFootstepAudioBridge>();
            if (debugLogs)
                Debug.Log($"[UnitDefinitionRuntimeApplier] {name}: Auto added UnitJumpFootstepAudioBridge.", this);
        }
        if (jumpBridge != null)
        {
            UnitMovementController mc = GetComponent<UnitMovementController>();
            jumpBridge.Configure(mc, footstepAudioEmitter);
        }

        // 自动添加奔跑/跳跃扬尘触发器，监听 footstepAudioEmitter 的 FootstepNoiseEmitted
        // 事件（跟脚步声同一个触发源，不用单独订阅Spine事件/轮询跳跃状态）。
        UnitFootstepVFXEmitter footstepVFXEmitter = GetComponent<UnitFootstepVFXEmitter>();
        if (footstepVFXEmitter == null && footstepAudioEmitter != null)
        {
            footstepVFXEmitter = gameObject.AddComponent<UnitFootstepVFXEmitter>();
            if (debugLogs)
                Debug.Log($"[UnitDefinitionRuntimeApplier] {name}: Auto added UnitFootstepVFXEmitter.", this);
        }
        if (footstepVFXEmitter != null && footstepAudioEmitter != null)
            footstepVFXEmitter.Configure(footstepAudioEmitter);

        // 自动添加闪避扬尘桥接器，轮询 UnitMovementController 闪避状态。
        UnitDodgeVFXBridge dodgeVFXBridge = GetComponent<UnitDodgeVFXBridge>();
        if (dodgeVFXBridge == null)
        {
            dodgeVFXBridge = gameObject.AddComponent<UnitDodgeVFXBridge>();
            if (debugLogs)
                Debug.Log($"[UnitDefinitionRuntimeApplier] {name}: Auto added UnitDodgeVFXBridge.", this);
        }
        UnitMovementController dodgeMc = GetComponent<UnitMovementController>();
        if (dodgeMc != null)
            dodgeVFXBridge.Configure(dodgeMc);
    }

    private void EnsureFootstepRelayBindingAfterVisualRefresh(UnitDefinition ud)
    {
        if (ud == null || ud.defineType != UnitDefineType.Character)
            return;

        if (footstepAudioEmitter == null)
            footstepAudioEmitter = GetComponent<UnitFootstepAudioEmitter>();

        if (spineFootstepEventRelay == null)
            spineFootstepEventRelay = GetComponent<SpineFootstepEventRelay>();

        if (footstepAudioEmitter == null || spineFootstepEventRelay == null)
            return;

        SkeletonAnimation skeleton = FindBestFootstepSkeletonAnimation();
        if (skeleton != null)
            spineFootstepEventRelay.Configure(footstepAudioEmitter, skeleton);
        else
            spineFootstepEventRelay.Configure(footstepAudioEmitter);

        // 原本不受 debugLogs 控制，理由是"Build里脚步声失败会很难跟音频输出问题区分"，
        // 但代价是每个单位生成都会刷一条，一多起来就是纯噪音——改成跟其它诊断一样受
        // debugLogs 控制，需要排查脚步绑定问题时再手动打开。
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (debugLogs)
        {
            string skeletonPath = skeleton != null ? GetHierarchyPath(skeleton.transform) : "null";
            string emitterPath = footstepAudioEmitter != null ? GetHierarchyPath(footstepAudioEmitter.transform) : "null";
            string packageName = ud.defaultFootstepAudioPackage != null ? ud.defaultFootstepAudioPackage.name : "null";
            Debug.Log($"[UnitDefinitionRuntimeApplier] {name}: Footstep runtime binding. skeleton={skeletonPath}, emitter={emitterPath}, package={packageName}", this);
        }
#endif
    }

    private SkeletonAnimation FindBestFootstepSkeletonAnimation()
    {
        SkeletonAnimation[] all = GetComponentsInChildren<SkeletonAnimation>(true);
        if (all == null || all.Length == 0)
            return null;

        SkeletonAnimation best = null;
        int bestScore = int.MinValue;

        for (int i = 0; i < all.Length; i++)
        {
            SkeletonAnimation candidate = all[i];
            if (candidate == null)
                continue;

            int score = ScoreFootstepSkeletonCandidate(candidate);
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    private int ScoreFootstepSkeletonCandidate(SkeletonAnimation candidate)
    {
        string path = GetHierarchyPath(candidate.transform).ToLowerInvariant();
        string objectName = candidate.name.ToLowerInvariant();

        int score = 0;

        // Never prefer outline / occlusion / shadow proxy skeletons for audio events.
        if (path.Contains("outlineproxy") || path.Contains("outline_proxy") || path.Contains("outline proxy"))
            score -= 1000;
        if (path.Contains("proxy") || path.Contains("outline") || path.Contains("occlusion") || path.Contains("shadow"))
            score -= 300;

        // Prefer the real visual hierarchy where gameplay Spine events live.
        if (path.Contains("visualroot"))
            score += 200;
        if (path.Contains("spineroot"))
            score += 200;
        if (path.Contains("spine gameobject") || path.Contains("axia_base"))
            score += 150;
        if (objectName.Contains("spine"))
            score += 80;

        if (candidate.gameObject.activeInHierarchy)
            score += 30;
        if (candidate.enabled)
            score += 20;

        return score;
    }


    private void EnsureVisualRuntimeUtilityComponentsForDefinition(UnitDefinition ud)
    {
        if (ud == null || ud.defineType != UnitDefineType.Character)
            return;

        if (autoEnsureCameraFacingBillboardForCharacterUnits)
            EnsureVisualRootMonoBehaviourByTypeName(cameraFacingBillboardTypeName, "CameraFacingBillboard");

        if (autoEnsureSpinePartOcclusionSamplerForCharacterUnits)
            EnsureRootMonoBehaviourByTypeName(spinePartOcclusionSamplerTypeName, "SpinePartOcclusionSampler");
    }

    private MonoBehaviour EnsureRootMonoBehaviourByTypeName(string typeName, string logName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return null;

        Type type = FindTypeByName(typeName);
        if (type == null || !typeof(MonoBehaviour).IsAssignableFrom(type))
        {
            if (debugLogs)
                Debug.LogWarning($"[UnitDefinitionRuntimeApplier] {name}: {logName} type was not found. Auto setup skipped.", this);
            return null;
        }

        Component existing = GetComponent(type);
        if (existing != null)
        {
            if (existing is Behaviour behaviour)
                behaviour.enabled = true;
            return existing as MonoBehaviour;
        }

        Component created = gameObject.AddComponent(type);
        if (created is Behaviour createdBehaviour)
            createdBehaviour.enabled = true;

        if (debugLogs)
            Debug.Log($"[UnitDefinitionRuntimeApplier] {name}: Auto added {logName}.", this);

        return created as MonoBehaviour;
    }

    private MonoBehaviour EnsureVisualRootMonoBehaviourByTypeName(string typeName, string logName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return null;

        Type type = FindTypeByName(typeName);
        if (type == null || !typeof(MonoBehaviour).IsAssignableFrom(type))
        {
            if (debugLogs)
                Debug.LogWarning($"[UnitDefinitionRuntimeApplier] {name}: {logName} type was not found. Visual-only auto setup skipped.", this);
            return null;
        }

        Transform visualRoot = FindVisualRootForOcclusion();
        if (visualRoot == null || visualRoot == transform)
        {
            Component dirtyRootOnly = GetComponent(type);
            if (dirtyRootOnly is Behaviour dirtyRootBehaviour)
                dirtyRootBehaviour.enabled = false;

            if (debugLogs)
                Debug.LogWarning($"[UnitDefinitionRuntimeApplier] {name}: VisualRoot was not found. {logName} was not auto-added to unit root because it would rotate physics/collision.", this);
            return null;
        }

        Component existing = visualRoot.GetComponent(type);
        if (existing == null)
        {
            existing = visualRoot.gameObject.AddComponent(type);

            if (debugLogs)
                Debug.Log($"[UnitDefinitionRuntimeApplier] {name}: Auto added {logName} to {visualRoot.name}.", visualRoot);
        }

        if (existing is Behaviour behaviour)
            behaviour.enabled = true;

        Component rootExisting = GetComponent(type);
        if (rootExisting != null && rootExisting != existing)
        {
            if (debugLogs)
                Debug.LogWarning($"[UnitDefinitionRuntimeApplier] {name}: Removing dirty root-level {logName}. Billboard must live on VisualRoot only, not on Player/Unit root.", rootExisting);

            RemoveComponentSafely(rootExisting);
        }

        return existing as MonoBehaviour;
    }

    private static void RemoveComponentSafely(Component component)
    {
        if (component == null)
            return;

        if (Application.isPlaying)
            Destroy(component);
        else
            DestroyImmediate(component);
    }


    private void EnsurePerceptionRuntimeForDefinition(UnitDefinition ud)
    {
        if (ud == null)
            return;

        bool isCharacter = ud.defineType == UnitDefineType.Character;
        bool shouldHavePerceptionRuntime = autoEnsurePerceptionRuntimeForCharacterUnits && isCharacter;

        if (perceptionRuntime == null)
            perceptionRuntime = GetComponent<UnitPerceptionRuntime>();

        if (perceptionRuntime == null && shouldHavePerceptionRuntime)
        {
            perceptionRuntime = gameObject.AddComponent<UnitPerceptionRuntime>();

            if (debugLogs)
                Debug.Log($"[UnitDefinitionRuntimeApplier] {name}: Auto added UnitPerceptionRuntime.", this);
        }
    }

    private void ApplyToPerceptionRuntime(UnitDefinition ud)
    {
        if (ud == null)
            return;

        if (perceptionRuntime == null)
            perceptionRuntime = GetComponent<UnitPerceptionRuntime>();

        if (perceptionRuntime == null)
            return;

        bool isCharacter = ud.defineType == UnitDefineType.Character;
        bool hasAIPackage = ud.aiBehaviorPackage != null
            || (aiBehaviorRuntimeController != null && aiBehaviorRuntimeController.BehaviorPackage != null);
        bool sensorsEnabledByDefault = !defaultPerceptionSensorsOnlyForAIUnits || hasAIPackage;
        bool runtimeEnabled = isCharacter && enablePerceptionRuntimeForCharacterUnits;

        perceptionRuntime.enabled = runtimeEnabled;
        perceptionRuntime.ConfigureFromDefinition(
            runtimeBinder,
            ud,
            runtimeEnabled,
            defaultCanUseVision: runtimeEnabled && sensorsEnabledByDefault,
            defaultCanUseHearing: runtimeEnabled && sensorsEnabledByDefault,
            defaultCanBePerceived: isCharacter
        );

        if (debugLogs)
        {
            Debug.Log(
                $"[UnitDefinitionRuntimeApplier] {name}: perceptionRuntime={(perceptionRuntime != null ? "present" : "missing")}, enabled={runtimeEnabled}, vision={perceptionRuntime.CanUseVision}, hearing={perceptionRuntime.CanUseHearing}, bePerceived={perceptionRuntime.CanBePerceived}, visionThresholds={perceptionRuntime.VisionSuspicionThreshold:0.###}/{perceptionRuntime.VisionAlertThreshold:0.###}/{perceptionRuntime.VisionDetectThreshold:0.###}, visionHold={perceptionRuntime.VisionAlertHoldSeconds:0.###}/{perceptionRuntime.VisionDetectHoldSeconds:0.###}, hearingMultiplier={perceptionRuntime.HearingMultiplier:0.###}, level={perceptionRuntime.CurrentLevel}",
                this
            );
        }
    }


    private void EnsureAIBehaviorRuntimeForDefinition(UnitDefinition ud)
    {
        if (ud == null)
            return;

        if (aiBehaviorRuntimeController == null)
            aiBehaviorRuntimeController = GetComponent<AIBehaviorPackageRuntimeController>();

        if (aiBehaviorRuntimeController == null)
            aiBehaviorRuntimeController = GetComponentInParent<AIBehaviorPackageRuntimeController>();

        if (aiBehaviorRuntimeController == null)
            aiBehaviorRuntimeController = GetComponentInChildren<AIBehaviorPackageRuntimeController>(true);

        bool hasAIPackage = ud.aiBehaviorPackage != null;
        bool shouldHaveAIRuntime =
            autoEnsureAIBehaviorRuntimeForUnitsWithPackage &&
            ud.defineType == UnitDefineType.Character &&
            hasAIPackage;

        if (aiBehaviorRuntimeController == null && shouldHaveAIRuntime)
        {
            aiBehaviorRuntimeController = gameObject.AddComponent<AIBehaviorPackageRuntimeController>();

            if (debugLogs)
                Debug.Log($"[UnitDefinitionRuntimeApplier] {name}: Auto added AIBehaviorPackageRuntimeController.", this);
        }

        if (aiBehaviorRuntimeController == null)
            return;

        if (autoApplyAIBehaviorPackage && hasAIPackage)
            aiBehaviorRuntimeController.SetBehaviorPackage(ud.aiBehaviorPackage);

        aiBehaviorRuntimeController.AutoSetup();

        if (hasAIPackage && movementController != null && movementController.InputMode != UnitMovementController.MovementInputMode.External)
            movementController.SetInputMode(UnitMovementController.MovementInputMode.External);
    }

    private void ApplyToOverheadUI(UnitDefinition ud)
    {
        if (overheadView == null)
            overheadView = GetComponent<UnitOverheadUIView>() ?? GetComponentInChildren<UnitOverheadUIView>(true);

        if (overheadView != null)
        {
            overheadView.unitDefinition = ud;
            overheadView.ApplyStyleFromDefinition();
        }

        if (overheadHealthBridge == null)
            overheadHealthBridge = GetComponent<UnitOverheadHealthBridge>() ?? GetComponentInChildren<UnitOverheadHealthBridge>(true);

        if (healthController == null)
            healthController = GetComponent<UnitHealthController>() ?? GetComponentInChildren<UnitHealthController>(true);

        if (overheadHealthBridge != null)
            overheadHealthBridge.RefreshNow();
    }

    private void ApplyToFogVisibility(UnitDefinition ud)
    {
        if (!autoEnsureFogVisibilityController && fogUnitVisibilityController == null)
            return;

        if (fogUnitVisibilityController == null && autoEnsureFogVisibilityController)
            fogUnitVisibilityController = gameObject.GetComponent<SkyPrisonFogUnitVisibilityController>() ?? gameObject.AddComponent<SkyPrisonFogUnitVisibilityController>();

        if (fogUnitVisibilityController == null)
            return;

        bool isCharacter = ud.defineType == UnitDefineType.Character;
        bool shouldEnable = isCharacter && enableFogVisibilityForAllCharacterUnits;

        if (!isCharacter && disableFogVisibilityForNonCharacterUnits)
            shouldEnable = false;

        fogUnitVisibilityController.enabled = shouldEnable;
        fogUnitVisibilityController.ResolveReferences();
        fogUnitVisibilityController.ApplyVisibilityImmediate();

        if (debugLogs)
        {
            Debug.Log(
                $"[UnitDefinitionRuntimeApplier] {name}: fogVisibilityComponent={(fogUnitVisibilityController != null ? "present" : "missing")}, enabled={shouldEnable}, defineType={ud.defineType}, identity={ud.characterIdentity}",
                this
            );
        }
    }

    private void ApplyToOutlineState(UnitDefinition ud)
    {
        if (unitOutlineState == null)
            unitOutlineState = GetComponent<UnitOutlineState>();

        if (unitOutlineState == null)
            return;

        OutlineState runtimeState = MapOutlineGroupToOutlineState(ud.ResolvedOutlineGroup);

        if (ud.ResolvedOutlineGroup == OutlineGroup.None)
        {
            unitOutlineState.SetOutlineState(OutlineState.None);
            unitOutlineState.SetOutlineEnabled(false);
            unitOutlineState.ApplyState();
            return;
        }

        unitOutlineState.SetOutlineState(runtimeState);
        unitOutlineState.SetOutlineEnabled(ud.useSharedOutlineGroup);
        unitOutlineState.ApplyState();
    }

    private void ApplyToOcclusionReceiver(UnitDefinition ud)
    {
        if (ud == null)
            return;

        if (unitOcclusionMaterialReceiver == null)
            unitOcclusionMaterialReceiver = GetComponent<UnitOcclusionMaterialReceiver>();

        bool shouldHaveReceiver = ud.defineType == UnitDefineType.Character && ud.useOcclusionOutline;

        if (unitOcclusionMaterialReceiver == null && shouldHaveReceiver && autoEnsureOcclusionMaterialReceiverForCharacterUnits)
        {
            unitOcclusionMaterialReceiver = gameObject.AddComponent<UnitOcclusionMaterialReceiver>();

            if (debugLogs)
                Debug.Log($"[UnitDefinitionRuntimeApplier] {name}: Auto added UnitOcclusionMaterialReceiver.", this);
        }

        if (unitOcclusionMaterialReceiver == null)
            return;

        unitOcclusionMaterialReceiver.enabled = shouldHaveReceiver;

        if (!shouldHaveReceiver || !autoBindSpineOcclusionMaterials)
            return;

        AutoBindOcclusionReceiverFromCurrentSpineVisual();

        // FindBestOcclusionCompositeMaterial 的 AssetDatabase 扫描分支只在 Editor 里存在——Build
        // 里永远走 CreateRuntimeOcclusionCompositeMaterial 兜底，那条路径从不设置阵营色，材质会
        // 停在 shader 默认值（黄色）。这里用跟另一套描边系统同源的 ResolvedOutlineGroup 直接补色，
        // 两条路径（Editor 预配色资产 / Build 运行时材质）都会覆盖到，不会出现 Editor 和 Build 表现不一致。
        ApplyHiddenOutlineColorForFaction(ud.ResolvedOutlineGroup);
    }

    private static readonly int HiddenOutlineColorId = Shader.PropertyToID("_SkyPrison_HiddenOutlineColor");
    private static readonly int UseHologramFillId = Shader.PropertyToID("_SkyPrison_UseHologramFill");
    private static readonly int HologramFillColorId = Shader.PropertyToID("_SkyPrison_HologramFillColor");

    private static Color ResolveHiddenOutlineColorForGroup(OutlineGroup group)
    {
        switch (group)
        {
            case OutlineGroup.Enemy:
                return new Color(1f, 0.16f, 0.16f, 1f);
            case OutlineGroup.Ally:
                return new Color(0.56f, 0.86f, 1f, 1f);
            case OutlineGroup.Item:
                return Color.white;
            case OutlineGroup.Player:
            default:
                return new Color(1f, 0.83f, 0f, 1f);
        }
    }

    // 2026-07-19：遮挡时的表现从"描边"换成"全息点阵填充"——描边在多个角色贴近交叉时
    // 有天花板（逐网格画的线只能画在自己网格覆盖到的范围内，交叉处会被对方实体盖住），
    // 填充式效果只需要"是否被遮挡"这一个信号，不需要找边缘，天生没有这个问题。
    // 颜色沿用同一套阵营配色，跟掉落物本来就有的全息效果（LootDropHologram.shader）
    // 风格统一。
    private static Color ResolveHologramFillColorForGroup(OutlineGroup group)
    {
        switch (group)
        {
            case OutlineGroup.Enemy:
                return new Color(1f, 0.35f, 0.35f, 0.8f);
            case OutlineGroup.Ally:
                return new Color(0.56f, 0.86f, 1f, 0.8f);
            case OutlineGroup.Item:
                return new Color(1f, 1f, 1f, 0.8f);
            case OutlineGroup.Player:
            default:
                return new Color(1f, 0.92f, 0.55f, 0.8f);
        }
    }

    private void ApplyHiddenOutlineColorForFaction(OutlineGroup group)
    {
        if (unitOcclusionMaterialReceiver == null)
            return;

        Material[] mats = unitOcclusionMaterialReceiver.OcclusionCompositeMaterials;
        if (mats == null)
            return;

        Color outlineColor = ResolveHiddenOutlineColorForGroup(group);
        Color hologramColor = ResolveHologramFillColorForGroup(group);
        for (int i = 0; i < mats.Length; i++)
        {
            Material mat = mats[i];
            if (mat == null)
                continue;

            if (mat.HasProperty(HiddenOutlineColorId))
                mat.SetColor(HiddenOutlineColorId, outlineColor);
            if (mat.HasProperty(UseHologramFillId))
                mat.SetFloat(UseHologramFillId, 1f);
            if (mat.HasProperty(HologramFillColorId))
                mat.SetColor(HologramFillColorId, hologramColor);
        }
    }

    [ContextMenu("Auto Bind Occlusion Receiver From Spine Visual")]
    private void AutoBindOcclusionReceiverFromCurrentSpineVisual()
    {
        if (unitOcclusionMaterialReceiver == null)
            unitOcclusionMaterialReceiver = GetComponent<UnitOcclusionMaterialReceiver>();

        if (unitOcclusionMaterialReceiver == null)
            return;

        Transform visualRoot = FindVisualRootForOcclusion();
        Transform searchRoot = visualRoot != null ? visualRoot : transform;

        Renderer primaryRenderer = FindBestSpineMainRenderer(searchRoot);
        if (primaryRenderer == null)
            return;

        Material[] normalSet = CloneMaterials(primaryRenderer.sharedMaterials);
        Material[] compositeSet = FindCompositeMaterialsForRenderer(primaryRenderer);
        Renderer[] extraRenderers = FindExtraRealVisualRenderers(searchRoot, primaryRenderer);

        unitOcclusionMaterialReceiver.ConfigureRendererAndMaterials(
            primaryRenderer,
            extraRenderers,
            normalSet,
            compositeSet,
            includeAllRenderers: false
        );

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEditor.EditorUtility.SetDirty(unitOcclusionMaterialReceiver);
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif

        if (debugLogs)
        {
            string normalName = normalSet != null && normalSet.Length > 0 && normalSet[0] != null ? normalSet[0].name : "NULL";
            string compositeName = compositeSet != null && compositeSet.Length > 0 && compositeSet[0] != null ? compositeSet[0].name : "NULL";
            Debug.Log(
                $"[UnitDefinitionRuntimeApplier] {name}: OcclusionReceiver bound. primary={GetHierarchyPath(primaryRenderer.transform)}, normal={normalName}, composite={compositeName}, extra={extraRenderers.Length}",
                this
            );
        }
    }

    private Transform FindVisualRootForOcclusion()
    {
        if (!string.IsNullOrWhiteSpace(visualRootNameForOcclusion))
        {
            Transform direct = transform.Find(visualRootNameForOcclusion);
            if (direct != null)
                return direct;

            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (t != null && t.name == visualRootNameForOcclusion)
                    return t;
            }
        }

        return null;
    }

    private Renderer FindBestSpineMainRenderer(Transform searchRoot)
    {
        if (searchRoot == null)
            return null;

        Renderer[] renderers = searchRoot.GetComponentsInChildren<Renderer>(true);
        Renderer best = null;
        int bestScore = int.MinValue;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (!IsRealUnitVisualRenderer(renderer))
                continue;

            int score = ScoreSpineMainRenderer(renderer);
            if (score > bestScore)
            {
                best = renderer;
                bestScore = score;
            }
        }

        return best;
    }

    private int ScoreSpineMainRenderer(Renderer renderer)
    {
        if (renderer == null)
            return int.MinValue;

        int score = 0;
        string path = GetHierarchyPath(renderer.transform).ToLowerInvariant();
        string objectName = renderer.name.ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(spineObjectNameHintForOcclusion) && path.Contains(spineObjectNameHintForOcclusion.ToLowerInvariant()))
            score += 1000;

        if (objectName.Contains("spine"))
            score += 300;

        Material[] mats = renderer.sharedMaterials;
        if (mats != null)
        {
            for (int i = 0; i < mats.Length; i++)
            {
                Material mat = mats[i];
                if (mat == null)
                    continue;

                string matName = mat.name.ToLowerInvariant();
                string shaderName = mat.shader != null ? mat.shader.name.ToLowerInvariant() : "";

                if (shaderName.Contains("spine/skeleton") || shaderName.Contains("spine"))
                    score += 500;

                if (matName.Contains("clean") || matName.Contains("base"))
                    score += 100;

                if (mat.mainTexture != null)
                    score += 50;
            }
        }

        return score;
    }

    private Renderer[] FindExtraRealVisualRenderers(Transform searchRoot, Renderer primaryRenderer)
    {
        if (searchRoot == null || primaryRenderer == null)
            return new Renderer[0];

        Texture primaryTexture = GetFirstMainTexture(primaryRenderer.sharedMaterials);
        List<Renderer> results = new List<Renderer>();
        Renderer[] renderers = searchRoot.GetComponentsInChildren<Renderer>(true);

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || renderer == primaryRenderer)
                continue;

            if (!IsRealUnitVisualRenderer(renderer))
                continue;

            // 只把同一张主贴图、或同类 Spine/Skeleton 材质的真实视觉 Renderer 纳入。
            // 这样武器/挂件可以跟着遮挡，但描边代理、阴影、UI 不会被卷进去。
            Texture tex = GetFirstMainTexture(renderer.sharedMaterials);
            if (primaryTexture != null && tex == primaryTexture)
            {
                results.Add(renderer);
                continue;
            }

            if (UsesSpineSkeletonShader(renderer))
                results.Add(renderer);
        }

        return results.ToArray();
    }

    private bool IsRealUnitVisualRenderer(Renderer renderer)
    {
        if (renderer == null)
            return false;

        string path = GetHierarchyPath(renderer.transform).ToLowerInvariant();

        if (includeOnlyRealVisualRenderersForOcclusion)
        {
            if (path.Contains("outline") ||
                path.Contains("proxy") ||
                path.Contains("shadow") ||
                path.Contains("canvas") ||
                path.Contains("ui") ||
                path.Contains("health") ||
                path.Contains("bar") ||
                path.Contains("trigger") ||
                path.Contains("collider") ||
                path.Contains("gizmo") ||
                path.Contains("debug"))
                return false;
        }

        Material[] mats = renderer.sharedMaterials;
        if (mats == null || mats.Length == 0)
            return false;

        bool hasUsableMaterial = false;
        for (int i = 0; i < mats.Length; i++)
        {
            Material mat = mats[i];
            if (mat == null)
                continue;

            string matName = mat.name.ToLowerInvariant();
            string shaderName = mat.shader != null ? mat.shader.name.ToLowerInvariant() : "";

            if (matName.Contains("occlusioncomposite") || shaderName.Contains("occlusioncomposite"))
                return false;

            hasUsableMaterial = true;
        }

        return hasUsableMaterial;
    }

    private bool UsesSpineSkeletonShader(Renderer renderer)
    {
        if (renderer == null)
            return false;

        Material[] mats = renderer.sharedMaterials;
        if (mats == null)
            return false;

        for (int i = 0; i < mats.Length; i++)
        {
            Material mat = mats[i];
            if (mat == null || mat.shader == null)
                continue;

            string shaderName = mat.shader.name.ToLowerInvariant();
            if (shaderName.Contains("spine/skeleton") || shaderName.Contains("spine"))
                return true;
        }

        return false;
    }

    private Material[] FindCompositeMaterialsForRenderer(Renderer primaryRenderer)
    {
        if (primaryRenderer == null)
            return null;

        // 如果组件上已经有人手动挂了 Composite 材质，优先尊重已有配置。
        Material[] existing = unitOcclusionMaterialReceiver != null ? unitOcclusionMaterialReceiver.OcclusionCompositeMaterials : null;
        if (existing != null && existing.Length > 0 && existing[0] != null)
            return CloneMaterials(existing);

        Material composite = FindBestOcclusionCompositeMaterial(primaryRenderer);
        if (composite == null)
            return null;

        Material[] current = primaryRenderer.sharedMaterials;
        int slotCount = current != null && current.Length > 0 ? current.Length : 1;
        Material[] result = new Material[slotCount];
        for (int i = 0; i < result.Length; i++)
            result[i] = composite;

        return result;
    }

    private Material FindBestOcclusionCompositeMaterial(Renderer primaryRenderer)
    {
        if (primaryRenderer == null)
            return null;

        // Build-safe path: AssetDatabase cannot be used outside the Editor.
        // Prefer an explicitly serialized material if present.
        if (IsValidOcclusionCompositeMaterial(fallbackSpineOcclusionCompositeMaterial))
        {
            PrepareCompositeMaterialFromRenderer(fallbackSpineOcclusionCompositeMaterial, primaryRenderer);
            return fallbackSpineOcclusionCompositeMaterial;
        }

        Texture primaryTexture = GetFirstMainTexture(primaryRenderer.sharedMaterials);
        string modelToken = BuildModelToken(primaryRenderer);

#if UNITY_EDITOR
        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:Material");
        Material best = null;
        int bestScore = int.MinValue;

        for (int i = 0; i < guids.Length; i++)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]);
            Material mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!IsValidOcclusionCompositeMaterial(mat))
                continue;

            string matName = mat.name.ToLowerInvariant();
            string shaderName = mat.shader != null ? mat.shader.name.ToLowerInvariant() : "";
            string keyword = string.IsNullOrWhiteSpace(occlusionCompositeShaderNameKeyword)
                ? "spineocclusioncomposite"
                : occlusionCompositeShaderNameKeyword.ToLowerInvariant();

            int score = 0;

            if (primaryTexture != null && mat.mainTexture == primaryTexture)
                score += 10000;

            if (!string.IsNullOrWhiteSpace(modelToken) && matName.Contains(modelToken.ToLowerInvariant()))
                score += 1000;

            if (shaderName.Contains(keyword))
                score += 500;

            if (path.ToLowerInvariant().Contains("occlusion"))
                score += 100;

            if (score > bestScore)
            {
                best = mat;
                bestScore = score;
            }
        }

        if (best != null)
        {
            PrepareCompositeMaterialFromRenderer(best, primaryRenderer);
            return best;
        }
#endif

        // Runtime / Build fallback. This is the important part:
        // never return null just because AssetDatabase is unavailable.
        if (createRuntimeOcclusionCompositeMaterial)
            return CreateRuntimeOcclusionCompositeMaterial(primaryRenderer);

        return null;
    }

    private bool IsValidOcclusionCompositeMaterial(Material mat)
    {
        if (mat == null || mat.shader == null)
            return false;

        string matName = mat.name.ToLowerInvariant();
        string shaderName = mat.shader.name.ToLowerInvariant();
        string keyword = string.IsNullOrWhiteSpace(occlusionCompositeShaderNameKeyword)
            ? "spineocclusioncomposite"
            : occlusionCompositeShaderNameKeyword.ToLowerInvariant();

        return shaderName.Contains(keyword) ||
               shaderName.Contains("occlusioncomposite") ||
               matName.Contains("occlusioncomposite");
    }

    private Material CreateRuntimeOcclusionCompositeMaterial(Renderer primaryRenderer)
    {
        if (primaryRenderer == null)
            return null;

        string shaderName = string.IsNullOrWhiteSpace(runtimeOcclusionCompositeShaderName)
            ? "Spine/SpineOcclusionComposite"
            : runtimeOcclusionCompositeShaderName;

        Shader compositeShader = Shader.Find(shaderName);
        if (compositeShader == null)
        {
            if (debugLogs)
                Debug.LogWarning($"[UnitDefinitionRuntimeApplier] {name}: Shader.Find failed: {shaderName}. Add the shader to Always Included Shaders or reference a fallback material.", this);
            return null;
        }

        Material source = GetFirstMaterial(primaryRenderer.sharedMaterials);
        Material runtimeMat = new Material(compositeShader)
        {
            name = (source != null ? source.name : primaryRenderer.name) + "_RuntimeOcclusionComposite"
        };

        CopyMaterialBasics(source, runtimeMat);
        PrepareCompositeMaterialFromRenderer(runtimeMat, primaryRenderer);
        return runtimeMat;
    }

    private static Material GetFirstMaterial(Material[] mats)
    {
        if (mats == null)
            return null;

        for (int i = 0; i < mats.Length; i++)
        {
            if (mats[i] != null)
                return mats[i];
        }

        return null;
    }

    private static void CopyMaterialBasics(Material source, Material target)
    {
        if (source == null || target == null)
            return;

        if (source.HasProperty("_MainTex") && target.HasProperty("_MainTex"))
            target.SetTexture("_MainTex", source.GetTexture("_MainTex"));

        if (source.HasProperty("_TintColor") && target.HasProperty("_TintColor"))
            target.SetColor("_TintColor", source.GetColor("_TintColor"));

        if (source.HasProperty("_Color") && target.HasProperty("_TintColor"))
            target.SetColor("_TintColor", source.GetColor("_Color"));

        if (source.HasProperty("_StraightAlphaInput") && target.HasProperty("_StraightAlphaInput"))
            target.SetFloat("_StraightAlphaInput", source.GetFloat("_StraightAlphaInput"));
    }

    private static void PrepareCompositeMaterialFromRenderer(Material composite, Renderer primaryRenderer)
    {
        if (composite == null || primaryRenderer == null)
            return;

        Material source = GetFirstMaterial(primaryRenderer.sharedMaterials);
        CopyMaterialBasics(source, composite);

        Texture mainTexture = GetFirstMainTexture(primaryRenderer.sharedMaterials);
        if (mainTexture != null && composite.HasProperty("_MainTex"))
            composite.SetTexture("_MainTex", mainTexture);

        if (composite.HasProperty("_OccludedAlpha"))
            composite.SetFloat("_OccludedAlpha", 0f);

        if (composite.HasProperty("_DebugMaskMode"))
            composite.SetFloat("_DebugMaskMode", 0f);

        if (composite.HasProperty("_RawDiscardThreshold"))
            composite.SetFloat("_RawDiscardThreshold", 0.01f);
    }

    private static Texture GetFirstMainTexture(Material[] mats)
    {
        if (mats == null)
            return null;

        for (int i = 0; i < mats.Length; i++)
        {
            Material mat = mats[i];
            if (mat != null && mat.mainTexture != null)
                return mat.mainTexture;
        }

        return null;
    }

    private static string BuildModelToken(Renderer renderer)
    {
        if (renderer == null)
            return "";

        Material[] mats = renderer.sharedMaterials;
        if (mats != null)
        {
            for (int i = 0; i < mats.Length; i++)
            {
                Material mat = mats[i];
                if (mat == null)
                    continue;

                string n = mat.name;
                n = n.Replace("_Material_Clean", "");
                n = n.Replace("_Material", "");
                n = n.Replace("_Clean", "");
                if (!string.IsNullOrWhiteSpace(n))
                    return n;
            }
        }

        string objectName = renderer.name;
        objectName = objectName.Replace("Spine GameObject", "");
        objectName = objectName.Replace("(", "").Replace(")", "").Trim();
        return objectName;
    }

    private static Material[] CloneMaterials(Material[] source)
    {
        if (source == null)
            return null;

        Material[] clone = new Material[source.Length];
        for (int i = 0; i < source.Length; i++)
            clone[i] = source[i];
        return clone;
    }

    private static string GetHierarchyPath(Transform t)
    {
        if (t == null)
            return "";

        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }

        return path;
    }

    private void ApplyToOccludedProxyGate()
    {
        if (occludedOutlineProxyGate == null)
            occludedOutlineProxyGate = GetComponent<UnitOccludedOutlineProxyGate>();

        if (occludedOutlineProxyGate == null)
            return;

        occludedOutlineProxyGate.AutoSetup();
        occludedOutlineProxyGate.RefreshCamp();
    }

    private void ApplyToPhysics(UnitDefinition ud)
    {
        if (ud == null)
            return;

        if (targetRigidbody == null)
            targetRigidbody = GetComponent<Rigidbody>();

        if (targetRigidbody == null)
            return;

        bool resolvedUseGravity = false;
        bool resolvedIsKinematic = ud.useKinematicBody && !ud.allowPhysicalPush;

        RigidbodyConstraints constraints = RigidbodyConstraints.None;
        constraints |= RigidbodyConstraints.FreezePositionY;

        if (ud.freezeRotationX)
            constraints |= RigidbodyConstraints.FreezeRotationX;
        if (ud.freezeRotationY)
            constraints |= RigidbodyConstraints.FreezeRotationY;
        if (ud.freezeRotationZ)
            constraints |= RigidbodyConstraints.FreezeRotationZ;

        targetRigidbody.useGravity = resolvedUseGravity;
        targetRigidbody.isKinematic = resolvedIsKinematic;
        targetRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        targetRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        targetRigidbody.constraints = constraints;

        if (movementController == null)
            movementController = GetComponent<UnitMovementController>();

        if (movementController != null)
        {
            movementController.ApplyPhysicsDefinition(
                resolvedUseGravity,
                resolvedIsKinematic,
                RigidbodyInterpolation.Interpolate,
                CollisionDetectionMode.ContinuousDynamic,
                constraints
            );
        }
    }

    private void ApplyToCollisionShape(UnitDefinition ud)
    {
        if (ud == null)
            return;

        if (collisionCapsule == null)
            collisionCapsule = FindCollisionCapsule();

        if (collisionCapsule == null)
            return;

        collisionCapsule.enabled = true;
        collisionCapsule.isTrigger = false;

        if (ud.enableUnitBodyCollision)
            ApplyLayerByName(collisionCapsule.gameObject, ud.unitBodyLayerName);

        if (movementController == null)
            movementController = GetComponent<UnitMovementController>();

        if (movementController != null)
            movementController.AssignMovementCapsule(collisionCapsule);

        if (!ud.overrideCollisionShape)
        {
            ApplyDefaultCapsuleShape();
            return;
        }

        collisionCapsule.radius = Mathf.Max(0.01f, ud.collisionRadius);
        collisionCapsule.height = Mathf.Max(Mathf.Max(0.01f, ud.collisionHeight), collisionCapsule.radius * 2f);
        collisionCapsule.center = ResolveCollisionCenterForRootConvention(ud, ud.collisionLocalCenter, collisionCapsule.height);
    }

    private Vector3 ResolveCollisionCenterForRootConvention(UnitDefinition ud, Vector3 authoredCenter, float resolvedHeight)
    {
        Vector3 center = authoredCenter;

        // Project convention: character unit root is the foot/ground point.
        // Therefore the movement capsule bottom must sit on the unit origin in both Editor Play and Build.
        // Older UnitDefinition assets may contain an authored center.y such as 0.7 for a 4.0 capsule;
        // TerrainGroundMotor then lifts the whole unit root by 1.3 to keep the capsule bottom on ground,
        // which also lifts VisualRoot/SpineRoot.  Normalize the center here instead of patching visual Y.
        if (ud != null && ud.defineType == UnitDefineType.Character && (capsuleRootIsFootPoint || keepAutoCapsuleBottomOnUnitOrigin))
        {
            float scaleY = collisionCapsule != null ? Mathf.Abs(collisionCapsule.transform.lossyScale.y) : 1f;
            if (scaleY < 0.0001f)
                scaleY = 1f;

            center.y = Mathf.Max(0.01f, resolvedHeight) * 0.5f + capsuleBottomPadding / scaleY;
        }

        return center;
    }


    private void ApplyDefaultCapsuleShape()
    {
        if (collisionCapsule == null)
            return;

        float height = Mathf.Max(0.01f, defaultCapsuleHeight);
        float radius = Mathf.Max(0.01f, defaultCapsuleRadius);
        collisionCapsule.direction = 1;
        collisionCapsule.radius = radius;
        collisionCapsule.height = Mathf.Max(height, radius * 2f);

        // 单位根节点默认按“脚底/落地点”使用。
        // Capsule center 如果放在 0，会让胶囊一半插到地面下面，物理求解或移动控制器容易把视觉体往下/往上挤。
        collisionCapsule.center = keepAutoCapsuleBottomOnUnitOrigin
            ? new Vector3(0f, collisionCapsule.height * 0.5f, 0f)
            : Vector3.zero;
    }


    private void ScheduleOrRunCapsuleAutoCalibration(UnitDefinition ud)
    {
        if (!ShouldAutoCalibrateCapsule(ud))
            return;

        if (calibrateCapsuleImmediatelyOnApply)
            AutoCalibrateCapsuleFromVisualBounds("Immediate");

        if (!calibrateCapsuleAfterVisualSettles || !Application.isPlaying || !isActiveAndEnabled)
            return;

        if (pendingCapsuleCalibrationRoutine != null)
            StopCoroutine(pendingCapsuleCalibrationRoutine);

        pendingCapsuleCalibrationRoutine = StartCoroutine(CoAutoCalibrateCapsuleAfterVisualSettles());
    }

    private IEnumerator CoAutoCalibrateCapsuleAfterVisualSettles()
    {
        int frames = Mathf.Max(0, capsuleVisualSettleFrames);
        for (int i = 0; i < frames; i++)
            yield return null;

        AutoCalibrateCapsuleFromVisualBounds("VisualSettled");
        pendingCapsuleCalibrationRoutine = null;
    }

    private bool ShouldAutoCalibrateCapsule(UnitDefinition ud)
    {
        if (!autoCalibrateCapsuleFromFinalVisualBounds)
            return false;

        if (ud == null || ud.defineType != UnitDefineType.Character)
            return false;

        if (collisionCapsule == null)
            collisionCapsule = FindCollisionCapsule();

        if (collisionCapsule == null)
            return false;

        if (ud.overrideCollisionShape && !runCapsuleCalibrationEvenWhenDefinitionOverridesShape)
            return false;

        return true;
    }

    [ContextMenu("Auto Calibrate Capsule From Visual Bounds")]
    public void AutoCalibrateCapsuleFromVisualBoundsContext()
    {
        AutoCalibrateCapsuleFromVisualBounds("ContextMenu");
    }

    private bool AutoCalibrateCapsuleFromVisualBounds(string reason)
    {
        if (collisionCapsule == null)
            collisionCapsule = FindCollisionCapsule();

        if (collisionCapsule == null)
            return false;

        Bounds visualBounds;
        if (!TryGetRealVisualWorldBounds(out visualBounds))
        {
            if (debugLogs || logCapsuleAutoCalibration)
                Debug.LogWarning($"[UnitDefinitionRuntimeApplier] {name}: Capsule auto calibration skipped. Real VisualRoot renderer bounds not found. reason={reason}", this);
            return false;
        }

        float scaleY = Mathf.Abs(collisionCapsule.transform.lossyScale.y);
        if (scaleY < 0.0001f)
            scaleY = 1f;

        float scaleXZ = Mathf.Max(
            Mathf.Abs(collisionCapsule.transform.lossyScale.x),
            Mathf.Abs(collisionCapsule.transform.lossyScale.z)
        );
        if (scaleXZ < 0.0001f)
            scaleXZ = 1f;

        float worldHeight = Mathf.Max(0.01f, visualBounds.size.y * Mathf.Max(0.01f, capsuleHeightScaleFromVisualBounds) + capsuleTopPadding + capsuleBottomPadding);
        float localHeight = Mathf.Max(capsuleMinimumHeight, worldHeight / scaleY);

        float localRadius = collisionCapsule.radius;
        if (autoCalibrateCapsuleRadiusFromVisualWidth && !keepCurrentCapsuleRadiusOnAutoCalibration)
        {
            float worldWidth = Mathf.Max(visualBounds.size.x, visualBounds.size.z);
            localRadius = Mathf.Max(capsuleMinimumRadius, (worldWidth * capsuleRadiusFromVisualWidthFactor) / scaleXZ);
        }
        else
        {
            localRadius = Mathf.Max(capsuleMinimumRadius, localRadius);
        }

        localHeight = Mathf.Max(localHeight, localRadius * 2f + 0.001f);

        if (resetCapsuleTransformPitchRollOnCalibration)
            ResetCapsuleTransformPitchRoll();

        collisionCapsule.enabled = true;
        collisionCapsule.isTrigger = false;
        collisionCapsule.direction = 1;
        collisionCapsule.radius = localRadius;
        collisionCapsule.height = localHeight;

        Vector3 center = collisionCapsule.center;
        if (capsuleRootIsFootPoint)
        {
            // 项目标准：Unit/Player Root 是脚底逻辑点，胶囊自身用 center.y 表达身体半高。
            // 这样 TerrainGroundMotor 看到的 Collider bottom 会落在 Root Y，而不是要求 Root 被抬到半高。
            center.y = localHeight * 0.5f + capsuleBottomPadding / scaleY;
        }
        else
        {
            center.y = 0f;
        }
        collisionCapsule.center = center;

        if (movementController == null)
            movementController = GetComponent<UnitMovementController>();
        if (movementController != null)
            movementController.AssignMovementCapsule(collisionCapsule);

        if (terrainGroundMotor == null)
            terrainGroundMotor = GetComponent<TerrainGroundMotorV5>();
        if (terrainGroundMotor != null && configureTerrainGroundMotorBodyCollider)
            terrainGroundMotor.bodyColliderOverride = collisionCapsule;

        if (debugLogs || logCapsuleAutoCalibration)
        {
            Debug.Log(
                $"[UnitDefinitionRuntimeApplier] {name}: Auto calibrated CapsuleCollider from final visual bounds. reason={reason}, " +
                $"visualHeight={visualBounds.size.y:F3}, height={collisionCapsule.height:F3}, radius={collisionCapsule.radius:F3}, center={collisionCapsule.center}.",
                this
            );
        }

        return true;
    }

    private bool TryGetRealVisualWorldBounds(out Bounds result)
    {
        result = default(Bounds);

        Transform visualRoot = FindVisualRootForOcclusion();
        if (visualRoot == null)
            return false;

        Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
        bool hasAny = false;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null || !r.enabled)
                continue;

            if (!IsRealUnitVisualRenderer(r))
                continue;

            Bounds b = r.bounds;
            if (b.size.sqrMagnitude <= 0.000001f)
                continue;

            if (!hasAny)
            {
                result = b;
                hasAny = true;
            }
            else
            {
                result.Encapsulate(b);
            }
        }

        return hasAny;
    }

    private void ResetCapsuleTransformPitchRoll()
    {
        if (collisionCapsule == null)
            return;

        Transform t = collisionCapsule.transform;
        Vector3 euler = t.localRotation.eulerAngles;
        t.localRotation = Quaternion.Euler(0f, euler.y, 0f);
    }

    private void ApplyToShadowProjector(UnitDefinition ud)
    {
        if (!ud.lockShadowProjectorTransform)
            return;

        if (shadowProjector == null)
            shadowProjector = FindShadowProjector();

        if (shadowProjector == null)
            return;

        shadowProjector.localPosition = ud.shadowLocalPosition;
        shadowProjector.localRotation = Quaternion.Euler(ud.shadowLocalEuler);
        shadowProjector.localScale = ud.shadowLocalScale;
    }

    private void ApplyToMovement(UnitDefinition ud)
    {
        if (movementController == null)
            movementController = GetComponent<UnitMovementController>();

        if (movementController == null)
            return;

        movementController.ApplyMovementDefinition(
            ud.movementType,
            ud.idealWalkSpeed,
            ud.idealSneakSpeed,
            ud.idealRunSpeed,
            ud.movementInertia,
            ud.minWalkSpeed,
            ud.sustainedMoveSpeedMultiplier,
            ud.sustainedMoveDelay
        );

        movementController.ApplyJumpDefinition(
            ud.idealJumpHeight,
            ud.lightBurdenJumpHeightMultiplier,
            ud.mediumBurdenJumpHeightMultiplier,
            ud.heavyBurdenJumpHeightMultiplier,
            ud.overweightDisablesJump
        );

        // 动作动画 Key 以 UnitDefinition 为准。
        // 例如敌人可以在单位定义里把奔跑 Key 写成 run_heavy，
        // 运行时会同步覆盖 UnitMovementController 上的 idle / walk / run / sneak 等字段。
        movementController.ApplyAnimationDefinition(ud.animationKeys);

        // 同步到 SpineAnimationDriver_Current（攻击/受击/死亡 Key 等）
        if (ud.animationKeys != null)
        {
            SpineAnimationDriver_Current spineDriver =
                GetComponent<SpineAnimationDriver_Current>()
                ?? GetComponentInParent<SpineAnimationDriver_Current>()
                ?? GetComponentInChildren<SpineAnimationDriver_Current>(true);
            spineDriver?.ApplyAnimationDefinition(ud.animationKeys);
        }
    }

    private void ApplyToLoadRuntime(UnitDefinition ud)
    {
        if (loadRuntime == null)
            loadRuntime = GetComponent<UnitLoadRuntime>();

        if (loadRuntime == null || ud == null)
            return;

        loadRuntime.ApplyDefinition(ud);

        if (movementController != null)
            movementController.AssignLoadRuntime(loadRuntime);
    }

    private void ApplyToDeath(UnitDefinition ud)
    {
        if (deathController == null)
            deathController = GetComponent<UnitDeathController>();

        if (overheadView == null)
            overheadView = GetComponent<UnitOverheadUIView>() ?? GetComponentInChildren<UnitOverheadUIView>(true);

        if (overheadHealthBridge == null)
            overheadHealthBridge = GetComponent<UnitOverheadHealthBridge>() ?? GetComponentInChildren<UnitOverheadHealthBridge>(true);

        if (healthController == null)
            healthController = GetComponent<UnitHealthController>() ?? GetComponentInChildren<UnitHealthController>(true);

        if (deathController == null)
            return;

        deathController.ApplyDefinitionDefaults(
            ud.playDeathAnimation,
            ud.deathTriggerName,
            ud.useCorpseDecay,
            ud.corpseDecaySeconds,
            MapCleanupMode(ud.corpseCleanupMode),
            ud.treatAsRespawnablePlayer,
            ud.useDeathDissolve,
            ud.deathDissolveSeconds,
            ud.deathDissolveEdgeColor,
            ud.deathDissolveEdgeWidth,
            ud.deathDissolveNoiseTexture,
            ud.deathDissolveNoiseScale,
            ud.deathDissolveDarkenFraction
        );

        if (ud.dropProfiles != null && ud.dropProfiles.Count > 0)
        {
            if (deathDropController == null)
                deathDropController = GetComponent<UnitDeathDropController>();

            if (deathDropController == null)
                deathDropController = gameObject.AddComponent<UnitDeathDropController>();

            deathDropController.SetDropProfiles(ud.dropProfiles);
        }
    }

    private UnitDeathController.CorpseCleanupMode MapCleanupMode(UnitCorpseCleanupMode mode)
    {
        switch (mode)
        {
            case UnitCorpseCleanupMode.DisableObject:
                return UnitDeathController.CorpseCleanupMode.DisableObject;
            default:
                return UnitDeathController.CorpseCleanupMode.DestroyObject;
        }
    }


    private Collider FindMovementBodyCollider()
    {
        string rootName = string.IsNullOrWhiteSpace(autoCollisionRootName) ? "CollisionRoot" : autoCollisionRootName;
        Transform collisionRoot = FindDirectOrDeepChild(rootName);

        Collider collider = FindFirstUsableBodyCollider(collisionRoot);
        if (collider != null)
            return collider;

        if (collisionCapsule == null)
            collisionCapsule = FindCollisionCapsule();

        if (collisionCapsule != null && collisionCapsule.enabled && !collisionCapsule.isTrigger)
            return collisionCapsule;

        return FindFirstUsableBodyCollider(transform);
    }

    private Collider FindFirstUsableBodyCollider(Transform root)
    {
        if (root == null)
            return null;

        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider c = colliders[i];
            if (c == null || !c.enabled || c.isTrigger)
                continue;

            if (c.GetComponent<TerrainCollider>() != null)
                continue;

            return c;
        }

        return null;
    }


    private CapsuleCollider FindCollisionCapsule()
    {
        Transform collisionRoot = transform.Find("CollisionRoot");
        if (collisionRoot != null)
        {
            CapsuleCollider cap = collisionRoot.GetComponent<CapsuleCollider>();
            if (cap != null)
                return cap;
        }

        return GetComponentInChildren<CapsuleCollider>(true);
    }


    private Transform FindDirectOrDeepChild(string targetName)
    {
        if (string.IsNullOrWhiteSpace(targetName))
            return null;

        Transform direct = transform.Find(targetName);
        if (direct != null)
            return direct;

        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t != null && t.name == targetName)
                return t;
        }

        return null;
    }

    private static Transform FindDirectChild(Transform parent, string targetName)
    {
        if (parent == null || string.IsNullOrWhiteSpace(targetName))
            return null;

        return parent.Find(targetName);
    }

    private static void ResetLocalTransform(Transform target)
    {
        if (target == null)
            return;

        target.localPosition = Vector3.zero;
        target.localRotation = Quaternion.identity;
        target.localScale = Vector3.one;
    }

    private Transform FindShadowProjector()
    {
        Transform shadowRoot = transform.Find("ShadowRoot");
        if (shadowRoot != null)
        {
            Transform projector = shadowRoot.Find("Shadow_Projector");
            if (projector != null)
                return projector;
        }

        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "Shadow_Projector")
                return t;
        }

        return null;
    }

    private static OutlineState MapOutlineGroupToOutlineState(OutlineGroup group)
    {
        switch (group)
        {
            case OutlineGroup.Player: return OutlineState.Player;
            case OutlineGroup.Enemy: return OutlineState.Enemy;
            case OutlineGroup.Ally: return OutlineState.Ally;
            case OutlineGroup.Item: return OutlineState.Item;
            default: return OutlineState.None;
        }
    }

    private void EnsureCombatRuntimeForDefinition(UnitDefinition ud)
    {
        if (ud == null || !autoEnsureCombatModuleRuntimeForCharacterUnits)
            return;

        if (ud.defineType != UnitDefineType.Character)
            return;

        // ── UnitActionModuleRuntime ───────────────────────────────────────
        if (combatModuleRuntime == null)
            combatModuleRuntime = GetComponent<UnitActionModuleRuntime>();

        if (combatModuleRuntime == null)
        {
            combatModuleRuntime = gameObject.AddComponent<UnitActionModuleRuntime>();

            if (debugLogs)
                Debug.Log($"[UnitDefinitionRuntimeApplier] {name}: Auto added UnitActionModuleRuntime.", this);
        }

        Transform hitRoot = FindDirectOrDeepChild(hitRootName);
        if (hitRoot != null)
            combatModuleRuntime.SetHitboxParent(hitRoot);

        // UnitDefinition 指定的默认战斗模组（空手/待机姿态）
        if (ud != null && ud.defaultCombatModule != null)
            combatModuleRuntime.SetDefaultModule(ud.defaultCombatModule);

        // 把正确的 SkeletonAnimation 传给战斗模组（防止拿到 outline proxy 骨架）
        SkeletonAnimation bestAnim = FindBestFootstepSkeletonAnimation();
        if (bestAnim != null)
            combatModuleRuntime.SetSkeletonAnimation(bestAnim);

        // ── WeaponVisualRuntime（装备近战武器时切视觉+切判定来源）───────────
        if (GetComponent<WeaponVisualRuntime>() == null)
            gameObject.AddComponent<WeaponVisualRuntime>();

        // ── ArmorVisualRuntime（装备防具时切视觉，同一时间最多5件同时叠加）──
        if (GetComponent<ArmorVisualRuntime>() == null)
            gameObject.AddComponent<ArmorVisualRuntime>();

        // ── SkyPrisonSwordSlashTrailController（剑挥砍空气扭曲拖尾，试验版）──
        if (GetComponent<SkyPrisonSwordSlashTrailController>() == null)
            gameObject.AddComponent<SkyPrisonSwordSlashTrailController>();

        // ── BoneFollower Hitbox ───────────────────────────────────────────
        if (autoEnsureSpineBoundingBoxHitbox && hitRoot != null)
            EnsureSpineBoundingBoxHitbox(hitRoot);

        // ── UnitCombatHurtbox ─────────────────────────────────────────────
        if (combatHurtbox == null)
            combatHurtbox = GetComponentInChildren<UnitCombatHurtbox>(true);

        if (combatHurtbox == null)
        {
            Transform hurtboxRoot = FindDirectOrDeepChild(hurtboxRootName);
            Transform parent      = hurtboxRoot != null ? hurtboxRoot : transform;

            var go  = new GameObject("CombatHurtbox");
            go.transform.SetParent(parent, false);

            var col       = go.AddComponent<CapsuleCollider>();
            col.isTrigger = true;
            col.direction = 1;

            // 尝试从实际视觉包围盒定标，失败则用保守默认值
            if (!TryCalibrateCapsuleFromVisualBounds(col))
            {
                col.radius = 0.5f;
                col.height = 2.0f;
                col.center = new Vector3(0f, 1.0f, 0f);
            }

            // 视觉第一帧可能还没出来，延迟一帧再校准一次
            if (Application.isPlaying)
                StartCoroutine(CoRecalibrateHurtboxNextFrame(col));

            combatHurtbox = go.AddComponent<UnitCombatHurtbox>();

            UnitHealthController hc = GetComponent<UnitHealthController>()
                                   ?? GetComponentInChildren<UnitHealthController>(true);
            if (hc != null)
                combatHurtbox.SetHealthController(hc);

            if (debugLogs)
                Debug.Log($"[UnitDefinitionRuntimeApplier] {name}: Auto added UnitCombatHurtbox under {parent.name}.", this);
        }
    }

    private System.Collections.IEnumerator CoRecalibrateHurtboxNextFrame(CapsuleCollider cap)
    {
        yield return null; // 等一帧让 Spine 渲染出 bounds
        TryCalibrateCapsuleFromVisualBounds(cap);
    }

    private bool TryCalibrateCapsuleFromVisualBounds(CapsuleCollider cap)
    {
        if (cap == null) return false;

        Bounds b;
        if (!TryGetRealVisualWorldBounds(out b)) return false;

        // 把世界包围盒转到 cap 所在 Transform 的本地空间
        Transform t      = cap.transform;
        float scaleY     = Mathf.Abs(t.lossyScale.y);
        if (scaleY < 0.0001f) scaleY = 1f;

        float worldH     = Mathf.Max(0.1f, b.size.y);
        float localH     = worldH / scaleY;
        float localR     = Mathf.Max(0.1f, (Mathf.Max(b.size.x, b.size.z) * 0.5f) / scaleY);

        // 保证胶囊高度 >= 直径
        localH = Mathf.Max(localH, localR * 2f + 0.01f);

        cap.radius = localR;
        cap.height = localH;
        // 底部贴地（脚底 Y=0 约定）
        cap.center = new Vector3(0f, localH * 0.5f, 0f);

        return true;
    }

    // 排除 OutlineProxyRoot 底下那些描边代理骨骼（GameObject名字以"OutlineProxy"开头），
    // 找真正驱动角色可见动作的那一具 SkeletonAnimation。
    private SkeletonAnimation FindRealSkeletonAnimation()
    {
        var candidates = GetComponentsInChildren<SkeletonAnimation>(true);
        foreach (var candidate in candidates)
        {
            if (candidate == null) continue;
            if (candidate.gameObject.name.StartsWith("OutlineProxy", System.StringComparison.Ordinal)) continue;
            return candidate;
        }
        return null;
    }

    private void EnsureSpineBoundingBoxHitbox(Transform hitRoot)
    {
        // 已经建过这套骨骼追随 hitbox 则跳过——之前判断条件是"hitRoot 底下有没有任意
        // BoneFollower"，范围太大：武器判定挂件（WeaponVisualRuntime._weaponBBSourceGo）
        // 也会在同一个 hitRoot 底下临时挂一个 BoneFollower，只要装备过带判定插槽的武器，
        // 这个方法就会被误判"已经建过"直接跳过，实际上手部追随 Hitbox 从来没建立过——
        // 表现为角色一直用 UnitActionModuleRuntime.CreateHitbox() 那套写死位置的兜底
        // 胶囊体（焊死在角色根节点附近，不管挥拳动画怎么摆都不会跟着动，看起来像焊在
        // 膝盖/脚下）。改成只认自己建的这个物体名字，不会被别的系统的 BoneFollower
        // 误伤。
        if (hitRoot.Find("CombatHitbox_Hand") != null)
            return;

        // GetComponentInChildren<SkeletonAnimation>(true) 曾经直接抓第一个找到的——但
        // 角色prefab里还有一组"遮挡描边代理"骨骼（OutlineProxyRoot 底下的
        // OutlineProxy_xxx_Occluded，被遮挡时才临时启用、用来画描边轮廓），它们是完全
        // 独立的 SkeletonAnimation 实例（不同缩放、默认禁用、姿势不跟随真实动作），
        // 且 OutlineProxyRoot 在层级里排在真正的 SpineRoot 前面，导致 GetComponentInChildren
        // 优先抓到这个静止的描边代理，把 BoneFollower/BoundingBoxFollower 绑到一具没有在
        // 播放攻击动画的骨骼上——hitbox 位置跟真实挥砍动作完全脱节（表现为焊在角色脚底
        // 附近），攻击永远打不中。改成显式跳过名字以"OutlineProxy"开头的候选，找真正在
        // 驱动角色动作的那一具。
        SkeletonAnimation skAnim = FindRealSkeletonAnimation();
        if (skAnim == null || skAnim.skeletonDataAsset == null)
            return;

        var skData = skAnim.skeletonDataAsset.GetSkeletonData(true);
        if (skData == null)
            return;

        // 在骨骼列表里找右手骨骼（优先 R_hand，其次任意 hand）
        //
        // 之前只搜 Bones 列表——这只怪（Memento_homingOne）的骨架里右手根本没有叫
        // "xxx_R_hand" 的骨骼，"arm_R_hand" 只是一个 Slot（插槽）名字，真正驱动右手的
        // 骨骼叫 "arm_R_upper3"，完全匹配不到"hand"这个词。结果只搜 Bones 会落到唯一
        // 命中的 "arm_L_hand"（左手骨骼）身上——攻击动画甩的是右手，Hitbox 却焊在没动的
        // 左手上，表现为"贴脸也打不到"，红色 Gizmo 圈还老待在身体/脚附近不动。
        // 这里改成先查 Slots（插槽名更贴近"这是不是手"的语义），命中就取该插槽绑定的
        // 骨骼 BoneData.Name；查不到插槽再退回 Bones 列表兜底。
        string boneName = null;
        var slots = skData.Slots;
        for (int i = 0; i < slots.Count; i++)
        {
            string n = slots.Items[i].Name;
            if (n.Contains("R_hand") || n.Contains("r_hand"))
            {
                boneName = slots.Items[i].BoneData?.Name;
                break;
            }
        }
        if (boneName == null)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots.Items[i].Name.ToLower().Contains("hand"))
                {
                    boneName = slots.Items[i].BoneData?.Name;
                    break;
                }
            }
        }

        var bones = skData.Bones;
        if (boneName == null)
        {
            for (int i = 0; i < bones.Count; i++)
            {
                string n = bones.Items[i].Name;
                if (n.Contains("R_hand") || n.Contains("r_hand")) { boneName = n; break; }
            }
        }
        if (boneName == null)
        {
            for (int i = 0; i < bones.Count; i++)
            {
                if (bones.Items[i].Name.ToLower().Contains("hand")) { boneName = bones.Items[i].Name; break; }
            }
        }

        if (boneName == null)
        {
            if (debugLogs)
                Debug.LogWarning($"[UnitDefinitionRuntimeApplier] {name}: 找不到 hand 骨骼，无法自动建 Hitbox。", this);
            return;
        }

        var go = new GameObject("CombatHitbox_Hand");
        go.transform.SetParent(hitRoot, false);

        // BoneFollower 逐帧跟骨骼位置（不依赖 BoundingBoxFollower 的延迟初始化）
        var boneFollower              = go.AddComponent<BoneFollower>();
        boneFollower.skeletonRenderer = skAnim.GetComponent<SkeletonRenderer>();
        boneFollower.boneName         = boneName;
        boneFollower.followBoneRotation = false;
        boneFollower.followZPosition    = false;

        // Kinematic Rigidbody：3D trigger-trigger 检测要求至少一方有 Rigidbody
        var rb             = go.AddComponent<Rigidbody>();
        rb.isKinematic     = true;
        rb.useGravity      = false;
        rb.interpolation   = RigidbodyInterpolation.Interpolate; // 平滑跟骨骼，减少一帧延迟

        // Z 轴方向的 CapsuleCollider：XY 是拳头大小，Z 轴拉长覆盖所有可能的场景深度差。
        // 注意 direction=2 时 X/Y 共用同一个 radius——也就是说这个半径同时决定了左右容错
        // 和上下容错。之前 0.55 是按角色体型差不多的情况调的，遇到体型差很大的敌人
        // （比如这只比玩家高一大截）手骨骼实际抬到的高度跟矮小目标的判定体对不上，
        // 差值一旦超过 0.55 就会出现"横向明明贴脸了、纵向差一截"导致的挥空——这正是
        // 体型差异大的组合特别容易复现的"上下打不到"问题。适当放宽半径换取更大的高度
        // 容错（连带横向容错也会变宽，但这是动作游戏可以接受的判定手感）。
        var col       = go.AddComponent<CapsuleCollider>();
        col.isTrigger = true;
        col.direction = 2;      // Z 轴方向
        col.radius    = 0.85f;  // 拳头半径（XY 面，含上下容错）
        col.height    = 2.5f;   // Z 轴覆盖范围

        var hitbox = go.AddComponent<UnitCombatHitbox>();
        hitbox.SetFacingRoot(transform);
        hitbox.SetOwner(gameObject); // gameObject = 实际单位对象，不是场景根节点

        // 美术标准从这次起统一：手部攻击判定用 boundingbox 附件描边（插槽名约定为
        // "arm_R_hand2"/"arm_L_hand2"，照抄角色 Axia_base 的命名习惯），比固定半径圆
        // 精确得多——附件形状会跟着挥拳动画实时形变，不用再猜半径够不够盖住目标。
        // 找不到就退回上面那个"骨骼位置+写死胶囊体半径"的兜底方案，不强制要求每个
        // 角色都必须画这个附件。
        //
        // 不能挂在 go（CombatHitbox_Hand）自己身上——这个物体已经有 3D Rigidbody（给
        // CapsuleCollider触发检测用），Unity 不允许同一物体上同时挂3D Rigidbody和2D
        // Collider，BoundingBoxFollower 内部生成 PolygonCollider2D 会被 Unity 直接拒绝
        // （Console 报"conflicts with the existing 'Rigidbody' derived component"）。
        //
        // 也不能挂在骨架根节点（skAnim.gameObject）上——SkeletonExtensions.
        // GetLocalVertices 算出来的顶点是相对"这个插槽绑定的那根骨骼"的本地空间坐标
        // （非weighted附件直接返回 va.Vertices，本来就是骨骼局部坐标；weighted附件
        // 也是拿世界坐标反乘骨骼的 WorldToLocalMatrix 转回骨骼局部坐标），根本不是
        // 相对骨架根节点——挂在根节点上，形状会出现在骨架原点附近而不是手上。
        // 必须挂在一个逐帧跟着"这根骨骼"走的物体上（位置+旋转都要跟，否则角色转身/
        // 抬手时形状朝向跟不上），所以新建一个独立的 BoneFollower 物体，不共用
        // CombatHitbox_Hand（避免Rigidbody冲突），也不共用骨架根节点（避免坐标系不对）。
        Slot boundingBoxSlot = FindHandBoundingBoxSlot(skAnim.Skeleton);
        if (boundingBoxSlot != null)
        {
            var bbGo = new GameObject("CombatHitbox_HandBBSource");
            bbGo.transform.SetParent(hitRoot, false);

            var bbBoneFollower = bbGo.AddComponent<BoneFollower>();
            bbBoneFollower.skeletonRenderer   = skAnim.GetComponent<SkeletonRenderer>();
            bbBoneFollower.boneName           = boundingBoxSlot.Data.BoneData?.Name ?? boneName;
            bbBoneFollower.followBoneRotation = true; // 形状顶点是骨骼局部坐标，旋转跟不上会导致挥拳/转身时判定框朝向错
            bbBoneFollower.followZPosition    = false;

            var bbFollower = bbGo.AddComponent<BoundingBoxFollower>();
            bbFollower.skeletonRenderer = skAnim.GetComponent<SkeletonRenderer>();
            bbFollower.slotName = boundingBoxSlot.Data.Name;
            bbFollower.isTrigger = true;
            bbFollower.Initialize();
            hitbox.SetBoundingBoxSource(bbFollower);

            if (debugLogs)
                Debug.Log($"[UnitDefinitionRuntimeApplier] {name}: 使用 boundingbox 插槽 '{boundingBoxSlot.Data.Name}' 驱动手部判定。", this);
        }

        if (combatModuleRuntime != null)
            combatModuleRuntime.SetHitbox(hitbox);

        if (debugLogs)
            Debug.Log($"[UnitDefinitionRuntimeApplier] {name}: BoneFollower Hitbox 建立完成。bone={boneName}", this);
    }

    // 优先找右手（R_hand2），其次任意含 hand2 的插槽，且必须真的挂了 boundingbox 附件
    // （不是随便叫这个名字的普通贴图插槽）。
    private Slot FindHandBoundingBoxSlot(Skeleton skeleton)
    {
        if (skeleton == null) return null;

        Slot fallback = null;
        var slots = skeleton.Slots;
        for (int i = 0; i < slots.Count; i++)
        {
            Slot slot = slots.Items[i];
            if (slot == null || !(slot.Pose.Attachment is BoundingBoxAttachment)) continue;

            string n = slot.Data.Name;
            if (n.Contains("R_hand2") || n.Contains("r_hand2"))
                return slot;

            if (fallback == null && n.ToLower().Contains("hand2"))
                fallback = slot;
        }

        return fallback;
    }
}
