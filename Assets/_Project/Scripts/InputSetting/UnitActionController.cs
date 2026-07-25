using UnityEngine;

/// <summary>
/// Formal command-state owner for Sky Prison units.
///
/// Responsibilities:
/// - Own high-level command state: Normal / Jump / Dodge / Attack / HitStun / Dead.
/// - Receive player / AI intentions.
/// - Gate load-consuming actions through UnitLoadRuntime.
/// - Push only authorized movement intent to UnitMovementController.
///
/// It does NOT play Spine animations directly. SpineAnimationDriver_Current reads this state.
/// It does NOT move the unit directly. UnitMovementController executes movement / jump physics.
/// </summary>
[DefaultExecutionOrder(9000)]
public class UnitActionController : MonoBehaviour
{
    public enum UnitActionState
    {
        Normal = 0,
        Jump = 20,
        Dodge = 30,
        Reload = 35,
        Attack = 40,
        HitStun = 80,
        Dead = 100,
    }

    public enum UnitLocomotionMode
    {
        Idle = 0,
        Walk = 10,
        Sprint = 20,
        Sneak = 30,
    }

    public enum JumpPhase
    {
        None = 0,
        Start = 10,
        Air = 20,
        Land = 30,
    }

    public enum AttackRequestKind
    {
        None = 0,
        Light = 1,
        Heavy = 2,
        DodgeThrust = 3,
        Aerial = 4,
        RunThrust = 5,
    }

    /// <summary>硬直/锁定期间攻击键被按下、缓冲下来的请求，锁定一结束准备自动补发时
    /// 触发——不在这里直接播动画，交给订阅方（UnitActionModuleRuntime）重新走一遍
    /// 正常的技能选定流程（TryPlayerRequestLightAttack/HeavyAttack，设置_currentSkill、
    /// 扣LP等），再回调 RequestLightAttack/RequestHeavyAttack 真正进入攻击状态。直接在
    /// 这里调 EnterAttack(kind) 会跳过那一整套选定逻辑，_currentSkill 停留在上一次
    /// 真正成功触发的技能上不会更新——蓄力技能被这样补发时，_currentSkill.isChargeSkill
    /// 还是旧值，charge_hold分支根本不会触发，表现为"连续攻击后蓄力失灵"。</summary>
    public event System.Action<AttackRequestKind> BufferedAttackReady;

    [Header("Version")]
    [SerializeField] private string scriptVersion = "V7 - 2026-05-27 - jump request debug and runtime trace";

    [Header("References")]
    [SerializeField] private UnitMovementController movement;
    [SerializeField] private UnitLoadRuntime loadRuntime;
    [SerializeField] private bool autoFindMovement = true;
    [SerializeField] private bool autoFindLoadRuntime = true;

    [Header("Authority")]
    [SerializeField] private bool forceMovementExternalInputMode = true;
    [SerializeField] private bool disableMovementFallbackActionKeys = true;
    [SerializeField] private bool ownMovementAnimation = true;
    [SerializeField] private bool zeroMoveInputWhenHardLocked = true;

    [Header("Attack Placeholder")]
    [Tooltip("临时攻击锁。正式攻击模组接入后由武器模组提供动作时长 / 取消窗口。")]
    [SerializeField] private float lightAttackLockSeconds = 0.32f;
    [SerializeField] private float heavyAttackLockSeconds = 0.48f;
    [SerializeField] private float dodgeThrustLockSeconds = 0.4f;

    [Header("Dodge Thrust（闪避接突刺）")]
    [Tooltip("闪避播放进度超过整段闪避时长的这个比例之后，才允许按攻击键打断闪避、" +
        "无缝衔接突刺——不是闪避一开始就能接，要播到这个比例之后到闪避结束这段窗口内" +
        "才行。用比例而不是固定秒数，是因为闪避动画本身有播放加速倍率" +
        "（dodgePlaybackSpeedMultiplier），固定秒数会被这个倍率打乱，比例不受影响。" +
        "2026-07-21：这个比例已经改成每个武器模组自己在 WeaponCombatModule." +
        "dodgeThrustOpenAfterFraction 上配置（不同武器/技能手感不一样），这里只在" +
        "拿不到武器模组数据时当兜底默认值用。")]
    [Range(0f, 1f)]
    [SerializeField] private float dodgeThrustOpenAfterFractionFallback = 0.6f;
    [Tooltip("突刺冲多远（沿闪避方向继续向前）。这是一个攻击手段，不是位移技，位移量" +
        "应该很小，主要靠攻击判定，不是靠冲多远。")]
    [SerializeField] private float dodgeThrustDistance = 2f;
    [Tooltip("突刺冲刺过程持续多久（配合上面的距离决定冲刺速度）。")]
    [SerializeField] private float dodgeThrustDurationSeconds = 0.2f;
    /// <summary>给 UnitActionModuleRuntime 播位移特效(travelVfx)时用，决定特效跟随
    /// 播放多久——要跟这次突刺冲刺的实际持续时间一致，飘带才不会提前消失或者拖到
    /// 冲刺结束后还在跟着走。</summary>
    public float DodgeThrustDurationSeconds => dodgeThrustDurationSeconds;

    [Tooltip("攻击取消后撤步的速度倍率——这个后撤步跟正常闪避共用 UnitMovementController." +
        "dodgeBackDistance/dodgeSpeed 那一套距离配置，但那套配置里 dodgeSpeed 是一个下限，" +
        "光调距离压不下去实际位移，必须在这里单独给这一次后撤步的速度再打一个折扣，才能" +
        "比正常闪避短，1=跟正常闪避一样远。")]
    [Range(0.1f, 1f)]
    [SerializeField] private float attackCancelDodgeBackSpeedScale = 0.5f;

