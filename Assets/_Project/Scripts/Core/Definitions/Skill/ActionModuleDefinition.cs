using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 战斗模组：把一组技能绑定到一种武器姿态。
/// 武器的 weaponModuleKey 对应一个 WeaponCombatModule.moduleKey。
/// 无武器时使用 moduleKey = "unarmed" 的模组。
/// </summary>
[CreateAssetMenu(menuName = "Sky Prison/Skills/Action Module Definition", fileName = "AM_NewModule")]
public class WeaponCombatModule : ScriptableObject
{
    [Header("基础")]
    public string moduleKey   = "unarmed";
    public string displayName = "徒手";

    [TextArea(2, 4)]
    public string description = "";

    [Header("轻攻击连段")]
    [Tooltip("按顺序依次循环，最后一个打完回到第一个")]
    public List<SkillDefinition> lightAttackCombo = new List<SkillDefinition>();

    [Header("重攻击")]
    public SkillDefinition heavyAttack;

    [Header("闪避接突刺")]
    [Tooltip("闪避快结束时可以打断闪避、无缝衔接的专属突刺技能，留空表示这个模组没有这个衔接（比如空手）。")]
    public SkillDefinition dodgeThrustAttack;
    [Tooltip("闪避播放进度超过整段闪避时长的这个比例之后，才允许打断闪避衔接上面这个突刺技能\n" +
        "（比如0.6表示要播完60%再到闪避结束这段窗口内才能接）。不同武器/技能的闪避手感可能\n" +
        "不一样，所以这个比例是每个模组自己配的，不是全局固定值。")]
    [Range(0f, 1f)]
    public float dodgeThrustOpenAfterFraction = 0.6f;

    [Header("攻击取消后撤步")]
    [Tooltip("勾选后，这个模组的攻击在判定帧结束(后摇阶段)时可以按闪避键取消攻击、无缝\n" +
        "衔接一个固定后撤步——不看输入方向，固定沿角色当前朝向的正后方冲一下，播放\n" +
        "dodge_back，且全程保持当前朝向不转身。不勾选则这个模组的攻击不能被闪避键\n" +
        "取消（比如空手）。")]
    public bool allowAttackCancelDodgeBack = false;

    [Header("移动限制")]
    [Tooltip("勾选后，这个模组的轻/重攻击在角色正处于潜行(Sneak)或奔跑(Sprint)状态时\n" +
        "不允许触发——按了没反应，得先松开潜行/奔跑键回到普通走路/站立才能出招。\n" +
        "普通走路状态不受影响，边走边打依然允许。留空(不勾)表示这个模组不限制，\n" +
        "任何移动状态都能出招（近战武器默认行为）。")]
    public bool blockAttackWhileSprintOrSneak = false;

    [Header("连发")]
    [Tooltip("勾选后，按住轻攻击键不放会持续自动重复触发轻攻击连段（每次上一发动画播完立即\n" +
        "补发下一发），不需要每发都单独点一下——用于枪械这类连发武器。不勾选则轻攻击\n" +
        "只在按键那一帧触发一次（近战武器默认行为）。")]
    public bool autoFireLightAttackWhileHeld = false;

    [Header("空中攻击（剑气弹幕）")]
    [Tooltip("跳跃在空中阶段时按攻击键触发的专属空中攻击技能，留空表示这个模组没有空中\n" +
        "攻击（比如空手）。每次跳跃只能触发一次，落地后重新计次。")]
    public SkillDefinition aerialAttack;

    [Header("战斗音效（武器级默认，技能可单独覆盖）")]
    [Tooltip("出招音（挥拳/出刀音）。随机取一个，每条素材可以单独设自己的音量。")]
    public SkillSoundEntry[] swingSE  = System.Array.Empty<SkillSoundEntry>();
    [Tooltip("命中音（撞击/打击音）。随机取一个，每条素材可以单独设自己的音量。")]
    public SkillSoundEntry[] hitSE    = System.Array.Empty<SkillSoundEntry>();
    [Tooltip("打空音（破风声）。随机取一个，每条素材可以单独设自己的音量。")]
    public SkillSoundEntry[] whiffSE  = System.Array.Empty<SkillSoundEntry>();
    [Tooltip("换弹音——绑在 Reload 这个 Spine 事件上（跟弹夹掉落 SpawnMagazineDropProp\n" +
        "同一个触发点），随机取一个，每条素材可以单独设自己的音量。留空表示这个模组\n" +
        "换弹没有专门的音效（比如近战武器/不需要弹药的武器）。")]
    public SkillSoundEntry[] reloadSE = System.Array.Empty<SkillSoundEntry>();
    [Tooltip("打空音（扳机空响）——弹匣已经空了还按攻击键时播放，代替一次正常的开火，\n" +
        "随机取一个。留空表示打空没有专门的提示音（比如近战武器/不需要弹药的武器）。")]
    public SkillSoundEntry[] dryFireSE = System.Array.Empty<SkillSoundEntry>();
    [Tooltip("音效音量倍率（相对于全局 sfxVolume）。")]
    public float seVolume = 1f;

