using System;
using System.Collections.Generic;
using UnityEngine;

public enum EquipmentSlotType
{
    [InspectorName("武器")]
    Weapon = 0,

    [InspectorName("副武器")]
    WeaponSecondary = 5,

    [InspectorName("头部")]
    Head = 10,

    [InspectorName("上装")]
    UpperBody = 20,

    [InspectorName("下装")]
    LowerBody = 30,

    [InspectorName("手部")]
    Hands = 40,

    [InspectorName("鞋子")]
    Shoes = 50
}

public enum WeaponCategoryType
{
    [InspectorName("无 / 非武器")]
    None = 0,

    [InspectorName("剑")]
    Sword = 10,

    [InspectorName("链锯")]
    Chainsaw = 20,

    [InspectorName("双枪")]
    DualGun = 30,

    [InspectorName("自定义 Key")]
    Custom = 999
}

[Serializable]
public class DyeColorScheme
{
    [Tooltip("按染色区域1/2/3顺序，一套完整配色。")]
    public Color[] colors = new Color[3] { Color.white, Color.white, Color.white };
}

[Serializable]
public class ItemEquipmentExtension
{
    [Header("装备基础")]
    public EquipmentSlotType slot = EquipmentSlotType.Weapon;
    public float moveSpeedMultiplier = 1f;
    public float lpCostMultiplier = 1f;

    [Header("脚步 / 感知")]
    public float footstepNoiseMultiplier = 1f;
    public float sneakNoiseMultiplier = 1f;
    public float sprintNoiseMultiplier = 1f;
    public float landingNoiseMultiplier = 1f;

    [Header("武器专属")]
    public WeaponCategoryType weaponCategory = WeaponCategoryType.None;
    public string weaponTypeKey = "";
    [Tooltip("装备此武器时切换到的战斗模组。优先使用，留空则退回到单位默认模组。")]
    public WeaponCombatModule combatModule;
    public string weaponModuleKey = "";

    [Tooltip("这把武器对应角色自己Spine骨架里的哪个Skin名字——武器现在直接画在角色骨架\n" +
             "本身里（美术在Axia_base.spine项目里把这把武器所有图层建成一个具名Skin），\n" +
             "不再是独立的Spine文件/预制体。装备时 WeaponVisualRuntime 直接给角色骨架切\n" +
             "皮肤（Skeleton.SetSkin），不用实例化任何GameObject，角色自己的排序/遮挡/\n" +
             "图层渲染管线本来就适用。留空=不切皮肤，沿用角色当前皮肤（比如还没做美术\n" +
             "的武器）。")]
    public string weaponSkinName = "";

    [Tooltip("这把武器判定形状对应角色自己骨架里的插槽名字（Boundingbox附件，比如\n" +
             "\"weapon_sword_heavySpade\"）——攻击判定从角色徒手判定（arm_L_hand2/\n" +
             "arm_R_hand2）切到这个插槽读取的形状。留空=装备这把武器不切换判定，\n" +
             "沿用角色徒手判定。")]
    public string weaponJudgmentSlotName = "";

    [Tooltip("战斗HUD右下角武器切换条用的剪影图——白色剪影、透明背景的PNG，跟角色\n" +
             "手上具体拿的皮肤/染色无关，只是给玩家一眼认出\"现在用的是哪把武器\"。\n" +
             "素材统一放在 Assets/_Project/Icon/Equipment/WeaponSilhouette 文件夹下。")]
    public Sprite weaponSilhouette;

    [Tooltip("挥砍特效（SkyPrisonSwordSlashTrailController）锚点相对武器骨骼的局部\n" +
             "偏移——每把武器的刀刃长度/骨骼朝向都不一样，这个数值必须按具体这把武器\n" +
             "实测调，不能所有剑共用同一个值（不同长度的剑用同一个偏移量会错位）。\n" +
             "默认值是照 weapon_sword_heavySpade 实测出来的，新武器需要重新在\n" +
             "Play模式里对着Gizmo小球调准。")]
    public Vector3 weaponSlashEffectTipOffset = new Vector3(-1f, 0f, 0f);

    // 口径只分小/大两级（AmmoCaliberType），不做"每把枪独立弹药"那种硬核颗粒度——
    // 现在只有双枪这一种热武器吃小口径，大口径先留给DEMO之后才会加入的炮。
    // 近战武器（剑/链锯）usesAmmo 留 false 就完全不受这套影响。
    [Header("防具专属")]
    [Tooltip("这件防具对应角色自己Spine骨架里的哪个Skin名字——用法跟 weaponSkinName 完全一致：\n" +
             "装备时角色骨架会叠加这个Skin层（跟发型/内衣/武器皮肤同一套合并机制，见\n" +
             "AppearanceRuntime类注释）。同一时间最多5件防具（头/上装/下装/手/鞋）可以\n" +
             "同时叠加，互不影响。留空=不切皮肤，沿用角色当前皮肤（比如还没做美术的防具）。")]
    public string armorSkinName = "";

