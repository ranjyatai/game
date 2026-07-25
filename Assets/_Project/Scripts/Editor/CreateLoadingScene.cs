using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 工具菜单：Tools → Sky Prison → 创建 LoadingScene
/// 自动生成 LoadingScene.unity 并加入 Build Settings。
/// </summary>
public static class CreateLoadingScene
{
    private const string ScenePath = "Assets/_Project/Scenes/LoadingScene.unity";
    private const string SceneName = "LoadingScene";

    [MenuItem("Tools/Sky Prison/创建 LoadingScene")]
    public static void Create()
    {
        // 如果已存在就跳过创建，只确保 Build Settings 里有它
        if (!System.IO.File.Exists(ScenePath))
        {
            // 新建空场景（不保存到当前打开场景）
            var newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            // Controller（运行时会自动创建相机和 UI）
            var go = new GameObject("[LoadingSceneController]");
            SceneManager.MoveGameObjectToScene(go, newScene);
            go.AddComponent<LoadingSceneController>();

            // 编辑器预览用相机（运行时 LoadingSceneController.Awake 会再建一个，无害）
            var camGo = new GameObject("[LoadingCamera_EditorPreview]");
            SceneManager.MoveGameObjectToScene(camGo, newScene);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags      = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask     = 0;

            // 保存
            EditorSceneManager.SaveScene(newScene, ScenePath);
            EditorSceneManager.CloseScene(newScene, true);

            Debug.Log($"[CreateLoadingScene] 已创建：{ScenePath}");
        }
        else
        {
            Debug.Log($"[CreateLoadingScene] 已存在，跳过创建：{ScenePath}");
        }

        AddToBuildSettings();
    }

    private static void AddToBuildSettings()
    {
        var scenes = EditorBuildSettings.scenes;

        // 检查是否已在列表里
        foreach (var s in scenes)
        {
            if (s.path == ScenePath)
            {
                Debug.Log("[CreateLoadingScene] Build Settings 已包含 LoadingScene，无需重复添加。");
                return;
            }
        }

        // 追加到末尾（顺序不影响运行，SceneLoader 按名字加载）
        var newList = new EditorBuildSettingsScene[scenes.Length + 1];
        System.Array.Copy(scenes, newList, scenes.Length);
        newList[scenes.Length] = new EditorBuildSettingsScene(ScenePath, true);
        EditorBuildSettings.scenes = newList;

        Debug.Log("[CreateLoadingScene] 已将 LoadingScene 加入 Build Settings。");
    }
}
