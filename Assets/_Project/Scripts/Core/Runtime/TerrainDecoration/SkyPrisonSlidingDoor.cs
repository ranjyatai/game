using UnityEngine;
using UnityEngine.Events;

/// <summary>门怎么被触发。</summary>
public enum DoorTriggerMode
{
    [InspectorName("靠近自动")] Proximity = 0,
    [InspectorName("玩家交互(按E)")] Interact = 1,
    [InspectorName("外部控制(演出/触发器)")] External = 2,
    [InspectorName("由远处的开关控制")] Switch = 3,
}

/// <summary>门开了以后怎么关。</summary>
public enum DoorCloseMode
{
    [InspectorName("玩家离开后关")] LeaveRadius = 0,
    [InspectorName("定时关")] Timed = 1,
    [InspectorName("永远不关")] Never = 2,
    [InspectorName("再交互才关(开关型)")] ToggleByInteract = 3,
}

/// <summary>门/开关给玩家看的文字：查 UILocalizationTable（和 NPC 交互提示同一套），查不到用中文兜底。</summary>
public static class DoorText
{
    public static string Get(string key, string fallback)
    {
        var table = Resources.Load<UILocalizationTable>("UILocalizationTable");
        return table != null ? table.Get(key, fallback) : fallback;
    }
}

/// <summary>
/// 可按语言逐个填写的自定义提示文字。每种语言都留空 = 用多语言字典表里的默认文字（defaultKey）。
/// 只填了部分语言时，缺的语言退回默认语言的那条。
/// </summary>
[System.Serializable]
public class DoorLocalizedLabel
{
    public System.Collections.Generic.List<LocalizedTextEntry> texts = new System.Collections.Generic.List<LocalizedTextEntry>();

    public bool HasAny()
    {
        if (texts == null) return false;
        foreach (var t in texts) if (t != null && !string.IsNullOrEmpty(t.text)) return true;
        return false;
    }

    public string Resolve(string defaultKey, string fallback)
    {
        if (HasAny())
        {
            var rt = LocalizationRuntime.Instance;
            string r = rt != null ? rt.GetText(texts, "") : "";
            if (string.IsNullOrEmpty(r))
                foreach (var t in texts) if (t != null && !string.IsNullOrEmpty(t.text)) { r = t.text; break; }
            if (!string.IsNullOrEmpty(r)) return r;
        }
        return Get(defaultKey, fallback);
    }

    private static string Get(string key, string fallback) { return DoorText.Get(key, fallback); }
}

/// <summary>标在 TriggerPackage 字段上：属性面板里用"触发器显示名称"的下拉选择，而不是资源文件名。</summary>
public sealed class TriggerPackageNamePickerAttribute : PropertyAttribute { }

public enum DoorConditionKind
{
    [InspectorName("变量")] Variable = 0,
    [InspectorName("变量(旧)")] NumberVariable = 1,   // 兼容：和 Variable 一样按变量表里的类型判断
    [InspectorName("任务标记")] QuestFlag = 2,
}

public enum DoorConditionMatch
{
    [InspectorName("全部满足")] All = 0,
    [InspectorName("任一满足")] Any = 1,
}

public enum DoorCompareOp
{
    [InspectorName("等于")] Equal = 0,
    [InspectorName("不等于")] NotEqual = 1,
    [InspectorName("大于")] Greater = 2,
    [InspectorName("大于等于")] GreaterEqual = 3,
    [InspectorName("小于")] Less = 4,
    [InspectorName("小于等于")] LessEqual = 5,
}

/// <summary>
/// 门/开关的解锁条件。变量取自触发器那套自定义变量（变量表，存档里），任务标记取自玩家存档。
/// 一扇门可以挂多条，必须全部满足才开得了；任何一条不满足 = 锁着。
/// </summary>
[System.Serializable]
public class DoorCondition
{
    public DoorConditionKind kind = DoorConditionKind.Variable;
    [Tooltip("变量名（布尔/数字变量）或任务标记 Key。")]
    public string variableName = "";
    [Tooltip("布尔变量：要求等于这个值。")]
    public bool boolValue = true;
    public DoorCompareOp op = DoorCompareOp.GreaterEqual;
    [Tooltip("数字变量：和这个数比较。")]
    public float number = 1f;

