using System.Collections.Generic;
using Effekseer;
using Spine;
using Spine.Unity;
using UnityEngine;

/// <summary>
/// 运行时战斗模组控制器。
///
/// 职责：
/// 1. 根据当前装备武器的 weaponModuleKey 加载 WeaponCombatModule。
/// 2. 响应攻击请求，从连段列表取出 SkillDefinition。
/// 3. 监听 Spine Event（hit_start / hit_end / jump_stay），激活/关闭 UnitCombatHitbox，
///    以及空中攻击悬停的开始/结束时机。
/// 4. 监听 Hitbox.OnHit，计算并施加伤害。
/// </summary>
[DisallowMultipleComponent]
public class UnitActionModuleRuntime : MonoBehaviour
{
    [Header("引用")]
    [SerializeField] private UnitActionController actionController;
    [SerializeField] private UnitMovementController movement;
    [SerializeField] private SpineAnimationDriver_Current animationDriver;
    [SerializeField] private SkeletonAnimation    skeletonAnimation;
    [SerializeField] private UnitCombatHitbox     hitbox;
    [Tooltip("Hitbox 自动创建时的父节点，留空则挂在本物体下。填入场景中的 HitRoot。")]
    [SerializeField] private Transform            hitboxParent;

    [Header("默认模组（无武器时使用）")]
    [SerializeField] private WeaponCombatModule unarmedModule;

    [Header("模组库")]
    [Tooltip("把所有 WeaponCombatModule 拖进来，或留空让运行时自动扫描 Resources")]
    [SerializeField] private List<WeaponCombatModule> moduleLibrary = new List<WeaponCombatModule>();

    [Header("Spine Event Key")]
    [SerializeField] private string hitStartEventKey = "hit_start";
    [SerializeField] private string hitEndEventKey   = "hit_end";
    [Tooltip("空中攻击悬停结束(开始真正下坠)的时机点——2026-07-21改成独立事件，不再\n" +
        "跟hit_end绑一起：hit_end管的是近战判定框关闭/挥空音效，跟悬停该什么时候\n" +
        "结束是两件不同的事，动画上未必是同一帧。")]
    [SerializeField] private string jumpStayEventKey = "jump_stay";

    [Header("空手攻击扬尘")]
    [Tooltip("空手攻击扬尘相对角色根节点、朝当前朝向的前方偏移。不追踪拳头骨骼——不同角色骨架规格不一(部分敌人没有专门的手部骨骼)，追踪出来的位置时准时不准，固定偏移对所有角色稳定一致。")]
    [SerializeField] private float unarmedAttackDustForwardOffset = 1f;
    [Tooltip("空手攻击扬尘相对角色根节点的高度偏移。")]
    [SerializeField] private float unarmedAttackDustHeightOffset = 2.8f;

    [Header("调试")]
    [SerializeField] private bool debugLogs = false;

    // ── 运行时状态 ────────────────────────────────────────────────────────


    private bool            _hitLandedThisActive = false;
    private UnitLoadRuntime _loadRuntime;

    // 攻击取消后撤步：只有判定帧真正开始过、且已经结束(hitbox不再Active)才算进入
    // "后摇阶段"，能被闪避键打断。每次新攻击开始(Try*Attack 系列)重置为false，
    // hit_start 触发时置true——避免前摇阶段(判定帧还没开始)就被误判成可以取消。
    private bool _attackActivePhaseStarted = false;

    // 攻击取消后撤步触发时冻结了 SpineAnimationDriver_Current 的朝向更新
    // (animationDriver.SetFacingHold(true))，这个后撤闪避一结束就要解除，不然角色会
    // 一直卡住朝向，之后正常走路/转身都失效。
    //
    // 2026-07-21：解除时机不能直接看 actionController.IsDodging——UnitMovementController.
    // RequestDodge 只是把闪避"排队"(dodgeQueued=true)，真正的 dodgeState 要等下一次
    // ConsumeQueuedActionInput 才会变成 Back，这中间有一帧空档 movement.IsDodging 还是
    // false；而 UnitActionController.RequestAttackCancelDodgeBack 又乐观地立刻把
    // currentState 设成了 Dodge，RefreshStateFromRuntime 在这个空档里看到
    // movement.IsDodging==false 会把 currentState 提前弹回 Normal，导致这里误判"已经
    // 结束"，冻结被过早解除——解除之后 UpdateFacing 立刻按残留的位移速度重新判定朝向，
    // 把角色转向了移动方向。实测日志(facingHoldActive True→False几乎同一帧)证实了这个
    // 时序竞争。修法：必须先亲眼确认真正的 movement.IsDodging 变过true(闪避真的开始
    // 了)，之后再看到false才解除，不会被这一帧的空档误伤。
    //
    // 2026-07-21（第二次）：实测确认过——闪避结束(movement.IsDodging变false)之后，
    // 角色水平速度不会自己衰减到0(这套 motor 没有被动摩擦力，停留在最后一次设置的
    // 速度上)，movement.FacingInput 的兜底逻辑会一直读到这个不会消失的残留速度，导致
    // 只要一解除冻结，UpdateFacing 立刻按残留速度把朝向转过去，且后续也不会自己转回来
    // (没有新输入触发判定)。等速度"自然归零"是死路——它根本不会归零。
    //
    // 2026-07-21（第三次）：改用 UnitMovementController.ImmediateStop()（受击硬直等
    // 场合已经在用的同一个方法：清输入+强制 motor 水平速度归零）主动清零，不再被动等。
    // 没有真实输入时，解冻前先 ImmediateStop() 清零残留速度，再用 ForceFacing() 把
    // 冻结期间全程没变过的朝向值摆正，最后才解冻——这样解冻后 UpdateFacing 读到的
    // FacingInput 必然是(0,0)，阈值判断不会被触发，不会再被残留速度带偏。
    private bool _holdingFacingForAttackCancelDodgeBack = false;
    private bool _hasSeenRealDodgeStartForFacingHold = false;
    private int _attackCancelDodgeBackHeldFacing = 1;

    // 空中攻击悬停：触发那一刻冻结跳跃垂直物理(movement.SetJumpVerticalPhysicsFrozen)，
    // 正常情况下 jump_stay 触发时解除（2026-07-21从hit_end改过来，见HandleSpineEvent）。
    // Update() 里有个兜底——如果攻击因为别的原因中途结束(比如中途被打断进HitStun)、
    // jump_stay 根本没机会触发，也要在这里强制解除，不然角色会一直卡在半空悬停出不来。
    private bool _aerialAttackHoverActive = false;

    // 蓄力攻击：动画播到 skill.charge.chargeHoldEventKey 那一帧时记下当前TrackEntry，
    // 冻结播放速度；ReleaseChargeAttack() 被调用时如果还是这个TrackEntry（没被别的
    // 动作打断掉），就把播放速度恢复，让动画继续播完剩下的部分。
    private TrackEntry _chargeHeldTrackEntry;
    public bool IsChargeHeld => _chargeHeldTrackEntry != null;

    // 玩家点得很快时，松开蓄力键那一刻动画可能还没播到charge_hold定格帧
    // （_chargeHeldTrackEntry还是null），ReleaseChargeAttack会直接空跑返回——之后
    // charge_hold真的触发、进入"等待松手"状态时，不会再有第二次松开事件，角色会卡在
    // 定格姿势里出不来。用这个标记记住"松手事件已经先到了"，charge_hold触发那一刻
    // 检查一下，有的话立刻释放，不再傻等一个不会再来的松开事件。
    private bool _chargeReleaseRequestedEarly;

    // 2026-07-19：蓄力参数已经从本组件搬到SkillDefinition.charge上(每个技能自己的
    // 蓄力手感，不再是所有蓄力技共用一份)。这里记一下charge_hold触发那一刻是哪个
    // 技能在蓄力，ReleaseChargeAttack/UpdateChargeHoldLpDrain都要读它的charge数据，
    // 不能直接用_currentSkill——万一蓄力过程中_currentSkill被别的逻辑改掉，这份快照
    // 还是指向真正在蓄力的那个技能。
    private SkillDefinition _chargeHeldSkill;

    // 蓄力冲刺特效：释放那一刻 PlayEffect 拿到 handle，冲刺持续期间每帧 SetLocation
    // 跟着角色走（这个特效是 Effekseer 的軌跡/Trail 类型，靠发射点真实位移画飘带，
    // 不跟着走就画不出来）。到 _chargeDashVfxUntil 时间点就不再管它，让它自己播完
    // 自然消失，不主动 Stop——避免飘带被强行掐断显得突兀。
    private EffekseerHandle _chargeDashVfxHandle;
    private float _chargeDashVfxUntil;
    // Update 里的 UpdateChargeDashVfxFollow 逐帧跟随时，_chargeHeldSkill 已经在
    // ReleaseChargeAttack 末尾清空了，所以锚点模式/偏移量要在释放那一刻先缓存下来。
    private ChargeDashVfxAnchorMode _chargeDashVfxAnchorMode;
    private Vector3 _chargeDashVfxCharacterAnchorOffset;

    // 通用位移特效（SkillDefinition.travelVfx）——跟蓄力冲刺特效是同一套锚点/跟随
    // 机制，但用独立的一份状态，不跟蓄力那套共用，避免两个系统万一同时触发时互相
    // 干扰。闪避接突刺（RequestDodgeThrust）用这个，不是charge专属。
    private EffekseerHandle _travelVfxHandle;
    private float _travelVfxUntil;
    private ChargeDashVfxAnchorMode _travelVfxAnchorMode;
    private Vector3 _travelVfxCharacterAnchorOffset;

    // 从charge_hold触发那一刻记录时间，ReleaseChargeAttack时算握住了多久，满
    // charge.fullChargeHoldSeconds才算"蓄满"，HandleHit结算伤害时读这个标记加成。
    // 只在本次攻击有效，命中结算完/下一次攻击开始就清掉，不会跨攻击误留。
    private float _chargeHoldStartTime;
    private bool  _pendingAttackWasFullyCharged;

    // 蓄满提示音只在真正刚跨过蓄满门槛的那一帧播一次——每帧都判一次"是否已经蓄满"
    // 没有这个标记的话，条件会连续多帧成立，提示音会跟着连续多帧重复播放。
    // 每次charge_hold重新定格时(见HandleSpineEvent)清成false，一次蓄力只提示一次。
    private bool _fullChargeSEPlayed;

    // 每次真正触发一次新攻击（RequestLightAttack/RequestHeavyAttack成功）就自增。
    // SpineAnimationDriver_Current靠这个判断"这是不是一次全新的攻击请求"，强制动画
    // 从头重新播放——如果连招里两下刚好用了同一个动画名字，PlayByKey"同名不重复
    // 播放"这个给站立/走路设计的优化会误伤攻击，音效/连段逻辑正常但动画卡在原地不重播。
    public int AttackRequestSequence { get; private set; }

    private WeaponCombatModule _currentModule;
    private SkillDefinition        _currentSkill;
    private int                    _comboIndex;
    private float                  _comboResetTime;
    private const float            ComboResetWindow = 2.0f;
    private bool                   _hitboxWasAutoCreated;

