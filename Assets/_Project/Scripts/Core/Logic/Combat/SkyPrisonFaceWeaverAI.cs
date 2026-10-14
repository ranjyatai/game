using System.Collections;
using UnityEngine;

/// <summary>
/// 织面者（フェイスウィーバー）的行为：休眠 → 被惊动后快速追击 → 两招——直线冲刺 / 蜘蛛式跳扑。
/// 现有 AI 词条读不到距离/深度，也做不出"前摇—位移—硬直"的节奏，所以像咏唱者一样单独封装。
///   · 休眠：玩家进入唤醒范围，或自己受到伤害才醒；醒了不会再睡（以后接"声音唤醒"只需调 Wake()）；
///   · 试探（像黑魂的狗）：出招前先绕着玩家游走一小会儿——深度(Z)来回晃、距离忽近忽远，给玩家"它在找机会"的压力；
///     移动分两档：试探用行走，追击/贴身/出招前的接近用奔跑；
///   · 远距离加速：离玩家越远跑得越快（按距离渐进，最多比奔跑再快 farSprintMaxBonusPercent%），近了恢复；
///   · 闪避躲避：玩家在近处发动近战攻击的瞬间，有一定概率朝反方向做一次真正的闪避（走项目的闪避系统，
///     闪避期间有无敌帧，所以是真躲开，不是倒着走），躲开后立刻反击；谨慎的个体更爱躲；有冷却；
///     自己的前摇/硬直期间也能躲，只有冲刺/跳扑飞行途中不能躲；
///   · 活动范围（SkyPrisonEnemyLeash）：被拉离出生点越远，意愿越低（加速变小、试探/冷却变长）；超过硬范围
///     就放弃追击回家（回归期间这里让位），到家后重置成休眠；
///   · Z 字形靠近：离玩家远时，追击/贴身一边往前一边在深度(Z)方向左右摆（带保持时间，不是每帧乱翻），
///     近到 zigzagMinDistance 以内才对齐深度出招——不是直线冲脸，也让它更难被一条直线的子弹打中；
///   · 受击硬直：被打进硬直时，打断它的前摇/试探/闪避，硬直期间不能决策、不能再触发闪避；
///     只有冲刺、跳扑真正飞出去的那一段保持霸体（飞到一半被打断会很怪）；
///   · 跳在半空是最适合冒险攻击的时机：承诺了落点、没法变招——空中受到的伤害 ×airVulnerability；
///     空中累计伤害超过更低的阈值就被「击落」：当场取消水平冲量垂直掉下来，落地不造成伤害，并硬直 airKnockdownSeconds；
///   · 抓蓄力破绽：玩家在蓄力定格(原地蓄力、行动受限)时是弱点期——织面者不再试探，直接出招惩罚；
///   · 躲冲刺：玩家的冲刺/突刺类攻击朝它冲过来时，像躲子弹一样沿深度方向侧闪一下，离开冲刺的直线；
///   · 适应同一招：短时间内被玩家同一个技能连续命中，第二次起伤害递减(最多减到 adaptMinMultiplier)，
///     并且更爱躲这一招；换招就恢复——只惩罚"技能复读"，不惩罚正常连招；
///   · 韧性（破韧）：单发子弹没有硬直，连发也不能把它锁死——短时间内累计受到的伤害超过阈值才会被打断一次，
///     打断后有一段不再被打断的保护期；冲刺/跳扑飞行中保持霸体；
///   · 躲子弹：看真正飞来的子弹——算它会不会擦过自己所在的深度线，会就朝上/下(深度)做一次真闪避离开弹道，
///     所以不能站远了一直按住开火就杀死它；有冷却，不是 100% 躲；
///   · 个体差异：每只出生时随机一次"性格"——有的急躁(试探短、冷却短、爱近身连击)、有的谨慎(多晃一会儿、爱绕后/跳扑)，
///     数值都在基础值上下浮动，所以同一群里不会每只都在做同一件事；
///   · 错峰：几只同时在场时，刚有一只发动攻击后的 staggerGapSeconds 内，其他的会再多游走一会儿，不会齐刷刷同时扑上来；
///   · 跃过头顶：绕后的另一种花样——远处直接起跳，越过玩家头顶落到背后，落地立刻转身出招；
///   · 绕后：试探开始时有一定概率走"绕后"路线——贴着另一条深度(Z)跑过玩家身边，切到玩家背后再出招，
///     不是只在正面晃；
///   · 追击：用奔跑速度贴近玩家，同时把深度(Z)对齐——冲刺是沿 X 的直线，深度不对就冲空；
///   · 冲刺：定住并朝向玩家 → 地面画一条预警线(前摇) → 沿 X 直线冲出去 → 硬直。撞到玩家造成一次伤害；
///   · 跳扑：定住 → 玩家脚下标出落点圈(前摇) → 起跳飞向落点 → 落地范围伤害 → 硬直。落点在起跳时锁定，
///     所以前摇结束后玩家换位置就能躲；有一定概率落到玩家身后（换边），打乱玩家的站位；
///   · 没有手臂，不会拉近；头只是装饰，不做多击破点。
/// 位移走 UnitMovementController.StartChargeDash（不是闪避，没有无敌），伤害走 ResolveSkillHit，
/// 所以防御/装备耐久/异常累积全部沿用现有的一套。占位阶段预警线/圈用代码画。
/// </summary>
[DisallowMultipleComponent]
public class SkyPrisonFaceWeaverAI : MonoBehaviour, ILeashListener, IIncomingHitModifier, IStaggerable
{
    [Header("技能（物理伤害，无属性）")]
    [SerializeField] private SkillDefinition dashSkill;
    [SerializeField] private SkillDefinition pounceSkill;
    [Tooltip("近身快速普通攻击（爪击）。")]
    [SerializeField] private SkillDefinition clawSkill;

    [Header("唤醒")]
    [SerializeField] private float wakeRangeX = 14f;
    [SerializeField] private float wakeRangeZ = 7f;

    [Header("追击")]
    [Tooltip("追击时的深度对齐容差：冲刺要沿 X 打，深度差超过这个值就先挪。")]
    [SerializeField] private float depthTolerance = 0.8f;
    [SerializeField] private float depthSettle = 0.3f;

    [Header("冲刺")]
    [SerializeField] private float dashMinRangeX = 0.5f;
    [SerializeField] private float dashMaxRangeX = 8f;
    [SerializeField] private float dashTelegraphSeconds = 0.4f;
    [SerializeField] private float dashDistance = 9f;
    [SerializeField] private float dashDurationSeconds = 0.3f;
    [SerializeField] private float dashRecoverySeconds = 0.55f;
    [SerializeField] private float dashHitRadius = 1.1f;

    [Header("跳扑")]
    [SerializeField] private float pounceMinRange = 2.5f;
    [SerializeField] private float pounceMaxRange = 11f;
    [SerializeField] private float pounceTelegraphSeconds = 0.5f;
    [SerializeField] private float pounceFlightSeconds = 0.55f;
    [SerializeField] private float pounceRecoverySeconds = 0.8f;
    [SerializeField] private float pounceLandRadius = 2.5f;
    [Tooltip("跳扑预判强度：落点 = 玩家位置 + 玩家实际移动速度 × 剩余前摇与飞行时间 × 这个值。0=不预判(落在你锁定那一刻站的位置)，1=完美预判。读的是玩家真实位移速度，不读按键。")]
    [SerializeField, Range(0f, 1f)] private float pounceLeadStrength = 0.7f;
    [Tooltip("前摇最后这段时间落点冻结不再跟着预判走：玩家看圈、在这个窗口里变向或急停就能躲开。")]
    [SerializeField] private float pounceFreezeSeconds = 0.15f;
    [Tooltip("预判位移上限(米)：避免玩家一路狂奔时圈被拉到离谱的位置。")]
    [SerializeField] private float pounceMaxLead = 4f;

    [Header("试探（绕圈）")]
    [SerializeField] private float stalkMinSeconds = 0.5f;
    [SerializeField] private float stalkMaxSeconds = 1.3f;
    [Tooltip("试探时与玩家保持的 X 距离带：近了后退，远了靠近。")]
    [SerializeField] private float stalkBandMinX = 3.5f;
    [SerializeField] private float stalkBandMaxX = 6.5f;
    [Tooltip("试探时是否奔跑。默认关：试探用「行走」慢慢绕，出招/追击才奔跑，速度反差本身就是压迫感。")]
    [SerializeField] private bool stalkRun = false;
    [Tooltip("绕圈时深度(Z)摆动幅度与速度。")]
    [SerializeField] private float stalkZAmplitude = 2.6f;
    [SerializeField] private float stalkZSpeed = 2.2f;
    [Tooltip("跳扑落在玩家身后(换边)的概率。")]
    [SerializeField, Range(0f, 1f)] private float crossOverChance = 0.45f;