    /// <summary>变量按变量表里声明的类型判断：布尔比真假，整数和浮点数按比较符比大小。</summary>
    public bool Evaluate()
    {
        if (string.IsNullOrWhiteSpace(variableName)) return true;   // 没填 = 不限制
        if (kind == DoorConditionKind.QuestFlag)
            return SaveManager.Player != null && SaveManager.Player.HasQuestFlag(variableName);

        var db = VariableDatabase.LoadOrNull();
        var decl = db != null ? db.FindByName(variableName) : null;
        if (decl == null) return false;   // 变量表里没有这个变量：当作不满足，避免误放行

        switch (decl.type)
        {
            case LogicSlotValueType.Bool:
                return SkyPrisonVariableRuntime.GetBool(variableName) == boolValue;
            case LogicSlotValueType.Int:
                return Compare(SkyPrisonVariableRuntime.GetInt(variableName), Mathf.RoundToInt(number));
            default:
                return Compare(SkyPrisonVariableRuntime.GetFloat(variableName), number);
        }
    }

    private bool Compare(float v, float rhs)
    {
        switch (op)
        {
            case DoorCompareOp.Equal: return Mathf.Approximately(v, rhs);
            case DoorCompareOp.NotEqual: return !Mathf.Approximately(v, rhs);
            case DoorCompareOp.Greater: return v > rhs;
            case DoorCompareOp.GreaterEqual: return v >= rhs;
            case DoorCompareOp.Less: return v < rhs;
            default: return v <= rhs;
        }
    }

    /// <summary>没有任何有效条件 = 通过；全部满足 / 任一满足 由 match 决定。</summary>
    public static bool AllMet(System.Collections.Generic.List<DoorCondition> list, DoorConditionMatch match = DoorConditionMatch.All)
    {
        if (list == null) return true;
        bool any = false, anyPass = false;
        foreach (var c in list)
        {
            if (c == null || string.IsNullOrWhiteSpace(c.variableName)) continue;
            any = true;
            bool ok = c.Evaluate();
            if (match == DoorConditionMatch.All && !ok) return false;
            if (ok) anyPass = true;
        }
        if (!any) return true;
        return match == DoorConditionMatch.All || anyPass;
    }
}

/// <summary>开关给门下的命令。</summary>
public enum DoorCommand
{
    [InspectorName("切换")] Toggle = 0,
    [InspectorName("打开")] Open = 1,
    [InspectorName("关闭")] Close = 2,
    [InspectorName("打开一段时间后自动关")] OpenForSeconds = 3,
    [InspectorName("永久打开")] OpenPermanently = 4,
}

