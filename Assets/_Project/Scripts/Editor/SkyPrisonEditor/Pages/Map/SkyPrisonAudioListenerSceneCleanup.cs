#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SkyPrison.Editor.UI
{
    /// <summary>
    /// 一次性场景清理：多张地图场景里同时烘焙了2个AudioListener——一个在Main Camera上
    /// （Unity新建摄像机的默认行为），一个在AudioListenerRoot上（项目自己那套2.5D
    /// 音频锚点系统真正在用的）。运行时靠脚本反复禁用/销毁多余的那个，结果引擎自己
    /// 那条"There are N audio listeners"警告显示的数字完全跟实际组件数(一直是2)对不上、
    /// 持续暴涨——怀疑是运行时反复启禁/销毁同一批监听器触发了引擎内部计数的bug。
    /// 与其运行时来回折腾，不如直接从场景文件源头把多余的那个删掉，让每张场景从
    /// 打开的那一刻起就只有1个AudioListener，运行时脚本完全不需要再做任何托管。
    /// </summary>
    public static class SkyPrisonAudioListenerSceneCleanup
    {
        [MenuItem("Tools/Sky Prison/Audio/清理所有场景多余AudioListener")]
        public static void CleanupAllScenes()
        {
            string[] sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/_Project" });
            int scenesChanged = 0;
            int listenersRemoved = 0;

            string activeScenePath = SceneManager.GetActiveScene().path;

            foreach (string guid in sceneGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

                AudioListener[] listeners = Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
                if (listeners.Length <= 1)
                    continue;

                AudioListener keep = null;
                foreach (var l in listeners)
                {
                    if (l.GetComponent<SkyPrisonPlayerAudioListenerAnchor>() != null || l.gameObject.name == "AudioListenerRoot")
                    {
                        keep = l;
                        break;
                    }
                }
                if (keep == null) keep = listeners[0];

                int removedThisScene = 0;
                foreach (var l in listeners)
                {
                    if (l == keep) continue;
                    Undo.DestroyObjectImmediate(l);
                    removedThisScene++;
                }

                if (removedThisScene > 0)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    scenesChanged++;
                    listenersRemoved += removedThisScene;
                    Debug.Log($"[SkyPrisonAudioListenerSceneCleanup] {path}: 删除了{removedThisScene}个多余AudioListener，保留 {GetHierarchyPath(keep.transform)}");
                }
            }

            if (!string.IsNullOrEmpty(activeScenePath))
                EditorSceneManager.OpenScene(activeScenePath, OpenSceneMode.Single);

            Debug.Log($"[SkyPrisonAudioListenerSceneCleanup] 完成：{scenesChanged}个场景被修改，共删除{listenersRemoved}个多余AudioListener。");
        }

        private static string GetHierarchyPath(Transform t)
        {
            string path = t.name;
            Transform current = t.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            return path;
        }
    }
}
#endif
