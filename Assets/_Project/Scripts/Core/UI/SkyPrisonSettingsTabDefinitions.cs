/// <summary>
/// 设置窗口左侧 7 个分类的唯一定义源——本地化 key + 中文兜底文案。
/// SettingsWindowUI（运行时侧栏）和 MainMenuSettingsWindow（编辑器"界面设置"工具的
/// "设置界面书签"页签）都读这一份，不要各自维护一份列表，不然两边顺序/数量对不上
/// 时书签图标会错位。
/// </summary>
public static class SkyPrisonSettingsTabDefinitions
{
    public static readonly string[] Keys = {
        "ui_settings_tab_display", "ui_settings_tab_graphics", "ui_settings_tab_audio",
        "ui_settings_tab_language", "ui_settings_tab_keymouse", "ui_settings_tab_gamepad",
        "ui_settings_tab_gameplay"
    };

    public static readonly string[] FallbackLabels = { "显示", "画面", "音频", "语言", "键鼠", "手柄", "辅助" };

    public const int Count = 7;
}