    [Header("Aerial Attack（空中攻击/剑气弹幕）")]
    [Tooltip("空中攻击的攻击状态锁定时长——跟其它攻击不一样，这个只是个兜底：正常情况下\n" +
        "动画播完会调 NotifyAttackAnimationComplete 提前解锁，这个值只在动画事件没接好\n" +
        "时当安全网用。")]
    [SerializeField] private float aerialAttackLockSeconds = 0.6f;
    [Tooltip("触发空中攻击那一刻施加给角色的后坐力冲量大小——方向由调用方算好传进来\n" +
        "（跟弹幕发射方向相反）。这个冲量会按 TerrainGroundMotorV5 的击退衰减率\n" +
        "(18 m/s²)衰减，实际位移距离≈冲量²÷(2×18)，数值调大才看得出明显位移。")]
    [SerializeField] private float aerialAttackRecoilImpulse = 8f;
    [Tooltip("触发空中攻击的瞬间，角色先向上“顶”一小段高度再进入悬停——像鸟拍一下翅膀\n" +
        "那种干净利落的上升感，不是缓慢的物理加速。是一次性直接抬高位置，不是速度\n" +
        "冲量（冻结那一刻马上就把速度摁到0了，用速度冲量会被立刻吃掉，感觉不出来）。\n" +
        "0=不额外上升。")]
    [SerializeField] private float aerialAttackLiftHeight = 2f;
    /// <summary>给 UnitActionModuleRuntime 触发悬停时用，决定顶起的高度。</summary>
    public float AerialAttackLiftHeight => aerialAttackLiftHeight;
    [Tooltip("空中攻击悬停期间的重力倍率（相对正常下落重力）——0=完全定住不动(像时间\n" +
        "停止，违和感很重，不建议用)，1=跟正常下落一样快。默认给个较小值，让角色\n" +
        "悬停时仍然带一点自然的缓慢下沉感，不是被按了暂停键，但也不会在演出(hit_end)\n" +
        "结束前就先落地——悬停期间跳过贴地判定，沉得再低也不会被算成落地。")]
    [Range(0f, 1f)]
    [SerializeField] private float aerialAttackHoverGravityScale = 0.15f;
    /// <summary>给 UnitActionModuleRuntime 触发悬停时用，决定悬停期间的重力倍率。</summary>
    public float AerialAttackHoverGravityScale => aerialAttackHoverGravityScale;
    // 每次 RequestJump() 起跳时重置为 false——空中攻击每次跳跃只能触发一次，不能在
    // 空中反复打断连续放。
    private bool usedAerialAttackThisJump = false;

    [Header("Reload（换弹）")]
    [Tooltip("换弹耗时兜底——正常时长由武器数据(ItemEquipmentExtension.reloadDurationSeconds)\n" +
        "通过 RequestReload(durationSeconds) 传入决定，这个值只在传入的时长异常小/为0时\n" +
        "当最低限度用，不是真实换弹时长的来源。")]
    [SerializeField] private float reloadMinLockSeconds = 0.05f;

    [Tooltip("攻击硬直期间点的攻击键不会被直接丢弃——记下来，硬直一结束就自动补发这次" +
        "攻击。不这样做的话，玩家没能精确卡在硬直结束那一瞬间点击，这次输入就白按了，" +
        "连点攻击会感觉像是反复吞键位/按键失灵。这个数值是缓冲的最长有效期（太久之前" +
        "点的就不再补发了，防止隔了很久突然冒出一次奇怪的攻击）。")]
    [SerializeField] private float attackInputBufferSeconds = 0.6f;
    private AttackRequestKind _bufferedAttackKind = AttackRequestKind.None;
    private float _bufferedAttackRequestTime = -999f;

    [Header("Runtime Debug")]
    [SerializeField] private UnitActionState currentState = UnitActionState.Normal;
    [SerializeField] private UnitLocomotionMode currentLocomotion = UnitLocomotionMode.Idle;
    [SerializeField] private JumpPhase currentJumpPhase = JumpPhase.None;
    [SerializeField] private UnitMovementController.DodgeRuntimeState currentDodgeKind = UnitMovementController.DodgeRuntimeState.None;
    [SerializeField] private AttackRequestKind currentAttackKind = AttackRequestKind.None;
    [SerializeField] private Vector2 moveInput = Vector2.zero;
    // 跟 moveInput 不一样：moveInput 在 IsHardLocked(攻击/硬直/死亡)且非蓄力技时会被
    // SubmitMoveIntent 强制清零(给movement用，攻击时不能真的移动)。这个字段是清零之前
    // 的原始值，纯粹给"玩家这一刻实际按的是哪个方向"这类查询用(比如攻击取消闪避判断
    // 前闪/后闪)，不影响移动/锁定逻辑，每帧无条件更新。
    [SerializeField] private Vector2 rawMoveInputUnsuppressed = Vector2.zero;
    [SerializeField] private bool runHeld = false;
    [SerializeField] private bool sneakHeld = false;
    [SerializeField] private float stateLockedUntil = 0f;
    [SerializeField] private string lastActionRequest = "";

    [Header("Jump Debug")]
    [SerializeField] private int jumpRequestCount = 0;
    [SerializeField] private bool lastJumpRequestAcceptedByAction = false;
    [SerializeField] private string lastJumpRejectReason = "";
    [SerializeField] private string lastMovementJumpRuntimeState = "None";
    [SerializeField] private bool lastMovementIsJumping = false;
    [SerializeField] private float lastJumpRequestTime = -999f;

