using System;
using UnityEngine;

public enum AppearanceSlotType
{
    [InspectorName("发型")]
    Hair = 0,

    [InspectorName("内衣 / 基础 Skin")]
    InnerSkin = 10
}

[Serializable]
public class ItemAppearanceExtension
{
    public AppearanceSlotType slot = AppearanceSlotType.Hair;
    public string appearanceKey = "";
    public string spineSkinKey = "";
    public bool overrideEquipmentAppearance = false;
}
