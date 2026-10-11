using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// 进图时校验 RenderSettings（天空盒/环境光/雾）跟 MapDefinition 里配置的环境设置
/// 是否一致，不一致就直接改过来。
///
/// 为什么需要这个：ApplyEnvironmentToScene 是编辑器工具，要靠人手动点按钮才会把
/// MapDefinition 上配好的环境预设值写进场景的 RenderSettings。选了预设、甚至天空盒
/// 材质都拖对了，只要没点那个按钮，场景文件里存的还是旧的 RenderSettings——
/// 这类"编辑器配置改了、场景里悄悄还是旧值"的坑今天已经踩过好几次（战争迷雾材质、
/// 距离模糊校准……），这里加一道运行时自愈，不依赖记得点按钮。
///
/// 只在场景加载时校验一次，不是每帧——环境设置不会在一局游戏里自己漂移，
/// 没必要按帧比对。
/// </summary>
public static class SkyPrisonMapEnvironmentRuntimeSync
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;

        // 首次进 Boot/主菜单也走一遍，覆盖"直接从这张场景启动 Play"的情况——
        // sceneLoaded 事件只在之后的场景切换里触发，第一张已加载完成的场景不会
        // 收到这个回调。
        SyncCurrentScene();
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => SyncCurrentScene();

    /// <summary>
    /// 供 SceneLoader 在读条流程里显式调用——不依赖 SceneManager.sceneLoaded 这个
    /// 被动事件"大概率"在正确的时机触发。读条会经历「场景激活→等待
    /// SignalReady→等玩家输入→揭幕」好几个阶段，环境校正必须在"揭幕"之前
    /// 确定性地跑完，不能靠事件顺序去赌——这里把它做成一个可以被精确插入到
    /// 读条流程里、时机完全可控的显式调用。
    /// </summary>
    public static void SyncNow() => SyncCurrentScene();

    private static void SyncCurrentScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        MapEnvironmentRegistry registry = MapEnvironmentRegistry.LoadOrNull();
        if (registry == null)
            return;

        MapEnvironmentRegistry.Entry entry = registry.GetForScene(scene.name, scene.path);
        if (entry == null)
            return; // 不是地图场景（主菜单、加载场景等），不该有对应条目，跳过。

        bool mismatch =
            RenderSettings.skybox != entry.skyboxMaterial ||
            RenderSettings.ambientLight != entry.ambientColor ||
            !Mathf.Approximately(RenderSettings.reflectionIntensity, entry.reflectionIntensity) ||
            RenderSettings.fog != entry.enableSceneFog;

        if (!mismatch)
            return;

        Debug.LogWarning($"[SkyPrisonMapEnvironmentRuntimeSync] 场景「{scene.name}」的 RenderSettings 跟" +
                          "MapDefinition 配置的环境设置不一致（大概率是编辑器改完忘了点「应用环境到场景」），" +
                          "已在进图时自动改正。");

        RenderSettings.skybox = entry.skyboxMaterial;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = entry.ambientColor;
        RenderSettings.reflectionIntensity = entry.reflectionIntensity;
        RenderSettings.fog = entry.enableSceneFog;
        RenderSettings.fogColor = entry.sceneFogColor;
        RenderSettings.fogStartDistance = entry.fogStartDistance;
        RenderSettings.fogEndDistance = entry.fogEndDistance;

        // 天空盒材质换了之后，环境光照（尤其是 Skybox 环境反射探针）需要重新烘焙
        // 一次才会跟着变，否则物体表面的反射还是用旧天空盒算出来的。
        DynamicGI.UpdateEnvironment();
    }
}
