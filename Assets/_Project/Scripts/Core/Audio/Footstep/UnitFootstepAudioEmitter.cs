using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// V22 - 2026-05-29 - synced global audio settings diagnostics
/// Unit-level footstep audio bridge.
///
/// Responsibility:
/// - Owns the unit footstep audio point / AudioSource binding.
/// - Converts Spine event names such as Footstep_L / Footstep_R into package track playback.
/// - Owns equipment / unit footstep package playback, and can optionally synthesize Terrain ground-surface layers.
/// - Exposes a shared distance attenuation evaluator and logical footstep noise event for enemy hearing.
/// - Motion loudness is authoritative: Run/Walk/Sneak multipliers affect both audible playback and logical noise strength.
/// - V25 closes AI hearing loop by broadcasting logical footstep noise to UnitPerceptionRuntime listeners.
/// - Adds natural variation without requiring more footstep assets: motion volume/pitch, left/right pan, random variation, segment anti-repeat.
/// - Surface synthesis uses the same layer strength for audible playback and logical AI-hearing noise.
///
/// Runtime Applier can auto-create:
/// UnitRoot/AudioRoot/FootstepAudioPoint + AudioSource + this component.
/// </summary>
[DisallowMultipleComponent]
public class UnitFootstepAudioEmitter : MonoBehaviour
{
    public enum FootstepEventKind
    {
        None = 0,
        Left = 10,
        Right = 20,
        JumpUp = 30,
        Land = 40
    }

    public enum FootstepMotionKind
    {
        Walk = 0,
        Run = 10,
        Sneak = 20,
        JumpUp = 30,
        Land = 40
    }

    [Serializable]
    public class GroundSurfaceAudioPackageBinding
    {
        [Tooltip("优先按引用匹配。这里拖入地表材质定义，例如草地、水泥、浅水。")]
        public GroundSurfaceMaterialDefinition surfaceDefinition;

        [Tooltip("可选：按地表材质 surfaceId 匹配。surfaceDefinition 为空或不一致时作为兼容。")]
        public string surfaceId = "";

        [Tooltip("可选：按地表音声运行层匹配，例如 surface_grass / surface_stone / surface_water。")]
        public string runtimeLayerKey = "";

        [Tooltip("该地表对应的音声包，例如 AP_Surface_Grass。")]
        public SkyPrisonAudioPackage audioPackage;

        [Tooltip("该地表层的额外播放响度倍率。通常保持 1。")]
        [Min(0f)] public float volumeMultiplier = 1f;

        [Tooltip("该地表层的额外逻辑噪声倍率。通常保持 1；真正的行走/奔跑/潜行倍率来自 GroundSurfaceMaterialDefinition。")]
        [Min(0f)] public float noiseMultiplier = 1f;
    }

    private struct RuntimeSurfaceLayer
    {
        public GroundSurfaceMaterialDefinition definition;
        public GroundSurfaceAudioPackageBinding binding;
        public SkyPrisonAudioPackage package;
        public string runtimeLayerKey;
        public string surfaceId;
        public string displayName;
        public float weight;
        public float surfaceNoiseMultiplier;
        public float volumeMultiplier;
        public float noiseMultiplier;
    }

    private struct PlayedLayerResult
    {
        public bool played;
        public string label;
        public string packageKey;
        public string trackName;
        public string clipName;
        public float volume;
        public float logicalNoise;
        public float weight;
    }

    // PlayFootstep 每次触发（走路每一步、起跳、落地）都要用到这两个列表——之前每次
    // 都 new 一份新的，纯粹是GC垃圾（两个结构体列表，装的都是值类型，没有跨帧持有
    // 引用的顾虑，可以放心复用同一份，每次用前 Clear() 就行）。跳跃时"起跳+落地"
    // 短时间内密集触发两次，之前这两次分配叠加在一起会造成一次能感知到的卡顿。
    private readonly List<PlayedLayerResult> _playedLayersBuffer = new List<PlayedLayerResult>();
    private readonly List<RuntimeSurfaceLayer> _surfaceLayersBuffer = new List<RuntimeSurfaceLayer>();

    public struct FootstepNoiseEvent
    {
        public UnitFootstepAudioEmitter emitter;
        public Vector3 position;
        public FootstepEventKind kind;
        public FootstepMotionKind motion;
        public float baseStrength;
        public bool spatial3D;
        public float minDistance;
        public float maxDistance;
        public AudioRolloffMode rolloffMode;
        public float time;
        public string surfaceMix;
    }

    public event Action<FootstepNoiseEvent> FootstepNoiseEmitted;


    [Header("References")]
    [SerializeField] private Transform audioRoot;
    [SerializeField] private Transform footstepAudioPoint;
    [SerializeField] private AudioSource footstepAudioSource;
    [SerializeField] private UnitMovementController movementController;

    [Header("Footstep Package")]
    [Tooltip("当前鞋子/装备覆盖的脚步声包。后续由装备运行时写入；为空时使用单位默认脚步声包。")]
    [SerializeField] private SkyPrisonAudioPackage footstepPackage;

    [Tooltip("单位自身默认脚步声包。Player 可设为裸足；怪物/机械单位应设为 claw / robotic / heavy creature 等专属包。由 UnitDefinitionRuntimeApplier 从 UnitDefinition 自动写入。")]
    [SerializeField] private SkyPrisonAudioPackage unitDefaultFootstepPackage;

    [Tooltip("最终兜底脚步声包。只作为系统 fallback，不应该代替怪物/敌人的单位默认脚步声。")]
    [SerializeField] private SkyPrisonAudioPackage fallbackBarefootPackage;

    [Header("Playback")]
    [Tooltip("开启后，运行时会把当前脚步声包的 Spatial 3D / Min Distance / Max Distance 同步到本组件和 AudioSource。这样音声包里改最大距离后，不会仍显示旧的 12。")]
    [SerializeField] private bool syncPlaybackSettingsFromActivePackage = true;
    [SerializeField] private bool play3D = true;
    [SerializeField] private float defaultMinDistance = 1f;
    [SerializeField] private float defaultMaxDistance = 12f;
    [SerializeField] private float globalVolumeMultiplier = 1f;
    [SerializeField] private Vector2 fallbackRandomPitchRange = new Vector2(0.98f, 1.03f);

    [Header("Authoritative 3D / Hearing Sync")]
    [Tooltip("开启后，只要音声包勾选 3D，本 AudioSource 就使用真实 3D 衰减：spatialBlend=1，min/max 直接使用音声包参数，不再套轻 3D 补偿。敌人听觉也应使用同一套 min/max/rolloff 计算。")]
    [SerializeField] private bool useAuthoritative3DAttenuationWhenPlay3D = true;

    [Tooltip("权威 3D 模式下默认不使用玩家反馈音量补偿，避免玩家听感和敌人听觉标准不一致。只有确实需要临时调试时才打开。")]
    [SerializeField] private bool allowPlayerFeedbackBoostInAuthoritative3D = false;

    [Tooltip("权威 3D 模式使用的衰减模式。建议 Linear，因为它和 AI 听觉阈值最容易保持一致。")]
    [SerializeField] private AudioRolloffMode authoritativeRolloffMode = AudioRolloffMode.Linear;

    [Tooltip("权威 3D 模式下的最小满音量距离下限。音声包写得太小会导致刚离开角色就明显变小；这里作为统一兜底，同时影响玩家听感和敌人听觉。")]
    [SerializeField] private float authoritativeMinDistanceFloor = 1f;

    [Tooltip("权威 3D 模式下的最大听距下限。音声包 Max Distance 太小会导致声音过早归零；这里作为统一兜底，同时影响玩家听感和敌人听觉。")]
    [SerializeField] private float authoritativeMaxDistanceFloor = 0.01f;

    [Tooltip("权威 3D 模式下对音声包 Max Distance 的倍率。最终 Max = max(包内Max × 倍率, 最大听距下限)。同时影响玩家听感和敌人听觉。")]
    [SerializeField] private float authoritativeMaxDistanceMultiplier = 1f;

    [Tooltip("权威 3D 模式下的共享响度倍率。它会同时放大玩家听到的声音和逻辑噪声强度，保证玩家听感与敌人听觉标准一致。")]
    [SerializeField] private float authoritativeSharedStrengthMultiplier = 1f;


    [Header("Build Audible Playback Safety")]
    [Tooltip("Build 运行时启用脚步声播放端保底。只影响玩家实际听到的 AudioSource；AI 听觉仍使用权威 3D 距离和逻辑噪声。")]
    [SerializeField] private bool useBuildAudibleFootstepPlaybackSafety = false;

    [Tooltip("在 Editor Play 模式也应用保底，用于预览 Build 听感。关闭时只在非 Editor Build 生效。")]
    [SerializeField] private bool applyBuildAudibleFootstepPlaybackSafetyInEditor = false;

    [Tooltip("保底模式下脚步声 AudioSource 的 3D 混合度。0=完全2D，1=完全3D。建议 0.45~0.65。")]
    [Range(0f, 1f)]
    [SerializeField] private float buildAudibleFootstepSpatialBlend = 0.55f;

    [Tooltip("保底模式下脚步声 AudioSource 的最小听距下限。只影响播放端，不影响 AI 听觉。")]
    [SerializeField] private float buildAudibleFootstepMinDistanceFloor = 1f;

    [Tooltip("保底模式下脚步声 AudioSource 的最大听距下限。只影响播放端，不影响 AI 听觉。")]
    [SerializeField] private float buildAudibleFootstepMaxDistanceFloor = 8f;


