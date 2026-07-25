using System;
using System.Collections.Generic;
using UnityEngine;

public enum StatusDurationType
{
    Instant = 0,
    Timed = 1,
    Permanent = 2,
}

public enum StatusDurationUpdateMode
{
    Override = 0,
    Keep = 1,
    Additive = 2,
}

public enum StatusStackUpdateMode
{
    Override = 0,
    Keep = 1,
    AddOne = 2,
    AddValue = 3,
}

public enum StatusGrantMode
{
    Direct = 0,
    ByAccumulationThreshold = 1,
    PersistentPassive = 2,
    UnlockedByProgression = 3,
}

public enum StatusAttributeOperator
{
    Add = 0,
    Multiply = 1,
    Override = 2,
}

public enum StatusAttributeStackScalingMode
{
    None = 0,
    Linear = 1,
    MultiplyByStack = 2,
}

public enum StatusDotTargetResource
{
    HP = 0,
    LP = 1,
}

public enum StatusDotValueMode
{
    Fixed = 0,
    TargetMaxResourcePercent = 1,
    TargetCurrentResourcePercent = 2,
    OwnerAttributeRatio = 3,
    TargetAttributeRatio = 4,
}

public enum StatusDotStackMode
{
    None = 0,
    LinearAdd = 1,
    MultiplyByStack = 2,
}

[Serializable]
public class StatusAttributeModifierDefinition
{
    public bool enabled = true;
    public string attributeKey = "";
    public string attributeDisplayName = "";
    public BattleValueType attributeValueType = BattleValueType.Float;

    public StatusAttributeOperator attributeOperator = StatusAttributeOperator.Add;
    public StatusAttributeStackScalingMode stackScaling = StatusAttributeStackScalingMode.Linear;

    public float value = 0f;
    public bool boolValue = false;

    public string note = "";
}

[CreateAssetMenu(
    fileName = "StatusDefinition",
    menuName = "Sky Prison/Status Definition",
    order = 1410)]
public class StatusDefinition : ScriptableObject
{
    [Header("基础信息")]
    public string statusId = "new_status";
    public string displayName = "新状态";
    public string note = "";

    [TextArea(4, 8)]
    public string description = "";

    [Header("多语言名称")]
    public List<LocalizedTextEntry> localizedNames = new List<LocalizedTextEntry>();

    [Header("多语言描述")]
    public List<LocalizedTextEntry> localizedDescriptions = new List<LocalizedTextEntry>();

    [Header("基础显示")]
    public bool isStandard = false;
    public bool isBuff = false;
    public bool isHidden = false;
    public bool showInHud = true;
    public Sprite icon;

    [Header("赋予方式")]
    public StatusGrantMode grantMode = StatusGrantMode.Direct;
    public string accumulationSourceKey = "";
    public float accumulationThreshold = 0f;

    [Header("持续时间")]
    public StatusDurationType durationType = StatusDurationType.Timed;
    public float baseDuration = 0f;
    public StatusDurationUpdateMode durationUpdateMode = StatusDurationUpdateMode.Override;
    public float maxDuration = 0f;

    [Header("叠层规则")]
    public bool canStack = false;
    public int baseStack = 1;
    public int maxStack = 1;
    public StatusStackUpdateMode stackUpdateMode = StatusStackUpdateMode.AddOne;

    [Header("基础属性修正")]
    public List<StatusAttributeModifierDefinition> attributeModifiers = new List<StatusAttributeModifierDefinition>();

    [Header("DOT设定")]
    public bool enableDot = false;
    public float dotTickInterval = 1f;
    public StatusDotTargetResource dotTargetResource = StatusDotTargetResource.HP;
    public StatusDotValueMode dotValueMode = StatusDotValueMode.Fixed;
    public float dotBaseValue = 0f;
    public float dotPercentValue = 0f;
    public string dotReferenceAttributeKey = "";
    public string dotReferenceAttributeDisplayName = "";
    public BattleValueType dotReferenceAttributeValueType = BattleValueType.Float;
    public float dotAttributeRatio = 1f;
    public StatusDotStackMode dotStackMode = StatusDotStackMode.None;
    public float dotStackAddValue = 0f;
    public bool dotAffectedByResistance = true;
    public bool dotCanKill = true;
    public string dotDamageTypeKey = "";
    public string dotDamageTypeDisplayName = "";

