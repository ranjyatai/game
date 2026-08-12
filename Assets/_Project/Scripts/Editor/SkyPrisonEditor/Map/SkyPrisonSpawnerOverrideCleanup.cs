using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 把场景里所有孵化器的视觉模型朝向拉回标准值（见 SkyPrisonSpawnerVisualOrientation）。
///
/// 新放置的孵化器在放置时就写死了，不需要这个工具。它是给之前已经摆好的实例用的——
/// 那些实例上可能带着 +90 之类的 override。
///
/// 只改视觉模型的 localRotation。不碰位置（那是每个实例自己的意义所在），
/// 不碰缩放（缩放有可能是按场景需要故意调的，不像旋转那样一定是错的）。
/// </summary>
public static class SkyPrisonSpawnerOverrideCleanup
{
    private const string LogPrefix = "[SkyPrison Spawner]";
    private const string MenuRoot = "Tools/Sky Prison/Map/孵化器/";

    [MenuItem(MenuRoot + "1. 检查视觉模型朝向")]
    public static void Audit() => Run(apply: false);

    [MenuItem(MenuRoot + "2. 校正视觉模型朝向（保留位置和缩放）")]
    public static void Fix() => Run(apply: true);

    /// <summary>
    /// 把预制体本身改成标准朝向。
    /// 不改的话，放置时 Apply 会在每个实例上写一个 override——功能上没错，
    /// 但每个孵化器都带一条蓝线覆盖，久了没人分得清哪些 override 是有意的。
    /// </summary>
    [MenuItem(MenuRoot + "3. 把预制体本身改成标准朝向")]
    public static void NormalizePrefab()
    {
        const string prefabPath = "Assets/_Project/Data/Definitions/Core/Spawners/UnitSpawner.prefab";

        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
        if (prefabRoot == null)
        {
            Debug.LogError($"{LogPrefix} 打不开预制体：{prefabPath}");
            return;
        }

        try
        {
            int changed = SkyPrisonSpawnerVisualOrientation.Apply(prefabRoot.transform);
            if (changed == 0)
            {
                Debug.Log(
                    $"{LogPrefix} 预制体已经是标准朝向 " +
                    $"{SkyPrisonSpawnerVisualOrientation.ModelLocalEuler}，无需改动。");
                return;
            }

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log(
                $"{LogPrefix} 预制体已改为 {SkyPrisonSpawnerVisualOrientation.ModelLocalEuler}，" +
                $"改动 {changed} 个节点。已有实例若带旧的 override，再跑一次「2」。");
        }
        finally
        {
            // LoadPrefabContents 开的是一个隐藏场景，不 Unload 会一直挂着。
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private static void Run(bool apply)
    {
        List<UnitSpawner> spawners = CollectSceneInstances<UnitSpawner>();
        if (spawners.Count == 0)
        {
            Debug.Log($"{LogPrefix} 场景里没有孵化器实例。");
            return;
        }

        Vector3 standard = SkyPrisonSpawnerVisualOrientation.ModelLocalEuler;
        Quaternion target = Quaternion.Euler(standard);

        var sb = new StringBuilder();
        int offCount = 0;
        int fixedCount = 0;
        int noVisual = 0;

        foreach (UnitSpawner spawner in spawners)
        {
            if (spawner == null)
                continue;

            Transform visualRoot = spawner.transform.Find("VisualRoot");
            if (visualRoot == null || visualRoot.childCount == 0)
            {
                // 不静默跳过：没有 VisualRoot 说明结构不对，比朝向错更值得知道。
                noVisual++;
                sb.AppendLine($"  {spawner.name}：找不到 VisualRoot 或它没有子节点，结构异常。");
                continue;
            }

            for (int i = 0; i < visualRoot.childCount; i++)
            {
                Transform model = visualRoot.GetChild(i);
                if (model == null)
                    continue;

                if (Quaternion.Angle(model.localRotation, target) < 0.01f)
                    continue;

                offCount++;
                Vector3 before = model.localEulerAngles;

                if (!apply)
                {
                    sb.AppendLine($"  {spawner.name}/{model.name}：现在 {before}，应为 {standard}");
                    continue;
                }

                Undo.RecordObject(model, "校正孵化器视觉朝向");
                model.localRotation = target;
                fixedCount++;
                sb.AppendLine($"  {spawner.name}/{model.name}：{before} -> {standard}");
            }
        }

        var header = new StringBuilder();
        header.AppendLine(
            $"{LogPrefix} 共 {spawners.Count} 个孵化器实例，" +
            $"{(apply ? $"校正了 {fixedCount} 个" : $"发现 {offCount} 个")}视觉模型朝向不对。");

        if (noVisual > 0)
            header.AppendLine($"另有 {noVisual} 个实例结构异常（见下），没有处理。");

        if (!apply && offCount > 0)
            header.AppendLine("跑「2. 校正视觉模型朝向」改回标准值。位置和缩放不变。");

        if (offCount == 0 && noVisual == 0)
        {
            Debug.Log($"{LogPrefix} 全部 {spawners.Count} 个孵化器的视觉朝向都已是 {standard}。");
            return;
        }

        Debug.Log(header.ToString() + sb.ToString());

        if (fixedCount == 0)
            return;

        for (int i = 0; i < SceneManager.sceneCount; i++)
            EditorSceneManager.MarkSceneDirty(SceneManager.GetSceneAt(i));

        Debug.Log($"{LogPrefix} 场景已标记为待保存——记得 Ctrl+S。");
    }

    private static List<T> CollectSceneInstances<T>() where T : Component
    {
        var found = new List<T>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded)
                continue;

            foreach (GameObject root in scene.GetRootGameObjects())
                found.AddRange(root.GetComponentsInChildren<T>(true));
        }
        return found;
    }
}
