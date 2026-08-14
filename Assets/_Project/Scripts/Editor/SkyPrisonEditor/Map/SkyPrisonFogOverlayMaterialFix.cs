using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 修复 FogOverlay 渲染器材质槽指向错误资产的问题。
///
/// 实测（Still Vault 场景）：FogOverlay 的 MeshRenderer.m_Materials[0] 静态保存的是
/// VLBDummyMaterialHD.mat（体积光束插件的占位材质，guid e41f1a0...），跟战争迷雾
/// 毫无关系。不是运行时代码改的——场景文件本身存的就是这个引用，大概率是之前在
/// Inspector 里操作时选错了对象保存下来的。
///
/// 直接改场景文件文本有风险：如果 Unity 当前正打开着这个场景，保存时会用内存里的
/// （还是错的）版本把文本层面的修改覆盖掉。这里改用 EditorSceneManager 操作当前
/// 已加载的场景对象，保证走 Unity 正常的序列化路径生效并存盘。
///
/// 按物体名 "FogOverlay" 查找，不写死具体场景，方便其它地图如果也中招可以直接复用。
/// </summary>
public static class SkyPrisonFogOverlayMaterialFix
{
    private const string LogPrefix = "[SkyPrison 战争迷雾]";
    private const string CorrectMaterialPath =
        "Assets/_Project/Art/Materials/FrontOccluder/FogOfWar/SkyPrisonFogOfWarOverlay.mat";

    [MenuItem("Tools/Sky Prison/Map/修复战争迷雾材质引用")]
    public static void Run()
    {
        Material correctMaterial = AssetDatabase.LoadAssetAtPath<Material>(CorrectMaterialPath);
        if (correctMaterial == null)
        {
            Debug.LogError($"{LogPrefix} 找不到正确材质 {CorrectMaterialPath}");
            return;
        }

        int fixedCount = 0;
        int checkedCount = 0;

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded)
                continue;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name != "FogOverlay")
                        continue;

                    var renderer = t.GetComponent<MeshRenderer>();
                    if (renderer == null)
                        continue;

                    checkedCount++;

                    if (renderer.sharedMaterial == correctMaterial)
                        continue;

                    string oldName = renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "NULL";
                    Undo.RecordObject(renderer, "Fix Fog Overlay Material");
                    renderer.sharedMaterial = correctMaterial;
                    EditorUtility.SetDirty(renderer);
                    fixedCount++;

                    Debug.Log($"{LogPrefix} 场景「{scene.name}」的 FogOverlay 材质从 " +
                              $"「{oldName}」修正为「{correctMaterial.name}」");
                }
            }

            if (fixedCount > 0)
                EditorSceneManager.MarkSceneDirty(scene);
        }

        if (checkedCount == 0)
        {
            Debug.LogWarning($"{LogPrefix} 当前没有已加载的场景包含名为 FogOverlay 的物体，" +
                              "请先打开对应地图场景再运行这个菜单。");
            return;
        }

        Debug.Log($"{LogPrefix} 检查了 {checkedCount} 个 FogOverlay，修正了 {fixedCount} 个。" +
                  (fixedCount > 0 ? " 记得 Ctrl+S 保存场景。" : " 全部正确，无需改动。"));
    }
}