    public string Version => scriptVersion;
    public UnitActionState CurrentState => currentState;
    public UnitLocomotionMode CurrentLocomotion => currentLocomotion;
    public JumpPhase CurrentJumpPhase => currentJumpPhase;
    public UnitMovementController.DodgeRuntimeState CurrentDodgeKind => currentDodgeKind;
    public AttackRequestKind CurrentAttackKind => currentAttackKind;

    public bool IsNormal => currentState == UnitActionState.Normal;
    public bool IsJumping => currentState == UnitActionState.Jump;
    public bool IsDodging => currentState == UnitActionState.Dodge;
    public bool IsReloading => currentState == UnitActionState.Reload;
    public bool IsAttacking => currentState == UnitActionState.Attack;
    public bool IsDead => currentState == UnitActionState.Dead;
    public bool IsHardLocked => currentState == UnitActionState.Attack || currentState == UnitActionState.HitStun || currentState == UnitActionState.Dead;

    public Vector2 MoveInput => moveInput;
    /// <summary>玩家这一刻实际按着的方向，不受攻击/硬直锁定影响——攻击取消闪避判断
    /// 前闪/后闪要用这个，不能用 MoveInput（那个在攻击时恒为(0,0)）。</summary>
    public Vector2 RawMoveInputUnsuppressed => rawMoveInputUnsuppressed;
    public bool RunHeld => runHeld;
    public bool SneakHeld => sneakHeld;

    // 蓄力攻击定格期间的例外——正常Attack状态是完全硬锁（IsHardLocked），不接受任何
    // 移动输入；蓄力定格这段时间用户明确要求"允许走路，但不能跑/潜行，跑/潜行会打断
    // 这次出招"，由 UnitActionModuleRuntime 在动画播到 charge_hold 事件时打开这个例外、
    // 释放/打断蓄力时关闭。只影响"是否接受移动输入"，不影响硬锁本身用在其他地方
    // （比如CanEnterAttack还是照常拒绝新的攻击/闪避请求）。
    private bool chargeMovementAllowed = false;
    public void SetChargeMovementAllowed(bool allowed) => chargeMovementAllowed = allowed;

    /// <summary>由 UnitActionModuleRuntime 在蓄力攻击被跑步/潜行打断时调用——直接取消
    /// 攻击状态，不算完整命中也不进硬直，跟正常攻击动画播完的 NotifyAttackAnimationComplete
    /// 效果一样都是解锁回 Normal，语义上分开命名方便以后看代码不会搞混"正常播完"和
    /// "被打断取消"这两种情况。</summary>
    public void CancelAttack()
    {
        if (currentState != UnitActionState.Attack) return;
        currentState      = UnitActionState.Normal;
        currentAttackKind = AttackRequestKind.None;
        stateLockedUntil  = 0f;
    }

    private void Awake()
    {
        ResolveReferences();
        ApplyAuthorityMode();
    }

    private void OnEnable()
    {
        ResolveReferences();
        ApplyAuthorityMode();
    }

    private void Update()
    {
        ResolveReferences();
        ApplyAuthorityMode();
        RefreshStateFromRuntime();
        TryConsumeBufferedAttack();
        PushMovementIntent();
    }

    public void SubmitMoveIntent(Vector2 input, bool wantsRun, bool wantsSneak)
    {
        // 无条件记录原始按键方向，不受下面 IsHardLocked 清零逻辑影响——纯粹给"玩家
        // 这一刻实际按的是哪个方向"这类查询用(比如攻击取消闪避判断前闪/后闪)。
        rawMoveInputUnsuppressed = input.sqrMagnitude > 1f ? input.normalized : input;

        // HitStun / Attack / Dead 期间不接受移动输入——蓄力定格期间例外，允许走路
        // （wantsRun/wantsSneak强制按false处理，蓄力期间的移动不会被当成"真的在跑/
        // 潜行"去乘速度倍率；蓄力现在优先级高于跑步/潜行，不会被这两个键打断）。
        if (IsHardLocked)
        {
            if (chargeMovementAllowed)
            {
                if (input.sqrMagnitude > 1f)
                    input.Normalize();

                moveInput = input;
                runHeld   = false;
                sneakHeld = false;
                return;
            }

            moveInput = Vector2.zero;
            runHeld   = false;
            sneakHeld = false;
            return;
        }

        if (input.sqrMagnitude > 1f)
            input.Normalize();

        moveInput = input;
        runHeld = wantsRun;
        sneakHeld = wantsSneak;
    }

    public void RequestJump()
    {
        lastActionRequest = "Jump";
        jumpRequestCount++;
        lastJumpRequestTime = Time.time;
        lastJumpRequestAcceptedByAction = false;
        lastJumpRejectReason = "";

        if (!CanRequestLocomotionAction(out string rejectReason))
        {
            lastJumpRejectReason = rejectReason;
            return;
        }

        movement.RequestJump();

        // Mark Jump immediately so animation / debug can react in the same command frame.
        // MovementController remains the physical authority and will confirm phase on following updates.
        currentState = UnitActionState.Jump;
        currentJumpPhase = JumpPhase.Start;
        lastJumpRequestAcceptedByAction = true;
        usedAerialAttackThisJump = false; // 每次起跳重新计次，空中攻击每次跳跃只能用一次

#if UNITY_EDITOR
        lastMovementJumpRuntimeState = movement.CurrentJumpRuntimeState.ToString();
#endif
    }