    // 当前装备武器的弹药配置——EquipWeapon/UnequipWeapon 时更新，只有玩家输入路径
    // （TryPlayerRequestXxxAttack）会检查/扣减，AI 敌人打枪暂时不受这套约束（弹药框架
    // 目前只服务玩家背包，敌人共享同一份检查逻辑没有意义）。
    private ItemEquipmentExtension _currentWeaponExt;

    // ── 换弹 ─────────────────────────────────────────────────────────────
    // 换弹分两步：TryPlayerRequestReload 成功后只是让 UnitActionController 进入
    // Reload状态锁一段时间，真正的"弹药从背包搬进弹匣"这个数值变化要等 Update() 里
    // 检测到 Reload状态自然结束(回到Normal)才发生——命中打断(currentState被
    // ForceHitStun/ForceDead直接改写成HitStun/Dead，不会经过Normal)时不会走到这一步，
    // 弹药就不会被填进去，符合"换弹被打断，弹药不填，要重新按键"的设计。
    private InventoryItemEntry _reloadTargetEntry;
    private bool                _wasReloadingLastFrame;

    /// <summary>弹匣数值(消耗/换弹补充)发生变化时触发——SkyPrisonWeaponSwitchHUD订阅这个
    /// 来实时刷新xx/xx弹药显示，不用等EquipmentRuntime.OnEquipped/OnActiveWeaponChanged
    /// 这些"换装备"才触发的事件（那些事件在同一把枪连续开火/换弹时根本不会触发）。</summary>
    public static event System.Action OnWeaponAmmoChanged;

    public WeaponCombatModule CurrentModule => _currentModule;
    /// <summary>当前是否处于空手模组（未装备武器，或装备的武器没配 weaponModuleKey 落回默认）。</summary>
    public bool IsUnarmed => _currentModule == unarmedModule;
    /// <summary>当前生效模组配置的"闪避接突刺"开窗比例——给 InputRouter 调用
    /// actionController.RequestDodgeThrust() 时传参用，跟 TryPlayerRequestDodgeThrust
    /// 里判断用的是同一个值，两次判断必须一致。</summary>
    public float CurrentDodgeThrustOpenAfterFraction =>
        (_currentModule ?? unarmedModule)?.dodgeThrustOpenAfterFraction ?? 0.6f;
    public SkillDefinition        CurrentSkill  => _currentSkill;
    public int                    ComboIndex    => _comboIndex;
    /// <summary>当前生效的判定框——武器视觉挂件（WeaponVisualRuntime）装备近战武器时
    /// 要用它的 SetBoundingBoxSource 把判定形状来源从徒手切到武器身上，不整个替换
    /// hitbox 组件（省得跟 SetHitbox 的自动销毁逻辑打架）。</summary>
    public UnitCombatHitbox Hitbox => hitbox;
    public SkeletonAnimation SkeletonAnimation => skeletonAnimation;

    /// <summary>返回下一次轻攻击技能的 hitbox 半径（含 offset.x），用于 AI 攻击距离判定。找不到时返回 fallback。</summary>
    public SkillDefinition GetNextLightAttackSkill()
    {
        if (_currentModule == null) return null;
        var combo = _currentModule.lightAttackCombo;
        if (combo == null || combo.Count == 0) return null;
        return combo[_comboIndex % combo.Count];
    }

    public float GetNextLightAttackRange(float fallback = 1.5f)
    {
        if (_currentModule == null) return fallback;
        var combo = _currentModule.lightAttackCombo;
        if (combo == null || combo.Count == 0) return fallback;
        int idx = (_comboIndex % combo.Count);
        SkillDefinition skill = combo[idx];
        if (skill == null || skill.hitbox == null) return fallback;
        return skill.hitbox.radius + Mathf.Abs(skill.hitbox.offset.x);
    }

    /// <summary>由 Applier 从 UnitDefinition.defaultCombatModule 注入，覆盖 Inspector 上的 unarmedModule 字段。</summary>
    public void SetDefaultModule(WeaponCombatModule module)
    {
        if (module == null) return;
        unarmedModule = module;
        // 如果当前还没有激活模组（Awake 时用了 null），立刻应用
        if (_currentModule == null)
            ApplyModule(unarmedModule);
    }

    public void SetHitboxParent(Transform parent)
    {
        if (hitboxParent == null)
            hitboxParent = parent;
    }

    /// <summary>
    /// 由 Applier 显式传入正确的 SkeletonAnimation（避免拿到 outline proxy 骨架）。
    /// 需要在 OnEnable 订阅之前调用。
    /// </summary>
    public void SetSkeletonAnimation(SkeletonAnimation anim)
    {
        if (anim == null) return;

        // 先取消旧订阅
        if (skeletonAnimation != null)
            skeletonAnimation.AnimationState.Event -= HandleSpineEvent;

        skeletonAnimation = anim;

        // 如果已经 OnEnable 过，立刻重新订阅
        if (isActiveAndEnabled)
            skeletonAnimation.AnimationState.Event += HandleSpineEvent;
    }

    /// <summary>
    /// 替换 hitbox 引用（Applier 扫到 Spine BoundingBox 后调用）。
    /// 若原有 hitbox 是自动建的则销毁它。
    /// </summary>
    public void SetHitbox(UnitCombatHitbox newHitbox)
    {
        if (newHitbox == null || newHitbox == hitbox) return;

        if (hitbox != null)
        {
            hitbox.OnHit -= HandleHit;
            if (_hitboxWasAutoCreated)
                Destroy(hitbox.gameObject);
        }

        hitbox = newHitbox;
        hitbox.OnHit += HandleHit;
        _hitboxWasAutoCreated = false;

        if (debugLogs)
            Debug.Log($"[ActionModule] Hitbox 替换为 {newHitbox.name}", this);
    }

    // ── 生命周期 ──────────────────────────────────────────────────────────

    private void Awake()
    {
        ResolveReferences();
        RefreshModule();
    }

    private void OnEnable()
    {
        ResolveReferences();

        if (skeletonAnimation != null)
            skeletonAnimation.AnimationState.Event += HandleSpineEvent;

        if (hitbox != null)
            hitbox.OnHit += HandleHit;

        if (actionController != null)
            actionController.BufferedAttackReady += HandleBufferedAttackReady;

        // TODO: 装备系统就绪后接入
        // inventoryRuntime.OnEquipmentChanged += HandleEquipmentChanged;
    }

    private void OnDisable()
    {
        if (skeletonAnimation != null)
            skeletonAnimation.AnimationState.Event -= HandleSpineEvent;

        if (hitbox != null)
            hitbox.OnHit -= HandleHit;

        if (actionController != null)
            actionController.BufferedAttackReady -= HandleBufferedAttackReady;

        // TODO: inventoryRuntime.OnEquipmentChanged -= HandleEquipmentChanged;
    }

    // UnitActionController 硬直/锁定期间缓冲的攻击请求，锁定一结束准备自动补发时触发
    // 这个回调——重新走一遍正常的技能选定流程（设_currentSkill、扣LP），再回调
    // actionController 真正进入攻击状态，不直接播动画。见 UnitActionController.
    // BufferedAttackReady 上的详细注释（"连续攻击后蓄力失灵"这个bug的根因）。
    private void HandleBufferedAttackReady(UnitActionController.AttackRequestKind kind)
    {
        if (kind == UnitActionController.AttackRequestKind.Light)
        {
            if (TryPlayerRequestLightAttack())
                actionController.RequestLightAttack();
        }
        else if (kind == UnitActionController.AttackRequestKind.Heavy)
        {
            if (TryPlayerRequestHeavyAttack())
                actionController.RequestHeavyAttack();
        }
    }

    private void Update()
    {
        if (_comboIndex > 0 && Time.time > _comboResetTime)
        {
            _comboIndex = 0;
            if (debugLogs) Debug.Log("[ActionModule] 连段超时重置", this);
        }

        // 2026-07-19：蓄力优先级现在高于跑步/潜行——按住跑步/潜行键不会打断蓄力，
        // 蓄力键本身的意图更明确、优先生效。之前这里有一段跑步/潜行会打断蓄力的逻辑
        // （通过InterruptChargeAttack），用户明确要求去掉。蓄力期间的移动速度依然只
        // 允许走路(不允许真的跑/潜行加速)，这个限制没变——只是不再因为按了跑步/
        // 潜行键就直接取消整次蓄力。

        if (_chargeHeldTrackEntry != null)
        {
            UpdateChargeHoldLpDrain();
        }

        // 再判一次——UpdateChargeHoldLpDrain可能因为LP耗尽刚触发了ReleaseChargeAttack，
        // 这种情况下这次蓄力已经结束了，不该再判"是否刚好蓄满"。
        if (_chargeHeldTrackEntry != null)
        {
            UpdateFullChargeNotification();
        }

        UpdateChargeDashVfxFollow();
        UpdateTravelVfxFollow();

        if (_holdingFacingForAttackCancelDodgeBack)
        {
            bool reallyDodgingNow = movement != null && movement.IsDodging;
            if (reallyDodgingNow)
            {
                _hasSeenRealDodgeStartForFacingHold = true;
            }
            else if (_hasSeenRealDodgeStartForFacingHold)
            {
                bool realInputActive = movement != null && movement.MoveInput.sqrMagnitude > 0.0001f;

                // 没有真实输入的话，解冻前先强制清零残留速度(ImmediateStop，跟受击硬直
                // 用的是同一个方法)，再把冻结期间全程没变过的朝向值摆正——这样解冻后
                // UpdateFacing 读到的 FacingInput 必然是(0,0)，不会再被"不会自己消失"的
                // 残留速度带偏。玩家如果正好有真实输入，说明是他自己的操作意图，直接
                // 放行，不做任何强制处理。
                if (!realInputActive)
                {
                    movement?.ImmediateStop();
                    animationDriver?.ForceFacing(_attackCancelDodgeBackHeldFacing);
                }

                animationDriver?.SetFacingHold(false);
                _holdingFacingForAttackCancelDodgeBack = false;
                _hasSeenRealDodgeStartForFacingHold = false;
            }
        }

        // 兜底：正常情况下悬停在 jump_stay 就解除了（见HandleSpineEvent），但如果这次
        // 空中攻击中途被打断(比如悬停中被敌人打中进了HitStun，动画Complete/jump_stay
        // 都不会再触发)，攻击状态一旦不再是Aerial就必须强制解冻，不然角色会一直卡在
        // 半空出不来。
        if (_aerialAttackHoverActive &&
            (actionController == null || actionController.CurrentAttackKind != UnitActionController.AttackRequestKind.Aerial))
        {
            movement?.SetJumpVerticalPhysicsFrozen(false);
            _aerialAttackHoverActive = false;
        }

        UpdateReloadCompletion();
    }