/// <summary>
/// 滑动门/升降闸门/双开门（白模阶段）。挂在门预制体的根节点上，门板是 <see cref="leaf"/>（双开再加 <see cref="leafB"/>）。
///
/// 触发方式 <see cref="triggerMode"/>：靠近自动 / 玩家交互(E) / 外部控制 / 远处开关。
/// 关闭方式 <see cref="closeMode"/>：离开后关 / 定时关 / 永不关 / 再交互才关。
/// 两项组合出所有用法，例如"交互 + 定时关"=按一下开 N 秒，"交互 + 永不关"=永久打开（主城里会存档）。
///
/// 滑动轴 <see cref="slideAxisLocal"/>：(1,0,0) 左右滑，(0,1,0) 向上升降。
/// 碰撞：门板上自带 Collider（定义里碰撞模式必须是 CustomRoot），开到 <see cref="colliderOffAfter01"/> 以上就关掉。
/// 声音：<see cref="audioProfile"/>，留空 = 无声。
/// </summary>
[DisallowMultipleComponent]
public sealed class SkyPrisonSlidingDoor : MonoBehaviour, IInteractable
{
    [Header("触发与关闭")]
    [SerializeField] private DoorTriggerMode triggerMode = DoorTriggerMode.Proximity;
    [SerializeField] private DoorCloseMode closeMode = DoorCloseMode.LeaveRadius;
    [Tooltip("定时关：开了多少秒后自己关。")]
    [SerializeField, Min(0.5f)] private float holdSeconds = 5f;
    [Tooltip("靠近自动：玩家进入这个距离（水平，到门中心）就开门。")]
    [SerializeField, Min(0.5f)] private float triggerRadius = 6.5f;
    [Tooltip("玩家离开这个距离才开始计关门倒计时，必须大于触发半径，避免在边缘来回抖动。")]
    [SerializeField, Min(0.5f)] private float closeRadius = 8.5f;
    [SerializeField, Min(0f)] private float closeDelay = 0.8f;

    [Header("交互（触发方式=玩家交互时）")]
    [Tooltip("自定义提示文字，按语言逐个填。全部留空 = 用多语言字典表里的默认文字（door_interact_open / door_interact_close）。")]
    [SerializeField] private DoorLocalizedLabel openLabel = new DoorLocalizedLabel();
    [SerializeField] private DoorLocalizedLabel closeLabel = new DoorLocalizedLabel();

    [Header("锁")]
    [Tooltip("锁住时不会自动开门/交互开门；演出脚本的 Open() 和开关命令仍然有效。")]
    [SerializeField] private bool locked = false;
    [Tooltip("解锁条件（布尔变量 / 数字比较 / 任务标记），可加一条或多条，关系由下面的「条件关系」决定。不加 = 不限制。")]
    [SerializeField] private System.Collections.Generic.List<DoorCondition> unlockConditions = new System.Collections.Generic.List<DoorCondition>();
    [Tooltip("多条条件之间的关系：全部满足（与）/ 任一满足（或）。")]
    [SerializeField] private DoorConditionMatch conditionMatch = DoorConditionMatch.All;
    [Tooltip("留空 = 多语言表默认文字（door_interact_locked）。")]
    [SerializeField] private DoorLocalizedLabel lockedLabel = new DoorLocalizedLabel();

    // 门板引用是预制体内部接线（单开留空，运行时自动找 Leaf；双开两扇永远对称联动），不在属性面板里露出来。
    [SerializeField, HideInInspector] private Transform leaf;
    [SerializeField, HideInInspector] private Collider leafCollider;
    [SerializeField, HideInInspector] private Transform leafB;
    [SerializeField, HideInInspector] private Collider leafBCollider;

    [Header("门板运动")]
    [Tooltip("滑动方向：+1 往滑动轴正方向滑，-1 反向。双开门的第二扇自动反向，不用单独设。")]
    [SerializeField] private float slideSign = 1f;
    [Tooltip("滑动轴（门的本地方向）：(1,0,0)=左右滑，(0,1,0)=向上升降。升降时开门距离按门板高度算。")]
    [SerializeField] private Vector3 slideAxisLocal = Vector3.right;
    [Tooltip("开门距离（米）。0 = 自动按门板宽度 + 0.1 米。")]
    [SerializeField, Min(0f)] private float openDistanceOverride = 0f;
    [SerializeField, Min(0.1f)] private float slideSpeed = 10f;
    [Tooltip("门板开到这个比例以上就关掉碰撞，让人提前穿过（0.2 = 开了 20% 就不挡了）。升降闸门建议 0.5 以上。")]
    [SerializeField, Range(0.05f, 0.9f)] private float colliderOffAfter01 = 0.2f;

    [Header("声音")]
    [SerializeField] private DoorAudioProfile audioProfile;