    [Header("跳扑落地判定")]
    [Tooltip("落地判定：角色离起跳前的地面高度小于这个值(米)才算真的落地，再结算伤害和扬尘。")]
    [SerializeField] private float landHeightTolerance = 0.1f;
    [Tooltip("着地后再多等这么久(秒)才结算伤害，让落地姿态先出来，别比画面快。")]
    [SerializeField] private float landDamageDelay = 0.08f;

    [Header("跳扑落地扬尘")]
    [Tooltip("跳扑落地瞬间在落点生成的扬尘/冲击烟预制体（来自 Stylized_Smoke_Dust）。同时生成列表里的每一个，叠出一团够力的尘雾。留空 = 不出。")]
    [SerializeField] private GameObject[] landingDustPrefabs;
    [Tooltip("扬尘整体放大倍数：越大越显得用力。")]
    [SerializeField] private float landingDustScale = 5.5f;
    [Tooltip("扬尘存活时间(秒)，超过后销毁。")]
    [SerializeField] private float landingDustLifetime = 3.5f;

    [Header("预警显示（占位视觉）")]
    [Tooltip("是否显示攻击圈（跳扑落点圈、爪击范围闪圈）。默认关：不画圈，靠前摇动作和声音让玩家读招。")]
    [SerializeField] private bool showAttackRings = false;
    [Tooltip("是否显示冲刺的地面预警线。")]
    [SerializeField] private bool showDashLine = true;

    [Header("远距离加速")]
    [Tooltip("离玩家横向距离超过这个值才开始加速。")]
    [SerializeField] private float farSprintStartX = 9f;
    [Tooltip("达到这个距离时加速到最大。")]
    [SerializeField] private float farSprintFullX = 16f;
    [Tooltip("最大加速幅度（百分比，50 = 比奔跑再快 50%；上限受移动速度倍率钳制在 200%）。")]
    [SerializeField] private float farSprintMaxBonusPercent = 50f;

    [Header("绕后")]
    [Tooltip("每轮试探选择走绕后路线的概率。")]
    [SerializeField, Range(0f, 1f)] private float flankChance = 0.35f;
    [Tooltip("绕后时贴着的深度偏移(米)：离玩家这么远的另一条深度线上跑过去，避免撞进玩家。")]
    [SerializeField] private float flankLaneOffset = 2.2f;
    [Tooltip("越过玩家这么远才算绕到了背后，随即发动出招。")]
    [SerializeField] private float flankPassDistance = 2.5f;
    [Tooltip("选中绕后后，改成「跃过头顶」(起跳越过玩家落到背后) 而不是贴着另一条深度线跑过去的概率。")]
    [SerializeField, Range(0f, 1f)] private float leapOverChance = 0.35f;
    [Tooltip("跃过头顶时落点越过玩家多远(米)。比落地伤害圈大一点，落在背后而不是砸在玩家身上。")]
    [SerializeField] private float leapOverLandBehind = 2.4f;
    [Tooltip("绕后路线最长持续时间(秒)，防止卡住。")]
    [SerializeField] private float flankMaxSeconds = 3f;

    [Header("近身连击（快速普通攻击）")]
    [Tooltip("每轮试探后选择近身连击的概率（否则走冲刺/跳扑）。")]
    [SerializeField, Range(0f, 1f)] private float meleeWeight = 0.45f;
    [SerializeField] private float clawRangeX = 1.7f;
    [SerializeField] private float clawRangeZ = 1.0f;
    [SerializeField] private int clawCombo = 2;
    [SerializeField] private float clawWindupSeconds = 0.14f;
    [SerializeField] private float clawIntervalSeconds = 0.22f;
    [SerializeField] private float clawRecoverySeconds = 0.3f;
    [SerializeField] private float clawReach = 1.3f;
    [SerializeField] private float clawHitRadius = 1.1f;

    [Header("个体差异 / 错峰")]
    [Tooltip("每只出生时的性格浮动幅度：1 = 各项数值在基础值的 ±100% 内摇摆（实际按各项上下限钳制）；0 = 每只完全一样。")]
    [SerializeField, Range(0f, 1f)] private float personalityVariation = 0.5f;
    [Tooltip("同一时间只允许一只发动攻击：别的织面者刚出手后，这段时间内其余的先继续游走。0 = 不错峰。")]
    [SerializeField] private float staggerGapSeconds = 0.7f;
    [Tooltip("醒来后随机的反应延迟(秒，最大值)：避免同时被惊醒的几只同一帧动作。")]
    [SerializeField] private float maxReactionDelay = 0.6f;

    [Header("空中易伤 / 击落（跳扑）")]
    [Tooltip("跳扑滞空期间受到的伤害倍率。>1 = 空中更容易被打伤。")]
    [SerializeField] private float airVulnerability = 1.4f;
    [Tooltip("被击落、摔到地上后的硬直(秒)：这段时间可以放心输出。")]
    [SerializeField] private float airKnockdownSeconds = 0.9f;

    [Header("Z 字形靠近")]
    [Tooltip("离玩家横向距离大于这个值(米)时，靠近途中走 Z 字；近了就对齐深度准备出招。")]
    [SerializeField] private float zigzagMinDistance = 4f;
    [Tooltip("Z 字摆动幅度(米)：相对玩家所在深度线左右各这么远。")]
    [SerializeField] private float zigzagAmplitude = 1.8f;
    [SerializeField] private float zigzagHoldMin = 0.35f;
    [SerializeField] private float zigzagHoldMax = 0.8f;

    [Header("抓蓄力破绽")]
    [Tooltip("玩家蓄力定格时，织面者在这个距离(米)内就立刻出招惩罚，不再试探。")]
    [SerializeField] private float punishChargeRange = 12f;
    [Header("警戒（玩家蓄力时不傻冲，躲出蓄力斩的冲刺线）")]
    [Tooltip("玩家蓄力释放会沿 X 轴冲出去：满蓄力 10 单位 + 判定半径 1 + 身位 ≈ 11.5。横向距离在这个值以内才需要躲线。")]
    [SerializeField] private float waryReach = 12.5f;
    [Tooltip("躲出冲刺线需要的最小 Z 偏差：判定半深 1 + 身位 0.6 + 余量 0.6。冲刺不改 Z，只要偏差够大就打不到。")]
    [SerializeField] private float waryLaneClear = 2.2f;

    [Header("适应同一招（防技能复读）")]
    [Tooltip("统计窗口(秒)：窗口内被同一个技能连续命中才算复读。")]
    [SerializeField] private float adaptWindowSeconds = 8f;
    [Tooltip("复读每多一次，伤害再少这么多(0.18 = 每次少 18%)。")]
    [SerializeField, Range(0f, 0.5f)] private float adaptStep = 0.18f;
    [Tooltip("伤害最低衰减到的倍率。")]
    [SerializeField, Range(0.1f, 1f)] private float adaptMinMultiplier = 0.5f;
    [Tooltip("复读每多一次，躲这一招的概率加这么多。")]
    [SerializeField, Range(0f, 0.5f)] private float adaptEvadeBonus = 0.15f;

    [Header("反制蓄力（玩家蓄力时有概率冲上来打断）")]
    [Tooltip("玩家刚开始蓄力时，每次判定冲上来的概率。")]
    [SerializeField, Range(0f, 1f)] private float chargePunishBaseChance = 0.2f;
    [Tooltip("玩家每多蓄力 1 秒，概率增加多少(按得越久越危险)。")]
    [SerializeField] private float chargePunishRampPerSecond = 0.2f;
    [SerializeField, Range(0f, 1f)] private float chargePunishMaxChance = 0.8f;
    [Tooltip("多久判定一次(秒)。")]
    [SerializeField] private float chargePunishRollInterval = 0.5f;

    [Header("躲冲刺（玩家冲刺/突刺朝它冲来）")]
    [SerializeField] private float chargeDodgeRange = 14f;
    [SerializeField] private float chargeLaneHalfWidth = 1.3f;
    [SerializeField, Range(0f, 1f)] private float chargeDodgeChance = 0.85f;
    [SerializeField] private float chargeDodgeCooldown = 1.2f;