    [Header("手部姿势叠加")]
    [Tooltip("这个模组对应的手型动画名字（Spine 里独立的 Hand_xxx 动画，比如 Hand_Sword/" +
        "Hand_Gun）。播在专属的手部叠加轨道上，跟身体动画（站立/走/跑/闪避等）叠加，" +
        "前提是那些身体动画完全不碰手指骨骼。留空表示这个模组不需要覆盖手型" +
        "（沿用当前骨架默认姿势）。")]
    public string handPoseAnimationKey = "";

    [Header("换弹演出（弹夹脱手掉落）")]
    [Tooltip("换弹动画里脱夹那一帧生成的掉落弹夹外观——一个独立于 Spine 骨架的世界空间预制体，" +
        "落地后自毁。Spine 那边只需要在同一帧把手上弹夹的 Attachment 隐藏，两边靠事件同帧触发对齐，" +
        "不需要额外做骨骼换绑。留空表示这个模组没有掉落演出（比如空手/近战武器）。")]
    public GameObject magazineDropPrefab;
    [Tooltip("掉落弹夹相对角色根节点的生成偏移（本地坐标，X 会按角色当前朝向自动镜像，Y 是抬高多少）。")]
    public Vector2 magazineDropSpawnOffset = new Vector2(0.3f, 1.1f);
    [Tooltip("掉落弹夹相对生成点的落地偏移（同样 X 按朝向镜像），决定掉落弧线往哪个方向、掉多远。")]
    public Vector2 magazineDropLandOffset = new Vector2(0.15f, -0.9f);

    [Header("locomotion 动画覆盖（留空 = 使用默认）")]
    public LocoAnimOverride locomotionOverride = new LocoAnimOverride();

    [Header("武器尖端锚点（Spine里fx_tip Point Attachment的Slot名字）")]
    [Tooltip("枪口火光/弹壳/子弹出生点这些要绑在武器尖端的效果，从这里指定的Slot名字找\n" +
        "锚点世界坐标——不同武器皮肤的fx_tip Slot名字都不一样（比如京极五式军铳左右手\n" +
        "分别叫 {fx_tip}gun_kyougokuType05_L / _R），不能用一个写死的名字通吃所有武器。\n" +
        "填一个就固定用这一个（单手武器）；双持武器（比如双枪）填两个，每次开火会按\n" +
        "顺序交替使用（这次左手这次右手），不会一直只从同一侧发射。留空则退回角色\n" +
        "骨骼根节点(基本等于脚下，效果位置会明显不对，不建议留空)。")]
    public string[] weaponTipSlotNames = System.Array.Empty<string>();

    [Header("枪械视觉（外购VFX包，普通GameObject/ParticleSystem，不是Effekseer）")]
    [Tooltip("枪口火光Prefab——每次gun事件在武器尖端(fx_tip)锚点生成一份、自动销毁。\n" +
        "留空表示这个模组没有枪口火光（比如近战武器）。")]
    public GameObject muzzleFlashPrefab;
    [Tooltip("枪口火光整体缩放倍率——外购VFX包大多按真实枪械比例做的粒子大小(比如\n" +
        "0.05~0.1世界单位)，跟这个项目角色的世界单位比例对不上，看起来会偏小，\n" +
        "用这个倍率放大，不用改素材本身。1=不缩放。")]
    public float muzzleFlashScale = 1f;
    [Tooltip("弹壳抛出Prefab——装备这个模组时在武器尖端锚点常驻实例化一份（不是每发\n" +
        "都new），之后每次gun事件调用它身上的ShellEjectionEmitter.Spawn()补一发。\n" +
        "留空表示这个模组不弹壳（比如近战武器/不需要弹壳的枪）。")]
    public GameObject shellEjectPrefab;
    [Tooltip("弹壳整体缩放倍率——跟枪口火光同一个问题，外购VFX包的弹壳网格按真实\n" +
        "尺寸做的，跟这个项目角色的世界单位比例对不上，容易小到肉眼看不清。\n" +
        "1=不缩放。")]
    public float shellEjectScale = 1f;
}

[Serializable]
public class LocoAnimOverride
{
    [Tooltip("留空表示不覆盖，继续用 SpineAnimationDriver 的默认 key")]
    public string idle   = "";
    public string walk   = "";
    public string run    = "";
    public string jump   = "";
    public string land   = "";
    public string crouch = "";

    public bool HasAnyOverride =>
        !string.IsNullOrEmpty(idle)   ||
        !string.IsNullOrEmpty(walk)   ||
        !string.IsNullOrEmpty(run)    ||
        !string.IsNullOrEmpty(jump)   ||
        !string.IsNullOrEmpty(land)   ||
        !string.IsNullOrEmpty(crouch);

    public string Resolve(string defaultKey, string overrideKey) =>
        string.IsNullOrEmpty(overrideKey) ? defaultKey : overrideKey;
}