    [Tooltip("运行时调试：上一次是否启用了 Build 脚步声播放保底。")]
    [SerializeField] private bool lastBuildAudiblePlaybackSafetyActiveRuntime = false;

    [Tooltip("运行时调试：记录上一次从当前 AudioListener 到脚步声点的距离。")]    [SerializeField] private float lastListenerDistanceRuntime = 0f;

    [Tooltip("运行时调试：记录上一次按共享衰减公式算出的 0~1 响度。")]
    [Range(0f, 1f)]
    [SerializeField] private float lastAuthoritativeAttenuation01Runtime = 1f;

    [Tooltip("运行时调试：记录上一次发出的逻辑噪声强度。敌人听觉应使用这个强度乘以距离衰减。")]
    [SerializeField] private float lastLogicalNoiseStrengthRuntime = 0f;

    [Header("Light 3D Footstep Tuning")]
    [Tooltip("脚步声是玩家自身反馈音，默认使用轻 3D，而不是完全真实距离衰减。0=2D，1=全3D。建议 0.55~0.8。")]
    [Range(0f, 1f)]
    [SerializeField] private float light3DSpatialBlend = 0.7f;

    [Tooltip("开启后脚步声 AudioSource 使用 Linear Rolloff，避免 2.5D 镜头距离导致 Logarithmic 衰减过狠。")]
    [SerializeField] private bool useLinearRolloffForFootsteps = true;

    [Tooltip("轻 3D 模式下的最小听距下限。即使音声包写 1，这里也会保证脚步声不会过早衰减。")]
    [SerializeField] private float light3DMinDistanceFloor = 1f;

    [Tooltip("脚步声玩家反馈补偿倍率。用于抵消 3D 衰减和素材响度偏低；建议 1~4，不建议拉到 20。")]
    [SerializeField] private float playerFeedbackVolumeBoost = 2.5f;

    [Header("Motion Loudness / Hearing Sync")]
    [Tooltip("行走响度倍率。以奔跑=1为基准；同时影响玩家听到的声音和敌人听觉噪声强度。")]
    [SerializeField] private float walkVolumeMultiplier = 0.7f;

    [Tooltip("奔跑响度倍率。作为标准基准，建议保持 1。")]
    [SerializeField] private float runVolumeMultiplier = 1.0f;

    [Tooltip("潜行响度倍率。建议 0.10~0.20；默认 0.15，不是完全无声，但远距离基本安全。")]
    [SerializeField] private float sneakVolumeMultiplier = 0.15f;

    [Tooltip("起跳响度倍率。跳跃不纳入潜行/行走/奔跑三段，单独配置。")]
    [SerializeField] private float jumpUpVolumeMultiplier = 1.0f;

    [Tooltip("落地响度倍率。落地通常比普通脚步更明显，默认略高于奔跑。")]
    [SerializeField] private float landVolumeMultiplier = 1.35f;

    [Tooltip("运行时调试：上一次动作响度倍率。")]
    [SerializeField] private float lastMotionLoudnessMultiplierRuntime = 1f;

    [Header("Natural Variation")]
    [Tooltip("是否启用脚步声自然化算法。只控制随机音量/音高、左右声像和避免连续重复；不再控制潜行/行走/奔跑响度。")]
    [SerializeField] private bool enableNaturalVariation = true;

    [Tooltip("每次脚步的随机音量范围。")]
    [SerializeField] private Vector2 randomVolumeRange = new Vector2(0.92f, 1.08f);

    [Tooltip("每次脚步的随机音高范围。")]
    [SerializeField] private Vector2 randomPitchRange = new Vector2(0.96f, 1.04f);

    [Tooltip("左脚声像轻微偏左。")]
    [SerializeField] private float leftFootPanOffset = -0.08f;

    [Tooltip("右脚声像轻微偏右。")]
    [SerializeField] private float rightFootPanOffset = 0.08f;

    [Tooltip("行走音高倍率。")]
    [SerializeField] private float walkPitchMultiplier = 1.0f;

    [Tooltip("奔跑音高倍率。")]
    [SerializeField] private float runPitchMultiplier = 0.97f;

    [Tooltip("潜行音高倍率。")]
    [SerializeField] private float sneakPitchMultiplier = 1.03f;

    [Tooltip("起跳音高倍率。")]
    [SerializeField] private float jumpUpPitchMultiplier = 1.02f;

    [Tooltip("落地音高倍率。")]
    [SerializeField] private float landPitchMultiplier = 0.92f;

    [Tooltip("同一音轨有多个 Segment 时，尽量避免连续两次播放同一个 Segment。")]
    [SerializeField] private bool avoidImmediateSegmentRepeat = true;


    [Header("Perception / Hearing Broadcast")]
    [Tooltip("开启后，每次脚步声都会把 totalLogicalNoise 广播给场景中启用听觉的 UnitPerceptionRuntime。")]
    [SerializeField] private bool broadcastLogicalFootstepNoiseToPerception = true;

    [Tooltip("开启后，声源单位不会听见自己的脚步声，避免自己把自己推入怀疑/警戒。")]
    [SerializeField] private bool skipSourceSelfHearing = true;

    [Tooltip("开启后，如果声源单位的 UnitPerceptionRuntime 标记为不可被感知，则不会把脚步声广播给其他单位。")]
    [SerializeField] private bool requireSourceCanBePerceivedForHearing = true;

    [Tooltip("调试：输出脚步声逻辑噪声广播给多少个听觉单位。")]
    [SerializeField] private bool debugHearingBroadcastLogs = false;


    [Header("Ground Surface Audio Synthesis")]
    [Tooltip("开启后，脚步声会在鞋底/单位脚步声之外，额外读取脚下 Terrain 地表材质并播放地表音声层。")]
    [SerializeField] private bool enableGroundSurfaceAudioSynthesis = true;

    [Tooltip("地表音声解析器。为空时会自动从父节点或场景中查找 SkyPrisonGroundAudioSurfaceResolver。")]
    [SerializeField] private SkyPrisonGroundAudioSurfaceResolver groundSurfaceResolver;

    [Tooltip("开启后保留原本鞋底/单位脚步声层；关闭后只播放地表层。建议先保持开启。")]
    [SerializeField] private bool playBaseFootwearLayer = true;
    [Tooltip("开启地表音声混合且脚下存在有效地表音声层时，原本鞋底/单位脚步声的播放音量倍率。草地、地毯、泥地等有材质覆盖时建议 0.35~0.5。")]
    [Range(0f, 1f)]
    [SerializeField] private float baseFootwearVolumeMultiplierWhenGroundSurfaceActive = 0.4f;

    [Tooltip("开启地表音声混合且脚下存在有效地表音声层时，原本鞋底/单位脚步声对 AI 听觉逻辑噪声的倍率。默认跟播放音量一致，避免玩家听感和敌人听觉不一致。")]
    [Range(0f, 1f)]
    [SerializeField] private float baseFootwearNoiseMultiplierWhenGroundSurfaceActive = 0.4f;

    [Tooltip("地表主材质权重大于等于该值时，只播放主地表层，避免声音过厚。")]
    [Range(0f, 1f)]
    [SerializeField] private float dominantSurfaceSingleLayerThreshold = 0.65f;

    [Tooltip("最多混合多少个地表音声层。建议 1~2；太多会糊。")]
    [Min(1)]
    [SerializeField] private int maxGroundSurfaceAudioLayers = 2;

    [Tooltip("低于该权重的地表层会被忽略。")]
    [Range(0f, 1f)]
    [SerializeField] private float minGroundSurfaceWeightToPlay = 0.08f;

    [Tooltip("主材质独占播放时，是否把该层权重归一为 1。开启后主地表不会因为 Terrain 权重是 0.7 而偏小。")]
    [SerializeField] private bool normalizeDominantSurfaceToFull = true;

    [Tooltip("所有地表层的额外播放响度倍率。和动作响度、地表权重相乘。")]
    [Min(0f)]
    [SerializeField] private float groundSurfaceLayerVolumeMultiplier = 1f;

    [Tooltip("所有地表层的额外 AI 逻辑噪声倍率。和动作响度、地表权重相乘。")]
    [Min(0f)]
    [SerializeField] private float groundSurfaceLayerNoiseMultiplier = 1f;

    [Tooltip("兼容旧版本：旧的 地表->音声包 绑定表。正式结构下优先读取 GroundSurfaceMaterialDefinition.surfaceAudioPackage；这里只作为历史资源兜底。")]
    [SerializeField, HideInInspector] private List<GroundSurfaceAudioPackageBinding> groundSurfaceAudioPackages = new List<GroundSurfaceAudioPackageBinding>();

    [Tooltip("运行时调试：上一次地表混合结果。")]
    [SerializeField] private string lastGroundSurfaceMixRuntime = "";

    [Tooltip("运行时调试：上一次实际播放的音声层。")]
    [SerializeField] private string lastPlayedAudioLayersRuntime = "";

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    [Tooltip("Build/Development Build 诊断：即使普通 Debug Logs 关闭，也输出脚步声事件映射、包、音轨、Clip 选择等关键链路。排查脚步声问题时再打开，默认关闭避免每一步都刷屏。")]
    [SerializeField] private bool buildFootstepDiagnosticLogs = false;

    private readonly List<SkyPrisonAudioTrack> matchBuffer = new List<SkyPrisonAudioTrack>();
    private readonly Dictionary<SkyPrisonAudioTrack, SkyPrisonAudioSegment> lastSegmentByTrack = new Dictionary<SkyPrisonAudioTrack, SkyPrisonAudioSegment>();

