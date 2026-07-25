using System;
using System.Collections.Generic;
using UnityEngine;

public enum UnitDefineType
{
    [InspectorName("人物")]
    Character,

    [InspectorName("道具")]
    Item,

    [InspectorName("可破坏物品")]
    Destructible,

    [InspectorName("特效")]
    VFX,

    [InspectorName("音效")]
    SFX
}

public enum CharacterIdentity
{
    [InspectorName("玩家")]
    Player,

    [InspectorName("敌人")]
    Enemy,

    [InspectorName("精英")]
    Elite,

    [InspectorName("BOSS")]
    Boss,

    [InspectorName("友军")]
    Ally,

    [InspectorName("中立敌意")]
    NeutralHostile,

    [InspectorName("中立无敌意")]
    NeutralPassive,

    [InspectorName("生物")]
    Creature
}

public enum OutlineGroup
{
    [InspectorName("无")]
    None,

    [InspectorName("Player 描边")]
    Player,

    [InspectorName("Enemy 描边")]
    Enemy,

    [InspectorName("Ally 描边")]
    Ally,

    [InspectorName("Item 描边")]
    Item
}


public enum UnitVisualChannel
{
    [InspectorName("Spine")]
    Spine,

    [InspectorName("3D 模型")]
    Model3D
}

public enum UnitMovementType
{
    [InspectorName("行走")]
    Walk,

    [InspectorName("浮游")]
    Hover,

    [InspectorName("不可移动")]
    Immobile
}

public enum UnitCorpseCleanupMode
{
    [InspectorName("禁用对象")]
    DisableObject,

    [InspectorName("销毁对象")]
    DestroyObject
}



public enum UnitActionAnimationSlot
{
    [InspectorName("待机")]
    Idle = 0,

    [InspectorName("行走")]
    Walk = 1,

    [InspectorName("奔跑")]
    Run = 2,

    // 旧通用跳跃槽：保留给旧资源 / 旧调用兜底。
    [InspectorName("跳跃")]
    Jump = 3,

    [InspectorName("攻击")]
    Attack = 4,

    [InspectorName("受击")]
    Hit = 5,

    // 旧通用闪避槽：保留给旧资源 / 旧调用兜底。
    [InspectorName("闪避")]
    Dodge = 6,

    [InspectorName("死亡")]
    Death = 7,

    [InspectorName("潜行")]
    Sneak = 8,

    // 新增槽位全部放在旧枚举值之后，避免改变旧资源序列化。
    [InspectorName("起跳")]
    JumpStart = 9,

    [InspectorName("滞空")]
    JumpAir = 10,

    [InspectorName("落地")]
    JumpLand = 11,

    [InspectorName("向前闪避")]
    DodgeForward = 12,

    [InspectorName("向后闪避")]
    DodgeBack = 13
}
[Serializable]
public class UnitAnimationKeySet
{
    [Header("移动状态")]
    public string idleKey = "idle";
    public string walkKey = "walk";
    public string runKey = "run";
    public string sneakKey = "sneak";

    [Header("跳跃状态")]
    // 旧字段保留：给旧资源 / 旧调用作为通用兜底，不建议新资源继续只填它。
    public string jumpKey = "jump";
    public string jumpStartKey = "Jump_Start";
    public string jumpAirKey = "Jump_Air";
    public string jumpLandKey = "Jump_Land";

    [Header("动作状态")]
    public string attackKey = "attack";
    public string hitKey = "hit";
    // 旧字段保留：给旧资源 / 旧调用作为通用闪避兜底。
    public string dodgeKey = "dodge";
    public string dodgeForwardKey = "dodge_forward";
    public string dodgeBackKey = "dodge_back";
    public string deathKey = "die";

    [Header("播放规则")]
    public bool driveMovementAnimation = true;
    public float movementAnimationFade = 0.08f;
    public float oneShotLockSeconds = 0.18f;