    /// <summary>换弹状态结束这一帧检查是不是"自然结束"(回到Normal，说明整段换弹耗时
    /// 都撑完了)——是的话才真正把弹药从背包搬进弹匣；如果是被命中/死亡打断
    /// (currentState变成HitStun/Dead而不是Normal)，弹药不会被填，符合"打断要重新
    /// 按键"的设计。</summary>
    private void UpdateReloadCompletion()
    {
        bool isReloadingNow = actionController != null && actionController.IsReloading;
        if (isReloadingNow)
        {
            _wasReloadingLastFrame = true;
            return;
        }

        if (!_wasReloadingLastFrame)
            return;

        _wasReloadingLastFrame = false;

        InventoryItemEntry entry = _reloadTargetEntry;
        _reloadTargetEntry = null;
        if (entry == null) return;
        if (actionController.CurrentState != UnitActionController.UnitActionState.Normal) return; // 被打断，弹药不填

        ItemEquipmentExtension ext = entry.definition?.equipment;
        var inventory = InventoryRuntimeBootstrap.Instance?.Inventory;
        if (ext == null || inventory == null) return;

        int room = Mathf.Max(0, ext.magazineSize - entry.currentMagazineAmmo);
        int available = inventory.GetAmmoCount(ext.ammoCaliber);
        int take = Mathf.Min(room, available);
        if (take <= 0) return;

        inventory.TryConsumeAmmo(ext.ammoCaliber, take);
        entry.currentMagazineAmmo += take;
        OnWeaponAmmoChanged?.Invoke();
    }

    /// <summary>当前生效武器对应的背包物品实例——弹匣数值(currentMagazineAmmo)是per-实例
    /// 数据，存在这个InventoryItemEntry上，不是_currentWeaponExt(那只是共享的ScriptableObject
    /// 配置数据)。只服务玩家(走EquipmentRuntime.Instance)，AI敌人没有走这条装备路径，拿到
    /// null是正常情况。</summary>
    private InventoryItemEntry ResolveCurrentWeaponEntry()
    {
        var eq = EquipmentRuntime.Instance;
        if (eq == null) return null;
        return eq.GetEquipped(eq.ActiveWeaponSlot);
    }

    /// <summary>由 InputRouter 在玩家按下换弹键时调用——检查武器是否吃弹药、弹匣是否
    /// 已满、背包是否还有备用弹药、当前状态是否允许进入换弹，都通过才真正进入Reload
    /// 状态。真正的弹药数值变化延迟到换弹自然结束才发生，见 UpdateReloadCompletion。</summary>
    public bool TryPlayerRequestReload()
    {
        if (actionController == null || !actionController.CanEnterReloadPublic())
            return false;

        InventoryItemEntry weaponEntry = ResolveCurrentWeaponEntry();
        ItemEquipmentExtension ext = weaponEntry?.definition?.equipment;
        if (ext == null || !ext.usesAmmo) return false;
        if (weaponEntry.currentMagazineAmmo >= ext.magazineSize) return false; // 弹匣已满，不用换

        var inventory = InventoryRuntimeBootstrap.Instance?.Inventory;
        if (inventory == null || inventory.GetAmmoCount(ext.ammoCaliber) <= 0) return false; // 背包没备用弹药

        if (!actionController.RequestReload(ext.reloadDurationSeconds))
            return false;

        _reloadTargetEntry = weaponEntry;
        return true;
    }

    /// <summary>冲刺特效跟随——軌跡类型靠发射点真实位移画飘带，必须逐帧把发射点位置
    /// 更新到角色当前位置，否则画不出来/直接缩成一个点。冲刺结束（_chargeDashVfxUntil）
    /// 之后不再更新位置也不主动 Stop，让飘带按自己的播放时长自然播完消失。</summary>
    private void UpdateChargeDashVfxFollow()
    {
        if (!_chargeDashVfxHandle.exists)
            return;

        if (Time.time >= _chargeDashVfxUntil)
            return;

        _chargeDashVfxHandle.SetLocation(ResolveChargeDashVfxWorldPosition());
    }

    /// <summary>通用位移特效跟随——跟 UpdateChargeDashVfxFollow 是同一个道理，只是给
    /// SkillDefinition.travelVfx（闪避接突刺等非蓄力技能的位移特效）用的独立一份。</summary>
    private void UpdateTravelVfxFollow()
    {
        if (!_travelVfxHandle.exists)
            return;

        if (Time.time >= _travelVfxUntil)
            return;

        _travelVfxHandle.SetLocation(ResolveDashVfxWorldPosition(_travelVfxAnchorMode, _travelVfxCharacterAnchorOffset));
    }

    /// <summary>播放一次 SkillDefinition.travelVfx 并在 durationSeconds 内逐帧跟随角色
    /// 位置——绑定方式（锚点/朝向镜像/渲染层）完全参照 ReleaseChargeAttack 里冲刺特效
    /// 那一套，保证同一套特效在不同技能里表现一致。</summary>
    private void PlayTravelVfx(SkillDefinition skill, float durationSeconds)
    {
        if (skill == null || skill.travelVfx == null) return;

        _travelVfxAnchorMode = skill.travelVfxAnchor;
        _travelVfxCharacterAnchorOffset = skill.travelVfxCharacterAnchorOffset;
        Vector3 vfxSpawnPosition = ResolveDashVfxWorldPosition(_travelVfxAnchorMode, _travelVfxCharacterAnchorOffset);

        float facing = animationDriver != null ? animationDriver.Facing : 1f;

        // 朝左用默认姿势(identity)，朝右在这基础上加180度——跟冲刺特效同一套约定。
        Quaternion vfxRotation = facing == -1 ? Quaternion.Euler(0f, 0f, 180f) : Quaternion.identity;

        if (_travelVfxHandle.exists)
            _travelVfxHandle.Stop();

        _travelVfxHandle = EffekseerSystem.PlayEffect(skill.travelVfx, vfxSpawnPosition);
        _travelVfxHandle.SetRotation(vfxRotation);

        // 装饰性粒子造型是不对称手性图形，镜像朝右时单靠旋转转不出镜像版本，必须用
        // 负缩放做真正的几何翻转——跟冲刺特效/挥剑特效验证过的一样是Y轴取负。
        Vector3 vfxScale = facing == -1 ? new Vector3(1f, -1f, 1f) : Vector3.one;
        _travelVfxHandle.SetScale(vfxScale);

        int vfxLayer = LayerMask.NameToLayer("World3D");
        if (vfxLayer < 0) vfxLayer = 0;
        _travelVfxHandle.layer = vfxLayer;

        _travelVfxUntil = Time.time + Mathf.Max(0.01f, durationSeconds);
    }

    // 骨架里有个独立的Slot，Slot名字和它下面挂的Point Attachment名字都叫"fx_tip"
    // （不是挂在武器网格那个weapon_sword_heavySpade插槽下面共用的）。
    private const string ChargeDashVfxFxTipSlotName = "fx_tip";
    private const string ChargeDashVfxFxTipAttachmentName = "fx_tip";

    private Vector3 ResolveChargeDashVfxWorldPosition() =>
        ResolveDashVfxWorldPosition(_chargeDashVfxAnchorMode, _chargeDashVfxCharacterAnchorOffset);

    // 通用版本——蓄力冲刺特效和闪避接突刺的位移特效共用同一套锚点解析逻辑，
    // 区别只在于各自缓存的锚点模式/偏移量。
    private Vector3 ResolveDashVfxWorldPosition(ChargeDashVfxAnchorMode anchorMode, Vector3 characterOffset)
    {
        Transform vfxAnchor = skeletonAnimation != null ? skeletonAnimation.transform : transform;

        if (anchorMode == ChargeDashVfxAnchorMode.Character)
        {
            // vfxAnchor(skeletonAnimation.transform，也就是spineRoot)朝右时localScale.x
            // 已经被 SpineAnimationDriver_Current.ApplyFacing 取负镜像过了，TransformPoint
            // 用这个已经镜像过的Transform算，本身就会跟着镜像——不能再手动对offset.x取一次
            // 负，两次取反会正好抵消（跟挥剑特效踩过的坑10同一个模式）。
            return vfxAnchor.TransformPoint(characterOffset);
        }

        if (skeletonAnimation != null && skeletonAnimation.Skeleton != null)
        {
            Skeleton skeleton = skeletonAnimation.Skeleton;
            Slot slot = skeleton.FindSlot(ChargeDashVfxFxTipSlotName);
            if (slot != null)
            {
                Attachment attachment = skeleton.GetAttachment(slot.Data.Index, ChargeDashVfxFxTipAttachmentName);
                if (attachment is PointAttachment point)
                {
                    point.ComputeWorldPosition(slot.Bone.AppliedPose, out float localX, out float localY);
                    return skeletonAnimation.transform.TransformPoint(new Vector3(localX, localY, 0f));
                }
            }
        }

        return vfxAnchor.position;
    }

    /// <summary>hit_start 触发、且 skill.isProjectileSkill 时调用——从武器尖端(fx_tip)
    /// 生成一发真正会飞的剑气抛射物，方向跟着角色朝向镜像(面右朝右下偏转，面左朝
    /// 左下偏转，偏转角度由 skill.projectile.launchAngleDownDegrees 配置)，同时给
    /// 角色施加一个跟发射方向相反的小顿挫后坐力。</summary>
    private void FireProjectileSkill(SkillDefinition skill)
    {
        if (skill == null || skill.projectile == null) return;

        // facingSign==-1(面右)对应世界+X，facingSign==1(面左)对应世界-X——这个映射
        // 已经在攻击取消后撤步那次实测确认过，直接复用，不用再验证一遍。
        int facingSign = animationDriver != null ? animationDriver.Facing : 1;
        float horizontalSign = facingSign == -1 ? 1f : -1f;

        float angleRad = skill.projectile.launchAngleDownDegrees * Mathf.Deg2Rad;
        Vector3 direction = new Vector3(horizontalSign * Mathf.Cos(angleRad), -Mathf.Sin(angleRad), 0f);
        if (direction.sqrMagnitude > 0.0001f) direction.Normalize();

        Vector3 spawnPosition = ResolveDashVfxWorldPosition(ChargeDashVfxAnchorMode.WeaponTip, Vector3.zero);
        // 深度(Z)按角色脚底深度对齐，不用武器尖端锚点自己的Z——武器/手臂挂在骨骼上，
        // 挥砍/持械姿势不同时Z会跟着抖动，用角色根节点(约等于脚底)的Z更稳定，也跟
        // 这个2.5D项目里其它按深度排序的效果(比如脚步扬尘)是同一个基准。
        spawnPosition.z = transform.position.z;
        SkyPrisonSwordQiProjectile.Spawn(gameObject, this, skill, spawnPosition, direction);

        // 后坐力只用水平分量、走既有的X轴击退通道(ApplyKnockback只处理世界X)——不用
        // 代码去改跳跃的垂直速度，避免跟重力/下落物理打架，"后仰"的视觉交给攻击动画
        // 本身的表现。
        actionController?.ApplyAerialAttackRecoil(new Vector2(-direction.x, 0f));
    }

    /// <summary>蓄力定格(charge_hold已触发，还没释放)期间每秒持续消耗LP。中途正好耗尽
    /// 的话直接当成玩家自己松开了蓄力键，触发ReleaseChargeAttack——不会让LP耗尽之后
    /// 还能白嫖继续蓄力。</summary>
    private void UpdateChargeHoldLpDrain()
    {
        if (_chargeHeldSkill == null || _loadRuntime == null)
            return;

        float costPerSecond = _chargeHeldSkill.charge.holdLpCostPerSecond;
        if (costPerSecond <= 0f)
            return;

        float amount = costPerSecond * Time.deltaTime;
        _loadRuntime.TrySpendLoadAction(amount, "ChargeHold");

        if (_loadRuntime.CurrentLoad <= 0.0001f)
        {
            if (debugLogs) Debug.Log("[ActionModule] 蓄力LP耗尽 → 自动触发释放", this);
            ReleaseChargeAttack();
        }
    }

