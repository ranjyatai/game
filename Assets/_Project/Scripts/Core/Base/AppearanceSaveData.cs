using System;
using System.Collections.Generic;

/// <summary>
/// 角色外观存档数据。存在 PlayerSaveData.appearance 里。
/// </summary>
[Serializable]
public class AppearanceSaveData
{
    /// <summary>当前装备的发型 spineSkinKey。空 = 使用默认。</summary>
    public string equippedHairKey = "";

    /// <summary>当前装备的内衣/基础Skin spineSkinKey。空 = 使用默认。</summary>
    public string equippedInnerSkinKey = "";

    /// <summary>已解锁的外观 key 列表（通过任务/购买/掉落获得）。</summary>
    public List<string> unlockedAppearanceKeys = new List<string>();

    public bool IsUnlocked(string key)
        => !string.IsNullOrEmpty(key) && unlockedAppearanceKeys.Contains(key);

    public void Unlock(string key)
    {
        if (!string.IsNullOrEmpty(key) && !unlockedAppearanceKeys.Contains(key))
            unlockedAppearanceKeys.Add(key);
    }
}