    public void RequestDodge(bool forward = true)
    {
        lastActionRequest = forward ? "DodgeForward" : "DodgeBack";

        if (!CanRequestLocomotionAction())
            return;

        if (loadRuntime != null && !loadRuntime.TrySpendDodge())
            return;

        currentDodgeKind = forward ? UnitMovementController.DodgeRuntimeState.Forward : UnitMovementController.DodgeRuntimeState.Back;
        movement?.RequestDodge(forward);
        currentState = UnitActionState.Dodge;
        PlayDodgeSFX();
    }

    public void RequestDodge(Vector2 worldXZDirection, UnitMovementController.DodgeRuntimeState dodgeKind)
    {
        lastActionRequest = "DodgeVector";

        if (!CanRequestLocomotionAction())
            return;

        if (loadRuntime != null && !loadRuntime.TrySpendDodge())
            return;

        currentDodgeKind = dodgeKind == UnitMovementController.DodgeRuntimeState.None ? UnitMovementController.DodgeRuntimeState.Forward : dodgeKind;
        movement?.RequestDodge(worldXZDirection, currentDodgeKind);
        currentState = UnitActionState.Dodge;
        PlayDodgeSFX();
    }

    private void PlayDodgeSFX()
    {
        var gs = SkyPrisonAudioGlobalSettings.Instance;
        if (gs == null || gs.dodgeSFXClips == null || gs.dodgeSFXClips.Length == 0) return;
        AudioClip clip = gs.dodgeSFXClips[UnityEngine.Random.Range(0, gs.dodgeSFXClips.Length)];
        if (clip == null) return;
        float vol = Mathf.Max(0f, gs.masterVolume * gs.seVolume * gs.dodgeSFXVolume);
        AudioSource.PlayClipAtPoint(clip, transform.position, vol);
    }

    /// <summary>能否触发换弹：只能从 Normal 状态进入（攻击/闪避/跳跃/硬直/死亡/已经在
    /// 换弹中都不行）——换弹本身是一个独立的锁定状态，跟"闪避接突刺"那种能从其它状态
    /// 打断进入的技能不一样，不需要打断谁，正常情况下也没必要在跳跃/闪避途中开始换弹。</summary>
    public bool CanEnterReloadPublic()
    {
        return currentState == UnitActionState.Normal;
    }

    /// <summary>由 UnitActionModuleRuntime.TryPlayerRequestReload 在确认弹匣未满、背包还有
    /// 备用弹药之后调用——进入Reload状态锁定durationSeconds这么久，期间不能攻击/闪避
    /// （不在IsHardLocked里，移动照常放行，能走动）。真正把弹药从背包搬进弹匣这个数值
    /// 变化不在这里发生——由调用方在Update()里检测"Reload状态是否自然结束回到Normal"
    /// 才执行，这样命中打断(currentState被ForceHitStun/ForceDead直接覆盖成HitStun/Dead，
    /// 不会回到Normal)时，弹药就不会被填进去，符合"换弹被打断要重新按键"的设计。</summary>
    public bool RequestReload(float durationSeconds)
    {
        lastActionRequest = "Reload";
        if (!CanEnterReloadPublic())
            return false;

        currentState = UnitActionState.Reload;
        stateLockedUntil = Time.time + Mathf.Max(reloadMinLockSeconds, durationSeconds);
        return true;
    }

    public void RequestLightAttack() => RequestAttack(AttackRequestKind.Light);
    public void RequestHeavyAttack() => RequestAttack(AttackRequestKind.Heavy);

    /// <summary>能否触发"闪避接突刺"：必须正处于闪避状态，且闪避播放进度（已播时长 ÷
    /// 整段闪避总时长）超过 openAfterFraction——用比例而不是固定秒数，这样不管闪避
    /// 动画播放加速倍率怎么调，窗口位置始终跟在"闪避后半段"这个相对位置上，不会被
    /// 速度倍率压缩甚至挤没。openAfterFraction 由调用方传入当前武器模组自己配置的值
    /// （WeaponCombatModule.dodgeThrustOpenAfterFraction），不传则用兜底默认值。</summary>
    public bool CanEnterDodgeThrustPublic(float? openAfterFraction = null)
    {
        if (currentState != UnitActionState.Dodge) return false;
        if (movement == null) return false;
        float remaining = movement.CurrentActionLockedUntil - Time.time;
        if (remaining <= 0f) return false;

        float elapsed = movement.CurrentDodgeElapsedSeconds;
        float total = elapsed + remaining;
        if (total <= 0f) return false;

        float fraction = openAfterFraction ?? dodgeThrustOpenAfterFractionFallback;
        return elapsed / total >= Mathf.Clamp01(fraction);
    }

    /// <summary>由 UnitActionModuleRuntime 在玩家请求闪避接突刺、技能选定/LP扣费都通过
    /// 之后调用——立即打断闪避，衔接一段突刺冲刺（复用充能攻击释放时用的
    /// StartChargeDash，同一套冲刺位移逻辑）。openAfterFraction 透传给
    /// CanEnterDodgeThrustPublic，跟 TryPlayerRequestDodgeThrust 那次判断保持一致。</summary>
    public bool RequestDodgeThrust(float? openAfterFraction = null)
    {
        lastActionRequest = "DodgeThrust";
        if (!CanEnterDodgeThrustPublic(openAfterFraction))
            return false;

        // 方向要在CancelDodge之前先取出来——CancelDodge会把它清零。
        Vector2 dodgeDirection = movement.CurrentDodgeDirection;

        // 2026-07-21：突刺动画(Attack_Sword_3)是朝着角色当前朝向往前刺的，不能无脑
        // 沿用闪避的物理位移方向——闪避本身分"前闪"(沿朝向位移)和"后闪"(按跟朝向
        // 相反的方向键，人往反方向位移但保持原朝向不转身，见 SkyPrisonPlayerInputRouter
        // 里的 dot 判定)。如果恰好是在做后闪时接技能，闪避的位移方向是背对朝向的，
        // 直接沿用会让突刺动画朝向前刺、身体却继续往后冲，看起来像"反方向突刺"。
        // 后闪的物理方向本来就是朝向的反方向，取一次负数还原回朝向方向即可，不需要
        // 额外读朝向数据源。
        Vector2 thrustDirection = movement.CurrentDodgeRuntimeState == UnitMovementController.DodgeRuntimeState.Back
            ? -dodgeDirection
            : dodgeDirection;

        movement.CancelDodge();
        EnterAttack(AttackRequestKind.DodgeThrust);
        movement.StartChargeDash(thrustDirection, dodgeThrustDistance, dodgeThrustDurationSeconds);
        return true;
    }

