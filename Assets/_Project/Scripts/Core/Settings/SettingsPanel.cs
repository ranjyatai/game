using UnityEngine;

/// <summary>
/// 设置面板主控制器。
/// 负责 Tab 切换和各子面板的显示/隐藏。
/// 视觉层暂缓，框架优先。
/// </summary>
public class SettingsPanel : MonoBehaviour
{
    public enum Tab { Display, Audio, Controls, Gameplay }

    // ── 子面板（Inspector 绑定，或由 SettingsPanelBuilder 自动注入）───────
    [SerializeField] private SettingsDisplayPanel  displayPanel;
    [SerializeField] private SettingsAudioPanel    audioPanel;
    [SerializeField] private SettingsControlsPanel controlsPanel;
    [SerializeField] private SettingsGameplayPanel gameplayPanel;

    private Tab _activeTab = Tab.Audio;

    private void OnEnable()
    {
        // 打开时从磁盘刷新一次数据
        displayPanel?.Refresh();
        audioPanel?.Refresh();
        controlsPanel?.Refresh();
        gameplayPanel?.Refresh();

        SwitchTab(_activeTab);
    }

    public void SwitchTab(Tab tab)
    {
        _activeTab = tab;
        displayPanel? .SetVisible(tab == Tab.Display);
        audioPanel?   .SetVisible(tab == Tab.Audio);
        controlsPanel?.SetVisible(tab == Tab.Controls);
        gameplayPanel?.SetVisible(tab == Tab.Gameplay);
    }

    /// <summary>外部按钮用：保存并关闭面板。</summary>
    public void ApplyAndClose()
    {
        displayPanel? .Apply();
        audioPanel?   .Apply();
        controlsPanel?.Apply();
        gameplayPanel?.Apply();

        SaveManager.SaveSettings();
        gameObject.SetActive(false);
    }

    /// <summary>放弃修改，重新从磁盘加载并关闭。</summary>
    public void CancelAndClose()
    {
        SaveManager.LoadSettings();
        gameObject.SetActive(false);
    }

    // ── Tab 快捷入口（供 UI Button.onClick 绑定）─────────────────────────
    public void ShowDisplay()  => SwitchTab(Tab.Display);
    public void ShowAudio()    => SwitchTab(Tab.Audio);
    public void ShowControls() => SwitchTab(Tab.Controls);
    public void ShowGameplay() => SwitchTab(Tab.Gameplay);
}