    [Header("躲子弹（横向/深度闪避）")]
    [Tooltip("躲子弹的基础概率（每次判定到有子弹要擦过自己时掷一次，再乘个体性格：谨慎的更爱躲）。")]
    [SerializeField, Range(0f, 1f)] private float bulletDodgeChance = 0.75f;
    [Tooltip("子弹离自己还有这么远(米)以内才反应（太远不躲，太近来不及）。")]
    [SerializeField] private float bulletReactMaxRange = 16f;
    [SerializeField] private float bulletReactMinRange = 2.5f;
    [Tooltip("子弹擦过自己所在深度线的横向偏差小于这个值(米)就算会被打中（含自身体积）。")]
    [SerializeField] private float bulletLaneHalfWidth = 0.9f;
    [Tooltip("两次躲子弹之间的最短间隔(秒)。持续开火时它会隔这么久躲一次。")]
    [SerializeField] private float bulletDodgeCooldown = 0.9f;
    [SerializeField] private float bulletDodgeSpeedScale = 1.3f;
    [SerializeField] private float bulletDodgeDuration = 0.28f;

    [Header("后跳躲避（玩家近战）")]
    [Tooltip("玩家在这个横向距离内发动近战攻击才可能触发躲避。")]
    [SerializeField] private float evadeTriggerRangeX = 3.2f;
    [SerializeField] private float evadeTriggerRangeZ = 1.6f;
    [Tooltip("每次被玩家近战攻击时触发后跳的基础概率（再乘个体性格：谨慎的更爱躲）。")]
    [SerializeField, Range(0f, 1f)] private float evadeChance = 0.5f;
    [Tooltip("闪避速度倍率（相对单位定义里的闪避速度）。")]
    [SerializeField] private float evadeSpeedScale = 1.4f;
    [Tooltip("闪避持续时间(秒)。闪避时长本来由闪避动画长度决定；怪物占位骨架没有闪避动画，所以这里手动指定（位移距离=闪避速度×这个时长，期间无敌）。")]
    [SerializeField] private float evadeDurationSeconds = 0.35f;
    [SerializeField] private float evadeRecoverySeconds = 0.12f;
    [Tooltip("两次躲避之间的最短间隔(秒)。")]
    [SerializeField] private float evadeCooldownSeconds = 3f;

    [Header("节奏")]
    [Tooltip("两招之间的最短间隔(秒)。")]
    [SerializeField] private float cooldownSeconds = 0.5f;
    [Tooltip("追击中跳扑相对冲刺的选择权重（0=全冲刺，1=全跳扑）。")]
    [SerializeField, Range(0f, 1f)] private float pounceWeight = 0.4f;

    private UnitMovementController _movement;
    private UnitActionController _action;
    private UnitHealthController _health;
    private UnitDeathController _death;
    private UnitActionModuleRuntime _module;
    private bool _awake;
    private bool _busy;
    private bool _movingZ;
    private float _nextReadyTime;
    private float _stalkUntil;
    private float _stalkPhase;
    private bool _stalkStarted;
    private float _stalkXCmd;
    private float _stalkZDir = 1f;
    private float _stalkDecideAt;
    private float _chaseXCmd;
    private float _chaseDecideAt;
    private bool _meleeMode;
    private float _zigDir = 1f;
    private float _zigFlipAt;
    private int _lastSeenPlayerAttackSeq = -1;
    private float _nextBulletDodgeTime;
    private bool _wasStunned;
    private bool _wary;
    private float _waryStart;
    private float _nextPunishRoll;
    private float _waryZDir;
    private float _spawnFillUntil = -1f;       // 出生后这段时间内，没挨过打就把当前血量补满到上限
    private bool _hurtSinceSpawn;
    // 适应同一招
    private string _adaptKey;
    private int _adaptCount;
    private float _adaptTime = -999f;
    private int _adaptFrame = -1;
    // 躲冲刺
    private UnitMovementController _playerMove;
    private Vector3 _lastPlayerPos;
    private bool _hasLastPlayerPos;
    private float _nextChargeDodgeTime;
    private bool _inPounceAir;            // 跳扑滞空中（起跳到落地）
    private bool _knockedOutOfAir;        // 空中被击落：落地不造成伤害，改为硬直
    private bool _inFlight;            // 冲刺/跳扑飞行途中：这段不能被闪避打断
    private readonly System.Collections.Generic.List<GameObject> _tempObjs = new System.Collections.Generic.List<GameObject>();
    private float _nextEvadeTime;
    private UnitActionModuleRuntime _playerModule;
    // 性格（出生时随机一次）：全部是相对基础值的倍率
    private float _pAggression = 1f;   // >1 更急躁：试探更短、冷却更短
    private float _pFlank = 1f;        // 绕后倾向
    private float _pMelee = 1f;        // 近身连击倾向
    private float _pPounce = 1f;       // 跳扑倾向
    private float _actAfterTime;       // 醒来后的反应延迟截止时间
    private bool _wasAwakeLastFrame;
    private static float s_lastCommitTime = -999f;
    private bool _flanking;
    private int _lastTactic = -1, _lastTactic2 = -1;       // 自己最近两轮用过的战术（避免连着用同一招）
    private static int s_recentTactic = -1;                  // 同群里最近一次有人选的战术（让几只怪互相换着来）
    private static float s_recentTacticTime = -999f;
    private bool _leapPending;          // 本轮选了「跃过头顶」：试探很短，随后强制起跳越过玩家
    private float _flankSide;
    private float _flankLane = 1f;
    private float _flankEndTime;
    private float _appliedSprintBonus;
    private UnitBattleStatRuntime _stats;
    private Collider _playerCollider;
    private static Material _lineMaterial;

    public void Wake() { _awake = true; }

    // 摆在场景里的单位：移动/行动控制器这些子组件是运行时才创建的，Awake 时多半还没有，
    // 所以引用不能只在 Awake 取一次，要在用到时补取（取到后做一次性初始化）。
    private bool _refsReady;
    private SkyPrisonEnemyLeash _leash;
    private bool _wasReturning;

    // 追击意愿 0~1（拴绳给出）；至少保留 0.25，不至于完全不动
    private float W => _leash != null ? Mathf.Max(0.25f, _leash.Willingness01) : 1f;

    private void EnsureRefs()
    {
        if (_refsReady) return;
        _movement = GetComponentInChildren<UnitMovementController>(true);
        if (_movement == null) return;           // 还没创建好，下一帧再试
        _action = GetComponentInChildren<UnitActionController>(true);
        _health = GetComponentInChildren<UnitHealthController>(true);
        _death = GetComponentInChildren<UnitDeathController>(true);
        _module = GetComponentInChildren<UnitActionModuleRuntime>(true);
        _leash = GetComponent<SkyPrisonEnemyLeash>();
        _movement.ConfigureAsExternalExecutor();
        if (_health != null) _health.OnDamaged += HandleDamaged;
        RollPersonality();
        _spawnFillUntil = Time.time + 1f;
        _refsReady = true;
    }

    private void RollPersonality()
    {
        float v = personalityVariation;
        _pAggression = Mathf.Clamp(1f + Random.Range(-0.5f, 0.5f) * v * 2f, 0.6f, 1.6f);
        _pFlank = Mathf.Clamp(1f + Random.Range(-0.8f, 0.8f) * v * 2f, 0.2f, 2.2f);
        _pMelee = Mathf.Clamp(1f + Random.Range(-0.8f, 0.8f) * v * 2f, 0.3f, 2f);
        _pPounce = Mathf.Clamp(1f + Random.Range(-0.8f, 0.8f) * v * 2f, 0.3f, 2f);
    }

    // 同一时间只放一只出手：刚有别的织面者发动攻击就先别扑，再游走一小会儿
    private bool TryCommitAttack()
    {
        if (staggerGapSeconds > 0f && Time.time - s_lastCommitTime < staggerGapSeconds)
        {
            _stalkUntil = Time.time + Random.Range(0.25f, 0.7f);
            _stalkStarted = true;
            return false;
        }
        s_lastCommitTime = Time.time;
        return true;
    }

    private void Awake() { EnsureRefs(); }

    private void Start() { EnsureRefs(); }

    private void OnDestroy()
    {
        if (_health != null) _health.OnDamaged -= HandleDamaged;
    }

    public void ModifyHit(bool isPhysical, string damageKey, float attackerX, ref float amount)
    {
        if (_inPounceAir) amount *= airVulnerability;
        amount *= AdaptMultiplier();
    }

    private bool PlayerIsChargingNearby(SkyPrisonUnitRuntimeIdentity player, float absDx)
    {
        if (absDx > punishChargeRange) return false;
        if (_playerModule == null) _playerModule = player.GetComponentInChildren<UnitActionModuleRuntime>(true);
        return _playerModule != null && _playerModule.IsChargeHeld;
    }

