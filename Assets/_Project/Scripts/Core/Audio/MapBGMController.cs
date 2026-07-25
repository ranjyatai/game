using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 地图专属BGM播放器。配两份曲目清单：探索曲目、战斗曲目，直接拖AudioClip，不走
/// SkyPrisonAudioPackage那套调音台（Track/Segment/MixerChannel）——BGM不需要混音
/// 分层，用那套太重了。
///
/// 正常不用手动挂——地图编辑器"地图BGM"表单配好清单后，运行时会从
/// MapBGMRegistry（编辑器保存MapDefinition时自动同步）按当前场景自动生成本组件，
/// 跟触发器包（TriggerPackageRuntime/MapTriggerRegistry）同一套模式。也可以手动
/// 挂在场景里直接在Inspector填清单，两种方式都支持，互不冲突。
///
/// 进场景自动开始播（探索曲目），离开场景（场景本身销毁）自动停，不用额外清理。
/// 同一层可以放多首曲子，随机/顺序轮播，一首放完自动挑下一首。
///
/// 曲层切换两条路都支持：
///   1. 自动——订阅 PlayerCombatStateRuntime.OnCombatStateChanged（任意敌人看见
///      玩家时自动切战斗曲目，脱战后延迟切回探索），默认行为。
///   2. 手动——地图触发器包里用 act_set_bgm_layer 行动句型显式调用 SetLayer()，
///      给"安全屋强制探索曲"、"boss房强制战斗曲"这类地图作者想手动控制的场景用，
///      优先级比自动切换高（调用后会暂停自动订阅的响应，直到下一次真正的战斗
///      状态变化）。
/// </summary>
[DisallowMultipleComponent]
public class MapBGMController : MonoBehaviour
{
    public const string ExploreLayerKey = "explore";
    public const string CombatLayerKey = "combat";

    public static MapBGMController Instance { get; private set; }

    private const string RuntimeObjectName = "SkyPrisonMapBGM";
    private static bool _sceneLoadedSubscribed;

    public enum PlayMode
    {
        [InspectorName("随机")] Random = 0,
        [InspectorName("顺序")] Sequential = 1,
    }

    // ── 自动挂载（从 MapBGMRegistry 按当前场景生成）────────────────────────────