    /// <summary>能否触发"奔跑接突刺"：必须正处于Normal状态（不是攻击/闪避/跳跃/硬直/
    /// 死亡中），且当前实际正在奔跑(Sprint locomotion，不是单纯按住奔跑键站着不动)。</summary>
    public bool CanEnterRunThrustPublic()
    {
        if (currentState != UnitActionState.Normal) return false;
        if (currentLocomotion != UnitLocomotionMode.Sprint) return false;
        if (movement == null) return false;
        return true;
    }

    /// <summary>由 UnitActionModuleRuntime 在玩家请求奔跑接突刺、技能选定/LP扣费都通过
    /// 之后调用——复用闪避接突刺同一个技能(WeaponCombatModule.dodgeThrustAttack)和同一套
    /// StartChargeDash冲刺位移，只是方向源换成当前奔跑的实际输入方向（不是闪避方向，
    /// 奔跑时也没有闪避方向可用）。</summary>
    public bool RequestRunThrust()
    {
        lastActionRequest = "RunThrust";
        if (!CanEnterRunThrustPublic())
            return false;

        Vector2 runDirection = movement.MoveInput.sqrMagnitude > 0.0001f ? movement.MoveInput.normalized : Vector2.zero;
        if (runDirection.sqrMagnitude <= 0.0001f)
            return false;

        EnterAttack(AttackRequestKind.RunThrust);
        movement.StartChargeDash(runDirection, dodgeThrustDistance, dodgeThrustDurationSeconds);
        return true;
    }

    /// <summary>由 UnitActionModuleRuntime.TryPlayerRequestAttackCancelDodgeBack 在判定帧
    /// 结束的后摇阶段、且当前武器模组允许(allowAttackCancelDodgeBack)时调用——立即打断
    /// 攻击，衔接一段闪避。kind==Back 时固定沿角色当前朝向的正后方（不看输入方向），
    /// 播放 dodge_back，速度按 attackCancelDodgeBackSpeedScale 打折(小步后退)；
    /// kind==Forward 时沿玩家当前按的方向键前闪，播放 dodge_front，用普通闪避的完整
    /// 距离/速度(不打折——前闪是真正的闪避动作，不是"退一小步"那种收尾反馈)。
    /// Back 情况下朝向保持不转身是调用方(UnitActionModuleRuntime)通过
    /// animationDriver.SetFacingHold(true) 冻结朝向更新实现的，这里不用管。</summary>
    public bool RequestAttackCancelDodgeBack(Vector2 direction, UnitMovementController.DodgeRuntimeState kind)
    {
        lastActionRequest = "AttackCancelDodgeBack";
        if (currentState != UnitActionState.Attack) return false;
        if (movement == null) return false;

        // 2026-07-21：这里之前漏了扣负重(TP)——攻击取消闪避跟普通闪避
        // (RequestDodge/RequestDodge(Vector2,...))是同一类位移动作，理应共用同一套
        // 消耗检查，不能因为走的是专属入口就绕过资源消耗，变成不花TP的白嫖闪避。
        if (loadRuntime != null && !loadRuntime.TrySpendDodge())
            return false;

        currentAttackKind = AttackRequestKind.None;
        stateLockedUntil  = 0f;

        currentDodgeKind = kind;
        float speedScale = kind == UnitMovementController.DodgeRuntimeState.Back ? attackCancelDodgeBackSpeedScale : 1f;
        movement.RequestDodge(direction, kind, speedScale);
        currentState = UnitActionState.Dodge;
        PlayDodgeSFX();
        return true;
    }

    /// <summary>能否触发"空中攻击"：必须正处于跳跃的空中阶段(Air，不是起跳瞬间Start或
    /// 落地Land)，且这次跳跃还没用过——每次跳跃只能触发一次，不能在空中反复打断连续
    /// 放。这个跟 CanEnterAttack() 的一般攻击门槛不一样——一般攻击明确拒绝Jump/Dodge
    /// 状态，空中攻击就是要打破这条限制，所以走专属的入口，不复用 CanEnterAttack()。</summary>
    public bool CanEnterAerialAttackPublic()
    {
        if (currentState == UnitActionState.Dead || currentState == UnitActionState.HitStun || currentState == UnitActionState.Attack)
            return false;
        if (movement == null) return false;
        if (!movement.IsJumping) return false;
        if (movement.CurrentJumpRuntimeState != UnitMovementController.JumpRuntimeState.Air) return false;
        if (usedAerialAttackThisJump) return false;
        return true;
    }

