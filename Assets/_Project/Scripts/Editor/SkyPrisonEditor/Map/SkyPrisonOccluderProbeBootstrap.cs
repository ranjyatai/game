using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 遮挡探测碰撞体的一次性接入：建层配 mask → 打开需要的定义 → 重建场景实例 → 保存。
///
/// 做成自动执行而不是留一份「请依次点这四个菜单」的清单：这四步有严格先后顺序，
/// 漏掉或者顺序错了，表现都是「什么都没变」——而且不会报错。
/// 尤其是「重建实例」那一步，改定义不回溯已放置的实例，这一整轮反复栽在这上面。
///
/// 用 EditorPrefs 版本号守住，只跑一次。要重跑就手动执行下面那个菜单。
/// </summary>
[InitializeOnLoad]
public static class SkyPrisonOccluderProbeBootstrap
{
    private const string LogPrefix = "[SkyPrison OccluderProbe]";
    private const string VersionKey = "SkyPrison.OccluderProbeBootstrap.Version";
    private const int Version = 1;

    static SkyPrisonOccluderProbeBootstrap()
    {
        EditorApplication.delayCall += RunOnce;
    }

    private static void RunOnce()
    {
        if (EditorPrefs.GetInt(VersionKey, 0) >= Version)
            return;

        EditorPrefs.SetInt(VersionKey, Version);
        Run();
    }

    [MenuItem("Tools/Sky Prison/Map/前景遮挡/一键接入（建层 + 开定义 + 重建实例）")]
    public static void Run()
    {
        // 编译刚结束、场景还没就绪时跑会白跑一趟，而且版本号已经写进去了不会重试。
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += Run;
            return;
        }

        if (SceneManager.sceneCount == 0 || !SceneManager.GetSceneAt(0).isLoaded)
        {
            Debug.Log($"{LogPrefix} 场景还没加载，跳过。要接入请手动跑一次这个菜单。");
            return;
        }

        // 1. 建层 + 把 OccluderProbe 从移动 mask 里剔除、加进遮挡射线 mask。
        SkyPrisonWalkableProbeLayerSetup.RunSetup(interactive: false);

        // 2. 给「要遮挡但没碰撞体」的定义打开探测开关。
        SkyPrisonFrontOccluderAudit.EnableProbeWhereNeeded();

        // 3. 重建场景实例——定义改了不回溯已放置的实例，这一步不能省。
        // 类名带 _V1 后缀，和文件名不一致——照文件名写会编译不过。
        SkyPrisonOcclusionStructureAuditAndRebuild_V1.RebuildAllSceneThroughBuilder();

        // 4. 保存。前三步都只是把场景改脏，不存的话下次打开全白做。
        bool saved = EditorSceneManager.SaveOpenScenes();

        Debug.Log(
            $"{LogPrefix} 一键接入完成。场景保存：{(saved ? "成功" : "失败或被取消")}。\n" +
            "接着跑「检查场景里的遮挡代理」核对结果，然后进 Play 走到草后面看效果。");
    }
}
