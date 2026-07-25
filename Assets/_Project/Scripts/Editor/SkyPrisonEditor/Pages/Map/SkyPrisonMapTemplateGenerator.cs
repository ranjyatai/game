using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 从 StillVault（余穹基地，目前唯一验证过"能正常站人、跑得起来"的地图）提取一份干净的
/// 空地图模板，覆盖掉旧的、靠 CreateMap() 手写节点拼出来的那套骨架——旧骨架漏了好几样
/// StillVault 实际在用的东西（GroundRoot 下面从来没放过真正的 GroundTerrain、完全没有
/// __SkyPrisonMapEnvironment 那套光照/后处理、没有 AudioListenerRoot），新建的地图因此
/// 连地面碰撞体都没有，人当然站不住。
///
/// 用法：Tools → Sky Prison → Map → 从 StillVault 生成新地图模板。
/// 跑完之后请在 Unity 里亲自检查一遍生成的模板场景，确认地形/相机/光照都还在，
/// 再决定要不要接着把 SkyPrisonMapEditorUtility.CreateMap() 改成从这个模板复制。
/// </summary>
public static class SkyPrisonMapTemplateGenerator
{
    private const string SourceScenePath   = "Assets/_Project/Maps/Hub/StillVault/Still Vault/Still Vault.unity";
    private const string TemplateFolder    = "Assets/_Project/Maps/_MapTemplate";
    private const string TemplateScenePath = TemplateFolder + "/MapTemplate.unity";

    // 这几个容器节点里装的是 StillVault 自己摆的具体地形装饰/建筑实例——是"内容"，
    // 不是"骨架"，新地图不应该带着这些东西。路径用 "父物体名/子物体名" 从场景根开始找，
    // 找到就把它的子物体全部清空（容器本身保留，方便以后地图编辑器往里面摆新内容）。
    private static readonly string[] ContentContainerPaths =
    {
        "WorldRoot/GroundRoot/GroundStamps",
        "WorldRoot/GroundRoot/GroundSplines",
        "WorldRoot/BackgroundRoot/StructureRoot",
    };

    [MenuItem("Tools/Sky Prison/Map/从 StillVault 生成新地图模板")]
    public static void GenerateTemplate()
    {
        if (!System.IO.File.Exists(SourceScenePath))
        {
            Debug.LogError($"[SkyPrisonMapTemplateGenerator] 找不到源场景：{SourceScenePath}");
            return;
        }

        if (!EditorUtility.DisplayDialog(
            "生成地图模板",
            "即将打开 Still Vault 场景，复制一份并清理成新地图模板，用来替换掉 CreateMap() 现在用的那套手写骨架（那套骨架缺地面/光照，新地图站不住人）。\n\n" +
            "会先保存你当前打开的场景（如果有未保存修改，请先手动保存或放弃），过程中会切换当前打开的场景，跑完后自动切回 Still Vault。\n\n继续吗？",
            "继续", "取消"))
        {
            return;
        }

        string activeScenePath = EditorSceneManager.GetActiveScene().path;

        if (!AssetDatabase.IsValidFolder(TemplateFolder))
            AssetDatabase.CreateFolder("Assets/_Project/Maps", "_MapTemplate");

        // 1) 打开源场景，另存一份到模板路径——saveAsCopy 不会改动源场景本身。
        Scene srcScene = EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Single);
        bool savedCopy = EditorSceneManager.SaveScene(srcScene, TemplateScenePath, saveAsCopy: true);
        if (!savedCopy)
        {
            Debug.LogError("[SkyPrisonMapTemplateGenerator] 另存模板场景失败。");
            RestoreOriginalScene(activeScenePath);
            return;
        }

        // 2) 打开刚存出来的模板副本，在副本上做清理，不碰源场景。
        Scene templateScene = EditorSceneManager.OpenScene(TemplateScenePath, OpenSceneMode.Single);

        int clearedContainers = 0;
        int destroyedObjects  = 0;
        foreach (string path in ContentContainerPaths)
        {
            GameObject container = FindByPath(templateScene, path);
            if (container == null)
            {
                Debug.LogWarning($"[SkyPrisonMapTemplateGenerator] 模板里没找到容器节点：{path}（跳过，可能是 StillVault 结构变了）");
                continue;
            }

            int childCount = container.transform.childCount;
            for (int i = childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(container.transform.GetChild(i).gameObject);
                destroyedObjects++;
            }
            clearedContainers++;
        }

        EditorSceneManager.MarkSceneDirty(templateScene);
        EditorSceneManager.SaveScene(templateScene);

        Debug.Log($"[SkyPrisonMapTemplateGenerator] 模板生成完成：{TemplateScenePath}，清空了 {clearedContainers} 个内容容器、删除了 {destroyedObjects} 个具体实例。地面/相机/光照/系统节点原样保留，请在 Unity 里亲自检查一遍。");

        // 3) 切回原来打开的场景，不让用户莫名其妙地停在模板场景里。
        RestoreOriginalScene(activeScenePath);

        EditorUtility.DisplayDialog(
            "生成完成",
            $"模板已生成：{TemplateScenePath}\n\n清空了 {clearedContainers} 个内容容器（{destroyedObjects} 个具体实例）。\n请打开这个场景亲自看一眼，确认地面、相机、光照都还在——确认没问题后，再决定要不要把 CreateMap() 改成从这个模板复制。",
            "好的");
    }

    private static void RestoreOriginalScene(string path)
    {
        if (!string.IsNullOrEmpty(path) && path != TemplateScenePath)
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
    }

    private static GameObject FindByPath(Scene scene, string path)
    {
        string[] segments = path.Split('/');
        GameObject[] roots = scene.GetRootGameObjects();

        Transform current = null;
        foreach (var root in roots)
        {
            if (root.name == segments[0]) { current = root.transform; break; }
        }
        if (current == null) return null;

        for (int i = 1; i < segments.Length; i++)
        {
            Transform next = current.Find(segments[i]);
            if (next == null) return null;
            current = next;
        }
        return current.gameObject;
    }
}