    /// <summary>蓄力刚好达到蓄满所需秒数的那一刻播放一次提示音，告诉玩家"已经蓄满了，
    /// 可以松手了"。用_fullChargeSEPlayed标记只在跨过门槛的那一帧播一次，不会每帧
    /// 重复播放。</summary>
    private void UpdateFullChargeNotification()
    {
        if (_fullChargeSEPlayed || _chargeHeldSkill == null)
            return;

        if (Time.time - _chargeHoldStartTime < _chargeHeldSkill.charge.fullChargeHoldSeconds)
            return;

        _fullChargeSEPlayed = true;
        PlaySkillSE(ResolveSE(_chargeHeldSkill.charge.fullChargeReachedSE, null), ResolveVolume(_chargeHeldSkill, _currentModule), transform.position);
        if (debugLogs) Debug.Log("[ActionModule] 蓄力已蓄满 → 播放提示音", this);
    }

    // ── 攻击请求（由 InputRouter / AI 调用） ─────────────────────────────

    public SkillDefinition RequestLightAttack()
    {
        if (_currentModule == null || actionController == null) return null;
        if (!actionController.CanEnterAttackPublic())          return null;

        var combo = _currentModule.lightAttackCombo;
        if (combo == null || combo.Count == 0) return null;

        _comboIndex = _comboIndex % combo.Count;
        SkillDefinition skill = combo[_comboIndex];
        if (skill == null) return null;

        if (!TryConsumeSkillLP(skill)) return null;

        _chargeHeldTrackEntry = null; // 新的一次攻击开始，清掉上一次可能残留的蓄力定格状态
        _chargeHeldSkill = null;
        _chargeReleaseRequestedEarly = false;
        _pendingAttackWasFullyCharged = false;
        _attackActivePhaseStarted = false;
        actionController?.SetChargeMovementAllowed(false); // 轻攻击不是蓄力技，防止上一次蓄力攻击万一没走ReleaseChargeAttack正常收尾，残留的"蓄力时允许走路"状态漏到这次普通轻攻击里
        AttackRequestSequence++;
        _currentSkill   = skill;
        _comboIndex     = (_comboIndex + 1) % combo.Count;
        _comboResetTime = Time.time + ComboResetWindow;

        actionController.RequestLightAttack();

        if (debugLogs) Debug.Log($"[ActionModule] 轻攻击 → {skill.skillKey}  连段[{_comboIndex}]", this);
        return skill;
    }

    public SkillDefinition RequestHeavyAttack()
    {
        if (_currentModule == null || actionController == null) return null;
        if (!actionController.CanEnterAttackPublic())          return null;

        SkillDefinition skill = _currentModule.heavyAttack;
        if (skill == null) return null;

        // 蓄力技(isChargeSkill)不在按下键这一刻一次性扣LP——改成蓄力定格期间(charge_hold
        // 触发到松开释放为止)按秒持续消耗，见SkillChargeData.holdLpCostPerSecond。
        // 非蓄力的重攻击不受影响，还是走TryConsumeSkillLP一次性扣费。
        if (!skill.isChargeSkill)
        {
            if (!TryConsumeSkillLP(skill)) return null;
        }

        _chargeHeldTrackEntry = null; // 新的一次攻击开始，清掉上一次可能残留的蓄力定格状态
        _chargeHeldSkill = null;
        _chargeReleaseRequestedEarly = false;
        _pendingAttackWasFullyCharged = false;
        _attackActivePhaseStarted = false;
        AttackRequestSequence++;
        _currentSkill   = skill;
        _comboIndex     = 0;
        _comboResetTime = 0f;

        // 蓄力技从按下键这一刻就允许继续走路，不用等动画播到 charge_hold 定格帧才解锁——
        // 之前是等 charge_hold 才调 SetChargeMovementAllowed(true)，按键瞬间到定格帧之间
        // 那段前摇动画期间移动输入还是被硬锁清零的，走着走着按蓄力键会有一下"停顿再走"
        // 的顿挫感。提前到这里授予，前摇和蓄力全程都能连贯走。
        actionController?.SetChargeMovementAllowed(skill.isChargeSkill);

        actionController.RequestHeavyAttack();

        if (debugLogs) Debug.Log($"[ActionModule] 重攻击 → {skill.skillKey}", this);
        return skill;
    }

    // ── 玩家输入通知（由 InputRouter 在调 actionController 之前调用） ──────
    // 不负责触发动画，只负责在 hit_start 事件前把 _currentSkill 设好。

    /// <summary>由 InputRouter 调用。检查能否出招 → 扣 LP → 设 _currentSkill → 返回是否继续触发
    /// actionController。出招SE不在这里播——绑在hit_start事件上，跟判定框一起开，见
    /// HandleSpineEvent。</summary>
    public bool TryPlayerRequestLightAttack()
    {
        if (actionController == null || !actionController.CanEnterAttackPublic()) return false;
        WeaponCombatModule mod = _currentModule ?? unarmedModule;
        if (mod == null) return false;
        var combo = mod.lightAttackCombo;
        if (combo == null || combo.Count == 0) return false;
        _comboIndex = _comboIndex % combo.Count;
        SkillDefinition skill = combo[_comboIndex];
        if (skill == null) return false;
        if (!TryConsumeSkillLP(skill)) return false;
        if (!TryConsumeAmmoForAttack()) return false;
        _currentSkill   = skill;
        _comboIndex     = (_comboIndex + 1) % combo.Count;
        _comboResetTime = Time.time + ComboResetWindow;
        _chargeReleaseRequestedEarly = false; // 新一次攻击开始，清掉上一次可能残留的标记
        _attackActivePhaseStarted = false;
        actionController?.SetChargeMovementAllowed(false); // 轻攻击不是蓄力技，防止上一次蓄力攻击残留的"蓄力时允许走路"状态漏进来
        return true;
    }

    /// <summary>由 InputRouter 调用。检查能否出招 → 扣 LP → 设 _currentSkill → 返回是否继续触发
    /// actionController。出招SE不在这里播——绑在hit_start事件上，跟判定框一起开，见
    /// HandleSpineEvent。</summary>
    public bool TryPlayerRequestHeavyAttack()
    {
        if (actionController == null || !actionController.CanEnterAttackPublic()) return false;
        WeaponCombatModule mod = _currentModule ?? unarmedModule;
        if (mod == null) return false;
        SkillDefinition skill = mod.heavyAttack;
        if (skill == null) return false;
        if (!TryConsumeSkillLP(skill)) return false;
        if (!TryConsumeAmmoForAttack()) return false;
        _currentSkill   = skill;
        _comboIndex     = 0;
        _comboResetTime = 0f;
        _chargeReleaseRequestedEarly = false; // 新一次攻击开始，清掉上一次可能残留的标记
        _attackActivePhaseStarted = false;
        // 玩家真正的攻击输入走的是这条路径（InputRouter 直接调 actionController.
        // RequestHeavyAttack()，不会经过下面那个同名的 UnitActionModuleRuntime.
        // RequestHeavyAttack()）——蓄力技从这一刻起就允许继续走路，不用等动画播到
        // charge_hold 定格帧才解锁，前摇和蓄力全程都能连贯走。
        actionController?.SetChargeMovementAllowed(skill.isChargeSkill);
        return true;
    }

    /// <summary>由 InputRouter 在角色处于跳跃空中阶段、按攻击键时调用——检查当前武器
    /// 模组是否配了空中攻击技能(WeaponCombatModule.aerialAttack)、这次跳跃还没用过
    /// (CanEnterAerialAttackPublic)，通过就扣 LP → 设 _currentSkill → 返回是否继续
    /// 触发 actionController.RequestAerialAttack()。真正的弹幕发射/后坐力在
    /// hit_start(见HandleSpineEvent→FireProjectileSkill)才会触发，不是这里。</summary>
    public bool TryPlayerRequestAerialAttack()
    {
        if (actionController == null || !actionController.CanEnterAerialAttackPublic()) return false;
        WeaponCombatModule mod = _currentModule ?? unarmedModule;
        if (mod == null) return false;
        SkillDefinition skill = mod.aerialAttack;
        if (skill == null) return false;
        if (!TryConsumeSkillLP(skill)) return false;
        if (!TryConsumeAmmoForAttack()) return false;
        _currentSkill   = skill;
        _comboIndex     = 0;
        _comboResetTime = 0f;
        _chargeReleaseRequestedEarly = false;
        _attackActivePhaseStarted = false;
        actionController.SetChargeMovementAllowed(false); // 空中攻击不是蓄力技

        // 悬停：从攻击一触发就开始，一直悬到 jump_stay 才解除（见 HandleSpineEvent 的
        // jump_stay 分支），不是等到 hit_start 弹幕发射才悬停。顺带把角色向上顶一小段
        // 高度(AerialAttackLiftHeight)，做出"鸟拍一下翅膀"那种干净利落的上升感；
        // 悬停期间不是完全定住不动，而是按 AerialAttackHoverGravityScale 缩小过的
        // 重力继续缓慢下坠，但跳过贴地判定，不会在演出结束前就先落地。
        float liftHeight = actionController != null ? actionController.AerialAttackLiftHeight : 0f;
        float hoverGravityScale = actionController != null ? actionController.AerialAttackHoverGravityScale : 0.15f;
        movement?.SetJumpVerticalPhysicsFrozen(true, liftHeight, hoverGravityScale);
        _aerialAttackHoverActive = true;

        return true;
    }

    /// <summary>由 InputRouter 在角色闪避快结束的可打断窗口内调用，触发专属的闪避接突刺技能
    /// （不走轻/重攻击那套武器连段选定）。检查能否出招（含时机窗口）→ 扣 LP →
    /// 设 _currentSkill → 返回是否继续触发 actionController.RequestDodgeThrust()。</summary>
    public bool TryPlayerRequestDodgeThrust()
    {
        // 闪避接突刺是武器模组自己的技能（跟轻/重攻击一样挂在 WeaponCombatModule 上），
        // 不是全局固定的一个技能——不同武器可以有不同的突刺，空手模组留空就是
        // "没有这个衔接"，直接返回false，闪避本身不受影响。窗口比例
        // (dodgeThrustOpenAfterFraction) 也是每个模组自己配的，要先拿到模组才能判断
        // 能不能出招，所以这里跟其它请求方法顺序不一样，先取 mod 再判 CanEnter。
        WeaponCombatModule mod = _currentModule ?? unarmedModule;
        if (mod == null) return false;
        if (actionController == null || !actionController.CanEnterDodgeThrustPublic(mod.dodgeThrustOpenAfterFraction)) return false;
        SkillDefinition skill = mod.dodgeThrustAttack;
        if (skill == null) return false;
        if (!TryConsumeSkillLP(skill)) return false;
        if (!TryConsumeAmmoForAttack()) return false;
        _currentSkill   = skill;
        _comboIndex     = 0;
        _comboResetTime = 0f;
        _chargeReleaseRequestedEarly = false;
        _attackActivePhaseStarted = false;
        actionController.SetChargeMovementAllowed(false); // 闪避突刺不是蓄力技

        // 位移特效：跟冲刺移动同步播放/跟随，锚点绑定方式参照蓄力突刺的冲刺特效
        // （ReleaseChargeAttack 那一套），不是挥砍瞬间打一下就结束的 swingVFX。
        PlayTravelVfx(skill, actionController.DodgeThrustDurationSeconds);

        return true;
    }

