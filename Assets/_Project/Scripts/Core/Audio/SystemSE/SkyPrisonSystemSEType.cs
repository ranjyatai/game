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

    /// <summary>工房修理成功——扣钱把装备修满那一下，需要专门的"敲打/机械维修"质感
    /// 音效，不跟通用Confirm共用。同上，追加在末尾，不能插中间。</summary>
    [InspectorName("修理成功")]
    Repair,

    /// <summary>NPC对话里"接受任务"二次确认框弹出——AXIA接收到一条新指令这个仪式感
    /// 时刻，需要比通用Open更有分量/更带战术终端感的独立音效，不跟其它窗口打开共用。
    /// 同上，追加在末尾，不能插中间。</summary>
    [InspectorName("接受任务确认框打开")]
    QuestAcceptConfirmOpen,

    // ── 进图地名牌（SkyPrisonMapNameCard）的几个时刻，追加在末尾（不能插中间，原因同上）。
    // 在 SkyPrisonSystemSETable 里给对应类型配 clip 就会响；没配就是静音。──

    /// <summary>地名牌出现的那一刻（牌子淡入开始）。</summary>
    [InspectorName("地名牌 · 出现")]
    MapNameCardShow,

    /// <summary>名字每打出一个字（连续触发，建议配很短的音，可以多放几个 clip 随机）。副标每个字也用它。</summary>
    [InspectorName("地名牌 · 打字（每字）")]
    MapNameCardType,

    /// <summary>名字全部打完、色差爆开的那一下。</summary>
    [InspectorName("地名牌 · 色差爆开")]
    MapNameCardAberration,

    /// <summary>名字下面的横线展开。</summary>
    [InspectorName("地名牌 · 横线展开")]
    MapNameCardLine,

    /// <summary>地名牌开始淡出消失。</summary>
    [InspectorName("地名牌 · 淡出")]
    MapNameCardFadeOut,

    /// <summary>出发转场：四周向中心凹陷、吸入黑屏的那一整段。</summary>
    [InspectorName("出发转场 · 凹陷吸入")]
    DepartWarp,

    // ── AFP 适应系统 / 能力 / 夜视 / 装备档位(追加在末尾，不能插中间，原因同上) ──
    [InspectorName("AFP · 完成部署成功")] AfpDeploy,
    [InspectorName("AFP · 预加点")] AfpAdd,
    [InspectorName("AFP · 不能再加(资源 / 条件不足)")] AfpInsufficient,
    [InspectorName("能力 · 致命子弹命中")] AbilityLethalHit,
    [InspectorName("能力 · 特效力场豁免")] AbilityFieldBlock,
    [InspectorName("能力 · 特效力场恢复完毕")] AbilityFieldRecharged,
    [InspectorName("能力 · 生命保险触发")] AbilityInsurance,
    [InspectorName("能力 · 回转回复 LP")] AbilityCycle,
    [InspectorName("能力 · 异常追伤触发")] AbilityBurst,
    [InspectorName("能力 · 篡夺者回血")] AbilityUsurper,
    [InspectorName("夜视仪 · 开启")] NightVisionOn,
    [InspectorName("夜视仪 · 关闭")] NightVisionOff,
    [InspectorName("夜视仪 · 电量耗尽")] NightVisionDepleted,
    [InspectorName("夜视仪 · 敌对单位进入视线")] NightVisionTarget,
    [InspectorName("装备档位 · 变重")] EquipTierUp,
    [InspectorName("装备档位 · 变轻")] EquipTierDown,

    // ── 选关(RAID 地图) ──
    [InspectorName("选关 · 打开地图")] MapOpen,
    [InspectorName("选关 · 关闭地图")] MapClose,
    [InspectorName("选关 · 切换 / 移动光标(轻)")] MapScroll,
    [InspectorName("选关 · 选中据点")] MapSelect,
    [InspectorName("选关 · 昼夜切换")] MapDayNight,
    [InspectorName("选关 · 出发")] MapDepart,

    // ── 屏幕横带(SkyPrisonScreenBanner：战术研修解放等)的几个时刻(追加在末尾，不能插中间，原因同上) ──
    [InspectorName("横带 · 展开")] BannerOpen,
    [InspectorName("横带 · 反色闪烁(每一闪)")] BannerFlash,
    [InspectorName("横带 · 右下角坐标发信")] BannerSignal,
    [InspectorName("横带 · 收起")] BannerClose,
}