    /// <summary>由 UnitActionModuleRuntime.TryPlayerRequestAerialAttack 在技能选定/LP扣费
    /// 都通过之后调用——进入Attack状态播放空中攻击动画。不打断 movement 自己的跳跃
    /// 物理状态机——跳跃的重力/下落/落地判定完全独立继续跑，动画层面靠
    /// SpineAnimationDriver_Current 那边对"Attack状态+Aerial"的特判，才能在
    /// movement.IsJumping依然为true的情况下，让这个技能的攻击动画正常显示出来
    /// （细节见 SpineAnimationDriver_Current.ResolveCurrentAnimation 的开头那段判断）。
    /// 后坐力不在这里施加——设计上要跟弹幕真正发射(hit_start)同一刻发生，不是按键
    /// 那一刻，见 ApplyAerialAttackRecoil()。</summary>
    public bool RequestAerialAttack()
    {
        lastActionRequest = "AerialAttack";
        if (!CanEnterAerialAttackPublic())
            return false;

        usedAerialAttackThisJump = true;
        EnterAttack(AttackRequestKind.Aerial);
        return true;
    }

    /// <summary>由 UnitActionModuleRuntime 在 hit_start（弹幕真正发射那一刻）调用，给角色
    /// 一个小顿挫后坐力——方向由调用方按弹幕发射方向的反方向算好传进来。故意跟
    /// RequestAerialAttack() 分开，因为后坐力要跟弹幕发射同一刻发生，而不是按键那一刻
    /// （按键到hit_start之间还有一段前摇）。</summary>
    public void ApplyAerialAttackRecoil(Vector2 recoilDirection)
    {
        if (movement == null) return;
        if (recoilDirection.sqrMagnitude <= 0.0001f) return;
        movement.ApplyKnockback(new Vector3(recoilDirection.x, 0f, recoilDirection.y) * aerialAttackRecoilImpulse);
    }

    /// <summary>由 SpineAnimationDriver 在攻击动画 Complete 事件里调用，立即解锁攻击状态。</summary>
    public void NotifyAttackAnimationComplete()
    {
        if (currentState != UnitActionState.Attack) return;
        currentState      = UnitActionState.Normal;
        currentAttackKind = AttackRequestKind.None;
        stateLockedUntil  = 0f;
    }

    public void RequestAttack(AttackRequestKind kind)
    {
#if UNITY_EDITOR
        lastActionRequest = kind.ToString();
#endif

        if (kind == AttackRequestKind.None)
            return;

        if (!CanEnterAttack())
        {
            // 硬直期间点的攻击键不能直接丢掉——记下来，硬直一结束（Update里
            // TryConsumeBufferedAttack）就自动补发，不然玩家没卡准硬直结束那一瞬间
            // 点击，这次输入就白按了，连点攻击会感觉像是反复吞键位。
            _bufferedAttackKind = kind;
            _bufferedAttackRequestTime = Time.time;
            return;
        }

        EnterAttack(kind);
    }

    private void EnterAttack(AttackRequestKind kind)
    {
        currentState = UnitActionState.Attack;
        currentAttackKind = kind;
        // 锁到动画播完为止，由 SpineAnimationDriver 的 TrackEntry.Complete 事件调用 NotifyAttackAnimationComplete 解锁。
        // lightAttackLockSeconds/heavyAttackLockSeconds 这两个配置值不能当真实动画时长
        // 来用——不同武器/攻击动作时长本来就不统一，写死成这两个固定数字，要么比真实
        // 动画短（正常攻击被这个兜底提前打断），要么比真实动画长（连点时卡在兜底里
        // 出不来）。这两个数字只应该是"Complete事件万一没触发时的极端安全网"，正常
        // 情况下动画播完必须靠 NotifyAttackAnimationComplete()（Complete事件）来解锁，
        // 不能靠猜一个数字。兜底本身给得足够宽松，不去卡"正常攻击的长度"。
        float fallback = kind == AttackRequestKind.Heavy ? heavyAttackLockSeconds
                        : kind == AttackRequestKind.DodgeThrust ? dodgeThrustLockSeconds
                        : kind == AttackRequestKind.RunThrust ? dodgeThrustLockSeconds
                        : kind == AttackRequestKind.Aerial ? aerialAttackLockSeconds
                        : lightAttackLockSeconds;
        stateLockedUntil = Time.time + Mathf.Max(1.5f, fallback);
    }

    /// <summary>蓄力(charge_hold)期间动画被人为冻结(TimeScale=0)，Complete事件永远不会
    /// 触发——上面EnterAttack()挂的兜底超时("Complete万一没触发就强制解锁"的安全网，
    /// 最短1.5秒)会把蓄满很久还按着不放的攻击误伤强制取消。蓄力开始时调用这个方法
    /// 解除兜底超时，配合ReleaseChargeAttack()调用RearmAttackLockFallback重新挂一个
    /// 正常的兜底，保护释放/突刺阶段依然有安全网。</summary>
    public void SuspendAttackLockFallback()
    {
        if (currentState != UnitActionState.Attack) return;
        stateLockedUntil = float.PositiveInfinity;
    }

    public void RearmAttackLockFallback()
    {
        if (currentState != UnitActionState.Attack) return;
        float fallback = currentAttackKind == AttackRequestKind.Heavy ? heavyAttackLockSeconds : lightAttackLockSeconds;
        stateLockedUntil = Time.time + Mathf.Max(1.5f, fallback);
    }

