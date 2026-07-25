using System.Collections;
using UnityEngine;
using Spine;
using Spine.Unity;

/// <summary>
/// Action-state-driven Spine animation presenter.
/// V9: Adds RecaptureBaseLocalTransform so runtime visual-foot alignment can become the new stabilized base.
/// V8: Adds small visual transition / hold windows for 3-stage jump animation so Jump_1 -> Jump_4 -> Jump_2 no longer hard-cuts.
///
/// It reads UnitActionController as the single command-state owner.
/// UnitMovementController only provides movement/facing and resolved animation keys.
/// This prevents Idle/Move/Run from overwriting Jump / Dodge / Attack timelines.
/// </summary>
[DefaultExecutionOrder(13000)]
public class SpineAnimationDriver_Current : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UnitActionController actionController;
    [SerializeField] private UnitMovementController movement;
    [SerializeField] private UnitActionModuleRuntime actionModuleRuntime;
    [SerializeField] private Transform spineRoot;
    [SerializeField] private SkeletonAnimation skeletonAnimation;

    [Header("Facing")]
    [SerializeField] private float faceThreshold = 0.15f;
    [SerializeField] private bool faceRightUsesNegativeScaleX = true;

    [Header("Visual Stabilize")]
    [SerializeField] private bool keepSpineRootTransformPosition = true;
    [SerializeField] private bool freezeSkeletonRootBoneTranslation = true;

    [Header("后退行走")]
    [Tooltip("有专属后退动画时填入动画 Key；留空则倒放行走动画。")]
    [SerializeField] private string moveBackAnimation = "";

    [Header("Animation Names / Fallback")]
    [SerializeField] private string idleAnimation = "Idle";
    [SerializeField] private string moveAnimation = "move";
    [SerializeField] private string runAnimation = "move";
    [SerializeField] private string sneakAnimation = "sneak";
    [SerializeField] private string jumpStartAnimation = "Jump_1";
    [SerializeField] private string jumpAirAnimation = "Jump_4";
    [SerializeField] private string jumpLandAnimation = "Jump_2";
    [SerializeField] private string dodgeForwardAnimation = "dodge_forward";
    [SerializeField] private string dodgeBackAnimation = "dodge_back";
    [Tooltip("闪避动画整体播放速度倍率——1=原速，越大越快。锁定窗口会跟着这个倍率" +
        "同步缩短，动画还是会完整播完，只是花的真实时间更短，不会变成播不全。")]
    [SerializeField] private float dodgePlaybackSpeedMultiplier = 1.3f;
    [SerializeField] private string attackAnimation = "attack";
    [SerializeField] private string hitAnimation = "hit";
    [SerializeField] private string deathAnimation = "die";
    [SerializeField] private string blinkAnimation = "Eye";

    [Header("移动动画速度匹配")]
    [Tooltip("只对潜行动画生效——潜行速度被反复调低后跟动画本身的节奏对不上容易看起来\n" +
        "打滑，用这个参考值把播放速度按「目标速度 / 参考速度」缩放配合。走/跑动画\n" +
        "不再套用这个逻辑：奔跑本来没人反馈有问题，之前顺手把跑步也接了上去想图个\n" +
        "统一，结果负重降低跑速的时候动画会跟着莫名其妙变慢，是扩大了改动范围，\n" +
        "已经改回原来的固定原速播放。\n" +
        "2026-07-18：这个值猜不准，只能在Play模式下一边看角色一边试。这个数字越大，\n" +
        "动画播放越慢；越小，播放越快。已知两个实测点：填2.0时播放速度只有0.15倍\n" +
        "(太慢，走出去老远动画都没播完一圈)；填0.3(=当前sneakSpeed，等于不缩放)时\n" +
        "又太快。当前先取中间值0.6试(约0.5倍速)，需要继续在这个基础上微调——找到\n" +
        "合适数字后告诉我，写死进默认值，不然每次改代码触发脚本重新编译，Play模式下\n" +
        "调的值都会被冲掉。0.6(0.5倍速)还是太快，再减半到1.2(0.25倍速)。")]
    [SerializeField] private float sneakAnimationReferenceSpeed = 1.2f;
    [Tooltip("动画速度匹配缩放的上下限——避免负重惩罚把实际速度压得极低时，动画被拉到\n" +
        "肉眼明显不自然的慢动作/定格。\n" +
        "2026-07-21：下限之前是0.35，但上面sneakAnimationReferenceSpeed最后调定的1.2\n" +
        "对应的目标缩放是0.25倍速(sneakSpeed=0.3时，0.3/1.2=0.25)——0.35这个下限比\n" +
        "0.25还高，等于一直把潜行动画速度地板夹到0.35倍速，从来没真的跑到过调好的\n" +
        "0.25倍速，这才是潜行动画看起来偏快的真正原因。下限改成0.2，给0.25留出余量，\n" +
        "同时负重更重、速度更低时也不会立刻被地板值顶住。")]
    [SerializeField] private Vector2 locomotionTimeScaleClamp = new Vector2(0.2f, 1.5f);

    [Header("Blink")]
    [SerializeField] private bool enableBlink = true;
    [SerializeField] private Vector2 blinkIntervalRange = new Vector2(2.5f, 5.0f);

    [Header("Normal Locomotion Source")]
    [Tooltip("动作状态仍由 UnitActionController 管。Normal 下如果 ActionController 没收到输入，但 MovementController / AI 已经让单位移动，则用实际运动兜底播放 Walk / Run。")]
    [SerializeField] private bool allowMovementFallbackWhenActionLocomotionIdle = true;
    [SerializeField] private float movementFallbackInputThreshold = 0.0001f;
    [SerializeField] private float movementFallbackVelocityThreshold = 0.015f;

    [Header("Jump Visual Transition")]
    [Tooltip("只平滑跳跃动画显示层，不改跳跃物理、不改输入、不改 UnitMovementController 状态。")]
    [SerializeField] private bool enableJumpVisualTransition = true;
    [Tooltip("起跳动画至少保留这一小段时间，避免刚起跳就被 Air 硬切。建议 0.08 ~ 0.14。")]
    [SerializeField] private float jumpStartMinHoldSeconds = 0.10f;
    [Tooltip("落地动画至少保留这一小段时间，避免落地瞬间被 Idle/Move 抢走。建议 0.06 ~ 0.12。")]
    [SerializeField] private float jumpLandMinHoldSeconds = 0.08f;
    [Tooltip("普通身体动画切换混合时间——站立/走/跑这些日常切换也是走这个值，" +
        "原来0.03秒近乎瞬切，站到跑、跑到站会显得很突然。调到0.1秒左右，" +
        "让日常移动切换也有个短暂的过渡，但不要调太大，否则操作会显得迟钝。")]
    [SerializeField] private float bodyDefaultMixDuration = 0.1f;
    [SerializeField] private float jumpStartToAirMixDuration = 0.08f;
    [SerializeField] private float jumpAirToLandMixDuration = 0.06f;
    [SerializeField] private float jumpLandToNormalMixDuration = 0.08f;
    [Tooltip("闪避动画切回正常状态的混合时间。之前跟普通切换共用0.03秒的默认值，" +
        "闪避锁定窗口一到就几乎硬切，看起来姿势僵硬地被拉回站立——调大一点让这个" +
        "过渡更柔和。")]
    [SerializeField] private float dodgeToNormalMixDuration = 0.12f;
    [Tooltip("攻击动画切回正常状态的混合时间。同样之前共用0.03秒默认值，攻击一结束就" +
        "近乎硬切回站立姿势——武器越重/挥砍幅度越大（比如大剑）越明显。调大一点让收招" +
        "更柔和。")]
    [SerializeField] private float attackToNormalMixDuration = 0.12f;

    // 上一次看到的 UnitActionModuleRuntime.AttackRequestSequence——连招里如果两下刚好
    // 用了同一个动画名字，PlayByKey"同名不重复播放"这个给站立/走路设计的优化会误伤
    // 攻击，音效/连段逻辑正常但动画卡在原地不重播。序号变了就说明是全新的一次攻击
    // 请求，强制这次调用带 force=true，不管动画名字是否重复都从头重新播放。
    private int _lastSeenAttackRequestSequence = -1;

    // 重攻击(蓄力)专用的"上半身"叠加轨道。Track0是唯一的"身体轨道"，站立/走/跑/闪避/
    // 受击/死亡这些状态本来就要全身一起变，继续独占Track0；但重攻击蓄力期间用户明确
    // 要求允许走路——如果蓄力动画还是霸占Track0，走路时下半身会跟着一起定格。改成
    // 重攻击动画整个播在这条独立的高优先级轨道上，Track0则全程按真实移动状态继续播放
    // 站立/走路，两轨叠加：只要蓄力动画本身只给上半身骨骼打关键帧、不碰腿/胯部，腿部
    // 就会自然透出Track0此刻真正在播的站立/走路姿势。这要求Spine里的重攻击动画本身
    // 不要给腿部打关键帧——这部分需要在Spine编辑器里配合调整，代码这边没法保证。
    private const int HeavyAttackOverlayTrack = 2;
    private bool _heavyAttackOverlayActive = false;
    private string _lastHeavyOverlayAnimation = "";

    [Header("Runtime Debug")]
    [SerializeField] private string currentBodyAnimation = "";
    [SerializeField] private bool lastBodyAnimationWasAttack = false;
    [SerializeField] private string lastRequestedAnimation = "";
    [SerializeField] private bool lastAnimationFound = false;
    [SerializeField] private UnitActionController.UnitLocomotionMode lastResolvedLocomotion = UnitActionController.UnitLocomotionMode.Idle;
    [SerializeField] private string lastLocomotionSource = "";
    [SerializeField] private string lastActionAnimationSource = "";
    [SerializeField] private string lastMovementJumpRuntimeState = "None";
    [SerializeField] private string currentJumpVisualPhase = "None";
    [SerializeField] private float currentJumpVisualHoldRemaining = 0f;

    private enum JumpVisualPhase
    {
        None = 0,
        Start = 1,
        Air = 2,
        Land = 3,
    }

    private JumpVisualPhase jumpVisualPhase = JumpVisualPhase.None;
    private float jumpVisualHoldUntil = 0f;

    private int facing = 1; // 1 = left, -1 = right in current project convention.

    /// <summary>当前实际视觉朝向：1=面朝左（世界-X），-1=面朝右（世界+X）。
    /// 这是唯一的朝向真值来源——UnitCombatHitbox 镜像攻击判定框时应该读这个，
    /// 不要自己按 FacingInput/localScale 另猜一套符号约定，容易跟这里的翻转对不上。</summary>
    public int Facing => facing;

    // 攻击取消后撤步这类"位移方向和朝向必须彻底解耦"的场景，靠拼一个特定符号的
    // Vector2 喂给 UnitMovementController.SetFacingOverride 反复踩坑(override 会经过
    // UpdateFacing 的阈值判断重新解释，跟"直接保持原样"不是一回事，两次实测都不稳定)。
    // 与其继续猜符号，这里直接加一个"冻结"开关——冻结期间 UpdateFacing 整个跳过，
    // facing 变量保持原值不变，不经过任何符号换算，绝对不会转向。
    //
    // 2026-07-21：光冻结 facing 变量不够——实测发现开始冻结那一刻 spineRoot.localScale
    // 可能跟当前 facing 值暂时不同步(比如攻击后摇期间还有残留输入，facing已经变了但
    // ApplyFacing还没来得及在同一帧追上)，冻结只是"不再更新"，不会主动把已经不同步的
    // scale 纠正回来，导致冻结期间一直卡在错的镜像状态上。开始冻结时必须显式调用一次
    // ApplyFacing()，确保 scale 先跟当前 facing 对齐，再冻结住，否则"冻住"的可能是错的。
    private bool facingHoldActive = false;
    public void SetFacingHold(bool hold)
    {
        facingHoldActive = hold;
        if (hold)
            ApplyFacing();
    }

    // 2026-07-21（第三次）：解除冻结这一刻，movement.FacingInput 的速度兜底经常还没
    // 真正降到0(角色物理减速不是瞬间的)，一解冻 UpdateFacing 马上就会按这个还没降完的
    // 残留速度把朝向转走——等速度彻底归零再解冻在某些情况下等不到(比如没有主动做归零
    // 的减速)。既然调用方明确知道"这段时间朝向应该是多少"（一直没变过），与其继续跟
    // 速度判定较劲，不如解冻的同时直接把这个已知的正确值摆正一次，不走阈值判断这条路
    // （不会有任何"猜"的成分）——后续如果玩家给了新的真实输入，正常的 UpdateFacing 逻辑
    // 自然会在下一帧接管，跟这里无关。
    public void ForceFacing(int value)
    {
        facing = value;
        ApplyFacing();
    }

    private Vector3 spineBaseLocalScale = Vector3.one;
    private Vector3 spineBaseLocalPosition = Vector3.zero;
    private Coroutine blinkCoroutine;

    private void Awake()
    {
        AutoResolveReferences();
    }

    private void OnEnable()
    {
        AutoResolveReferences();
    }

    private void Start()
    {
        if (skeletonAnimation == null)
            return;

        skeletonAnimation.Initialize(false);

        PlayByKey(idleAnimation, true, force: true);

        if (enableBlink && HasAnimation(blinkAnimation))
            blinkCoroutine = StartCoroutine(BlinkLoop());

        ApplyFacing();
    }

    private void OnDisable()
    {
        if (blinkCoroutine != null)
        {
            StopCoroutine(blinkCoroutine);
            blinkCoroutine = null;
        }
    }

    private void Update()
    {
        if (movement == null || actionController == null || skeletonAnimation == null)
            AutoResolveReferences();

        if (skeletonAnimation == null)
            return;

        UpdateFacing();
        UpdateBodyAnimationFromActionState();
    }

    private void LateUpdate()
    {
        StabilizeSkeletonRoot();
    }

    public void SetMovementController(UnitMovementController controller)
    {
        movement = controller;
    }

    public void SetActionController(UnitActionController controller)
    {
        actionController = controller;
    }

    /// <summary>
    /// Re-captures the transform values that LateUpdate keeps stable.
    /// Call this after runtime systems intentionally correct VisualRoot / SpineRoot offsets.
    /// </summary>
    public void RecaptureBaseLocalTransform(string reason = "Runtime")
    {
        AutoResolveReferences();

        if (spineRoot != null)
        {
            spineBaseLocalScale = spineRoot.localScale;
            spineBaseLocalPosition = spineRoot.localPosition;
        }
    }

    public void ApplyAnimationDefinition(UnitAnimationKeySet animationKeys)
    {
        if (animationKeys == null)
            return;

        idleAnimation = NonEmpty(animationKeys.idleKey, idleAnimation);
        moveAnimation = NonEmpty(animationKeys.walkKey, moveAnimation);
        runAnimation = NonEmpty(animationKeys.runKey, runAnimation);
        sneakAnimation = NonEmpty(animationKeys.sneakKey, sneakAnimation);
        jumpStartAnimation = NonEmpty(animationKeys.jumpStartKey, NonEmpty(animationKeys.jumpKey, jumpStartAnimation));
        jumpAirAnimation = NonEmpty(animationKeys.jumpAirKey, NonEmpty(animationKeys.jumpKey, jumpAirAnimation));
        jumpLandAnimation = NonEmpty(animationKeys.jumpLandKey, NonEmpty(animationKeys.jumpKey, jumpLandAnimation));
        dodgeForwardAnimation = NonEmpty(animationKeys.dodgeForwardKey, NonEmpty(animationKeys.dodgeKey, dodgeForwardAnimation));
        dodgeBackAnimation = NonEmpty(animationKeys.dodgeBackKey, NonEmpty(animationKeys.dodgeKey, dodgeBackAnimation));
        attackAnimation = NonEmpty(animationKeys.attackKey, attackAnimation);
        hitAnimation = NonEmpty(animationKeys.hitKey, hitAnimation);
        deathAnimation = NonEmpty(animationKeys.deathKey, deathAnimation);

        currentBodyAnimation = string.Empty;
        if (isActiveAndEnabled && skeletonAnimation != null && skeletonAnimation.AnimationState != null)
            UpdateBodyAnimationFromActionState(force: true);
    }

    public void AutoResolveReferences()
    {
        if (actionController == null)
            actionController = GetComponent<UnitActionController>() ?? GetComponentInParent<UnitActionController>() ?? GetComponentInChildren<UnitActionController>(true);

        if (movement == null)
            movement = GetComponent<UnitMovementController>() ?? GetComponentInParent<UnitMovementController>() ?? GetComponentInChildren<UnitMovementController>(true);

        if (actionModuleRuntime == null)
            actionModuleRuntime = GetComponent<UnitActionModuleRuntime>() ?? GetComponentInParent<UnitActionModuleRuntime>() ?? GetComponentInChildren<UnitActionModuleRuntime>(true);

        if (skeletonAnimation == null)
            skeletonAnimation = FindPreferredSkeletonAnimation();

        if (spineRoot == null)
        {
            Transform namedChild = FindDeepChild(transform, "Spine GameObject (Axia_base)");
            if (namedChild != null)
                spineRoot = namedChild;
        }

        if (spineRoot == null && skeletonAnimation != null)
            spineRoot = skeletonAnimation.transform;

        if (spineRoot != null)
        {
            spineBaseLocalScale = spineRoot.localScale;
            spineBaseLocalPosition = spineRoot.localPosition;
        }

        if (movement != null)
            movement.SetActionControllerOwnsAnimation(true);
    }

    private SkeletonAnimation FindPreferredSkeletonAnimation()
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

            string path = GetTransformPath(candidate.transform).ToLowerInvariant();
            int score = 0;
            if (path.Contains("visualroot")) score += 100;
            if (path.Contains("spineroot")) score += 80;
            if (path.Contains("spine gameobject")) score += 80;
            if (path.Contains("outline") || path.Contains("proxy") || path.Contains("shadow") || path.Contains("occlusion")) score -= 1000;

            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best != null ? best : all[0];
    }

    // ── 后退检测 ──────────────────────────────────────────────────────────────

    private TrackEntry GetTrack0()
    {
        var tracks = skeletonAnimation?.AnimationState?.Tracks;
        if (tracks == null || tracks.Count == 0) return null;
        return tracks.Items[0];
    }

    private void SetTrack0TimeScale(float scale)
    {
        TrackEntry t = GetTrack0();
        if (t != null) t.TimeScale = scale;
    }

    // ── 朝向 ──────────────────────────────────────────────────────────────────

    private void UpdateFacing()
    {
        if (facingHoldActive)
            return;

        if (movement == null)
            return;

        // 闪避全程冻结朝向——不管是前闪还是后闪，闪避这个动作本身应该是"定住朝向冲
        // 一下"，不该在闪避过程中因为玩家还按着方向键就跟着转身（后闪尤其明显：
        // 人往反方向位移，但按的还是反方向的键，不冻结的话会被这个输入直接转过去，
        // 变成"转身之后又原地不动"这种奇怪表现）。闪避开始/结束前后一瞬间的时序竞争
        // 已经在攻击取消后撤步那条专属路径上验证过、有独立的 SetFacingHold 处理，
        // 这里只是给"所有闪避"补一条通用规则，两边不冲突。
        if (movement.IsDodging)
            return;

        Vector2 input = movement.FacingInput;
        if (input.x > faceThreshold && facing != -1)
        {
            facing = -1;
            ApplyFacing();
        }
        else if (input.x < -faceThreshold && facing != 1)
        {
            facing = 1;
            ApplyFacing();
        }
    }

    private void ApplyFacing()
    {
        if (spineRoot == null)
            return;

        Vector3 scale = spineBaseLocalScale;
        float absX = Mathf.Abs(scale.x);
        scale.x = faceRightUsesNegativeScaleX
            ? (facing == -1 ? -absX : absX)
            : (facing == -1 ? absX : -absX);
        spineRoot.localScale = scale;
    }

    private void UpdateBodyAnimationFromActionState(bool force = false)
    {
        // 2026-07-19：改成读isChargeSkill而不是category==HeavyAttack——这是每个技能
        // 自己的蓄力开关，比"重攻击分类"更精确(以后可能有不蓄力的重攻击，或者非重攻击
        // 分类的蓄力技，category不再是可靠的判断依据)。
        bool isHeavyAttack = actionController != null
            && actionController.CurrentState == UnitActionController.UnitActionState.Attack
            && actionModuleRuntime != null && actionModuleRuntime.CurrentSkill != null
            && actionModuleRuntime.CurrentSkill.isChargeSkill;

        if (!force && actionController != null && actionController.CurrentState == UnitActionController.UnitActionState.Attack
            && actionModuleRuntime != null && actionModuleRuntime.AttackRequestSequence != _lastSeenAttackRequestSequence)
        {
            force = true;
        }

        if (actionModuleRuntime != null)
            _lastSeenAttackRequestSequence = actionModuleRuntime.AttackRequestSequence;

        if (isHeavyAttack)
        {
            UpdateHeavyAttackOverlay(force);
            return;
        }

        ClearHeavyAttackOverlayIfNeeded();

        string key;
        bool loop;
        ResolveCurrentAnimation(out key, out loop);
        PlayByKey(key, loop, force);

        // 只在这一帧确实走的是"站立/走/跑/潜行"这条普通地面移动分支时才做速度匹配——
        // 闪避/跳跃/攻击/受击/死亡这些状态各自已经决定好了TimeScale(或者就该是原速)，
        // lastResolvedLocomotion在那些分支里不会被刷新，是上一次地面移动时的旧值，
        // 这时候如果还硬套速度匹配就会用一个过期的移动模式去覆盖别的状态本该有的
        // TimeScale。
        if (lastActionAnimationSource == "NormalLocomotion" || lastActionAnimationSource == "NoActionControllerNormal")
            ApplyLocomotionAnimationSpeedMatch();
    }

    /// <summary>潜行动画播放速度按「实际移动速度 / 参考速度」缩放，让脚步跨距和身体真实
    /// 位移对上，避免潜行速度被调得比动画原本设计的慢很多时看起来像打滑。只对潜行生效，
    /// 走/跑固定原速播放。</summary>
    private void ApplyLocomotionAnimationSpeedMatch()
    {
        if (movement == null)
            return;

        // 只对潜行生效——走/跑不套用这套速度匹配，见上面字段注释。
        if (lastResolvedLocomotion != UnitActionController.UnitLocomotionMode.Sneak)
        {
            SetTrack0TimeScale(1f);
            return;
        }

        float referenceSpeed = sneakAnimationReferenceSpeed;
        float targetSpeed = movement.CurrentEffectiveSneakSpeed;

        if (referenceSpeed <= 0.01f || targetSpeed <= 0.01f)
        {
            SetTrack0TimeScale(1f);
            return;
        }

        // 用目标(稳定)速度而不是CurrentVelocity的瞬时值——见上面CurrentEffectiveSneakSpeed
        // 的注释，瞬时值在起步/急停的过渡帧会先经过接近0，拿去缩放会让动画跟着抽一下。
        float scale = Mathf.Clamp(targetSpeed / referenceSpeed, locomotionTimeScaleClamp.x, locomotionTimeScaleClamp.y);
        SetTrack0TimeScale(scale);
    }

    /// <summary>重攻击(蓄力)期间：Track0继续按真实移动状态播放站立/走路(腿部来源)，
    /// 重攻击动画本身播在独立的HeavyAttackOverlayTrack上(上半身来源)。</summary>
    private void UpdateHeavyAttackOverlay(bool force)
    {
        string locomotionKey;
        bool locomotionLoop;
        ResolveNormalLocomotion(out locomotionKey, out locomotionLoop);
        PlayByKey(locomotionKey, locomotionLoop, force: false);
        ApplyLocomotionAnimationSpeedMatch();

        string skillKey = actionModuleRuntime?.CurrentSkill?.spineAnimationKey;
        string overlayKey = !string.IsNullOrWhiteSpace(skillKey) ? skillKey : attackAnimation;

        // 2026-07-19：正处于蓄力定格、等玩家松手期间，不能重新播放这条轨道——重新播放
        // 会生成一个全新的、没被冻结的TrackEntry，直接盖掉charge_hold刚冻结的定格状态，
        // 动画就从头正常播完了，完全绕开冻结（"连续攻击几次后蓄力失灵，还没扣费就打
        // 出去了"这个bug的根因：force在定格期间被某次残留的攻击请求重新置true，
        // needsRestart判断没考虑"已经在蓄力定格中"这个例外）。同一个技能定格中就直接
        // 跳过，不重新SetAnimation。
        if (actionModuleRuntime != null && actionModuleRuntime.IsChargeHeld
            && IsSameAnimationName(_lastHeavyOverlayAnimation, overlayKey))
            return;

        bool needsRestart = force || !_heavyAttackOverlayActive || !IsSameAnimationName(_lastHeavyOverlayAnimation, overlayKey);
        if (!needsRestart || !HasAnimation(overlayKey))
            return;

        TrackEntry entry = skeletonAnimation.AnimationState.SetAnimation(HeavyAttackOverlayTrack, overlayKey, false);
        if (entry != null)
            entry.Complete += _ => actionController.NotifyAttackAnimationComplete();

        _lastHeavyOverlayAnimation = overlayKey;
        _heavyAttackOverlayActive = true;
    }

    private void ClearHeavyAttackOverlayIfNeeded()
    {
        if (!_heavyAttackOverlayActive)
            return;

        _heavyAttackOverlayActive = false;
        _lastHeavyOverlayAnimation = "";

        if (skeletonAnimation != null && skeletonAnimation.AnimationState != null)
            skeletonAnimation.AnimationState.SetEmptyAnimation(HeavyAttackOverlayTrack, Mathf.Max(0f, attackToNormalMixDuration));
    }

    private void ResolveCurrentAnimation(out string key, out bool loop)
    {
        key = idleAnimation;
        loop = true;

        // 空中攻击(AttackRequestKind.Aerial)是故意在 movement.IsJumping 依然为true的
        // 情况下进入Attack状态的——跳跃的重力/下落物理完全不受影响，只是动画层面要显示
        // 攻击动作而不是跳跃动作。下面的 movement.IsJumping 提前return如果不加这个
        // 特判，会让空中攻击的技能动画永远播不出来（一直显示跳跃动画），是这类"攻击态
        // 和跳跃/闪避物理状态同时成立"场景的通用坑，闪避接突刺/攻击取消后撤步能正常
        // 显示动画正是因为它们会先取消掉 movement 那边的对应状态，空中攻击不会。
        bool isAerialAttackInProgress = actionController != null
            && actionController.CurrentState == UnitActionController.UnitActionState.Attack
            && actionController.CurrentAttackKind == UnitActionController.AttackRequestKind.Aerial;

        // MovementController is the physical/runtime action executor.
        // Read it first so AI / buffered input / same-frame jump state can drive animation even
        // before UnitActionController has refreshed its debug state.
        if (movement != null && !isAerialAttackInProgress)
        {
            if (movement.IsDodging)
            {
                if (movement.CurrentDodgeRuntimeState == UnitMovementController.DodgeRuntimeState.Back)
                    key = ResolveMovementKey(UnitActionAnimationSlot.DodgeBack, dodgeBackAnimation);
                else
                    key = ResolveMovementKey(UnitActionAnimationSlot.DodgeForward, dodgeForwardAnimation);
                loop = false;
                lastActionAnimationSource = "MovementRuntimeDodge";
                return;
            }

            if (movement.IsJumping)
            {
                ResolveMovementRuntimeJumpAnimation(out key, out loop);
                lastActionAnimationSource = "MovementRuntimeJump";
                return;
            }

            if (TryResolveHeldJumpLandAnimation(out key, out loop))
            {
                lastActionAnimationSource = "MovementRuntimeJumpLandHold";
                return;
            }
        }

        ClearJumpVisualPhaseIfExpired();

        if (actionController == null)
        {
            ResolveNormalLocomotion(out key, out loop);
            lastActionAnimationSource = "NoActionControllerNormal";
            return;
        }

        switch (actionController.CurrentState)
        {
            case UnitActionController.UnitActionState.Dead:
                key = ResolveMovementKey(UnitActionAnimationSlot.Death, deathAnimation);
                loop = false;
                lastActionAnimationSource = "ActionControllerDead";
                return;

            case UnitActionController.UnitActionState.HitStun:
                key = ResolveMovementKey(UnitActionAnimationSlot.Hit, hitAnimation);
                loop = false;
                lastActionAnimationSource = "ActionControllerHitStun";
                return;

            case UnitActionController.UnitActionState.Attack:
                // The combo skill actually selected by UnitActionModuleRuntime (weapon-specific,
                // e.g. "Attack_Sword") takes priority over the generic attack key - otherwise
                // every weapon plays the same unarmed animation regardless of what was equipped
                // or which combo step is active.
                string skillKey = actionModuleRuntime != null && actionModuleRuntime.CurrentSkill != null
                    ? actionModuleRuntime.CurrentSkill.spineAnimationKey
                    : null;
                key = !string.IsNullOrWhiteSpace(skillKey) ? skillKey : ResolveMovementKey(UnitActionAnimationSlot.Attack, attackAnimation);
                loop = false;
                lastActionAnimationSource = !string.IsNullOrWhiteSpace(skillKey) ? "ActionModuleSkill" : "ActionControllerAttack";
                return;

            // 2026-07-18：原来这里有一个 case Dodge 分支，但 movement.IsDodging 在这个
            // switch之前就已经提前return了（见上面"MovementRuntimeDodge"那段），这个
            // 分支实际永远走不到，是排查半天才确认的死代码，直接删掉，别再留着误导人。

            case UnitActionController.UnitActionState.Jump:
                ResolveActionControllerJumpAnimation(out key, out loop);
                lastActionAnimationSource = "ActionControllerJump";
                return;

            default:
                ResolveNormalLocomotion(out key, out loop);
                lastActionAnimationSource = "NormalLocomotion";
                return;
        }
    }

    private void ResolveMovementRuntimeJumpAnimation(out string key, out bool loop)
    {
        UnitMovementController.JumpRuntimeState runtimeState = movement.CurrentJumpRuntimeState;
#if UNITY_EDITOR
        // 纯 Inspector 调试用——枚举 ToString() 每次调用都新分配一个字符串，这个方法
        // 在跳跃全程每帧都会跑，之前在 Build 里也一直在付出这个分配代价，正是"只有
        // 跳跃时 GC 才明显变多"的一个真实来源。只在编辑器里赋值，Build 里跳过。
        lastMovementJumpRuntimeState = runtimeState.ToString();
#endif

        switch (runtimeState)
        {
            case UnitMovementController.JumpRuntimeState.Land:
                EnterJumpVisualPhase(JumpVisualPhase.Land, jumpLandMinHoldSeconds);
                key = ResolveMovementKey(UnitActionAnimationSlot.JumpLand, jumpLandAnimation);
                loop = false;
                return;

            case UnitMovementController.JumpRuntimeState.Air:
                if (ShouldHoldJumpStart())
                {
                    key = ResolveMovementKey(UnitActionAnimationSlot.JumpStart, jumpStartAnimation);
                    loop = false;
                    return;
                }

                EnterJumpVisualPhase(JumpVisualPhase.Air, 0f);
                key = ResolveMovementKey(UnitActionAnimationSlot.JumpAir, jumpAirAnimation);
                loop = true;
                return;

            default:
                EnterJumpVisualPhase(JumpVisualPhase.Start, jumpStartMinHoldSeconds);
                key = ResolveMovementKey(UnitActionAnimationSlot.JumpStart, jumpStartAnimation);
                loop = false;
                return;
        }
    }

    private void ResolveActionControllerJumpAnimation(out string key, out bool loop)
    {
        switch (actionController.CurrentJumpPhase)
        {
            case UnitActionController.JumpPhase.Land:
                EnterJumpVisualPhase(JumpVisualPhase.Land, jumpLandMinHoldSeconds);
                key = ResolveMovementKey(UnitActionAnimationSlot.JumpLand, jumpLandAnimation);
                loop = false;
                return;

            case UnitActionController.JumpPhase.Air:
                if (ShouldHoldJumpStart())
                {
                    key = ResolveMovementKey(UnitActionAnimationSlot.JumpStart, jumpStartAnimation);
                    loop = false;
                    return;
                }

                EnterJumpVisualPhase(JumpVisualPhase.Air, 0f);
                key = ResolveMovementKey(UnitActionAnimationSlot.JumpAir, jumpAirAnimation);
                loop = true;
                return;

            default:
                EnterJumpVisualPhase(JumpVisualPhase.Start, jumpStartMinHoldSeconds);
                key = ResolveMovementKey(UnitActionAnimationSlot.JumpStart, jumpStartAnimation);
                loop = false;
                return;
        }
    }

    private bool TryResolveHeldJumpLandAnimation(out string key, out bool loop)
    {
        key = idleAnimation;
        loop = true;

        if (!enableJumpVisualTransition)
            return false;

        if (jumpVisualPhase != JumpVisualPhase.Land)
            return false;

        if (Time.time >= jumpVisualHoldUntil)
            return false;

        UpdateJumpVisualDebug();
        key = ResolveMovementKey(UnitActionAnimationSlot.JumpLand, jumpLandAnimation);
        loop = false;
        return true;
    }

    private bool ShouldHoldJumpStart()
    {
        if (!enableJumpVisualTransition)
            return false;

        if (jumpVisualPhase != JumpVisualPhase.Start)
            return false;

        if (Time.time >= jumpVisualHoldUntil)
            return false;

        UpdateJumpVisualDebug();
        return true;
    }

    private void EnterJumpVisualPhase(JumpVisualPhase phase, float holdSeconds)
    {
        if (!enableJumpVisualTransition)
        {
            jumpVisualPhase = phase;
            jumpVisualHoldUntil = 0f;
            UpdateJumpVisualDebug();
            return;
        }

        if (jumpVisualPhase != phase)
        {
            jumpVisualPhase = phase;
            jumpVisualHoldUntil = holdSeconds > 0f ? Time.time + Mathf.Max(0f, holdSeconds) : 0f;
        }
        else if (holdSeconds > 0f && jumpVisualHoldUntil <= 0f)
        {
            jumpVisualHoldUntil = Time.time + Mathf.Max(0f, holdSeconds);
        }

        UpdateJumpVisualDebug();
    }

    private void ClearJumpVisualPhaseIfExpired()
    {
        if (jumpVisualPhase == JumpVisualPhase.None)
        {
            UpdateJumpVisualDebug();
            return;
        }

        if (jumpVisualPhase == JumpVisualPhase.Start || jumpVisualPhase == JumpVisualPhase.Land)
        {
            if (Time.time < jumpVisualHoldUntil)
            {
                UpdateJumpVisualDebug();
                return;
            }
        }

        jumpVisualPhase = JumpVisualPhase.None;
        jumpVisualHoldUntil = 0f;
        UpdateJumpVisualDebug();
    }

    private void UpdateJumpVisualDebug()
    {
#if UNITY_EDITOR
        // 同上——纯 Inspector 调试字段，这个方法在跳跃过程中会被多处（进入/清除
        // 跳跃视觉阶段、判断是否保持起跳/落地帧）反复调用，每次 ToString() 都是一次
        // 跳跃专属的额外分配，Build 里没人看这个字段，直接跳过。
        currentJumpVisualPhase = jumpVisualPhase.ToString();
#endif
        currentJumpVisualHoldRemaining = jumpVisualHoldUntil > 0f ? Mathf.Max(0f, jumpVisualHoldUntil - Time.time) : 0f;
    }

    private void ResolveNormalLocomotion(out string key, out bool loop)
    {
        loop = true;
        UnitActionController.UnitLocomotionMode locomotion = ResolveNormalLocomotionMode();
        lastResolvedLocomotion = locomotion;

        switch (locomotion)
        {
            case UnitActionController.UnitLocomotionMode.Sprint:
                key = ResolveMovementKey(UnitActionAnimationSlot.Run, runAnimation);
                break;
            case UnitActionController.UnitLocomotionMode.Sneak:
                key = ResolveMovementKey(UnitActionAnimationSlot.Sneak, sneakAnimation);
                break;
            case UnitActionController.UnitLocomotionMode.Walk:
                key = ResolveMovementKey(UnitActionAnimationSlot.Walk, moveAnimation);
                break;
            default:
                key = ResolveMovementKey(UnitActionAnimationSlot.Idle, idleAnimation);
                break;
        }
    }

    private UnitActionController.UnitLocomotionMode ResolveNormalLocomotionMode()
    {
        if (actionController != null)
        {
            UnitActionController.UnitLocomotionMode commandLocomotion = actionController.CurrentLocomotion;

            if (commandLocomotion != UnitActionController.UnitLocomotionMode.Idle)
            {
                lastLocomotionSource = "ActionController";
                return commandLocomotion;
            }

            if (!allowMovementFallbackWhenActionLocomotionIdle)
            {
                lastLocomotionSource = "ActionControllerIdle";
                return commandLocomotion;
            }
        }

        UnitActionController.UnitLocomotionMode movementLocomotion = ResolveMovementFallbackLocomotion();
        if (movementLocomotion != UnitActionController.UnitLocomotionMode.Idle)
        {
            lastLocomotionSource = actionController != null ? "MovementFallbackWhenActionIdle" : "MovementFallbackNoActionController";
            return movementLocomotion;
        }

        lastLocomotionSource = actionController != null ? "ActionControllerIdle" : "MovementIdle";
        return UnitActionController.UnitLocomotionMode.Idle;
    }

    private UnitActionController.UnitLocomotionMode ResolveMovementFallbackLocomotion()
    {
        if (movement == null)
            return UnitActionController.UnitLocomotionMode.Idle;

        bool hasInput = movement.MoveInput.sqrMagnitude > Mathf.Max(0f, movementFallbackInputThreshold);
        Vector3 velocity = movement.CurrentVelocity;
        bool hasVelocity = new Vector2(velocity.x, velocity.z).sqrMagnitude > movementFallbackVelocityThreshold * movementFallbackVelocityThreshold;

        if (!hasInput && !hasVelocity && !movement.ShouldPlayMoveAnimation)
            return UnitActionController.UnitLocomotionMode.Idle;

        if (movement.IsSneakHeld)
            return UnitActionController.UnitLocomotionMode.Sneak;
        if (movement.IsRunHeld && movement.CanRun)
            return UnitActionController.UnitLocomotionMode.Sprint;
        return UnitActionController.UnitLocomotionMode.Walk;
    }

    private UnitActionController.UnitLocomotionMode ResolveFallbackLocomotion()
    {
        return ResolveMovementFallbackLocomotion();
    }

    private string ResolveMovementKey(UnitActionAnimationSlot slot, string fallback)
    {
        if (movement != null)
        {
            string key = movement.ResolveActionAnimationKey(slot);
            if (!string.IsNullOrWhiteSpace(key))
                return key;
        }

        return fallback;
    }

    private void PlayByKey(string animationName, bool loop, bool force = false)
    {
        if (string.IsNullOrWhiteSpace(animationName) || skeletonAnimation == null || skeletonAnimation.AnimationState == null)
            return;

        lastRequestedAnimation = animationName;

        if (!force && currentBodyAnimation == animationName)
            return;

        if (!HasAnimation(animationName))
        {
            lastAnimationFound = false;
            return;
        }

        lastAnimationFound = true;
        string previousBodyAnimation = currentBodyAnimation;
        currentBodyAnimation = animationName;
        bool previousBodyAnimationWasAttack = lastBodyAnimationWasAttack;
        bool isAttackNow = !loop && actionController != null && actionController.CurrentState == UnitActionController.UnitActionState.Attack;
        lastBodyAnimationWasAttack = isAttackNow;

        TrackEntry entry = skeletonAnimation.AnimationState.SetAnimation(0, animationName, loop);
        if (entry != null)
        {
            entry.MixDuration = ResolveBodyMixDuration(previousBodyAnimation, animationName, previousBodyAnimationWasAttack, isAttackNow);

            // 攻击动画播完时通知 ActionController 解锁，而不是靠固定秒数。
            if (isAttackNow)
                entry.Complete += _ => actionController.NotifyAttackAnimationComplete();

            // 闪避的位移/无敌窗口时长是 UnitMovementController.dodgeLockSeconds 这个固定
            // 秒数决定的，跟闪避动画片段本身有多长完全无关——如果动画比这个时长长，
            // 状态一到点就会切走，动画播到一半被硬切掉。用户明确要求闪避动画必须完整
            // 播完——所以让锁定窗口按"动画真实时长 / 播放速度倍率"动态延长
            // （ExtendDodgeLockDuration只会延长不会缩短，不影响已经调好的最短位移/
            // 无敌时间）：动画整体播快 dodgePlaybackSpeedMultiplier 倍，锁定窗口跟着
            // 同步缩短，播完需要的真实时间变短了，但依然是100%完整播完，不会播不全。
            //
            // 之前这里判断条件用的是 actionController.CurrentState == Dodge，但
            // ResolveCurrentAnimation() 里真正决定播放闪避动画的分支走的是
            // movement.IsDodging（"MovementRuntimeDodge"，在 ActionController 状态
            // 刷新之前就先响应，本文件顶部注释也说明了这个优先级），这个条件在实际
            // 闪避时几乎不会为true——等于之前几版闪避修复全都没有真正生效过。改成跟
            // ResolveCurrentAnimation 同一个判断依据。
            if (!loop && movement != null && movement.IsDodging && entry.Animation != null)
            {
                float speedMultiplier = Mathf.Max(0.01f, dodgePlaybackSpeedMultiplier);
                entry.TimeScale = speedMultiplier;
                movement.ExtendDodgeLockDuration(entry.Animation.Duration / speedMultiplier);
            }
        }
    }

    private float ResolveBodyMixDuration(string previousAnimation, string nextAnimation, bool previousWasAttack, bool nextIsAttack)
    {
        if (!enableJumpVisualTransition)
            return Mathf.Max(0f, bodyDefaultMixDuration);

        if (IsJumpStartAnimation(previousAnimation) && IsJumpAirAnimation(nextAnimation))
            return Mathf.Max(0f, jumpStartToAirMixDuration);

        if (IsJumpAirAnimation(previousAnimation) && IsJumpLandAnimation(nextAnimation))
            return Mathf.Max(0f, jumpAirToLandMixDuration);

        if (IsJumpLandAnimation(previousAnimation) && !IsAnyJumpAnimation(nextAnimation))
            return Mathf.Max(0f, jumpLandToNormalMixDuration);

        // 闪避锁定窗口一到，UnitActionController.CurrentState 立刻从 Dodge 切回 Normal，
        // 下一帧就会请求切到 idle/move——之前这里跟普通动画切换共用0.03秒默认混合时间，
        // 几乎是硬切，翻滚/位移到一半的姿势直接跳成站立姿势，看起来僵硬突兀。
        if (IsDodgeAnimation(previousAnimation) && !IsDodgeAnimation(nextAnimation))
            return Mathf.Max(0f, dodgeToNormalMixDuration);

        // 攻击动画播完（entry.Complete 通知 ActionController 解锁）之后立刻切回站立/
        // 移动姿势，同样之前共用0.03秒默认混合时间，挥砍收招瞬间硬切——武器越重、
        // 挥砍幅度越大（比如大剑）收招姿势和站立姿势差得越多，硬切就越明显僵硬。
        // 攻击技能名是动态的（每把武器/每段连招不一样），没法像闪避那样按固定动画名
        // 比对，改成直接用 PlayByKey 里记录的"上一次是不是攻击状态"来判断。
        if (previousWasAttack && !nextIsAttack)
            return Mathf.Max(0f, attackToNormalMixDuration);

        return Mathf.Max(0f, bodyDefaultMixDuration);
    }

    private bool IsAnyJumpAnimation(string animationName)
    {
        return IsJumpStartAnimation(animationName) || IsJumpAirAnimation(animationName) || IsJumpLandAnimation(animationName);
    }

    private bool IsDodgeAnimation(string animationName)
    {
        return IsSameAnimationName(animationName, dodgeForwardAnimation) ||
               IsSameAnimationName(animationName, dodgeBackAnimation) ||
               IsSameAnimationName(animationName, ResolveMovementKey(UnitActionAnimationSlot.DodgeForward, dodgeForwardAnimation)) ||
               IsSameAnimationName(animationName, ResolveMovementKey(UnitActionAnimationSlot.DodgeBack, dodgeBackAnimation));
    }

    private bool IsJumpStartAnimation(string animationName)
    {
        return IsSameAnimationName(animationName, jumpStartAnimation) ||
               IsSameAnimationName(animationName, ResolveMovementKey(UnitActionAnimationSlot.JumpStart, jumpStartAnimation));
    }

    private bool IsJumpAirAnimation(string animationName)
    {
        return IsSameAnimationName(animationName, jumpAirAnimation) ||
               IsSameAnimationName(animationName, ResolveMovementKey(UnitActionAnimationSlot.JumpAir, jumpAirAnimation));
    }

    private bool IsJumpLandAnimation(string animationName)
    {
        return IsSameAnimationName(animationName, jumpLandAnimation) ||
               IsSameAnimationName(animationName, ResolveMovementKey(UnitActionAnimationSlot.JumpLand, jumpLandAnimation));
    }

    private static bool IsSameAnimationName(string a, string b)
    {
        return !string.IsNullOrWhiteSpace(a) &&
               !string.IsNullOrWhiteSpace(b) &&
               string.Equals(a.Trim(), b.Trim(), System.StringComparison.OrdinalIgnoreCase);
    }

    private bool HasAnimation(string animationName)
    {
        if (string.IsNullOrEmpty(animationName) || skeletonAnimation == null || skeletonAnimation.Skeleton == null)
            return false;
        return skeletonAnimation.Skeleton.Data.FindAnimation(animationName) != null;
    }

    private void StabilizeSkeletonRoot()
    {
        if (skeletonAnimation == null || skeletonAnimation.Skeleton == null)
            return;

        if (keepSpineRootTransformPosition && spineRoot != null)
            spineRoot.localPosition = spineBaseLocalPosition;

        if (freezeSkeletonRootBoneTranslation)
        {
            Bone rootBone = skeletonAnimation.Skeleton.RootBone;
            if (rootBone != null && rootBone.Data != null)
            {
                float setupX, setupY;
                GetBoneDataSetupPosition(rootBone.Data, out setupX, out setupY);
                rootBone.Pose.X = setupX;
                rootBone.Pose.Y = setupY;
            }
        }
    }

    private IEnumerator BlinkLoop()
    {
        while (true)
        {
            float wait = Random.Range(blinkIntervalRange.x, blinkIntervalRange.y);
            yield return new WaitForSeconds(wait);

            if (skeletonAnimation == null || skeletonAnimation.AnimationState == null || !HasAnimation(blinkAnimation))
                continue;

            if (actionController != null && actionController.IsDead)
                continue;

            TrackEntry blinkEntry = skeletonAnimation.AnimationState.SetAnimation(1, blinkAnimation, false);
            if (blinkEntry != null)
                blinkEntry.MixDuration = 0.03f;
            skeletonAnimation.AnimationState.AddEmptyAnimation(1, 0.05f, 0f);
        }
    }

    private Transform FindDeepChild(Transform root, string targetName)
    {
        if (root == null || string.IsNullOrWhiteSpace(targetName))
            return null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name == targetName)
                return child;

            Transform nested = FindDeepChild(child, targetName);
            if (nested != null)
                return nested;
        }

        return null;
    }

    private static string NonEmpty(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static string GetTransformPath(Transform t)
    {
        if (t == null)
            return string.Empty;
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }

    // 2026-07-15：原来这里是反射按 "X"/"x"/"SetupPose"/"setupPose" 这些字符串名字去探测
    // 字段/属性，每次 LateUpdate、每个 Spine 角色都要跑一遍，属于稳定的GC/CPU开销来源。
    // 而且反射探测本身就找不到东西——Spine运行时里 setup pose 是靠 GetSetupPose() 这个
    // 方法拿到的，不是叫"SetupPose"的字段/属性，反射永远探测不到，x/y 一直停在默认值0，
    // 等于 freezeSkeletonRootBoneTranslation 开着的时候（默认就是开着）根骨骼每帧都被
    // 强制归零，不是真正的 setup pose 偏移——这同时是个功能性 bug。直接用强类型 API：
    // BoneData 继承自 PosedData<BonePose>，GetSetupPose() 返回的 BonePose 有现成的
    // X/Y 属性，不需要反射。
    private static void GetBoneDataSetupPosition(BoneData boneData, out float x, out float y)
    {
        x = 0f;
        y = 0f;
        if (boneData == null)
            return;

        BonePose setupPose = boneData.GetSetupPose();
        if (setupPose == null)
            return;

        x = setupPose.X;
        y = setupPose.Y;
    }
}
