using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 一键创建游戏场景并配置 Build Settings。
/// 菜单：SkyPrison → Scene Setup → ...
/// </summary>
public static class SceneSetupTool
{
    private const string SceneRoot   = "Assets/_Project/Scenes";
    private const string MainMenuScene = "MainMenu";
    private const string HubScene     = "Hub_Base";

    // ── 菜单入口 ────────────────────────────────────────────────────────

    [MenuItem("SkyPrison/Scene Setup/① 创建 MainMenu 场景 + 配置 Build Settings")]
    public static void CreateMainMenuScene()
    {
        EnsureSceneFolder();

        string path = $"{SceneRoot}/{MainMenuScene}.unity";

        // 已存在则只更新 Build Settings
        if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(path))
        {
            // 新建空场景并保存
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            // 创建 MainMenuController GameObject
            var go = new GameObject("MainMenuController");
            go.AddComponent<MainMenuController>();
            SceneManager.MoveGameObjectToScene(go, scene);

            // 添加基础定向光
            var lightGo = new GameObject("Directional Light");
            var light   = lightGo.AddComponent<Light>();
            light.type      = LightType.Directional;
            light.intensity = 1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            SceneManager.MoveGameObjectToScene(lightGo, scene);

            EditorSceneManager.SaveScene(scene, path);
            EditorSceneManager.CloseScene(scene, true);

            AssetDatabase.Refresh();
            Debug.Log($"[SceneSetup] 已创建场景：{path}");
        }
        else
        {
            Debug.Log($"[SceneSetup] 场景已存在，跳过创建：{path}");
        }

        RegisterInBuildSettings(path, 0);
    }

    [MenuItem("SkyPrison/Scene Setup/② 创建 Hub_Base 场景占位")]
    public static void CreateHubScene()
    {
        EnsureSceneFolder();

        string path = $"{SceneRoot}/{HubScene}.unity";

        if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(path))
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Additive);
            EditorSceneManager.SaveScene(scene, path);
            EditorSceneManager.CloseScene(scene, true);
            AssetDatabase.Refresh();
            Debug.Log($"[SceneSetup] 已创建占位场景：{path}");
        }
        else
        {
            Debug.Log($"[SceneSetup] 场景已存在：{path}");
        }

        RegisterInBuildSettings(path, 1);
    }

    [MenuItem("SkyPrison/Scene Setup/一键全部创建")]
    public static void CreateAll()
    {
        CreateMainMenuScene();
        CreateHubScene();
        Debug.Log("[SceneSetup] 全部场景创建完毕，MainMenu 在 Build Index 0。");
    }

    // ── 内部工具 ─────────────────────────────────────────────────────────

    private static void EnsureSceneFolder()
    {
        if (!AssetDatabase.IsValidFolder(SceneRoot))
        {
            // Assets/_Project 必须已存在
            AssetDatabase.CreateFolder("Assets/_Project", "Scenes");
            AssetDatabase.Refresh();
        }
    }

    /// <summary>
    /// 将场景注册到 Build Settings，insertAt 指定目标索引位置。
    /// 若已存在则移动到目标位置，不重复添加。
    /// </summary>
    private static void RegisterInBuildSettings(string scenePath, int insertAt)
    {
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(
            EditorBuildSettings.scenes);

        // 检查是否已存在
        int existingIndex = scenes.FindIndex(s => s.path == scenePath);
        if (existingIndex >= 0)
        {
            if (existingIndex == insertAt)
            {
                Debug.Log($"[SceneSetup] Build Settings 已是目标位置 [{insertAt}]：{scenePath}");
                return;
            }
            scenes.RemoveAt(existingIndex);
        }

        insertAt = Mathf.Clamp(insertAt, 0, scenes.Count);
        scenes.Insert(insertAt, new EditorBuildSettingsScene(scenePath, true));

        EditorBuildSettings.scenes = scenes.ToArray();
        Debug.Log($"[SceneSetup] Build Settings 已更新，[{insertAt}] = {scenePath}");
    }
}
