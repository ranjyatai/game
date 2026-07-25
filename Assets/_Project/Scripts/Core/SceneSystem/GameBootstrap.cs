using UnityEngine;

/// <summary>
/// 游戏全局引导。放在第一个场景（通常是 Bootstrap 或 MainMenu 场景）的任意 GameObject 上。
/// 负责按顺序初始化所有跨场景单例：SaveManager → SceneLoader → LoadingScreenUI。
/// </summary>
public class GameBootstrap : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        // SaveManager
        var saveGo = new GameObject("[SaveManager]");
        saveGo.AddComponent<SaveManager>();
        Object.DontDestroyOnLoad(saveGo);

        // SceneLoader
        var loaderGo = new GameObject("[SceneLoader]");
        loaderGo.AddComponent<SceneLoader>();
        Object.DontDestroyOnLoad(loaderGo);

        // EquipmentRuntime
        var equipGo = new GameObject("[EquipmentRuntime]");
        equipGo.AddComponent<EquipmentRuntime>();
        Object.DontDestroyOnLoad(equipGo);

        // QuickSlotRuntime
        var quickSlotGo = new GameObject("[QuickSlotRuntime]");
        quickSlotGo.AddComponent<QuickSlotRuntime>();
        Object.DontDestroyOnLoad(quickSlotGo);

        // QuickSlotUseController
        var quickSlotUseGo = new GameObject("[QuickSlotUseController]");
        quickSlotUseGo.AddComponent<QuickSlotUseController>();
        Object.DontDestroyOnLoad(quickSlotUseGo);

        // AppearanceRuntime
        var appearGo = new GameObject("[AppearanceRuntime]");
        appearGo.AddComponent<AppearanceRuntime>();
        Object.DontDestroyOnLoad(appearGo);

        // StashRuntime
        var stashGo = new GameObject("[StashRuntime]");
        stashGo.AddComponent<StashRuntime>();
        Object.DontDestroyOnLoad(stashGo);

        // InventorySaveConnector
        var invSaveGo = new GameObject("[InventorySaveConnector]");
        invSaveGo.AddComponent<InventorySaveConnector>();
        Object.DontDestroyOnLoad(invSaveGo);

        // TechTreeRuntime
        var techGo = new GameObject("[TechTreeRuntime]");
        techGo.AddComponent<TechTreeRuntime>();
        Object.DontDestroyOnLoad(techGo);

        // LoadingScreenUI（需要在 SceneLoader 之后，确保能订阅到事件）
        var uiGo = new GameObject("[LoadingScreenUI]");
        uiGo.AddComponent<LoadingScreenUI>();
        Object.DontDestroyOnLoad(uiGo);
    }
}
