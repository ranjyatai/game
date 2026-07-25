using UnityEngine;

/// <summary>
/// 挂在 LoadingScene 场景里的唯一 GameObject 上。
/// Awake 时创建相机和 LoadingScreenUI，Start 时触发 SceneLoader 开始异步加载目标场景。
/// </summary>
public class LoadingSceneController : MonoBehaviour
{
    private void Awake()
    {
        // 纯黑相机（UI Canvas ScreenSpaceOverlay 不依赖相机，但场景需要至少一个相机）
        var camGo = new GameObject("[LoadingCamera]");
        var cam   = camGo.AddComponent<Camera>();
        cam.clearFlags       = CameraClearFlags.SolidColor;
        cam.backgroundColor  = Color.black;
        cam.cullingMask      = 0;          // 不渲染任何 3D 层，只负责清屏
        cam.depth            = -1;

        // 用 GameObject 名字查找而不是 C# 类型/静态字段判重：
        // 脚本改动后如果没有完整退出 Play 模式重新编译（没有真正 Domain Reload），
        // 新旧两代 LoadingScreenUI 类型会各自持有独立的静态字段，谁都看不到对方，
        // 导致两个实例都以为自己是唯一的，各自淡出黑幕、互相打架造成闪烁。
        // GameObject.Find 按名字在场景里搜索，不受 C# 类型身份问题影响，更可靠。
        if (GameObject.Find("[LoadingScreenUI]") == null)
        {
            var uiGo = new GameObject("[LoadingScreenUI]");
            uiGo.AddComponent<LoadingScreenUI>();
        }
    }

    private void Start()
    {
        SceneLoader.BeginLoad();
    }
}