    [Header("永久状态")]
    [Tooltip("永久打开后写进存档（只在主城地图生效，局内每局重置）。留空则按 场景名+物体名 自动生成。")]
    [SerializeField] private string persistId = "";

    [Header("触发包（演出用）")]
    [Tooltip("门开到位的那一刻执行这个触发包。留空 = 什么也不做。")]
    [SerializeField, TriggerPackageNamePicker] private TriggerPackage onOpenedTrigger;
    [Tooltip("门关到位的那一刻执行这个触发包。")]
    [SerializeField, TriggerPackageNamePicker] private TriggerPackage onClosedTrigger;

    [Header("初始状态")]
    [SerializeField] private bool startOpen = false;

    // ── 运行时状态 ─────────────────────────────────────────────
    private Vector3 _closedLocalPos;
    private Vector3 _closedLocalPosB;
    private float _open01;          // 0 = 完全关闭，1 = 完全打开
    private bool _wantOpen;
    private bool _permanent;
    private float _leaveTimer;
    private float _timedCloseAt = -1f;
    private bool _initialized;
    private bool _wasMoving;
    private bool _registeredInteractable;
    private AudioSource _oneShotSource;
    private AudioSource _loopSource;

    public bool IsOpen => _open01 > 0.5f;
    public bool IsLocked => locked || QuestLockActive;
    public float Open01 => _open01;
    public DoorTriggerMode TriggerMode => triggerMode;
    public DoorCloseMode CloseMode => closeMode;
    public bool IsPermanentlyOpen => _permanent;

    private bool QuestLockActive => !DoorCondition.AllMet(unlockConditions, conditionMatch);

    // ── 公开 API ───────────────────────────────────────────────
    public void Open() { _wantOpen = true; _leaveTimer = 0f; _timedCloseAt = -1f; }
    public void Close() { if (!_permanent) { _wantOpen = false; _timedCloseAt = -1f; } }
    public void Toggle() { if (_wantOpen) Close(); else Open(); }
    public void OpenForSeconds(float seconds) { _wantOpen = true; _leaveTimer = 0f; _timedCloseAt = Time.time + Mathf.Max(0.1f, seconds); }
    public void OpenPermanently(bool save = true)
    {
        _wantOpen = true; _permanent = true; _timedCloseAt = -1f;
        if (save) SavePermanentState();
    }
    public void SetLocked(bool value) { locked = value; }
    public void SetTriggerMode(DoorTriggerMode mode) { triggerMode = mode; RefreshInteractableRegistration(); }
    public void SetAutoOpen(bool value) { triggerMode = value ? DoorTriggerMode.Proximity : DoorTriggerMode.External; RefreshInteractableRegistration(); }

    /// <summary>开关发来的命令。switchUnlocks = 开关自带"解锁"效果。</summary>
    public void ApplyCommand(DoorCommand command, float seconds, bool unlockDoor)
    {
        if (unlockDoor) locked = false;
        switch (command)
        {
            case DoorCommand.Toggle: Toggle(); break;
            case DoorCommand.Open: Open(); break;
            case DoorCommand.Close: Close(); break;
            case DoorCommand.OpenForSeconds: OpenForSeconds(seconds > 0f ? seconds : holdSeconds); break;
            case DoorCommand.OpenPermanently: OpenPermanently(true); break;
        }
    }

    // ── IInteractable ──────────────────────────────────────────
    public Vector3 InteractPosition
    {
        get
        {
            // 门很宽（6~10 米），提示点取门线上离玩家最近的一点，免得站在门边却"离门中心太远"。
            var player = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
            if (player == null || leaf == null) return transform.position;
            Vector3 axis = transform.TransformDirection(slideAxisLocal.sqrMagnitude > 1e-6f ? slideAxisLocal.normalized : Vector3.right);
            if (Mathf.Abs(slideAxisLocal.y) > Mathf.Abs(slideAxisLocal.x)) axis = transform.TransformDirection(Vector3.right);   // 升降门：沿门宽方向取点
            float half = DoorHalfWidth();
            Vector3 p = player.transform.position - transform.position;
            float t = Mathf.Clamp(Vector3.Dot(p, axis), -half, half);
            Vector3 q = transform.position + axis * t;
            q.y = player.transform.position.y;
            return q;
        }
    }