    public string GetKey(UnitActionAnimationSlot slot)
    {
        switch (slot)
        {
            case UnitActionAnimationSlot.Idle: return idleKey;
            case UnitActionAnimationSlot.Walk: return walkKey;
            case UnitActionAnimationSlot.Run: return runKey;
            case UnitActionAnimationSlot.Sneak: return sneakKey;
            case UnitActionAnimationSlot.Jump: return GetFirstNonEmpty(jumpKey, jumpStartKey);
            case UnitActionAnimationSlot.JumpStart: return GetFirstNonEmpty(jumpStartKey, jumpKey);
            case UnitActionAnimationSlot.JumpAir: return GetFirstNonEmpty(jumpAirKey, jumpKey);
            case UnitActionAnimationSlot.JumpLand: return GetFirstNonEmpty(jumpLandKey, jumpKey);
            case UnitActionAnimationSlot.Attack: return attackKey;
            case UnitActionAnimationSlot.Hit: return hitKey;
            case UnitActionAnimationSlot.Dodge: return dodgeKey;
            case UnitActionAnimationSlot.DodgeForward: return GetFirstNonEmpty(dodgeForwardKey, dodgeKey);
            case UnitActionAnimationSlot.DodgeBack: return GetFirstNonEmpty(dodgeBackKey, dodgeKey);
            case UnitActionAnimationSlot.Death: return deathKey;
            default: return string.Empty;
        }
    }

    private static string GetFirstNonEmpty(params string[] values)
    {
        if (values == null)
            return string.Empty;

        for (int i = 0; i < values.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(values[i]))
                return values[i];
        }

        return string.Empty;
    }
}

public enum UnitVisionShape
{
    [InspectorName("圆形")]
    Circle,

    [InspectorName("扇区")]
    Sector
}

[Serializable]
public class UnitParameterValue
{
    public string parameterKey = "";
    public float value = 0f;
}

[CreateAssetMenu(menuName = "Game/Units/Unit Definition", fileName = "UD_NewUnit")]
public class UnitDefinition : ScriptableObject
{
    [Header("基础信息")]
    public string unitId;
    public string displayName;
    public string note;

    [TextArea(4, 8)]
    public string description;

    [Header("多语言名称")]
    public List<LocalizedTextEntry> localizedNames = new List<LocalizedTextEntry>();

    [Header("多语言描述")]
    public List<LocalizedTextEntry> localizedDescriptions = new List<LocalizedTextEntry>();

    [Header("定义类型")]
    public UnitDefineType defineType = UnitDefineType.Character;

    [Header("人物专用")]
    public CharacterIdentity characterIdentity = CharacterIdentity.Player;

    [Header("战斗参数")]
    public BattleParameterDatabase battleParameterDatabase;
    public float anomalyReceiveMultiplier = 1f;

    [Header("单位运行时预制体")]
    public GameObject prefab;

    [Header("视觉通道")]
    public UnitVisualChannel visualChannel = UnitVisualChannel.Spine;

    [Header("Spine 通道")]
    public ScriptableObject spinePrefab;

    [Header("3D 通道")]
    public GameObject model3DPrefab;


    [Header("基础显示")]
    public Sprite icon;

    [Header("控制方式")]
    public UnitControlMode controlMode = UnitControlMode.AIControlled;

    [Header("AI 行为包")]
    public AIBehaviorPackage aiBehaviorPackage;

    [Header("视野")]
    public bool enableVision = true;
    public float visionRadius = 12f;
    public float visionHeight = 2.2f;
    public UnitVisionShape visionShape = UnitVisionShape.Circle;
    public float visionAngle = 120f;
    public bool useFacingDirection = false;
    public bool ignoreObstacleOcclusion = false;
    public bool shareVisionToFaction = true;
    public bool ignoreFogOfWar = false;

    [Header("听觉感知")]
    [Tooltip("是否启用听觉感知。敌人/生物通常开启；玩家、纯装饰单位、无生命单位可以关闭。")]
    public bool canUseHearing = true;

    [Tooltip("是否可以被其它单位感知。玩家和敌人通常开启；纯装饰物通常关闭。")]
    public bool canBePerceived = true;

    [Tooltip("听力倍率。1.0 = 普通听力；20.0 = 顺风耳；0.0 = 完全听不见。")]
    public float hearingMultiplier = 1f;

    [Tooltip("听觉基础范围。该范围内距离衰减按满值计算。")]
    public float hearingBaseRange = 10f;

    [Tooltip("听觉最大范围。超过该范围后不会接收到声音事件。")]
    public float hearingMaxRange = 30f;