    public Transform AudioRoot => audioRoot;
    public Transform FootstepAudioPoint => footstepAudioPoint;
    public AudioSource FootstepAudioSource => footstepAudioSource;
    public SkyPrisonAudioPackage FootstepPackage => footstepPackage;
    public SkyPrisonAudioPackage UnitDefaultFootstepPackage => unitDefaultFootstepPackage;

    private void Reset()
    {
        EnsureAudioNodes();
    }

    private void Awake()
    {
        EnsureAudioNodes();
    }

    private void OnValidate()
    {
        EnsureAudioNodes();
    }

    public void ConfigureAudioNodes(Transform newAudioRoot, Transform newFootstepAudioPoint, AudioSource newFootstepAudioSource)
    {
        if (newAudioRoot != null)
            audioRoot = newAudioRoot;

        if (newFootstepAudioPoint != null)
            footstepAudioPoint = newFootstepAudioPoint;

        if (newFootstepAudioSource != null)
            footstepAudioSource = newFootstepAudioSource;

        EnsureAudioSourceSettings();
    }

    public void SetFootstepPackage(SkyPrisonAudioPackage package)
    {
        footstepPackage = package;
        EnsureAudioSourceSettings();
    }

    public void SetUnitDefaultFootstepPackage(SkyPrisonAudioPackage package)
    {
        unitDefaultFootstepPackage = package;
        EnsureAudioSourceSettings();
    }

    public void SetFallbackBarefootPackage(SkyPrisonAudioPackage package)
    {
        fallbackBarefootPackage = package;
        EnsureAudioSourceSettings();
    }

    [ContextMenu("Ensure Audio Nodes")]
    public void EnsureAudioNodes()
    {
        if (movementController == null)
            movementController = GetComponent<UnitMovementController>();

        if (groundSurfaceResolver == null)
            groundSurfaceResolver = GetComponentInParent<SkyPrisonGroundAudioSurfaceResolver>();

        if (groundSurfaceResolver == null)
            groundSurfaceResolver = FindObjectOfType<SkyPrisonGroundAudioSurfaceResolver>();

        if (audioRoot == null)
        {
            Transform found = transform.Find("AudioRoot");
            if (found != null)
                audioRoot = found;
        }

        if (footstepAudioPoint == null)
        {
            Transform searchRoot = audioRoot != null ? audioRoot : transform;
            Transform found = searchRoot.Find("FootstepAudioPoint");
            if (found != null)
                footstepAudioPoint = found;
        }

        if (footstepAudioPoint != null && footstepAudioSource == null)
            footstepAudioSource = footstepAudioPoint.GetComponent<AudioSource>();

        if (footstepAudioPoint == null)
            return;

        if (footstepAudioSource == null)
            footstepAudioSource = footstepAudioPoint.gameObject.AddComponent<AudioSource>();

        EnsureAudioSourceSettings();
    }

    private void EnsureAudioSourceSettings()
    {
        if (footstepAudioSource == null)
            return;

        SkyPrisonAudioPackage activePackage = GetActivePackage();
        ApplyActivePackagePlaybackSettings(activePackage);

        footstepAudioSource.playOnAwake = false;
        footstepAudioSource.loop = false;
        footstepAudioSource.volume = 1f;

        float packageMinDistance = Mathf.Max(0.01f, defaultMinDistance);
        float packageMaxDistance = Mathf.Max(packageMinDistance + 0.01f, defaultMaxDistance);

        if (play3D && useAuthoritative3DAttenuationWhenPlay3D)
        {
            // Authoritative mode: AI hearing keeps the same distance model.
            // Playback safety only changes how the player's AudioSource is heard in Build, because a 2.5D camera
            // can put AudioListener far away from the visible character and make full 3D footsteps disappear.
            GetEffectiveAuthoritativeDistances(packageMinDistance, packageMaxDistance, out float effectiveMinDistance, out float effectiveMaxDistance);

            // V24: Build audible playback safety has been retired after the footstep Build loop was closed.
            // The AudioSource now obeys the package-driven authoritative 3D distances exactly, so distance debugging
            // is not masked by hidden min/max floors or half-2D fallback playback.
            lastBuildAudiblePlaybackSafetyActiveRuntime = false;
            footstepAudioSource.spatialBlend = 1f;
            footstepAudioSource.minDistance = effectiveMinDistance;
            footstepAudioSource.maxDistance = effectiveMaxDistance;
            footstepAudioSource.rolloffMode = authoritativeRolloffMode;
        }
        else
        {
            // Sky Prison is a 2.5D game: the camera/listener can be physically far away from the unit even when
            // the character is visually close to the player.  Full 3D + logarithmic rolloff makes footsteps
            // nearly inaudible, so non-authoritative preview mode keeps the old light-3D profile.
            footstepAudioSource.spatialBlend = play3D ? Mathf.Clamp01(light3DSpatialBlend) : 0f;

            float effectiveMinDistance = packageMinDistance;
            if (play3D)
                effectiveMinDistance = Mathf.Max(effectiveMinDistance, light3DMinDistanceFloor);

            footstepAudioSource.minDistance = effectiveMinDistance;
            footstepAudioSource.maxDistance = Mathf.Max(footstepAudioSource.minDistance + 0.01f, packageMaxDistance);
            footstepAudioSource.rolloffMode = useLinearRolloffForFootsteps ? AudioRolloffMode.Linear : AudioRolloffMode.Logarithmic;
        }
    }

    private void ApplyActivePackagePlaybackSettings(SkyPrisonAudioPackage activePackage)
    {
        if (!syncPlaybackSettingsFromActivePackage || activePackage == null)
            return;

        play3D = activePackage.spatial3D;
        defaultMinDistance = Mathf.Max(0.01f, activePackage.minDistance);
        defaultMaxDistance = Mathf.Max(defaultMinDistance, activePackage.maxDistance);
    }

    [ContextMenu("Sync Playback Settings From Active Package")]
    public void SyncPlaybackSettingsFromActivePackageNow()
    {
        ApplyActivePackagePlaybackSettings(GetActivePackage());
        EnsureAudioSourceSettings();
    }

    public void HandleSpineEventName(string eventName)
    {
        FootstepEventKind kind = MapSpineEventName(eventName);
        if (ShouldLogFootstepDiagnostics())
            Debug.Log($"[UnitFootstepAudioEmitter] {name}: received SpineEvent='{eventName}', mappedKind={kind}, activePackage={(GetActivePackage() != null ? GetActivePackage().name : "null")}, source={(footstepAudioSource != null ? footstepAudioSource.name : "null")}", this);

        if (kind == FootstepEventKind.None)
            return;

        PlayFootstep(kind);
    }

#if SPINE_UNITY || SPINE_TK2D
    public void HandleSpineEvent(Spine.Event spineEvent)
    {
        if (spineEvent == null || spineEvent.Data == null)
            return;

        HandleSpineEventName(spineEvent.Data.Name);
    }
#else
    // This overload still compiles in projects with Spine because the project already references Spine.Unity.
    // Kept out of normal compile symbols in case the file is reused in a non-Spine test assembly.
#endif