    public string InteractLabel
    {
        get
        {
            if (IsLocked) return lockedLabel.Resolve("door_interact_locked", "无法开启");
            if (closeMode == DoorCloseMode.ToggleByInteract && _wantOpen)
                return closeLabel.Resolve("door_interact_close", "关门");
            return openLabel.Resolve("door_interact_open", "开门");
        }
    }

    public bool CanInteract
    {
        get
        {
            if (triggerMode != DoorTriggerMode.Interact || _permanent) return false;
            if (_wantOpen && closeMode != DoorCloseMode.ToggleByInteract) return false;   // 已经开着，且不是开关型：没必要再交互
            return true;
        }
    }

    public void Interact()
    {
        if (triggerMode != DoorTriggerMode.Interact) return;
        if (IsLocked) { PlayOne(audioProfile != null ? audioProfile.denied : null); return; }
        PlayOne(audioProfile != null ? audioProfile.interact : null);

        switch (closeMode)
        {
            case DoorCloseMode.Never: OpenPermanently(true); break;
            case DoorCloseMode.Timed: OpenForSeconds(holdSeconds); break;
            case DoorCloseMode.ToggleByInteract: Toggle(); break;
            default: Open(); break;   // LeaveRadius：开，玩家离开后关
        }
    }

    private void RefreshInteractableRegistration()
    {
        bool want = isActiveAndEnabled && triggerMode == DoorTriggerMode.Interact;
        if (want == _registeredInteractable) return;
        _registeredInteractable = want;
        if (want) InteractableRegistry.Register(this); else InteractableRegistry.Unregister(this);
    }

    // ── 生命周期 ───────────────────────────────────────────────
    private void Awake()
    {
        Init();
        _wantOpen = startOpen;
        _open01 = startOpen ? 1f : 0f;
        RestorePermanentState();
        Apply();
    }

    private void OnEnable() { RefreshInteractableRegistration(); }

    private void OnDisable()
    {
        if (_registeredInteractable) { InteractableRegistry.Unregister(this); _registeredInteractable = false; }
        StopLoop();
    }

    private void Init()
    {
        if (_initialized) return;
        if (leaf == null)
            leaf = transform.Find("Leaf") ?? (transform.childCount > 0 ? transform.GetChild(0) : null);
        if (leaf == null) return;
        if (leafCollider == null)
            leafCollider = leaf.GetComponentInChildren<Collider>(true);
        _closedLocalPos = leaf.localPosition;
        if (leafB != null)
        {
            if (leafBCollider == null)
                leafBCollider = leafB.GetComponentInChildren<Collider>(true);
            _closedLocalPosB = leafB.localPosition;
        }
        _initialized = true;
    }

    private void Update()
    {
        if (!_initialized) { Init(); if (!_initialized) return; }

        TickTriggerLogic(Time.deltaTime);

        float target = _wantOpen ? 1f : 0f;
        bool moving = !Mathf.Approximately(_open01, target);
        if (moving)
        {
            if (!_wasMoving)
            {
                _wasMoving = true;
                PlayOne(target > 0.5f ? (audioProfile != null ? audioProfile.open : null) : (audioProfile != null ? audioProfile.close : null));
                StartLoop();
            }
            float dist = Mathf.Max(0.01f, ResolveOpenDistance());
            float before = _open01;
            _open01 = Mathf.MoveTowards(_open01, target, slideSpeed * Time.deltaTime / dist);
            Apply();
            if (Mathf.Approximately(_open01, target))
                OnReachedEnd(target > 0.5f);
        }
        else if (_wasMoving)
        {
            OnReachedEnd(_open01 > 0.5f);
        }
    }

