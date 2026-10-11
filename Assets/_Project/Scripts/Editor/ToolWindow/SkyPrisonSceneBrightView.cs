using UnityEditor;
using UnityEngine;

/// <summary>
/// 场景视图「全局照明」开关：只在 Scene 视图里用一盏跟着相机的辅助光把地图照亮，方便暗图摆放物体。
///
/// 实现就是切 SceneView.sceneLighting（Scene 工具栏上的灯泡）：关掉场景灯光 = 改用默认辅助光。
/// 它只影响 Scene 视图，不改任何光源、环境光、雾，也不会写进场景文件；Game 视图和 Play 里的
/// 画面完全不受影响，所以关掉它就是游戏里真实的暗度。
/// 地图放置工具的标题栏上有按钮调用这里。
/// </summary>
public static class SkyPrisonSceneBrightView
{
    /// <summary>当前是否处在「全局照明」（= Scene 视图不使用场景里的灯）。</summary>
    public static bool IsActive
    {
        get
        {
            SceneView sv = SceneView.lastActiveSceneView;
            return sv != null && !sv.sceneLighting;
        }
    }

    [MenuItem("天空囚笼/场景视图 全局照明 开关 %#l", false, 3)]
    public static void Toggle()
    {
        SetActive(!IsActive);
    }

    public static void SetActive(bool bright)
    {
        foreach (SceneView sv in SceneView.sceneViews)
        {
            sv.sceneLighting = !bright;
            sv.Repaint();
        }
    }
}
