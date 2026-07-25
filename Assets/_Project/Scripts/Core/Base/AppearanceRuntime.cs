using System;
using System.Collections.Generic;
using Spine;
using Spine.Unity;
using UnityEngine;

/// <summary>
/// 角色外观运行时。单例，DontDestroyOnLoad。
/// 管理发型 / 内衣 Skin 的切换，并将变更持久化到 PlayerSaveData。
///
/// Spine 多 Skin 合并方式：
///   创建一个合并 Skin，把基础 Skin + 发型 Skin + 内衣 Skin 叠加进去，
///   再 SetSkin 到骨骼上。切换时重建合并 Skin 即可。
/// </summary>
public class AppearanceRuntime : MonoBehaviour
{
    public static AppearanceRuntime Instance { get; private set; }

    // ── 事件 ──────────────────────────────────────────────────────────────
    public static event Action OnAppearanceChanged;

    // ── 玩家骨骼引用（场景加载后由玩家角色注册）──────────────────────────
    private SkeletonAnimation _skeleton;

    // ── 默认 Skin key（由 UnitDefinition 或项目约定固定）─────────────────
    private const string DefaultHairKey      = "hair_default";
    private const string DefaultInnerSkinKey = "innerskin_default";

    // 武器皮肤不走 SaveManager 持久化——装备了哪把武器这件事本来就已经由
    // EquipmentRuntime/背包存档管理了，这里只是运行时"当前应该往合并Skin里加哪个
    // 武器皮肤"的临时状态，重新装备/卸下时 WeaponVisualRuntime 直接调 SetWeaponSkin
    // 覆盖，不用再存一份重复的持久化数据。
    private string _weaponSkinKey;

    // 武器染色（3个染色区域，乘算叠色）——同样不持久化在这里，装备的是哪把武器/
    // 用什么配色本来就是 InventoryItemEntry.dyeColors 在管（工坊改色也是改那份数据），
    // 这里只是"当前应该往武器插槽上乘算哪三个颜色"的临时状态。null＝不染色（比如
    // 卸下武器，或者这把武器没配置染色插槽）。
    private Color[] _weaponDyeColors;

    // 武器身上负责染色的插槽，名字里带这三个花括号标签之一——同一个标签下允许有
    // 好几个插槽（比如刀刃一块、护手一块），美术画图层时打好标签，这里不用逐个配置。
    private static readonly string[] WeaponDyeTags = { "{weapon_dye_01}", "{weapon_dye_02}", "{weapon_dye_03}" };

    // 防具皮肤——跟武器不同，同一时间最多5件（头/上装/下装/手/鞋）可以同时叠加，
    // 不是"只有一件生效"，所以不能用单个字段，得按槽位分别记。同样不持久化在这里，
    // 装备了什么本来就是 EquipmentRuntime 在管，这里只是"当前应该往合并Skin里加哪些
    // 防具皮肤"的临时状态，ArmorVisualRuntime 装备/卸下时直接覆盖对应槽位。
    private readonly Dictionary<EquipmentSlotType, string> _armorSkinKeys = new Dictionary<EquipmentSlotType, string>();

    // 防具染色——同理，每个防具槽位各自独立的3色染色数据。
    private readonly Dictionary<EquipmentSlotType, Color[]> _armorDyeColors = new Dictionary<EquipmentSlotType, Color[]>();

    // 防具染色插槽标签按槽位区分前缀（{head_dye_01}/{upperbody_dye_01}/...），不能跟
    // 武器共用同一套 {weapon_dye_0X} 标签——防具最多5件同时装备，如果标签不分槽位，
    // 装两件防具时后处理的那件会把先处理那件的染色插槽也覆盖掉（只要两边插槽名字
    // 都恰好带了同一个标签）。武器同一时间只有一把生效，不存在这个冲突，所以维持
    // 原来的 {weapon_dye_0X} 不变。
    private static readonly Dictionary<EquipmentSlotType, string[]> ArmorDyeTags = new Dictionary<EquipmentSlotType, string[]>
    {
        { EquipmentSlotType.Head,      new[] { "{head_dye_01}",      "{head_dye_02}",      "{head_dye_03}" } },
        { EquipmentSlotType.UpperBody, new[] { "{upperbody_dye_01}", "{upperbody_dye_02}", "{upperbody_dye_03}" } },
        { EquipmentSlotType.LowerBody, new[] { "{lowerbody_dye_01}", "{lowerbody_dye_02}", "{lowerbody_dye_03}" } },
        { EquipmentSlotType.Hands,     new[] { "{hands_dye_01}",     "{hands_dye_02}",     "{hands_dye_03}" } },
        { EquipmentSlotType.Shoes,     new[] { "{shoes_dye_01}",     "{shoes_dye_02}",     "{shoes_dye_03}" } },
    };

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ── 注册玩家骨骼（玩家 GameObject 激活时调用）─────────────────────────