    /// <summary>由 InputRouter 在角色实际处于奔跑(Sprint)状态、按攻击键时调用——复用
    /// 闪避接突刺同一个技能(WeaponCombatModule.dodgeThrustAttack)，不新增技能槽位，
    /// 奔跑时攻击直接接这个突刺，而不是停下来打一次普通轻/重攻击。检查能否出招
    /// (CanEnterRunThrustPublic) → 扣 LP → 设 _currentSkill → 返回是否继续触发
    /// actionController.RequestRunThrust()。</summary>
    public bool TryPlayerRequestRunThrust()
    {
        if (actionController == null || !actionController.CanEnterRunThrustPublic()) return false;
        WeaponCombatModule mod = _currentModule ?? unarmedModule;
        if (mod == null) return false;
        SkillDefinition skill = mod.dodgeThrustAttack;
        if (skill == null) return false;
        if (!TryConsumeSkillLP(skill)) return false;
        if (!TryConsumeAmmoForAttack()) return false;
        _currentSkill   = skill;
        _comboIndex     = 0;
        _comboResetTime = 0f;
        _chargeReleaseRequestedEarly = false;
        _attackActivePhaseStarted = false;
        actionController.SetChargeMovementAllowed(false); // 奔跑突刺不是蓄力技

        // 同一套位移特效跟随，跟闪避接突刺共用一份逻辑。
        PlayTravelVfx(skill, actionController.DodgeThrustDurationSeconds);

        return true;
    }

    // 判断攻击取消闪避该接"前闪"还是"后闪"用的阈值——玩家这时候如果按着方向键、且
    // 方向跟角色当前朝向大致一致(点积超过这个值)，就当成前闪处理；没按键或者按的
    // 方向跟朝向不一致(包括相反)，走原来的后闪(固定沿朝向反方向、保持朝向不转身)。
    private const float AttackCancelForwardDodgeDotThreshold = 0.3f;

    /// <summary>由 InputRouter 在角色处于攻击状态、按下闪避键时调用——检查当前武器模组
    /// 是否允许这个衔接(WeaponCombatModule.allowAttackCancelDodgeBack)、判定帧是否已经
    /// 开始过且已经结束(后摇阶段，不能在前摇/判定帧激活期间打断)，通过就按玩家当前
    /// 是否按着"朝向方向"的方向键，分别接前闪(玩家方向键，播dodge_front，正常速度)
    /// 或后闪(固定朝向反方向，播dodge_back，速度打折，保持朝向不转身)。</summary>
    public bool TryPlayerRequestAttackCancelDodgeBack()
    {
        if (actionController == null || !actionController.IsAttacking) return false;

        // 空中攻击是弹幕技，不会 Activate 近战 hitbox——hitbox.IsActive 一直是false，
        // 如果不排除会导致"空中攻击一进入后摇阶段就能被闪避键打断成地面后撤步"这种
        // 人还在天上却触发了地面位移的错乱状态。这个后撤步只服务地面近战攻击。
        if (actionController.CurrentAttackKind == UnitActionController.AttackRequestKind.Aerial) return false;

        WeaponCombatModule mod = _currentModule ?? unarmedModule;
        if (mod == null || !mod.allowAttackCancelDodgeBack) return false;

        // 判定帧还没开始过（还在前摇）不能取消；判定帧还在激活中(hitbox.IsActive)也
        // 不能取消——只有真正播过判定帧、判定帧又已经关闭的后摇阶段才允许。
        if (!_attackActivePhaseStarted) return false;
        if (hitbox != null && hitbox.IsActive) return false;

        // facing==-1 对应世界+X方向——实测确认过的映射，不要再改。
        int facingSign = animationDriver != null ? animationDriver.Facing : 1;
        Vector2 facingVector = new Vector2(facingSign == -1 ? 1f : -1f, 0f);

        // 注意：不能读 movement.MoveInput 或 actionController.MoveInput——两个都会在
        // 攻击状态下被强制清零(movement.MoveInput 是 PushMovementIntent 清零发给movement
        // 的值；actionController.MoveInput 更早一步，SubmitMoveIntent 自己在 IsHardLocked
        // && !chargeMovementAllowed 时就已经把它摁成(0,0)了，攻击不是蓄力技所以恒成立)。
        // 必须读 RawMoveInputUnsuppressed——这个是 SubmitMoveIntent 一进来就无条件记录的
        // 原始按键方向，不受任何锁定状态影响。
        Vector2 realInput = actionController.RawMoveInputUnsuppressed;
        bool isForward = false;
        if (realInput.sqrMagnitude > 0.0001f)
        {
            float dot = Vector2.Dot(realInput.normalized, facingVector);
            isForward = dot > AttackCancelForwardDodgeDotThreshold;
        }

        if (isForward)
        {
            // 前闪：沿玩家实际按的方向走，是真正的闪避动作，不用冻结朝向——正常的
            // UpdateFacing() 本来就会让角色面朝这个方向，不需要额外处理。
            return actionController.RequestAttackCancelDodgeBack(realInput.normalized, UnitMovementController.DodgeRuntimeState.Forward);
        }

        // 后闪：朝向"保持不变"改成直接冻结 SpineAnimationDriver_Current.UpdateFacing()——
        // 拼一个特定符号的Vector2喂SetFacingOverride这条路反复踩坑(override要经过
        // UpdateFacing的阈值判断重新解释，实测两次结果都不稳定，不是简单的"符号猜反了"
        // 能一次解决的)，直接冻结facing变量不参与任何符号换算，不会再转向。
        Vector2 backDirection = -facingVector;

        if (animationDriver != null)
        {
            animationDriver.SetFacingHold(true);
            _holdingFacingForAttackCancelDodgeBack = true;
            _hasSeenRealDodgeStartForFacingHold = false;
            _attackCancelDodgeBackHeldFacing = facingSign;
        }

        bool started = actionController.RequestAttackCancelDodgeBack(backDirection, UnitMovementController.DodgeRuntimeState.Back);
        if (!started && animationDriver != null)
        {
            // TP不够时 RequestAttackCancelDodgeBack 现在会直接返回false——上面提前设置的
            // 朝向冻结必须撤销，否则闪避根本没发生(movement.IsDodging 永远不会变true)，
            // Update()里那个"看到过真实闪避开始才允许释放"的安全网永远等不到触发条件，
            // 朝向会被永久卡住转不动。
            animationDriver.SetFacingHold(false);
            _holdingFacingForAttackCancelDodgeBack = false;
        }
        return started;
    }

    // 保留旧名以防其他地方引用
    public void OnPlayerRequestLightAttack()  => TryPlayerRequestLightAttack();
    public void OnPlayerRequestHeavyAttack()  => TryPlayerRequestHeavyAttack();

    // ── 模组切换 ─────────────────────────────────────────────────────────

    /// <summary>装备变化时由外部（装备系统）调用。</summary>
    public void NotifyEquipmentChanged() => RefreshModule();

    /// <summary>装备武器时调用。优先用 ext.combatModule 直接引用，无则按 weaponModuleKey 查库，再无则保持 unarmed。</summary>
    public void EquipWeapon(ItemEquipmentExtension ext)
    {
        _currentWeaponExt = ext;

        if (ext == null) { ApplyModule(unarmedModule); return; }

        WeaponCombatModule module = ext.combatModule;

        if (module == null && !string.IsNullOrEmpty(ext.weaponModuleKey))
            module = FindModule(ext.weaponModuleKey);

        ApplyModule(module ?? unarmedModule);
    }

    /// <summary>卸下武器时调用，回到单位默认模组。</summary>
    public void UnequipWeapon()
    {
        _currentWeaponExt = null;
        ApplyModule(unarmedModule);
    }

    /// <summary>当前武器不需要弹药（近战/未装备）时直接放行；需要弹药时检查弹匣
    /// (currentMagazineAmmo，不是背包)够不够，够就扣掉返回true，不够整个不扣、返回
    /// false（打不出去，需要先按换弹键）。2026-07-21：弹药消耗来源从"直接扣背包"
    /// 改成"扣弹匣"，背包只在换弹(TryPlayerRequestReload/UpdateReloadCompletion)时
    /// 才会被扣。找不到装备实例(比如AI敌人没走EquipmentRuntime这条路)时视为放行，
    /// 不该被弹药规则卡住。</summary>
    private bool TryConsumeAmmoForAttack()
    {
        if (_currentWeaponExt == null || !_currentWeaponExt.usesAmmo)
            return true;

        InventoryItemEntry weaponEntry = ResolveCurrentWeaponEntry();
        if (weaponEntry == null)
            return true;

        if (weaponEntry.currentMagazineAmmo < _currentWeaponExt.ammoPerShot)
            return false;

        weaponEntry.currentMagazineAmmo -= _currentWeaponExt.ammoPerShot;
        OnWeaponAmmoChanged?.Invoke();
        return true;
    }

    /// <summary>强制切换到指定 moduleKey，传空串切回 unarmed。</summary>
    public void SetModuleByKey(string key)
    {
        WeaponCombatModule module = string.IsNullOrEmpty(key) ? null : FindModule(key);
        ApplyModule(module ?? unarmedModule);
    }

    /// <summary>重新同步当前生效模组——不能无脑退回空手，得先去问一下 EquipmentRuntime
    /// 当前生效武器槽(ActiveWeaponSlot)里实际装的是什么。2026-07-21修复：这里以前无
    /// 条件 ApplyModule(unarmedModule)，而这是 Awake() 里唯一会跑到的初始化路径——
    /// 意味着任何时候 UnitActionModuleRuntime 被重新创建(读档后场景重建角色、章节内
    /// 切地图、进本出本)，不管 EquipmentRuntime(DontDestroyOnLoad单例)里实际记着装备了
    /// 什么武器，战斗模组永远重置成空手，HUD却还在照 EquipmentRuntime 原样显示着武器
    /// 图标——两边对不上，表现就是"背包/HUD显示有武器，但手上打的是空手"。真正的
    /// 装备/卸下(Equip/Unequip/CycleActiveWeapon)本身没问题，缺的是这条"组件刚创建时
    /// 主动去问一次当前状态"的拉取同步。</summary>
    public void RefreshModule()
    {
        var eq = EquipmentRuntime.Instance;
        InventoryItemEntry activeWeaponEntry = eq != null ? eq.GetEquipped(eq.ActiveWeaponSlot) : null;

        if (activeWeaponEntry?.definition?.equipment != null)
            EquipWeapon(activeWeaponEntry.definition.equipment);
        else
            ApplyModule(unarmedModule);
    }

