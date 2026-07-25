using System;
using System.Collections.Generic;
using Effekseer;
using UnityEngine;
using UnityEngine.Serialization;

public enum SkillCategory
{
    [InspectorName("轻攻击")]   LightAttack  = 0,
    [InspectorName("重攻击")]   HeavyAttack  = 10,
    [InspectorName("冲刺技")]   Dash         = 20,
    [InspectorName("特殊技")]   Special      = 30,
    [InspectorName("被动")]     Passive      = 40,
}

public enum SkillHitboxShape
{
    [InspectorName("圆形")] Circle = 0,
    [InspectorName("矩形")] Box    = 1,
}

// 一条SE素材+它自己的音量倍率——不同录音本身响度不一致时，不用把整批素材都换掉，
// 单独把偏轻的那条调响就行。这个倍率是在技能整体的音量倍率(seVolume/swingSEVolume)
// 基础上再乘一次，两者不冲突：整体倍率控制"这一类SE(出招/命中/打空)整体多响"，
// 这里的倍率控制"这一条具体素材相对同类里其他素材多响"。
[Serializable]
public class SkillSoundEntry
{
    public AudioClip clip;
    [Tooltip("这一条素材自己的音量倍率，在技能/模组的整体音量倍率基础上再乘一次。1=不额外调整。")]
    public float volume = 1f;
}

[Serializable]
public class SkillHitboxData
{
    public SkillHitboxShape shape   = SkillHitboxShape.Circle;
    public Vector2          offset  = Vector2.zero;
    public float            radius  = 0.5f;
    public Vector2          size    = Vector2.one;
    [Tooltip("3D 判定 Z 轴覆盖深度（胶囊朝 Z 轴延伸的总长度）。通常 1.5~3，覆盖场景里角色 Z 轴差即可。")]
    public float            zDepth  = 2.0f;
}

// 蓄力技能专用参数——只在 SkillDefinition.isChargeSkill = true 时才会被读取/生效，
// 普通技能完全不关心这组字段，跟蓄力没有任何关系。2026-07-19从UnitActionModuleRuntime
// 组件搬过来：这些数值本质上是"这个技能蓄力起来是什么手感"，是每个技能自己的属性，
// 不该是攻击执行器组件上全局共用的一份（以后加第二把武器的蓄力技，需要各自配自己
// 的蓄力手感，不能所有蓄力技共用同一套数字）。
[Serializable]
public class SkillChargeData
{
    [Header("定格 & 释放")]
    [Tooltip("Spine事件名——动画放到这个事件所在的那一帧，会自动把播放速度定格成0（暂停在\n" +
        "这个姿势），等玩家松开蓄力键才会继续往下播完剩下的部分（比如刺出去）。")]
    public string chargeHoldEventKey = "charge_hold";
    [Tooltip("松开蓄力键之后，动画以多快的速度继续播放剩下的部分。1=原速，调高让释放瞬间\n" +
        "更干脆利落，冲刺时长也会跟着变短(见下面冲刺距离注释)。")]
    public float releaseTimeScale = 1f;

    [Header("蓄满伤害加成")]
    [Tooltip("从动画定格（charge_hold触发）那一刻开始算，握住蓄力键满这么多秒再松开，这次\n" +
        "攻击算作'蓄满'，伤害会乘上下面的加成倍率。可以随时提前松开不蓄满，正常造成伤害，\n" +
        "只是没有这个加成。")]
    public float fullChargeHoldSeconds = 1f;
    [Tooltip("蓄满之后，攻击力（进而物理伤害+属性伤害）额外乘上这个倍率。1=没有加成。")]
    public float fullChargeDamageMultiplier = 1.5f;
    [Tooltip("蓄力刚好达到蓄满所需秒数的那一刻播放一次，提示玩家'已经蓄满了，可以松手了'。\n" +
        "随机取一个片段。留空则蓄满时不播放提示音。")]
    public SkillSoundEntry[] fullChargeReachedSE = System.Array.Empty<SkillSoundEntry>();

    [Header("持续LP消耗")]
    [Tooltip("蓄力定格期间(charge_hold触发到松开释放为止)每秒持续消耗多少LP，不是按下蓄力键\n" +
        "那一刻一次性扣。0=不消耗。LP中途正好耗尽会自动触发释放，等同于玩家自己松开了\n" +
        "按键，不会白嫖。")]
    public float holdLpCostPerSecond = 3f;

