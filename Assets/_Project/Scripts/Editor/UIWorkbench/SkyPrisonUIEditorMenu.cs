#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SkyPrison.Editor.UI
{
    /// <summary>
    /// Sky Prison UI editor menu.
    ///
    /// V2:
    /// - Removed the dangerous "Create Runtime UI Driver In Scene" menu entry.
    /// - Added a safe cleanup command for legacy / accidentally-created runtime UI scene objects.
    ///
    /// This file only controls Editor menu entries.
    /// It does NOT touch PlayerStatusArea, HUD prefab logic, StyleProfile, HP/LP logic, or runtime UI rendering.
    /// </summary>
    public static class SkyPrisonUIEditorMenu_V2
    {
        private const string LegacyUISystemName = "SkyPrisonUISystem";
        private const string RuntimeUIRootName = "SkyPrisonRuntimeUIRoot";

        /*
         * Removed intentionally.
         *
         * Old dangerous entry:
         * [MenuItem("Tools/Sky Prison/UI/Create Runtime UI Driver In Scene")]
         *
         * Reason:
         * This creates a SkyPrisonUISystem object in the active Scene and attaches
         * SkyPrisonUIRuntimeDriver_V1. The driver may then spawn a runtime HUD instance,
         * causing duplicated Player HUD layers in Scene/Game view.
         *
         * Normal UI editing must use the UI Workbench only.
         */

        [MenuItem("Tools/Sky Prison/UI/Cleanup Legacy Runtime UI Driver In Scene")]
        public static void CleanupLegacyRuntimeUIDriverInScene()
        {
            int removed = 0;

            removed += DestroySceneObjectIfExists(LegacyUISystemName);
            removed += DestroySceneObjectIfExists(RuntimeUIRootName);

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            Debug.Log($"[SkyPrison UI] Cleanup finished. Removed {removed} legacy runtime UI object(s).");
        }

        private static int DestroySceneObjectIfExists(string objectName)
        {
            GameObject go = GameObject.Find(objectName);
            if (go == null)
                return 0;

            Undo.DestroyObjectImmediate(go);
            return 1;
        }
    }
}
#endif