    private void ApplyModule(WeaponCombatModule module)
    {
        if (module == null || module == _currentModule) return;
        _currentModule  = module;
        _currentSkill   = null;
        _comboIndex     = 0;
        _comboResetTime = 0f;

        if (hitbox != null && hitbox.IsActive)
            hitbox.Deactivate();

        if (debugLogs) Debug.Log($"[ActionModule] 切换模组 → {_currentModule?.moduleKey ?? "null"}", this);
    }

    // moduleLibrary 里没配全的武器 key 会走到这个兜底分支——之前每次都现场
    // Resources.LoadAll<WeaponCombatModule>("") 扫一遍整个 Resources 目录树，
    // 而这个方法在每次切武器（含滚轮/手柄快速切换）时都会调用，等于把一次昂贵的
    // 全资源扫描绑定在了高频操作上。改成只扫一次、结果缓存到静态字典里复用。
    private static Dictionary<string, WeaponCombatModule> s_ResourcesModuleCache;

    private WeaponCombatModule FindModule(string key)
    {
        foreach (var m in moduleLibrary)
            if (m != null && m.moduleKey == key) return m;

        if (s_ResourcesModuleCache == null)
        {
            s_ResourcesModuleCache = new Dictionary<string, WeaponCombatModule>();
            foreach (var m in Resources.LoadAll<WeaponCombatModule>(""))
                if (m != null && !string.IsNullOrEmpty(m.moduleKey))
                    s_ResourcesModuleCache[m.moduleKey] = m;
        }

        return s_ResourcesModuleCache.TryGetValue(key, out var found) ? found : null;
    }

    // ── Spine Event ───────────────────────────────────────────────────────

    private void HandleSpineEvent(TrackEntry trackEntry, Spine.Event e)
    {
        if (e == null) return;

        Debug.Log($"[ActionModule] 诊断：Spine事件={e.Data.Name}，_currentSkill={_currentSkill?.skillKey ?? "NULL"}，" +
                  $"isChargeSkill={(_currentSkill != null ? _currentSkill.isChargeSkill.ToString() : "N/A")}", this);

        if (e.Data.Name == hitStartEventKey)
        {
            if (debugLogs) Debug.Log($"[ActionModule] hit_start → skill={_currentSkill?.skillKey ?? "NULL"}  module={_currentModule?.moduleKey ?? "NULL"}", this);
            _hitLandedThisActive = false;
            _attackActivePhaseStarted = true;

            // 出招SE(挥砍音)绑在这个事件上，跟判定框/弹幕一起触发——之前是请求一被接受
            // 就立刻播，跟动画播放进度完全无关；蓄力技尤其明显：按下键那一刻就
            // 放挥砍音效，但角色还要经历前摇→定格→等玩家松开才真正挥出去，音效
            // 跟动作差出一整个蓄力时长。现在改成绑hit_start，判定框什么时候真正
            // 打开，音效就什么时候响，轻攻击/重攻击/蓄力技/弹幕技统一都准。
            if (_currentSkill != null)
                PlaySkillSE(ResolveSE(_currentSkill.swingSE, _currentModule?.swingSE),
                    ResolveVolume(_currentSkill, _currentModule) * Mathf.Max(0f, _currentSkill.swingSEVolume),
                    transform.position);

            if (_currentSkill != null && _currentSkill.isProjectileSkill)
            {
                FireProjectileSkill(_currentSkill);
            }
            else if (hitbox != null)
            {
                hitbox.Activate(_currentSkill);

                // 空手攻击扬尘：只在没装备武器时出，判定框真正打开(挥出去)那一刻触发，
                // 不用等命中目标——挥空也该有扬尘，跟脚步/闪避扬尘同一套管理器。
                //
                // 生成点用角色根节点朝当前朝向 + 固定高度偏移，不去追踪拳头骨骼——试过
                // 用 hitbox 的 boundingbox/BoneFollower 位置，但不同角色骨架规格不一
                // （有的没有专门的手部骨骼，判定插槽实际绑在大臂上），追踪出来的位置
                // 时准时不准。固定偏移虽然不会像真骨骼那样精确跟手挥动，但对所有角色
                // 稳定一致，够用。
                if (IsUnarmed && FootstepVFXManager.Instance != null)
                {
                    float facingSign = animationDriver != null ? -animationDriver.Facing : 1f;
                    Vector3 spawnPosition = transform.position
                        + new Vector3(facingSign * unarmedAttackDustForwardOffset, unarmedAttackDustHeightOffset, 0f);

                    FootstepVFXManager.Instance.SpawnDust(
                        FootstepVFXManager.Instance.UnarmedAttackDustPrefabs,
                        spawnPosition,
                        Quaternion.identity,
                        1f,
                        Mathf.RoundToInt(transform.position.z * 100f));
                }
            }
        }
        else if (e.Data.Name == jumpStayEventKey)
        {
            // 空中攻击悬停结束、开始真正下坠的时机点——独立于hit_end，跟是不是弹幕技
            // 无关，只要之前是这次悬停自己冻的，这里就要解冻，不能只在弹幕分支里做。
            if (_aerialAttackHoverActive)
            {
                movement?.SetJumpVerticalPhysicsFrozen(false);
                _aerialAttackHoverActive = false;
            }
        }
        else if (e.Data.Name == hitEndEventKey)
        {
            // 弹幕技能没有近战判定框可关，命中判定完全交给飞行中的抛射物自己算，这里
            // 不用管——弹幕可能在hit_end之后很久才真正命中（甚至一直飞到存活时间耗尽
            // 都没碰到任何人），"到hit_end还没命中"不能当成"打空"来播whiff音效。
            if (_currentSkill != null && _currentSkill.isProjectileSkill)
                return;

            if (hitbox != null)
            {
                hitbox.Deactivate();
                if (!_hitLandedThisActive && _currentSkill != null)
                    PlaySkillSE(ResolveSE(_currentSkill.whiffSE, _currentModule?.whiffSE), ResolveVolume(_currentSkill, _currentModule), transform.position);
            }
        }
        else if (_currentSkill != null && _currentSkill.isChargeSkill && e.Data.Name == _currentSkill.charge.chargeHoldEventKey)
        {
            trackEntry.TimeScale = 0f;
            _chargeHeldTrackEntry = trackEntry;
            _chargeHeldSkill = _currentSkill;
            _chargeHoldStartTime = Time.time;
            _fullChargeSEPlayed = false;
            // SetChargeMovementAllowed(true) 现在提前到 RequestHeavyAttack 按键那一刻就
            // 授予了（蓄力技从前摇开始就能连贯走路），这里不用再重复设置。
            // 动画冻结之后 Complete 永远不会触发，攻击状态的兜底超时(至少1.5秒)必须
            // 暂停，否则蓄力蓄满很久还按着不放会被这个"安全网"误伤强制取消。
            actionController?.SuspendAttackLockFallback();
            if (debugLogs) Debug.Log($"[ActionModule] charge_hold → 动画定格，等待 ReleaseChargeAttack", this);

            // 蓄力起手扬尘：动画定格蓄力那一刻触发一次，不是冲刺瞬间。
            if (FootstepVFXManager.Instance != null)
            {
                FootstepVFXManager.Instance.SpawnDust(
                    FootstepVFXManager.Instance.ChargeDustPrefabs,
                    transform.position,
                    Quaternion.identity,
                    1f,
                    Mathf.RoundToInt(transform.position.z * 100f));
            }

            // 玩家点得很快、松开键那一刻这一帧还没播到——松开事件已经先空跑过了，
            // 不会再有第二次，这里补一次，不然会卡在定格姿势里出不来。
            if (_chargeReleaseRequestedEarly)
            {
                Debug.Log($"[ActionModule] 诊断：charge_hold触发时发现_chargeReleaseRequestedEarly=true，" +
                          $"立刻补触发释放，时间={Time.time}", this);
                _chargeReleaseRequestedEarly = false;
                ReleaseChargeAttack();
            }
        }
    }