    [Header("释放冲刺")]
    [Tooltip("松开蓄力键的瞬间，除了动画恢复播放，角色本身也朝当前朝向冲一段距离——类似\n" +
        "闪避的位移感，但保留攻击判定(hit_start/hit_end事件照常生效)，不是无敌帧，跟闪避\n" +
        "完全是两套独立机制。")]
    public bool dashOnRelease = true;
    [Tooltip("冲刺距离（米）。冲刺实际跑多久是按释放瞬间动画剩余播放时长/releaseTimeScale\n" +
        "自动算的（保证冲刺物理位移和刺击动作同步，判定框不管在动画哪个位置触发都落在\n" +
        "冲刺过程中）——时长不是这里配的，调这个距离数值就相当于同时调了距离和速度。")]
    public float dashDistance = 10f;
    [Tooltip("勾选后，冲刺距离按实际蓄力时长/蓄满所需秒数(fullChargeHoldSeconds)的比例\n" +
        "缩放——没蓄满就松手，冲刺距离按比例变短，蓄满或超过蓄满时长则跑满冲刺距离。\n" +
        "不勾选则不管蓄没蓄满，冲刺距离都是固定的dashDistance。")]
    public bool dashDistanceScalesWithChargeRatio = false;
    [Tooltip("冲刺持续时间的备用值，只在拿不到动画时长时才会被当兜底用。")]
    public float dashFallbackDurationSeconds = 0.25f;
    [Tooltip("冲刺特效（軌跡/Trail类型，靠发射点真实位移画出飘带）。释放蓄力那一刻播放，\n" +
        "挂在角色身上逐帧跟随位移，直到冲刺结束（不会中途打断，保证飘带不脱节）。")]
    public EffekseerEffectAsset dashVfx;
    [Tooltip("冲刺特效锚点：绑到武器尖端(Spine骨架里名叫fx_tip的插槽)，或者绑到角色本身\n" +
        "（骨骼所在Transform，可以用下面的偏移量调整位置）。")]
    public ChargeDashVfxAnchorMode dashVfxAnchor = ChargeDashVfxAnchorMode.WeaponTip;
    [Tooltip("锚点选「绑到角色」时生效——在角色骨骼位置基础上再加的本地偏移量（X右/\n" +
        "Y上/Z前后）。锚点选「绑到武器尖端」时这个偏移量不生效。")]
    public Vector3 dashVfxCharacterAnchorOffset = Vector3.zero;
}

public enum ChargeDashVfxAnchorMode
{
    [InspectorName("绑到武器尖端(fx_tip)")] WeaponTip = 0,
    [InspectorName("绑到角色(带偏移量)")]    Character = 1,
}

// 远程弹幕技能专用参数——只在 SkillDefinition.isProjectileSkill = true 时才会被读取/
// 生效，跟近战判定框(hitbox)是两条互斥的判定路径：勾了这个，hit_start 触发时就不再
// Activate 近战 hitbox，改成生成一发真的会飞、会检测碰撞的抛射物。
[Serializable]
public class SkillProjectileData
{
    [Tooltip("抛射物飞行速度（米/秒）。")]
    public float speed = 20f;
    [Tooltip("最长存活时间（秒）——超过这个时间还没命中任何目标就自行销毁，防止弹幕\n" +
        "无限飞出场景外还占着一个GameObject。")]
    public float maxLifetimeSeconds = 2f;
    [Tooltip("发射方向和角色当前朝向的夹角（度），0=正前方，正数=朝下偏转。跟角色朝向\n" +
        "镜像联动：面朝右时朝右下偏转，面朝左时朝左下偏转，不用分别配两份。")]
    public float launchAngleDownDegrees = 45f;
    [Tooltip("命中判定半径（米）——抛射物是一个球形触发器，飞行过程中只要有敌对单位的\n" +
        "受击体进到这个半径内就算命中。")]
    public float hitRadius = 0.4f;
    [Tooltip("命中后要不要销毁抛射物。不勾选=可以穿透继续飞、命中多个目标（每个目标只\n" +
        "计一次，不会同一帧内被同一发弹幕反复打好几下）。")]
    public bool destroyOnHit = true;
    [Tooltip("抛射物本体的 Effekseer 特效（跟着弹幕一起飞的视觉，比如剑气本体）。")]
    public EffekseerEffectAsset projectileVfx;
    [Tooltip("抛射物特效整体缩放。")]
    public Vector3 projectileVfxScale = Vector3.one;
    [Tooltip("命中目标时额外播放一次的 Effekseer 特效（留空则命中时只有伤害数字/受击音，\n" +
        "没有额外的命中爆点特效）。")]
    public EffekseerEffectAsset impactVfx;
}