    private string CurrentPlayerSkillKey()
    {
        var player = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
        if (player == null) return null;
        if (_playerModule == null) _playerModule = player.GetComponentInChildren<UnitActionModuleRuntime>(true);
        var sk = _playerModule != null ? _playerModule.CurrentSkill : null;
        return sk != null ? sk.skillKey : null;
    }

    // 同一个技能连续命中：同一帧内的多次伤害分量(物理+属性)只算一次；窗口过期或换招就重新计数
    private float AdaptMultiplier()
    {
        string key = CurrentPlayerSkillKey();
        if (string.IsNullOrEmpty(key)) return 1f;
        if (key == _adaptKey && Time.time - _adaptTime < adaptWindowSeconds)
        {
            if (Time.frameCount != _adaptFrame) _adaptCount++;
        }
        else { _adaptKey = key; _adaptCount = 1; }
        _adaptTime = Time.time;
        _adaptFrame = Time.frameCount;
        return Mathf.Max(adaptMinMultiplier, 1f - adaptStep * (_adaptCount - 1));
    }

    // 玩家刚复读同一招的次数（用来提高躲这一招的概率）
    private int RecentRepeatCount()
    {
        return Time.time - _adaptTime < adaptWindowSeconds ? Mathf.Max(0, _adaptCount - 1) : 0;
    }

    private void HandleDamaged(UnitHealthController hc, float amount)
    {
        _awake = true; _hurtSinceSpawn = true;
        // 打断判定已交给通用失衡条 SkyPrisonStagger(伤害结算里累积，满了回调 Stagger)
    }

    private void Update()
    {
        EnsureRefs();
        // 单位定义里的血量上限（属性系统）比"当前血量"晚生效：出生时上限已经是 180、当前血量还停在默认的 100，
        // 看上去只有半血。出生后 1 秒内、没挨过打，就把当前血量补满到上限。
        if (_health != null && !_hurtSinceSpawn && Time.time < _spawnFillUntil && _health.CurrentHealth < _health.MaxHealth - 0.01f)
            _health.SetCurrentHealth(_health.MaxHealth);
        if (_movement == null) return;
        if (_death != null && _death.IsDeadLike) { SubmitMove(Vector2.zero, false); return; }

        var player = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
        if (player == null) { SubmitMove(Vector2.zero, false); return; }
        if (_leash != null && _leash.IsReturning)
        {
            if (!_wasReturning) { _wasReturning = true; AbortActions(); }
            return;                                              // 回家路上由拴绳接管移动
        }
        _wasReturning = false;

        // 受击硬直：打断动作、硬直期间什么都不做（飞行中的冲刺/跳扑除外，保持霸体）
        if (_action != null && _action.CurrentState == UnitActionController.UnitActionState.HitStun && !_inFlight)
        {
            if (!_wasStunned) { _wasStunned = true; AbortActions(); SubmitMove(Vector2.zero, false); }
            return;
        }
        _wasStunned = false;

        Vector3 deltaForEvade = player.transform.position - transform.position;
        if (_awake && !_inFlight && TryEvadePlayerMelee(player, deltaForEvade)) return;
        if (_awake && !_inFlight && TryDodgeIncomingBullets(player)) return;
        if (_awake && !_inFlight && TryDodgePlayerCharge(player)) return;
        if (_busy) return;

        Vector3 delta = player.transform.position - transform.position;
        if (!_awake)
        {
            if (Mathf.Abs(delta.x) <= wakeRangeX && Mathf.Abs(delta.z) <= wakeRangeZ) _awake = true;
            else { SubmitMove(Vector2.zero, false); return; }
        }

        if (!_wasAwakeLastFrame) { _wasAwakeLastFrame = true; _actAfterTime = Time.time + Random.Range(0f, maxReactionDelay); }
        if (Time.time < _actAfterTime) { SubmitMove(Vector2.zero, false); return; }

        float dirX = delta.x >= 0f ? 1f : -1f;
        if (Mathf.Abs(delta.x) > 1.0f) _movement.SetFacingOverride(new Vector2(dirX, 0f));

        float absDx = Mathf.Abs(delta.x);
        float absDz = Mathf.Abs(delta.z);
        bool aligned = absDz <= depthTolerance;
        UpdateSprintBoost(absDx);

        // 警戒：玩家在蓄力(大剑等)时不跳扑、不冲刺、不近身，保持距离，太近就后闪，蓄力一松开再恢复出招。
        // (以前是「蓄力就冲上去惩罚」，结果傻傻地跳进大剑的蓄力斩范围里。)
        if (PlayerIsChargingNearby(player, absDx))
        {
            if (_wary && TryPunishCharge(player, dirX, absDx, absDz, aligned)) return;
            WaryBehavior(player, delta, dirX, absDx); return;
        }
        if (_wary) { _wary = false; _stalkStarted = false; _leapPending = false; _flanking = false; _nextReadyTime = Mathf.Max(_nextReadyTime, Time.time + 0.25f); }

        // 招式冷却中也在绕圈游走，不贴在玩家身上干站着
        if (Time.time < _nextReadyTime) { Stalk(player, delta, dirX); return; }

        // 冷却结束后先试探一会儿，再出招（每轮随机时长，节奏不固定）
        if (Time.time >= _nextReadyTime)
        {
            if (!_stalkStarted)
            {
                _stalkStarted = true;
                _stalkUntil = Time.time + Random.Range(stalkMinSeconds, stalkMaxSeconds) / (_pAggression * W);
                _stalkPhase = Random.value * 6.28f;
                _stalkZDir = Random.value < 0.5f ? -1f : 1f;
                _stalkDecideAt = 0f;
                ChooseTactic(delta, player);
            }
            if (Time.time < _stalkUntil) { Stalk(player, delta, dirX); return; }
        }

        if (Time.time >= _nextReadyTime && _meleeMode)
        {
            if (absDx <= clawRangeX && absDz <= clawRangeZ) { if (!TryCommitAttack()) { Stalk(player, delta, dirX); return; } StartCoroutine(ClawRoutine(player, dirX)); return; }
            // 贴上去：X 靠到爪击范围内，Z 对齐
            float mx = absDx > clawRangeX * 0.7f ? dirX : 0f;
            float mz = absDx > zigzagMinDistance ? ZigZagZ(player.transform.position.z)
                     : (absDz > clawRangeZ * 0.5f ? Mathf.Sign(delta.z) : 0f);
            SubmitMove(new Vector2(mx, mz), true);
            return;
        }

        if (Time.time >= _nextReadyTime)
        {
            bool canDash = aligned && absDx >= dashMinRangeX && absDx <= dashMaxRangeX && dashSkill != null;
            bool canPounce = pounceSkill != null && Mathf.Sqrt(absDx * absDx + absDz * absDz) >= pounceMinRange
                             && Mathf.Sqrt(absDx * absDx + absDz * absDz) <= pounceMaxRange;
            if (_leapPending)
            {
                float dist2 = Mathf.Sqrt(absDx * absDx + absDz * absDz);
                if (dist2 >= pounceMinRange && dist2 <= pounceMaxRange)
                {
                    if (!TryCommitAttack()) { Stalk(player, delta, dirX); return; }
                    _leapPending = false;
                    StartCoroutine(PounceRoutine(player, true));
                    return;
                }
                // 还没到起跳距离：先逼近/拉开到范围内
                float lx = dist2 > pounceMaxRange ? dirX : (dist2 < pounceMinRange ? -dirX : 0f);
                SubmitMove(new Vector2(lx, 0f), true);
                return;
            }

            float pw = Mathf.Clamp01(pounceWeight * _pPounce);
            if (canDash && (!canPounce || Random.value >= pw)) { if (!TryCommitAttack()) { Stalk(player, delta, dirX); return; } StartCoroutine(DashRoutine(player, dirX)); return; }
            if (canPounce) { if (!TryCommitAttack()) { Stalk(player, delta, dirX); return; } StartCoroutine(PounceRoutine(player)); return; }
        }

        // 追击：X 一直贴近，Z 偏出容差才挪（带回差）
        if (Time.time >= _chaseDecideAt)
        {
            _chaseDecideAt = Time.time + 0.25f;
            _chaseXCmd = absDx > 2.5f ? dirX : 0f;
        }
        float moveX = _chaseXCmd;
        float moveZ = 0f;
        if (absDx > zigzagMinDistance)
        {
            moveZ = ZigZagZ(player.transform.position.z);                 // 远：边靠近边 Z 字摆动
            _movingZ = false;
        }
        else if (_movingZ ? absDz > depthSettle : absDz > depthTolerance)
        {
            _movingZ = true;
            moveZ = Mathf.Sign(delta.z);
        }
        else _movingZ = false;
        SubmitMove(new Vector2(moveX, moveZ), true);
    }