    public void RegisterPlayerSkeleton(SkeletonAnimation skeleton)
    {
        _skeleton = skeleton;
        ApplyCurrentAppearance();
    }

    public void UnregisterPlayerSkeleton()
    {
        _skeleton = null;
    }

    // ── 查询接口 ──────────────────────────────────────────────────────────

    public string CurrentHairKey      => SaveManager.Player?.appearance?.equippedHairKey      ?? DefaultHairKey;
    public string CurrentInnerSkinKey => SaveManager.Player?.appearance?.equippedInnerSkinKey ?? DefaultInnerSkinKey;

    public bool IsUnlocked(string appearanceKey)
        => SaveManager.Player?.appearance?.IsUnlocked(appearanceKey) ?? false;

    // ── 解锁外观（任务奖励/购买时调用）──────────────────────────────────

    public void UnlockAppearance(string appearanceKey)
    {
        if (SaveManager.Player?.appearance == null) return;
        SaveManager.Player.appearance.Unlock(appearanceKey);
        SaveManager.Save();
    }

    // ── 切换外观（美容室 UI 调用）────────────────────────────────────────

    /// <summary>切换发型。key 必须已解锁。</summary>
    public bool SetHair(string spineSkinKey)
    {
        var ap = SaveManager.Player?.appearance;
        if (ap == null) return false;
        if (!ap.IsUnlocked(spineSkinKey) && spineSkinKey != DefaultHairKey)
        {
            Debug.LogWarning($"[AppearanceRuntime] 发型未解锁：{spineSkinKey}");
            return false;
        }
        ap.equippedHairKey = spineSkinKey;
        ApplyCurrentAppearance();
        SaveManager.Save();
        return true;
    }

    /// <summary>切换内衣/基础Skin。key 必须已解锁。</summary>
    public bool SetInnerSkin(string spineSkinKey)
    {
        var ap = SaveManager.Player?.appearance;
        if (ap == null) return false;
        if (!ap.IsUnlocked(spineSkinKey) && spineSkinKey != DefaultInnerSkinKey)
        {
            Debug.LogWarning($"[AppearanceRuntime] 内衣未解锁：{spineSkinKey}");
            return false;
        }
        ap.equippedInnerSkinKey = spineSkinKey;
        ApplyCurrentAppearance();
        SaveManager.Save();
        return true;
    }

    /// <summary>切换当前叠进合并Skin的武器皮肤——由 WeaponVisualRuntime 在武器
    /// 装备/卸下/切换主副手时调用。传 null/空字符串＝不叠加武器层（卸下武器时用）。</summary>
    public void SetWeaponSkin(string spineSkinKey)
    {
        _weaponSkinKey = spineSkinKey;
        ApplyCurrentAppearance();
    }

    /// <summary>设置当前武器的3个染色区域颜色（乘算叠色）——由 WeaponVisualRuntime
    /// 在武器装备/卸下/切换主副手时调用，传的是该件装备实例自己的
    /// InventoryItemEntry.dyeColors（工坊改过色就是改这份数据，不是配置表里的默认色）。
    /// 传 null＝不染色（卸下武器，或者这把武器没有染色插槽）。</summary>
    public void SetWeaponDye(Color[] dyeColors)
    {
        _weaponDyeColors = dyeColors;
        ApplyCurrentAppearance();
    }

    /// <summary>切换指定防具槽位当前叠进合并Skin的皮肤——由 ArmorVisualRuntime 在防具
    /// 装备/卸下时调用。传 null/空字符串＝不叠加这个槽位（卸下该件防具时用）。</summary>
    public void SetArmorSkin(EquipmentSlotType slot, string spineSkinKey)
    {
        if (string.IsNullOrEmpty(spineSkinKey))
            _armorSkinKeys.Remove(slot);
        else
            _armorSkinKeys[slot] = spineSkinKey;
        ApplyCurrentAppearance();
    }