    public void PlayFootstep(FootstepEventKind kind)
    {
        EnsureAudioNodes();

        AudioSource source = footstepAudioSource;
        if (source == null)
            return;

        EnsureAudioSourceSettings();

        FootstepMotionKind motion = ResolveMotionKind(kind);
        Vector3 sourcePosition = source.transform.position;

        float motionLoudness = GetMotionVolumeMultiplier(motion);
        lastMotionLoudnessMultiplierRuntime = motionLoudness;

        float naturalVolumeMultiplier = enableNaturalVariation ? SafeRandomRange(randomVolumeRange, 1f) : 1f;

        List<PlayedLayerResult> playedLayers = _playedLayersBuffer;
        playedLayers.Clear();
        float totalLogicalNoiseStrength = 0f;

        List<RuntimeSurfaceLayer> surfaceLayers = _surfaceLayersBuffer;
        surfaceLayers.Clear();
        if (enableGroundSurfaceAudioSynthesis)
            ResolveRuntimeSurfaceLayers(kind, motion, sourcePosition, surfaceLayers);

        bool hasGroundSurfaceAudioLayer = HasPlayableGroundSurfaceLayer(surfaceLayers);
        float baseFootwearVolumeMultiplier = hasGroundSurfaceAudioLayer
            ? GetEffectiveBaseFootwearVolumeMultiplierWhenGroundSurfaceActive()
            : 1f;
        float baseFootwearNoiseMultiplier = hasGroundSurfaceAudioLayer
            ? GetEffectiveBaseFootwearNoiseMultiplierWhenGroundSurfaceActive()
            : 1f;

        SkyPrisonAudioPackage basePackage = GetActivePackage();
        if (playBaseFootwearLayer || !enableGroundSurfaceAudioSynthesis)
        {
            PlayedLayerResult baseResult = PlayPackageLayer(
                basePackage,
                kind,
                motion,
                source,
                layerLabel: hasGroundSurfaceAudioLayer ? "base×surfaceDuck" : "base",
                requiredRuntimeLayerKey: "",
                layerVolumeMultiplier: baseFootwearVolumeMultiplier,
                layerNoiseMultiplier: baseFootwearNoiseMultiplier,
                naturalVolumeMultiplier: naturalVolumeMultiplier);

            if (baseResult.played)
            {
                playedLayers.Add(baseResult);
                totalLogicalNoiseStrength += baseResult.logicalNoise;
            }
            else if (!enableGroundSurfaceAudioSynthesis && debugLogs)
            {
                Debug.LogWarning($"[UnitFootstepAudioEmitter] {name}: no clip found for {kind}/{motion} in package {(basePackage != null ? basePackage.name : "null")}.", this);
            }
        }

        if (enableGroundSurfaceAudioSynthesis)
        {
            for (int i = 0; i < surfaceLayers.Count; i++)
            {
                RuntimeSurfaceLayer layer = surfaceLayers[i];
                if (layer.package == null || layer.weight <= 0f)
                    continue;

                float layerVolumeMultiplier =
                    layer.weight *
                    layer.volumeMultiplier *
                    GetEffectiveGroundSurfaceLayerVolumeMultiplier();

                float layerNoiseMultiplier =
                    layer.weight *
                    layer.surfaceNoiseMultiplier *
                    layer.noiseMultiplier *
                    GetEffectiveGroundSurfaceLayerNoiseMultiplier();

                PlayedLayerResult surfaceResult = PlayPackageLayer(
                    layer.package,
                    kind,
                    motion,
                    source,
                    layerLabel: string.IsNullOrWhiteSpace(layer.displayName) ? layer.surfaceId : layer.displayName,
                    requiredRuntimeLayerKey: layer.runtimeLayerKey,
                    layerVolumeMultiplier: layerVolumeMultiplier,
                    layerNoiseMultiplier: layerNoiseMultiplier,
                    naturalVolumeMultiplier: naturalVolumeMultiplier);

                if (surfaceResult.played)
                {
                    playedLayers.Add(surfaceResult);
                    totalLogicalNoiseStrength += surfaceResult.logicalNoise;
                }
            }
        }

        lastPlayedAudioLayersRuntime = BuildPlayedLayerDebugString(playedLayers);

        if (playedLayers.Count <= 0)
        {
            if (ShouldLogFootstepDiagnostics())
                Debug.LogWarning($"[UnitFootstepAudioEmitter] {name}: no playable layer found for {kind}/{motion}. basePackage={(basePackage != null ? basePackage.name : "null")}, surfaceMix={lastGroundSurfaceMixRuntime}, playBase={playBaseFootwearLayer}, groundSynthesis={enableGroundSurfaceAudioSynthesis}, audioSource={(footstepAudioSource != null ? footstepAudioSource.name : "null")}", this);
            return;
        }

        EmitLogicalFootstepNoise(kind, motion, totalLogicalNoiseStrength, sourcePosition);

        if (ShouldLogFootstepDiagnostics())
        {
            SkyPrisonAudioGlobalSettings activeSettings = GetGlobalAudioSettings();
            Debug.Log(
                $"[UnitFootstepAudioEmitter] {name}: play {kind}/{motion}, layers={lastPlayedAudioLayersRuntime}, totalLogicalNoise={totalLogicalNoiseStrength:0.###}, 3D={play3D}, authoritative3D={useAuthoritative3DAttenuationWhenPlay3D}, blend={footstepAudioSource.spatialBlend:0.###}, min={footstepAudioSource.minDistance}, max={footstepAudioSource.maxDistance}, rolloff={footstepAudioSource.rolloffMode}, lastAttn={lastAuthoritativeAttenuation01Runtime:0.###}, motionLoudness={lastMotionLoudnessMultiplierRuntime:0.###}, surfaceMix={lastGroundSurfaceMixRuntime}, baseDuck={(hasGroundSurfaceAudioLayer ? baseFootwearVolumeMultiplier : 1f):0.###}, buildSafety={lastBuildAudiblePlaybackSafetyActiveRuntime}, globalSettings={(activeSettings != null ? activeSettings.name : "null")}, globalValues={(activeSettings != null ? activeSettings.BuildDebugSummary() : "null")}.",
                this);
        }
    }

    private PlayedLayerResult PlayPackageLayer(
        SkyPrisonAudioPackage package,
        FootstepEventKind kind,
        FootstepMotionKind motion,
        AudioSource source,
        string layerLabel,
        string requiredRuntimeLayerKey,
        float layerVolumeMultiplier,
        float layerNoiseMultiplier,
        float naturalVolumeMultiplier)
    {
        PlayedLayerResult result = new PlayedLayerResult
        {
            played = false,
            label = layerLabel,
            packageKey = package != null ? package.packageKey : "",
            weight = layerVolumeMultiplier
        };

        if (package == null || source == null || layerVolumeMultiplier <= 0f || layerNoiseMultiplier <= 0f)
            return result;

        SkyPrisonAudioTrack track = FindTrackForKind(package, kind, motion, requiredRuntimeLayerKey);
        SkyPrisonAudioSegment segment = PickSegment(track, avoidImmediateSegmentRepeat);
        if (segment == null || segment.sourceClip == null)
            return result;

        SkyPrisonMixerChannel channel = package.FindMixerChannel(track != null ? track.mixerChannelId : string.Empty);

        float packageVolume = Mathf.Max(0f, package.masterVolume);
        float channelVolume = channel != null ? Mathf.Max(0f, channel.volume) : 1f;
        float segmentVolume = Mathf.Max(0f, segment.volume);

        float logicalNoiseStrength =
            packageVolume *
            channelVolume *
            segmentVolume *
            GetEffectiveFootstepNoiseMasterMultiplier() *
            GetMotionVolumeMultiplier(motion) *
            Mathf.Max(0f, layerNoiseMultiplier) *
            Mathf.Max(0f, naturalVolumeMultiplier);

        if (play3D && useAuthoritative3DAttenuationWhenPlay3D)
            logicalNoiseStrength *= Mathf.Max(0f, authoritativeSharedStrengthMultiplier);

        float volume =
            packageVolume *
            channelVolume *
            segmentVolume *
            GetEffectiveFootstepOutputMasterMultiplier() *
            GetMotionVolumeMultiplier(motion) *
            Mathf.Max(0f, layerVolumeMultiplier) *
            Mathf.Max(0f, naturalVolumeMultiplier);

        if (play3D && useAuthoritative3DAttenuationWhenPlay3D)
            volume *= Mathf.Max(0f, authoritativeSharedStrengthMultiplier);

        if (play3D && (!useAuthoritative3DAttenuationWhenPlay3D || allowPlayerFeedbackBoostInAuthoritative3D))
            volume *= Mathf.Max(0f, playerFeedbackVolumeBoost);

        float packageRandomPitch = UnityEngine.Random.Range(package.randomPitchRange.x, package.randomPitchRange.y);
        if (float.IsNaN(packageRandomPitch) || packageRandomPitch <= 0f)
            packageRandomPitch = UnityEngine.Random.Range(fallbackRandomPitchRange.x, fallbackRandomPitchRange.y);

        float naturalPitch = enableNaturalVariation ? SafeRandomRange(randomPitchRange, 1f) * GetMotionPitchMultiplier(motion) : 1f;

        source.pitch = Mathf.Clamp(segment.pitch * packageRandomPitch * naturalPitch, 0.25f, 3f);

        float footPanOffset = enableNaturalVariation ? GetFootPanOffset(kind) : 0f;
        source.panStereo = Mathf.Clamp((channel != null ? channel.pan : 0f) + segment.pan + footPanOffset, -1f, 1f);
        source.PlayOneShot(segment.sourceClip, volume);

        result.played = true;
        result.trackName = track != null ? track.displayName : "null";
        result.clipName = segment.sourceClip.name;
        result.volume = volume;
        result.logicalNoise = logicalNoiseStrength;
        return result;
    }

    private bool HasPlayableGroundSurfaceLayer(List<RuntimeSurfaceLayer> surfaceLayers)
    {
        if (surfaceLayers == null || surfaceLayers.Count <= 0)
            return false;

        for (int i = 0; i < surfaceLayers.Count; i++)
        {
            RuntimeSurfaceLayer layer = surfaceLayers[i];
            if (layer.package != null && layer.weight > 0f)
                return true;
        }

        return false;
    }

