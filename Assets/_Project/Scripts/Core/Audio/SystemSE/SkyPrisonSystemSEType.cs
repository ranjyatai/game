using UnityEngine;

/// <summary>
/// 系统 UI 音效类型枚举。
/// 新增类型在此处添加，同步在 SkyPrisonSystemSETable 里挂 clip 即可，调用侧无需改动。
/// </summary>
public enum SkyPrisonSystemSEType
{
    [InspectorName("无")]
    None = 0,

    /// <summary>确定 / 使用 / 交互</summary>
    [InspectorName("确认 / 交互")]
    Confirm,

    /// <summary>取消 / 关闭 / 返回</summary>
    [InspectorName("取消 / 返回")]
    Cancel,

    /// <summary>系统禁止 / 操作不可用（如 GCD 中、背包满）</summary>
    [InspectorName("禁止 / 不可用")]
    Forbidden,

    /// <summary>切换 / 导航（如左右翻页、标签页切换）</summary>
    [InspectorName("切换 / 导航")]
    Switch,

    /// <summary>窗口 / 面板打开</summary>
    [InspectorName("窗口打开")]
    Open,

    /// <summary>窗口 / 面板关闭</summary>
    [InspectorName("窗口关闭")]
    Close,

    /// <summary>拾取 / 获得物品</summary>
    [InspectorName("拾取 / 获得")]
    Pickup,

    /// <summary>丢弃物品落地（从背包丢出，物品掉落到地面）</summary>
    [InspectorName("丢弃 / 落地")]
    DropToGround,

    /// <summary>装备防具——武器/防具/快捷道具三条通道分开，防具穿上的反馈感跟武器
    /// 上手/快捷道具指定应该是不同质感的音效。</summary>
    [InspectorName("装备防具")]
    EquipArmor,

    /// <summary>卸下防具——跟 EquipArmor 是同一动作的两个方向，通常配不同 clip。</summary>
    [InspectorName("卸下防具")]
    UnequipArmor,

    /// <summary>提示 / 通知（弱提示，比 Confirm 轻）</summary>
    [InspectorName("提示 / 通知")]
    Notice,

    /// <summary>错误 / 失败（比 Forbidden 重，用于操作后发现失败）</summary>
    [InspectorName("错误 / 失败")]
    Error,

    /// <summary>复活 / 使用复活道具成功</summary>
    [InspectorName("复活成功")]
    Revive,

    /// <summary>整理 / 背包排序</summary>
    [InspectorName("整理 / 排序")]
    Tidy,

    /// <summary>濒死循环环境音（由 PlayerDeathReviveUI 开启/关闭，不用 Play，用 PlayLoop/StopLoop）</summary>
    [InspectorName("濒死环境音（循环）")]
    NearDeath,

    /// <summary>把物品指定到快捷物品槽——跟 EquipArmor（穿装备）是不同的动作，不共用同一个
    /// 音效。需要在 SkyPrisonSystemSETable 里单独配 clip。</summary>
    [InspectorName("指定为快捷物品")]
    QuickSlotAssign,

    /// <summary>取消快捷物品槽的指定——跟 QuickSlotAssign 是同一动作的两个方向。</summary>
    [InspectorName("取消快捷物品指定")]
    UnassignQuickSlot,

    /// <summary>装备武器——跟防具用同一个 Equip 类型区分开，武器上手需要更有分量感的
    /// 独立音效，不跟穿防具共用。</summary>
    [InspectorName("装备武器")]
    EquipWeapon,

    /// <summary>卸下武器——跟 EquipWeapon 是同一个动作的两个方向，通常会配不同 clip
    /// （拔出/收回），所以单独给一个类型而不是复用 EquipWeapon。</summary>
    [InspectorName("卸下武器")]
    UnequipWeapon,

    /// <summary>开场"按任意键继续"——标题画面从AnyKey提示进入正式菜单的那一下，
    /// 通常需要比普通Confirm更有仪式感的独立音效，不跟菜单里到处都在用的通用确认音共用。</summary>
    [InspectorName("开场按任意键")]
    TitlePressAnyKey,

    /// <summary>战斗中滚轮切换主/副武器——跟 Switch（菜单翻页/标签切换）是不同性质的
    /// 反馈：这个是高频的战斗操作，需要短促带机械感的独立音效，用菜单那种UI点击音
    /// 会让切枪听起来像在翻菜单，削弱手感。</summary>
    [InspectorName("武器切换（滚轮）")]
    WeaponSwitch,

    /// <summary>商店结账成功——付款那一下需要专门的支付音效，不跟通用Confirm共用，
    /// 得在 SkyPrisonSystemSETable 里单独配 clip。追加在枚举末尾，不能插到中间，
    /// 插中间会把后面所有类型的隐式序号往后挪一位，SkyPrisonSystemSETable已经
    /// 序列化好的clip配置会全部错位。</summary>
    [InspectorName("购买 / 支付")]
    Purchase,
}