    [Header("触发器生命周期")]
    public string onApplyTriggerKey = "";
    public string onTickTriggerKey = "";
    public float tickInterval = 0f;
    public string onRemoveTriggerKey = "";
    public string onExpireTriggerKey = "";

    [Header("特效生命周期")]
    public string onApplyVfxKey = "";
    public string persistentVfxKey = "";
    public string tickVfxKey = "";
    public float tickVfxInterval = 0f;
    public string onRemoveVfxKey = "";
    public string onExpireVfxKey = "";

    [Header("音效生命周期")]
    public string onApplySfxKey = "";
    public string onRemoveSfxKey = "";
    public string onExpireSfxKey = "";

    [Header("Regain 设定")]
    public bool enableRegain = false;
    public float regainRatio = 0f;

    [Header("状态描边特效")]
    [Tooltip("状态存在期间是否让角色持续显示发光描边（比如灼烧）。多个状态同时启用时，取角色当前生效状态列表里第一个启用了描边的。")]
    public bool useStatusOutline = false;
    [Tooltip("描边发光颜色，建议用HDR高亮色，配合Bloom后处理才会有向外扩散的光晕感。G/B通道尽量压得比R低很多（拉开饱和度余量），否则整圈描边糊在一起容易被Bloom+色调映射的高光滚降压成发白。")]
    [ColorUsage(true, true)]
    public Color statusOutlineColor = new Color(4f, 0.25f, 0.03f, 1f);
    [Tooltip("描边宽度（像素），描的是角色整体外轮廓（跟遮挡描边同一套屏幕空间蒙版算法），不是逐部件贴图边缘。")]
    [Range(1f, 12f)]
    public float statusOutlineWidthPixels = 3f;
    [Tooltip("描边粗细/明暗随噪波变化的幅度。0=等宽等亮的实线，越大变化越明显。")]
    [Range(0f, 1f)]
    public float statusOutlineWidthVariance = 0.6f;
    [Tooltip("描边噪波流动速度，让粗细/明暗随时间沿轮廓流动，而不是静止不动。")]
    public float statusOutlineFlowSpeed = 0.6f;
    [Tooltip("描边噪波密度，越大噪波颗粒越细碎、越不容易察觉流动感；越小颗粒越大越明显。")]
    public float statusOutlineNoiseScale = 0.3f;
    [Tooltip("状态刚赋予时，描边从无到有的淡入时长（秒），带缓动曲线，不是硬切。")]
    public float statusOutlineFadeInSeconds = 0.35f;
    [Tooltip("状态结束/被清除时，描边从有到无的淡出时长（秒），带缓动曲线。通常比淡入更长，像火焰熄灭那种慢慢暗下去的过程。")]
    public float statusOutlineFadeOutSeconds = 0.6f;

    [Header("状态效果响应闪烁")]
    [Tooltip("这个状态每次触发DOT tick（真的跳一下伤害）时，角色本体是否全身半透明呼吸闪一下。")]
    public bool useStatusFlash = false;
    [Tooltip("闪烁色调，呼吸峰值时角色本体颜色会乘上这个色调（保留原有明暗细节，只是整体颜色偏向这个色调，不是拿纯色盖上去）。白色(1,1,1)等于不生效，想要偏红效果把G/B通道调低就行（R保持1不变，G/B调暗，相当于把非红色成分过滤掉）。")]
    public Color statusFlashColor = new Color(1f, 0.45f, 0.45f, 1f);
    [Tooltip("一次闪烁的总时长（秒），强度按sin曲线在这段时间内0→1→0起伏一次。")]
    public float statusFlashDuration = 0.3f;
    [Tooltip("呼吸峰值时的半透明程度（0=不透明，1=完全透明）。")]
    [Range(0f, 1f)]
    public float statusFlashAlphaDip = 0.35f;

}
