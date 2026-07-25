using UnityEngine;

/// <summary>
/// 设置窗口左侧每个分类可选的书签图标——按 SkyPrisonSettingsTabDefinitions 的顺序
/// 一一对应，留空的项就不显示图标、也不占位置（SettingsWindowUI.BuildSidebar 里
/// 只在这里有值时才腾图标位置）。在 Tools/界面设置 的"设置界面书签"页签里编辑。
///
/// 字段类型是 Texture2D 不是 Sprite——直接拖 PNG 进来就能用，不用先把 Texture Type
/// 改成"Sprite (2D and UI)"、也不用管 Sprite Mode 是 Single 还是 Multiple。渲染那边
/// (SettingsWindowUI.BuildSidebar) 对应用 RawImage 而不是 Image，天生就吃 Texture2D。
/// </summary>
[CreateAssetMenu(menuName = "Sky Prison/UI/Settings Sidebar Icon Settings", fileName = "SettingsSidebarIconSettings")]
public class SettingsSidebarIconSettings : ScriptableObject
{
    public Texture2D[] tabIcons = new Texture2D[SkyPrisonSettingsTabDefinitions.Count];

    public Texture2D GetIcon(int tabIndex)
    {
        if (tabIcons == null || tabIndex < 0 || tabIndex >= tabIcons.Length) return null;
        return tabIcons[tabIndex];
    }
}