    // 玩家蓄力释放的冲刺只沿 X 轴、10 单位、0.1~0.3 秒冲完，后闪 / 后撤都来不及。
    // 唯一可靠的躲法是提前躲出那条线：Z 偏差 ≥ waryLaneClear 就根本打不到。
    // 玩家蓄力时不再一律退开：每隔一段时间按概率冲上来(同线冲刺、不同线跳扑)，打断玩家的蓄力(被打会取消蓄力)。
    // 蓄力越久概率越高、急躁的个体更早出手；两只织面者共用错峰(TryCommitAttack)，同一时间只会有一只扑上来。
    private bool TryPunishCharge(SkyPrisonUnitRuntimeIdentity player, float dirX, float absDx, float absDz, bool aligned)
    {
        if (Time.time < _nextReadyTime || Time.time < _nextPunishRoll) return false;
        _nextPunishRoll = Time.time + chargePunishRollInterval;
        float chance = Mathf.Min(chargePunishMaxChance, (chargePunishBaseChance + chargePunishRampPerSecond * (Time.time - _waryStart)) * _pAggression);
        if (Random.value >= chance) return false;
        float dist = Mathf.Sqrt(absDx * absDx + absDz * absDz);
        bool canDash = aligned && absDx >= dashMinRangeX && absDx <= dashMaxRangeX && dashSkill != null;
        bool canPounce = pounceSkill != null && dist >= pounceMinRange && dist <= pounceMaxRange;
        if (!canDash && !canPounce) return false;
        if (!TryCommitAttack()) return false;
        _wary = false;
        if (canDash) StartCoroutine(DashRoutine(player, dirX));
        else StartCoroutine(PounceRoutine(player));
        return true;
    }

    private void WaryBehavior(SkyPrisonUnitRuntimeIdentity player, Vector3 delta, float dirX, float absDx)
    {
        if (!_wary) { _wary = true; _waryStart = Time.time; _nextPunishRoll = Time.time + chargePunishRollInterval; _leapPending = false; _flanking = false; _meleeMode = false; _stalkStarted = false; _waryZDir = 0f; }
        float dz = transform.position.z - player.transform.position.z;     // >0：织面者在玩家的 +Z 侧
        float adz = Mathf.Abs(dz);
        if (absDx <= waryReach && adz < waryLaneClear)
        {
            // 在冲刺线上：往偏差更大的一侧侧移(正好在线上就随机选一侧并记住，不来回抖)
            if (_waryZDir == 0f || Mathf.Sign(dz) == -_waryZDir && adz > 0.4f) _waryZDir = adz < 0.3f ? (Random.value < 0.5f ? -1f : 1f) : Mathf.Sign(dz);
            SubmitMove(new Vector2(0f, _waryZDir), true);
            return;
        }
        // 已经在线外：原地警戒，只在线外小幅游走(方向固定为远离线的一侧)，始终面朝玩家
        _waryZDir = 0f;
        float side = dz >= 0f ? 1f : -1f;
        float strafe = adz < waryLaneClear + 0.8f ? side * 0.3f : 0f;
        SubmitMove(new Vector2(0f, strafe), false);
    }

    // Z 字：每隔 0.35~0.8 秒反向一次，或碰到幅度边界立刻反向；不能每帧按位置重算（跑得快会高速抖）
    private float ZigZagZ(float playerZ)
    {
        float relZ = transform.position.z - playerZ;
        if (Time.time >= _zigFlipAt)
        {
            _zigDir = -_zigDir;
            _zigFlipAt = Time.time + Random.Range(zigzagHoldMin, zigzagHoldMax);
        }
        if (_zigDir > 0f && relZ > zigzagAmplitude) { _zigDir = -1f; _zigFlipAt = Time.time + Random.Range(zigzagHoldMin, zigzagHoldMax); }
        else if (_zigDir < 0f && relZ < -zigzagAmplitude) { _zigDir = 1f; _zigFlipAt = Time.time + Random.Range(zigzagHoldMin, zigzagHoldMax); }
        return _zigDir;
    }

    // 试探：X 保持在带内（近了退、远了进），Z 在玩家两侧来回绕。
    // 方向每隔一小段才重新判断、Z 方向走到幅度边界才反向——不能每帧按位置重算，
    // 它跑得快，一帧就会冲过目标点，每帧反向就是高速抖动。
    private void Stalk(SkyPrisonUnitRuntimeIdentity player, Vector3 delta, float dirX)
    {
        if (_flanking && FlankStep(player)) return;
        float absDx = Mathf.Abs(delta.x);
        if (Time.time >= _stalkDecideAt)
        {
            _stalkDecideAt = Time.time + 0.4f;
            if (absDx > stalkBandMaxX) _stalkXCmd = dirX;
            else if (absDx < stalkBandMinX) _stalkXCmd = -dirX;
            else _stalkXCmd = 0f;
        }

        float relZ = transform.position.z - player.transform.position.z;
        if (_stalkZDir > 0f && relZ > stalkZAmplitude) _stalkZDir = -1f;
        else if (_stalkZDir < 0f && relZ < -stalkZAmplitude) _stalkZDir = 1f;

        SubmitMove(new Vector2(_stalkXCmd, _stalkZDir), stalkRun);   // 试探：行走（stalkRun 勾上才跑）
    }

    // ── 近身连击 ─────────────────────────────────────────────

    private IEnumerator ClawRoutine(SkyPrisonUnitRuntimeIdentity player, float dirX)
    {
        _busy = true;
        _inFlight = true;
        SubmitMove(Vector2.zero, false);
        _movement.SetFacingOverride(new Vector2(dirX, 0f));

        for (int i = 0; i < Mathf.Max(1, clawCombo); i++)
        {
            // 极短前摇：只是一个"缩一下"的信号，玩家看清得靠身体动作（Spine 做好后换成真动作）
            yield return new WaitForSeconds(clawWindupSeconds);
            if (Dead) { _busy = false; yield break; }

            Vector3 center = transform.position + new Vector3(dirX * clawReach, 0f, 0f);
            TryHitPlayer(player, clawSkill, clawHitRadius, center);
            ShowClawFlash(center);

            if (i < clawCombo - 1) yield return new WaitForSeconds(clawIntervalSeconds);
        }

        _inFlight = false;
        yield return new WaitForSeconds(clawRecoverySeconds);
        _nextReadyTime = Time.time + 0.25f;
        _stalkStarted = false;
        _busy = false;
    }

    // 跳扑落地：在实际落点叠几层扬尘/冲击烟，放大一点，砸地要有力气感
    private void SpawnLandingDust()
    {
        if (landingDustPrefabs == null || landingDustPrefabs.Length == 0) return;
        var vfx = FootstepVFXManager.Instance;
        if (vfx == null) return;
        Vector3 pos = transform.position;
        int sorting = Mathf.RoundToInt(pos.z * 100f);
        for (int i = 0; i < landingDustPrefabs.Length; i++)
        {
            if (landingDustPrefabs[i] == null) continue;
            vfx.SpawnDust(new[] { landingDustPrefabs[i] }, pos, Quaternion.identity, landingDustScale, sorting, landingDustLifetime);
        }
    }

    // 占位：爪击瞬间在落点画一个短暂的红圈
    private void ShowClawFlash(Vector3 center)
    {
        var ring = CreateRing(center, clawHitRadius);
        SetLineAlpha(ring, 0.8f);
        Destroy(ring.gameObject, 0.1f);
    }

    // 绕后：沿"另一条深度线"一路跑过玩家，越过 flankPassDistance 后算绕到背后，立刻结束试探去出招。
    // 返回 true = 这一帧已经处理了移动；false = 绕后结束，交还给普通试探。
    private bool FlankStep(SkyPrisonUnitRuntimeIdentity player)
    {
        Vector3 pp = player.transform.position;
        float relX = transform.position.x - pp.x;
        bool crossed = Mathf.Sign(relX) != _flankSide && Mathf.Abs(relX) >= flankPassDistance;
        if (crossed || Time.time >= _flankEndTime)
        {
            _flanking = false;
            _stalkUntil = Time.time;       // 到背后了：马上出招
            return false;
        }

        float laneZ = pp.z + _flankLane * flankLaneOffset;
        float dz = laneZ - transform.position.z;
        float moveZ = Mathf.Abs(dz) > 0.35f ? Mathf.Sign(dz) : 0f;
        SubmitMove(new Vector2(-_flankSide, moveZ), true);     // 绕后一律奔跑
        return true;
    }

