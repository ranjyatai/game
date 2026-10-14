using UnityEngine;

/// <summary>
/// 挂在 Bootstrap 场景的唯一 GameObject 上。
/// Bootstrap 场景是 Build Settings index=0 的第一个场景。
/// GameBootstrap 的 RuntimeInitializeOnLoadMethod 已经把所有单例拉起来了，
/// 这里只负责等一帧确认初始化完毕，然后跳到主界面。
/// </summary>
public class BootstrapSceneController : MonoBehaviour
{
    [Tooltip("主界面场景名，必须与 Build Settings 里的场景名一致")]
    [SerializeField] private string mainMenuScene = "MainMenu";

    private void Start()
    {
        // 先播开头商标 / 片头列表（天空囚笼/片头/商标动画列表），玩家点一下跳过全部；列表为空则直接进主界面
        SkyPrisonIntroPlayer.PlayThen(() => SceneLoader.LoadScene(mainMenuScene));
    }
}