    private void OnReachedEnd(bool opened)
    {
        _wasMoving = false;
        StopLoop();
        PlayOne(audioProfile != null ? audioProfile.stop : null);
        var pkg = opened ? onOpenedTrigger : onClosedTrigger;
        if (pkg != null && isActiveAndEnabled)
            StartCoroutine(TriggerPackageRuntime.ExecuteImmediatelyCoroutine(pkg));
    }

    private void TickTriggerLogic(float dt)
    {
        // 定时关：只要有倒计时就按倒计时走（交互/开关/演出都可能设）。
        if (_timedCloseAt > 0f && Time.time >= _timedCloseAt && !_permanent)
        {
            _timedCloseAt = -1f;
            _wantOpen = false;
        }

        var playerUnit = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
        if (playerUnit == null) return;
        float d = Distance2D(playerUnit.transform.position);

        if (triggerMode == DoorTriggerMode.Proximity)
        {
            if (!IsLocked && d <= triggerRadius)
            {
                _wantOpen = true;
                _leaveTimer = 0f;
            }
            else if (_wantOpen && !_permanent && closeMode != DoorCloseMode.Never && _timedCloseAt < 0f)
            {
                if (d >= closeRadius)
                {
                    _leaveTimer += dt;
                    if (_leaveTimer >= closeDelay) _wantOpen = false;
                }
                else _leaveTimer = 0f;
            }
            return;
        }

        // 交互门：开了之后，"离开后关"这种关闭方式按距离关；定时/永不/开关型不看距离。
        if (triggerMode == DoorTriggerMode.Interact && closeMode == DoorCloseMode.LeaveRadius && _wantOpen && !_permanent)
        {
            if (d >= closeRadius)
            {
                _leaveTimer += dt;
                if (_leaveTimer >= closeDelay) _wantOpen = false;
            }
            else _leaveTimer = 0f;
        }
        // 外部控制 / 开关控制：只听命令（以及上面的定时），不按距离关。
    }

    // ── 运动 ───────────────────────────────────────────────────
    private float Distance2D(Vector3 p)
    {
        Vector3 c = transform.position;
        return new Vector2(p.x - c.x, p.z - c.z).magnitude;
    }

    private float DoorHalfWidth()
    {
        if (leaf == null) return 1f;
        float w = Mathf.Abs(leaf.lossyScale.x);
        return (leafB != null ? w * 2f : w) * 0.5f;
    }

    private float ResolveOpenDistance()
    {
        if (openDistanceOverride > 0.01f) return openDistanceOverride;
        if (leaf == null) return 1f;
        bool vertical = Mathf.Abs(slideAxisLocal.y) > Mathf.Abs(slideAxisLocal.x);
        return (vertical ? Mathf.Abs(leaf.lossyScale.y) : Mathf.Abs(leaf.lossyScale.x)) + 0.1f;
    }

    private void Apply()
    {
        if (leaf == null) return;
        Vector3 axis = slideAxisLocal.sqrMagnitude > 1e-6f ? slideAxisLocal.normalized : Vector3.right;
        Vector3 worldDir = transform.TransformDirection(axis) * slideSign;
        float dist = ResolveOpenDistance() * _open01;
        Vector3 localOffset = leaf.parent != null
            ? leaf.parent.InverseTransformVector(worldDir * dist)
            : worldDir * dist;
        leaf.localPosition = _closedLocalPos + localOffset;

        if (leafCollider != null)
            leafCollider.enabled = _open01 < colliderOffAfter01;

        if (leafB != null)
        {
            Vector3 localOffsetB = leafB.parent != null
                ? leafB.parent.InverseTransformVector(-worldDir * dist)
                : -worldDir * dist;
            leafB.localPosition = _closedLocalPosB + localOffsetB;
            if (leafBCollider != null)
                leafBCollider.enabled = _open01 < colliderOffAfter01;
        }
    }