    // 关闭域重载（Enter Play Mode Options）时静态字段不会自动归零，这里手动重置，
    // 避免上一次 Play 残留的订阅状态导致本次不再生成节点——跟 TriggerPackageRuntime
    // 同一个坑，见 misdiagnosed_bgm_probe_clip 那次排查记录。
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _sceneLoadedSubscribed = false;
        MapBGMRegistry.ClearCache();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreateAfterSceneLoad()
    {
        if (!Application.isPlaying)
            return;

        EnsureForActiveScene();

        if (!_sceneLoadedSubscribed)
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
            _sceneLoadedSubscribed = true;
        }
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (Application.isPlaying)
            EnsureForActiveScene();
    }

    private static void EnsureForActiveScene()
    {
        if (Instance != null)
            return; // 已经有一个了（手动挂的，或者已经自动生成过）

        Scene scene = SceneManager.GetActiveScene();

        MapBGMRegistry registry = MapBGMRegistry.LoadOrNull();
        if (registry == null)
            return;

        MapBGMRegistry.Entry entry = registry.GetForScene(scene.name, scene.path);
        if (entry == null)
            return;

        GameObject go = new GameObject(RuntimeObjectName);
        MapBGMController controller = go.AddComponent<MapBGMController>();
        controller.Configure(entry);
    }

    /// <summary>由 MapBGMRegistry 自动生成时调用，把注册表条目里的配置灌进来。
    /// 手动挂在场景里、直接在Inspector填清单的用法不受影响，不会调用这个方法。</summary>
    public void Configure(MapBGMRegistry.Entry entry)
    {
        if (entry == null) return;

        exploreClips = entry.exploreClips ?? new List<AudioClip>();
        combatClips = entry.combatClips ?? new List<AudioClip>();
        playMode = entry.sequentialPlayMode ? PlayMode.Sequential : PlayMode.Random;
        crossfadeDuration = Mathf.Max(0.1f, entry.crossfadeDuration);
        authoredVolume = Mathf.Clamp(entry.volume, 0f, 2f);
    }

    [Header("曲目清单")]
    [Tooltip("探索状态下轮播的曲目，可以放多首。")]
    [SerializeField] private List<AudioClip> exploreClips = new List<AudioClip>();
    [Tooltip("战斗状态下轮播的曲目，可以放多首。留空时进入战斗会维持探索曲目不切。")]
    [SerializeField] private List<AudioClip> combatClips = new List<AudioClip>();
    [SerializeField] private PlayMode playMode = PlayMode.Random;

    [Header("播放参数")]
    [Tooltip("两首歌之间的淡入淡出时长（秒）——切歌、切战斗/探索状态时都用这个。")]
    [SerializeField, Min(0.1f)] private float crossfadeDuration = 3f;

    [Tooltip("进场景后延迟多久开始播——留一点时间让场景其它系统（摄像机/AudioListener）先就位。")]
    [SerializeField, Min(0f)] private float startDelay = 0.5f;

    [Range(0f, 2f)]
    [SerializeField] private float authoredVolume = 1f;

    [Header("调试")]
    [SerializeField] private bool debugLogs = false;

    private AudioSource _sourceA;
    private AudioSource _sourceB;
    private AudioSource _activeSource;
    private AudioSource _idleSource => _activeSource == _sourceA ? _sourceB : _sourceA;

    private string _currentLayerKey = ExploreLayerKey;
    private AudioClip _currentClip;
    private int _lastPickIndex = -1;
    private Coroutine _crossfadeRoutine;
    private bool _manualOverride;

    // 记住每首"被切走时还没放完"的曲子播到哪了——探索1切到探索2再切回探索1时接着
    // 播，不是从头开始。只记"被打断"的，自然放完的那首会被移除（下次轮到就该从头，
    // 不是"续播"）。只在这个组件的生命周期内有效（离开地图场景/组件销毁就清空，
    // 不做跨场景/跨存档持久化——没人要求这层，不过度设计）。
    private readonly Dictionary<AudioClip, float> _resumePositions = new Dictionary<AudioClip, float>();

    private void Awake()
    {
        Instance = this;
        _sourceA = CreateSource("BGM_SourceA");
        _sourceB = CreateSource("BGM_SourceB");
        _activeSource = _sourceA;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private AudioSource CreateSource(string childName)
    {
        var go = new GameObject(childName);
        go.transform.SetParent(transform, false);
        var source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;
        source.volume = 0f;
        return source;
    }

    private void OnEnable()
    {
        PlayerCombatStateRuntime.OnCombatStateChanged += HandleCombatStateChanged;
    }

    private void OnDisable()
    {
        PlayerCombatStateRuntime.OnCombatStateChanged -= HandleCombatStateChanged;
    }

    private IEnumerator Start()
    {
        if (startDelay > 0f)
            yield return new WaitForSeconds(startDelay);

        _currentLayerKey = PlayerCombatStateRuntime.IsInCombat ? CombatLayerKey : ExploreLayerKey;
        PlayNextClip();
    }

    private void Update()
    {
        if (_activeSource == null || _currentClip == null)
            return;

        // 当前曲子自然放完了（没有在做切歌淡入淡出的过程中）——挑下一首继续，
        // 实现"这一层的几首歌轮着放"。
        if (_crossfadeRoutine == null && !_activeSource.isPlaying)
        {
            PlayNextClip();
            return;
        }

        if (_crossfadeRoutine == null)
            _activeSource.volume = authoredVolume * GetGlobalBgmVolumeMultiplier();
    }

    private static float GetGlobalBgmVolumeMultiplier()
    {
        var gs = SkyPrisonAudioGlobalSettings.Instance;
        if (gs == null) return 1f;
        return Mathf.Max(0f, gs.masterVolume) * Mathf.Max(0f, gs.bgmVolume);
    }

    private void HandleCombatStateChanged(bool inCombat)
    {
        if (_manualOverride)
        {
            // 手动指定过曲层之后，只有"真正的战斗状态变化"才会解除覆盖——避免
            // 触发器刚强制切完，紧接着又被自动订阅立刻切回去。
            _manualOverride = false;
        }

        SwitchToLayer(inCombat ? CombatLayerKey : ExploreLayerKey);
    }

    /// <summary>给 act_set_bgm_layer 触发器行动句型调用的公开入口。</summary>
    public void SetLayer(string layerKey)
    {
        _manualOverride = true;
        SwitchToLayer(layerKey);
    }

    private void SwitchToLayer(string layerKey)
    {
        if (layerKey == _currentLayerKey)
            return;

        _currentLayerKey = layerKey;
        if (debugLogs) Debug.Log($"[MapBGMController] 曲层切换 → {_currentLayerKey}", this);
        PlayNextClip();
    }

    private void PlayNextClip()
    {
        AudioClip next = PickNextClip();
        if (next == null)
        {
            if (debugLogs) Debug.LogWarning($"[MapBGMController] 曲层「{_currentLayerKey}」下没有配曲目，维持当前播放。", this);
            return;
        }

        // 这首曲子如果之前被切走过、还没放完，接着放；没记录过（或者上次是自然放完
        // 的）就从头开始。
        float resumeFrom = _resumePositions.TryGetValue(next, out float saved) ? saved : 0f;
        CrossfadeToClip(next, resumeFrom);
        if (debugLogs) Debug.Log($"[MapBGMController] 播放「{next.name}」（曲层={_currentLayerKey}，续播于{resumeFrom:0.##}秒）", this);
    }

    /// <summary>给 act_play_bgm_track_from_start / act_play_bgm_track_at_time 触发器行动
    /// 句型调用的公开入口——直接播放指定曲子，跳过当前曲层的轮播清单。这首播完之后，
    /// Update() 会按正常逻辑从当前曲层清单里挑下一首继续（不会卡在这首放完后没声音）。
    /// 这里的起始秒数是调用方明确指定的，不查续播记忆——触发器主动要求"从头"/"从
    /// 第几分几秒"就该照做，不该被之前的播放记忆覆盖。</summary>
    public void PlaySpecificClip(AudioClip clip, float startTimeSeconds)
    {
        if (clip == null)
            return;

        CrossfadeToClip(clip, startTimeSeconds);
        if (debugLogs) Debug.Log($"[MapBGMController] 触发器指定播放「{clip.name}」，起始秒数={startTimeSeconds:0.##}", this);
    }

    private void CrossfadeToClip(AudioClip clip, float startTimeSeconds)
    {
        RememberCurrentPosition();

        _currentClip = clip;

        AudioSource target = _idleSource;
        target.clip = clip;
        target.time = Mathf.Clamp(startTimeSeconds, 0f, Mathf.Max(0f, clip.length - 0.01f));
        target.Play();

        if (_crossfadeRoutine != null)
            StopCoroutine(_crossfadeRoutine);
        _crossfadeRoutine = StartCoroutine(CrossfadeRoutine(_activeSource, target, authoredVolume));
        _activeSource = target;
    }

    // 在真正切到下一首之前，把"当前这首"的播放进度存起来（如果它是被打断的，也就是
    // 还在播）；如果是自然放完才走到这一步的（Update()里isPlaying已经是false），
    // 就清掉它的记录——放完了不算"被打断"，下次该从头开始。
    private void RememberCurrentPosition()
    {
        if (_currentClip == null || _activeSource == null)
            return;

        if (_activeSource.isPlaying)
            _resumePositions[_currentClip] = _activeSource.time;
        else
            _resumePositions.Remove(_currentClip);
    }

    // 等功率淡入淡出（equal-power crossfade）：用 sin/cos 曲线而不是直线插值——
    // 两条独立音轨线性交叉淡变时，中间那一刻两边音量都没顶满，总响度会有一下
    // 能感觉到的"塌陷"，专业音频/DJ软件的crossfade都用这条sin/cos曲线（任意时刻
    // sin²+cos²=1，总能量基本恒定）来避免这个问题。
    private IEnumerator CrossfadeRoutine(AudioSource from, AudioSource to, float toTargetAuthoredVolume)
    {
        float fromStartVolume = from.volume;
        float duration = Mathf.Max(0.01f, crossfadeDuration);
        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;
            float ratio = Mathf.Clamp01(t / duration);
            float fadeIn = Mathf.Sin(ratio * Mathf.PI * 0.5f);
            float fadeOut = Mathf.Cos(ratio * Mathf.PI * 0.5f);
            float globalMul = GetGlobalBgmVolumeMultiplier();
            to.volume = fadeIn * toTargetAuthoredVolume * globalMul;
            if (from != to)
                from.volume = fadeOut * fromStartVolume;
            yield return null;
        }

        to.volume = toTargetAuthoredVolume * GetGlobalBgmVolumeMultiplier();
        if (from != to)
        {
            from.volume = 0f;
            from.Stop();
        }

        _crossfadeRoutine = null;
    }

    private AudioClip PickNextClip()
    {
        List<AudioClip> pool = _currentLayerKey == CombatLayerKey ? combatClips : exploreClips;
        List<AudioClip> matching = new List<AudioClip>();
        if (pool != null)
        {
            foreach (var clip in pool)
                if (clip != null)
                    matching.Add(clip);
        }

        if (matching.Count == 0)
            return null;

        if (matching.Count == 1)
        {
            _lastPickIndex = 0;
            return matching[0];
        }

        if (playMode == PlayMode.Sequential)
        {
            _lastPickIndex = (_lastPickIndex + 1) % matching.Count;
            return matching[_lastPickIndex];
        }

        int pick;
        do
        {
            pick = UnityEngine.Random.Range(0, matching.Count);
        } while (pick == _lastPickIndex);

        _lastPickIndex = pick;
        return matching[pick];
    }
}