    // 硬直一结束（NotifyAttackAnimationComplete 解锁，或兜底超时解锁）就检查有没有
    // 攒着的攻击输入，有的话立刻补发，不需要玩家再点一次。
    private void TryConsumeBufferedAttack()
    {
        if (_bufferedAttackKind == AttackRequestKind.None)
            return;

        if (Time.time - _bufferedAttackRequestTime > attackInputBufferSeconds)
        {
            _bufferedAttackKind = AttackRequestKind.None; // 缓冲的这次输入放太久了，不再补发
            return;
        }

        if (!CanEnterAttack())
            return;

        AttackRequestKind kind = _bufferedAttackKind;
        _bufferedAttackKind = AttackRequestKind.None;

        // 交给订阅方（UnitActionModuleRuntime）重新走一遍技能选定流程，不直接播动画，
        // 见 BufferedAttackReady 上的注释。没有订阅方时才退回旧的直接进入攻击的兜底，
        // 保证这个组件脱离 UnitActionModuleRuntime 单独测试时依然能用。
        if (BufferedAttackReady != null)
            BufferedAttackReady.Invoke(kind);
        else
            EnterAttack(kind);
    }

    public void ForceHitStun(float seconds)
    {
        currentState = UnitActionState.HitStun;
        currentJumpPhase = JumpPhase.None;
        currentDodgeKind = UnitMovementController.DodgeRuntimeState.None;
        currentAttackKind = AttackRequestKind.None;
        stateLockedUntil = Time.time + Mathf.Max(0.01f, seconds);
        lastActionRequest = "HitStun";
        // 被打中打断攻击不该"记仇"——硬直是玩家攻击途中挨的这一下，不是玩家自己
        // 手速跟不上，硬直结束后不应该替玩家自动补发一次攻击。
        _bufferedAttackKind = AttackRequestKind.None;
        movement?.CancelActionState();
        movement?.ImmediateStop(); // 清零 motor 水平速度，防止受击时脚底继续滑动
    }

    public void ForceDead()
    {
        currentState = UnitActionState.Dead;
        currentLocomotion = UnitLocomotionMode.Idle;
        currentJumpPhase = JumpPhase.None;
        currentDodgeKind = UnitMovementController.DodgeRuntimeState.None;
        currentAttackKind = AttackRequestKind.None;
        stateLockedUntil = float.PositiveInfinity;
        moveInput = Vector2.zero;
        runHeld = false;
        sneakHeld = false;
        lastActionRequest = "Dead";
        _bufferedAttackKind = AttackRequestKind.None;
        movement?.CancelActionState();
        movement?.ClearMoveInput();
    }

    public void ForceAlive()
    {
        if (currentState != UnitActionState.Dead) return;
        currentState = UnitActionState.Normal;
        stateLockedUntil = 0f;
        lastActionRequest = "Revived";
    }

    public void ClearForcedState()
    {
        if (currentState == UnitActionState.Dead)
            return;

        currentState = UnitActionState.Normal;
        currentJumpPhase = JumpPhase.None;
        currentDodgeKind = UnitMovementController.DodgeRuntimeState.None;
        currentAttackKind = AttackRequestKind.None;
        stateLockedUntil = 0f;
        lastActionRequest = "ClearForcedState";
    }

    private bool CanRequestLocomotionAction()
    {
        return CanRequestLocomotionAction(out _);
    }

    private bool CanRequestLocomotionAction(out string rejectReason)
    {
        rejectReason = "";

        if (movement == null)
        {
            rejectReason = "MovementController is null";
            return false;
        }

        if (currentState == UnitActionState.Dead)
        {
            rejectReason = "Dead";
            return false;
        }

        if (currentState == UnitActionState.HitStun)
        {
            rejectReason = "HitStun";
            return false;
        }

        if (currentState == UnitActionState.Attack)
        {
            rejectReason = "Attack";
            return false;
        }

        // 2026-07-21：跳跃空中阶段不该能再触发闪避——UnitMovementController.TryStartDodge
        // 内部虽然会因为 jumpState!=None 而拒绝真正开始闪避，但那是"排队"之后才检查的，
        // 这一层如果不提前拦住，RequestDodge 还是会先把闪避音效播出去、TP/LP 也先扣了，
        // 实际闪避却因为内部检查失败根本没发生——玩家听到闪避声音、看到资源被扣，人却
        // 没有任何反应，就是这个"外层乐观、内层才真正拒绝"的时序空子。RequestJump 也
        // 走这个共用入口，跳跃中再按跳跃键同样应该被这里拦住（这个项目没有二段跳）。
        if (currentState == UnitActionState.Jump)
        {
            rejectReason = "Jump";
            return false;
        }

        // 换弹期间不能跳跃/闪避（可以走动，走动不走这个入口）——换弹是跟Attack/Dodge
        // 同级别的锁定状态，见 RequestReload 注释。
        if (currentState == UnitActionState.Reload)
        {
            rejectReason = "Reload";
            return false;
        }

        return true;
    }

    public bool CanEnterAttackPublic() => CanEnterAttack();

    private bool CanEnterAttack()
    {
        if (currentState == UnitActionState.Dead || currentState == UnitActionState.HitStun || currentState == UnitActionState.Attack)
            return false;
        if (currentState == UnitActionState.Jump || currentState == UnitActionState.Dodge || currentState == UnitActionState.Reload)
            return false;
        return true;
    }