    // ── 战术选择 ────────────────────────────────────────────
    // 每轮试探开始时选一种战术。性格只是"倾向"（偏差被压缩到一半），再叠两层避让：
    //   · 自己别连着用同一招（上一轮 ×0.3，上上轮 ×0.7）；
    //   · 同群里别人刚用过的这招（5 秒内）×0.5——几只怪会自然地换着来，而不是每只固定一套。
    private const int TacMelee = 0, TacCharge = 1, TacRunBy = 2, TacLeap = 3;

    private void ChooseTactic(Vector3 delta, SkyPrisonUnitRuntimeIdentity player)
    {
        float absDx = Mathf.Abs(delta.x);
        bool canFlank = absDx > 4f;
        bool canLeap = pounceSkill != null && canFlank;
        float flank = Mathf.Clamp01(flankChance);
        float rest = 1f - flank;

        float[] w = new float[4];
        w[TacMelee] = clawSkill != null ? rest * Mathf.Clamp01(meleeWeight) * Soft(_pMelee) : 0f;
        w[TacCharge] = rest * (1f - Mathf.Clamp01(meleeWeight)) * Soft(_pPounce);
        w[TacRunBy] = canFlank ? flank * (1f - Mathf.Clamp01(leapOverChance)) * Soft(_pFlank) : 0f;
        w[TacLeap] = canLeap ? flank * Mathf.Clamp01(leapOverChance) * Soft(_pFlank) : 0f;

        for (int t = 0; t < 4; t++)
        {
            if (w[t] <= 0f) continue;
            if (t == _lastTactic) w[t] *= 0.3f;
            else if (t == _lastTactic2) w[t] *= 0.7f;
            if (t == s_recentTactic && Time.time - s_recentTacticTime < 5f) w[t] *= 0.5f;
        }

        float sum = w[0] + w[1] + w[2] + w[3];
        int pick = TacCharge;
        if (sum > 0.0001f)
        {
            float r = Random.value * sum;
            for (int t = 0; t < 4; t++) { r -= w[t]; if (r <= 0f) { pick = t; break; } }
        }

        _lastTactic2 = _lastTactic;
        _lastTactic = pick;
        s_recentTactic = pick;
        s_recentTacticTime = Time.time;

        _meleeMode = pick == TacMelee;
        _flanking = pick == TacRunBy;
        _leapPending = pick == TacLeap;
        if (_leapPending) _stalkUntil = Time.time + Random.Range(0.2f, 0.5f);       // 跃过头顶：试探很短，随后强制起跳
        if (_flanking)
        {
            _flankSide = Mathf.Sign(transform.position.x - player.transform.position.x);
            if (_flankSide == 0f) _flankSide = 1f;
            _flankLane = Random.value < 0.5f ? -1f : 1f;
            _flankEndTime = Time.time + flankMaxSeconds;
        }
    }

    // 性格只做轻微倾向：把倍率向 1 压缩一半，避免"爱绕的永远绕"
    private static float Soft(float bias) { return Mathf.Lerp(1f, bias, 0.5f); }

    // 中止当前所有出招/试探（回归、被拉走时用），并清掉预警物体
    // ── 失衡(SkyPrisonStagger：失衡条满了才打断；原来自己的「累计伤害破韧」已合并过去) ──
    /// <summary>地面冲刺飞行段是完全霸体：失衡条满了也等飞完再打断(破甲冲击除外)。</summary>
    public bool SuperArmored => _inFlight && !_inPounceAir;
    /// <summary>跳扑滞空：用更低的空中阈值，满了直接击落。</summary>
    public bool Airborne => _inPounceAir;

    public void Stagger(float seconds)
    {
        EnsureRefs();
        if (Dead) return;
        if (_inPounceAir)
        {
            // 空中被击落：取消水平冲量垂直掉下来，落地不造成伤害、改为硬直
            _knockedOutOfAir = true;
            if (_movement != null) _movement.CancelChargeDash();
            return;
        }
        if (_inFlight && _movement != null) _movement.CancelChargeDash();     // 破甲冲击强行打断冲刺：当场停下
        AbortActions();                                         // 打断前摇/试探/闪避
        SubmitMove(Vector2.zero, false);
        if (_action != null) _action.ForceHitStun(seconds);
    }

    private void AbortActions()
    {
        StopAllCoroutines();
        for (int i = 0; i < _tempObjs.Count; i++) if (_tempObjs[i] != null) Destroy(_tempObjs[i]);
        _tempObjs.Clear();
        _busy = false;
        _inFlight = false;
        _inPounceAir = false;
        _knockedOutOfAir = false;
        _flanking = false;
        _stalkStarted = false;
    }

    // 到家：重置成休眠，下次玩家进入唤醒范围/打到它才会再醒
    public void OnLeashReturnedHome()
    {
        AbortActions();
        _awake = false;
        _wasAwakeLastFrame = false;
        _nextReadyTime = 0f;
        _meleeMode = false;
        SubmitMove(Vector2.zero, false);
    }

    // 玩家近战攻击的瞬间(AttackRequestSequence 变化)，在近处就有概率后跳躲开。
    // 位移走 StartChargeDash（真的跳出攻击范围，没有无敌），所以玩家前摇很短或者贴得很近时仍然可能打中。
    private bool TryEvadePlayerMelee(SkyPrisonUnitRuntimeIdentity player, Vector3 delta)
    {
        if (_playerModule == null) _playerModule = player.GetComponentInChildren<UnitActionModuleRuntime>(true);
        if (_playerModule == null) return false;

        int seq = _playerModule.AttackRequestSequence;
        bool newAttack = _lastSeenPlayerAttackSeq >= 0 && seq != _lastSeenPlayerAttackSeq;
        _lastSeenPlayerAttackSeq = seq;
        if (!newAttack || Time.time < _nextEvadeTime) return false;

        var skill = _playerModule.CurrentSkill;
        if (skill == null || skill.isProjectileSkill) return false;      // 枪/弹幕不躲，只躲近战
        if (Mathf.Abs(delta.x) > evadeTriggerRangeX || Mathf.Abs(delta.z) > evadeTriggerRangeZ) return false;

        // 谨慎(急躁值低)的个体更爱躲
        float chance = Mathf.Clamp01(evadeChance / Mathf.Max(0.5f, _pAggression) + adaptEvadeBonus * RecentRepeatCount());
        if (Random.value >= chance) { _nextEvadeTime = Time.time + 0.6f; return false; }

        _nextEvadeTime = Time.time + evadeCooldownSeconds;
        StopAllCoroutines();                       // 打断当前的试探/出招前摇
        for (int i = 0; i < _tempObjs.Count; i++) if (_tempObjs[i] != null) Destroy(_tempObjs[i]);
        _tempObjs.Clear();
        _inFlight = false;
        StartCoroutine(EvadeRoutine(player, delta));
        return true;
    }

    // 看真正飞来的玩家子弹：沿子弹方向外推，算它经过自己 X 位置时的深度(Z)偏差，会擦过就朝远离弹道的深度方向闪一次
    private bool TryDodgeIncomingBullets(SkyPrisonUnitRuntimeIdentity player)
    {
        if (Time.time < _nextBulletDodgeTime) return false;
        var bullets = SkyPrisonSwordQiProjectile.Active;
        if (bullets.Count == 0) return false;

        Vector3 self = transform.position;
        for (int i = 0; i < bullets.Count; i++)
        {
            var b = bullets[i];
            if (b == null || b.Owner == null) continue;
            if (!UnitCombatHitbox.AreHostile(b.Owner, gameObject)) continue;          // 只躲会打到自己的（敌对方的）子弹
            Vector3 bp = b.transform.position, dir = b.Direction;
            if (Mathf.Abs(dir.x) < 0.2f) continue;                                    // 基本不沿 X 飞的不管
            float dx = self.x - bp.x;
            if (Mathf.Sign(dx) != Mathf.Sign(dir.x)) continue;                        // 子弹在背离自己的方向飞
            float dist = Mathf.Abs(dx);
            if (dist > bulletReactMaxRange || dist < bulletReactMinRange) continue;

            float tHit = dx / dir.x;                                                  // 沿方向走多少单位到自己的 X
            float zAtSelf = bp.z + dir.z * tHit;
            float offset = self.z - zAtSelf;                                          // 自己相对弹道的深度偏差
            if (Mathf.Abs(offset) > bulletLaneHalfWidth + b.HitRadius) continue;      // 擦不到自己

            float chance = Mathf.Clamp01(bulletDodgeChance / Mathf.Max(0.6f, _pAggression));
            _nextBulletDodgeTime = Time.time + bulletDodgeCooldown;
            if (Random.value >= chance) return false;

            float zDir = offset >= 0f ? 1f : -1f;                                     // 朝离开弹道的那一侧
            if (Mathf.Abs(offset) < 0.15f) zDir = Random.value < 0.5f ? -1f : 1f;
            StopAllCoroutines();
            for (int k = 0; k < _tempObjs.Count; k++) if (_tempObjs[k] != null) Destroy(_tempObjs[k]);
            _tempObjs.Clear();
            _inFlight = false;
            StartCoroutine(BulletDodgeRoutine(zDir));
            return true;
        }
        return false;
    }