    // 改成往调用方传进来的 result 列表里写（不再 new 一份新的返回）——这个方法在
    // 每一步脚步声/起跳/落地都会跑一次，之前每次都现造一份新 List 纯属GC垃圾。
    private void ResolveRuntimeSurfaceLayers(FootstepEventKind kind, FootstepMotionKind motion, Vector3 worldPosition, List<RuntimeSurfaceLayer> result)
    {
        lastGroundSurfaceMixRuntime = "";

        if (groundSurfaceResolver == null)
            return;

        List<SkyPrisonGroundAudioSurfaceResolver.SurfaceWeight> weights = groundSurfaceResolver.GetSurfaceWeights(worldPosition);
        if (weights == null || weights.Count == 0)
            return;

        weights.Sort((a, b) => b.weight.CompareTo(a.weight));

        int layerLimit = Mathf.Clamp(maxGroundSurfaceAudioLayers, 1, 4);
        if (weights[0].weight >= dominantSurfaceSingleLayerThreshold)
            layerLimit = 1;

        float usedWeightSum = 0f;

        for (int i = 0; i < weights.Count && result.Count < layerLimit; i++)
        {
            SkyPrisonGroundAudioSurfaceResolver.SurfaceWeight weight = weights[i];
            if (weight.weight < minGroundSurfaceWeightToPlay)
                continue;

            GroundSurfaceMaterialDefinition definition = weight.definition;
            if (definition == null)
                continue;

            SkyPrisonAudioPackage surfacePackage = ResolveGroundSurfaceAudioPackage(definition, kind, out GroundSurfaceAudioPackageBinding binding);
            if (surfacePackage == null)
                continue;

            string runtimeKey = GetGroundSurfaceRuntimeLayerKey(definition, kind);
            if (string.IsNullOrWhiteSpace(runtimeKey) && binding != null)
                runtimeKey = binding.runtimeLayerKey;

            float finalWeight = Mathf.Clamp01(weight.weight);
            if (result.Count == 0 && weights[0].weight >= dominantSurfaceSingleLayerThreshold && normalizeDominantSurfaceToFull)
                finalWeight = 1f;

            RuntimeSurfaceLayer layer = new RuntimeSurfaceLayer
            {
                definition = definition,
                binding = binding,
                package = surfacePackage,
                runtimeLayerKey = runtimeKey,
                surfaceId = definition.surfaceId,
                displayName = definition.displayName,
                weight = finalWeight,
                surfaceNoiseMultiplier = GetSurfaceNoiseMultiplier(definition, motion),
                volumeMultiplier = Mathf.Max(0f, binding != null ? binding.volumeMultiplier : 1f),
                noiseMultiplier = Mathf.Max(0f, binding != null ? binding.noiseMultiplier : 1f)
            };

            usedWeightSum += finalWeight;
            result.Add(layer);
        }

        if (result.Count > 1 && usedWeightSum > 0.0001f)
        {
            // Keep mixed boundary layers predictable: selected layers sum to 1 instead of becoming too quiet
            // when the remaining Terrain layers were ignored by max layer count / min threshold.
            for (int i = 0; i < result.Count; i++)
            {
                RuntimeSurfaceLayer layer = result[i];
                layer.weight = Mathf.Clamp01(layer.weight / usedWeightSum);
                result[i] = layer;
            }
        }

        lastGroundSurfaceMixRuntime = BuildSurfaceMixDebugString(result);
    }

    private SkyPrisonAudioPackage ResolveGroundSurfaceAudioPackage(
        GroundSurfaceMaterialDefinition definition,
        FootstepEventKind kind,
        out GroundSurfaceAudioPackageBinding legacyBinding)
    {
        legacyBinding = null;

        if (definition == null)
            return null;

        // 正式结构：地表材质自己直接绑定地表音声包。
        // 角色脚步声组件只负责采样和混合，不再维护 草地->AP_Surface_Grass 这类外部映射表。
        if (definition.surfaceAudioPackage != null)
            return definition.surfaceAudioPackage;

        // 兼容历史资源：如果旧绑定表还有数据，就兜底使用它。
        legacyBinding = FindLegacyGroundSurfaceAudioBinding(definition, kind);
        return legacyBinding != null ? legacyBinding.audioPackage : null;
    }

    private GroundSurfaceAudioPackageBinding FindLegacyGroundSurfaceAudioBinding(GroundSurfaceMaterialDefinition definition, FootstepEventKind kind)
    {
        if (definition == null || groundSurfaceAudioPackages == null)
            return null;

        string surfaceId = NormalizeKey(definition.surfaceId);
        string runtimeKey = NormalizeKey(GetGroundSurfaceRuntimeLayerKey(definition, kind));

        for (int i = 0; i < groundSurfaceAudioPackages.Count; i++)
        {
            GroundSurfaceAudioPackageBinding binding = groundSurfaceAudioPackages[i];
            if (binding == null || binding.audioPackage == null)
                continue;

            if (binding.surfaceDefinition == definition)
                return binding;

            string bindingSurfaceId = NormalizeKey(binding.surfaceId);
            if (!string.IsNullOrEmpty(surfaceId) && bindingSurfaceId == surfaceId)
                return binding;

            string bindingRuntimeKey = NormalizeKey(binding.runtimeLayerKey);
            if (!string.IsNullOrEmpty(runtimeKey) && bindingRuntimeKey == runtimeKey)
                return binding;
        }

        return null;
    }

    private static string GetGroundSurfaceRuntimeLayerKey(GroundSurfaceMaterialDefinition definition, FootstepEventKind kind)
    {
        if (definition == null)
            return "";

        // 正式结构：地表材质只提供一个 surface_* 运行层 Key。
        // 起跳 / 落地 / 左右脚 / 滑步差异由音声包内部音轨和 Spine 事件决定。
        return definition.EffectiveAudioRuntimeLayerKey;
    }

    private static float GetSurfaceNoiseMultiplier(GroundSurfaceMaterialDefinition definition, FootstepMotionKind motion)
    {
        if (definition == null)
            return 1f;

        switch (motion)
        {
            case FootstepMotionKind.Run:
                return Mathf.Max(0f, definition.runNoiseMultiplier);
            case FootstepMotionKind.Sneak:
                return Mathf.Max(0f, definition.sneakNoiseMultiplier);
            case FootstepMotionKind.Land:
                return Mathf.Max(0f, definition.landingNoiseMultiplier);
            case FootstepMotionKind.JumpUp:
            case FootstepMotionKind.Walk:
            default:
                return Mathf.Max(0f, definition.walkNoiseMultiplier);
        }
    }

    private static string BuildSurfaceMixDebugString(List<RuntimeSurfaceLayer> layers)
    {
        if (layers == null || layers.Count == 0)
            return "none";

        System.Text.StringBuilder builder = new System.Text.StringBuilder();
        for (int i = 0; i < layers.Count; i++)
        {
            if (i > 0)
                builder.Append(" + ");

            RuntimeSurfaceLayer layer = layers[i];
            builder.Append(string.IsNullOrWhiteSpace(layer.displayName) ? layer.surfaceId : layer.displayName);
            builder.Append(" ");
            builder.Append((layer.weight * 100f).ToString("0.#"));
            builder.Append("%");
            if (!string.IsNullOrWhiteSpace(layer.runtimeLayerKey))
            {
                builder.Append(" / ");
                builder.Append(layer.runtimeLayerKey);
            }
        }

        return builder.ToString();
    }

    private static string BuildPlayedLayerDebugString(List<PlayedLayerResult> layers)
    {
        if (layers == null || layers.Count == 0)
            return "none";

        System.Text.StringBuilder builder = new System.Text.StringBuilder();
        for (int i = 0; i < layers.Count; i++)
        {
            if (i > 0)
                builder.Append(" | ");

            PlayedLayerResult layer = layers[i];
            builder.Append(layer.label);
            builder.Append(":");
            builder.Append(string.IsNullOrWhiteSpace(layer.packageKey) ? "package" : layer.packageKey);
            builder.Append("/");
            builder.Append(string.IsNullOrWhiteSpace(layer.trackName) ? "track" : layer.trackName);
            builder.Append("/");
            builder.Append(string.IsNullOrWhiteSpace(layer.clipName) ? "clip" : layer.clipName);
            builder.Append(" v=");
            builder.Append(layer.volume.ToString("0.###"));
            builder.Append(" n=");
            builder.Append(layer.logicalNoise.ToString("0.###"));
        }

        return builder.ToString();
    }

    private void EmitLogicalFootstepNoise(FootstepEventKind kind, FootstepMotionKind motion, float logicalNoiseStrength, Vector3 sourcePosition)
    {
        GetEffectiveRuntimeAttenuationSettings(out float minDistance, out float maxDistance, out AudioRolloffMode rolloff);

        lastLogicalNoiseStrengthRuntime = Mathf.Max(0f, logicalNoiseStrength);
        UpdateListenerDistanceDebug(sourcePosition, minDistance, maxDistance, rolloff);
        BroadcastLogicalFootstepNoiseToPerceptionListeners(kind, motion, lastLogicalNoiseStrengthRuntime, sourcePosition);

        if (!play3D)
            return;

        FootstepNoiseEmitted?.Invoke(new FootstepNoiseEvent
        {
            emitter = this,
            position = sourcePosition,
            kind = kind,
            motion = motion,
            baseStrength = lastLogicalNoiseStrengthRuntime,
            spatial3D = play3D,
            minDistance = minDistance,
            maxDistance = maxDistance,
            rolloffMode = rolloff,
            time = Time.time,
            surfaceMix = lastGroundSurfaceMixRuntime
        });
    }


    // 之前 broadcastAction 是个每次调用都新建的 lambda，闭包捕获了一堆局部变量
    // （sourceBinder/sourcePerception/sourceIsPlayer/logicalNoiseStrength/sourcePosition/
    // notified），每次脚步事件都要新分配一个闭包对象+委托——乘以场上所有会走路的
    // 单位，是持续GC压力来源之一。改成用实例字段暂存这次调用的上下文，委托本身
    // 只在第一次用到时创建一次并缓存，之后一直复用同一个委托实例。
    private UnitDefinitionRuntimeBinder _broadcastSourceBinder;
    private UnitPerceptionRuntime _broadcastSourcePerception;
    private bool _broadcastSourceIsPlayer;
    private float _broadcastLogicalNoiseStrength;
    private Vector3 _broadcastSourcePosition;
    private int _broadcastNotifiedCount;
    private System.Action<UnitPerceptionRuntime> _cachedBroadcastAction;

    private void BroadcastToListener(UnitPerceptionRuntime listener)
    {
        if (!listener.CanUseHearing)
            return;

        if (skipSourceSelfHearing)
        {
            if (listener == _broadcastSourcePerception)
                return;

            if (_broadcastSourceBinder != null && listener.RuntimeBinder == _broadcastSourceBinder)
                return;
        }

        UnitPerceptionLevel before = listener.CurrentLevel;
        UnitPerceptionLevel after = listener.ReportHeardSound(_broadcastSourceBinder, _broadcastSourcePosition, _broadcastLogicalNoiseStrength, _broadcastSourceIsPlayer);
        if (after != before || listener.HasLastHeardPosition)
            _broadcastNotifiedCount++;
    }

