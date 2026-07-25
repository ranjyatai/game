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
    [Tooltip("音效音量倍率（相对于全局 sfxVolume）。")]
    public float seVolume = 1f;

    [Header("locomotion 动画覆盖（留空 = 使用默认）")]
    public LocoAnimOverride locomotionOverride = new LocoAnimOverride();
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
