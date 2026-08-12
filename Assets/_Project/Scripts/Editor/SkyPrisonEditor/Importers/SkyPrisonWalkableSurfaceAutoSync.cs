using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 让「可站立装饰物 → 地表脚步声」这条链自动保持正确，不需要记着去点菜单。
///
/// 起因：定义资产上填了 walkableSurface，场景里已放置的实例却不会自动跟上——
/// 挂 GroundSurfaceMarker 的逻辑原本只在 RuntimeApplier.ApplyDefinition() 里，
/// 而那个方法唯一入口是右键 ContextMenu，放置和重建流程都不会调它。
/// 结果是碰撞体建好了、marker 始终为空，脚步声查不到地表却毫无报错。
///
/// 这里做两件事：
///   1. 一次性：建层 + 把 WalkableProbe 从三个移动 mask 里剔掉（扫全部预制体，较慢，
///      用 EditorPrefs 版本号守住，只在版本变化时跑）。
///   2. 每次脚本重载 / 打开场景：校正当前已加载场景里的 marker（只扫场景，很便宜）。
///
/// 只 MarkSceneDirty，不替用户保存场景——自动保存别人的场景太越界。
/// 改了什么都会打日志，日志前缀固定，方便事后核对。
/// </summary>
[InitializeOnLoad]
public static class SkyPrisonWalkableSurfaceAutoSync
{
    private const string LogPrefix = "[SkyPrison WalkableSync]";

    // 一次性步骤的版本号。改了一次性逻辑就 +1，让它在所有机器上重跑一次。
    // v2：新增 Obstacle_UnitOnly 层（挡单位、不挡子弹），需要在所有机器上再跑一次建层。
    // v3：创建空气墙那一套资产（材质 / 预制体 / 定义）。
    private const int OneTimeSetupVersion = 3;
    private const string OneTimeSetupPrefKey = "SkyPrison.WalkableProbe.OneTimeSetupVersion";

    static SkyPrisonWalkableSurfaceAutoSync()
    {
        // 静态构造跑在域重载中途，这时候场景不一定加载完，延后一帧。
        EditorApplication.delayCall += RunAll;
        EditorSceneManager.sceneOpened += OnSceneOpened;
    }

    private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
    {
        SyncLoadedScenes();
    }

    private static void RunAll()
    {
        RunOneTimeSetupIfNeeded();
        SyncLoadedScenes();
    }

    private static void RunOneTimeSetupIfNeeded()
    {
        if (EditorPrefs.GetInt(OneTimeSetupPrefKey, 0) >= OneTimeSetupVersion)
            return;

        int fixedMasks = SkyPrisonWalkableProbeLayerSetup.RunSetup(interactive: false);
        if (fixedMasks < 0)
            return; // 没有空闲层槽，别记版本号，下次还要再试。

        // 空气墙是纯工具资产，缺了地图作者就没法摆挡人体积。放在一次性配置里
        // 自动补齐，避免"菜单没点 = 功能不存在"这种静默失败。
        SkyPrisonAirWallAssetCreator.CreateOrRepair();

        EditorPrefs.SetInt(OneTimeSetupPrefKey, OneTimeSetupVersion);
        Debug.Log($"{LogPrefix} 一次性配置完成（v{OneTimeSetupVersion}），修正 {fixedMasks} 个 mask 字段。");
    }

    /// <summary>
    /// 把已加载场景里每个装饰物实例的 GroundSurfaceMarker 对齐到它定义的 walkableSurface。
    /// 只在真的不一致时才动对象，所以每次重载跑一遍不会把场景标脏。
    /// </summary>
    private static void SyncLoadedScenes()
    {
        int added = 0;
        int updated = 0;
        int removed = 0;

        TerrainDecorationRuntimeBinder[] binders =
            Object.FindObjectsByType<TerrainDecorationRuntimeBinder>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (TerrainDecorationRuntimeBinder binder in binders)
        {
            if (binder == null || binder.definition == null)
                continue;

            GameObject go = binder.gameObject;
            if (!go.scene.IsValid())
                continue; // 预制体资产不在这里处理，交给 Builder。

            GroundSurfaceMaterialDefinition surface = binder.definition.walkableSurface;
            GroundSurfaceMarker marker = go.GetComponent<GroundSurfaceMarker>();

            if (surface == null)
            {
                // 残留一个 surfaceDefinition 为空的 marker 比完全没有 marker 更难查：
                // GroundQueryService 会认为这里有地面却没有材质。
                if (marker == null)
                    continue;

                Undo.DestroyObjectImmediate(marker);
                EditorSceneManager.MarkSceneDirty(go.scene);
                removed++;
                continue;
            }

            if (marker == null)
            {
                marker = Undo.AddComponent<GroundSurfaceMarker>(go);
                marker.surfaceDefinition = surface;
                marker.surfaceType = surface.surfaceType;
                EditorUtility.SetDirty(marker);
                EditorSceneManager.MarkSceneDirty(go.scene);
                added++;
                continue;
            }

            if (marker.surfaceDefinition == surface && marker.surfaceType == surface.surfaceType)
                continue;

            Undo.RecordObject(marker, "Sync walkable surface marker");
            marker.surfaceDefinition = surface;
            marker.surfaceType = surface.surfaceType;
            EditorUtility.SetDirty(marker);
            EditorSceneManager.MarkSceneDirty(go.scene);
            updated++;
        }

        if (added == 0 && updated == 0 && removed == 0)
            return;

        Debug.Log(
            $"{LogPrefix} 校正地表标记：新增 {added}、更新 {updated}、移除 {removed}（共扫描 {binders.Length} 个装饰物实例）。" +
            "场景已标脏，记得保存。");
    }

    [MenuItem("Tools/Sky Prison/Ground/Surface/立即校正场景内可站立装饰物标记")]
    private static void SyncNow()
    {
        SyncLoadedScenes();
        Debug.Log($"{LogPrefix} 手动校正完成。");
    }
}