    [Header("弹药（仅热武器需要）")]
    [Tooltip("勾上后，攻击前会检查/扣减背包里对应口径的弹药——不够就打不出去。\n" +
             "近战武器（剑/链锯）不用勾。")]
    public bool usesAmmo = false;

    [Tooltip("这把武器吃哪个口径的弹药，要跟弹药物品的 ItemAmmoExtension.caliber 一致。")]
    public AmmoCaliberType ammoCaliber = AmmoCaliberType.SmallCaliber;

    [Tooltip("每次攻击（轻/重各算一次）消耗的弹药数量。")]
    [Min(1)]
    public int ammoPerShot = 1;

    [Tooltip("弹匣容量——攻击消耗的是弹匣里的弹药（InventoryItemEntry.currentMagazineAmmo），\n" +
        "不是直接扣背包。弹匣打空后攻击会失败，需要按换弹键(默认R)从背包补充，补充量= \n" +
        "min(弹匣空位, 背包里这个口径的备用弹药数量)。")]
    [Min(1)]
    public int magazineSize = 12;

    [Tooltip("换弹耗时（秒）——按下换弹键后角色进入换弹状态锁这么久，期间不能攻击/闪避\n" +
        "（可以走动），耗时内被命中会打断换弹（弹药不会被填进弹匣，需要重新按键）。")]
    [Min(0.05f)]
    public float reloadDurationSeconds = 1.2f;

    [Header("耐久度")]
    [Tooltip("最大耐久度。0 = 该装备不参与耐久系统（如饰品）")]
    [Min(0)]
    public int maxDurability = 100;

    [Tooltip("每次受到伤害时扣除的耐久点数（防具）或每次攻击命中扣除的（武器）")]
    [Min(0)]
    public int durabilityLossPerHit = 1;

    [Header("改装槽位（武器/防具专属）")]
    [Tooltip("该装备拥有的改装槽列表。组件物品按 slotTypeKey 匹配后才能安装。")]
    public List<ModSlotDefinition> modSlots = new List<ModSlotDefinition>();

    // 染色区域→插槽的归属关系不用在这里手动配置一张映射表——直接靠Spine插槽/图层
    // 命名来体现：任何名字里带 "{weapon_dye_01}"/"{weapon_dye_02}"/"{weapon_dye_03}"
    // 这种花括号标签的插槽，运行时扫描骨架时会自动按标签分组，同一个标签下可以有
    // 好几个散落在武器身上不相邻的插槽（比如刀刃一小块、护手一小块，只要材质类别
    // 一样就打同一个标签），一起被同一个颜色驱动。美术画图层时把标签打对，不用额外
    // 在这里配置——运行时按名字扫描本来就是这套约定的自然结果。
    [Header("染色（3个染色区域）")]
    [Tooltip("掉落时可能出现的默认配色方案列表。填1个=固定用这一套；填多个=掉落时\n" +
             "从里面随机抽1套。每套方案只需要设定3个染色区域各自的颜色。列表为空时\n" +
             "退回中性白（乘法叠色下=不改变原图，等于该件装备不染色）。")]
    public List<DyeColorScheme> defaultDyeColorSchemes = new List<DyeColorScheme>();

    /// <summary>按 defaultDyeColorSchemes 随机抽一套掉落配色（掉落时调用一次）。
    /// 列表为空时返回中性白三色。</summary>
    public Color[] GetRandomDefaultDyeColors()
    {
        if (defaultDyeColorSchemes != null && defaultDyeColorSchemes.Count > 0)
        {
            var scheme = defaultDyeColorSchemes[UnityEngine.Random.Range(0, defaultDyeColorSchemes.Count)];
            if (scheme?.colors != null && scheme.colors.Length == 3)
                return (Color[])scheme.colors.Clone();
        }
        return new Color[3] { Color.white, Color.white, Color.white };
    }

    // 跟 UnitDefinition.parameterValues 用同一个 UnitParameterValue 类型、同一份
    // BattleParameterDatabase.coreAttributes 做字典——装备加成不用另开一套字段，
    // 角色核心属性有什么（攻击/防御/暴击率/抗性……）装备就能对应加什么，编辑器里
    // 直接照 BattleParameterDatabase 的清单一行行填，不用每加一个新核心属性就要
    // 回来给 ItemEquipmentExtension 补一个新字段。
    [Header("属性加成（装备后对角色核心属性的加成）")]
    public List<UnitParameterValue> statBonuses = new List<UnitParameterValue>();

    // 强化熔炉计算契合度时要用——跟素材那边 ItemMaterialAlchemyExtension.affinityTags
    // 共用同一个 MaterialAffinityTagWeight 类型，标签key要对得上
    // MaterialAffinityDatabase.tags 里注册的那些。留空=这件装备没有特殊偏好，
    // 丢什么素材进去都只按素材自己的基础效力算，不吃标签契合度加成。
    [Header("强化熔炉（偏好标签）")]
    [Tooltip("这件装备偏好的相性标签——丢进强化熔炉的素材如果携带匹配的标签，\n" +
             "契合度更高，成功率加成更明显。")]
    public List<MaterialAffinityTagWeight> alchemyPreferredTags = new List<MaterialAffinityTagWeight>();
}