    private void BroadcastLogicalFootstepNoiseToPerceptionListeners(FootstepEventKind kind, FootstepMotionKind motion, float logicalNoiseStrength, Vector3 sourcePosition)
    {
        if (!broadcastLogicalFootstepNoiseToPerception || logicalNoiseStrength <= 0f)
            return;

        UnitDefinitionRuntimeBinder sourceBinder = GetComponent<UnitDefinitionRuntimeBinder>();
        UnitPerceptionRuntime sourcePerception = GetComponent<UnitPerceptionRuntime>();

        if (requireSourceCanBePerceivedForHearing && sourcePerception != null && !sourcePerception.CanBePerceived)
            return;

        _broadcastSourceBinder = sourceBinder;
        _broadcastSourcePerception = sourcePerception;
        _broadcastSourceIsPlayer = sourceBinder != null && sourceBinder.UnitDefinitionAsset != null && sourceBinder.UnitDefinitionAsset.characterIdentity == CharacterIdentity.Player;
        _broadcastLogicalNoiseStrength = logicalNoiseStrength;
        _broadcastSourcePosition = sourcePosition;
        _broadcastNotifiedCount = 0;

        if (_cachedBroadcastAction == null)
            _cachedBroadcastAction = BroadcastToListener;

        SkyPrisonVisionManager vm = SkyPrisonVisionManager.Instance;
        if (vm != null)
        {
            vm.IteratePerceptionListeners(_cachedBroadcastAction);
        }
        else
        {
            // VisionManager 未初始化时的兜底路径（仅编辑器/单元测试场景）
            UnitPerceptionRuntime[] fallback = FindObjectsOfType<UnitPerceptionRuntime>();
            for (int i = 0; i < fallback.Length; i++)
                if (fallback[i] != null && fallback[i].isActiveAndEnabled)
                    BroadcastToListener(fallback[i]);
        }

        if (debugHearingBroadcastLogs)
        {
            Debug.Log(
                $"[UnitFootstepAudioEmitter] {name}: hearing broadcast {kind}/{motion}, noise={logicalNoiseStrength:0.###}, listeners={_broadcastNotifiedCount}, source={(sourceBinder != null ? sourceBinder.name : name)}.",
                this);
        }
    }

    // 场景里正常只有一个 AudioListener（主摄像机那个），之前每次脚步事件都
    // FindObjectOfType 扫一遍全场景——这个纯粹是给Inspector看的调试距离值，不值得
    // 每步都全场景扫描一次。缓存住，只有缓存为空/被销毁时才重新找一次。
    private static AudioListener _cachedAudioListener;

    private void UpdateListenerDistanceDebug(Vector3 sourcePosition, float minDistance, float maxDistance, AudioRolloffMode rolloff)
    {
        if (_cachedAudioListener == null)
            _cachedAudioListener = FindObjectOfType<AudioListener>();
        AudioListener listener = _cachedAudioListener;
        if (listener == null)
        {
            lastListenerDistanceRuntime = 0f;
            lastAuthoritativeAttenuation01Runtime = 1f;
            return;
        }

        lastListenerDistanceRuntime = Vector3.Distance(sourcePosition, listener.transform.position);
        lastAuthoritativeAttenuation01Runtime = EvaluateDistanceAttenuation01(lastListenerDistanceRuntime, minDistance, maxDistance, rolloff);
    }

    public float EvaluateLogicalAudibilityAt(Vector3 listenerPosition, float listenerHearingMultiplier = 1f)
    {
        Vector3 sourcePosition = footstepAudioPoint != null ? footstepAudioPoint.position : transform.position;
        float distance = Vector3.Distance(sourcePosition, listenerPosition);
        GetEffectiveRuntimeAttenuationSettings(out float minDistance, out float maxDistance, out AudioRolloffMode rolloff);
        float attenuation = EvaluateDistanceAttenuation01(distance, minDistance, maxDistance, rolloff);
        return Mathf.Max(0f, lastLogicalNoiseStrengthRuntime) * Mathf.Max(0f, listenerHearingMultiplier) * attenuation;
    }


    private void GetEffectiveRuntimeAttenuationSettings(out float minDistance, out float maxDistance, out AudioRolloffMode rolloff)
    {
        float packageMinDistance = Mathf.Max(0.01f, defaultMinDistance);
        float packageMaxDistance = Mathf.Max(packageMinDistance + 0.01f, defaultMaxDistance);

        if (play3D && useAuthoritative3DAttenuationWhenPlay3D)
        {
            GetEffectiveAuthoritativeDistances(packageMinDistance, packageMaxDistance, out minDistance, out maxDistance);
            rolloff = authoritativeRolloffMode;
            return;
        }

        minDistance = packageMinDistance;
        if (play3D)
            minDistance = Mathf.Max(minDistance, light3DMinDistanceFloor);

        maxDistance = Mathf.Max(minDistance + 0.01f, packageMaxDistance);
        rolloff = useLinearRolloffForFootsteps ? AudioRolloffMode.Linear : AudioRolloffMode.Logarithmic;
    }

    private void GetEffectiveAuthoritativeDistances(float packageMinDistance, float packageMaxDistance, out float minDistance, out float maxDistance)
    {
        packageMinDistance = Mathf.Max(0.01f, packageMinDistance);
        packageMaxDistance = Mathf.Max(packageMinDistance + 0.01f, packageMaxDistance);

        minDistance = Mathf.Max(packageMinDistance, authoritativeMinDistanceFloor);
        float multipliedMax = packageMaxDistance * Mathf.Max(0.01f, authoritativeMaxDistanceMultiplier);
        maxDistance = Mathf.Max(minDistance + 0.01f, multipliedMax, authoritativeMaxDistanceFloor);
    }

    public static float EvaluateDistanceAttenuation01(float distance, float minDistance, float maxDistance, AudioRolloffMode rolloffMode)
    {
        minDistance = Mathf.Max(0.01f, minDistance);
        maxDistance = Mathf.Max(minDistance + 0.01f, maxDistance);
        distance = Mathf.Max(0f, distance);

        if (distance <= minDistance)
            return 1f;

        if (distance >= maxDistance)
            return 0f;

        float t = Mathf.InverseLerp(minDistance, maxDistance, distance);

        switch (rolloffMode)
        {
            case AudioRolloffMode.Logarithmic:
            {
                // Normalized logarithmic-style curve that still reaches 0 at maxDistance,
                // so AI hearing can share a deterministic cutoff.
                float raw = minDistance / Mathf.Max(minDistance, distance);
                float rawAtMax = minDistance / maxDistance;
                return Mathf.Clamp01((raw - rawAtMax) / Mathf.Max(0.0001f, 1f - rawAtMax));
            }

            case AudioRolloffMode.Custom:
            case AudioRolloffMode.Linear:
            default:
                return Mathf.Clamp01(1f - t);
        }
    }

    private bool ShouldLogFootstepDiagnostics()
    {
#if UNITY_EDITOR
        return debugLogs || buildFootstepDiagnosticLogs;
#else
        return debugLogs || (buildFootstepDiagnosticLogs && Debug.isDebugBuild);
#endif
    }

    private SkyPrisonAudioPackage GetActivePackage()
    {
        if (footstepPackage != null)
            return footstepPackage;

        if (unitDefaultFootstepPackage != null)
            return unitDefaultFootstepPackage;

        return fallbackBarefootPackage;
    }

    private static FootstepEventKind MapSpineEventName(string eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            return FootstepEventKind.None;

        // Spine editor may show events under folders/groups, and at runtime the name can arrive as
        // "Move/Footstep_L" or "Run/Footstep_R".  The actual footstep marker is the last token.
        string key = NormalizeKey(eventName);
        string leafKey = NormalizeKey(ExtractEventLeaf(eventName));

        FootstepEventKind direct = MapNormalizedFootstepKey(leafKey);
        if (direct != FootstepEventKind.None)
            return direct;

        direct = MapNormalizedFootstepKey(key);
        if (direct != FootstepEventKind.None)
            return direct;

        // Extra safety for names like "move_footstep_l" after normalization.
        if (key.EndsWith("_footstep_l") || key.EndsWith("_foot_l") || key.EndsWith("_step_l"))
            return FootstepEventKind.Left;
        if (key.EndsWith("_footstep_r") || key.EndsWith("_foot_r") || key.EndsWith("_step_r"))
            return FootstepEventKind.Right;
        if (key.EndsWith("_jumpstep_up") || key.EndsWith("_jumpstep_u") || key.EndsWith("_jump_up") || key.EndsWith("_jump_u") || key.EndsWith("_jumpoff") || key.EndsWith("_jump_off"))
            return FootstepEventKind.JumpUp;
        if (key.EndsWith("_jumpstep_down") || key.EndsWith("_jumpstep_d") || key.EndsWith("_jump_down") || key.EndsWith("_jump_d") || key.EndsWith("_land") || key.EndsWith("_landing"))
            return FootstepEventKind.Land;

        return FootstepEventKind.None;
    }

    private static FootstepEventKind MapNormalizedFootstepKey(string key)
    {
        switch (key)
        {
            case "footstep_l":
            case "footstep_left":
            case "foot_l":
            case "step_l":
                return FootstepEventKind.Left;

            case "footstep_r":
            case "footstep_right":
            case "foot_r":
            case "step_r":
                return FootstepEventKind.Right;

            case "jumpstep_up":
            case "jumpstep_u":
            case "jump_up":
            case "jump_u":
            case "jumpoff":
            case "jump_off":
                return FootstepEventKind.JumpUp;

            case "jumpstep_down":
            case "jumpstep_d":
            case "jump_down":
            case "jump_d":
            case "land":
            case "landing":
                return FootstepEventKind.Land;

            default:
                return FootstepEventKind.None;
        }
    }