    /// <summary>
    /// 玩家松开蓄力键时调用（由 InputRouter 接蓄力键的"松开"事件）。如果动画确实还
    /// 停在 charge_hold 那一帧（没被闪避/受击等打断），把播放速度恢复，让动画继续
    /// 播完剩下的部分（比如刺出去）。已经被打断的话什么都不做——TrackEntry换掉之后
    /// 旧的引用已经不是当前正在播的动画了，恢复它的TimeScale没有意义。
    /// </summary>
    public void ReleaseChargeAttack()
    {
        if (_chargeHeldTrackEntry == null || _chargeHeldSkill == null)
        {
            // 这个项目有两套并行的输入路径（主输入+兜底按键）都会调用这个方法，不是
            // 每次空跑都代表"玩家真的提前松手了"——只在确实正处于这个蓄力技能的攻击
            // 动画进行中（还没到定格帧）时才记这个标记，不然长按蓄力过程中偶尔被另一
            // 套输入路径空跑触发一次，就会把正常的长按也误判成"提前松手"提前收掉。
            if (_currentSkill != null && _currentSkill.isChargeSkill)
            {
                _chargeReleaseRequestedEarly = true;
                Debug.Log($"[ActionModule] 诊断：ReleaseChargeAttack空跑，标记_chargeReleaseRequestedEarly=true，" +
                          $"时间={Time.time}，_currentSkill={_currentSkill.skillKey}", this);
            }
            return;
        }

        SkillChargeData charge = _chargeHeldSkill.charge;

        _pendingAttackWasFullyCharged = (Time.time - _chargeHoldStartTime) >= charge.fullChargeHoldSeconds;

        float releaseTimeScale = Mathf.Max(0.01f, charge.releaseTimeScale);
        Spine.Animation releasedAnimation = _chargeHeldTrackEntry.Animation;
        float trackTimeAtRelease = _chargeHeldTrackEntry.TrackTime;

        _chargeHeldTrackEntry.TimeScale = releaseTimeScale;
        actionController?.SetChargeMovementAllowed(false);
        // 动画恢复播放了，Complete 很快会正常触发——重新挂上正常的兜底超时，保护
        // 释放/突刺这一段万一 Complete 没触发时依然有安全网。
        actionController?.RearmAttackLockFallback();

        // 松开瞬间朝角色当前朝向冲一段距离——方向取SpineAnimationDriver.Facing
        // (1=面朝左/世界-X，-1=面朝右/世界+X，见该字段注释)，不用玩家当前按住的移动
        // 方向，避免蓄力走位过程中松手方向和视觉朝向对不上。跟闪避是完全独立的两套
        // 机制，攻击判定(hit_start/hit_end)照常由Spine事件驱动，不受冲刺影响。
        //
        // 冲刺时长不能用写死的固定秒数——那样跟动画实际播放时长对不上，之前固定
        // 0.1秒结果冲刺半途就先停了，之后角色站定不动才慢慢播完刺的动作，判定框
        // 如果在动画更靠后触发，判定窗口和冲刺窗口就完全错开了（这正是"停止位移
        // 后才开始做出刺的姿势"的根因）。改成用动画从释放那一刻到播完还剩多久
        // (Duration-TrackTime，按releaseTimeScale折算成真实时间)来决定冲刺要跑
        // 多久，保证冲刺物理位移的窗口覆盖整个刺击动作，判定必然发生在冲刺过程中。
        if (charge.dashOnRelease && movement != null && animationDriver != null)
        {
            Vector2 dashDirection = new Vector2(-animationDriver.Facing, 0f);

            float dashDurationSeconds = charge.dashFallbackDurationSeconds;
            if (releasedAnimation != null)
            {
                float remaining = releasedAnimation.Duration - trackTimeAtRelease;
                if (remaining > 0.01f)
                    dashDurationSeconds = remaining / releaseTimeScale;
            }

            float dashDistance = charge.dashDistance;
            if (charge.dashDistanceScalesWithChargeRatio && charge.fullChargeHoldSeconds > 0.0001f)
            {
                float heldSeconds = Time.time - _chargeHoldStartTime;
                float chargeRatio = Mathf.Clamp01(heldSeconds / charge.fullChargeHoldSeconds);
                dashDistance *= chargeRatio;
            }

            movement.StartChargeDash(dashDirection, dashDistance, dashDurationSeconds);

            // 突刺松手瞬间脚底扬尘：跟蓄力起手那次(charge_hold定格时)是独立的两次触发，
            // 这次卡在真正冲出去的那一帧，复用同一套 chargeDustPrefabs。放大3倍——冲刺
            // 这种大幅位移动作扬尘太小根本看不清。
            if (FootstepVFXManager.Instance != null)
            {
                FootstepVFXManager.Instance.SpawnDust(
                    FootstepVFXManager.Instance.ChargeDustPrefabs,
                    transform.position,
                    Quaternion.identity,
                    3f,
                    Mathf.RoundToInt(transform.position.z * 100f));
            }

            // 冲刺特效：释放这一刻起播，冲刺期间(dashDurationSeconds)由 Update 里
            // UpdateChargeDashVfxFollow 逐帧跟着角色位置画飘带，之后放手让它自然播完。
            if (charge.dashVfx != null)
            {
                // 绑到武器尖端（跟 SkyPrisonSwordSlashTrailController 挥砍特效同一套
                // Point Attachment 锚点：weapon_fx_tip 插槽下的 fx_tip，随当前装备的
                // 武器Skin自动切换位置），找不到就退回骨骼所在的 Transform 兜底。
                _chargeDashVfxAnchorMode = charge.dashVfxAnchor;
                _chargeDashVfxCharacterAnchorOffset = charge.dashVfxCharacterAnchorOffset;
                Vector3 vfxSpawnPosition = ResolveChargeDashVfxWorldPosition();

                // 朝左用默认姿势(identity)，朝右在这基础上加180度。
                Quaternion dashVfxRotation = animationDriver.Facing == -1
                    ? Quaternion.Euler(0f, 0f, 180f)
                    : Quaternion.identity;

                _chargeDashVfxHandle = EffekseerSystem.PlayEffect(charge.dashVfx, vfxSpawnPosition);
                _chargeDashVfxHandle.SetRotation(dashVfxRotation);
                // 跟挥剑特效(SkyPrisonSwordSlashTrailController)同样的坑：这个特效里的
                // 装饰性粒子造型（Back Glow等，带各自不同的局部角度偏移）是不对称的手性
                // 图形，角色镜像朝右时单靠旋转转不出它的镜像版本，必须用负缩放做真正的
                // 几何翻转——跟挥剑特效验证过的一样是Y轴取负（facingMirrorScaleSign）。
                Vector3 dashVfxScale = Vector3.one;
                if (animationDriver.Facing == -1)
                    dashVfxScale = new Vector3(1f, -1f, 1f);
                _chargeDashVfxHandle.SetScale(dashVfxScale);
                // 挥剑特效(SkyPrisonSwordSlashTrailController)会显式把特效渲染层设成"World3D"，
                // 不设的话 Effekseer 会退回默认0号(Default)层——如果摄像机剔除遮罩没勾Default，
                // 位置/旋转全对，画面上还是什么都看不见。这里踩过同样的坑，补上同样的设置。
                int dashVfxLayer = LayerMask.NameToLayer("World3D");
                if (dashVfxLayer < 0)
                    dashVfxLayer = 0;
                _chargeDashVfxHandle.layer = dashVfxLayer;
                _chargeDashVfxUntil = Time.time + dashDurationSeconds;
            }
        }

        if (debugLogs) Debug.Log($"[ActionModule] ReleaseChargeAttack → 动画恢复播放，TimeScale={releaseTimeScale}  蓄满={_pendingAttackWasFullyCharged}", this);
        _chargeHeldTrackEntry = null;
        _chargeHeldSkill = null;
        _chargeReleaseRequestedEarly = false;
    }

    // ── 命中处理 ─────────────────────────────────────────────────────────

    private void HandleHit(UnitCombatHitbox src, Collider other) => ResolveSkillHit(src.ActiveSkill, other);

    /// <summary>由 SkyPrisonSwordQiProjectile 等非近战判定框来源（比如飞行中的弹幕）在
    /// 命中目标时调用——伤害/暴击/防御抗性/属性伤害/硬直击退/血液特效/伤害数字这整套
    /// 结算逻辑跟近战 hitbox 命中完全共用同一份，不用为每种命中来源各写一遍。</summary>
    public void ResolveSkillHit(SkillDefinition skill, Collider other)
    {
        if (debugLogs) Debug.Log($"[HIT] ResolveSkillHit  skill={skill?.skillKey ?? "NULL"}  kb={skill?.knockbackForce ?? -1}  other={other?.name}", this);
        if (skill == null) return;

        UnitCombatHurtbox hurtbox =
            other.GetComponent<UnitCombatHurtbox>()
         ?? other.GetComponentInParent<UnitCombatHurtbox>(true);

        UnitHealthController targetHealth = hurtbox != null
            ? hurtbox.HealthController
            : other.GetComponent<UnitHealthController>()
           ?? other.GetComponentInParent<UnitHealthController>(true);


        if (targetHealth == null) return;
        if (targetHealth.IsDead) return;

        UnitBattleStatRuntime attackerStats = ResolveAttackerStats();
        UnitBattleStatRuntime targetStats = targetHealth.GetComponent<UnitBattleStatRuntime>()
                                         ?? targetHealth.GetComponentInParent<UnitBattleStatRuntime>();
        float atk = ResolveAttackPower(attackerStats);
        if (_pendingAttackWasFullyCharged && skill.isChargeSkill) atk *= skill.charge.fullChargeDamageMultiplier;

        // 暴击判定：正向暴击和负向暴击互斥——先掷正向暴击，没触发再掷负向暴击，
        // 一次攻击最多命中一种，不会同时发生。
        float critMultiplier = ResolveCritMultiplier(attackerStats, out bool isPositiveCrit, out bool isNegativeCrit);

        // 物理伤害：跟属性伤害是分开判定、分开结算的两笔独立伤害（一把武器可以同时
        // 斩击物理 + 灼热 + 电磁 + 冻结，互不影响），暴击倍率对每一笔都统一生效。
        // 防御+物理类型抗性两层叠加：先用防御的渐进衰减公式减一次（K/(K+def)，防御再高
        // 也只会无限逼近0，不会出现直接减成0伤害的情况），再乘上目标对这个物理类型
        // （斩击/打击/冲击）的抗性百分比。
        // 每一笔伤害各自独立浮动 ±10%（不是整次攻击共用一个随机数），手感上更像"每一下
        // 打击力度略有差异"，而不是"这次攻击运气好/差"（那个是暴击的事）。
        float physicalDamage = atk * skill.damageMultiplier * critMultiplier * ResolveDamageVariance();
        physicalDamage *= ResolveDefenseMultiplier(targetStats);
        physicalDamage *= ResolveResistMultiplier(targetStats, skill.damageTypeKey + "Resist");

        // 伤害数字、命中SE、hitLanded 标记必须在 ApplyDamage 之前完成，
        // 否则 ApplyDamage 同步触发 Kill() → ApplyDeathVisual 会把头顶 UI 隐藏，
        // 导致最后一击数字不显示，且 _hitLandedThisActive 未设置时 hit_end 会误判为挥空。
        UnitOverheadHealthBridge healthBridge =
            targetHealth.GetComponent<UnitOverheadHealthBridge>()
         ?? targetHealth.GetComponentInChildren<UnitOverheadHealthBridge>(true);

        healthBridge?.ShowDamageNumberByKey(skill.damageTypeKey, physicalDamage, false, isPositiveCrit, isNegativeCrit);

        _hitLandedThisActive = true;

        // 命中 SE（3D 空间音效，从受击位置发出）
        PlaySkillSE(ResolveSE(skill.hitSE, _currentModule?.hitSE), ResolveVolume(skill, _currentModule), targetHealth.transform.position);

        // 血液飞溅特效：类型和颜色来自被打单位的 UnitDefinition
        {
            UnitBloodVFXType targetBloodType = UnitBloodVFXType.Normal;
            Color targetBloodColor = new Color(0.72f, 0.02f, 0.02f, 1f);
            UnitDefinitionRuntimeBinder targetBinder =
                targetHealth.GetComponent<UnitDefinitionRuntimeBinder>()
             ?? targetHealth.GetComponentInParent<UnitDefinitionRuntimeBinder>(true);
            if (targetBinder?.UnitDefinitionAsset != null)
            {
                targetBloodType  = targetBinder.UnitDefinitionAsset.bloodVFXType;
                targetBloodColor = targetBinder.UnitDefinitionAsset.bloodColor;
            }
            BloodVFXManager.Instance?.SpawnSplash(targetHealth.gameObject, transform.position, targetBloodColor, targetBloodType);
        }

        targetHealth.ApplyDamage(physicalDamage);

        // 属性伤害（斩击/打击物理之外，灼热/电磁/腐蚀/冻结这些）：每一种都独立结算一次伤害、
        // 独立累计一次异常，互不影响——一把武器可以同时挂好几种属性。伤害倍率=0 的属性条目
        // 不造成 HP 伤害，只贡献异常累计（纯赋异常用的属性词条）。
        if (skill.attributeHits != null && skill.attributeHits.Count > 0 && !targetHealth.IsDead)
        {
            UnitAnomalyController targetAnomaly = UnitAnomalyController.EnsureOnRoot(ResolveUnitRoot(targetHealth.gameObject));

            foreach (SkillAttributeHit hit in skill.attributeHits)
            {
                if (hit == null || string.IsNullOrWhiteSpace(hit.attributeKey)) continue;

                // 属性伤害只吃对应属性抗性（灼热/电磁/腐蚀/冻结抗性），不经过防御这一层——
                // 防御是物理概念，属性伤害走自己独立的抗性通道。
                float attributeDamage = atk * hit.attributeDamageMultiplier * critMultiplier * ResolveDamageVariance();
                attributeDamage *= ResolveResistMultiplier(targetStats, hit.attributeKey + "Resist");
                if (attributeDamage > 0f)
                {
                    targetHealth.ApplyDamage(attributeDamage);
                    healthBridge?.ShowDamageNumberByKey(hit.attributeKey, attributeDamage, false, isPositiveCrit, isNegativeCrit);
                }

                // 1 点属性伤害 = 1 点对应异常累计，再叠加这一条词条自己的蓄积倍率。
                float buildupAmount = attributeDamage * hit.anomalyBuildupMultiplier;
                if (buildupAmount > 0f)
                    targetAnomaly?.ApplyAccumulation(hit.attributeKey, buildupAmount, gameObject);
            }
        }

        // 硬直 + 击退（死亡后不施加）
        if (!targetHealth.IsDead)
        {
            UnitActionController targetAction =
                targetHealth.GetComponent<UnitActionController>()
             ?? targetHealth.GetComponentInParent<UnitActionController>();

            UnitMovementController targetMovement =
                targetHealth.GetComponent<UnitMovementController>()
             ?? targetHealth.GetComponentInParent<UnitMovementController>();

            if (skill.stunDuration > 0f)
                targetAction?.ForceHitStun(skill.stunDuration);

            if (skill.knockbackForce > 0f)
            {
                if (targetMovement == null)
                {
                    Debug.LogWarning($"[Knockback] 未找到 UnitMovementController，目标={targetHealth.name}", this);
                }
                else
                {
                    float dirX = Mathf.Sign(targetHealth.transform.position.x - transform.position.x);
                    if (dirX == 0f) dirX = 1f;
                    Vector3 impulse = new Vector3(dirX * skill.knockbackForce, 0f, 0f);
                    if (debugLogs) Debug.Log($"[Knockback] 施加击退 dir={dirX} force={skill.knockbackForce} impulse={impulse} → {targetHealth.name}", this);
                    targetMovement.ApplyKnockback(impulse);
                }
            }
        }

        if (debugLogs)
            Debug.Log($"[ActionModule] 命中 {other.name}  skill={skill.skillKey}  atk={atk:F1}  dmg={physicalDamage:F1}  crit={(isPositiveCrit ? "positive" : isNegativeCrit ? "negative" : "none")}  stun={skill.stunDuration:F2}s  kb={skill.knockbackForce:F1}", this);
    }

