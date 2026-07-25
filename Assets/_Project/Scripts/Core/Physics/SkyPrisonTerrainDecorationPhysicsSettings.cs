using UnityEngine;

public enum SkyPrisonPushableColliderBuildMode
{
    // 兼容旧资产：直接使用视觉 Mesh / 自定义 Mesh 生成 Convex MeshCollider。
    ConvexMesh = 0,

    // 正式推荐：按视觉模型生成低面数、贴底、轻微内缩的物理 Hull。
    AutoFittedPhysics = 1,

    // 高价值资产可指定专用物理 Mesh。
    CustomPhysicsMesh = 2
}

[CreateAssetMenu(
    fileName = "TerrainDecorationPhysicsSettings",
    menuName = "Sky Prison/Terrain Decoration Physics Settings",
    order = 1215)]
public class SkyPrisonTerrainDecorationPhysicsSettings : ScriptableObject
{
    [Header("Identity")]
    public string decorationId = "";
    public string displayName = "";

    [Header("Structure")]
    public bool enablePhysicsStructure = false;

    [Header("Collision Response")]
    public bool receiveVolumeCollision = false;
    public bool receiveAttackImpulse = true;
    public bool receiveExplosionImpulse = true;
    public bool receiveScriptedImpulse = true;

    [Header("Collider Build")]
    public SkyPrisonPushableColliderBuildMode pushableColliderBuildMode = SkyPrisonPushableColliderBuildMode.ConvexMesh;
    public Mesh customPhysicsMesh;
    public bool autoPickLargestVisibleMesh = true;
    public bool forceConvexMeshCollider = true;
    public string pushableLayerName = "PushableProp";

    [Header("Auto Fitted Physics Hull")]
    [Range(0.78f, 1.0f)] public float fittedHullShrink = 0.94f;
    [Range(4, 12)] public int fittedHullHeightSlices = 6;
    [Range(8, 20)] public int fittedHullRadialSegments = 12;
    [Range(0.02f, 0.25f)] public float fittedHullSliceSearchPadding = 0.08f;
    [Range(0.01f, 0.3f)] public float fittedHullMinimumRadius = 0.035f;
    public bool fittedHullForceBottomToVisualBottom = true;
    public bool fittedHullUseBottomFootprint = true;
    public PhysicsMaterial pushablePhysicMaterial;
    public bool useGeneratedLowFrictionMaterial = true;

    [Header("Movement Plane Defaults")]
    public bool useHorizontalGroundPlane = true;

    [Header("Rigidbody Defaults")]
    public float mass = 0.35f;
    public float linearDamping = 6f;
    public float angularDamping = 9f;
    public float maxPlanarSpeed = 3.5f;

    [Header("Push Defaults")]
    public float externalPushMultiplier = 2.2f;
    public bool applyForceAtTop = true;
    public float topForceHeight = 0.9f;
    public float topForceMultiplier = 2.0f;

    [Header("Paper Doll Push Defaults")]
    public bool paperDollPushMode = true;
    public bool paperDollTriggerKnockdownImmediately = true;
    public bool paperDollSuppressPhysicsForce = true;
    public bool paperDollKeepKinematicAfterTip = true;
    public bool paperDollAllowPushAfterKnockdown = true;
    [Range(0.05f, 2f)] public float paperDollPostKnockdownPushMultiplier = 0.85f;
    public bool paperDollPostKnockdownUseRealPhysics = true;
    public bool paperDollPostKnockdownForceAtEdge = true;
    [Range(0f, 4f)] public float paperDollPostKnockdownTorqueMultiplier = 1.2f;
    public float paperDollPostKnockdownLinearDamping = 2.0f;
    public float paperDollPostKnockdownAngularDamping = 1.2f;
    public bool paperDollUseGravityWhenPushedAfterKnockdown = true;
    public float paperDollPostKnockdownMinimumImpulse = 0.65f;
    public float paperDollPostKnockdownMinimumPlanarSpeed = 1.15f;
    public bool paperDollApplyGroundProtectionAfterPostPush = false;

    [Header("Real Physics Authority Defaults")]
    public bool realPhysicsAuthorityAfterKnockdown = true;
    public bool realPhysicsReleaseAfterPivotTip = true;
    public bool realPhysicsKeepDynamicWhenStable = true;
    public bool realPhysicsSkipGroundProtection = true;
    public bool realPhysicsSkipPlanarSpeedLimit = true;
    public bool realPhysicsAllowUnitBodyCollision = true;
    public bool realPhysicsUseGravity = true;
    public bool realPhysicsUseOffCenterImpulse = true;
    public bool realPhysicsUseExtraScriptTorque = false;
    [Range(0f, 4f)] public float realPhysicsExtraTorqueMultiplier = 0f;
    public float realPhysicsLinearDamping = 0.45f;
    public float realPhysicsAngularDamping = 0.35f;
    public float realPhysicsMaxAngularVelocity = 14f;
    public float realPhysicsMaxDepenetrationVelocity = 3f;
    public bool realPhysicsDisableMinimumPlanarVelocity = true;

    [Header("Knockdown / Ground Protection")]
    public bool enableKnockdown = true;
    public bool protectAfterPivotRelease = true;
    public bool useLastKnownGroundWhenRayMisses = true;
    public bool useFallbackGroundPlaneWhenRayMisses = false;
    public float fallbackGroundY = 0f;
}