    /// <summary>设置指定防具槽位的3个染色区域颜色（乘算叠色）——由 ArmorVisualRuntime
    /// 在防具装备/卸下时调用，传的是该件装备实例自己的 InventoryItemEntry.dyeColors。
    /// 传 null＝不染色（卸下该件防具，或者这件防具没有染色插槽）。</summary>
    public void SetArmorDye(EquipmentSlotType slot, Color[] dyeColors)
    {
        if (dyeColors == null)
            _armorDyeColors.Remove(slot);
        else
            _armorDyeColors[slot] = dyeColors;
        ApplyCurrentAppearance();
    }

    // ── 应用到 Spine 骨骼 ────────────────────────────────────────────────

    /// <summary>将当前外观设置应用到玩家骨骼。无骨骼时静默返回。</summary>
    public void ApplyCurrentAppearance()
    {
        if (_skeleton == null) return;

        var skeleton = _skeleton.Skeleton;
        var data     = skeleton.Data;

        // 构建合并 Skin
        var combined = new Skin("combined");

        TryAddSkin(combined, data, CurrentInnerSkinKey, DefaultInnerSkinKey);
        TryAddSkin(combined, data, CurrentHairKey,      DefaultHairKey);
        if (!string.IsNullOrEmpty(_weaponSkinKey))
            TryAddSkin(combined, data, _weaponSkinKey, null);
        foreach (var kv in _armorSkinKeys)
        {
            if (!string.IsNullOrEmpty(kv.Value))
                TryAddSkin(combined, data, kv.Value, null);
        }

        skeleton.SetSkin(combined);
        InvokeSetupPose(skeleton); // 会把所有插槽颜色重置回setup pose的白色，染色必须在这之后再乘算叠上去
        ApplyDyeTags(skeleton, WeaponDyeTags, _weaponDyeColors);
        foreach (var kv in ArmorDyeTags)
        {
            _armorDyeColors.TryGetValue(kv.Key, out Color[] colors);
            ApplyDyeTags(skeleton, kv.Value, colors);
        }
        _skeleton.AnimationState.Apply(skeleton);

        OnAppearanceChanged?.Invoke();
    }

    // 扫描当前叠合骨架里带指定花括号标签的插槽，按标签乘算叠上对应染色区域的颜色。
    // 同一个标签可以对应好几个插槽（一起被同一个颜色驱动），标签本身不用额外配置——
    // 运行时按插槽名字扫描是这套染色约定的自然结果（同DyeColorScheme三区域约定，见
    // ItemEquipmentExtension 注释）。武器/每个防具槽位各自传自己专属的标签数组，
    // 互不覆盖（见 ArmorDyeTags 上的说明）。
    private static void ApplyDyeTags(Skeleton skeleton, string[] tags, Color[] dyeColors)
    {
        if (dyeColors == null || dyeColors.Length == 0) return;

        var slots = skeleton.Slots;
        for (int s = 0; s < slots.Count; s++)
        {
            Slot slot = slots.Items[s];
            string n = slot.Data.Name;
            for (int i = 0; i < tags.Length && i < dyeColors.Length; i++)
            {
                if (!n.Contains(tags[i])) continue;
                slot.Pose.SetColor(dyeColors[i]);
                break;
            }
        }
    }

    // fallback 传 null 表示"没有兜底"（比如武器皮肤——找不到就什么都不叠加，不像
    // 发型/内衣那样有一个必然存在的默认皮肤兜底）。SkeletonData.FindSkin 对 null
    // 参数会直接抛 ArgumentNullException，不能无脑往里传，得先判空跳过。
    private static void TryAddSkin(Skin combined, SkeletonData data, string key, string fallback)
    {
        Skin skin = data.FindSkin(key);
        if (skin == null)
        {
            if (!string.IsNullOrEmpty(fallback)) skin = data.FindSkin(fallback);
            if (skin == null)
            {
                Debug.LogWarning($"[AppearanceRuntime] 角色骨架里找不到Skin「{key}」（也没有可用的兜底），这一层不会叠加。");
                return;
            }
        }
        combined.AddSkin(skin);
    }

    // 兼容不同 Spine 版本的 SetupPose 调用（方法名因版本而异）
    private static void InvokeSetupPose(Spine.Skeleton skeleton)
    {
        var type = skeleton.GetType();
        foreach (var name in new[] { "SetupPoseSlots", "SetSlotsToSetupPose", "SetToSetupPose", "SetupPose" })
        {
            var m = type.GetMethod(name,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public   |
                System.Reflection.BindingFlags.NonPublic,
                null, System.Type.EmptyTypes, null);
            if (m == null) continue;
            m.Invoke(skeleton, null);
            return;
        }
    }
}