    private static string ExtractEventLeaf(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        int slash = raw.LastIndexOf('/');
        int backslash = raw.LastIndexOf('\\');
        int index = Mathf.Max(slash, backslash);

        if (index >= 0 && index + 1 < raw.Length)
            return raw.Substring(index + 1);

        return raw;
    }

    private SkyPrisonAudioTrack FindTrackForKind(SkyPrisonAudioPackage package, FootstepEventKind kind, FootstepMotionKind motion)
    {
        return FindTrackForKind(package, kind, motion, "");
    }

    private SkyPrisonAudioTrack FindTrackForKind(SkyPrisonAudioPackage package, FootstepEventKind kind, FootstepMotionKind motion, string requiredRuntimeLayerKey)
    {
        matchBuffer.Clear();

        SkyPrisonAudioFootSide side = ToFootSide(kind);
        string[] candidates = GetLayerCandidates(kind, motion);
        string normalizedRuntimeLayerKey = NormalizeKey(requiredRuntimeLayerKey);

        for (int c = 0; c < candidates.Length; c++)
        {
            string candidate = NormalizeKey(candidates[c]);
            if (string.IsNullOrEmpty(candidate))
                continue;

            SkyPrisonAudioTrack found = FindExactTrack(package, candidate, side, normalizedRuntimeLayerKey);
            if (found != null)
                return found;
        }

        return null;
    }

    private static SkyPrisonAudioTrack FindExactTrack(SkyPrisonAudioPackage package, string normalizedCandidate, SkyPrisonAudioFootSide side, string normalizedRequiredRuntimeLayerKey)
    {
        if (package == null || package.tracks == null)
            return null;

        for (int i = 0; i < package.tracks.Count; i++)
        {
            SkyPrisonAudioTrack track = package.tracks[i];
            if (track == null || track.locked)
                continue;

            if (!string.IsNullOrEmpty(normalizedRequiredRuntimeLayerKey) && NormalizeKey(track.runtimeLayerKey) != normalizedRequiredRuntimeLayerKey)
                continue;

            // JumpUp / Land are side-neutral events.  Do not let an accidental LeftOnly/RightOnly
            // setting on jump tracks block playback.  Left/Right footstep events still respect side conditions.
            if (side != SkyPrisonAudioFootSide.None && !SkyPrisonAudioPackageRuntimeQuery.FootSideMatches(track.footSideCondition, side))
                continue;

            foreach (string identityKey in GetTrackIdentityKeys(track))
            {
                if (identityKey == normalizedCandidate)
                    return track;
            }
        }

        return null;
    }

    // Prefer real identity/name fields first. Different audio-package model versions used different names,
    // so reflection keeps this compatible with older assets and scripts.
    private static readonly string[] IdentityMemberNames =
    {
        "trackId", "trackID", "trackKey", "key", "id", "name",
        "displayName",
        "runtimeLayerKey"
    };

    // 之前这里是个 yield return 的 IEnumerable，每次调用都：new一个string[8]数组、
    // new一个HashSet、生成一个迭代器状态机对象，外加每个候选字段名都走一次未缓存的
    // 反射(GetField/GetProperty，反射调用本身也会分配)。这个方法在每一次脚步/落地/
    // 跳跃事件里，对每个候选音轨都要跑一遍，乘以场上所有会走路的单位，是持续GC压力
    // 的主要来源之一。轨道的身份字段运行时不会变，按轨道对象本身缓存一次结果，
    // 之后所有调用直接返回同一个数组，不再重复分配/反射。
    private static readonly Dictionary<SkyPrisonAudioTrack, string[]> _trackIdentityKeyCache = new Dictionary<SkyPrisonAudioTrack, string[]>();

    private static string[] GetTrackIdentityKeys(SkyPrisonAudioTrack track)
    {
        if (track == null)
            return System.Array.Empty<string>();

        if (_trackIdentityKeyCache.TryGetValue(track, out string[] cached))
            return cached;

        var result = new List<string>(IdentityMemberNames.Length);
        var emitted = new HashSet<string>();
        for (int i = 0; i < IdentityMemberNames.Length; i++)
        {
            string value = ReadStringMember(track, IdentityMemberNames[i]);
            string normalized = NormalizeKey(value);
            if (string.IsNullOrEmpty(normalized) || emitted.Contains(normalized))
                continue;

            emitted.Add(normalized);
            result.Add(normalized);
        }

        string[] keys = result.ToArray();
        _trackIdentityKeyCache[track] = keys;
        return keys;
    }

    // 反射本身没缓存的话每次调用GetField/GetProperty都要重新走一遍类型元数据查找
    // （且反射调用内部也会有分配）——按(类型,字段名)缓存解析结果，只在每个类型+
    // 字段名组合第一次被用到时真正反射一次。
    private static readonly Dictionary<(Type type, string member), object> _reflectedMemberCache = new Dictionary<(Type, string), object>();