    private void PushMovementIntent()
    {
        if (movement == null)
            return;

        Vector2 finalMove = moveInput;
        bool finalSneak = sneakHeld;
        bool finalRun = runHeld;

        // 蓄力定格期间例外：runHeld/sneakHeld在SubmitMoveIntent那边已经被强制按false
        // 存了，这里不用再单独处理，只需要不要把移动方向本身也清零。
        if (zeroMoveInputWhenHardLocked && IsHardLocked && !chargeMovementAllowed)
        {
            finalMove = Vector2.zero;
            finalRun = false;
            finalSneak = false;
        }

        if (finalSneak)
            finalRun = false;

        bool hasMove = finalMove.sqrMagnitude > 0.0001f;
        if (!hasMove)
        {
            finalRun = false;
            finalSneak = false;
        }

        if (finalRun)
        {
            if (loadRuntime != null)
            {
                if (!loadRuntime.CanSprint || !loadRuntime.TrySpendSprint(Time.deltaTime))
                    finalRun = false;
            }

            // 之前这里只看耐力(loadRuntime.CanSprint)，完全不知道超重——超重时
            // UnitMovementController 内部的 Burden 系统已经把 wantsRun 压成 false、
            // 真实移动速度也确实变慢了，但这里的 finalRun 没跟着变，导致
            // currentLocomotion 还是被判成 Sprint，Spine 播的还是奔跑动画/姿势，
            // 跟"人物明明超重、按下奔跑键角色姿势还是在跑"这个反馈对得上。
            if (finalRun && movement != null && !movement.CanRun)
                finalRun = false;
        }

        currentLocomotion = ResolveLocomotion(finalMove, finalRun, finalSneak);
        movement.SetExternalMoveInput(finalMove, finalRun, finalSneak);
    }

    private UnitLocomotionMode ResolveLocomotion(Vector2 input, bool finalRun, bool finalSneak)
    {
        // chargeMovementAllowed 例外：蓄力定格期间 currentState 还是 Attack，但用户明确
        // 要求蓄力时允许走路——如果这里还是无条件返回旧值，脚停下来之后 currentLocomotion
        // 会一直卡在停下前的最后一个值(比如Walk)，SpineAnimationDriver那边一看
        // CurrentLocomotion != Idle 就直接采信，不会再去看真实速度是不是已经变成0，
        // 表现就是"脚已经停了，走路动画还在播"。
        // Reload例外：换弹允许走动，locomotion不该跟Attack/Dodge那些真正硬锁的状态一样
        // 冻结在进入换弹那一刻的值，否则玩家换弹途中开始移动，脚步动画/奔跑判定会卡住
        // 不更新。
        if (currentState != UnitActionState.Normal && currentState != UnitActionState.Reload && !chargeMovementAllowed)
            return currentLocomotion;

        if (input.sqrMagnitude <= 0.0001f)
            return UnitLocomotionMode.Idle;
        if (finalSneak)
            return UnitLocomotionMode.Sneak;
        if (finalRun)
            return UnitLocomotionMode.Sprint;
        return UnitLocomotionMode.Walk;
    }

    private void RefreshStateFromRuntime()
    {
        if (currentState == UnitActionState.Dead)
            return;

        if (currentState == UnitActionState.HitStun || currentState == UnitActionState.Attack || currentState == UnitActionState.Reload)
        {
            if (Time.time < stateLockedUntil)
                return;

            currentState = UnitActionState.Normal;
            currentAttackKind = AttackRequestKind.None;
            stateLockedUntil = 0f;
        }

        if (movement != null)
        {
            lastMovementIsJumping = movement.IsJumping;
#if UNITY_EDITOR
            lastMovementJumpRuntimeState = movement.CurrentJumpRuntimeState.ToString();
#endif

            if (movement.IsDodging)
            {
                currentState = UnitActionState.Dodge;
                currentDodgeKind = movement.CurrentDodgeRuntimeState;
                return;
            }

            if (movement.IsJumping)
            {
                currentState = UnitActionState.Jump;
                switch (movement.CurrentJumpRuntimeState)
                {
                    case UnitMovementController.JumpRuntimeState.Start:
                        currentJumpPhase = JumpPhase.Start;
                        break;
                    case UnitMovementController.JumpRuntimeState.Air:
                        currentJumpPhase = JumpPhase.Air;
                        break;
                    case UnitMovementController.JumpRuntimeState.Land:
                        currentJumpPhase = JumpPhase.Land;
                        break;
                    default:
                        currentJumpPhase = JumpPhase.Start;
                        break;
                }
                return;
            }
        }

        if (currentState == UnitActionState.Jump || currentState == UnitActionState.Dodge)
        {
            currentState = UnitActionState.Normal;
            currentJumpPhase = JumpPhase.None;
            currentDodgeKind = UnitMovementController.DodgeRuntimeState.None;
        }

        if (currentState == UnitActionState.Normal)
            currentJumpPhase = JumpPhase.None;
    }

    private void ResolveReferences()
    {
        if (autoFindMovement && movement == null)
            movement = GetComponent<UnitMovementController>() ?? GetComponentInParent<UnitMovementController>() ?? GetComponentInChildren<UnitMovementController>(true);

        if (autoFindLoadRuntime && loadRuntime == null)
            loadRuntime = GetComponent<UnitLoadRuntime>() ?? GetComponentInParent<UnitLoadRuntime>() ?? GetComponentInChildren<UnitLoadRuntime>(true);
    }

    private void ApplyAuthorityMode()
    {
        if (movement == null)
            return;

        if (forceMovementExternalInputMode)
            movement.SetInputMode(UnitMovementController.MovementInputMode.External);

        if (disableMovementFallbackActionKeys)
            movement.SetPlayerFallbackActionKeysEnabled(false);

        if (ownMovementAnimation)
            movement.SetActionControllerOwnsAnimation(true);

        if (loadRuntime != null)
            movement.AssignLoadRuntime(loadRuntime);
    }
}

// Compatibility shim for older scenes / scripts that still reference UnitActionController_V1.
// New code should reference UnitActionController.
public class UnitActionController_V1 : UnitActionController { }