    // 玩家冲刺/突刺朝自己冲来：看玩家是否在冲刺、是否朝向自己、自己是否在冲刺的直线(深度偏差小)上，是就侧闪一次
    private bool TryDodgePlayerCharge(SkyPrisonUnitRuntimeIdentity player)
    {
        Vector3 pp = player.transform.position;
        Vector3 vel = _hasLastPlayerPos && Time.deltaTime > 0.0001f ? (pp - _lastPlayerPos) / Time.deltaTime : Vector3.zero;
        _lastPlayerPos = pp; _hasLastPlayerPos = true;
        if (Time.time < _nextChargeDodgeTime) return false;

        if (_playerMove == null) _playerMove = player.GetComponentInChildren<UnitMovementController>(true);
        if (_playerMove == null || !_playerMove.IsChargeDashing) return false;

        Vector3 toSelf = transform.position - pp;
        float dist = Mathf.Abs(toSelf.x);
        if (dist > chargeDodgeRange || dist < 1.8f) return false;
        if (Mathf.Abs(vel.x) < 4f || Mathf.Sign(vel.x) != Mathf.Sign(toSelf.x)) return false;     // 没朝着自己冲
        // 沿冲刺方向外推到自己的 X，看深度偏差
        float tHit = toSelf.x / vel.x;
        float zAtSelf = pp.z + vel.z * tHit;
        float offset = transform.position.z - zAtSelf;
        if (Mathf.Abs(offset) > chargeLaneHalfWidth) return false;

        float chance = Mathf.Clamp01(chargeDodgeChance + adaptEvadeBonus * RecentRepeatCount());
        _nextChargeDodgeTime = Time.time + chargeDodgeCooldown;
        if (Random.value >= chance) return false;

        float zDir = offset >= 0f ? 1f : -1f;
        if (Mathf.Abs(offset) < 0.15f) zDir = Random.value < 0.5f ? -1f : 1f;
        StopAllCoroutines();
        for (int k = 0; k < _tempObjs.Count; k++) if (_tempObjs[k] != null) Destroy(_tempObjs[k]);
        _tempObjs.Clear();
        _inFlight = false;
        StartCoroutine(BulletDodgeRoutine(zDir));
        return true;
    }

    private IEnumerator BulletDodgeRoutine(float zDir)
    {
        _busy = true;
        SubmitMove(Vector2.zero, false);
        _movement.RequestDodge(new Vector2(0f, zDir), UnitMovementController.DodgeRuntimeState.Forward, bulletDodgeSpeedScale);
        float wait = 0f;
        while (!_movement.IsDodging && wait < 0.2f) { wait += Time.deltaTime; yield return null; }
        _movement.ExtendDodgeLockDuration(bulletDodgeDuration);
        float guard = 0f;
        while (_movement.IsDodging && guard < 1f) { guard += Time.deltaTime; yield return null; }
        // 躲开后不直接出招：回到游走，下一轮重新选战术
        _stalkStarted = false;
        _nextReadyTime = Time.time + 0.2f;
        _busy = false;
    }

    private IEnumerator EvadeRoutine(SkyPrisonUnitRuntimeIdentity player, Vector3 delta)
    {
        _busy = true;
        float away = delta.x >= 0f ? -1f : 1f;                  // 离开玩家的方向（沿 X）
        SubmitMove(Vector2.zero, false);
        if (Mathf.Abs(delta.x) > 0.3f) _movement.SetFacingOverride(new Vector2(-away, 0f));   // 仍然面朝玩家，倒着闪开
        _movement.RequestDodge(new Vector2(away, 0f), UnitMovementController.DodgeRuntimeState.Back, evadeSpeedScale);

        // 等闪避真正开始（输入是排队的，下一帧才会处理），再等它结束
        float wait = 0f;
        while (!_movement.IsDodging && wait < 0.2f) { wait += Time.deltaTime; yield return null; }
        _movement.ExtendDodgeLockDuration(evadeDurationSeconds);
        float guard = 0f;
        while (_movement.IsDodging && guard < 1f) { guard += Time.deltaTime; yield return null; }
        yield return new WaitForSeconds(evadeRecoverySeconds);

        // 躲开之后立刻反击：跳过试探、冷却几乎为零
        _nextReadyTime = Time.time + 0.1f;
        _stalkStarted = true;
        _stalkUntil = Time.time;
        _flanking = false;
        _busy = false;
    }

    // 远距离加速：按横向距离渐进，通过"移动速度倍率"外部修正实现（跟冻结/感电减速同一个通道）。
    private void UpdateSprintBoost(float absDx)
    {
        float bonus = farSprintFullX > farSprintStartX
            ? Mathf.Clamp01((absDx - farSprintStartX) / (farSprintFullX - farSprintStartX)) * farSprintMaxBonusPercent
            : 0f;
        if (_leash != null) bonus *= _leash.Willingness01;        // 被拉得越远越不愿意加速追
        if (Mathf.Abs(bonus - _appliedSprintBonus) < 5f && !(bonus <= 0f && _appliedSprintBonus > 0f)) return;   // 变化太小不重算属性
        _appliedSprintBonus = bonus;
        if (_stats == null) _stats = UnitBattleStatRuntime.EnsureOnRoot(gameObject);
        if (_stats == null) return;
        if (bonus <= 0.5f) { _appliedSprintBonus = 0f; _stats.ClearExternalModifiers("FaceWeaverSprint"); return; }
        _stats.SetExternalModifiers("FaceWeaverSprint", new System.Collections.Generic.List<StatModifier> { new StatModifier("moveSpeedRate", bonus, StatModifierOp.Add) });
    }

    // ── 冲刺 ──────────────────────────────────────────────────