    private static string ReadStringMember(object target, string memberName)
    {
        if (target == null || string.IsNullOrWhiteSpace(memberName))
            return string.Empty;

        Type type = target.GetType();
        var cacheKey = (type, memberName);

        if (!_reflectedMemberCache.TryGetValue(cacheKey, out object member))
        {
            System.Reflection.FieldInfo field = type.GetField(
                memberName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

            if (field != null && field.FieldType == typeof(string))
            {
                member = field;
            }
            else
            {
                System.Reflection.PropertyInfo property = type.GetProperty(
                    memberName,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

                if (property != null && property.PropertyType == typeof(string) && property.GetIndexParameters().Length == 0)
                    member = property;
            }

            _reflectedMemberCache[cacheKey] = member; // null也缓存，避免每次都重新查一遍确认"这个类型没有这个字段"
        }

        if (member is System.Reflection.FieldInfo fi)
            return fi.GetValue(target) as string;
        if (member is System.Reflection.PropertyInfo pi)
            return pi.GetValue(target, null) as string;

        return string.Empty;
    }

    // 之前每次调用都 new 一个 string[] 数组字面量——每次脚步/跳跃/落地事件都会触发，
    // 乘以场上所有会走路的单位，是持续GC压力的来源之一。这些候选列表内容固定不变，
    // 提到静态只读字段，所有调用共享同一份数组实例，不再重复分配。
    private static readonly string[] LeftRunCandidates    = { "footstep_run_l", "run_l", "footstep_l", "foot_l", "step_l", "footstep", "foot", "default" };
    private static readonly string[] LeftSneakCandidates  = { "footstep_sneak_l", "sneak_l", "footstep_l", "foot_l", "step_l", "footstep", "foot", "default" };
    private static readonly string[] LeftWalkCandidates   = { "footstep_walk_l", "walk_l", "footstep_l", "foot_l", "step_l", "footstep", "foot", "default" };
    private static readonly string[] RightRunCandidates   = { "footstep_run_r", "run_r", "footstep_r", "foot_r", "step_r", "footstep", "foot", "default" };
    private static readonly string[] RightSneakCandidates = { "footstep_sneak_r", "sneak_r", "footstep_r", "foot_r", "step_r", "footstep", "foot", "default" };
    private static readonly string[] RightWalkCandidates  = { "footstep_walk_r", "walk_r", "footstep_r", "foot_r", "step_r", "footstep", "foot", "default" };
    private static readonly string[] JumpUpCandidates     = { "jumpstep_up", "jumpstep_u", "jump_up", "jump_u", "footstep_jump_up", "default" };
    private static readonly string[] LandCandidates       = { "jumpstep_down", "jumpstep_d", "land", "landing", "jump_down", "jump_d", "footstep_land", "footstep", "default" };
    private static readonly string[] DefaultOnlyCandidate = { "default" };

    private static string[] GetLayerCandidates(FootstepEventKind kind, FootstepMotionKind motion)
    {
        // Optional fine-grained tracks are supported, but not required.
        // First try motion-specific names, then fall back to the simple V1 two-track setup: footstep_l / footstep_r.
        switch (kind)
        {
            case FootstepEventKind.Left:
                switch (motion)
                {
                    case FootstepMotionKind.Run:   return LeftRunCandidates;
                    case FootstepMotionKind.Sneak: return LeftSneakCandidates;
                    default:                       return LeftWalkCandidates;
                }

            case FootstepEventKind.Right:
                switch (motion)
                {
                    case FootstepMotionKind.Run:   return RightRunCandidates;
                    case FootstepMotionKind.Sneak: return RightSneakCandidates;
                    default:                       return RightWalkCandidates;
                }

            case FootstepEventKind.JumpUp:
                return JumpUpCandidates;

            case FootstepEventKind.Land:
                return LandCandidates;

            default:
                return DefaultOnlyCandidate;
        }
    }

    private static SkyPrisonAudioFootSide ToFootSide(FootstepEventKind kind)
    {
        switch (kind)
        {
            case FootstepEventKind.Left:
                return SkyPrisonAudioFootSide.Left;
            case FootstepEventKind.Right:
                return SkyPrisonAudioFootSide.Right;
            default:
                return SkyPrisonAudioFootSide.None;
        }
    }

    private SkyPrisonAudioSegment PickSegment(SkyPrisonAudioTrack track, bool avoidRepeat)
    {
        if (track == null || track.segments == null || track.segments.Count == 0)
            return null;

        List<SkyPrisonAudioSegment> valid = new List<SkyPrisonAudioSegment>();
        float totalWeight = 0f;

        SkyPrisonAudioSegment last = null;
        lastSegmentByTrack.TryGetValue(track, out last);

        for (int i = 0; i < track.segments.Count; i++)
        {
            SkyPrisonAudioSegment segment = track.segments[i];
            if (segment == null || segment.sourceClip == null)
                continue;

            if (avoidRepeat && track.segments.Count > 1 && segment == last)
                continue;

            valid.Add(segment);
            totalWeight += Mathf.Max(0.0001f, segment.randomWeight);
        }

        // If every valid segment was filtered by anti-repeat, fall back to the full valid list.
        if (valid.Count == 0)
        {
            totalWeight = 0f;
            for (int i = 0; i < track.segments.Count; i++)
            {
                SkyPrisonAudioSegment segment = track.segments[i];
                if (segment == null || segment.sourceClip == null)
                    continue;

                valid.Add(segment);
                totalWeight += Mathf.Max(0.0001f, segment.randomWeight);
            }
        }

        if (valid.Count == 0)
            return null;

        SkyPrisonAudioSegment picked;
        if (valid.Count == 1 || totalWeight <= 0f)
        {
            picked = valid[0];
        }
        else
        {
            float roll = UnityEngine.Random.Range(0f, totalWeight);
            float acc = 0f;
            picked = valid[valid.Count - 1];

            for (int i = 0; i < valid.Count; i++)
            {
                acc += Mathf.Max(0.0001f, valid[i].randomWeight);
                if (roll <= acc)
                {
                    picked = valid[i];
                    break;
                }
            }
        }

        lastSegmentByTrack[track] = picked;
        return picked;
    }

    private FootstepMotionKind ResolveMotionKind(FootstepEventKind kind)
    {
        if (kind == FootstepEventKind.JumpUp)
            return FootstepMotionKind.JumpUp;

        if (kind == FootstepEventKind.Land)
            return FootstepMotionKind.Land;

        if (movementController == null)
            movementController = GetComponent<UnitMovementController>();

        if (movementController != null)
        {
            if (movementController.IsSneakHeld)
                return FootstepMotionKind.Sneak;

            if (movementController.IsRunHeld && movementController.CanRun)
                return FootstepMotionKind.Run;
        }

        return FootstepMotionKind.Walk;
    }

    private bool ShouldUseBuildAudibleFootstepPlaybackSafety()
    {
        // V24: this safety layer was useful while Build playback was silent, but it overrides the real 3D range
        // with large floors such as max=160 and destroys distance perception. Keep the method for compatibility
        // with older serialized data and debug UI, but make it permanently inactive.
        return false;
    }

    private float GetEffectiveBuildAudibleFootstepSpatialBlend()
    {
        SkyPrisonAudioGlobalSettings settings = GetGlobalAudioSettings();
        float value = settings != null ? settings.buildAudibleFootstepSpatialBlend : buildAudibleFootstepSpatialBlend;
        return Mathf.Clamp01(value);
    }

    private float GetEffectiveBuildAudibleFootstepMinDistanceFloor()
    {
        SkyPrisonAudioGlobalSettings settings = GetGlobalAudioSettings();
        float value = settings != null ? settings.buildAudibleFootstepMinDistanceFloor : buildAudibleFootstepMinDistanceFloor;
        return Mathf.Max(0.01f, value);
    }

    private float GetEffectiveBuildAudibleFootstepMaxDistanceFloor()
    {
        SkyPrisonAudioGlobalSettings settings = GetGlobalAudioSettings();
        float value = settings != null ? settings.buildAudibleFootstepMaxDistanceFloor : buildAudibleFootstepMaxDistanceFloor;
        return Mathf.Max(0.01f, value);
    }

    private SkyPrisonAudioGlobalSettings GetGlobalAudioSettings()
    {
        return SkyPrisonAudioGlobalSettings.Instance;
    }

    private float GetEffectiveFootstepOutputMasterMultiplier()
    {
        SkyPrisonAudioGlobalSettings settings = GetGlobalAudioSettings();
        if (settings != null && settings.useGlobalFootstepSettings)
            return settings.GetFootstepOutputMultiplier(globalVolumeMultiplier);

        return Mathf.Max(0f, globalVolumeMultiplier);
    }

    private float GetEffectiveFootstepNoiseMasterMultiplier()
    {
        SkyPrisonAudioGlobalSettings settings = GetGlobalAudioSettings();
        if (settings != null && settings.useGlobalFootstepSettings)
            return settings.GetFootstepNoiseMultiplier(globalVolumeMultiplier);

        return Mathf.Max(0f, globalVolumeMultiplier);
    }

    private float GetEffectiveBaseFootwearVolumeMultiplierWhenGroundSurfaceActive()
    {
        SkyPrisonAudioGlobalSettings settings = GetGlobalAudioSettings();
        if (settings != null && settings.useGlobalFootstepSettings && settings.overrideEmitterSurfaceDucking)
            return Mathf.Clamp01(settings.baseFootwearVolumeMultiplierWhenGroundSurfaceActive);

        return Mathf.Clamp01(baseFootwearVolumeMultiplierWhenGroundSurfaceActive);
    }

    private float GetEffectiveBaseFootwearNoiseMultiplierWhenGroundSurfaceActive()
    {
        SkyPrisonAudioGlobalSettings settings = GetGlobalAudioSettings();
        if (settings != null && settings.useGlobalFootstepSettings && settings.overrideEmitterSurfaceDucking)
            return Mathf.Clamp01(settings.baseFootwearNoiseMultiplierWhenGroundSurfaceActive);

        return Mathf.Clamp01(baseFootwearNoiseMultiplierWhenGroundSurfaceActive);
    }

    private float GetEffectiveGroundSurfaceLayerVolumeMultiplier()
    {
        SkyPrisonAudioGlobalSettings settings = GetGlobalAudioSettings();
        if (settings != null && settings.useGlobalFootstepSettings && settings.overrideEmitterGroundSurfaceMix)
            return Mathf.Max(0f, settings.groundSurfaceLayerVolumeMultiplier);

        return Mathf.Max(0f, groundSurfaceLayerVolumeMultiplier);
    }

    private float GetEffectiveGroundSurfaceLayerNoiseMultiplier()
    {
        SkyPrisonAudioGlobalSettings settings = GetGlobalAudioSettings();
        if (settings != null && settings.useGlobalFootstepSettings && settings.overrideEmitterGroundSurfaceMix)
            return Mathf.Max(0f, settings.groundSurfaceLayerNoiseMultiplier);

        return Mathf.Max(0f, groundSurfaceLayerNoiseMultiplier);
    }

    private float GetMotionVolumeMultiplier(FootstepMotionKind motion)
    {
        SkyPrisonAudioGlobalSettings settings = GetGlobalAudioSettings();
        bool useGlobalMotion = settings != null && settings.useGlobalFootstepSettings && settings.overrideEmitterMotionLoudness;

        switch (motion)
        {
            case FootstepMotionKind.Run:
                return Mathf.Max(0f, useGlobalMotion ? settings.runVolumeMultiplier : runVolumeMultiplier);
            case FootstepMotionKind.Sneak:
                return Mathf.Max(0f, useGlobalMotion ? settings.sneakVolumeMultiplier : sneakVolumeMultiplier);
            case FootstepMotionKind.JumpUp:
                return Mathf.Max(0f, useGlobalMotion ? settings.jumpUpVolumeMultiplier : jumpUpVolumeMultiplier);
            case FootstepMotionKind.Land:
                return Mathf.Max(0f, useGlobalMotion ? settings.landVolumeMultiplier : landVolumeMultiplier);
            default:
                return Mathf.Max(0f, useGlobalMotion ? settings.walkVolumeMultiplier : walkVolumeMultiplier);
        }
    }

    private float GetMotionPitchMultiplier(FootstepMotionKind motion)
    {
        switch (motion)
        {
            case FootstepMotionKind.Run:
                return Mathf.Max(0.01f, runPitchMultiplier);
            case FootstepMotionKind.Sneak:
                return Mathf.Max(0.01f, sneakPitchMultiplier);
            case FootstepMotionKind.JumpUp:
                return Mathf.Max(0.01f, jumpUpPitchMultiplier);
            case FootstepMotionKind.Land:
                return Mathf.Max(0.01f, landPitchMultiplier);
            default:
                return Mathf.Max(0.01f, walkPitchMultiplier);
        }
    }

    private float GetFootPanOffset(FootstepEventKind kind)
    {
        switch (kind)
        {
            case FootstepEventKind.Left:
                return leftFootPanOffset;
            case FootstepEventKind.Right:
                return rightFootPanOffset;
            default:
                return 0f;
        }
    }

    private static float SafeRandomRange(Vector2 range, float fallback)
    {
        float min = range.x;
        float max = range.y;

        if (float.IsNaN(min) || float.IsInfinity(min) || float.IsNaN(max) || float.IsInfinity(max))
            return fallback;

        if (min <= 0f && max <= 0f)
            return fallback;

        if (max < min)
        {
            float tmp = min;
            min = max;
            max = tmp;
        }

        min = Mathf.Max(0.01f, min);
        max = Mathf.Max(min, max);
        return UnityEngine.Random.Range(min, max);
    }

    private static string NormalizeKey(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        return raw.Trim().Replace(" ", "_").Replace("-", "_").ToLowerInvariant();
    }
}
