using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 2026-08-19：碎块之前放在 World3D 层能看见，是因为 Main Camera 的 CullingMask
/// 只勾了 World3D(层7)+Character2D(层8)（m_Bits=384）。改用新开的 Debris 层
/// （TagManager 里之前空着的槽位，层6）之后，这个层从来没被任何相机的 CullingMask
/// 勾选过——碎块物理上照样在生成、落地，只是没有任何相机画它，表现为"直接消失"。
///
/// 直接改磁盘上的 .unity 文件不安全：Unity 当前把这个场景加载在内存里，之后一保存
/// 就会用内存版本覆盖磁盘上手改的这一行。改成在运行中的编辑器里，对已加载场景的
/// Camera 组件直接改 cullingMask 并 MarkSceneDirty，跟"修复战争迷雾材质引用"那个
/// 工具用的是同一套安全改法。
/// </summary>
public static class SkyPrisonDebrisLayerCameraFix
{
    [MenuItem("Tools/Sky Prison/Debug/把 Debris 层加进 Main Camera 的 CullingMask")]
    public static void Fix()
    {
        int debrisLayer = LayerMask.NameToLayer("Debris");
        if (debrisLayer < 0)
        {
            Debug.LogError("[Debris层修复] 找不到 Debris 层，先确认 TagManager 里已经加了这一层。");
            return;
        }

        Camera mainCamera = null;
        foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (cam.CompareTag("MainCamera"))
            {
                mainCamera = cam;
                break;
            }
        }

        if (mainCamera == null)
        {
            Debug.LogError("[Debris层修复] 当前打开的场景里找不到 Tag=MainCamera 的相机。");
            return;
        }

        int bit = 1 << debrisLayer;
        if ((mainCamera.cullingMask & bit) != 0)
        {
            Debug.Log($"[Debris层修复] {mainCamera.name} 的 CullingMask 已经包含 Debris 层，不用改。");
            return;
        }

        mainCamera.cullingMask |= bit;
        EditorUtility.SetDirty(mainCamera);
        EditorSceneManager.MarkSceneDirty(mainCamera.gameObject.scene);

        Debug.Log($"[Debris层修复] 已把 Debris 层加进 {mainCamera.name} 的 CullingMask（{mainCamera.cullingMask}）。" +
                  "记得 Ctrl+S 保存场景，不保存的话下次重开场景又会丢。");
    }
}
