using System;
using UnityEngine;

public enum GeneralItemType
{
    [InspectorName("普通")]
    Common = 0,

    [InspectorName("消耗品")]
    Consumable = 10,

    [InspectorName("材料")]
    Material = 20,

    [InspectorName("任务物品")]
    Quest = 30,

    [InspectorName("钥匙 / 凭证")]
    KeyItem = 40,

    [InspectorName("投掷物")]
    Throwable = 50,

    [InspectorName("工具")]
    Tool = 60,

    [InspectorName("货币 / 凭证")]
    Currency = 70,

    [InspectorName("特殊")]
    Special = 80
}

[Serializable]
public class ItemGeneralExtension
{
    public GeneralItemType type = GeneralItemType.Common;
    public string useEffectKey = "";
    public bool canPutInQuickSlot = false;
    public bool consumeOnUse = true;
    [Tooltip("标记为复活道具：玩家 HP 归零时优先弹出使用确认")]
    public bool isReviveItem = false;
    [Tooltip("复活后无敌保护时长（秒），0 = 无保护）")]
    public float reviveProtectionDuration = 5f;

    [Tooltip("拿起/放下时播放哪种材质音效")]
    public ItemSoundMaterial soundMaterial = ItemSoundMaterial.Default;

}