    [Tooltip("听觉有效强度达到该值时进入怀疑。")]
    public float hearingSuspicionThreshold = 0.005f;

    [Tooltip("听觉有效强度达到该值时进入警戒。")]
    public float hearingAlertThreshold = 0.015f;

    [Tooltip("听觉有效强度达到该值时进入发现。")]
    public float hearingDetectThreshold = 0.04f;

    [Tooltip("听觉记忆秒数。声音消失后怀疑/警戒/发现状态保留多久。")]
    public float hearingMemorySeconds = 3f;

    [Header("遮挡与描边规则")]
    public bool useOcclusionOutline = true;
    public bool useSharedOutlineGroup = true;

    [Header("物理规范")]
    public bool useKinematicBody = true;
    public bool freezeRotationX = true;
    public bool freezeRotationY = true;
    public bool freezeRotationZ = true;
    public bool allowPhysicalPush = false;


    [Header("单位音声")]
    [Tooltip("单位没有鞋子装备，或怪物/机械等不走鞋子装备逻辑时使用的默认脚步声包。Player 可设为裸足；敌人/怪物应按单位类型设置 claw / robotic / heavy creature 等专属包。")]
    public SkyPrisonAudioPackage defaultFootstepAudioPackage;

    [Header("单位UI")]
    public bool lockShadowProjectorTransform = true;
    public Vector3 shadowLocalPosition = new Vector3(0f, -0.02f, 0f);
    public Vector3 shadowLocalEuler = Vector3.zero;
    public Vector3 shadowLocalScale = Vector3.one;
    public float overheadUiOffsetX = 0f;
    public float overheadUiOffsetY = 0f;
    public bool autoOverheadNameVisibility = true;
    public bool manualShowOverheadName = false;
    public OverheadBarStyleAsset overheadHpBarStyle;

    [Header("单位碰撞盒")]
    public bool overrideCollisionShape = true;
    public Vector3 collisionLocalCenter = new Vector3(0f, 0.7f, 0f);
    public float collisionRadius = 0.6f;
    public float collisionHeight = 1.2f;

    [Header("单位物理碰撞判定")]
    [Tooltip("把单位实体碰撞根节点设置到 UnitBody 等真实物理层。不会修改单位根节点 Layer，旧遮挡仍可继续走 Character2D。")]
    public bool enableUnitBodyCollision = true;
    public string unitBodyLayerName = "UnitBody";

    [Tooltip("自动生成包裹单位的 UnitPhysicsProbe 触发器，用来推动 PushableProp。")]
    public bool enableUnitPhysicsProbe = true;
    public string unitPhysicsProbeLayerName = "UnitPhysicsProbe";
    public string unitPhysicsProbeRootName = "UnitPhysicsProbe";
    public Vector3 unitPhysicsProbeLocalCenter = new Vector3(0f, 0.65f, 0f);
    public float unitPhysicsProbeRadius = 0.36f;
    public float unitPhysicsProbeHeight = 1.15f;

    [Header("移动规则")]
    public UnitMovementType movementType = UnitMovementType.Walk;
    public float idealWalkSpeed = 3.6f;
    public float idealSneakSpeed = 2.0f;
    public float idealRunSpeed = 5.2f;
    public float idealJumpHeight = 1.4f;
    [Tooltip("轻负重跳跃高度倍率。通常保持 1。")]
    public float lightBurdenJumpHeightMultiplier = 1.0f;
    [Tooltip("中负重跳跃高度倍率。运行时会在轻/中/重区间内按当前负重比例插值。")]
    public float mediumBurdenJumpHeightMultiplier = 0.88f;
    [Tooltip("重负重跳跃高度倍率。超重后默认不能跳跃。")]
    public float heavyBurdenJumpHeightMultiplier = 0.62f;
    [Tooltip("超重时禁止跳跃，和超重不能跑步保持一致。")]
    public bool overweightDisablesJump = true;
    public float movementInertia = 45f;
    public float minWalkSpeed = 1.2f;
    public float sustainedMoveSpeedMultiplier = 0.93f;
    public float sustainedMoveDelay = 0.12f;

    [Header("战斗模组")]
    [Tooltip("单位默认使用的战斗模组（无武器/待机姿态）。运行时装备武器后会切换到对应模组。")]
    public WeaponCombatModule defaultCombatModule;