// 之前叫"元素伤害"（SkillElementalHit/elementalHits），跟属性/异常系统（BattleParameterDatabase
// 里的"属性/异常定义"）统一改叫"属性伤害"，避免同一个概念两套叫法。用 FormerlySerializedAs
// 保留旧序列化数据，已经配好的技能不会因为改名丢数据。
[Serializable]
public class SkillAttributeHit
{
    [FormerlySerializedAs("elementKey")]
    [Tooltip("属性 key，对应 BattleParameterDatabase → 属性/异常定义（heat / shock / corrosion / freeze）")]
    public string attributeKey              = "heat";
    [FormerlySerializedAs("elementDamageMultiplier")]
    [Tooltip("属性伤害 = 攻击力 × attributeDamageMultiplier，独立于物理伤害单独判定/单独结算。0 = 只蓄积异常，不直接扣血。")]
    public float  attributeDamageMultiplier = 0.5f;
    [Tooltip("异常蓄积量倍率，相对于属性定义里的默认蓄积速率。")]
    public float  anomalyBuildupMultiplier = 1.0f;
}

[CreateAssetMenu(menuName = "Sky Prison/Skills/Skill Definition", fileName = "SK_NewSkill")]
public class SkillDefinition : ScriptableObject
{
    [Header("基础信息")]
    public string skillKey    = "new_skill";
    public string displayName = "新技能";

    [TextArea(2, 5)]
    public string description = "";

    [Header("分类")]
    public SkillCategory category = SkillCategory.LightAttack;

    [Header("动画")]
    [Tooltip("对应 Spine 动画 key，传给 SpineAnimationDriver")]
    public string spineAnimationKey = "";
    [Tooltip("Spine event 名称，触发 hitbox 判定帧")]
    public string spineEventKey     = "hit";

    [Header("蓄力")]
    [Tooltip("这个技能是不是蓄力技——不是每个技能都需要蓄力，勾上之后下面charge这组参数\n" +
        "才会生效(动画定格/蓄满加成/持续LP消耗/释放冲刺)，非蓄力技能完全不关心这组\n" +
        "字段，两者没有任何关系。")]
    public bool isChargeSkill = false;
    [Tooltip("只在isChargeSkill勾选时才生效。")]
    public SkillChargeData charge = new SkillChargeData();

    [Header("判定框")]
    [Tooltip("isProjectileSkill 勾选时不生效——远程弹幕技能的判定改由下面的\n" +
        "SkillProjectileData.hitRadius 决定，这组近战判定框数据会被跳过。")]
    public SkillHitboxData hitbox = new SkillHitboxData();

    [Header("远程弹幕")]
    [Tooltip("这个技能是不是远程弹幕技——勾上之后 hit_start 不再打开近战判定框，改成生成\n" +
        "一发真的会飞的抛射物，飞行中检测碰撞；下面这组 projectile 参数才会生效。\n" +
        "非弹幕技能完全不关心这组字段。")]
    public bool isProjectileSkill = false;
    [Tooltip("只在isProjectileSkill勾选时才生效。")]
    public SkillProjectileData projectile = new SkillProjectileData();

    [Header("伤害")]
    [Tooltip("物理伤害 = 攻击力 × damageMultiplier")]
    public float  damageMultiplier = 1.0f;
    [Tooltip("物理伤害类型（slash / strike / impact）。影响护甲类型交互。")]
    public string damageTypeKey    = "strike";
    [FormerlySerializedAs("elementalHits")]
    [Tooltip("属性伤害列表。每种属性各自算一次伤害、独立结算、独立蓄积异常（斩击物理 + 灼热 + 电磁 + 冻结" +
             "可以同时挂在一把武器上，互不影响），可叠加多个属性。")]
    public List<SkillAttributeHit> attributeHits = new List<SkillAttributeHit>();

    [Header("消耗")]
    [Tooltip("释放此技能消耗的 LP（负荷值）。0 = 无消耗，LP 不足时无法释放。")]
    public float lpCost = 0f;