    private IEnumerator DashRoutine(SkyPrisonUnitRuntimeIdentity player, float dirX)
    {
        _busy = true;
        SubmitMove(Vector2.zero, false);
        _movement.SetFacingOverride(new Vector2(dirX, 0f));

        var line = CreateLine();
        Vector3 start = transform.position + new Vector3(0f, 0.05f, 0f);
        Vector3 end = start + new Vector3(dirX * dashDistance, 0f, 0f);
        line.SetPosition(0, start); line.SetPosition(1, end);
        float t = 0f;
        while (t < dashTelegraphSeconds)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dashTelegraphSeconds);
            float flash = k > 0.75f ? (Mathf.Sin(t * 60f) * 0.5f + 0.5f) * 0.4f : 0f;
            SetLineAlpha(line, 0.15f + 0.5f * k + flash);
            if (Dead) { Destroy(line.gameObject); _busy = false; yield break; }
            yield return null;
        }

        // 冲出去：沿锁定的方向，冲刺途中碰到玩家结算一次伤害
        _movement.StartChargeDash(new Vector2(dirX, 0f), dashDistance, dashDurationSeconds);
        _inFlight = true;
        bool hit = false;
        t = 0f;
        while (t < dashDurationSeconds)
        {
            t += Time.deltaTime;
            if (!hit && TryHitPlayer(player, dashSkill, dashHitRadius)) hit = true;
            SetLineAlpha(line, Mathf.Lerp(0.6f, 0f, t / dashDurationSeconds));
            yield return null;
        }
        Destroy(line.gameObject);
        _inFlight = false;

        yield return new WaitForSeconds(dashRecoverySeconds);
        _nextReadyTime = Time.time + cooldownSeconds / (_pAggression * W);
        _stalkStarted = false;
        _busy = false;
    }

    // ── 跳扑 ──────────────────────────────────────────────────

    private IEnumerator PounceRoutine(SkyPrisonUnitRuntimeIdentity player, bool leapOver = false)
    {
        _busy = true;
        SubmitMove(Vector2.zero, false);

        // 落点预判：前摇前段跟着「玩家预计会走到的位置」慢慢移动，最后 pounceFreezeSeconds 冻结——
        // 玩家看圈、在冻结窗口里变向或急停就能躲开；匀速逃跑(上下遛怪)会被追上。
        Vector3 target = player.transform.position;
        target.y = transform.position.y;
        float side = Mathf.Sign(player.transform.position.x - transform.position.x);
        Vector3 landOffset = Vector3.zero;
        if (side != 0f && leapOver)
            landOffset.x = side * leapOverLandBehind;                  // 跃过头顶：明确落到背后
        else if (side != 0f && Random.value < crossOverChance)
            landOffset.x = side * 1.0f;      // 越过玩家落到身后，同时仍在落地范围内
        target += landOffset;
        var ring = CreateRing(target, pounceLandRadius);
        Vector3 prevPlayerPos = player.transform.position, playerVel = Vector3.zero;
        float t = 0f;
        while (t < pounceTelegraphSeconds)
        {
            float dtp = Mathf.Max(Time.deltaTime, 0.0001f);
            t += Time.deltaTime;
            // 玩家真实位移速度(平滑)：不读按键，读实际走出来的位置
            Vector3 cur = player.transform.position;
            Vector3 instVel = (cur - prevPlayerPos) / dtp; instVel.y = 0f;
            playerVel = Vector3.Lerp(playerVel, instVel, 1f - Mathf.Exp(-12f * dtp));
            prevPlayerPos = cur;
            if (t < pounceTelegraphSeconds - pounceFreezeSeconds)
            {
                Vector3 lead = playerVel * ((pounceTelegraphSeconds - t) + pounceFlightSeconds) * pounceLeadStrength;
                if (lead.magnitude > pounceMaxLead) lead = lead.normalized * pounceMaxLead;
                Vector3 goal = cur + lead + landOffset; goal.y = target.y;
                target = Vector3.Lerp(target, goal, 1f - Mathf.Exp(-10f * dtp));
                MoveRing(ring, target, pounceLandRadius);
            }
            float k = Mathf.Clamp01(t / pounceTelegraphSeconds);
            float flash = k > 0.75f ? (Mathf.Sin(t * 60f) * 0.5f + 0.5f) * 0.4f : 0f;
            SetLineAlpha(ring, 0.2f + 0.5f * k + flash);
            float dx = target.x - transform.position.x;
            if (Mathf.Abs(dx) > 0.3f) _movement.SetFacingOverride(new Vector2(Mathf.Sign(dx), 0f));
            if (Dead) { Destroy(ring.gameObject); _busy = false; yield break; }
            yield return null;
        }

        float groundY = transform.position.y;              // 起跳前的地面高度，落地以回到这个高度为准
        // 起跳 + 水平位移同时进行，飞行时间到就是落地
        Vector3 d = target - transform.position;
        Vector2 dir2 = new Vector2(d.x, d.z);
        float dist = dir2.magnitude;
        if (dist > 0.05f)
        {
            _movement.RequestJump();
            _movement.StartChargeDash(dir2.normalized, dist, pounceFlightSeconds);
        }
        _inFlight = true;
        _inPounceAir = true;
        _knockedOutOfAir = false;
        t = 0f;
        while (t < pounceFlightSeconds)
        {
            t += Time.deltaTime;
            if (Dead) { Destroy(ring.gameObject); _busy = false; _inPounceAir = false; yield break; }
            yield return null;
        }

        // 定时器到点不等于真的落地：跳跃物理有自己的滞空弧线，经常比这个计时更长，
        // 直接结算会出现"伤害比模型落地更早"。再等到真正落地（跳跃状态结束）才结算，最多多等 1.2 秒兜底。
        // 光看"跳跃状态结束"还不够（它结束得比模型视觉落地早）：再看高度，真的回到地面才算落地。
        for (float g = 0f; _movement != null && (_movement.IsJumping || transform.position.y - groundY > landHeightTolerance) && g < 1.4f; g += Time.deltaTime)
        {
            if (Dead) { Destroy(ring.gameObject); _busy = false; _inFlight = false; _inPounceAir = false; yield break; }
            yield return null;
        }
        _inPounceAir = false;
        if (landDamageDelay > 0f) yield return new WaitForSeconds(landDamageDelay);

        if (_knockedOutOfAir)
        {
            // 被击落：摔在地上，不造成落地伤害，硬直一段时间——这就是空中冒险攻击的回报
            _knockedOutOfAir = false;
            _inFlight = false;
            Destroy(ring.gameObject);
            if (_action != null) _action.ForceHitStun(airKnockdownSeconds);
            yield return new WaitForSeconds(airKnockdownSeconds);
            _nextReadyTime = Time.time + cooldownSeconds / (_pAggression * W);
            _stalkStarted = false;
            _busy = false;
            yield break;
        }

        // 落地：以落点为圆心的范围伤害（飞行途中不伤人，所以空中的它是安全的"穿越"窗口）
        _inFlight = false;
        SpawnLandingDust();
        TryHitPlayer(player, pounceSkill, pounceLandRadius, target);
        Destroy(ring.gameObject);

        yield return new WaitForSeconds(leapOver ? pounceRecoverySeconds * 0.4f : pounceRecoverySeconds);
        if (leapOver)
        {
            // 落到背后了：不试探，立刻从背后出下一招
            _nextReadyTime = Time.time + 0.1f;
            _stalkStarted = true;
            _stalkUntil = Time.time;
            _meleeMode = clawSkill != null;
            _busy = false;
            yield break;
        }
        _nextReadyTime = Time.time + cooldownSeconds / (_pAggression * W);
        _stalkStarted = false;
        _busy = false;
    }

    // ── 工具 ──────────────────────────────────────────────────

    private bool Dead => _death != null && _death.IsDeadLike;

    private bool TryHitPlayer(SkyPrisonUnitRuntimeIdentity player, SkillDefinition skill, float radius, Vector3? center = null)
    {
        if (player == null || skill == null || _module == null) return false;
        Vector3 c = center ?? transform.position;
        Vector3 p = player.transform.position;
        float dx = p.x - c.x, dz = p.z - c.z;
        if (dx * dx + dz * dz > radius * radius) return false;
        if (Mathf.Abs(p.y - transform.position.y) > 2.0f) return false;   // 玩家在高处/空中时够不着
        if (_playerCollider == null) _playerCollider = player.GetComponentInChildren<Collider>();
        if (_playerCollider == null) return false;
        _module.ResolveSkillHit(skill, _playerCollider);
        return true;
    }

    private void SubmitMove(Vector2 move, bool run)
    {
        if (_action != null) _action.SubmitMoveIntent(move, run, false);
        else if (_movement != null) _movement.SetExternalMoveInput(move, run);
    }

    private static Material GetLineMaterial()
    {
        if (_lineMaterial == null) _lineMaterial = new Material(Shader.Find("Sprites/Default"));
        return _lineMaterial;
    }

    private LineRenderer CreateLine()
    {
        var go = new GameObject("FaceWeaverTelegraph");
        int layer = LayerMask.NameToLayer("World3D");
        if (layer >= 0) go.layer = layer;
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.enabled = showDashLine;
        lr.positionCount = 2;
        lr.startWidth = lr.endWidth = 0.14f;
        lr.alignment = LineAlignment.View;
        lr.material = new Material(GetLineMaterial());
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        SetLineAlpha(lr, 0.15f);
        _tempObjs.Add(go);
        return lr;
    }

    private void MoveRing(LineRenderer lr, Vector3 center, float radius)
    {
        if (lr == null) return;
        int seg = lr.positionCount;
        for (int i = 0; i < seg; i++)
        {
            float a = i / (float)seg * Mathf.PI * 2f;
            lr.SetPosition(i, center + new Vector3(Mathf.Cos(a) * radius, 0.05f, Mathf.Sin(a) * radius));
        }
    }

    private LineRenderer CreateRing(Vector3 center, float radius)
    {
        var go = new GameObject("FaceWeaverLandRing");
        int layer = LayerMask.NameToLayer("World3D");
        if (layer >= 0) go.layer = layer;
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.enabled = showAttackRings;          // 不显示攻击圈：对象照建（调用方照常用），只是不渲染
        lr.loop = true;
        const int seg = 40;
        lr.positionCount = seg;
        for (int i = 0; i < seg; i++)
        {
            float a = i / (float)seg * Mathf.PI * 2f;
            lr.SetPosition(i, center + new Vector3(Mathf.Cos(a) * radius, 0.05f, Mathf.Sin(a) * radius));
        }
        lr.startWidth = lr.endWidth = 0.14f;
        lr.alignment = LineAlignment.View;
        lr.material = new Material(GetLineMaterial());
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        SetLineAlpha(lr, 0.2f);
        _tempObjs.Add(go);
        return lr;
    }

    private static void SetLineAlpha(LineRenderer lr, float alpha)
    {
        if (lr == null) return;
        var c = new Color(1f, 0.25f, 0.2f, Mathf.Clamp01(alpha));
        lr.startColor = lr.endColor = c;
        if (lr.material != null) lr.material.color = c;
    }
}