    private UnitBattleStatRuntime ResolveAttackerStats()
    {
        return GetComponent<UnitBattleStatRuntime>() ?? GetComponentInParent<UnitBattleStatRuntime>();
    }

    private float ResolveAttackPower(UnitBattleStatRuntime stats)
    {
        return stats != null ? stats.GetFinalValue("atk", 10f) : 10f;
    }

    // BattleParameterDatabase 里 Percentage 类型字段存的是"整数百分比"（100=100%，
    // 150=150%），不是 0~1 的小数——UnitBattleStatRuntime.GetFinalValue 原样返回资产里
    // 存的数字，不会自动换算。任何 Percentage 字段读出来都要在这里统一 /100 再用，
    // 别再犯"直接拿100当1.0用"这种错。
    private const float PercentScale = 100f;

    // 正向暴击和负向暴击互斥：先掷正向暴击，没触发再掷负向暴击，一次攻击最多命中一种。
    // 暴击伤害倍率/负向暴击伤害倍率都是"触发后最终伤害是原伤害的百分之多少"（150% = ×1.5，
    // 70% = ×0.7），不是"在1.0基础上再加/减多少"。
    private float ResolveCritMultiplier(UnitBattleStatRuntime stats, out bool isPositiveCrit, out bool isNegativeCrit)
    {
        isPositiveCrit = false;
        isNegativeCrit = false;
        if (stats == null) return 1f;

        float critRate = stats.GetFinalValue("critRate", 0f) / PercentScale;
        if (critRate > 0f && UnityEngine.Random.value < critRate)
        {
            isPositiveCrit = true;
            float mult = stats.GetFinalValue("critDamageMultiplier", 0f) / PercentScale;
            return Mathf.Max(0f, mult);
        }

        float negativeCritRate = stats.GetFinalValue("negativeCritRate", 0f) / PercentScale;
        if (negativeCritRate > 0f && UnityEngine.Random.value < negativeCritRate)
        {
            isNegativeCrit = true;
            float mult = stats.GetFinalValue("negativeCritDamageMultiplier", 0f) / PercentScale;
            // 负向暴击顾名思义只能削弱伤害，倍率必须封顶在100%（×1.0）——如果玩法/装备
            // 数值算出来超过100%，负向暴击反而变成加伤，跟"负"这个名字自相矛盾。
            return Mathf.Clamp01(mult);
        }

        return 1f;
    }

    // 每一笔伤害独立浮动 ±10%，避免同一个数值每次打出来完全一样、手感发死。
    private const float DamageVarianceRange = 0.1f;

    private static float ResolveDamageVariance()
    {
        return UnityEngine.Random.Range(1f - DamageVarianceRange, 1f + DamageVarianceRange);
    }

    // 防御的渐进衰减公式：K/(K+def)，K 越大同样防御数值带来的减伤效果越弱。
    // 跟"伤害-防御"直接相减不一样，防御再高也只会让倍率无限逼近0，不会出现直接扣成0伤害。
    private const float DefenseMitigationK = 100f;

    private static float ResolveDefenseMultiplier(UnitBattleStatRuntime targetStats)
    {
        if (targetStats == null) return 1f;
        float def = Mathf.Max(0f, targetStats.GetFinalValue("def", 0f));
        return DefenseMitigationK / (DefenseMitigationK + def);
    }

    // 抗性统一是"目标身上某个 xxxResist 百分比字段"，物理类型抗性（slashResist/strikeResist/
    // impactResist）和属性抗性（heatResist/shockResist/corrosionResist/freezeResist）走的是
    // 同一套读取逻辑，只是 key 不同。封顶 90%，不让抗性叠加到 100% 变成免疫。
    private const float MaxResistClamp = 0.9f;

    private static float ResolveResistMultiplier(UnitBattleStatRuntime targetStats, string resistKey)
    {
        if (targetStats == null || string.IsNullOrWhiteSpace(resistKey)) return 1f;
        float resist = targetStats.GetFinalValue(resistKey, 0f) / PercentScale; // 抗性也是 Percentage 字段，同样要 /100
        resist = Mathf.Clamp(resist, 0f, MaxResistClamp);
        return 1f - resist;
    }

    // 命中回调只拿到 targetHealth 所在的 Collider/子物体，异常控制器要挂在单位的根节点上
    // （跟 EnsureOnRoot 的语义一致），不能直接挂在 targetHealth 自己的物体上，否则同一个单位
    // 身上如果有多个 Hurtbox 子物体，会各自建出一份独立的异常状态，累计值对不上。
    private static GameObject ResolveUnitRoot(GameObject fromObject)
    {
        UnitDefinitionRuntimeBinder binder = fromObject.GetComponent<UnitDefinitionRuntimeBinder>()
                                           ?? fromObject.GetComponentInParent<UnitDefinitionRuntimeBinder>(true);
        return binder != null ? binder.gameObject : fromObject;
    }

    private bool TryConsumeSkillLP(SkillDefinition skill)
    {
        if (skill == null || skill.lpCost <= 0f) return true;
        if (_loadRuntime == null) return true;
        return _loadRuntime.TrySpendLoadAction(skill.lpCost, $"Skill:{skill.skillKey}");
    }

    // 技能 SE 非空优先，否则用武器模组级默认
    private static SkillSoundEntry[] ResolveSE(SkillSoundEntry[] skillClips, SkillSoundEntry[] moduleClips)
        => (skillClips != null && skillClips.Length > 0) ? skillClips : moduleClips;

    // 技能有自己的 seVolume(!=1) 则用技能的，否则用模组的
    private static float ResolveVolume(SkillDefinition skill, WeaponCombatModule module)
        => (skill != null && skill.seVolume != 1f) ? skill.seVolume : (module?.seVolume ?? 1f);

    // volumeScale 是技能/模组整体的音量倍率(ResolveVolume，必要时再叠加swingSEVolume这类
    // 分类倍率)；entry.volume 是随机抽中的这一条素材自己的音量倍率，两层各管各的，最终
    // 相乘。
    private static void PlaySkillSE(SkillSoundEntry[] entries, float volumeScale, Vector3 worldPos)
    {
        if (entries == null || entries.Length == 0) return;
        SkillSoundEntry entry = entries[Random.Range(0, entries.Length)];
        if (entry == null || entry.clip == null) return;

        var gs = SkyPrisonAudioGlobalSettings.Instance;
        float vol = (gs != null ? gs.masterVolume * gs.seVolume : 1f) * volumeScale * Mathf.Max(0f, entry.volume);

        // 2026-07-19：不用 AudioSource.PlayClipAtPoint——它默认创建的是3D空间音源、
        // 带距离衰减，而且固定在传进来的worldPos播放，不会跟着角色继续移动。蓄力冲刺
        // 这类瞬间几十米/秒的位移，会让角色（以及有平滑跟随延迟的镜头）在音效播放期间
        // 迅速远离这个固定发声点，触发距离衰减，表现就是"声音变轻"——不是素材音量
        // 的问题。改成手动建一个 spatialBlend=0 的2D音源，音量只取决于上面算出来的
        // vol，不随位置/距离变化，战斗反馈音效不该因为角色跑多快就变轻。
        GameObject go = new GameObject("SkillSE_OneShot");
        go.transform.position = worldPos;
        AudioSource source = go.AddComponent<AudioSource>();
        source.clip = entry.clip;
        source.volume = vol;
        source.spatialBlend = 0f;
        source.Play();
        Object.Destroy(go, entry.clip.length + 0.1f);
    }

    // ── 引用解析 ─────────────────────────────────────────────────────────

    private void ResolveReferences()
    {
        if (_loadRuntime == null)
            _loadRuntime = GetComponentInParent<UnitLoadRuntime>(true)
                        ?? GetComponentInChildren<UnitLoadRuntime>(true);

        if (actionController == null)
            actionController = GetComponent<UnitActionController>()
                            ?? GetComponentInParent<UnitActionController>();

        if (movement == null)
            movement = GetComponent<UnitMovementController>()
                    ?? GetComponentInParent<UnitMovementController>();

        if (animationDriver == null)
            animationDriver = GetComponent<SpineAnimationDriver_Current>()
                            ?? GetComponentInParent<SpineAnimationDriver_Current>()
                            ?? GetComponentInChildren<SpineAnimationDriver_Current>(true);

        if (skeletonAnimation == null)
            skeletonAnimation = GetComponentInChildren<SkeletonAnimation>(true);

        if (hitbox == null)
            hitbox = GetComponentInChildren<UnitCombatHitbox>(true);

        if (hitbox == null)
            hitbox = CreateHitbox();
    }

    private UnitCombatHitbox CreateHitbox()
    {
        var go = new GameObject("CombatHitbox");
        go.transform.SetParent(hitboxParent != null ? hitboxParent : transform, false);

        // Z 轴方向胶囊，覆盖场景深度，XY 面是拳头大小
        var col       = go.AddComponent<CapsuleCollider>();
        col.isTrigger = true;
        col.direction = 2;     // Z 轴
        col.radius    = 0.55f;
        col.height    = 2.5f;

        var h = go.AddComponent<UnitCombatHitbox>();
        h.SetFacingRoot(transform);
        _hitboxWasAutoCreated = true;

        if (debugLogs)
            Debug.Log("[ActionModule] 自动创建 CombatHitbox（Fallback 3D CapsuleCollider）", this);

        return h;
    }
}