    // ── 声音 ───────────────────────────────────────────────────
    private AudioSource EnsureSource(ref AudioSource src, string childName, bool loop)
    {
        if (src != null) return src;
        var go = new GameObject(childName);
        go.transform.SetParent(transform, false);
        src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = loop;
        ConfigureSource(src);
        return src;
    }

    private void ConfigureSource(AudioSource s)
    {
        if (audioProfile == null) return;
        s.spatialBlend = audioProfile.spatial ? 1f : 0f;
        s.minDistance = audioProfile.minDistance;
        s.maxDistance = Mathf.Max(audioProfile.maxDistance, audioProfile.minDistance + 0.1f);
        s.rolloffMode = AudioRolloffMode.Linear;
    }

    private float MasterVolume()
    {
        var gs = SkyPrisonAudioGlobalSettings.Instance;
        float g = gs != null ? gs.masterVolume * gs.seVolume : 1f;
        return g * (audioProfile != null ? audioProfile.masterVolume : 1f);
    }

    private void PlayOne(DoorAudioProfile.Entry[] entries)
    {
        if (audioProfile == null || entries == null || entries.Length == 0) return;
        var e = entries[Random.Range(0, entries.Length)];
        if (e == null || e.clip == null) return;
        var src = EnsureSource(ref _oneShotSource, "DoorAudio_OneShot", false);
        src.pitch = 1f + Random.Range(-e.pitchJitter, e.pitchJitter);
        src.PlayOneShot(e.clip, MasterVolume() * Mathf.Max(0f, e.volume));
    }

    private void StartLoop()
    {
        if (audioProfile == null || audioProfile.moveLoop == null || audioProfile.moveLoop.clip == null) return;
        var src = EnsureSource(ref _loopSource, "DoorAudio_Loop", true);
        src.clip = audioProfile.moveLoop.clip;
        src.volume = MasterVolume() * Mathf.Max(0f, audioProfile.moveLoop.volume);
        src.pitch = 1f;
        if (!src.isPlaying) src.Play();
    }

    private void StopLoop()
    {
        if (_loopSource != null && _loopSource.isPlaying) _loopSource.Stop();
    }

    // ── 永久状态存档（只在主城）─────────────────────────────────
    private string PersistKey()
    {
        string id = string.IsNullOrEmpty(persistId)
            ? gameObject.scene.name + "/" + name + "/" + Mathf.RoundToInt(transform.position.x) + "_" + Mathf.RoundToInt(transform.position.z)
            : persistId;
        return "door_open_" + id;
    }

    private void SavePermanentState()
    {
        if (!SkyPrisonHubCombatGate.IsHubScene || SaveManager.Player == null) return;
        SaveManager.Player.SetQuestFlag(PersistKey());
    }

    private void RestorePermanentState()
    {
        if (!Application.isPlaying || !SkyPrisonHubCombatGate.IsHubScene || SaveManager.Player == null) return;
        if (!SaveManager.Player.HasQuestFlag(PersistKey())) return;
        _permanent = true; _wantOpen = true; _open01 = 1f;
    }

    // ── 编辑器辅助 ─────────────────────────────────────────────
    private void OnValidate()
    {
        if (closeRadius < triggerRadius + 0.5f)
            closeRadius = triggerRadius + 0.5f;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.5f);
        DrawCircle(transform.position + Vector3.up * 0.2f, triggerRadius);
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.35f);
        DrawCircle(transform.position + Vector3.up * 0.2f, closeRadius);
    }

    private static void DrawCircle(Vector3 c, float r)
    {
        Vector3 prev = c + new Vector3(r, 0f, 0f);
        for (int i = 1; i <= 48; i++)
        {
            float a = i * Mathf.PI * 2f / 48f;
            Vector3 p = c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            Gizmos.DrawLine(prev, p);
            prev = p;
        }
    }
}
