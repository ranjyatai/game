using UnityEngine;

/// <summary>
/// Keeps a unit ground shadow/projector under the unit on the actual ground,
/// while still allowing the shadow object to remain inside the Player hierarchy.
///
/// V11 - 2026-06-06
/// - Shadow mask is no longer globally disabled by a coarse self-depth gate.
/// - The projector always feeds its own per-pixel shadow occluder mask to the shader.
/// - Carrier depth is still bound so the shader can decide pixel-by-pixel whether a visible surface is in front of the shadow carrier.
/// - Character FrontOccluder / BackTrigger rules are not touched.
///
/// Attach this to ShadowRoot. Set Follow Target to Player.
/// </summary>
[DefaultExecutionOrder(32000)]
[DisallowMultipleComponent]
public sealed class UnitGroundShadowFollowerTerrain : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform followTarget;

    [Header("Ground")]
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private bool preferTerrainSampleHeight = true;
    [SerializeField] private float probeHeight = 8f;
    [SerializeField] private float probeDistance = 30f;
    [SerializeField] private float groundOffset = 0.02f;

    [Header("Follow")]
    [SerializeField] private bool followX = true;
    [SerializeField] private bool followZ = true;
    [SerializeField] private bool keepLocalRotation = true;
    [SerializeField] private bool keepLocalScale = true;

    [Header("Render Order")]
    [SerializeField] private bool forceStableRenderOrder = true;
    [SerializeField] private int shadowRenderQueue = 2920;
    [SerializeField] private bool forceRendererSortingOrder = false;
    [SerializeField] private string shadowSortingLayerName = "Character2D";
    [SerializeField] private int shadowSortingOrder = 80;

    [Header("Occlusion Mask Binding")]
    [SerializeField] private bool bindGlobalFrontOccluderMask = true;

    [Tooltip("Primary global texture name published by the main-camera occlusion feature.")]
    [SerializeField] private string globalOccluderMaskName = "_SkyPrison_GlobalShadowOccluderMask";

    [Tooltip("Optional fallback global texture name. Leave empty if unused.")]
    [SerializeField] private string fallbackGlobalOccluderMaskName = "RT_MainCameraShadowOccluderMask_All";

    [SerializeField] private string materialOcclusionTextureName = "_OcclusionTex";
    [SerializeField] private string materialUseOcclusionMaskName = "_UseOcclusionMask";
    [SerializeField] private string materialInsideMaskAlphaName = "_InsideMaskAlpha";
    [SerializeField] private string materialMaskDilatePixelsName = "_MaskDilatePixels";
    [SerializeField] private string materialMaskThresholdName = "_MaskThreshold";
    [SerializeField] private string materialMaskSoftnessName = "_MaskSoftness";
    [SerializeField] private string materialUseGlobalShadowOccluderMaskName = "_UseGlobalShadowOccluderMask";

    [SerializeField] private float useOcclusionMask = 1f;
    [SerializeField] private float insideMaskAlpha = 0f;
    [SerializeField] private float maskDilatePixels = 1f;
    [SerializeField] private float maskThreshold = 0.5f;
    [SerializeField] private float maskSoftness = 0.02f;
    [SerializeField] private float useGlobalShadowOccluderMask = 1f;

    [Header("Shadow Self Depth Gate - diagnostic only")]
    [Tooltip("Diagnostic only in V11. This no longer disables the projector mask globally. The shader receives the mask every frame and decides per pixel with carrier depth.")]
    [SerializeField] private bool useShadowSelfDepthGate = true;

    [SerializeField] private string materialShadowSelfDepthEstablishedName = "_ShadowSelfDepthEstablished";

    [Tooltip("Viewport padding applied to projected shadow and occluder rectangles before checking whether the shadow has entered a foreground relationship.")]
    [SerializeField] private float shadowSelfDepthViewportPadding = 0.006f;

    [Tooltip("Foreground occluder must be this much closer to camera than the shadow carrier depth to establish shadow-behind relationship.")]
    [SerializeField] private float shadowSelfDepthBias = 0.018f;

    [Tooltip("Keep shadow-behind relationship for a few frames after a positive hit to avoid edge flicker.")]
    [SerializeField] private float shadowSelfDepthHoldSeconds = 0.10f;

    [Tooltip("If ON, use shadow renderer bounds in Main Camera viewport. This lets the whole shadow footprint, not only the center point, establish the relation.")]
    [SerializeField] private bool useShadowRendererBoundsForDepthGate = true;

    [Header("Carrier Depth Binding - shadow only")]
    [Tooltip("Pass the actual ground/carrier depth to the projector shader. This lets the shadow suppress itself behind foreground props without affecting character occlusion rules.")]
    [SerializeField] private bool bindCarrierDepthToMaterial = true;

    [SerializeField] private Camera carrierDepthCamera;
    [SerializeField] private string materialUseSceneDepthForegroundGuardName = "_UseSceneDepthForegroundGuard";
    [SerializeField] private string materialKeepShadowWhenSceneDepthInvalidName = "_KeepShadowWhenSceneDepthInvalid";
    [SerializeField] private string materialUseCarrierDepthGuardName = "_UseShadowCarrierDepthGuard";
    [SerializeField] private string materialCarrierEyeDepthName = "_ShadowCarrierEyeDepth";
    [SerializeField] private string materialCarrierWorldPositionName = "_ShadowCarrierWorldPosition";
    [SerializeField] private string materialCarrierDepthBiasName = "_CarrierDepthForegroundBias";
    [SerializeField] private string materialCarrierDepthSoftnessName = "_CarrierDepthForegroundSoftness";

    [SerializeField] private float useSceneDepthForegroundGuard = 1f;
    [SerializeField] private float keepShadowWhenSceneDepthInvalid = 1f;
    [SerializeField] private float useCarrierDepthGuard = 1f;
    [SerializeField] private float carrierDepthForegroundBias = 0.025f;
    [SerializeField] private float carrierDepthForegroundSoftness = 0.018f;

    [Header("Projection Occlusion Authority - diagnostic only")]
    [Tooltip("OFF by default. V4 used this to toggle the entire shadow mask from a few sample points, but that is too coarse for commercial edge quality. Leave OFF so the shader clips the shadow per pixel with the global shadow occluder mask.")]
    [SerializeField] private bool useProjectionOcclusionAuthority = false;

    [SerializeField] private Camera authorityCamera;
    [SerializeField] private string fallbackCameraName = "Main Camera";
    [SerializeField] private string shadowOccluderLayerName = "OcclusionMask";
    [SerializeField] private LayerMask shadowOccluderLayerMask = 0;

    [Tooltip("Renderer paths containing these keywords will not be considered as foreground shadow occluders.")]
    [SerializeField] private string authorityRejectPathKeywords = "outline;shadow;canvas;ui;character;player;enemy;ally;item";

    [SerializeField] private bool authorityRequireRendererEnabled = true;
    [SerializeField] private bool authorityRequireActiveInHierarchy = true;

    [Tooltip("World-space radius used for the four extra sample points around the shadow center.")]
    [SerializeField] private float authoritySampleRadius = 0.36f;

    [Tooltip("Viewport padding added to each occluder bounds rect. Helps avoid one-frame edge misses.")]
    [SerializeField] private float authorityScreenPadding = 0.018f;

    [Tooltip("Occluder must be at least this much closer to camera than the shadow sample to count as foreground.")]
    [SerializeField] private float authorityDepthBias = 0.04f;

    [Tooltip("How often to refresh the list of foreground occluder renderers.")]
    [SerializeField] private float authorityRendererRefreshInterval = 0.15f;

    [Tooltip("Keep the occlusion authority positive for a tiny moment after a hit. This prevents edge flicker around layered props.")]
    [SerializeField] private float authorityPositiveHoldSeconds = 0.10f;

    [Header("Auto Collect Renderers")]
    [SerializeField] private bool autoCollectRenderers = true;
    [SerializeField] private Renderer[] shadowRenderers;

    [Header("Debug")]
    [SerializeField] private bool drawDebug = false;
    [SerializeField, TextArea(2, 4)] private string lastStatus = "";

    private Quaternion initialLocalRotation;
    private Vector3 initialLocalScale;
    private MaterialPropertyBlock propertyBlock;

    private int globalOccluderMaskId;
    private int fallbackGlobalOccluderMaskId;
    private int materialOcclusionTextureId;
    private int materialUseOcclusionMaskId;
    private int materialInsideMaskAlphaId;
    private int materialMaskDilatePixelsId;
    private int materialMaskThresholdId;
    private int materialMaskSoftnessId;
    private int materialUseGlobalShadowOccluderMaskId;
    private int materialShadowSelfDepthEstablishedId;
    private int materialUseSceneDepthForegroundGuardId;
    private int materialKeepShadowWhenSceneDepthInvalidId;
    private int materialUseCarrierDepthGuardId;
    private int materialCarrierEyeDepthId;
    private int materialCarrierWorldPositionId;
    private int materialCarrierDepthBiasId;
    private int materialCarrierDepthSoftnessId;

    private Vector3 lastCarrierWorldPosition;
    private float lastCarrierEyeDepth;
    private bool hasCarrierDepth;

    private float shadowSelfDepthPositiveUntil = -999f;
    private bool lastShadowSelfDepthEstablished = false;
    private string lastShadowSelfDepthReason = "-";

    private readonly System.Collections.Generic.List<Renderer> authorityOccluderRenderers = new System.Collections.Generic.List<Renderer>(96);
    private readonly Vector3[] authoritySamplePoints = new Vector3[5];
    private float nextAuthorityRendererRefreshTime = -999f;
    private float authorityPositiveUntil = -999f;
    private bool lastAuthorityGate = true;
    private string lastAuthorityReason = "-";

    private void Awake()
    {
        initialLocalRotation = transform.localRotation;
        initialLocalScale = transform.localScale;

        if (followTarget == null)
            followTarget = FindLikelyUnitRoot();

        CacheShaderIds();
        EnsurePropertyBlock();
        RefreshRenderersIfNeeded();
        ApplyRenderOrderSettings();
        ApplyShadowMaterialBinding();
    }

    private void OnEnable()
    {
        CacheShaderIds();
        EnsurePropertyBlock();
        RefreshRenderersIfNeeded();
        ApplyRenderOrderSettings();
        ApplyFollow();
        ApplyShadowMaterialBinding();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        CacheShaderIds();
        EnsurePropertyBlock();
        RefreshRenderersIfNeeded();
        ApplyRenderOrderSettings();
        ApplyShadowMaterialBinding();
    }