    [Header("血液特效")]
    [Tooltip("受击时的出血演出类型。None = 不显示；其余类型驱动 BloodVFXManager 选用对应特效。")]
    public UnitBloodVFXType bloodVFXType = UnitBloodVFXType.Normal;
    [Tooltip("血液颜色（Normal / DarkBlood 类型生效）。和谐模式开启时此项被覆盖为黑色。")]
    public Color bloodColor = new Color(0.72f, 0.02f, 0.02f, 1f);

    [Header("动作动画 Key")]
    public UnitAnimationKeySet animationKeys = new UnitAnimationKeySet();

    [Header("死亡规则")]
    public bool playDeathAnimation = true;
    public string deathTriggerName = "Die";
    public bool useCorpseDecay = true;
    public float corpseDecaySeconds = 8f;
    public UnitCorpseCleanupMode corpseCleanupMode = UnitCorpseCleanupMode.DestroyObject;
    public bool treatAsRespawnablePlayer = false;

    [Header("死亡溶解特效")]
    [Tooltip("尸体停留时间快结束时是否播放溶解消失特效（只在启用尸体自动清理时生效；玩家走复活流程，不会进入这段逻辑）。")]
    public bool useDeathDissolve = true;
    [Tooltip("溶解特效播放时长（秒），从尸体停留时间倒数这么多秒开始播放，播完立刻清理尸体。")]
    public float deathDissolveSeconds = 1.2f;
    [Tooltip("溶解特效前半段用来把本体压黑的时间占比（0~1）。比如0.35代表先花35%的时长把角色压黑，剩下65%再播放真正的消失。")]
    [Range(0f, 1f)]
    public float deathDissolveDarkenFraction = 0.35f;
    [Tooltip("溶解边缘发光颜色。")]
    public Color deathDissolveEdgeColor = new Color(0.5f, 0.04f, 0.02f, 1f);
    [Tooltip("溶解边缘宽度。")]
    [Range(0f, 1f)]
    public float deathDissolveEdgeWidth = 0.12f;
    [Tooltip("溶解噪波贴图，留空则使用运行时默认噪波。")]
    public Texture2D deathDissolveNoiseTexture;
    [Tooltip("溶解噪波在世界空间的平铺密度，数值越大噪波颗粒越细碎。")]
    public float deathDissolveNoiseScale = 0.6f;

    [Header("掉落配置")]
    public List<DropProfile> dropProfiles = new List<DropProfile>();

    [Header("属性数值容器")]
    public List<UnitParameterValue> parameterValues = new List<UnitParameterValue>();

    public bool IsCharacter => defineType == UnitDefineType.Character;
    public bool IsItem => defineType == UnitDefineType.Item;
    public bool HasOutline => ResolvedOutlineGroup != OutlineGroup.None;
    public bool IsImmobile => movementType == UnitMovementType.Immobile;
    public bool IsHoverUnit => movementType == UnitMovementType.Hover;
    public bool EnableHiddenPartOutline => HasOutline && useOcclusionOutline;

    public float ClampedAnomalyReceiveMultiplier => Mathf.Max(0f, anomalyReceiveMultiplier);

    public OutlineGroup ResolvedOutlineGroup
    {
        get
        {
            switch (defineType)
            {
                case UnitDefineType.Character:
                    return ResolveCharacterOutline(characterIdentity);
                case UnitDefineType.Item:
                    return OutlineGroup.Item;
                case UnitDefineType.Destructible:
                case UnitDefineType.VFX:
                case UnitDefineType.SFX:
                default:
                    return OutlineGroup.None;
            }
        }
    }

    private static OutlineGroup ResolveCharacterOutline(CharacterIdentity identity)
    {
        switch (identity)
        {
            case CharacterIdentity.Player:
                return OutlineGroup.Player;
            case CharacterIdentity.Enemy:
            case CharacterIdentity.Elite:
            case CharacterIdentity.Boss:
            case CharacterIdentity.NeutralHostile:
                return OutlineGroup.Enemy;
            case CharacterIdentity.Ally:
            case CharacterIdentity.NeutralPassive:
            case CharacterIdentity.Creature:
                return OutlineGroup.Ally;
            default:
                return OutlineGroup.None;
        }
    }
}