    [Header("击退 & 硬直")]
    public float knockbackForce  = 0f;
    public float stunDuration    = 0f;

    [Header("帧数据 (秒)")]
    [Tooltip("前摇：动画开始到判定帧")]
    public float startupTime  = 0.12f;
    [Tooltip("判定持续帧长")]
    public float activeTime   = 0.08f;
    [Tooltip("后摇：判定结束到动作结束")]
    public float recoveryTime = 0.20f;

    [Header("连段")]
    [Tooltip("此技能打中后可接的下一个技能 key（留空表示无连段）")]
    public string nextComboSkillKey = "";

    [Header("特效 (VFX)")]
    [Tooltip("出招时播放的 Effekseer 特效（比如挥砍轨迹）。留空则这个技能不播放专属特效。" +
        "不同技能可以配不同特效，互不影响——不再是同武器类别共用同一个特效。")]
    public EffekseerEffectAsset swingVFX;
    [Tooltip("swingVFX 生成位置的额外偏移量(角色本地空间，跟着朝向镜像自动翻转)——挥砍\n" +
        "特效的锚点(武器尖端Point Attachment或骨骼+偏移量兜底)是所有挥砍技能共用的，\n" +
        "如果某个技能的动作姿势特殊(比如空中挥砍)导致共用锚点位置对不上，不想连累\n" +
        "其它技能改共用偏移量，就用这个字段单独给这一个技能叠加一次修正。0=不额外\n" +
        "偏移，用共用锚点算出来的原始位置。")]
    public Vector3 swingVfxOffset = Vector3.zero;

    [Header("位移特效（跟随位移的軌跡特效，比如突刺/冲刺）")]
    [Tooltip("跟 swingVFX 不一样：swingVFX 是出招瞬间播一次就结束的静止特效，这个是\n" +
        "軌跡/Trail 类型，会在这个技能触发的位移过程中每帧跟随角色位置更新，画出一条\n" +
        "飘带，用法参照蓄力突刺的冲刺特效(SkillChargeData.dashVfx)。留空则不播放。\n" +
        "跟 isChargeSkill 无关，任何技能（包括非蓄力技，比如闪避接突刺）都能用。")]
    public EffekseerEffectAsset travelVfx;
    [Tooltip("位移特效锚点：绑到武器尖端(Spine骨架里名叫fx_tip的插槽)，或者绑到角色本身\n" +
        "（骨骼所在Transform，可以用下面的偏移量调整位置）。")]
    public ChargeDashVfxAnchorMode travelVfxAnchor = ChargeDashVfxAnchorMode.Character;
    [Tooltip("锚点选「绑到角色」时生效——在角色骨骼位置基础上再加的本地偏移量（X右/\n" +
        "Y上/Z前后）。锚点选「绑到武器尖端」时这个偏移量不生效。")]
    public Vector3 travelVfxCharacterAnchorOffset = Vector3.zero;

    [Header("音效 (SE)")]
    [Tooltip("出招时播放（挥拳/出刀音）。随机取一个，每条素材可以单独设自己的音量。")]
    public SkillSoundEntry[] swingSE  = System.Array.Empty<SkillSoundEntry>();
    [Tooltip("出招SE专属音量倍率，在音量倍率的基础上再乘一次——素材本身很轻的话可以单独" +
        "把出招SE整体调响，不会跟着把命中/打空SE也一起调响。1=不额外调整。")]
    public float swingSEVolume = 1f;
    [Tooltip("命中时播放（撞击/打击音）。随机取一个，每条素材可以单独设自己的音量。")]
    public SkillSoundEntry[] hitSE    = System.Array.Empty<SkillSoundEntry>();
    [Tooltip("打空时播放（破风声）。随机取一个，每条素材可以单独设自己的音量。")]
    public SkillSoundEntry[] whiffSE  = System.Array.Empty<SkillSoundEntry>();
    [Tooltip("音效音量倍率（相对于全局 sfxVolume），三组SE共用这一个基础倍率。")]
    public float seVolume = 1f;

    [Header("多语言")]
    public List<LocalizedTextEntry> localizedNames       = new List<LocalizedTextEntry>();
    public List<LocalizedTextEntry> localizedDescriptions = new List<LocalizedTextEntry>();

    public float TotalDuration => startupTime + activeTime + recoveryTime;
}