#endif

    private void LateUpdate()
    {
        ApplyFollow();
        ApplyShadowMaterialBinding();
    }

    private void CacheShaderIds()
    {
        ResolveDefaultShadowOccluderLayerMask();
        globalOccluderMaskId = Shader.PropertyToID(string.IsNullOrWhiteSpace(globalOccluderMaskName) ? "_SkyPrison_GlobalShadowOccluderMask" : globalOccluderMaskName);
        fallbackGlobalOccluderMaskId = Shader.PropertyToID(string.IsNullOrWhiteSpace(fallbackGlobalOccluderMaskName) ? "RT_MainCameraShadowOccluderMask_All" : fallbackGlobalOccluderMaskName);
        materialOcclusionTextureId = Shader.PropertyToID(string.IsNullOrWhiteSpace(materialOcclusionTextureName) ? "_OcclusionTex" : materialOcclusionTextureName);
        materialUseOcclusionMaskId = Shader.PropertyToID(string.IsNullOrWhiteSpace(materialUseOcclusionMaskName) ? "_UseOcclusionMask" : materialUseOcclusionMaskName);
        materialInsideMaskAlphaId = Shader.PropertyToID(string.IsNullOrWhiteSpace(materialInsideMaskAlphaName) ? "_InsideMaskAlpha" : materialInsideMaskAlphaName);
        materialMaskDilatePixelsId = Shader.PropertyToID(string.IsNullOrWhiteSpace(materialMaskDilatePixelsName) ? "_MaskDilatePixels" : materialMaskDilatePixelsName);
        materialMaskThresholdId = Shader.PropertyToID(string.IsNullOrWhiteSpace(materialMaskThresholdName) ? "_MaskThreshold" : materialMaskThresholdName);
        materialMaskSoftnessId = Shader.PropertyToID(string.IsNullOrWhiteSpace(materialMaskSoftnessName) ? "_MaskSoftness" : materialMaskSoftnessName);
        materialUseGlobalShadowOccluderMaskId = Shader.PropertyToID(string.IsNullOrWhiteSpace(materialUseGlobalShadowOccluderMaskName) ? "_UseGlobalShadowOccluderMask" : materialUseGlobalShadowOccluderMaskName);
        materialShadowSelfDepthEstablishedId = Shader.PropertyToID(string.IsNullOrWhiteSpace(materialShadowSelfDepthEstablishedName) ? "_ShadowSelfDepthEstablished" : materialShadowSelfDepthEstablishedName);
        materialUseSceneDepthForegroundGuardId = Shader.PropertyToID(string.IsNullOrWhiteSpace(materialUseSceneDepthForegroundGuardName) ? "_UseSceneDepthForegroundGuard" : materialUseSceneDepthForegroundGuardName);
        materialKeepShadowWhenSceneDepthInvalidId = Shader.PropertyToID(string.IsNullOrWhiteSpace(materialKeepShadowWhenSceneDepthInvalidName) ? "_KeepShadowWhenSceneDepthInvalid" : materialKeepShadowWhenSceneDepthInvalidName);
        materialUseCarrierDepthGuardId = Shader.PropertyToID(string.IsNullOrWhiteSpace(materialUseCarrierDepthGuardName) ? "_UseShadowCarrierDepthGuard" : materialUseCarrierDepthGuardName);
        materialCarrierEyeDepthId = Shader.PropertyToID(string.IsNullOrWhiteSpace(materialCarrierEyeDepthName) ? "_ShadowCarrierEyeDepth" : materialCarrierEyeDepthName);
        materialCarrierWorldPositionId = Shader.PropertyToID(string.IsNullOrWhiteSpace(materialCarrierWorldPositionName) ? "_ShadowCarrierWorldPosition" : materialCarrierWorldPositionName);
        materialCarrierDepthBiasId = Shader.PropertyToID(string.IsNullOrWhiteSpace(materialCarrierDepthBiasName) ? "_CarrierDepthForegroundBias" : materialCarrierDepthBiasName);
        materialCarrierDepthSoftnessId = Shader.PropertyToID(string.IsNullOrWhiteSpace(materialCarrierDepthSoftnessName) ? "_CarrierDepthForegroundSoftness" : materialCarrierDepthSoftnessName);
    }

    private void EnsurePropertyBlock()
    {
        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();
    }

    private void RefreshRenderersIfNeeded()
    {
        if (!autoCollectRenderers)
            return;

        shadowRenderers = GetComponentsInChildren<Renderer>(true);
    }

    private Transform FindLikelyUnitRoot()
    {
        Transform current = transform.parent;
        while (current != null)
        {
            if (current.GetComponent<TerrainGroundMotorV5>() != null || current.name == "Player")
                return current;

            current = current.parent;
        }

        return transform.root;
    }

    private void ApplyFollow()
    {
        if (followTarget == null)
            return;

        Vector3 targetPos = followTarget.position;
        Vector3 nextWorldPos = transform.position;

        if (followX)
            nextWorldPos.x = targetPos.x;

        if (followZ)
            nextWorldPos.z = targetPos.z;

        if (TryGetGroundY(targetPos, out float groundY))
            nextWorldPos.y = groundY + groundOffset;

        lastCarrierWorldPosition = nextWorldPos;
        UpdateCarrierEyeDepth(lastCarrierWorldPosition);

        // This writes world position. If this object is still a child of Player,
        // Unity will automatically convert it into the required localPosition,
        // cancelling the parent's jump Y offset.
        transform.position = nextWorldPos;

        if (keepLocalRotation)
            transform.localRotation = initialLocalRotation;

        if (keepLocalScale)
            transform.localScale = initialLocalScale;
    }

    private void UpdateCarrierEyeDepth(Vector3 carrierWorldPosition)
    {
        hasCarrierDepth = false;

        Camera cam = carrierDepthCamera != null ? carrierDepthCamera : Camera.main;
        if (cam == null && !string.IsNullOrWhiteSpace(fallbackCameraName))
        {
            Camera[] cameras = Camera.allCameras;
            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] != null && cameras[i].name == fallbackCameraName)
                {
                    cam = cameras[i];
                    break;
                }
            }
        }

        if (cam == null)
        {
            lastCarrierEyeDepth = 0f;
            return;
        }

        Vector3 view = cam.worldToCameraMatrix.MultiplyPoint(carrierWorldPosition);
        lastCarrierEyeDepth = Mathf.Max(0.0001f, -view.z);
        hasCarrierDepth = true;
    }

    private void ApplyRenderOrderSettings()
    {
        if (shadowRenderers == null || shadowRenderers.Length == 0)
            return;

        for (int i = 0; i < shadowRenderers.Length; i++)
        {
            Renderer r = shadowRenderers[i];
            if (r == null)
                continue;

            if (forceRendererSortingOrder)
            {
                if (!string.IsNullOrWhiteSpace(shadowSortingLayerName))
                    r.sortingLayerName = shadowSortingLayerName;
                r.sortingOrder = shadowSortingOrder;
            }

            if (!forceStableRenderOrder)
                continue;

            Material[] mats = r.sharedMaterials;
            if (mats == null)
                continue;

            for (int m = 0; m < mats.Length; m++)
            {
                Material mat = mats[m];
                if (mat == null)
                    continue;

                mat.renderQueue = shadowRenderQueue;
            }
        }
    }

    private void ApplyShadowMaterialBinding()
    {
        if (shadowRenderers == null || shadowRenderers.Length == 0)
            return;

        Texture occluderMask = null;
        if (bindGlobalFrontOccluderMask)
        {
            occluderMask = Shader.GetGlobalTexture(globalOccluderMaskId);
            if (occluderMask == null && fallbackGlobalOccluderMaskId != 0)
                occluderMask = Shader.GetGlobalTexture(fallbackGlobalOccluderMaskId);
        }

        // V5 rule:
        // Do NOT toggle the whole projector by a few world-space samples. That makes edge cases
        // look non-pixel-accurate. The projector shader already samples _OcclusionTex per pixel,
        // so keep the mask enabled whenever a global shadow occluder mask is bound.
        // Projection authority is retained only as an optional diagnostic override.
        bool authorityAllowsMask = ShouldApplyProjectionOcclusionMask();

        // V11 rule:
        // Do not use a coarse whole-shadow depth gate to disable the mask. That causes the shadow
        // to inherit the character's "not occluded yet" feeling at hard prop edges. The mask must
        // stay available every frame; the shader decides per pixel by comparing the carrier depth
        // against the Main Camera depth / shadow occluder mask.
        bool shadowDepthEstablished = ShouldEstablishShadowSelfDepthGate();

        float effectiveUseOcclusionMask = bindGlobalFrontOccluderMask ? useOcclusionMask : 0f;
        if (useProjectionOcclusionAuthority && !authorityAllowsMask)
            effectiveUseOcclusionMask = 0f;

        int boundCount = 0;
        for (int i = 0; i < shadowRenderers.Length; i++)
        {
            Renderer r = shadowRenderers[i];
            if (r == null)
                continue;

            r.GetPropertyBlock(propertyBlock);

            propertyBlock.SetFloat(materialUseOcclusionMaskId, effectiveUseOcclusionMask);
            propertyBlock.SetFloat(materialInsideMaskAlphaId, insideMaskAlpha);
            propertyBlock.SetFloat(materialMaskDilatePixelsId, maskDilatePixels);
            propertyBlock.SetFloat(materialMaskThresholdId, maskThreshold);
            propertyBlock.SetFloat(materialMaskSoftnessId, maskSoftness);
            propertyBlock.SetFloat(materialUseGlobalShadowOccluderMaskId, useGlobalShadowOccluderMask);
            propertyBlock.SetFloat(materialShadowSelfDepthEstablishedId, shadowDepthEstablished ? 1f : 0f);

            if (bindCarrierDepthToMaterial && hasCarrierDepth)
            {
                propertyBlock.SetFloat(materialUseSceneDepthForegroundGuardId, useSceneDepthForegroundGuard);
                propertyBlock.SetFloat(materialKeepShadowWhenSceneDepthInvalidId, keepShadowWhenSceneDepthInvalid);
                propertyBlock.SetFloat(materialUseCarrierDepthGuardId, useCarrierDepthGuard);
                propertyBlock.SetFloat(materialCarrierEyeDepthId, lastCarrierEyeDepth);
                propertyBlock.SetVector(materialCarrierWorldPositionId, new Vector4(lastCarrierWorldPosition.x, lastCarrierWorldPosition.y, lastCarrierWorldPosition.z, 1f));
                propertyBlock.SetFloat(materialCarrierDepthBiasId, carrierDepthForegroundBias);
                propertyBlock.SetFloat(materialCarrierDepthSoftnessId, carrierDepthForegroundSoftness);
            }
            else
            {
                propertyBlock.SetFloat(materialUseSceneDepthForegroundGuardId, 0f);
                propertyBlock.SetFloat(materialUseCarrierDepthGuardId, 0f);
            }

            if (occluderMask != null)
                propertyBlock.SetTexture(materialOcclusionTextureId, occluderMask);

            r.SetPropertyBlock(propertyBlock);
            boundCount++;
        }

        if (drawDebug)
        {
            string texName = occluderMask != null ? occluderMask.name : "null";
            lastStatus = $"shadowRenderers={boundCount}, occluderMask={texName}, queue={shadowRenderQueue}, maskAlwaysFed={(effectiveUseOcclusionMask > 0.5f)}, globalSelfMask={(useGlobalShadowOccluderMask > 0.5f)}, sceneDepthGuard={(bindCarrierDepthToMaterial && hasCarrierDepth)}, carrierDepth={(hasCarrierDepth ? lastCarrierEyeDepth.ToString("F3") : "null")}, authorityDiagnostic={useProjectionOcclusionAuthority}, authority={authorityAllowsMask}, coarseShadowDepthDiagnostic={shadowDepthEstablished}, shadowDepthReason={lastShadowSelfDepthReason}, reason={lastAuthorityReason}";
        }
    }


    private bool ShouldEstablishShadowSelfDepthGate()
    {
        if (!useShadowSelfDepthGate)
        {
            lastShadowSelfDepthEstablished = true;
            lastShadowSelfDepthReason = "shadow self depth gate off";
            return true;
        }

        Camera cam = ResolveAuthorityCamera();
        if (cam == null)
        {
            // Fail-safe: keep the old conservative path when no camera is available.
            lastShadowSelfDepthEstablished = true;
            lastShadowSelfDepthReason = "no camera -> conservative on";
            return true;
        }

        RefreshAuthorityOccluderRenderersIfNeeded();
        if (authorityOccluderRenderers.Count == 0)
        {
            lastShadowSelfDepthEstablished = false;
            lastShadowSelfDepthReason = "no shadow occluder renderers";
            return false;
        }

        bool hit = false;
        string reason = "no foreground depth relation";

        if (useShadowRendererBoundsForDepthGate && shadowRenderers != null)
        {
            float shadowDepth = hasCarrierDepth ? lastCarrierEyeDepth : ViewDepth(cam, transform.position);
            float padding = Mathf.Max(0f, shadowSelfDepthViewportPadding);
            float bias = Mathf.Max(0f, shadowSelfDepthBias);

            for (int s = 0; s < shadowRenderers.Length && !hit; s++)
            {
                Renderer shadowRenderer = shadowRenderers[s];
                if (shadowRenderer == null || !shadowRenderer.enabled)
                    continue;

                if (!TryGetViewportRectAndNearDepth(cam, shadowRenderer.bounds, out Rect shadowRect, out float shadowNearDepth))
                    continue;

                // Carrier depth represents the surface this shadow belongs to; renderer near depth can be too close
                // because the tilted quad has thickness in view space. Use the farther/more stable carrier depth.
                float effectiveShadowDepth = hasCarrierDepth ? shadowDepth : shadowNearDepth;

                shadowRect.xMin -= padding;
                shadowRect.yMin -= padding;
                shadowRect.xMax += padding;
                shadowRect.yMax += padding;

                for (int i = 0; i < authorityOccluderRenderers.Count; i++)
                {
                    Renderer occ = authorityOccluderRenderers[i];
                    if (occ == null)
                        continue;

                    if (!TryGetViewportRectAndNearDepth(cam, occ.bounds, out Rect occRect, out float occNearDepth))
                        continue;

                    occRect.xMin -= padding;
                    occRect.yMin -= padding;
                    occRect.xMax += padding;
                    occRect.yMax += padding;

                    if (!shadowRect.Overlaps(occRect))
                        continue;

                    if (occNearDepth < effectiveShadowDepth - bias)
                    {
                        hit = true;
                        reason = "shadow footprint behind foreground=" + occ.name;
                        break;
                    }
                }
            }
        }

        if (!hit)
        {
            BuildAuthoritySamplePoints();
            for (int i = 0; i < authoritySamplePoints.Length; i++)
            {
                if (IsSampleBehindForegroundOccluder(cam, authoritySamplePoints[i], out string hitReason))
                {
                    hit = true;
                    reason = "shadow sample depth=" + hitReason;
                    break;
                }
            }
        }

        float now = Application.isPlaying ? Time.unscaledTime : 0f;
        if (hit)
            shadowSelfDepthPositiveUntil = now + Mathf.Max(0f, shadowSelfDepthHoldSeconds);

        bool established = hit || now <= shadowSelfDepthPositiveUntil;
        lastShadowSelfDepthEstablished = established;
        lastShadowSelfDepthReason = established ? reason : "clear";
        return established;
    }


    private void ResolveDefaultShadowOccluderLayerMask()
    {
        if (shadowOccluderLayerMask.value != 0)
            return;

        if (string.IsNullOrWhiteSpace(shadowOccluderLayerName))
            return;

        int layer = LayerMask.NameToLayer(shadowOccluderLayerName);
        if (layer >= 0)
            shadowOccluderLayerMask = 1 << layer;
    }

    private bool ShouldApplyProjectionOcclusionMask()
    {
        // If the new authority is OFF, preserve V3 behavior: bind the mask whenever available.
        if (!useProjectionOcclusionAuthority)
        {
            lastAuthorityGate = true;
            lastAuthorityReason = "authority off";
            return true;
        }

        Camera cam = ResolveAuthorityCamera();
        if (cam == null)
        {
            // Fail-safe: if no camera is available, keep the conservative mask on.
            lastAuthorityGate = true;
            lastAuthorityReason = "no camera -> conservative on";
            return true;
        }

        RefreshAuthorityOccluderRenderersIfNeeded();
        BuildAuthoritySamplePoints();

        bool hit = false;
        string reason = "no foreground hit";
        for (int i = 0; i < authoritySamplePoints.Length; i++)
        {
            if (IsSampleBehindForegroundOccluder(cam, authoritySamplePoints[i], out string hitReason))
            {
                hit = true;
                reason = hitReason;
                break;
            }
        }

        float now = Application.isPlaying ? Time.unscaledTime : 0f;
        if (hit)
            authorityPositiveUntil = now + Mathf.Max(0f, authorityPositiveHoldSeconds);

        bool gate = hit || now <= authorityPositiveUntil;
        lastAuthorityGate = gate;
        lastAuthorityReason = gate ? reason : "clear";
        return gate;
    }

    private Camera ResolveAuthorityCamera()
    {
        if (authorityCamera != null)
            return authorityCamera;

        Camera main = Camera.main;
        if (main != null)
        {
            authorityCamera = main;
            return authorityCamera;
        }

        if (!string.IsNullOrWhiteSpace(fallbackCameraName))
        {
            Camera[] cameras = Camera.allCameras;
            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] != null && cameras[i].name == fallbackCameraName)
                {
                    authorityCamera = cameras[i];
                    return authorityCamera;
                }
            }
        }

        return null;
    }

    private void RefreshAuthorityOccluderRenderersIfNeeded()
    {
        float now = Application.isPlaying ? Time.unscaledTime : 0f;
        if (Application.isPlaying && now < nextAuthorityRendererRefreshTime)
            return;

        nextAuthorityRendererRefreshTime = now + Mathf.Max(0.02f, authorityRendererRefreshInterval);
        authorityOccluderRenderers.Clear();

        Renderer[] all = Resources.FindObjectsOfTypeAll<Renderer>();
        for (int i = 0; i < all.Length; i++)
        {
            Renderer r = all[i];
            if (r == null || r.gameObject == null)
                continue;

            if (authorityRequireActiveInHierarchy && !r.gameObject.activeInHierarchy)
                continue;

            if (authorityRequireRendererEnabled && !r.enabled)
                continue;

            if (shadowOccluderLayerMask.value != 0)
            {
                int bit = 1 << r.gameObject.layer;
                if ((shadowOccluderLayerMask.value & bit) == 0)
                    continue;
            }

            string path = GetTransformPath(r.transform);
            if (PathContainsAnyKeyword(path, authorityRejectPathKeywords))
                continue;

            // Never let this shadow's own renderer become its occluder.
            if (r.transform == transform || r.transform.IsChildOf(transform))
                continue;

            authorityOccluderRenderers.Add(r);
        }
    }

    private void BuildAuthoritySamplePoints()
    {
        Vector3 center = transform.position;
        Vector3 right = FlattenDirection(transform.right);
        Vector3 forward = FlattenDirection(transform.forward);

        if (right.sqrMagnitude < 0.0001f)
            right = Vector3.right;
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;

        float radius = Mathf.Max(0f, authoritySampleRadius);
        authoritySamplePoints[0] = center;
        authoritySamplePoints[1] = center + right * radius;
        authoritySamplePoints[2] = center - right * radius;
        authoritySamplePoints[3] = center + forward * radius;
        authoritySamplePoints[4] = center - forward * radius;
    }

    private static Vector3 FlattenDirection(Vector3 dir)
    {
        dir.y = 0f;
        float mag = dir.magnitude;
        if (mag <= 0.0001f)
            return Vector3.zero;
        return dir / mag;
    }

    private bool IsSampleBehindForegroundOccluder(Camera cam, Vector3 sampleWorld, out string reason)
    {
        reason = "-";
        if (authorityOccluderRenderers.Count == 0)
        {
            reason = "no occluder renderers";
            return false;
        }

        Vector3 sampleViewport = cam.WorldToViewportPoint(sampleWorld);
        if (sampleViewport.z <= 0f)
        {
            reason = "sample behind camera";
            return false;
        }

        float sampleDepth = ViewDepth(cam, sampleWorld);
        for (int i = 0; i < authorityOccluderRenderers.Count; i++)
        {
            Renderer r = authorityOccluderRenderers[i];
            if (r == null)
                continue;

            Bounds b = r.bounds;
            if (!TryGetViewportRectAndNearDepth(cam, b, out Rect rect, out float occluderNearDepth))
                continue;

            rect.xMin -= authorityScreenPadding;
            rect.yMin -= authorityScreenPadding;
            rect.xMax += authorityScreenPadding;
            rect.yMax += authorityScreenPadding;

            if (!rect.Contains(new Vector2(sampleViewport.x, sampleViewport.y)))
                continue;

            // Smaller view depth means closer to camera. If the occluder is closer than this shadow sample,
            // it is a true foreground blocker for the projection, independent from unit BackTrigger state.
            if (occluderNearDepth < sampleDepth - Mathf.Max(0f, authorityDepthBias))
            {
                reason = "front occluder=" + r.name;
                return true;
            }
        }

        reason = "no front occluder at sample";
        return false;
    }

    private static float ViewDepth(Camera cam, Vector3 world)
    {
        return Vector3.Dot(cam.transform.forward, world - cam.transform.position);
    }

    private static bool TryGetViewportRectAndNearDepth(Camera cam, Bounds bounds, out Rect rect, out float nearDepth)
    {
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        Vector3[] corners =
        {
            new Vector3(min.x, min.y, min.z),
            new Vector3(min.x, min.y, max.z),
            new Vector3(min.x, max.y, min.z),
            new Vector3(min.x, max.y, max.z),
            new Vector3(max.x, min.y, min.z),
            new Vector3(max.x, min.y, max.z),
            new Vector3(max.x, max.y, min.z),
            new Vector3(max.x, max.y, max.z),
        };

        float minX = float.PositiveInfinity;
        float minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float maxY = float.NegativeInfinity;
        nearDepth = float.PositiveInfinity;
        bool anyInFront = false;

        for (int i = 0; i < corners.Length; i++)
        {
            float d = ViewDepth(cam, corners[i]);
            if (d <= 0f)
                continue;

            anyInFront = true;
            nearDepth = Mathf.Min(nearDepth, d);

            Vector3 vp = cam.WorldToViewportPoint(corners[i]);
            minX = Mathf.Min(minX, vp.x);
            minY = Mathf.Min(minY, vp.y);
            maxX = Mathf.Max(maxX, vp.x);
            maxY = Mathf.Max(maxY, vp.y);
        }

        if (!anyInFront)
        {
            rect = new Rect();
            nearDepth = 0f;
            return false;
        }

        rect = Rect.MinMaxRect(minX, minY, maxX, maxY);
        return true;
    }

    private static bool PathContainsAnyKeyword(string path, string keywords)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(keywords))
            return false;

        string lowerPath = path.ToLowerInvariant();
        string[] parts = keywords.Split(';');
        for (int i = 0; i < parts.Length; i++)
        {
            string k = parts[i];
            if (string.IsNullOrWhiteSpace(k))
                continue;
            if (lowerPath.Contains(k.Trim().ToLowerInvariant()))
                return true;
        }

        return false;
    }

    private static string GetTransformPath(Transform t)
    {
        if (t == null)
            return string.Empty;

        string path = t.name;
        Transform current = t.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }
        return path;
    }

    private bool TryGetGroundY(Vector3 targetWorldPos, out float groundY)
    {
        if (preferTerrainSampleHeight && TrySampleTerrainHeight(targetWorldPos, out groundY))
            return true;

        Vector3 origin = new Vector3(targetWorldPos.x, targetWorldPos.y + probeHeight, targetWorldPos.z);
        float maxDistance = probeHeight + probeDistance;

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, maxDistance, groundMask, QueryTriggerInteraction.Ignore))
        {
            groundY = hit.point.y;

            if (drawDebug)
                Debug.DrawLine(origin, hit.point, Color.green);

            return true;
        }

        if (drawDebug)
            Debug.DrawLine(origin, origin + Vector3.down * maxDistance, Color.red);

        groundY = 0f;
        return false;
    }

    private bool TrySampleTerrainHeight(Vector3 targetWorldPos, out float groundY)
    {
        Terrain[] terrains = Terrain.activeTerrains;
        for (int i = 0; i < terrains.Length; i++)
        {
            Terrain terrain = terrains[i];
            if (terrain == null || terrain.terrainData == null)
                continue;

            TerrainData data = terrain.terrainData;
            Vector3 pos = terrain.transform.position;
            Vector3 size = data.size;

            bool insideX = targetWorldPos.x >= pos.x && targetWorldPos.x <= pos.x + size.x;
            bool insideZ = targetWorldPos.z >= pos.z && targetWorldPos.z <= pos.z + size.z;
            if (!insideX || !insideZ)
                continue;

            groundY = terrain.SampleHeight(targetWorldPos) + pos.y;

            if (drawDebug)
            {
                Vector3 p = new Vector3(targetWorldPos.x, groundY, targetWorldPos.z);
                Debug.DrawLine(p + Vector3.up * 0.25f, p, Color.cyan);
            }

            return true;
        }

        groundY = 0f;
        return false;
    }
}
